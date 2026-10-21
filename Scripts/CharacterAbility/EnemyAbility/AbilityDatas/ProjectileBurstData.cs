using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Projectile Burst")]
public class ProjectileBurstData : EnemyAbilityData
{
    [Header("Projectile")]
    public ProjectileBase projectilePrefab;
    public LayerMask collideLayer;
    public WeaponBase.DamageInfo damageInfo;
    public float projectileSpeed = 8f;

    [Header("Burst")]
    public int count = 3;
    public float interval = 0.1f;

    [Header("Fire")]
    public bool facePlayerOnWindup = true;
    public bool facePlayerBeforeEachShot = false;
    public bool useExecutePoint = true;
}