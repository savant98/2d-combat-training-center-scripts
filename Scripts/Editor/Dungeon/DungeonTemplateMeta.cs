using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>방 템플릿 메타(DESIGN_V2: 크기 등급·방향·배경·벽면 키트·꼬리표)의 표시 이름과 판정. 격자 편집기 패널·미리보기·검사가 같이 쓴다.</summary>
public static class DungeonTemplateMeta
{
    public static readonly string[] SizeLabels = { "큰 방", "작은 방" };
    public static readonly string[] OrientationLabels = { "수평", "수직", "혼합" };
    public static readonly string[] BackdropLabels = { "자동 (작은 방 = 정적 벽면 · 큰 방 = 시차 배경)", "시차 배경 (큰 Parallax)", "정적 벽면" };
    public static readonly string[] KitLabels = { "없음", "배관", "환풍 덕트", "전선 트렁크", "설비 벽면" };
    // 4차: 꼬리표 'NoCrane'(크레인 없음 — 휴식 방·피스톤 복도만, 템플릿 규칙 v4-3)·'TrapHeavy'(함정 위주 — 묶음 2~3, 규칙 v4-5).
    // 통합 4차에 계약 열거형 DungeonRoomTags 끝에 두 값을 더했다(빌더가 사양 꼬리표를 에셋에 그대로 옮긴다).
    public static readonly DungeonRoomTags[] TagOrder = BuildTagOrder();
    public static readonly string[] TagLabels = TagOrder.Select(t => t == DungeonRoomTags.Rest ? "휴식" : t == DungeonRoomTags.TeleportHub ? "텔레포트 허브" : t == DungeonRoomTags.TeleportNode ? "텔레포트 끝점"
        : t == DungeonRoomTags.DeadEnd ? "막다른 방" : t == DungeonRoomTags.NoStart ? "시작 불가" : t == DungeonRoomTags.TrapHeavy ? "함정 위주" : "크레인 없음").ToArray();
    static DungeonRoomTags[] BuildTagOrder()
    {
        var order = new List<DungeonRoomTags> { DungeonRoomTags.Rest, DungeonRoomTags.TeleportHub, DungeonRoomTags.TeleportNode, DungeonRoomTags.DeadEnd, DungeonRoomTags.NoStart };
        if (TryNoCraneTag(out var noCrane)) order.Add(noCrane);
        order.Add(DungeonRoomTags.TrapHeavy);
        return order.ToArray();
    }
    /// <summary>NoCrane 꼬리표를 쓸 수 있는 휴식 방 밖 템플릿(빌더 NoCraneAllowedIds·검사기 NO_CRANE_IDS와 같다).</summary>
    public static readonly string[] NoCraneAllowedNames = { "CombatPistonCorridor" };
    /// <summary>DungeonRoomTags에 'NoCrane' 값이 정의돼 있으면 그 값.</summary>
    public static bool TryNoCraneTag(out DungeonRoomTags tag)
    {
        tag = DungeonRoomTags.None;
        return Enum.TryParse("NoCrane", false, out tag) && tag != DungeonRoomTags.None && Enum.IsDefined(typeof(DungeonRoomTags), tag);
    }
    /// <summary>크레인 없음 예외(규칙 v4-3): 꼬리표 NoCrane(정의돼 있으면) 또는 컨셉 한 줄에 'NoCrane'이 적혀 있음(꼬리표 값이 계약에 생기기 전의 대신 표기).</summary>
    public static bool HasNoCraneTag(DungeonRoomTemplateSO template)
        => template != null && (TryNoCraneTag(out var tag) && template.HasTag(tag) || (template.concept ?? "").IndexOf("NoCrane", StringComparison.OrdinalIgnoreCase) >= 0);

