using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

// 낙석 구조물(7차 V1 — DESIGN_V7 §1.1, 폐자재 슈트를 대신하는 한 번만 떨어지는 천장 함정).
// 천장 레일 호이스트의 사슬 두 줄에 낮게 매단 4×4 압축 고철 블록이다.
//   대기(Armed) → Player 몸통이 낙하 열 둘레 감지 사각형에 들면 진동(Trembling: 사슬 덜컹·블록 흔들림·볼트 튐·먼지/자갈·붉은 램프 깜박임·착지점 예고선·지속 카메라 흔들림)
//   → 사슬이 끊어져 낙하(Falling — 가속·최고 속도) → 착지하면 파편으로 부서져 사라진다(Shattered — 지형은 바뀌지 않는다). 이번 대여 동안 다시 작동하지 않는다.
// - 물리 판정은 루트 RaycastMotor2D(정적 질의 전용 — 몸통 없음)만 쓴다.
//   감지 = Player 몸통 Collider.bounds(PropHazardKit.TryPlayerBody — 물리 질의 없음) ∩ 감지 사각형(블록 폭 ± 감지 여유 × 착지면 ~ 블록 아랫변).
//   착지 = RaycastEnvironment(아래, Platform)를 블록 폭 안 4열에서 쏴 가장 가까운 받침(강체 블록이라 모든 열이 같이 멈춘다). OneWay는 지나간다(R1 — 모터 무수정, 템플릿 검사가 배치를 막는다).
//   피해 = 지난 판정 아랫변 ~ 지금 아랫변을 쓴 상자(폭 = 블록 폭 − 양쪽 여유)를 OverlapAttackBox(PropHazardKit.OverlapBox)로 묻는다.
//   사슬 길이 = 꺼낼 때 블록 윗변에서 위로 RaycastEnvironment(Platform)를 사슬 열 2곳에서 쏴 천장까지 맞춘다(호이스트를 옮기고 사슬은 고리를 이어 붙인 Mesh를 새로 채운다 — 늘이지 않는다).
//   위치를 옮기는 것은 표시 부품(블록·호이스트·사슬·예고선·입자)뿐이다. Scale은 쓰지 않는다(회전·이동만).
// - 피해(대상당 1회): Player = WaveTrapDamage(복귀 없음), 적 = WeaponTraitEffects.ApplyEnemyHit(쉴드 자동 — 등장 연출 중인 적은 뺀다),
//   파괴형 Prop(DungeonPropTargeting.IsProp) = 부순다(쓸기 상자 + 착지 순간 블록 아래로 crushDepth만큼 늘린 상자의 받침 Prop).
// - 방 클리어 정지(IDungeonRoomHazard): 감지·피해를 그 호출에서 끄고 지속 흔들림을 닫는다. 외형은 정전 물결 차례(DungeonHazardHalt.VisualDelay)가 오면
//   대기 = 위험 띠·램프를 끈 매달림, 진동 중 = haltSettle초 안에 제자리 자세로 돌아간 뒤 불 꺼진 매달림, 낙하 중 = 그 자리에서 부서짐(피해 없음 — Broken도 낸다), 부서짐 = 할 일 없음.
//   신호가 없으면(던전 밖) 늘 작동한다. 정지를 풀면(재도전·풀 재사용) 대기일 때만 감지를 다시 켠다.
// - 재입장: IDungeonBreakable(Broken = 블록이 부서져 사라진 순간)이라 방 단계(DungeonRoomStage.destroyedProps)가 기억해 그 던전에서 다시 세우지 않는다(호이스트도 함께).
//   일반 WaveData는 풀에서 다시 꺼내면 OnPropSpawned가 처음 상태(대기)로 되돌린다.
// - 지속 흔들림(FeedbackPlayer.BeginHeldShake)은 낙하 시작·정지 신호·부서짐·풀 반환·비활성·파괴 모든 경로에서 EndHeldShake로 닫는다.
// - 던전 컷씬(레버·문·도착 걸어 나오기 — DungeonCutsceneDirector.AnyPlaying) 동안에는 감지하지 않는다(자동 이동으로 발동하지 않게 — 판단값).
// - 소리(새 음원 없음 — 기존 효과음, 사용자 청취 전 판단값): 진동 시작·볼트 튐·사슬 끊어짐·충돌 겹침음. 충돌은 Bulwark 착지 Feedback의 소리와 파티클을 쓰고 카메라 흔들림을 더 강하게 낸다(거리 감쇠).
// - 매 프레임 할당 없음: 감지·피해 질의는 미리 잡은 배열·HashSet을 다시 쓰고, 사슬 Mesh는 꺼낼 때(길이가 바뀔 때)만 다시 채운다.
[DisallowMultipleComponent]
public sealed class PropRockfall : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings, IDungeonRoomHazard, IDungeonBreakable
{
    public enum Stage { Armed, Trembling, Falling, Shattered }

    public const string EnemyDamageKey = "rockEnemyDamage";
    public const string WarningKey = "rockWarning";
    public const string TriggerRangeKey = "rockTriggerRange";
    public const string GraceKey = "rockGrace";

    [Header("배치 기본값")]
    [SerializeField, Min(0f)] private float damage = 20f;
    [SerializeField, Min(0f)] private float enemyDamage = 80f;
    [SerializeField, Min(.5f), Tooltip("감지 뒤 진동(예고) 시간(초). 끝나면 사슬이 끊어져 떨어진다.")] private float warningTime = .9f;
    [SerializeField, Min(0f), Tooltip("낙하 열(블록 폭) 좌우로 더 넓혀 Player 몸통을 감지하는 거리(칸).")] private float triggerRange = 1.25f;
    [SerializeField, Min(0f), Tooltip("꺼낸 뒤 감지하지 않는 시간(초) — 출입구 바로 옆 배치의 즉시 발동 방지.")] private float grace = .8f;

