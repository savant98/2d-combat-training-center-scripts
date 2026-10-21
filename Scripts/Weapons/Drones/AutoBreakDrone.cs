using UnityEngine;

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
    [SerializeField] private FeedbackDataSO hitFeedback;
    [SerializeField] private FeedbackDataSO blockFeedback;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    private static readonly int StateHash = Animator.StringToHash("State");

    private DroneState state;
    private Transform target;
    private Vector2 dashDirection;
    private Quaternion defaultRotation;

    private float lastAttackTime;
    private float stateStartTime;

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

        UpdateAnimationState();
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

        target = FindNearestTarget();

        if (target == null)
            return;

        dashDirection = GetDashDirection();

        if (dashDirection.sqrMagnitude > 0.0001f)
        {
            dashDirection.Normalize();
            RotateToDashDirection(dashDirection);
        }

        lastAttackTime = Time.time;
        ChangeState(DroneState.Windup);

        if (windupFeedback != null)
            FeedbackPlayer.Instance.Play(windupFeedback, transform.position);
    }
    private void UpdateWindup()
    {
        FollowPlayer(0.35f);

        Vector2 desiredDir = GetDashDirection();

        if (desiredDir.sqrMagnitude > 0.0001f)
        {
            dashDirection = RotateDirectionTowards(
                dashDirection,
                desiredDir.normalized,
                windupAimTurnSpeed * Time.deltaTime);

            RotateToDashDirection(dashDirection);
        }

        if (Time.time < stateStartTime + windupTime)
            return;

        if (dashDirection.sqrMagnitude <= 0.0001f)
        {
            ChangeState(DroneState.Recover);
            return;
        }

        if (dashFeedback != null)
            FeedbackPlayer.Instance.Play(dashFeedback, transform.position);

        ChangeState(DroneState.Execute);
    }
    private void UpdateExecute()
    {
        velocity = dashDirection * dashSpeed;

        if (Time.time >= stateStartTime + dashDuration)
            FinishDash();
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

        // 기본 스프라이트가 "수직 아래"를 바라보므로 -90도 보정
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

    private Transform FindNearestTarget()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(
            transform.position,
            searchRange,
            targetLayer);

        Transform nearest = null;
        float nearestSqrDist = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];

            if (hit == null)
                continue;

            if (!hit.TryGetComponent<EnemyHealth>(out _))
                continue;

            float sqrDist = ((Vector2)hit.transform.position - (Vector2)transform.position).sqrMagnitude;

            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = hit.transform;
            }
        }

        return nearest;
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
    private Vector2 GetDashDirection()
    {
        if (target == null)
            return Vector2.zero;

        Vector2 dir = (Vector2)target.position - (Vector2)transform.position;

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

    private void FinishDash()
    {
        velocity = Vector2.zero;
        ChangeState(DroneState.Recover);
    }

    protected override void OnHitSomething()
    {
        if (state != DroneState.Execute)
            return;

        if (blockFeedback != null)
            FeedbackPlayer.Instance.Play(blockFeedback, transform.position);

        FinishDash();
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (state != DroneState.Execute)
            return;

        if (!collision.TryGetComponent<EnemyHealth>(out var enemy))
            return;

        if (enemy.enemyShieldSystem == null)
            enemy.TakeDamage(damageInfo, transform.position);
        else
            enemy.enemyShieldSystem.TakeDamage(damageInfo, transform.position);

        if (hitFeedback != null)
            FeedbackPlayer.Instance.Play(hitFeedback, transform.position);

        FinishDash();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, searchRange);
    }
#endif
}