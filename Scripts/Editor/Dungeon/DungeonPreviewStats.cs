using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>생성 미리보기 통계(편집기 표시·회귀용).</summary>
public readonly struct DungeonPreviewSummary
{
    public readonly int Rooms, Combat, Small, Horizontal, Vertical, Mixed, Links, SameOrientationLinks, Rest, TeleportPairs;
    public DungeonPreviewSummary(int rooms, int combat, int small, int horizontal, int vertical, int mixed, int links, int sameOrientationLinks, int rest, int teleportPairs)
    {
        Rooms = rooms; Combat = combat; Small = small; Horizontal = horizontal; Vertical = vertical; Mixed = mixed;
        Links = links; SameOrientationLinks = sameOrientationLinks; Rest = rest; TeleportPairs = teleportPairs;
    }
    public string SizeLine => $"작은 방 {Small}/전투 {Combat} · 휴식 방 {Rest} · 텔레포트 {TeleportPairs}쌍";
    public string OrientationLine => $"방향: 수평 {Horizontal} · 수직 {Vertical} · 혼합 {Mixed} · 같은 방향 이웃 {SameOrientationLinks}/{Links}";
}

public static class DungeonPreviewStats
{
    /// <summary>작은 방은 전투 방 중에서, 방향·휴식은 모든 방(시작·보스 포함)에서 센다. 연결은 방 쌍마다 한 번(같은 쌍의 고리 연결도 따로 센다).</summary>
    public static DungeonPreviewSummary Summarize(DungeonLayout layout)
    {
        if (layout == null) return default;
        int combat = 0, small = 0, horizontal = 0, vertical = 0, mixed = 0, links = 0, same = 0, rest = 0, rooms = 0;
        foreach (var room in layout.rooms)
        {
            if (room == null) continue;
            rooms++;
            var template = room.template;
            if (room.kind == DungeonRoomKind.Combat)
            {
                combat++;
                if (template != null && template.sizeClass == DungeonRoomSize.Small) small++;
            }
            var orientation = template != null ? template.orientation : DungeonRoomOrientation.Mixed;
            if (orientation == DungeonRoomOrientation.Horizontal) horizontal++;
            else if (orientation == DungeonRoomOrientation.Vertical) vertical++;
            else mixed++;
            if (template != null && template.HasTag(DungeonRoomTags.Rest)) rest++;
            foreach (var link in room.links)
            {
                if (link.toRoom <= room.id) continue;
                links++;
                var other = layout.Room(link.toRoom)?.template;
                if (template != null && other != null && template.orientation != DungeonRoomOrientation.Mixed && template.orientation == other.orientation) same++;
            }
        }
        return new DungeonPreviewSummary(rooms, combat, small, horizontal, vertical, mixed, links, same, rest, layout.teleports.Count);
    }

