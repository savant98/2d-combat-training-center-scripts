using DG.Tweening;
using UnityEngine;

/// <summary>상호작용 지점(FacilityInteractPoint)이 처리를 넘기는 시설. 부모(시설 루트)의 컴포넌트가 구현한다.</summary>
public interface IFacilityInteractTarget
{
    /// <summary>키 안내를 보일지(지금 쓸 수 있고, Player가 쓸 자리에 있음).</summary>
    bool ShowFacilityHint(PlayerController player, FacilityInteractPoint point);
    /// <summary>상호작용 키를 눌렀다. 쓸 수 없으면 거절 소리를 낸다. 끝나면 지점이 Interact 상태를 끝낸다.</summary>
    void FacilityInteract(PlayerController player, FacilityInteractPoint point);
}

/// <summary>
/// 시설의 상호작용 영역(Layer 8 Interact 트리거). InteractAbility는 찾은 Collider와 같은 GameObject의 IInteractable만 보므로
/// 트리거마다 이 컴포넌트를 두고, 처리는 부모의 IFacilityInteractTarget에 넘긴다.
/// - 키 안내는 RoomTrigger의 표시 사본(CanvasGroup + ButtonImage, WorldInteractionBindingHint로 현재 키)을 쓰고,
///   FieldElevatorTrigger처럼 쓸 수 있을 때만 0.3초 동안 나타난다. 거리는 물리 질의 없이 트리거 상자와 Player 발의 거리로 잰다.
/// - Interact 안에서 반드시 InteractAbility.Interrupt를 불러 Player가 Interact 상태로 굳지 않게 한다(예외가 나도 finally).
/// </summary>
[DisallowMultipleComponent]
public sealed class FacilityInteractPoint : MonoBehaviour, IInteractable
{
    [SerializeField, Tooltip("상호작용 표시(RoomTrigger의 버튼 그림 사본)")] private CanvasGroup interactUI;
    [SerializeField, Min(0f), Tooltip("안내를 띄우는 거리(Player 발 ~ 트리거 상자). InteractAbility 범위(1.5)와 같게 둔다.")] private float hintRange = 1.5f;
    [SerializeField, Tooltip("쓸 수 없을 때(방 미클리어·방전·사용 중)")] private SoundOptions rejectSound;
    [SerializeField, Tooltip("같은 시설에 지점이 여럿일 때 구분하는 번호(하나뿐이면 0).")] private int slot;

    private IFacilityInteractTarget target;
    private BoxCollider2D area;
    private bool hintVisible;

    public int Slot => slot;
    public bool HintVisible => hintVisible;
    /// <summary>
    /// 상호작용 범위 안 표시(키 안내)가 켜지거나(true) 꺼질(false) 때 올린다(3차 계약). FacilityHighlight가 구독해 테두리를 꾸준히 밝힌다(4차: 쉐브론 없음). FacilityZip은 HintVisible이 켜진 지점이 있으면 붙기 입력을 양보한다.
    /// 값이 실제로 바뀔 때만 올린다.
    /// </summary>
    public event System.Action<bool> HighlightChanged;
    /// <summary>지금 범위 안 표시가 켜져 있는지(<see cref="HighlightChanged"/>의 마지막 값).</summary>
    public bool Highlighted => hintVisible;
    /// <summary>상호작용을 받은 횟수(검사용).</summary>
    public int Interactions { get; private set; }
    /// <summary>거절한 횟수(검사용).</summary>
    public int Rejections { get; private set; }
    /// <summary>키 안내가 숨은 채 거절할 뻔한 입력을 붙기에 양보한 횟수(검사용, fix4zip — 거절음·Rejections 없음).</summary>
    public int ZipYields { get; private set; }

    // 지금 Interact 안에서 처리 중인 Player(Reject가 이번 상호작용 입력의 거절인지 알기 위해). Interact 밖이면 null.
    private PlayerController interacting;

