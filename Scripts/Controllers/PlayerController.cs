using System;
using System.Collections.Generic;
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
            // Wave 준비 중 구속(WavePlayerStart)은 입력을 막지 않는다. 이동만 모터의 구속 영역이 막는다.
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
    // 무장 Animation 교체용 캐시. weaponAnim 이름마다 "Player_<이름>1·2·3" 원본 Clip을 처음 한 번만 이름으로 찾아 둔다.
    private readonly Dictionary<string, AnimationClip[]> weaponAnimOriginals = new();
    // ApplyOverrides에 넘길 (원본, 새 Clip) 3쌍. 매번 비우고 다시 채워 할당하지 않는다.
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> weaponAnimOverrides = new(3);
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
        motor.ResetOneWayState();
        transform.position = spawnPos;
        if (TryGetComponent<FixedMotionBody>(out var presentation)) presentation.ResetPose();
        // 구속 상자에 갇힌 채 옮겨졌으면 구속도 도착한 자리로 옮긴다(남겨 두면 다음 물리 틱에 원래 자리로 끌려간다).
        motor.RefitConfinement();
        Velocity = Vector2.zero;
    }
    public void ResetPlayerStatus(bool preserveExecutingAttack = false)
    {
        if (abilityManager != null && abilityManager.Grab != null) abilityManager.Grab.Abort(true);
        health.ResetHealth(preserveExecutingAttack);
        abilityManager.AttackAbility.ResetSkillGauge(preserveExecutingAttack);
    }
    public void ApplyWeaponAnimation(AnimationSetSO set, string animName)
    {
        // Clip을 바꾸면 Animator가 다시 바인딩하면서 그 순간 자세를 Write Defaults 기본값으로 잡는다.
        // 이동·점프 Animation 도중 공격해도 몸의 위치·배율이 Idle에 남지 않도록 바꾸기 전에 기본 자세로 되돌린다.
        animator.WriteDefaultValues();
        // 예전에는 인덱서(이름)로 3번 넣어 넣을 때마다 다시 바인딩(3회)하고 이름 문자열도 3개 만들었다.
        // 같은 원본 Clip에 같은 순서·같은 Clip을 ApplyOverrides로 한 번에 넣어 다시 바인딩을 1회로 줄인다(최종 Override 결과는 같다).
        // 같은 Clip이어도 건너뛰지 않고 늘 다시 바인딩한다. 예전 세 번의 바인딩 사이에는 Animator 평가가 없었으므로
        // 위 WriteDefaultValues 직후 자세를 기본값으로 잡는 결과도 예전과 같다.
        AnimationClip[] originals = GetWeaponAnimOriginals(animName);
        weaponAnimOverrides.Clear();
        AddOverride(weaponAnimOverrides, originals[0], set.windup);
        AddOverride(weaponAnimOverrides, originals[1], set.execute);
        AddOverride(weaponAnimOverrides, originals[2], set.recover);
        // 이름이 맞는 원본이 없으면 인덱서도 바꾸지 않았으므로, 넣을 것이 없을 때는 다시 바인딩하지 않는다.
        if (weaponAnimOverrides.Count > 0)
            animatorOverride.ApplyOverrides(weaponAnimOverrides);
    }
    private AnimationClip[] GetWeaponAnimOriginals(string animName)
    {
        // 예전 문자열 연결은 null을 빈 문자열처럼 이어 붙였으므로 키도 같게 맞춘다.
        string key = animName ?? string.Empty;
        if (weaponAnimOriginals.TryGetValue(key, out var originals))
            return originals;

        var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        animatorOverride.GetOverrides(clips);
        originals = new AnimationClip[3];
        for (int i = 0; i < originals.Length; i++)
            originals[i] = FindOriginalClip(clips, "Player_" + key + (i + 1));
        weaponAnimOriginals.Add(key, originals);
        return originals;
    }
    // 인덱서가 이름으로 찾던 원본 Clip(PlayerBase.controller 안에서 원본 이름은 겹치지 않는다).
    private static AnimationClip FindOriginalClip(List<KeyValuePair<AnimationClip, AnimationClip>> clips, string name)
    {
        foreach (var pair in clips)
            if (pair.Key != null && pair.Key.name == name)
                return pair.Key;
        return null;
    }
    private static void AddOverride(List<KeyValuePair<AnimationClip, AnimationClip>> overrides, AnimationClip original, AnimationClip clip)
    {
        if (original != null)
            overrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(original, clip));
    }
    private const float AimResponse = 24f;
    private Texture2D aimCursor;
    private bool aimCursorActive;

    public bool UsesMouseAim => InputManager.Instance != null && InputManager.Instance.MouseAim;

    private bool TryGetMousePoint(out Vector2 point)
    {
        point = default;
        if (!UsesMouseAim || !CanReceiveInput || UnityEngine.InputSystem.Mouse.current == null) return false;
        var camera = Camera.main;
        if (camera == null) return false;
        Vector2 screen = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        Ray ray = camera.ScreenPointToRay(screen);
        float depth = transform.position.z;
        if (Mathf.Abs(ray.direction.z) < .0001f) return false;
        point = ray.GetPoint((depth - ray.origin.z) / ray.direction.z);
        return true;
    }

    public bool TryGetMouseAim(Vector2 origin, out Vector2 direction)
    {
        direction = default;
        if (!TryGetMousePoint(out var point)) return false;
        Vector2 offset = point - origin;
        if (offset.sqrMagnitude < .0001f) return false;
        direction = offset.normalized;
        return true;
    }

    public bool TryGetRangedAim(Vector2 origin, float range, LayerMask targets, out Vector2 direction, out Collider2D target)
    {
        target = null;
        if (OptionSettingsService.Instance?.Current?.rangedAutoAim == true)
        {
            target = RailEcho.FindNearest(this, transform.position, Vector2.zero, range, 0f, targets);
            if (target != null)
            {
                Vector2 offset = (Vector2)target.bounds.center - origin;
                if (offset.sqrMagnitude >= .0001f)
                {
                    direction = offset.normalized;
                    return true;
                }
            }
        }
        target = null;
        return TryGetMouseAim(origin, out direction);
    }

    public static float AimAngle(Vector2 direction) => Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

    public static Vector2 SmoothAim(Vector2 current, Vector2 target)
    {
        float angle = Mathf.LerpAngle(AimAngle(current), AimAngle(target), 1f - Mathf.Exp(-AimResponse * Time.deltaTime));
        return (Vector2)(Quaternion.Euler(0f, 0f, angle) * Vector3.right);
    }

    protected override int ResolveFacing() => UsesMouseAim ? MouseFacing() : base.ResolveFacing();

    private int MouseFacing()
    {
        if (!TryGetMousePoint(out var point)) return Facing;
        float horizontal = point.x - transform.position.x;
        return Mathf.Abs(horizontal) > .001f ? (horizontal > 0f ? 1 : -1) : Facing;
    }

    private void SetAimCursor(bool active)
    {
        if (aimCursorActive == active) return;
        aimCursorActive = active;
        if (active && aimCursor == null)
        {
            const int size = 32, center = size / 2;
            aimCursor = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int near = Mathf.Min(Mathf.Abs(x - center), Mathf.Abs(y - center));
                int far = Mathf.Max(Mathf.Abs(x - center), Mathf.Abs(y - center));
                if (near <= 1 && (far is >= 3 and <= 10 || far <= 1))
                    pixels[y * size + x] = near == 0 && far != 3 && far != 10
                        ? new Color32(192, 252, 255, 255) : new Color32(10, 35, 46, 255);
            }
            aimCursor.SetPixels32(pixels);
            aimCursor.Apply();
        }
        Cursor.SetCursor(active ? aimCursor : null, active ? new Vector2(16, 15) : Vector2.zero, CursorMode.Auto);
    }

    private void Update()
    {
        SetAimCursor(UsesMouseAim && CanReceiveInput && Application.isFocused && !Application.isBatchMode
            && UnityEngine.InputSystem.Mouse.current != null);
        if (UsesMouseAim && !LockFacing)
        {
            Facing = MouseFacing();
            if (facingModel != null)
            {
                var scale = facingModel.transform.localScale;
                scale.x = Mathf.Abs(scale.x) * Facing;
                facingModel.transform.localScale = scale;
            }
        }
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
        SetAimCursor(false);
        if (abilityManager != null && abilityManager.Grab != null) abilityManager.Grab.Abort(true);
    }
    private void OnDestroy()
    {
        if (aimCursor != null) Destroy(aimCursor);
    }

    public void FacingUpdate(int value)
    {
        if (UsesMouseAim)
        {
            if (!LockFacing) Facing = MouseFacing();
            return;
        }
        Facing = value;
    }
}
