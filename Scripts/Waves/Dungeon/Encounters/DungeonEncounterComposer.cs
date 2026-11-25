using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>동적 적 구성 요청(방 하나, 들어오는 순간).</summary>
public sealed class DungeonEncounterRequest
{
    public DungeonEncounterRulesSO rules;
    /// <summary>방의 생성 후보 지점. 결과 spawnPosition은 이 맵과 같은 좌표다(방 데이터 좌표로 만들면 DungeonEncounter에 Origin()과 함께 그대로 넘긴다).</summary>
    public DungeonSpawnPointMap map;
    /// <summary>들어온 순간의 난이도 단계.</summary>
    public DungeonThreatLevel level;
    /// <summary>클리어한 방 재입장(순찰 구성 — 수 × patrolRatio, 최소 patrolMin, Bulwark 없음, 한 번에 생성).</summary>
    public bool patrol;
    /// <summary>방의 빈 칸 수 A(실내 − 고체 지형 − 고체 Prop). 0 이하이면 map.EmptyCells를 쓴다.</summary>
    public int emptyCells;
    /// <summary>실내 높이(칸) — Bulwark 조건. 0 이하이면 map.InteriorHeight를 쓴다.</summary>
    public int interiorHeight;
    /// <summary>방 방향(템플릿 orientation) — 수평 방은 지상, 수직 방은 공중 종류 보정.</summary>
    public DungeonRoomOrientation orientation = DungeonRoomOrientation.Mixed;
    /// <summary>구성 난수 시드(실행 시드·층·방·방문 순번으로 만든 값 — <see cref="DungeonEncounterComposer.Seed"/>). 같은 시드면 같은 구성.</summary>
    public int seed;
}

/// <summary>
/// 동적 적 구성(4차 R16~R18, 순수 C#, DESIGN_V4 §2.8). 요청(단계·빈 칸·방향·후보 지점)으로 이 방의 적 항목을 짠다.
/// 순서:
/// 1. 적 수(환산 단위, Bulwark = 3) = 규칙 SO의 TargetUnits(단계·빈 칸·순찰).
/// 2. Bulwark: 중급 이상·실내 높이 ≥ 17·연속 바닥 ≥ 8 + 머리 위 5인 자리, 방당 1(특급 & 빈 칸 ≥ 1000이면 2). 순찰에는 넣지 않는다.
///    다른 적을 3단위 이상 남길 수 있을 때만 넣는다. 첫째는 첫 파도, 둘째는 secondDelay(6초) 뒤 파도에 둔다.
/// 3. 파도: 첫 파도 ≈ firstWaveRatio(40%, 동시 생존 상한 이하), 나머지는 laterWaves개로 나눠 waveInterval(3초) 간격. 순찰은 한 번에 낸다.
/// 4. 파도마다 종류를 가중치(단계 × 방 방향 보정)로 뽑아 후보 지점에 놓는다. Swarmer는 3마리 묶음(반경 1.5칸 안), 방당 최대 4묶음.
///    지상 종류에 묶음 수(groupSize &gt; 1)를 주면 묶음 몸 폭 안에 발 높이를 같게 나란히 둔다(폭 전체 아래 받침 필요) — 예산·기록과 실제 수가 같다.
///    놓을 수 없는 종류는 그 파도에서 빼고, 남은 단위는 다음 파도로 넘긴다(끝까지 남으면 알림).
/// 5. 거리: 첫 파도는 Player 몸에서 firstWaveClearance(8칸), 이후·순찰은 playerClearance(6칸) 밖. 같은 파도의 다른 적과 몸 크기 + .5칸.
/// 결과 항목: prefab·spawnPosition(Prefab 원점 — 지상은 발 + spawnOffset, 공중은 몸 상자 가운데 + spawnOffset)·spawnDelay(그 항목 '뒤'의 대기 — DungeonEncounter 규칙)·
/// isRequiredToClear true(순찰도 내부적으로 필수로 세고, 잠금·클리어 처리는 DungeonManager가 bypass로 건너뛴다)·isBossEnemy false.
/// 같은 요청·시드면 같은 결과다(System.Random, 정해진 순서). 맵의 Player 거리 설정은 끝나면 원래대로 돌려 둔다.
/// </summary>
public static class DungeonEncounterComposer
{
    /// <summary>계획보다 더 붙일 수 있는 파도 수(자리가 없어 남은 단위를 다시 시도).</summary>
    public const int ExtraWaves = 2;
    /// <summary>Bulwark를 넣어도 다른 적에게 남겨 둘 최소 단위.</summary>
    public const int MinOthersWithBulwark = 3;

