using UnityEngine;

// 플레이어 옆 위의 유지 지점(기본 좌우 5칸·위 5칸)에서 거리를 두고 파리처럼 맴도는 비행 순찰이다.
// - 무리로 움직이면(moveAsFlock, Swarmer) 무리 전체가 sideSwitchInterval마다 플레이어의 왼쪽·오른쪽을 바꾼다.
//   여러 마리면 무리 중심의 구성원이 유지 지점으로 가고, 나머지는 그 구성원을 둘러싼 고리 칸으로 간다(EnemyFlock).
// - 무리 이동을 끄면 개체마다 좌우를 정해 각자 유지 지점으로 간다. 대형·동시 추적 제한은 쓰지 않고,
//   같은 flockId끼리는 가까워질 때 서로 밀어내기만 한다(FlyingAbility의 분리).
// - 천장·벽은 RaycastMotor2D.RaycastEnvironment로 확인해 유지 지점을 방 안쪽으로 당긴다.
// - 유지 시간이 끝나면 다음 패턴(Swarmer 추적, Drone 사격)을 예약한다. 무리의 동시 추적 수가 가득 찼거나
//   다음 패턴의 시작 조건(FlyingAbility.CanBegin, 예: 사거리·사격선)을 채우지 못하면 거리 유지를 이어 간다.
//   추적(FlyingChaseAbility)은 그 추적 시간 안에 닿을 수 있는 자리(자기 유지 자리 근처, 또는 닿을 직선 거리 안에서 직선이 열림)에 왔을 때만 넘긴다.
//   먼 자리에서 시작한 추적은 더 느린 속도로 날다 닿지 못하고 물러나므로, 그 전에는 더 빠른 거리 유지로 계속 다가간다(유지 시간 + 8초가 상한).
// - 장애물 회피(EnemyFlightNavigator, 설정은 데이터의 avoidance):
//   유지 지점은 몸 크기로 맞추고(그 자리의 벽·천장·바닥) holdCacheTime 동안 다시 쓴다(좌우·감지·기준점이 바뀌면 바로 다시 계산).
//   대형 고리 칸은 기준 구성원에서 몸으로 쓸어 지형 앞에서 멈춘다. 갈 수 없는 유지 지점은 사격하는 적(Drone·Aegis)이면
//   사격선이 열리고 갈 수 있는 자리로 다시 고르고(유지 지점에서 사격선이 losGrace보다 오래 막혀도 같다), 그 밖에는 좌우 전환을 앞당긴다.
//   기본 후보(playerOffset 둘레)에 사격선이 열린 자리가 없으면 더 가깝고 높은 후보(CoverFallbackOffset)로 한 번 더 찾는다(낮은 엄폐 뒤 Player).
// - 길찾기 우선 무리(6차, 데이터 formationPathFirst): 추종 구성원은 상태 셋 중 하나로 움직인다.
//   Formation = 지금까지의 대형(기준 구성원 현재 위치 + 고리 칸, 지형 투영). 자기 칸까지 몸 쓸기가 열리고, 기준 구성원이 직선으로 날며 막히지 않았고,
//               칸이 고리 간격 × formationRegroupDistance 안일 때만(다시 들어올 때는 formationRejoinDelay 동안 계속 가능해야) 쓴다.
//   Regroup   = 유지 지점 기준 가상 칸(유지 지점 + 같은 고리 오프셋, 유지 지점에서 몸으로 투영)으로 길찾기 경로를 따라간다. 목표가 움직이는
//               기준 구성원이 아니라 유지 지점이라 경로를 자주 다시 찾지 않고, 벽 너머 기준 구성원을 쫓지 않는다.
//   Override  = 사격하는 적의 추종 구성원도 자기 목표 근처에서 사격선이 losGrace 넘게 막히면 공격 위치를 다시 골라 그 자리로 간다.
//               다시 고를 자리가 없으면 가상 칸의 좌우를 뒤집는다.
//   판정은 holdCacheTime 주기(개체마다 위상을 나눔)로 하고, 길찾기 중(경로·국소)에는 분리 힘에 pathSeparationScale을 곱한다.
//   기준 구성원이 직선이 아닌 회피로 leaderBlockedDemote보다 오래 날면 EnemyFlockMember.NavigationBlocked를 켜 무리가 기준 구성원을 바꾸게 한다.
public class FlyingPatrolAbility : FlyingAbility
{
    // 길찾기 우선 무리에서 추종 구성원의 상태다. None = 기준 구성원·단독·대형 밖(5차와 같은 이동).
    public enum FollowerState { None, Formation, Regroup, Override }

