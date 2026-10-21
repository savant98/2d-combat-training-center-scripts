using System;
using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName ="Weapon/Melee")]
public class MeleeWeapon : WeaponBase
{
    public MeleeComboStep[] comboSteps;
    public float comboResetTime = 0.6f;

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        yield return null;
    }
    public void BindMeleeHitboxes(MeleeHitbox meleeHitbox, CharacterController2D controller, int currentComboIndex)
    {
        // 현재 콤보 스텝

        MeleeComboStep step = comboSteps[currentComboIndex];
        meleeHitbox.Initialize(controller, step.damageInfo);
        meleeHitbox.SetHitFeedback(step.hitFeedbackData, (owner, hitPoint) => OnHitEnemy?.Invoke(owner, hitPoint));
    }
}
