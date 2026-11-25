using UnityEngine;

// 플레이어 옆 위의 유지 지점(기본 좌우 5칸·위 5칸)에서 거리를 두고 파리처럼 맴도는 비행 순찰이다.
// - 무리로 움직이면(moveAsFlock, Swarmer) 무리 전체가 sideSwitchInterval마다 플레이어의 왼쪽·오른쪽을 바꾼다.
//   여러 마리면 무리 중심의 구성원이 유지 지점으로 가고, 나머지는 그 구성원을 둘러싼 고리 칸으로 간다(EnemyFlock).
// - 무리 이동을 끄면 개체마다 좌우를 정해 각자 유지 지점으로 간다. 대형·동시 추적 제한은 쓰지 않고,
//   같은 flockId끼리는 가까워질 때 서로 밀어내기만 한다(FlyingAbility의 분리).
// - 천장·벽은 RaycastMotor2D.RaycastEnvironment로 확인해 유지 지점을 방 안쪽으로 당긴다.
// - 유지 시간이 끝나면 다음 패턴(Swarmer 추적, Drone 사격)을 예약한다. 무리의 동시 추적 수가 가득 찼거나
//   다음 패턴의 시작 조건(FlyingAbility.CanBegin, 예: 사거리·사격선)을 채우지 못하면 거리 유지를 이어 간다.
// - 장애물 회피(EnemyFlightNavigator, 설정은 데이터의 avoidance):
//   유지 지점은 몸 크기로 맞추고(그 자리의 벽·천장·바닥) holdCacheTime 동안 다시 쓴다(좌우·감지·기준점이 바뀌면 바로 다시 계산).
//   대형 고리 칸은 기준 구성원에서 몸으로 쓸어 지형 앞에서 멈춘다. 갈 수 없는 유지 지점은 사격하는 적(Drone·Aegis)이면
//   사격선이 열리고 갈 수 있는 자리로 다시 고르고(유지 지점에서 사격선이 losGrace보다 오래 막혀도 같다), 그 밖에는 좌우 전환을 앞당긴다.
public class FlyingPatrolAbility : FlyingAbility
{
    // 사격 시작 조건(사거리·사격선 광선)을 다시 확인하는 간격(초)이다. 유지 시간이 끝난 첫 프레임은 바로 확인한다.
    // 값은 순찰 SO의 attackCheckInterval(기본 0.1초, Enemy Tool에서 편집)이다. 0이면 매 프레임 확인한다.
    private float ReadyCheckInterval => patrolData != null ? patrolData.attackCheckInterval : .1f;
    // 유지 지점에서 이 거리(칸) 안에 있을 때만 사격선이 막힌 시간을 잰다(돌아가는 중에는 재지 않는다).
    private const float HoldNearDistance = 2.5f;

    [Tooltip("거리 유지가 끝난 뒤 실행할 패턴입니다(Swarmer는 FlyingChaseAbility, Drone은 FlyingShootAbility). 비우면 계속 거리를 유지합니다.")]
    [SerializeField] private AiAbility nextAbility;

    private FlyingPatrolDataSO patrolData;
    private float holdDuration;
    // 무리로 움직이지 않을 때 이 개체가 유지하는 쪽(-1 왼쪽, 1 오른쪽, 0 아직 정하지 않음)과 다음 전환 시각
    private int soloSide;
    private float soloNextSwitchTime;

