using UnityEngine;

// 플레이어 옆 위의 유지 지점(기본 좌우 5칸·위 5칸)에서 거리를 두고 파리처럼 맴도는 비행 순찰이다.
// - 무리 전체가 sideSwitchInterval마다 플레이어의 왼쪽·오른쪽을 바꾼다.
// - 여러 마리면 무리 중심의 구성원이 유지 지점으로 가고, 나머지는 그 구성원을 둘러싼 고리 칸으로 간다(EnemyFlock).
// - 천장·벽은 RaycastMotor2D.RaycastEnvironment로 확인해 유지 지점을 방 안쪽으로 당긴다.
// - 유지 시간이 끝나면 다음 패턴(추적)을 예약한다. 무리의 동시 추적 수가 가득 차면 자리가 날 때까지 거리 유지를 이어 간다.
public class FlyingPatrolAbility : FlyingAbility
{
    [Tooltip("거리 유지가 끝난 뒤 실행할 패턴입니다(Swarmer는 FlyingChaseAbility). 비우면 계속 거리를 유지합니다.")]
    [SerializeField] private AiAbility nextAbility;

    private FlyingPatrolDataSO patrolData;
    private float holdDuration;

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
        holdDuration = Random.Range(patrolData.holdDuration.x, patrolData.holdDuration.y);
        SetFlockRole(EnemyFlockRole.Formation);
    }

    protected override void OnWindup(float t) => Fly();
    protected override void OnExecute(float t) => Fly();

    // 유지 시간이 지났고 대상이 있으며 무리의 동시 추적 수에 여유가 있을 때 끝난다.
    protected override bool CanFinishExecute(float elapsed)
    {
        if (nextAbility == null || !HasTarget || elapsed < holdDuration)
            return false;

        EnemyFlock flock = Flock;
        int limit = patrolData.maxConcurrentChasers;
        return limit <= 0 || flock == null || flock.CountRole(EnemyFlockRole.Chase) < limit;
    }

    // 추적 자리를 바로 차지해, 같은 프레임에 판단하는 다른 구성원이 동시 추적 수를 넘지 않게 한다.
    protected override void OnRecoverStart() => SetFlockRole(EnemyFlockRole.Chase);
    protected override void OnRecover(float t) => Fly();

    protected override void OnStop()
    {
        if (brain != null && brain.IsPaused)
            SetFlockRole(EnemyFlockRole.None);

        ReserveNext(nextAbility);
    }

    private void Fly()
    {
        if (!HasTarget)
        {
            AcquireTarget();
            if (!HasTarget)
            {
                Hover();
                return;
            }
        }

        FaceTarget();
        Vector2 holdPoint = ResolveHoldPoint();
        EnemyFlock flock = Flock;
        Vector2 point = flock != null
            ? flock.GetFormationPoint(flockMember, holdPoint, patrolData.formationSpacing, patrolData.formationHeightRatio)
            : holdPoint;
        SteerTowards(point + UpdateWander(), MaxSpeed);
    }

    // 플레이어 기준 유지 지점이다. 무리가 정한 쪽(왼쪽·오른쪽)으로 playerOffset.x, 위로 playerOffset.y만큼 떨어진다.
    // 천장·벽이 가까우면 wallMargin만큼 안쪽으로 당기고, 정한 쪽 공간이 minSideSpace보다 좁으면 이번에는 반대쪽을 쓴다.
    // 질의는 이 적의 RaycastMotor2D(충돌 Layer: Platform)로 하며, 같은 프레임의 다른 구성원은 계산 결과를 다시 쓴다.
    private Vector2 ResolveHoldPoint()
    {
        EnemyFlock flock = Flock;
        if (flock != null && flock.TryGetHoldPoint(out Vector2 cached))
            return cached;

        Vector2 player = target.position;
        Vector2 offset = patrolData.playerOffset;
        int preferredSide = transform.position.x >= player.x ? 1 : -1;
        int side = flock != null ? flock.ResolveSide(preferredSide, patrolData.sideSwitchInterval) : preferredSide;

        float height = AvailableDistance(player, Vector2.up, offset.y);
        Vector2 basePoint = player + Vector2.up * height;
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
        if (flock != null)
            flock.SetHoldPoint(point);
        return point;
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
