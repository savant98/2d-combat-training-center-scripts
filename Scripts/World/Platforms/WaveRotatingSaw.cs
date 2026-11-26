using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 가운데 기어 축을 중심으로 도는 팔의 양 끝 톱날이 닿은 플레이어에게 피해를 준다.
/// 팔과 톱날의 회전은 WaveMachineMotion이 맡고, 여기서는 그 자세를 읽어 판정과 효과만 처리한다.
/// 팔 길이는 배치마다 바꿀 수 있으며, 길이에 맞춰 팔 몸체와 톱날 위치, 회전 속도를 맞춘다.
/// 시작 각도(sawStartAngle, 위상)도 배치마다 바꿀 수 있다. 기본 0이면 지금과 같고, 0이 아니면 팔과 맞물린 구동 기어를
/// WaveMachineMotion과 같은 식(기준 자세 × 회전)에 그 각도를 더해 다시 놓는다. 묶음으로 둔 톱날을 엇갈려 돌게(파도) 할 때 쓴다.
/// 6차 T9(던전 방 클리어 정지, IDungeonRoomHazard): 정지 신호를 받은 호출에서 피해 판정을 끄고, 정전 물결 차례가 오면 불꽃·회전음을 거두며
/// 팔을 감속해 설치면과 평행한 자세(팔 회전 0·180도 — 공격 원이 덜 읽힘)에 세운 뒤 톱날의 빨강(Damage 재질 Renderer)을 끈다.
/// 감속은 WaveMachineMotion의 시간을 직접 정해(Seek) 팔·구동 기어·톱날 원판이 같은 식으로 함께 선다. 조립 등장이 끝나기 전에 온 신호는
/// 기억했다가 등장 완료 때 바로 정지 자세로 들어간다. 신호가 없으면(던전 밖 일반 Wave) 늘 작동한다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // 캐릭터가 이번 틱의 이동을 마친 뒤에 판정한다.
public sealed class WaveRotatingSaw : WaveEnvironmentDevice, IDungeonRoomHazard
{
    [Header("톱날")]
    [Tooltip("팔 끝의 톱날 중심들.")]
    [SerializeField] private Transform[] blades = Array.Empty<Transform>();
    [SerializeField, Min(.01f)] private float bladeRadius = .42f;
    [SerializeField, Min(0f)] private float damage = 10f;
    private readonly WaveTrapDamage trapDamage = new WaveTrapDamage();
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
    public const string StartAngleKey = "sawStartAngle";
    // 편집기 미리보기 그림은 바꾸지 않는다(ChangesShape = false). 차지하는 칸과 공격 범위(원)는 각도와 관계없다.
    private static WaveDeviceParameter StartAngleParameter => new WaveDeviceParameter(StartAngleKey, "시작 각도",
        "팔이 이 각도(도, 반시계 방향)만큼 돌아간 자세에서 돌기 시작합니다. 같은 줄의 톱날마다 일정하게 늘리면 파도처럼 엇갈려 돕니다. 0이면 기본 자세입니다.",
        "도", 0f, 360f, 1f, 0f);
    public override WaveDeviceParameter[] Parameters => new[] { LengthParameter, SpinParameter, StartAngleParameter, WaveTrapDamage.DamageParameter(damage), WaveTrapDamage.ReturnParameter };
    public override bool SettingsChangeShape => true;

    /// <summary>방 클리어 정지 신호를 받아 판정이 꺼져 있다(6차 T9, 검사용).</summary>
    public bool IsHalted => halt.Halted;
    /// <summary>정지 자세(팔 평행·빨강 끔·기어 정지)에 다 들어갔다(검사용).</summary>
    public bool InHaltedPose => halt.Halted && haltSettled;
    /// <summary>톱날의 빨강이 켜져 있는지(검사용).</summary>
    public bool DangerLit => red == null || red.IsOn;
    /// <summary>지금 팔 회전각(도, 기준 자세 기준·시작 각도 포함). 0·180이면 설치면과 평행하다(검사용).</summary>
    public float ArmAngle => machine != null && machine.gearSpeeds != null && rotorGear >= 0 && rotorGear < machine.gearSpeeds.Length
        ? Mathf.Repeat(machine.MotionTime * machine.gearSpeeds[rotorGear] + currentStartAngle, 360f) : 0f;

