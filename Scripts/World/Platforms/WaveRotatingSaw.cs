using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 가운데 기어 축을 중심으로 도는 팔의 양 끝 톱날이 닿은 플레이어에게 피해를 준다.
/// 팔과 톱날의 회전은 WaveMachineMotion이 맡고, 여기서는 그 자세를 읽어 판정과 효과만 처리한다.
/// 팔 길이는 배치마다 바꿀 수 있으며, 길이에 맞춰 팔 몸체와 톱날 위치, 회전 속도를 맞춘다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // 캐릭터가 이번 틱의 이동을 마친 뒤에 판정한다.
public sealed class WaveRotatingSaw : WaveEnvironmentDevice
{
    [Header("톱날")]
    [Tooltip("팔 끝의 톱날 중심들.")]
    [SerializeField] private Transform[] blades = Array.Empty<Transform>();
    [SerializeField, Min(.01f)] private float bladeRadius = .42f;
    [SerializeField, Min(0f)] private float damage = 10f;
    [SerializeField] private LayerMask targetMask;

    [Header("팔 길이 (등장·퇴장 Clip이 다루지 않는 시각 부품)")]
    [Tooltip("기본 팔 길이. 축에서 톱날 중심까지의 칸 수다. 배치마다 Wave 격자 편집기에서 바꿀 수 있다.")]
    [SerializeField, Min(.5f)] private float armLength = 3f;
    [Tooltip("팔 끝에서 톱날을 받치는 노드. 로컬 X를 팔 길이로 옮긴다.")]
    [SerializeField] private Transform[] bladeMounts = Array.Empty<Transform>();
    [Tooltip("길이 1로 만든 팔 몸체. 로컬 X 크기를 (팔 길이 - 시작점)으로 늘인다.")]
    [SerializeField] private Transform[] armBeams = Array.Empty<Transform>();
    [SerializeField] private float armStart = .1f;
    [Tooltip("길이 1로 만든 팔 보강선. 축 쪽 시작점부터 톱날 안쪽 가장자리 앞까지 늘인다.")]
    [SerializeField] private Transform[] armTrusses = Array.Empty<Transform>();
    [SerializeField] private float trussStart = .28f;
    [SerializeField] private float trussInset = .49f;
    [Tooltip("가장 짧게 줄일 수 있는 팔 길이(칸). 톱날이 축 기어박스를 덮지 않는 길이다.")]
    [SerializeField, Min(.5f)] private float minArmLength = 1f;
    [Tooltip("톱날 중심이 도는 속도(칸/초). 길이가 달라도 같은 빠르기로 지나가도록 팔 회전 속도를 맞춘다.")]
    [SerializeField, Min(.1f)] private float tipSpeed = 6.3f;
    [Tooltip("기본 회전 속도 배율. 톱날 중심 속도(tipSpeed)에 곱한다. 배치마다 Wave 격자 편집기에서 바꿀 수 있다.")]
    [SerializeField, Min(.1f)] private float rotationSpeed = 1f;
    [Tooltip("WaveMachineMotion에서 팔 전체를 돌리는 항목과, 그와 맞물린 구동 기어 항목의 번호.")]
    [SerializeField] private int rotorGear = 2;
    [SerializeField] private int[] driveGears = { 0, 1 };
    [Tooltip("가운데 기어와 구동 기어의 반지름 비. 구동 기어는 반대 방향으로 이 비율만큼 빠르게 돈다.")]
    [SerializeField, Min(.01f)] private float driveRatio = 2.35f;

    [Header("효과")]
    [SerializeField] private FeedbackDataSO hitFeedback;
    [Tooltip("톱날에서 계속 튀는 불꽃. 작동하는 동안만 켠다.")]
    [SerializeField] private ParticleSystem[] sparks = Array.Empty<ParticleSystem>();
    [SerializeField] private SoundOptions loopSound = new SoundOptions();
    [Tooltip("회전음이 켜지고 꺼질 때 음량이 따라가는 시간(초).")]
    [SerializeField, Min(0f)] private float loopFadeTime = .25f;

