using System;
using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Healing")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "HealingWeapon")]
public class HealingWeaponSO : WeaponBaseSO
{
    // 회복 준비 문양. 준비가 끊기면 스스로 꺼지고, 준비음도 함께 멈춘다(그래서 준비음은 피드백이 아니라 여기에 둔다).
    [Header("Healing Aura")]
    public HealingAura auraPrefab;
    public SoundOptions chargeSound;

    // 준비 중인 문양. 준비 시작마다 새로 정해지므로 Play 세션을 넘어 남아 있어도 영향이 없다.
    [NonSerialized] private HealingAura activeAura;

    public override void OnWindupStarted(CharacterController2D controller)
    {
        activeAura = null;
        if (controller == null || auraPrefab == null || WorldEntityPool.Instance == null)
            return;

        activeAura = WorldEntityPool.Instance.Spawn(auraPrefab, controller.transform.position, Quaternion.identity);
        activeAura.BeginCharge(controller, windupTime, chargeSound);
    }

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        if (activeAura != null && activeAura.IsCharging)
            activeAura.Complete();
        activeAura = null;

        switch (controller.Faction)
        {
            case Faction.Player:
                PlayerController playerController = controller as PlayerController;
                playerController.Health.Heal(WithSource(damageInfo));
                yield break;
            case Faction.Enemy: 
                EnemyController enemyController = controller as EnemyController;
                enemyController.Health.Heal(damageInfo);
                yield break;
        }
    }
}
