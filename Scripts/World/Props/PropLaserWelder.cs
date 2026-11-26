using System;
using UnityEngine;

// Prop 레이저 계열(6차 T6에서 하나로 합침): 레이저 용접 팔(N06, 2×2 벽 함정)과 소형 레이저 사출기(DungeonPropLaserEmitter, 1×1·4방향).
// 접힌 채 대기 → 펼침·노즐 붉은 충전(예고) → 레이저 발사 → 접힘을 되풀이한다. 예전 Wave 환경 장치 LaserEmitterCell을 대신한다.
// - 레이저 판정은 새로 만들지 않는다: 자식 'Emitter'의 기존 LaserSpawner(EnvironmentLaserBeam, Platform에 막힘, Player에만 피해)를
//   발사 순간마다 한 번 장전해(OnSpawnedFromPool, creationCycle 0·spawnOnce) 쏘게 한다.
//   Emitter에는 칸을 차지하지 않는 WaveEnvironmentTool 표식이 있어 LaserSpawner가 빔에 WaveTrapDamage 경로를 단다.
//   피해량(과 소형 사출기의 '시작 위치 복귀')은 배치 설정을 Emitter에 그대로 넘긴다. 던전 방 신호(IDungeonRoomHazard)를 받은 뒤에는
//   복귀를 넘기지 않는다(던전 방은 Wave 시작 좌표와 다르다).
// - 발사 방향은 Emitter LaserSpawner의 로컬 방향이라 Prop 회전을 따른다(용접 팔: 회전 0 = 오른쪽, 소형 사출기: 회전 0 = 위·1 = 왼쪽·2 = 아래·3 = 오른쪽).
// - 기계 동작(크기 변형 없음): 두 관절(용접 팔은 어깨·팔뚝, 소형 사출기는 좌우 덮개)이 회전으로 펼쳐지고 접힌다. 소형 사출기는 노즐 머리(slider)가
//   레일을 따라 밀려 올라오고, 등장할 때 하우징이 받침판에서 올라오며 양옆 고정 쇠가 회전해 물린다(deploy — 0이면 없음).
//   노즐 렌즈만 빨강(피해 부위), 충전 동안 밝아지고 깜박인다.
// - 박자(배치 설정): 대기 welderIdle · 예고(충전) welderCharge · 발사 welderFire · 시작 위상 welderOffset.
//   주기 = 대기 + 펼침 + 충전 + 발사 + 접힘. 발사 시간은 Emitter 빔 길이(beamDuration)를 넘지 못하고, 짧게 두면 그 시각에 빔을 거둔다.
//   등장 직후 첫 주기는 startWait만큼 더 쉰다(예전 사출기의 조립 + 첫 주기 대기와 같은 첫 발사 시각). 시작 위상은 그 첫 대기 안에서 앞당긴다.
// - 6차 T9(IDungeonRoomHazard): 방이 클리어되면 같은 호출 안에서 빔·예고선을 즉시 거두고(판정 끔) 접힌 채 쉰다(노즐·표시등 Renderer 끔).
//   instant면 바로 접힌 자세. 신호가 없으면(던전 밖) 늘 작동한다. 빔 즉시 거두기는 Emitter GameObject를 껐다 켜서
//   기존 LaserSpawner.OnDisable 경로(쏜 빔 회수·예고선 숨김)를 쓴다(LaserSpawner.cs 무수정).
// - 소리(기존 효과음만): 용접 팔은 충전 EnemyWindupWarning·발사 LaserFlashCrack, 소형 사출기는 빔 자체 소리만(예전 사출기와 같다 — 생성 도구가 비움).
// - 예고선: 충전(Charging) 동안 늘 Emitter LaserSpawner의 예고선을 보인다(발사와 같은 경로, 얇게 깜박이다 발사 직전 굵어짐).
//   예고선은 실행 중에 만든다(Prefab 무수정). 충전이 아닌 단계에 들어가면 바로 숨긴다.
[DisallowMultipleComponent]
public sealed class PropLaserWelder : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings, IDungeonRoomHazard
{
    // Resting = 방 클리어로 멈춘 상태(접힌 채 쉼). Deploying = 소형 사출기의 등장(조립) — 순서 값은 직렬화하지 않는다.
    public enum Phase { Folded, Unfolding, Charging, Firing, Folding, Resting, Deploying }

