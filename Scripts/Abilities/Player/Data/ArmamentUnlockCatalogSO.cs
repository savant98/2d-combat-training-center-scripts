using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>훈련 필증 해금 항목의 종류. 세이브 키는 Weapon = weaponName, Armament = armamentId다.</summary>
public enum ArmamentUnlockKind
{
    Weapon = 0,
    Armament = 1
}

/// <summary>
/// 무장·기술 해금 목록(2026-11-20 훈련 개편). 사용자 요청: "게임을 시작하면 Hammer와 라이플을 장착한 상태고, 기어스킬은 라이플의 핸드캐논과
/// Hammer의 회전망치만 가지고 있고, 보조무기도 Hammer자리에 회복만 가지고 있도록 해. 그리고 추후에 UpgradeMenu에서 훈련필증으로 해금하도록".
/// - 판정 단위는 무기(weaponName)와 무장(armamentId)이다. 자리(weaponSets 번호)와 무관하므로 보조 무기를 어느 자리에 끼워도 같은 판정이 성립한다.
/// - 판정 규칙(IsWeaponUnlocked): ① 그 무기의 항목이 있으면 그 항목의 상태 ② 없으면 그 무기를 담은 무장 항목 중 하나라도 해금이면 해금
///   ③ 어느 항목도 다루지 않는 무기는 해금(예: 머신건 장착이 잠시 바꿔 끼우는 기본 공격 MachineGun).
/// - 기본 해금 항목(unlockedByDefault)은 세이브에 남기지 않는다. 구매한 항목만 DataManager의 certificateUnlocked* 목록에 남는다.
/// - Resources/Player/ArmamentUnlockCatalog 에셋 하나를 쓴다. 에셋이 없으면 ArmamentUnlocks가 모두 해금(옛 동작)으로 본다.
/// tools/Unity/ArmamentUnlockSetup.cs가 에셋을 만든다.
/// </summary>
[CreateAssetMenu(menuName = "Player/Armament Unlock Catalog", fileName = "ArmamentUnlockCatalog")]
public sealed class ArmamentUnlockCatalogSO : ScriptableObject
{
    public const string ResourcePath = "Player/ArmamentUnlockCatalog";

    [Serializable]
    public sealed class Entry
    {
        public ArmamentUnlockKind kind;
        [Tooltip("kind가 Weapon일 때의 대상 기술(무기 SO)")]
        public WeaponBaseSO weapon;
        [Tooltip("kind가 Armament일 때의 대상 무장. 사면 그 무장의 기본 공격과 Gear 기술이 함께 열린다")]
        public PlayerArmamentSO armament;
        [Tooltip("훈련 필증 수")]
        [Min(0)] public int price = 1;
        [Tooltip("처음부터 쓸 수 있다(구매 대상이 아니다)")]
        public bool unlockedByDefault;
        [Tooltip("화면 이름. 비우면 무기 이름 / 무장 이름 + ' 무장'")]
        public string title;
        [Tooltip("상세 해금 줄·카드 설명. 비우면 기본 문구")]
        [TextArea(2, 4)] public string description;

        /// <summary>세이브 키(Weapon = weaponName, Armament = armamentId). 대상이 없으면 빈 문자열.</summary>
        public string Key => kind == ArmamentUnlockKind.Weapon
            ? (weapon != null ? weapon.weaponName ?? string.Empty : string.Empty)
            : (armament != null ? armament.armamentId ?? string.Empty : string.Empty);

