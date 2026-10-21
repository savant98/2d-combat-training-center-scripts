public class JumpAbility : PlayerAbility
{
    public override PlayerActionType ActionType => PlayerActionType.Jump;
    public override bool IsActive => !controller.IsGrounded;

    // Jump Options
    private float jumpForce = 10f;
    private int maxJumpCount = 2;
    private float lowGravity = -15f;
    private float highGravity = -55f;

    // Feedbacks
    private FeedbackData startJumpFeedback;
    private FeedbackData extraJumpFeedback;

    public int currentJumpCount = 0;
    private bool releasedThisJump = false;
    private InputBuffer jumpBuffer = new InputBuffer(0.2f);
    private PlayerJumpData jumpData;
    public override void Initialize(AbilityManager manager)
    {
        base.Initialize(manager);
        jumpData = (PlayerJumpData)data;
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
    private void TryJump()
    {
        if (!CanDo())
            return;
        if (currentJumpCount <= 0)
            return;

        if (!jumpBuffer.ConsumeIfValid())
            return;

        Jump();
    }
    public override void Tick()
    {
        // 점프 시작
        TryJump();

        // 롱점프 판정
        bool jumpHeld = InputManager.Held(Button.Jump);

        bool canUseLowGravity = !releasedThisJump && jumpHeld && controller.Velocity.y > 0;

        // 높은 점프 체크
        if (canUseLowGravity)
            controller.Gravity = lowGravity;
        else
            controller.Gravity = highGravity;
    }
    private void Jump()
    {
        currentJumpCount--;

        //릴리즈 이력이 있으면 롱점프 불가능한 상태로 점프
        releasedThisJump = !InputManager.Held(Button.Jump);

        controller.Velocity.y = jumpForce;

        if (currentJumpCount == maxJumpCount - 1)
            feedbackPlayer.Play(startJumpFeedback, transform);
        else
            feedbackPlayer.Play(extraJumpFeedback, transform);
    }
    private void StopLongJump()
    {
        releasedThisJump = true;
    }
    private void ResetJumpCount()
    {
        currentJumpCount = maxJumpCount;

        controller.Gravity = highGravity;
    }
    private void BeginFall()
    {
        if (currentJumpCount == maxJumpCount && controller.Velocity.y < 0)
            currentJumpCount--;
    }
    public override void Interrupt()
    {
        releasedThisJump = true;
        controller.Gravity = highGravity;
    }
}
