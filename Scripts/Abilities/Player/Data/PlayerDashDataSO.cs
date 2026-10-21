using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Dash Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "PlayerDashData")]
public class PlayerDashDataSO : PlayerAbilityDataSO
{
    [Header("Dash Option")]
    public float dashCooldown = 1f;
    public float dashForce = 10f;
    public float dashDuration = 0.15f;

    [Header("Feedback")]
    public FeedbackDataSO dashFeedback;
    public TrailRenderer dashTrail;
}
