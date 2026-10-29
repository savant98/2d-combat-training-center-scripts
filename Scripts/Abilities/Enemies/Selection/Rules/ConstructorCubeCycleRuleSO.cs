using System;
using System.Runtime.CompilerServices;
using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/AI Pattern Rules/Constructor Cube Cycle")]
public sealed class ConstructorCubeCycleRuleSO : AiPatternRuleSO
{
    [SerializeField] private string[] cubeAbilityKeys = {
        "Constructor.CubeBomb", "Constructor.CubeShockwave", "Constructor.CubeBounce"
    };

    private sealed class SelectionState { public string lastKey; }
    [NonSerialized] private ConditionalWeakTable<AiPatternContext, SelectionState> selectionStates;

    public override bool CanHandle(AiPatternContext context, AiAbility candidate)
    {
        return candidate != null && cubeAbilityKeys != null
            && Array.IndexOf(cubeAbilityKeys, candidate.AbilityKey) >= 0;
    }

    public override AiAbility Resolve(AiPatternContext context, AiAbility candidate)
    {
        if (context == null || context.Brain == null || !CanHandle(context, candidate)) return candidate;
        selectionStates ??= new ConditionalWeakTable<AiPatternContext, SelectionState>();
        var state = selectionStates.GetValue(context, _ => new SelectionState());
        int last = Array.IndexOf(cubeAbilityKeys, state.lastKey);
        int start = last >= 0 ? (last + 1) % cubeAbilityKeys.Length
            : Array.IndexOf(cubeAbilityKeys, candidate.AbilityKey);

        // 보스마다 다른 선택 기록을 사용한다. 큐브 이외의 후보와 그 선택 빈도는 바꾸지 않는다.
        for (int offset = 0; offset < cubeAbilityKeys.Length; offset++)
        {
            string key = cubeAbilityKeys[(start + offset) % cubeAbilityKeys.Length];
            if (key == state.lastKey) continue;
            var ability = context.Brain.GetAbilityByKey(key);
            if (ability == null || !ability.IsReady()) continue;
            state.lastKey = key;
            return ability;
        }

        // 다른 종류가 모두 쿨다운이면 원래 준비된 후보를 그대로 사용한다.
        state.lastKey = candidate.AbilityKey;
        return candidate;
    }
}
