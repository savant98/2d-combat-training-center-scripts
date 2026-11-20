using System;
using System.Collections;
using UnityEngine;

public sealed class SwordWaveWeaponSO : WeaponBaseSO
{
    [SerializeField] private LayerMask damageMask;

    public override string PresentationWeaponName => "Sword";

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        if (controller is not PlayerController player || player.Motor == null) yield break;
        var manager = player.AbilityManager;
        var traits = manager.AttackAbility.Traits;
        bool empowered = traits.Resolve >= 2;
        if (empowered) traits.ChangeResolve(-2);

        const float ascentTime = 0.2f;
        float originY = player.transform.position.y;
        float rise = manager.JumpAbility.MaxJumpHeight;
        int facing = player.Facing;
        var wait = new WaitForFixedUpdate();
        player.GravityOn = false;
        player.IgnoreExternalForces = true;
        for (float elapsed = 0f; elapsed < ascentTime - 0.001f; elapsed += Time.fixedDeltaTime)
        {
            float before = player.transform.position.y;
            float remaining = originY + rise - before;
            if (remaining <= 0.001f) break;
            player.Velocity = Vector2.up * Mathf.Min(rise / ascentTime, remaining / Time.fixedDeltaTime);
            yield return wait;
            if (player.transform.position.y <= before + 0.001f) break;
        }
        player.Velocity = Vector2.zero;
        player.GravityOn = true;
        player.IgnoreExternalForces = false;

        Vector2 origin = (Vector2)player.transform.position + Vector2.right * (facing * 0.5f);
        Vector2 direction = new Vector2(facing, -0.75f).normalized;
        RaycastMotor2D.InvalidateAttackQuerySync();
        var wall = player.Motor.RaycastEnvironment(origin, direction, 16f);
        Vector2 end = wall.collider != null ? wall.point : origin + direction * 16f;
        WeaponTraitFeedbackSO.Play(empowered ? WeaponTraitCue.SwordWaveEmpowered : WeaponTraitCue.SwordWave,
            origin, direction);

        var info = WithSource(damageInfo, player);
        info.knockbackOrigin = origin;
        info.knockbackFacing = facing;
        new GameObject("SwordWave").AddComponent<SwordWave>().Begin(
            player, origin, end, direction, damageMask, info, empowered, OnHitEnemy);
        forceRecover?.Invoke();
    }
}
