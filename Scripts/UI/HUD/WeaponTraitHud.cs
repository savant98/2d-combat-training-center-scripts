using TMPro;
using UnityEngine;

[DefaultExecutionOrder(160)]
public sealed class WeaponTraitHud : UnityEngine.UI.MaskableGraphic
{
    private static readonly Color Plate = new(0.035f, 0.060f, 0.10f, 0.86f);
    private static readonly Color Empty = new(0.25f, 0.34f, 0.46f, 0.87f);
    private readonly Vector3[] corners = new Vector3[4];
    private PlayerWeaponAbility attack;
    private PlayerWeaponTraits traits;
    private PlayerController player;
    private Camera gameplayCamera;
    private TextMeshProUGUI caption;
    private RectTransform avoid;
    private CanvasGroup visibility;
    private string armament;
    private int count, capacity, mode;
    private float ratio;
    private Color accent;

    public void Configure(TMP_FontAsset font, RectTransform avoid)
    {
        this.avoid = avoid;
        visibility = gameObject.AddComponent<CanvasGroup>();
        visibility.alpha = 0f;
        visibility.blocksRaycasts = false;
        visibility.interactable = false;
        var label = new GameObject("Status", typeof(RectTransform));
        label.transform.SetParent(transform, false);
        caption = label.AddComponent<TextMeshProUGUI>();
        if (font != null) caption.font = font;
        caption.fontSize = 19f;
        caption.fontStyle = FontStyles.Bold;
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.textWrappingMode = TextWrappingModes.NoWrap;
        caption.raycastTarget = false;
        var rect = caption.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(94f, 0f);
        rect.offsetMax = new Vector2(-8f, 0f);
    }

    private void LateUpdate()
    {
        if (visibility == null) return;
        player = GameManager.Instance?.Player;
        var next = player != null ? player.AbilityManager?.AttackAbility : null;
        if (next != attack)
        {
            if (traits != null) traits.Changed -= Refresh;
            attack = next;
            traits = attack != null ? attack.Traits : null;
            if (traits != null) traits.Changed += Refresh;
        }
        Refresh();
        visibility.alpha = PositionNearPlayer() ? 1f : 0f;
    }

