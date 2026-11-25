using System;
using UnityEngine;

// 노출 접속함(N18, 함정). 대기 → 예고(1초: 덮개가 떨리고 붉은 단자가 깜박이며 작은 불꽃이 튄다) → 방전(반경 1.2칸 붉은 전기 불꽃) → 대기.
// - 피해: 방전 동안 루트 RaycastMotor2D.OverlapAttackCircle(Player)로 묻고 WaveTrapDamage(복귀 없음)로 한 번 준다. 적은 맞지 않는다.
// - 기계 동작: 덮개 경첩 회전 떨림, 단자 발광 밝기, 전기 줄기 Renderer 켜고 끄기(크기 변형 없음). 전기 줄기는 방전 순간만 보인다.
// - 작동 조건(배치 설정): 항상 / 방 클리어 전까지.
// - 소리(임시 기존 효과음): 방전 EnergyZapBurst + RollerSparkFeedback(불꽃 입자 포함).
[DisallowMultipleComponent]
public sealed class PropSparkJunction : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings, IDungeonRoomPowered
{
    public enum Phase { Idle, Warning, Discharge, Resting }

    public const string IdleKey = "sparkIdle";
    public const string WarningKey = "sparkWarning";
    public const string OffsetKey = "sparkOffset";
    public const string RadiusKey = "sparkRadius";

    [Header("배치 기본값")]
    [SerializeField, Min(.2f)] private float idleTime = 3f;
    [SerializeField, Min(.2f)] private float warningTime = 1f;
    [SerializeField, Min(0f)] private float phaseOffset;
    [SerializeField, Min(.3f)] private float radius = 1.2f;
    [SerializeField, Min(0f)] private float damage = 10f;
    [SerializeField] private int powerMode = HazardPowerGate.UntilClear;

    [Header("방전")]
    [SerializeField, Min(.05f)] private float dischargeTime = .28f;
    [SerializeField, Tooltip("방전 중심(루트 기준).")] private Vector2 center;
    [SerializeField] private LayerMask playerMask;

    [Header("구성(생성 도구가 연결)")]
    [SerializeField, Tooltip("떨리는 덮개(경첩 회전).")] private Transform lid;
    [SerializeField, Min(0f)] private float lidRattle = 7f;
    [SerializeField, Tooltip("붉은 단자(위험부).")] private Renderer[] terminals = Array.Empty<Renderer>();
    [SerializeField, Tooltip("방전 줄기(평소 꺼짐, 방전 중 무작위로 켜고 끈다).")] private Renderer[] arcs = Array.Empty<Renderer>();
    [SerializeField] private ParticleSystem crackle;
    [SerializeField] private ParticleSystem burst;
    [SerializeField, Min(0f)] private float idleGlow = .3f;
    [SerializeField, Min(0f)] private float warningGlow = 1.8f;
    [SerializeField, Min(.05f)] private float blinkPeriod = .12f;

    [Header("효과(임시: 기존 효과음)")]
    [SerializeField] private SoundOptions zapSound;
    [SerializeField] private FeedbackDataSO dischargeFeedback;

    private readonly WaveTrapDamage trapDamage = new WaveTrapDamage();
    private readonly HazardPowerGate power = new HazardPowerGate();
    private readonly System.Collections.Generic.List<Collider2D> hits = new System.Collections.Generic.List<Collider2D>(8);
    private RaycastMotor2D motor;
    private HazardLights glow;
    private HazardLights[] arcLights = Array.Empty<HazardLights>();
    private Quaternion lidHome;
    private float activeIdle, activeWarning, activeOffset, activeRadius, activeDamage;
    private Phase phase = Phase.Resting;
    private float phaseTime;
    private float waitTime;
    private bool hitThisDischarge;
    private float arcTimer;
    private bool cached;
    private bool live;

    public Phase CurrentPhase => phase;
    public int Discharges { get; private set; }
    public int PlayerHits { get; private set; }
    public float LastWarningTime { get; private set; } = -1f;
    public float LastHitTime { get; private set; } = -1f;
    public float Radius => activeRadius;
    public Vector2 WorldCenter => transform.TransformPoint(center);

