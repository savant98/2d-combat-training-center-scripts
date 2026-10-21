using UnityEngine;

public class ParabolicDashAbility : AiAbility
{
    // ParabolicSettings
    private float maxHeight = 4f;
    private float flyDuration = 1f;
    private float maxDistance = 10f;

    private Vector3 playerPos;
    private ParabolicDashData parabolicDashData;
    private void Awake()
    {
        parabolicDashData = (ParabolicDashData)data;
        maxHeight = parabolicDashData.maxHeight;
        flyDuration = parabolicDashData.flyDuration;
        maxDistance = parabolicDashData.maxDistance;
    }
    protected override void OnWindupStart()
    {
        controller.FacingUpdate(GameManager.Instance.Player.transform.position.x - transform.position.x > 0 ? 1 : -1);
        playerPos = GameManager.Instance.Player.transform.position;
    }
    protected override void OnExecuteStart()
    {
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
}