    /// <summary>요청으로 적 항목을 짠다. notes가 null이 아니면 고른 근거·실패 사유를 적는다. 늘 null이 아닌 목록을 돌려준다.</summary>
    public static List<WaveSpawnInfo> Compose(DungeonEncounterRequest request, List<string> notes)
    {
        var result = new List<WaveSpawnInfo>();
        if (request == null || request.rules == null)
        {
            notes?.Add("요청 또는 생성 규칙이 없어 적을 만들지 않습니다.");
            return result;
        }
        var rules = request.rules;
        var map = request.map;
        var level = request.level;
        bool patrol = request.patrol;
        int empty = request.emptyCells > 0 ? request.emptyCells : map != null ? map.EmptyCells : 0;
        int height = request.interiorHeight > 0 ? request.interiorHeight : map != null ? map.InteriorHeight : 0;
        int target = rules.TargetUnits(level, empty, patrol);
        notes?.Add($"단계 {DungeonThreatText.Label(level)}({DungeonThreatText.Short(level)}) · 빈 칸 {empty} · 실내 높이 {height} · 방향 {request.orientation}{(patrol ? " · 재입장 순찰" : "")}"
            + $" → 목표 {target}단위(D0 {rules.baseDensity:0.##} × 배율 {rules.Multiplier(level):0.##} × 빈 칸 / 100, 최소 {rules.MinCount(level)}·최대 {rules.maxCount}"
            + (patrol ? $", 순찰 × {rules.patrolRatio:0.##} 최소 {rules.patrolMin}" : "") + ")");
        if (map == null || map.Count == 0)
        {
            notes?.Add("생성 후보 지점이 없어 적을 만들지 않습니다(맵 없음 또는 후보 0).");
            return result;
        }
        if (target <= 0) return result;

        var rng = new System.Random(request.seed);
        var species = rules.species ?? Array.Empty<DungeonSpeciesRule>();
        int n = species.Length;
        var weights = new float[n];
        var groups = new int[n];
        var failed = new bool[n];
        float bias = Mathf.Max(0f, rules.orientationBias);
        var weightText = new List<string>(n);
        for (int i = 0; i < n; i++)
        {
            var s = species[i];
            if (s == null || s.prefab == null) continue;
            float w = s.Weight(level);
            if (request.orientation == DungeonRoomOrientation.Horizontal && !s.air) w *= bias;
            if (request.orientation == DungeonRoomOrientation.Vertical && s.air) w *= bias;
            weights[i] = w;
            weightText.Add($"{Name(s)} {w:0.#}");
        }
        notes?.Add("종류 가중치(단계·방향 보정): " + (weightText.Count > 0 ? string.Join(" · ", weightText) : "없음(Prefab 미연결)"));

        // Bulwark 수(조건·예산)
        var bulwark = rules.bulwark;
        int bulwarkUnits = bulwark != null ? Mathf.Max(1, bulwark.units) : 3;
        int bulwarkMax = patrol || bulwark == null ? 0 : bulwark.MaxFor(level, height, empty);
        int bulwarkPlanned = 0;
        while (bulwarkPlanned < bulwarkMax && target - (bulwarkPlanned + 1) * bulwarkUnits >= MinOthersWithBulwark) bulwarkPlanned++;
        notes?.Add("Bulwark: " + BulwarkReason(bulwark, patrol, level, height, empty, bulwarkMax, bulwarkPlanned, target));

        // 파도 크기(Bulwark를 뺀 나머지 단위)
        int cap = rules.ConcurrentCap(level);
        int others = target - bulwarkPlanned * bulwarkUnits;
        var sizes = new List<int>();
        if (patrol)
        {
            for (int rest = others; rest > 0; rest -= Mathf.Min(rest, cap)) sizes.Add(Mathf.Min(rest, cap));
        }
        else
        {
            int firstBulwark = bulwarkPlanned > 0 ? bulwarkUnits : 0;
            int first = Mathf.Clamp(Mathf.RoundToInt(target * Mathf.Clamp01(rules.firstWaveRatio)) - firstBulwark, 0, Mathf.Max(0, cap - (firstBulwark > 0 ? 1 : 0)));
            first = Mathf.Min(first, others);
            if (first == 0 && others > 0 && firstBulwark == 0) first = Mathf.Min(others, cap);
            sizes.Add(first);
            int rest = others - first;
            int per = rest > 0 ? Mathf.Clamp(Mathf.CeilToInt(rest / (float)Mathf.Max(1, rules.laterWaves)), 1, cap) : 0;
            while (rest > 0) { int take = Mathf.Min(per, rest); sizes.Add(take); rest -= take; }
        }
        // 둘째 Bulwark는 첫째 뒤 secondDelay 이상 지난 파도(모자라면 빈 파도를 늘린다).
        float interval = Mathf.Max(0f, rules.waveInterval);
        int secondWave = bulwarkPlanned > 1 ? Mathf.Max(1, interval > 0f ? Mathf.CeilToInt(bulwark.secondDelay / interval - 1e-4f) : 1) : -1;
        while (secondWave >= sizes.Count) sizes.Add(0);

        float firstClearance = patrol ? rules.playerClearance : rules.firstWaveClearance;
        var savedFeet = map.PlayerFeet;
        float savedClearance = map.PlayerClearance;
        var emitted = new List<(int wave, WaveSpawnInfo info)>();
        var occupied = new List<Rect>();
        var counts = new int[n];
        int bulwarkPlaced = 0, carry = 0, placedUnits = 0, wave = 0;
        var waveText = new List<string>();
        try
        {
            int plannedWaves = sizes.Count;
            for (wave = 0; wave < plannedWaves + ExtraWaves; wave++)
            {
                int budget = (wave < plannedWaves ? sizes[wave] : 0) + carry;
                bool bulwarkHere = bulwark != null && ((wave == 0 && bulwarkPlanned > 0) || (wave == secondWave));
                if (wave >= plannedWaves && budget <= 0) break;
                occupied.Clear();
                Array.Clear(failed, 0, n);
                map.SetPlayer(savedFeet, wave == 0 ? firstClearance : rules.playerClearance);
                int waveUnits = 0;
                if (bulwarkHere)
                {
                    var body = BodyFor(bulwark);
                    if (map.TryPick(body, rng, occupied, out var feet))
                    {
                        emitted.Add((wave, Make(bulwark.prefab, feet + bulwark.spawnOffset)));
                        occupied.Add(body.RectAt(feet));
                        bulwarkPlaced++;
                        placedUnits += bulwarkUnits;
                        waveUnits += bulwarkUnits;
                    }
                    else
                    {
                        // 자리가 없으면 그 단위를 다른 적으로 채운다.
                        budget += bulwarkUnits;
                        notes?.Add($"파도 {wave + 1}: Bulwark 자리(연속 바닥 {bulwark.minFloorRun}칸 + 머리 위 {bulwark.headroom:0.#}칸)를 찾지 못해 다른 적으로 채웁니다.");
                    }
                }
                while (budget > 0)
                {
                    int pick = PickSpecies(species, weights, groups, failed, budget, rng);
                    if (pick < 0) break;
                    var s = species[pick];
                    var body = BodyFor(s);
                    if (!map.TryPick(body, rng, occupied, out var feet)) { failed[pick] = true; continue; }
                    Emit(s, body, feet, wave, rng, emitted);
                    occupied.Add(body.RectAt(feet));
                    groups[pick]++;
                    counts[pick] += Mathf.Max(1, s.groupSize);
                    budget -= s.GroupUnits;
                    placedUnits += s.GroupUnits;
                    waveUnits += s.GroupUnits;
                }
                carry = Mathf.Max(0, budget);
                if (waveUnits > 0) waveText.Add($"{wave + 1}파 {waveUnits}단위");
            }
        }
        finally
        {
            map.SetPlayer(savedFeet, savedClearance);
        }

        // spawnDelay: 그 항목 '뒤'의 대기. 다음 항목이 다음 파도면 파도 차이 × 간격.
        for (int i = 0; i < emitted.Count; i++)
        {
            var (w, info) = emitted[i];
            int nextWave = i + 1 < emitted.Count ? emitted[i + 1].wave : w;
            info.spawnDelay = nextWave > w ? (nextWave - w) * interval : 0f;
            result.Add(info);
        }

        if (notes != null)
        {
            var kinds = new List<string>();
            for (int i = 0; i < n; i++) if (counts[i] > 0) kinds.Add($"{Name(species[i])} {counts[i]}" + (species[i].groupSize > 1 ? $"({groups[i]}묶음)" : ""));
            if (bulwarkPlaced > 0) kinds.Add($"Bulwark {bulwarkPlaced}");
            notes.Add($"배치 {placedUnits}/{target}단위 · {result.Count}마리 · " + (kinds.Count > 0 ? string.Join(" · ", kinds) : "없음")
                + " · 파도 " + (waveText.Count > 0 ? string.Join(", ", waveText) : "없음") + $"(간격 {interval:0.#}초, 동시 생존 상한 {cap})");
            if (carry > 0) notes.Add($"자리가 모자라 {carry}단위를 놓지 못했습니다(몸 크기·제외 구역·Player 거리·간격).");
        }
        return result;
    }

