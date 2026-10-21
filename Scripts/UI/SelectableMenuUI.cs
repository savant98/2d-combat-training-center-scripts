using DG.Tweening;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public abstract class SelectableMenuUI : MonoBehaviour, IUIInputReceiver
{
    [Header("Input Layer")]
    [SerializeField] private UIInputLayer inputLayer = UIInputLayer.Gameplay;

    [Header("Mapping")]
    [SerializeField] protected RectTransform[] buttons;
    [SerializeField] protected Image[] images;
    [SerializeField] protected TextMeshProUGUI[] texts;

    [Header("Feedback")]
    [SerializeField] protected FeedbackData openFeedback;
    [SerializeField] protected FeedbackData moveFeedback;
    [SerializeField] protected FeedbackData confirmFeedback;
    [SerializeField] protected FeedbackData cancelFeedback;

    protected int currentIndex;
    protected int prevIndex;

    public event Action<int> OnSelected;
    public event Action OnCanceled;

    public bool IsActive { get; private set; }

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

        foreach (var t in texts)
        {
            t.color = Color.white;
        }

        foreach (var i in images)
        {
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
        if (!IsActive)
            return;

        ProcessMove(dir);
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
    public void OnConfirm()
    {
        if (!IsActive)
            return;

        PlayFeedback(confirmFeedback);
        OnSelected?.Invoke(currentIndex);
    }

    public void OnCancel()
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

    protected void HighlightButton(int index, bool instant)
    {
        HighlightText(index, instant);
        HighlightBackgroundImage(index, instant);

        var button = buttons[index];
        button.DOKill();

        if (instant)
        {
            button.localScale = Vector3.one * 1.15f;

        }
        else
        {
            button.DOScale(1.15f, 0.15f)
                .SetEase(Ease.OutBack)
                .SetUpdate(true);

            button.DOPunchScale(Vector3.one * 0.1f, 0.15f)
                .SetUpdate(true);
        }
    }
    private void HighlightText(int index, bool instant)
    {
        if (texts.Length - 1 < index)
            return;

        var text = texts[index];

        text.DOKill();

        if (instant)
        {
            text.color = Color.cyan;
        }
        else
        {
            text.DOColor(Color.cyan, 0.1f)
                .SetUpdate(true);
        }
    }
    private void HighlightBackgroundImage(int index, bool instant)
    {
        if (images.Length - 1 < index)
            return;

        var image = images[index];

        image.DOKill();

        if (instant)
        {
            image.color = Color.white;
        }
        else
        {
            image.DOColor(Color.white, 0.1f).SetUpdate(true);
        }
    }

    protected void ResetButton(int index, bool instant)
    {
        ResetText(index, instant);
        ResetBackgroundImage(index, instant);

        var button = buttons[index];

        if (instant)
        {
            button.localScale = Vector3.one;
        }
        else
        {
            button.DOScale(1f, 0.12f)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }
    }
    private void ResetText(int index, bool instant)
    {
        if (texts.Length - 1 < index)
            return;

        var text = texts[index];

        if (instant)
        {
            text.color = Color.white;
        }
        else
        {
            text.DOColor(Color.white, 0.12f)
                .SetUpdate(true);
        }
    }
    private void ResetBackgroundImage(int index, bool instant)
    {
        if (images.Length - 1 < index)
            return;

        var image = images[index];

        if (instant)
        {
            image.color = new Color(1f, 1f, 1f, 0f);
        }
        else
        {
            image.DOColor(new Color(1f, 1f, 1f, 0f), 0.12f).SetUpdate(true);
        }
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
        if (!IsActive)
            return;

        // UIManager에서 UI 입력이 막혀있다면 마우스 호버(Hover) 완벽 무시!
        if (UIManager.Instance != null && UIManager.Instance.IsUIInputBlocked)
            return;

        ChangeIndex(index);
    }

    protected virtual void OnPointerClick(int index)
    {
        if (!IsActive)
            return;

        // UIManager에서 UI 입력이 막혀있다면 마우스 호버(Hover) 완벽 무시!
        if (UIManager.Instance != null && UIManager.Instance.IsUIInputBlocked)
            return;

        OnConfirm();
    }

    #endregion

    protected void PlayFeedback(FeedbackData feedback)
    {
        if (feedback == null || FeedbackPlayer.Instance == null)
            return;

        FeedbackPlayer.Instance.PlayExact(
            feedback,
            Vector3.zero,
            Quaternion.identity);
    }
}