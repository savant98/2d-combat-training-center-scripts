using UnityEngine;

// Player Hammer 반격(Counter) 투사체 반사 전용 투사체.
// - 레일건 StarSpear의 '짧은 조준·당김(Hold) → 직선 사출 + 고속 경로 궤적(RecordPath: 0.4초 가는 빛 선 + 0.045초 굵은 빛 번짐 + 연기 + 불꽃)' 연출과
//   별창 동일 속도(150칸/초)를 사용하되, 유도 선회 없는 순수 직선 비행과 Hammer 반격 고유의 모루형 기어 볼트 외형·청록/자홍 팔레트·충격 피드백을 유지한다.
// - 이동·벽 충돌·적 판정은 ProjectileBase / MovingEntityBase(RaycastMotor2D) 경로를 그대로 따른다.
// - 반격 성공 직후 이미 생성·예약된 투사체는 Player가 회수 중 대시해도 독립적으로 사출된다(기존 충격파 규칙과 동일).
[DefaultExecutionOrder(105)]
public class CounterReflectShot : ProjectileBase
{
    public enum ShotState { Held, Flight, Done }

    [Header("Presentation")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Transform body;
    [SerializeField] private FeedbackDataSO fireFeedbackData;
    [SerializeField] private FeedbackDataSO emptyHitFeedbackData;
    [SerializeField, Min(0.01f)] private float appearTime = 0.07f;
    [SerializeField, Min(1f)] private float appearOvershoot = 1.18f;
    [SerializeField, Min(0f)] private float aimPullBack = 0.22f;

    [Header("Flight Trail")]
    [SerializeField] private TrailRenderer streak;
    [SerializeField] private ParticleSystem smoke;
    [SerializeField] private ParticleSystem sparks;
    [SerializeField, Min(0.05f)] private float smokeSpacing = 0.45f;
    [SerializeField, Min(0f)] private float smokeJitter = 0.06f;
    [SerializeField] private Vector2 smokeDrift = new Vector2(0.4f, 1.2f);
    [SerializeField, Min(0)] private int launchSmokeCount = 4;
    [SerializeField, Min(1f)] private float launchSmokeScale = 1.6f;
    [SerializeField, Min(0.05f)] private float sparkSpacing = 0.9f;
    [SerializeField] private Vector2 sparkSpeed = new Vector2(2.5f, 6f);

    public ShotState State { get; private set; } = ShotState.Held;
    public Vector2 FlightDirection => flightDirection;
    public float Speed => speed;
    public float FlightAge => flightAge;
    public float TraveledDistance => traveledDistance;
    public CharacterController2D TargetEnemy => targetEnemy;
    public TrailRenderer PathTrail => trail;
    public TrailRenderer StreakTrail => streak;

    private CharacterController2D targetEnemy;
    private Collider2D targetCollider;
    private Vector2 holdOrigin;
    private Vector2 fallbackDirection = Vector2.right;
    private Vector2 flightDirection = Vector2.right;
    private float holdDuration = 0.1f;
    private float heldAge;
    private float flightAge;
    private float maxFlightTime = 1.3f;
    private float maxFlightDistance = 45f;
    private float traveledDistance;
    private Vector3 bodyScale = Vector3.one;
    private Vector2 lastPathPoint;
    private float smokeDistance;
    private float sparkDistance;

    protected override bool UsesPathHitDetection => State == ShotState.Flight;
    public override bool CanBeCounterReflected => false;

    protected override void Awake()
    {
        base.Awake();
        if (visualRoot == null) visualRoot = transform.Find("Visual");
        if (body == null && visualRoot != null) body = visualRoot.Find("Body");
        if (body != null) bodyScale = body.localScale;
        if (trail != null)
        {
            if (streak == null) streak = FindIn<TrailRenderer>(trail.transform, "Streak");
            if (smoke == null) smoke = FindIn<ParticleSystem>(trail.transform, "Smoke");
            if (sparks == null) sparks = FindIn<ParticleSystem>(trail.transform, "Sparks");
        }
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        State = ShotState.Held;
        targetEnemy = null;
        targetCollider = null;
        holdOrigin = Vector2.zero;
        fallbackDirection = Vector2.right;
        flightDirection = Vector2.right;
        holdDuration = 0.1f;
        heldAge = 0f;
        flightAge = 0f;
        traveledDistance = 0f;
        velocity = Vector2.zero;
        if (col != null) col.enabled = false;
        if (body != null) body.localScale = Vector3.zero;
        if (streak != null) streak.Clear();
        if (smoke != null) smoke.Clear();
        if (sparks != null) sparks.Clear();
    }

    public override void Initialize(CharacterController2D owner, WeaponBaseSO.DamageInfo damageInfo,
        float speed, int count, LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);
        if (col != null) col.enabled = false;
        if (trail != null) trail.emitting = false;
        if (streak != null)
        {
            streak.emitting = false;
            streak.Clear();
        }
    }

