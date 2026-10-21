using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Teleport Sweep Laser")]
public class TeleportSweepLaserData : EnemyAbilityData
{
    [Header("Teleport")]
    public float teleportTriggerTime = 0.2f;
    public Vector2 teleportOffset = new Vector2(6f, 1.5f);
    public LayerMask teleportBlockMask;
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public FeedbackData teleportStartFx;
    public FeedbackData teleportEndFx;

    [Header("Move")]
    public float moveMaxSpeed = 7f;
    public float moveAcceleration = 24f;
    public float moveDeceleration = 32f;
    public float moveArrivalThreshold = 0.08f;

    [Header("Shot")]
    public int maxShotCount = 4;
    public float shotInterval = 0.7f;
    public float firstShotPreviewLeadTime = 0.45f;
    public float previewLeadTime = 0.2f;

    [Header("Aim Tracking")]
    public float aimTrackSpeed = 360f;
    [Range(0f, 1f)] public float minMultiplier = 0.35f;
    public float curvePower = 2f;

    [Header("Preview")]
    public LaserPreviewLine previewLinePrefab;
    public float previewLineWidth = 0.3f;
    public Color previewLineColor = Color.red;

    [Header("Laser")]
    public LaserBeam laserPrefab;
    public WeaponBase.DamageInfo laserDamageInfo;
    public float laserLength = 20f;
    public float laserWidth = 0.45f;
    public float laserDuration = 0.2f;
    public LayerMask laserBlockMask;
    public LayerMask laserDamageMask;

    [Header("Emission")]
    public Color idleEmissionColor = Color.black;
    public Color chargeEmissionColor = Color.yellow;
    public Color fireEmissionColor = Color.red;
}