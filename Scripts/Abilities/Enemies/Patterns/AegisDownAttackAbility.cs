using UnityEngine;

// Aegis 내려찍기. Admin 내려찍기(DownAttackAbility)와 같은 흐름이다:
// 준비 → 플레이어 머리 위로 순간이동 → 체공하며 따라감(Admin보다 길다) → 수직 낙하 → 착지 → 회수.
// 착지 판정(RedCube)·소리·이펙트(준비 경고·포탈 순간이동·예고선·착지 충격)는 Admin과 같은 에셋을 쓰고, 체공만 더 길다.
// - 자리·바닥·천장·벽은 모두 기존 모터 질의(RaycastEnvironment·RaycastFloorBelow·IsBodyBlockedAt)로 찾는다. Physics2D 직접 질의는 없다.
// - 이동은 Velocity만 쓰고 실제 이동·충돌은 RaycastMotor2D가 한다. 순간이동 중에는 Teleporter가 쓰는 Velocity를 덮어쓰지 않는다.
// - Admin처럼 통과 발판 위의 플레이어에게도 내려앉도록, 체공~낙하 동안만 EnemyController.SetOneWayLanding으로 통과 발판 착지를 켠다.
// - 끝나면 다른 공격처럼 재배치(순간이동)로 넘긴다. 중단(브레이크·사망·풀 반환)은 순간이동·외형·무적·외력·예고를 모두 되돌린다.
[RequireComponent(typeof(EnemyTeleporter))]
public sealed class AegisDownAttackAbility : FlyingAbility
{
    private enum Step { Waiting, Teleporting, Hovering }

    [SerializeField] private AiAbility nextAbility;
    [SerializeField] private Renderer[] teleportRenderers;

    private EnemyTeleporter teleporter;
    private AegisDownAttackDataSO settings;
    private bool[] rendererStates;
    private Step step;
    private bool ownsTeleportState, wasInvincible, ownsMovement, previousIgnoreForces, landed;
    private float hoverAnchorY, windupElapsed;
    private LaserPreviewLine preview;

    public override bool IsBodyAttackActive => State == AbilityState.Execute && controller.Velocity.y < -.01f;
    // 검증용 읽기 전용 상태
    public Vector2 TeleportTarget { get; private set; }
    public bool IsTeleporting => State == AbilityState.Windup && step == Step.Teleporting;
    public bool IsHovering => State == AbilityState.Windup && step == Step.Hovering;
    public float HoverStartElapsed { get; private set; }
    public bool Landed => landed;
    public float LandingFloorY { get; private set; }
    public MeleeHitbox LastHitbox { get; private set; }
    public int SlamCount { get; private set; }
    public LaserPreviewLine Preview => preview;

    protected override void Awake()
    {
        base.Awake();
        teleporter = GetComponent<EnemyTeleporter>();
        settings = (AegisDownAttackDataSO)data;
        rendererStates = new bool[teleportRenderers != null ? teleportRenderers.Length : 0];
    }

    public override bool CanBegin()
    {
        AcquireTarget();
        if (!HasTarget || settings.meleeHitbox == null || teleporter == null || controller.Motor == null)
            return false;
        if (Vector2.Distance(target.position, transform.position) > settings.maxStartDistance)
            return false;
        return TryFindSlamPoint(out _, out _);
    }

    protected override void OnWindupStart()
    {
        AcquireTarget();
        step = Step.Waiting;
        landed = false;
        windupElapsed = 0f;
        HoverStartElapsed = -1f;
        LastHitbox = null;
        ReturnPreview();
        SetFlockRole(EnemyFlockRole.None);
        previousIgnoreForces = controller.IgnoreExternalForces;
        ownsMovement = true;
        controller.IgnoreExternalForces = true;
        controller.externalForce = Vector2.zero;
        controller.Velocity = Vector2.zero;
        FaceTarget();
        // 착지 순간 RedCube를 처음 만드느라 끊기지 않게 풀을 미리 채운다.
        if (settings.meleeHitbox != null && WorldEntityPool.Instance != null)
            WorldEntityPool.Instance.Prewarm(settings.meleeHitbox);
    }

