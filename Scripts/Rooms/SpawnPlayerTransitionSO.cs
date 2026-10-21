using UnityEngine;
using System.Collections;

[CreateAssetMenu(menuName = "Room/Transition/SpawnPlayer")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "SpawnPlayerTransition")]
public class SpawnPlayerTransitionSO : RoomTransitionEffectSO
{
    [Header("SpawnPlayerSettings")]
    [SerializeField] private float fadeOutTime = 0.2f;
    [SerializeField] private float fadeInTime = 0.5f;
    [SerializeField] private float darknessTime = 0.5f;
    [SerializeField] private Color wireColor = Color.red;

    [Header("Feedbacks")]
    [SerializeField] private FeedbackDataSO endFeedback;
    public override IEnumerator Play(RoomTransitionContext ctx)
    {
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

        // Room 이동 완료 연출
        FeedbackPlayer.Instance.Play(endFeedback, GameManager.Instance.Player.transform);
        GameManager.Instance.Player.animator.SetBool(changePlayerAnim, false);
    }
}
