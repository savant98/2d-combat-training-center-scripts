using System.ComponentModel;
using UnityEngine;
public class ReadOnlyAttribute : PropertyAttribute { }
public class AbilityManager : MonoBehaviour
{
    public PlayerStateMachine StateMachine { get; private set; }
    private PlayerAbility[] abilities;
    [SerializeField, ReadOnly]
    private PlayerState debugState;

    public PlayerWeaponAbility AttackAbility => attackAbility;
    public JumpAbility JumpAbility => jumpAbility;
    public MoveAbility MoveAbility => moveAbility;
    public HitAbility HitAbility => hitAbility;
    public DashAbility DashAbility => dashAbility;
    public InteractAbility InteractAbility => interactAbility;

    private PlayerWeaponAbility attackAbility;
    private JumpAbility jumpAbility;
    private MoveAbility moveAbility;
    private HitAbility hitAbility;
    private DashAbility dashAbility;
    private InteractAbility interactAbility;
    private PlayerController controller;

    private void Awake()
    {
        StateMachine = new PlayerStateMachine();
        abilities = GetComponents<PlayerAbility>();
        controller = GetComponent<PlayerController>();
        attackAbility = GetComponent<PlayerWeaponAbility>();
        jumpAbility = GetComponent<JumpAbility>();
        moveAbility = GetComponent<MoveAbility>();
        hitAbility = GetComponent<HitAbility>();
        dashAbility = GetComponent<DashAbility>();
        interactAbility = GetComponent<InteractAbility>();
    }
    public void InterruptAllAbilities()
    {
        foreach (var ability in abilities)
        {
            ability.Interrupt();
        }
    }
    private void Start()
    {
        foreach (var ability in abilities)
        {
            ability.Initialize(this);
        }   
    }
    private void Update()
    {
        // Ability 로직 실행
        foreach (var ability in abilities)
        {
            ability.Tick();

            if (ability is PlayerWeaponAbility)
            {
                StateMachine.SetAttackPhase(attackAbility.AttackPhase);
            }
        }

        // 최종 상태 결정
        UpdateState();

        // 디버그 상태 표시
        debugState = StateMachine.CurrentState;
    }
    private void UpdateState()
    {
        // 최우선: Interact
        if (interactAbility != null && interactAbility.IsActive)
        {
            StateMachine.SetState(PlayerState.Interact);
            return;
        }

        // 최우선: Hit
        if (hitAbility != null && hitAbility.IsActive)
        {
            StateMachine.SetState(PlayerState.Hit);
            return;
        }

        // 1순위: 대쉬
        if (dashAbility != null && dashAbility.IsActive)
        {
            StateMachine.SetState(PlayerState.Dash);
            return;
        }

        // 2순위: 공격
        if (attackAbility != null && attackAbility.IsActive)
        {
            StateMachine.SetState(PlayerState.Attack);
            return;
        }

        // 3순위: 점프/공중 (점프 어빌리티 활성 or 공중)
        if (jumpAbility != null && jumpAbility.IsActive)
        {
            StateMachine.SetState(PlayerState.Jump);
            return;
        }

        // 4순위: 이동
        if (Mathf.Abs(controller.Velocity.x) > 0.01f && controller.IsGrounded)
        {
            StateMachine.SetState(PlayerState.Move);
            return;
        }

        // 기본
        StateMachine.SetState(PlayerState.Idle);
    }
}
