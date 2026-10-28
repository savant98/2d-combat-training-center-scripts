using System;
using UnityEngine;
using UnityEngine.UI;

public class TrainingMenu : SelectableMenuUI
{
    [SerializeField] private ScrollRect scrollRect;

    public event Action<int> OnSelectionChanged;
    public bool InputSuppressed { get; set; }

    public override void Open()
    {
        base.Open();
        NotifySelection();
    }

    protected override void ProcessMove(Vector2 direction)
    {
        if (InputSuppressed) return;
        int previous = currentIndex;
        base.ProcessMove(direction);
        if (previous != currentIndex) NotifySelection();
    }

    protected override void OnPointerEnter(int index)
    {
        if (InputSuppressed) return;
        int previous = currentIndex;
        base.OnPointerEnter(index);
        if (previous != currentIndex) NotifySelection();
    }

    protected override void OnPointerClick(int index)
    {
        if (InputSuppressed) return;
        if (!IsActive || (UIManager.Instance != null && UIManager.Instance.IsUIInputBlocked)) return;
        int previous = currentIndex;
        ChangeIndex(index);
        if (previous != currentIndex) NotifySelection();
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

    private void NotifySelection()
    {
        OnSelectionChanged?.Invoke(currentIndex);
        if (scrollRect == null || scrollRect.content == null || scrollRect.viewport == null ||
            currentIndex < 0 || currentIndex >= buttons.Length) return;

        Canvas.ForceUpdateCanvases();
        Bounds viewport = RectTransformUtility.CalculateRelativeRectTransformBounds(scrollRect.viewport, scrollRect.viewport);
        Bounds item = RectTransformUtility.CalculateRelativeRectTransformBounds(scrollRect.viewport, buttons[currentIndex]);
        float offset = item.max.y > viewport.max.y ? viewport.max.y - item.max.y :
            item.min.y < viewport.min.y ? viewport.min.y - item.min.y : 0f;
        if (Mathf.Approximately(offset, 0f)) return;
        Vector3 worldOffset = scrollRect.viewport.TransformVector(new Vector3(0f, offset, 0f));
        scrollRect.content.position += worldOffset;
        scrollRect.StopMovement();
    }
}
