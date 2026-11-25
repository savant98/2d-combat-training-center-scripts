using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 여러 목표 SO가 함께 쓰는 '멀리·흩어 고르기' 방 선택(생성 규칙 v3-12, understand3/gimmick.md §2-3). 순수 C#이며 주어진 난수만 쓴다.
/// 후보: 전투 방(적 구성 있음, 시작·보스 제외), 휴식 방은 allowRestRooms일 때만, 덧붙인 끝점 방은 allowAppendedNodes일 때만, 목표 슬롯이 있는 방. 방당 하나.
/// 조건: 시작에서의 BFS 거리 ≥ max(minStartDistance, round(startDistanceRatio × 최대 거리)), 고른 방끼리 BFS 거리 ≥ minSpacing.
/// 순서: 첫 방은 거리² 가중 무작위 → 다음 방은 점수(고른 방까지 최소 거리 + 0.5 × 시작 거리 + 다른 가지면 1)가 높은 상위 2개 중 무작위.
/// 완화: 간격 minSpacing → … → 1, 그다음 시작 거리 하한을 1씩 낮춰 다시 고른다(단계마다 몇 번 다시 굴림). 그래도 모자라면 실패(생성기가 시드+1로 다시 만든다).
/// </summary>
public static class DungeonObjectiveRoomPicker
{
    /// <summary>완화해서 배치했을 때 알림 문장의 머리(격자 Tool 미리보기·Verify·회귀가 찾는다).</summary>
    public const string RelaxedNote = "[목표] 완화";
    // 완화 단계마다 다시 굴려 보는 횟수(첫 방 선택이 무작위라 다시 굴리면 다른 조합이 나온다).
    private const int TriesPerLevel = 4;

    /// <summary>방 고르기 규칙(목표 SO의 필드를 옮겨 담는다).</summary>
    public struct Rules
    {
        public int count;
        public int minStartDistance;
        public float startDistanceRatio;
        public int minSpacing;
        public bool allowRestRooms;
        public bool allowAppendedNodes;
    }

    /// <summary>고른 방 하나(방 id·슬롯 순번·시작에서의 거리).</summary>
    public struct Pick
    {
        public int room;
        public int slot;
        public int distance;
    }

