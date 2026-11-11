
using System.Collections.Generic;
using UnityEngine;

public class LaserSpawner : ObjectSpawner
{
    [Header("Laser Settings")]
    [SerializeField] private WeaponBaseSO.DamageInfo laserDamageInfo;
    [SerializeField] private Vector2 direction = Vector2.down;
    [SerializeField, Tooltip("회전된 환경 도구의 로컬 방향으로 발사합니다. 기존 Tutorial은 월드 방향을 유지합니다.")]
    private bool useLocalDirection;
    [SerializeField, Tooltip("환경 도구가 제거될 때 자신이 생성한 레이저도 회수합니다.")]
    private bool cleanupSpawnedBeams;
    [SerializeField] private float laserLength = 10f;
    [SerializeField] private float duration = 1f;
    [SerializeField] private float laserWidth = 1f;
    [SerializeField] private LayerMask laserBlockMask;
    [SerializeField] private LayerMask laserDamageMask;

    private LaserBeam laserPrefab;
    private WaveEnvironmentLifecycle lifecycle;
    private readonly List<(LaserBeam beam, uint version)> spawnedBeams = new();
    protected override void Awake()
    {
        base.Awake();
        lifecycle = GetComponent<WaveEnvironmentLifecycle>();
        laserPrefab = entityBase.GetComponent<LaserBeam>();
        if (laserPrefab == null)
            Debug.LogError($"[LaserSpawner] {gameObject.name}의 entityBase에 LaserBeam 컴포넌트가 없습니다!");
    }
    public override void OnSpawnedFromPool()
    {
        if (lifecycle == null) { base.OnSpawnedFromPool(); return; }
        isReturning = false;
        StopAllCoroutines();
        CleanupSpawnedBeams();
        // 등장 완료 뒤 발사 주기를 시작하여 준비 중에는 레이저를 만들지 않는다.
        lifecycle.BeginSpawn(BeginSpawnCycle);
    }

    private void BeginSpawnCycle() => base.OnSpawnedFromPool();

    protected override void Update()
    {
        if (lifecycle != null && !lifecycle.IsReady) return;
        base.Update();
    }

    protected override void SpawnEntity()
    {
        if (lifecycle != null && !lifecycle.IsReady) return;
        if (laserPrefab == null)
            return;

        LaserBeam laser = WorldEntityPool.Instance.Spawn(laserPrefab, spawnTr.position, spawnTr.rotation);

        if (cleanupSpawnedBeams)
        {
            spawnedBeams.RemoveAll(item => item.beam == null || !item.beam.gameObject.activeInHierarchy || item.beam.SpawnVersion != item.version);
            spawnedBeams.Add((laser, laser.SpawnVersion));
        }

        Vector2 fireDirection = useLocalDirection ? (Vector2)transform.TransformDirection(direction) : direction;
        laser.Fire(
            laserDamageInfo,
            spawnTr.position,
            fireDirection.normalized,
            laserLength,
            duration,
            laserBlockMask,
            laserDamageMask,
            laserWidth);
    }
    public override void ForceReturnToPool()
    {
        if (isReturning) return;
        CleanupSpawnedBeams();
        if (lifecycle != null) lifecycle.BeginDespawn(ReturnAfterDespawn);
        else base.ForceReturnToPool();
    }

    private void ReturnAfterDespawn() => base.ForceReturnToPool();

    private void OnDisable() => CleanupSpawnedBeams();

    private void CleanupSpawnedBeams()
    {
        // 반환된 풀 개체가 다른 발사에 재대여됐다면 건드리지 않습니다.
        foreach (var item in spawnedBeams)
            if (item.beam != null && item.beam.gameObject.activeInHierarchy && item.beam.SpawnVersion == item.version)
                item.beam.ForceReturnToPool();
        spawnedBeams.Clear();
    }
}
