using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 7차(DESIGN_V7 §1.2 V1) 낙석 구조물 규칙 — 규칙 이름 'v7 낙석'. 카탈로그 위험 모양 Danger.DropBlock(값 5)인 함정에 적용한다.
// 낙석은 모터를 고치지 않는 R1이라 블록이 OneWay를 지나 아래 고체에 떨어진다. 그래서 OneWay 위에는 두지 않고, 카메라(Player 몸 중심 위아래 7칸)에서
// 감지 순간 블록 전체가 보이도록 낙하 높이를 짧게(평평한 바닥이면 3칸) 둔다.
//  1. (오류) 착지면 없음 — 낙하 열이 고체 받침을 만나기 전에 실내를 벗어난다.
//  2. (오류) 낙하 열에 OneWay — 블록 칸·낙하 구간(x0..x0+3 × [착지면, 블록 아랫줄])에 지형 OneWay 셀·OneWay Prop(정비 발판·팔레트·파괴 격자 등 — 카탈로그 Collision.OneWay) 칸.
//  3. (오류) 사슬 칸에 OneWay·다른 Prop 차지 칸(천장 지형·고체 Prop·천장까지 거리 ceilingReach는 설치면 검사 MountProblem이 본다 — 유령 미리보기 색도 같은 함수).
//  4. (경고) 낙하 높이 < RockfallMinDrop(3)칸 — 진동 예고에 비해 떨어지는 거리가 짧다.
//  5. (경고) 시야 — 감지 사각형 열마다 블록 아랫변 아래 첫 서는 면 s(지형·고체 Prop·OneWay 윗면)를 구하고, 그 위에 선 몸(1.51칸)이 감지 사각형에 걸치는 열 중
//     가장 낮은 s_low에서 블록 윗변이 몸 중심 + RockfallSightReach(6.25 = 반시야 7 − 여유 0.75)를 넘으면 경고. 평평한 착지면이면 낙하 3칸이 4·5를 함께 만족한다.
// 위험 칸(DropBlockCells)은 TrapDanger(빌더·생성 구역·T10·크레인 경로 규칙 공용)가 같은 계산을 쓴다. 심각도는 Sink(오류 = Firm — 엄격이면 오류).
public static partial class DungeonTemplateValidation
{
    /// <summary>낙석 낙하 높이 권장 하한(칸, 블록 아랫변 ~ 착지면).</summary>
    public const int RockfallMinDrop = 3;
    /// <summary>시야: 감지 위치의 몸 중심에서 화면 위 끝까지(카메라 세로 반시야 7 − 여유 0.75, critic §2.1).</summary>
    public const float RockfallSightReach = 6.25f;
    /// <summary>감지 여유(칸) 기본값 — PropRockfall rockTriggerRange 기본값과 같다(배치 설정 값이 있으면 그 값).</summary>
    public const float RockfallTriggerDefault = 1.25f;
    public const string RockfallTriggerKey = "rockTriggerRange";
    const int DropScanLimit = 64;

    /// <summary>
    /// 강체 블록(낙석) 위험 칸: 차지 칸 열마다 아랫줄 바로 밑부터 아래로 실내·비고체 칸을 내려가 첫 고체(blocked — 지형·고체 Prop, OneWay는 받침이 아님) 윗면을 찾고,
    /// 착지면 landing = 열들의 최댓값(가장 높은 받침에서 모든 열이 멈춤)이다. 위험 칸 = 차지 칸 열 × [landing, 아랫줄 − 1].
    /// 어느 열도 받침을 못 찾으면(실내를 벗어남) supported = false, landing = 가장 깊이 내려간 줄이고 열마다 실내 끝까지 위험 칸이다.
    /// </summary>
    static HashSet<Vector2Int> DropBlockCells(RectInt area, Func<Vector2Int, bool> blocked, Func<Vector2Int, bool> interior, out int landing, out bool supported)
    {
        var cells = new HashSet<Vector2Int>();
        supported = false;
        landing = int.MinValue;
        int deepest = area.yMin;
        var bottoms = new int[Mathf.Max(0, area.width)];
        for (int i = 0; i < area.width; i++)
        {
            int x = area.xMin + i;
            int y = area.yMin - 1;
            int guard = 0;
            while (guard++ < DropScanLimit && interior(new Vector2Int(x, y)) && !blocked(new Vector2Int(x, y))) y--;
            bool hit = blocked(new Vector2Int(x, y));
            bottoms[i] = y + 1;   // 이 열이 닿는 가장 낮은 빈 칸(받침이면 받침 윗면)
            deepest = Mathf.Min(deepest, y + 1);
            if (!hit) continue;
            supported = true;
            landing = Mathf.Max(landing, y + 1);
        }
        if (!supported) landing = deepest;
        for (int i = 0; i < area.width; i++)
        {
            int x = area.xMin + i;
            int floor = supported ? landing : bottoms[i];
            for (int y = floor; y < area.yMin; y++) cells.Add(new Vector2Int(x, y));
        }
        return cells;
    }

    static bool IsDropBlock(Placed item) => item?.Entry != null && item.Entry.danger == DungeonPropCatalogSO.Danger.DropBlock;