    static string Pick(string[] names, int index) => names[Mathf.Clamp(index, 0, names.Length - 1)];
    public static string SizeName(DungeonRoomSize size) => Pick(SizeLabels, (int)size);
    public static string OrientationName(DungeonRoomOrientation orientation) => Pick(OrientationLabels, (int)orientation);
    public static string BackdropName(DungeonBackdropMode mode) => mode == DungeonBackdropMode.Parallax ? "시차 배경" : mode == DungeonBackdropMode.Wall ? "정적 벽면" : "자동";
    public static string KitName(DungeonWallKit kit) => Pick(KitLabels, (int)kit);
    public static string TagsName(DungeonRoomTags tags)
    {
        var names = new List<string>();
        for (int i = 0; i < TagOrder.Length; i++) if ((tags & TagOrder[i]) != 0) names.Add(TagLabels[i]);
        return names.Count == 0 ? "없음" : string.Join("·", names);
    }
    /// <summary>한 줄 요약: 크기 · 방향 · 배경(자동이면 푼 값과 표시) · 키트 · 꼬리표 · 던전당 최대 수.</summary>
    public static string Summary(DungeonRoomTemplateSO template)
    {
        if (template == null) return "";
        var backdrop = template.ResolvedBackdrop;
        string text = $"{SizeName(template.sizeClass)} · 방향 {OrientationName(template.orientation)} · 배경 {BackdropName(backdrop)}"
            + (template.backdropMode == DungeonBackdropMode.Auto ? "(자동)" : "")
            + (backdrop == DungeonBackdropMode.Wall ? $" · 벽면 {KitName(template.wallKit)}" : "")
            + $" · 꼬리표 {TagsName(template.tags)}";
        if (template.maxPerFloor > 0) text += $" · 던전당 최대 {template.maxPerFloor}";
        int slots = ObjectiveSlots(template).Count;
        if (slots > 0) text += $" · 목표 자리 {slots}";
        // 4차: 출구 자리·생성 구역·크레인 수.
        int exits = ExitSlots(template).Count, zones = SpawnZones(template).Count, cranes = CraneCount(template), arrivals = ArrivalSlots(template).Count;
        if (exits > 0) text += $" · 출구 자리 {exits}";
        if (arrivals > 0) text += $" · 도착 자리 {arrivals}";
        if (zones > 0) text += $" · 생성 구역 {zones}";
        if (cranes > 0) text += $" · 크레인 {cranes}";
        return text;
    }
    // 맵 층 항목 중 조건에 맞는 것의 spawnObjects 번호(놓인 순서).
    private static List<int> MapIndices(DungeonRoomTemplateSO template, Func<GameObject, bool> match)
    {
        var result = new List<int>();
        var spawns = template?.MapLayer?.spawnObjects;
        if (spawns == null) return result;
        for (int i = 0; i < spawns.Length; i++)
            if (spawns[i]?.prefab != null && match(spawns[i].prefab)) result.Add(i);
        return result;
    }
    /// <summary>4차: 맵 층 출구 자리 표식 항목(spawnObjects 번호, 놓인 순서). 실행 쪽(DungeonRoomData.exit)은 첫 표식을 쓴다.</summary>
    public static List<int> ExitSlots(DungeonRoomTemplateSO template) => MapIndices(template, WavePrefabVisuals.IsDungeonExitSlot);
    /// <summary>6차(T11): 맵 층 도착 자리 표식 항목(spawnObjects 번호, 놓인 순서). 다음 Wave 시작 방의 도착 문 자리.</summary>
    public static List<int> ArrivalSlots(DungeonRoomTemplateSO template) => MapIndices(template, WavePrefabVisuals.IsDungeonArrivalSlot);
    /// <summary>출구 자리 표식 순번 slot이 놓인 단위 칸. 찾지 못하면 모양의 첫 단위 칸.</summary>
    public static Vector2Int ExitSlotUnit(DungeonRoomTemplateSO template, int slot) => SpawnUnit(template, ExitSlots(template), slot);
    /// <summary>4차: 맵 층 생성 구역 표식 항목(spawnObjects 번호).</summary>
    public static List<int> SpawnZones(DungeonRoomTemplateSO template) => MapIndices(template, WavePrefabVisuals.IsDungeonSpawnZone);
    /// <summary>4차: 맵 층 크레인(경로·고정) 수.</summary>
    public static int CraneCount(DungeonRoomTemplateSO template) => MapIndices(template, DungeonCranePaths.IsCrane).Count;
    /// <summary>아직 적용하지 않은 SerializedObject 값으로 푼 배경(패널 표시용). 없으면 템플릿 값.</summary>
    public static DungeonBackdropMode ResolvedBackdrop(DungeonRoomTemplateSO template, SerializedObject serialized)
    {
        var mode = serialized?.FindProperty("backdropMode");
        var size = serialized?.FindProperty("sizeClass");
        if (mode == null || size == null) return template != null ? template.ResolvedBackdrop : DungeonBackdropMode.Parallax;
        if (mode.enumValueIndex != (int)DungeonBackdropMode.Auto) return (DungeonBackdropMode)mode.enumValueIndex;
        return size.enumValueIndex == (int)DungeonRoomSize.Small ? DungeonBackdropMode.Wall : DungeonBackdropMode.Parallax;
    }
    /// <summary>생성 규칙 v2의 시작 방 조건: 시작 방 가능 · '시작 불가'·'휴식' 꼬리표 없음 · 배경 시차 · 보스 방 아님.</summary>
    public static bool CanStart(DungeonRoomTemplateSO template)
        => template != null && template.role != DungeonRoomRole.Boss && template.allowAsStart && !template.HasTag(DungeonRoomTags.NoStart)
           && !template.HasTag(DungeonRoomTags.Rest) && template.ResolvedBackdrop == DungeonBackdropMode.Parallax;
    /// <summary>맵 층 텔레포트 시설 항목(spawnObjects 번호, 놓인 순서). 생성기의 '텔레포트 항목 순번'은 이 목록의 순번이다.</summary>
    public static List<int> TeleportPads(DungeonRoomTemplateSO template)
    {
        var result = new List<int>();
        var spawns = template?.MapLayer?.spawnObjects;
        if (spawns == null) return result;
        for (int i = 0; i < spawns.Length; i++)
            if (spawns[i]?.prefab != null && WavePrefabVisuals.IsDungeonTeleporter(spawns[i].prefab)) result.Add(i);
        return result;
    }
    /// <summary>텔레포트 짝을 받을 수 있는 템플릿: 꼬리표 '텔레포트 허브'·'텔레포트 끝점' + 맵 층 텔레포트 시설 + 보스 방 아님.</summary>
    public static bool CanHostTeleport(DungeonRoomTemplateSO template)
        => template != null && template.role != DungeonRoomRole.Boss
           && (template.HasTag(DungeonRoomTags.TeleportHub) || template.HasTag(DungeonRoomTags.TeleportNode)) && TeleportPads(template).Count > 0;
    /// <summary>텔레포트 항목 순번 pad가 놓인 단위 칸(모양 기준, TryReadDoors와 같은 실내 등분). 찾지 못하면 모양의 첫 단위 칸.</summary>
    public static Vector2Int PadUnit(DungeonRoomTemplateSO template, int pad) => SpawnUnit(template, TeleportPads(template), pad);

