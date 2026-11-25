using System;
using UnityEngine;

/// <summary>방 템플릿의 모양. 미니맵과 연결 그래프에서 쓰는 단위 칸 집합으로 나타낸다(실제 방 크기와는 별개).</summary>
public enum DungeonRoomShape
{
    /// <summary>1×1. 보스 방(기존 결투장 실내 26×17).</summary>
    Single = 0,
    /// <summary>1×2 수직 터널.</summary>
    VerticalTunnel = 1,
    /// <summary>2×1 수평 터널.</summary>
    HorizontalTunnel = 2,
    /// <summary>'L' 모양: 왼쪽 위·왼쪽 아래·오른쪽 아래(오른쪽 위가 빔).</summary>
    LShape = 3,
    /// <summary>'ㄱ' 모양: 왼쪽 위·오른쪽 위·오른쪽 아래(왼쪽 아래가 빔).</summary>
    GiyeokShape = 4,
    /// <summary>3×1 가로 긴 방(4차, 예: 실내 ≈ 78×17).</summary>
    HorizontalLong = 5,
    /// <summary>1×3 세로 긴 방(4차, 예: 실내 ≈ 23×59).</summary>
    VerticalLong = 6,
    /// <summary>2×2 네모 방(4차, 단위 칸 4개 모두 — 가운데를 고체 덩어리로 막는 回 컨셉).</summary>
    Square = 7,
}

/// <summary>방 템플릿의 역할.</summary>
public enum DungeonRoomRole
{
    Normal = 0,
    Boss = 1,
}

/// <summary>방 크기 등급. 작은 방은 큰 방의 1/4 크기까지(실내 23×11 이상 — 카메라 확대 없음).</summary>
public enum DungeonRoomSize { Large = 0, Small = 1 }

/// <summary>방의 주된 이동 방향. 생성기가 수평·수직이 번갈아 나오도록 균형을 맞춘다(원거리·근거리 무기 교대).</summary>
public enum DungeonRoomOrientation { Horizontal = 0, Vertical = 1, Mixed = 2 }

/// <summary>방 배경. Auto = 크기로 판정(작은 방이면 Wall), Parallax = 큰 시차 배경, Wall = 정적 벽면 키트.</summary>
public enum DungeonBackdropMode { Auto = 0, Parallax = 1, Wall = 2 }

/// <summary>정적 벽면 키트 종류(지역 변화). 실제 Prefab은 DungeonRulesSO.wallKits가 정한다(테마별 교체 가능).</summary>
public enum DungeonWallKit { None = 0, Pipe = 1, Duct = 2, CableTrunk = 3, Facility = 4 }

/// <summary>방 템플릿 꼬리표(생성 규칙용).</summary>
[System.Flags]
public enum DungeonRoomTags
{
    None = 0,
    /// <summary>휴식 방(수리대 등). 던전당 개수 제한.</summary>
    Rest = 1,
    /// <summary>텔레포트 허브(큰 방 중계실).</summary>
    TeleportHub = 2,
    /// <summary>텔레포트 끝점(작은 중계 노드).</summary>
    TeleportNode = 4,
    /// <summary>막다른 방(출입구 하나로 붙이는 것을 선호).</summary>
    DeadEnd = 8,
    /// <summary>시작 방으로 쓰지 않음(떨어짐·컨베이어·함정 등).</summary>
    NoStart = 16,
    /// <summary>크레인 없음 예외(4차 템플릿 규칙 v4-3 — 휴식 방·피스톤 복도만). 빌더·정적 검사기·격자 Tool 검사가 같은 뜻으로 본다.</summary>
    NoCrane = 32,
    /// <summary>함정 위주 방(4차 템플릿 규칙 v4-5 — 함정 묶음 2~3개 허용).</summary>
    TrapHeavy = 64,
}

/// <summary>출입구가 있는 벽. v1은 좌우 벽(가로 연결)만 쓴다.</summary>
public enum DungeonDoorSide
{
    Left = 0,
    Right = 1,
}

/// <summary>모양별 단위 칸 표.</summary>
public static class DungeonShapes
{
    private static readonly Vector2Int[] single = { new(0, 0) };
    private static readonly Vector2Int[] vertical = { new(0, 0), new(0, 1) };
    private static readonly Vector2Int[] horizontal = { new(0, 0), new(1, 0) };
    private static readonly Vector2Int[] lShape = { new(0, 0), new(0, 1), new(1, 0) };
    private static readonly Vector2Int[] giyeok = { new(0, 1), new(1, 1), new(1, 0) };
    private static readonly Vector2Int[] horizontalLong = { new(0, 0), new(1, 0), new(2, 0) };
    private static readonly Vector2Int[] verticalLong = { new(0, 0), new(0, 1), new(0, 2) };
    private static readonly Vector2Int[] square = { new(0, 0), new(1, 0), new(0, 1), new(1, 1) };

    /// <summary>모양의 단위 칸(왼쪽 아래 기준 0 이상). 반환한 배열은 고치지 않는다.</summary>
    public static Vector2Int[] Units(DungeonRoomShape shape) => shape switch
    {
        DungeonRoomShape.VerticalTunnel => vertical,
        DungeonRoomShape.HorizontalTunnel => horizontal,
        DungeonRoomShape.LShape => lShape,
        DungeonRoomShape.GiyeokShape => giyeok,
        DungeonRoomShape.HorizontalLong => horizontalLong,
        DungeonRoomShape.VerticalLong => verticalLong,
        DungeonRoomShape.Square => square,
        _ => single,
    };

    /// <summary>모양의 외접 크기(단위 칸 수).</summary>
    public static Vector2Int Size(DungeonRoomShape shape) => shape switch
    {
        DungeonRoomShape.VerticalTunnel => new Vector2Int(1, 2),
        DungeonRoomShape.HorizontalTunnel => new Vector2Int(2, 1),
        DungeonRoomShape.LShape or DungeonRoomShape.GiyeokShape or DungeonRoomShape.Square => new Vector2Int(2, 2),
        DungeonRoomShape.HorizontalLong => new Vector2Int(3, 1),
        DungeonRoomShape.VerticalLong => new Vector2Int(1, 3),
        _ => Vector2Int.one,
    };

    public static bool Contains(DungeonRoomShape shape, Vector2Int unit) => Array.IndexOf(Units(shape), unit) >= 0;

    /// <summary>side 벽 너머의 이웃 단위 칸 방향.</summary>
    public static Vector2Int Outward(DungeonDoorSide side) => side == DungeonDoorSide.Right ? Vector2Int.right : Vector2Int.left;

    public static DungeonDoorSide Opposite(DungeonDoorSide side) => side == DungeonDoorSide.Right ? DungeonDoorSide.Left : DungeonDoorSide.Right;

    /// <summary>Tool 표시 이름.</summary>
    public static string DisplayName(DungeonRoomShape shape) => shape switch
    {
        DungeonRoomShape.VerticalTunnel => "수직 터널 (1×2)",
        DungeonRoomShape.HorizontalTunnel => "수평 터널 (2×1)",
        DungeonRoomShape.LShape => "L 모양",
        DungeonRoomShape.GiyeokShape => "ㄱ 모양",
        DungeonRoomShape.HorizontalLong => "가로 긴 방 (3×1)",
        DungeonRoomShape.VerticalLong => "세로 긴 방 (1×3)",
        DungeonRoomShape.Square => "네모 방 (2×2)",
        _ => "단일 (1×1)",
    };
}
