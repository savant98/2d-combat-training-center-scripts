using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>생성 후보 지점 하나(발 위치 = 몸 아래 가운데, 맵 좌표).</summary>
public readonly struct DungeonSpawnCandidate
{
    /// <summary>발 위치(지상 = 서는 면 윗면 가운데, 공중 = 몸 아래 가운데).</summary>
    public readonly Vector2 feet;
    public readonly bool air;
    /// <summary>고르는 가중치(기본 1, 생성 구역 안이면 ×(1 + 구역 가중치)).</summary>
    public readonly float weight;
    /// <summary>지상 후보면 그 발 아래 연속 바닥 길이(칸). 공중이면 0.</summary>
    public readonly int floorRun;
    /// <summary>발 위로 비어 있는 높이(칸).</summary>
    public readonly float headroom;

    public DungeonSpawnCandidate(Vector2 feet, bool air, float weight, int floorRun = 0, float headroom = 0f)
    {
        this.feet = feet;
        this.air = air;
        this.weight = weight;
        this.floorRun = floorRun;
        this.headroom = headroom;
    }
}

/// <summary>생성할 몸 하나(묶음이면 묶음 전체)의 자리 조건.</summary>
public readonly struct DungeonSpawnBody
{
    /// <summary>비어 있어야 하는 몸 크기(칸, 발 위치 기준 가로 가운데·위로).</summary>
    public readonly Vector2 size;
    public readonly bool air;
    /// <summary>연속 바닥·머리 위 여유가 필요한지(Bulwark).</summary>
    public readonly bool needsFloorRun;
    /// <summary>needsFloorRun일 때 필요한 연속 바닥(칸).</summary>
    public readonly int floorRun;
    /// <summary>needsFloorRun일 때 필요한 머리 위 여유(칸).</summary>
    public readonly float headroom;
    /// <summary>
    /// 지상 몸의 폭 전체 아래가 받침이어야 하는지(지상 묶음 — 묶음 몸 폭 안에 개체를 나란히 두므로 가장자리 개체도 바닥 위에 서게).
    /// 끄면 발 가운데 칸만 받침이면 된다(단일 지상 적·Bulwark — 3차·4차 기본 동작).
    /// </summary>
    public readonly bool fullSupport;

    public DungeonSpawnBody(Vector2 size, bool air, bool needsFloorRun = false, int floorRun = 0, float headroom = 0f, bool fullSupport = false)
    {
        this.size = size;
        this.air = air;
        this.needsFloorRun = needsFloorRun;
        this.floorRun = floorRun;
        this.headroom = headroom;
        this.fullSupport = fullSupport;
    }

    /// <summary>발 위치에 이 몸을 두었을 때의 사각형(맵 좌표).</summary>
    public Rect RectAt(Vector2 feet) => new Rect(feet.x - size.x * .5f, feet.y, size.x, size.y);
}

/// <summary>
/// 후보 지점 계산 입력(4차). 좌표는 셀·사각형·Player 발이 모두 같은 기준이면 된다 — 실행 중에는 방 데이터 좌표(템플릿 좌표 = 방 로컬,
/// DungeonManager.Origin()을 더하기 전)를 권한다. 그러면 Compose 결과의 spawnPosition을 그대로 DungeonEncounter(origin = Origin())에 넘길 수 있다.
/// 물리 질의 없이 셀 데이터와 방에 세운 Prop의 Collider2D.bounds(읽기)만 쓴다.
/// </summary>
public sealed class DungeonSpawnMapInput
{
    /// <summary>이 방에서 켠 지형 칸(칸 (x, y)는 [x, x+1]×[y, y+1]).</summary>
    public WaveMapCell[] cells = Array.Empty<WaveMapCell>();
    /// <summary>실내 외접 범위(칸). 비어 있으면 셀 외접에서 벽 두께(3)를 뺀 범위를 쓴다.</summary>
    public RectInt interior;
    /// <summary>고체 Prop 경계(받침·막힘으로 본다 — 고체·OneWay 발판 Prop).</summary>
    public List<Rect> solidProps = new List<Rect>();
    /// <summary>생성 구역(사각형, kind = (int)DungeonSpawnZoneKind, weight = 구역 가중치 — DungeonSpawnZone.TryRead).</summary>
    public List<(Rect rect, int kind, float weight)> zones = new List<(Rect rect, int kind, float weight)>();
    /// <summary>생성 금지 사각형: 출입구 앞 공간·텔레포트 패드·목표/출구 자리·함정 칸 + 1칸·크레인 경로·시작 좌표·파괴 Prop.</summary>
    public List<Rect> exclusions = new List<Rect>();
    /// <summary>만들 때의 Player 발 위치. 실행 중 바뀌면 SetPlayer로 갱신한다.</summary>
    public Vector2 playerFeet;
    /// <summary>Player 몸에서 이 거리(칸) 안에는 고르지 않는다(첫 파도 8 · 이후 6 — 규칙 SO의 값).</summary>
    public float playerClearance = 8f;

