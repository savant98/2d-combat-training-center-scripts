using UnityEngine;

public class DownAttackAbility : AiAbility
{
    // basic
    private EnemyTeleporter teleporter;
    private MeleeHitbox hitbox;
    private Transform playerTr;

    // teleport
    private bool teleportTriggered;
    private bool teleportFinished;

    private Vector2 teleportTarget;

    private float teleportTriggerTime;
    private float disappearDelay;
    private float appearDelay;
    private Vector2 teleportOffset;
    private FeedbackDataSO teleportStartFx;
    private FeedbackDataSO teleportEndFx;

    // slam
    private float windupRiseSpeed;
    private float windupFollowSpeed;
    private float slamSpeed;

    // recover
    private float recoverBounceHeight;
    private float recoverStartY;
    private float recoverTargetY;

    // preview line
    private LaserPreviewLine activePreview;

    // data
    private DownAttackDataSO downAttackData;

    private void Awake()
    {
        downAttackData = (DownAttackDataSO)data;

        // melee hitbox
        hitbox = downAttackData.meleeHitbox;

        // teleport data
        teleportTriggerTime = downAttackData.teleportTriggerTime;
        disappearDelay = downAttackData.teleportDisappearDelay;
        appearDelay = downAttackData.teleportAppearDelay;
        teleportOffset = downAttackData.teleportOffset;
        teleportStartFx = downAttackData.teleportStartFeedback;
        teleportEndFx = downAttackData.teleportEndFeedback;

        // slam data
        windupFollowSpeed = downAttackData.windupFollowSpeed;
        windupRiseSpeed = downAttackData.windupRiseSpeed;
        slamSpeed = downAttackData.slamSpeed;
        recoverBounceHeight = downAttackData.recoverBounceHeight;
    }
    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        playerTr = GameManager.Instance.Player.transform;

        teleportTriggered = false;
        teleportFinished = false;

        ReturnPreview();
        controller.Velocity = Vector3.zero;
    }

    protected override void OnWindup(float t)
    {
        // 텔포 시작 전에는 멈춤
        if (!teleportTriggered && t < teleportTriggerTime)
        {
            controller.Velocity = Vector2.zero;
        }

        // 텔레포트 전 시작 타이밍 1회 실행
        if (!teleportTriggered && t >= teleportTriggerTime)
        {
            teleportTriggered = true;
            controller.Velocity = Vector2.zero;

            // 목표 위치 계산 -> Player 머리 위
            Vector2 point = playerTr.position;
            teleportTarget = GetTeleportTarget(point, controller.transform.position);

            // 텔포 시퀀스 실행
            teleporter.TeleportSequence(
                teleportTarget,
                feedbackPlayer,
                teleportStartFx,
                teleportEndFx,
                disappearDelay,
                appearDelay, 
                () => 
                { 
                    teleportFinished = true;
                    controller.Velocity = Vector2.zero;
                    SpawnPreview();
                    UpdatePreview();
                });

            return;
        }

        // 텔포 중에는 이동 금지
        if (!teleportFinished)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        // 텔레포트 끝난 후 Execute 직전까지 player 추적
        float targetX = playerTr.position.x + teleportOffset.x;
        float deltaX = targetX - controller.transform.position.x;

        float xVelocity = 0f;
        float deadZone = 0.05f;

        if (Mathf.Abs(deltaX) > deadZone)
            xVelocity = Mathf.Sign(deltaX) * windupFollowSpeed;

        controller.Velocity = new Vector2(xVelocity, windupRiseSpeed);
        controller.FacingUpdate(playerTr.position.x - transform.position.x > 0 ? 1 : -1);

        float remaining = data.windupTime - t;
        UpdatePreview();
        activePreview?.UpdatePreviewVisual(remaining, data.windupTime);
    }
    private Vector2 GetTeleportTarget(Vector2 playerPos, Vector2 enemyPos)
    {
        bool enemyIsLeftOfPlayer = enemyPos.x < playerPos.x;

        float targetX = playerPos.x + downAttackData.teleportOffset.x;

        Vector2 targetPos = new Vector2(
            targetX,
            GetGroundY(targetX) + downAttackData.teleportOffset.y);

        return targetPos;
    }
    private float GetGroundY(float x)
    {
        Vector2 origin = new Vector2(x, playerTr.position.y + 5f);

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            Vector2.down,
            50f,
            downAttackData.previewBlockMask);

        if (hit.collider != null)
            return hit.point.y;

        return playerTr.position.y;
    }
    protected override void OnExecuteStart()
    {
        // 내려찍기 직전에 추적 종료
        controller.Velocity = Vector2.zero;
        //ReturnPreview();
    }
    protected override void OnExecute(float t)
    {
        controller.Velocity = new Vector2(0f, -slamSpeed);
        UpdatePreview();
    }

    protected override void OnRecoverStart()
    {
        controller.Velocity = Vector2.zero;
        ReturnPreview();

        recoverStartY = controller.transform.position.y;
        recoverTargetY = recoverStartY + recoverBounceHeight;

        MeleeHitbox meleeHitBox = WorldEntityPool.Instance.Spawn(hitbox, recoverPoint.position, Quaternion.identity);
        meleeHitBox.Initialize(controller, downAttackData.damageInfo);
        meleeHitBox.SetHitFeedback(null);
    }

    protected override void OnRecover(float t)
    {
        float duration = Mathf.Max(0.0001f, data.recoverTime);
        float nomalized = Mathf.Clamp01(t / duration);

        // 처음 빠르고, 갈수록 느려짐
        float easeT = 1f - Mathf.Pow(1f - nomalized, 3f);

        float desiredY = Mathf.Lerp(recoverStartY, recoverTargetY, easeT);
        float neededVelocityY = (desiredY - controller.transform.position.y) / Time.deltaTime;

        // velocityY 음수 방지
        neededVelocityY = Mathf.Max(0f, neededVelocityY);

        controller.Velocity = new Vector2(0f, neededVelocityY);
    }

    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;
        ReturnPreview();
    }
    private void SpawnPreview()
    {
        if (downAttackData.previewLinePrefab == null)
            return;

        if (activePreview != null)
            return;

        activePreview = WorldEntityPool.Instance.Spawn(
            downAttackData.previewLinePrefab,
            executePoint.position,
            Quaternion.identity);

        activePreview.Show(
            executePoint.position,
            Vector2.down,
            downAttackData.previewLineLength,
            downAttackData.previewLineWidth,
            downAttackData.previewBlockMask,
            downAttackData.previewLineColor,
            data.windupTime);
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
