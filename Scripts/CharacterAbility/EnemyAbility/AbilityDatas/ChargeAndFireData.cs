using UnityEngine;
using static WeaponBase;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Charge And Fire")]
public class ChargeAndFireData : EnemyAbilityData
{
    [Header("Teleport")]
    public float teleportTriggerTime = 0.15f;
    public Vector2 teleportOffset = new Vector2(2.5f, 3.5f);
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public FeedbackData teleportStartFx;
    public FeedbackData teleportEndFx;
    public LayerMask teleportBlockMask;
    public float groundCheckStartHeight = 5f;
    public float groundCheckDistance = 20f;

    [Header("Preview")]
    public LaserPreviewLine previewLinePrefab;
    public float previewLineLength = 10f;
    public float previewLineWidth = 0.2f;
    public Color previewLineColor = Color.red;
    public LayerMask previewBlockMask;

    [Header("Execute - Slash")]
    [Range(0f, 89f)] public float slashAngleFromDown = 45f;
    public float slashSpeed = 16f;
    public float slashMaxDuration = 0.3f;
    public float slashGroundHitThreshold = 0.1f;
    public MeleeHitbox meleeHitbox;
    public DamageInfo slashDamageInfo;
    public FeedbackData slashFeedback;

    [Header("Slash Mine Scatter")]
    public ProjectileBase minePrefab;
    [Min(1)] public int mineCount = 10;
    [Min(0f)] public float mineLaunchSpeed = 8f;
    public LayerMask minePlatformLayer;
    public DamageInfo mineDamageInfo;

    [Header("Execute - Slash Pause")]
    public float slashPauseDuration = 0.1f;

    [Header("Execute - Reverse Dash")]
    public float reverseDashSpeed = 18f;
    public float reverseDashDuration = 0.2f;
    public float reverseDashDeadZone = 0.05f;
    public FeedbackData sparkFeedback;
    public float sparkInterval = 0.1f;

    [Header("Execute - Brake And Fire")]
    public float brakeDeceleration = 60f;
    public float brakeBeforeFireTime = 0.3f;

    [Header("Laser")]
    public LaserBeam laserPrefab;
    public WeaponBase.DamageInfo laserDamageInfo;
    public float laserLength = 12f;
    public float laserDuration = 0.25f;
    public float laserWidth = 0.25f;
    [Range(0f, 45f)] public float laserAngleOffset = 15f;
    public Color laserEmissionColor = Color.red;
    public LayerMask laserBlockMask;
    public LayerMask laserDamageMask;
    public FeedbackData laserFireFeedback;
}