    static void RockfallRules(Model model, List<Placed> traps, Sink sink)
    {
        var rocks = traps.Where(IsDropBlock).ToList();
        if (rocks.Count == 0 || !model.HasRegion) return;
        sink.Rule = "v7 낙석";
        // OneWay 칸: 지형 OneWay 셀 + Prop OneWay 윗면(카탈로그 밖 Prop 포함) + 카탈로그 OneWay Prop(파괴 격자·팔레트·정비 발판)의 차지 칸 전체.
        var oneWay = new HashSet<Vector2Int>(model.OneWay);
        oneWay.UnionWith(model.PropOneWay);
        foreach (var item in model.Items)
            if (item.Entry != null && item.Entry.collision == DungeonPropCatalogSO.Collision.OneWay && item.Cells != null) oneWay.UnionWith(item.Cells);
        foreach (var t in rocks)
        {
            var area = t.Area;
            DropBlockCells(area, model.Blocked, model.Interior.Contains, out int landing, out bool supported);

            // 1. 착지면 없음.
            if (!supported)
            {
                sink.Firm($"{t.Label}: 낙하 열(x {area.xMin}~{area.xMax - 1}) 아래에 착지면(지형·고체 Prop)이 없습니다 — 블록이 실내 밖으로 빠집니다. 고체 바닥 위에 두세요(7차 V1).",
                    0, t.Index, area.position, CellsOf(area));
                continue;
            }

            // 2. 낙하 열(블록 칸 + 낙하 구간)에 OneWay.
            var through = new List<Vector2Int>();
            for (int x = area.xMin; x < area.xMax; x++)
                for (int y = landing; y < area.yMax; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (oneWay.Contains(c)) through.Add(c);
                }
            if (through.Count > 0)
                sink.Firm($"{t.Label}: 낙하 열(x {area.xMin}~{area.xMax - 1}, y {landing}~{area.yMax - 1})에 OneWay 칸 ({through[0].x}, {through[0].y})이 있습니다 — 낙석은 OneWay를 지나 아래 고체에 떨어집니다(R1). "
                    + "OneWay·정비 발판·팔레트·파괴 격자 위에는 두지 않습니다(7차 V1).", 0, t.Index, through[0], through);

            // 3. 사슬 칸(블록 윗줄 위 ~ 천장 지형 아래)의 OneWay·다른 Prop 차지 칸. 천장을 못 찾는 열은 설치면 검사가 알린다.
            int reach = t.Entry.ceilingReach > 0 ? t.Entry.ceilingReach : 0;
            Vector2Int? chainHit = null;
            string chainWhat = null;
            for (int x = area.xMin; x < area.xMax && chainHit == null; x++)
                for (int k = 0; k <= reach; k++)
                {
                    var c = new Vector2Int(x, area.yMax + k);
                    if (model.Solids.Contains(c)) break;
                    if (oneWay.Contains(c)) { chainHit = c; chainWhat = "OneWay 칸"; break; }
                    if (model.Owner.TryGetValue(c, out var other) && other != t) { chainHit = c; chainWhat = other.Label; break; }
                }
            if (chainHit is Vector2Int blockedChain)
                sink.Firm($"{t.Label}: 사슬 칸 ({blockedChain.x}, {blockedChain.y})에 {chainWhat}이(가) 있습니다 — 천장 호이스트까지 사슬이 지나는 칸(블록 윗줄 위 ~ 천장)은 비워 둡니다(7차 V1).",
                    0, t.Index, blockedChain, new[] { blockedChain });

            // 4. 낙하 높이.
            int drop = area.yMin - landing;
            if (drop < RockfallMinDrop)
            {
                var gap = new List<Vector2Int>();
                for (int x = area.xMin; x < area.xMax; x++) for (int y = landing; y < area.yMin; y++) gap.Add(new Vector2Int(x, y));
                sink.Warn($"{t.Label}: 낙하 높이가 {drop}칸입니다({RockfallMinDrop}칸 이상 권장 — 진동 예고에 비해 떨어지는 거리가 짧습니다, 7차 V1).", 0, t.Index, area.position, gap.Count > 0 ? gap : CellsOf(area));
            }

            // 5. 시야: 감지 사각형(블록 폭 ± 감지 여유 × 착지면 ~ 블록 아랫변)에 걸치는 선 몸 중 가장 낮은 서는 면에서 블록 윗변이 화면 위 끝을 넘는지.
            float range = Mathf.Max(0f, SettingOr(t.Spawn, RockfallTriggerKey, RockfallTriggerDefault));
            int left = Mathf.FloorToInt(area.xMin - range + 1e-4f), right = Mathf.CeilToInt(area.xMax + range - 1e-4f) - 1;
            int lowest = int.MaxValue;
            for (int x = left; x <= right; x++)
            {
                int y = area.yMin - 1;
                int guard = 0;
                while (guard++ < DropScanLimit && model.Interior.Contains(new Vector2Int(x, y)) && !model.Support(new Vector2Int(x, y))) y--;
                if (!model.Support(new Vector2Int(x, y))) continue;   // 실내를 벗어남(서는 면 없음)
                int stand = y + 1;
                if (stand >= area.yMin || stand + PlayerHeight <= landing) continue;   // 몸이 감지 사각형에 걸치지 않음
                lowest = Mathf.Min(lowest, stand);
            }
            if (lowest != int.MaxValue)
            {
                float limit = lowest + PlayerHeight * .5f + RockfallSightReach;
                if (area.yMax > limit + 1e-3f)
                    sink.Warn($"{t.Label}: 감지 위치(서는 면 y {lowest})에서 블록 윗변(y {area.yMax})이 화면 위 끝(몸 중심 + {RockfallSightReach:0.##}칸 = y {limit:0.##})을 넘습니다 — "
                        + $"블록이 화면 밖에서 떨어집니다. 낙하 높이를 줄이세요(평평한 바닥이면 {RockfallMinDrop}칸, 7차 V1 시야).", 0, t.Index, area.position, CellsOf(area));
            }
        }
    }
}
