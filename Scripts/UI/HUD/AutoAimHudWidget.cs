using TMPro;
using UnityEngine;

/// <summary>
/// PlayerHUD의 무기·보조무기 아이콘 왼쪽 아래에 자동 조준 ON/OFF 상태를 상시 표시하고,
/// 전환 시 총기 옵틱을 바꾸는 듯한 딸깍 효과음과 플레이어 주변 모드 안내("자동 조준 모드" / "수동 조준 모드")를 띄운다.
/// </summary>
[DisallowMultipleComponent]
public sealed class AutoAimHudWidget : MonoBehaviour
{
    public const string ResourcePath = "UI/AutoAimHudWidget";

    // HUD 단위(부모 PlayerHUD, scale 0.8 기준): normalWeapon(-149)·secondaryWeapon(-217) 왼쪽 아래, WeaponTraitHud(x=164, y=-256) 왼쪽 여백 자리.
    public static readonly Vector2 BadgePosition = new(30f, -234f);
    public static readonly Vector2 BadgeSize = new(126f, 46f);

    private const float ToastDuration = 1.2f;
    private const float ToastFadeDuration = 0.24f;
    private const float ToastFloatPixels = 20f;
    private static readonly Color OnColor = new Color32(102, 246, 255, 255);        // #66F6FF
    private static readonly Color OffColor = new Color32(162, 174, 188, 255);       // #A2AEBC
    private static readonly Color SurfaceFill = new Color(0.031f, 0.067f, 0.114f, 0.90f); // #08111D
    private static readonly Color ToastFill = new Color(0.047f, 0.071f, 0.098f, 0.94f);

    [SerializeField] private AudioClip opticClickClip;
    [SerializeField] private AudioClip opticLatchClip;

    private CanvasGroup hudGroup;
    private TMP_FontAsset font;
    private OptionSettingsService source;
    private bool built;
    private bool displayedState;
    private bool hasDisplayedState;
    private float badgePulseAt = -10f;

    // 상시 배지 요소
    private RectTransform badgeRoot;
    private TutorialCardGraphic badgeSurface;
    private TutorialCardGraphic badgeReticle;
    private TextMeshProUGUI badgeTitle;
    private TextMeshProUGUI badgeStatus;

    // 플레이어 주변 토스트 요소
    private RectTransform toastRoot;
    private CanvasGroup toastGroup;
    private TutorialCardGraphic toastSurface;
    private TutorialCardGraphic toastReticle;
    private TextMeshProUGUI toastLabel;
    private float toastStartAt = -10f;
    private float toastUntil = -10f;
    private bool toastActive;

    // 회귀 검증용 읽기 전용 속성
    public RectTransform BadgeRect => badgeRoot;
    public TextMeshProUGUI BadgeStatusLabel => badgeStatus;
    public RectTransform ToastRect => toastRoot;
    public TextMeshProUGUI ToastLabel => toastLabel;
    public bool IsToastVisible => toastActive && toastRoot != null && toastRoot.gameObject.activeInHierarchy;
    public bool DisplayedAutoAim => displayedState;
    public AudioClip OpticClickClip => opticClickClip;
    public int OpticClickPlayCount { get; private set; }

    public void Configure(CanvasGroup ownerGroup, TMP_FontAsset labelFont)
    {
        hudGroup = ownerGroup;
        font = labelFont;
        EnsureVisuals();
        SubscribeSource();
        InputManager.BindingsChanged -= RefreshBadgeText;
        InputManager.BindingsChanged += RefreshBadgeText;
        SyncFromSettings(force: true);
    }

    private void OnEnable()
    {
        if (!built) return;
        SubscribeSource();
        InputManager.BindingsChanged -= RefreshBadgeText;
        InputManager.BindingsChanged += RefreshBadgeText;
        SyncFromSettings(force: true);
    }

    private void OnDisable()
    {
        if (source != null) source.RangedAutoAimToggled -= OnAutoAimToggled;
        source = null;
        InputManager.BindingsChanged -= RefreshBadgeText;
        HideToast();
    }

