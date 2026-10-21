using UnityEngine;

public class AnimationFeedbackReceiver : MonoBehaviour
{
    [System.Serializable]
    private class AnimationFeedbackEntry
    {
        public string key;
        public FeedbackData feedback;
        public Transform point;
    }

    [SerializeField] private AnimationFeedbackEntry[] feedbackEntries;


    public void PlayAnimationFeedback(string key)
    {
        if (FeedbackPlayer.Instance == null || string.IsNullOrEmpty(key))
            return;

        for (int i = 0; i < feedbackEntries.Length; i++)
        {
            AnimationFeedbackEntry entry = feedbackEntries[i];

            if (entry == null || entry.key != key || entry.feedback == null)
                continue;

            Transform spawnPoint = entry.point != null ? entry.point : transform;
            FeedbackPlayer.Instance.Play(entry.feedback, spawnPoint);
            return;
        }
    }
}