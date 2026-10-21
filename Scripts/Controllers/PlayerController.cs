using System;
using UnityEngine;

public class PlayerController : CharacterController2D
{
    [Header("Gravity Setting")]
    public bool GravityOn = true;
    public float InitialGravity = -55f;
    public float Gravity = -30f;

    [Header("Health")]
    [SerializeField] private PlayerHealth health;
    [SerializeField] private PlayerCounterState playerCounterState;
    public bool CanReceiveInput
    {
        get
        {
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
        transform.position = spawnPos;
        Velocity = Vector2.zero;
    }
    public void ResetPlayerStatus()
    {
        health.ResetHealth();
        abilityManager.AttackAbility.ResetSkillGauge();
    }
    public void ApplyWeaponAnimation(AnimationSet set, string animName)
    {
        animatorOverride["Player" + animName + "1"] = set.windup;
        animatorOverride["Player" + animName + "2"] = set.execute;
        animatorOverride["Player" + animName + "3"] = set.recover;
    }
    private void Update()
    {
        InputUpdate();
    }
    private void InputUpdate()
    {
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
        Velocity.y += GravityOn && CanReceiveInput ? Gravity * Time.deltaTime : 0;

        return Velocity;
    }
    public void FacingUpdate(int value)
    {
        Facing = value;
    }
}
