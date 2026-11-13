using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

// 훈련 구간의 시작 쪽(바닥·벽 속)에 숨어 있다가, 잠시 뒤 진행 방향으로 밀고 나오며 플레이어를 쫓는 분쇄기다.
// 분쇄 날 끝(분쇄선)부터 그 뒤쪽 전체가 피해 영역이라 구간을 뒤에서부터 막는다. 플레이어가 닿으면 같은 웨이브의
// TutorialPositionResetter가 시작 위치로 되돌리고, 분쇄기도 시작 위치에서 다시 기다렸다가 쫓는다.
// 코스에 놓인 적에게 맞은 피해로는 다시 시작하지 않는다. 피해의 출처는 IsGrinderHit로 구분한다.
// 분쇄선이 지나간 생성 발판(Wave가 시간에 따라 만든 칸)과 적은 갈려 없어진다. 발판은 분쇄선이 칸을 완전히 지난 뒤에 빠지므로
// 그 위에 설 수 있는 시간은 그대로이고, 구간을 다시 시작하면 갈린 발판이 모두 되살아난다(적은 CourseEnemySpawner가 다시 꺼낸다).
// 피해·적 판정은 기존 모터(RaycastMotor2D)의 범위 질의를 쓰고, 보이는 몸체(ChasingGrinderBody)는 이 위치를 따라 놓일 뿐이다.
// Wave 격자 편집기에 놓는 분쇄기(Environment/WaveTools/ChasingGrinder)는 배치 회전이 쫓는 방향이고, 띠 폭·속도·대기·거리·닿았을 때의
// 동작·피해량을 배치마다 정한다(IWaveDeviceSettings). 닿으면 처음부터 다시 하는 분쇄기는 그 Wave에 위치 초기화 장치가 없으면
// 플레이어를 이 Wave의 시작 좌표로 직접 돌려보낸다. 보스로 나온 적은 갈지 않는다.
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // 캐릭터가 이번 틱의 이동을 마친 뒤에 판정한다.
public sealed class ChasingGrinder : WorldEntityBase, IWaveDeviceSettings
{
    // 분쇄기가 플레이어에게 피해를 주는 동안(TakeDamage 호출 중)에만 그 분쇄기를 가리킨다.
    private static ChasingGrinder hitSource;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => hitSource = null;

    /// <summary>
    /// PlayerHealth.OnDamaged를 받는 중에 부르면, 이번 피해가 닿으면 처음부터 다시 하는 추격 분쇄기에서 왔는지 알려 준다.
    /// 피해만 주고 계속 쫓는 분쇄기의 피해는 일반 피해로 다룬다(위치 초기화·코스 적이 처음으로 돌아가지 않는다).
    /// </summary>
    public static bool IsGrinderHit => hitSource != null && hitSource.currentTouch == TouchResponse.Restart;

    public enum Phase { Holding, Chasing, Halted }
    /// <summary>플레이어가 분쇄 영역에 닿았을 때의 동작.</summary>
    public enum TouchResponse { Restart, DamageOnly }

    // 위치 초기화 장치가 없는 Wave에서 직접 되돌릴 때의 암전 시간(초). 위치 초기화 장치의 기본값과 같다.
    private const float ReturnFadeOut = .15f, ReturnBlackHold = .15f, ReturnFadeIn = .3f;

    [Header("방향")]
    [SerializeField, Tooltip("분쇄기가 쫓아가는 방향입니다. 등반은 위, 달리기는 오른쪽입니다. 생성 회전(격자 편집기의 설치 방향)만큼 함께 돕니다.")]
    private Vector2 travelDirection = Vector2.up;
    [SerializeField, Tooltip("분쇄 띠가 뻗는 방향입니다. 진행 방향과 수직으로 둡니다. 생성 위치가 띠의 시작 끝입니다.")]
    private Vector2 spanDirection = Vector2.right;
    [SerializeField, Tooltip("켜면 생성 위치가 놓인 칸의 가운데이고, 띠는 그 칸의 진행 반대쪽·띠 시작 쪽 모서리에서 시작합니다(격자 편집기에 놓는 분쇄기). 끄면 생성 위치가 띠의 시작 끝입니다.")]
    private bool anchorAtCellCorner;

    [Header("닿았을 때")]
    [SerializeField, Tooltip("처음부터 다시: 피해 없이 위치 초기화 장치(없으면 이 Wave의 시작 좌표)로 돌려보내고 분쇄기도 처음부터 다시 쫓습니다. 피해만 주고 계속: 피해량만큼 피해를 주고 계속 쫓습니다.")]
    private TouchResponse touchResponse = TouchResponse.Restart;

    [Header("분쇄 영역")]
    [SerializeField, Tooltip("닿았을 때의 피해. 피해량(damage)은 '피해만 주고 계속'일 때만 씁니다(처음부터 다시는 피해 없이 되돌립니다).")]
    private WeaponBaseSO.DamageInfo damage;
    [SerializeField, Min(0f), Tooltip("생성 위치에서 띠 방향으로 뻗는 길이(칸)입니다. 구간의 안쪽 폭에 맞춥니다.")]
    private float span = 26f;
    [SerializeField, Min(0f), Tooltip("나아간 거리보다 이만큼(칸) 앞까지가 피해 영역입니다. 띠의 양 끝도 이만큼 넓습니다. 분쇄 날 끝이 이 선에 놓입니다.")]
    private float edgeMargin = .125f;
    [SerializeField, Tooltip("피해를 줄 대상 레이어입니다.")]
    private LayerMask damageMask;
    [SerializeField, Tooltip("갈아 없앨 적의 레이어입니다.")]
    private LayerMask enemyMask;

    [Header("추격")]
    [SerializeField, Min(0f), Tooltip("나온 뒤 움직이기 전에 기다리는 시간(초)입니다. 플레이어가 닿은 뒤에도 시작 위치에서 같은 시간만큼 기다립니다.")]
    private float holdDuration = 2f;
    [SerializeField, Min(0f), Tooltip("초당 나아가는 거리(칸)입니다.")]
    private float speed = 1.25f;
    [SerializeField, Min(0f), Tooltip("생성 위치에서 나아갈 수 있는 최대 거리(칸)입니다.")]
    private float maxTravel = 84f;
    [SerializeField, Min(0f), Tooltip("화면 밖에서 따라붙는 최대 속도(칸/초)입니다. 화면 경계까지의 이동량만 가속합니다.")]
    private float catchUpSpeed = 80f;
    [SerializeField, Min(0f), Tooltip("화면에 살짝 들어올 거리(칸)입니다. 여기부터 기본 추격 속도를 사용합니다.")]
    private float visibleInset = .1f;

