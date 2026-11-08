using System;
using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Drone Spawn")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "DroneSpawnWeapon")]
public class DroneSpawnWeaponSO : WeaponBaseSO
{
    [Header("Drone Settings")]
    public DroneBase dronePrefab;
    public float droneLifeTime = 10f;
    public float droneSpeed = 5f;
    public Vector2 spawnOffset = new Vector2(-1.5f, 1f);
    public LayerMask platformLayer;

    // 드론을 만들어 내는 준비 연출. 준비가 끊기면 스스로 꺼지고 준비음도 함께 멈춘다(그래서 준비음은 피드백이 아니라 여기에 둔다).
    [Header("Fabrication")]
    public DroneFabricator fabricatorPrefab;
    public SoundOptions chargeSound;

    // 준비 중인 연출. 준비 시작마다 새로 정해지므로 Play 세션을 넘어 남아 있어도 영향이 없다.
    [NonSerialized] private DroneFabricator activeFabricator;

    public override void OnWindupStarted(CharacterController2D controller)
    {
        activeFabricator = null;
        if (controller == null || fabricatorPrefab == null || WorldEntityPool.Instance == null)
            return;

        Vector3 offset = GetSpawnOffset(controller);
        activeFabricator = WorldEntityPool.Instance.Spawn(fabricatorPrefab, controller.transform.position + offset, Quaternion.identity);
        activeFabricator.BeginCharge(controller, windupTime, offset, chargeSound);
    }

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        if (activeFabricator != null && activeFabricator.IsCharging)
            activeFabricator.Complete();
        activeFabricator = null;

        // 드론 생성 위치 계산
        Vector3 spawnPos = controller.transform.position + GetSpawnOffset(controller);

        var drone = (DroneBase)WorldEntityPool.Instance.Spawn(dronePrefab, spawnPos, Quaternion.identity);
        drone.InitializeDrone(controller, WithSource(damageInfo), droneSpeed, count: 0, droneLifeTime, platformLayer);

        yield break;
    }

    private Vector3 GetSpawnOffset(CharacterController2D controller)
    {
        Vector2 offset = spawnOffset;
        offset.x *= -controller.Facing;
        return offset;
    }
}
