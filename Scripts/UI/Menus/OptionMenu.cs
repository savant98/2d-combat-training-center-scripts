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

    private OptionSettings saved, draft;
    private string originalBindings;
    private readonly List<Vector2Int> resolutions = new();
    private static readonly int[] FrameRates = { 60, 120, 144, 165, 240, -1 };
    private static readonly string[] PageNames = { "화면 · 시각 효과", "사운드", "키보드 조작" };
    public enum NavigationStage { Categories, Items, Editing, Footer }
    private NavigationStage navigationStage;
    private static readonly int[][] PageRows =
    {
        new[] { 20, 0, 1, 2, 3, 4, 5 },
        new[] { 6, 7, 8 },
        new[] { 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 }
    };
    private int RowCount => PageRows[page].Length;
    private int SelectedRow => PageRows[page][Mathf.Clamp(selection, 0, RowCount - 1)];
    private OptionSettings editSnapshot;
    private bool dirtyBeforeEdit;
    [SerializeField] private TextMeshProUGUI navigationHint;
    [SerializeField] private TextMeshProUGUI navigationStageText;
    [SerializeField] private UnityEngine.UI.Image[] tabFocus;
    private InputActionRebindingExtensions.RebindingOperation rebind;
    private int page, selection, dialogIndex;
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
        BuildMenuMapping();
        base.Awake();
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
                navigationStage = NavigationStage.Footer; selection = index; RefreshFocus();
            });
            trigger.triggers.Add(enter);
        }
        foreach (var button in rootObject.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            button.onClick.AddListener(() => PlayFeedback(confirmFeedback));
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
        TitleMenu.OnOptionSelected -= OpenMenu;
        PauseMenuController.OnOptionSelected -= OpenMenu;
        if (IsActive) CloseMenu(false, true);
    }

    public void OpenMenu()
    {
        if (IsActive || closing) return;
        saved = DataManager.Instance.Options.Copy();
        draft = saved.Copy();
        originalBindings = InputManager.Instance != null ? InputManager.Instance.Actions.SaveBindingOverridesAsJson() : "";
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

    public void ShowPage(int index)
    {
        if (!CanUsePointer()) return;
        page = (index + pages.Length) % pages.Length;
        selection = 0;
        navigationStage = NavigationStage.Categories;
        editSnapshot = null;
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].SetActive(i == page);
            tabFills[i].color = i == page ? new Color(.08f, .3f, .4f, 1) : TrainingBoardStyle.SlotFill;
        }
        pageTitle.text = PageNames[page];
        Refresh();
    }

    private void Resize()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var size = ((RectTransform)canvas.rootCanvas.transform).rect.size;
        ((RectTransform)rootObject.transform).sizeDelta = size;
        frame.localScale = Vector3.one * Mathf.Min(1f, (size.x - 40f) / 1520f, (size.y - 40f) / 900f);
    }

    private void Update()
    {
        if (!IsActive) return;
        Resize();
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
        if (CanReceiveMenuInput) Navigate(direction);
    }

    public override void OnConfirm()
    {
        if (!CanReceiveMenuInput) return;
        base.OnConfirm();
        WithoutMoveFeedback(ConfirmNavigation);
    }

    public override void OnCancel()
    {
        if (!CanReceiveMenuInput) return;
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
        foreach (int index in PageRows[page]) candidates.Add(optionRows[index].root);
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
                else Apply();
                break;
        }
        inputReadyAt = Time.unscaledTime + .12f;
    }

    private void BeginEditing()
    {
        if (SelectedRow == 3 && draft.vSync)
        {
            statusText.text = "프레임 제한을 바꾸려면 수직 동기화를 먼저 꺼 주세요.";
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
        int next = Array.IndexOf(PageRows[page], index);
        if (next < 0) return;
        if (navigationStage == NavigationStage.Editing && SelectedRow != index) FinishEditing(false);
        selection = next;
        if (navigationStage != NavigationStage.Editing) navigationStage = NavigationStage.Items;
        RefreshFocus();
    }

    public void ActivateRow(int index)
    {
        if (!CanUsePointer()) return;
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
            case 20: draft.language = draft.language == "en" ? "ko" : "en"; break;
            case 0: draft.windowed = !draft.windowed; break;
            case 1:
                int resolutionIndex = resolutions.FindIndex(r => r.x == EffectiveWidth(draft) && r.y == EffectiveHeight(draft));
                var res = resolutions[(resolutionIndex + direction + resolutions.Count) % resolutions.Count];
                draft.width = res.x; draft.height = res.y; break;
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
        OptionSettingsService.Instance.ApplyPreview(draft);
        Refresh();
    }

    private void Refresh()
    {
        optionRows[0].value.text = draft.windowed ? "창 모드" : "테두리 없는 전체 화면";
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
        for (int i = 9; i < 20; i++)
        {
            var row = optionRows[i];
            var action = InputManager.Instance != null ? InputManager.Instance.Actions.FindAction("Player/" + row.action) : null;
            int binding = FindBinding(action, row.part);
            row.value.text = binding < 0 ? "입력 장치 없음" : action.GetBindingDisplayString(binding);
        }
        statusText.text = dirty ? "변경 사항 미저장 · 소리와 시각 효과는 미리 적용 중" : "저장된 설정 · 변경 후 [적용 및 저장]을 눌러 주세요";
        RefreshFocus();
    }

    private void RefreshFocus()
    {
        int target = dialog.activeSelf ? 27 + dialogIndex : navigationStage switch
        {
            NavigationStage.Categories => page,
            NavigationStage.Items => 3 + SelectedRow,
            NavigationStage.Editing => 3 + SelectedRow,
            _ => 24 + selection
        };
        for (int i = 0; i < images.Length; i++)
            if (images[i] is TrainingHologramGraphic graphic)
                graphic.Configure(i >= 3 && i < 24 ? TrainingHologramGraphic.Shape.FocusFrame : TrainingHologramGraphic.Shape.Frame,
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
        if (navigationHint != null) navigationHint.text = navigationStage switch
        {
            NavigationStage.Categories => "↑ ↓ 분류 선택   Z / → 항목으로   X 닫기",
            NavigationStage.Items => "↑ ↓ ← → 가까운 항목 선택   Z 편집 시작   X 분류로",
            NavigationStage.Editing => IsRebinding ? "새 키 입력   X 취소   10초 뒤 자동 취소" : "← → 값 조절   Z 편집 완료   X 이번 편집 취소",
            _ => "← → 버튼 선택   ↑ 가까운 항목으로   Z 실행   X 분류로"
        };
    }
    [SerializeField] private UnityEngine.UI.Image[] footerFocus;

    private static int EffectiveWidth(OptionSettings s) => s.width > 0 ? s.width : Screen.currentResolution.width > 0 ? Screen.currentResolution.width : 1920;
    private static int EffectiveHeight(OptionSettings s) => s.height > 0 ? s.height : Screen.currentResolution.height > 0 ? Screen.currentResolution.height : 1080;
    private void BuildResolutions()
    {
        resolutions.Clear();
        foreach (var r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (r.width >= 960 && r.height >= 540 && !resolutions.Contains(size)) resolutions.Add(size);
        }
        foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(EffectiveWidth(saved), EffectiveHeight(saved)) })
            if (!resolutions.Contains(size) && (size.x <= Screen.currentResolution.width || resolutions.Count == 0)) resolutions.Add(size);
        var current = new Vector2Int(EffectiveWidth(saved), EffectiveHeight(saved));
        if (!resolutions.Contains(current)) resolutions.Add(current);
        resolutions.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
    }

    public static int FindBinding(InputAction action, string part)
    {
        if (action == null) return -1;
        for (int i = 0; i < action.bindings.Count; i++)
        {
            var b = action.bindings[i];
            if (!b.isComposite && (b.path ?? "").StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(part) || string.Equals(b.name, part, StringComparison.OrdinalIgnoreCase))) return i;
        }
        return -1;
    }

    public void BeginRebind(int index)
    {
        if (!CanUsePointer() || InputManager.Instance == null) return;
        SelectPointerRow(index);
        navigationStage = NavigationStage.Editing;
        var manager = InputManager.Instance;
        var row = optionRows[index];
        var action = manager.Actions.FindAction("Player/" + row.action);
        int binding = FindBinding(action, row.part);
        if (binding < 0) return;
        string before = manager.Actions.SaveBindingOverridesAsJson();
        manager.SuspendForRebind(true);
        optionCanvas.interactable = false;
        statusText.text = "새 키를 누르세요 · X 취소 · 10초 뒤 자동 취소";
        row.value.text = "입력 대기…";
        RefreshFocus();
        try
        {
            rebind = action.PerformInteractiveRebinding(binding).WithControlsHavingToMatchPath("<Keyboard>")
                .WithCancelingThrough("<Keyboard>/x").WithTimeout(10f).WithActionEventNotificationsBeingSuppressed()
                .OnCancel(_ => FinishRebind(before, "키 변경을 취소했습니다."))
                .OnComplete(_ =>
                {
                    string path = action.bindings[binding].effectivePath;
                    bool reserved = string.Equals(path, "<Keyboard>/escape", StringComparison.OrdinalIgnoreCase);
                    if (reserved || HasConflict(index, path)) FinishRebind(before, reserved ? "Esc는 일시정지용입니다. 다른 키를 골라 주세요." : "이미 사용 중인 키입니다. 다른 키를 골라 주세요.");
                    else { FinishRebind(null, "키 변경 완료 · 적용 및 저장을 눌러 주세요."); dirty = true; }
                });
            rebind.Start();
        }
        catch (Exception e) { FinishRebind(before, "키 변경을 시작하지 못했습니다: " + e.Message); }
    }

    private bool HasConflict(int index, string path)
    {
        var selected = optionRows[index];
        foreach (var row in optionRows)
        {
            if (string.IsNullOrEmpty(row.action) || row == selected) continue;
            // 기본 설정이 공유하는 위 이동 + 상호작용만 허용한다.
            if ((selected.action == "Move" && selected.part == "up" && row.action == "Interact") ||
                (selected.action == "Interact" && row.action == "Move" && row.part == "up")) continue;
            var action = InputManager.Instance.Actions.FindAction("Player/" + row.action);
            int binding = FindBinding(action, row.part);
            if (binding >= 0 && string.Equals(path, action.bindings[binding].effectivePath, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

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
        }
        optionCanvas.interactable = true;
        inputReadyAt = Time.unscaledTime + .25f;
        if (draft != null) { Refresh(); statusText.text = message; }
    }

    public void Apply()
    {
        if (!CanUsePointer()) return;
        FinishEditing(false);
        draft.bindingOverrides = InputManager.Instance != null ? InputManager.Instance.Actions.SaveBindingOverridesAsJson() : saved.bindingOverrides;
        if (draft.width != saved.width || draft.height != saved.height || draft.windowed != saved.windowed)
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
        originalBindings = draft.bindingOverrides;
        dirty = false;
        OptionSettingsService.Instance.ApplyPreview(saved);
        Refresh();
        statusText.text = "설정을 저장했습니다.";
    }

    private void RevertDisplay()
    {
        waitingDisplay = false;
        draft.width = saved.width; draft.height = saved.height; draft.windowed = saved.windowed;
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
            InputManager.Instance?.LoadBindings("");
            draft.bindingOverrides = "";
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
        if (IsRebinding) { rebind.Cancel(); if (IsRebinding) FinishRebind(originalBindings, ""); }
        if (!keep)
        {
            InputManager.Instance?.LoadBindings(originalBindings);
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
