using UnityEngine;

public class TeleportSweepLaserAbility : AiAbility
{
    [Header("References")]
    [SerializeField] private Renderer[] casterRenderers;

    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    private EnemyTeleporter teleporter;
    private Transform playerTr;
    private Material[] casterMaterials;

    // teleport
    private bool teleportTriggered;
    private bool teleportFinished;
    private bool teleportedToRightSide;
    private Vector2 teleportAnchor;

    // move
    private float currentMoveVelocityX;
    private float currentMoveVelocityY;

    // shot
    private int firedCount;
    private float shotIntervalTimer;
    private bool previewActiveForCurrentShot;

    // locked aim
    private bool aimInitialized;
    private Vector2 currentAimDirection;

    private LaserPreviewLine activePreview;
    private LaserBeam activeLaser;

    private TeleportSweepLaserData SweepData => (TeleportSweepLaserData)data;

    private void Awake()
    {
        casterMaterials = new Material[casterRenderers.Length];

        for (int i = 0; i < casterRenderers.Length; i++)
            casterMaterials[i] = casterRenderers[i].material;

        SetCasterEmission(SweepData.idleEmissionColor);
    }

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        playerTr = GameManager.Instance.Player.transform;

        teleportTriggered = false;
        teleportFinished = false;
        teleportedToRightSide = false;
        teleportAnchor = Vector2.zero;

        currentMoveVelocityX = 0f;
        currentMoveVelocityY = 0f;

        firedCount = 0;
        shotIntervalTimer = 0f;
        previewActiveForCurrentShot = false;

        aimInitialized = false;
        currentAimDirection = Vector2.zero;

        activeLaser = null;
        activePreview = null;

