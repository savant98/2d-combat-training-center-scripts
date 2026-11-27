using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 던전 현장 단말기(3차, 2026-11-24, DESIGN_V3 '현장 단말기'). 던전 진행 중 Tab으로 여는 손목 단말기 화면이다.
/// - 입력: 코드에서 만든 InputAction(Tab, InputBindingDisplay.InventoryPath — 기존 Lobby 단말기와 같은 키)을 던전 진행 중에만 켠다. 입력 설정 에셋·ProjectSettings 무수정.
///   Lobby 단말기(InventoryTerminalMenu)는 Lobby에서만 열리므로 같은 키가 겹치지 않는다. Alt+Tab은 무시한다. 닫기 = Tab 또는 Esc(UIManager가 OnCancel로 보낸다).
/// - 열 수 있을 때: 던전 진행 중 · 다른 입력 UI·UI 입력 차단·게임 입력 차단 없음 · 일시정지 아님 · Player 살아 있음 · 구속(관 통과·크레인) 아님 ·
///   패배 화면·결과 보드 아님 · Wave 진행 정지(패배) 아님. 열려 있는 동안 던전이 끝나거나 Player가 죽거나 가리는 화면이 열리면 바로 닫는다.
/// - 일시정지: 열면 GameTimeController.PushPause, 닫기 연출이 끝나면 자기 몫만 PopPause(기존 단말기와 같은 방식 — 다른 일시정지와 섞이지 않는다).
/// - 외형: InventoryTerminal.prefab의 손목 단말기 외형(Wrist·WatchCase·MechanicalFrame·ScreenClip 머리말·바닥줄)을 생성 도구가 복사한 사본(원본 무수정).
///   여는 연출도 기존 단말기와 같다(오른쪽 아래에서 −14° 기울어진 채 0.45배 → 1배로 밀려 올라옴, 손목은 아래에서 올라옴, 0.38초 열림·0.24초 닫힘, OutCubic, 실제 시간).
/// - 화면: 큰 지도(알게 된 방 실루엣을 실제 비율로 단위 칸 블록 안에 배치·출입구끼리 꺾인 선·Player 위치·텔레포트 연결·레버 표식, 방향키로 이동·확인으로 가운데) +
///   훈련 상황(TrainingRunTracker.Instance: 예상 등급·경과 시간·피격·등급 유지까지 남은 양·등급 기준표) + 목표 진행(IDungeonObjectiveSource).
/// - 접점: 게임플레이가 내주는 계약 인터페이스만 읽는다(IDungeonMapSource·IDungeonDetailSource·IDungeonObjectiveSource, 읽기 전용). DungeonManager를 직접 참조하지 않는다.
///   DungeonManager가 IDungeonMapView.Bind(this)만 불러도 원본이 상세·목표를 구현하면 스스로 함께 연결한다.
/// - 4차(2026-11-25, DESIGN_V4 §2.6·§2.8): 원본이 <see cref="IDungeonThreatSource"/>·<see cref="IDungeonExitSource"/>도 구현하면(선택 인터페이스, 'is'로 확인) 함께 구독한다.
///   훈련 상황 첫 줄 = '난이도 중급 · 클리어 5 · 다음 단계까지 1방'(최고 단계면 '최고 단계'), 목표 줄 끝 = '출구 잠김 레버 2/3' / '출구 개방 · 시작 방',
///   큰 지도에는 시작 방 출구 문 표식(잠김 흐림·열림 밝음 — DungeonMapMesh). 가운뎃점은 정적 글꼴에 없어 <see cref="DungeonUiText.MidDot"/>를 쓴다.
/// - 그림은 열 때·지도 이동 때·열려 있는 동안 원본이 바뀔 때만 다시 만든다. 매 프레임은 Player 점 자리·깜박임과 연출만 — 할당 없음.
/// - 5차(2026-11-25, DESIGN_V5 §1 S9): 큰 지도 표식은 배지형 픽토그램(점프대 포함), 지금 방 = 굵은 밝은 테두리 + 꺾쇠 4개 + 위 표지 삼각형 + '현재 방' 글자
///   (Current 층 — 지도를 다시 만들 때만 Mesh·글자 자리, 열려 있는 동안 currentPulsePeriod로 알파 맥동, 일시정지 중 실제 시간), 지도를 옮겨 지금 방이 영역 밖이면
///   가장자리 화살. Player 표식은 빛 고리 + 바라보는 쪽 꼬리(원본이 IDungeonPlayerHeadingSource도 구현하면 — 선택 인터페이스 — localScale.x ±1).
/// - 7차(2026-11-26, DESIGN_V7 §1.3·§1.4·§1.6):
///   V2: 크레인 표식은 지도에 그리지 않는다(범례 7칸 — DungeonMapMesh).
///   V4: 지금 방 = 주황 공기·벽 + 흰 Current 층(맥동 최저 0.6). 확대·축소 3단계(0 전체 = 5차 맞춤, 1 중간 = 두 pitch의 기하 평균, 2 확대 = 지금 방이 영역 짧은 쪽의 약 55%),
///   열 때마다 확대·지금 방 가운데로 초기화. 배율을 바꿔도 보이던 가운데를 유지한다. 방향키 한 걸음은 화면 px 기준(panStepPixels — 배율과 무관하게 같은 거리).
///   지도 모델(BuildFloorModel — 할당)은 원본이 바뀔 때만 다시 만들고, 이동·배율·끌기는 화면 맞춤(RefitView)만 한다(할당 없음).
///   V7: 마우스 — 머리줄 오른쪽 버튼 [−][+][현재 방] 왼쪽 클릭, 지도 위 휠(한 칸 = 한 단계, 위 = 확대, wheelCooldown 간격), 지도 왼쪽 끌기 = 이동.
///   EventSystem 포인터 이벤트를 받는 DungeonFieldTerminalPointer가 이 클래스의 공개 API를 부른다(입력 설정 에셋 무수정, Selectable 없음).
///   열림 연출이 끝나고 닫는 중이 아닐 때만 DeviceRoot·ScreenClip CanvasGroup의 blocksRaycasts를 켠다(Prefab 값은 false). 닫힐 때·비활성 때 끌기·hover를 초기화한다.
///   바닥줄 '[TAB] 닫기  [방향키/드래그] 이동  [휠] 확대/축소  [Z] 현재 방'(가운뎃점 대신 '/'). 기존 키보드(방향키·Z·Tab·Esc)는 그대로다.
///   V6: 정보 판은 계기판형 DungeonTerminalStatusPanel에 맡긴다(Refresh·PlayIntro·Advance 위임). 패널이 없는 옛 Prefab이면 3~4차 글자 경로를 그대로 쓴다.
/// Prefab(Assets/Resources/UI/DungeonFieldTerminal.prefab)은 tools/Unity/DungeonFieldTerminalSetup.cs가 만든다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonFieldTerminal : MonoBehaviour, IDungeonMapView, IDungeonDetailView, IDungeonObjectiveView, IDungeonThreatView, IWaveTerminalView, IUIInputReceiver
{
    public const string ResourcePath = "UI/DungeonFieldTerminal";
    public const float OpenDuration = .38f;
    public const float CloseDuration = .24f;

    /// <summary>단말기 상호작용 깊이 상태(메뉴 선택 → 상세 맵 탐색 / 재시작·포기 확인 팝업).</summary>
    public enum TerminalState
    {
        Menu = 0,
        MapDetail = 1,
        ConfirmPopup = 2,
    }

    /// <summary>하단 동작 버튼 키보드 선택 항목(던전: 상세 맵·재시작·포기, 일반 Wave: 재시작·포기).</summary>
    public enum ActionChoice
    {
        Map = 0,
        Restart = 1,
        GiveUp = 2,
    }

    /// <summary>확인 팝업 대상 동작.</summary>
    public enum ConfirmTarget
    {
        None = 0,
        Restart = 1,
        GiveUp = 2,
    }

    [Header("Presentation (InventoryTerminal 외형 사본)")]
    [Tooltip("열려 있는 동안만 켜는 표시 루트(배경 막 + 단말기)")]
    [SerializeField] private GameObject presentation;
    [SerializeField] private RectTransform fitRect;
    [SerializeField] private RectTransform terminalRoot;
    [SerializeField] private RectTransform wristRoot;
    [SerializeField] private CanvasGroup terminalGroup;
    [SerializeField] private CanvasGroup screenGroup;
    [SerializeField] private CanvasGroup backdropGroup;

    [Header("Map")]
    [SerializeField] private DungeonFieldTerminalMapGraphic mapGraphic;
    [SerializeField] private DungeonFieldTerminalMapGraphic playerGraphic;
    [SerializeField] private DungeonFieldTerminalMapGraphic legendGraphic;
    [Tooltip("5차 지금 방 테두리·꺾쇠·표지(비어 있으면 없음)")]
    [SerializeField] private DungeonFieldTerminalMapGraphic currentGraphic;
    [Tooltip("5차 '현재 방' 표지 글자(비어 있으면 없음)")]
    [SerializeField] private TMP_Text currentLabel;
    [SerializeField] private string currentLabelText = "현재 방";
    [Tooltip("5차 지금 방 테두리 맥동 주기(초, 실제 시간)와 최저 알파(7차 V4: 0.45 → 0.6 — 맥동 최저에도 또렷하게)")]
    [SerializeField, Min(.1f)] private float currentPulsePeriod = 1.2f;
    [SerializeField, Range(0f, 1f)] private float currentPulseMin = .6f;
    [SerializeField, Min(0f)] private float mapPadding = 18f;
    [Tooltip("단위 칸 한 칸의 최소·최대 px(큰 지도 '전체' 단계)")]
    [SerializeField, Min(4f)] private float minPitch = 48f;
    [SerializeField, Min(4f)] private float maxPitch = 190f;
    [SerializeField, Min(.1f)] private float playerBlinkPeriod = .9f;

    [Header("7차 확대·축소·마우스(DESIGN_V7 §1.4 V4·V7 — 판단값)")]
    [Tooltip("배율 단계 수(0 전체 · 1 중간 · 2 확대). 전체 pitch가 확대 pitch 이상이면 한 단계로 줄고 버튼이 흐려진다.")]
    [SerializeField, Range(1, 3)] private int zoomLevels = 3;
    [Tooltip("열 때마다 쓰는 배율 단계(기본 2 = 확대·지금 방 가운데)")]
    [SerializeField, Range(0, 2)] private int defaultZoom = 2;
    [Tooltip("확대 단계에서 지금 방이 지도 영역 짧은 쪽에서 차지하는 비율")]
    [SerializeField, Range(.1f, 1f)] private float zoomRoomShare = .55f;
    [Tooltip("확대 단계의 최대 pitch(단위 칸 px)")]
    [SerializeField, Min(4f)] private float maxZoomPitch = 420f;
    [Tooltip("방향키 한 번의 지도 이동(px — 배율과 무관하게 같은 화면 거리)")]
    [SerializeField, Min(4f)] private float panStepPixels = 140f;
    [Tooltip("휠 한 칸 사이 최소 간격(초, 실제 시간)")]
    [SerializeField, Min(0f)] private float wheelCooldown = .1f;
    [Tooltip("7차 지도 조작 버튼 층(Controls — 비어 있으면 없음)")]
    [SerializeField] private DungeonFieldTerminalMapGraphic controlsGraphic;

    [Header("7차 계기판형 정보 판(비어 있으면 3~4차 글자 판)")]
    [SerializeField] private DungeonTerminalStatusPanel statusPanel;

    [Header("상세 맵 · 재시작 · 포기 · 확인 팝업")]
    [SerializeField] private RectTransform mapPanelRoot;
    [SerializeField] private RectTransform infoPanelRoot;
    [SerializeField] private DungeonTerminalGaugeGraphic mapActiveFrame;
    [SerializeField] private RectTransform mapButtonRoot;
    [SerializeField] private DungeonTerminalGaugeGraphic mapButtonGraphic;
    [SerializeField] private TMP_Text mapButtonLabel;
    [SerializeField] private RectTransform restartButtonRoot;
    [SerializeField] private DungeonTerminalGaugeGraphic restartButtonGraphic;
    [SerializeField] private TMP_Text restartButtonLabel;
    [SerializeField] private RectTransform giveUpButtonRoot;
    [SerializeField] private DungeonTerminalGaugeGraphic giveUpButtonGraphic;
    [SerializeField] private TMP_Text giveUpButtonLabel;
    [SerializeField] private GameObject confirmPopupRoot;
    [SerializeField] private DungeonTerminalGaugeGraphic confirmFrameGraphic;
    [SerializeField] private TMP_Text confirmTitleText;
    [SerializeField] private TMP_Text confirmBodyText;
    [SerializeField] private DungeonTerminalGaugeGraphic confirmYesGraphic;
    [SerializeField] private TMP_Text confirmYesLabel;
    [SerializeField] private DungeonTerminalGaugeGraphic confirmNoGraphic;
    [SerializeField] private TMP_Text confirmNoLabel;
    [SerializeField] private string waveContext = "전투 훈련";
    [SerializeField] private string menuFooter = "[TAB/ESC] 닫기  [방향키] 메뉴 선택  [Z] 확인";
    [SerializeField] private string waveFooter = "[TAB/ESC] 닫기  [방향키] 메뉴 선택  [Z] 확인";
    [SerializeField] private string confirmFooter = "[방향키] 선택  [Z] 확인  [ESC] 취소";
    [SerializeField] private string mapLabel = "상세 맵";
    [SerializeField] private string restartLabel = "WAVE 재시작";
    [SerializeField] private string giveUpLabel = "훈련 포기";
    [SerializeField] private string confirmYesText = "예";
    [SerializeField] private string confirmNoText = "아니오";
    [SerializeField] private string restartConfirmBody = "처음부터 다시 시작할까요?";
    [SerializeField] private string giveUpConfirmBody = "훈련을 포기하고 로비로 돌아갈까요?";

    [Header("Text (정적 글꼴 esamanru — 공백은 여백 태그)")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text contextText;
    [SerializeField] private TMP_Text footerText;
    [SerializeField] private TMP_Text gradeText;
    [SerializeField] private TMP_Text trainingText;
    [SerializeField] private TMP_Text objectiveTitleText;
    [SerializeField] private TMP_Text objectiveText;
    [SerializeField] private DungeonObjectivePanelGraphic objectiveCells;

    [Header("Labels")]
    [SerializeField] private string title = "현장 단말기";
    [SerializeField] private string context = "전투 구역 지도";
    [SerializeField] private string footer = "[TAB] 닫기  [ESC] 메뉴  [방향키/드래그] 이동  [Q/E/휠] 확대/축소  [Z] 현재 방";
    [Header("4차 글자(정적 글꼴 — DungeonUiText.Glyphs 안)")]
    [SerializeField] private string threatRowLabel = "난이도";
    [SerializeField] private string exitRowLabel = "출구";
    [SerializeField] private string exitLockedLabel = "잠김";
    [SerializeField] private string exitOpenLabel = "개방";
    [SerializeField] private string exitWhereLabel = "시작 방";
    [SerializeField] private string leverLabel = "레버";

    [Header("UI 사운드(기존 Menu 공용 Feedback)")]
    [SerializeField] private FeedbackDataSO openFeedback;
    [SerializeField] private FeedbackDataSO moveFeedback;
    [SerializeField] private FeedbackDataSO confirmFeedback;
    [SerializeField] private FeedbackDataSO cancelFeedback;

    private static readonly Color Dim = new Color32(120, 160, 172, 255);
    private static readonly Color Bright = new Color32(143, 244, 255, 255);
    private static readonly Color FocusWhite = new Color32(255, 255, 255, 255);
    private static readonly Color MapCyan = new Color32(43, 200, 226, 255);
    private static readonly Color RestartMuted = new Color32(172, 130, 62, 255);
    private static readonly Color RestartAmber = new Color32(255, 176, 33, 255);
    private static readonly Color RestartBright = new Color32(255, 231, 179, 255);
    private static readonly Color GiveUpMuted = new Color32(176, 84, 72, 255);
    private static readonly Color GiveUpRed = new Color32(255, 90, 60, 255);
    private static readonly Color GiveUpBright = new Color32(255, 198, 186, 255);

    private IDungeonMapSource source;
    private IDungeonDetailSource detailSource;
    private IDungeonObjectiveSource objectiveSource;
    private IDungeonThreatSource threatSource;
    private IDungeonExitSource exitSource;
    private IDungeonPlayerHeadingSource headingSource;
    private IWaveTerminalSource waveSource;
    private int facing = 1;
    private bool currentLabelVisible;
    private InputAction toggleAction;
    private bool actionEnabled;
    private bool initialized;
    private bool isOpen, isClosing, transitioning, live;
    private float progress, transitionFrom, transitionTo, transitionTime, transitionDuration;
    private Vector2 terminalHome, wristHome, infoHome;
    private Quaternion terminalRotation = Quaternion.identity, wristRotation = Quaternion.identity;
    private Vector2 lastFitSize;
    private GameTimeController pauseOwner;
    private bool ownsPause;
    private float clock;
    private bool dirty;
    private int floorKey;
    private Vector2 pan;
    private readonly Dictionary<int, DungeonMapMesh.RoomSilhouette> silhouettes = new Dictionary<int, DungeonMapMesh.RoomSilhouette>();
    private DungeonMapMesh.FloorModel model;
    private DungeonMapMesh.FloorView view;
    private bool playerVisible;
    private readonly StringBuilder builder = new StringBuilder(512);
    // 7차 확대·축소·마우스
    private int zoomLevel, zoomCount = 1;
    private float fullPitch = 1f, zoomedPitch = 1f;
    private bool dragging;
    private Vector2 dragLast;
    private int controlHover = -1;
    private bool mapButtonHover;
    private bool restartHover;
    private bool giveUpHover;
    private bool confirmYesHover;
    private bool confirmNoHover;
    private TerminalState state = TerminalState.Menu;
    private ActionChoice selectedAction = ActionChoice.Map;
    private ConfirmTarget confirmTarget = ConfirmTarget.None;
    private bool confirmYes;
    private float lastWheel = -999f;
    private FeedbackDataSO lastUiFeedback;

    /// <summary>검증용 재시작 가로채기 훅. 설정하면 실제 GameManager 대신 이 콜백을 호출한다.</summary>
    public Action RestartHandler;
    /// <summary>검증용 포기 가로채기 훅. 설정하면 실제 GameManager 대신 이 콜백을 호출한다.</summary>
    public Action GiveUpHandler;

    // ===== 검증이 읽는 값 =====
    public IDungeonMapSource Source => source;
    public IDungeonDetailSource DetailSource => detailSource;
    public IDungeonObjectiveSource ObjectiveSource => objectiveSource;
    public IDungeonThreatSource ThreatSource => threatSource;
    public IDungeonExitSource ExitSource => exitSource;
    /// <summary>열려 있는지(닫기 연출 중 포함). IUIInputReceiver.</summary>
    public bool IsActive => isOpen;
    public bool IsClosing => isClosing;
    public bool IsTransitioning => transitioning;
    public float Progress => progress;
    /// <summary>Tab 입력이 켜져 있는지(던전 진행 중에만).</summary>
    public bool IsInputEnabled => actionEnabled;
    public bool OwnsPause => ownsPause;
    public bool PlayerVisible => playerVisible;
    public Vector2 Pan => pan;
    public DungeonMapMesh.FloorModel Model => model;
    public DungeonMapMesh.FloorView MapView => view;
    public DungeonFieldTerminalMapGraphic MapGraphic => mapGraphic;
    public DungeonFieldTerminalMapGraphic PlayerGraphic => playerGraphic;
    public DungeonFieldTerminalMapGraphic LegendGraphic => legendGraphic;
    public DungeonFieldTerminalMapGraphic CurrentGraphic => currentGraphic;
    public TMP_Text CurrentLabel => currentLabel;
    /// <summary>'현재 방' 표지가 보이는지(지금 방이 영역 안).</summary>
    public bool CurrentLabelVisible => currentLabelVisible;
    public float CurrentPulsePeriod => currentPulsePeriod;
    public IDungeonPlayerHeadingSource HeadingSource => headingSource;
    /// <summary>Player 표식이 바라보는 쪽(+1/−1 — localScale.x).</summary>
    public int PlayerFacing => facing;
    public DungeonObjectivePanelGraphic ObjectiveCells => objectiveCells;
    public TMP_Text TitleText => titleText;
    public TMP_Text ContextText => contextText;
    public TMP_Text GradeText => gradeText;
    public TMP_Text TrainingText => trainingText;
    public TMP_Text ObjectiveTitleText => objectiveTitleText;
    public TMP_Text ObjectiveText => objectiveText;
    public GameObject Presentation => presentation;
    public RectTransform TerminalRoot => terminalRoot;
    /// <summary>7차: 지금 배율 단계(0 전체 · 1 중간 · 2 확대)와 지금 쓸 수 있는 단계 수(전체 ≥ 확대면 1).</summary>
    public int ZoomLevel => zoomLevel;
    public int ZoomLevelCount => zoomCount;
    /// <summary>7차: '전체' 단계 pitch와 '확대' 단계 pitch(마지막 화면 맞춤 기준).</summary>
    public float FullPitchValue => fullPitch;
    public float ZoomedPitchValue => zoomedPitch;
    public int DefaultZoom => defaultZoom;
    public float PanStepPixels => panStepPixels;
    public float WheelCooldown => wheelCooldown;
    /// <summary>7차: 지도를 끌고 있는지.</summary>
    public bool IsDragging => dragging;
    /// <summary>7차: 마우스 입력을 받는지(열려 있고 닫는 중이 아니며 최상단 입력 수신자).</summary>
    public bool AcceptsPointer => isOpen && !isClosing && IsTopInput;
    /// <summary>7차: 레이 판정이 켜졌는지(열림 연출이 끝나고 닫는 중이 아닐 때만 — DeviceRoot·ScreenClip CanvasGroup.blocksRaycasts).</summary>
    public bool RaycastsEnabled => terminalGroup != null && terminalGroup.blocksRaycasts && (screenGroup == null || screenGroup.blocksRaycasts);
    public int ControlHover => controlHover;
    public DungeonFieldTerminalMapGraphic ControlsGraphic => controlsGraphic;
    public DungeonTerminalStatusPanel StatusPanel => statusPanel;
    public IWaveTerminalSource WaveSource => waveSource;
    public bool IsWaveMode => !FloorActive() && WaveActive();
    public TerminalState State => state;
    public ActionChoice SelectedAction => selectedAction;
    public ConfirmTarget PendingConfirm => confirmTarget;
    public bool ConfirmSelectedYes => confirmYes;
    public bool IsConfirmOpen => state == TerminalState.ConfirmPopup;
    public bool MapButtonHover => mapButtonHover;
    public bool RestartHover => restartHover;
    public bool GiveUpHover => giveUpHover;
    public bool CanRestart => isOpen && !isClosing && (FloorActive() || WaveActive())
        && (GameManager.Instance == null || !GameManager.Instance.IsRetrying);
    public bool CanGiveUp => isOpen && !isClosing && (FloorActive() || WaveActive())
        && (GameManager.Instance == null || !GameManager.Instance.IsRetrying)
        && (RoomTransitionManager.Instance == null || !RoomTransitionManager.Instance.IsTransitioning);
    public DungeonTerminalGaugeGraphic MapActiveFrame => mapActiveFrame;
    public DungeonTerminalGaugeGraphic MapButtonGraphic => mapButtonGraphic;
    public TMP_Text MapButtonLabel => mapButtonLabel;
    public DungeonTerminalGaugeGraphic RestartButtonGraphic => restartButtonGraphic;
    public TMP_Text RestartButtonLabel => restartButtonLabel;
    public DungeonTerminalGaugeGraphic GiveUpButtonGraphic => giveUpButtonGraphic;
    public TMP_Text GiveUpButtonLabel => giveUpButtonLabel;
    public GameObject ConfirmPopupRoot => confirmPopupRoot;
    public DungeonTerminalGaugeGraphic ConfirmFrameGraphic => confirmFrameGraphic;
    public TMP_Text ConfirmTitleText => confirmTitleText;
    public TMP_Text ConfirmBodyText => confirmBodyText;
    public DungeonTerminalGaugeGraphic ConfirmYesGraphic => confirmYesGraphic;
    public TMP_Text ConfirmYesLabel => confirmYesLabel;
    public DungeonTerminalGaugeGraphic ConfirmNoGraphic => confirmNoGraphic;
    public TMP_Text ConfirmNoLabel => confirmNoLabel;
    public FeedbackDataSO OpenFeedback => openFeedback;
    public FeedbackDataSO MoveFeedback => moveFeedback;
    public FeedbackDataSO ConfirmFeedback => confirmFeedback;
    public FeedbackDataSO CancelFeedback => cancelFeedback;
    public FeedbackDataSO LastUiFeedback => lastUiFeedback;

    private void Awake() => EnsureInit();

    private void EnsureInit()
    {
        if (initialized) return;
        initialized = true;
        if (terminalRoot != null)
        {
            terminalHome = terminalRoot.anchoredPosition;
            terminalRotation = terminalRoot.localRotation;
        }
        if (wristRoot != null)
        {
            wristHome = wristRoot.anchoredPosition;
            wristRotation = wristRoot.localRotation;
        }
        if (mapPanelRoot == null && mapGraphic != null) mapPanelRoot = mapGraphic.transform.parent as RectTransform;
        if (infoPanelRoot == null && statusPanel != null) infoPanelRoot = statusPanel.transform as RectTransform;
        if (infoPanelRoot != null) infoHome = infoPanelRoot.anchoredPosition;
        if (mapButtonRoot == null && mapButtonGraphic != null) mapButtonRoot = mapButtonGraphic.rectTransform;
        if (restartButtonRoot == null && restartButtonGraphic != null) restartButtonRoot = restartButtonGraphic.rectTransform;
        if (giveUpButtonRoot == null && giveUpButtonGraphic != null) giveUpButtonRoot = giveUpButtonGraphic.rectTransform;
        if (mapButtonLabel != null) mapButtonLabel.text = DungeonUiText.Static(mapLabel);
        if (restartButtonLabel != null) restartButtonLabel.text = DungeonUiText.Static(restartLabel);
        if (giveUpButtonLabel != null) giveUpButtonLabel.text = DungeonUiText.Static(giveUpLabel);
        if (confirmYesLabel != null) confirmYesLabel.text = DungeonUiText.Static(confirmYesText);
        if (confirmNoLabel != null) confirmNoLabel.text = DungeonUiText.Static(confirmNoText);
        if (confirmPopupRoot != null) confirmPopupRoot.SetActive(false);
        if (mapActiveFrame != null) mapActiveFrame.canvasRenderer.SetAlpha(0f);
        toggleAction = new InputAction("DungeonFieldTerminal", InputActionType.Button, InputBindingDisplay.InventoryPath);
        toggleAction.performed += HandleToggle;
        if (titleText != null) titleText.text = DungeonUiText.Static(title);
        if (footerText != null) footerText.text = DungeonUiText.Static(menuFooter);
        if (objectiveCells != null) objectiveCells.AlignRight = false;
        if (mapGraphic != null) mapGraphic.ClearFloor();
        if (currentGraphic != null) currentGraphic.SetCurrent(default, false);
        if (currentLabel != null)
        {
            currentLabel.richText = true;
            currentLabel.text = DungeonUiText.Static(currentLabelText);
            currentLabel.enabled = false;
        }
        ApplyPresentation(0f);
        ApplyRaycast();
        if (controlsGraphic != null) controlsGraphic.SetControls(0, 1, -1, false, false);
        if (presentation != null) presentation.SetActive(false);
    }

    // 비활성·파괴 중에는 자식 활성 상태를 바꾸지 않는다(부모 비활성 도중 SetActive 금지) — 입력·일시정지만 돌려준다.
    private void OnDisable()
    {
        if (!initialized) return;
        CloseImmediately(false);
        SetActionEnabled(false);
    }

    private void OnDestroy()
    {
        if (initialized)
        {
            CloseImmediately(false);
            SetActionEnabled(false);
            if (toggleAction != null)
            {
                toggleAction.performed -= HandleToggle;
                toggleAction.Dispose();
                toggleAction = null;
            }
        }
        Unbind();
    }

    // ===== 연결(IDungeonMapView·IDungeonDetailView·IDungeonObjectiveView) =====

    public void Bind(IDungeonMapSource mapSource)
    {
        EnsureInit();
        if (!ReferenceEquals(source, mapSource))
        {
            if (source != null) source.MapChanged -= MarkDirty;
            source = mapSource;
            if (source != null) source.MapChanged += MarkDirty;
        }
        if (mapSource is IDungeonDetailSource details) BindDetail(details);
        if (mapSource is IDungeonObjectiveSource objectives) BindObjectives(objectives);
        if (mapSource is IDungeonThreatSource threats) BindThreat(threats);
        if (mapSource is IDungeonExitSource exits) BindExit(exits);
        if (mapSource is IDungeonPlayerHeadingSource heading) headingSource = heading;
        MarkDirty();
    }

    void IDungeonDetailView.Bind(IDungeonDetailSource details) => BindDetail(details);
    void IDungeonObjectiveView.Bind(IDungeonObjectiveSource objectives) => BindObjectives(objectives);
    void IDungeonThreatView.Bind(IDungeonThreatSource threats) => BindThreat(threats);
    void IDungeonThreatView.Unbind() => BindThreat(null);

    /// <summary>난이도 원본을 연결한다(4차 — 훈련 상황 첫 줄).</summary>
    public void BindThreat(IDungeonThreatSource threats)
    {
        EnsureInit();
        if (ReferenceEquals(threatSource, threats)) return;
        if (threatSource != null) threatSource.ThreatChanged -= OnThreatChanged;
        threatSource = threats;
        if (threatSource != null) threatSource.ThreatChanged += OnThreatChanged;
        MarkDirty();
    }

    /// <summary>출구 원본을 연결한다(4차 — 목표 줄의 출구 상태·큰 지도 출구 표식).</summary>
    public void BindExit(IDungeonExitSource exits)
    {
        EnsureInit();
        if (ReferenceEquals(exitSource, exits)) return;
        if (exitSource != null) exitSource.ExitChanged -= MarkDirty;
        exitSource = exits;
        if (exitSource != null) exitSource.ExitChanged += MarkDirty;
        MarkDirty();
    }

    private void OnThreatChanged(DungeonThreatLevel previous, DungeonThreatLevel next) => MarkDirty();

    public void BindDetail(IDungeonDetailSource details)
    {
        EnsureInit();
        if (ReferenceEquals(detailSource, details)) return;
        if (detailSource != null) detailSource.DetailChanged -= MarkDirty;
        detailSource = details;
        if (detailSource != null) detailSource.DetailChanged += MarkDirty;
        if (details is IDungeonPlayerHeadingSource heading) headingSource = heading;
        silhouettes.Clear();
        MarkDirty();
    }

    public void BindObjectives(IDungeonObjectiveSource objectives)
    {
        EnsureInit();
        if (ReferenceEquals(objectiveSource, objectives)) return;
        if (objectiveSource != null) objectiveSource.ObjectiveChanged -= MarkDirty;
        objectiveSource = objectives;
        if (objectiveSource != null) objectiveSource.ObjectiveChanged += MarkDirty;
        MarkDirty();
    }

    /// <summary>일반 Wave 원본을 연결한다(던전이 아닌 훈련 방에서도 단말기 열기·재시작 지원).</summary>
    public void BindWave(IWaveTerminalSource waves)
    {
        EnsureInit();
        if (ReferenceEquals(waveSource, waves)) return;
        if (waveSource != null) waveSource.WaveStateChanged -= MarkDirty;
        waveSource = waves;
        if (waveSource != null) waveSource.WaveStateChanged += MarkDirty;
        MarkDirty();
    }

    public void UnbindWave()
    {
        if (initialized && isOpen && !FloorActive()) CloseImmediately(isActiveAndEnabled);
        if (waveSource != null) waveSource.WaveStateChanged -= MarkDirty;
        waveSource = null;
        if (initialized && !FloorActive()) SetActionEnabled(false);
    }

    /// <summary>모든 원본 연결을 끊는다(View 인터페이스 공통 — 4차 난이도·출구·일반 Wave 포함). 열려 있으면 바로 닫는다.</summary>
    public void Unbind()
    {
        if (initialized && isOpen) CloseImmediately(isActiveAndEnabled);
        if (source != null) source.MapChanged -= MarkDirty;
        if (detailSource != null) detailSource.DetailChanged -= MarkDirty;
        if (objectiveSource != null) objectiveSource.ObjectiveChanged -= MarkDirty;
        if (threatSource != null) threatSource.ThreatChanged -= OnThreatChanged;
        if (exitSource != null) exitSource.ExitChanged -= MarkDirty;
        if (waveSource != null) waveSource.WaveStateChanged -= MarkDirty;
        source = null;
        detailSource = null;
        objectiveSource = null;
        threatSource = null;
        exitSource = null;
        headingSource = null;
        waveSource = null;
        silhouettes.Clear();
        model = null;
        if (initialized)
        {
            if (mapGraphic != null) mapGraphic.ClearFloor();
            ClearCurrent();
            SetActionEnabled(false);
        }
    }

    private void MarkDirty() => dirty = true;

    // ===== 입력 =====

    private void HandleToggle(InputAction.CallbackContext context)
    {
        // Alt+Tab(창 전환)의 Tab은 단말기 키로 치지 않는다(기존 단말기와 같다).
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed)) return;
        if (isOpen)
        {
            if (!isClosing && IsTopInput) Close();
        }
        else TryOpen();
    }

    private bool IsTopInput => UIManager.Instance == null || UIManager.Instance.IsCurrentInputTarget(this);

    private void SetActionEnabled(bool on)
    {
        if (toggleAction == null || actionEnabled == on) return;
        actionEnabled = on;
        if (on) toggleAction.Enable();
        else toggleAction.Disable();
    }

    public void OnMove(Vector2 dir)
    {
        if (!isOpen || isClosing || dir.sqrMagnitude < .01f) return;
        var step = new Vector2(Mathf.Abs(dir.x) > .5f ? Mathf.Sign(dir.x) : 0f, Mathf.Abs(dir.y) > .5f ? Mathf.Sign(dir.y) : 0f);
        if (step == Vector2.zero) return;

        if (state == TerminalState.ConfirmPopup)
        {
            if (step.x < 0f) SetConfirmSelection(true);
            else if (step.x > 0f) SetConfirmSelection(false);
            return;
        }
        if (state == TerminalState.Menu || IsWaveMode)
        {
            if (IsWaveMode)
            {
                if (step.x < 0f) SelectAction(ActionChoice.Restart);
                else if (step.x > 0f) SelectAction(ActionChoice.GiveUp);
                else if (step.y != 0f) SelectAction(selectedAction == ActionChoice.Restart ? ActionChoice.GiveUp : ActionChoice.Restart);
            }
            else
            {
                if (step.x < 0f) SelectAction((ActionChoice)Mathf.Max((int)ActionChoice.Map, (int)selectedAction - 1));
                else if (step.x > 0f) SelectAction((ActionChoice)Mathf.Min((int)ActionChoice.GiveUp, (int)selectedAction + 1));
                else if (step.y < 0f && selectedAction == ActionChoice.Map) SelectAction(ActionChoice.Restart);
                else if (step.y > 0f && selectedAction != ActionChoice.Map) SelectAction(ActionChoice.Map);
            }
            return;
        }
        // 상세 맵 탐색 모드: 화면 px 기준 한 걸음(확대해도 한 번에 수백 px씩 튀지 않게)
        PanBy(step * (panStepPixels / Mathf.Max(1f, view.pitch)));
    }

    /// <summary>지도를 단위 칸 기준으로 옮긴다(방향키·검증). 화면 맞춤만 다시 한다(모델 재생성 없음 — 할당 없음).</summary>
    public void PanBy(Vector2 delta)
    {
        pan += delta;
        RefitView();
    }

    public void OnConfirm()
    {
        if (!isOpen || isClosing) return;
        if (state == TerminalState.ConfirmPopup)
        {
            if (confirmYes) ExecuteConfirmTarget();
            else CancelConfirmPopup();
            return;
        }
        if (state == TerminalState.Menu || IsWaveMode)
        {
            switch (selectedAction)
            {
                case ActionChoice.Map:
                    if (!IsWaveMode) EnterMapDetail();
                    break;
                case ActionChoice.Restart:
                    RequestRestartConfirm();
                    break;
                case ActionChoice.GiveUp:
                    RequestGiveUpConfirm();
                    break;
            }
            return;
        }
        Recenter();
        PlayUiFeedback(confirmFeedback);
    }

    private void PlayUiFeedback(FeedbackDataSO feedback)
    {
        if (!live || feedback == null) return;
        lastUiFeedback = feedback;
        if (FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.PlayExact(feedback, Vector3.zero, Quaternion.identity);
    }

    /// <summary>상세 맵 탐색 깊이로 진입한다(던전 모드 전용).</summary>
    public bool EnterMapDetail() => EnterMapDetail(true);

    private bool EnterMapDetail(bool playFeedback)
    {
        if (!isOpen || isClosing || IsWaveMode || state == TerminalState.ConfirmPopup) return false;
        if (state == TerminalState.MapDetail) return true;
        state = TerminalState.MapDetail;
        selectedAction = ActionChoice.Map;
        UpdateActionButtons();
        UpdateControls();
        UpdateFooterText();
        if (playFeedback) PlayUiFeedback(confirmFeedback);
        return true;
    }

    /// <summary>상세 맵 탐색에서 취소해 3버튼 메뉴로 복귀한다(단말기는 닫지 않는다).</summary>
    public bool ExitMapDetail()
    {
        if (!isOpen || isClosing || state != TerminalState.MapDetail) return false;
        EndPointerDrag();
        state = TerminalState.Menu;
        selectedAction = ActionChoice.Map;
        UpdateActionButtons();
        UpdateControls();
        UpdateFooterText();
        PlayUiFeedback(cancelFeedback);
        return true;
    }

    /// <summary>하단 동작 버튼 키보드 선택 항목을 바꾼다.</summary>
    public void SelectAction(ActionChoice choice)
    {
        if (IsWaveMode && choice == ActionChoice.Map) choice = ActionChoice.Restart;
        if (selectedAction == choice) return;
        selectedAction = choice;
        UpdateActionButtons();
        PlayUiFeedback(moveFeedback);
    }

    /// <summary>확인 팝업에서 예(true) / 아니오(false)를 선택한다.</summary>
    public void SetConfirmSelection(bool yes)
    {
        if (state != TerminalState.ConfirmPopup || confirmYes == yes) return;
        confirmYes = yes;
        UpdateConfirmPopup();
        PlayUiFeedback(moveFeedback);
    }

    /// <summary>재시작 확인 팝업을 연다(기본 선택 = 아니오).</summary>
    public bool RequestRestartConfirm()
    {
        if (!CanRestart) return false;
        selectedAction = ActionChoice.Restart;
        return OpenConfirmPopup(ConfirmTarget.Restart);
    }

    /// <summary>포기 확인 팝업을 연다(기본 선택 = 아니오).</summary>
    public bool RequestGiveUpConfirm()
    {
        if (!CanGiveUp) return false;
        selectedAction = ActionChoice.GiveUp;
        return OpenConfirmPopup(ConfirmTarget.GiveUp);
    }

    private bool OpenConfirmPopup(ConfirmTarget target)
    {
        if (!isOpen || isClosing || target == ConfirmTarget.None) return false;
        EndPointerDrag();
        state = TerminalState.ConfirmPopup;
        confirmTarget = target;
        confirmYes = false;   // 실수 방지를 위해 최초 포커스는 항상 '아니오'
        confirmYesHover = false;
        confirmNoHover = false;
        if (confirmPopupRoot != null && !confirmPopupRoot.activeSelf) confirmPopupRoot.SetActive(true);
        UpdateConfirmPopup();
        UpdateActionButtons();
        UpdateControls();
        UpdateFooterText();
        PlayUiFeedback(confirmFeedback);
        return true;
    }

    /// <summary>확인 팝업을 취소하고 메뉴 상태로 복귀한다.</summary>
    public bool CancelConfirmPopup() => CancelConfirmPopup(true);

    private bool CancelConfirmPopup(bool playFeedback)
    {
        if (state != TerminalState.ConfirmPopup) return false;
        state = TerminalState.Menu;
        confirmTarget = ConfirmTarget.None;
        confirmYes = false;
        confirmYesHover = false;
        confirmNoHover = false;
        if (confirmPopupRoot != null && confirmPopupRoot.activeSelf) confirmPopupRoot.SetActive(false);
        UpdateActionButtons();
        UpdateControls();
        UpdateFooterText();
        if (playFeedback) PlayUiFeedback(cancelFeedback);
        return true;
    }

    /// <summary>확인 팝업에서 '예'를 확정해 재시작 또는 포기를 실행한다.</summary>
    public bool PressConfirmYes()
    {
        if (!AcceptsPointer || state != TerminalState.ConfirmPopup) return false;
        return ExecuteConfirmTarget();
    }

    /// <summary>확인 팝업에서 '아니오'를 눌러 메뉴로 돌아간다.</summary>
    public bool PressConfirmNo()
    {
        if (!AcceptsPointer || state != TerminalState.ConfirmPopup) return false;
        return CancelConfirmPopup();
    }

    private bool ExecuteConfirmTarget()
    {
        var target = confirmTarget;
        if (target == ConfirmTarget.Restart)
        {
            if (!CanRestart) { CancelConfirmPopup(); return false; }
            PlayUiFeedback(confirmFeedback);
            CancelConfirmPopup(false);
            if (RestartHandler != null) { RestartHandler(); return true; }
            var gm = GameManager.Instance;
            if (gm == null) return false;
            CloseImmediately();
            gm.RequestRetryFromTerminal();
            return true;
        }
        if (target == ConfirmTarget.GiveUp)
        {
            if (!CanGiveUp) { CancelConfirmPopup(); return false; }
            PlayUiFeedback(confirmFeedback);
            CancelConfirmPopup(false);
            if (GiveUpHandler != null) { GiveUpHandler(); return true; }
            var gm = GameManager.Instance;
            if (gm == null) return false;
            CloseImmediately();
            gm.RequestReturnToLobbyFromTerminal();
            return true;
        }
        return false;
    }

    /// <summary>키보드로 미니맵 배율을 조절한다(delta &gt; 0 확대, delta &lt; 0 축소 — 상세 맵 모드 [Q/E]·[-/+]).</summary>
    public bool HandleZoomKey(int delta)
    {
        if (!isOpen || isClosing || IsWaveMode || state != TerminalState.MapDetail || delta == 0) return false;
        bool changed = delta > 0 ? ZoomIn() : ZoomOut();
        if (changed) PlayUiFeedback(moveFeedback);
        return changed;
    }

    /// <summary>7차: 지금 방 가운데로(배율은 그대로). Z 키·[현재 방] 버튼.</summary>
    public void Recenter()
    {
        if (!isOpen || isClosing) return;
        pan = Vector2.zero;
        RefitView();
    }

    /// <summary>7차: 배율 단계를 바꾼다(0 전체 · 1 중간 · 2 확대 — 쓸 수 있는 단계로 자른다). 보이던 가운데를 유지한다. 바뀌었으면 true.</summary>
    public bool SetZoom(int level)
    {
        EnsureInit();
        if (model == null || mapGraphic == null)
        {
            zoomLevel = Mathf.Clamp(level, 0, Mathf.Clamp(zoomLevels, 1, 3) - 1);
            return false;
        }
        ComputeZoom();
        level = Mathf.Clamp(level, 0, zoomCount - 1);
        if (level == zoomLevel)
        {
            UpdateControls();
            return false;
        }
        var keep = view.center;
        zoomLevel = level;
        pan = keep - DungeonMapMesh.FitFloor(model, CurrentRoom, MapArea, mapPadding, minPitch, maxPitch, Vector2.zero, false, PitchForLevel(level)).center;
        RefitView();
        return true;
    }

    /// <summary>7차: 한 단계 확대·축소([+]·[−] 버튼·휠). 열려 있고 닫는 중이 아닐 때만.</summary>
    public bool ZoomIn() => isOpen && !isClosing && SetZoom(zoomLevel + 1);
    public bool ZoomOut() => isOpen && !isClosing && SetZoom(zoomLevel - 1);

    /// <summary>7차: 단계 level의 FitFloor zoomPitch(0 = 전체 맞춤). 마지막 화면 맞춤의 pitch 기준.</summary>
    public float PitchForLevel(int level)
    {
        level = Mathf.Clamp(level, 0, Mathf.Max(1, zoomCount) - 1);
        if (zoomCount <= 1 || level == 0) return 0f;
        if (level >= zoomCount - 1) return zoomedPitch;
        return Mathf.Sqrt(fullPitch * zoomedPitch);   // 중간 = 전체·확대의 기하 평균
    }

    // 지금 모델·지금 방·영역으로 '전체'·'확대' pitch와 쓸 수 있는 단계 수를 정한다(할당 없음).
    private void ComputeZoom()
    {
        int levels = Mathf.Clamp(zoomLevels, 1, 3);
        if (model == null)
        {
            zoomCount = 1;
            fullPitch = zoomedPitch = maxPitch;
            zoomLevel = Mathf.Clamp(zoomLevel, 0, levels - 1);
            return;
        }
        var area = MapArea;
        fullPitch = DungeonMapMesh.FullPitch(model, area, mapPadding, minPitch, maxPitch);
        zoomedPitch = DungeonMapMesh.ZoomPitch(model, CurrentRoom, area, mapPadding, zoomRoomShare, maxZoomPitch, fullPitch);
        zoomCount = zoomedPitch > fullPitch * 1.02f ? levels : 1;
        zoomLevel = Mathf.Clamp(zoomLevel, 0, zoomCount - 1);
    }

    // ===== 7차 마우스(DungeonFieldTerminalPointer가 부른다 — 검증은 직접 부른다) =====

    /// <summary>지도를 화면 px(지도 영역 로컬)만큼 끈다 — 지도가 포인터를 따라 움직인다.</summary>
    public void PointerPan(Vector2 localDelta)
    {
        if (!AcceptsPointer || model == null) return;
        pan -= localDelta / Mathf.Max(1f, view.pitch);
        RefitView();
    }

    /// <summary>휠 한 칸 = 한 단계(위 = 확대). wheelCooldown 안의 다음 칸은 무시한다. 바뀌었으면 true.</summary>
    public bool PointerScroll(float delta)
    {
        if (!AcceptsPointer || state == TerminalState.ConfirmPopup || IsWaveMode || Mathf.Abs(delta) < 1e-3f) return false;
        if (state == TerminalState.Menu) EnterMapDetail(false);
        if (clock - lastWheel < wheelCooldown) return false;
        lastWheel = clock;
        bool changed = delta > 0f ? SetZoom(zoomLevel + 1) : SetZoom(zoomLevel - 1);
        if (changed) PlayUiFeedback(moveFeedback);
        return changed;
    }

    /// <summary>끌기 시작(지도 영역 로컬 점).</summary>
    public void BeginPointerDrag(Vector2 local)
    {
        if (!AcceptsPointer || state == TerminalState.ConfirmPopup || IsWaveMode) return;
        if (state == TerminalState.Menu) EnterMapDetail(false);
        dragging = true;
        dragLast = local;
    }

    /// <summary>끌기 중 새 점(지도 영역 로컬) — 앞 점과의 차이만큼 옮긴다.</summary>
    public void PointerDragTo(Vector2 local)
    {
        if (!dragging) return;
        if (!AcceptsPointer || state == TerminalState.ConfirmPopup)
        {
            EndPointerDrag();
            return;
        }
        var delta = local - dragLast;
        dragLast = local;
        if (delta.sqrMagnitude > 1e-6f) PointerPan(delta);
    }

    /// <summary>끌기 끝(닫힘·비활성 때도 부른다).</summary>
    public void EndPointerDrag() => dragging = false;

    /// <summary>버튼 클릭(DungeonFieldTerminalMapGraphic.ControlZoomOut·ControlZoomIn·ControlRecenter). 받았으면 true.</summary>
    public bool PressControl(int index)
    {
        if (!AcceptsPointer || state == TerminalState.ConfirmPopup || IsWaveMode) return false;
        if (state == TerminalState.Menu) EnterMapDetail(false);
        switch (index)
        {
            case DungeonFieldTerminalMapGraphic.ControlZoomOut:
                if (ZoomOut()) PlayUiFeedback(confirmFeedback);
                return true;
            case DungeonFieldTerminalMapGraphic.ControlZoomIn:
                if (ZoomIn()) PlayUiFeedback(confirmFeedback);
                return true;
            case DungeonFieldTerminalMapGraphic.ControlRecenter:
                Recenter();
                PlayUiFeedback(confirmFeedback);
                return true;
            default: return false;
        }
    }

    /// <summary>버튼에 마우스가 올라감·나감(밝게 그리기 + 이동 UI 사운드).</summary>
    public void SetControlHover(int index, bool on)
    {
        bool entered = on && controlHover != index && AcceptsPointer && state != TerminalState.ConfirmPopup && !IsWaveMode;
        if (on) controlHover = index;
        else if (controlHover == index) controlHover = -1;
        UpdateControls();
        if (entered) PlayUiFeedback(moveFeedback);
    }

    /// <summary>상세 맵 버튼에 마우스가 올라감·나감.</summary>
    public void SetMapButtonHover(bool on)
    {
        if (mapButtonHover == on) return;
        mapButtonHover = on;
        UpdateActionButtons();
        if (on && AcceptsPointer && state == TerminalState.Menu && !IsWaveMode) PlayUiFeedback(moveFeedback);
    }

    /// <summary>재시작 버튼에 마우스가 올라감·나감(밝게 그리기 + 이동 UI 사운드).</summary>
    public void SetRestartHover(bool on)
    {
        if (restartHover == on) return;
        restartHover = on;
        UpdateActionButtons();
        if (on && AcceptsPointer && CanRestart && state != TerminalState.ConfirmPopup) PlayUiFeedback(moveFeedback);
    }

    /// <summary>포기 버튼에 마우스가 올라감·나감(밝게 그리기 + 이동 UI 사운드).</summary>
    public void SetGiveUpHover(bool on)
    {
        if (giveUpHover == on) return;
        giveUpHover = on;
        UpdateActionButtons();
        if (on && AcceptsPointer && CanGiveUp && state != TerminalState.ConfirmPopup) PlayUiFeedback(moveFeedback);
    }

    /// <summary>확인 팝업 예/아니오 버튼 hover.</summary>
    public void SetConfirmHover(bool yes, bool on)
    {
        if (yes)
        {
            if (confirmYesHover == on) return;
            confirmYesHover = on;
        }
        else
        {
            if (confirmNoHover == on) return;
            confirmNoHover = on;
        }
        UpdateConfirmPopup();
        if (on && AcceptsPointer && state == TerminalState.ConfirmPopup) PlayUiFeedback(moveFeedback);
    }

    /// <summary>상세 맵 버튼 클릭.</summary>
    public bool PressMapButton()
    {
        if (!AcceptsPointer || IsWaveMode || state == TerminalState.ConfirmPopup) return false;
        return EnterMapDetail();
    }

    /// <summary>재시작 버튼 클릭 → 확인 팝업을 띄운다(실제 실행은 팝업에서 '예' 선택 시).</summary>
    public bool PressRestart()
    {
        if (!AcceptsPointer || state == TerminalState.ConfirmPopup || !CanRestart) return false;
        return RequestRestartConfirm();
    }

    /// <summary>포기 버튼 클릭 → 확인 팝업을 띄운다(실제 실행은 팝업에서 '예' 선택 시).</summary>
    public bool PressGiveUp()
    {
        if (!AcceptsPointer || state == TerminalState.ConfirmPopup || !CanGiveUp) return false;
        return RequestGiveUpConfirm();
    }

    private void UpdateActionButtons()
    {
        bool menuFocused = isOpen && !isClosing && state == TerminalState.Menu;

        bool mapReady = isOpen && !isClosing && !IsWaveMode;
        bool mapHovered = mapReady && mapButtonHover && state != TerminalState.ConfirmPopup;
        bool mapSelected = mapReady && menuFocused && selectedAction == ActionChoice.Map;
        bool mapDetail = mapReady && state == TerminalState.MapDetail;
        bool mapFocused = mapSelected || mapDetail;
        if (mapActiveFrame != null)
        {
            int frameMode = mapDetail
                ? DungeonTerminalGaugeGraphic.MapFrameDetail
                : (mapSelected || (menuFocused && mapHovered) ? DungeonTerminalGaugeGraphic.MapFrameSelected : DungeonTerminalGaugeGraphic.MapFrameHidden);
            mapActiveFrame.SetMapFrameMode(frameMode);
            if (frameMode == DungeonTerminalGaugeGraphic.MapFrameHidden)
                mapActiveFrame.canvasRenderer.SetAlpha(0f);
        }
        if (mapButtonGraphic != null)
            mapButtonGraphic.SetButtonStyle(mapReady, mapHovered, mapFocused, DungeonTerminalGaugeGraphic.ToneCyan, DungeonTerminalGaugeGraphic.IconMap);
        if (mapButtonLabel != null)
        {
            string tag = mapDetail ? (mapLabel + " 이동") : (mapSelected || mapHovered ? (mapLabel + " [Z]") : mapLabel);
            mapButtonLabel.text = DungeonUiText.Static(tag);
            mapButtonLabel.color = mapReady ? (mapFocused ? FocusWhite : (mapHovered ? Bright : Dim)) : Dim;
        }

        bool restartReady = CanRestart;
        bool restartHovered = restartReady && restartHover && state != TerminalState.ConfirmPopup;
        bool restartFocused = restartReady && menuFocused && selectedAction == ActionChoice.Restart;
        if (restartButtonGraphic != null)
            restartButtonGraphic.SetButtonStyle(restartReady, restartHovered, restartFocused, DungeonTerminalGaugeGraphic.ToneAmber, DungeonTerminalGaugeGraphic.IconRestart);
        if (restartButtonLabel != null)
            restartButtonLabel.color = restartReady ? (restartFocused ? FocusWhite : (restartHovered ? RestartBright : RestartMuted)) : Dim;

        bool giveUpReady = CanGiveUp;
        bool giveUpHovered = giveUpReady && giveUpHover && state != TerminalState.ConfirmPopup;
        bool giveUpFocused = giveUpReady && menuFocused && selectedAction == ActionChoice.GiveUp;
        if (giveUpButtonGraphic != null)
            giveUpButtonGraphic.SetButtonStyle(giveUpReady, giveUpHovered, giveUpFocused, DungeonTerminalGaugeGraphic.ToneDanger, DungeonTerminalGaugeGraphic.IconGiveUp);
        if (giveUpButtonLabel != null)
            giveUpButtonLabel.color = giveUpReady ? (giveUpFocused ? FocusWhite : (giveUpHovered ? GiveUpBright : GiveUpMuted)) : Dim;
    }

    private void UpdateConfirmPopup()
    {
        bool open = state == TerminalState.ConfirmPopup && isOpen && !isClosing;
        bool danger = confirmTarget == ConfirmTarget.GiveUp;
        if (confirmFrameGraphic != null) confirmFrameGraphic.SetPopupDanger(danger);
        if (confirmTitleText != null)
        {
            confirmTitleText.text = DungeonUiText.Static(danger ? giveUpLabel : restartLabel);
            confirmTitleText.color = danger ? GiveUpBright : RestartBright;
        }
        if (confirmBodyText != null)
            confirmBodyText.text = DungeonUiText.Static(danger ? giveUpConfirmBody : restartConfirmBody);
        int yesTone = danger ? DungeonTerminalGaugeGraphic.ToneDanger : DungeonTerminalGaugeGraphic.ToneAmber;
        bool yesHot = open && (confirmYes || confirmYesHover);
        bool noHot = open && (!confirmYes || confirmNoHover);
        if (confirmYesGraphic != null)
            confirmYesGraphic.SetButtonStyle(open, open && confirmYesHover, open && confirmYes, yesTone, DungeonTerminalGaugeGraphic.IconCheck);
        if (confirmYesLabel != null)
            confirmYesLabel.color = open && confirmYes ? FocusWhite : (danger ? (yesHot ? GiveUpBright : GiveUpMuted) : (yesHot ? RestartBright : RestartMuted));
        if (confirmNoGraphic != null)
            confirmNoGraphic.SetButtonStyle(open, open && confirmNoHover, open && !confirmYes, DungeonTerminalGaugeGraphic.ToneCyan, DungeonTerminalGaugeGraphic.IconCancel);
        if (confirmNoLabel != null)
            confirmNoLabel.color = open && !confirmYes ? FocusWhite : (noHot ? Bright : Dim);
    }

    private void UpdateFooterText()
    {
        if (footerText == null) return;
        string raw = state switch
        {
            TerminalState.ConfirmPopup => confirmFooter,
            TerminalState.MapDetail when !IsWaveMode => footer,
            _ => IsWaveMode ? waveFooter : menuFooter,
        };
        footerText.text = DungeonUiText.Static(raw);
    }

    private void UpdateControls()
    {
        if (controlsGraphic == null) return;
        bool ready = isOpen && !isClosing && model != null && state != TerminalState.ConfirmPopup;
        controlsGraphic.SetControls(zoomLevel, zoomCount, ready ? controlHover : -1, ready && zoomLevel < zoomCount - 1, ready && zoomLevel > 0);
    }

    // 열림 연출이 끝나고 닫는 중이 아닐 때만 레이 판정을 켠다(Prefab 값은 false).
    private void ApplyRaycast()
    {
        bool on = isOpen && !isClosing && !transitioning;
        if (terminalGroup != null && terminalGroup.blocksRaycasts != on) terminalGroup.blocksRaycasts = on;
        if (screenGroup != null && screenGroup.blocksRaycasts != on) screenGroup.blocksRaycasts = on;
    }

    public void OnCancel()
    {
        if (!isOpen || isClosing) return;
        if (state == TerminalState.ConfirmPopup) { CancelConfirmPopup(); return; }
        if (state == TerminalState.MapDetail) { ExitMapDetail(); return; }
        Close();
    }

    // ===== 열기·닫기 =====

    /// <summary>지금 열 수 있는지와 그 이유(검증용).</summary>
    public bool CanOpen(out string reason)
    {
        EnsureInit();
        reason = null;
        bool floor = FloorActive();
        bool wave = !floor && WaveActive();
        if (isOpen || transitioning) reason = "이미 열림";
        else if (!floor && !wave) reason = waveSource != null && source == null ? "Wave 진행 중이 아님" : "던전 진행 중이 아님";
        else if (floor && source is IDungeonCombatSource combat && combat.IsHalted) reason = "Wave 진행 정지";
        else if (wave && waveSource.IsHalted) reason = "Wave 진행 정지";
        else if (DungeonHudGate.IsBlockingScreenOpen()) reason = "패배 화면·결과 보드";
        else if (!isActiveAndEnabled) reason = "비활성";
        else
        {
            var ui = UIManager.Instance;
            var time = GameTimeController.Instance;
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (ui == null) reason = "UIManager 없음";
            else if (ui.HasActiveInputUI || ui.IsUIInputBlocked || ui.BlocksGameplayInput) reason = "다른 입력 UI·입력 차단";
            else if (time == null || time.IsPaused) reason = "일시정지 중";
            else if (player == null || !player.gameObject.activeInHierarchy || player.Health == null || player.Health.currentHP <= 0f) reason = "Player 없음·사망";
            else if (WavePlayerStart.IsHolding(player)) reason = "구속 중(관 통과·크레인)";
            else if (GameManager.Instance != null && GameManager.Instance.IsRetrying) reason = "재시작 진행 중";
        }
        return reason == null;
    }

    /// <summary>조건을 확인하고 연다(Tab). 열었으면 true.</summary>
    public bool TryOpen()
    {
        if (!CanOpen(out _)) return false;
        Open(true);
        return true;
    }

    /// <summary>
    /// 일시정지·입력 대상 등록 없이 바로 펼친 모습으로 연다(검증·편집기 미리보기 전용). 그림은 실제와 같은 방식으로 만든다.
    /// </summary>
    public void OpenPreview()
    {
        EnsureInit();
        if (isOpen) CloseImmediately();
        Open(false);
        transitioning = false;
        ApplyPresentation(1f);
        ApplyRaycast();
    }

    private void Open(bool liveOpen)
    {
        live = liveOpen;
        isOpen = true;
        isClosing = false;
        pan = Vector2.zero;
        // 7차 V4: 열 때마다 확대·지금 방 가운데로(쓸 수 없는 단계면 RefitView가 자른다), 끌기·hover 초기화
        zoomLevel = Mathf.Clamp(defaultZoom, 0, Mathf.Clamp(zoomLevels, 1, 3) - 1);
        dragging = false;
        controlHover = -1;
        mapButtonHover = false;
        restartHover = false;
        giveUpHover = false;
        confirmYesHover = false;
        confirmNoHover = false;
        state = TerminalState.Menu;
        selectedAction = IsWaveMode ? ActionChoice.Restart : ActionChoice.Map;   // 던전에서는 반드시 '상세 맵'이 최초 선택
        confirmTarget = ConfirmTarget.None;
        confirmYes = false;
        if (confirmPopupRoot != null && confirmPopupRoot.activeSelf) confirmPopupRoot.SetActive(false);
        if (presentation != null) presentation.SetActive(true);
        RebuildAll();
        if (statusPanel != null) statusPanel.PlayIntro();
        if (live)
        {
            UIManager.Instance.SetInputTarget(this, UIInputLayer.Gameplay);
            pauseOwner = GameTimeController.Instance;
            pauseOwner.PushPause();
            ownsPause = true;
            PlayUiFeedback(openFeedback);
        }
        BeginTransition(1f, OpenDuration);
    }

    public void Close()
    {
        if (!isOpen || isClosing) return;
        isClosing = true;
        PlayUiFeedback(cancelFeedback);
        EndPointerDrag();
        controlHover = -1;
        mapButtonHover = false;
        restartHover = false;
        giveUpHover = false;
        confirmYesHover = false;
        confirmNoHover = false;
        state = TerminalState.Menu;
        confirmTarget = ConfirmTarget.None;
        confirmYes = false;
        if (confirmPopupRoot != null && confirmPopupRoot.activeSelf) confirmPopupRoot.SetActive(false);
        BeginTransition(0f, CloseDuration);
        UpdateControls();
        UpdateActionButtons();
    }

    /// <summary>연출 없이 바로 닫고 입력·일시정지를 돌려준다. hide가 false면 표시 루트의 활성 상태는 그대로 둔다(비활성·파괴 도중).</summary>
    public void CloseImmediately(bool hide = true)
    {
        transitioning = false;
        if (!isOpen && !ownsPause) return;
        ApplyPresentation(0f);
        FinishClose(hide);
    }

    private void FinishClose(bool hide = true)
    {
        bool wasLive = live;
        isOpen = false;
        isClosing = false;
        live = false;
        playerVisible = false;
        // 7차 V7: 닫히면 끌기·hover·팝업을 초기화하고 레이 판정을 끈다
        dragging = false;
        controlHover = -1;
        mapButtonHover = false;
        restartHover = false;
        giveUpHover = false;
        confirmYesHover = false;
        confirmNoHover = false;
        state = TerminalState.Menu;
        selectedAction = ActionChoice.Map;
        confirmTarget = ConfirmTarget.None;
        confirmYes = false;
        if (hide && confirmPopupRoot != null && confirmPopupRoot.activeSelf) confirmPopupRoot.SetActive(false);
        if (mapActiveFrame != null) mapActiveFrame.canvasRenderer.SetAlpha(0f);
        ApplyRaycast();
        UpdateControls();
        UpdateActionButtons();
        if (wasLive && UIManager.Instance != null) UIManager.Instance.ClearInputTarget(this);
        if (ownsPause)
        {
            ownsPause = false;
            if (pauseOwner != null) pauseOwner.PopPause();
            pauseOwner = null;
        }
        if (hide && presentation != null) presentation.SetActive(false);
    }

    private void BeginTransition(float target, float duration)
    {
        transitionFrom = progress;
        transitionTo = target;
        transitionTime = 0f;
        transitionDuration = Mathf.Max(.01f, duration);
        transitioning = true;
        ApplyRaycast();
    }

    // 기존 단말기(InventoryTerminalMenu.ApplyPresentation)와 같은 연출: 오른쪽 아래에서 기울어진 채 작게 → 제자리, 손목은 아래에서 올라온다.
    private void ApplyPresentation(float value)
    {
        progress = value;
        if (terminalRoot == null || fitRect == null) return;
        lastFitSize = fitRect.rect.size;
        float scale = Mathf.Min(lastFitSize.x / Mathf.Max(1f, terminalRoot.rect.width), lastFitSize.y / Mathf.Max(1f, terminalRoot.rect.height));
        if (scale <= 0f) scale = 1f;
        terminalRoot.localScale = Vector3.one * (scale * Mathf.Lerp(.45f, 1f, value));
        terminalRoot.anchoredPosition = terminalHome + new Vector2(lastFitSize.x * .62f, -lastFitSize.y * .7f) * (1f - value);
        terminalRoot.localRotation = terminalRotation * Quaternion.Euler(0f, 0f, Mathf.Lerp(-14f, 0f, value));
        if (terminalGroup != null) terminalGroup.alpha = Mathf.Clamp01(value * 2f);
        if (screenGroup != null) screenGroup.alpha = Mathf.InverseLerp(.25f, .85f, value);
        if (backdropGroup != null) backdropGroup.alpha = value;
        if (wristRoot != null)
        {
            wristRoot.anchoredPosition = wristHome + new Vector2(20f, -45f) * (1f - value);
            wristRoot.localRotation = wristRotation * Quaternion.Euler(0f, 0f, Mathf.Lerp(-8f, 0f, value));
        }
    }

    // ===== 진행 =====

    private void Update() => Tick(Time.unscaledDeltaTime);

    /// <summary>한 프레임 진행: 던전·일반 Wave 진행 중에만 Tab 입력을 켜고, 열린 채 조건이 깨지면 바로 닫은 뒤 연출을 진행한다(Update가 부른다. 검증은 직접 부른다).</summary>
    public void Tick(float deltaTime)
    {
        EnsureInit();
        bool active = FloorActive() || WaveActive();
        SetActionEnabled(active);
        if (isOpen && live)
        {
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (!active || player == null || !player.gameObject.activeInHierarchy || player.Health == null || player.Health.currentHP <= 0f
                || DungeonHudGate.IsBlockingScreenOpen())
            {
                CloseImmediately();
                return;
            }
        }
        if (isOpen && !isClosing && IsTopInput)
            PollKeyboardShortcuts();
        Advance(deltaTime);
    }

    // 마우스 없이 키보드만으로 상세 맵 배율 조절([Q/E]·[-/+])·확인([ENTER])을 처리한다.
    private void PollKeyboardShortcuts()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (state == TerminalState.MapDetail && !IsWaveMode)
        {
            if (kb.qKey.wasPressedThisFrame || kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame)
                HandleZoomKey(-1);
            else if (kb.eKey.wasPressedThisFrame || kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame)
                HandleZoomKey(1);
        }
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            OnConfirm();
    }

    /// <summary>실제 시간 dt만큼 열기·닫기 연출과 Player 점을 진행한다(Update가 부른다. 검증은 직접 부른다). 그림이 바뀌었을 때만 다시 만든다.</summary>
    public void Advance(float deltaTime)
    {
        EnsureInit();
        deltaTime = Mathf.Max(0f, deltaTime);
        clock += deltaTime;
        if (transitioning)
        {
            transitionTime += deltaTime;
            float t = Mathf.Clamp01(transitionTime / transitionDuration);
            ApplyPresentation(Mathf.Lerp(transitionFrom, transitionTo, 1f - Mathf.Pow(1f - t, 3f)));
            if (t >= 1f)
            {
                transitioning = false;
                if (transitionTo < .5f) FinishClose();
                ApplyRaycast();
            }
        }
        else if (isOpen && fitRect != null && fitRect.rect.size != lastFitSize) ApplyPresentation(progress);
        if (!isOpen) return;
        if (dirty) RebuildAll();
        UpdatePlayer();
        if (statusPanel != null) statusPanel.Advance(deltaTime);
    }

    private bool FloorActive() => SourceAlive() && source.HasFloor && source.Rooms != null && source.Rooms.Count > 0;
    private bool WaveActive() => WaveSourceAlive() && waveSource.HasActiveWave;

    private int CurrentRoom => SourceAlive() ? source.CurrentRoom : -1;

    private Rect MapArea => mapGraphic != null ? mapGraphic.rectTransform.rect : new Rect(-420f, -250f, 840f, 500f);

    private void RebuildAll()
    {
        dirty = false;
        ApplyModeLayout();
        if (SourceAlive() && source.HasFloor)
        {
            int key = Mathf.Max(1, source.FloorNumber);
            if (key != floorKey)
            {
                floorKey = key;
                silhouettes.Clear();
                pan = Vector2.zero;
            }
        }
        RebuildMap();
        RefreshTexts();
        UpdateActionButtons();
    }

    private void ApplyModeLayout()
    {
        bool waveMode = IsWaveMode;
        if (waveMode)
        {
            if (state == TerminalState.MapDetail) state = TerminalState.Menu;
            if (selectedAction == ActionChoice.Map) selectedAction = ActionChoice.Restart;
        }
        if (mapPanelRoot != null && mapPanelRoot.gameObject.activeSelf == waveMode)
            mapPanelRoot.gameObject.SetActive(!waveMode);
        if (infoPanelRoot != null)
        {
            Vector2 targetPos = waveMode ? new Vector2(0f, infoHome.y) : infoHome;
            if ((infoPanelRoot.anchoredPosition - targetPos).sqrMagnitude > 1e-4f)
                infoPanelRoot.anchoredPosition = targetPos;
        }
        if (mapButtonRoot != null && mapButtonRoot.gameObject.activeSelf == waveMode)
            mapButtonRoot.gameObject.SetActive(!waveMode);
        if (restartButtonRoot != null)
        {
            const float rx = 20f, rw = 230f;
            if (Mathf.Abs(restartButtonRoot.anchoredPosition.x - rx) > .01f)
                restartButtonRoot.anchoredPosition = new Vector2(rx, restartButtonRoot.anchoredPosition.y);
            if (Mathf.Abs(restartButtonRoot.sizeDelta.x - rw) > .01f)
                restartButtonRoot.sizeDelta = new Vector2(rw, restartButtonRoot.sizeDelta.y);
        }
        if (giveUpButtonRoot != null)
        {
            const float gx = 262f, gw = 230f;
            if (Mathf.Abs(giveUpButtonRoot.anchoredPosition.x - gx) > .01f)
                giveUpButtonRoot.anchoredPosition = new Vector2(gx, giveUpButtonRoot.anchoredPosition.y);
            if (Mathf.Abs(giveUpButtonRoot.sizeDelta.x - gw) > .01f)
                giveUpButtonRoot.sizeDelta = new Vector2(gw, giveUpButtonRoot.sizeDelta.y);
        }
        UpdateFooterText();
    }

    // 7차 V4: 모델 재생성(원본이 바뀔 때만 — BuildFloorModel은 할당한다)과 화면 맞춤(이동·배율·끌기 — 할당 없음)을 나눈다.
    private void RebuildMap()
    {
        RebuildModel();
        RefitView();
    }

    private void RebuildModel()
    {
        if (mapGraphic == null) return;
        if (!SourceAlive() || source.Rooms == null || !DetailAlive())
        {
            model = null;
            return;
        }
        model = DungeonMapMesh.BuildFloorModel(source.Rooms, detailSource, silhouettes);
    }

    /// <summary>지금 모델에 배율·이동을 적용해 지도·지금 방 층·버튼을 다시 그린다(할당 없음). 범위 끝에서 밀린 이동량은 버린다.</summary>
    private void RefitView()
    {
        if (mapGraphic == null) return;
        if (model == null || !DetailAlive())
        {
            model = null;
            mapGraphic.ClearFloor();
            ClearCurrent();
            UpdateControls();
            return;
        }
        ComputeZoom();
        var area = MapArea;
        float zoom = PitchForLevel(zoomLevel);
        view = DungeonMapMesh.FitFloor(model, CurrentRoom, area, mapPadding, minPitch, maxPitch, pan, false, zoom);
        // 범위 끝에서 더 밀리지 않게 실제로 적용된 이동량으로 되돌린다.
        pan = view.center - DungeonMapMesh.FitFloor(model, CurrentRoom, area, mapPadding, minPitch, maxPitch, Vector2.zero, false, zoom).center;
        mapGraphic.SetFloor(model, CurrentRoom, view, detailSource, ObjectiveAlive() ? objectiveSource.ObjectiveMarkers : null);
        RebuildCurrent();
        UpdateControls();
    }

    // 5차: 지금 방 테두리·꺾쇠·표지(지도를 다시 만들 때만). 지금 방 범위 = 큰 지도가 그 방 실루엣을 그린 사각형.
    private void RebuildCurrent()
    {
        var entry = model != null ? model.Find(CurrentRoom) : null;
        if (entry == null || entry.silhouette == null || entry.silhouette.IsEmpty)
        {
            ClearCurrent();
            return;
        }
        var area = MapArea;
        var box = view.RoomViewFor(area, entry).CellRect(entry.silhouette.bounds);
        if (currentGraphic != null)
        {
            // Current 층은 MapArea와 같은 사각형(펴짐) — 좌표를 그대로 쓴다.
            currentGraphic.SetCurrent(box, true);
        }
        if (currentLabel != null)
        {
            bool inside = box.Overlaps(area);
            if (inside)
            {
                var point = DungeonMapMesh.CurrentLabelPoint(area, box, null, out _);
                var anchored = point - area.center;
                var rt = currentLabel.rectTransform;
                if ((rt.anchoredPosition - anchored).sqrMagnitude > 1e-4f) rt.anchoredPosition = anchored;
            }
            currentLabelVisible = inside;
            if (currentLabel.enabled != inside) currentLabel.enabled = inside;
        }
    }

    private void ClearCurrent()
    {
        if (currentGraphic != null && currentGraphic.HasCurrent) currentGraphic.SetCurrent(default, false);
        currentLabelVisible = false;
        if (currentLabel != null && currentLabel.enabled) currentLabel.enabled = false;
    }

    private void UpdatePlayer()
    {
        if (playerGraphic == null) return;
        bool visible = false;
        var entry = model != null ? model.Find(CurrentRoom) : null;
        if (entry != null && DetailAlive() && detailSource.TryGetPlayerLocal(out var local))
        {
            var area = MapArea;
            var p = view.LocalToScreen(area, entry, local);
            if (area.Contains(p))
            {
                var anchored = p - area.center;
                var rt = playerGraphic.rectTransform;
                if ((rt.anchoredPosition - anchored).sqrMagnitude > 1e-4f) rt.anchoredPosition = anchored;
                visible = true;
            }
        }
        playerVisible = visible;
        float blink = visible ? Mathf.Lerp(.55f, 1f, .5f + .5f * Mathf.Cos(clock * Mathf.PI * 2f / playerBlinkPeriod)) : 0f;
        playerGraphic.canvasRenderer.SetAlpha(blink);
        // 5차: 바라보는 쪽(바뀔 때만 localScale.x)
        if (visible && headingSource != null && !(headingSource is UnityEngine.Object unityObject && unityObject == null) && headingSource.TryGetPlayerFacing(out int f))
        {
            int want = f < 0 ? -1 : 1;
            if (want != facing)
            {
                facing = want;
                playerGraphic.rectTransform.localScale = new Vector3(facing, 1f, 1f);
            }
        }
        // 5차: 지금 방 테두리 맥동(Mesh는 그대로, 알파만)
        if (currentGraphic != null)
        {
            float wave = .5f - .5f * Mathf.Cos(clock * Mathf.PI * 2f / currentPulsePeriod);
            currentGraphic.canvasRenderer.SetAlpha(currentGraphic.HasCurrent ? Mathf.Lerp(currentPulseMin, 1f, wave) : 0f);
        }
        // 구역 지도 대형 버튼 선택 하이라이트 및 상세 맵 탐색 활성 테두리 맥동
        if (mapActiveFrame != null)
        {
            bool alive = !IsWaveMode && isOpen && !isClosing;
            int mode = mapActiveFrame.MapFrameMode;
            float activeAlpha = 0f;
            if (alive && mode == DungeonTerminalGaugeGraphic.MapFrameSelected)
                activeAlpha = .84f + .16f * (.5f + .5f * Mathf.Cos(clock * Mathf.PI * 2f / 1.15f));
            else if (alive && mode == DungeonTerminalGaugeGraphic.MapFrameDetail)
                activeAlpha = .72f + .28f * (.5f + .5f * Mathf.Cos(clock * Mathf.PI * 2f / 1.4f));
            mapActiveFrame.canvasRenderer.SetAlpha(activeAlpha);
        }
    }

    // ===== 글자 =====

    private void RefreshTexts()
    {
        if (contextText != null) contextText.text = BuildContext();
        // 7차 V6: 계기판형 정보 판이 있으면 그쪽이 훈련·난이도·구역·목표를 모두 그린다(살아 있는 원본만 넘긴다).
        if (statusPanel != null)
        {
            statusPanel.Refresh(SourceAlive() ? source : null, ObjectiveAlive() ? objectiveSource : null, ThreatAlive() ? threatSource : null, ExitAlive() ? exitSource : null);
            return;
        }
        RefreshTraining();
        RefreshObjective();
    }

    private string BuildContext()
    {
        builder.Clear();
        if (SourceAlive() && source.HasFloor)
        {
            builder.AppendStatic(context);
            builder.Append(DungeonUiText.SpaceTag).Append(DungeonUiText.SpaceTag)
                   .Append('F').Append(Mathf.Max(1, source.FloorNumber)).Append('/').Append(Mathf.Max(1, source.FloorCount));
            CountRooms(out int cleared, out int total);
            builder.Append(DungeonUiText.SpaceTag).Append(DungeonUiText.SpaceTag).AppendStatic("구역 클리어 ").Append(cleared).Append('/').Append(total);
        }
        else if (WaveSourceAlive() && waveSource.HasActiveWave)
        {
            builder.AppendStatic(waveContext);
            int total = Mathf.Max(1, waveSource.TotalWaveCount);
            int current = Mathf.Clamp(waveSource.CurrentWaveNumber, 1, total);
            builder.Append(DungeonUiText.SpaceTag).Append(DungeonUiText.SpaceTag)
                   .Append("WAVE").Append(DungeonUiText.SpaceTag).Append(current).Append('/').Append(total);
        }
        else
        {
            builder.AppendStatic(context);
        }
        return builder.ToString();
    }

    private void CountRooms(out int cleared, out int total)
    {
        cleared = total = 0;
        var rooms = source.Rooms;
        if (rooms == null) return;
        for (int i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room == null || room.kind == DungeonRoomKind.Start) continue;
            total++;
            if (room.cleared) cleared++;
        }
    }

    private void RefreshTraining()
    {
        var tracker = TrainingRunTracker.Instance;
        var training = tracker != null ? tracker.TrainingDefinition : null;
        if (tracker == null || training == null || !tracker.IsRunActive)
        {
            if (gradeText != null) gradeText.text = "-";
            if (trainingText != null)
            {
                builder.Clear();
                if (AppendThreatRow()) builder.Append('\n');
                builder.AppendStatic("훈련 기록 없음");
                trainingText.text = builder.ToString();
            }
            return;
        }
        int hits = tracker.CurrentHItCount;
        float seconds = tracker.CurrentTimeSeconds;
        var grade = tracker.CurrentPredictedGrade;
        if (gradeText != null)
        {
            gradeText.text = TrainingBoardStyle.GradeLabel(grade);
            gradeText.color = TrainingBoardStyle.GradeColor(grade);
        }
        if (trainingText == null) return;
        TrainingBoardStyle.GetPassLimits(training, out int hitLimit, out float timeLimit);
        builder.Clear();
        if (AppendThreatRow()) builder.Append('\n');
        Row("경과 시간").Append("<mspace=0.6em>").AppendClock(seconds).Append("</mspace>");
        if (timeLimit >= 0f) builder.Append(DungeonUiText.SpaceTag).AppendColorTag(Dim).AppendStatic("/ 제한 ").AppendClock(timeLimit).Append("</color>");
        builder.Append('\n');
        Row("피격").Append(hits).AppendStatic("회");
        if (hitLimit >= 0) builder.Append(DungeonUiText.SpaceTag).AppendColorTag(Dim).AppendStatic("/ 한도 ").Append(hitLimit).AppendStatic("회").Append("</color>");
        builder.Append('\n');
        Row("등급 유지");
        var rule = RuleFor(training, grade);
        if (grade == Grade.Poor || rule == null) builder.AppendColorTag(TrainingBoardStyle.GradeColor(Grade.Poor)).AppendStatic("없음").Append("</color>");
        else
        {
            bool any = false;
            if (rule.Value.maxHits >= 0) { builder.AppendStatic("피격 ").Append(Mathf.Max(0, rule.Value.maxHits - hits)).AppendStatic("회"); any = true; }
            if (rule.Value.maxTimeSeconds >= 0f)
            {
                if (any) builder.Append(DungeonUiText.SpaceTag).Append(DungeonUiText.SpaceTag);
                builder.AppendStatic("시간 ").AppendClock(rule.Value.maxTimeSeconds - seconds);
                any = true;
            }
            if (any) builder.Append(DungeonUiText.SpaceTag).AppendStatic("남음");
            else builder.AppendStatic("제한 없음");
        }
        builder.Append("\n\n").AppendColorTag(Bright).AppendStatic("등급 기준").Append("</color>");
        if (training.rules != null)
            foreach (var r in training.rules)
            {
                builder.Append('\n').AppendColorTag(r.grade == grade ? TrainingBoardStyle.GradeColor(r.grade) : Dim)
                       .AppendStatic(TrainingBoardStyle.GradeLabel(r.grade)).Append(DungeonUiText.SpaceTag).Append(DungeonUiText.SpaceTag);
                if (r.maxHits >= 0) builder.AppendStatic("피격 ").Append(r.maxHits).AppendStatic("회 이하");
                else builder.AppendStatic("피격 제한 없음");
                if (r.maxTimeSeconds >= 0f) builder.Append(DungeonUiText.SpaceTag).Append(DungeonUiText.SpaceTag).AppendClock(r.maxTimeSeconds).AppendStatic(" 이내");
                builder.Append("</color>");
            }
        trainingText.text = builder.ToString();
    }

    // 4차 난이도 줄: '난이도  중급 · 클리어 5 · 다음 단계까지 1방'(최고 단계 = '최고 단계'). 원본이 없거나 HasThreat 거짓이면 쓰지 않는다(false).
    private bool AppendThreatRow()
    {
        if (!ThreatAlive() || !threatSource.HasThreat) return false;
        var level = threatSource.ThreatLevel;
        Row(threatRowLabel).AppendColorTag(DungeonThreatText.Color(level)).AppendStatic(DungeonThreatText.Label(level)).Append("</color>");
        builder.AppendSeparator().AppendStatic("클리어 ").Append(Mathf.Max(0, threatSource.ClearedRooms));
        builder.AppendSeparator();
        int next = threatSource.NextLevelAt;
        if (next < 0) builder.AppendColorTag(Dim).AppendStatic("최고 단계").Append("</color>");
        else builder.AppendColorTag(Dim).AppendStatic("다음 단계까지 ").Append(Mathf.Max(0, next - threatSource.ClearedRooms)).AppendStatic("방").Append("</color>");
        return true;
    }

    private StringBuilder Row(string label)
    {
        return builder.AppendColorTag(Dim).AppendStatic(label).Append("</color>").Append(DungeonUiText.SpaceTag).Append(DungeonUiText.SpaceTag);
    }

    private static GradeRule? RuleFor(TrainingDefinitionSO training, Grade grade)
    {
        if (training == null || training.rules == null) return null;
        foreach (var r in training.rules) if (r.grade == grade) return r;
        return null;
    }

    private void RefreshObjective()
    {
        bool has = ObjectiveAlive() && objectiveSource.HasObjective;
        if (!has)
        {
            if (objectiveTitleText != null) objectiveTitleText.text = DungeonUiText.Static("추가 목표 없음");
            if (objectiveText != null) objectiveText.text = "";
            if (objectiveCells != null) objectiveCells.SetProgress(0, 0, DungeonObjectivePanelGraphic.Amber);
            return;
        }
        int total = Mathf.Clamp(objectiveSource.ObjectiveTotal, 0, DungeonObjectivePanelGraphic.MaxCells);
        int done = Mathf.Clamp(objectiveSource.ObjectiveDone, 0, total);
        var color = objectiveSource.ObjectiveColor;
        var markers = objectiveSource.ObjectiveMarkers;
        int mask = 0;
        if (markers != null)
            for (int i = 0; i < markers.Count; i++)
                if (markers[i].done && markers[i].index >= 0 && markers[i].index < total) mask |= 1 << markers[i].index;
        if (objectiveCells != null) objectiveCells.SetProgress(total, mask, color);
        if (objectiveTitleText != null)
        {
            builder.Clear();
            builder.AppendColorTag(color).AppendStatic(string.IsNullOrEmpty(objectiveSource.ObjectiveTitle) ? "목표" : objectiveSource.ObjectiveTitle).Append("</color>")
                   .Append(DungeonUiText.SpaceTag).Append(DungeonUiText.SpaceTag).Append(done).Append('/').Append(total);
            objectiveTitleText.text = builder.ToString();
        }
        if (objectiveText == null) return;
        builder.Clear();
        for (int i = 0; i < total; i++)
        {
            bool found = false, done1 = false, known = false;
            if (markers != null)
                for (int m = 0; m < markers.Count; m++)
                    if (markers[m].index == i) { found = true; done1 = markers[m].done; known = markers[m].known; break; }
            if (i > 0) builder.Append('\n');
            builder.AppendStatic("목표 ").Append(i + 1).Append(DungeonUiText.SpaceTag).Append(DungeonUiText.SpaceTag);
            if (done1) builder.AppendColorTag(color).AppendStatic("완료").Append("</color>");
            else if (found && known) builder.AppendColorTag(Color.white).AppendStatic("발견").Append("</color>");
            else builder.AppendColorTag(Dim).AppendStatic("미확인").Append("</color>");
        }
        // 4차 출구 줄: 해금 전 '출구 잠김 레버 2/3', 해금 뒤 '출구 개방 · 시작 방'
        if (ExitAlive() && exitSource.HasExit)
        {
            if (total > 0) builder.Append('\n');
            builder.AppendStatic(exitRowLabel).Append(DungeonUiText.SpaceTag).Append(DungeonUiText.SpaceTag);
            if (exitSource.ExitUnlocked)
                builder.AppendColorTag(color).AppendStatic(exitOpenLabel).Append("</color>").AppendSeparator().AppendColorTag(Color.white).AppendStatic(exitWhereLabel).Append("</color>");
            else
                builder.AppendColorTag(Dim).AppendStatic(exitLockedLabel).Append(DungeonUiText.SpaceTag).AppendStatic(leverLabel).Append(DungeonUiText.SpaceTag)
                       .Append(done).Append('/').Append(total).Append("</color>");
        }
        objectiveText.text = builder.ToString();
    }

    // ===== 원본 생존 =====

    private bool SourceAlive()
    {
        if (source == null) return false;
        if (source is UnityEngine.Object unityObject && unityObject == null) { source = null; return false; }
        return true;
    }

    private bool DetailAlive()
    {
        if (detailSource == null) return false;
        if (detailSource is UnityEngine.Object unityObject && unityObject == null) { detailSource = null; return false; }
        return true;
    }

    private bool ObjectiveAlive()
    {
        if (objectiveSource == null) return false;
        if (objectiveSource is UnityEngine.Object unityObject && unityObject == null) { objectiveSource = null; return false; }
        return true;
    }

    private bool ThreatAlive()
    {
        if (threatSource == null) return false;
        if (threatSource is UnityEngine.Object unityObject && unityObject == null) { threatSource = null; return false; }
        return true;
    }

    private bool ExitAlive()
    {
        if (exitSource == null) return false;
        if (exitSource is UnityEngine.Object unityObject && unityObject == null) { exitSource = null; return false; }
        return true;
    }

    private bool WaveSourceAlive()
    {
        if (waveSource == null) return false;
        if (waveSource is UnityEngine.Object unityObject && unityObject == null) { waveSource = null; return false; }
        return true;
    }
}
