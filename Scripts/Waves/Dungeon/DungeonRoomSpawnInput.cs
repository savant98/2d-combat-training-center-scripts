using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 동적 적 구성(4차 DESIGN_V4 §2.8)의 생성 후보 입력을 만든다(순수 계산 + 세운 Prop의 Collider2D.bounds 읽기 — 물리 질의 없음).
/// DungeonManager가 방에 들어온 순간 부른다. 좌표는 모두 방 로컬(= 월드 − Wave 생성 기준점)이다: 셀 좌표가 정수로 남고,
/// 결과 항목의 spawnPosition을 DungeonEncounter가 기준점에 더해 그대로 쓴다(계약의 '방을 세운 자리 기준' 좌표계 — 기준점만 뺀 것).
/// </summary>
public static class DungeonRoomSpawnInput
{
    /// <summary>출입구 앞 생성 금지 폭(칸, 실내 쪽 벽면에서). 관에서 나온 Player 바로 앞에 적이 서지 않게 한다(판단값).</summary>
    public const float DoorFrontWidth = 4f;
    /// <summary>텔레포트 시설 둘레 생성 금지(좌우 칸, 빌더 규칙의 4칸과 같다).</summary>
    public const float TeleportClearance = 4f;

    /// <summary>
    /// 방 하나의 후보 입력을 만든다. stage가 이 방을 세운 단계면 Prop 경계도 넣는다. objective가 있으면 배정된 목표 자리, room.exit가 있으면 출구 자리를 뺀다.
    /// emptyCells = 실내 칸 − 고체 지형 칸 − 고체 Prop이 덮는 칸(적 수 공식의 A).
    /// </summary>
    public static DungeonSpawnMapInput Build(DungeonRoomData room, DungeonRoomStage stage, DungeonObjectiveSO objective, Vector2 playerFeet, float clearance, out int emptyCells)
    {
        var input = new DungeonSpawnMapInput();
        emptyCells = 0;
        if (room == null) return input;
        input.cells = room.cells ?? Array.Empty<WaveMapCell>();
        input.interior = InteriorCells(room.interior);
        input.playerFeet = playerFeet;
        input.playerClearance = Mathf.Max(0f, clearance);
        if (stage != null && stage.Room == room) stage.CollectSpawnBlockers(input.solidProps, input.exclusions);

        // 생성 구역 표식(배치 설정 크기·종류·가중치)
        foreach (var info in room.spawnZones)
            if (DungeonSpawnZone.TryRead(info, Vector2.zero, out var rect, out int kind, out float weight)) input.zones.Add((rect, kind, weight));

        // 출입구 앞 공간(실내 쪽 벽면에서 DoorFrontWidth칸, 바닥부터 출입구 높이 + 1칸)
        foreach (var door in room.doors ?? Array.Empty<DungeonDoorPlacement>())
        {
            if (door == null || !door.Connected) continue;
            float x = door.info.side == DungeonDoorSide.Right ? door.info.wallFaceX - DoorFrontWidth : door.info.wallFaceX;
            input.exclusions.Add(new Rect(x, door.info.floorY, DoorFrontWidth, DungeonDoor.Height + 1));
        }
        // 텔레포트 시설(짝 없는 꺼진 받침 포함)
        foreach (var pad in room.teleports ?? Array.Empty<DungeonTeleportPlacement>())
        {
            if (pad?.info == null) continue;
            var c = pad.info.spawnPosition;
            input.exclusions.Add(new Rect(c.x - TeleportClearance, c.y - 2f, TeleportClearance * 2f, 6f));
        }
        // 배정된 목표 자리(레버 4×6)와 출구 자리(5×6)
        if (objective != null)
            foreach (var slot in room.objectives ?? Array.Empty<DungeonObjectivePlacement>())
                if (slot != null && slot.Assigned && slot.info != null)
                    input.exclusions.Add(DungeonSpawnBlockers.FootprintRect(slot.info, new Vector2Int(4, 6)));
        if (room.HasExit) input.exclusions.Add(DungeonSpawnBlockers.FootprintRect(room.exit, DungeonExitSlot.Size));
        // 시작 좌표(템플릿 맵 층 시작 위치 — 발 기준 3×3)
        input.exclusions.Add(new Rect(room.entry.x - 1.5f, room.entry.y, 3f, 3f));

        emptyCells = EmptyCells(room, input.interior, input.solidProps);
        return input;
    }

