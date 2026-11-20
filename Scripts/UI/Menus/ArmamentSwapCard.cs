using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무장 교체 페이지의 카드 하나(2026-11-18). 무장 아이콘·이름·설명과, 기본 무기 + Gear 1~3 무기의 미리보기, '장착 중' 표시를 보여 준다.
/// 선택 대상(hitArea)은 카드보다 1/1.15 작게 두어, 선택되어 1.15배로 커지면 외곽선이 카드 바로 바깥에 맞는다(카드 본문은 커지지 않는다).
/// 원은 PlayerHUD 무기 칸과 같은 톱니 모양이다(2026-11-18 2차). 톱니 재질(GlowMultiply)은 Image.color를 쓰지 않으므로 상태는 재질로 바꾼다:
/// 기본 무기 원은 HUD 기본 칸 재질, Gear 1~3 원은 해금한 무기면 단계 색 밝은 재질, 해금 전·비어 있으면 어두운 재질.
/// 2026-11-20 훈련 개편: 훈련 필증으로 해금하지 않은 무장·보조 무기 카드는 아이콘 원을 회색 잠금 링으로 바꾸고 잠금 부품(ArmamentLockOverlay)과
/// '잠김 · 필증 n' 배지를 보인다. 해금하면 잠금 부품이 풀리고, 무장 카드는 체결 뒤 Gear 미리보기 원이 차례로 밝아진다(연쇄 체결).
/// </summary>
[DisallowMultipleComponent]
public sealed class ArmamentSwapCard : MonoBehaviour
{
    // 연쇄 체결: 아이콘이 끼워진 뒤(ArmamentLockOverlay.EngageTime) 이 시간부터 Gear 미리보기 원을 이 간격으로 하나씩 밝힌다(실제 시간).
    public const float ChainDelay = .10f, ChainStep = .10f;

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

    [Header("Lock")]
    [Tooltip("카드 아이콘 뒤의 HUD 기어 모양 원(해금한 카드 = 기본 칸 재질, 잠긴 카드 = 잠금 링 재질)")]
    [SerializeField] private Image iconRing;
    [Tooltip("잠긴 카드의 아이콘 위에 얹는 잠금 부품(tools/Unity/ArmamentUnlockSetup.cs가 만든다)")]
    [SerializeField] private ArmamentLockOverlay lockOverlay;
    [Tooltip("잠긴 카드의 아이콘 원 재질(ArmamentLockedRing.mat)")]
    [SerializeField] private Material lockedRingMaterial;

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
    public ArmamentLockOverlay LockOverlay => lockOverlay;
    public Image IconRing => iconRing;

    public void Bind(PlayerArmamentSO armament, bool equipped) => Bind(armament, equipped, false, null);

    /// <summary>무장 카드. locked면 잠금 링·잠금 부품과 '잠김 · lockLabel' 배지를 보인다(장착 중 배지가 우선).</summary>
    public void Bind(PlayerArmamentSO armament, bool equipped, bool locked, string lockLabel)
    {
        if (armament == null) return;
        foreach (var preview in previews)
        {
            if (preview.node != null) preview.node.gameObject.SetActive(true);
            if (preview.stageText != null) preview.stageText.gameObject.SetActive(true);
            if (preview.nameText != null) preview.nameText.gameObject.SetActive(true);
        }
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

        ShowBadge(equipped, locked, lockLabel, !ready);
        if (contact != null) contact.color = equipped ? contactColor : idleContactColor;
        ShowLock(locked);

        for (int i = 0; previews != null && i < previews.Length; i++)
            ShowWeapon(previews[i], i == 0 ? armament.normal : armament.GetGearWeapon(i), i);
    }

    public void BindSecondary(WeaponBaseSO weapon, bool equipped) => BindSecondary(weapon, equipped, false, null);