    public int BladeCount => blades.Length;
    public float BladeRadius => bladeRadius;
    /// <summary>이 개체에 적용된 팔 길이(칸).</summary>
    public float ArmLength => currentLength;
    /// <summary>이 개체에 적용된 회전 속도 배율.</summary>
    public float RotationSpeed => currentSpin;
    /// <summary>지금 팔이 도는 각속도(도/초).</summary>
    public float ArmSpin => tipSpeed * currentSpin / Mathf.Max(.1f, currentLength) * Mathf.Rad2Deg;

    private float currentLength = 3f, currentSpin = 1f;
    private WaveDeviceParameter LengthParameter => new WaveDeviceParameter("length", "팔 길이",
        "가운데 축에서 톱날 중심까지의 칸 수입니다. 톱날 중심이 지나가는 빠르기는 길이와 관계없이 같습니다.", "칸", minArmLength, 6f, .5f, armLength, true);
    private WaveDeviceParameter SpinParameter => new WaveDeviceParameter("spin", "회전 속도",
        "1이면 기본입니다. 2면 팔이 두 배 빠르게 돕니다. 길이와 관계없이 톱날 중심이 지나가는 빠르기에 곱합니다.", "배", .2f, 3f, .1f, rotationSpeed);
    public override WaveDeviceParameter[] Parameters => new[] { LengthParameter, SpinParameter };
    public override bool SettingsChangeShape => true;

    public override void ApplySettings(WaveDeviceSetting[] overrides)
    {
        currentLength = WaveDeviceSettings.Value(overrides, LengthParameter);
        currentSpin = Mathf.Max(.1f, WaveDeviceSettings.Value(overrides, SpinParameter));
        ApplyShape();
    }

