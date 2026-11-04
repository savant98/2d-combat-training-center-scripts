using UnityEngine;

// 감지 범위 안에 플레이어가 있으면 감지 패턴(예: 돌진)을, 없으면 감지 전 패턴(예: 순찰)을 고른다.
// 감지 패턴이 쿨다운이면 원래 후보를 유지한다. 순찰처럼 끝나지 않는 패턴은 AiBrain이 실행 중에도 이 규칙을 확인한다.
[CreateAssetMenu(menuName = "Enemy/Ai Pattern Rules/Player Detection Swap Rule")]
public class PlayerDetectionSwapRuleSO : AiPatternRuleSO
{
    [Header("Ability Keys")]
    [Tooltip("플레이어를 감지하지 못했을 때 사용하는 패턴의 abilityKey입니다.")]
    [SerializeField] private string undetectedAbilityKey;
    [Tooltip("감지 범위 안에 플레이어가 있을 때 사용하는 패턴의 abilityKey입니다.")]
    [SerializeField] private string detectedAbilityKey;

    [Header("Detection")]
    [Tooltip("적 중심에서 플레이어 중심까지의 수평 감지 거리(칸)입니다.")]
    [SerializeField, Min(0f)] private float detectDistanceX = 6f;
    [Tooltip("적 중심에서 플레이어 중심까지의 수직 감지 거리(칸)입니다.")]
    [SerializeField, Min(0f)] private float detectDistanceY = 2f;
    [Tooltip("켜면 바닥에 닿아 있을 때만 감지 패턴으로 바꿉니다.")]
    [SerializeField] private bool requireGrounded = true;
    [Tooltip("켜면 적과 플레이어 사이가 RaycastMotor2D의 충돌 레이어로 막혀 있을 때 감지하지 않습니다.")]
    [SerializeField] private bool requireLineOfSight = true;

    public float DetectDistanceX => detectDistanceX;
    public float DetectDistanceY => detectDistanceY;

    public override bool CanHandle(AiPatternContext context, AiAbility candidate)
    {
        if (candidate == null)
            return false;

        string key = candidate.AbilityKey;
        return key == undetectedAbilityKey || key == detectedAbilityKey;
    }

    public override AiAbility Resolve(AiPatternContext context, AiAbility candidate)
    {
        if (context == null || context.Brain == null || candidate == null)
            return candidate;

        string desiredKey = IsPlayerDetected(context) ? detectedAbilityKey : undetectedAbilityKey;
        if (candidate.AbilityKey == desiredKey)
            return candidate;

        AiAbility desired = context.Brain.GetAbilityByKey(desiredKey);
        return desired != null && desired.IsReady() ? desired : candidate;
    }

    public bool IsPlayerDetected(AiPatternContext context)
    {
        Transform player = context.PlayerTransform;
        EnemyController controller = context.Controller;
        if (player == null || controller == null || !player.gameObject.activeInHierarchy)
            return false;

        if (requireGrounded && !controller.IsGrounded)
            return false;

        Vector2 origin = controller.transform.position;
        Vector2 delta = (Vector2)player.position - origin;
        if (Mathf.Abs(delta.x) > detectDistanceX || Mathf.Abs(delta.y) > detectDistanceY)
            return false;

        if (requireLineOfSight && controller.Motor != null && delta.sqrMagnitude > 0.0001f)
        {
            float distance = delta.magnitude;
            RaycastHit2D blocker = controller.Motor.RaycastEnvironment(origin, delta / distance, distance);
            if (blocker.collider != null)
                return false;
        }

        return true;
    }
}
