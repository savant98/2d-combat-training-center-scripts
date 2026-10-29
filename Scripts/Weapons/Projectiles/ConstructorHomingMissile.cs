using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class ConstructorHomingMissile : ProjectileBase
{
    private const float MinimumFlightSpeed = 3f;
    public enum FlightPhase { Inactive, Launch, Slow, Chase, Coast }

    [Header("Guidance")]
    [FormerlySerializedAs("launchStraightTime")]
    [SerializeField, Min(0f)] private float launchDuration = 0.7f;
    [SerializeField, Min(0f)] private float launchTurnRateDegrees = 20f;
    [SerializeField, Min(0f)] private float slowDuration = 0.7f;
    [SerializeField, Range(0f, 1f)] private float slowSpeedRatio = 3f / 14f;
    [SerializeField, Min(0f)] private float slowTurnRateDegrees = 180f;
    [SerializeField, Min(0f)] private float trackingDuration = 3.75f;
    [SerializeField, Min(0f)] private float turnRateDegrees = 180f;
    [SerializeField, Min(0f)] private float accelerationDuration = 0.9f;
    [SerializeField, Range(0f, 1f)] private float initialSpeedRatio = 6f / 14f;
    [SerializeField, Min(0.1f)] private float lifeTime = 5f;

    [Header("Ejection And Inertia")]
    [SerializeField, Range(1f, 89f)] private float ejectionAngleDegrees = 45f;
    [SerializeField, Min(0f)] private float coastTurnDecayDuration = 1.8f;
    [SerializeField, Min(0f)] private float steeringAcceleration = 540f;

    [Header("Presentation")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private FeedbackDataSO emptyHitFeedbackData;

    [Header("Explosion")]
    [SerializeField, Min(0f)] private float explosionRadius = 1.5f;
    [SerializeField] private WeaponBaseSO.DamageInfo explosionDamageInfo = new WeaponBaseSO.DamageInfo { damage = 12f };

    public FlightPhase CurrentPhase { get; private set; } = FlightPhase.Inactive;
    public bool UsesEjection { get; private set; }
    public int EjectionSide { get; private set; }
    public bool HasPassedTarget { get; private set; }
    public float CurrentTurnRate => angularVelocity;

    private ParticleSystem[] propulsionParticles;
    private Quaternion restingVisualRotation;
    private Vector2 flightDirection;
    private float activeLaunchDuration;
    private float activeLaunchTurnRateDegrees;
    private float activeSlowDuration;
    private float activeSlowSpeedRatio;
    private float activeSlowTurnRateDegrees;
    private float activeTrackingDuration;
    private float activeTurnRateDegrees;
    private float activeAccelerationDuration;
    private float activeInitialSpeedRatio;
    private float activeLifeTime;
    private float age;
    private float currentSpeed;
    private bool guidanceConfigured;
    private bool initialized;
    private bool hasDetonated;
    private float activeExplosionRadius;
    private WeaponBaseSO.DamageInfo activeExplosionDamageInfo;
    private ContactFilter2D explosionFilter;
    private readonly List<Collider2D> explosionHits = new List<Collider2D>(8);
    private readonly HashSet<HealthBase> explosionTargets = new HashSet<HealthBase>();
    private float activeEjectionAngleDegrees;
    private float activeCoastTurnDecayDuration;
    private float activeSteeringAcceleration;
    private float angularVelocity;
    private float closestTargetDistance;
    private float previousTargetDistance;
    private float coastElapsed;
    private float coastInitialTurnRate;
    private bool targetApproachArmed;
    private bool coasting;

    protected override void Awake()
    {
        base.Awake();
        if (visualRoot == null) visualRoot = transform.Find("Visual");
        // 모터의 판정 기준은 회전시키지 않고 내부 외형만 실제 비행 방향으로 돌린다.
        if (visualRoot == transform) visualRoot = null;
        if (visualRoot != null) restingVisualRotation = visualRoot.localRotation;
        propulsionParticles = GetComponentsInChildren<ParticleSystem>(true);
        ApplyGuidanceSettings(launchDuration, launchTurnRateDegrees, trackingDuration, turnRateDegrees,
            accelerationDuration, initialSpeedRatio, lifeTime, slowDuration, slowSpeedRatio, slowTurnRateDegrees);
        ApplyEjectionSettings(ejectionAngleDegrees, coastTurnDecayDuration, steeringAcceleration);
        ConfigureExplosion(explosionRadius, explosionDamageInfo);
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        initialized = false;
        guidanceConfigured = false;
        target = null;
        velocity = Vector2.zero;
        flightDirection = Vector2.up;
        age = currentSpeed = 0f;
        CurrentPhase = FlightPhase.Inactive;
        ResetEjectionState();
        hasDetonated = false;
        explosionHits.Clear();
        explosionTargets.Clear();
        ApplyGuidanceSettings(launchDuration, launchTurnRateDegrees, trackingDuration, turnRateDegrees,
            accelerationDuration, initialSpeedRatio, lifeTime, slowDuration, slowSpeedRatio, slowTurnRateDegrees);
        ApplyEjectionSettings(ejectionAngleDegrees, coastTurnDecayDuration, steeringAcceleration);
        ConfigureExplosion(explosionRadius, explosionDamageInfo);
        ClearPropulsion();
        if (trail != null) trail.emitting = false;
        if (visualRoot != null) visualRoot.localRotation = restingVisualRotation;
    }

    // 풀에서 꺼낸 뒤 Initialize 전에 호출한다. 발사 방향은 월드 좌표 기준으로 전달한다.
    public void ConfigureGuidance(Transform target, Vector2 launchDirection,
        float launchDuration, float launchTurnRateDegrees, float trackingDuration, float turnRateDegrees,
        float accelerationDuration, float initialSpeedRatio, float lifeTime,
        float slowDuration = 0.7f, float slowSpeedRatio = 0.15f, float slowTurnRateDegrees = 180f)
    {
        this.target = target;
        flightDirection = launchDirection.sqrMagnitude > 0.000001f
            ? launchDirection.normalized : Vector2.up;
        ApplyGuidanceSettings(launchDuration, launchTurnRateDegrees, trackingDuration, turnRateDegrees,
            accelerationDuration, initialSpeedRatio, lifeTime, slowDuration, slowSpeedRatio, slowTurnRateDegrees);
        guidanceConfigured = true;
        UpdateVisualDirection();
    }

    public void ConfigureExplosion(float radius, WeaponBaseSO.DamageInfo damage)
    {
        activeExplosionRadius = Mathf.Max(0f, radius);
        damage.damage = Mathf.Max(0f, damage.damage);
        activeExplosionDamageInfo = damage;
    }

    // 발사구의 좌우를 월드 기준으로 전달한다. 일반 발사체의 기본 유도와는 별도로 설정한다.
    public void ConfigureEjection(int side, float angleDegrees = 45f,
        float coastTurnDecayDuration = 1.8f, float steeringAcceleration = 540f)
    {
        UsesEjection = side != 0;
        EjectionSide = side == 0 ? 0 : side < 0 ? -1 : 1;
        ApplyEjectionSettings(angleDegrees, coastTurnDecayDuration, steeringAcceleration);
    }

    public override void Initialize(CharacterController2D owner,
        WeaponBaseSO.DamageInfo damageInfo, float speed, int count, LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);
        this.speed = Mathf.Max(MinimumFlightSpeed, speed);
        if (!guidanceConfigured)
            flightDirection = Vector2.up;
        if (UsesEjection) flightDirection = EjectionDirection();
        age = 0f;
        currentSpeed = Mathf.Max(MinimumFlightSpeed, this.speed * activeInitialSpeedRatio);
        initialized = true;
        hasDetonated = false;
        explosionFilter = new ContactFilter2D();
        explosionFilter.SetLayerMask(1 << LayerMask.NameToLayer(ownerFaction == Faction.Player ? "Enemy" : "Player"));
        explosionFilter.useTriggers = true;
        CurrentPhase = UsesEjection
            ? (activeLaunchDuration > 0f ? FlightPhase.Launch : FlightPhase.Chase) : PhaseAt(0f);
        currentSpeed = SpeedAt(CurrentPhase, 0f);
        velocity = flightDirection * currentSpeed;
        StartPropulsion();
        UpdateVisualDirection();
    }

    protected override void FixedUpdate()
    {
        if (!initialized || isReturning) return;
        float step = Time.fixedDeltaTime;
        age += step;
        if (age >= activeLifeTime)
        {
            Detonate(transform.position, null, true);
            return;
        }

        if (UsesEjection)
        {
            UpdateEjection(step);
            UpdateVisualDirection();
            base.FixedUpdate();
            return;
        }

        CurrentPhase = PhaseAt(age);
        switch (CurrentPhase)
        {
            case FlightPhase.Launch:
                // 초반 속도를 유지하다가 구간 후반에 빠르게 감속한다.
                TurnTowardTarget(step, activeLaunchTurnRateDegrees);
                currentSpeed = SpeedAt(CurrentPhase, age);
                velocity = flightDirection * currentSpeed;
                break;
            case FlightPhase.Slow:
                // 최저 속도를 유지해 이동하면서 하강 방향을 잡는다.
                TurnTowardTarget(step, activeSlowTurnRateDegrees);
                currentSpeed = SpeedAt(CurrentPhase, age);
                velocity = flightDirection * currentSpeed;
                break;
            case FlightPhase.Chase:
                // 저속 때의 방향을 이어받고 지정한 시간에 걸쳐 부드럽게 최고 속도까지 가속한다.
                TurnTowardTarget(step, activeTurnRateDegrees);
                currentSpeed = SpeedAt(CurrentPhase, age);
                velocity = flightDirection * currentSpeed;
                break;
            case FlightPhase.Coast:
                velocity = flightDirection * currentSpeed;
                break;
        }

        UpdateVisualDirection();
        // 이동·벽·경로 피격과 현재 위치의 Overlap을 모두 기존 모터가 처리한다.
        base.FixedUpdate();
    }

    protected override void HitTarget(HealthBase health, Vector2 hitPoint)
    {
        // 경로 Cast의 접촉점을 중심으로 폭발시켜 고속 탄의 이동 끝점에서 범위 피해가 생기지 않게 한다.
        Detonate(hitPoint, health);
    }

    protected override void OnHitSomething()
    {
        // 벽 앞까지 지나온 경로의 직격 판정을 먼저 처리하는 공통 순서를 유지한다.
        CheckMovementHits();
        if (!isReturning && !hasDetonated) Detonate(transform.position);
    }

    private void Detonate(Vector2 center, HealthBase directTarget = null, bool expired = false)
    {
        if (!initialized || isReturning || hasDetonated) return;
        hasDetonated = true;
        PreserveExplosionTrail();
        explosionTargets.Clear();
        if (directTarget != null)
        {
            explosionTargets.Add(directTarget);
            DamageTarget(directTarget, damageInfo, center);
        }

        if (motor != null && activeExplosionRadius > 0f && activeExplosionDamageInfo.damage > 0f)
        {
            RaycastMotor2D.InvalidateAttackQuerySync();
            motor.OverlapAttackCircle(center, activeExplosionRadius, explosionFilter, explosionHits);
            foreach (var hit in explosionHits)
            {
                if (hit == null) continue;
                var health = hit.GetComponentInParent<HealthBase>();
                if (health == null || !explosionTargets.Add(health)) continue;
                DamageTarget(health, activeExplosionDamageInfo, hit.ClosestPoint(center));
            }
        }

        var feedback = expired && emptyHitFeedbackData != null ? emptyHitFeedbackData : hitFeedbackData;
        if (FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(feedback, center);
        ReturnToPool();
    }

    private void DamageTarget(HealthBase health, WeaponBaseSO.DamageInfo damage, Vector2 hitPoint)
    {
        if (health == null || health.IsInvincible || damage.damage <= 0f) return;
        if (owner != null && (health == owner.GetComponentInParent<HealthBase>()
            || health.transform.IsChildOf(owner.transform))) return;
        if (ownerFaction == Faction.Player && health is EnemyHealth enemy)
        {
            if (enemy.enemyShieldSystem != null && enemy.enemyShieldSystem.IsSpawnLocked) return;
            if (enemy.enemyShieldSystem == null) enemy.TakeDamage(damage, hitPoint);
            else enemy.enemyShieldSystem.TakeDamage(damage, hitPoint);
            OnHitEnemy?.Invoke(owner, hitPoint);
        }
        else if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
            player.TakeDamage(damage, hitPoint);
    }

    private void PreserveExplosionTrail()
    {
        if (trail == null || trail.positionCount == 0 || trail.time <= 0f) return;
        var fadingTrail = trail;
        // 원래 이력과 수명을 유지하고 다음 발사는 빈 Renderer를 사용한다.
        var nextTrail = Instantiate(fadingTrail, transform);
        nextTrail.name = fadingTrail.name;
        nextTrail.Clear();
        nextTrail.emitting = false;
        nextTrail.autodestruct = false;
        nextTrail.gameObject.SetActive(false);
        nextTrail.transform.localPosition = Vector3.zero;
        nextTrail.transform.localRotation = Quaternion.identity;
        nextTrail.transform.localScale = Vector3.one;
        fadingTrail.transform.SetParent(null, true);
        fadingTrail.emitting = false;
        fadingTrail.autodestruct = false;
        Destroy(fadingTrail.gameObject, fadingTrail.time + Time.fixedDeltaTime);
        trail = nextTrail;
    }

    protected override void ReturnToPool()
    {
        if (isReturning) return;
        initialized = false;
        target = null;
        velocity = Vector2.zero;
        CurrentPhase = FlightPhase.Inactive;
        ResetEjectionState();
        ClearPropulsion();
        if (trail != null) trail.emitting = false;
        base.ReturnToPool();
    }

    private void OnDisable()
    {
        initialized = false;
        target = null;
        velocity = Vector2.zero;
        CurrentPhase = FlightPhase.Inactive;
        ResetEjectionState();
        ClearPropulsion();
        // 폭발 잔상은 별도 Renderer로 남기고 현재 소유한 빈 Trail 또는 취소된 비행만 정리한다.
        if (trail != null)
        {
            trail.emitting = false;
            trail.Clear();
            trail.gameObject.SetActive(false);
        }
        if (visualRoot != null) visualRoot.localRotation = restingVisualRotation;
    }

    private void OnDestroy()
    {
        if (trail != null && !trail.transform.IsChildOf(transform)) Destroy(trail.gameObject);
    }

    private void ApplyEjectionSettings(float angleDegrees, float coastDecay, float angularAcceleration)
    {
        activeEjectionAngleDegrees = Mathf.Clamp(angleDegrees, 1f, 89f);
        activeCoastTurnDecayDuration = Mathf.Max(0f, coastDecay);
        activeSteeringAcceleration = Mathf.Max(0f, angularAcceleration);
    }

    private void ResetEjectionState()
    {
        UsesEjection = false;
        EjectionSide = 0;
        HasPassedTarget = false;
        angularVelocity = coastElapsed = coastInitialTurnRate = 0f;
        closestTargetDistance = previousTargetDistance = float.PositiveInfinity;
        targetApproachArmed = coasting = false;
    }

    private Vector2 EjectionDirection()
    {
        float radians = activeEjectionAngleDegrees * Mathf.Deg2Rad;
        return new Vector2(EjectionSide * Mathf.Cos(radians), Mathf.Sin(radians));
    }

    private void UpdateEjection(float step)
    {
        if (age < activeLaunchDuration)
        {
            CurrentPhase = FlightPhase.Launch;
            // 사출 시간 동안에는 표적 위치와 무관하게 처음의 대각선 방향을 유지한다.
            flightDirection = EjectionDirection();
            angularVelocity = 0f;
            currentSpeed = SpeedAt(CurrentPhase, age);
        }
        else
        {
            currentSpeed = SpeedAt(FlightPhase.Chase, age);
            UpdateFreeHoming(step);
        }
        velocity = flightDirection * currentSpeed;
    }

    private void UpdateFreeHoming(float step)
    {
        if (!coasting)
        {
            bool hasTarget = TryTargetDirection(out Vector2 direction);
            if (hasTarget)
            {
                float distance = Vector2.Distance(transform.position, target.position);
                closestTargetDistance = Mathf.Min(closestTargetDistance, distance);
                float forward = Vector2.Dot(flightDirection, direction);
                if (forward > 0f && !float.IsPositiveInfinity(previousTargetDistance)
                    && distance < previousTargetDistance - 0.001f)
                    targetApproachArmed = true;
                // 추적 중 앞쪽 표적에 접근한 기록이 있을 때만 통과를 판정한다.
                // 빗나간 폭이 커도 표적이 뒤로 넘어가며 멀어지면 재조준하지 않는다.
                if (targetApproachArmed && forward < 0f && distance > closestTargetDistance + 0.05f
                    && distance > previousTargetDistance + 0.001f)
                {
                    HasPassedTarget = true;
                    BeginCoast();
                }
                previousTargetDistance = distance;
            }
            if (!hasTarget || age >= activeLaunchDuration + activeTrackingDuration)
                BeginCoast();
            if (!coasting)
            {
                CurrentPhase = FlightPhase.Chase;
                float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                float angleError = Mathf.DeltaAngle(DirectionAngle(), targetAngle);
                float desiredTurnRate = Mathf.Clamp(angleError / 0.2f,
                    -activeTurnRateDegrees, activeTurnRateDegrees);
                // 추적 방향을 즉시 꺾지 않고 각속도를 바꿔 발사 때의 큰 호를 이어간다.
                angularVelocity = Mathf.MoveTowards(angularVelocity, desiredTurnRate,
                    activeSteeringAcceleration * step);
            }
        }
        if (coasting)
        {
            CurrentPhase = FlightPhase.Coast;
            coastElapsed += step;
            float progress = activeCoastTurnDecayDuration > 0f
                ? Mathf.Clamp01(coastElapsed / activeCoastTurnDecayDuration) : 1f;
            // 지나친 순간의 회전 관성만 감쇠시키고 표적을 재획득하지 않는다.
            angularVelocity = coastInitialTurnRate * (1f - Mathf.SmoothStep(0f, 1f, progress));
        }
        SetFlightAngle(DirectionAngle() + angularVelocity * step);
    }

    private void BeginCoast()
    {
        if (coasting) return;
        coasting = true;
        coastElapsed = 0f;
        coastInitialTurnRate = angularVelocity;
    }

    private void ApplyGuidanceSettings(float launchTime, float launchTurnRate, float duration, float turnRate,
        float accelerationTime, float initialRatio, float lifetime, float slowTime, float slowRatio, float slowTurnRate)
    {
        activeLaunchDuration = Mathf.Max(0f, launchTime);
        activeLaunchTurnRateDegrees = Mathf.Max(0f, launchTurnRate);
        activeSlowDuration = Mathf.Max(0f, slowTime);
        activeSlowTurnRateDegrees = Mathf.Max(0f, slowTurnRate);
        activeTrackingDuration = Mathf.Max(0f, duration);
        activeTurnRateDegrees = Mathf.Max(0f, turnRate);
        activeAccelerationDuration = Mathf.Max(0f, accelerationTime);
        activeInitialSpeedRatio = Mathf.Clamp01(initialRatio);
        activeSlowSpeedRatio = Mathf.Clamp(slowRatio, 0f, activeInitialSpeedRatio);
        activeLifeTime = Mathf.Max(0.1f, lifetime);
    }

    private FlightPhase PhaseAt(float elapsed)
    {
        if (elapsed < activeLaunchDuration) return FlightPhase.Launch;
        if (elapsed < activeLaunchDuration + activeSlowDuration) return FlightPhase.Slow;
        if (elapsed < activeLaunchDuration + activeSlowDuration + activeTrackingDuration)
            return FlightPhase.Chase;
        return FlightPhase.Coast;
    }

    private float SpeedAt(FlightPhase phase, float elapsed)
    {
        float slowSpeed = Mathf.Clamp(speed * activeSlowSpeedRatio, MinimumFlightSpeed, speed);
        if (phase == FlightPhase.Launch)
        {
            float progress = activeLaunchDuration > 0f ? Mathf.Clamp01(elapsed / activeLaunchDuration) : 1f;
            float eased = progress * progress * progress;
            return Mathf.Lerp(Mathf.Max(MinimumFlightSpeed, speed * activeInitialSpeedRatio), slowSpeed, eased);
        }
        if (phase == FlightPhase.Slow) return slowSpeed;
        if (phase == FlightPhase.Chase)
        {
            float chaseElapsed = elapsed - activeLaunchDuration
                - (UsesEjection ? 0f : activeSlowDuration);
            float progress = activeAccelerationDuration > 0f
                ? Mathf.Clamp01(chaseElapsed / activeAccelerationDuration) : 1f;
            return Mathf.SmoothStep(slowSpeed, speed, progress);
        }
        return Mathf.Max(MinimumFlightSpeed, currentSpeed);
    }

    private void TurnTowardTarget(float step, float turnRate)
    {
        Vector2 direction;
        if (!TryTargetDirection(out direction)) return;
        TurnTowardDirection(direction, step, turnRate);
    }

    private void TurnTowardDirection(Vector2 direction, float step, float turnRate)
    {
        if (direction.sqrMagnitude < 0.000001f) return;
        float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        SetFlightAngle(Mathf.MoveTowardsAngle(DirectionAngle(), targetAngle, turnRate * step));
    }

    private bool TryTargetDirection(out Vector2 direction)
    {
        direction = Vector2.zero;
        if (target == null || !target.gameObject.activeInHierarchy) return false;
        Vector2 delta = (Vector2)target.position - (Vector2)transform.position;
        if (delta.sqrMagnitude <= 0.000001f) return false;
        direction = delta.normalized;
        return true;
    }

    private float DirectionAngle() => Mathf.Atan2(flightDirection.y, flightDirection.x) * Mathf.Rad2Deg;

    private void SetFlightAngle(float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        flightDirection = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }

    private void StartPropulsion()
    {
        if (trail != null) trail.emitting = true;
        if (propulsionParticles == null) return;
        foreach (var particle in propulsionParticles)
        {
            if (particle == null) continue;
            particle.Play(false);
        }
    }

    private void UpdateVisualDirection()
    {
        if (visualRoot == null) return;
        visualRoot.rotation = Quaternion.Euler(0f, 0f, DirectionAngle());
    }

    private void ClearPropulsion()
    {
        if (propulsionParticles == null) return;
        foreach (var particle in propulsionParticles)
            if (particle != null) particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
