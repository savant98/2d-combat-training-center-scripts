using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Room/Transition/WaveRetry")]
public class WaveRetryTransition : RoomTransitionEffectSO
{
    [Header("WaveRetrySettings")]
    [SerializeField] private float fadeOutTime = 0.2f;
    [SerializeField] private float fadeInTime = 0.5f;
    [SerializeField] private float darknessTime = 0.5f;
    [SerializeField] private Color wireColor = Color.cyan;

    [Header("Feedbacks")]
    [SerializeField] private FeedbackData retryFeedback;
    public override IEnumerator Play(RoomTransitionContext context)
    {
        UIManager.Instance.StartScreenFade(1f, fadeOutTime);
        UIManager.Instance.StartWireframeEffect(0f, wireColor, fadeOutTime);
        yield return new WaitForSeconds(darknessTime);

        UIManager.Instance.StartWireframeEffect(3f, wireColor, fadeOutTime);
        yield return new WaitForSeconds(fadeOutTime);

        UIManager.Instance.StartWireframeEffect(0f, wireColor, fadeInTime);
        UIManager.Instance.StartScreenFade(0f, fadeInTime);

        GameManager.Instance.Player.animator.SetBool(changePlayerAnim, false);
        FeedbackPlayer.Instance.Play(retryFeedback, RoomManager.Instance.CurrentRoom.SpawnPoint);

        // Player초기화
        GameManager.Instance.Player.ResetPlayerPosition(RoomManager.Instance.CurrentRoom.SpawnPoint);
    }
}
