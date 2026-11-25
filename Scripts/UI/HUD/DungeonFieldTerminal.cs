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
/// - 5차(2026-11-25, DESIGN_V5 §1 S9): 큰 지도 표식은 배지형 픽토그램(크레인 2종·점프대 포함), 지금 방 = 굵은 밝은 테두리 + 꺾쇠 4개 + 위 표지 삼각형 + '현재 방' 글자
///   (Current 층 — 지도를 다시 만들 때만 Mesh·글자 자리, 열려 있는 동안 currentPulsePeriod로 알파 맥동, 일시정지 중 실제 시간), 지도를 옮겨 지금 방이 영역 밖이면
///   가장자리 화살. Player 표식은 빛 고리 + 바라보는 쪽 꼬리(원본이 IDungeonPlayerHeadingSource도 구현하면 — 선택 인터페이스 — localScale.x ±1). 범례 9칸.
/// Prefab(Assets/Resources/UI/DungeonFieldTerminal.prefab)은 tools/Unity/DungeonFieldTerminalSetup.cs가 만든다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonFieldTerminal : MonoBehaviour, IDungeonMapView, IDungeonDetailView, IDungeonObjectiveView, IDungeonThreatView, IUIInputReceiver
{
    public const string ResourcePath = "UI/DungeonFieldTerminal";
    public const float OpenDuration = .38f;
    public const float CloseDuration = .24f;

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
    [Tooltip("5차 지금 방 테두리 맥동 주기(초, 실제 시간)와 최저 알파")]
    [SerializeField, Min(.1f)] private float currentPulsePeriod = 1.2f;
    [SerializeField, Range(0f, 1f)] private float currentPulseMin = .45f;
    [SerializeField, Min(0f)] private float mapPadding = 18f;
    [Tooltip("단위 칸 한 칸의 최소·최대 px(큰 지도)")]
    [SerializeField, Min(4f)] private float minPitch = 48f;
    [SerializeField, Min(4f)] private float maxPitch = 190f;
    [Tooltip("방향키 한 번의 지도 이동(단위 칸)")]
    [SerializeField, Min(.05f)] private float panStep = .6f;
    [SerializeField, Min(.1f)] private float playerBlinkPeriod = .9f;

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
    [SerializeField] private string footer = "[TAB] 닫기    [ESC] 닫기    [방향키] 지도 이동";
    [Header("4차 글자(정적 글꼴 — DungeonUiText.Glyphs 안)")]
    [SerializeField] private string threatRowLabel = "난이도";
    [SerializeField] private string exitRowLabel = "출구";
    [SerializeField] private string exitLockedLabel = "잠김";
    [SerializeField] private string exitOpenLabel = "개방";
    [SerializeField] private string exitWhereLabel = "시작 방";
    [SerializeField] private string leverLabel = "레버";

    private static readonly Color Dim = new Color32(120, 160, 172, 255);
    private static readonly Color Bright = new Color32(143, 244, 255, 255);

    private IDungeonMapSource source;
    private IDungeonDetailSource detailSource;
    private IDungeonObjectiveSource objectiveSource;
    private IDungeonThreatSource threatSource;
    private IDungeonExitSource exitSource;
    private IDungeonPlayerHeadingSource headingSource;
    private int facing = 1;
    private bool currentLabelVisible;
    private InputAction toggleAction;
    private bool actionEnabled;
    private bool initialized;
    private bool isOpen, isClosing, transitioning, live;
    private float progress, transitionFrom, transitionTo, transitionTime, transitionDuration;
    private Vector2 terminalHome, wristHome;
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
        toggleAction = new InputAction("DungeonFieldTerminal", InputActionType.Button, InputBindingDisplay.InventoryPath);
        toggleAction.performed += HandleToggle;
        if (titleText != null) titleText.text = DungeonUiText.Static(title);
        if (footerText != null) footerText.text = DungeonUiText.Static(footer);
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

    /// <summary>모든 원본 연결을 끊는다(View 인터페이스 공통 — 4차 난이도·출구 포함). 열려 있으면 바로 닫는다.</summary>
    public void Unbind()
    {
        if (initialized && isOpen) CloseImmediately(isActiveAndEnabled);
        if (source != null) source.MapChanged -= MarkDirty;
        if (detailSource != null) detailSource.DetailChanged -= MarkDirty;
        if (objectiveSource != null) objectiveSource.ObjectiveChanged -= MarkDirty;
        if (threatSource != null) threatSource.ThreatChanged -= OnThreatChanged;
        if (exitSource != null) exitSource.ExitChanged -= MarkDirty;
        source = null;
        detailSource = null;
        objectiveSource = null;
        threatSource = null;
        exitSource = null;
        headingSource = null;
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
        PanBy(step * panStep);
    }

    /// <summary>지도를 단위 칸 기준으로 옮긴다(방향키·검증).</summary>
    public void PanBy(Vector2 delta)
    {
        pan += delta;
        RebuildMap();
        // 범위 끝에서 더 밀리지 않게 실제로 적용된 이동량으로 되돌린다.
        if (model != null) pan = view.center - DungeonMapMesh.FitFloor(model, CurrentRoom, MapArea, mapPadding, minPitch, maxPitch).center;
    }

    public void OnConfirm()
    {
        if (!isOpen || isClosing) return;
        pan = Vector2.zero;
        RebuildMap();
    }

    public void OnCancel()
    {
        if (isOpen && !isClosing) Close();
    }

    // ===== 열기·닫기 =====

    /// <summary>지금 열 수 있는지와 그 이유(검증용).</summary>
    public bool CanOpen(out string reason)
    {
        EnsureInit();
        reason = null;
        if (isOpen || transitioning) reason = "이미 열림";
        else if (!FloorActive()) reason = "던전 진행 중이 아님";
        else if (source is IDungeonCombatSource combat && combat.IsHalted) reason = "Wave 진행 정지";
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
    }

    private void Open(bool liveOpen)
    {
        live = liveOpen;
        isOpen = true;
        isClosing = false;
        pan = Vector2.zero;
        if (presentation != null) presentation.SetActive(true);
        RebuildAll();
        if (live)
        {
            UIManager.Instance.SetInputTarget(this, UIInputLayer.Gameplay);
            pauseOwner = GameTimeController.Instance;
            pauseOwner.PushPause();
            ownsPause = true;
        }
        BeginTransition(1f, OpenDuration);
    }

    public void Close()
    {
        if (!isOpen || isClosing) return;
        isClosing = true;
        BeginTransition(0f, CloseDuration);
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

    /// <summary>한 프레임 진행: 던전 진행 중에만 Tab 입력을 켜고, 열린 채 조건이 깨지면 바로 닫은 뒤 연출을 진행한다(Update가 부른다. 검증은 직접 부른다).</summary>
    public void Tick(float deltaTime)
    {
        EnsureInit();
        bool floor = FloorActive();
        SetActionEnabled(floor);
        if (isOpen && live)
        {
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (!floor || player == null || !player.gameObject.activeInHierarchy || player.Health == null || player.Health.currentHP <= 0f
                || DungeonHudGate.IsBlockingScreenOpen())
            {
                CloseImmediately();
                return;
            }
        }
        Advance(deltaTime);
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
            }
        }
        else if (isOpen && fitRect != null && fitRect.rect.size != lastFitSize) ApplyPresentation(progress);
        if (!isOpen) return;
        if (dirty) RebuildAll();
        UpdatePlayer();
    }

    private bool FloorActive() => SourceAlive() && source.HasFloor && source.Rooms != null && source.Rooms.Count > 0;

    private int CurrentRoom => SourceAlive() ? source.CurrentRoom : -1;

    private Rect MapArea => mapGraphic != null ? mapGraphic.rectTransform.rect : new Rect(-420f, -250f, 840f, 500f);

    private void RebuildAll()
    {
        dirty = false;
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
    }

    private void RebuildMap()
    {
        if (mapGraphic == null) return;
        if (!SourceAlive() || source.Rooms == null || !DetailAlive())
        {
            model = null;
            mapGraphic.ClearFloor();
            ClearCurrent();
            return;
        }
        model = DungeonMapMesh.BuildFloorModel(source.Rooms, detailSource, silhouettes);
        view = DungeonMapMesh.FitFloor(model, CurrentRoom, MapArea, mapPadding, minPitch, maxPitch, pan);
        mapGraphic.SetFloor(model, CurrentRoom, view, detailSource, ObjectiveAlive() ? objectiveSource.ObjectiveMarkers : null);
        RebuildCurrent();
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
    }

    // ===== 글자 =====

    private void RefreshTexts()
    {
        if (contextText != null) contextText.text = BuildContext();
        RefreshTraining();
        RefreshObjective();
    }

    private string BuildContext()
    {
        builder.Clear();
        builder.AppendStatic(context);
        if (SourceAlive() && source.HasFloor)
        {
            builder.Append(DungeonUiText.SpaceTag).Append(DungeonUiText.SpaceTag)
                   .Append('F').Append(Mathf.Max(1, source.FloorNumber)).Append('/').Append(Mathf.Max(1, source.FloorCount));
            CountRooms(out int cleared, out int total);
            builder.Append(DungeonUiText.SpaceTag).Append(DungeonUiText.SpaceTag).AppendStatic("구역 클리어 ").Append(cleared).Append('/').Append(total);
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
        if (source is Object unityObject && unityObject == null) { source = null; return false; }
        return true;
    }

    private bool DetailAlive()
    {
        if (detailSource == null) return false;
        if (detailSource is Object unityObject && unityObject == null) { detailSource = null; return false; }
        return true;
    }

    private bool ObjectiveAlive()
    {
        if (objectiveSource == null) return false;
        if (objectiveSource is Object unityObject && unityObject == null) { objectiveSource = null; return false; }
        return true;
    }

    private bool ThreatAlive()
    {
        if (threatSource == null) return false;
        if (threatSource is Object unityObject && unityObject == null) { threatSource = null; return false; }
        return true;
    }

    private bool ExitAlive()
    {
        if (exitSource == null) return false;
        if (exitSource is Object unityObject && unityObject == null) { exitSource = null; return false; }
        return true;
    }
}