    /// <summary>실행 시드·층(Wave 번호)·방 id·방문 순번으로 구성 시드를 만든다(0은 피한다).</summary>
    public static int Seed(int runSeed, int floor, int room, int visit)
    {
        unchecked
        {
            uint h = (uint)runSeed * 0x9E3779B1u;
            h ^= (uint)(floor + 1) * 0x85EBCA77u; h = (h << 13) | (h >> 19);
            h ^= (uint)(room + 1) * 0xC2B2AE3Du; h = (h << 11) | (h >> 21);
            h ^= (uint)(visit + 1) * 0x27D4EB2Fu;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            int value = (int)h;
            return value != 0 ? value : 1;
        }
    }

    /// <summary>
    /// 종류의 생성 몸(묶음이면 묶음 전체). 지상 묶음(groupSize &gt; 1)은 묶음 몸 폭 안에 개체를 나란히 세우므로 폭 전체 아래가 받침이어야 한다(fullSupport).
    /// </summary>
    public static DungeonSpawnBody BodyFor(DungeonSpeciesRule species)
        => species == null ? default : new DungeonSpawnBody(species.bodySize, species.air, fullSupport: !species.air && species.groupSize > 1);

    /// <summary>Bulwark 생성 몸(연속 바닥·머리 위 조건 포함).</summary>
    public static DungeonSpawnBody BodyFor(DungeonBulwarkRule bulwark)
        => bulwark == null ? default : new DungeonSpawnBody(bulwark.bodySize, false, true, bulwark.minFloorRun, bulwark.headroom);

