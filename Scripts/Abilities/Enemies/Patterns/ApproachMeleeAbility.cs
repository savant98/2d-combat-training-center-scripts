using UnityEngine;

public class ApproachMeleeAbility : AiAbility
{
    private Transform playerTr;
    private ApproachMeleeDataSO meleeData;

    private int moveDirection;
    private float targetStopX;

    private void Awake()
    {
        meleeData = (ApproachMeleeDataSO)data;
    }

    protected override void OnWindupStart()
    {
        playerTr = GameManager.Instance.Player.transform;

        controller.Velocity = Vector2.zero;

        if (playerTr == null)
            return;

        float deltaX = playerTr.position.x - controller.transform.position.x;

        if (Mathf.Abs(deltaX) <= meleeData.faceDeadZone)
        {
            moveDirection = controller.Facing;
        }
        else
        {
            moveDirection = deltaX > 0f ? 1 : -1;
            controller.FacingUpdate(moveDirection);
        }

        float offsetX = Mathf.Abs(meleeData.approachOffsetX) * moveDirection;
        targetStopX = playerTr.position.x - offsetX;
    }

    protected override void OnWindup(float t)
    {
        if (playerTr == null)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        float currentX = controller.transform.position.x;
        float remainX = targetStopX - currentX;

        bool passedTarget =
            (moveDirection > 0 && currentX >= targetStopX) ||
            (moveDirection < 0 && currentX <= targetStopX);

        if (passedTarget || Mathf.Abs(remainX) <= meleeData.approachDeadZone)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        controller.Velocity = new Vector2(moveDirection * meleeData.approachSpeed, 0f);
    }

    protected override void OnExecuteStart()
    {
        controller.Velocity = Vector2.zero;
    }

    protected override void OnExecute(float t)
    {
        controller.Velocity = Vector2.zero;
    }

    protected override void OnRecoverStart()
    {
        controller.Velocity = Vector2.zero;
    }

    protected override void OnRecover(float t)
    {
        controller.Velocity = Vector2.zero;
    }

    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;
    }
}