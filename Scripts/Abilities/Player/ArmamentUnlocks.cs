using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>해금 상태가 어디서 왔는가(상세 페이지 상태 글자).</summary>
public enum UnlockSource
{
    Unmanaged = 0,   // 해금 목록이 다루지 않는 기술(늘 사용 가능)
    Default = 1,     // 처음부터 쓸 수 있는 항목
    Purchased = 2,   // 훈련 필증으로 산 항목
    ViaArmament = 3, // 그 기술을 담은 무장을 해금해 함께 열림
    Locked = 4       // 아직 잠김
}

/// <summary>
/// 무장·기술 해금 판정과 훈련 필증 구매(2026-11-20 훈련 개편). 목록은 ArmamentUnlockCatalogSO, 구매 기록은 DataManager가 맡는다.
/// - 판정은 Play 중이고 세이브 슬롯을 고른 뒤에만 구매 기록을 읽는다. Edit Mode에서는 DataManager를 만들지 않도록 기본 해금만 본다.
/// - 해금 기록이 바뀌면(구매·테스트 진입점) Changed를 낸다. PlayerWeaponAbility가 실행용 사본을 다시 만들고 HUD·메뉴에 알린다.
/// - 목록 에셋이 없으면 모든 무장·기술을 해금으로 본다(옛 동작).
/// </summary>
public static class ArmamentUnlocks
{
    public const string ShortPriceFormat = "필증 {0}";

    /// <summary>해금 기록이 바뀌었다(구매·테스트 진입점).</summary>
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Changed = null;

    public static ArmamentUnlockCatalogSO Catalog => ArmamentUnlockCatalogSO.Load();

    public static string PriceLabel(int price) => string.Format(ShortPriceFormat, price);
    public static string ShortageMessage => TrainingCatalogSO.CertificateName + " 수가 부족합니다";

    // 구매 기록 조회. Play 중이 아니면 DataManager를 만들지 않고 기록 없음으로 본다.
    private static bool Purchased(ArmamentUnlockKind kind, string key)
    {
        if (!Application.isPlaying || string.IsNullOrEmpty(key)) return false;
        var data = DataManager.Instance;
        return data != null && data.HasCertificateUnlock(kind, key);
    }

    private static readonly Func<ArmamentUnlockKind, string, bool> PurchasedLookup = Purchased;

    public static bool IsWeaponUnlocked(WeaponBaseSO weapon)
    {
        if (weapon == null) return false;
        var catalog = Catalog;
        return catalog == null || catalog.IsWeaponUnlocked(weapon, PurchasedLookup);
    }

    public static bool IsArmamentUnlocked(PlayerArmamentSO armament)
    {
        if (armament == null) return false;
        var catalog = Catalog;
        return catalog == null || catalog.IsArmamentUnlocked(armament, PurchasedLookup);
    }

    /// <summary>잠겨 있고 이 무기의 항목으로 바로 살 수 있으면 그 항목. 아니면 null(무장에 묶인 기술·이미 해금·목록 밖).</summary>
    public static ArmamentUnlockCatalogSO.Entry PurchasableEntry(WeaponBaseSO weapon)
    {
        var catalog = Catalog;
        if (catalog == null || weapon == null) return null;
        var entry = catalog.Find(weapon);
        return entry != null && !ArmamentUnlockCatalogSO.IsUnlocked(entry, PurchasedLookup) && !string.IsNullOrEmpty(entry.Key) ? entry : null;
    }

    /// <summary>잠겨 있고 살 수 있는 무장 항목. 아니면 null.</summary>
    public static ArmamentUnlockCatalogSO.Entry PurchasableEntry(PlayerArmamentSO armament)
    {
        var catalog = Catalog;
        if (catalog == null || armament == null) return null;
        var entry = catalog.Find(armament);
        return entry != null && !ArmamentUnlockCatalogSO.IsUnlocked(entry, PurchasedLookup) && !string.IsNullOrEmpty(entry.Key) ? entry : null;
    }

    /// <summary>이 기술을 담은 무장(해금된 것 우선). 없으면 null. 상세 페이지의 "무장을 해금하면 함께 열립니다" 안내에 쓴다.</summary>
    public static PlayerArmamentSO CoveringArmament(WeaponBaseSO weapon)
    {
        var catalog = Catalog;
        var entry = catalog != null ? catalog.FindCoveringArmament(weapon, PurchasedLookup) : null;
        return entry != null ? entry.armament : null;
    }

