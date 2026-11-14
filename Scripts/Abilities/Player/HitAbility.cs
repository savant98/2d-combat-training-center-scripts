using UnityEngine;
using System.Collections;

public class HitAbility : PlayerAbility
{
    public override PlayerActionType ActionType => PlayerActionType.Hit;
    public override bool IsActive => isGettingHit;

    // Hit Settings
    private float hitDuration;
    private float invincibleTime = 1.5f;

    // Feedbacks
    private FeedbackDataSO feedbackData;

    private bool isGettingHit = false;
    private bool isInvincible = false;

    private PlayerHitDataSO hitData;
    protected override void Awake()
    {
        base.Awake();
        hitData = (PlayerHitDataSO)data;
        hitDuration = hitData.hitDuration;
        invincibleTime = hitData.invincibleTime;
        feedbackData = hitData.feedbackData;
    }
    public bool CanTakeDamage()
    {
        return !isGettingHit && !isInvincible;
    }
    public void Hit()
    {
        manager.InterruptAllAbilities();

        isGettingHit = true;
        isInvincible = true;
        // 상태 머신은 AbilityManager.Update의 끝에서만 갱신된다. 여기서 바로 Hit으로 바꾸지 않으면 피격 직후의 첫 Tick이
        // 피격 전 상태(Idle·Move·Jump)로 판단해, 버퍼에 남은 공격·대시 입력을 피격 자세 도중에 시작한다. 그러면 로직만 흐르고
        // Animator는 피격 자세에 머물러, 자세로 판정을 켜는 해머 1타가 통째로 나가지 않고 넉백도 지워진다.
        manager.StateMachine.SetState(PlayerState.Hit);

        Vector2 force = controller.knockbackForce;
        force.x *= controller.Facing < 0 ? -1f : 1f;
        controller.AddKnockback(force);
        feedbackPlayer.Play(feedbackData, transform);

        StartCoroutine(HitRoutine());
    }
    public void CancelForGrab()
    {
        StopAllCoroutines(); isGettingHit = isInvincible = false;
        controller.animator.SetBool("Hit", false);
    }
    public void ForceStop()
    {
        manager.InterruptAllAbilities();

        StopAllCoroutines();
        isGettingHit = false;
        isInvincible = false;
    }
    private IEnumerator HitRoutine()
    {
        controller.animator.SetBool("Hit", true);
        yield return new WaitForSeconds(hitDuration);

        isGettingHit = false;

        StartCoroutine(IFrameRoutine());
    }
    private IEnumerator IFrameRoutine()
    {
        controller.animator.SetBool("Hit", false);
        yield return new WaitForSeconds(invincibleTime);

        isInvincible = false;
    }
}
