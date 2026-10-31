using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>상호작용용 투사 화면과 전투 중 화면 좌상단에 표시하는 분석 결과를 분리한다.</summary>
public sealed class SelfTrainingAnalyzerUI : MonoBehaviour, IUIInputReceiver, IInteractable
{
    private static readonly int[] ShieldValues = { 10, 100, 300, 500 };
    [SerializeField] private SelfTrainingController controller;
    [SerializeField] private CanvasGroup boardGroup;
    [SerializeField] private RectTransform boardPresentation;
    [SerializeField] private TextMeshProUGUI stateText, totalText, currentDpsText, maximumText, summaryText;
    [SerializeField] private TextMeshProUGUI dpsCaption;
    [SerializeField] private TextMeshProUGUI startLabel, hintText, emptyText;
    [SerializeField] private UnityEngine.UI.Image timeFill;
    [SerializeField] private UnityEngine.UI.Selectable[] choices = Array.Empty<UnityEngine.UI.Selectable>();
    [SerializeField] private UnityEngine.UI.Toggle shieldToggle;
    [SerializeField] private RectTransform sourceContent;
    [SerializeField] private SelfTrainingDamageRowUI sourceTemplate;
    [SerializeField] private UnityEngine.UI.ScrollRect sourceScroll;
    [SerializeField] private CanvasGroup countdownGroup, timerGroup;
    [SerializeField] private TextMeshProUGUI countdownText, timerText;
    [SerializeField] private UnityEngine.UI.Image countdownFill, timerFill;
    [SerializeField] private TextMeshProUGUI hudTotalText, hudDpsText, hudMaximumText, hudSummaryText, hudEmptyText;
    [SerializeField] private RectTransform hudSourceContent;
    [SerializeField] private SelfTrainingDamageRowUI hudSourceTemplate;
    [SerializeField] private UnityEngine.UI.ScrollRect hudSourceScroll;
    [SerializeField] private CanvasGroup interactionPrompt;
    [SerializeField] private RectTransform interactionPromptRect;
    [SerializeField] private Transform interactionAnchor;
    [SerializeField] private TextMeshProUGUI hudScrollHint;
    [SerializeField] private CanvasGroup projectionGroup;
    [SerializeField] private FeedbackDataSO moveFeedback, confirmFeedback;
    private readonly List<SelfTrainingDamageRowUI> rows = new List<SelfTrainingDamageRowUI>();
    private readonly List<SelfTrainingDamageRowUI> hudRows = new List<SelfTrainingDamageRowUI>();
    private readonly List<UnityEngine.Events.UnityAction> handlers = new List<UnityEngine.Events.UnityAction>();
    private readonly Color normal = new Color(.045f, .13f, .21f, 1f);
    private readonly Color selected = new Color(.14f, .56f, .82f, 1f);
    private readonly Color enabledChoice = new Color(.07f, .31f, .43f, 1f);
    private bool controlsOpen, listenersReady;
    private int selection, previousCount = -1, previousHudCount = -1;
    private SelfTrainingPhase previousPhase;
    private float lastRefresh, presentationStarted, presentationFrom, presentationTarget;
    private bool playerNearby;
    private Canvas promptCanvas;
    private int scrollDirection;
    private float nextScrollAt;
    private PlayerController interactingPlayer;
    public bool IsActive => controlsOpen && isActiveAndEnabled && controller != null;
    public bool IsOpen => controlsOpen;
    public int SelectedChoiceIndex => selection;
    public SelfTrainingController Controller => controller;
    private bool CanNavigate => IsActive && UIManager.Instance != null && !UIManager.Instance.IsUIInputBlocked
        && UIManager.Instance.IsCurrentInputTarget(this);
    private bool HasOtherInput => UIManager.Instance != null && (UIManager.Instance.IsUIInputBlocked
        || (UIManager.Instance.BlocksGameplayInput && !UIManager.Instance.IsCurrentInputTarget(this)));

    public void Bind(SelfTrainingController value) { controller = value; Refresh(); }

    private void Awake()
    {
        if (controller == null) controller = GetComponentInParent<SelfTrainingController>();
        for (int i = 0; i < choices.Length; i++)
        {
            int index = i;
            UnityEngine.Events.UnityAction handler = () => ClickChoice(index);
            handlers.Add(handler);
            if (choices[i] is UnityEngine.UI.Button button) button.onClick.AddListener(handler);
        }
        if (shieldToggle != null) shieldToggle.onValueChanged.AddListener(ChangeShield);
        listenersReady = true;
        if (sourceTemplate != null) sourceTemplate.gameObject.SetActive(false);
        if (hudSourceTemplate != null) hudSourceTemplate.gameObject.SetActive(false);
        if (interactionPrompt != null) promptCanvas = interactionPrompt.GetComponent<Canvas>();
    }

