using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Teleport Retreat Laser")]
public class TeleportRetreatLaserData : EnemyAbilityData
{
    [Header("Teleport")]
    public float teleportTriggerTime = 0.2f;
    public Vector2 teleportOffset = new Vector2(3f, 1.5f);
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public FeedbackData teleportStartFx;
    public FeedbackData teleportEndFx;
    public LayerMask teleportBlockMask;
    public float standPositionCheckRadius = 0.3f;

    [Header("Ground Check")]
    public float groundCheckStartHeight = 5f;
    public float groundCheckDistance = 50f;

    [Header("Retreat Move")]
    public float postTeleportMoveSpeed = 6f;
    public float postTeleportDeceleration = 20f;

    [Header("Spark")]
    public FeedbackData sparkFeedback;
    public float sparkInterval = 0.08f;

    [Header("Preview")]
    public LaserPreviewLine previewLinePrefab;
    public float previewLineWidth = 0.2f;
    public Color previewLineColor = Color.red;

    [Header("Laser")]
    public LaserBeam laserPrefab;
    public WeaponBase.DamageInfo laserDamageInfo;
    public float laserLength = 12f;
    public float laserWidth = 0.35f;
    public float laserDuration = 0.35f;
    public LayerMask laserBlockMask;
    public LayerMask laserDamageMask;
    public Color fireEmissionColor = Color.white;
}