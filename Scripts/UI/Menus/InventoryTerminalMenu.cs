using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 손목 단말기 시안의 표시와 입력만 담당한다. 해금·거래·저장·방 이동은 수행하지 않는다.
[DisallowMultipleComponent]
public sealed class InventoryTerminalMenu : SelectableMenuUI
{
    [Serializable]
    public sealed class EquipmentGuide
    {
        public PlayerArmamentSO armament;
        public WeaponBaseSO weapon;
        public int parent = -1;
        public bool passive;
        public WeaponBaseSO[] videoRequirements;
        public UnityEngine.Video.VideoClip alternateVideo;
        public GameObject[] videoPresentation;
        public GameObject textGuide;
    }

    [Serializable]
    public sealed class SectionBinding
    {
        public GameObject root;
        public RectTransform listRoot;
        public UnityEngine.UI.Button[] entryButtons;
        public UnityEngine.UI.Image[] entryHighlights;
        public GameObject[] details;
        public UnityEngine.UI.Button[] detailActions;
        public bool inlineInformation;
        public GameObject[] inlineDetails;
        public UnityEngine.UI.ScrollRect entryScroll;
        [Min(0f), Tooltip("첫 행을 자동으로 보일 때 함께 드러낼 목록 위 제목 영역의 높이입니다.")]
        public float firstRowTopInset;
        public int columns = 1;
        public bool horizontalList;
        public bool[] entryAvailable;
        public UnityEngine.UI.Button[] popupButtons;
        public bool[] popupAvailable;
        [Tooltip("목록 옆 요약 정보를 복제해 항목 선택 시 단말기 위 팝업으로 띄웁니다.")]
        public bool popupFromInline;
        public TextMeshProUGUI visibleCount;
        public GameObject emptyState;
        public EquipmentGuide[] equipmentGuides;
    }

    [Header("Availability")]
    [SerializeField] private Room lobbyRoom;

    [Header("Presentation")]
    [SerializeField] private GameObject rootObject;
    [SerializeField] private RectTransform fitRect;
    [SerializeField] private RectTransform terminalRoot;
    [SerializeField] private TitlePanelMotion panelMotion;
    [SerializeField] private CanvasGroup terminalGroup;
    [SerializeField] private CanvasGroup screenGroup;
    [SerializeField] private CanvasGroup backdropGroup;

    [Header("Home")]
    [SerializeField] private RectTransform homeRoot;
    [SerializeField] private GameObject catalogNavigation;
    [SerializeField] private UnityEngine.UI.Button[] homeButtons;
    [SerializeField] private UnityEngine.UI.Button homeNavigationButton;
    [SerializeField] private UnityEngine.UI.Image homeProgressFill;
    [SerializeField] private TextMeshProUGUI homeProgressText;
    [SerializeField] private TextMeshProUGUI homeTrainingText;
    [SerializeField] private TextMeshProUGUI homeGoldText;
    [SerializeField] private TrainingDefinitionSO[] homeTrainingDefinitions;
    [SerializeField] private UnityEngine.UI.Graphic[] homeLaserSignals;

    [Header("Training Goals")]
    [Tooltip("훈련 목표 분류의 항목 순서와 같은 훈련입니다. 세이브의 기록으로 수료 여부를 항목에 보여 줍니다.")]
    [SerializeField] private TrainingDefinitionSO[] goalTrainings;
    [SerializeField] private TextMeshProUGUI[] goalStateLabels;
    [SerializeField] private TextMeshProUGUI[] goalGradeLabels;
    [SerializeField] private TextMeshProUGUI[] goalBestTimeLabels;
    [SerializeField] private TextMeshProUGUI[] goalBestHitLabels;
    [SerializeField] private string[] codexEnemyIds;
    [Tooltip("수료한 항목에만 켜는 표시와 수료 전의 항목에만 켜는 표시(항목 순서)")]
    [SerializeField] private GameObject[] goalClearedMarks;
    [SerializeField] private GameObject[] goalPendingMarks;
    [SerializeField] private string goalClearedText = "수료";
    [SerializeField] private string goalPendingText = "미수료";
    [Tooltip("수료한 항목의 상태 글자색입니다. 수료 전에는 Prefab에 저장된 글자색 그대로 둡니다.")]
    [SerializeField] private Color goalClearedColor = new Color(.396f, .839f, .627f, 1f);

    [Header("Screen Machinery")]
    [SerializeField] private RectTransform[] rotatingGears;
    [SerializeField] private UnityEngine.UI.RawImage[] flowingCables;

    [Header("Sections")]
    [SerializeField] private RectTransform contentViewport;
    [SerializeField] private UnityEngine.UI.Button[] tabButtons;
    [SerializeField] private UnityEngine.UI.Image[] tabHighlights;
    [SerializeField] private SectionBinding[] sections;

    [Header("Preview Notice")]
    [SerializeField] private GameObject noticeRoot;
    [SerializeField] private TextMeshProUGUI noticeText;

    [Header("Popup Layer")]
    [Tooltip("단말기 화면 밖, 단말기 위에서 상세창·안내창을 띄우는 층입니다. 비어 있으면 단말기 화면 안에 띄웁니다.")]
    [SerializeField] private CanvasGroup popupLayerGroup;
    [Tooltip("상세창을 놓는 무대입니다. 단말기와 같은 배율로 화면에 맞춥니다.")]
    [SerializeField] private RectTransform popupStage;
    [Tooltip("팝업 뒤 화면 전체를 어둡게 하는 막입니다. 누르면 팝업을 닫습니다.")]
    [SerializeField] private UnityEngine.UI.Graphic popupDimmer;
    [SerializeField, Min(.5f)] private float inlinePopupScale = 1.5f;

    private const float OpenDuration = TitlePanelMotion.OpenDuration;
    private const float CloseDuration = TitlePanelMotion.CloseDuration;
    private const float PageDuration = .52f;
    private const float TabInputInterval = .2f;
    private const int EquipmentTab = 2;
    private readonly int[] selectedEntries = new int[4];
    private readonly List<Action> unbindButtons = new List<Action>();
    private readonly Dictionary<UnityEngine.UI.Button, bool> detailActionAvailability = new Dictionary<UnityEngine.UI.Button, bool>();
    private readonly Dictionary<UnityEngine.UI.Button, bool> entryAvailability = new Dictionary<UnityEngine.UI.Button, bool>();
    private readonly Dictionary<RectTransform, Vector2> pageHomes = new Dictionary<RectTransform, Vector2>();
    private readonly Dictionary<UnityEngine.UI.Image, Color> highlightColors = new Dictionary<UnityEngine.UI.Image, Color>();
    private readonly Dictionary<RectTransform, Vector3> pageScales = new Dictionary<RectTransform, Vector3>();
    private readonly Dictionary<RectTransform, Quaternion> pageRotations = new Dictionary<RectTransform, Quaternion>();
    private readonly Dictionary<RectTransform, CanvasGroup> pageGroups = new Dictionary<RectTransform, CanvasGroup>();
    private readonly Dictionary<UnityEngine.UI.Button, UnityEngine.UI.Graphic> buttonFocus = new Dictionary<UnityEngine.UI.Button, UnityEngine.UI.Graphic>();
    private readonly Dictionary<GameObject, GameObject> inlinePopupSources = new Dictionary<GameObject, GameObject>();
    private float popupVisibility;
    private UnityEngine.UI.Button hoveredButton;
    private InputAction inventoryAction;
    private Coroutine transitionRoutine;
    private Coroutine pageRoutine;
    private Coroutine homeRoutine;
    private CanvasGroup contentInputGroup;
    private CanvasGroup homeGroup, navigationGroup;
    private Vector3 homeScale, catalogScale;
    private DataManager homeDataManager;
    private Color[] homeLaserColors;
    private readonly HashSet<string> homeKnownTrainingIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> homeClearedTrainingIds = new HashSet<string>(StringComparer.Ordinal);
    private EventSystem inputEventSystem;
    private bool previousNavigationEvents;
    private GameTimeController pauseOwner;
    private Vector2 lastFitSize;
    private float presentationProgress;
    private float nextTabInputAt;
    private int selectedTab;
    private int selectedHomeIndex;
    private bool homeVisible, isHomeTransitioning;
    private bool listFocused, initialized, isTransitioning, isClosing, ownsPause;
    [SerializeField] private bool equipmentUsesGeometry;
    private bool detailOpen, isPageTransitioning, isCategoryTransitioning, contentInteractable, contentRaycasts;
    private int equipmentLesson = -1, equipmentFocus = -1;
    private int DetailIndex => selectedTab == EquipmentTab && equipmentLesson >= 0 ? equipmentLesson : SelectedEntry;

