using System.Collections.Generic;
using UnityEngine;

// 비행 적(Drone·Swarmer·Bomber·Aegis·Sniper)의 공용 장애물 회피다. 직접 움직이지 않고 '다음에 향할 점(sub-goal)'만 돌려준다.
// 이동은 기존대로 패턴이 Velocity를 정하고 EnemyController.FixedUpdate → RaycastMotor2D.Move가 처리한다.
// 물리 질의는 모두 이 적의 RaycastMotor2D(CastBody·RaycastEnvironment)로 하고 Physics2D를 직접 부르지 않는다.
// - 직선: 목표까지 몸 쓸기가 열려 있으면 목표를 그대로 돌려준다(기존 조향과 비트 단위로 같다).
// - 경로: 막히면 같은 방·같은 몸의 적이 함께 쓰는 격자(EnemyFlightGrid)에서 길을 찾아, 몸 쓸기가 열린 가장 먼 경유점으로 간다.
// - 국소: 길이 없으면 방향 표본을 몸으로 쓸어 목표 쪽·넓은 쪽을 고른다. 목표에서 비껴가는 우회는 한쪽 편을 정해 고수한다(벽 따라가기).
// - 막힘: 일정 시간 거의 못 움직였는데 목표가 멀면 바로 다시 계획한다. 짧은 시간에 여러 번 막히면 '도달 불가'를 알린다
//   (순찰은 좌우 전환을 앞당기고, 사격하는 적은 공격 위치를 다시 고른다).
// 난수를 쓰지 않는다(방향 순서 고정, 동점은 앞 번호). 재계획 시각은 생성 순번으로 나눠 여러 적이 같은 프레임에 몰리지 않게 한다.
// 패턴이 바뀌어도(순찰 → 추적 → 순찰) 경로·막힘 기록을 이어 쓰도록 적 하나에 하나 둔다(Prefab에는 넣지 않고 실행 중에 붙인다).
[DisallowMultipleComponent]
public sealed class EnemyFlightNavigator : MonoBehaviour
{
    public enum NavigationMode { Direct, Path, Local }

    // 목표가 지난 계획보다 이만큼(칸) 옮겨지면 바로 다시 계획한다.
    private const float GoalShiftReplan = 1.5f;
    // 막힘 판정은 목표가 이보다 멀고 지령 속도가 이보다 빠를 때만 한다(제자리 유지·감속 중 제외).
    private const float StuckGoalDistance = 1.5f;
    private const float StuckMinSpeed = 1f;
    private const float WaypointReach = .6f;
    // 이 시간(초) 안에 이만큼 막히면 도달 불가로 본다.
    private const int StrikeLimit = 3;
    private const float StrikeWindow = 3f;
    private const int VisibleWaypointChecks = 6;
    // 회피를 거치지 않는 동작(정지·사격·순간이동) 뒤 다시 부를 때는 막힘 기록을 새로 시작한다.
    private const float ResumeGap = .25f;
    private const float WaypointTolerance = .05f;
    private const float Golden = .618034f;

    // 프레임당 질의 상한(모든 비행 적 합계). 넘치면 그 적의 재계획을 다음 프레임으로 미룬다.
    public const int MaxCastsPerFrame = 240;
    public const int MaxPathSearchesPerFrame = 2;
    public const int GridRefreshPerFrame = 24;
    // 모르는 칸 미리 채우기: 한 격자가 프레임에 쓰는 최대 조사 수. 모든 격자의 채우기를 합쳐 프레임 질의 상한의 절반(FillCastCeiling)을 넘지 않는다.
    public const int GridFillPerFrame = 96;
    private const int FillCastCeiling = MaxCastsPerFrame / 2;
    private const int SearchProbeBudget = 96;
    private const int SearchExpandLimit = 2400;
    private const int AttackCandidateSearches = 2;
    private const int AttackCandidateCasts = 48;

    private static int spawnCounter;
    private static int statFrame = -1;
    private static int castsThisFrame;
    private static int searchesThisFrame;