    // 사격 시작 조건(사거리·사격선 광선)을 다시 확인하는 간격(초)이다. 유지 시간이 끝난 첫 프레임은 바로 확인한다.
    // 값은 순찰 SO의 attackCheckInterval(기본 0.1초, Enemy Tool에서 편집)이다. 0이면 매 프레임 확인한다.
    private float ReadyCheckInterval => patrolData != null ? patrolData.attackCheckInterval : .1f;
    // 유지 지점에서 이 거리(칸) 안에 있을 때만 사격선이 막힌 시간을 잰다(돌아가는 중에는 재지 않는다).
    private const float HoldNearDistance = 2.5f;
    // 추적으로 넘길 때 닿을 직선 거리 = 추적 속도 × 최소 추적 시간 × 이 비율 + 접촉 거리(경로가 직선보다 조금 길어도 최소 추적 시간 안에 닿게 두는 여유).
    private const float ChaseReachMargin = .85f;
    // 유지 시간이 지난 뒤 닿을 자리에 들어오기를 기다리는 상한(초). 넘으면 조건 없이 추적으로 넘긴다(들어올 수 없는 자리에서 추적을 영영 미루지 않게).
    // 긴 방(LongGallery 등 생성 자리가 Player에서 50칸 넘게)은 유지 시간 + 4초 안에 닿을 거리에 못 와 먼 추적이 헛돌았다(근사 첫 추적 실패 12~15%, 8초면 0%).
    private const float ChaseApproachLimit = 8f;
    // 자기 유지 자리 근처에서 넘길 때 Player까지 거리의 상한 여유(칸): 닿을 거리와 '자리의 먼 끝까지 거리 + 이 값' 가운데 큰 값.
    // 먼 곳에서 들어오는 개체가 자리 둘레(HoldNearDistance)의 바깥 가장자리(바깥 칸이면 Player에서 10~11칸)에서 넘기지 않게 한다.
    private const float SpotReachSlack = .5f;
    // 엄폐 뒤 Player용 재선정 후보(CoverFallbackOffset): 옆 거리는 이만큼(칸) 가깝게(최소 CoverFallbackMinWidth), 높이는 이만큼(칸) 높게 잡는다.
    // 가파른 사격선이 Player 바로 옆의 2칸 높이 엄폐(ReinforcedCrate 등)를 넘는다(Drone (7, 4.5) → (3, 7.5)).
    private const float CoverFallbackCloser = 4f;
    private const float CoverFallbackRise = 3f;
    private const float CoverFallbackMinWidth = 2f;
    // 개체마다 판정 시각을 나누는 황금비(EnemyFlightNavigator의 재계획 분산과 같은 방식).
    private const float Golden = .618034f;

    [Tooltip("거리 유지가 끝난 뒤 실행할 패턴입니다(Swarmer는 FlyingChaseAbility, Drone은 FlyingShootAbility). 비우면 계속 거리를 유지합니다.")]
    [SerializeField] private AiAbility nextAbility;

    private FlyingPatrolDataSO patrolData;
    private float holdDuration;
    // 무리로 움직이지 않을 때 이 개체가 유지하는 쪽(-1 왼쪽, 1 오른쪽, 0 아직 정하지 않음)과 다음 전환 시각
    private int soloSide;
    private float soloNextSwitchTime;