    // ── 채우기 도우미(DungeonManager·Tool 미리보기·회귀가 같은 제외 규칙을 쓰게) ──

    /// <summary>출입구 앞 공간: 실내 벽면에서 안쪽 3칸 × 바닥부터 4칸(빌더 DoorZone 2×3에 한 칸씩 여유).</summary>
    public const int DoorFrontDepth = 3, DoorFrontHeight = 4;
    /// <summary>텔레포트 패드 중심에서 이 거리(칸) 안의 상자는 제외(빌더 TeleportSpawnGap 4와 같다).</summary>
    public const float TeleportGap = 4f;
    /// <summary>함정·시설·목표 자리 둘레 여유(칸).</summary>
    public const float HazardMargin = 1f;

    /// <summary>
    /// 실행 데이터 방 하나로 입력을 만든다(좌표 = 방 데이터 좌표). liveSolidProps·liveBlockers를 주면(방에 세운 Prop의 Collider2D.bounds를
    /// 방 데이터 좌표로 옮긴 값) Prefab 추정 대신 그 값을 쓴다 — 중력 상자처럼 움직인 Prop의 현재 자리를 반영할 때.
    /// </summary>
    public static DungeonSpawnMapInput ForRoom(DungeonRoomData room, Vector2 playerFeet, IReadOnlyList<Rect> liveSolidProps = null, IReadOnlyList<Rect> liveBlockers = null)
    {
        var input = new DungeonSpawnMapInput { playerFeet = playerFeet };
        if (room == null) return input;
        input.cells = room.cells ?? Array.Empty<WaveMapCell>();
        if (room.interior.width > 0f && room.interior.height > 0f)
            input.interior = new RectInt(Mathf.FloorToInt(room.interior.xMin + .01f), Mathf.FloorToInt(room.interior.yMin + .01f),
                Mathf.RoundToInt(room.interior.width), Mathf.RoundToInt(room.interior.height));
        bool estimate = liveSolidProps == null;
        if (room.props != null)
            foreach (var info in room.props) input.AddSpawn(info, estimate);
        if (liveSolidProps != null) input.solidProps.AddRange(liveSolidProps);
        if (liveBlockers != null) input.exclusions.AddRange(liveBlockers);
        if (room.doors != null)
            foreach (var door in room.doors) if (door != null) input.AddDoorFront(door.info.side, door.info.wallFaceX, door.info.floorY);
        if (room.teleports != null)
            foreach (var pad in room.teleports) if (pad?.info?.prefab != null) input.AddTeleport(pad.info);
        if (room.objectives != null)
            foreach (var slot in room.objectives) if (slot?.info?.prefab != null) input.AddFootprint(slot.info, HazardMargin);
        if (room.exit?.prefab != null) input.AddFootprint(room.exit, HazardMargin);
        if (room.spawnZones != null)
            foreach (var zone in room.spawnZones) input.AddZone(zone);
        input.AddStart(room.entry);
        return input;
    }

    /// <summary>
    /// 템플릿 맵 층으로 입력을 만든다(Tool 미리보기·회귀·정적 검사용, 좌표 = 템플릿 좌표). Player는 없는 것으로 본다(거리 제한 0).
    /// 고체 Prop은 Prefab으로 추정한다(DungeonProp 종류 Platform + 충돌체).
    /// </summary>
    public static DungeonSpawnMapInput ForTemplate(DungeonRoomTemplateSO template)
    {
        var input = new DungeonSpawnMapInput { playerClearance = 0f, playerFeet = new Vector2(-100000f, -100000f) };
        if (template == null) return input;
        input.cells = template.MapCells ?? Array.Empty<WaveMapCell>();
        if (template.TryGetCellBounds(out var box))
        {
            int wall = DungeonRoomTemplateSO.WallThickness;
            input.interior = new RectInt(box.xMin + wall, box.yMin + wall, Mathf.Max(0, box.width - wall * 2), Mathf.Max(0, box.height - wall * 2));
        }
        var map = template.MapLayer;
        if (map?.spawnObjects != null)
            foreach (var info in map.spawnObjects)
            {
                if (info?.prefab == null) continue;
                if (info.prefab.GetComponent<DungeonDoor>() != null) continue;   // 출입구는 아래 TryReadDoors로
                if (info.prefab.GetComponent<DungeonTeleporter>() != null) { input.AddTeleport(info); continue; }
                input.AddSpawn(info, true);
            }
        var doors = new List<DungeonDoorSocket>();
        if (template.TryReadDoors(doors, out _))
            foreach (var door in doors) input.AddDoorFront(door.side, door.wallFaceX, door.floorY);
        input.AddStart(WaveRoomState.PlayerStartAt(template, 0));
        return input;
    }

