using UnityEngine;

// 저장 시점의 무장과 단계를 유지한다. 무기를 바꿔도 기억한 기술은 바뀌지 않는다.
public readonly struct StoredGearSkill
{
    public readonly WeaponBaseSO Weapon;
    public readonly int SetIndex;
    public readonly int Stage;
    public readonly Color Color;
    public StoredGearSkill(WeaponBaseSO weapon, int setIndex, int stage, Color color)
    { Weapon = weapon; SetIndex = setIndex; Stage = stage; Color = color; }
}
