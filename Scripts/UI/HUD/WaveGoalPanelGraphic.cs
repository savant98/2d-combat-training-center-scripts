using UnityEngine;

// 원본 목표 글씨와 파티클 뒤에 놓이는 벡터 Panel이다.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WaveGoalPanelGraphic : UnityEngine.UI.MaskableGraphic
{
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        var r = GetPixelAdjustedRect();
        float cut = Mathf.Min(r.width, r.height) * .12f;
        var outline = new[]
        {
            new Vector2(r.xMin + cut, r.yMin), new Vector2(r.xMax - cut, r.yMin),
            new Vector2(r.xMax, r.yMin + cut), new Vector2(r.xMax, r.yMax - cut),
            new Vector2(r.xMax - cut, r.yMax), new Vector2(r.xMin + cut, r.yMax),
            new Vector2(r.xMin, r.yMax - cut), new Vector2(r.xMin, r.yMin + cut)
        };
        int center = vh.currentVertCount;
        var body = new Color32(9, 18, 27, 220);
        vh.AddVert(r.center, body, Vector2.zero);
        foreach (var p in outline) vh.AddVert(p, body, Vector2.zero);
        for (int i = 0; i < outline.Length; i++) vh.AddTriangle(center, center + 1 + i, center + 1 + (i + 1) % outline.Length);

        var rim = new Color32(178, 212, 226, 220);
        for (int i = 0; i < outline.Length; i++) Stroke(vh, outline[i], outline[(i + 1) % outline.Length], 1.5f, rim);
        var blue = new Color32(40, 190, 255, 255);
        float x0 = r.xMin + cut + 10, x1 = r.xMax - cut - 10;
        Stroke(vh, new Vector2(x0, r.yMax - 7), new Vector2(x0 + 42, r.yMax - 7), 3, blue);
        Stroke(vh, new Vector2(x1 - 42, r.yMin + 7), new Vector2(x1, r.yMin + 7), 3, blue);
        for (int side = -1; side <= 1; side += 2)
        {
            float x = r.center.x + side * (r.width * .5f - 8);
            Stroke(vh, new Vector2(x, r.center.y - 18), new Vector2(x, r.center.y + 18), 2.5f, blue);
            for (int i = 0; i < 3; i++)
                Stroke(vh, new Vector2(x - side * 5, r.center.y - 10 + i * 10), new Vector2(x - side * 9, r.center.y - 10 + i * 10), 2, rim);
        }
    }

    // 격자 Tool도 같은 벡터 Panel을 읽어 미리보기를 만든다.
    public void BuildPanelMesh(Mesh output)
    {
        using (var vh = new UnityEngine.UI.VertexHelper())
        {
            OnPopulateMesh(vh);
            vh.FillMesh(output);
        }
    }

    private static void Stroke(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 tint)
    {
        Vector2 direction = (b - a).normalized;
        Vector2 n = new Vector2(-direction.y, direction.x) * width * .5f;
        int i = vh.currentVertCount;
        vh.AddVert(a - n, tint, Vector2.zero); vh.AddVert(b - n, tint, Vector2.zero);
        vh.AddVert(b + n, tint, Vector2.zero); vh.AddVert(a + n, tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }
}
