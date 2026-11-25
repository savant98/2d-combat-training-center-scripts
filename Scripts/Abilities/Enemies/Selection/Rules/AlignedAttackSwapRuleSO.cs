using UnityEngine;

// 플레이어가 정면 공격(예: Driller 드릴 발사)이 실제로 닿는 높이와 사거리 안에 있고, 공격선 높이에서 사이가 막혀 있지 않으면 그 공격을 고른다.
// 원래 후보가 sourceAbilityKeys 중 하나이고 정면 공격이 준비됐을 때만 바꾼다. 정면 공격이 후보인데 조건이 맞지 않으면 fallbackAbilityKey로 돌린다.
// PlayerDetectionSwapRuleSO보다 앞에 두면, 순찰 → (정면 공격 | 감지 공격) 세 갈래 전환을 기존 규칙과 함께 만들 수 있다.
[CreateAssetMenu(menuName = "Enemy/Ai Pattern Rules/Aligned Attack Swap Rule")]
public class AlignedAttackSwapRuleSO : AiPatternRuleSO
{
    [Header("Ability Keys")]
    [Tooltip("조건이 맞을 때 고르는 정면 공격 패턴의 abilityKey입니다.")]
    [SerializeField] private string attackAbilityKey;
    [Tooltip("이 패턴들이 후보일 때 정면 공격으로 바꿀 수 있습니다.")]
    [SerializeField] private string[] sourceAbilityKeys = new string[0];
    [Tooltip("정면 공격이 후보인데 조건이 맞지 않을 때 대신 고르는 패턴의 abilityKey입니다.")]
    [SerializeField] private string fallbackAbilityKey;

    [Header("Window")]
    [Tooltip("적 중심에서 플레이어 중심까지의 최대 수평 거리(칸)입니다.")]
    [SerializeField, Min(0f)] private float maxDistanceX = 5.5f;
    [Tooltip("플레이어 중심 높이 - 적 중심 높이의 최솟값(칸)입니다. 공격선 아래로 맞힐 수 있는 한계입니다.")]
    [SerializeField] private float minDeltaY = -1.2f;
    [Tooltip("플레이어 중심 높이 - 적 중심 높이의 최댓값(칸)입니다. 공격선 위로 맞힐 수 있는 한계입니다.")]
    [SerializeField] private float maxDeltaY = .55f;
    [Tooltip("시야를 검사하는 공격선 높이(적 중심 기준, 칸)입니다. 이 높이에서 플레이어 쪽으로 수평 광선을 쏩니다.")]
    [SerializeField] private float lineOffsetY = -.36f;
    [Tooltip("켜면 바닥에 닿아 있을 때만 정면 공격을 고릅니다.")]
    [SerializeField] private bool requireGrounded = true;
    [Tooltip("켜면 공격선 높이에서 플레이어 쪽이 RaycastMotor2D의 충돌 레이어로 막혀 있을 때 고르지 않습니다.")]
    [SerializeField] private bool requireLineOfSight = true;

    public float MaxDistanceX => maxDistanceX;

    public override bool CanHandle(AiPatternContext context, AiAbility candidate)
    {
        if (candidate == null) return false;
        string key = candidate.AbilityKey;
        return key == attackAbilityKey || IsSource(key);
    }

    public override AiAbility Resolve(AiPatternContext context, AiAbility candidate)
    {
        if (context == null || context.Brain == null || candidate == null) return candidate;
        bool aligned = IsAligned(context);
        if (candidate.AbilityKey == attackAbilityKey)
        {
            if (aligned) return candidate;
            var fallback = context.Brain.GetAbilityByKey(fallbackAbilityKey);
            return fallback != null ? fallback : candidate;
        }
        if (!aligned) return candidate;
        var attack = context.Brain.GetAbilityByKey(attackAbilityKey);
        return attack != null && attack.IsReady() ? attack : candidate;
    }

    public bool IsAligned(AiPatternContext context)
    {
        Transform player = context.PlayerTransform;
        EnemyController controller = context.Controller;
        if (player == null || controller == null || !player.gameObject.activeInHierarchy) return false;
        if (requireGrounded && !controller.IsGrounded) return false;
        Vector2 origin = controller.transform.position;
        Vector2 delta = (Vector2)player.position - origin;
        if (Mathf.Abs(delta.x) > maxDistanceX || delta.y < minDeltaY || delta.y > maxDeltaY) return false;
        if (requireLineOfSight && controller.Motor != null && Mathf.Abs(delta.x) > .0001f)
        {
            Vector2 line = origin + Vector2.up * lineOffsetY;
            Vector2 direction = delta.x > 0f ? Vector2.right : Vector2.left;
            if (controller.Motor.RaycastEnvironment(line, direction, Mathf.Abs(delta.x)).collider != null) return false;
        }
        return true;
    }

    private bool IsSource(string key)
    {
        if (string.IsNullOrEmpty(key) || sourceAbilityKeys == null) return false;
        foreach (string source in sourceAbilityKeys)
            if (source == key) return true;
        return false;
    }
}