    public const string IdleKey = "welderIdle";
    public const string ChargeKey = "welderCharge";
    public const string FireKey = "welderFire";
    public const string OffsetKey = "welderOffset";

    [Header("배치 기본값")]
    [SerializeField, Min(.05f), Tooltip("접힌 채 쉬는 시간(초).")] private float idleTime = 2.4f;
    [SerializeField, Min(.1f), Tooltip("펼친 뒤 노즐이 충전되는 예고 시간(초).")] private float chargeTime = .9f;
    [SerializeField, Min(0f), Tooltip("주기 시작 위치(초). 여러 개를 엇갈리게 둔다.")] private float phaseOffset;
    [SerializeField, Min(0f)] private float damage = 10f;
    [SerializeField, Tooltip("배치 설정에 '피격 시 시작 위치 복귀'를 보인다(소형 사출기 — 기본 진행 Wave용). 던전 방에서는 켜 두어도 쓰지 않는다.")]
    private bool offerReturnToStart;

    [Header("기계 동작")]
    [SerializeField, Tooltip("첫 관절(용접 팔 어깨 · 소형 사출기 왼쪽 덮개).")] private Transform shoulder;
    [SerializeField, Tooltip("둘째 관절(용접 팔 팔뚝 · 소형 사출기 오른쪽 덮개).")] private Transform forearm;
    [SerializeField, Tooltip("첫 관절을 펼칠 때 접힌 자세에 더하는 각도(도).")] private float shoulderUnfold = -75f;
    [SerializeField, Tooltip("둘째 관절을 펼칠 때 접힌 자세에 더하는 각도(도).")] private float forearmUnfold = 150f;
    [SerializeField, Tooltip("펼칠 때 함께 밀려 나오는 부품(소형 사출기의 노즐 머리). 비우면 없음.")] private Transform slider;
    [SerializeField, Tooltip("펼친 자세에서 slider가 접힌 자리에서 옮겨 가는 거리(부모 좌표).")] private Vector2 slideUnfold;
    [SerializeField, Min(.05f)] private float unfoldTime = .35f;
    [SerializeField, Min(.05f)] private float foldTime = .4f;
    [SerializeField, Min(.1f), Tooltip("기본 발사 시간(초). 배치 설정 welderFire의 기본값이다.")] private float fireTime = 1.2f;
    [SerializeField, Min(.1f), Tooltip("Emitter LaserSpawner의 duration(빔 한 번의 길이 — 생성 도구가 같게 맞춘다). 발사 시간은 이 값을 넘지 못한다.")]
    private float beamDuration = 1.2f;
    [SerializeField, Tooltip("빔이 나가는 방향(Prop 좌표, 편집기 범위 표시용). 실제 방향은 Emitter LaserSpawner 설정이다.")] private Vector2 beamAxis = Vector2.right;
    [SerializeField, Min(0f), Tooltip("발사 순간 관절이 뒤로 밀리는 각도(도).")] private float recoil = 3f;

    [Header("등장(조립) — deployTime 0이면 없음")]
    [SerializeField, Min(0f), Tooltip("풀에서 꺼낸 뒤 부품이 조립되는 시간(초). 그동안은 쏘지 않는다.")] private float deployTime;
    [SerializeField, Tooltip("등장할 때 받침에서 밀려 나오는 몸체.")] private Transform deployBody;
    [SerializeField, Tooltip("등장 시작 때 deployBody가 완성 자리에서 떨어져 있는 거리(부모 좌표).")] private Vector2 deployOffset;
    [SerializeField, Tooltip("등장할 때 회전으로 물리는 부품(고정 쇠).")] private Transform[] deployJoints = Array.Empty<Transform>();
    [SerializeField, Tooltip("deployJoints마다 등장 시작 때 완성 자세에 더해 둔 각도(도).")] private float[] deployAngles = Array.Empty<float>();
    [SerializeField, Min(0f), Tooltip("등장 직후 첫 주기에만 더 쉬는 시간(초). 시작 위상은 이 첫 대기 안에서 앞당긴다.")] private float startWait;

