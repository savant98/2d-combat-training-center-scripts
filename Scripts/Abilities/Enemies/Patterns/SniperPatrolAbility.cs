using UnityEngine;

// 같은 편은 기존 고리 대형을 따르고 전체 Sniper는 서로 분리한다. 좌우 전환은 기존 Teleporter가 맡는다.
// 장애물 회피(EnemyFlightNavigator, 설정은 데이터의 avoidance): 조향은 돌아갈 점으로 방향만 바꾸고(직선이 열려 있으면 기존 식 그대로),
// 유지 지점은 몸 크기로 맞춰 holdCacheTime 동안 다시 쓰고, 대형 칸은 지형 앞에서 멈춘다.
// 순간이동은 모터가 수평 → 수직 순서로 자르므로 L자 경로가 열린 후보만 쓴다. 유지 지점에서 사격선이 오래 막히거나 갈 수 없으면
// 같은 쪽에서 사격선이 열린 자리를 다시 고르고, 없으면 후퇴 순간이동(감지 전이면 좌우 전환)으로 자리를 바꾼다.
[RequireComponent(typeof(EnemyTeleporter), typeof(EnemyFlockMember))]
public class SniperPatrolAbility : AiAbility
{
    [SerializeField] private AiAbility nextAbility;
    [SerializeField] private Renderer[] teleportRenderers;

    // 유지 지점에서 이 거리(칸) 안에 있을 때만 사격선이 막힌 시간을 잰다.
    private const float HoldNearDistance = 2.5f;

    private Transform target;
    private MoveAbility playerMove;
    private PlayerController playerMoveOwner;
    private EnemyTeleporter teleporter;
    private EnemyFlightNavigator navigator;
    // 유지 지점 보정 캐시: 감지 여부·요청한 쪽·재장전 뒤 유지·후퇴 거리가 같고 기준점이 1칸 안이면 다시 쓴다.
    private bool holdCacheValid;
    private bool holdCacheDetected;
    private bool holdCacheReload;
    private int holdCacheSideIn;
    private int holdCacheSideOut;
    private float holdCacheRetreat;
    private Vector2 holdCacheAnchor;
    private Vector2 holdCachePoint;
    private float holdCacheUntil;
    private float slotRatio = 1f;
    private float slotRatioUntil;
    private Vector2 slotRatioOffset;
    private float lineBlockedSince = -1f;
    private bool hasAttackOverride;
    private Vector2 attackOverride;
    private Vector2 overrideAnchor;
    private float overrideUntil;
    private Vector2 steerVelocity;
    private Vector2 wanderOffset;
    private Vector2 wanderGoal;
    private float nextWanderTime;
    private float nextSwitchTime;
    private float attackWait;
    private float retreatDistance;
    private int side;
    private bool teleporting;
    private bool shotLineCached;
    private float nextShotLineCheck;
    private bool[] rendererStates;
    private bool wasInvincible;
    private bool teleportRequested;
    private bool holdSideAfterReload;
    private EnemyFlockMember flockMember;
    private EnemyFlock formationFlock;
    private string formationKey;
    private bool HasTarget => target != null && target.gameObject.activeInHierarchy
        && (flockMember == null || flockMember.TryDetectPlayer(target.position));

    private SniperPatrolDataSO PatrolData => (SniperPatrolDataSO)data;
    public bool IsTeleporting => teleporting;
    public int PatrolSide => side;
    public EnemyFlock FormationFlock => formationFlock;
    public EnemyFlightNavigator Navigator => navigator;
    public bool HasAttackOverride => hasAttackOverride;

    private void Awake() => navigator = EnemyFlightNavigator.Attach(gameObject);

    // 사격과 재장전이 정상 완료된 뒤 다음 순찰 시작 시 한 번만 수행한다.
    public void RequestTeleportAfterReload() => teleportRequested = true;

