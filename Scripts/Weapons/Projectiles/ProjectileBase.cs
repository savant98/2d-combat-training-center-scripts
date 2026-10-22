using System;
using System.Collections.Generic;
using UnityEngine;

// 캐릭터의 기본 FixedUpdate 이동을 마친 뒤 공격 대상을 한 번 동기화한다.
[DefaultExecutionOrder(100)]
public abstract class ProjectileBase : MovingEntityBase
{
    public Action<CharacterController2D, Vector2> OnHitEnemy;

    [Header("HitFeedback")]
    [SerializeField] protected FeedbackDataSO hitFeedbackData;

    protected TrailRenderer trail;
    protected Transform target;

    private static int playerHitLayer;
    private static int enemyHitLayer;
    private readonly List<RaycastHit2D> pathHits = new List<RaycastHit2D>(8);
    private readonly List<Collider2D> overlapHits = new List<Collider2D>(8);
    private ContactFilter2D hitFilter;
    private Transform castOrigin;
    private Vector2 movementStart;
    private Vector2 castStart;
    private float castAngle;
    private bool movementChecked;

    protected virtual bool UsesPathHitDetection => true;

    protected override void Awake()
    {
        base.Awake();
        trail = GetComponentInChildren<TrailRenderer>();
        var body = col.attachedRigidbody;
        castOrigin = body != null ? body.transform : transform;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        if (trail != null)
        {
            trail.Clear();
            trail.transform.parent = transform;
            trail.transform.localPosition = Vector3.zero;
            trail.gameObject.SetActive(false);
        }
        OnHitEnemy = null;
    }

    public override void Initialize(CharacterController2D owner, WeaponBaseSO.DamageInfo damageInfo,
        float speed, int count, LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);
        if (playerHitLayer == 0)
        {
            playerHitLayer = 1 << LayerMask.NameToLayer("Player");
            enemyHitLayer = 1 << LayerMask.NameToLayer("Enemy");
        }
        hitFilter = new ContactFilter2D();
        hitFilter.SetLayerMask(ownerFaction == Faction.Player ? enemyHitLayer : playerHitLayer);
        hitFilter.useTriggers = true;
        RaycastMotor2D.InvalidateAttackQuerySync();

        if (trail != null)
        {
            trail.Clear();
            trail.gameObject.SetActive(true);
        }
    }

    protected override void FixedUpdate()
    {
        if (!UsesPathHitDetection)
        {
            base.FixedUpdate();
            return;
        }

        RaycastMotor2D.PrepareAttackQueries();
        movementStart = transform.position;
        castStart = castOrigin.position;
        castAngle = castOrigin.eulerAngles.z;
        movementChecked = false;
        base.FixedUpdate();
        if (!isReturning && !movementChecked)
            CheckMovementHits();
    }

    private void CheckMovementHits()
    {
        if (!UsesPathHitDetection || movementChecked) return;
        movementChecked = true;
        Vector2 move = (Vector2)transform.position - movementStart;
        if (move.sqrMagnitude > 0.000001f)
        {
            motor.CastAttackPath(castStart, castAngle, move, hitFilter, pathHits);
            foreach (var hit in pathHits)
            {
                TryHit(hit.collider, hit.point);
                if (isReturning) break;
            }
        }
        else
        {
            motor.OverlapAttackCollider(castStart, castAngle, hitFilter, overlapHits);
            foreach (var hit in overlapHits)
            {
                TryHit(hit);
                if (isReturning) break;
            }
        }
    }

    public void SetTarget(Transform target)
    {
        this.target = target;
    }

    protected override void ReturnToPool()
    {
        if (trail != null) trail.transform.parent = null;
        base.ReturnToPool();
    }

    protected override void OnHitSomething()
    {
        // 벽에 반환되기 전 실제로 이동한 구간의 대상을 먼저 처리한다.
        CheckMovementHits();
        if (isReturning) return;
        FeedbackPlayer.Instance.Play(hitFeedbackData, transform.position);
        ReturnToPool();
    }

    protected virtual void OnTriggerEnter2D(Collider2D collision)
    {
        if (UsesPathHitDetection)
            TryHit(collision);
    }

    private void TryHit(Collider2D collision, Vector2? hitPoint = null)
    {
        if (isReturning || collision == null) return;
        if ((hitFilter.layerMask & (1 << collision.gameObject.layer)) == 0) return;
        HealthBase health = collision.GetComponentInParent<HealthBase>();
        if (health == null || health.IsInvincible) return;

        if (ownerFaction == Faction.Player && health is EnemyHealth enemy)
        {
            if (enemy.enemyShieldSystem != null && enemy.enemyShieldSystem.IsSpawnLocked) return;
        }
        else if (!(ownerFaction == Faction.Enemy && health is PlayerHealth))
            return;

        HitTarget(health, hitPoint ?? collision.ClosestPoint(transform.position));
    }

    protected virtual void HitTarget(HealthBase health, Vector2 hitPoint)
    {
        ApplyHitDamage(health, hitPoint);
        ReturnToPool();
    }

    protected void ApplyHitDamage(HealthBase health, Vector2 hitPoint)
    {
        if (health is EnemyHealth enemy)
        {
            if (enemy.enemyShieldSystem == null) enemy.TakeDamage(damageInfo, hitPoint);
            else enemy.enemyShieldSystem.TakeDamage(damageInfo, hitPoint);
            OnHitEnemy?.Invoke(owner, hitPoint);
        }
        else if (health is PlayerHealth player)
            player.TakeDamage(damageInfo, hitPoint);

        if (hitFeedbackData != null)
            FeedbackPlayer.Instance.Play(hitFeedbackData, hitPoint);
    }
}
