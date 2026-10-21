using UnityEngine;
using System.Collections;

[CreateAssetMenu(menuName = "Room/Transition/PlayerDeath")]
public class PlayerDeathTransition : RoomTransitionEffectSO
{
    [Header("PlayerDeathSettings")]
    [SerializeField] private float windupDeathTime = 1.5f;
    [SerializeField] private float deathEffectTime = 0.3f;

    [Header("Feedbacks")]
    [SerializeField] private FeedbackData crossHairFeedback;
    [SerializeField] private FeedbackData deathFeedback;
    public override IEnumerator Play(RoomTransitionContext context)
    {
        FeedbackPlayer.Instance.Play(crossHairFeedback, GameManager.Instance.Player.transform);
        Time.timeScale = 0f;

        yield return new WaitForSecondsRealtime(windupDeathTime);

        Time.timeScale = 1f;
        GameManager.Instance.Player.animator.SetBool(changePlayerAnim, true);
        FeedbackPlayer.Instance.Play(deathFeedback, GameManager.Instance.Player.transform);

        yield return new WaitForSeconds(deathEffectTime);
        
        UIManager.Instance.StartWireframeEffect(3f, Color.red, 2f);
    }
}
