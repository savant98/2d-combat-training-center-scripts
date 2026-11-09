using UnityEngine;

// 같은 편은 기존 고리 대형을 따르고 전체 Sniper는 서로 분리한다. 좌우 전환은 기존 Teleporter가 맡는다.
[RequireComponent(typeof(EnemyTeleporter), typeof(EnemyFlockMember))]
public class SniperPatrolAbility : AiAbility
{
    [SerializeField] private AiAbility nextAbility;
    [SerializeField] private Renderer[] teleportRenderers;

    private Transform target;
    private MoveAbility playerMove;
    private EnemyTeleporter teleporter;
    private Vector2 steerVelocity;
    private Vector2 wanderOffset;
    private Vector2 wanderGoal;
    private float nextWanderTime;
    private float nextSwitchTime;
    private float attackWait;
    private float retreatDistance;
    private int side;
    private bool teleporting;
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
        flockMember = GetComponent<EnemyFlockMember>();
        flockMember?.RememberSpawnPosition();
        AcquireTarget();
        steerVelocity = controller.Velocity;
        wanderOffset = wanderGoal = Vector2.zero;
        nextWanderTime = 0f;
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
        && nextAbility != null && (nextAbility is SniperLaserAbility shot ? shot.CanBeginShot : nextAbility.IsReady());

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
    }

    private void AcquireTarget()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        target = player != null ? player.transform : null;
        playerMove = player != null ? player.GetComponent<MoveAbility>() : null;
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
        Vector2 point = HoldPoint(ref wantedSide, !holdSideAfterReload);
        // 공간 부족으로 반대편을 선택한 경우도 가로질러 비행하지 않고 순간이동한다.
        if (wantedSide != side)
        {
            if (detected)
            {
                teleportRequested = true;
                controller.Velocity = Vector2.zero;
                TryRequestedTeleport();
                return;
            }
            side = wantedSide;
            ScheduleSwitch();
            BeginTeleport(point);
            return;
        }
        if (Time.time >= nextSwitchTime) ScheduleSwitch();
        JoinFormation();
        SetFlockRole(detected ? EnemyFlockRole.Formation : EnemyFlockRole.LocalPatrol);
        if (detected && PatrolData.moveAsFlock && formationFlock != null)
            point = formationFlock.GetFormationPoint(flockMember, point, PatrolData.formationSpacing, PatrolData.formationHeightRatio);

        if (Time.time >= nextWanderTime)
        {
            var unit = Random.insideUnitCircle;
            wanderGoal = Vector2.Scale(unit, PatrolData.wanderRadius);
            nextWanderTime = Time.time + Random.Range(PatrolData.wanderInterval.x, PatrolData.wanderInterval.y);
        }
        wanderOffset = Vector2.Lerp(wanderOffset, wanderGoal,
            1f - Mathf.Exp(-PatrolData.wanderLerpSpeed * Time.deltaTime));
        Vector2 delta = point + wanderOffset - (Vector2)transform.position;
        float maxSpeed = playerMove != null ? playerMove.MoveSpeed * PatrolData.playerSpeedRatio : PatrolData.fallbackSpeed;
        Vector2 desired = delta.normalized * maxSpeed * Mathf.Clamp01(delta.magnitude / Mathf.Max(0.01f, PatrolData.slowingDistance));
        steerVelocity = Vector2.MoveTowards(steerVelocity, desired, PatrolData.acceleration * Time.deltaTime);
        Vector2 separation = flockMember != null && flockMember.Flock != null
            ? flockMember.Flock.GetSeparation(flockMember, PatrolData.separationDistance, PatrolData.separationStrength, PatrolData.separationSidestep)
            : Vector2.zero;
        // 동시 등장·Teleport로 여러 대가 겹쳐도 이웃 수에 비례해 과속하지 않게 한다.
        controller.Velocity = steerVelocity + Vector2.ClampMagnitude(separation, maxSpeed * 1.5f);
    }

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
            Mathf.Abs(transform.position.x - player.x) + PatrolData.playerOffset.x * .5f);
        float distance = AvailableDistance(anchor, Vector2.right * nextSide, wantedDistance);
        float otherDistance = AvailableDistance(anchor, Vector2.right * -nextSide, wantedDistance);
        Vector2 point = anchor + Vector2.right * (nextSide * distance);
        Vector2 otherPoint = anchor + Vector2.right * (-nextSide * otherDistance);
        float currentDistance = Vector2.Distance(transform.position, player);
        bool retreat = Vector2.Distance(point, player) > currentDistance + .1f;
        bool otherRetreat = Vector2.Distance(otherPoint, player) > currentDistance + .1f;
        if (!retreat && otherRetreat)
        { nextSide = -nextSide; distance = otherDistance; point = otherPoint; }
        else if (!retreat) return false;
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
