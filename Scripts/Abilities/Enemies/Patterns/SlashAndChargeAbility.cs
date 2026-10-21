using UnityEngine;

public class SlashAndChargeAbility : AiAbility
{
    private EnemyTeleporter teleporter;
    private CharacterController2D playerController;
    private Transform playerTr;
    private SlashAndChargeDataSO slashData;

    // teleport
    private bool teleportTriggered;
    private bool teleportFinished;
    private Vector2 teleportTarget;

    // execute sub phase
    private bool chargeStarted;
    private bool shotsFired;

    // charge
    private Vector2 chargeStartPos;
    private float traveled;
    private int chargeDirection;

    private void Awake()
    {
        slashData = (SlashAndChargeDataSO)data;
    }

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        playerController = GameManager.Instance.Player;
        playerTr = playerController != null ? playerController.transform : null;

        teleportTriggered = false;
        teleportFinished = false;
        teleportTarget = Vector2.zero;

        chargeStarted = false;
        shotsFired = false;

        chargeStartPos = Vector2.zero;
        traveled = 0f;
        chargeDirection = 0;

        controller.Velocity = Vector2.zero;
    }

    protected override void OnWindup(float t)
    {
        if (playerTr == null)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!teleportTriggered && t < slashData.teleportTriggerTime)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!teleportTriggered && t >= slashData.teleportTriggerTime)
        {
            teleportTriggered = true;
            controller.Velocity = Vector2.zero;

            teleportTarget = GetTeleportTarget(
                playerTr.position,
                controller.transform.position
            );

            teleporter.TeleportSequence(
                teleportTarget,
                feedbackPlayer,
                slashData.teleportStartFx,
                slashData.teleportEndFx,
                slashData.teleportDisappearDelay,
                slashData.teleportAppearDelay,
                () =>
                {
                    teleportFinished = true;
                    controller.Velocity = Vector2.zero;
                    FacePlayer();
                });

            return;
        }

        if (!teleportFinished)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        controller.Velocity = Vector2.zero;
    }

    protected override void OnExecuteStart()
    {
        chargeStarted = false;
        shotsFired = false;
        traveled = 0f;
        chargeDirection = 0;
        chargeStartPos = controller.transform.position;

        controller.Velocity = Vector2.zero;
        FacePlayer();
    }

    protected override void OnExecute(float t)
    {
        if (playerTr == null)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!shotsFired)
        {
            controller.Velocity = Vector2.zero;

            if (t < slashData.executeShotDelay)
                return;

            StartChargeAndShots();
        }

        // Execute 초반: 슬래시 애니메이션 대기
        if (!chargeStarted)
        {
            if (t < slashData.executeSlashDelay)
                return;

            chargeStarted = true;
        }

        // Execute 후반: 돌진
        UpdateChargeMovement();
    }

    protected override void OnRecoverStart()
    {
        controller.Velocity = Vector2.zero;
    }

    protected override void OnRecover(float t)
    {
        controller.Velocity = Vector2.zero;
    }

    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;

        teleportTriggered = false;
        teleportFinished = false;
        chargeStarted = false;
        shotsFired = false;
    }

    private void StartChargeAndShots()
    {
        chargeStartPos = controller.transform.position;
        traveled = 0f;
        FacePlayer();
        chargeDirection = controller.Facing;

        if (!shotsFired)
        {
            FireAngleShots();
            shotsFired = true;
        }
    }

    private void UpdateChargeMovement()
    {
        float currentX = controller.transform.position.x;
        traveled = Mathf.Abs(currentX - chargeStartPos.x);

        if (traveled >= slashData.chargeDistance)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        float normalized = traveled / Mathf.Max(0.0001f, slashData.chargeDistance);
        float speedFactor = slashData.chargeSpeedCurve.Evaluate(normalized);
        float baseSpeed = slashData.chargeDistance / Mathf.Max(0.0001f, slashData.chargeDuration);
        float finalSpeed = baseSpeed * speedFactor;

        controller.Velocity = new Vector2(finalSpeed * chargeDirection, 0f);
    }

    private void FireAngleShots()
    {
        if (slashData.projectilePrefab == null || playerTr == null)
            return;

        ProjectileSpawnUtility.SpawnValidationSettings validationSettings =
            new ProjectileSpawnUtility.SpawnValidationSettings
            {
                checkRadius = slashData.shotSpawnCheckRadius,
                blockMask = slashData.shotSpawnBlockMask,
                overlapMask = slashData.shotSpawnOverlapMask
            };

        Vector2 anchor = GetShotAnchor();

        int count = Mathf.Max(1, slashData.shotCount);

        for (int i = 0; i < count; i++)
        {
            Vector2 spawnPos = ProjectileSpawnUtility.GetValidSpawnPosition(
                anchor,
                slashData.shotSpacingOffset,
                i,
                ProjectileSpawnUtility.SpawnPattern.AlternatingHorizontal,
                validationSettings,
                slashData.shotSpawnMaxSearchDepth
            );

            ProjectileBase projectile = WorldEntityPool.Instance.Spawn(
                slashData.projectilePrefab,
                spawnPos,
                Quaternion.identity
            );

            if (projectile is AngleChargeShot angleChargeShot)
                angleChargeShot.SetAngleFromVertical(slashData.shotAngleFromVertical);

            projectile.Initialize(
                controller,
                slashData.projectileDamageInfo,
                slashData.projectileSpeed,
                i,
                slashData.projectileCollideLayer
            );
        }
    }

    /// <summary>
    /// 첫 번째 샷 기준점.
    /// player 머리 기준 (0, offset.y) 위치에 생성하고,
    /// 이후 샷들은 ProjectileSpawnUtility가 좌우로 퍼뜨린다.
    /// </summary>
    private Vector2 GetShotAnchor()
    {
        return (Vector2)playerTr.position + slashData.shotAnchorOffset;
    }

    private Vector2 GetTeleportTarget(Vector2 playerPos, Vector2 enemyPos)
    {
        int playerFacing = 1;

        if (playerController != null)
            playerFacing = playerController.Facing >= 0 ? 1 : -1;

        float forwardX = playerPos.x + Mathf.Abs(slashData.teleportOffset.x) * playerFacing;
        float backwardX = playerPos.x - Mathf.Abs(slashData.teleportOffset.x) * playerFacing;

        Vector2 frontTarget = new Vector2(
            forwardX,
            GetGroundY(forwardX) + slashData.teleportOffset.y);

        Vector2 backTarget = new Vector2(
            backwardX,
            GetGroundY(backwardX) + slashData.teleportOffset.y);

        if (!IsBlocked(playerPos, frontTarget) && IsStandPositionValid(frontTarget))
            return frontTarget;

        return backTarget;
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

    private float GetGroundY(float x)
    {
        if (playerTr == null)
            return controller.transform.position.y;

        Vector2 origin = new Vector2(x, playerTr.position.y + slashData.groundCheckStartHeight);

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            Vector2.down,
            slashData.groundCheckDistance,
            slashData.groundCheckMask);

        if (hit.collider != null)
            return hit.point.y;

        return playerTr.position.y;
    }

    private bool IsBlocked(Vector2 from, Vector2 to)
    {
        RaycastHit2D hit = Physics2D.Linecast(from, to, slashData.teleportBlockMask);
        return hit.collider != null;
    }

    private bool IsStandPositionValid(Vector2 pos)
    {
        return !Physics2D.OverlapCircle(
            pos,
            slashData.standPositionCheckRadius,
            slashData.teleportBlockMask);
    }
}