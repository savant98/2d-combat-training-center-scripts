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
    private TrailRenderer dashTrail;

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
        dashTrail = Instantiate(dashData.dashTrail, controller.facingModel.transform);

        controller.OnDashInput += () => buffer.RegisterInput();

        dashTrail.emitting = false;
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
        manager.InterruptAllAbilities();
        StartDash();
    }
    
    public override void Tick()
    {
        if (isDashing)
        {
            // Dash 방향으로 고정 속도 부여
            controller.Velocity = new Vector2(controller.Facing * dashForce, 0f);

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                EndDash();
            }
            return;
        }

        TryDash();
    }
    private void StartDash()
    {
        // 대쉬 방향 결정
        int inputDir = (int)controller.MoveInput.x;
        if (inputDir != 0)
        {
            controller.FacingUpdate(inputDir);
        }

        isDashing = true;
        timer = dashDuration;

        // 외부 힘 무시
        controller.IgnoreExternalForces = true;
        controller.GravityOn = false;

        // 피드백
        controller.animator.SetBool("Dash", true);
        feedbackPlayer.Play(dashFeedback, transform);
        dashTrail.emitting = true;
    }
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
        dashTrail.emitting = false;

        controller.animator.SetBool("Dash", false);
    }
}