    // 유지 지점 보정 캐시: 감지 여부·요청한 쪽이 같고 기준점이 1칸 안이면 holdCacheTime 동안 다시 쓴다.
    private bool holdCacheValid;
    private bool holdCacheDetected;
    private int holdCacheSide;
    private Vector2 holdCacheAnchor;
    private Vector2 holdCachePoint;
    private float holdCacheUntil;
    // 대형 고리 칸 투영 비율 캐시(기준 구성원 → 칸 방향으로 몸이 지나갈 수 있는 비율)
    private float slotRatio = 1f;
    // 이번 프레임 대형 칸이 지형 앞으로 당겨졌으면(투영 비율 < 1) 막힌 쪽 단위 방향, 아니면 0. 분리 힘의 그 성분을 버린다.
    private Vector2 blockedSlotAxis;
    private float slotRatioUntil;
    private Vector2 slotRatioOffset;
    // 사격선이 필요한 다음 패턴(Drone 사격·Aegis 공격)인지, 유지 지점에서 사격선이 막히기 시작한 시각(-1은 막히지 않음)
    private bool needsLineOfSight;
    private float lineBlockedSince = -1f;
    // 공격 위치 재선정 결과. 좌우 전환 간격이 지나거나 Player가 3칸 넘게 움직이면 버린다.
    private bool hasAttackOverride;
    private Vector2 attackOverride;
    private Vector2 overrideAnchor;
    private float overrideUntil;
    // 무리 순찰에서 막혀 이 개체만 반대쪽으로 바꾼 상태. 무리가 스스로 쪽을 바꾸면 끝난다.
    private bool sideFlip;
    private int sideFlipBase;
    private float nextReadyCheck;
    private bool readyCached;

    public float HoldDuration => holdDuration;
    public bool HasAttackOverride => hasAttackOverride;

    // 순간이동 뒤 현재 위치에서 유지 방향을 다시 정할 때 사용한다.
    protected void ResetPatrolSide() => soloSide = 0;

    protected override void Awake()
    {
        base.Awake();
        patrolData = (FlyingPatrolDataSO)data;
        needsLineOfSight = nextAbility is FlyingShootAbility || this is AegisPatrolAbility;
    }

    protected override void OnWindupStart()
    {
        blockedSlotAxis = Vector2.zero;
        AcquireTarget();
        BeginFlight();
        if (flockMember != null) flockMember.RememberSpawnPosition();
        holdDuration = Random.Range(patrolData.holdDuration.x, patrolData.holdDuration.y);
        SetFlockRole(HasTarget ? EnemyFlockRole.Formation : EnemyFlockRole.LocalPatrol);
        holdCacheValid = false;
        lineBlockedSince = -1f;
        nextReadyCheck = 0f;
        readyCached = false;
        // 다른 패턴(추적·사격) 중에 남은 도달 불가 알림은 이번 순찰과 관계없다.
        navigator?.ConsumeUnreachable();
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

        if (!(nextAbility is FlyingAbility flight))
            return true;
        if (!(flight is FlyingShootAbility))
            return flight.CanBegin();

        // 사격 시작 조건은 사격선 광선이 들어 있어 ReadyCheckInterval(순찰 SO attackCheckInterval)마다 확인한다(기본값이면 사격 시작이 최대 0.1초 늦어진다).
        float now = Time.time;
        if (now >= nextReadyCheck)
        {
            readyCached = flight.CanBegin();
            nextReadyCheck = now + ReadyCheckInterval;
        }
        return readyCached;
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
            sideFlip = false;
            hasAttackOverride = false;
            holdCacheValid = false;
        }

