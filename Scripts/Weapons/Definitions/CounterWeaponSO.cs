using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

[CreateAssetMenu(menuName = "Weapon/Counter")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "CounterWeapon")]
public class CounterWeaponSO : WeaponBaseSO
{
    [SerializeField] private MeleeHitbox counterHitbox;
    [SerializeField] private DamageInfo counterDamageInfo;
    [SerializeField] private FeedbackDataSO counterHitFeedback;   // 충격파에 맞은 적 위치의 효과

    [SerializeField] private float counterWindowTime = 0.5f;
    [SerializeField] private float counterHitboxDelay = 0.1f;
    // 반격 성공 순간부터의 무적 시간(초). 회수 동작(recoverTime)이 끝난 뒤에도 이어져 그 차이만큼 판정 밖으로 빠져나갈 수 있다.
    [SerializeField] private float successInvincibleTime = 1f;
    [SerializeField] private CounterShield shieldPrefab;

    [Header("투사체 반사(Hammer Counter Reflect)")]
    [SerializeField] private CounterReflectShot reflectShotPrefab;
    [SerializeField] private DamageInfo reflectDamageInfo = new DamageInfo { damage = 150f, damageType = DamageType.Skill };
    [Tooltip("반사 전용 투사체 속도(칸/초). 레일건 별창(StarSpear 150)과 동일.")]
    [SerializeField, Min(1f)] private float reflectProjectileSpeed = 150f;
    [Tooltip("반격 대기 창에서 적 투사체를 감지해 반격을 발동시키는 반경(칸).")]
    [SerializeField, Min(0.5f)] private float reflectDetectRadius = 1.85f;
    [Tooltip("반격 성공·충격파 발동 시 범위 안 적 투사체를 함께 소멸·반사하는 반경(칸). 충격파 판정 반경과 일치.")]
    [SerializeField, Min(0.5f)] private float reflectSweepRadius = 4f;
    [Tooltip("반사 전용 투사체 최대 비행 거리(칸).")]
    [SerializeField, Min(5f)] private float reflectMaxFlightDistance = 45f;
    [Tooltip("여러 투사체를 동시에 반사할 때 순차 사출 간격(초).")]
    [SerializeField, Min(0f)] private float reflectStaggerInterval = 0.05f;
    [Tooltip("한 번의 반격에서 생성하는 반사 투사체 상한.")]
    [SerializeField, Min(1)] private int reflectMaxShots = 6;
    [SerializeField] private LayerMask reflectPlatformLayer;

    private bool counterSucceeded;
    private readonly List<Collider2D> projectileHits = new List<Collider2D>(16);
    private readonly HashSet<ProjectileBase> capturedProjectiles = new HashSet<ProjectileBase>();

    public CounterReflectShot ReflectShotPrefab => reflectShotPrefab;
    public float ReflectProjectileSpeed => reflectProjectileSpeed;
    public float ReflectDetectRadius => reflectDetectRadius;
    public float ReflectSweepRadius => reflectSweepRadius;

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        counterSucceeded = false;

        PlayerController playerController = controller as PlayerController;

        if (playerController == null)
            yield break;

        PlayerHealth health = playerController.Health;
        PlayerCounterState counter = playerController.PlayerCounterState;

        if (health == null || counter == null)
            yield break;

        CounterShield shield = SpawnShield(controller, counter);
        capturedProjectiles.Clear();
        int spawnedReflectCount = 0;

        counter.StartCounter((hitPosition) =>
        {
            if (!IsStillAttackState(playerController))
            {
                counter.EndCounter();
                return;
            }

            counterSucceeded = true;
            // 반격 성공: 지금 든 무장과 관계없이 해머 방어막을 남은 양·충전량과 상관없이 100%로 채운다(2026-11-21 사용자 지시).
            playerController.AbilityManager?.AttackAbility?.Traits?.FillBarrier();
            // 이 순간부터 무적 시간을 잰다. 끝내는 일은 PlayerCounterState가 맡으므로 공격 상태가 먼저 끝나도 이어진다.
            counter.BeginInvulnerability(health, successInvincibleTime);
            if (shield != null) shield.Parry(hitPosition, counterHitboxDelay);

            // 반격 성공 순간: 타격한 투사체 및 범위 내 적 투사체를 소멸시키고, 충격파 밖의 발사 적에게 반사 투사체를 예약 사출한다.
            spawnedReflectCount += ReflectProjectilesInRange(
                playerController,
                reflectSweepRadius,
                counterHitboxDelay,
                spawnedReflectCount,
                PlayerCounterState.CurrentImpactProjectile);

            forceRecover?.Invoke();
        });

        float timer = 0f;

