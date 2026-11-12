using UnityEngine;

public class AiPatternDirector : MonoBehaviour
{
    [SerializeField] private bool enableDirector = true;
    [SerializeField] private AiPatternProfileSO profile;

    private AiPatternContext context;
    private AiBrain brain;
    private EnemyController controller;
    private Transform playerTr;

    public AiPatternProfileSO Profile => profile;
    /// <summary>현재 페이즈 번호. 페이즈를 쓰지 않거나 스폰 뒤 첫 선택 전이면 -1이다.</summary>
    public int PhaseIndex => enableDirector && profile != null && profile.UsesPhases && context != null ? context.PhaseIndex : -1;

    public void Initialize(AiBrain brain, EnemyController controller)
    {
        this.brain = brain;
        this.controller = controller;

        context = new AiPatternContext();
        RefreshPlayerTransform();
        context.Initialize(brain, controller, playerTr);
    }

    public void ResetForSpawn() => Initialize(brain, controller);

    private bool RefreshContext()
    {
        if (!enableDirector || profile == null || context == null) return false;
        RefreshPlayerTransform();
        context.SetPlayerTransform(playerTr);
        return true;
    }

    public bool IsCandidateAvailable(AiAbility ability)
        => !RefreshContext() || profile.IsCandidateAvailable(context, ability);

    public float EvaluateScore(AiAbility ability)
        => RefreshContext() ? profile.EvaluateScore(context, ability) : ability.CurrentWeight;

    public AiAbility GetAbsoluteAbility(out bool ignoreCooldown)
    {
        ignoreCooldown = false;
        if (!RefreshContext()) return null;
        // 패턴을 고르기 시작할 때만 체력 비율로 페이즈를 맞춘다. 뒤따르는 후보·점수 평가는 상태를 바꾸지 않는다.
        profile.UpdatePhase(context);
        return profile.GetAbsoluteAbility(context, out ignoreCooldown);
    }

    public void NotifyAbilityStarted(AiAbility ability)
    {
        if (RefreshContext()) profile.NotifyAbilityStarted(context, ability);
    }

    public int ResolvePatrolDirection(AiAbility ability, int proposedDirection)
        => RefreshContext() ? profile.ResolvePatrolDirection(context, ability, proposedDirection) : proposedDirection;

    public AiAbility ResolveNextAbility(AiAbility candidate)
    {
        if (!enableDirector)
            return candidate;

        if (profile == null)
            return candidate;

        RefreshPlayerTransform();
        context.SetPlayerTransform(playerTr);

        return profile.Resolve(context, candidate);
    }

    private void RefreshPlayerTransform()
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            playerTr = null;
            return;
        }

        playerTr = GameManager.Instance.Player.transform;
    }
}