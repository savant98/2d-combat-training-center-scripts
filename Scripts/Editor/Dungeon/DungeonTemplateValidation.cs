using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 던전 방 템플릿 검사(편집기 전용, 데이터를 바꾸지 않는다). 격자 편집기의 템플릿 패널과 회귀 도구가 같은 규칙을 쓴다.
/// 출입구 자리·바닥·바깥 벽, 실내 닫힘(파기 전·후), 적 구성의 지형 재정의 금지, 맵 층 배치 규칙, L·ㄱ 빈 모서리, 단위 칸 크기,
/// 적 구성 적의 몸과 맵 층 고체 칸의 겹침, 적 구성 장치와 맵 층 점유 항목(Prop·출입구·장치)·출입구 앞 공간의 겹침을 본다.
/// 2차(DESIGN_V2): 실내 23×11 이상(카메라 확대 없음), 시작 가능 템플릿의 시차 배경, 정적 벽면의 키트, 텔레포트 꼬리표 ↔ 맵 층 텔레포트 시설,
/// 휴식 꼬리표 ↔ 수리대, 상호작용 시설 사용 지점 3.5칸 이상, 텔레포트 도착 지점 ↔ 적 생성 위치 4칸 이상, 크레인 이동 경로 빈 칸·끝과 출입구 3칸 이상,
/// 화물 승강기 위 정지점·통로, 늘어난 컨베이어·레일 칸, 적 구성의 시설·Swarmer 수. 시설은 텔레포트(계약 타입)만 타입으로, 나머지는 컴포넌트 이름으로 찾는다.
/// 3차(DESIGN_V3): 목표 자리 표식(DungeonObjectiveSlot — 맵 층만·바닥 위·출입구 앞 금지·보스/전투 아닌 방 경고), 승강기 정지점 = 기둥에서 3칸 안 출입구의 바닥,
/// 크레인 레일 이동 범위가 출입구 설 자리 x를 덮음(2차의 '크레인 끝 ↔ 출입구 3칸' 규칙은 없앰), 차폐막 스위치·캐터펄트·레버의 상호작용 간격과 바닥 받침,
/// 텔레포트 받침(크기는 Prefab 점유 칸 — 3차 6×4)의 바닥·지형 겹침, 적 구성 없는 휴식 시작 방 경고 완화.
/// 4차(DESIGN_V4 §6 — 빌더·정적 검사기와 같은 뜻): 삭제 시설(승강기·차폐막 스위치·캐터펄트) 오류, 목표 자리 4×6·출구 자리 5×6 뒤 벽 규칙(지형·고체 Prop 겹침,
/// 아랫줄 밑 가운데 2칸 서는 면, 출입구 벽면 3칸, 크레인 경로와 겹침 오류·생성 구역 겹침 경고, 크레인 반경 중심 3.5칸), 출구 자리(시작 전용·1개),
/// 경로 크레인(쓸기 상자 '발 ~ 발 + 4칸, 폭 2칸'의 고체 겹침·경로 점 실내·출입구 앞 통과 오류, 전투 구간 2곳 권장), 고정 크레인 사슬(반경 × 0.9)·서는 면 도달(경고),
/// 크레인 없음(큰 방 오류·작은 방 경고, 예외 NoCrane), 생성 구역(지상/공중 1개 이상·겹침 경고), 수직 여유 비율(경고 — 빌더는 75% 미만 오류), 레이저 예고 0.5초(경고).
/// 3차의 승강기 정지점·크레인 레일 덮기 검사는 지웠다(승강기 삭제·경로 크레인 재작성).
/// 통합 4차 검토 반영(빌더 DungeonTemplateBuilder.InspectV4가 정본 — 같은 상수·같은 칸 정의): 크레인 출입구 통과 ±1.5(경로 표본 0.5칸), 전투 구간 = 넉넉한 바닥 구간(여유 5 · 4칸),
/// 자리 표식 ↔ 출입구(행 바닥 − 1 ~ 바닥 + 3 · 벽면 3칸 · 사용 지점 ↔ 서는 자리 3.5칸), 자리 표식·생성 구역 ↔ 크레인 차지/쓸기 칸, Swarmer 12(오류),
/// 생성 구역 대상 = 적이 나오는 템플릿(IsFightTemplate — 손 배치 적 구성 수와 무관), 생성 구역 ↔ 함정 = 빌더 Trap 종류의 칸 + 위험 범위(점프대 제외).
/// 6차(DESIGN_V6 T16 — 단일 검사기): 이 클래스가 템플릿 규칙의 정본이다. 빌더(DungeonTemplateBuilder)·정적 검사기에만 있던 규칙(도달성 BFS·수직 여유 오류·
/// 함정 묶음/위상/안전 경로·설치면·Prop 겹침·파괴 Prop 수/쌓기·Prop 상한)과 6차 새 규칙(T10 함정 간격·크레인 쓸기 ∩ 함정 위험 + 1칸, T8 적 구성 층 함정 금지,
/// 테마 금지 Prop(Combat 레이저), 도착 자리 표식)을 DungeonTemplateValidation.Rules6.cs에 두고, 빌더 자기 검사도 이 클래스를 부른다.
/// 7차(DESIGN_V7): 낙석 구조물 규칙 'v7 낙석'(착지면·낙하 열 OneWay·사슬 칸·낙하 높이·시야 — DungeonTemplateValidation.Rules7.cs), 강체 블록 위험 칸(Danger.DropBlock),
/// 고정 크레인 매달림 = DungeonCranePaths.HangFeet(V11 — 상호작용 간격·사슬 노드·함정 금지 구역·안전 경로·도달성이 같은 점을 쓴다).
/// 심각도: 테마 폴더(Assets/Data/Dungeon/Templates/&lt;테마&gt;)의 실물 템플릿과 빌더(<see cref="Options.Strict"/>)는 빌더와 같은 기준(오류)으로,
/// 그 밖(메모리 사본·회귀 고정물·테마 폴더 밖 에셋)은 예전 편집기 기준으로 본다(빌더에만 있던 규칙은 경고).
/// Prop 규칙(종류·설치면·충돌·허용 회전·길이 키·위험 범위)은 카탈로그(DungeonPropCatalogSO)를 읽는다.
/// </summary>
public static partial class DungeonTemplateValidation
{
    /// <summary>템플릿 패널 안내. 6차부터 도달성 BFS(걷기·1회 점프·점프대·크레인 — 2단 점프로만 닿으면 알림)를 이 검사가 직접 본다.</summary>
    public const string ReachabilityNote = "출입구 도달성(맵 층 시작 좌표에서 걷기·점프·점프대·크레인으로 각 출입구·넉넉한 바닥·목표/출구/도착 자리·크레인에 닿는지)도 이 검사가 봅니다(6차 — 빌더와 같은 BFS, "
        + "예전 사양 검사기 design/check_templates.py와 같은 정의). 테마 폴더의 실물 템플릿은 빌더와 같은 기준(오류)으로 봅니다.";
    /// <summary>적 몸이 덮는 칸을 셀 때 맞닿은 면을 겹침으로 보지 않는 여유(칸). DungeonTemplateBuilder 검사와 같은 값.</summary>
    public const float BodyMargin = .08f;
    /// <summary>출입구 앞 공간의 폭(실내 쪽 칸 수, 판단값). 관에서 나온 Player가 서는 자리라 적 구성 장치를 두지 않는다. DungeonTemplateBuilder와 같은 값.</summary>
    public const int DoorFrontDepth = 2;

    /// <summary>기준 결투장 단위 칸 1개(실내 26×17 + 벽 3칸)의 외접 크기 가로:세로(판단값).</summary>
    public const float ReferenceUnitAspect = 32f / 23f;
    /// <summary>단위 칸 비율이 기준의 이 배수를 넘거나 1/배수보다 작으면 경고(판단값).</summary>
    public const float AspectTolerance = 2f;
    /// <summary>단위 칸 하나의 최소 외접 크기(벽 포함, 판단값). 이보다 작으면 관 출입구·전투 공간이 모자라다.</summary>
    public const int MinUnitWidth = 14, MinUnitHeight = 10;

    public static List<DungeonTemplateIssue> Check(DungeonRoomTemplateSO template) => Check(template, null);
    /// <summary>options가 null이면 에셋 경로로 정한다(테마 폴더 안 = 엄격·그 테마).</summary>
    public static List<DungeonTemplateIssue> Check(DungeonRoomTemplateSO template, Options options)
    {
        options ??= OptionsFor(template);
        var issues = new List<DungeonTemplateIssue>();
        void Error(string message) => issues.Add(new DungeonTemplateIssue(true, message));
        void Warn(string message) => issues.Add(new DungeonTemplateIssue(false, message));
        if (template == null) { Error("템플릿이 없습니다."); return issues; }
        var map = template.MapLayer;
        if (map == null) { Error("맵 층(Wave 1)이 없습니다."); return issues; }
        if (template.cellMap == null || !template.cellMap.enabled) Error("셀 맵이 꺼져 있습니다. 던전 방은 셀 지형만 지원합니다.");
        var cells = template.MapCells ?? Array.Empty<WaveMapCell>();
        var solids = new HashSet<Vector2Int>(cells.Where(c => c.kind == WaveCellKind.Platform).Select(c => c.position));
        if (solids.Count == 0 || !template.TryGetCellBounds(out var box)) { Error("맵 층에 지형(Platform) 칸이 없습니다."); return issues; }
        var size = DungeonShapes.Size(template.shape);
        Vector2Int UnitOf(Vector2 point) => new Vector2Int(
            Mathf.Clamp(Mathf.FloorToInt((point.x - box.xMin) / box.width * size.x), 0, size.x - 1),
            Mathf.Clamp(Mathf.FloorToInt((point.y - box.yMin) / box.height * size.y), 0, size.y - 1));

        // ── 맵 층 배치: 적 없음, 처치 필수 아님 ──
        var mapSpawns = map.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            string item = $"맵 층 #{i + 1} {(spawn?.prefab != null ? spawn.prefab.name : "참조 없음")}";
            if (spawn?.prefab == null) { Error(item + ": Prefab 참조가 비었습니다."); continue; }
            if (spawn.prefab.GetComponent<EnemyController>() != null) Error(item + ": 맵 층에 적이 있습니다. 적은 적 구성 Wave에 놓으세요.");
            else if (spawn.isRequiredToClear) Error(item + ": 맵 층 항목은 '처치 필수 목표'를 끄세요(Prop·출입구·함정은 방 클리어 조건이 아닙니다).");
        }
        if ((map.cellBatches?.Length ?? 0) > 0 || (map.terrainEvents?.Length ?? 0) > 0)
            Warn("맵 층에 시간별 지형 예약이 있습니다. 던전 방은 맵 층의 처음 지형만 씁니다.");

        // ── 출입구 ──
        var doors = new List<DungeonDoorSocket>();
        bool doorsRead = template.TryReadDoors(doors, out string doorError);
        if (!doorsRead) Error("출입구: " + doorError);
        else if (doors.Count == 0) Error("출입구가 없습니다. 팔레트 '던전' 탭의 출입구를 좌우 바깥 벽에 하나 이상 놓으세요.");
        var carved = new HashSet<Vector2Int>();
        foreach (var door in doors)
        {
            string label = $"출입구 {door.spawnIndex + 1}번({(door.side == DungeonDoorSide.Right ? "오른쪽" : "왼쪽")} 벽 · 단위 {door.unit})";
            var carve = door.carve;
            int notWall = 0;
            for (int x = carve.xMin; x < carve.xMax; x++)
                for (int y = carve.yMin; y < carve.yMax; y++)
                {
                    var p = new Vector2Int(x, y);
                    carved.Add(p);
                    if (!solids.Contains(p)) notWall++;
                }
            if (notWall > 0) Error($"{label}: 파낼 자리 x {carve.xMin}~{carve.xMax - 1}, y {carve.yMin}~{carve.yMax - 1} 중 {notWall}칸이 벽 칸이 아닙니다. 출입구는 벽 3칸 중 안쪽 2칸 자리에 놓아야 합니다.");
            for (int x = carve.xMin; x < carve.xMax; x++)
                if (!solids.Contains(new Vector2Int(x, door.floorY - 1))) { Error($"{label}: 출입구 아래 바닥 ({x}, {door.floorY - 1})이 Platform이 아닙니다."); break; }
            int frontX = door.side == DungeonDoorSide.Right ? door.wallFaceX - 1 : door.wallFaceX;
            if (!solids.Contains(new Vector2Int(frontX, door.floorY - 1)))
                Error($"{label}: 실내 쪽 한 칸 앞 바닥 ({frontX}, {door.floorY - 1})이 Platform이 아닙니다. 관에서 나온 Player가 설 바닥이 없습니다.");
            for (int y = door.floorY; y < door.floorY + DungeonDoor.Height; y++)
                if (solids.Contains(new Vector2Int(frontX, y))) { Warn($"{label}: 실내 쪽 출입구 앞 ({frontX}, {y})이 막혀 있습니다."); break; }
            int outerX = door.side == DungeonDoorSide.Right ? carve.xMax : carve.xMin - 1;
            for (int y = door.floorY; y < door.floorY + DungeonDoor.Height; y++)
                if (!solids.Contains(new Vector2Int(outerX, y))) { Error($"{label}: 바깥 벽 1칸 ({outerX}, {y})이 없어 출입구를 파면 방 공간이 밖으로 샙니다."); break; }
        }
        foreach (var group in doors.GroupBy(d => (d.unit, d.side)).Where(g => g.Count() > 1))
            Warn($"단위 {group.Key.unit}의 {(group.Key.side == DungeonDoorSide.Right ? "오른쪽" : "왼쪽")} 벽에 출입구가 {group.Count()}개 있습니다. 같은 이웃으로 이어지는 출입구는 하나만 둡니다.");