    // 켜져 있는 상호작용 지점(옮겨지는 시설이 다른 지점과 겹치지 않게 자리를 고를 때 읽는다 — 물리 질의 없음).
    private static readonly System.Collections.Generic.List<FacilityInteractPoint> live = new System.Collections.Generic.List<FacilityInteractPoint>();
    /// <summary>지금 켜져 있는 상호작용 지점 목록(읽기 전용).</summary>
    public static System.Collections.Generic.IReadOnlyList<FacilityInteractPoint> Live => live;

    private void OnEnable() { if (!live.Contains(this)) live.Add(this); }

    private void Awake()
    {
        area = GetComponent<BoxCollider2D>();
        target = GetComponentInParent<IFacilityInteractTarget>(true);
        WorldInteractionBindingHint.Bind(interactUI);
        if (interactUI != null) interactUI.alpha = 0f;
    }

    private void OnDisable()
    {
        live.Remove(this);
        bool was = hintVisible;
        hintVisible = false;
        if (interactUI != null) { interactUI.DOKill(); interactUI.alpha = 0f; }
        if (was) RaiseHighlight(false);
    }

    // 표시 상태가 바뀐 뒤 구독자에게 알린다. 구독자 예외가 안내 갱신을 막지 않게 따로 잡는다.
    private void RaiseHighlight(bool on)
    {
        try { HighlightChanged?.Invoke(on); }
        catch (System.Exception e) { Debug.LogException(e, this); }
    }

    /// <summary>트리거 상자(월드, 회전 없음).</summary>
    public Rect Area
    {
        get
        {
            if (area == null) area = GetComponent<BoxCollider2D>();
            if (area == null) return new Rect(transform.position, Vector2.zero);
            var scale = transform.lossyScale;
            Vector2 size = new Vector2(Mathf.Abs(area.size.x * scale.x), Mathf.Abs(area.size.y * scale.y));
            Vector2 center = transform.TransformPoint(area.offset);
            return new Rect(center - size * .5f, size);
        }
    }

    /// <summary>Player 발이 이 지점의 상호작용 거리 안에 있는지.</summary>
    public bool InRange(PlayerController player)
    {
        if (player == null) return false;
        return FacilityAccess.DistanceToRect(player.transform.position, Area) <= hintRange;
    }

    private void Update()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        bool show = target != null && player != null && InRange(player) && target.ShowFacilityHint(player, this);
        SetHint(show);
    }

    private void SetHint(bool show)
    {
        if (show == hintVisible) return;
        hintVisible = show;
        RaiseHighlight(show);
        if (interactUI == null) return;
        interactUI.DOKill();
        interactUI.DOFade(show ? 1f : 0f, .3f);
    }

    public bool CanInteract(PlayerController player) => target != null && player != null && isActiveAndEnabled;

    public void Interact(PlayerController player)
    {
        Interactions++;
        var previous = interacting;
        interacting = player;
        try
        {
            if (target != null) target.FacilityInteract(player, this);
        }
        finally
        {
            interacting = previous;
            // 처리 결과와 관계없이 이번 상호작용을 끝낸다(다음 입력을 받을 수 있게, Interact 상태 고착 방지).
            FacilityAccess.EndInteract(player);
        }
    }

    public void CancelInteract(PlayerController player) { }

    /// <summary>
    /// 쓸 수 없다는 짧은 소리. fix4zip: 키 안내가 숨은(쓸 수 없는) 지점이 상호작용 입력(↑)을 거절할 때, 같은 입력을 붙기가 받을 수 있으면
    /// (FacilityZip.WouldTakePress) 거절음·Rejections 없이 붙기에 양보한다. 붙기 후보가 없거나 키 안내가 보이는 지점이면 지금처럼 거절한다.
    /// </summary>
    public void Reject()
    {
        if (interacting != null && !hintVisible && FacilityZip.WouldTakePress(interacting))
        {
            ZipYields++;
            return;
        }
        Rejections++;
        FacilityFeedback.Play(rejectSound, transform.position);
    }

    /// <summary>안내를 즉시 숨긴다(사용을 시작한 순간).</summary>
    public void HideHint()
    {
        bool was = hintVisible;
        hintVisible = false;
        if (interactUI != null) { interactUI.DOKill(); interactUI.DOFade(0f, .15f); }
        if (was) RaiseHighlight(false);
    }
}
