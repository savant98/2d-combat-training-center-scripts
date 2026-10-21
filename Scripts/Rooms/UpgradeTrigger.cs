using UnityEngine;
using DG.Tweening;

public class UpgradeTrigger : MonoBehaviour, IInteractable
{
    [Header("UpgradeMenu")]
    [SerializeField] private UpgradeMenu menu;

    [Header("UI")]
    [SerializeField] private CanvasGroup interactUI;

    private bool playerInside;
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
        if (menu != null)
        {
            menu.OpenMenu();
        }
    }

    public void CancelInteract(PlayerController player)
    {

    }
}