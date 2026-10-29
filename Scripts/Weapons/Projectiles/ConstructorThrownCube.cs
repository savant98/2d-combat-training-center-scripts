using System.Collections.Generic;
using UnityEngine;

public enum ConstructorCubeKind { Bomb, Shockwave, Bounce }

public sealed class ConstructorThrownCube : ProjectileBase
{
    public enum BounceDeploymentPhase { None, Drop, VerticalRise, ActivationWait, Active }

    [Header("Cube")]
    [SerializeField] private ConstructorCubeKind cubeKind;
    [SerializeField] private Transform visualRoot;
    [SerializeField, Min(0.1f)] private float gravity = 26f;
    [SerializeField, Min(0.1f)] private float lifeTime = 4f;
    [SerializeField] private float rotationSpeed = 180f;

    [Header("Bomb")]
    [SerializeField, Min(0f)] private float explosionRadius = 5.5f;
    [SerializeField] private WeaponBaseSO.DamageInfo explosionDamageInfo = new WeaponBaseSO.DamageInfo { damage = 18f };
    [SerializeField] private ConstructorExplosionRange explosionRangePrefab;
    [SerializeField, Min(0.01f)] private float bombWarningDuration = 0.7f;

    [Header("Ground Wave")]
    [SerializeField] private ConstructorGroundWave wavePrefab;
    [SerializeField] private FeedbackDataSO groundImpactFeedbackData;

    [Header("Ground Bounce")]
    [SerializeField, Min(0f)] private float bounceHorizontalSpeed = 12f;
    [SerializeField, Min(0f)] private float bounceVerticalSpeed = 11f;
    [SerializeField, Min(0.01f)] private float bounceHitInterval = 0.75f;
    [SerializeField] private FeedbackDataSO bounceFeedbackData;
    [SerializeField] private FeedbackDataSO wallBounceFeedbackData;
    [SerializeField] private FeedbackDataSO expireFeedbackData;
    [SerializeField, Min(0.01f)] private float bounceSquashDuration = 0.18f;
    [SerializeField, Min(1f)] private float bounceSquashWidth = 1.24f;
    [SerializeField, Range(0.1f, 1f)] private float bounceSquashHeight = 0.62f;

    [Header("Autonomous Bounce Deployment")]
    [SerializeField, Min(0.01f)] private float deploymentRiseDuration = 0.7f;
    [SerializeField, Min(0.01f)] private float activationWaitDuration = 0.5f;
    [SerializeField, Min(0.1f)] private float deploymentTimeout = 6f;
    [SerializeField] private FeedbackDataSO activationFeedbackData;

    public ConstructorCubeKind Kind => activeKind;
    public Vector2 CurrentVelocity => velocity;
    public float Age => age;
    public float Gravity => gravity;
    public bool HasResolved => resolved;
    public int LeaseId { get; private set; }
    public bool IsBombWarning => bombWarningActive;
    public float WarningAge => warningAge;
    public Vector2 WarningCenter => warningCenter;
    public int GroundBounceCount { get; private set; }
    public int WallReflectionCount { get; private set; }
    public BounceDeploymentPhase BouncePhase { get; private set; }
    public float BouncePhaseAge => bouncePhaseAge;
    public float BounceActiveAge => bounceActiveAge;
    public bool UsesAutonomousBounce => autonomousBounce;
    protected override bool UsesPathHitDetection => activeKind != ConstructorCubeKind.Shockwave
        && (!autonomousBounce || BouncePhase == BounceDeploymentPhase.Active);

    private ConstructorCubeKind activeKind;
    private ConstructorGroundWave activeWavePrefab;
    private Vector2 initialVelocity;
    private float initialBounceDirection = 1f;
    private float activeGravity;
    private float activeLifeTime;
    private float activeExplosionRadius;
    private WeaponBaseSO.DamageInfo activeExplosionDamage;
    private float age;
    private bool configured;
    private bool initialized;
    private bool resolved;
    private bool bombWarningActive;
    private bool restingColliderEnabled;
    private float warningAge;
    private Vector2 warningCenter;
    private HealthBase warningDirectTarget;
    private ConstructorExplosionRange warningRange;
    private int warningRangeLease;
    private bool stationaryVisualHidden;
    private bool stationaryVisualWasActive;
    private Quaternion restingVisualRotation;
    private Vector3 restingVisualScale;
    private Vector3 restingVisualPosition;
    private float bounceVisualHalfHeight;
    private float squashAge;
    private bool autonomousBounce, deploymentRiseBlocked;
    private int deploymentDirection = 1;
    private float bouncePhaseAge, bounceActiveAge, deploymentAge;
    private ContactFilter2D explosionFilter;
    private readonly List<Collider2D> explosionHits = new List<Collider2D>(8);
    private readonly HashSet<HealthBase> damagedTargets = new HashSet<HealthBase>();
    private readonly Dictionary<HealthBase, float> bounceHitTimes = new Dictionary<HealthBase, float>();

