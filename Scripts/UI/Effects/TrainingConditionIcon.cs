using UnityEngine;

/// <summary>성적·피격·시간 구획의 작은 아이콘을 같은 선 굵기로 그린다.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TrainingConditionIcon : UnityEngine.UI.MaskableGraphic
{
    public enum Symbol { Medal, Hit, Clock, Warning }
    [SerializeField] private Symbol symbol;
    public void Configure(Symbol value, Color tint) { symbol = value; color = tint; raycastTarget = false; SetVerticesDirty(); }
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float unit = Mathf.Min(rect.width, rect.height) / 32f;
        if (symbol == Symbol.Medal)
        {
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * .5f + i * Mathf.PI / 5f, b = a + Mathf.PI / 5f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? 13 : 6);
                Vector2 q = new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * (i % 2 == 0 ? 6 : 13);
                Line(vh, center + p * unit, center + q * unit, unit * 2);
            }
        }
        else if (symbol == Symbol.Warning)
        {
            Vector2[] points = { new Vector2(0, 13), new Vector2(13, -11), new Vector2(-13, -11), new Vector2(0, 13) };
            for (int i = 1; i < points.Length; i++) Line(vh, center + points[i - 1] * unit, center + points[i] * unit, unit * 2);
            Line(vh, center + new Vector2(0, 6) * unit, center + new Vector2(0, -3) * unit, unit * 2);
            Line(vh, center + new Vector2(0, -7) * unit, center + new Vector2(0, -8) * unit, unit * 2.5f);
        }
        else
        {
            for (int i = 0; i < 20; i++)
            {
                float a = i * Mathf.PI / 10, b = (i + 1) * Mathf.PI / 10;
                Line(vh, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * unit * 12,
                    center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * unit * 12, unit * 2);
            }
            if (symbol == Symbol.Clock)
            {
                Line(vh, center, center + new Vector2(0, 8) * unit, unit * 2);
                Line(vh, center, center + new Vector2(6, -3) * unit, unit * 2);
            }
            else
            {
                Line(vh, center + new Vector2(-5, -5) * unit, center + new Vector2(5, 5) * unit, unit * 2);
                Line(vh, center + new Vector2(5, -5) * unit, center + new Vector2(-5, 5) * unit, unit * 2);
            }
        }
    }
    private void Line(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, float thickness)
    {
        Vector2 delta = b - a;
        if (delta.sqrMagnitude < .0001f) return;
        Vector2 n = new Vector2(-delta.y, delta.x).normalized * thickness * .5f;
        int i = vh.currentVertCount;
        vh.AddVert(a - n, color, Vector2.zero); vh.AddVert(a + n, color, Vector2.zero);
        vh.AddVert(b + n, color, Vector2.zero); vh.AddVert(b - n, color, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }
}
