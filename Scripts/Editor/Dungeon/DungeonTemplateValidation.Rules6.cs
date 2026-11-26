using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 6차 단일 검사기(DESIGN_V6 T16) — 빌더(DungeonTemplateBuilder.Inspect·InspectV4)와 작업 폴더 정적 검사기(check_templates.py)에만 있던 규칙을 옮기고
// 6차 새 규칙을 더한 부분. 규칙 정의(칸·상수)는 빌더와 같다(빌더는 이제 이 검사기를 부른다).
//  - 메타(규칙 v4-12 컨셉 · 배경/벽면 키트 · 휴식 방 최대 수)
//  - 테마 금지 Prop(Combat 테마 레이저 — T6), 적 구성 층 함정 금지(T8), 지상 적 발밑 받침·OneWay 겹침
//  - 배치(카탈로그 Prop): 허용 회전 · 실내 빈 칸 · OneWay·출입구 앞·시작 위치·다른 Prop과 겹침 · 설치면 · 템플릿에 두지 않는 실물 · 카탈로그 밖 Prefab ·
//    생성 구역 놓는 칸 · Prop 수 권장(v4-11) · 전투 후보 목표 자리 1~2개(v4-7)
//  - 함정 묶음(v4-5: 개수·외톨이·위상·안전 경로 — 틈 하한 4, T10) · 레이저 예고 0.5초 · 함정 칸 ↔ 출입구 앞·시작·자리 표식·고정 크레인 매달림·생성 구역
//  - T10 함정 간격(서는 면 안전 칸 ≥ 4 — 정의 D, 서는 면에 닿지 않는 함정은 체비셰프 빈 칸 ≥ 4 — 정의 B), 경로 크레인 쓸기 ∩ (함정 칸 ∪ 위험 ∪ 둘레 1칸) = ∅
//  - 파괴 Prop 수(v4-10)·중력형 쌓기 금지·외톨이, 수직 여유(v4-1 — 75% 미만 오류), 도달성 BFS(v4-2)
// 충돌·설치면·위험 범위는 카탈로그(DungeonPropCatalogSO)의 빌더 정의를 쓰고, 카탈로그에 없는 항목(회귀 고정물 등)은 Prefab 충돌체로 대신 센다.
// 심각도는 Sink가 정한다: Firm = 빌더 오류 규칙(엄격이면 오류, 아니면 경고), Complete = 완성도 규칙(엄격이면 오류 — Completeness 표시), Advice = 권장(늘 경고).
// 7차(DESIGN_V7): 낙석 구조물 규칙 'v7 낙석'은 DungeonTemplateValidation.Rules7.cs(이 진입점에서 부른다). 고정 크레인 경로 점 = DungeonCranePaths.HangFeet(V11 — 매달린 발).
public static partial class DungeonTemplateValidation
{
    /// <summary>검사 설정. null이면 <see cref="OptionsFor"/>(에셋 경로)로 정한다.</summary>
    public sealed class Options
    {
        /// <summary>템플릿 테마(템플릿 폴더 이름, 예 "Combat"). 테마 금지 Prop을 본다. 비면 테마 규칙 없음.</summary>
        public string Theme;
        /// <summary>엄격: 빌더와 같은 심각도(빌더 오류 규칙·6차 새 규칙·완성도 규칙이 오류). 테마 폴더 안 에셋·빌더가 켠다.</summary>
        public bool Strict;
    }
    /// <summary>템플릿 정본 폴더(테마별 하위 폴더 — DUNGEON_WAVES.md).</summary>
    public const string TemplatesRoot = "Assets/Data/Dungeon/Templates";
    /// <summary>에셋 경로가 템플릿 정본 폴더 안이면 그 테마(바로 아래 폴더 이름 — 폴더 바로 아래면 빈 문자열), 밖이면 null.</summary>
    public static string ThemeOf(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;
        string path = assetPath.Replace('\\', '/');
        string root = TemplatesRoot + "/";
        if (!path.StartsWith(root, StringComparison.Ordinal)) return null;
        string rest = path.Substring(root.Length);
        int slash = rest.IndexOf('/');
        return slash > 0 ? rest.Substring(0, slash) : "";
    }
    public static Options OptionsFor(DungeonRoomTemplateSO template)
    {
        string path = template != null ? AssetDatabase.GetAssetPath(template) : null;
        string theme = ThemeOf(path);
        return new Options { Theme = string.IsNullOrEmpty(theme) ? null : theme, Strict = theme != null };
    }

    // 이동 수치(빌더·check_templates.py와 같은 값): 1회 점프 3.283칸(고정 틱) → 정수 3칸·가로 7, 2단 점프 5칸·가로 9, 점프대 = 3.283 × 높이 배율.
    const float JumpHeight = 10f * 10f / (2f * 15f) - 10f * .01f * .5f, CraneBodySize = 1.5f;
    const int JumpRise = 3, JumpReach = 7, DoubleRise = 5, DoubleReach = 9;
    /// <summary>T10 함정 간격 하한(서는 면 안전 칸·체비셰프 빈 칸)과 묶음 안전 경로 틈 하한(6차: 2 → 4).</summary>
    public const int TrapSpacingMin = 4;
    /// <summary>T10 경로 크레인 쓸기 상자와 함정 칸·위험 범위 사이에 비워 둘 둘레(칸).</summary>
    public const int CraneTrapMargin = 1;
    public const int BundleMin = 2, BundleMax = 5, BundleGapMax = 16, PropLimit = 32;

    sealed class Sink
    {
        readonly List<DungeonTemplateIssue> issues;
        public readonly bool Strict;
        public string Rule;
        public Sink(List<DungeonTemplateIssue> issues, bool strict) { this.issues = issues; Strict = strict; }
        void Add(bool error, bool completeness, string message, int wave, int spawn, Vector2Int? cell, IEnumerable<Vector2Int> cells)
            => issues.Add(new DungeonTemplateIssue(error, message, Rule, completeness, wave, spawn, cell, cells?.Distinct().Take(400).ToArray()));
        public void Error(string m, int wave = -1, int spawn = -1, Vector2Int? cell = null, IEnumerable<Vector2Int> cells = null) => Add(true, false, m, wave, spawn, cell, cells);
        public void Warn(string m, int wave = -1, int spawn = -1, Vector2Int? cell = null, IEnumerable<Vector2Int> cells = null) => Add(false, false, m, wave, spawn, cell, cells);
        /// <summary>빌더 오류 규칙: 엄격이면 오류, 아니면 경고.</summary>
        public void Firm(string m, int wave = -1, int spawn = -1, Vector2Int? cell = null, IEnumerable<Vector2Int> cells = null) => Add(Strict, false, m, wave, spawn, cell, cells);
        /// <summary>완성도 규칙(실물 템플릿에만 필요한 구성): 엄격이면 오류.</summary>
        public void Complete(string m, int wave = -1, int spawn = -1, Vector2Int? cell = null, IEnumerable<Vector2Int> cells = null) => Add(Strict, true, m, wave, spawn, cell, cells);
        /// <summary>권장(늘 경고, 완성도 표시).</summary>
        public void Advice(string m, int wave = -1, int spawn = -1, Vector2Int? cell = null, IEnumerable<Vector2Int> cells = null) => Add(false, true, m, wave, spawn, cell, cells);
    }