    [Header("몸체 (보이는 것만 바꾸며 판정과는 무관합니다)")]
    [SerializeField, Tooltip("분쇄 날과 몸체 Prefab입니다. 풀의 개체마다 만들지 않고 필요한 수만 만들어 빌려 씁니다.")]
    private ChasingGrinderBody bodyPrefab;
    [SerializeField, Min(0f), Tooltip("기다리는 동안 몸체를 진행 반대쪽으로 숨겨 두는 거리(칸)입니다.")]
    private float hideDepth = 3f;
    [SerializeField, Min(0f), Tooltip("기다림이 끝나기 전에 몸체가 분쇄선까지 올라오는 데 쓰는 시간(초)입니다.")]
    private float emergeDuration = .7f;
    [SerializeField, Min(0f), Tooltip("플레이어가 닿은 뒤 몸체를 그 자리에 남겨 두는 시간(초)입니다. 위치 초기화의 암전 시간에 맞춥니다.")]
    private float returnDelay = .15f;
    [SerializeField, Min(0f), Tooltip("구간이 끝났을 때 몸체가 물러나 사라지는 시간(초)과 거리(칸)입니다.")]
    private float retreatDuration = .9f;
    [SerializeField, Min(0f)] private float retreatDistance = 22f;

    [Header("갈리는 연출")]
    [SerializeField, Min(0f), Tooltip("분쇄선 뒤로 이만큼(칸) 들어간 조각은 몸체에 가려지므로 그림을 끕니다.")]
    private float bladeDepth = 1.5f;
    [SerializeField, Min(0f), Tooltip("분쇄 날에 닿은 발판이 떨리는 폭(칸)입니다.")]
    private float contactShake = .025f;
    [SerializeField, Min(0f), Tooltip("갈려 나온 조각이 떨리는 폭(칸)입니다.")]
    private float pieceShake = .07f;
    [SerializeField, Min(0f), Tooltip("움직이기 시작한 뒤 이 거리(칸)를 나아갈 때까지 띠 전체에서 바닥·벽을 뚫는 불꽃을 냅니다.")]
    private float breachDistance = 1f;

    [Header("효과")]
    [SerializeField, Tooltip("플레이어가 갈렸을 때.")] private FeedbackDataSO hitFeedback;
    [SerializeField, Tooltip("발판이 갈려 떨어져 나올 때.")] private FeedbackDataSO slabFeedback;
    [SerializeField, Tooltip("적이 갈렸을 때.")] private FeedbackDataSO enemyFeedback;
    [SerializeField, Tooltip("움직이기 시작해 바닥·벽을 뚫고 나올 때.")] private FeedbackDataSO breachFeedback;
    [SerializeField, Tooltip("분쇄기가 도는 동안의 반복음.")] private SoundOptions loopSound = new SoundOptions();
    [SerializeField, Min(0f), Tooltip("반복음과 화면 떨림이 켜지고 꺼질 때 따라가는 시간(초).")] private float loopFadeTime = .3f;
    [SerializeField, Min(0f), Tooltip("쫓아오는 동안 이어지는 화면 떨림의 세기(가까울 때).")] private float rumbleShake = .11f;
    [SerializeField, Min(0f), Tooltip("카메라에서 이 거리(칸) 안이면 소리와 떨림을 원래 세기로 냅니다.")] private float effectNearDistance = 10f;
    [SerializeField, Min(0f), Tooltip("카메라에서 이 거리(칸)부터는 소리와 떨림을 내지 않습니다.")] private float effectFarDistance = 26f;

    private RaycastMotor2D motor;
    private WaveCellTerrain terrain;
    private TrainingCourseStage[] courseStages;
    private PlayerHealth subscribedHealth;
    private ChasingGrinderBody body;
    private Collider2D[] hits = new Collider2D[16];
    private readonly HashSet<PlayerHealth> damagedPlayers = new();
    private bool damageApplied;
    private Action<WeaponBaseSO.DamageInfo, float> damageListener;

    // 이 개체에 적용된 값. Prefab의 값이 기본값이고, 격자 편집기에서 배치마다 바꾼 값이 ApplySettings로 들어온다.
    // 아직 적용하지 않은 개체(Prefab 에셋)는 Prefab의 값을 그대로 보여 준다.
    private float currentSpan, currentSpeed, currentHold, currentMaxTravel, currentDamage;
    private TouchResponse currentTouch;
    private bool settingsApplied;
    private readonly WavePlayerReturn playerReturn = new WavePlayerReturn();
    private Coroutine returnRoutine;

    private float travel;
    private float timer;
    // 닿은 다음 프레임에 처음부터 다시 기다린다. 같은 피해 알림을 받는 다른 쪽(위치 초기화·코스 적)이 처리를 마친 뒤에 상태를 바꾼다.
    private bool holdRequested;

    // 몸체가 보이는 자리: 평소에는 분쇄선과 같고, 플레이어가 닿은 직후 잠깐은 닿은 자리에 남는다.
    private float shownTravel;
    private float returnTimer;
    private bool breached;
    private bool retreating;
    private float retreatTime;
    // 물러나기 시작할 때 몸체가 있던 자리(나아간 거리 기준). 숨어 있던 몸체가 분쇄선으로 튀지 않고 그 자리에서 물러난다.
    private float retreatBase;
    private bool returnPending;

    // 지금 Wave의 생성 발판: 아직 서 있는 칸(진행 방향 순)과, 갈려서 조각으로 보이는 칸.
    private struct Slab { public WaveMapCell cell; public float along; public Vector2 center; }
    private readonly List<WaveMapCell> cellBuffer = new();
    private readonly List<Slab> standing = new();
    private readonly List<Slab> pieces = new();
    private readonly List<WaveMapCell> touched = new();
    private int firstStanding;
    private float slabRefreshTimer;
    private const float SlabRefreshInterval = .25f;
    private float slabSoundBlock;