    /// <summary>실내 외접 범위(Rect, 칸 경계)를 칸 범위로.</summary>
    public static RectInt InteriorCells(Rect interior)
    {
        int xMin = Mathf.FloorToInt(interior.xMin + .001f), yMin = Mathf.FloorToInt(interior.yMin + .001f);
        int xMax = Mathf.CeilToInt(interior.xMax - .001f), yMax = Mathf.CeilToInt(interior.yMax - .001f);
        return new RectInt(xMin, yMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin));
    }

    /// <summary>빈 칸 수 A = 실내 칸 − 고체 지형 칸 − 고체 Prop이 덮는 칸(칸 가운데가 경계 안). OneWay 칸은 빈 칸으로 센다.</summary>
    public static int EmptyCells(DungeonRoomData room, RectInt interior, List<Rect> solidProps)
    {
        if (room == null || interior.width <= 0 || interior.height <= 0) return 0;
        var solid = new HashSet<Vector2Int>();
        foreach (var cell in room.cells ?? Array.Empty<WaveMapCell>())
            if (cell.kind == WaveCellKind.Platform) solid.Add(cell.position);
        int empty = 0;
        for (int x = interior.xMin; x < interior.xMax; x++)
            for (int y = interior.yMin; y < interior.yMax; y++)
            {
                if (solid.Contains(new Vector2Int(x, y))) continue;
                var center = new Vector2(x + .5f, y + .5f);
                bool covered = false;
                if (solidProps != null)
                    foreach (var rect in solidProps)
                        if (rect.Contains(center)) { covered = true; break; }
                if (!covered) empty++;
            }
        return empty;
    }
}

/// <summary>
/// 세운 Prop 하나가 막는 생성 영역(4차). DungeonRoomStage.CollectSpawnBlockers가 부른다. 물리 질의 없이 Collider2D.bounds·배치 설정·격자 점유만 읽는다.
/// 분류: 크레인(배치 설정 cranePathCount·zipRadius) → 점유 칸 + 경로 쓸기 상자, 발판·파괴형 Prop → 고체 경계,
/// 함정 Prop·맵 층 장치(DungeonProp이 아닌 환경 장치) → 점유 칸·작동 범위·경계 + margin, 상호작용 시설 → 점유 칸. 칸을 차지하지 않는 표시 장치는 뺀다.
/// </summary>
public static class DungeonSpawnBlockers
{
    public const string CranePathCountKey = "cranePathCount";
    public const string CraneSpeedKey = "craneSpeed";
    public const string ZipRadiusKey = "zipRadius";
    /// <summary>경로 크레인 점 오프셋 키(i = 1..7): cranePath{i}X·cranePath{i}Y.</summary>
    public static string CranePathXKey(int i) => "cranePath" + i + "X";
    public static string CranePathYKey(int i) => "cranePath" + i + "Y";
    /// <summary>경로 크레인 쓸기 상자: 매달린 발 ~ 발 + 4칸, 폭 2칸(DESIGN_V4 §2.3 경로 공간 규칙). 값은 DungeonSpawnMapInput의 것과 같다.</summary>
    public const float CraneSweepHeight = DungeonSpawnMapInput.CraneSweepHeight, CraneSweepHalfWidth = DungeonSpawnMapInput.CraneSweepHalfWidth;

    /// <summary>격자 점유 칸(spawnPosition = 점유 사각형 가운데, 짝수 축은 격자 꼭짓점 — 격자 Tool의 WavePrefabVisuals.Cells와 같은 식).</summary>
    public static RectInt FootprintCells(Vector2 point, Vector2Int size)
        => new RectInt(Vector2Int.FloorToInt(point - (Vector2)size * .5f + Vector2.one * .5f), size);

