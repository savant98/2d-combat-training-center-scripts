using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Slash And Charge Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "SlashAndChargeData")]
public class SlashAndChargeDataSO : EnemyAbilityDataSO
{
    [Header("Teleport")]
    public float teleportTriggerTime = 0.1f;
    public Vector2 teleportOffset = new Vector2(2f, 0f);
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public FeedbackDataSO teleportStartFx;
    public FeedbackDataSO teleportEndFx;
    public LayerMask teleportBlockMask;
    public float standPositionCheckRadius = 0.4f;

    [Header("Ground Check")]
    public float groundCheckStartHeight = 5f;
    public float groundCheckDistance = 50f;
    public LayerMask groundCheckMask;

    [Header("Execute")]
    public float executeSlashDelay = 0.2f;
    public float executeShotDelay = 0.3f;

    [Header("Charge")]
    public float chargeDistance = 5f;
    public float chargeDuration = 0.35f;
    public AnimationCurve chargeSpeedCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Angle Shot")]
    public ProjectileBase projectilePrefab;
    public WeaponBaseSO.DamageInfo projectileDamageInfo;
    public float projectileSpeed = 8f;
    public LayerMask projectileCollideLayer;
    public int shotCount = 10;
    [Range(0f, 89f)] public float shotAngleFromVertical = 30f;

    [Header("Shot Placement")]
    public Vector2 shotAnchorOffset = new Vector2(0f, 2f);
    public Vector2 shotSpacingOffset = new Vector2(0.8f, 0f);
    public int shotSpawnMaxSearchDepth = 20;

    [Header("Shot Spawn Validation")]
    public float shotSpawnCheckRadius = 0.25f;
    public LayerMask shotSpawnBlockMask;
    public LayerMask shotSpawnOverlapMask;
}