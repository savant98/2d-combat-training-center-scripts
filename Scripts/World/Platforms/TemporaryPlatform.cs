using UnityEngine;

public class TemporaryPlatform : WorldEntityBase
{
    [SerializeField] private Animator animator;
    [SerializeField] private string animTriggerName;
    public override void ForceReturnToPool()
    {
        animator.SetTrigger(animTriggerName);
    }

    public void ForceReturnTrigger()
    {
        StopAllCoroutines();
        isReturning = false;
        ReturnToPool();
    }
}
