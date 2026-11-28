using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 던전 방 상태 띠 + 적 위치 마커(2026-11-24 던전형 Wave 2차, 요청 5·6·13, DESIGN_V2 §1).
/// 게임플레이(DungeonManager)가 <see cref="IDungeonCombatSource"/>로 내주는 읽기 전용 상태·이벤트를 구독해 그리기만 한다(AGENTS.md UI 접점 규칙).
/// DungeonManager가 Resources/UI/DungeonRoomStatus Prefab(DungeonRules.roomStatusPrefab)을 UI Canvas의 TrainingScoreboard 바로 다음 형제로 만들고 Bind한다(MainScene 무수정).
/// - 띠(화면 위 가운데 anchor·pivot (0.5,1), 자리 (0,-6), 420×34 — 보스 HUD 위 빈 줄):
///   방이 잠기고 적이 있으면 붉은 'HOSTILES n'(가로로 열림 + 붉은 번쩍임, 처치마다 숫자 펄스, 마지막 1이면 'LAST HOSTILE')
///   → 방 클리어 녹색 'ROOM CLEAR'(번쩍임 + 가로 펄스) → 던전 조건을 채웠으면 0.9초 뒤 금색 'SECTOR CLEAR' → 잠시 뒤 페이드아웃.
///   클리어와 던전 클리어 이벤트가 같은 프레임에 와도 ROOM CLEAR를 먼저 보인다. 잠금 해제(RoomUnlocked) 뒤 0.4초 더 머문다.
///   보스 바가 보이면 띠 가운데를 보스 바 가운데(x +132)로 0.3초 동안 옮긴다(세로는 그대로).
/// - 4차(2026-11-25, DESIGN_V4 §2.6·§2.8 — 선택 인터페이스는 원본을 'is'로 확인해 함께 구독한다, 기존 계약 무수정):
///   · 재입장 순찰(Phase = Patrol): 어두운 앰버 'HOSTILES n · BYPASS'를 가로로 열어 patrolHold 동안 보이고 가로로 접으며 사라진다. 적 위치 마커는 켜지 않는다.
///     같은 방문에서는 한 번만 보인다(단계가 Patrol에서 벗어나면 다음 방문에 다시 보인다). 다른 띠(클리어 연쇄)가 보이는 중이면 건너뛴다.
///   · 출구 해금(IDungeonExitSource.ExitUnlocked가 거짓 → 참이 된 순간 1회): 앰버 'EXIT UNLOCKED'. 띠가 비었거나 순찰 띠면 바로, 클리어 연쇄 중이면 그 뒤에 보인다.
///   · 단계 상승(IDungeonThreatSource.ThreatChanged, 새 단계 &gt; 이전, 원본 HasThreat일 때만): 단계 색 '▲ THREAT 중급'(▲는 판 Mesh). ROOM CLEAR 뒤 대기열로 roomToSectorDelay에 보인다.
///   · 대기열 순서(기존 규칙 유지 + 확장): ROOM CLEAR → THREAT ▲ → EXIT UNLOCKED → SECTOR CLEAR. SECTOR CLEAR는 언제나 마지막이다.
/// - 번쩍임: TrainingAlertBand(붉은 띠 — HOSTILES) / TrainingBeam(흰 빛줄 — 클리어 색으로 물듦) Image + TrainingBoardGlow 재질(가산, CanvasGroup 페이드됨).
///   화면 전체 Flash는 쓰지 않는다(사용자가 회복 개편 때 제거). 소리·흔들림은 DungeonManager의 roomClearFeedback·clearShake가 낸다.
/// - 마커: 자식 <see cref="DungeonEnemyMarkers"/>(HOSTILES 동안만 — 순찰에는 켜지 않는다).
/// - 보임: 던전 표시 중 && <see cref="DungeonHudGate"/>(HUD 보임·패배 화면·결과 보드 아님) && 진행 정지(IsHalted, 패배) 아님.
///   단 SECTOR CLEAR(던전 성공)는 던전이 끝나 IsHalted가 되어도 정해진 시간만큼 마저 보인다.
/// - 시간: 실제 시간(Time.unscaledDeltaTime) — 히트스톱·사망 정지·일시정지 중에도 일관. 카메라가 LateUpdate에 움직이므로 LateUpdate에서 진행한다.
/// - 글자: 정적 글꼴 esamanru(영문 대문자·숫자, 4차부터 난이도 단계 한글 이름과 가운뎃점 대용 <see cref="DungeonUiText.MidDot"/>).
/// - 5차(2026-11-25, DESIGN_V5 §1 S8): 자식 부품 <see cref="DungeonUnlockBurst"/>(UnlockBurst) — RoomCleared(잠금이 있던 방, 보스 방 포함)에 화면 가장자리 녹색 빛 +
///   띠 아래 '잠금 해제!' 문구를 재생하고, RoomUnlocked(문 열림)에 한 번 더 맥동시킨다. 띠 상태 기계와 따로 흐르는 평행 연출이라 띠 순서·시간은 그대로다.
///   보임은 띠와 같은 던전 HUD 판정 + 패배 정지 아님(던전 성공 SECTOR CLEAR가 이어지는 중에는 마저 보임). 부품이 없으면(4차 Prefab) 아무것도 하지 않는다.
/// - 6차(2026-11-26, DESIGN_V6 T12): 단계 상승 알림은 대기열 자리(ROOM CLEAR + roomToSectorDelay)에 띄우고 띠의 THREAT 단계는 건너뛴다(EXIT·SECTOR 순서 그대로).
///   던전 컷씬(DungeonCutsceneDirector)이 도는 동안은 알림을 기다렸다가 컷씬 뒤에 띄운다.
/// - 7차(2026-11-26, DESIGN_V7):
///   · V8 난이도 상승 계기: 단계 상승은 등급 알림(위 가운데 붉은 띠 — 6차에 재사용하던 것, 이제 main 그대로)과 다른 자리·다른 모양인 자식 부품
///     <see cref="DungeonThreatRise"/>(ThreatRise — 오른쪽 HUD 열 미니맵 아래 세로 5칸 사다리·빛 기둥, 약 3.65초)로 띄운다. 이전 배율은 이 띠가 기억한다(densityShown).
///     재생 중 컷씬·HUD 숨김·일시정지(현장 단말기 포함)면 시간표를 멈추고, 다음 전투 방 HOSTILES에 0.3초 안에 접고, 새 던전·재도전·패배·Unbind면 바로 끈다.
///     계기를 띄울 수 없으면(부품 없음·규칙에서 끔·훈련 기록 아님 = DungeonThreatAlert.Available 거짓) 4차 THREAT 띠로 대신한다.
///   · V5 레버 대기: 원본이 IDungeonObjectiveGate를 함께 구현하면 ObjectiveGateChanged를 구독한다. 적 전멸 뒤 레버가 남은 방(Phase = Objective)은
///     앰버 'PULL LEVER'(판 Exit 톤, 저절로 걷히지 않음)를 유지한다 — 유지되는 동안 EXIT UNLOCKED 등은 대기열에 남아 레버 컷씬 뒤 ROOM CLEAR 다음에 보인다.
///     레버를 당겨 방 클리어되면 RoomCleared로 ROOM CLEAR, 대기가 버려지면(재도전·종료) 원본 단계(None) 폴링으로 걷는다.
/// Prefab은 tools/Unity/DungeonRoomStatusSetup.cs가 만든다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonRoomStatus : MonoBehaviour, IDungeonCombatView, IDungeonThreatView
{
    public const string ResourcePath = "UI/DungeonRoomStatus";

    public enum BandState
    {
        Hidden = 0,
        Hostile = 1,
        RoomClear = 2,
        SectorClear = 3,
        /// <summary>4차: 재입장 순찰 'HOSTILES n · BYPASS'(잠깐 보이고 접음).</summary>
        Patrol = 4,
        /// <summary>4차: 마지막 레버 순간 'EXIT UNLOCKED'.</summary>
        ExitUnlocked = 5,
        /// <summary>4차: 단계 상승 '▲ THREAT 중급'.</summary>
        Threat = 6,
        /// <summary>7차 V5: 레버 대기 'PULL LEVER'(앰버, 레버를 당길 때까지 유지).</summary>
        Objective = 7,
    }

    [Header("Parts")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform band;
    [SerializeField] private CanvasGroup bandGroup;
    [SerializeField] private DungeonStatusPlateGraphic plate;
    [Tooltip("붉은 번쩍임(TrainingAlertBand + TrainingBoardGlow)")]
    [SerializeField] private Image flash;
    [Tooltip("클리어 빛줄(TrainingBeam + TrainingBoardGlow, 상태 색으로 물듦)")]
    [SerializeField] private Image sweep;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private DungeonEnemyMarkers markers;
    [Tooltip("5차 방 클리어 녹색 빛·'잠금 해제!' 부품(비어 있으면 연출 없음)")]
    [SerializeField] private DungeonUnlockBurst unlockBurst;
    [Tooltip("7차 난이도 상승 계기(미니맵 아래, 비어 있으면 4차 THREAT 띠로 대신)")]
    [SerializeField] private DungeonThreatRise threatRise;

    [Header("Labels (정적 글꼴 — 영문 대문자·숫자만)")]
    [SerializeField] private string hostileLabel = "HOSTILES";
    [SerializeField] private string lastHostileLabel = "LAST HOSTILE";
    [SerializeField] private string roomClearLabel = "ROOM CLEAR";
    [SerializeField] private string sectorClearLabel = "SECTOR CLEAR";
    [Tooltip("4차 순찰 띠: 'HOSTILES n · BYPASS'의 앞 글자")]
    [SerializeField] private string patrolLabel = "HOSTILES";
    [Tooltip("4차 순찰 띠: 가운뎃점 뒤 글자(통과 가능)")]
    [SerializeField] private string bypassLabel = "BYPASS";
    [Tooltip("4차 출구 해금 띠")]
    [SerializeField] private string exitLabel = "EXIT UNLOCKED";
    [Tooltip("4차 단계 상승 띠의 앞 글자(뒤에 한글 단계 이름 — DungeonThreatText.Label)")]
    [SerializeField] private string threatLabel = "THREAT";
    [Tooltip("7차 레버 대기 띠(적 전멸 뒤 레버가 남은 방 — 앰버, 유지)")]
    [SerializeField] private string objectiveLabel = "PULL LEVER";
    [Tooltip("HOSTILES 글자의 왼쪽·오른쪽 여백(아이콘·숫자 자리)")]
    [SerializeField] private Vector2 hostileLabelMargin = new Vector2(46f, 86f);

    [Header("Colors")]
    [SerializeField] private Color hostileColor = new Color32(0xff, 0x5a, 0x3c, 0xff);
    [SerializeField] private Color clearColor = new Color32(0x38, 0xb6, 0xff, 0xff);
    [SerializeField] private Color sectorColor = new Color32(0xff, 0xd1, 0x66, 0xff);
    [Tooltip("4차 순찰 띠(어두운 앰버)")]
    [SerializeField] private Color patrolColor = new Color32(0xd6, 0x92, 0x36, 0xff);
    [Tooltip("4차 출구 해금 띠(목표 앰버 #FFB021)")]
    [SerializeField] private Color exitColor = new Color32(0xff, 0xb0, 0x21, 0xff);

    [Header("Motion (실제 시간, 초)")]
    [SerializeField, Min(.01f)] private float fadeInTime = .2f;
    [SerializeField, Min(.01f)] private float fadeOutTime = .25f;
    [Tooltip("HOSTILES 등장: 가로로 열림 + 붉은 번쩍임")]
    [SerializeField, Min(.01f)] private float introTime = .22f;
    [Tooltip("처치마다 숫자 펄스")]
    [SerializeField, Min(.01f)] private float killPulseTime = .4f;
    [Tooltip("클리어 빛줄 번쩍임")]
    [SerializeField, Min(.01f)] private float clearFlashTime = .45f;
    [Tooltip("ROOM CLEAR 최소 유지 시간")]
    [SerializeField, Min(0f)] private float clearHold = 1.3f;
    [Tooltip("잠금 해제(문 열림) 뒤 더 머무는 시간")]
    [SerializeField, Min(0f)] private float unlockLinger = .4f;
    [Tooltip("잠금 해제 알림이 오지 않을 때 ROOM CLEAR를 최대로 유지하는 시간")]
    [SerializeField, Min(0f)] private float maxClearWait = 3f;
    [Tooltip("ROOM CLEAR에서 다음 띠(THREAT·EXIT·SECTOR)로 넘어가는 시간(같은 프레임에 둘 다 와도 ROOM CLEAR를 먼저 보인다)")]
    [SerializeField, Min(0f)] private float roomToSectorDelay = .9f;
    [Tooltip("SECTOR CLEAR 유지 시간(던전이 끝나도 마저 보인다)")]
    [SerializeField, Min(0f)] private float sectorHold = 1.6f;
    [Tooltip("띠 페이드아웃")]
    [SerializeField, Min(.01f)] private float bandFadeOut = .35f;
    [Tooltip("4차 순찰 띠 유지 시간(그 뒤 가로로 접으며 사라진다)")]
    [SerializeField, Min(0f)] private float patrolHold = 1.6f;
    [Tooltip("4차 EXIT UNLOCKED 유지 시간")]
    [SerializeField, Min(0f)] private float exitHold = 1.8f;
    [Tooltip("4차 THREAT ▲ 유지 시간")]
    [SerializeField, Min(0f)] private float threatHold = .9f;

    [Header("Boss")]
    [Tooltip("보스 바가 보일 때 띠 가운데를 옮기는 거리(px, 보스 HUD 가운데 = +132)")]
    [SerializeField] private float bossShiftX = 132f;
    [SerializeField, Min(.01f)] private float bossShiftTime = .3f;

    private IDungeonCombatSource source;
    private IDungeonExitSource exitSource;
    private IDungeonThreatSource threatSource;
    private IDungeonObjectiveGate gateSource;
    private bool initialized;
    private float clock;
    private float alpha;
    private BandState state = BandState.Hidden;
    private float stateStart;
    private float fadeStart = -1f;
    private float fadeFrom = 1f;
    private float bandAlpha;
    private int shownRemaining, shownTotal;
    private float killPulseStart = -100f;
    private bool sectorPending, unlocked;
    private float unlockAt;
    private Vector2 bandBase;
    private float shiftFrom, shiftTo, shiftStart = -100f, shiftNow;
    private StageHUD stageHud;
    private float nextStageSearch = float.NegativeInfinity;
    // 4차 대기열·순찰
    private bool threatPending, exitPending, patrolSeen, lastExitUnlocked;
    private DungeonThreatLevel threatShown = DungeonThreatLevel.Intro;
    // 6차 알림(7차 V8 — 난이도 상승 계기): 띠 대신 띄울 단계 상승(이전 → 새), 컷씬 중이면 기다린다.
    private bool alertPending;
    private DungeonThreatLevel alertFrom = DungeonThreatLevel.Intro, alertTo = DungeonThreatLevel.Intro;
    // 7차 V8: 마지막으로 보인 적 수 배율(원본은 지금 배율만 준다 — 연결·재도전 때 지금 값, 상승 대기 시작 때 이전 값으로 붙잡고, 띄운 뒤 갱신).
    private float densityShown = 1f, alertFromDensity = 1f;

    // ===== 검증·디버그가 읽는 값 =====
    public bool ShowObjectiveAnnouncements { get; set; } = true;
    public IDungeonCombatSource Source => source;
    public IDungeonExitSource ExitSource => exitSource;
    public IDungeonThreatSource ThreatSource => threatSource;
    public BandState State => state;
    public float Alpha => alpha;
    public float BandAlpha => bandAlpha;
    public int ShownRemaining => shownRemaining;
    public int ShownTotal => shownTotal;
    public DungeonEnemyMarkers Markers => markers;
    /// <summary>5차 방 클리어 녹색 빛·'잠금 해제!' 부품(없으면 null).</summary>
    public DungeonUnlockBurst UnlockBurst => unlockBurst;
    /// <summary>7차 난이도 상승 계기 부품(없으면 null).</summary>
    public DungeonThreatRise ThreatRise => threatRise;
    /// <summary>7차 레버 대기 원본(IDungeonObjectiveGate, 없으면 null).</summary>
    public IDungeonObjectiveGate GateSource => gateSource;
    public string ObjectiveLabel => objectiveLabel;
    public DungeonStatusPlateGraphic Plate => plate;
    public RectTransform Band => band;
    public Image Flash => flash;
    public Image Sweep => sweep;
    public Image Icon => icon;
    public TMP_Text LabelText => labelText;
    public TMP_Text CountText => countText;
    public Color HostileColor => hostileColor;
    public Color ClearColor => clearColor;
    public Color SectorColor => sectorColor;
    public Color PatrolColor => patrolColor;
    public Color ExitColor => exitColor;
    public float RoomToSectorDelay => roomToSectorDelay;
    public float PatrolHold => patrolHold;
    public float ExitHold => exitHold;
    public float ThreatHold => threatHold;
    public float BossShiftX => bossShiftX;
    public Vector2 BandBasePosition => bandBase;
    /// <summary>대기 중인 단계 상승·출구 해금 띠(검증용).</summary>
    public bool ThreatPending => threatPending;
    public bool ExitPending => exitPending;
    /// <summary>마지막으로 보인(또는 대기 중인) 단계 상승의 단계.</summary>
    public DungeonThreatLevel ThreatShown => threatShown;
    /// <summary>HUD 숨김을 따를 CanvasGroup을 직접 지정한다(검증용). null이면 장면의 PlayerHUD CanvasGroup.</summary>
    public CanvasGroup HudGroupOverride { get; set; }
    /// <summary>보스 바 보임을 직접 지정한다(검증용). null이면 StageHUD.CurrentBoss로 판정.</summary>
    public bool? BossBarOverride { get; set; }
    /// <summary>단계 상승 알림을 받을 곳(검증용 — 이전·새 단계·이전·새 적 수 배율을 받아 띄웠으면 true). null이면 7차 난이도 상승 계기(ThreatRise).</summary>
    public Func<DungeonThreatLevel, DungeonThreatLevel, float, float, bool> ThreatAlertOverride { get; set; }
    /// <summary>7차: 난이도 상승 계기를 띄울 수 있는지 직접 지정한다(검증용). null이면 DungeonThreatAlert.Available(규칙 켬 + 훈련 기록 중).</summary>
    public bool? ThreatRiseAvailableOverride { get; set; }
    /// <summary>7차: 일시정지(GameTimeController.IsPaused — 일시정지 메뉴·현장 단말기)를 직접 지정한다(검증용). null이면 실제 값.</summary>
    public bool? PauseOverride { get; set; }
    /// <summary>7차: 마지막으로 보인(또는 상승 대기 시작 때 붙잡은) 적 수 배율(검증용).</summary>
    public float DensityShown => densityShown;
    /// <summary>6차: 컷씬 진행 여부를 직접 지정한다(검증용). null이면 DungeonCutsceneDirector.AnyPlaying.</summary>
    public bool? CutsceneOverride { get; set; }
    /// <summary>6차: 띄우려고 기다리는 단계 상승 알림이 있는지(컷씬 중 등, 검증용).</summary>
    public bool ThreatAlertPending => alertPending;
    /// <summary>알림(7차 계기 또는 검증용 대체)으로 띄운 단계 상승 수(검증용).</summary>
    public int ThreatAlertsShown { get; private set; }

    private void Awake() => EnsureInit();

    private void EnsureInit()
    {
        if (initialized) return;
        initialized = true;
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        if (band != null) bandBase = band.anchoredPosition;
        if (markers != null) markers.EnsureInit();
        if (unlockBurst != null) unlockBurst.EnsureInit();
        if (threatRise != null) threatRise.EnsureInit();
        bandAlpha = 0f;
        Apply();
    }

    private void OnDestroy() => Unbind();

    // ===== 연결(IDungeonCombatView) =====

    public void Bind(IDungeonCombatSource combatSource)
    {
        EnsureInit();
        if (ReferenceEquals(source, combatSource)) { SyncFromSource(); return; }
        Unbind();
        source = combatSource;
        if (source == null) return;
        source.RoomHostile += OnRoomHostile;
        source.HostilesChanged += OnHostilesChanged;
        source.RoomCleared += OnRoomCleared;
        source.RoomUnlocked += OnRoomUnlocked;
        source.FloorCleared += OnFloorCleared;
        source.FloorStarted += OnFloorStarted;
        source.FloorEnded += OnFloorEnded;
        var rules = DungeonRulesSO.Default;
        if (rules != null && markers != null)
        {
            markers.Threshold = rules.markerThreshold;
            markers.RevealDelay = rules.offscreenRevealDelay;
        }
        if (combatSource is IDungeonThreatSource threats) BindThreat(threats);
        if (ShowObjectiveAnnouncements)
        {
            if (combatSource is IDungeonExitSource exits) BindExit(exits);
            if (combatSource is IDungeonObjectiveGate gate) BindGate(gate);
        }
        SyncFromSource();
    }

    /// <summary>모든 원본(전투·출구·난이도) 연결을 끊는다.</summary>
    public void Unbind()
    {
        if (source != null)
        {
            source.RoomHostile -= OnRoomHostile;
            source.HostilesChanged -= OnHostilesChanged;
            source.RoomCleared -= OnRoomCleared;
            source.RoomUnlocked -= OnRoomUnlocked;
            source.FloorCleared -= OnFloorCleared;
            source.FloorStarted -= OnFloorStarted;
            source.FloorEnded -= OnFloorEnded;
        }
        source = null;
        UnbindExit();
        UnbindThreat();
        UnbindGate();
        sectorPending = unlocked = false;
        threatPending = exitPending = patrolSeen = false;
        alertPending = false;
        SetState(BandState.Hidden);
        bandAlpha = 0f;
        fadeStart = -1f;
        if (initialized)
        {
            if (markers != null) markers.Clear();
            if (unlockBurst != null) unlockBurst.Stop();
            if (threatRise != null) threatRise.Stop();
            Apply();
        }
    }

    // IDungeonThreatView(명시 구현 — Bind(IDungeonCombatSource) 호출이 모호해지지 않게)
    void IDungeonThreatView.Bind(IDungeonThreatSource threats) => BindThreat(threats);
    void IDungeonThreatView.Unbind() => UnbindThreat();

    /// <summary>출구 원본을 연결한다(같은 원본이면 그대로). 지금 해금 상태는 '이미 본 것'으로 친다(연결 순간에는 띠를 띄우지 않는다).</summary>
    public void BindExit(IDungeonExitSource exits)
    {
        EnsureInit();
        if (ReferenceEquals(exitSource, exits)) return;
        UnbindExit();
        exitSource = exits;
        if (exitSource == null) return;
        exitSource.ExitChanged += OnExitChanged;
        lastExitUnlocked = ExitUnlockedNow();
    }

    /// <summary>난이도 원본을 연결한다(같은 원본이면 그대로).</summary>
    public void BindThreat(IDungeonThreatSource threats)
    {
        EnsureInit();
        if (ReferenceEquals(threatSource, threats)) return;
        UnbindThreat();
        threatSource = threats;
        if (threatSource == null) return;
        threatSource.ThreatChanged += OnThreatChanged;
        threatShown = threatSource.ThreatLevel;
        densityShown = threatSource.DensityMultiplier;
    }

    /// <summary>7차 V5: 레버 대기 원본을 연결한다(같은 원본이면 그대로). 연결 순간 이미 대기 중이면 원본 단계 폴링(Objective)이 'PULL LEVER'를 맞춘다.</summary>
    public void BindGate(IDungeonObjectiveGate gate)
    {
        EnsureInit();
        if (ReferenceEquals(gateSource, gate)) return;
        UnbindGate();
        gateSource = gate;
        if (gateSource != null) gateSource.ObjectiveGateChanged += OnObjectiveGateChanged;
    }

    private void UnbindGate()
    {
        if (gateSource != null) gateSource.ObjectiveGateChanged -= OnObjectiveGateChanged;
        gateSource = null;
    }

    private void UnbindExit()
    {
        if (exitSource != null) exitSource.ExitChanged -= OnExitChanged;
        exitSource = null;
        exitPending = lastExitUnlocked = false;
    }

    private void UnbindThreat()
    {
        if (threatSource != null) threatSource.ThreatChanged -= OnThreatChanged;
        threatSource = null;
        threatPending = false;
        alertPending = false;
    }

    // 연결 순간(또는 이벤트를 놓쳤을 때) 원본 단계에 맞춘다.
    private void SyncFromSource()
    {
        if (!SourceAlive()) return;
        if (source.Phase == DungeonRoomPhase.Hostile && state != BandState.Hostile) EnterHostile(source.HostilesRemaining, source.HostilesTotal);
    }

    // ===== 이벤트 =====

    private void OnFloorStarted()
    {
        sectorPending = unlocked = false;
        threatPending = exitPending = patrolSeen = false;
        alertPending = false;
        lastExitUnlocked = ExitUnlockedNow();
        densityShown = DensityNow();
        SetState(BandState.Hidden);
        bandAlpha = 0f;
        fadeStart = -1f;
        if (markers != null) markers.Clear();
        if (unlockBurst != null) unlockBurst.Stop();
        if (threatRise != null) threatRise.Stop();
        Apply();
    }

    // 7차 V5: 레버 대기 시작 → 'PULL LEVER'. 끝(false)은 같은 프레임의 RoomCleared(ROOM CLEAR) 또는 원본 단계 폴링(대기를 버림 → None → 걷힘)에 맡긴다.
    private void OnObjectiveGateChanged(int room, bool awaiting)
    {
        if (awaiting && SourceAlive() && !source.IsHalted) EnterObjective();
    }

    private void EnterObjective()
    {
        shownRemaining = 0;
        killPulseStart = -100f;
        SetState(BandState.Objective);
    }

    private void OnRoomHostile()
    {
        if (!SourceAlive()) return;
        // 4차: 재입장 순찰도 같은 이벤트로 올 수 있다 — 잠긴 전투가 아니므로 순찰 띠 규칙으로 보낸다.
        if (source.Phase == DungeonRoomPhase.Patrol) { TryEnterPatrol(); return; }
        EnterHostile(source.HostilesRemaining, source.HostilesTotal);
    }

    private void OnHostilesChanged(int remaining, int total)
    {
        if (state == BandState.Patrol)
        {
            int shown = Mathf.Max(0, remaining);
            if (shown != shownRemaining)
            {
                shownRemaining = shown;
                shownTotal = Mathf.Max(shown, total);
                UpdateTexts();
            }
            return;
        }
        if (state != BandState.Hostile)
        {
            if (SourceAlive() && source.Phase == DungeonRoomPhase.Hostile && remaining > 0) EnterHostile(remaining, total);
            else if (SourceAlive() && source.Phase == DungeonRoomPhase.Patrol) TryEnterPatrol();
            return;
        }
        if (remaining < shownRemaining) killPulseStart = clock;
        shownRemaining = Mathf.Max(0, remaining);
        shownTotal = Mathf.Max(shownRemaining, total);
        UpdateTexts();
    }

    private void OnRoomCleared(int roomId)
    {
        sectorPending = false;
        unlocked = false;
        shownRemaining = 0;
        SetState(BandState.RoomClear);
        // 5차: RoomCleared는 잠금이 있던 방에서만 온다(순찰 전멸·빈 동적 방·출구 입장은 오지 않음) → 녹색 빛·'잠금 해제!'.
        if (unlockBurst != null) unlockBurst.Play();
    }

    private void OnRoomUnlocked()
    {
        unlocked = true;
        unlockAt = clock;
        if (unlockBurst != null) unlockBurst.Beat();
    }

    private void OnFloorCleared()
    {
        sectorPending = true;
        // 클리어 연쇄(ROOM CLEAR → THREAT → EXIT) 중이면 그 끝에 보인다. 아니면 바로.
        bool chaining = fadeStart < 0f && (state == BandState.RoomClear || state == BandState.Threat || state == BandState.ExitUnlocked);
        if (!chaining) SetState(BandState.SectorClear);
    }

    private void OnFloorEnded()
    {
        // 싸우는 중(또는 레버 대기 중)에 던전이 끝나면(초기화 등) 바로 걷는다. 클리어 표시는 정해진 시간만큼 마저 보인다.
        if (state == BandState.Hostile || state == BandState.Patrol || state == BandState.Objective) BeginFade();
        threatPending = exitPending = false;
        if (!unlocked) { unlocked = true; unlockAt = clock; }
    }

    private void OnExitChanged()
    {
        bool now = ExitUnlockedNow();
        bool entered = exitSource != null && exitSource.HasExit && exitSource.ExitEntered;
        if (now && !lastExitUnlocked && !entered)
        {
            exitPending = true;
            TryFlushPending();
        }
        lastExitUnlocked = now;
    }

    private void OnThreatChanged(DungeonThreatLevel previous, DungeonThreatLevel next)
    {
        // 4차 검토: 원본이 난이도를 쓰는 훈련(HasThreat)일 때만 단계 상승 띠를 띄운다(목표 패널·단말기와 같은 기준 — 손 배치 던전만 있는 훈련에는 없다).
        if (next > previous && ThreatShownAllowed())
        {
            // 알림의 '이전 단계'(와 7차 이전 배율)는 아직 보이지 않은 상승의 처음 값이다(대기 중에 두 번 오르면 입문 → 중급).
            if (!threatPending && !alertPending) { alertFrom = previous; alertFromDensity = densityShown; }
            threatPending = true;
            threatShown = next;
            TryFlushPending();
        }
        else
        {
            // 재도전(처음 단계로)·내려감은 알리지 않는다(7차: 재생 중인 계기도 바로 끈다).
            threatPending = false;
            alertPending = false;
            threatShown = next;
            densityShown = DensityNow();
            if (next < previous && threatRise != null) threatRise.Stop();
        }
    }

    // 난이도 원본이 살아 있고 이 훈련이 실시간 난이도를 쓰는지(IDungeonThreatSource.HasThreat).
    private bool ThreatShownAllowed()
    {
        if (threatSource == null) return false;
        if (threatSource is UnityEngine.Object o && o == null) return false;
        return threatSource.HasThreat;
    }

    private bool ExitUnlockedNow() => exitSource != null && !(exitSource is UnityEngine.Object o && o == null) && exitSource.HasExit && exitSource.ExitUnlocked;

    // 단계 상승 알림을 띄울 곳이 있는지(검증용 대체 → 7차 난이도 상승 계기 — 부품 있음 && 규칙 켬·훈련 기록 중).
    private bool ThreatAlertAvailable() => ThreatAlertOverride != null || (threatRise != null && (ThreatRiseAvailableOverride ?? DungeonThreatAlert.Available));

    // 지금 단계의 적 수 배율(난이도 원본이 없거나 사라졌으면 1).
    private float DensityNow()
    {
        if (threatSource == null || (threatSource is UnityEngine.Object o && o == null)) return 1f;
        return threatSource.DensityMultiplier;
    }

    // 기다리는 단계 상승 알림을 띄운다(던전 컷씬 중이면 다음 프레임에 다시 본다). 띄우지 못했으면(부품이 그 사이 사라짐 등) 버린다.
    private void FlushThreatAlert()
    {
        if (!alertPending || (CutsceneOverride ?? DungeonCutsceneDirector.AnyPlaying)) return;
        alertPending = false;
        if (!ThreatShownAllowed()) return;
        float density = DensityNow();
        bool shown;
        try { shown = ThreatAlertOverride != null ? ThreatAlertOverride(alertFrom, alertTo, alertFromDensity, density) : threatRise != null && threatRise.Play(alertFrom, alertTo, alertFromDensity, density); }
        catch (Exception e) { Debug.LogException(e, this); shown = false; }
        if (shown) ThreatAlertsShown++;
        alertFrom = alertTo;
        densityShown = density;
    }

    private void EnterHostile(int remaining, int total)
    {
        shownRemaining = Mathf.Max(0, remaining);
        shownTotal = Mathf.Max(shownRemaining, total);
        killPulseStart = -100f;
        sectorPending = unlocked = false;
        SetState(BandState.Hostile);
        // 7차 V8: 다음 전투 방이 시작되면 난이도 상승 계기를 접는다(전투 시선 우선 — 0.3초).
        if (threatRise != null) threatRise.Fold();
    }

    // 순찰 띠: 같은 방문에서 한 번만, 남은 적이 있고 다른 띠가 보이지 않을 때만(클리어 연쇄를 끊지 않는다).
    private void TryEnterPatrol()
    {
        if (!SourceAlive() || patrolSeen || source.IsHalted) return;
        int remaining = source.HostilesRemaining;
        if (remaining <= 0) return;
        patrolSeen = true;
        bool idle = state == BandState.Hidden || (fadeStart >= 0f && state != BandState.Hostile);
        if (!idle) return;
        shownRemaining = remaining;
        shownTotal = Mathf.Max(remaining, source.HostilesTotal);
        killPulseStart = -100f;
        SetState(BandState.Patrol);
    }

    // 대기 중인 THREAT·EXIT를 지금 보일 수 있으면 보인다(띠가 비었거나 사라지는 중, 또는 출구가 순찰 띠를 앞지를 때).
    private void TryFlushPending()
    {
        if (!threatPending && !exitPending) return;
        if (SourceAlive() && source.IsHalted && state != BandState.SectorClear) return;
        bool idle = state == BandState.Hidden || (fadeStart >= 0f && state != BandState.Hostile);
        bool patrolOverride = exitPending && state == BandState.Patrol && fadeStart < 0f;
        if (idle || patrolOverride) ShowNext(includeSector: false);
    }

    // 대기열의 다음 띠: THREAT ▲ → EXIT UNLOCKED → SECTOR CLEAR. 보였으면 true.
    private bool ShowNext(bool includeSector)
    {
        if (threatPending)
        {
            threatPending = false;
            // 대기 중에 원본이 바뀌었거나 HasThreat가 꺼졌으면 건너뛴다.
            if (ThreatShownAllowed())
            {
                // 7차 V8: 미니맵 아래 난이도 상승 계기로 띄운다(띠의 THREAT 단계는 건너뛰고 대기열의 다음 띠로). 띄울 수 없으면 4차 띠로 대신한다.
                if (ThreatAlertAvailable())
                {
                    alertPending = true;
                    alertTo = threatShown;
                    FlushThreatAlert();
                }
                else
                {
                    densityShown = DensityNow();
                    SetState(BandState.Threat);
                    return true;
                }
            }
        }
        if (exitPending)
        {
            exitPending = false;
            SetState(BandState.ExitUnlocked);
            return true;
        }
        if (includeSector && sectorPending)
        {
            SetState(BandState.SectorClear);
            return true;
        }
        return false;
    }

    private void SetState(BandState next)
    {
        state = next;
        stateStart = clock;
        fadeStart = -1f;
        UpdateTexts();
    }

    private void BeginFade()
    {
        if (fadeStart >= 0f || state == BandState.Hidden) return;
        fadeStart = clock;
        fadeFrom = bandAlpha;
    }

    private void UpdateTexts()
    {
        bool hostile = state == BandState.Hostile;
        bool patrol = state == BandState.Patrol;
        bool threat = state == BandState.Threat;
        bool last = hostile && shownRemaining == 1;
        if (labelText != null)
        {
            string shown;
            switch (state)
            {
                case BandState.Hostile: shown = Spaced(last ? lastHostileLabel : hostileLabel); break;
                case BandState.RoomClear: shown = Spaced(roomClearLabel); break;
                case BandState.SectorClear: shown = Spaced(sectorClearLabel); break;
                case BandState.ExitUnlocked: shown = Spaced(exitLabel); break;
                case BandState.Objective: shown = Spaced(objectiveLabel); break;
                case BandState.Patrol:
                    // 'HOSTILES n · BYPASS' — 가운뎃점은 정적 글꼴에 없어 마침표를 올려 쓴다(DungeonUiText.MidDot).
                    shown = Spaced(patrolLabel) + DungeonUiText.SpaceTag + shownRemaining + DungeonUiText.SpaceTag + DungeonUiText.MidDot + DungeonUiText.SpaceTag + Spaced(bypassLabel);
                    break;
                case BandState.Threat:
                    shown = Spaced(threatLabel) + DungeonUiText.SpaceTag + DungeonThreatText.Label(threatShown);
                    break;
                default: shown = labelText.text; break;
            }
            if (labelText.text != shown) labelText.text = shown;
            bool left = hostile || patrol || threat;
            var align = left ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
            if (labelText.alignment != align) labelText.alignment = align;
            var margin = hostile ? new Vector4(hostileLabelMargin.x, 0f, last ? hostileLabelMargin.x : hostileLabelMargin.y, 0f)
                       : patrol || threat ? new Vector4(hostileLabelMargin.x, 0f, hostileLabelMargin.x, 0f)
                       : Vector4.zero;
            if (labelText.margin != margin) labelText.margin = margin;
        }
        if (countText != null)
        {
            string count = shownRemaining.ToString();
            if (hostile && countText.text != count) countText.text = count;
        }
        // 숨김 상태에서는 마지막 색을 둔다(페이드아웃 중 색이 바뀌지 않게).
        if (plate != null && state != BandState.Hidden)
        {
            plate.CurrentTone = state switch
            {
                BandState.RoomClear => DungeonStatusPlateGraphic.Tone.Clear,
                BandState.SectorClear => DungeonStatusPlateGraphic.Tone.Sector,
                BandState.Patrol => DungeonStatusPlateGraphic.Tone.Patrol,
                BandState.ExitUnlocked => DungeonStatusPlateGraphic.Tone.Exit,
                BandState.Objective => DungeonStatusPlateGraphic.Tone.Exit,
                BandState.Threat => DungeonStatusPlateGraphic.Tone.Threat,
                _ => DungeonStatusPlateGraphic.Tone.Hostile,
            };
            if (threat) plate.Accent = (Color32)DungeonThreatText.Color(threatShown);
            plate.RiseGlyph = threat;
        }
    }

    // esamanru 정적 글꼴에는 공백 글자가 없다 → 공백을 TMP 여백 태그로 바꿔 넣는다(무장 특성 글자와 같은 방식).
    private static string Spaced(string label) => label != null && label.IndexOf(' ') >= 0 ? label.Replace(" ", DungeonUiText.SpaceTag) : label;

    // ===== 진행 =====

    private void LateUpdate() => Advance(Time.unscaledDeltaTime);

    /// <summary>실제 시간 dt만큼 상태·움직임·마커를 진행한다(LateUpdate가 부른다. 검증은 직접 부른다).</summary>
    public void Advance(float deltaTime)
    {
        EnsureInit();
        deltaTime = Mathf.Max(0f, deltaTime);
        clock += deltaTime;
        bool alive = SourceAlive();

        // 이벤트를 놓쳤을 때의 보정(원본 단계 폴링)
        if (alive)
        {
            var phase = source.Phase;
            if (phase == DungeonRoomPhase.Hostile && (state == BandState.Hidden || state == BandState.Patrol || fadeStart >= 0f) && source.HostilesRemaining > 0 && !source.IsHalted)
                EnterHostile(source.HostilesRemaining, source.HostilesTotal);
            else if (phase == DungeonRoomPhase.Cleared && (state == BandState.Hostile || state == BandState.Objective)) OnRoomCleared(-1);
            else if (phase == DungeonRoomPhase.None && (state == BandState.Hostile || state == BandState.Objective)) BeginFade();
            // 7차 V5: 레버 대기(이벤트를 놓쳤을 때 — 마지막 처치 뒤 'HOSTILES 0'이 남지 않게).
            else if (phase == DungeonRoomPhase.Objective && !source.IsHalted)
            {
                if (ShowObjectiveAnnouncements && (state == BandState.Hostile || state == BandState.Hidden)) EnterObjective();
                else if (state == BandState.Hostile) BeginFade();
            }
            // 4차 순찰: 방문마다 한 번(단계가 Patrol에서 벗어나면 다음 방문에 다시)
            if (phase == DungeonRoomPhase.Patrol) TryEnterPatrol();
            else
            {
                patrolSeen = false;
                if (state == BandState.Patrol) BeginFade();
            }
            TryFlushPending();
            // 6차: 컷씬 때문에 기다리던 단계 상승 알림.
            FlushThreatAlert();
        }
        else if (state != BandState.Hidden) BeginFade();

        // 상태 시간표
        float t = clock - stateStart;
        if (state == BandState.RoomClear && fadeStart < 0f)
        {
            bool next = threatPending || exitPending || sectorPending;
            if (next && t >= roomToSectorDelay) ShowNext(includeSector: true);
            else if (!next && t >= clearHold && (unlocked ? clock >= unlockAt + unlockLinger : t >= maxClearWait)) BeginFade();
        }
        else if (state == BandState.Threat && fadeStart < 0f && t >= threatHold) { if (!ShowNext(includeSector: true)) BeginFade(); }
        else if (state == BandState.ExitUnlocked && fadeStart < 0f && t >= exitHold) { if (!ShowNext(includeSector: true)) BeginFade(); }
        else if (state == BandState.Patrol && fadeStart < 0f && t >= patrolHold) { if (!ShowNext(includeSector: false)) BeginFade(); }
        else if (state == BandState.SectorClear && fadeStart < 0f && t >= sectorHold) BeginFade();

        // 띠 알파
        t = clock - stateStart;
        float baseAlpha = state switch
        {
            BandState.Hostile => TrainingBoardMotion.Seg(t, 0f, introTime),
            BandState.Patrol => TrainingBoardMotion.Seg(t, 0f, introTime),
            BandState.Hidden => 0f,
            _ => 1f,
        };
        if (fadeStart >= 0f)
        {
            float k = TrainingBoardMotion.Seg(clock, fadeStart, fadeStart + bandFadeOut);
            bandAlpha = Mathf.Min(baseAlpha, fadeFrom) * (1f - k);
            if (k >= 1f)
            {
                SetState(BandState.Hidden);
                bandAlpha = 0f;
            }
        }
        else bandAlpha = baseAlpha;

        // 전체 보임(던전 HUD 판정)
        bool halted = alive && source.IsHalted;
        bool show = alive && state != BandState.Hidden && DungeonHudGate.ShouldShowDungeonUi(HudGroupOverride) && (!halted || state == BandState.SectorClear);
        if (!alive && state != BandState.Hidden) show = DungeonHudGate.ShouldShowDungeonUi(HudGroupOverride);   // 원본이 사라져도 페이드아웃은 보인다
        float rate = deltaTime / (show ? fadeInTime : fadeOutTime);
        alpha = Mathf.MoveTowards(alpha, show ? 1f : 0f, rate);
        if (group != null && !Mathf.Approximately(group.alpha, alpha)) group.alpha = alpha;

        // 보스 바 쪽으로 가로 이동
        bool boss = BossBarVisible();
        float shiftTarget = boss ? bossShiftX : 0f;
        if (!Mathf.Approximately(shiftTarget, shiftTo))
        {
            shiftFrom = shiftNow;
            shiftTo = shiftTarget;
            shiftStart = clock;
        }
        shiftNow = Mathf.Lerp(shiftFrom, shiftTo, TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(clock, shiftStart, shiftStart + bossShiftTime)));

        // 마커: HOSTILES 동안만(순찰·패배·HUD 숨김이면 보이지 않는다)
        if (markers != null)
        {
            markers.BossBarVisible = boss;
            bool active = alive && state == BandState.Hostile && fadeStart < 0f && !halted;
            markers.Advance(deltaTime, alive ? source : null, active);
        }

        // 5차 녹색 빛·'잠금 해제!': 던전 HUD 판정 && 패배 정지 아님(던전 성공으로 SECTOR CLEAR가 이어지는 중에는 마저 보임). 원본이 사라져도 남은 재생은 보인다.
        if (unlockBurst != null)
        {
            bool burstShow = DungeonHudGate.ShouldShowDungeonUi(HudGroupOverride) && (!halted || sectorPending || state == BandState.SectorClear);
            unlockBurst.Advance(deltaTime, burstShow, shiftNow);
        }

        // 7차 V8 난이도 상승 계기: 던전 HUD 판정 && 패배 정지 아님(던전 성공 SECTOR CLEAR가 이어지는 중에는 마저). 패배면 바로 끈다.
        // 컷씬·HUD 숨김·일시정지(현장 단말기 포함)면 시간표를 멈췄다가 다시 보이면 이어서 진행한다.
        if (threatRise != null)
        {
            bool hud = DungeonHudGate.ShouldShowDungeonUi(HudGroupOverride);
            bool success = sectorPending || state == BandState.SectorClear;
            if (halted && !success && threatRise.IsPlaying) threatRise.Stop();
            bool paused = !hud || (CutsceneOverride ?? DungeonCutsceneDirector.AnyPlaying) || (PauseOverride ?? GamePaused());
            threatRise.Advance(deltaTime, hud && (!halted || success), paused);
        }

        Apply();
    }

    // 지금 상태의 모습을 그린다.
    private void Apply()
    {
        float t = clock - stateStart;
        if (bandGroup != null && !Mathf.Approximately(bandGroup.alpha, bandAlpha)) bandGroup.alpha = bandAlpha;

        bool opening = state == BandState.Hostile || state == BandState.Patrol;
        float intro = opening ? TrainingBoardMotion.Seg(t, 0f, introTime) : 1f;
        float kill = state == BandState.Hostile ? 1f - TrainingBoardMotion.Seg(clock - killPulseStart, 0f, killPulseTime) : 0f;
        bool clearing = state == BandState.RoomClear || state == BandState.SectorClear || state == BandState.ExitUnlocked || state == BandState.Threat || state == BandState.Objective;
        float clearFlash = clearing ? TrainingBoardMotion.Seg(t, 0f, .04f) * (1f - TrainingBoardMotion.Seg(t, .06f, clearFlashTime)) : 0f;

        if (band != null)
        {
            // 세로로는 커지지 않는다(가로 늘림·색·발광만). 순찰 띠는 사라질 때 가로로 접힌다.
            float sx;
            if (opening)
            {
                sx = Mathf.Lerp(.55f, 1f, TrainingBoardMotion.OutBack(intro)) * (1f + .05f * kill);
                if (state == BandState.Patrol && fadeStart >= 0f) sx *= Mathf.Lerp(1f, .55f, TrainingBoardMotion.Seg(clock, fadeStart, fadeStart + bandFadeOut));
            }
            else sx = clearing ? 1f + .08f * TrainingBoardMotion.Pulse(t, 0f, .3f) : 1f;
            band.localScale = new Vector3(sx, 1f, 1f);
            band.anchoredPosition = bandBase + new Vector2(shiftNow, 0f);
        }

        Color tone = state switch
        {
            BandState.RoomClear => clearColor,
            BandState.SectorClear => sectorColor,
            BandState.Patrol => patrolColor,
            BandState.ExitUnlocked => exitColor,
            BandState.Objective => exitColor,
            BandState.Threat => DungeonThreatText.Color(threatShown),
            _ => hostileColor,
        };

        if (flash != null)
        {
            Color f = hostileColor;
            f.a = state == BandState.Hostile ? Mathf.Max(.8f * (1f - intro), .55f * kill) : 0f;
            flash.color = f;
        }
        if (sweep != null)
        {
            Color s = tone;
            s.a = .9f * clearFlash;
            sweep.color = s;
        }
        if (icon != null)
        {
            Color c = state == BandState.Patrol ? patrolColor : hostileColor;
            c.a = state == BandState.Hostile || state == BandState.Patrol ? 1f : 0f;
            icon.color = c;
        }
        if (labelText != null) labelText.color = Color.Lerp(tone, Color.white, .55f * clearFlash);
        if (countText != null)
        {
            bool showCount = state == BandState.Hostile && shownRemaining != 1;
            Color c = Color.Lerp(hostileColor, Color.white, kill);
            c.a = showCount ? 1f : 0f;
            countText.color = c;
            countText.rectTransform.localScale = Vector3.one * (1f + .45f * kill);
        }
    }

    // 일시정지(일시정지 메뉴·현장 단말기 — GameTimeController.IsPaused, 읽기만).
    private static bool GamePaused()
    {
        var timeController = GameTimeController.Instance;
        return timeController != null && timeController.IsPaused;
    }

    private bool BossBarVisible()
    {
        if (BossBarOverride.HasValue) return BossBarOverride.Value;
        if (stageHud == null)
        {
            float now = Time.realtimeSinceStartup;
            if (now >= nextStageSearch)
            {
                nextStageSearch = now + 1f;
                stageHud = FindFirstObjectByType<StageHUD>();
            }
        }
        if (stageHud != null) return stageHud.isActiveAndEnabled && stageHud.CurrentBoss != null;
        return SourceAlive() && source.IsBossRoom && state == BandState.Hostile;
    }

    private bool SourceAlive()
    {
        if (source == null) return false;
        // 원본이 파괴된 Unity 객체(DungeonManager)면 끊는다.
        if (source is UnityEngine.Object unityObject && unityObject == null)
        {
            source = null;
            return false;
        }
        return true;
    }
}
