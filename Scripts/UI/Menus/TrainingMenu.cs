using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class TrainingMenu : SelectableMenuUI
{
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField, Min(0), Tooltip("목록을 열 때 처음 선택할 항목의 순서입니다.")]
    private int defaultIndex;

    public event Action<int> OnSelectionChanged;
    public bool InputSuppressed { get; set; }

    protected override void Awake()
    {
        base.Awake();
        if (scrollRect == null) return;
        // 각 항목의 EventTrigger가 부모 ScrollRect로 가는 휠 이벤트를 가로채므로 직접 전달한다.
        foreach (var button in buttons)
        {
            var trigger = button.GetComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
            entry.callback.AddListener(ForwardScroll);
            trigger.triggers.Add(entry);
        }
    }

    private void ForwardScroll(BaseEventData data)
    {
        if (!IsActive || InputSuppressed || scrollRect == null ||
            (UIManager.Instance != null && UIManager.Instance.IsUIInputBlocked)) return;
        if (data is PointerEventData pointer) scrollRect.OnScroll(pointer);
    }

    public override void Open()
    {
        base.Open();
        if (defaultIndex > 0 && defaultIndex < buttons.Length)
        {
            currentIndex = defaultIndex;
            UpdateHighlight(true);
        }
        NotifySelection();
    }

    protected override void ProcessMove(Vector2 direction)
    {
        if (InputSuppressed) return;
        int previous = currentIndex;
        base.ProcessMove(direction);
        if (previous != currentIndex) NotifySelection(true);
    }

    protected override void OnPointerEnter(int index)
    {
        if (InputSuppressed) return;
        int previous = currentIndex;
        base.OnPointerEnter(index);
        if (previous != currentIndex) NotifySelection(true);
    }

    protected override void OnPointerClick(int index)
    {
        if (InputSuppressed) return;
        if (!IsActive || (UIManager.Instance != null && UIManager.Instance.IsUIInputBlocked)) return;
        int previous = currentIndex;
        ChangeIndex(index);
        if (previous != currentIndex) NotifySelection(true);
        base.OnPointerClick(index);
    }

    public override void OnConfirm()
    {
        if (!InputSuppressed) base.OnConfirm();
    }

    public override void OnCancel()
    {
        if (!InputSuppressed) base.OnCancel();
    }

    private void NotifySelection(bool animate = false)
    {
        if (animate)
        {
            // 같은 Transform에 확대와 Punch가 겹쳐 최종 배율이 덮이지 않도록 한 Tween으로 정착한다.
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].DOKill();
                buttons[i].DOScale(i == currentIndex ? 1.15f : 1f, .16f).SetEase(Ease.OutCubic).SetUpdate(true);
            }
        }
        OnSelectionChanged?.Invoke(currentIndex);
        if (scrollRect == null || scrollRect.content == null || scrollRect.viewport == null ||
            currentIndex < 0 || currentIndex >= buttons.Length) return;

        Canvas.ForceUpdateCanvases();
        // 자식 Content까지 포함한 Bounds 대신 실제 마스크 사각형을 사용한다.
        Rect viewport = scrollRect.viewport.rect;
        Bounds item = RectTransformUtility.CalculateRelativeRectTransformBounds(scrollRect.viewport, buttons[currentIndex]);
        float scale = Mathf.Abs(buttons[currentIndex].localScale.y);
        item.extents *= 1.15f / Mathf.Max(.001f, scale);
        float offset = item.max.y > viewport.yMax ? viewport.yMax - item.max.y :
            item.min.y < viewport.yMin ? viewport.yMin - item.min.y : 0f;
        if (Mathf.Approximately(offset, 0f)) return;
        Vector3 worldOffset = scrollRect.viewport.TransformVector(new Vector3(0f, offset, 0f));
        scrollRect.content.position += worldOffset;
        scrollRect.StopMovement();
    }
}
