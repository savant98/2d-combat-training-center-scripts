using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Central Area Attack Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "CentralAreaAttackData")]
public class CentralAreaAttackDataSO : EnemyAbilityDataSO
{
    [Header("Teleport Timing")]
    public float teleportTriggerTime = 0.15f;
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public FeedbackDataSO teleportStartFeedback;
    public FeedbackDataSO teleportEndFeedback;

    [Header("Wall Search")]
    public float wallSearchDistance = 20f;
    public float wallCheckHeightOffset = 1f;
    public float wallOffsetX = 2f;

    [Header("Height / Ground")]
    public float teleportHeightOffset = 8f;
    public float groundCheckStartHeight = 15f;
    public float groundCheckDistance = 40f;
    public float landOffsetY = 0f;
    public float landDeadZone = 0.1f;

    [Header("Hover / Drop")]
    public float hoverTime = 0.25f;
    public float hoverRiseSpeed = 1.5f;
    public float dropSpeed = 18f;

    [Header("Preview")]
    public LaserPreviewLine previewLinePrefab;
    public float previewLineLength = 12f;
    public float previewLineWidth = 0.2f;
    public Color previewLineColor = Color.red;
    public LayerMask previewBlockMask;

    [Header("Slam")]
    public MeleeHitbox meleeHitbox;
    public WeaponBaseSO.DamageInfo slamDamageInfo;

    [Header("Optional")]
    public float faceDeadZone = 0.05f;
    public LayerMask teleportBlockMask;
}