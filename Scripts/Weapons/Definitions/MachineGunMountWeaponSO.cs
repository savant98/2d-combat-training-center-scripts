using System;
using System.Collections;
using UnityEngine;

// Rifle Gear 3단계 '머신건 장착'(2026-11-18, 1차 시안).
// 사용자 결정: "일정 시간 기본 공격이 머신건으로" — 발동하면 mountDuration초 동안 그 자리(Rifle을 든 자리)의 공격 키가 고속 연사 머신건(machineGun)으로 바뀌고,
// 시간이 지나면 원래 Rifle로 돌아온다.
// - 준비(windupTime): 총을 젖혀 장전하는 짧은 장착 동작(Clip Player_MachineGunMount_*). 장전 소리·작은 Flash는 실행 피드백(executeFeedback)이 낸다.
// - 실행 첫 프레임: PlayerWeaponAbility.BeginPrimaryOverride(machineGun, mountDuration)로 기본 공격 자리를 바꾼다. 저장해 둔 기술로 다른 자리에서 써도
//   저장한 자리(Rifle 자리)에 장착된다(BeginPrimaryOverride가 실행 중인 기술의 자리를 쓴다).
// - 남은 시간 표시: 풀 개체 MachineGunMountVisual을 꺼내 총에 회전 총열을 붙이고 남은 시간 막대를 보여 준다. 장착이 끝나면 스스로 사라진다.
//   장착 중에 다시 쓰면(저장 기술 등) 남은 시간만 새로 정해지고 표시는 그대로 이어진다.
// 판정·피해는 없다(연사 피해는 machineGun 무기가 준다).
[CreateAssetMenu(menuName = "Weapon/Machine Gun Mount")]
public class MachineGunMountWeaponSO : WeaponBaseSO
{
    [Header("Machine Gun Mount")]
    [Tooltip("장착하는 동안 기본 공격 자리에 들어갈 연사 무기(MachineGun.asset, StarShot 계열 탄을 쓰는 RangedWeaponSO)")]
    public WeaponBaseSO machineGun;
    [Tooltip("기본 공격이 머신건으로 바뀌어 있는 시간(초)")]
    [Min(0.1f)] public float mountDuration = 6f;
    [Tooltip("총에 붙는 회전 총열과 남은 시간 막대(풀 개체)")]
    public MachineGunMountVisual visualPrefab;
    [Tooltip("장착 시간이 다해 기본 공격으로 돌아갈 때 한 번만 재생한다. 자리 전환·표시 숨김·수동 해제에서는 재생하지 않는다.")]
    public FeedbackDataSO mountEndFeedback;

    // 지금 보이는 표시. 발동마다 다시 확인하므로 Play 세션을 넘어 남아 있어도 영향이 없다.
    [NonSerialized] private MachineGunMountVisual activeVisual;

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        var player = controller as PlayerController;
        var ability = player != null && player.AbilityManager != null ? player.AbilityManager.AttackAbility : null;
        if (ability == null || machineGun == null)
            yield break;

        // 장착할 자리: 실행 중인 자리(저장해 둔 기술이면 저장한 자리)가 지금도 이 기술을 Gear로 가진 무장을 들고 있어야 한다.
        // 저장한 뒤 그 자리의 무장을 바꿨으면(Rifle → Railgun) 이 기술을 가진 다른 자리를 찾고, 없으면 장착하지 않는다
        // (그대로 걸면 Railgun 자리의 기본 공격이 보이지 않는 별 총 머신건으로 바뀐다 - 2026-11-18 독립 검토).
        int target = FindMountSet(ability);
        if (target < 0)
            yield break;
        ability.BeginPrimaryOverride(machineGun, mountDuration, target, mountEndFeedback);
        int setIndex = ability.PrimaryOverrideSetIndex;
        if (setIndex < 0 || ability.PrimaryOverrideWeapon != machineGun)
            yield break;

        // 같은 장착이 이어지는 중이면(남은 시간은 위에서 새로 정해졌다) 표시를 새로 꺼내지 않는다.
        if (activeVisual != null && activeVisual.IsRunningFor(player, machineGun, setIndex))
        {
            activeVisual.Refresh();
            yield break;
        }

        activeVisual = null;
        if (visualPrefab == null || WorldEntityPool.Instance == null)
            yield break;
        activeVisual = WorldEntityPool.Instance.Spawn(visualPrefab, controller.transform.position, Quaternion.identity);
        activeVisual.Begin(player, ability, machineGun, setIndex);
    }

    private int FindMountSet(PlayerWeaponAbility ability)
    {
        int executing = ability.ExecutingSetIndex;
        if (HasThis(ability.GetWeaponSet(executing))) return executing;
        for (int i = 0; i < ability.WeaponSetCount; i++)
            if (HasThis(ability.GetWeaponSet(i))) return i;
        return -1;
    }

    private bool HasThis(WeaponSet set) =>
        set != null && (set.gearStage1 == this || set.gearStage2 == this || set.gearStage3 == this);
}