    [Header("블록·낙하")]
    [SerializeField, Tooltip("블록 반 크기(칸, 루트 기준 — footprint 4×4의 절반). 블록 아랫변 = 루트 − y.")] private Vector2 blockHalf = new Vector2(2f, 2f);
    [SerializeField, Min(1f), Tooltip("낙하 가속(칸/초²).")] private float gravity = 34f;
    [SerializeField, Min(1f), Tooltip("최고 낙하 속도(칸/초).")] private float maxFallSpeed = 24f;
    [SerializeField, Min(1f), Tooltip("받침을 못 찾을 때 떨어지는 거리(칸).")] private float maxDrop = 40f;
    [SerializeField, Tooltip("착지 받침을 찾는 열(루트 기준 x). 강체 블록이라 가장 가까운 받침에서 모든 열이 멈춘다.")] private float[] landingColumns = { -1.9f, -.6f, .6f, 1.9f };
    [SerializeField, Min(0f), Tooltip("피해 상자를 블록 폭보다 양쪽으로 줄이는 양(칸) — 맞닿은 이웃 칸을 잡지 않게.")] private float damageInset = .1f;
    [SerializeField, Min(0f), Tooltip("피해 상자가 블록 아랫변 안쪽으로 들어가는 깊이(칸).")] private float sweepLip = .3f;
    [SerializeField, Min(0f), Tooltip("착지 순간 블록 아래로 늘려 받침 파괴형 Prop을 부수는 깊이(칸).")] private float crushDepth = .25f;
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private LayerMask enemyMask;
    [SerializeField] private LayerMask floorMask;

    [Header("호이스트·사슬(생성 도구가 연결)")]
    [SerializeField, Tooltip("천장을 찾는 최대 거리(칸, 블록 윗변 위 빈 칸 수 — 카탈로그 ceilingReach와 같은 값).")] private float ceilingReach = 14f;
    [SerializeField, Tooltip("호이스트 높이(칸): 사슬 고리 줄 ~ 레일 윗면(천장).")] private float hoistHeight = 1f;
    [SerializeField, Tooltip("Prefab 기본 모습의 천장 높이(블록 윗변 위 칸) — 천장을 못 찾으면 이 모습(사슬 토막)으로 둔다.")] private float defaultCeiling = 3f;
    [SerializeField, Tooltip("사슬 끝이 걸리는 블록 고리 구멍(블록 윗변 아래 깊이, 칸).")] private float chainAttachDepth = .15f;
    [SerializeField, Tooltip("천장을 찾는 사슬 열(루트 기준 x).")] private float[] chainColumns = { -1.2f, 1.2f };
    [SerializeField] private Transform block;
    [SerializeField] private Transform hoist;
    [SerializeField, Tooltip("사슬 부품(고리 줄에서 아래로 — 회전으로 흔들린다).")] private Transform[] chains = Array.Empty<Transform>();
    [SerializeField, Tooltip("사슬 부품마다 한 장의 MeshFilter(실행 중 길이대로 고리를 이어 붙인 Mesh로 바꾼다).")] private MeshFilter[] chainFilters = Array.Empty<MeshFilter>();
    [SerializeField, Tooltip("사슬 반 마디 길이(칸). 마디 = 정면 고리 반 마디 + 옆 고리 반 마디.")] private float chainHalfLength = .5f;
    [SerializeField, HideInInspector] private Vector3[] chainUnitVertices = Array.Empty<Vector3>();
    [SerializeField, HideInInspector] private Color[] chainUnitColors = Array.Empty<Color>();
    [SerializeField, HideInInspector] private int[] chainUnitTriangles = Array.Empty<int>();
    [SerializeField, HideInInspector, Tooltip("마디 정점·삼각형 중 정면 고리(앞 반 마디)의 끝 번호.")] private int chainVertexSplit;
    [SerializeField, HideInInspector] private int chainTriangleSplit;
    [SerializeField, Tooltip("진동 때 튀는 볼트(블록 고리 위).")] private Transform[] bolts = Array.Empty<Transform>();
    [SerializeField, Tooltip("호이스트 붉은 경고 램프(진동 때 깜박임).")] private Renderer[] dangerLamps = Array.Empty<Renderer>();
    [SerializeField, Tooltip("호이스트 흰 상태등.")] private Renderer[] statusLamps = Array.Empty<Renderer>();
    [SerializeField, Tooltip("착지점 예고선(착지면으로 옮긴다, 평소 꺼짐).")] private Transform marker;
    [SerializeField] private Renderer[] markerRenderers = Array.Empty<Renderer>();
    [SerializeField, Tooltip("진동 때 천장에서 떨어지는 먼지(호이스트 아래).")] private ParticleSystem chainDust;
    [SerializeField, Tooltip("진동 때 떨어지는 자갈.")] private ParticleSystem pebbles;
    [SerializeField, Tooltip("볼트가 튈 때 불꽃(볼트 자리로 옮긴다).")] private ParticleSystem boltSparks;
    [SerializeField, Tooltip("부서질 때 파편(착지선으로 옮긴다).")] private ParticleSystem impactDebris;
    [SerializeField, Tooltip("부서질 때 먼지.")] private ParticleSystem impactDust;
    [SerializeField, Tooltip("부서질 때 불꽃.")] private ParticleSystem impactSparks;

    [Header("움직임")]
    [SerializeField, Min(0f)] private float idleLampGlow = .25f;
    [SerializeField, Min(0f)] private float warningLampGlow = 1.6f;
    [SerializeField, Min(0f)] private float statusGlow = 1f;
    [SerializeField, Min(0f)] private float markerGlow = 1.2f;
    [SerializeField, Min(.05f)] private float blinkPeriod = .16f;
    [SerializeField, Tooltip("진동 때 블록 흔들림 각도(°, 회전만).")] private float trembleAngle = 2f;
    [SerializeField, Tooltip("진동 때 블록 흔들림 이동(칸).")] private float trembleShift = .05f;
    [SerializeField, Tooltip("진동 때 사슬 덜컹 각도(°).")] private float chainRattle = 1.2f;
    [SerializeField, Tooltip("진동 때 볼트 떨림 각도(°).")] private float boltJitter = 25f;
    [SerializeField, Tooltip("볼트가 튀는 시점(진동 시간 비율).")] private float[] boltPops = { .3f, .58f, .84f };
    [SerializeField, Min(.02f), Tooltip("진동 때 먼지·자갈을 내는 간격(초).")] private float dustInterval = .14f;
    [SerializeField, Tooltip("진동 동안 지속 카메라 흔들림 세기(BeginHeldShake 원래 값).")] private float heldShake = .07f;
    [SerializeField, Tooltip("낙하·부서짐 뒤 빈 사슬이 처음 흔들리는 각속도(°/초) — 감쇠 흔들림(회전만).")] private float releaseSwing = 70f;
    [SerializeField, Min(.05f), Tooltip("진동 중 정지 신호 때 제자리 자세로 돌아가는 시간(초).")] private float haltSettle = .25f;

