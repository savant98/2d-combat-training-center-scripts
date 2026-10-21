using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

public class EnemyPool : MonoBehaviour
{
    public static EnemyPool Instance;

    private readonly Dictionary<EnemyController, Queue<EnemyController>> pool = new();
    private readonly HashSet<EnemyController> activeEnemies = new();

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
        StartCoroutine(enemy.OnSpawn()); // 커스텀 초기화 (HP 회복 등)
        return enemy;
    }

    public void Despawn(EnemyController enemy)
    {
        if (!activeEnemies.Remove(enemy))
            return;

        enemy.gameObject.SetActive(false);
        enemy.OnDespawn();
        pool[enemy.Poolkey].Enqueue(enemy);
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
