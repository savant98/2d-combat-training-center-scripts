using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 7차 난이도 공통 모양(DESIGN_V7 §3.3) — '세로 5칸 사다리 + ▲'. 현장 단말기 난이도 게이지(V6)와 난이도 상승 계기(V8)가 이 함수 하나로 그린다
/// (contracts7 소유 — 영역은 고치지 않고 Look 값만 골라 부른다. 고칠 일이 있으면 통합 담당에게 패치로 넘긴다).
/// - 칸 0(아래) = 입문 … 칸 4(위) = 특급. 칸 색 = <see cref="DungeonThreatText.Color"/>(색표를 여기서 다시 정하지 않는다).
/// - 켜진 칸(lit 비트, bit i = 칸 i): 지금 단계 칸은 밝게(1.0), 그 밖의 켜진 칸은 흐리게(look.pastAlpha). 켜지지 않은 칸은 어두운 칸 + 흐린 자기 색 테두리(look.slotAlpha).
/// - 점화 칸(ignite 비트): 흰빛 쪽으로 섞는다(점화 순간 — 부르는 쪽은 정수 상태가 바뀔 때만 다시 그린다. 밝기 맥동은 CanvasRenderer 알파·덧그린 빛 Image로).
/// - ▲(지금 단계 표시)는 사다리 옆 자리(look.pointerHalf > 0일 때 늘 비워 둔다)에 지금 칸 높이로 그린다. 오르는 연출의 움직이는 ▲는
///   <see cref="WritePointer"/>로 따로 그린 Graphic(DungeonThreatLadderGraphic Part.Pointer)의 RectTransform을 <see cref="PointerCenter"/>로 옮긴다(Mesh 재생성 없음).
/// 시각 언어: 얇은 발광선 Sci-fi — 왼쪽 위·오른쪽 아래를 깎은 칸(DungeonMapMesh.WritePlate와 같은 깎임 방향)·얇은 테두리. 굵은 부품·톱니·액체 효과 없음, 크기 변형 없음.
/// 정점 색은 화면색(sRGB) 그대로다(던전 UI Canvas는 정점 색 감마 공간 — DungeonMapMesh와 같은 규칙). 할당 없음(정적 버퍼 — UI 갱신은 주 스레드뿐).
/// </summary>
public static class DungeonThreatGlyph
{
    /// <summary>난이도 단계 수(입문·하급·중급·상급·특급).</summary>
    public const int StepCount = 5;
    /// <summary>모든 칸 비트.</summary>
    public const int AllCells = (1 << StepCount) - 1;

    /// <summary>사다리 모양 값(px, 1080p 기준). 영역마다 자기 값을 만들어 넘긴다(단말기는 낮은 칸, 계기는 큰 칸).</summary>
    [System.Serializable]
    public struct Look
    {
        [Tooltip("칸 사이 간격(px).")] public float gap;
        [Tooltip("칸 모서리 깎임(px, 왼쪽 위·오른쪽 아래). 0이면 직사각형.")] public float cut;
        [Tooltip("칸 테두리 두께(px).")] public float line;
        [Tooltip("지금 단계가 아닌 켜진 칸의 밝기(0~1).")] public float pastAlpha;
        [Tooltip("켜지지 않은 칸 테두리 밝기(0~1, 자기 단계 색).")] public float slotAlpha;
        [Tooltip("켜지지 않은 칸 채움(어두운 중립 바탕 — 테마색 채움 금지: 선형 블렌딩에서 면 전체가 물든다).")] public Color32 slotFill;
        [Tooltip("점화 칸이 흰빛 쪽으로 섞이는 비율(0~1).")] public float igniteWhite;
        [Tooltip("▲ 반폭(px). 0 이하면 ▲ 자리를 두지 않는다.")] public float pointerHalf;
        [Tooltip("▲와 칸 사이 여백(px).")] public float pointerGap;
        [Tooltip("▲ 자리를 사다리 오른쪽에 둔다(기본 왼쪽).")] public bool pointerRight;

        /// <summary>기본값(난이도 상승 계기 크기 — 칸 높이 약 28px 기준).</summary>
        public static Look Default => new Look
        {
            gap = 4f,
            cut = 4f,
            line = 1.5f,
            pastAlpha = .45f,
            slotAlpha = .4f,
            slotFill = new Color32(8, 12, 18, 205),
            igniteWhite = .7f,
            pointerHalf = 6f,
            pointerGap = 4f,
            pointerRight = false,
        };
    }

