using System;
using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Ranged")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "RangedWeapon")]
public class RangedWeaponSO : WeaponBaseSO
{
    [Header("Weapon Settings")]
    public LayerMask platformLayer;
    public ProjectileBase projectilePrefab;

    [Header("Ranged Settings")]
    public int count = 3; // 발사 횟수
    public float interval = 0.1f; // 연사 간격
    public float projectileSpeed;

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        int currentCount = 0;
        float t = Time.time + executeTime;
        float intervalTime = interval;
        PlayerController playerController = (PlayerController)controller;
        var attack = playerController.AbilityManager.AttackAbility;
        bool machineGun = this is MachineGunWeaponSO;
        bool starShot = projectilePrefab is StarShot;
        // 한 번의 실행은 시작한 방향을 유지한다. 좌우는 보는 방향을 기준으로 한다. 머신건만 탄마다 지금 조준을 다시 읽는다.
        Vector2 aim = attack.PrimaryFireDirection;

        // 별탄 계열(라이플·작열탄·머신건)의 첫 발은 실행이 시작된 프레임에 첫 yield 전에 넣는다(2026-11-21).
        // PlayerStarShotAim.LateUpdate가 같은 프레임(실행 단계)에 쏘므로 실행 시간이 한 프레임보다 짧아도(작열탄 0.033초) 발사가 빠지지 않는다.
        // 그 밖의 탄(회전 해머 투척)은 예전처럼 다음 프레임에 쏜다.
        if (starShot && count > 0)
        {
            if (!attack.IsStarShotActive || attack.AttackPhase != AbilityState.Execute) yield break;
            if (!Fire(controller, attack, aim)) yield break;
            intervalTime = 0f;
            currentCount++;
        }

        while (currentCount < count && Time.time < t)
        {
            yield return null;

            if (starShot && (!attack.IsStarShotActive
                || attack.AttackPhase != AbilityState.Execute)) yield break;

            intervalTime += Time.deltaTime;

            if (intervalTime >= interval)
            {
                if (!Fire(controller, attack, machineGun ? attack.PrimaryFireDirection : aim)) yield break;
                intervalTime = 0f;
                currentCount++;
            }

            if (!machineGun && playerController.AbilityManager.StateMachine.CurrentState != PlayerState.Attack)
            {
                break;
            }
        }
    }

    // 한 발을 쏜다. 별탄 계열은 PlayerStarShotAim에 맡기고(총신을 조준한 뒤 같은 프레임 LateUpdate에서 탄창을 쓰고 생성한다),
    // 총 모형·총구가 없거나 다른 탄이면 여기서 바로 만든다. 탄창이 비어 쏘지 못했으면 false.
    private bool Fire(CharacterController2D controller, PlayerWeaponAbility attack, Vector2 direction)
    {
        if (projectilePrefab is StarShot && attack.QueuePrimaryStarShot(this, WithSource(damageInfo, controller), direction))
            return true;
        if (!attack.UsePrimaryRound(this)) return false;
        controller.AddRecoil(recoilPower, controller.Facing);
        var projectile = (ProjectileBase)WorldEntityPool.Instance.Spawn(projectilePrefab, controller.transform.position, controller.transform.rotation);
        if (projectile is StarShot star) star.SetFireDirection(direction);
        projectile.Initialize(controller, WithSource(damageInfo, controller), projectileSpeed, count, platformLayer);
        projectile.OnHitEnemy += (owner, hitPoint) => OnHitEnemy?.Invoke(owner, hitPoint);
        return true;
    }
}
