using System;
using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Drone Spawn")]
public class DroneSpawnWeapon : WeaponBase
{
    [Header("Drone Settings")]
    public DroneBase dronePrefab;
    public float droneLifeTime = 10f;
    public float droneSpeed = 5f;
    public Vector2 spawnOffset = new Vector2(-1.5f, 1f);
    public LayerMask platformLayer;

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        // 드론 생성 위치 계산
        Vector2 offset = spawnOffset;
        offset.x *= -controller.Facing;

        Vector3 spawnPos = controller.transform.position + (Vector3)offset;

        var drone = (DroneBase)WorldEntityPool.Instance.Spawn(dronePrefab, spawnPos, Quaternion.identity);
        drone.InitializeDrone(controller, damageInfo, droneSpeed, count: 0, droneLifeTime, platformLayer);

        yield break;
    }
}