    // 맵 층 항목 하나(출입구·적·셀 제외).
    sealed class Placed
    {
        public int Index;
        public string Label;
        public WaveSpawnInfo Spawn;
        public GameObject Prefab;
        public DungeonPropCatalogSO.Entry Entry;
        public Vector2 Root;
        public int Rotation;
        /// <summary>차지 칸(길이 설정형은 늘어난 칸 포함).</summary>
        public RectInt Area;
        /// <summary>회전한 기본 점유 칸(늘어나지 않은 칸).</summary>
        public RectInt Base;
        public HashSet<Vector2Int> Cells;
        public bool Marker, Objective;
        public string Name => Prefab != null ? Prefab.name : "참조 없음";
    }
    // 빌더 Plan과 같은 정의의 검사 모델(SO에서 만든다).
    sealed class Model
    {
        public HashSet<Vector2Int> Solids, OneWay, Region;
        public readonly HashSet<Vector2Int> PropSolids = new HashSet<Vector2Int>(), PropOneWay = new HashSet<Vector2Int>();
        public readonly HashSet<Vector2Int> DoorZone = new HashSet<Vector2Int>(), StartBody = new HashSet<Vector2Int>(), Interior = new HashSet<Vector2Int>();
        public readonly Dictionary<Vector2Int, Placed> Owner = new Dictionary<Vector2Int, Placed>();
        public readonly Dictionary<Vector2Int, int> Pads = new Dictionary<Vector2Int, int>();
        public readonly List<Placed> Items = new List<Placed>();
        public List<DungeonDoorSocket> Doors;
        public RectInt InteriorBox;
        public Vector2Int StartCell;
        public bool HasRegion;
        public bool Support(Vector2Int c) => Solids.Contains(c) || PropSolids.Contains(c) || OneWay.Contains(c) || PropOneWay.Contains(c);
        public bool Blocked(Vector2Int c) => Solids.Contains(c) || PropSolids.Contains(c);
        public bool Through(Vector2Int c) => OneWay.Contains(c) || PropOneWay.Contains(c);
    }
    sealed class CraneRoute
    {
        public Placed Item;
        public string Label;
        public bool Path;
        public Vector2 Board;
        public float Radius;
        public List<Vector2> Corners, Points;
        public readonly HashSet<Vector2Int> Sweep = new HashSet<Vector2Int>();
    }
    readonly struct StandCell
    {
        public readonly int X, Y, Strict; public readonly bool UnderCeiling;
        public StandCell(int x, int y, int strict, bool underCeiling) { X = x; Y = y; Strict = strict; UnderCeiling = underCeiling; }
    }

    static Model BuildModel(DungeonRoomTemplateSO template, HashSet<Vector2Int> solids, HashSet<Vector2Int> region, List<DungeonDoorSocket> doors)
    {
        var model = new Model { Solids = solids, Region = region ?? new HashSet<Vector2Int>(), Doors = doors ?? new List<DungeonDoorSocket>(), HasRegion = region != null && region.Count > 0 };
        model.OneWay = new HashSet<Vector2Int>((template.MapCells ?? Array.Empty<WaveMapCell>()).Where(c => c.kind == WaveCellKind.OneWayPlatform).Select(c => c.position));
        // 시작 좌표 = 발 가운데 격자점 + 1칸(빌더 StandPoint). 몸 = 두 열(x−1, x) × 두 칸.
        var start = WaveRoomState.PlayerStartAt(template, 0);
        int sx = Mathf.RoundToInt(start.x), sy = Mathf.FloorToInt(start.y + .01f) - 1;
        model.StartCell = new Vector2Int(sx - 1, sy);
        model.StartBody.UnionWith(new[] { new Vector2Int(sx - 1, sy), new Vector2Int(sx, sy), new Vector2Int(sx - 1, sy + 1), new Vector2Int(sx, sy + 1) });
        foreach (var door in model.Doors)
        {
            int first = door.side == DungeonDoorSide.Right ? door.wallFaceX - DoorFrontDepth : door.wallFaceX;
            for (int x = first; x < first + DoorFrontDepth; x++)
                for (int y = door.floorY; y < door.floorY + DungeonDoor.Height; y++) model.DoorZone.Add(new Vector2Int(x, y));
        }
        var spawns = template.MapLayer?.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
        var offset = WaveRoomState.LayoutAt(template, 0).spawnOffset;
        for (int i = 0; i < spawns.Length; i++)
        {
            var spawn = spawns[i];
            var prefab = spawn?.prefab;
            if (prefab == null || prefab.GetComponent<DungeonDoor>() != null || prefab.GetComponent<EnemyController>() != null || prefab.GetComponent<WaveTerrainCellView>() != null) continue;
            var entry = DungeonPropCatalogSO.Lookup(prefab);
            int rotation = DungeonPropCatalogSO.Normalize(spawn.rotationSteps);
            var root = spawn.spawnPosition + offset;
            bool slot = WavePrefabVisuals.IsDungeonSlot(prefab);
            var baseArea = WavePrefabVisuals.Cells(root, slot ? WavePrefabVisuals.SlotFootprint(prefab, rotation) : WavePrefabVisuals.Footprint(prefab, rotation));
            var area = slot ? baseArea : WavePrefabVisuals.OccupiedCells(prefab, root, rotation, spawn.deviceSettings);
            var item = new Placed
            {
                Index = i, Spawn = spawn, Prefab = prefab, Entry = entry, Root = root, Rotation = rotation, Area = area, Base = baseArea,
                Label = $"맵 층 #{i + 1} {prefab.name}",
                Marker = entry != null ? entry.kind == DungeonPropCatalogSO.Kind.Marker : WavePrefabVisuals.IsDungeonSpawnZone(prefab),
                Objective = entry != null ? entry.kind == DungeonPropCatalogSO.Kind.Objective : slot,
            };
            item.Cells = new HashSet<Vector2Int>(CellsOf(area));
            model.Items.Add(item);
            if (item.Marker) continue;   // 표식은 칸을 차지하지 않는다(놓는 칸만 따로 본다)
            foreach (var c in item.Cells) model.Owner.TryAdd(c, item);
            if (entry != null)
            {
                switch (entry.collision)
                {
                    case DungeonPropCatalogSO.Collision.Solid: model.PropSolids.UnionWith(item.Cells); break;
                    case DungeonPropCatalogSO.Collision.OneWay: for (int x = area.xMin; x < area.xMax; x++) model.PropOneWay.Add(new Vector2Int(x, area.yMax - 1)); break;
                    case DungeonPropCatalogSO.Collision.Base: for (int x = area.xMin; x < area.xMax; x++) model.PropSolids.Add(new Vector2Int(x, area.yMin)); break;
                }
                if (!string.IsNullOrEmpty(entry.padHeightKey))
                {
                    int rise = Mathf.FloorToInt(JumpHeight * SettingOr(spawn, entry.padHeightKey, entry.padHeightDefault));
                    foreach (var c in item.Cells) model.Pads[c] = rise;
                }
            }
            else if (!item.Objective && !NotSolidAtStart(WavePrefabVisuals.DungeonFacilityName(prefab)))
            {
                // 카탈로그 밖 항목(회귀 고정물 등)은 Prefab 충돌체로 센다(예전 편집기 검사와 같다).
                SolidCells(prefab, root, rotation, model.PropSolids);
                SolidCells(prefab, root, rotation, model.PropOneWay, OneWayLayer());
            }
        }
        if (model.HasRegion)
        {
            int xMin = model.Region.Min(c => c.x), yMin = model.Region.Min(c => c.y), xMax = model.Region.Max(c => c.x), yMax = model.Region.Max(c => c.y);
            model.InteriorBox = new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
            model.Interior.UnionWith(model.Region);
            for (int x = xMin; x <= xMax; x++)
                for (int y = yMin; y <= yMax; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (model.Blocked(c)) model.Interior.Add(c);
                }
        }
        return model;
    }

    static List<CraneRoute> CraneRoutes(Model model)
    {
        var result = new List<CraneRoute>();
        foreach (var item in model.Items)
        {
            if (!DungeonCranePaths.IsCrane(item.Prefab)) continue;
            var settings = item.Spawn.deviceSettings;
            var route = new CraneRoute { Item = item, Label = item.Label, Path = DungeonCranePaths.IsPathCrane(item.Prefab), Radius = DungeonCranePaths.Radius(item.Prefab, settings) };
            if (route.Path)
            {
                route.Corners = DungeonCranePaths.Feet(item.Prefab, item.Root, settings);
                if (route.Corners.Count == 0) route.Corners.Add(DungeonCranePaths.HomeFeet(item.Prefab, item.Root));
                route.Points = PathSamples(route.Corners);
                foreach (var p in route.Points) route.Sweep.UnionWith(SweepCells(p));
            }
            else
            {
                // 7차 V11: 고정 크레인은 매달린 발(HangFeet — 몸 중심 = 고리 중심). 함정 안전 경로(Points)·함정 금지 구역(Board 몸 칸)·도달성이 이 점을 따른다.
                route.Corners = new List<Vector2> { DungeonCranePaths.HangFeet(item.Prefab, item.Root) };
                route.Points = new List<Vector2>(route.Corners);
            }
            route.Board = route.Corners[0];
            result.Add(route);
        }
        return result;
    }