    /// <summary>
    /// 생성 직전 자리 다시 고르기(DungeonManager 선택 사용): 나중 파도가 나올 때 Player가 그 자리 가까이에 와 있으면 같은 Prefab의 몸 규칙으로
    /// 새 자리를 고른다(맵의 SetPlayer로 지금 위치·거리를 먼저 준다). 묶음 종류는 묶음 몸으로 골라 그 가운데에 둔다. 규칙에 없는 Prefab이면 false.
    /// </summary>
    public static bool TryRelocate(DungeonEncounterRulesSO rules, DungeonSpawnPointMap map, GameObject prefab, System.Random rng, IReadOnlyList<Rect> occupied, out Vector2 spawnPosition)
    {
        spawnPosition = default;
        if (rules == null || map == null || prefab == null || rng == null) return false;
        if (rules.bulwark != null && rules.bulwark.prefab == prefab)
        {
            if (!map.TryPick(BodyFor(rules.bulwark), rng, occupied, out var feet)) return false;
            spawnPosition = feet + rules.bulwark.spawnOffset;
            return true;
        }
        if (rules.species == null) return false;
        foreach (var s in rules.species)
        {
            if (s == null || s.prefab != prefab) continue;
            var body = BodyFor(s);
            if (!map.TryPick(body, rng, occupied, out var feet)) return false;
            spawnPosition = s.air ? feet + new Vector2(0f, body.size.y * .5f) + s.spawnOffset : feet + s.spawnOffset;
            return true;
        }
        return false;
    }

