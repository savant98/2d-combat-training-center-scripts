using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeleeHitbox : WorldEntityBase
{
    [Header("Lifetime")]
    [SerializeField] private bool autoReturnToPool = false;
    [SerializeField] private float lifeTime = 1.5f;

    private FeedbackData hitFeedbackData;
    private Action<CharacterController2D, Vector2> OnHitEnemy;

    private readonly HashSet<HealthBase> hitSet = new();
    private Coroutine lifeRoutine;

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        hitSet.Clear();
        OnHitEnemy = null;

        StopLifeRoutine();
    }
    /// <summary>
    /// 공격 시작 시마다 주입
    /// </summary>
    public override void Initialize(CharacterController2D owner, WeaponBase.DamageInfo damageInfo, int count = 0)
    {
        base.Initialize(owner, damageInfo, count);

        if (autoReturnToPool)
            StartLifeRoutine();
    }

    public void SetHitFeedback(FeedbackData hitFeedbackData, Action<CharacterController2D, Vector2> OnHitEnemy = null)
    {
        this.hitFeedbackData = hitFeedbackData;
        this.OnHitEnemy = OnHitEnemy;
    }
    // Animation Event
    public void EnableHitbox()
    {
        hitSet.Clear();

        if (col == null)
            return;

        col.enabled = true;
    }

    // Animation Event
    public void DisableHitbox()
    {
        if (col == null)
            return;

        col.enabled = false;
    }
    private void StartLifeRoutine()
    {
        StopLifeRoutine();
        lifeRoutine = StartCoroutine(CoReturnAfterLifeTime());
    }

    private void StopLifeRoutine()
    {
        if (lifeRoutine != null)
        {
            StopCoroutine(lifeRoutine);
            lifeRoutine = null;
        }
    }

    private IEnumerator CoReturnAfterLifeTime()
    {
        yield return new WaitForSeconds(lifeTime);
        ForceReturnToPool();
        lifeRoutine = null;
    }
    public override void ForceReturnToPool()
    {
        StopLifeRoutine();
        base.ForceReturnToPool();
    }
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!col.enabled)
            return;

        //if (!collision.TryGetComponent<HealthBase>(out var health))
        //    return;
        HealthBase health = collision.GetComponentInParent<HealthBase>();
        if (health == null)
            return;

        if (!hitSet.Add(health))
            return;

        // 맞은 지점
        Vector2 hitPoint = collision.ClosestPoint(col.bounds.center);
        
        if (ownerFaction == Faction.Player && health is EnemyHealth enemy)
        {
            if (enemy.enemyShieldSystem == null)
                enemy.TakeDamage(damageInfo, hitPoint);
            else
                enemy.enemyShieldSystem.TakeDamage(damageInfo, hitPoint);

            OnHitEnemy?.Invoke(owner, hitPoint);
            FeedbackPlayer.Instance.Play(hitFeedbackData, hitPoint);
        }
        else if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
        {
            player.TakeDamage(damageInfo, transform.position);
            FeedbackPlayer.Instance.Play(hitFeedbackData, hitPoint);
        }
    }
}

