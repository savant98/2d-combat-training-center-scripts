using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Down Attack")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "DownAttackData")]
public class DownAttackDataSO : EnemyAbilityDataSO
{
    [Header("DownAttack Object")]
    public MeleeHitbox meleeHitbox;
    public WeaponBaseSO.DamageInfo damageInfo;

    [Header("Teleport")]
    public float teleportTriggerTime = 0.05f;
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public FeedbackDataSO teleportStartFeedback;
    public FeedbackDataSO teleportEndFeedback;
    public Vector2 teleportOffset = new Vector2(0f, 8f);

    [Header("Preview Line")]
    public LaserPreviewLine previewLinePrefab;
    public Color previewLineColor = Color.red;
    public float previewLineWidth = 0.08f;
    public float previewLineLength = 20f;
    public LayerMask previewBlockMask;

    [Header("Windup Move")]
    public float windupRiseSpeed = 2f;
    public float windupFollowSpeed = 8f;

    [Header("Execute Move")]
    public float slamSpeed = 10f;

    [Header("Recover Move")]
    public float recoverBounceHeight = 1f;
}