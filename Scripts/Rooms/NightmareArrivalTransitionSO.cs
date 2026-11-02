using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName="Room/Transition/NightmareArrival")]
public sealed class NightmareArrivalTransitionSO : RoomTransitionEffectSO
{
    [SerializeField,Min(.1f)] private float duration=3f;
    public override IEnumerator Play(RoomTransitionContext context)
    {
        var sequence=GameManager.Instance.GetComponent<NightmareIntroSequence>();
        var effects=sequence!=null?sequence.ScreenEffects:null;
        if(effects==null){context.SwitchRoom();yield break;}
        yield return effects.PlayArrival(context.SwitchRoom,duration);
        // 표시 컴포넌트가 중간에 꺼져도 방 전환 관리자를 진입 대기 상태에 남기지 않는다.
        if(RoomManager.Instance.CurrentRoom!=context.request.targetRoom)
        {context.SwitchRoom();UIManager.Instance.StartScreenFade(0,0);}
        if(!string.IsNullOrEmpty(changePlayerAnim))GameManager.Instance.Player.animator.SetBool(changePlayerAnim,false);
    }
}
