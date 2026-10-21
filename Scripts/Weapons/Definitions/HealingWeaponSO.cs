using System;
using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Healing")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "HealingWeapon")]
public class HealingWeaponSO : WeaponBaseSO
{
    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        switch (controller.Faction)
        {
            case Faction.Player:
                PlayerController playerController = controller as PlayerController;
                playerController.Health.Heal(damageInfo);
                yield break;
            case Faction.Enemy: 
                EnemyController enemyController = controller as EnemyController;
                enemyController.Health.Heal(damageInfo);
                yield break;
        }
    }
}
