using System.Collections.Generic;
using UnityEngine;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    [System.Serializable]
    private class ButtonData
    {
        public ButtonState state;
        public bool pressedThisFrame;
    }

    private PlayerInputActions inputActions;
    readonly private Dictionary<Button, ButtonData> buttonDatas = new();

    public Vector2 Move { get; private set; }

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
    }

    private void OnEnable()
    {
        inputActions.Enable();

        buttonDatas[Button.Jump] = new ButtonData();
        buttonDatas[Button.Attack] = new ButtonData();
        buttonDatas[Button.Dash] = new ButtonData();
        buttonDatas[Button.Gear] = new ButtonData();
        buttonDatas[Button.SecondaryWeapon] = new ButtonData();
        buttonDatas[Button.Interact] = new ButtonData();
        buttonDatas[Button.SwapWeapon] = new ButtonData();
        buttonDatas[Button.Escape] = new ButtonData();

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

    private void LateUpdate()
    {
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
