using System;
using UnityEngine;

// 레이저 용접 팔(N06, 벽 함정). 접힌 팔이 대기 → 펼침·노즐 붉은 충전(예고) → 수평 용접 레이저 발사 → 접힘을 되풀이한다.
// - 레이저 판정은 새로 만들지 않는다: 자식 'Emitter'의 기존 LaserSpawner(LaserEmitterCell과 같은 구성 — EnvironmentLaserBeam,
//   Platform에 막힘, Player에만 피해)를 발사 순간마다 한 번 장전해(OnSpawnedFromPool, creationCycle 0·spawnOnce) 쏘게 한다.
//   Emitter에는 칸을 차지하지 않는 WaveEnvironmentTool 표식이 있어 LaserSpawner가 빔에 WaveTrapDamage 경로(복귀 없음)를 단다.
//   피해량은 배치 설정을 Emitter에 그대로 넘긴다('시작 위치 복귀'는 넘기지 않는다 — 던전 방은 Wave 시작 좌표와 다르다).
// - 발사 방향은 Prop 회전을 따른다(회전 0 = 오른쪽, 2 = 왼쪽). 레이저 높이는 footprint 가운데(두 칸 경계).
// - 기계 동작: 어깨·팔뚝 두 관절이 회전으로 펼쳐지고 접힌다(크기 변형 없음). 노즐 렌즈만 빨강(피해 부위), 충전 동안 밝아지고 깜박인다.
// - 작동 조건(배치 설정): 항상 / 방 클리어 전까지. 클리어 신호가 오면 지금 발사를 마치고 접힌 채 쉰다.
// - 소리(임시 기존 효과음): 충전 EnemyWindupWarning, 발사 LaserFlashCrack(빔 자체 소리가 있으면 함께 난다).
// - 예고선(4차): 충전(Charging) 동안 늘 Emitter LaserSpawner의 예고선을 보인다(발사와 같은 경로, 얇게 깜박이다 발사 직전 굵어짐).
//   예고선은 실행 중에 만든다(Prefab 무수정). 충전이 아닌 단계에 들어가면 바로 숨긴다.
[DisallowMultipleComponent]
public sealed class PropLaserWelder : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings, IDungeonRoomPowered
{
    public enum Phase { Folded, Unfolding, Charging, Firing, Folding, Resting }

    public const string IdleKey = "welderIdle";
    public const string ChargeKey = "welderCharge";
    public const string OffsetKey = "welderOffset";

    [Header("배치 기본값")]
    [SerializeField, Min(.2f), Tooltip("접힌 채 쉬는 시간(초).")] private float idleTime = 2.4f;
    [SerializeField, Min(.2f), Tooltip("펼친 뒤 노즐이 충전되는 예고 시간(초).")] private float chargeTime = .9f;
    [SerializeField, Min(0f), Tooltip("주기 시작 위치(초). 층마다 엇갈리게 둔다.")] private float phaseOffset;
    [SerializeField, Min(0f)] private float damage = 10f;
    [SerializeField] private int powerMode = HazardPowerGate.UntilClear;

    [Header("기계 동작")]
    [SerializeField] private Transform shoulder;
    [SerializeField] private Transform forearm;
    [SerializeField, Tooltip("어깨를 펼칠 때 접힌 자세에 더하는 각도(도).")] private float shoulderUnfold = -75f;
    [SerializeField, Tooltip("팔뚝을 펼칠 때 접힌 자세에 더하는 각도(도).")] private float forearmUnfold = 150f;
    [SerializeField, Min(.05f)] private float unfoldTime = .35f;
    [SerializeField, Min(.05f)] private float foldTime = .4f;
    [SerializeField, Min(.1f), Tooltip("발사 시간(초) = Emitter LaserSpawner의 duration과 같게 둔다(생성 도구가 맞춘다).")] private float fireTime = 1.2f;
    [SerializeField, Min(0f), Tooltip("발사 순간 팔이 뒤로 밀리는 각도(도).")] private float recoil = 3f;

