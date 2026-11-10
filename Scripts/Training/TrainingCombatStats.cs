using System.Collections.Generic;

// 훈련 한 판이 패배로 끝난 이유(패배 결과 화면에 표시한다).
public enum TrainingFailReason
{
    None,
    Destroyed,   // 체력이 0이 됨
    HitLimit,    // 피격 한도를 넘어 성적이 미흡이 됨
    TimeLimit,   // 제한 시간을 넘어 성적이 미흡이 됨
}

// 무장 하나가 한 판 동안 낸 피해와 회복.
public sealed class TrainingWeaponRecord
{
    public WeaponBaseSO Weapon { get; }
    public string Name { get; }
    public float Damage { get; private set; }
    public float Healing { get; private set; }

    public TrainingWeaponRecord(WeaponBaseSO weapon, string name)
    {
        Weapon = weapon;
        Name = name;
    }

    internal void AddDamage(float amount) => Damage += amount;
    internal void AddHealing(float amount) => Healing += amount;
}

// 훈련 한 판의 전투 기록(2026-11-10, 사용자 요청 "훈련 종료 시 Panel에 내가 넣은 총 데미지, 받은 피해, 무장별 피해량(힐량)을 같이 표시").
// TrainingRunTracker가 Begin에서 비우고, 판이 진행 중일 때만 채운다. 모두 실제로 적용된 양이다.
// - 가한 피해: 적이 실제로 잃은 체력 + 쉴드(남은 양을 넘는 피해는 뺀다).
// - 받은 피해: 플레이어가 실제로 잃은 체력.  - 회복: 실제로 회복한 체력(최대 체력을 넘는 양은 뺀다).
public sealed class TrainingCombatStats
{
    public const string UnknownSourceName = "출처 미지정";

    private readonly List<TrainingWeaponRecord> weapons = new List<TrainingWeaponRecord>();

    public float DamageDealt { get; private set; }
    public float DamageTaken { get; private set; }
    public float Healing { get; private set; }
    public IReadOnlyList<TrainingWeaponRecord> Weapons => weapons;

    public void Reset()
    {
        DamageDealt = DamageTaken = Healing = 0f;
        weapons.Clear();
    }

    public void RecordDealt(WeaponBaseSO weapon, float amount)
    {
        if (!IsValid(amount)) return;
        DamageDealt += amount;
        Find(weapon).AddDamage(amount);
    }

    public void RecordTaken(float amount)
    {
        if (IsValid(amount)) DamageTaken += amount;
    }

    // 출처 무장이 없는 회복(튜토리얼의 위치 되돌림 등)은 총 회복량에만 더한다.
    public void RecordHealing(WeaponBaseSO weapon, float amount)
    {
        if (!IsValid(amount)) return;
        Healing += amount;
        if (weapon != null) Find(weapon).AddHealing(amount);
    }

    // 기여(피해 + 회복)가 큰 순으로 buffer에 담는다.
    public void CopySorted(List<TrainingWeaponRecord> buffer)
    {
        buffer.Clear();
        buffer.AddRange(weapons);
        buffer.Sort((a, b) => (b.Damage + b.Healing).CompareTo(a.Damage + a.Healing));
    }

    private TrainingWeaponRecord Find(WeaponBaseSO weapon)
    {
        foreach (var record in weapons)
            if (record.Weapon == weapon) return record;

        string name = weapon != null ? weapon.weaponName : UnknownSourceName;
        if (string.IsNullOrWhiteSpace(name)) name = weapon.name;
        var created = new TrainingWeaponRecord(weapon, name);
        weapons.Add(created);
        return created;
    }

    private static bool IsValid(float amount) => amount > 0f && !float.IsNaN(amount) && !float.IsInfinity(amount);
}