    [Header("구성(생성 도구가 연결)")]
    [SerializeField] private LaserSpawner emitter;
    [SerializeField, Tooltip("노즐 렌즈(붉은 위험부).")] private Renderer[] nozzleGlow = Array.Empty<Renderer>();
    [SerializeField, Tooltip("관절·상태 표시등(흰색, 동작 중 밝게, 멈추면 끔).")] private Renderer[] jointLights = Array.Empty<Renderer>();
    [SerializeField] private ParticleSystem chargeSparks;
    [SerializeField] private ParticleSystem muzzleSparks;
    [SerializeField, Min(0f)] private float idleNozzleGlow = .2f;
    [SerializeField, Min(0f)] private float chargeNozzleGlow = 1.8f;
    [SerializeField, Min(0f)] private float fireNozzleGlow = 2.4f;

    [Header("효과(기존 효과음 — 비우면 소리 없음)")]
    [SerializeField] private SoundOptions chargeSound;
    [SerializeField] private SoundOptions fireSound;

    private HazardLights nozzle, joints;
    private Quaternion shoulderHome, forearmHome;
    private Vector3 sliderHome, deployHome;
    private Quaternion[] deployJointHome = Array.Empty<Quaternion>();
    private float activeIdle, activeCharge, activeFire, activeOffset, activeDamage;
    private bool activeReturn;
    private WaveDeviceSetting[] emitterSettings = WaveDeviceSettings.None;
    private Phase phase = Phase.Resting;
    private float phaseTime;
    private float foldedWait;  // 지금 접힘 대기의 길이(첫 주기면 startWait 포함)
    private bool firstWait;    // 지금 접힘 대기가 등장 직후 첫 대기인지
    private float unfold;      // 0 접힘 ~ 1 펼침
    private bool cached;
    private bool live;
    private bool halted;       // 방 클리어로 멈춤(IDungeonRoomHazard)
    private bool hazardSignal; // 이번 대여에서 던전 방 신호를 받았는지(받았으면 던전 방 안 — 시작 위치 복귀를 넘기지 않는다)
    private float haltDelay;   // 6차 T9 정전 물결: 외형(접힘·표시등 끔)을 늦추는 남은 시간(판정은 신호 순간 끈다 — DungeonHazardHalt.VisualDelay)

    public Phase CurrentPhase => phase;
    /// <summary>이번 대여에서 레이저를 장전해 쏜 횟수(검사용).</summary>
    public int Shots { get; private set; }
    public float LastShotTime { get; private set; } = -1f;
    public float LastChargeTime { get; private set; } = -1f;
    /// <summary>방 클리어로 빔을 거둔 마지막 시각(검사용, 없으면 -1).</summary>
    public float LastRetractTime { get; private set; } = -1f;
    public LaserSpawner Emitter => emitter;
    /// <summary>방 클리어 신호로 멈춰 있는지.</summary>
    public bool IsHalted => halted;
    /// <summary>Emitter 빔 한 번의 길이(초) = 발사 시간의 상한.</summary>
    public float BeamDuration => beamDuration;
    /// <summary>지금 적용된 발사 시간(초). 시작 위치 복귀가 켜져 있으면 빔 길이 전체(끊으면 복귀가 취소되므로).</summary>
    public float FireTime => ReturnInEffect ? beamDuration : Mathf.Min(activeFire, beamDuration);
    public float DeployTime => deployTime;
    public float StartWait => startWait;
    /// <summary>한 주기(접힘 대기 + 펼침 + 충전 + 발사 + 접힘) 길이(초).</summary>
    public float Period => activeIdle + unfoldTime + activeCharge + FireTime + foldTime;
    /// <summary>등장 시작부터 첫 발사까지(위상 0일 때, 초).</summary>
    public float FirstShotDelay => deployTime + startWait + activeIdle + unfoldTime + activeCharge;