    private SoundHandle loopHandle;
    private float loopBaseVolume, loopGain, nextLoopRetryTime;
    private CinemachineImpulseManager.ImpulseEvent rumble;
    private float rumbleLevel;
    private readonly Dictionary<FeedbackDataSO, FeedbackDataSO> distanceFeedbacks = new();

    public Phase CurrentPhase { get; private set; }
    public float CurrentTravel => travel;
    public float CurrentSpeed { get; private set; }
    public bool IsHolding => CurrentPhase == Phase.Holding;
    public bool IsHalted => CurrentPhase == Phase.Halted;
    public Vector2 TravelDirection => Rotate(travelDirection);
    public Vector2 SpanDirection => Rotate(spanDirection);
    public float Span => settingsApplied ? currentSpan : span;
    public float Speed => settingsApplied ? currentSpeed : speed;
    public float HoldDuration => settingsApplied ? currentHold : holdDuration;
    public float MaxTravel => settingsApplied ? currentMaxTravel : maxTravel;
    public TouchResponse Touch => settingsApplied ? currentTouch : touchResponse;
    /// <summary>띠의 시작 끝(분쇄선이 나아간 거리를 재는 기준점)의 월드 좌표.</summary>
    public Vector2 Origin => (Vector2)transform.position + (anchorAtCellCorner ? -(TravelDirection + SpanDirection) * .5f : Vector2.zero);
    /// <summary>분쇄선(피해 영역의 앞 가장자리)이 생성 위치에서 나아간 거리(칸).</summary>
    public float EdgeTravel => travel + edgeMargin;
    /// <summary>분쇄선 가운데의 월드 좌표.</summary>
    public Vector2 EdgeCenter => Origin + SpanDirection * (currentSpan * .5f) + TravelDirection * EdgeTravel;
    /// <summary>지금 조각으로 보이는(갈렸지만 아직 몸체에 가려지지 않은) 발판 칸 수.</summary>
    public int PieceCount => pieces.Count;
    public ChasingGrinderBody Body => body;

    // 생성 회전(격자 편집기의 설치 방향)만큼 돌린 방향. 회전하지 않은 개체(종합훈련 Wave5·9)는 Prefab의 방향 그대로다.
    private Vector2 Rotate(Vector2 direction) => ((Vector2)(transform.rotation * (Vector3)direction)).normalized;

    protected override void Awake()
    {
        base.Awake();
        ApplySettings(WaveDeviceSettings.None);
        // 모든 판정은 기존 모터의 질의를 거친다.
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();
        // 풀이 개체를 미리 만들 때(입장 로딩) 몸체도 하나 만들어 둔다. 처음 나오는 순간에 만들지 않는다.
        if (Application.isPlaying) ChasingGrinderBody.Prewarm(bodyPrefab);
    }

    /// <summary>
    /// 입장 로딩 때 한 Wave의 분쇄기 배치가 쓸 몸체를 미리 만들어 둔다. 같은 몸체 Prefab을 쓰는 분쇄기가 함께 나오면 그 수만큼 만들고,
    /// 배치마다 그 띠 폭의 Mesh와 부품을 준비한다(나오는 순간에는 바꿔 끼우기만 한다). 분쇄기가 없는 Wave면 아무것도 하지 않는다.
    /// </summary>
    public static void WarmWave(WaveSpawnInfo[] spawns)
    {
        if (spawns == null) return;
        var counts = new Dictionary<ChasingGrinderBody, int>();
        foreach (var info in spawns)
        {
            if (info == null || info.prefab == null || !info.prefab.TryGetComponent(out ChasingGrinder grinder) || grinder.bodyPrefab == null) continue;
            int count = counts[grinder.bodyPrefab] = (counts.TryGetValue(grinder.bodyPrefab, out int n) ? n : 0) + 1;
            ChasingGrinderBody.Prewarm(grinder.bodyPrefab, WaveDeviceSettings.Value(info.deviceSettings, grinder.WidthParameter), count);
        }
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        var waves = WaveManager.ActiveInstance;
        terrain = waves != null ? waves.GetComponentInParent<WaveCellTerrain>(true) : null;
        var room = RoomManager.Instance != null ? RoomManager.Instance.CurrentRoom : null;
        // 방 전체를 훑지 않고 방이 찾아 둔 구간 목록을 쓴다.
        courseStages = RoomViewRules.CourseStagesIn(room);
        retreating = false; returnPending = false; retreatTime = 0f;
        returnTimer = 0f; slabSoundBlock = 0f;
        standing.Clear(); pieces.Clear(); touched.Clear(); firstStanding = 0;
        // 풀에서 다시 꺼낸 개체는 Prefab 기본값에서 시작한다. 배치별 값은 WaveManager가 곧바로 적용한다.
        ApplySettings(WaveDeviceSettings.None);
        Subscribe();
        // 처음 나올 때는 지형을 건드리지 않는다(같은 Wave의 다른 분쇄기가 간 발판을 되살리지 않는다).
        Hold(false);
        shownTravel = travel;
        // 몸체는 배치별 띠 폭이 정해진 뒤 첫 Update에서 빌린다(기다리는 동안은 숨어 있어 보이지 않는다).
    }

    // 시작 위치에 숨어서 기다린다. restore면 갈았던 발판을 모두 되살린다(플레이어가 닿아 구간을 처음부터 다시 시작할 때).
    private void Hold(bool restore)
    {
        holdRequested = false;
        travel = 0f;
        CurrentSpeed = 0f;
        breached = false;
        CurrentPhase = Phase.Holding;
        timer = currentHold;
        ReleaseSlabs(restore);
        StopRumble();
    }

    private void Update()
    {
        float delta = Time.deltaTime;
        if (retreating) { Retreat(delta); return; }
        if (CurrentPhase == Phase.Halted) return;

        if (holdRequested)
        {
            // 몸체는 화면이 어두워질 때까지 닿은 자리에 남긴다. 판정과 추격은 지금 처음으로 돌아간다.
            returnTimer = returnDelay;
            Hold(true);
        }
        else
        {
            switch (CurrentPhase)
            {
                case Phase.Holding:
                    timer -= delta;
                    if (timer <= 0f) CurrentPhase = Phase.Chasing;
                    break;
                case Phase.Chasing:
                    float advance = ChaseAdvance(delta);
                    CurrentSpeed = delta > 0f ? advance / delta : 0f;
                    travel = Mathf.Min(currentMaxTravel, travel + advance);
                    break;
            }
        }

        if (returnTimer > 0f)
        {
            returnTimer -= delta;
            if (returnTimer <= 0f) shownTravel = travel;
        }
        else shownTravel = travel;

        if (CurrentPhase == Phase.Chasing && !breached)
        {
            breached = true;
            PlayFeedback(breachFeedback, NearestEdgePoint(ListenerPosition()), Facing());
        }
        PlaceBody();
        UpdateSlabVisuals(delta);
        UpdateEffects();
    }