    private void OnEnable()
    {
        controlsOpen = false;
        playerNearby = false;
        previousCount = previousHudCount = -1;
        scrollDirection = 0;
        Canvas.willRenderCanvases += UpdateInteractionPrompt;
        SetBoardVisibility(false, true);
        SetOverlay(countdownGroup, false);
        SetOverlay(timerGroup, false);
        Refresh();
    }

    private void Update()
    {
        if (controller == null) return;
        bool available = controlsOpen && !HasOtherInput;
        if (boardGroup != null)
        {
            boardGroup.interactable = available;
            boardGroup.blocksRaycasts = available;
            float progress = Mathf.Clamp01((Time.unscaledTime - presentationStarted) / .3f);
            boardGroup.alpha = Mathf.Lerp(presentationFrom, presentationTarget, 1f - Mathf.Pow(1f - progress, 3f));
        }
        if (boardPresentation != null)
        {
            boardPresentation.localScale = new Vector3(1f, Mathf.Lerp(.03f, 1f, boardGroup != null ? boardGroup.alpha : 0f), 1f);
        }
        if (projectionGroup != null) projectionGroup.alpha = boardGroup != null ? boardGroup.alpha : 0f;
        UpdateOverlays();
        ScrollHudWithKeyboard();
        if (Time.unscaledTime - lastRefresh < .05f && previousPhase == controller.Phase) return;
        Refresh();
    }

    public bool CanInteract(PlayerController player) => isActiveAndEnabled && controller != null && playerNearby && !HasOtherInput;
    public void Interact(PlayerController player) { interactingPlayer = player; OpenControls(); }
    public void CancelInteract(PlayerController player) { CloseControls(); }

    public void OpenControls()
    {
        if (!isActiveAndEnabled || controller == null || controlsOpen || HasOtherInput || UIManager.Instance == null) return;
        controlsOpen = true;
        selection = 0;
        UIManager.Instance.PushInputTarget(this, UIInputLayer.Gameplay);
        SetBoardVisibility(true);
        UpdateOverlays();
        Play(confirmFeedback);
        Refresh();
    }

    public void CloseControls()
    {
        bool wasOpen = controlsOpen;
        controlsOpen = false;
        SetBoardVisibility(false);
        if (controller != null) UpdateOverlays();
        if (UIManager.Instance != null) UIManager.Instance.RemoveInputTarget(this);
        var player = interactingPlayer;
        interactingPlayer = null;
        if (wasOpen && player != null && player.AbilityManager != null && player.AbilityManager.InteractAbility != null)
            player.AbilityManager.InteractAbility.Interrupt();
        Refresh();
    }

    public void OnMove(Vector2 direction)
    {
        if (!CanNavigate) return;
        int next = FindClosestChoice(selection, direction);
        if (next == selection) return;
        selection = next;
        Play(moveFeedback);
        Refresh();
    }

    public int FindClosestChoice(int fromIndex, Vector2 direction)
    {
        if (fromIndex < 0 || fromIndex >= choices.Length || choices[fromIndex] == null || direction.sqrMagnitude < .1f)
            return fromIndex;
        direction = Mathf.Abs(direction.x) > Mathf.Abs(direction.y) ? new Vector2(Mathf.Sign(direction.x), 0f) : new Vector2(0f, Mathf.Sign(direction.y));
        var reference = boardPresentation != null ? boardPresentation : transform;
        Rect originBounds = ChoiceBounds(choices[fromIndex], reference);
        Vector2 origin = originBounds.center;
        int next = fromIndex;
        float closest = float.PositiveInfinity;
        bool foundAligned = false;
        for (int i = 0; i < choices.Length; i++)
        {
            if (i == fromIndex || choices[i] == null || !choices[i].IsActive() || !choices[i].IsInteractable()) continue;
            Rect candidate = ChoiceBounds(choices[i], reference);
            Vector2 difference = candidate.center - origin;
            if (Vector2.Dot(difference, direction) <= .01f) continue;
            // 넓은 버튼의 옆으로 이동할 때 위쪽 대각선 항목에 먼저 끌려가지 않게 같은 행·열을 우선한다.
            bool aligned = Mathf.Abs(direction.x) > .5f
                ? Mathf.Min(originBounds.yMax, candidate.yMax) > Mathf.Max(originBounds.yMin, candidate.yMin)
                : Mathf.Min(originBounds.xMax, candidate.xMax) > Mathf.Max(originBounds.xMin, candidate.xMin);
            if (foundAligned && !aligned) continue;
            if (aligned && !foundAligned) { foundAligned = true; closest = float.PositiveInfinity; }
            if (difference.sqrMagnitude >= closest) continue;
            closest = difference.sqrMagnitude;
            next = i;
        }
        return next;
    }

