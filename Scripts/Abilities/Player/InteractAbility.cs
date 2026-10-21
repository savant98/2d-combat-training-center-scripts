using UnityEngine;

public class InteractAbility : PlayerAbility
{
    public override PlayerActionType ActionType => PlayerActionType.Interact;
    public override bool IsActive => isInteracting;

    // Interact Settings
    private float interactRange = 1.5f;
    private LayerMask interactLayer;

    private bool isInteracting;
    private IInteractable currentTarget;
    private PlayerInteractDataSO interactData;

    public override void Initialize(AbilityManager manager)
    {
        base.Initialize(manager);
        interactData = (PlayerInteractDataSO)data;
        interactRange = interactData.interactRange;
        interactLayer = interactData.interactLayer;

        controller.OnInteractInput += TryInteract;
    }

    private void TryInteract()
    {
        if (isInteracting)
            return;

        if (!CanDo())
            return;

        if (!FindInteractable(out currentTarget))
            return;

        EnterInteract();
    }

    private void EnterInteract()
    {
        isInteracting = true;

        // 대상에게 위임
        currentTarget?.Interact(controller);
    }

    private void ExitInteract()
    {
        if (!isInteracting)
            return;

        isInteracting = false;

        currentTarget?.CancelInteract(controller);
        currentTarget = null;
    }

    private bool FindInteractable(out IInteractable interactable)
    {
        interactable = null;

        Collider2D hit = Physics2D.OverlapCircle(
            controller.transform.position,
            interactRange,
            interactLayer);

        if (hit == null)
        {
            return false;
        }

        interactable = hit.GetComponent<IInteractable>();
        return interactable != null && interactable.CanInteract(controller);
    }
    public override void Interrupt()
    {
        ExitInteract();
    }
}
