using UnityEngine;

public class PlayerStateMachine
{
    public PlayerState CurrentState { get; private set; } = PlayerState.Idle;
    public AbilityState CurrentAttackPhase { get; private set; } = AbilityState.None;

    //Ability Manager만 호출해야함
    public void SetState(PlayerState newState)
    {
        CurrentState = newState;
    }
    public void SetAttackPhase(AbilityState newState)
    {
        CurrentAttackPhase = newState;
    }
    public bool CanPerform(PlayerActionType action)
    {
        switch (CurrentState)
        {
            case PlayerState.Idle:
            case PlayerState.Move:
                return true;

            case PlayerState.Jump:
                return action != PlayerActionType.Interact; //action != PlayerActionType.Attack; // 점프 중 공격 금지

            case PlayerState.Attack:
                return action == PlayerActionType.Dash && CurrentAttackPhase == AbilityState.Recover;

            case PlayerState.Dash:
                return false;

            case PlayerState.Hit:
                return false;
            case PlayerState.Interact:
                return false;
            default:
                break;
        }

        return false;
    }
}