    /// <summary>Prefab의 점유 칸 수(회전 반영, WaveEnvironmentTool이 없으면 fallback).</summary>
    public static Vector2Int Footprint(GameObject prefab, int rotationSteps, Vector2Int fallback)
    {
        var size = prefab != null && prefab.TryGetComponent(out WaveEnvironmentTool tool) ? tool.Footprint : fallback;
        int steps = ((rotationSteps % 4) + 4) % 4;
        return steps % 2 == 1 ? new Vector2Int(size.y, size.x) : size;
    }

    /// <summary>배치 항목의 점유 사각형(방 로컬).</summary>
    public static Rect FootprintRect(WaveSpawnInfo info, Vector2Int fallback)
    {
        var cells = FootprintCells(info.spawnPosition, Footprint(info.prefab, info.rotationSteps, fallback));
        return new Rect(cells.xMin, cells.yMin, cells.width, cells.height);
    }

    public static void Collect(GameObject target, WaveSpawnInfo info, Vector2 origin, List<Rect> solids, List<Rect> exclusions, float margin)
    {
        if (target == null || info == null) return;
        var tool = info.prefab != null ? info.prefab.GetComponent<WaveEnvironmentTool>() : null;
        if (tool != null && !tool.OccupiesCells) return;   // 길 안내 화살표 같은 표시 장치
        var parameters = Parameters(target);
        var foot = FootprintRect(info, Vector2Int.one);
        // 1. 크레인(경로·고정): 점유 칸 + 경로 쓸기 상자. 붙기 반경(zipRadius)은 막지 않는다.
        if (Has(parameters, info.deviceSettings, CranePathCountKey) || Has(parameters, info.deviceSettings, ZipRadiusKey))
        {
            exclusions.Add(foot);
            var path = new List<Vector2>();
            // 구간마다 칸 단위 표본의 '발 ~ 발 + 4칸, 폭 2칸' 상자(템플릿 규칙과 같은 뜻). 대각선 구간을 외접 사각형 하나로 막지 않는다(4차 검토).
            if (TryCranePath(info, parameters, path))
                for (int i = 1; i < path.Count; i++) DungeonSpawnMapInput.AddCraneSweep(exclusions, path[i - 1], path[i]);
            return;
        }
        var prop = target.GetComponent<DungeonProp>();
        // 2. 발판·파괴형: 켜진 Collider 경계를 고체로(파괴형 hurtbox도 몸이 겹치면 안 되므로 넣는다).
        if (prop != null && (prop.Kind == DungeonPropKind.Platform || prop.Kind == DungeonPropKind.Destructible))
        {
            foreach (var collider in target.GetComponentsInChildren<Collider2D>(false))
            {
                if (collider == null || !collider.enabled) continue;
                if (prop.Kind == DungeonPropKind.Platform && collider.isTrigger) continue;
                var b = collider.bounds;
                if (b.size.sqrMagnitude <= 0f) continue;
                solids.Add(new Rect((Vector2)b.min - origin, b.size));
            }
            return;
        }
        // 3. 함정 Prop·맵 층 장치: 점유 칸 ∪ 작동 범위 ∪ 경계, margin칸 넓힘.
        bool hazard = prop != null ? prop.Kind == DungeonPropKind.Hazard : target.GetComponent<WorldEntityBase>() != null || tool != null;
        if (hazard)
        {
            var area = foot;
            if (TryRange(target, tool, info, out var range)) area = Union(area, range);
            foreach (var collider in target.GetComponentsInChildren<Collider2D>(false))
            {
                if (collider == null || !collider.enabled) continue;
                var b = collider.bounds;
                if (b.size.sqrMagnitude <= 0f) continue;
                area = Union(area, new Rect((Vector2)b.min - origin, b.size));
            }
            exclusions.Add(Rect.MinMaxRect(area.xMin - margin, area.yMin - margin, area.xMax + margin, area.yMax + margin));
            return;
        }
        // 4. 상호작용 시설(수리대 등 장식 Prop): 점유 칸만.
        if (target.GetComponentInChildren<IFacilityInteractTarget>(true) != null) exclusions.Add(foot);
    }

