using System;
using UnityEngine;

public abstract class ProjectileBase : MovingEntityBase
{
    public Action<CharacterController2D, Vector2> OnHitEnemy;

    [Header("HitFeedback")]
    [SerializeField] protected FeedbackData hitFeedbackData;

    protected TrailRenderer trail;
    protected Transform target;

    protected override void Awake()
    {
        base.Awake();
        trail = GetComponentInChildren<TrailRenderer>();
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        if (trail != null)
        {
            trail.Clear();
            trail.transform.parent = this.transform;
            trail.transform.localPosition = Vector3.zero;

            trail.gameObject.SetActive(false);
        }

        OnHitEnemy = null;
    }
    public override void Initialize(CharacterController2D owner, WeaponBase.DamageInfo damageInfo, float speed, int count, LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);

        if (trail != null)
        {
            trail.Clear();
            trail.gameObject.SetActive(true);
        }
    }
    public void SetTarget(Transform target)
    {
        this.target = target;
    }
    protected override void ReturnToPool()
    {
        if (trail != null)
            trail.transform.parent = null;

        base.ReturnToPool();
    }
    protected override void OnHitSomething()
    {
        FeedbackPlayer.Instance.Play(hitFeedbackData, transform.position);
        ReturnToPool();
    }
    protected virtual void OnTriggerEnter2D(Collider2D collision)
    {
        HealthBase health = collision.GetComponentInParent<HealthBase>();
        
        if (health == null) 
            return;
        //if (!collision.TryGetComponent<HealthBase>(out var health))
        //    return;

        if (health.IsInvincible)
            return;

        if (ownerFaction == Faction.Player && health is EnemyHealth enemy)
        {
            if (enemy.enemyShieldSystem == null)
                enemy.TakeDamage(damageInfo, transform.position);
            else
                enemy.enemyShieldSystem.TakeDamage(damageInfo, transform.position);

            Vector2 hitPoint = collision.ClosestPoint(transform.position);

            OnHitEnemy?.Invoke(owner, hitPoint);
            FeedbackPlayer.Instance.Play(hitFeedbackData, hitPoint);
            ReturnToPool();
        }
        else if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
        {
            player.TakeDamage(damageInfo, transform.position);
            FeedbackPlayer.Instance.Play(hitFeedbackData, transform.position);
            ReturnToPool();
        }
    }
}
