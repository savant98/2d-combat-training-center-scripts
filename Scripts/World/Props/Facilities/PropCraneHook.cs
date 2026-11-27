using System;
using System.Collections;
using UnityEngine;

// 경로 크레인(DESIGN_V4 §2.3 — 4차 재작성 · 5차 DESIGN_V5 §1 왕복 도크, Prefab DungeonPropCraneHook 같은 GUID). 붙기(↑)로 매달리면 배치 설정의 다각선 경로를 따라
// 반대편 도크(승강장)까지 옮겨 주고, 도착 도크에서 매달린 채 기다린다(점프·대시·다른 크레인 붙기로 내림).
// - 5차 왕복: 경로 양 끝(점 0 = 집, 마지막 점)이 도크 2곳이다. 크레인은 마지막으로 도착한 도크(처음은 집)에서 쉬고, 거기서 붙으면 반대 도크로 간다(경로를 뒤집어 운반).
//   도착 도크에서 내리면 그 도크가 쉬는 자리가 된다(돌아가지 않는다 — 경로를 다 간 뒤 대기로 바뀌기 전 한 물리 틱 안에 내려도 같다). 도중에 내리면(점프·대시·연쇄 붙기·자동 하차) 빈 채로 출발 도크로 40칸/초 복귀한다(복귀 중 크레인 자신은 붙기 대상 아님).
//   6차 T5 즉시 생성(5차 호출 B안 대체): 크레인이 없는 도크는 '호출 지점'(IZipCallTarget)으로 붙기 목록에 오른다(쉬는 중이면 반대 도크, 복귀 중이면 두 도크 모두).
//   그 도크를 고르고 ↑ → Summon: 옛 자리에서 파괴 연출(자기 불꽃 입자 + 기존 해제음) → 같은 프레임에 그 도크에서 쉬는 크레인으로 옮기고 자세를 바로 맞춤
//   → 새 자리 생성 연출(빈 도크 유령 윤곽 섬광 0.18초·도크등·불꽃 입자·기존 전원음 하나) → FacilityZip이 같은 프레임에 붙기를 시작한다(호출 이동·자동 붙기 대기 없음).
//   붙기가 막히면 크레인은 그 도크에 생긴 채 기다린다. 흔들림·새 음원 없음. 붙은 뒤 0.12초(DepartDelay) 기다렸다 출발한다(체결 흔들림 첫 반 주기가 읽히게).
// - 배치: footprint 2×4, 집(점 0)의 매달린 발 = (x + 1, y). 경로 점 i(1..7)는 집 기준 칸 오프셋(cranePath{i}X·Y, 눈금 0.5), 점 수 cranePathCount(2~8).
//   캐리지는 '매달린 발 + 4칸' 높이의 레일(빛 통로) 위를 위아래 바퀴로 물고 달린다. 경로 공간 규칙(발 ~ 발 + 4칸, 폭 2칸 쓸기 상자가 고체와 겹치지 않음)은 템플릿 검사가 맡는다.
// - 이동(AGENTS 물리 규칙): FacilityRide.HaulPath — 일정 속도 craneSpeed(5차 기본 24칸/초 = 대시 30의 0.8배), 시작·끝 0.1초만 가감속, 물리 틱마다 ShiftConfinement(위치 직접 대입 없음).
//   빈 크레인의 복귀 이동·즉시 생성은 표시만 바꾼다(판정 없음 — 경로 위 거리 pathS만 바꾼다).
// - 외형(5차 S3 '넓은 Π'): 레일을 문 캐리지(위 바퀴 2·아래 안내 롤러 2)·감기 드럼(이동 중 회전)·진행 방향 화살표 2개(다음 도크 쪽으로 회전, 이동 중 차례로 점멸)·
//   벌어진 케이블 2가닥·긴 스프레더 머리(±0.98)·운반 케이지(캐리지에 붙음 — 머리 흔들림과 따로)·양 끝 도크(정지대 블록 + 게이트 기둥 + 파랑 도크등 + 빈 도크의 유령 윤곽 —
//   6차: 캐리지·케이지에 스프레더 점선을 더함).
// - 6차 T4: 붙기 직선·표적 괄호는 머리(스프레더) 중심 = 매달린 발 + 1.97(빈 도크는 생겨날 머리 자리)을 가리킨다. 괄호 상자 ±1.25 × ±0.6(돌지 않음),
//   키 안내는 머리 바로 아래 매달린 발 + 1.25(선택된 빈 도크도 같은 자리).
//   레일은 파랑 2가닥 선 + 흐린 띠 + 1칸마다 침목 + 꺾이는 점 관절 고리 + 흐르는 빛 덩어리(다음 도크 쪽으로). 기능부는 파랑, 청록은 붙기 강조에만.
// - 탑승부 효과(R7): 탑승 중 케이지 표시등 최대·케이지 아래 작은 추진 불꽃·속도 반대쪽 잔선, 이동 중 트롤리 접점 불꽃(속도 비례) + RollerSparkFeedback 0.25초 간격,
//   구동음 travelLoop(기존 효과음). 탑승 중 수레는 Player의 화면 보간 위치(FixedMotionBody)를 따른다.
[DisallowMultipleComponent]
public sealed class PropCraneHook : ZipCraneBase
{
    public const string PathCountKey = "cranePathCount";
    public const string SpeedKey = "craneSpeed";
    public const int MinPoints = 2, MaxPoints = 8;
    /// <summary>트롤리(레일)가 매달린 발보다 높은 칸 수.</summary>
    public const float TrackHeight = 4f;
    /// <summary>경로 끝 감속 시간(초).</summary>
    public const float PathEase = .1f;
    /// <summary>출발·반전 후 최고 속도(activeSpeed)까지 가속하는 시간(초).</summary>
    public const float AccelTime = 1f;
    /// <summary>도착 도크 직전 감속 시간(초).</summary>
    public const float DecelEase = .12f;
    /// <summary>진행 반대 방향 입력 시 멈춰 서는 덜컹 정지 시간(초).</summary>
    public const float ReverseStopDelay = .22f;
    /// <summary>방향 전환 직후 중복 반전 방지 쿨다운(초).</summary>
    public const float ReverseCooldown = .15f;
    /// <summary>도착 도크 대기 상태에서 방향키 재출발 허용 전 짧은 완충 시간(초).</summary>
    public const float WaitDepartCooldown = .12f;
    /// <summary>탑승 직후 출발 구간에서 의도치 않은 즉시 역주행(출발점 복귀 멈춤)을 막는 입력 여유 시간(초).</summary>
    public const float MountReverseGrace = .35f;
    /// <summary>방향 입력으로 인식하는 최소 입력 크기.</summary>
    public const float ReverseInputThreshold = .35f;
    /// <summary>입력 방향과 실제 운동 방향(좌/우·상/하 벡터)의 내적 판정 기준(0.25 ≈ 75° 이내 정렬/반대).</summary>
    public const float DirectionAlignThreshold = .25f;
    /// <summary>이동 중 RollerSparkFeedback 재생 기본 간격(초, 기존 0.25초의 2배 빈도).</summary>
    public const float DefaultSparkInterval = .125f;
    /// <summary>5차 기본 이동 속도(칸/초, 대시 30의 0.8배 — DESIGN_V5 판단값).</summary>
    public const float DefaultSpeed = 24f;
    /// <summary>도중 하차 시 빈 크레인 복귀 속도(칸/초) — 내렸다가 다시 붙을 수 있게 천천히 돌아간다.</summary>
    public const float ReturnSpeed = 6f;
    /// <summary>5차 체결 뒤 출발 지연(초).</summary>
    public const float DepartDelay = .12f;
    /// <summary>6차 T4: 표시 기준점 G의 머리 로컬 y(머리 원점 = 매달린 발 + 2 → G = 매달린 발 + 1.97, 강조 테두리 중심 — 스프레더·훅 블록·집게를 한 상자에 담는다).</summary>
    public const float HeadAimY = -.03f;
    /// <summary>6차 T4: 매달린 발 → 표시 기준점 G(빈 도크 = 생겨날 머리 자리).</summary>
    public const float AimOffset = 1.97f;
    /// <summary>6차 T4: 표적 괄호 상자 반크기(강조 상자 1.1 × 0.43 + 여유). 생성 도구가 같은 값으로 괄호 Mesh를 굽는다.</summary>
    public static readonly Vector2 FrameHalf = new Vector2(1.25f, .6f);
    /// <summary>6차 T4: 키 안내 자리 = 매달린 발 + 1.25(머리 바로 아래 — 예전 0.1은 꺼진 케이지 바닥의 허공이었다).</summary>
    public const float PromptOffset = 1.25f;
    /// <summary>6차 T5: 즉시 생성 직후 새 도크 유령 윤곽·도크등 섬광 시간(초, 판단값).</summary>
    public const float SummonFlashTime = .18f;
    /// <summary>6차 T5: 옛 자리 파괴·새 자리 생성 때 터뜨리는 불꽃 입자 수(판단값 — 곧 붙기 도착 때 14개가 다시 터진다).</summary>
    public const int VanishSparks = 10, AppearSparks = 8;

