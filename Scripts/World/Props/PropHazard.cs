using System.Collections.Generic;
using UnityEngine;

// 주기적으로 증기를 뿜는 함정(증기 배출구). 대기 → 경고(위험부 붉은 발광이 깜박이며 덮개가 열림) → 분출 → 대기를 되풀이한다.
// - 분출 동안 배출구 위 판정 상자(기본 배출구 칸부터 위로 1×3칸)를 RaycastMotor2D.OverlapAttackBox(Player 레이어)로 묻고,
//   WaveTrapDamage(가시·피스톤과 같은 함정 피해 — 실제 적중 판정·선택형 시작 위치 복귀)로 Player에 피해를 준다. 적은 피해를 받지 않는다.
// - 회전(rotationSteps)을 따른다: 벽·천장 배출구는 판정 상자도 함께 돈다.
// - 주기·피해는 배치별 설정(IWaveDeviceSettings — DungeonProp가 모아 보인다). 위상(phaseOffset)으로 여러 배출구를 엇갈리게 둔다.
// - 기계 동작: 덮개(louvers)가 회전으로 열리고 닫힌다(크기 변형 없음). 증기는 ParticleSystem, 경고는 발광 밝기·Renderer 켜고 끄기.
// - 6차 T14(가시성): 기본 주기 대기 4.0 + 경고 1.0 + 분출 1.2 = 6.0초. 분출 동안 분출구 바로 위에 붉은 '과열' 코어 입자(공격 효과 빨강),
//   경고 동안 판정 기둥 둘레에 붉은 예고 테두리가 깜박인다(레이저 예고선과 같은 문법). 새 소리는 없다(효과음 칸은 비어 있다).
// - 6차 T9(방 클리어 정지, IDungeonRoomHazard): 정지 신호를 받은 그 호출에서 판정을 끄고, 정전 물결 차례가 오면 입자를 멈추고
//   위험부·예고 테두리 Renderer를 끈 채 덮개를 닫는다(불 꺼짐·공격 모양 아님·안 움직임). 신호가 없으면(던전 밖) 늘 작동한다.
[DisallowMultipleComponent]
public sealed class PropHazard : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings, IDungeonRoomHazard
{
    public enum Phase { Idle, Warning, Venting }

    public const string IdleKey = "ventIdle";
    public const string WarningKey = "ventWarning";
    public const string VentKey = "ventDuration";
    public const string OffsetKey = "ventOffset";

    [Header("주기(초)")]
    [SerializeField, Min(0f)] private float idleTime = 4f;
    [SerializeField, Min(0f)] private float warningTime = 1f;
    [SerializeField, Min(0.05f)] private float ventTime = 1.2f;
    [Tooltip("주기 시작 위치(초). 같은 방의 배출구를 엇갈리게 둔다.")]
    [SerializeField, Min(0f)] private float phaseOffset;

    [Header("피해")]
    [SerializeField, Min(0f)] private float damage = 10f;
    [Tooltip("판정 상자 중심(루트 기준, 회전 전). 기본: 배출구 칸 아래 경계부터 위로 3칸.")]
    [SerializeField] private Vector2 damageAreaOffset = new Vector2(0f, 1f);
    [SerializeField] private Vector2 damageAreaSize = new Vector2(0.8f, 3f);
    [SerializeField] private LayerMask targetMask;
    [Tooltip("분출 중 판정 간격(초). 피격 무적이 연속 피해를 막는다.")]
    [SerializeField, Min(0.01f)] private float checkInterval = 0.1f;

    [Header("표시")]
    [Tooltip("분출 동안 재생하는 증기 입자.")]
    [SerializeField] private ParticleSystem[] steamParticles = System.Array.Empty<ParticleSystem>();
    [Tooltip("경고 동안 재생하는 짧은 김(선택).")]
    [SerializeField] private ParticleSystem[] warningParticles = System.Array.Empty<ParticleSystem>();
    [Tooltip("분출 동안 분출구 바로 위에서 나오는 붉은 '과열' 코어 입자(공격 효과 빨강, 선택).")]
    [SerializeField] private ParticleSystem[] overheatParticles = System.Array.Empty<ParticleSystem>();
    [Tooltip("위험부 발광(붉은 노즐 테두리 등). 대기 중 낮게, 경고 중 깜박임, 분출 중 밝게. 정지하면 Renderer를 끈다.")]
    [SerializeField] private Renderer[] dangerRenderers = System.Array.Empty<Renderer>();
    [SerializeField, Min(0f)] private float idleGlow = 0.35f;
    [SerializeField, Min(0f)] private float warningGlow = 1.6f;
    [SerializeField, Min(0f)] private float ventGlow = 1.25f;
    [SerializeField, Min(0.05f)] private float warningBlinkPeriod = 0.16f;
    [Tooltip("경고 동안 판정 기둥 둘레에 깜박이는 붉은 예고 테두리(선택). 경고가 아닐 때는 Renderer를 꺼 둔다.")]
    [SerializeField] private Renderer[] telegraphRenderers = System.Array.Empty<Renderer>();
    [SerializeField, Min(0f)] private float telegraphGlow = 1.4f;
    [Tooltip("열리고 닫히는 덮개(회전만).")]
    [SerializeField] private Transform[] louvers = System.Array.Empty<Transform>();
    [Tooltip("덮개가 열렸을 때 각도(도, 닫힌 자세에 더한다). 덮개마다 부호를 바꾸려면 짝수 번째에 -를 곱한다.")]
    [SerializeField] private float louverOpenAngle = 55f;
    [SerializeField] private bool alternateLouvers = true;
    [SerializeField, Min(0.01f)] private float louverTime = 0.18f;
    [SerializeField] private FeedbackDataSO warningFeedback;
    [SerializeField] private FeedbackDataSO ventFeedback;

