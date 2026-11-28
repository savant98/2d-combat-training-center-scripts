using UnityEngine;
using UnityEngine.UI;

/// <summary>무장에 연결된 기술의 해금 상태와 현재 선택을 같은 경로 위에 표시한다.</summary>
[AddComponentMenu("UI/Armament Branch Graphic")]
public sealed class ArmamentBranchGraphic : Image
{
    private bool available;
    private bool focused;

    public void SetAvailable(bool value)
    {
        if (available == value) return;
        available = value;
        SetVerticesDirty();
    }

    public void SetFocused(bool value)
    {
        if (focused == value) return;
        focused = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f) return;

        Color ink = focused ? TrainingBoardStyle.LineBright
            : available ? TrainingBoardStyle.Line : TrainingBoardStyle.SecondaryText;
        ink *= color;
        Color glow = ink;
        glow.a *= focused ? .21f : .09f;
        AddStrip(mesh, rect.xMin, rect.xMax, rect.center.y, focused ? 8f : 6f, glow);

        if (available)
        {
            AddStrip(mesh, rect.xMin, rect.xMax, rect.center.y, focused ? 4.2f : 3f, ink);
            return;
        }

        if (!focused) ink.a *= .74f;
        for (float x = rect.xMin; x < rect.xMax; x += 19f)
            AddStrip(mesh, x, Mathf.Min(x + 10f, rect.xMax), rect.center.y, focused ? 3.5f : 2.5f, ink);
    }

    private static void AddStrip(VertexHelper mesh, float left, float right, float y, float width, Color color)
    {
        int first = mesh.currentVertCount;
        float half = width * .5f;
        mesh.AddVert(new Vector2(left, y - half), color, Vector2.zero);
        mesh.AddVert(new Vector2(right, y - half), color, Vector2.zero);
        mesh.AddVert(new Vector2(right, y + half), color, Vector2.zero);
        mesh.AddVert(new Vector2(left, y + half), color, Vector2.zero);
        mesh.AddTriangle(first, first + 1, first + 2);
        mesh.AddTriangle(first, first + 2, first + 3);
    }
}
