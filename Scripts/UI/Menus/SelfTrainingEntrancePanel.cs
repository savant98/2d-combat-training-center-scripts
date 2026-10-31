using System;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>자율훈련장 안내를 상호작용으로 투사하고 입장 선택만 방 전환에 전달한다.</summary>
public sealed class SelfTrainingEntrancePanel : MonoBehaviour, IUIInputReceiver
{
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private RectTransform presentation;
    [SerializeField] private CanvasGroup beamGroup;
    [SerializeField] private UnityEngine.UI.Button[] buttons;
    [SerializeField] private CinemachineCamera menuCamera;
    [SerializeField] private FeedbackDataSO moveFeedback, confirmFeedback;
    private Action enter;
    private PlayerController player;
    private bool open;
    private int selection;
    private float shownAt;
    private Vector3 restScale = Vector3.one;
    public bool IsActive => open && isActiveAndEnabled;
    private bool CanAct => IsActive && UIManager.Instance != null && !UIManager.Instance.IsUIInputBlocked && UIManager.Instance.IsCurrentInputTarget(this);

    private void Awake()
    {
        if (presentation != null) restScale = presentation.localScale;
        for (int i = 0; buttons != null && i < buttons.Length; i++)
        {
            int index = i;
            buttons[i].onClick.AddListener(() => Click(index));
        }
        HideVisuals();
    }
    public void Show(Action onEnter, PlayerController interactor)
    {
        if (open || UIManager.Instance == null || UIManager.Instance.BlocksGameplayInput || UIManager.Instance.IsUIInputBlocked) return;
        enter = onEnter; player = interactor; open = true; selection = 1; shownAt = Time.unscaledTime;
        UIManager.Instance.PushInputTarget(this, UIInputLayer.Gameplay);
        UIManager.Instance.HUDVisible(false);
        if (menuCamera != null) { menuCamera.enabled = true; menuCamera.Priority = 999; }
        Highlight();
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
        if (panelGroup != null) { panelGroup.alpha = 0f; panelGroup.interactable = panelGroup.blocksRaycasts = false; }
        if (beamGroup != null) { beamGroup.alpha = 0f; beamGroup.blocksRaycasts = beamGroup.interactable = false; }
        if (presentation != null) presentation.localScale = restScale;
        if (menuCamera != null) { menuCamera.Priority = 0; menuCamera.enabled = false; }
    }
    private void Update()
    {
        if (!open) return;
        float t = Mathf.Clamp01((Time.unscaledTime - shownAt) / .3f);
        if (panelGroup != null) { panelGroup.alpha = t; panelGroup.interactable = panelGroup.blocksRaycasts = CanAct && t >= 1f; }
        if (beamGroup != null) beamGroup.alpha = Mathf.Clamp01(t * 2f);
        if (presentation != null) presentation.localScale = new Vector3(restScale.x, restScale.y * Mathf.Lerp(.03f, 1f, 1f-Mathf.Pow(1f-t,3f)), restScale.z);
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
        if (closest == selection) return;
        selection = closest; Highlight(); Play(moveFeedback);
    }
    public void OnConfirm() { if (CanAct) Click(selection); }
    public void OnCancel() { if (CanAct) Hide(); }
    public void Click(int index)
    {
        if (!CanAct || Time.unscaledTime-shownAt < .3f) return;
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
            buttons[i].targetGraphic.color = i == selection ? new Color(.18f,.65f,.9f,1f) : new Color(.035f,.12f,.18f,1f);
            foreach(var label in buttons[i].GetComponentsInChildren<TextMeshProUGUI>()) label.color = i == selection ? new Color(.015f,.06f,.09f) : new Color(.85f,.95f,1f);
        }
    }
    private void Play(FeedbackDataSO data) { if(data != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(data,transform); }
    private void OnDisable() { Hide(); }
}