    // ───────── 3차(DESIGN_V3): 목표 자리 표식·생성 규칙 v3 ─────────
    /// <summary>
    /// 맵 층 목표 자리 표식 항목(spawnObjects 번호, 놓인 순서). '자리 표식 순번'(DungeonObjectiveAssignment.slot·DungeonObjectivePlacement.slot)은 이 목록의 순번이다
    /// (텔레포트 항목 순번과 같은 규칙 — 생성기·실행 쪽도 같은 순서로 세야 한다).
    /// </summary>
    public static List<int> ObjectiveSlots(DungeonRoomTemplateSO template)
    {
        var result = new List<int>();
        var spawns = template?.MapLayer?.spawnObjects;
        if (spawns == null) return result;
        for (int i = 0; i < spawns.Length; i++)
            if (spawns[i]?.prefab != null && WavePrefabVisuals.IsDungeonObjectiveSlot(spawns[i].prefab)) result.Add(i);
        return result;
    }
    /// <summary>자리 표식 순번 slot이 놓인 단위 칸. 찾지 못하면 모양의 첫 단위 칸.</summary>
    public static Vector2Int ObjectiveSlotUnit(DungeonRoomTemplateSO template, int slot) => SpawnUnit(template, ObjectiveSlots(template), slot);
    /// <summary>전투 방 수 입력 하한(생성 규칙 v3-1): 보스 방이 있으면 0(시작 방 + 보스 방), 없으면 1.</summary>
    public static int MinCombatRooms(bool includeBossRoom) => includeBossRoom ? 0 : 1;
    /// <summary>고정 시작 방(startTemplates)으로 쓸 수 있는지: 보스 방 아님 · 시차 배경('휴식'·'시작 불가'·시작 방 가능 여부는 보지 않음 — 생성 규칙 v3-2).</summary>
    public static bool CanFixedStart(DungeonRoomTemplateSO template)
        => template != null && template.role != DungeonRoomRole.Boss && template.ResolvedBackdrop == DungeonBackdropMode.Parallax;
    /// <summary>
    /// 빌더(DungeonTemplateBuilder.InspectV4 FightRoom)·정적 검사기(check_templates.py fight_room)와 같은 '적이 나오는 템플릿' 판정:
    /// 일반 방 · 출구 자리 표식 없음(시작 전용 아님) · 휴식 시작 방('휴식' + 시작 방 가능) 아님. 손 배치 적 구성 수는 보지 않는다(4차 동적 구성 템플릿은 구성이 0개다).
    /// </summary>
    public static bool IsFightTemplate(DungeonRoomTemplateSO template)
        => template != null && template.role == DungeonRoomRole.Normal && ExitSlots(template).Count == 0
           && !(template.HasTag(DungeonRoomTags.Rest) && template.allowAsStart);
    /// <summary>
    /// 이 던전 설정에서 template이 전투 방으로 싸우는지(실행 쪽 DungeonRoomNode.HasFight와 같은 뜻): 동적 구성(UsesDynamicEncounters)이면 적 구성이 없어도 전투 방은 싸우고,
    /// 손 배치 구성이면 적 구성이 1개 이상이어야 한다.
    /// </summary>
    public static bool FightsIn(DungeonWaveSettings settings, DungeonRoomTemplateSO template)
        => template != null && (template.EncounterCount > 0 || (settings != null && settings.UsesDynamicEncounters));
    /// <summary>
    /// 목표를 놓을 수 있는 일반 방 후보 템플릿: 가중치 > 0 · 보스 방 아님 · 전투 방으로 싸움(FightsIn — 동적 구성이면 적 구성이 없어도 됨, DungeonObjectiveRoomPicker의 HasFight와 같음)
    /// · 자리 표식 1개 이상 · (휴식 꼬리표면 레버 목표의 '휴식 방 허용').
    /// </summary>
    public static List<DungeonRoomTemplateSO> ObjectiveCandidates(DungeonWaveSettings settings, DungeonObjectiveSO objective)
    {
        var result = new List<DungeonRoomTemplateSO>();
        bool allowRest = !(objective is LeverObjectiveSO lever) || lever.allowRestRooms;
        foreach (var entry in settings?.templates ?? Array.Empty<DungeonTemplateEntry>())
        {
            var t = entry?.template;
            if (t == null || !(entry.weight > 0f) || result.Contains(t) || t.role == DungeonRoomRole.Boss || !FightsIn(settings, t)) continue;
            if (t.HasTag(DungeonRoomTags.Rest) && !allowRest) continue;
            if (ObjectiveSlots(t).Count > 0) result.Add(t);
        }
        return result;
    }
    /// <summary>목표 설정 요약 한 줄: 자리 표식 있는 전투 템플릿 n/m종(일반 후보 중).</summary>
    public static string ObjectiveCandidateLine(DungeonWaveSettings settings, DungeonObjectiveSO objective)
    {
        if (settings == null || objective == null) return null;
        int normal = (settings.templates ?? Array.Empty<DungeonTemplateEntry>()).Where(e => e?.template != null && e.weight > 0f && e.template.role != DungeonRoomRole.Boss).Select(e => e.template).Distinct().Count();
        return $"목표 자리 표식이 있는 전투 템플릿 {ObjectiveCandidates(settings, objective).Count}/{normal}종" + (objective is LeverObjectiveSO lever ? $" · 레버 {lever.count}개(방당 1개)" : "");
    }
    /// <summary>
    /// 목표 설정 검사(클리어 조건이 '목표 완료'일 때). 목표 SO 비어 있음, 실물 Prefab 없음·목표 실물 계약(IDungeonObjectivePoint) 없음,
    /// 자리 표식 있는 전투 템플릿 없음, 전투 방 최대 수 &lt; 레버 수, 같은 템플릿 최대 수 합 &lt; 레버 수.
    /// </summary>
    public static List<DungeonTemplateIssue> ObjectiveIssues(DungeonWaveSettings settings, DungeonObjectiveSO objective)
    {
        var issues = new List<DungeonTemplateIssue>();
        void Error(string message) => issues.Add(new DungeonTemplateIssue(true, message));
        void Warn(string message) => issues.Add(new DungeonTemplateIssue(false, message));
        if (settings == null) return issues;
        if (objective == null) { Error("목표 모듈이 비어 있습니다. 실행 때는 '모든 전투 방 클리어' 규칙으로 대신합니다."); return issues; }
        var lever = objective as LeverObjectiveSO;
        int needed = lever != null ? Mathf.Max(1, lever.count) : 1;
        GameObject prefab = null;
        try { prefab = objective.PrefabFor(0); } catch (Exception) { /* 파생 SO가 Edit Mode에서 실패해도 아래 경고로 알린다. */ }
        if (prefab == null) Error($"{objective.name}: 목표 실물 Prefab이 비어 실행 때 세울 것이 없습니다" + (lever != null ? "(레버 Prefab)." : "."));
        else if (prefab.GetComponent<IDungeonObjectivePoint>() == null) Warn($"{objective.name}: 실물 Prefab {prefab.name}에 목표 실물 컴포넌트(IDungeonObjectivePoint)가 없어 완료를 알릴 수 없습니다.");
        var candidates = ObjectiveCandidates(settings, objective);
        if (candidates.Count == 0)
            Error("목표 자리 표식이 있는 전투 방 템플릿(" + (settings.UsesDynamicEncounters ? "동적 구성 전투 방" : "적 구성 있음") + " · 보스 방 아님"
                + (lever != null && !lever.allowRestRooms ? " · 휴식 꼬리표 아님" : "") + ")이 후보에 없어 목표를 놓을 수 없습니다. 템플릿 맵 층에 목표 자리 표식을 놓으세요.");
        int maxRooms = Mathf.Max(settings.minRooms, settings.maxRooms);
        if (maxRooms < needed)
        {
            string message = $"전투 방 최대 {maxRooms}개가 레버 {needed}개보다 적습니다. 레버는 방당 1개라 다 놓지 못합니다";
            if (lever != null && lever.allowAppendedNodes) Warn(message + "(덧붙인 텔레포트 끝점 방이 있어야만 채워집니다).");
            else Error(message + ".");
        }
        int capacity = candidates.Sum(t => t.maxPerFloor > 0 ? t.maxPerFloor : Mathf.Max(1, settings.sameTemplateLimit));
        if (candidates.Count > 0 && capacity < needed)
            Warn($"자리 표식 있는 전투 템플릿 {candidates.Count}종의 던전당 최대 수 합이 {capacity}로 레버 {needed}개보다 적습니다. 생성이 자주 실패할 수 있습니다.");
        // 4차: 시작 방 출구(마무리 방식 ExitInStartRoom) — 출구 문 Prefab·출구 문 계약(IDungeonExitPoint)·출구 자리 표식이 있는 시작 후보.
        if (objective.finish == DungeonObjectiveFinish.ExitInStartRoom)
        {
            GameObject exit = null;
            try { exit = objective.ExitPrefab; } catch (Exception) { /* 파생 SO 계산 실패는 아래 오류로 알린다. */ }
            if (exit == null) Error($"{objective.name}: 마무리 방식이 '시작 방 출구'인데 출구 문 Prefab이 비어 있습니다. 실행 때는 즉시 클리어로 대신합니다.");
            else if (exit.GetComponent<IDungeonExitPoint>() == null) Warn($"{objective.name}: 출구 문 Prefab {exit.name}에 출구 문 컴포넌트(IDungeonExitPoint)가 없어 진행·입장을 받을 수 없습니다.");
            var starts = StartCandidates(settings);
            if (starts.Count > 0 && !starts.Any(t => ExitSlots(t).Count > 0))
                Error("마무리 방식이 '시작 방 출구'인데 시작 방 후보(" + string.Join(", ", starts.Select(t => t.name)) + ")에 출구 자리 표식(5×6)이 없습니다. 시작 전용 템플릿에 출구 자리 표식을 놓으세요.");
            else if (starts.Any(t => ExitSlots(t).Count == 0))
                Warn("시작 방 후보 중 출구 자리 표식이 없는 템플릿(" + string.Join(", ", starts.Where(t => ExitSlots(t).Count == 0).Select(t => t.name)) + ")은 시작 방으로 고르지 않습니다(생성 규칙 v4-2).");
        }
        return issues;
    }
    /// <summary>시작 방 후보: 고정 시작 방 목록이 있으면 그중 쓸 수 있는 것, 없으면 일반 후보 중 시작 방 조건(CanStart)을 채우는 템플릿.</summary>
    public static List<DungeonRoomTemplateSO> StartCandidates(DungeonWaveSettings settings)
    {
        var fixedStarts = (settings?.startTemplates ?? new List<DungeonRoomTemplateSO>()).Where(CanFixedStart).Distinct().ToList();
        if ((settings?.startTemplates?.Count ?? 0) > 0) return fixedStarts;
        return (settings?.templates ?? Array.Empty<DungeonTemplateEntry>()).Where(e => e?.template != null && e.weight > 0f).Select(e => e.template).Where(CanStart).Distinct().ToList();
    }

