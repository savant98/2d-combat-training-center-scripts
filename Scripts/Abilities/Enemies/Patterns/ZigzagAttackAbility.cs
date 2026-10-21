using UnityEngine;

public class ZigzagAttackAbility : AiAbility
{
    private Transform playerTr;
    private ZigzagAttackDataSO zigzagData;
    private int dashDirection;

    //turning cycle
    private bool isCycleTurning;
    private float cycleMoveTimer;
    private float cycleTurnTimer;
    private Vector2 turnStartVelocity;

    // scatter projectile
    private float lastTimeShot = -999f;
    private int currentProjectileCount = 0;
    private ProjectileBase projectile;

    // recover
    private MeleeHitbox hitbox;
    private LaserPreviewLine activePreview;
    private float recoverTargetY;
    private bool recoverSlamTriggered;
    private void Awake()
    {
        zigzagData = (ZigzagAttackDataSO)data;
        hitbox = zigzagData.meleeHitbox;
    }

    protected override void OnWindupStart()
    {
        playerTr = GameManager.Instance.Player.transform;
        controller.Velocity = Vector2.zero;
        ReturnPreview();

        if (playerTr != null)
        {
            float playerDeltaX = playerTr.position.x - controller.transform.position.x;

            if (Mathf.Abs(playerDeltaX) > zigzagData.faceDeadZone)
                controller.FacingUpdate(playerDeltaX > 0f ? 1 : -1);
        }

        dashDirection = controller.Facing;
    }

    protected override void OnWindup(float t)
    {
        if (playerTr == null)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        // 플레이어 기준으로 "멀어지는" 방향 유지
        dashDirection = controller.transform.position.x < playerTr.position.x ? -1 : 1;
        controller.FacingUpdate(dashDirection);

        float targetX = playerTr.position.x + zigzagData.windupRiseOffset.x * dashDirection;
        float targetY = GetGroundY(targetX) + zigzagData.windupRiseOffset.y;

        Vector2 currentPos = controller.transform.position;
        Vector2 delta = new Vector2(targetX - currentPos.x, targetY - currentPos.y);

        float vx = 0f;
        float vy = 0f;

        if (Mathf.Abs(delta.x) > zigzagData.windupDeadZone)
            vx = Mathf.Sign(delta.x) * zigzagData.windupMoveSpeedX;

        if (Mathf.Abs(delta.y) > zigzagData.windupDeadZone)
            vy = Mathf.Sign(delta.y) * zigzagData.windupMoveSpeedY;

        controller.Velocity = new Vector2(vx, vy);


        float playerDeltaX = playerTr.position.x - controller.transform.position.x;

        if (Mathf.Abs(playerDeltaX) > zigzagData.faceDeadZone)
            controller.FacingUpdate(playerDeltaX > 0f ? 1 : -1);
    }

