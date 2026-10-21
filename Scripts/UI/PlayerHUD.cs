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
        [HideInInspector] public WeaponBase weapon;
    }

    [Header("HUD CanvasGroup")]
    [SerializeField] private CanvasGroup hudCanvasGroup;

    [Header("HP")]
    [SerializeField] private Image hpBar;

    [Header("Skill Bar")]
    [SerializeField] private Image skillGaugeBar;
    [SerializeField] private Color skillGaugeBarIdleEmission = Color.black;
    [SerializeField] private Color skillGaugeBarGainEmission = Color.cyan;
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

    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    private Material skillGaugeBarMaterial;
    private Material[] skillIconMaterials;

    private float previousSkillGaugeNormalized = 0f;
    private int previousSkillIconCount = 0;

    private void Awake()
    {
        InitializeSkillGaugeMaterials();

        SetSkillGauge(0, 100);
    }

    private void OnDestroy()
    {
        KillWeaponSlotTween(normalSlot);
        KillWeaponSlotTween(gearStage1Slot);
        KillWeaponSlotTween(gearStage2Slot);
        KillWeaponSlotTween(gearStage3Slot);
        KillWeaponSlotTween(secondarySlot);
    }

    private void Update()
    {
        RotateWeaponSlot(normalSlot);
        RotateWeaponSlot(gearStage1Slot);
        RotateWeaponSlot(gearStage2Slot);
        RotateWeaponSlot(gearStage3Slot);
        RotateWeaponSlot(secondarySlot);
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
    public void SwapWeaponSetImage(WeaponSet weaponSet, float value)
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
        hpBar.DOFillAmount(value / max, 0.3f);
    }

    public void SetSkillGauge(float value, float max)
    {
        float normalized = max <= 0f ? 0f : Mathf.Clamp01(value / max);

        skillGaugeBar.DOKill();
        skillGaugeBar.DOFillAmount(normalized, 0.25f).SetEase(Ease.OutQuad);

        if (normalized > previousSkillGaugeNormalized)
        {
            PlaySkillBarEmissionPulse();
        }

        UpdateSkillBarIcons(normalized);

        previousSkillGaugeNormalized = normalized;
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

            instanced.SetColor(
                EmissionColorID,
                skillIconOffEmission * skillIconEmissionIntensity);
        }
    }

    private void SetSlotWeapon(WeaponHudSlot slot, WeaponBase weapon)
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

    private bool IsWeaponReady(WeaponBase weapon, float gauge)
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
            slot.circle.material = ready ? weaponReadyMaterial : weaponPreparingMaterial;

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
    }

    #region Skill Effect

    private void PlaySkillBarEmissionPulse()
    {
        if (skillGaugeBarMaterial == null)
            return;

        skillGaugeBarMaterial.DOKill();

        skillGaugeBarMaterial.SetColor(
            EmissionColorID,
            skillGaugeBarGainEmission * skillGaugeBarEmissionIntensity);

        skillGaugeBarMaterial
            .DOColor(
                skillGaugeBarIdleEmission * skillGaugeBarEmissionIntensity,
                EmissionColorID,
                skillGaugeBarEmissionDuration)
            .SetEase(Ease.OutQuad);
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
                PlaySkillIconEmissionOn(i);
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
                skillIconOnEmission * skillIconEmissionIntensity,
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
                skillIconOffEmission * skillIconEmissionIntensity,
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