    // ───────── 4차: 적 생성 규칙 검사·요약 ─────────
    /// <summary>
    /// 적 생성 규칙 검사(편집기 표시): 종류 없음(오류), 종류 Prefab 없음·적이 아님, 단계 배열 길이 5 미만, 문턱 오름차순 아님, 어떤 단계의 가중치 합 0, Bulwark Prefab 없음(경고).
    /// </summary>
    public static List<DungeonTemplateIssue> EncounterRuleIssues(DungeonEncounterRulesSO rules)
    {
        var issues = new List<DungeonTemplateIssue>();
        void Error(string message) => issues.Add(new DungeonTemplateIssue(true, message));
        void Warn(string message) => issues.Add(new DungeonTemplateIssue(false, message));
        if (rules == null) return issues;
        var species = rules.species ?? Array.Empty<DungeonSpeciesRule>();
        if (species.Length == 0) Error($"{rules.name}: 적 종류가 없어 동적 구성이 적을 만들지 못합니다.");
        foreach (var rule in species)
        {
            if (rule == null) continue;
            string name = string.IsNullOrWhiteSpace(rule.name) ? "이름 없는 종류" : rule.name;
            if (rule.prefab == null) Warn($"{rules.name}: 종류 {name}의 Prefab이 비어 있어 그 종류는 나오지 않습니다.");
            else if (rule.prefab.GetComponent<EnemyController>() == null) Warn($"{rules.name}: 종류 {name}의 Prefab {rule.prefab.name}이 적(EnemyController)이 아닙니다.");
        }
        foreach (var (field, length) in new[] { ("단계 배율", rules.multipliers?.Length ?? 0), ("단계 최소 수", rules.minCounts?.Length ?? 0), ("단계 문턱", rules.thresholds?.Length ?? 0), ("동시 생존 상한", rules.concurrentCaps?.Length ?? 0) })
            if (length < DungeonEncounterRulesSO.LevelCount) Warn($"{rules.name}: {field} 값이 {length}개입니다(5단계 — 모자라면 마지막 값을 씁니다).");
        var thresholds = rules.thresholds ?? Array.Empty<int>();
        for (int i = 1; i < thresholds.Length; i++)
            if (thresholds[i] < thresholds[i - 1]) { Warn($"{rules.name}: 단계 문턱이 오름차순이 아닙니다({string.Join("·", thresholds)}). 실행 때는 앞 값으로 맞춥니다."); break; }
        for (int level = 0; level < DungeonEncounterRulesSO.LevelCount; level++)
        {
            var threat = (DungeonThreatLevel)level;
            if (species.Length > 0 && species.Where(s => s != null && s.prefab != null).Sum(s => s.Weight(threat)) <= 0f)
                Warn($"{rules.name}: {DungeonThreatText.Label(threat)} 단계에서 나올 수 있는 종류의 가중치 합이 0입니다.");
        }
        if (rules.bulwark == null || rules.bulwark.prefab == null) Warn($"{rules.name}: Bulwark Prefab이 비어 있어 큰 방에도 Bulwark가 나오지 않습니다.");
        return issues;
    }
    /// <summary>'단계: 입문 0 ×0.7 · 하급 2 ×1 · …' 한 줄(문턱 = 첫 클리어 방 수, 배율 = 적 수 배율).</summary>
    public static string ThreatLine(DungeonEncounterRulesSO rules)
    {
        if (rules == null) return null;
        var parts = new List<string>();
        for (int level = 0; level < DungeonEncounterRulesSO.LevelCount; level++)
        {
            var threat = (DungeonThreatLevel)level;
            int threshold = rules.thresholds != null && rules.thresholds.Length > 0 ? rules.thresholds[Mathf.Min(level, rules.thresholds.Length - 1)] : 0;
            parts.Add($"{DungeonThreatText.Label(threat)} {threshold}방 ×{rules.Multiplier(threat):0.##}(최소 {rules.MinCount(threat)} · 동시 {rules.ConcurrentCap(threat)})");
        }
        return "난이도 단계(첫 클리어 방 수 · 적 수 배율): " + string.Join(" / ", parts) + $" · 방당 최대 {Mathf.Max(1, rules.maxCount)}";
    }

