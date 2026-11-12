using UnityEngine;

public class TutorialGoal : MonoBehaviour
{
    [SerializeField] private GameObject goalParticle;
    [SerializeField] private FeedbackDataSO goalFeedback;
    [SerializeField] private string triggerKey;

    private bool goalIn = false;
    private string originalKey;
    private WaveManager waveOwner;
    private bool waveReachGoal;
    public bool HasReached => goalIn;
    public bool IsWaveGoalActive => isActiveAndEnabled && !goalIn && waveOwner != null && waveOwner.IsGoalActive(this);

    public void ConfigureWaveGoal(string key, WaveManager owner = null, bool reachToClear = false)
    {
        waveOwner = owner;
        waveReachGoal = reachToClear;
        if (originalKey == null) originalKey = triggerKey;
        triggerKey = string.IsNullOrEmpty(key) ? originalKey : key;
    }

    public void InitializeGoalSetting()
    {
        goalIn = false;
        if (goalParticle != null) goalParticle.SetActive(true);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        TryReach(collision);
    }

    // 목표가 플레이어와 겹쳐 생성되거나 Wave 준비 중 진입한 경우에도 판정한다.
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (IsWaveGoalActive) TryReach(collision);
    }

    private void TryReach(Collider2D collision)
    {
        if (!isActiveAndEnabled || goalIn)
            return;

        if (collision.TryGetComponent(out PlayerController playerController))
        {
            bool waveGoal = waveReachGoal;
            if (waveGoal && (waveOwner == null || !waveOwner.CanReachGoal(this))) return;
            // 같은 프레임의 재진입과 즉시 Wave 정리에 앞서 도달 상태를 확정한다.
            goalIn = true;
            if (goalParticle != null) goalParticle.SetActive(false);
            if (goalFeedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(goalFeedback, transform);
            if (waveGoal) waveOwner.HandleGoalReached(this);
            else (waveOwner != null ? waveOwner : WaveManager.ActiveInstance)?.HandleCustomTrigger(triggerKey);
        }
    }
}
