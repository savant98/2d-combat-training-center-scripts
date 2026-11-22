using DG.Tweening;
using UnityEngine;

/// <summary>
/// 각개 전투 엘리베이터의 탑승 입구(2026-11-20). 케이지 앞의 상호작용 영역(Layer 8 Interact, Trigger)이다.
/// - 상호작용 표시와 Lobby 잠금(LobbyInteractionLock)은 RoomTrigger와 같은 방식으로 다룬다. RoomTrigger가 아니므로
///   Lobby 안내(LobbyOnboarding.FindEntrance)가 훈련 입구를 고를 때 섞이지 않고, 훈련 메뉴도 열지 않는다.
/// - 첫 입력은 문을 열고, 다 열린 뒤의 다음 입력은 탑승을 요청한다. 탑승 전에 멀어지면 문이 자동으로 닫힌다.
/// </summary>
[DisallowMultipleComponent]
public sealed class FieldElevatorTrigger : MonoBehaviour, IInteractable
{
    [SerializeField] private FieldElevator elevator;
    [SerializeField, Tooltip("상호작용 표시(RoomTrigger의 버튼 그림 사본)")] private CanvasGroup interactUI;
    [SerializeField, Min(0f), Tooltip("탑승 입구 중심에서 이 거리 이상 멀어지면 열린 문이 닫힌다.")] private float autoCloseDistance = 3f;

    [Header("소리")]
    [SerializeField, Tooltip("문 열기 입력")] private SoundOptions armSound;
    [SerializeField, Tooltip("출발할 수 없을 때")] private SoundOptions rejectSound;

    private BoxCollider2D boardingCollider;
    private bool playerInside;
    private bool hintVisible;

    public FieldElevator Elevator => elevator;
    public bool PlayerInside => playerInside;

    private void Awake()
    {
        boardingCollider = GetComponent<BoxCollider2D>();
        WorldInteractionBindingHint.Bind(interactUI);
        if (interactUI != null) interactUI.alpha = 0f;
    }

    private void OnDisable()
    {
        playerInside = false;
        hintVisible = false;
        if (interactUI != null) { interactUI.DOKill(); interactUI.alpha = 0f; }
    }

    private void Update()
    {
        if (elevator != null && elevator.CanBoard && !playerInside)
        {
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            Vector2 center = boardingCollider != null ? transform.TransformPoint(boardingCollider.offset) : transform.position;
            if (player == null || ((Vector2)player.transform.position - center).sqrMagnitude >= autoCloseDistance * autoCloseDistance)
                elevator.Car.SetDoors(false);
        }
        UpdateHint();
    }

    private bool CanUseEntrance => playerInside && elevator != null && elevator.CanInteractAtDock;

    private void UpdateHint()
    {
        bool show = CanUseEntrance && !LobbyInteractionLock.IsBlocked(this);
        if (show == hintVisible) return;
        hintVisible = show;
        if (interactUI == null) return;
        interactUI.DOKill();
        interactUI.DOFade(show ? 1f : 0f, .3f);
    }

    private void OnTriggerEnter2D(Collider2D col)
    {
        if (!col.TryGetComponent<PlayerController>(out _)) return;
        playerInside = true;
        UpdateHint();
    }

    private void OnTriggerExit2D(Collider2D col)
    {
        if (!col.TryGetComponent<PlayerController>(out _)) return;
        playerInside = false;
        UpdateHint();
    }

    public bool CanInteract(PlayerController player)
    {
        if (!CanUseEntrance) return false;
        if (LobbyInteractionLock.IsBlocked(this))
        {
            LobbyInteractionLock.NotifyRejected(this);
            return false;
        }
        return true;
    }

    public void Interact(PlayerController player)
    {
        if (CanInteract(player))
        {
            if (!elevator.Car.DoorsOpen)
            {
                elevator.Car.SetDoors(true);
                PlaySound(armSound);
            }
            else if (elevator.RequestRide())
            {
                UpdateHint();
                return;
            }
            else PlaySound(rejectSound);
        }
        UpdateHint();
        // 문이 열린 뒤 새 입력을 받을 수 있도록 이번 상호작용을 끝낸다.
        if (player != null) player.AbilityManager?.InteractAbility?.Interrupt();
    }

    public void CancelInteract(PlayerController player) { }

    private void PlaySound(SoundOptions sound)
    {
        if (sound != null && sound.clip != null && SoundManager.Instance != null) SoundManager.Instance.PlaySound(sound, transform.position);
    }
}