    // 맵 층 항목 indices[order]의 기준점이 놓인 단위 칸(TryReadDoors와 같은 실내 등분). 찾지 못하면 모양의 첫 단위 칸.
    private static Vector2Int SpawnUnit(DungeonRoomTemplateSO template, List<int> indices, int order)
    {
        if (template == null) return Vector2Int.zero;
        var first = DungeonShapes.Units(template.shape).FirstOrDefault();
        if (order < 0 || order >= indices.Count || !template.TryGetCellBounds(out var box)) return first;
        var point = template.MapLayer.spawnObjects[indices[order]].spawnPosition;
        var size = DungeonShapes.Size(template.shape);
        int wall = DungeonRoomTemplateSO.WallThickness;
        float ix = box.xMin + wall, iy = box.yMin + wall;
        float iw = Mathf.Max(1f, box.width - 2 * wall), ih = Mathf.Max(1f, box.height - 2 * wall);
        var unit = new Vector2Int(Mathf.Clamp(Mathf.FloorToInt((point.x - ix) / iw * size.x), 0, size.x - 1),
            Mathf.Clamp(Mathf.FloorToInt((point.y - iy) / ih * size.y), 0, size.y - 1));
        return DungeonShapes.Contains(template.shape, unit) ? unit : first;
    }
}