    /// <summary>
    /// 맵 층 항목 하나를 규칙대로 넣는다: 생성 구역 → zones, 목표·출구 자리·시설·함정 → 제외(+1칸, 함정은 작동 범위도),
    /// 크레인 → 경로 쓸기 상자 제외, 파괴 Prop → 제외, 고체 발판 Prop → solidProps(estimateSolid일 때). 적·출입구는 건너뛴다.
    /// </summary>
    public void AddSpawn(WaveSpawnInfo info, bool estimateSolid)
    {
        if (info?.prefab == null) return;
        var prefab = info.prefab;
        if (prefab.GetComponent<EnemyController>() != null || prefab.GetComponent<DungeonDoor>() != null) return;
        if (prefab.GetComponent<DungeonSpawnZone>() != null) { AddZone(info); return; }
        if (prefab.GetComponent<DungeonTeleporter>() != null) { AddTeleport(info); return; }
        if (prefab.GetComponent<DungeonObjectiveSlot>() != null || prefab.GetComponent<DungeonExitSlot>() != null) { AddFootprint(info, HazardMargin); return; }
        var settings = prefab.GetComponent<IWaveDeviceSettings>();
        if (TryAddCrane(info, settings)) return;
        var prop = prefab.GetComponent<DungeonProp>();
        if (prop != null)
        {
            switch (prop.Kind)
            {
                case DungeonPropKind.Platform:
                    if (estimateSolid && HasSolidCollider(prefab) && TryFootprint(info, 0f, out var rect)) solidProps.Add(rect);
                    return;
                case DungeonPropKind.Destructible:
                    AddFootprint(info, 0f);
                    return;
                case DungeonPropKind.Hazard:
                    AddFootprint(info, HazardMargin);
                    AddRange(info, settings);
                    return;
                default:
                    // 장식은 막지 않는다. 상호작용 시설(수리대·레버 등)은 그 앞에서 적이 나오지 않게 둘레까지 뺀다.
                    if (prefab.GetComponentInChildren<FacilityInteractPoint>(true) != null) AddFootprint(info, HazardMargin);
                    return;
            }
        }
        // DungeonProp이 아닌 Wave 장치(톱날·레이저·피스톤·가시·분쇄기 등)는 함정으로 본다(차지 칸 + 1칸, 작동 범위 상자).
        if (prefab.GetComponent<WaveEnvironmentTool>() != null)
        {
            AddFootprint(info, HazardMargin);
            AddRange(info, settings);
        }
    }

    /// <summary>출입구 앞 공간(관에서 나온 Player가 서는 자리)을 뺀다.</summary>
    public void AddDoorFront(DungeonDoorSide side, int wallFaceX, int floorY)
    {
        float x = side == DungeonDoorSide.Right ? wallFaceX - DoorFrontDepth : wallFaceX;
        exclusions.Add(new Rect(x, floorY, DoorFrontDepth, DoorFrontHeight));
    }

    /// <summary>텔레포트 패드 둘레(중심에서 TeleportGap, 차지 칸 + 1칸 중 큰 쪽)를 뺀다.</summary>
    public void AddTeleport(WaveSpawnInfo info)
    {
        if (info?.prefab == null) return;
        var size = FootprintSize(info);
        float hx = Mathf.Max(size.x * .5f + HazardMargin, TeleportGap), hy = Mathf.Max(size.y * .5f + HazardMargin, TeleportGap);
        exclusions.Add(new Rect(info.spawnPosition.x - hx, info.spawnPosition.y - hy, hx * 2f, hy * 2f));
    }

    /// <summary>장치 차지 칸(회전 반영) + margin을 뺀다.</summary>
    public void AddFootprint(WaveSpawnInfo info, float margin)
    {
        if (TryFootprint(info, margin, out var rect)) exclusions.Add(rect);
    }

    /// <summary>생성 구역 표식 항목을 zones에 넣는다.</summary>
    public void AddZone(WaveSpawnInfo info)
    {
        if (DungeonSpawnZone.TryRead(info, Vector2.zero, out var rect, out int kind, out float weight)) zones.Add((rect, kind, weight));
    }

    /// <summary>시작 좌표(발 가운데) 둘레 3×3칸을 뺀다.</summary>
    public void AddStart(Vector2 feet)
    {
        if (!float.IsFinite(feet.x) || !float.IsFinite(feet.y)) return;
        exclusions.Add(new Rect(feet.x - 1.5f, feet.y, 3f, 3f));
    }

