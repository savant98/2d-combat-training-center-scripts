using System;
using System.Collections.Generic;
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

    /// <summary>줄(선택 대상)의 수. Prefab에 놓인 고정 줄의 수다.</summary>
    public int RowCount => buttons != null ? buttons.Length : 0;

    /// <summary>
    /// 앞에서부터 labels 수만큼 줄을 켜고 글자를 쓴다. 남는 줄은 끈다(꺼진 줄은 이동과 선택에서 건너뛴다).
    /// 줄보다 많은 항목은 담지 못한다. 담은 수를 돌려준다. 메뉴를 열기 전에 부른다.
    /// </summary>
    public int SetRows(IReadOnlyList<string> labels)
    {
        int used = Mathf.Min(labels != null ? labels.Count : 0, RowCount);
        for (int i = 0; i < RowCount; i++)
        {
            bool on = i < used;
            if (buttons[i].gameObject.activeSelf != on) buttons[i].gameObject.SetActive(on);
            if (on && i < texts.Length && texts[i] != null) texts[i].text = labels[i];
        }
        return used;
    }

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
        // 처음 선택은 지정한 줄이고, 그 줄이 꺼져 있으면 켜진 첫 줄이다.
        int start = IsSelectable(defaultIndex) ? defaultIndex : FirstSelectable();
        if (start >= 0 && start != currentIndex)
        {
            currentIndex = start;
            UpdateHighlight(true);
        }
        NotifySelection();
    }

    protected override void ProcessMove(Vector2 direction)
    {
        if (InputSuppressed) return;
        int previous = currentIndex;
        // 기본 이동과 같은 계산으로 가되, 닿은 줄이 꺼져 있으면 같은 방향의 다음 켜진 줄로 넘어간다.
        int step = 0;
        if (direction.x > 0.5f) step++;
        if (direction.x < -0.5f) step--;
        if (direction.y > 0.5f) step--;
        if (direction.y < -0.5f) step++;
        if (step == 0) return;
        int next = currentIndex + step;
        int forward = step > 0 ? 1 : -1;
        while (next >= 0 && next < buttons.Length && !IsSelectable(next)) next += forward;
        ChangeIndex(next);
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
                if (!buttons[i].gameObject.activeSelf) continue;   // 꺼진 줄은 그대로 둔다
                buttons[i].DOKill();
                buttons[i].DOScale(i == currentIndex ? 1.15f : 1f, .16f).SetEase(Ease.OutCubic).SetUpdate(true);
            }
        }
        OnSelectionChanged?.Invoke(currentIndex);
        if (scrollRect == null || scrollRect.content == null || scrollRect.viewport == null ||
            !IsSelectable(currentIndex)) return;

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

    // 켜져 있는 줄만 고를 수 있다. 줄을 끄는 것은 SetRows뿐이라 다른 메뉴(상세 화면의 버튼 두 개)는 모든 줄이 켜져 있다.
    private bool IsSelectable(int index) => index >= 0 && index < buttons.Length && buttons[index].gameObject.activeSelf;

    private int FirstSelectable()
    {
        for (int i = 0; i < buttons.Length; i++)
            if (IsSelectable(i)) return i;
        return -1;
    }
}
