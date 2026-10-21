public interface IInteractable
{
    bool CanInteract(PlayerController player);
    void Interact(PlayerController player);
    void CancelInteract(PlayerController player);
}