    private static Rect ChoiceBounds(UnityEngine.UI.Selectable choice, Transform reference)
    {
        var rect = (RectTransform)choice.transform;
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 min = reference.InverseTransformPoint(corners[0]), max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point = reference.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    public void OnConfirm() { if (CanNavigate) ClickChoice(selection); }
    public void OnCancel() { if (CanNavigate) CloseControls(); }

    public void ClickChoice(int index)
    {
        if (!controlsOpen || controller == null || HasOtherInput || index < 0 || index >= choices.Length) return;
        if (choices[index] == null || !choices[index].interactable) return;
        selection = index;
        Play(confirmFeedback);
        if (index == 0)
        {
            if (controller.Phase == SelfTrainingPhase.Countdown || controller.Phase == SelfTrainingPhase.Measuring)
                controller.CancelAnalysis();
            else if (controller.StartAnalysis()) CloseControls();
        }
        else if (index >= 1 && index <= 3) controller.Configure(index, controller.UseShield, controller.ShieldCapacity);
        else if (index == 4) controller.Configure(controller.DummyCount, !controller.UseShield, controller.ShieldCapacity);
        else if (index >= 5 && index <= 8)
            controller.Configure(controller.DummyCount, controller.UseShield, ShieldValues[index - 5]);
        else if (index == 9) controller.ResetPractice();
        else if (index == 10) CloseControls();
        Refresh();
    }

    private void ChangeShield(bool value)
    {
        if (controller == null) return;
        if (!controlsOpen || HasOtherInput) { shieldToggle.SetIsOnWithoutNotify(controller.UseShield); return; }
        selection = 4;
        controller.Configure(controller.DummyCount, value, controller.ShieldCapacity);
        Play(confirmFeedback);
        Refresh();
    }

    private void Refresh()
    {
        if (controller == null) return;
        lastRefresh = Time.unscaledTime;
        bool busy = controller.Phase == SelfTrainingPhase.Countdown || controller.Phase == SelfTrainingPhase.Measuring;
        previousPhase = controller.Phase;
        var stats = controller.Statistics;
        if (stateText != null) stateText.text = !busy && !controller.TargetsReady ? "표적 준비 중" : controller.Phase == SelfTrainingPhase.Countdown ? "측정 준비"
            : controller.Phase == SelfTrainingPhase.Measuring ? "전투 분석 중" : controller.Phase == SelfTrainingPhase.Results ? "분석 완료" : "자유 훈련";
        if (totalText != null) totalText.text = Number(stats.TotalDamage);
        if (currentDpsText != null) currentDpsText.text = Number(controller.Phase == SelfTrainingPhase.Results ? stats.Dps : stats.CurrentDps);
        if (dpsCaption != null) dpsCaption.text = controller.Phase == SelfTrainingPhase.Results ? "분석 평균 DPS" : "최근 1초 DPS";
        if (maximumText != null) maximumText.text = Number(stats.MaximumHit);
        if (summaryText != null) summaryText.text = "평균 DPS  " + Number(stats.Dps) + "     최고 DPS  " + Number(stats.PeakDps) + "     적중  " + stats.HitCount;
        if (startLabel != null) startLabel.text = busy ? "측정 취소" : controller.Phase == SelfTrainingPhase.Results ? "다시 분석 시작" : "전투 분석 시작";
        if (hintText != null) hintText.text = "방향키 선택 · Z 확인 · X 닫기 · 마우스 휠로 피해 내역 탐색";
        if (timeFill != null) timeFill.fillAmount = controller.Phase == SelfTrainingPhase.Measuring ? Mathf.Clamp01(controller.Elapsed / 10f)
            : controller.Phase == SelfTrainingPhase.Results ? 1f : 0f;
        if (shieldToggle != null)
        {
            shieldToggle.SetIsOnWithoutNotify(controller.UseShield);
            shieldToggle.interactable = !busy;
        }
        for (int i = 0; i < choices.Length; i++)
        {
            if (choices[i] == null) continue;
            choices[i].interactable = i == 0 ? busy || controller.TargetsReady : i == 10 || !busy;
            bool configured = i >= 1 && i <= 3 && controller.DummyCount == i
                || i == 4 && controller.UseShield
                || i >= 5 && i <= 8 && controller.ShieldCapacity == ShieldValues[i - 5];
            if (choices[i].targetGraphic != null)
                choices[i].targetGraphic.color = controlsOpen && selection == i ? selected : configured ? enabledChoice : normal;
        }
        UpdateRows();
        UpdateHudRows();
    }

    private void UpdateRows()
    {
        if (sourceTemplate == null || sourceContent == null) return;
        var sources = controller.Statistics.Sources;
        while (rows.Count < sources.Count)
        {
            var row = Instantiate(sourceTemplate, sourceContent);
            row.gameObject.name = "DamageSource" + rows.Count;
            rows.Add(row);
        }
        for (int i = 0; i < rows.Count; i++)
        {
            bool active = i < sources.Count;
            rows[i].gameObject.SetActive(active);
            if (active) rows[i].Show(sources[i]);
        }
        if (emptyText != null) emptyText.gameObject.SetActive(sources.Count == 0);
        if (previousCount != sources.Count)
        {
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(sourceContent);
            if (sources.Count == 0 && sourceScroll != null) sourceScroll.verticalNormalizedPosition = 1f;
            previousCount = sources.Count;
        }
    }

    private void UpdateHudRows()
    {
        if (hudSourceTemplate == null || hudSourceContent == null) return;
        var sources = controller.Statistics.Sources;
        while (hudRows.Count < sources.Count)
        {
            var row = Instantiate(hudSourceTemplate, hudSourceContent);
            row.gameObject.name = "HudDamageSource" + hudRows.Count;
            hudRows.Add(row);
        }
        for (int i = 0; i < hudRows.Count; i++)
        {
            bool active = i < sources.Count;
            hudRows[i].gameObject.SetActive(active);
            if (active) hudRows[i].Show(sources[i]);
        }
        if (hudEmptyText != null) hudEmptyText.gameObject.SetActive(sources.Count == 0);
        if (previousHudCount == sources.Count) return;
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(hudSourceContent);
        if (sources.Count == 0 && hudSourceScroll != null) hudSourceScroll.verticalNormalizedPosition = 1f;
        previousHudCount = sources.Count;
    }

    private void UpdateOverlays()
    {
        bool counting = !controlsOpen && !HasOtherInput && controller.Phase == SelfTrainingPhase.Countdown;
        bool measuring = !controlsOpen && !HasOtherInput && controller.Phase != SelfTrainingPhase.Free;
        SetOverlay(countdownGroup, counting);
        SetOverlay(timerGroup, measuring, true);
        if (countdownText != null && counting) countdownText.text = Mathf.Max(1, Mathf.CeilToInt(controller.CountdownRemaining)).ToString();
        if (countdownFill != null && counting) countdownFill.fillAmount = Mathf.Clamp01(controller.CountdownRemaining / 3f);
        if (timerText != null && measuring) timerText.text = controller.Phase == SelfTrainingPhase.Results ? "전투 분석 완료" : counting ? "전투 분석 준비  " + Mathf.CeilToInt(controller.CountdownRemaining) : "전투 분석  " + controller.Remaining.ToString("0.0") + " s";
        if (timerFill != null && measuring) timerFill.fillAmount = controller.Phase == SelfTrainingPhase.Results ? 1f : controller.Phase == SelfTrainingPhase.Countdown ? 0f : Mathf.Clamp01(controller.Elapsed / 10f);
        var stats = controller.Statistics;
        if (hudTotalText != null) hudTotalText.text = Number(stats.TotalDamage);
        if (hudDpsText != null) hudDpsText.text = Number(controller.Phase == SelfTrainingPhase.Results ? stats.Dps : stats.CurrentDps);
        if (hudMaximumText != null) hudMaximumText.text = Number(stats.MaximumHit);
        if (hudSummaryText != null) hudSummaryText.text = "평균 DPS " + Number(stats.Dps) + "  ·  최고 DPS " + Number(stats.PeakDps) + "  ·  적중 " + stats.HitCount;
        if (hudScrollHint != null) hudScrollHint.text = playerNearby
            ? "단말기 근처 ↑ 조작 / 멀리서 ↑↓ 내역 스크롤"
            : "↑↓ · 마우스 휠 · 우측 스크롤바로 내역 보기";
    }

    private void ScrollHudWithKeyboard()
    {
        // 단말기 범위에서는 기존 ↑ 상호작용을 우선한다. 멀리서는 전투 입력을 잠그지 않고 목록만 움직인다.
        if (controlsOpen || playerNearby || HasOtherInput || Time.timeScale <= 0f || controller.Phase == SelfTrainingPhase.Free
            || hudSourceScroll == null || hudSourceScroll.content == null || hudSourceScroll.viewport == null || Keyboard.current == null)
        {
            scrollDirection = 0;
            return;
        }
        int direction = Keyboard.current.upArrowKey.isPressed ? 1 : Keyboard.current.downArrowKey.isPressed ? -1 : 0;
        if (direction == 0) { scrollDirection = 0; return; }
        if (direction == scrollDirection && Time.unscaledTime < nextScrollAt) return;
        float distance = hudSourceScroll.content.rect.height - hudSourceScroll.viewport.rect.height;
        if (distance <= 0f) return;
        bool firstStep = direction != scrollDirection;
        scrollDirection = direction;
        nextScrollAt = Time.unscaledTime + (firstStep ? .28f : .07f);
        hudSourceScroll.velocity = Vector2.zero;
        hudSourceScroll.verticalNormalizedPosition = Mathf.Clamp01(hudSourceScroll.verticalNormalizedPosition + direction * 64f / distance);
    }

    private void UpdateInteractionPrompt()
    {
        if (interactionPrompt == null) return;
        interactionPrompt.interactable = interactionPrompt.blocksRaycasts = false;
        bool visible = isActiveAndEnabled && playerNearby && !controlsOpen && !HasOtherInput;
        var camera = visible ? Camera.main : null;
        if (camera == null || promptCanvas == null || interactionPromptRect == null || interactionAnchor == null)
        {
            interactionPrompt.alpha = 0f;
            return;
        }
        Vector3 screen = camera.WorldToScreenPoint(interactionAnchor.position);
        Vector2 margin = interactionPromptRect.rect.size * promptCanvas.scaleFactor * .5f;
        visible = screen.z > 0f && screen.x >= margin.x && screen.x <= Screen.width-margin.x
            && screen.y >= margin.y && screen.y <= Screen.height-margin.y;
        if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)promptCanvas.transform, screen, null, out var point))
            interactionPromptRect.anchoredPosition = point;
        interactionPrompt.alpha = visible ? 1f : 0f;
    }

    private static void SetOverlay(CanvasGroup group, bool visible, bool scrollable = false)
    {
        if (group == null) return;
        group.alpha = visible ? 1f : 0f;
        group.interactable = group.blocksRaycasts = visible && scrollable;
    }
    private void SetBoardVisibility(bool visible, bool immediate = false)
    {
        presentationStarted = Time.unscaledTime;
        presentationFrom = boardGroup != null ? boardGroup.alpha : 0f;
        presentationTarget = visible ? 1f : 0f;
        if (boardGroup == null) return;
        boardGroup.interactable = boardGroup.blocksRaycasts = visible && !HasOtherInput;
        if (immediate) boardGroup.alpha = presentationFrom = presentationTarget;
        if (immediate && projectionGroup != null) projectionGroup.alpha = presentationTarget;
    }
    private void OnTriggerEnter2D(Collider2D other) { if (other.GetComponent<PlayerController>() != null) playerNearby = true; }
    private void OnTriggerExit2D(Collider2D other) { if (other.GetComponent<PlayerController>() != null) playerNearby = false; }
    private void Play(FeedbackDataSO data) { if (data != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(data, transform); }
    private static string Number(float value) => value.ToString(value >= 10000f ? "N0" : "N1");
    private void OnDisable() { Canvas.willRenderCanvases -= UpdateInteractionPrompt; scrollDirection = 0; CloseControls(); SetBoardVisibility(false, true); SetOverlay(countdownGroup, false); SetOverlay(timerGroup, false); if (interactionPrompt != null) interactionPrompt.alpha = 0f; }
    private void OnDestroy()
    {
        if (!listenersReady) return;
        for (int i = 0; i < choices.Length && i < handlers.Count; i++) if (choices[i] is UnityEngine.UI.Button button) button.onClick.RemoveListener(handlers[i]);
        if (shieldToggle != null) shieldToggle.onValueChanged.RemoveListener(ChangeShield);
    }
}
