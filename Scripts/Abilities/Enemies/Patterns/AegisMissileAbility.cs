using UnityEngine;

// 기존 유도탄의 모터 이동·선회·직격/폭발 중복 방지와 풀 반환을 그대로 사용한다.
public sealed class AegisMissileAbility : FlyingAbility
{
    [SerializeField] private AiAbility nextAbility;
    private AegisMissileDataSO settings;
    public override bool IsBodyAttackActive => false;
    public int FiredCount { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        settings = (AegisMissileDataSO)data;
    }

    public override bool CanBegin()
    {
        AcquireTarget();
        if (!HasTarget || settings.projectilePrefab == null) return false;
        Vector2 delta = target.position - transform.position;
        float distance = delta.magnitude;
        return distance >= settings.minRange && distance <= settings.maxRange
            && controller.Motor.RaycastEnvironment(transform.position, delta.normalized, distance).collider == null;
    }

    protected override void OnWindupStart()
    {
        AcquireTarget();
        BeginFlight();
        FiredCount = 0;
        SetFlockRole(EnemyFlockRole.Chase);
        FaceTarget();
    }

    protected override void OnWindup(float t) { FaceTarget(); Hover(); }
    protected override void OnExecute(float t) => Hover();
    protected override void OnRecover(float t) => Hover();

    protected override void OnExecuteStart()
    {
        if (!HasTarget || settings.projectilePrefab == null || WorldEntityPool.Instance == null) return;
        Vector2 direction = (target.position - transform.position).normalized;
        var missile = WorldEntityPool.Instance.Spawn(settings.projectilePrefab, transform.position, Quaternion.identity);
        missile.ConfigureGuidance(target, direction, .15f, 0f, settings.trackingDuration, settings.turnRateDegrees,
            .45f, .55f, settings.lifeTime, 0f);
        missile.ConfigureExplosion(settings.explosionRadius, settings.explosionDamageInfo);
        missile.Initialize(controller, settings.damageInfo, settings.projectileSpeed, 1, settings.collideLayer);
        FiredCount = 1;
    }

    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;
        SetFlockRole(EnemyFlockRole.None);
        ReserveNext(nextAbility);
    }
}