    public override bool TryGetRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        float reach = WaveDeviceSettings.Value(overrides, LengthParameter) + bladeRadius;
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Circle, Vector2.zero, new Vector2(reach, reach), new Color(1f, .22f, .16f, .9f));
        return true;
    }

    // 팔 몸체·톱날 위치와 회전 속도를 현재 길이에 맞춘다. 저장된 기준 자세를 되돌리는 등장 완료 뒤에도 다시 부른다.
    private void ApplyShape()
    {
        foreach (var mount in bladeMounts)
            if (mount != null) mount.localPosition = new Vector3(currentLength, 0f, mount.localPosition.z);
        foreach (var beam in armBeams)
            if (beam != null)
            {
                var scale = beam.localScale;
                scale.x = Mathf.Max(.01f, currentLength - armStart);
                beam.localScale = scale;
            }
        foreach (var truss in armTrusses)
            if (truss != null)
            {
                var scale = truss.localScale;
                scale.x = Mathf.Max(.01f, currentLength - trussInset - trussStart);
                truss.localScale = scale;
            }
        var motion = GetComponent<WaveMachineMotion>();
        if (motion == null || motion.gearSpeeds == null) return;
        float spin = ArmSpin;
        if (rotorGear >= 0 && rotorGear < motion.gearSpeeds.Length) motion.gearSpeeds[rotorGear] = spin;
        foreach (int gear in driveGears)
            if (gear >= 0 && gear < motion.gearSpeeds.Length) motion.gearSpeeds[gear] = -spin * driveRatio;
    }

    private readonly List<Collider2D> hits = new();
    private readonly HashSet<PlayerHealth> damagedPlayers = new();
    private ContactFilter2D filter;
    private bool spinning;

    // 여러 톱날이 같은 소리를 겹쳐 내지 않도록, 켜져 있는 장치 전체가 반복음 하나를 함께 쓴다.
    private static readonly List<WaveRotatingSaw> active = new();
    private static SoundHandle loopHandle;
    private static float loopBaseVolume;
    private static float loopGain;
    private static float nextLoopRetryTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetShared()
    {
        // 도메인 리로드를 끈 Play에서도 지난 실행의 목록과 핸들이 남지 않게 한다.
        active.Clear();
        loopHandle = null;
        loopBaseVolume = loopGain = nextLoopRetryTime = 0f;
    }

    protected override void Awake()
    {
        base.Awake();
        if (targetMask.value == 0) targetMask = LayerMask.GetMask("Player");
        filter = new ContactFilter2D();
        filter.SetLayerMask(targetMask);
        filter.useTriggers = true; // 플레이어의 피해 콜라이더는 Trigger다.
    }

    protected override void ResetDevice()
    {
        spinning = false;
        StopSparks();
    }

    protected override void OnDeviceReady()
    {
        // 등장 완료 때 기준 자세(기본 길이)로 되돌아간 팔을 이 배치의 길이로 다시 맞춘다.
        ApplyShape();
        spinning = true;
        foreach (var system in sparks)
            if (system != null) system.Play(true);
    }

    protected override void OnDeviceLeaving()
    {
        // 접히는 동안에는 소리 목록에 남아 회전음이 뚝 끊기지 않고 잦아든다.
        spinning = false;
        StopSparks();
    }

    private void OnEnable()
    {
        if (Application.isPlaying && !active.Contains(this)) active.Add(this);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        active.Remove(this);
        if (active.Count == 0) StopLoop();
    }

    private void StopSparks()
    {
        foreach (var system in sparks)
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void FixedUpdate()
    {
        if (!IsOperating || damage <= 0f) return;
        damagedPlayers.Clear();
        float radius = bladeRadius * Mathf.Abs(transform.lossyScale.x);
        var info = new WeaponBaseSO.DamageInfo { damage = damage, damageType = DamageType.Normal };
        foreach (var blade in blades)
        {
            if (blade == null) continue;
            Vector2 center = blade.position;
            // 매 틱 판정해 빠르게 도는 톱날이 대상을 건너뛰지 않게 한다. 다시 맞는 간격은 피격 무적이 정한다.
            int count = motor.OverlapAttackCircle(center, radius, filter, hits);
            for (int i = 0; i < count; i++)
            {
                if (hits[i] == null) continue;
                var player = hits[i].GetComponentInParent<PlayerHealth>();
                if (player == null || !damagedPlayers.Add(player)) continue;
                Vector2 point = hits[i].ClosestPoint(center);
                // 반격·무적으로 막힌 접촉에는 갈리는 효과를 내지 않는다.
                if (TryDamage(player, info, point))
                    PlayFeedback(hitFeedback, point, Quaternion.FromToRotation(Vector3.up, (Vector3)(point - center)));
            }
        }
    }

    private void Update()
    {
        // 목록의 첫 장치가 공유 반복음을 대표로 갱신한다.
        if (active.Count > 0 && active[0] == this) UpdateLoop(loopSound, loopFadeTime);
    }

    private static void UpdateLoop(SoundOptions sound, float fadeTime)
    {
        float target = 0f;
        Vector2 position = default;
        foreach (var saw in active)
        {
            if (saw == null || !saw.spinning) continue;
            foreach (var blade in saw.blades)
            {
                if (blade == null) continue;
                float gain = saw.DistanceGain(blade.position);
                if (gain <= target) continue;
                // 카메라에 가장 가까운 톱날 하나가 음량과 위치를 정한다.
                target = gain;
                position = blade.position;
            }
        }
        float step = fadeTime > 0f ? Time.unscaledDeltaTime / fadeTime : 1f;
        loopGain = Mathf.MoveTowards(loopGain, target, step);
        if (loopGain <= 0f) { StopLoop(); return; }

        if (loopHandle == null || !loopHandle.IsValid)
        {
            if (sound == null || sound.clip == null || SoundManager.Instance == null) return;
            if (Time.unscaledTime < nextLoopRetryTime) return;
            nextLoopRetryTime = Time.unscaledTime + .12f;
            loopHandle = SoundManager.Instance.PlayLoop(sound, position, null);
            if (loopHandle == null || !loopHandle.IsValid) return;
            loopBaseVolume = loopHandle.GetSource().volume;
        }
        if (target > 0f) loopHandle.SetPosition(position);
        loopHandle.GetSource().volume = loopBaseVolume * loopGain;
    }

    private static void StopLoop()
    {
        if (loopHandle != null && loopHandle.IsValid) loopHandle.Stop();
        loopHandle = null;
        loopGain = 0f;
    }
}