    // ───────── 3차: 텔레포트 거리·목표 배치 ─────────
    /// <summary>방 id별 시작 방에서의 연결 거리(닿지 않으면 -1). 생성기와 같은 BFS(DungeonGenerator.Distances).</summary>
    public static int[] StartDistances(DungeonLayout layout) => layout != null && layout.rooms.Count > 0 ? Reachable(DungeonGenerator.Distances(layout)) : Array.Empty<int>();
    /// <summary>from 방에서의 연결 거리(닿지 않으면 -1).</summary>
    public static int[] DistancesFrom(DungeonLayout layout, int from) => layout != null ? Reachable(DungeonGenerator.DistancesFrom(layout, from)) : Array.Empty<int>();
    // 생성기는 닿지 않는 방을 int.MaxValue로 둔다. 표시·비교를 위해 -1로 바꾼다.
    private static int[] Reachable(int[] distance)
    {
        for (int i = 0; i < distance.Length; i++) if (distance[i] == int.MaxValue) distance[i] = -1;
        return distance;
    }
    /// <summary>텔레포트 짝의 연결 거리(BFS, 닿지 않으면 -1)와 던전 격자 거리(단위 칸 맨해튼).</summary>
    public static (int graph, int grid) TeleportDistance(DungeonLayout layout, DungeonTeleportLink pair)
    {
        var a = layout?.Room(pair.roomA);
        var b = layout?.Room(pair.roomB);
        if (a == null || b == null) return (-1, -1);
        var distance = DistancesFrom(layout, pair.roomA);
        return (pair.roomB < distance.Length ? distance[pair.roomB] : -1, DungeonGenerator.GridDistance(a, b));
    }
    public static string DistanceText(int distance) => distance >= 0 ? distance.ToString() : "닿지 않음";
    /// <summary>'텔레포트 거리: 방 0↔5 연결 5 · 격자 3 / …'. 설정의 최소 거리보다 작으면 '!'. 짝이 없으면 null.</summary>
    public static string TeleportLine(DungeonLayout layout, DungeonWaveSettings settings)
    {
        if (layout == null || layout.teleports.Count == 0) return null;
        var parts = new List<string>();
        foreach (var pair in layout.teleports)
        {
            var (graph, grid) = TeleportDistance(layout, pair);
            bool shortGraph = settings != null && graph >= 0 && graph < settings.teleportMinGraphDistance;
            bool shortGrid = settings != null && grid >= 0 && grid < settings.teleportMinGridDistance;
            parts.Add($"방 {pair.roomA}↔{pair.roomB} 연결 {DistanceText(graph)}{(shortGraph ? "!" : "")} · 격자 {grid}{(shortGrid ? "!" : "")}");
        }
        string limits = settings != null ? $" (최소 연결 {settings.teleportMinGraphDistance} · 격자 {settings.teleportMinGridDistance})" : "";
        return "텔레포트 거리" + limits + ": " + string.Join(" / ", parts);
    }
    /// <summary>
    /// '목표 3/3(필요 3) · 시작 거리 4/6/7 · 목표 방 최소 간격 3'. 목표가 없고 클리어 조건도 목표가 아니면 null.
    /// 필요 수는 목표 SO의 RequiredCount(놓은 수). 레버 목표면 놓은 수/레버 수로 보인다.
    /// </summary>
    public static string ObjectiveLine(DungeonLayout layout, DungeonWaveSettings settings)
    {
        if (layout == null) return null;
        bool expected = settings != null && settings.clearCondition == DungeonClearCondition.Objective;
        if (layout.objectives.Count == 0 && !expected) return null;
        int placed = layout.objectives.Count;
        var objective = settings?.objective;
        int wanted = objective is LeverObjectiveSO lever ? lever.count : placed;
        int required = placed;
        try { if (objective != null) required = objective.RequiredCount(placed); } catch (Exception) { /* 파생 SO 계산 실패는 놓은 수로 본다. */ }
        if (placed == 0) return $"목표 0/{wanted}개 — 목표를 놓지 못했습니다(생성 알림 확인). 실행 때는 '모든 전투 방 클리어'로 대신합니다.";
        var start = StartDistances(layout);
        var rooms = layout.objectives.Select(o => o.room).Distinct().ToList();
        string starts = string.Join("/", layout.objectives.OrderBy(o => o.index).Select(o => o.room >= 0 && o.room < start.Length ? DistanceText(start[o.room]) : "?"));
        int spacing = int.MaxValue;
        for (int i = 0; i < rooms.Count; i++)
        {
            var from = DistancesFrom(layout, rooms[i]);
            for (int j = i + 1; j < rooms.Count; j++)
                if (rooms[j] < from.Length && from[rooms[j]] >= 0) spacing = Mathf.Min(spacing, from[rooms[j]]);
        }
        string spacingText = rooms.Count < 2 ? "" : $" · 목표 방 최소 간격 {(spacing == int.MaxValue ? "닿지 않음" : spacing.ToString())}";
        string sameRoom = rooms.Count < placed ? $" · 같은 방에 {placed - rooms.Count}개 더" : "";
        return $"목표 {placed}/{wanted}개(필요 {required}) · 시작 거리 {starts}{spacingText}{sameRoom}";
    }