    /// <summary>경로 점 i(1..7)의 가로·세로 설정 키.</summary>
    public static string PathXKey(int i) => "cranePath" + i + "X";
    public static string PathYKey(int i) => "cranePath" + i + "Y";

    [Header("경로(배치 기본값)")]
    [SerializeField, Range(MinPoints, MaxPoints)] private int pathCount = 2;
    [SerializeField, Tooltip("점 1..7의 집 기준 오프셋(칸). 0번 칸은 쓰지 않는다.")] private Vector2[] pathDefaults = { Vector2.zero, new Vector2(8f, 0f), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero };
    [SerializeField, Range(10f, 40f), Tooltip("경로 이동 속도(칸/초).")] private float travelSpeed = DefaultSpeed;

    [Header("경로 크레인 구성(생성 도구가 연결)")]
    [SerializeField, Tooltip("트롤리·케이블·머리·케이지를 함께 옮기는 수레(원점 = 매달린 발).")] private Transform carriage;
    [SerializeField, Tooltip("레일을 문 캐리지(원점 = 레일 위 점 — 구간 방향으로 회전).")] private Transform trolley;
    [SerializeField] private Transform[] trolleyWheels = Array.Empty<Transform>();
    [SerializeField, Tooltip("감기 드럼(이동 중 회전).")] private Transform winchDrum;
    [SerializeField, Tooltip("진행 방향 화살표 2개(다음 도크 쪽으로 회전).")] private Transform[] directionArrows = Array.Empty<Transform>();
    [SerializeField] private Renderer[] arrowLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("레일 2가닥 선(파랑).")] private MeshFilter pathLines;
    [SerializeField, Tooltip("레일 흐린 띠.")] private MeshFilter pathBand;
    [SerializeField, Tooltip("꺾이는 점 관절 고리.")] private MeshFilter pathJoints;
    [SerializeField, Tooltip("1칸마다 침목.")] private MeshFilter pathTies;
    [SerializeField] private Renderer[] pathLineLights = Array.Empty<Renderer>();
    [SerializeField] private Renderer[] pathBandLights = Array.Empty<Renderer>();
    [SerializeField] private Renderer[] pathJointLights = Array.Empty<Renderer>();
    [SerializeField] private Renderer[] pathTieLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("레일을 따라 흐르는 빛 덩어리(이동만).")] private Transform[] pulses = Array.Empty<Transform>();
    [SerializeField] private Renderer[] pulseLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("도크 2곳(0 = 집, 1 = 끝 — 경로 양 끝으로 옮긴다, 이동만).")] private Transform[] docks = Array.Empty<Transform>();
    [SerializeField, Tooltip("도크 정지대 블록 2개(바깥쪽으로 회전).")] private Transform[] dockStops = Array.Empty<Transform>();
    [SerializeField, Tooltip("도크등(도크마다 하나, 파랑).")] private Renderer[] dockLampLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("빈 도크의 캐리지·케이지 유령 윤곽(도크마다 하나).")] private Renderer[] dockGhostLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("도크 몸체(머리 보·기둥·정지대) — 격자 Tool 미리보기에 남지 않게 꺼 둔 채 저장하고 생성 때 켠다.")] private Renderer[] dockBodies = Array.Empty<Renderer>();
    [SerializeField, Tooltip("속도 반대쪽 잔선(실행 중 다시 그림).")] private MeshFilter wake;
    [SerializeField] private Renderer[] wakeLights = Array.Empty<Renderer>();
    [SerializeField] private ParticleSystem thruster;
    [SerializeField] private ParticleSystem trolleySparks;
    [SerializeField] private float lineWidth = .04f;
    [SerializeField] private float lineSpacing = .18f;
    [SerializeField] private float bandWidth = .55f;
    [SerializeField] private float jointRadius = .16f;
    [SerializeField] private float tieSpacing = 1f;
    [SerializeField] private float tieHalfLength = .22f;
    [SerializeField] private float tieWidth = .05f;

    [Header("경로 크레인 효과(기존 효과음·효과 재사용)")]
    [SerializeField] private SoundOptions travelLoop;
    [SerializeField, Tooltip("이동 중 진행 반대 방향 입력 시 덜컹 멈춤 효과음.")] private SoundOptions reverseClunkSound;
    [SerializeField] private FeedbackDataSO sparkFeedback;
    [SerializeField, Min(.05f)] private float sparkInterval = DefaultSparkInterval;

    // 빈 도크 호출 지점(붙기 목록에 따로 오른다 — 6차 T5: 고르면 크레인이 그 도크에 즉시 생기고 같은 프레임에 붙는다).
    private sealed class DockPoint : IZipAnchor, IZipCallTarget
    {
        private readonly PropCraneHook hook;
        public readonly int Index;
        public DockPoint(PropCraneHook hook, int index) { this.hook = hook; Index = index; }
        public bool ZipAvailable => hook.DockCallable(Index);
        public Vector2 ZipFeet => hook.DockFeet(Index);
        public float ZipRadius => hook.ZipRadius;
        public Vector2 ZipHookPoint => hook.DockFeet(Index) + Vector2.up * 1.8f;
        public bool HasRider(PlayerController player) => false;
        public void SetZipFocus(bool focused) => hook.SetDockFocus(Index, focused);
        public void BeginZipIn(PlayerController player) { }
        public void EndZipIn(bool arrived) { }
        public bool AcceptZip(PlayerController player) => false;
        public void ReleaseForZip(PlayerController player) { }
        public bool Call(PlayerController player) => hook.Call(player, Index);
        public IZipAnchor Owner => hook;
    }

    private int activeCount;
    private readonly Vector2[] activeOffsets = new Vector2[MaxPoints];
    private float activeSpeed = DefaultSpeed;
    private Vector2[] worldPath = Array.Empty<Vector2>();
    private Vector2[] localTrack = Array.Empty<Vector2>();
    private float pathLength;
    private float pathS;                    // 수레가 있는 경로 위 거리(0 = 집 도크, pathLength = 끝 도크)
    private int restDock, targetDock;
    private int originDock, arrivedDock;    // 이번 탑승의 원래 출발 도크·최종 도착 도크(중간 반전·복귀 중 재탑승 구분)
    private int rideDirSign = 1;            // 탑승 이동 진행 부호(+1 = 집 → 끝, −1 = 끝 → 집)
    private float moveFrom, moveDistance, moveSign, moveTime;
    private float summonFlash = -1f;        // 6차 T5: 즉시 생성 뒤 경과(음수 = 섬광 없음)
    private int summonDock;
    private readonly bool[] dockFocus = new bool[2];
    private DockPoint[] dockPoints = Array.Empty<DockPoint>();
    private float flow;
    private float sparkClock;
    private float cageFlash = -1f;
    private Vector3 builtAt = new Vector3(float.NaN, 0f, 0f);
    private Vector2 lastCarriageFeet;
    private bool carriagePlaced;
    private Coroutine routine;
    private bool hauledToDock;              // 운반 경로를 다 갔지만 마지막 이동량 반영 틱을 기다리는 중(5차 검토 p2 — 이때 내려도 도착으로 본다)
    private Mesh linesBuilt, bandBuilt, jointsBuilt, tiesBuilt, wakeBuilt;
    private string wakeMeshName;   // 잔선 Mesh 이름(처음 그릴 때 한 번만 만든다 — 탑승 이동 중 매 프레임 문자열 할당 방지, fix4zip)
    private FacilityLights lineLamp, bandLamp, jointLamp, tieLamp, pulseLamp, wakeLamp, arrowLamp, dockLamp, ghostLamp;
    private readonly FacilityLoopSound loop = new FacilityLoopSound();
    private bool wakeShown;

    /// <summary>마지막 탑승이 반대 도크까지 갔는지(검사용).</summary>
    public bool LastRideArrived { get; private set; }
    /// <summary>도중 하차 뒤 출발 도크로 돌아온 횟수(검사용).</summary>
    public int Returns { get; private set; }
    /// <summary>6차 T5: 빈 도크 호출로 그 도크에 즉시 생성한 수·파괴/생성 연출을 낸 수(검사용, 대여마다 0).</summary>
    public int Calls { get; private set; }
    public int SummonEffects { get; private set; }
    /// <summary>6차 T5: 마지막 즉시 생성 직전의 수레 매달린 발(옛 자리, 월드, 검사용).</summary>
    public Vector2 LastSummonFrom { get; private set; }
    /// <summary>6차 T5: 새 도크 섬광이 진행 중인지(검사용).</summary>
    public bool SummonFlashing => summonFlash >= 0f;
    /// <summary>탑승 중 찍은 최고 속도(칸/초, 검사용).</summary>
    public float PeakSpeed { get; private set; }
    /// <summary>5차: 빈 크레인 복귀 이동 중 찍은 최고 속도(칸/초, 검사용 — 6차: 즉시 생성은 이동이 없어 올리지 않는다).</summary>
    public float PeakMoveSpeed { get; private set; }
    /// <summary>5차: 붙은 뒤 실제로 출발하기까지 걸린 시간(초, 검사용).</summary>
    public float LastDepartWait { get; private set; }
    /// <summary>탑승 이동 중 현재 속도(칸/초, 검사용).</summary>
    public float CurrentRideSpeed { get; private set; }
    /// <summary>탑승 이동 중 현재 진행 부호(+1 = 집 → 끝, −1 = 끝 → 집, 검사용).</summary>
    public int RideDirectionSign => rideDirSign;
    /// <summary>현재 위치·진행 부호 기준 플레이어의 실시간 운동 방향 단위 벡터(좌↔우, 하↔상, 검사용).</summary>
    public Vector2 CurrentTravelDirectionNow => CurrentTravelDirection(pathS, rideDirSign);
    /// <summary>이동 중 반대 방향 입력으로 반전한 횟수(검사용).</summary>
    public int Reversals { get; private set; }
    /// <summary>반전 정지 시 덜컹 효과음을 재생한 횟수(검사용).</summary>
    public int ReverseClunks { get; private set; }
    /// <summary>경로 끝 도크 도달 시 덜컹 연출을 재생한 횟수(검사용).</summary>
    public int DockArrivalClunks { get; private set; }
    /// <summary>도착 도크 대기 상태에서 방향키로 재출발한 횟수(검사용).</summary>
    public int DeparturesFromWait { get; private set; }
    /// <summary>복귀 도중 재탑승한 횟수(검사용).</summary>
    public int MidReturnMounts { get; private set; }
    /// <summary>이동 중 스파크 효과 재생 간격(초, 검사용).</summary>
    public float SparkInterval => sparkInterval;
    /// <summary>현재 트롤리 스파크 파티클 초당 방출률(검사용).</summary>
    public float TrolleySparkRate => trolleySparks != null ? trolleySparks.emission.rateOverTime.constant : 0f;
    /// <summary>검사용 방향 입력 덮어쓰기(입력 장치 없이 반대 방향 반전 검증).</summary>
    public Vector2? RideInputOverride { get; set; }
    /// <summary>지금 경로 점(월드 매달린 발, 검사용).</summary>
    public Vector2[] WorldPath => worldPath;
    /// <summary>경로 길이(칸).</summary>
    public float PathLength => pathLength;
    /// <summary>수레가 있는 경로 위 거리(검사용).</summary>
    public float PathPosition => pathS;
    /// <summary>지금 이동 속도 설정(칸/초).</summary>
    public float Speed => activeSpeed;
    /// <summary>지금 수레의 매달린 발(월드, 검사용).</summary>
    public Vector2 CarriageFeet => lastCarriageFeet;
    /// <summary>5차: 쉬는 도크(0 = 집, 1 = 끝, 검사용).</summary>
    public int RestDock => restDock;
    /// <summary>5차: 도크 i의 호출 지점(검사용 — 붙기 목록에 오른 대상).</summary>
    public IZipAnchor DockAnchor(int index) => index >= 0 && index < dockPoints.Length ? dockPoints[index] : null;
    /// <summary>5차: 도크 i의 도크등·유령 윤곽 밝기(검사용).</summary>
    public float DockLampLevel(int index) => index >= 0 && index < dockLampLevels.Length ? Mathf.Max(0f, dockLampLevels[index]) : 0f;
    public float DockGhostLevel(int index) => index >= 0 && index < dockGhostLevels.Length ? Mathf.Max(0f, dockGhostLevels[index]) : 0f;
    private readonly float[] dockLampLevels = { -1f, -1f }, dockGhostLevels = { -1f, -1f }, arrowLevels = { -1f, -1f };

    // ── 설정 ─────────────────────────────

    private WaveDeviceParameter CountParameter => new WaveDeviceParameter(PathCountKey, "경로 점 수", "집(점 0)을 포함한 경로 점 수입니다. 2면 집에서 점 1까지 곧게 갑니다. 양 끝이 도크(승강장)입니다.", "개", MinPoints, MaxPoints, 1f, pathCount, true);
    private WaveDeviceParameter SpeedParameter => new WaveDeviceParameter(SpeedKey, "이동 속도", "Player를 태우고 1초 가속 뒤 유지하는 최고 속도입니다. 이동 중 반대 방향키로 멈췄다 반전할 수 있고, 도중 하차 시 천천히(6칸/초) 원래 도크로 복귀하며 복귀 중에도 다시 탈 수 있습니다.", "칸/초", 10f, 40f, 1f, travelSpeed);

    private WaveDeviceParameter PointParameter(int i, bool y)
    {
        Vector2 fallback = pathDefaults != null && i < pathDefaults.Length ? pathDefaults[i] : Vector2.zero;
        return y
            ? new WaveDeviceParameter(PathYKey(i), "점 " + i + " 세로", "집의 매달린 발에서 점 " + i + "까지 세로 칸(위 +). 트롤리는 이보다 4칸 위를 달립니다.", "칸", -60f, 60f, .5f, fallback.y, true)
            : new WaveDeviceParameter(PathXKey(i), "점 " + i + " 가로", "집의 매달린 발에서 점 " + i + "까지 가로 칸(오른쪽 +).", "칸", -80f, 80f, .5f, fallback.x, true);
    }

    public override WaveDeviceParameter[] PropParameters
    {
        get
        {
            var list = new WaveDeviceParameter[3 + (MaxPoints - 1) * 2];
            int k = 0;
            list[k++] = CountParameter;
            for (int i = 1; i < MaxPoints; i++)
            {
                list[k++] = PointParameter(i, false);
                list[k++] = PointParameter(i, true);
            }
            list[k++] = SpeedParameter;
            list[k] = RadiusParameter;
            return list;
        }
    }

    protected override void ReadSettings(WaveDeviceSetting[] overrides)
    {
        base.ReadSettings(overrides);
        activeCount = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, CountParameter));
        activeOffsets[0] = Vector2.zero;
        for (int i = 1; i < MaxPoints; i++)
            activeOffsets[i] = new Vector2(WaveDeviceSettings.Value(overrides, PointParameter(i, false)), WaveDeviceSettings.Value(overrides, PointParameter(i, true)));
        activeSpeed = WaveDeviceSettings.Value(overrides, SpeedParameter);
    }

    /// <summary>
    /// 편집기·검사용 읽기 API: 이 설정의 경로 점(루트 기준 로컬 매달린 발, 점 0 = 집 (0, −2)). 실행 상태에 기대지 않는다(Edit Mode에서도 된다).
    /// 트롤리 레일은 각 점 + (0, TrackHeight). 첫 점·마지막 점이 도크다.
    /// </summary>
    public bool TryGetPath(WaveDeviceSetting[] overrides, out Vector2[] localFeet)
    {
        int count = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, CountParameter));
        localFeet = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = i == 0 ? Vector2.zero : new Vector2(WaveDeviceSettings.Value(overrides, PointParameter(i, false)), WaveDeviceSettings.Value(overrides, PointParameter(i, true)));
            localFeet[i] = HangFeetLocal + offset;
        }
        return count >= MinPoints;
    }

    /// <summary>격자 Tool 범위 미리보기: 붙기 반경 원(경로 선·도크는 편집기가 TryGetPath로 따로 그린다).</summary>
    public override bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range) => base.TryGetPropRange(overrides, out range);

    // ── 도크 ─────────────────────────────

    /// <summary>도크 i의 매달린 발(월드). 0 = 집(첫 점), 1 = 끝(마지막 점).</summary>
    public Vector2 DockFeet(int index)
    {
        if (worldPath.Length == 0) return HomeFeet;
        return index <= 0 ? worldPath[0] : worldPath[worldPath.Length - 1];
    }

    private float DockS(int index) => index <= 0 ? 0f : pathLength;

    protected override bool CanAcceptZipPhase => phase == Phase.Idle || phase == Phase.Returning;
    public override bool ZipAvailable => base.ZipAvailable && (phase == Phase.Returning || Mathf.Abs(pathS - DockS(restDock)) < .05f);
    public override Vector2 ZipFeet => worldPath.Length > 0 ? FacilityMesh.PointAlong(worldPath, pathS, out _) : HomeFeet;
    protected override float ShakeScale => 1.2f;
    protected override bool AnyFocus => ZipFocused || dockFocus[0] || dockFocus[1];

    // 빈 도크 i를 호출 지점으로 고를 수 있는지: 쓸 수 있는 시점이고, 크레인이 반대 도크에서 쉬는 중이거나(6차 T5) 도중 하차 뒤 복귀 중(두 도크 모두 — 복귀를 끊고 그 도크로).
    // 탑승 중·붙어 오는 중이면 고르지 않는다.
    private bool DockCallable(int index)
    {
        if (!ReadyToServe || pathLength < .05f || IsRiding || ZipIncoming) return false;
        if (phase == Phase.Returning) return true;
        return phase == Phase.Idle && restDock != index;
    }

    private void SetDockFocus(int index, bool on)
    {
        if (dockFocus[index] == on) return;
        dockFocus[index] = on;
        RefreshFocus();
    }

    /// <summary>
    /// 6차 T5: 크레인을 도크 index에 즉시 생성한다(빈 도크 호출 지점의 Call — 쉬는 중·복귀 중). 고를 수 없는 도크면 false(아무것도 하지 않는다).
    /// 붙기는 부른 쪽(FacilityZip)이 같은 프레임에 시작한다 — 직접 부르면(검사 등) 크레인은 그 도크에서 기다린다. player는 쓰지 않는다(계약 유지).
    /// </summary>
    public bool Call(PlayerController player, int index)
    {
        if (index < 0 || index > 1 || !DockCallable(index)) return false;
        Summon(index);
        return true;
    }

    // 6차 T5: 옛 자리 파괴 연출 → 그 도크에서 쉬는 크레인으로 즉시 옮김(복귀 중이면 복귀를 끊는다 — 복귀 수는 세지 않는다) → 새 자리 생성 연출.
    // 위치는 경로 위 거리(pathS)와 표시 부품만 바꾼다(판정 없음 — 빈 크레인은 원래 표시 전용). 흔들림·새 음원 없음.
    private void Summon(int dock)
    {
        Calls++;
        // ① 옛 자리 파괴: 옮기기 전에 자기 불꽃 입자를 터뜨린다(월드 공간 입자라 옛 자리에 남는다). 소리는 크레인 자신의 해제음(기존 — 거리 감쇠).
        Vector2 oldHook = ZipHookPoint;
        LastSummonFrom = carriagePlaced ? lastCarriageFeet : ZipFeet;
        FacilityFeedback.Burst(arrivalSparks, VanishSparks);
        FacilityFeedback.Burst(trolleySparks, VanishSparks);
        FacilityFeedback.Play(releaseFeedback, oldHook);
        // ② 그 도크에서 쉬는 크레인.
        targetDock = restDock = originDock = arrivedDock = dock;
        rideDirSign = dock == 0 ? 1 : -1;
        pathS = DockS(dock);
        moveFrom = moveDistance = moveTime = 0f;
        travelVelocity = Vector2.zero;
        CurrentRideSpeed = 0f;
        hauledToDock = false;
        phase = Phase.Idle;
        phaseTime = 0f;
        // ③ 바퀴·드럼이 순간 이동을 굴러간 거리로 세지 않게 한 뒤 자세를 바로 맞춘다(수레·훅이 새 도크로 — 같은 프레임의 붙기 연출이 거기서 시작한다).
        carriagePlaced = false;
        ApplyPose(0f);
        // ④ 새 자리 생성: 유령 윤곽·도크등 섬광(UpdateDockLights)·불꽃 입자·전원음 하나(곧 붙기 출발음·도착 체결음이 이어진다).
        summonFlash = 0f;
        summonDock = dock;
        FacilityFeedback.Burst(arrivalSparks, AppearSparks);
        FacilityFeedback.Play(powerOnSound, ZipHookPoint);
        SummonEffects++;
    }

    // 빈 크레인을 도크 dock으로 옮기기 시작한다(도중 하차 복귀, 표시만 — 판정 없음).
    private void StartMove(int dock, Phase movePhase)
    {
        targetDock = dock;
        moveFrom = pathS;
        float goal = DockS(dock);
        moveDistance = Mathf.Abs(goal - pathS);
        moveSign = goal >= pathS ? 1f : -1f;
        moveTime = 0f;
        phase = movePhase;
        phaseTime = 0f;
        if (moveDistance < .05f) FinishMove();
    }

    private void FinishMove()
    {
        pathS = DockS(targetDock);
        restDock = targetDock;
        travelVelocity = Vector2.zero;
        phase = Phase.Idle;
        phaseTime = 0f;
        Returns++;
    }

    // ── 수명 ─────────────────────────────

    protected override void OnCache()
    {
        lineLamp = new FacilityLights(pathLineLights);
        bandLamp = new FacilityLights(pathBandLights);
        jointLamp = new FacilityLights(pathJointLights);
        tieLamp = new FacilityLights(pathTieLights);
        pulseLamp = new FacilityLights(pulseLights);
        wakeLamp = new FacilityLights(wakeLights);
        arrowLamp = new FacilityLights(arrowLights);
        dockLamp = new FacilityLights(dockLampLights);
        ghostLamp = new FacilityLights(dockGhostLights);
        dockPoints = new[] { new DockPoint(this, 0), new DockPoint(this, 1) };
    }

    protected override void OnSpawned()
    {
        LastRideArrived = false;
        hauledToDock = false;
        Returns = Calls = SummonEffects = 0;
        Reversals = ReverseClunks = DockArrivalClunks = DeparturesFromWait = MidReturnMounts = 0;
        PeakSpeed = PeakMoveSpeed = LastDepartWait = CurrentRideSpeed = 0f;
        RideInputOverride = null;
        LastSummonFrom = Vector2.zero;
        summonFlash = -1f;
        summonDock = 0;
        pathS = 0f;
        restDock = targetDock = originDock = arrivedDock = 0;
        rideDirSign = 1;
        moveFrom = moveDistance = moveTime = 0f;
        moveSign = 1f;
        dockFocus[0] = dockFocus[1] = false;
        for (int i = 0; i < 2; i++) dockLampLevels[i] = dockGhostLevels[i] = arrowLevels[i] = -1f;
        flow = 0f;
        sparkClock = 0f;
        cageFlash = -1f;
        carriagePlaced = false;
        wakeShown = true;
        HideWake();
        SetDockBodies(true);
        RebuildPath();
    }

    private void SetDockBodies(bool on)
    {
        for (int i = 0; i < dockBodies.Length; i++) if (dockBodies[i] != null) dockBodies[i].enabled = on;
    }

    public override void OnPropSpawned(DungeonProp prop)
    {
        base.OnPropSpawned(prop);
        for (int i = 0; i < dockPoints.Length; i++) FacilityZip.Register(dockPoints[i]);
    }

    // 6차: 대여 중 다시 켜지면 도크 호출 지점도 다시 올린다(OnDisable이 내렸다).
    protected override void OnReenabled()
    {
        for (int i = 0; i < dockPoints.Length; i++) FacilityZip.Register(dockPoints[i]);
    }

    private void UnregisterDocks()
    {
        for (int i = 0; i < dockPoints.Length; i++) FacilityZip.Unregister(dockPoints[i]);
        dockFocus[0] = dockFocus[1] = false;
    }

    protected override void OnReturning()
    {
        RideInputOverride = null;
        CurrentRideSpeed = 0f;
        UnregisterDocks();
        loop.Stop();
        FacilityFeedback.Emit(thruster, false);
        FacilityFeedback.Emit(trolleySparks, false);
        lineLamp?.Restore();
        bandLamp?.Restore();
        jointLamp?.Restore();
        tieLamp?.Restore();
        pulseLamp?.Restore();
        wakeLamp?.Restore();
        arrowLamp?.Restore();
        dockLamp?.Restore();
        ghostLamp?.Restore();
        for (int i = 0; i < 2; i++) dockLampLevels[i] = dockGhostLevels[i] = arrowLevels[i] = -1f;
        SetDockBodies(false);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        UnregisterDocks();
        loop.Stop();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        UnregisterDocks();
        FacilityMesh.Release(ref linesBuilt);
        FacilityMesh.Release(ref bandBuilt);
        FacilityMesh.Release(ref jointsBuilt);
        FacilityMesh.Release(ref tiesBuilt);
        FacilityMesh.Release(ref wakeBuilt);
    }

    protected override void OnSettingsApplied() => RebuildPath();

    // 지금 자리·설정으로 월드 경로와 레일 Mesh·도크 자리를 다시 만든다(배치 설정이 바뀌거나 개체가 옮겨졌을 때만).
    private void RebuildPath()
    {
        builtAt = transform.position;
        int count = Mathf.Clamp(activeCount, MinPoints, MaxPoints);
        Vector2 home = HomeFeet;
        worldPath = new Vector2[count];
        for (int i = 0; i < count; i++) worldPath[i] = home + activeOffsets[i];
        pathLength = FacilityMesh.PathLength(worldPath);
        localTrack = new Vector2[count];
        for (int i = 0; i < count; i++) localTrack[i] = (Vector2)transform.InverseTransformPoint(worldPath[i] + Vector2.up * TrackHeight);
        pathS = Mathf.Clamp(pathS, 0f, pathLength);
        if (restDock == 1 && phase == Phase.Idle) pathS = pathLength;
        if (pathLines != null)
            pathLines.sharedMesh = linesBuilt = FacilityMesh.Polyline(linesBuilt, ToFilter(pathLines.transform), lineWidth, lineSpacing, 2, Color.white, name + "_PathLines");
        if (pathBand != null)
            pathBand.sharedMesh = bandBuilt = FacilityMesh.Polyline(bandBuilt, ToFilter(pathBand.transform), bandWidth, 0f, 1, Color.white, name + "_PathBand");
        if (pathJoints != null)
        {
            // 관절 고리는 꺾이는 점에만(양 끝은 도크가 차지한다).
            var filter = ToFilter(pathJoints.transform);
            var inner = new Vector2[Mathf.Max(0, filter.Length - 2)];
            for (int i = 0; i < inner.Length; i++) inner[i] = filter[i + 1];
            pathJoints.sharedMesh = jointsBuilt = FacilityMesh.Rings(jointsBuilt, inner, jointRadius, .045f, 18, Color.white, name + "_PathJoints");
        }
        if (pathTies != null)
            pathTies.sharedMesh = tiesBuilt = FacilityMesh.Ties(tiesBuilt, ToFilter(pathTies.transform), tieSpacing, tieHalfLength, tieWidth, Color.white, name + "_PathTies");
        PlaceDocks();
    }

    // 도크 2곳을 경로 양 끝(레일 높이 기준 — 도크 원점 = 매달린 발)으로 옮기고, 정지대는 경로 바깥쪽을 향하게 돌린다(이동·회전만).
    private void PlaceDocks()
    {
        for (int i = 0; i < docks.Length && i < 2; i++)
        {
            if (docks[i] == null) continue;
            Vector2 feet = DockFeet(i);
            var parent = docks[i].parent;
            Vector3 local = parent != null ? parent.InverseTransformPoint(new Vector3(feet.x, feet.y, parent.position.z)) : (Vector3)feet;
            docks[i].localPosition = new Vector3(local.x, local.y, docks[i].localPosition.z);
            if (i < dockStops.Length && dockStops[i] != null && worldPath.Length >= 2)
            {
                Vector2 outward = i == 0 ? worldPath[0] - worldPath[1] : worldPath[worldPath.Length - 1] - worldPath[worldPath.Length - 2];
                if (outward.sqrMagnitude < 1e-6f) outward = i == 0 ? Vector2.left : Vector2.right;
                dockStops[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(outward.y, outward.x) * Mathf.Rad2Deg);
            }
        }
    }

    // 루트 기준 레일 점을 그 MeshFilter의 로컬로.
    private Vector2[] ToFilter(Transform space)
    {
        var result = new Vector2[localTrack.Length];
        for (int i = 0; i < localTrack.Length; i++) result[i] = space.InverseTransformPoint(transform.TransformPoint(localTrack[i]));
        return result;
    }

    // ── 탑승 ─────────────────────────────

    protected override void OnRideStarted()
    {
        bool fromReturn = phase == Phase.Returning;
        LastRideArrived = false;
        hauledToDock = false;
        PeakSpeed = 0f;
        CurrentRideSpeed = 0f;
        LastDepartWait = 0f;
        if (fromReturn)
        {
            MidReturnMounts++;
            // 복귀 도중 재탑승: 원래 출발 도크(originDock) 기준 진행 방향(반대 도크 쪽)으로 이어서 출발한다.
            rideDirSign = originDock == 0 ? 1 : -1;
        }
        else
        {
            originDock = restDock;
            rideDirSign = restDock == 0 ? 1 : -1;
        }
        arrivedDock = rideDirSign > 0 ? 1 : 0;
        if (pathLength < .05f)
        {
            phase = Phase.Waiting;
            phaseTime = 0f;
            LastRideArrived = true;
            return;
        }
        phase = Phase.Riding;
        phaseTime = 0f;
        routine = StartCoroutine(RideRoutine(false));
    }

    private void DepartFromWait(int departDirSign)
    {
        if (routine != null) { StopCoroutine(routine); routine = null; }
        originDock = restDock;
        rideDirSign = departDirSign;
        arrivedDock = rideDirSign > 0 ? 1 : 0;
        hauledToDock = false;
        LastRideArrived = false;
        PeakSpeed = 0f;
        CurrentRideSpeed = 0f;
        LastDepartWait = 0f;
        DeparturesFromWait++;
        phase = Phase.Riding;
        phaseTime = 0f;
        FacilityMesh.PointAlong(worldPath, Mathf.Clamp(pathS, .01f, Mathf.Max(.01f, pathLength - .01f)), out Vector2 segDir);
        Vector2 travelDir = segDir * rideDirSign;
        float kickDir = Mathf.Abs(travelDir.x) > .1f ? -Mathf.Sign(travelDir.x) : -rideDirSign;
        TriggerKick(kickDir, .9f);
        FacilityFeedback.Burst(trolleySparks, 8);
        routine = StartCoroutine(RideRoutine(true));
    }

    private IEnumerator RideRoutine(bool skipDepartDelay)
    {
        bool done = false;
        try
        {
            // 출발 지연: 처음 체결 시 흔들림 첫 반 주기가 읽히도록 물리 틱으로 0.12초 기다린다(도착 도크에서 방향키 재출발 시 즉시 가속).
            var tick = new WaitForFixedUpdate();
            float waited = 0f;
            if (!skipDepartDelay)
            {
                while (waited < DepartDelay - 1e-4f)
                {
                    yield return tick;
                    if (!ride.IsRiding || ride.Interrupted) yield break;
                    waited += Time.fixedDeltaTime;
                }
            }
            LastDepartWait = waited;

            float currentSpeed = 0f;
            float movedLeg = 0f;
            float reverseCooldown = 0f;
            // 새로 탑승한 경우 출발 직후 MountReverseGrace(0.35초) 동안 반전을 보류하고,
            // 탑승 시점에 이미 누르고 있던 방향키(연쇄 크레인 이동 중 유지 입력)는 손을 떼거나 방향을 바꾸기 전까지 반전으로 보지 않는다.
            float mountGrace = skipDepartDelay ? 0f : MountReverseGrace;
            Vector2 prevTravelDir = CurrentTravelDirection(pathS, rideDirSign);
            Vector2Int latchedInput = !skipDepartDelay ? ReadQuantizedRideInput(ride.Rider) : Vector2Int.zero;
            bool hasInputLatch = latchedInput != Vector2Int.zero;
            Vector2Int prevInput = latchedInput;
            Vector2 lastFeet = FacilityMesh.PointAlong(worldPath, pathS, out _);

            while (ride.IsRiding && !ride.Interrupted)
            {
                yield return tick;
                if (!ride.IsRiding || ride.Interrupted) yield break;
                var rider = ride.Rider;
                if (rider == null || rider.Motor == null) yield break;
                float dt = Time.fixedDeltaTime;
                if (dt <= 0f) continue;

                // 플레이어의 실시간 실제 운동 방향(왼쪽↔오른쪽, 아래↔위)과 입력 방향을 직접 비교해 반대 방향 입력만 반전한다.
                // 직선·수직·대각선·'ㄷ'자 모든 구간에서 지금 실제로 나아가는 방향의 반대 키(내적 ≤ -0.25)로 직관적으로 전환된다.
                Vector2 travelDir = CurrentTravelDirection(pathS, rideDirSign);
                Vector2Int input = ReadQuantizedRideInput(rider);
                if (hasInputLatch && input != latchedInput)
                    hasInputLatch = false;
                if (Vector2.Dot(prevTravelDir, travelDir) < .85f)
                {
                    // 커브·대각선 꺾임 지점에서 이전 구간부터 계속 누르고 있던 키가 갑자기 반대가 된 경우, 떼기 전까지 반전하지 않는다.
                    if (input != Vector2Int.zero && input == prevInput && CompareInputToDirection(input, travelDir) < 0)
                    {
                        latchedInput = input;
                        hasInputLatch = true;
                    }
                }
                prevTravelDir = travelDir;
                prevInput = input;

                if (mountGrace > 0f) mountGrace -= dt;
                if (reverseCooldown > 0f) reverseCooldown -= dt;
                else if (mountGrace <= 0f && !hasInputLatch && (currentSpeed > .2f || movedLeg > .02f))
                {
                    if (CompareInputToDirection(input, travelDir) < 0)
                    {
                        currentSpeed = 0f;
                        CurrentRideSpeed = 0f;
                        travelVelocity = Vector2.zero;
                        Reversals++;
                        float kickDir = Mathf.Abs(travelDir.x) > .1f ? -Mathf.Sign(travelDir.x) : -rideDirSign;
                        if (PlayMechanismClunk(kickDir, 1.35f, .28f, 8, reverseClunkSound)) ReverseClunks++;
                        FacilityFeedback.Burst(trolleySparks, 10);

                        float stopWaited = 0f;
                        while (stopWaited < ReverseStopDelay - 1e-4f)
                        {
                            yield return tick;
                            if (!ride.IsRiding || ride.Interrupted) yield break;
                            stopWaited += Time.fixedDeltaTime;
                        }
                        rideDirSign = -rideDirSign;
                        arrivedDock = rideDirSign > 0 ? 1 : 0;
                        movedLeg = 0f;
                        reverseCooldown = ReverseCooldown;
                        prevTravelDir = CurrentTravelDirection(pathS, rideDirSign);
                        prevInput = ReadQuantizedRideInput(ride.Rider);
                        continue;
                    }
                }

                float destS = rideDirSign > 0 ? pathLength : 0f;
                float remaining = rideDirSign > 0 ? pathLength - pathS : pathS;
                if (remaining <= 1e-4f)
                {
                    pathS = destS;
                    arrivedDock = rideDirSign > 0 ? 1 : 0;
                    hauledToDock = true;
                    break;
                }

                // 출발 후 1초에 걸쳐 최고 속도(activeSpeed)까지 가속하고, 도착 도크 직전에는 부드럽게 감속한다.
                float accel = activeSpeed / AccelTime;
                float decel = activeSpeed / DecelEase;
                float nextSpeed = Mathf.Min(activeSpeed, currentSpeed + accel * dt);
                float maxBrake = Mathf.Sqrt(2f * decel * remaining);
                if (nextSpeed > maxBrake) nextSpeed = maxBrake;

                float stepDist = .5f * (currentSpeed + nextSpeed) * dt;
                if (stepDist < 1e-4f) stepDist = Mathf.Min(remaining, nextSpeed * dt);
                if (remaining - stepDist <= .005f) stepDist = remaining;
                else stepDist = Mathf.Min(remaining, stepDist);

                float nextS = Mathf.Clamp(pathS + rideDirSign * stepDist, 0f, pathLength);
                Vector2 nextFeet = FacilityMesh.PointAlong(worldPath, nextS, out _);
                rider.Motor.ShiftConfinement(nextFeet - lastFeet);
                travelVelocity = (nextFeet - lastFeet) / dt;
                pathS = nextS;
                lastFeet = nextFeet;
                currentSpeed = nextSpeed;
                CurrentRideSpeed = currentSpeed;
                movedLeg += stepDist;
                PeakSpeed = Mathf.Max(PeakSpeed, travelVelocity.magnitude);

                if (stepDist >= remaining - 1e-4f)
                {
                    pathS = destS;
                    arrivedDock = rideDirSign > 0 ? 1 : 0;
                    hauledToDock = true;
                    break;
                }
            }

            if (!ride.IsRiding || ride.Interrupted || !hauledToDock) yield break;
            // 마지막으로 넘긴 이동량이 반영된 뒤 도착 도크 대기로 바꾼다. 그 한 틱 안에 내려도 도착으로 본다(OnRideEnded — 경로는 이미 다 갔다).
            yield return tick;
            done = ride.IsRiding && !ride.Interrupted;
        }
        finally
        {
            routine = null;
            travelVelocity = Vector2.zero;
            CurrentRideSpeed = 0f;
            if (done)
            {
                pathS = DockS(arrivedDock);
                restDock = arrivedDock;
                phase = Phase.Waiting;
                phaseTime = 0f;
                LastRideArrived = true;
                // 끝 도크 도달 시 방향전환 때와 동일한 덜컹 연출(소리·회전 흔들림·스파크·카메라 임펄스)
                float probe = Mathf.Clamp(pathS - rideDirSign * .05f, .01f, Mathf.Max(.01f, pathLength - .01f));
                FacilityMesh.PointAlong(worldPath, probe, out Vector2 endSegDir);
                Vector2 endTravel = endSegDir * rideDirSign;
                float kickDir = Mathf.Abs(endTravel.x) > .1f ? -Mathf.Sign(endTravel.x) : -rideDirSign;
                if (PlayMechanismClunk(kickDir, 1.35f, .28f, 8, reverseClunkSound)) DockArrivalClunks++;
                FacilityFeedback.Burst(trolleySparks, 10);
            }
        }
    }

    // 현재 경로 거리 s·진행 부호 dirSign에서 플레이어가 실제로 움직이는 실시간 운동 방향 단위 벡터를 구한다.
    private Vector2 CurrentTravelDirection(float s, int dirSign)
    {
        if (worldPath == null || worldPath.Length < 2 || pathLength < 1e-4f)
            return dirSign >= 0 ? Vector2.right : Vector2.left;
        float probe = Mathf.Clamp(s + dirSign * .02f, .01f, Mathf.Max(.01f, pathLength - .01f));
        FacilityMesh.PointAlong(worldPath, probe, out Vector2 segDir);
        Vector2 dir = segDir * dirSign;
        return dir.sqrMagnitude > 1e-6f ? dir.normalized : (dirSign >= 0 ? Vector2.right : Vector2.left);
    }

    // 방향 입력을 8방향 정수 벡터(-1/0/+1, -1/0/+1)로 양자화한다.
    private Vector2Int ReadQuantizedRideInput(PlayerController rider)
    {
        Vector2 input = ReadRideDirectionInput(rider);
        int ix = input.x >= ReverseInputThreshold ? 1 : (input.x <= -ReverseInputThreshold ? -1 : 0);
        int iy = input.y >= ReverseInputThreshold ? 1 : (input.y <= -ReverseInputThreshold ? -1 : 0);
        return new Vector2Int(ix, iy);
    }

    // 입력 방향이 실제 운동 방향(좌/우·하/상)과 같은 쪽(+1), 반대쪽(-1), 무관/직교(0)인지 판정한다.
    private static int CompareInputToDirection(Vector2Int quantizedInput, Vector2 travelDir)
    {
        if (quantizedInput == Vector2Int.zero || travelDir.sqrMagnitude < 1e-6f) return 0;
        Vector2 inDir = ((Vector2)quantizedInput).normalized;
        float dot = Vector2.Dot(inDir, travelDir.normalized);
        if (dot >= DirectionAlignThreshold) return 1;
        if (dot <= -DirectionAlignThreshold) return -1;
        return 0;
    }

    private Vector2 ReadRideDirectionInput(PlayerController rider)
    {
        if (RideInputOverride.HasValue) return RideInputOverride.Value;
        if (rider == null || !rider.CanReceiveInput) return Vector2.zero;
        Vector2 move = rider.MoveInput;
        if (InputManager.Instance != null)
        {
            Vector2 raw = InputManager.Instance.Move;
            if (Mathf.Abs(raw.x) > Mathf.Abs(move.x)) move.x = raw.x;
            if (Mathf.Abs(raw.y) > Mathf.Abs(move.y)) move.y = raw.y;
            if (move.y > -.3f && InputManager.Instance.MoveDownHeld) move.y = -1f;
        }
        return move;
    }

    private void PlayReverseClunk()
    {
        SoundOptions sound = reverseClunkSound.clip != null
            ? reverseClunkSound
            : (grabHeavySound.clip != null ? grabHeavySound : grabLockSound);
        if (sound.clip != null)
        {
            FacilityFeedback.Play(sound, ZipHookPoint);
            ReverseClunks++;
        }
    }

    protected override void StopRideMotion()
    {
        if (routine != null) { StopCoroutine(routine); routine = null; }
        travelVelocity = Vector2.zero;
        CurrentRideSpeed = 0f;
    }

    protected override void OnRideEnded(bool interrupted)
    {
        // 도착 도크에서 내렸으면 그 도크가 쉬는 자리가 된다(돌아가지 않는다). 도중에 내렸으면 빈 채로 원래 출발 도크로 천천히(6칸/초) 복귀한다.
        // 5차 검토 p2: 운반 경로를 다 간 뒤 마지막 이동량 반영 틱(아직 Riding) 안에 내려도 도착으로 본다(pathS는 이미 도착 도크).
        bool hauled = phase == Phase.Riding && hauledToDock;
        hauledToDock = false;
        if (phase == Phase.Waiting || hauled)
        {
            if (hauled) LastRideArrived = true;
            restDock = LastRideArrived && pathLength >= .05f ? arrivedDock : originDock;
            pathS = DockS(restDock);
            phase = Phase.Idle;
            phaseTime = 0f;
            return;
        }
        restDock = originDock;
        StartMove(originDock, Phase.Returning);
    }

    protected override void OnGrabbed() => cageFlash = 0f;

    protected override float CageLevel()
    {
        if (cageFlash < 0f) return base.CageLevel();
        return Mathf.Max(base.CageLevel(), 2.4f);
    }

    // ── 선택 표시(도크 호출 지점) ─────────────────────────────

    // 6차 T4·T5: 선택된 빈 도크는 크레인이 생겨날 머리 자리(도크 매달린 발 + 1.97)를 같은 크기 괄호로 가리킨다. 밝기는 일반 선택과 같게 0.9(예전 0.5 — 호출이 곧 탑승).
    protected override bool TryAimTarget(out Vector2 center, out Vector2 half, out float level)
    {
        for (int i = 0; i < 2; i++)
        {
            if (!dockFocus[i] || IsRiding) continue;
            center = DockFeet(i) + Vector2.up * AimOffset;
            half = FrameHalf;
            var runner = FacilityZip.Runner;
            level = runner != null && i < dockPoints.Length && ReferenceEquals(runner.Locked, dockPoints[i]) ? lockedLevel : focusLevel;
            return true;
        }
        return base.TryAimTarget(out center, out half, out level);
    }

    /// <summary>6차 T4: 표시 기준점 G = 머리(스프레더) 중심(매달린 발 + 1.97). 머리가 없으면 수레 매달린 발 + 1.97.</summary>
    protected override Vector2 AimPoint => head != null ? (Vector2)head.TransformPoint(new Vector3(0f, HeadAimY, 0f)) : (carriagePlaced ? lastCarriageFeet : ZipFeet) + Vector2.up * AimOffset;

    protected override Vector2 AimFrameHalf => FrameHalf;

    // 직사각형 괄호는 돌리지 않는다(직선 끝이 상자 가장자리에 맞게).
    protected override bool BracketSpins => false;

    // 6차 T4: 키 안내는 머리 바로 아래(매달린 발 + 1.25). 선택된 빈 도크도 같은 자리.
    protected override Vector2 PromptPoint
    {
        get
        {
            for (int i = 0; i < 2; i++) if (dockFocus[i]) return DockFeet(i) + Vector2.up * PromptOffset;
            return (carriagePlaced ? lastCarriageFeet : ZipFeet) + Vector2.up * PromptOffset;
        }
    }

    // ── 매 프레임 ─────────────────────────────

    protected override void TickMotion(float dt)
    {
        if (transform.position != builtAt) RebuildPath();
        if (phase == Phase.Waiting && IsRiding && ride.Rider != null && !ride.Interrupted && pathLength >= .05f && routine == null && phaseTime >= WaitDepartCooldown)
        {
            Vector2Int input = ReadQuantizedRideInput(ride.Rider);
            int departDirSign = restDock == 0 ? 1 : -1;
            Vector2 departDir = CurrentTravelDirection(DockS(restDock), departDirSign);
            if (CompareInputToDirection(input, departDir) > 0)
                DepartFromWait(departDirSign);
        }
        if (phase == Phase.Returning)
        {
            // 복귀 중 Player가 이 크레인으로 붙어 오는 동안은 잠시 멈춰 안정적으로 체결한다.
            if (ZipIncoming)
            {
                travelVelocity = Vector2.zero;
            }
            else
            {
                moveTime += dt;
                float before = pathS;
                float duration = FacilityRide.PathDuration(moveDistance, ReturnSpeed, PathEase);
                pathS = Mathf.Clamp(moveFrom + moveSign * FacilityRide.PathDistance(moveTime, moveDistance, ReturnSpeed, PathEase), 0f, pathLength);
                Vector2 a = FacilityMesh.PointAlong(worldPath, before, out _), b = FacilityMesh.PointAlong(worldPath, pathS, out _);
                travelVelocity = dt > 0f ? (b - a) / dt : Vector2.zero;
                PeakMoveSpeed = Mathf.Max(PeakMoveSpeed, travelVelocity.magnitude);
                if (moveTime >= duration) FinishMove();
            }
        }
        if (cageFlash >= 0f) { cageFlash += dt; if (cageFlash > .2f) cageFlash = -1f; }
        if (summonFlash >= 0f) { summonFlash += dt; if (summonFlash > SummonFlashTime) summonFlash = -1f; }

        float speed = travelVelocity.magnitude;
        bool moving = (phase == Phase.Riding || phase == Phase.Returning) && speed > .2f;
        // 레일: 대기 중 흐리고 느리게, 탑승 중 밝고 빠르게, 빈 이동 중 중간, 쓸 수 없으면 거의 꺼짐. 빛 덩어리는 다음 도크 쪽으로 흐른다.
        bool available = ZipAvailable;
        float line, band, joint, pulse, flowSpeed;
        float toward = NextDockSign();
        if (IsRiding) { line = 1.1f; band = .6f; joint = 1f; pulse = 1.4f; flowSpeed = 14f; }
        else if (phase == Phase.Returning) { line = .6f; band = .35f; joint = .6f; pulse = .8f; flowSpeed = 8f; }
        else if (available) { line = .45f; band = .28f; joint = .5f; pulse = .6f; flowSpeed = 3f; }
        else { line = .15f; band = .1f; joint = .15f; pulse = 0f; flowSpeed = 0f; }
        float p = Mathf.Max(.25f, power);
        lineLamp?.Set(line * p);
        bandLamp?.Set(band * p);
        jointLamp?.Set(joint * p);
        tieLamp?.Set(Mathf.Max(.25f, line * .7f) * p);
        pulseLamp?.Set(pulse * p);
        float trackLength = FacilityMesh.PathLength(localTrack);
        if (trackLength > .01f && pulses.Length > 0 && pulse > 0f)
        {
            flow = Mathf.Repeat(flow + toward * flowSpeed * dt, trackLength);
            for (int i = 0; i < pulses.Length; i++)
            {
                if (pulses[i] == null) continue;
                Vector2 at = FacilityMesh.PointAlong(localTrack, Mathf.Repeat(flow + trackLength * i / pulses.Length, trackLength), out _);
                pulses[i].localPosition = new Vector3(at.x, at.y, pulses[i].localPosition.z);
            }
        }
        UpdateDockLights();
        UpdateArrows(moving);

        // 탑승부 효과: 추진 불꽃(탑승 중 이동), 트롤리 접점 불꽃(속도 비례, 기존 2배) + 기존 불꽃 효과 0.125초 간격(기존 2배), 구동음.
        FacilityFeedback.Emit(thruster, phase == Phase.Riding && speed > 1f);
        FacilityFeedback.Emit(trolleySparks, moving && speed > 6f);
        if (trolleySparks != null)
        {
            var emission = trolleySparks.emission;
            emission.rateOverTime = Mathf.Lerp(16f, 80f, Mathf.Clamp01(speed / 40f));
        }
        sparkClock += dt;
        if (moving && speed > 10f && sparkClock >= sparkInterval)
        {
            sparkClock = 0f;
            FacilityFeedback.Play(sparkFeedback, trolley != null ? trolley.position : (Vector3)lastCarriageFeet);
        }
        loop.Update(travelLoop, trolley != null ? trolley.position : transform.position, moving, Mathf.Clamp01(speed / 20f));
    }

    // 다음 도크 쪽 경로 진행 부호(+1 = 집 → 끝, −1 = 끝 → 집).
    private float NextDockSign()
    {
        if (phase == Phase.Riding) return rideDirSign;
        if (phase == Phase.Waiting) return restDock == 0 ? 1f : -1f;
        if (phase == Phase.Returning) return moveSign;
        return restDock == 0 ? 1f : -1f;
    }

    // 도크등: 크레인이 있으면 켜짐, 빈 도크에서 부를 수 있으면 천천히 점멸(선택되면 조금 빠르게), 그 밖에는 흐리게.
    // 유령 윤곽: 크레인이 없는 도크에만 흐리게. 6차 T5: 즉시 생성 직후 0.18초는 그 도크의 유령 윤곽이 0.6 → 2.4 → 0으로 번쩍이고 도크등도 함께 섬광.
    private void UpdateDockLights()
    {
        for (int i = 0; i < 2; i++)
        {
            bool here = Mathf.Abs(pathS - DockS(i)) < .05f;
            float lamp;
            if (here) lamp = 1f;
            else if (DockCallable(i))
            {
                float period = dockFocus[i] ? .5f : 1.2f;
                lamp = Mathf.Repeat(clock, period) / period < .5f ? .9f : .2f;
            }
            else lamp = .2f;
            lamp *= Mathf.Max(.25f, power);
            float ghost = here ? 0f : (dockFocus[i] ? .6f : .3f) * Mathf.Max(.25f, power);
            if (summonFlash >= 0f && i == summonDock)
            {
                float f = Mathf.Clamp01(summonFlash / SummonFlashTime);
                ghost = f < 1f / 3f ? Mathf.Lerp(.6f, 2.4f, f * 3f) : Mathf.Lerp(2.4f, 0f, (f - 1f / 3f) * 1.5f);
                lamp = Mathf.Max(lamp, 1.6f * (1f - f));
            }
            // 값이 바뀔 때만 밝기를 넣는다(MaterialPropertyBlock을 매 프레임 쓰지 않게).
            if (i < dockLampLights.Length && Mathf.Abs(dockLampLevels[i] - lamp) > .002f) dockLamp?.SetOne(i, lamp);
            if (i < dockGhostLights.Length && Mathf.Abs(dockGhostLevels[i] - ghost) > .002f) ghostLamp?.SetOne(i, ghost);
            dockLampLevels[i] = lamp;
            dockGhostLevels[i] = ghost;
        }
    }

    // 방향 화살표: 다음 도크 쪽 진행 방향(캐리지 공간)으로 돌리고, 이동 중에는 차례로 점멸, 대기 중에는 켜짐, 쓸 수 없으면 흐리게.
    private void UpdateArrows(bool moving)
    {
        if (directionArrows.Length == 0 || worldPath.Length < 2) return;
        FacilityMesh.PointAlong(worldPath, Mathf.Clamp(pathS, .01f, Mathf.Max(.01f, pathLength - .01f)), out var dir);
        dir *= NextDockSign();
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        for (int i = 0; i < directionArrows.Length; i++)
        {
            if (directionArrows[i] == null) continue;
            directionArrows[i].rotation = Quaternion.Euler(0f, 0f, angle);
            float level = (moving ? (Mathf.Repeat(clock * 4f - i * .5f, 1f) < .5f ? 1.3f : .25f) : ZipAvailable ? .8f : .2f) * Mathf.Max(.25f, power);
            if (i < arrowLights.Length && i < arrowLevels.Length && Mathf.Abs(arrowLevels[i] - level) > .002f) { arrowLamp?.SetOne(i, level); arrowLevels[i] = level; }
        }
    }

    protected override void ApplyPose(float dt)
    {
        // 수레 자리: 탑승 중 Player의 화면 보간 발, 그 밖에는 경로 위 거리.
        Vector2 feet;
        if (!(IsRiding && TryRiderShownFeet(out feet)))
            feet = worldPath.Length > 0 ? FacilityMesh.PointAlong(worldPath, pathS, out _) : HomeFeet;
        Vector2 last = lastCarriageFeet;
        lastCarriageFeet = feet;
        if (carriage != null)
        {
            Vector3 local = carriage.parent != null ? carriage.parent.InverseTransformPoint(new Vector3(feet.x, feet.y, carriage.parent.position.z)) : (Vector3)feet;
            carriage.localPosition = new Vector3(local.x, local.y, carriage.localPosition.z);
        }
        // 캐리지는 지금 구간 방향으로 기운다(위 바퀴가 늘 레일 위 — 세로 구간에서는 ±90°로 접는다). 회전만.
        Vector2 segment = Vector2.right;
        if (worldPath.Length >= 2) FacilityMesh.PointAlong(worldPath, Mathf.Clamp(pathS, .01f, Mathf.Max(.01f, pathLength - .01f)), out segment);
        float tilt = Mathf.Atan2(segment.y, segment.x) * Mathf.Rad2Deg;
        if (tilt > 90f) tilt -= 180f;
        else if (tilt <= -90f) tilt += 180f;
        if (trolley != null) trolley.localRotation = Quaternion.Euler(0f, 0f, tilt);
        Vector2 moved = carriagePlaced ? feet - last : Vector2.zero;
        carriagePlaced = true;
        float rolled = Vector2.Dot(moved, new Vector2(Mathf.Cos(tilt * Mathf.Deg2Rad), Mathf.Sin(tilt * Mathf.Deg2Rad)));
        for (int i = 0; i < trolleyWheels.Length; i++)
            if (trolleyWheels[i] != null) trolleyWheels[i].localRotation *= Quaternion.Euler(0f, 0f, -rolled / .12f * Mathf.Rad2Deg);
        if (winchDrum != null && moved.sqrMagnitude > 0f) winchDrum.localRotation *= Quaternion.Euler(0f, 0f, -moved.magnitude * NextDockSign() / .15f * Mathf.Rad2Deg * .5f);
        base.ApplyPose(dt);
        DrawWake();
    }

    // 속도 반대쪽 잔선(탑승 중 이동할 때만). 케이지 가운데에서 뒤로 짧게.
    private void DrawWake()
    {
        if (wake == null) return;
        float speed = travelVelocity.magnitude;
        if (phase != Phase.Riding || speed < 2f) { HideWake(); return; }
        Vector2 center = lastCarriageFeet + Vector2.up * BodyHalfHeight;
        Vector2 back = center - travelVelocity / speed * Mathf.Min(1.4f, speed * .045f);
        var space = wake.transform;
        if (wakeMeshName == null) wakeMeshName = name + "_Wake";
        wake.sharedMesh = wakeBuilt = FacilityMesh.Strands(wakeBuilt, space.InverseTransformPoint(center), space.InverseTransformPoint(back), .025f, .35f, 3, Color.white, wakeMeshName);
        wakeShown = true;
        wakeLamp?.Set(Mathf.Clamp01(speed / 30f) * 1.2f);
    }

    private void HideWake()
    {
        if (!wakeShown) return;
        wakeShown = false;
        wakeLamp?.Set(0f);
        if (wakeBuilt != null) wakeBuilt.Clear();
    }
}
