using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class InputBindingHint : MonoBehaviour
{
    private TMP_Text label;
    private string template;

    public static void Bind(Transform root, string path, string template)
    {
        if (root == null) return;
        Bind(root.Find(path)?.GetComponent<TMP_Text>(), template);
    }

    public static void Bind(TMP_Text text, string template)
    {
        if (text == null) return;
        var hint = text.GetComponent<InputBindingHint>() ?? text.gameObject.AddComponent<InputBindingHint>();
        hint.label = text;
        hint.template = template;
        hint.Refresh();
    }

    private void OnEnable()
    {
        InputManager.BindingsChanged += Refresh;
        Refresh();
    }

    private void OnDisable() => InputManager.BindingsChanged -= Refresh;

    private void Refresh()
    {
        if (label == null) return;
        string value = InputBindingDisplay.Format(template);
        if (label.text != value) label.text = value;
    }
}
