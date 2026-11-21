using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 무장 특성 연출 검수 창(2026-11-21 무장 특성 UI·효과음 개편). Play 중에만 동작한다.
/// 해머 방어막(충전·전개·피격·반격 100%·파괴), 라이플 탄창(소진·장전), 작열탄·기관총 장착, 가까운 적 화상 스택을 버튼 하나로 만들어
/// 연출·소리를 바로 확인하게 한다. 게임 규칙 코드는 바꾸지 않고, 게임이 쓰는 공개 API(PlayerWeaponTraits·TryEquipArmament 등)를 부른다.
/// 스킬 게이지 채우기만 비공개 값을 리플렉션으로 바꾼다(검수 전용).
/// </summary>
internal sealed class WeaponTraitReviewWindow : EditorWindow
{
    private const string MenuPath = "Tools/Armament/무장 특성 검수 창";
    private int burnStacks = 3;
    private Vector2 scroll;

    [MenuItem(MenuPath)]
    private static void Open() => GetWindow<WeaponTraitReviewWindow>("무장 특성 검수");

    private void OnInspectorUpdate() => Repaint();

    private void OnGUI()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Play 중에만 동작합니다. MainScene에서 Play를 누른 뒤 사용하세요.", MessageType.Info);
            return;
        }
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        var attack = player != null ? player.AbilityManager?.AttackAbility : null;
        var traits = attack != null ? attack.Traits : null;
        if (traits == null)
        {
            EditorGUILayout.HelpBox("Player를 찾지 못했습니다.", MessageType.Warning);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField($"무장 {attack.ActiveArmamentId} · 방어막 {traits.Barrier:F0}/{PlayerWeaponTraits.BarrierCapacity:F0} · 충전 {traits.BarrierCharge:F0}"
            + $" · 탄 {traits.RifleAmmo}/{PlayerWeaponTraits.MagazineCapacity}{(traits.IsReloading ? $" (장전 {traits.ReloadRemaining:F1}초)" : "")}"
            + $" · 작열탄 {traits.IncendiaryAmmo}", EditorStyles.wordWrappedLabel);

        Header("준비");
        Row(("해금 전부 켜기", () => Debug.Log("[WeaponTraitReview] " + ArmamentUnlocks.DebugSetAll(true))),
            ("스킬 게이지 가득", () => FillGauge(attack)));
        Row(("근접 자리 = 해머", () => Equip(attack, 0, "Hammer")),
            ("원거리 자리 = 라이플", () => Equip(attack, 1, "Rifle")));

        Header("해머 방어막");
        Row(("충전 +20", () => traits.AddBarrierCharge(20f)),
            ("가득 채워 전개", () => traits.AddBarrierCharge(PlayerWeaponTraits.BarrierCapacity)));
        Row(("앞에서 피격(피해 20)", () => traits.Absorb(20f, player.transform.position + Vector3.right * player.Facing)),
            ("뒤에서 피격(피해 20)", () => traits.Absorb(20f, player.transform.position - Vector3.right * player.Facing)));
        Row(("반격 성공(100% 채움)", () => traits.FillBarrier()),
            ("파괴(큰 피해)", () => traits.Absorb(1000f, player.transform.position + Vector3.up)));

        Header("라이플");
        Row(("탄 1발만 남기기", () => SetAmmo(traits, 1)),
            ("탄창 가득", () => SetAmmo(traits, PlayerWeaponTraits.MagazineCapacity)));
        Row(("작열탄 장착(8초·6발)", () => Mount(attack, traits, true)),
            ("기관총 장착", () => Mount(attack, traits, false)));

        Header("화상(가장 가까운 적)");
        burnStacks = EditorGUILayout.IntSlider("스택", burnStacks, 1, EnemyBurn.Capacity);
        Row(("화상 부여", () => Burn(player, burnStacks)),
            ("화상 지우기", () => { var e = Nearest(player); if (e != null && e.TryGetComponent<EnemyBurn>(out var b)) b.Clear(); }));
        EditorGUILayout.EndScrollView();
    }

    private static void Header(string text)
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
    }

    private static void Row(params (string label, System.Action action)[] buttons)
    {
        EditorGUILayout.BeginHorizontal();
        foreach (var (label, action) in buttons)
            if (GUILayout.Button(label)) action();
        EditorGUILayout.EndHorizontal();
    }

    private static void FillGauge(PlayerWeaponAbility attack)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(PlayerWeaponAbility);
        float max = (float)type.GetField("maxSkillGauge", flags).GetValue(attack);
        type.GetField("skillGauge", flags).SetValue(attack, max);
        type.GetMethod("PublishSkillGauge", flags).Invoke(attack, new object[] { false });
    }

    private static void Equip(PlayerWeaponAbility attack, int set, string armamentId)
    {
        var armament = AssetDatabase.FindAssets("t:PlayerArmamentSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<PlayerArmamentSO>(AssetDatabase.GUIDToAssetPath(g)))
            .FirstOrDefault(a => a != null && a.armamentId == armamentId);
        bool ok = armament != null && attack.TryEquipArmament(set, armament);
        Debug.Log($"[WeaponTraitReview] 자리 {set} ← {armamentId}: {(ok ? "장착" : "실패(해금·자리 확인)")}");
    }

    private static void SetAmmo(PlayerWeaponTraits traits, int ammo)
    {
        var property = typeof(PlayerWeaponTraits).GetProperty(nameof(PlayerWeaponTraits.RifleAmmo));
        property.GetSetMethod(true).Invoke(traits, new object[] { Mathf.Clamp(ammo, 0, PlayerWeaponTraits.MagazineCapacity) });
    }

    private static void Mount(PlayerWeaponAbility attack, PlayerWeaponTraits traits, bool incendiary)
    {
        int rifleSet = -1;
        for (int i = 0; i < 2; i++)
            if (attack.GetEquippedArmament(i)?.armamentId == "Rifle") rifleSet = i;
        if (rifleSet < 0) { Debug.Log("[WeaponTraitReview] 라이플을 장착한 자리가 없습니다."); return; }
        var rifle = attack.GetEquippedArmament(rifleSet);
        var gear = incendiary ? rifle.gearStage2 : rifle.gearStage3;
        if (incendiary && gear is IncendiaryMountWeaponSO mount)
        {
            var weapon = typeof(IncendiaryMountWeaponSO).GetField("incendiaryRifle", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mount) as WeaponBaseSO;
            traits.LoadIncendiary();
            attack.BeginPrimaryOverride(weapon, 8f, rifleSet);
        }
        else if (!incendiary && gear is MachineGunMountWeaponSO machine)
        {
            var weapon = machine.machineGun;
            if (weapon != null) attack.BeginPrimaryOverride(weapon, 12f, rifleSet);
            else Debug.Log("[WeaponTraitReview] 기관총 무기를 찾지 못했습니다(Gear 3을 직접 쓰세요).");
        }
    }

    private static EnemyHealth Nearest(PlayerController player) =>
        Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None)
            .Where(e => e.isActiveAndEnabled && e.currentHP > 0f)
            .OrderBy(e => (e.transform.position - player.transform.position).sqrMagnitude)
            .FirstOrDefault();

    private static void Burn(PlayerController player, int stacks)
    {
        var enemy = Nearest(player);
        if (enemy == null) { Debug.Log("[WeaponTraitReview] 살아 있는 적이 없습니다(자율 훈련장 더미 등)."); return; }
        for (int i = 0; i < stacks; i++) EnemyBurn.Apply(enemy);
        Debug.Log($"[WeaponTraitReview] {enemy.name} 화상 {enemy.GetComponent<EnemyBurn>()?.Stacks}스택");
    }
}