    protected override void Awake()
    {
        base.Awake();
        restingColliderEnabled = col != null && col.enabled;
        if (visualRoot == null) visualRoot = transform.Find("Visual");
        if (visualRoot == transform) visualRoot = null;
        if (visualRoot != null)
        {
            restingVisualRotation = visualRoot.localRotation;
            restingVisualScale = visualRoot.localScale;
            restingVisualPosition = visualRoot.localPosition;
            float parentScale = visualRoot.parent != null ? Mathf.Abs(visualRoot.parent.lossyScale.y) : 1f;
            float worldHalfHeight = col is CircleCollider2D circle
                ? circle.radius * Mathf.Abs(circle.transform.lossyScale.y) : col.bounds.extents.y;
            bounceVisualHalfHeight = worldHalfHeight / Mathf.Max(0.001f, parentScale);
        }
        ResetPayload();
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        unchecked { LeaseId++; }
        ClearWarning();
        initialized = configured = resolved = false;
        age = 0f;
        velocity = initialVelocity = Vector2.zero;
        initialBounceDirection = 1f;
        ResetBounceDeployment();
        GroundBounceCount = WallReflectionCount = 0;
        damagedTargets.Clear();
        bounceHitTimes.Clear();
        ResetPayload();
        ResetVisual();
    }

    // 풀 대여 뒤 Initialize 전에 호출한다. 중력은 아래 방향 가속도의 양수 크기다.
    public void ConfigureThrow(ConstructorCubeKind kind, Vector2 launchVelocity, float gravity,
        float lifeTime, float explosionRadius, WeaponBaseSO.DamageInfo explosionDamageInfo,
        ConstructorGroundWave wavePrefab)
    {
        activeKind = kind;
        initialVelocity = launchVelocity;
        activeGravity = Mathf.Max(0.1f, gravity);
        activeLifeTime = Mathf.Max(0.1f, lifeTime);
        activeExplosionRadius = Mathf.Max(0f, explosionRadius);
        explosionDamageInfo.damage = Mathf.Max(0f, explosionDamageInfo.damage);
        activeExplosionDamage = explosionDamageInfo;
        activeWavePrefab = wavePrefab;
        ResetBounceDeployment();
        configured = true;
    }

    // 놓은 큐브만 자율 준비 과정을 사용한다. 기존 투사체의 즉시 반동 설정은 유지한다.
    public bool ConfigureBounceDeployment(int direction)
    {
        if (!configured || initialized || activeKind != ConstructorCubeKind.Bounce) return false;
        autonomousBounce = true;
        deploymentDirection = direction < 0 ? -1 : 1;
        initialVelocity = Vector2.zero;
        return true;
    }

