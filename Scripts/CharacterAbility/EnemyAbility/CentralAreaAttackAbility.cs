using UnityEngine;

public class CentralAreaAttackAbility : AiAbility
{
    private enum WindupPhase
    {
        WaitingForTeleport,
        Teleporting,
        Hovering,
        Dropping,
        Landed
    }

    private EnemyTeleporter teleporter;
    private Transform playerTr;
    private CentralAreaAttackData areaData;

    private WindupPhase windupPhase;
    private Vector2 teleportTarget;
    private float targetLandY;
    private float hoverStartTime;
    private bool slamTriggered;

    private LaserPreviewLine activePreview;

    private void Awake()
    {
        areaData = (CentralAreaAttackData)data;
    }

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        playerTr = GameManager.Instance.Player.transform;

        windupPhase = WindupPhase.WaitingForTeleport;
        teleportTarget = controller.transform.position;
        targetLandY = controller.transform.position.y;
        hoverStartTime = 0f;
        slamTriggered = false;

        ReturnPreview();
        controller.Velocity = Vector2.zero;
    }

    protected override void OnWindup(float t)
    {
        switch (windupPhase)
        {
            case WindupPhase.WaitingForTeleport:
                UpdateWaitingForTeleport(t);
                break;

            case WindupPhase.Teleporting:
                controller.Velocity = Vector2.zero;
                break;

            case WindupPhase.Hovering:
                UpdateHovering();
                break;

            case WindupPhase.Dropping:
                UpdateDropping();
                break;

            case WindupPhase.Landed:
                controller.Velocity = Vector2.zero;
                FacePlayer();
                break;
        }
    }

    protected override void OnExecuteStart()
    {
        controller.Velocity = Vector2.zero;
        FacePlayer();
    }

    protected override void OnExecute(float t)
    {
        controller.Velocity = Vector2.zero;
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
        ReturnPreview();
    }

    private void UpdateWaitingForTeleport(float t)
    {
        if (t < areaData.teleportTriggerTime)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        teleportTarget = CalculateTeleportTarget();
        targetLandY = GetGroundY(teleportTarget.x) + areaData.landOffsetY;

        windupPhase = WindupPhase.Teleporting;

        teleporter.TeleportSequence(
            teleportTarget,
            feedbackPlayer,
            areaData.teleportStartFeedback,
            areaData.teleportEndFeedback,
            areaData.teleportDisappearDelay,
            areaData.teleportAppearDelay,
            () =>
            {
                controller.Velocity = Vector2.zero;
                hoverStartTime = Time.time;
                slamTriggered = false;

                SpawnPreview();
                UpdatePreview();

                windupPhase = WindupPhase.Hovering;
            });
    }

    private void UpdateHovering()
    {
        controller.Velocity = new Vector2(0f, areaData.hoverRiseSpeed);

        UpdatePreview();
        activePreview?.UpdatePreviewVisual(
            Mathf.Max(0f, (hoverStartTime + areaData.hoverTime) - Time.time),
            Mathf.Max(0.0001f, areaData.hoverTime));

        if (Time.time >= hoverStartTime + areaData.hoverTime)
        {
            windupPhase = WindupPhase.Dropping;
            controller.Velocity = Vector2.zero;
        }
    }

    private void UpdateDropping()
    {
        float currentY = controller.transform.position.y;
        float deltaY = targetLandY - currentY;

        controller.Velocity = new Vector2(0f, -areaData.dropSpeed);
        UpdatePreview();

        if (deltaY > -areaData.landDeadZone)
        {
            if (!slamTriggered)
            {
                slamTriggered = true;
                SpawnSlamHitbox();
                ReturnPreview();
            }

            controller.Velocity = Vector2.zero;
            windupPhase = WindupPhase.Landed;
            FacePlayer();
        }
    }

    private void SpawnSlamHitbox()
    {
        if (areaData.meleeHitbox == null)
            return;

        MeleeHitbox meleeHitBox = WorldEntityPool.Instance.Spawn(
            areaData.meleeHitbox,
            recoverPoint.position,
            Quaternion.identity);

        meleeHitBox.Initialize(controller, areaData.slamDamageInfo);
        meleeHitBox.SetHitFeedback(null);
    }

    private void SpawnPreview()
    {
        if (areaData.previewLinePrefab == null)
            return;

        if (activePreview != null)
            return;

        activePreview = WorldEntityPool.Instance.Spawn(
            areaData.previewLinePrefab,
            executePoint.position,
            Quaternion.identity);

        activePreview.Show(
            executePoint.position,
            Vector2.down,
            areaData.previewLineLength,
            areaData.previewLineWidth,
            areaData.previewBlockMask,
            areaData.previewLineColor,
            areaData.hoverTime);
    }

    private void UpdatePreview()
    {
        if (activePreview == null)
            return;

        activePreview.SetOrigin(executePoint.position);
        activePreview.SetDirection(Vector2.down);
    }

    private void ReturnPreview()
    {
        if (activePreview == null)
            return;

        activePreview.ForceReturnToPool();
        activePreview = null;
    }

    private Vector2 CalculateTeleportTarget()
    {
        Vector2 currentPos = controller.transform.position;

        bool foundLeftWall = TryGetWallPoint(-1, out RaycastHit2D leftHit);
        bool foundRightWall = TryGetWallPoint(1, out RaycastHit2D rightHit);

        if (!foundLeftWall && !foundRightWall)
            return currentPos + Vector2.up * areaData.teleportHeightOffset;

        float targetX;

        if (foundLeftWall && foundRightWall)
        {
            float leftDistance = Mathf.Abs(currentPos.x - leftHit.point.x);
            float rightDistance = Mathf.Abs(currentPos.x - rightHit.point.x);

            bool leftIsCloser = leftDistance <= rightDistance;

            if (leftIsCloser)
                targetX = leftHit.point.x + Mathf.Abs(areaData.wallOffsetX);
            else
                targetX = rightHit.point.x - Mathf.Abs(areaData.wallOffsetX);
        }
        else if (foundLeftWall)
        {
            targetX = leftHit.point.x + Mathf.Abs(areaData.wallOffsetX);
        }
        else
        {
            targetX = rightHit.point.x - Mathf.Abs(areaData.wallOffsetX);
        }

        float targetY = GetGroundY(targetX) + areaData.teleportHeightOffset;
        return new Vector2(targetX, targetY);
    }

    private bool TryGetWallPoint(int dir, out RaycastHit2D hit)
    {
        Vector2 origin = new Vector2(
            controller.transform.position.x,
            controller.transform.position.y + areaData.wallCheckHeightOffset);

        Vector2 direction = dir > 0 ? Vector2.right : Vector2.left;

        hit = Physics2D.Raycast(
            origin,
            direction,
            areaData.wallSearchDistance,
            areaData.teleportBlockMask);

        return hit.collider != null;
    }

    private float GetGroundY(float x)
    {
        float originY = controller.transform.position.y + areaData.groundCheckStartHeight;

        RaycastHit2D hit = Physics2D.Raycast(
            new Vector2(x, originY),
            Vector2.down,
            areaData.groundCheckDistance,
            areaData.teleportBlockMask);

        if (hit.collider != null)
            return hit.point.y;

        return controller.transform.position.y;
    }

    private void FacePlayer()
    {
        if (playerTr == null)
            return;

        float deltaX = playerTr.position.x - controller.transform.position.x;

        if (Mathf.Abs(deltaX) <= areaData.faceDeadZone)
            return;

        controller.FacingUpdate(deltaX > 0f ? 1 : -1);
    }
}