using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/AI Pattern Profile")]
public class AiPatternProfile : ScriptableObject
{
    [SerializeField] private List<AiPatternRule> rules = new();

    public AiAbility Resolve(AiPatternContext context, AiAbility candidate)
    {
        if (candidate == null)
            return null;

        for (int i = 0; i < rules.Count; i++)
        {
            AiPatternRule rule = rules[i];

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