    /// <summary>파도 자리 다시 고르기에서 묶음 하나에 TryRelocate를 다시 해 보는 최대 횟수(고른 자리가 부르는 쪽 기준으로도 가까우면 다시 고른다).</summary>
    public const int RelocateTries = 4;

    /// <summary>
    /// 생성 직전 파도 자리 다시 고르기(4차 검토 — DungeonEncounter가 Player 가까움 대기 한도를 넘기면 DungeonManager를 거쳐 부른다).
    /// items[from..to](한 파도)를 구성 순서대로 묶음(같은 종류 groupSize개 연속)·단일로 나누고, tooClose인 개체가 있는 묶음만 같은 몸 규칙(TryRelocate)으로
    /// 새 자리를 고른 뒤 묶음 안 상대 위치를 그대로 옮긴다. 옮기지 않는 묶음의 몸 사각형은 같은 파도 간격 검사(occupied)에 넣는다.
    /// positions(길이 ≥ to − from + 1, 방 로컬)는 부르기 전에 지금 자리로 채워 두고, 옮긴 개체만 바뀐다. 맵의 Player 위치·거리는 부르는 쪽이 SetPlayer로 지금 값을 준다.
    /// 같은 입력·난수면 같은 결과다. 반환: 가까운 개체가 하나도 남지 않으면 true(일부만 옮겼거나 규칙에 없는 Prefab이면 false — 부르는 쪽이 조금 더 미룬 뒤 다시 부른다).
    /// </summary>
    public static bool RelocateWave(DungeonEncounterRulesSO rules, DungeonSpawnPointMap map, System.Random rng, IReadOnlyList<WaveSpawnInfo> items, int from, int to,
        Vector2[] positions, Func<Vector2, bool> tooClose)
    {
        if (rules == null || map == null || rng == null || items == null || positions == null || tooClose == null) return false;
        if (from < 0 || to < from || to >= items.Count || positions.Length < to - from + 1) return false;
        // 1. 묶음 나누기(Emit이 묶음 개체를 연달아 넣는다 — 같은 종류 묶음이 이어져도 groupSize개씩 끊는다).
        var clusters = new List<(int start, int count, bool close)>();
        for (int i = from; i <= to;)
        {
            var prefab = items[i] != null ? items[i].prefab : null;
            var species = SpeciesFor(rules, prefab);
            int count = 1;
            if (species != null && species.groupSize > 1)
                while (count < species.groupSize && i + count <= to && items[i + count] != null && items[i + count].prefab == prefab) count++;
            bool close = false;
            for (int k = i; k < i + count && !close; k++) close = prefab != null && tooClose(positions[k - from]);
            clusters.Add((i, count, close));
            i += count;
        }
        // 2. 옮기지 않는 묶음의 몸은 새 자리와 겹치지 않게 둔다.
        var occupied = new List<Rect>();
        foreach (var c in clusters)
            if (!c.close && TryBodyRect(rules, items[c.start]?.prefab, Anchor(positions, c.start - from, c.count), out var kept)) occupied.Add(kept);
        // 3. 가까운 묶음을 옮긴다.
        bool ok = true;
        foreach (var c in clusters)
        {
            if (!c.close) continue;
            var prefab = items[c.start].prefab;
            var anchor = Anchor(positions, c.start - from, c.count);
            bool moved = false;
            for (int attempt = 0; attempt < RelocateTries && !moved; attempt++)
            {
                if (!TryRelocate(rules, map, prefab, rng, occupied, out var spot)) break;
                var delta = spot - anchor;
                bool near = false;
                for (int k = 0; k < c.count && !near; k++) near = tooClose(positions[c.start - from + k] + delta);
                if (near) continue;
                for (int k = 0; k < c.count; k++) positions[c.start - from + k] += delta;
                if (TryBodyRect(rules, prefab, spot, out var rect)) occupied.Add(rect);
                moved = true;
            }
            if (!moved) ok = false;
        }
        return ok;
    }

