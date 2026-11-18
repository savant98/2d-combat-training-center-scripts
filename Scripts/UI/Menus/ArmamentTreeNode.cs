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

    public RectTransform Button => button;
    public Image Highlight => highlight;
    public Image Ring => ring;
    public Transform FeedbackPoint => icon != null ? icon.transform : button != null ? button : transform;

    /// <summary>무기(무장)를 보여 준다. lit이면 밝은 재질, 아니면(해금 전) 어두운 재질.</summary>
    public void Show(Sprite sprite, string label, string status, bool lit)
    {
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
