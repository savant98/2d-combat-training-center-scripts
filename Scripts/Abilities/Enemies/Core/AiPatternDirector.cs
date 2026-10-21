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