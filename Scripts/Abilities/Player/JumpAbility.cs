public class JumpAbility : PlayerAbility
{
    public override PlayerActionType ActionType => PlayerActionType.Jump;
    public override bool IsActive => !controller.IsGrounded;

    // Jump Options
    private float jumpForce = 10f;
    private int maxJumpCount = 2;
    private float lowGravity = -15f;
    private float highGravity = -55f;
    public float MaxJumpHeight => jumpForce * jumpForce / (2f * UnityEngine.Mathf.Max(0.01f, -lowGravity));

    // Feedbacks
    private FeedbackDataSO startJumpFeedback;
    private FeedbackDataSO extraJumpFeedback;

    public int currentJumpCount = 0;
    private bool releasedThisJump = false;
    private bool extraJumpAscending;
    private InputBuffer jumpBuffer = new InputBuffer(0.2f);
    private PlayerJumpDataSO jumpData;
    public override void Initialize(AbilityManager manager)
    {
        base.Initialize(manager);
        jumpData = (PlayerJumpDataSO)data;
        jumpBuffer.SetBufferTime(jumpData.inputBufferTime);
        jumpForce = jumpData.jumpForce;
        maxJumpCount = jumpData.maxJumpCount;
        lowGravity = jumpData.lowGravity;
        highGravity = jumpData.highGravity;
        startJumpFeedback = jumpData.startJumpFeedback;
        extraJumpFeedback = jumpData.extraJumpFeedback;

        currentJumpCount = maxJumpCount;

        controller.OnJumpInput += () => jumpBuffer.RegisterInput();
        controller.OnJumpRelease += StopLongJump;
        controller.OnLanded += ResetJumpCount;
        controller.OnFalled += BeginFall;
    }
    protected override bool CanDo() => base.CanDo()
        || (manager.StateMachine.CurrentState == PlayerState.Attack && manager.AttackAbility != null
            && manager.AttackAbility.IsMachineGunAttacking);

    private void TryJump()
    {
        if (!CanDo())
            return;
        if (currentJumpCount <= 0)
            return;

        if (!jumpBuffer.ConsumeIfValid())
            return;

        if (controller.IsGrounded && InputManager.Instance != null && InputManager.Instance.Move.y < -.5f
            && controller.Motor.TryDropThroughOneWay())
        {
            controller.Velocity.y = -4f;
            releasedThisJump = true;
            return;
        }
        Jump();
    }
    public override void Tick()
    {
        // 같은 프레임의 공격·대시가 상승을 취소하면 새 착지 이벤트 없이 접지 상태가 이어질 수 있다.
        // 상승 중 잠깐 남은 IsGrounded에서는 복구하지 않아 공중 점프 횟수는 유지한다.
        if (controller.IsGrounded && controller.Velocity.y <= 0f && currentJumpCount < maxJumpCount)
            ResetJumpCount();
        // 점프 시작
        TryJump();

        // 롱점프 판정
        bool jumpHeld = InputManager.Held(Button.Jump);

        bool canUseLowGravity = !releasedThisJump && jumpHeld && controller.Velocity.y > 0;

        if (controller.Velocity.y <= 0f) extraJumpAscending = false;
        // 이단 점프는 초기 상승 속도를 유지하고 상승 중력만 조정한다.
        // 원래 최고점 도달 시간과 상승 높이의 65%를 사용한다.
        float ascentScale = extraJumpAscending && controller.Velocity.y > 0f ? 1f / .65f : 1f;
        controller.Gravity = (canUseLowGravity ? lowGravity : highGravity) * ascentScale;
    }
    private void Jump()
    {
        extraJumpAscending = currentJumpCount < maxJumpCount;
        currentJumpCount--;

        //릴리즈 이력이 있으면 롱점프 불가능한 상태로 점프
        releasedThisJump = !InputManager.Held(Button.Jump);

        controller.Velocity.y = jumpForce;

        if (currentJumpCount == maxJumpCount - 1)
        {
            feedbackPlayer.Play(startJumpFeedback, transform);
            controller.animator.SetTrigger("Jump");
        }
        else
        {
            feedbackPlayer.Play(extraJumpFeedback, transform);
            controller.animator.SetTrigger("DoubleJump");
        }
    }
    private void StopLongJump()
    {
        releasedThisJump = true;
    }
    private void ResetJumpCount()
    {
        currentJumpCount = maxJumpCount;
        extraJumpAscending = false;

        controller.Gravity = highGravity;

        // Warp 등 전이가 없는 상태에서 소비되지 못한 점프 Trigger가 착지 뒤 늦게 재생되지 않게 지운다.
        controller.animator.ResetTrigger("Jump");
        controller.animator.ResetTrigger("DoubleJump");
    }
    private void BeginFall()
    {
        // 지상을 떠나면 세로 속도와 무관하게 지상 점프 몫을 사용한다.
        if (currentJumpCount == maxJumpCount)
            currentJumpCount--;
    }
    public void ClearPendingInput() { jumpBuffer.Reset(); releasedThisJump = true; }
    public override void Interrupt()
    {
        releasedThisJump = true;
        extraJumpAscending = false;
        controller.Gravity = highGravity;
    }
}
