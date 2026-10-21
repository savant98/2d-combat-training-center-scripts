using System;
using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Ranged")]
public class RangedWeapon : WeaponBase
{
    [Header("Weapon Settings")]
    public LayerMask platformLayer;
    public ProjectileBase projectilePrefab;

    [Header("Ranged Settings")]
    public int count = 3; // 발사 횟수
    public float interval = 0.1f; // 연사 간격
    public float projectileSpeed;

    private int currentCount = 0;
    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        currentCount = 0;
        float t = Time.time + executeTime;
        float intervalTime = interval;
        PlayerController playerController = (PlayerController)controller;

        while (Time.time < t)
        {
            yield return null;

            intervalTime += Time.deltaTime;

            if (intervalTime >= interval)
            {
                // 반동 적용
                controller.AddRecoil(recoilPower, controller.Facing);

                // 발사체 발사
                var projectile = (ProjectileBase)WorldEntityPool.Instance.Spawn(projectilePrefab, controller.transform.position, controller.transform.rotation);
                projectile.Initialize(controller, damageInfo, projectileSpeed, count, platformLayer);
                projectile.OnHitEnemy += (controller, hitPoint) =>
                {
                    OnHitEnemy?.Invoke(controller, hitPoint);
                };

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