        // ── 실내 닫힘(파기 전·후) ──
        var start = WaveRoomState.PlayerStartAt(template, 0);
        var region = new HashSet<Vector2Int>();
        bool closed = TryRoom(solids, start, region);
        if (!closed) Error($"시작 좌표 ({start.x:0.##}, {start.y:0.##})에서 찾은 실내가 닫혀 있지 않거나 시작 좌표가 벽 안입니다. 맵 층 시작 좌표를 실내에 두고 벽을 닫으세요.");
        else
        {
            if (carved.Count > 0)
            {
                var open = new HashSet<Vector2Int>(solids); open.ExceptWith(carved);
                var openRegion = new HashSet<Vector2Int>();
                if (!TryRoom(open, start, openRegion)) Error("출입구를 모두 판 뒤 실내가 밖으로 샙니다. 출입구 바깥쪽 벽 1칸을 남기세요.");
                else
                    foreach (var door in doors)
                    {
                        var mouth = new Vector2Int(door.side == DungeonDoorSide.Right ? door.carve.xMin : door.carve.xMax - 1, door.floorY);
                        if (!openRegion.Contains(mouth)) Warn($"출입구 {door.spawnIndex + 1}번을 파도 실내와 이어지지 않습니다(실내 쪽이 막혔는지 확인).");
                    }
            }
            // 외접 사각형 안인데 실내 밖인 빈 칸(L·ㄱ 빈 모서리 등)은 카메라가 보므로 고체로 채운다.
            int outside = 0; Vector2Int? example = null;
            for (int x = box.xMin; x < box.xMax; x++)
                for (int y = box.yMin; y < box.yMax; y++)
                {
                    var p = new Vector2Int(x, y);
                    if (solids.Contains(p) || region.Contains(p)) continue;
                    outside++; example ??= p;
                }
            if (outside > 0) Warn($"외접 사각형 안에서 실내 밖인 빈 칸이 {outside}개 있습니다(예: {example}). L·ㄱ의 빈 모서리는 고체 칸으로 채우세요(카메라가 외접 사각형을 봅니다).");
            int strayInterior = region.Count(p => !DungeonShapes.Contains(template.shape, UnitOf(p + Vector2.one * .5f)));
            if (strayInterior > 0) Warn($"모양({DungeonShapes.DisplayName(template.shape)})에 없는 단위 칸 자리에 실내 칸이 {strayInterior}개 있습니다. 빈 단위 칸 쪽은 고체로 채우세요.");
        }

        // ── 단위 칸 크기 ──
        float unitWidth = box.width / (float)size.x, unitHeight = box.height / (float)size.y;
        float aspect = unitWidth / unitHeight / ReferenceUnitAspect;
        if (aspect > AspectTolerance || aspect < 1f / AspectTolerance)
            Warn($"단위 칸 하나가 {unitWidth:0.#}×{unitHeight:0.#}칸으로 기준 결투장(32×23) 비율과 많이 다릅니다. 모양({DungeonShapes.DisplayName(template.shape)})과 지형 크기를 확인하세요.");
        if (unitWidth < MinUnitWidth || unitHeight < MinUnitHeight)
            Warn($"단위 칸 하나가 {unitWidth:0.#}×{unitHeight:0.#}칸으로 작습니다(최소 {MinUnitWidth}×{MinUnitHeight}, 벽 포함).");

