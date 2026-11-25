using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 던전 방 카메라 범위 안에서 '보이지 않아야 할 고체'를 바닥 그림자와 같은 색으로 가린다(던전 Wave만).
// 던전 방 카메라 범위는 연결 공간 + 사방 3칸(셀 외접으로 자름, WaveCellTerrain.DungeonCameraRectFor)이라 천장·좌·우 벽 3칸이 화면에 들어온다.
// 바닥 3줄은 지금처럼 RoomViewRules.ApplyBlinder(바닥 그림자)가 덮고, 여기서는 나머지를 하나의 Mesh로 덮는다.
//  (a) 테두리: 테두리(frame = CameraFrameCells, 보통 셀 외접) 안쪽 위 3줄·왼쪽 3열·오른쪽 3열(출입구 판 칸 포함 — 관 그림은 정렬상 위에 그려진다).
//  (b) 덩어리: 실내 연결 공간 칸에서 위·아래·왼쪽·오른쪽으로 이어진 고체가 MinimumDepth 이상이고 범위 끝까지 닿으면 그 고체(L 방 채움 블록, ㄱ 방 위층 바닥 밑 등).
//      그 방향과 수직인 이웃이 연결 공간인 칸(옆 벽면·발판 윗면)은 보이는 지형으로 남긴다. 실내에 떠 있는 덩어리(回 방 가운데 등)는 끝까지 닿지 않아 그대로 보인다.
//      그 방향과 수직으로 잰 고체 두께도 MinimumDepth 이상이어야 한다. 벽에 붙은 얇은 판(3칸 덕트 바닥)이나 바닥 위 1~2칸 턱은 진행 방향으로는 길어도
//      덩어리가 아니다(옛 규칙은 '수직 이웃이 연결 공간'만 봐서 3칸 판의 가운데 줄·턱의 아랫줄만 띠처럼 가렸다).
//  (c) 빈 공간: 범위 안이면서 테두리 밖(21:9처럼 넓은 화면에서 범위를 늘린 자리)은 불투명.
// 진하기는 '보이는 칸까지의 Chebyshev 거리' 하나로 정한다(1~3줄 그라데이션, 4 이상 불투명). 보이는 칸 = 실내(테두리 안쪽 상자)의 연결 공간 칸과
// 가리지 않는 실내 고체(덕트·턱·발판 윗면·옆 벽면)다. 그래서 음영은 가리지 않은 지형과 맞닿는 곳에서 늘 0(투명)부터 시작해 경계가 딱 끊기지 않는다.
// 칸 꼭짓점마다 거리를 넣어 칸 안에서 부드럽게 보간하므로
// 모서리·면·오목한 덩어리가 서로 덮지 않는다. 바닥 그림자 줄(범위 아래 3줄)에서는 가로 성분만 넣는다(세로 그라데이션은 바닥 그림자가 맡아 두 겹이 되지 않는다).
// 물리 질의 없이 셀 데이터(연결 공간의 빈 칸·Platform 칸)로만 계산하고, 결과가 바뀐 때만 Mesh 하나를 다시 만든다(매 프레임 할당 없음).
// WaveCellTerrain이 연결 공간을 다시 구할 때(방이 바뀔 때·화면 비율이 바뀔 때) Rebuild를 부른다(던전 Wave에서만).
// 테두리를 주지 않으면 3차 규칙(위층 바닥 밑만)으로 계산한다.
[DisallowMultipleComponent]
public sealed class WaveRoomFloorShade : MonoBehaviour
{
    /// <summary>그라데이션 줄 수(바닥 그림자 줄 수와 같다). 거리 1~3은 그라데이션, 4 이상은 불투명.</summary>
    public const int GradientRows = 3;
    /// <summary>
    /// 연결 공간 칸에서 한 방향으로 이어진 고체가 이 줄 수 이상이고 카메라 범위 끝까지 닿을 때만 덩어리로 가린다(오목한 방의 채움 고체는 바깥 벽까지 이어진다).
    /// 맨 아래 바닥·천장·옆 벽(벽 두께 3)과 1~2줄 단차(4~5)는 덩어리로 보지 않고, 실내에 떠 있는 덩어리(전선 트렁크 기둥·回 방 가운데 등)는 끝이 비어 있어 건드리지 않는다.
    /// 덩어리 칸은 진행 방향과 수직으로 잰 고체 두께도 이 값 이상이어야 한다(벽에 붙은 얇은 판·바닥 위 턱 제외 — 아래 방향 규칙과 같은 문턱).
    /// 판단값: 벽 두께 3 + 3.
    /// </summary>
    public const int MinimumDepth = 6;
    /// <summary>테두리 두께(칸): 테두리 안쪽 위·왼쪽·오른쪽 이만큼이 벽 띠다. WaveCellTerrain.DungeonCameraMargin·벽 두께와 같다.</summary>
    public const int EdgeRows = 3;
    /// <summary>정렬 순서: 일반 지형(0) 위, 위험·장식 Prop(Default 3 이상·Player 층)·출입구 관(Default 3~8) 아래. 바닥 그림자(AutomaticFloorBlinder)와 같다.</summary>
    public const int SortingOrder = 1;

