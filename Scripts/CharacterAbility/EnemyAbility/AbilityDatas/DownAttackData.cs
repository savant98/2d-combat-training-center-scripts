using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Down Attack")]
public class DownAttackData : EnemyAbilityData
{
    [Header("DownAttack Object")]
    public MeleeHitbox meleeHitbox;
    public WeaponBase.DamageInfo damageInfo;

    [Header("Teleport")]
    public float teleportTriggerTime = 0.05f;
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public FeedbackData teleportStartFeedback;
    public FeedbackData teleportEndFeedback;
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