    public override void Initialize(CharacterController2D owner, WeaponBaseSO.DamageInfo damageInfo,
        float speed, int count, LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);
        ClearWarning();
        if (!configured) initialVelocity = new Vector2((owner != null ? owner.Facing : 1) * speed, 0f);
        velocity = initialVelocity;
        initialBounceDirection = Mathf.Abs(initialVelocity.x) > 0.001f ? Mathf.Sign(initialVelocity.x)
            : owner != null && owner.Facing < 0 ? -1f : 1f;
        if (autonomousBounce)
        {
            initialBounceDirection = deploymentDirection;
            BouncePhase = BounceDeploymentPhase.Drop;
        }
        bouncePhaseAge = bounceActiveAge = deploymentAge = 0f;
        deploymentRiseBlocked = false;
        age = 0f;
        initialized = true;
        resolved = false;
        GroundBounceCount = WallReflectionCount = 0;
        damagedTargets.Clear();
        bounceHitTimes.Clear();
        squashAge = bounceSquashDuration;
        explosionFilter = new ContactFilter2D();
        explosionFilter.SetLayerMask(1 << LayerMask.NameToLayer(ownerFaction == Faction.Player ? "Enemy" : "Player"));
        explosionFilter.useTriggers = true;
        if (trail != null) trail.emitting = true;
    }

    // 장착 시각물은 Ability가 유지하고, 월드 풀의 논리 폭탄은 현재 위치에서 예고만 진행한다.
    public bool BeginStationaryBombWarning(bool hideVisual = false)
    {
        if (!initialized || resolved || isReturning || bombWarningActive
            || activeKind != ConstructorCubeKind.Bomb || !gameObject.activeInHierarchy) return false;
        BeginBombWarning(transform.position);
        if (hideVisual && visualRoot != null)
        {
            stationaryVisualWasActive = visualRoot.gameObject.activeSelf;
            stationaryVisualHidden = true;
            visualRoot.gameObject.SetActive(false);
        }
        return true;
    }

    protected override void FixedUpdate()
    {
        if (!initialized || isReturning) return;
        squashAge = Mathf.Min(bounceSquashDuration, squashAge + Time.fixedDeltaTime);
        age += Time.fixedDeltaTime;
        if (bombWarningActive)
        {
            warningAge += Time.fixedDeltaTime;
            if (warningAge >= Mathf.Max(0.01f, bombWarningDuration))
                Detonate(warningCenter, warningDirectTarget);
            return;
        }
        if (autonomousBounce && BouncePhase != BounceDeploymentPhase.Active)
        {
            UpdateBounceDeployment();
            return;
        }
        if (autonomousBounce) bounceActiveAge += Time.fixedDeltaTime;
        if ((autonomousBounce ? bounceActiveAge : age) >= activeLifeTime)
        {
            if (activeKind == ConstructorCubeKind.Bomb) BeginBombWarning(transform.position);
            else
            {
                if (activeKind == ConstructorCubeKind.Bounce) PlayFeedback(expireFeedbackData, transform.position);
                ReturnToPool();
            }
            return;
        }
        velocity.y -= activeGravity * Time.fixedDeltaTime;
        base.FixedUpdate();
        if (initialized && !bombWarningActive) UpdateVisual();
    }

    private void UpdateBounceDeployment()
    {
        float step = Time.fixedDeltaTime;
        deploymentAge += step;
        if (deploymentAge >= Mathf.Max(0.1f, deploymentTimeout))
        {
            PlayFeedback(expireFeedbackData, transform.position);
            ReturnToPool();
            return;
        }
        if (BouncePhase == BounceDeploymentPhase.ActivationWait)
        {
            velocity = Vector2.zero;
            bouncePhaseAge += step;
            if (bouncePhaseAge >= Mathf.Max(0.01f, activationWaitDuration))
            {
                BouncePhase = BounceDeploymentPhase.Active;
                bouncePhaseAge = bounceActiveAge = 0f;
                velocity = new Vector2(deploymentDirection * bounceHorizontalSpeed, 0f);
            }
            UpdateVisual();
            return;
        }

        bool rising = BouncePhase == BounceDeploymentPhase.VerticalRise;
        if (rising)
        {
            bouncePhaseAge += step;
            velocity = new Vector2(0f, deploymentRiseBlocked ? 0f : Mathf.Max(0f, velocity.y - activeGravity * step));
        }
        else
        {
            bouncePhaseAge += step;
            velocity = new Vector2(0f, velocity.y - activeGravity * step);
        }
        // 준비 중에도 이동·벽·바닥 판정은 기존 MovingEntityBase와 모터가 담당한다.
        base.FixedUpdate();
        if (!initialized || isReturning) return;
        if (rising && BouncePhase == BounceDeploymentPhase.VerticalRise
            && bouncePhaseAge >= Mathf.Max(0.01f, deploymentRiseDuration))
            BeginBounceActivationWait();
        UpdateVisual();
    }

    private void BeginBounceActivationWait()
    {
        if (BouncePhase != BounceDeploymentPhase.VerticalRise) return;
        BouncePhase = BounceDeploymentPhase.ActivationWait;
        bouncePhaseAge = 0f;
        velocity = Vector2.zero;
        PlayFeedback(activationFeedbackData, transform.position);
    }

    protected override void HandleCollision(RaycastCollisionInfo2D info)
    {
        if (!initialized || isReturning || bombWarningActive) return;
        if (!(info.left || info.right || info.up || info.down)) return;
        if (activeKind == ConstructorCubeKind.Bounce)
        {
            if (autonomousBounce && BouncePhase != BounceDeploymentPhase.Active)
            {
                if (BouncePhase == BounceDeploymentPhase.Drop && info.down && velocity.y <= 0f)
                {
                    BouncePhase = BounceDeploymentPhase.VerticalRise;
                    bouncePhaseAge = 0f;
                    deploymentRiseBlocked = false;
                    velocity = new Vector2(0f, activeGravity * Mathf.Max(0.01f, deploymentRiseDuration));
                    GroundBounceCount++;
                    squashAge = 0f;
                    PlayFeedback(bounceFeedbackData, SurfaceContact(info));
                }
                else if (BouncePhase == BounceDeploymentPhase.VerticalRise && info.up && velocity.y > 0f)
                {
                    // 낮은 천장에서는 모터의 충돌 위치에 머물다가 정해진 예열 시각에 작동한다.
                    velocity = Vector2.zero;
                    deploymentRiseBlocked = true;
                }
                return;
            }
            // 천장에서는 반사하지 않는다. 벽은 수평 속도만, 지면은 수직 속도만 바꾼다.
            bool bounced = false;
            bool wallReflected = false;
            if (info.left && velocity.x < 0f) { velocity.x = Mathf.Abs(velocity.x); WallReflectionCount++; bounced = wallReflected = true; }
            else if (info.right && velocity.x > 0f) { velocity.x = -Mathf.Abs(velocity.x); WallReflectionCount++; bounced = wallReflected = true; }
            if (info.up && velocity.y > 0f) velocity.y = 0f;
            if (info.down && velocity.y <= 0f)
            {
                float direction = Mathf.Abs(velocity.x) > 0.001f ? Mathf.Sign(velocity.x)
                    : initialBounceDirection;
                velocity.x = direction * bounceHorizontalSpeed;
                velocity.y = bounceVerticalSpeed;
                GroundBounceCount++;
                squashAge = 0f;
                bounced = true;
            }
            // 모서리에서 벽과 바닥을 함께 맞아도 반동 피드백은 한 번만 재생한다.
            if (bounced) PlayFeedback(wallReflected && wallBounceFeedbackData != null
                ? wallBounceFeedbackData : bounceFeedbackData, SurfaceContact(info));
            return;
        }

        if (activeKind == ConstructorCubeKind.Shockwave)
        {
            if (info.down && velocity.y <= 0f)
            {
                resolved = true;
                Vector2 contact = SurfaceContact(info);
                if (activeWavePrefab != null && WorldEntityPool.Instance != null)
                {
                    var wave = WorldEntityPool.Instance.Spawn(activeWavePrefab, contact, Quaternion.identity);
                    wave.InitializeWave(owner, activeExplosionDamage, collideLayer);
                }
                PlayFeedback(groundImpactFeedbackData != null ? groundImpactFeedbackData : hitFeedbackData, contact);
                ReturnToPool();
            }
            else
            {
                // 벽·천장 충돌을 지면 충돌로 취급하지 않는다. 옆으로 막히면 그 자리에서 낙하한다.
                if (info.left || info.right) velocity.x = 0f;
                if (info.up && velocity.y > 0f) velocity.y = 0f;
            }
            return;
        }

        // 벽에 도착하기 전 경로에 있던 직접 타격을 먼저 확정한다.
        CheckMovementHits();
        if (initialized && !isReturning && !bombWarningActive) BeginBombWarning(SurfaceContact(info));
    }

    protected override void HitTarget(HealthBase health, Vector2 hitPoint)
    {
        if (!initialized || resolved || isReturning || bombWarningActive) return;
        if (activeKind == ConstructorCubeKind.Bomb) BeginBombWarning(hitPoint, health);
        else if (activeKind == ConstructorCubeKind.Bounce)
        {
            if (autonomousBounce && BouncePhase != BounceDeploymentPhase.Active) return;
            if (bounceHitTimes.TryGetValue(health, out float nextHitAt) && age < nextHitAt) return;
            bounceHitTimes[health] = age + bounceHitInterval;
            ApplyHitDamage(health, hitPoint);
        }
        // 충격파 큐브의 비행에는 접촉 피해가 없다. 피해는 착지 뒤 지면 파도가 담당한다.
    }

    protected override void OnTriggerEnter2D(Collider2D collision)
    {
        if (initialized && !isReturning && !bombWarningActive) base.OnTriggerEnter2D(collision);
    }

    private Vector2 SurfaceContact(RaycastCollisionInfo2D info)
    {
        RaycastMotor2D.InvalidateAttackQuerySync();
        RaycastMotor2D.PrepareAttackQueries();
        Vector2 direction = info.down ? Vector2.down : info.up ? Vector2.up
            : info.left ? Vector2.left : Vector2.right;
        float extent = info.down || info.up ? col.bounds.extents.y : col.bounds.extents.x;
        var contact = motor.RaycastEnvironment(col.bounds.center, direction, extent + 0.2f);
        return contact.collider != null ? contact.point : (Vector2)transform.position;
    }

    private void BeginBombWarning(Vector2 center, HealthBase directTarget = null)
    {
        if (!initialized || resolved || isReturning || bombWarningActive) return;
        bombWarningActive = true;
        warningAge = 0f;
        warningCenter = center;
        warningDirectTarget = directTarget;
        velocity = Vector2.zero;
        if (col != null) col.enabled = false;
        if (trail != null) trail.emitting = false;
        if (activeExplosionRadius > 0f && explosionRangePrefab != null && WorldEntityPool.Instance != null)
        {
            warningRange = WorldEntityPool.Instance.Spawn(explosionRangePrefab, center, Quaternion.identity);
            warningRangeLease = warningRange.LeaseId;
            // 표시의 Update가 피해의 FixedUpdate보다 먼저 끝나지 않도록 두 틱만 여유를 둔다.
            // 실제 경고는 폭발·취소가 종료하고, 소유자가 사라져도 표시 자체의 수명은 유한하다.
            float displayDuration = Mathf.Max(0.01f, bombWarningDuration) + Time.fixedDeltaTime * 2f;
            warningRange.InitializeWarning(activeExplosionRadius, displayDuration, owner);
        }
    }

    private void Detonate(Vector2 center, HealthBase directTarget = null)
    {
        if (!initialized || resolved || isReturning) return;
        resolved = true;
        if (activeExplosionRadius > 0f && explosionRangePrefab != null && WorldEntityPool.Instance != null)
        {
            var range = OwnsWarningRange() ? warningRange
                : WorldEntityPool.Instance.Spawn(explosionRangePrefab, center, Quaternion.identity);
            warningRange = null;
            warningRangeLease = 0;
            range.InitializeRange(activeExplosionRadius, 0.45f, owner);
        }
        damagedTargets.Clear();
        if (activeExplosionRadius > 0f && activeExplosionDamage.damage > 0f)
        {
            RaycastMotor2D.InvalidateAttackQuerySync();
            motor.OverlapAttackCircle(center, activeExplosionRadius, explosionFilter, explosionHits);
            foreach (var hit in explosionHits)
            {
                if (hit == null) continue;
                var health = hit.GetComponentInParent<HealthBase>();
                if (health == null || !damagedTargets.Add(health)) continue;
                // 예고 중 원 밖으로 피한 접촉 대상은 맞지 않는다. 폭발 시점의 겹침만 소비한다.
                DamageTarget(health, health == directTarget ? damageInfo : activeExplosionDamage, hit.ClosestPoint(center));
            }
        }
        PlayFeedback(hitFeedbackData, center);
        ReturnToPool();
    }

    private void DamageTarget(HealthBase health, WeaponBaseSO.DamageInfo hitDamage, Vector2 point)
    {
        if (health == null || health.IsInvincible || hitDamage.damage <= 0f) return;
        if (owner != null && (health == owner.GetComponentInParent<HealthBase>()
            || health.transform.IsChildOf(owner.transform))) return;
        if (ownerFaction == Faction.Player && health is EnemyHealth enemy)
        {
            if (enemy.enemyShieldSystem != null && enemy.enemyShieldSystem.IsSpawnLocked) return;
            if (enemy.enemyShieldSystem == null) enemy.TakeDamage(hitDamage, point);
            else enemy.enemyShieldSystem.TakeDamage(hitDamage, point);
            OnHitEnemy?.Invoke(owner, point);
        }
        else if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
            player.TakeDamage(hitDamage, point);
    }

    private static void PlayFeedback(FeedbackDataSO feedback, Vector2 position)
    {
        if (feedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.Play(feedback, position);
    }

    private void ResetPayload()
    {
        activeKind = cubeKind;
        activeGravity = Mathf.Max(0.1f, gravity);
        activeLifeTime = Mathf.Max(0.1f, lifeTime);
        activeExplosionRadius = Mathf.Max(0f, explosionRadius);
        activeExplosionDamage = explosionDamageInfo;
        activeWavePrefab = wavePrefab;
    }

    private void ResetBounceDeployment()
    {
        autonomousBounce = deploymentRiseBlocked = false;
        deploymentDirection = 1;
        BouncePhase = BounceDeploymentPhase.None;
        bouncePhaseAge = bounceActiveAge = deploymentAge = 0f;
    }

    private void UpdateVisual()
    {
        if (visualRoot == null) return;
        visualRoot.localRotation = activeKind == ConstructorCubeKind.Bounce ? restingVisualRotation
            : restingVisualRotation * Quaternion.Euler(0f, 0f,
                -Mathf.Sign(initialVelocity.x == 0f ? 1f : initialVelocity.x) * rotationSpeed * age);
        if (activeKind != ConstructorCubeKind.Bounce || squashAge >= bounceSquashDuration)
        {
            visualRoot.localScale = restingVisualScale;
            visualRoot.localPosition = restingVisualPosition;
            return;
        }

        float progress = Mathf.Clamp01(squashAge / Mathf.Max(0.01f, bounceSquashDuration));
        float width, height;
        if (progress < 0.4f)
        {
            float recovery = Mathf.SmoothStep(0f, 1f, progress / 0.4f);
            width = Mathf.Lerp(bounceSquashWidth, 0.92f, recovery);
            height = Mathf.Lerp(bounceSquashHeight, 1.12f, recovery);
        }
        else
        {
            float settle = Mathf.SmoothStep(0f, 1f, (progress - 0.4f) / 0.6f);
            width = Mathf.Lerp(0.92f, 1f, settle);
            height = Mathf.Lerp(1.12f, 1f, settle);
        }
        // Collider는 원형 그대로 두고 외형의 바닥만 그 원의 하단에 맞춰 눌렀다 편다.
        visualRoot.localScale = Vector3.Scale(restingVisualScale, new Vector3(width, height, 1f));
        visualRoot.localPosition = restingVisualPosition + Vector3.up * (bounceVisualHalfHeight * (height - 1f));
    }

    private void ResetVisual()
    {
        squashAge = bounceSquashDuration;
        if (visualRoot == null) return;
        visualRoot.localRotation = restingVisualRotation;
        visualRoot.localScale = restingVisualScale;
        visualRoot.localPosition = restingVisualPosition;
    }

    protected override void ReturnToPool()
    {
        if (isReturning) return;
        initialized = false;
        velocity = Vector2.zero;
        ClearWarning();
        base.ReturnToPool();
    }

    public override void ForceReturnToPool()
    {
        if (isReturning) return;
        base.ForceReturnToPool();
    }

    private void OnDisable()
    {
        initialized = false;
        velocity = Vector2.zero;
        autonomousBounce = deploymentRiseBlocked = false;
        BouncePhase = BounceDeploymentPhase.None;
        ClearWarning();
        if (trail != null)
        {
            trail.emitting = false;
            trail.Clear();
        }
        ResetVisual();
    }

    private void OnDestroy()
    {
        ClearWarning();
        if (trail != null && !trail.transform.IsChildOf(transform)) Destroy(trail.gameObject);
    }

    private bool OwnsWarningRange() => warningRange != null && warningRange.gameObject.activeSelf
        && warningRange.LeaseId == warningRangeLease && warningRange.IsWarning;

    private void ClearWarning()
    {
        if (OwnsWarningRange() && WorldEntityPool.Instance != null) warningRange.ForceReturnToPool();
        warningRange = null;
        warningRangeLease = 0;
        bombWarningActive = false;
        warningAge = 0f;
        warningCenter = Vector2.zero;
        warningDirectTarget = null;
        if (stationaryVisualHidden && visualRoot != null) visualRoot.gameObject.SetActive(stationaryVisualWasActive);
        stationaryVisualHidden = false;
        stationaryVisualWasActive = false;
        if (col != null) col.enabled = restingColliderEnabled;
    }
}
