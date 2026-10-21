using UnityEngine;

public class MoveAbility : PlayerAbility
{
    public override PlayerActionType ActionType => PlayerActionType.Move;

    // Move Option
    private float moveSpeed = 10f;

    private PlayerMoveDataSO moveData;
    public float MoveSpeed => moveSpeed;
    protected override void Awake()
    {
        base.Awake();
        moveData = (PlayerMoveDataSO)data;
        moveSpeed = moveData.moveSpeed;
    }
    public override void Initialize(AbilityManager manager)
    {
        base.Initialize(manager);
        controller.OnMoveInput += Move;
    }
    private void Move(float x)
    {
        if (!CanDo())
        {
            controller.Velocity.x = 0;
            return;
        }

        controller.Velocity.x = x * moveSpeed;
    }
}
