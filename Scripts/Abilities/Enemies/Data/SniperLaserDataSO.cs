using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Sniper Laser")]
public class SniperLaserDataSO : EnemyAbilityDataSO
{
    [Header("Aim")]
    [Tooltip("플레이어 주변의 조준점 이동 속도(칸/초)입니다. 적과의 거리에 따른 각속도 보정은 사용하지 않습니다.")]
    [Min(0f)] public float aimPointSpeed = 6f;
    public LaserPreviewLine previewPrefab;
    [Min(0.01f)] public float previewWidth = 0.7f;
    public Color previewColor = Color.red;

    [Header("Laser")]
    public LaserBeam laserPrefab;
    [Min(0.01f)] public float laserLength = 200f;
    [Min(0.01f)] public float laserWidth = 0.425f;
    public LayerMask blockMask;
    public LayerMask damageMask;
    public WeaponBaseSO.DamageInfo damageInfo;
}
