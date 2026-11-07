using UnityEngine;

// 표시 pose만 복원한다. 투하·재장착의 숨김과 움직임은 전용 Clip이 담당한다.
public class BomberBombRack : MonoBehaviour
{
    [SerializeField] private Transform bomb;
    [SerializeField] private Transform frame;
    private void OnEnable() => ResetPose();
    private void OnDisable() => ResetPose();
    public void ResetPose()
    {
        if (bomb != null) { bomb.localPosition = new Vector3(0, -.65f, 0); bomb.localScale = Vector3.one; bomb.localRotation = Quaternion.identity; }
        if (frame != null) { frame.localPosition = Vector3.zero; frame.localScale = Vector3.one; }
    }
}