    // 경로 크레인(cranePathCount·cranePath{i}X/Y)·고정 크레인(zipRadius): 매달린 몸이 지나는 '발 ~ 발 + 4칸, 폭 2칸' 쓸기 상자와 차지 칸을 뺀다.
    private bool TryAddCrane(WaveSpawnInfo info, IWaveDeviceSettings settings)
    {
        if (settings == null) return false;
        WaveDeviceParameter[] parameters;
        try { parameters = settings.Parameters; }
        catch (Exception) { return false; }
        if (parameters == null) return false;
        bool path = TryParameter(parameters, "cranePathCount", out var countParameter);
        bool anchor = TryParameter(parameters, "zipRadius", out _);
        if (!path && !anchor) return false;
        AddFootprint(info, HazardMargin);
        if (!path || !TryFootprint(info, 0f, out var foot)) return true;
        var overrides = info.deviceSettings ?? WaveDeviceSettings.None;
        int count = Mathf.Clamp(Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, countParameter)), 2, 8);
        // 집(점 0)의 매달린 발 = 차지 칸 (x + 1, y) — 2×4 크레인의 왼쪽 아래 칸 기준(DESIGN_V4 §2.3).
        Vector2 home = new Vector2(foot.xMin + 1f, foot.yMin), previous = home;
        for (int i = 1; i < count; i++)
        {
            float dx = TryParameter(parameters, $"cranePath{i}X", out var px) ? WaveDeviceSettings.Value(overrides, px) : 0f;
            float dy = TryParameter(parameters, $"cranePath{i}Y", out var py) ? WaveDeviceSettings.Value(overrides, py) : 0f;
            var next = home + new Vector2(dx, dy);
            AddSweep(previous, next);
            previous = next;
        }
        return true;
    }

    // 구간 하나의 쓸기 상자(실행 쪽 DungeonSpawnBlockers.Collect와 같은 도우미).
    private void AddSweep(Vector2 a, Vector2 b) => AddCraneSweep(exclusions, a, b);

    /// <summary>경로 크레인 쓸기 상자 표본 간격(칸). 템플릿 검사(0.25칸 표본)보다 성기되 칸 단위 제외에는 충분히 촘촘하다.</summary>
    public const float CraneSweepStep = .5f;
    /// <summary>경로 크레인 쓸기 상자 높이·반폭(칸): 매달린 발 ~ 발 + 4칸, 폭 2칸(DESIGN_V4 §2.3 — 템플릿 규칙 v4-3과 같은 뜻).</summary>
    public const float CraneSweepHeight = 4f, CraneSweepHalfWidth = 1f;

    /// <summary>
    /// 경로 크레인 구간 a → b(매달린 발, 같은 좌표계)의 쓸기 상자를 into에 넣는다. 축 정렬 구간이면 상자 하나(외접 = 쓸기 영역과 같다),
    /// 비스듬한 구간이면 CraneSweepStep 간격 표본마다 '발 ~ 발 + 4칸, 폭 2칸' 상자를 넣는다 — 외접 사각형 하나로 막으면 대각선 구간에서
    /// 쓸기 영역 밖 칸까지 막는다(4차 검토). 실행 중 생성 제외(DungeonSpawnBlockers)와 템플릿 미리보기(TryAddCrane)가 함께 쓴다.
    /// </summary>
    public static void AddCraneSweep(List<Rect> into, Vector2 a, Vector2 b)
    {
        if (into == null) return;
        const float half = CraneSweepHalfWidth, height = CraneSweepHeight;
        if (Mathf.Abs(a.x - b.x) < .01f || Mathf.Abs(a.y - b.y) < .01f)
        {
            into.Add(Rect.MinMaxRect(Mathf.Min(a.x, b.x) - half, Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x) + half, Mathf.Max(a.y, b.y) + height));
            return;
        }
        int steps = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(a, b) / CraneSweepStep), 1, 512);
        for (int s = 0; s <= steps; s++)
        {
            var p = Vector2.Lerp(a, b, s / (float)steps);
            into.Add(new Rect(p.x - half, p.y, half * 2f, height));
        }
    }

    // 함정 작동 범위(IWaveDeviceSettings.TryGetRange — 장치 원점 기준, 회전 함께)의 외접 상자를 뺀다.
    private void AddRange(WaveSpawnInfo info, IWaveDeviceSettings settings)
    {
        if (settings == null) return;
        WaveDeviceRange range;
        try { if (!settings.TryGetRange(info.deviceSettings ?? WaveDeviceSettings.None, out range)) return; }
        catch (Exception) { return; }
        if (range.Shape == WaveEnvironmentTool.RangeShape.None) return;
        Vector2 size = range.Shape == WaveEnvironmentTool.RangeShape.Circle ? Vector2.one * (range.Size.x * 2f) : range.Size;
        int steps = ((info.rotationSteps % 4) + 4) % 4;
        Vector2 center = Rotate(range.Center, steps);
        if (steps % 2 == 1) size = new Vector2(size.y, size.x);
        var c = info.spawnPosition + center;
        exclusions.Add(new Rect(c.x - size.x * .5f - HazardMargin, c.y - size.y * .5f - HazardMargin, size.x + HazardMargin * 2f, size.y + HazardMargin * 2f));
    }

    private static Vector2 Rotate(Vector2 v, int steps) => steps switch
    {
        1 => new Vector2(-v.y, v.x),
        2 => new Vector2(-v.x, -v.y),
        3 => new Vector2(v.y, -v.x),
        _ => v,
    };

    private static bool TryParameter(WaveDeviceParameter[] parameters, string key, out WaveDeviceParameter parameter)
    {
        foreach (var item in parameters)
            if (item.Key == key) { parameter = item; return true; }
        parameter = default;
        return false;
    }

    private static Vector2Int FootprintSize(WaveSpawnInfo info)
    {
        var tool = info.prefab.GetComponent<WaveEnvironmentTool>();
        var size = tool != null ? tool.Footprint : Vector2Int.one;
        int steps = ((info.rotationSteps % 4) + 4) % 4;
        return steps % 2 == 1 ? new Vector2Int(size.y, size.x) : size;
    }

    // 장치 원점 = 차지 칸 묶음의 가운데(WavePrefabVisuals.Placement 규칙). 그 둘레에 margin을 더한 사각형.
    private static bool TryFootprint(WaveSpawnInfo info, float margin, out Rect rect)
    {
        rect = default;
        if (info?.prefab == null) return false;
        var size = FootprintSize(info);
        rect = new Rect(info.spawnPosition.x - size.x * .5f - margin, info.spawnPosition.y - size.y * .5f - margin, size.x + margin * 2f, size.y + margin * 2f);
        return true;
    }

    private static bool HasSolidCollider(GameObject prefab)
    {
        foreach (var collider in prefab.GetComponentsInChildren<Collider2D>(true))
            if (collider != null && !collider.isTrigger) return true;
        return false;
    }
}

