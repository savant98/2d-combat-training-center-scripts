using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>게임 키와 독립적인 메뉴 입력, 설정 미리보기·저장·복구를 담당한다.</summary>
public class OptionMenu : SelectableMenuUI
{
    [Serializable]
    public class Row
    {
        public RectTransform root;
        public TextMeshProUGUI value;
        public UnityEngine.UI.Image focus;
        public UnityEngine.UI.Slider slider;
        public string action;
        public string part;
    }

    [SerializeField] private GameObject rootObject;
    [SerializeField] private CanvasGroup optionCanvas;
    [SerializeField] private RectTransform frame;
    [SerializeField] private GameObject[] pages;
    [SerializeField] private UnityEngine.UI.Image[] tabFills;
    [SerializeField] private Row[] optionRows;
    [SerializeField] private TextMeshProUGUI pageTitle;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private GameObject dialog;
    [SerializeField] private TextMeshProUGUI dialogText;
    [SerializeField] private TextMeshProUGUI acceptText;
    [SerializeField] private UnityEngine.UI.Image[] dialogFocus;

    [SerializeField] private UnityEngine.UI.ScrollRect displayScroll;
    [SerializeField] private UnityEngine.UI.ScrollRect controlsScroll;
    [SerializeField] private GameObject controlsHome;
    [SerializeField] private GameObject controlsDetail;
    [SerializeField] private AudioCreditsPanel audioCredits;
    [SerializeField] private TextMeshProUGUI storeSkillHint;
    [SerializeField] private UnityEngine.UI.Button storeSkillClear;
    private const int StoreSkillRow = 23;
    private const int ControlModeRow = 24;
    private const int AutoAimRow = 25;
    private const int OpenKeysRow = 26;
    private const int BackFromKeysRow = 27;
    private static readonly Guid StoreSkillBindingId = new Guid("ed98d38e-dbb6-49ad-a536-196d7492c04c");
    private OptionSettings saved, draft;
    private bool showingKeys;
    private readonly List<Vector2Int> resolutions = new();
    private static readonly int[] FrameRates = { 60, 120, 144, 165, 240, -1 };
    private static readonly string[] PageNames = { "화면 · 시각 효과", "사운드", "조작" };
    public enum NavigationStage { Categories, Items, Editing, Footer }
    private NavigationStage navigationStage;
    private static readonly int[][] PageRows =
    {
        new[] { 20, 21, 0, 1, 22, 2, 3, 4, 5 },
        new[] { 6, 7, 8 },
        new[] { 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, StoreSkillRow }
    };
    private static readonly int[] ControlRows = { ControlModeRow, AutoAimRow, OpenKeysRow };
    private static readonly int[] KeyDetailRows = { BackFromKeysRow, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, StoreSkillRow };
    private int[] CurrentRows => page == 2 ? (showingKeys ? KeyDetailRows : ControlRows) : PageRows[page];
    private UnityEngine.UI.ScrollRect CurrentScroll => page == 0 ? displayScroll : page == 2 && showingKeys ? controlsScroll : null;
    private int RowCount => CurrentRows.Length;
    private int SelectedRow => CurrentRows[Mathf.Clamp(selection, 0, RowCount - 1)];
    private OptionSettings editSnapshot;
    private bool dirtyBeforeEdit;
    [SerializeField] private TextMeshProUGUI navigationHint;
    [SerializeField] private TextMeshProUGUI navigationStageText;
    [SerializeField] private UnityEngine.UI.Image[] tabFocus;
    private InputActionRebindingExtensions.RebindingOperation rebind;
    private int page, selection, dialogIndex;
    private int hoveredCategory = -1;
    private float inputReadyAt, displayDeadline;
    private Action acceptDialog, rejectDialog;
    private bool waitingDisplay, dirty, closing;
    private bool suppressMoveFeedback;
    private EventSystem inputEventSystem;
    private bool previousNavigationEvents;
    public bool IsRebinding => rebind != null;

