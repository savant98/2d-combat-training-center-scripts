using System;
using UnityEngine;

public class PlayerController : CharacterController2D
{
    [Header("Gravity Setting")]
    public bool GravityOn = true;
    public float InitialGravity = -55f;
    public float Gravity = -30f;
    [Tooltip("중력으로 떨어질 때 넘지 않는 속도(칸/초)입니다. 빠르게 떨어지며 얇은 판정을 지나치지 않게 합니다.")]
    public float MaxFallSpeed = 25f;

    [Header("Health")]
    [SerializeField] private PlayerHealth health;
    [SerializeField] private PlayerCounterState playerCounterState;
    public bool CanReceiveInput
    {
        get
        {
            if (abilityManager != null && abilityManager.Grab != null && abilityManager.Grab.IsActive) return false;
            if (UIManager.Instance != null && UIManager.Instance.BlocksGameplayInput)
                return false;

            return true;
        }
    }
    public Vector2 MoveInput { get; private set; }
    public AbilityManager AbilityManager => abilityManager;
    public PlayerHealth Health => health;
    public PlayerCounterState PlayerCounterState => playerCounterState;

    public event Action<float> OnMoveInput;
    public event Action OnJumpInput;
    public event Action OnJumpRelease;
    public event Action OnNormalWeapon;
    public event Action OnGearPressed;
    public event Action OnGearReleased;
    public event Action OnDashInput;
    public event Action OnInteractInput;
    public event Action OnSecondaryWeapon;
    public event Action OnSwapWeaponSet;

    private AnimatorOverrideController animatorOverride;
    private AbilityManager abilityManager;
    protected override void Awake()
    {
        base.Awake();
        abilityManager = GetComponent<AbilityManager>();

        animatorOverride = Instantiate(overrideAnimator);
        animator.runtimeAnimatorController = animatorOverride;
    }
    public void ResetPlayerPosition(Vector3 spawnPos)
    {
        if (abilityManager != null && abilityManager.Grab != null) abilityManager.Grab.Abort(true);
        transform.position = spawnPos;
        Velocity = Vector2.zero;
    }
    public void ResetPlayerStatus()
    {
        if (abilityManager != null && abilityManager.Grab != null) abilityManager.Grab.Abort(true);
        health.ResetHealth();
        abilityManager.AttackAbility.ResetSkillGauge();
    }
    public void ApplyWeaponAnimation(AnimationSetSO set, string animName)
    {
        animatorOverride["Player_" + animName + "1"] = set.windup;
        animatorOverride["Player_" + animName + "2"] = set.execute;
        animatorOverride["Player_" + animName + "3"] = set.recover;
    }
    private void Update()
    {
        InputUpdate();
    }
    private void InputUpdate()
    {
        if (abilityManager != null && abilityManager.Grab != null && abilityManager.Grab.IsActive)
        {
            MoveInput = Vector2.zero; return;
        }
        if (InputManager.Instance == null) return;

        if (!CanReceiveInput)
        {
            // Input 불가능할 경우 Velocity 0으로 고정
            Velocity = Vector2.zero;

            // 입력이 막힌 순간 Gear 키를 누르고 있었다면,
            // PlayerWeaponAbility 쪽에서 Charging이 남지 않도록 Release를 한번 흘려준다.
            OnGearReleased?.Invoke();
            return;
        }

        MoveInput = InputManager.Instance.Move;
        OnMoveInput?.Invoke(MoveInput.x);

        if (InputManager.Pressed(Button.Jump))
            OnJumpInput?.Invoke();

        if (InputManager.Released(Button.Jump))
            OnJumpRelease?.Invoke();

        if (InputManager.Pressed(Button.Attack))
            OnNormalWeapon?.Invoke();

        if (InputManager.Pressed(Button.Gear))
            OnGearPressed?.Invoke();

        if (InputManager.Released(Button.Gear))
            OnGearReleased?.Invoke();

        if (InputManager.Pressed(Button.SecondaryWeapon))
            OnSecondaryWeapon?.Invoke();

        if (InputManager.Pressed(Button.Dash))
            OnDashInput?.Invoke();

        if (InputManager.Pressed(Button.Interact))
            OnInteractInput?.Invoke();

        if (InputManager.Pressed(Button.SwapWeapon))
            OnSwapWeaponSet?.Invoke();
    }
    protected override Vector2 CalculateVelocity()
    {
        float fallen = Velocity.y + (GravityOn && CanReceiveInput ? Gravity * Time.deltaTime : 0);
        // 중력으로 빨라지는 낙하만 상한에 맞춘다. 다른 기술이 준 더 빠른 하강 속도는 줄이지 않는다.
        if (fallen < -MaxFallSpeed && fallen < Velocity.y)
            fallen = Mathf.Min(Velocity.y, -MaxFallSpeed);
        Velocity.y = fallen;

        return Velocity;
    }
    private void OnDisable()
    {
        if (abilityManager != null && abilityManager.Grab != null) abilityManager.Grab.Abort(true);
    }
    public void FacingUpdate(int value)
    {
        Facing = value;
    }
}