    // 질의 수 기록(검증·계측용). 격자 조사도 포함한다.
    public static int MaxCastsInFrame { get; private set; }
    public static int MaxSearchesInFrame { get; private set; }
    public static long TotalCasts { get; private set; }
    public static long TotalSearches { get; private set; }
    public static long TotalReplans { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        spawnCounter = 0;
        statFrame = -1;
        castsThisFrame = searchesThisFrame = 0;
        ResetStats();
    }

    public static void ResetStats()
    {
        MaxCastsInFrame = MaxSearchesInFrame = 0;
        TotalCasts = TotalSearches = TotalReplans = 0;
    }

    private static void RollFrame()
    {
        int frame = Time.frameCount;
        if (frame == statFrame) return;
        statFrame = frame;
        castsThisFrame = searchesThisFrame = 0;
    }

    internal static void NoteCast()
    {
        RollFrame();
        castsThisFrame++;
        TotalCasts++;
        if (castsThisFrame > MaxCastsInFrame) MaxCastsInFrame = castsThisFrame;
    }

    private static bool HasCastBudget(int count)
    {
        RollFrame();
        return castsThisFrame + count <= MaxCastsPerFrame;
    }

    // 이 프레임에 남은 미리 채우기 몫(다른 격자·질의가 이미 쓴 만큼 줄어든다).
    private static int FillBudget()
    {
        RollFrame();
        return Mathf.Clamp(FillCastCeiling - castsThisFrame, 0, GridFillPerFrame);
    }

    private static bool TryBeginSearch()
    {
        RollFrame();
        if (searchesThisFrame >= MaxPathSearchesPerFrame) return false;
        searchesThisFrame++;
        TotalSearches++;
        if (searchesThisFrame > MaxSearchesInFrame) MaxSearchesInFrame = searchesThisFrame;
        return true;
    }

    // 적 루트에 하나만 둔다. 비행 패턴이 Awake에서 부른다(Prefab 수정 없음).
    public static EnemyFlightNavigator Attach(GameObject owner)
    {
        if (owner == null) return null;
        return owner.TryGetComponent(out EnemyFlightNavigator navigator) ? navigator : owner.AddComponent<EnemyFlightNavigator>();
    }

    private EnemyController controller;
    private RaycastMotor2D motor;
    private EnemyFlightGrid grid;
    private string bodyKey;
    private int order;

    private bool hasPlan;
    private float nextPlanTime;
    private float nextPathTime;
    // 직전 격자 탐색이 길을 못 찾았는지. 실패 뒤에는 막힘·목표 이동이 아니면 pathInterval까지 다시 찾지 않는다.
    private bool pathSearchFailed;
    private Vector2 planGoal;
    private Vector2 pathGoal;
    private Vector2 subGoal;
    private readonly List<Vector2> path = new(32);
    private readonly List<Vector2> scratchPath = new(32);
    private int pathIndex;
    // 경로 끝(마지막 경유점)에 닿았지만 목표가 직선으로 열리지 않아 그 점을 유지하는 중인지. 이때는 '경로 끝' 재계획을 매 프레임 하지 않는다.
    private bool holdingPathEnd;

    private Vector2 windowStart;
    private float windowStartTime;
    private float lastResolveTime = float.NegativeInfinity;
    private readonly float[] strikes = new float[StrikeLimit];
    private int strikeCount;
    private bool unreachable;

    private int detourSide;
    private float detourUntil;
    private Vector2 lastLocalDirection;

    private readonly Vector2[] candidates = new Vector2[12];
    private readonly float[] candidateKeys = new float[12];

    public NavigationMode Mode { get; private set; } = NavigationMode.Direct;
    public Vector2 SubGoal => subGoal;
    public int Order => order;
    public int ReplanCount { get; private set; }
    public int PathSearchCount { get; private set; }
    public int StuckCount { get; private set; }
    public int UnreachableCount { get; private set; }
    public IReadOnlyList<Vector2> CurrentPath => path;
    public EnemyFlightGrid Grid => grid;

    private void Awake() => order = spawnCounter++;
    private void OnEnable() => ResetPlan();
    private void OnDisable() => ResetPlan();

