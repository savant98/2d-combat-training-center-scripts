using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 훈련 입장 때 원본 WaveData의 실행 사본을 만들고 던전 Wave마다 던전을 생성해 펼친다(DESIGN.md §2.2). 원본 에셋은 절대 고치지 않는다.
/// 던전 Wave의 실행 사본은 시작 방 셀·시작 좌표·크레인 이동·CustomTrigger 완료로 바뀌어, 기존 지형 패널 + 크레인이 그대로 다음 던전으로 옮긴다.
/// 방 내용물(Prop·출입구·텔레포트 시설·적 구성)은 Wave의 spawnObjects에 넣지 않고 DungeonManager가 방 단위로 세운다.
/// v2: 방마다 텔레포트 시설 배치(짝 채움)·배경 종류(Auto를 푼 값)·정적 벽면 키트를 담고, 텔레포트 항목은 Prop에서 뺀다.
/// v3: 목표 슬롯 표식(DungeonObjectiveSlot)도 Prop에서 빼고 room.objectives로 옮긴다(배정된 슬롯에만 목표 번호). 던전의 목표 SO는 floor.objective.
/// v4(DESIGN_V4 §4): 출구 자리 표식(DungeonExitSlot)은 room.exit로, 적 생성 구역 표식(DungeonSpawnZone)은 room.spawnZones로 옮기고 Prop으로 세우지 않는다.
/// 싸우는 방 여부는 room.HasFight로 명시한다(동적 구성 던전의 전투 방은 room.encounter가 비어 있어도 true). 동적 구성 던전은 규칙 SO의 종류별 최대 수를
/// 예열 힌트(DungeonRun.HintPrewarm)로 남긴다.
/// 6차(DESIGN_V6 T11): 도착 문 자리 표식(DungeonArrivalSlot)도 Prop에서 빼고 room.arrival로 옮긴다. 다음 Wave를 문에서 시작할지(크레인 생략)는
/// 실행 중 DungeonCutsceneDirector가 정한다 — 여기서 쓰는 teleportAtStart = true는 재도전(Regenerate)마다 원래대로 되돌리는 기준값이다.
/// </summary>
public static class DungeonRunBuilder
{
    /// <summary>source에 던전 Wave가 하나라도 있는지.</summary>
    public static bool HasDungeon(WaveDataSO source)
    {
        if (source == null || source.waves == null) return false;
        foreach (var wave in source.waves) if (wave != null && wave.flowMode == WaveFlowMode.Dungeon) return true;
        return false;
    }

    /// <summary>
    /// 원본 WaveData의 실행 사본을 만들고 던전 Wave마다 던전을 생성해 펼친다(원본 에셋은 바꾸지 않는다).
    /// runSeed가 0이면 무작위 시드를 쓴다. 던전 Wave가 없으면 null.
    /// </summary>
    public static DungeonRun Build(WaveDataSO source, int runSeed)
    {
        if (!HasDungeon(source)) return null;
        var data = Object.Instantiate(source);
        data.name = source.name + " (Dungeon)";
        data.hideFlags = HideFlags.DontSave;
        var run = new DungeonRun { Data = data, Source = source };
        Fill(run, runSeed);
        return run;
    }

    /// <summary>
    /// 같은 실행 사본(run.Data)을 제자리에서 다시 생성한다(재도전). WaveManager·지형이 들고 있는 사본·Wave 객체는 그대로이고 내용만 바뀐다.
    /// seed가 0이면 무작위 시드다. 새로 생긴 셀은 부른 쪽이 WaveCellTerrain.RegisterCells로 등록한다.
    /// </summary>
    public static void Regenerate(DungeonRun run, int seed)
    {
        if (run == null || run.Data == null || run.Source == null) return;
        Fill(run, seed);
    }