    public enum Shade : byte
    {
        None = 0,
        /// <summary>실내 연결 공간에서 1칸(가장 옅음).</summary>
        Fade1 = 1,
        Fade2 = 2,
        Fade3 = 3,
        /// <summary>4칸 이상(불투명).</summary>
        Solid = 4,
    }

    /// <summary>한 줄(y)에서 같은 음영이 이어진 칸 구간 [xMin, xMax).</summary>
    public struct Span : IEquatable<Span>
    {
        public int y, xMin, xMax;
        public Shade shade;
        public bool Equals(Span other) => y == other.y && xMin == other.xMin && xMax == other.xMax && shade == other.shade;
        public override bool Equals(object obj) => obj is Span other && Equals(other);
        public override int GetHashCode() => ((y * 397 ^ xMin) * 397 ^ xMax) * 7 + (int)shade;
        public override string ToString() => $"y{y} x[{xMin},{xMax}) {shade}";
    }

    /// <summary>그리는 사각형 하나(방 좌표 칸 경계)와 네 꼭짓점의 거리(0 = 투명 ~ GradientRows = 불투명).</summary>
    public struct Quad : IEquatable<Quad>
    {
        public int xMin, yMin, xMax, yMax;
        public byte bottomLeft, topLeft, topRight, bottomRight;
        public bool Equals(Quad o) => xMin == o.xMin && yMin == o.yMin && xMax == o.xMax && yMax == o.yMax
            && bottomLeft == o.bottomLeft && topLeft == o.topLeft && topRight == o.topRight && bottomRight == o.bottomRight;
        public override bool Equals(object obj) => obj is Quad other && Equals(other);
        public override int GetHashCode() => (((xMin * 397 ^ yMin) * 397 ^ xMax) * 397 ^ yMax) * 31 + (bottomLeft | topLeft << 2 | topRight << 4 | bottomRight << 6);
        public override string ToString() => $"[{xMin},{xMax})x[{yMin},{yMax}) {bottomLeft}{topLeft}{topRight}{bottomRight}";
    }

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly Vector2Int[] Directions = { Vector2Int.down, Vector2Int.up, Vector2Int.left, Vector2Int.right };
    // 계산용 버퍼(주 스레드에서만 쓴다). 넓은 범위가 처음 나올 때만 늘린다.
    private static Shade[] grid = Array.Empty<Shade>();
    private static bool[] columnHasRegion = Array.Empty<bool>();
    private static bool[] target = Array.Empty<bool>();
    private static bool[] used = Array.Empty<bool>();
    private static byte[] distance = Array.Empty<byte>();
    private static byte[] vertex = Array.Empty<byte>();
    private static int[] queue = Array.Empty<int>();
    // 마지막 Compute가 테두리 규칙을 썼는지와 그 범위(CollectQuads가 바로 이어서 읽는다).
    private static bool computedEdge;
    private static RectInt computedArea;

    private readonly List<Span> spans = new();
    private readonly List<Span> next = new();
    private readonly List<Quad> quads = new();
    private readonly List<Quad> nextQuads = new();
    private readonly List<Vector3> vertices = new();
    private readonly List<Vector2> uvs = new();
    private readonly List<Color32> colors = new();
    private readonly List<int> triangles = new();
    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock properties;
    private Sprite uvSprite;
    private float uOpaque, uClear, vMiddle;