    private readonly WaveTrapDamage trapDamage = new WaveTrapDamage();
    private readonly HashSet<PlayerHealth> damagedPlayers = new HashSet<PlayerHealth>();
    private readonly HazardHalt halt = new HazardHalt();
    private Collider2D[] hits = new Collider2D[8];
    private RaycastMotor2D motor;
    private HazardLights danger;
    private HazardLights telegraph;
    private Quaternion[] louverHome = System.Array.Empty<Quaternion>();
    private float activeIdle, activeWarning, activeVent, activeOffset;
    private float clock;
    private float nextCheck;
    private float louverOpen;      // 0 닫힘 ~ 1 열림
    private Phase phase = Phase.Idle;
    private bool running;
    private bool haltedPose;       // 정지 외형(입자·발광 끔)에 들어갔다

    public Phase CurrentPhase => phase;
    public bool IsVenting => running && !halt.Halted && phase == Phase.Venting;
    /// <summary>방 클리어 정지 신호를 받아 판정이 꺼져 있다(6차 T9, 검사용).</summary>
    public bool IsHalted => halt.Halted;
    /// <summary>정지 외형(입자·위험부·예고 끔, 덮개 닫힘)에 들어갔다(검사용).</summary>
    public bool InHaltedPose => haltedPose;
    /// <summary>덮개 열림(0 닫힘 ~ 1 열림, 검사용).</summary>
    public float LouverOpen => louverOpen;
    /// <summary>예고 테두리가 지금 보이는지(검사용).</summary>
    public bool TelegraphVisible => AnyEnabled(telegraphRenderers);
    /// <summary>위험부 발광이 지금 보이는지(검사용).</summary>
    public bool DangerVisible => AnyEnabled(dangerRenderers);
    /// <summary>과열 코어 입자가 재생 중인지(검사용).</summary>
    public bool OverheatPlaying => AnyPlaying(overheatParticles);
    /// <summary>증기·김·과열 입자 중 하나라도 내보내는 중인지(검사용).</summary>
    public bool AnyEmitting => AnyPlaying(steamParticles) || AnyPlaying(warningParticles) || AnyPlaying(overheatParticles);
    /// <summary>판정 상자(월드 중심·크기·각도). 검사용.</summary>
    public void GetDamageArea(out Vector2 center, out Vector2 size, out float angle)
    {
        var scale = transform.lossyScale;
        size = new Vector2(Mathf.Abs(damageAreaSize.x * scale.x), Mathf.Abs(damageAreaSize.y * scale.y));
        center = transform.TransformPoint(damageAreaOffset);
        angle = transform.eulerAngles.z;
    }
    /// <summary>분출이 실제로 Player에 피해를 준 횟수(검사용, 대여마다 0).</summary>
    public int PlayerHits { get; private set; }

    private void Awake()
    {
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();   // 정적 공격 질의만 쓴다
        if (targetMask.value == 0) targetMask = LayerMask.GetMask("Player");
        danger = new HazardLights(dangerRenderers);
        telegraph = new HazardLights(telegraphRenderers);
        telegraph.Set(0f);
        louverHome = new Quaternion[louvers.Length];
        for (int i = 0; i < louvers.Length; i++) louverHome[i] = louvers[i] != null ? louvers[i].localRotation : Quaternion.identity;
        ApplyPropSettings(WaveDeviceSettings.None);
    }

    private void OnEnable()
    {
        // 풀을 거치지 않고 켜진 개체(장면 배치)도 돌게 한다. 풀 개체는 OnPropSpawned가 다시 맞춘다.
        if (Application.isPlaying && !running) Begin();
    }