    // ───────── 4차: 시작 방 출구·동적 구성 예상 ─────────
    /// <summary>
    /// '출구: 시작 방 0(템플릿) 출구 자리 1'. 목표 SO가 '시작 방 출구'가 아니면 null. 시작 방에 출구 자리 표식이 없으면 missing = true(오류 문장).
    /// </summary>
    public static string ExitLine(DungeonLayout layout, DungeonWaveSettings settings, out bool missing)
    {
        missing = false;
        if (layout == null || settings?.objective == null || settings.objective.finish != DungeonObjectiveFinish.ExitInStartRoom) return null;
        var start = layout.Room(layout.startRoom);
        if (start?.template == null) { missing = true; return "출구: 시작 방을 찾지 못했습니다."; }
        int slots = DungeonTemplateMeta.ExitSlots(start.template).Count;
        if (slots == 0) { missing = true; return $"출구: 시작 방 {start.id}({start.template.name})에 출구 자리 표식이 없어 출구 문을 세우지 못합니다(시작 후보를 출구 자리 있는 템플릿으로)."; }
        return $"출구 ▣: 시작 방 {start.id}({start.template.name}) · 출구 자리 {slots}" + (slots > 1 ? "(첫 표식 사용)" : "") + $" · 문 {(settings.objective.ExitPrefab != null ? settings.objective.ExitPrefab.name : "없음")}";
    }
    /// <summary>방 하나의 동적 구성 예상: '빈 칸 520 · 예상 4/7/9/12/14(입문→특급)'. 측정하지 못하면 이유.</summary>
    public static string DynamicRoomText(DungeonRoomTemplateSO template, DungeonEncounterRulesSO rules)
    {
        if (template == null || rules == null) return "규칙 없음";
        if (!DungeonTemplateValidation.TryMeasure(template, out int empty, out var interior)) return "빈 칸을 재지 못함(실내가 닫히지 않음)";
        var counts = Enumerable.Range(0, DungeonEncounterRulesSO.LevelCount).Select(l => rules.TargetUnits((DungeonThreatLevel)l, empty, false)).ToArray();
        int bulwark = rules.bulwark != null ? rules.bulwark.MaxFor(DungeonThreatLevel.Extreme, interior.height, empty) : 0;
        return $"빈 칸 {empty} · 예상 {string.Join("/", counts)}(입문→특급)" + (bulwark > 0 ? $" · Bulwark 최대 {bulwark}(특급)" : "");
    }
    /// <summary>
    /// 동적 구성 요약: '동적 구성: 전투 방 4 · 빈 칸 평균 480 · 방당 예상 입문 3 ~ 특급 13(합 12 ~ 52)'. 손 배치 구성이면 null, 규칙이 없으면 경고 문장.
    /// </summary>
    public static string DynamicLine(DungeonLayout layout, DungeonWaveSettings settings)
    {
        if (layout == null || settings == null || settings.encounterMode != DungeonEncounterMode.Dynamic) return null;
        var rules = settings.encounterRules;
        if (rules == null) return "동적 구성인데 적 생성 규칙이 없어 손 배치 구성으로 대신합니다.";
        var combat = layout.rooms.Where(r => r != null && r.kind == DungeonRoomKind.Combat && r.template != null).ToList();
        if (combat.Count == 0) return "동적 구성: 전투 방 없음";
        var empty = new List<int>();
        foreach (var room in combat) if (DungeonTemplateValidation.TryMeasure(room.template, out int cells, out _)) empty.Add(cells);
        if (empty.Count == 0) return "동적 구성: 빈 칸을 잰 방이 없습니다(실내가 닫히지 않음).";
        int Total(DungeonThreatLevel level) => empty.Sum(a => rules.TargetUnits(level, a, false));
        int low = Total(DungeonThreatLevel.Intro), high = Total(DungeonThreatLevel.Extreme);
        return $"동적 구성: 전투 방 {combat.Count} · 빈 칸 평균 {Mathf.RoundToInt((float)empty.Average())} · 방당 예상 입문 {Mathf.RoundToInt(low / (float)empty.Count)} ~ 특급 {Mathf.RoundToInt(high / (float)empty.Count)}(합 {low} ~ {high}) · 단계는 첫 클리어 방 수로 오름";
    }
}