    /// <summary>지금 그린 음영 구간(방 좌표 칸, 칸마다 Chebyshev 거리 단계).</summary>
    public IReadOnlyList<Span> Spans => spans;
    /// <summary>지금 그린 사각형(Mesh 원본).</summary>
    public IReadOnlyList<Quad> Quads => quads;
    /// <summary>마지막으로 계산한 범위(방 좌표 칸).</summary>
    public RectInt Area { get; private set; }
    /// <summary>마지막으로 받은 테두리(방 좌표 칸, 비어 있으면 3차 규칙).</summary>
    public RectInt Frame { get; private set; }
    /// <summary>Mesh를 실제로 다시 만든 횟수(같은 결과면 늘지 않는다 — 회귀 검사용).</summary>
    public int BuildCount { get; private set; }
    public MeshRenderer Renderer => meshRenderer;
    public bool IsVisible => meshRenderer != null && meshRenderer.enabled && spans.Count > 0;

    /// <summary>방 좌표 칸 cell의 음영(구간에 없으면 None).</summary>
    public Shade ShadeAt(Vector2Int cell)
    {
        foreach (var span in spans)
            if (span.y == cell.y && cell.x >= span.xMin && cell.x < span.xMax) return span.shade;
        return Shade.None;
    }

    /// <summary>3차 규칙(테두리 없음, 위층 바닥 밑만)으로 다시 계산한다.</summary>
    public void Rebuild(HashSet<Vector2Int> region, HashSet<Vector2Int> solids, Rect cameraArea) => Rebuild(region, solids, cameraArea, default);

    /// <summary>
    /// 연결 공간의 빈 칸(region)·Platform 칸(solids)·카메라 범위(cameraArea, 방 좌표)·테두리(frame, 방 좌표 칸 — WaveCellTerrain.CameraFrameCells)로 음영을 다시 계산한다.
    /// frame이 비어 있으면 3차 규칙(위층 바닥 밑만). 결과가 지난번과 같으면 Mesh를 건드리지 않는다.
    /// </summary>
    public void Rebuild(HashSet<Vector2Int> region, HashSet<Vector2Int> solids, Rect cameraArea, RectInt frame)
    {
        var area = AreaFor(cameraArea);
        Area = area;
        Frame = frame;
        Compute(region, solids, area, frame, next);
        CollectQuads(next, nextQuads);
        if (mesh != null && SameAs())
        {
            if (meshRenderer != null) meshRenderer.enabled = spans.Count > 0 && meshRenderer.sharedMaterial != null;
            return;
        }
        spans.Clear();
        spans.AddRange(next);
        quads.Clear();
        quads.AddRange(nextQuads);
        Build();
    }

    /// <summary>음영을 모두 지운다(던전이 아닌 Wave·초기화).</summary>
    public void Clear()
    {
        Area = default;
        Frame = default;
        if (spans.Count == 0 && (meshRenderer == null || !meshRenderer.enabled)) return;
        spans.Clear();
        quads.Clear();
        if (mesh != null) mesh.Clear();
        if (meshRenderer != null) meshRenderer.enabled = false;
    }

