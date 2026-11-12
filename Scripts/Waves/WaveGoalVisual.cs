using UnityEngine;

// 원본 글씨와 파티클을 보존하고 뒤쪽 Panel만 등장·퇴장시킨다.
public sealed class WaveGoalVisual : WorldEntityBase
{
    [SerializeField] private TutorialGoal goal;
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private GameObject particleRoot;
    private bool closing;
    private float opened;

    private void OnEnable() { opened = 0; Apply(); }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        closing = false; opened = 0;
        if (col != null) col.enabled = true;
        if (goal != null) goal.enabled = true;
        Apply();
    }

    public override void ForceReturnToPool()
    {
        if (closing) return;
        closing = true;
        if (col != null) col.enabled = false;
        if (goal != null) goal.enabled = false;
        if (particleRoot != null) particleRoot.SetActive(false);
    }

    private void Update()
    {
        opened = Mathf.MoveTowards(opened, !closing && goal != null && !goal.HasReached ? 1 : 0, Time.deltaTime * 3);
        Apply();
        if (closing && opened <= 0) base.ForceReturnToPool();
    }

    private void Apply()
    {
        if (panelGroup != null) panelGroup.alpha = opened * opened * (3 - 2 * opened);
    }
}
