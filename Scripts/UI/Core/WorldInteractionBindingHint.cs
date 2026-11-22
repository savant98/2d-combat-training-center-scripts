using TMPro;
using UnityEngine;

/// <summary>훈련 입구의 상호작용 표지에 현재 키를 표시한다.</summary>
[DisallowMultipleComponent]
public sealed class WorldInteractionBindingHint : MonoBehaviour
{
    private UnityEngine.UI.Image defaultArrow;
    private TextMeshProUGUI label;
    private RectTransform keycap;

    public static void Bind(CanvasGroup prompt)
    {
        if (prompt == null || prompt.GetComponent<WorldInteractionBindingHint>() != null) return;
        var arrow = prompt.transform.Find("ButtonImage")?.GetComponent<UnityEngine.UI.Image>();
        if (arrow == null) return;
        var hint = prompt.gameObject.AddComponent<WorldInteractionBindingHint>();
        hint.defaultArrow = arrow;
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
        if (defaultArrow == null) return;
        string key = InputBindingDisplay.Get("Interact");
        bool useArrow = key == "↑";
        defaultArrow.enabled = useArrow;
        if (!useArrow && label == null) CreateKeycap();
        if (keycap == null) return;
        keycap.gameObject.SetActive(!useArrow);
        if (!useArrow) label.text = key;
    }

    private void CreateKeycap()
    {
        var plate = ChildRect(defaultArrow.rectTransform, "BoundInteractKey", new Vector2(10f, 18f));
        keycap = plate;
        var background = plate.gameObject.AddComponent<UnityEngine.UI.Image>();
        background.color = new Color32(10, 33, 48, 242);
        background.raycastTarget = false;

        var frame = ChildRect(plate, "Frame", Vector2.zero).gameObject.AddComponent<TrainingHologramGraphic>();
        frame.Configure(TrainingHologramGraphic.Shape.Frame, new Color32(166, 234, 255, 255), 3f, 6f);
        frame.raycastTarget = false;

        label = ChildRect(plate, "Label", new Vector2(5f, 3f)).gameObject.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color32(232, 250, 255, 255);
        label.enableAutoSizing = true;
        label.fontSizeMin = 13f;
        label.fontSizeMax = 44f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        label.richText = false;
    }

    private static RectTransform ChildRect(RectTransform parent, string name, Vector2 inset)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = inset;
        rect.offsetMax = -inset;
        return rect;
    }
}
