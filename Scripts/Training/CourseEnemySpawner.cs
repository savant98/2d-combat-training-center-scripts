using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 훈련 코스 웨이브에 놓는 적 생성기다. 웨이브의 비필수 월드 오브젝트로 생성되면 정해 둔 자리에 적을 꺼낸다.
// 플레이어가 코스 추격 레이저(ChasingLaserWall)에 맞으면 남은 적을 거두고, 플레이어가 시작 위치로 돌아가는 암전 뒤
// 처음 자리에 다시 꺼낸다. 코스 적에게 맞은 피해로는 다시 꺼내지 않는다(피해 출처는 ChasingLaserWall.IsCourseLaserHit).
// 목표에 도달하면 TrainingCourseStage가 Halt를 불러 모두 거두고 더 꺼내지 않으며, 웨이브가 끝나 회수될 때도 모두 거둔다.
// 꺼낸 적은 웨이브 클리어 조건에 들어가지 않고, 쓰러지면 바로 풀로 돌아간다(일반 웨이브의 비필수 적과 같다).
public sealed class CourseEnemySpawner : WorldEntityBase
{
    [SerializeField] private EnemyController enemyPrefab;
    [SerializeField, Tooltip("생성기 위치에서 적을 꺼낼 자리입니다. 여러 마리 묶음은 자리를 여러 개 둡니다.")]
    private Vector2[] offsets = { Vector2.zero };
    [SerializeField, Min(0f), Tooltip("코스 레이저에 맞은 뒤 다시 꺼내기까지 기다리는 시간(초)입니다. 플레이어가 시작 위치로 돌아가는 암전 시간에 맞춥니다.")]
    private float respawnDelay = 0.3f;
    [SerializeField, Min(0f), Tooltip("0보다 크면 플레이어가 이 거리(칸) 안에 들어올 때 꺼냅니다. 0이면 생성되자마자 꺼냅니다.")]
    private float activationDistance;

    private readonly List<EnemyController> spawned = new List<EnemyController>();
    private PlayerHealth subscribedHealth;
    private Coroutine respawnRoutine;
    private bool waiting;

    public bool IsHalted { get; private set; }
    // 지금까지 적을 꺼낸 횟수(레이저에 맞아 다시 꺼낸 것 포함)
    public int SpawnCount { get; private set; }
    public EnemyController EnemyPrefab => enemyPrefab;
    public int GroupSize => offsets != null ? offsets.Length : 0;
    public IReadOnlyList<EnemyController> Spawned => spawned;
    public int AliveCount
    {
        get
        {
            int count = 0;
            foreach (var enemy in spawned)
                if (enemy != null && enemy.gameObject.activeInHierarchy) count++;
            return count;
        }
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        IsHalted = false;
        SpawnCount = 0;
        respawnRoutine = null;
        DespawnAll();
        Subscribe();
        waiting = true;
        if (activationDistance <= 0f)
            SpawnAll();
    }

    private void Update()
    {
        if (!waiting || IsHalted || activationDistance <= 0f)
            return;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null)
            return;
        Vector2 delta = (Vector2)player.transform.position - (Vector2)transform.position;
        if (delta.sqrMagnitude <= activationDistance * activationDistance)
            SpawnAll();
    }

    private void SpawnAll()
    {
        waiting = false;
        if (enemyPrefab == null || EnemyPool.Instance == null || offsets == null)
            return;

        SpawnCount++;
        foreach (var offset in offsets)
        {
            var enemy = EnemyPool.Instance.Spawn(enemyPrefab, transform.position + (Vector3)offset);
            enemy.Health.IsBossEnemy = false;
            enemy.SetWaveDeathHandler(() => HandleEnemyDeath(enemy));
            spawned.Add(enemy);
        }
    }

    private void HandleEnemyDeath(EnemyController enemy)
    {
        enemy.ClearWaveDeathHandler();
        spawned.Remove(enemy);
        if (EnemyPool.Instance != null)
            EnemyPool.Instance.Despawn(enemy);
    }

    // 코스 레이저에 맞으면 남은 적을 거두고, 암전 뒤 처음 상태로 다시 꺼낸다.
    private void HandlePlayerDamaged(WeaponBaseSO.DamageInfo _)
    {
        if (IsHalted || !ChasingLaserWall.IsCourseLaserHit)
            return;

        DespawnAll();
        waiting = false;
        if (respawnRoutine != null)
            StopCoroutine(respawnRoutine);
        respawnRoutine = StartCoroutine(RespawnAfterDelay());
    }

    private IEnumerator RespawnAfterDelay()
    {
        if (respawnDelay > 0f)
            yield return new WaitForSeconds(respawnDelay);

        respawnRoutine = null;
        waiting = true;
        if (activationDistance <= 0f)
            SpawnAll();
    }

    // 목표에 도달하면 꺼낸 적을 모두 거두고 더 꺼내지 않는다.
    public void Halt()
    {
        IsHalted = true;
        waiting = false;
        if (respawnRoutine != null)
        {
            StopCoroutine(respawnRoutine);
            respawnRoutine = null;
        }
        Unsubscribe();
        DespawnAll();
    }

    private void DespawnAll()
    {
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            var enemy = spawned[i];
            if (enemy == null)
                continue;
            enemy.ClearWaveDeathHandler();
            if (enemy.gameObject.activeSelf && EnemyPool.Instance != null)
                EnemyPool.Instance.Despawn(enemy);
        }
        spawned.Clear();
    }

    private void Subscribe()
    {
        Unsubscribe();
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || player.Health == null)
            return;
        subscribedHealth = player.Health;
        subscribedHealth.OnDamaged += HandlePlayerDamaged;
    }

    private void Unsubscribe()
    {
        if (subscribedHealth != null)
            subscribedHealth.OnDamaged -= HandlePlayerDamaged;
        subscribedHealth = null;
    }

    public override void ForceReturnToPool()
    {
        Halt();
        base.ForceReturnToPool();
    }

    // 풀 전체 정리(WorldEntityPool.DespawnAll)로 바로 꺼질 때도 구독과 꺼낸 적이 남지 않게 한다.
    private void OnDisable()
    {
        Unsubscribe();
        respawnRoutine = null;
        DespawnAll();
    }
}
