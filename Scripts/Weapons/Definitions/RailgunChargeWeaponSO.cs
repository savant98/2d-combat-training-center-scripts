using System;
using System.Collections;

public sealed class RailgunChargeWeaponSO : WeaponBaseSO
{
    public override string PresentationWeaponName => "Railgun";

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        var attack = WeaponTraitEffects.Attack(controller);
        if (attack != null)
            attack.Traits.ChargeRail();
        yield break;
    }
}