    private void OnDisable()
    {
        running = false;
        trapDamage.Cancel();
        StopParticles(false);
    }

    public void OnPropSpawned(DungeonProp prop)
    {
        halt.Reset();
        Begin();
    }

    public void OnPropReturning(DungeonProp prop)
    {
        running = false;
        trapDamage.Cancel();
        StopParticles(false);
        danger?.Restore();
        telegraph?.Restore();
        telegraph?.Set(0f);
        halt.Reset();
        haltedPose = false;
    }

    private void Begin()
    {
        running = true;
        haltedPose = false;
        PlayerHits = 0;
        trapDamage.Cancel();
        clock = activeOffset;
        nextCheck = 0f;
        StopParticles(false);
        danger?.Restore();
        phase = PhaseAt(clock);
        louverOpen = phase == Phase.Idle ? 0f : 1f;
        ApplyLouvers();
        if (phase == Phase.Venting) { PlayParticles(steamParticles); PlayParticles(overheatParticles); }
        UpdateGlow();
    }

    // ── 6차 T9: 방 클리어 정지 ─────────────────────────────

    public void SetHazardHalted(bool halted, bool instant)
    {
        bool changed = halt.Signal(halted, instant);
        if (halted)
        {
            trapDamage.Cancel();   // 판정은 이 호출에서 바로 끈다(IsVenting이 거짓이 된다)
            if (instant && running)
            {
                EnterHaltedPose(true);
                louverOpen = 0f;
                ApplyLouvers();
            }
            return;
        }
        if (changed && running) Begin();   // 다시 작동(재도전 등): 주기를 처음부터
    }

    // 정지 외형: 입자를 멈추고(clear면 남은 입자도 지움) 위험부·예고 테두리 Renderer를 끈다. 덮개는 Update가 닫는다.
    private void EnterHaltedPose(bool clear)
    {
        haltedPose = true;
        phase = Phase.Idle;
        StopParticles(clear);
        danger?.Set(0f);
        telegraph?.Set(0f);
    }

    private float Period => activeIdle + activeWarning + activeVent;

    private Phase PhaseAt(float time)
    {
        float period = Period;
        if (period <= 0f) return Phase.Venting;
        float t = Mathf.Repeat(time, period);
        if (t < activeIdle) return Phase.Idle;
        if (t < activeIdle + activeWarning) return Phase.Warning;
        return Phase.Venting;
    }

    private void Update()
    {
        if (!running) return;
        float dt = Time.deltaTime;
        if (halt.Halted)
        {
            halt.Step(dt, louverTime, out bool started, out _);
            // 정전 물결 차례 전: 판정은 이미 꺼졌고 외형만 잠시 그대로 돈다.
            if (!halt.VisualHalting) { Cycle(dt); return; }
            if (started || !haltedPose) EnterHaltedPose(false);
            louverOpen = Mathf.MoveTowards(louverOpen, 0f, dt / louverTime);
            ApplyLouvers();
            return;
        }
        Cycle(dt);
    }

    private void Cycle(float dt)
    {
        clock += dt;
        var next = PhaseAt(clock);
        if (next != phase) Enter(next);
        float target = phase == Phase.Idle ? 0f : 1f;
        louverOpen = Mathf.MoveTowards(louverOpen, target, dt / louverTime);
        ApplyLouvers();
        UpdateGlow();
    }

    private void Enter(Phase next)
    {
        phase = next;
        switch (next)
        {
            case Phase.Idle:
                StopParticles(false);
                break;
            case Phase.Warning:
                PlayParticles(warningParticles);
                if (warningFeedback != null && !halt.Halted) DungeonPropFeedback.Play(warningFeedback, transform.position, transform.rotation);
                break;
            case Phase.Venting:
                StopParticles(warningParticles, false);
                PlayParticles(steamParticles);
                PlayParticles(overheatParticles);
                nextCheck = 0f;
                if (ventFeedback != null && !halt.Halted) DungeonPropFeedback.Play(ventFeedback, transform.position, transform.rotation);
                break;
        }
    }

