using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Aegis Missile")]
public sealed class AegisMissileDataSO : FlyingAbilityDataSO
{
    public ConstructorHomingMissile projectilePrefab;
    public WeaponBaseSO.DamageInfo damageInfo;
    public WeaponBaseSO.DamageInfo explosionDamageInfo;
    [Min(0f)] public float minRange = 5f;
    [Min(.1f)] public float maxRange = 24f;
    [Min(.1f)] public float projectileSpeed = 8f;
    [Min(0f)] public float explosionRadius = 1.5f;
    [Min(0f)] public float trackingDuration = 3.5f;
    [Min(0f)] public float turnRateDegrees = 125f;
    [Min(.1f)] public float lifeTime = 5f;
    public LayerMask collideLayer = 64;
}
