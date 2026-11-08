using UnityEngine;

// 플레이어 옆 위의 유지 지점(기본 좌우 5칸·위 5칸)에서 거리를 두고 파리처럼 맴도는 비행 순찰이다.
// - 무리로 움직이면(moveAsFlock, Swarmer) 무리 전체가 sideSwitchInterval마다 플레이어의 왼쪽·오른쪽을 바꾼다.
//   여러 마리면 무리 중심의 구성원이 유지 지점으로 가고, 나머지는 그 구성원을 둘러싼 고리 칸으로 간다(EnemyFlock).
// - 무리 이동을 끄면 개체마다 좌우를 정해 각자 유지 지점으로 간다. 대형·동시 추적 제한은 쓰지 않고,
//   같은 flockId끼리는 가까워질 때 서로 밀어내기만 한다(FlyingAbility의 분리).
// - 천장·벽은 RaycastMotor2D.RaycastEnvironment로 확인해 유지 지점을 방 안쪽으로 당긴다.
// - 유지 시간이 끝나면 다음 패턴(Swarmer 추적, Drone 사격)을 예약한다. 무리의 동시 추적 수가 가득 찼거나
//   다음 패턴의 시작 조건(FlyingAbility.CanBegin, 예: 사거리·사격선)을 채우지 못하면 거리 유지를 이어 간다.
public class FlyingPatrolAbility : FlyingAbility
{
    [Tooltip("거리 유지가 끝난 뒤 실행할 패턴입니다(Swarmer는 FlyingChaseAbility, Drone은 FlyingShootAbility). 비우면 계속 거리를 유지합니다.")]
    [SerializeField] private AiAbility nextAbility;

    private FlyingPatrolDataSO patrolData;
    private float holdDuration;
    // 무리로 움직이지 않을 때 이 개체가 유지하는 쪽(-1 왼쪽, 1 오른쪽, 0 아직 정하지 않음)과 다음 전환 시각
    private int soloSide;
    private float soloNextSwitchTime;

    public float HoldDuration => holdDuration;

    protected override void Awake()
    {
        base.Awake();
        patrolData = (FlyingPatrolDataSO)data;
    }

    protected override void OnWindupStart()
    {
        AcquireTarget();
        BeginFlight();
        if (flockMember != null) flockMember.RememberSpawnPosition();
        holdDuration = Random.Range(patrolData.holdDuration.x, patrolData.holdDuration.y);
        SetFlockRole(HasTarget ? EnemyFlockRole.Formation : EnemyFlockRole.LocalPatrol);
    }

    protected override void OnWindup(float t) => Fly();
    protected override void OnExecute(float t) => Fly();

    // 유지 시간이 지났고 대상이 있으며, 무리의 동시 추적 수에 여유가 있고 다음 패턴이 시작할 수 있을 때 끝난다.
    protected override bool CanFinishExecute(float elapsed)
    {
        if (nextAbility == null || !nextAbility.IsReady() || !HasTarget || elapsed < holdDuration)
            return false;

        EnemyFlock flock = Flock;
        int limit = patrolData.maxConcurrentChasers;
        if (patrolData.moveAsFlock && limit > 0 && flock != null && flock.CountRole(EnemyFlockRole.Chase) >= limit)
            return false;

        return !(nextAbility is FlyingAbility flight) || flight.CanBegin();
    }

    // 추적 자리를 바로 차지해, 같은 프레임에 판단하는 다른 구성원이 동시 추적 수를 넘지 않게 한다.
    protected override void OnRecoverStart() => SetFlockRole(EnemyFlockRole.Chase);
    protected override void OnRecover(float t) => Fly();

    protected override void OnStop()
    {
        if (brain != null && brain.IsPaused)
        {
            SetFlockRole(EnemyFlockRole.None);
            soloSide = 0;   // 등장·사망(Pool 반환)·Freeze 뒤에는 쪽을 처음부터 다시 정한다.
        }

        ReserveNext(HasTarget ? nextAbility : this);
    }

