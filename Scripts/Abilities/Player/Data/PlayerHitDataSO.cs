using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Hit Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "PlayerHitData")]
public class PlayerHitDataSO : PlayerAbilityDataSO
{
    [Header("Hit Settings")]
    public float hitDuration = 0.3f;
    public float invincibleTime = 1.5f;

    [Header("Feedback")]
    public FeedbackDataSO feedbackData;
}
