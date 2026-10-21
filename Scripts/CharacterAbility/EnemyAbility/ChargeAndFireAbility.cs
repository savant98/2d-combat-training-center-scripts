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

    private ChargeAndFireData ChargeData => (ChargeAndFireData)data;

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        playerTr = GameManager.Instance.Player.transform;

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
                SpawnSlashLandMines();
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
            SpawnSlashLandMines();
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
    private void SpawnSlashLandMines()
    {
        if (ChargeData.minePrefab == null)
            return;

        int spawnCount = Mathf.Max(1, ChargeData.mineCount);

        Vector2 basePos = sparkPoint != null
            ? (Vector2)sparkPoint.position
            : (Vector2)transform.position;

        Quaternion spawnRotation = controller.transform.rotation;

        // 좌우로 퍼지는 시작 위치 간격
        float horizontalSpacing = 0.35f;

        // count가 홀수면 중앙 포함, 짝수면 중앙 기준 양옆 대칭
        float centerOffset = (spawnCount - 1) * 0.5f;

        for (int i = 0; i < spawnCount; i++)
        {
            float offsetIndex = i - centerOffset;

            // 시작 위치를 좌우로 조금씩 벌림
            Vector2 spawnPos = basePos + new Vector2(offsetIndex * horizontalSpacing, 0f);

            ProjectileBase mine = WorldEntityPool.Instance.Spawn(
                ChargeData.minePrefab,
                spawnPos,
                spawnRotation
            );

            // LandMine은 angleStep * count 를 쓰니까
            // count 값을 0,1,2... 대신 "중앙 기준 대칭 순번"처럼 맞춰주기 위해 1부터 순차 전달
            mine.Initialize(
                controller,
                ChargeData.mineDamageInfo,
                ChargeData.mineLaunchSpeed,
                i,
                ChargeData.minePlatformLayer
            );
        }
    }
}