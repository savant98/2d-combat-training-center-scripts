using UnityEngine;

public class ParabolicDashAbility : AiAbility
{
    [SerializeField] private bool targetRoomCenter;
    [SerializeField] private AiAbility followupAbility;
    public bool TargetsRoomCenter => targetRoomCenter;
    public Vector2 CenterTarget { get; private set; }
    public bool ArrivedAtCenter { get; private set; }
    private bool centerTargetValid;
    private bool launchedForCenter;

    // ParabolicSettings
    private float maxHeight = 4f;
    private float flyDuration = 1f;
    private float maxDistance = 10f;

    private Vector3 playerPos;
    private ParabolicDashDataSO parabolicDashData;
    private void Awake()
    {
        parabolicDashData = (ParabolicDashDataSO)data;
        maxHeight = parabolicDashData.maxHeight;
        flyDuration = parabolicDashData.flyDuration;
        maxDistance = parabolicDashData.maxDistance;
    }
    protected override void OnWindupStart()
    {
        ArrivedAtCenter = launchedForCenter = false;
        if (targetRoomCenter)
        {
            centerTargetValid = TryFindRoomCenter(out var target);
            if (!centerTargetValid) { StopAbility(); return; }
            CenterTarget = target;
            playerPos = target;
            controller.OnLanded -= HandleCenterLanding;
            controller.OnLanded += HandleCenterLanding;
        }
        else playerPos = GameManager.Instance.Player.transform.position;
        controller.FacingUpdate(playerPos.x - transform.position.x > 0 ? 1 : -1);
    }
    protected override void OnExecuteStart()
    {
        launchedForCenter = targetRoomCenter;
        Vector3 startPos = transform.position;

        Vector3 targetPos = new Vector3(playerPos.x, transform.position.y, transform.position.z);
        // 최대 이동 거리 설정
        float maxDistanceCalculate = transform.position.x - playerPos.x;
        if (Mathf.Abs(maxDistanceCalculate) > maxDistance)
        {
            targetPos.x += maxDistanceCalculate > 0 ? maxDistanceCalculate - maxDistance : maxDistanceCalculate + maxDistance;
        }

        float g = -controller.Gravity;

        Vector3 delta = targetPos - startPos;
        float deltaX = delta.x;
        float deltaY = delta.y;

        // 시간 기반 초기속도 계산
        float v_x = deltaX / flyDuration;
        float v_y = (deltaY + 0.5f * g * flyDuration * flyDuration) / flyDuration;

        //최고점 제한 (출발점 기준 상대 높이)
        float currentPeak = startPos.y + (v_y * v_y) / (2f * g);
        float maxPeak = startPos.y + maxHeight;

        if (currentPeak > maxPeak)
        {
            // 최고점 제한 적용
            v_y = Mathf.Sqrt(2f * g * maxHeight);
            // Player 위치에 정확히 도달하도록 flyDuration 보정
            // t = (v_y + Mathf.Sqrt(v_y*v_y + 2*g*deltaY)) / g
            float t = (v_y + Mathf.Sqrt(v_y * v_y + 2f * g * deltaY)) / g;
            v_x = deltaX / t;
        }

        controller.Velocity.x = v_x;
        controller.Velocity.y = v_y;
    }
    protected override void OnRecoverStart()
    {
        controller.Velocity = Vector2.zero;
    }

    // 목표 외의 발사식·중력·Animation·Feedback은 기존 경로를 유지한다.
    private bool TryFindRoomCenter(out Vector2 target)
    {
        RaycastMotor2D.InvalidateAttackQuerySync(); RaycastMotor2D.PrepareAttackQueries();
        Vector2 origin = (Vector2)transform.position + Vector2.up * .1f;
        var left = controller.Motor.RaycastEnvironment(origin, Vector2.left, 100f);
        var right = controller.Motor.RaycastEnvironment(origin, Vector2.right, 100f);
        target = Vector2.zero;
        if (left.collider == null || right.collider == null || right.point.x <= left.point.x) return false;
        float x = (left.point.x + right.point.x) * .5f;
        var floor = controller.Motor.RaycastEnvironment(new Vector2(x, origin.y), Vector2.down, 100f);
        if (floor.collider == null) return false;
        target = new Vector2(x, transform.position.y);
        return true;
    }

    private void HandleCenterLanding()
    {
        // 기본 ForceRecover가 먼저 호출되므로 Recover도 같은 착지로 처리한다.
        if (!launchedForCenter || (State != AbilityState.Execute && State != AbilityState.Recover)) return;
        ArrivedAtCenter = Mathf.Abs(transform.position.x - CenterTarget.x) <= .75f;
    }

    protected override void OnStop()
    {
        if (!targetRoomCenter) return;
        controller.OnLanded -= HandleCenterLanding;
        controller.Velocity = Vector2.zero;
        launchedForCenter = false;
        if (ArrivedAtCenter && !brain.IsPaused && followupAbility != null) brain.ForceNextAbility(followupAbility);
    }
}