/// <summary>
/// 방의 지상·공중 생성 후보 지점(4차 R16, 순수 C#). 셀 + 고체 Prop 경계 + 생성 구역 표식으로 후보를 만들고,
/// 몸 크기·다른 적과의 간격·Player 거리를 지켜 하나를 고른다. Tool 미리보기·정적 검사도 같은 Build를 쓴다.
/// 규칙(DESIGN_V4 §2.8):
/// - 실내 = 실내 범위 안에서 셀 바깥 테두리와 이어지지 않은 빈 칸(L·ㄱ의 바깥 오목 자리는 빠진다). 빈 칸 수 A = 실내 − 고체 지형 − 고체 Prop(OneWay 칸은 빈 칸으로 셈).
/// - 지상 후보 = 아래 칸이 받침(지형·OneWay·고체 Prop)인 빈 칸. 몸은 지형·OneWay와 겹치지 않는다(OneWay 위에 서는 것은 됨).
/// - 공중 후보 = 빈 칸 중 바로 아래 2칸이 비어 있는 칸(바닥 위 2칸). 몸 위로 1.5칸 이상 비어야 한다(천장 아래 1.5칸).
/// - 제외 사각형과 겹치는 칸, 실내 밖 칸에는 몸을 두지 않는다.
/// 물리 질의 없음 · 같은 입력과 난수면 같은 결과 · Build 뒤 TryPick은 할당하지 않는다.
/// </summary>
public sealed class DungeonSpawnPointMap
{
    /// <summary>같은 파도의 다른 적과 둘 간격(칸, 몸 크기 + 이 값).</summary>
    public const float BodySpacing = .5f;
    /// <summary>공중 후보의 최소 바닥 위 높이·천장 아래 여유(칸).</summary>
    public const float AirFloorGap = 2f, AirCeilingGap = 1.5f;
    /// <summary>계산하는 격자의 최대 칸 수(WaveCellRoomSpace 한도와 같다).</summary>
    public const int MaxGridCells = 262144;
    /// <summary>Player 몸통 중심 높이(발 위, DungeonDoor·DungeonTeleporter와 같은 기준).</summary>
    public const float PlayerBodyLift = .755f;

    // 칸 표시(비트)
    private const byte Solid = 1, OneWay = 2, Inside = 4, Excluded = 8, PropSolid = 16, Outside = 32;
    // 칸 겹침으로 보지 않는 여유(사각형이 칸 경계에 정확히 닿기만 하면 겹침이 아니다).
    private const float Epsilon = .02f;

    private readonly List<DungeonSpawnCandidate> candidates = new List<DungeonSpawnCandidate>();
    private byte[] grid = Array.Empty<byte>();
    private int gx, gy, gw, gh;
    private RectInt interior;
    private int emptyCells;
    private int groundCount, airCount;
    private Vector2 playerFeet;
    private float playerClearance;

    private DungeonSpawnPointMap() { }

    /// <summary>후보 지점을 계산한다(input이 null이거나 격자가 너무 크면 빈 맵).</summary>
    public static DungeonSpawnPointMap Build(DungeonSpawnMapInput input)
    {
        var map = new DungeonSpawnPointMap();
        if (input == null) return map;
        map.playerFeet = input.playerFeet;
        map.playerClearance = Mathf.Max(0f, input.playerClearance);
        map.Compute(input);
        return map;
    }

