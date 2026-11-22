using System.Collections;
using UnityEngine;

/// <summary>
/// 각개 전투 엘리베이터 전환: 케이지가 어두운 수직 갱도를 지나는 동안 방을 바꾼다.
/// 연출 본체는 Scene 루트의 FieldElevatorRide가 맡는다(ScriptableObject는 Scene 개체를 직렬화로 가리킬 수 없다).
/// 이동 장치가 없거나 시작하지 못하면 보통 입장(방을 바로 바꾸고 입장 로딩·Wave 자동 시작)으로 대신한다.
/// </summary>
[CreateAssetMenu(menuName = "Room/Transition/ElevatorRide")]
public sealed class ElevatorRideTransitionSO : RoomTransitionEffectSO
{
    public override IEnumerator Play(RoomTransitionContext context)
    {
        var ride = FieldElevatorRide.Instance;
        if (ride == null || !ride.Begin(context))
        {
            context.SwitchRoom();
            yield break;
        }
        while (ride != null && ride.IsRiding) yield return null;
        // 보루: 이동이 방을 바꾸지 못하고 끝났으면 여기서 바꾼다(전환 관리자를 대기 상태에 남기지 않는다 — NightmareArrivalTransitionSO와 같음).
        if (RoomManager.Instance != null && context.request != null && RoomManager.Instance.CurrentRoom != context.request.targetRoom)
            context.SwitchRoom();
    }
}