    protected override void OnWindup(float elapsed)
    {
        windupElapsed = elapsed;
        switch (step)
        {
            case Step.Waiting:
                controller.Velocity = Vector2.zero;
                if (elapsed < settings.teleportTriggerTime)
                    return;
                if (!HasTarget || !TryFindSlamPoint(out Vector2 point, out _))
                {
                    StopAbility();
                    return;
                }
                BeginTeleport(point);
                return;

            case Step.Teleporting:
                // 순간이동이 준비 시간을 크게 넘겨도 끝나지 않으면(지연·취소) 내려찍지 않고 넘긴다.
                if (elapsed > data.windupTime + 1f)
                    StopAbility();
                return;

            case Step.Hovering:
                Hover(elapsed);
                return;
        }
    }

    private void BeginTeleport(Vector2 point)
    {
        TeleportTarget = point;
        wasInvincible = controller.Health.IsInvincible;
        for (int i = 0; i < rendererStates.Length; i++)
            rendererStates[i] = teleportRenderers[i] != null && teleportRenderers[i].enabled;
        ownsTeleportState = true;
        step = Step.Teleporting;
        teleporter.TeleportSequence(point, feedbackPlayer, settings.teleportFeedback, settings.teleportFeedback,
            settings.disappearDelay, settings.appearDelay, HandleTeleportFinished);
    }

    private void HandleTeleportFinished()
    {
        // 순간이동 도중 브레이크·사망으로 멈췄으면 아무것도 하지 않는다(OnStop이 이미 되돌렸다).
        if (State != AbilityState.Windup || step != Step.Teleporting)
            return;

        RestoreTeleportState();
        controller.Velocity = Vector2.zero;
        // 지형에 막혀 머리 위에 닿지 못했으면 엉뚱한 곳을 내려찍지 않는다.
        if (Vector2.Distance(transform.position, TeleportTarget) > 1.5f)
        {
            StopAbility();
            return;
        }

        step = Step.Hovering;
        HoverStartElapsed = windupElapsed;
        hoverAnchorY = transform.position.y;
        controller.SetOneWayLanding(true);
        BeginFlight();
        FaceTarget();
        SpawnTelegraph();
    }

    // 체공: 마지막 aimLockDuration 전까지 플레이어 x를 따라가고, 그 뒤에는 자리를 고정한 채 살짝 떠올라 내려찍기를 예고한다.
    private void Hover(float elapsed)
    {
        float remaining = data.windupTime - elapsed;
        if (remaining > settings.aimLockDuration && HasTarget)
        {
            SteerTowards(new Vector2(target.position.x, hoverAnchorY), settings.followSpeed);
            FaceTarget();
        }
        else
        {
            controller.Velocity = new Vector2(0f, settings.lockRiseSpeed);
        }

        float hoverTotal = Mathf.Max(.05f, data.windupTime - HoverStartElapsed);
        UpdateTelegraph(Mathf.Max(0f, remaining), hoverTotal);
    }

    protected override bool CanFinishWindup(float elapsed) => step == Step.Hovering && elapsed >= data.windupTime;

    protected override void OnExecuteStart()
    {
        landed = false;
        controller.OnLanded -= HandleLanded;
        controller.OnLanded += HandleLanded;
        controller.Velocity = new Vector2(0f, -settings.slamSpeed);
    }

    protected override void OnExecute(float elapsed)
    {
        controller.Velocity = new Vector2(0f, -settings.slamSpeed);
        UpdateTelegraph(0f, 1f);
    }

    private void HandleLanded()
    {
        if (State == AbilityState.Execute)
            landed = true;
    }

    // 착지하면 바로 회수한다. 이미 바닥에 닿아 있었거나 바닥이 없는 경우(시간 상한)도 끝낸다.
    protected override bool CanFinishExecute(float elapsed)
        => landed || (elapsed > .02f && controller.IsGrounded) || elapsed >= data.executeTime;

