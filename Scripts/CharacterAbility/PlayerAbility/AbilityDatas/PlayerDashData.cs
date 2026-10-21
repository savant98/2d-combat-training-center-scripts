using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Dash Data")]
public class PlayerDashData : PlayerAbilityData
{
    [Header("Dash Option")]
    public float dashCooldown = 1f;
    public float dashForce = 10f;
    public float dashDuration = 0.15f;

    [Header("Feedback")]
    public FeedbackData dashFeedback;
    public TrailRenderer dashTrail;
}