    /// <summary>보조 무기 카드. locked면 잠금 링·잠금 부품과 '잠김 · lockLabel' 배지를 보이고, 해금하면 그 자리에 바로 장착된다고 알린다.</summary>
    public void BindSecondary(WeaponBaseSO weapon, bool equipped, bool locked, string lockLabel)
    {
        if (weapon == null) return;
        if (portText != null) portText.text = "SUPPORT";
        if (nameText != null) nameText.text = weapon.DisplayName;
        if (descriptionText != null)
            descriptionText.text = (locked ? "훈련 필증으로 해금 · 해금하면 이 자리에 바로 장착" : "모든 무장에서 사용 가능")
                + "\n게이지 " + weapon.skillGauge.ToString("0.#") + " · 쿨타임 " + weapon.cooldown.ToString("0.#") + "초";
        if (icon != null) { icon.sprite = weapon.weaponSprite; icon.enabled = icon.sprite != null; }
        ShowBadge(equipped, locked, lockLabel, false);
        if (contact != null) contact.color = equipped ? contactColor : idleContactColor;
        ShowLock(locked);
        foreach (var preview in previews)
        {
            if (preview.node != null) preview.node.gameObject.SetActive(false);
            if (preview.stageText != null) preview.stageText.gameObject.SetActive(false);
            if (preview.nameText != null) preview.nameText.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 해금 연출(페이지 코루틴이 안쪽 단계를 직접 돌린다). 잠금 부품이 풀리고 아이콘 원이 잠금 링에서 기본 칸 재질로 차오른다.
    /// 무장 카드(armament가 있으면)는 체결 뒤 Gear 미리보기 원(무기 있는 단계만)을 ChainStep 간격으로 어두운 → 밝은 재질로 바꾼다.
    /// 잠금 부품이 없으면(적용 도구 실행 전) 아무것도 하지 않는다.
    /// </summary>
    public IEnumerator PlayUnlock(PlayerArmamentSO armament)
    {
        if (lockOverlay == null) yield break;
        int lit = 0;
        var routine = lockOverlay.PlayUnlock(lockedRingMaterial, normalMaterial);
        while (routine.MoveNext())
        {
            if (armament != null) lit = LightChain(armament, lit, lockOverlay.Elapsed);
            yield return routine.Current;
        }
        if (armament != null) LightChain(armament, lit, float.PositiveInfinity);
    }

    /// <summary>해금 연출을 끊는다(최종 자세: 부품 숨김, 아이콘 원 = 기본 칸 재질).</summary>
    public void CompleteUnlock()
    {
        if (lockOverlay != null) lockOverlay.CompleteImmediately(normalMaterial);
        if (iconRing != null && normalMaterial != null && iconRing.material != normalMaterial) iconRing.material = normalMaterial;
    }

    // 지금까지 밝힌 단계 수(lit)에서 elapsed초까지 밝혀야 할 단계를 더 밝힌다.
    private int LightChain(PlayerArmamentSO armament, int lit, float elapsed)
    {
        float start = ArmamentLockOverlay.EngageTime + ChainDelay;
        while (lit < 3 && elapsed >= start + lit * ChainStep)
        {
            int stage = lit + 1;
            var preview = previews != null && stage < previews.Length ? previews[stage] : null;
            if (preview != null && preview.node != null && armament.GetGearWeapon(stage) != null)
            {
                Material material = Pick(stageMaterials, stage);
                if (material != null && preview.node.material != material) preview.node.material = material;
            }
            lit++;
        }
        return lit;
    }

    // 배지: 장착 중 > 잠김 · 가격 > 준비 중.
    private void ShowBadge(bool equipped, bool locked, string lockLabel, bool pending)
    {
        bool show = equipped || locked || pending;
        if (badge != null) badge.SetActive(show);
        Color badgeColor = equipped ? equippedColor : pendingColor;
        if (badgeText != null)
        {
            badgeText.text = equipped ? "장착 중" : locked ? (string.IsNullOrEmpty(lockLabel) ? "잠김" : "잠김 · " + lockLabel) : "준비 중";
            badgeText.color = badgeColor;
        }
        if (badgeFrame != null) badgeFrame.color = badgeColor;
    }

    private void ShowLock(bool locked)
    {
        if (iconRing != null)
        {
            Material material = locked && lockedRingMaterial != null ? lockedRingMaterial : normalMaterial;
            if (material != null && iconRing.material != material) iconRing.material = material;
        }
        if (lockOverlay != null) lockOverlay.SetLocked(locked);
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