    /// <summary>후보 지점 수(지상 + 공중).</summary>
    public int Count => candidates.Count;
    /// <summary>지상·공중 후보 수.</summary>
    public int GroundCount => groundCount;
    public int AirCount => airCount;
    /// <summary>후보 지점 전부(Tool 미리보기·검사용). 지상 다음 공중, 각각 아래 → 위·왼쪽 → 오른쪽 순서.</summary>
    public IReadOnlyList<DungeonSpawnCandidate> Candidates => candidates;
    /// <summary>빈 칸 수 A(실내 − 고체 지형 − 고체 Prop). 적 수 공식의 면적.</summary>
    public int EmptyCells => emptyCells;
    /// <summary>실내 범위(칸)와 그 높이(Bulwark 조건 '실내 높이 ≥ 17').</summary>
    public RectInt Interior => interior;
    public int InteriorHeight => interior.height;
    /// <summary>지금 Player 발 위치와 거리 제한.</summary>
    public Vector2 PlayerFeet => playerFeet;
    public float PlayerClearance => playerClearance;

    /// <summary>실행 중 Player 위치·거리 제한을 갱신한다(파도마다 — 첫 파도 8칸, 이후 6칸).</summary>
    public void SetPlayer(Vector2 feet, float clearance)
    {
        playerFeet = feet;
        playerClearance = Mathf.Max(0f, clearance);
    }

    /// <summary>칸이 실내 빈 칸(고체·OneWay 아님)인지. 범위 밖이면 false.</summary>
    public bool IsOpen(int x, int y) => TryFlags(x, y, out byte f) && (f & Inside) != 0 && (f & (Solid | OneWay)) == 0;
    /// <summary>칸이 받침(지형·OneWay·고체 Prop)인지.</summary>
    public bool IsSupport(int x, int y) => TryFlags(x, y, out byte f) && (f & (Solid | OneWay)) != 0;
    /// <summary>칸이 제외 사각형에 걸리는지.</summary>
    public bool IsExcluded(int x, int y) => TryFlags(x, y, out byte f) && (f & Excluded) != 0;

