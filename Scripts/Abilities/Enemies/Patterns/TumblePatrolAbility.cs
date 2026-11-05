using UnityEngine;

// 정사각형 몸체가 진행 방향 아래 모서리를 축으로 한 면씩 넘어가며 바닥을 이동하는 순찰 패턴이다(Jumper).
// - 한 번 넘어가기: 앞쪽 아래 모서리를 축으로 몸체를 들어 올려 꼭대기(45도)를 넘긴 뒤, 모서리 축 중력 회전으로 떨어져
//   다음 면이 바닥에 닿는다. 착지 직후 같은 모서리를 축으로 살짝 젖혀졌다 돌아오고, 잠시 쉰 뒤 다음으로 넘어간다.
// - 수평 이동은 각도에서 계산한다. 중심 x = 출발 x + 방향 × s/2·(1 + sinθ - cosθ)이므로 축 모서리가 미끄러지지 않고,
//   들어 올릴 때 느렸다가 떨어질 때 빨라진다. 이동은 controller.Velocity로만 하며 충돌은 RaycastMotor2D가 처리한다.
// - 각도는 물리 단계(FixedUpdate)에서 진행하고, 이번 물리 단계에 목표 위치까지 가는 Velocity.x를 넣는다.
//   [DefaultExecutionOrder]로 CharacterController2D.FixedUpdate보다 먼저 실행된다.
// - 벽: 넘어가기 전에 RaycastMotor2D.RaycastEnvironment로 진행 방향의 벽까지 거리를 잰다. 한 칸이 남지 않으면
//   벽 쪽으로 기울었다 떨어져 돌아온 뒤 방향을 바꾼다. 예상하지 못한 벽에 막히면(OnWallHit) 그 자리에서 떨어져 돌아온다.
// - 감지·전환: 플레이어 감지와 다음 패턴 선택은 AiPatternProfileSO의 규칙이 맡는다. 바닥에 놓여 쉬는 동안에만 자리를 넘기고,
//   넘긴 패턴(점프)이 끝나면 순찰로 돌아와 플레이어 쪽으로 출발한다(방향 규칙은 Roller의 GroundPatrolAbility와 같다).
// - 외형의 회전·들림은 EnemyTumbleVisual이 실제 이동 거리로 계산한다.
[DefaultExecutionOrder(-50)]
public class TumblePatrolAbility : AiAbility
{
    private enum Phase { Rest, Lift, Fall, Bounce, BlockedTilt, FallBack }

    // 벽까지 거리를 잴 때 충돌 상자 앞면에서 안쪽으로 물린 거리(칸). 벽에 붙어 있어도 광선이 벽 안에서 시작하지 않는다.
    private const float WallProbeInset = 0.05f;
    // 충돌 상자 위아래 끝에서 띄운 광선 높이(칸). 바닥·천장을 벽으로 읽지 않는다.
    private const float WallProbeEdgeMargin = 0.1f;
    // 이 각도보다 덜 기울 만큼 벽이 가까우면 기울지 않고 바로 돌아선다(도).
    private const float MinBlockedTilt = 1f;
    // 꼭대기를 넘은 채 막혀도 뒤로 떨어지도록 되돌아오는 회전에 주는 최소 토크 비율(sin 8도).
    private const float MinFallBackTorque = 0.14f;
    // 막혀서 기울 때 벽에 닿는 순간의 속도(곡선 끝 기울기). 1보다 크면 툭 부딪힌다.
    private const float BlockedTiltEndSlope = 1.2f;
    private const int GravitySubsteps = 4;

    [Tooltip("외형을 돌리고 들어 올리는 표시 컴포넌트입니다. 비워 두면 이동만 합니다.")]
    [SerializeField] private EnemyTumbleVisual tumbleVisual;

    private TumblePatrolDataSO patrolData;
    private int patrolDirection = 1;
    private bool wallHitSubscribed;
    private bool returningFromOtherPattern;

    private Phase phase = Phase.Rest;
    private float phaseTime;
    private float restTime;
    private float angle;           // 이번에 넘어간 각도(도)
    private float angularSpeed;    // 도/초
    private float blockedTiltAngle;
    private float stepStartX;
    private int stepDirection = 1;
    private int stepsCompleted;

    public int PatrolDirection => patrolDirection;
    public int StepsCompleted => stepsCompleted;
    public bool IsResting => phase == Phase.Rest;

    // 바닥에 놓여 쉬는 동안에만 다른 패턴으로 넘어간다. 넘어가는 도중에 멈춰 기울어진 채 점프하지 않는다.
    public override bool CanYieldToPattern =>
        State == AbilityState.Execute && phase == Phase.Rest && stepsCompleted >= patrolData.minStepsBeforeYield;

    private void Awake()
    {
        patrolData = (TumblePatrolDataSO)data;
    }

    protected override void OnWindupStart()
    {
        // 처음 순찰할 때는 바라보던 방향으로 간다. 다른 패턴을 마치고 돌아오면 플레이어 쪽으로 출발한다.
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
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

        // 바닥에 놓인 자세에서 쉬지 않고 바로 첫 번째로 넘어간다.
        ResetStepState();
        restTime = patrolData.restDuration;
        stepsCompleted = 0;
        controller.Velocity.x = 0f;

        SetWallHitSubscription(true);
    }

