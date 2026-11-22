using System;
using System.Text.RegularExpressions;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>조작 안내가 실제 키보드 바인딩과 같은 표기를 사용하게 한다.</summary>
public static class InputBindingDisplay
{
    public const string InventoryPath = "<Keyboard>/tab";

    private const string KeyboardPath = "<Keyboard>/";
    private static readonly Regex PromptToken = new Regex(@"\{(\w+)\}");

    public static string StoreSkill => FindLabel("Player/StoreSkill", null) ?? Get("Move", "down") + " + " + Get("Attack");
    public static string Confirm => Get("UI/Submit");
    public static string Cancel
    {
        get
        {
            string cancel = Get("UI/Cancel");
            string escape = Get("Escape");
            return cancel == escape ? cancel : cancel + " / " + escape;
        }
    }

    public static string Get(string action, string part = null)
    {
        if (string.IsNullOrEmpty(action)) return string.Empty;
        string qualified = action.Contains("/") ? action : "Player/" + action;
        if (qualified == "Player/StoreSkill") return StoreSkill;
        if (qualified == "Player/Inventory") return DisplayKey(InventoryPath, "Tab");
        if (qualified == "Player/Move" && string.IsNullOrEmpty(part)) return Get(action, "left") + " / " + Get(action, "right");

        string label = FindLabel(qualified, part);
        if (label != null) return label;
        if (InputManager.Instance != null && InputManager.Instance.Actions.FindAction(qualified) != null) return "미할당";
        return DefaultLabel(qualified, part);
    }

    public static string Title(string action) => action switch
    {
        "Move" => "이동",
        "Jump" => "점프",
        "Dash" => "대시",
        "Attack" => "공격",
        "Gear" => "기어",
        "SecondaryWeapon" => "보조 무기",
        "SwapWeapon" => "무장 전환",
        "Interact" => "상호작용",
        "StoreSkill" => "스킬 저장",
        "Escape" => "취소",
        "Inventory" => "휴대용 단말기",
        _ => action
    };

    public static string Format(string template)
    {
        if (string.IsNullOrEmpty(template)) return template;
        return PromptToken.Replace(template, token => token.Groups[1].Value switch
        {
            "Confirm" => Confirm,
            "Cancel" => Cancel,
            "Escape" => Get("Escape"),
            "MenuUp" => Get("UI/Navigate", "up"),
            "MenuDown" => Get("UI/Navigate", "down"),
            "MenuLeft" => Get("UI/Navigate", "left"),
            "MenuRight" => Get("UI/Navigate", "right"),
            "MenuVertical" => Get("UI/Navigate", "up") + " " + Get("UI/Navigate", "down"),
            "MenuHorizontal" => Get("UI/Navigate", "left") + " " + Get("UI/Navigate", "right"),
            "MenuDirections" => Get("UI/Navigate", "left") + " " + Get("UI/Navigate", "right") + " " + Get("UI/Navigate", "up") + " " + Get("UI/Navigate", "down"),
            "Move" => Get("Move"),
            "Attack" => Get("Attack"),
            "Dash" => Get("Dash"),
            "Gear" => Get("Gear"),
            "Interact" => Get("Interact"),
            "Inventory" => Get("Inventory"),
            "StoreSkill" => StoreSkill,
            _ => token.Value
        });
    }

    public static ButtonControl KeyboardButton(string qualified, string part)
    {
        var keyboard = Keyboard.current;
        var action = FindAction(qualified);
        int index = FindKeyboardBinding(action, qualified, part);
        if (keyboard == null || index < 0) return null;
        return InputControlPath.TryFindControl(keyboard, action.bindings[index].effectivePath) as ButtonControl;
    }

    private static InputAction FindAction(string qualified)
    {
        var manager = InputManager.Instance;
        return manager != null ? manager.Actions.FindAction(qualified) : null;
    }

    private static string FindLabel(string qualified, string part)
    {
        var action = FindAction(qualified);
        int index = FindKeyboardBinding(action, qualified, part);
        return index < 0 ? null : DisplayKey(action.bindings[index].effectivePath, action.GetBindingDisplayString(index, InputBinding.DisplayStringOptions.DontIncludeInteractions));
    }

    private static int FindKeyboardBinding(InputAction action, string qualified, string part)
    {
        if (action == null) return -1;

        int candidate = -1;
        for (int i = 0; i < action.bindings.Count; i++)
        {
            var binding = action.bindings[i];
            string effective = binding.effectivePath;
            if (binding.isComposite || string.IsNullOrEmpty(effective)
                || !effective.StartsWith(KeyboardPath, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(part) && !string.Equals(binding.name, part, StringComparison.OrdinalIgnoreCase))) continue;

            if (candidate < 0) candidate = i;
            if (qualified == "UI/Navigate" && binding.path?.EndsWith("Arrow", StringComparison.OrdinalIgnoreCase) == true)
            {
                candidate = i;
                break;
            }
        }

        return candidate;
    }

    private static string DisplayKey(string path, string label) => path.ToLowerInvariant() switch
    {
        "<keyboard>/uparrow" => "↑",
        "<keyboard>/downarrow" => "↓",
        "<keyboard>/leftarrow" => "←",
        "<keyboard>/rightarrow" => "→",
        "<keyboard>/escape" => "Esc",
        "<keyboard>/leftshift" => "LShift",
        "<keyboard>/rightshift" => "RShift",
        "<keyboard>/leftctrl" => "LCtrl",
        "<keyboard>/rightctrl" => "RCtrl",
        "<keyboard>/leftalt" => "LAlt",
        "<keyboard>/rightalt" => "RAlt",
        _ => label
    };

    private static string DefaultLabel(string qualified, string part) => qualified switch
    {
        "Player/Move" or "UI/Navigate" => part switch { "up" => "↑", "down" => "↓", "left" => "←", "right" => "→", _ => "← → ↑ ↓" },
        "Player/Attack" or "UI/Cancel" => "X",
        "Player/Jump" or "UI/Submit" => "Z",
        "Player/Dash" => "C",
        "Player/Gear" => "Space",
        "Player/SecondaryWeapon" => "A",
        "Player/SwapWeapon" => "S",
        "Player/Interact" => "↑",
        "Player/Escape" => "Esc",
        _ => "미할당"
    };
}