    private void Fly()
    {
        if (target == null || !target.gameObject.activeInHierarchy) AcquireTarget();
        bool detected = HasTarget;
        if (flockMember == null && !detected) { Hover(); return; }

        SetFlockRole(!detected ? EnemyFlockRole.LocalPatrol
            : State == AbilityState.Recover ? EnemyFlockRole.Chase : EnemyFlockRole.Formation);
        if (detected) FaceTarget();
        // 이미 공중에 있는 최초 위치를 중심으로 같은 좌우 순찰을 한다. 미감지 개체는 교전 대형에 끌려가지 않는다.
        Vector2 anchor = detected ? (Vector2)target.position : flockMember.SpawnPosition;
        Vector2 holdPoint = ResolveHoldPoint(anchor, detected);
        EnemyFlock flock = Flock;
        Vector2 point = detected && patrolData.moveAsFlock && flock != null
            ? flock.GetFormationPoint(flockMember, holdPoint, patrolData.formationSpacing, patrolData.formationHeightRatio)
            : holdPoint;
        if (!detected && Mathf.Abs(point.x - transform.position.x) > .05f)
            controller.FacingUpdate(point.x > transform.position.x ? 1 : -1);
        SteerTowards(point + UpdateWander(), MaxSpeed);
    }

    // 플레이어 기준 유지 지점이다. 정한 쪽(왼쪽·오른쪽)으로 playerOffset.x, 위로 playerOffset.y만큼 떨어진다.
    // 천장·벽이 가까우면 wallMargin만큼 안쪽으로 당기고, 정한 쪽 공간이 minSideSpace보다 좁으면 이번에는 반대쪽을 쓴다.
    // 질의는 이 적의 RaycastMotor2D(충돌 Layer: Platform)로 한다. 무리로 움직이면 같은 프레임의 다른 구성원이 계산 결과를 다시 쓴다.
    private Vector2 ResolveHoldPoint(Vector2 anchor, bool detected)
    {
        EnemyFlock flock = patrolData.moveAsFlock ? Flock : null;
        if (detected && flock != null && flock.TryGetHoldPoint(out Vector2 cached))
            return cached;

        Vector2 offset = patrolData.playerOffset;
        int preferredSide = transform.position.x >= anchor.x ? 1 : -1;
        int side = flock != null ? flock.ResolveSide(preferredSide, patrolData.sideSwitchInterval) : ResolveSoloSide(preferredSide);

        float height = detected ? AvailableDistance(anchor, Vector2.up, offset.y) : 0f;
        Vector2 basePoint = anchor + Vector2.up * height;
        float width = AvailableDistance(basePoint, Vector2.right * side, offset.x);
        if (width < patrolData.minSideSpace)
        {
            float otherWidth = AvailableDistance(basePoint, Vector2.right * -side, offset.x);
            if (otherWidth > width)
            {
                side = -side;
                width = otherWidth;
            }
        }

        Vector2 point = basePoint + Vector2.right * (side * width);
        if (detected && flock != null)
            flock.SetHoldPoint(point);
        return point;
    }

    // 무리 없이 이 개체만의 좌우를 정한다. 처음에는 지금 있는 쪽에서 시작하고, 간격을 개체마다 ±25% 흔들어
    // 함께 생성된 여러 대가 같은 순간에 바꾸지 않게 한다.
    private int ResolveSoloSide(int preferredSide)
    {
        float now = Time.time;
        if (soloSide == 0)
        {
            soloSide = preferredSide < 0 ? -1 : 1;
            soloNextSwitchTime = now + patrolData.sideSwitchInterval * Random.Range(0.75f, 1.25f);
        }
        else if (now >= soloNextSwitchTime)
        {
            soloSide = -soloSide;
            soloNextSwitchTime = now + patrolData.sideSwitchInterval * Random.Range(0.75f, 1.25f);
        }
        return soloSide;
    }

    // origin에서 direction으로 wanted만큼 갈 수 있는 거리다. 지형이 막으면 wallMargin을 뺀 거리로 줄인다.
    private float AvailableDistance(Vector2 origin, Vector2 direction, float wanted)
    {
        if (wanted <= 0f || controller.Motor == null)
            return Mathf.Max(0f, wanted);

        RaycastHit2D hit = controller.Motor.RaycastEnvironment(origin, direction, wanted + patrolData.wallMargin);
        return hit.collider != null ? Mathf.Clamp(hit.distance - patrolData.wallMargin, 0f, wanted) : wanted;
    }
}
