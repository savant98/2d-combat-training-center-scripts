using UnityEngine;
using DG.Tweening;

public class RoomTrigger : MonoBehaviour, IInteractable
{
    [Header("Room")]
    [SerializeField] private Room room;
    [SerializeField] private RoomTransitionEffectSO transitionEffect;
    [SerializeField] private TrainingMenuController menuController;
    [SerializeField] private SelfTrainingEntrancePanel selfTrainingPanel;

    [Header("Teleport Point")]
    [SerializeField] private Transform teleportPoint;

    [Header("UI")]
    [SerializeField] private CanvasGroup interactUI;

    public Transform TeleportPoint => teleportPoint;
    public Room TargetRoom => room;
    public bool OpensTrainingMenu => menuController != null;

    // 상호작용이 실제로 일어났을 때(메뉴를 열거나 방을 옮길 때). Lobby 안내가 훈련 메뉴를 열었는지 알 때 쓴다.
    public static event System.Action<RoomTrigger> Interacted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Interacted = null;

    private bool playerInside;
    // Lobby 안내가 입구를 잠가 둔 동안에는 상호작용 표시를 띄우지 않는다(LobbyInteractionLock).
    private bool blocked;
    private void OnDrawGizmos()
    {
        if (teleportPoint == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, teleportPoint.position);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(teleportPoint.position, 0.5f);
    }
    private void Awake()
    {
        if (interactUI != null)
            interactUI.alpha = 0;
    }
    private void Update()
    {
        // 잠금이 걸리거나 풀리는 순간에만 표시를 바꾼다(그 밖의 표시 시점은 예전 그대로다).
        bool now = LobbyInteractionLock.IsBlocked(this);
        if (now == blocked) return;
        blocked = now;
        if (playerInside && interactUI != null)
            interactUI.DOFade(blocked ? 0f : 1f, 0.3f);
    }
    private void OnTriggerEnter2D(Collider2D col)
    {
        if (!col.TryGetComponent<PlayerController>(out _))
            return;

        playerInside = true;
        blocked = LobbyInteractionLock.IsBlocked(this);

        if (interactUI != null && !blocked)
        {
            interactUI.DOFade(1f, 0.3f);
        }
    }

    private void OnTriggerExit2D(Collider2D col)
    {
        if (!col.TryGetComponent<PlayerController>(out _))
            return;

        playerInside = false;

        if (interactUI != null)
        {
            interactUI.DOFade(0f, 0.3f);
        }   
    }

    public bool CanInteract(PlayerController player)
    {
        if (!playerInside)
            return false;

        if (LobbyInteractionLock.IsBlocked(this))
        {
            LobbyInteractionLock.NotifyRejected(this);
            return false;
        }

        return true;
    }
    public void Interact(PlayerController player)
    {
        if (interactUI != null)
            interactUI.DOFade(0f, 0.3f);

        Interacted?.Invoke(this);

        // Menu를 연다면
        if (selfTrainingPanel != null)
        {
            selfTrainingPanel.Show(EnterRoom, player);
        }
        else if (menuController != null)
        {
            menuController.OnConfirmEnterRoom -= HandleEnterRoom;
            menuController.Show();
            menuController.OnConfirmEnterRoom += HandleEnterRoom;
        }
        else // 메뉴 없이 즉시 이동
        {
            EnterRoom();
        }
    }
    public void HandleEnterRoom()
    {
        menuController.OnConfirmEnterRoom -= HandleEnterRoom;
        RoomTransitionManager.Instance.RequestTransition(new RoomTransitionRequest
        {
            targetRoom = menuController.SelectedRoom != null ? menuController.SelectedRoom : room,
            waveData = menuController.SelectedWaveData,
            teleportPoint = menuController.SelectedSpawnPoint != null ? menuController.SelectedSpawnPoint : teleportPoint,
            transitionEffect = transitionEffect
        });
    }
    private void EnterRoom()
    {
        RoomTransitionManager.Instance.RequestTransition(
                new RoomTransitionRequest
                {
                    targetRoom = room,
                    teleportPoint = teleportPoint,
                    transitionEffect = transitionEffect
                });
    }
    public void CancelInteract(PlayerController player)
    {
        if (selfTrainingPanel != null) selfTrainingPanel.Hide();
        // Room 이동에는 취소가 없으므로 비워둠
    }
}