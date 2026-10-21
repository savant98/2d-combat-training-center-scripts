using UnityEngine;

public class RadialAngleShotAbility : AiAbility
{
    private EnemyTeleporter teleporter;
    private Transform playerTr;

    private bool teleportTriggered;
    private bool teleportFinished;
    private Vector2 teleportTarget;

    private int firedCount;
    private float shotTimer;

    private RadialAngleShotData ShotData => (RadialAngleShotData)data;
    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        playerTr = GameManager.Instance.Player.transform;

        teleportTriggered = false;
        teleportFinished = false;
        teleportTarget = Vector2.zero;

        firedCount = 0;
        shotTimer = 0f;

        controller.Velocity = Vector2.zero;
    }

    protected override void OnWindup(float t)
    {
        if (!teleportTriggered && t < ShotData.teleportTriggerTime)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!teleportTriggered && t >= ShotData.teleportTriggerTime)
        {
            teleportTriggered = true;
            controller.Velocity = Vector2.zero;

            teleportTarget = GetTeleportTarget();

            teleporter.TeleportSequence(
                teleportTarget,
                feedbackPlayer,
                ShotData.teleportStartFx,
                ShotData.teleportEndFx,
                ShotData.teleportDisappearDelay,
                ShotData.teleportAppearDelay,
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

        float normalized = Mathf.InverseLerp(ShotData.teleportTriggerTime, data.windupTime, t);
    }

    protected override void OnExecuteStart()
    {
        controller.Velocity = Vector2.zero;

        firedCount = 0;
        shotTimer = 0f;

        if (ShotData.fireFirstShotImmediately)
        {
            FireOneShot();
            firedCount++;
        }
    }

    protected override void OnExecute(float t)
    {
        controller.Velocity = Vector2.zero;

        if (firedCount >= ShotData.totalShotCount)
        {
            return;
        }

        shotTimer += Time.deltaTime;

        while (firedCount < ShotData.totalShotCount && shotTimer >= ShotData.shotInterval)
        {
            shotTimer -= ShotData.shotInterval;
            FireOneShot();
            firedCount++;
        }

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
    }

    private void FireOneShot()
    {
        if (ShotData.projectilePrefab == null)
            return;

        Vector2 origin = transform.position;

        Vector2 direction = RadialShotHelper.GetRadialDirection(
            firedCount,
            ShotData.totalShotCount,
            ShotData.startAngleDeg,
            ShotData.clockwise);

        ProjectileBase projectile = WorldEntityPool.Instance.Spawn(
            ShotData.projectilePrefab,
            origin,
            Quaternion.identity);

        if (projectile is AngleChargeShot angleChargeShot)
        {
            angleChargeShot.SetUseOwnerFacing(false);
            angleChargeShot.SetAngleFromVertical(
                RadialShotHelper.ToAngleChargeShotAngle(direction));
        }

        projectile.Initialize(
            controller,
            ShotData.projectileDamageInfo,
            ShotData.projectileSpeed,
            firedCount,
            ShotData.projectileCollideLayer
        );

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        projectile.transform.rotation = Quaternion.Euler(0f, 0f, angle + 90f);
    }

    private Vector2 GetTeleportTarget()
    {
        if (playerTr == null)
            return controller.transform.position;

        float targetX = playerTr.position.x + ShotData.teleportOffset.x;
        float groundY = GetGroundY(targetX);

        return new Vector2(
            targetX,
            groundY + ShotData.teleportOffset.y);
    }

    private float GetGroundY(float x)
    {
        if (playerTr == null)
            return controller.transform.position.y;

        Vector2 origin = new Vector2(x, playerTr.position.y + ShotData.groundCheckStartHeight);

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            Vector2.down,
            ShotData.groundCheckDistance,
            ShotData.teleportBlockMask);

        if (hit.collider != null)
            return hit.point.y;

        return playerTr.position.y;
    }

    private void FacePlayer()
    {
        if (playerTr == null)
            return;

        float deltaX = playerTr.position.x - controller.transform.position.x;

        if (Mathf.Abs(deltaX) <= ShotData.faceDeadZone)
            return;

        controller.FacingUpdate(deltaX > 0f ? 1 : -1);
    }
}