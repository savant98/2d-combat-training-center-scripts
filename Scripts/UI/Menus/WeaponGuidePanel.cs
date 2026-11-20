using TMPro;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public sealed class WeaponGuidePanel : MonoBehaviour
{
    private static readonly Color TitleColor = new(0.70f, 0.89f, 1f);
    private static readonly Color BodyColor = new(0.86f, 0.94f, 1f);
    private UnityEngine.UI.Image accent;
    private TextMeshProUGUI summary;
    private TextMeshProUGUI synergy;

    public void Configure(TMP_FontAsset font)
    {
        if (summary != null) return;
        Fill("Surface", Vector2.zero, Vector2.one, new Color(0.035f, 0.115f, 0.16f, 0.62f));
        Fill("UpperRule", new Vector2(0f, 0.994f), Vector2.one, new Color(0.30f, 0.65f, 0.83f, 0.65f));
        accent = Fill("Accent", new Vector2(0f, 0.69f), new Vector2(0.006f, 1f), TitleColor);
        Fill("Divider", new Vector2(0.043f, 0.505f), new Vector2(0.957f, 0.51f), new Color(0.31f, 0.60f, 0.77f, 0.29f));
        Label("SummaryTitle", "기술 효과", font, 22f, TitleColor, 0.81f, 0.95f);
        summary = Label("Summary", string.Empty, font, 24f, BodyColor, 0.56f, 0.80f);
        Label("TraitTitle", "무장 특성 연계", font, 22f, TitleColor, 0.31f, 0.45f);
        synergy = Label("TraitSynergy", string.Empty, font, 24f, BodyColor, 0.06f, 0.30f);
    }

    public void Show(WeaponBaseSO weapon, Color color)
    {
        var guide = ArmamentMenuGuide.Gear(weapon);
        gameObject.SetActive(!string.IsNullOrWhiteSpace(guide.summary));
        if (!gameObject.activeSelf || summary == null) return;
        accent.color = color;
        summary.text = UpgradeMenuPage.EvenLines(guide.summary);
        synergy.text = UpgradeMenuPage.EvenLines(guide.synergy);
    }

    private UnityEngine.UI.Image Fill(string name, Vector2 min, Vector2 max, Color color)
    {
        var rect = Region(name, min, max);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private TextMeshProUGUI Label(string name, string text, TMP_FontAsset font, float size, Color color, float bottom, float top)
    {
        var rect = Region(name, new Vector2(0.043f, bottom), new Vector2(0.957f, top));
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.lineSpacing = 3f;
        label.enableAutoSizing = true;
        label.fontSizeMin = size - 5f;
        label.fontSizeMax = size;
        label.overflowMode = TextOverflowModes.Truncate;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.raycastTarget = false;
        return label;
    }

    private RectTransform Region(string name, Vector2 min, Vector2 max)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(transform, false);
        rect.gameObject.layer = gameObject.layer;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
}