    private bool PositionNearPlayer()
    {
        if (player == null || player.Health.IsDead || string.IsNullOrEmpty(armament)) return false;
        if (gameplayCamera == null || !gameplayCamera.isActiveAndEnabled) gameplayCamera = Camera.main;
        if (gameplayCamera == null || canvas == null || transform.parent is not RectTransform parent) return false;

        var canvasCamera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
        FixedMotionBody.GetPresentationPose(player.transform, out Vector3 position, out _);
        Vector3 projected = gameplayCamera.WorldToScreenPoint(position);
        if (projected.z <= 0f || !gameplayCamera.pixelRect.Contains(projected)) return false;

        rectTransform.GetWorldCorners(corners);
        Vector2 halfSize = (RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[2])
            - RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[0])) * 0.5f;
        Rect safe = Screen.safeArea;
        Rect viewport = gameplayCamera.pixelRect;
        Vector2 min = Vector2.Max(safe.min, viewport.min) + halfSize + Vector2.one * 5f;
        Vector2 max = Vector2.Min(safe.max, viewport.max) - halfSize - Vector2.one * 5f;
        if (max.x < min.x || max.y < min.y) return false;

        Vector2 below = gameplayCamera.WorldToScreenPoint(position + Vector3.down * 2.05f);
        below.y -= halfSize.y;
        Vector2 point = below;
        if (point.y < min.y || OverlapsFixedHUD(canvasCamera, point, halfSize))
        {
            point = gameplayCamera.WorldToScreenPoint(position + Vector3.up * 3.9f);
            point.y += halfSize.y;
            if (point.y > max.y || OverlapsFixedHUD(canvasCamera, point, halfSize))
            {
                point = projected;
                float side = projected.x < viewport.center.x ? 1f : -1f;
                point.x += side * (halfSize.x + Mathf.Abs(projected.y - below.y) * 0.68f);
                point.y -= halfSize.y;
            }
        }
        point.x = Mathf.Clamp(point.x, min.x, max.x);
        point.y = Mathf.Clamp(point.y, min.y, max.y);
        if (OverlapsFixedHUD(canvasCamera, point, halfSize)) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, point, canvasCamera, out Vector2 local)) return false;
        rectTransform.localPosition = local;
        return true;
    }

    private bool OverlapsFixedHUD(Camera camera, Vector2 position, Vector2 halfSize)
    {
        if (avoid == null) return false;
        avoid.GetWorldCorners(corners);
        var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        var reserved = Rect.MinMaxRect(min.x - 8f, min.y - 38f, max.x + 8f, max.y + 20f);
        return reserved.Overlaps(new Rect(position - halfSize, halfSize * 2f));
    }

    protected override void OnDestroy()
    {
        if (traits != null) traits.Changed -= Refresh;
        base.OnDestroy();
    }

    private void Refresh()
    {
        if (caption == null) return;
        string name = attack != null ? attack.ActiveArmamentId : null;
        int value = 0, max = 0, style = 0;
        float fill = 0f;
        Color tint = Color.white;
        string title = string.Empty;
        if (traits != null)
        {
            switch (name)
            {
                case "Hammer":
                    bool shield = traits.Barrier > 0f;
                    fill = (shield ? traits.Barrier : traits.BarrierCharge) / PlayerWeaponTraits.BarrierCapacity;
                    tint = shield ? new Color(0.39f, 0.98f, 1f) : new Color(0.32f, 0.66f, 1f);
                    title = $"{(shield ? "방어막" : "충전")} {Mathf.CeilToInt(fill * 100f)}%";
                    break;
                case "Rifle":
                    bool mounted = attack.PrimaryOverrideSetIndex == attack.CurrentSetIndex;
                    bool incendiary = mounted && attack.PrimaryOverrideWeapon is IncendiaryRifleWeaponSO;
                    bool machineGun = mounted && attack.PrimaryOverrideWeapon is MachineGunWeaponSO;
                    bool reloading = traits.ReloadRemaining > 0f && !mounted;
                    style = 1;
                    value = incendiary ? traits.IncendiaryAmmo : traits.RifleAmmo;
                    max = incendiary ? PlayerWeaponTraits.IncendiaryCapacity : PlayerWeaponTraits.MagazineCapacity;
                    tint = incendiary ? WeaponTraitEffects.HeatColor : new Color(0.88f, 0.96f, 0.58f);
                    title = $"{value:D2}/{max:D2}";
                    if (incendiary) title += $" · {attack.PrimaryOverrideRemaining:F1}초";
                    if (machineGun)
                    {
                        title = $"∞ · {attack.PrimaryOverrideRemaining:F1}초";
                        value = max;
                        tint = new Color(0.44f, 0.94f, 1f);
                    }
                    if (reloading)
                    {
                        title = $"장전 {traits.ReloadRemaining:F1}초";
                        fill = 1f - traits.ReloadRemaining / PlayerWeaponTraits.ReloadDuration;
                        value = Mathf.FloorToInt(fill * max);
                    }
                    break;
                case "Sword":
                    style = 2;
                    max = PlayerWeaponTraits.ResolveCapacity;
                    value = traits.Resolve;
                    tint = value == max ? new Color(1f, 0.77f, 0.37f) : new Color(0.81f, 0.66f, 1f);
                    title = $"필살 {value}/{max}";
                    break;
                case "Railgun":
                    style = 3;
                    max = 7;
                    fill = traits.Heat;
                    value = Mathf.CeilToInt(fill * max - 0.001f);
                    tint = traits.IsRailCharged ? WeaponTraitEffects.RailColor : WeaponTraitEffects.HeatColor;
                    title = $"열 {Mathf.RoundToInt(fill * 100f)}%";
                    if (traits.OverheatRemaining > 0f) title = $"복구 {traits.OverheatRemaining:F1}초";
                    else if (traits.IsRailCharged) title += $" · {traits.ChargeRemaining:F1}초";
                    break;
            }
        }
        if (caption.text != title) caption.text = title;
        if (armament == name && count == value && capacity == max && mode == style && ratio == fill && accent == tint) return;
        armament = name;
        count = value;
        capacity = max;
        mode = style;
        ratio = fill;
        accent = tint;
        caption.color = tint;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper v)
    {
        v.Clear();
        if (string.IsNullOrEmpty(armament)) return;
        Rect r = rectTransform.rect;
        Quad(v, r.xMin, r.yMin, r.width - 8f, r.height, 8f, Plate);
        Quad(v, r.xMin + 7.5f, r.yMax - 2f, r.width - 9f, 2f, .5f, Color.Lerp(Empty, accent, .28f));
        float x = r.xMin + 21f, y = r.center.y;
        switch (mode)
        {
            case 0:
                Shield(v, x + 25f, y, ratio);
                break;
            case 1:
                Magazine(v, x - 4f, y);
                break;
            case 2:
                for (int i = 0; i < capacity; i++) Sword(v, x + i * 23f, y, i < count ? accent : Empty);
                break;
            case 3:
                for (int i = 0; i < capacity; i++)
                {
                    float height = 8f + i * 3f;
                    Quad(v, x - 7f + i * 10f, y - 12f, 5f, height, 5f, i < count ? accent : Empty);
                }
                break;
        }
    }

    private void Shield(UnityEngine.UI.VertexHelper v, float x, float y, float progress)
    {
        ShieldSection(v, x, y, 1f, Empty, 1f);
        ShieldSection(v, x, y, 1f, Plate, 0.84f);
        ShieldSection(v, x, y, progress, accent, 0.77f);
    }

    private static void ShieldSection(UnityEngine.UI.VertexHelper v, float x, float y, float fill, Color tint, float size)
    {
        if (fill <= 0f) return;
        float cut = Mathf.Lerp(-15f, 15f, fill) * size;
        float join = -3f * size;
        float first = Mathf.Min(cut, join);
        float w = Mathf.Lerp(0f, 19f * size, Mathf.InverseLerp(-15f * size, join, first));
        Add(v, new Vector2(x, y - 15f * size), new Vector2(x - w, y + first),
            new Vector2(x + w, y + first), new Vector2(x, y - 15f * size), tint);
        if (cut > join)
        {
            float top = Mathf.Lerp(19f, 21f, Mathf.InverseLerp(join, 15f * size, cut)) * size;
            Add(v, new Vector2(x - 19f * size, y + join), new Vector2(x - top, y + cut),
                new Vector2(x + top, y + cut), new Vector2(x + 19f * size, y + join), tint);
        }
    }

    private void Magazine(UnityEngine.UI.VertexHelper v, float x, float y)
    {
        Add(v, new Vector2(x - 3f, y - 10f), new Vector2(x, y + 10f),
            new Vector2(x + 61f, y + 10f), new Vector2(x + 69f, y - 8f), Empty);
        Add(v, new Vector2(x, y - 7.5f), new Vector2(x + 3f, y + 7.5f),
            new Vector2(x + 59f, y + 7.5f), new Vector2(x + 65f, y - 6f), Plate);
        float step = 58f / Mathf.Max(1, capacity);
        for (int i = 0; i < capacity; i++)
            Quad(v, x + 3f + i * step, y - 5.5f, step * 0.66f, 11f, 1f, i < count ? accent : Empty);
    }

    private static void Sword(UnityEngine.UI.VertexHelper v, float x, float y, Color tint)
    {
        Add(v, new Vector2(x - 3.5f, y - 4f), new Vector2(x - 3.5f, y + 7f),
            new Vector2(x, y + 15f), new Vector2(x + 3.5f, y + 7f), tint);
        Quad(v, x - 8f, y - 5f, 16f, 2.5f, 1f, tint);
        Quad(v, x - 1.5f, y - 11f, 3f, 7f, 0f, tint);
        Quad(v, x - 3f, y - 13f, 6f, 2f, 0f, tint);
    }

    private static void Quad(UnityEngine.UI.VertexHelper v, float x, float y, float width, float height, float slant, Color tint)
    {
        Add(v, new Vector2(x, y), new Vector2(x + slant, y + height),
            new Vector2(x + width + slant, y + height), new Vector2(x + width, y), tint);
    }

    private static void Add(UnityEngine.UI.VertexHelper v, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        int i = v.currentVertCount;
        v.AddVert(a, tint, Vector2.zero);
        v.AddVert(b, tint, Vector2.zero);
        v.AddVert(c, tint, Vector2.zero);
        v.AddVert(d, tint, Vector2.zero);
        v.AddTriangle(i, i + 1, i + 2);
        v.AddTriangle(i, i + 2, i + 3);
    }
}
