using DG.Tweening;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public abstract class SelectableMenuUI : MonoBehaviour, IUIInputReceiver
{
    [Header("Input Layer")]
    [SerializeField] protected UIInputLayer inputLayer = UIInputLayer.Gameplay;

    [Header("Mapping")]
    [SerializeField] protected RectTransform[] buttons;
    [SerializeField] protected Image[] images;
    [SerializeField] protected TextMeshProUGUI[] texts;

    [Header("Feedback")]
    [SerializeField] protected FeedbackDataSO openFeedback;
    [SerializeField] protected FeedbackDataSO moveFeedback;
    [SerializeField] protected FeedbackDataSO confirmFeedback;
    [SerializeField] protected FeedbackDataSO cancelFeedback;

    protected int currentIndex;
    protected int prevIndex;

    public event Action<int> OnSelected;
    public event Action OnCanceled;

    public bool IsActive { get; private set; }

    protected virtual bool CanSelect => IsActive;

    protected virtual void Awake()
    {
        BindMouseEvents();
    }

    public virtual void Open()
    {
        IsActive = true;
        currentIndex = 0;
        prevIndex = -1;

        UIManager.Instance.SetInputTarget(this, inputLayer);

        foreach (var button in buttons)
        {
            button.DOKill();
            button.localScale = Vector3.one;
        }
        foreach (var t in texts)
        {
            t.DOKill();
            t.color = TrainingBoardStyle.Body;
        }

        foreach (var i in images)
        {
            i.DOKill();
            i.color = new Color(1f, 1f, 1f, 0f);
        }

        // 메뉴를 열며 기본 선택 상태를 보여주는 것이므로
        // 여기서는 moveFeedback을 재생하지 않는다.
        UpdateHighlight(true);

        PlayFeedback(openFeedback);
    }

    public virtual void Close()
    {
        IsActive = false;
        UIManager.Instance.ClearInputTarget(this);
    }

    public void OnMove(Vector2 dir)
    {
        if (CanSelect) ProcessMove(dir);
    }
    protected virtual void ProcessMove(Vector2 dir)
    {
        // 기본 동작: 단순 1차원 배열 상정
        int nextIndex = currentIndex;
        if (dir.x > 0.5f) nextIndex++;
        if (dir.x < -0.5f) nextIndex--;

        if (dir.y > 0.5f) nextIndex--;
        if (dir.y < -0.5f) nextIndex++;

        ChangeIndex(nextIndex);
    }

    protected void ChangeIndex(int newIndex)
    {
        // 배열 범위를 벗어나면 무시 (이동 불가)
        if (newIndex < 0 || newIndex >= buttons.Length)
            return;

        if (currentIndex != newIndex)
        {
            currentIndex = newIndex;
            UpdateHighlight(false);
            PlayFeedback(moveFeedback);
        }
    }
    public virtual void OnConfirm()
    {
        if (!CanSelect) return;

        PlayFeedback(confirmFeedback);
        OnSelected?.Invoke(currentIndex);
    }

    public virtual void OnCancel()
    {
        if (!IsActive)
            return;

        PlayFeedback(cancelFeedback);
        OnCanceled?.Invoke();
    }

    #region Higlith Effects
    protected void UpdateHighlight(bool instant)
    {
        // 이전 버튼 복귀
        if (prevIndex >= 0)
            ResetButton(prevIndex, instant);

        // 현재 버튼 강조
        HighlightButton(currentIndex, instant);

        prevIndex = currentIndex;
    }

    protected void HighlightButton(int index, bool instant) => SetFocus(index, true, instant);

    protected void ResetButton(int index, bool instant) => SetFocus(index, false, instant);

    private void SetFocus(int index, bool selected, bool instant)
    {
        var button = buttons[index];
        button.DOKill();
        float scale = selected ? TrainingBoardStyle.FocusScale : 1f;
        if (instant) button.localScale = Vector3.one * scale;
        else button.DOScale(scale, .16f).SetEase(Ease.OutCubic).SetUpdate(true);

        if (index < texts.Length)
            Tint(texts[index], selected ? Color.white : TrainingBoardStyle.Body, instant);
        if (index < images.Length)
            Tint(images[index], selected ? Color.white : new Color(1f, 1f, 1f, 0f), instant);
    }

    private static void Tint(Graphic graphic, Color target, bool instant)
    {
        graphic.DOKill();
        if (instant) graphic.color = target;
        else graphic.DOColor(target, .12f).SetUpdate(true);
    }
    #endregion

    #region Bind Mouse

    private void BindMouseEvents()
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            RectTransform btnRect = buttons[i];

            // EventTrigger 컴포넌트가 없다면 추가
            EventTrigger trigger = btnRect.gameObject.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = btnRect.gameObject.AddComponent<EventTrigger>();
            }

            // 1. 마우스 Hover (Pointer Enter) 이벤트
            EventTrigger.Entry enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener((data) => { OnPointerEnter(index); });
            trigger.triggers.Add(enterEntry);

            // 2. 마우스 Click (Pointer Click) 이벤트
            EventTrigger.Entry clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            clickEntry.callback.AddListener((data) => { OnPointerClick(index); });
            trigger.triggers.Add(clickEntry);
        }
    }

    protected virtual void OnPointerEnter(int index)
    {
        if (!CanSelect)
            return;

        // UIManager에서 UI 입력이 막혀있다면 마우스 호버(Hover) 완벽 무시!
        if (UIManager.Instance != null && (UIManager.Instance.IsUIInputBlocked || !UIManager.Instance.IsCurrentInputTarget(this)))
            return;

        ChangeIndex(index);
    }

    protected virtual void OnPointerClick(int index)
    {
        if (!CanSelect)
            return;

        // UIManager에서 UI 입력이 막혀있다면 마우스 호버(Hover) 완벽 무시!
        if (UIManager.Instance != null && (UIManager.Instance.IsUIInputBlocked || !UIManager.Instance.IsCurrentInputTarget(this)))
            return;

        ChangeIndex(index);
        OnConfirm();
    }

    #endregion

    protected void PlayFeedback(FeedbackDataSO feedback)
    {
        if (feedback == null || FeedbackPlayer.Instance == null)
            return;

        FeedbackPlayer.Instance.PlayExact(
            feedback,
            Vector3.zero,
            Quaternion.identity);
    }
}