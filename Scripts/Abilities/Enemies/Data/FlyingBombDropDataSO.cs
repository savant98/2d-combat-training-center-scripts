using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Flying Bomb Drop")]
public class FlyingBombDropDataSO : FlyingAbilityDataSO
{
    public BomberFallingBomb bombPrefab;
    [Min(.05f)] public float alignmentTolerance = .45f;
    [Min(.1f)] public float minimumDropHeight = 2f;
    [Min(.1f)] public float maximumDropHeight = 12f;
    [Min(.1f)] public float cruiseHeight = 5f;
    [Header("Drop Brake")]
    [Min(.1f)] public float dropBrakeDuration = .5f;
    [Range(0f, 1f)] public float dropBrakeSpeedRatio = .02f;
    [Min(.1f)] public float dropBrakeDeceleration = 120f;
    public LayerMask bombCollideLayer = 4160;
    public WeaponBaseSO.DamageInfo damageInfo = new() { damage = 20f, damageType = DamageType.Normal };
}
