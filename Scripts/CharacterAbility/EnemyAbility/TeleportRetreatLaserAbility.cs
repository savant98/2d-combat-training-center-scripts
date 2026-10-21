using UnityEngine;

public class TeleportRetreatLaserAbility : AiAbility
{
    private EnemyTeleporter teleporter;
    private Transform playerTr;

    // teleport
    private bool teleportTriggered;
    private bool teleportFinished;
    private bool teleportedToRightSide;
    private Vector2 teleportTarget;

    // windup retreat move
    private float currentRetreatSpeed;
    private float sparkTimer;

    // laser
    private LaserPreviewLine activePreview;
    private LaserBeam activeLaser;

    private TeleportRetreatLaserData LaserData => (TeleportRetreatLaserData)data;

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        playerTr = GameManager.Instance.Player.transform;

        teleportTriggered = false;
        teleportFinished = false;
        teleportedToRightSide = false;
        teleportTarget = Vector2.zero;

        currentRetreatSpeed = 0f;
        sparkTimer = 0f;

        activePreview = null;
        activeLaser = null;

        controller.Velocity = Vector2.zero;
    }

    protected override void OnWindup(float t)
    {
        if (playerTr == null)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!teleportTriggered && t < LaserData.teleportTriggerTime)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!teleportTriggered && t >= LaserData.teleportTriggerTime)
        {
            teleportTriggered = true;
            controller.Velocity = Vector2.zero;

            teleportTarget = GetTeleportTarget(
                playerTr.position,
                out bool targetIsRightSide);

            teleportedToRightSide = targetIsRightSide;

            teleporter.TeleportSequence(
                teleportTarget,
                feedbackPlayer,
                LaserData.teleportStartFx,
                LaserData.teleportEndFx,
                LaserData.teleportDisappearDelay,
                LaserData.teleportAppearDelay,
                () =>
                {
                    teleportFinished = true;
                    controller.Velocity = Vector2.zero;
                    currentRetreatSpeed = LaserData.postTeleportMoveSpeed;

                    // 플레이어 방향을 보도록 정면 갱신
                    controller.FacingUpdate(teleportedToRightSide ? -1 : 1);

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

        UpdatePostTeleportRetreat();

        float remaining = data.windupTime - t;
        UpdatePreview();
        activePreview?.UpdatePreviewVisual(remaining, data.windupTime);
    }

    protected override void OnExecuteStart()
    {
        controller.Velocity = Vector2.zero;
        FireLaser();
        UpdateActiveLaser();
        ReturnPreview();
    }

    protected override void OnExecute(float t)
    {
        controller.Velocity = Vector2.zero;
        UpdateActiveLaser();
    }

    protected override void OnRecoverStart()
    {
        controller.Velocity = Vector2.zero;
        ReturnPreview();
    }

    protected override void OnRecover(float t)
    {
        controller.Velocity = Vector2.zero;
    }

    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;

        if (activeLaser != null)
            activeLaser.ForceReturnToPool();

        ReturnPreview();
    }

    private void UpdatePostTeleportRetreat()
    {
        int backwardDir = -controller.Facing;

        currentRetreatSpeed = Mathf.MoveTowards(
            currentRetreatSpeed,
            0f,
            LaserData.postTeleportDeceleration * Time.deltaTime);

        controller.Velocity = new Vector2(backwardDir * currentRetreatSpeed, 0f);

        UpdateSpark(backwardDir);
    }

    private void UpdateSpark(int moveDir)
    {
        if (LaserData.sparkFeedback == null)
            return;

        sparkTimer -= Time.deltaTime;

        if (sparkTimer > 0f)
            return;

        sparkTimer = LaserData.sparkInterval;

        Vector3 offset = LaserData.sparkFeedback.offset;
        Quaternion sparkRotation = Quaternion.identity;

        if (moveDir < 0)
        {
            offset.x *= -1f;
            sparkRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        feedbackPlayer.PlayExact(
            LaserData.sparkFeedback,
            windupPoint.position + offset,
            sparkRotation);
    }

    private Vector2 GetTeleportTarget(Vector2 playerPos, out bool targetIsRightSide)
    {
        float offsetX = Mathf.Abs(LaserData.teleportOffset.x);

        // 랜덤하게 좌/우 중 하나를 먼저 시도
        bool tryRightFirst = Random.value < 0.5f;

        float firstX = tryRightFirst ? playerPos.x + offsetX : playerPos.x - offsetX;
        float secondX = tryRightFirst ? playerPos.x - offsetX : playerPos.x + offsetX;

        Vector2 firstTarget = new Vector2(
            firstX,
            GetGroundY(firstX) + LaserData.teleportOffset.y);

        Vector2 secondTarget = new Vector2(
            secondX,
            GetGroundY(secondX) + LaserData.teleportOffset.y);

        bool firstIsRight = tryRightFirst;
        bool secondIsRight = !tryRightFirst;

        if (!IsBlocked(playerPos, firstTarget) && IsStandPositionValid(firstTarget))
        {
            targetIsRightSide = firstIsRight;
            return firstTarget;
        }

        targetIsRightSide = secondIsRight;
        return secondTarget;
    }

    private float GetGroundY(float x)
    {
        if (playerTr == null)
            return controller.transform.position.y;

        Vector2 origin = new Vector2(x, playerTr.position.y + LaserData.groundCheckStartHeight);

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            Vector2.down,
            LaserData.groundCheckDistance,
            LaserData.teleportBlockMask);

        if (hit.collider != null)
            return hit.point.y;

        return playerTr.position.y;
    }

    private bool IsBlocked(Vector2 from, Vector2 to)
    {
        RaycastHit2D hit = Physics2D.Linecast(from, to, LaserData.teleportBlockMask);
        return hit.collider != null;
    }

    private bool IsStandPositionValid(Vector2 pos)
    {
        return !Physics2D.OverlapCircle(
            pos,
            LaserData.standPositionCheckRadius,
            LaserData.teleportBlockMask);
    }

    private void SpawnPreview()
    {
        if (LaserData.previewLinePrefab == null)
            return;

        if (activePreview != null)
            return;

        activePreview = WorldEntityPool.Instance.Spawn(
            LaserData.previewLinePrefab,
            executePoint.position,
            Quaternion.identity);

        activePreview.Show(
            executePoint.position,
            GetLaserDirection(),
            LaserData.laserLength,
            LaserData.previewLineWidth,
            LaserData.laserBlockMask,
            LaserData.previewLineColor,
            -1f);
    }

    private void UpdatePreview()
    {
        if (activePreview == null)
            return;

        activePreview.SetOrigin(executePoint.position);
        activePreview.SetDirection(GetLaserDirection());
    }

    private Vector2 GetLaserDirection()
    {
        return controller.Facing >= 0 ? Vector2.right : Vector2.left;
    }

    private void FireLaser()
    {
        if (LaserData.laserPrefab == null)
            return;

        Vector2 direction = GetLaserDirection();

        activeLaser = WorldEntityPool.Instance.Spawn(
            LaserData.laserPrefab,
            executePoint.position,
            Quaternion.identity);

        activeLaser.Fire(
            LaserData.laserDamageInfo,
            executePoint.position,
            direction,
            LaserData.laserLength,
            LaserData.laserDuration,
            LaserData.laserBlockMask,
            LaserData.laserDamageMask,
            LaserData.laserWidth,
            controller);
    }

    private void UpdateActiveLaser()
    {
        if (activeLaser == null)
            return;

        activeLaser.SetOrigin(executePoint.position);
    }

    private void ReturnPreview()
    {
        if (activePreview == null)
            return;

        activePreview.ForceReturnToPool();
        activePreview = null;
    }
}