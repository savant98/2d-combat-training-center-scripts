using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 기본 무장 소개 영상의 3D 흰색 키캡 줄(KeyLegend / SkillCard 키 줄)을 실시간으로 그려 주는 컴포넌트.
/// 현재 바인딩 문자열을 읽어 키캡을 배치하고, 실습 중 해당 키를 실제로 누르면 키캡이 물리적으로 눌리며 강조색으로 점등한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialKeycapStrip : MonoBehaviour
{
    public readonly struct Item
    {
        public readonly bool IsKey;
        public readonly string ActionName;     // 실시간 눌림 감지용 Player 액션 이름(예: "Gear", "Attack", "StoreSkill", null이면 표시 전용)
        public readonly string Text;           // 키캡 라벨 또는 구분자·설명 텍스트
        public readonly string HoldNote;       // 예: "길게"
        public readonly bool Muted;

        private Item(bool isKey, string actionName, string text, string holdNote, bool muted)
        {
            IsKey = isKey;
            ActionName = actionName;
            Text = text;
            HoldNote = holdNote;
            Muted = muted;
        }

        public static Item Key(string actionName, string displayLabel, string holdNote = null) =>
            new Item(true, actionName, displayLabel, holdNote, false);

        public static Item Label(string text, bool muted = false) =>
            new Item(false, null, text, null, muted);
    }

    private sealed class KeyVisual
    {
        public string ActionName;
        public TutorialCardGraphic Cap;
        public RectTransform TextRect;
        public TextMeshProUGUI Label;
        public float BaseTextY;
        public float Pressed;
    }

    private RectTransform root;
    private TMP_FontAsset font;
    private string namePrefix = "KeyStrip";
    private Color accentColor = new Color(0.30f, 0.85f, 1f, 1f);
    private bool centerAlign;
    private float keyHeight = 36f;
    private float fontSize = 18f;
    private readonly List<GameObject> nodes = new();
    private readonly List<KeyVisual> keys = new();
    private readonly List<string> renderedKeys = new();

    public IReadOnlyList<string> RenderedKeys => renderedKeys;
    public int KeyCount => keys.Count;

    public void Initialize(TMP_FontAsset labelFont, string prefix, bool centered = false)
    {
        root = transform as RectTransform;
        font = labelFont;
        namePrefix = string.IsNullOrEmpty(prefix) ? "KeyStrip" : prefix;
        centerAlign = centered;
    }

    public void SetItems(IReadOnlyList<Item> items, Color accent, float height = 36f, float textSize = 18f, float availableWidth = 0f)
    {
        if (root == null) root = transform as RectTransform;
        accentColor = accent;
        keyHeight = Mathf.Max(24f, height);
        fontSize = Mathf.Max(13f, textSize);
        ClearNodes();
        if (items == null || items.Count == 0 || root == null) return;

        float maxWidth = availableWidth > 1f ? availableWidth : Mathf.Max(160f, root.rect.width);
        BuildRow(items, maxWidth);
    }

    private void BuildRow(IReadOnlyList<Item> items, float maxWidth)
    {
        // 먼저 기준 크기로 전체 폭을 측정해 넘치면 비율에 맞춰 깔끔하게 축소한다(한 줄 유지, 넘침 방지).
        float scale = 1f;
        float totalWidth = MeasureTotal(items, keyHeight, fontSize);
        if (totalWidth > maxWidth && totalWidth > 1f)
            scale = Mathf.Clamp((maxWidth - 4f) / totalWidth, 0.50f, 1f);

        float h = Mathf.Round(keyHeight * scale);
        float keyFont = Mathf.Max(11f, Mathf.Round(fontSize * scale));
        float labelFont = Mathf.Max(11f, Mathf.Round(fontSize * 0.94f * scale));
        float holdFont = Mathf.Max(10.5f, Mathf.Round(fontSize * 0.82f * scale));
        float gap = Mathf.Max(3f, Mathf.Round(8f * scale));
        float actualTotal = MeasureTotal(items, h, keyFont, labelFont, holdFont, gap);
        if (actualTotal > maxWidth && actualTotal > 1f)
        {
            float tighten = Mathf.Clamp((maxWidth - 4f) / actualTotal, 0.72f, 1f);
            keyFont = Mathf.Max(10.5f, keyFont * tighten);
            labelFont = Mathf.Max(10.5f, labelFont * tighten);
            holdFont = Mathf.Max(10f, holdFont * tighten);
            gap = Mathf.Max(2f, gap * tighten);
            actualTotal = MeasureTotal(items, h, keyFont, labelFont, holdFont, gap);
        }
        float containerWidth = root.rect.width > 1f ? root.rect.width : maxWidth;
        float x = centerAlign ? Mathf.Max(0f, (containerWidth - actualTotal) * 0.5f) : 0f;
        float centerY = 0f;
        int serial = 0;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.IsKey)
            {
                string label = string.IsNullOrEmpty(item.Text) ? "?" : item.Text;
                renderedKeys.Add(label);
                float kw = KeyWidth(label, h, keyFont);
                var capRect = CreateRect(namePrefix + "_Cap_" + serial++);
                capRect.anchorMin = capRect.anchorMax = new Vector2(0f, 0.5f);
                capRect.pivot = new Vector2(0f, 0.5f);
                capRect.anchoredPosition = new Vector2(x, centerY);
                capRect.sizeDelta = new Vector2(kw, h);

                var capGraphic = capRect.gameObject.AddComponent<TutorialCardGraphic>();
                capGraphic.ConfigureKeycap(accentColor, Mathf.Min(9f, h * 0.22f));

                var textObj = CreateText(capRect, namePrefix + "_KeyText_" + serial++, label, keyFont,
                    new Color(0.082f, 0.102f, 0.129f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
                float lipOffset = Mathf.Clamp(h * 0.06f, 1.5f, 3.5f);
                textObj.rectTransform.anchorMin = new Vector2(0f, 0.5f);
                textObj.rectTransform.anchorMax = new Vector2(1f, 0.5f);
                textObj.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                textObj.rectTransform.sizeDelta = new Vector2(-4f, h);
                textObj.rectTransform.anchoredPosition = new Vector2(0f, lipOffset);

                keys.Add(new KeyVisual
                {
                    ActionName = item.ActionName,
                    Cap = capGraphic,
                    TextRect = textObj.rectTransform,
                    Label = textObj,
                    BaseTextY = lipOffset,
                    Pressed = 0f
                });
                x += kw + gap;

                if (!string.IsNullOrEmpty(item.HoldNote))
                {
                    float hw = ApproxTextWidth(item.HoldNote, holdFont) + 6f;
                    var hold = CreateText(root, namePrefix + "_Hold_" + serial++, item.HoldNote, holdFont,
                        new Color(0.62f, 0.69f, 0.76f, 1f), TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                    PlaceInlineText(hold, x, centerY, hw, Mathf.Max(h, holdFont * 1.5f));
                    x += hw + gap;
                }
            }
            else if (!string.IsNullOrEmpty(item.Text))
            {
                bool isArrow = item.Text == "→" || item.Text == "·" || item.Text == "+";
                float fs = isArrow ? keyFont * 1.05f : labelFont;
                Color col = item.Muted || isArrow
                    ? new Color(0.75f, 0.81f, 0.87f, 1f)
                    : new Color(0.94f, 0.96f, 0.98f, 1f);
                float lw = ApproxTextWidth(item.Text, fs) + (isArrow ? 6f : 8f);
                var txt = CreateText(root, namePrefix + "_Txt_" + serial++, item.Text, fs, col,
                    isArrow ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft,
                    isArrow ? FontStyles.Bold : FontStyles.Normal);
                PlaceInlineText(txt, x, centerY, lw, Mathf.Max(h, fs * 1.5f));
                x += lw + gap;
            }
        }
    }

    private void Update()
    {
        if (keys.Count == 0) return;
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < keys.Count; i++)
        {
            var kv = keys[i];
            if (kv.Cap == null) continue;
            bool down = IsActionPressed(kv.ActionName);
            float target = down ? 1f : 0f;
            float next = Mathf.MoveTowards(kv.Pressed, target, dt * (down ? 16f : 9f));
            if (!Mathf.Approximately(next, kv.Pressed))
            {
                kv.Pressed = next;
                kv.Cap.SetKeycapState(next, accentColor);
                if (kv.TextRect != null)
                {
                    float shift = Mathf.Clamp(keyHeight * 0.08f, 1.5f, 4f) * next;
                    kv.TextRect.anchoredPosition = new Vector2(0f, kv.BaseTextY - shift);
                }
            }
        }
    }

    private static bool IsActionPressed(string actionName)
    {
        if (string.IsNullOrEmpty(actionName) || InputManager.Instance == null || InputManager.Instance.Actions == null)
            return false;
        if (actionName == "UI/Submit")
        {
            var submit = InputManager.Instance.Actions.FindAction("UI/Submit", false);
            return submit != null && submit.IsPressed();
        }
        if (actionName == "StoreSkill")
        {
            if (InputManager.Instance.HasStoreSkillBinding)
            {
                var store = InputManager.Instance.Actions.FindAction("Player/StoreSkill", false);
                return store != null && store.IsPressed();
            }
            return InputManager.Held(Button.Attack) && InputManager.Instance.Move.y < -0.35f;
        }
        if (actionName == "Move")
            return Mathf.Abs(InputManager.Instance.Move.x) > 0.1f;

        var action = InputManager.Instance.Actions.FindAction("Player/" + actionName, false);
        if (action != null && action.IsPressed()) return true;

        return actionName switch
        {
            "Jump" => InputManager.Held(Button.Jump),
            "Dash" => InputManager.Held(Button.Dash),
            "Attack" => InputManager.Held(Button.Attack),
            "Gear" => InputManager.Held(Button.Gear),
            "SwapWeapon" => InputManager.Held(Button.SwapWeapon),
            "SecondaryWeapon" => InputManager.Held(Button.SecondaryWeapon),
            _ => false
        };
    }

    private static float MeasureTotal(IReadOnlyList<Item> items, float h, float keyFont) =>
        MeasureTotal(items, h, keyFont, keyFont * 0.94f, keyFont * 0.82f, 8f);

    private static float MeasureTotal(IReadOnlyList<Item> items, float h, float keyFont, float labelFont, float holdFont, float gap)
    {
        float total = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.IsKey)
            {
                total += KeyWidth(item.Text, h, keyFont) + gap;
                if (!string.IsNullOrEmpty(item.HoldNote))
                    total += ApproxTextWidth(item.HoldNote, holdFont) + 6f + gap;
            }
            else if (!string.IsNullOrEmpty(item.Text))
            {
                bool isArrow = item.Text == "→" || item.Text == "·" || item.Text == "+";
                float fs = isArrow ? keyFont * 1.05f : labelFont;
                total += ApproxTextWidth(item.Text, fs) + (isArrow ? 6f : 8f) + gap;
            }
        }
        return Mathf.Max(0f, total - gap);
    }

    private static float KeyWidth(string label, float h, float keyFont)
    {
        float tw = ApproxTextWidth(string.IsNullOrEmpty(label) ? "?" : label, keyFont);
        return Mathf.Max(h * 1.08f, tw + h * 0.62f);
    }

    // esamanru Medium 기준 한글 0.96em · 영문/숫자/기호 0.62em으로 여유 있게 측정한다.
    private static float ApproxTextWidth(string text, float size)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        float units = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ' ') units += 0.34f;
            else if (c < 128) units += 0.66f;
            else if (c == '·') units += 0.42f;
            else units += 0.98f;
        }
        return units * size;
    }

    private void PlaceInlineText(TextMeshProUGUI txt, float x, float y, float w, float h)
    {
        var r = txt.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta = new Vector2(w, h);
    }

    private RectTransform CreateRect(string objectName)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.layer = gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(root, false);
        nodes.Add(go);
        return rect;
    }

    private TextMeshProUGUI CreateText(RectTransform parent, string objectName, string value, float size, Color color,
        TextAlignmentOptions alignment, FontStyles style)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        if (parent == root) nodes.Add(go);
        var txt = go.GetComponent<TextMeshProUGUI>();
        if (font != null) txt.font = font;
        txt.text = value;
        txt.fontSize = size;
        txt.color = color;
        txt.alignment = alignment;
        txt.fontStyle = style;
        txt.enableAutoSizing = false;
        txt.textWrappingMode = TextWrappingModes.NoWrap;
        txt.overflowMode = TextOverflowModes.Overflow;
        txt.raycastTarget = false;
        return txt;
    }

    private void ClearNodes()
    {
        for (int i = 0; i < nodes.Count; i++)
            if (nodes[i] != null) Destroy(nodes[i]);
        nodes.Clear();
        keys.Clear();
        renderedKeys.Clear();
    }
}
