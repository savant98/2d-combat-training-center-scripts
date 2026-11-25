using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 던전 하나(= Wave 하나)의 방 배치와 연결을 만든다(DESIGN.md §2.2, 생성 규칙 v2 = DESIGN_V2.md §2).
/// 순수 C#이다. 난수는 System.Random만 쓰므로(UnityEngine.Random 금지) 같은 설정·같은 시드면 언제나 같은 결과다.
/// 순서: 시작 방(격자 (0,0), Parallax 방만) → 빈 출입구 바깥 단위 칸에 맞은편 출입구를 가진 템플릿을 놓기를 전투 방 수까지 반복
/// (크기 섞기·방향 교대·반복 제한·휴식 방 규칙으로 후보와 가중치를 정하고, 새 방마다 고리 연결 시도)
/// → 보스 방을 시작에서 가장 먼 방(동률이면 작은 방·연결 수 적은 방 우선)의 빈 출입구에 하나만 연결 → 방마다 적 구성 고르기
/// → 텔레포트 짝짓기(별도 난수 seed ^ <see cref="TeleportSeedSalt"/>. 위 결과는 텔레포트 설정과 무관하게 같다).
/// 생성 규칙 v3(DESIGN_V3.md §2): 전투 방 0개는 보스 방이 있을 때만(<see cref="CombatRoomRange"/>), 시작 방 지정 목록(startTemplates),
/// 시작에서의 거리로 난이도 올리기(tierByDistance, <see cref="TierForDistance"/>), 목표 배치(클리어 조건 Objective — 텔레포트 뒤 별도 난수
/// seed ^ <see cref="ObjectiveSeedSalt"/>로 목표 SO가 방·슬롯을 고르고, 못 고르면 그 시도를 실패로 보고 시드+1로 다시 만든다).
/// 생성 규칙 v4(DESIGN_V4 §3): 목표가 '시작 방 출구' 방식이면 출구 자리 표식(DungeonExitSlot)이 있는 템플릿만 시작 후보로 거른다(<see cref="ExitSlots"/> —
/// 후보 목록만 바꾸고 난수는 그대로 쓴다, 없으면 알림 후 즉시 방식). 동적 적 구성(Dynamic) 던전은 전투 방에 dynamicFight를 표시한다(난수 없음).
/// 새 모양(3×1·1×3·2×2)은 모양 표(DungeonShapes)만으로 배치된다.
/// </summary>
public static class DungeonGenerator
{
    /// <summary>방을 다 놓지 못하면 시드를 1씩 늘려 다시 시도하는 최대 횟수.</summary>
    public const int MaxAttempts = 32;
    /// <summary>텔레포트 짝짓기 난수의 시드 섞기 값(seed ^ 이 값). 배치·적 구성 난수와 분리해 텔레포트 설정이 기존 시드 결과를 바꾸지 않게 한다.</summary>
    public const int TeleportSeedSalt = 0x5EED7E1E;
    /// <summary>목표 배치 난수의 시드 섞기 값(seed ^ 이 값). 배치·적 구성·텔레포트 결과를 바꾸지 않게 따로 굴린다(생성 규칙 v3-12).</summary>
    public const int ObjectiveSeedSalt = 0x0B1EC7A1;
    // 한 번의 시도에서 출입구를 고르는 최대 횟수(막힌 출입구는 다시 고르지 않으므로 보통 훨씬 적게 쓴다).
    private const int MaxPicksPerAttempt = 512;
    // 텔레포트 끝점 방을 덧붙일 때 시도해 볼 출입구 수 상한(한 짝당).
    private const int MaxAppendSockets = 24;

    private sealed class Candidate
    {
        public DungeonRoomTemplateSO template;
        public float weight;
        public List<DungeonDoorSocket> sockets;
        public int pads;
    }

    // 한 번의 생성(모든 시도)에 공통인 값.
    private sealed class Context
    {
        public DungeonWaveSettings settings;
        public List<Candidate> normal;
        public List<Candidate> starts;
        public List<Candidate> nodes;
        public Candidate boss;
        public readonly Dictionary<DungeonRoomTemplateSO, int> pads = new();
        public readonly Dictionary<DungeonRoomTemplateSO, int> slots = new();
        public readonly Dictionary<DungeonRoomTemplateSO, int> exitSlots = new();
        // 전투 방 수 범위(보스 방이 있으면 0부터, 생성 규칙 v3-1).
        public int combatMin, combatMax;
        // 시작 방을 설정의 startTemplates 목록에서 골랐는지(그 시작 방은 휴식 방 거리 규칙에서 뺀다).
        public bool startOverride;
        // 이번 시도에서 생긴 알림(성공한 시도의 것만 결과에 남긴다).
        public readonly List<string> attemptNotes = new();
    }