    // 실제 출력 카메라의 뒤쪽 경계까지는 빠르게 따라붙되, 가속 이동이 화면 안으로 지나치지 않게 제한한다.
    // 길을 보여 주는 연출 카메라가 플레이어를 벗어나 비출 때는 가속하지 않는다.
    private float ChaseAdvance(float deltaTime)
    {
        float normal = currentSpeed * deltaTime;
        Camera camera = Camera.main;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (camera == null || player == null || !player.CanReceiveInput) return normal;
        if (courseStages != null)
            foreach (var stage in courseStages)
                if (stage != null && stage.IsRevealing) return normal;
        Vector3 viewport = camera.WorldToViewportPoint(player.transform.position);
        if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) return normal;

        Vector2 axis = TravelDirection;
        float boundary = float.PositiveInfinity;
        float depth = camera.WorldToViewportPoint(transform.position).z;
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = camera.ViewportToWorldPoint(new Vector3(i % 2, i / 2, depth));
            boundary = Mathf.Min(boundary, Vector2.Dot(corner, axis));
        }
        Vector2 origin = Origin;
        float wanted = boundary + visibleInset - Vector2.Dot(origin, axis);
        float safeLimit = Vector2.Dot((Vector2)player.transform.position - origin, axis) - 1.5f;
        wanted = Mathf.Min(wanted, safeLimit, currentMaxTravel);
        float catchUp = Mathf.Min(catchUpSpeed * deltaTime, Mathf.Max(0f, wanted - travel));
        return Mathf.Max(normal, catchUp);
    }

    // ───────────── 판정 ─────────────
    private void FixedUpdate()
    {
        if (CurrentPhase == Phase.Halted || retreating) return;
        Vector2 forward = TravelDirection, side = SpanDirection, origin = Origin;
        float edge = EdgeTravel;
        // 띠의 시작 끝부터 분쇄선까지, 띠 길이 전체를 한 상자로 본다. 양 끝과 뒤쪽은 edgeMargin만큼 넓다.
        Vector2 center = origin + side * (currentSpan * .5f) + forward * (travel * .5f);
        Vector2 size = new Vector2(currentSpan + edgeMargin * 2f, travel + edgeMargin * 2f);
        float angle = Mathf.Atan2(side.y, side.x) * Mathf.Rad2Deg;
        int mask = damageMask.value | enemyMask.value;
        int count;
        do
        {
            count = motor.OverlapAttackBox(center, size, angle, hits, mask);
            if (count < hits.Length) break;
            Array.Resize(ref hits, hits.Length * 2);
        } while (true);

        damagedPlayers.Clear();
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit == null || !hit.gameObject.activeInHierarchy) continue;
            int layer = 1 << hit.gameObject.layer;
            if ((layer & damageMask.value) != 0)
            {
                var player = hit.GetComponentInParent<PlayerHealth>();
                if (player == null || !damagedPlayers.Add(player)) continue;
                Vector2 point = hit.ClosestPoint(NearestEdgePoint(hit.bounds.center));
                // 반격·무적으로 막힌 접촉에는 갈리는 효과를 내지 않는다.
                if (TryDamage(player, point)) PlayFeedback(hitFeedback, point, Facing());
                // 이 피격으로 플레이어가 죽어 멈췄으면 같은 틱의 남은 분쇄도 하지 않는다.
                if (CurrentPhase == Phase.Halted) return;
            }
            else if ((layer & enemyMask.value) != 0)
            {
                var enemy = hit.GetComponentInParent<EnemyController>();
                if (enemy == null || !enemy.gameObject.activeInHierarchy) continue;
                // 보스로 나온 적은 갈지 않는다(그대로 지나간다).
                if (enemy.Health != null && enemy.Health.IsBossEnemy) continue;
                Vector2 core = hit.bounds.center;
                // 몸 가운데가 분쇄선에 닿으면 갈린다. 그 전까지는 날에 물린 채 움직인다.
                if (Vector2.Dot(core - origin, forward) > edge) continue;
                Shred(enemy, core);
            }
        }
        GrindSlabs(edge);
    }

    private bool TryDamage(PlayerHealth player, Vector2 point)
    {
        damageApplied = false;
        damageListener ??= (_, _) => damageApplied = true;
        player.OnDamageApplied += damageListener;
        hitSource = this;
        // 처음부터 다시 하는 분쇄기는 피해 없이 닿음만 알린다(옛 추격 레이저와 같다).
        var info = damage;
        info.damage = currentTouch == TouchResponse.DamageOnly ? currentDamage : 0f;
        try { player.TakeDamage(info, point); }
        finally
        {
            hitSource = null;
            player.OnDamageApplied -= damageListener;
        }
        return damageApplied;
    }

    // 적을 갈아 없앤다. 피해로 쓰러뜨린 것이 아니므로 체력·훈련 기록은 건드리지 않고, Wave·코스 생성기가 걸어 둔 사망 처리만 부른다.
    private void Shred(EnemyController enemy, Vector2 point)
    {
        PlayFeedback(enemyFeedback, point, Facing());
        if (body != null) body.EmitShred(point, TravelDirection, Gain(point));
        var health = enemy.Health;
        if (health != null && health.OnDeath != null) health.OnDeath.Invoke();
        // 걸어 둔 처리가 없거나 그 처리가 풀로 돌려보내지 않은 적은 여기서 돌려보낸다.
        if (enemy.gameObject.activeSelf && EnemyPool.Instance != null) EnemyPool.Instance.Despawn(enemy);
    }

    // ───────────── 생성 발판 ─────────────
    // 분쇄선이 칸을 완전히 지나면(칸의 앞쪽 끝까지) 그 칸을 지형에서 뺀다. 그 전까지는 충돌이 그대로라 설 수 있는 시간이 달라지지 않는다.
    private void GrindSlabs(float edge)
    {
        if (terrain == null) return;
        slabRefreshTimer -= Time.fixedDeltaTime;
        if (slabRefreshTimer <= 0f) RefreshStanding();
        bool ground = false;
        Vector2 last = default;
        while (firstStanding < standing.Count && standing[firstStanding].along + .5f <= edge)
        {
            var slab = standing[firstStanding++];
            if (!terrain.Grind(slab.cell)) continue;
            pieces.Add(slab);
            ground = true; last = slab.center;
        }
        if (!ground) return;
        if (body != null) body.EmitSlabBreak(last, TravelDirection, Gain(last));
        // 한 줄이 한꺼번에 갈려도 소리와 화면 떨림은 한 번만 낸다.
        if (Time.time < slabSoundBlock) return;
        slabSoundBlock = Time.time + .12f;
        PlayFeedback(slabFeedback, last, Facing());
    }

    /// <summary>
    /// 이 분쇄기의 띠가 쓸고 지나가는 칸인지: 띠 폭 안(양 끝은 피해 판정과 같은 edgeMargin)이고, 출발할 때 분쇄선보다 앞에 있는 1칸짜리 칸.
    /// 분쇄기는 지금 Wave의 생성 발판·지형 추가 칸 가운데 이 칸만 간다.
    /// </summary>
    public bool Sweeps(Vector2 cellCenter)
    {
        Vector2 offset = cellCenter - Origin;
        float along = Vector2.Dot(offset, TravelDirection), across = Vector2.Dot(offset, SpanDirection);
        return across + .5f > -edgeMargin && across - .5f < Span + edgeMargin && along + .5f > edgeMargin;
    }

    // 지금 켜져 있는 생성 발판을 진행 방향 순서로 다시 모은다(Wave가 발판을 시간차로 만들기 때문에 주기적으로 본다).
    private void RefreshStanding()
    {
        slabRefreshTimer = SlabRefreshInterval;
        standing.Clear(); firstStanding = 0;
        if (terrain == null) return;
        terrain.CollectGrindable(cellBuffer);
        Vector2 origin = Origin, forward = TravelDirection;
        foreach (var cell in cellBuffer)
        {
            Vector2 center = terrain.CellWorldCenter(cell);
            // 띠 폭 밖(옆 통로)의 칸과 시작선 뒤의 칸은 이 분쇄기가 갈지 않는다(대기 중에 뒤쪽 발판의 충돌이 빠지지 않게 한다).
            if (!Sweeps(center)) continue;
            standing.Add(new Slab { cell = cell, center = center, along = Vector2.Dot(center - origin, forward) });
        }
        standing.Sort((a, b) => a.along.CompareTo(b.along));
    }

    // 날에 닿은 발판은 가늘게 떨리고, 갈려 나온 조각은 크게 떨리며 어두워지다가 몸체 뒤로 들어가면 사라진다.
    private void UpdateSlabVisuals(float delta)
    {
        if (terrain == null || CurrentPhase != Phase.Chasing) return;
        // 분쇄선은 앞으로만 가므로 닿았던 칸은 갈릴 때까지 계속 닿아 있다. 다시 기다리거나 멈출 때 ReleaseSlabs가 제자리로 되돌린다.
        touched.Clear();
        float edge = EdgeTravel;
        Vector2 jitter = new Vector2(Mathf.PerlinNoise(Time.time * 31f, .37f) - .5f, Mathf.PerlinNoise(.61f, Time.time * 29f) - .5f) * 2f;
        Vector2 forward = TravelDirection;
        for (int i = firstStanding; i < standing.Count; i++)
        {
            var slab = standing[i];
            float sunk = edge - (slab.along - .5f);   // 분쇄선이 칸 뒤쪽 끝을 넘어 들어온 깊이
            if (sunk <= 0f) break;
            float amount = Mathf.Clamp01(sunk);
            terrain.PoseGrinding(slab.cell, jitter * contactShake, Mathf.Lerp(1f, .8f, amount));
            touched.Add(slab.cell);
            if (body != null) body.EmitContact(slab.center - forward * (.5f - sunk), forward, delta, Gain(slab.center));
        }
        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            var piece = pieces[i];
            float depth = edge - (piece.along + .5f);  // 칸 앞쪽 끝이 분쇄선 뒤로 들어간 깊이
            if (depth >= bladeDepth) { terrain.HideGround(piece.cell); pieces.RemoveAt(i); continue; }
            float amount = bladeDepth > 0f ? Mathf.Clamp01(depth / bladeDepth) : 1f;
            terrain.PoseGrinding(piece.cell, jitter * pieceShake, Mathf.Lerp(.8f, .25f, amount));
        }
    }

    // 닿아 있던 발판을 제자리 모습으로 되돌리고, 조각은 치운다. restore면 갈았던 발판을 모두 되살린다(구간을 처음부터 다시 시작할 때).
    private void ReleaseSlabs(bool restore)
    {
        if (terrain != null)
        {
            foreach (var cell in touched) terrain.SettleGrinding(cell);
            if (restore) terrain.RestoreGround();
            else foreach (var piece in pieces) terrain.HideGround(piece.cell);
        }
        touched.Clear(); pieces.Clear(); standing.Clear(); firstStanding = 0;
        slabRefreshTimer = 0f;
    }

    // ───────────── 몸체 ─────────────
    private void PlaceBody()
    {
        // 배치별 띠 폭이 정해진 뒤에 그 폭으로 맞춘 몸체를 빌린다.
        if (body == null && Application.isPlaying) body = ChasingGrinderBody.Rent(bodyPrefab, currentSpan);
        if (body == null) return;
        Vector2 center = Origin + SpanDirection * (currentSpan * .5f) + TravelDirection * (shownTravel + edgeMargin + HoldOffset());
        body.Place(center, TravelDirection);
        body.SetDrive(CurrentPhase == Phase.Chasing || returnTimer > 0f ? 1f : .55f);
        body.SetSparks(CurrentPhase == Phase.Chasing || returnTimer > 0f, Gain(NearestEdgePoint(ListenerPosition())), CurrentPhase == Phase.Chasing && travel < breachDistance);
    }

    // 기다리는 동안에는 숨어 있다가, 끝나기 직전에 분쇄선까지 올라온다. 분쇄선에서 진행 반대쪽으로 숨어 있는 거리(음수)다.
    private float HoldOffset()
    {
        if (returnTimer > 0f || CurrentPhase != Phase.Holding) return 0f;
        float lead = Mathf.Min(emergeDuration, currentHold);
        float rise = lead > 0f ? 1f - Mathf.Clamp01(timer / lead) : 1f;
        return -hideDepth * (1f - Mathf.SmoothStep(0f, 1f, rise));
    }

    // 구간이 끝나면 날을 멈추고 진행 반대쪽으로 물러나 사라진다. 다 물러난 뒤에 풀로 돌아간다.
    private void Retreat(float delta)
    {
        retreatTime += delta;
        float amount = retreatDuration > 0f ? Mathf.Clamp01(retreatTime / retreatDuration) : 1f;
        if (body != null)
        {
            Vector2 center = Origin + SpanDirection * (currentSpan * .5f) + TravelDirection * (retreatBase - retreatDistance * amount * amount);
            body.Place(center, TravelDirection);
            body.SetDrive(Mathf.Lerp(.55f, 0f, amount));
            body.SetSparks(false, 0f, false);
        }
        UpdateEffects();
        if (amount < 1f) return;
        retreating = false;
        ReleaseBody();
        if (returnPending) { returnPending = false; base.ForceReturnToPool(); }
    }

    private void ReleaseBody()
    {
        if (body != null) body.Release();
        body = null;
    }

    private Quaternion Facing() => Quaternion.FromToRotation(Vector3.up, (Vector3)TravelDirection);

    // ───────────── 소리·화면 떨림 ─────────────
    private static Vector2 ListenerPosition()
    {
        var listener = Camera.main;
        return listener != null ? (Vector2)listener.transform.position : Vector2.zero;
    }

    // 분쇄선 위에서 주어진 점과 가장 가까운 점.
    private Vector2 NearestEdgePoint(Vector2 point)
    {
        Vector2 start = Origin + TravelDirection * (shownTravel + edgeMargin);
        return start + SpanDirection * Mathf.Clamp(Vector2.Dot(point - start, SpanDirection), 0f, currentSpan);
    }

    /// <summary>카메라와의 거리에 따라 1(가까움)에서 0(멂)으로 줄어드는 효과 세기.</summary>
    private float Gain(Vector2 position)
    {
        var listener = Camera.main;
        if (listener == null) return 1f;
        // 카메라 깊이가 2D 음량에 영향을 주지 않도록 XY 거리만 쓴다.
        float distance = Vector2.Distance(listener.transform.position, position);
        float near = Mathf.Max(0f, effectNearDistance);
        float far = Mathf.Max(near, effectFarDistance);
        if (far <= near) return distance <= near ? 1f : 0f;
        float t = Mathf.InverseLerp(near, far, distance);
        return 1f - t * t * (3f - 2f * t);
    }

    private void UpdateEffects()
    {
        Vector2 nearest = NearestEdgePoint(ListenerPosition());
        float gain = Gain(nearest);
        bool running = !retreating && CurrentPhase == Phase.Chasing;
        // 기다리는 동안에는 올라오기 시작할 때부터 소리와 떨림이 차오른다(아래에서 다가오는 것을 미리 알린다).
        float rise = 0f;
        if (!retreating && CurrentPhase == Phase.Holding && returnTimer <= 0f)
        {
            float lead = Mathf.Min(emergeDuration, currentHold);
            rise = lead > 0f ? 1f - Mathf.Clamp01(timer / lead) : 0f;
        }
        UpdateLoop(nearest, gain * (running ? 1f : rise * .6f));
        UpdateRumble(nearest, rumbleShake * gain * (running ? 1f : rise * .7f));
    }

    private void UpdateLoop(Vector2 position, float target)
    {
        float step = loopFadeTime > 0f ? Time.unscaledDeltaTime / loopFadeTime : 1f;
        loopGain = Mathf.MoveTowards(loopGain, target, step);
        if (loopGain <= 0f) { StopLoop(); return; }
        if (loopHandle == null || !loopHandle.IsValid)
        {
            if (loopSound == null || loopSound.clip == null || SoundManager.Instance == null) return;
            if (Time.unscaledTime < nextLoopRetryTime) return;
            nextLoopRetryTime = Time.unscaledTime + .12f;
            loopHandle = SoundManager.Instance.PlayLoop(loopSound, position, null);
            if (loopHandle == null || !loopHandle.IsValid) return;
            loopBaseVolume = loopHandle.GetSource().volume;
        }
        loopHandle.SetPosition(position);
        loopHandle.GetSource().volume = loopBaseVolume * loopGain;
    }

    private void StopLoop()
    {
        if (loopHandle != null && loopHandle.IsValid) loopHandle.Stop();
        loopHandle = null;
        loopGain = 0f;
    }

    // 쫓아오는 동안 화면이 가늘게 계속 떨린다. 세기가 눈에 띄게 달라질 때만 다시 건다.
    private void UpdateRumble(Vector2 position, float level)
    {
        if (level < .01f) { StopRumble(); return; }
        if (rumble != null && Mathf.Abs(level - rumbleLevel) < .02f) return;
        StopRumble();
        if (FeedbackPlayer.Instance == null) return;
        rumble = FeedbackPlayer.Instance.BeginHeldShake(level, position);
        rumbleLevel = level;
    }

    private void StopRumble()
    {
        if (rumble == null) return;
        // 계속 이어지는 떨림이라 끝내지 않으면 영원히 남는다. 씬 정리 순서 때문에 FeedbackPlayer가 먼저 사라졌어도 직접 끝낸다.
        if (FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.EndHeldShake(rumble);
        else rumble.Cancel(CinemachineImpulseManager.Instance.CurrentTime, false);
        rumble = null;
        rumbleLevel = 0f;
    }

    // 파티클은 그대로, 소리와 카메라 흔들림은 카메라와의 거리에 맞춰 줄여 재생한다.
    private void PlayFeedback(FeedbackDataSO data, Vector2 position, Quaternion rotation)
    {
        if (data == null || FeedbackPlayer.Instance == null) return;
        float gain = Gain(position);
        if (!distanceFeedbacks.TryGetValue(data, out var copy) || copy == null)
        {
            // 공유 에셋을 바꾸지 않도록 사본 하나를 만들어 계속 쓴다.
            copy = Instantiate(data);
            copy.name = data.name + " (Distance Instance)";
            copy.hideFlags = HideFlags.DontSave;
            if (copy.particlePrefabs == null) copy.particlePrefabs = Array.Empty<GameObject>();
            var sound = data.sound;
            if (sound != null)
                copy.sound = new SoundOptions
                {
                    bus = sound.bus, clip = sound.clip, volume = sound.volume, pitch = sound.pitch,
                    spatialBlend = sound.spatialBlend, randomizePitch = sound.randomizePitch,
                    pitchRange = sound.pitchRange, minInterval = sound.minInterval,
                    maxSimultaneous = sound.maxSimultaneous, crowdedVolumeMultiplier = sound.crowdedVolumeMultiplier
                };
            distanceFeedbacks[data] = copy;
        }
        if (copy.sound != null && data.sound != null)
        {
            copy.sound.volume = data.sound.volume * gain;
            // 음량 0인 단발음도 공용 동시 재생 수를 차지하므로 들리지 않는 소리는 재생 요청을 뺀다.
            copy.sound.clip = gain > 0f ? data.sound.clip : null;
        }
        copy.shakeIntensity = data.shakeIntensity * gain;
        FeedbackPlayer.Instance.PlayExact(copy, position, rotation);
    }

    // ───────────── 수명 ─────────────
    /// <summary>구간 목표에 닿으면 판정을 끄고 물러난다. 더 이상 다시 시작하지 않는다.</summary>
    public void Halt()
    {
        // 진행 중이던 되돌리기(암전·이동)도 멈춘다. 끝난 Wave의 시작 좌표로 옮기거나 다음 Wave 준비에 암전이 겹치지 않게 한다.
        if (playerReturn.IsRunning) StopReturn();
        if (CurrentPhase == Phase.Halted) return;
        // 지금 몸체가 보이는 자리(숨어 있던 중이면 숨은 자리)에서 물러나기 시작한다.
        retreatBase = shownTravel + edgeMargin + HoldOffset();
        holdRequested = false;
        CurrentPhase = Phase.Halted;
        CurrentSpeed = 0f;
        Unsubscribe();
        ReleaseSlabs(false);
        StopRumble();
        returnTimer = 0f;
        if (body != null && isActiveAndEnabled) { retreating = true; retreatTime = 0f; }
        else StopLoop();
    }

    // 처음부터 다시 하는 분쇄기에 닿으면(이 분쇄기든 같은 Wave의 다른 분쇄기든) 다음 프레임에 시작 위치에서 다시 기다린다.
    // 코스 적이나 피해만 주는 분쇄기에게 맞은 피해는 무시한다.
    // 이 피격으로 패배가 확정됐으면(훈련 성적 미흡) 처음으로 돌아가지 않는다(위치 초기화도 같은 경우에 되돌리지 않는다).
    private void HandlePlayerDamaged(WeaponBaseSO.DamageInfo _)
    {
        if (CurrentPhase == Phase.Halted || !IsGrinderHit) return;
        if (subscribedHealth != null && (subscribedHealth.IsDead || subscribedHealth.IsDeathPending)) return;
        holdRequested = true;
        // 위치 초기화 장치가 없는 Wave면 닿은 분쇄기가 직접 이 Wave의 시작 좌표로 돌려보낸다.
        if (hitSource == this && !TutorialPositionResetter.AnyActive) BeginReturn();
    }

    private void BeginReturn()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || playerReturn.IsRunning || !isActiveAndEnabled || !TryGetWaveStart(out var destination)) return;
        returnRoutine = StartCoroutine(playerReturn.Run(player, destination, false, ReturnFadeOut, ReturnBlackHold, ReturnFadeIn));
    }

    // 지금 Wave의 시작 좌표(격자 편집기의 "Wave N 시작점"). Wave 시작 때 크레인이 옮겨 놓는 자리와 같은 좌표계로 구한다.
    private static bool TryGetWaveStart(out Vector3 position)
    {
        position = default;
        var waves = WaveManager.ActiveInstance;
        var data = waves != null ? waves.Data : null;
        if (data == null || data.waves == null || data.waves.Length == 0) return false;
        var cells = waves.GetComponentInParent<WaveCellTerrain>(true);
        var room = waves.GetComponentInParent<Room>(true);
        var coordinates = cells != null ? cells.transform : room != null ? room.transform : waves.transform;
        position = coordinates.TransformPoint(WaveRoomState.PlayerStartAt(data, Mathf.Clamp(waves.currentWaveIndex, 0, data.waves.Length - 1)));
        return true;
    }

    private void StopReturn()
    {
        if (returnRoutine != null) StopCoroutine(returnRoutine);
        returnRoutine = null;
        playerReturn.Recover(ReturnFadeIn);
    }

    // 플레이어가 죽으면 그 자리에 멈춘다. 패배 화면 동안 쫓거나 발판·적을 갈지 않고, 소리와 화면 떨림도 끈다
    // (Wave도 패배로 멈추며 적과 지형을 그대로 둔다). 재도전·로비 복귀 때 풀 정리가 바로 돌려보낸다(물러나지 않는다).
    private void HandlePlayerDeath()
    {
        // 되돌리는 도중에 죽으면 되돌림을 멈추고 화면과 입력을 돌려준다(사망 연출과 겹치지 않게 한다).
        if (playerReturn.IsRunning) StopReturn();
        if (CurrentPhase == Phase.Halted) return;
        holdRequested = false;
        CurrentPhase = Phase.Halted;
        CurrentSpeed = 0f;
        returnTimer = 0f;
        Unsubscribe();
        // 날에 닿아 떨던 발판은 제자리 모습으로 돌린다. 갈려 나온 조각은 그대로 두고 Wave 정리가 치운다.
        if (terrain != null) foreach (var cell in touched) terrain.SettleGrinding(cell);
        touched.Clear();
        StopRumble();
        StopLoop();
        if (body != null)
        {
            body.SetDrive(0f);
            body.SetSparks(false, 0f, false);
        }
    }

    private void Subscribe()
    {
        Unsubscribe();
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || player.Health == null) return;
        subscribedHealth = player.Health;
        subscribedHealth.OnDamaged += HandlePlayerDamaged;
        subscribedHealth.OnPlayerDeath += HandlePlayerDeath;
    }

    private void Unsubscribe()
    {
        if (subscribedHealth != null)
        {
            subscribedHealth.OnDamaged -= HandlePlayerDamaged;
            subscribedHealth.OnPlayerDeath -= HandlePlayerDeath;
        }
        subscribedHealth = null;
    }

    public override void ForceReturnToPool()
    {
        if (returnPending || !gameObject.activeSelf) return;
        Halt();
        // 몸체가 물러나는 동안은 남아 있다가, 다 물러난 뒤에 풀로 돌아간다.
        if (retreating) { returnPending = true; return; }
        ReleaseBody();
        base.ForceReturnToPool();
    }

    // 풀 전체 정리나 방 이탈로 바로 꺼질 때도 구독·소리·떨림·몸체가 남지 않게 한다. 지형은 같은 정리에서 스스로 되돌린다.
    private void OnDisable()
    {
        Unsubscribe();
        if (playerReturn.IsRunning) StopReturn();
        StopLoop();
        StopRumble();
        ReleaseBody();
        touched.Clear(); pieces.Clear(); standing.Clear(); firstStanding = 0;
        holdRequested = false; retreating = false; returnPending = false;
        if (hitSource == this) hitSource = null;
    }

    // ───────────── 격자 편집기 세부 설정 ─────────────
    private static readonly string[] touchOptions = { "처음부터 다시", "피해만 주고 계속" };
    private WaveDeviceParameter WidthParameter => new WaveDeviceParameter("width", "띠 폭",
        "분쇄 띠의 길이입니다. 통로의 안쪽 폭에 맞춥니다. 놓은 칸에서 진행 방향의 오른쪽으로 뻗습니다(격자에 그려지는 범위로 확인).", "칸",
        ChasingGrinderLayout.MinSpan, ChasingGrinderLayout.MaxSpan, 1f, span);
    private WaveDeviceParameter SpeedParameter => new WaveDeviceParameter("speed", "추격 속도",
        "초당 나아가는 거리입니다. 플레이어가 화면 앞쪽에 있으면 화면 뒤쪽 경계까지는 빠르게 따라붙습니다.", "칸/초", .25f, 12f, .05f, speed);
    private WaveDeviceParameter HoldParameter => new WaveDeviceParameter("hold", "출발 대기",
        "나온 뒤 쫓기 시작하기 전에 숨어서 기다리는 시간입니다. 닿아서 처음부터 다시 할 때도 같은 시간을 기다립니다.", "초", 0f, 10f, .1f, holdDuration);
    private WaveDeviceParameter DistanceParameter => new WaveDeviceParameter("distance", "최대 거리",
        "놓은 자리에서 나아갈 수 있는 최대 거리입니다. 격자에 그려지는 범위의 길이입니다.", "칸", 2f, 300f, 1f, maxTravel);
    private WaveDeviceParameter TouchParameter => new WaveDeviceParameter("touch", "닿았을 때",
        "처음부터 다시: 피해 없이 이 Wave의 시작 좌표(위치 초기화 장치가 있으면 그 자리)로 돌려보내고, 갈린 발판을 되살린 뒤 분쇄기도 처음부터 다시 쫓습니다.\n" +
        "피해만 주고 계속: 피해량만큼 피해를 주고 계속 쫓습니다.", touchOptions, (float)touchResponse);
    private WaveDeviceParameter DamageParameter => new WaveDeviceParameter("damage", "피해량",
        "'피해만 주고 계속'일 때 닿으면 주는 피해입니다. 피격 뒤 무적 시간 동안은 다시 피해를 주지 않습니다.", "", 0f, 100f, 1f, damage.damage);

    public WaveDeviceParameter[] Parameters => new[] { WidthParameter, SpeedParameter, HoldParameter, DistanceParameter, TouchParameter, DamageParameter };
    // 격자 편집기의 그림은 작은 표식이라 설정과 무관하다. 띠 폭·거리는 범위(TryGetRange)로 그린다.
    public bool SettingsChangeShape => false;

    public void ApplySettings(WaveDeviceSetting[] overrides)
    {
        overrides ??= WaveDeviceSettings.None;
        currentSpan = ChasingGrinderLayout.ClampSpan(WaveDeviceSettings.Value(overrides, WidthParameter));
        currentSpeed = WaveDeviceSettings.Value(overrides, SpeedParameter);
        currentHold = WaveDeviceSettings.Value(overrides, HoldParameter);
        currentMaxTravel = WaveDeviceSettings.Value(overrides, DistanceParameter);
        currentTouch = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, TouchParameter)) == (int)TouchResponse.DamageOnly ? TouchResponse.DamageOnly : TouchResponse.Restart;
        currentDamage = WaveDeviceSettings.Value(overrides, DamageParameter);
        settingsApplied = true;
        if (!Application.isPlaying || !isActiveAndEnabled || CurrentPhase != Phase.Holding || travel > 0f || returnTimer > 0f) return;
        // 막 나온 개체(처음 기다리는 중)면 바뀐 대기 시간으로 다시 세고, 몸체는 바뀐 띠 폭으로 다시 빌린다.
        timer = currentHold;
        if (body != null && !Mathf.Approximately(body.Span, currentSpan)) ReleaseBody();
    }

    public bool TryGetRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        // 띠의 시작 끝에서 최대 거리(분쇄선 끝)까지 쓸고 지나가는 넓이. 장치 원점 기준, 회전 전 좌표다.
        float width = ChasingGrinderLayout.ClampSpan(WaveDeviceSettings.Value(overrides, WidthParameter));
        float distance = WaveDeviceSettings.Value(overrides, DistanceParameter) + edgeMargin;
        Vector2 forward = travelDirection.normalized, side = spanDirection.normalized;
        Vector2 corner = anchorAtCellCorner ? -(forward + side) * .5f : Vector2.zero;
        Vector2 center = corner + side * (width * .5f) + forward * (distance * .5f);
        Vector2 size = new Vector2(Mathf.Abs(side.x) * width + Mathf.Abs(forward.x) * distance, Mathf.Abs(side.y) * width + Mathf.Abs(forward.y) * distance);
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, center, size, new Color(1f, .22f, .16f, .9f));
        return true;
    }

    private void OnDestroy()
    {
        foreach (var copy in distanceFeedbacks.Values)
            if (copy != null) Destroy(copy);
        distanceFeedbacks.Clear();
    }
}