    private void Update()
    {
        if (!built) return;
        SubscribeSource();
        SyncFromSettings(force: false);
        UpdateBadgePulse();
    }

    private void LateUpdate()
    {
        if (!built) return;
        UpdatePlayerToast();
    }

    private void SubscribeSource()
    {
        var current = OptionSettingsService.Instance;
        if (source == current) return;
        if (source != null) source.RangedAutoAimToggled -= OnAutoAimToggled;
        source = current;
        if (source != null) source.RangedAutoAimToggled += OnAutoAimToggled;
    }

    private void SyncFromSettings(bool force)
    {
        var current = source != null ? source.Current : OptionSettingsService.Instance?.Current;
        bool enabled = current != null && current.rangedAutoAim;
        if (!force && hasDisplayedState && displayedState == enabled) return;
        displayedState = enabled;
        hasDisplayedState = true;
        ApplyBadgeStyle();
    }

    /// <summary>옵션 전환 키(V 등)로 자동 조준이 바뀔 때 호출된다.</summary>
    public void OnAutoAimToggled(bool enabled, bool saved)
    {
        displayedState = enabled;
        hasDisplayedState = true;
        badgePulseAt = Time.unscaledTime;
        ApplyBadgeStyle();

        if (!isActiveAndEnabled || hudGroup == null || hudGroup.alpha <= 0f) return;
        PlayOpticClick(enabled);
        ShowPlayerToast(enabled, saved);
    }

    private void PlayOpticClick(bool enabled)
    {
        var sound = SoundManager.Instance;
        if (sound == null) return;
        Vector3 pos = GameManager.Instance != null && GameManager.Instance.Player != null
            ? GameManager.Instance.Player.transform.position
            : Vector3.zero;
        bool played = false;
        if (opticClickClip != null)
        {
            sound.PlaySFX(new SoundOptions
            {
                bus = SoundBus.SFX,
                clip = opticClickClip,
                volume = 0.65f,
                pitch = enabled ? 1.44f : 1.04f,
                spatialBlend = 0f,
                minInterval = 0.02f,
                maxSimultaneous = 4,
            }, pos);
            played = true;
        }
        if (opticLatchClip != null)
        {
            sound.PlaySFX(new SoundOptions
            {
                bus = SoundBus.SFX,
                clip = opticLatchClip,
                volume = 0.30f,
                pitch = enabled ? 1.62f : 1.22f,
                spatialBlend = 0f,
                minInterval = 0.02f,
                maxSimultaneous = 4,
            }, pos);
            played = true;
        }
        if (played) OpticClickPlayCount++;
    }

    private void ShowPlayerToast(bool enabled, bool saved)
    {
        EnsureVisuals();
        if (toastRoot == null || toastLabel == null) return;

        Color accent = enabled ? OnColor : OffColor;
        string text = enabled ? "자동 조준 모드" : "수동 조준 모드";
        if (!saved) text += " <color=#FF6462>· 저장 실패</color>";
        toastLabel.text = text;
        toastLabel.color = enabled ? OnColor : new Color32(226, 232, 240, 255);

        float width = saved ? 174f : 256f;
        toastRoot.sizeDelta = new Vector2(width, 36f);
        if (toastSurface != null)
            toastSurface.ConfigureCard(18f, ToastFill, new Color(accent.r, accent.g, accent.b, enabled ? 0.68f : 0.42f), 1.8f, accent, 0f, 0.38f, 8f, 0.06f);
        if (toastReticle != null)
            toastReticle.ConfigureReticle(accent, enabled, 1.6f);

        toastStartAt = Time.unscaledTime;
        toastUntil = toastStartAt + ToastDuration;
        toastActive = true;
        toastRoot.gameObject.SetActive(true);
        toastRoot.SetAsLastSibling();
        if (toastGroup != null) toastGroup.alpha = 1f;
        UpdatePlayerToast();
    }

    public void HideToast()
    {
        toastActive = false;
        if (toastRoot != null) toastRoot.gameObject.SetActive(false);
    }