    // 규칙에서 Prefab의 종류(Bulwark·규칙에 없는 Prefab이면 null).
    private static DungeonSpeciesRule SpeciesFor(DungeonEncounterRulesSO rules, GameObject prefab)
    {
        if (rules == null || rules.species == null || prefab == null) return null;
        foreach (var s in rules.species) if (s != null && s.prefab == prefab) return s;
        return null;
    }

    // 묶음 개체 자리의 평균(= 묶음 기준 자리: 공중 묶음은 정다각형 가운데, 지상 묶음은 나란한 줄 가운데).
    private static Vector2 Anchor(Vector2[] positions, int start, int count)
    {
        var sum = Vector2.zero;
        for (int k = 0; k < count; k++) sum += positions[start + k];
        return sum / Mathf.Max(1, count);
    }

    // 기준 자리(TryRelocate가 돌려주는 spawnPosition과 같은 뜻)에서 몸 사각형(맵 좌표)을 되짚는다. 규칙에 없는 Prefab이면 false.
    private static bool TryBodyRect(DungeonEncounterRulesSO rules, GameObject prefab, Vector2 anchor, out Rect rect)
    {
        rect = default;
        if (rules == null || prefab == null) return false;
        if (rules.bulwark != null && rules.bulwark.prefab == prefab)
        {
            rect = BodyFor(rules.bulwark).RectAt(anchor - rules.bulwark.spawnOffset);
            return true;
        }
        var s = SpeciesFor(rules, prefab);
        if (s == null) return false;
        var body = BodyFor(s);
        var feet = s.air ? anchor - s.spawnOffset - new Vector2(0f, body.size.y * .5f) : anchor - s.spawnOffset;
        rect = body.RectAt(feet);
        return true;
    }

    /// <summary>
    /// 입장 로딩 풀 예열용: 규칙으로 한 방에 동시에 있을 수 있는 Prefab별 최악의 수(동시 생존 상한·방 상한·묶음 상한으로 자름).
    /// DungeonRunBuilder가 DungeonRun.HintPrewarm(prefab, count)로 넘긴다(그쪽에서 PrewarmHintCap 12로 다시 자른다).
    /// </summary>
    public static List<(GameObject prefab, int count)> PrewarmCounts(DungeonEncounterRulesSO rules)
    {
        var result = new List<(GameObject prefab, int count)>();
        if (rules == null) return result;
        int cap = 0;
        for (int i = 0; i < DungeonEncounterRulesSO.LevelCount; i++) cap = Mathf.Max(cap, rules.ConcurrentCap((DungeonThreatLevel)i));
        int roomMax = Mathf.Max(1, rules.maxCount);
        if (rules.species != null)
            foreach (var s in rules.species)
            {
                if (s == null || s.prefab == null) continue;
                int byUnits = roomMax / Mathf.Max(1, s.units);
                int byGroups = s.maxGroupsPerRoom > 0 ? s.maxGroupsPerRoom * Mathf.Max(1, s.groupSize) : int.MaxValue;
                result.Add((s.prefab, Mathf.Max(1, Mathf.Min(cap, Mathf.Min(byUnits, byGroups)))));
            }
        if (rules.bulwark != null && rules.bulwark.prefab != null)
            result.Add((rules.bulwark.prefab, Mathf.Max(1, Mathf.Max(rules.bulwark.maxPerRoom, rules.bulwark.extremeMaxPerRoom))));
        return result;
    }

    // ── 내부 ──

    private static string Name(DungeonSpeciesRule s) => !string.IsNullOrEmpty(s.name) ? s.name : s.prefab != null ? s.prefab.name : "?";

    // 남은 예산 안에 들어가고, 이 파도에서 실패하지 않았고, 묶음 상한이 남은 종류 중 가중치로 하나(없으면 -1).
    private static int PickSpecies(DungeonSpeciesRule[] species, float[] weights, int[] groups, bool[] failed, int budget, System.Random rng)
    {
        double total = 0d;
        for (int i = 0; i < species.Length; i++) if (Eligible(species, weights, groups, failed, budget, i)) total += weights[i];
        if (total <= 0d) return -1;
        double r = rng.NextDouble() * total;
        int last = -1;
        for (int i = 0; i < species.Length; i++)
        {
            if (!Eligible(species, weights, groups, failed, budget, i)) continue;
            last = i;
            r -= weights[i];
            if (r < 0d) return i;
        }
        return last;
    }