    public void ClearPostReloadTeleport()
    {
        teleportRequested = false;
        holdSideAfterReload = false;
        retreatDistance = 0f;
    }

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        nextShotLineCheck = 0f;   // 순찰을 시작할 때마다 첫 확인은 바로 한다
        flockMember = GetComponent<EnemyFlockMember>();
        flockMember?.RememberSpawnPosition();
        AcquireTarget();
        steerVelocity = controller.Velocity;
        wanderOffset = wanderGoal = Vector2.zero;
        nextWanderTime = 0f;
        holdCacheValid = false;
        lineBlockedSince = -1f;
        // 사격 중에 남은 도달 불가 알림은 이번 순찰과 관계없다.
        navigator?.ConsumeUnreachable();
        float baseline = Mathf.Max(PatrolData.patrolDuration, nextAbility != null ? nextAbility.CooldownRemaining : 0f);
        // 기본 쿨타임보다 일찍 쏘지 않으며 개체마다 매 회 최대 1초의 시차를 만든다.
        attackWait = baseline > 0f ? baseline * Random.Range(1f, 1f + PatrolData.attackStartJitter / baseline)
            : Random.Range(0f, PatrolData.attackStartJitter);
        if (!TryRequestedTeleport())
        {
            if (side == 0 && flockMember != null)
            {
                Vector2 anchor = HasTarget ? (Vector2)target.position : flockMember.SpawnPosition;
                side = transform.position.x >= anchor.x ? 1 : -1;
                ScheduleSwitch();
            }
            JoinFormation();
            SetFlockRole(HasTarget ? EnemyFlockRole.Formation : EnemyFlockRole.LocalPatrol);
        }
    }

    protected override void OnWindup(float t) => Patrol();
    protected override void OnExecute(float t) => Patrol();
    protected override bool CanFinishExecute(float elapsed) =>
        !teleporting && HasTarget && elapsed >= attackWait
        && nextAbility != null && (nextAbility is SniperLaserAbility shot ? shot.CanBeginShot && ShotLineReady(shot) : nextAbility.IsReady());

    // 사격선이 지형에 막힌 동안은 레이저를 시작하지 않는다(5차 S1 — 벽에 헛사격하며 멈추지 않고 회피·재선정을 이어 간다).
    // 광선 하나라 순찰 SO attackCheckInterval(기본 0.1초)마다만 다시 묻는다. requireLineOfSight를 끄면 기존처럼 막혀도 쏜다.
    private bool ShotLineReady(SniperLaserAbility shot)
    {
        if (!PatrolData.requireLineOfSight) return true;
        float now = Time.time;
        if (now >= nextShotLineCheck)
        {
            shotLineCached = shot.ShotLineOpen();
            nextShotLineCheck = now + PatrolData.attackCheckInterval;
        }
        return shotLineCached;
    }

    protected override void OnRecoverStart() => controller.Velocity = Vector2.zero;

    protected override void OnStop()
    {
        CancelTeleport();
        controller.Velocity = Vector2.zero;
        if (brain.IsPaused)
        {
            SetFlockRole(EnemyFlockRole.None);
            side = 0;
            ClearPostReloadTeleport();
            hasAttackOverride = false;
            holdCacheValid = false;
        }
        else if (nextAbility != null)
        {
            SetFlockRole(HasTarget ? EnemyFlockRole.Chase : EnemyFlockRole.LocalPatrol);
            brain.ForceNextAbility(HasTarget ? nextAbility : this);
        }
    }

    private void OnDisable()
    {
        CancelTeleport();
        SetFlockRole(EnemyFlockRole.None);
        formationFlock?.Leave(flockMember);
        formationFlock = null;
        formationKey = null;
        side = 0;
        ClearPostReloadTeleport();
        hasAttackOverride = false;
        holdCacheValid = false;
    }

    // 같은 Player면 이동 속도 구성요소를 다시 찾지 않는다.
    private void AcquireTarget()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        target = player != null ? player.transform : null;
        if (player != playerMoveOwner || playerMove == null)
        {
            playerMove = player != null ? player.GetComponent<MoveAbility>() : null;
            playerMoveOwner = player;
        }
    }

    private void Patrol()
    {
        // Teleporter가 한 FixedUpdate에 적용하는 Velocity를 여기서 덮어쓰면 순간이동이 취소된다.
        if (teleporting) return;
        if (target == null || !target.gameObject.activeInHierarchy) AcquireTarget();
        bool detected = HasTarget;
        if (!detected && flockMember == null)
        {
            controller.Velocity = Vector2.zero;
            return;
        }
        if (TryRequestedTeleport()) return;

        Vector2 anchor = detected ? (Vector2)target.position : flockMember.SpawnPosition;
        float dx = anchor.x - transform.position.x;
        if (Mathf.Abs(dx) > 0.01f) controller.FacingUpdate(dx > 0f ? 1 : -1);
        if (side == 0)
        {
            side = transform.position.x >= anchor.x ? 1 : -1;
            ScheduleSwitch();
        }

        if (Time.time >= nextSwitchTime)
        {
            teleportRequested = true;
            if (TryRequestedTeleport()) return;
        }
        int wantedSide = side;
        // 재장전 후 좁은 쪽으로 이동했더라도 곧바로 기존 쪽으로 되돌아가지 않는다.
        Vector2 point = AdjustedHoldPoint(ref wantedSide, detected);
        // 공간 부족으로 반대편을 선택한 경우도 가로질러 비행하지 않고 순간이동한다.
        if (wantedSide != side)
        {
            if (detected)
            {
                teleportRequested = true;
                controller.Velocity = Vector2.zero;
                if (TryRequestedTeleport()) return;
                // 후퇴 순간이동 후보가 없거나 L자 경로가 막혔다: 제자리에 멈추지 않고 그 쪽으로 날아간다(회피가 길을 찾는다).
                side = wantedSide;
                ScheduleSwitch();
            }
            else
            {
                side = wantedSide;
                ScheduleSwitch();
                // 순간이동 L자 경로가 막혔으면 날아서 간다(회피가 길을 찾는다).
                if (TeleportClear(point))
                {
                    BeginTeleport(point);
                    return;
                }
            }
        }
        if (Time.time >= nextSwitchTime) ScheduleSwitch();
        JoinFormation();
        SetFlockRole(detected ? EnemyFlockRole.Formation : EnemyFlockRole.LocalPatrol);
        if (detected && PatrolData.moveAsFlock && formationFlock != null)
            point = ProjectFormation(formationFlock.GetFormationPoint(flockMember, point, PatrolData.formationSpacing, PatrolData.formationHeightRatio), point);

        if (Time.time >= nextWanderTime)
        {
            var unit = Random.insideUnitCircle;
            wanderGoal = Vector2.Scale(unit, PatrolData.wanderRadius);
            nextWanderTime = Time.time + Random.Range(PatrolData.wanderInterval.x, PatrolData.wanderInterval.y);
        }
        wanderOffset = Vector2.Lerp(wanderOffset, wanderGoal,
            1f - Mathf.Exp(-PatrolData.wanderLerpSpeed * Time.deltaTime));
        Vector2 goal = point + wanderOffset;
        Vector2 delta = goal - (Vector2)transform.position;
        float maxSpeed = playerMove != null ? playerMove.MoveSpeed * PatrolData.playerSpeedRatio : PatrolData.fallbackSpeed;
        float speedScale = Mathf.Clamp01(delta.magnitude / Mathf.Max(0.01f, PatrolData.slowingDistance));
        // 직선이 열려 있으면 기존 식 그대로, 막혔으면 돌아갈 점으로 방향만 바꾸고 속도는 최종 목표 거리로 정한다.
        Vector2 aim = navigator != null ? navigator.Resolve(goal, maxSpeed * speedScale, PatrolData.avoidance) : goal;
        Vector2 desired = aim == goal
            ? delta.normalized * maxSpeed * speedScale
            : (aim - (Vector2)transform.position).normalized * maxSpeed * speedScale;
        steerVelocity = Vector2.MoveTowards(steerVelocity, desired, PatrolData.acceleration * Time.deltaTime);
        Vector2 separation = flockMember != null && flockMember.Flock != null
            ? flockMember.Flock.GetSeparation(flockMember, PatrolData.separationDistance, PatrolData.separationStrength, PatrolData.separationSidestep)
            : Vector2.zero;
        // 동시 등장·Teleport로 여러 대가 겹쳐도 이웃 수에 비례해 과속하지 않게 한다.
        controller.Velocity = steerVelocity + Vector2.ClampMagnitude(separation, maxSpeed * 1.5f);
        if (navigator != null && navigator.ConsumeUnreachable()) OnUnreachable(detected);
    }

    // 짧은 시간에 여러 번 막혔다: 감지 뒤면 같은 쪽에서 사격선이 열린 자리를 고르고, 없으면 후퇴 순간이동을 요청한다. 감지 전이면 좌우를 바꾼다.
    private void OnUnreachable(bool detected)
    {
        holdCacheValid = false;
        if (detected)
        {
            if (!TryReselect(target.position, side, Time.time)) teleportRequested = true;
            return;
        }
        side = -side;
        ScheduleSwitch();
    }

    // 회피 보정을 거친 유지 지점이다. 계산은 HoldPoint(기존 규칙)이고, 여기서 공격 위치 재선정·몸 맞춤을 더하고 캐시한다.
    private Vector2 AdjustedHoldPoint(ref int wantedSide, bool detected)
    {
        FlightAvoidanceSettings settings = PatrolData.avoidance;
        Vector2 anchor = detected ? (Vector2)target.position : flockMember.SpawnPosition;
        float now = Time.time;
        if (holdCacheValid && now < holdCacheUntil && holdCacheDetected == detected && holdCacheSideIn == wantedSide
            && holdCacheReload == holdSideAfterReload && holdCacheRetreat == retreatDistance
            && (anchor - holdCacheAnchor).sqrMagnitude < 1f)
        {
            wantedSide = holdCacheSideOut;
            return holdCachePoint;
        }

        int requested = wantedSide;
        Vector2 point = HoldPoint(ref wantedSide, !holdSideAfterReload);
        if (navigator != null && settings.enabled)
        {
            if (detected && wantedSide == requested) point = ApplyAttackOverride(anchor, point, wantedSide, settings, now);
            point = navigator.FitHoldPoint(new Vector2(anchor.x, point.y), point, settings);
        }

        holdCacheValid = settings.holdCacheTime > 0f;
        holdCacheUntil = now + settings.holdCacheTime;
        holdCacheDetected = detected;
        holdCacheReload = holdSideAfterReload;
        holdCacheRetreat = retreatDistance;
        holdCacheSideIn = requested;
        holdCacheSideOut = wantedSide;
        holdCacheAnchor = anchor;
        holdCachePoint = point;
        return point;
    }

    private Vector2 ApplyAttackOverride(Vector2 anchor, Vector2 point, int wantedSide, in FlightAvoidanceSettings settings, float now)
    {
        if (hasAttackOverride && (now >= overrideUntil || (anchor - overrideAnchor).sqrMagnitude > 9f
            || (attackOverride.x >= anchor.x ? 1 : -1) != wantedSide))
            hasAttackOverride = false;
        if (hasAttackOverride) point = attackOverride;
        if (controller.Motor == null) return point;

        Vector2 position = transform.position;
        Vector2 toPlayer = anchor - position;
        float distance = toPlayer.magnitude;
        bool near = (position - point).sqrMagnitude <= HoldNearDistance * HoldNearDistance;
        if (!near || distance <= .01f || controller.Motor.RaycastEnvironment(position, toPlayer / distance, distance).collider == null)
        {
            lineBlockedSince = -1f;
            return point;
        }
        if (lineBlockedSince < 0f) lineBlockedSince = now;
        if (now - lineBlockedSince < settings.losGrace) return point;
        lineBlockedSince = -1f;
        return TryReselect(anchor, wantedSide, now) ? attackOverride : point;
    }

    // 같은 쪽에서 몸이 들어가고 사격선이 열리며 갈 수 있는 자리를 고른다(반대쪽은 기존처럼 순간이동이 맡는다).
    private bool TryReselect(Vector2 anchor, int wantedSide, float now)
    {
        if (navigator == null || wantedSide == 0) return false;
        if (!navigator.TryFindAttackPosition(anchor, PatrolData.playerOffset, PatrolData.wallMargin, wantedSide, true, PatrolData.avoidance, out Vector2 chosen))
            return false;
        hasAttackOverride = true;
        attackOverride = chosen;
        overrideAnchor = anchor;
        overrideUntil = now + PatrolData.sideSwitchInterval;
        return true;
    }

    // 대형 고리 칸을 기준 구성원에서 몸으로 쓸어 지형 앞에서 멈춘다(EnemyFlock 계산은 그대로 두고 사용처에서 보정).
    private Vector2 ProjectFormation(Vector2 slot, Vector2 holdPoint)
    {
        EnemyFlockMember leader = formationFlock != null ? formationFlock.Leader : null;
        FlightAvoidanceSettings settings = PatrolData.avoidance;
        if (navigator == null || !settings.enabled || leader == null || leader == flockMember || slot == holdPoint) return slot;
        Vector2 from = leader.Position;
        Vector2 offset = slot - from;
        float now = Time.time;
        if (now >= slotRatioUntil || (offset - slotRatioOffset).sqrMagnitude > .25f)
        {
            slotRatio = navigator.ProjectSlot(from, slot, settings);
            slotRatioOffset = offset;
            slotRatioUntil = now + settings.holdCacheTime;
        }
        return slotRatio >= 1f ? slot : from + offset * slotRatio;
    }

    // 순간이동 L자 경로(수평 → 수직)가 열렸는지. 회피를 끄면 기존처럼 늘 true다.
    private bool TeleportClear(Vector2 point) =>
        navigator == null || navigator.IsTeleportPathClear(transform.position, point, PatrolData.avoidance);

    private void JoinFormation()
    {
        if (flockMember == null || side == 0) return;
        string key = flockMember.EffectiveFlockId + (side < 0 ? ".Formation.Left" : ".Formation.Right");
        if (formationFlock != null && formationKey == key) return;
        formationFlock?.Leave(flockMember);
        formationKey = key;
        formationFlock = EnemyFlock.Join(key, flockMember);
    }

    private void SetFlockRole(EnemyFlockRole role)
    {
        if (flockMember == null || flockMember.Role == role) return;
        flockMember.SetRole(role);
        formationFlock?.MarkLayoutDirty();
    }

    private void ScheduleSwitch() => nextSwitchTime = Time.time + PatrolData.sideSwitchInterval * Random.Range(0.75f, 1.25f);

    private bool TryRequestedTeleport()
    {
        if (!teleportRequested || !HasTarget) return false;
        teleportRequested = false;
        ScheduleSwitch();
        int nextSide = Random.value < .5f ? -1 : 1;
        Vector2 player = target.position;
        Vector2 anchor = player + Vector2.up * AvailableDistance(player, Vector2.up, PatrolData.playerOffset.y);
        float wantedDistance = Mathf.Max(PatrolData.playerOffset.x,
            Mathf.Abs(transform.position.x - player.x) + PatrolData.retreatExtraDistance);
        float distance = AvailableDistance(anchor, Vector2.right * nextSide, wantedDistance);
        float otherDistance = AvailableDistance(anchor, Vector2.right * -nextSide, wantedDistance);
        Vector2 point = anchor + Vector2.right * (nextSide * distance);
        Vector2 otherPoint = anchor + Vector2.right * (-nextSide * otherDistance);
        float currentDistance = Vector2.Distance(transform.position, player);
        bool retreat = Vector2.Distance(point, player) > currentDistance + .1f;
        bool otherRetreat = Vector2.Distance(otherPoint, player) > currentDistance + .1f;
        // 순간이동은 한 물리 틱에 수평 → 수직으로 잘리므로 L자 경로가 열린 후보만 쓴다(막힌 칸막이 너머 후보 제외).
        if (!retreat || !TeleportClear(point))
        {
            if (!otherRetreat || !TeleportClear(otherPoint)) return false;
            nextSide = -nextSide; distance = otherDistance; point = otherPoint;
        }
        retreatDistance = distance;
        side = nextSide;
        holdSideAfterReload = true;
        ScheduleSwitch();
        BeginTeleport(point);
        return true;
    }

    private Vector2 HoldPoint(ref int wantedSide, bool allowSideFallback = true)
    {
        bool detected = HasTarget;
        Vector2 player = detected ? (Vector2)target.position : flockMember.SpawnPosition;
        float height = detected ? AvailableDistance(player, Vector2.up, PatrolData.playerOffset.y) : 0f;
        Vector2 anchor = player + Vector2.up * height;
        float width = AvailableDistance(anchor, Vector2.right * wantedSide, holdSideAfterReload ? retreatDistance : PatrolData.playerOffset.x);
        if (allowSideFallback && width < PatrolData.minSideSpace)
        {
            float other = AvailableDistance(anchor, Vector2.right * -wantedSide, PatrolData.playerOffset.x);
            if (other > width) { wantedSide = -wantedSide; width = other; }
        }
        return anchor + Vector2.right * (wantedSide * width);
    }

    private float AvailableDistance(Vector2 origin, Vector2 direction, float wanted)
    {
        if (controller.Motor == null) return Mathf.Max(0f, wanted);
        var hit = controller.Motor.RaycastEnvironment(origin, direction, wanted + PatrolData.wallMargin);
        return hit.collider != null ? Mathf.Clamp(hit.distance - PatrolData.wallMargin, 0f, wanted) : wanted;
    }

    private void BeginTeleport(Vector2 point)
    {
        JoinFormation();
        SetFlockRole(EnemyFlockRole.None);
        teleporting = true;
        wasInvincible = controller.Health.IsInvincible;
        rendererStates = new bool[teleportRenderers.Length];
        for (int i = 0; i < teleportRenderers.Length; i++)
            rendererStates[i] = teleportRenderers[i] != null && teleportRenderers[i].enabled;
        controller.externalForce = Vector2.zero;
        teleporter.TeleportSequence(point, feedbackPlayer, PatrolData.teleportFeedback, PatrolData.teleportFeedback,
            PatrolData.disappearDelay, PatrolData.appearDelay, () =>
            {
                teleporting = false;
                steerVelocity = Vector2.zero;
                holdCacheValid = false;
                navigator?.ResetPlan();
                BeginAfterTeleport();
            });
    }

    private void BeginAfterTeleport()
    {
        SetFlockRole(HasTarget ? EnemyFlockRole.Formation : EnemyFlockRole.LocalPatrol);
        wanderOffset = wanderGoal = Vector2.zero;
        nextWanderTime = 0f;
    }

    private void CancelTeleport()
    {
        if (teleporter == null) return;
        teleporter.Cancel();
        if (!teleporting) return;
        teleporting = false;
        for (int i = 0; i < teleportRenderers.Length; i++)
            if (teleportRenderers[i] != null) teleportRenderers[i].enabled = rendererStates[i];
        if (controller != null)
        {
            controller.Health.IsInvincible = wasInvincible;
            controller.CanTouchDamageActive(brain != null && !brain.IsPaused);
            controller.Velocity = Vector2.zero;
        }
    }
}
