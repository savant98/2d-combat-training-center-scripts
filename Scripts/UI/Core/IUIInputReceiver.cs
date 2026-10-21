using UnityEngine;
public interface IUIInputReceiver
{
    bool IsActive { get; }
    void OnMove(Vector2 dir);
    void OnConfirm();
    void OnCancel();
}
