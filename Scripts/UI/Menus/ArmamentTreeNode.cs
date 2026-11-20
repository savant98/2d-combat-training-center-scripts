using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무장 개요 페이지의 원 하나(2026-11-18). 열 맨 아래의 큰 원은 장착 무장, 그 위의 작은 원 1·2·3은 Gear 1~3단계 무기다.
/// 선택 대상은 button(선택하면 1.15배로 커진다)이고, 이름·상태 글자는 원 옆에 두어 커지지 않는다.
/// 원은 PlayerHUD 무기 칸과 같은 모양(HudSlotGear 톱니 + GearUiWeaponFrame 틀)이다(2026-11-18 2차, 사용자 지시
/// "파란색 기어 모양이 이제 사용하지 않는 것이기 때문에, PlayerHUD의 기어 모양과 일치하게 해주고, 몇번 기어 스킬인지 더 크고 명확하게 표시해줘").
/// - 톱니 재질(GlowMultiply)은 Image.color를 쓰지 않으므로 상태는 재질을 바꿔 보인다: 무장 원은 항상 HUD 기본 칸 재질,
///   Gear 원은 해금한 무기면 단계 색 밝은 재질(lit), 해금 전·비어 있으면 어두운 재질(dim).
/// - Gear 원 안쪽(가운데 구분선 쪽)에 'GEAR' 글자와 큰 단계 숫자를 둔다. 비어 있는 단계는 숫자도 흐리게 보이고 원 가운데에 '없음'을 띄운다.
/// - 2026-11-20 훈련 개편: 훈련 필증으로 해금하지 않은 Gear 기술은 원 위에 잠금 부품(ArmamentLockOverlay)을 얹는다(SetLocked).
///   빈 보조 무기 원은 PlayerHUD의 빈 보조 무기 칸과 같게 회색 잠금 링 + 작은 자물쇠로 보인다(ShowLockedEmpty, 독립 검토 반영).
/// </summary>
[DisallowMultipleComponent]
public sealed class ArmamentTreeNode : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private RectTransform button;
    [SerializeField] private Image highlight;

    [Header("Visuals")]
    [SerializeField] private Image ring;
    [SerializeField] private Image icon;
    [Tooltip("비어 있는 단계일 때 원 가운데에 보이는 글자")]
    [SerializeField] private TextMeshProUGUI emptyText;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Lock")]
    [Tooltip("잠긴 Gear 기술 위에 얹는 잠금 부품(2026-11-20 훈련 개편, tools/Unity/ArmamentUnlockSetup.cs가 만든다). 무장 원·보조 무기 원에는 없다")]
    [SerializeField] private ArmamentLockOverlay lockOverlay;

    [Header("Ring Materials")]
    [Tooltip("무기가 있고 쓸 수 있을 때(무장 원은 HUD 기본 칸 재질, Gear 원은 단계 색 밝은 재질)")]
    [SerializeField] private Material litMaterial;
    [Tooltip("해금 전이거나 비어 있을 때(Gear 원은 단계 색 어두운 재질)")]
    [SerializeField] private Material dimMaterial;

    [Header("Gear Stage Mark")]
    [Tooltip("단계 숫자 위의 'GEAR' 글자(무장 원에는 없다)")]
    [SerializeField] private TextMeshProUGUI stageCaption;
    [Tooltip("큰 단계 숫자(무장 원에는 없다)")]
    [SerializeField] private TextMeshProUGUI stageText;

    [Header("Colors")]
    [SerializeField] private Color nameColor = new Color(.659f, .906f, 1f, 1f);
    [SerializeField] private Color emptyNameColor = new Color(.51f, .62f, .71f, .55f);
    [SerializeField] private Color statusColor = new Color(.51f, .62f, .71f, 1f);
    [Tooltip("단계 숫자 색(Player.prefab PlayerGearUI의 단계 색과 같게, 생성 도구가 쓴다)")]
    [SerializeField] private Color stageColor = Color.white;
    [SerializeField] private Color emptyStageColor = new Color(.51f, .62f, .71f, .5f);
    [SerializeField, Range(0f, 1f)] private float captionAlpha = .85f;

    // 빈 보조 무기 원의 작은 자물쇠(PlayerHUD.EnsureLockMark와 같은 모양). Prefab을 고치지 않도록 처음 필요할 때 아이콘 바로 다음 형제로 만든다.
    private const float LockMarkRatio = .4f;   // 아이콘 크기에 대한 자물쇠 크기 비율(HUD는 칸 아이콘의 약 절반 크기 30)
    private static readonly Color LockMarkColor = new Color(1f, 1f, 1f, .8f);   // PlayerHUD.lockMarkColor 기본값과 같다
    private Image lockMark;

    public RectTransform Button => button;
    public Image Highlight => highlight;
    public Image Ring => ring;
    public Transform FeedbackPoint => icon != null ? icon.transform : button != null ? button : transform;
    public ArmamentLockOverlay LockOverlay => lockOverlay;

    /// <summary>잠금 부품을 보이거나 숨긴다(부품이 없는 원은 아무것도 하지 않는다).</summary>
    public void SetLocked(bool locked)
    {
        if (lockOverlay != null) lockOverlay.SetLocked(locked);
    }

    /// <summary>무기(무장)를 보여 준다. lit이면 밝은 재질, 아니면(해금 전) 어두운 재질.</summary>
    public void Show(Sprite sprite, string label, string status, bool lit)
    {
        SetLockMark(null);
        SetRing(lit);
        if (icon != null)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
        if (emptyText != null) emptyText.gameObject.SetActive(false);
        SetText(nameText, label, nameColor);
        SetText(statusText, status, statusColor);
        SetStageMark(stageColor);
    }

    public void ShowEmpty(string label, string status)
    {
        SetLocked(false);
        SetLockMark(null);
        SetRing(false);
        if (icon != null)
        {
            icon.sprite = null;
            icon.enabled = false;
        }
        if (emptyText != null) emptyText.gameObject.SetActive(true);
        SetText(nameText, label, emptyNameColor);
        SetText(statusText, status, emptyNameColor);
        SetStageMark(emptyStageColor);
    }

    /// <summary>
    /// 빈 칸을 잠금 모습으로 보인다(개요의 빈 보조 무기 원). PlayerHUD의 빈 보조 무기 칸과 같게 원은 회색 잠금 링 재질(lockedRing),
    /// 아이콘 자리에는 작은 자물쇠(glyph)를 띄운다. 재질·그림이 없으면(적용 도구 실행 전) ShowEmpty와 같다.
    /// </summary>
    public void ShowLockedEmpty(string label, string status, Material lockedRing, Sprite glyph)
    {
        ShowEmpty(label, status);
        if (ring != null && lockedRing != null && ring.material != lockedRing) ring.material = lockedRing;
        SetLockMark(glyph);
    }

    /// <summary>작은 자물쇠가 켜져 있는가(검증용).</summary>
    public bool LockMarkShown => lockMark != null && lockMark.enabled;

    // glyph가 있으면 자물쇠를 켜고, null이면 끈다(아직 만들지 않았으면 만들지 않는다).
    private void SetLockMark(Sprite glyph)
    {
        if (glyph == null)
        {
            if (lockMark != null && lockMark.enabled) lockMark.enabled = false;
            return;
        }
        if (lockMark == null && !CreateLockMark()) return;
        if (lockMark.sprite != glyph) lockMark.sprite = glyph;
        if (!lockMark.enabled) lockMark.enabled = true;
    }

    // 아이콘의 형제(아이콘 바로 다음)로 자물쇠 그림을 만든다. 원(ring)의 자식이 아니라서 원이 돌아도 자물쇠는 돌지 않는다.
    private bool CreateLockMark()
    {
        if (icon == null) return false;
        Transform parent = icon.transform.parent;
        var mark = new GameObject("LockMark", typeof(RectTransform), typeof(Image));
        mark.layer = icon.gameObject.layer;
        var rect = (RectTransform)mark.transform;
        rect.SetParent(parent, false);
        rect.SetSiblingIndex(icon.transform.GetSiblingIndex() + 1);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        RectTransform iconRect = icon.rectTransform;
        float size = Mathf.Min(iconRect.rect.width, iconRect.rect.height) * LockMarkRatio;
        rect.sizeDelta = new Vector2(size, size);
        rect.localPosition = parent != null ? parent.InverseTransformPoint(iconRect.TransformPoint(iconRect.rect.center)) : iconRect.localPosition;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        lockMark = mark.GetComponent<Image>();
        lockMark.color = LockMarkColor;
        lockMark.raycastTarget = false;
        lockMark.preserveAspect = true;
        lockMark.enabled = false;
        return true;
    }

    private void SetRing(bool lit)
    {
        if (ring == null) return;
        Material material = lit || dimMaterial == null ? litMaterial : dimMaterial;
        if (material != null && ring.material != material) ring.material = material;
    }

    private void SetStageMark(Color color)
    {
        if (stageText != null) stageText.color = color;
        if (stageCaption != null)
        {
            Color caption = color;
            caption.a *= captionAlpha;
            stageCaption.color = caption;
        }
    }

    private static void SetText(TextMeshProUGUI text, string value, Color color)
    {
        if (text == null) return;
        text.text = value ?? string.Empty;
        text.color = color;
    }
}
