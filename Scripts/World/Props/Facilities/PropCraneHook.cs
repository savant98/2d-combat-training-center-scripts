using System;
using System.Collections;
using UnityEngine;

// 경로 크레인(DESIGN_V4 §2.3 — 4차 재작성 · 5차 DESIGN_V5 §1 왕복 도크, Prefab DungeonPropCraneHook 같은 GUID). 붙기(↑)로 매달리면 배치 설정의 다각선 경로를 따라
// 반대편 도크(승강장)까지 옮겨 주고, 도착 도크에서 매달린 채 기다린다(점프·대시·다른 크레인 붙기로 내림).
// - 5차 왕복: 경로 양 끝(점 0 = 집, 마지막 점)이 도크 2곳이다. 크레인은 마지막으로 도착한 도크(처음은 집)에서 쉬고, 거기서 붙으면 반대 도크로 간다(경로를 뒤집어 운반).
//   도착 도크에서 내리면 그 도크가 쉬는 자리가 된다(돌아가지 않는다 — 경로를 다 간 뒤 대기로 바뀌기 전 한 물리 틱 안에 내려도 같다). 도중에 내리면(점프·대시·연쇄 붙기·자동 하차) 빈 채로 출발 도크로 40칸/초 복귀한다(복귀 중 붙기 대상 아님).
//   반대편 빈 도크는 '호출 지점'(IZipCallTarget)으로 붙기 목록에 오른다: 그 도크를 고르고 ↑ → 크레인이 40칸/초로 오고, 도착 때 그 도크가 계속 선택돼 있으면
//   FacilityZip.TryAutoZip으로 자동 붙기(B안). 아니면 그 도크에서 기다린다. 붙은 뒤 0.12초(DepartDelay) 기다렸다 출발한다(체결 흔들림 첫 반 주기가 읽히게).
// - 배치: footprint 2×4, 집(점 0)의 매달린 발 = (x + 1, y). 경로 점 i(1..7)는 집 기준 칸 오프셋(cranePath{i}X·Y, 눈금 0.5), 점 수 cranePathCount(2~8).
//   캐리지는 '매달린 발 + 4칸' 높이의 레일(빛 통로) 위를 위아래 바퀴로 물고 달린다. 경로 공간 규칙(발 ~ 발 + 4칸, 폭 2칸 쓸기 상자가 고체와 겹치지 않음)은 템플릿 검사가 맡는다.
// - 이동(AGENTS 물리 규칙): FacilityRide.HaulPath — 일정 속도 craneSpeed(5차 기본 24칸/초 = 대시 30의 0.8배), 시작·끝 0.1초만 가감속, 물리 틱마다 ShiftConfinement(위치 직접 대입 없음).
//   빈 크레인의 복귀·호출 이동은 표시만 바꾼다(판정 없음 — 지금 복귀와 같다).
// - 외형(5차 S3 '넓은 Π'): 레일을 문 캐리지(위 바퀴 2·아래 안내 롤러 2)·감기 드럼(이동 중 회전)·진행 방향 화살표 2개(다음 도크 쪽으로 회전, 이동 중 차례로 점멸)·
//   벌어진 케이블 2가닥·긴 스프레더 머리(±0.98)·운반 케이지(캐리지에 붙음 — 머리 흔들림과 따로)·양 끝 도크(정지대 블록 + 게이트 기둥 + 파랑 도크등 + 빈 도크의 유령 윤곽).
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
    /// <summary>경로 시작·끝 가감속 시간(초).</summary>
    public const float PathEase = .1f;
    /// <summary>5차 기본 이동 속도(칸/초, 대시 30의 0.8배 — DESIGN_V5 판단값).</summary>
    public const float DefaultSpeed = 24f;
    /// <summary>5차 빈 크레인 복귀·호출 속도(칸/초, 상수 — 배치 설정 키를 늘리지 않는다).</summary>
    public const float ReturnSpeed = 40f;
    /// <summary>5차 체결 뒤 출발 지연(초).</summary>
    public const float DepartDelay = .12f;
    /// <summary>5차 호출 뒤 이 시간 안에 도착해야 자동 붙기를 시도한다(초 — 그보다 늦으면 도크에서 기다린다).</summary>
    public const float CallAutoWindow = 3f;

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
    [SerializeField] private FeedbackDataSO sparkFeedback;
    [SerializeField, Min(.05f)] private float sparkInterval = .25f;

    // 빈 도크 호출 지점(붙기 목록에 따로 오른다 — 고르면 붙지 않고 크레인을 부른다).
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
    private Vector2[] reversedPath = Array.Empty<Vector2>();
    private Vector2[] localTrack = Array.Empty<Vector2>();
    private float pathLength;
    private float pathS;                    // 수레가 있는 경로 위 거리(0 = 집 도크, pathLength = 끝 도크)
    private int restDock, departDock, targetDock;
    private float moveFrom, moveDistance, moveSign, moveTime;
    private PlayerController caller;
    private float callTime;
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
    /// <summary>5차: 받은 호출 수·호출로 도크에 도착한 수·도착 때 자동 붙기를 시작한 수(검사용).</summary>
    public int Calls { get; private set; }
    public int CallArrivals { get; private set; }
    public int AutoZips { get; private set; }
    /// <summary>탑승 중 찍은 최고 속도(칸/초, 검사용).</summary>
    public float PeakSpeed { get; private set; }
    /// <summary>5차: 빈 크레인 복귀·호출 이동 중 찍은 최고 속도(칸/초, 검사용).</summary>
    public float PeakMoveSpeed { get; private set; }
    /// <summary>5차: 붙은 뒤 실제로 출발하기까지 걸린 시간(초, 검사용).</summary>
    public float LastDepartWait { get; private set; }
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
    private WaveDeviceParameter SpeedParameter => new WaveDeviceParameter(SpeedKey, "이동 속도", "Player를 태우고 경로를 따라 옮기는 일정 속도입니다(시작·끝 0.1초만 가감속). 빈 크레인의 복귀·호출은 40칸/초입니다.", "칸/초", 10f, 40f, 1f, travelSpeed);

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

    public override bool ZipAvailable => base.ZipAvailable && Mathf.Abs(pathS - DockS(restDock)) < .05f;
    public override Vector2 ZipFeet => worldPath.Length > 0 ? FacilityMesh.PointAlong(worldPath, pathS, out _) : HomeFeet;
    protected override float ShakeScale => 1.2f;
    protected override bool AnyFocus => ZipFocused || dockFocus[0] || dockFocus[1];

    // 빈 도크 i를 호출 지점으로 고를 수 있는지: 크레인이 반대 도크에서 쉬는 중(또는 이미 i로 오는 중)이고 쓸 수 있는 시점.
    private bool DockCallable(int index)
    {
        if (!ReadyToServe || pathLength < .05f || IsRiding) return false;
        if (phase == Phase.Calling) return targetDock == index;
        return phase == Phase.Idle && restDock != index;
    }

    private void SetDockFocus(int index, bool on)
    {
        if (dockFocus[index] == on) return;
        dockFocus[index] = on;
        RefreshFocus();
    }

    /// <summary>
    /// 5차 호출(B안): 크레인을 도크 index로 부른다(빈 채로 40칸/초). 이미 그 도크로 오는 중이면 true만 돌려준다. 쉬는 중이 아니면 false.
    /// 도착 때 caller가 그 도크를 계속 선택 중이면 FacilityZip.TryAutoZip으로 붙는다. 검사에서도 쓴다.
    /// </summary>
    public bool Call(PlayerController player, int index)
    {
        if (index < 0 || index > 1 || !DockCallable(index)) return false;
        if (phase == Phase.Calling) return true;
        caller = player;
        callTime = Time.time;
        Calls++;
        StartMove(index, Phase.Calling);
        return true;
    }

    // 빈 크레인을 도크 dock으로 옮기기 시작한다(복귀·호출 공통, 표시만 — 판정 없음).
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
        bool calling = phase == Phase.Calling;
        pathS = DockS(targetDock);
        restDock = targetDock;
        travelVelocity = Vector2.zero;
        phase = Phase.Idle;
        phaseTime = 0f;
        if (!calling) { Returns++; return; }
        CallArrivals++;
        var who = caller;
        caller = null;
        // 도착 때 그 도크가 계속 선택돼 있으면(FacilityZip이 확인) 자동으로 붙는다. 아니면 이 도크에서 기다린다.
        if (who != null && Time.time - callTime <= CallAutoWindow && targetDock < dockPoints.Length && FacilityZip.TryAutoZip(this, dockPoints[targetDock], who))
            AutoZips++;
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
        Returns = Calls = CallArrivals = AutoZips = 0;
        PeakSpeed = PeakMoveSpeed = LastDepartWait = 0f;
        pathS = 0f;
        restDock = departDock = targetDock = 0;
        moveFrom = moveDistance = moveTime = 0f;
        moveSign = 1f;
        caller = null;
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

    private void UnregisterDocks()
    {
        for (int i = 0; i < dockPoints.Length; i++) FacilityZip.Unregister(dockPoints[i]);
        dockFocus[0] = dockFocus[1] = false;
        caller = null;
    }

    protected override void OnReturning()
    {
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
        reversedPath = new Vector2[count];
        for (int i = 0; i < count; i++) reversedPath[i] = worldPath[count - 1 - i];
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
        LastRideArrived = false;
        hauledToDock = false;
        PeakSpeed = 0f;
        LastDepartWait = 0f;
        departDock = restDock;
        caller = null;
        if (pathLength < .05f)
        {
            phase = Phase.Waiting;
            phaseTime = 0f;
            LastRideArrived = true;
            return;
        }
        phase = Phase.Riding;
        phaseTime = 0f;
        routine = StartCoroutine(RideRoutine(departDock));
    }

    private IEnumerator RideRoutine(int from)
    {
        bool done = false;
        try
        {
            // 출발 지연: 체결 흔들림 첫 반 주기가 읽히도록 물리 틱으로 0.12초 기다린다(구속은 이미 크레인이 잡고 있다).
            var tick = new WaitForFixedUpdate();
            float waited = 0f;
            while (waited < DepartDelay - 1e-4f)
            {
                yield return tick;
                if (!ride.IsRiding || ride.Interrupted) yield break;
                waited += Time.fixedDeltaTime;
            }
            LastDepartWait = waited;
            var path = from == 0 ? worldPath : reversedPath;
            for (var step = ride.HaulPath(path, activeSpeed, PathEase, OnHaulStep); step.MoveNext();) yield return step.Current;
            if (!ride.IsRiding || ride.Interrupted) yield break;
            // 마지막으로 넘긴 이동량이 반영된 뒤 도착 도크 대기로 바꾼다. 그 한 틱 안에 내려도 도착으로 본다(OnRideEnded — 경로는 이미 다 갔다).
            hauledToDock = true;
            yield return tick;
            done = ride.IsRiding && !ride.Interrupted;
        }
        finally
        {
            routine = null;
            travelVelocity = Vector2.zero;
            if (done)
            {
                pathS = DockS(1 - from);
                phase = Phase.Waiting;
                phaseTime = 0f;
                LastRideArrived = true;
            }
        }
    }

    private void OnHaulStep(Vector2 feet, Vector2 velocity, float distance)
    {
        travelVelocity = velocity;
        pathS = departDock == 0 ? distance : pathLength - distance;
        PeakSpeed = Mathf.Max(PeakSpeed, velocity.magnitude);
    }

    protected override void StopRideMotion()
    {
        if (routine != null) { StopCoroutine(routine); routine = null; }
        travelVelocity = Vector2.zero;
    }

    protected override void OnRideEnded(bool interrupted)
    {
        // 도착 도크에서 내렸으면 그 도크가 쉬는 자리가 된다(돌아가지 않는다). 도중에 내렸으면 빈 채로 출발 도크로 40칸/초 복귀한다.
        // 5차 검토 p2: 운반 경로를 다 간 뒤 마지막 이동량 반영 틱(아직 Riding) 안에 내려도 도착으로 본다(pathS는 이미 도착 도크).
        bool hauled = phase == Phase.Riding && hauledToDock;
        hauledToDock = false;
        if (phase == Phase.Waiting || hauled)
        {
            if (hauled) LastRideArrived = true;
            restDock = LastRideArrived && pathLength >= .05f ? 1 - departDock : departDock;
            pathS = DockS(restDock);
            phase = Phase.Idle;
            phaseTime = 0f;
            return;
        }
        restDock = departDock;
        StartMove(departDock, Phase.Returning);
    }

    protected override void OnGrabbed() => cageFlash = 0f;

    protected override float CageLevel()
    {
        if (cageFlash < 0f) return base.CageLevel();
        return Mathf.Max(base.CageLevel(), 2.4f);
    }

    // ── 선택 표시(도크 호출 지점) ─────────────────────────────

    protected override bool TryAimTarget(out Vector2 center, out float level)
    {
        for (int i = 0; i < 2; i++)
        {
            if (!dockFocus[i] || IsRiding) continue;
            center = DockFeet(i) + Vector2.up * BodyHalfHeight;
            var runner = FacilityZip.Runner;
            level = runner != null && i < dockPoints.Length && ReferenceEquals(runner.Locked, dockPoints[i]) ? lockedLevel : .5f;
            return true;
        }
        return base.TryAimTarget(out center, out level);
    }

    protected override Vector2 PromptPoint
    {
        get
        {
            for (int i = 0; i < 2; i++) if (dockFocus[i]) return DockFeet(i) + Vector2.up * .1f;
            return (carriagePlaced ? lastCarriageFeet : ZipFeet) + Vector2.up * .1f;
        }
    }

    // ── 매 프레임 ─────────────────────────────

    protected override void TickMotion(float dt)
    {
        if (transform.position != builtAt) RebuildPath();
        if (phase == Phase.Returning || phase == Phase.Calling)
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
        if (cageFlash >= 0f) { cageFlash += dt; if (cageFlash > .2f) cageFlash = -1f; }

        float speed = travelVelocity.magnitude;
        bool moving = (phase == Phase.Riding || phase == Phase.Returning || phase == Phase.Calling) && speed > .2f;
        // 레일: 대기 중 흐리고 느리게, 탑승 중 밝고 빠르게, 빈 이동 중 중간, 쓸 수 없으면 거의 꺼짐. 빛 덩어리는 다음 도크 쪽으로 흐른다.
        bool available = ZipAvailable;
        float line, band, joint, pulse, flowSpeed;
        float toward = NextDockSign();
        if (IsRiding) { line = 1.1f; band = .6f; joint = 1f; pulse = 1.4f; flowSpeed = 14f; }
        else if (phase == Phase.Returning || phase == Phase.Calling) { line = .6f; band = .35f; joint = .6f; pulse = .8f; flowSpeed = 8f; }
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

        // 탑승부 효과: 추진 불꽃(탑승 중 이동), 트롤리 접점 불꽃(속도 비례) + 기존 불꽃 효과 0.25초 간격, 구동음.
        FacilityFeedback.Emit(thruster, phase == Phase.Riding && speed > 1f);
        FacilityFeedback.Emit(trolleySparks, moving && speed > 6f);
        if (trolleySparks != null)
        {
            var emission = trolleySparks.emission;
            emission.rateOverTime = Mathf.Lerp(8f, 40f, Mathf.Clamp01(speed / 40f));
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
        if (phase == Phase.Riding || phase == Phase.Waiting) return departDock == 0 ? 1f : -1f;
        if (phase == Phase.Returning || phase == Phase.Calling) return moveSign;
        return restDock == 0 ? 1f : -1f;
    }

    // 도크등: 크레인이 있으면 켜짐, 빈 도크에서 부를 수 있으면 천천히 점멸(선택되면 조금 빠르게), 호출 중이면 빠르게 점멸, 그 밖에는 흐리게.
    // 유령 윤곽: 크레인이 없는 도크에만 흐리게.
    private void UpdateDockLights()
    {
        for (int i = 0; i < 2; i++)
        {
            bool here = Mathf.Abs(pathS - DockS(i)) < .05f;
            float lamp;
            if (phase == Phase.Calling && targetDock == i) lamp = Mathf.Repeat(clock, .25f) < .12f ? 1.3f : .2f;
            else if (here) lamp = 1f;
            else if (DockCallable(i))
            {
                float period = dockFocus[i] ? .5f : 1.2f;
                lamp = Mathf.Repeat(clock, period) / period < .5f ? .9f : .2f;
            }
            else lamp = .2f;
            lamp *= Mathf.Max(.25f, power);
            float ghost = here ? 0f : (dockFocus[i] ? .6f : .3f) * Mathf.Max(.25f, power);
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
