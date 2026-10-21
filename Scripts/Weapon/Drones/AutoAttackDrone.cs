using UnityEngine;
using System.Collections;

public class AutoAttackDrone : DroneBase
{
    [Header("Attack")]
    [SerializeField] private float attackRange = 6f;
    [SerializeField] private float attackInterval = 1.2f;
    [SerializeField] private LayerMask targetLayer;
    [SerializeField] private Transform attackTr;

    [Header("Projectile Setting")]
    [SerializeField] private ProjectileBase projectilePrefab;
    [SerializeField] private float projectileSpeed = 8f;
    [SerializeField] private int count = 3; // 발사 횟수
    [SerializeField] private float interval = 0.1f; // 연사 간격

    private float lastAttackTime;
    protected override void Awake()
    {
        base.Awake();
    }
    protected override void DroneTick()
    {
        FollowPlayer();
        TryAttack();
    }
    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        lastAttackTime = Time.time;
    }
    private void FollowPlayer()
    {
        Vector2 desiredPos = (Vector2)player.position + new Vector2(-owner.Facing * followDistance, followHeight);

        velocity = (desiredPos - (Vector2)transform.position) * speed;
    }

    private void TryAttack()
    {
        if (Time.time < lastAttackTime + attackInterval)
            return;

        Collider2D target = Physics2D.OverlapCircle(transform.position, attackRange, targetLayer);

        if (target == null)
            return;

        lastAttackTime = Time.time;
        StartCoroutine(LaunchProjectile(owner, target.transform));
    }
    private IEnumerator LaunchProjectile(CharacterController2D controller, Transform targetTr)
    {
        for (int i = 0; i < count; i++)
        {
            var projectile = (ProjectileBase)WorldEntityPool.Instance.Spawn(projectilePrefab, attackTr.position, transform.rotation);
            projectile.SetTarget(targetTr);
            projectile.Initialize(controller, damageInfo, projectileSpeed, count, collideLayer);

            yield return new WaitForSeconds(interval);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
#endif
}
