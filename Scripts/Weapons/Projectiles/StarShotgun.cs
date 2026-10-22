using System.Collections.Generic;
using UnityEngine;

public class StarShotgun : ProjectileBase
{
    [SerializeField] private FeedbackDataSO emptyHitFeedbackData;
    [SerializeField] private float lifeTime = 0.3f;

    private float currentLifeTime = 0f;

    private bool hasCustomDirection;
    private Vector2 customDirection;
    private readonly HashSet<HealthBase> hitTargets = new HashSet<HealthBase>(8);
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

    protected override void HitTarget(HealthBase health, Vector2 hitPoint)
    {
        // 관통 탄 하나당 대상별 피해는 한 번만 허용한다.
        if (hitTargets.Add(health))
            ApplyHitDamage(health, hitPoint);
    }
}