    protected override void OnRecoverStart()
    {
        controller.OnLanded -= HandleLanded;
        controller.Velocity = Vector2.zero;
        bool grounded = landed || controller.IsGrounded;
        ReturnPreview();
        if (grounded)
        {
            SlamCount++;
            LandingFloorY = FindLandingFloor(transform.position.x, transform.position.y - settings.bodyRadius, .3f, 1f,
                transform.position.y - settings.bodyRadius);
            // Admin처럼 착지한 바닥 높이에 RedCube를 만든다(Admin은 몸 바닥 0.02칸 위의 회수 지점).
            if (settings.meleeHitbox != null && WorldEntityPool.Instance != null)
            {
                var spawnAt = new Vector2(transform.position.x, LandingFloorY + settings.hitboxFloorOffset);
                var hitbox = WorldEntityPool.Instance.Spawn(settings.meleeHitbox, spawnAt, Quaternion.identity);
                hitbox.Initialize(controller, settings.damageInfo);
                hitbox.SetHitFeedback(null);
                LastHitbox = hitbox;
            }
        }
        controller.SetOneWayLanding(false);
    }

    // 회수: 잠시 바닥에 머문 뒤 easeOutCubic으로 떠오른다(속도는 해석식이라 프레임률과 무관).
    protected override void OnRecover(float elapsed)
    {
        float rise = Mathf.Max(.01f, data.recoverTime - settings.recoverHoldTime);
        if (elapsed < settings.recoverHoldTime || settings.recoverRiseHeight <= 0f)
        {
            controller.Velocity = Vector2.zero;
            return;
        }
        float n = Mathf.Clamp01((elapsed - settings.recoverHoldTime) / rise);
        controller.Velocity = new Vector2(0f, settings.recoverRiseHeight * 3f * (1f - n) * (1f - n) / rise);
    }

    protected override void OnStop()
    {
        Cleanup();
        SetFlockRole(EnemyFlockRole.None);
        ReserveNext(nextAbility);
    }

    private void OnDisable()
    {
        if (controller != null)
            Cleanup();
    }

    private void Cleanup()
    {
        controller.OnLanded -= HandleLanded;
        if (teleporter != null && step == Step.Teleporting)
            teleporter.Cancel();
        RestoreTeleportState();
        ReturnPreview();
        if (ownsMovement)
        {
            controller.IgnoreExternalForces = previousIgnoreForces;
            ownsMovement = false;
        }
        controller.SetOneWayLanding(false);
        controller.Velocity = Vector2.zero;
        step = Step.Waiting;
    }

    private void RestoreTeleportState()
    {
        if (!ownsTeleportState || controller == null)
            return;
        for (int i = 0; i < rendererStates.Length; i++)
            if (teleportRenderers[i] != null) teleportRenderers[i].enabled = rendererStates[i];
        controller.Health.IsInvincible = wasInvincible;
        ownsTeleportState = false;
    }

    // ===== 자리 찾기 =====
    // 플레이어가 선 바닥(통과 발판 포함) 위 hoverHeight에 몸 중심을 두고, 천장·카메라 경계·벽을 피한다.
    // 그 자리에서 수직으로 떨어졌을 때 처음 닿는 바닥이 플레이어의 바닥보다 높으면(사이에 통과 발판) 그 아래로 내린다.
    private bool TryFindSlamPoint(out Vector2 point, out float landingY)
    {
        point = transform.position;
        landingY = 0f;
        var motor = controller.Motor;
        if (!HasTarget || motor == null)
            return false;

        Vector2 player = target.position;
        float feet = PlayerFeetY(player.y);
        float floorY = FindLandingFloor(player.x, feet, .3f, settings.floorSearchDistance, float.NaN);
        if (float.IsNaN(floorY))
            return false;

        float radius = settings.bodyRadius;
        float centerY = floorY + settings.hoverHeight;
        var ceiling = motor.RaycastEnvironment(new Vector2(player.x, floorY + .5f), Vector2.up,
            settings.hoverHeight + radius + settings.ceilingMargin);
        if (ceiling.collider != null)
            centerY = Mathf.Min(centerY, ceiling.point.y - radius - settings.ceilingMargin);
        // 카메라 경계는 플레이어가 그 안에 있을 때만 쓴다(방 전환 중처럼 경계가 다른 방을 가리키면 무시).
        if (settings.clampToCameraBounds && EnemyCameraBounds.TryGet(out Bounds bounds)
            && player.x >= bounds.min.x && player.x <= bounds.max.x && player.y >= bounds.min.y && player.y <= bounds.max.y)
            centerY = Mathf.Min(centerY, bounds.max.y - radius - settings.ceilingMargin);

        float x = ClearWalls(player.x, centerY);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            float bottom = centerY - radius;
            landingY = FindLandingFloor(x, bottom, 0f, bottom - floorY + .5f, float.NaN);
            if (float.IsNaN(landingY))
                return false;
            if (landingY <= floorY + .05f)
                break;
            // 머리 위와 플레이어 사이에 통과 발판이 있다: 그 발판 아래로 내려 다시 본다.
            centerY = landingY - .2f - radius - settings.ceilingMargin;
            if (attempt == 1)
                return false;
        }

