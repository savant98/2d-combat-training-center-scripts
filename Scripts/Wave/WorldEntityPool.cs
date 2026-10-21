using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class WorldEntityPool : MonoBehaviour
{
    public static WorldEntityPool Instance { get; private set; }

    [SerializeField] private int poolPerWorldEntity = 10;

    private Dictionary<WorldEntityBase, Queue<WorldEntityBase>> pools = new();
    private HashSet<WorldEntityBase> activeWorldEntities = new();


    private Transform poolRoot;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        poolRoot = new GameObject("WorldEntityPoolRoot").transform;
        poolRoot.SetParent(transform);
    }

    // ───────────────────────────────────────
    // GET from Pool
    // ───────────────────────────────────────
    public WorldEntityBase Spawn(WorldEntityBase prefab, Vector3 pos, Quaternion rot)
    {
        if (!pools.ContainsKey(prefab))
            pools[prefab] = new Queue<WorldEntityBase>();

        var pool = pools[prefab];

        while (pool.Count > 0 && pool.Peek() == null)
            pool.Dequeue();

        if (pool.Count == 0)
            CreateObjects(prefab);

        var worldEntity = pool.Dequeue();
        worldEntity.gameObject.SetActive(true);
        worldEntity.transform.SetPositionAndRotation(pos, rot);
        worldEntity.OnSpawnedFromPool();

        activeWorldEntities.Add(worldEntity);
        return worldEntity;
    }
    public T Spawn<T>(T prefab, Vector3 pos, Quaternion rot) where T : WorldEntityBase
    {
        return (T)Spawn((WorldEntityBase)prefab, pos, rot);
    }

    // ───────────────────────────────────────
    // RETURN to Pool
    // ───────────────────────────────────────
    public void Return(WorldEntityBase prefab, WorldEntityBase instance)
    {
        if (instance == null)
            return;

        // 이미 반환된 객체면 중복 enqueue 방지
        if (!activeWorldEntities.Remove(instance))
        {
            Debug.LogWarning($"[WorldEntityPool] Duplicate Return ignored: {instance.name}", instance);
            return;
        }

        instance.gameObject.SetActive(false);

        if (!pools.ContainsKey(prefab))
            pools[prefab] = new Queue<WorldEntityBase>();

        pools[prefab].Enqueue(instance);
    }
    public void DespawnAll()
    {
        foreach (WorldEntityBase projectile in activeWorldEntities.ToArray())
        {
            if (projectile != null)
                projectile.ForceReturnToPool();
        }

        //activeWorldEntities.Clear();
    }

    // ───────────────────────────────────────
    // Pool 생성
    // ───────────────────────────────────────
    private void CreateObjects(WorldEntityBase prefab)
    {
        Debug.Log($"[WorldEntityPool] Pool expanded for {prefab.name}");

        for (int i = 0; i < poolPerWorldEntity; i++)
        {
            var obj = Instantiate(prefab, poolRoot);
            obj.gameObject.SetActive(false);
            obj.poolPrefab = prefab; // prefab 원본 저장
            pools[prefab].Enqueue(obj);
        }
    }
}
