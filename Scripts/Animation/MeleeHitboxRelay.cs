using UnityEngine;

public class MeleeHitboxRelay : MonoBehaviour
{
    [SerializeField] private MeleeHitbox[] hitboxes; // 자식 히트박스들

    // 애니메이션 이벤트에서 호출
    public void EnableHitbox(int index)
    {
        if (hitboxes == null || index < 0 || index >= hitboxes.Length) return;
        hitboxes[index].EnableHitbox();
    }

    public void DisableHitbox(int index)
    {
        if (hitboxes == null || index < 0 || index >= hitboxes.Length) return;
        hitboxes[index].DisableHitbox();
    }

    // 여러 개를 한 번에 켜고 끌 때
    public void EnableAllHitboxes()
    {
        foreach (var hb in hitboxes) hb.EnableHitbox();
    }

    public void DisableAllHitboxes()
    {
        foreach (var hb in hitboxes) hb.DisableHitbox();
    }
}
