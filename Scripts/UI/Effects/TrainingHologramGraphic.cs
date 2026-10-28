using UnityEngine;
using UnityEngine.UI;

/// <summary>홀로그램의 얇은 프레임·투사 빔·주사선을 UI Mesh로 그린다.</summary>
[AddComponentMenu("UI/Training Hologram Graphic")]
public class TrainingHologramGraphic : UnityEngine.UI.Image
{
    public enum Shape { Frame, Beam, Scanlines }

    [SerializeField] private Shape shape;
    [SerializeField] private Color tint = new Color(.26f, .72f, 1f, 1f);
    [SerializeField, Min(.1f)] private float thickness = 1.5f;
    [SerializeField, Min(0f)] private float cornerCut = 8f;
    [SerializeField, Min(2f)] private float scanlineSpacing = 18f;
    [SerializeField, Range(0f, 1f)] private float fillAlpha;

    private readonly Vector2[] outer = new Vector2[8];
    private readonly Vector2[] inner = new Vector2[8];

    // 공통 메뉴의 흰색 선택 효과와 투명도에 별도의 청색 조색을 곱한다.
    public void Configure(Shape newShape, Color newTint, float newThickness = 1.5f,
        float newCornerCut = 8f, float newFillAlpha = 0f)
    {
        shape = newShape;
        tint = newTint;
        thickness = Mathf.Max(.1f, newThickness);
        cornerCut = Mathf.Max(0f, newCornerCut);
        fillAlpha = Mathf.Clamp01(newFillAlpha);
        sprite = null;
        overrideSprite = null;
        type = Type.Simple;
        SetVerticesDirty();
    }

    public float ScanlineSpacing
    {
        get => scanlineSpacing;
        set { scanlineSpacing = Mathf.Max(2f, value); SetVerticesDirty(); }
    }

    public override Texture mainTexture => Texture2D.whiteTexture;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        Color drawColor = color * tint;
        switch (shape)
        {
            case Shape.Beam: DrawBeam(vh, rect, drawColor); break;
            case Shape.Scanlines: DrawScanlines(vh, rect, drawColor); break;
            default: DrawFrame(vh, rect, drawColor); break;
        }
    }

    private void DrawFrame(VertexHelper vh, Rect rect, Color drawColor)
    {
        float cut = Mathf.Min(cornerCut, Mathf.Min(rect.width, rect.height) * .5f);
        float line = Mathf.Min(thickness, Mathf.Min(rect.width, rect.height) * .49f);
        Corners(rect, cut, outer);
        var inset = new Rect(rect.xMin + line, rect.yMin + line,
            rect.width - line * 2f, rect.height - line * 2f);
        float innerCut = Mathf.Max(0f, cut - line * (2f - Mathf.Sqrt(2f)));
        Corners(inset, Mathf.Min(innerCut, Mathf.Min(inset.width, inset.height) * .5f), inner);

        if (fillAlpha > 0f)
        {
            Color fill = drawColor;
            fill.a *= fillAlpha;
            int start = vh.currentVertCount;
            vh.AddVert(rect.center, fill, Vector2.zero);
            for (int i = 0; i < 8; i++) vh.AddVert(inner[i], fill, Vector2.zero);
            for (int i = 0; i < 8; i++) vh.AddTriangle(start, start + i + 1, start + (i + 1) % 8 + 1);
        }
        for (int i = 0; i < 8; i++)
        {
            int next = (i + 1) % 8;
            Quad(vh, outer[i], outer[next], inner[next], inner[i], drawColor);
        }
    }

    private static void Corners(Rect r, float cut, Vector2[] points)
    {
        points[0] = new Vector2(r.xMin + cut, r.yMin);
        points[1] = new Vector2(r.xMax - cut, r.yMin);
        points[2] = new Vector2(r.xMax, r.yMin + cut);
        points[3] = new Vector2(r.xMax, r.yMax - cut);
        points[4] = new Vector2(r.xMax - cut, r.yMax);
        points[5] = new Vector2(r.xMin + cut, r.yMax);
        points[6] = new Vector2(r.xMin, r.yMax - cut);
        points[7] = new Vector2(r.xMin, r.yMin + cut);
    }

    private static void DrawBeam(VertexHelper vh, Rect rect, Color drawColor)
    {
        float halfSource = Mathf.Min(rect.width * .04f, 3f);
        Color bottom = drawColor;
        bottom.a *= .35f;
        Color top = drawColor;
        top.a *= .04f;
        vh.AddVert(new Vector2(rect.center.x - halfSource, rect.yMin), bottom, Vector2.zero);
        vh.AddVert(new Vector2(rect.center.x + halfSource, rect.yMin), bottom, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMax, rect.yMax), top, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMin, rect.yMax), top, Vector2.zero);
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }

    private void DrawScanlines(VertexHelper vh, Rect rect, Color drawColor)
    {
        drawColor.a *= .08f;
        float spacing = Mathf.Max(2f, scanlineSpacing);
        float line = Mathf.Min(thickness, spacing * .5f);
        // 주사선은 화면 안에서만 생성하고 실제 클리핑은 기존 Canvas 마스크가 담당한다.
        for (float y = rect.yMin + spacing * .5f; y < rect.yMax; y += spacing)
        {
            float top = Mathf.Min(rect.yMax, y + line);
            Quad(vh, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y),
                new Vector2(rect.xMax, top), new Vector2(rect.xMin, top), drawColor);
        }
    }

    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color drawColor)
    {
        int start = vh.currentVertCount;
        vh.AddVert(a, drawColor, Vector2.zero);
        vh.AddVert(b, drawColor, Vector2.zero);
        vh.AddVert(c, drawColor, Vector2.zero);
        vh.AddVert(d, drawColor, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 3, start);
    }
}
