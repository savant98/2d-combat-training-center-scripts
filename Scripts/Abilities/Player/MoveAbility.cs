using UnityEngine;

public class MoveAbility : PlayerAbility
{
    private static readonly int MovementHash = Animator.StringToHash("Movement");

    public override PlayerActionType ActionType => PlayerActionType.Move;

    // Move Option
    private float moveSpeed = 10f;

    private PlayerMoveDataSO moveData;
    private PlayerGroundTrail groundTrail;
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

        if (moveData.groundTrail != null)
        {
            groundTrail = Instantiate(moveData.groundTrail, controller.transform);
            groundTrail.Initialize(manager.StateMachine);
        }
    }
    public override void Tick()
    {
        // 공중·공격·대시가 아닌, 바닥을 실제로 달리는 상태에서만 이동 Animation을 재생한다.
        controller.animator.SetFloat(MovementHash, manager.StateMachine.CurrentState == PlayerState.Move ? 1f : 0f);
    }
    private void Move(float x)
    {
        if (manager.DashAbility.IsActive) return;

        // 기관총 연사 중에는 공격 상태여도 평소 속도로 움직인다(2026-11-21 사용자 지시: 이동 속도 감소·방향 전환 제한 완전 해제).
        bool firingMachineGun = manager.StateMachine.CurrentState == PlayerState.Attack
            && manager.AttackAbility.IsMachineGunAttacking;
        if (!CanDo() && !firingMachineGun)
        {
            controller.Velocity.x = 0;
            return;
        }

        controller.Velocity.x = x * moveSpeed;
    }
}
