using UnityEngine;

public class ChargeAndFireAbility : AiAbility
{
    [Header("Spark")]
    [SerializeField] private Transform sparkPoint;
    private enum ExecutePhase
    {
        None,
        Slash,
        SlashPause,
        ReverseDash,
        BrakeAndFire
    }

    private EnemyTeleporter teleporter;
    private Transform playerTr;

    private bool facingLocked;
    private int lockedFacing;

    private bool teleportTriggered;
    private bool teleportFinished;
    private Vector2 teleportTarget;

    private ExecutePhase executePhase = ExecutePhase.None;
    private float phaseTimer;

    private Vector2 slashDirection;
    private Vector2 reverseDashDirection;
    private Vector2 currentExecuteVelocity;

    private bool slashHitGround;
    private bool laserFired;

    private float sparkTimer;

    private LaserBeam activeLaser;
    private LaserPreviewLine activePreview;
    // 코어. 유도탄·레이저를 쏘는 순간 쏘는 방향을 알려 반동·섬광을 보여 준다(표시 전용, 없으면 건너뛴다).
    private NightmareCore nightmareCore;
    private readonly System.Collections.Generic.List<ConstructorHomingMissile> firedMissiles = new System.Collections.Generic.List<ConstructorHomingMissile>();

    /// <summary>가장 최근 베기에서 쏜 유도탄(발사 순서)과 그 출발점. 검증용.</summary>
    public System.Collections.Generic.IReadOnlyList<ConstructorHomingMissile> FiredMissiles => firedMissiles;
    public Vector2 MissileOrigin { get; private set; }

    private ChargeAndFireDataSO ChargeData => (ChargeAndFireDataSO)data;

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        playerTr = GameManager.Instance.Player.transform;
        if (nightmareCore == null) nightmareCore = GetComponentInChildren<NightmareCore>(true);

        facingLocked = false;
        lockedFacing = controller.Facing;

        teleportTriggered = false;
        teleportFinished = false;
        teleportTarget = Vector2.zero;

        executePhase = ExecutePhase.None;
        phaseTimer = 0f;

        slashDirection = Vector2.zero;
        reverseDashDirection = Vector2.zero;
        currentExecuteVelocity = Vector2.zero;

        slashHitGround = false;
        laserFired = false;
        sparkTimer = 0f;

        ReturnLaser();
        ReturnPreview();

        controller.Velocity = Vector2.zero;