    /// <summary>
    /// 반격 투사체를 포획 지점에서 생성·조준 대기(holdDelay) 후 목표 적을 향해 일직선으로 사출한다.
    /// </summary>
    public void ArmAndLaunch(
        Vector2 spawnPosition,
        CharacterController2D target,
        Vector2 fallbackDir,
        float holdDelay,
        float maxDistance)
    {
        holdOrigin = spawnPosition;
        transform.position = spawnPosition;
        targetEnemy = target;
        targetCollider = ResolveTargetCollider(target);
        fallbackDirection = fallbackDir.sqrMagnitude > 0.0001f ? fallbackDir.normalized : Vector2.right;
        holdDuration = Mathf.Max(0f, holdDelay);
        maxFlightDistance = Mathf.Max(2f, maxDistance);
        maxFlightTime = maxFlightDistance / Mathf.Max(1f, speed);
        heldAge = 0f;
        flightAge = 0f;
        traveledDistance = 0f;
        State = ShotState.Held;

        flightDirection = ComputeAimDirection(holdOrigin);
        SetVisualAngle(DirectionAngle(flightDirection));

        if (holdDuration <= 0.0001f)
            Launch();
    }

    private void Update()
    {
        if (State != ShotState.Held || body == null) return;
        float t = Mathf.Clamp01(heldAge / Mathf.Max(0.01f, appearTime));
        float s = t < 0.7f
            ? Mathf.Lerp(0f, appearOvershoot, 1f - Mathf.Pow(1f - t / 0.7f, 3f))
            : Mathf.Lerp(appearOvershoot, 1f, (t - 0.7f) / 0.3f);
        body.localScale = bodyScale * s;
    }

    protected override void FixedUpdate()
    {
        if (isReturning || State == ShotState.Done) return;

        float step = Time.fixedDeltaTime;
        if (State == ShotState.Held)
        {
            heldAge += step;
            flightDirection = ComputeAimDirection(holdOrigin);
            float pull = holdDuration > 0.0001f ? Smooth01(heldAge / holdDuration) : 1f;
            transform.position = holdOrigin - flightDirection * (aimPullBack * pull);
            SetVisualAngle(DirectionAngle(flightDirection));
            if (heldAge >= holdDuration - 0.0001f)
                Launch();
            return;
        }

        flightAge += step;
        if (flightAge >= maxFlightTime - 0.0001f || traveledDistance >= maxFlightDistance)
        {
            Expire();
            return;
        }

        Vector2 before = transform.position;
        velocity = flightDirection * speed;
        SetVisualAngle(DirectionAngle(flightDirection));
        base.FixedUpdate();
        if (State == ShotState.Flight && !isReturning)
        {
            Vector2 after = transform.position;
            traveledDistance += Vector2.Distance(before, after);
            RecordPath(after);
        }
    }