        while (timer < counterWindowTime && !counterSucceeded)
        {
            if (!IsStillAttackState(playerController))
            {
                counter.EndCounter();
                if (shield != null) shield.Expire();
                yield break;
            }

            // 쉴드·반격 범위 안에 적 투사체가 있으면 직접 몸에 닿기 전이라도 반격을 발동한다(모터 공격 범위 질의 재사용).
            ProjectileBase incoming = FindProjectileInRange(playerController, reflectDetectRadius);
            if (incoming != null)
            {
                PlayerCounterState.PushImpactProjectile(incoming);
                try
                {
                    counter.TryCounter(incoming.transform.position);
                }
                finally
                {
                    PlayerCounterState.PopImpactProjectile(incoming);
                }
                if (counterSucceeded)
                    break;
            }

            timer += Time.deltaTime;
            yield return null;
        }

        if (!counterSucceeded)
        {
            counter.EndCounter();
            if (shield != null) shield.Expire();
            yield break;
        }

        // 쉴드가 타격을 받아 수축한 뒤 충격파로 터진다. 이미 성공한 반격이므로 그사이 대시로 취소해도 충격파는 나간다.
        float hitboxSpawnTime = Time.time + counterHitboxDelay;

        while (Time.time < hitboxSpawnTime)
            yield return null;

        // 지연 시간 동안 충격파 범위 안에 들어온 추가 적 투사체도 소멸·반사한다.
        spawnedReflectCount += ReflectProjectilesInRange(
            playerController,
            reflectSweepRadius,
            0.04f,
            spawnedReflectCount,
            null);

        SpawnCounterHitbox(controller);
        if (shield != null) shield.Release();

