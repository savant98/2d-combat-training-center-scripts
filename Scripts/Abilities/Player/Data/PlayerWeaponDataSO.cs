using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Weapon Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "PlayerWeaponData")]
public class PlayerWeaponDataSO : PlayerAbilityDataSO
{
    [Header("Weapons")]
    public WeaponSet[] weaponSets = new WeaponSet[2];

    [Header("Armaments")]
    [Tooltip("업그레이드 메뉴에서 장착할 수 있는 무장 전체(기본 무기 + Gear 1~3단계). 같은 분류의 무장끼리만 바꿔 낀다.")]
    public PlayerArmamentSO[] armaments = new PlayerArmamentSO[0];

    // 무장 목록에서 armamentId로 찾는다. 없으면 null.
    public PlayerArmamentSO FindArmament(string armamentId)
    {
        if (string.IsNullOrEmpty(armamentId) || armaments == null) return null;
        foreach (var armament in armaments)
        {
            if (armament != null && armament.armamentId == armamentId) return armament;
        }
        return null;
    }

    public WeaponBaseSO[] secondaryWeapons;

    [Header("SkillGauge")]
    public float maxSkillGauge;

    [Header("Skill Battery")]
    [Tooltip("스킬 게이지를 Player 몸 옆에 세로로 쌓이는 배터리 4칸으로 보여 주는 표시. 칸이 다 찰 때 'Charge!'와 작은 소리를 낸다")]
    public PlayerSkillBatteryVisual skillBatteryVisual;

    [Header("Skill Memory")]
    public PlayerSkillMemoryVisual skillMemoryVisual;
    public AnimationSetSO skillStoreAnimation;
    public SoundOptions skillStoreSound;
    public SoundOptions skillStoreCompleteSound;
    [Min(.01f)] public float skillStoreDuration = .2f;

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
    [Header("Armament Slot")]
    [Tooltip("이 자리에 장착하는 무장의 분류(근접·원거리). 다른 분류의 무장은 장착하지 않는다.")]
    public ArmamentCategory armamentCategory;
    [Tooltip("세이브에 장착 기록이 없을 때 쓰는 무장. 비어 있으면 아래 무기 칸을 그대로 쓴다.")]
    public PlayerArmamentSO defaultArmament;

    [Header("Armament Weapons")]
    [Tooltip("기본 무기와 Gear 무기 칸은 실행할 때 장착한 무장의 값으로 채워진다(PlayerWeaponAbility의 실행용 사본).")]
    public WeaponBaseSO normal;

    [Header("Gear Weapons")]
    public WeaponBaseSO gearStage1;
    public WeaponBaseSO gearStage2;
    public WeaponBaseSO gearStage3;

    [Header("Slot Weapon")]
    [Tooltip("보조 무기는 무장과 무관하게 이 자리에 남는다.")]
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