    private static void Fill(DungeonRun run, int runSeed)
    {
        if (runSeed == 0) runSeed = Environment.TickCount;
        if (runSeed == 0) runSeed = 1;
        run.Seed = runSeed;
        var source = run.Source;
        var data = run.Data;
        // 다시 생성할 때 이전 결과가 남지 않게, 아래에서 쓰는 필드를 모두 원본 값으로 되돌린 뒤 쓴다(처음 만들 때는 같은 값이다).
        Restore(source, data);
        int count = data.waves != null ? data.waves.Length : 0;
        run.floors = new DungeonFloor[count];
        int firstDungeon = -1;
        for (int i = 0; i < count; i++)
        {
            var wave = data.waves[i];
            if (wave == null || wave.flowMode != WaveFlowMode.Dungeon) continue;
            var settings = wave.dungeon ?? new DungeonWaveSettings();
            int seed = settings.seed != 0 ? settings.seed : Mix(runSeed, i);
            if (!DungeonGenerator.TryGenerate(settings, seed, out var layout, out var error))
            {
                Debug.LogError($"[DungeonRunBuilder] {source.name} Wave {i + 1} 던전 생성 실패 — 원본 그대로(기본 진행) 진행합니다.\n{error}", source);
                continue;
            }
            if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"[DungeonRunBuilder] {source.name} Wave {i + 1}: {error}", source);
            var floor = BuildFloor(data, i, settings, layout);
            run.floors[i] = floor;
            if (settings.encounterMode == DungeonEncounterMode.Dynamic && settings.encounterRules == null)
                Debug.LogWarning($"[DungeonRunBuilder] {source.name} Wave {i + 1}: 적 구성 방식이 Dynamic이지만 적 생성 규칙(encounterRules)이 비어 있어 손 배치 구성(Authored)으로 진행합니다.", source);
            if (floor.Dynamic) HintDynamic(run, floor.EncounterRules);
            if (firstDungeon < 0) firstDungeon = i;
            ApplyFloor(data, wave, floor, i);
        }
        if (firstDungeon < 0) return;
        if (firstDungeon == 0)
        {
            // 입장하자마자 시작 방에 선다(Room 입장 지점 = 첫 Wave 시작 좌표).
            var start = run.floors[0].Room(run.floors[0].layout.startRoom);
            data.cellMap.cells = start.cells;
            data.playerStart = start.entry;
        }
        // 던전 뒤의 기본 진행 Wave는 원본에서 이어 받던 지형·실내 배치를 그대로 쓰도록 명시한다
        // (실행 사본의 던전 Wave가 지형을 덮어쓰므로, 그대로 두면 던전 시작 방 지형을 이어 받는다).
        for (int i = Mathf.Max(1, firstDungeon); i < count; i++)
        {
            var wave = data.waves[i];
            if (wave == null || run.floors[i] != null) continue;
            if (!wave.overrideCellMap)
            {
                wave.overrideCellMap = true;
                wave.cells = Clone(WaveCellState.Initial(source, i));
            }
            if (!wave.changeTerrain)
            {
                wave.changeTerrain = true;
                wave.room = Copy(WaveRoomState.LayoutAt(source, i));
            }
        }
    }

    // 실행 사본에서 생성이 바꾸는 필드를 원본 값으로 되돌린다(Wave 객체는 그대로 두고 내용만 바꾼다).
    private static void Restore(WaveDataSO source, WaveDataSO data)
    {
        data.cellMap.cells = Clone(source.cellMap.cells);
        data.playerStart = source.playerStart;
        data.overrideClearPortalPosition = source.overrideClearPortalPosition;
        data.clearPortalPosition = source.clearPortalPosition;
        int count = Mathf.Min(source.waves?.Length ?? 0, data.waves?.Length ?? 0);
        for (int i = 0; i < count; i++)
        {
            var from = source.waves[i];
            var to = data.waves[i];
            if (from == null || to == null) continue;
            to.overrideCellMap = from.overrideCellMap;
            to.cells = Clone(from.cells);
            to.changeTerrain = from.changeTerrain;
            to.room = Copy(from.room);
            to.overridePlayerStart = from.overridePlayerStart;
            to.playerStart = from.playerStart;
            to.teleportAtStart = from.teleportAtStart;
            to.alwaysCarryToStart = from.alwaysCarryToStart;
            to.clearType = from.clearType;
            to.customTriggerKey = from.customTriggerKey;
            to.spawnObjects = CopySpawns(from.spawnObjects);
            to.cellBatches = from.cellBatches != null ? (WaveCellBatch[])from.cellBatches.Clone() : Array.Empty<WaveCellBatch>();
            to.terrainEvents = from.terrainEvents != null ? (WaveTerrainEvent[])from.terrainEvents.Clone() : Array.Empty<WaveTerrainEvent>();
        }
    }

    private static void ApplyFloor(WaveDataSO data, Wave wave, DungeonFloor floor, int index)
    {
        var start = floor.Room(floor.layout.startRoom);
        var template = start.node.template;
        wave.overrideCellMap = true;
        wave.cells = start.cells;
        wave.changeTerrain = true;
        var layout = Copy(WaveRoomState.LayoutAt(template, 0));
        layout.bounds = start.interior;
        layout.spawnOffset = Vector2.zero;
        wave.room = layout;
        wave.overridePlayerStart = true;
        wave.playerStart = start.entry;
        wave.teleportAtStart = true;
        // 첫 Wave는 입장 지점이 곧 시작 방 입장점이라 제자리에서 들었다 놓지 않는다(기존 결투장 W1과 같다). 그 뒤 던전은 늘 크레인으로 옮긴다.
        wave.alwaysCarryToStart = index > 0;
        wave.clearType = WaveClearType.CustomTrigger;
        wave.customTriggerKey = DungeonRulesSO.ClearTriggerKey;
        wave.spawnObjects = Array.Empty<WaveSpawnInfo>();
        wave.cellBatches = Array.Empty<WaveCellBatch>();
        wave.terrainEvents = Array.Empty<WaveTerrainEvent>();
    }

    private static DungeonFloor BuildFloor(WaveDataSO data, int index, DungeonWaveSettings settings, DungeonLayout layout)
    {
        var rules = DungeonRulesSO.Default;
        var floor = new DungeonFloor
        {
            waveIndex = index,
            settings = settings,
            layout = layout,
            passage = settings.passage != null ? settings.passage : rules != null ? rules.defaultPassage : null,
            rooms = new DungeonRoomData[layout.rooms.Count],
            // 클리어 조건이 목표 완료일 때만 목표 SO를 넘긴다(배치는 생성기가 layout.objectives에 했다).
            objective = settings.clearCondition == DungeonClearCondition.Objective ? settings.objective : null,
        };
        var styleMaps = new Dictionary<DungeonRoomTemplateSO, int[]>();
        var padIndices = new List<int>();
        var slotIndices = new List<int>();
        foreach (var node in layout.rooms)
        {
            var template = node.template;
            if (!styleMaps.TryGetValue(template, out var styles)) styleMaps[template] = styles = StyleMap(template, data);
            var map = template.MapLayer;
            var objects = map != null && map.spawnObjects != null ? map.spawnObjects : Array.Empty<WaveSpawnInfo>();
            var room = new DungeonRoomData
            {
                node = node,
                backdrop = template.ResolvedBackdrop,
                wallKit = template.wallKit,
            };

            // 셀: 템플릿 맵 층 − 연결된 출입구의 파는 칸(안쪽 2칸 × 높이 3칸). 모든 방이 같은 방 로컬 원점(템플릿 좌표 그대로)에 선다.
            var carved = new HashSet<Vector2Int>();
            foreach (var link in node.links)
            {
                if (link.socket < 0 || link.socket >= node.sockets.Count) continue;
                var carve = node.sockets[link.socket].carve;
                for (int x = carve.xMin; x < carve.xMax; x++)
                    for (int y = carve.yMin; y < carve.yMax; y++) carved.Add(new Vector2Int(x, y));
            }
            var source = template.MapCells ?? Array.Empty<WaveMapCell>();
            var cells = new List<WaveMapCell>(source.Length);
            foreach (var cell in source)
            {
                if (carved.Contains(cell.position)) continue;
                var copy = cell;
                // 템플릿 스타일 번호를 실행 데이터의 스타일 번호로 옮긴다. 셀 묶음(group)은 쓰지 않는다(방 지형은 모두 고정 지형).
                copy.style = cell.style >= 0 && cell.style < styles.Length ? styles[cell.style] : 0;
                copy.group = 0;
                cells.Add(copy);
            }
            room.cells = cells.ToArray();

            // 실내: 맵 층 셀 외접에서 벽 두께만큼 안쪽(셀이 없으면 템플릿 실내 배치)
            room.interior = template.TryGetCellBounds(out var box) && box.width > DungeonRoomTemplateSO.WallThickness * 2 && box.height > DungeonRoomTemplateSO.WallThickness * 2
                ? new Rect(box.xMin + DungeonRoomTemplateSO.WallThickness, box.yMin + DungeonRoomTemplateSO.WallThickness,
                    box.width - DungeonRoomTemplateSO.WallThickness * 2, box.height - DungeonRoomTemplateSO.WallThickness * 2)
                : WaveRoomState.LayoutAt(template, 0).bounds;
            room.entry = WaveRoomState.PlayerStartAt(template, 0);

            // 출입구: 템플릿 출입구마다 하나(연결 안 된 것은 세우지 않고 벽을 남긴다)
            room.doors = new DungeonDoorPlacement[node.sockets.Count];
            for (int s = 0; s < node.sockets.Count; s++)
            {
                var socket = node.sockets[s];
                var placement = new DungeonDoorPlacement
                {
                    socket = s,
                    info = socket,
                    prefab = socket.spawnIndex >= 0 && socket.spawnIndex < objects.Length && objects[socket.spawnIndex] != null ? objects[socket.spawnIndex].prefab : null,
                };
                foreach (var link in node.links)
                    if (link.socket == s) { placement.toRoom = link.toRoom; placement.toSocket = link.toSocket; break; }
                room.doors[s] = placement;
            }

            // 텔레포트 시설: 맵 층에서 읽은 순서(순번 0부터)대로. 짝은 레이아웃의 텔레포트 짝에서 양쪽 모두 채운다(테마 교체표는 적용하지 않는다).
            DungeonGenerator.TeleportPads(template, padIndices);
            room.teleports = new DungeonTeleportPlacement[padIndices.Count];
            for (int k = 0; k < padIndices.Count; k++)
            {
                var info = CopySpawn(objects[padIndices[k]]);
                info.isRequiredToClear = false;
                room.teleports[k] = new DungeonTeleportPlacement { pad = k, info = info };
            }
            foreach (var link in layout.teleports)
            {
                if (link.roomA == node.id && link.padA >= 0 && link.padA < room.teleports.Length) { room.teleports[link.padA].toRoom = link.roomB; room.teleports[link.padA].toPad = link.padB; }
                if (link.roomB == node.id && link.padB >= 0 && link.padB < room.teleports.Length) { room.teleports[link.padB].toRoom = link.roomA; room.teleports[link.padB].toPad = link.padA; }
            }

            // 목표 슬롯: 맵 층에서 읽은 순서(슬롯 순번 0부터). 표식은 세우지 않고 위치만 쓴다. 배정된 슬롯에 목표 진행 번호를 채운다.
            DungeonGenerator.ObjectiveSlots(template, slotIndices);
            room.objectives = new DungeonObjectivePlacement[slotIndices.Count];
            for (int k = 0; k < slotIndices.Count; k++)
            {
                var info = CopySpawn(objects[slotIndices[k]]);
                info.isRequiredToClear = false;
                room.objectives[k] = new DungeonObjectivePlacement { slot = k, info = info };
            }
            if (floor.objective != null)
                foreach (var assignment in layout.objectives)
                    if (assignment.room == node.id && assignment.slot >= 0 && assignment.slot < room.objectives.Length) room.objectives[assignment.slot].index = assignment.index;

            // Prop: 맵 층의 적·출입구·텔레포트 시설·목표 슬롯·출구 자리·생성 구역이 아닌 항목(클리어 조건에 넣지 않고, 테마 교체표를 적용한다)
            var props = new List<WaveSpawnInfo>();
            foreach (var info in objects)
            {
                if (info == null || info.prefab == null) continue;
                if (info.prefab.GetComponent<EnemyController>() != null || info.prefab.GetComponent<DungeonDoor>() != null || info.prefab.GetComponent<DungeonTeleporter>() != null) continue;
                if (info.prefab.GetComponent<DungeonObjectiveSlot>() != null) continue;
                // 4차: 출구 자리 표식은 세우지 않고 위치만 room.exit로 옮긴다(시작 방이면 목표 SO의 출구 문을 그 자리에 세운다). 첫 표식만 쓴다.
                if (info.prefab.GetComponent<DungeonExitSlot>() != null)
                {
                    if (room.exit == null)
                    {
                        room.exit = CopySpawn(info);
                        room.exit.isRequiredToClear = false;
                    }
                    else Debug.LogWarning($"[DungeonRunBuilder] 템플릿 '{template.name}'에 출구 자리 표식이 둘 이상 있어 첫 표식만 씁니다.", template);
                    continue;
                }
                // 6차 T11: 도착 문 자리 표식도 세우지 않고 위치만 room.arrival로 옮긴다(던전 시작 방이면 출구 문을 도착 모드로 그 자리에 세운다). 첫 표식만 쓴다.
                if (info.prefab.GetComponent<DungeonArrivalSlot>() != null)
                {
                    if (room.arrival == null)
                    {
                        room.arrival = CopySpawn(info);
                        room.arrival.isRequiredToClear = false;
                    }
                    else Debug.LogWarning($"[DungeonRunBuilder] 템플릿 '{template.name}'에 도착 문 자리 표식이 둘 이상 있어 첫 표식만 씁니다.", template);
                    continue;
                }
                // 4차: 적 생성 구역 표식은 세우지 않는다(동적 구성의 후보 지점 편향 — 배치 설정 그대로).
                if (DungeonSpawnZone.IsZone(info.prefab))
                {
                    var zone = CopySpawn(info);
                    zone.isRequiredToClear = false;
                    room.spawnZones.Add(zone);
                    continue;
                }
                var copy = CopySpawn(info);
                copy.isRequiredToClear = false;
                if (settings.theme != null) copy.prefab = settings.theme.Resolve(copy.prefab);
                props.Add(copy);
            }
            room.props = props.ToArray();

            // 적 구성: 고른 Enemy 템플릿 Wave의 항목(시간 순서 유지). 동적 구성 던전의 전투 방은 비워 두고 들어오는 순간 짠다(보스 방은 손 배치 그대로).
            var encounter = node.encounter > 0 && !node.dynamicFight ? template.EncounterWave(node.encounter) : null;
            room.encounter = encounter != null ? CopySpawns(encounter.spawnObjects) : Array.Empty<WaveSpawnInfo>();
            room.HasFight = node.dynamicFight || room.encounter.Length > 0;
            floor.rooms[node.id] = room;
        }
        return floor;
    }

    // 동적 구성 던전의 예열 힌트: 종류별로 한 방에서 꺼낼 수 있는 최악의 수(방 상한 ÷ 환산 단위, 방당 묶음 상한, 동시 생존 상한 중 작은 값).
    // Bulwark는 방당 최대 수. 실제 상한(Prefab당 12)은 DungeonRun.HintPrewarm이 자른다.
    private static void HintDynamic(DungeonRun run, DungeonEncounterRulesSO rules)
    {
        if (run == null || rules == null) return;
        int cap = 0;
        for (int level = 0; level < DungeonEncounterRulesSO.LevelCount; level++) cap = Mathf.Max(cap, rules.ConcurrentCap((DungeonThreatLevel)level));
        foreach (var species in rules.species ?? Array.Empty<DungeonSpeciesRule>())
        {
            if (species == null || species.prefab == null) continue;
            int perRoom = Mathf.Max(1, rules.maxCount / Mathf.Max(1, species.units));
            if (species.maxGroupsPerRoom > 0) perRoom = Mathf.Min(perRoom, species.maxGroupsPerRoom * Mathf.Max(1, species.groupSize));
            if (cap > 0) perRoom = Mathf.Min(perRoom, cap);
            run.HintPrewarm(species.prefab, perRoom);
        }
        var bulwark = rules.bulwark;
        if (bulwark != null && bulwark.prefab != null) run.HintPrewarm(bulwark.prefab, Mathf.Max(1, Mathf.Max(bulwark.maxPerRoom, bulwark.extremeMaxPerRoom)));
    }

    // 템플릿 셀 스타일 번호 → 실행 데이터 스타일 번호. 같은 그림·재질·Prefab·충돌을 가진 스타일을 찾고, 없으면 같은 번호(범위 밖이면 0).
    private static int[] StyleMap(DungeonRoomTemplateSO template, WaveDataSO data)
    {
        var from = template.cellMap != null && template.cellMap.styles != null ? template.cellMap.styles : Array.Empty<WaveCellStyle>();
        var to = data.cellMap != null && data.cellMap.styles != null ? data.cellMap.styles : Array.Empty<WaveCellStyle>();
        var result = new int[Mathf.Max(1, from.Length)];
        for (int i = 0; i < from.Length; i++)
        {
            int found = -1;
            if (i < to.Length && Same(from[i], to[i])) found = i;
            for (int j = 0; found < 0 && j < to.Length; j++) if (Same(from[i], to[j])) found = j;
            if (found < 0)
            {
                found = i < to.Length ? i : 0;
                Debug.LogWarning($"[DungeonRunBuilder] 템플릿 '{template.name}'의 셀 스타일 {i}번과 같은 스타일이 '{data.name}'에 없어 {found}번으로 씁니다.", template);
            }
            result[i] = found;
        }
        return result;
    }

    private static bool Same(WaveCellStyle a, WaveCellStyle b)
    {
        if (a == null || b == null) return a == b;
        return a.tile == b.tile && a.material == b.material && a.prefab == b.prefab && a.generated == b.generated && a.layer == b.layer
            && a.color == b.color && a.lineMaterial == b.lineMaterial && a.sortingLayer == b.sortingLayer && a.sortingOrder == b.sortingOrder;
    }

    // 실행 시드와 Wave 번호로 Wave마다 다른 시드를 만든다(0은 피한다).
    private static int Mix(int runSeed, int index)
    {
        unchecked
        {
            uint h = (uint)runSeed * 0x9E3779B1u ^ (uint)(index + 1) * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            int value = (int)h;
            return value != 0 ? value : 1;
        }
    }

    private static WaveMapCell[] Clone(WaveMapCell[] cells) => cells != null ? (WaveMapCell[])cells.Clone() : Array.Empty<WaveMapCell>();

    private static WaveRoomLayout Copy(WaveRoomLayout layout)
    {
        if (layout == null) return new WaveRoomLayout();
        return new WaveRoomLayout
        {
            bounds = layout.bounds,
            spawnOffset = layout.spawnOffset,
            centerPlayer = layout.centerPlayer,
            faceCenter = layout.faceCenter,
            duration = layout.duration,
            paintPlatforms = layout.paintPlatforms,
            platformCells = layout.platformCells != null ? (Vector2Int[])layout.platformCells.Clone() : Array.Empty<Vector2Int>(),
            removedCells = layout.removedCells != null ? (Vector2Int[])layout.removedCells.Clone() : Array.Empty<Vector2Int>(),
        };
    }

    private static WaveSpawnInfo[] CopySpawns(WaveSpawnInfo[] spawns)
    {
        if (spawns == null) return Array.Empty<WaveSpawnInfo>();
        var result = new List<WaveSpawnInfo>(spawns.Length);
        foreach (var info in spawns) if (info != null) result.Add(CopySpawn(info));
        return result.ToArray();
    }

    private static WaveSpawnInfo CopySpawn(WaveSpawnInfo info) => new WaveSpawnInfo
    {
        prefab = info.prefab,
        isRequiredToClear = info.isRequiredToClear,
        keepAfterWave = info.keepAfterWave,
        isBossEnemy = info.isBossEnemy,
        spawnDelay = info.spawnDelay,
        spawnPosition = info.spawnPosition,
        rotationSteps = info.rotationSteps,
        lifetime = info.lifetime,
        goalKeyOverride = info.goalKeyOverride,
        deviceSettings = info.deviceSettings != null ? (WaveDeviceSetting[])info.deviceSettings.Clone() : Array.Empty<WaveDeviceSetting>(),
    };
}
