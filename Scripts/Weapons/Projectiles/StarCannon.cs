using System.Collections.Generic;
using UnityEngine;

// StarCannon 별 창(2026-11-08 재디자인, 사용자 요청 "머리 위에 창 5개를 부채꼴로 0.2초 간격으로 생성한 뒤, 일정 시간 후 적을 추적해서
// 날아가 적이나 벽에 닿으면 폭발").
// - 발사 전(Held): StarCannonVolley가 매 고정 단계 Hold로 자리·창끝 방향을 정한다. 판정 Collider를 꺼 두고 모터 이동·경로 판정을 하지 않는다.
// - 비행(Flight): Launch 방향으로 일정 속도로 날아가며, 목표 쪽으로 초당 최대 turnRateDegrees만큼만 돈다(최소 선회 반경 = 속도 / 각속도).
//   목표를 잃으면 retargetInterval마다 가장 가까운 적을 다시 찾는다. 이동·벽 충돌은 MovingEntityBase(RaycastMotor2D.Move),
//   적 판정은 ProjectileBase의 Collider 경로·겹침 질의를 그대로 쓴다.
// - 폭발: 적에 닿거나(경로 판정), 벽에 닿거나(모터 충돌), 비행 시간이 끝나면 그 자리에서 RaycastMotor2D.OverlapAttackCircle로
//   폭발 반경 안의 적마다 한 번 피해를 준다(닿은 적도 반경 안이라 따로 피해를 주지 않는다).
// - 외형: 판정 기준(루트)은 돌리지 않고 Visual 자식만 창끝 방향으로 돌린다. 루트(판정 원)는 창끝 쪽에 있다.
// - 비행 자국(3차, 사용자 요청 "아주 빠른 공격 — 지나간 자리를 선으로 보여주고, 추가 이펙트와 연기 자국"): 아주 빠른 창은 고정 단계마다
//   여러 칸씩 움직여 프레임마다 보이는 자리가 띄엄띄엄하다. 그래서 고정 단계마다 지나간 점을 궤적에 직접 더하고(AddPosition)
//   그 구간을 따라 연기·불꽃을 일정 간격으로 뿌린다(월드 공간). Trail(길 전체를 남기는 선)의 자식인 Streak(창 뒤의 짧은 빛 번짐)·
//   Smoke(연기 자국)·Sparks(불꽃)는 ProjectileBase가 풀로 돌아갈 때 Trail과 함께 떼어 두므로 폭발 뒤에도 남았다가 사라진다.
//   TrailRenderer는 emitting이 꺼진 동안 들어온 점을 끊긴 구간으로 보고 그리지 않으므로 비행 중에는 emitting을 켜 두고,
//   자동 점 생성은 Prefab의 minVertexDistance를 아주 크게 해 막는다(점은 모두 RecordPath가 넣음). 폭발 지점을 넣은 뒤 emitting을 끄면 남은 선은 그대로 보인다.
public class StarCannon : ProjectileBase
{
    public enum SpearState { Held, Flight, Done }
    public enum DetonationCause { None, Enemy, Wall, Expired }