    /// <summary>단말기에서 하는 조작. 안내가 받을 조작을 가릴 때 쓴다(ActionGuard).</summary>
    public enum GuardedAction { Open, Move, OpenCategory, SelectTab, ReturnHome, SelectEntry, ShowDetails, Back, Close }
    /// <summary>조작을 받을지 묻는다(false를 돌려주면 그 조작을 받지 않는다. 소리도 내지 않는다). 두 번째 값은 대상의 순서(분류·항목)이고 없으면 -1이다.
    /// Lobby 안내가 메일을 확인할 때까지 정해진 조작만 받게 할 때 쓴다. 방 이동·사망 때의 강제 닫기는 막지 않는다.</summary>
    public Func<GuardedAction, int, bool> ActionGuard;
    /// <summary>분류를 열었을 때 바로 골라 둘 항목(분류 → 항목, 없으면 -1). 안내가 환영 메일을 가리킬 때 쓴다.</summary>
    public Func<int, int> GuidedEntry;
    /// <summary>게임 입력이 막혀 있어도 단말기를 열 수 있는가(안내가 단말기 여는 키만 남기고 입력을 막아 둔 동안).</summary>
    public Func<bool> OpenWhileGameplayBlocked;
    /// <summary>항목의 상세를 열었다(탭, 항목).</summary>
    public event Action<int, int> EntryDetailsShown;
    /// <summary>열었을 때 첫 화면에서 먼저 고를 항목(비우면 0번). Lobby 안내가 메시지 수신함을 가리킬 때 쓴다.</summary>
    public Func<int> OpeningHomeIndex;
    public bool IsTransitioning => isTransitioning || isPageTransitioning || isHomeTransitioning;
    internal RectTransform Viewport => fitRect;
    public bool IsHomeVisible => IsActive && homeVisible;
    public bool IsHomeTransitioning => isHomeTransitioning;
    public int SelectedHomeIndex => selectedHomeIndex;
    public int HomeTrainingTotal { get; private set; }
    public int HomeTrainingCleared { get; private set; }
    public float HomeProgressNormalized { get; private set; }
    public bool IsDetailOpen => detailOpen;
    public bool IsPageTransitioning => isPageTransitioning && isCategoryTransitioning;
    public bool IsPopupTransitioning => isPageTransitioning && !isCategoryTransitioning;
    public int SelectedTab => selectedTab;
    public int SelectedEntry => selectedEntries[selectedTab];
    public bool IsListFocused => listFocused;
    public bool IsInlineInformationVisible => IsActive && !homeVisible && !detailOpen && sections[selectedTab].inlineInformation;
    private bool NoticeVisible => noticeRoot != null && noticeRoot.activeSelf;
    // 자유 훈련실도 Lobby 분류를 사용하므로 실제로 지정된 Lobby 인스턴스만 허용한다.
    // 최초 시작 도입부의 '가짜 Lobby'(위장한 Lobby)에서는 단말기를 열지 않는다.
    private bool IsInLobby => lobbyRoom != null && RoomManager.Instance != null && RoomManager.Instance.CurrentRoom == lobbyRoom && !LobbyDisguise.IsActive;
    private bool IsTopInput => IsActive && isActiveAndEnabled && UIManager.Instance != null
        && IsInLobby && !UIManager.Instance.IsUIInputBlocked && UIManager.Instance.IsCurrentInputTarget(this);
    private bool CanReceiveInput => IsTopInput && !IsTransitioning && !isClosing;
    private bool CanReceiveTabInput => IsTopInput && !isTransitioning && !isHomeTransitioning && !isClosing;
    public bool CanControlTutorialVideo => CanReceiveInput && detailOpen && selectedTab == 2 && !NoticeVisible;
    private bool Allowed(GuardedAction action, int index = -1) => ActionGuard == null || ActionGuard(action, index);

    protected override void Awake()
    {
        initialized = ValidateBindings();
        if (!initialized)
        {
            Debug.LogError("인벤토리 단말기의 화면·4개 탭·목록/상세 참조가 필요합니다.", this);
            return;
        }
        var screen = terminalRoot.Find("ScreenClip");
        InputBindingHint.Bind(screen, "Header/Close/Label", "{Cancel} 닫기");
        InputBindingHint.Bind(screen, "Footer/InputHint", "{MenuDirections} 이동 · {Confirm} 선택 · {Cancel} 뒤로 · {Inventory} 닫기");
        InputBindingHint.Bind(transform, "Onboarding/TerminalIcon/KeyHint/Key", "{Inventory}");
        InputBindingHint.Bind(transform, "Onboarding/OpenPrompt/KeyGroup/Key", "{Inventory}");
        contentInputGroup = contentViewport.GetComponent<CanvasGroup>();
        if (contentInputGroup == null) contentInputGroup = contentViewport.gameObject.AddComponent<CanvasGroup>();
        contentInteractable = contentInputGroup.interactable;
        contentRaycasts = contentInputGroup.blocksRaycasts;
        homeGroup = homeRoot.GetComponent<CanvasGroup>();
        if (homeGroup == null) homeGroup = homeRoot.gameObject.AddComponent<CanvasGroup>();
        navigationGroup = catalogNavigation.GetComponent<CanvasGroup>();
        if (navigationGroup == null) navigationGroup = catalogNavigation.AddComponent<CanvasGroup>();
        homeScale = homeRoot.localScale;
        catalogScale = contentViewport.localScale;
        homeDataManager = UnityEngine.Object.FindFirstObjectByType<DataManager>();
        homeLaserSignals = homeLaserSignals ?? Array.Empty<UnityEngine.UI.Graphic>();
        homeLaserColors = Array.ConvertAll(homeLaserSignals, signal => signal != null ? signal.color : Color.clear);
        CreateInlinePopups();
        foreach (var section in sections)
        {
            CachePage(section.listRoot);
            foreach (var detail in section.details)
            {
                if (detail == null) continue;
                var page = detail.GetComponent<RectTransform>();
                CachePage(page);
            }
        }

        // 표준 Button을 직접 연결한다. 기반 클래스의 EventTrigger와 중복 등록하지 않는다.
        buttons = new RectTransform[tabButtons.Length];
        images = tabHighlights;
        texts = Array.Empty<TextMeshProUGUI>();
        for (int tab = 0; tab < tabButtons.Length; tab++)
        {
            int tabIndex = tab;
            highlightColors[tabHighlights[tab]] = tabHighlights[tab].color;
            buttons[tab] = tabButtons[tab].transform as RectTransform;
            BindClick(tabButtons[tab], () => SelectTab(tabIndex));
            for (int entry = 0; entry < sections[tab].entryButtons.Length; entry++)
            {
                int entryIndex = entry;
                var button = sections[tab].entryButtons[entry];
                entryAvailability[button] = button.interactable;
                highlightColors[sections[tab].entryHighlights[entry]] = sections[tab].entryHighlights[entry].color;
                if (tab == EquipmentTab && EquipmentParent(sections[tab], entry) >= 0)
                {
                    BindClick(button, () => OpenEquipmentLesson(entryIndex));
                    BindHover(button, () => SelectEquipmentLesson(entryIndex));
                }
                else
                {
                    BindClick(button, () => SelectEntryFromPointer(tabIndex, entryIndex, true));
                    BindHover(button, () => SelectEntryFromPointer(tabIndex, entryIndex, false));
                }
            }
            var popupButtons = sections[tab].popupButtons;
            if (popupButtons != null)
                for (int entry = 0; entry < popupButtons.Length; entry++)
                {
                    var popup = popupButtons[entry];
                    if (popup == null) continue;
                    int entryIndex = entry;
                    BindClick(popup, () => { if (selectedTab == tabIndex) ShowEntryDetails(entryIndex); });
                    if (!detailActionAvailability.ContainsKey(popup)) detailActionAvailability.Add(popup, popup.interactable);
                }
            if (sections[tab].detailActions == null) continue;
            foreach (var action in sections[tab].detailActions)
                if (action != null && !detailActionAvailability.ContainsKey(action)) detailActionAvailability.Add(action, action.interactable);
        }
        for (int index = 0; index < homeButtons.Length; index++)
        {
            int category = index;
            BindClick(homeButtons[index], () => OpenCategory(category));
            BindHover(homeButtons[index], () =>
            {
                if (!CanReceiveInput || !homeVisible || !Allowed(GuardedAction.Move)) return;
                if (selectedHomeIndex != category) PlayFeedback(moveFeedback);
                selectedHomeIndex = category;
                RefreshSelection();
            });
        }
        BindClick(homeNavigationButton, ReturnHome);
        if (popupDimmer != null) BindPointerClick(popupDimmer.gameObject, HandlePopupBackdropClick);
        foreach (var button in GetComponentsInChildren<UnityEngine.UI.Button>(true))
        {
            if (button.GetComponent<TerminalPlaybackButton>() == null)
            {
                var focus = button.transform.Find("SelectionFill");
                if (focus != null) buttonFocus[button] = focus.GetComponent<UnityEngine.UI.Graphic>();
            }
            BindHover(button, () =>
            {
                if (!CanHighlight(button)) return;
                hoveredButton = button;
                RefreshButtonFaces();
            });
            BindPointerExit(button);
        }
        foreach (var trigger in GetComponentsInChildren<EventTrigger>(true))
            if (trigger.GetComponentInParent<UnityEngine.UI.ScrollRect>(true) != null && trigger.GetComponent<MenuScrollRelay>() == null)
                trigger.gameObject.AddComponent<MenuScrollRelay>();
        inventoryAction = new InputAction("Inventory", InputActionType.Button, InputBindingDisplay.InventoryPath);
        inventoryAction.performed += HandleInventoryInput;
        noticeRoot.SetActive(false);
        homeVisible = true;
        NormalizePages();
        ApplyPresentation(0f);
        rootObject.SetActive(false);
    }

    // 목록 옆 요약 정보를 복제해 단말기 위 팝업으로 쓴다. 원본은 목록 옆에 그대로 두고, 열 때마다 글자를 다시 맞춘다.
    private void CreateInlinePopups()
    {
        if (popupStage == null) return;
        foreach (var section in sections)
        {
            if (!section.popupFromInline || section.inlineDetails == null) continue;
            if (section.popupAvailable == null || section.popupAvailable.Length != section.entryButtons.Length)
                Array.Resize(ref section.popupAvailable, section.entryButtons.Length);
            // 다른 분류의 DetailArea처럼 확대는 묶음 영역이 맡고, 각 팝업은 원래 크기를 기준으로 열고 닫는다.
            RectTransform area = null;
            for (int index = 0; index < section.inlineDetails.Length; index++)
            {
                var source = section.inlineDetails[index];
                if (source == null || section.details[index] != null) continue;
                if (area == null)
                {
                    area = new GameObject((section.root != null ? section.root.name : "Inline") + "PopupArea", typeof(RectTransform)).GetComponent<RectTransform>();
                    area.SetParent(popupStage, false);
                    area.anchorMin = area.anchorMax = area.pivot = new Vector2(.5f, .5f);
                    area.sizeDelta = Vector2.zero;
                    area.localScale = Vector3.one * inlinePopupScale;
                    area.SetSiblingIndex(0);
                }
                var copy = Instantiate(source, area, false);
                copy.name = source.name + "Popup";
                var rect = (RectTransform)copy.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                copy.SetActive(false);
                section.details[index] = copy;
                section.popupAvailable[index] = true;
                inlinePopupSources[copy] = source;
            }
        }
    }