    /// <summary>
    /// 경로 크레인의 매달린 발 위치 목록(방 로컬): 점 0 = 집 = 점유 2×4의 (x + 1, y), 점 i = 집 + (cranePath{i}X, cranePath{i}Y).
    /// cranePathCount가 없으면 false(고정 크레인).
    /// </summary>
    public static bool TryCranePath(WaveSpawnInfo info, WaveDeviceParameter[] parameters, List<Vector2> feet)
    {
        feet.Clear();
        if (info == null || !Has(parameters, info.deviceSettings, CranePathCountKey)) return false;
        var cells = FootprintCells(info.spawnPosition, Footprint(info.prefab, info.rotationSteps, new Vector2Int(2, 4)));
        var home = new Vector2(cells.xMin + 1f, cells.yMin);
        int count = Mathf.Clamp(Mathf.RoundToInt(Read(parameters, info.deviceSettings, CranePathCountKey, 2f)), 1, 8);
        feet.Add(home);
        for (int i = 1; i < count; i++)
            feet.Add(home + new Vector2(Read(parameters, info.deviceSettings, CranePathXKey(i), 0f), Read(parameters, info.deviceSettings, CranePathYKey(i), 0f)));
        return true;
    }

    /// <summary>개체(또는 Prefab)의 배치 설정 항목 전부(같은 키는 처음 것).</summary>
    public static WaveDeviceParameter[] Parameters(GameObject target)
    {
        if (target == null) return Array.Empty<WaveDeviceParameter>();
        var result = new List<WaveDeviceParameter>();
        var keys = new HashSet<string>();
        foreach (var device in target.GetComponents<IWaveDeviceSettings>())
        {
            WaveDeviceParameter[] list;
            try { list = device?.Parameters; }
            catch (Exception e) { Debug.LogException(e); continue; }
            if (list == null) continue;
            foreach (var parameter in list) if (keys.Add(parameter.Key)) result.Add(parameter);
        }
        return result.ToArray();
    }

    private static bool Has(WaveDeviceParameter[] parameters, WaveDeviceSetting[] overrides, string key)
    {
        if (WaveDeviceSettings.Has(overrides, key)) return true;
        foreach (var parameter in parameters) if (parameter.Key == key) return true;
        return false;
    }

    private static float Read(WaveDeviceParameter[] parameters, WaveDeviceSetting[] overrides, string key, float fallback)
    {
        foreach (var parameter in parameters) if (parameter.Key == key) return WaveDeviceSettings.Value(overrides, parameter);
        if (overrides != null) foreach (var setting in overrides) if (setting.key == key) return setting.value;
        return fallback;
    }

    // 작동 범위(배치 설정 반영 → 없으면 WaveEnvironmentTool 표시 범위)를 회전해 방 로컬 사각형으로.
    private static bool TryRange(GameObject target, WaveEnvironmentTool tool, WaveSpawnInfo info, out Rect rect)
    {
        rect = default;
        WaveDeviceRange range = default;
        bool found = false;
        foreach (var device in target.GetComponents<IWaveDeviceSettings>())
        {
            try { found = device != null && device.TryGetRange(info.deviceSettings ?? WaveDeviceSettings.None, out range); }
            catch (Exception e) { Debug.LogException(e); found = false; }
            if (found) break;
        }
        if (!found && tool != null && tool.Range != WaveEnvironmentTool.RangeShape.None)
        {
            range = new WaveDeviceRange(tool.Range, tool.RangeCenter, tool.RangeSize, tool.RangeColor);
            found = true;
        }
        if (!found || range.Shape == WaveEnvironmentTool.RangeShape.None) return false;
        Vector2 half = range.Shape == WaveEnvironmentTool.RangeShape.Circle ? new Vector2(range.Size.x, range.Size.x) : range.Size * .5f;
        var rotation = Quaternion.Euler(0f, 0f, (((info.rotationSteps % 4) + 4) % 4) * 90f);
        Vector2 center = info.spawnPosition + (Vector2)(rotation * (Vector3)range.Center);
        Vector2 extent = (Vector2)(rotation * (Vector3)half);
        extent = new Vector2(Mathf.Abs(extent.x), Mathf.Abs(extent.y));
        rect = Rect.MinMaxRect(center.x - extent.x, center.y - extent.y, center.x + extent.x, center.y + extent.y);
        return true;
    }

    private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
}
