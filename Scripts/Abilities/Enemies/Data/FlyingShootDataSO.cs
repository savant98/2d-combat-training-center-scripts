using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Flying Shoot")]
public class FlyingShootDataSO : FlyingAbilityDataSO
{
    [Header("Projectile")]
    [Tooltip("발사할 탄 Prefab입니다(Drone은 RedStarCannon).")]
    public ProjectileBase projectilePrefab;
    [Tooltip("탄이 부딪혀 사라지는 지형 Layer입니다.")]
    public LayerMask collideLayer;
    public WeaponBaseSO.DamageInfo damageInfo;
    [Tooltip("탄 속도(칸/초)입니다.")]
    [Min(0f)] public float projectileSpeed = 15f;

    [Header("Burst")]
    [Tooltip("한 번의 사격에서 쏘는 탄 수입니다. 첫 발은 Execute를 시작할 때 쏩니다.")]
    [Min(1)] public int shotCount = 1;
    [Tooltip("두 번째 발부터의 연사 간격(초)입니다.")]
    [Min(0f)] public float shotInterval = 0.15f;
    [Tooltip("켜면 FeedbackPoints의 ExecutePoint에서 쏩니다. 끄거나 비어 있으면 적의 중심에서 쏩니다.")]
    public bool useExecutePoint = true;
    [Tooltip("쏠 때마다 플레이어 반대쪽으로 받는 반동 속도(칸/초)입니다. 외부 힘이라 EnemyController가 감쇠합니다.")]
    [Min(0f)] public float recoilSpeed = 3f;

    [Header("Begin Condition")]
    [Tooltip("플레이어가 이 거리(칸) 안에 있어야 거리 유지에서 사격으로 넘어갑니다.")]
    [Min(0f)] public float maxRange = 11f;
    [Tooltip("사격 각도(도)입니다. 적이 바라보는 방향과 플레이어 방향 사이가 이 값 이하일 때만 넘어가고, 쏠 때 탄(DroneEnemyBullet)의 발사 각도 제한에도 같은 값을 넣습니다. 90이면 바로 아래까지, 180이면 모든 방향으로 쏩니다.")]
    [Range(0f, 180f)] public float maxAimAngle = 120f;
    [Tooltip("켜면 사격선이 지형(Platform)에 막히지 않을 때만 넘어갑니다.")]
    public bool requireLineOfSight = true;
}