        counter.EndCounter();
    }

    private ProjectileBase FindProjectileInRange(PlayerController player, float radius)
    {
        if (player == null || player.Motor == null || radius <= 0f) return null;
        Vector2 center = GetCounterCenter(player);
        ContactFilter2D filter = BuildProjectileFilter();
        projectileHits.Clear();
        // FixedUpdate 이후 이동한 투사체의 최신 Transform 위치를 물리 질의에 반영한다.
        RaycastMotor2D.InvalidateAttackQuerySync();
        player.Motor.OverlapAttackCircle(center, radius, filter, projectileHits);

        ProjectileBase nearest = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < projectileHits.Count; i++)
        {
            Collider2D hit = projectileHits[i];
            if (hit == null) continue;
            ProjectileBase proj = hit.GetComponentInParent<ProjectileBase>();
            if (proj == null || !proj.CanBeCounterReflected) continue;
            float sqr = ((Vector2)proj.transform.position - center).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                nearest = proj;
            }
        }
        return nearest;
    }

    private int ReflectProjectilesInRange(
        PlayerController player,
        float radius,
        float baseHoldDelay,
        int alreadySpawned,
        ProjectileBase directImpact)
    {
        if (player == null || player.Motor == null) return 0;

        Vector2 center = GetCounterCenter(player);
        float shockwaveRadius = GetShockwaveRadius();
        int spawned = 0;

        if (directImpact != null && directImpact.CanBeCounterReflected && capturedProjectiles.Add(directImpact))
        {
            if (ConsumeAndReflect(player, center, shockwaveRadius, directImpact, baseHoldDelay, alreadySpawned + spawned))
                spawned++;
        }

        if (radius > 0f)
        {
            ContactFilter2D filter = BuildProjectileFilter();
            projectileHits.Clear();
            RaycastMotor2D.InvalidateAttackQuerySync();
            player.Motor.OverlapAttackCircle(center, radius, filter, projectileHits);

            for (int i = 0; i < projectileHits.Count; i++)
            {
                Collider2D hit = projectileHits[i];
                if (hit == null) continue;
                ProjectileBase proj = hit.GetComponentInParent<ProjectileBase>();
                if (proj == null || !proj.CanBeCounterReflected) continue;
                if (!capturedProjectiles.Add(proj)) continue;

                if (ConsumeAndReflect(player, center, shockwaveRadius, proj, baseHoldDelay, alreadySpawned + spawned))
                    spawned++;
            }
        }

        return spawned;
    }

    private bool ConsumeAndReflect(
        PlayerController player,
        Vector2 center,
        float shockwaveRadius,
        ProjectileBase enemyProjectile,
        float baseHoldDelay,
        int shotIndex)
    {
        Vector2 projPos = enemyProjectile.transform.position;
        CharacterController2D shooter = enemyProjectile.Owner;
        Vector2 incomingVel = enemyProjectile.CurrentVelocity;
        Vector2 fallbackDir = incomingVel.sqrMagnitude > 0.001f
            ? -incomingVel.normalized
            : (projPos - center).sqrMagnitude > 0.001f ? (projPos - center).normalized : Vector2.right * player.Facing;

        // 적 투사체는 즉시 소멸(풀 반환)시킨다.
        enemyProjectile.ForceReturnToPool();

        if (reflectShotPrefab == null || WorldEntityPool.Instance == null || shotIndex >= reflectMaxShots)
            return false;

        // 충격파 범위 안의 살아 있는 적은 기존 충격파 피해를 그대로 받으므로 반사 투사체를 중복 발사하지 않는다.
        bool shooterAlive = IsShooterAlive(shooter);
        if (shooterAlive && IsInsideShockwave(center, shockwaveRadius, shooter))
            return false;

        // 반사 투사체는 포획 지점(쉴드 바깥 테두리 부근으로 정돈)에서 생성해 목표 적에게 일직선으로 사출한다.
        Vector2 offset = projPos - center;
        float dist = offset.magnitude;
        Vector2 spawnPos = dist > 0.05f
            ? center + (offset / dist) * Mathf.Clamp(dist, 1.15f, 2.3f)
            : center + fallbackDir * 1.45f;

        CounterReflectShot shot = WorldEntityPool.Instance.Spawn(reflectShotPrefab, spawnPos, Quaternion.identity);
        DamageInfo info = reflectDamageInfo.damage > 0f ? reflectDamageInfo : counterDamageInfo;
        LayerMask mask = reflectPlatformLayer.value != 0 ? reflectPlatformLayer : LayerMask.GetMask("Platform");
        shot.Initialize(player, WithSource(info, player), reflectProjectileSpeed, shotIndex, mask);
        shot.OnHitEnemy += (owner, hitPoint) => OnHitEnemy?.Invoke(owner, hitPoint);
        float delay = Mathf.Max(0f, baseHoldDelay) + shotIndex * reflectStaggerInterval;
        shot.ArmAndLaunch(spawnPos, shooterAlive ? shooter : null, fallbackDir, delay, reflectMaxFlightDistance);
        return true;
    }

    private static Vector2 GetCounterCenter(PlayerController player)
    {
        if (player.Health != null && player.Health.TryGetComponent<Collider2D>(out var col))
            return col.bounds.center;
        return player.transform.position;
    }

    private float GetShockwaveRadius()
    {
        if (counterHitbox != null && counterHitbox.TryGetComponent<CircleCollider2D>(out var circle))
            return circle.radius * Mathf.Max(Mathf.Abs(counterHitbox.transform.localScale.x), Mathf.Abs(counterHitbox.transform.localScale.y));
        return 4f;
    }

    private static bool IsShooterAlive(CharacterController2D shooter)
    {
        if (shooter == null || !shooter.gameObject.activeInHierarchy) return false;
        if (shooter is EnemyController enemy)
            return enemy.Health != null && enemy.Health.currentHP > 0f;
        var health = shooter.GetComponentInChildren<HealthBase>(true);
        return health != null && health.currentHP > 0f;
    }

    private static bool IsInsideShockwave(Vector2 center, float shockwaveRadius, CharacterController2D shooter)
    {
        Collider2D col = null;
        if (shooter is EnemyController enemy && enemy.Health != null)
            col = enemy.Health.GetComponent<Collider2D>();
        if (col == null) col = shooter.GetComponentInChildren<Collider2D>();
        Vector2 closest = col != null ? col.ClosestPoint(center) : (Vector2)shooter.transform.position;
        return Vector2.Distance(center, closest) <= shockwaveRadius;
    }

    private static ContactFilter2D BuildProjectileFilter()
    {
        int mask = (1 << 0) | (1 << LayerMask.NameToLayer("Projectile"));
        var filter = new ContactFilter2D();
        filter.SetLayerMask(mask);
        filter.useTriggers = true;
        return filter;
    }

    private bool IsStillAttackState(PlayerController playerController)
    {
        return playerController.AbilityManager.StateMachine.CurrentState == PlayerState.Attack;
    }

    private CounterShield SpawnShield(CharacterController2D controller, PlayerCounterState counter)
    {
        if (shieldPrefab == null || WorldEntityPool.Instance == null)
            return null;

        CounterShield shield = WorldEntityPool.Instance.Spawn(shieldPrefab, controller.transform.position, Quaternion.identity);
        shield.Begin(controller, counter);
        return shield;
    }

    private void SpawnCounterHitbox(CharacterController2D controller)
    {
        MeleeHitbox hitbox = WorldEntityPool.Instance.Spawn(
            counterHitbox,
            controller.transform.position,
            Quaternion.identity);

        hitbox.SetHitFeedback(counterHitFeedback);
        hitbox.Initialize(controller, WithSource(counterDamageInfo, controller));
    }

    public override bool ShouldEnterRecover()
    {
        return counterSucceeded;
    }

    public override bool CanForceRecoverOnSuccess()
    {
        return true;
    }
}