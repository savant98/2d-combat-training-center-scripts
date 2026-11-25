using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 던전 방 이동 한 번에 필요한 정보. DungeonManager가 만들어 <see cref="DungeonPassageEffectSO.Play"/>에 넘긴다.
/// 연출은 화면을 가린 동안 <see cref="SwapRoom"/>을 정확히 한 번 부르고, 그 뒤 <see cref="ArrivalDoor"/>로 도착 출입구를 얻는다.
/// </summary>
public sealed class DungeonPassageContext
{
    public PlayerController Player;
    public WaveCellTerrain Terrain;
    /// <summary>Player가 들어간 출발 출입구(현재 방).</summary>
    public DungeonDoor FromDoor;
    /// <summary>방을 바꾼다(셀 교체·Prop·출입구 생성). 화면이 가려진 동안 한 번만 부른다.</summary>
    public Action SwapRoom;
    /// <summary>SwapRoom 뒤의 도착 출입구(다음 방, 이 출입구에서 Player가 나온다).</summary>
    public Func<DungeonDoor> ArrivalDoor;
    /// <summary>연출이 끝까지 정상 진행했는지(연출이 채운다). false면 DungeonManager가 즉시 교체·순간이동으로 마무리한다.</summary>
    public bool Completed;
    /// <summary>SwapRoom을 이미 불렀는지(DungeonManager가 채운다).</summary>
    public bool Swapped;
    /// <summary>출발·도착 지점(출입구든 텔레포트든). 텔레포트 연출은 이것을 쓴다. 출입구 이동이면 FromDoor·ArrivalDoor와 같은 대상이다.</summary>
    public IDungeonPassagePoint From;
    public Func<IDungeonPassagePoint> Arrival;
    /// <summary>텔레포트 시설로 옮기는 이동인지.</summary>
    public bool IsTeleport;
}

/// <summary>
/// 던전 방 사이를 옮기는 연출(교체형). 맵 컨셉마다 에셋을 바꿔 끼운다(관 통과·엘리베이터·텔레포터 등).
/// 구현 규칙: Player 위치는 모터 구속(<c>RaycastMotor2D.ShiftConfinement</c>)이나 기존 순간이동 경로(<c>PlayerController.ResetPlayerPosition</c> + 카메라 warp)로만 바꾼다.
/// 예외가 나거나 중간에 끊겨도 Player를 구속한 채 남기지 않는다(finally에서 해제).
/// </summary>
public abstract class DungeonPassageEffectSO : ScriptableObject
{
    public abstract IEnumerator Play(DungeonPassageContext context);
}
