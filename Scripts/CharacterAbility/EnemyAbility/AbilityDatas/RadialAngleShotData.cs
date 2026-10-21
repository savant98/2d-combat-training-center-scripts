using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Radial Angle Shot Data")]
public class RadialAngleShotData : EnemyAbilityData
{
    [Header("Projectile")]
    public ProjectileBase projectilePrefab;
    public WeaponBase.DamageInfo projectileDamageInfo;
    public float projectileSpeed = 12f;
    public LayerMask projectileCollideLayer;

    [Header("Teleport")]
    public float teleportTriggerTime = 0.2f;
    public Vector2 teleportOffset = new Vector2(0f, 4f);
    public FeedbackData teleportStartFx;
    public FeedbackData teleportEndFx;
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;

    [Header("Ground Check")]
    public LayerMask teleportBlockMask;
    public float groundCheckStartHeight = 5f;
    public float groundCheckDistance = 50f;
    public float faceDeadZone = 0.05f;

    [Header("Radial Shot")]
    public int totalShotCount = 12;
    public float shotInterval = 0.1f;
    public float startAngleDeg = 90f;
    public bool clockwise = true;
    public bool fireFirstShotImmediately = true;
}