using UnityEngine;

// Model의 AnimationClip 이벤트를 루트의 BurstChargeAbility로 전달한다.
// 공회전을 끊어 올리는 시점(rev)은 Windup Clip이 정하고, 효과의 방향과 차지 소리 조각은 Ability가 바라보는 방향과 rev 순서로 맞춘다.
// Animation 이벤트는 Animator와 같은 GameObject의 컴포넌트만 받으므로 Model에 둔다.
public class BurstChargeAnimationRelay : MonoBehaviour
{
    [SerializeField] private BurstChargeAbility ability;

    // Windup Clip 이벤트: intParameter는 0부터 시작하는 rev 순서다.
    public void OnChargeRev(int index)
    {
        if (ability != null)
            ability.PlayRev(index);
    }
}
