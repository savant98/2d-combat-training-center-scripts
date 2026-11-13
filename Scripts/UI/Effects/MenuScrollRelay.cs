using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>행의 EventTrigger가 가로채는 휠·드래그 입력을 실제 부모 스크롤에 전달한다.</summary>
public sealed class MenuScrollRelay : MonoBehaviour, IScrollHandler, IInitializePotentialDragHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private UnityEngine.UI.ScrollRect scroll;
    private UnityEngine.UI.ScrollRect Scroll => scroll != null ? scroll : scroll = GetComponentInParent<UnityEngine.UI.ScrollRect>();
    public void OnScroll(PointerEventData data) { if (Scroll != null) Scroll.OnScroll(data); }
    public void OnInitializePotentialDrag(PointerEventData data) { if (Scroll != null) Scroll.OnInitializePotentialDrag(data); }
    public void OnBeginDrag(PointerEventData data) { data.eligibleForClick=false; if (Scroll != null) Scroll.OnBeginDrag(data); }
    public void OnDrag(PointerEventData data) { if (Scroll != null) Scroll.OnDrag(data); }
    public void OnEndDrag(PointerEventData data) { if (Scroll != null) Scroll.OnEndDrag(data); }
}
