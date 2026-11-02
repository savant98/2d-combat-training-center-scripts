using System;
using System.Collections;
using UnityEngine;

// 패배 이후의 안내 표시와 입력만 담당한다. 부활·방 이동·시간 소유권은 연출 흐름에 맡긴다.
[DisallowMultipleComponent]
public sealed class NightmareDefeatTerminal : MonoBehaviour, IUIInputReceiver
{
    [SerializeField] private CanvasGroup blackoutGroup;
    [SerializeField] private CanvasGroup terminalGroup;
    [SerializeField] private RectTransform terminalRoot;
    [SerializeField] private UnityEngine.UI.Button continueButton;
    [SerializeField, Min(0f)] private float blackoutDuration = .65f;
    [SerializeField, Min(0f)] private float blackoutHold = .2f;
    [SerializeField, Min(0f)] private float openDuration = .3f;
    private uint version;
    private float readyAt;
    private bool active;

    public bool IsActive => active && isActiveAndEnabled;
    public bool IsAwaitingContinue { get; private set; }
    public float BlackoutAlpha => blackoutGroup != null ? blackoutGroup.alpha : 0f;
    public event Action ContinueRequested;

    private void Awake()
    {
        continueButton.onClick.AddListener(OnConfirm);
        HidePresentation();
    }

    public IEnumerator ShowRoutine()
    {
        if (IsActive) yield break;
        uint current = ++version;
        active = true;
        IsAwaitingContinue = false;
        continueButton.interactable = false;
        UIManager.Instance.PushInputTarget(this, UIInputLayer.System);
        blackoutGroup.blocksRaycasts = true;
        terminalGroup.blocksRaycasts = false;
        terminalGroup.alpha = 0f;
        yield return Fade(blackoutDuration, t => blackoutGroup.alpha = t, current);
        if (!Current(current)) yield break;
        float elapsed = 0f;
        while (elapsed < blackoutHold && Current(current)) { elapsed += Time.unscaledDeltaTime; yield return null; }
        if (!Current(current)) yield break;
        yield return Fade(openDuration, t => {
            terminalGroup.alpha = t;
            terminalRoot.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, t);
        }, current);
        if (!Current(current)) yield break;
        terminalGroup.blocksRaycasts = terminalGroup.interactable = true;
        readyAt = Time.unscaledTime + .15f;
        while (Time.unscaledTime < readyAt && Current(current)) yield return null;
        if (!Current(current)) yield break;
        IsAwaitingContinue = true;
        continueButton.interactable = true;
    }

    private bool Current(uint current) => version == current && IsActive;

    private IEnumerator Fade(float duration, Action<float> step, uint current)
    {
        float elapsed = 0f;
        step(0f);
        while (elapsed < duration && Current(current))
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            step(progress * progress * (3f - 2f * progress));
            yield return null;
        }
        if (Current(current)) step(1f);
    }

    public void OnConfirm()
    {
        if (!IsActive || !IsAwaitingContinue || Time.unscaledTime < readyAt || UIManager.Instance == null
            || UIManager.Instance.IsUIInputBlocked || !UIManager.Instance.IsCurrentInputTarget(this)) return;
        IsAwaitingContinue = false;
        continueButton.interactable = false;
        ContinueRequested?.Invoke();
    }

    public void OnCancel() { }
    public void OnMove(Vector2 direction) { }

    public void Dismiss()
    {
        ++version;
        active = false;
        IsAwaitingContinue = false;
        if (UIManager.Instance != null) UIManager.Instance.RemoveInputTarget(this);
        HidePresentation();
    }

    private void HidePresentation()
    {
        if (blackoutGroup != null) { blackoutGroup.alpha = 0; blackoutGroup.blocksRaycasts = blackoutGroup.interactable = false; }
        if (terminalGroup != null) { terminalGroup.alpha = 0; terminalGroup.blocksRaycasts = terminalGroup.interactable = false; }
        if (terminalRoot != null) terminalRoot.localScale = Vector3.one;
        if (continueButton != null) continueButton.interactable = false;
    }

    private void OnDisable() => Dismiss();
    private void OnDestroy()
    {
        if (continueButton != null) continueButton.onClick.RemoveListener(OnConfirm);
    }
}
