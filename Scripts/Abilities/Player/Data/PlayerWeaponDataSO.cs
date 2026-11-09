using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Weapon Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "PlayerWeaponData")]
public class PlayerWeaponDataSO : PlayerAbilityDataSO
{
    [Header("Weapons")]
    public WeaponSet[] weaponSets = new WeaponSet[2];

    [Header("SkillGauge")]
    public float maxSkillGauge;

    [Header("Skill Gauge Orb")]
    public SkillGaugeOrbSettings skillGaugeOrbSettings;

    [Header("WeaponSwap")]
    public float swapCoolTime = 0.5f;
    public FeedbackDataSO swapFeedback;
    [Tooltip("무기를 바꾸는 순간 머리 위에 떠오르는 교체 아이콘 표시")]
    public PlayerWeaponSwapVisual weaponSwapVisual;

    [Header("Gear")]
    [Tooltip("각 Gear 단계에 도달하기까지 필요한 충전 시간")]
    [Min(0f)] public float gearStageChargeTime = 0.35f;

    [Tooltip("각 Gear 단계에 도달한 뒤, 다음 입력을 기다리는 시간")]
    [Min(0f)] public float gearWaitTime = 0.4f;

    [Header("Gear Presentation")]
    [Tooltip("Gear 준비·대기 동안 Player 아래에 표시되는 체결 고리와 소리")]
    public PlayerGearVisual gearVisual;

    [Tooltip("1~3단계의 준비음과 준비 완료 Feedback")]
    public GearStagePresentation[] gearStagePresentations = new GearStagePresentation[3];

    [Tooltip("준비 완료 뒤 대기하는 동안 나는 Loop")]
    public SoundOptions gearHoldSound;

    [Tooltip("대기 시간이 한 칸 줄 때마다 나는 틱")]
    public SoundOptions gearTickSound;

    [Tooltip("손을 떼거나 대기 시간이 끝나 풀릴 때의 소리")]
    public SoundOptions gearDisengageSound;
}
[Serializable]
public class GearStagePresentation
{
    [Tooltip("준비 중 소리. 취소되면 바로 멈춰야 하므로 Loop로 재생한다")]
    public SoundOptions chargeSound;

    [Tooltip("준비 완료 순간의 체결 효과·소리·흔들림")]
    public FeedbackDataSO lockFeedback;
}
[Serializable]
public class WeaponSet
{
    public WeaponBaseSO normal;

    [Header("Gear Weapons")]
    public WeaponBaseSO gearStage1;
    public WeaponBaseSO gearStage2;
    public WeaponBaseSO gearStage3;

    public WeaponBaseSO secondary;

    [Header("Swap Presentation")]
    [Tooltip("이 세트로 바꿀 때 머리 위 교체 아이콘의 색")]
    public Color swapColor = Color.white;

    public WeaponBaseSO GetGearWeapon(int stage)
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
