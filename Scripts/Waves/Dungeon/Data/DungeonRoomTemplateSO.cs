using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>방 템플릿의 적 구성(Enemy 템플릿) 정보. <see cref="DungeonRoomTemplateSO.waves"/>[번호]와 같은 순서다.</summary>
[Serializable]
public sealed class DungeonEncounterInfo
{
    public string name = "적 구성";
    [Range(1, 3)] public int tier = 1;
    [Min(0f)] public float weight = 1f;
}

/// <summary>템플릿 맵 층에서 읽은 출입구 자리(읽은 순간의 값, 템플릿 좌표).</summary>
[Serializable]
public struct DungeonDoorSocket
{
    /// <summary>맵 층 spawnObjects 안 번호. Tool이 배열을 다시 정렬할 수 있으므로 한 번 읽은 동안에만 쓴다.</summary>
    public int spawnIndex;
    /// <summary>출입구 Prefab 기준점(점유 2×3칸의 가운데, 템플릿 좌표).</summary>
    public Vector2 position;
    public int rotationSteps;
    public DungeonDoorSide side;
    /// <summary>출입구가 붙은 단위 칸(모양 기준).</summary>
    public Vector2Int unit;
    /// <summary>연결되면 지울 벽 칸(안쪽 2칸 × 높이 3칸, 템플릿 셀 좌표). 바깥 1칸은 남겨 방 공간이 새지 않게 한다.</summary>
    public RectInt carve;
    /// <summary>출입구 바닥 높이(이 높이의 칸 위에 Player가 선다).</summary>
    public int floorY;
    /// <summary>방 실내 쪽 벽면 x(오른쪽 벽이면 실내 xMax, 왼쪽 벽이면 실내 xMin).</summary>
    public int wallFaceX;
}

/// <summary>
/// 던전 방 템플릿. WaveDataSO를 상속해 Wave 격자 Tool이 그대로 편집한다.
/// waves[0] = 맵 층(지형·Prop·출입구·고정 함정), waves[1..] = 적 구성(Enemy 템플릿).
/// 적 구성 Wave는 지형을 바꾸지 않는다(overrideCellMap·changeTerrain 끔) — 맵 층 지형을 순차 상속으로 함께 쓴다.
/// 좌표는 기존 결투장과 같은 기준이다: 단위 칸(0,0)의 실내 왼쪽 아래 = (-4,-3), 벽 두께 3, spawnOffset (0,0).
/// </summary>
[CreateAssetMenu(menuName = "Training/Dungeon Room Template", fileName = "DungeonRoomTemplate")]
public class DungeonRoomTemplateSO : WaveDataSO
{
    public const int WallThickness = 3;

    [Header("던전 방 템플릿")]
    public DungeonRoomShape shape = DungeonRoomShape.Single;
    public DungeonRoomRole role = DungeonRoomRole.Normal;
    [Tooltip("던전의 시작 방(적 없음)으로 쓸 수 있는지.")]
    public bool allowAsStart = true;
    [Tooltip("이 방의 컨셉 한 줄(격자 Tool·문서 표시용). 예: '완전 대칭 아레나', '생산 라인: 컨베이어로 적이 실려 온다'.")]
    [TextArea(1, 3)] public string concept = "";
    [Tooltip("크기 등급. 작은 방은 큰 방의 1/4 크기까지(실내 23×11 이상).")]
    public DungeonRoomSize sizeClass = DungeonRoomSize.Large;
    [Tooltip("주된 이동 방향. 생성기가 수평·수직을 번갈아 고르도록 쓴다.")]
    public DungeonRoomOrientation orientation = DungeonRoomOrientation.Mixed;
    [Tooltip("방 배경. Auto면 작은 방은 정적 벽면, 큰 방은 시차 배경.")]
    public DungeonBackdropMode backdropMode = DungeonBackdropMode.Auto;
    [Tooltip("정적 벽면 키트(배경이 Wall일 때). 배관·환풍 덕트·전선 트렁크·설비 벽면.")]
    public DungeonWallKit wallKit = DungeonWallKit.None;
    [Tooltip("생성 규칙 꼬리표: 휴식·텔레포트 허브/끝점·막다른 방·시작 불가.")]
    public DungeonRoomTags tags = DungeonRoomTags.None;
    [Tooltip("한 던전에 이 템플릿을 둘 수 있는 최대 수(0 = 던전 설정의 기본값).")]
    [Min(0)] public int maxPerFloor;
    [Tooltip("설계 메모(왜 이렇게 배치했는지). 6차에 사양 JSON의 note를 옮겼다 — 실행에는 쓰이지 않으며 던전 템플릿 편집기에서 고친다.")]
    [TextArea(3, 12)] public string designNote = "";
    /// <summary>실제로 쓸 배경 종류(Auto를 풀어서).</summary>
    public DungeonBackdropMode ResolvedBackdrop => backdropMode != DungeonBackdropMode.Auto ? backdropMode
        : sizeClass == DungeonRoomSize.Small ? DungeonBackdropMode.Wall : DungeonBackdropMode.Parallax;
    public bool HasTag(DungeonRoomTags tag) => (tags & tag) != 0;
    [Tooltip("적 구성 정보. waves[1..]과 같은 순서이며 길이는 자동으로 맞춘다.")]
    public DungeonEncounterInfo[] encounters = Array.Empty<DungeonEncounterInfo>();