    private static void CopyTexts(GameObject source, GameObject target)
    {
        var from = source.GetComponentsInChildren<TextMeshProUGUI>(true);
        var to = target.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int index = 0; index < Mathf.Min(from.Length, to.Length); index++)
            if (to[index].text != from[index].text) to[index].text = from[index].text;
    }

    private void CachePage(RectTransform page)
    {
        pageHomes[page] = page.anchoredPosition;
        pageScales[page] = page.localScale;
        pageRotations[page] = page.localRotation;
        var group = page.GetComponent<CanvasGroup>();
        if (group == null) group = page.gameObject.AddComponent<CanvasGroup>();
        pageGroups[page] = group;
    }

    private bool CanHighlight(UnityEngine.UI.Button button)
    {
        if (!IsTopInput || isClosing || isTransitioning || !button.interactable) return false;
        return !NoticeVisible || button.transform.IsChildOf(noticeRoot.transform);
    }

    private void BindPointerExit(UnityEngine.UI.Button button)
    {
        var trigger = button.GetComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        entry.callback.AddListener(_ =>
        {
            if (hoveredButton != button) return;
            hoveredButton = null;
            RefreshButtonFaces();
        });
        trigger.triggers.Add(entry);
        unbindButtons.Add(() => { if (trigger != null) trigger.triggers.Remove(entry); });
    }

    private bool ValidateBindings()
    {
        if (rootObject == null || transform.IsChildOf(rootObject.transform) || fitRect == null || terminalRoot == null
            || terminalGroup == null || screenGroup == null || backdropGroup == null || contentViewport == null
            || noticeRoot == null || noticeText == null || tabButtons == null || tabHighlights == null
            || sections == null || tabButtons.Length != 4 || tabHighlights.Length != 4 || sections.Length != 4
            || homeRoot == null || catalogNavigation == null || homeButtons == null || homeButtons.Length != 4
            || homeNavigationButton == null || homeProgressFill == null || homeProgressText == null
            || homeTrainingText == null || homeGoldText == null)
            return false;
        foreach (var button in homeButtons) if (button == null) return false;
        for (int tab = 0; tab < 4; tab++)
        {
            var section = sections[tab];
            if (tabButtons[tab] == null || tabHighlights[tab] == null || section == null || section.root == null || section.listRoot == null
                || section.entryButtons == null || section.entryHighlights == null || section.details == null
                || section.entryButtons.Length < 1
                || section.entryHighlights.Length != section.entryButtons.Length || section.details.Length != section.entryButtons.Length)
                return false;
            if (section.inlineInformation && (section.inlineDetails == null || section.inlineDetails.Length != section.entryButtons.Length)) return false;
            for (int entry = 0; entry < section.entryButtons.Length; entry++)
                if (section.entryButtons[entry] == null || section.entryHighlights[entry] == null
                    || section.details[entry] != null && section.details[entry].GetComponent<RectTransform>() == null
                    || !section.inlineInformation && DeclaredEntryAvailable(section, entry) && section.details[entry] == null
                    || section.inlineInformation && DeclaredEntryAvailable(section, entry) && section.inlineDetails[entry] == null)
                    return false;
        }
        return true;
    }

    private void BindClick(UnityEngine.UI.Button button, UnityAction action)
    {
        button.onClick.AddListener(action);
        unbindButtons.Add(() => { if (button != null) button.onClick.RemoveListener(action); });
    }
    private void BindPointerClick(GameObject target, Action action)
    {
        var trigger = target.GetComponent<EventTrigger>();
        if (trigger == null) trigger = target.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
        unbindButtons.Add(() => { if (trigger != null) trigger.triggers.Remove(entry); });
    }
    private void BindHover(UnityEngine.UI.Button button, Action action)
    {
        var trigger = button.GetComponent<EventTrigger>();
        if (trigger == null) trigger = button.gameObject.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
        unbindButtons.Add(() => { if (trigger != null) trigger.triggers.Remove(entry); });
    }
    private void OnEnable()
    {
        inventoryAction?.Enable();
        ArmamentUnlocks.Changed += RefreshEquipmentAfterUnlock;
    }
    private void OnDisable()
    {
        ArmamentUnlocks.Changed -= RefreshEquipmentAfterUnlock;
        inventoryAction?.Disable();
        CloseImmediately();
    }
    private void OnDestroy()
    {
        CloseImmediately();
        foreach (var unbind in unbindButtons) unbind();
        unbindButtons.Clear();
        if (inventoryAction == null) return;
        inventoryAction.performed -= HandleInventoryInput;
        inventoryAction.Dispose();
    }
    private void Update()
    {
        if (!initialized) return;
        if (fitRect.rect.size != lastFitSize) ApplyPresentation(presentationProgress);
        if (IsActive)
        {
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (!IsInLobby || player == null || !player.gameObject.activeInHierarchy || player.Health.currentHP <= 0f)
            {
                CloseImmediately();
                return;
            }
        }
        if (homeVisible && IsActive && !isClosing)
        {
            RefreshHomeStatistics();
            AnimateHomeSignals();
        }
        // 영상 확인키는 공통 방향 입력 잠금 직후에도 소비한다. 같은 입력은 한 번만 처리된다.
        if (CanReceiveInput && detailOpen && selectedTab == 2 && !NoticeVisible
            && InputManager.MenuConfirmPressed()) OnConfirm();
        AnimateScreenMachinery();
        RefreshInteractable();
    }

    private void RefreshCatalogProgress()
    {
        if (homeDataManager == null) homeDataManager = FindFirstObjectByType<DataManager>();
        var catalog = TrainingCatalogSO.Load();

        var goals = sections[0];
        if (goalTrainings != null && goalTrainings.Length == goals.entryButtons.Length)
        {
            int visible = 0;
            for (int index = 0; index < goalTrainings.Length; index++)
            {
                var training = goalTrainings[index];
                var entry = training != null ? catalog?.Find(training.trainingId) : null;
                bool available = entry?.waveData?.waves?.Length > 0 && !entry.alwaysAvailable && catalog.IsUnlocked(entry, homeDataManager);
                goals.entryButtons[index].gameObject.SetActive(available);
                if (available) visible++;
            }
            UpdateFilteredList(goals, visible, "과정");
        }

        var enemies = sections[1];
        if (codexEnemyIds != null && codexEnemyIds.Length == enemies.entryButtons.Length)
        {
            int visible = 0;
            for (int index = 0; index < codexEnemyIds.Length; index++)
            {
                bool encountered = homeDataManager != null && homeDataManager.HasEncounteredEnemy(codexEnemyIds[index]);
                enemies.entryButtons[index].gameObject.SetActive(encountered);
                if (encountered) visible++;
            }
            UpdateFilteredList(enemies, visible, "기록");
        }
        RefreshEquipmentGuides();
    }

    private static void UpdateFilteredList(SectionBinding section, int visible, string unit)
    {
        SetHomeText(section.visibleCount, $"{visible} {unit}");
        if (section.emptyState != null) section.emptyState.SetActive(visible == 0);
        var scroll = section.entryScroll;
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;
        var layout = scroll.content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (layout == null) return;
        float height = layout.padding.vertical + Mathf.Max(0, visible - 1) * layout.spacing;
        foreach (var button in section.entryButtons)
        {
            if (button.gameObject.activeSelf) height += ((RectTransform)button.transform).rect.height;
        }
        scroll.StopMovement();
        scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(scroll.viewport.rect.height, height));
        scroll.content.anchoredPosition = Vector2.zero;
        scroll.verticalNormalizedPosition = 1f;
    }

    private static int EquipmentParent(SectionBinding section, int index) =>
        section.equipmentGuides != null && index >= 0 && index < section.equipmentGuides.Length
            ? section.equipmentGuides[index].parent : -1;

    private void RefreshEquipmentGuides()
    {
        var section = sections[EquipmentTab];
        var guides = section.equipmentGuides;
        if (guides == null || guides.Length == 0) return;
        var content = section.entryScroll.content;
        var techniqueCounts = new int[guides.Length];
        int common = 0, armaments = 0;
        var secondary = new List<RectTransform>();
        for (int i = 0; i < guides.Length; i++)
        {
            var guide = guides[i];
            var row = (RectTransform)section.entryButtons[i].transform;
            bool unlocked = (guide.armament == null || ArmamentUnlocks.IsArmamentUnlocked(guide.armament))
                && (guide.weapon == null || ArmamentUnlocks.IsWeaponUnlocked(guide.weapon));
            row.gameObject.SetActive(unlocked);
            if (!unlocked)
            {
                section.details[i].SetActive(false);
                continue;
            }
            if (guide.videoRequirements != null && guide.videoRequirements.Length > 0)
            {
                bool full = Array.TrueForAll(guide.videoRequirements, ArmamentUnlocks.IsWeaponUnlocked);
                if (guide.alternateVideo != null)
                    section.details[i].GetComponentInChildren<TerminalTutorialVideo>(true).SetAlternativeClip(full ? null : guide.alternateVideo);
                if (guide.textGuide != null)
                {
                    foreach (var element in guide.videoPresentation) element.SetActive(full);
                    guide.textGuide.SetActive(!full);
                }
            }
            if (guide.parent >= 0)
            {
                if (!guide.passive)
                {
                    int order = techniqueCounts[guide.parent]++;
                    row.anchoredPosition = new Vector2(order % 2 == 0 ? -233f : 233f, -88f - order / 2 * 75f);
                }
            }
            else if (guide.armament != null) PlaceEquipmentRow(row, 174f, armaments++);
            else if (guide.weapon != null) secondary.Add(row);
            else PlaceEquipmentRow(row, -498f, common++);
        }

        int secondaryLine = (armaments + 1) / 2 + 1;
        for (int index = 0; index < secondary.Count; index++) PlaceEquipmentRow(secondary[index], 174f, secondaryLine * 2 + index);
        if (content.Find("SupportHeading") is RectTransform supportHeading)
        {
            supportHeading.anchoredPosition = new Vector2(supportHeading.anchoredPosition.x, -37f - secondaryLine * 78f);
            supportHeading.gameObject.SetActive(secondary.Count > 0);
        }
        int rowCount = Mathf.Max((common + 1) / 2, secondary.Count > 0 ? secondaryLine + (secondary.Count + 1) / 2 : (armaments + 1) / 2);
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(section.entryScroll.viewport.rect.height, 85 + rowCount * 78));
        if (content.Find("VerticalDivider") is RectTransform divider)
            divider.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, content.rect.height - 24f);
        SetHomeText(section.visibleCount, $"{common + armaments + secondary.Count} 항목");
    }

    private static void PlaceEquipmentRow(RectTransform row, float startX, int index) =>
        row.anchoredPosition = new Vector2(startX + index % 2 * 324f, -115f - index / 2 * 78f);

    private void RefreshEquipmentAfterUnlock()
    {
        if (!initialized) return;
        RefreshEquipmentGuides();
        var section = sections[EquipmentTab];
        int selected = selectedEntries[EquipmentTab];
        if (!EntryAvailable(section, selected))
        {
            selectedEntries[EquipmentTab] = FirstAvailableEntry(section);
            if (selectedTab == EquipmentTab && detailOpen)
            {
                detailOpen = false;
                equipmentLesson = equipmentFocus = -1;
                CancelSlide();
            }
        }
        else if (selectedTab == EquipmentTab && detailOpen)
        {
            if (equipmentLesson >= 0 && !section.entryButtons[equipmentLesson].gameObject.activeSelf)
            {
                equipmentLesson = -1;
                CancelSlide();
            }
            if (equipmentFocus < 0 || !section.entryButtons[equipmentFocus].gameObject.activeSelf)
                equipmentFocus = FirstEquipmentLesson(selected);
        }
        if (IsActive) RefreshSelection();
    }

    private int FirstEquipmentLesson(int parent)
    {
        var section = sections[EquipmentTab];
        var guides = section.equipmentGuides;
        if (guides == null) return -1;
        for (int i = 0; i < guides.Length; i++)
            if (guides[i].parent == parent && guides[i].passive && section.entryButtons[i].gameObject.activeSelf) return i;
        return -1;
    }

    private bool EquipmentLessonAvailable(SectionBinding section, int index) =>
        selectedTab == EquipmentTab && equipmentLesson < 0 && EquipmentParent(section, index) == SelectedEntry
        && SelectedEntry >= 0 && section.entryButtons[index].gameObject.activeSelf && EntryAvailable(section, SelectedEntry);

    private void SelectEquipmentLesson(int index)
    {
        if (!CanReceiveInput || !detailOpen || NoticeVisible || !EquipmentLessonAvailable(sections[EquipmentTab], index)) return;
        if (equipmentFocus != index) PlayFeedback(moveFeedback);
        equipmentFocus = index;
        RefreshButtonFaces();
    }

    private void OpenEquipmentLesson(int index)
    {
        var section = sections[EquipmentTab];
        if (!CanReceiveInput || !detailOpen || NoticeVisible || !EquipmentLessonAvailable(section, index)
            || !Allowed(GuardedAction.ShowDetails, index)) return;
        equipmentLesson = equipmentFocus = index;
        hoveredButton = null;
        PlayFeedback(confirmFeedback);
        BeginPopup((RectTransform)section.details[index].transform);
        EntryDetailsShown?.Invoke(EquipmentTab, index);
    }

    private void MoveEquipmentLesson(Vector2 direction)
    {
        var section = sections[EquipmentTab];
        bool vertical = Mathf.Abs(direction.y) > .5f;
        if (!vertical && Mathf.Abs(direction.x) <= .5f) return;
        Vector2 origin = ((RectTransform)section.entryButtons[equipmentFocus].transform).anchoredPosition;
        float nearest = float.PositiveInfinity;
        int next = equipmentFocus;
        for (int i = 0; i < section.entryButtons.Length; i++)
        {
            if (!EquipmentLessonAvailable(section, i)) continue;
            Vector2 offset = ((RectTransform)section.entryButtons[i].transform).anchoredPosition - origin;
            float distance = vertical ? offset.y * Mathf.Sign(direction.y) : offset.x * Mathf.Sign(direction.x);
            float side = vertical ? Mathf.Abs(offset.x) : Mathf.Abs(offset.y);
            if (distance < 1f || (!vertical && side > 1f)) continue;
            float score = distance * 100f + side;
            if (score < nearest) { nearest = score; next = i; }
        }
        SelectEquipmentLesson(next);
    }

    private void SelectCurrentTrainingGoal()
    {
        for (int index = 0; goalTrainings != null && index < goalTrainings.Length; index++)
        {
            var training = goalTrainings[index];
            if (!EntryAvailable(sections[0], index) || training == null
                || DataManager.IsCleared(homeDataManager?.GetTrainingRoomRecord(training.trainingId))) continue;
            selectedEntries[0] = index;
            return;
        }
    }

    // 현재 공개된 과정의 기록만 읽고 수료, 최고 등급과 기록을 표시합니다.
    public void RefreshHomeStatistics()
    {
        if (homeDataManager == null) homeDataManager = UnityEngine.Object.FindFirstObjectByType<DataManager>();
        homeKnownTrainingIds.Clear();
        homeClearedTrainingIds.Clear();
        if (homeTrainingDefinitions != null)
            for (int index = 0; index < homeTrainingDefinitions.Length; index++)
            {
                var training = homeTrainingDefinitions[index];
                if (training != null && !string.IsNullOrWhiteSpace(training.trainingId) && EntryAvailable(sections[0], index))
                    homeKnownTrainingIds.Add(training.trainingId);
            }
        var save = homeDataManager != null ? homeDataManager.CurrentSaveData : null;
        if (save != null && save.trainingRoomRecords != null)
            foreach (var record in save.trainingRoomRecords)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.trainingId) || !homeKnownTrainingIds.Contains(record.trainingId)) continue;
                if (record.bestHitCount < 0 || record.bestHitCount == int.MaxValue || record.bestTimeSeconds < 0f
                    || float.IsNaN(record.bestTimeSeconds) || float.IsInfinity(record.bestTimeSeconds) || record.bestTimeSeconds == float.MaxValue) continue;
                homeClearedTrainingIds.Add(record.trainingId);
            }
        HomeTrainingTotal = homeKnownTrainingIds.Count;
        HomeTrainingCleared = homeClearedTrainingIds.Count;
        HomeProgressNormalized = HomeTrainingTotal > 0 ? Mathf.Clamp01((float)HomeTrainingCleared / HomeTrainingTotal) : 0f;
        if (homeProgressFill != null) homeProgressFill.fillAmount = HomeProgressNormalized;
        SetHomeText(homeProgressText, Mathf.RoundToInt(HomeProgressNormalized * 100f).ToString(CultureInfo.InvariantCulture) + "%");
        SetHomeText(homeTrainingText, HomeTrainingCleared.ToString(CultureInfo.InvariantCulture) + " / " + HomeTrainingTotal.ToString(CultureInfo.InvariantCulture));
        int certificates = homeDataManager != null ? homeDataManager.TrainingCertificates : 0;
        SetHomeText(homeGoldText, certificates.ToString("N0", CultureInfo.InvariantCulture));
        // 훈련 목표의 항목마다 실제 수료 여부를 보여 준다(위에서 모은 수료 기록 그대로).
        if (goalTrainings == null) return;
        bool pendingAssigned = false;
        for (int index = 0; index < goalTrainings.Length; index++)
        {
            var training = goalTrainings[index];
            bool visible = training != null && EntryAvailable(sections[0], index);
            var record = visible ? homeDataManager?.GetTrainingRoomRecord(training.trainingId) : null;
            bool cleared = DataManager.IsCleared(record);
            bool current = visible && !cleared && !pendingAssigned;
            if (current) pendingAssigned = true;
            UpdateGoalRecord(index, cleared ? record : null);
            if (goalStateLabels != null && index < goalStateLabels.Length && goalStateLabels[index] != null)
            {
                var label = goalStateLabels[index];
                // 수료 전의 글자색은 처음 본 색(Prefab 값)을 기억해 두고 쓴다.
                if (goalPendingColors == null || goalPendingColors.Length != goalStateLabels.Length)
                    goalPendingColors = Array.ConvertAll(goalStateLabels, source => source != null ? source.color : Color.white);
                SetHomeText(label, cleared ? goalClearedText : current ? "현재 목표" : goalPendingText);
                Color wanted = cleared ? goalClearedColor : goalPendingColors[index];
                if (label.color != wanted) label.color = wanted;
            }
            if (goalClearedMarks != null && index < goalClearedMarks.Length && goalClearedMarks[index] != null && goalClearedMarks[index].activeSelf != cleared)
                goalClearedMarks[index].SetActive(cleared);
            if (goalPendingMarks != null && index < goalPendingMarks.Length && goalPendingMarks[index] != null && goalPendingMarks[index].activeSelf == cleared)
                goalPendingMarks[index].SetActive(!cleared);
        }
    }
    private void UpdateGoalRecord(int index, TrainingRoomSaveData record)
    {
        var grade = GoalLabel(goalGradeLabels, index);
        if (grade != null)
        {
            string gradeText = record?.bestGrade switch
            {
                Grade.Flawless => "무결점",
                Grade.Excellent => "우수",
                Grade.Pass => "통과",
                _ => "기록 없음"
            };
            SetHomeText(grade, gradeText);
        }
        SetHomeText(GoalLabel(goalBestTimeLabels, index),
            record != null ? TrainingScoreFormatHelper.FormatTime(record.bestTimeSeconds) : "—");
        SetHomeText(GoalLabel(goalBestHitLabels, index),
            record != null ? record.bestHitCount.ToString(CultureInfo.InvariantCulture) + "회" : "—");
    }

    private static TextMeshProUGUI GoalLabel(TextMeshProUGUI[] labels, int index) =>
        labels != null && index < labels.Length ? labels[index] : null;

    private Color[] goalPendingColors;
    private static void SetHomeText(TextMeshProUGUI label, string value)
    {
        if (label != null && label.text != value) label.text = value;
    }
    private void AnimateHomeSignals()
    {
        if (!homeRoot.gameObject.activeInHierarchy || homeLaserColors == null) return;
        for (int index = 0; index < homeLaserSignals.Length; index++)
        {
            var signal = homeLaserSignals[index];
            if (signal == null) continue;
            Color tint = homeLaserColors[index];
            tint.a *= .85f + .15f * Mathf.Sin(Time.unscaledTime * 2.4f + index * .8f);
            signal.color = tint;
        }
    }
    private void RestoreHomeSignals()
    {
        if (homeLaserColors == null) return;
        for (int index = 0; index < homeLaserSignals.Length; index++)
            if (homeLaserSignals[index] != null) homeLaserSignals[index].color = homeLaserColors[index];
    }

    private void AnimateScreenMachinery()
    {
        if (!rootObject.activeInHierarchy) return;
        float delta = Time.unscaledDeltaTime;
        if (rotatingGears != null)
            for (int i = 0; i < rotatingGears.Length; i++)
                if (rotatingGears[i] != null && rotatingGears[i].gameObject.activeInHierarchy)
                    rotatingGears[i].Rotate(0f, 0f, delta * (i % 2 == 0 ? 10f : -10f));
        if (flowingCables == null) return;
        for (int i = 0; i < flowingCables.Length; i++)
        {
            var cable = flowingCables[i];
            if (cable == null || !cable.gameObject.activeInHierarchy) continue;
            Rect uv = cable.uvRect;
            uv.x = Mathf.Repeat(uv.x + delta * (i % 2 == 0 ? -.18f : .18f), 1f);
            cable.uvRect = uv;
        }
    }

    private void HandleInventoryInput(InputAction.CallbackContext context)
    {
        // Alt+Tab(창 전환)의 Tab은 단말기 키로 치지 않는다.
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed)) return;
        if (IsActive)
        {
            if (IsTopInput) Close();
        }
        else OpenMenu();
    }
    public void OpenMenu()
    {
        var ui = UIManager.Instance;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        // 게임 입력이 막힌 동안에는 열지 않는다. 다만 안내가 단말기 여는 키만 남기고 입력을 막아 둔 동안에는 연다.
        bool gameplayBlocked = ui != null && ui.BlocksGameplayInput && !(OpenWhileGameplayBlocked != null && OpenWhileGameplayBlocked());
        if (!initialized || !isActiveAndEnabled || IsActive || IsTransitioning || !IsInLobby || ui == null
            || ui.HasActiveInputUI || ui.IsUIInputBlocked || gameplayBlocked
            || GameTimeController.Instance == null || GameTimeController.Instance.IsPaused
            || player == null || !player.gameObject.activeInHierarchy || player.Health.currentHP <= 0f)
            return;
        if (!Allowed(GuardedAction.Open)) return;
        selectedTab = 0;
        equipmentLesson = equipmentFocus = -1;
        selectedHomeIndex = OpeningHomeIndex != null ? Mathf.Clamp(OpeningHomeIndex(), 0, sections.Length - 1) : 0;
        homeVisible = true;
        nextTabInputAt = float.NegativeInfinity;
        RefreshCatalogProgress();
        for (int tab = 0; tab < sections.Length; tab++) selectedEntries[tab] = FirstAvailableEntry(sections[tab]);
        SelectCurrentTrainingGoal();
        listFocused = false;
        detailOpen = false;
        isClosing = false;
        CancelSlide();
        noticeRoot.SetActive(false);
        rootObject.SetActive(true);
        if (panelMotion != null) panelMotion.Open();
        base.Open();
        inputEventSystem = EventSystem.current;
        if (inputEventSystem != null)
        {
            previousNavigationEvents = inputEventSystem.sendNavigationEvents;
            inputEventSystem.sendNavigationEvents = false;
            inputEventSystem.SetSelectedGameObject(null);
        }
        // 단말기와 Pause 메뉴는 각각 자신이 올린 정지 횟수만 회수한다.
        pauseOwner = GameTimeController.Instance;
        pauseOwner.PushPause();
        ownsPause = true;
        RefreshHomeStatistics();
        RefreshSelection();
        BeginTransition(1f, OpenDuration);
    }
    public override void Open() => OpenMenu();
    public void RequestClose()
    {
        if (IsTopInput && !isClosing) Close();
    }
    public override void Close()
    {
        if (!initialized) return;
        if (!IsActive)
        {
            if (!isTransitioning) ReleasePause();
            return;
        }
        if (isClosing) return;
        if (!Allowed(GuardedAction.Close)) return;
        isClosing = true;
        hoveredButton = null;
        CancelHomeTransition();
        CancelSlide();
        RestoreHomeSignals();
        noticeRoot.SetActive(false);
        RefreshInteractable();
        RefreshButtonFaces();
        if (panelMotion != null) panelMotion.Close();
        BeginTransition(0f, CloseDuration);
    }
    private void CloseImmediately()
    {
        isClosing = true;
        detailOpen = false;
        homeVisible = true;
        hoveredButton = null;
        CancelHomeTransition();
        CancelSlide();
        RestoreHomeSignals();
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = null;
        isTransitioning = false;
        isClosing = false;
        if (IsActive && UIManager.Instance != null) base.Close();
        ReleasePause();
        if (!initialized) return;
        noticeRoot.SetActive(false);
        if (panelMotion != null) panelMotion.SnapHidden();
        ApplyPresentation(0f);
        rootObject.SetActive(false);
        RefreshInteractable();
        RefreshButtonFaces();
    }
    private void ReleasePause()
    {
        if (inputEventSystem != null)
        {
            inputEventSystem.sendNavigationEvents = previousNavigationEvents;
            inputEventSystem.SetSelectedGameObject(null);
            inputEventSystem = null;
        }
        if (!ownsPause) return;
        ownsPause = false;
        if (pauseOwner != null) pauseOwner.PopPause();
        pauseOwner = null;
    }

    public void OpenCategory(int index)
    {
        if (!CanReceiveInput || NoticeVisible || index < 0 || index >= sections.Length) return;
        if (!homeVisible) { SelectTab(index); return; }
        if (!Allowed(GuardedAction.OpenCategory, index)) return;
        selectedHomeIndex = index;
        selectedTab = index;
        homeVisible = false;
        detailOpen = false;
        equipmentLesson = -1;
        listFocused = false;
        // 안내가 가리키는 항목이 있으면 바로 골라 둔다(확인 한 번에 열린다).
        int guided = GuidedEntry != null ? GuidedEntry(index) : -1;
        if (guided >= 0 && EntryAvailable(sections[index], guided)) { selectedEntries[index] = guided; listFocused = true; }
        hoveredButton = null;
        PlayFeedback(confirmFeedback);
        BeginHomeNavigation(false);
    }

    public void ReturnHome() => ReturnHome(true);
    private void ReturnHome(bool feedback)
    {
        if (!CanReceiveTabInput || NoticeVisible || homeVisible) return;
        if (!Allowed(GuardedAction.ReturnHome)) return;
        CancelSlide();
        detailOpen = false;
        equipmentLesson = -1;
        listFocused = false;
        hoveredButton = null;
        selectedHomeIndex = selectedTab;
        if (feedback) PlayFeedback(cancelFeedback);
        RefreshHomeStatistics();
        // 중간 팝업·슬라이드의 기준 상태를 정리한 뒤 같은 정지 소유 안에서 홈으로 돌아간다.
        NormalizePages();
        homeVisible = true;
        BeginHomeNavigation(true);
    }

    private void BeginHomeNavigation(bool showHome)
    {
        CancelHomeTransition();
        isHomeTransitioning = true;
        homeRoot.gameObject.SetActive(true);
        catalogNavigation.SetActive(true);
        contentViewport.gameObject.SetActive(true);
        homeGroup.alpha = showHome ? 0f : 1f;
        homeRoot.localScale = homeScale * (showHome ? .93f : 1f);
        contentInputGroup.alpha = showHome ? 1f : 0f;
        contentViewport.localScale = catalogScale * (showHome ? 1f : .96f);
        navigationGroup.alpha = showHome ? 1f : 0f;
        homeGroup.interactable = homeGroup.blocksRaycasts = false;
        contentInputGroup.interactable = contentInputGroup.blocksRaycasts = false;
        navigationGroup.interactable = navigationGroup.blocksRaycasts = false;
        RefreshSelection();
        homeRoutine = StartCoroutine(AnimateHomeNavigation(showHome));
    }
    private IEnumerator AnimateHomeNavigation(bool showHome)
    {
        float age = 0f;
        const float duration = .3f;
        while (age < duration)
        {
            age += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(age / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            homeGroup.alpha = showHome ? eased : 1f - eased;
            homeRoot.localScale = homeScale * Mathf.Lerp(showHome ? .93f : 1f, showHome ? 1f : .94f, eased);
            contentInputGroup.alpha = showHome ? 1f - eased : eased;
            contentViewport.localScale = catalogScale * Mathf.Lerp(showHome ? 1f : .96f, showHome ? .96f : 1f, eased);
            navigationGroup.alpha = contentInputGroup.alpha;
            yield return null;
        }
        homeRoutine = null;
        isHomeTransitioning = false;
        RestoreContentInput();
        RefreshSelection();
        if (!homeVisible) EnsureEntryVisible(sections[selectedTab], SelectedEntry);
    }
    private void CancelHomeTransition()
    {
        if (homeRoutine != null) StopCoroutine(homeRoutine);
        homeRoutine = null;
        isHomeTransitioning = false;
    }

    public void SelectTab(int index)
    {
        if (homeVisible) OpenCategory(index);
        else SelectTab(index, Math.Sign(index - selectedTab));
    }

    private void SelectTab(int index, int direction)
    {
        if (!CanReceiveTabInput || homeVisible || NoticeVisible || index < 0 || index >= sections.Length
            || Time.unscaledTime < nextTabInputAt) return;
        if (!Allowed(GuardedAction.SelectTab, index)) return;
        hoveredButton = null;
        if (selectedTab == index && !detailOpen)
        {
            listFocused = false;
            RefreshSelection();
            return;
        }
        if (selectedTab == index)
        {
            PlayFeedback(cancelFeedback);
            detailOpen = false;
            equipmentLesson = -1;
            listFocused = false;
            hoveredButton = null;
            BeginPopup(null);
            return;
        }
        var previous = CurrentPage();
        bool reverse = direction < 0;
        PlayFeedback(moveFeedback);
        // 화면 전환 시간과 분리한 입력 간격으로 연타만 제한한다.
        nextTabInputAt = Time.unscaledTime + TabInputInterval;
        selectedTab = index;
        detailOpen = false;
        equipmentLesson = -1;
        listFocused = false;
        hoveredButton = null;
        BeginPageSlide(previous, sections[index].listRoot, reverse);
    }
    public void SelectEntry(int index)
    {
        SelectEntry(index, true);
    }
    private void SelectEntry(int index, bool ensureVisible)
    {
        if (!CanReceiveInput || homeVisible || NoticeVisible || detailOpen || !EntryAvailable(sections[selectedTab], index)) return;
        if (!Allowed(GuardedAction.SelectEntry, index)) return;
        hoveredButton = null;
        if (selectedEntries[selectedTab] != index) PlayFeedback(moveFeedback);
        selectedEntries[selectedTab] = index;
        listFocused = true;
        RefreshSelection();
        if (ensureVisible) EnsureEntryVisible(sections[selectedTab], index);
    }
    private static bool DeclaredEntryAvailable(SectionBinding section, int index)
    {
        if (index < 0 || index >= section.entryButtons.Length) return false;
        return section.entryAvailable == null || section.entryAvailable.Length == 0
            || index < section.entryAvailable.Length && section.entryAvailable[index];
    }
    private bool EntryAvailable(SectionBinding section, int index)
    {
        if (!DeclaredEntryAvailable(section, index) || section.entryButtons[index] == null || !section.entryButtons[index].gameObject.activeSelf
            || EquipmentParent(section, index) >= 0) return false;
        return !entryAvailability.TryGetValue(section.entryButtons[index], out bool available) || available;
    }
    private int FirstAvailableEntry(SectionBinding section)
    {
        for (int index = 0; index < section.entryButtons.Length; index++)
            if (EntryAvailable(section, index)) return index;
        return -1;
    }
    private static bool PopupAvailable(SectionBinding section, int index)
    {
        if (index < 0 || index >= section.details.Length || section.details[index] == null) return false;
        return !section.inlineInformation || section.popupAvailable != null
            && index < section.popupAvailable.Length && section.popupAvailable[index];
    }
    private void SelectNextEntry(int step)
    {
        if (step == 0) return;
        var section = sections[selectedTab];
        for (int index = SelectedEntry + step; index >= 0 && index < section.entryButtons.Length; index += step)
            if (EntryAvailable(section, index)) { SelectEntry(index); return; }
    }
    private static void EnsureEntryVisible(SectionBinding section, int index)
    {
        var scroll = section.entryScroll;
        if (scroll == null || scroll.content == null || scroll.viewport == null || index < 0 || index >= section.entryButtons.Length) return;
        var target = section.entryButtons[index].transform as RectTransform;
        if (target == null || !target.IsChildOf(scroll.content)) return;
        Canvas.ForceUpdateCanvases();
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners)
        {
            Vector2 point = scroll.viewport.InverseTransformPoint(corner);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        if (scroll.vertical && section.firstRowTopInset > 0f)
        {
            float contentTop = scroll.viewport.InverseTransformPoint(scroll.content.TransformPoint(new Vector3(0f, scroll.content.rect.yMax))).y;
            // 첫 행으로 복귀하거나 목록을 다시 열면 버튼 위의 구획 제목도 함께 드러낸다.
            if (contentTop - max.y <= section.firstRowTopInset + .1f) max.y = contentTop;
        }
        Rect viewport = scroll.viewport.rect;
        Vector2 shift = Vector2.zero;
        if (scroll.horizontal)
        {
            if (min.x < viewport.xMin) shift.x = viewport.xMin - min.x;
            else if (max.x > viewport.xMax) shift.x = viewport.xMax - max.x;
        }
        if (scroll.vertical)
        {
            if (max.y > viewport.yMax) shift.y = viewport.yMax - max.y;
            else if (min.y < viewport.yMin) shift.y = viewport.yMin - min.y;
        }
        scroll.StopMovement();
        scroll.content.anchoredPosition += shift;
    }
    public void ShowEntryDetails(int index)
    {
        if (!CanReceiveInput || homeVisible || NoticeVisible || detailOpen || !EntryAvailable(sections[selectedTab], index)) return;
        if (!Allowed(GuardedAction.ShowDetails, index)) return;
        var section = sections[selectedTab];
        SelectEntry(index);
        if (section.inlineInformation && !PopupAvailable(section, index)) return;
        if (section.details[index] == null) return;
        if (inlinePopupSources.TryGetValue(section.details[index], out var source)) CopyTexts(source, section.details[index]);
        PlayFeedback(confirmFeedback);
        selectedEntries[selectedTab] = index;
        equipmentLesson = -1;
        if (selectedTab == EquipmentTab) equipmentFocus = FirstEquipmentLesson(index);
        detailOpen = true;
        listFocused = true;
        hoveredButton = null;
        BeginPopup(section.details[index].GetComponent<RectTransform>());
        EntryDetailsShown?.Invoke(selectedTab, index);
    }
    private void SelectEntryFromPointer(int tab, int index, bool openDetails)
    {
        if (!CanReceiveInput || homeVisible || NoticeVisible || detailOpen || tab != selectedTab) return;
        var section = sections[tab];
        bool opensPopup = !section.inlineInformation || section.popupFromInline && PopupAvailable(section, index);
        if (openDetails && opensPopup) ShowEntryDetails(index);
        else SelectEntry(index, false);
    }
    public void ShowPreviewNotice(string message)
    {
        if (!CanReceiveInput || homeVisible || NoticeVisible) return;
        noticeText.text = message ?? string.Empty;
        var path = noticeRoot.transform.Find("Window/Context")?.GetComponent<TextMeshProUGUI>();
        var context = CurrentPage().Find("Context")?.GetComponent<TextMeshProUGUI>();
        if (path != null) path.text = (context != null ? context.text : "단말기") + " / 안내";
        hoveredButton = null;
        noticeRoot.SetActive(true);
        popupVisibility = 1f;
        ApplyPopupLayer();
        RefreshInteractable();
        RefreshButtonFaces();
    }
    public void DismissNotice()
    {
        if (!CanReceiveInput || !NoticeVisible) return;
        noticeRoot.SetActive(false);
        hoveredButton = null;
        listFocused = true;
        RefreshSelection();
    }
    protected override void ProcessMove(Vector2 direction)
    {
        if (!CanReceiveTabInput || NoticeVisible) return;
        if (!Allowed(GuardedAction.Move)) return;
        hoveredButton = null;
        int horizontal = direction.x > .5f ? 1 : direction.x < -.5f ? -1 : 0;
        int vertical = direction.y > .5f ? -1 : direction.y < -.5f ? 1 : 0;
        if (homeVisible)
        {
            if (!CanReceiveInput) return;
            int column = Mathf.Clamp(selectedHomeIndex % 2 + horizontal, 0, 1);
            int row = Mathf.Clamp(selectedHomeIndex / 2 + vertical, 0, 1);
            int next = row * 2 + column;
            if (next != selectedHomeIndex) PlayFeedback(moveFeedback);
            selectedHomeIndex = next;
            RefreshSelection();
            return;
        }
        if (detailOpen && selectedTab == 3 && CanReceiveInput)
        {
            sections[3].details[SelectedEntry].GetComponent<TerminalMailDocumentView>()?.ScrollBy(direction.y);
            return;
        }
        if (detailOpen && selectedTab == EquipmentTab && CanReceiveInput)
        {
            var tutorial = sections[EquipmentTab].details[DetailIndex].GetComponentInChildren<TerminalTutorialVideo>();
            if (tutorial != null) { if (horizontal != 0) tutorial.SeekBySeconds(horizontal); return; }
            if (equipmentLesson < 0 && equipmentFocus >= 0) { MoveEquipmentLesson(direction); return; }
        }
        if (!listFocused)
        {
            int nextTab = (selectedTab + horizontal + sections.Length) % sections.Length;
            if (horizontal != 0) SelectTab(nextTab, horizontal);
            else if (vertical > 0 && CanReceiveInput && SelectedEntry >= 0)
            { listFocused = true; RefreshSelection(); EnsureEntryVisible(sections[selectedTab], SelectedEntry); }
            return;
        }
        if (!CanReceiveInput) return;
        if (detailOpen)
        {
            // 목록이 가려진 팝업에서는 방향키로 다른 항목에 넘어가지 않는다.
            if (vertical < 0) { listFocused = false; RefreshSelection(); }
            return;
        }
        var section = sections[selectedTab];
        if (selectedTab == 2 && equipmentUsesGeometry)
        {
            MoveEquipment(direction, section);
            return;
        }
        int columns = Mathf.Max(1, section.columns);
        if (section.horizontalList)
        {
            if (vertical < 0) { listFocused = false; RefreshSelection(); }
            else if (horizontal != 0) SelectNextEntry(horizontal);
            return;
        }
        if (columns > 1)
        {
            if (vertical < 0 && SelectedEntry < columns) { listFocused = false; RefreshSelection(); }
            else if (vertical != 0) SelectNextEntry(vertical * columns);
            else if (horizontal != 0) SelectNextEntry(horizontal);
            return;
        }
        if (vertical < 0 && SelectedEntry == FirstAvailableEntry(section)) { listFocused = false; RefreshSelection(); }
        else if (vertical != 0) SelectNextEntry(vertical);
    }
    private void MoveEquipment(Vector2 direction, SectionBinding section)
    {
        bool vertical = Mathf.Abs(direction.y) > .5f;
        var origin = section.entryButtons[SelectedEntry].transform as RectTransform;
        var reference = section.entryScroll != null ? section.entryScroll.content : section.listRoot;
        Vector2 center = reference.InverseTransformPoint(origin.TransformPoint(origin.rect.center));
        int best = -1; float bestScore = float.PositiveInfinity;
        for (int i = 0; i < section.entryButtons.Length; i++)
        {
            if (i == SelectedEntry || !EntryAvailable(section, i)) continue;
            var candidate = (RectTransform)section.entryButtons[i].transform;
            Vector2 delta = (Vector2)reference.InverseTransformPoint(candidate.TransformPoint(candidate.rect.center)) - center;
            // 세로 이동은 같은 장비 구획에 머물고 좌우 입력으로 구획을 오간다.
            if (vertical && center.x * (center.x + delta.x) < 0) continue;
            float along = vertical ? delta.y * Mathf.Sign(direction.y) : delta.x * Mathf.Sign(direction.x);
            float across = vertical ? Mathf.Abs(delta.x) : Mathf.Abs(delta.y);
            if (along < 1) continue;
            float score = vertical ? along * 10000 + across : across * 100 + along;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best >= 0) SelectEntry(best);
        else if (vertical && direction.y > 0) { listFocused = false; RefreshSelection(); }
    }
    public override void OnConfirm()
    {
        if (!CanReceiveInput) return;
        hoveredButton = null;
        if (homeVisible) { OpenCategory(selectedHomeIndex); return; }
        if (NoticeVisible) { DismissNotice(); return; }
        if (detailOpen && selectedTab == EquipmentTab)
        {
            var tutorial = sections[EquipmentTab].details[DetailIndex].GetComponentInChildren<TerminalTutorialVideo>();
            if (tutorial != null) { tutorial.TogglePlayback(); return; }
            if (equipmentLesson < 0 && equipmentFocus >= 0) { OpenEquipmentLesson(equipmentFocus); return; }
        }
        if (SelectedEntry < 0) return;
        if (!listFocused) { PlayFeedback(confirmFeedback); listFocused = true; RefreshSelection(); return; }
        if (!detailOpen)
        {
            var section = sections[selectedTab];
            if (section.inlineInformation)
            {
                var inline = section.inlineDetails[SelectedEntry];
                var inlineAction = inline != null ? inline.GetComponentInChildren<UnityEngine.UI.Button>(false) : null;
                if (inlineAction != null && inlineAction.interactable) inlineAction.onClick.Invoke();
                else if (inlineAction == null && section.popupFromInline) ShowEntryDetails(SelectedEntry);
            }
            else ShowEntryDetails(SelectedEntry);
            return;
        }
        PlayFeedback(confirmFeedback);
        var actions = sections[selectedTab].detailActions;
        var action = actions != null && DetailIndex < actions.Length ? actions[DetailIndex] : null;
        if (action != null && action.isActiveAndEnabled && action.interactable) action.onClick.Invoke();
    }
    public override void OnCancel()
    {
        if (!CanReceiveInput) return;
        if (!Allowed(GuardedAction.Back)) return;
        hoveredButton = null;
        PlayFeedback(cancelFeedback);
        if (homeVisible) Close();
        else if (NoticeVisible) DismissNotice();
        else if (detailOpen && selectedTab == EquipmentTab && equipmentLesson >= 0)
        {
            equipmentFocus = equipmentLesson;
            equipmentLesson = -1;
            BeginPopup((RectTransform)sections[EquipmentTab].details[SelectedEntry].transform);
        }
        else if (detailOpen)
        {
            detailOpen = false;
            listFocused = true;
            hoveredButton = null;
            BeginPopup(null);
        }
        else if (listFocused) { listFocused = false; RefreshSelection(); }
        else ReturnHome(false);
    }

    private void RefreshSelection()
    {
        currentIndex = homeVisible ? selectedHomeIndex : selectedTab;
        prevIndex = currentIndex;
        for (int tab = 0; tab < sections.Length; tab++)
        {
            bool active = tab == selectedTab;
            tabButtons[tab].transform.localScale = Vector3.one;
            SetHighlightAlpha(tabHighlights[tab], active ? (listFocused ? .55f : 1f) : 0f);
            for (int entry = 0; entry < sections[tab].entryButtons.Length; entry++)
            {
                bool selected = entry == selectedEntries[tab];
                SetHighlightAlpha(sections[tab].entryHighlights[entry], selected ? (listFocused ? 1f : .55f) : 0f);
            }
        }
        if (!isPageTransitioning && !isHomeTransitioning) NormalizePages();
        RefreshButtonFaces();
        RefreshInteractable();
    }

    private void RefreshButtonFaces()
    {
        if (hoveredButton != null && (!hoveredButton.gameObject.activeInHierarchy || !hoveredButton.interactable)) hoveredButton = null;
        foreach (var pair in buttonFocus)
        {
            var button = pair.Key;
            if (button == null || pair.Value == null) continue;
            bool selected = IsActive && !isClosing && button.gameObject.activeInHierarchy
                && (button == hoveredButton || IsSelectedButton(button));
            if (button.TryGetComponent<TerminalButtonMotion>(out var motion)) motion.Select(selected);
            pair.Value.color = selected ? Color.white : Color.clear;
        }
    }

    private bool IsSelectedButton(UnityEngine.UI.Button button)
    {
        if (NoticeVisible) return button.transform.IsChildOf(noticeRoot.transform);
        if (homeVisible) return button == homeButtons[selectedHomeIndex];
        if (button == tabButtons[selectedTab]) return true;
        var section = sections[selectedTab];
        if (SelectedEntry < 0) return false;
        if (!detailOpen) return listFocused && button == section.entryButtons[SelectedEntry];
        if (selectedTab == EquipmentTab && equipmentLesson < 0 && equipmentFocus >= 0)
            return button == section.entryButtons[equipmentFocus];
        return listFocused && section.detailActions != null && DetailIndex < section.detailActions.Length
            && button == section.detailActions[DetailIndex];
    }
    private void SetHighlightAlpha(UnityEngine.UI.Image image, float alpha)
    {
        Color tint = highlightColors[image];
        tint.a = alpha;
        image.color = tint;
    }
    private void RefreshInteractable()
    {
        bool enabledInput = CanReceiveInput && !NoticeVisible;
        foreach (var button in homeButtons) button.interactable = enabledInput && homeVisible;
        homeGroup.interactable = homeGroup.blocksRaycasts = enabledInput && homeVisible;
        homeNavigationButton.gameObject.SetActive(!homeVisible);
        homeNavigationButton.interactable = CanReceiveTabInput && !NoticeVisible && !homeVisible;
        navigationGroup.interactable = navigationGroup.blocksRaycasts = CanReceiveTabInput && !NoticeVisible && !homeVisible;
        foreach (var button in tabButtons) button.interactable = CanReceiveTabInput && !NoticeVisible && !homeVisible;
        foreach (var section in sections)
        {
            for (int index = 0; index < section.entryButtons.Length; index++)
                section.entryButtons[index].interactable = enabledInput && !homeVisible &&
                    (detailOpen ? EquipmentLessonAvailable(section, index) : EntryAvailable(section, index));
            if (section.entryScroll != null) section.entryScroll.enabled = enabledInput && !homeVisible && !detailOpen;
            if (section.detailActions != null)
                foreach (var button in section.detailActions)
                    if (button != null && button.GetComponent<TerminalPlaybackButton>() == null) button.interactable = enabledInput && !homeVisible
                        && detailActionAvailability.TryGetValue(button, out bool actionAvailable) && actionAvailable;
            if (section.popupButtons != null)
                foreach (var button in section.popupButtons)
                    if (button != null) button.interactable = enabledInput && !homeVisible && !detailOpen
                        && detailActionAvailability.TryGetValue(button, out bool popupEnabled) && popupEnabled;
        }
    }

    private RectTransform CurrentPage() => homeVisible ? homeRoot : detailOpen
        ? sections[selectedTab].details[DetailIndex].GetComponent<RectTransform>() : sections[selectedTab].listRoot;
    private void NormalizePages()
    {
        homeRoot.gameObject.SetActive(homeVisible);
        catalogNavigation.SetActive(!homeVisible);
        contentViewport.gameObject.SetActive(!homeVisible);
        homeRoot.localScale = homeScale;
        homeGroup.alpha = 1f;
        contentViewport.localScale = catalogScale;
        contentInputGroup.alpha = 1f;
        navigationGroup.alpha = 1f;
        foreach (var home in pageHomes)
        {
            home.Key.anchoredPosition = home.Value;
            home.Key.localScale = pageScales[home.Key];
            home.Key.localRotation = pageRotations[home.Key];
            var group = pageGroups[home.Key];
            group.alpha = 1f;
            group.interactable = group.blocksRaycasts = true;
        }
        for (int tab = 0; tab < sections.Length; tab++)
        {
            bool active = !homeVisible && tab == selectedTab;
            sections[tab].root.SetActive(active);
            sections[tab].listRoot.gameObject.SetActive(active);
            if (sections[tab].inlineDetails != null)
                for (int index = 0; index < sections[tab].inlineDetails.Length; index++)
                    if (sections[tab].inlineDetails[index] != null)
                        sections[tab].inlineDetails[index].SetActive(index == selectedEntries[tab]);
            if (active && detailOpen)
            {
                var list = sections[tab].listRoot;
                list.localScale = pageScales[list] * .94f;
                pageGroups[list].alpha = .22f;
                pageGroups[list].interactable = pageGroups[list].blocksRaycasts = false;
            }
            for (int entry = 0; entry < sections[tab].details.Length; entry++)
            {
                bool visible = active && detailOpen && entry == DetailIndex;
                var detail = sections[tab].details[entry];
                if (detail == null) continue;
                detail.SetActive(visible);
                if (visible)
                {
                    var page = (RectTransform)detail.transform;
                    page.localScale = pageScales[page];
                }
            }
        }
        popupVisibility = PopupLayerWanted ? 1f : 0f;
        ApplyPopupLayer();
    }

    private bool PopupLayerWanted => detailOpen || NoticeVisible;

    // 팝업 층의 투명도는 팝업 표시 정도와 단말기 화면의 등장 정도를 함께 따른다.
    private void ApplyPopupLayer()
    {
        if (popupLayerGroup == null) return;
        float alpha = popupVisibility * Mathf.InverseLerp(.25f, .85f, presentationProgress);
        popupLayerGroup.alpha = alpha;
        popupLayerGroup.interactable = popupLayerGroup.blocksRaycasts = alpha > .99f && PopupLayerWanted;
    }

    // 어두운 배경을 누르면 안내창이나 상세창을 닫는다.
    private void HandlePopupBackdropClick()
    {
        if (!CanReceiveInput) return;
        if (NoticeVisible)
        {
            PlayFeedback(cancelFeedback);
            DismissNotice();
        }
        else if (detailOpen) OnCancel();
    }

    private void BeginPopup(RectTransform next)
    {
        if (pageRoutine != null) StopCoroutine(pageRoutine);
        pageRoutine = null;
        isCategoryTransitioning = false;
        isPageTransitioning = true;
        var section = sections[selectedTab];
        var list = section.listRoot;
        list.gameObject.SetActive(true);
        section.root.SetActive(true);
        var outgoing = new List<RectTransform>();
        foreach (var detail in section.details)
        {
            if (detail == null) continue;
            var page = (RectTransform)detail.transform;
            if (page != next && page.gameObject.activeInHierarchy) outgoing.Add(page);
        }
        if (next != null)
        {
            next.gameObject.SetActive(true);
            next.SetAsLastSibling();
            next.anchoredPosition = pageHomes[next];
            next.localScale = Vector3.Scale(pageScales[next], TitlePanelMotion.UnfoldScale(0f));
            pageGroups[next].alpha = 0f;
        }
        contentInputGroup.interactable = contentInputGroup.blocksRaycasts = false;
        RefreshSelection();
        pageRoutine = StartCoroutine(AnimatePopup(list, next, outgoing));
    }

    private IEnumerator AnimatePopup(RectTransform list, RectTransform next, List<RectTransform> outgoing)
    {
        Vector3 listStart = list.localScale;
        float listAlpha = pageGroups[list].alpha;
        var oldScales = outgoing.ConvertAll(page => page.localScale);
        var oldAlphas = outgoing.ConvertAll(page => pageGroups[page].alpha);
        float duration = next == null ? CloseDuration : OpenDuration;
        float visibilityStart = popupVisibility, visibilityTarget = next != null || NoticeVisible ? 1f : 0f;
        float age = 0f;
        while (age < duration)
        {
            age += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(age / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            popupVisibility = Mathf.Lerp(visibilityStart, visibilityTarget, eased);
            ApplyPopupLayer();
            list.localScale = Vector3.LerpUnclamped(listStart, pageScales[list] * (next == null ? 1f : .94f), eased);
            pageGroups[list].alpha = Mathf.Lerp(listAlpha, next == null ? 1f : .22f, eased);
            for (int index = 0; index < outgoing.Count; index++)
            {
                var old = outgoing[index];
                old.localScale = Vector3.LerpUnclamped(oldScales[index], oldScales[index] * .85f, eased);
                pageGroups[old].alpha = Mathf.Lerp(oldAlphas[index], 0f, eased);
            }
            if (next != null)
            {
                next.localScale = Vector3.Scale(pageScales[next], TitlePanelMotion.UnfoldScale(t));
                pageGroups[next].alpha = Mathf.InverseLerp(0f, .65f, t);
            }
            yield return null;
        }
        pageRoutine = null;
        isPageTransitioning = false;
        isCategoryTransitioning = false;
        RestoreContentInput();
        RefreshSelection();
    }

    private void BeginPageSlide(RectTransform previous, RectTransform next, bool reverse)
    {
        if (previous == next) { RefreshSelection(); return; }
        if (pageRoutine != null) StopCoroutine(pageRoutine);
        pageRoutine = null;

        float distance = Mathf.Max(1f, contentViewport.rect.width) + 24f;
        float direction = reverse ? -1f : 1f;
        bool nextWasVisible = next.gameObject.activeInHierarchy;
        Vector2 nextStart = nextWasVisible ? next.anchoredPosition
            : pageHomes[next] + Vector2.right * (distance * direction);
        var outgoing = new List<RectTransform>();
        var outgoingStarts = new List<Vector2>();
        foreach (var home in pageHomes)
        {
            var page = home.Key;
            if (page == next || !page.gameObject.activeInHierarchy) continue;
            outgoing.Add(page);
            outgoingStarts.Add(page.anchoredPosition);
        }

        // 재선택할 때 현재 보이는 페이지의 위치를 보존해 전환 도중에도 이어서 넘긴다.
        isPageTransitioning = true;
        isCategoryTransitioning = true;
        for (int tab = 0; tab < sections.Length; tab++)
        {
            var section = sections[tab];
            bool listVisible = section.listRoot == next || outgoing.Contains(section.listRoot);
            section.listRoot.gameObject.SetActive(listVisible);
            bool anyVisible = listVisible;
            foreach (var detail in section.details)
            {
                if (detail == null) continue;
                bool visible = detail.transform == next || outgoing.Contains(detail.GetComponent<RectTransform>());
                detail.SetActive(visible);
                anyVisible |= visible;
            }
            section.root.SetActive(anyVisible);
        }
        sections[selectedTab].root.transform.SetAsLastSibling();
        next.anchoredPosition = nextStart;
        contentInputGroup.interactable = contentInputGroup.blocksRaycasts = false;
        RefreshSelection();
        EnsureEntryVisible(sections[selectedTab], SelectedEntry);
        pageRoutine = StartCoroutine(SlidePages(outgoing, outgoingStarts, next, nextStart, distance * direction));
    }
    private IEnumerator SlidePages(List<RectTransform> outgoing, List<Vector2> starts,
        RectTransform next, Vector2 nextStart, float distance)
    {
        Vector2 nextHome = pageHomes[next];
        Vector3 nextScale = next.localScale;
        Quaternion nextRotation = next.localRotation;
        float nextAlpha = pageGroups[next].alpha;
        float age = 0f;
        while (age < PageDuration)
        {
            age += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(age / PageDuration);
            float eased = t < .5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * .5f;
            for (int i = 0; i < outgoing.Count; i++)
            {
                var page = outgoing[i];
                Vector2 end = pageHomes[page] + Vector2.left * distance;
                page.anchoredPosition = Vector2.LerpUnclamped(starts[i], end, eased);
            }
            next.anchoredPosition = Vector2.LerpUnclamped(nextStart, nextHome, eased);
            next.localScale = Vector3.LerpUnclamped(nextScale, pageScales[next], eased);
            next.localRotation = Quaternion.SlerpUnclamped(nextRotation, pageRotations[next], eased);
            pageGroups[next].alpha = Mathf.Lerp(nextAlpha, 1f, eased);
            yield return null;
        }
        pageRoutine = null;
        isPageTransitioning = false;
        isCategoryTransitioning = false;
        RestoreContentInput();
        RefreshSelection();
    }
    private void CancelSlide()
    {
        if (pageRoutine != null) StopCoroutine(pageRoutine);
        pageRoutine = null;
        isPageTransitioning = false;
        isCategoryTransitioning = false;
        RestoreContentInput();
        if (!initialized) return;
        NormalizePages();
        RefreshInteractable();
    }
    private void RestoreContentInput()
    {
        if (contentInputGroup == null) return;
        contentInputGroup.interactable = contentInteractable;
        contentInputGroup.blocksRaycasts = contentRaycasts;
    }

    private void BeginTransition(float target, float duration)
    {
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        isTransitioning = true;
        RefreshInteractable();
        transitionRoutine = StartCoroutine(AnimatePresentation(target, duration));
    }
    private IEnumerator AnimatePresentation(float target, float duration)
    {
        float start = presentationProgress, age = 0f;
        while (age < duration)
        {
            age += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(age / duration);
            ApplyPresentation(Mathf.Lerp(start, target, t));
            yield return null;
        }
        ApplyPresentation(target);
        transitionRoutine = null;
        isTransitioning = false;
        if (target < .5f)
        {
            // 회수 연출이 끝난 프레임에 입력 수신과 정지를 함께 해제한다.
            if (IsActive && UIManager.Instance != null) base.Close();
            isClosing = false;
            detailOpen = false;
            homeVisible = true;
            hoveredButton = null;
            NormalizePages();
            rootObject.SetActive(false);
            ReleasePause();
            RefreshButtonFaces();
        }
        RefreshInteractable();
    }
    private void ApplyPresentation(float progress)
    {
        presentationProgress = progress;
        lastFitSize = fitRect.rect.size;
        float scale = PortableTerminalLayout.Fit(terminalRoot, lastFitSize);
        terminalRoot.localScale = (panelMotion != null ? Vector3.one : TitlePanelMotion.UnfoldScale(progress)) * scale;
        // 장비 영상과 문서는 각자 만든 무대 안에서 표시하고, 네 변에 닫기 버튼 여백을 남긴다.
        if (popupStage != null && lastFitSize.x > 0f && lastFitSize.y > 0f)
        {
            Vector2 stage = popupStage.rect.size;
            float popupScale = Mathf.Min(lastFitSize.x / Mathf.Max(1f, stage.x), lastFitSize.y / Mathf.Max(1f, stage.y));
            popupStage.localScale = Vector3.one * (popupScale * .96f);
        }
        ApplyPopupLayer();
        terminalGroup.alpha = Mathf.Clamp01(progress * 5f);
        screenGroup.alpha = 1f;
        backdropGroup.alpha = progress;
        terminalGroup.interactable = terminalGroup.blocksRaycasts = progress > .99f;
        screenGroup.interactable = screenGroup.blocksRaycasts = progress > .99f;
        backdropGroup.interactable = backdropGroup.blocksRaycasts = progress > 0f;
    }
}
