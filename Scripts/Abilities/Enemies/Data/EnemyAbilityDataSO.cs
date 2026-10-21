using UnityEngine;
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "EnemyAbilityData")]
public abstract class EnemyAbilityDataSO : ScriptableObject
{
    [Header("Ability Key")]
    public string abilityKey;

    [Header("Animation")]
    public AnimationSetSO animationSet;

    [Header("Times")]
    public float cooldown = 1f;
    public float windupTime = 0.3f;
    public float executeTime = 0.5f;
    public float recoverTime = 0.3f;

    [Header("Feedbacks")]
    public FeedbackDataSO windupFeedback;
    public FeedbackDataSO executeFeedback;
    public FeedbackDataSO recoverFeedback;

    [Header("ForceRecover")]
    public bool forceRecoverOnLand = false;
}
