using UnityEngine;

public class RoomTransitionContext
{
    public Room fromRoom;
    public Room toRoom;
    public Transform teleportPoint;
    public RoomTransitionRequest request;

    public void SwitchRoom()
    {
        RoomManager.Instance.ChangeRoomInternal(this);
    }

    // 방을 바꾼 뒤 TrainingRoom의 입장 로딩(Wave에 쓸 것을 미리 만들어 두는 단계)이 끝날 때까지 기다린다.
    // 전환 연출은 SwitchRoom 바로 다음에 이것을 기다려 암전을 유지한다. 로딩이 없으면 바로 끝난다.
    public System.Collections.IEnumerator WaitForLoading()
    {
        var host = request != null && request.targetRoom != null ? request.targetRoom.GetComponent<TrainingRoomHost>() : null;
        while (host != null && host.IsLoading) yield return null;
    }
}