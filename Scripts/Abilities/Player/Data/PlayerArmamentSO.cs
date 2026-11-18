using UnityEngine;

/// <summary>
/// 무장의 분류. 같은 분류의 무장끼리만 서로 바꿔 낀다(근접: Hammer·Sword, 원거리: Rifle·Railgun).
/// </summary>
public enum ArmamentCategory
{
    Melee = 0,
    Ranged = 1
}

/// <summary>
/// 무장(Armament): 기본 무기 + Gear 1~3단계 무기 묶음(2026-11-18).
/// Player_Weapon_Data의 각 자리(weaponSets[n], 0 = 근접, 1 = 원거리)에 같은 분류의 무장을 하나씩 장착한다.
/// 보조 무기와 교체 색은 무장에 속하지 않고 자리에 남는다. 비어 있는 Gear 단계(null)는 충전은 되지만 쓸 기술이 없는 단계다.
/// 장착 상태는 세이브(DataManager.equippedArmaments)에 armamentId로 남고, 업그레이드 메뉴에서 바꾼다(PlayerWeaponAbility.TryEquipArmament).
/// </summary>
[CreateAssetMenu(menuName = "Player/Armament")]
public class PlayerArmamentSO : ScriptableObject
{
    [Header("Armament Information")]
    [Tooltip("세이브 키. 장착 기록이 이 값으로 남으므로 바꾸지 않는다(화면 이름은 displayName).")]
    public string armamentId;
    [Tooltip("화면에 보이는 무장 이름(업그레이드 메뉴).")]
    public string displayName;
    public ArmamentCategory category;
    [TextArea(2, 4)]
    public string description;
    [Tooltip("메뉴에 쓰는 무장 아이콘. 비우면 기본 무기의 아이콘을 쓴다.")]
    public Sprite icon;

    [Header("Weapons")]
    public WeaponBaseSO normal;
    public WeaponBaseSO gearStage1;
    public WeaponBaseSO gearStage2;
    public WeaponBaseSO gearStage3;

    public string DisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName
        : normal != null ? normal.DisplayName : armamentId;

    public Sprite Icon => icon != null ? icon : normal != null ? normal.weaponSprite : null;

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
