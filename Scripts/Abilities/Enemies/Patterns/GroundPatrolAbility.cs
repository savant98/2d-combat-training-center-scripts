using UnityEngine;

// 바닥을 한 방향으로 계속 이동하는 순찰 패턴이다.
// 벽 판정은 EnemyController가 RaycastMotor2D 이동 결과로 알리는 OnWallHit을 사용한다.
// 플레이어 감지와 다음 패턴 선택은 AiPatternProfileSO의 규칙이 맡고, 이 패턴은 실행 중 자리를 넘긴다.
// 플레이어를 발견해 다른 패턴(돌진)으로 넘어갔다가 돌아오면 플레이어 쪽으로 출발한다.
public class GroundPatrolAbility : AiAbility
{
    private GroundPatrolDataSO patrolData;
    private MoveAbility playerMove;
    private int patrolDirection = 1;
    private bool wallHitSubscribed;
    private bool returningFromOtherPattern;

    public int PatrolDirection => patrolDirection;
    public float PatrolSpeed => ResolvePatrolSpeed();
    public override bool CanYieldToPattern => State == AbilityState.Execute;

    private void Awake()
    {
        patrolData = (GroundPatrolDataSO)data;
    }

    protected override void OnWindupStart()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        playerMove = player != null ? player.GetComponent<MoveAbility>() : null;

        // 처음 순찰할 때는 바라보던 방향으로 간다. 다른 패턴을 마치고 돌아오면 플레이어 쪽으로 출발한다.
        // 돌진이 플레이어를 지나쳐 끝나도 다시 플레이어에게 다가가며 다음 돌진을 노린다.
        int direction = controller.Facing >= 0 ? 1 : -1;
        if (returningFromOtherPattern && patrolData.facePlayerAfterOtherPattern && player != null)
        {
            float deltaX = player.transform.position.x - transform.position.x;
            if (Mathf.Abs(deltaX) > 0.01f)
                direction = deltaX > 0f ? 1 : -1;
        }
        returningFromOtherPattern = false;

        patrolDirection = direction;
        controller.FacingUpdate(patrolDirection);

        SetWallHitSubscription(true);
    }

    protected override void OnWindup(float t) => AccelerateToPatrolSpeed();
    protected override void OnExecute(float t) => AccelerateToPatrolSpeed();

    // 순찰은 시간으로 끝나지 않는다. Profile 규칙이 다른 패턴을 고를 때 AiBrain이 중단한다.
    protected override bool CanFinishExecute(float elapsed) => false;

    protected override void OnStop()
    {
        SetWallHitSubscription(false);

        // AiBrain은 정지할 때 IsPaused를 먼저 켜고 패턴을 멈춘다. 정지가 아니면 Profile 규칙이 다른 패턴으로 자리를 넘긴 것이다.
        bool paused = brain != null && brain.IsPaused;
        returningFromOtherPattern = !paused;

        // 다음 패턴으로 넘어갈 때는 남은 속도를 다음 패턴이 감속한다. 정지 상태로 바뀔 때만 즉시 멈춘다.
        if (paused)
            controller.Velocity.x = 0f;
    }

    private void OnDestroy()
    {
        SetWallHitSubscription(false);
    }

    private void AccelerateToPatrolSpeed()
    {
        float speed = ResolvePatrolSpeed();
        float acceleration = speed / Mathf.Max(0.01f, patrolData.accelerationTime);
        controller.Velocity.x = Mathf.MoveTowards(
            controller.Velocity.x,
            patrolDirection * speed,
            acceleration * Time.deltaTime);
    }

    private float ResolvePatrolSpeed()
    {
        return playerMove != null ? playerMove.MoveSpeed * patrolData.playerSpeedRatio : patrolData.fallbackSpeed;
    }

    private void HandleWallHit()
    {
        if (State == AbilityState.None)
            return;

        // 이번 이동에서 벽 쪽으로 민 경우에만 방향을 바꾼다. 넉백으로 반대편 벽에 닿으면 순찰 방향을 유지한다.
        float push = controller.Velocity.x + (controller.IgnoreExternalForces ? 0f : controller.externalForce.x);
        if (Mathf.Abs(push) < 0.001f || (push > 0f ? 1 : -1) != patrolDirection)
            return;

        patrolDirection = -patrolDirection;
        controller.Velocity.x = 0f;
        controller.FacingUpdate(patrolDirection);
    }

    private void SetWallHitSubscription(bool subscribe)
    {
        if (controller == null || wallHitSubscribed == subscribe)
            return;

        if (subscribe)
            controller.OnWallHit += HandleWallHit;
        else
            controller.OnWallHit -= HandleWallHit;

        wallHitSubscribed = subscribe;
    }
}
