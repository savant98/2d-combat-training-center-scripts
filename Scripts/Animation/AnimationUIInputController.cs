using UnityEngine;

public class AnimationUIInputController : MonoBehaviour
{
    public void UIInputBlockOn()
    {
        UIManager.Instance.SetUIInputBlock(true);
    }
    public void UIInputBlockOff()
    {
        UIManager.Instance.SetUIInputBlock(false);
    }
}
