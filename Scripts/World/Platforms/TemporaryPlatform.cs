using UnityEngine;

public class TemporaryPlatform : WorldEntityBase
{
    [SerializeField] private Animator animator;
    [SerializeField] private string animTriggerName;
    [Tooltip("풀에서 꺼내 나타날 때 재생할 등장음입니다.")]
    [SerializeField] private FeedbackDataSO spawnFeedback;

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        if (spawnFeedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.Play(spawnFeedback, transform);
    }

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
