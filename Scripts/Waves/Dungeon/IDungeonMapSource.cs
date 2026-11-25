using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>미니맵에 그릴 방 하나의 읽기 전용 상태.</summary>
public sealed class DungeonMapRoom
{
    public int id;
    public DungeonRoomKind kind;
    /// <summary>던전 격자의 단위 칸(절대 좌표).</summary>
    public Vector2Int[] units = Array.Empty<Vector2Int>();
    /// <summary>연결된 출입구: (단위 칸, 벽 방향, 건너편 방 id).</summary>
    public (Vector2Int unit, DungeonDoorSide side, int toRoom)[] links = Array.Empty<(Vector2Int, DungeonDoorSide, int)>();
    /// <summary>인접한 방에 들어가 존재를 알게 되었는지(미니맵에 흐린 테두리로 보인다).</summary>
    public bool known;
    /// <summary>들어가 본 적이 있는지.</summary>
    public bool visited;
    /// <summary>클리어했는지(적이 없는 방은 들어가면 바로 클리어).</summary>
    public bool cleared;
    /// <summary>보스 방이 봉인되어 있는지.</summary>
    public bool sealedRoom;
    /// <summary>크기 등급(미니맵은 작은 방을 더 작게 그린다).</summary>
    public DungeonRoomSize sizeClass;
    /// <summary>짝지은 텔레포트가 있으면 건너편 방 id(없으면 -1). 이 방이 알려져 있으면 미니맵이 연결 표시를 그린다.</summary>
    public int teleportTo = -1;
    /// <summary>휴식 방(수리대) 여부.</summary>
    public bool rest;
}

/// <summary>
/// 던전 상태를 UI(미니맵 등)에 내주는 읽기 전용 접점. 게임플레이(DungeonManager)가 구현하고 UI는 구독해 표시만 한다(AGENTS.md UI 접점 규칙).
/// </summary>
public interface IDungeonMapSource
{
    /// <summary>지금 던전 Wave가 진행 중인지.</summary>
    bool HasFloor { get; }
    /// <summary>던전 번호(= Wave 번호, 1부터).</summary>
    int FloorNumber { get; }
    int FloorCount { get; }
    IReadOnlyList<DungeonMapRoom> Rooms { get; }
    /// <summary>Player가 있는 방 id(없으면 -1).</summary>
    int CurrentRoom { get; }
    /// <summary>던전 시작·끝·방 상태가 바뀌었다(다시 그린다).</summary>
    event Action MapChanged;
    /// <summary>Player가 방에 들어왔다(방 id).</summary>
    event Action<int> RoomEntered;
    /// <summary>방을 클리어했다(방 id).</summary>
    event Action<int> RoomCleared;
    /// <summary>던전 조건을 채웠다(Wave 클리어 연출 직전).</summary>
    event Action FloorCleared;
}