    [Header("구성(생성 도구가 연결)")]
    [SerializeField] private LaserSpawner emitter;
    [SerializeField, Tooltip("노즐 렌즈(붉은 위험부).")] private Renderer[] nozzleGlow = Array.Empty<Renderer>();
    [SerializeField, Tooltip("팔 관절 표시등(흰색, 동작 중 밝게).")] private Renderer[] jointLights = Array.Empty<Renderer>();
    [SerializeField] private ParticleSystem chargeSparks;
    [SerializeField] private ParticleSystem muzzleSparks;
    [SerializeField, Min(0f)] private float idleNozzleGlow = .2f;
    [SerializeField, Min(0f)] private float chargeNozzleGlow = 1.8f;
    [SerializeField, Min(0f)] private float fireNozzleGlow = 2.4f;

    [Header("효과(임시: 기존 효과음)")]
    [SerializeField] private SoundOptions chargeSound;
    [SerializeField] private SoundOptions fireSound;

    private readonly HazardPowerGate power = new HazardPowerGate();
    private HazardLights nozzle, joints;
    private Quaternion shoulderHome, forearmHome;
    private float activeIdle, activeCharge, activeOffset, activeDamage;
    private WaveDeviceSetting[] emitterSettings = WaveDeviceSettings.None;
    private Phase phase = Phase.Resting;
    private float phaseTime;
    private float unfold;      // 0 접힘 ~ 1 펼침
    private bool cached;
    private bool live;

    public Phase CurrentPhase => phase;
    /// <summary>이번 대여에서 레이저를 장전해 쏜 횟수(검사용).</summary>
    public int Shots { get; private set; }
    public float LastShotTime { get; private set; } = -1f;
    public float LastChargeTime { get; private set; } = -1f;
    public LaserSpawner Emitter => emitter;
    /// <summary>한 주기(접힘 대기 + 펼침 + 충전 + 발사 + 접힘) 길이(초).</summary>
    public float Period => activeIdle + unfoldTime + activeCharge + fireTime + foldTime;

    private void Awake() => Cache();

    private void Cache()
    {
        if (cached) return;
        cached = true;
        ReadSettings(WaveDeviceSettings.None);
        nozzle = new HazardLights(nozzleGlow);
        joints = new HazardLights(jointLights);
        if (shoulder != null) shoulderHome = shoulder.localRotation;
        if (forearm != null) forearmHome = forearm.localRotation;
        if (emitter != null)
        {
            emitter.creationCycle = 0f;   // 장전하면 다음 Update에 바로 한 번 쏜다
            emitter.spawnOnce = true;
        }
    }

