using System.Collections.Generic;
using UnityEngine;

// Player 돌격 드론(2026-11-09 재디자인, 사용자 요청 "AutoBreakDrone은 StarSpear의 것을 본따서 드론으로 만들고, 그만큼 속도도 빠르게").
// - 순환: Player를 따라다니다(Orbit) 탐지 범위 안의 가장 가까운 적을 조준하고(Windup) StarSpear 창과 같은 속도로 일직선 돌진한다(Execute). 적·벽에 닿거나 시간이 끝나면 폭발하고 돌아온다(Recover).
// - 판정: 고정 단계마다 1.5칸씩 움직이므로 겹침 콜백으로는 적을 지나친다. 이동은 지금처럼 MovingEntityBase(RaycastMotor2D.Move)를 쓰고,
//   이번 단계에 지나간 구간을 모터의 CastAttackPath로 훑어 적을 찾는다(ProjectileBase와 같은 경로 판정). 적을 먼저 판정한 뒤 벽을 처리한다.
// - 폭발(사용자 요청 "범위 폭발 부분은 가져오고"): StarSpear 창과 같은 반경으로 모터의 OverlapAttackCircle을 써서 반경 안의 적마다 한 번 피해를 준다(닿은 적도 반경 안이라 따로 피해를 주지 않는다). 드론은 사라지지 않고 돌아온다.
// - 비행 자국: DroneDashTrail이 StarSpear 창과 같은 선·빛 번짐·연기·불꽃을 남긴다.
public class AutoBreakDrone : DroneBase
{
    private enum DroneState
    {
        Orbit,
        Windup,
        Execute,
        Recover
    }

    [Header("Target")]
    [SerializeField] private float searchRange = 6f;
    [SerializeField] private LayerMask targetLayer;

    [Header("Aim")]
    [SerializeField] private float windupAimTurnSpeed = 360f;

    [Header("Orbit")]
    [SerializeField] private float orbitFollowSpeedMultiplier = 1f;

    [Header("Dash Cycle")]
    [SerializeField] private float attackInterval = 1.2f;
    [SerializeField] private float windupTime = 0.25f;
    [SerializeField] private float dashSpeed = 14f;
    [SerializeField] private float dashDuration = 0.35f;
    [SerializeField] private float recoverTime = 0.25f;

    [Header("Feedback")]
    [SerializeField] private FeedbackDataSO windupFeedback;
    [SerializeField] private FeedbackDataSO dashFeedback;
    [SerializeField] private FeedbackDataSO explosionFeedback;

    [Header("Explosion")]
    [SerializeField, Min(0f)] private float explosionRadius = 3.75f;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    [Header("Dash Trail")]
    [SerializeField] private DroneDashTrail dashTrail;

    private static readonly int StateHash = Animator.StringToHash("State");

    private readonly List<RaycastHit2D> pathHits = new List<RaycastHit2D>(8);
    private readonly List<Collider2D> explosionHits = new List<Collider2D>(16);
    private readonly HashSet<EnemyHealth> damagedEnemies = new HashSet<EnemyHealth>();
    private ContactFilter2D hitFilter;
    private DroneState state;
    private Collider2D target;
    private Vector2 dashDirection;
    private Quaternion defaultRotation;
    private Vector2 movementStart, castStart;
    private float castAngle;

    private float lastAttackTime;
    private float stateStartTime;

    protected override void Awake()
    {
        base.Awake();
        hitFilter = new ContactFilter2D();
        hitFilter.SetLayerMask(targetLayer);
        hitFilter.useTriggers = true;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        state = DroneState.Orbit;
        target = null;
        dashDirection = Vector2.zero;
        velocity = Vector2.zero;
        defaultRotation = transform.rotation;

        lastAttackTime = Time.time;
        stateStartTime = Time.time;

        if (dashTrail != null)
            dashTrail.Clear();

        UpdateAnimationState();
    }

    protected override void FixedUpdate()
    {
        // 이번 고정 단계에 지나갈 구간의 시작점. 판정은 이동 직후 HandleCollision에서 한다.
        if (state == DroneState.Execute && !isReturning)
        {
            RaycastMotor2D.PrepareAttackQueries();
            movementStart = castStart = transform.position;
            castAngle = transform.eulerAngles.z;
        }

        base.FixedUpdate();
    }

    protected override void HandleCollision(RaycastCollisionInfo2D info)
    {
        // 벽에 막히기 전에 지나간 구간의 적을 먼저 판정한다.
        if (state == DroneState.Execute)
            CheckDashHits();

        base.HandleCollision(info);
    }

    protected override void DroneTick()
    {
        switch (state)
        {
            case DroneState.Orbit:
                UpdateOrbit();
                TryStartWindup();
                break;

            case DroneState.Windup:
                UpdateWindup();
                break;

            case DroneState.Execute:
                UpdateExecute();
                break;

            case DroneState.Recover:
                UpdateRecover();
                break;
        }
    }

    private void UpdateOrbit()
    {
        FollowPlayer(orbitFollowSpeedMultiplier);
    }

    private void TryStartWindup()
    {
        if (Time.time < lastAttackTime + attackInterval)
            return;

        target = FindNearestEnemy(searchRange, targetLayer);

        if (target == null)
            return;

        dashDirection = GetDashDirection();

        if (dashDirection.sqrMagnitude > 0.0001f)
            RotateToDashDirection(dashDirection);

        lastAttackTime = Time.time;
        ChangeState(DroneState.Windup);

        if (windupFeedback != null)
            FeedbackPlayer.Instance.Play(windupFeedback, transform.position);
    }

