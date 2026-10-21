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
}