    // ── IDungeonPropPart ─────────────────────────────

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        live = true;
        power.Reset();
        power.Mode = powerMode;
        Shots = 0;
        LastShotTime = LastChargeTime = -1f;
        unfold = 0f;
        ApplyPose(0f);
        PropHazardKit.Stop(chargeSparks, true);
        PropHazardKit.Stop(muzzleSparks, true);
        if (emitter != null) emitter.HideWarning();
        if (emitter != null && emitter.entityBase != null && WorldEntityPool.Instance != null) WorldEntityPool.Instance.Prewarm(emitter.entityBase);
        StartCycle();
    }

    public void OnPropReturning(DungeonProp prop)
    {
        live = false;
        unfold = 0f;
        ApplyPose(0f);
        nozzle.Restore();
        joints.Restore();
        PropHazardKit.Stop(chargeSparks, true);
        PropHazardKit.Stop(muzzleSparks, true);
        if (emitter != null) emitter.HideWarning();
        // Emitter가 쏜 빔은 루트가 꺼질 때 LaserSpawner.OnDisable이 거둔다.
    }

    public void SetRoomPowered(bool powered, bool instant)
    {
        power.SetRoomPowered(powered);
        if (!live) return;
        if (!power.Running)
        {
            if (phase == Phase.Folded || phase == Phase.Unfolding || phase == Phase.Charging) Enter(instant || phase == Phase.Folded ? Phase.Resting : Phase.Folding);
        }
        else if (phase == Phase.Resting) StartCycle();
    }

    // 위상(phaseOffset)만큼 주기를 앞당겨 시작한다. 쉬는 구간 안이면 그만큼 덜 쉰다.
    private void StartCycle()
    {
        if (!power.Running) { Enter(Phase.Resting); return; }
        Enter(Phase.Folded);
        phaseTime = Mathf.Repeat(activeOffset, Mathf.Max(.01f, Period));
        if (phaseTime > activeIdle) phaseTime = activeIdle;   // 접힘 대기 밖의 위상은 대기 끝으로 맞춘다(갑자기 발사하지 않게)
    }

    private void Enter(Phase next)
    {
        phase = next;
        phaseTime = 0f;
        // 예고선은 충전 단계에만 보인다.
        if (emitter != null)
        {
            if (next == Phase.Charging) emitter.ShowWarning(0f, Mathf.Max(.01f, activeCharge));
            else emitter.HideWarning();
        }
        switch (next)
        {
            case Phase.Unfolding:
                LastChargeTime = Time.time;
                PropHazardKit.PlayClip(chargeSound, NozzlePosition, .8f);
                break;
            case Phase.Charging:
                PropHazardKit.Play(chargeSparks);
                break;
            case Phase.Firing:
                PropHazardKit.Stop(chargeSparks);
                Fire();
                break;
            case Phase.Folding:
            case Phase.Resting:
                PropHazardKit.Stop(chargeSparks);
                PropHazardKit.Stop(muzzleSparks);
                break;
        }
    }

    private Vector3 NozzlePosition => emitter != null ? emitter.transform.position : transform.position;

    // 기존 LaserSpawner를 한 번 장전한다(장전이 피해 설정을 기본값으로 되돌리므로 배치 피해량을 다시 넘긴다).
    private void Fire()
    {
        Shots++;
        LastShotTime = Time.time;
        PropHazardKit.PlayClip(fireSound, NozzlePosition, .9f);
        PropHazardKit.Play(muzzleSparks);
        if (emitter == null || emitter.entityBase == null || WorldEntityPool.Instance == null) return;
        emitter.creationCycle = 0f;
        emitter.spawnOnce = true;
        emitter.OnSpawnedFromPool();
        emitter.ApplySettings(emitterSettings);
    }

    private void Update()
    {
        if (!live) return;
        float dt = Time.deltaTime;
        phaseTime += dt;
        switch (phase)
        {
            case Phase.Folded:
                unfold = 0f;
                nozzle.Set(idleNozzleGlow);
                joints.Set(.35f);
                if (!power.Running) Enter(Phase.Resting);
                else if (phaseTime >= activeIdle) Enter(Phase.Unfolding);
                break;
            case Phase.Unfolding:
                unfold = Mathf.Clamp01(phaseTime / unfoldTime);
                nozzle.Set(Mathf.Lerp(idleNozzleGlow, chargeNozzleGlow * .6f, unfold));
                joints.Set(1f);
                if (phaseTime >= unfoldTime) Enter(Phase.Charging);
                break;
            case Phase.Charging:
            {
                unfold = 1f;
                float k = Mathf.Clamp01(phaseTime / Mathf.Max(.01f, activeCharge));
                float period = Mathf.Lerp(.22f, .07f, k);                          // 충전이 끝날수록 빨리 깜박인다
                bool on = Mathf.Repeat(phaseTime, period) < period * .55f;
                nozzle.Set(chargeNozzleGlow * Mathf.Lerp(.55f, 1f, k) * (on ? 1f : .55f));
                joints.Set(1f);
                if (phaseTime >= activeCharge) Enter(Phase.Firing);
                else if (emitter != null) emitter.ShowWarning(phaseTime, Mathf.Max(.01f, activeCharge));
                break;
            }
            case Phase.Firing:
                unfold = 1f;
                nozzle.Set(fireNozzleGlow);
                joints.Set(1f);
                if (phaseTime >= fireTime) Enter(Phase.Folding);
                break;
            case Phase.Folding:
                unfold = 1f - Mathf.Clamp01(phaseTime / foldTime);
                nozzle.Set(Mathf.Lerp(idleNozzleGlow, fireNozzleGlow * .4f, unfold));
                joints.Set(.6f);
                if (phaseTime >= foldTime) Enter(power.Running ? Phase.Folded : Phase.Resting);
                break;
            case Phase.Resting:
                unfold = Mathf.MoveTowards(unfold, 0f, dt / foldTime);
                nozzle.Set(0f);
                joints.Set(.2f);
                if (power.Running && unfold <= 0f) StartCycle();
                break;
        }
        float kick = phase == Phase.Firing ? recoil * Mathf.Exp(-phaseTime * 14f) : 0f;
        ApplyPose(PropHazardKit.Smooth(unfold), kick);
    }

    private void ApplyPose(float t, float kick = 0f)
    {
        if (shoulder != null) shoulder.localRotation = shoulderHome * Quaternion.Euler(0f, 0f, shoulderUnfold * t + kick);
        if (forearm != null) forearm.localRotation = forearmHome * Quaternion.Euler(0f, 0f, forearmUnfold * t - kick);
    }

    // ── 배치별 설정 ─────────────────────────────

    private WaveDeviceParameter IdleParameter => new WaveDeviceParameter(IdleKey, "대기 시간", "팔이 접힌 채 쉬는 시간입니다.", "초", .2f, 20f, .1f, idleTime);
    private WaveDeviceParameter ChargeParameter => new WaveDeviceParameter(ChargeKey, "예고 시간", "팔이 펼쳐진 뒤 노즐이 붉게 충전되는 시간입니다. 끝나면 레이저를 쏩니다.", "초", .2f, 5f, .05f, chargeTime);
    private WaveDeviceParameter OffsetParameter => new WaveDeviceParameter(OffsetKey, "시작 위상", "주기를 이 시간만큼 앞당겨 시작합니다(대기 시간 안에서). 여러 팔을 엇갈리게 둘 때 씁니다.", "초", 0f, 30f, .1f, phaseOffset);

    public WaveDeviceParameter[] PropParameters => new[]
    {
        WaveTrapDamage.DamageParameter(damage), IdleParameter, ChargeParameter, OffsetParameter, PropHazardKit.PowerParameter(powerMode),
    };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        float oldOffset = activeOffset;
        ReadSettings(overrides);
        power.Mode = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, PropHazardKit.PowerParameter(powerMode)));
        if (live && phase == Phase.Folded && Shots == 0 && !Mathf.Approximately(oldOffset, activeOffset)) StartCycle();   // 생성 직후 배치값이 오면 위상만 맞춘다
    }

    private void ReadSettings(WaveDeviceSetting[] overrides)
    {
        activeDamage = WaveDeviceSettings.Value(overrides, WaveTrapDamage.DamageParameter(damage));
        activeIdle = WaveDeviceSettings.Value(overrides, IdleParameter);
        activeCharge = WaveDeviceSettings.Value(overrides, ChargeParameter);
        activeOffset = WaveDeviceSettings.Value(overrides, OffsetParameter);
        emitterSettings = new[] { new WaveDeviceSetting("damage", activeDamage) };
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        // 편집기에는 노즐 앞 12칸을 보인다(실제로는 고체 벽에 막힐 때까지 뻗는다).
        float start = emitter != null ? transform.InverseTransformPoint(emitter.transform.position).x : .9f;
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, new Vector2(start + 6f, 0f), new Vector2(12f, .3f), new Color(1f, .22f, .16f, .9f));
        return true;
    }
}