    // 계획·막힘 기록을 지운다(Pool 재사용·순간이동 뒤). 다음 Resolve에서 처음부터 다시 계획한다.
    public void ResetPlan()
    {
        hasPlan = false;
        Mode = NavigationMode.Direct;
        path.Clear();
        pathIndex = 0;
        holdingPathEnd = false;
        pathSearchFailed = false;
        strikeCount = 0;
        unreachable = false;
        detourSide = 0;
        detourUntil = 0f;
        lastLocalDirection = Vector2.zero;
        lastResolveTime = float.NegativeInfinity;
    }

    private bool EnsureMotor()
    {
        if (motor != null) return true;
        if (controller == null) controller = GetComponentInParent<EnemyController>();
        motor = controller != null ? controller.Motor : null;
        return motor != null;
    }

    // 짧은 시간에 여러 번 막혔으면 한 번 true를 돌려주고 지운다.
    public bool ConsumeUnreachable()
    {
        bool value = unreachable;
        unreachable = false;
        return value;
    }

    // goal로 가려는 이번 프레임의 향할 점이다. commandSpeed는 패턴이 낼 속도(칸/초, 막힘 판정에만 쓴다).
    // 직선이 열려 있으면 goal을 그대로 돌려준다(호출한 쪽은 같은 값이면 기존 식을 그대로 쓴다).
    public Vector2 Resolve(Vector2 goal, float commandSpeed, in FlightAvoidanceSettings settings)
    {
        if (!settings.enabled || !EnsureMotor())
        {
            Mode = NavigationMode.Direct;
            hasPlan = false;
            return goal;
        }

        float now = Time.time;
        Vector2 position = transform.position;
        bool stuck = TrackProgress(position, goal, commandSpeed, settings, now);
        if (Mode == NavigationMode.Path) AdvancePath(position);
        // 경로 끝에 닿으면 바로 다시 본다. 목표가 지형 속이라 끝점을 유지하는 중이면 재계획 간격·막힘·목표 이동 때만 본다(매 프레임 질의 2회 방지).
        bool replan = !hasPlan || stuck || now >= nextPlanTime
            || (goal - planGoal).sqrMagnitude >= GoalShiftReplan * GoalShiftReplan
            || (Mode == NavigationMode.Local && (subGoal - position).sqrMagnitude <= WaypointReach * WaypointReach)
            || (Mode == NavigationMode.Path && pathIndex >= path.Count && !holdingPathEnd);
        if (replan) Replan(position, goal, stuck, settings, now);
        if (grid != null) grid.Tick(motor, GridRefreshPerFrame, FillBudget());
        return Mode == NavigationMode.Direct ? goal : subGoal;
    }

    private bool TrackProgress(Vector2 position, Vector2 goal, float commandSpeed, in FlightAvoidanceSettings s, float now)
    {
        if (now - lastResolveTime > ResumeGap)
        {
            windowStart = position;
            windowStartTime = now;
        }
        lastResolveTime = now;
        if (now - windowStartTime < Mathf.Max(.1f, s.stuckWindow)) return false;

        bool stuck = (position - windowStart).sqrMagnitude < s.stuckDistance * s.stuckDistance
            && (goal - position).sqrMagnitude > StuckGoalDistance * StuckGoalDistance
            && commandSpeed > StuckMinSpeed;
        windowStart = position;
        windowStartTime = now;
        if (!stuck) return false;

        StuckCount++;
        RegisterStrike(now);
        return true;
    }

    private void RegisterStrike(float now)
    {
        if (strikeCount < StrikeLimit) strikes[strikeCount++] = now;
        else
        {
            for (int i = 1; i < StrikeLimit; i++) strikes[i - 1] = strikes[i];
            strikes[StrikeLimit - 1] = now;
        }
        if (strikeCount == StrikeLimit && now - strikes[0] <= StrikeWindow)
        {
            unreachable = true;
            UnreachableCount++;
            strikeCount = 0;
        }
    }