    [Header("효과(기존 효과음 — 청취 후보)")]
    [SerializeField] private SoundOptions trembleSound;
    [SerializeField] private SoundOptions boltSound;
    [SerializeField] private SoundOptions releaseSound;
    [SerializeField] private SoundOptions impactLayerSound;
    [SerializeField] private FeedbackDataSO impactFeedback;
    [SerializeField, Min(0f), Tooltip("바닥에 닿을 때의 카메라 흔들림 배율")] private float impactShakeMultiplier = 1.8f;

    private readonly WaveTrapDamage trapDamage = new WaveTrapDamage();
    private readonly HazardHalt halt = new HazardHalt();
    private readonly HashSet<Component> hitThisDrop = new HashSet<Component>();
    private readonly List<Vector3> chainVertices = new List<Vector3>();
    private readonly List<Color> chainColors = new List<Color>();
    private readonly List<Vector2> chainUvs = new List<Vector2>();
    private readonly List<int> chainTriangles = new List<int>();
    private Collider2D[] hits = new Collider2D[16];
    private Collider2D[] pending = new Collider2D[16];
    private RaycastMotor2D motor;
    private HazardLights lamps, status, markerLights;
    private HazardRendererSwitch danger;
    private Mesh[] chainMeshes = Array.Empty<Mesh>();
    private int builtHalves = -1;
    private Vector3 blockHomePosition, hoistHomePosition, markerHome;
    private Quaternion blockHomeRotation = Quaternion.identity;
    private Quaternion[] chainHome = Array.Empty<Quaternion>();
    private Vector3[] boltHomePosition = Array.Empty<Vector3>();
    private Quaternion[] boltHomeRotation = Array.Empty<Quaternion>();
    private Vector3[] particleHome = Array.Empty<Vector3>();
    private ParticleSystem[] particles = Array.Empty<ParticleSystem>();
    private CinemachineImpulseManager.ImpulseEvent shake;
    private float activeDamage, activeEnemyDamage, activeWarning, activeRange, activeGrace;
    private Stage stage = Stage.Armed;
    private float stageTime;
    private float spawnTime;
    private float dropDistance;
    private float lastDamageDistance;
    private float landingY;
    private bool landingKnown;
    private Vector3 dropOrigin;
    private int nextPop;
    private float nextDust;
    private float swingAngle, swingVelocity;
    private bool swinging;
    private bool haltPosed;
    private Vector3 haltFromPosition;
    private Quaternion haltFromRotation = Quaternion.identity;
    private int hoistRetries;
    private bool cached;
    private bool live;

    public event Action<IDungeonBreakable> Broken;

    /// <summary>블록이 부서져 사라졌다(이번 대여). 방 단계가 Broken으로 기억해 그 던전에서 다시 세우지 않는다.</summary>
    public bool IsBroken { get; private set; }
    public Stage CurrentStage => stage;
    /// <summary>착지면(월드 y). 아직 구하지 않았으면 NaN(검사용).</summary>
    public float LandingY => landingKnown ? landingY : float.NaN;
    /// <summary>이번 대여에서 시작한 낙하 수(0 또는 1 — 검사용).</summary>
    public int Drops { get; private set; }
    /// <summary>이번 낙하에서 피해를 준(부순) 대상 수(검사용).</summary>
    public int HitsThisDrop { get; private set; }
    public int PlayerHits { get; private set; }
    public int EnemyHits { get; private set; }
    public int PropsCrushed { get; private set; }
    /// <summary>지난 진동이 시작된 Time.time(검사용, 없으면 -1).</summary>
    public float LastTrembleTime { get; private set; } = -1f;
    public float LastFallTime { get; private set; } = -1f;
    public float LastHitTime { get; private set; } = -1f;
    /// <summary>천장까지 맞춘 사슬 길이(칸, 고리 줄 ~ 블록 고리 구멍 — 검사용).</summary>
    public float ChainLength { get; private set; }
    /// <summary>꺼낸 뒤 천장을 찾았다(못 찾으면 Prefab 기본 사슬 토막).</summary>
    public bool CeilingFound { get; private set; }
    public bool IsRunning => live && !halt.Halted && (stage == Stage.Armed || stage == Stage.Trembling || stage == Stage.Falling);
    /// <summary>방 클리어 정지 신호를 받아 감지·피해가 꺼져 있다(검사용).</summary>
    public bool IsHalted => halt.Halted;
    public bool HeldShakeActive => shake != null;
    public float WarningTime => activeWarning;
    public bool MarkerVisible => AnyEnabled(markerRenderers);
    public bool LampsLit => AnyEnabled(dangerLamps);
    public bool DangerVisible => BlockVisible && danger != null && danger.IsOn && AnyEnabled(danger.Renderers);
    public bool BlockVisible => block != null && block.gameObject.activeSelf;
    /// <summary>정지 자세: 정지 신호 중, 매달린 대기 자세(블록 제자리), 위험 띠·램프·예고선 꺼짐(검사용).</summary>
    public bool InHaltedPose => halt.Halted && haltPosed && stage == Stage.Armed && !DangerVisible && !LampsLit && !MarkerVisible && BlockAtHome;
    public bool BlockAtHome => block == null || (block.localPosition - blockHomePosition).sqrMagnitude < 1e-6f && Quaternion.Angle(block.localRotation, blockHomeRotation) < .05f;

