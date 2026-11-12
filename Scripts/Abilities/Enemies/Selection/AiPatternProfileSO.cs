using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/AI Pattern Profile")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "AiPatternProfile")]
public class AiPatternProfileSO : ScriptableObject
{
    [SerializeField] private List<AiPatternRuleSO> rules = new();

    [Tooltip("목록 순서로 점수를 보정합니다. 절대 우선 규칙은 priority가 높은 순서입니다.")]
    [SerializeField] private List<AiPatternScoreRuleSO> scoreRules = new();

    [Tooltip("체력 비율로 나누는 페이즈입니다. 비워 두면 모든 패턴을 처음부터 같은 수치로 고릅니다.")]
    [SerializeField] private List<AiPatternPhase> phases = new();

    public IReadOnlyList<AiPatternPhase> Phases => phases;
    public bool UsesPhases => phases != null && phases.Count > 0;

    /// <summary>
    /// 체력 비율에 맞는 페이즈로 넘어간다. 바뀌었으면 그 페이즈의 선택 수치를 패턴에 적용하고 진입 패턴을 줄 세운다.
    /// 한번 넘어간 페이즈는 체력이 회복되어도 되돌아가지 않는다. 런타임 상태는 개체별 문맥에만 둔다.
    /// </summary>
    public void UpdatePhase(AiPatternContext context)
    {
        if (!UsesPhases || context == null || context.Brain == null) return;
        int target = Mathf.Max(0, context.PhaseIndex);
        float ratio = context.GetHealthRatio();
        // 조건을 만족하는 가장 뒤 페이즈로 간다. 한 번에 여러 페이즈를 건너뛰면 사이 페이즈의 진입 패턴은 생략한다.
        for (int i = target + 1; i < phases.Count; i++)
            if (phases[i] != null && ratio < phases[i].enterBelowHealthRatio) target = i;
        if (target == context.PhaseIndex) return;
        context.EnterPhase(target);
        var phase = phases[target];
        foreach (var ability in context.Brain.Abilities)
        {
            if (ability == null) continue;
            var pattern = phase != null ? phase.Find(ability.AbilityKey) : null;
            // 등록하지 않은 패턴은 SO의 공통 수치로 돌아가며, 예약·전환 규칙이 부를 때만 실행된다.
            if (pattern == null) ability.ClearSelectionTuning();
            else ability.ApplySelectionTuning(pattern.baseWeight, pattern.weightChangeOnUse,
                pattern.weightRecoveryPerSecond, pattern.cooldown);
        }
        if (phase == null || phase.patterns == null) return;
        foreach (var pattern in phase.patterns)
            if (pattern != null && pattern.useOnPhaseEnter)
                context.QueuePhaseEntry(context.Brain.GetAbilityByKey(pattern.abilityKey));
    }

    // 페이즈를 쓰면 현재 페이즈에 등록한 패턴만 점수 경쟁에 참여한다.
    private bool IsInCurrentPhase(AiPatternContext context, AiAbility ability)
    {
        if (!UsesPhases || ability == null) return true;
        int index = context.PhaseIndex;
        if (index < 0 || index >= phases.Count || phases[index] == null) return true;
        return phases[index].Find(ability.AbilityKey) != null;
    }

    public bool IsCandidateAvailable(AiPatternContext context, AiAbility candidate)
    {
        if (!IsInCurrentPhase(context, candidate)) return false;
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
        // 페이즈 진입 패턴은 다른 절대 우선 규칙·예약·점수보다 먼저이며 쿨타임을 기다리지 않는다.
        var phaseEntry = context.PeekPhaseEntry();
        if (phaseEntry != null && !phaseEntry.IsRunning)
        {
            ignoreCooldown = true;
            return phaseEntry;
        }
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
        context.CompletePhaseEntry(ability);
        foreach (var rule in scoreRules)
            if (rule != null && rule.oncePerSpawn && rule.Matches(context, ability)) context.Consume(rule);
    }

    public int ResolvePatrolDirection(AiPatternContext context, AiAbility ability, int proposedDirection)
    {
        foreach (var rule in rules)
            if (rule != null && rule.IsEnabled && rule.TryResolvePatrolDirection(context, ability, proposedDirection, out int direction))
                return direction >= 0 ? 1 : -1;
        return proposedDirection;
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