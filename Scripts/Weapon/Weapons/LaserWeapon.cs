using System;
using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Laser")]
public class LaserWeapon : WeaponBase
{
    [Header("Laser Settings")]
    public LayerMask platformLayer;
    public LayerMask damageMask;
    public LaserBeam laserPrefab;
    public Vector2 spawnOffset = Vector2.zero;
    public float laserLength = 55f;
    public float laserWidth = 1f;

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        // 반동 적용
        controller.AddRecoil(recoilPower, controller.Facing);

        Vector2 origin = new Vector2(controller.transform.position.x + (spawnOffset.x * controller.Facing), controller.transform.position.y + spawnOffset.y);

        LaserBeam laser = WorldEntityPool.Instance.Spawn(laserPrefab, controller.transform.position, Quaternion.identity);

        laser.Fire(
            damageInfo,
            origin,
            Vector2.right * controller.Facing,
            laserLength,
            executeTime,
            platformLayer,
            damageMask,
            laserWidth,
            controller);

        // 발사체 발사
        laser.OnHitEnemy += (controller, hitPoint) =>
        {
            OnHitEnemy?.Invoke(controller, hitPoint);
        };

        float t = Time.time + executeTime;
        PlayerController playerController = (PlayerController)controller;
        while (Time.time < t)
        {
            yield return null;

            if (playerController.AbilityManager.StateMachine.CurrentState != PlayerState.Attack)
            {
                laser.ForceReturnToPool();
                break;
            }
        }
    }
}