    protected override void OnExecuteStart()
    {
        if (playerTr != null)
        {
            dashDirection = controller.transform.position.x < playerTr.position.x ? 1 : -1;
            controller.FacingUpdate(dashDirection);
        }

        isCycleTurning = false;
        cycleMoveTimer = 0f;
        cycleTurnTimer = 0f;
        turnStartVelocity = Vector2.zero;

        // scatter Projectile Initialize
        lastTimeShot = Time.time;
        currentProjectileCount = 1;
    }
    protected override void OnExecute(float t)
    {
        // 재배치 중
        if (isCycleTurning)
        {
            // 발사체 발사
            if (currentProjectileCount < zigzagData.projectileCount && Time.time >= lastTimeShot + zigzagData.scatterInterval)
            {
                ScatterProjectile();
            }
            
            cycleTurnTimer += Time.deltaTime;

            float turnProgress = Mathf.Clamp01(cycleTurnTimer / zigzagData.cycleTurnDuration);

            float speedX = Mathf.Abs(turnStartVelocity.x) * Mathf.Pow(Mathf.Cos(turnProgress * Mathf.PI * 0.5f), 3f);
            float speedY = Mathf.Abs(zigzagData.cycleTurnRiseSpeed) * Mathf.Pow(Mathf.Cos(turnProgress * Mathf.PI * 0.5f), 3f);

            float vx = -Mathf.Sign(turnStartVelocity.x) * speedX;
            float vy = Mathf.Sign(zigzagData.cycleTurnRiseSpeed) * speedY;

            controller.Velocity = new Vector2(vx, vy);

            if (turnProgress >= 1f)
            {
                isCycleTurning = false;
                cycleTurnTimer = 0f;
                cycleMoveTimer = 0f;

                if (playerTr != null)
                    dashDirection = controller.transform.position.x < playerTr.position.x ? 1 : -1;

                controller.FacingUpdate(dashDirection);
            }

            return;
        }

        // 내려찍기 중

        // 총알 초기화
        currentProjectileCount = 0;

        cycleMoveTimer += Time.deltaTime;

        float cycleDuration = Mathf.Max(0.0001f, zigzagData.executeCycleDuration);
        float cycleNormalized = Mathf.Clamp01(cycleMoveTimer / cycleDuration);
        float phase = cycleNormalized * Mathf.PI * 2f;

        int xDirection = dashDirection;

        float cycleProgress = (phase % (Mathf.PI * 2f)) / (Mathf.PI * 2f);
        float speedScaleX = Mathf.Sin((cycleProgress - 0.15f) * Mathf.PI * 0.5f);
        float speedScaleY = Mathf.Cos((cycleProgress) * Mathf.PI * 0.5f);

        controller.Velocity.x = zigzagData.executeHorizontalSpeed * speedScaleX * xDirection;
        controller.Velocity.y = -Mathf.Sin(phase) * zigzagData.executeVerticalAmplitude * speedScaleY;

        if (cycleMoveTimer >= cycleDuration)
        {
            isCycleTurning = true;
            cycleTurnTimer = 0f;
            turnStartVelocity = controller.Velocity;
        }
    }
    protected override void OnRecoverStart()
    {
        recoverSlamTriggered = false;

        float currentX = controller.transform.position.x;
        recoverTargetY = GetGroundY(currentX) + zigzagData.recoverGroundOffset;

        controller.Velocity = Vector2.zero;

        SpawnPreview();
        UpdatePreview();
    }
    protected override void OnRecover(float t)
    {
        float currentY = controller.transform.position.y;
        float deltaY = recoverTargetY - currentY;

        // 이미 목표 높이에 거의 도달했으면 정지
        if (Mathf.Abs(deltaY) <= zigzagData.windupDeadZone)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        // 초반: 잠깐 멈추거나 아주 살짝 상승
        if (t < zigzagData.recoverHoldTime)
        {
            float vx = 0f;

            if (playerTr != null)
            {
                float deltaX = playerTr.position.x - controller.transform.position.x;

                if (Mathf.Abs(deltaX) > zigzagData.windupDeadZone)
                {
                    vx = Mathf.Sign(deltaX) * zigzagData.recoverTrackSpeed;
                }
            }

            controller.Velocity = new Vector2(vx, zigzagData.recoverRiseSpeed);

            UpdatePreview();
            activePreview?.UpdatePreviewVisual(
                zigzagData.recoverHoldTime - t,
                zigzagData.recoverHoldTime);

            return;
        }

        // 이후: 수직으로 내려찍기
        controller.Velocity = new Vector2(0f, -zigzagData.recoverDropSpeed);

        // 바닥 근처면 마무리
        if (deltaY > -zigzagData.windupDeadZone)
        {
            if (!recoverSlamTriggered)
            {
                recoverSlamTriggered = true;
                feedbackPlayer.Play(zigzagData.slamFeedback, controller.transform);
                ReturnPreview();

                MeleeHitbox meleeHitBox = WorldEntityPool.Instance.Spawn(
                    hitbox,
                    recoverPoint.position,
                    Quaternion.identity);

                meleeHitBox.Initialize(controller, zigzagData.slamDamageInfo);
                meleeHitBox.SetHitFeedback(null);
            }

            controller.Velocity = Vector2.zero;
        }
    }

    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;
        ReturnPreview();
    }
    private void ScatterProjectile()
    {
        ProjectileSpawnUtility.SpawnValidationSettings validationSettings =
            new ProjectileSpawnUtility.SpawnValidationSettings
            {
                checkRadius = zigzagData.projectileSpawnCheckRadius,
                blockMask = zigzagData.projectileSpawnBlockMask,
                overlapMask = zigzagData.projectileOverlapMask
            };

        Vector2 spawnPos = ProjectileSpawnUtility.GetValidSpawnPosition(
            GetPlayerGroundAnchor(),
            zigzagData.projectileSpawnOffset,
            currentProjectileCount,
            ProjectileSpawnUtility.SpawnPattern.AlternatingHorizontal,
            validationSettings
        );

        projectile = WorldEntityPool.Instance.Spawn(
            zigzagData.projectile,
            spawnPos,
            controller.transform.rotation
        );

        if (projectile is AngleChargeShot angleChargeShot)
        {
            angleChargeShot.SetAngleFromVertical(zigzagData.projectileShotAngle);
        }

        projectile.Initialize(
            controller,
            zigzagData.projectileDamageInfo,
            zigzagData.projectileSpeed,
            currentProjectileCount,
            zigzagData.groundCheckMask
        );

        currentProjectileCount++;
        lastTimeShot = Time.time;
    }
    private Vector2 GetPlayerGroundAnchor()
    {
        Vector2 pos = playerTr.position;
        pos.y = GetGroundY(playerTr.position.x);
        return pos;
    }
    private float GetGroundY(float x)
    {
        if (playerTr == null)
            return controller.transform.position.y;

        Vector2 origin = new Vector2(x, playerTr.position.y + zigzagData.groundCheckStartHeight);

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            Vector2.down,
            zigzagData.groundCheckDistance,
            zigzagData.groundCheckMask);

        if (hit.collider != null)
            return hit.point.y;

        return playerTr.position.y;
    }
    private void SpawnPreview()
    {
        if (zigzagData.previewLinePrefab == null)
            return;

        if (activePreview != null)
            return;

        activePreview = WorldEntityPool.Instance.Spawn(
            zigzagData.previewLinePrefab,
            executePoint.position,
            Quaternion.identity);

        activePreview.Show(
            executePoint.position,
            Vector2.down,
            zigzagData.previewLineLength,
            zigzagData.previewLineWidth,
            zigzagData.previewBlockMask,
            zigzagData.previewLineColor,
            zigzagData.recoverHoldTime);
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
}