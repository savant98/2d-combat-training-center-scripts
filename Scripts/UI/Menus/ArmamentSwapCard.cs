using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 왼쪽의 무기 후보 노드와 오른쪽에 펼쳐지는 해당 후보의 정보를 연결한다.
/// 장착 및 해금 판정은 페이지가 맡고 이 컴포넌트는 아이콘, 상태, 효과와 Gear 구성을 표시한다.
/// 잠금 해제 시 아이콘의 부품이 풀린 뒤 해당 무장의 Gear 원이 차례로 밝아진다.
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
        public ArmamentBranchGraphic branch;
        public GameObject lockMark;

        public void ShowAvailability(bool available, bool hasWeapon)
        {
            if (branch != null) branch.SetAvailable(available);
            if (lockMark != null) lockMark.SetActive(hasWeapon && !available);
        }
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

    [Header("Inspection")]
    [SerializeField] private RectTransform inspection;
    [SerializeField] private Image inspectionIcon;
    [SerializeField] private TextMeshProUGUI inspectionName, actionText;
    [SerializeField] private GameObject gearGroup, secondaryGroup;
    [SerializeField] private TextMeshProUGUI gaugeValue, windupValue;
    [SerializeField] private ArmamentBranchGraphic route;

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

    public void Inspect(bool selected, bool showingCandidate, bool animate)
    {
        if (route != null) route.SetFocused(selected);
        // '트리로' 버튼 위에서도 마지막 무기의 정보를 계속 읽을 수 있다.
        if (inspection == null || !showingCandidate) return;
        if (inspection.gameObject.activeSelf == selected) return;
        inspection.DOKill();
        inspection.gameObject.SetActive(selected);
        inspection.anchoredPosition = selected && animate ? new Vector2(15f, 0f) : Vector2.zero;
        if (selected && animate) inspection.DOAnchorPosX(0f, .16f).SetEase(Ease.OutCubic).SetUpdate(true);
    }

    public void Bind(PlayerArmamentSO armament, bool equipped) => Bind(armament, equipped, false, null);

    /// <summary>무장 카드. locked면 잠금 링·잠금 부품과 '잠김 · lockLabel' 배지를 보인다(장착 중 배지가 우선).</summary>
    public void Bind(PlayerArmamentSO armament, bool equipped, bool locked, string lockLabel)
    {
        if (armament == null) return;
        bool ready = armament.normal != null;
        string trait = ArmamentMenuGuide.TraitName(armament);
        BindInspection(armament.DisplayName, armament.Icon, false, equipped, locked, lockLabel, ready);
        if (portText != null) portText.text = string.IsNullOrEmpty(trait) ? "주무기" : "무장 특성  /  " + trait;
        if (descriptionText != null)
            descriptionText.text = UpgradeMenuPage.EvenLines(ArmamentMenuGuide.CardDescription(armament));

        ShowBadge(equipped, locked, lockLabel, !ready);
        if (contact != null) contact.color = equipped ? contactColor : idleContactColor;
        ShowLock(locked);

        for (int i = 0; previews != null && i < previews.Length; i++)
            ShowWeapon(previews[i], i == 0 ? armament.normal : armament.GetGearWeapon(i), i, locked);
    }

    public void BindSecondary(WeaponBaseSO weapon, bool equipped) => BindSecondary(weapon, equipped, false, null);

    /// <summary>보조 무기 카드. locked면 잠금 링·잠금 부품과 '잠김 · lockLabel' 배지를 보이고, 해금하면 그 자리에 바로 장착된다고 알린다.</summary>
    public void BindSecondary(WeaponBaseSO weapon, bool equipped, bool locked, string lockLabel)
    {
        if (weapon == null) return;
        BindInspection(weapon.DisplayName, weapon.weaponSprite, true, equipped, locked, lockLabel, true);
        if (portText != null) portText.text = "보조 무기  /  효과";
        if (descriptionText != null) descriptionText.text = UpgradeMenuPage.EvenLines(ArmamentMenuGuide.SecondaryDescription(weapon));
        if (gaugeValue != null) gaugeValue.text = weapon.skillGauge.ToString("0.##");
        if (windupValue != null) windupValue.text = weapon.windupTime.ToString("0.##") + "초";
        ShowBadge(equipped, locked, lockLabel, false);
        if (contact != null) contact.color = equipped ? contactColor : idleContactColor;
        ShowLock(locked);
    }

    private void BindInspection(string label, Sprite sprite, bool secondary, bool equipped, bool locked, string price, bool ready)
    {
        if (nameText != null) nameText.text = label;
        if (inspectionName != null) inspectionName.text = label;
        if (icon != null) { icon.sprite = sprite; icon.enabled = sprite != null; }
        if (inspectionIcon != null) { inspectionIcon.sprite = sprite; inspectionIcon.enabled = sprite != null; }
        if (gearGroup != null) gearGroup.SetActive(!secondary);
        if (secondaryGroup != null) secondaryGroup.SetActive(secondary);
        if (route != null) route.SetAvailable(!locked);
        if (actionText == null) return;
        actionText.color = locked ? TrainingBoardStyle.Gold : TrainingBoardStyle.Line;
        if (equipped) actionText.text = "이 자리에 장착 중";
        else if (locked) actionText.text = (string.IsNullOrEmpty(price) ? "잠김" : price) + (secondary ? "   ·   해금하면 이 자리에 장착" : "   ·   해금 후 장착 가능");
        else actionText.text = ready ? "선택하면 이 자리에 장착" : "준비 중";
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
            if (preview != null && preview.node != null && UpgradeMenuPage.IsUnlocked(armament.GetGearWeapon(stage)))
            {
                preview.ShowAvailability(true, true);
                Material material = Pick(stageMaterials, stage);
                if (material != null && preview.node.material != material) preview.node.material = material;
            }
            lit++;
        }
        return lit;
    }

    // 후보의 현재 상태와 해금에 필요한 필증을 같은 자리에 표시한다.
    private void ShowBadge(bool equipped, bool locked, string lockLabel, bool pending)
    {
        if (badge != null) badge.SetActive(true);
        Color badgeColor = equipped ? equippedColor : locked || pending ? pendingColor : TrainingBoardStyle.SecondaryText;
        if (badgeText != null)
        {
            badgeText.text = equipped ? "장착 중" : locked ? (string.IsNullOrEmpty(lockLabel) ? "잠김" : "잠김 · " + lockLabel) : pending ? "준비 중" : "장착 가능";
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

    private void ShowWeapon(WeaponPreview preview, WeaponBaseSO weapon, int stage, bool armamentLocked)
    {
        if (preview == null) return;
        bool available = stage == 0 ? weapon != null && !armamentLocked : UpgradeMenuPage.IsUnlocked(weapon);
        preview.ShowAvailability(available, weapon != null);
        if (preview.stageText != null)
        {
            // 글꼴(esamanru)에 공백 글자가 없어 간격은 <space> 태그로 둔다.
            preview.stageText.text = stage == 0 ? "기본" : "GEAR<space=.25em>" + stage;
            preview.stageText.color = stage == 0 ? baseStageColor : weapon != null ? StageColor(stage) : emptyNameColor;
        }
        if (preview.node != null)
        {
            Material material = stage == 0 ? normalMaterial
                : Pick(available ? stageMaterials : stageDimMaterials, stage);
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