    // 순찰은 시간으로 끝나지 않는다. Profile 규칙이 다른 패턴을 고를 때 AiBrain이 중단한다.
    protected override bool CanFinishExecute(float elapsed) => false;

    protected override void OnStop()
    {
        SetWallHitSubscription(false);
        ResetStepState();
        controller.Velocity.x = 0f;

        // AiBrain은 정지할 때 IsPaused를 먼저 켜고 패턴을 멈춘다. 정지가 아니면 Profile 규칙이 다른 패턴으로 자리를 넘긴 것이다.
        bool paused = brain != null && brain.IsPaused;
        returningFromOtherPattern = !paused;

        // 자리를 넘긴 패턴(점프)이 끝나면 무작위 선택 없이 순찰로 돌아와 플레이어 쪽으로 굴러간다.
        // 등장·사망으로 정지한 경우에는 ResumeBrain이 첫 패턴을 다시 정한다.
        if (!paused && brain != null)
            brain.ForceNextAbility(this);
    }

    private void OnDestroy()
    {
        SetWallHitSubscription(false);
    }

    private void FixedUpdate()
    {
        if (State != AbilityState.Windup && State != AbilityState.Execute)
            return;

        float dt = Time.fixedDeltaTime;
        if (phase == Phase.Rest)
        {
            controller.Velocity.x = 0f;
            restTime += dt;

            // 공중(발판 끝에서 떨어지는 중)에서는 착지할 때까지 기다린다.
            if (restTime < patrolData.restDuration || !controller.IsGrounded)
                return;
            if (!TryBeginStep())
                return;
        }

        bool finished = AdvancePhase(dt);

        // 이번 물리 단계가 끝날 때 몸체가 있어야 할 위치로 가는 속도를 넣는다. 가로막히면 RaycastMotor2D가 멈춘다.
        float targetX = stepStartX + stepDirection * EnemyTumbleVisual.ForwardOffset(patrolData.tumbleSize, angle);
        controller.Velocity.x = (targetX - transform.position.x) / dt;

        if (finished)
            FinishStep();
    }

    // 다음으로 넘어갈 수 있으면 넘어가기를, 벽이 가까우면 벽 쪽으로 기울기를 시작한다. 바로 돌아섰으면 false다.
    private bool TryBeginStep()
    {
        float size = patrolData.tumbleSize;
        float gap = MeasureWallGap(patrolDirection, size);

        ResetStepState();
        stepStartX = transform.position.x;
        stepDirection = patrolDirection;

        if (gap >= size)
        {
            phase = Phase.Lift;
        }
        else
        {
            // 한 칸이 남지 않았다. 앞쪽 위 모서리가 벽에 닿는 각도(asin(남은 거리 / 한 변))까지만 기울었다가 돌아온다.
            blockedTiltAngle = Mathf.Min(patrolData.maxBlockedTiltAngle, Mathf.Asin(Mathf.Clamp01(gap / size)) * Mathf.Rad2Deg);
            if (blockedTiltAngle < MinBlockedTilt)
            {
                TurnAround();
                return false;
            }
            phase = Phase.BlockedTilt;
        }

        if (tumbleVisual != null)
            tumbleVisual.BeginStep(stepStartX, stepDirection, size);
        return true;
    }

    // 각도를 이번 물리 단계 끝의 목표로 진행한다. 이번 단계로 넘어가기·기울기가 끝나면 true다.
    private bool AdvancePhase(float dt)
    {
        phaseTime += dt;
        switch (phase)
        {
            case Phase.Lift:
            {
                // 멈춘 상태에서 천천히 들어 올려 topAngularSpeed로 꼭대기를 넘는다.
                float u = Mathf.Clamp01(phaseTime / patrolData.liftDuration);
                float endSlope = Mathf.Clamp(patrolData.topAngularSpeed * patrolData.liftDuration / 45f, 0f, 2.9f);
                angle = 45f * Hermite(u, endSlope);
                if (u >= 1f)
                {
                    angle = 45f;
                    angularSpeed = patrolData.topAngularSpeed;
                    phase = Phase.Fall;
                    phaseTime = 0f;
                }
                return false;
            }
            case Phase.Fall:
                IntegrateCornerGravity(dt, false);
                if (angle < 90f)
                    return false;
                angle = 90f;
                angularSpeed = 0f;
                if (patrolData.landingBounceAngle <= 0f)
                    return true;
                phase = Phase.Bounce;
                phaseTime = 0f;
                return false;

            case Phase.Bounce:
            {
                // 새 면이 바닥을 친 반동으로 같은 모서리를 축으로 살짝 젖혀졌다가 다시 놓인다.
                float u = Mathf.Clamp01(phaseTime / patrolData.landingBounceDuration);
                angle = 90f - patrolData.landingBounceAngle * Mathf.Sin(Mathf.PI * u);
                if (u < 1f)
                    return false;
                angle = 90f;
                return true;
            }
            case Phase.BlockedTilt:
            {
                // 벽 쪽으로 기울다가 앞쪽 위 모서리가 벽에 닿으면 멈춘다.
                float duration = Mathf.Max(0.02f, patrolData.blockedTiltDuration * blockedTiltAngle / Mathf.Max(1f, patrolData.maxBlockedTiltAngle));
                float u = Mathf.Clamp01(phaseTime / duration);
                angle = blockedTiltAngle * Hermite(u, BlockedTiltEndSlope);
                if (u >= 1f)
                {
                    angle = blockedTiltAngle;
                    angularSpeed = 0f;
                    phase = Phase.FallBack;
                    phaseTime = 0f;
                }
                return false;
            }
            case Phase.FallBack:
                IntegrateCornerGravity(dt, true);
                if (angle > 0f)
                    return false;
                angle = 0f;
                angularSpeed = 0f;
                return true;
        }
        return false;
    }

