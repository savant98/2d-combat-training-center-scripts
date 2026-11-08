using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/AI Pattern Profile")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "AiPatternProfile")]
public class AiPatternProfileSO : ScriptableObject
{
    [SerializeField] private List<AiPatternRuleSO> rules = new();

    [Tooltip("목록 순서로 점수를 보정합니다. 절대 우선 규칙은 priority가 높은 순서입니다.")]
    [SerializeField] private List<AiPatternScoreRuleSO> scoreRules = new();

    public bool IsCandidateAvailable(AiPatternContext context, AiAbility candidate)
    {
        foreach (var rule in rules)
            if (rule != null && rule.IsEnabled && !rule.IsCandidateAvailable(context, candidate)) return false;
        return true;
    }

    public float EvaluateScore(AiPatternContext context, AiAbility ability)
    {
        float score = ability.CurrentWeight;
        foreach (var rule in scoreRules)
            if (rule != null && rule.operation != AiPatternScoreRuleSO.ScoreOperation.AbsolutePriority && rule.Matches(context, ability))
                score = rule.Apply(score);
        return score;
    }

    public AiAbility GetAbsoluteAbility(AiPatternContext context, out bool ignoreCooldown)
    {
        ignoreCooldown = false;
        AiAbility selected = null;
        int highestPriority = int.MinValue;
        foreach (var rule in scoreRules)
        {
            if (rule == null || rule.operation != AiPatternScoreRuleSO.ScoreOperation.AbsolutePriority) continue;
            var ability = context.Brain.GetAbilityByKey(rule.abilityKey);
            if (ability == null || !ability.isActiveAndEnabled || ability.IsRunning || ability.Data == null) continue;
            if ((!rule.ignoreCooldown && !ability.IsReady()) || !rule.Matches(context, ability)) continue;
            if (selected != null && rule.priority <= highestPriority) continue;
            highestPriority = rule.priority;
            selected = ability;
            ignoreCooldown = rule.ignoreCooldown;
        }
        return selected;
    }

    public void NotifyAbilityStarted(AiPatternContext context, AiAbility ability)
    {
        foreach (var rule in scoreRules)
            if (rule != null && rule.oncePerSpawn && rule.Matches(context, ability)) context.Consume(rule);
    }

    public AiAbility Resolve(AiPatternContext context, AiAbility candidate)
    {
        if (candidate == null)
            return null;

        for (int i = 0; i < rules.Count; i++)
        {
            AiPatternRuleSO rule = rules[i];

            if (rule == null || !rule.IsEnabled)
                continue;

            if (!rule.CanHandle(context, candidate))
                continue;

            candidate = rule.Resolve(context, candidate);

            if (candidate == null)
                return null;
        }

        return candidate;
    }
}