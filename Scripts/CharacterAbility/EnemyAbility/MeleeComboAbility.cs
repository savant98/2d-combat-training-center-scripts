using UnityEngine;

public class MeleeComboAbility : AiAbility
{
    private enum ExecutePhase
    {
        BeforeFirstTeleport,
        WaitingFirstTeleport,
        BeforeSecondTeleport,
        WaitingSecondTeleport,
        AirHold,
        Slam,
        Finished
    }

    private EnemyTeleporter teleporter;
    private Transform playerTr;
    private MeleeComboData comboData;

    // Windup
    private bool windupTeleportTriggered;
    private bool windupTeleportFinished;

    // Execute
    private ExecutePhase executePhase;
    private bool executeFirstTeleportTriggered;
    private bool executeSecondTeleportTriggered;

    private float secondTeleportCompleteTime;
    private float slamTargetGroundY;

    private void Awake()
    {
        comboData = (MeleeComboData)data;
    }

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        playerTr = GameManager.Instance.Player.transform;

        windupTeleportTriggered = false;
        windupTeleportFinished = false;

        controller.Velocity = Vector2.zero;
    }

    protected override void OnWindup(float t)
    {
        if (playerTr == null)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!windupTeleportTriggered && t < comboData.windupTeleportTriggerTime)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!windupTeleportTriggered && t >= comboData.windupTeleportTriggerTime)
        {
            windupTeleportTriggered = true;
            controller.Velocity = Vector2.zero;

            Vector2 target = GetWindupTeleportTarget();

            teleporter.TeleportSequence(
                target,
                feedbackPlayer,
                comboData.teleportStartFeedback,
                comboData.teleportEndFeedback,
                comboData.teleportDisappearDelay,
                comboData.teleportAppearDelay,
                () =>
                {
                    windupTeleportFinished = true;
                    controller.Velocity = Vector2.zero;

                    // 요구사항:
                    // Windup 텔레포트 완료 시 1회만 FacingUpdate
                    FacePlayerOnce();
                });

            return;
        }

        if (!windupTeleportFinished)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        controller.Velocity = Vector2.zero;
    }

    protected override void OnExecuteStart()
    {
        executeFirstTeleportTriggered = false;
        executeSecondTeleportTriggered = false;

        executePhase = ExecutePhase.BeforeFirstTeleport;
        secondTeleportCompleteTime = -999f;
        slamTargetGroundY = controller.transform.position.y;

        controller.Velocity = Vector2.zero;
    }

    protected override void OnExecute(float t)
    {
        if (playerTr == null)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        switch (executePhase)
        {
            case ExecutePhase.BeforeFirstTeleport:
                UpdateBeforeFirstTeleport(t);
                break;

            case ExecutePhase.WaitingFirstTeleport:
                controller.Velocity = Vector2.zero;
                break;

            case ExecutePhase.BeforeSecondTeleport:
                UpdateBeforeSecondTeleport(t);
                break;

            case ExecutePhase.WaitingSecondTeleport:
                controller.Velocity = Vector2.zero;
                break;

            case ExecutePhase.AirHold:
                UpdateAirHold();
                break;

            case ExecutePhase.Slam:
                UpdateSlam();
                break;

            case ExecutePhase.Finished:
                controller.Velocity = Vector2.zero;
                break;
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

        windupTeleportTriggered = false;
        windupTeleportFinished = false;

        executeFirstTeleportTriggered = false;
        executeSecondTeleportTriggered = false;
        executePhase = ExecutePhase.Finished;
    }

    private void UpdateBeforeFirstTeleport(float t)
    {
        controller.Velocity = Vector2.zero;

        if (executeFirstTeleportTriggered)
            return;

        if (t < comboData.firstTeleportTriggerTime)
            return;

        executeFirstTeleportTriggered = true;
        executePhase = ExecutePhase.WaitingFirstTeleport;

        Vector2 target = GetFirstExecuteTeleportTarget();

        teleporter.TeleportSequence(
            target,
            feedbackPlayer,
            comboData.teleportStartFeedback,
            comboData.teleportEndFeedback,
            comboData.teleportDisappearDelay,
            comboData.teleportAppearDelay,
            () =>
            {
                controller.Velocity = Vector2.zero;
                executePhase = ExecutePhase.BeforeSecondTeleport;

                FacePlayerOnce();
            });
    }

    private void UpdateBeforeSecondTeleport(float t)
    {
        controller.Velocity = Vector2.zero;

        if (executeSecondTeleportTriggered)
            return;

        if (t < comboData.secondTeleportTriggerTime)
            return;

        executeSecondTeleportTriggered = true;
        executePhase = ExecutePhase.WaitingSecondTeleport;
        Vector2 target = GetSecondExecuteTeleportTarget();

        teleporter.TeleportSequence(
            target,
            feedbackPlayer,
            comboData.teleportStartFeedback,
            comboData.teleportEndFeedback,
            comboData.teleportDisappearDelay,
            comboData.teleportAppearDelay,
            () =>
            {
                controller.Velocity = Vector2.zero;
                secondTeleportCompleteTime = Time.time;
                slamTargetGroundY = GetGroundY(controller.transform.position.x);
                executePhase = ExecutePhase.AirHold;

                FacePlayerOnce();
            });
    }

    private void UpdateAirHold()
    {
        controller.Velocity = new Vector2(0f, comboData.airRiseSpeed);

        if (Time.time < secondTeleportCompleteTime + comboData.airHoldDuration)
            return;

        executePhase = ExecutePhase.Slam;
    }

    private void UpdateSlam()
    {
        float currentX = controller.transform.position.x;
        slamTargetGroundY = GetGroundY(currentX);

        float currentY = controller.transform.position.y;
        bool reachedGround =
            controller.IsGrounded ||
            currentY <= slamTargetGroundY + comboData.groundStopThreshold;

        if (reachedGround)
        {
            controller.Velocity = Vector2.zero;
            executePhase = ExecutePhase.Finished;

            // 내려찍기 끝났으면 바로 Recover로 넘김
            //ForceRecover();
            return;
        }

        controller.Velocity = new Vector2(0f, -comboData.slamSpeed);
    }

    private Vector2 GetWindupTeleportTarget()
    {
        float offsetX = GetFrontSideOffsetX();
        float targetX = playerTr.position.x + offsetX;
        float groundY = GetGroundY(targetX);

        Vector2 target = new Vector2(targetX, groundY);

        if (!IsBlocked(controller.transform.position, target))
            return target;

        // 막혀 있으면 반대편 fallback
        float fallbackX = playerTr.position.x - offsetX;
        float fallbackGroundY = GetGroundY(fallbackX);
        return new Vector2(fallbackX, fallbackGroundY);
    }

    private Vector2 GetFirstExecuteTeleportTarget()
    {
        float offsetX = GetBackSideOffsetX();
        float targetX = playerTr.position.x + offsetX;
        float groundY = GetGroundY(targetX);

        Vector2 target = new Vector2(targetX, groundY);

        if (!IsBlocked(controller.transform.position, target))
            return target;

        // fallback
        float fallbackX = playerTr.position.x - offsetX;
        float fallbackGroundY = GetGroundY(fallbackX);
        return new Vector2(fallbackX, fallbackGroundY);
    }

    private Vector2 GetSecondExecuteTeleportTarget()
    {
        // 반대방향 출현
        float offsetX = -GetBackSideOffsetX();
        float targetX = playerTr.position.x + offsetX;
        float groundY = GetGroundY(targetX) + comboData.airTeleportOffsetY;

        Vector2 target = new Vector2(targetX, groundY);

        if (!IsBlocked(controller.transform.position, target))
            return target;

        // fallback
        float fallbackX = playerTr.position.x - offsetX;
        float fallbackGroundY = GetGroundY(fallbackX) + comboData.airTeleportOffsetY;
        return new Vector2(fallbackX, fallbackGroundY);
    }

    private float GetFrontSideOffsetX()
    {
        // 플레이어의 정면 = 플레이어가 나를 기준으로 어느 쪽에 있는가가 아니라,
        // "현재 enemy가 player의 어느 쪽에 있는가" 기준으로 맞은편/정면을 잡는 기존 패턴과 유사하게 처리.
        bool enemyIsLeftOfPlayer = controller.transform.position.x < playerTr.position.x;
        float abs = Mathf.Abs(comboData.windupFrontOffset.x);

        return enemyIsLeftOfPlayer ? abs : -abs;
    }

    private float GetBackSideOffsetX()
    {
        // 플레이어 후방 = Windup에서 섰던 쪽의 반대편
        bool enemyIsLeftOfPlayer = controller.transform.position.x < playerTr.position.x;
        float abs = Mathf.Abs(comboData.backTeleportOffsetX);

        return enemyIsLeftOfPlayer ? -abs : abs;
    }

    private void FacePlayerOnce()
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
        float originY = playerTr != null
            ? playerTr.position.y + comboData.groundCheckStartHeight
            : controller.transform.position.y + comboData.groundCheckStartHeight;

        RaycastHit2D hit = Physics2D.Raycast(
            new Vector2(x, originY),
            Vector2.down,
            comboData.groundCheckDistance,
            comboData.groundCheckMask
        );

        if (hit.collider != null)
            return hit.point.y;

        return playerTr != null ? playerTr.position.y : controller.transform.position.y;
    }

    private bool IsBlocked(Vector2 from, Vector2 to)
    {
        RaycastHit2D hit = Physics2D.Linecast(from, to, comboData.teleportBlockMask);
        return hit.collider != null;
    }
}