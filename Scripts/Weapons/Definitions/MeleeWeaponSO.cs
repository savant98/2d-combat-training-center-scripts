using System;
using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName ="Weapon/Melee")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "MeleeWeapon")]
public class MeleeWeaponSO : WeaponBaseSO
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
        meleeHitbox.Initialize(controller, WithSource(step.damageInfo));
        meleeHitbox.SetHitFeedback(step.hitFeedbackData, (owner, hitPoint) => OnHitEnemy?.Invoke(owner, hitPoint));
    }
}
