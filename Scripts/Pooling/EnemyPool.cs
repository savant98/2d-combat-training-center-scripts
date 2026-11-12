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

    // 미리 만들어 풀에 넣어 둔다(TrainingRoom 입장 로딩). 처음 등장하는 순간에 Prefab을 새로 만드느라 화면이 끊기지 않게 한다.
    // 풀에 쉬고 있는 수가 count가 될 때까지 한 번에 perCall개씩 만들고, 만든 수를 돌려준다.
    public int Prewarm(EnemyController prefab, int count, int perCall = 1)
    {
        if (prefab == null) return 0;
        if (!pool.ContainsKey(prefab))
            pool[prefab] = new Queue<EnemyController>();

        int made = 0;
        while (pool[prefab].Count < count && made < perCall)
        {
            EnemyController enemy = Instantiate(prefab);
            enemy.Poolkey = prefab;
            enemy.gameObject.SetActive(false);
            pool[prefab].Enqueue(enemy);
            made++;
        }
        return made;
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
