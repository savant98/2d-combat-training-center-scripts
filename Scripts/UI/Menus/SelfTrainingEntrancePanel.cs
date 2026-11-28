using System;
using System.Collections;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>자율훈련장 안내를 상호작용으로 투사하고 입장 선택만 방 전환에 전달한다.</summary>
public sealed class SelfTrainingEntrancePanel : MonoBehaviour, IUIInputReceiver
{
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private RectTransform presentation;
    [SerializeField] private CanvasGroup beamGroup;
    [SerializeField] private UnityEngine.UI.Button[] buttons;
    [SerializeField] private TrainingHologramGraphic[] buttonFrames;
    [SerializeField] private CinemachineCamera menuCamera;
    [SerializeField] private FeedbackDataSO moveFeedback, confirmFeedback;
    [Header("자율훈련 메뉴 색상")]
    [SerializeField] private Color selectedBackground = new Color32(155, 232, 188, 255);
    [SerializeField] private Color normalBackground = new Color32(4, 6, 9, 255);
    [SerializeField] private Color selectedText = new Color32(9, 14, 20, 255);
    private Action enter;
    private PlayerController player;
    private bool open;
    private int selection;
    private float shownAt;
    private Coroutine cameraReveal;
    private Vector3 restScale = Vector3.one;
    public bool IsActive => open && isActiveAndEnabled;
    private const float OpeningDuration = TitlePanelMotion.OpenDuration;
    private bool IsReady => CanAct && Time.unscaledTime - shownAt >= OpeningDuration;
    private bool CanAct => IsActive && UIManager.Instance != null && !UIManager.Instance.IsUIInputBlocked && UIManager.Instance.IsCurrentInputTarget(this);

    private void Awake()
    {
        InputBindingHint.Bind(transform, "HologramSign/InputHint", "{MenuHorizontal} 선택   ·   {Confirm} 확인   ·   {Cancel} 취소");
        if (presentation != null) restScale = presentation.localScale;
        for (int i = 0; buttons != null && i < buttons.Length; i++)
        {
            int index = i;
            var button = buttons[i];
            button.onClick.AddListener(() => Click(index));
            var trigger = button.GetComponent<EventTrigger>() ?? button.gameObject.AddComponent<EventTrigger>();
            var pointerEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            pointerEnter.callback.AddListener(_ =>
            {
                if (IsReady && button.IsInteractable()) Select(index);
            });
            trigger.triggers.Add(pointerEnter);
        }
        HideVisuals();
    }
    public void Show(Action onEnter, PlayerController interactor)
    {
        if (open || UIManager.Instance == null || UIManager.Instance.BlocksGameplayInput || UIManager.Instance.IsUIInputBlocked) return;
        enter = onEnter; player = interactor; open = true; selection = 1; shownAt = Time.unscaledTime;
        UIManager.Instance.PushInputTarget(this, UIInputLayer.Gameplay);
        UIManager.Instance.HUDVisible(false);
        if (menuCamera != null)
        {
            menuCamera.enabled = true;
            menuCamera.Priority = 999;
            shownAt = float.PositiveInfinity;
            cameraReveal = StartCoroutine(RevealAtEntrance());
        }
        Highlight();
    }
    private IEnumerator RevealAtEntrance()
    {
        yield return TitlePanelMotion.WaitForCamera(menuCamera);
        shownAt = Time.unscaledTime;
        cameraReveal = null;
    }

    public void Hide()
    {
        bool wasOpen = open; open = false; enter = null;
        var previousPlayer = player; player = null;
        HideVisuals();
        if (wasOpen && UIManager.Instance != null) { UIManager.Instance.RemoveInputTarget(this); UIManager.Instance.HUDVisible(true); }
        if (previousPlayer != null && previousPlayer.AbilityManager != null) previousPlayer.AbilityManager.InteractAbility.Interrupt();
    }
    private void HideVisuals()
    {
        if (cameraReveal != null) { StopCoroutine(cameraReveal); cameraReveal = null; }
        if (panelGroup != null) { panelGroup.alpha = 0f; panelGroup.interactable = panelGroup.blocksRaycasts = false; }
        if (beamGroup != null) { beamGroup.alpha = 0f; beamGroup.blocksRaycasts = beamGroup.interactable = false; }
        if (presentation != null) presentation.localScale = restScale;
        if (menuCamera != null) { menuCamera.Priority = 0; menuCamera.enabled = false; }
    }
    private void Update()
    {
        if (!open) return;
        float t = Mathf.Clamp01((Time.unscaledTime - shownAt) / OpeningDuration);
        if (panelGroup != null) { panelGroup.alpha = t; panelGroup.interactable = panelGroup.blocksRaycasts = CanAct && t >= 1f; }
        if (beamGroup != null) beamGroup.alpha = Mathf.Clamp01(t * 2f);
        if (presentation != null) presentation.localScale = Vector3.Scale(restScale, TitlePanelMotion.UnfoldScale(t));
    }
    public void OnMove(Vector2 direction)
    {
        if (!CanAct || buttons == null || buttons.Length == 0) return;
        Vector3 origin = ((RectTransform)buttons[selection].transform).position;
        int closest = selection; float distance = float.PositiveInfinity;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (i == selection || !buttons[i].interactable) continue;
            Vector2 delta = (Vector2)(buttons[i].transform.position-origin);
            if (Vector2.Dot(delta.normalized, direction.normalized) < .707f || delta.sqrMagnitude >= distance) continue;
            closest = i; distance = delta.sqrMagnitude;
        }
        Select(closest);
    }
    private void Select(int index)
    {
        if (index == selection) return;
        selection = index;
        Highlight();
        Play(moveFeedback);
    }

    public void OnConfirm() { if (CanAct) Click(selection); }
    public void OnCancel() { if (CanAct) Hide(); }
    public void Click(int index)
    {
        if (!IsReady || !buttons[index].IsInteractable()) return;
        selection = index; Highlight(); Play(confirmFeedback);
        var action = enter; Hide();
        if (index == 1) action?.Invoke();
    }
    private void Highlight()
    {
        if (buttons == null) return;
        for (int i=0;i<buttons.Length;i++)
        {
            if(buttons[i] == null) continue;
            bool selected = i == selection;
            buttons[i].targetGraphic.color = selected ? selectedBackground : normalBackground;
            buttons[i].transform.localScale = Vector3.one * (selected ? TrainingBoardStyle.FocusScale : 1f);
            if (buttonFrames != null && i < buttonFrames.Length && buttonFrames[i] != null)
            {
                var shape = selected ? TrainingHologramGraphic.Shape.FocusFrame : TrainingHologramGraphic.Shape.Frame;
                buttonFrames[i].Configure(shape, selected ? TrainingBoardStyle.LineBright : TrainingBoardStyle.LineDim,
                    selected ? 1.7f : 1f, 7f, selected ? .07f : 0f);
                buttonFrames[i].color = Color.white;
            }
            foreach (var label in buttons[i].GetComponentsInChildren<TextMeshProUGUI>())
                label.color = selected ? selectedText : new Color(.85f, .95f, 1f);
        }
    }
    private void Play(FeedbackDataSO data) { if(data != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(data,transform); }
    private void OnDisable() { Hide(); }
}