    // 칸 하나의 깎인 육각형(시계 방향) — 정적 버퍼(할당 없음).
    private static readonly Vector2[] poly = new Vector2[6];

    private static int Index(DungeonThreatLevel level) => Mathf.Clamp((int)level, 0, StepCount - 1);

    /// <summary>입문부터 level까지 켠 비트(기본 상태).</summary>
    public static int MaskUpTo(DungeonThreatLevel level) => (1 << (Index(level) + 1)) - 1;

    /// <summary>▲ 자리 폭(px). look.pointerHalf ≤ 0이면 0.</summary>
    public static float PointerLane(in Look look) => look.pointerHalf > 0f ? look.pointerHalf * 2f + 3f + Mathf.Max(0f, look.pointerGap) : 0f;

    /// <summary>rect(▲ 자리 포함 전체)에서 칸이 차지하는 열.</summary>
    public static Rect CellColumn(Rect rect, in Look look)
    {
        float lane = Mathf.Min(PointerLane(look), rect.width * .5f);
        return look.pointerRight
            ? new Rect(rect.xMin, rect.yMin, rect.width - lane, rect.height)
            : new Rect(rect.xMin + lane, rect.yMin, rect.width - lane, rect.height);
    }

    /// <summary>칸 하나의 높이(px).</summary>
    public static float CellHeight(Rect rect, in Look look) => Mathf.Max(0f, (rect.height - Mathf.Max(0f, look.gap) * (StepCount - 1)) / StepCount);

    /// <summary>칸 step(0 = 입문, 아래)의 사각형. 생성 도구가 단계 이름표 자리를 맞추는 데도 쓴다.</summary>
    public static Rect CellRect(Rect rect, int step, in Look look)
    {
        var column = CellColumn(rect, look);
        float h = CellHeight(rect, look);
        int i = Mathf.Clamp(step, 0, StepCount - 1);
        return new Rect(column.xMin, column.yMin + i * (h + Mathf.Max(0f, look.gap)), column.width, h);
    }

    /// <summary>
    /// ▲ 가운데(rect 좌표계). position = 칸 위치(0 = 입문 칸 높이, 4 = 특급 칸 높이, 소수 허용 — 오르는 연출은 이 값만 보간한다).
    /// </summary>
    public static Vector2 PointerCenter(Rect rect, float position, in Look look)
    {
        float lane = Mathf.Min(PointerLane(look), rect.width * .5f);
        float outer = Mathf.Max(0f, lane - Mathf.Max(0f, look.pointerGap));
        float x = look.pointerRight ? rect.xMax - outer * .5f : rect.xMin + outer * .5f;
        float h = CellHeight(rect, look);
        float p = Mathf.Clamp(position, 0f, StepCount - 1);
        return new Vector2(x, rect.yMin + h * .5f + p * (h + Mathf.Max(0f, look.gap)));
    }

    /// <summary>▲ 색(지금 단계 색을 흰빛 쪽으로 35% — 어두운 바탕 위에서 읽히게).</summary>
    public static Color32 PointerColor(DungeonThreatLevel level)
    {
        Color c = Color.Lerp(DungeonThreatText.Color(level), Color.white, .35f);
        c.a = 1f;
        return c;
    }

