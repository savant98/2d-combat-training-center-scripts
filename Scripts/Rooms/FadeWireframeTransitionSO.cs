using UnityEngine;
using System.Collections;

[CreateAssetMenu(menuName ="Room/Transition/FadeWireframe")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "FadeWireframeTransition")]
public class FadeWireframeTransitionSO : RoomTransitionEffectSO 
{
    [Header("FadeWireframeSettings")]
    [SerializeField] private float fadeOutTime = 0.2f;
    [SerializeField] private float fadeInTime = 0.5f;
    [SerializeField] private float playerWarpStartAnimDelay = 1.2f;
    [SerializeField] private float playerWarpEndAnimDelay = 0.3f;
    [SerializeField] private float darknessTime = 0.5f;
    [SerializeField] private Color wireColor = Color.cyan;

    [Header("Feedbacks")]
    [SerializeField] private FeedbackDataSO startFeedback;
    [SerializeField] private FeedbackDataSO warpFeedback;
    [SerializeField] private FeedbackDataSO endFeedback;

    private Transform playerTransform;
    public override IEnumerator Play(RoomTransitionContext ctx)
    {
        if (playerTransform == null)
            playerTransform = GameManager.Instance.Player.transform;

        if (changePlayerAnim != null)
        {
            FeedbackPlayer.Instance.Play(startFeedback, playerTransform);
            GameManager.Instance.Player.animator.SetBool(changePlayerAnim, true);
            yield return new WaitForSeconds(playerWarpStartAnimDelay);
        }

        FeedbackPlayer.Instance.Play(warpFeedback, new Vector3(playerTransform.position.x, playerTransform.position.y + 1.5f, 0));

        UIManager.Instance.StartScreenFade(1f, fadeOutTime);
        UIManager.Instance.StartWireframeEffect(3f, wireColor, fadeOutTime);

        // 암전 시작
        yield return new WaitForSeconds(fadeOutTime);
        UIManager.Instance.StartWireframeEffect(0f, wireColor, fadeInTime);

        // 암전 중
        yield return new WaitForSeconds(darknessTime);
        UIManager.Instance.StartWireframeEffect(3f, wireColor, fadeOutTime);

        // 암전 종료
        yield return new WaitForSeconds(fadeOutTime);

        ctx.SwitchRoom();

        UIManager.Instance.StartWireframeEffect(0f, wireColor, fadeInTime);
        UIManager.Instance.StartScreenFade(0f, fadeInTime);

        if (changePlayerAnim != null)
            GameManager.Instance.Player.animator.SetBool(changePlayerAnim, false);

        // Room 이동 완료 연출 대기
        yield return new WaitForSeconds(playerWarpEndAnimDelay);

        // Room 이동 완료 연출
        FeedbackPlayer.Instance.Play(endFeedback, GameManager.Instance.Player.transform);
    }
}