    private void AdvancePath(Vector2 position)
    {
        while (pathIndex < path.Count && (path[pathIndex] - position).sqrMagnitude <= WaypointReach * WaypointReach)
            pathIndex++;
        if (pathIndex < path.Count) subGoal = path[pathIndex];
    }

    private void Replan(Vector2 position, Vector2 goal, bool stuck, in FlightAvoidanceSettings s, float now)
    {
        // 이번 프레임의 질의 몫이 찼으면 지난 계획을 그대로 두고 다음 프레임에 다시 본다.
        if (!HasCastBudget(1)) return;
        bool first = !hasPlan;
        hasPlan = true;
        planGoal = goal;
        ReplanCount++;
        TotalReplans++;
        float interval = Mathf.Max(.05f, s.replanInterval);
        // 첫 계획 뒤의 시각을 생성 순번(황금비 간격)으로 나눠 여러 적의 재계획이 한 프레임에 몰리지 않게 한다.
        nextPlanTime = now + (first ? interval * Mathf.Repeat(order * Golden, 1f) : interval);

        float clearance = Mathf.Max(0f, s.clearance);
        if (!stuck && IsClear(position, goal, clearance))
        {
            SetDirect(goal);
            return;
        }
        if (s.usePath && TryPathStep(position, goal, stuck, s, now)) return;
        if (TrySampleLocal(position, goal, s, now)) return;
        // 갈 수 있는 방향이 없으면 기존처럼 목표로 민다. 질의 몫이 모자라 국소 표본을 못 본 것이면 다음 프레임에 다시 본다.
        if (!HasCastBudget(Mathf.Clamp(s.sampleCount, 4, 32))) nextPlanTime = now + .02f;
        SetDirect(goal);
    }

    private void SetDirect(Vector2 goal)
    {
        Mode = NavigationMode.Direct;
        subGoal = goal;
    }

    private bool TryPathStep(Vector2 position, Vector2 goal, bool stuck, in FlightAvoidanceSettings s, float now)
    {
        bool goalShifted = (goal - pathGoal).sqrMagnitude >= GoalShiftReplan * GoalShiftReplan;
        // 직전 탐색이 길을 못 찾았으면 막힘·목표 이동이 아닌 한 pathInterval까지 다시 찾지 않고 국소 표본으로 간다
        // (실패 뒤에는 Mode가 Local·Direct라 아래 조건만으로는 재계획마다 A*를 다시 돌린다 — 설계는 '경로 주기 + 막힘 즉시').
        if (pathSearchFailed && !stuck && !goalShifted && now < nextPathTime) return false;
        bool search = Mode != NavigationMode.Path || stuck || path.Count == 0 || now >= nextPathTime || goalShifted;
        if (search)
        {
            if (!HasCastBudget(SearchProbeBudget + VisibleWaypointChecks) || !TryBeginSearch())
            {
                // 이번 프레임의 경로 탐색 몫(MaxPathSearchesPerFrame)이 찼다: 지난 경로가 있으면 이어 쓰고, 없으면 국소 회피로 버틴 뒤 곧 다시 찾는다.
                nextPathTime = now;
                nextPlanTime = Mathf.Min(nextPlanTime, now + .05f);
                if (Mode != NavigationMode.Path || path.Count == 0) return false;
            }
            else
            {
                PathSearchCount++;
                // 막혀서 다시 찾는 것이면 따라가던 구간의 격자를 먼저 실제와 맞춘다(불일치 즉시 갱신).
                if (stuck && grid != null && Mode == NavigationMode.Path && pathIndex < path.Count)
                    grid.ReprobeSegment(position, path[pathIndex], motor, 8);
                AcquireGrid(position, goal);
                bool found = grid != null && grid.FindPath(position, goal, motor, SearchProbeBudget, SearchExpandLimit, path);
                pathSearchFailed = !found;
                pathGoal = goal;
                nextPathTime = now + Mathf.Max(.1f, s.pathInterval);
                pathIndex = 0;
                if (!found)
                {
                    path.Clear();
                    return false;
                }
            }
        }
        return PickVisibleWaypoint(position, now);
    }

