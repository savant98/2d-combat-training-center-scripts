using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

public class EnemyPool : MonoBehaviour
{
    public static EnemyPool Instance;

    private readonly Dictionary<EnemyController, Queue<EnemyController>> pool = new();
    private readonly HashSet<EnemyController> activeEnemies = new();
    private readonly Dictionary<EnemyController, Coroutine> spawnRoutines = new();

    private void Awake()
    {
        Instance = this;
    }
    public EnemyController Spawn(EnemyController prefab, Vector3 pos)
    {
        // Enemy Spawn
        if (!pool.ContainsKey(prefab))
            pool[prefab] = new Queue<EnemyController>();

        EnemyController enemy;

        if (pool[prefab].Count > 0)
        {
            enemy = pool[prefab].Dequeue();
            CancelSpawn(enemy);
            enemy.gameObject.SetActive(true);
        }
        else
        {
            enemy = Instantiate(prefab);
            enemy.Poolkey = prefab;
        }

        activeEnemies.Add(enemy);
        enemy.Velocity = Vector2.zero;
        enemy.externalForce = Vector2.zero;
        enemy.transform.position = pos;
        spawnRoutines[enemy] = StartCoroutine(SpawnRoutine(enemy)); // 커스텀 초기화 (HP 회복 등)
        return enemy;
    }

    public void Despawn(EnemyController enemy)
    {
        if (!activeEnemies.Remove(enemy))
            return;

        // 생성 코루틴은 Pool 소유이므로 표적의 비활성화만으로 중단되지 않는다.
        CancelSpawn(enemy);
        enemy.gameObject.SetActive(false);
        enemy.OnDespawn();
        pool[enemy.Poolkey].Enqueue(enemy);
    }

    private IEnumerator SpawnRoutine(EnemyController enemy)
    {
        // 기존 StartCoroutine(OnSpawn)처럼 반환 전에 첫 단계(무적·생성 잠금)를 실행한다.
        var sequence = enemy.OnSpawn();
        try
        {
            while (sequence.MoveNext())
                yield return sequence.Current;
        }
        finally
        {
            (sequence as System.IDisposable)?.Dispose();
            spawnRoutines.Remove(enemy);
        }
    }

    private void CancelSpawn(EnemyController enemy)
    {
        if (!spawnRoutines.TryGetValue(enemy, out var routine)) return;
        spawnRoutines.Remove(enemy);
        if (routine != null) StopCoroutine(routine);
    }

    public void DespawnAll()
    {
        var list = activeEnemies.ToArray();

        foreach (var enemy in list)
        {
            Despawn(enemy);
        }

        activeEnemies.Clear();
    }
    public void FreezeAll()
    {
        foreach (var enemy in activeEnemies)
        {
            enemy.EnterFreeze();
        }
    }
}
