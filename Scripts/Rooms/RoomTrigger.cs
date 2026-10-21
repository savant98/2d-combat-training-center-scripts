using UnityEngine;
using DG.Tweening;

public class RoomTrigger : MonoBehaviour, IInteractable
{
    [Header("Room")]
    [SerializeField] private Room room;
    [SerializeField] private RoomTransitionEffectSO transitionEffect;
    [SerializeField] private TrainingMenuController menuController;

    [Header("Teleport Point")]
    [SerializeField] private Transform teleportPoint;

    [Header("UI")]
    [SerializeField] private CanvasGroup interactUI;

    private bool playerInside;
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
    private void OnTriggerEnter2D(Collider2D col)
    {
        if (!col.TryGetComponent<PlayerController>(out _))
            return;

        playerInside = true;

        if (interactUI != null)
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
        return playerInside;
    }
    public void Interact(PlayerController player)
    {
        if (interactUI != null)
            interactUI.DOFade(0f, 0.3f);

        // Menu를 연다면
        if (menuController != null)
        {
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
        EnterRoom();
        
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
        // Room 이동에는 취소가 없으므로 비워둠
    }
}