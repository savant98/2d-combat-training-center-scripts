using UnityEngine;

public class TutorialGoal : MonoBehaviour
{
    [SerializeField] private GameObject goalParticle;
    [SerializeField] private FeedbackData goalFeedback;
    [SerializeField] private string triggerKey;

    private bool goalIn = false;

    public void InitializeGoalSetting()
    {
        goalIn = false;
        goalParticle.SetActive(true);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (goalIn)
            return;

        if (collision.TryGetComponent(out PlayerController playerController))
        {
            FeedbackPlayer.Instance.Play(goalFeedback, transform);
            WaveManager.ActiveInstance?.HandleCustomTrigger(triggerKey);
            goalParticle.SetActive(false);
            goalIn = true;
        }
    }
}