    private void FinishStep()
    {
        bool completedTumble = phase == Phase.Fall || phase == Phase.Bounce;
        if (completedTumble)
        {
            stepsCompleted++;
        }
        else
        {
            // 벽에 막혀 제자리로 떨어졌다. 쉬었다가 반대 방향으로 간다.
            TurnAround();
        }

        ResetStepState();
        if (tumbleVisual != null)
            tumbleVisual.EndStep();
    }

    // 모서리 축 회전: θ'' = 3g / (2√2·s) · sin(θ - 45도). 질량 m, 한 변 s인 정사각형 판의 모서리 축 관성 모멘트는 2ms²/3이고,
    // 중심이 축에서 s/√2 떨어져 있어 중력 토크가 m·g·(s/√2)·cos(135도 - θ)다. 45도보다 작으면 되돌아오고, 크면 넘어간다.
    private void IntegrateCornerGravity(float dt, bool fallingBack)
    {
        float k = 3f * patrolData.tumbleGravity / (2f * Mathf.Sqrt(2f) * patrolData.tumbleSize) * Mathf.Rad2Deg;
        float h = dt / GravitySubsteps;
        for (int i = 0; i < GravitySubsteps; i++)
        {
            float torque = Mathf.Sin((angle - 45f) * Mathf.Deg2Rad);
            if (fallingBack)
                torque = Mathf.Min(torque, -MinFallBackTorque);
            angularSpeed += k * torque * h;
            angle += angularSpeed * h;
        }
    }

    // 삼차 Hermite 곡선: H(0)=0, H(1)=1, H'(0)=0, H'(1)=endSlope. endSlope가 3보다 작으면 단조 증가한다.
    private static float Hermite(float u, float endSlope)
    {
        float u2 = u * u;
        return u2 * (3f - 2f * u) + endSlope * u2 * (u - 1f);
    }

    // 충돌 상자(루트 중심, 한 변 size) 앞면에서 진행 방향의 벽까지 남은 거리(칸). 벽이 없으면 매우 큰 값이다.
    private float MeasureWallGap(int direction, float size)
    {
        RaycastMotor2D motor = controller.Motor;
        if (motor == null)
            return float.MaxValue;

        float half = size * 0.5f;
        Vector2 center = transform.position;
        Vector2 rayDirection = new Vector2(direction, 0f);
        float reach = size + WallProbeInset + 0.01f;
        float gap = float.MaxValue;

        // 아래·가운데·위 세 높이에서 잰다.
        for (int i = -1; i <= 1; i++)
        {
            Vector2 origin = new Vector2(center.x + direction * (half - WallProbeInset), center.y + i * (half - WallProbeEdgeMargin));
            RaycastHit2D hit = motor.RaycastEnvironment(origin, rayDirection, reach);
            if (hit.collider != null)
                gap = Mathf.Min(gap, Mathf.Max(0f, hit.distance - WallProbeInset));
        }
        return gap;
    }

    private void HandleWallHit()
    {
        if (State == AbilityState.None || (phase != Phase.Lift && phase != Phase.Fall))
            return;

        // 이번 이동에서 진행 방향으로 민 경우만 처리한다.
        float push = controller.Velocity.x;
        if (Mathf.Abs(push) < 0.001f || (push > 0f ? 1 : -1) != stepDirection)
            return;

        // 미리 재지 못한 벽에 막혔다. 실제로 넘어간 각도에서 멈춘 뒤 떨어져 돌아오고 방향을 바꾼다.
        float offset = (transform.position.x - stepStartX) * stepDirection;
        angle = EnemyTumbleVisual.AngleFromOffset(patrolData.tumbleSize, offset);
        angularSpeed = 0f;
        phase = Phase.FallBack;
        phaseTime = 0f;
        controller.Velocity.x = 0f;
    }

    private void TurnAround()
    {
        patrolDirection = -patrolDirection;
        controller.FacingUpdate(patrolDirection);
    }

    private void ResetStepState()
    {
        phase = Phase.Rest;
        phaseTime = 0f;
        restTime = 0f;
        angle = 0f;
        angularSpeed = 0f;
        if (tumbleVisual != null && tumbleVisual.IsStepping)
            tumbleVisual.EndStep();
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