    /// <summary>
    /// body가 들어가는 후보 하나를 가중치로 고른다. occupied(같은 파도에 이미 고른 몸 사각형)와 몸 크기 + BodySpacing 간격,
    /// Player 거리 제한(Player 몸통 중심 ~ 몸 사각형의 가장 가까운 점)을 지킨다. 고르지 못하면 false. 할당 없음.
    /// </summary>
    public bool TryPick(DungeonSpawnBody body, System.Random rng, IReadOnlyList<Rect> occupied, out Vector2 feet)
    {
        feet = default;
        if (rng == null || candidates.Count == 0 || body.size.x <= 0f || body.size.y <= 0f) return false;
        Vector2 player = playerFeet + new Vector2(0f, PlayerBodyLift);
        float clearSq = playerClearance * playerClearance;
        double total = 0d;
        bool found = false;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c.air != body.air || c.weight <= 0f) continue;
            if (!Fits(body, c, out var rect)) continue;
            if (playerClearance > 0f && SqrDistance(rect, player) < clearSq) continue;
            if (occupied != null && Crowded(rect, occupied)) continue;
            // 가중 저수지 추출(하나): 지금까지 합 중 이 후보의 몫만큼 바꾼다 — 한 번 훑어 가중치 비례로 고른다.
            total += c.weight;
            if (rng.NextDouble() * total < c.weight) { feet = c.feet; found = true; }
        }
        return found;
    }

    /// <summary>body를 feet에 둘 수 있는지(실내·지형·OneWay·제외·바닥 길이·머리 위·공중 여유). Player·간격은 보지 않는다.</summary>
    public bool CanPlace(DungeonSpawnBody body, Vector2 feet)
    {
        foreach (var c in candidates)
            if (c.air == body.air && (c.feet - feet).sqrMagnitude < 1e-4f) return Fits(body, c, out _);
        return false;
    }

    private bool Fits(DungeonSpawnBody body, in DungeonSpawnCandidate c, out Rect rect)
    {
        rect = body.RectAt(c.feet);
        if (body.needsFloorRun)
        {
            if (c.floorRun < body.floorRun) { return false; }
            if (body.headroom > rect.height) rect.height = body.headroom;
        }
        if (body.air && c.headroom < Mathf.Ceil(body.size.y + AirCeilingGap - Epsilon)) return false;
        if (!body.air && c.headroom < Mathf.Ceil(rect.height - Epsilon)) return false;
        if (!body.air && body.fullSupport && !SupportedBelow(rect)) return false;
        return RectClear(rect);
    }

    // 몸 사각형 폭 전체의 바로 아래 줄이 받침(지형·OneWay·고체 Prop)인지(지상 묶음용).
    private bool SupportedBelow(Rect rect)
    {
        int x0 = Mathf.FloorToInt(rect.xMin + Epsilon), x1 = Mathf.FloorToInt(rect.xMax - Epsilon);
        int y = Mathf.RoundToInt(rect.yMin) - 1;
        for (int x = x0; x <= x1; x++)
            if (!IsSupport(x, y)) return false;
        return true;
    }

    // 사각형이 걸치는 칸이 모두 실내 빈 칸(지형·OneWay·제외 아님)인지.
    private bool RectClear(Rect rect)
    {
        int x0 = Mathf.FloorToInt(rect.xMin + Epsilon), x1 = Mathf.FloorToInt(rect.xMax - Epsilon);
        int y0 = Mathf.FloorToInt(rect.yMin + Epsilon), y1 = Mathf.FloorToInt(rect.yMax - Epsilon);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!TryFlags(x, y, out byte f)) return false;
                if ((f & Inside) == 0 || (f & (Solid | OneWay | Excluded)) != 0) return false;
            }
        return true;
    }

    private static bool Crowded(Rect rect, IReadOnlyList<Rect> occupied)
    {
        var grown = new Rect(rect.x - BodySpacing, rect.y - BodySpacing, rect.width + BodySpacing * 2f, rect.height + BodySpacing * 2f);
        for (int i = 0; i < occupied.Count; i++)
            if (grown.Overlaps(occupied[i])) return true;
        return false;
    }

    private static float SqrDistance(Rect rect, Vector2 point)
    {
        float dx = Mathf.Max(rect.xMin - point.x, 0f, point.x - rect.xMax);
        float dy = Mathf.Max(rect.yMin - point.y, 0f, point.y - rect.yMax);
        return dx * dx + dy * dy;
    }

    private bool TryFlags(int x, int y, out byte flags)
    {
        int ix = x - gx, iy = y - gy;
        if (ix < 0 || iy < 0 || ix >= gw || iy >= gh) { flags = 0; return false; }
        flags = grid[iy * gw + ix];
        return true;
    }

    private void Mark(int x, int y, byte flag)
    {
        int ix = x - gx, iy = y - gy;
        if (ix < 0 || iy < 0 || ix >= gw || iy >= gh) return;
        grid[iy * gw + ix] |= flag;
    }

    private void MarkRect(Rect rect, byte flag)
    {
        int x0 = Mathf.FloorToInt(rect.xMin + Epsilon), x1 = Mathf.FloorToInt(rect.xMax - Epsilon);
        int y0 = Mathf.FloorToInt(rect.yMin + Epsilon), y1 = Mathf.FloorToInt(rect.yMax - Epsilon);
        x0 = Mathf.Max(x0, gx); y0 = Mathf.Max(y0, gy); x1 = Mathf.Min(x1, gx + gw - 1); y1 = Mathf.Min(y1, gy + gh - 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) grid[(y - gy) * gw + (x - gx)] |= flag;
    }

    private void Compute(DungeonSpawnMapInput input)
    {
        var cells = input.cells ?? Array.Empty<WaveMapCell>();
        // 격자 범위 = 셀 외접 ∪ 실내 범위, 둘레 1칸(바깥 채우기의 시작점).
        bool any = false;
        int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
        int pxMin = int.MaxValue, pyMin = int.MaxValue, pxMax = int.MinValue, pyMax = int.MinValue;
        foreach (var cell in cells)
        {
            any = true;
            xMin = Mathf.Min(xMin, cell.position.x); yMin = Mathf.Min(yMin, cell.position.y);
            xMax = Mathf.Max(xMax, cell.position.x); yMax = Mathf.Max(yMax, cell.position.y);
            if (cell.kind != WaveCellKind.Platform) continue;
            pxMin = Mathf.Min(pxMin, cell.position.x); pyMin = Mathf.Min(pyMin, cell.position.y);
            pxMax = Mathf.Max(pxMax, cell.position.x); pyMax = Mathf.Max(pyMax, cell.position.y);
        }
        interior = input.interior;
        if (interior.width <= 0 || interior.height <= 0)
        {
            int wall = DungeonRoomTemplateSO.WallThickness;
            interior = pxMin <= pxMax ? new RectInt(pxMin + wall, pyMin + wall, Mathf.Max(0, pxMax - pxMin + 1 - wall * 2), Mathf.Max(0, pyMax - pyMin + 1 - wall * 2)) : default;
        }
        if (interior.width <= 0 || interior.height <= 0) return;
        if (!any) { xMin = interior.xMin; yMin = interior.yMin; xMax = interior.xMax - 1; yMax = interior.yMax - 1; }
        xMin = Mathf.Min(xMin, interior.xMin) - 1; yMin = Mathf.Min(yMin, interior.yMin) - 1;
        xMax = Mathf.Max(xMax, interior.xMax - 1) + 1; yMax = Mathf.Max(yMax, interior.yMax - 1) + 1;
        long w = (long)xMax - xMin + 1, h = (long)yMax - yMin + 1;
        if (w <= 0 || h <= 0 || w * h > MaxGridCells) return;
        gx = xMin; gy = yMin; gw = (int)w; gh = (int)h;
        grid = new byte[gw * gh];

        foreach (var cell in cells) Mark(cell.position.x, cell.position.y, cell.kind == WaveCellKind.Platform ? Solid : OneWay);
        if (input.solidProps != null) foreach (var rect in input.solidProps) MarkRect(rect, (byte)(Solid | PropSolid));

        // 바깥 채우기: 격자 테두리에서 고체가 아닌 칸으로 이어진 곳은 방 밖이다(L·ㄱ의 오목 바깥, 열린 맵).
        var queue = new int[gw * gh];
        int head = 0, tail = 0;
        for (int ix = 0; ix < gw; ix++) { Seed(ix, 0); Seed(ix, gh - 1); }
        for (int iy = 0; iy < gh; iy++) { Seed(0, iy); Seed(gw - 1, iy); }
        void Seed(int ix, int iy)
        {
            int i = iy * gw + ix;
            if ((grid[i] & (Solid | Outside)) != 0) return;
            grid[i] |= Outside;
            queue[tail++] = i;
        }
        while (head < tail)
        {
            int i = queue[head++], ix = i % gw, iy = i / gw;
            if (ix > 0) Seed(ix - 1, iy);
            if (ix < gw - 1) Seed(ix + 1, iy);
            if (iy > 0) Seed(ix, iy - 1);
            if (iy < gh - 1) Seed(ix, iy + 1);
        }
        int inside = 0;
        for (int y = interior.yMin; y < interior.yMax; y++)
            for (int x = interior.xMin; x < interior.xMax; x++)
            {
                int i = (y - gy) * gw + (x - gx);
                if ((grid[i] & (Outside | Solid)) != 0) continue;
                grid[i] |= Inside;
                inside++;
            }
        // 셀이 닫혀 있지 않아 모두 바깥으로 보이면(셀 없는 합성 맵 등) 실내 범위 전체를 실내로 본다.
        if (inside == 0)
            for (int y = interior.yMin; y < interior.yMax; y++)
                for (int x = interior.xMin; x < interior.xMax; x++)
                {
                    int i = (y - gy) * gw + (x - gx);
                    if ((grid[i] & Solid) == 0) { grid[i] |= Inside; inside++; }
                }
        emptyCells = inside;   // OneWay 칸은 빈 칸으로 센다(밀집도 측정과 같은 정의).
        if (input.exclusions != null) foreach (var rect in input.exclusions) MarkRect(rect, Excluded);

        // 지상 후보: 줄마다 '빈 칸 + 아래 받침' 연속 구간을 찾아 길이(floorRun)를 함께 적는다.
        for (int y = interior.yMin; y < interior.yMax; y++)
        {
            int x = interior.xMin;
            while (x < interior.xMax)
            {
                if (!Stand(x, y)) { x++; continue; }
                int start = x;
                while (x < interior.xMax && Stand(x, y)) x++;
                int run = x - start;
                for (int k = start; k < x; k++)
                {
                    if (IsExcluded(k, y)) continue;
                    AddCandidate(new Vector2(k + .5f, y), false, run, Headroom(k, y), input);
                }
            }
        }
        groundCount = candidates.Count;
        // 공중 후보: 빈 칸 중 바로 아래 AirFloorGap칸이 비어 있는 칸.
        int floorGap = Mathf.CeilToInt(AirFloorGap - Epsilon);
        for (int y = interior.yMin; y < interior.yMax; y++)
            for (int x = interior.xMin; x < interior.xMax; x++)
            {
                if (!IsOpen(x, y) || IsExcluded(x, y)) continue;
                bool gap = true;
                for (int d = 1; d <= floorGap && gap; d++) gap = IsOpen(x, y - d);
                if (!gap) continue;
                AddCandidate(new Vector2(x + .5f, y), true, 0, Headroom(x, y), input);
            }
        airCount = candidates.Count - groundCount;
    }

    private bool Stand(int x, int y) => IsOpen(x, y) && IsSupport(x, y - 1);

    private int Headroom(int x, int y)
    {
        int n = 0;
        while (IsOpen(x, y + n)) n++;
        return n;
    }

    private void AddCandidate(Vector2 feet, bool air, int floorRun, int headroom, DungeonSpawnMapInput input)
    {
        float bonus = 0f;
        if (input.zones != null)
        {
            var probe = feet + new Vector2(0f, .5f);
            foreach (var (rect, kind, weight) in input.zones)
            {
                if (!rect.Contains(probe)) continue;
                bool match = kind == (int)DungeonSpawnZoneKind.Mixed || (air ? kind == (int)DungeonSpawnZoneKind.Air : kind == (int)DungeonSpawnZoneKind.Ground);
                if (match) bonus = Mathf.Max(bonus, Mathf.Max(0f, weight));
            }
        }
        candidates.Add(new DungeonSpawnCandidate(feet, air, 1f + bonus, floorRun, headroom));
    }
}
