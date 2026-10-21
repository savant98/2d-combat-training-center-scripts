using UnityEngine;

public class EnemyDeathFeedback : MonoBehaviour
{
    [Header("Destroy Timing Feedback")]
    [SerializeField] private FeedbackDataSO destroyFeedback;
    [SerializeField] private FeedbackDataSO destroyRepeatFeedback;

    public void DestroyFeedbackStart()
    {
        FeedbackPlayer.Instance.Play(destroyFeedback, transform);
    }

    public void DestroyRepeatFeedbackStart()
    {
        FeedbackPlayer.Instance.Play(destroyRepeatFeedback, transform);
    }

}
