using UnityEngine;

public class WallTeleportChargeAbility : AiAbility
{
    private EnemyTeleporter teleporter;
    private Transform playerTr;

    private bool teleportTriggered;
    private bool teleportFinished;
    private Vector2 teleportTarget;

    private bool executeStarted;
    private bool executeFinished;
    private bool teleportedToRightWall;
    private int dashDirection;

    private LaserPreviewLine activePreview;

    private WallTeleportChargeDataSO chargeData;

    private void Awake()
    {
        chargeData = (WallTeleportChargeDataSO)data;
    }

    private void OnDisable()
    {
        UnsubscribeWallHit();
        ReturnPreview();
    }

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        playerTr = GameManager.Instance.Player.transform;

        teleportTriggered = false;
        teleportFinished = false;
        teleportTarget = Vector2.zero;

        executeStarted = false;
        executeFinished = false;
        dashDirection = 0;

        controller.Velocity = Vector2.zero;
        UnsubscribeWallHit();
        ReturnPreview();
    }

    protected override void OnWindup(float t)
    {
        if (!teleportTriggered && t < chargeData.teleportTriggerTime)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!teleportTriggered && t >= chargeData.teleportTriggerTime)
        {
            teleportTriggered = true;
            controller.Velocity = Vector2.zero;

            teleportTarget = GetFarWallTeleportTarget(
                playerTr.position,
                chargeData.chargeTeleportOffset.x,
                chargeData.chargeTeleportOffset.y,
                out bool targetIsRightWall);

            teleportedToRightWall = targetIsRightWall;

            teleporter.TeleportSequence(
                teleportTarget,
                feedbackPlayer,
                chargeData.teleportStartFeedback,
                chargeData.teleportEndFeedback,
                chargeData.teleportDisappearDelay,
                chargeData.teleportAppearDelay,
                () =>
                {
                    teleportFinished = true;
                    controller.Velocity = Vector2.zero;
                    FacePlayer();
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
        FacePlayer();

        float remaining = data.windupTime - t;
        UpdatePreview();
        activePreview?.UpdatePreviewVisual(remaining, data.windupTime);
    }

    protected override void OnExecuteStart()
    {
        executeStarted = true;
        executeFinished = false;

        dashDirection = teleportedToRightWall ? -1 : 1;

        controller.FacingUpdate(dashDirection);
        controller.Velocity = new Vector2(dashDirection * chargeData.chargeSpeed, 0f);

        SubscribeWallHit();
        ReturnPreview();
    }

    protected override void OnExecute(float t)
    {
        if (!executeStarted || executeFinished)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        controller.Velocity = new Vector2(dashDirection * chargeData.chargeSpeed, 0f);
    }

    protected override void OnRecoverStart()
    {
        controller.Velocity = Vector2.zero;
        UnsubscribeWallHit();
        ReturnPreview();

        Vector2 recoverTarget = GetRecoverTeleportTarget(
            playerTr.position,
            controller.transform.position,
            chargeData.recoverTeleportOffset);

        teleporter.TeleportSequence(
            recoverTarget,
            feedbackPlayer,
            chargeData.teleportStartFeedback,
            chargeData.teleportEndFeedback,
            chargeData.teleportDisappearDelay,
            chargeData.teleportAppearDelay,
            () =>
            {
                controller.Velocity = Vector2.zero;
                FacePlayer();
            });
    }

    protected override void OnRecover(float t)
    {
        controller.Velocity = Vector2.zero;
    }

    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;
        UnsubscribeWallHit();
        ReturnPreview();
    }

    private void HandleWallHit()
    {
        if (!executeStarted || executeFinished)
            return;

        FinishExecuteAndForceRecover();
    }

    private void FinishExecuteAndForceRecover()
    {
        if (executeFinished)
            return;

        executeFinished = true;
        controller.Velocity = Vector2.zero;
        UnsubscribeWallHit();

        ForceRecover();
    }

    private void SubscribeWallHit()
    {
        controller.OnWallHit -= HandleWallHit;
        controller.OnWallHit += HandleWallHit;
    }

    private void UnsubscribeWallHit()
    {
        controller.OnWallHit -= HandleWallHit;
    }

    private void FacePlayer()
    {
        if (playerTr == null)
            return;

        float deltaX = playerTr.position.x - controller.transform.position.x;
        if (Mathf.Abs(deltaX) <= 0.001f)
            return;

        controller.FacingUpdate(deltaX > 0f ? 1 : -1);
    }

    private void SpawnPreview()
    {
        if (chargeData.previewLinePrefab == null)
            return;

        if (activePreview != null)
            return;

        activePreview = WorldEntityPool.Instance.Spawn(
            chargeData.previewLinePrefab,
            executePoint.position,
            Quaternion.identity);

        activePreview.Show(
            executePoint.position,
            GetDashDirectionVector(),
            chargeData.previewLineLength,
            chargeData.previewLineWidth,
            chargeData.previewBlockMask,
            chargeData.previewLineColor,
            data.windupTime);
    }

    private void UpdatePreview()
    {
        if (activePreview == null)
            return;

        activePreview.SetOrigin(executePoint.position);
        activePreview.SetDirection(GetDashDirectionVector());
    }

    private void ReturnPreview()
    {
        if (activePreview == null)
            return;

        activePreview.ForceReturnToPool();
        activePreview = null;
    }
    private Vector2 GetDashDirectionVector()
    {
        int dir = teleportedToRightWall ? -1 : 1;
        return dir > 0 ? Vector2.right : Vector2.left;
    }
    /// <summary>
    /// enemy 기준 좌/우를 탐색해서 더 멀리 있는 벽 앞 좌표를 반환.
    /// 벽이 없으면 최대 거리 끝 좌표를 사용.
    /// </summary>
    private Vector2 GetFarWallTeleportTarget(
        Vector2 playerPos,
        float maxDistance,
        float groundOffsetY,
        out bool targetIsRightWall)
    {
        float leftX = GetWallFrontX(playerPos, -1, maxDistance);
        float rightX = GetWallFrontX(playerPos, 1, maxDistance);

        float leftDistanceFromPlayer = Mathf.Abs(playerPos.x - leftX);
        float rightDistanceFromPlayer = Mathf.Abs(playerPos.x - rightX);

        if (rightDistanceFromPlayer >= leftDistanceFromPlayer)
        {
            targetIsRightWall = true;
            return new Vector2(rightX, GetGroundY(rightX) + groundOffsetY);
        }
        else
        {
            targetIsRightWall = false;
            return new Vector2(leftX, GetGroundY(leftX) + groundOffsetY);
        }
    }

    /// <summary>
    /// recover 시에는 플레이어와 더 멀어지는 쪽으로 텔레포트.
    /// </summary>
    private Vector2 GetRecoverTeleportTarget(
        Vector2 playerPos,
        Vector2 enemyPos,
        Vector2 recoverOffset)
    {
        bool enemyIsRightOfPlayer = enemyPos.x > playerPos.x;

        float offsetX = Mathf.Abs(recoverOffset.x);
        float offsetY = recoverOffset.y;

        float rightX = playerPos.x + offsetX;
        float leftX = playerPos.x - offsetX;

        Vector2 rightTarget = new Vector2(
            rightX,
            GetGroundY(rightX) + offsetY);

        Vector2 leftTarget = new Vector2(
            leftX,
            GetGroundY(leftX) + offsetY);

        // enemy가 플레이어 오른쪽에 있었으면,
        // recover는 우선 플레이어 왼쪽을 시도
        if (enemyIsRightOfPlayer)
        {
            if (!IsBlocked(playerPos, leftTarget))
                return leftTarget;

            return rightTarget;
        }
        else
        {
            if (!IsBlocked(playerPos, rightTarget))
                return rightTarget;

            return leftTarget;
        }
    }
    private bool IsBlocked(Vector2 from, Vector2 to)
    {
        RaycastHit2D hit = Physics2D.Linecast(from, to, chargeData.teleportBlockMask);
        return hit.collider != null;
    }
    /// <summary>
    /// dir 방향으로 벽을 찾고, 벽이 있으면 벽 앞 좌표를 반환.
    /// 없으면 maxDistance 끝 좌표를 반환.
    /// </summary>
    private float GetWallFrontX(Vector2 origin, int dir, float maxDistance)
    {
        Vector2 rayDir = dir > 0 ? Vector2.right : Vector2.left;

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            rayDir,
            maxDistance,
            chargeData.teleportBlockMask);

        if (hit.collider != null)
        {
            float offsetSign = dir > 0 ? -1f : 1f;
            return hit.point.x + offsetSign * chargeData.wallFrontOffset;
        }

        return origin.x + dir * maxDistance;
    }

    private float GetGroundY(float x)
    {
        Vector2 origin = new Vector2(x, playerTr.position.y + chargeData.groundCheckStartHeight);

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            Vector2.down,
            chargeData.groundCheckDistance,
            chargeData.teleportBlockMask);

        if (hit.collider != null)
            return hit.point.y;

        return playerTr.position.y;
    }
}