    static string AllowedText(DungeonPropCatalogSO.Entry entry)
    {
        var list = new List<string>();
        for (int r = 0; r < 4; r++) if (entry.Allows(r)) list.Add($"{r * 90}°");
        return list.Count == 0 ? "없음" : string.Join("·", list);
    }
    static int Chebyshev(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    /// <summary>두 칸 집합 사이 체비셰프 빈 칸 수(맞닿으면 0, 겹치면 -1).</summary>
    static int ChebyshevGap(HashSet<Vector2Int> a, HashSet<Vector2Int> b)
    {
        int best = int.MaxValue;
        foreach (var p in a) foreach (var q in b) best = Mathf.Min(best, Chebyshev(p, q) - 1);
        return best;
    }
    static List<(int y, int x0, int x1)> RowSegments(IEnumerable<Vector2Int> cells)
    {
        var segments = new List<(int y, int x0, int x1)>();
        foreach (var c in cells.OrderBy(c => c.y).ThenBy(c => c.x))
        {
            if (segments.Count > 0 && segments[segments.Count - 1].y == c.y && segments[segments.Count - 1].x1 == c.x - 1) segments[segments.Count - 1] = (c.y, segments[segments.Count - 1].x0, c.x);
            else segments.Add((c.y, c.x, c.x));
        }
        return segments;
    }

    // ───────────────────────────── 6차 규칙 진입점 ─────────────────────────────
    static void CheckRules6(DungeonRoomTemplateSO template, List<DungeonTemplateIssue> issues, HashSet<Vector2Int> solids, HashSet<Vector2Int> region, List<DungeonDoorSocket> doors, Options options)
    {
        var sink = new Sink(issues, options != null && options.Strict);
        var model = BuildModel(template, solids, region, doors);
        MetaRules(template, sink);
        ThemeRules(template, options?.Theme, sink);
        EncounterRules(template, model, sink);
        PlacementRules(template, model, sink);
        var cranes = CraneRoutes(model);
        var traps = model.Items.Where(i => i.Entry != null && i.Entry.kind == DungeonPropCatalogSO.Kind.Trap).ToList();
        Func<Vector2Int, bool> inside = model.HasRegion ? (Func<Vector2Int, bool>)model.Interior.Contains : c => !model.Blocked(c);
        var dangers = traps.ToDictionary(t => t, t => TrapDanger(t.Spawn, t.Area, model.Blocked, inside, (Func<Vector2Int, bool>)model.Solids.Contains));
        BundleRules(template, model, traps, dangers, cranes, sink);
        HaltRules(template, traps, sink);
        RockfallRules(model, traps, sink);   // 7차 V1 낙석(Rules7)
        if (model.HasRegion)
        {
            TrapSpacingRules(model, traps, dangers, sink);
            CraneTrapRules(cranes, traps, dangers, sink);
        }
        BreakableRules(template, model, sink);
        var stands = model.HasRegion ? ClearanceRule(model, sink) : new List<StandCell>();
        if (model.HasRegion) ReachRules(model, cranes, stands, sink);
    }

    // ── 메타(빌더 Inspect 메타 규칙) ──
    static void MetaRules(DungeonRoomTemplateSO template, Sink sink)
    {
        sink.Rule = "v4-12 메타";
        if (string.IsNullOrWhiteSpace(template.concept)) sink.Firm("컨셉 한 줄이 비었습니다(규칙 v4-12 — 격자 Tool·문서 표시용).");
        var resolved = template.ResolvedBackdrop;
        if (resolved == DungeonBackdropMode.Parallax && template.wallKit != DungeonWallKit.None) sink.Warn($"시차 배경 방인데 벽면 키트 {template.wallKit}가 있습니다(쓰이지 않음).");
        if (template.sizeClass == DungeonRoomSize.Small && resolved != DungeonBackdropMode.Wall) sink.Warn("작은 방인데 배경이 정적 벽면이 아닙니다(좁은 맵 = 정적 벽면 권장).");
        if (template.sizeClass == DungeonRoomSize.Large && resolved != DungeonBackdropMode.Parallax) sink.Warn("큰 방인데 배경이 시차 배경이 아닙니다(큰 맵 = 시차 배경 권장).");
        if (template.HasTag(DungeonRoomTags.Rest) && template.maxPerFloor != 1) sink.Warn("휴식 방은 던전당 최대 수 1을 권장합니다(수리대 던전당 1회).");
    }

    // ── 테마 금지 Prop(6차 T6 — 전투 훈련 Combat 테마 레이저 금지) ──
    static void ThemeRules(DungeonRoomTemplateSO template, string theme, Sink sink)
    {
        if (string.IsNullOrEmpty(theme)) return;
        sink.Rule = "T6 테마 금지";
        var catalog = DungeonPropCatalogSO.Active;
        for (int w = 0; w < (template.waves?.Length ?? 0); w++)
        {
            var spawns = template.waves[w]?.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
            for (int s = 0; s < spawns.Length; s++)
            {
                var prefab = spawns[s]?.prefab;
                if (prefab == null || !catalog.Forbidden(theme, prefab.name, out string reason)) continue;
                sink.Firm($"{(w == 0 ? "맵 층" : $"적 구성 {w}")} #{s + 1} {prefab.name}: {theme} 테마 템플릿에 둘 수 없는 Prefab입니다 — {reason}", w, s);
            }
        }
    }

    // ── 적 구성 층(6차 T8 함정 금지)·지상 적 발밑(빌더 Inspect) ──
    static readonly HashSet<string> GroundEnemies = new HashSet<string> { "Roller", "Bulwark", "Jumper" };
    static void EncounterRules(DungeonRoomTemplateSO template, Model model, Sink sink)
    {
        for (int w = 1; w < (template.waves?.Length ?? 0); w++)
        {
            var wave = template.waves[w];
            if (wave == null) continue;
            var info = template.encounters != null && w - 1 < template.encounters.Length ? template.encounters[w - 1] : null;
            string prefix = $"적 구성 {w}({(info != null ? info.name : "이름 없음")})";
            var offset = WaveRoomState.LayoutAt(template, w).spawnOffset;
            var spawns = wave.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
            for (int s = 0; s < spawns.Length; s++)
            {
                var prefab = spawns[s]?.prefab;
                if (prefab == null) continue;
                if (IsTrap(prefab))
                {
                    sink.Rule = "T8 적 구성 층 함정";
                    sink.Firm($"{prefix} #{s + 1} {prefab.name}: 적 구성 층에는 함정을 두지 않습니다(6차 T8 — 함정은 맵 층에만 두고 방을 클리어하면 멈춥니다).", w, s);
                    continue;
                }
                if (prefab.GetComponent<EnemyController>() == null || !GroundEnemies.Contains(prefab.name)) continue;
                sink.Rule = "지상 적 받침";
                var shape = WavePrefabVisuals.LocalBounds(prefab, true);
                var origin = spawns[s].spawnPosition + offset;
                var feet = Vector2Int.FloorToInt(origin + new Vector2(shape.center.x, shape.min.y + .001f));
                if (!model.Support(feet + Vector2Int.down))
                    sink.Firm($"{prefix} #{s + 1} {prefab.name}: 지상 적인데 발밑 칸 ({feet.x}, {feet.y - 1})에 받침(지형·고체 Prop·OneWay)이 없습니다.", w, s, feet + Vector2Int.down);
                Vector2 min = origin + (Vector2)shape.min, max = origin + (Vector2)shape.max;
                for (int y = Mathf.FloorToInt(min.y + BodyMargin); y <= Mathf.FloorToInt(max.y - BodyMargin); y++)
                    for (int x = Mathf.FloorToInt(min.x + BodyMargin); x <= Mathf.FloorToInt(max.x - BodyMargin); x++)
                    {
                        var c = new Vector2Int(x, y);
                        if (!model.Through(c)) continue;
                        sink.Firm($"{prefix} #{s + 1} {prefab.name}: 지상 적 몸이 OneWay 칸 ({x}, {y})과 겹칩니다.", w, s, c);
                        y = int.MaxValue - 1; break;
                    }
            }
        }
    }

    // ── 배치(빌더 Inspect Prop 규칙 — 카탈로그 Prop) ──
    static void PlacementRules(DungeonRoomTemplateSO template, Model model, Sink sink)
    {
        int counted = 0;
        var baseOwner = new Dictionary<Vector2Int, Placed>();
        foreach (var item in model.Items)
        {
            if (item.Marker) continue;
            foreach (var c in CellsOf(item.Base)) baseOwner.TryAdd(c, item);
        }
        var reportedPairs = new HashSet<(int, int)>();
        foreach (var item in model.Items)
        {
            var entry = item.Entry;
            if (!item.Marker && !item.Objective) counted++;
            sink.Rule = "v4 배치";
            if (entry == null)
            {
                // 카탈로그 밖 실물 Prefab(삭제 예정 Prop·튜토리얼 부품 등). 삭제 시설은 다른 검사가 알린다.
                if (EditorUtility.IsPersistent(item.Prefab) && !item.Marker && !item.Objective && WavePrefabVisuals.DeletedFacilityName(item.Prefab) == null)
                    sink.Firm($"{item.Label}: Prop 카탈로그({DungeonPropCatalogSO.AssetPath})에 없는 Prefab입니다. 템플릿에 둘 수 있는 Prop이 아니거나 카탈로그에 아직 등록하지 않았습니다.", 0, item.Index, item.Area.position);
                continue;
            }
            if (!entry.templateAllowed)
            {
                sink.Firm($"{item.Label}: 템플릿에 직접 두지 않는 실물입니다 — {(string.IsNullOrWhiteSpace(entry.note) ? "목표 SO가 세웁니다" : entry.note.Trim())}", 0, item.Index, item.Area.position);
                continue;
            }
            if (!entry.Allows(item.Rotation))
                sink.Firm($"{item.Label}: 회전 {item.Rotation * 90}°는 이 Prop의 허용 회전({AllowedText(entry)})이 아닙니다(카탈로그 — 격자 Tool의 R은 허용 회전만 돕니다).", 0, item.Index, item.Area.position);
            if (item.Marker)
            {
                // 생성 구역: 놓는 칸(구역 왼쪽 아래)이 실내 빈 칸이고 다른 항목이 차지하지 않아야 한다(빌더 규칙 v4-9).
                var anchor = item.Area.position;
                if (model.HasRegion && !model.Region.Contains(anchor)) sink.Firm($"{item.Label}: 놓는 칸 ({anchor.x}, {anchor.y})(구역 왼쪽 아래)이 실내 빈 칸이 아닙니다.", 0, item.Index, anchor);
                if (model.Owner.TryGetValue(anchor, out var under)) sink.Firm($"{item.Label}: 놓는 칸 ({anchor.x}, {anchor.y})이 {under.Label}와 겹칩니다(격자 Tool 배치가 실패할 수 있음 — 빈 칸에 놓으세요).", 0, item.Index, anchor);
                if (DungeonSpawnZone.TryRead(item.Spawn, item.Root - item.Spawn.spawnPosition, out var rect, out _, out _))
                {
                    var cells = new List<Vector2Int>();
                    for (int x = Mathf.RoundToInt(rect.xMin); x < Mathf.RoundToInt(rect.xMin) + Mathf.Max(1, Mathf.RoundToInt(rect.width)); x++)
                        for (int y = Mathf.RoundToInt(rect.yMin); y < Mathf.RoundToInt(rect.yMin) + Mathf.Max(1, Mathf.RoundToInt(rect.height)); y++) cells.Add(new Vector2Int(x, y));
                    if (model.HasRegion && cells.Count(c => model.Region.Contains(c) && !model.PropSolids.Contains(c)) < cells.Count * .5f)
                        sink.Warn($"{item.Label}: 구역 {rect.width:0}×{rect.height:0}의 절반 넘게가 고체·실내 밖입니다.", 0, item.Index, anchor);
                }
                continue;
            }
            if (model.HasRegion && !item.Objective)
            {
                var outside = item.Cells.Where(c => !model.Region.Contains(c)).OrderBy(c => c.y).ThenBy(c => c.x).Take(1).ToArray();
                if (outside.Length > 0) sink.Firm($"{item.Label}: 차지 칸 ({outside[0].x}, {outside[0].y})이 실내 빈 칸 밖(고체·외곽)입니다.", 0, item.Index, outside[0]);
            }
            var oneWay = item.Cells.Where(model.OneWay.Contains).Take(1).ToArray();
            if (oneWay.Length > 0) sink.Firm($"{item.Label}: 지형 OneWay 칸 ({oneWay[0].x}, {oneWay[0].y})과 겹칩니다.", 0, item.Index, oneWay[0]);
            var front = item.Cells.Where(model.DoorZone.Contains).Take(1).ToArray();
            if (front.Length > 0) sink.Firm($"{item.Label}: 출입구 앞 공간 ({front[0].x}, {front[0].y})과 겹칩니다(관에서 나온 Player가 서는 자리).", 0, item.Index, front[0]);
            var start = item.Cells.Where(model.StartBody.Contains).Take(1).ToArray();
            if (start.Length > 0) sink.Firm($"{item.Label}: 시작 위치(몸 2×2) 칸 ({start[0].x}, {start[0].y})과 겹칩니다.", 0, item.Index, start[0]);
            // 다른 Prop과 겹침: 기본 점유 칸끼리(격자 Tool이 막지만 JSON·수동 편집 데이터를 본다), 늘어난 칸(분쇄 투입구·파괴 격자 — 컨베이어·레일은 '늘어난 칸' 검사가 본다).
            bool stretchedFacility = WavePrefabVisuals.DungeonFacilityName(item.Prefab) is string facility && (facility == Conveyor || facility == Rail);
            foreach (var c in item.Cells)
            {
                if (!baseOwner.TryGetValue(c, out var other) || other == item) continue;
                bool stretched = !item.Base.Contains(c);
                if (stretched && stretchedFacility) continue;
                if (!reportedPairs.Add((Mathf.Min(item.Index, other.Index), Mathf.Max(item.Index, other.Index)))) continue;
                sink.Firm(stretched ? $"{item.Label}: 늘어난 칸 ({c.x}, {c.y})에 {other.Label}이 있습니다(길이 설정으로 늘어난 칸도 차지 칸입니다)."
                    : $"{item.Label}: {other.Label}와 칸 ({c.x}, {c.y})이 겹칩니다.", 0, item.Index, c);
                break;
            }
            if (entry.mount != DungeonPropCatalogSO.Mount.Back && model.HasRegion)
            {
                string problem = DungeonPropCatalogSO.MountProblem(entry, item.Area, item.Rotation, model.Solids.Contains, model.Blocked, model.Support);
                if (problem != null) sink.Firm($"{item.Label}: {problem}.", 0, item.Index, item.Area.position);
            }
        }
        sink.Rule = "v4-11 Prop 수";
        int low = template.sizeClass == DungeonRoomSize.Small ? 2 : 5;
        if (counted < low || counted > PropLimit) sink.Advice($"Prop {counted}개(표식 제외, 권장 {low}~{PropLimit} — 규칙 v4-11).");
        // 목표 자리 수(규칙 v4-7): 전투 후보 템플릿(일반 · 휴식 아님 · 시작 전용 아님)은 1~2개.
        sink.Rule = "v4-7 목표 자리";
        int slots = model.Items.Count(i => WavePrefabVisuals.IsDungeonObjectiveSlot(i.Prefab));
        bool startOnly = model.Items.Any(i => WavePrefabVisuals.IsDungeonExitSlot(i.Prefab));
        bool candidate = template.role == DungeonRoomRole.Normal && !template.HasTag(DungeonRoomTags.Rest) && !startOnly;
        if (candidate && (slots < 1 || slots > 2)) sink.Complete($"전투 후보 템플릿인데 목표 자리(4×6)가 {slots}개입니다(1~2개 — 규칙 v4-7).");
    }

    // ── 함정 묶음(규칙 v4-5)·레이저 예고·함정 칸 금지 구역 ──
    static void BundleRules(DungeonRoomTemplateSO template, Model model, List<Placed> traps, Dictionary<Placed, HashSet<Vector2Int>> dangers, List<CraneRoute> cranes, Sink sink)
    {
        sink.Rule = "v4-5 함정 묶음";
        var bundles = new List<(bool row, int spacing, List<Placed> chain)>();
        var loners = new List<Placed>();
        foreach (var group in traps.GroupBy(t => (t.Name, t.Rotation)))
        {
            var left = group.ToList();
            foreach (bool row in new[] { true, false })
            {
                var used = new HashSet<int>();
                foreach (var line in left.GroupBy(t => row ? t.Area.yMin : t.Area.xMin))
                {
                    var sorted = line.OrderBy(t => row ? t.Area.xMin : t.Area.yMin).ToList();
                    var chain = new List<Placed>();
                    int spacing = 0;
                    foreach (var t in sorted)
                    {
                        int pos = row ? t.Area.xMin : t.Area.yMin;
                        if (chain.Count == 0) { chain.Add(t); spacing = 0; continue; }
                        int prev = row ? chain[chain.Count - 1].Area.xMin : chain[chain.Count - 1].Area.yMin;
                        int gap = pos - prev;
                        if ((spacing == 0 && gap > 0 && gap <= BundleGapMax) || (spacing != 0 && gap == spacing)) { spacing = gap; chain.Add(t); continue; }
                        if (chain.Count >= BundleMin) { bundles.Add((row, spacing, chain)); used.UnionWith(chain.Select(c => c.Index)); }
                        chain = new List<Placed> { t };
                        spacing = 0;
                    }
                    if (chain.Count >= BundleMin) { bundles.Add((row, spacing, chain)); used.UnionWith(chain.Select(c => c.Index)); }
                }
                left = left.Where(t => !used.Contains(t.Index)).ToList();
            }
            loners.AddRange(left);
        }
        // templates6(DESIGN_V6 T15 'DropShaft 폭 14 하나'): 구덩이 바닥을 벽에서 벽까지 덮는 단일 분쇄 투입구(길이 설정형·위험 윗줄)는 묶음 하나로 본다(외톨이 아님 — check_templates.py 규칙 20).
        foreach (var t in loners.ToList())
        {
            if (!t.Entry.HasLength || t.Entry.danger != DungeonPropCatalogSO.Danger.TopRow) continue;
            if (!model.Blocked(new Vector2Int(t.Area.xMin - 1, t.Area.yMin)) || !model.Blocked(new Vector2Int(t.Area.xMax, t.Area.yMin))) continue;
            loners.Remove(t);
            bundles.Add((true, t.Area.width, new List<Placed> { t }));
        }
        bool heavy = template.HasTag(DungeonRoomTags.TrapHeavy);
        int lowB = heavy ? 2 : 1, highB = heavy ? 3 : 2;
        if (heavy && (bundles.Count < lowB || bundles.Count > highB)) sink.Firm($"함정 위주 방(꼬리표 '함정 위주')인데 함정 묶음 {bundles.Count}개입니다({lowB}~{highB} — 규칙 v4-5).");
        else if (!heavy && bundles.Count > highB) sink.Firm($"함정 묶음 {bundles.Count}개(일반 방 {lowB}~{highB} — 많으면 꼬리표 '함정 위주').");
        else if (!heavy && bundles.Count == 0 && DungeonTemplateMeta.IsFightTemplate(template)) sink.Advice("함정 묶음이 없습니다(일반 방 1~2 권장 — 규칙 v4-5).");
        foreach (var t in loners) sink.Advice($"{t.Label}: 외톨이 함정입니다(같은 장치 2~5개를 한 줄 등간격으로 묶으세요 — 규칙 v4-5).", 0, t.Index, t.Area.position);
        sink.Rule = "v4-5 레이저 예고";
        foreach (var t in traps)
        {
            if (string.IsNullOrEmpty(t.Entry.warningKey)) continue;
            float value = SettingOr(t.Spawn, t.Entry.warningKey, t.Entry.warningDefault);
            if (value < LaserWarningMin - 1e-4f) sink.Firm($"{t.Label}: 레이저 예고선(예고 {t.Entry.warningKey}) {value:0.##}초 < {LaserWarningMin:0.#}초(규칙 v4-5 — 예고를 켜세요).", 0, t.Index, t.Area.position);
        }
        sink.Rule = "v4-5 함정 묶음";
        foreach (var (row, spacing, chain) in bundles)
        {
            var first = chain[0];
            string at = $"({first.Area.xMin}, {first.Area.yMin})~";
            if (chain.Count > BundleMax) sink.Warn($"{first.Name} 묶음 {at}이 {chain.Count}개입니다({BundleMin}~{BundleMax}개 권장 — 둘로 나누세요).", 0, first.Index, first.Area.position);
            string phaseKey = first.Entry.phaseKey;
            var phases = string.IsNullOrEmpty(phaseKey) ? new List<float>() : chain.Select(c => SettingOr(c.Spawn, phaseKey, 0f)).ToList();
            if (phases.Count > 1)
            {
                int kinds = Enumerable.Range(1, phases.Count - 1).Select(k => Mathf.Round((phases[k] - phases[k - 1]) * 1000f) / 1000f).Distinct().Count();
                if (kinds > 1) sink.Warn($"{first.Name} 묶음 {at} 위상 [{string.Join(", ", phases.Select(p => p.ToString("0.###")))}]이 일정하게 늘지 않습니다(파도처럼 같은 간격으로).", 0, first.Index, first.Area.position);
            }
            int width = row ? first.Area.width : first.Area.height;
            bool saw = first.Entry.danger == DungeonPropCatalogSO.Danger.Circle && first.Entry.dangerExtra > 0f;
            bool gapOk = spacing - width >= TrapSpacingMin && !saw;
            bool phaseOk = phases.Count > 1 && Enumerable.Range(1, phases.Count - 1).Any(k => Mathf.Abs(phases[k] - phases[k - 1]) > 1e-4f);
            var mid = new Vector2(chain.Average(c => (float)c.Area.xMin) + .5f, chain.Average(c => (float)c.Area.yMin) + .5f);
            bool craneOk = cranes.Any(c => c.Points.Any(p => Vector2.Distance(p + Vector2.up * DungeonCranePaths.BodyLift, mid) <= c.Radius + 3f));
            if (!(gapOk || phaseOk || craneOk)) sink.Warn($"{first.Name} 묶음 {at}: 안전 경로(안전 칸 ≥ {TrapSpacingMin}·위상 틈·근처 크레인)를 찾지 못했습니다.", 0, first.Index, first.Area.position);
        }
        // 함정 칸 금지 구역: 출입구 앞·시작 위치·목표/출구/도착 자리·고정 크레인 매달림 몸·생성 구역(빌더 keepOut — 경로 크레인 하차 몸은 T10 쓸기 규칙이 대신한다).
        sink.Rule = "v4-5 함정 자리";
        var keepOut = new HashSet<Vector2Int>(model.DoorZone);
        keepOut.UnionWith(model.StartBody);
        foreach (var item in model.Items.Where(i => i.Objective)) keepOut.UnionWith(item.Cells);
        foreach (var crane in cranes.Where(c => !c.Path)) keepOut.UnionWith(BodyCells(crane.Board));
        foreach (var item in model.Items.Where(i => i.Marker))
            if (DungeonSpawnZone.TryRead(item.Spawn, item.Root - item.Spawn.spawnPosition, out var rect, out _, out _))
                for (int x = Mathf.RoundToInt(rect.xMin); x < Mathf.RoundToInt(rect.xMin) + Mathf.Max(1, Mathf.RoundToInt(rect.width)); x++)
                    for (int y = Mathf.RoundToInt(rect.yMin); y < Mathf.RoundToInt(rect.yMin) + Mathf.Max(1, Mathf.RoundToInt(rect.height)); y++) keepOut.Add(new Vector2Int(x, y));
        foreach (var t in traps)
        {
            var bad = t.Cells.Where(keepOut.Contains).Take(1).ToArray();
            if (bad.Length > 0) sink.Firm($"{t.Label}: 함정 칸이 출입구 앞·시작·목표/출구/도착 자리·고정 크레인 매달림·생성 구역과 겹칩니다(예 ({bad[0].x}, {bad[0].y})).", 0, t.Index, bad[0]);
            var reach = dangers[t].Where(c => !t.Cells.Contains(c) && (model.DoorZone.Contains(c) || model.StartBody.Contains(c))).Take(1).ToArray();
            if (reach.Length > 0) sink.Warn($"{t.Label}: 위험 범위가 출입구 앞·시작 위치에 닿습니다(예 ({reach[0].x}, {reach[0].y})).", 0, t.Index, reach[0]);
        }
    }

    // ── 6차 T9: 함정 정지(방 클리어에 멈춤 — integration6이 traps6 §4-E를 반영) ──
    // 싸움 없는 방(시작 전용·고정 시작 휴식 방 — IsFightTemplate 거짓, 보스 방 제외)의 함정은 방에 들어올 때부터 정지 자세다 → 경고.
    // 예전 '작동 조건' 저장값(hazardPower)은 6차부터 읽지 않는다 → 경고(지워도 됨).
    const string LegacyHazardPowerKey = "hazardPower";
    static void HaltRules(DungeonRoomTemplateSO template, List<Placed> traps, Sink sink)
    {
        if (traps.Count == 0) return;
        sink.Rule = "T9 함정 정지";
        bool fights = template.role == DungeonRoomRole.Boss || DungeonTemplateMeta.IsFightTemplate(template);
        if (!fights)
            sink.Warn($"싸움 없는 방(시작 전용·고정 시작 휴식 방)에 함정이 {traps.Count}개 있습니다 — 방이 처음부터 클리어 상태라 늘 멈춰 있습니다(6차 T9 — 장식이면 다른 Prop으로 바꾸세요).",
                0, traps[0].Index, traps[0].Area.position, traps.SelectMany(t => t.Cells));
        foreach (var t in traps)
        {
            var settings = t.Spawn?.deviceSettings;
            if (settings == null || !settings.Any(s => s.key == LegacyHazardPowerKey)) continue;
            sink.Warn($"{t.Label}: 예전 작동 조건 저장값(hazardPower)이 남아 있습니다 — 6차부터 읽지 않습니다(던전 함정은 방 클리어에 멈춤). 지워도 됩니다.", 0, t.Index, t.Area.position);
        }
    }

    // ── 6차 T10: 함정 간격 ──
    // 정의 D(서는 면): 서는 칸(실내 빈 칸 · 통과 발판 아님 · 아래 받침)의 몸 기둥(그 칸·위 칸)이 함정 칸·위험 범위에 닿으면 위험 칸이다.
    //   같은 행의 이어진 서는 구간에서 서로 다른 함정의 위험 사이 안전 칸이 TrapSpacingMin(4) 미만이면 오류(끝이 벽·낭떠러지인 구간은 세지 않는다).
    // 정의 B(서는 면에 닿지 않는 함정 — 천장·공중·벽): 그 함정과 다른 함정의 차지 칸 사이 체비셰프 빈 칸이 4 미만이면 오류.
    static void TrapSpacingRules(Model model, List<Placed> traps, Dictionary<Placed, HashSet<Vector2Int>> dangers, Sink sink)
    {
        if (traps.Count < 2) return;
        sink.Rule = "T10 함정 간격";
        var stands = model.Interior.Where(c => !model.Blocked(c) && !model.Through(c) && model.Support(c + Vector2Int.down)).ToList();
        var owners = new Dictionary<Vector2Int, List<Placed>>();
        var touches = new HashSet<Placed>();
        foreach (var c in stands)
            foreach (var t in traps)
            {
                var danger = dangers[t];
                if (!danger.Contains(c) && !danger.Contains(c + Vector2Int.up)) continue;
                if (!owners.TryGetValue(c, out var list)) owners[c] = list = new List<Placed>();
                list.Add(t);
                touches.Add(t);
            }
        var reported = new HashSet<(int, int)>();
        foreach (var (y, x0, x1) in RowSegments(stands))
        {
            List<Placed> previous = null;
            int run = 0;
            for (int x = x0; x <= x1; x++)
            {
                if (!owners.TryGetValue(new Vector2Int(x, y), out var set)) { run++; continue; }
                // templates6: 위험이 한 칸에 겹친 서로 다른 함정 쌍과 맞닿은(run 0) 쌍도 안전 칸 0으로 본다(check_templates.py 규칙 17과 같다).
                var within = set.SelectMany((a, k) => set.Skip(k + 1).Where(b => b != a).Select(b => (a, b))).FirstOrDefault();
                if (within.a != null || previous != null && run < TrapSpacingMin)
                {
                    int safe = within.a != null ? 0 : run;
                    var pair = within.a != null ? within : previous.SelectMany(a => set.Where(b => b != a).Select(b => (a, b))).FirstOrDefault();
                    if (pair.a != null && reported.Add((Mathf.Min(pair.a.Index, pair.b.Index), Mathf.Max(pair.a.Index, pair.b.Index))))
                    {
                        var gap = safe > 0 ? Enumerable.Range(x - safe, safe).Select(gx => new Vector2Int(gx, y)).ToArray() : new[] { new Vector2Int(x, y) };
                        string where = safe > 0 ? $"x {x - safe}~{x - 1}" : $"x {x} — 위험이 맞닿거나 겹침";
                        sink.Firm($"함정 {pair.a.Label}와 {pair.b.Label} 사이 서는 칸(행 {y}, {where})의 안전 칸이 {safe}칸입니다({TrapSpacingMin}칸 이상 — 6차 T10 함정 간격).",
                            0, pair.a.Index, gap[0], gap);
                    }
                }
                previous = set;
                run = 0;
            }
        }
        for (int i = 0; i < traps.Count; i++)
            for (int j = i + 1; j < traps.Count; j++)
            {
                var a = traps[i];
                var b = traps[j];
                if (touches.Contains(a) && touches.Contains(b)) continue;
                int gap = ChebyshevGap(a.Cells, b.Cells);
                if (gap >= TrapSpacingMin || !reported.Add((Mathf.Min(a.Index, b.Index), Mathf.Max(a.Index, b.Index)))) continue;
                sink.Firm($"함정 {a.Label}와 {b.Label}의 차지 칸 사이 빈 칸이 {Mathf.Max(0, gap)}칸입니다(서는 면에 닿지 않는 함정은 체비셰프 {TrapSpacingMin}칸 이상 — 6차 T10 함정 간격).",
                    0, a.Index, a.Area.position, a.Cells.Concat(b.Cells));
            }
    }

    // ── 6차 T10: 경로 크레인 쓸기 상자(0.5칸 표본 · 발 ~ 발 + 4 · 폭 2) ∩ (함정 칸 ∪ 위험 범위 ∪ 둘레 1칸) = ∅ ──
    // 예전 규칙(경로 꼭짓점 하차 몸 ∩ 함정 칸)을 포함·대체한다. 고정 크레인 붙기 직선은 대상이 아니다(고정 크레인 매달림 몸 ∩ 함정 칸은 함정 자리 규칙이 본다).
    static void CraneTrapRules(List<CraneRoute> cranes, List<Placed> traps, Dictionary<Placed, HashSet<Vector2Int>> dangers, Sink sink)
    {
        if (traps.Count == 0) return;
        sink.Rule = "T10 크레인 경로";
        foreach (var crane in cranes.Where(c => c.Path))
            foreach (var t in traps)
            {
                var forbidden = new HashSet<Vector2Int>();
                foreach (var c in dangers[t])
                    for (int dx = -CraneTrapMargin; dx <= CraneTrapMargin; dx++)
                        for (int dy = -CraneTrapMargin; dy <= CraneTrapMargin; dy++) forbidden.Add(new Vector2Int(c.x + dx, c.y + dy));
                var hit = crane.Sweep.Where(forbidden.Contains).OrderBy(c => c.y).ThenBy(c => c.x).ToList();
                if (hit.Count == 0) continue;
                sink.Firm($"{crane.Label}: 경로 크레인 쓸기 상자(발 ~ 발 + {DungeonCranePaths.SweepHeight:0}칸, 폭 {DungeonCranePaths.SweepWidth:0}칸)가 함정 {t.Label}의 칸·위험 범위(둘레 {CraneTrapMargin}칸 포함)와 겹칩니다"
                    + $"(예 ({hit[0].x}, {hit[0].y}) — 6차 T10: 크레인이 옮기는 길에는 함정을 두지 않습니다).", 0, crane.Item.Index, hit[0], hit);
            }
    }

    // ── 파괴 Prop(규칙 v4-10) ──
    static readonly HashSet<DungeonRoomShape> HugeShapes = new HashSet<DungeonRoomShape> { DungeonRoomShape.HorizontalLong, DungeonRoomShape.VerticalLong, DungeonRoomShape.Square };
    static void BreakableRules(DungeonRoomTemplateSO template, Model model, Sink sink)
    {
        sink.Rule = "v4-10 파괴 Prop";
        var loose = model.Items.Where(i => i.Entry != null && i.Entry.kind == DungeonPropCatalogSO.Kind.Destructible).ToList();
        (int lo, int hi, string name) range = template.role == DungeonRoomRole.Boss ? (2, 4, "보스") : HugeShapes.Contains(template.shape) ? (10, 14, "초대형")
            : template.sizeClass == DungeonRoomSize.Small ? (3, 5, "작은 방") : (6, 10, "큰 방");
        if (loose.Count < range.lo || loose.Count > range.hi) sink.Advice($"파괴 Prop {loose.Count}개({range.name} 권장 {range.lo}~{range.hi} — 규칙 v4-10).");
        foreach (var item in loose.Where(i => i.Entry.gravity))
            for (int x = item.Area.xMin; x < item.Area.xMax; x++)
            {
                if (!model.Owner.TryGetValue(new Vector2Int(x, item.Area.yMin - 1), out var below) || below == item || below.Entry == null || !below.Entry.gravity) continue;
                sink.Firm($"{item.Label}: 중력형 {item.Name}이 중력형 {below.Name} 위에 쌓였습니다(쌓기 금지 — 지형·고정 Prop 위에만).", 0, item.Index, new Vector2Int(x, item.Area.yMin));
                break;
            }
        if (loose.Count > 1)
            foreach (var item in loose)
            {
                bool near = loose.Any(o => o != item && item.Cells.Any(a => o.Cells.Any(b => Chebyshev(a, b) <= 4)));
                if (!near) sink.Advice($"{item.Label}: 다른 파괴 Prop과 4칸 안에 없습니다(적재 구역으로 묶으세요).", 0, item.Index, item.Area.position);
            }
    }

    // ── 수직 여유(규칙 v4-1 — 빌더 Clearance): 서는 칸에서 위 플랫폼 아랫면까지 빈 칸 ≥ 5인 칸이 75% 이상(75~80% 경고) ──
    static List<StandCell> ClearanceRule(Model model, Sink sink)
    {
        sink.Rule = "v4-1 수직 여유";
        var box = model.InteriorBox;
        int top = box.yMax;
        var list = new List<StandCell>();
        for (int x = box.xMin; x < box.xMax; x++)
            for (int y = box.yMin; y <= top; y++)
            {
                var c = new Vector2Int(x, y);
                if (!model.Interior.Contains(c) || model.Support(c) || !model.Support(new Vector2Int(x, y - 1))) continue;
                int strict = 0, yy = y;
                while (!model.Support(new Vector2Int(x, yy)) && yy <= top) { strict++; yy++; }
                bool ceiling = yy >= box.yMax;
                int phys = 0;
                yy = y;
                while (!model.Blocked(new Vector2Int(x, yy)) && yy <= top) { phys++; yy++; }
                if (phys < 2) continue;
                list.Add(new StandCell(x, y, strict, ceiling));
            }
        if (list.Count == 0) { sink.Firm("서는 칸이 없습니다(수직 여유를 잴 수 없음 — 규칙 v4-1)."); return list; }
        var ample = list.Where(s => s.Strict >= ClearanceCells).ToList();
        float ratio = ample.Count / (float)list.Count;
        var narrow = RowSegments(list.Where(s => s.Strict < ClearanceCells).Select(s => new Vector2Int(s.X, s.Y)));
        string narrowText = narrow.Count == 0 ? "" : " 좁은 구간: " + string.Join(" / ", narrow.Take(6).Select(g => $"y {g.y}: x {g.x0}~{g.x1}")) + (narrow.Count > 6 ? " …" : "");
        var narrowCells = list.Where(s => s.Strict < ClearanceCells).Select(s => new Vector2Int(s.X, s.Y));
        string head = $"수직 여유: 서는 칸 {list.Count}개 중 위로 빈 칸 {ClearanceCells}칸 이상이 {ample.Count}개({Mathf.RoundToInt(ratio * 100f)}%)입니다";
        if (ratio < ClearancePass - 1e-6f) sink.Firm(head + " — 75% 미만(규칙 v4-1 — 빌더 오류 기준)." + narrowText, cells: narrowCells);
        else if (ratio < ClearanceComfort) sink.Warn(head + " — 75~80%(통과, 경고 구간)." + narrowText, cells: narrowCells);
        var low = list.Where(s => s.UnderCeiling && s.Strict < ClearanceCells).ToList();
        if (low.Count > 0)
            sink.Warn($"최상층 천장 여유 < {ClearanceCells}인 서는 칸 {low.Count}칸(예 ({low[0].X}, {low[0].Y}) 여유 {low[0].Strict} — 실내 높이 6k − 1 권장, 지금 {box.height}).", -1, -1, new Vector2Int(low[0].X, low[0].Y));
        return list;
    }

    // ── 도달성(규칙 v4-2 — 빌더 Explore): 걷기·1회 점프·점프대·크레인(경로 크레인은 양 끝 도크)으로 출입구·자리 표식·크레인·넉넉한 바닥에 닿는지 ──
    static void ReachRules(Model model, List<CraneRoute> cranes, List<StandCell> clearance, Sink sink)
    {
        sink.Rule = "v4-2 도달성";
        var (stands, hangs) = Explore(model, model.StartCell, JumpRise, JumpReach, cranes);
        if (stands.Count == 0) return;   // 시작 좌표가 설 수 없는 자리면 실내 검사가 이미 알린다
        var (stands2, hangs2) = Explore(model, model.StartCell, DoubleRise, DoubleReach, cranes);
        var reached = Covered(stands);
        var reached2 = Covered(stands2);
        void Need(string label, bool ok1, bool ok2, int spawn, Vector2Int? cell)
        {
            if (ok1 || ok2) return;
            sink.Firm($"{label}: 시작점에서 닿지 않습니다(1회 점프·점프대·크레인·2단 점프로도 — 규칙 v4-2 도달성).", spawn >= 0 ? 0 : -1, spawn, cell);
        }
        foreach (var door in model.Doors)
        {
            var stand = new Vector2Int(door.side == DungeonDoorSide.Right ? door.wallFaceX - 2 : door.wallFaceX, door.floorY);
            Need($"출입구 {door.spawnIndex + 1}번({(door.side == DungeonDoorSide.Right ? "오른쪽" : "왼쪽")} 벽 · 단위 {door.unit})", stands.Contains(stand), stands2.Contains(stand), door.spawnIndex, stand);
        }
        foreach (var item in model.Items.Where(i => i.Objective))
        {
            var xs = Enumerable.Range(item.Area.xMin, Mathf.Max(1, item.Area.width - 1)).Select(x => new Vector2Int(x, item.Area.yMin)).ToArray();
            Need(item.Label, xs.Any(stands.Contains), xs.Any(stands2.Contains), item.Index, item.Area.position);
        }
        for (int i = 0; i < cranes.Count; i++)
        {
            int index = i;
            Need(cranes[i].Label + " 매달림", hangs.Any(h => h.x == index), hangs2.Any(h => h.x == index), cranes[i].Item.Index, Vector2Int.FloorToInt(cranes[i].Board));
        }
        foreach (var (y, x0, x1) in RowSegments(clearance.Where(s => s.Strict >= ClearanceCells).Select(s => new Vector2Int(s.X, s.Y))).Where(g => g.x1 - g.x0 + 1 >= FloorSegmentCells))
        {
            var cells = Enumerable.Range(x0, x1 - x0 + 1).Select(x => new Vector2Int(x, y)).ToArray();
            Need($"넉넉한 바닥 y {y} x {x0}~{x1}", cells.Any(reached.Contains), cells.Any(reached2.Contains), -1, cells[0]);
        }
    }

    // 도달성 BFS(빌더 Explore와 같은 계산 — 2칸 폭 몸, 칸 자료만 쓴다).
    static bool Fits(Model m, int x, int y)
        => !m.Blocked(new Vector2Int(x, y)) && !m.Blocked(new Vector2Int(x + 1, y)) && !m.Blocked(new Vector2Int(x, y + 1)) && !m.Blocked(new Vector2Int(x + 1, y + 1));
    static bool Grounded(Model m, int x, int y) => m.Support(new Vector2Int(x, y - 1)) || m.Support(new Vector2Int(x + 1, y - 1));
    static int? Land(Model m, int x, int y)
    {
        if (!Fits(m, x, y)) return null;
        for (int i = 0; i < 120; i++)
        {
            if (Grounded(m, x, y)) return y;
            if (!Fits(m, x, y - 1)) return y;
            y--;
        }
        return null;
    }
    static IEnumerable<Vector2Int> JumpMoves(Model m, int x, int y, int rise, int reach)
    {
        int topH = y;
        for (int h = y + 1; h <= y + rise; h++)
        {
            if (!Fits(m, x, h)) break;
            topH = h;
        }
        for (int h = y + 1; h <= topH; h++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                if (dx == 0) continue;
                int step = dx > 0 ? 1 : -1;
                bool ok = true;
                for (int i = step; i != dx + step; i += step)
                    if (!Fits(m, x + i, h)) { ok = false; break; }
                if (!ok) continue;
                int? ly = Land(m, x + dx, h);
                if (ly.HasValue) yield return new Vector2Int(x + dx, ly.Value);
            }
    }
    static IEnumerable<Vector2Int> WalkMoves(Model m, int x, int y)
    {
        foreach (int dx in new[] { -1, 1 })
        {
            int? ly = Land(m, x + dx, y);
            if (ly.HasValue) yield return new Vector2Int(x + dx, ly.Value);
        }
        var below = new[] { new Vector2Int(x, y - 1), new Vector2Int(x + 1, y - 1) };
        if (below.All(c => m.Through(c) || !m.Support(c)) && below.Any(m.Through) && !below.Any(m.Blocked))
        {
            int? ly = Land(m, x, y - 1);
            if (ly.HasValue) yield return new Vector2Int(x, ly.Value);
        }
    }
    // 몸(1.5×1.5) 중심을 a → b로 곧게 옮길 때 고체(지형·고체 Prop)와 겹치지 않는지(OneWay는 통과) — 붙기 시야.
    static bool BodyLineClear(Model m, Vector2 a, Vector2 b, Dictionary<(int, int, int, int), bool> cache)
    {
        var key = (Mathf.RoundToInt(a.x * 100), Mathf.RoundToInt(a.y * 100), Mathf.RoundToInt(b.x * 100), Mathf.RoundToInt(b.y * 100));
        if (cache.TryGetValue(key, out bool cached)) return cached;
        int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / .25f));
        const float half = CraneBodySize * .5f - .05f;
        bool clear = true;
        for (int i = 0; i <= n && clear; i++)
        {
            var p = Vector2.Lerp(a, b, i / (float)n);
            for (int cx = Mathf.FloorToInt(p.x - half); cx <= Mathf.FloorToInt(p.x + half) && clear; cx++)
                for (int cy = Mathf.FloorToInt(p.y - half); cy <= Mathf.FloorToInt(p.y + half); cy++)
                    if (m.Blocked(new Vector2Int(cx, cy))) { clear = false; break; }
        }
        cache[key] = clear;
        return clear;
    }
    static (HashSet<Vector2Int> stands, HashSet<Vector2Int> hangs) Explore(Model m, Vector2Int start, int rise, int reach, List<CraneRoute> cranes)
    {
        var stands = new HashSet<Vector2Int>();
        var hangs = new HashSet<Vector2Int>();
        var cache = new Dictionary<(int, int, int, int), bool>();
        var queue = new Queue<(bool hang, Vector2Int at)>();
        int minX = m.InteriorBox.xMin - 1, maxX = m.InteriorBox.xMax - 1;
        int? s = Land(m, start.x, start.y);
        if (!s.HasValue) return (stands, hangs);
        void PushStand(Vector2Int n)
        {
            if (n.x >= minX && n.x <= maxX && stands.Add(n)) queue.Enqueue((false, n));
        }
        void Zips(Vector2 center, int skip)
        {
            for (int ci = 0; ci < cranes.Count; ci++)
            {
                if (ci == skip) continue;
                var crane = cranes[ci];
                bool ok = false;
                for (int dock = 0; dock < (crane.Path && crane.Corners.Count >= 2 ? 2 : 1) && !ok; dock++)
                {
                    var target = (dock == 0 ? crane.Board : crane.Corners[crane.Corners.Count - 1]) + Vector2.up * DungeonCranePaths.BodyLift;
                    ok = Vector2.Distance(center, target) <= crane.Radius + 1e-4f && BodyLineClear(m, center, target, cache);
                }
                if (!ok) continue;
                for (int k = 0; k < (crane.Path ? crane.Points.Count : 1); k++)
                    if (hangs.Add(new Vector2Int(ci, k))) queue.Enqueue((true, new Vector2Int(ci, k)));
            }
        }
        PushStand(new Vector2Int(start.x, s.Value));
        int guard = 0;
        while (queue.Count > 0 && guard++ < 200000)
        {
            var (hang, at) = queue.Dequeue();
            if (!hang)
            {
                foreach (var n in WalkMoves(m, at.x, at.y)) PushStand(n);
                int r = rise;
                foreach (var c in new[] { new Vector2Int(at.x, at.y - 1), new Vector2Int(at.x + 1, at.y - 1), at, new Vector2Int(at.x + 1, at.y) })
                    if (m.Pads.TryGetValue(c, out int padRise)) r = Mathf.Max(r, padRise);
                foreach (var n in JumpMoves(m, at.x, at.y, r, reach)) PushStand(n);
                Zips(new Vector2(at.x + 1, at.y + DungeonCranePaths.BodyLift), -1);
            }
            else
            {
                var feet = cranes[at.x].Points[at.y];
                int xl = Mathf.FloorToInt(feet.x - .5f), yc = Mathf.FloorToInt(feet.y + 1e-4f);
                if (Fits(m, xl, yc))
                {
                    int? ly = Land(m, xl, yc);
                    if (ly.HasValue) PushStand(new Vector2Int(xl, ly.Value));
                    foreach (var n in JumpMoves(m, xl, yc, rise, reach)) PushStand(n);   // 매달림에서 점프해 내림
                }
                Zips(feet + Vector2.up * DungeonCranePaths.BodyLift, at.x);
            }
        }
        return (stands, hangs);
    }
    static HashSet<Vector2Int> Covered(HashSet<Vector2Int> stands)
    {
        var cells = new HashSet<Vector2Int>();
        foreach (var s in stands) { cells.Add(s); cells.Add(s + Vector2Int.right); }
        return cells;
    }

