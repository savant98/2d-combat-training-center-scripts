using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    [System.Serializable]
    private class ButtonData
    {
        public ButtonState state;
        public bool pressedThisFrame;
    }

    public UnityEngine.InputSystem.InputActionAsset Actions => inputActions.asset;
    private bool callbacksBound;
    private readonly List<UnityEngine.InputSystem.InputActionMap> suspendedMaps = new();
    private bool rebindSuspended;
    private PlayerInputActions inputActions;
    public bool MouseAim { get; private set; }

    private static readonly (string action, string binding, string path)[] MouseAimDefaults =
    {
        ("Move", "up", "<Keyboard>/w"),
        ("Move", "down", "<Keyboard>/s"),
        ("Move", "left", "<Keyboard>/a"),
        ("Move", "right", "<Keyboard>/d"),
        ("Jump", null, "<Keyboard>/space"),
        ("Attack", null, "<Mouse>/leftButton"),
        ("Dash", null, "<Mouse>/rightButton"),
        ("SecondaryWeapon", null, "<Keyboard>/q"),
        ("Interact", null, "<Keyboard>/e"),
        ("SwapWeapon", null, "<Keyboard>/f"),
        ("Gear", null, "<Keyboard>/shift")
    };
    readonly private Dictionary<Button, ButtonData> buttonDatas = new();

    public Vector2 Move { get; private set; }
    public Vector2 MenuMove => inputActions.UI.Navigate.ReadValue<Vector2>();
    private readonly ButtonData menuConfirm = new();
    private readonly ButtonData menuCancel = new();
    private readonly ButtonData storeSkill = new();
    private InputAction storeSkillAction;
    public static event System.Action BindingsChanged;
    public static void NotifyBindingsChanged() => BindingsChanged?.Invoke();

    public bool HasStoreSkillBinding => storeSkillAction != null && storeSkillAction.bindings.Count > 0
        && !string.IsNullOrEmpty(storeSkillAction.bindings[0].effectivePath);

    public static bool StoreSkillPressed() => Instance != null && Instance.HasStoreSkillBinding && ConsumeMenuButton(Instance.storeSkill);

    public static bool MenuConfirmPressed() => Instance != null && ConsumeMenuButton(Instance.menuConfirm);
    public static bool MenuCancelPressed() => Instance != null && ConsumeMenuButton(Instance.menuCancel);
    private static bool ConsumeMenuButton(ButtonData data)
    {
        if (data.state != ButtonState.Pressed) return false;
        data.state = ButtonState.Held;
        return true;
    }

    private static bool IsMenuControl(UnityEngine.InputSystem.InputAction.CallbackContext context, string keyboardPath) =>
        !(context.control.device is UnityEngine.InputSystem.Keyboard) ||
        string.Equals(context.control.path.Substring(context.control.path.LastIndexOf('/') + 1), keyboardPath, System.StringComparison.OrdinalIgnoreCase);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        inputActions = new PlayerInputActions();
        storeSkillAction = inputActions.asset.FindAction("Player/StoreSkill", true);
        // 기존 UI map을 메뉴 입력의 원본으로 사용한다. gameplay 키를 바꿔도 메뉴 Z/X는 유지한다.
        inputActions.UI.Submit.AddBinding("<Keyboard>/z");
        inputActions.UI.Cancel.AddBinding("<Keyboard>/x");
        LoadControlProfile(DataManager.Instance.Options);
    }

    private void OnEnable()
    {
        if (inputActions == null) return;
        inputActions.Enable();
        if (callbacksBound) return;
        callbacksBound = true;

        buttonDatas[Button.Jump] = new ButtonData();
        buttonDatas[Button.Attack] = new ButtonData();
        buttonDatas[Button.Dash] = new ButtonData();
        buttonDatas[Button.Gear] = new ButtonData();
        buttonDatas[Button.SecondaryWeapon] = new ButtonData();
        buttonDatas[Button.Interact] = new ButtonData();
        buttonDatas[Button.SwapWeapon] = new ButtonData();
        buttonDatas[Button.Escape] = new ButtonData();
        buttonDatas[Button.ToggleAutoAim] = new ButtonData();

        inputActions.UI.Submit.performed += ctx =>
        {
            if (IsMenuControl(ctx, "z")) { menuConfirm.state = ButtonState.Pressed; menuConfirm.pressedThisFrame = true; }
        };
        inputActions.UI.Submit.canceled += _ => menuConfirm.state = ButtonState.Released;
        inputActions.UI.Cancel.performed += ctx =>
        {
            if (IsMenuControl(ctx, "x")) { menuCancel.state = ButtonState.Pressed; menuCancel.pressedThisFrame = true; }
        };
        inputActions.UI.Cancel.canceled += _ => menuCancel.state = ButtonState.Released;

        // Jump
        inputActions.Player.Jump.started += ctx => { buttonDatas[Button.Jump].state = ButtonState.Pressed; buttonDatas[Button.Jump].pressedThisFrame = true; };
        inputActions.Player.Jump.canceled += ctx => buttonDatas[Button.Jump].state = ButtonState.Released;

        // Move
        inputActions.Player.Move.performed += ctx => Move = ctx.ReadValue<Vector2>();
        inputActions.Player.Move.canceled += ctx => Move = Vector2.zero;

        storeSkillAction.started += _ => { storeSkill.state = ButtonState.Pressed; storeSkill.pressedThisFrame = true; };
        storeSkillAction.canceled += _ => storeSkill.state = ButtonState.Released;

        // NormalAttack
        inputActions.Player.Attack.started += ctx => { buttonDatas[Button.Attack].state = ButtonState.Pressed; buttonDatas[Button.Attack].pressedThisFrame = true; };
        inputActions.Player.Attack.canceled += ctx => buttonDatas[Button.Attack].state = ButtonState.Released;

        // SkillAttack
        inputActions.Player.Gear.started += ctx => { buttonDatas[Button.Gear].state = ButtonState.Pressed; buttonDatas[Button.Gear].pressedThisFrame = true; };
        inputActions.Player.Gear.canceled += ctx => buttonDatas[Button.Gear].state = ButtonState.Released;

        // SecondaryWeapon
        inputActions.Player.SecondaryWeapon.started += ctx => { buttonDatas[Button.SecondaryWeapon].state = ButtonState.Pressed; buttonDatas[Button.SecondaryWeapon].pressedThisFrame = true; };
        inputActions.Player.SecondaryWeapon.canceled += ctx => buttonDatas[Button.SecondaryWeapon].state = ButtonState.Released;

        // Jump
        inputActions.Player.Dash.started += ctx => { buttonDatas[Button.Dash].state = ButtonState.Pressed; buttonDatas[Button.Dash].pressedThisFrame = true; };
        inputActions.Player.Dash.canceled += ctx => buttonDatas[Button.Dash].state = ButtonState.Released;

        // Interact
        inputActions.Player.Interact.started += ctx => { buttonDatas[Button.Interact].state = ButtonState.Pressed; buttonDatas[Button.Interact].pressedThisFrame = true; };
        inputActions.Player.Interact.canceled += ctx => buttonDatas[Button.Interact].state = ButtonState.Released;

        // SwapWeapon
        inputActions.Player.SwapWeapon.started += ctx => { buttonDatas[Button.SwapWeapon].state = ButtonState.Pressed; buttonDatas[Button.SwapWeapon].pressedThisFrame = true; };
        inputActions.Player.SwapWeapon.canceled += ctx => buttonDatas[Button.SwapWeapon].state = ButtonState.Released;

        // Escape
        inputActions.Player.Escape.started += ctx => { buttonDatas[Button.Escape].state = ButtonState.Pressed; buttonDatas[Button.Escape].pressedThisFrame = true; };
        inputActions.Player.Escape.canceled += ctx => buttonDatas[Button.Escape].state = ButtonState.Released;

        // ToggleAutoAim(자동 조준 켜기·끄기 전환 키, 2026-11-26 사용자 요청 "자동조준 ON/OFF를 키 할당해서 관리").
        // 다른 버튼과 같이 눌림·뗌만 기록하고, 전환은 게임플레이 입력(PlayerController)이 읽어 처리한다(메뉴·안내 중에는 그쪽에서 막힌다).
        // 기본 키 V는 마우스 조준 모드에서도 쓰지 않는 키라 MouseAimDefaults에 따로 넣지 않는다.
        inputActions.Player.ToggleAutoAim.started += ctx => { buttonDatas[Button.ToggleAutoAim].state = ButtonState.Pressed; buttonDatas[Button.ToggleAutoAim].pressedThisFrame = true; };
        inputActions.Player.ToggleAutoAim.canceled += ctx => buttonDatas[Button.ToggleAutoAim].state = ButtonState.Released;
    }

    public void LoadControlProfile(OptionSettings settings)
    {
        MouseAim = settings.mouseAim;
        LoadBindings(settings.ActiveBindingOverrides);
    }

    private void ApplyProfileDefaults()
    {
        if (!MouseAim) return;
        foreach (var entry in MouseAimDefaults)
        {
            var action = Actions.FindAction("Player/" + entry.action, true);
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (!binding.path.StartsWith("<Keyboard>/", System.StringComparison.OrdinalIgnoreCase)
                    || (entry.binding != null && binding.name != entry.binding)) continue;
                action.ApplyBindingOverride(i, entry.path);
                break;
            }
        }
    }

    public void LoadBindings(string json)
    {
        bool wasSuspended = rebindSuspended;
        if (!wasSuspended) SuspendForRebind(true);
        inputActions.asset.RemoveAllBindingOverrides();
        ApplyProfileDefaults();
        try
        {
            if (!string.IsNullOrEmpty(json)) inputActions.asset.LoadBindingOverridesFromJson(json, removeExisting: false);
        }
        catch (System.Exception e)
        {
            inputActions.asset.RemoveAllBindingOverrides();
            ApplyProfileDefaults();
            Debug.LogWarning("[InputManager] 저장된 키 설정을 읽지 못해 기본키를 사용합니다: " + e.Message);
        }
        ReleaseConflictingToggleAutoAim();
        if (!wasSuspended) SuspendForRebind(false);
        NotifyBindingsChanged();
    }

    // 자동 조준 전환 키는 2026-11-26에 새로 생긴 조작이라, 그 전에 저장한 키 설정에는 항목이 없어 기본 키(V)가 그대로 쓰인다.
    // 사용자가 예전에 다른 조작을 그 키로 바꿔 두었다면 한 번 누를 때 두 조작이 함께 발동하므로(옵션 창의 중복 검사는 키를 바꿀 때만 돈다)
    // 이때만 전환 키를 미지정으로 둔다. 사용자가 직접 지정한(덮어쓰기가 있는) 전환 키는 건드리지 않는다.
    private void ReleaseConflictingToggleAutoAim()
    {
        var toggle = inputActions.Player.ToggleAutoAim;
        for (int i = 0; i < toggle.bindings.Count; i++)
        {
            var binding = toggle.bindings[i];
            if (binding.isComposite || binding.overridePath != null || string.IsNullOrEmpty(binding.effectivePath)) continue;
            if (IsUsedByOtherPlayerAction(toggle, binding.effectivePath)) toggle.ApplyBindingOverride(i, string.Empty);
        }
    }

    private bool IsUsedByOtherPlayerAction(InputAction except, string path)
    {
        foreach (var action in inputActions.Player.Get().actions)
        {
            if (action == except) continue;
            foreach (var binding in action.bindings)
                if (!binding.isComposite && string.Equals(binding.effectivePath, path, System.StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public void SuspendForRebind(bool suspend)
    {
        if (suspend && !rebindSuspended)
        {
            suspendedMaps.Clear();
            foreach (var map in inputActions.asset.actionMaps) if (map.enabled) suspendedMaps.Add(map);
            rebindSuspended = true;
            inputActions.Disable();
        }
        else if (!suspend && rebindSuspended)
        {
            if (isActiveAndEnabled) foreach (var map in suspendedMaps) map.Enable();
            suspendedMaps.Clear();
            rebindSuspended = false;
        }
        ClearButtonStates();
    }

    public void ClearButtonStates()
    {
        Move = Vector2.zero;
        menuConfirm.state = menuCancel.state = storeSkill.state = ButtonState.None;
        menuConfirm.pressedThisFrame = menuCancel.pressedThisFrame = storeSkill.pressedThisFrame = false;
        foreach (var data in buttonDatas.Values) { data.state = ButtonState.None; data.pressedThisFrame = false; }
    }

    private void OnDisable() { inputActions?.Disable(); ClearButtonStates(); }
    private void OnDestroy()
    {
        inputActions?.Dispose();
        if (Instance == this) Instance = null;
    }

    private void LateUpdate()
    {
        UpdateButton(menuConfirm);
        UpdateButton(menuCancel);
        UpdateButton(storeSkill);
        // Pressed/Released는 한 프레임만 유지
        // 매 프레임 키 목록(List)을 새로 만들던 것을 값 직접 순회로 바꿔 할당을 없앤다.
        // UpdateButton은 ButtonData의 필드만 바꾸고 Dictionary에 넣거나 빼지 않으므로 순회 중 수정 예외가 없고, 순서도 키 목록과 같다.
        foreach (var data in buttonDatas.Values)
        {
            UpdateButton(data);
        }
    }
    private void UpdateButton(ButtonData button)
    {
        if (button.pressedThisFrame)
            button.pressedThisFrame = false; // 한 프레임 유지 후 다음 프레임부터 Held로 전환
        else if (button.state == ButtonState.Pressed)
            button.state = ButtonState.Held;
        else if (button.state == ButtonState.Released)
            button.state = ButtonState.None;
    }

    public static bool Pressed(Button b)
    {
        if (Instance == null) return false;

        if (Instance.buttonDatas.TryGetValue(b, out var buttonData))
        {
            if (buttonData.state == ButtonState.Pressed)
            {
                buttonData.state = ButtonState.Held;
                return true;
            }
        }
        return false;
    }
    public static bool PressedAnyButton()
    {
        if (Instance == null)
            return false;

        foreach (var pair in Instance.buttonDatas)
        {
            if (pair.Value.state == ButtonState.Pressed)
                return true;
        }

        return false;
    }
    // 이동 입력에 아래가 들어 있는지(2026-11-17, 저장 기술의 '아래 + 공격'). 다른 방향이 함께 눌려 있어도 아래가 들어 있으면 true다.
    // 합친 Move가 아래 세 방향(아래, 왼쪽 아래, 오른쪽 아래 = 수평에서 아래로 22.5도 넘게)에 있거나, 키보드 합성 바인딩의 down 키가 눌려 있으면
    // (위와 아래를 함께 눌러 Move가 0이어도) 아래로 본다. 입력 설정 에셋은 바꾸지 않는다.
    public bool MoveDownHeld
    {
        get
        {
            if (IsDownward(Move)) return true;
            if (inputActions == null) return false;
            var action = inputActions.Player.Move;
            var controls = action.controls;
            for (int i = 0; i < controls.Count; i++)
            {
                int index = action.GetBindingIndexForControl(controls[i]);
                if (index < 0) continue;
                var binding = action.bindings[index];
                if (binding.isPartOfComposite && binding.name == "down" && controls[i].IsPressed()) return true;
            }
            return false;
        }
    }
    public static bool IsDownward(Vector2 move) => move.sqrMagnitude > 0.0001f && -move.y >= 0.3827f * move.magnitude;

    public static bool Held(Button b) =>
        Instance != null && Instance.buttonDatas.TryGetValue(b, out var buttonData) && buttonData.state == ButtonState.Held;

    public static bool Released(Button b) =>
        Instance != null && Instance.buttonDatas.TryGetValue(b, out var buttonData) && buttonData.state == ButtonState.Released;
}
