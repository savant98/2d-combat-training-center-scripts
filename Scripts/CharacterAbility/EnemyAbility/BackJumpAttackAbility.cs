using UnityEngine;

public class BackJumpAttackAbility : AiAbility
{
    [Header("Chained Ability")]
    [SerializeField] protected AiAbility chainedNextAbility;
    [SerializeField] protected bool useChainedAbility = true;

    private Transform playerTr;
    private BackJumpAttackData backJumpData;

    private Vector2 currentAirVelocity;
    private int jumpDirection;
    private bool shotFired;
    private float recoverGroundY;

    private void Awake()
    {
        backJumpData = (BackJumpAttackData)data;
    }

    protected override void OnWindupStart()
    {
        playerTr = GameManager.Instance.Player.transform;

        shotFired = false;
        currentAirVelocity = Vector2.zero;

        if (playerTr == null)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        FacePlayer();

        // 플레이어 반대 방향으로 점프
        jumpDirection = controller.transform.position.x < playerTr.position.x ? -1 : 1;

        currentAirVelocity = new Vector2(
            jumpDirection * backJumpData.backJumpSpeedX,
            backJumpData.backJumpSpeedY
        );

        controller.Velocity = currentAirVelocity;
    }

    protected override void OnWindup(float t)
    {
        UpdateAirMovement(
            backJumpData.windupGravity,
            backJumpData.windupHorizontalDamping
        );
    }

    protected override void OnExecuteStart()
    {
        // Execute 시작 시 hover용 속도 고정
        currentAirVelocity.x *= backJumpData.executeHoverHorizontalMultiplier;
        currentAirVelocity.y = 0f;
        controller.Velocity = currentAirVelocity;

        FireAngleChargeShot();
        shotFired = true;
    }

    protected override void OnExecute(float t)
    {
        UpdateExecuteHover();
    }

    protected override void OnRecoverStart()
    {
        // hover 종료 후 자연스럽게 낙하 시작
        currentAirVelocity.y = backJumpData.recoverStartFallSpeed;
        controller.Velocity = currentAirVelocity;

        recoverGroundY = GetGroundY(controller.transform.position.x);
    }
    protected override void OnRecover(float t)
    {
        float currentX = controller.transform.position.x;
        recoverGroundY = GetGroundY(currentX);

        bool reachedGroundY =
            currentAirVelocity.y <= 0f &&
            controller.transform.position.y <= recoverGroundY + backJumpData.recoverGroundStopThreshold;

        if (controller.IsGrounded || reachedGroundY)
        {
            controller.Velocity = Vector2.zero;

            if (useChainedAbility && chainedNextAbility != null)
                brain.ForceNextAbility(chainedNextAbility);
            return;
        }

        UpdateAirMovement(
            backJumpData.recoverGravity,
            backJumpData.recoverHorizontalDamping
        );
    }
    private float GetGroundY(float x)
    {
        float originY = controller.transform.position.y + backJumpData.groundCheckStartHeight;

        RaycastHit2D hit = Physics2D.Raycast(
            new Vector2(x, originY),
            Vector2.down,
            backJumpData.groundCheckDistance,
            backJumpData.groundBlockMask
        );

        if (hit.collider != null)
            return hit.point.y;

        return controller.transform.position.y;
    }

    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;
        currentAirVelocity = Vector2.zero;
        shotFired = false;
    }

    private void UpdateAirMovement(float gravity, float horizontalDamping)
    {
        currentAirVelocity.y -= gravity * Time.deltaTime;

        currentAirVelocity.x = Mathf.MoveTowards(
            currentAirVelocity.x,
            0f,
            horizontalDamping * Time.deltaTime
        );

        controller.Velocity = currentAirVelocity;
    }

    private void UpdateExecuteHover()
    {
        // 공중에 잠깐 멈춘 느낌
        currentAirVelocity.y = Mathf.MoveTowards(
            currentAirVelocity.y,
            0f,
            backJumpData.executeHoverVerticalStabilize * Time.deltaTime
        );

        currentAirVelocity.x = Mathf.MoveTowards(
            currentAirVelocity.x,
            0f,
            backJumpData.executeHoverHorizontalDamping * Time.deltaTime
        );

        controller.Velocity = currentAirVelocity;
    }
    private void FireAngleChargeShot()
    {
        if (shotFired)
            return;

        if (backJumpData.projectilePrefab == null)
            return;

        Vector2 origin = executePoint.position;

        float angleFromVertical = backJumpData.shotAngleFromVertical;

        // 실제 발사 방향 계산
        Vector2 fireDir = CalculateShotDirection(angleFromVertical);

        // 발사 방향에 수직인 방향
        Vector2 sideDir = new Vector2(-fireDir.y, fireDir.x).normalized;

        int count = Mathf.Max(1, backJumpData.multiShotCount);

        if (count % 2 == 0)
            count += 1;

        int half = count / 2;

        for (int i = -half; i <= half; i++)
        {
            Vector2 spawnPos = origin + sideDir * (i * backJumpData.multiShotOffset);
            SpawnAngleChargeShot(spawnPos, angleFromVertical);
        }

        shotFired = true;
    }
    private Vector2 CalculateShotDirection(float angleFromVertical)
    {
        Vector2 down = Vector2.down;

        int horizontalSign = controller.Facing >= 0 ? 1 : -1;
        float signedAngle = angleFromVertical * -horizontalSign;

        Vector2 dir = Quaternion.Euler(0f, 0f, signedAngle) * down;
        return dir.normalized;
    }
    private void SpawnAngleChargeShot(Vector2 spawnPos, float angleFromVertical)
    {
        ProjectileBase projectile = WorldEntityPool.Instance.Spawn(
            backJumpData.projectilePrefab,
            spawnPos,
            Quaternion.identity
        );

        if (projectile is AngleChargeShot angleChargeShot)
            angleChargeShot.SetAngleFromVertical(angleFromVertical);

        projectile.Initialize(
            controller,
            backJumpData.projectileDamageInfo,
            backJumpData.projectileSpeed,
            0,
            backJumpData.projectileCollideLayer
        );
    }
    private void FacePlayer()
    {
        if (playerTr == null)
            return;

        float deltaX = playerTr.position.x - controller.transform.position.x;

        if (Mathf.Abs(deltaX) <= backJumpData.faceDeadZone)
            return;

        controller.FacingUpdate(deltaX > 0f ? 1 : -1);
    }
}