    private bool ReturnInEffect => offerReturnToStart && activeReturn && !hazardSignal;

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
        if (slider != null) sliderHome = slider.localPosition;
        if (deployBody != null) deployHome = deployBody.localPosition;
        deployJointHome = new Quaternion[deployJoints != null ? deployJoints.Length : 0];
        for (int i = 0; i < deployJointHome.Length; i++) deployJointHome[i] = deployJoints[i] != null ? deployJoints[i].localRotation : Quaternion.identity;
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
        // 지난 대여에서 장전만 되고 쏘지 못한 발사(Fire 직후 반환)를 푼다. ObjectSpawner.spawning은 반환 뒤에도 남으므로
        // 주기를 아주 길게 둬 막는다. 다음 Fire가 creationCycle을 0으로 되돌린다(RetractBeam과 같은 방식).
        if (emitter != null) emitter.creationCycle = 1e9f;
        live = true;
        halted = false;
        hazardSignal = false;
        haltDelay = 0f;
        Shots = 0;
        LastShotTime = LastChargeTime = LastRetractTime = -1f;
        unfold = 0f;
        ApplyPose(0f);
        RebuildEmitterSettings();
        PropHazardKit.Stop(chargeSparks, true);
        PropHazardKit.Stop(muzzleSparks, true);
        if (emitter != null) emitter.HideWarning();
        if (emitter != null && emitter.entityBase != null && WorldEntityPool.Instance != null) WorldEntityPool.Instance.Prewarm(emitter.entityBase);
        if (deployTime > 0f)
        {
            ApplyDeploy(0f);
            Enter(Phase.Deploying);
            nozzle.Set(0f);
            joints.Set(0f);
        }
        else
        {
            ApplyDeploy(1f);
            StartCycle(true);
        }
    }

    public void OnPropReturning(DungeonProp prop)
    {
        live = false;
        if (emitter != null) emitter.creationCycle = 1e9f;   // 장전만 되고 아직 쏘지 않은 발사가 풀 안에 남지 않게
        unfold = 0f;
        ApplyPose(0f);
        ApplyDeploy(1f);
        nozzle.Restore();
        joints.Restore();
        PropHazardKit.Stop(chargeSparks, true);
        PropHazardKit.Stop(muzzleSparks, true);
        if (emitter != null) emitter.HideWarning();
        // Emitter가 쏜 빔은 루트가 꺼질 때 LaserSpawner.OnDisable이 거둔다.
    }

    // ── IDungeonRoomHazard(6차 T9) ─────────────────────────────

    public void SetHazardHalted(bool halted, bool instant)
    {
        Cache();
        bool firstSignal = !hazardSignal;
        hazardSignal = true;
        this.halted = halted;
        if (firstSignal)
        {
            // 던전 방 안 — 시작 위치 복귀를 빼고 Emitter에도 바로 넘긴다(이미 쏜 빔은 쏠 때의 값을 그대로 쓴다).
            RebuildEmitterSettings();
            if (emitter != null) emitter.ApplySettings(emitterSettings);
        }
        if (!live) return;
        if (halted)
        {
            // 판정은 같은 호출 안에서 끈다: 쏜 빔·예고선을 거둔다. 외형은 접힘 속도(foldTime)로 접히고 노즐·표시등은 끈다.
            RetractBeam();
            if (emitter != null) emitter.HideWarning();
            PropHazardKit.Stop(chargeSparks, instant);
            PropHazardKit.Stop(muzzleSparks, instant);
            if (phase == Phase.Deploying) ApplyDeploy(1f);
            Enter(Phase.Resting);
            // 정전 물결(DungeonHazardHalt.HaltWave): 외형(접힘·노즐·관절 표시등)만 차례가 올 때까지 그대로 둔다. instant·직접 호출이면 0.
            haltDelay = instant ? 0f : DungeonHazardHalt.VisualDelay;
            if (haltDelay <= 0f)
            {
                nozzle.Set(0f);
                joints.Set(0f);
            }
            if (instant)
            {
                unfold = 0f;
                ApplyPose(0f);
            }
            LastRetractTime = Time.time;
        }
        else
        {
            haltDelay = 0f;
            if (phase == Phase.Resting) StartCycle(true);
        }
    }

    // 쏜 빔을 즉시 거둔다. LaserSpawner(CP949)를 고치지 않고 Emitter를 껐다 켜서 기존 OnDisable(쏜 빔 회수·예고선 숨김·복귀 취소)을 쓴다.
    // Emitter의 cleanupSpawnedBeams가 켜져 있어야 한다(생성 도구가 켠다).
    private void RetractBeam()
    {
        if (emitter == null) return;
        // 이번 프레임에 장전만 되고 아직 쏘지 않은 발사도 막는다(다음 장전 Fire가 creationCycle을 0으로 되돌린다).
        emitter.creationCycle = 1e9f;
        var go = emitter.gameObject;
        if (!go.activeInHierarchy) return;
        go.SetActive(false);
        go.SetActive(true);
    }

    // 위상(phaseOffset)만큼 주기를 앞당겨 시작한다. 쉬는 구간 안이면 그만큼 덜 쉰다. first면 등장 직후 첫 대기(startWait 포함)다.
    private void StartCycle(bool first)
    {
        if (halted) { Enter(Phase.Resting); return; }
        Enter(Phase.Folded);
        firstWait = first;
        foldedWait = activeIdle + (first ? startWait : 0f);
        phaseTime = Mathf.Repeat(activeOffset, Mathf.Max(.01f, Period));
        if (phaseTime > foldedWait) phaseTime = foldedWait;   // 접힘 대기 밖의 위상은 대기 끝으로 맞춘다(갑자기 발사하지 않게)
    }

    private void Enter(Phase next)
    {
        phase = next;
        phaseTime = 0f;
        if (next == Phase.Folded)
        {
            foldedWait = activeIdle;
            firstWait = false;
        }
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
            case Phase.Deploying:
                PropHazardKit.Stop(chargeSparks);
                PropHazardKit.Stop(muzzleSparks);
                break;
        }
    }

    // 단계 끝에서 넘친 시간을 다음 단계로 넘긴다(프레임마다 잃는 시간이 쌓여 주기가 늘어나지 않게).
    private void Advance(Phase next, float threshold)
    {
        float over = Mathf.Max(0f, phaseTime - threshold);
        Enter(next);
        phaseTime = over;
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
            case Phase.Deploying:
            {
                float k = Mathf.Clamp01(phaseTime / Mathf.Max(.01f, deployTime));
                ApplyDeploy(PropHazardKit.Smooth(k));
                unfold = 0f;
                nozzle.Set(0f);
                joints.Set(Mathf.Repeat(phaseTime, .16f) < .08f ? 1f : .15f);   // 조립 중 상태 확인 깜박임
                if (k >= 1f)
                {
                    float over = Mathf.Max(0f, phaseTime - deployTime);   // 넘친 시간은 첫 대기에서 뺀다(첫 발사 시각이 프레임마다 밀리지 않게)
                    ApplyDeploy(1f);
                    StartCycle(true);
                    if (phase == Phase.Folded) phaseTime = Mathf.Min(phaseTime + over, foldedWait);
                }
                break;
            }
            case Phase.Folded:
                unfold = 0f;
                nozzle.Set(idleNozzleGlow);
                joints.Set(.35f);
                if (halted) Enter(Phase.Resting);
                else if (phaseTime >= foldedWait) Advance(Phase.Unfolding, foldedWait);
                break;
            case Phase.Unfolding:
                unfold = Mathf.Clamp01(phaseTime / unfoldTime);
                nozzle.Set(Mathf.Lerp(idleNozzleGlow, chargeNozzleGlow * .6f, unfold));
                joints.Set(1f);
                if (phaseTime >= unfoldTime) Advance(Phase.Charging, unfoldTime);
                break;
            case Phase.Charging:
            {
                unfold = 1f;
                float k = Mathf.Clamp01(phaseTime / Mathf.Max(.01f, activeCharge));
                float period = Mathf.Lerp(.22f, .07f, k);                          // 충전이 끝날수록 빨리 깜박인다
                bool on = Mathf.Repeat(phaseTime, period) < period * .55f;
                nozzle.Set(chargeNozzleGlow * Mathf.Lerp(.55f, 1f, k) * (on ? 1f : .55f));
                joints.Set(1f);
                if (phaseTime >= activeCharge) Advance(Phase.Firing, activeCharge);
                else if (emitter != null) emitter.ShowWarning(phaseTime, Mathf.Max(.01f, activeCharge));
                break;
            }
            case Phase.Firing:
            {
                unfold = 1f;
                nozzle.Set(fireNozzleGlow);
                joints.Set(1f);
                float fire = FireTime;
                if (phaseTime >= fire)
                {
                    // 발사 시간을 빔 길이보다 짧게 두었으면 그 시각에 빔을 거둔다(빔은 스스로 beamDuration 동안 남는다).
                    if (fire < beamDuration - .01f) RetractBeam();
                    Advance(Phase.Folding, fire);
                }
                break;
            }
            case Phase.Folding:
                unfold = 1f - Mathf.Clamp01(phaseTime / foldTime);
                nozzle.Set(Mathf.Lerp(idleNozzleGlow, fireNozzleGlow * .4f, unfold));
                joints.Set(.6f);
                if (phaseTime >= foldTime)
                {
                    if (halted) Enter(Phase.Resting);
                    else Advance(Phase.Folded, foldTime);
                }
                break;
            case Phase.Resting:
                if (haltDelay > 0f)
                {
                    // 정전 물결 차례 전: 판정은 이미 꺼졌고 외형만 그대로 둔다.
                    haltDelay -= dt;
                    break;
                }
                unfold = Mathf.MoveTowards(unfold, 0f, dt / foldTime);
                nozzle.Set(0f);
                joints.Set(0f);
                if (!halted && unfold <= 0f) StartCycle(true);
                break;
        }
        float kick = phase == Phase.Firing ? recoil * Mathf.Exp(-phaseTime * 14f) : 0f;
        ApplyPose(PropHazardKit.Smooth(unfold), kick);
    }

    private void ApplyPose(float t, float kick = 0f)
    {
        if (shoulder != null) shoulder.localRotation = shoulderHome * Quaternion.Euler(0f, 0f, shoulderUnfold * t + kick);
        if (forearm != null) forearm.localRotation = forearmHome * Quaternion.Euler(0f, 0f, forearmUnfold * t - kick);
        if (slider != null) slider.localPosition = sliderHome + (Vector3)(slideUnfold * t);
    }

    // d = 0 등장 시작 자세(몸체 내려감·고정 쇠 벌어짐) ~ 1 완성 자세(Prefab 저장 자세).
    private void ApplyDeploy(float d)
    {
        if (deployBody != null) deployBody.localPosition = deployHome + (Vector3)(deployOffset * (1f - d));
        for (int i = 0; i < deployJointHome.Length; i++)
        {
            if (deployJoints[i] == null) continue;
            float angle = i < deployAngles.Length ? deployAngles[i] : 0f;
            deployJoints[i].localRotation = deployJointHome[i] * Quaternion.Euler(0f, 0f, angle * (1f - d));
        }
    }

    // ── 배치별 설정 ─────────────────────────────

    private float FireMax => Mathf.Max(.2f, beamDuration);
    private WaveDeviceParameter IdleParameter => new WaveDeviceParameter(IdleKey, "대기 시간", "접힌 채 쉬는 시간입니다.", "초", .05f, 20f, .05f, idleTime);
    private WaveDeviceParameter ChargeParameter => new WaveDeviceParameter(ChargeKey, "예고 시간", "펼쳐진 뒤 노즐이 붉게 충전되며 예고선을 보이는 시간입니다. 끝나면 레이저를 쏩니다.", "초", .1f, 5f, .05f, chargeTime);
    private WaveDeviceParameter FireParameter => new WaveDeviceParameter(FireKey, "발사 시간", "레이저를 쏘는 시간입니다. 빔 한 번의 길이(" + FireMax.ToString("0.##") + "초)를 넘지 못하고, 짧게 두면 그 시각에 빔을 거둡니다. 시작 위치 복귀를 켜면 빔 길이 전체를 씁니다.",
        "초", .2f, FireMax, .05f, Mathf.Clamp(fireTime, .2f, FireMax));
    private WaveDeviceParameter OffsetParameter => new WaveDeviceParameter(OffsetKey, "시작 위상", "주기를 이 시간만큼 앞당겨 시작합니다(첫 대기 안에서). 여러 개를 엇갈리게 둘 때 씁니다.", "초", 0f, 30f, .025f, phaseOffset);

    public WaveDeviceParameter[] PropParameters => offerReturnToStart
        ? new[] { WaveTrapDamage.DamageParameter(damage), IdleParameter, ChargeParameter, FireParameter, OffsetParameter, WaveTrapDamage.ReturnParameter }
        : new[] { WaveTrapDamage.DamageParameter(damage), IdleParameter, ChargeParameter, FireParameter, OffsetParameter };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        float oldOffset = activeOffset, oldIdle = activeIdle;
        ReadSettings(overrides);
        // Emitter의 함정 피해 설정도 바로 맞춘다(발사 때마다 다시 넘기지만, 설정만 바꾼 상태에서도 Emitter 값이 배치 값과 같게).
        if (emitter != null) emitter.ApplySettings(emitterSettings);
        // 생성 직후 배치값이 오면 첫 주기를 그 값으로 다시 잡는다(대기 시간·위상 — 첫 대기 길이는 StartCycle에서 정해지므로 대기 시간이 바뀌어도 다시 잡아야 한다).
        if (live && phase == Phase.Folded && Shots == 0
            && (!Mathf.Approximately(oldOffset, activeOffset) || !Mathf.Approximately(oldIdle, activeIdle))) StartCycle(firstWait);
    }

    private void ReadSettings(WaveDeviceSetting[] overrides)
    {
        activeDamage = WaveDeviceSettings.Value(overrides, WaveTrapDamage.DamageParameter(damage));
        activeIdle = WaveDeviceSettings.Value(overrides, IdleParameter);
        activeCharge = WaveDeviceSettings.Value(overrides, ChargeParameter);
        activeFire = WaveDeviceSettings.Value(overrides, FireParameter);
        activeOffset = WaveDeviceSettings.Value(overrides, OffsetParameter);
        activeReturn = offerReturnToStart && WaveDeviceSettings.Value(overrides, WaveTrapDamage.ReturnParameter) > .5f;
        RebuildEmitterSettings();
    }

    private void RebuildEmitterSettings()
    {
        emitterSettings = ReturnInEffect
            ? new[] { new WaveDeviceSetting("damage", activeDamage), new WaveDeviceSetting("returnToStart", 1f) }
            : new[] { new WaveDeviceSetting("damage", activeDamage) };
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        // 편집기에는 노즐 앞 12칸을 보인다(실제로는 고체 벽에 막힐 때까지 뻗는다).
        Vector2 axis = beamAxis.sqrMagnitude > .0001f ? beamAxis.normalized : Vector2.right;
        Vector2 start = emitter != null ? (Vector2)transform.InverseTransformPoint(emitter.transform.position) : axis * .9f;
        var size = Mathf.Abs(axis.x) >= Mathf.Abs(axis.y) ? new Vector2(12f, .3f) : new Vector2(.3f, 12f);
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, start + axis * 6f, size, new Color(1f, .22f, .16f, .9f));
        return true;
    }
}
