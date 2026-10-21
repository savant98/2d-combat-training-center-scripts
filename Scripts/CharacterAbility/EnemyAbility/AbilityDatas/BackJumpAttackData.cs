using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/BackJump Attack")]
public class BackJumpAttackData : EnemyAbilityData
{
    [Header("Jump")]
    public float backJumpSpeedX = 6f;
    public float backJumpSpeedY = 7f;

    [Header("Windup")]
    public float windupGravity = 18f;
    public float windupHorizontalDamping = 1.5f;

    [Header("Execute Hover")]
    public float executeHoverHorizontalMultiplier = 0.35f;
    public float executeHoverHorizontalDamping = 6f;
    public float executeHoverVerticalStabilize = 30f;

    [Header("Recover")]
    public float recoverStartFallSpeed = -1f;
    public float recoverGravity = 22f;
    public float recoverHorizontalDamping = 4f;

    [Header("Recover Ground Check")]
    public float recoverGroundStopThreshold = 0.1f;
    public float groundCheckStartHeight = 5f;
    public float groundCheckDistance = 20f;
    public LayerMask groundBlockMask;

    [Header("Facing")]
    public float faceDeadZone = 0.05f;

    [Header("Projectile")]
    public ProjectileBase projectilePrefab;
    public WeaponBase.DamageInfo projectileDamageInfo;
    public float projectileSpeed = 14f;
    public LayerMask projectileCollideLayer;

    [Header("Multi Shot")]
    public int multiShotCount = 3;
    public float multiShotOffset = 0.6f;
    public float shotAngleFromVertical = 30f;
}
