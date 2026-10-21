using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Jump Data")]
public class PlayerJumpData : PlayerAbilityData
{
    [Header("Jump Option")]
    public float jumpForce = 10f;
    public int maxJumpCount = 2;
    public float lowGravity = -15f;
    public float highGravity = -55f;

    [Header("Feedback")]
    public FeedbackData startJumpFeedback;
    public FeedbackData extraJumpFeedback;
}