    /// <summary>
    /// 설정과 시드로 던전 하나의 방 배치·연결을 만든다. 같은 시드면 같은 결과다.
    /// 실패하면 false와 오류 문장을 돌려준다(시드+1 재시도는 안에서 한다). 성공해도 건너뛴 템플릿 등 알릴 내용이 있으면 error에 담는다.
    /// </summary>
    public static bool TryGenerate(DungeonWaveSettings settings, int seed, out DungeonLayout layout, out string error)
    {
        layout = null;
        var notes = new StringBuilder();
        if (settings == null) { error = "던전 설정이 없습니다."; return false; }

        // 1. 템플릿 출입구 읽기(읽기 실패·출입구 없는 템플릿은 건너뛰고 사유를 모은다)
        var read = new Dictionary<DungeonRoomTemplateSO, List<DungeonDoorSocket>>();
        var ctx = new Context { settings = settings, normal = new List<Candidate>() };
        foreach (var entry in settings.templates ?? Array.Empty<DungeonTemplateEntry>())
        {
            if (entry == null || entry.template == null) continue;
            if (!(entry.weight > 0f)) continue;
            var template = entry.template;
            if (template.role == DungeonRoomRole.Boss)
            {
                Note(notes, $"보스 역할 템플릿 '{template.name}'은 일반 방 후보에서 뺐습니다(보스 방 칸에 지정하세요).");
                continue;
            }
            if (!TryRead(template, read, notes, out var sockets)) continue;
            int pads = PadCount(ctx, template);
            if (pads > 0 && !template.HasTag(DungeonRoomTags.TeleportHub) && !template.HasTag(DungeonRoomTags.TeleportNode))
                Note(notes, $"템플릿 '{template.name}'에 텔레포트 시설이 있지만 TeleportHub·TeleportNode 꼬리표가 없어 짝을 짓지 않습니다(꺼진 받침).");
            else if (pads == 0 && (template.HasTag(DungeonRoomTags.TeleportHub) || template.HasTag(DungeonRoomTags.TeleportNode)))
                Note(notes, $"템플릿 '{template.name}'은 텔레포트 꼬리표가 있지만 맵 층에 텔레포트 시설이 없습니다.");
            ctx.normal.Add(new Candidate { template = template, weight = entry.weight, sockets = sockets, pads = pads });
        }
        CombatRoomRange(settings, out ctx.combatMin, out ctx.combatMax);
        // 생성 규칙 v3-2: 시작 방 지정 목록이 있으면 그 목록만 후보로 쓴다(Rest·NoStart·allowAsStart 조건은 건너뛰고 Parallax 조건은 유지).
        ctx.starts = StartOverride(ctx, read, notes);
        ctx.startOverride = ctx.starts != null;
        // 전투 방 0개(보스 방만 붙이는 던전)는 지정한 시작 방만 있으면 일반 방 템플릿이 없어도 된다.
        if (ctx.normal.Count == 0 && !(ctx.startOverride && ctx.combatMax == 0))
        {
            error = Join(notes, "쓸 수 있는 일반 방 템플릿이 없습니다.");
            return false;
        }
        // 시작 방: allowAsStart && !NoStart && 배경 Parallax(덮개 없이 세우므로 정적 벽면 방은 안 됨). 휴식 방은 시작에서 떨어져야 하므로 뺀다.
        if (!ctx.startOverride)
            ctx.starts = ctx.normal.FindAll(c => c.template.allowAsStart && !c.template.HasTag(DungeonRoomTags.NoStart)
                && c.template.ResolvedBackdrop == DungeonBackdropMode.Parallax && !c.template.HasTag(DungeonRoomTags.Rest));
        if (ctx.starts.Count == 0)
        {
            ctx.starts = ctx.normal.FindAll(c => c.template.allowAsStart && !c.template.HasTag(DungeonRoomTags.NoStart));
            if (ctx.starts.Count == 0) ctx.starts = ctx.normal.FindAll(c => c.template.allowAsStart);
            if (ctx.starts.Count == 0)
            {
                error = Join(notes, "시작 방으로 쓸 수 있는(allowAsStart) 템플릿이 없습니다.");
                return false;
            }
            Note(notes, "시차 배경(Parallax)·NoStart 아님 조건을 모두 채우는 시작 방 템플릿이 없어 조건을 줄여 골랐습니다.");
        }
        // 생성 규칙 v4-2: 시작 방 출구 방식이면 출구 자리 표식이 있는 후보만 남긴다(지정 목록이어도 같다). 후보만 거르므로 난수 소비 순서는 같다.
        FilterExitStarts(ctx, notes);
        ctx.nodes = ctx.normal.FindAll(c => c.pads > 0 && c.template.HasTag(DungeonRoomTags.TeleportNode));
        if (settings.includeBossRoom)
        {
            if (settings.bossRoom == null) { error = Join(notes, "보스 방을 넣도록 했지만 보스 방 템플릿이 비어 있습니다."); return false; }
            if (!TryRead(settings.bossRoom, read, notes, out var bossSockets)) { error = Join(notes, "보스 방 템플릿을 쓸 수 없습니다."); return false; }
            if (settings.bossRoom.role != DungeonRoomRole.Boss) Note(notes, $"보스 방 템플릿 '{settings.bossRoom.name}'의 역할이 Boss가 아닙니다(그대로 보스 방으로 씁니다).");
            ctx.boss = new Candidate { template = settings.bossRoom, weight = 1f, sockets = bossSockets, pads = 0 };
        }

        // 2. 시드를 늘려 가며 시도
        string last = null;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            int used = unchecked(seed + attempt);
            var rng = new System.Random(used);
            ctx.attemptNotes.Clear();
            if (!TryBuild(ctx, rng, out var built, out last)) continue;
            built.seed = used;
            // 이 시도의 알림은 따로 모았다가 시도가 성공할 때만 결과에 옮긴다(목표 배치가 실패하면 다음 시도로 넘어간다).
            var attemptLog = new StringBuilder();
            PickEncounters(ctx, built, rng, attemptLog);
            int baseCount = built.rooms.Count;
            // 텔레포트는 별도 난수로 짝짓는다(위 배치·적 구성은 텔레포트 설정과 무관하게 같다).
            AssignTeleports(ctx, built, new System.Random(used ^ TeleportSeedSalt), attemptLog);
            // 목표도 별도 난수로 고른다(위 배치·적 구성·텔레포트는 목표 설정과 무관하게 같다). 고르지 못하면 이 시도는 실패다.
            if (!PlaceObjectives(ctx, built, used, baseCount, attemptLog, out string objectiveFailure)) { last = objectiveFailure; continue; }
            foreach (var line in Lines(attemptLog)) Note(notes, line);
            foreach (var line in ctx.attemptNotes) Note(notes, line);
            layout = built;
            error = notes.Length > 0 ? notes.ToString().TrimEnd() : null;
            return true;
        }
        error = Join(notes, $"시드 {seed}부터 {MaxAttempts}번 시도했지만 던전을 만들지 못했습니다({last}).");
        return false;
    }

    /// <summary>
    /// 템플릿 맵 층의 텔레포트 항목(spawnObjects 중 Prefab에 DungeonTeleporter가 있는 것) 수. spawnIndices를 주면 항목 번호(순번 0부터의 순서)를 채운다.
    /// 출입구처럼 읽은 순간의 값이다(Tool이 배열을 다시 정렬할 수 있다).
    /// </summary>
    public static int TeleportPads(DungeonRoomTemplateSO template, List<int> spawnIndices = null)
    {
        spawnIndices?.Clear();
        var map = template != null ? template.MapLayer : null;
        var objects = map != null && map.spawnObjects != null ? map.spawnObjects : Array.Empty<WaveSpawnInfo>();
        int count = 0;
        for (int i = 0; i < objects.Length; i++)
        {
            var info = objects[i];
            if (info == null || info.prefab == null || info.prefab.GetComponent<DungeonTeleporter>() == null) continue;
            count++;
            spawnIndices?.Add(i);
        }
        return count;
    }

    /// <summary>템플릿이 텔레포트 짝을 받을 수 있는지(시설 1개 이상 + TeleportHub·TeleportNode 꼬리표).</summary>
    public static bool CanHostTeleport(DungeonRoomTemplateSO template)
        => template != null && (template.HasTag(DungeonRoomTags.TeleportHub) || template.HasTag(DungeonRoomTags.TeleportNode)) && TeleportPads(template) > 0;

    /// <summary>
    /// 템플릿 맵 층의 목표 슬롯 표식(spawnObjects 중 Prefab에 DungeonObjectiveSlot이 있는 것) 수. spawnIndices를 주면 항목 번호를 채운다.
    /// 슬롯 번호(DungeonObjectiveAssignment.slot·DungeonRoomState.completedObjectives의 키) = 이 순서(0부터)다.
    /// </summary>
    public static int ObjectiveSlots(DungeonRoomTemplateSO template, List<int> spawnIndices = null)
    {
        spawnIndices?.Clear();
        var map = template != null ? template.MapLayer : null;
        var objects = map != null && map.spawnObjects != null ? map.spawnObjects : Array.Empty<WaveSpawnInfo>();
        int count = 0;
        for (int i = 0; i < objects.Length; i++)
        {
            var info = objects[i];
            if (info == null || info.prefab == null || info.prefab.GetComponent<DungeonObjectiveSlot>() == null) continue;
            count++;
            spawnIndices?.Add(i);
        }
        return count;
    }

    /// <summary>
    /// 템플릿 맵 층의 출구 자리 표식(spawnObjects 중 Prefab에 DungeonExitSlot이 있는 것) 수(4차). spawnIndices를 주면 항목 번호를 채운다.
    /// 시작 방 출구는 첫 표식 하나만 쓴다(DungeonRunBuilder).
    /// </summary>
    public static int ExitSlots(DungeonRoomTemplateSO template, List<int> spawnIndices = null)
    {
        spawnIndices?.Clear();
        var map = template != null ? template.MapLayer : null;
        var objects = map != null && map.spawnObjects != null ? map.spawnObjects : Array.Empty<WaveSpawnInfo>();
        int count = 0;
        for (int i = 0; i < objects.Length; i++)
        {
            var info = objects[i];
            if (info == null || info.prefab == null || info.prefab.GetComponent<DungeonExitSlot>() == null) continue;
            count++;
            spawnIndices?.Add(i);
        }
        return count;
    }

    /// <summary>설정이 시작 방 출구 방식인지(클리어 조건 Objective + 목표 SO가 ExitInStartRoom이고 출구 Prefab이 있음).</summary>
    public static bool UsesStartRoomExit(DungeonWaveSettings settings)
        => settings != null && settings.clearCondition == DungeonClearCondition.Objective && settings.objective != null && settings.objective.UsesStartRoomExit;

    /// <summary>
    /// 전투 방 수 범위(시작·보스 방 제외, 생성 규칙 v3-1). 보스 방이 있으면 0개까지(= 시작 방 + 보스 방) 허용하고, 없으면 1개 이상으로 본다.
    /// 설정의 ClampedMinRooms·ClampedMaxRooms(늘 1 이상) 대신 생성기·격자 Tool 미리보기·회귀가 이 값을 쓴다.
    /// </summary>
    public static void CombatRoomRange(DungeonWaveSettings settings, out int min, out int max)
    {
        int floor = settings != null && settings.includeBossRoom ? 0 : 1;
        int a = settings != null ? settings.minRooms : 1, b = settings != null ? settings.maxRooms : 1;
        min = Mathf.Max(floor, Mathf.Min(a, b));
        max = Mathf.Max(floor, Mathf.Max(a, b));
    }

    /// <summary>
    /// 시작에서의 거리로 정하는 적 구성 난이도(생성 규칙 v3-3, tierByDistance): round(lerp(minTier, maxTier, distance / farthest)).
    /// farthest는 <see cref="FarthestForTier"/>(보스 방을 뺀 방 중 가장 먼 거리)다(0이면 minTier). 반올림은 0.5를 올린다.
    /// </summary>
    public static int TierForDistance(DungeonWaveSettings settings, int distance, int farthest)
    {
        int lo = settings.ClampedMinTier, hi = settings.ClampedMaxTier;
        if (farthest <= 0 || distance <= 0) return lo;
        float t = Mathf.Clamp01((float)distance / farthest);
        return Mathf.Clamp(Mathf.FloorToInt(Mathf.Lerp(lo, hi, t) + .5f), lo, hi);
    }

    /// <summary>난이도 계산의 '최대 거리': 보스 방을 뺀 닿는 방 중 시작에서 가장 먼 거리.</summary>
    public static int FarthestForTier(DungeonLayout layout, int[] distance)
    {
        int far = 0;
        foreach (var room in layout.rooms)
        {
            if (room.kind == DungeonRoomKind.Boss || room.id < 0 || room.id >= distance.Length || distance[room.id] == int.MaxValue) continue;
            far = Mathf.Max(far, distance[room.id]);
        }
        return far;
    }

    /// <summary>두 방의 던전 격자 거리(서로 가장 가까운 단위 칸 사이의 맨해튼 거리).</summary>
    public static int GridDistance(DungeonRoomNode a, DungeonRoomNode b)
    {
        int best = int.MaxValue;
        foreach (var u in a.Units)
            foreach (var v in b.Units)
                best = Mathf.Min(best, Mathf.Abs(u.x - v.x) + Mathf.Abs(u.y - v.y));
        return best;
    }

    private static int PadCount(Context ctx, DungeonRoomTemplateSO template)
    {
        if (template == null) return 0;
        if (!ctx.pads.TryGetValue(template, out int count)) ctx.pads[template] = count = TeleportPads(template);
        return count;
    }

    private static int SlotCount(Context ctx, DungeonRoomTemplateSO template)
    {
        if (template == null) return 0;
        if (!ctx.slots.TryGetValue(template, out int count)) ctx.slots[template] = count = ObjectiveSlots(template);
        return count;
    }

    private static int ExitSlotCount(Context ctx, DungeonRoomTemplateSO template)
    {
        if (template == null) return 0;
        if (!ctx.exitSlots.TryGetValue(template, out int count)) ctx.exitSlots[template] = count = ExitSlots(template);
        return count;
    }

    // 생성 규칙 v4-2: 시작 방 출구 방식이면 시작 후보를 출구 자리 표식이 있는 템플릿으로 거른다. 하나도 없으면 후보를 그대로 두고 알린다
    // (실행 쪽 DungeonObjectiveTracker가 출구 자리가 없는 시작 방을 보고 즉시 방식으로 대신한다).
    private static void FilterExitStarts(Context ctx, StringBuilder notes)
    {
        if (!UsesStartRoomExit(ctx.settings) || ctx.starts == null) return;
        var withExit = ctx.starts.FindAll(c => ExitSlotCount(ctx, c.template) > 0);
        if (withExit.Count == 0)
        {
            Note(notes, "목표가 '시작 방 출구' 방식이지만 출구 자리 표식(DungeonExitSlot)이 있는 시작 방 후보가 없어 마지막 목표에서 바로 끝냅니다(즉시 방식).");
            return;
        }
        ctx.starts = withExit;
    }

    // startTemplates가 비어 있으면 null. 있으면 그 목록의 후보(가중치는 일반 방 목록의 것, 없으면 1). Parallax 방만 남기되 하나도 없으면 목록 전체를 쓴다.
    private static List<Candidate> StartOverride(Context ctx, Dictionary<DungeonRoomTemplateSO, List<DungeonDoorSocket>> read, StringBuilder notes)
    {
        var settings = ctx.settings;
        if (settings.startTemplates == null || settings.startTemplates.Count == 0) return null;
        var list = new List<Candidate>();
        foreach (var template in settings.startTemplates)
        {
            if (template == null || list.Exists(c => c.template == template)) continue;
            if (template.role == DungeonRoomRole.Boss)
            {
                Note(notes, $"보스 역할 템플릿 '{template.name}'은 시작 방 지정 목록에서 뺐습니다.");
                continue;
            }
            if (!TryRead(template, read, notes, out var sockets)) continue;
            var known = ctx.normal.Find(c => c.template == template);
            list.Add(new Candidate { template = template, weight = known != null ? known.weight : 1f, sockets = sockets, pads = PadCount(ctx, template) });
        }
        if (list.Count == 0)
        {
            Note(notes, "시작 방 지정 목록(startTemplates)에 쓸 수 있는 템플릿이 없어 기본 규칙으로 시작 방을 골랐습니다.");
            return null;
        }
        var parallax = list.FindAll(c => c.template.ResolvedBackdrop == DungeonBackdropMode.Parallax);
        if (parallax.Count > 0) return parallax;
        Note(notes, "시작 방 지정 목록(startTemplates)에 시차 배경(Parallax) 방이 없어 정적 벽면 방을 시작 방으로 씁니다(덮개 없이 세움).");
        return list;
    }

    // 생성 규칙 v3-12: 목표 SO가 있으면(클리어 조건 Objective) 별도 난수로 방·슬롯을 고른다. 목표가 없으면 아무것도 하지 않고 true.
    // baseCount = 텔레포트 끝점을 덧붙이기 전 방 수(이 값 이상의 id = 덧붙인 방).
    private static bool PlaceObjectives(Context ctx, DungeonLayout layout, int used, int baseCount, StringBuilder notes, out string failure)
    {
        failure = null;
        layout.objectives.Clear();
        var settings = ctx.settings;
        var objective = settings.objective;
        if (settings.clearCondition != DungeonClearCondition.Objective || objective == null) return true;
        var distance = Distances(layout);
        var fromStart = new int[distance.Length];
        for (int i = 0; i < distance.Length; i++) fromStart[i] = distance[i] == int.MaxValue ? -1 : distance[i];
        var context = new DungeonObjectivePlanContext
        {
            layout = layout,
            distanceFromStart = fromStart,
            slotCount = node => node != null ? SlotCount(ctx, node.template) : 0,
            appendedFrom = baseCount,
        };
        var result = new List<DungeonObjectiveAssignment>();
        var log = new StringBuilder();
        bool ok;
        try { ok = objective.TryPlace(context, new System.Random(used ^ ObjectiveSeedSalt), result, log); }
        catch (Exception e)
        {
            ok = false;
            log.AppendLine($"목표 '{objective.name}' 배치 중 예외: {e.Message}");
        }
        string reason = string.Join(" / ", Lines(log));
        if (!ok)
        {
            failure = $"목표 '{objective.name}' 배치 실패" + (reason.Length > 0 ? $"({reason})" : "");
            return false;
        }
        if (result.Count == 0)
        {
            failure = $"목표 '{objective.name}'가 아무것도 배치하지 않음" + (reason.Length > 0 ? $"({reason})" : "");
            return false;
        }
        // 목표 SO가 돌려준 값을 확인한다(방·슬롯 범위, 같은 슬롯 두 번, 진행 번호 중복).
        var usedSlots = new HashSet<(int, int)>();
        var usedIndices = new HashSet<int>();
        foreach (var a in result)
        {
            var room = layout.Room(a.room);
            if (room == null || a.slot < 0 || a.slot >= SlotCount(ctx, room.template) || a.index < 0 || !usedSlots.Add((a.room, a.slot)) || !usedIndices.Add(a.index))
            {
                failure = $"목표 '{objective.name}'의 배치 결과가 잘못됨(방 {a.room}, 슬롯 {a.slot}, 번호 {a.index})";
                return false;
            }
        }
        layout.objectives.AddRange(result);
        foreach (var line in Lines(log)) Note(notes, line);
        return true;
    }

    private static IEnumerable<string> Lines(StringBuilder text)
    {
        foreach (var line in text.ToString().Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(trimmed)) yield return trimmed;
        }
    }

    private static bool TryRead(DungeonRoomTemplateSO template, Dictionary<DungeonRoomTemplateSO, List<DungeonDoorSocket>> read, StringBuilder notes, out List<DungeonDoorSocket> sockets)
    {
        if (read.TryGetValue(template, out sockets)) return sockets != null;
        var list = new List<DungeonDoorSocket>();
        if (!template.TryReadDoors(list, out var problem))
        {
            Note(notes, $"템플릿 '{template.name}'을 건너뜀: {problem}");
            read[template] = sockets = null;
            return false;
        }
        if (list.Count == 0)
        {
            Note(notes, $"템플릿 '{template.name}'을 건너뜀: 출입구가 없습니다.");
            read[template] = sockets = null;
            return false;
        }
        read[template] = sockets = list;
        return true;
    }

    private static bool TryBuild(Context ctx, System.Random rng, out DungeonLayout layout, out string failure)
    {
        var settings = ctx.settings;
        layout = new DungeonLayout();
        failure = null;
        var start = Pick(ctx.starts, rng);
        Place(layout, start, Vector2Int.zero, DungeonRoomKind.Start);
        layout.startRoom = 0;

        // 전투 방 수(보스 방이 있으면 0도 된다 — 그때는 보스 방을 시작 방에 바로 붙인다).
        int target = rng.Next(ctx.combatMin, ctx.combatMax + 1);
        int combat = 0, picks = 0;
        bool relaxedNoted = false;
        // 이번 시도에서 더 붙일 수 없다고 판명된 출입구(방 id, 출입구 번호)
        var dead = new HashSet<(int, int)>();
        var open = new List<(int room, int socket)>();
        while (combat < target)
        {
            if (++picks > MaxPicksPerAttempt) { failure = "출입구 선택 한도 초과"; return false; }
            open.Clear();
            foreach (var node in layout.rooms)
                for (int s = 0; s < node.sockets.Count; s++)
                    if (!node.IsLinked(s) && !dead.Contains((node.id, s)) && !layout.occupancy.ContainsKey(Outside(node, s))) open.Add((node.id, s));
            if (open.Count == 0) { failure = $"빈 출입구가 없어 전투 방 {target}개 중 {combat}개만 놓음"; return false; }
            var chosen = open[rng.Next(open.Count)];
            var from = layout.rooms[chosen.room];
            var pool = Pool(ctx, layout, from, ctx.normal, false, true, out bool limited);
            bool placedOk = TryAttach(layout, from, chosen.socket, pool, rng, DungeonRoomKind.Combat, null, out var placed);
            if (!placedOk && limited)
            {
                // 반복 제한 때문에 후보가 모자라면 그 제한만 풀어 다시 본다(바로 이웃 같은 템플릿 금지는 지킨다).
                pool = Pool(ctx, layout, from, ctx.normal, true, true, out _);
                placedOk = TryAttach(layout, from, chosen.socket, pool, rng, DungeonRoomKind.Combat, null, out placed);
                if (placedOk && !relaxedNoted)
                {
                    relaxedNoted = true;
                    ctx.attemptNotes.Add("템플릿 종류가 모자라 같은 템플릿 반복 제한(던전당 최대 수)을 일부 방에서 풀었습니다.");
                }
            }
            if (!placedOk) { dead.Add(chosen); continue; }
            combat++;
            TryLoops(ctx, layout, placed, rng);
        }

        if (ctx.boss != null && !TryPlaceBoss(layout, ctx.boss, settings.smallRoomBeforeBoss, rng)) { failure = "보스 방을 붙일 자리가 없음"; return false; }
        return true;
    }

    /// <summary>
    /// from 옆에 붙일 후보와 가중치. 규칙: 바로 이웃 같은 템플릿 금지, 던전당 최대 수(relaxed면 무시), 휴식 방 수·거리,
    /// 같은 방향(수평/수직) 이웃 가중치 × sameOrientationWeight, 크기 섞기(작은 방 목표 비율로 작은 쪽·큰 쪽 가중치 합을 맞춤).
    /// limited = 최대 수 때문에 뺀 후보가 있었는지.
    /// </summary>
    private static List<Candidate> Pool(Context ctx, DungeonLayout layout, DungeonRoomNode from, List<Candidate> source, bool relaxed, bool balanceSize, out bool limited)
    {
        var settings = ctx.settings;
        limited = false;
        var counts = new Dictionary<DungeonRoomTemplateSO, int>();
        int rests = 0, small = 0, large = 0;
        foreach (var room in layout.rooms)
        {
            if (room.template == null) continue;
            counts.TryGetValue(room.template, out int c);
            counts[room.template] = c + 1;
            if (room.template.HasTag(DungeonRoomTags.Rest)) rests++;
            if (room.kind == DungeonRoomKind.Combat)
            {
                if (room.template.sizeClass == DungeonRoomSize.Small) small++;
                else large++;
            }
        }
        int fromDistance = 0;
        bool restPossible = rests < settings.maxRestRooms;
        if (restPossible)
        {
            var distance = Distances(layout);
            fromDistance = from.id >= 0 && from.id < distance.Length && distance[from.id] != int.MaxValue ? distance[from.id] : 0;
        }
        var result = new List<Candidate>(source.Count);
        foreach (var c in source)
        {
            var t = c.template;
            if (t == from.template) continue;
            counts.TryGetValue(t, out int have);
            int limit = t.maxPerFloor > 0 ? t.maxPerFloor : Mathf.Max(1, settings.sameTemplateLimit);
            if (have >= limit)
            {
                limited = true;
                if (!relaxed) continue;
            }
            if (t.HasTag(DungeonRoomTags.Rest) && (!restPossible || fromDistance + 1 < settings.restMinDistance)) continue;
            float w = Mathf.Max(0f, c.weight);
            if (from.template != null && from.template.orientation != DungeonRoomOrientation.Mixed && t.orientation == from.template.orientation)
                w *= Mathf.Clamp01(settings.sameOrientationWeight);
            result.Add(new Candidate { template = t, weight = w, sockets = c.sockets, pads = c.pads });
        }
        if (balanceSize) BalanceSizes(result, Mathf.Clamp01(settings.smallRoomRatio), small, large);
        return result;
    }

    // 다음 전투 방이 작은 방일 확률을 목표 비율에 맞게 정하고(지금까지 놓은 수로 모자란 쪽을 키움), 작은 쪽·큰 쪽 가중치 합을 그 비율로 맞춘다.
    // 확률은 [0.1, 0.9]로 묶어 한쪽이 완전히 빠지지 않게 한다(목표가 0·1이면 그쪽 끝까지 허용).
    private static void BalanceSizes(List<Candidate> pool, float ratio, int small, int large)
    {
        float ws = 0f, wl = 0f;
        foreach (var c in pool) { if (c.template.sizeClass == DungeonRoomSize.Small) ws += c.weight; else wl += c.weight; }
        if (!(ws > 0f) || !(wl > 0f)) return;
        int placed = small + large;
        float lo = ratio <= 0f ? 0f : .1f, hi = ratio >= 1f ? 1f : .9f;
        float p = Mathf.Clamp(ratio * (placed + 1) - small, lo, hi);
        float smallScale = p / ws, largeScale = (1f - p) / wl;
        foreach (var c in pool) c.weight *= c.template.sizeClass == DungeonRoomSize.Small ? smallScale : largeScale;
    }

    // from의 socket 바깥 단위 칸에 맞은편 출입구를 그 칸에 가진 템플릿을 무작위로 놓아 본다(모든 단위 칸이 비어 있어야 한다).
    // accept가 있으면 그 자리(템플릿, 원점)를 받아들일 때만 놓는다.
    private static bool TryAttach(DungeonLayout layout, DungeonRoomNode from, int socket, List<Candidate> pool, System.Random rng, DungeonRoomKind kind,
        Func<Candidate, Vector2Int, bool> accept, out DungeonRoomNode placed)
    {
        placed = null;
        var outside = Outside(from, socket);
        var want = DungeonShapes.Opposite(from.sockets[socket].side);
        // 가중치 순서로 템플릿을 하나씩 꺼내(뽑은 것은 다시 안 뽑음) 그 템플릿의 맞는 출입구를 무작위 순서로 시도한다.
        var remaining = new List<Candidate>(pool);
        while (remaining.Count > 0)
        {
            var candidate = Pick(remaining, rng);
            remaining.Remove(candidate);
            var matches = new List<int>();
            for (int t = 0; t < candidate.sockets.Count; t++) if (candidate.sockets[t].side == want) matches.Add(t);
            Shuffle(matches, rng);
            foreach (int t in matches)
            {
                var origin = outside - candidate.sockets[t].unit;
                if (!Fits(layout, candidate.template, origin)) continue;
                if (accept != null && !accept(candidate, origin)) continue;
                placed = Place(layout, candidate, origin, kind);
                Link(from, socket, placed, t);
                return true;
            }
        }
        return false;
    }

    // 새 방의 빈 출입구가 이웃 방의 마주 보는 빈 출입구와 맞닿으면 loopChance 확률로 잇는다(보스 방과는 잇지 않는다).
    // 같은 템플릿끼리는 잇지 않고(바로 이웃 금지), 이어서 휴식 방이 시작에 너무 가까워지면 되돌린다.
    private static void TryLoops(Context ctx, DungeonLayout layout, DungeonRoomNode node, System.Random rng)
    {
        float chance = Mathf.Clamp01(ctx.settings.loopChance);
        for (int s = 0; s < node.sockets.Count; s++)
        {
            if (node.IsLinked(s)) continue;
            var outside = Outside(node, s);
            if (!layout.occupancy.TryGetValue(outside, out int otherId) || otherId == node.id) continue;
            var other = layout.rooms[otherId];
            if (other.kind == DungeonRoomKind.Boss || other.template == node.template) continue;
            var want = DungeonShapes.Opposite(node.sockets[s].side);
            int match = -1;
            for (int t = 0; t < other.sockets.Count; t++)
                if (!other.IsLinked(t) && other.sockets[t].side == want && other.sockets[t].unit + other.gridOrigin == outside) { match = t; break; }
            if (match < 0) continue;
            // 확률은 맞닿은 쌍이 있을 때만 굴린다(같은 시드면 같은 순서로 굴린다).
            if (!(rng.NextDouble() < chance)) continue;
            Link(node, s, other, match);
            if (!RestRoomsFarEnough(ctx, layout)) Unlink(node, s, other, match);
        }
    }

    private static bool RestRoomsFarEnough(Context ctx, DungeonLayout layout)
    {
        var settings = ctx.settings;
        if (settings.restMinDistance <= 0) return true;
        int[] distance = null;
        foreach (var room in layout.rooms)
        {
            if (room.template == null || !room.template.HasTag(DungeonRoomTags.Rest)) continue;
            // 지정 목록에서 고른 시작 방(예: 휴식 방 고정 시작)은 거리 규칙에서 뺀다(거리 0이라 모든 고리 연결이 되돌려지지 않게).
            if (ctx.startOverride && room.id == layout.startRoom) continue;
            distance ??= Distances(layout);
            if (distance[room.id] < settings.restMinDistance) return false;
        }
        return true;
    }

    private static bool TryPlaceBoss(DungeonLayout layout, Candidate boss, bool preferSmall, System.Random rng)
    {
        var distance = Distances(layout);
        // 시작에서 먼 방부터(동률이면 작은 방 → 연결 수가 적은 방 → id 순서) 빈 출입구에 붙여 본다.
        var order = new List<DungeonRoomNode>(layout.rooms);
        order.Sort((a, b) => CompareBossOrder(a, b, distance, preferSmall));
        foreach (var room in order)
        {
            var sockets = new List<int>();
            for (int s = 0; s < room.sockets.Count; s++)
                if (!room.IsLinked(s) && !layout.occupancy.ContainsKey(Outside(room, s))) sockets.Add(s);
            Shuffle(sockets, rng);
            foreach (int s in sockets)
            {
                var outside = Outside(room, s);
                var want = DungeonShapes.Opposite(room.sockets[s].side);
                var matches = new List<int>();
                for (int t = 0; t < boss.sockets.Count; t++) if (boss.sockets[t].side == want) matches.Add(t);
                Shuffle(matches, rng);
                foreach (int t in matches)
                {
                    var origin = outside - boss.sockets[t].unit;
                    if (!Fits(layout, boss.template, origin)) continue;
                    var placed = Place(layout, boss, origin, DungeonRoomKind.Boss);
                    Link(room, s, placed, t);
                    layout.bossRoom = placed.id;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>보스 방을 붙일 방의 순서: 시작에서 먼 방 → (preferSmall이면) 작은 방 → 연결 수 적은 방 → id. 회귀 검사도 이 순서를 쓴다.</summary>
    public static int CompareBossOrder(DungeonRoomNode a, DungeonRoomNode b, int[] distance, bool preferSmall)
        => CompareBossOrder(a, b, distance, preferSmall, a.links.Count, b.links.Count);

    /// <summary>연결 수를 따로 주는 판(회귀 검사가 보스 방을 놓기 전의 연결 수로 비교할 때).</summary>
    public static int CompareBossOrder(DungeonRoomNode a, DungeonRoomNode b, int[] distance, bool preferSmall, int linksA, int linksB)
    {
        int c = distance[b.id].CompareTo(distance[a.id]);
        if (c != 0) return c;
        if (preferSmall)
        {
            bool sa = a.template != null && a.template.sizeClass == DungeonRoomSize.Small;
            bool sb = b.template != null && b.template.sizeClass == DungeonRoomSize.Small;
            if (sa != sb) return sa ? -1 : 1;
        }
        c = linksA.CompareTo(linksB);
        return c != 0 ? c : a.id.CompareTo(b.id);
    }

    /// <summary>시작 방에서 연결을 따라간 거리(단계 수). 닿지 않는 방은 int.MaxValue.</summary>
    public static int[] Distances(DungeonLayout layout) => DistancesFrom(layout, layout.startRoom);

    /// <summary>from 방에서 연결을 따라간 거리(단계 수). 닿지 않는 방은 int.MaxValue.</summary>
    public static int[] DistancesFrom(DungeonLayout layout, int from)
    {
        var distance = new int[layout.rooms.Count];
        for (int i = 0; i < distance.Length; i++) distance[i] = int.MaxValue;
        if (distance.Length == 0 || from < 0 || from >= distance.Length) return distance;
        var queue = new Queue<int>();
        distance[from] = 0;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            foreach (var link in layout.rooms[id].links)
            {
                if (link.toRoom < 0 || link.toRoom >= distance.Length || distance[link.toRoom] != int.MaxValue) continue;
                distance[link.toRoom] = distance[id] + 1;
                queue.Enqueue(link.toRoom);
            }
        }
        return distance;
    }

    // ── 텔레포트 짝짓기 ──
    // 방 수(시작·보스 포함)가 teleportMinRooms 이상이면 maxTeleportPairs쌍(teleportSecondPairRooms 이상이면 하나 더)을 짓는다.
    // 두 방: 텔레포트를 받을 수 있는 템플릿(시설 + 꼬리표), 보스 방 아님, 그래프 거리 ≥ teleportMinGraphDistance, 격자 거리 ≥ teleportMinGridDistance.
    // 한쪽이 막다른 방(연결 1개)인 쌍을 우선한다. 짝이 없으면 막다른 출입구에 TeleportNode 템플릿을 덧붙여 만든다(가능할 때).
    private static void AssignTeleports(Context ctx, DungeonLayout layout, System.Random rng, StringBuilder notes)
    {
        var settings = ctx.settings;
        if (!settings.teleports || settings.maxTeleportPairs <= 0) return;
        int total = layout.rooms.Count;
        if (total < settings.teleportMinRooms) return;
        int pairs = settings.maxTeleportPairs + (total >= settings.teleportSecondPairRooms ? 1 : 0);
        var used = new Dictionary<int, List<int>>();
        for (int p = 0; p < pairs; p++)
        {
            if (TryPair(ctx, layout, used, rng)) continue;
            if (TryAppendPair(ctx, layout, used, rng, notes)) continue;
            Note(notes, $"텔레포트 짝 {p + 1}번째를 지을 방을 찾지 못했습니다(거리 조건·덧붙일 자리 부족).");
            break;
        }
    }

    private static int FreePads(Context ctx, DungeonRoomNode node, Dictionary<int, List<int>> used)
    {
        if (node == null || node.kind == DungeonRoomKind.Boss || node.template == null) return 0;
        if (!node.template.HasTag(DungeonRoomTags.TeleportHub) && !node.template.HasTag(DungeonRoomTags.TeleportNode)) return 0;
        int pads = PadCount(ctx, node.template);
        return used.TryGetValue(node.id, out var list) ? pads - list.Count : pads;
    }

    private static bool PairAllowed(Context ctx, DungeonLayout layout, DungeonRoomNode a, DungeonRoomNode b, int[] distanceFromA)
    {
        if (a == null || b == null || a.id == b.id) return false;
        int graph = distanceFromA[b.id];
        if (graph == int.MaxValue || graph < ctx.settings.teleportMinGraphDistance) return false;
        return GridDistance(a, b) >= ctx.settings.teleportMinGridDistance;
    }

    private static bool TryPair(Context ctx, DungeonLayout layout, Dictionary<int, List<int>> used, System.Random rng)
    {
        var eligible = new List<DungeonRoomNode>();
        foreach (var room in layout.rooms) if (FreePads(ctx, room, used) > 0) eligible.Add(room);
        if (eligible.Count < 2) return false;
        var preferred = new List<(DungeonRoomNode, DungeonRoomNode)>();
        var others = new List<(DungeonRoomNode, DungeonRoomNode)>();
        for (int i = 0; i < eligible.Count; i++)
        {
            var distance = DistancesFrom(layout, eligible[i].id);
            for (int j = i + 1; j < eligible.Count; j++)
            {
                if (!PairAllowed(ctx, layout, eligible[i], eligible[j], distance)) continue;
                bool deadEnd = IsDeadEnd(eligible[i]) || IsDeadEnd(eligible[j]);
                (deadEnd ? preferred : others).Add((eligible[i], eligible[j]));
            }
        }
        var list = preferred.Count > 0 ? preferred : others;
        if (list.Count == 0) return false;
        var (a, b) = list[rng.Next(list.Count)];
        AddPair(ctx, layout, used, a, b, rng);
        return true;
    }

    private static bool IsDeadEnd(DungeonRoomNode node) => node.links.Count <= 1;

    private static void AddPair(Context ctx, DungeonLayout layout, Dictionary<int, List<int>> used, DungeonRoomNode a, DungeonRoomNode b, System.Random rng)
    {
        int padA = TakePad(ctx, a, used, rng), padB = TakePad(ctx, b, used, rng);
        layout.teleports.Add(new DungeonTeleportLink { roomA = a.id, padA = padA, roomB = b.id, padB = padB });
    }

    private static int TakePad(Context ctx, DungeonRoomNode node, Dictionary<int, List<int>> used, System.Random rng)
    {
        if (!used.TryGetValue(node.id, out var list)) used[node.id] = list = new List<int>();
        var free = new List<int>();
        int pads = PadCount(ctx, node.template);
        for (int i = 0; i < pads; i++) if (!list.Contains(i)) free.Add(i);
        int pad = free.Count > 0 ? free[rng.Next(free.Count)] : 0;
        list.Add(pad);
        return pad;
    }

    // 짝 없는 시설 방(E)에서 멀리 떨어진 막다른 출입구에 TeleportNode 템플릿을 붙여 짝을 만든다. 시설 방이 하나도 없으면 끝점 두 개를 붙인다.
    private static bool TryAppendPair(Context ctx, DungeonLayout layout, Dictionary<int, List<int>> used, System.Random rng, StringBuilder notes)
    {
        if (ctx.nodes.Count == 0)
        {
            Note(notes, "텔레포트 끝점(TeleportNode 꼬리표 + 시설) 템플릿이 없어 끝점 방을 덧붙이지 못했습니다.");
            return false;
        }
        var eligible = new List<DungeonRoomNode>();
        foreach (var room in layout.rooms) if (FreePads(ctx, room, used) > 0) eligible.Add(room);
        Shuffle(eligible, rng);
        foreach (var partner in eligible)
            if (TryAppendNodeFor(ctx, layout, partner, rng, out var node)) { AddPair(ctx, layout, used, partner, node, rng); return true; }
        // 시설 방이 없으면: 시작에서 먼 막다른 출입구에 끝점 하나를 붙이고, 그 끝점과 짝이 될 끝점을 하나 더 붙인다(안 되면 첫 끝점을 거둔다).
        var sockets = OpenSockets(layout, null);
        var startDistance = Distances(layout);
        Shuffle(sockets, rng);
        sockets.Sort((x, y) => startDistance[y.room].CompareTo(startDistance[x.room]));
        int tries = 0;
        foreach (var (roomId, socket) in sockets)
        {
            if (++tries > MaxAppendSockets) break;
            var from = layout.rooms[roomId];
            if (!AttachNode(ctx, layout, from, socket, rng, null, out var first)) continue;
            if (TryAppendNodeFor(ctx, layout, first, rng, out var second))
            {
                AddPair(ctx, layout, used, first, second, rng);
                return true;
            }
            RemoveLast(layout);
        }
        return false;
    }

    // partner에서 그래프 거리·격자 거리 조건을 채우는 막다른 출입구에 TeleportNode 방을 붙인다.
    private static bool TryAppendNodeFor(Context ctx, DungeonLayout layout, DungeonRoomNode partner, System.Random rng, out DungeonRoomNode node)
    {
        node = null;
        var distance = DistancesFrom(layout, partner.id);
        var sockets = OpenSockets(layout, r => distance[r.id] != int.MaxValue && distance[r.id] + 1 >= ctx.settings.teleportMinGraphDistance);
        Shuffle(sockets, rng);
        // 멀리 있는 출입구부터(같은 거리는 섞은 순서).
        sockets.Sort((x, y) => distance[y.room].CompareTo(distance[x.room]));
        int tries = 0;
        foreach (var (roomId, socket) in sockets)
        {
            if (++tries > MaxAppendSockets) break;
            var from = layout.rooms[roomId];
            bool Accept(Candidate c, Vector2Int origin)
            {
                int best = int.MaxValue;
                foreach (var u in DungeonShapes.Units(c.template.shape))
                    foreach (var v in partner.Units)
                        best = Mathf.Min(best, Mathf.Abs(u.x + origin.x - v.x) + Mathf.Abs(u.y + origin.y - v.y));
                return best >= ctx.settings.teleportMinGridDistance;
            }
            if (AttachNode(ctx, layout, from, socket, rng, Accept, out node)) return true;
        }
        return false;
    }

    private static bool AttachNode(Context ctx, DungeonLayout layout, DungeonRoomNode from, int socket, System.Random rng, Func<Candidate, Vector2Int, bool> accept, out DungeonRoomNode node)
    {
        var pool = Pool(ctx, layout, from, ctx.nodes, false, false, out bool limited);
        if (!TryAttach(layout, from, socket, pool, rng, DungeonRoomKind.Combat, accept, out node) && limited)
        {
            pool = Pool(ctx, layout, from, ctx.nodes, true, false, out _);
            if (!TryAttach(layout, from, socket, pool, rng, DungeonRoomKind.Combat, accept, out node)) return false;
            // 생성 규칙 5: 제한을 풀었으면 그 사실을 남긴다(배치 단계와 같은 문장 규칙 — '반복 제한').
            const string relaxedNote = "텔레포트 끝점 방을 붙이느라 같은 템플릿 반복 제한(던전당 최대 수)을 풀었습니다.";
            if (!ctx.attemptNotes.Contains(relaxedNote)) ctx.attemptNotes.Add(relaxedNote);
        }
        if (node == null) return false;
        // 덧붙인 방의 적 구성은 텔레포트 난수로 고른다(배치 난수를 건드리지 않는다). 거리로 난이도를 정하면 덧붙인 뒤의 거리로 본다.
        int[] distance = ctx.settings.tierByDistance ? Distances(layout) : null;
        PickEncounter(ctx.settings, node, rng, null, distance, distance != null ? FarthestForTier(layout, distance) : 0);
        return true;
    }

    // 보스 방이 아닌 방의 빈 출입구(바깥 단위 칸이 빈 것). filter가 있으면 그 방만.
    private static List<(int room, int socket)> OpenSockets(DungeonLayout layout, Func<DungeonRoomNode, bool> filter)
    {
        var result = new List<(int, int)>();
        foreach (var room in layout.rooms)
        {
            if (room.kind == DungeonRoomKind.Boss || (filter != null && !filter(room))) continue;
            for (int s = 0; s < room.sockets.Count; s++)
                if (!room.IsLinked(s) && !layout.occupancy.ContainsKey(Outside(room, s))) result.Add((room.id, s));
        }
        return result;
    }

    // 마지막에 놓은 방을 거둔다(덧붙인 끝점 되돌리기 전용 — 연결은 그 방으로 가는 것뿐이다).
    private static void RemoveLast(DungeonLayout layout)
    {
        if (layout.rooms.Count == 0) return;
        var node = layout.rooms[layout.rooms.Count - 1];
        foreach (var unit in node.Units)
            if (layout.occupancy.TryGetValue(unit, out int id) && id == node.id) layout.occupancy.Remove(unit);
        foreach (var link in node.links)
        {
            var other = layout.Room(link.toRoom);
            other?.links.RemoveAll(l => l.toRoom == node.id);
        }
        layout.rooms.RemoveAt(layout.rooms.Count - 1);
    }

    // ── 적 구성 ──
    // 전투 방마다 적 구성을 [minTier, maxTier] 안에서 가중치로 고른다(없으면 가장 가까운 난이도). 시작 방은 0(적 없음).
    // tierByDistance면 방마다 난이도 하나(TierForDistance)를 목표로 고른다(그 난이도가 없으면 가장 가까운 난이도 — 같은 규칙).
    private static void PickEncounters(Context ctx, DungeonLayout layout, System.Random rng, StringBuilder notes)
    {
        int[] distance = ctx.settings.tierByDistance ? Distances(layout) : null;
        int far = distance != null ? FarthestForTier(layout, distance) : 0;
        foreach (var node in layout.rooms) PickEncounter(ctx.settings, node, rng, notes, distance, far);
    }

    private static void PickEncounter(DungeonWaveSettings settings, DungeonRoomNode node, System.Random rng, StringBuilder notes, int[] distance = null, int far = 0)
    {
        // 4차: 동적 구성 던전의 전투 방은 들어오는 순간 적 구성을 짠다(난수를 쓰지 않으므로 아래 손 배치 구성 고르기와 결과는 그대로다).
        node.dynamicFight = node.kind == DungeonRoomKind.Combat && settings.UsesDynamicEncounters;
        if (node.kind == DungeonRoomKind.Start) { node.encounter = 0; return; }
        int count = node.template.EncounterCount;
        if (node.kind == DungeonRoomKind.Boss)
        {
            if (count == 0) { node.encounter = 0; Note(notes, $"보스 방 템플릿 '{node.template.name}'에 적 구성이 없습니다(들어가면 바로 클리어)."); return; }
            int wanted = Mathf.Clamp(settings.bossEncounter, 1, count);
            if (wanted != settings.bossEncounter) Note(notes, $"보스 적 구성 번호 {settings.bossEncounter}가 범위를 벗어나 {wanted}번을 씁니다.");
            node.encounter = wanted;
            return;
        }
        if (count == 0)
        {
            node.encounter = 0;
            // 동적 구성 방은 손 배치 구성이 없어도 싸운다(템플릿 규칙 v4-9 — 없어도 됨).
            if (!node.dynamicFight) Note(notes, $"템플릿 '{node.template.name}'에 적 구성이 없습니다(들어가면 바로 클리어).");
            return;
        }
        int min = settings.ClampedMinTier, max = settings.ClampedMaxTier;
        if (distance != null && node.id >= 0 && node.id < distance.Length && distance[node.id] != int.MaxValue)
            min = max = TierForDistance(settings, distance[node.id], far);
        var choices = new List<(int number, float weight)>();
        int best = int.MaxValue;
        for (int n = 1; n <= count; n++)
        {
            var info = Info(node.template, n);
            int tier = info != null ? info.tier : 1;
            int gap = tier < min ? min - tier : tier > max ? tier - max : 0;
            float weight = info != null ? Mathf.Max(0f, info.weight) : 1f;
            if (gap < best) { best = gap; choices.Clear(); }
            if (gap == best) choices.Add((n, weight));
        }
        float total = 0f;
        foreach (var c in choices) total += c.weight;
        if (!(total > 0f))
        {
            // 가중치가 모두 0이면 같은 비율로 고른다.
            node.encounter = choices[rng.Next(choices.Count)].number;
            return;
        }
        double roll = rng.NextDouble() * total;
        node.encounter = choices[choices.Count - 1].number;
        foreach (var c in choices)
        {
            roll -= c.weight;
            if (roll < 0) { node.encounter = c.number; break; }
        }
    }

    // 적 구성 정보(번호 1부터). EncounterInfo()는 길이를 맞추며 에셋을 고치므로 여기서는 읽기만 한다.
    private static DungeonEncounterInfo Info(DungeonRoomTemplateSO template, int number)
    {
        var list = template.encounters;
        return list != null && number >= 1 && number <= list.Length ? list[number - 1] : null;
    }

    private static DungeonRoomNode Place(DungeonLayout layout, Candidate candidate, Vector2Int origin, DungeonRoomKind kind)
    {
        var node = new DungeonRoomNode { id = layout.rooms.Count, template = candidate.template, gridOrigin = origin, kind = kind };
        node.sockets.AddRange(candidate.sockets);
        layout.rooms.Add(node);
        foreach (var unit in DungeonShapes.Units(candidate.template.shape)) layout.occupancy[unit + origin] = node.id;
        return node;
    }

    private static bool Fits(DungeonLayout layout, DungeonRoomTemplateSO template, Vector2Int origin)
    {
        foreach (var unit in DungeonShapes.Units(template.shape))
            if (layout.occupancy.ContainsKey(unit + origin)) return false;
        return true;
    }

    private static void Link(DungeonRoomNode a, int socketA, DungeonRoomNode b, int socketB)
    {
        a.links.Add(new DungeonDoorLink { socket = socketA, toRoom = b.id, toSocket = socketB });
        b.links.Add(new DungeonDoorLink { socket = socketB, toRoom = a.id, toSocket = socketA });
    }

    private static void Unlink(DungeonRoomNode a, int socketA, DungeonRoomNode b, int socketB)
    {
        a.links.RemoveAll(l => l.socket == socketA && l.toRoom == b.id && l.toSocket == socketB);
        b.links.RemoveAll(l => l.socket == socketB && l.toRoom == a.id && l.toSocket == socketA);
    }

    /// <summary>node의 socket번 출입구 바깥(벽 너머)의 던전 격자 단위 칸.</summary>
    public static Vector2Int Outside(DungeonRoomNode node, int socket)
    {
        var s = node.sockets[socket];
        return node.gridOrigin + s.unit + DungeonShapes.Outward(s.side);
    }

    private static Candidate Pick(List<Candidate> list, System.Random rng)
    {
        float total = 0f;
        foreach (var c in list) total += Mathf.Max(0f, c.weight);
        if (!(total > 0f)) return list[rng.Next(list.Count)];
        double roll = rng.NextDouble() * total;
        foreach (var c in list)
        {
            roll -= Mathf.Max(0f, c.weight);
            if (roll < 0) return c;
        }
        return list[list.Count - 1];
    }

    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static void Note(StringBuilder notes, string line)
    {
        if (notes == null || notes.ToString().Contains(line)) return;
        notes.AppendLine(line);
    }

    private static string Join(StringBuilder notes, string main) => notes.Length > 0 ? main + "\n" + notes.ToString().TrimEnd() : main;
}