    // 지금 자리에서 몸 쓸기가 열린 가장 먼 경유점(앞으로 최대 6개)을 고른다.
    private bool PickVisibleWaypoint(Vector2 position, float now)
    {
        holdingPathEnd = false;
        if (path.Count == 0) return false;
        // 경로 끝까지 왔는데 목표가 직선으로 열리지 않았다(목표가 몸 기준으로 지형 속 → FindPath가 고른 가까운 빈 칸이 끝점).
        // 마지막 경유점을 유지한다(pathIndex == path.Count 그대로면 아래 for가 돌지 않고 path[pathIndex]가 범위를 넘는다).
        bool reachedEnd = pathIndex >= path.Count;
        if (reachedEnd) pathIndex = path.Count - 1;
        while (pathIndex < path.Count - 1 && (path[pathIndex] - position).sqrMagnitude <= WaypointReach * WaypointReach)
            pathIndex++;
        int last = Mathf.Min(path.Count - 1, pathIndex + VisibleWaypointChecks - 1);
        for (int i = last; i >= pathIndex; i--)
        {
            if (!IsClear(position, path[i], WaypointTolerance)) continue;
            pathIndex = i;
            Mode = NavigationMode.Path;
            subGoal = path[i];
            holdingPathEnd = reachedEnd;
            return true;
        }

        // 가장 가까운 경유점도 몸으로 막혔다 = 격자가 실제와 다르다. 그 구간을 바로 다시 조사하고 곧 경로를 다시 찾는다.
        if (grid != null) grid.ReprobeSegment(position, path[pathIndex], motor, 8);
        path.Clear();
        pathIndex = 0;
        nextPathTime = now;
        return false;
    }

    // 방향 표본(고정 순서)을 몸으로 쓸어 목표 쪽·넓은 쪽 점수가 가장 높은 방향의 앞 점을 고른다.
    private bool TrySampleLocal(Vector2 position, Vector2 goal, in FlightAvoidanceSettings s, float now)
    {
        int count = Mathf.Clamp(s.sampleCount, 4, 32);
        if (!HasCastBudget(count)) return false;
        float look = Mathf.Max(.5f, s.lookAhead);
        float clearance = Mathf.Max(0f, s.clearance);
        Vector2 toGoal = goal - position;
        Vector2 goalDirection = toGoal.sqrMagnitude > 1e-6f ? toGoal.normalized : Vector2.up;
        bool committed = now < detourUntil && detourSide != 0;
        float bestScore = float.NegativeInfinity;
        Vector2 bestDirection = Vector2.zero;
        float bestFree = 0f;
        for (int i = 0; i < count; i++)
        {
            float angle = i * (Mathf.PI * 2f / count);
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            RaycastHit2D hit = Cast(position, direction, look);
            float free = hit.collider != null ? hit.distance : look;
            if (free < clearance + .2f) continue;
            float dot = Vector2.Dot(direction, goalDirection);
            float score = dot + .5f * free / look;
            if (committed && SideOf(goalDirection, direction) == detourSide) score += .6f;
            if (lastLocalDirection != Vector2.zero && Vector2.Dot(lastLocalDirection, direction) < -.5f) score -= .3f;
            if (score <= bestScore + 1e-5f) continue;
            bestScore = score;
            bestDirection = direction;
            bestFree = free;
        }
        if (bestDirection == Vector2.zero) return false;

        // 목표에서 60도 넘게 비껴가는 우회는 그 편을 detourCommit 동안 고수한다(좌우로 흔들리며 제자리에 머무는 것을 막는다).
        // 고수 중인데 가산(+0.6)을 받고도 반대편이 이겼다면 고수하던 편이 막힌 것이다. 편을 바꿔 막다른 편으로 되돌아가지 않게 한다
        // (정확히 뒤쪽은 SideOf가 +1로 치우치므로 편을 바꾸는 근거로 쓰지 않는다).
        if (Vector2.Dot(bestDirection, goalDirection) < .5f)
        {
            float cross = goalDirection.x * bestDirection.y - goalDirection.y * bestDirection.x;
            int side = cross >= 0f ? 1 : -1;
            if (!committed || (side != detourSide && Mathf.Abs(cross) > .1f)) detourSide = side;
            detourUntil = now + Mathf.Max(0f, s.detourCommit);
        }
        lastLocalDirection = bestDirection;
        Mode = NavigationMode.Local;
        subGoal = position + bestDirection * Mathf.Clamp(bestFree - clearance, .2f, look);
        return true;
    }