    private void UpdateBadgePulse()
    {
        if (badgeRoot == null) return;
        float elapsed = Time.unscaledTime - badgePulseAt;
        if (elapsed >= 0f && elapsed < 0.26f)
        {
            float t = elapsed / 0.26f;
            float s = 1f + 0.14f * Mathf.Sin(Mathf.PI * (1f - t)) * (1f - t);
            badgeRoot.localScale = new Vector3(s, s, 1f);
            if (badgeReticle != null)
                badgeReticle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, (1f - TutorialCardGraphic.OutCubic(t)) * (displayedState ? -45f : 45f));
        }
        else if (badgeRoot.localScale != Vector3.one)
        {
            badgeRoot.localScale = Vector3.one;
            if (badgeReticle != null) badgeReticle.rectTransform.localRotation = Quaternion.identity;
        }
    }

    private void UpdatePlayerToast()
    {
        if (!toastActive || toastRoot == null) return;
        if (hudGroup == null || hudGroup.alpha <= 0f)
        {
            HideToast();
            return;
        }
        float now = Time.unscaledTime;
        float remaining = toastUntil - now;
        if (remaining <= 0f)
        {
            HideToast();
            return;
        }
        float elapsed = Mathf.Max(0f, now - toastStartAt);
        float pop = TutorialCardGraphic.OutBack(Mathf.Clamp01(elapsed / 0.16f), 1.45f);
        float scale = Mathf.Lerp(0.78f, 1f, pop);
        toastRoot.localScale = new Vector3(scale, scale, 1f);

        if (toastGroup != null)
            toastGroup.alpha = Mathf.Clamp01(remaining / ToastFadeDuration);

        // 플레이어 머리 위 월드 좌표를 HUD 로컬 좌표로 투영해 플레이어 바로 주변에 표시한다.
        float lift = TutorialCardGraphic.OutCubic(Mathf.Clamp01(elapsed / ToastDuration)) * ToastFloatPixels;
        Vector2 targetPos = ResolvePlayerToastPosition() + Vector2.up * lift;
        toastRoot.anchoredPosition = targetPos;
    }

    private Vector2 ResolvePlayerToastPosition()
    {
        var hudRect = transform.parent as RectTransform;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        var worldCam = Camera.main;
        if (hudRect != null && player != null && worldCam != null)
        {
            var col = player.GetComponent<Collider2D>();
            Vector3 anchorWorld = col != null
                ? new Vector3(col.bounds.center.x, col.bounds.max.y + 0.85f, player.transform.position.z)
                : player.transform.position + Vector3.up * 1.75f;
            Vector3 screen = worldCam.WorldToScreenPoint(anchorWorld);
            if (screen.z > 0f)
            {
                var canvas = GetComponentInParent<Canvas>();
                Camera uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(hudRect, screen, uiCam, out Vector2 local))
                {
                    // hudRect의 anchor=(0,1), pivot=(0,1) 기준 좌표로 그대로 사용하되 화면 밖 이탈만 막는다.
                    return new Vector2(
                        Mathf.Clamp(local.x, 120f, Mathf.Max(220f, hudRect.rect.width - 120f)),
                        Mathf.Clamp(local.y, -Mathf.Max(200f, hudRect.rect.height - 80f), -60f));
                }
            }
        }
        return BadgePosition + new Vector2(BadgeSize.x * 0.5f, -BadgeSize.y - 26f);
    }

    private void RefreshBadgeText()
    {
        if (!built) return;
        ApplyBadgeStyle();
    }

    private void ApplyBadgeStyle()
    {
        if (!built) return;
        Color accent = displayedState ? OnColor : OffColor;
        Color border = new Color(accent.r, accent.g, accent.b, displayedState ? 0.55f : 0.24f);
        if (badgeSurface != null)
            badgeSurface.ConfigureCard(10f, SurfaceFill, border, 1.5f, accent, displayedState ? 4f : 2.5f, 0.30f, 6f, 0.06f);
        if (badgeReticle != null)
            badgeReticle.ConfigureReticle(accent, displayedState, 1.7f);
        if (badgeTitle != null)
        {
            badgeTitle.text = "자동 조준";
            badgeTitle.color = displayedState ? new Color32(236, 246, 255, 255) : new Color32(176, 186, 198, 255);
        }
        if (badgeStatus != null)
        {
            string key = InputBindingDisplay.Get("ToggleAutoAim");
            string status = displayedState ? "ON" : "OFF";
            if (!string.IsNullOrEmpty(key) && key != "미할당")
                status += "  [" + key + "]";
            badgeStatus.text = status;
            badgeStatus.color = accent;
        }
    }

    private void EnsureVisuals()
    {
        if (built) return;
        built = true;

        var selfRect = transform as RectTransform;
        if (selfRect != null)
        {
            selfRect.anchorMin = selfRect.anchorMax = new Vector2(0f, 1f);
            selfRect.pivot = new Vector2(0f, 1f);
            selfRect.anchoredPosition = Vector2.zero;
            selfRect.sizeDelta = Vector2.zero;
        }

        // 1) 상시 표시 배지(무기·보조무기 아이콘 왼쪽 아래)
        badgeRoot = CreateRect(transform, "AutoAimHudBadge", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), BadgePosition, BadgeSize);
        badgeSurface = badgeRoot.gameObject.AddComponent<TutorialCardGraphic>();

        var reticleRect = CreateRect(badgeRoot, "Reticle", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(21f, 0f), new Vector2(24f, 24f));
        badgeReticle = reticleRect.gameObject.AddComponent<TutorialCardGraphic>();

        badgeTitle = CreateLabel(badgeRoot, "Title", "자동 조준", 13.5f, TextAlignmentOptions.MidlineLeft);
        var tr = badgeTitle.rectTransform;
        tr.anchorMin = new Vector2(0f, 0.5f);
        tr.anchorMax = new Vector2(1f, 0.5f);
        tr.pivot = new Vector2(0f, 0.5f);
        tr.offsetMin = new Vector2(36f, 0f);
        tr.offsetMax = new Vector2(-6f, 20f);

        badgeStatus = CreateLabel(badgeRoot, "Status", "OFF", 13.5f, TextAlignmentOptions.MidlineLeft);
        var sr = badgeStatus.rectTransform;
        sr.anchorMin = new Vector2(0f, 0.5f);
        sr.anchorMax = new Vector2(1f, 0.5f);
        sr.pivot = new Vector2(0f, 0.5f);
        sr.offsetMin = new Vector2(36f, -20f);
        sr.offsetMax = new Vector2(-6f, 1f);

        // 2) 플레이어 주변 전환 토스트("자동 조준 모드" / "수동 조준 모드")
        toastRoot = CreateRect(transform, "AutoAimPlayerToast", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(174f, 36f));
        toastGroup = toastRoot.gameObject.AddComponent<CanvasGroup>();
        toastGroup.blocksRaycasts = false;
        toastGroup.interactable = false;
        toastSurface = toastRoot.gameObject.AddComponent<TutorialCardGraphic>();

        var toastIconRect = CreateRect(toastRoot, "ToastReticle", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(21f, 0f), new Vector2(20f, 20f));
        toastReticle = toastIconRect.gameObject.AddComponent<TutorialCardGraphic>();

        toastLabel = CreateLabel(toastRoot, "ToastText", "자동 조준 모드", 17.5f, TextAlignmentOptions.MidlineLeft);
        var lr = toastLabel.rectTransform;
        lr.anchorMin = new Vector2(0f, 0.5f);
        lr.anchorMax = new Vector2(1f, 0.5f);
        lr.pivot = new Vector2(0f, 0.5f);
        lr.offsetMin = new Vector2(36f, -15f);
        lr.offsetMax = new Vector2(-12f, 15f);

        toastRoot.gameObject.SetActive(false);
    }

    private RectTransform CreateRect(Transform parent, string objectName, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = aMin;
        rect.anchorMax = aMax;
        rect.pivot = pivot;
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        return rect;
    }

    private TextMeshProUGUI CreateLabel(Transform parent, string objectName, string value, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = value;
        label.fontSize = size;
        label.fontStyle = FontStyles.Bold;
        label.alignment = align;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        label.outlineColor = new Color32(10, 14, 20, 255);
        label.outlineWidth = 0.16f;
        return label;
    }
}
