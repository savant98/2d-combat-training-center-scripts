using UnityEngine;
using System.Collections;

// Player 사격 드론(2026-11-09 재디자인, 사용자 요청 "player의 다른 무장을 참고해 새롭게, 총알은 StarShot의 것을 사용").
// - 조준: 가장 가까운 적의 판정 상자 중심을 향해 포신(aimPivot)이 돈다. 적이 없으면 Player가 보는 쪽을 겨눈다.
// - 사격: attackInterval마다 interval 간격으로 count발을 쏜다. 탄은 StarShot 탄(직선 비행)이며, 쏘는 순간 포구에서 목표까지의 직선 방향으로 날아간다.
// - 동작: 사격을 시작할 때 Animator의 Fire를 켜고, Fire Clip이 interval에 맞춰 포신 반동·포구 별 맥동을 보여 준다.
public class AutoAttackDrone : DroneBase
{
    [Header("Attack")]
    [SerializeField] private float attackRange = 6f;
    [SerializeField] private float attackInterval = 1.2f;
    [SerializeField] private LayerMask targetLayer;
    [SerializeField] private Transform attackTr;

    [Header("Projectile Setting")]
    [SerializeField] private StarShot projectilePrefab;
    [SerializeField] private float projectileSpeed = 8f;
    [SerializeField] private int count = 3; // 발사 횟수
    [SerializeField] private float interval = 0.1f; // 발사 간격

    [Header("Presentation")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform aimPivot;
    [SerializeField, Min(0f)] private float aimTurnSpeed = 720f;
    [SerializeField, Min(0.01f)] private float retargetInterval = 0.1f;

    private static readonly int FireHash = Animator.StringToHash("Fire");

    private Collider2D target;
    private float lastAttackTime;
    private float retargetTimer;
    private float aimAngle;

    public override void InitializeDrone(CharacterController2D owner, WeaponBaseSO.DamageInfo damageInfo, float speed, int count, float lifeTime, LayerMask collideLayer)
    {
        base.InitializeDrone(owner, damageInfo, speed, count, lifeTime, collideLayer);

        aimAngle = RestAngle();
        ApplyAim();
    }

    protected override void DroneTick()
    {
        FollowPlayer();
        UpdateAim();
        TryAttack();
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        lastAttackTime = Time.time;
        target = null;
        retargetTimer = 0f;
    }

    private void FollowPlayer()
    {
        Vector2 desiredPos = (Vector2)player.position + new Vector2(-owner.Facing * followDistance, followHeight);

        velocity = (desiredPos - (Vector2)transform.position) * speed;
    }

    private void UpdateAim()
    {
        retargetTimer -= Time.fixedDeltaTime;

        if (retargetTimer <= 0f || !IsTargetable(target))
        {
            retargetTimer = retargetInterval;
            target = FindNearestEnemy(attackRange, targetLayer);
        }

        float desired = RestAngle();

        if (target != null && aimPivot != null)
        {
            Vector2 delta = (Vector2)target.bounds.center - (Vector2)aimPivot.position;

            if (delta.sqrMagnitude > 0.0001f)
                desired = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        }

        aimAngle = Mathf.MoveTowardsAngle(aimAngle, desired, aimTurnSpeed * Time.fixedDeltaTime);
        ApplyAim();
    }

    private void TryAttack()
    {
        if (Time.time < lastAttackTime + attackInterval)
            return;

        if (target == null)
            return;

        lastAttackTime = Time.time;

        if (animator != null)
            animator.SetTrigger(FireHash);

        StartCoroutine(LaunchProjectile(owner));
    }

    private IEnumerator LaunchProjectile(CharacterController2D controller)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = FireDirection();
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            var projectile = WorldEntityPool.Instance.Spawn(projectilePrefab, attackTr.position, Quaternion.Euler(0f, 0f, angle));
            projectile.SetFireDirection(direction);
            projectile.Initialize(controller, damageInfo, projectileSpeed, count, collideLayer);

            yield return new WaitForSeconds(interval);
        }
    }

    // 포구에서 목표 판정 상자 중심까지의 방향. 목표가 사라지면 지금 겨눈 쪽으로 쏜다.
    private Vector2 FireDirection()
    {
        if (IsTargetable(target))
        {
            Vector2 delta = (Vector2)target.bounds.center - (Vector2)attackTr.position;

            if (delta.sqrMagnitude > 0.0001f)
                return delta.normalized;
        }

        float radians = aimAngle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }

    private float RestAngle()
    {
        return owner != null && owner.Facing < 0 ? 180f : 0f;
    }

    private void ApplyAim()
    {
        if (aimPivot != null)
            aimPivot.rotation = Quaternion.Euler(0f, 0f, aimAngle);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
#endif
}
