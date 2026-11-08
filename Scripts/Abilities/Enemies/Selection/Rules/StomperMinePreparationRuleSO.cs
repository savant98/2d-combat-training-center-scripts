using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/AI Rules/Stomper Mine Preparation")]
public sealed class StomperMinePreparationRuleSO : AiPatternRuleSO
{
    public override bool CanHandle(AiPatternContext context, AiAbility candidate)
        => candidate is MineScatterAbility && context.Brain != null;

    public override bool IsCandidateAvailable(AiPatternContext context, AiAbility candidate)
    {
        if (!(candidate is MineScatterAbility)) return true;
        if (context.Controller == null) return false;
        foreach (var dash in context.Controller.GetComponents<ParabolicDashAbility>())
            if (dash.TargetsRoomCenter) return dash.IsReady();
        return false;
    }

    public override AiAbility Resolve(AiPatternContext context, AiAbility candidate)
    {
        // 지뢰를 선택할 때마다 중앙 점프부터 시작한다. 성공한 점프가 지뢰를 강제 예약한다.
        foreach (var dash in context.Controller.GetComponents<ParabolicDashAbility>())
            if (dash.TargetsRoomCenter) return dash;
        return null;
    }
}
