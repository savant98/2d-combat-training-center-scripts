using System;
using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Multiple Shot")]
public class MultipleShotWeapon : WeaponBase
{
    [Header("Weapon Settings")]
    public LayerMask platformLayer;
    public ProjectileBase projectilePrefab;

    [Header("Multiple Shot Settings")]
    [SerializeField, Min(1)] private int pelletsPerShot = 5;
    [SerializeField, Min(0f)] private float spreadAngle = 30f;
    [SerializeField, Min(0f)] private float projectileSpeed = 12f;

    [Header("Repeat Settings")]
    [SerializeField, Min(1)] private int shotCount = 1;
    [SerializeField, Min(0.01f)] private float shotInterval = 0.1f;

    [Header("Spawn")]
    [SerializeField] private Vector2 spawnOffset = new Vector2(0.4f, 0f);

    public override IEnumerator WeaponUseSequence(
        CharacterController2D controller,
        Action forceRecover = null)
    {
        PlayerController playerController = controller as PlayerController;

        int firedCount = 0;
        float intervalTimer = shotInterval;

        float endTime = Time.time + executeTime;

        while (Time.time < endTime)
        {
            yield return null;

            if (playerController != null &&
                playerController.AbilityManager.StateMachine.CurrentState != PlayerState.Attack)
            {
                break;
            }

            intervalTimer += Time.deltaTime;

            if (firedCount >= shotCount)
                continue;

            if (intervalTimer < shotInterval)
                continue;

            FireSpread(controller);

            firedCount++;
            intervalTimer = 0f;
        }
    }

    private void FireSpread(CharacterController2D controller)
    {
        controller.AddRecoil(recoilPower, controller.Facing);

        int facing = controller.Facing >= 0 ? 1 : -1;

        Vector2 spawnPos = GetSpawnPosition(controller, facing);

        if (pelletsPerShot <= 1)
        {
            FireProjectile(controller, spawnPos, Vector2.right * facing);
            return;
        }

        float startAngle = -spreadAngle * 0.5f;
        float angleStep = spreadAngle / (pelletsPerShot - 1);

        for (int i = 0; i < pelletsPerShot; i++)
        {
            float angle = startAngle + angleStep * i;

            Vector2 dir = Quaternion.Euler(0f, 0f, angle)
                * Vector2.right;

            dir.x *= facing;

            FireProjectile(controller, spawnPos, dir.normalized);
        }
    }

    private Vector2 GetSpawnPosition(CharacterController2D controller, int facing)
    {
        Vector2 offset = spawnOffset;
        offset.x *= facing;

        return (Vector2)controller.transform.position + offset;
    }

    private void FireProjectile(
        CharacterController2D controller,
        Vector2 spawnPos,
        Vector2 direction)
    {
        float zAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Quaternion rot = Quaternion.Euler(0f, 0f, zAngle);

        ProjectileBase projectile = WorldEntityPool.Instance.Spawn(
            projectilePrefab,
            spawnPos,
            rot);

        if (projectile is StarShotgun starShot)
            starShot.SetFireDirection(direction);

        projectile.Initialize(
            controller,
            damageInfo,
            projectileSpeed,
            pelletsPerShot,
            platformLayer);

        projectile.OnHitEnemy += (hitController, hitPoint) =>
        {
            OnHitEnemy?.Invoke(hitController, hitPoint);
        };
    }
}