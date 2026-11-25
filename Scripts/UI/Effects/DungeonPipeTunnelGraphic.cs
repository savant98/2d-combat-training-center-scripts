using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면을 가득 채운 관 속(옆에서 본 관)을 생성 Mesh로 그린다. 위·아래 흑연 관벽과 더 어두운 가운데 통로,
/// 진행 반대 방향으로 흐르는 세로 리브(얇은 흰·청록 발광선)와 관벽 이음매의 발광 띠, 통로의 속도선, 가장자리 어둠.
/// </summary>
/// <remarks>
/// 진행 방향 기준 좌표 p(0 = 진행 반대쪽 화면 끝, 화면 폭 = 진행 방향 쪽 끝)로 그린 뒤 Direction에 맞춰 좌우를 뒤집는다.
/// 덮인 구역은 [ (1 - head)·폭, (1 - tail)·폭 ]이다. 덮기는 진행 방향 쪽 끝에서 밀려오고, 걷기도 같은 쪽에서 걷혀 화면 속 세계가
/// 진행 반대 방향으로 지나가는 것처럼 보인다. 정점 색은 UI Canvas(정점 색 감마 공간)의 sRGB 값이다. 캐주얼 부품 없이 얇은 선만 쓴다.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DungeonPipeTunnelGraphic : MaskableGraphic
{
    [Header("관벽·통로")]
    [SerializeField, Tooltip("화면 위·아래 끝 쪽 관벽 색.")] private Color32 wallEdge = new Color32(0x0c, 0x10, 0x14, 255);
    [SerializeField, Tooltip("통로에 닿는 쪽 관벽 색.")] private Color32 wallFace = new Color32(0x1b, 0x22, 0x28, 255);
    [SerializeField, Tooltip("관벽에 가까운 통로 색.")] private Color32 passageEdge = new Color32(0x0b, 0x0f, 0x13, 255);
    [SerializeField, Tooltip("통로 가운데 색(가장 어둡다).")] private Color32 passageCore = new Color32(0x05, 0x07, 0x0a, 255);
    [SerializeField, Range(.1f, .4f), Tooltip("관벽 한쪽의 높이(화면 높이 대비).")] private float wallFraction = .24f;

    [Header("발광선")]
    [SerializeField] private Color32 ribGlow = new Color32(0x8f, 0xf4, 0xff, 205);
    [SerializeField] private Color32 ribCyan = new Color32(0x2b, 0xc8, 0xe2, 190);
    [SerializeField] private Color32 seamDim = new Color32(0x17, 0x4b, 0x5c, 150);
    [SerializeField] private Color32 seamCore = new Color32(0xe8, 0xfb, 0xff, 215);
    [SerializeField, Range(.08f, .5f), Tooltip("리브 간격(화면 높이 대비).")] private float ribSpacing = .19f;
    [SerializeField, Range(2, 8), Tooltip("리브 몇 개마다 이음매 띠를 둘지.")] private int seamEvery = 4;
    [SerializeField, Min(.5f)] private float ribWidth = 1.5f;
    [SerializeField, Min(1f)] private float seamWidth = 7f;
    [SerializeField, Min(0f), Tooltip("리브가 흐르는 빠르기(초당 화면 높이).")] private float flowSpeed = 7f;

    [Header("속도선")]
    [SerializeField, Range(0, 16)] private int speedLines = 7;
    [SerializeField, Min(0f), Tooltip("속도선이 리브보다 빠른 배율.")] private float speedLineFactor = 1.8f;

    [Header("가장자리")]
    [SerializeField, Range(0f, .3f), Tooltip("진행 방향 양 끝을 어둡게 하는 폭(화면 폭 대비).")] private float vignette = .12f;
    [SerializeField, Range(0, 255)] private int vignetteAlpha = 150;
    [SerializeField, Min(0f), Tooltip("덮는 중·걷는 중의 관 끝 테두리 안쪽 그늘 폭.")] private float rimShade = 36f;

    private int direction = 1;
    private float head, tail, scroll;
    private float clipA, clipB;          // 이번에 그리는 덮인 구역(p 기준)
    private Rect area;

    /// <summary>진행 방향(+1 = 화면 오른쪽으로 지나감, -1 = 왼쪽).</summary>
    public int Direction
    {
        get => direction;
        set { int next = value < 0 ? -1 : 1; if (next != direction) { direction = next; SetVerticesDirty(); } }
    }
    public float Head => head;
    public float Tail => tail;
    /// <summary>덮인 구역이 화면 전체인지.</summary>
    public bool FullyCovered => head >= 1f && tail <= 0f;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    public void ResetFlow()
    {
        scroll = 0f;
        SetVerticesDirty();
    }

    public void SetCoverage(float nextHead, float nextTail)
    {
        nextHead = Mathf.Clamp01(nextHead); nextTail = Mathf.Clamp01(nextTail);
        if (Mathf.Approximately(nextHead, head) && Mathf.Approximately(nextTail, tail)) return;
        head = nextHead; tail = nextTail;
        SetVerticesDirty();
    }

    /// <summary>리브·속도선을 흘린다. 덮인 구역이 있을 때만 다시 그린다.</summary>
    public void Advance(float deltaTime)
    {
        if (deltaTime <= 0f || head <= tail || !isActiveAndEnabled) return;
        scroll += deltaTime * flowSpeed * Mathf.Max(1f, rectTransform.rect.height);
        // 아주 오래 켜 두면 부동소수 정밀도가 떨어지므로 한 번 되감는다(그 순간 무늬가 한 번 튈 수 있지만 관 통과 한 번은 1초 남짓이다).
        if (scroll > 1e6f) scroll = Mathf.Repeat(scroll, 1e5f);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        area = GetPixelAdjustedRect();
        float width = area.width, height = area.height;
        clipA = (1f - head) * width;
        clipB = (1f - tail) * width;
        if (width <= 0f || height <= 0f || clipB - clipA <= .01f) return;

        float wall = wallFraction * height;
        float bottom = area.yMin, top = area.yMax;
        float low = bottom + wall, high = top - wall, mid = (low + high) * .5f;

        // 관벽과 통로. 관벽은 화면 끝으로 갈수록 어두워지고, 통로는 가운데가 가장 어둡다.
        Band(vh, 0f, width, bottom, low, wallEdge, wallFace);
        Band(vh, 0f, width, high, top, wallFace, wallEdge);
        Band(vh, 0f, width, low, mid, passageEdge, passageCore);
        Band(vh, 0f, width, mid, high, passageCore, passageEdge);

        // 관벽 안쪽의 겹판 선(얇고 어두운 청록).
        var lining = seamDim; lining.a = (byte)(seamDim.a * .7f);
        Line(vh, 0f, width, low - wall * .38f, 1f, lining);
        Line(vh, 0f, width, high + wall * .38f, 1f, lining);

        // 관벽과 통로가 만나는 선의 발광(청록)과 통로 쪽으로 번지는 옅은 빛.
        var glow = ribCyan; glow.a = 46; var clear = ribCyan; clear.a = 0;
        Band(vh, 0f, width, low, low + 14f, glow, clear);
        Band(vh, 0f, width, high - 14f, high, clear, glow);
        Line(vh, 0f, width, low, 2f, ribCyan);
        Line(vh, 0f, width, high, 2f, ribCyan);

        // 세로 리브: 진행 반대 방향으로 흐른다. 일정 간격마다 관벽 이음매 띠를 더한다.
        float spacing = Mathf.Max(8f, ribSpacing * height);
        int first = Mathf.FloorToInt(scroll / spacing);
        float offset = scroll - first * spacing;
        int count = Mathf.CeilToInt(width / spacing) + 2;
        for (int k = 0; k < count; k++)
        {
            float p = k * spacing - offset;
            if (p < -seamWidth || p > width + seamWidth) continue;
            int id = k + first;
            bool seam = ((id % seamEvery) + seamEvery) % seamEvery == 0;
            if (seam)
            {
                var band = seamDim; var bandFade = seamDim; bandFade.a = (byte)(seamDim.a * .35f);
                Rib(vh, p, seamWidth, bottom, low, bandFade, band);
                Rib(vh, p, seamWidth, high, top, band, bandFade);
            }
            var line = seam ? seamCore : ribGlow;
            var fade = line; fade.a = (byte)(line.a * .22f);
            float w = seam ? ribWidth + .5f : ribWidth;
            Rib(vh, p, w, bottom, low, fade, line);
            Rib(vh, p, w, high, top, line, fade);
            var through = ribCyan; through.a = 26;
            Rib(vh, p, 1f, low, high, through, through);
        }

        // 통로의 속도선: 앞쪽(진행 반대 방향 끝)이 밝고 뒤로 흐려진다.
        for (int i = 0; i < speedLines; i++)
        {
            float y = Mathf.Lerp(low, high, .12f + .76f * Hash(i, 1));
            float length = (.10f + .22f * Hash(i, 2)) * width;
            float speed = speedLineFactor * (.8f + .6f * Hash(i, 3));
            float span = width + length;
            float start = Mathf.Repeat(Hash(i, 5) * span - scroll * speed, span) - length;
            var bright = new Color32(0xe8, 0xfb, 0xff, (byte)(40 + 55 * Hash(i, 4)));
            var tailColor = bright; tailColor.a = 0;
            Quad(vh, start, start + length, y - .75f, y + .75f, bright, tailColor, bright, tailColor);
        }

        // 진행 방향 양 끝을 어둡게 한다.
        if (vignette > 0f && vignetteAlpha > 0)
        {
            var dark = new Color32(0, 0, 0, (byte)vignetteAlpha); var none = new Color32(0, 0, 0, 0);
            Quad(vh, 0f, vignette * width, bottom, top, dark, none, dark, none);
            Quad(vh, width - vignette * width, width, bottom, top, none, dark, none, dark);
        }

        // 덮는 중·걷는 중에 보이는 관 끝: 안쪽 그늘 + 밝은 테두리 + 얇은 청록 선.
        var shade = new Color32(0, 0, 0, 170); var shadeNone = new Color32(0, 0, 0, 0);
        if (clipA > .5f)
        {
            Quad(vh, clipA, clipA + rimShade, bottom, top, shade, shadeNone, shade, shadeNone);
            Rib(vh, clipA + 1f, 2f, bottom, top, ribGlow, ribGlow);
            Rib(vh, clipA + 7f, 1f, bottom, top, ribCyan, ribCyan);
        }
        if (clipB < width - .5f)
        {
            Quad(vh, clipB - rimShade, clipB, bottom, top, shadeNone, shade, shadeNone, shade);
            Rib(vh, clipB - 1f, 2f, bottom, top, ribGlow, ribGlow);
            Rib(vh, clipB - 7f, 1f, bottom, top, ribCyan, ribCyan);
        }
    }

    // 세로 선(p 중심, 폭 w), 아래·위 색.
    private void Rib(VertexHelper vh, float p, float w, float y0, float y1, Color32 lower, Color32 upper)
        => Quad(vh, p - w * .5f, p + w * .5f, y0, y1, lower, lower, upper, upper);
    // 가로 띠(위아래 그라데이션).
    private void Band(VertexHelper vh, float p0, float p1, float y0, float y1, Color32 lower, Color32 upper)
        => Quad(vh, p0, p1, y0, y1, lower, lower, upper, upper);
    private void Line(VertexHelper vh, float p0, float p1, float y, float w, Color32 tint)
        => Quad(vh, p0, p1, y - w * .5f, y + w * .5f, tint, tint, tint, tint);

    // p 구간 [p0, p1] × [y0, y1]의 사각형을 덮인 구역으로 잘라 그린다. 색은 (p0 아래, p1 아래, p0 위, p1 위).
    private void Quad(VertexHelper vh, float p0, float p1, float y0, float y1, Color32 a0, Color32 a1, Color32 b0, Color32 b1)
    {
        if (p1 <= p0 || y1 <= y0) return;
        float s = Mathf.Max(p0, clipA), e = Mathf.Min(p1, clipB);
        if (e <= s) return;
        float fs = (s - p0) / (p1 - p0), fe = (e - p0) / (p1 - p0);
        Color32 c0 = Color32.Lerp(a0, a1, fs), c1 = Color32.Lerp(a0, a1, fe);
        Color32 d0 = Color32.Lerp(b0, b1, fs), d1 = Color32.Lerp(b0, b1, fe);
        float x0 = ToX(s), x1 = ToX(e);
        int i = vh.currentVertCount;
        vh.AddVert(new Vector3(x0, y0), c0, Vector2.zero);
        vh.AddVert(new Vector3(x1, y0), c1, Vector2.zero);
        vh.AddVert(new Vector3(x1, y1), d1, Vector2.zero);
        vh.AddVert(new Vector3(x0, y1), d0, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }

    private float ToX(float p) => direction > 0 ? area.xMin + p : area.xMax - p;

    private static float Hash(int index, int salt)
    {
        float v = Mathf.Sin(index * 12.9898f + salt * 78.233f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }
}
