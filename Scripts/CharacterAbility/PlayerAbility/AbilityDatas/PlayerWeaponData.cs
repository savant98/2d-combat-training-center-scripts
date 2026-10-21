using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Weapon Data")]
public class PlayerWeaponData : PlayerAbilityData
{
    [Header("Weapons")]
    public WeaponSet[] weaponSets = new WeaponSet[2];

    [Header("SkillGauge")]
    public float maxSkillGauge;

    [Header("Skill Gauge Orb")]
    public SkillGaugeOrbSettings skillGaugeOrbSettings;

    [Header("WeaponSwap")]
    public float swapCoolTime = 0.5f;
    public FeedbackData swapFeedback;

    [Header("Gear")]
    [Tooltip("각 Gear 단계에 도달하기까지 필요한 충전 시간")]
    [Min(0f)] public float gearStageChargeTime = 0.35f;

    [Tooltip("각 Gear 단계에 도달한 뒤, 다음 입력을 기다리는 시간")]
    [Min(0f)] public float gearWaitTime = 0.4f;
}
[Serializable]
public class WeaponSet
{
    public WeaponBase normal;

    [Header("Gear Weapons")]
    public WeaponBase gearStage1;
    public WeaponBase gearStage2;
    public WeaponBase gearStage3;

    public WeaponBase secondary;

    public WeaponBase GetGearWeapon(int stage)
    {
        return stage switch
        {
            1 => gearStage1,
            2 => gearStage2,
            3 => gearStage3,
            _ => null
        };
    }
}