    [Header("Presentation")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Transform body;
    [SerializeField] private FeedbackDataSO fireFeedbackData;
    [SerializeField, Min(0.01f)] private float appearTime = 0.15f;
    [SerializeField, Min(1f)] private float appearOvershoot = 1.15f;

    [Header("Guidance")]
    [SerializeField, Min(0.01f)] private float retargetInterval = 0.1f;

    [Header("Flight Trail")]
    [SerializeField] private TrailRenderer streak;           // 창 뒤의 짧은 빛 번짐(Trail 자식)
    [SerializeField] private ParticleSystem smoke;            // 지나간 길의 연기 자국(Trail 자식, 월드 공간)
    [SerializeField] private ParticleSystem sparks;           // 지나간 길에서 옆으로 튀는 불꽃(Trail 자식, 월드 공간)
    [SerializeField, Min(0.05f)] private float smokeSpacing = 0.45f;
    [SerializeField, Min(0f)] private float smokeJitter = 0.08f;
    [SerializeField] private Vector2 smokeDrift = new Vector2(0.3f, 1.2f);   // 연기가 비행 방향으로 밀리는 처음 속도(칸/초) 범위
    [SerializeField, Min(0)] private int launchSmokeCount = 4;               // 발사 자리에 한 번에 피어나는 연기
    [SerializeField, Min(1f)] private float launchSmokeScale = 1.6f;
    [SerializeField, Min(0.05f)] private float sparkSpacing = 0.9f;
    [SerializeField] private Vector2 sparkSpeed = new Vector2(2f, 6f);        // 불꽃이 옆으로 튀는 속도(칸/초) 범위

    public SpearState State { get; private set; } = SpearState.Held;
    public DetonationCause LastCause { get; private set; }
    public Vector2 LastDetonationPoint { get; private set; }
    public Vector2 FlightDirection => flightDirection;
    public float FlightAge => flightAge;
    public float Speed => speed;
    public float TurnRateDegrees => turnRateDegrees;
    public float ExplosionRadius => explosionRadius;
    public Collider2D TargetCollider => targetCollider;
    public bool IsHeld => State == SpearState.Held && !isReturning && gameObject.activeInHierarchy;
    public TrailRenderer PathTrail => trail;      // 검증·디버그용: 길 전체 선
    public TrailRenderer StreakTrail => streak;   // 검증·디버그용: 창 뒤 빛 번짐
    public ParticleSystem SmokeTrail => smoke;
    public ParticleSystem SparkTrail => sparks;

    private Vector2 flightDirection = Vector2.right;
    private float flightAge, heldAge, retargetTimer;
    private float turnRateDegrees = 452.3f, maxFlightTime = 0.24f, explosionRadius = 3.75f, searchRadius = 16f;
    private Collider2D targetCollider;
    private ContactFilter2D enemyFilter;
    private readonly List<Collider2D> queryHits = new List<Collider2D>(16);
    private readonly HashSet<HealthBase> damagedTargets = new HashSet<HealthBase>();
    private Vector3 bodyScale = Vector3.one;
    private Vector2 lastPathPoint;
    private float smokeDistance, sparkDistance;   // 마지막으로 연기·불꽃을 뿌린 뒤 지나온 거리

    protected override bool UsesPathHitDetection => State == SpearState.Flight;

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
        State = SpearState.Held;
        LastCause = DetonationCause.None;
        flightAge = heldAge = retargetTimer = 0f;
        flightDirection = Vector2.right;
        velocity = Vector2.zero;
        SetTargetCollider(null);
        damagedTargets.Clear();
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
        enemyFilter = new ContactFilter2D();
        enemyFilter.SetLayerMask(1 << LayerMask.NameToLayer(ownerFaction == Faction.Player ? "Enemy" : "Player"));
        enemyFilter.useTriggers = true;
        if (col != null) col.enabled = false;
        // 발사 전에는 궤적을 남기지 않는다. 비행 중에는 고정 단계마다 RecordPath가 점을 더한다.
        if (trail != null) trail.emitting = false;
        if (streak != null)
        {
            streak.emitting = false;
            streak.Clear();
        }
    }

    public void ConfigureFlight(float turnRateDegrees, float maxFlightTime, float explosionRadius, float searchRadius)
    {
        this.turnRateDegrees = Mathf.Max(0f, turnRateDegrees);
        this.maxFlightTime = Mathf.Max(0.01f, maxFlightTime);
        this.explosionRadius = Mathf.Max(0f, explosionRadius);
        this.searchRadius = Mathf.Max(0f, searchRadius);
    }

    // 발사 전: 자리와 창끝 방향(도)을 정한다. 물리 판정 없이 외형만 놓는다.
    public void Hold(Vector2 position, float angleDegrees)
    {
        if (State != SpearState.Held || isReturning) return;
        transform.position = position;
        SetVisualAngle(angleDegrees);
    }

    // 발사: 이 순간의 방향으로 날아가기 시작하고, 목표(없으면 null)를 향해 선회한다.
    public void Launch(Vector2 direction, Collider2D target)
    {
        if (State != SpearState.Held || isReturning) return;
        State = SpearState.Flight;
        flightDirection = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector2.right;
        flightAge = 0f;
        retargetTimer = retargetInterval;
        SetTargetCollider(target);
        velocity = flightDirection * speed;
        if (col != null) col.enabled = true;
        RaycastMotor2D.InvalidateAttackQuerySync();
        if (body != null) body.localScale = bodyScale;
        float angle = DirectionAngle();
        SetVisualAngle(angle);
        BeginPath(transform.position);
        if (fireFeedbackData != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.PlayExact(fireFeedbackData, transform.position, Quaternion.Euler(0f, 0f, angle));
    }

    // from에서 radius 안의 적 가운데 판정 상자 중심이 가장 가까운 것(무적·생성 잠금 제외). 모터의 공격 범위 질의를 쓴다.
    public Collider2D FindNearestTarget(Vector2 from, float radius)
    {
        if (motor == null || radius <= 0f) return null;
        motor.OverlapAttackCircle(from, radius, enemyFilter, queryHits);
        Collider2D best = null;
        float bestDistance = float.MaxValue;
        foreach (var hit in queryHits)
        {
            if (!IsTrackable(hit)) continue;
            var health = hit.GetComponentInParent<HealthBase>();
            if (health == null || health.IsInvincible) continue;
            float distance = ((Vector2)hit.bounds.center - from).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = hit;
            }
        }
        return best;
    }

