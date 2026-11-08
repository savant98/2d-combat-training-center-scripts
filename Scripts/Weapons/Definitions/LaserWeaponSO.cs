using System;
using System.Collections;
using UnityEngine;

// PlayerLaser(Star 세트 기어 3단계 '파괴 광선', 2026-11-08 재디자인): 1초 충전(예고선) 뒤 큰 레이저를 바로 쏜다.
// 예고선부터 레이저 끝까지의 진행과 판정은 PlayerLaserBeam이 맡고, 이 무기는 준비 시작에 개체를 꺼내고 실행 시작에 발사를 알린다.
// 레이저가 나가는 시간은 실행 시간(executeTime)이다.
[CreateAssetMenu(menuName = "Weapon/Laser")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "LaserWeapon")]
public class LaserWeaponSO : WeaponBaseSO
{
    [Header("Laser Settings")]
    public LayerMask platformLayer;
    public LayerMask damageMask;
    public PlayerLaserBeam beamPrefab;
    public Vector2 spawnOffset = new Vector2(1f, 0f);
    public float laserLength = 55f;
    [Min(0.01f)] public float laserWidth = 2f;          // 레이저 굵기(칸)
    [Min(0.01f)] public float tickInterval = 0.1f;      // 피해 간격(초)

    // 준비 중인 레이저. 준비 시작마다 새로 정해지므로 Play 세션을 넘어 남아 있어도 영향이 없다.
    [NonSerialized] private PlayerLaserBeam activeBeam;

    public override void OnWindupStarted(CharacterController2D controller)
    {
        activeBeam = null;
        if (controller == null || beamPrefab == null || WorldEntityPool.Instance == null)
            return;

        activeBeam = WorldEntityPool.Instance.Spawn(beamPrefab, controller.transform.position, Quaternion.identity);
        activeBeam.BeginCharge(controller, this, WithSource(damageInfo), (owner, hitPoint) => OnHitEnemy?.Invoke(owner, hitPoint));
    }

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        if (activeBeam != null && activeBeam.IsCharging)
            activeBeam.Fire();

        activeBeam = null;
        yield break;
    }
}
