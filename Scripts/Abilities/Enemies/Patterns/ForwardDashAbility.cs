using UnityEngine;

public class ForwardDashAbility : AiAbility
{
    public override bool IsBodyAttackActive => State == AbilityState.Execute && Mathf.Abs(controller.Velocity.x) > .01f;

    // ForwardDash Settings
    private float dashDistance = 5f;  // 총 이동 거리
    private float dashDuration = 0.5f; // 대략적인 수행 시간
    private AnimationCurve speedCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

    // Additional Feedback
    private FeedbackDataSO sparkFeedback;
    private float sparkInterval = 0.1f;

    private Vector2 startPos;
    private float traveled;     // 이동 누적 거리
    private float directionX;
    private float sparkTimer;
    private LaserPreviewLine activePreview;
    private ForwardDashDataSO forwardDashData;

    private void Awake()
    {
        forwardDashData = (ForwardDashDataSO)data;
        dashDistance = forwardDashData.dashDistance;
        dashDuration = forwardDashData.dashDuration;
        speedCurve = forwardDashData.speedCurve;
        sparkFeedback = forwardDashData.sparkFeedback;
        sparkInterval = forwardDashData.sparkInterval;
    }
    protected override void OnWindupStart()
    {
        controller.Velocity.x = 0f;
        controller.FacingUpdate(GameManager.Instance.Player.transform.position.x - transform.position.x > 0 ? 1 : -1);

        SpawnPreview();
        UpdatePreview();
    }
    protected override void OnWindup(float t)
    {
        UpdatePreview();

        float remaining = data.windupTime - t;
        activePreview?.UpdatePreviewVisual(remaining, data.windupTime);
    }
    protected override void OnExecuteStart()
    {
        ReturnPreview();
        controller.OnWallHit -= HandleWallHit;
        controller.OnWallHit += HandleWallHit;

        startPos = transform.position;       // 시작 위치
        traveled = 0f;
        directionX = controller.Facing;      // +1 or -1

        sparkTimer = 0f;
        controller.Velocity.x = dashDistance / Mathf.Max(.001f, dashDuration) * speedCurve.Evaluate(0f) * directionX;
    }
    protected override void OnExecute(float t)
    {
        // 현재까지 이동거리
        float currentX = transform.position.x;
        traveled = Mathf.Abs(currentX - startPos.x);

        // 목표 거리 도달하면 종료
        if (traveled >= dashDistance)
        {
            ForceRecover();
            return;
        }

        // "진행률" (0~1)
        float normalized = traveled / dashDistance;

        // 커브 기반 속도 배율 (예: 처음 빠름 → 나중 느림)
        float speedFactor = speedCurve.Evaluate(normalized);

        // 최대 속도 = 전체 거리를 duration으로 이동한다고 가정
        float baseSpeed = dashDistance / dashDuration;

        // 최종 속도
        float finalSpeed = baseSpeed * speedFactor;

        // Velocity로만 이동
        controller.Velocity.x = finalSpeed * directionX;


        // Spark생성
        sparkTimer -= Time.deltaTime;

        if (sparkTimer <= 0f)
        {
            sparkTimer = sparkInterval; // 다음 생성까지 대기
            Vector3 offset = sparkFeedback.offset;
            Quaternion sparkRotation = Quaternion.identity;

            if (controller.Facing < 0)
            {
                offset.x *= -1f;
                sparkRotation = Quaternion.Euler(0, 180f, 0);
            }

            feedbackPlayer.PlayExact(sparkFeedback, executePoint.position + offset, sparkRotation);
        }
    }
    protected override void OnRecoverStart()
    {
        controller.OnWallHit -= HandleWallHit;
        controller.Velocity = Vector2.zero;
    }

    protected override void OnStop()
    {
        controller.OnWallHit -= HandleWallHit;
        controller.Velocity.x = 0f;
        ReturnPreview();
    }

    private void HandleWallHit()
    {
        if (State == AbilityState.Execute) ForceRecover();
    }

    private void OnDisable()
    {
        if (controller != null) controller.OnWallHit -= HandleWallHit;
    }

    private void SpawnPreview()
    {
        if (forwardDashData.prevLinePrefab == null)
            return;

        if (activePreview != null)
            return;

        activePreview = WorldEntityPool.Instance.Spawn(
            forwardDashData.prevLinePrefab,
            windupPoint.position,
            Quaternion.identity);

        Vector2 dir = controller.Facing > 0 ? Vector2.right : Vector2.left;

        activePreview.Show(
            windupPoint.position,
            dir,
            forwardDashData.prevLineLength,
            forwardDashData.prevLineWidth,
            forwardDashData.prevLineBlockMask,
            forwardDashData.prevLineColor,
            data.windupTime);
    }

    private void UpdatePreview()
    {
        if (activePreview == null)
            return;

        Vector2 dir = controller.Facing > 0 ? Vector2.right : Vector2.left;

        activePreview.SetOrigin(windupPoint.position);
        activePreview.SetDirection(dir);
    }

    private void ReturnPreview()
    {
        if (activePreview == null)
            return;

        activePreview.ForceReturnToPool();
        activePreview = null;
    }
}
