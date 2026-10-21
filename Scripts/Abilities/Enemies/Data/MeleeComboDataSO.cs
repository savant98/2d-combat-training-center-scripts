using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Melee Combo")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "MeleeComboData")]
public class MeleeComboDataSO : EnemyAbilityDataSO
{
    [Header("Windup Teleport")]
    public float windupTeleportTriggerTime = 0.1f;
    public Vector2 windupFrontOffset = new Vector2(2f, 0f);
    public FeedbackDataSO teleportStartFeedback;
    public FeedbackDataSO teleportEndFeedback;
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;

    [Header("Execute Teleport Timing")]
    public float firstTeleportTriggerTime = 0.15f;
    public float secondTeleportTriggerTime = 0.55f;

    [Header("Execute Teleport Offset")]
    public float backTeleportOffsetX = 2f;
    public float airTeleportOffsetY = 2.5f;

    [Header("Air Hold & Slam")]
    public float airHoldDuration = 0.25f;
    public float airRiseSpeed = 1.25f;
    public float slamSpeed = 16f;
    public float groundStopThreshold = 0.05f;

    [Header("Ground Check")]
    public LayerMask groundCheckMask;
    public float groundCheckStartHeight = 5f;
    public float groundCheckDistance = 50f;

    [Header("Teleport Block Check")]
    public LayerMask teleportBlockMask;
}