    // 유지 지점 보정 캐시: 감지 여부·요청한 쪽·재선정 적용 여부가 같고 기준점이 1칸 안이면 holdCacheTime 동안 다시 쓴다.
    private bool holdCacheValid;
    private bool holdCacheDetected;
    private bool holdCacheOverride;
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
    // 이번 프레임 이 개체의 대형 칸 오프셋(추종 구성원 — 자기 유지 자리 = 유지 지점 + 오프셋, 기준 구성원·단독은 0)
    private Vector2 spotOffset;
    // 추적으로 넘기기 전 Player까지 직선이 열렸는지(모터 광선 1회)와 다음 확인 시각(ReadyCheckInterval 간격)
    private float nextChaseLineCheck;
    private bool chaseLineClear;

    // 길찾기 우선 무리(6차) — 추종 구성원 상태와 판정
    private FollowerState followState;
    private float followCheckAt;
    // 대형이 계속 가능해진 시각(-1은 아직 아님). formationRejoinDelay가 지나야 Formation으로 돌아간다.
    private float formationFeasibleSince = -1f;
    // 공격 위치를 다시 고르지 못해 가상 칸의 좌우를 뒤집은 상태와 끝나는 시각(좌우 전환 간격 뒤 원래대로).
    private bool virtualFlip;
    private float virtualFlipUntil;
    // 가상 칸 투영 비율 캐시(유지 지점 → 가상 칸으로 몸이 지나갈 수 있는 비율)
    private float virtualRatio = 1f;
    private float virtualRatioUntil;
    private Vector2 virtualRatioFrom;
    private Vector2 virtualRatioOffset;
    private Vector2 lastVirtualGoal;
    private bool hasVirtualGoal;
    // 기준 구성원 강등용 막힘 기록: 직선이 아닌 회피가 시작된 시각(-1은 직선)과 도달 불가 알림으로 막힘을 유지하는 시각
    private float notDirectSince = -1f;
    private float unreachableBlockedUntil;

    public float HoldDuration => holdDuration;
    public bool HasAttackOverride => hasAttackOverride;
    // 검증용: 길찾기 우선 무리의 추종 구성원 상태(기준 구성원·단독이면 None)와 가상 칸 좌우 뒤집기 여부
    public FollowerState Follow => followState;
    public bool VirtualSlotFlipped => virtualFlip;

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
        nextChaseLineCheck = 0f;
        spotOffset = Vector2.zero;
        ResetFollow();
        ResetNavigationBlocked();
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
            return flight.CanBegin() && (!(flight is FlyingChaseAbility chase) || ChaseInReach(chase, elapsed));

