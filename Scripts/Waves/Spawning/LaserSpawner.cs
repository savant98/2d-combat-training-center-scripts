
using UnityEngine;

public class LaserSpawner : ObjectSpawner
{
    [Header("Laser Settings")]
    [SerializeField] private WeaponBaseSO.DamageInfo laserDamageInfo;
    [SerializeField] private Vector2 direction = Vector2.down;
    [SerializeField] private float laserLength = 10f;
    [SerializeField] private float duration = 1f;
    [SerializeField] private float laserWidth = 1f;
    [SerializeField] private LayerMask laserBlockMask;
    [SerializeField] private LayerMask laserDamageMask;

    private LaserBeam laserPrefab;
    protected override void Awake()
    {
        base.Awake();
        laserPrefab = entityBase.GetComponent<LaserBeam>();
        if (laserPrefab == null)
            Debug.LogError($"[LaserSpawner] {gameObject.name}의 entityBase에 LaserBeam 컴포넌트가 없습니다!");
    }
    protected override void SpawnEntity()
    {
        if (laserPrefab == null)
            return;

        LaserBeam laser = WorldEntityPool.Instance.Spawn(laserPrefab, spawnTr.position, spawnTr.rotation);

        laser.Fire(
            laserDamageInfo,
            spawnTr.position,
            direction.normalized,
            laserLength,
            duration,
            laserBlockMask,
            laserDamageMask,
            laserWidth);
    }
}