        ReserveNext(HasTarget ? nextAbility : this);
    }

    private void Fly()
    {
        blockedSlotAxis = Vector2.zero;
        if (target == null || !target.gameObject.activeInHierarchy) AcquireTarget();
        bool detected = HasTarget;
        if (flockMember == null && !detected) { Hover(); return; }

        SetFlockRole(!detected ? EnemyFlockRole.LocalPatrol
            : State == AbilityState.Recover ? EnemyFlockRole.Chase : EnemyFlockRole.Formation);
        if (detected) FaceTarget();
        // 이미 공중에 있는 최초 위치를 중심으로 같은 좌우 순찰을 한다. 미감지 개체는 교전 대형에 끌려가지 않는다.
        Vector2 anchor = detected ? (Vector2)target.position : flockMember.SpawnPosition;
        Vector2 holdPoint = AdjustedHoldPoint(anchor, detected);
        EnemyFlock flock = Flock;
        Vector2 point = detected && patrolData.moveAsFlock && flock != null
            ? ProjectFormation(flock, flock.GetFormationPoint(flockMember, holdPoint, patrolData.formationSpacing, patrolData.formationHeightRatio), holdPoint)
            : holdPoint;
        if (!detected && Mathf.Abs(point.x - transform.position.x) > .05f)
            controller.FacingUpdate(point.x > transform.position.x ? 1 : -1);
        SteerTowards(point + UpdateWander(), MaxSpeed);
        if (navigator != null && navigator.ConsumeUnreachable()) OnUnreachable(detected);
    }

    // 회피 보정을 거친 유지 지점이다. 계산은 ResolveHoldPoint(기존 규칙)이고, 여기서 공격 위치 재선정·몸 맞춤을 더하고 캐시한다.
    private Vector2 AdjustedHoldPoint(Vector2 anchor, bool detected)
    {
        FlightAvoidanceSettings settings = flightData.avoidance;
        EnemyFlock flock = patrolData.moveAsFlock ? Flock : null;
        int side = ResolveRequestedSide(anchor, flock);
        float now = Time.time;
        if (holdCacheValid && now < holdCacheUntil && holdCacheDetected == detected && holdCacheSide == side
            && (anchor - holdCacheAnchor).sqrMagnitude < 1f)
            return holdCachePoint;

        Vector2 point = ResolveHoldPoint(anchor, detected);
        if (navigator != null && settings.enabled)
        {
            if (detected) point = ApplyAttackOverride(anchor, point, settings, now);
            point = navigator.FitHoldPoint(new Vector2(anchor.x, point.y), point, settings);
        }

        holdCacheValid = settings.holdCacheTime > 0f;
        holdCacheUntil = now + settings.holdCacheTime;
        holdCacheDetected = detected;
        holdCacheSide = side;
        holdCacheAnchor = anchor;
        holdCachePoint = point;
        return point;
    }

    // 재선정한 자리가 있으면 그 자리를 쓰고, 사격이 필요한 적은 유지 지점 근처에서 사격선이 막힌 시간을 재 losGrace를 넘으면 다시 고른다.
    private Vector2 ApplyAttackOverride(Vector2 anchor, Vector2 point, in FlightAvoidanceSettings settings, float now)
    {
        if (hasAttackOverride && (now >= overrideUntil || (anchor - overrideAnchor).sqrMagnitude > 9f))
            hasAttackOverride = false;
        if (hasAttackOverride) point = attackOverride;
        if (!needsLineOfSight || controller.Motor == null) return point;

        Vector2 position = transform.position;
        bool near = (position - point).sqrMagnitude <= HoldNearDistance * HoldNearDistance;
        if (!near || !LineBlocked(position, anchor))
        {
            lineBlockedSince = -1f;
            return point;
        }
        if (lineBlockedSince < 0f) lineBlockedSince = now;
        if (now - lineBlockedSince < settings.losGrace) return point;

        lineBlockedSince = -1f;
        return TryReselect(anchor, settings, now, out Vector2 chosen) ? chosen : point;
    }

    private bool TryReselect(Vector2 anchor, in FlightAvoidanceSettings settings, float now, out Vector2 chosen)
    {
        int preferredSide = transform.position.x >= anchor.x ? 1 : -1;
        if (!navigator.TryFindAttackPosition(anchor, patrolData.playerOffset, patrolData.wallMargin, preferredSide, false, settings, out chosen))
            return false;
        hasAttackOverride = true;
        attackOverride = chosen;
        overrideAnchor = anchor;
        overrideUntil = now + patrolData.sideSwitchInterval;
        return true;
    }

    private bool LineBlocked(Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        return distance > .01f && controller.Motor.RaycastEnvironment(from, delta / distance, distance).collider != null;
    }

    // 짧은 시간에 여러 번 막혔다: 사격하는 적은 공격 위치를 바로 다시 고르고, 그 밖에는(또는 고를 자리가 없으면) 좌우 전환을 앞당긴다.
    private void OnUnreachable(bool detected)
    {
        holdCacheValid = false;
        if (detected && needsLineOfSight && TryReselect(target.position, flightData.avoidance, Time.time, out _))
            return;
        AdvanceSideSwitch();
    }

    private void AdvanceSideSwitch()
    {
        EnemyFlock flock = patrolData.moveAsFlock ? Flock : null;
        if (flock == null)
        {
            if (soloSide != 0) soloNextSwitchTime = Time.time;
            return;
        }
        // 무리의 쪽은 다른 구성원도 쓰므로 이 개체만 반대쪽으로 바꾼다. 이미 바꿨으면 원래 쪽으로 돌아간다.
        if (sideFlip) { sideFlip = false; return; }
        sideFlip = true;
        sideFlipBase = flock.Side;
    }

    // 대형 고리 칸을 기준 구성원에서 몸으로 쓸어 지형 앞에서 멈춘다(EnemyFlock 계산은 그대로 두고 사용처에서 보정).
    private Vector2 ProjectFormation(EnemyFlock flock, Vector2 slot, Vector2 holdPoint)
    {
        EnemyFlockMember leader = flock.Leader;
        if (navigator == null || leader == null || leader == flockMember || slot == holdPoint) return slot;
        FlightAvoidanceSettings settings = flightData.avoidance;
        if (!settings.enabled) return slot;
        Vector2 from = leader.Position;
        Vector2 offset = slot - from;
        float now = Time.time;
        if (now >= slotRatioUntil || (offset - slotRatioOffset).sqrMagnitude > .25f)
        {
            slotRatio = navigator.ProjectSlot(from, slot, settings);
            slotRatioOffset = offset;
            slotRatioUntil = now + settings.holdCacheTime;
        }
        if (slotRatio >= 1f) return slot;
        float length = offset.magnitude;
        if (length > 1e-4f) blockedSlotAxis = offset / length;
        return from + offset * slotRatio;
    }

    // 대형 칸이 천장·벽 앞으로 당겨진 구성원은 이웃의 분리 힘이 막힌 지형 쪽으로 밀어도 그 성분을 버린다(5차 — 위 칸 Drone이
    // 기준 구성원에게 밀려 천장에 붙은 채 머물지 않게). 옆·반대쪽 분리는 그대로다.
    protected override Vector2 FilterSeparation(Vector2 separation)
    {
        if (blockedSlotAxis == Vector2.zero) return separation;
        float toward = Vector2.Dot(separation, blockedSlotAxis);
        return toward > 0f ? separation - blockedSlotAxis * toward : separation;
    }

    // 플레이어 기준 유지 지점이다. 정한 쪽(왼쪽·오른쪽)으로 playerOffset.x, 위로 playerOffset.y만큼 떨어진다.
    // 천장·벽이 가까우면 wallMargin만큼 안쪽으로 당기고, 정한 쪽 공간이 minSideSpace보다 좁으면 이번에는 반대쪽을 쓴다.
    // 질의는 이 적의 RaycastMotor2D(충돌 Layer: Platform)로 한다. 무리로 움직이면 같은 프레임의 다른 구성원이 계산 결과를 다시 쓴다.
    private Vector2 ResolveHoldPoint(Vector2 anchor, bool detected)
    {
        EnemyFlock flock = patrolData.moveAsFlock ? Flock : null;
        if (detected && flock != null && !sideFlip && flock.TryGetHoldPoint(out Vector2 cached))
            return cached;

        Vector2 offset = patrolData.playerOffset;
        int side = ResolveRequestedSide(anchor, flock);

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
        if (detected && flock != null && !sideFlip)
            flock.SetHoldPoint(point);
        return point;
    }

    // 이번에 유지하려는 쪽이다(무리면 무리의 쪽, 아니면 개체의 쪽). 막혀서 이 개체만 반대쪽으로 바꾼 동안은 무리의 반대쪽이다.
    private int ResolveRequestedSide(Vector2 anchor, EnemyFlock flock)
    {
        int preferredSide = transform.position.x >= anchor.x ? 1 : -1;
        if (flock == null) return ResolveSoloSide(preferredSide);
        int side = flock.ResolveSide(preferredSide, patrolData.sideSwitchInterval);
        if (!sideFlip) return side;
        if (side != sideFlipBase)
        {
            sideFlip = false;
            return side;
        }
        return -side;
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