        // 사격 시작 조건은 사격선 광선이 들어 있어 ReadyCheckInterval(순찰 SO attackCheckInterval)마다 확인한다(기본값이면 사격 시작이 최대 0.1초 늦어진다).
        float now = Time.time;
        if (now >= nextReadyCheck)
        {
            readyCached = flight.CanBegin();
            nextReadyCheck = now + ReadyCheckInterval;
        }
        return readyCached;
    }

    // 추적(Swarmer)은 거리 유지보다 느리고(Player 속도 × 0.75 대 × 1.2) 추적 시간(chaseDuration)이 지나면 닿지 못해도 물러난다.
    // 잠긴 던전 방은 생성 즉시 교전(6차)이라 방 반대편에서 출발한 개체는 유지 시간이 끝나도 아직 멀다. 그때 넘기면 첫 추적이 닿지 못하고
    // 물러남 + 다음 유지 시간을 더 쓴다(dFlock v6b — 긴 터널 방 Swarmer 6마리 모두 첫 추적 실패, 첫 접촉이 등장 끝 12~14초).
    // 그래서 ① 자기 유지 자리(유지 지점 → 유지 지점 + 대형 칸 오프셋 선분, 지형에 당겨진 칸 포함) HoldNearDistance 안이고 Player까지 거리가
    // 닿을 거리 또는 자리의 먼 끝까지 거리 + SpotReachSlack 안이거나 — 5차와 같은 출발 자리(직선이 막혀도 됨) —,
    // ② 최소 추적 시간에 닿을 직선 거리 안에서 직선이 열렸을 때만 넘기고, 그 전에는 더 빠른 거리 유지로 계속 다가간다.
    // ①의 거리 상한이 없으면 먼 곳에서 들어오는 바깥 칸 구성원이 자리 둘레의 바깥 가장자리(Player에서 10~11칸)에서 넘겨 첫 추적을 놓친다.
    // 유지 시간 + ChaseApproachLimit가 지나면 조건 없이 넘긴다. 직선 확인은 이 적의 모터(RaycastEnvironment) 광선 1회이며
    // ReadyCheckInterval마다만 한다(①로 넘기거나 닿을 거리 밖이면 쏘지 않는다).
    private bool ChaseInReach(FlyingChaseAbility chase, float elapsed)
    {
        if (elapsed >= holdDuration + ChaseApproachLimit || target == null || !(chase.Data is FlyingChaseDataSO chaseData))
            return true;
        Vector2 position = transform.position;
        Vector2 player = target.position;
        float reach = chase.MaxSpeed * Mathf.Min(chaseData.chaseDuration.x, chaseData.chaseDuration.y) * ChaseReachMargin + chaseData.contactDistance;
        float distanceSq = (player - position).sqrMagnitude;
        Vector2 spot = holdCachePoint + spotOffset;
        if (DistanceToSegment(position, holdCachePoint, spot) <= HoldNearDistance)
        {
            float spotLimit = Mathf.Max(reach, Mathf.Max(Vector2.Distance(player, holdCachePoint), Vector2.Distance(player, spot)) + SpotReachSlack);
            if (distanceSq <= spotLimit * spotLimit)
                return true;
        }
        if (distanceSq > reach * reach)
            return false;
        float now = Time.time;
        if (now >= nextChaseLineCheck)
        {
            chaseLineClear = controller.Motor == null || !LineBlocked(position, player);
            nextChaseLineCheck = now + ReadyCheckInterval;
        }
        return chaseLineClear;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSq = ab.sqrMagnitude;
        float t = lengthSq > 1e-6f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSq) : 0f;
        return Vector2.Distance(point, a + ab * t);
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
            virtualFlip = false;
            ResetFollow();
            ResetNavigationBlocked();
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
        EnemyFlock flock = Flock;
        bool flocking = detected && patrolData.moveAsFlock && flock != null;
        Vector2 point;
        if (flocking && patrolData.formationPathFirst
            && flock.TryGetSlot(flockMember, patrolData.formationSpacing, patrolData.formationHeightRatio, out Vector2 slotOffset))
        {
            point = FollowerGoal(flock, anchor, slotOffset);
            spotOffset = slotOffset;
        }
        else
        {
            // 기준 구성원·단독·대형 밖(추적 예약 중): 5차와 같은 식이다.
            if (followState != FollowerState.None) ResetFollow();
            Vector2 holdPoint = AdjustedHoldPoint(anchor, detected);
            Vector2 formationPoint = flocking ? flock.GetFormationPoint(flockMember, holdPoint, patrolData.formationSpacing, patrolData.formationHeightRatio) : holdPoint;
            point = flocking ? ProjectFormation(flock, formationPoint, holdPoint) : holdPoint;
            // 5차 대형(길찾기 우선 끔)의 추종 구성원도 자기 칸 오프셋을 기록한다(추적 넘김의 '자기 유지 자리' 판정용, 이동 계산과 무관).
            EnemyFlockMember leader = flocking ? flock.Leader : null;
            spotOffset = leader != null && leader != flockMember && formationPoint != holdPoint ? formationPoint - leader.Position : Vector2.zero;
        }
        if (!detected && Mathf.Abs(point.x - transform.position.x) > .05f)
            controller.FacingUpdate(point.x > transform.position.x ? 1 : -1);
        SteerTowards(point + UpdateWander(), MaxSpeed);
        bool unreachable = navigator != null && navigator.ConsumeUnreachable();
        TrackNavigationBlocked(unreachable);
        if (unreachable) OnUnreachable(detected);
    }

    // 회피 보정을 거친 유지 지점이다. 계산은 ResolveHoldPoint(기존 규칙)이고, 여기서 공격 위치 재선정·몸 맞춤을 더하고 캐시한다.
    // applyOverride가 거짓이면(길찾기 우선 무리의 추종 구성원) 재선정을 빼고 무리가 함께 쓰는 유지 지점만 맞춘다.
    private Vector2 AdjustedHoldPoint(Vector2 anchor, bool detected, bool applyOverride = true)
    {
        FlightAvoidanceSettings settings = flightData.avoidance;
        EnemyFlock flock = patrolData.moveAsFlock ? Flock : null;
        int side = ResolveRequestedSide(anchor, flock);
        float now = Time.time;
        if (holdCacheValid && now < holdCacheUntil && holdCacheDetected == detected && holdCacheSide == side
            && holdCacheOverride == applyOverride && (anchor - holdCacheAnchor).sqrMagnitude < 1f)
            return holdCachePoint;

        Vector2 point = ResolveHoldPoint(anchor, detected);
        if (navigator != null && settings.enabled)
        {
            if (detected && applyOverride) point = ApplyAttackOverride(anchor, point, settings, now);
            point = navigator.FitHoldPoint(new Vector2(anchor.x, point.y), point, settings);
        }

        holdCacheValid = settings.holdCacheTime > 0f;
        holdCacheUntil = now + settings.holdCacheTime;
        holdCacheDetected = detected;
        holdCacheOverride = applyOverride;
        holdCacheSide = side;
        holdCacheAnchor = anchor;
        holdCachePoint = point;
        return point;
    }

    // 재선정한 자리가 있으면 그 자리를 쓰고, 사격이 필요한 적은 유지 지점 근처에서 사격선이 막힌 시간을 재 losGrace를 넘으면 다시 고른다.
    // fix6g: 아직 유지 지점(재선정 자리)으로 가는 중이면 그 자리 자체에서 Player까지 사격선이 막혔는지를 잰다. 엄폐 뒤 Player처럼 유지 지점이
    // 처음부터 막힌 자리면 도착한 뒤 losGrace를 더 기다리지 않고 가는 동안 다시 고른다(광선 1회 — 유지 지점 캐시 갱신 때만, 기존과 같은 모터 광선).
    private Vector2 ApplyAttackOverride(Vector2 anchor, Vector2 point, in FlightAvoidanceSettings settings, float now)
    {
        if (hasAttackOverride && (now >= overrideUntil || (anchor - overrideAnchor).sqrMagnitude > 9f))
            hasAttackOverride = false;
        if (hasAttackOverride) point = attackOverride;
        if (!needsLineOfSight || controller.Motor == null) return point;

        Vector2 position = transform.position;
        bool near = (position - point).sqrMagnitude <= HoldNearDistance * HoldNearDistance;
        if (!LineBlocked(near ? position : point, anchor))
        {
            lineBlockedSince = -1f;
            return point;
        }
        if (lineBlockedSince < 0f) lineBlockedSince = now;
        if (now - lineBlockedSince < settings.losGrace) return point;

        lineBlockedSince = -1f;
        return TryReselect(anchor, settings, now, out Vector2 chosen) ? chosen : point;
    }

    // 공격 위치 재선정. 기본 후보(playerOffset 둘레: 높이 offset.y·offset.y − 1.5·1.5 × 거리 offset.x·offset.x − 2)에 사격선이 열린 자리가 없으면
    // 더 가깝고 높은 후보(CoverFallbackOffset)로 한 번 더 찾는다. 출입구 바로 옆 2칸 높이 엄폐 뒤에 선 Player는 기본 후보의 낮은 사격선이 모두 막혀
    // 후보가 하나도 없었고, 그러면 사격선이 막힌 유지 지점에 영영 머물렀다(fix6g — CombatVerticalTunnel·ControlRoom·GiyeokRoom 오른쪽 아래 출입구 + ReinforcedCrate).
    // 질의는 기존 TryFindAttackPosition(이 적의 모터 광선·몸 쓸기, 프레임 질의 몫 안)이며, 기본 후보로 찾으면 예전과 같다.
    private bool TryReselect(Vector2 anchor, in FlightAvoidanceSettings settings, float now, out Vector2 chosen)
    {
        int preferredSide = transform.position.x >= anchor.x ? 1 : -1;
        Vector2 offset = patrolData.playerOffset;
        if (!navigator.TryFindAttackPosition(anchor, offset, patrolData.wallMargin, preferredSide, false, settings, out chosen)
            && !navigator.TryFindAttackPosition(anchor, CoverFallbackOffset(offset), patrolData.wallMargin, preferredSide, false, settings, out chosen))
            return false;
        hasAttackOverride = true;
        attackOverride = chosen;
        overrideAnchor = anchor;
        overrideUntil = now + patrolData.sideSwitchInterval;
        return true;
    }

    // 엄폐 뒤 Player용 재선정 후보 오프셋(TryReselect의 두 번째 탐색). 회귀(DungeonFlockAttackRegression)도 이 값으로 제품 탐색 결과를 기록한다.
    public static Vector2 CoverFallbackOffset(Vector2 offset)
        => new(Mathf.Max(CoverFallbackMinWidth, offset.x - CoverFallbackCloser), offset.y + CoverFallbackRise);

    private bool LineBlocked(Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        return distance > .01f && controller.Motor.RaycastEnvironment(from, delta / distance, distance).collider != null;
    }

    // 짧은 시간에 여러 번 막혔다: 사격하는 적은 공격 위치를 바로 다시 고르고, 그 밖에는(또는 고를 자리가 없으면) 좌우 전환을 앞당긴다.
    // 길찾기 우선 무리의 추종 구성원은 대형을 바로 풀고(Regroup) 다시 판정한다.
    private void OnUnreachable(bool detected)
    {
        holdCacheValid = false;
        bool follower = followState != FollowerState.None;
        if (follower)
        {
            formationFeasibleSince = -1f;
            if (followState == FollowerState.Formation) followState = FollowerState.Regroup;
            followCheckAt = Time.time;
        }
        if (detected && needsLineOfSight && TryReselect(target.position, flightData.avoidance, Time.time, out _))
        {
            if (follower) followState = FollowerState.Override;
            return;
        }
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

    // ── 길찾기 우선 무리(6차) ──────────────────────────────────────────────

    // 추종 구성원의 이번 목표점이다(상태에 따라 대형 칸·가상 칸·재선정 자리).
    private Vector2 FollowerGoal(EnemyFlock flock, Vector2 anchor, Vector2 offset)
    {
        FlightAvoidanceSettings settings = flightData.avoidance;
        float now = Time.time;
        // 무리가 함께 쓰는 유지 지점(재선정 제외). 이 개체만 좌우를 바꿨으면(sideFlip) 그 쪽의 유지 지점이다.
        Vector2 holdPoint = AdjustedHoldPoint(anchor, true, false);
        if (hasAttackOverride && (now >= overrideUntil || (anchor - overrideAnchor).sqrMagnitude > 9f))
            hasAttackOverride = false;
        if (followState == FollowerState.Override && !hasAttackOverride)
        {
            followState = FollowerState.Regroup;
            formationFeasibleSince = -1f;
        }
        if (virtualFlip && now >= virtualFlipUntil) virtualFlip = false;

        Vector2 formationSlot = ProjectFormation(flock, flock.Leader.Position + offset, holdPoint);
        Vector2 formationAxis = blockedSlotAxis;
        blockedSlotAxis = Vector2.zero;
        if (now >= followCheckAt) EvaluateFollow(flock.Leader, formationSlot, anchor, settings, now);

        switch (followState)
        {
            case FollowerState.Override:
                return attackOverride;
            case FollowerState.Formation:
                blockedSlotAxis = formationAxis;
                return formationSlot;
            default:
                return VirtualSlot(holdPoint, offset, settings, now);
        }
    }

    // 상태 판정(holdCacheTime 주기, 첫 판정은 바로): ① 사격선 확인·재선정 ② 대형 가능 여부.
    private void EvaluateFollow(EnemyFlockMember leader, Vector2 formationSlot, Vector2 anchor, in FlightAvoidanceSettings settings, float now)
    {
        bool first = followState == FollowerState.None;
        float interval = Mathf.Max(.05f, settings.holdCacheTime);
        float phase = navigator != null ? Mathf.Repeat(navigator.Order * Golden, 1f) : 0f;
        followCheckAt = now + (first ? interval * (.5f + .5f * phase) : interval);
        if (first)
        {
            followState = FollowerState.Regroup;
            lineBlockedSince = -1f;   // 기준 구성원일 때 재던 시간은 이어 쓰지 않는다.
        }

        // ① 사격하는 적: 이번 목표 근처에서 사격선이 losGrace 넘게 막히면 공격 위치를 다시 고른다(5차 규칙의 추종 구성원 판).
        // fix6g: 고정 목표(가상 칸·재선정 자리)로 가는 중이면 그 자리 자체의 사격선을 잰다(기준 구성원의 ApplyAttackOverride와 같음).
        // 대형 칸은 기준 구성원과 함께 움직이므로 예전처럼 도착(2.5칸 안)한 뒤에만 잰다.
        if (hasAttackOverride) followState = FollowerState.Override;
        if (needsLineOfSight && controller.Motor != null)
        {
            Vector2 goal = followState == FollowerState.Override ? attackOverride
                : followState == FollowerState.Formation || !hasVirtualGoal ? formationSlot : lastVirtualGoal;
            bool fixedGoal = followState == FollowerState.Override || (followState != FollowerState.Formation && hasVirtualGoal);
            Vector2 position = transform.position;
            bool near = (position - goal).sqrMagnitude <= HoldNearDistance * HoldNearDistance;
            if (near ? !LineBlocked(position, anchor) : !fixedGoal || !LineBlocked(goal, anchor)) lineBlockedSince = -1f;
            else
            {
                if (lineBlockedSince < 0f) lineBlockedSince = now;
                if (now - lineBlockedSince >= settings.losGrace)
                {
                    lineBlockedSince = -1f;
                    if (TryReselect(anchor, settings, now, out _)) followState = FollowerState.Override;
                    else
                    {
                        // 다시 고를 자리가 없다: 가상 칸의 좌우를 뒤집어 다른 쪽에서 사격선을 찾는다(좌우 전환 간격 뒤 원래대로).
                        hasAttackOverride = false;
                        virtualFlip = !virtualFlip;
                        virtualFlipUntil = now + patrolData.sideSwitchInterval;
                        followState = FollowerState.Regroup;
                        formationFeasibleSince = -1f;
                    }
                }
            }
        }
        if (followState == FollowerState.Override) { formationFeasibleSince = -1f; return; }

        // ② 대형 가능 = 가상 칸을 뒤집지 않음 ∧ 기준 구성원이 직선·비막힘 ∧ 칸이 가까움 ∧ 칸까지 몸 쓸기가 열림.
        int feasible = virtualFlip ? 0 : FormationFeasible(leader, formationSlot, settings);
        if (feasible < 0) { followCheckAt = now; return; }   // 질의 몫이 모자람: 상태를 그대로 두고 다음 프레임에 다시 본다.
        if (feasible == 0)
        {
            formationFeasibleSince = -1f;
            followState = FollowerState.Regroup;
            return;
        }
        // 처음 판정에서 가능하면 바로 대형(열린 방의 모습은 5차와 같다). 길찾기에서 돌아올 때만 지연을 둔다.
        if (first || followState == FollowerState.Formation) { followState = FollowerState.Formation; return; }
        if (formationFeasibleSince < 0f) formationFeasibleSince = now;
        followState = now - formationFeasibleSince >= patrolData.formationRejoinDelay ? FollowerState.Formation : FollowerState.Regroup;
    }

    // 1 = 대형 가능, 0 = 불가, -1 = 이번 프레임 질의 몫이 모자라 모름.
    private int FormationFeasible(EnemyFlockMember leader, Vector2 slot, in FlightAvoidanceSettings settings)
    {
        if (leader == null || leader.NavigationBlocked) return 0;
        EnemyFlightNavigator leaderNavigator = leader.Navigator;
        if (leaderNavigator != null && leaderNavigator.Mode != EnemyFlightNavigator.NavigationMode.Direct) return 0;
        Vector2 position = transform.position;
        float limit = patrolData.formationSpacing * patrolData.formationRegroupDistance;
        if ((slot - position).sqrMagnitude > limit * limit) return 0;
        if (navigator == null) return 1;
        if (!navigator.TryCheckBodyPath(position, slot, settings, out bool clear)) return -1;
        return clear ? 1 : 0;
    }

    // 유지 지점 기준 가상 칸: 유지 지점 + 고리 오프셋(뒤집었으면 x 반대)을 유지 지점에서 몸으로 쓸어 지형 앞에서 멈춘다.
    private Vector2 VirtualSlot(Vector2 holdPoint, Vector2 offset, in FlightAvoidanceSettings settings, float now)
    {
        if (virtualFlip) offset.x = -offset.x;
        Vector2 slot = holdPoint + offset;
        hasVirtualGoal = true;
        if (navigator == null || !settings.enabled) return lastVirtualGoal = slot;
        if (now >= virtualRatioUntil || (holdPoint - virtualRatioFrom).sqrMagnitude > .25f || (offset - virtualRatioOffset).sqrMagnitude > .25f)
        {
            virtualRatio = navigator.ProjectSlot(holdPoint, slot, settings);
            virtualRatioFrom = holdPoint;
            virtualRatioOffset = offset;
            virtualRatioUntil = now + settings.holdCacheTime;
        }
        if (virtualRatio < 1f)
        {
            float length = offset.magnitude;
            if (length > 1e-4f) blockedSlotAxis = offset / length;
            slot = holdPoint + offset * virtualRatio;
        }
        return lastVirtualGoal = slot;
    }

    private void ResetFollow()
    {
        followState = FollowerState.None;
        followCheckAt = 0f;
        formationFeasibleSince = -1f;
        hasVirtualGoal = false;
        virtualRatioUntil = 0f;
        lineBlockedSince = -1f;
    }

    // 기준 구성원 강등용 막힘 표시: 회피가 직선이 아닌 상태가 leaderBlockedDemote 넘게 이어지거나, 도달 불가가 나면 그만큼 켠다.
    private void TrackNavigationBlocked(bool unreachable)
    {
        if (flockMember == null) return;
        float limit = patrolData.leaderBlockedDemote;
        if (navigator == null || limit <= 0f || !patrolData.formationPathFirst || !patrolData.moveAsFlock)
        {
            flockMember.NavigationBlocked = false;
            return;
        }
        float now = Time.time;
        if (unreachable) unreachableBlockedUntil = now + limit;
        if (navigator.Mode == EnemyFlightNavigator.NavigationMode.Direct) notDirectSince = -1f;
        else if (notDirectSince < 0f) notDirectSince = now;
        flockMember.NavigationBlocked = now < unreachableBlockedUntil || (notDirectSince >= 0f && now - notDirectSince >= limit);
    }

    private void ResetNavigationBlocked()
    {
        notDirectSince = -1f;
        unreachableBlockedUntil = 0f;
        if (flockMember != null) flockMember.NavigationBlocked = false;
    }

    // 대형 칸이 천장·벽 앞으로 당겨진 구성원은 이웃의 분리 힘이 막힌 지형 쪽으로 밀어도 그 성분을 버린다(5차 — 위 칸 Drone이
    // 기준 구성원에게 밀려 천장에 붙은 채 머물지 않게). 옆·반대쪽 분리는 그대로다.
    // 6차: 길찾기 우선 무리가 경로·국소 우회로 나는 동안에는 분리 힘을 pathSeparationScale배로 줄인다(직선 비행은 그대로 — 열린 공간 조향 불변).
    protected override Vector2 FilterSeparation(Vector2 separation)
    {
        if (patrolData.moveAsFlock && patrolData.formationPathFirst && navigator != null
            && navigator.Mode != EnemyFlightNavigator.NavigationMode.Direct)
            separation *= Mathf.Clamp01(patrolData.pathSeparationScale);
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
