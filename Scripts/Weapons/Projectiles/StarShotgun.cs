using System.Collections.Generic;
using UnityEngine;

public class StarShotgun : ProjectileBase
{
    [SerializeField] private FeedbackDataSO emptyHitFeedbackData;
    [SerializeField] private float lifeTime = 0.3f;

    private float currentLifeTime = 0f;

    private bool hasCustomDirection;
    private Vector2 customDirection;
    private readonly HashSet<HealthBase> hitTargets = new HashSet<HealthBase>();
    private readonly List<Collider2D> pathHits = new List<Collider2D>();
    private Vector2 movementStart;

    private Vector2 HitCenter => transform.TransformPoint(col.offset);
    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        hasCustomDirection = false;
        customDirection = Vector2.zero;
        hitTargets.Clear();
    }

    public void SetFireDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        hasCustomDirection = true;
        customDirection = direction.normalized;
    }

    protected override void FixedUpdate()
    {
        if (Time.time > currentLifeTime + lifeTime)
        {
            FeedbackPlayer.Instance.Play(emptyHitFeedbackData, transform.position);
            ReturnToPool();
            return;
        }

        movementStart = HitCenter;
        base.FixedUpdate();
        if (!isReturning)
            CheckMovementHits();
    }



    public override void Initialize(
        CharacterController2D owner,
        WeaponBaseSO.DamageInfo damageInfo,
        float speed,
        int count,
        LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);

        Vector2 dir = hasCustomDirection
            ? customDirection
            : Vector2.right * owner.Facing;

        velocity = dir.normalized * speed;
        currentLifeTime = Time.time;
    }

    protected override void OnHitSomething()
    {
        // 지형에 반환되기 전까지 실제로 이동한 구간의 적도 처리한다.
        CheckMovementHits();
        base.OnHitSomething();
    }

    private void CheckMovementHits()
    {
        if (!(col is CircleCollider2D circle)) return;
        Vector3 scale = transform.lossyScale;
        float radius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        motor.OverlapAttackCirclePath(movementStart, HitCenter, radius, pathHits, ~0);
        foreach (var collision in pathHits)
            TryHit(collision);
    }

    protected override void OnTriggerEnter2D(Collider2D collision)
    {
        TryHit(collision);
    }

    private void TryHit(Collider2D collision)
    {
        HealthBase health = collision.GetComponentInParent<HealthBase>();
        if (health == null || health.IsInvincible || hitTargets.Contains(health))
            return;

        Vector2 hitPoint = collision.ClosestPoint(HitCenter);
        if (ownerFaction == Faction.Player && health is EnemyHealth enemy)
        {
            // 무적·스폰 잠금 중 거절된 접촉은 명중으로 기록하지 않는다.
            if (enemy.enemyShieldSystem != null && enemy.enemyShieldSystem.IsSpawnLocked)
                return;
            hitTargets.Add(health);
            if (enemy.enemyShieldSystem == null)
                enemy.TakeDamage(damageInfo, hitPoint);
            else
                enemy.enemyShieldSystem.TakeDamage(damageInfo, hitPoint);

            OnHitEnemy?.Invoke(owner, hitPoint);
            FeedbackPlayer.Instance.Play(hitFeedbackData, hitPoint);
        }
        else if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
        {
            hitTargets.Add(health);
            player.TakeDamage(damageInfo, hitPoint);
            FeedbackPlayer.Instance.Play(hitFeedbackData, hitPoint);
        }
    }
}