    private void Awake()
    {
        Cache();
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();   // 정적 질의만 쓴다(몸통 없음)
        if (playerMask.value == 0) playerMask = LayerMask.GetMask("Player");
        if (enemyMask.value == 0) enemyMask = LayerMask.GetMask("Enemy");
        if (floorMask.value == 0) floorMask = LayerMask.GetMask("Platform");
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        ReadSettings(WaveDeviceSettings.None);
        lamps = new HazardLights(dangerLamps);
        status = new HazardLights(statusLamps);
        markerLights = new HazardLights(markerRenderers);
        danger = HazardRendererSwitch.ByMaterial(block, "DungeonPropDanger");
        if (block != null) { blockHomePosition = block.localPosition; blockHomeRotation = block.localRotation; }
        if (hoist != null) hoistHomePosition = hoist.localPosition;
        if (marker != null) markerHome = marker.localPosition;
        chainHome = new Quaternion[chains.Length];
        for (int i = 0; i < chains.Length; i++) chainHome[i] = chains[i] != null ? chains[i].localRotation : Quaternion.identity;
        boltHomePosition = new Vector3[bolts.Length];
        boltHomeRotation = new Quaternion[bolts.Length];
        for (int i = 0; i < bolts.Length; i++)
        {
            boltHomePosition[i] = bolts[i] != null ? bolts[i].localPosition : Vector3.zero;
            boltHomeRotation[i] = bolts[i] != null ? bolts[i].localRotation : Quaternion.identity;
        }
        particles = new[] { chainDust, pebbles, boltSparks, impactDebris, impactDust, impactSparks };
        particleHome = new Vector3[particles.Length];
        for (int i = 0; i < particles.Length; i++) particleHome[i] = particles[i] != null ? particles[i].transform.localPosition : Vector3.zero;
        PrepareChainMeshes();
    }

    // 사슬 MeshFilter마다 실행 중 Mesh 한 장(개체가 살아 있는 동안 다시 쓴다). 마디 자료가 없으면 Prefab Mesh(사슬 토막)를 그대로 둔다.
    private void PrepareChainMeshes()
    {
        bool template = chainUnitVertices != null && chainUnitVertices.Length > 0 && chainUnitColors != null && chainUnitColors.Length == chainUnitVertices.Length
            && chainUnitTriangles != null && chainUnitTriangles.Length > 0 && chainVertexSplit > 0 && chainVertexSplit < chainUnitVertices.Length
            && chainTriangleSplit > 0 && chainTriangleSplit < chainUnitTriangles.Length;
        if (!template || chainFilters == null || chainFilters.Length == 0) return;
        chainMeshes = new Mesh[chainFilters.Length];
        for (int i = 0; i < chainFilters.Length; i++)
        {
            if (chainFilters[i] == null) continue;
            chainMeshes[i] = new Mesh { name = "PropRockfallChain (runtime)", hideFlags = HideFlags.DontSave };
            chainMeshes[i].MarkDynamic();
            chainFilters[i].sharedMesh = chainMeshes[i];
        }
        int halves = MaxHalves();
        int perUnit = chainUnitVertices.Length, trisPerUnit = chainUnitTriangles.Length;
        chainVertices.Capacity = Mathf.Max(chainVertices.Capacity, (halves / 2 + 1) * perUnit);
        chainColors.Capacity = chainVertices.Capacity;
        chainUvs.Capacity = chainVertices.Capacity;
        chainTriangles.Capacity = Mathf.Max(chainTriangles.Capacity, (halves / 2 + 1) * trisPerUnit);
        builtHalves = -1;
    }

    private int MaxHalves() => Mathf.CeilToInt((ceilingReach + defaultCeiling + 1f) / Mathf.Max(.05f, chainHalfLength)) + 1;

    private void OnDestroy()
    {
        EndShake();
        foreach (var mesh in chainMeshes)
            if (mesh != null) Destroy(mesh);
    }

