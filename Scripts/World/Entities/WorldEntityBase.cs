using UnityEngine;

public abstract class WorldEntityBase : MonoBehaviour
{
    [HideInInspector] public WorldEntityBase poolPrefab;

    [Header("LaunchFeedbackData")]
    [SerializeField] protected FeedbackDataSO launchFeedbackData;

    protected CharacterController2D owner;
    protected Faction ownerFaction;
    protected WeaponBaseSO.DamageInfo damageInfo;
    protected Collider2D col;
    protected int spawnCount;
    protected bool isReturning = false;

    protected virtual void Awake()
    {
        col = GetComponent<Collider2D>();
    }
    public virtual void OnSpawnedFromPool()
    {
        isReturning = false;
        StopAllCoroutines();
    }
    public virtual void Initialize(CharacterController2D owner, WeaponBaseSO.DamageInfo damageInfo, int count = 0)
    {
        this.owner = owner;
        this.ownerFaction = owner != null ? owner.Faction : Faction.Enemy;
        this.damageInfo = damageInfo;
        this.spawnCount = count;
        
        // 방향에 맞게 반전
        if (launchFeedbackData == null)
            return;

        Vector3 offset = launchFeedbackData.offset;

        if (owner != null)
        {
            if (owner.Facing < 0)
                offset.x *= -1f;
        }

        Vector3 pos = transform.position + offset;
        FeedbackPlayer.Instance.PlayExact(launchFeedbackData, pos, transform.rotation);

        transform.position += offset;
    }
    protected virtual void ReturnToPool()
    {
        if (isReturning) return;
        isReturning = true;

        WorldEntityPool.Instance.Return(poolPrefab, this);
    }
    public virtual void ForceReturnToPool()
    {
        StopAllCoroutines();
        isReturning = false;
        ReturnToPool();
    }
}
