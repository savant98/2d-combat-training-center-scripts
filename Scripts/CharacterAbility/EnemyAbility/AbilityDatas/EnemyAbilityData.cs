using UnityEngine;
public abstract class EnemyAbilityData : ScriptableObject
{
    [Header("Ability Key")]
    public string abilityKey;

    [Header("Animation")]
    public AnimationSet animationSet;

    [Header("Times")]
    public float cooldown = 1f;
    public float windupTime = 0.3f;
    public float executeTime = 0.5f;
    public float recoverTime = 0.3f;

    [Header("Feedbacks")]
    public FeedbackData windupFeedback;
    public FeedbackData executeFeedback;
    public FeedbackData recoverFeedback;

    [Header("ForceRecover")]
    public bool forceRecoverOnLand = false;
}
