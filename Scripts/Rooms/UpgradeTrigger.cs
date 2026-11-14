using UnityEngine;
using DG.Tweening;

public class UpgradeTrigger : MonoBehaviour, IInteractable
{
    [Header("UpgradeMenu")]
    [SerializeField] private UpgradeMenu menu;

    [Header("UI")]
    [SerializeField] private CanvasGroup interactUI;

    private bool playerInside;
    // Lobby 안내가 설비를 잠가 둔 동안에는 상호작용 표시를 띄우지 않는다(LobbyInteractionLock).
    private bool blocked;
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

        // Menu를 연다면
        if (menu != null)
        {
            menu.OpenMenu();
        }
    }

    public void CancelInteract(PlayerController player)
    {

    }
}