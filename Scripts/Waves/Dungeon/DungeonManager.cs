using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 던전 Wave의 진행(DESIGN.md §2.3, 2차 DESIGN_V2.md). TrainingRoomHost가 던전 실행 데이터가 있는 방 내용의 WaveManager와 같은 GameObject에 붙이고 Configure한다.
/// WaveManager는 고치지 않고 이벤트만 듣는다: 던전 Wave가 시작되면 시작 방을 세우고, 끝나거나 초기화되면 정리한다.
/// 방 이동: Player 몸통 중심이 열린 출입구의 입구 사각형에 들어오면(물리 질의 없음) 관 통과 연출(DungeonPassageEffectSO)로 방을 바꾼다.
/// 텔레포트 시설은 IDungeonFacilityHost.TryBeginTeleport로 같은 연출 실행기(일반화한 출발·도착 지점)를 써서 짝 방으로 옮긴다.
/// 아이작식 진행: 클리어 안 한 방에 들어오면 잠금 → 적 구성 생성 → 필수 적을 모두 처치하면 클리어(흔들림·시설 차례로 켜짐)·잠금 해제. 던전 조건을 채우면
/// WaveManager.HandleCustomTrigger(DungeonRulesSO.ClearTriggerKey)로 Wave를 끝내고, 다음 던전으로는 기존 지형 패널 + 크레인이 옮긴다.
/// UI에는 읽기 전용 상태와 이벤트만 내준다: 미니맵 = IDungeonMapSource, 방 상태 띠·적 위치 마커 = IDungeonCombatSource.
/// 시설(Prop)에는 IDungeonFacilityHost(DungeonFacilityHost.Current, 던전 진행 중에만)를 내준다.
/// 3차(DESIGN_V3.md): 목표(레버 등) — 진행 상태는 DungeonObjectiveTracker에 두고 여기에는 연결만 둔다(IDungeonObjectiveHost·IDungeonObjectiveSource 위임,
/// 클리어 조건 Objective = 목표 모두 완료, 배치가 없으면 AllRooms로 대신). 방 상세 지도(IDungeonDetailSource, 던전 시작 때 한 번 만듦),
/// 전투 중 시설의 목적지·사용 조건(IDungeonFacilityRoute), 목표 패널·현장 단말기 Prefab 생성(미니맵과 같은 방식).
/// 4차(DESIGN_V4 §2.6·§2.8): 시작 방 출구(목표 SO finish = ExitInStartRoom — 레버를 다 당기면 출구 해금, 출구에 들어가야 던전 클리어,
/// IDungeonExitHost·IDungeonExitSource, 진행 상태는 DungeonObjectiveTracker), 지금 방 목표 대상(IDungeonObjectiveTargets — UI 마커),
/// 실시간 난이도(DungeonThreatTracker 위임 — IDungeonThreatSource, 첫 클리어 방 수, 훈련 한 판 누적), 동적 적 구성(encounterMode = Dynamic —
/// 들어오는 순간 DungeonRoomSpawnInput → DungeonSpawnPointMap → DungeonEncounterComposer, 파도·동시 생존 상한은 DungeonEncounter),
/// 클리어한 전투 방 재입장 순찰(Phase Patrol — 잠그지 않고 클리어 수·RoomCleared에 세지 않음). 여기에는 연결만 둔다.
/// 4차 검토 반영: 난이도 첫 클리어는 동적 던전에서 필수 적이 실제로 나온 방만, 동적 구성이 비면 잠그지 않고 조용히 클리어, 순찰은 도착·구속 해제 뒤 시작하고
/// 떠나거나 던전 조건을 채우면 거둠, Player 가까움 대기를 넘긴 파도는 같은 방 후보에서 먼 자리를 다시 고름, 목표 마커는 던전 HUD 위에 그림.
/// 5차(DESIGN_V5 §1 S9): 방 상세 표식에 경로 크레인(경로 = 배치 설정 cranePath* → DungeonSpawnBlockers.TryCranePath)·고정 크레인·점프대를 더하고,
/// Player가 바라보는 쪽(IDungeonPlayerHeadingSource — 미니맵·큰 지도 Player 표식 꼬리)을 내준다. 둘 다 읽기 전용이다.
/// 6차(DESIGN_V6 T11): 레버·문 컷씬은 같은 GameObject의 DungeonCutsceneDirector가 맡는다(Wave를 넘는 코루틴 — EndFloor의 StopAllCoroutines를 피함).
/// 여기에는 연결만 둔다: 목표 완료·출구 입장 직후 감독 호출, 컷씬 동안 방 이동 감지·시설 사용 막기, 미리보기 시작·끝(시작 방을 상태 변경 없이 세움 — 레버 방은 보류),
/// 다음 던전 '시작 위치 복귀' 목적지 바꾸기, 재도전 때 감독 끊기, 던전 시작 방 도착 자리(room.arrival)에 도착 문 Prefab 넘기기.
/// 7차(DESIGN_V7 V5): 레버가 남은 방은 적 전멸만으로 클리어되지 않는다 — '레버 대기'(IDungeonObjectiveGate, 흐름은 DungeonRoomClearGate)를 거쳐 레버 뒤에 방 클리어를 잇는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonManager : MonoBehaviour, IDungeonMapSource, IDungeonCombatSource, IDungeonFacilityHost, IDungeonFacilityTokens,
    IDungeonObjectiveHost, IDungeonObjectiveSource, IDungeonDetailSource, IDungeonFacilityRoute,
    IDungeonExitHost, IDungeonExitSource, IDungeonObjectiveTargets, IDungeonThreatSource, IDungeonPlayerHeadingSource, IDungeonObjectiveGate, IDungeonThreatLadder
{
    // 관 통과·텔레포트 한 번의 요청(출발 지점·도착 방·도착 지점 번호·연출).
    private sealed class PassageRequest
    {
        public IDungeonPassagePoint from;
        /// <summary>출입구로 떠날 때만(텔레포트면 null).</summary>
        public DungeonDoor fromDoor;
        public int target;
        /// <summary>도착 지점이 텔레포트 시설인지(아니면 출입구).</summary>
        public bool teleport;
        /// <summary>도착 출입구 번호(socket) 또는 도착 텔레포트 항목 번호(pad).</summary>
        public int arrival;
        public DungeonPassageEffectSO effect;
    }

    private DungeonRun run;
    private WaveManager waves;
    private WaveCellTerrain terrain;
    private DungeonRoomStage stage;
    // 6차 컷씬 감독(같은 GameObject, Configure가 붙인다).
    private DungeonCutsceneDirector cutscenes;
    private DungeonFloor floor;
    private DungeonRoomState[] states = Array.Empty<DungeonRoomState>();
    private readonly List<DungeonMapRoom> mapRooms = new();
    private int currentRoom = -1;
    // 던전을 시작·정리할 때마다 올린다. 늦게 끝난 코루틴·처치 처리기가 다음 던전에 끼어들지 않게 한다.
    private int floorSerial;
    // 이 실행 데이터로 던전을 시작한 적이 있는지. 그 뒤의 초기화(WavesReset)는 재도전으로 보고 새 구조를 만든다.
    private bool begun;
    private bool floorCleared;
    private bool passing;
    private DungeonEncounter encounter;
    private DungeonRoomPhase phase = DungeonRoomPhase.None;
    private Coroutine passageRoutine;
    // 관 통과 연출의 순서(안쪽 순서까지 펼쳐 직접 돌린다). 중간에 끊기면 남은 순서를 Dispose해 연출의 finally(구속 해제)를 실행한다.
    private readonly Stack<IEnumerator> passageSteps = new();
    private GameObject minimap;
    private IDungeonMapView minimapView;
    private GameObject roomStatus;
    private IDungeonCombatView roomStatusView;
    // 3차 화면 요소: 미니맵이 함께 구현할 수 있는 상세·목표 접점, 목표 패널, 현장 단말기(지도·상세·목표 접점을 찾는 대로 연결).
    private IDungeonDetailView minimapDetailView;
    private IDungeonObjectiveView minimapObjectiveView;
    private GameObject objectivePanel;
    private IDungeonDetailView objectivePanelDetailView;
    private IDungeonObjectiveView objectivePanelView;
    private GameObject fieldTerminal;
    private IDungeonMapView fieldTerminalMapView;
    private IDungeonDetailView fieldTerminalDetailView;
    private IDungeonObjectiveView fieldTerminalObjectiveView;
    // 목표 진행(던전마다 Begin/End). 방 상세 지도(던전 시작 때 한 번 만들고 고치지 않음).
    private readonly DungeonObjectiveTracker objectives = new();
    private DungeonRoomDetail[] details = Array.Empty<DungeonRoomDetail>();
    private static readonly IReadOnlyList<IDungeonPassagePoint> NoPassages = Array.Empty<IDungeonPassagePoint>();
    private PlayerController bodyOwner;
    private Collider2D playerBody;
    // 실행 사본 Wave의 시작 좌표('시작 위치 복귀' 목적지)를 시작 방이 아닌 지금 방으로 옮겨 두었는지. 패배로 멈추면 시작 방 입장점으로 되돌린다.
    private bool startMoved;
    // 던전(층)당 한 번만 쓰는 시설 토큰(수리대 등). 던전을 시작할 때 비운다.
    private readonly HashSet<string> floorTokens = new();
    // Player를 붙잡고 있는 시설(크레인 등). 던전 클리어 뒤 Wave 종료를 잠깐 미룬다.
    private readonly HashSet<object> busyFacilities = new();
    private static FeedbackDataSO clearShake;
    // 잠글 때 Player가 열린 출입구의 파인 칸에 서 있으면 빠질 때까지 잠금을 미루는 최대 시간(초, 판단값).
    private const float LockWaitLimit = 2f;
    // 던전 조건을 채운 뒤 시설이 Player를 붙잡고 있으면 Wave 종료를 미루는 최대 시간(초). 층 클리어 뒤에는 새로 탈 수 없으므로
    // 이미 탄 탑승만 기다린다 — 가장 긴 크레인 탑승(집기·들기·레일 40칸·내리기 약 14초)보다 길게 둔다(판단값, DESIGN_V2의 3초에서 늘림).
    private const float FacilityBusyWaitLimit = 15f;
    private static readonly IReadOnlyList<EnemyController> NoEnemies = Array.Empty<EnemyController>();

    // ── 4차 ──
    // 실시간 난이도(훈련 한 판 누적: 첫 던전을 시작할 때·재도전·새 실행 데이터면 처음부터). 지금 방의 목표 대상(UI 마커).
    private readonly DungeonThreatTracker threat = new();
    private readonly List<DungeonObjectiveTarget> targets = new();
    // 출구 마커가 '해금됐지만 문이 아직 열리는 중(Locked 표시)'인지. 문이 실제로 열리면(IsOpen) Update가 대상 목록을 다시 만든다.
    private bool exitAwaitingOpen;
    // 출구 방에서의 연결 거리(출구 안내 — 출구를 쓰는 던전만, 아니면 빈 배열).
    private int[] exitDistance = Array.Empty<int>();
    // 목표 마커 화면 요소(DungeonRulesSO.objectiveMarkersPrefab)와 찾은 대로 연결한 목표 대상·난이도 화면 요소.
    private GameObject objectiveMarkers;
    private readonly List<IDungeonObjectiveTargetsView> targetsViews = new();
    private readonly List<IDungeonThreatView> threatViews = new();
    private IDungeonDetailView roomStatusDetailView;
    private IDungeonObjectiveView roomStatusObjectiveView;
    // 출구 입장 연출(문간 흡입) 동안 시설 붙잡음으로 알리는 최대 시간(초). 실물이 먼저 풀어도 된다(판단값 — 흡입 0.35초보다 길게).
    private const float ExitBusyHold = .6f;
    // 동적 구성 파도를 Player가 너무 가까울 때 미루는 최대 시간(초, 판단값). 넘기면 같은 방 후보에서 먼 자리를 다시 고른다(4차 검토).
    private const float SpawnCloseWaitLimit = 2f;
    // 순찰 시작 대기를 세기 전에 Player 구속(도착 연출 등)이 풀리기를 기다리는 최대 시간(초, 판단값 — 크레인 탑승처럼 긴 구속은 끝까지 기다리지 않는다).
    private const float PatrolArrivalHoldLimit = 3f;
    // 방에 들어올 때마다(SwapRoom) 올린다. 늦게 깨어난 순찰 코루틴이 다음 방문에 끼어들지 않게 한다.
    private int roomVisitSerial;
    /// <summary>마지막 동적 구성의 알림(검사·도구용 — 구성기가 남긴 근거·실패 사유).</summary>
    public IReadOnlyList<string> LastComposeNotes => lastComposeNotes;
    private readonly List<string> lastComposeNotes = new();
    /// <summary>마지막 동적 구성의 요청(검사용). 없으면 null.</summary>
    public DungeonEncounterRequest LastComposeRequest { get; private set; }

    // ── 읽기 전용 상태(IDungeonMapSource) ──
    public DungeonRun Run => run;
    public DungeonFloor Floor => floor;
    public bool HasFloor => floor != null;
    public int FloorNumber => floor != null ? floor.waveIndex + 1 : 0;
    public int FloorCount => run != null && run.Data != null && run.Data.waves != null ? run.Data.waves.Length : 0;
    public IReadOnlyList<DungeonMapRoom> Rooms => mapRooms;
    public int CurrentRoom => currentRoom;
    /// <summary>관 통과·텔레포트 중인지.</summary>
    public bool IsPassing => passing;
    /// <summary>던전 조건을 채워 Wave 종료를 기다리는 중인지.</summary>
    public bool IsFloorCleared => floorCleared;
    public DungeonRoomState RoomState(int id) => id >= 0 && id < states.Length ? states[id] : null;
    /// <summary>지금 세워 둔 방(출입구·Prop·텔레포트 시설).</summary>
    public DungeonRoomStage Stage => stage;
    /// <summary>지금 방의 적 구성(없으면 null).</summary>
    public DungeonEncounter Encounter => encounter;
    /// <summary>시설이 Player를 붙잡고 있다고 알린 수.</summary>
    public int BusyFacilityCount => busyFacilities.Count;
    /// <summary>지금 던전의 목표 진행(읽기 전용으로 쓸 것 — 회귀 검사·도구용).</summary>
    public DungeonObjectiveTracker Objectives => objectives;
    /// <summary>6차 컷씬 감독(레버·문 컷씬 — 읽기 전용으로 쓸 것, 회귀 검사용).</summary>
    public DungeonCutsceneDirector Cutscenes => cutscenes;
    // 컷씬이 도는 동안 방 이동 감지·시설 사용을 막는다.
    private bool CutsceneBlocking => cutscenes != null && cutscenes.BlocksDungeon;

    // ── 읽기 전용 상태(IDungeonCombatSource) ──
    public DungeonRoomPhase Phase => phase;
    public int HostilesRemaining => (phase == DungeonRoomPhase.Hostile || phase == DungeonRoomPhase.Patrol) && encounter != null ? encounter.RequiredRemaining : 0;
    public int HostilesTotal => encounter != null ? encounter.RequiredTotal : 0;
    public bool HostilesAllSpawned => encounter == null || encounter.SpawningDone;
    public IReadOnlyList<EnemyController> HostileEnemies => (phase == DungeonRoomPhase.Hostile || phase == DungeonRoomPhase.Patrol) && encounter != null ? encounter.RequiredEnemies : NoEnemies;
    public bool IsBossRoom => floor != null && currentRoom >= 0 && currentRoom == floor.layout.bossRoom;
    public bool IsHalted => floor == null || WaveHalted();

    public event Action MapChanged;
    public event Action<int> RoomEntered;
    /// <summary>방을 클리어했다(방 id). 미니맵·상태 띠가 함께 듣는다(IDungeonMapSource·IDungeonCombatSource 같은 이벤트).</summary>
    public event Action<int> RoomCleared;
    public event Action FloorCleared;
    /// <summary>던전을 시작했다(시작 방을 세운 직후).</summary>
    public event Action FloorStarted;
    /// <summary>던전을 정리했다(Wave 종료·초기화).</summary>
    public event Action FloorEnded;
    /// <summary>방이 잠기고 적 구성을 시작했다(붉은 'HOSTILES n' 시작).</summary>
    public event Action RoomHostile;
    /// <summary>남은 필수 적 수가 바뀌었다(남은 수, 전체 수).</summary>
    public event Action<int, int> HostilesChanged;
    /// <summary>지금 방의 출입구 잠금이 풀렸다.</summary>
    public event Action RoomUnlocked;

    // ── 읽기 전용 상태(IDungeonObjectiveSource — DungeonObjectiveTracker 위임) ──
    public bool HasObjective => objectives.HasObjective;
    public string ObjectiveTitle => objectives.Title;
    public string ObjectiveShortLabel => objectives.ShortLabel;
    public int ObjectiveDone => objectives.Done;
    public int ObjectiveTotal => objectives.Total;
    public Color ObjectiveColor => objectives.Color;
    public IReadOnlyList<DungeonObjectiveMarker> ObjectiveMarkers => objectives.Markers;
    public event Action ObjectiveChanged { add => objectives.Changed += value; remove => objectives.Changed -= value; }
    public event Action<int> ObjectiveCompleted { add => objectives.Completed += value; remove => objectives.Completed -= value; }

    // ── 읽기 전용 상태(IDungeonExitSource — 4차, DungeonObjectiveTracker 위임) ──
    public bool HasExit => floor != null && objectives.UsesExit;
    public bool ExitUnlocked => floor != null && objectives.ExitUnlocked;
    public bool ExitEntered => floor != null && objectives.ExitEntered;
    public int ExitRoom => floor != null ? objectives.ExitRoom : -1;
    public string ExitShortLabel => objectives.ExitShortLabel;
    public event Action ExitChanged { add => objectives.ExitChanged += value; remove => objectives.ExitChanged -= value; }

    // ── 읽기 전용 상태(IDungeonObjectiveTargets — 4차) ──
    public IReadOnlyList<DungeonObjectiveTarget> CurrentTargets => targets;
    public event Action TargetsChanged;

    // ── 읽기 전용 상태(IDungeonThreatSource — 4차, DungeonThreatTracker 위임) ──
    /// <summary>이 훈련(실행 데이터)에 동적 구성 던전이 있으면 true — 손 배치 던전(W2 보스)에서도 도달한 단계를 그대로 보인다.</summary>
    public bool HasThreat => floor != null && RunUsesThreat();
    public DungeonThreatLevel ThreatLevel => threat.ThreatLevel;
    public int ClearedRooms => threat.ClearedRooms;
    public int NextLevelAt => threat.NextLevelAt;
    public float DensityMultiplier => threat.DensityMultiplier;
    public event Action<DungeonThreatLevel, DungeonThreatLevel> ThreatChanged { add => threat.ThreatChanged += value; remove => threat.ThreatChanged -= value; }
    /// <summary>실시간 난이도 추적(읽기 전용으로 쓸 것 — 회귀 검사·도구용).</summary>
    public DungeonThreatTracker Threat => threat;
    public int ThreatThreshold(DungeonThreatLevel level) => HasThreat ? threat.Threshold(level) : 0;

    // ── 7차 V5: 레버 대기(IDungeonObjectiveGate — 흐름은 DungeonRoomClearGate, 여기는 연결만. ClearGate는 회귀 검사용 읽기 전용) ──
    private DungeonRoomClearGate clearGate;
    public DungeonRoomClearGate ClearGate => clearGate ??= new DungeonRoomClearGate(RoomState, () => CutsceneBlocking,
        (id, serial) => serial == floorSerial && floor != null && !WaveHalted() && currentRoom == id, (id, serial, fight) => FinishRoomClear(id, serial, fight, true));
    public bool CanCompleteObjectives => FacilitiesOpen && RoomState(currentRoom) is DungeonRoomState s && ((s.cleared && !s.locked) || s.objectiveHold);
    public bool AwaitingObjective => ClearGate.Awaiting;
    public int AwaitingRoom => ClearGate.Room;
    public event Action<int, bool> ObjectiveGateChanged { add => ClearGate.Changed += value; remove => ClearGate.Changed -= value; }

    // ── 읽기 전용 상태(IDungeonDetailSource) ──
    /// <summary>출입구 상태·현재 방·목표 완료 등 방 상세 표시가 바뀌었다(MapChanged와 함께, 그리고 목표 완료 때).</summary>
    public event Action DetailChanged;

    private static DungeonRulesSO Rules => DungeonRulesSO.Default;

    public void Configure(DungeonRun dungeonRun, WaveManager manager, WaveCellTerrain cells)
    {
        Unsubscribe();
        run = dungeonRun;
        waves = manager;
        terrain = cells;
        // 새 실행 데이터(새 입장)는 훈련 한 판의 시작이다.
        threat.Reset();
        var looseParent = cells != null ? cells.transform : transform;
        stage = new DungeonRoomStage(cells, Origin, looseParent);
        // 6차: 컷씬 감독(Wave를 넘는 연출은 거기서 돈다 — 여기는 연결만).
        if (cutscenes == null && !TryGetComponent(out cutscenes)) cutscenes = gameObject.AddComponent<DungeonCutsceneDirector>();
        cutscenes.Bind(this, manager);
        if (waves == null) return;
        waves.WaveIntroStarted += OnWaveIntro;
        waves.WaveStarted += OnWaveStarted;
        waves.WaveEnded += OnWaveEnded;
        waves.WavesReset += OnWavesReset;
        if (run != null && isActiveAndEnabled) DungeonRunState.Enter(this);
    }

    private void OnEnable()
    {
        if (run != null) DungeonRunState.Enter(this);
    }

    private void Unsubscribe()
    {
        if (waves == null) return;
        waves.WaveIntroStarted -= OnWaveIntro;
        waves.WaveStarted -= OnWaveStarted;
        waves.WaveEnded -= OnWaveEnded;
        waves.WavesReset -= OnWavesReset;
    }

    private void OnDisable()
    {
        // 방을 떠나 방 내용이 꺼지면 Wave도 멈춘다(WaveManager.OnDisable). 진행 중이던 관 통과를 풀고 방을 바로 거둔다.
        DungeonRunState.Exit(this);
        EndFloor(false);
    }

    private void OnDestroy()
    {
        DungeonRunState.Exit(this);
        EndFloor(false);
        Unsubscribe();
        if (minimapView != null) { try { minimapView.Unbind(); } catch (Exception e) { Debug.LogException(e); } }
        minimapView = null;
        if (minimap != null) Destroy(minimap);
        minimap = null;
        if (roomStatusView != null) { try { roomStatusView.Unbind(); } catch (Exception e) { Debug.LogException(e); } }
        roomStatusView = null;
        if (roomStatus != null) Destroy(roomStatus);
        roomStatus = null;
        Unbind(ref minimapDetailView);
        Unbind(ref minimapObjectiveView);
        Unbind(ref objectivePanelDetailView);
        Unbind(ref objectivePanelView);
        if (objectivePanel != null) Destroy(objectivePanel);
        objectivePanel = null;
        if (fieldTerminalMapView != null) { try { fieldTerminalMapView.Unbind(); } catch (Exception e) { Debug.LogException(e); } }
        fieldTerminalMapView = null;
        Unbind(ref fieldTerminalDetailView);
        Unbind(ref fieldTerminalObjectiveView);
        if (fieldTerminal != null) Destroy(fieldTerminal);
        fieldTerminal = null;
        Unbind(ref roomStatusDetailView);
        Unbind(ref roomStatusObjectiveView);
        foreach (var view in targetsViews) { try { view?.Unbind(); } catch (Exception e) { Debug.LogException(e); } }
        targetsViews.Clear();
        foreach (var view in threatViews) { try { view?.Unbind(); } catch (Exception e) { Debug.LogException(e); } }
        threatViews.Clear();
        if (objectiveMarkers != null) Destroy(objectiveMarkers);
        objectiveMarkers = null;
    }

    // ── WaveManager 이벤트 ──
    // 시작 안내가 뜰 때 시작 방(Prop·출입구)을 미리 세운다. 안내 없이 시작하면 Wave 시작 때 세운다. 관 통과는 Wave가 진행 중일 때만 된다.
    private void OnWaveIntro(int number) => TryBeginFloor(number - 1);
    private void OnWaveStarted(Wave wave) => TryBeginFloor(waves != null ? waves.currentWaveIndex : -1);
    private void OnWaveEnded() => EndFloor(true);

    private void OnWavesReset()
    {
        // 6차: 재도전·강제 정리면 진행 중인 컷씬을 먼저 끊고 원복한다(미리보기 방·문 안의 Player·크레인 생략 사본).
        if (cutscenes != null) cutscenes.Abort("재도전");
        bool retry = begun;
        EndFloor(true);
        if (!retry || run == null) return;
        // 재도전: 같은 실행 사본을 새 시드로 다시 만든다(매번 다른 구조). 지형 초기화(ResetMap)는 이미 옛 시작 방으로 돌아갔으므로 새 시작 방으로 바로 바꾼다.
        begun = false;
        // 실시간 난이도도 훈련 한 판의 처음(입문)으로 돌린다.
        threat.Reset();
        // 재생성(ApplyFloor)이 Wave 시작 좌표를 새 시작 방 입장점으로 다시 쓴다.
        DungeonRunBuilder.Regenerate(run, 0);
        startMoved = false;
        if (terrain == null) return;
        terrain.RegisterCells(run.AllCells());
        var first = run.FloorFor(0);
        var start = first != null ? first.Room(first.layout.startRoom) : null;
        if (start != null) terrain.ShowRoom(start.cells, start.entry);
        // 새 시작 방은 시차 배경 방이다(정적 벽면에 있다가 재도전하면 시차 배경으로 되돌린다).
        stage?.ClearBackdrop();
        // 재생성으로 쓰지 않게 된 옛 셀 배열의 초기맵 조회 집합을 놓아 준다(재도전마다 쌓이지 않게).
        terrain.ForgetStaleInitialCaches();
    }

    private void TryBeginFloor(int index)
    {
        if (run == null || !isActiveAndEnabled) return;
        var next = run.FloorFor(index);
        if (next == null || next == floor) return;
        if (floor != null) EndFloor(true);
        BeginFloor(next);
    }

    // ── 던전 시작·정리 ──
    private void BeginFloor(DungeonFloor next)
    {
        floorSerial++;
        floor = next;
        begun = true;
        floorCleared = false;
        passing = false;
        phase = DungeonRoomPhase.None;
        floorTokens.Clear();
        busyFacilities.Clear();
        states = new DungeonRoomState[next.rooms.Length];
        for (int i = 0; i < states.Length; i++) states[i] = new DungeonRoomState();
        // 4차 실시간 난이도: 훈련의 첫 던전이면 처음부터, 동적 구성 던전이면 그 규칙의 문턱·배율을 쓴다(클리어 수는 W1 → W2로 이어진다).
        if (next == FirstDungeonFloor()) threat.Reset();
        if (next.Dynamic) threat.Configure(next.EncounterRules.thresholds, next.EncounterRules.multipliers);
        threat.HasThreat = RunUsesThreat();
        BuildMap();
        var start = next.Room(next.layout.startRoom);
        if (start == null) { Debug.LogError("[DungeonManager] 시작 방이 없습니다.", this); floor = null; return; }
        DungeonFacilityHost.Current = this;
        currentRoom = start.Id;
        var state = states[currentRoom];
        state.visited = state.discovered = state.cleared = true;
        state.visits = 1;
        Discover(currentRoom);
        // 목표 진행을 시작한다(알게 된 방 = 미니맵 discovered). 클리어 조건이 목표인데 배치가 없으면 모든 방 클리어로 대신한다.
        objectives.Begin(next, id => id >= 0 && id < states.Length && states[id].discovered);
        if (next.settings != null && next.settings.clearCondition == DungeonClearCondition.Objective && !objectives.HasObjective)
            Debug.LogWarning($"[DungeonManager] Wave {next.waveIndex + 1}: 클리어 조건이 '목표 완료'지만 배치된 목표가 없어 모든 방 클리어 조건으로 대신합니다.", this);
        // 4차 출구: 시작 방 출구 방식인데 시작 방에 출구 자리가 없으면 마지막 목표에서 바로 끝낸다(즉시 방식).
        if (objectives.RequiresExit && !objectives.ExitPlaced)
            Debug.LogWarning($"[DungeonManager] Wave {next.waveIndex + 1}: 목표가 '시작 방 출구' 방식이지만 시작 방 '{start.node?.template?.name}'에 출구 자리(DungeonExitSlot)가 없어 마지막 목표에서 바로 끝냅니다.", this);
        exitDistance = objectives.UsesExit ? DungeonGenerator.DistancesFrom(next.layout, objectives.ExitRoom) : Array.Empty<int>();
        // 방 상세 지도(출구 표식은 출구를 쓰는지 정한 뒤에 넣는다).
        BuildDetails();
        SetWaveBounds(start);
        // '시작 위치 복귀' 목적지는 시작 방 입장점에서 시작한다(이전 진행이 옮겨 둔 값이 남지 않게).
        SetWaveStart(start.entry);
        startMoved = false;
        // 시작 방 셀은 WaveManager의 지형 준비(PrepareWave)가 이미 깔았다. Prop·출입구·시설만 세운다(시작 방은 시차 배경 방 — 덮개가 없다).
        stage.Enter(start, state, false, start.entry, DoorStateFor, next.objective, ExitPrefabFor(start), ArrivalPrefabFor(start));
        // 출구 문에 지금 진행(시작이므로 0)을 바로 맞춘다.
        PushExit(false);
        SyncMap();
        // 현장 단말기를 먼저 만든다: 같은 자리(상황판 바로 뒤)에는 나중에 만든 것이 형제 순서 앞(먼저 그려짐)에 끼므로, 먼저 만든 단말기가 던전 HUD 위에 그려진다.
        EnsureFieldTerminal();
        EnsureMinimap();
        EnsureRoomStatus();
        EnsureObjectivePanel();
        EnsureObjectiveMarkers();
        RebuildTargets();
        FloorStarted?.Invoke();
        NotifyMapChanged();
        RoomEntered?.Invoke(currentRoom);
        // 싸울 방이 하나도 없으면 바로 던전 조건을 채운다.
        if (FloorConditionMet()) BeginFloorClear();
    }

    /// <summary>던전을 정리한다. graceful이면 Prop·출입구·장치가 퇴장 연출을 재생하고, 아니면 바로 풀로 돌아간다.</summary>
    private void EndFloor(bool graceful)
    {
        AbortPassage();
        StopAllCoroutines();
        clearGate?.Abandon();
        bool had = floor != null;
        floorSerial++;
        if (encounter != null) { encounter.Stop(true, !graceful); encounter = null; }
        stage?.Leave(!graceful);
        floor = null;
        currentRoom = -1;
        floorCleared = false;
        passing = false;
        phase = DungeonRoomPhase.None;
        floorTokens.Clear();
        busyFacilities.Clear();
        if (ReferenceEquals(DungeonFacilityHost.Current, this)) DungeonFacilityHost.Current = null;
        states = Array.Empty<DungeonRoomState>();
        mapRooms.Clear();
        details = Array.Empty<DungeonRoomDetail>();
        exitDistance = Array.Empty<int>();
        lastComposeNotes.Clear();
        LastComposeRequest = null;
        objectives.End();
        exitAwaitingOpen = false;
        if (!had) return;
        targets.Clear();
        Raise(TargetsChanged);
        FloorEnded?.Invoke();
        NotifyMapChanged();
    }

    // ── 방 이동 감지 ──
    private void Update()
    {
        // 패배로 Wave가 멈추면(이벤트가 없다) 시작 좌표를 시작 방 입장점으로 되돌린다. 재도전의 지형 초기화가 시작 방 셀과 같은 기준점을 보게 한다.
        if (startMoved && floor != null && WaveHalted()) RestoreWaveStart();
        // 출구 마커: 문이 실제로 열린 프레임에 Ready로 바꾼다(아래 방 이동 감지의 조건과 무관하게 본다).
        WatchExitOpen();
        clearGate?.Tick(Time.deltaTime);
        if (floor == null || passing || floorCleared || CutsceneBlocking || waves == null || !waves.IsWaveActive || waves.IsPreparingWave) return;
        var state = RoomState(currentRoom);
        if (state == null || !state.cleared || state.locked) return;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || !player.isActiveAndEnabled || player.Health == null || player.Health.IsDead) return;
        // 크레인·다른 연출이 Player를 붙잡고 있는 동안에는 들어가지 않는다.
        if (player.Motor != null && player.Motor.IsConfined) return;
        Vector2 body = BodyCenter(player);
        DungeonDoor entered = null;
        foreach (var door in stage.Doors)
        {
            if (door == null || door.State != DungeonDoor.DoorState.Open || !door.MouthRect.Contains(body)) continue;
            entered = door;
            break;
        }
        // 목록을 다 읽은 뒤에 시작한다(연출이 첫 프레임에 방을 바꾸면 출입구 목록이 바뀐다).
        if (entered != null) BeginPassage(entered, player);
    }

    private void BeginPassage(DungeonDoor door, PlayerController player)
    {
        var room = floor.Room(currentRoom);
        if (room == null || door.Socket < 0 || door.Socket >= room.doors.Length) return;
        var placement = room.doors[door.Socket];
        if (!placement.Connected || floor.Room(placement.toRoom) == null) return;
        StartPassage(new PassageRequest { from = door, fromDoor = door, target = placement.toRoom, teleport = false, arrival = placement.toSocket, effect = floor.passage }, player);
    }

    private void StartPassage(PassageRequest request, PlayerController player)
    {
        // 4차 검토: 순찰 중에 떠나면 더 내지 않는다(관 통과 연출 중에 생성·생성 연출이 끼지 않게).
        StopPatrolForPassage();
        passing = true;
        passageRoutine = StartCoroutine(PassageRoutine(request, player, floorSerial));
    }

    // 순찰 구성의 생성을 멈춘다. 아직 하나도 나오지 않았으면 바로 거두고(None), 나온 적은 화면이 가려진 뒤 SwapRoom이 거둔다(눈앞에서 사라지지 않게).
    private void StopPatrolForPassage()
    {
        if (encounter == null || !encounter.IsBypass) return;
        if (encounter.Enemies.Count > 0) { encounter.Stop(false, false); return; }
        int total = encounter.RequiredTotal;
        encounter.Stop(true, true);
        encounter = null;
        phase = DungeonRoomPhase.None;
        Raise(HostilesChanged, 0, total);
    }

    // 순찰 구성을 거둔다(던전 조건 충족 — 클리어 대기 동안 순찰 적이 공격하지 않게, 4차 검토).
    private void StopPatrol(bool immediate)
    {
        if (encounter == null || !encounter.IsBypass) return;
        int total = encounter.RequiredTotal;
        encounter.Stop(true, immediate);
        encounter = null;
        if (phase == DungeonRoomPhase.Patrol) phase = DungeonRoomPhase.None;
        Raise(HostilesChanged, 0, total);
    }

    private IEnumerator PassageRoutine(PassageRequest request, PlayerController player, int serial)
    {
        var context = new DungeonPassageContext
        {
            Player = player,
            Terrain = terrain,
            FromDoor = request.fromDoor,
            From = request.from,
            IsTeleport = request.teleport,
        };
        context.SwapRoom = () =>
        {
            if (context.Swapped || serial != floorSerial) return;
            context.Swapped = true;
            SwapRoom(request.target, request.teleport, request.arrival);
        };
        context.ArrivalDoor = () => !request.teleport && context.Swapped && serial == floorSerial ? stage.DoorFor(request.arrival) : null;
        context.Arrival = () => context.Swapped && serial == floorSerial ? ArrivalPoint(request) : null;
        bool failed = false;
        var effect = request.effect;
        if (effect == null && request.teleport) SafeCall(request.from.PlayIntake);
        if (effect != null)
        {
            IEnumerator routine = null;
            try { routine = effect.Play(context); }
            catch (Exception e) { Debug.LogException(e, this); failed = true; }
            if (routine != null)
            {
                passageSteps.Clear();
                passageSteps.Push(routine);
                while (passageSteps.Count > 0)
                {
                    object current = null;
                    bool moved = false;
                    try
                    {
                        var top = passageSteps.Peek();
                        moved = top.MoveNext();
                        if (moved) current = top.Current;
                    }
                    catch (Exception e) { Debug.LogException(e, this); failed = true; break; }
                    if (!moved) { passageSteps.Pop(); continue; }
                    if (current is IEnumerator nested) { passageSteps.Push(nested); continue; }
                    yield return current;
                    if (serial != floorSerial) { DisposePassage(); yield break; }
                    // 패배(HaltForPlayerDefeat)는 이벤트 없이 Wave만 멈춘다. 남은 연출을 Dispose해 finally(구속 해제 등)만 돌리고,
                    // 방 교체·순간이동·잠금 없이 끝낸다(재도전·퇴장의 초기화가 나머지를 정리한다).
                    if (WaveHalted()) { HaltPassage(); yield break; }
                }
                // 예외로 빠져나왔으면 바깥 순서의 finally(구속 해제 등)를 마저 실행한다.
                DisposePassage();
            }
        }
        if (serial != floorSerial) yield break;
        // 연출이 스스로 끝난 뒤(예: 패배로 구속이 풀려 흡입 중에 빠져나온 경우)에도 대체 경로로 방을 바꾸지 않는다.
        if (WaveHalted()) { HaltPassage(); yield break; }
        if (failed || !context.Completed) FinishPassageDirectly(context, player, request, effect == null);
        passing = false;
        passageRoutine = null;
        AfterArrival(serial);
    }

    // 연출이 없거나 실패·중단되면 즉시 교체·순간이동으로 마무리한다(ElevatorRide 선례). 연출이 이미 옮겼으면 다시 옮기지 않는다.
    private void FinishPassageDirectly(DungeonPassageContext context, PlayerController player, PassageRequest request, bool noEffect)
    {
        if (!context.Swapped) context.SwapRoom();
        if (player == null || currentRoom != request.target) return;
        var room = floor.Room(request.target);
        var arrival = ArrivalPoint(request);
        Vector2 stand = arrival != null ? arrival.StandPoint : (Vector2)Origin() + StandLocal(request, room);
        var body = BodyCenter(player);
        // 이미 도착 지점 앞에 서 있으면(연출이 옮긴 뒤 끝만 못 채운 경우) 그대로 둔다.
        if (Vector2.Distance(body, stand) > .75f) Teleport(player, stand);
        if (request.teleport)
        {
            // 텔레포트 연출이 없으면 도착 시설의 배출 피드백만 낸다(관 배출음은 쓰지 않는다).
            if (noEffect && arrival != null) SafeCall(arrival.PlayEject);
        }
        else Play(Rules != null ? Rules.passageExitFeedback : null, stand);
    }

    private void AbortPassage()
    {
        if (!passing && passageSteps.Count == 0) return;
        if (passageRoutine != null) StopCoroutine(passageRoutine);
        passageRoutine = null;
        DisposePassage();
        passing = false;
    }

    // 패배로 멈춘 관 통과를 정리한다(코루틴 안에서 부른다. 남은 연출 순서의 finally만 실행한다).
    private void HaltPassage()
    {
        DisposePassage();
        passing = false;
        passageRoutine = null;
    }

    private void DisposePassage()
    {
        while (passageSteps.Count > 0)
        {
            var step = passageSteps.Pop();
            try { (step as IDisposable)?.Dispose(); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
    }

    // 연출이 화면을 가린 동안 한 번 부른다: 지금 방을 바로 거두고 → 셀을 즉시 교체(도착 지점 안쪽에서 공간을 찾음)·방 배경 → 대상 방을 세운다.
    private void SwapRoom(int target, bool teleport, int arrival)
    {
        var room = floor != null ? floor.Room(target) : null;
        if (room == null) return;
        if (encounter != null) { encounter.Stop(true, true); encounter = null; }
        phase = DungeonRoomPhase.None;
        roomVisitSerial++;
        var door = !teleport && arrival >= 0 && arrival < room.doors.Length ? room.doors[arrival] : null;
        var pad = teleport ? Pad(room, arrival) : null;
        Vector2 roomPoint = door != null ? RecessLocal(door) : pad != null ? PadBodyLocal(pad) : room.entry;
        currentRoom = target;
        var state = states[target];
        state.visited = state.discovered = true;
        // 4차: 방문 순번(순찰 구성 시드).
        state.visits++;
        if (teleport) state.teleportArrivals++;
        if (!HasFight(room)) state.cleared = true;
        Discover(target);
        // 셀 공간을 찾지 못할 때의 대체 범위·적 방향 기준을 이 방 실내로 맞춘다(실행 사본만 바꾼다).
        SetWaveBounds(room);
        // '시작 위치 복귀'(추격 분쇄기·함정 옵션)가 시작 방 좌표로 옮기지 않도록, 목적지를 이 방의 도착 지점 앞 발 자리로 바꾼다.
        SetWaveStart(door != null ? StandFeetLocal(door) : pad != null ? PadFeetLocal(pad) : room.entry);
        startMoved = true;
        stage.Enter(room, state, true, roomPoint, DoorStateFor, floor.objective, ExitPrefabFor(room), ArrivalPrefabFor(room));
        // 출구 문은 '마지막으로 보인 진행'으로 바로 세운다. 새 풀림은 도착한 뒤(AfterArrival) 차례로 재생한다(지연 재생).
        PushExit(false);
        SyncMap();
        NotifyMapChanged();
        RebuildTargets();
        RoomEntered?.Invoke(target);
    }

    private void AfterArrival(int serial)
    {
        if (serial != floorSerial || floor == null || WaveHalted()) return;
        var room = floor.Room(currentRoom);
        var state = RoomState(currentRoom);
        if (room == null || state == null) return;
        if (state.cleared)
        {
            // 4차: 출구 방(시작 방)에 돌아오면 아직 보이지 않은 풀림(볼트)과 해금을 재생한다.
            PushExit(true);
            if (FloorConditionMet()) { BeginFloorClear(); return; }
            // 4차 R18: 클리어한 전투 방 재입장 = 잠그지 않는 순찰 구성.
            if (PatrolEligible(room, state)) StartCoroutine(PatrolFight(room, state, serial));
            return;
        }
        StartCoroutine(RoomFight(room, state, serial));
    }

    // ── 방 전투 ──
    private IEnumerator RoomFight(DungeonRoomData room, DungeonRoomState state, int serial)
    {
        var rules = Rules;
        float lockDelay = rules != null ? rules.lockDelay : .25f;
        if (lockDelay > 0f) yield return new WaitForSeconds(lockDelay);
        // Player가 열린 출입구의 파인 칸(잠그면 고체가 켜질 자리)에 서 있으면 빠질 때까지 잠금을 미룬다.
        // 한도가 지나도 Player를 밀어내지 않는다. 그대로 잠그면 그 출입구만 고체·셔터가 Player가 빠질 때까지 열린 채로 기다린다(DungeonDoor).
        for (float waited = 0f; waited < LockWaitLimit && PlayerInOpenRecess(); waited += Time.deltaTime)
        {
            if (serial != floorSerial || currentRoom != room.Id || state.cleared || WaveHalted()) yield break;
            yield return null;
        }
        if (serial != floorSerial || currentRoom != room.Id || state.cleared || WaveHalted()) yield break;
        // 4차: 동적 구성 방은 지금(잠그는 같은 프레임) Player 위치·난이도 단계로 구성을 짠다. 손 배치 방은 3차 그대로(구성 Wave의 시작 대기 포함).
        WaveSpawnInfo[] items = room.encounter;
        DungeonEncounterPacing pacing = null;
        float startDelay = rules != null ? rules.encounterStartDelay : .5f;
        if (IsDynamicRoom(room))
        {
            items = ComposeDynamic(room, state, false, out pacing);
            // 4차 검토: 동적 구성이 비면(후보 0·규칙 미연결 등 비정상) 잠그지 않고 조용히 클리어한다(ROOM CLEAR 연출·난이도 첫 클리어 없음).
            if (!HasRequiredEnemy(items)) { ClearWithoutFight(room, state, serial); yield break; }
        }
        else
        {
            var wave = room.node != null && room.node.template != null ? room.node.template.EncounterWave(room.node.encounter) : null;
            startDelay += wave != null ? Mathf.Max(0f, wave.startDelay) : 0f;
        }
        state.locked = true;
        // 방 전체를 한꺼번에 잠그므로 출입구마다 소리를 내지 않고 Player 위치에서 한 번만 낸다.
        stage.SetDoorStates(DoorStateFor, false, true);
        Play(rules != null ? rules.doorLockFeedback : null, PlayerPoint());
        SyncMap();
        NotifyMapChanged();
        var fight = new DungeonEncounter(waves, items, Origin(), startDelay, terrain != null ? terrain.transform : transform, pacing, false);
        encounter = fight;
        int id = room.Id;
        fight.Cleared += () => OnRoomCleared(id, serial, fight);
        fight.RequiredChanged += (remaining, total) =>
        {
            if (serial != floorSerial || encounter != fight || phase != DungeonRoomPhase.Hostile) return;
            Raise(HostilesChanged, remaining, total);
        };
        fight.EnemySpawned += (enemy, position) =>
        {
            if (serial != floorSerial || encounter != fight) return;
            // 6차 T1(nav6): 무리 키·즉시 교전 설정을 생성 연출(PlayAnchor)보다 먼저 건다.
            OnEncounterEnemySpawned(room, enemy, position, false);
            PlayAnchor(enemy, position);
        };
        // 적 구성 시작 직후: 붉은 상태 띠(HOSTILES n = 아직 나오지 않은 필수 적 포함).
        phase = DungeonRoomPhase.Hostile;
        Raise(RoomHostile);
        Raise(HostilesChanged, fight.RequiredRemaining, fight.RequiredTotal);
        yield return fight.Run();
    }

    // 항목 중 처치 필수 적이 하나라도 있는지.
    private static bool HasRequiredEnemy(WaveSpawnInfo[] items)
    {
        if (items == null) return false;
        foreach (var info in items) if (DungeonEncounter.IsRequiredEnemy(info)) return true;
        return false;
    }

    // 4차 검토: 싸울 적 없이 끝난 동적 전투 방(구성이 빔). 잠그지 않았으므로 해제음·RoomCleared·ROOM CLEAR 연출·흔들림·RoomUnlocked·난이도 첫 클리어는 없고,
    // 방 상태(클리어)·미니맵·출입구(보스 봉인 해제)·목표 대상·시설 전원·던전 조건만 맞춘다. 경고는 ComposeDynamic이 남긴다.
    private void ClearWithoutFight(DungeonRoomData room, DungeonRoomState state, int serial)
    {
        // 7차 V5: 적 없이 끝난 레버 방도 레버를 요구한다 — 문을 잠그고 레버 대기로 간다(레버 뒤에는 기존 방 클리어 흐름, 난이도 첫 클리어 없음).
        if (NeedsObjective(room.Id)) { HoldForObjective(room.Id, serial, null); return; }
        state.cleared = true;
        state.locked = false;
        phase = DungeonRoomPhase.None;
        DiscoverTeleportPartners(room.Id);
        if (currentRoom == room.Id) stage.SetDoorStates(DoorStateFor, false, true);
        SyncMap();
        NotifyMapChanged();
        if (currentRoom == room.Id) RebuildTargets();
        if (FloorConditionMet()) BeginFloorClear();
        // 6차 T9(traps6): 싸울 적 없이 끝난 방도 함정을 멈춘다(조용히 클리어 — 정전 물결).
        HaltRoomHazards(room.Id, PlayerPoint());
        if (currentRoom == room.Id) StartCoroutine(PowerUpRoom(room.Id, serial));
    }

    // 7차 V5: 적 전멸 순간. 이 방에 당기지 않은 레버가 남았으면 레버 대기(문 잠김 유지), 아니면 바로 방 클리어(FinishRoomClear).
    private void OnRoomCleared(int id, int serial, DungeonEncounter fight)
    {
        if (serial == floorSerial && RoomState(id) is { cleared: false, objectiveHold: false } && NeedsObjective(id)) HoldForObjective(id, serial, fight);
        else FinishRoomClear(id, serial, fight, false);
    }

    // 지금 방의 목표 SO가 방 클리어를 막고(gatesRoomClear) 이 방에 아직 끝내지 않은 목표가 있고, 그 실물(레버)이 실제로 세워져 있는지.
    // fix7a: 실물이 없으면(생성 실패 등) 대기하지 않는다 — 당길 레버 없이 문이 잠긴 채 남는 갇힘 방지(6차처럼 적 전멸 = 방 클리어).
    private bool NeedsObjective(int id) => floor != null && currentRoom == id && floor.objective != null && floor.objective.gatesRoomClear && objectives.PendingIn(id)
        && stage != null && stage.ObjectivePoints.Any(p => p != null && p.RoomId == id && !p.IsCompleted);

    // 레버 대기 시작: 장치 접기, 함정·낙석 정지(적 전멸 순간 — 사용자 결정, 미리보기 전이라 이 방 함정이 멈춘다), 레버만 전원(빛·준비음), 대기 표시(objectiveHold·이벤트), 레버 마커 Ready.
    private void HoldForObjective(int id, int serial, DungeonEncounter fight)
    {
        var state = RoomState(id);
        // 적 없이 끝난 레버 방(아직 잠그지 않음)은 여기서 잠근다(방 전체 한 번 — 잠금음).
        if (!state.locked) { state.locked = true; stage.SetDoorStates(DoorStateFor, false, true); Play(Rules != null ? Rules.doorLockFeedback : null, PlayerPoint()); SyncMap(); NotifyMapChanged(); }
        phase = DungeonRoomPhase.Objective;
        if (encounter != null) encounter.RetractDevices(false);
        HaltRoomHazards(id, PlayerPoint());
        foreach (var part in stage.PoweredParts) if (part is IDungeonObjectivePoint) stage.SetPowered(part, true, false);
        ClearGate.Hold(id, serial, fight);
        RebuildTargets();
    }

    // 방 클리어 처리(6차까지의 OnRoomCleared 본문). fromHold = 레버 대기를 거쳐 왔음(함정은 대기 시작 때 이미 멈췄다 — 다시 보내지 않는다).
    private void FinishRoomClear(int id, int serial, DungeonEncounter fight, bool fromHold)
    {
        if (serial != floorSerial || floor == null) return;
        var state = RoomState(id);
        if (state == null || state.cleared || state.objectiveHold) return;
        state.cleared = true;
        if (currentRoom == id) phase = DungeonRoomPhase.Cleared;
        // 이 구성이 놓은 함정 장치는 접어 넣는다. 남은 비필수 적은 방을 떠날 때 거둔다.
        if (encounter != null) encounter.RetractDevices(false);
        // 보스가 있던 방이면 보스 HP 바를 내린다. 던전은 보스를 잡아도 Wave가 이어질 수 있어(모든 방 조건 등) 기존 Wave 종료 경로가 끄지 못한다.
        // 보스 등장 카메라·페이즈 연출은 보스가 사라지면 스스로 되돌아가므로(Room.ActivateBossEntryCamera·ShowBossPhase) 따로 되돌리지 않는다.
        if (HadBoss(floor.Room(id)) && !BossAlive() && UIManager.Instance != null) UIManager.Instance.ShowBossHP(false);
        var rules = Rules;
        var at = PlayerPoint();
        Play(rules != null ? rules.roomClearFeedback : null, at);
        PlayClearShake(rules, at);
        // 6차 T9: ROOM CLEAR와 같은 프레임에 이 방 함정을 정지한다(연결 자리).
        if (currentRoom == id && !fromHold) HaltRoomHazards(id, at);
        // 짝지은 텔레포트가 있으면 건너편 방을 알게 된다(미니맵 연결 표시).
        DiscoverTeleportPartners(id);
        SyncMap();
        RoomCleared?.Invoke(id);
        NotifyMapChanged();
        // 4차 실시간 난이도: 첫 클리어만 센다(순찰·시작·적 없는 방은 여기로 오지 않는다). 단계 상승(ThreatChanged)은 ROOM CLEAR(RoomCleared) 직후다.
        // 4차 검토: 동적 구성 던전(Dynamic)에서만 센다(손 배치 던전·W2 보스 방 클리어로 오르지 않게 — DESIGN_V4 §3-3).
        // 필수 적이 실제로 하나도 생성되지 않은 클리어(생성 실패를 처치로 본 경우)도 세지 않는다.
        if (CountsForThreat(id, fight)) state.clearOrder = threat.RecordFirstClear(floor.waveIndex, id);
        if (currentRoom == id) RebuildTargets();
        if (FloorConditionMet()) BeginFloorClear();
        // 시설을 Player에 가까운 것부터 차례로 켠다(점프대 등 IDungeonRoomPowered가 아닌 장치는 늘 켜져 있다).
        if (currentRoom == id) StartCoroutine(PowerUpRoom(id, serial));
        StartCoroutine(UnlockAfter(id, serial));
    }

    // 이 방의 첫 클리어를 난이도에 세는지: 동적 구성 던전·싸울 방·시작 방 아님·필수 적이 실제로 생성됨(4차 검토).
    private bool CountsForThreat(int id, DungeonEncounter fight)
        => floor != null && floor.Dynamic && HasFight(floor.Room(id)) && id != floor.layout.startRoom && fight != null && fight.RequiredSpawned > 0;

    private IEnumerator PowerUpRoom(int id, int serial)
    {
        var parts = stage.PoweredParts.ToList();
        if (parts.Count == 0) yield break;
        Vector3 at = PlayerPoint();
        parts.Sort((a, b) =>
        {
            float da = stage.PartPosition(a) is Vector3 pa ? ((Vector2)(pa - at)).sqrMagnitude : float.MaxValue;
            float db = stage.PartPosition(b) is Vector3 pb ? ((Vector2)(pb - at)).sqrMagnitude : float.MaxValue;
            return da.CompareTo(db);
        });
        var rules = Rules;
        float stagger = rules != null ? rules.facilityPowerStagger : .12f;
        for (int i = 0; i < parts.Count; i++)
        {
            if (serial != floorSerial || stage.Room == null || stage.Room.Id != id) yield break;
            stage.SetPowered(parts[i], true, false);
            if (stagger > 0f && i < parts.Count - 1) yield return new WaitForSeconds(stagger);
        }
    }

    private IEnumerator UnlockAfter(int id, int serial)
    {
        var rules = Rules;
        float delay = rules != null ? rules.unlockDelay : .6f;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        // 패배로 멈췄으면 잠금을 풀지 않는다(해제음·셔터가 패배 화면 뒤에서 움직이지 않게. 재도전·퇴장이 방을 정리한다).
        if (serial != floorSerial || WaveHalted()) yield break;
        var state = RoomState(id);
        if (state == null) yield break;
        state.locked = false;
        if (currentRoom == id)
        {
            // 방 전체 해제(봉인이 함께 풀리는 출입구 포함)이므로 출입구마다 소리를 내지 않고 Player 위치에서 한 번만 낸다.
            stage.SetDoorStates(DoorStateFor, false, true);
            Play(rules != null ? rules.doorUnlockFeedback : null, PlayerPoint());
        }
        SyncMap();
        NotifyMapChanged();
        if (currentRoom == id) { RebuildTargets(); Raise(RoomUnlocked); }
    }

    // ── 던전 조건 ──
    // 목표 완료(Objective): 배치된 목표를 모두(RequiredCount) 완료. 배치가 없으면(목표 SO 없음·배치 0) 아래 모든 방 규칙으로 대신한다(BeginFloor가 경고).
    // 4차: 시작 방 출구 방식이면 목표를 모두 채운 뒤 출구에 들어가야 한다(objectives.IsFinished).
    private bool FloorConditionMet()
    {
        if (floor == null) return false;
        int boss = floor.layout.bossRoom;
        if (floor.settings != null && floor.settings.clearCondition == DungeonClearCondition.BossRoom && boss >= 0 && boss < states.Length)
            return states[boss].cleared;
        if (floor.settings != null && floor.settings.clearCondition == DungeonClearCondition.Objective && objectives.HasObjective)
            return objectives.IsFinished;
        for (int i = 0; i < floor.rooms.Length; i++)
            if (HasFight(floor.rooms[i]) && !states[i].cleared) return false;
        return true;
    }

    private void BeginFloorClear()
    {
        if (floorCleared || floor == null) return;
        floorCleared = true;
        // 4차 검토: 순찰 중에 던전 조건을 채웠으면(즉시 방식의 마지막 레버 등) 순찰을 거둔다 — 'SECTOR CLEAR' 뒤 클리어 대기 동안 공격받지 않게.
        StopPatrol(false);
        FloorCleared?.Invoke();
        StartCoroutine(FloorClearRoutine(floorSerial));
    }

    private IEnumerator FloorClearRoutine(int serial)
    {
        // 시작 안내 중에 조건을 채웠으면(싸울 방이 없는 던전) Wave가 시작될 때까지 기다린다.
        while (serial == floorSerial && waves != null && !waves.IsWaveActive) yield return null;
        var rules = Rules;
        float delay = rules != null ? rules.floorClearDelay : 1.2f;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        // 시설(크레인 등)이 Player를 붙잡고 있으면 최대 FacilityBusyWaitLimit초 기다린다(다음 Wave 크레인 운반과 구속이 겹치지 않게).
        for (float waited = 0f; waited < FacilityBusyWaitLimit && serial == floorSerial && HasBusyFacility(); waited += Time.deltaTime)
            yield return null;
        if (serial != floorSerial || waves == null || !waves.IsWaveActive || floor == null) yield break;
        if (run.Data != null && run.Data.waves != null && floor.waveIndex >= run.Data.waves.Length - 1) PlaceClearPortal();
        waves.HandleCustomTrigger(DungeonRulesSO.ClearTriggerKey);
    }

    // 마지막 던전이면 Lobby 복귀 포탈을 지금 방에 둔다: 템플릿이 정한 위치, 없으면 그 방 시작 좌표.
    private void PlaceClearPortal()
    {
        var room = floor.Room(currentRoom);
        if (room == null) return;
        var template = room.node != null ? room.node.template : null;
        Vector2 point = template != null && template.overrideClearPortalPosition ? template.clearPortalPosition : room.entry;
        run.Data.overrideClearPortalPosition = true;
        run.Data.clearPortalPosition = point;
    }

    // ── 시설 접점(IDungeonFacilityHost) ──
    /// <summary>시설을 쓸 수 있는지: 던전 진행 중·Wave 진행 중(준비 아님)·패배 아님·관 통과 아님·지금 방 클리어·잠금 해제·던전 조건 미충족.
    /// 던전 조건을 채운 뒤(Wave 종료 대기)에는 새로 쓰지 못한다 — 탑승이 Wave 전환에 끊기거나, 안내가 뜨는데 눌러도 거절되거나,
    /// 수리 도중 방이 거둬져 회복을 잃지 않게 한다. 이미 탄 탑승은 SetFacilityBusy로 종료를 미룬다(Interrupted는 이 값을 보지 않는다).</summary>
    public bool CanUseFacilities => FacilitiesOpen && RoomState(currentRoom) is DungeonRoomState s && s.cleared && !s.locked;
    // 시설·목표 사용의 공통 조건(던전 진행·Wave 진행·관 통과 아님·컷씬 아님·패배 아님·던전 조건 미충족 — 7차에 CanCompleteObjectives와 나눠 씀).
    private bool FacilitiesOpen => floor != null && !floorCleared && !passing && !CutsceneBlocking && waves != null && waves.IsWaveActive && !waves.IsPreparingWave && !WaveHalted();

    /// <summary>층당 1회 표식을 이미 썼는지(IDungeonFacilityTokens). 같은 층에서 다시 세운 수리대가 처음부터 방전 상태로 선다.</summary>
    public bool IsFloorTokenUsed(string key) => floor != null && !string.IsNullOrEmpty(key) && floorTokens.Contains(key);

    public bool TryUseFloorToken(string key)
    {
        if (floor == null || string.IsNullOrEmpty(key)) return false;
        if (!floorTokens.Add(key)) return false;
        RoomState(currentRoom)?.usedTokens.Add(key);
        return true;
    }

    public bool TryBeginTeleport(DungeonTeleporter from)
    {
        if (from == null || !CanUseFacilities || floorCleared || !from.IsLinked) return false;
        if (from.RoomId != currentRoom || !stage.Owns(from)) return false;
        var target = floor.Room(from.LinkedRoom);
        if (target == null || target.Id == currentRoom || Pad(target, from.LinkedPad) == null) return false;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || !player.isActiveAndEnabled || player.Health == null || player.Health.IsDead) return false;
        if (player.Motor != null && player.Motor.IsConfined) return false;
        var state = RoomState(currentRoom);
        if (state != null) state.teleportUses++;
        var rules = Rules;
        StartPassage(new PassageRequest
        {
            from = from,
            fromDoor = null,
            target = target.Id,
            teleport = true,
            arrival = from.LinkedPad,
            // 텔레포트 연출은 던전 공통 규칙의 것만 쓴다(관 통과 연출은 출입구용). 없으면 즉시 교체·순간이동.
            effect = rules != null ? rules.teleportPassage : null,
        }, player);
        return true;
    }

    public void SetFacilityBusy(object owner, bool busy)
    {
        if (owner == null) return;
        if (busy) busyFacilities.Add(owner);
        else busyFacilities.Remove(owner);
    }

    // ── 전투 중 시설 접점(IDungeonFacilityRoute) ──
    /// <summary>지금 방에 세운 연결 출입구(DungeonDoor — 시설은 FloorY를 읽으려고 캐스트해도 된다). 던전 밖이면 빈 목록.</summary>
    public IReadOnlyList<IDungeonPassagePoint> RoomPassages => floor != null && stage != null ? stage.Doors : NoPassages;

    /// <summary>
    /// 전투 중 시설(크레인 붙기·탑승 — 4차에 차폐막 스위치·캐터펄트·승강기는 삭제)을 쓸 수 있는지: 던전 진행 중·Wave 진행 중(준비 아님)·관 통과 아님·패배 아님·
    /// 던전 조건 미충족. 지금 방의 클리어·잠금은 보지 않는다(CanUseFacilities와 다른 점).
    /// </summary>
    public bool CanUseFacilitiesInCombat
        => floor != null && !floorCleared && !passing && !CutsceneBlocking && waves != null && waves.IsWaveActive && !waves.IsPreparingWave && !WaveHalted();

    // ── 목표 접점(IDungeonObjectiveHost) ──
    /// <summary>
    /// 목표 실물(레버 등)이 완료를 요청한다. 목표 사용 조건(CanCompleteObjectives — 지금 방 클리어·잠금 해제 또는 7차 레버 대기, 관 통과 아님·던전 조건 미충족)과 지금 방·이 단계가 세운 실물·
    /// 배정(방·슬롯·번호)을 확인한 뒤 방 상태와 진행을 기록한다. 받아들이면 true(실물이 SetCompleted(true, false)로 연출을 적용한다).
    /// 마지막 목표면 던전 조건을 채워 Wave 종료로 넘어간다.
    /// </summary>
    public bool TryCompleteObjective(IDungeonObjectivePoint point)
    {
        if (point == null || floor == null || !objectives.HasObjective || !CanCompleteObjectives) return false;
        if (point.RoomId != currentRoom || stage == null || !stage.Owns(point)) return false;
        var state = RoomState(currentRoom);
        int slot = point.Slot, index = point.Index;
        if (state == null || state.completedObjectives.Contains(slot)) return false;
        if (objectives.IndexAt(currentRoom, slot) != index || objectives.IsDone(index)) return false;
        state.completedObjectives.Add(slot);
        if (!objectives.TryComplete(currentRoom, slot, index))
        {
            state.completedObjectives.Remove(slot);
            return false;
        }
        Raise(DetailChanged);
        RebuildTargets();
        // 6차 T11: 레버 컷씬 — 감독이 시작 방(출구 방)을 잠깐 세워 문이 풀리는 모습을 보이고 지연 재생 기록을 남긴다. 컷씬이 꺼졌거나 시작하지 못하면 4차 그대로.
        bool revealing = cutscenes != null && cutscenes.TryPlayLeverReveal(point);
        // 4차: 마지막 목표로 출구가 열렸으면 해금 알림만 낸다(ExitChanged — 문은 시작 방에 돌아왔을 때 지연 재생). 지금 방이 출구 방이면 바로 재생한다.
        if (!revealing && objectives.ExitUnlocked) PushExit(true);
        // 7차 V5: 레버 대기 방의 레버를 다 당겼으면 방 클리어를 잇는다(컷씬 뒤 또는 빗장 뒤). 던전 조건은 그 방 클리어 처리가 본다(SECTOR CLEAR가 ROOM CLEAR 뒤 — R3).
        if (state.objectiveHold && !objectives.PendingIn(currentRoom)) ClearGate.Release(revealing);
        else if (FloorConditionMet()) BeginFloorClear();
        return true;
    }

    // ── 출구 접점(IDungeonExitHost — 4차) ──
    /// <summary>
    /// 출구 문 실물이 입장을 요청한다. 출구를 쓰는 던전·해금됨·아직 안 들어감·시설 사용 조건(지금 방 클리어·잠금 해제·관 통과 아님·던전 조건 미충족)·
    /// 지금 방 = 출구 방·이 단계가 세운 실물·문이 열림을 확인한 뒤 입장을 기록하고 던전 클리어(SECTOR CLEAR → Dungeon.Clear)로 넘어간다.
    /// 문간 흡입은 실물이 한다(보호 구속). 그 동안 시설 붙잡음(SetFacilityBusy)으로 Wave 종료를 미룬다(최대 ExitBusyHold초 뒤 자동 해제).
    /// </summary>
    public bool TryEnterExit(IDungeonExitPoint exit)
    {
        if (exit == null || floor == null || !objectives.UsesExit || !objectives.ExitUnlocked || objectives.ExitEntered || !CanUseFacilities) return false;
        if (currentRoom != objectives.ExitRoom || exit.RoomId != currentRoom || stage == null || !stage.Owns(exit)) return false;
        bool open;
        try { open = exit.IsOpen; }
        catch (Exception e) { Debug.LogException(e, this); return false; }
        if (!open || !objectives.TryEnterExit()) return false;
        SetFacilityBusy(exit, true);
        StartCoroutine(ReleaseBusyAfter(exit, ExitBusyHold, floorSerial));
        // 6차 T11: 문 컷씬(문짝 닫힘 → 다음 Wave는 도착 문에서 시작) — 감독이 SetFacilityBusy로 Wave 종료를 문이 닫힐 때까지 미룬다.
        if (cutscenes != null) cutscenes.PlayExitEntry(exit);
        RebuildTargets();
        Raise(DetailChanged);
        if (FloorConditionMet()) BeginFloorClear();
        return true;
    }

    private IEnumerator ReleaseBusyAfter(object owner, float seconds, int serial)
    {
        if (seconds > 0f) yield return new WaitForSeconds(seconds);
        if (serial == floorSerial) SetFacilityBusy(owner, false);
    }

    /// <summary>
    /// 지금 방에서 출구 방 쪽으로 가는 연결 출입구(IDungeonExitSource, 4차). 출구가 해금됐고 아직 안 들어갔으며 지금 방이 출구 방이 아닐 때,
    /// 출구 방에서의 연결 거리(BFS)가 가장 짧아지는 연결 출입구(같으면 socket 번호가 작은 것). 보스 방을 거쳐 가는 길은 쓰지 않는다.
    /// </summary>
    public bool TryGetExitGuide(out int socket)
    {
        socket = -1;
        if (floor == null || !objectives.ExitUnlocked || objectives.ExitEntered || currentRoom < 0 || currentRoom == objectives.ExitRoom) return false;
        var node = floor.layout.Room(currentRoom);
        if (node == null || exitDistance.Length == 0) return false;
        int best = int.MaxValue;
        foreach (var link in node.links)
        {
            if (link.toRoom < 0 || link.toRoom >= exitDistance.Length) continue;
            if (link.toRoom == floor.layout.bossRoom && link.toRoom != objectives.ExitRoom) continue;
            int d = exitDistance[link.toRoom];
            if (d == int.MaxValue) continue;
            if (d < best || (d == best && link.socket < socket)) { best = d; socket = link.socket; }
        }
        return socket >= 0;
    }

    // ── 방 상세 지도(IDungeonDetailSource) ──
    /// <summary>방 상세(방 로컬 좌표, 던전 시작 때 한 번 만든 것). 없으면 null.</summary>
    public DungeonRoomDetail RoomDetail(int roomId) => roomId >= 0 && roomId < details.Length ? details[roomId] : null;

    /// <summary>방의 socket번 출입구 상태. 지금 방이면 세운 출입구의 실제 상태, 아니면 같은 규칙(잠금·보스 봉인)으로 계산한 값.</summary>
    public DungeonDoor.DoorState DoorState(int roomId, int socket)
    {
        var room = floor != null ? floor.Room(roomId) : null;
        if (room == null || socket < 0 || socket >= room.doors.Length) return DungeonDoor.DoorState.Open;
        if (roomId == currentRoom && stage != null)
        {
            var door = stage.DoorFor(socket);
            if (door != null) return door.State;
        }
        return DoorStateIn(roomId, room.doors[socket]);
    }

    /// <summary>Player 몸통 중심(지금 방 로컬 = 월드 − Wave 생성 기준점). 던전 밖이거나 Player가 없으면 false.</summary>
    public bool TryGetPlayerLocal(out Vector2 local)
    {
        local = default;
        if (floor == null || currentRoom < 0) return false;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || !player.isActiveAndEnabled) return false;
        local = BodyCenter(player) - (Vector2)Origin();
        return true;
    }

    /// <summary>5차(S9): Player가 바라보는 쪽(+1 오른쪽·-1 왼쪽, CharacterController2D.Facing 읽기만). 던전 밖이거나 Player가 없으면 false.</summary>
    public bool TryGetPlayerFacing(out int facing)
    {
        facing = 1;
        if (floor == null || currentRoom < 0) return false;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || !player.isActiveAndEnabled) return false;
        facing = player.Facing < 0 ? -1 : 1;
        return true;
    }

    // 파괴된 Unity 개체는 붙잡고 있다고 보지 않는다.
    private bool HasBusyFacility()
    {
        if (busyFacilities.Count == 0) return false;
        busyFacilities.RemoveWhere(o => o is UnityEngine.Object unity && unity == null);
        return busyFacilities.Count > 0;
    }

    // ── 출입구 상태 ──
    private DungeonDoor.DoorState DoorStateFor(DungeonDoorPlacement placement) => DoorStateIn(currentRoom, placement);

    // roomId 방의 출입구 상태 규칙(잠금 → 보스 방 봉인 → 열림). 지금 방은 세울 때·잠금·해제 때 이 값으로 맞춘다.
    private DungeonDoor.DoorState DoorStateIn(int roomId, DungeonDoorPlacement placement)
    {
        var state = RoomState(roomId);
        if (state != null && state.locked) return DungeonDoor.DoorState.Locked;
        if (placement != null && floor != null && placement.toRoom == floor.layout.bossRoom && roomId != floor.layout.bossRoom && BossSealed())
            return DungeonDoor.DoorState.Sealed;
        return DungeonDoor.DoorState.Open;
    }

    // 보스 방 봉인: 설정이 켜져 있고 보스 방이 아닌 싸울 방 중 클리어 안 한 방이 남았다.
    private bool BossSealed()
    {
        if (floor == null || floor.settings == null || !floor.settings.sealBossRoom) return false;
        int boss = floor.layout.bossRoom;
        if (boss < 0) return false;
        for (int i = 0; i < floor.rooms.Length; i++)
            if (i != boss && HasFight(floor.rooms[i]) && !states[i].cleared) return true;
        return false;
    }

    // 4차: 명시 값(DungeonRunBuilder가 채운다 — 동적 구성 전투 방은 손 배치 구성이 비어 있어도 true).
    private static bool HasFight(DungeonRoomData room) => room != null && room.HasFight;

    // 방 적 구성에 보스 등장 적이 있었는지.
    private static bool HadBoss(DungeonRoomData room)
    {
        if (room == null || room.encounter == null) return false;
        foreach (var info in room.encounter) if (info != null && info.isBossEnemy) return true;
        return false;
    }

    // 지금 방 구성에서 살아 있는 보스가 남았는지(처치 필수가 아닌 보스가 남아 있으면 HP 바를 내리지 않는다).
    private bool BossAlive()
    {
        if (encounter == null) return false;
        foreach (var enemy in encounter.Enemies)
        {
            if (enemy == null || !enemy.gameObject.activeInHierarchy) continue;
            var health = enemy.Health;
            if (health != null && health.IsBossEnemy && health.currentHP > 0f) return true;
        }
        return false;
    }

    // 패배(HaltForPlayerDefeat는 이벤트를 내지 않는다)·Wave 정지·Player 사망으로 던전 진행을 멈춰야 하는지.
    private bool WaveHalted()
    {
        if (waves == null || !waves.IsWaveActive) return true;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        return player != null && player.Health != null && player.Health.IsDead;
    }

    // Player 몸통이 지금 방의 열린 출입구 파인 칸과 겹치는지(물리 질의 없이 Collider 경계만 비교한다).
    private bool PlayerInOpenRecess()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || !player.isActiveAndEnabled) return false;
        foreach (var door in stage.Doors)
            if (door != null && door.State == DungeonDoor.DoorState.Open && door.IsPlayerInRecess(player)) return true;
        return false;
    }

    // ── 적 생성 앵커 ──
    // 생성 위치를 담는 앵커 Prop이 있으면 그 앵커가 생성 연출을 재생한다(제작 부스·수리 거치대·중계 비컨).
    private void PlayAnchor(EnemyController enemy, Vector3 position)
    {
        int propIndex = -1;
        var anchor = stage != null ? stage.AnchorAt(position, out propIndex) : null;
        if (anchor == null) return;
        // 다시 들어왔을 때 '내보낸 뒤' 모습으로 세우도록 방 상태에 남긴다.
        if (propIndex >= 0) RoomState(currentRoom)?.spentAnchors.Add(propIndex);
        try { anchor.PlaySpawn(enemy); }
        catch (Exception e) { Debug.LogException(e, this); }
    }

    // ── 클리어 피드백 ──
    // 방 클리어 흔들림(DungeonRulesSO.clearShakeIntensity/Duration). 기존 화면 흔들림 경로(FeedbackPlayer → Cinemachine Impulse, 창 한도 합산)를 쓴다.
    // 클리어 피드백 에셋에 흔들림이 이미 있으면 겹치지 않게 건너뛴다.
    private static void PlayClearShake(DungeonRulesSO rules, Vector3 at)
    {
        if (rules == null || rules.clearShakeIntensity <= 0f || FeedbackPlayer.Instance == null) return;
        if (rules.roomClearFeedback != null && rules.roomClearFeedback.shakeIntensity > 0f) return;
        if (clearShake == null)
        {
            clearShake = ScriptableObject.CreateInstance<FeedbackDataSO>();
            clearShake.name = "DungeonRoomClearShake (Runtime)";
            clearShake.hideFlags = HideFlags.HideAndDontSave;
        }
        // 흔들림만 낸다(입자·소리·후처리·히트스톱 없음).
        clearShake.particlePrefabs = Array.Empty<GameObject>();
        clearShake.sound = null;
        clearShake.postFX = null;
        clearShake.useHitStop = false;
        clearShake.shakeIntensity = rules.clearShakeIntensity;
        clearShake.shakeDuration = rules.clearShakeDuration;
        FeedbackPlayer.Instance.PlayExact(clearShake, at, Quaternion.identity);
    }

    // ── 미니맵 상태 ──
    private void BuildMap()
    {
        mapRooms.Clear();
        foreach (var room in floor.rooms)
        {
            var node = room.node;
            var links = new List<(Vector2Int, DungeonDoorSide, int)>();
            foreach (var link in node.links)
            {
                if (link.socket < 0 || link.socket >= node.sockets.Count) continue;
                var socket = node.sockets[link.socket];
                links.Add((node.gridOrigin + socket.unit, socket.side, link.toRoom));
            }
            int teleportTo = -1;
            foreach (var pad in room.teleports ?? Array.Empty<DungeonTeleportPlacement>())
                if (pad != null && pad.Linked) { teleportTo = pad.toRoom; break; }
            mapRooms.Add(new DungeonMapRoom
            {
                id = node.id,
                kind = node.kind,
                units = node.Units.ToArray(),
                links = links.ToArray(),
                sizeClass = room.SizeClass,
                teleportTo = teleportTo,
                rest = node.template != null && node.template.HasTag(DungeonRoomTags.Rest),
            });
        }
    }

    private void SyncMap()
    {
        bool sealedBoss = BossSealed();
        int boss = floor != null ? floor.layout.bossRoom : -1;
        for (int i = 0; i < mapRooms.Count && i < states.Length; i++)
        {
            var map = mapRooms[i];
            var state = states[i];
            map.known = state.discovered;
            map.visited = state.visited;
            map.cleared = state.cleared;
            map.sealedRoom = i == boss && sealedBoss;
        }
        // 알게 된 방이 늘었으면 목표 표식의 known도 고친다(바뀌었을 때만 ObjectiveChanged).
        objectives.RefreshKnown();
    }

    // MapChanged(미니맵)와 DetailChanged(방 상세)를 함께 알린다. MapChanged는 기존처럼 바로 부르고, 상세 쪽 예외는 진행을 끊지 않게 잡는다.
    private void NotifyMapChanged()
    {
        MapChanged?.Invoke();
        Raise(DetailChanged);
    }

    // 방 상세 지도: 방마다 셀·셀 범위·실내·연결 출입구·표식(텔레포트·수리대·목표·그 밖의 상호작용 시설)을 방 로컬 좌표로 한 번 만든다.
    // 5차(S9): 경로 크레인(집 발 + 경로 — 배치 설정 cranePath*를 DungeonSpawnBlockers.TryCranePath로 읽음)·고정 크레인(매달린 발)·점프대 표식을 더한다(물리 질의 없음).
    private void BuildDetails()
    {
        details = new DungeonRoomDetail[floor.rooms.Length];
        var kinds = new Dictionary<GameObject, int>();
        var parameters = new Dictionary<GameObject, WaveDeviceParameter[]>();
        var doors = new List<DungeonRoomDetailDoor>();
        var points = new List<DungeonRoomDetailPoint>();
        var paths = new List<Vector2[]>();
        var feet = new List<Vector2>();
        for (int i = 0; i < floor.rooms.Length; i++)
        {
            var room = floor.rooms[i];
            if (room == null) continue;
            doors.Clear();
            foreach (var door in room.doors ?? Array.Empty<DungeonDoorPlacement>())
                if (door != null && door.Connected)
                    doors.Add(new DungeonRoomDetailDoor { local = door.info.position, side = door.info.side, socket = door.socket, toRoom = door.toRoom });
            points.Clear();
            paths.Clear();
            foreach (var pad in room.teleports ?? Array.Empty<DungeonTeleportPlacement>())
                if (pad != null && pad.info != null)
                    points.Add(new DungeonRoomDetailPoint { local = pad.info.spawnPosition, kind = DungeonDetailMarkerKind.Teleport, index = pad.pad, toRoom = pad.toRoom });
            for (int k = 0; k < room.props.Length; k++)
            {
                var info = room.props[k];
                int kind = info != null ? FacilityKind(info.prefab, kinds) : -1;
                if (kind < 0) continue;
                var local = info.spawnPosition;
                int index = k;
                if (kind == (int)DungeonDetailMarkerKind.CranePath || kind == (int)DungeonDetailMarkerKind.CraneAnchor)
                {
                    // 2×4 크레인의 매달린 발 = 점유 칸 (x + 1, y)(DESIGN_V4 §2.3·§2.4 — 실행 쪽 생성 제외·템플릿 검사와 같은 기준).
                    var cells = DungeonSpawnBlockers.FootprintCells(info.spawnPosition, DungeonSpawnBlockers.Footprint(info.prefab, info.rotationSteps, new Vector2Int(2, 4)));
                    local = new Vector2(cells.xMin + 1f, cells.yMin);
                    if (kind == (int)DungeonDetailMarkerKind.CranePath)
                    {
                        if (!parameters.TryGetValue(info.prefab, out var list)) parameters[info.prefab] = list = DungeonSpawnBlockers.Parameters(info.prefab);
                        index = -1;
                        if (DungeonSpawnBlockers.TryCranePath(info, list, feet) && feet.Count > 0)
                        {
                            local = feet[0];
                            index = paths.Count;
                            paths.Add(feet.ToArray());
                        }
                    }
                }
                points.Add(new DungeonRoomDetailPoint { local = local, kind = (DungeonDetailMarkerKind)kind, index = index, toRoom = -1 });
            }
            if (floor.objective != null)
                foreach (var slot in room.objectives ?? Array.Empty<DungeonObjectivePlacement>())
                    if (slot != null && slot.Assigned && slot.info != null)
                        points.Add(new DungeonRoomDetailPoint { local = slot.info.spawnPosition, kind = DungeonDetailMarkerKind.Objective, index = slot.index, toRoom = -1 });
            // 4차: 시작 방 출구 문(출구를 쓰는 던전만).
            if (objectives.UsesExit && i == objectives.ExitRoom && room.HasExit)
                points.Add(new DungeonRoomDetailPoint { local = room.exit.spawnPosition, kind = DungeonDetailMarkerKind.Exit, index = 0, toRoom = -1 });
            details[i] = new DungeonRoomDetail
            {
                id = i,
                cells = room.cells,
                cellBounds = CellBounds(room.cells),
                interior = room.interior,
                doors = doors.ToArray(),
                points = points.ToArray(),
                cranePaths = paths.Count > 0 ? paths.ToArray() : null,
            };
        }
    }

    // Prop Prefab의 상세 표식 종류: 수리대 = Repair, 경로 크레인 = CranePath, 고정 크레인 = CraneAnchor, 점프대 = JumpPad(5차),
    // 그 밖의 상호작용 시설(IFacilityInteractTarget) = Facility, 아니면 -1. 같은 Prefab은 한 번만 찾는다.
    private static int FacilityKind(GameObject prefab, Dictionary<GameObject, int> cache)
    {
        if (prefab == null) return -1;
        if (cache.TryGetValue(prefab, out int kind)) return kind;
        kind = prefab.GetComponentInChildren<PropRepairStation>(true) != null ? (int)DungeonDetailMarkerKind.Repair
            : prefab.GetComponentInChildren<PropCraneHook>(true) != null ? (int)DungeonDetailMarkerKind.CranePath
            : prefab.GetComponentInChildren<PropCraneAnchor>(true) != null ? (int)DungeonDetailMarkerKind.CraneAnchor
            : prefab.GetComponentInChildren<WaveJumpPad>(true) != null ? (int)DungeonDetailMarkerKind.JumpPad
            : prefab.GetComponentInChildren<IFacilityInteractTarget>(true) != null ? (int)DungeonDetailMarkerKind.Facility : -1;
        cache[prefab] = kind;
        return kind;
    }

    private static RectInt CellBounds(WaveMapCell[] cells)
    {
        if (cells == null || cells.Length == 0) return default;
        int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
        foreach (var cell in cells)
        {
            xMin = Mathf.Min(xMin, cell.position.x);
            yMin = Mathf.Min(yMin, cell.position.y);
            xMax = Mathf.Max(xMax, cell.position.x);
            yMax = Mathf.Max(yMax, cell.position.y);
        }
        return new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
    }

    private void Discover(int id)
    {
        var node = floor.layout.Room(id);
        if (node == null) return;
        foreach (var link in node.links)
            if (link.toRoom >= 0 && link.toRoom < states.Length) states[link.toRoom].discovered = true;
        // 이미 클리어한 방(텔레포트가 켜진 방)이면 짝 방도 알게 된다.
        var state = RoomState(id);
        if (state != null && state.cleared) DiscoverTeleportPartners(id);
    }

    private void DiscoverTeleportPartners(int id)
    {
        if (floor == null) return;
        foreach (var link in floor.layout.teleports)
        {
            int other = link.roomA == id ? link.roomB : link.roomB == id ? link.roomA : -1;
            if (other >= 0 && other < states.Length) states[other].discovered = true;
        }
    }

    private void EnsureMinimap()
    {
        if (minimap != null) return;
        var rules = Rules;
        if (rules == null || rules.minimapPrefab == null) return;
        minimap = DungeonUiPlacement.Instantiate(rules.minimapPrefab, DungeonUiPlacement.Slot.AfterScoreboard);
        if (minimap == null) return;
        minimapView = minimap.GetComponentInChildren<IDungeonMapView>(true);
        // 미니맵이 '지금 방 자세히'(IDungeonDetailView)·목표 표식(IDungeonObjectiveView)도 구현하면 함께 연결한다.
        BindExtraViews(minimap, ref minimapDetailView, ref minimapObjectiveView);
        BindFourthViews(minimap);
        if (minimapView == null) return;
        try { minimapView.Bind(this); }
        catch (Exception e) { Debug.LogException(e, this); }
    }

    // 상황판 바로 뒤 목표 패널(DungeonRulesSO.objectivePanelPrefab)을 한 번 만들고 IDungeonObjectiveView로 연결한다. 목표가 없는 던전에서는 패널이 스스로 숨는다.
    private void EnsureObjectivePanel()
    {
        if (objectivePanel != null) return;
        var rules = Rules;
        if (rules == null || rules.objectivePanelPrefab == null) return;
        objectivePanel = DungeonUiPlacement.Instantiate(rules.objectivePanelPrefab, DungeonUiPlacement.Slot.AfterScoreboard);
        if (objectivePanel == null) return;
        BindExtraViews(objectivePanel, ref objectivePanelDetailView, ref objectivePanelView);
        BindFourthViews(objectivePanel);
        if (objectivePanelView == null) Debug.LogWarning("[DungeonManager] 목표 패널 Prefab에 IDungeonObjectiveView 컴포넌트가 없습니다.", objectivePanel);
    }

    // 현장 단말기(DungeonRulesSO.fieldTerminalPrefab, Tab으로 여는 큰 지도)를 한 번 만들고 찾은 지도·상세·목표 접점을 모두 연결한다.
    private void EnsureFieldTerminal()
    {
        if (fieldTerminal != null) return;
        var rules = Rules;
        if (rules == null || rules.fieldTerminalPrefab == null) return;
        // 단말기는 열면 화면을 덮으므로 HUD(상황판·미니맵·목표 패널)보다 앞, 암전 바로 앞에 둔다(ui3 약속).
        fieldTerminal = DungeonUiPlacement.Instantiate(rules.fieldTerminalPrefab, DungeonUiPlacement.Slot.BeforeFade);
        if (fieldTerminal == null) return;
        fieldTerminalMapView = fieldTerminal.GetComponentInChildren<IDungeonMapView>(true);
        if (fieldTerminalMapView != null)
        {
            try { fieldTerminalMapView.Bind(this); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
        BindExtraViews(fieldTerminal, ref fieldTerminalDetailView, ref fieldTerminalObjectiveView);
        BindFourthViews(fieldTerminal);
        if (fieldTerminalMapView == null && fieldTerminalDetailView == null && fieldTerminalObjectiveView == null)
            Debug.LogWarning("[DungeonManager] 현장 단말기 Prefab에 IDungeonMapView·IDungeonDetailView·IDungeonObjectiveView 컴포넌트가 없습니다.", fieldTerminal);
    }

    // root 안의 방 상세·목표 화면 접점을 찾아 연결한다(없으면 null로 둔다).
    private void BindExtraViews(GameObject root, ref IDungeonDetailView detail, ref IDungeonObjectiveView objective)
    {
        if (root == null) return;
        detail = root.GetComponentInChildren<IDungeonDetailView>(true);
        objective = root.GetComponentInChildren<IDungeonObjectiveView>(true);
        if (detail != null)
        {
            try { detail.Bind(this); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
        if (objective != null)
        {
            try { objective.Bind(this); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
    }

    private void Unbind(ref IDungeonDetailView view)
    {
        if (view != null) { try { view.Unbind(); } catch (Exception e) { Debug.LogException(e); } }
        view = null;
    }

    private void Unbind(ref IDungeonObjectiveView view)
    {
        if (view != null) { try { view.Unbind(); } catch (Exception e) { Debug.LogException(e); } }
        view = null;
    }

    // 화면 위 가운데 방 상태 띠·적 위치 마커(DungeonRulesSO.roomStatusPrefab)를 한 번 만들고 IDungeonCombatView로 연결한다.
    private void EnsureRoomStatus()
    {
        if (roomStatus != null) return;
        var rules = Rules;
        if (rules == null || rules.roomStatusPrefab == null) return;
        roomStatus = DungeonUiPlacement.Instantiate(rules.roomStatusPrefab, DungeonUiPlacement.Slot.AfterScoreboard);
        if (roomStatus == null) return;
        roomStatusView = roomStatus.GetComponentInChildren<IDungeonCombatView>(true);
        // 4차: 방 상태 띠도 목표(EXIT UNLOCKED)·난이도(THREAT ▲)·목표 대상 화면 요소를 구현하면 함께 연결한다.
        BindExtraViews(roomStatus, ref roomStatusDetailView, ref roomStatusObjectiveView);
        BindFourthViews(roomStatus);
        if (roomStatusView == null)
        {
            Debug.LogWarning("[DungeonManager] 방 상태 표시 Prefab에 IDungeonCombatView 컴포넌트가 없습니다.", roomStatus);
            return;
        }
        try { roomStatusView.Bind(this); }
        catch (Exception e) { Debug.LogException(e, this); }
    }

    // 4차: 지금 방 목표 대상 마커(DungeonRulesSO.objectiveMarkersPrefab)를 한 번 만들고 IDungeonObjectiveTargetsView로 연결한다(미니맵과 같은 방식).
    private void EnsureObjectiveMarkers()
    {
        if (objectiveMarkers != null) return;
        var rules = Rules;
        if (rules == null || rules.objectiveMarkersPrefab == null) return;
        objectiveMarkers = DungeonUiPlacement.Instantiate(rules.objectiveMarkersPrefab, DungeonUiPlacement.Slot.AfterScoreboard);
        if (objectiveMarkers == null) return;
        // 4차 검토: 같은 AfterScoreboard 자리는 나중에 만든 것이 형제 순서 앞(먼저 = 아래에 그려짐)에 끼므로, 목표 마커(화면 밖 화살·자물쇠)가
        // 미니맵·방 상태 띠·목표 패널 판 밑에 묻힌다. 그 셋 중 가장 뒤 형제 바로 다음으로 옮겨 HUD 위에 그린다.
        // 현장 단말기(BeforeFade 자리)는 그보다 뒤라 열면 여전히 마커를 덮는다.
        PlaceAboveDungeonHud(objectiveMarkers.transform);
        int before = targetsViews.Count;
        BindFourthViews(objectiveMarkers);
        if (targetsViews.Count == before) Debug.LogWarning("[DungeonManager] 목표 마커 Prefab에 IDungeonObjectiveTargetsView 컴포넌트가 없습니다.", objectiveMarkers);
    }

    // item을 같은 부모 아래 던전 HUD(미니맵·방 상태 띠·목표 패널) 중 가장 뒤 형제 바로 다음에 둔다(형제 순서 = 그리기 순서, 뒤가 위).
    private void PlaceAboveDungeonHud(Transform item)
    {
        var parent = item != null ? item.parent : null;
        if (parent == null) return;
        int last = -1;
        if (minimap != null && minimap.transform.parent == parent) last = Mathf.Max(last, minimap.transform.GetSiblingIndex());
        if (roomStatus != null && roomStatus.transform.parent == parent) last = Mathf.Max(last, roomStatus.transform.GetSiblingIndex());
        if (objectivePanel != null && objectivePanel.transform.parent == parent) last = Mathf.Max(last, objectivePanel.transform.GetSiblingIndex());
        if (last < 0) return;
        // SetSiblingIndex는 자기 자리를 뺀 목록에 끼운다: 지금 자리가 last보다 앞이면 last로 옮겨야 그 HUD 바로 뒤에 선다.
        int index = item.GetSiblingIndex();
        if (index < last) item.SetSiblingIndex(last);
        else if (index > last + 1) item.SetSiblingIndex(last + 1);
    }

    // 4차 화면 접점: root 안의 목표 대상(IDungeonObjectiveTargetsView)·난이도(IDungeonThreatView) 화면 요소를 모두 찾아 연결한다(같은 것은 한 번).
    private void BindFourthViews(GameObject root)
    {
        if (root == null) return;
        foreach (var view in root.GetComponentsInChildren<IDungeonObjectiveTargetsView>(true))
        {
            if (view == null || targetsViews.Contains(view)) continue;
            try { view.Bind(this); targetsViews.Add(view); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
        foreach (var view in root.GetComponentsInChildren<IDungeonThreatView>(true))
        {
            if (view == null || threatViews.Contains(view)) continue;
            try { view.Bind(this); threatViews.Add(view); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
    }

    // ── 4차: 출구·목표 대상·동적 구성·순찰 ──
    // 이 실행 데이터의 첫 던전(훈련 한 판의 시작).
    private DungeonFloor FirstDungeonFloor()
    {
        if (run == null || run.floors == null) return null;
        foreach (var f in run.floors) if (f != null) return f;
        return null;
    }

    // 이 실행 데이터에 동적 구성 던전이 하나라도 있는지(난이도 표시 여부).
    private bool RunUsesThreat()
    {
        if (run == null || run.floors == null) return false;
        foreach (var f in run.floors) if (f != null && f.Dynamic) return true;
        return false;
    }

    // 방에 세울 출구 문 Prefab(출구를 쓰는 던전의 출구 방만, 아니면 null).
    private GameObject ExitPrefabFor(DungeonRoomData room)
        => room != null && floor != null && floor.objective != null && objectives.UsesExit && room.Id == objectives.ExitRoom && room.HasExit ? floor.objective.ExitPrefab : null;

    // ── 6차: 컷씬 감독 연결(연출 순서는 DungeonCutsceneDirector) ──
    // 출구·도착 자리(5×6 뒤 벽) 가운데에서 문 앞에 선 몸통 중심까지(바닥 = 자리 가운데 − 3칸, 몸통 = 바닥 + 0.755).
    private static readonly Vector2 ExitStandOffset = new Vector2(0f, -3f + .755f);

    // 방에 세울 도착 문 Prefab(던전 시작 방의 도착 자리만): 규칙 SO의 arrivalGatePrefab, 없으면 이 훈련 목표 SO의 출구 문(겉모습이 앞 던전 출구 문과 같다).
    private GameObject ArrivalPrefabFor(DungeonRoomData room)
    {
        if (room == null || floor == null || !room.HasArrival || room.Id != floor.layout.startRoom) return null;
        var rules = Rules;
        if (rules != null && rules.arrivalGatePrefab != null) return rules.arrivalGatePrefab;
        if (run != null && run.floors != null)
            foreach (var f in run.floors)
                if (f != null && f.objective != null && f.objective.ExitPrefab != null) return f.objective.ExitPrefab;
        return null;
    }

    /// <summary>Wave 생성 기준점(spawnRoot)의 월드 위치(방 로컬 → 월드, 읽기 전용 — 컷씬 감독·회귀 검사가 읽는다).</summary>
    public Vector3 OriginWorld => Origin();
    /// <summary>지형(셀) — 컷씬 감독이 끊긴 뒤 Player 자리를 정할 때 읽는다.</summary>
    internal WaveCellTerrain Terrain => terrain;

    /// <summary>
    /// 레버 컷씬 미리보기 시작: 지금 방(레버 방)을 보류하고 출구 방(시작 방)을 상태 변경 없이 세운다(셀·Prop·출입구·출구 문 — 방문 수·발견·RoomEntered 없음).
    /// 순찰·남은 적은 거두고(돌아와도 다시 내지 않는다 — 방문 순번을 올려 기다리던 순찰도 멈춘다) 출구 문은 '마지막으로 보인 진행'으로 바로 맞춘다. 실패하면 false.
    /// </summary>
    internal bool BeginExitPreview()
    {
        if (floor == null || stage == null || stage.IsPreviewing || !objectives.UsesExit) return false;
        var start = floor.Room(objectives.ExitRoom);
        if (start == null || start.Id == currentRoom || start.Id < 0 || start.Id >= states.Length) return false;
        roomVisitSerial++;
        if (encounter != null)
        {
            int total = encounter.RequiredTotal;
            bool patrol = phase == DungeonRoomPhase.Patrol;
            encounter.Stop(true, true);
            encounter = null;
            if (patrol) { phase = DungeonRoomPhase.None; Raise(HostilesChanged, 0, total); }
        }
        Vector2 point = start.HasExit ? start.exit.spawnPosition + ExitStandOffset : start.entry;
        int startId = start.Id;
        if (!stage.BeginPreview(start, states[startId], point, placement => DoorStateIn(startId, placement), floor.objective, ExitPrefabFor(start), ArrivalPrefabFor(start)))
            return false;
        var exit = stage.Exit;
        if (exit != null)
        {
            int shown = objectives.ExitShownDone, total = objectives.Total;
            bool shownUnlocked = objectives.ExitShownUnlocked;
            SafeCall(() => exit.SetProgress(shown, total, shownUnlocked, true));
        }
        // 지금 방이 아닌 방이 서 있으므로 목표 대상은 비어 있다(마커가 저절로 사라진다).
        RebuildTargets();
        return true;
    }

    /// <summary>레버 컷씬 미리보기 끝: 미리보기 방을 거두고 보류한 레버 방을 되살린다(셀 = Player 몸통 쪽 공간). 미리보기 중이 아니면 아무것도 하지 않는다.</summary>
    internal void EndExitPreview()
    {
        if (stage == null || !stage.IsPreviewing) return;
        var held = stage.HeldRoom;
        Vector2 point = TryGetPlayerLocal(out var local) ? local : held != null ? held.entry : Vector2.zero;
        stage.EndPreview(point);
        stage.SetDoorStates(DoorStateFor, true, true);   // 7차 V5 안전장치(R1): 보류 중 바뀐 잠금을 복귀한 방 출입구에 다시 맞춘다(같으면 그대로)
        RebuildTargets();
    }

    /// <summary>지금 던전 실행 사본 Wave의 '시작 위치 복귀' 목적지를 바꾼다(문으로 도착한 다음 던전 — 걸어 나와 설 자리).
    /// 값은 방 로컬의 Player Transform 위치다(복귀 경로가 이 좌표에 Transform을 그대로 놓는다 — 감독이 몸통 기준점을 빼서 넘긴다, fix6d).</summary>
    internal void OverrideWaveStart(Vector2 standLocal)
    {
        if (floor != null) SetWaveStart(standLocal);
    }

    // ── 6차 다른 영역 연결(integration6이 nav6·traps6 패치를 넣었다) ──
    // T1(nav6): 방 적 구성(잠긴 전투 = patrol false, 재입장 순찰 = true)이 적을 낸 순간. DungeonEnemyEngagement로 즉시 교전·무리 키를 건다.
    // 무리 키 = Dungeon:{층 순번}:{방}:{종류}. 잠긴 방은 감지 거리 0(즉시 교전), 순찰은 Prefab 감지 거리 + 무리 감지 공유(생성 규칙 SO 스위치 — 손 배치 던전처럼 SO가 없으면 둘 다 켬).
    private void OnEncounterEnemySpawned(DungeonRoomData room, EnemyController enemy, Vector3 position, bool patrol)
    {
        if (room == null) return;
        DungeonEnemyEngagement.Configure(enemy, position, floorSerial, room.Id, patrol, floor != null ? floor.EncounterRules : null);
    }
    // T9(traps6): 방을 클리어한 순간(ROOM CLEAR와 같은 프레임). DungeonRoomStage의 함정 정지(판정 즉시 끔 + from에 가까운 순서 정전 물결)를 부른다.
    private void HaltRoomHazards(int roomId, Vector3 from)
    {
        if (currentRoom == roomId && stage != null) stage.HaltHazards(from);
    }

    /// <summary>
    /// 출구 문에 진행을 밀어 넣는다(출구 방에 있을 때만). reveal이 false면 '마지막으로 보인 진행'으로 연출 없이 맞추고(방을 세울 때),
    /// true면 지금 진행을 연출과 함께 적용한 뒤(아직 보이지 않은 풀림만 차례로 재생 — 실물 몫) 보인 값으로 기록한다.
    /// </summary>
    private void PushExit(bool reveal)
    {
        if (floor == null || stage == null || !objectives.UsesExit || currentRoom != objectives.ExitRoom) return;
        var exit = stage.Exit;
        if (exit == null) return;
        int total = objectives.Total;
        if (!reveal)
        {
            int shown = objectives.ExitShownDone;
            bool shownUnlocked = objectives.ExitShownUnlocked;
            SafeCall(() => exit.SetProgress(shown, total, shownUnlocked, true));
            return;
        }
        int done = objectives.Done;
        bool unlocked = objectives.ExitUnlocked;
        if (done == objectives.ExitShownDone && unlocked == objectives.ExitShownUnlocked) return;
        SafeCall(() => exit.SetProgress(done, total, unlocked, false));
        objectives.MarkExitShown(done, unlocked);
        RebuildTargets();
    }

    // 지금 방의 목표 대상(레버·출구)을 다시 만든다. 위치 = 실물 점유 사각형의 윗변 가운데(월드). 상태: 완료 → Done,
    // 레버는 방 클리어·잠금 해제 또는 레버 대기(7차)면 Ready(아니면 Locked), 출구는 문이 실제로 열렸으면(IsOpen) Ready·아니면 Locked·입장 뒤 Done(objective4 보고서 5-2).
    private void RebuildTargets()
    {
        targets.Clear();
        exitAwaitingOpen = false;
        var room = stage != null ? stage.Room : null;
        if (floor != null && room != null && room.Id == currentRoom)
        {
            var state = RoomState(room.Id);
            bool usable = state != null && ((state.cleared && !state.locked) || state.objectiveHold) && !floorCleared;
            Vector2 at = Origin();
            if (floor.objective != null)
                foreach (var slot in room.objectives ?? Array.Empty<DungeonObjectivePlacement>())
                {
                    if (slot == null || !slot.Assigned || slot.info == null || stage.ObjectiveFor(slot.slot) == null) continue;
                    bool done = objectives.IsDone(slot.index) || (state != null && state.completedObjectives.Contains(slot.slot));
                    var kind = done ? DungeonObjectiveTargetState.Done : usable ? DungeonObjectiveTargetState.Ready : DungeonObjectiveTargetState.Locked;
                    targets.Add(new DungeonObjectiveTarget(MarkerAt(stage.ObjectiveFor(slot.slot) as Component, slot.info, new Vector2Int(4, 6), at), DungeonObjectiveTargetKind.Lever, kind, slot.index));
                }
            if (objectives.UsesExit && room.Id == objectives.ExitRoom && room.HasExit && stage.Exit != null)
            {
                // 해금(ExitUnlocked)만으로는 Ready로 두지 않는다. 지연 해금 연출(볼트 → 봉인 → 문짝)이 끝나 문이 열린 뒤에 Ready가 된다.
                bool? read = objectives.ExitEntered ? true : ExitOpen(stage.Exit);
                bool open = read == true;
                var kind = objectives.ExitEntered ? DungeonObjectiveTargetState.Done : open ? DungeonObjectiveTargetState.Ready : DungeonObjectiveTargetState.Locked;
                // 읽기에 실패한 실물(read == null)은 매 프레임 다시 묻지 않는다(예외 로그 반복 방지).
                exitAwaitingOpen = !objectives.ExitEntered && read == false && objectives.ExitUnlocked;
                targets.Add(new DungeonObjectiveTarget(MarkerAt(stage.Exit as Component, room.exit, DungeonExitSlot.Size, at), DungeonObjectiveTargetKind.Exit, kind, 0));
            }
        }
        Raise(TargetsChanged);
    }

    // 출구 실물이 지금 열려 있는지(IsOpen). 다른 구현의 예외는 기록하고 null(모름)을 돌려준다.
    private bool? ExitOpen(IDungeonExitPoint exit)
    {
        if (exit == null) return false;
        try { return exit.IsOpen; }
        catch (Exception e) { Debug.LogException(e, this); return null; }
    }

    // 해금 연출 중인 출구 문이 실제로 열리면 대상 목록을 다시 만든다(Locked → Ready). 문이 열리는 순간을 알리는 이벤트가 접점에 없어서 상태를 본다.
    private void WatchExitOpen()
    {
        if (!exitAwaitingOpen) return;
        var exit = stage != null ? stage.Exit : null;
        if (exit == null || floor == null || currentRoom != objectives.ExitRoom) { RebuildTargets(); return; }
        if (ExitOpen(exit) != false) RebuildTargets();
    }

    // 4차 통합(objective4 보고서 5-2): 마커 자리 =실물의 MarkerPoint(레버·출구 문 루트 + (0, 3.3) — 점유 윗변 0.3칸 위). 실물 형을 모르면 점유 윗변 가운데.
    private static Vector3 MarkerAt(Component point, WaveSpawnInfo info, Vector2Int fallback, Vector2 origin)
    {
        if (point != null)
        {
            var lever = point.GetComponentInParent<PropObjectiveLever>();
            if (lever != null) return lever.MarkerPoint;
            var gate = point.GetComponentInParent<PropExitGate>();
            if (gate != null) return gate.MarkerPoint;
        }
        return TopCenter(info, fallback, origin);
    }

    private static Vector3 TopCenter(WaveSpawnInfo info, Vector2Int fallback, Vector2 origin)
    {
        var rect = DungeonSpawnBlockers.FootprintRect(info, fallback);
        return new Vector3(origin.x + rect.center.x, origin.y + rect.yMax, 0f);
    }

    // 동적 구성 방인지(동적 던전의 전투 방 — 보스 방은 손 배치 구성).
    private bool IsDynamicRoom(DungeonRoomData room) => floor != null && floor.Dynamic && room != null && room.node != null && room.node.dynamicFight;

    // 순찰(재입장) 대상: 동적 던전의 클리어한 전투 방(시작·휴식·보스 제외), 던전 조건 미충족.
    private bool PatrolEligible(DungeonRoomData room, DungeonRoomState state)
    {
        if (!IsDynamicRoom(room) || floorCleared || state == null || !state.cleared || state.locked) return false;
        if (room.Kind != DungeonRoomKind.Combat || room.Id == floor.layout.startRoom || room.Id == floor.layout.bossRoom) return false;
        var template = room.node.template;
        return template == null || !template.HasTag(DungeonRoomTags.Rest);
    }

    /// <summary>동적 구성의 난수 시드 = (실행 시드, 층, 방, 방문 순번). 같은 값이면 같은 구성이다(회귀 검사도 이 식을 쓴다).</summary>
    public static int EncounterSeedFor(int runSeed, int waveIndex, int roomId, int visit)
    {
        unchecked
        {
            uint h = (uint)runSeed * 0x9E3779B1u;
            h ^= (uint)(waveIndex + 1) * 0x85EBCA77u; h = (h << 13) | (h >> 19);
            h ^= (uint)(roomId + 1) * 0xC2B2AE3Du; h = (h << 17) | (h >> 15);
            h ^= (uint)(visit + 1) * 0x27D4EB2Fu;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            int value = (int)h;
            return value != 0 ? value : 1;
        }
    }

    /// <summary>
    /// 지금 방의 동적 구성을 짠다(4차): 후보 입력(셀·실내·세운 Prop 경계·생성 구역·제외 영역·Player 발, 방 로컬) → 후보 지점 → 구성기.
    /// 결과는 방 로컬 좌표 항목이고 DungeonEncounter가 기준점을 더한다. pacing = 동시 생존 상한·Player 거리·Bulwark 둘째 대기.
    /// 구성기가 실패하면 빈 목록(첫 방문이면 바로 클리어된다 — 알림을 남긴다).
    /// </summary>
    private WaveSpawnInfo[] ComposeDynamic(DungeonRoomData room, DungeonRoomState state, bool patrol, out DungeonEncounterPacing pacing)
    {
        pacing = null;
        lastComposeNotes.Clear();
        LastComposeRequest = null;
        var rules = floor != null ? floor.EncounterRules : null;
        if (rules == null || room == null) return Array.Empty<WaveSpawnInfo>();
        Vector2 origin = Origin();
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        // 4차 통합: 구성기는 '발' 위치를 받아 몸통 중심(발 + 0.755)으로 거리를 잰다. Player 기준점은 몸통 중심이라 몸 사각형 아랫변(Collider 경계 읽기)을 발로 쓴다.
        Vector2 feet = player != null ? FacilityAccess.Feet(player) - origin : room.entry;
        WaveSpawnInfo[] result = Array.Empty<WaveSpawnInfo>();
        DungeonSpawnPointMap map = null;
        int seed = EncounterSeedFor(run != null ? run.Seed : 1, floor.waveIndex, room.Id, state != null ? state.visits : 0);
        try
        {
            var input = DungeonRoomSpawnInput.Build(room, stage, floor.objective, feet, rules.firstWaveClearance, out int empty);
            map = DungeonSpawnPointMap.Build(input);
            var request = new DungeonEncounterRequest
            {
                rules = rules,
                map = map,
                level = threat.ThreatLevel,
                patrol = patrol,
                emptyCells = empty,
                interiorHeight = input.interior.height,
                orientation = room.node != null && room.node.template != null ? room.node.template.orientation : DungeonRoomOrientation.Mixed,
                seed = seed,
            };
            LastComposeRequest = request;
            var list = DungeonEncounterComposer.Compose(request, lastComposeNotes) ?? new List<WaveSpawnInfo>();
            var copy = new List<WaveSpawnInfo>(list.Count);
            foreach (var info in list)
            {
                if (info == null || info.prefab == null) continue;
                // 동적 구성의 적은 보스가 아니다(W1 Bulwark 포함 — 보스 HUD·BGM 없음).
                info.isBossEnemy = false;
                copy.Add(info);
            }
            result = copy.ToArray();
        }
        catch (Exception e) { Debug.LogException(e, this); result = Array.Empty<WaveSpawnInfo>(); }
        if (result.Length == 0 && !patrol)
            Debug.LogWarning($"[DungeonManager] Wave {floor.waveIndex + 1} 방 {room.Id}: 동적 구성이 적을 만들지 못해 바로 클리어됩니다. {string.Join(" / ", lastComposeNotes)}", this);
        float clearance = Mathf.Max(0f, rules.playerClearance);
        // 4차 검토: 가까움 대기 한도를 넘긴 파도는 같은 방의 후보 맵(이 구성이 쓴 것)으로 먼 자리를 다시 고른다(결정적 난수).
        var relocateMap = map != null && map.Count > 0 && clearance > 0f ? map : null;
        pacing = new DungeonEncounterPacing
        {
            concurrentCap = rules.ConcurrentCap(threat.ThreatLevel),
            tooClose = clearance > 0f ? (Func<Vector3, bool>)(position => PlayerNear(position, clearance)) : null,
            closeWaitLimit = SpawnCloseWaitLimit,
            relocate = relocateMap != null
                ? (DungeonWaveRelocator)((list, from, to, attempt, positions) => RelocateWave(rules, relocateMap, seed, origin, clearance, list, from, to, attempt, positions))
                : null,
            soloPrefab = rules.bulwark != null ? rules.bulwark.prefab : null,
            soloWaitLimit = rules.bulwark != null ? rules.bulwark.secondDelay : 6f,
        };
        return result;
    }

    /// <summary>파도 자리 다시 고르기의 난수 시드 = (구성 시드, 파도 첫 항목, 시도 번호). 같은 값이면 같은 난수다.</summary>
    public static int RelocateSeedFor(int encounterSeed, int waveStart, int attempt)
    {
        unchecked
        {
            uint h = (uint)encounterSeed * 0x9E3779B1u;
            h ^= (uint)(waveStart + 1) * 0x85EBCA77u; h = (h << 13) | (h >> 19);
            h ^= (uint)(attempt + 1) * 0xC2B2AE3Du;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
            return (int)h;
        }
    }

    // 파도 자리 다시 고르기 대리자 몸체(4차 검토): 지금 Player 발(방 로컬)과 이후 파도 거리(playerClearance)로 맵을 맞추고 구성기에 맡긴다.
    // '가까움'은 DungeonEncounter의 대기 판정(PlayerNear — 생성 위치와 Player 기준점 거리)과 같은 식으로 본다. 맵의 Player 설정은 되돌려 둔다.
    private bool RelocateWave(DungeonEncounterRulesSO rules, DungeonSpawnPointMap map, int seed, Vector2 origin, float clearance,
        IReadOnlyList<WaveSpawnInfo> list, int from, int to, int attempt, Vector2[] positions)
    {
        if (floor == null || rules == null || map == null) return false;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || !player.isActiveAndEnabled) return false;
        Vector2 feet = FacilityAccess.Feet(player) - origin;
        Vector2 pivot = (Vector2)player.transform.position - origin;
        float clearSq = clearance * clearance;
        var savedFeet = map.PlayerFeet;
        float savedClearance = map.PlayerClearance;
        map.SetPlayer(feet, clearance);
        try
        {
            var rng = new System.Random(RelocateSeedFor(seed, from, attempt));
            return DungeonEncounterComposer.RelocateWave(rules, map, rng, list, from, to, positions, p => (p - pivot).sqrMagnitude < clearSq);
        }
        finally { map.SetPlayer(savedFeet, savedClearance); }
    }

    // 월드 위치가 지금 Player 발에서 distance칸 안인지(Transform 위치 읽기 — 물리 질의 없음).
    private static bool PlayerNear(Vector3 position, float distance)
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || !player.isActiveAndEnabled) return false;
        return ((Vector2)(position - player.transform.position)).sqrMagnitude < distance * distance;
    }

    // 4차 R18 순찰: 잠그지 않고, 시작 대기(encounterStartDelay + patrolDelay) 뒤 짠 구성을 낸다. 떠나면 SwapRoom이 거둔다.
    // 4차 검토: 시작 대기는 도착(관 통과 끝)·Player 구속 해제 뒤부터 센다 — 구속은 최대 PatrolArrivalHoldLimit초까지만 기다린다.
    // 그 사이 다시 떠나면(관 통과 시작) 짜지 않고, 짠 뒤 떠나면 StartPassage가 생성을 멈춘다(관 통과 연출 중 생성 방지).
    private IEnumerator PatrolFight(DungeonRoomData room, DungeonRoomState state, int serial)
    {
        int visit = roomVisitSerial;
        for (float held = 0f; passing || (held < PatrolArrivalHoldLimit && PlayerConfined()); held += Time.deltaTime)
        {
            if (serial != floorSerial || visit != roomVisitSerial || currentRoom != room.Id || floorCleared || WaveHalted()) yield break;
            yield return null;
        }
        if (serial != floorSerial || visit != roomVisitSerial || currentRoom != room.Id || encounter != null || passing || floorCleared || WaveHalted()) yield break;
        var items = ComposeDynamic(room, state, true, out var pacing);
        if (items.Length == 0) yield break;
        var rules = Rules;
        var encounterRules = floor.EncounterRules;
        float startDelay = (rules != null ? rules.encounterStartDelay : .5f) + (encounterRules != null ? Mathf.Max(0f, encounterRules.patrolDelay) : 1f);
        var fight = new DungeonEncounter(waves, items, Origin(), startDelay, terrain != null ? terrain.transform : transform, pacing, true);
        encounter = fight;
        int id = room.Id;
        fight.Cleared += () => OnPatrolCleared(id, serial, fight);
        fight.RequiredChanged += (remaining, total) =>
        {
            if (serial != floorSerial || encounter != fight || phase != DungeonRoomPhase.Patrol) return;
            Raise(HostilesChanged, remaining, total);
        };
        fight.EnemySpawned += (enemy, position) =>
        {
            if (serial != floorSerial || encounter != fight) return;
            // 6차 T1(nav6): 재입장 순찰은 Prefab 감지 거리 + 무리 감지 공유(생성 연출보다 먼저 건다).
            OnEncounterEnemySpawned(room, enemy, position, true);
            PlayAnchor(enemy, position);
        };
        // 순찰 상태(어두운 앰버 'HOSTILES n · BYPASS' — UI가 Phase로 구분한다). 출입구는 잠그지 않는다.
        phase = DungeonRoomPhase.Patrol;
        Raise(RoomHostile);
        Raise(HostilesChanged, fight.RequiredRemaining, fight.RequiredTotal);
        yield return fight.Run();
    }

    // Player가 구속돼 있는지(관 통과 도착 연출·크레인 등 — 모터 상태 읽기).
    private static bool PlayerConfined()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        return player != null && player.Motor != null && player.Motor.IsConfined;
    }

    // 순찰을 모두 처치했다: 방 상태·클리어 수·시설은 그대로 두고 전투 상태만 None으로 돌린다(ROOM CLEAR 없음).
    private void OnPatrolCleared(int id, int serial, DungeonEncounter fight)
    {
        if (serial != floorSerial || floor == null || encounter != fight || currentRoom != id) return;
        phase = DungeonRoomPhase.None;
        Raise(HostilesChanged, 0, fight.RequiredTotal);
    }

    // ── 좌표 ──
    // Wave 생성 기준점(spawnRoot)의 월드 위치. 방 데이터 좌표(템플릿 좌표)를 여기에 더한다.
    private Vector3 Origin()
    {
        if (waves != null && waves.SpawnRoot != null) return waves.SpawnRoot.position;
        return terrain != null ? terrain.transform.position : transform.position;
    }

    // 실행 사본에서 이 던전에 해당하는 Wave(없으면 null).
    private Wave FloorWave()
    {
        if (run == null || run.Data == null || run.Data.waves == null || floor == null || floor.waveIndex < 0 || floor.waveIndex >= run.Data.waves.Length) return null;
        return run.Data.waves[floor.waveIndex];
    }

    private void SetWaveBounds(DungeonRoomData room)
    {
        var wave = FloorWave();
        if (wave != null && wave.room != null) wave.room.bounds = room.interior;
    }

    // 실행 사본 Wave의 시작 좌표(방 로컬, 발 가운데 기준)를 바꾼다. 추격 분쇄기·함정의 '시작 위치 복귀'가 이 값을 읽는다.
    private void SetWaveStart(Vector2 point)
    {
        var wave = FloorWave();
        if (wave == null) return;
        wave.overridePlayerStart = true;
        wave.playerStart = point;
    }

    // 시작 좌표를 이 던전 시작 방의 입장점으로 되돌린다.
    private void RestoreWaveStart()
    {
        startMoved = false;
        var start = floor != null ? floor.Room(floor.layout.startRoom) : null;
        if (start != null) SetWaveStart(start.entry);
    }

    // 도착 지점(도착 출입구 또는 도착 텔레포트 시설). 아직 세우지 않았으면 null.
    private IDungeonPassagePoint ArrivalPoint(PassageRequest request)
    {
        if (request.teleport) return stage.TeleporterFor(request.arrival);
        return stage.DoorFor(request.arrival);
    }

    private static DungeonTeleportPlacement Pad(DungeonRoomData room, int pad)
        => room != null && room.teleports != null && pad >= 0 && pad < room.teleports.Length ? room.teleports[pad] : null;

    // 출입구 안쪽 깊숙이 들어간 몸통 중심(방 로컬). DungeonDoor.RecessPoint와 같은 계산이다.
    private static Vector2 RecessLocal(DungeonDoorPlacement placement)
    {
        float outward = placement.info.side == DungeonDoorSide.Right ? 1f : -1f;
        var p = placement.info.position;
        return new Vector2(p.x + outward * .25f, p.y - DungeonDoor.Height * .5f + .755f);
    }

    // 텔레포트 시설 위 몸통 중심(방 로컬). DungeonTeleporter.RecessPoint 기본 계산과 같다.
    private static Vector2 PadBodyLocal(DungeonTeleportPlacement pad) => pad.info.spawnPosition + new Vector2(0f, -DungeonTeleporter.StandOffsetY + .755f);

    // 텔레포트 시설 위 발 자리(방 로컬) = Wave 시작 좌표 규칙(x = 발 가운데, y = 발밑 바닥 윗면).
    private static Vector2 PadFeetLocal(DungeonTeleportPlacement pad) => pad.info.spawnPosition + new Vector2(0f, -DungeonTeleporter.StandOffsetY);

    // 도착 지점이 서지 않았을 때 쓸 서는 몸통 중심(방 로컬).
    private static Vector2 StandLocal(PassageRequest request, DungeonRoomData room)
    {
        if (request.teleport)
        {
            var pad = Pad(room, request.arrival);
            return pad != null ? PadBodyLocal(pad) : room != null ? room.entry + Vector2.up * .755f : Vector2.zero;
        }
        return StandLocal(room != null && request.arrival >= 0 && request.arrival < room.doors.Length ? room.doors[request.arrival] : null, room);
    }

    // 관에서 나와 방 안에 선 몸통 중심(방 로컬). DungeonDoor.StandPoint와 같은 계산이다.
    private static Vector2 StandLocal(DungeonDoorPlacement placement, DungeonRoomData room)
    {
        if (placement == null) return room != null ? room.entry + Vector2.up * .755f : Vector2.zero;
        float outward = placement.info.side == DungeonDoorSide.Right ? 1f : -1f;
        var p = placement.info.position;
        return new Vector2(p.x - outward * 2.2f, p.y - DungeonDoor.Height * .5f + .755f);
    }

    // 관에서 나와 방 안에 선 Player의 발 자리(방 로컬) = Wave 시작 좌표 규칙(x = 발 가운데, y = 발밑 바닥 윗면).
    // 시작 좌표는 Player 기준점(발)이 놓일 자리다(PlayerStartAt → TransformPoint → ResetPlayerPosition). 바닥 칸은 y − 1 줄이다.
    // y는 출입구 바닥(FloorY = 점유 칸 아래 경계 = 실내 바닥 윗면), x는 StandPoint와 같다. 관 통과 배출이 Player를 세우는 바로 그 자리다.
    private static Vector2 StandFeetLocal(DungeonDoorPlacement placement)
    {
        float outward = placement.info.side == DungeonDoorSide.Right ? 1f : -1f;
        var p = placement.info.position;
        return new Vector2(p.x - outward * 2.2f, p.y - DungeonDoor.Height * .5f);
    }

    /// <summary>
    /// 입장 로딩이 배경 모듈을 미리 만들 수 있도록 모든 던전 방의 연결 공간(방 로컬)을 미리 구한다(지형은 건드리지 않는다).
    /// 방마다 시작 좌표와 연결된 출입구 안쪽, 텔레포트 시설 위에서 찾는다. 찾지 못한 방은 그때 만든다.
    /// </summary>
    public static List<Rect> PredictRoomSpaces(DungeonRun dungeonRun)
    {
        var spaces = new List<Rect>();
        if (dungeonRun == null) return spaces;
        foreach (var floor in dungeonRun.floors)
        {
            if (floor == null) continue;
            foreach (var room in floor.rooms)
            {
                if (room == null) continue;
                if (TryRoomSpace(room, out var bounds) && !spaces.Contains(bounds)) spaces.Add(bounds);
            }
        }
        return spaces;
    }

    /// <summary>
    /// 입장 로딩에서 정적 벽면 방의 키트 Mesh를 미리 합쳐 두도록 방마다의 배경 요청을 만든다(같은 키트·같은 실내는 한 번).
    /// 실내는 PredictRoomSpaces와 같은 연결 공간이다 — 방을 세울 때 ShowRoom이 배경에 넘기는 값과 같아 캐시가 그대로 맞는다.
    /// </summary>
    public static List<DungeonRoomBackdropRequest> PredictRoomKits(DungeonRun dungeonRun)
    {
        var requests = new List<DungeonRoomBackdropRequest>();
        var rules = DungeonRulesSO.Default;
        if (dungeonRun == null || rules == null) return requests;
        foreach (var floor in dungeonRun.floors)
        {
            if (floor == null) continue;
            foreach (var room in floor.rooms)
            {
                if (room == null || room.backdrop != DungeonBackdropMode.Wall) continue;
                var kit = rules.WallKitPrefab(room.wallKit);
                if (kit == null || !TryRoomSpace(room, out var bounds)) continue;
                if (requests.Exists(r => r.kitPrefab == kit && r.bounds == bounds)) continue;
                requests.Add(new DungeonRoomBackdropRequest { mode = DungeonBackdropMode.Wall, kit = room.wallKit, kitPrefab = kit, bounds = bounds });
            }
        }
        return requests;
    }

    // 방의 연결 공간(방 로컬): 시작 좌표·연결된 출입구 안쪽·짝 있는 텔레포트 자리 중 처음 찾아지는 공간.
    private static bool TryRoomSpace(DungeonRoomData room, out Rect bounds)
    {
        bounds = default;
        var solids = new HashSet<Vector2Int>();
        foreach (var cell in room.cells) if (cell.kind == WaveCellKind.Platform) solids.Add(cell.position);
        var seeds = new List<Vector2> { room.entry };
        foreach (var door in room.doors) if (door != null && door.Connected) seeds.Add(RecessLocal(door));
        foreach (var pad in room.teleports ?? Array.Empty<DungeonTeleportPlacement>()) if (pad != null && pad.Linked && pad.info != null) seeds.Add(PadBodyLocal(pad));
        foreach (var seed in seeds)
            if (WaveCellRoomSpace.TryGetBounds(solids, seed, out bounds)) return true;
        return false;
    }

    // ── Player ──
    // 몸통 중심(월드). 물리 질의가 아니라 Collider의 위치(Transform + Collider offset)로 계산한다.
    // 4차 검토 반영 뒤: Collider2D.bounds는 물리 동기화(SyncTransforms·물리 틱) 전에는 순간이동 전 자리를 돌려준다. 예전에는 다른 시설이
    // 매 프레임 동기화를 강제해 늘 최신이었지만, 그 강제가 없어지자 순간이동 직후 첫 프레임의 출입구 감지가 낡은 자리를 봤다.
    // 회전 없는 Player에서는 동기화된 bounds.center와 같은 값이다.
    private Vector2 BodyCenter(PlayerController player)
    {
        if (bodyOwner != player) { bodyOwner = player; playerBody = player != null ? player.GetComponent<Collider2D>() : null; }
        if (playerBody != null && playerBody.enabled) return playerBody.transform.TransformPoint(playerBody.offset);
        return player != null ? (Vector2)player.transform.position + Vector2.up * .755f : Vector2.zero;
    }

    private Vector3 PlayerPoint()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        return player != null ? player.transform.position : Origin();
    }

    // 기존 순간이동 경로(PlayerController.ResetPlayerPosition) + 방 카메라 warp. 몸통 중심이 body(월드)에 오도록 옮긴다.
    private void Teleport(PlayerController player, Vector2 body)
    {
        Vector2 offset = BodyCenter(player) - (Vector2)player.transform.position;
        Vector3 before = player.transform.position;
        Vector3 target = body - offset;
        target.z = before.z;
        player.ResetPlayerPosition(target);
        Vector3 delta = player.transform.position - before;
        CinemachineCore.OnTargetObjectWarped(player.transform, delta);
        var room = GetComponentInParent<Room>();
        if (room != null && room.VirtualCamera != null) room.VirtualCamera.PreviousStateIsValid = false;
    }

    private static void Play(FeedbackDataSO data, Vector3 position)
    {
        if (data != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(data, position);
    }

    // 다른 영역(시설·UI) 코드를 부를 때 예외가 던전 진행을 끊지 않게 한다.
    private void SafeCall(Action action)
    {
        if (action == null) return;
        try { action(); }
        catch (Exception e) { Debug.LogException(e, this); }
    }

    private void Raise(Action handler)
    {
        if (handler == null) return;
        try { handler(); }
        catch (Exception e) { Debug.LogException(e, this); }
    }

    private void Raise(Action<int, int> handler, int a, int b)
    {
        if (handler == null) return;
        try { handler(a, b); }
        catch (Exception e) { Debug.LogException(e, this); }
    }
}
