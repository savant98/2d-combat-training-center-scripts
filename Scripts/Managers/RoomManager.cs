using UnityEngine;
using System;
using System.Collections;
using Unity.Cinemachine;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    public Room CurrentRoom { get; private set; }

    public event Action<RoomType> OnRoomChanged;

    private void Awake()
    {
        Instance = this;
    }
    public void ChangeRoomInternal(RoomTransitionContext context)
    {
        var request = context.request;
        ChangeRoom(request);
    }
    private void ChangeRoom(RoomTransitionRequest request)
    {   
        if (CurrentRoom == null)
            CurrentRoom = request.targetRoom;

        var brain = Camera.main.GetComponent<CinemachineBrain>();
        brain.enabled = false;

        // 기존 Room 비활성화
        CurrentRoom.DeactivateRoom();

        // 새로운 Room 활성화
        CurrentRoom = request.targetRoom;
        CurrentRoom.ActivateRoom(request.teleportPoint.position);

        // Player 이동
        if (request.teleportPoint != null)
        {
            GameManager.Instance.Player.transform.position = request.teleportPoint.position;
        }

        // UI 시스템 등에게 알리기
        OnRoomChanged?.Invoke(CurrentRoom.roomType);

        StartCoroutine(EnableCameraNextFrame(brain));
    }

    private IEnumerator EnableCameraNextFrame(CinemachineBrain brain)
    {
        yield return null;
        brain.enabled = true;
    }
}