    public static UnlockSource SourceOf(WeaponBaseSO weapon)
    {
        var catalog = Catalog;
        if (weapon == null || catalog == null) return UnlockSource.Unmanaged;
        var own = catalog.Find(weapon);
        if (own != null)
        {
            if (own.unlockedByDefault) return UnlockSource.Default;
            return ArmamentUnlockCatalogSO.IsUnlocked(own, PurchasedLookup) ? UnlockSource.Purchased : UnlockSource.Locked;
        }
        bool covered = false, viaPurchase = false;
        foreach (var entry in catalog.CoveringArmaments(weapon))
        {
            covered = true;
            if (entry.unlockedByDefault) return UnlockSource.Default;
            if (ArmamentUnlockCatalogSO.IsUnlocked(entry, PurchasedLookup)) viaPurchase = true;
        }
        if (!covered) return UnlockSource.Unmanaged;
        return viaPurchase ? UnlockSource.ViaArmament : UnlockSource.Locked;
    }

    /// <summary>
    /// 항목을 훈련 필증으로 산다. 필증 차감과 해금 기록은 저장 한 번으로 함께 남는다(DataManager.TryPurchaseCertificateUnlock).
    /// 성공하면 Changed를 낸다. 실패하면 warning에 이유를 넣고 아무것도 바꾸지 않는다.
    /// </summary>
    public static bool TryPurchase(ArmamentUnlockCatalogSO.Entry entry, out string warning)
    {
        warning = null;
        if (entry == null || string.IsNullOrEmpty(entry.Key)) { warning = "해금 정보를 찾을 수 없습니다."; return false; }
        if (ArmamentUnlockCatalogSO.IsUnlocked(entry, PurchasedLookup)) { warning = "이미 사용할 수 있습니다."; return false; }
        var data = Application.isPlaying ? DataManager.Instance : null;
        if (data == null || !data.HasSelectedSlot) { warning = "세이브를 고른 뒤에 해금할 수 있습니다."; return false; }
        if (data.TrainingCertificates < entry.price) { warning = ShortageMessage; return false; }
        if (!data.TryPurchaseCertificateUnlock(entry.kind, entry.Key, entry.price)) { warning = ShortageMessage; return false; }
        NotifyChanged();
        return true;
    }

    /// <summary>해금 기록이 바뀌었음을 알린다. 구독자 하나의 예외가 나머지를 막지 않게 한다.</summary>
    public static void NotifyChanged()
    {
        var handlers = Changed;
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception error) { Debug.LogException(error); }
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// 테스트 진입점: 구매 대상 전부를 해금(true)하거나 해금 기록을 모두 지운다(false). 필증은 그대로 둔다.
    /// Play 중이고 세이브 슬롯을 고른 뒤에만 동작하며, 결과를 문장으로 돌려준다(편집기 메뉴 Tools/Armament/…, 검증 도구가 부른다).
    /// </summary>
    public static string DebugSetAll(bool unlocked)
    {
        if (!Application.isPlaying) return "해금 테스트 진입점: Play 중에만 쓸 수 있습니다.";
        var data = DataManager.Instance;
        if (data == null || !data.HasSelectedSlot) return "해금 테스트 진입점: 세이브 슬롯을 고른 뒤에 쓸 수 있습니다.";
        var catalog = Catalog;
        if (catalog == null) return "해금 테스트 진입점: 해금 목록 에셋이 없습니다(모두 해금으로 동작 중).";
        var weapons = new List<string>();
        var armaments = new List<string>();
        if (unlocked && catalog.entries != null)
            foreach (var entry in catalog.entries)
            {
                if (entry == null || entry.unlockedByDefault || string.IsNullOrEmpty(entry.Key)) continue;
                var list = entry.kind == ArmamentUnlockKind.Weapon ? weapons : armaments;
                if (!list.Contains(entry.Key)) list.Add(entry.Key);
            }
        data.SetCertificateUnlocks(weapons, armaments);
        NotifyChanged();
        return unlocked
            ? $"해금 {weapons.Count + armaments.Count}개 기록(무기 {weapons.Count}, 무장 {armaments.Count}), 보유 필증 {data.TrainingCertificates}"
            : $"해금 기록을 모두 지움(기본 해금만 남음), 보유 필증 {data.TrainingCertificates}";
    }
#endif
}