    private void FixedUpdate()
    {
        if (!IsVenting || Time.time < nextCheck) return;
        if (trapDamage.Damage <= 0f && !trapDamage.ReturnToStart) return;
        nextCheck = Time.time + checkInterval;
        GetDamageArea(out var center, out var size, out float angle);
        int count;
        do
        {
            count = motor.OverlapAttackBox(center, size, angle, hits, targetMask);
            if (count < hits.Length) break;
            System.Array.Resize(ref hits, hits.Length * 2);
        } while (true);

        damagedPlayers.Clear();
        var info = new WeaponBaseSO.DamageInfo { damage = trapDamage.Damage, damageType = DamageType.Normal };
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            hits[i] = null;
            if (hit == null) continue;
            var player = hit.GetComponentInParent<PlayerHealth>();
            if (player == null || !damagedPlayers.Add(player)) continue;
            if (trapDamage.TryDamage(this, player, info, hit.ClosestPoint(center))) PlayerHits++;
        }
    }

    private void ApplyLouvers()
    {
        float eased = louverOpen * louverOpen * (3f - 2f * louverOpen);
        for (int i = 0; i < louvers.Length; i++)
        {
            if (louvers[i] == null) continue;
            float sign = alternateLouvers && i % 2 == 1 ? -1f : 1f;
            louvers[i].localRotation = louverHome[i] * Quaternion.Euler(0f, 0f, louverOpenAngle * sign * eased);
        }
    }

    private void UpdateGlow()
    {
        bool blinkOn = Mathf.Repeat(clock, warningBlinkPeriod) < warningBlinkPeriod * 0.5f;
        if (danger != null && danger.Count > 0)
        {
            float level = idleGlow;
            if (phase == Phase.Warning) level = warningGlow * (blinkOn ? 1f : 0.45f);
            else if (phase == Phase.Venting) level = ventGlow;
            danger.Set(level);
        }
        // 예고 테두리: 경고 동안만 위험부와 같은 박자로 깜박인다(꺼진 박자는 Renderer를 끈다 — 알파 블렌드 재질).
        if (telegraph != null && telegraph.Count > 0)
            telegraph.Set(phase == Phase.Warning && blinkOn ? telegraphGlow : 0f);
    }

    private static void PlayParticles(ParticleSystem[] systems)
    {
        if (systems == null) return;
        foreach (var system in systems)
            if (system != null && !system.isPlaying) system.Play(true);
    }

    private void StopParticles(bool clear)
    {
        StopParticles(steamParticles, clear);
        StopParticles(warningParticles, clear);
        StopParticles(overheatParticles, clear);
    }

    private static void StopParticles(ParticleSystem[] systems, bool clear)
    {
        if (systems == null) return;
        foreach (var system in systems)
            if (system != null) system.Stop(true, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
    }

    private static bool AnyEnabled(Renderer[] renderers)
    {
        if (renderers == null) return false;
        foreach (var renderer in renderers)
            if (renderer != null && renderer.enabled) return true;
        return false;
    }

    private static bool AnyPlaying(ParticleSystem[] systems)
    {
        if (systems == null) return false;
        foreach (var system in systems)
            if (system != null && system.isEmitting) return true;
        return false;
    }

    // ── 배치별 설정 ─────────────────────────────

    private WaveDeviceParameter IdleParameter => new WaveDeviceParameter(IdleKey, "대기 시간", "분출이 끝난 뒤 다음 경고까지의 시간입니다.", "초", 0f, 20f, 0.1f, idleTime);
    private WaveDeviceParameter WarningParameter => new WaveDeviceParameter(WarningKey, "경고 시간", "붉은 발광과 예고 테두리가 깜박이며 덮개가 열리는 시간입니다.", "초", 0f, 5f, 0.05f, warningTime);
    private WaveDeviceParameter VentParameter => new WaveDeviceParameter(VentKey, "분출 시간", "증기가 나오며 피해를 주는 시간입니다.", "초", 0.1f, 10f, 0.05f, ventTime);
    private WaveDeviceParameter OffsetParameter => new WaveDeviceParameter(OffsetKey, "시작 위상", "주기를 이 시간만큼 앞당겨 시작합니다. 여러 배출구를 엇갈리게 둘 때 씁니다.", "초", 0f, 30f, 0.1f, phaseOffset);

    public WaveDeviceParameter[] PropParameters => new[]
    {
        WaveTrapDamage.DamageParameter(damage), IdleParameter, WarningParameter, VentParameter, OffsetParameter, WaveTrapDamage.ReturnParameter,
    };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        trapDamage.ApplySettings(overrides, damage);
        activeIdle = WaveDeviceSettings.Value(overrides, IdleParameter);
        activeWarning = WaveDeviceSettings.Value(overrides, WarningParameter);
        activeVent = WaveDeviceSettings.Value(overrides, VentParameter);
        float offset = WaveDeviceSettings.Value(overrides, OffsetParameter);
        if (running && !Mathf.Approximately(offset, activeOffset)) clock += offset - activeOffset;   // 생성 직후 배치값이 오면 위상만 맞춘다
        activeOffset = offset;
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, damageAreaOffset, damageAreaSize, new Color(1f, .22f, .16f, .9f));
        return true;
    }
}
