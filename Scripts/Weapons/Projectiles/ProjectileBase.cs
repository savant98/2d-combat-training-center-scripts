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
    private bool initializedForHits;
    private bool trailEmitsOnFlight;

    protected virtual bool UsesPathHitDetection => true;

    protected override void Awake()
    {
        base.Awake();
        trail = GetComponentInChildren<TrailRenderer>(true);
        if (trail != null) trailEmitsOnFlight = trail.emitting;
        var body = col.attachedRigidbody;
        castOrigin = body != null ? body.transform : transform;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        initializedForHits = false;
        if (trail != null)
        {
            trail.emitting = false;
            trail.gameObject.SetActive(false);
            trail.transform.SetParent(transform, false);
            trail.transform.localPosition = Vector3.zero;
            trail.Clear();
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
        initializedForHits = true;
        RaycastMotor2D.InvalidateAttackQuerySync();

        if (trail != null)
        {
            trail.Clear();
            trail.gameObject.SetActive(true);
            trail.emitting = trailEmitsOnFlight;
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

    protected void CheckMovementHits()
    {
        if (!UsesPathHitDetection || movementChecked) return;
        movementChecked = true;
        Vector2 move = (Vector2)transform.position - movementStart;
        if (move.sqrMagnitude > 0.000001f)
        {
            motor.CastAttackPath(castStart, castAngle, move, hitFilter, pathHits);
            foreach (var hit in pathHits)
            {
                // 시작부터 겹친 캐스트에는 새 접점이 없으므로 대상의 현재 표면을 사용한다.
                var targetCollider = hit.collider;
                if (targetCollider == null) continue;
                Vector2 contact = hit.distance > 0f ? hit.point : targetCollider.ClosestPoint(castStart);
                TryHit(targetCollider, contact);
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
        if (trail != null)
        {
            trail.emitting = false;
            trail.transform.SetParent(null, true);
        }
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
        if (!UsesPathHitDetection || !initializedForHits || isReturning || collision == null) return;

        // 지연된 트리거가 재사용된 탄에 도착해도 현재 배치에서 겹치는 대상만 처리한다.
        RaycastMotor2D.PrepareAttackQueries();
        motor.OverlapAttackCollider(castOrigin.position, castOrigin.eulerAngles.z, hitFilter, overlapHits);
        if (overlapHits.Contains(collision))
            TryHit(collision);
    }

    private void TryHit(Collider2D collision, Vector2? hitPoint = null)
    {
        if (!initializedForHits || isReturning || collision == null) return;
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
            var impact = damageInfo;
            bool machineGun = impact.sourceWeapon is MachineGunWeaponSO;
            if (machineGun && enemy.TryGetComponent<EnemyBurn>(out var burn) && burn.Stacks > 0)
            {
                impact.damage *= 1f + burn.Stacks * 0.12f;
                if (WeaponTraitEffects.ApplyEnemyHit(enemy, impact, hitPoint))
                    WeaponTraitFeedbackSO.Play(WeaponTraitCue.MachineGunBurn, hitPoint, -velocity);
            }
            else if (enemy.enemyShieldSystem == null) enemy.TakeDamage(impact, hitPoint);
            else enemy.enemyShieldSystem.TakeDamage(impact, hitPoint);
            // 2026-11-21 사용자 지시: 기관총 장착 중 기본 공격도 화상을 1스택 쌓는다(이번 탄의 추가 피해는 맞기 전 스택으로 계산).
            if (machineGun) EnemyBurn.Apply(enemy);
            OnHitEnemy?.Invoke(owner, hitPoint);
        }
        else if (health is PlayerHealth player)
            player.TakeDamage(damageInfo, hitPoint);

        if (hitFeedbackData != null)
            FeedbackPlayer.Instance.Play(hitFeedbackData, hitPoint);
    }
}
