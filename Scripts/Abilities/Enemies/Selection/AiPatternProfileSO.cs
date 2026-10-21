using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/AI Pattern Profile")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "AiPatternProfile")]
public class AiPatternProfileSO : ScriptableObject
{
    [SerializeField] private List<AiPatternRuleSO> rules = new();

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