    private static int SideOf(Vector2 forward, Vector2 direction) => forward.x * direction.y - forward.y * direction.x >= 0f ? 1 : -1;

    private void AcquireGrid(Vector2 position, Vector2 goal)
    {
        bodyKey ??= BodyKeyOf(controller);
        Rect area;
        // 던전은 '연결 공간 + 사방 3칸', 다른 방은 카메라 구속 범위를 방으로 본다. 출발·목표가 그 밖이면 둘을 감싼 영역을 쓴다.
        if (EnemyCameraBounds.TryGet(out Bounds bounds))
        {
            area = Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y);
            if (!area.Contains(position) || !area.Contains(goal)) area = LocalArea(position, goal);
        }
        else area = LocalArea(position, goal);
        grid = EnemyFlightGrid.Acquire(bodyKey, area, position, goal);
    }

    private static Rect LocalArea(Vector2 a, Vector2 b)
    {
        const float pad = 12f, snap = 8f;
        float minX = Mathf.Floor((Mathf.Min(a.x, b.x) - pad) / snap) * snap, minY = Mathf.Floor((Mathf.Min(a.y, b.y) - pad) / snap) * snap;
        float maxX = Mathf.Ceil((Mathf.Max(a.x, b.x) + pad) / snap) * snap, maxY = Mathf.Ceil((Mathf.Max(a.y, b.y) + pad) / snap) * snap;
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    // 같은 Prefab(같은 몸)의 적이 격자를 나눠 쓰도록 이름에서 Pool 복제 표시를 뺀 것을 쓴다.
    private static string BodyKeyOf(Component owner)
    {
        if (owner == null) return string.Empty;
        string name = owner.gameObject.name;
        int clone = name.IndexOf("(Clone)", System.StringComparison.Ordinal);
        return clone >= 0 ? name.Substring(0, clone).Trim() : name;
    }

    private RaycastHit2D Cast(Vector2 from, Vector2 direction, float distance)
    {
        NoteCast();
        return motor.CastBody(from, direction, distance);
    }

    // from → to를 몸으로 쓸어 끝(tolerance 안쪽)까지 막히지 않는지.
    private bool IsClear(Vector2 from, Vector2 to, float tolerance)
    {
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 1e-4f) return true;
        RaycastHit2D hit = Cast(from, delta / distance, distance);
        return hit.collider == null || hit.distance >= distance - tolerance;
    }

    private bool LegClear(Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        return distance < 1e-4f || Cast(from, delta / distance, distance).collider == null;
    }

    // ── 유지 지점·대형·순간이동 보정(패턴이 부른다) ─────────────────────────────

    // 유지 지점 몸 맞춤·천장 상한. basePoint(앵커 위 높이)에서 point(옆으로 뻗은 자리)까지 몸으로 쓸어 막히면 그 앞에서 멈추고,
    // 그 자리의 천장·바닥에 몸이 clearance보다 가까우면 위아래로 띄운다. 지형에 닿지 않는 열린 자리는 point를 그대로 돌려준다.
    public Vector2 FitHoldPoint(Vector2 basePoint, Vector2 point, in FlightAvoidanceSettings s)
    {
        if (!s.enabled || !EnsureMotor() || !HasCastBudget(10)) return point;
        float clearance = Mathf.Max(0f, s.clearance);
        if (!TryFreeSpot(basePoint, out Vector2 start)) return point;
        Vector2 fitted = point;
        if (start != basePoint) fitted += start - basePoint;

        Vector2 delta = fitted - start;
        float distance = delta.magnitude;
        if (distance > 1e-4f)
        {
            RaycastHit2D side = Cast(start, delta / distance, distance + clearance);
            if (side.collider != null && side.distance < distance + clearance)
                fitted = start + delta / distance * Mathf.Max(0f, side.distance - clearance);
        }

        RaycastHit2D up = Cast(fitted, Vector2.up, clearance);
        RaycastHit2D down = Cast(fitted, Vector2.down, clearance);
        bool ceiling = up.collider != null && up.distance < clearance;
        bool floor = down.collider != null && down.distance < clearance;
        if (ceiling && floor) fitted.y += (up.distance - down.distance) * .5f;
        else if (ceiling) fitted.y -= clearance - up.distance;
        else if (floor) fitted.y += clearance - down.distance;
        return fitted;
    }

    // 길찾기 우선 무리(6차): from → to를 몸으로 쓸어 to(clearance 안쪽)까지 열려 있는지 clear에 넣는다(질의 1회).
    // 회피가 꺼졌거나 모터가 없으면 열린 것으로 본다. 이번 프레임 질의 몫이 모자라면 false를 돌려준다(부른 쪽이 다음 프레임에 다시 본다).
    public bool TryCheckBodyPath(Vector2 from, Vector2 to, in FlightAvoidanceSettings s, out bool clear)
    {
        clear = true;
        if (!s.enabled || !EnsureMotor()) return true;
        if (!HasCastBudget(1)) return false;
        clear = IsClear(from, to, Mathf.Max(0f, s.clearance));
        return true;
    }

    // 대형 칸 지형 투영: from(기준 구성원)에서 slot까지 몸으로 쓸어 열린 비율(0~1)이다. 막히면 지형 앞 clearance에서 멈춘다.
    public float ProjectSlot(Vector2 from, Vector2 slot, in FlightAvoidanceSettings s)
    {
        if (!s.enabled || !EnsureMotor() || !HasCastBudget(1)) return 1f;
        Vector2 delta = slot - from;
        float distance = delta.magnitude;
        if (distance < 1e-4f) return 1f;
        RaycastHit2D hit = Cast(from, delta / distance, distance);
        if (hit.collider == null) return 1f;
        return Mathf.Clamp01(Mathf.Max(0f, hit.distance - Mathf.Max(0f, s.clearance)) / distance);
    }

    // 순간이동(EnemyTeleporter, 한 물리 틱 이동)은 모터가 수평(지금 높이) → 수직(목표 x) 순서로 자르므로 그 L자 두 다리가 모두 열려야 도착한다.
    public bool IsTeleportPathClear(Vector2 from, Vector2 to, in FlightAvoidanceSettings s)
    {
        if (!s.enabled || !EnsureMotor()) return true;
        var corner = new Vector2(to.x, from.y);
        return LegClear(from, corner) && LegClear(corner, to);
    }

    // 공격 위치 재선정: Player 둘레 후보(쪽 × 높이 offset.y·offset.y − 1.5·1.5 × 거리 offset.x·offset.x − 2) 가운데
    // 몸이 들어가고 Player와 사격선이 열린 자리를 지금 자리에서 가까운 순서로 보고, 직선 또는 격자 경로로 갈 수 있는 첫 자리를 고른다.
    public bool TryFindAttackPosition(Vector2 player, Vector2 offset, float wallMargin, int preferredSide, bool sameSideOnly,
        in FlightAvoidanceSettings s, out Vector2 point)
    {
        point = default;
        if (!s.enabled || !EnsureMotor() || !HasCastBudget(AttackCandidateCasts)) return false;
        Vector2 position = transform.position;
        int side0 = preferredSide < 0 ? -1 : 1;
        int count = 0;
        for (int sideIndex = 0; sideIndex < (sameSideOnly ? 1 : 2); sideIndex++)
        {
            int side = sideIndex == 0 ? side0 : -side0;
            for (int h = 0; h < 3; h++)
            {
                float wantedHeight = h == 0 ? offset.y : h == 1 ? offset.y - 1.5f : 1.5f;
                float height = Available(player, Vector2.up, Mathf.Max(0f, wantedHeight), wallMargin);
                Vector2 anchor = player + Vector2.up * height;
                for (int d = 0; d < 2; d++)
                {
                    float wantedWidth = Mathf.Max(1f, d == 0 ? offset.x : offset.x - 2f);
                    float width = Available(anchor, Vector2.right * side, wantedWidth, wallMargin);
                    if (width < 1.5f) continue;
                    Vector2 candidate = anchor + Vector2.right * (side * width);
                    if (Cast(candidate, Vector2.up, 0f).collider != null) continue;
                    Vector2 toPlayer = player - candidate;
                    float lineLength = toPlayer.magnitude;
                    NoteCast();
                    if (lineLength > .01f && motor.RaycastEnvironment(candidate, toPlayer / lineLength, lineLength).collider != null) continue;
                    // 같은 자리 중복은 건너뛴다.
                    bool duplicate = false;
                    for (int i = 0; i < count && !duplicate; i++) duplicate = (candidates[i] - candidate).sqrMagnitude < .01f;
                    if (duplicate || count >= candidates.Length) continue;
                    candidates[count] = candidate;
                    candidateKeys[count] = (candidate - position).sqrMagnitude;
                    count++;
                }
            }
        }

        // 가까운 순서(동점은 앞 번호) — 삽입 정렬이라 같은 입력이면 같은 순서다.
        for (int i = 1; i < count; i++)
        {
            Vector2 c = candidates[i];
            float key = candidateKeys[i];
            int j = i - 1;
            while (j >= 0 && candidateKeys[j] > key)
            {
                candidates[j + 1] = candidates[j];
                candidateKeys[j + 1] = candidateKeys[j];
                j--;
            }
            candidates[j + 1] = c;
            candidateKeys[j + 1] = key;
        }

        int searches = 0;
        float clearance = Mathf.Max(0f, s.clearance);
        for (int i = 0; i < count; i++)
        {
            Vector2 candidate = candidates[i];
            if (IsClear(position, candidate, clearance))
            {
                point = candidate;
                return true;
            }
            if (!s.usePath || searches >= AttackCandidateSearches || !HasCastBudget(SearchProbeBudget) || !TryBeginSearch()) continue;
            searches++;
            PathSearchCount++;
            AcquireGrid(position, candidate);
            if (grid != null && grid.FindPath(position, candidate, motor, SearchProbeBudget, SearchExpandLimit, scratchPath))
            {
                point = candidate;
                return true;
            }
        }
        return false;
    }

    // 검증용: 지금 자리에서 goal까지 격자 경로를 찾아 output에 넣는다(이동 계획은 바꾸지 않는다).
    public bool FindPathForCheck(Vector2 goal, List<Vector2> output)
    {
        output.Clear();
        if (!EnsureMotor()) return false;
        Vector2 position = transform.position;
        AcquireGrid(position, goal);
        if (grid == null) return false;
        // 같은 격자 지식에서 늘 같은 경로가 나오는지 보도록 모르는 칸을 먼저 모두 조사한다(실행 중에는 Tick이 몇 초에 걸쳐 채운다).
        grid.FillAllForCheck(motor);
        return grid.FindPath(position, goal, motor, SearchProbeBudget, SearchExpandLimit, output);
    }

    // 지형과 겹치는 기준점은 조금씩 올리거나 내려서 몸이 들어가는 자리를 찾는다(고정 순서).
    private static readonly float[] SpotOffsets = { 0f, .5f, 1f, 1.5f, 2f, -.5f, -1f };
    private bool TryFreeSpot(Vector2 point, out Vector2 free)
    {
        for (int i = 0; i < SpotOffsets.Length; i++)
        {
            Vector2 candidate = point + Vector2.up * SpotOffsets[i];
            if (Cast(candidate, Vector2.up, 0f).collider != null) continue;
            free = candidate;
            return true;
        }
        free = point;
        return false;
    }

    private float Available(Vector2 origin, Vector2 direction, float wanted, float margin)
    {
        if (wanted <= 0f) return 0f;
        NoteCast();
        RaycastHit2D hit = motor.RaycastEnvironment(origin, direction, wanted + margin);
        return hit.collider != null ? Mathf.Clamp(hit.distance - margin, 0f, wanted) : wanted;
    }
}