        public string Title
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(title)) return title;
                if (kind == ArmamentUnlockKind.Weapon) return weapon != null ? weapon.DisplayName : string.Empty;
                return armament != null ? armament.DisplayName + " 무장" : string.Empty;
            }
        }

        public Sprite Icon => kind == ArmamentUnlockKind.Weapon
            ? (weapon != null ? weapon.weaponSprite : null)
            : (armament != null ? armament.Icon : null);
    }

    public Entry[] entries = Array.Empty<Entry>();

    [Header("잠금 표시")]
    [Tooltip("HUD·메뉴가 함께 쓰는 자물쇠 그림(ArmamentLockGlyph.png)")]
    public Sprite lockGlyph;
    [Tooltip("잠긴 칸의 회색 톱니 원 재질(ArmamentLockedRing.mat, GlowMultiply)")]
    public Material lockedRingMaterial;

    private static ArmamentUnlockCatalogSO cached;
    private static bool missingReported;

    // 도메인 리로드를 끈 편집기 실행에서도 지난 Play의 값이 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cached = null;
        missingReported = false;
    }

    /// <summary>Resources의 해금 목록. 없으면 null(경고는 한 번만).</summary>
    public static ArmamentUnlockCatalogSO Load()
    {
        if (cached != null) return cached;
        cached = Resources.Load<ArmamentUnlockCatalogSO>(ResourcePath);
        if (cached == null && !missingReported)
        {
            missingReported = true;
            Debug.LogWarning($"[ArmamentUnlockCatalog] Resources/{ResourcePath} 에셋이 없습니다. 모든 무장·기술을 해금한 것으로 봅니다(tools/Unity/ArmamentUnlockSetup.Run 필요).");
        }
        return cached;
    }

    /// <summary>그 무기의 무기 항목(없으면 null). 비교는 참조가 아니라 weaponName이다.</summary>
    public Entry Find(WeaponBaseSO weapon)
    {
        if (weapon == null || string.IsNullOrEmpty(weapon.weaponName) || entries == null) return null;
        foreach (var entry in entries)
            if (entry != null && entry.kind == ArmamentUnlockKind.Weapon && entry.Key == weapon.weaponName) return entry;
        return null;
    }

    /// <summary>그 무장의 무장 항목(없으면 null). 비교는 armamentId다.</summary>
    public Entry Find(PlayerArmamentSO armament)
    {
        if (armament == null || string.IsNullOrEmpty(armament.armamentId) || entries == null) return null;
        foreach (var entry in entries)
            if (entry != null && entry.kind == ArmamentUnlockKind.Armament && entry.Key == armament.armamentId) return entry;
        return null;
    }

    /// <summary>항목이 해금됐는가. purchased는 (종류, 키)로 구매 기록을 묻는다(세이브 조회를 밖에서 넣는다).</summary>
    public static bool IsUnlocked(Entry entry, Func<ArmamentUnlockKind, string, bool> purchased)
    {
        if (entry == null) return false;
        if (entry.unlockedByDefault) return true;
        string key = entry.Key;
        return !string.IsNullOrEmpty(key) && purchased != null && purchased(entry.kind, key);
    }

    /// <summary>기술(무기)을 쓸 수 있는가. 규칙은 클래스 설명의 ①②③.</summary>
    public bool IsWeaponUnlocked(WeaponBaseSO weapon, Func<ArmamentUnlockKind, string, bool> purchased)
    {
        if (weapon == null) return false;
        var own = Find(weapon);
        if (own != null) return IsUnlocked(own, purchased);
        bool covered = false;
        foreach (var entry in CoveringArmaments(weapon))
        {
            covered = true;
            if (IsUnlocked(entry, purchased)) return true;
        }
        return !covered;
    }

    /// <summary>무장을 장착할 수 있는가. 목록에 없는 무장은 해금으로 본다.</summary>
    public bool IsArmamentUnlocked(PlayerArmamentSO armament, Func<ArmamentUnlockKind, string, bool> purchased)
    {
        if (armament == null) return false;
        var own = Find(armament);
        return own == null || IsUnlocked(own, purchased);
    }

    /// <summary>그 무기를 담은(기본 공격·Gear 1~3) 무장 항목들.</summary>
    public IEnumerable<Entry> CoveringArmaments(WeaponBaseSO weapon)
    {
        if (weapon == null || string.IsNullOrEmpty(weapon.weaponName) || entries == null) yield break;
        foreach (var entry in entries)
        {
            if (entry == null || entry.kind != ArmamentUnlockKind.Armament || entry.armament == null) continue;
            if (Holds(entry.armament, weapon.weaponName)) yield return entry;
        }
    }

    /// <summary>그 무기를 담은 무장 항목 하나(해금된 것 우선, 없으면 첫 항목). 없으면 null.</summary>
    public Entry FindCoveringArmament(WeaponBaseSO weapon, Func<ArmamentUnlockKind, string, bool> purchased)
    {
        Entry first = null;
        foreach (var entry in CoveringArmaments(weapon))
        {
            if (IsUnlocked(entry, purchased)) return entry;
            first ??= entry;
        }
        return first;
    }

    /// <summary>구매 대상(기본 해금이 아닌 항목)의 가격 합. 기대값 7(각개 전투 전까지 받는 필증 수).</summary>
    public int TotalPurchasablePrice
    {
        get
        {
            int total = 0;
            if (entries != null)
                foreach (var entry in entries)
                    if (entry != null && !entry.unlockedByDefault) total += Mathf.Max(0, entry.price);
            return total;
        }
    }

    private static bool Holds(PlayerArmamentSO armament, string weaponName)
    {
        for (int stage = 0; stage <= 3; stage++)
        {
            var weapon = stage == 0 ? armament.normal : armament.GetGearWeapon(stage);
            if (weapon != null && weapon.weaponName == weaponName) return true;
        }
        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (entries == null) return;
        var keys = new HashSet<string>();
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry == null) continue;
            string label = $"[{name}] {i}번 항목";
            if (entry.kind == ArmamentUnlockKind.Weapon && entry.weapon == null) Debug.LogWarning(label + ": 무기 항목인데 무기가 비어 있습니다.", this);
            if (entry.kind == ArmamentUnlockKind.Armament && entry.armament == null) Debug.LogWarning(label + ": 무장 항목인데 무장이 비어 있습니다.", this);
            if (entry.kind == ArmamentUnlockKind.Weapon && entry.armament != null) Debug.LogWarning(label + ": 무기 항목에 무장이 들어 있습니다(무시됨).", this);
            if (entry.kind == ArmamentUnlockKind.Armament && entry.weapon != null) Debug.LogWarning(label + ": 무장 항목에 무기가 들어 있습니다(무시됨).", this);
            if (entry.price < 0) Debug.LogWarning(label + ": 가격이 음수입니다.", this);
            string key = entry.kind + ":" + entry.Key;
            if (!string.IsNullOrEmpty(entry.Key) && !keys.Add(key)) Debug.LogWarning(label + ": 키가 중복됩니다(" + key + ").", this);
            if (entry.kind == ArmamentUnlockKind.Weapon && entry.weapon != null)
                foreach (var other in entries)
                    if (other != null && other.kind == ArmamentUnlockKind.Armament && other.armament != null && other.armament.normal == entry.weapon)
                        Debug.LogWarning(label + ": 무장의 기본 공격(" + entry.weapon.weaponName + ")을 무기 항목으로 두면 무장 해금과 따로 놉니다.", this);
        }
    }
#endif
}