    private void UpdateWindup()
    {
        FollowPlayer(0.35f);

        if (!IsTargetable(target))
            target = FindNearestEnemy(searchRange, targetLayer);

        Vector2 desiredDir = GetDashDirection();

        if (desiredDir.sqrMagnitude > 0.0001f)
        {
            dashDirection = RotateDirectionTowards(
                dashDirection,
                desiredDir,
                windupAimTurnSpeed * Time.deltaTime);

            RotateToDashDirection(dashDirection);
        }

        if (Time.time < stateStartTime + windupTime)
            return;

        // 한 단계에 1.5칸씩 일직선으로 나가므로, 출발 순간에는 목표를 정확히 겨눈다.
        if (desiredDir.sqrMagnitude > 0.0001f)
        {
            dashDirection = desiredDir;
            RotateToDashDirection(dashDirection);
        }

        if (dashDirection.sqrMagnitude <= 0.0001f)
        {
            ChangeState(DroneState.Recover);
            return;
        }

        StartDash();
    }

    private void StartDash()
    {
        Vector2 start = transform.position;
        float angle = Mathf.Atan2(dashDirection.y, dashDirection.x) * Mathf.Rad2Deg;

        if (dashFeedback != null)
            FeedbackPlayer.Instance.PlayExact(dashFeedback, start, Quaternion.Euler(0f, 0f, angle));

        if (dashTrail != null)
            dashTrail.Begin(start, dashDirection);

        velocity = dashDirection * dashSpeed;
        ChangeState(DroneState.Execute);
    }

    private void UpdateExecute()
    {
        velocity = dashDirection * dashSpeed;

        if (Time.time >= stateStartTime + dashDuration)
            Detonate(transform.position);
    }

    private void UpdateRecover()
    {
        FollowPlayer(1f);

        if (Time.time >= stateStartTime + recoverTime)
        {
            target = null;
            dashDirection = Vector2.zero;
            ChangeState(DroneState.Orbit);
        }

        transform.rotation = Quaternion.RotateTowards(transform.rotation, defaultRotation, 720f * Time.deltaTime);
    }

    private void RotateToDashDirection(Vector2 dir)
    {
        if (dir.sqrMagnitude <= 0.0001f)
            return;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        // 기본 자세는 창끝이 바로 아래를 바라보므로 90도 보정
        transform.rotation = Quaternion.Euler(0f, 0f, angle + 90f);
    }

    private void FollowPlayer(float multiplier)
    {
        if (player == null || owner == null)
        {
            velocity = Vector2.zero;
            return;
        }

        Vector2 desiredPos =
            (Vector2)player.position +
            new Vector2(-owner.Facing * followDistance, followHeight);

        velocity = (desiredPos - (Vector2)transform.position) * speed * multiplier;
    }

    private Vector2 RotateDirectionTowards(Vector2 currentDir, Vector2 desiredDir, float maxDegreesDelta)
    {
        if (currentDir.sqrMagnitude <= 0.0001f)
            return desiredDir;

        float currentAngle = Mathf.Atan2(currentDir.y, currentDir.x) * Mathf.Rad2Deg;
        float desiredAngle = Mathf.Atan2(desiredDir.y, desiredDir.x) * Mathf.Rad2Deg;

        float nextAngle = Mathf.MoveTowardsAngle(
            currentAngle,
            desiredAngle,
            maxDegreesDelta);

        float rad = nextAngle * Mathf.Deg2Rad;

        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
    }

    // 목표 판정 상자 중심을 겨눈다.
    private Vector2 GetDashDirection()
    {
        if (target == null)
            return Vector2.zero;

        Vector2 dir = (Vector2)target.bounds.center - (Vector2)transform.position;

        if (dir.sqrMagnitude <= 0.0001f)
            return Vector2.zero;

        return dir.normalized;
    }

    private void ChangeState(DroneState nextState)
    {
        state = nextState;
        stateStartTime = Time.time;

        UpdateAnimationState();
    }

    private void UpdateAnimationState()
    {
        if (animator == null)
            return;

        animator.SetInteger(StateHash, (int)state);
    }

    // 돌진이 끝난 자리에서 폭발한다(StarSpear 창과 같은 방식). 드론은 사라지지 않고 돌아온다.
    private void Detonate(Vector2 center)
    {
        velocity = Vector2.zero;

        if (dashTrail != null)
            dashTrail.End();

        damagedEnemies.Clear();

        if (motor != null && explosionRadius > 0f && damageInfo.damage > 0f)
        {
            RaycastMotor2D.InvalidateAttackQuerySync();
            motor.OverlapAttackCircle(center, explosionRadius, hitFilter, explosionHits);

            foreach (var hit in explosionHits)
            {
                if (!IsTargetable(hit))
                    continue;

                var enemy = hit.GetComponentInParent<EnemyHealth>();

                if (!damagedEnemies.Add(enemy))
                    continue;

                Vector2 hitPoint = hit.ClosestPoint(center);

                if (enemy.enemyShieldSystem == null)
                    enemy.TakeDamage(damageInfo, hitPoint);
                else
                    enemy.enemyShieldSystem.TakeDamage(damageInfo, hitPoint);
            }
        }

        if (explosionFeedback != null)
            FeedbackPlayer.Instance.Play(explosionFeedback, center);

        ChangeState(DroneState.Recover);
    }

    protected override void OnHitSomething()
    {
        if (state != DroneState.Execute)
            return;

        Detonate(transform.position);
    }

    private void CheckDashHits()
    {
        Vector2 position = transform.position;

        if (dashTrail != null)
            dashTrail.Record(position);

        Vector2 move = position - movementStart;

        if (move.sqrMagnitude <= 0.000001f)
            return;

        motor.CastAttackPath(castStart, castAngle, move, hitFilter, pathHits);

        foreach (var hit in pathHits)
        {
            if (!IsTargetable(hit.collider))
                continue;

            Detonate(hit.point);
            return;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, searchRange);
    }
#endif
}
