using DG.Tweening;
using UnityEngine;

public class TutorialCanvasBase : WorldEntityBase
{
    [Header("Tutorial Components")]
    [SerializeField] private TutorialGoal goal;
    [SerializeField] private Animator animator;
    [SerializeField] private string closeTrigger;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float closeTime = 1.5f;

    [Header("Scroll Line")]
    [SerializeField] private LineRenderer[] lineRenderers;
    [SerializeField] private float scrollSpeed = 5f;

    private static readonly int ScrollOffsetID = Shader.PropertyToID("_ScrollOffset");

    private Material[] lineMaterials;
    private Vector2 scrollOffset = Vector2.zero;
    protected override void Awake()
    {
        base.Awake();

        lineMaterials = new Material[lineRenderers.Length];

        for (int i = 0; i < lineRenderers.Length; i++)
        {
            lineMaterials[i] = lineRenderers[i].material;
            lineRenderers[i].material = lineMaterials[i];
        }

    }

    private void Update()
    {
        if (lineMaterials.Length < 1)
            return;

        scrollOffset.x += scrollSpeed * Time.deltaTime;

        foreach (Material material in lineMaterials)
        {
            material.SetVector(ScrollOffsetID, scrollOffset);   
        }
    }
    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        canvasGroup.alpha = 0f;
        canvasGroup.DOFade(1f, 0.5f);

        if (goal != null)
            goal.InitializeGoalSetting();
    }
    public override void ForceReturnToPool()
    {
        animator.SetTrigger(closeTrigger);

        canvasGroup.DOKill();

        canvasGroup.DOFade(0f, closeTime).OnComplete(() => base.ForceReturnToPool());
    }
}
