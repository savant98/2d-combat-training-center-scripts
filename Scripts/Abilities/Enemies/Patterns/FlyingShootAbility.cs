using UnityEngine;

// 거리를 유지하던 비행 적이 공중에 멈춰 플레이어에게 탄을 쏘는 사격 패턴이다(Drone).
// - Windup: 가속도 제한 안에서 감속해 멈추고, 플레이어를 바라보며 충전한다. 같은 무리와의 분리는 계속 적용한다.
// - Execute: 시작할 때 첫 발을 쏘고 shotInterval마다 남은 탄을 쏜다. 쏠 때마다 플레이어 반대쪽 반동을 외부 힘으로 받는다.
// - Recover: 반동이 줄어드는 동안 떠 있다가 다음 패턴(거리 유지)을 예약한다.
// 거리 유지(FlyingPatrolAbility)는 유지 시간이 지난 뒤 CanBegin이 true일 때만 이 패턴을 예약한다(사거리·조준 각도·사격선).
// 이 패턴은 거리 유지가 예약할 때만 시작하므로(allowRandomSelection 끔) 같은 자리에서 사격이 이어지지 않는다.
// 사격선은 이 적의 RaycastMotor2D(충돌 Layer: Platform)로 확인하며, 이동은 EnemyController.Velocity만 바꾼다.
public class FlyingShootAbility : FlyingAbility
{
    [Tooltip("사격이 끝난 뒤 실행할 패턴입니다(Drone은 FlyingPatrolAbility).")]
    [SerializeField] private AiAbility nextAbility;

    private FlyingShootDataSO shootData;
    private int firedCount;
    private float shotTimer;

    // 검증과 확인용 읽기 전용 상태
    public int FiredCount => firedCount;
    public Vector2 MuzzlePosition => shootData.useExecutePoint && executePoint != null
        ? (Vector2)executePoint.position
        : (Vector2)transform.position;

    protected override void Awake()
    {
        base.Awake();
        shootData = (FlyingShootDataSO)data;
    }

    // 플레이어가 사거리 안에 있고, 바라보는 방향에서 사격 각도(maxAimAngle) 안쪽이며, 사격선이 지형에 막히지 않았을 때 시작할 수 있다.
    // 거리 유지 중에는 매 프레임 플레이어를 바라보므로 사격 각도가 90 이상이면 바로 아래의 플레이어도 쏜다.
    public override bool CanBegin()
    {
        if (!HasTarget)
            AcquireTarget();
        if (!HasTarget || controller == null)
            return false;

        Vector2 origin = MuzzlePosition;
        Vector2 toTarget = (Vector2)target.position - origin;
        float distance = toTarget.magnitude;
        if (distance < 0.01f || distance > shootData.maxRange)
            return false;

        if (Vector2.Angle(new Vector2(controller.Facing, 0f), toTarget) > shootData.maxAimAngle)
            return false;

        if (!shootData.requireLineOfSight || controller.Motor == null)
            return true;

        return controller.Motor.RaycastEnvironment(origin, toTarget / distance, distance).collider == null;
    }

    protected override void OnWindupStart()
    {
        AcquireTarget();
        BeginFlight();
        firedCount = 0;
        shotTimer = 0f;
        // 사격 중에도 같은 flockId 구성원과의 분리 계산에 남는다(역할 None이면 빠진다). 무리 대형에서는 빠진다.
        SetFlockRole(EnemyFlockRole.Chase);
        FaceTarget();
    }

    protected override void OnWindup(float t) => HoldPosition();

    protected override void OnExecuteStart()
    {
        FaceTarget();
        Fire();
    }

    protected override void OnExecute(float t)
    {
        Hover();
        if (firedCount >= shootData.shotCount)
            return;

        shotTimer += Time.deltaTime;
        if (shotTimer < shootData.shotInterval)
            return;

        shotTimer -= shootData.shotInterval;
        FaceTarget();
        Fire();
    }

    // 모든 탄을 쏘고 executeTime(발사 연출 길이)이 지나면 끝난다.
    protected override bool CanFinishExecute(float elapsed) => firedCount >= shootData.shotCount && elapsed >= data.executeTime;

    protected override void OnRecover(float t) => Hover();

    protected override void OnStop()
    {
        SetFlockRole(EnemyFlockRole.None);
        ReserveNext(nextAbility);
    }

    private void HoldPosition()
    {
        if (!HasTarget)
            AcquireTarget();
        FaceTarget();
        Hover();
    }

    private void Fire()
    {
        firedCount++;
        if (!HasTarget || shootData.projectilePrefab == null || WorldEntityPool.Instance == null)
            return;

        // 탄 방향은 탄(DroneEnemyBullet)이 적의 바라보는 방향 기준 발사 각도 안에서 정한다.
        // 그 각도를 사격 시작 조건과 같은 사격 각도로 맞춰, 쏘기 시작한 탄이 각도 제한에 잘리지 않게 한다.
        Vector2 origin = MuzzlePosition;
        ProjectileBase projectile = WorldEntityPool.Instance.Spawn(shootData.projectilePrefab, origin, Quaternion.identity);
        if (projectile is DroneEnemyBullet coneBullet)
            coneBullet.FireConeAngle = shootData.maxAimAngle;
        projectile.Initialize(controller, shootData.damageInfo, shootData.projectileSpeed, shootData.shotCount, shootData.collideLayer);

        // 반동: 플레이어 반대쪽으로 외부 힘을 준다. EnemyController가 감쇠하고 EnemyFlightTilt가 기울기로 보여 준다.
        if (shootData.recoilSpeed > 0f && HasTarget)
        {
            Vector2 aim = (Vector2)target.position - origin;
            if (aim.sqrMagnitude > 0.0001f)
                controller.AddKnockback(-aim.normalized * shootData.recoilSpeed);
        }
    }
}
