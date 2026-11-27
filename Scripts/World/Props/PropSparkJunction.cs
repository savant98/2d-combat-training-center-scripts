using System;
using UnityEngine;

// 노출 접속함(N18, 장식 Prop). 피해 판정 없이 대기 → 예고(덮개 떨림·호박 단자 깜박임·작은 불꽃) → 방전 연출(전기 줄기·스파크) → 대기 주기를 반복한다.
// - 기계 동작: 덮개 경첩 회전 떨림, 단자 발광 밝기, 전기 줄기 Renderer 켜고 끄기(크기 변형 없음). 전기 줄기는 방전 순간만 보인다.
// - 물리·피해: 데미지 및 RaycastMotor2D 공격 질의 없음(장식 Prop 전용, IDungeonRoomHazard 미구현).
// - 소리(임시 기존 효과음): 방전 EnergyZapBurst + RollerSparkFeedback(불꽃 입자 포함).
[DisallowMultipleComponent]
public sealed class PropSparkJunction : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings
{
    public enum Phase { Idle, Warning, Discharge }

    public const string IdleKey = "sparkIdle";
    public const string WarningKey = "sparkWarning";
    public const string OffsetKey = "sparkOffset";

    [Header("배치 기본값")]
    [SerializeField, Min(.2f)] private float idleTime = 3f;
    [SerializeField, Min(.2f)] private float warningTime = 1f;
    [SerializeField, Min(0f)] private float phaseOffset;

    [Header("방전 연출")]
    [SerializeField, Min(.05f)] private float dischargeTime = .28f;
    [SerializeField, Tooltip("방전 중심(루트 기준).")] private Vector2 center;

    [Header("구성(생성 도구가 연결)")]
    [SerializeField, Tooltip("떨리는 덮개(경첩 회전).")] private Transform lid;
    [SerializeField, Min(0f)] private float lidRattle = 7f;
    [SerializeField, Tooltip("호박 단자(장식 표시등).")] private Renderer[] terminals = Array.Empty<Renderer>();
    [SerializeField, Tooltip("방전 줄기(평소 꺼짐, 방전 중 무작위로 켜고 끈다).")] private Renderer[] arcs = Array.Empty<Renderer>();
    [SerializeField] private ParticleSystem crackle;
    [SerializeField] private ParticleSystem burst;
    [SerializeField, Min(0f)] private float idleGlow = .3f;
    [SerializeField, Min(0f)] private float warningGlow = 1.6f;
    [SerializeField, Min(.05f)] private float blinkPeriod = .12f;

    [Header("효과(임시: 기존 효과음)")]
    [SerializeField] private SoundOptions zapSound;
    [SerializeField] private FeedbackDataSO dischargeFeedback;

    private HazardLights glow;
    private HazardLights[] arcLights = Array.Empty<HazardLights>();
    private Quaternion lidHome;
    private float activeIdle, activeWarning, activeOffset;
    private Phase phase = Phase.Idle;
    private float phaseTime;
    private float waitTime;
    private float arcTimer;
    private bool cached;
    private bool live;

    public Phase CurrentPhase => phase;
    public int Discharges { get; private set; }
    public float LastWarningTime { get; private set; } = -1f;
    public Vector2 WorldCenter => transform.TransformPoint(center);
    /// <summary>단자 발광이 지금 보이는지(검사용).</summary>
    public bool TerminalsLit
    {
        get
        {
            foreach (var renderer in terminals) if (renderer != null && renderer.enabled) return true;
            return false;
        }
    }

    private void Awake()
    {
        Cache();
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
        Discharges = 0;
        LastWarningTime = -1f;
        if (lid != null) lid.localRotation = lidHome;
        SetArcs(false);
        PropHazardKit.Stop(crackle, true);
        PropHazardKit.Stop(burst, true);
        StartCycle();
    }

    public void OnPropReturning(DungeonProp prop)
    {
        live = false;
        if (lid != null) lid.localRotation = lidHome;
        glow.Restore();
        foreach (var arc in arcLights) arc.Restore();
        PropHazardKit.Stop(crackle, true);
    }

    private void StartCycle()
    {
        // 위상만큼 앞당긴다(대기 구간 안에서만 — 들어오자마자 방전 연출이 튀지 않게).
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
                PropHazardKit.Stop(crackle);
                Vector3 at = WorldCenter;
                PropHazardKit.PlayClip(zapSound, at, .65f);
                PropHazardKit.PlayFeedback(dischargeFeedback, at);
                if (burst != null) burst.Emit(16);
                SetArcs(true);
                arcTimer = .045f;
                break;
            case Phase.Idle:
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
                if (phaseTime >= waitTime) Enter(Phase.Warning, activeWarning);
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
                glow.Set(warningGlow * 1.25f);
                arcTimer -= dt;
                if (arcTimer <= 0f)
                {
                    arcTimer = .045f;
                    for (int i = 0; i < arcLights.Length; i++) arcLights[i].Set(UnityEngine.Random.value < .6f ? 1.3f : 0f);
                }
                if (lid != null) lid.localRotation = lidHome * Quaternion.Euler(0f, 0f, lidRattle * 1.3f);
                if (phaseTime >= waitTime)
                {
                    SetArcs(false);
                    Enter(Phase.Idle, activeIdle);
                }
                break;
        }
    }

    private void SetArcs(bool on)
    {
        foreach (var arc in arcLights) arc.Set(on ? 1.3f : 0f);
    }

    // ── 배치별 설정 ─────────────────────────────

    private WaveDeviceParameter IdleParameter => new WaveDeviceParameter(IdleKey, "대기 시간", "방전 연출이 끝난 뒤 다음 예고까지의 시간입니다.", "초", .2f, 20f, .1f, idleTime);
    private WaveDeviceParameter WarningParameter => new WaveDeviceParameter(WarningKey, "예고 시간", "덮개가 떨리고 단자가 깜박이는 시간입니다.", "초", .2f, 5f, .05f, warningTime);
    private WaveDeviceParameter OffsetParameter => new WaveDeviceParameter(OffsetKey, "시작 위상", "첫 예고를 이 시간만큼 앞당깁니다(대기 시간 안에서). 여러 접속함을 엇갈리게 둘 때 씁니다.", "초", 0f, 30f, .1f, phaseOffset);

    public WaveDeviceParameter[] PropParameters => new[]
    {
        IdleParameter, WarningParameter, OffsetParameter,
    };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        float oldOffset = activeOffset, oldIdle = activeIdle;
        ReadSettings(overrides);
        if (live && phase == Phase.Idle && Discharges == 0 && phaseTime < .01f
            && (!Mathf.Approximately(oldOffset, activeOffset) || !Mathf.Approximately(oldIdle, activeIdle))) StartCycle();
    }

    private void ReadSettings(WaveDeviceSetting[] overrides)
    {
        activeIdle = WaveDeviceSettings.Value(overrides, IdleParameter);
        activeWarning = WaveDeviceSettings.Value(overrides, WarningParameter);
        activeOffset = WaveDeviceSettings.Value(overrides, OffsetParameter);
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        range = default;
        return false;
    }
}
