using UnityEngine;

public class EnemyDeathFeedback : MonoBehaviour
{
    [Header("Destroy Timing Feedback")]
    [SerializeField] private FeedbackData destroyFeedback;
    [SerializeField] private FeedbackData destroyRepeatFeedback;

    public void DestroyFeedbackStart()
    {
        FeedbackPlayer.Instance.Play(destroyFeedback, transform);
    }

    public void DestroyRepeatFeedbackStart()
    {
        FeedbackPlayer.Instance.Play(destroyRepeatFeedback, transform);
    }

}
