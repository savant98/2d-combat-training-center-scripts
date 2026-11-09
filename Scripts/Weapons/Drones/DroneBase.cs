using System.Collections.Generic;
using UnityEngine;

public abstract class DroneBase : MovingEntityBase
{
    [Header("Follow Settings")]
    [SerializeField] protected float followDistance = 1.5f;
    [SerializeField] protected float followHeight = 1.0f;

    [Header("Destroy Feedback")]
    [SerializeField] protected FeedbackDataSO destroyFeedback;

    protected Transform player;
    protected float spawnTime;
    protected float lifeTime;

    private readonly List<Collider2D> targetHits = new List<Collider2D>(16);

    public virtual void InitializeDrone(CharacterController2D owner, WeaponBaseSO.DamageInfo damageInfo, float speed, int count, float lifeTime, LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);
        this.player = owner.transform;
        this.lifeTime = lifeTime;
        this.spawnTime = Time.time;
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        if (Time.time - spawnTime > lifeTime)
        {
            FeedbackPlayer.Instance.Play(destroyFeedback, transform.position);
            ForceReturnToPool();
            return;
        }

        DroneTick();
    }

    protected abstract void DroneTick();

    // range 안의 적 가운데 판정 상자 중심이 가장 가까운 것(무적·생성 잠금 제외). 모터의 공격 범위 질의를 쓴다.
    protected Collider2D FindNearestEnemy(float range, LayerMask layer)
    {
        if (motor == null || range <= 0f)
            return null;

        var filter = new ContactFilter2D();
        filter.SetLayerMask(layer);
        filter.useTriggers = true;
        motor.OverlapAttackCircle(transform.position, range, filter, targetHits);

        Collider2D nearest = null;
        float nearestSqrDist = float.MaxValue;

        foreach (var hit in targetHits)
        {
            if (!IsTargetable(hit))
                continue;

            float sqrDist = ((Vector2)hit.bounds.center - (Vector2)transform.position).sqrMagnitude;

            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = hit;
            }
        }

        return nearest;
    }

    protected static bool IsTargetable(Collider2D target)
    {
        if (target == null || !target.enabled || !target.gameObject.activeInHierarchy)
            return false;

        var enemy = target.GetComponentInParent<EnemyHealth>();

        if (enemy == null || enemy.IsInvincible)
            return false;

        return enemy.enemyShieldSystem == null || !enemy.enemyShieldSystem.IsSpawnLocked;
    }
}
