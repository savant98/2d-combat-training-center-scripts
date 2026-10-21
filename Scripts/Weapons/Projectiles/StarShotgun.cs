using System.Collections.Generic;
using UnityEngine;

public class StarShotgun : ProjectileBase
{
    [SerializeField] private FeedbackDataSO emptyHitFeedbackData;
    [SerializeField] private float lifeTime = 0.3f;

    private float currentLifeTime = 0f;

    private bool hasCustomDirection;
    private Vector2 customDirection;
    private HashSet<HealthBase> hitTargets = new HashSet<HealthBase>();
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

        base.FixedUpdate();
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

    protected override void OnTriggerEnter2D(Collider2D collision)
    {
        //if (!collision.TryGetComponent<HealthBase>(out var health))
        //    return;

        HealthBase health = collision.GetComponentInParent<HealthBase>();

        if (health == null || !hitTargets.Add(health)) 
            return;

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
            //ReturnToPool();
        }
        else if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
        {
            player.TakeDamage(damageInfo, transform.position);
            FeedbackPlayer.Instance.Play(hitFeedbackData, transform.position);
            //ReturnToPool();
        }
    }
}