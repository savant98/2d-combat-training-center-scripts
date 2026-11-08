using UnityEngine;

public class AiPatternDirector : MonoBehaviour
{
    [SerializeField] private bool enableDirector = true;
    [SerializeField] private AiPatternProfileSO profile;

    private AiPatternContext context;
    private AiBrain brain;
    private EnemyController controller;
    private Transform playerTr;

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
        return RefreshContext() ? profile.GetAbsoluteAbility(context, out ignoreCooldown) : null;
    }

    public void NotifyAbilityStarted(AiAbility ability)
    {
        if (RefreshContext()) profile.NotifyAbilityStarted(context, ability);
    }

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