    public Wave MapLayer => waves != null && waves.Length > 0 ? waves[0] : null;
    public int EncounterCount => Mathf.Max(0, (waves?.Length ?? 0) - 1);
    /// <summary>적 구성 Wave(번호 1부터). 없으면 null.</summary>
    public Wave EncounterWave(int number) => number >= 1 && waves != null && number < waves.Length ? waves[number] : null;
    /// <summary>적 구성 정보(번호 1부터). 읽기 전용 — 길이가 어긋나 있으면 null(에셋을 고치지 않는다).</summary>
    public DungeonEncounterInfo EncounterInfo(int number)
        => encounters != null && number >= 1 && number <= encounters.Length ? encounters[number - 1] : null;
    /// <summary>맵 층의 지형 칸(앞 Wave 순차 상속 규칙 그대로).</summary>
    public WaveMapCell[] MapCells => waves != null && waves.Length > 0 ? WaveCellState.Initial(this, 0) : cellMap.cells;

    /// <summary>맵 층 지형(Platform 칸)의 외접 범위(셀 좌표, 벽 포함). 칸이 없으면 false.</summary>
    public bool TryGetCellBounds(out RectInt bounds)
    {
        bounds = default;
        bool any = false;
        int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
        foreach (var cell in MapCells)
        {
            if (cell.kind != WaveCellKind.Platform) continue;
            any = true;
            xMin = Mathf.Min(xMin, cell.position.x); yMin = Mathf.Min(yMin, cell.position.y);
            xMax = Mathf.Max(xMax, cell.position.x); yMax = Mathf.Max(yMax, cell.position.y);
        }
        if (any) bounds = new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
        return any;
    }

    /// <summary>
    /// 맵 층에 놓인 출입구(<see cref="DungeonDoor"/> Prefab)를 읽어 단위 칸·방향·팔 칸을 구한다.
    /// 회전 0 = 오른쪽 벽(밖이 +x), 회전 2 = 왼쪽 벽. 회전 1·3(위·아래 벽)은 v1에서 오류로 본다.
    /// </summary>
    public bool TryReadDoors(List<DungeonDoorSocket> doors, out string error)
    {
        doors.Clear();
        error = null;
        var map = MapLayer;
        if (map == null) { error = "맵 층(Wave 1)이 없습니다."; return false; }
        if (!TryGetCellBounds(out var box)) { error = "맵 층에 지형 칸이 없습니다."; return false; }
        var size = DungeonShapes.Size(shape);
        var objects = map.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
        for (int i = 0; i < objects.Length; i++)
        {
            var info = objects[i];
            if (info?.prefab == null || info.prefab.GetComponent<DungeonDoor>() == null) continue;
            int steps = ((info.rotationSteps % 4) + 4) % 4;
            if (steps == 1 || steps == 3) { error = $"출입구 {i + 1}번이 위·아래 벽 방향입니다(좌우 벽만 지원)."; return false; }
            var side = steps == 0 ? DungeonDoorSide.Right : DungeonDoorSide.Left;
            var p = info.spawnPosition;
            int floorY = Mathf.RoundToInt(p.y - DungeonDoor.Height * .5f);
            int carveX = Mathf.RoundToInt(p.x - DungeonDoor.Depth * .5f);
            int wallFace = side == DungeonDoorSide.Right ? carveX : carveX + DungeonDoor.Depth;
            // 단위 칸은 출입구 바로 앞 실내 칸을 실내 외접 범위(셀 외접 − 벽 두께)로 등분해 정한다.
            // 셀 외접으로 나누면 L·ㄱ의 오목한 쪽 벽 출입구가 경계 너머 이웃 칸으로 읽힌다.
            var inside = new Vector2(side == DungeonDoorSide.Right ? wallFace - .5f : wallFace + .5f, floorY + .5f);
            float ix = box.xMin + WallThickness, iy = box.yMin + WallThickness;
            float iw = Mathf.Max(1f, box.width - 2 * WallThickness), ih = Mathf.Max(1f, box.height - 2 * WallThickness);
            int col = Mathf.Clamp(Mathf.FloorToInt((inside.x - ix) / iw * size.x), 0, size.x - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt((inside.y - iy) / ih * size.y), 0, size.y - 1);
            var unit = new Vector2Int(col, row);
            if (!DungeonShapes.Contains(shape, unit)) { error = $"출입구 {i + 1}번이 모양에 없는 단위 칸 {unit}에 있습니다."; return false; }
            if (DungeonShapes.Contains(shape, unit + DungeonShapes.Outward(side))) { error = $"출입구 {i + 1}번이 바깥 벽이 아닌 쪽(같은 방의 이웃 단위 칸)을 향합니다."; return false; }
            doors.Add(new DungeonDoorSocket
            {
                spawnIndex = i,
                position = p,
                rotationSteps = steps,
                side = side,
                unit = unit,
                carve = new RectInt(carveX, floorY, DungeonDoor.Depth, DungeonDoor.Height),
                floorY = floorY,
                wallFaceX = wallFace,
            });
        }
        return true;
    }

    /// <summary>적 구성 정보 길이를 Wave 수에 맞춘다.</summary>
    public void SyncEncounters()
    {
        int count = EncounterCount;
        if (encounters != null && encounters.Length == count) return;
        var next = new DungeonEncounterInfo[count];
        for (int i = 0; i < count; i++)
            next[i] = encounters != null && i < encounters.Length && encounters[i] != null ? encounters[i] : new DungeonEncounterInfo { name = $"적 구성 {i + 1}" };
        encounters = next;
    }

    protected virtual void OnValidate() => SyncEncounters();
}
