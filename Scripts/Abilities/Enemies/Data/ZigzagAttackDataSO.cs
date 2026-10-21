using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Zigzag Attack")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "ZigzagAttackData")]
public class ZigzagAttackDataSO : EnemyAbilityDataSO
{
    [Header("Windup")]
    public Vector2 windupRiseOffset = new Vector2(2f, 3f);
    public float windupMoveSpeedX = 8f;
    public float windupMoveSpeedY = 10f;
    public float windupDeadZone = 0.05f;
    public float faceDeadZone = 0.05f;

    [Header("Ground Check")]
    public float groundCheckStartHeight = 5f;
    public float groundCheckDistance = 50f;
    public LayerMask groundCheckMask;

    [Header("Execute")]
    public float executeHorizontalSpeed = 15f;
    public float executeVerticalAmplitude = 15f;
    public float executeSineCycleCount = 2f;

    [Header("Cycle Timing")]
    public float executeCycleDuration = 0.6f;
    public float cycleTurnDuration = 0.2f;
    public float cycleTurnRiseSpeed = 4f;

    [Header("Scatter Projectile")]
    public ProjectileBase projectile;
    public Vector2 projectileSpawnOffset;
    public float projectileShotAngle = 30f;
    public float projectileSpawnCheckRadius = 0.3f;
    public LayerMask projectileSpawnBlockMask;
    public LayerMask projectileOverlapMask;
    public float projectileSpeed = 8f;
    public int projectileCount = 5;
    public float scatterInterval = 0.1f;
    public WeaponBaseSO.DamageInfo projectileDamageInfo;

    [Header("Recover")]
    public float recoverHoldTime = 0.12f;
    public float recoverRiseSpeed = 1.5f;   // 0이면 그냥 정지
    public float recoverDropSpeed = 18f;
    public float recoverTrackSpeed = 6f;
    public float recoverGroundOffset = 0f;

    [Header("Slam")]
    public MeleeHitbox meleeHitbox;
    public WeaponBaseSO.DamageInfo slamDamageInfo;
    public FeedbackDataSO slamFeedback;

    [Header("Recover Preview")]
    public LaserPreviewLine previewLinePrefab;
    public float previewLineLength = 10f;
    public float previewLineWidth = 0.5f;
    public LayerMask previewBlockMask;
    public Color previewLineColor = Color.red;
}