    private void Awake()
    {
        Cache();
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();   // 정적 공격 질의만 쓴다
        if (playerMask.value == 0) playerMask = LayerMask.GetMask("Player");
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        ReadSettings(WaveDeviceSettings.None);
        glow = new HazardLights(terminals);
        arcLights = new HazardLights[arcs.Length];
        for (int i = 0; i < arcs.Length; i++) arcLights[i] = new HazardLights(new[] { arcs[i] });
        if (lid != null) lidHome = lid.localRotation;
    }

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        live = true;
        power.Reset();
        power.Mode = powerMode;
        trapDamage.Cancel();
        Discharges = 0;
        PlayerHits = 0;
        LastWarningTime = LastHitTime = -1f;
        if (lid != null) lid.localRotation = lidHome;
        SetArcs(false);
        PropHazardKit.Stop(crackle, true);
        PropHazardKit.Stop(burst, true);
        StartCycle();
    }

    public void OnPropReturning(DungeonProp prop)
    {
        live = false;
        trapDamage.Cancel();
        if (lid != null) lid.localRotation = lidHome;
        glow.Restore();
        foreach (var arc in arcLights) arc.Restore();
        PropHazardKit.Stop(crackle, true);
    }

    private void OnDisable() => trapDamage.Cancel();

    public void SetRoomPowered(bool powered, bool instant)
    {
        power.SetRoomPowered(powered);
        if (!live) return;
        if (!power.Running && phase != Phase.Discharge) Enter(Phase.Resting, 0f);
        else if (power.Running && phase == Phase.Resting) StartCycle();
    }

    private void StartCycle()
    {
        if (!power.Running) { Enter(Phase.Resting, 0f); return; }
        // 위상만큼 앞당긴다(대기 구간 안에서만 — 들어오자마자 방전하지 않게).
        Enter(Phase.Idle, Mathf.Max(.2f, activeIdle - Mathf.Repeat(activeOffset, activeIdle + activeWarning + dischargeTime)));
    }

    private void Enter(Phase next, float wait)
    {
        phase = next;
        phaseTime = 0f;
        waitTime = wait;
        switch (next)
        {
            case Phase.Warning:
                LastWarningTime = Time.time;
                PropHazardKit.Play(crackle);
                break;
            case Phase.Discharge:
                Discharges++;
                hitThisDischarge = false;
                PropHazardKit.Stop(crackle);
                Vector3 at = WorldCenter;
                PropHazardKit.PlayClip(zapSound, at, .85f);
                PropHazardKit.PlayFeedback(dischargeFeedback, at);
                if (burst != null) burst.Emit(Mathf.RoundToInt(16 * activeRadius));
                // 피해가 들어가는 첫 프레임부터 줄기를 모두 켠다(무작위 깜박임은 다음 박자부터). 맞았는데 줄기가 안 보이는 프레임을 없앤다.
                SetArcs(true);
                arcTimer = .045f;
                DamagePlayer();
                break;
            case Phase.Idle:
            case Phase.Resting:
                PropHazardKit.Stop(crackle);
                SetArcs(false);
                break;
        }
    }

    private void Update()
    {
        if (!live) return;
        float dt = Time.deltaTime;
        phaseTime += dt;
        switch (phase)
        {
            case Phase.Idle:
                glow.Set(idleGlow);
                if (lid != null) lid.localRotation = lidHome;
                if (!power.Running) Enter(Phase.Resting, 0f);
                else if (phaseTime >= waitTime) Enter(Phase.Warning, activeWarning);
                break;
            case Phase.Warning:
            {
                bool on = Mathf.Repeat(phaseTime, blinkPeriod) < blinkPeriod * .5f;
                float k = Mathf.Clamp01(phaseTime / Mathf.Max(.01f, activeWarning));
                glow.Set((on ? warningGlow : warningGlow * .4f) * Mathf.Lerp(.6f, 1f, k));
                if (lid != null)
                {
                    // 덮개가 점점 세게 떨린다(경첩 회전만).
                    float rattle = lidRattle * k * (Mathf.PerlinNoise(phaseTime * 30f, 0f) * 2f - 1f);
                    lid.localRotation = lidHome * Quaternion.Euler(0f, 0f, rattle);
                }
                if (phaseTime >= activeWarning) Enter(Phase.Discharge, dischargeTime);
                break;
            }
            case Phase.Discharge:
                glow.Set(warningGlow * 1.3f);
                arcTimer -= dt;
                if (arcTimer <= 0f)
                {
                    arcTimer = .045f;
                    for (int i = 0; i < arcLights.Length; i++) arcLights[i].Set(UnityEngine.Random.value < .6f ? 1.4f : 0f);
                }
                if (lid != null) lid.localRotation = lidHome * Quaternion.Euler(0f, 0f, lidRattle * 1.4f);
                if (phaseTime >= waitTime)
                {
                    SetArcs(false);
                    if (power.Running) Enter(Phase.Idle, activeIdle);
                    else Enter(Phase.Resting, 0f);
                }
                break;
            case Phase.Resting:
                glow.Set(idleGlow * .5f);
                if (lid != null) lid.localRotation = lidHome;
                if (power.Running) StartCycle();
                break;
        }
    }

    private void FixedUpdate()
    {
        if (live && phase == Phase.Discharge && !hitThisDischarge) DamagePlayer();
    }

    private void DamagePlayer()
    {
        if (activeDamage <= 0f || motor == null) return;
        Vector2 c = WorldCenter;
        var filter = new ContactFilter2D();
        filter.SetLayerMask(playerMask);
        filter.useTriggers = true;
        motor.OverlapAttackCircle(c, activeRadius, filter, hits);
        foreach (var hit in hits)
        {
            if (hit == null) continue;
            var player = hit.GetComponentInParent<PlayerHealth>();
            if (player == null) continue;
            Vector2 point = hit.ClosestPoint(c);
            var info = new WeaponBaseSO.DamageInfo { damage = activeDamage, damageType = DamageType.Normal, knockbackOrigin = c };
            if (trapDamage.TryDamage(this, player, info, point, activeDamage, false))
            {
                PlayerHits++;
                LastHitTime = Time.time;
            }
            hitThisDischarge = true;   // 무적·반격으로 막혀도 한 번의 방전은 한 번만 묻는다
            break;
        }
        hits.Clear();
    }

    private void SetArcs(bool on)
    {
        foreach (var arc in arcLights) arc.Set(on ? 1.4f : 0f);
    }

    // ── 배치별 설정 ─────────────────────────────

    private WaveDeviceParameter IdleParameter => new WaveDeviceParameter(IdleKey, "대기 시간", "방전이 끝난 뒤 다음 예고까지의 시간입니다.", "초", .2f, 20f, .1f, idleTime);
    private WaveDeviceParameter WarningParameter => new WaveDeviceParameter(WarningKey, "예고 시간", "덮개가 떨리고 단자가 깜박이는 시간입니다.", "초", .2f, 5f, .05f, warningTime);
    private WaveDeviceParameter OffsetParameter => new WaveDeviceParameter(OffsetKey, "시작 위상", "첫 예고를 이 시간만큼 앞당깁니다(대기 시간 안에서). 여러 접속함을 엇갈리게 둘 때 씁니다.", "초", 0f, 30f, .1f, phaseOffset);
    private WaveDeviceParameter RadiusParameter => new WaveDeviceParameter(RadiusKey, "방전 반경", "불꽃이 닿는 원의 반지름(칸)입니다.", "칸", .5f, 3f, .1f, radius, true);

    public WaveDeviceParameter[] PropParameters => new[]
    {
        WaveTrapDamage.DamageParameter(damage), IdleParameter, WarningParameter, OffsetParameter, RadiusParameter, PropHazardKit.PowerParameter(powerMode),
    };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        float oldOffset = activeOffset, oldIdle = activeIdle;
        ReadSettings(overrides);
        power.Mode = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, PropHazardKit.PowerParameter(powerMode)));
        if (live && phase == Phase.Idle && Discharges == 0 && phaseTime < .01f
            && (!Mathf.Approximately(oldOffset, activeOffset) || !Mathf.Approximately(oldIdle, activeIdle))) StartCycle();
    }

    private void ReadSettings(WaveDeviceSetting[] overrides)
    {
        activeDamage = WaveDeviceSettings.Value(overrides, WaveTrapDamage.DamageParameter(damage));
        activeIdle = WaveDeviceSettings.Value(overrides, IdleParameter);
        activeWarning = WaveDeviceSettings.Value(overrides, WarningParameter);
        activeOffset = WaveDeviceSettings.Value(overrides, OffsetParameter);
        activeRadius = WaveDeviceSettings.Value(overrides, RadiusParameter);
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        float r = WaveDeviceSettings.Value(overrides, RadiusParameter);
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Circle, center, new Vector2(r, r), new Color(1f, .22f, .16f, .9f));
        return true;
    }
}