    /// <summary>
    /// 규칙대로 방과 슬롯을 고른다. 성공하면 picked에 rules.count개를 시작에서 가까운 순서(같으면 방 id 순서)로 채우고 true.
    /// 완화했으면 notes에 <see cref="RelaxedNote"/>로 시작하는 한 줄을, 실패하면 사유 한 줄을 남긴다. label은 알림에 쓸 목표 이름이다.
    /// </summary>
    public static bool TryPick(DungeonObjectivePlanContext context, System.Random rng, Rules rules, List<Pick> picked, StringBuilder notes, string label)
    {
        picked.Clear();
        var layout = context != null ? context.layout : null;
        if (layout == null || rng == null) { notes?.AppendLine($"[목표] {label}: 던전 배치가 없습니다."); return false; }
        int count = Mathf.Max(1, rules.count);
        var fromStart = context.distanceFromStart ?? Array.Empty<int>();
        int Dist(int id) => id >= 0 && id < fromStart.Length ? fromStart[id] : -1;

        // 1. 후보 방
        var candidates = new List<DungeonRoomNode>();
        var slots = new Dictionary<int, int>();
        int far = 0;
        foreach (var room in layout.rooms)
        {
            if (room == null) continue;
            int d = Dist(room.id);
            if (d > 0 && room.kind != DungeonRoomKind.Boss) far = Mathf.Max(far, d);
            // 4차 통합: 손 배치 구성 번호 대신 HasFight(손 배치 구성 있음 또는 동적 구성 전투 방)로 본다 — 구성 없는 동적 템플릿도 목표 후보가 된다.
            if (room.kind != DungeonRoomKind.Combat || !room.HasFight || d <= 0 || room.template == null) continue;
            if (!rules.allowRestRooms && room.template.HasTag(DungeonRoomTags.Rest)) continue;
            if (!rules.allowAppendedNodes && room.id >= context.appendedFrom) continue;
            int slotCount = context.slotCount != null ? context.slotCount(room) : 0;
            if (slotCount <= 0) continue;
            slots[room.id] = slotCount;
            candidates.Add(room);
        }
        if (candidates.Count < count)
        {
            notes?.AppendLine($"[목표] {label}: {count}개를 놓을 방이 모자랍니다(목표 슬롯이 있는 전투 방 {candidates.Count}개).");
            return false;
        }

        // 2. 후보끼리의 거리와 가지(시작 방에서 나가는 첫 연결)
        var pair = new Dictionary<int, int[]>();
        foreach (var room in candidates) pair[room.id] = DungeonGenerator.DistancesFrom(layout, room.id);
        var branch = Branches(layout);

        int strictStart = Mathf.Max(Mathf.Max(1, rules.minStartDistance), Mathf.FloorToInt(Mathf.Clamp01(rules.startDistanceRatio) * far + .5f));
        int strictSpacing = Mathf.Max(1, rules.minSpacing);
        var chosen = new List<DungeonRoomNode>();
        for (int start = strictStart; start >= 1; start--)
        {
            for (int spacing = strictSpacing; spacing >= 1; spacing--)
            {
                for (int attempt = 0; attempt < TriesPerLevel; attempt++)
                {
                    if (!TryGreedy(candidates, Dist, pair, branch, start, spacing, count, rng, chosen)) continue;
                    foreach (var room in chosen) picked.Add(new Pick { room = room.id, slot = rng.Next(slots[room.id]), distance = Dist(room.id) });
                    picked.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) : a.room.CompareTo(b.room));
                    if (start != strictStart || spacing != strictSpacing)
                        notes?.AppendLine($"{RelaxedNote} {label}: 조건을 줄여 배치했습니다(시작 거리 ≥ {start}, 기준 {strictStart} / 간격 ≥ {spacing}, 기준 {strictSpacing}).");
                    return true;
                }
            }
        }
        notes?.AppendLine($"[목표] {label}: {count}개를 서로 떨어진 방에 놓지 못했습니다(후보 {candidates.Count}개, 최대 거리 {far}).");
        return false;
    }

    // 한 번의 '멀리·흩어 고르기'. 성공하면 chosen에 count개.
    private static bool TryGreedy(List<DungeonRoomNode> candidates, Func<int, int> dist, Dictionary<int, int[]> pair, int[] branch,
        int minStart, int spacing, int count, System.Random rng, List<DungeonRoomNode> chosen)
    {
        chosen.Clear();
        var pool = candidates.FindAll(r => dist(r.id) >= minStart);
        if (pool.Count < count) return false;
        // 첫 방: 시작 거리² 가중 무작위(먼 방일수록 잘 뽑힌다).
        double total = 0;
        foreach (var room in pool) total += (double)dist(room.id) * dist(room.id);
        double roll = rng.NextDouble() * total;
        var first = pool[pool.Count - 1];
        foreach (var room in pool)
        {
            roll -= (double)dist(room.id) * dist(room.id);
            if (roll < 0) { first = room; break; }
        }
        chosen.Add(first);
        var scored = new List<(DungeonRoomNode room, double score)>();
        while (chosen.Count < count)
        {
            scored.Clear();
            foreach (var room in pool)
            {
                if (chosen.Contains(room)) continue;
                int nearest = int.MaxValue;
                bool newBranch = true;
                foreach (var other in chosen)
                {
                    int d = pair[other.id][room.id];
                    nearest = Mathf.Min(nearest, d);
                    if (Branch(branch, other.id) == Branch(branch, room.id)) newBranch = false;
                }
                if (nearest == int.MaxValue || nearest < spacing) continue;
                scored.Add((room, nearest + .5 * dist(room.id) + (newBranch ? 1 : 0)));
            }
            if (scored.Count == 0) return false;
            scored.Sort((a, b) => a.score != b.score ? b.score.CompareTo(a.score) : a.room.id.CompareTo(b.room.id));
            chosen.Add(scored[rng.Next(Mathf.Min(2, scored.Count))].room);
        }
        return true;
    }

    private static int Branch(int[] branch, int id) => id >= 0 && id < branch.Length ? branch[id] : -1;

    // 시작 방 BFS 트리에서 방마다 '시작 방 바로 옆 조상'(가지). 시작 방 자신과 닿지 않는 방은 -1.
    private static int[] Branches(DungeonLayout layout)
    {
        var branch = new int[layout.rooms.Count];
        for (int i = 0; i < branch.Length; i++) branch[i] = -1;
        int start = layout.startRoom;
        if (start < 0 || start >= branch.Length) return branch;
        var seen = new bool[branch.Length];
        var queue = new Queue<int>();
        seen[start] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            foreach (var link in layout.rooms[id].links)
            {
                int to = link.toRoom;
                if (to < 0 || to >= branch.Length || seen[to]) continue;
                seen[to] = true;
                branch[to] = id == start ? to : branch[id];
                queue.Enqueue(to);
            }
        }
        return branch;
    }
}