    /// <summary>
    /// 사다리를 그린다. current = 지금 단계(밝은 칸·▲ 자리), litMask = 켜진 칸 비트, igniteMask = 흰빛으로 섞을 칸 비트, pointer = ▲를 그릴지.
    /// 더한 정점 수를 돌려준다(rect가 너무 작으면 0).
    /// </summary>
    public static int WriteLadder(VertexHelper vh, Rect rect, DungeonThreatLevel current, int litMask, int igniteMask, bool pointer, in Look look)
    {
        if (vh == null || rect.width <= 2f || rect.height <= 2f) return 0;
        float h = CellHeight(rect, look);
        if (h < 1f) return 0;
        int start = vh.currentVertCount;
        int cur = Index(current);
        float width = Mathf.Max(.5f, look.line);
        for (int i = 0; i < StepCount; i++)
        {
            // 선은 변 한가운데에 그리므로(반폭이 바깥으로 나감, 깎은 모서리는 √2배) 선 두께 0.75배만큼 안쪽으로 들여 rect 밖으로 나가지 않게 한다.
            var cell = CellRect(rect, i, look);
            float inset = Mathf.Min(width * .75f, cell.width * .25f, cell.height * .25f);
            cell = new Rect(cell.xMin + inset, cell.yMin + inset, cell.width - inset * 2f, cell.height - inset * 2f);
            Color c = DungeonThreatText.Color((DungeonThreatLevel)i);
            bool lit = (litMask & (1 << i)) != 0;
            bool ignite = (igniteMask & (1 << i)) != 0;
            Chamfer(cell, Mathf.Min(Mathf.Max(0f, look.cut), cell.height * .45f, cell.width * .45f));
            Color fill, line;
            if (ignite)
            {
                fill = Color.Lerp(c, Color.white, Mathf.Clamp01(look.igniteWhite));
                fill.a = 1f;
                line = Color.white;
            }
            else if (lit)
            {
                float a = i == cur ? 1f : Mathf.Clamp01(look.pastAlpha);
                fill = c;
                fill.a = .85f * a;
                line = Color.Lerp(c, Color.white, .45f);
                line.a = a;
            }
            else
            {
                fill = look.slotFill;
                line = c;
                line.a = Mathf.Clamp01(look.slotAlpha);
            }
            Fill(vh, fill);
            for (int k = 0; k < poly.Length; k++) Stroke(vh, poly[k], poly[(k + 1) % poly.Length], width, line);
        }
        if (pointer && look.pointerHalf > 0f)
            WritePointer(vh, PointerCenter(rect, cur, look), look.pointerHalf, PointerColor(current), look.slotFill);
        return vh.currentVertCount - start;
    }

    /// <summary>위를 향한 ▲ 하나(어두운 바탕 한 겹 + 밝은 면 + 안쪽 진한 점 — DungeonStatusPlateGraphic.WriteRise와 같은 모양). 더한 정점 수를 돌려준다.</summary>
    public static int WritePointer(VertexHelper vh, Vector2 center, float half, Color32 fill, Color32 backing)
    {
        if (vh == null || half < 1f) return 0;
        int start = vh.currentVertCount;
        Triangle(vh, center, half + 1.5f, backing);
        Triangle(vh, center, half, fill);
        Color inner = Color.Lerp(fill, Color.black, .25f);
        inner.a = 1f;
        Triangle(vh, center + new Vector2(0f, -half * .15f), half * .5f, inner);
        return vh.currentVertCount - start;
    }

    // 왼쪽 위·오른쪽 아래를 깎은 육각형(시계 방향) — DungeonMapMesh.Chamfered와 같은 순서.
    private static void Chamfer(Rect r, float cut)
    {
        poly[0] = new Vector2(r.xMin, r.yMax - cut);
        poly[1] = new Vector2(r.xMin + cut, r.yMax);
        poly[2] = new Vector2(r.xMax, r.yMax);
        poly[3] = new Vector2(r.xMax, r.yMin + cut);
        poly[4] = new Vector2(r.xMax - cut, r.yMin);
        poly[5] = new Vector2(r.xMin, r.yMin);
    }

    // 볼록 육각형 채움(점 0에서 부채꼴).
    private static void Fill(VertexHelper vh, Color32 tint)
    {
        if (tint.a == 0) return;
        int i = vh.currentVertCount;
        for (int k = 0; k < poly.Length; k++) vh.AddVert(poly[k], tint, Vector2.zero);
        for (int k = 1; k < poly.Length - 1; k++) vh.AddTriangle(i, i + k, i + k + 1);
    }

    // 끝을 반폭만큼 늘인 선분(이웃 변과 모서리에서 이어진다 — DungeonMapMesh.StrokeSegment와 같다).
    private static void Stroke(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 tint)
    {
        if (tint.a == 0) return;
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 1e-4f) return;
        d /= len;
        float h = width * .5f;
        a -= d * h;
        b += d * h;
        Vector2 n = new Vector2(-d.y, d.x) * h;
        int i = vh.currentVertCount;
        vh.AddVert(a - n, tint, Vector2.zero); vh.AddVert(b - n, tint, Vector2.zero);
        vh.AddVert(b + n, tint, Vector2.zero); vh.AddVert(a + n, tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }

    private static void Triangle(VertexHelper vh, Vector2 c, float h, Color32 tint)
    {
        int i = vh.currentVertCount;
        vh.AddVert(new Vector2(c.x - h, c.y - h * .8f), tint, Vector2.zero);
        vh.AddVert(new Vector2(c.x, c.y + h), tint, Vector2.zero);
        vh.AddVert(new Vector2(c.x + h, c.y - h * .8f), tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
    }
}