        // ── 맵 층 점유(Prop·출입구·장치)와 출입구 앞 공간: 적 구성 장치 겹침 검사에 쓴다 ──
        // 격자 Tool과 같은 규칙: 점유 칸 = WavePrefabVisuals.Cells(spawnPosition + spawnOffset, Footprint(회전)), 칸을 차지하지 않는 표시 장치는 뺀다.
        var mapOffset = WaveRoomState.LayoutAt(template, 0).spawnOffset;
        var mapOccupied = new Dictionary<Vector2Int, string>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || !WavePrefabVisuals.OccupiesCells(spawn.prefab)) continue;
            var area = WavePrefabVisuals.Cells(spawn.spawnPosition + mapOffset, WavePrefabVisuals.Footprint(spawn.prefab, spawn.rotationSteps));
            foreach (var c in area.allPositionsWithin) mapOccupied.TryAdd(c, $"#{i + 1} {spawn.prefab.name}");
        }
        var doorFront = new Dictionary<Vector2Int, int>();
        foreach (var door in doors)
        {
            // WaveEnvironmentTool이 없는 출입구도 파낼 칸을 점유로 본다.
            foreach (var c in door.carve.allPositionsWithin) mapOccupied.TryAdd(c, $"#{door.spawnIndex + 1} 출입구");
            int first = door.side == DungeonDoorSide.Right ? door.wallFaceX - DoorFrontDepth : door.wallFaceX;
            for (int x = first; x < first + DoorFrontDepth; x++)
                for (int y = door.floorY; y < door.floorY + DungeonDoor.Height; y++) doorFront.TryAdd(new Vector2Int(x, y), door.spawnIndex + 1);
        }
        // 고체 Prop 칸(컨테이너·폐자재 더미·관제 책상 등 Platform 레이어 BoxCollider2D, 클리어 뒤 열리는 셔터 제외).
        // 적 구성 Wave와 맵 층은 Wave가 달라 같은 칸 검사로 잡히지 않으므로 적 몸과 따로 비교한다(사양 빌더 plan.Blocked와 같은 규칙).
        var propSolidCells = new Dictionary<Vector2Int, string>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || NotSolidAtStart(WavePrefabVisuals.DungeonFacilityName(spawn.prefab))) continue;
            var cellsOfProp = new HashSet<Vector2Int>();
            SolidCells(spawn.prefab, spawn.spawnPosition + mapOffset, spawn.rotationSteps, cellsOfProp);
            foreach (var c in cellsOfProp) propSolidCells.TryAdd(c, $"#{i + 1} {spawn.prefab.name}");
        }
        // 적 몸 칸은 외접 사각형보다 한 칸 넓은 범위까지만 센다(그 밖은 어차피 실내 밖).
        var bodyLimit = new RectInt(box.xMin - 1, box.yMin - 1, box.width + 2, box.height + 2);
        var body = new List<Vector2Int>();

        // ── 적 구성 ──
        int encounterCount = template.EncounterCount;
        // 4차: 생성 구역 표식이 있으면 동적 구성(Dynamic) 전투 방을 뜻한다 — 적 구성 Wave가 없거나 비어도 동적 Wave에서는 적이 나온다.
        bool zoned = mapSpawns.Any(s => WavePrefabVisuals.IsDungeonSpawnZone(s?.prefab));
        if ((template.encounters?.Length ?? 0) != encounterCount) Warn($"적 구성 정보 {template.encounters?.Length ?? 0}개가 적 구성 Wave {encounterCount}개와 다릅니다(템플릿을 격자 편집기로 열면 맞춥니다).");
        // 통합 4차 검토 반영: 손 배치 적 구성이 없는 일반 템플릿은 동적 구성(Dynamic) 전용 전투 방이거나(빌더 FightRoom — 생성 구역은 CheckZones가 본다)
        // 시작 전용·휴식 시작 방이라 경고하지 않는다(빌더·정적 검사기도 안내만 남긴다). 실행 쪽 목표 배치도 동적 구성 방을 후보로 본다(DungeonObjectiveRoomPicker — HasFight).
        // 손 배치 Wave에 이 템플릿을 넣었는지는 Wave 설정의 목표·후보 검사(DungeonTemplateMeta.FightsIn)가 본다. 보스 방은 손 배치 보스 구성이 꼭 있어야 한다.
        if (encounterCount == 0 && template.role == DungeonRoomRole.Boss) Error("적 구성이 없어 이 템플릿을 쓸 수 없습니다. '적 구성 추가'로 만드세요.");
        else if (template.encounters != null && template.encounters.Any(e => e != null) && template.encounters.Where(e => e != null).All(e => e.weight <= 0)) Warn("모든 적 구성의 가중치가 0입니다.");
        if (template.role == DungeonRoomRole.Boss && template.allowAsStart) Warn("보스 방 템플릿이 '시작 방으로 쓸 수 있음'으로 되어 있습니다.");
        for (int w = 1; w < (template.waves?.Length ?? 0); w++)
        {
            var wave = template.waves[w];
            var info = template.encounters != null && w - 1 < template.encounters.Length ? template.encounters[w - 1] : null;
            string prefix = $"적 구성 {w}({(info != null ? info.name : "이름 없음")})";
            if (wave == null) { Error(prefix + ": Wave가 비었습니다."); continue; }
            if (wave.overrideCellMap) Error(prefix + ": 지형을 재정의합니다. 적 구성은 맵 층 지형을 그대로 써야 합니다(이 Wave에서 칠한 지형을 되돌리세요).");
            if (wave.changeTerrain) Error(prefix + ": 'Wave 시작 시 지형 변경'이 켜져 있습니다.");
            if ((wave.cellBatches?.Length ?? 0) > 0) Error(prefix + $": 시간별 생성 발판 {wave.cellBatches.Length}묶음이 있습니다. 적 구성은 지형을 바꾸지 않습니다.");
            if ((wave.terrainEvents?.Length ?? 0) > 0) Error(prefix + $": 시간별 지형 예약 {wave.terrainEvents.Length}개가 있습니다. 적 구성은 지형을 바꾸지 않습니다.");
            var spawns = wave.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
            var offset = WaveRoomState.LayoutAt(template, w).spawnOffset;
            int required = 0;
            for (int s = 0; s < spawns.Length; s++)
            {
                var spawn = spawns[s];
                if (spawn?.prefab == null) { Error(prefix + ": Prefab 참조가 빈 항목이 있습니다."); continue; }
                if (spawn.prefab.GetComponent<DungeonDoor>() != null) Error(prefix + ": 출입구는 맵 층에만 놓습니다.");
                if (spawn.isRequiredToClear && spawn.prefab.GetComponent<EnemyController>() != null) required++;
                if (spawn.keepAfterWave) Warn(prefix + $": {spawn.prefab.name}의 'Wave 종료 후 유지'는 던전 방에서 쓰이지 않습니다.");
                string item = $"{prefix} #{s + 1} {spawn.prefab.name}";
                if (spawn.prefab.GetComponent<EnemyController>() != null)
                {
                    // 적 몸: 격자 Tool이 놓은 원점에 Prefab의 실제 충돌 범위(Collider, 없으면 Renderer)를 더한 칸들.
                    bool beyond = EnemyBodyCells(spawn.prefab, spawn.spawnPosition + offset, bodyLimit, body, out var shape);
                    int hit = body.FindIndex(solids.Contains);
                    int propHit = hit >= 0 ? -1 : body.FindIndex(propSolidCells.ContainsKey);
                    if (hit >= 0)
                        Error($"{item}: 몸({shape.size.x:0.##}×{shape.size.y:0.##})이 맵 층 고체 칸 ({body[hit].x}, {body[hit].y})과 겹칩니다. 벽에 묻힌 적은 처치할 수 없어 방이 잠긴 채로 남습니다.");
                    else if (propHit >= 0)
                        Error($"{item}: 몸이 맵 층 고체 Prop {propSolidCells[body[propHit]]}의 칸 ({body[propHit].x}, {body[propHit].y})과 겹칩니다. Prop 속에 생긴 적은 처치할 수 없어 방이 잠긴 채로 남습니다.");
                    else if (beyond || (closed && body.Any(c => !region.Contains(c))))
                        Error($"{item}: 몸이 실내 밖으로 나갑니다. 실내 빈 칸 안에 놓으세요.");
                    int frontHit = body.FindIndex(doorFront.ContainsKey);
                    if (frontHit >= 0)
                        Error($"{item}: 출입구 {doorFront[body[frontHit]]}번 앞 공간 ({body[frontHit].x}, {body[frontHit].y})에 생성됩니다. 관에서 나온 Player와 겹쳐 바로 맞습니다.");
                }
                else if (WavePrefabVisuals.IsEnvironmentTool(spawn.prefab) && WavePrefabVisuals.OccupiesCells(spawn.prefab))
                {
                    // 적 구성 장치(함정 등)와 맵 층 점유 항목의 같은 칸: Wave가 달라 WaveAuthoring.Validate의 같은 칸 검사가 보지 못한다.
                    var area = WavePrefabVisuals.Cells(spawn.spawnPosition + offset, WavePrefabVisuals.Footprint(spawn.prefab, spawn.rotationSteps));
                    Vector2Int? shared = null, front = null;
                    foreach (var c in area.allPositionsWithin)
                    {
                        if (shared == null && mapOccupied.ContainsKey(c)) shared = c;
                        if (front == null && doorFront.ContainsKey(c)) front = c;
                    }
                    if (shared is Vector2Int sc) Error($"{item}: 맵 층 {mapOccupied[sc]}와 같은 칸 ({sc.x}, {sc.y})을 차지합니다. 실행 때 두 항목이 겹칩니다.");
                    if (front is Vector2Int fc) Error($"{item}: 출입구 {doorFront[fc]}번 앞 공간 ({fc.x}, {fc.y})을 차지합니다. 관에서 나온 Player가 바로 맞거나 막힙니다.");
                }
            }
            if (required == 0) Warn(prefix + ": 처치 필수 적이 없어 방에 들어가자마자 클리어됩니다" + (zoned ? "(동적 구성 Wave에서는 생성 구역으로 적을 짜므로 손 배치 Wave에 쓸 때만 해당)." : "."));
        }
        CheckV2(template, issues, solids, closed ? region : null, box, doors, options);
        // 6차: 빌더·정적 검사기에만 있던 규칙과 새 규칙(DungeonTemplateValidation.Rules6.cs).
        CheckRules6(template, issues, solids, closed ? region : null, doors, options);
        return issues;
    }

    // ───────────────────────────── 2차·4차 검사(DESIGN_V2·V4) ─────────────────────────────
    /// <summary>
    /// 최소 실내(23×11). 3차까지는 'RoomViewRules: Confiner = 실내 + (2, 3)이 기본 시야 24.9×14를 덮는 크기'가 근거였다. 4차 카메라(던전 Wave는 연결 공간 + 사방 3칸, DESIGN_V4 §2.7)
    /// 뒤에는 더 작은 방도 늘림 없이 들어가지만, 작은 방 하한·전투 공간 기준으로 값은 그대로 둔다(템플릿 규칙 v4-6).
    /// </summary>
    public const int MinInteriorWidth = 23, MinInteriorHeight = 11;
    /// <summary>상호작용 시설(텔레포트·수리대·목표/출구 자리)과 크레인 붙기 반경 중심 사이 최소 거리. 상호작용은 가까운 Collider 하나만 잡는다.</summary>
    public const float InteractSpacing = 3.5f;
    /// <summary>텔레포트 도착 지점과 적 구성 적 생성 위치(몸 중심) 사이 최소 거리. 클리어 안 한 방에 도착하자마자 적이 옆에 나오지 않게 한다.</summary>
    public const float TeleportEnemyClearance = 4f;
    /// <summary>2차 규칙의 크레인 끝 ↔ 출입구 최소 거리. 3차부터 검사에 쓰지 않는다(외부 참조 호환용으로만 남김).</summary>
    public const float CraneDoorClearance = 3f;
    /// <summary>적 구성 하나의 Swarmer 상한(풀 예열 12/Prefab — 4차 방당 4묶음). DungeonTemplateBuilder·check_templates.py SWARMER_LIMIT과 같은 값(넘으면 오류).</summary>
    public const int SwarmerLimit = 12;
    /// <summary>경로 검사에 쓰는 Player 몸(판단값): 반폭 0.4, 높이 1.51(몸통 중심 0.755 — DungeonTeleporter.RecessPoint와 같은 기준).</summary>
    public const float PlayerHalfWidth = .4f, PlayerHeight = 1.51f;
    /// <summary>시설 배치별 설정 키(PropCraneRail·PropConveyor와 같은 문자열). 4차 크레인 키는 DungeonCranePaths에 있다.</summary>
    public const string CraneRailLengthKey = "railLength", ConveyorLengthKey = "conveyorLength";
    /// <summary>4차 레이저 사출기 예고선 키(traps4 — 공용 LaserEmitterCell 배치 설정)와 던전 묶음 하한(템플릿 규칙 v4-5: 예고 ≥ 0.5초).</summary>
    public const string LaserWarningKey = "laserWarningTime";
    public const float LaserWarningMin = .5f;
    /// <summary>수직 여유(템플릿 규칙 v4-1): 서는 칸 위 빈 칸 기준, 통과 비율(이 미만은 빌더 오류), 넉넉 비율(이 미만은 경고).</summary>
    public const int ClearanceCells = 5;
    public const float ClearancePass = .75f, ClearanceComfort = .80f;
    /// <summary>
    /// 경로 크레인이 지나야 하는 출입구 앞: 경로 표본(0.5칸)의 발이 서는 자리 x ± 이 값, 바닥 ~ 바닥 + CraneDoorRise칸(규칙 v4-3 '서는 자리 위 3칸 안').
    /// 통합 4차 검토 반영: 빌더(DungeonTemplateBuilder 1110행 `≤ 1.5`)·정적 검사기와 같은 값 — 빌더가 정본이다.
    /// </summary>
    public const float CraneDoorReach = 1.5f;
    public const int CraneDoorRise = 3;
    /// <summary>경로 표본 간격(칸) — 빌더 PolylineSamples·check_templates.py polyline_samples와 같은 0.5칸.</summary>
    public const float CranePathSampleStep = .5f;
    /// <summary>'넉넉한 바닥 구간'으로 볼 이어진 서는 칸 수(수직 여유 ≥ ClearanceCells) — 빌더 FloorSegment와 같은 4칸. 경로 크레인의 '전투 구간 통과'를 센다.</summary>
    public const int FloorSegmentCells = 4;
    /// <summary>고정 크레인 사슬 간격 상한 비율(붙기 반경 × 0.9, 규칙 v4-3).</summary>
    public const float CraneChainRatio = .9f;
    // 텔레포트 도착 몸통 = 루트 + (0, −0.5 + 0.755). 바뀌면 같이 고친다.
    const float TeleporterBodyLift = -DungeonTeleporter.StandOffsetY + .755f;
    const string Hook = DungeonCranePaths.PathComponent, Anchor = DungeonCranePaths.AnchorComponent, Rail = "PropCraneRail", Repair = "PropRepairStation",
        Shutter = "PropShutterGate", Conveyor = "PropConveyor", Teleporter = nameof(DungeonTeleporter);
    // 3·4차 시설(컴포넌트 이름 — WavePrefabVisuals.DungeonFacilityComponents와 같은 문자열)과 자리 표식 종류.
    const string Lever = "PropObjectiveLever", ExitGate = "PropExitGate", Slot = nameof(DungeonObjectiveSlot), ExitSlot = nameof(DungeonExitSlot), ArrivalSlot = WavePrefabVisuals.ArrivalSlotComponent;
    /// <summary>목표·출구 자리 표식과 출입구 실내 벽면 사이 최소 가로 거리(칸). 관에서 나온 Player가 서는 자리·관 통과 판정을 피한다(판단값).</summary>
    public const int ObjectiveDoorClearance = 3;
    // 처음에는 고체가 아닌 Prop 벽: 클리어 뒤 열리는 셔터(4차: 차폐막 스위치는 삭제).
    static bool NotSolidAtStart(string kind) => kind == Shutter;
    /// <summary>출입구 설 자리(DungeonDoor.StandPoint)의 실내 벽면에서 안쪽 거리: 기준점(벽 2칸 가운데) − 바깥 방향 × 2.2.</summary>
    public const float DoorStandInset = 2.2f - DungeonDoor.Depth * .5f;
    /// <summary>출입구 설 자리 x(템플릿 좌표, DungeonDoor.StandPoint와 같은 계산).</summary>
    public static float DoorStandX(DungeonDoorSocket door) => door.side == DungeonDoorSide.Right ? door.wallFaceX - DoorStandInset : door.wallFaceX + DoorStandInset;
    // 상호작용 지점: Prefab 안 FacilityInteractPoint(트리거 상자 중심)마다 하나. 없으면 점유 칸 바닥에 선 몸 중심.
    static List<(string what, Vector2 point)> InteractPoints(GameObject prefab, Vector2 root, Vector2Int footprint)
    {
        var result = new List<(string, Vector2)>();
        var toRoot = prefab.transform.worldToLocalMatrix;
        foreach (var point in prefab.GetComponentsInChildren<FacilityInteractPoint>(true))
        {
            var world = point.TryGetComponent<BoxCollider2D>(out var area) ? point.transform.TransformPoint(area.offset) : point.transform.position;
            result.Add((point.gameObject == prefab ? "" : " " + point.name, root + (Vector2)toRoot.MultiplyPoint3x4(world)));
        }
        if (result.Count == 0) result.Add(("", new Vector2(root.x, root.y - footprint.y * .5f + .75f)));
        return result;
    }
    // 삭제한 시설의 한글 이름.
    static string DeletedLabel(string component) => component == "PropCargoLift" ? "화물 승강기" : component == "PropBarrierSwitch" ? "차폐막 스위치" : component == "PropCatapult" ? "사출 캐터펄트" : component;

    private sealed class Facility
    {
        public int index;
        public string kind;
        public WaveSpawnInfo spawn;
        public Vector2 root;
        public Vector2Int footprint;
        public string label;
    }
    // 생성 구역(맵 층 표식 하나).
    private sealed class Zone
    {
        public int index;
        public Rect rect;
        public int kind;
        public string label;
    }

    private static void CheckV2(DungeonRoomTemplateSO template, List<DungeonTemplateIssue> issues, HashSet<Vector2Int> solids, HashSet<Vector2Int> region, RectInt box, List<DungeonDoorSocket> doors, Options options)
    {
        void Error(string message) => issues.Add(new DungeonTemplateIssue(true, message));
        void Warn(string message) => issues.Add(new DungeonTemplateIssue(false, message));
        // 6차: 빌더가 오류로 보던 규칙은 엄격(테마 폴더 실물·빌더)이면 오류, 아니면 예전처럼 경고.
        bool strict = options != null && options.Strict;
        void Firm(string message) => issues.Add(new DungeonTemplateIssue(strict, message));
        void Complete(string message) => issues.Add(new DungeonTemplateIssue(strict, message, "완성도", true, -1, -1, null, null));
        var mapSpawns = template.MapLayer?.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
        var mapOffset = WaveRoomState.LayoutAt(template, 0).spawnOffset;
        int wall = DungeonRoomTemplateSO.WallThickness;

        // ── 실내 크기: 시작 좌표에서 찾은 닫힌 실내의 외접 크기. 닫히지 않았으면 셀 외접 − 벽 두께. ──
        RectInt interior;
        if (region != null && region.Count > 0)
        {
            int xMin = region.Min(c => c.x), yMin = region.Min(c => c.y), xMax = region.Max(c => c.x), yMax = region.Max(c => c.y);
            interior = new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
        }
        else interior = new RectInt(box.xMin + wall, box.yMin + wall, Mathf.Max(0, box.width - 2 * wall), Mathf.Max(0, box.height - 2 * wall));
        if (interior.width < MinInteriorWidth || interior.height < MinInteriorHeight)
            Error($"실내 {interior.width}×{interior.height}칸이 최소 크기 {MinInteriorWidth}×{MinInteriorHeight}보다 작습니다"
                + (template.sizeClass == DungeonRoomSize.Small ? "(작은 방 하한)" : "") + ". 전투 공간이 모자라고 작은 화면 비율에서 카메라가 확대될 수 있습니다.");

        // ── 배경·시작 방 ──
        var backdrop = template.ResolvedBackdrop;
        if (backdrop == DungeonBackdropMode.Wall && template.wallKit == DungeonWallKit.None)
            Firm("배경이 정적 벽면인데 벽면 키트가 '없음'입니다. 실행 때 시차 배경으로 남습니다(배관·환풍 덕트·전선 트렁크·설비 벽면 중 고르세요).");
        if (template.allowAsStart && template.role != DungeonRoomRole.Boss)
        {
            if (template.HasTag(DungeonRoomTags.NoStart)) Firm("'시작 방으로 쓸 수 있음'과 꼬리표 '시작 불가'가 함께 켜져 있습니다. 생성기는 이 방을 시작 방으로 고르지 않습니다.");
            // 적 구성 없는 휴식 방은 고정 시작 방(startTemplates)으로 쓰는 구성이라 경고하지 않는다(생성 규칙 v3-2).
            else if (template.HasTag(DungeonRoomTags.Rest) && template.EncounterCount > 0)
                Warn("휴식 방(꼬리표 '휴식')은 일반 시작 방 후보에서 빠집니다. '시작 방으로 쓸 수 있음'을 끄세요(던전 설정의 '고정 시작 방'으로는 쓸 수 있습니다).");
            else if (backdrop != DungeonBackdropMode.Parallax)
                Warn($"시작 방은 관 통과 덮개 없이 바로 보이므로 배경이 시차 배경인 방만 씁니다. 이 방은 배경이 {DungeonTemplateMeta.BackdropName(backdrop)}이라 시작 방 후보에서 빠집니다(배경을 시차 배경으로 두거나 '시작 방으로 쓸 수 있음'을 끄세요).");
        }

        // ── 맵 층 시설 · 4차 삭제 시설 ──
        var facilities = new List<Facility>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null) continue;
            string deleted = WavePrefabVisuals.DeletedFacilityName(spawn.prefab);
            if (deleted != null)
            {
                Error($"맵 층 #{i + 1} {spawn.prefab.name}: 4차에서 삭제한 시설({DeletedLabel(deleted)})입니다. 지우고 크레인(경로·고정)·점프대로 바꾸세요(템플릿 규칙 v4-4).");
                continue;
            }
            string kind = WavePrefabVisuals.DungeonFacilityName(spawn.prefab);
            if (kind == null) continue;
            var facility = new Facility
            {
                index = i, kind = kind, spawn = spawn, root = spawn.spawnPosition + mapOffset,
                footprint = WavePrefabVisuals.Footprint(spawn.prefab, 0), label = $"맵 층 #{i + 1} {spawn.prefab.name}",
            };
            if (kind == Hook || kind == Anchor) facility.footprint = DungeonCranePaths.Footprint(spawn.prefab);
            facilities.Add(facility);
            // 6차: 카탈로그에 있는 시설은 허용 회전 규칙(Rules6 — 엄격이면 오류)이 본다.
            if ((((spawn.rotationSteps % 4) + 4) % 4) != 0 && DungeonPropCatalogSO.Lookup(spawn.prefab) == null)
                Warn($"{facility.label}: 시설은 회전 0°로 둡니다(배치 규약). 회전한 시설의 경로·사용 지점은 회전 없이 검사합니다.");
        }

        // ── 목표·출구·도착 자리 표식(맵 층, 회전 0°). 목표는 전투 방에서, 출구는 시작 방에서, 도착(6차 T11)은 다음 Wave 시작 방에서만 쓰인다 ──
        var slots = new List<Facility>();
        int objectiveSlots = 0, exitSlots = 0, arrivalSlots = 0;
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || !WavePrefabVisuals.IsDungeonSlot(spawn.prefab)) continue;
            bool exit = WavePrefabVisuals.IsDungeonExitSlot(spawn.prefab);
            bool arrival = !exit && WavePrefabVisuals.IsDungeonArrivalSlot(spawn.prefab);
            var slot = new Facility
            {
                index = i, kind = exit ? ExitSlot : arrival ? ArrivalSlot : Slot, spawn = spawn, root = spawn.spawnPosition + mapOffset,
                footprint = WavePrefabVisuals.SlotFootprint(spawn.prefab, 0),
                label = exit ? $"맵 층 #{i + 1} 출구 자리 표식 {++exitSlots}번" : arrival ? $"맵 층 #{i + 1} 도착 자리 표식 {++arrivalSlots}번" : $"맵 층 #{i + 1} 목표 자리 표식 {++objectiveSlots}번",
            };
            slots.Add(slot);
            if ((((spawn.rotationSteps % 4) + 4) % 4) != 0)
            {
                if (DungeonPropCatalogSO.Lookup(spawn.prefab) == null) Warn($"{slot.label}: 자리 표식은 회전 0°로 둡니다(실물은 회전 없이 뒤 벽에 섭니다).");
            }
        }
        if (objectiveSlots > 0)
        {
            // 6차: 빌더는 '전투 후보가 아닌 방(시작 전용·보스·휴식)의 목표 자리'를 오류로 본다 — 엄격이면 오류(완성도 규칙).
            if (template.role == DungeonRoomRole.Boss) Complete($"보스 방의 목표 자리 표식 {objectiveSlots}개는 쓰이지 않습니다(목표는 전투 방에만 놓습니다).");
            // 통합 4차 검토 반영: 적 구성 수가 아니라 빌더와 같은 '시작 전용(출구 자리 있음)' 기준으로 본다 — 동적 구성 전투 방의 목표 자리는 쓰인다.
            else if (exitSlots > 0) Complete($"시작 전용 템플릿(출구 자리 표식 있음)의 목표 자리 표식 {objectiveSlots}개는 쓰이지 않습니다(목표는 전투 방에만 놓습니다 — 빌더·정적 검사기는 오류, 규칙 v4-7).");
            else if (template.HasTag(DungeonRoomTags.Rest)) Complete("휴식 방의 목표 자리 표식은 레버 목표의 '휴식 방 허용'을 켰을 때만 쓰입니다(빌더는 휴식 방 목표 자리를 오류로 봅니다).");
        }
        // 4차 출구 자리(규칙 v4-8): 시작 전용 템플릿만, 1개.
        if (exitSlots > 1) Error($"출구 자리 표식이 {exitSlots}개입니다. 시작 방 출구는 1개만 둡니다(실행 쪽은 첫 표식만 씁니다).");
        if (exitSlots > 0)
        {
            if (template.role == DungeonRoomRole.Boss || !template.allowAsStart)
                Error("출구 자리 표식은 시작 전용 템플릿(시작 방으로 쓸 수 있음 · 보스 방 아님)에만 둡니다. 다른 방의 출구 자리는 쓰이지 않습니다.");
            else if (template.EncounterCount > 0)
                Warn("적 구성이 있는 템플릿에 출구 자리 표식이 있습니다. 시작 방으로 골랐을 때만 쓰입니다(시작 전용 템플릿 권장 — 규칙 v4-8).");
            // 6차: 빌더 규칙 — 시작 전용 템플릿은 '시작 불가'·'휴식' 꼬리표가 없고 배경이 시차 배경이어야 한다.
            if (template.HasTag(DungeonRoomTags.NoStart) || template.HasTag(DungeonRoomTags.Rest))
                Firm("출구 자리가 있는 템플릿은 시작 전용입니다: 꼬리표 '시작 불가'·'휴식'을 두지 않습니다(규칙 v4-8).");
            if (template.ResolvedBackdrop != DungeonBackdropMode.Parallax)
                Firm("시작 전용 템플릿(출구 자리)은 배경이 시차 배경이어야 합니다(시작 방은 덮개 없이 세웁니다 — 규칙 v4-8).");
        }
        // 6차 도착 자리(T11): 다음 Wave 시작 방(고정 시작 방 — 휴식 방이어도 됨)에 1개. 출구 자리와 같은 5×6 뒤 벽 규칙(아래 자리 표식 검사)을 받는다.
        if (arrivalSlots > 1) Firm($"도착 자리 표식이 {arrivalSlots}개입니다. 도착 문은 1개만 둡니다(6차 T11).");
        if (arrivalSlots > 0 && (template.role == DungeonRoomRole.Boss || !template.allowAsStart))
            Firm("도착 자리 표식은 시작 방으로 쓸 수 있는 템플릿(보스 방 아님 — 다음 Wave의 고정 시작 방)에만 둡니다(6차 T11).");
        bool knownLever(Facility f) => DungeonPropCatalogSO.Lookup(f.spawn.prefab) != null;
        foreach (var f in facilities.Where(f => f.kind == Lever && !knownLever(f)))
            Firm($"{f.label}: 목표 레버 실물을 템플릿에 직접 놓았습니다. 직접 놓은 레버는 목표 진행에 들어가지 않습니다. 목표 자리 표식을 놓고 목표 SO가 레버를 세우게 하세요.");
        foreach (var f in facilities.Where(f => f.kind == ExitGate && !knownLever(f)))
            Firm($"{f.label}: 출구 문 실물을 템플릿에 직접 놓았습니다. 직접 놓은 문은 레버 진행을 받지 않습니다. 출구 자리 표식을 놓고 목표 SO(마무리 방식 '시작 방 출구')가 문을 세우게 하세요.");

        // ── 텔레포트 꼬리표 ↔ 맵 층 텔레포트 시설, 휴식 꼬리표 ↔ 수리대 ──
        int pads = facilities.Count(f => f.kind == Teleporter);
        bool teleportTag = template.HasTag(DungeonRoomTags.TeleportHub) || template.HasTag(DungeonRoomTags.TeleportNode);
        if (teleportTag && pads == 0) Error("꼬리표 '텔레포트 허브'·'텔레포트 끝점'이 있는데 맵 층에 텔레포트 시설(DungeonTeleporter)이 없습니다. 생성기가 짝을 짓지 않습니다.");
        if (!teleportTag && pads > 0) Error($"맵 층에 텔레포트 시설 {pads}개가 있는데 꼬리표 '텔레포트 허브'·'텔레포트 끝점'이 없습니다. 짝을 짓지 않아 늘 꺼진 받침으로 섭니다.");
        if (pads > 0 && template.role == DungeonRoomRole.Boss) Warn("보스 방의 텔레포트 시설은 짝을 짓지 않습니다(보스 방 제외 규칙).");
        int repairs = facilities.Count(f => f.kind == Repair);
        if (template.HasTag(DungeonRoomTags.Rest) && repairs == 0) Warn("꼬리표 '휴식'인데 맵 층에 수리대(PropRepairStation)가 없습니다.");
        if (!template.HasTag(DungeonRoomTags.Rest) && repairs > 0) Warn($"수리대 {repairs}개가 있는데 꼬리표 '휴식'이 없습니다. 휴식 방 개수·시작 방 거리 제한을 받지 않습니다.");

        // ── 적 구성: 시설·표식 금지, 삭제 시설, Swarmer 수, 텔레포트 도착 지점 ↔ 적 생성 위치 ──
        var padPoints = facilities.Where(f => f.kind == Teleporter).Select(f => (facility: f, point: f.root + new Vector2(0f, TeleporterBodyLift))).ToList();
        for (int w = 1; w < (template.waves?.Length ?? 0); w++)
        {
            var wave = template.waves[w];
            if (wave == null) continue;
            var info = template.encounters != null && w - 1 < template.encounters.Length ? template.encounters[w - 1] : null;
            string prefix = $"적 구성 {w}({(info != null ? info.name : "이름 없음")})";
            var offset = WaveRoomState.LayoutAt(template, w).spawnOffset;
            var spawns = wave.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
            int swarmers = 0;
            for (int s = 0; s < spawns.Length; s++)
            {
                var spawn = spawns[s];
                if (spawn?.prefab == null) continue;
                string item = $"{prefix} #{s + 1} {spawn.prefab.name}";
                string deleted = WavePrefabVisuals.DeletedFacilityName(spawn.prefab);
                if (deleted != null) { Error($"{item}: 4차에서 삭제한 시설({DeletedLabel(deleted)})입니다. 지우세요(템플릿 규칙 v4-4)."); continue; }
                string kind = WavePrefabVisuals.DungeonFacilityName(spawn.prefab);
                if (WavePrefabVisuals.IsDungeonObjectiveSlot(spawn.prefab)) Error($"{item}: 목표 자리 표식은 맵 층에만 놓습니다(적 구성에 놓은 표식은 생성기가 읽지 않습니다).");
                if (WavePrefabVisuals.IsDungeonExitSlot(spawn.prefab)) Error($"{item}: 출구 자리 표식은 맵 층에만 놓습니다(적 구성에 놓은 표식은 읽지 않습니다).");
                if (WavePrefabVisuals.IsDungeonArrivalSlot(spawn.prefab)) Error($"{item}: 도착 자리 표식은 맵 층에만 놓습니다(적 구성에 놓은 표식은 읽지 않습니다 — 6차 T11).");
                if (WavePrefabVisuals.IsDungeonSpawnZone(spawn.prefab)) Error($"{item}: 생성 구역 표식은 맵 층에만 놓습니다(적 구성에 놓은 표식은 읽지 않습니다).");
                if (kind == Teleporter) Error($"{item}: 텔레포트 시설은 맵 층에만 놓습니다(적 구성으로 나온 시설은 짝을 짓지 않습니다).");
                else if (kind != null) Warn($"{item}: 방 클리어에 반응하는 시설은 맵 층에 놓습니다(적 구성으로 두면 고른 구성에 따라 있었다 없었다 합니다).");
                if (spawn.prefab.GetComponent<EnemyController>() == null) continue;
                if (spawn.prefab.name.IndexOf("Swarmer", StringComparison.OrdinalIgnoreCase) >= 0) swarmers++;
                var center = spawn.spawnPosition + offset + (Vector2)WavePrefabVisuals.LocalBounds(spawn.prefab, true).center;
                foreach (var (pad, point) in padPoints)
                {
                    float distance = Vector2.Distance(center, point);
                    if (distance >= TeleportEnemyClearance) continue;
                    Error($"{item}: {pad.label}의 도착 지점과 {distance:0.#}칸으로 가깝습니다({TeleportEnemyClearance:0.#}칸 이상). 텔레포트로 클리어 안 한 방에 오면 적이 바로 옆에 나옵니다.");
                    break;
                }
            }
            // 통합 4차 검토 반영: 빌더(DungeonTemplateBuilder SwarmerLimit 12 · 오류)와 같은 상한·심각도.
            if (swarmers > SwarmerLimit) Error($"{prefix}: Swarmer {swarmers}마리입니다. 방마다 {SwarmerLimit}마리(4묶음) 이하로 둡니다(풀 예열 상한 — 빌더와 같은 기준).");
        }

        // ── 경로 검사용 칸: 지형 고체 + 고체 Prop(Platform 레이어 BoxCollider2D, 셔터 제외 — 클리어 뒤 열림), 바닥(+ OneWay·Prop 윗면), 점유 칸 ──
        var propSolids = new Dictionary<int, HashSet<Vector2Int>>();
        var propSurfaces = new HashSet<Vector2Int>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || NotSolidAtStart(WavePrefabVisuals.DungeonFacilityName(spawn.prefab))) continue;
            var cells = new HashSet<Vector2Int>();
            SolidCells(spawn.prefab, spawn.spawnPosition + mapOffset, spawn.rotationSteps, cells);
            if (cells.Count > 0) propSolids[i] = cells;
            SolidCells(spawn.prefab, spawn.spawnPosition + mapOffset, spawn.rotationSteps, propSurfaces, OneWayLayer());
        }
        var propSolidAll = new HashSet<Vector2Int>(propSolids.Values.SelectMany(c => c));
        string PropAt(Vector2Int cell)
        {
            foreach (var pair in propSolids) if (pair.Value.Contains(cell)) return mapSpawns[pair.Key]?.prefab != null ? $"#{pair.Key + 1} {mapSpawns[pair.Key].prefab.name}" : $"#{pair.Key + 1}";
            return null;
        }
        var oneWayCells = new HashSet<Vector2Int>((template.MapCells ?? Array.Empty<WaveMapCell>()).Where(c => c.kind == WaveCellKind.OneWayPlatform).Select(c => c.position));
        var floors = new HashSet<Vector2Int>(solids);
        floors.UnionWith(oneWayCells);
        floors.UnionWith(propSolidAll);
        floors.UnionWith(propSurfaces);
        bool Hard(Vector2Int cell) => solids.Contains(cell) || propSolidAll.Contains(cell);
        var occupied = new Dictionary<Vector2Int, int>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || !WavePrefabVisuals.OccupiesCells(spawn.prefab)) continue;
            foreach (var c in WavePrefabVisuals.Cells(spawn.spawnPosition + mapOffset, WavePrefabVisuals.Footprint(spawn.prefab, spawn.rotationSteps)).allPositionsWithin) occupied.TryAdd(c, i);
        }
        foreach (var door in doors) foreach (var c in door.carve.allPositionsWithin) occupied.TryAdd(c, door.spawnIndex);
        string OccupantName(int index) => index >= 0 && index < mapSpawns.Length && mapSpawns[index]?.prefab != null ? $"#{index + 1} {mapSpawns[index].prefab.name}" : $"#{index + 1}";

        // 서는 칸(수직 여유·고정 크레인 도달 기준): 실내 빈 칸 · 그 칸과 위 칸이 플랫폼 아님 · 아래 칸이 받침(지형·OneWay·고체/OneWay Prop).
        var stands = StandCells(region, floors, solids, oneWayCells, propSolidAll, propSurfaces);

        // ── 상호작용 지점: 텔레포트·수리대·직접 놓은 레버/출구 문·목표/출구 자리 표식·크레인 붙기 반경 중심(집 매달림 몸 + 5차 경로 크레인 끝 도크 매달림 몸) ──
        var interact = new List<(Facility owner, string what, Vector2 point)>();
        foreach (var f in facilities)
        {
            if (f.kind == Teleporter) interact.Add((f, "", f.root + new Vector2(0f, TeleporterBodyLift)));
            else if (f.kind == Repair) interact.Add((f, "", f.root));
            else if (f.kind == Lever || f.kind == ExitGate)
                foreach (var (what, point) in InteractPoints(f.spawn.prefab, f.root, f.footprint)) interact.Add((f, what, point));
            else if (f.kind == Hook || f.kind == Anchor)
            {
                // 7차 V11: 고정 크레인은 매달린 발(HangFeet — 몸 중심 = 고리 중심), 경로 크레인은 집 발(HomeFeet).
                interact.Add((f, " 붙기 반경 중심", DungeonCranePaths.HangCenter(DungeonCranePaths.HangFeet(f.spawn.prefab, f.root))));
                // 5차(도크 왕복): 경로 크레인 끝 도크도 붙기·호출 지점이다(빌더·검사기 간격 규칙과 같은 양 끝 도크). 같은 크레인의 두 중심끼리는 아래 간격 판정에서 건너뛴다.
                if (f.kind == Hook)
                {
                    var feet = DungeonCranePaths.Feet(f.spawn.prefab, f.root, f.spawn.deviceSettings);
                    if (feet.Count >= 2) interact.Add((f, " 끝 도크 붙기 반경 중심", DungeonCranePaths.HangCenter(feet[feet.Count - 1])));
                }
            }
        }
        // 자리 표식: 세워질 실물의 사용 지점(패널·문 아래 가운데, y + 0.75). 같은 방에는 목표가 하나만 서므로 목표 표식끼리는 간격을 보지 않는다.
        foreach (var slot in slots)
        {
            var area = WavePrefabVisuals.Cells(slot.root, slot.footprint);
            interact.Add((slot, "", new Vector2(area.xMin + area.width * .5f, area.yMin + .75f)));
        }

        // ── 텔레포트 받침: 점유 칸이 지형 밖이고 맨 아래 줄 바로 밑이 바닥(3차 규칙 그대로) ──
        foreach (var f in facilities.Where(f => f.kind == Teleporter))
        {
            var area = WavePrefabVisuals.Cells(f.root, f.footprint);
            Vector2Int? buried = null;
            int supported = 0;
            for (int x = area.xMin; x < area.xMax; x++)
            {
                for (int y = area.yMin; y < area.yMax && buried == null; y++)
                    if (solids.Contains(new Vector2Int(x, y))) buried = new Vector2Int(x, y);
                if (floors.Contains(new Vector2Int(x, area.yMin - 1))) supported++;
            }
            if (buried is Vector2Int sunk) Error($"{f.label}: 점유 칸 ({sunk.x}, {sunk.y})이 지형 고체 칸과 겹칩니다. 실내 빈 칸, 바닥 바로 위에 놓으세요.");
            if (supported == 0) Error($"{f.label}: 점유 칸 아래 줄(y {area.yMin - 1})에 바닥이 없습니다(공중에 뜸). 바닥 바로 위에 놓으세요.");
            else if (supported < area.width) Warn($"{f.label}: 아래 바닥이 {area.width}칸 중 {supported}칸만 받칩니다(가장자리가 허공에 걸림).");
        }

        // ── 4차: 목표 자리(4×6)·출구 자리(5×6) 뒤 벽 규칙(템플릿 규칙 v4-7·8): 점유 칸이 지형·고체 Prop과 겹치지 않고,
        //    아랫줄 바로 밑 가운데(상호작용 지점 아래)에 2칸 이상 이어진 서는 면이 있어야 한다(뒤 벽 패널이라 가장자리 받침은 보지 않음) ──
        foreach (var slot in slots)
        {
            var area = WavePrefabVisuals.Cells(slot.root, slot.footprint);
            Vector2Int? buried = null, propHit = null;
            foreach (var c in area.allPositionsWithin)
            {
                if (buried == null && solids.Contains(c)) buried = c;
                if (propHit == null && propSolidAll.Contains(c)) propHit = c;
            }
            if (buried is Vector2Int sunk) Error($"{slot.label}: 점유 칸 ({sunk.x}, {sunk.y})이 지형 고체 칸과 겹칩니다. 뒤 벽 실물({slot.footprint.x}×{slot.footprint.y})은 실내 빈 칸에 둡니다.");
            if (propHit is Vector2Int hit) Error($"{slot.label}: 점유 칸 ({hit.x}, {hit.y})이 고체 Prop {PropAt(hit)}과 겹칩니다. 실물이 Prop 속에 묻힙니다.");
            int run = CenterSupport(area, floors);
            if (run < 2) Error($"{slot.label}: 아랫줄 바로 밑(y {area.yMin - 1}) 가운데에 이어진 서는 면이 {run}칸입니다(2칸 이상). 상호작용 지점 아래에 바닥을 두세요.");
        }

        // ── 목표·출구 자리 표식은 출입구 앞에 두지 않는다 — 관에서 나온 Player가 서는 자리(빌더 InspectV4 규칙 7·8과 같은 범위·값):
        //    ① 사용 지점(아래 가운데, y + 0.75) ↔ 출입구 서는 자리(DoorStandX, 바닥 + 0.755) InteractSpacing칸 이상,
        //    ② 행이 출입구 바닥 − 1 ~ 바닥 + 높이(3)와 겹치면 실내 벽면과 ObjectiveDoorClearance칸 이상(벽면 바깥쪽 음수 간격은 다른 검사가 본다) ──
        foreach (var slot in slots)
        {
            var area = WavePrefabVisuals.Cells(slot.root, slot.footprint);
            var use = new Vector2(area.xMin + area.width * .5f, area.yMin + .75f);
            foreach (var door in doors)
            {
                float toStand = Vector2.Distance(use, new Vector2(DoorStandX(door), door.floorY + DungeonCranePaths.BodyLift));
                if (toStand < InteractSpacing)
                    Error($"{slot.label}: 사용 지점이 출입구 {door.spawnIndex + 1}번 서는 자리와 {toStand:0.#}칸입니다({InteractSpacing:0.#}칸 이상 — 관에서 나오자마자 키가 잡히지 않게).");
                bool level = area.yMin <= door.floorY + DungeonDoor.Height && area.yMax - 1 >= door.floorY - 1;
                int gap = door.side == DungeonDoorSide.Right ? door.wallFaceX - area.xMax : area.xMin - door.wallFaceX;
                if (!level || gap < 0 || gap >= ObjectiveDoorClearance) continue;
                Error($"{slot.label}: 출입구 {door.spawnIndex + 1}번 앞({gap}칸)에 있습니다. 자리 표식은 출입구 실내 벽면에서 {ObjectiveDoorClearance}칸 이상 떨어뜨리세요(관에서 나온 Player가 서는 자리).");
            }
        }

        // ── 늘어난 컨베이어·레일 칸(왼쪽 고정, 오른쪽으로 늘어남). 4차: 레일은 장식으로만 남는다. 6차: 격자 Tool도 늘어난 칸을 같은 칸 규칙에 넣는다(WavePrefabVisuals.OccupiedCells) ──
        foreach (var f in facilities.Where(f => f.kind == Conveyor || f.kind == Rail))
        {
            float left = f.root.x - f.footprint.x * .5f;
            int length = Mathf.RoundToInt(DeviceValue(f.spawn, f.kind == Rail ? CraneRailLengthKey : ConveyorLengthKey, f.footprint.x));
            int row = Mathf.FloorToInt(f.root.y), x0 = Mathf.RoundToInt(left);
            for (int x = x0 + f.footprint.x; x < x0 + length; x++)
                if (occupied.TryGetValue(new Vector2Int(x, row), out int other) && other != f.index)
                {
                    Error($"{f.label}: 늘어난 칸 ({x}, {row})에 맵 층 {OccupantName(other)}이 있습니다. 길이 설정으로 늘어난 칸도 차지 칸입니다(6차 — 같은 칸 규칙). 길이를 줄이거나 그 항목을 옮기세요.");
                    break;
                }
            if (f.kind != Rail) continue;
            for (int x = x0; x < x0 + length; x++)
                if (solids.Contains(new Vector2Int(x, row))) { Warn($"{f.label}: 레일이 지형 칸 ({x}, {row})을 지나갑니다(벽 속으로 들어간 레일)."); break; }
        }

        // ── 생성 구역 표식(4차, 규칙 v4-9) ──
        var zones = new List<Zone>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || !DungeonSpawnZone.TryRead(spawn, mapOffset, out var rect, out int zoneKind, out _)) continue;
            zones.Add(new Zone { index = i, rect = rect, kind = zoneKind, label = $"맵 층 #{i + 1} 생성 구역({ZoneKindName(zoneKind)} {rect.width:0}×{rect.height:0})" });
            if ((((spawn.rotationSteps % 4) + 4) % 4) != 0) Warn($"맵 층 #{i + 1} 생성 구역: 회전하지 않고 놓습니다(구역은 놓은 칸에서 오른쪽·위로 넓어집니다).");
        }

        // ── 넉넉한 바닥 구간(빌더 ampleSegs): 수직 여유 ≥ ClearanceCells인 서는 칸이 한 줄에 FloorSegmentCells칸 이상 이어진 구간 — 경로 크레인 '전투 구간 통과'의 기준 ──
        var floorSegments = AmpleSegments(stands, floors, region);

        // ── 4차 크레인(규칙 v4-3): 수·경로 쓸기 상자·실내·출입구 앞 통과·전투 구간(넉넉한 바닥 구간)·고정 크레인 사슬. 돌려주는 값 = 크레인 차지 칸 + 경로 쓸기 칸(빌더 craneCells) ──
        var craneCells = CheckCranes(template, facilities, doors, region, stands, floorSegments, Hard, PropAt, solids, Error, Warn, Firm);

        // ── 자리 표식 ↔ 크레인 차지 칸·경로 쓸기 칸(빌더와 같은 칸 기준 — 생성 구역과의 겹침은 CheckZones가 본다) ──
        foreach (var slot in slots)
        {
            var area = WavePrefabVisuals.Cells(slot.root, slot.footprint);
            foreach (var c in area.allPositionsWithin)
                if (craneCells.TryGetValue(c, out string label)) { Error($"{slot.label}: 크레인 {label}의 차지 칸·경로(쓸기 상자)와 겹칩니다(예 ({c.x}, {c.y})). 자리 표식은 크레인이 Player를 옮기는 길 밖에 두세요."); break; }
        }

        // ── 생성 구역(규칙 v4-9, 빌더와 같은 정의): 적이 나오는 템플릿(IsFightTemplate)은 지상(수직 방은 공중) 구역 1개 이상,
        //    출입구 앞·목표/출구 자리·크레인 칸·함정 칸과 위험 범위·텔레포트와 겹치지 않게 ──
        var slotCells = new HashSet<Vector2Int>(slots.SelectMany(s => CellsOf(WavePrefabVisuals.Cells(s.root, s.footprint))));
        var padCells = new HashSet<Vector2Int>(facilities.Where(f => f.kind == Teleporter).SelectMany(f => CellsOf(WavePrefabVisuals.Cells(f.root, f.footprint))));
        CheckZones(template, zones, doors, craneCells, slotCells, padCells, region, interior, solids, floors, Hard, mapSpawns, mapOffset, Warn, Firm, Complete);

        // ── 수직 여유(규칙 v4-1): 6차부터 빌더와 같은 계산(카탈로그 충돌 — DungeonTemplateValidation.Rules6.cs ClearanceRule)이 본다(엄격이면 75% 미만 오류) ──

        // ── 레이저 예고선(규칙 v4-5): 예고 설정을 내는 레이저는 예고 0.5초 이상. 카탈로그 함정(warningKey)은 Rules6가 본다 — 여기서는 카탈로그 밖 항목만 ──
        for (int w = 0; w < (template.waves?.Length ?? 0); w++)
        {
            var spawns = template.waves[w]?.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
            for (int s = 0; s < spawns.Length; s++)
            {
                var spawn = spawns[s];
                if (spawn?.prefab == null || !string.IsNullOrEmpty(DungeonPropCatalogSO.Lookup(spawn.prefab)?.warningKey)) continue;
                bool known = DungeonCranePaths.TryParameter(spawn.prefab, LaserWarningKey, out _) || WaveDeviceSettings.Has(spawn.deviceSettings, LaserWarningKey);
                if (!known) continue;
                float warning = DeviceValue(spawn, LaserWarningKey, 0f);
                if (warning < LaserWarningMin - .001f)
                    Firm($"{(w == 0 ? "맵 층" : $"적 구성 {w}")} #{s + 1} {spawn.prefab.name}: 레이저 예고선 {warning:0.##}초 — 던전 템플릿은 {LaserWarningMin:0.#}초 이상으로 둡니다(배치 설정 {LaserWarningKey}, 기본 0.6).");
            }
        }

        // ── 상호작용 지점 간격: 시설끼리·자리 표식과 시설·크레인 붙기 반경 중심(자리 표식과는 오류, 다른 시설과는 경고 — 붙기는 보통 상호작용 키 안내가 뜨면 양보) ──
        var reported = new HashSet<(int, int)>();
        for (int i = 0; i < interact.Count; i++)
            for (int j = i + 1; j < interact.Count; j++)
            {
                var (ownerA, whatA, pointA) = interact[i];
                var (ownerB, whatB, pointB) = interact[j];
                bool craneA = ownerA.kind == Hook || ownerA.kind == Anchor, craneB = ownerB.kind == Hook || ownerB.kind == Anchor;
                if (ownerA == ownerB || ownerA.kind == Slot && ownerB.kind == Slot || craneA && craneB) continue;
                // 6차: 출구 자리와 도착 자리는 같은 방에 함께 쓰이지 않는다(W1 시작 방 = 출구 · W2 시작 방 = 도착).
                if ((ownerA.kind == ExitSlot || ownerA.kind == ArrivalSlot) && (ownerB.kind == ExitSlot || ownerB.kind == ArrivalSlot)) continue;
                float distance = Vector2.Distance(pointA, pointB);
                var key = (Mathf.Min(ownerA.index, ownerB.index), Mathf.Max(ownerA.index, ownerB.index));
                if (distance >= InteractSpacing || !reported.Add(key)) continue;
                string message = $"상호작용 시설 {ownerA.label}{whatA}와 {ownerB.label}{whatB}의 사용 지점이 {distance:0.#}칸으로 가깝습니다({InteractSpacing:0.#}칸 이상).";
                bool slotPair = ownerA.kind == Slot || ownerA.kind == ExitSlot || ownerA.kind == ArrivalSlot || ownerB.kind == Slot || ownerB.kind == ExitSlot || ownerB.kind == ArrivalSlot;
                if (craneA || craneB)
                {
                    if (slotPair) Error(message + " 목표·출구 자리는 크레인 붙기 반경 중심과 떨어뜨립니다(규칙 v4-7).");
                    else Warn(message + " 그 시설의 키 안내가 뜨는 동안에는 크레인에 붙지 않습니다.");
                }
                else Error(message + " 상호작용 키가 어느 시설을 잡을지 정해지지 않습니다.");
            }
    }

    // 생성 구역 종류 이름(DungeonSpawnZoneKind 순서).
    static string ZoneKindName(int kind) => kind == (int)DungeonSpawnZoneKind.Ground ? "지상" : kind == (int)DungeonSpawnZoneKind.Air ? "공중" : "혼합";
    // 맞닿은 면을 겹침으로 보지 않도록 사각형을 조금 줄인다.
    static Rect Shrink(Rect rect) => new Rect(rect.x + BodyMargin, rect.y + BodyMargin, Mathf.Max(0f, rect.width - BodyMargin * 2), Mathf.Max(0f, rect.height - BodyMargin * 2));
    // 아랫줄 바로 밑에서 가운데 열(짝수 폭 = 가운데 2열, 홀수 폭 = 가운데 1열)을 포함해 이어진 받침 칸 수. 가운데 열이 받침이 아니면 0.
    static int CenterSupport(RectInt area, HashSet<Vector2Int> floors)
    {
        int y = area.yMin - 1;
        int left = area.xMin + (area.width - 1) / 2, right = area.xMin + area.width / 2;
        for (int x = left; x <= right; x++) if (!floors.Contains(new Vector2Int(x, y))) return 0;
        int run = right - left + 1;
        for (int x = left - 1; x >= area.xMin && floors.Contains(new Vector2Int(x, y)); x--) run++;
        for (int x = right + 1; x < area.xMax && floors.Contains(new Vector2Int(x, y)); x++) run++;
        return run;
    }
    // OneWay Prop 윗면 레이어(TransparentPlatform). 없으면 -1.
    static int OneWayLayer() => LayerMask.NameToLayer("TransparentPlatform");
    // 서는 칸: 실내 빈 칸 c와 c + 위가 플랫폼(지형·OneWay·고체/OneWay Prop)이 아니고, c 아래가 받침이다.
    static List<Vector2Int> StandCells(HashSet<Vector2Int> region, HashSet<Vector2Int> floors, HashSet<Vector2Int> solids, HashSet<Vector2Int> oneWay, HashSet<Vector2Int> props, HashSet<Vector2Int> surfaces)
    {
        var result = new List<Vector2Int>();
        if (region == null) return result;
        bool Platform(Vector2Int c) => solids.Contains(c) || oneWay.Contains(c) || props.Contains(c) || surfaces.Contains(c);
        foreach (var c in region)
        {
            if (Platform(c) || Platform(c + Vector2Int.up) || !floors.Contains(c + Vector2Int.down)) continue;
            result.Add(c);
        }
        result.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        return result;
    }
    // 수직 여유 측정: 서는 칸마다 위로 다음 플랫폼(바닥 집합 = 지형·OneWay·고체/OneWay Prop) 아랫면까지 빈 칸 수.
    private struct ClearanceResult { public int stands, roomy, lowCeiling; public float ratio; public string narrow; }
    static ClearanceResult MeasureClearance(List<Vector2Int> stands, HashSet<Vector2Int> floors, HashSet<Vector2Int> region, RectInt interior)
    {
        var result = new ClearanceResult { stands = stands.Count };
        if (stands.Count == 0) return result;
        var narrowCells = new List<Vector2Int>();
        foreach (var c in stands)
        {
            int gap = 0;
            var p = c;
            while (!floors.Contains(p) && region.Contains(p) && gap < 64) { gap++; p += Vector2Int.up; }
            if (gap >= ClearanceCells) result.roomy++;
            else
            {
                narrowCells.Add(c);
                // 위를 막은 칸이 실내 위쪽 끝 너머(천장 벽)면 최상층 천장 여유가 모자란 칸이다.
                if (p.y >= interior.yMax) result.lowCeiling++;
            }
        }
        result.ratio = result.roomy / (float)result.stands;
        // 좁은 구간: 같은 줄에서 이어진 서는 칸 묶음(최대 6개 표시).
        var segments = new List<string>();
        for (int i = 0; i < narrowCells.Count && segments.Count < 6;)
        {
            int j = i;
            while (j + 1 < narrowCells.Count && narrowCells[j + 1].y == narrowCells[i].y && narrowCells[j + 1].x == narrowCells[j].x + 1) j++;
            segments.Add($"y {narrowCells[i].y}: x {narrowCells[i].x}~{narrowCells[j].x}");
            i = j + 1;
        }
        result.narrow = string.Join(" / ", segments) + (segments.Count == 6 ? " …" : "");
        return result;
    }

    // ───────── 4차 크레인 검사(규칙 v4-3) ─────────
    // 돌려주는 값 = 크레인 칸(칸 → 크레인 이름): 크레인마다 차지 칸(집 발 x − 1 ~ x, 발 ~ 발 + 3) + 경로 크레인의 0.5칸 경로 표본마다 쓸기 칸(발 ~ 발 + 4칸, 폭 2칸).
    // 통합 4차 검토 반영: 빌더 InspectV4의 craneCells와 같은 칸 정의(목표/출구 자리·생성 구역 겹침 검사용 — 맞닿은 면은 겹침이 아니다).
    private static Dictionary<Vector2Int, string> CheckCranes(DungeonRoomTemplateSO template, List<Facility> facilities, List<DungeonDoorSocket> doors, HashSet<Vector2Int> region,
        List<Vector2Int> stands, List<(int y, int x0, int x1)> floorSegments, Func<Vector2Int, bool> hard, Func<Vector2Int, string> propAt, HashSet<Vector2Int> solids, Action<string> error, Action<string> warn, Action<string> firm)
    {
        var craneCells = new Dictionary<Vector2Int, string>();
        var cranes = facilities.Where(f => f.kind == Hook || f.kind == Anchor).ToList();
        // 통합 4차: 꼬리표 NoCrane(열거값)을 쓴 자리가 빌더·검사기 규칙(휴식 방·CombatPistonCorridor만)과 맞는지(빌더는 오류 — 편집기는 경고로 알린다).
        if (DungeonTemplateMeta.TryNoCraneTag(out var noCraneTag) && template.HasTag(noCraneTag))
        {
            if (!template.HasTag(DungeonRoomTags.Rest) && Array.IndexOf(DungeonTemplateMeta.NoCraneAllowedNames, template.name) < 0)
                firm($"NoCrane 꼬리표는 휴식 방·{string.Join("·", DungeonTemplateMeta.NoCraneAllowedNames)}만 씁니다(빌더·정적 검사기는 오류).");
            if (cranes.Count > 0) warn($"NoCrane 꼬리표인데 크레인이 {cranes.Count}개 있습니다.");
        }
        // 수: 템플릿마다 1개 이상(큰 방 오류 · 작은 방 경고), 예외 꼬리표 NoCrane.
        if (cranes.Count == 0)
        {
            if (!DungeonTemplateMeta.HasNoCraneTag(template))
            {
                string message = "크레인(경로·고정)이 없습니다. 템플릿마다 1개 이상 둡니다(템플릿 규칙 v4-3 — 예외 꼬리표 NoCrane: 휴식 방·피스톤 복도만).";
                if (template.sizeClass == DungeonRoomSize.Large) error(message); else warn(message + "(작은 방이라 경고)");
            }
            return craneCells;
        }
        // 몸이 지나는 선(붙기 직선)이 고체(지형·고체 Prop)와 겹치는지: 몸 위·가운데·아래 3줄(±0.5)을 0.2칸 간격으로 본다. OneWay는 무시한다.
        bool LineClear(Vector2 a, Vector2 b)
        {
            float length = Vector2.Distance(a, b);
            int samples = Mathf.Max(1, Mathf.CeilToInt(length / .2f));
            foreach (float lift in new[] { -.5f, 0f, .5f })
                for (int k = 0; k <= samples; k++)
                {
                    var p = Vector2.Lerp(a, b, k / (float)samples) + new Vector2(0f, lift);
                    if (hard(Vector2Int.FloorToInt(p))) return false;
                }
            return true;
        }
        var nodes = new List<(Facility crane, Vector2 center, float radius, bool ground)>();
        foreach (var f in cranes)
        {
            var settings = f.spawn.deviceSettings;
            float radius = DungeonCranePaths.Radius(f.spawn.prefab, settings);
            // 7차 V11: 고정 크레인 사슬 노드 = 매달린 발(HangFeet). 아래 차지 칸은 계속 집 발(HomeFeet) 기준이다.
            var feet = f.kind == Hook ? DungeonCranePaths.Feet(f.spawn.prefab, f.root, settings) : new List<Vector2> { DungeonCranePaths.HangFeet(f.spawn.prefab, f.root) };
            // 크레인 차지 칸(빌더 RectCells(round(집 발 x) − 1, 집 발 y, 2, 4)).
            var home = DungeonCranePaths.HomeFeet(f.spawn.prefab, f.root);
            for (int x = Mathf.RoundToInt(home.x) - 1; x <= Mathf.RoundToInt(home.x); x++)
                for (int y = Mathf.RoundToInt(home.y); y < Mathf.RoundToInt(home.y) + DungeonCranePaths.Size.y; y++) craneCells.TryAdd(new Vector2Int(x, y), f.label);
            if (f.kind == Hook)
            {
                float length = DungeonCranePaths.Length(feet);
                if (length < .5f) { error($"{f.label}: 경로 크레인의 경로 길이가 {length:0.#}칸입니다. 점 1을 집과 다른 곳에 두세요(배치 설정 {DungeonCranePaths.CountKey}·{DungeonCranePaths.PointXKey(1)}·{DungeonCranePaths.PointYKey(1)})."); }
                // 쓸기 상자: 0.5칸 경로 표본(빌더 PolylineSamples)마다 쓸기 칸(빌더 SweepCells — 발 ~ 발 + 4칸, 폭 2칸)이 고체 지형·고체 Prop과 겹치면 안 된다(OneWay 통과 허용).
                // 통합 4차 검토 반영: 표본 간격·칸 경계를 빌더와 같게 맞췄다(전에는 0.25칸 표본 + 0.08 여유라 대각선 구간에서 판정이 갈릴 수 있었다).
                Vector2Int? blocked = null; int blockedSegment = -1;
                for (int i = 0; i + 1 < feet.Count && blocked == null; i++)
                {
                    int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(feet[i], feet[i + 1]) / CranePathSampleStep));
                    for (int k = i == 0 ? 0 : 1; k <= samples && blocked == null; k++)
                        foreach (var cell in SweepCells(Vector2.Lerp(feet[i], feet[i + 1], k / (float)samples)))
                            if (hard(cell)) { blocked = cell; blockedSegment = i; break; }
                }
                if (blocked is Vector2Int c)
                    error($"{f.label}: 경로 크레인 구간 {blockedSegment}→{blockedSegment + 1}의 쓸기 상자(발 ~ 발 + {DungeonCranePaths.SweepHeight:0}칸, 폭 {DungeonCranePaths.SweepWidth:0}칸)가 "
                        + (solids.Contains(c) ? "지형 고체 칸" : $"고체 Prop {propAt(c)}의 칸") + $" ({c.x}, {c.y})과 겹칩니다. 크레인은 지형을 통과해 옮기므로 몸·케이지가 벽 속을 지나갑니다(템플릿 규칙 v4-3).");
                // 같은 자리 점(빌더 오류와 같음 — 길이 0 구간).
                for (int i = 1; i < feet.Count; i++)
                    if (Vector2.Distance(feet[i], feet[i - 1]) < .001f) { error($"{f.label}: 경로 점 {i}가 앞 점과 같은 자리입니다(빌더와 같은 오류)."); break; }
                // 집·끝점·꺾이는 점에 매달린 몸(빌더 BodyCells — 발 기준 폭 1.5 · 높이 1.5칸)이 모두 실내 빈 칸 안이어야 한다(빌더와 같은 범위 — 집 점도 본다).
                if (region != null)
                    for (int i = 0; i < feet.Count; i++)
                    {
                        if (BodyCells(feet[i]).All(region.Contains)) continue;
                        error($"{f.label}: 경로 점 {i}의 발 ({feet[i].x:0.#}, {feet[i].y:0.#})에 매달린 몸(1.5칸)이 실내 밖입니다. 끝점·꺾이는 점은 실내 안에 둡니다.");
                        break;
                    }
                // 경로 표본: 빌더 PolylineSamples와 같은 0.5칸 간격(첫 점 + 구간마다 끝 포함). 출입구 통과·바닥 구간 통과·쓸기 칸을 모두 이 표본으로 센다.
                var samplesOnPath = PathSamples(feet);
                foreach (var p in samplesOnPath)
                    foreach (var cell in SweepCells(p)) craneCells.TryAdd(cell, f.label);
                // 출입구 앞 통과(빌더와 같은 판정): 표본 발이 어느 출입구의 서는 자리 x ± CraneDoorReach(1.5), 바닥 ~ 바닥 + CraneDoorRise 안을 지나야 한다.
                var passed = doors.Where(d => samplesOnPath.Any(p => Mathf.Abs(p.x - DoorStandX(d)) <= CraneDoorReach && p.y >= d.floorY && p.y <= d.floorY + CraneDoorRise)).ToList();
                if (doors.Count > 0 && passed.Count == 0)
                    error($"{f.label}: 경로 크레인이 어느 출입구 앞(서는 자리 x ± {CraneDoorReach:0.#}칸, 바닥 위 {CraneDoorRise}칸 안)도 지나지 않습니다. 출입구에서 바로 탈 수 있게 경로가 출입구 앞을 한 번 이상 지나게 하세요(템플릿 규칙 v4-3).");
                // 전투 구간 통과(빌더와 같은 뜻 — 생성 구역이 아니라 넉넉한 바닥 구간): 표본 발이 구간 x0 − 0.5 ~ x1 + 1.5, 구간 바닥 위 0 ~ ClearanceCells칸을 지나면 그 구간을 지난 것이다.
                // 생성 구역 ↔ 크레인 경로 겹침 금지(CheckZones)와 서로 막지 않는다(구역은 바닥 구간 안에서 경로 쓸기 칸만 피하면 된다).
                int crossed = floorSegments.Count(g => samplesOnPath.Any(p => p.x >= g.x0 - .5f && p.x <= g.x1 + 1.5f && p.y - g.y >= 0f && p.y - g.y <= ClearanceCells));
                if (crossed < 2) warn($"{f.label}: 경로가 지나는 넉넉한 바닥 구간이 {crossed}곳입니다. 전투 구간 사이를 2곳 이상 지나는 경로를 권장합니다(템플릿 규칙 v4-3 — 빌더와 같은 기준).");
                nodes.Add((f, DungeonCranePaths.HangCenter(feet[0]), radius, false));
                if (feet.Count > 1) nodes.Add((f, DungeonCranePaths.HangCenter(feet[feet.Count - 1]), radius, false));
            }
            else nodes.Add((f, DungeonCranePaths.HangCenter(feet[0]), radius, false));
        }

        // ── 붙기 도달(규칙 v4-3): 서는 면 몸 중심(서는 칸 + (0.5, 0.755))에서 반경 안 · 붙기 직선이 고체와 안 겹침.
        //    5차(DESIGN_V5 §1 도크 왕복): 경로 크레인은 집 도크·끝 도크 둘 다 붙기 대상(빈 도크를 고르면 크레인이 그 도크에 즉시 생긴다(6차 T5) — 빌더·검사기 BFS 양방향 간선과 같다).
        //    끝 도크는 반경 안·직선 열린 서는 면이 없으면 오류(빌더·검사기 오류와 같다 — 편집기는 서는 면 전체 기준). ──
        var standBodies = stands.Select(s => new Vector2(s.x + .5f, s.y + DungeonCranePaths.BodyLift)).ToList();
        var groundReach = new bool[nodes.Count];
        var inRadius = new bool[nodes.Count];
        for (int n = 0; n < nodes.Count; n++)
        {
            var node = nodes[n];
            foreach (var body in standBodies)
            {
                if (Vector2.Distance(body, node.center) > node.radius) continue;
                inRadius[n] = true;
                if (LineClear(body, node.center)) { groundReach[n] = true; break; }
            }
            bool endDock = node.crane.kind == Hook && n > 0 && nodes[n - 1].crane == node.crane;
            if (endDock && !groundReach[n])
                error($"{node.crane.label}: 경로 크레인 끝 도크의 붙기 반경({node.radius:0.#}칸) 안에 붙기 직선이 열린 서는 면이 없습니다. 끝 도크에서도 탈 수 있게 도크 0~2칸 안에 서는 자리를 두세요(템플릿 규칙 v4-3 — 5차 도크 왕복).");
        }
        // ── 사슬: 다른 크레인 노드와 반경 × 0.9 안 + 붙기 직선 비어 있음 = 이어짐. 같은 경로 크레인의 집·끝은 타고 이어진다 ──
        var parent = Enumerable.Range(0, nodes.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        void Union(int a, int b) { parent[Find(a)] = Find(b); }
        for (int a = 0; a < nodes.Count; a++)
            for (int b = a + 1; b < nodes.Count; b++)
            {
                if (nodes[a].crane == nodes[b].crane) { Union(a, b); continue; }
                float distance = Vector2.Distance(nodes[a].center, nodes[b].center);
                float limit = Mathf.Min(nodes[a].radius, nodes[b].radius);
                if (distance > limit || !LineClear(nodes[a].center, nodes[b].center)) continue;
                if (distance <= limit * CraneChainRatio) Union(a, b);
                else if (nodes[a].crane.kind == Anchor || nodes[b].crane.kind == Anchor)
                    warn($"크레인 {nodes[a].crane.label}과 {nodes[b].crane.label}의 사슬 간격 {distance:0.#}칸이 붙기 반경 × {CraneChainRatio:0.#}({limit * CraneChainRatio:0.#}칸)를 넘습니다. 연쇄 붙기가 아슬아슬합니다(템플릿 규칙 v4-3).");
            }
        var reachedRoots = new HashSet<int>(Enumerable.Range(0, nodes.Count).Where(n => groundReach[n]).Select(Find));
        var reportedCranes = new HashSet<Facility>();
        for (int n = 0; n < nodes.Count; n++)
        {
            if (reachedRoots.Contains(Find(n)) || !reportedCranes.Add(nodes[n].crane)) continue;
            var crane = nodes[n].crane;
            string kind = crane.kind == Hook ? "경로 크레인" : "고정 크레인";
            warn(inRadius[n]
                ? $"{crane.label}: {kind}의 붙기 반경 안에 서는 면은 있지만 붙기 직선이 모두 지형·고체 Prop에 막힙니다(사슬로도 이어지지 않음 — 템플릿 규칙 v4-3)."
                : $"{crane.label}: {kind}이 서는 면에서 붙기 반경({nodes[n].radius:0.#}칸) 안에 없고, 닿는 다른 크레인과 반경 × {CraneChainRatio:0.#} 안으로 이어지지도 않습니다(템플릿 규칙 v4-3).");
        }
        return craneCells;
    }

    // ───────── 빌더와 같은 칸 계산(통합 4차 검토 반영 — DungeonTemplateBuilder.InspectV4·check_templates.py와 같은 정의, 빌더가 정본) ─────────
    /// <summary>사각형 안의 칸(RectInt.allPositionsWithin을 LINQ·HashSet에 넘기려고 감싼다).</summary>
    static IEnumerable<Vector2Int> CellsOf(RectInt area)
    {
        foreach (var c in area.allPositionsWithin) yield return c;
    }
    /// <summary>경로 표본(빌더 PolylineSamples): 첫 점 + 구간마다 ceil(길이 / 0.5)등분한 점(끝 포함).</summary>
    public static List<Vector2> PathSamples(List<Vector2> corners)
    {
        var list = new List<Vector2>();
        if (corners == null || corners.Count == 0) return list;
        list.Add(corners[0]);
        for (int k = 1; k < corners.Count; k++)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(corners[k - 1], corners[k]) / CranePathSampleStep));
            for (int i = 1; i <= n; i++) list.Add(Vector2.Lerp(corners[k - 1], corners[k], i / (float)n));
        }
        return list;
    }
    /// <summary>매달린 몸 칸(빌더 BodyCells): 발 기준 x ± 0.75, 발 ~ 발 + 1.5칸을 덮는 칸(맞닿은 면 0.02 제외).</summary>
    public static IEnumerable<Vector2Int> BodyCells(Vector2 feet)
    {
        const float e = .02f, size = 1.5f;
        for (int x = Mathf.FloorToInt(feet.x - size * .5f + e); x <= Mathf.FloorToInt(feet.x + size * .5f - e); x++)
            for (int y = Mathf.FloorToInt(feet.y + e); y <= Mathf.FloorToInt(feet.y + size - e); y++) yield return new Vector2Int(x, y);
    }
    /// <summary>쓸기 칸(빌더 SweepCells): 발 기준 x − 1 ~ x + 1, 발 ~ 발 + 4칸을 덮는 칸(맞닿은 면 0.02 제외).</summary>
    public static IEnumerable<Vector2Int> SweepCells(Vector2 feet)
    {
        const float e = .02f;
        for (int x = Mathf.FloorToInt(feet.x - DungeonCranePaths.SweepWidth * .5f + e); x <= Mathf.FloorToInt(feet.x + DungeonCranePaths.SweepWidth * .5f - e); x++)
            for (int y = Mathf.FloorToInt(feet.y + e); y <= Mathf.FloorToInt(feet.y + DungeonCranePaths.SweepHeight - e); y++) yield return new Vector2Int(x, y);
    }
    /// <summary>
    /// 넉넉한 바닥 구간(빌더 ampleSegs): 서는 칸 중 위로 다음 플랫폼까지 빈 칸이 ClearanceCells 이상인 칸을 같은 줄에서 이어 묶고, FloorSegmentCells칸 이상인 구간만 남긴다.
    /// </summary>
    static List<(int y, int x0, int x1)> AmpleSegments(List<Vector2Int> stands, HashSet<Vector2Int> floors, HashSet<Vector2Int> region)
    {
        var segments = new List<(int y, int x0, int x1)>();
        if (region == null) return segments;
        foreach (var c in stands.OrderBy(c => c.y).ThenBy(c => c.x))
        {
            int gap = 0;
            var p = c;
            while (!floors.Contains(p) && region.Contains(p) && gap < 64) { gap++; p += Vector2Int.up; }
            if (gap < ClearanceCells) continue;
            if (segments.Count > 0 && segments[segments.Count - 1].y == c.y && segments[segments.Count - 1].x1 == c.x - 1) segments[segments.Count - 1] = (c.y, segments[segments.Count - 1].x0, c.x);
            else segments.Add((c.y, c.x, c.x));
        }
        segments.RemoveAll(g => g.x1 - g.x0 + 1 < FloorSegmentCells);
        return segments;
    }

    // 함정 = 카탈로그(DungeonPropCatalogSO) 종류 Trap(6차 — 빌더 PropRules·편집기 TrapPrefabNames 이름표를 카탈로그로 합쳤다).
    // 점프대(JumpPadCell)·컨베이어 같은 다른 환경 장치는 함정이 아니다.
    public static string[] TrapPrefabNames => DungeonPropCatalogSO.Active.Names(DungeonPropCatalogSO.Kind.Trap).ToArray();
    public static bool IsTrap(GameObject prefab) => DungeonPropCatalogSO.Lookup(prefab)?.kind == DungeonPropCatalogSO.Kind.Trap;
    // 배치 설정 값: Prefab 항목이 있으면 그 규칙(기본값·범위), 없으면 저장값, 그것도 없으면 fallback(DeviceValue와 같다 — 실행 쪽이 쓰는 값).
    static float SettingOr(WaveSpawnInfo spawn, string key, float fallback) => string.IsNullOrEmpty(key) ? fallback : DeviceValue(spawn, key, fallback);
    /// <summary>함정 차지 칸(빌더 PropCells): 회전한 점유 칸, 길이 설정형(분쇄 투입구 shredderWidth 등)은 회전 0·180°일 때 오른쪽으로 늘어난다.</summary>
    static RectInt TrapArea(WaveSpawnInfo spawn, Vector2 offset) => WavePrefabVisuals.OccupiedCells(spawn.prefab, spawn.spawnPosition + offset, spawn.rotationSteps, spawn.deviceSettings);
    /// <summary>
    /// 함정이 실제로 해치는 칸(빌더 DangerCells와 같은 계산 — 모양은 카탈로그 danger): 차지 칸 + 톱날·접속함 원(Circle)·레이저 빔(Ray)·피스톤 뻗음·증기 기둥(Stroke)·
    /// 용접 빔(WelderRows)·낙석 강체 블록(DropBlock — 7차, 가장 높은 고체 받침까지 · OneWay는 받침이 아님)·투입구 윗줄(TopRow).
    /// blocked = 고체(지형·고체 Prop), interior = 실내 칸(빈 칸 + 실내의 고체), solids = 지형 고체(벽 옆 설치면 판정).
    /// </summary>
    static HashSet<Vector2Int> TrapDanger(WaveSpawnInfo spawn, RectInt area, Func<Vector2Int, bool> blocked, Func<Vector2Int, bool> interior, HashSet<Vector2Int> solids)
        => TrapDanger(spawn, area, blocked, interior, solids.Contains);
    static HashSet<Vector2Int> TrapDanger(WaveSpawnInfo spawn, RectInt area, Func<Vector2Int, bool> blocked, Func<Vector2Int, bool> interior, Func<Vector2Int, bool> terrainSolid)
    {
        var cells = new HashSet<Vector2Int>(CellsOf(area));
        var entry = DungeonPropCatalogSO.Lookup(spawn.prefab);
        if (entry == null || entry.danger == DungeonPropCatalogSO.Danger.None) { cells.RemoveWhere(c => !interior(c)); return cells; }
        var at = new Vector2Int(area.xMin, area.yMin);
        Vector2Int Direction() => DungeonPropCatalogSO.Direction(entry, area, spawn.rotationSteps, terrainSolid);
        void Ray(Vector2Int from, Vector2Int d)
        {
            var c = from;
            for (int i = 0; i < 50; i++)
            {
                c += d;
                if (blocked(c) || !interior(c)) return;
                cells.Add(c);
            }
        }
        void Circle(Vector2 center, float radius)
        {
            for (int x = Mathf.FloorToInt(center.x - radius); x <= Mathf.FloorToInt(center.x + radius); x++)
                for (int y = Mathf.FloorToInt(center.y - radius); y <= Mathf.FloorToInt(center.y + radius); y++)
                    if (Vector2.Distance(new Vector2(x + .5f, y + .5f), center) <= radius + .5f) cells.Add(new Vector2Int(x, y));
        }
        switch (entry.danger)
        {
            case DungeonPropCatalogSO.Danger.Circle:
                Circle(new Vector2(at.x + .5f, at.y + .5f), SettingOr(spawn, entry.dangerKey, entry.dangerBase) + entry.dangerExtra);
                break;
            case DungeonPropCatalogSO.Danger.Ray:
                Ray(at, Direction());
                break;
            case DungeonPropCatalogSO.Danger.Stroke:
            {
                var d = Direction();
                int stroke = Mathf.RoundToInt(SettingOr(spawn, entry.dangerKey, entry.dangerBase));
                for (int k = 1; k <= stroke; k++) cells.Add(at + d * k);
                break;
            }
            case DungeonPropCatalogSO.Danger.WelderRows:
            {
                char side = DungeonPropCatalogSO.MountSide(area, terrainSolid);
                var d = side == 'L' ? Vector2Int.right : Vector2Int.left;
                int sx = side == 'L' ? area.xMax - 1 : area.xMin;
                for (int y = area.yMin; y <= area.yMin + 1; y++) Ray(new Vector2Int(sx, y), d);
                break;
            }
            case DungeonPropCatalogSO.Danger.DropBlock:
                // 7차 V1: 강체 블록 — 열마다 첫 고체 받침을 찾아 가장 높은 받침에서 모든 열이 멈춘다(Rules7 DropBlockCells, OneWay는 지나감 — R1).
                cells.UnionWith(DropBlockCells(area, blocked, interior, out _, out _));
                break;
            case DungeonPropCatalogSO.Danger.TopRow:
                for (int x = area.xMin; x < area.xMax; x++) cells.Add(new Vector2Int(x, area.yMax));
                break;
        }
        cells.RemoveWhere(c => !interior(c));
        return cells;
    }

    // ───────── 4차 생성 구역 검사(규칙 v4-9) ─────────
    // 통합 4차 검토 반영: 빌더 InspectV4 '생성 구역'과 같은 정의로 본다.
    //   · 대상 = 적이 나오는 템플릿(DungeonTemplateMeta.IsFightTemplate — 손 배치 적 구성 수와 무관).
    //   · 지상 구역 = 지상·혼합 구역 안에 서는 칸(실내 빈 칸 · 아래가 받침)이 있음, 공중 구역 = 공중·혼합 구역 안에 바닥 위 2칸 넘게 뜬 빈 칸이 있음.
    //   · 겹침 금지 = 출입구 앞 공간 · 목표/출구 자리 칸 · 크레인 차지/경로 쓸기 칸 · 함정 칸과 위험 범위(빌더 Trap 종류만 — 점프대는 함정이 아님) · 텔레포트 칸.
    // 빌더·정적 검사기는 이 겹침과 '지상(수직 방은 공중) 구역 없음'을 오류로 본다. 여기서는 고체 Prop 칸을 Prefab 충돌체로 세어(빌더는 Prop 규칙표) 한두 칸 해석이 갈릴 수 있어
    // 경고로 알리고 문구에 '빌더 오류'를 적는다(빌더가 이 검사의 오류를 빌드 실패로 가져다 쓰므로 해석이 갈릴 수 있는 규칙은 경고로 둔다 — editor4 원칙).
    private static void CheckZones(DungeonRoomTemplateSO template, List<Zone> zones, List<DungeonDoorSocket> doors, Dictionary<Vector2Int, string> craneCells,
        HashSet<Vector2Int> slotCells, HashSet<Vector2Int> padCells, HashSet<Vector2Int> region, RectInt interior, HashSet<Vector2Int> solids, HashSet<Vector2Int> floors,
        Func<Vector2Int, bool> hard, WaveSpawnInfo[] mapSpawns, Vector2 mapOffset, Action<string> warn, Action<string> firm, Action<string> complete)
    {
        // 6차: 겹침은 빌더 오류 규칙(엄격이면 오류 — firm), 구역 없음은 완성도 규칙(complete)이다. 함정 칸·위험 범위는 카탈로그 정의라 빌더와 같다.
        bool fight = DungeonTemplateMeta.IsFightTemplate(template);
        if (template.role == DungeonRoomRole.Boss && zones.Count > 0) warn($"보스 방의 생성 구역 {zones.Count}개는 쓰이지 않습니다(보스 방은 손 배치 보스 구성을 씁니다).");
        // 출입구 앞 공간(빌더 DoorZone: 실내 쪽 DoorFrontDepth칸 · 바닥 ~ 바닥 + 출입구 높이).
        var doorZone = new Dictionary<Vector2Int, int>();
        foreach (var d in doors)
        {
            int first = d.side == DungeonDoorSide.Right ? d.wallFaceX - DoorFrontDepth : d.wallFaceX;
            for (int x = first; x < first + DoorFrontDepth; x++)
                for (int y = d.floorY; y < d.floorY + DungeonDoor.Height; y++) doorZone.TryAdd(new Vector2Int(x, y), d.spawnIndex + 1);
        }
        // 함정 칸 + 위험 범위(빌더 DangerCells). 실내 = 실내 빈 칸 + 실내 외접 사각형 안의 고체(빌더 plan.Interior와 같은 뜻).
        bool InteriorCell(Vector2Int c) => region != null ? region.Contains(c) || (hard(c) && interior.Contains(c)) : interior.Contains(c);
        var traps = new List<(string name, HashSet<Vector2Int> cells)>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || !IsTrap(spawn.prefab)) continue;
            traps.Add(($"#{i + 1} {spawn.prefab.name}", TrapDanger(spawn, TrapArea(spawn, mapOffset), hard, InteriorCell, solids)));
        }
        bool groundOk = false, airOk = false;
        foreach (var zone in zones)
        {
            int x0 = Mathf.RoundToInt(zone.rect.xMin), y0 = Mathf.RoundToInt(zone.rect.yMin);
            int w = Mathf.Max(1, Mathf.RoundToInt(zone.rect.width)), h = Mathf.Max(1, Mathf.RoundToInt(zone.rect.height));
            var cells = new List<Vector2Int>();
            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++) cells.Add(new Vector2Int(x, y));
            // 겹침: 첫 칸 하나만 알린다(빌더와 같은 칸 집합 — 무엇과 겹쳤는지 이름을 붙인다).
            string hit = null;
            Vector2Int at = default;
            foreach (var c in cells)
            {
                if (doorZone.TryGetValue(c, out int doorNumber)) hit = $"출입구 {doorNumber}번 앞 공간";
                else if (slotCells.Contains(c)) hit = "목표·출구 자리 표식 칸";
                else if (craneCells.TryGetValue(c, out string crane)) hit = $"크레인 {crane}의 차지 칸·경로(쓸기 상자)";
                else
                {
                    foreach (var (name, danger) in traps)
                        if (danger.Contains(c)) { hit = $"함정 {name}의 칸·위험 범위"; break; }
                    if (hit == null && padCells.Contains(c)) hit = "텔레포트 시설 칸";
                }
                if (hit != null) { at = c; break; }
            }
            if (hit != null)
                firm($"{zone.label}: {hit}과 겹칩니다(예 ({at.x}, {at.y}) — 실행 때 그 칸은 후보에서 빠집니다. 빌더·정적 검사기는 오류, 템플릿 규칙 v4-9).");
            // 지상·공중 구역 판정(빌더 standsIn·airs). 실내를 찾지 못했으면 종류만 본다.
            bool standsIn = region == null ? zone.kind != (int)DungeonSpawnZoneKind.Air
                : cells.Any(c => region.Contains(c) && !hard(c) && floors.Contains(c + Vector2Int.down));
            bool airs = region == null ? zone.kind != (int)DungeonSpawnZoneKind.Ground
                : cells.Any(c => region.Contains(c) && !hard(c) && !floors.Contains(c + Vector2Int.down) && !floors.Contains(c + Vector2Int.down * 2));
            if (zone.kind != (int)DungeonSpawnZoneKind.Air && standsIn) groundOk = true;
            if (zone.kind != (int)DungeonSpawnZoneKind.Ground && airs) airOk = true;
            if (zone.kind == (int)DungeonSpawnZoneKind.Ground && !standsIn) warn($"{zone.label}: 지상 구역인데 안에 서는 칸(아래가 받침인 빈 칸)이 없습니다.");
            if (zone.kind == (int)DungeonSpawnZoneKind.Air && !airs) warn($"{zone.label}: 공중 구역인데 바닥 위 2칸 넘게 뜬 빈 칸이 없습니다.");
            if (region != null)
            {
                bool inside = false;
                for (int x = Mathf.FloorToInt(zone.rect.xMin); x < Mathf.CeilToInt(zone.rect.xMax) && !inside; x++)
                    for (int y = Mathf.FloorToInt(zone.rect.yMin); y < Mathf.CeilToInt(zone.rect.yMax) && !inside; y++)
                        inside = region.Contains(new Vector2Int(x, y));
                if (!inside) warn($"{zone.label}: 실내 빈 칸과 겹치지 않습니다(벽 속 구역은 쓰이지 않습니다).");
            }
        }
        if (!fight) return;
        bool vertical = template.orientation == DungeonRoomOrientation.Vertical;
        if (zones.Count == 0)
            complete($"전투 템플릿에 생성 구역 표식이 없습니다. 동적 구성은 빈 칸 전체에서 고릅니다({(vertical ? "공중" : "지상")} 구역 1개 이상 — 빌더·정적 검사기는 오류, 템플릿 규칙 v4-9).");
        else if (vertical)
        {
            if (!airOk) complete("수직 방에 공중 적을 받는 생성 구역(공중·혼합, 바닥 위 2칸 넘게 뜬 빈 칸)이 없습니다(빌더·정적 검사기는 오류 — 템플릿 규칙 v4-9).");
            if (!groundOk) warn("수직 방에 지상 적을 받는 생성 구역(지상·혼합, 안에 서는 칸)이 없습니다(템플릿 규칙 v4-9).");
        }
        else if (!groundOk) complete("지상 적을 받는 생성 구역(지상·혼합, 안에 서는 칸)이 없습니다(빌더·정적 검사기는 오류 — 템플릿 규칙 v4-9).");
    }

    // 배치별 설정 값: Prefab의 IWaveDeviceSettings 항목(기본값·범위)으로 읽고, 항목을 못 찾으면 저장된 값, 그것도 없으면 fallback.
    private static float DeviceValue(WaveSpawnInfo spawn, string key, float fallback)
    {
        try
        {
            var parameters = spawn?.prefab != null ? spawn.prefab.GetComponent<IWaveDeviceSettings>()?.Parameters : null;
            if (parameters != null)
                foreach (var parameter in parameters)
                    if (parameter.Key == key) return WaveDeviceSettings.Value(spawn.deviceSettings, parameter);
        }
        catch (Exception) { /* Prefab 쪽 항목 계산이 Edit Mode에서 실패해도 저장값·기본값으로 검사한다. */ }
        if (spawn?.deviceSettings != null)
            foreach (var setting in spawn.deviceSettings)
                if (setting.key == key) return setting.value;
        return fallback;
    }

    // Prop의 고체 칸: Platform 레이어의 트리거 아닌 BoxCollider2D를 Prefab 루트 기준으로 모아 회전·배치한 뒤 맞닿은 면(BodyMargin)을 빼고 센다.
    // 4차: layer를 주면 그 레이어(예: OneWay Prop 윗면 TransparentPlatform)의 칸을 센다. 음수면 아무것도 넣지 않는다.
    private static void SolidCells(GameObject prefab, Vector2 root, int rotationSteps, HashSet<Vector2Int> into, int layer = int.MinValue)
    {
        if (layer == int.MinValue)
        {
            layer = LayerMask.NameToLayer("Platform");
            if (layer < 0) layer = 6;
        }
        if (layer < 0) return;
        var toRoot = prefab.transform.worldToLocalMatrix;
        var rotation = Quaternion.Euler(0f, 0f, (((rotationSteps % 4) + 4) % 4) * 90f);
        foreach (var collider in prefab.GetComponentsInChildren<BoxCollider2D>(true))
        {
            if (collider.isTrigger || collider.gameObject.layer != layer) continue;
            var matrix = toRoot * collider.transform.localToWorldMatrix;
            Vector2 half = collider.size * .5f, min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var corner in new[] { new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y), new Vector2(-half.x, half.y), new Vector2(half.x, half.y) })
            {
                Vector2 point = rotation * matrix.MultiplyPoint3x4(collider.offset + corner);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            for (int y = Mathf.FloorToInt(root.y + min.y + BodyMargin); y <= Mathf.FloorToInt(root.y + max.y - BodyMargin); y++)
                for (int x = Mathf.FloorToInt(root.x + min.x + BodyMargin); x <= Mathf.FloorToInt(root.x + max.x - BodyMargin); x++)
                    into.Add(new Vector2Int(x, y));
        }
    }

    // 적 몸이 덮는 칸(셀 좌표). origin = 격자 Tool이 놓은 원점(spawnPosition + spawnOffset, WavePrefabVisuals.Placement 결과),
    // 모양 = WavePrefabVisuals.LocalBounds(collision: true)(Collider 범위, 없으면 Renderer 범위). 맞닿은 면은 BodyMargin만큼 빼고 센다.
    // limit 밖 칸은 목록에 넣지 않고, 몸이 limit을 벗어나면 true를 돌려준다.
    private static bool EnemyBodyCells(GameObject prefab, Vector2 origin, RectInt limit, List<Vector2Int> cells, out Bounds shape)
    {
        cells.Clear();
        shape = WavePrefabVisuals.LocalBounds(prefab, true);
        Vector2 min = origin + (Vector2)shape.min, max = origin + (Vector2)shape.max;
        int x0 = Mathf.FloorToInt(min.x + BodyMargin), x1 = Mathf.FloorToInt(max.x - BodyMargin);
        int y0 = Mathf.FloorToInt(min.y + BodyMargin), y1 = Mathf.FloorToInt(max.y - BodyMargin);
        // 여유보다 작은 몸은 중심 칸 하나로 본다.
        if (x1 < x0) x0 = x1 = Mathf.FloorToInt((min.x + max.x) * .5f);
        if (y1 < y0) y0 = y1 = Mathf.FloorToInt((min.y + max.y) * .5f);
        bool beyond = x0 < limit.xMin || y0 < limit.yMin || x1 >= limit.xMax || y1 >= limit.yMax;
        for (int y = Mathf.Max(y0, limit.yMin); y <= Mathf.Min(y1, limit.yMax - 1); y++)
            for (int x = Mathf.Max(x0, limit.xMin); x <= Mathf.Min(x1, limit.xMax - 1); x++) cells.Add(new Vector2Int(x, y));
        return beyond;
    }

    // 시작 좌표(서 있는 몸 위치일 수 있어 한 칸 위도 본다)에서 닫힌 실내를 찾는다.
    private static bool TryRoom(HashSet<Vector2Int> solids, Vector2 start, HashSet<Vector2Int> region)
    {
        region.Clear();
        if (WaveCellRoomSpace.TryGetBounds(solids, start, out _, region)) return true;
        region.Clear();
        if (WaveCellRoomSpace.TryGetBounds(solids, start + Vector2.up, out _, region)) return true;
        region.Clear();
        return false;
    }

    // ───────── 4차: 빈 칸 면적·수직 여유 요약(미리보기·템플릿 패널용, 템플릿 변경 횟수로 캐시) ─────────
    private sealed class Measure
    {
        public int dirty;
        public bool ok;
        public int empty;
        public RectInt interior;
        public string clearance;
    }
    private static readonly Dictionary<int, Measure> measures = new();
    private static Measure MeasureOf(DungeonRoomTemplateSO template)
    {
        if (template == null) return null;
        int id = template.GetInstanceID(), dirty = EditorUtility.GetDirtyCount(template);
        if (measures.TryGetValue(id, out var cached) && cached.dirty == dirty) return cached;
        var result = new Measure { dirty = dirty };
        measures[id] = result;
        var cells = template.MapCells ?? Array.Empty<WaveMapCell>();
        var solids = new HashSet<Vector2Int>(cells.Where(c => c.kind == WaveCellKind.Platform).Select(c => c.position));
        var region = new HashSet<Vector2Int>();
        if (solids.Count == 0 || template.MapLayer == null || !TryRoom(solids, WaveRoomState.PlayerStartAt(template, 0), region)) return result;
        var mapSpawns = template.MapLayer.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
        var mapOffset = WaveRoomState.LayoutAt(template, 0).spawnOffset;
        var props = new HashSet<Vector2Int>();
        var surfaces = new HashSet<Vector2Int>();
        foreach (var spawn in mapSpawns)
        {
            if (spawn?.prefab == null || NotSolidAtStart(WavePrefabVisuals.DungeonFacilityName(spawn.prefab))) continue;
            SolidCells(spawn.prefab, spawn.spawnPosition + mapOffset, spawn.rotationSteps, props);
            SolidCells(spawn.prefab, spawn.spawnPosition + mapOffset, spawn.rotationSteps, surfaces, OneWayLayer());
        }
        int xMin = region.Min(c => c.x), yMin = region.Min(c => c.y), xMax = region.Max(c => c.x), yMax = region.Max(c => c.y);
        result.ok = true;
        result.interior = new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
        // 빈 칸 수 A(DESIGN_V4 §2.8) = 실내 − 고체 지형 − 고체 Prop. 실내(region)에는 고체 지형이 없다.
        result.empty = region.Count(c => !props.Contains(c));
        var oneWay = new HashSet<Vector2Int>(cells.Where(c => c.kind == WaveCellKind.OneWayPlatform).Select(c => c.position));
        var floors = new HashSet<Vector2Int>(solids); floors.UnionWith(oneWay); floors.UnionWith(props); floors.UnionWith(surfaces);
        var stands = StandCells(region, floors, solids, oneWay, props, surfaces);
        var clearance = MeasureClearance(stands, floors, region, result.interior);
        if (clearance.stands > 0)
            result.clearance = $"수직 여유: 서는 칸 {clearance.stands}개 중 빈 칸 {ClearanceCells}칸 이상 {clearance.roomy}개({Mathf.RoundToInt(clearance.ratio * 100f)}%)"
                + (clearance.ratio >= ClearanceComfort ? " · 통과" : clearance.ratio >= ClearancePass ? " · 통과(경고 구간 75~80%)" : " · 75% 미만(빌더 오류 기준)")
                + $" · 빈 칸 면적 {result.empty} · 실내 {result.interior.width}×{result.interior.height}" + ((result.interior.height + 1) % 6 == 0 ? "(6k − 1)" : "")
                + (string.IsNullOrEmpty(clearance.narrow) ? "" : " · 좁은 구간 " + clearance.narrow);
        return result;
    }
    /// <summary>
    /// 빈 칸 수 A(실내 − 고체 지형 − 고체 Prop, DESIGN_V4 §2.8 동적 구성의 면적)와 실내 외접 사각형. 시작 좌표에서 닫힌 실내를 찾지 못하면 false.
    /// 미리보기의 '예상 적 수'가 쓴다(실행 쪽 DungeonEncounterComposer의 emptyCells와 같은 뜻 — 셀·Prop 경계 계산 방식은 실행 쪽이 정본).
    /// </summary>
    public static bool TryMeasure(DungeonRoomTemplateSO template, out int emptyCells, out RectInt interior)
    {
        var measure = MeasureOf(template);
        emptyCells = measure?.empty ?? 0;
        interior = measure?.interior ?? default;
        return measure != null && measure.ok;
    }
    /// <summary>템플릿 패널의 수직 여유 한 줄(서는 칸 비율·빈 칸 면적·실내 크기·좁은 구간). 잴 수 없으면 null.</summary>
    public static string ClearanceLine(DungeonRoomTemplateSO template) => MeasureOf(template)?.clearance;
}