    // 계속 쫓을 수 있는 목표인가: 켜져 있고 체력이 있으며 생성 중이 아니다(잠깐의 무적은 놓치지 않는다).
    public static bool IsTrackable(Collider2D target)
    {
        if (target == null || !target.enabled || !target.gameObject.activeInHierarchy) return false;
        var health = target.GetComponentInParent<HealthBase>();
        if (health == null) return false;
        return !(health is EnemyHealth enemy && enemy.enemyShieldSystem != null && enemy.enemyShieldSystem.IsSpawnLocked);
    }

    private void Update()
    {
        if (State != SpearState.Held || body == null) return;
        // 소환: 빠르게 커졌다 조금 넘친 뒤 제자리
        heldAge += Time.deltaTime;
        float t = Mathf.Clamp01(heldAge / appearTime);
        float s = t < 0.7f ? Mathf.Lerp(0f, appearOvershoot, 1f - Mathf.Pow(1f - t / 0.7f, 3f))
                           : Mathf.Lerp(appearOvershoot, 1f, (t - 0.7f) / 0.3f);
        body.localScale = bodyScale * s;
    }

    protected override void FixedUpdate()
    {
        if (State != SpearState.Flight || isReturning) return;
        float step = Time.fixedDeltaTime;
        flightAge += step;
        if (flightAge >= maxFlightTime - 0.0001f)
        {
            Detonate(transform.position, DetonationCause.Expired);
            return;
        }
        Steer(step);
        velocity = flightDirection * speed;
        SetVisualAngle(DirectionAngle());
        base.FixedUpdate();
        // 적·벽에 닿아 폭발했으면 Detonate가 폭발 지점까지 기록했다.
        if (State == SpearState.Flight && !isReturning) RecordPath(transform.position);
    }

