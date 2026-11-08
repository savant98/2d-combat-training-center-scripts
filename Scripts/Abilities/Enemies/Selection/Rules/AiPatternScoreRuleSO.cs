using UnityEngine;

/// <summary>조건에 맞는 패턴의 점수를 보정하거나 점수를 무시하는 절대 우선권을 준다.</summary>
[CreateAssetMenu(menuName = "Enemy/AI Pattern Rules/Score Rule")]
public class AiPatternScoreRuleSO : ScriptableObject
{
    public enum ScoreOperation { Add, Multiply, Set, AbsolutePriority }

    public bool isEnabled = true;
    [Tooltip("대상 EnemyAbilityDataSO의 abilityKey입니다.")]
    public string abilityKey;
    public ScoreOperation operation;
    public float value = 100f;
    [Tooltip("절대 우선 규칙끼리는 큰 값이 먼저입니다. 동점이면 scoreRules 목록 순서입니다.")]
    public int priority;
    [Tooltip("이 스폰에서 조건에 맞는 패턴을 한 번 시작하면 규칙을 소비합니다. 페이즈 전환에 사용합니다.")]
    public bool oncePerSpawn;
    [Tooltip("AbsolutePriority에서만 쿨다운을 우회합니다. 일반 점수 보정은 항상 쿨다운을 지킵니다.")]
    public bool ignoreCooldown;

    [Header("조건: 체력 비율 (양 끝 포함)")]
    [Range(0f, 1f)] public float minimumHealthRatio;
    [Range(0f, 1f)] public float maximumHealthRatio = 1f;
    [Header("조건: 플레이어 수평 거리 (선택)")]
    public bool usePlayerDistance;
    [Min(0f)] public float minimumDistanceX;
    [Min(0f)] public float maximumDistanceX = 5f;
    [Header("조건: 직전 시작 패턴 (빈 값이면 제한 없음)")]
    public string previousAbilityKey;

    /// <summary>특수 상태·추가 센서 조건은 파생 SO에서 base.Matches와 함께 확장한다. 평가 중 실행 상태는 변경하지 않는다.</summary>
    public virtual bool Matches(AiPatternContext context, AiAbility ability)
    {
        if (!isEnabled || context == null || ability == null || string.IsNullOrEmpty(abilityKey)
            || ability.AbilityKey != abilityKey || (oncePerSpawn && context.IsConsumed(this))) return false;
        var health = context.Controller != null ? context.Controller.Health : null;
        float ratio = health != null && health.maxHP > 0f ? health.currentHP / health.maxHP : 1f;
        if (ratio < minimumHealthRatio || ratio > maximumHealthRatio) return false;
        if (!string.IsNullOrEmpty(previousAbilityKey) && context.Brain.LastAbilityKey != previousAbilityKey) return false;
        if (usePlayerDistance)
        {
            if (context.PlayerTransform == null) return false;
            float distance = context.GetPlayerDistanceX();
            if (distance < minimumDistanceX || distance > maximumDistanceX) return false;
        }
        return true;
    }

    public virtual float Apply(float score)
    {
        switch (operation)
        {
            case ScoreOperation.Add: return score + value;
            case ScoreOperation.Multiply: return score * value;
            case ScoreOperation.Set: return value;
            default: return score;
        }
    }
}
