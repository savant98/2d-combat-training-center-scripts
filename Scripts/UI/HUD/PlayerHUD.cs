using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerHUD : MonoBehaviour
{
    [System.Serializable]
    private class WeaponHudSlot
    {
        public string label;
        public Image icon;
        public Image circle;

        [Header("CoolDown")]
        public Image cooldownFill;
        public TextMeshProUGUI cooldownText;

        public bool alwaysReady;

        [HideInInspector] public bool isReady;
        [HideInInspector] public bool gaugeReady;
        [HideInInspector] public bool cooldownReady = true;
        [HideInInspector] public WeaponBaseSO weapon;

        // 기어 슬롯 톱니의 단계 색 재질(PlayerGearUI의 색을 받아 실행 중에 만든다). 없으면 공용 재질을 쓴다.
        [System.NonSerialized] public Material readyMaterial;
        [System.NonSerialized] public Material preparingMaterial;

        // 쿨다운 글자에 마지막으로 넣은 표시 키(0 = 아직 없음). 표시 문자열이 바뀔 때만 text를 다시 넣으려고 둔다.
        [System.NonSerialized] public int shownCooldownKey;

        // 해금 전이라 비운 칸(Gear 기술) 또는 빈 보조 무기 칸(2026-11-20 훈련 개편). 잠금 표시는 실행 중에 만든다(MainScene을 고치지 않는다).
        // 칸 GameObject는 끄지 않는다(기초 훈련의 강조 표시가 아이콘 GameObject의 활성 여부를 본다).
        [System.NonSerialized] public bool locked;
        [System.NonSerialized] public Image lockMark;
        [System.NonSerialized] public TextMeshProUGUI finisherRequirement;
    }

    // 체력 깎인 양을 잠시 남겨 보여 주는 뒤쪽 값(셰이더 _Trail)과 그 줄어드는 Tween
    private sealed class GaugeTrail
    {
        public float value;
        public Tween tween;
    }

    [Header("HUD CanvasGroup")]
    [SerializeField] private CanvasGroup hudCanvasGroup;

    [Header("HP")]
    [Tooltip("비스듬한 칸 게이지(UI/PlayerHudHpFill). 깎인 양(붉은 칸)·점검 빛·번쩍임을 셰이더가 그린다")]
    [SerializeField] private Image hpBar;
    [Tooltip("체력이 바뀔 때 함께 튀는 바와 뒤판")]
    [SerializeField] private RectTransform[] hpPunchTargets;
    [SerializeField] private Color hpIdleEmission = new Color(0.2f, 0.95f, 0.35f, 1f);
    [SerializeField] private Color hpDamageEmission = new Color(1f, 0.3f, 0.22f, 1f);
    [SerializeField] private Color hpHealEmission = new Color(0.55f, 1f, 0.7f, 1f);
    [SerializeField] private float hpFlashStrength = 3f;
    [SerializeField] private float hpEmissionDuration = 0.3f;

    [Header("Skill Bar")]
    [Tooltip("배터리 칸 게이지(UI/PlayerHudSkillBattery). 충전 중인 칸(어둡게)·다 찬 칸(전류)·완충 번쩍임·쓴 칸 방전·번개 표시를 셰이더가 그린다. 칸 1·2·3은 기어 슬롯 1·2·3 바로 위다")]
    [SerializeField] private Image skillGaugeBar;
    [Tooltip("게이지가 바뀔 때 함께 튀는 대상. 배터리 칸이 아래 기어 슬롯과 줄 맞춰 있어 비워 두고, 대신 셰이더의 칸과 번개가 번쩍인다")]
    [SerializeField] private RectTransform[] skillPunchTargets;
    [SerializeField] private Color skillGaugeBarIdleEmission = Color.black;
    [SerializeField] private Color skillGaugeBarGainEmission = Color.cyan;
    [SerializeField] private Color skillGaugeBarSpendEmission = new Color(1f, 0.95f, 0.8f, 1f);
    [SerializeField] private float skillGaugeBarFlashStrength = 2.2f;
    [SerializeField] private float skillGaugeBarEmissionDuration = 0.15f;
    [SerializeField] private float skillGaugeBarEmissionIntensity = 2.2f;

    [Header("Skill Icon")]
    [SerializeField] private Image[] skillBarIcons;
    [SerializeField] private Color skillIconOffEmission = Color.black;
    [SerializeField] private Color skillIconOnEmission = Color.cyan;
    [SerializeField] private float skillIconEmissionDuration = 0.15f;
    [SerializeField] private float skillIconEmissionIntensity = 2.2f;

    [Header("WeaponSet")]
    [SerializeField] private Animator weaponSwapAnimator;
    [SerializeField] private Material weaponReadyMaterial;
    [SerializeField] private Material weaponPreparingMaterial;

    [Header("Weapon Slots")]
    [SerializeField] private WeaponHudSlot normalSlot;
    [SerializeField] private WeaponHudSlot gearStage1Slot;
    [SerializeField] private WeaponHudSlot gearStage2Slot;
    [SerializeField] private WeaponHudSlot gearStage3Slot;
    [SerializeField] private WeaponHudSlot secondarySlot;

    [Header("Weapon Slot Motion")]
    [SerializeField] private float weaponCircleRotateSpeed = 25f;
    [SerializeField] private float readyPulseScale = 1.25f;
    [SerializeField] private float readyPulseDuration = 0.22f;
    [SerializeField] private float readyIconAlpha = 1f;
    [SerializeField] private float preparingIconAlpha = 0.35f;

    [Header("Gauge Motion")]
    [SerializeField] private float gaugeFillDuration = 0.2f;
    [Tooltip("체력 깎인 양이 남아 있는 시간과 따라 줄어드는 시간(BossHP와 같은 값)")]
    [SerializeField] private float gaugeSubDelay = 0.5f;
    [SerializeField] private float gaugeSubDuration = 0.5f;
    [SerializeField] private float gaugePunchDuration = 0.26f;
    [SerializeField] private int gaugePunchVibrato = 6;
    [Tooltip("위아래로 튀는 비율. 변화량이 gaugePunchFullChange 이상이면 최대값")]
    [SerializeField] private float gaugePunchMin = 0.08f;
    [SerializeField] private float gaugePunchMax = 0.3f;
    [SerializeField] private float gaugePunchFullChange = 0.25f;

    [Header("Gauge Idle Motion")]
    [Tooltip("체력 칸을 한 칸씩 훑는 점검 빛의 빠르기와 값이 바뀐 직후 더 빨라지는 양, 바뀐 지점 칸이 번쩍이는 시간")]
    [SerializeField] private float hpScanSpeed = 1f;
    [SerializeField] private float hpScanBoost = 2.5f;
    [SerializeField] private float hpPulseDuration = 1.2f;
    [Tooltip("스킬 배터리의 흐르는 빛·전류 시간의 빠르기와 값이 바뀐 직후 더 빨라지는 양, 칸·번개가 번쩍이는 시간")]
    [SerializeField] private float skillScanSpeed = 1f;
    [SerializeField] private float skillScanBoost = 4f;
    [SerializeField] private float skillPulseDuration = 0.6f;
    [Tooltip("켜진 단계 램프가 숨 쉬듯 밝아졌다 어두워지는 폭(세기 배율)과 빠르기(라디안/초)")]
    [SerializeField] private float skillLampBreath = 0.25f;
    [SerializeField] private float skillLampBreathSpeed = 3f;

    [Header("Skill Battery")]
    [Tooltip("스킬을 쓰면 쓴 칸이 방전 섬광으로 번쩍였다가 사라지는 시간(뒤쪽 바를 남기지 않는다)")]
    [SerializeField] private float skillDischargeDuration = 0.25f;
    [Tooltip("배터리 칸이 다 찬 순간 그 칸이 번쩍이는 시간")]
    [SerializeField] private float skillCellFlashDuration = 0.45f;

    [Header("Gear Stage Slot")]
    [Tooltip("단계 색으로 그리는 기어 슬롯 톱니의 밝기(준비 완료 · 준비 전)")]
    [SerializeField] private float gearReadyIntensity = 1.7f;
    [SerializeField] private float gearPreparingIntensity = 0.45f;

    [Header("Locked Slot")]
    [Tooltip("잠긴 칸(해금 전 Gear 기술·빈 보조 무기) 가운데의 자물쇠 크기와 색. 자물쇠는 실행 중에 아이콘 옆에 만든다")]
    [SerializeField] private float lockMarkSize = 30f;
    [SerializeField] private Color lockMarkColor = new Color(1f, 1f, 1f, .8f);

    [Header("Weapon Swap Motion")]
    [SerializeField] private float weaponSwapSlotScale = -0.4f;
    [SerializeField] private float weaponSwapSlotDegrees = -40f;
    [SerializeField] private float weaponSwapSlotDuration = 0.24f;
    [SerializeField] private float weaponSwapSlotStagger = 0.025f;

    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    private static readonly int FillID = Shader.PropertyToID("_Fill");
    private static readonly int ScanTimeID = Shader.PropertyToID("_ScanTime");
    private static readonly int PulseID = Shader.PropertyToID("_Pulse");
    private static readonly int PulseXID = Shader.PropertyToID("_PulseX");
    private static readonly int PulseAgeID = Shader.PropertyToID("_PulseAge");
    private static readonly int TrailID = Shader.PropertyToID("_Trail");
    private static readonly int DischargeID = Shader.PropertyToID("_Discharge");
    private static readonly int CellFlashID = Shader.PropertyToID("_CellFlash");

    private readonly Color[] gearStageColors = { Color.white, Color.white, Color.white };

    /// <summary>체력 바 바로 위의 해머 방어막 바(Start에서 Resources/UI/PlayerShieldBar로 만든다, 없으면 null). 검증이 읽기만 한다.</summary>
    public PlayerShieldBar ShieldBar { get; private set; }

    private Material hpBarMaterial;
    private Material skillGaugeBarMaterial;
    private Material[] skillIconMaterials;

    private float previousHpNormalized = 1f;
    private float hpScanTime, hpPulse, hpPulseX = 1f, hpPulseAge = 9f;
    private float skillScanTime, skillPulse;
    private readonly GaugeTrail hpTrail = new GaugeTrail { value = 1f };
    private float skillDischargeTop, skillDischarge;
    private Vector4 skillCellFlash;
    private float previousSkillGaugeNormalized = 0f;
    private int previousSkillIconCount = 0;
    private PlayerWeaponAbility attackRejectionSource;
    private TextMeshProUGUI attackRejectionText;
    private float attackRejectionUntil;
    private const float AttackRejectionDuration = 0.95f;
    private static readonly Color AttackRejectionColor = new Color32(255, 100, 98, 255);

    private void Awake()
    {
        InitializeHpBarMaterial();
        InitializeSkillGaugeMaterials();

        // 시작 값은 연출 없이 맞춘다(첫 호출이 게이지 소모로 보이지 않게).
        skillGaugeBar.fillAmount = 0f;

        SetSkillGauge(0, 100);
    }

    private void Start()
    {
        var prefab = Resources.Load<GameObject>("UI/WeaponTraitHud");
        if (prefab != null)
            Instantiate(prefab, transform).GetComponent<WeaponTraitHud>().Configure(gearStage1Slot?.cooldownText?.font, hpBar.rectTransform);

        // 체력 바 바로 위의 해머 방어막 바(2026-11-21 4차 사용자 요청 "체력 위에 쉴드 바"). 체력 바 바로 다음 형제로 들어간다(MainScene 무수정).
        var shieldPrefab = Resources.Load<GameObject>(PlayerShieldBar.ResourcePath);
        if (shieldPrefab != null && Instantiate(shieldPrefab, transform).TryGetComponent<PlayerShieldBar>(out var shieldBar))
        {
            ShieldBar = shieldBar;
            shieldBar.Configure(hpBar.rectTransform);
        }
    }

    private void OnDisable()
    {
        if (attackRejectionSource != null)
            attackRejectionSource.AttackRejected -= ShowAttackRejection;
        attackRejectionSource = null;
        if (attackRejectionText != null) attackRejectionText.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        KillWeaponSlotTween(normalSlot);
        KillWeaponSlotTween(gearStage1Slot);
        KillWeaponSlotTween(gearStage2Slot);
        KillWeaponSlotTween(gearStage3Slot);
        KillWeaponSlotTween(secondarySlot);

        DestroyGearSlotMaterials(gearStage1Slot);
        DestroyGearSlotMaterials(gearStage2Slot);
        DestroyGearSlotMaterials(gearStage3Slot);

        hpTrail.tween?.Kill();

        if (hpBarMaterial != null)
            Destroy(hpBarMaterial);

        if (skillGaugeBarMaterial != null)
            Destroy(skillGaugeBarMaterial);
    }

    private void Update()
    {
        RotateWeaponSlot(normalSlot);
        RotateWeaponSlot(gearStage1Slot);
        RotateWeaponSlot(gearStage2Slot);
        RotateWeaponSlot(gearStage3Slot);
        RotateWeaponSlot(secondarySlot);

        UpdateGaugeMotion();
        UpdateAttackRejection();
    }

    private void UpdateAttackRejection()
    {
        var attack = GameManager.Instance?.Player?.AbilityManager?.AttackAbility;
        if (attackRejectionSource != attack)
        {
            if (attackRejectionSource != null) attackRejectionSource.AttackRejected -= ShowAttackRejection;
            attackRejectionSource = attack;
            if (attackRejectionSource != null) attackRejectionSource.AttackRejected += ShowAttackRejection;
            if (attackRejectionText != null) attackRejectionText.gameObject.SetActive(false);
        }

        if (attackRejectionText == null || !attackRejectionText.gameObject.activeSelf) return;
        float remaining = attackRejectionUntil - Time.unscaledTime;
        attackRejectionText.alpha = Mathf.Clamp01(remaining / 0.2f);
        if (remaining <= 0f) attackRejectionText.gameObject.SetActive(false);
    }

    private void ShowAttackRejection(PlayerWeaponAbility.RejectionReason reason)
    {
        if (!isActiveAndEnabled || hudCanvasGroup == null || hudCanvasGroup.alpha <= 0f) return;
        if (attackRejectionText == null)
        {
            attackRejectionText = CreateCombatLabel(
                "AttackRejection", transform, gearStage3Slot?.cooldownText?.font, 23f, TextAlignmentOptions.MidlineLeft);
            var rect = attackRejectionText.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = WeaponTraitHud.BandPosition + new Vector2(0f, -50f);
            rect.sizeDelta = new Vector2(350f, 34f);
        }

        attackRejectionText.text = reason switch
        {
            PlayerWeaponAbility.RejectionReason.Cooldown => "기술 재사용 대기 중",
            PlayerWeaponAbility.RejectionReason.Gauge => "스킬 게이지 부족",
            PlayerWeaponAbility.RejectionReason.Resolve => "필살 3스택 필요",
            PlayerWeaponAbility.RejectionReason.Reloading => "재장전 중",
            PlayerWeaponAbility.RejectionReason.EmptyMagazine => "탄창이 비었습니다",
            PlayerWeaponAbility.RejectionReason.Overheated => "레일건 과열",
            PlayerWeaponAbility.RejectionReason.GearCharging => "기어 충전 중",
            PlayerWeaponAbility.RejectionReason.Busy => "기술 저장 중",
            _ => "사용할 수 없는 기술"
        };
        attackRejectionUntil = Time.unscaledTime + AttackRejectionDuration;
        attackRejectionText.alpha = 1f;
        attackRejectionText.gameObject.SetActive(true);
    }

    internal static void SetResolveRequirement(ref TextMeshProUGUI label, RectTransform icon, TMP_FontAsset font, int stacks, int required)
    {
        if (stacks < 0)
        {
            if (label != null) label.gameObject.SetActive(false);
            return;
        }
        if (icon == null) return;
        if (label == null)
        {
            label = CreateCombatLabel("FinisherRequirement", icon, font, 18f, TextAlignmentOptions.MidlineRight);
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(4f, -3f);
            rect.sizeDelta = new Vector2(48f, 20f);
        }

        // 스택이 달라졌을 때만 문자열과 TMP 메시를 갱신한다.
        string shown = label.text;
        if (string.IsNullOrEmpty(shown) || shown.Length != 3 || shown[0] != '0' + stacks || shown[2] != '0' + required)
            label.SetText("{0}/{1}", stacks, required);
        label.gameObject.SetActive(true);
    }

    private static TextMeshProUGUI CreateCombatLabel(string name, Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions alignment)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        root.layer = parent.gameObject.layer;
        root.transform.SetParent(parent, false);
        var label = root.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.fontSize = size;
        label.color = AttackRejectionColor;
        label.fontStyle = FontStyles.Bold;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        label.outlineColor = new Color32(14, 7, 15, 255);
        label.outlineWidth = 0.18f;
        return label;
    }

    // 체력·스킬 칸 게이지의 채움·깎인 양·방전·점검 빛·번쩍임을 셰이더에 넘긴다. 실제 시간 기준이라 히트스톱 중에도 멈추지 않는다.
    private void UpdateGaugeMotion()
    {
        float dt = Time.unscaledDeltaTime;

        hpPulse = Mathf.MoveTowards(hpPulse, 0f, dt / Mathf.Max(hpPulseDuration, 0.01f));
        hpPulseAge += dt;
        hpScanTime += dt * (hpScanSpeed + hpPulse * hpScanBoost);
        if (hpBarMaterial != null)
        {
            hpBarMaterial.SetFloat(FillID, hpBar.fillAmount);
            hpBarMaterial.SetFloat(TrailID, Mathf.Max(hpTrail.value, hpBar.fillAmount));
            hpBarMaterial.SetFloat(ScanTimeID, hpScanTime);
            hpBarMaterial.SetFloat(PulseID, hpPulse);
            hpBarMaterial.SetFloat(PulseXID, hpPulseX);
            hpBarMaterial.SetFloat(PulseAgeID, hpPulseAge);
        }

        skillPulse = Mathf.MoveTowards(skillPulse, 0f, dt / Mathf.Max(skillPulseDuration, 0.01f));
        skillScanTime += dt * (skillScanSpeed + skillPulse * skillScanBoost);
        skillDischarge = Mathf.MoveTowards(skillDischarge, 0f, dt / Mathf.Max(skillDischargeDuration, 0.01f));
        if (skillDischarge <= 0f)
            skillDischargeTop = skillGaugeBar.fillAmount;
        for (int i = 0; i < 4; i++)
            skillCellFlash[i] = Mathf.MoveTowards(skillCellFlash[i], 0f, dt / Mathf.Max(skillCellFlashDuration, 0.01f));
        if (skillGaugeBarMaterial != null)
        {
            skillGaugeBarMaterial.SetFloat(FillID, skillGaugeBar.fillAmount);
            skillGaugeBarMaterial.SetFloat(TrailID, Mathf.Max(skillDischargeTop, skillGaugeBar.fillAmount));
            skillGaugeBarMaterial.SetFloat(DischargeID, skillDischarge);
            skillGaugeBarMaterial.SetVector(CellFlashID, skillCellFlash);
            skillGaugeBarMaterial.SetFloat(ScanTimeID, skillScanTime);
            skillGaugeBarMaterial.SetFloat(PulseID, skillPulse);
        }

        // 켜진 단계 램프만 숨 쉬듯 밝기가 오르내린다(꺼진 램프는 기본 세기)
        if (skillIconMaterials == null)
            return;

        for (int i = 0; i < skillIconMaterials.Length; i++)
        {
            if (skillIconMaterials[i] == null)
                continue;

            float breath = i < previousSkillIconCount ? skillLampBreath * Mathf.Sin(skillScanTime * skillLampBreathSpeed + i * 1.3f) : 0f;
            skillIconMaterials[i].SetFloat(IntensityID, 1f + breath);
        }
    }
    public void HUDVisible(bool isVisible)
    {
        hudCanvasGroup.alpha = isVisible ? 1f : 0f;
        if (!isVisible && attackRejectionText != null) attackRejectionText.gameObject.SetActive(false);
    }
    public void UpdateWeaponCooldowns(
    float normalRemaining,
    float normalDuration,
    float gear1Remaining,
    float gear1Duration,
    float gear2Remaining,
    float gear2Duration,
    float gear3Remaining,
    float gear3Duration,
    float secondaryRemaining,
    float secondaryDuration)
    {
        //SetSlotCooldown(normalSlot, normalRemaining, normalDuration);
        HideCooldown(normalSlot);

        SetSlotCooldown(gearStage1Slot, gear1Remaining, gear1Duration);
        SetSlotCooldown(gearStage2Slot, gear2Remaining, gear2Duration);
        SetSlotCooldown(gearStage3Slot, gear3Remaining, gear3Duration);
        SetSlotCooldown(secondarySlot, secondaryRemaining, secondaryDuration);
    }
    private void SetSlotCooldown(WeaponHudSlot slot, float remaining, float duration)
    {
        if (slot == null)
            return;

        float normalized = 0f;

        if (duration > 0f)
            normalized = Mathf.Clamp01(remaining / duration);

        bool coolingDown = remaining > 0.01f && duration > 0f;

        if (slot.cooldownFill != null)
        {
            slot.cooldownFill.gameObject.SetActive(coolingDown);
            slot.cooldownFill.fillAmount = normalized;
        }

        if (slot.cooldownText != null)
        {
            slot.cooldownText.gameObject.SetActive(coolingDown);

            // 숨겨진 동안에는 글자가 보이지 않으므로 넣지 않는다(대기 중 매 프레임 "0.0" 문자열을 만들던 할당 제거).
            // 다시 보일 때는 위 SetActive와 같은 호출에서 바로 맞추므로 보이는 글자는 기존과 같다.
            if (coolingDown)
                ApplyCooldownText(slot.cooldownText, remaining, ref slot.shownCooldownKey);
        }

        var attack = GameManager.Instance?.Player?.AbilityManager?.AttackAbility;
        SetSlotCooldownReady(slot, !coolingDown && (attack == null || attack.IsWeaponTraitReady(slot.weapon)));
        int required = PlayerWeaponTraits.ResolveCapacity;
        int stacks = slot.locked || attack == null ? -1 : attack.GetResolveRequirementStacks(slot.weapon, out required);
        SetResolveRequirement(ref slot.finisherRequirement, slot.icon?.rectTransform, slot.cooldownText?.font, stacks, required);
    }
    private void HideCooldown(WeaponHudSlot slot)
    {
        if (slot == null)
            return;

        if (slot.cooldownFill != null)
        {
            slot.cooldownFill.fillAmount = 0f;
            slot.cooldownFill.gameObject.SetActive(false);
        }

        if (slot.cooldownText != null)
        {
            slot.cooldownText.text = "";
            slot.shownCooldownKey = 0;
            slot.cooldownText.gameObject.SetActive(false);
        }
    }
    // 쿨다운 남은 시간 글자를 넣는다. 표시 규칙은 기존 식과 같다(1초 초과는 올림 정수, 1초 이하는 ToString("0.0")).
    // 표시 키(1초 초과 = 올림 정수, 1초 이하 = -(0.1초 단위 + 1))가 바뀔 때만 문자열을 만들어 넣어 매 프레임 할당과 비교를 없앤다.
    // PlayerGearUI의 단계 무기 쿨다운 글자도 이 함수를 같이 써서 규칙을 한 곳에 둔다.
    internal static void ApplyCooldownText(TMP_Text text, float remaining, ref int shownKey)
    {
        if (remaining > 1f)
        {
            int seconds = Mathf.CeilToInt(remaining);
            if (seconds == shownKey)
                return;

            shownKey = seconds;
            text.text = seconds.ToString();
            return;
        }

        // 0.1초 반올림 경계(x.x5) 바로 근처는 반올림 결과가 구현에 따라 갈릴 수 있으므로 기존 식을 그대로 써서 같은 문자열을 보장한다.
        float scaled = remaining * 10f;
        float whole = Mathf.Floor(scaled);
        float fraction = scaled - whole;
        if (Mathf.Abs(fraction - 0.5f) < 0.01f)
        {
            shownKey = 0;
            text.text = remaining.ToString("0.0");
            return;
        }

        // 경계에서 떨어진 값은 가장 가까운 0.1초로 반올림된다. 그 0.1초 값(tenths / 10f)을 같은 형식으로 만들면 같은 문자열이 나온다.
        int tenths = (int)whole + (fraction > 0.5f ? 1 : 0);
        int key = -1 - tenths;
        if (key == shownKey)
            return;

        shownKey = key;
        text.text = (tenths / 10f).ToString("0.0");
    }

    public void SwapWeaponSetImage(WeaponSet weaponSet, float value, bool animate = false)
    {
        if (weaponSwapAnimator != null)
        {
            weaponSwapAnimator.Rebind();
            weaponSwapAnimator.Update(0f);
        }

        SetSlotWeapon(normalSlot, weaponSet.normal);
        SetSlotWeapon(gearStage1Slot, weaponSet.gearStage1);
        SetSlotWeapon(gearStage2Slot, weaponSet.gearStage2);
        SetSlotWeapon(gearStage3Slot, weaponSet.gearStage3);
        SetSlotWeapon(secondarySlot, weaponSet.secondary);

        SetSlotLocked(gearStage1Slot, weaponSet.IsSlotLocked(1));
        SetSlotLocked(gearStage2Slot, weaponSet.IsSlotLocked(2));
        SetSlotLocked(gearStage3Slot, weaponSet.IsSlotLocked(3));
        SetSlotLocked(secondarySlot, weaponSet.IsSlotLocked(WeaponSet.SecondarySlot));

        UpdateWeaponSetImage(weaponSet, value);
        UpdateNormalSlotStatic();

        if (animate)
            PlayWeaponSwapSlotMotion();
    }

    // 무기를 바꾸면 슬롯 아이콘이 왼쪽부터 차례로 돌며 튄다(실린더가 돌아 새 무기가 들어오는 느낌).
    private void PlayWeaponSwapSlotMotion()
    {
        PunchSlotIcon(normalSlot, 0);
        PunchSlotIcon(gearStage1Slot, 1);
        PunchSlotIcon(gearStage2Slot, 2);
        PunchSlotIcon(gearStage3Slot, 3);
        PunchSlotIcon(secondarySlot, 4);
    }

    private void PunchSlotIcon(WeaponHudSlot slot, int order)
    {
        if (slot == null || slot.icon == null)
            return;

        RectTransform icon = slot.icon.rectTransform;
        icon.DOKill(true);

        float delay = order * weaponSwapSlotStagger;
        icon.DOPunchScale(Vector3.one * weaponSwapSlotScale, weaponSwapSlotDuration, 6, 0.5f).SetDelay(delay);
        icon.DOPunchRotation(new Vector3(0f, 0f, weaponSwapSlotDegrees), weaponSwapSlotDuration, 6, 0.5f).SetDelay(delay);
    }
    public void UpdateWeaponSetImage(WeaponSet weaponSet, float value)
    {
        UpdateNormalSlotStatic();

        SetSlotGaugeReady(gearStage1Slot, IsWeaponReady(weaponSet.gearStage1, value));
        SetSlotGaugeReady(gearStage2Slot, IsWeaponReady(weaponSet.gearStage2, value));
        SetSlotGaugeReady(gearStage3Slot, IsWeaponReady(weaponSet.gearStage3, value));
        SetSlotGaugeReady(secondarySlot, IsWeaponReady(weaponSet.secondary, value));
    }
    private void UpdateNormalSlotStatic()
    {
        if (normalSlot == null)
            return;

        normalSlot.isReady = false;

        if (normalSlot.circle != null)
        {
            normalSlot.circle.rectTransform.DOKill();
            normalSlot.circle.rectTransform.localScale = Vector3.one;

            // Normal은 Ready/Preparing material을 건드리지 않는다.
            // Inspector에 넣어둔 기본 Circle Material 그대로 사용.
        }

        if (normalSlot.icon != null)
        {
            Color color = normalSlot.icon.color;
            color.a = 1f;
            normalSlot.icon.color = color;
        }
    }
    public void SetHP(float value, float max)
    {
        float normalized = max <= 0f ? 0f : Mathf.Clamp01(value / max);
        float change = normalized - previousHpNormalized;
        previousHpNormalized = normalized;

        AnimateGaugeFill(hpBar, normalized);
        UpdateTrail(hpTrail, normalized);

        if (Mathf.Approximately(change, 0f))
            return;

        // 바뀐 지점 둘레 칸이 번쩍이고 점검 빛이 잠깐 빨라진다
        hpPulse = 1f;
        hpPulseX = normalized;
        hpPulseAge = 0f;

        PunchGauge(hpPunchTargets, change);
        PlayHpBarEmissionFlash(change < 0f ? hpDamageEmission : hpHealEmission);
    }

    public void SetSkillGauge(float value, float max)
    {
        float normalized = max <= 0f ? 0f : Mathf.Clamp01(value / max);
        float change = normalized - previousSkillGaugeNormalized;

        AnimateGaugeFill(skillGaugeBar, normalized);

        // 쓴 칸은 뒤쪽 바를 남기지 않고 짧은 방전 섬광으로 번쩍인 뒤 사라진다(방전 중에 또 쓰면 위쪽 끝을 유지한다)
        if (change < -0.0001f)
        {
            skillDischargeTop = skillDischarge > 0f ? Mathf.Max(skillDischargeTop, previousSkillGaugeNormalized) : previousSkillGaugeNormalized;
            skillDischarge = 1f;
        }

        if (!Mathf.Approximately(change, 0f))
        {
            // 바뀌면 배터리 칸이 밝아지고 번개 표시가 번쩍인다
            skillPulse = 1f;

            PunchGauge(skillPunchTargets, change);
            PlaySkillBarEmissionPulse(change > 0f ? skillGaugeBarGainEmission : skillGaugeBarSpendEmission);
        }

        UpdateSkillBarIcons(normalized);

        previousSkillGaugeNormalized = normalized;
    }

    private void AnimateGaugeFill(Image bar, float normalized)
    {
        if (bar == null)
            return;

        bar.DOKill();
        bar.DOFillAmount(normalized, gaugeFillDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    // 체력이 줄면 뒤쪽 값이 잠시 남았다가 따라 줄어 깎인 양을 보여 준다(BossHP와 같은 타이밍). 늘어날 때는 바로 맞춘다.
    // 칸이 비스듬해 가로 채움 이미지로는 경계가 어긋나므로 이 값을 셰이더 _Trail로 그린다(스킬은 뒤쪽 값 대신 방전 섬광).
    private void UpdateTrail(GaugeTrail trail, float normalized)
    {
        trail.tween?.Kill();

        if (normalized >= trail.value)
        {
            trail.value = normalized;
            return;
        }

        trail.tween = DOTween.To(() => trail.value, value => trail.value = value, normalized, gaugeSubDuration)
            .SetDelay(gaugeSubDelay)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);
    }

    // 바와 뒤판을 위아래로 튕긴다. 많이 바뀔수록 크게 튄다.
    private void PunchGauge(RectTransform[] targets, float change)
    {
        if (targets == null)
            return;

        float amount = Mathf.Lerp(gaugePunchMin, gaugePunchMax, Mathf.Clamp01(Mathf.Abs(change) / gaugePunchFullChange));

        foreach (RectTransform target in targets)
        {
            if (target == null)
                continue;

            target.DOKill(true);
            target.localScale = Vector3.one;
            target.DOPunchScale(new Vector3(0f, amount, 0f), gaugePunchDuration, gaugePunchVibrato, 0.6f).SetUpdate(true);
        }
    }

    private void InitializeHpBarMaterial()
    {
        hpBarMaterial = Instantiate(hpBar.material);
        hpBar.material = hpBarMaterial;
        hpBarMaterial.SetColor(EmissionColorID, hpIdleEmission);
    }

    private void PlayHpBarEmissionFlash(Color flashColor)
    {
        if (hpBarMaterial == null)
            return;

        hpBarMaterial.DOKill();
        hpBarMaterial.SetColor(EmissionColorID, flashColor * hpFlashStrength);
        hpBarMaterial
            .DOColor(hpIdleEmission, EmissionColorID, hpEmissionDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);
    }

    // PlayerGearUI의 단계 색을 기어 슬롯 톱니와 스킬 램프에 쓴다. 램프 1·2·3의 비용이 기어 슬롯 1·2·3과 같다.
    public void SetGearStageColors(Color stage1, Color stage2, Color stage3)
    {
        gearStageColors[0] = stage1;
        gearStageColors[1] = stage2;
        gearStageColors[2] = stage3;

        ApplyGearSlotColor(gearStage1Slot, stage1);
        ApplyGearSlotColor(gearStage2Slot, stage2);
        ApplyGearSlotColor(gearStage3Slot, stage3);

        for (int i = 0; i < skillBarIcons.Length; i++)
        {
            if (IsValidSkillIconIndex(i))
                skillIconMaterials[i].SetColor(EmissionColorID, GetSkillIconEmission(i, i < previousSkillIconCount));
        }

    }

    private void ApplyGearSlotColor(WeaponHudSlot slot, Color stageColor)
    {
        if (slot == null || slot.circle == null)
            return;

        if (slot.readyMaterial == null)
        {
            slot.readyMaterial = Instantiate(weaponReadyMaterial);
            slot.preparingMaterial = Instantiate(weaponPreparingMaterial);
        }

        slot.readyMaterial.SetColor(EmissionColorID, stageColor);
        slot.readyMaterial.SetFloat(IntensityID, gearReadyIntensity);
        slot.preparingMaterial.SetColor(EmissionColorID, stageColor);
        slot.preparingMaterial.SetFloat(IntensityID, gearPreparingIntensity);

        if (!slot.locked)
            slot.circle.material = slot.isReady ? slot.readyMaterial : slot.preparingMaterial;
    }

    private void DestroyGearSlotMaterials(WeaponHudSlot slot)
    {
        if (slot == null)
            return;

        if (slot.readyMaterial != null)
            Destroy(slot.readyMaterial);

        if (slot.preparingMaterial != null)
            Destroy(slot.preparingMaterial);
    }

    private Color GetSkillIconEmission(int index, bool on)
    {
        Color stageColor = index < gearStageColors.Length ? gearStageColors[index] : Color.white;
        return stageColor * (on ? skillIconOnEmission : skillIconOffEmission) * skillIconEmissionIntensity;
    }

    private void InitializeSkillGaugeMaterials()
    {
        skillGaugeBarMaterial = Instantiate(skillGaugeBar.material);
        skillGaugeBar.material = skillGaugeBarMaterial;
        skillGaugeBarMaterial.SetColor(
            EmissionColorID,
            skillGaugeBarIdleEmission * skillGaugeBarEmissionIntensity);

        skillIconMaterials = new Material[skillBarIcons.Length];

        for (int i = 0; i < skillBarIcons.Length; i++)
        {
            if (skillBarIcons[i] == null)
                continue;

            Material instanced = Instantiate(skillBarIcons[i].material);
            skillBarIcons[i].material = instanced;
            skillIconMaterials[i] = instanced;

            instanced.SetColor(EmissionColorID, GetSkillIconEmission(i, false));
        }
    }

    // 칸의 잠금 상태를 바꾼다(PlayerWeaponAbility의 실행용 사본이 남긴 WeaponSet 잠금 표시). 잠기면 아이콘 없는 회색 잠금 링 + 작은 자물쇠.
    private void SetSlotLocked(WeaponHudSlot slot, bool locked)
    {
        if (slot == null)
            return;

        bool changed = slot.locked != locked;
        slot.locked = locked;

        if (locked)
        {
            EnsureLockMark(slot);
            if (slot.circle != null)
            {
                slot.circle.rectTransform.DOKill();
                slot.circle.rectTransform.localScale = Vector3.one;
            }
            ShowLockedSlot(slot);
            return;
        }

        if (slot.lockMark != null)
            slot.lockMark.enabled = false;

        // 잠금 링 재질이 남지 않도록 준비 중 모습으로 한 번 맞춘다(다음 갱신에서 준비 상태가 다시 정해진다).
        if (changed)
        {
            slot.isReady = false;
            SetSlotVisual(slot, false, slot.weapon == null);
        }
    }

    // 잠긴 칸의 모습: 아이콘을 끄고, 원은 회색 잠금 링 재질로 맞추고, 자물쇠를 켠다. 매 프레임 불리므로 바뀐 값만 넣는다.
    private void ShowLockedSlot(WeaponHudSlot slot)
    {
        slot.isReady = false;

        if (slot.circle != null)
        {
            Material material = LockedRingMaterial(slot);
            if (material != null && slot.circle.material != material)
                slot.circle.material = material;

            if (slot.circle.rectTransform.localScale != Vector3.one)
            {
                slot.circle.rectTransform.DOKill();
                slot.circle.rectTransform.localScale = Vector3.one;
            }
        }

        if (slot.icon != null && slot.icon.enabled)
            slot.icon.enabled = false;

        if (slot.lockMark != null && !slot.lockMark.enabled)
            slot.lockMark.enabled = true;
    }

    private Material LockedRingMaterial(WeaponHudSlot slot)
    {
        ArmamentUnlockCatalogSO catalog = ArmamentUnlockCatalogSO.Load();
        if (catalog != null && catalog.lockedRingMaterial != null)
            return catalog.lockedRingMaterial;

        return slot.preparingMaterial != null ? slot.preparingMaterial : weaponPreparingMaterial;
    }

    // 자물쇠 그림을 아이콘 바로 다음 형제(쿨다운 표시 앞)로 한 번 만든다. 원의 형제라 칸 원과 함께 돌지 않는다.
    private void EnsureLockMark(WeaponHudSlot slot)
    {
        if (slot.lockMark != null || slot.icon == null)
            return;

        ArmamentUnlockCatalogSO catalog = ArmamentUnlockCatalogSO.Load();
        Sprite glyph = catalog != null ? catalog.lockGlyph : null;
        if (glyph == null)
            return;

        Transform parent = slot.icon.transform.parent;
        GameObject mark = new GameObject("LockMark", typeof(RectTransform), typeof(Image));
        mark.layer = slot.icon.gameObject.layer;

        RectTransform rect = (RectTransform)mark.transform;
        rect.SetParent(parent, false);
        rect.SetSiblingIndex(slot.icon.transform.GetSiblingIndex() + 1);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(lockMarkSize, lockMarkSize);
        RectTransform iconRect = slot.icon.rectTransform;
        rect.localPosition = parent != null ? parent.InverseTransformPoint(iconRect.TransformPoint(iconRect.rect.center)) : iconRect.localPosition;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        Image image = mark.GetComponent<Image>();
        image.sprite = glyph;
        image.color = lockMarkColor;
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.enabled = false;
        slot.lockMark = image;
    }

    private void SetSlotWeapon(WeaponHudSlot slot, WeaponBaseSO weapon)
    {
        if (slot == null)
            return;

        slot.weapon = weapon;

        if (slot.icon == null)
            return;

        if (weapon == null)
        {
            slot.icon.enabled = false;
            return;
        }

        slot.icon.enabled = true;
        slot.icon.sprite = weapon.weaponSprite;
    }

    private bool IsWeaponReady(WeaponBaseSO weapon, float gauge)
    {
        if (weapon == null)
            return false;

        return weapon.skillGauge <= gauge;
    }
    private void SetSlotGaugeReady(WeaponHudSlot slot, bool ready)
    {
        if (slot == null)
            return;

        slot.gaugeReady = ready;
        RefreshSlotCombinedReady(slot);
    }

    private void SetSlotCooldownReady(WeaponHudSlot slot, bool ready)
    {
        if (slot == null)
            return;

        slot.cooldownReady = ready;
        RefreshSlotCombinedReady(slot);
    }

    private void RefreshSlotCombinedReady(WeaponHudSlot slot)
    {
        if (slot == null)
            return;

        bool ready = slot.gaugeReady && slot.cooldownReady;
        UpdateSlotReady(slot, ready);
    }
    private void UpdateSlotReady(WeaponHudSlot slot, bool ready)
    {
        if (slot == null)
            return;

        if (slot.locked)
        {
            ShowLockedSlot(slot);
            return;
        }

        if (slot.weapon == null && !slot.alwaysReady)
        {
            SetSlotVisual(slot, false, true);
            return;
        }

        if (slot.alwaysReady)
            ready = true;

        if (slot.isReady == ready)
        {
            RefreshSlotAlpha(slot, ready);
            return;
        }

        slot.isReady = ready;
        SetSlotVisual(slot, ready, false);
    }

    private void SetSlotVisual(WeaponHudSlot slot, bool ready, bool forceOff)
    {
        if (slot.circle != null)
        {
            Material readyMaterial = slot.readyMaterial != null ? slot.readyMaterial : weaponReadyMaterial;
            Material preparingMaterial = slot.preparingMaterial != null ? slot.preparingMaterial : weaponPreparingMaterial;
            slot.circle.material = ready ? readyMaterial : preparingMaterial;

            slot.circle.rectTransform.DOKill();
            slot.circle.rectTransform.localScale = Vector3.one;

            if (ready && !forceOff)
            {
                slot.circle.rectTransform
                    .DOScale(readyPulseScale, readyPulseDuration)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine);
            }
        }

        RefreshSlotAlpha(slot, ready && !forceOff);
    }

    private void RefreshSlotAlpha(WeaponHudSlot slot, bool ready)
    {
        if (slot.icon == null)
            return;

        Color color = slot.icon.color;
        color.a = ready ? readyIconAlpha : preparingIconAlpha;
        slot.icon.color = color;
    }

    private void RotateWeaponSlot(WeaponHudSlot slot)
    {
        if (slot == null || slot.circle == null)
            return;

        slot.circle.transform.Rotate(new Vector3(0f, 0f, weaponCircleRotateSpeed * Time.deltaTime));
    }

    private void KillWeaponSlotTween(WeaponHudSlot slot)
    {
        if (slot == null || slot.circle == null)
            return;

        slot.circle.rectTransform.DOKill();

        if (slot.icon != null)
            slot.icon.rectTransform.DOKill();
    }

    #region Skill Effect

    private void PlaySkillBarEmissionPulse(Color flashColor)
    {
        if (skillGaugeBarMaterial == null)
            return;

        skillGaugeBarMaterial.DOKill();

        skillGaugeBarMaterial.SetColor(
            EmissionColorID,
            flashColor * skillGaugeBarEmissionIntensity * skillGaugeBarFlashStrength);

        skillGaugeBarMaterial
            .DOColor(
                skillGaugeBarIdleEmission * skillGaugeBarEmissionIntensity,
                EmissionColorID,
                skillGaugeBarEmissionDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);
    }

    private void UpdateSkillBarIcons(float normalized)
    {
        int iconCount = Mathf.FloorToInt(normalized * 4f + 0.0001f);
        iconCount = Mathf.Clamp(iconCount, 0, skillBarIcons.Length);

        if (iconCount == previousSkillIconCount)
            return;

        if (iconCount > previousSkillIconCount)
        {
            for (int i = previousSkillIconCount; i < iconCount; i++)
            {
                PlaySkillIconEmissionOn(i);

                // 막 다 찬 배터리 칸이 번쩍인다
                if (i < 4)
                    skillCellFlash[i] = 1f;
            }
        }
        else
        {
            for (int i = iconCount; i < previousSkillIconCount; i++)
                PlaySkillIconEmissionOff(i);
        }

        previousSkillIconCount = iconCount;
    }

    private void PlaySkillIconEmissionOn(int index)
    {
        if (!IsValidSkillIconIndex(index))
            return;

        Material mat = skillIconMaterials[index];

        mat.DOKill();
        mat.DOColor(
                GetSkillIconEmission(index, true),
                EmissionColorID,
                skillIconEmissionDuration)
            .SetEase(Ease.OutQuad);
    }

    private void PlaySkillIconEmissionOff(int index)
    {
        if (!IsValidSkillIconIndex(index))
            return;

        Material mat = skillIconMaterials[index];

        mat.DOKill();
        mat.DOColor(
                GetSkillIconEmission(index, false),
                EmissionColorID,
                skillIconEmissionDuration)
            .SetEase(Ease.OutQuad);
    }

    private bool IsValidSkillIconIndex(int index)
    {
        if (skillIconMaterials == null)
            return false;

        if (index < 0 || index >= skillIconMaterials.Length)
            return false;

        return skillIconMaterials[index] != null;
    }

    #endregion
}