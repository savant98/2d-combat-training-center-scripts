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
        // 한 번의 3점사는 시작한 방향을 유지한다. 좌우는 보는 방향을 기준으로 한다.
        Vector2 aim = attack.PrimaryFireDirection;

        while (Time.time < t)
        {
            yield return null;

            if (projectilePrefab is StarShot && (!attack.IsStarShotActive
                || attack.AttackPhase != AbilityState.Execute)) yield break;

            intervalTime += Time.deltaTime;

            if (intervalTime >= interval)
            {
                // 반동 적용

                // 발사체 발사
                Vector2 direction = machineGun ? attack.PrimaryFireDirection : aim;
                if (projectilePrefab is not StarShot || !attack.QueuePrimaryStarShot(this, WithSource(damageInfo, controller), direction))
                {
                    if (!attack.UsePrimaryRound(this)) yield break;
                    controller.AddRecoil(recoilPower, controller.Facing);
                    var projectile = (ProjectileBase)WorldEntityPool.Instance.Spawn(projectilePrefab, controller.transform.position, controller.transform.rotation);
                    if (projectile is StarShot star) star.SetFireDirection(direction);
                    projectile.Initialize(controller, WithSource(damageInfo, controller), projectileSpeed, count, platformLayer);
                    projectile.OnHitEnemy += (owner, hitPoint) => OnHitEnemy?.Invoke(owner, hitPoint);
                }

                intervalTime = 0f;
                currentCount++;
            }

            if (currentCount >= count || (!machineGun && playerController.AbilityManager.StateMachine.CurrentState != PlayerState.Attack))
            {
                break;
            }
        }
    }
}
