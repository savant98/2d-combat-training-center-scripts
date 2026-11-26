using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 7차(2026-11-26, DESIGN_V7 §1.4 V7) 현장 단말기 큰 지도의 마우스 입력. EventSystem 포인터 이벤트(MainScene EventSystem + InputSystemUIInputModule, UI Canvas GraphicRaycaster)를
/// 단말기 공개 API로 넘기기만 한다 — 입력 설정 에셋·PlayerInputActions는 건드리지 않는다. Selectable이 아니어서 방향키 내비게이션·Enter 재클릭 문제가 없다.
/// - Role.Map(지도 위 투명 hit Image): 왼쪽 끌기 = 지도 이동(<see cref="DungeonFieldTerminal.PointerPan"/>), 휠 = 확대·축소(<see cref="DungeonFieldTerminal.PointerScroll"/>).
///   끌기 델타는 RectTransformUtility.ScreenPointToLocalPointInRectangle(space, 위치, pressEventCamera)로 앞뒤 점을 지도 좌표로 바꾼 차이다(손목 단말기 배율·회전·CanvasScaler 반영).
/// - Role.ZoomIn·ZoomOut·Recenter(머리줄 버튼 hit Image): 왼쪽 클릭 = 버튼, 올리면 밝게(단말기가 Controls 층에 알림), 휠도 확대·축소.
/// - 왼쪽 버튼만 받는다(오른쪽·가운데 무시). 단말기는 열려 있고 닫는 중이 아니며 최상단 입력 수신자일 때만 받는다(단말기가 확인).
///   단말기가 열린 동안에만 DeviceRoot·ScreenClip CanvasGroup의 blocksRaycasts를 켜므로 평소에는 이벤트가 오지 않는다.
/// - 끌기 상태는 단말기가 들고, 이 컴포넌트가 꺼지면(단말기 닫힘·비활성) 끌기를 끝낸다. 할당 없음(이벤트 처리는 값 계산만).
/// Prefab(Assets/Resources/UI/DungeonFieldTerminal.prefab)은 tools/Unity/DungeonFieldTerminalSetup.cs가 만든다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonFieldTerminalPointer : MonoBehaviour,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public enum Role
    {
        Map = 0,
        ZoomIn = 1,
        ZoomOut = 2,
        Recenter = 3,
    }

    [SerializeField] private DungeonFieldTerminal terminal;
    [SerializeField] private Role role = Role.Map;
    [Tooltip("끌기 좌표를 잴 사각형(큰 지도 MapArea). 비어 있으면 이 오브젝트의 RectTransform.")]
    [SerializeField] private RectTransform space;

    public DungeonFieldTerminal Terminal => terminal;
    public Role CurrentRole => role;
    public RectTransform Space => space != null ? space : transform as RectTransform;

    /// <summary>검증·생성 도구용 연결.</summary>
    public void Configure(DungeonFieldTerminal owner, Role pointerRole, RectTransform mapSpace)
    {
        terminal = owner;
        role = pointerRole;
        space = mapSpace;
    }

    /// <summary>Controls 층 버튼 번호(DungeonFieldTerminalMapGraphic.ControlZoomOut 등). 지도는 −1.</summary>
    public int ControlIndex => role switch
    {
        Role.ZoomOut => DungeonFieldTerminalMapGraphic.ControlZoomOut,
        Role.ZoomIn => DungeonFieldTerminalMapGraphic.ControlZoomIn,
        Role.Recenter => DungeonFieldTerminalMapGraphic.ControlRecenter,
        _ => -1,
    };

    private bool Alive => terminal != null && terminal.AcceptsPointer;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left || role == Role.Map || !Alive) return;
        // 끌기 끝에 따라오는 클릭은 버튼에만 의미가 있다(지도 끌기는 클릭으로 치지 않는다).
        terminal.PressControl(ControlIndex);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (role == Role.Map || terminal == null) return;
        terminal.SetControlHover(ControlIndex, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (role == Role.Map || terminal == null) return;
        terminal.SetControlHover(ControlIndex, false);
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (eventData == null || !Alive) return;
        terminal.PointerScroll(eventData.scrollDelta.y);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left || role != Role.Map || !Alive) return;
        // fix7b: OnBeginDrag는 끌기 문턱(EventSystem.pixelDragThreshold)을 넘은 뒤에 온다. 누른 점(pressPosition)에서 시작해
        //        지금 점까지 바로 옮겨야 지도가 문턱만큼 커서보다 뒤처지지 않는다(V4·V7 '포인터를 따라 이동').
        if (!ToLocal(eventData, eventData.pressPosition, out var press)) return;
        terminal.BeginPointerDrag(press);
        if (ToLocal(eventData, eventData.position, out var local)) terminal.PointerDragTo(local);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left || role != Role.Map || terminal == null || !terminal.IsDragging) return;
        if (!ToLocal(eventData, eventData.position, out var local)) return;
        terminal.PointerDragTo(local);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left || role != Role.Map || terminal == null) return;
        terminal.EndPointerDrag();
    }

    private void OnDisable()
    {
        if (terminal == null) return;
        if (role == Role.Map) terminal.EndPointerDrag();
        else terminal.SetControlHover(ControlIndex, false);
    }

    private bool ToLocal(PointerEventData eventData, Vector2 screenPoint, out Vector2 local)
    {
        var rect = Space;
        local = default;
        return rect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, eventData.pressEventCamera, out local);
    }
}