    private static bool Eligible(DungeonSpeciesRule[] species, float[] weights, int[] groups, bool[] failed, int budget, int i)
    {
        var s = species[i];
        if (s == null || s.prefab == null || weights[i] <= 0f || failed[i]) return false;
        if (s.GroupUnits > budget) return false;
        return s.maxGroupsPerRoom <= 0 || groups[i] < s.maxGroupsPerRoom;
    }

    // 종류 하나(묶음이면 묶음 전체)를 항목으로 만든다. 지상 = 발 + spawnOffset, 공중 = 몸 상자 가운데 + spawnOffset(묶음은 그 둘레 반경 안에 고르게).
    // 지상 묶음(4차 검토): 묶음 몸 폭을 묶음 수로 나눈 칸마다 한 마리씩, 발 높이를 같게 나란히 둔다(가운데 = 묶음 발 — 예산·counts와 실제 수가 같게).
    private static void Emit(DungeonSpeciesRule s, DungeonSpawnBody body, Vector2 feet, int wave, System.Random rng, List<(int, WaveSpawnInfo)> emitted)
    {
        int count = Mathf.Max(1, s.groupSize);
        if (!s.air)
        {
            if (count == 1) { emitted.Add((wave, Make(s.prefab, feet + s.spawnOffset))); return; }
            float slot = body.size.x / count;
            for (int k = 0; k < count; k++)
                emitted.Add((wave, Make(s.prefab, new Vector2(feet.x - body.size.x * .5f + (k + .5f) * slot, feet.y) + s.spawnOffset)));
            return;
        }
        var center = feet + new Vector2(0f, body.size.y * .5f);
        if (count == 1) { emitted.Add((wave, Make(s.prefab, center + s.spawnOffset))); return; }
        // 묶음 몸 상자 안에 들도록 반경을 줄인다(개체 몸 반경 약 .45칸).
        float radius = Mathf.Max(0f, Mathf.Min(s.groupRadius, Mathf.Min(body.size.x, body.size.y) * .5f - .45f));
        // 묶음 가운데 둘레에 같은 반경·같은 간격으로 둔다(정다각형 — 무리 대형처럼 보이고, 세 점의 가운데가 묶음 가운데와 같다).
        float start = (float)(rng.NextDouble() * 360d);
        float r = radius * (.6f + .4f * (float)rng.NextDouble());
        for (int k = 0; k < count; k++)
        {
            float angle = (start + k * 360f / count) * Mathf.Deg2Rad;
            emitted.Add((wave, Make(s.prefab, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r + s.spawnOffset)));
        }
    }

    private static WaveSpawnInfo Make(GameObject prefab, Vector2 position) => new WaveSpawnInfo
    {
        prefab = prefab,
        isRequiredToClear = true,
        keepAfterWave = false,
        isBossEnemy = false,
        spawnDelay = 0f,
        spawnPosition = position,
        rotationSteps = 0,
        lifetime = 0f,
        deviceSettings = WaveDeviceSettings.None,
    };

    private static string BulwarkReason(DungeonBulwarkRule bulwark, bool patrol, DungeonThreatLevel level, int height, int empty, int max, int planned, int target)
    {
        if (bulwark == null) return "규칙 없음";
        if (patrol) return "순찰 구성에는 넣지 않음";
        if (bulwark.prefab == null) return "Prefab 미연결";
        if (level < bulwark.minLevel) return $"단계 {DungeonThreatText.Label(level)} < {DungeonThreatText.Label(bulwark.minLevel)}";
        if (height < bulwark.minInteriorHeight) return $"실내 높이 {height} < {bulwark.minInteriorHeight}";
        if (planned < max) return $"최대 {max}마리 중 {planned}마리(목표 {target}단위에서 다른 적 {MinOthersWithBulwark}단위 이상을 남김)";
        return $"{planned}마리" + (max > Mathf.Max(0, bulwark.maxPerRoom) ? $"(특급·빈 칸 {empty} ≥ {bulwark.extremeLargeArea} — 둘째는 {bulwark.secondDelay:0.#}초 뒤 파도)" : "");
    }
}