    private void Launch()
    {
        if (State != ShotState.Held || isReturning) return;
        State = ShotState.Flight;
        flightDirection = ComputeAimDirection(transform.position);
        flightAge = 0f;
        traveledDistance = 0f;
        velocity = flightDirection * speed;
        if (col != null) col.enabled = true;
        RaycastMotor2D.InvalidateAttackQuerySync();
        if (body != null) body.localScale = bodyScale;

        float angle = DirectionAngle(flightDirection);
        SetVisualAngle(angle);
        BeginPath(transform.position);

        if (fireFeedbackData != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.PlayExact(fireFeedbackData, transform.position, Quaternion.Euler(0f, 0f, angle));
    }

    private Vector2 ComputeAimDirection(Vector2 from)
    {
        if (IsTargetAlive())
        {
            Vector2 aimPoint = targetCollider != null ? (Vector2)targetCollider.bounds.center : (Vector2)targetEnemy.transform.position;
            Vector2 delta = aimPoint - from;
            if (delta.sqrMagnitude > 0.0001f)
                return delta.normalized;
        }
        return fallbackDirection;
    }

    private bool IsTargetAlive()
    {
        if (targetEnemy == null || !targetEnemy.gameObject.activeInHierarchy) return false;
        if (targetEnemy is EnemyController enemy)
            return enemy.Health != null && enemy.Health.currentHP > 0f;
        var health = targetEnemy.GetComponentInChildren<HealthBase>(true);
        return health != null && health.currentHP > 0f;
    }

    private static Collider2D ResolveTargetCollider(CharacterController2D target)
    {
        if (target == null) return null;
        if (target is EnemyController enemy && enemy.Health != null)
        {
            var col = enemy.Health.GetComponent<Collider2D>();
            if (col != null) return col;
        }
        return target.GetComponentInChildren<Collider2D>();
    }

    protected override void HitTarget(HealthBase health, Vector2 hitPoint)
    {
        if (State != ShotState.Flight) return;
        State = ShotState.Done;
        RecordPath(hitPoint);
        FinishPath();
        ApplyHitDamage(health, hitPoint);
        ReturnToPool();
    }

    protected override void OnHitSomething()
    {
        if (State != ShotState.Flight) return;
        CheckMovementHits();
        if (isReturning || State != ShotState.Flight) return;
        State = ShotState.Done;
        RecordPath(transform.position);
        FinishPath();
        if (hitFeedbackData != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.Play(hitFeedbackData, transform.position);
        ReturnToPool();
    }

    private void Expire()
    {
        if (State == ShotState.Done) return;
        State = ShotState.Done;
        RecordPath(transform.position);
        FinishPath();
        if (emptyHitFeedbackData != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.Play(emptyHitFeedbackData, transform.position);
        ReturnToPool();
    }

    protected override void ReturnToPool()
    {
        FinishPath();
        base.ReturnToPool();
    }

    // ===== 비행 자국(StarSpear.RecordPath 방식 — 150칸/초 직선 비행 궤적 + 빛 번짐 + 연기 + 불꽃) =====
    private void BeginPath(Vector2 start)
    {
        lastPathPoint = start;
        smokeDistance = smokeSpacing * 0.5f;
        sparkDistance = 0f;
        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
            trail.AddPosition(start);
        }
        if (streak != null)
        {
            streak.Clear();
            streak.emitting = true;
            streak.AddPosition(start);
        }
        if (smoke != null) smoke.Play();
        if (sparks != null) sparks.Play();
        EmitLaunchSmoke(start);
    }

    private void RecordPath(Vector2 end)
    {
        Vector2 delta = end - lastPathPoint;
        float length = delta.magnitude;
        if (length < 0.0001f) return;
        if (trail != null) trail.AddPosition(end);
        if (streak != null) streak.AddPosition(end);
        Vector2 forward = delta / length;
        Vector2 normal = new Vector2(-forward.y, forward.x);
        if (smoke != null && smokeSpacing > 0f) EmitAlong(smoke, lastPathPoint, forward, normal, length, smokeSpacing, ref smokeDistance, true);
        if (sparks != null && sparkSpacing > 0f) EmitAlong(sparks, lastPathPoint, forward, normal, length, sparkSpacing, ref sparkDistance, false);
        lastPathPoint = end;
    }

    private void FinishPath()
    {
        if (trail != null) trail.emitting = false;
        if (streak != null) streak.emitting = false;
    }

    private void EmitLaunchSmoke(Vector2 start)
    {
        if (smoke == null || launchSmokeCount <= 0) return;
        Vector2 normal = new Vector2(-flightDirection.y, flightDirection.x);
        var main = smoke.main;
        float sizeMin = main.startSize.mode == ParticleSystemCurveMode.TwoConstants ? main.startSize.constantMin : main.startSize.constant;
        float sizeMax = main.startSize.mode == ParticleSystemCurveMode.TwoConstants ? main.startSize.constantMax : main.startSize.constant;
        for (int i = 0; i < launchSmokeCount; i++)
        {
            float spread = (i - (launchSmokeCount - 1) * 0.5f) * 0.16f;
            var p = new ParticleSystem.EmitParams
            {
                position = start + normal * spread - flightDirection * (0.06f * i),
                velocity = normal * (spread * 3.5f) + flightDirection * Random.Range(-0.5f, 0.35f),
                startSize = Random.Range(sizeMin, sizeMax) * launchSmokeScale,
                applyShapeToPosition = false,
            };
            smoke.Emit(p, 1);
        }
    }

    private void EmitAlong(ParticleSystem system, Vector2 start, Vector2 forward, Vector2 normal, float length, float spacing, ref float carry, bool isSmoke)
    {
        float d = spacing - carry;
        while (d <= length)
        {
            Vector2 point = start + forward * d;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            if (isSmoke)
            {
                p.position = point + normal * Random.Range(-smokeJitter, smokeJitter);
                p.velocity = forward * Random.Range(smokeDrift.x, smokeDrift.y) + normal * Random.Range(-0.35f, 0.35f);
            }
            else
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                p.position = point;
                p.velocity = (normal * side + forward * Random.Range(0.2f, 0.7f)).normalized * Random.Range(sparkSpeed.x, sparkSpeed.y);
            }
            system.Emit(p, 1);
            d += spacing;
        }
        carry = length - (d - spacing);
    }

    private void SetVisualAngle(float degrees)
    {
        if (visualRoot != null)
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, degrees);
    }

    private static float DirectionAngle(Vector2 dir) => Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private static T FindIn<T>(Transform root, string name) where T : Component
    {
        var child = root.Find(name);
        return child != null ? child.GetComponent<T>() : null;
    }
}
