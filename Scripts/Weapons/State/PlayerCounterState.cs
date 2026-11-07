using UnityEngine;

public class PlayerCounterState : MonoBehaviour
{
    public bool IsWaiting { get; private set; }
    public bool IsSuccess { get; private set; }

    private System.Action<Vector3> onSuccess;
    private PlayerHealth protectedHealth;
    private bool savedInvulnerability;
    public void BeginInvulnerability(PlayerHealth health)
    {
        if (protectedHealth != null) return;
        protectedHealth = health; savedInvulnerability = health.IsInvincible; health.IsInvincible = true;
    }
    public void EndInvulnerability()
    {
        if (protectedHealth == null) return;
        if (protectedHealth.currentHP > 0) protectedHealth.IsInvincible = savedInvulnerability;
        protectedHealth = null;
    }

    public void StartCounter(System.Action<Vector3> onSuccess)
    {
        IsWaiting = true;
        IsSuccess = false;
        this.onSuccess = onSuccess;
    }

    public bool TryCounter(Vector3 hitPosition)
    {
        if (!IsWaiting)
            return false;

        IsWaiting = false;
        IsSuccess = true;

        onSuccess?.Invoke(hitPosition);
        return true;
    }

    public void EndCounter()
    {
        IsWaiting = false;
        IsSuccess = false;
        onSuccess = null;
    }
}