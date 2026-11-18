using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무장 교체 페이지의 카드 하나(2026-11-18). 무장 아이콘·이름·설명과, 기본 무기 + Gear 1~3 무기의 미리보기, '장착 중' 표시를 보여 준다.
/// 선택 대상(hitArea)은 카드보다 1/1.15 작게 두어, 선택되어 1.15배로 커지면 외곽선이 카드 바로 바깥에 맞는다(카드 본문은 커지지 않는다).
/// 원은 PlayerHUD 무기 칸과 같은 톱니 모양이다(2026-11-18 2차). 톱니 재질(GlowMultiply)은 Image.color를 쓰지 않으므로 상태는 재질로 바꾼다:
/// 기본 무기 원은 HUD 기본 칸 재질, Gear 1~3 원은 해금한 무기면 단계 색 밝은 재질, 해금 전·비어 있으면 어두운 재질.
/// </summary>
[DisallowMultipleComponent]
public sealed class ArmamentSwapCard : MonoBehaviour
{
    [Serializable]
    public sealed class WeaponPreview
    {
        public Image node;
        public Image icon;
        public TextMeshProUGUI stageText;
        public TextMeshProUGUI nameText;
    }

    [Header("Selection")]
    [SerializeField] private RectTransform hitArea;
    [SerializeField] private Image highlight;

    [Header("Armament")]
    [SerializeField] private Graphic contact;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI portText;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private GameObject badge;
    [SerializeField] private Graphic badgeFrame;
    [SerializeField] private TextMeshProUGUI badgeText;

    [Header("Weapons")]
    [Tooltip("[0] 기본 무기, [1~3] Gear 1~3단계")]
    [SerializeField] private WeaponPreview[] previews = new WeaponPreview[4];

    [Header("Ring Materials")]
    [Tooltip("기본 무기 원(HUD 기본 칸과 같은 재질)")]
    [SerializeField] private Material normalMaterial;
    [Tooltip("[0~2] Gear 1~3 단계 색 밝은 재질(해금한 무기)")]
    [SerializeField] private Material[] stageMaterials = new Material[3];
    [Tooltip("[0~2] Gear 1~3 단계 색 어두운 재질(해금 전·비어 있음)")]
    [SerializeField] private Material[] stageDimMaterials = new Material[3];

    [Header("Colors")]
    [Tooltip("[0~2] Gear 1~3 단계 글자 색(Player.prefab PlayerGearUI의 단계 색과 같게, 생성 도구가 쓴다)")]
    [SerializeField] private Color[] stageColors = { new Color(.392f, .991f, .991f, 1f), new Color(1f, .196f, 0f, 1f), new Color(1f, .392f, 1f, 1f) };
    [SerializeField] private Color baseStageColor = new Color(.659f, .906f, 1f, 1f);
    [SerializeField] private Color nameColor = new Color(.659f, .906f, 1f, 1f);
    [SerializeField] private Color emptyNameColor = new Color(.51f, .62f, .71f, .55f);
    [SerializeField] private Color equippedColor = new Color(.263f, .718f, 1f, 1f);
    [SerializeField] private Color pendingColor = new Color(.914f, .71f, .42f, 1f);
    [SerializeField] private Color contactColor = new Color(.263f, .718f, 1f, 1f);
    [SerializeField] private Color idleContactColor = new Color(.322f, .443f, .553f, 1f);

    public RectTransform HitArea => hitArea;
    public Image Highlight => highlight;
    public Transform FeedbackPoint => icon != null ? icon.transform : transform;

    public void Bind(PlayerArmamentSO armament, bool equipped)
    {
        if (armament == null) return;
        bool ready = armament.normal != null;
        if (portText != null) portText.text = UpgradeMenuPage.CategoryCode(armament.category) + " ARMAMENT";
        if (nameText != null) nameText.text = armament.DisplayName;
        if (descriptionText != null)
            descriptionText.text = UpgradeMenuPage.EvenLines(string.IsNullOrWhiteSpace(armament.description) ? "설명이 아직 없습니다." : armament.description);
        if (icon != null)
        {
            icon.sprite = armament.Icon;
            icon.enabled = icon.sprite != null;
        }

        bool showBadge = equipped || !ready;
        if (badge != null) badge.SetActive(showBadge);
        Color badgeColor = equipped ? equippedColor : pendingColor;
        if (badgeText != null)
        {
            badgeText.text = equipped ? "장착 중" : "준비 중";
            badgeText.color = badgeColor;
        }
        if (badgeFrame != null) badgeFrame.color = badgeColor;
        if (contact != null) contact.color = equipped ? contactColor : idleContactColor;

        for (int i = 0; previews != null && i < previews.Length; i++)
            ShowWeapon(previews[i], i == 0 ? armament.normal : armament.GetGearWeapon(i), i);
    }

    // 장착에 성공한 카드의 아이콘을 짧게 튕긴다(효과음·입자는 페이지가 Feedback으로 낸다).
    public void PlayEquipPulse()
    {
        if (icon == null) return;
        var rect = icon.rectTransform;
        rect.DOKill(true);
        rect.localScale = Vector3.one;
        rect.DOPunchScale(Vector3.one * .2f, .35f, 6, .6f).SetUpdate(true);
    }

    private void ShowWeapon(WeaponPreview preview, WeaponBaseSO weapon, int stage)
    {
        if (preview == null) return;
        if (preview.stageText != null)
        {
            // 글꼴(esamanru)에 공백 글자가 없어 간격은 <space> 태그로 둔다.
            preview.stageText.text = stage == 0 ? "기본" : "GEAR<space=.25em>" + stage;
            preview.stageText.color = stage == 0 ? baseStageColor : weapon != null ? StageColor(stage) : emptyNameColor;
        }
        if (preview.node != null)
        {
            Material material = stage == 0 ? normalMaterial
                : Pick(weapon != null && UpgradeMenuPage.IsUnlocked(weapon) ? stageMaterials : stageDimMaterials, stage);
            if (material != null && preview.node.material != material) preview.node.material = material;
        }
        if (preview.icon != null)
        {
            preview.icon.sprite = weapon != null ? weapon.weaponSprite : null;
            preview.icon.enabled = preview.icon.sprite != null;
        }
        if (preview.nameText != null)
        {
            preview.nameText.text = weapon != null ? weapon.DisplayName : stage == 0 ? "준비 중" : "없음";
            preview.nameText.color = weapon != null ? nameColor : emptyNameColor;
        }
    }

    private Color StageColor(int stage) =>
        stageColors != null && stage >= 1 && stage <= stageColors.Length ? stageColors[stage - 1] : baseStageColor;

    private static Material Pick(Material[] materials, int stage) =>
        materials != null && stage >= 1 && stage <= materials.Length ? materials[stage - 1] : null;
}