    protected override void Awake()
    {
        inputLayer = UIInputLayer.Pause;
        foreach (var trigger in GetComponentsInChildren<EventTrigger>(true))
            if (trigger.GetComponent<MenuScrollRelay>() == null && trigger.GetComponentInParent<UnityEngine.UI.ScrollRect>() != null)
                trigger.gameObject.AddComponent<MenuScrollRelay>();
        BuildMenuMapping();
        base.Awake();
        for (int i = 0; i < tabFills.Length; i++)
        {
            int index = i;
            var tab = tabFills[i];
            tab.raycastTarget = true;
            var trigger = tab.GetComponent<EventTrigger>() ?? tab.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => SetHoveredCategory(index));
            trigger.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                if (hoveredCategory == index) SetHoveredCategory(-1);
            });
            trigger.triggers.Add(exit);
        }
        for (int i = 0; i < optionRows.Length; i++)
        {
            int index = i;
            var row = optionRows[i];
            var trigger = row.root.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            entry.callback.AddListener(_ =>
            {
                if (Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > .01f) SelectPointerRow(index);
            });
            trigger.triggers.Add(entry);
            if (row.root.GetComponentInParent<UnityEngine.UI.ScrollRect>() != null && row.root.GetComponent<MenuScrollRelay>() == null)
                row.root.gameObject.AddComponent<MenuScrollRelay>();
            if (row.slider != null) row.slider.onValueChanged.AddListener(value => ChangeSlider(index, value));
        }
        for (int i = 0; i < footerFocus.Length; i++)
        {
            int index = i;
            var trigger = footerFocus[i].transform.parent.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (!CanUsePointer() || Mouse.current == null || Mouse.current.delta.ReadValue().sqrMagnitude < .01f) return;
                FinishEditing(false);
                hoveredCategory = -1;
                navigationStage = NavigationStage.Footer; selection = index; RefreshFocus();
            });
            trigger.triggers.Add(enter);
        }
        foreach (var button in rootObject.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            button.onClick.AddListener(() => PlayFeedback(confirmFeedback));
        foreach (var trigger in rootObject.GetComponentsInChildren<EventTrigger>(true))
            if (trigger.GetComponentInParent<UnityEngine.UI.ScrollRect>() != null && trigger.GetComponent<MenuScrollRelay>() == null)
                trigger.gameObject.AddComponent<MenuScrollRelay>();
        optionCanvas.ignoreParentGroups = true;
        rootObject.SetActive(false);
    }

    private void BuildMenuMapping()
    {
        var targets = new List<RectTransform>();
        var highlights = new List<UnityEngine.UI.Image>();
        var labels = new List<TextMeshProUGUI>();
        Action<Transform, UnityEngine.UI.Image, string> add = (root, image, name) =>
        {
            var label = root.Find(name).GetComponent<TextMeshProUGUI>();
            targets.Add(label.rectTransform); highlights.Add(image); labels.Add(label);
        };
        for (int i = 0; i < tabFills.Length; i++) add(tabFills[i].transform, tabFocus[i], "Name");
        foreach (var row in optionRows) add(row.root, row.focus, "Name");
        foreach (var image in footerFocus) add(image.transform.parent, image, "Label");
        foreach (var image in dialogFocus) add(image.transform.parent, image, "Label");
        buttons = targets.ToArray(); images = highlights.ToArray(); texts = labels.ToArray();
    }

    private void OnEnable()
    {
        TitleMenu.OnOptionSelected += OpenMenu;
        PauseMenuController.OnOptionSelected += OpenMenu;
    }

    private void OnDisable()
    {
        if (audioCredits != null) audioCredits.Close();
        TitleMenu.OnOptionSelected -= OpenMenu;
        PauseMenuController.OnOptionSelected -= OpenMenu;
        if (IsActive) CloseMenu(false, true);
    }

    public void OpenMenu()
    {
        if (IsActive || closing) return;
        saved = DataManager.Instance.Options.Copy();
        DisplayOptions.NormalizeMonitor(saved);
        // 독점 화면을 바꾸기 전에 0(자동)을 구체적인 원본 크기로 고정해 취소 시 되돌린다.
        int originalWidth = DisplayOptions.Width(saved), originalHeight = DisplayOptions.Height(saved);
        saved.width = originalWidth; saved.height = originalHeight;
        draft = saved.Copy();
        showingKeys = false;
        BuildResolutions();
        dirty = false;
        rootObject.SetActive(true);
        dialog.SetActive(false);
        foreach (var button in buttons) { button.DOKill(); button.localScale = Vector3.one; }
        base.Open();
        var canvas = rootObject.GetComponent<Canvas>();
        var parentCanvas = GetComponentInParent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingLayerID = parentCanvas.sortingLayerID;
        canvas.worldCamera = parentCanvas.worldCamera;
        canvas.sortingOrder = Mathf.Max(200, parentCanvas.sortingOrder + 1);
        inputEventSystem = EventSystem.current;
        if (inputEventSystem != null)
        {
            previousNavigationEvents = inputEventSystem.sendNavigationEvents;
            inputEventSystem.sendNavigationEvents = false;
            inputEventSystem.SetSelectedGameObject(null);
        }
        inputReadyAt = Time.unscaledTime + .2f;
        optionCanvas.DOKill();
        optionCanvas.alpha = 0;
        optionCanvas.interactable = optionCanvas.blocksRaycasts = true;
        optionCanvas.DOFade(1f, .18f).SetUpdate(true);
        ShowPage(0);
        Resize();
    }

    private bool CanUsePointer() => IsActive && !IsRebinding && !dialog.activeSelf && !closing &&
        (UIManager.Instance == null || (!UIManager.Instance.IsUIInputBlocked && UIManager.Instance.IsCurrentInputTarget(this)));

    private void SetHoveredCategory(int index)
    {
        if (!CanUsePointer() || hoveredCategory == index) return;
        hoveredCategory = index;
        RefreshFocus();
    }

    public void ShowPage(int index)
    {
        if (!CanUsePointer()) return;
        hoveredCategory = -1;
        page = (index + pages.Length) % pages.Length;
        selection = 0;
        navigationStage = NavigationStage.Categories;
        editSnapshot = null;
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].SetActive(i == page);
            tabFills[i].color = i == page ? new Color(.08f, .3f, .4f, 1) : TrainingBoardStyle.SlotFill;
        }
        if (page == 2) SetKeyPage(false);
        else pageTitle.text = PageNames[page];
        Refresh();
    }

    private void SetKeyPage(bool detail)
    {
        showingKeys = detail;
        controlsHome.SetActive(!detail);
        controlsDetail.SetActive(detail);
        pageTitle.text = detail ? ModeLabel + " · 키 설정" : PageNames[2];
        if (detail)
        {
            Canvas.ForceUpdateCanvases();
            controlsScroll.StopMovement();
            controlsScroll.verticalNormalizedPosition = 1f;
        }
    }

    private string ModeLabel => draft.mouseAim ? "마우스 조준 모드" : "기본 모드";

    private void SaveDraftBindings()
    {
        if (InputManager.Instance != null) draft.ActiveBindingOverrides = InputManager.Instance.Actions.SaveBindingOverridesAsJson();
    }

    private void Resize()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var size = ((RectTransform)canvas.rootCanvas.transform).rect.size;
        ((RectTransform)rootObject.transform).sizeDelta = size;
        frame.localScale = Vector3.one * Mathf.Min(size.x * .95f / frame.rect.width, size.y * .95f / frame.rect.height);
    }

    private void Update()
    {
        if (!IsActive) return;
        Resize();
        if (page == 2 && navigationStage == NavigationStage.Items && SelectedRow == StoreSkillRow
            && CanReceiveMenuInput && !dialog.activeSelf && Keyboard.current?.backspaceKey.wasPressedThisFrame == true)
            ClearStoreSkillBinding();
        if (waitingDisplay)
        {
            int seconds = Mathf.CeilToInt(displayDeadline - Time.unscaledTime);
            dialogText.text = $"화면 설정을 유지할까요?\n{seconds}초 안에 확인하지 않으면 이전 화면으로 돌아갑니다.";
            if (seconds <= 0) { RejectDialog(); return; }
        }
    }

    private bool CanReceiveMenuInput => IsActive && !closing && !IsRebinding && Time.unscaledTime >= inputReadyAt &&
        UIManager.Instance != null && !UIManager.Instance.IsUIInputBlocked && UIManager.Instance.IsCurrentInputTarget(this);

    protected override void ProcessMove(Vector2 direction)
    {
        if (!CanReceiveMenuInput) return;
        SetHoveredCategory(-1);
        Navigate(direction);
    }

    public override void OnConfirm()
    {
        if (!CanReceiveMenuInput) return;
        SetHoveredCategory(-1);
        base.OnConfirm();
        WithoutMoveFeedback(ConfirmNavigation);
    }

    public override void OnCancel()
    {
        if (!CanReceiveMenuInput) return;
        SetHoveredCategory(-1);
        base.OnCancel();
        WithoutMoveFeedback(NavigateBack);
    }

    private void WithoutMoveFeedback(Action action)
    {
        bool previous = suppressMoveFeedback;
        suppressMoveFeedback = true;
        try { action(); }
        finally { suppressMoveFeedback = previous; }
    }

    // 실제 클릭은 각 Button/Slider가 한 번만 처리한다.
    protected override void OnPointerClick(int index) { }

    private void Navigate(Vector2 move)
    {
        if (dialog.activeSelf)
        {
            var candidates = new List<RectTransform>();
            foreach (var focus in dialogFocus) candidates.Add((RectTransform)focus.transform.parent);
            int next = FindDirectionalIndex(candidates[dialogIndex], move, candidates);
            if (next >= 0) { dialogIndex = next; RefreshDialogFocus(); }
            return;
        }
        bool vertical = Mathf.Abs(move.y) > .5f;
        int direction = vertical ? (move.y > 0 ? -1 : 1) : (move.x > 0 ? 1 : -1);
        switch (navigationStage)
        {
            case NavigationStage.Categories:
                if (!vertical)
                {
                    if (direction > 0) MoveFromCategory(move);
                    return;
                }
                if (page == pages.Length - 1 && direction > 0) MoveFromCategory(Vector2.down);
                else ShowPage(Mathf.Clamp(page + direction, 0, pages.Length - 1));
                break;
            case NavigationStage.Items:
                MoveFromItem(move);
                break;
            case NavigationStage.Editing:
                if (!vertical) AdjustRow(SelectedRow, direction);
                break;
            case NavigationStage.Footer:
                MoveFromFooter(move);
                break;
        }
    }

    // UpgradeMenu처럼 같은 행·열의 이웃을 우선하되, 배열 순서 대신 실제 버튼 중심으로 고른다.
    private int FindDirectionalIndex(RectTransform origin, Vector2 direction, IList<RectTransform> candidates)
    {
        direction.Normalize();
        Vector2 start = frame.InverseTransformPoint(origin.TransformPoint(origin.rect.center));
        Vector2 side = new Vector2(-direction.y, direction.x);
        int closest = -1;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate == origin || !candidate.gameObject.activeInHierarchy) continue;
            Vector2 delta = (Vector2)frame.InverseTransformPoint(candidate.TransformPoint(candidate.rect.center)) - start;
            float forward = Vector2.Dot(delta, direction);
            if (forward < 1f) continue;
            float score = forward + Mathf.Abs(Vector2.Dot(delta, side)) * 3f;
            if (score < bestScore) { bestScore = score; closest = i; }
        }
        return closest;
    }

    private List<RectTransform> ItemRects()
    {
        var candidates = new List<RectTransform>();
        foreach (int index in CurrentRows) candidates.Add(optionRows[index].root);
        return candidates;
    }

    private List<RectTransform> FooterRects()
    {
        var candidates = new List<RectTransform>();
        foreach (var focus in footerFocus) candidates.Add((RectTransform)focus.transform.parent);
        return candidates;
    }

    private void MoveFromCategory(Vector2 direction)
    {
        bool toItems = direction.x > .5f;
        var candidates = toItems ? ItemRects() : FooterRects();
        int next = FindDirectionalIndex(tabFills[page].rectTransform, direction, candidates);
        if (next < 0) return;
        navigationStage = toItems ? NavigationStage.Items : NavigationStage.Footer;
        selection = next;
        RefreshFocus();
    }

    private void MoveFromItem(Vector2 direction)
    {
        if (CurrentScroll != null && Mathf.Abs(direction.y) > .5f)
        {
            int nextRow = selection + (direction.y > 0 ? -1 : 1);
            if (nextRow >= 0 && nextRow < RowCount) { selection = nextRow; RefreshFocus(); return; }
            if (direction.y < 0) { navigationStage = NavigationStage.Footer; selection = 1; RefreshFocus(); }
            return;
        }
        var candidates = ItemRects();
        if (Mathf.Abs(direction.y) > .5f) candidates.AddRange(FooterRects());
        int next = FindDirectionalIndex(optionRows[SelectedRow].root, direction, candidates);
        if (next >= 0)
        {
            navigationStage = next < RowCount ? NavigationStage.Items : NavigationStage.Footer;
            selection = next < RowCount ? next : next - RowCount;
            RefreshFocus();
        }
        else if (direction.x < -.5f)
        {
            navigationStage = NavigationStage.Categories;
            selection = 0;
            RefreshFocus();
        }
    }

    private void MoveFromFooter(Vector2 direction)
    {
        var footer = FooterRects();
        var origin = footer[selection];
        if (direction.y > .5f)
        {
            var candidates = ItemRects();
            candidates.Add(tabFills[page].rectTransform);
            int next = FindDirectionalIndex(origin, direction, candidates);
            if (next < 0) return;
            navigationStage = next < RowCount ? NavigationStage.Items : NavigationStage.Categories;
            selection = next < RowCount ? next : 0;
        }
        else
        {
            int next = FindDirectionalIndex(origin, direction, footer);
            if (next < 0) return;
            selection = next;
        }
        RefreshFocus();
    }

    private void ConfirmNavigation()
    {
        if (dialog.activeSelf) { if (dialogIndex == 0) AcceptDialog(); else RejectDialog(); return; }
        switch (navigationStage)
        {
            case NavigationStage.Categories:
                navigationStage = NavigationStage.Items; selection = 0; RefreshFocus(); break;
            case NavigationStage.Items:
                BeginEditing(); break;
            case NavigationStage.Editing:
                FinishEditing(false); break;
            case NavigationStage.Footer:
                if (selection == 0) RequestReset();
                else if (selection == 1) CancelOrClose();
                else if (selection == 2) Apply();
                else if (selection == 3) OpenAudioCredits();
                break;
        }
        inputReadyAt = Time.unscaledTime + .12f;
    }

    // 출처를 읽는 동안 설정 초안과 선택 위치를 유지하고 입력만 자식 패널로 넘긴다.
    public void OpenAudioCredits()
    {
        if (!CanUsePointer() || audioCredits == null) return;
        FinishEditing(false);
        navigationStage = NavigationStage.Footer;
        selection = 3;
        WithoutMoveFeedback(RefreshFocus);
        audioCredits.ConfigureFeedback(confirmFeedback, cancelFeedback);
        audioCredits.Open();
    }

    private void BeginEditing()
    {
        if (SelectedRow == 22 && !DisplayOptions.CanChangeRefresh(draft))
        {
            statusText.text = "주사율 변경은 독점 전체 화면에서 적용됩니다. 창·테두리 없는 모드는 OS 주사율을 사용합니다.";
            return;
        }
        if (SelectedRow == 3 && draft.vSync)
        {
            statusText.text = "프레임 제한을 바꾸려면 수직 동기화를 먼저 꺼 주세요.";
            return;
        }
        if (SelectedRow is ControlModeRow or AutoAimRow or OpenKeysRow or BackFromKeysRow)
        {
            AdjustRow(SelectedRow, 1);
            return;
        }
        navigationStage = NavigationStage.Editing;
        editSnapshot = draft.Copy();
        dirtyBeforeEdit = dirty;
        if (!string.IsNullOrEmpty(optionRows[SelectedRow].action)) BeginRebind(SelectedRow);
        RefreshFocus();
    }

    private void FinishEditing(bool revert)
    {
        if (navigationStage != NavigationStage.Editing) return;
        if (revert && editSnapshot != null)
        {
            draft = editSnapshot;
            BuildResolutions();
            dirty = dirtyBeforeEdit;
            OptionSettingsService.Instance.ApplyPreview(draft);
        }
        editSnapshot = null;
        navigationStage = NavigationStage.Items;
        Refresh();
    }

    private void NavigateBack()
    {
        if (dialog.activeSelf) RejectDialog();
        else if (navigationStage == NavigationStage.Editing) FinishEditing(true);
        else if (page == 2 && showingKeys) AdjustRow(BackFromKeysRow, 1);
        else if (navigationStage == NavigationStage.Items || navigationStage == NavigationStage.Footer)
        {
            navigationStage = NavigationStage.Categories; selection = 0; RefreshFocus();
        }
        else CancelOrClose();
        inputReadyAt = Time.unscaledTime + .12f;
    }

    private void SelectPointerRow(int index)
    {
        if (!CanUsePointer()) return;
        int next = Array.IndexOf(CurrentRows, index);
        if (next < 0) return;
        if (navigationStage == NavigationStage.Editing && SelectedRow != index) FinishEditing(false);
        hoveredCategory = -1;
        selection = next;
        if (navigationStage != NavigationStage.Editing) navigationStage = NavigationStage.Items;
        RefreshFocus();
    }

    public void ActivateRow(int index)
    {
        if (!CanUsePointer() || Array.IndexOf(CurrentRows, index) < 0) return;
        SelectPointerRow(index);
        if (!string.IsNullOrEmpty(optionRows[index].action)) BeginRebind(index);
        else AdjustRow(index, 1);
    }

    public void IncreaseRow(int index) { SelectPointerRow(index); AdjustRow(index, 1); }
    public void DecreaseRow(int index) { SelectPointerRow(index); AdjustRow(index, -1); }

    public void AdjustRow(int index, int direction)
    {
        if (!CanUsePointer() || index < 0 || index >= optionRows.Length || !string.IsNullOrEmpty(optionRows[index].action)) return;
        switch (index)
        {
            case ControlModeRow:
                SaveDraftBindings();
                draft.mouseAim = !draft.mouseAim;
                InputManager.Instance?.LoadControlProfile(draft);
                break;
            case AutoAimRow:
                draft.rangedAutoAim = !draft.rangedAutoAim;
                break;
            case OpenKeysRow:
            case BackFromKeysRow:
                SetKeyPage(index == OpenKeysRow);
                navigationStage = NavigationStage.Items;
                selection = showingKeys ? 1 : Array.IndexOf(ControlRows, OpenKeysRow);
                Refresh();
                return;
            case 20: draft.language = draft.language == "en" ? "ko" : "en"; break;
            case 0:
                int mode = draft.windowed ? 0 : DisplayOptions.Mode(draft) == FullScreenMode.ExclusiveFullScreen ? 2 : 1;
                int modeCount = DisplayOptions.CanUseExclusive(draft) ? 3 : 2;
                mode = (mode + direction + modeCount) % modeCount;
                draft.windowed = mode == 0; draft.exclusiveFullscreen = mode == 2; break;
            case 21:
                var displays = DisplayOptions.Layout();
                if (displays.Count < 2) return;
                int monitor = (DisplayOptions.Monitor(draft, displays) + direction + displays.Count) % displays.Count;
                draft.monitorIndex = monitor; draft.monitorName = displays[monitor].name;
                draft.width = draft.height = 0;
                draft.refreshNumerator = draft.refreshDenominator = 0;
                BuildResolutions(); break;
            case 22:
                if (!DisplayOptions.CanChangeRefresh(draft)) return;
                var rates = DisplayOptions.Rates(draft);
                int rateIndex = rates.FindIndex(r => r.numerator == draft.refreshNumerator && r.denominator == draft.refreshDenominator);
                var rate = rates[(Mathf.Max(0, rateIndex) + direction + rates.Count) % rates.Count];
                draft.refreshNumerator = rate.numerator; draft.refreshDenominator = rate.denominator; break;
            case 1:
                int resolutionIndex = resolutions.FindIndex(r => r.x == EffectiveWidth(draft) && r.y == EffectiveHeight(draft));
                var res = resolutions[(resolutionIndex + direction + resolutions.Count) % resolutions.Count];
                draft.width = res.x; draft.height = res.y; draft.refreshNumerator = draft.refreshDenominator = 0; break;
            case 2: draft.vSync = !draft.vSync; break;
            case 3:
                if (draft.vSync) return;
                draft.frameRate = FrameRates[(Array.IndexOf(FrameRates, draft.frameRate) + direction + FrameRates.Length) % FrameRates.Length]; break;
            default: ChangeSlider(index, Mathf.Clamp01(GetSlider(index) + .05f * direction)); return;
        }
        Changed();
    }

    private float GetSlider(int index) => index switch { 4 => draft.bloom, 5 => draft.flash, 6 => draft.masterVolume, 7 => draft.bgmVolume, _ => draft.sfxVolume };
    private void ChangeSlider(int index, float value)
    {
        if (!CanUsePointer() || draft == null) return;
        SelectPointerRow(index);
        switch (index) { case 4: draft.bloom = value; break; case 5: draft.flash = value; break; case 6: draft.masterVolume = value; break; case 7: draft.bgmVolume = value; break; case 8: draft.sfxVolume = value; break; default: return; }
        Changed();
    }

    private void Changed()
    {
        dirty = true;
        BuildResolutions();
        OptionSettingsService.Instance.ApplyPreview(draft);
        Refresh();
    }

    private void Refresh()
    {
        optionRows[0].value.text = draft.windowed ? "창 모드" : DisplayOptions.Mode(draft) == FullScreenMode.ExclusiveFullScreen ? "독점 전체 화면" : "테두리 없는 전체 화면";
        var displayLayout = DisplayOptions.Layout();
        int monitor = DisplayOptions.Monitor(draft, displayLayout);
        optionRows[21].value.text = monitor >= 0 ? $"{monitor + 1}. {displayLayout[monitor].name}" : "기본 모니터";
        optionRows[21].value.enableAutoSizing = true;
        optionRows[21].value.fontSizeMin = 14;
        var rate = DisplayOptions.Rate(draft);
        optionRows[22].value.text = !DisplayOptions.CanChangeRefresh(draft) ? "OS 설정 사용" : rate.numerator == 0 ? "자동 (지원 최고)" : $"{rate.value:0.##} Hz";
        optionRows[22].value.color = DisplayOptions.CanChangeRefresh(draft) ? Color.white : new Color(.5f, .6f, .65f);
        optionRows[1].value.text = $"{EffectiveWidth(draft)} × {EffectiveHeight(draft)}";
        optionRows[2].value.text = draft.vSync ? "켜짐" : "꺼짐";
        optionRows[3].value.text = draft.vSync ? "VSync에 맞춤" : draft.frameRate == -1 ? "제한 없음" : draft.frameRate + " FPS";
        optionRows[3].value.color = draft.vSync ? new Color(.5f, .6f, .65f) : Color.white;
        for (int i = 4; i < 9; i++)
        {
            optionRows[i].slider.SetValueWithoutNotify(GetSlider(i));
            optionRows[i].value.text = Mathf.RoundToInt(GetSlider(i) * 100f) + "%";
        }
        optionRows[20].value.text = draft.language == "en" ? "English" : "한국어";
        optionRows[ControlModeRow].value.text = ModeLabel;
        optionRows[AutoAimRow].value.text = draft.rangedAutoAim ? "켜짐" : "꺼짐";
        foreach (int i in PageRows[2])
        {
            var row = optionRows[i];
            var (action, binding) = FindRowBinding(row);
            var name = row.root.Find("Name").GetComponent<TextMeshProUGUI>();
            name.text = ControlName(row);
            row.value.text = binding < 0 ? "미지정" : BindingLabel(action, binding);
        }
        if (storeSkillHint != null)
        {
            storeSkillHint.text = $"스킬 저장 · 저장 스킬 사용  {InputBindingDisplay.StoreSkill}\n사용 중인 키는 확인 후 교환합니다. 전용 키를 지우려면 기본 조합 버튼 또는 Backspace를 누르세요.";
        }
        if (storeSkillClear != null)
        {
            var (action, binding) = FindRowBinding(optionRows[StoreSkillRow]);
            storeSkillClear.interactable = binding >= 0 && !string.IsNullOrEmpty(action.bindings[binding].effectivePath);
        }
        statusText.text = dirty ? "변경 사항 미저장 · 소리와 시각 효과는 미리 적용 중" : "저장된 설정 · 변경 후 [적용 및 저장]을 눌러 주세요";
        RefreshFocus();
    }

    private void RefreshFocus()
    {
        if (CurrentScroll != null && !dialog.activeSelf && SelectedRow != BackFromKeysRow &&
            (navigationStage == NavigationStage.Items || navigationStage == NavigationStage.Editing))
            KeepRowVisible(CurrentScroll);
        int target = dialog.activeSelf ? 3 + optionRows.Length + footerFocus.Length + dialogIndex
            : hoveredCategory >= 0 ? hoveredCategory : navigationStage switch
        {
            NavigationStage.Categories => page,
            NavigationStage.Items => 3 + SelectedRow,
            NavigationStage.Editing => 3 + SelectedRow,
            _ => 3 + optionRows.Length + selection
        };
        for (int i = 0; i < images.Length; i++)
            if (images[i] is TrainingHologramGraphic graphic)
                graphic.Configure(i >= 3 && i < 3 + optionRows.Length ? TrainingHologramGraphic.Shape.FocusFrame : TrainingHologramGraphic.Shape.Frame,
                    navigationStage == NavigationStage.Editing && i == target ? TrainingBoardStyle.Gold : TrainingBoardStyle.Line,
                    2f, 9f, .16f);
        if (suppressMoveFeedback)
        {
            if (currentIndex != target) { currentIndex = target; UpdateHighlight(false); }
        }
        else ChangeIndex(target);
        // 빠른 이동에서도 이전 행의 배경이 남아 두 개가 선택된 것처럼 보이지 않게 한다.
        for (int i = 0; i < images.Length; i++)
        {
            if (i == target) continue;
            images[i].DOKill();
            images[i].color = new Color(1f, 1f, 1f, 0f);
        }
        if (navigationStageText != null) navigationStageText.text = navigationStage switch
        {
            NavigationStage.Categories => "01 / 분류 선택",
            NavigationStage.Items => "02 / 항목 선택",
            NavigationStage.Editing => "03 / 값 편집",
            _ => "저장 · 닫기"
        };
        if (navigationHint != null) navigationHint.text = InputBindingDisplay.Format(navigationStage switch
        {
            NavigationStage.Categories => "{MenuUp} {MenuDown} 분류 선택   {Confirm} / {MenuRight} 항목으로   {Cancel} 닫기",
            NavigationStage.Items when page == 2 && showingKeys => "{MenuUp} {MenuDown} 항목 이동   {Confirm} 선택   {Cancel} 조작 설정으로",
            NavigationStage.Items => "{MenuUp} {MenuDown} {MenuLeft} {MenuRight} 항목 선택   {Confirm} 편집   {Cancel} 분류로",
            NavigationStage.Editing => IsRebinding ? "새 키 입력   Esc 취소   10초 뒤 자동 취소" : "{MenuLeft} {MenuRight} 값 조절   {Confirm} 완료   {Cancel} 편집 취소",
            _ => "{MenuLeft} {MenuRight} 버튼 선택   {MenuUp} 항목으로   {Confirm} 실행   {Cancel} 분류로"
        });
    }
    private void KeepRowVisible(UnityEngine.UI.ScrollRect scroll)
    {
        Canvas.ForceUpdateCanvases();
        var row = optionRows[SelectedRow].root;
        var corners = new Vector3[4]; row.GetWorldCorners(corners);
        float bottom = scroll.viewport.InverseTransformPoint(corners[0]).y;
        float top = scroll.viewport.InverseTransformPoint(corners[1]).y;
        Rect area = scroll.viewport.rect;
        float shift = top > area.yMax ? area.yMax - top : bottom < area.yMin ? area.yMin - bottom : 0;
        if (Mathf.Abs(shift) > .1f) { scroll.StopMovement(); scroll.content.anchoredPosition += Vector2.up * shift; }
    }
    [SerializeField] private UnityEngine.UI.Image[] footerFocus;

    private static int EffectiveWidth(OptionSettings s) => DisplayOptions.Width(s);
    private static int EffectiveHeight(OptionSettings s) => DisplayOptions.Height(s);
    private void BuildResolutions()
    {
        resolutions.Clear();
        resolutions.AddRange(DisplayOptions.Resolutions(draft));
    }

    public static int FindBinding(InputAction action, string part)
    {
        if (action == null) return -1;
        for (int i = 0; i < action.bindings.Count; i++)
        {
            var b = action.bindings[i];
            if (!b.isComposite && (b.id == StoreSkillBindingId || (b.path ?? "").StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(part) || string.Equals(b.name, part, StringComparison.OrdinalIgnoreCase))) return i;
        }
        return -1;
    }

    public void BeginRebind(int index)
    {
        if (!CanUsePointer() || InputManager.Instance == null || page != 2 || !showingKeys || Array.IndexOf(PageRows[2], index) < 0) return;
        var manager = InputManager.Instance;
        var row = optionRows[index];
        var (action, binding) = FindRowBinding(row);
        if (binding < 0) return;
        SelectPointerRow(index);
        navigationStage = NavigationStage.Editing;
        var previousBinding = action.bindings[binding];
        string previousLabel = BindingLabel(action, binding);
        string before = manager.Actions.SaveBindingOverridesAsJson();
        manager.SuspendForRebind(true);
        optionCanvas.interactable = false;
        statusText.text = "새 키를 누르세요 · Esc 취소 · 10초 뒤 자동 취소";
        row.value.text = "입력 대기…";
        try
        {
            rebind = action.PerformInteractiveRebinding(binding).WithControlsHavingToMatchPath("<Keyboard>")
                .WithExpectedControlType<UnityEngine.InputSystem.Controls.ButtonControl>()
                .WithCancelingThrough("<Keyboard>/escape").WithTimeout(10f).WithActionEventNotificationsBeingSuppressed()
                .OnCancel(_ => FinishRebind(before, "키 변경을 취소했습니다."))
                .OnComplete(_ => CompleteBindingCandidate(index, action, binding, previousBinding, previousLabel, before));
            if (draft.mouseAim)
            {
                rebind.WithControlsHavingToMatchPath("<Mouse>/leftButton")
                    .WithControlsHavingToMatchPath("<Mouse>/rightButton")
                    .WithControlsHavingToMatchPath("<Mouse>/middleButton")
                    .WithControlsHavingToMatchPath("<Mouse>/backButton")
                    .WithControlsHavingToMatchPath("<Mouse>/forwardButton");
            }
            rebind.Start();
            RefreshFocus();
        }
        catch (Exception e) { FinishRebind(before, "키 변경을 시작하지 못했습니다: " + e.Message); }
    }

    private void CompleteBindingCandidate(int index, InputAction action, int binding, InputBinding previousBinding, string previousLabel, string before)
    {
        try
        {
            string path = action.bindings[binding].effectivePath;
            string previousPath = previousBinding.effectivePath;
            if (string.Equals(path, previousPath, StringComparison.OrdinalIgnoreCase))
            {
                FinishRebind(before, "기존 키를 유지했습니다.");
                return;
            }

            string keyLabel = BindingLabel(action, binding);
            var conflicts = GetRowsAtPath(index, path);
            ApplyBindingPath(action, binding, path, previousBinding);
            if (conflicts.Count == 0)
            {
                dirty = true;
                FinishRebind(null, "키 변경 완료 · 적용 및 저장을 눌러 주세요.");
                return;
            }

            // 위 방향과 상호작용처럼 이미 공유하는 키들은 같은 묶음으로 교환한다.
            // 대상 조작의 이전 키가 미지정이면 그 상태까지 서로 교환한다.
            var current = GetRowsAtPath(index, previousPath);
            ChangeRowsPath(current, path);
            ChangeRowsPath(conflicts, previousPath);
            string candidate = InputManager.Instance.Actions.SaveBindingOverridesAsJson();
            current.Insert(0, optionRows[index]);
            string fromNames = JoinControlNames(current);
            string toNames = JoinControlNames(conflicts);
            string message = string.IsNullOrEmpty(previousPath)
                ? $"[{keyLabel}] {toNames}\n{fromNames}에 지정하고 기존 조작의 키를 해제할까요?"
                : $"[{previousLabel}] {fromNames}\n[{keyLabel}] {toNames}\n두 키를 서로 교환할까요?";
            FinishRebind(before, "교환할 키를 확인해 주세요.");
            ShowDialog(message, "키 교환", () =>
            {
                InputManager.Instance?.LoadBindings(candidate);
                dirty = true;
                Refresh();
                statusText.text = "키 교환 완료 · 적용 및 저장을 눌러 주세요.";
            }, () => statusText.text = "기존 키를 유지했습니다.");
        }
        catch (Exception error)
        {
            FinishRebind(before, "키 설정을 변경하지 못했습니다.");
            Debug.LogException(error, this);
        }
    }

    private List<Row> GetRowsAtPath(int selectedIndex, string path)
    {
        var result = new List<Row>();
        if (string.IsNullOrEmpty(path)) return result;
        foreach (int index in PageRows[2])
        {
            if (index == selectedIndex) continue;
            var row = optionRows[index];
            var (action, binding) = FindRowBinding(row);
            if (binding >= 0 && BindingPathsOverlap(path, action.bindings[binding].effectivePath))
                result.Add(row);
        }
        return result;
    }

    private static bool BindingPathsOverlap(string first, string second)
    {
        const string shift = "<Keyboard>/shift";
        if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(first, shift, StringComparison.OrdinalIgnoreCase)) return IsSingleShift(second);
        return string.Equals(second, shift, StringComparison.OrdinalIgnoreCase) && IsSingleShift(first);
    }

    private static bool IsSingleShift(string path) =>
        string.Equals(path, "<Keyboard>/leftShift", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(path, "<Keyboard>/rightShift", StringComparison.OrdinalIgnoreCase);

    private static void ChangeRowsPath(List<Row> rows, string path)
    {
        foreach (var row in rows)
        {
            var (action, binding) = FindRowBinding(row);
            ApplyBindingPath(action, binding, path, action.bindings[binding]);
        }
    }

    private static void ApplyBindingPath(InputAction action, int binding, string path, InputBinding prior) =>
        action.ApplyBindingOverride(binding, new InputBinding
        {
            overridePath = path ?? string.Empty,
            overrideProcessors = prior.overrideProcessors,
            overrideInteractions = prior.overrideInteractions
        });

    private static (InputAction action, int binding) FindRowBinding(Row row)
    {
        var action = InputManager.Instance?.Actions.FindAction("Player/" + row.action);
        return (action, FindBinding(action, row.part));
    }

    private static string BindingLabel(InputAction action, int binding) => string.IsNullOrEmpty(action.bindings[binding].effectivePath)
        ? "미지정" : InputBindingDisplay.DisplayKey(action.bindings[binding].effectivePath,
            action.GetBindingDisplayString(binding, InputBinding.DisplayStringOptions.DontIncludeInteractions));

    private static string JoinControlNames(List<Row> rows) => string.Join(" · ", rows.ConvertAll(ControlName));

    public void ClearStoreSkillBinding()
    {
        if (!CanUsePointer() || page != 2 || !showingKeys) return;
        var (action, binding) = FindRowBinding(optionRows[StoreSkillRow]);
        if (binding < 0 || string.IsNullOrEmpty(action.bindings[binding].effectivePath)) return;
        SelectPointerRow(StoreSkillRow);
        action.RemoveBindingOverride(binding);
        InputManager.Instance.ClearButtonStates();
        InputManager.NotifyBindingsChanged();
        dirty = true;
        Refresh();
        statusText.text = "스킬 저장 기본 조합을 복원했습니다. 적용 및 저장을 눌러 주세요.";
    }

    private static string ControlName(Row row) => row.action switch
    {
        "Move" => row.part switch
        {
            "up" => "위 방향 · 조준",
            "down" => "아래 방향 · 발판 통과",
            "left" => "왼쪽 이동",
            _ => "오른쪽 이동"
        },
        "Jump" => "점프 · 더블 점프",
        "Dash" => "대시",
        "Attack" => "기본 공격 · 기어 발동",
        "Gear" => "기어 준비",
        "StoreSkill" => "스킬 저장 · 사용",
        "SecondaryWeapon" => "보조 무기",
        "Interact" => "상호작용",
        "SwapWeapon" => "무장 전환",
        _ => row.action
    };

    private void FinishRebind(string restore, string message)
    {
        var operation = rebind;
        rebind = null;
        operation?.Dispose();
        editSnapshot = null;
        navigationStage = NavigationStage.Items;
        if (InputManager.Instance != null)
        {
            if (restore != null) InputManager.Instance.LoadBindings(restore);
            InputManager.Instance.SuspendForRebind(false);
            if (restore == null) InputManager.NotifyBindingsChanged();
        }
        optionCanvas.interactable = true;
        inputReadyAt = Time.unscaledTime + .25f;
        if (draft != null) { Refresh(); statusText.text = message; }
    }

    public void Apply()
    {
        if (!CanUsePointer()) return;
        FinishEditing(false);
        SaveDraftBindings();
        if (DisplayOptions.Changed(draft, saved))
        {
            OptionSettingsService.ApplyDisplay(draft);
            waitingDisplay = true;
            displayDeadline = Time.unscaledTime + 15f;
            ShowDialog("화면 설정을 유지할까요?", "유지 및 저장", Commit, RevertDisplay);
        }
        else Commit();
    }

    private void Commit()
    {
        waitingDisplay = false;
        OptionSettingsService.ApplyDisplay(draft);
        if (!DataManager.Instance.SaveOptions(draft))
        {
            OptionSettingsService.ApplyDisplay(saved);
            statusText.text = "저장하지 못했습니다. 저장 폴더를 확인하고 다시 시도해 주세요.";
            return;
        }
        saved = draft.Copy();
        dirty = false;
        OptionSettingsService.Instance.ApplyPreview(saved);
        Refresh();
        statusText.text = "설정을 저장했습니다.";
    }

    private void RevertDisplay()
    {
        waitingDisplay = false;
        DisplayOptions.Restore(draft, saved);
        BuildResolutions();
        OptionSettingsService.ApplyDisplay(saved);
        Refresh();
        statusText.text = "이전 화면 설정으로 돌아왔습니다. 나머지 변경은 미저장 상태입니다.";
    }

    public void RequestReset()
    {
        if (!CanUsePointer()) return;
        FinishEditing(false);
        ShowDialog(page == 2 ? "키보드 조작을 기본키로 되돌릴까요?" : "모든 옵션과 키 설정을 기본값으로 되돌릴까요?", "기본값 복원", () =>
        {
            if (page != 2) draft = new OptionSettings();
            draft.ActiveBindingOverrides = "";
            InputManager.Instance?.LoadControlProfile(draft);
            Changed();
        }, () => { });
    }

    public void CancelOrClose()
    {
        if (!IsActive || closing || IsRebinding) return;
        if (dialog.activeSelf) { RejectDialog(); return; }
        FinishEditing(false);
        if (dirty) ShowDialog("저장하지 않은 변경을 취소하고 닫을까요?", "변경 취소 및 닫기", () => CloseMenu(false), () => { });
        else CloseMenu(false);
    }

    private void ShowDialog(string message, string accept, Action onAccept, Action onReject)
    {
        dialogText.text = message; acceptText.text = accept;
        acceptDialog = onAccept; rejectDialog = onReject;
        dialog.SetActive(true); dialogIndex = 1; RefreshDialogFocus();
        inputReadyAt = Time.unscaledTime + .15f;
        EventSystem.current?.SetSelectedGameObject(null);
    }
    private void RefreshDialogFocus() => RefreshFocus();
    public void AcceptDialog() { if (!dialog.activeSelf) return; var action = acceptDialog; dialog.SetActive(false); acceptDialog = rejectDialog = null; WithoutMoveFeedback(() => { action?.Invoke(); if (IsActive) RefreshFocus(); }); inputReadyAt = Time.unscaledTime + .2f; }
    public void RejectDialog() { if (!dialog.activeSelf) return; var action = rejectDialog; dialog.SetActive(false); acceptDialog = rejectDialog = null; WithoutMoveFeedback(() => { action?.Invoke(); if (IsActive) RefreshFocus(); }); inputReadyAt = Time.unscaledTime + .2f; }

    private void CloseMenu(bool keep, bool immediate = false)
    {
        if (!IsActive) return;
        if (audioCredits != null) audioCredits.Close();
        if (IsRebinding) { rebind.Cancel(); if (IsRebinding) FinishRebind(null, ""); }
        if (!keep)
        {
            InputManager.Instance?.LoadControlProfile(saved);
            OptionSettingsService.Instance.ApplyPreview(saved);
            OptionSettingsService.ApplyDisplay(saved);
        }
        waitingDisplay = false;
        dialog.SetActive(false);
        base.Close();
        if (inputEventSystem != null) inputEventSystem.sendNavigationEvents = previousNavigationEvents;
        inputEventSystem = null;
        InputManager.Instance?.ClearButtonStates();
        optionCanvas.DOKill();
        optionCanvas.interactable = optionCanvas.blocksRaycasts = false;
        if (immediate) { rootObject.SetActive(false); return; }
        closing = true;
        optionCanvas.DOFade(0, .12f).SetUpdate(true).OnComplete(() => { rootObject.SetActive(false); closing = false; });
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void OnDestroy()
    {
        rebind?.Dispose();
        optionCanvas?.DOKill();
    }
}
