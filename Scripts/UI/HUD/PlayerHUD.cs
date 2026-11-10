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

    private void Awake()
    {
        InitializeHpBarMaterial();
        InitializeSkillGaugeMaterials();

        // 시작 값은 연출 없이 맞춘다(첫 호출이 게이지 소모로 보이지 않게).
        skillGaugeBar.fillAmount = 0f;

        SetSkillGauge(0, 100);
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
            slot.cooldownText.text = remaining > 1f
                ? Mathf.CeilToInt(remaining).ToString()
                : remaining.ToString("0.0");
        }

        SetSlotCooldownReady(slot, !coolingDown);
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
            slot.cooldownText.gameObject.SetActive(false);
        }
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