        controller.Velocity = Vector2.zero;
        SetCasterEmission(SweepData.idleEmissionColor);
    }

    protected override void OnWindup(float t)
    {
        if (!teleportTriggered && t < SweepData.teleportTriggerTime)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!teleportTriggered && t >= SweepData.teleportTriggerTime)
        {
            teleportTriggered = true;

            teleportAnchor = GetTeleportTarget(
                playerTr.position,
                controller.transform.position,
                out bool targetIsRightSide);

            teleportedToRightSide = targetIsRightSide;

            teleporter.TeleportSequence(
                teleportAnchor,
                feedbackPlayer,
                SweepData.teleportStartFx,
                SweepData.teleportEndFx,
                SweepData.teleportDisappearDelay,
                SweepData.teleportAppearDelay,
                () =>
                {
                    teleportFinished = true;
                    controller.Velocity = Vector2.zero;
                    controller.FacingUpdate(teleportedToRightSide ? -1 : 1);
                });

            return;
        }

        if (!teleportFinished)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        controller.Velocity = Vector2.zero;

        float normalized = Mathf.InverseLerp(SweepData.teleportTriggerTime, data.windupTime, t);
        Color color = Color.Lerp(SweepData.idleEmissionColor, SweepData.chargeEmissionColor, normalized);
        SetCasterEmission(color);
    }

    protected override void OnExecuteStart()
    {
        firedCount = 0;
        shotIntervalTimer = 0f;
        previewActiveForCurrentShot = false;

        aimInitialized = false;
        currentAimDirection = Vector2.zero;

        currentMoveVelocityX = 0f;
        currentMoveVelocityY = 0f;

        controller.Velocity = Vector2.zero;
        SetCasterEmission(SweepData.fireEmissionColor);
    }

    protected override void OnExecute(float t)
    {
        UpdateExecuteMovement();
        UpdateShotCycle(Time.deltaTime, t);
        UpdateActiveLaser();

        SetCasterEmission(SweepData.fireEmissionColor);
    }

    protected override void OnRecoverStart()
    {
        controller.Velocity = Vector2.zero;

        ReturnPreview();
        ReturnLaser();

        currentMoveVelocityX = 0f;
        currentMoveVelocityY = 0f;
        aimInitialized = false;
        currentAimDirection = Vector2.zero;

        SetCasterEmission(SweepData.idleEmissionColor);
    }

    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;

        ReturnPreview();
        ReturnLaser();

        currentMoveVelocityX = 0f;
        currentMoveVelocityY = 0f;
        aimInitialized = false;
        currentAimDirection = Vector2.zero;

        SetCasterEmission(SweepData.idleEmissionColor);
    }
    private void UpdateExecuteMovement()
    {
        if (!teleportFinished || playerTr == null)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        float currentX = controller.transform.position.x;
        float currentY = controller.transform.position.y;

        float deltaFromPlayerX = playerTr.transform.position.x - currentX;
        float deltaY = teleportAnchor.y - currentY;

        float moveDirX;
        if (Mathf.Abs(deltaFromPlayerX) <= SweepData.moveArrivalThreshold)
            moveDirX = teleportedToRightSide ? 1f : -1f;
        else
            moveDirX = Mathf.Sign(deltaFromPlayerX);

        float desiredVelocityX = moveDirX * SweepData.moveMaxSpeed;

        currentMoveVelocityX = Mathf.MoveTowards(
            currentMoveVelocityX,
            desiredVelocityX,
            SweepData.moveAcceleration * Time.deltaTime);

        float brakingDistanceY =
            (currentMoveVelocityY * currentMoveVelocityY) /
            (2f * Mathf.Max(0.01f, SweepData.moveDeceleration));

        bool shouldBrakeY = Mathf.Abs(deltaY) <= brakingDistanceY + SweepData.moveArrivalThreshold;

        float desiredVelocityY = shouldBrakeY
            ? 0f
            : Mathf.Sign(deltaY) * SweepData.moveMaxSpeed;

        float accelY = shouldBrakeY
            ? SweepData.moveDeceleration
            : SweepData.moveAcceleration;

        currentMoveVelocityY = Mathf.MoveTowards(
            currentMoveVelocityY,
            desiredVelocityY,
            accelY * Time.deltaTime);

        controller.Velocity = new Vector2(currentMoveVelocityX, currentMoveVelocityY);

        //if (Mathf.Abs(currentMoveVelocityX) > 0.01f)
        //    controller.FacingUpdate(currentMoveVelocityX > 0f ? 1 : -1);
    }
    private void UpdateShotCycle(float deltaTime, float t)
    {
        if (firedCount >= SweepData.maxShotCount)
        {
            ReturnPreview();
            previewActiveForCurrentShot = false;
            return;
        }

        shotIntervalTimer += deltaTime;

        float currentPreviewLead = firedCount == 0
            ? SweepData.firstShotPreviewLeadTime
            : SweepData.previewLeadTime;

        currentPreviewLead = Mathf.Min(currentPreviewLead, SweepData.shotInterval);

        bool shouldShowPreview = shotIntervalTimer >= (SweepData.shotInterval - currentPreviewLead);

        if (shouldShowPreview)
        {
            if (!previewActiveForCurrentShot)
            {
                previewActiveForCurrentShot = true;
                InitializeAimDirectionIfNeeded();
                SpawnPreview();
            }

            UpdateAimTracking(deltaTime);
            UpdatePreviewTracked();

            float remaining = Mathf.Max(0f, SweepData.shotInterval - shotIntervalTimer);
            activePreview?.UpdatePreviewVisual(remaining, Mathf.Max(0.0001f, currentPreviewLead));
        }
        else
        {
            ReturnPreview();
            previewActiveForCurrentShot = false;
        }

        if (shotIntervalTimer < SweepData.shotInterval)
            return;

        FireLaser();
        firedCount++;

        shotIntervalTimer = 0f;
        previewActiveForCurrentShot = false;

        ReturnPreview();
    }
    private void InitializeAimDirectionIfNeeded()
    {
        if (aimInitialized)
            return;

        Vector2 desiredDir = GetDesiredAimDirection();

        currentAimDirection = desiredDir;
        aimInitialized = true;
    }

    private void UpdateAimTracking(float deltaTime)
    {
        InitializeAimDirectionIfNeeded();

        Vector2 desiredDir = GetDesiredAimDirection();

        float currentAngle = Mathf.Atan2(currentAimDirection.y, currentAimDirection.x) * Mathf.Rad2Deg;
        float desiredAngle = Mathf.Atan2(desiredDir.y, desiredDir.x) * Mathf.Rad2Deg;

        float trackSpeed = GetAngleScaledAimTrackSpeed(currentAimDirection);

        float nextAngle = Mathf.MoveTowardsAngle(
            currentAngle,
            desiredAngle,
            trackSpeed * deltaTime);

        float rad = nextAngle * Mathf.Deg2Rad;
        currentAimDirection = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
    }
    private Vector2 GetDesiredAimDirection()
    {
        if (playerTr == null)
            return teleportedToRightSide ? Vector2.left : Vector2.right;

        Vector2 origin = executePoint.position;
        Vector2 dir = (Vector2)playerTr.position - origin;

        if (dir.sqrMagnitude <= 0.0001f)
            dir = teleportedToRightSide ? Vector2.left : Vector2.right;

        return dir.normalized;
    }
    private float GetAngleScaledAimTrackSpeed(Vector2 desiredDir)
    {
        Vector2 downDir = Vector2.down;

        // 0 = 수직 아래, 180 = 수직 위
        float angleFromDown = Vector2.Angle(downDir, desiredDir);

        // 수직 아래일수록 1, 멀어질수록 0
        float t = 1f - Mathf.Clamp01(angleFromDown / 90f);

        // 곡선 적용
        // 값이 작을수록 완만, 클수록 아래 근처에서만 더 빠름
        t = Mathf.Pow(t, SweepData.curvePower);

        float speedMultiplier = Mathf.Lerp(SweepData.minMultiplier, 1f, t);
        return SweepData.aimTrackSpeed * speedMultiplier;
    }
    private void SpawnPreview()
    {
        if (SweepData.previewLinePrefab == null)
            return;

        if (activePreview != null)
            return;

        activePreview = WorldEntityPool.Instance.Spawn(
            SweepData.previewLinePrefab,
            executePoint.position,
            Quaternion.identity);

        activePreview.Show(
            executePoint.position,
            currentAimDirection,
            SweepData.laserLength,
            SweepData.previewLineWidth,
            SweepData.laserBlockMask,
            SweepData.previewLineColor,
            data.executeTime);
    }
    private void UpdatePreviewTracked()
    {
        if (activePreview == null || !aimInitialized)
            return;

        activePreview.SetOrigin(executePoint.position);
        activePreview.SetDirection(currentAimDirection);
    }

    private void FireLaser()
    {
        if (SweepData.laserPrefab == null)
            return;

        InitializeAimDirectionIfNeeded();  

        ReturnLaser();

        activeLaser = WorldEntityPool.Instance.Spawn(
            SweepData.laserPrefab,
            executePoint.position,
            Quaternion.identity);

        activeLaser.Fire(
            SweepData.laserDamageInfo,
            executePoint.position,
            currentAimDirection,
            SweepData.laserLength,
            SweepData.laserDuration,
            SweepData.laserBlockMask,
            SweepData.laserDamageMask,
            SweepData.laserWidth,
            controller);
    }

    private void UpdateActiveLaser()
    {
        if (activeLaser == null)
            return;

        // 방향은 고정, 시작점만 현재 발사 위치를 따라감
        activeLaser.SetOrigin(executePoint.position);
    }

    private void ReturnLaser()
    {
        if (activeLaser == null)
            return;

        activeLaser.ForceReturnToPool();
        activeLaser = null;
    }

    private void ReturnPreview()
    {
        if (activePreview == null)
            return;

        activePreview.ForceReturnToPool();
        activePreview = null;
    }
    private Vector2 GetTeleportTarget(Vector2 playerPos, Vector2 enemyPos, out bool targetIsRightSide)
    {
        bool enemyIsLeftOfPlayer = enemyPos.x < playerPos.x;

        float rightX = playerPos.x + SweepData.teleportOffset.x;
        float leftX = playerPos.x - SweepData.teleportOffset.x;

        Vector2 rightTarget = new Vector2(
            rightX,
            GetGroundY(rightX) + SweepData.teleportOffset.y);

        Vector2 leftTarget = new Vector2(
            leftX,
            GetGroundY(leftX) + SweepData.teleportOffset.y);

        if (enemyIsLeftOfPlayer)
        {
            if (!IsBlocked(playerPos, rightTarget))
            {
                targetIsRightSide = true;
                return rightTarget;
            }

            targetIsRightSide = false;
            return leftTarget;
        }
        else
        {
            if (!IsBlocked(playerPos, leftTarget))
            {
                targetIsRightSide = false;
                return leftTarget;
            }

            targetIsRightSide = true;
            return rightTarget;
        }
    }
    private float GetGroundY(float x)
    {
        Vector2 origin = new Vector2(x, playerTr.position.y + 5f);

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            Vector2.down,
            50f,
            SweepData.teleportBlockMask);

        if (hit.collider != null)
            return hit.point.y;

        return playerTr.position.y;
    }
    private bool IsBlocked(Vector2 from, Vector2 to)
    {
        RaycastHit2D hit = Physics2D.Linecast(from, to, SweepData.teleportBlockMask);
        return hit.collider != null;
    }

    private void SetCasterEmission(Color color)
    {
        if (casterMaterials == null)
            return;

        for (int i = 0; i < casterMaterials.Length; i++)
        {
            if (casterMaterials[i] != null && casterMaterials[i].HasProperty(EmissionColorID))
                casterMaterials[i].SetColor(EmissionColorID, color);
        }
    }
}