using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Hit Data")]
public class PlayerHitData : PlayerAbilityData
{
    [Header("Hit Settings")]
    public float hitDuration = 0.3f;
    public float invincibleTime = 1.5f;

    [Header("Feedback")]
    public FeedbackData feedbackData;
}
