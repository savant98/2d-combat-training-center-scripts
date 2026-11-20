using System;
using System.Collections;
using UnityEngine;

public sealed class IncendiaryMountWeaponSO : WeaponBaseSO
{
    [SerializeField] private IncendiaryRifleWeaponSO incendiaryRifle;
    [SerializeField, Min(0.1f)] private float duration = 8f;

    public override string PresentationWeaponName => "StarShot";

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        var attack = WeaponTraitEffects.Attack(controller);
        if (attack == null || incendiaryRifle == null) yield break;
        int slot = attack.ExecutingSetIndex;
        if (attack.GetWeaponSet(slot)?.gearStage2 != this)
        {
            slot = -1;
            for (int i = 0; i < attack.WeaponSetCount; i++)
                if (attack.GetWeaponSet(i)?.gearStage2 == this) { slot = i; break; }
        }
        if (slot < 0) yield break;
        attack.Traits.LoadIncendiary();
        attack.BeginPrimaryOverride(incendiaryRifle, duration, slot);
    }
}
