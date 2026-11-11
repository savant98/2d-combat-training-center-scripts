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
    readonly private Dictionary<Button, ButtonData> buttonDatas = new();

    public Vector2 Move { get; private set; }
    public Vector2 MenuMove => inputActions.UI.Navigate.ReadValue<Vector2>();
    private readonly ButtonData menuConfirm = new();
    private readonly ButtonData menuCancel = new();

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
        // 기존 UI map을 메뉴 입력의 원본으로 사용한다. gameplay 키를 바꿔도 메뉴 Z/X는 유지한다.
        inputActions.UI.Submit.AddBinding("<Keyboard>/z");
        inputActions.UI.Cancel.AddBinding("<Keyboard>/x");
        LoadBindings(DataManager.Instance.Options.bindingOverrides);
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
    }

    public void LoadBindings(string json)
    {
        inputActions.asset.RemoveAllBindingOverrides();
        if (string.IsNullOrEmpty(json)) return;
        try { inputActions.asset.LoadBindingOverridesFromJson(json); }
        catch (System.Exception e)
        {
            inputActions.asset.RemoveAllBindingOverrides();
            Debug.LogWarning("[InputManager] 저장된 키 설정을 읽지 못해 기본키를 사용합니다: " + e.Message);
        }
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
        menuConfirm.state = menuCancel.state = ButtonState.None;
        menuConfirm.pressedThisFrame = menuCancel.pressedThisFrame = false;
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
        // Pressed/Released는 한 프레임만 유지
        var keys = new List<Button>(buttonDatas.Keys);
        foreach (var key in keys)
        {
            UpdateButton(buttonDatas[key]);
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
    public static bool Held(Button b) =>
        Instance != null && Instance.buttonDatas.TryGetValue(b, out var buttonData) && buttonData.state == ButtonState.Held;

    public static bool Released(Button b) =>
        Instance != null && Instance.buttonDatas.TryGetValue(b, out var buttonData) && buttonData.state == ButtonState.Released;
}