    // 6차 T9 정지 감속: 정지 판단값. 감속 시간은 남은 각도에 따라 HaltMinTime~HaltMaxTime(초).
    private const float HaltMinTime = .3f, HaltMaxTime = .7f;
    private readonly HazardHalt halt = new HazardHalt();
    private HazardRendererSwitch red;
    private WaveMachineMotion machine;
    private bool haltSettled, decelerating;
    private float decelFrom, decelDistance, decelTime, decelElapsed;

    /// <summary>이 개체에 적용된 시작 각도(도).</summary>
    public float StartAngle => currentStartAngle;

    public override void ApplySettings(WaveDeviceSetting[] overrides)
    {
        trapDamage.ApplySettings(overrides, damage);
        currentLength = WaveDeviceSettings.Value(overrides, LengthParameter);
        currentSpin = Mathf.Max(.1f, WaveDeviceSettings.Value(overrides, SpinParameter));
        currentStartAngle = WaveDeviceSettings.Value(overrides, StartAngleParameter);
        ApplyShape();
        PosePhase();
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

    // ── 시작 각도(위상) ─────────────────────────────
    // WaveMachineMotion은 매 프레임 기준 자세 × (경과 시간 × 속도)로 기어를 놓는다. 이 컴포넌트는 실행 순서가 뒤(100)라
    // 같은 프레임에 같은 식에 시작 각도만 더해 팔(rotorGear)과 구동 기어(driveGears, 반대 방향 × driveRatio)를 다시 놓는다.
    // 등장 중에도 같은 자세를 유지해 작동 시작 순간 팔이 튀지 않는다. 퇴장 중에는 WaveEnvironmentLifecycle이 잡은 자세를 그대로 둔다.
    // 시작 각도가 0이고 이전에 바꾼 적이 없으면 아무것도 하지 않는다(기존과 같다). Edit Mode(편집기 미리보기)에서는 쓰지 않는다.
    private float currentStartAngle;
    private bool phaseApplied;
    private bool phaseCached;
    private WaveMachineMotion phaseMotion;
    private Transform[] phaseGears = Array.Empty<Transform>();
    private int[] phaseIndices = Array.Empty<int>();
    private float[] phaseFactors = Array.Empty<float>();
    private Quaternion[] phaseRest = Array.Empty<Quaternion>();

    // 기준 자세는 처음 켜질 때(Awake, WaveMachineMotion이 기준을 잡는 때와 같은 자세)에 한 번 읽는다.
    private void CachePhase()
    {
        if (phaseCached || !Application.isPlaying) return;
        phaseCached = true;
        phaseMotion = GetComponent<WaveMachineMotion>();
        if (phaseMotion == null || phaseMotion.gears == null) return;
        var gears = new List<Transform>();
        var indices = new List<int>();
        var factors = new List<float>();
        var rests = new List<Quaternion>();
        void Add(int index, float factor)
        {
            if (index < 0 || index >= phaseMotion.gears.Length || indices.Contains(index)) return;
            var gear = phaseMotion.gears[index];
            // WaveMachineMotion이 돌리지 않는 부품(루트·물리 부품)은 건드리지 않는다.
            if (gear == null || gear == transform || !gear.IsChildOf(transform) || gear.GetComponentInChildren<Collider2D>(true) != null) return;
            gears.Add(gear);
            indices.Add(index);
            factors.Add(factor);
            rests.Add(gear.localRotation);
        }
        Add(rotorGear, 1f);
        foreach (int gear in driveGears) Add(gear, -driveRatio);
        phaseGears = gears.ToArray();
        phaseIndices = indices.ToArray();
        phaseFactors = factors.ToArray();
        phaseRest = rests.ToArray();
    }

    private void PosePhase()
    {
        if (!Application.isPlaying) return;
        bool wanted = !Mathf.Approximately(Mathf.Repeat(currentStartAngle, 360f), 0f);
        if (!wanted && !phaseApplied) return;
        CachePhase();
        if (phaseMotion == null || phaseGears.Length == 0) return;
        if (lifecycle != null && lifecycle.IsDespawning) return;
        bool running = lifecycle == null || lifecycle.IsReady;
        float time = running ? phaseMotion.MotionTime : 0f;
        for (int i = 0; i < phaseGears.Length; i++)
        {
            var gear = phaseGears[i];
            if (gear == null) continue;
            int index = phaseIndices[i];
            float speed = phaseMotion.gearSpeeds != null && index < phaseMotion.gearSpeeds.Length ? phaseMotion.gearSpeeds[index] : 0f;
            float offset = wanted ? currentStartAngle * phaseFactors[i] : 0f;
            gear.localRotation = phaseRest[i] * Quaternion.Euler(0f, 0f, Mathf.Repeat(time * speed + offset, 360f));
        }
        phaseApplied = wanted;
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
        machine = GetComponent<WaveMachineMotion>();
        red = HazardRendererSwitch.ByMaterial(lifecycle != null && lifecycle.visualRoot != null ? lifecycle.visualRoot : transform, "Damage");
        CachePhase();
        ApplySettings(WaveDeviceSettings.None);
        if (targetMask.value == 0) targetMask = LayerMask.GetMask("Player");
        filter = new ContactFilter2D();
        filter.SetLayerMask(targetMask);
        filter.useTriggers = true; // 플레이어의 피해 콜라이더는 Trigger다.
    }

    protected override void ResetDevice()
    {
        spinning = false;
        StopSparks();
        // 풀 재사용·꺼짐: 정지 신호를 잊고(던전 밖 = 작동) 빨강·기어를 처음 상태로 둔다.
        halt.Reset();
        haltSettled = decelerating = false;
        red?.Restore();
        if (machine != null) machine.Frozen = false;
    }

    protected override void OnDeviceReady()
    {
        // 등장 완료 때 기준 자세(기본 길이)로 되돌아간 팔을 이 배치의 길이와 시작 각도로 다시 맞춘다.
        ApplyShape();
        PosePhase();
        // 조립 등장 중에 받은 정지 신호(방이 이미 클리어됨): 등장이 끝나는 지금 바로 정지 자세로(돌기 시작하지 않는다).
        if (halt.Halted) { EnterHaltedPose(); return; }
        spinning = true;
        foreach (var system in sparks)
            if (system != null) system.Play(true);
    }

    // ── 6차 T9: 방 클리어 정지 ─────────────────────────────

    public void SetHazardHalted(bool halted, bool instant)
    {
        bool changed = halt.Signal(halted, instant);
        if (halted)
        {
            trapDamage.Cancel();   // 판정은 이 호출에서 바로 끈다(FixedUpdate가 묻지 않는다)
            if (instant && IsOperating) EnterHaltedPose();
            return;
        }
        if (!changed) return;
        // 다시 작동(재도전 등): 빨강·기어·불꽃·회전음을 되돌린다.
        haltSettled = decelerating = false;
        red?.Restore();
        if (machine != null) machine.Frozen = false;
        if (!IsOperating) return;
        spinning = true;
        foreach (var system in sparks)
            if (system != null) system.Play(true);
    }

    // 바로 정지 자세: 가장 가까운 평행 자세로 기어 시간을 옮기고 세운다. 불꽃·회전음·빨강을 끈다.
    private void EnterHaltedPose()
    {
        spinning = false;
        decelerating = false;
        StopSparks();
        if (machine != null)
        {
            machine.Frozen = true;
            if (TryParallelTarget(out float distance)) machine.Seek(machine.MotionTime + distance);
        }
        PosePhase();
        red?.Set(false);
        haltSettled = true;
    }

    // 팔 회전각이 설치면과 평행(0·180도)이 되는 가장 가까운 기어 시간까지의 차이(MotionTime 단위, 음수면 조금 되돌아감).
    private bool TryParallelTarget(out float distance)
    {
        distance = 0f;
        if (machine == null || machine.gearSpeeds == null || rotorGear < 0 || rotorGear >= machine.gearSpeeds.Length) return false;
        float speed = machine.gearSpeeds[rotorGear];
        if (Mathf.Abs(speed) < .01f) return false;
        float angle = machine.MotionTime * speed + currentStartAngle;
        // 도는 방향 앞쪽 평행 자세까지의 각도(0~180). 반 바퀴의 2/3보다 멀면 뒤쪽(조금 되돌아감)이 가깝다.
        float ahead = speed > 0f ? 180f - Mathf.Repeat(angle, 180f) : Mathf.Repeat(angle, 180f);
        if (ahead >= 180f - .001f) ahead = 0f;
        float degrees = ahead <= 120f ? ahead : ahead - 180f;
        distance = degrees / Mathf.Abs(speed);
        return true;
    }

    // 정전 물결 차례: 불꽃·회전음을 거두고 기어를 지금 빠르기에서 평행 자세까지 감속한다(Hermite — 처음 빠르기 = 지금, 끝 빠르기 = 0).
    private void BeginDecel()
    {
        spinning = false;
        foreach (var system in sparks)
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (machine == null || !TryParallelTarget(out float distance))
        {
            if (machine != null) machine.Frozen = true;
            red?.Set(false);
            haltSettled = true;
            return;
        }
        machine.Frozen = true;
        decelerating = true;
        decelFrom = machine.MotionTime;
        decelDistance = distance;
        decelTime = Mathf.Clamp(1.5f * Mathf.Abs(distance) + .15f, HaltMinTime, HaltMaxTime);
        decelElapsed = 0f;
    }

    private void UpdateHalt(float delta)
    {
        if (haltSettled || !IsOperating) return;
        halt.Step(delta, decelTime > 0f ? decelTime : HaltMinTime, out _, out _);
        if (!halt.VisualHalting) return;   // 정전 물결 차례 전: 외형은 그대로 돈다(판정은 이미 꺼짐)
        if (!decelerating) { BeginDecel(); if (haltSettled) return; }
        decelElapsed += delta;
        float s = Mathf.Clamp01(decelElapsed / decelTime);
        float h10 = s * s * s - 2f * s * s + s, h01 = -2f * s * s * s + 3f * s * s;
        machine.Seek(decelFrom + h10 * decelTime + h01 * decelDistance);
        PosePhase();
        if (s < 1f) return;
        decelerating = false;
        red?.Set(false);
        haltSettled = true;
    }

    protected override void OnDeviceLeaving()
    {
        trapDamage.Cancel();
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
        trapDamage.Cancel();
        base.OnDisable();
        active.Remove(this);
        if (active.Count == 0) StopLoop();
    }

    private void StopSparks()
    {
        // (정지 감속 BeginDecel은 남은 불꽃이 자연히 사라지게 StopEmitting을 따로 쓴다.)
        foreach (var system in sparks)
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void FixedUpdate()
    {
        if (!IsOperating || halt.Halted || (trapDamage.Damage <= 0f && !trapDamage.ReturnToStart)) return;
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
                if (trapDamage.TryDamage(this, player, info, point))
                    PlayFeedback(hitFeedback, point, Quaternion.FromToRotation(Vector3.up, (Vector3)(point - center)));
            }
        }
    }

    private void Update()
    {
        if (halt.Halted) UpdateHalt(Time.deltaTime);
        // WaveMachineMotion이 이번 프레임에 놓은 팔에 시작 각도를 더한다(0이면 아무것도 하지 않는다).
        if (phaseApplied || currentStartAngle != 0f) PosePhase();
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
