using UnityEngine;

public class DashAbility : PlayerAbility
{
    public override PlayerActionType ActionType => PlayerActionType.Dash;
    public override bool IsActive => isDashing;

    // DashOptions
    private float dashCooldown = 1f;
    private float dashForce = 10f;
    private float dashDuration = 0.15f;

    // Feedbacks
    private FeedbackDataSO dashFeedback;
    private FeedbackDataSO dashBrakeFeedback;
    private PlayerDashVisual dashVisual;

    private int dashDirection;
    private bool isDashing = false;
    private float timer = 0f;
    private float lastDashTime = -999f;
    private PlayerDashDataSO dashData;
    private InputBuffer buffer = new InputBuffer(0.15f);
    public override void Initialize(AbilityManager manager)
    {
        base.Initialize(manager);
        dashData = (PlayerDashDataSO)data;
        buffer.SetBufferTime(dashData.inputBufferTime);
        dashCooldown = dashData.dashCooldown;
        dashForce = dashData.dashForce;
        dashDuration = dashData.dashDuration;
        dashFeedback = dashData.dashFeedback;
        dashBrakeFeedback = dashData.dashBrakeFeedback;
        dashVisual = Instantiate(dashData.dashVisual, transform);
        dashVisual.Initialize();

        controller.OnDashInput += () => buffer.RegisterInput();
    }
    protected override bool CanDo()
    {
        var attack = manager.AttackAbility;
        if (manager.StateMachine.CurrentState == PlayerState.Attack && attack != null
            && (attack.IsStoringSkill || attack.IsMachineGunAttacking))
            return true;
        return base.CanDo();
    }
    private void TryDash()
    {
        if (!CanDo())
            return;
        
        //쿨타임 체크
        if (Time.time < lastDashTime + dashCooldown)
            return;

        if (!buffer.ConsumeIfValid())
            return;

        lastDashTime = Time.time;
        var attack = manager.AttackAbility;
        manager.InterruptAbilitiesExcept(attack != null && attack.IsMachineGunAttacking ? attack : null);
        StartDash();
    }
    
    public override void Tick()
    {
        if (isDashing)
        {
            // 시작 방향의 반대 입력은 즉시 제동한다. 중립·같은 방향은 대시를 유지한다.
            if (controller.MoveInput.x * dashDirection < -.1f)
            {
                EndDash();
                feedbackPlayer.Play(dashBrakeFeedback, dashVisual.transform);
                return;
            }
            // Dash 방향으로 고정 속도 부여
            controller.Velocity = new Vector2(dashDirection * dashForce, 0f);

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                EndDash();
                // 끝까지 달렸을 때만 제동 효과를 낸다(중간에 끊기면 다음 동작의 효과가 바로 이어진다).
                feedbackPlayer.Play(dashBrakeFeedback, dashVisual.transform);
            }
            return;
        }

        TryDash();
    }
    private void StartDash()
    {
        // 기관총 연사 중에도 대시의 진행 방향은 이동 입력에서 정한다.
        float input = controller.MoveInput.x;
        dashDirection = Mathf.Abs(input) > .1f ? (input > 0f ? 1 : -1) : controller.Facing;
        if (!controller.LockFacing) controller.FacingUpdate(dashDirection);
        isDashing = true;
        timer = dashDuration;

        // 외부 힘 무시
        controller.IgnoreExternalForces = true;
        controller.GravityOn = false;

        // 피드백
        controller.animator.SetBool("Dash", true);
        controller.Velocity = new Vector2(dashDirection * dashForce, 0f);
        dashVisual.Begin(dashDirection, dashDuration);
        feedbackPlayer.Play(dashFeedback, dashVisual.transform);
    }
    public void ClearPendingInput() => buffer.Reset();
    public override void Interrupt()
    {
        if (!isDashing) return;

        EndDash();
    }
    private void EndDash()
    {
        isDashing = false;
        controller.IgnoreExternalForces = false;
        controller.GravityOn = true;
        controller.Velocity = Vector2.zero;
        dashVisual.End();

        controller.animator.SetBool("Dash", false);
    }
}
