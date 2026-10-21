using UnityEngine;

public class PlayerCounterState : MonoBehaviour
{
    public bool IsWaiting { get; private set; }
    public bool IsSuccess { get; private set; }

    private System.Action<Vector3> onSuccess;

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