        if (centerY - radius - landingY < settings.minimumDrop)
            return false;
        point = new Vector2(x, centerY);
        return !motor.IsBodyBlockedAt(point - (Vector2)transform.position);
    }

    private float ClearWalls(float x, float y)
    {
        var motor = controller.Motor;
        float reach = settings.bodyRadius + settings.wallMargin;
        var origin = new Vector2(x, y);
        var left = motor.RaycastEnvironment(origin, Vector2.left, reach);
        var right = motor.RaycastEnvironment(origin, Vector2.right, reach);
        if (left.collider != null && right.collider != null)
            return (left.point.x + right.point.x) * .5f;
        if (left.collider != null)
            return left.point.x + reach;
        if (right.collider != null)
            return right.point.x - reach;
        return x;
    }

    // 몸 바닥이 feet에 있다고 보고 수직으로 떨어졌을 때 처음 닿는 바닥 높이(통과 발판 포함). 없으면 fallback.
    private float FindLandingFloor(float x, float feet, float above, float below, float fallback)
    {
        bool wasRequested = controller.IsOneWayLandingRequested;
        if (!wasRequested) controller.SetOneWayLanding(true);
        var hit = controller.Motor.RaycastFloorBelow(new Vector2(x, feet), above, Mathf.Max(.1f, below));
        if (!wasRequested) controller.SetOneWayLanding(false);
        return hit.collider != null ? hit.point.y : fallback;
    }

    private float PlayerFeetY(float fallbackCenterY)
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        var body = player != null && player.Health != null ? player.Health.GetComponent<Collider2D>() : null;
        return body != null ? body.bounds.min.y : fallbackCenterY - .75f;
    }

    // ===== 예고(Admin 예고선) =====
    // 몸 아래에서 실제로 내려앉을 바닥(통과 발판 포함)까지 그린다. 막는 지형 없이 계산한 길이를 쓴다.
    private void SpawnTelegraph()
    {
        if (WorldEntityPool.Instance == null || settings.previewPrefab == null)
            return;
        float feet = transform.position.y - settings.bodyRadius;
        float floor = FindLandingFloor(transform.position.x, feet, 0f, settings.floorSearchDistance, feet - settings.floorSearchDistance);
        preview = WorldEntityPool.Instance.Spawn(settings.previewPrefab, new Vector2(transform.position.x, feet), Quaternion.identity);
        // 수명은 이 패턴이 관리한다.
        preview.Show(new Vector2(transform.position.x, feet), Vector2.down, feet - floor, settings.previewWidth, 0,
            settings.previewColor, -1f);
    }

    private void UpdateTelegraph(float remaining, float total)
    {
        if (preview == null)
            return;
        float x = transform.position.x;
        float feet = transform.position.y - settings.bodyRadius;
        float floor = FindLandingFloor(x, feet, 0f, settings.floorSearchDistance, feet - settings.floorSearchDistance);
        preview.SetOrigin(new Vector2(x, feet));
        preview.SetLength(Mathf.Max(0f, feet - floor));
        preview.UpdatePreviewVisual(remaining, total);
    }

    private void ReturnPreview()
    {
        if (preview != null && preview.gameObject.activeInHierarchy)
            preview.ForceReturnToPool();
        preview = null;
    }
}