    /// <summary>카메라 범위(방 좌표)를 덮는 칸 범위.</summary>
    public static RectInt AreaFor(Rect cameraArea)
    {
        int xMin = Mathf.FloorToInt(cameraArea.xMin + .001f), yMin = Mathf.FloorToInt(cameraArea.yMin + .001f);
        int xMax = Mathf.CeilToInt(cameraArea.xMax - .001f), yMax = Mathf.CeilToInt(cameraArea.yMax - .001f);
        return new RectInt(xMin, yMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin));
    }

    /// <summary>테두리(frame)를 사방 EdgeRows칸 줄인 상자(= 실내 외접). 이 안의 연결 공간 칸이 거리의 기준이다.</summary>
    public static RectInt InnerOf(RectInt frame)
        => new RectInt(frame.xMin + EdgeRows, frame.yMin + EdgeRows, Mathf.Max(0, frame.width - 2 * EdgeRows), Mathf.Max(0, frame.height - 2 * EdgeRows));

    /// <summary>3차 규칙(테두리 없음). Compute(region, solids, area, default, result)와 같다.</summary>
    public static void Compute(HashSet<Vector2Int> region, HashSet<Vector2Int> solids, RectInt area, List<Span> result) => Compute(region, solids, area, default, result);

    /// <summary>
    /// 음영 규칙(정적 — 회귀·편집기 검사도 같은 함수를 쓴다). result에는 줄마다 같은 단계가 이어진 구간을 담는다.
    /// frame(테두리)이 있으면: 대상 = (a) 테두리 위 3줄·좌우 3열 ∪ (b) 4방향 덩어리(진행·수직 두께 모두 MinimumDepth 이상) ∪ (c) 테두리 밖 빈 공간.
    ///   단계 = 보이는 칸(테두리 안쪽 상자 안의 region 칸 + 대상이 아닌 고체 칸)까지의 Chebyshev 거리(1~3 그라데이션, 4 이상 Solid).
    ///   범위 아래 GradientRows줄(바닥 그림자 자리)의 칸은 그 위 줄(범위 아래 + GradientRows)의 거리를 쓴다(가로 성분만).
    /// frame이 비어 있으면 3차 규칙(위층 바닥 밑만, 바닥 윗면에서 센 줄 수).
    /// </summary>
    public static void Compute(HashSet<Vector2Int> region, HashSet<Vector2Int> solids, RectInt area, RectInt frame, List<Span> result)
    {
        result.Clear();
        computedEdge = false;
        computedArea = area;
        if (frame.width <= 0 || frame.height <= 0) { ComputeDown(region, solids, area, result); return; }
        if (region == null || solids == null || region.Count == 0 || area.width <= 0 || area.height <= 0) return;
        computedEdge = true;
        int width = area.width, height = area.height, count = width * height;
        if (grid.Length < count) grid = new Shade[Mathf.NextPowerOfTwo(count)];
        if (target.Length < count) target = new bool[Mathf.NextPowerOfTwo(count)];
        Array.Clear(grid, 0, count);
        Array.Clear(target, 0, count);
        var inner = InnerOf(frame);
        // (a) 테두리 위·좌·우 띠와 (c) 테두리 밖 빈 공간. 아래 띠(바닥 벽)는 바닥 그림자가 덮으므로 넣지 않는다.
        for (int iy = 0; iy < height; iy++)
        {
            int y = area.yMin + iy;
            for (int ix = 0; ix < width; ix++)
            {
                int x = area.xMin + ix;
                bool inFrame = x >= frame.xMin && x < frame.xMax && y >= frame.yMin && y < frame.yMax;
                if (!inFrame || y >= inner.yMax || x < inner.xMin || x >= inner.xMax) target[iy * width + ix] = true;
            }
        }
        // (b) 4방향 덩어리: 실내 연결 공간 칸 옆에서 시작해 그 방향으로 이어진 고체(테두리 밖 빈 공간도 고체로 센다)가 범위 끝까지 닿고 MinimumDepth 이상이면.
        foreach (var cell in region)
        {
            if (!inner.Contains(cell)) continue;
            foreach (var d in Directions)
            {
                var p = cell + d;
                if (!solids.Contains(p)) continue;
                int depth = 0;
                while (area.Contains(p) && (solids.Contains(p) || !frame.Contains(p))) { depth++; p += d; }
                if (depth < MinimumDepth || area.Contains(p)) continue;
                var side = d.x == 0 ? Vector2Int.right : Vector2Int.up;
                for (int k = 1; k <= depth; k++)
                {
                    var q = cell + d * k;
                    // 옆 벽면·발판 윗면: 그 방향과 수직인 이웃이 연결 공간이면 보이는 지형으로 둔다.
                    if (region.Contains(q - side) || region.Contains(q + side)) continue;
                    // 얇은 판·턱: 수직으로 잰 고체 두께가 MinimumDepth 미만이면(벽에 붙은 3칸 덕트 바닥, 바닥 위 2칸 턱 + 바닥 벽 3칸 = 5) 덩어리가 아니다.
                    // 옛 규칙은 위의 이웃 검사만 있어 판의 가운데 줄·턱의 아랫줄만 대상이 되어 위아래가 딱 끊기는 띠가 생겼다.
                    if (Thickness(q, side, solids, area, frame) < MinimumDepth) continue;
                    target[(q.y - area.yMin) * width + (q.x - area.xMin)] = true;
                }
            }
        }
        // 거리: 범위를 한 칸씩 넓힌 격자에서 보이는 칸(0)부터 8방향으로 센다(GradientRows + 1에서 멈춘다).
        // 보이는 칸 = 실내(inner) 연결 공간 칸 + 대상이 아닌 실내 고체 칸(가리지 않고 남긴 덕트·턱·발판 윗면·옆 벽면).
        // 연결 공간만 기준으로 삼으면 남긴 고체와 맞닿은 음영이 1/3부터 시작해 경계가 딱 끊긴다(벽과 덕트가 만나는 곳, 덩어리 옆 기둥 등).
        int dw = width + 2, dh = height + 2, dcount = dw * dh;
        if (distance.Length < dcount) distance = new byte[Mathf.NextPowerOfTwo(dcount)];
        if (queue.Length < dcount) queue = new int[Mathf.NextPowerOfTwo(dcount)];
        const byte Far = GradientRows + 1;
        for (int i = 0; i < dcount; i++) distance[i] = Far;
        int head = 0, tail = 0;
        foreach (var cell in region)
        {
            if (!inner.Contains(cell)) continue;
            int dx = cell.x - area.xMin + 1, dy = cell.y - area.yMin + 1;
            if (dx < 0 || dy < 0 || dx >= dw || dy >= dh) continue;
            int index = dy * dw + dx;
            if (distance[index] == 0) continue;
            distance[index] = 0;
            queue[tail++] = index;
        }
        foreach (var cell in solids)
        {
            if (!inner.Contains(cell)) continue;
            if (area.Contains(cell) && target[(cell.y - area.yMin) * width + (cell.x - area.xMin)]) continue;
            int dx = cell.x - area.xMin + 1, dy = cell.y - area.yMin + 1;
            if (dx < 0 || dy < 0 || dx >= dw || dy >= dh) continue;
            int index = dy * dw + dx;
            if (distance[index] == 0) continue;
            distance[index] = 0;
            queue[tail++] = index;
        }
        while (head < tail)
        {
            int index = queue[head++];
            byte d = distance[index];
            if (d + 1 >= Far) continue;
            int cx = index % dw, cy = index / dw;
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int nx = cx + ox, ny = cy + oy;
                    if (nx < 0 || ny < 0 || nx >= dw || ny >= dh) continue;
                    int n = ny * dw + nx;
                    if (distance[n] <= d + 1) continue;
                    distance[n] = (byte)(d + 1);
                    queue[tail++] = n;
                }
        }
        // 칸 단계: 바닥 그림자 줄은 그 위 줄의 거리(가로 성분)를 쓴다.
        int band = Mathf.Min(GradientRows, height);
        for (int iy = 0; iy < height; iy++)
        {
            int sy = iy < band && band < height ? band : iy;
            for (int ix = 0; ix < width; ix++)
            {
                int index = iy * width + ix;
                if (!target[index]) continue;
                int d = distance[(sy + 1) * dw + ix + 1];
                grid[index] = (Shade)Mathf.Clamp(d, 1, (int)Shade.Solid);
            }
        }
        // 꼭짓점 거리: 꼭짓점에 닿은 네 칸 거리의 최솟값(GradientRows에서 자른다). 바닥 그림자 줄은 그 윗변 값을 그대로 내린다.
        int vw = width + 1, vh = height + 1, vcount = vw * vh;
        if (vertex.Length < vcount) vertex = new byte[Mathf.NextPowerOfTwo(vcount)];
        for (int j = 0; j < vh; j++)
            for (int i = 0; i < vw; i++)
            {
                // 꼭짓점 (area.xMin + i, area.yMin + j)의 왼쪽 아래 칸은 넓힌 격자의 (i, j).
                int a = distance[j * dw + i], b = distance[j * dw + i + 1], c = distance[(j + 1) * dw + i], e = distance[(j + 1) * dw + i + 1];
                vertex[j * vw + i] = (byte)Mathf.Min(GradientRows, Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, e)));
            }
        if (band < height)
            for (int j = 0; j < band; j++)
                for (int i = 0; i < vw; i++) vertex[j * vw + i] = vertex[band * vw + i];
        CollectSpans(area, result);
    }

    // 칸 q를 지나 axis 방향(양쪽)으로 이어진 고체 두께(q 포함). 덩어리 광선과 같이 범위 안에서 고체이거나 테두리 밖 빈 공간인 칸을 센다.
    // MinimumDepth에 닿으면 더 세지 않는다(덩어리 판정에는 문턱만 필요하다).
    private static int Thickness(Vector2Int q, Vector2Int axis, HashSet<Vector2Int> solids, RectInt area, RectInt frame)
    {
        int count = 1;
        for (var p = q + axis; count < MinimumDepth && area.Contains(p) && (solids.Contains(p) || !frame.Contains(p)); p += axis) count++;
        for (var p = q - axis; count < MinimumDepth && area.Contains(p) && (solids.Contains(p) || !frame.Contains(p)); p -= axis) count++;
        return count;
    }

    // 3차 규칙: 위층 바닥 밑만(아래 방향, 바닥 윗면에서 센 줄 수).
    private static void ComputeDown(HashSet<Vector2Int> region, HashSet<Vector2Int> solids, RectInt area, List<Span> result)
    {
        if (region == null || solids == null || region.Count == 0 || solids.Count == 0 || area.width <= 0 || area.height <= 0) return;
        int width = area.width, height = area.height, count = width * height;
        if (grid.Length < count) grid = new Shade[Mathf.NextPowerOfTwo(count)];
        if (columnHasRegion.Length < width) columnHasRegion = new bool[Mathf.NextPowerOfTwo(width)];
        Array.Clear(grid, 0, count);
        int limit = height + 4096;
        for (int ix = 0; ix < width; ix++)
        {
            int x = area.xMin + ix;
            bool any = false;
            for (int y = area.yMax - 1; y >= area.yMin; y--)
            {
                if (!region.Contains(new Vector2Int(x, y))) continue;
                any = true;
                if (!solids.Contains(new Vector2Int(x, y - 1))) continue;
                // 바닥 윗면(y의 아랫면) 아래로 이어진 고체 줄 수. 범위 밖까지 이어져도 센다.
                // 실내에 떠 있는 덩어리(아래가 빈 칸)는 범위 아랫변에 닿지 않으므로 가리지 않는다.
                int depth = 0;
                while (depth < limit && solids.Contains(new Vector2Int(x, y - 1 - depth))) depth++;
                if (depth < MinimumDepth || y - depth > area.yMin) continue;
                for (int k = 1; k <= depth; k++)
                {
                    int cy = y - k;
                    if (cy < area.yMin) break;
                    if (cy >= area.yMax) continue;
                    // 옆 벽면: 같은 줄 좌우가 연결 공간이면 그 방을 둘러싼 벽으로 보이게 둔다.
                    if (region.Contains(new Vector2Int(x - 1, cy)) || region.Contains(new Vector2Int(x + 1, cy))) continue;
                    var shade = k <= GradientRows ? (Shade)k : Shade.Solid;
                    int index = (cy - area.yMin) * width + ix;
                    if (shade > grid[index]) grid[index] = shade;
                }
            }
            columnHasRegion[ix] = any;
        }
        // 바깥 벽 기둥(연결 공간이 없는 열): 왼쪽에서, 오른쪽에서 한 번씩 옆 열의 음영을 이어받는다.
        for (int pass = 0; pass < 2; pass++)
        {
            int step = pass == 0 ? 1 : -1;
            for (int ix = pass == 0 ? 1 : width - 2; ix >= 0 && ix < width; ix += step)
            {
                if (columnHasRegion[ix]) continue;
                int from = ix - step;
                int x = area.xMin + ix;
                for (int iy = 0; iy < height; iy++)
                {
                    int index = iy * width + ix;
                    if (grid[index] != Shade.None) continue;
                    var source = grid[iy * width + from];
                    if (source == Shade.None || !solids.Contains(new Vector2Int(x, area.yMin + iy))) continue;
                    grid[index] = source;
                }
            }
        }
        CollectSpans(area, result);
    }

    private static void CollectSpans(RectInt area, List<Span> result)
    {
        int width = area.width, height = area.height;
        for (int iy = 0; iy < height; iy++)
        {
            int ix = 0;
            while (ix < width)
            {
                var shade = grid[iy * width + ix];
                if (shade == Shade.None) { ix++; continue; }
                int start = ix;
                while (ix < width && grid[iy * width + ix] == shade) ix++;
                result.Add(new Span { y = area.yMin + iy, xMin = area.xMin + start, xMax = area.xMin + ix, shade = shade });
            }
        }
    }

    /// <summary>
    /// 바로 앞 Compute 결과(source = 그 결과 구간)로 그릴 사각형을 만든다(Rebuild가 쓰는 것과 같다 — 회귀 검사도 쓴다). 테두리 규칙이면 칸 꼭짓점 거리를 넣고,
    /// 줄 방향으로 같은 값이 이어진 칸(위·아래 변이 각각 한 값)은 가로로, 열 방향으로 같은 값인 칸(좌·우 변이 각각 한 값)은 세로로 합친다.
    /// 3차 규칙이면 구간마다 윗변 k − 1·아랫변 k(Solid는 모두 불투명)로 3차와 같은 그림을 만든다.
    /// </summary>
    public static void CollectQuads(List<Span> source, List<Quad> result)
    {
        result.Clear();
        var area = computedArea;
        if (!computedEdge)
        {
            foreach (var span in source)
            {
                byte bottom = (byte)Mathf.Min((int)span.shade, GradientRows);
                byte top = span.shade == Shade.Solid ? (byte)GradientRows : (byte)((int)span.shade - 1);
                result.Add(new Quad { xMin = span.xMin, xMax = span.xMax, yMin = span.y, yMax = span.y + 1, bottomLeft = bottom, bottomRight = bottom, topLeft = top, topRight = top });
            }
            return;
        }
        int width = area.width, height = area.height, count = width * height, vw = width + 1;
        if (count <= 0) return;
        if (used.Length < count) used = new bool[Mathf.NextPowerOfTwo(count)];
        Array.Clear(used, 0, count);
        // 가로로 합치기(위·아래 변이 각각 한 값인 칸).
        for (int iy = 0; iy < height; iy++)
        {
            int ix = 0;
            while (ix < width)
            {
                int index = iy * width + ix;
                byte bottom = vertex[iy * vw + ix], top = vertex[(iy + 1) * vw + ix];
                if (!target[index] || vertex[iy * vw + ix + 1] != bottom || vertex[(iy + 1) * vw + ix + 1] != top) { ix++; continue; }
                int start = ix;
                while (ix < width && target[iy * width + ix] && vertex[iy * vw + ix + 1] == bottom && vertex[(iy + 1) * vw + ix + 1] == top
                       && vertex[iy * vw + ix] == bottom && vertex[(iy + 1) * vw + ix] == top)
                {
                    used[iy * width + ix] = true;
                    ix++;
                }
                result.Add(new Quad { xMin = area.xMin + start, xMax = area.xMin + ix, yMin = area.yMin + iy, yMax = area.yMin + iy + 1, bottomLeft = bottom, bottomRight = bottom, topLeft = top, topRight = top });
            }
        }
        // 세로로 합치기(좌·우 변이 각각 한 값인 칸).
        for (int ix = 0; ix < width; ix++)
        {
            int iy = 0;
            while (iy < height)
            {
                int index = iy * width + ix;
                byte left = vertex[iy * vw + ix], right = vertex[iy * vw + ix + 1];
                if (!target[index] || used[index] || vertex[(iy + 1) * vw + ix] != left || vertex[(iy + 1) * vw + ix + 1] != right) { iy++; continue; }
                int start = iy;
                while (iy < height && target[iy * width + ix] && !used[iy * width + ix] && vertex[iy * vw + ix] == left && vertex[iy * vw + ix + 1] == right
                       && vertex[(iy + 1) * vw + ix] == left && vertex[(iy + 1) * vw + ix + 1] == right)
                {
                    used[iy * width + ix] = true;
                    iy++;
                }
                result.Add(new Quad { xMin = area.xMin + ix, xMax = area.xMin + ix + 1, yMin = area.yMin + start, yMax = area.yMin + iy, bottomLeft = left, topLeft = left, topRight = right, bottomRight = right });
            }
        }
        // 나머지(모서리처럼 네 꼭짓점 값이 다른 칸)는 한 칸씩.
        for (int iy = 0; iy < height; iy++)
            for (int ix = 0; ix < width; ix++)
            {
                int index = iy * width + ix;
                if (!target[index] || used[index]) continue;
                result.Add(new Quad
                {
                    xMin = area.xMin + ix, xMax = area.xMin + ix + 1, yMin = area.yMin + iy, yMax = area.yMin + iy + 1,
                    bottomLeft = vertex[iy * vw + ix], topLeft = vertex[(iy + 1) * vw + ix], topRight = vertex[(iy + 1) * vw + ix + 1], bottomRight = vertex[iy * vw + ix + 1],
                });
            }
    }

    private bool SameAs()
    {
        if (next.Count != spans.Count || nextQuads.Count != quads.Count) return false;
        for (int i = 0; i < next.Count; i++) if (!next[i].Equals(spans[i])) return false;
        for (int i = 0; i < nextQuads.Count; i++) if (!nextQuads[i].Equals(quads[i])) return false;
        return true;
    }

    private float U(byte d) => Mathf.Lerp(uClear, uOpaque, d / (float)GradientRows);

    private void Build()
    {
        BuildCount++;
        var rules = WaveRoomRulesSO.Default;
        var sprite = rules != null ? rules.blinderSprite : null;
        var material = rules != null ? rules.blinderMaterial : null;
        if (mesh == null)
        {
            mesh = new Mesh { name = "RoomFloorShade", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sortingOrder = SortingOrder;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            properties = new MaterialPropertyBlock();
        }
        mesh.Clear();
        if (quads.Count == 0 || sprite == null || material == null)
        {
            meshRenderer.enabled = false;
            return;
        }
        if (uvSprite != sprite)
        {
            // 바닥 그림자 Sprite는 가로 그라데이션이다(왼쪽 불투명 → 오른쪽 투명, 세로는 같은 값). 꼭짓점 거리를 u로 넣는다(0 = 투명 끝, GradientRows = 불투명 끝).
            uvSprite = sprite;
            var uv = sprite.uv;
            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            foreach (var p in uv) { uMin = Mathf.Min(uMin, p.x); uMax = Mathf.Max(uMax, p.x); vMin = Mathf.Min(vMin, p.y); vMax = Mathf.Max(vMax, p.y); }
            uOpaque = uMin; uClear = uMax; vMiddle = (vMin + vMax) * .5f;
            properties.SetTexture(MainTexId, sprite.texture);
            meshRenderer.SetPropertyBlock(properties);
        }
        meshRenderer.sharedMaterial = material;
        vertices.Clear(); uvs.Clear(); colors.Clear(); triangles.Clear();
        foreach (var quad in quads)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(quad.xMin, quad.yMin, 0f));
            vertices.Add(new Vector3(quad.xMin, quad.yMax, 0f));
            vertices.Add(new Vector3(quad.xMax, quad.yMax, 0f));
            vertices.Add(new Vector3(quad.xMax, quad.yMin, 0f));
            uvs.Add(new Vector2(U(quad.bottomLeft), vMiddle));
            uvs.Add(new Vector2(U(quad.topLeft), vMiddle));
            uvs.Add(new Vector2(U(quad.topRight), vMiddle));
            uvs.Add(new Vector2(U(quad.bottomRight), vMiddle));
            // 색은 Sprite 그대로(바닥 그림자 SpriteRenderer의 흰색과 같다).
            for (int i = 0; i < 4; i++) colors.Add(new Color32(255, 255, 255, 255));
            // 모서리 칸은 같은 값끼리 잇는 대각선으로 나눈다(보간이 한쪽으로 쏠리지 않게).
            if (quad.topLeft == quad.bottomRight && quad.bottomLeft != quad.topRight)
            {
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 3);
                triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start + 3);
            }
            else
            {
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
        }
        mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        meshRenderer.enabled = true;
    }

    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        mesh = null;
    }
}
