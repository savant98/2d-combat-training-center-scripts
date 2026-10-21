using UnityEngine;
using static WeaponBase;

[CreateAssetMenu(menuName = "Enemy/Ability Data/LandMine Data")]
public class LandMineData : EnemyAbilityData
{
    [Header("Scatter Settings")]
    public ProjectileBase minePrefab;
    public int mineCount = 12;
    public float launchSpeed = 8f;
    public float scatterInterval = 0.2f;

    [Header("Mine Settings")]
    public DamageInfo damageInfo;
    public LayerMask platformLayer;
}
