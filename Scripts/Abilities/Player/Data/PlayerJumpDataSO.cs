using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Jump Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "PlayerJumpData")]
public class PlayerJumpDataSO : PlayerAbilityDataSO
{
    [Header("Jump Option")]
    public float jumpForce = 10f;
    public int maxJumpCount = 2;
    public float lowGravity = -15f;
    public float highGravity = -55f;

    [Header("Feedback")]
    public FeedbackDataSO startJumpFeedback;
    public FeedbackDataSO extraJumpFeedback;
}