    // ───────── 격자 Tool·템플릿 편집기용 공개 도우미 ─────────
    /// <summary>
    /// 배치 미리보기(커서 유령)·배치 막기용 판정: 카탈로그 Prop을 rotation으로 cells에 둘 때 허용 회전·설치면 문제. terrain = (칸 → 셀 종류) 지형,
    /// propSolid·propSupport = 같은 Wave(템플릿은 맵 층)에 놓인 다른 항목의 고체·윗면 칸. 문제가 없거나 카탈로그 밖이면 null.
    /// </summary>
    public static string PlacementProblem(GameObject prefab, RectInt cells, int rotation, Func<Vector2Int, bool> terrainSolid, Func<Vector2Int, bool> terrainSupport,
        Func<Vector2Int, bool> propSolid, Func<Vector2Int, bool> propSupport)
    {
        var entry = DungeonPropCatalogSO.Lookup(prefab);
        if (entry == null) return null;
        if (!entry.Allows(rotation)) return $"허용 회전({AllowedText(entry)})이 아닙니다";
        bool Blocked(Vector2Int c) => terrainSolid(c) || propSolid(c);
        bool Support(Vector2Int c) => terrainSupport(c) || propSolid(c) || propSupport(c);
        return DungeonPropCatalogSO.MountProblem(entry, cells, rotation, terrainSolid, Blocked, Support);
    }
    /// <summary>카탈로그 충돌 규칙으로 센 항목 하나의 고체·윗면 칸(카탈로그 밖이면 Prefab 충돌체). 격자 Tool 미리보기가 받침 칸을 셀 때 쓴다.</summary>
    public static void CollisionCells(WaveSpawnInfo spawn, Vector2 offset, HashSet<Vector2Int> solid, HashSet<Vector2Int> support)
    {
        var prefab = spawn?.prefab;
        if (prefab == null || prefab.GetComponent<EnemyController>() != null || prefab.GetComponent<WaveTerrainCellView>() != null) return;
        var root = spawn.spawnPosition + offset;
        int rotation = DungeonPropCatalogSO.Normalize(spawn.rotationSteps);
        var entry = DungeonPropCatalogSO.Lookup(prefab);
        if (entry == null)
        {
            if (WavePrefabVisuals.IsDungeonSlot(prefab) || WavePrefabVisuals.IsDungeonSpawnZone(prefab)) return;
            SolidCells(prefab, root, rotation, solid);
            SolidCells(prefab, root, rotation, support, OneWayLayer());
            return;
        }
        var area = WavePrefabVisuals.OccupiedCells(prefab, root, rotation, spawn.deviceSettings);
        switch (entry.collision)
        {
            case DungeonPropCatalogSO.Collision.Solid: foreach (var c in CellsOf(area)) solid.Add(c); break;
            case DungeonPropCatalogSO.Collision.OneWay: for (int x = area.xMin; x < area.xMax; x++) support.Add(new Vector2Int(x, area.yMax - 1)); break;
            case DungeonPropCatalogSO.Collision.Base: for (int x = area.xMin; x < area.xMax; x++) solid.Add(new Vector2Int(x, area.yMin)); break;
        }
    }
}
