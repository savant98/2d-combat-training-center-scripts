using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ai Pattern Rules/Distance Ability Swap Rule")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "DistanceAbilitySwapRule")]
public class DistanceAbilitySwapRuleSO : AiPatternRuleSO
{
    [Header("Ability Keys")]
    [SerializeField] private string nearAbilityKey;
    [SerializeField] private string farAbilityKey;

    [Header("Distance")]
    [SerializeField, Min(0f)] private float nearDistanceX = 3f;

    public override bool CanHandle(AiPatternContext context, AiAbility candidate)
    {
        if (candidate == null)
            return false;

        string key = candidate.AbilityKey;
        return key == nearAbilityKey || key == farAbilityKey;
    }

    public override AiAbility Resolve(AiPatternContext context, AiAbility candidate)
    {
        if (context == null || candidate == null)
            return candidate;

        float distanceX = context.GetPlayerDistanceX();
        bool isNear = distanceX <= nearDistanceX;

        // nearAbility가 후보일 때:
        // 가까우면 그대로, 멀면 farAbility로 교체 시도
        if (candidate.AbilityKey == nearAbilityKey)
        {
            if (isNear)
                return candidate;

            return GetResolvedAbilityOrFallback(context, farAbilityKey, candidate);
        }

        // farAbility가 후보일 때:
        // 멀면 그대로, 가까우면 nearAbility로 교체 시도
        if (candidate.AbilityKey == farAbilityKey)
        {
            if (!isNear)
                return candidate;

            return GetResolvedAbilityOrFallback(context, nearAbilityKey, candidate);
        }

        return candidate;
    }

    private AiAbility GetResolvedAbilityOrFallback(
        AiPatternContext context,
        string desiredKey,
        AiAbility fallback)
    {
        AiAbility desired = context.Brain.GetAbilityByKey(desiredKey);

        if (desired != null && desired.IsReady())
            return desired;

        return fallback;
    }
}