    // ── IDungeonPropPart ─────────────────────────────

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        if (motor == null) motor = GetComponent<RaycastMotor2D>();
        live = true;
        halt.Reset();
        trapDamage.Cancel();
        EndShake();
        stage = Stage.Armed;
        stageTime = 0f;
        spawnTime = Time.time;
        IsBroken = false;
        Drops = HitsThisDrop = PlayerHits = EnemyHits = PropsCrushed = 0;
        LastTrembleTime = LastFallTime = LastHitTime = -1f;
        hitThisDrop.Clear();
        landingKnown = false;
        haltPosed = false;
        ResetPose();
        RestoreLights();
        ClearParticles();
        hoistRetries = FitHoist() ? 0 : HoistRetryTicks;
    }

    public void OnPropReturning(DungeonProp prop)
    {
        live = false;
        EndShake();
        trapDamage.Cancel();
        ResetPose();
        RestoreLights();
        ClearParticles();
        halt.Reset();
        haltPosed = false;
        Broken = null;   // 방 단계가 다음 대여에서 새로 구독한다(DestructibleProp과 같다)
    }

    private void OnDisable()
    {
        EndShake();
        trapDamage.Cancel();
    }

    // 꺼낸 직후 지형 Collider가 아직 물리에 반영되지 않았을 수 있어 천장을 못 찾으면 몇 물리 틱 동안 다시 본다.
    private const int HoistRetryTicks = 6;

    // 처음 자세(꺼낼 때·돌려보낼 때): 블록 자세 + 호이스트 기본 높이 + 입자 자리.
    private void ResetPose()
    {
        RestBlock();
        if (hoist != null) hoist.localPosition = hoistHomePosition;
        for (int i = 0; i < particles.Length; i++) if (particles[i] != null) particles[i].transform.localPosition = particleHome[i];
    }

    // 매달린 대기 자세(블록·볼트·사슬 회전·예고선 제자리). 천장에 맞춘 호이스트 높이는 그대로 둔다(정지 신호로 제자리에 돌아갈 때).
    private void RestBlock()
    {
        if (block != null)
        {
            block.localPosition = blockHomePosition;
            block.localRotation = blockHomeRotation;
            block.gameObject.SetActive(true);
        }
        for (int i = 0; i < chains.Length; i++) if (chains[i] != null) chains[i].localRotation = chainHome[i];
        for (int i = 0; i < bolts.Length; i++)
        {
            if (bolts[i] == null) continue;
            bolts[i].localPosition = boltHomePosition[i];
            bolts[i].localRotation = boltHomeRotation[i];
        }
        if (marker != null) marker.localPosition = markerHome;
        swingAngle = swingVelocity = 0f;
        swinging = false;
    }

    private void RestoreLights()
    {
        danger?.Restore();
        lamps?.Restore();
        status?.Restore();
        markerLights?.Restore();
        markerLights?.Set(0f);
        if (live)
        {
            lamps?.Set(idleLampGlow);
            status?.Set(statusGlow);
        }
    }

    private void ClearParticles()
    {
        for (int i = 0; i < particles.Length; i++) PropHazardKit.Stop(particles[i], true);
    }

    private void EndShake()
    {
        if (shake != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.EndHeldShake(shake);
        shake = null;
    }

    // ── 호이스트·사슬 ─────────────────────────────

    // 블록 윗변에서 위로 사슬 열마다 천장을 찾아(가까운 값) 호이스트를 그 높이로 옮기고 사슬을 그 길이로 다시 채운다. 못 찾으면 Prefab 기본 모습(사슬 토막).
    private bool FitHoist()
    {
        float ceiling = float.PositiveInfinity;
        if (motor != null && chainColumns != null)
            foreach (float x in chainColumns)
            {
                Vector2 origin = transform.TransformPoint(new Vector3(x, blockHalf.y + .02f, 0f));
                var hit = motor.RaycastEnvironment(origin, transform.up, ceilingReach + hoistHeight + .5f, floorMask);
                if (hit.collider != null) ceiling = Mathf.Min(ceiling, hit.distance + .02f);
            }
        CeilingFound = !float.IsPositiveInfinity(ceiling);
        float distance = CeilingFound ? ceiling : defaultCeiling;
        if (hoist != null) hoist.localPosition = hoistHomePosition + Vector3.up * (distance - defaultCeiling);
        ChainLength = Mathf.Max(0f, distance - hoistHeight + chainAttachDepth);
        BuildChains(ChainLength);
        return CeilingFound;
    }

    // 사슬 Mesh를 length만큼 반 마디씩(정면 고리·옆 고리 번갈아) 이어 붙여 다시 채운다(늘이지 않는다). 반 마디 수가 같으면 다시 만들지 않는다.
    private void BuildChains(float length)
    {
        if (chainMeshes.Length == 0) return;
        int halves = Mathf.Clamp(Mathf.CeilToInt(length / Mathf.Max(.05f, chainHalfLength) - 1e-3f), 0, MaxHalves());
        if (halves == builtHalves) return;
        builtHalves = halves;
        chainVertices.Clear(); chainColors.Clear(); chainUvs.Clear(); chainTriangles.Clear();
        float unit = chainHalfLength * 2f;
        for (int i = 0; i < halves; i++)
        {
            bool front = i % 2 == 0;
            int v0 = front ? 0 : chainVertexSplit, v1 = front ? chainVertexSplit : chainUnitVertices.Length;
            int t0 = front ? 0 : chainTriangleSplit, t1 = front ? chainTriangleSplit : chainUnitTriangles.Length;
            int baseIndex = chainVertices.Count;
            var offset = new Vector3(0f, -(i / 2) * unit, 0f);
            for (int v = v0; v < v1; v++)
            {
                chainVertices.Add(chainUnitVertices[v] + offset);
                chainColors.Add(chainUnitColors[v]);
                chainUvs.Add(Vector2.zero);
            }
            for (int t = t0; t < t1; t++) chainTriangles.Add(chainUnitTriangles[t] - v0 + baseIndex);
        }
        foreach (var mesh in chainMeshes)
        {
            if (mesh == null) continue;
            mesh.Clear();
            mesh.SetVertices(chainVertices);
            mesh.SetColors(chainColors);
            mesh.SetUVs(0, chainUvs);
            mesh.SetTriangles(chainTriangles, 0);
            mesh.RecalculateBounds();
        }
    }

    // ── 6차 T9 계약: 방 클리어 정지 ─────────────────────────────

    public void SetHazardHalted(bool halted, bool instant)
    {
        bool changed = halt.Signal(halted, instant);
        if (!live) return;
        if (halted)
        {
            trapDamage.Cancel();   // 감지·피해는 이 호출에서 바로 끈다(FixedUpdate·DamageAlong이 halt.Halted를 본다)
            EndShake();
            if (instant) HaltNow();
            return;
        }
        if (!changed) return;
        haltPosed = false;
        if (stage == Stage.Armed)
        {
            danger?.Set(true);
            lamps?.Restore(); lamps?.Set(idleLampGlow);
            status?.Restore(); status?.Set(statusGlow);
        }
    }

    // 바로 정지 자세(재입장 — 이미 클리어한 방): 진동은 제자리로, 낙하 중이면 그 자리에서 부서짐, 대기면 불 꺼진 매달림.
    private void HaltNow()
    {
        if (stage == Stage.Trembling)
        {
            RestBlock();
            stage = Stage.Armed;
            stageTime = 0f;
        }
        else if (stage == Stage.Falling) Shatter(false);
        if (stage == Stage.Armed) Darken();
        haltPosed = true;
    }

    // 정전 물결 차례가 온 순간(지연 뒤): 단계별 정지 외형을 시작한다.
    private void BeginHaltPose()
    {
        haltPosed = true;
        switch (stage)
        {
            case Stage.Armed:
                Darken();
                break;
            case Stage.Trembling:
                if (block != null) { haltFromPosition = block.localPosition; haltFromRotation = block.localRotation; }
                markerLights?.Set(0f);
                PropHazardKit.Stop(chainDust);
                PropHazardKit.Stop(pebbles);
                break;
            case Stage.Falling:
                Shatter(false);
                break;
        }
    }

    // 불 꺼진 매달림: 위험 띠·램프·상태등·예고선 끔, 입자 정지.
    private void Darken()
    {
        danger?.Set(false);
        lamps?.Set(0f);
        status?.Set(0f);
        markerLights?.Set(0f);
        PropHazardKit.Stop(chainDust);
        PropHazardKit.Stop(pebbles);
    }

    // ── 단계 ─────────────────────────────

    /// <summary>
    /// 검사용: 유예·감지를 건너뛰고 지금 진동을 시작한다. 정지 신호 중이거나 대기가 아니면 무시하고 false.
    /// </summary>
    public bool Trigger()
    {
        if (!live || halt.Halted || stage != Stage.Armed) return false;
        BeginTremble();
        return true;
    }

    private void BeginTremble()
    {
        stage = Stage.Trembling;
        stageTime = 0f;
        LastTrembleTime = Time.time;
        nextPop = 0;
        nextDust = 0f;
        ComputeLanding();
        PlaceMarker();
        markerLights?.Set(markerGlow);
        PropHazardKit.PlayClip(trembleSound, HoistPoint);
        EndShake();
        if (FeedbackPlayer.Instance != null && heldShake > 0f) shake = FeedbackPlayer.Instance.BeginHeldShake(heldShake, transform.position);
    }

    private void BeginFall()
    {
        EndShake();
        // 그사이 받침(상자 등)이 부서졌을 수 있어 착지면을 다시 구한다.
        ComputeLanding();
        PlaceMarker();
        if (block != null)
        {
            block.localPosition = blockHomePosition;
            block.localRotation = blockHomeRotation;
            dropOrigin = block.position;
        }
        else dropOrigin = transform.position;
        for (int i = 0; i < bolts.Length; i++)
            if (bolts[i] != null) { bolts[i].localPosition = boltHomePosition[i]; bolts[i].localRotation = boltHomeRotation[i]; }
        stage = Stage.Falling;
        stageTime = 0f;
        LastFallTime = Time.time;
        Drops++;
        HitsThisDrop = 0;
        hitThisDrop.Clear();
        lastDamageDistance = 0f;
        lamps?.Set(warningLampGlow);
        markerLights?.Set(markerGlow);
        PropHazardKit.PlayClip(releaseSound, HoistPoint);
        Kick();
    }

    // 블록 폭 안 열에서 아래로 가장 가까운 받침(Platform — OneWay는 지나간다)까지 거리. 못 찾으면 maxDrop.
    private void ComputeLanding()
    {
        float nearest = maxDrop;
        Vector2 down = -(Vector2)transform.up;
        if (motor != null && landingColumns != null)
            foreach (float x in landingColumns)
            {
                Vector2 origin = transform.TransformPoint(new Vector3(x, -blockHalf.y + .05f, 0f));
                var hit = motor.RaycastEnvironment(origin, down, maxDrop + .05f, floorMask);
                if (hit.collider != null) nearest = Mathf.Min(nearest, Mathf.Max(0f, hit.distance - .05f));
            }
        dropDistance = Mathf.Max(0f, nearest);
        landingY = BlockBottomY - dropDistance;
        landingKnown = true;
    }

    private void PlaceMarker()
    {
        if (marker == null || !landingKnown) return;
        Vector2 at = transform.TransformPoint(new Vector3(0f, -blockHalf.y, 0f)) - transform.up * dropDistance;
        marker.position = new Vector3(at.x, at.y, marker.position.z);
    }

    private float BlockBottomY => transform.TransformPoint(new Vector3(0f, -blockHalf.y, 0f)).y;
    private Vector3 HoistPoint => hoist != null ? hoist.position : transform.position + transform.up * (blockHalf.y + defaultCeiling);

    private void FixedUpdate()
    {
        if (!live) return;
        if (hoistRetries > 0)
        {
            // 꺼낸 직후 지형 Collider가 아직 물리에 없었을 수 있다 — 천장을 다시 찾고, 착지면도 다음 감지 때 다시 구한다.
            hoistRetries = FitHoist() ? 0 : hoistRetries - 1;
            landingKnown = false;
        }
        if (stage == Stage.Armed)
        {
            if (CanSense() && PlayerInside()) BeginTremble();
            return;
        }
        if (stage != Stage.Falling) return;
        float d = Mathf.Min(FallDistance(stageTime + Time.fixedDeltaTime * .5f), dropDistance);
        DamageAlong(d, false);
    }

    private bool CanSense() => !halt.Halted && Time.time - spawnTime >= activeGrace && !DungeonCutsceneDirector.AnyPlaying;

    // Player 몸통(Collider.bounds — 물리 질의 없음)이 감지 사각형(블록 폭 ± 감지 여유 × 착지면 ~ 블록 아랫변)과 겹치는지.
    private bool PlayerInside()
    {
        if (!PropHazardKit.TryPlayerBody(out var player, out Bounds body)) return false;
        if (player.Health == null || player.Health.IsDead) return false;
        if (!landingKnown) ComputeLanding();
        Vector2 a = transform.TransformPoint(new Vector3(-blockHalf.x - activeRange, -blockHalf.y, 0f));
        Vector2 b = transform.TransformPoint(new Vector3(blockHalf.x + activeRange, -blockHalf.y, 0f));
        float left = Mathf.Min(a.x, b.x), right = Mathf.Max(a.x, b.x);
        float top = Mathf.Max(a.y, b.y), bottom = Mathf.Min(landingY, top);
        return body.max.x > left && body.min.x < right && body.max.y > bottom && body.min.y < top;
    }

    private void Update()
    {
        if (!live) return;
        float dt = Time.deltaTime;
        // fix7a: 정지를 푼 뒤에도 Blend를 0으로 되돌린다(정지 중에만 진행하면 Blend가 1에 남아, 다음 정지 신호에서 진동 중 제자리 복귀(haltSettle)가 건너뛰어지고 바로 튄다).
        if (halt.Halted || halt.Blend > 0f)
        {
            halt.Step(dt, haltSettle, out _, out _);
            if (halt.VisualHalting && !haltPosed) BeginHaltPose();
        }
        switch (stage)
        {
            case Stage.Trembling:
                if (haltPosed) SettleToRest();
                else
                {
                    stageTime += dt;
                    Tremble();
                    if (!halt.Halted && stageTime >= activeWarning) BeginFall();
                }
                break;
            case Stage.Falling:
            {
                stageTime += dt;
                float d = Mathf.Min(FallDistance(stageTime), dropDistance);
                if (block != null) block.position = dropOrigin - transform.up * d;
                if (d >= dropDistance) Land();
                Swing(dt);
                break;
            }
            case Stage.Shattered:
                Swing(dt);
                break;
        }
    }

    // 진동: 블록 흔들림(회전 ±trembleAngle·이동 ±trembleShift — 늘이지 않음), 사슬 덜컹, 볼트 떨림·튐, 램프·예고선 깜박임, 먼지·자갈.
    private void Tremble()
    {
        float t = stageTime;
        float k = Mathf.Clamp01(t / Mathf.Max(.05f, activeWarning));
        float level = .45f + .55f * k;
        if (block != null)
        {
            float angle = trembleAngle * level * (Mathf.Sin(t * 37f) * .6f + Mathf.Sin(t * 61f + 1.3f) * .4f);
            var shift = new Vector3(trembleShift * level * Mathf.Sin(t * 53f + .7f), -trembleShift * .4f * level * Mathf.Abs(Mathf.Sin(t * 29f)), 0f);
            block.localPosition = blockHomePosition + shift;
            block.localRotation = blockHomeRotation * Quaternion.Euler(0f, 0f, angle);
        }
        for (int i = 0; i < chains.Length; i++)
            if (chains[i] != null)
                chains[i].localRotation = chainHome[i] * Quaternion.Euler(0f, 0f, chainRattle * level * Mathf.Sin(t * (43f + i * 9f) + i * 2.1f));
        for (int i = 0; i < bolts.Length; i++)
            if (bolts[i] != null)
            {
                bolts[i].localRotation = boltHomeRotation[i] * Quaternion.Euler(0f, 0f, boltJitter * level * Mathf.Sin(t * (71f + i * 13f)));
                bolts[i].localPosition = boltHomePosition[i] + new Vector3(0f, .025f * level * Mathf.Abs(Mathf.Sin(t * (47f + i * 7f))), 0f);
            }
        bool on = Mathf.Repeat(t, blinkPeriod) < blinkPeriod * .5f;
        lamps?.Set(on ? warningLampGlow : warningLampGlow * .3f);
        markerLights?.Set(on ? markerGlow : markerGlow * .4f);
        if (boltPops != null && nextPop < boltPops.Length && k >= boltPops[nextPop])
        {
            var bolt = bolts.Length > 0 ? bolts[nextPop % bolts.Length] : null;
            Vector3 at = bolt != null ? bolt.position : transform.TransformPoint(new Vector3(0f, blockHalf.y, 0f));
            PropHazardKit.Burst(boltSparks, 6, at);
            PropHazardKit.PlayClip(boltSound, at);
            nextPop++;
        }
        if (t >= nextDust)
        {
            nextDust = t + dustInterval;
            PropHazardKit.Burst(chainDust, 2);
            PropHazardKit.Burst(pebbles, 1);
        }
    }

    // 진동 중 정지 신호: haltSettle초 안에 제자리 자세로 돌아가고(halt.Blend 0 → 1), 닿으면 불 꺼진 매달림(대기)으로.
    private void SettleToRest()
    {
        float blend = PropHazardKit.Smooth(halt.Blend);
        if (block != null)
        {
            block.localPosition = Vector3.Lerp(haltFromPosition, blockHomePosition, blend);
            block.localRotation = Quaternion.Slerp(haltFromRotation, blockHomeRotation, blend);
        }
        for (int i = 0; i < chains.Length; i++) if (chains[i] != null) chains[i].localRotation = Quaternion.Slerp(chains[i].localRotation, chainHome[i], blend);
        for (int i = 0; i < bolts.Length; i++)
            if (bolts[i] != null)
            {
                bolts[i].localPosition = Vector3.Lerp(bolts[i].localPosition, boltHomePosition[i], blend);
                bolts[i].localRotation = Quaternion.Slerp(bolts[i].localRotation, boltHomeRotation[i], blend);
            }
        if (halt.Blend < 1f) return;
        RestBlock();
        stage = Stage.Armed;
        stageTime = 0f;
        Darken();
    }

    private float FallDistance(float t)
    {
        float reach = maxFallSpeed / gravity;               // 최고 속도에 닿는 시간
        if (t <= reach) return .5f * gravity * t * t;
        return .5f * gravity * reach * reach + maxFallSpeed * (t - reach);
    }

    private void Land()
    {
        if (block != null) block.position = dropOrigin - transform.up * dropDistance;
        DamageAlong(dropDistance, true);
        if (stage == Stage.Falling) Shatter(true);   // 피해 처리 중 정지 신호로 이미 부서졌으면 건너뛴다
    }

    // 블록이 부서져 사라진다(지형 불변). landed = 착지(충돌 Feedback·소리·흔들림), 아니면 정지 신호로 그 자리에서 부서짐(피해 없이 파편·먼지·겹침음만).
    private void Shatter(bool landed)
    {
        if (stage == Stage.Shattered) return;
        EndShake();
        Vector3 at = landed && landingKnown
            ? transform.TransformPoint(new Vector3(0f, -blockHalf.y, 0f)) - transform.up * dropDistance
            : block != null ? block.TransformPoint(new Vector3(0f, -(blockHomePosition.y + blockHalf.y), 0f)) : transform.position;   // 블록 부품 원점(윗변 가운데) 기준 아랫변
        if (landed) DungeonPropFeedback.Play(impactFeedback, at, Quaternion.identity, impactShakeMultiplier);
        else PropHazardKit.PlayClip(impactLayerSound, at, .6f);
        PropHazardKit.Burst(impactDebris, landed ? 40 : 24, at);
        PropHazardKit.Burst(impactDust, landed ? 16 : 10, at);
        PropHazardKit.Burst(impactSparks, landed ? 22 : 10, at);
        if (block != null) block.gameObject.SetActive(false);
        PropHazardKit.Stop(chainDust);
        PropHazardKit.Stop(pebbles);
        markerLights?.Set(0f);
        lamps?.Set(0f);
        status?.Set(statusGlow * .35f);
        stage = Stage.Shattered;
        stageTime = 0f;
        if (!swinging) Kick();
        if (IsBroken) return;
        IsBroken = true;
        Broken?.Invoke(this);
    }

    // 블록이 빠진 빈 사슬의 감쇠 흔들림(고리 줄을 축으로 회전만).
    private void Kick()
    {
        swingAngle = 0f;
        swingVelocity = releaseSwing;
        swinging = chains.Length > 0;
    }

    private void Swing(float dt)
    {
        if (!swinging || dt <= 0f) return;
        const float stiffness = 60f, dampingRatio = .18f;
        float damping = 2f * dampingRatio * Mathf.Sqrt(stiffness);
        swingVelocity += (-stiffness * swingAngle - damping * swingVelocity) * dt;
        swingAngle += swingVelocity * dt;
        for (int i = 0; i < chains.Length; i++)
            if (chains[i] != null) chains[i].localRotation = chainHome[i] * Quaternion.Euler(0f, 0f, swingAngle * (i % 2 == 0 ? 1f : .8f));
        if (Mathf.Abs(swingAngle) >= .02f || Mathf.Abs(swingVelocity) >= .2f) return;
        swinging = false;
        for (int i = 0; i < chains.Length; i++) if (chains[i] != null) chains[i].localRotation = chainHome[i];
    }

    // 지난 판정 아랫변부터 d까지 블록 아랫면이 쓸고 지나간 상자를 묻는다(빠르게 떨어져도 건너뛰지 않게). 결과는 따로 옮겨 적은 뒤 피해를 준다.
    private void DamageAlong(float d, bool landing)
    {
        if (halt.Halted) { lastDamageDistance = d; return; }
        float from = Mathf.Min(lastDamageDistance, d);
        float to = d + (landing ? crushDepth : 0f);
        lastDamageDistance = d;
        Vector2 bottom = transform.TransformPoint(new Vector3(0f, -blockHalf.y, 0f));
        Vector2 down = -(Vector2)transform.up;
        float top = from - sweepLip;
        Vector2 center = bottom + down * ((top + to) * .5f);
        var size = new Vector2(Mathf.Max(.1f, blockHalf.x * 2f - damageInset * 2f), Mathf.Max(.05f, to - top));
        int count = PropHazardKit.OverlapBox(motor, center, size, transform.eulerAngles.z, ref hits, playerMask | enemyMask);
        if (pending.Length < count) Array.Resize(ref pending, hits.Length);
        Array.Copy(hits, pending, count);
        for (int i = 0; i < count; i++) hits[i] = null;
        for (int i = 0; i < count; i++)
        {
            var hit = pending[i];
            pending[i] = null;
            if (hit == null || halt.Halted || !live) continue;   // 피해 처리 중 방이 클리어되면(정지 신호) 남은 대상은 건너뛴다
            Vector2 point = hit.ClosestPoint(center);
            if ((playerMask.value & (1 << hit.gameObject.layer)) != 0)
            {
                var player = hit.GetComponentInParent<PlayerHealth>();
                if (player == null || activeDamage <= 0f || !hitThisDrop.Add(player)) continue;
                var info = new WeaponBaseSO.DamageInfo { damage = activeDamage, damageType = DamageType.Normal, knockbackOrigin = point };
                if (trapDamage.TryDamage(this, player, info, point, activeDamage, false))
                {
                    PlayerHits++;
                    HitsThisDrop++;
                    LastHitTime = Time.time;
                }
                continue;
            }
            var health = hit.GetComponentInParent<EnemyHealth>();
            if (health == null || hitThisDrop.Contains(health)) continue;
            if (DungeonPropTargeting.IsProp(health))
            {
                hitThisDrop.Add(health);
                if (health is DestructibleProp breakable && !breakable.IsBroken)
                {
                    breakable.Break(point);
                    PropsCrushed++;
                    HitsThisDrop++;
                }
                continue;
            }
            var enemy = health.GetComponentInParent<EnemyController>();
            if (enemy != null && EnemyPool.Instance != null && EnemyPool.Instance.IsSpawning(enemy)) continue;   // 등장 연출 중(판단값 — PropShredder와 같다)
            if (activeEnemyDamage <= 0f) continue;
            hitThisDrop.Add(health);
            var enemyInfo = new WeaponBaseSO.DamageInfo { damage = activeEnemyDamage, damageType = DamageType.Normal, knockbackOrigin = point };
            if (WeaponTraitEffects.ApplyEnemyHit(health, enemyInfo, point))
            {
                EnemyHits++;
                HitsThisDrop++;
                LastHitTime = Time.time;
            }
        }
    }

    private static bool AnyEnabled(IReadOnlyList<Renderer> renderers)
    {
        if (renderers == null) return false;
        for (int i = 0; i < renderers.Count; i++)
            if (renderers[i] != null && renderers[i].enabled) return true;
        return false;
    }

    // ── 배치별 설정 ─────────────────────────────

    private WaveDeviceParameter EnemyDamageParameter => new WaveDeviceParameter(EnemyDamageKey, "적 피해량",
        "떨어진 블록에 맞은 적이 받는 피해량입니다(쉴드가 있으면 쉴드부터 — 등장 연출 중인 적은 맞지 않습니다). 파괴형 Prop은 피해량과 관계없이 부서집니다.", "", 0f, 10000f, 1f, enemyDamage);
    private WaveDeviceParameter WarningParameter => new WaveDeviceParameter(WarningKey, "진동 시간",
        "Player가 감지 범위에 든 뒤 사슬이 덜컹거리고 블록이 흔들리는 예고 시간입니다. 끝나면 사슬이 끊어져 떨어집니다.", "초", .5f, 3f, .05f, warningTime);
    private WaveDeviceParameter TriggerRangeParameter => new WaveDeviceParameter(TriggerRangeKey, "감지 여유",
        "낙하 열(블록 폭 4칸) 좌우로 더 넓혀 Player 몸통을 감지하는 거리입니다. 세로는 착지면 ~ 블록 아랫변입니다.", "칸", 0f, 4f, .25f, triggerRange);
    private WaveDeviceParameter GraceParameter => new WaveDeviceParameter(GraceKey, "입장 유예",
        "생성된 뒤 이 시간 동안은 감지하지 않습니다(출입구 바로 옆 배치에서 들어오자마자 떨어지지 않게).", "초", 0f, 5f, .1f, grace);

    public WaveDeviceParameter[] PropParameters => new[]
    {
        WaveTrapDamage.DamageParameter(damage), EnemyDamageParameter, WarningParameter, TriggerRangeParameter, GraceParameter,
    };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides) => ReadSettings(overrides);

    private void ReadSettings(WaveDeviceSetting[] overrides)
    {
        activeDamage = WaveDeviceSettings.Value(overrides, WaveTrapDamage.DamageParameter(damage));
        activeEnemyDamage = WaveDeviceSettings.Value(overrides, EnemyDamageParameter);
        activeWarning = Mathf.Max(.05f, WaveDeviceSettings.Value(overrides, WarningParameter));
        activeRange = Mathf.Max(0f, WaveDeviceSettings.Value(overrides, TriggerRangeParameter));
        activeGrace = Mathf.Max(0f, WaveDeviceSettings.Value(overrides, GraceParameter));
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        // 편집기에는 블록 아래 7칸만 보인다(실제 위험은 착지면까지 — 템플릿 검사가 칸으로 본다).
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, new Vector2(0f, -blockHalf.y - 3.5f), new Vector2(blockHalf.x * 2f, 7f), new Color(1f, .22f, .16f, .9f));
        return true;
    }
}
