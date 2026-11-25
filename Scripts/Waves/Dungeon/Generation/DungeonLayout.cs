using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>던전 안에서 방이 맡는 역할.</summary>
public enum DungeonRoomKind
{
    /// <summary>시작 방(적 없음).</summary>
    Start = 0,
    Combat = 1,
    Boss = 2,
}

/// <summary>연결된 출입구 한 쌍의 한쪽.</summary>
public struct DungeonDoorLink
{
    /// <summary>이 방 템플릿의 출입구 번호(TryReadDoors 결과 순서).</summary>
    public int socket;
    public int toRoom;
    public int toSocket;
}

/// <summary>던전 격자에 놓인 방 하나.</summary>
public sealed class DungeonRoomNode
{
    public int id;
    public DungeonRoomTemplateSO template;
    /// <summary>템플릿 모양의 단위 칸 (0,0)이 놓인 던전 격자 좌표.</summary>
    public Vector2Int gridOrigin;
    public DungeonRoomKind kind;
    /// <summary>쓸 적 구성 번호(템플릿 Wave 번호, 1부터). 0이면 적 없음.</summary>
    public int encounter;
    /// <summary>
    /// 동적 적 구성(4차 encounterMode = Dynamic) 던전의 전투 방이라 들어오는 순간 적 구성을 짜는지. 생성기가 PickEncounter에서 채운다(난수를 쓰지 않는다).
    /// 손 배치 구성 번호(encounter)는 Dynamic이어도 지금처럼 고른다(같은 시드 = 같은 배치·텔레포트·목표).
    /// </summary>
    public bool dynamicFight;
    /// <summary>싸우는 방인지: 전투 방은 손 배치 구성이 있거나 동적 구성 방, 보스 방은 손 배치 구성이 있을 때, 시작 방은 아니다.</summary>
    public bool HasFight => kind == DungeonRoomKind.Combat ? encounter > 0 || dynamicFight : kind == DungeonRoomKind.Boss && encounter > 0;
    /// <summary>연결된 출입구만 담는다.</summary>
    public readonly List<DungeonDoorLink> links = new();
    /// <summary>템플릿 출입구(생성 때 읽은 값).</summary>
    public readonly List<DungeonDoorSocket> sockets = new();

    public IEnumerable<Vector2Int> Units => DungeonShapes.Units(template != null ? template.shape : DungeonRoomShape.Single).Select(u => u + gridOrigin);
    public bool IsLinked(int socket) => links.Any(l => l.socket == socket);
}

/// <summary>짝지은 텔레포트 시설 한 쌍(방 id와 템플릿 맵 층 안 텔레포트 항목 번호).</summary>
public struct DungeonTeleportLink
{
    public int roomA;
    public int padA;
    public int roomB;
    public int padB;
}

/// <summary>던전 하나(= Wave 하나)의 방 배치와 연결. 순수 데이터이며 생성기(<see cref="DungeonGenerator"/>)가 만든다.</summary>
public sealed class DungeonLayout
{
    public int seed;
    public readonly List<DungeonRoomNode> rooms = new();
    /// <summary>던전 격자 단위 칸 → 방 id.</summary>
    public readonly Dictionary<Vector2Int, int> occupancy = new();
    public int startRoom;
    /// <summary>보스 방 id(없으면 -1).</summary>
    public int bossRoom = -1;
    /// <summary>짝지은 텔레포트(생성 규칙 v2). padA·padB는 각 방 템플릿 맵 층에서 읽은 텔레포트 항목 순번(0부터).</summary>
    public readonly System.Collections.Generic.List<DungeonTeleportLink> teleports = new();
    /// <summary>
    /// 목표 배치(생성 규칙 v3-12). 목표 SO(<see cref="DungeonObjectiveSO.TryPlace"/>)가 텔레포트 뒤 별도 난수로 고른 방·슬롯·진행 번호.
    /// 목표가 없는 던전이면 비어 있다.
    /// </summary>
    public readonly List<DungeonObjectiveAssignment> objectives = new();

    public DungeonRoomNode Room(int id) => id >= 0 && id < rooms.Count ? rooms[id] : null;
}