    private void Steer(float step)
    {
        if (!IsTrackable(targetCollider))
        {
            SetTargetCollider(null);
            retargetTimer -= step;
            if (retargetTimer > 0f) return;
            retargetTimer = retargetInterval;
            SetTargetCollider(FindNearestTarget(transform.position, searchRadius));
            if (targetCollider == null) return;
        }
        Vector2 delta = (Vector2)targetCollider.bounds.center - (Vector2)transform.position;
        if (delta.sqrMagnitude < 0.000001f) return;
        float desired = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        float angle = Mathf.MoveTowardsAngle(DirectionAngle(), desired, turnRateDegrees * step) * Mathf.Deg2Rad;
        flightDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    protected override void HitTarget(HealthBase health, Vector2 hitPoint)
    {
        Detonate(hitPoint, DetonationCause.Enemy);
    }

    protected override void OnHitSomething()
    {
        // 벽에 닿기 전 이동 구간의 적 판정을 먼저 처리한다(ProjectileBase와 같은 순서).
        CheckMovementHits();
        if (isReturning || State != SpearState.Flight) return;
        Detonate(transform.position, DetonationCause.Wall);
    }

    private void Detonate(Vector2 center, DetonationCause cause)
    {
        if (State == SpearState.Done || isReturning) return;
        bool flying = State == SpearState.Flight;
        State = SpearState.Done;
        LastCause = cause;
        LastDetonationPoint = center;
        velocity = Vector2.zero;
        if (flying) RecordPath(center);   // 선·연기가 폭발 지점까지 닿게 한다
        damagedTargets.Clear();
        if (motor != null && explosionRadius > 0f && damageInfo.damage > 0f)
        {
            RaycastMotor2D.InvalidateAttackQuerySync();
            motor.OverlapAttackCircle(center, explosionRadius, enemyFilter, queryHits);
            foreach (var hit in queryHits)
            {
                if (hit == null) continue;
                var health = hit.GetComponentInParent<HealthBase>();
                if (health == null || !damagedTargets.Add(health)) continue;
                DamageTarget(health, hit.ClosestPoint(center));
            }
        }
        if (hitFeedbackData != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.Play(hitFeedbackData, center);
        // 폭발 지점까지 넣은 점은 그대로 보이고, 떼어진 궤적이 더 이어지지 않게 한다
        if (trail != null) trail.emitting = false;
        if (streak != null) streak.emitting = false;
        ReturnToPool();
    }

    private void DamageTarget(HealthBase health, Vector2 hitPoint)
    {
        if (health.IsInvincible) return;
        if (owner != null && (health == owner.GetComponentInParent<HealthBase>() || health.transform.IsChildOf(owner.transform))) return;
        if (ownerFaction == Faction.Player && health is EnemyHealth enemy)
        {
            if (enemy.enemyShieldSystem != null && enemy.enemyShieldSystem.IsSpawnLocked) return;
            if (enemy.enemyShieldSystem == null) enemy.TakeDamage(damageInfo, hitPoint);
            else enemy.enemyShieldSystem.TakeDamage(damageInfo, hitPoint);
            OnHitEnemy?.Invoke(owner, hitPoint);
        }
        else if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
            player.TakeDamage(damageInfo, hitPoint);
    }

    // ===== 비행 자국 =====
    // 발사 자리에서 궤적을 새로 시작하고, 발사 자리에 연기를 한 번에 피운다.
    private void BeginPath(Vector2 start)
    {
        lastPathPoint = start;
        smokeDistance = smokeSpacing * 0.5f;   // 첫 연기는 출발 뒤 반 간격에서
        sparkDistance = 0f;
        Vector3 point = PathPoint(start);
        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
            trail.AddPosition(point);
        }
        if (streak != null)
        {
            streak.Clear();
            streak.emitting = true;
            streak.AddPosition(point);
        }
        if (sparks != null)
        {
            sparks.Clear();
            sparks.Play();
        }
        if (smoke == null) return;
        smoke.Clear();
        smoke.Play();
        for (int i = 0; i < launchSmokeCount; i++)
            EmitSmoke(start + Random.insideUnitCircle * 0.25f, -flightDirection, launchSmokeScale);
    }

    // 지난 기록 지점에서 to까지 지나간 길을 궤적에 더하고, 그 구간에 연기·불꽃을 간격마다 뿌린다.
    private void RecordPath(Vector2 to)
    {
        Vector2 from = lastPathPoint;
        lastPathPoint = to;
        Vector3 point = PathPoint(to);
        if (trail != null) trail.AddPosition(point);
        if (streak != null) streak.AddPosition(point);
        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.0001f) return;
        Vector2 direction = delta / length;
        if (smoke != null)
        {
            float s = smokeSpacing - smokeDistance;
            for (; s <= length; s += smokeSpacing) EmitSmoke(from + direction * s, direction, 1f);
            smokeDistance = length - (s - smokeSpacing);
        }
        if (sparks != null)
        {
            float s = sparkSpacing - sparkDistance;
            for (; s <= length; s += sparkSpacing) EmitSpark(from + direction * s, direction);
            sparkDistance = length - (s - sparkSpacing);
        }
    }

    private void EmitSmoke(Vector2 at, Vector2 direction, float sizeScale)
    {
        Vector2 side = new Vector2(-direction.y, direction.x);
        var p = new ParticleSystem.EmitParams
        {
            position = PathPoint(at + side * Random.Range(-smokeJitter, smokeJitter)),
            velocity = direction * Random.Range(smokeDrift.x, smokeDrift.y) + side * Random.Range(-0.25f, 0.25f),
        };
        if (sizeScale != 1f)
        {
            var size = smoke.main.startSize;
            p.startSize = Random.Range(size.constantMin, size.constantMax) * sizeScale;
        }
        smoke.Emit(p, 1);
    }

    private void EmitSpark(Vector2 at, Vector2 direction)
    {
        Vector2 side = new Vector2(-direction.y, direction.x) * (Random.value < 0.5f ? -1f : 1f);
        var p = new ParticleSystem.EmitParams
        {
            position = PathPoint(at),
            velocity = side * Random.Range(sparkSpeed.x, sparkSpeed.y) + direction * Random.Range(-1f, 3f),
        };
        sparks.Emit(p, 1);
    }

    private Vector3 PathPoint(Vector2 at) => new Vector3(at.x, at.y, transform.position.z);

    private void SetTargetCollider(Collider2D collider)
    {
        targetCollider = collider;
        target = collider != null ? collider.transform : null;
    }

    private float DirectionAngle() => Mathf.Atan2(flightDirection.y, flightDirection.x) * Mathf.Rad2Deg;

    private void SetVisualAngle(float angleDegrees)
    {
        if (visualRoot != null) visualRoot.rotation = Quaternion.Euler(0f, 0f, angleDegrees);
    }

    private static T FindIn<T>(Transform parent, string name) where T : Component
    {
        var child = parent.Find(name);
        return child != null ? child.GetComponent<T>() : null;
    }
}
