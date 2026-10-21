using UnityEngine;

public class TeleportAngleShotAbility : AiAbility
{
    private EnemyTeleporter teleporter;
    private Transform playerTr;
    private TeleportAngleShotData shotData;

    // teleport
    private bool teleportTriggered;
    private bool teleportFinished;
    private bool teleportedToRightSide;
    private Vector2 teleportTarget;

    // execute
    private bool fired;

    private void Awake()
    {
        shotData = (TeleportAngleShotData)data;
    }

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        playerTr = GameManager.Instance.Player.transform;

        teleportTriggered = false;
        teleportFinished = false;
        teleportedToRightSide = false;
        teleportTarget = Vector2.zero;

        fired = false;

        controller.Velocity = Vector2.zero;
    }

    protected override void OnWindup(float t)
    {
        if (playerTr == null)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!teleportTriggered && t < shotData.teleportTriggerTime)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!teleportTriggered && t >= shotData.teleportTriggerTime)
        {
            teleportTriggered = true;
            controller.Velocity = Vector2.zero;

            teleportTarget = GetTeleportTarget(
                playerTr.position,
                controller.transform.position,
                out bool targetIsRightSide);

            teleportedToRightSide = targetIsRightSide;

            teleporter.TeleportSequence(
                teleportTarget,
                feedbackPlayer,
                shotData.teleportStartFx,
                shotData.teleportEndFx,
                shotData.teleportDisappearDelay,
                shotData.teleportAppearDelay,
                () =>
                {
                    teleportFinished = true;
                    controller.Velocity = Vector2.zero;

                    // 텔포 완료 시점에만 Facing 갱신
                    controller.FacingUpdate(teleportedToRightSide ? -1 : 1);
                });

            return;
        }

        if (!teleportFinished)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        UpdatePostTeleportRetreat();
    }

    protected override void OnExecuteStart()
    {
        controller.Velocity = Vector2.zero;

        if (!fired)
        {
            FireAngleShots();
            fired = true;
        }
    }

    protected override void OnExecute(float t)
    {
        controller.Velocity = Vector2.zero;
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
        fired = false;
    }

    private void UpdatePostTeleportRetreat()
    {
        int backwardDir = -controller.Facing;

        controller.Velocity = new Vector2(backwardDir * shotData.postTeleportMoveSpeed, 0f);
    }

    private Vector2 GetTeleportTarget(Vector2 playerPos, Vector2 enemyPos, out bool targetIsRightSide)
    {
        bool enemyIsLeftOfPlayer = enemyPos.x < playerPos.x;

        float offsetX = Mathf.Abs(shotData.teleportOffset.x);
        float rightX = playerPos.x + offsetX;
        float leftX = playerPos.x - offsetX;

        Vector2 rightTarget = new Vector2(
            rightX,
            GetGroundY(rightX) + shotData.teleportOffset.y);

        Vector2 leftTarget = new Vector2(
            leftX,
            GetGroundY(leftX) + shotData.teleportOffset.y);

        // enemy가 플레이어 왼쪽에 있었으면
        // 우선 플레이어 오른쪽(= 플레이어 기준 반대편)으로 텔포
        if (enemyIsLeftOfPlayer)
        {
            if (!IsBlocked(playerPos, rightTarget) && IsStandPositionValid(rightTarget))
            {
                targetIsRightSide = true;
                return rightTarget;
            }

            targetIsRightSide = false;
            return leftTarget;
        }
        else
        {
            if (!IsBlocked(playerPos, leftTarget) && IsStandPositionValid(leftTarget))
            {
                targetIsRightSide = false;
                return leftTarget;
            }

            targetIsRightSide = true;
            return rightTarget;
        }
    }

    private void FireAngleShots()
    {
        if (shotData.projectilePrefab == null || playerTr == null)
            return;

        Vector2 baseAnchor = GetShotSpawnBaseAnchor();
        Vector2 fireDir = CalculateShotDirection(shotData.shotAngleFromVertical);
        Vector2 sideDir = new Vector2(-fireDir.y, fireDir.x).normalized;

        int count = Mathf.Max(1, shotData.shotCount);

        for (int i = 0; i < count; i++)
        {
            float centeredIndex = i - (count - 1) * 0.5f;
            Vector2 desiredPos = baseAnchor + sideDir * (centeredIndex * shotData.multiShotSpacing);
            Vector2 spawnPos = GetValidShotSpawnPosition(desiredPos, sideDir);

            ProjectileBase projectile = WorldEntityPool.Instance.Spawn(
                shotData.projectilePrefab,
                spawnPos,
                Quaternion.identity
            );

            if (projectile is AngleChargeShot angleChargeShot)
                angleChargeShot.SetAngleFromVertical(shotData.shotAngleFromVertical);

            projectile.Initialize(
                controller,
                shotData.projectileDamageInfo,
                shotData.projectileSpeed,
                i,
                shotData.projectileCollideLayer
            );
        }
    }

    private Vector2 GetShotSpawnBaseAnchor()
    {
        Vector2 playerGroundAnchor = GetPlayerGroundAnchor();

        float offsetX = Mathf.Abs(shotData.shotSpawnOffset.x);
        float signedOffsetX = controller.Facing >= 0 ? offsetX : -offsetX;

        float desiredX = playerGroundAnchor.x + signedOffsetX;
        float desiredY = playerGroundAnchor.y + shotData.shotSpawnOffset.y;

        Vector2 desired = new Vector2(desiredX, desiredY);

        if (!IsShotSpawnBlocked(desired))
            return desired;

        // x만 막히면 player 중심 기준으로 반대쪽도 시도
        float fallbackX = playerGroundAnchor.x - signedOffsetX;
        Vector2 fallback = new Vector2(fallbackX, playerGroundAnchor.y + shotData.shotSpawnOffset.y);

        if (!IsShotSpawnBlocked(fallback))
            return fallback;

        // 둘 다 애매하면 player 기준 중앙 상단
        Vector2 centerFallback = new Vector2(
            playerGroundAnchor.x,
            playerGroundAnchor.y + shotData.shotSpawnOffset.y);

        return centerFallback;
    }

    private Vector2 GetValidShotSpawnPosition(Vector2 desiredPos, Vector2 sideDir)
    {
        if (!IsShotSpawnBlocked(desiredPos))
            return desiredPos;

        float step = Mathf.Max(0.01f, shotData.shotSpawnResolveStep);
        int maxTries = Mathf.Max(1, shotData.shotSpawnResolveTries);

        // 위로 올리기 먼저
        for (int i = 1; i <= maxTries; i++)
        {
            Vector2 candidate = desiredPos + Vector2.up * (step * i);
            if (!IsShotSpawnBlocked(candidate))
                return candidate;
        }

        // 좌우 퍼짐 방향으로도 시도
        for (int i = 1; i <= maxTries; i++)
        {
            Vector2 candidateA = desiredPos + sideDir * (step * i);
            if (!IsShotSpawnBlocked(candidateA))
                return candidateA;

            Vector2 candidateB = desiredPos - sideDir * (step * i);
            if (!IsShotSpawnBlocked(candidateB))
                return candidateB;
        }

        return desiredPos;
    }

    private Vector2 GetPlayerGroundAnchor()
    {
        float groundY = GetGroundY(playerTr.position.x);
        return new Vector2(playerTr.position.x, groundY);
    }

    private Vector2 CalculateShotDirection(float angleFromVertical)
    {
        Vector2 down = Vector2.down;

        int horizontalSign = controller.Facing >= 0 ? 1 : -1;
        float signedAngle = angleFromVertical * -horizontalSign;

        Vector2 dir = Quaternion.Euler(0f, 0f, signedAngle) * down;
        return dir.normalized;
    }

    private float GetGroundY(float x)
    {
        if (playerTr == null)
            return controller.transform.position.y;

        Vector2 origin = new Vector2(x, playerTr.position.y + shotData.groundCheckStartHeight);

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            Vector2.down,
            shotData.groundCheckDistance,
            shotData.groundCheckMask);

        if (hit.collider != null)
            return hit.point.y;

        return playerTr.position.y;
    }

    private bool IsBlocked(Vector2 from, Vector2 to)
    {
        RaycastHit2D hit = Physics2D.Linecast(from, to, shotData.teleportBlockMask);
        return hit.collider != null;
    }

    private bool IsStandPositionValid(Vector2 pos)
    {
        return !Physics2D.OverlapCircle(
            pos,
            shotData.standPositionCheckRadius,
            shotData.teleportBlockMask);
    }

    private bool IsShotSpawnBlocked(Vector2 pos)
    {
        bool blocked = Physics2D.OverlapCircle(
            pos,
            shotData.shotSpawnCheckRadius,
            shotData.shotSpawnBlockMask);

        if (blocked)
            return true;

        bool overlapped = Physics2D.OverlapCircle(
            pos,
            shotData.shotSpawnCheckRadius,
            shotData.shotSpawnOverlapMask);

        return overlapped;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (data == null)
            return;

        if (shotData == null)
            shotData = data as TeleportAngleShotData;

        if (shotData == null)
            return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, shotData.standPositionCheckRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, shotData.shotSpawnCheckRadius);
    }
#endif
}