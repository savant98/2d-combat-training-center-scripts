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
        // 한 번의 3점사는 시작한 방향을 유지한다. 좌우는 보는 방향을 기준으로 한다.
        Vector2 aim = playerController.AbilityManager.AttackAbility.PrimaryFireDirection;

        while (Time.time < t)
        {
            yield return null;

            if (projectilePrefab is StarShot && (!playerController.AbilityManager.AttackAbility.IsStarShotActive
                || playerController.AbilityManager.AttackAbility.AttackPhase != AbilityState.Execute)) yield break;

            intervalTime += Time.deltaTime;

            if (intervalTime >= interval)
            {
                // 반동 적용
                controller.AddRecoil(recoilPower, controller.Facing);

                // 발사체 발사
                if (projectilePrefab is not StarShot || !playerController.AbilityManager.AttackAbility.QueuePrimaryStarShot(this, WithSource(damageInfo), aim))
                {
                    var projectile = (ProjectileBase)WorldEntityPool.Instance.Spawn(projectilePrefab, controller.transform.position, controller.transform.rotation);
                    if (projectile is StarShot star) star.SetFireDirection(aim);
                    projectile.Initialize(controller, WithSource(damageInfo), projectileSpeed, count, platformLayer);
                    projectile.OnHitEnemy += (owner, hitPoint) => OnHitEnemy?.Invoke(owner, hitPoint);
                }

                intervalTime = 0f;
                currentCount++;
            }

            if (playerController.AbilityManager.StateMachine.CurrentState != PlayerState.Attack || currentCount >= count)
            {
                break;
            }
        }
    }
}
