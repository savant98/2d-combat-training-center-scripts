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
    }

    [Header("Availability")]
    [SerializeField] private Room lobbyRoom;

    [Header("Presentation")]
    [SerializeField] private GameObject rootObject;
    [SerializeField] private RectTransform fitRect;
    [SerializeField] private RectTransform terminalRoot;
    [SerializeField] private RectTransform wristRoot;
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

    [Header("버튼 선택 색상")]
    [SerializeField, Tooltip("훈련 목표·적 도감·장비·수신함 순서의 불투명 선택 배경입니다.")]
    private Color[] selectionColors = { new Color32(248, 212, 106, 255), new Color32(155, 232, 188, 255),
        new Color32(158, 217, 255, 255), new Color32(245, 184, 217, 255) };
    [SerializeField] private Color selectionInk = new Color32(9, 14, 20, 255);

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

    private const float OpenDuration = .38f;
    private const float CloseDuration = .24f;
    private const float PageDuration = .52f;
    private const float TabInputInterval = .2f;
    private readonly int[] selectedEntries = new int[4];
    private readonly List<Action> unbindButtons = new List<Action>();
    private readonly Dictionary<UnityEngine.UI.Button, bool> detailActionAvailability = new Dictionary<UnityEngine.UI.Button, bool>();
    private readonly Dictionary<UnityEngine.UI.Button, bool> entryAvailability = new Dictionary<UnityEngine.UI.Button, bool>();
    private readonly Dictionary<RectTransform, Vector2> pageHomes = new Dictionary<RectTransform, Vector2>();
    private readonly Dictionary<UnityEngine.UI.Image, Color> highlightColors = new Dictionary<UnityEngine.UI.Image, Color>();
    private readonly Dictionary<RectTransform, Vector3> pageScales = new Dictionary<RectTransform, Vector3>();
    private readonly Dictionary<RectTransform, Quaternion> pageRotations = new Dictionary<RectTransform, Quaternion>();
    private readonly Dictionary<RectTransform, CanvasGroup> pageGroups = new Dictionary<RectTransform, CanvasGroup>();
    private readonly Dictionary<UnityEngine.UI.Button, ButtonVisual> buttonVisuals = new Dictionary<UnityEngine.UI.Button, ButtonVisual>();
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
    private Vector2 terminalHome, wristHome, lastFitSize;
    private Quaternion terminalRotation, wristRotation;
    private float presentationProgress;
    private float nextTabInputAt;
    private int selectedTab;
    private int selectedHomeIndex;
    private bool homeVisible, isHomeTransitioning;
    private bool listFocused, initialized, isTransitioning, isClosing, ownsPause;
    [SerializeField] private bool equipmentUsesGeometry;
    private bool detailOpen, isPageTransitioning, isCategoryTransitioning, contentInteractable, contentRaycasts;

    private sealed class ButtonVisual
    {
        public UnityEngine.UI.Graphic fill;
        public UnityEngine.UI.Graphic face;
        public Color faceColor;
        public UnityEngine.UI.Graphic[] foreground;
        public Color[] foregroundColors;
        public bool inverted;
    }

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
    private bool IsInLobby => lobbyRoom != null && RoomManager.Instance != null && RoomManager.Instance.CurrentRoom == lobbyRoom;
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
        terminalHome = terminalRoot.anchoredPosition;
        terminalRotation = terminalRoot.localRotation;
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
        if (wristRoot != null)
        {
            wristHome = wristRoot.anchoredPosition;
            wristRotation = wristRoot.localRotation;
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
                BindClick(button, () => SelectEntryFromPointer(tabIndex, entryIndex, true));
                BindHover(button, () => SelectEntryFromPointer(tabIndex, entryIndex, false));
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
            CacheButtonVisual(button);
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
        inventoryAction = new InputAction("Inventory", InputActionType.Button, "<Keyboard>/tab");
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

    private void CacheButtonVisual(UnityEngine.UI.Button button)
    {
        var foreground = new List<UnityEngine.UI.Graphic>();
        var statusMark = button.transform.Find("OnboardingMark");
        foreach (var graphic in button.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
        {
            // 장식 테두리와 상태 배지를 제외하고 글자·아이콘만 배경색으로 바꾼다.
            if (statusMark != null && graphic.transform.IsChildOf(statusMark)) continue;
            if (graphic is TMP_Text || graphic is TerminalEquipmentGlyph
                || IsButtonGlyph(graphic.transform, button.transform)) foreground.Add(graphic);
        }
        buttonVisuals[button] = new ButtonVisual
        {
            fill = button.transform.Find("SelectionFill")?.GetComponent<UnityEngine.UI.Graphic>(),
            face = button.targetGraphic, faceColor = button.targetGraphic != null ? button.targetGraphic.color : new Color(.06f, .14f, .2f),
            foreground = foreground.ToArray(), foregroundColors = new Color[foreground.Count]
        };
    }

    private static bool IsButtonGlyph(Transform graphic, Transform button)
    {
        if (graphic.name.StartsWith("CloseStroke", StringComparison.Ordinal)
            || graphic.name.StartsWith("ConfirmStroke", StringComparison.Ordinal)
            || graphic.name.StartsWith("Knurl", StringComparison.Ordinal)) return true;
        for (var parent = graphic; parent != null && parent != button; parent = parent.parent)
            if (parent.name == "Emblem" || parent.name == "CategoryGlyph") return true;
        return false;
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
    }
    private void OnDisable()
    {
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

    // 수료 표시만 읽는다. 실패 시 생성되는 초기 기록과 등록되지 않은 훈련은 집계하지 않는다.
    public void RefreshHomeStatistics()
    {
        if (homeDataManager == null) homeDataManager = UnityEngine.Object.FindFirstObjectByType<DataManager>();
        homeKnownTrainingIds.Clear();
        homeClearedTrainingIds.Clear();
        if (homeTrainingDefinitions != null)
            foreach (var training in homeTrainingDefinitions)
                if (training != null && !string.IsNullOrWhiteSpace(training.trainingId)) homeKnownTrainingIds.Add(training.trainingId);
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
        int gold = homeDataManager != null ? homeDataManager.CurrentGold : 0;
        SetHomeText(homeGoldText, gold.ToString("N0", CultureInfo.InvariantCulture));
        // 훈련 목표의 항목마다 실제 수료 여부를 보여 준다(위에서 모은 수료 기록 그대로).
        if (goalTrainings == null) return;
        for (int index = 0; index < goalTrainings.Length; index++)
        {
            bool cleared = goalTrainings[index] != null && homeClearedTrainingIds.Contains(goalTrainings[index].trainingId);
            if (goalStateLabels != null && index < goalStateLabels.Length && goalStateLabels[index] != null)
            {
                var label = goalStateLabels[index];
                // 수료 전의 글자색은 처음 본 색(Prefab 값)을 기억해 두고 쓴다.
                if (goalPendingColors == null || goalPendingColors.Length != goalStateLabels.Length)
                    goalPendingColors = Array.ConvertAll(goalStateLabels, source => source != null ? source.color : Color.white);
                SetHomeText(label, cleared ? goalClearedText : goalPendingText);
                Color wanted = cleared ? goalClearedColor : goalPendingColors[index];
                if (label.color != wanted) label.color = wanted;
            }
            if (goalClearedMarks != null && index < goalClearedMarks.Length && goalClearedMarks[index] != null && goalClearedMarks[index].activeSelf != cleared)
                goalClearedMarks[index].SetActive(cleared);
            if (goalPendingMarks != null && index < goalPendingMarks.Length && goalPendingMarks[index] != null && goalPendingMarks[index].activeSelf == cleared)
                goalPendingMarks[index].SetActive(!cleared);
        }
    }
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
        selectedHomeIndex = OpeningHomeIndex != null ? Mathf.Clamp(OpeningHomeIndex(), 0, sections.Length - 1) : 0;
        homeVisible = true;
        nextTabInputAt = float.NegativeInfinity;
        for (int tab = 0; tab < sections.Length; tab++) selectedEntries[tab] = FirstAvailableEntry(sections[tab]);
        listFocused = false;
        detailOpen = false;
        isClosing = false;
        CancelSlide();
        noticeRoot.SetActive(false);
        rootObject.SetActive(true);
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
        if (!DeclaredEntryAvailable(section, index) || section.entryButtons[index] == null) return false;
        return !entryAvailability.TryGetValue(section.entryButtons[index], out bool available) || available;
    }
    private int FirstAvailableEntry(SectionBinding section)
    {
        for (int index = 0; index < section.entryButtons.Length; index++)
            if (EntryAvailable(section, index)) return index;
        return 0;
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
        if (detailOpen && selectedTab == 2 && CanReceiveInput)
        {
            var tutorial = sections[2].details[SelectedEntry].GetComponentInChildren<TerminalTutorialVideo>(true);
            if (tutorial != null) { if (horizontal != 0) tutorial.SeekBySeconds(horizontal); return; }
        }
        if (!listFocused)
        {
            int nextTab = (selectedTab + horizontal + sections.Length) % sections.Length;
            if (horizontal != 0) SelectTab(nextTab, horizontal);
            else if (vertical > 0 && CanReceiveInput) { listFocused = true; RefreshSelection(); EnsureEntryVisible(sections[selectedTab], SelectedEntry); }
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
            if (along < 1 || (!vertical && across > 2)) continue;
            float score = vertical ? along * 10000 + across : along;
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
        if (detailOpen && selectedTab == 2)
        {
            var tutorial = sections[2].details[SelectedEntry].GetComponentInChildren<TerminalTutorialVideo>(true);
            if (tutorial != null) { tutorial.TogglePlayback(); return; }
        }
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
        var action = actions != null && SelectedEntry < actions.Length ? actions[SelectedEntry] : null;
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
        foreach (var pair in buttonVisuals)
        {
            var button = pair.Key;
            var visual = pair.Value;
            if (button == null || button.GetComponent<TerminalPlaybackButton>() != null) continue;
            bool selected = button.gameObject.activeInHierarchy && IsActive && !isClosing &&
                (button == hoveredButton || IsSelectedButton(button));
            if (button.TryGetComponent<TerminalButtonMotion>(out var motion)) motion.Select(selected);
            // 분류별 밝은 선택면과 짙은 전경으로 대비를 유지한다. 상세·안내 버튼도 현재 분류를 따른다.
            var selectionColor = SelectionColor(button);
            if (visual.fill != null) visual.fill.color = selected ? selectionColor : new Color(1f, 1f, 1f, 0f);
            else if (visual.face != null) visual.face.color = selected ? selectionColor : visual.faceColor;
            for (int i = 0; i < visual.foreground.Length; i++)
            {
                var graphic = visual.foreground[i];
                if (graphic == null) continue;
                if (selected && !visual.inverted) visual.foregroundColors[i] = graphic.color;
                if (selected)
                {
                    var ink = selectionInk;
                    ink.a = visual.foregroundColors[i].a;
                    // 아이콘 뒤의 기계식 받침은 비워 흰 선택면 위에 아이콘이 보이게 한다.
                    if (graphic is TerminalChassisGraphic) ink.a = 0f;
                    graphic.color = ink;
                }
                else if (visual.inverted) graphic.color = visual.foregroundColors[i];
            }
            visual.inverted = selected;
        }
    }

    private Color SelectionColor(UnityEngine.UI.Button button)
    {
        int category = homeVisible ? selectedHomeIndex : selectedTab;
        for (int i = 0; i < sections.Length; i++)
            if (button == homeButtons[i] || button == tabButtons[i]
                || (sections[i].root != null && button.transform.IsChildOf(sections[i].root.transform)))
            { category = i; break; }
        return selectionColors != null && selectionColors.Length > category ? selectionColors[category] : Color.white;
    }

    private bool IsSelectedButton(UnityEngine.UI.Button button)
    {
        if (NoticeVisible) return button.transform.IsChildOf(noticeRoot.transform);
        if (homeVisible) return button == homeButtons[selectedHomeIndex];
        if (button == tabButtons[selectedTab]) return true;
        var section = sections[selectedTab];
        if (!detailOpen) return listFocused && button == section.entryButtons[SelectedEntry];
        return listFocused && section.detailActions != null && SelectedEntry < section.detailActions.Length
            && button == section.detailActions[SelectedEntry];
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
                section.entryButtons[index].interactable = enabledInput && !homeVisible && !detailOpen && EntryAvailable(section, index);
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
        ? sections[selectedTab].details[SelectedEntry].GetComponent<RectTransform>() : sections[selectedTab].listRoot;
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
                bool visible = active && detailOpen && entry == selectedEntries[tab];
                var detail = sections[tab].details[entry];
                if (detail == null) continue;
                detail.SetActive(visible);
                if (visible)
                {
                    var page = (RectTransform)detail.transform;
                    page.localScale = pageScales[page] * .96f;
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
            next.localScale = Vector3.Scale(pageScales[next], PopupStartScale(selectedTab, SelectedEntry));
            next.localRotation = pageRotations[next] * Quaternion.Euler(0f, 0f, PopupStartAngle(selectedTab, SelectedEntry));
            pageGroups[next].alpha = 0f;
        }
        contentInputGroup.interactable = contentInputGroup.blocksRaycasts = false;
        RefreshSelection();
        pageRoutine = StartCoroutine(AnimatePopup(list, next, outgoing));
    }

    private static Vector3 PopupStartScale(int tab, int entry)
    {
        // 명령서·식별 스캔·장비 장착창·수신 문서를 서로 다른 펼침 축으로 구분한다.
        switch (tab)
        {
            case 0:
                if (entry == 1) return new Vector3(.68f, .68f, 1f);
                if (entry == 2) return new Vector3(.42f, .42f, 1f);
                if (entry == 3) return new Vector3(.2f, .91f, 1f);
                return new Vector3(.91f, .22f, 1f);
            case 1: return new Vector3(.12f, .93f, 1f);
            case 2:
                if (entry == 0) return new Vector3(.92f, .28f, 1f);
                if (entry == 1) return new Vector3(.18f, .92f, 1f);
                return entry == 2 ? new Vector3(.26f, .26f, 1f) : new Vector3(.68f, .68f, 1f);
            default: return new Vector3(.74f, .14f, 1f);
        }
    }

    private static float PopupStartAngle(int tab, int entry)
    {
        if (tab == 2) return entry == 2 ? -7f : entry % 2 == 0 ? -3f : 3f;
        return tab == 3 ? -2.5f : 0f;
    }

    private IEnumerator AnimatePopup(RectTransform list, RectTransform next, List<RectTransform> outgoing)
    {
        Vector3 listStart = list.localScale;
        float listAlpha = pageGroups[list].alpha;
        Vector3 nextScale = next != null ? next.localScale : Vector3.one;
        Quaternion nextRotation = next != null ? next.localRotation : Quaternion.identity;
        var oldScales = outgoing.ConvertAll(page => page.localScale);
        var oldAlphas = outgoing.ConvertAll(page => pageGroups[page].alpha);
        float duration = next == null ? .22f : selectedTab == 1 ? .34f : selectedTab == 3 ? .38f : .3f;
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
                next.localScale = Vector3.LerpUnclamped(nextScale, pageScales[next] * .96f, eased);
                next.localRotation = Quaternion.SlerpUnclamped(nextRotation, pageRotations[next], eased);
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
            ApplyPresentation(Mathf.Lerp(start, target, 1f - Mathf.Pow(1f - t, 3f)));
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
        float scale = Mathf.Min(lastFitSize.x / Mathf.Max(1f, terminalRoot.rect.width),
            lastFitSize.y / Mathf.Max(1f, terminalRoot.rect.height));
        terminalRoot.localScale = Vector3.one * (scale * Mathf.Lerp(.45f, 1f, progress));
        // 팝업 무대는 연출 중 크기 변화 없이 단말기의 최종 배율로 화면 중앙에 맞춘다.
        if (popupStage != null) popupStage.localScale = Vector3.one * scale;
        ApplyPopupLayer();
        terminalRoot.anchoredPosition = terminalHome + new Vector2(lastFitSize.x * .62f, -lastFitSize.y * .7f) * (1f - progress);
        terminalRoot.localRotation = terminalRotation * Quaternion.Euler(0f, 0f, Mathf.Lerp(-14f, 0f, progress));
        terminalGroup.alpha = Mathf.Clamp01(progress * 2f);
        screenGroup.alpha = Mathf.InverseLerp(.25f, .85f, progress);
        backdropGroup.alpha = progress;
        terminalGroup.interactable = terminalGroup.blocksRaycasts = progress > .99f;
        screenGroup.interactable = screenGroup.blocksRaycasts = progress > .99f;
        backdropGroup.interactable = backdropGroup.blocksRaycasts = progress > 0f;
        if (wristRoot == null) return;
        wristRoot.anchoredPosition = wristHome + new Vector2(20f, -45f) * (1f - progress);
        wristRoot.localRotation = wristRotation * Quaternion.Euler(0f, 0f, Mathf.Lerp(-8f, 0f, progress));
    }
}
