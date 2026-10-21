using UnityEngine;

public abstract class ObjectSpawner : WorldEntityBase
{
    [Header("Block Settings")]
    [SerializeField] private Animator animator;
    [SerializeField] private string animTriggerName;

    [Header("Spawn Settings")]
    public WorldEntityBase entityBase;
    public Faction entityFaction = Faction.Enemy;
    public Transform spawnTr;
    public float creationCycle = 1.5f;
    public bool spawnOnce = false;
    
    private bool spawning = false;
    private float currentTime;

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        spawning = true;
        currentTime = Time.time;
    }

    protected virtual void Update()
    {
        if (!spawning)
            return;

        if (Time.time - currentTime >= creationCycle)
        {
            SpawnEntity();
            currentTime = Time.time;

            if (spawnOnce)
                spawning = false;
        }
    }

    protected virtual void SpawnEntity()
    {
        WorldEntityBase worldEntity = WorldEntityPool.Instance.Spawn(entityBase, spawnTr.position, spawnTr.rotation);
    }

    public override void ForceReturnToPool()
    {
        spawning = false;
        if (animator != null)
            animator.SetTrigger(animTriggerName);
        else
            ForceReturnTrigger();
    }

    public void ForceReturnTrigger()
    {
        StopAllCoroutines();
        isReturning = false;
        ReturnToPool();
    }
}