        LockFacingToPlayerOnce();
    }

    protected override void OnWindup(float t)
    {
        if (!teleportTriggered && t < ChargeData.teleportTriggerTime)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!teleportTriggered && t >= ChargeData.teleportTriggerTime)
        {
            teleportTriggered = true;
            controller.Velocity = Vector2.zero;

            teleportTarget = GetTeleportTarget();

            teleporter.TeleportSequence(
                teleportTarget,
                feedbackPlayer,
                ChargeData.teleportStartFx,
                ChargeData.teleportEndFx,
                ChargeData.teleportDisappearDelay,
                ChargeData.teleportAppearDelay,
                () =>
                {
                    teleportFinished = true;
                    controller.Velocity = Vector2.zero;
                    controller.FacingUpdate(lockedFacing);

                    slashDirection = GetSlashDirection();
                    SpawnPreview();
                    UpdatePreview();
                });

            return;
        }

        if (!teleportFinished)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        controller.Velocity = Vector2.zero;

        float remaining = Mathf.Max(0f, data.windupTime - t);
        UpdatePreview();
        activePreview?.UpdatePreviewVisual(remaining, Mathf.Max(0.0001f, data.windupTime));
    }

    protected override void OnExecuteStart()
    {
        controller.FacingUpdate(lockedFacing);

        slashDirection = GetSlashDirection();
        reverseDashDirection = Vector2.zero;

        currentExecuteVelocity = Vector2.zero;
        slashHitGround = false;
        laserFired = false;
        sparkTimer = 0f;

        EnterExecutePhase(ExecutePhase.Slash);
    }

    protected override void OnExecute(float t)
    {
        phaseTimer += Time.deltaTime;

        switch (executePhase)
        {
            case ExecutePhase.Slash:
                UpdateSlashPhase();
                break;

            case ExecutePhase.SlashPause:
                UpdateSlashPausePhase();
                break;

            case ExecutePhase.ReverseDash:
                UpdateReverseDashPhase();
                break;

            case ExecutePhase.BrakeAndFire:
                UpdateBrakeAndFirePhase();
                break;
        }

        UpdateActiveLaser();
    }

    protected override void OnRecoverStart()
    {
        controller.Velocity = Vector2.zero;
        currentExecuteVelocity = Vector2.zero;

        ReturnPreview();
    }

    protected override void OnRecover(float t)
    {
        controller.Velocity = Vector2.zero;
    }

    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;
        currentExecuteVelocity = Vector2.zero;

        ReturnLaser();
        ReturnPreview();
    }


    private void EnterExecutePhase(ExecutePhase nextPhase)
    {
        executePhase = nextPhase;
        phaseTimer = 0f;

        switch (nextPhase)
        {
            case ExecutePhase.Slash:
                currentExecuteVelocity = slashDirection * ChargeData.slashSpeed;
                controller.Velocity = currentExecuteVelocity;
                ReturnPreview();
                break;

            case ExecutePhase.SlashPause:
                currentExecuteVelocity = Vector2.zero;
                controller.Velocity = Vector2.zero;
                break;

            case ExecutePhase.ReverseDash:
                reverseDashDirection = GetReverseDashDirectionToPlayer();
                controller.FacingUpdate(reverseDashDirection.x >= 0f ? -1 : 1);

                currentExecuteVelocity = new Vector2(
                    reverseDashDirection.x * ChargeData.reverseDashSpeed,
                    0f);

                controller.Velocity = currentExecuteVelocity;
                sparkTimer = 0f;
                break;

            case ExecutePhase.BrakeAndFire:
                controller.Velocity = currentExecuteVelocity;
                break;
        }
    }

    private void UpdateSlashPhase()
    {
        if (!slashHitGround)
        {
            currentExecuteVelocity = slashDirection * ChargeData.slashSpeed;
            controller.Velocity = currentExecuteVelocity;

            UpdatePreview();

            if (HasReachedGroundDuringSlash())
            {
                slashHitGround = true;
                currentExecuteVelocity = Vector2.zero;
                controller.Velocity = Vector2.zero;

                SpawnSlashHitbox();
                FireSlashMissiles();
                ReturnPreview();

                EnterExecutePhase(ExecutePhase.SlashPause);
                return;
            }
        }

        if (phaseTimer >= ChargeData.slashMaxDuration)
        {
            slashHitGround = true;
            currentExecuteVelocity = Vector2.zero;
            controller.Velocity = Vector2.zero;

            SpawnSlashHitbox();
            FireSlashMissiles();
            ReturnPreview();

            EnterExecutePhase(ExecutePhase.SlashPause);
        }
    }

    private void UpdateSlashPausePhase()
    {
        currentExecuteVelocity = Vector2.zero;
        controller.Velocity = Vector2.zero;

        if (phaseTimer >= ChargeData.slashPauseDuration)
            EnterExecutePhase(ExecutePhase.ReverseDash);
    }

    private void UpdateReverseDashPhase()
    {
        currentExecuteVelocity = new Vector2(
            reverseDashDirection.x * ChargeData.reverseDashSpeed,
            0f);

        controller.Velocity = currentExecuteVelocity;

        UpdateReverseDashSpark();

        if (phaseTimer >= ChargeData.reverseDashDuration)
            EnterExecutePhase(ExecutePhase.BrakeAndFire);
    }

    private void UpdateBrakeAndFirePhase()
    {
        currentExecuteVelocity = Vector2.MoveTowards(
            currentExecuteVelocity,
            Vector2.zero,
            ChargeData.brakeDeceleration * Time.deltaTime);

        controller.Velocity = currentExecuteVelocity;

        if (!laserFired && phaseTimer >= ChargeData.brakeBeforeFireTime)
        {
            FireLaser();
            laserFired = true;
        }
    }

    private void UpdateReverseDashSpark()
    {
        if (ChargeData.sparkFeedback == null)
            return;

        sparkTimer -= Time.deltaTime;

        if (sparkTimer > 0f)
            return;

        sparkTimer = ChargeData.sparkInterval;

        Vector3 offset = ChargeData.sparkFeedback.offset;
        Quaternion sparkRotation = Quaternion.identity;

        if (controller.Facing > 0)
        {
            offset.x *= -1f;
            sparkRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        feedbackPlayer.PlayExact(
            ChargeData.sparkFeedback,
            sparkPoint.position + offset,
            sparkRotation);
    }

    private void LockFacingToPlayerOnce()
    {
        if (facingLocked || playerTr == null)
            return;

        float deltaX = playerTr.position.x - controller.transform.position.x;

        if (Mathf.Abs(deltaX) <= 0.001f)
            lockedFacing = controller.Facing;
        else
            lockedFacing = deltaX > 0f ? 1 : -1;

        controller.FacingUpdate(lockedFacing);
        facingLocked = true;
    }

    private Vector2 GetTeleportTarget()
    {
        if (playerTr == null)
            return controller.transform.position;

        float offsetX = Mathf.Abs(ChargeData.teleportOffset.x);

        float primaryX = playerTr.position.x - lockedFacing * offsetX;
        float fallbackX = playerTr.position.x + lockedFacing * offsetX;

        Vector2 primaryTarget = new Vector2(
            primaryX,
            GetGroundY(primaryX) + ChargeData.teleportOffset.y);

        if (!IsBlocked(playerTr.position, primaryTarget))
            return primaryTarget;

        return new Vector2(
            fallbackX,
            GetGroundY(fallbackX) + ChargeData.teleportOffset.y);
    }

    private Vector2 GetSlashDirection()
    {
        Vector2 down = Vector2.down;

        // facing right -> down-left
        // facing left  -> down-right
        float signedAngle = ChargeData.slashAngleFromDown * lockedFacing;
        return (Quaternion.Euler(0f, 0f, signedAngle) * down).normalized;
    }

    private Vector2 GetReverseDashDirectionToPlayer()
    {
        if (playerTr == null)
            return new Vector2(lockedFacing, 0f);

        float deltaX = playerTr.position.x - controller.transform.position.x;

        if (Mathf.Abs(deltaX) <= ChargeData.reverseDashDeadZone)
            return new Vector2(controller.Facing, 0f);

        return new Vector2(deltaX > 0f ? 1f : -1f, 0f);
    }

    private Vector2 GetLaserDirection()
    {
        Vector2 oppositeOfDash = -reverseDashDirection.normalized;
        float signedAngle = ChargeData.laserAngleOffset * controller.Facing;
        return (Quaternion.Euler(0f, 0f, signedAngle) * oppositeOfDash).normalized;
    }

    private void FireLaser()
    {
        if (ChargeData.laserPrefab == null)
            return;

        if (ChargeData.laserFireFeedback != null)
            feedbackPlayer.Play(ChargeData.laserFireFeedback, executePoint ? executePoint : transform);

        ReturnLaser();

        Vector2 fireDirection = GetLaserDirection();
        if (nightmareCore != null) nightmareCore.Fire(fireDirection);

        activeLaser = WorldEntityPool.Instance.Spawn(
            ChargeData.laserPrefab,
            executePoint.position,
            Quaternion.identity);

        activeLaser.Fire(
            ChargeData.laserDamageInfo,
            executePoint.position,
            fireDirection,
            ChargeData.laserLength,
            ChargeData.laserDuration,
            ChargeData.laserBlockMask,
            ChargeData.laserDamageMask,
            ChargeData.laserWidth,
            controller);
    }

    private void UpdateActiveLaser()
    {
        if (activeLaser == null)
            return;

        activeLaser.SetOrigin(executePoint.position);
    }

    private void ReturnLaser()
    {
        if (activeLaser == null)
            return;

        activeLaser.ForceReturnToPool();
        activeLaser = null;
    }

    private void SpawnPreview()
    {
        if (ChargeData.previewLinePrefab == null)
            return;

        if (activePreview != null)
            return;

        activePreview = WorldEntityPool.Instance.Spawn(
            ChargeData.previewLinePrefab,
            windupPoint.position,
            Quaternion.identity);

        activePreview.Show(
            windupPoint.position,
            slashDirection,
            ChargeData.previewLineLength,
            ChargeData.previewLineWidth,
            ChargeData.previewBlockMask,
            ChargeData.previewLineColor,
            data.windupTime);
    }

    private void UpdatePreview()
    {
        if (activePreview == null)
            return;

        activePreview.SetOrigin(windupPoint.position);
        activePreview.SetDirection(slashDirection);
    }

    private void ReturnPreview()
    {
        if (activePreview == null)
            return;

        activePreview.ForceReturnToPool();
        activePreview = null;
    }

    private void SpawnSlashHitbox()
    {
        if (ChargeData.meleeHitbox == null)
            return;

        MeleeHitbox meleeHitbox = WorldEntityPool.Instance.Spawn(
            ChargeData.meleeHitbox,
            sparkPoint.position,
            Quaternion.identity);

        meleeHitbox.Initialize(controller, ChargeData.slashDamageInfo);
        meleeHitbox.SetHitFeedback(null);

        if (ChargeData.slashFeedback != null)
            feedbackPlayer.Play(ChargeData.slashFeedback, executePoint ? executePoint : transform);
    }

    private bool HasReachedGroundDuringSlash()
    {
        float currentGroundY = GetGroundY(controller.transform.position.x);
        return controller.transform.position.y <= currentGroundY + ChargeData.slashGroundHitThreshold;
    }

    private float GetGroundY(float x)
    {
        if (playerTr == null)
            return controller.transform.position.y;

        Vector2 origin = new Vector2(x, playerTr.position.y + ChargeData.groundCheckStartHeight);

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            Vector2.down,
            ChargeData.groundCheckDistance,
            ChargeData.teleportBlockMask);

        if (hit.collider != null)
            return hit.point.y;

        return playerTr.position.y;
    }

    private bool IsBlocked(Vector2 from, Vector2 to)
    {
        RaycastHit2D hit = Physics2D.Linecast(from, to, ChargeData.teleportBlockMask);
        return hit.collider != null;
    }
    // 베기가 끝나는 순간 코어 중심에서 유도탄을 좌우로 벌려 쏜다(예전에는 여기서 지뢰를 뿌렸다).
    // 이동·벽 충돌·피격·폭발 판정은 유도탄(ConstructorHomingMissile)이 기존 모터 경로로 처리한다.
    private void FireSlashMissiles()
    {
        firedMissiles.Clear();

        if (ChargeData.missilePrefab == null || WorldEntityPool.Instance == null)
            return;

        int count = Mathf.Max(1, ChargeData.missileCount);

        // 코어 중심에서 나온다(코어가 없는 적은 준비 지점).
        Vector2 origin = nightmareCore != null
            ? nightmareCore.Center
            : (Vector2)(windupPoint != null ? windupPoint.position : transform.position);

        MissileOrigin = origin;

        Vector2 totalDirection = Vector2.zero;

        for (int i = 0; i < count; i++)
        {
            // 위쪽을 기준으로 좌우로 고르게 벌린다. 2발이면 왼쪽·오른쪽으로 벌림 각도만큼 기울인다.
            float side = count == 1 ? 0f : i / (float)(count - 1) * 2f - 1f;
            float radians = (90f - GetMissileLaunchAngle(origin, side)) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            totalDirection += direction;

            ConstructorHomingMissile missile = WorldEntityPool.Instance.Spawn(
                ChargeData.missilePrefab,
                origin,
                Quaternion.identity);

            missile.ConfigureGuidance(
                playerTr,
                direction,
                ChargeData.missileLaunchDuration,
                ChargeData.missileLaunchTurnRateDegrees,
                ChargeData.missileTrackingDuration,
                ChargeData.missileTurnRateDegrees,
                ChargeData.missileAccelerationDuration,
                ChargeData.missileInitialSpeedRatio,
                ChargeData.missileLifeTime,
                ChargeData.missileSlowDuration,
                ChargeData.missileSlowSpeedRatio,
                ChargeData.missileSlowTurnRateDegrees);

            missile.ConfigureExplosion(
                ChargeData.missileExplosionRadius,
                ChargeData.missileExplosionDamageInfo);

            missile.Initialize(
                controller,
                ChargeData.missileDamageInfo,
                ChargeData.missileSpeed,
                i + 1,
                ChargeData.missileCollideLayer);

            firedMissiles.Add(missile);
        }

        if (ChargeData.missileLaunchFeedback != null)
            feedbackPlayer.PlayExact(ChargeData.missileLaunchFeedback, origin, Quaternion.identity);

        // 코어의 반동: 쏜 방향을 모두 더한 쪽(좌우 대칭이면 위쪽)을 향해 번쩍이고 반대쪽으로 밀린다.
        if (nightmareCore != null)
            nightmareCore.Fire(totalDirection, ChargeData.missileRecoil);
    }

    // 유도탄 한 발이 나가는 각도(위쪽에서 기운 정도, 도. 오른쪽이 +).
    // 퍼져 나갈 길에 벽이 가까우면 위쪽으로 세워 쏜다(나오자마자 벽에 부딪혀 터지지 않게). 세워도 막혀 있으면 원래 각도로 쏜다.
    // 지형은 투사체가 쓰는 기존 모터의 질의로 확인한다(탄의 크기만큼 두께를 준다).
    private float GetMissileLaunchAngle(Vector2 origin, float side)
    {
        float spread = side * ChargeData.missileSpreadAngle;

        if (ChargeData.missileClearance <= 0f || Mathf.Approximately(spread, 0f))
            return spread;

        CircleCollider2D missileCollider = ChargeData.missilePrefab.GetComponent<CircleCollider2D>();
        float radius = missileCollider != null ? missileCollider.radius : 0.25f;

        const int steps = 4;

        for (int i = 0; i < steps; i++)
        {
            float angle = Mathf.Lerp(spread, 0f, i / (float)steps);
            float radians = (90f - angle) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));

            RaycastHit2D hit = RaycastMotor2D.CircleCastEnvironment(
                origin,
                radius,
                direction,
                ChargeData.missileClearance,
                ChargeData.missileCollideLayer);

            if (hit.collider == null)
                return angle;
        }

        return spread;
    }
}