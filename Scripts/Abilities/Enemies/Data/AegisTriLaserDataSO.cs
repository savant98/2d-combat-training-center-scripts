using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Aegis Tri Laser")]
public sealed class AegisTriLaserDataSO : FlyingAbilityDataSO
{
    [Header("사거리·3방향 조준")]
    [Min(0f)] public float minRange = 4f;
    [Min(.1f)] public float maxRange = 26f;
    [Range(5f, 80f)] public float spreadAngle = 30f;
    [Min(0f)] public float aimPointSpeed = 6f;
    [Tooltip("발사 직전 조준을 고정해 회피할 수 있는 시간입니다.")]
    [Min(0f)] public float aimLockDuration = .4f;
    [Min(0f)] public float muzzleDistance = 1.55f;

    [Header("예고")]
    public LaserPreviewLine previewPrefab;
    [Min(.01f)] public float previewWidth = .8f;
    public Color previewColor = Color.red;

    [Header("레이저")]
    public LaserBeam laserPrefab;
    [Min(.1f)] public float laserLength = 28f;
    [Min(.01f)] public float laserWidth = .4f;
    public LayerMask blockMask;
    public LayerMask damageMask;
    public WeaponBaseSO.DamageInfo damageInfo;
}
