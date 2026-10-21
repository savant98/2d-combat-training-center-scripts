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
    private FeedbackData feedbackData;

    private bool isGettingHit = false;
    private bool isInvincible = false;

    private PlayerHitData hitData;
    protected override void Awake()
    {
        base.Awake();
        hitData = (PlayerHitData)data;
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

        Vector2 force = controller.knockbackForce;
        force.x *= controller.Facing < 0 ? -1f : 1f;
        controller.AddKnockback(force);
        feedbackPlayer.Play(feedbackData, transform);

        StartCoroutine(HitRoutine());
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
