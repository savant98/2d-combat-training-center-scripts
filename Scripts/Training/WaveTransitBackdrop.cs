using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 서로 떨어진 Wave 실내 사이에 어두운 구조물을 그린다. 열린 두 방과 이동 중인 플레이어는 그대로 보인다.
internal sealed class WaveTransitBackdrop
{
    private readonly GameObject root;
    private readonly Mesh mesh;
    private readonly List<Vector3> vertices = new();
    private readonly List<Color> colors = new();
    private readonly List<int> triangles = new();

    public WaveTransitBackdrop(Transform parent, Material material)
    {
        root = new GameObject("WaveTransitBackdrop", typeof(MeshFilter), typeof(MeshRenderer));
        root.SetActive(false);
        root.transform.SetParent(parent, false);
        mesh = new Mesh { name = "WaveTransitBackdrop", hideFlags = HideFlags.DontSave };
        mesh.MarkDynamic();
        root.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = root.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = -2;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    public static bool Separated(Rect from, Rect to)
        => Mathf.Max(from.xMin, to.xMin) - Mathf.Min(from.xMax, to.xMax) > .25f
            || Mathf.Max(from.yMin, to.yMin) - Mathf.Min(from.yMax, to.yMax) > .25f;

    public void Show(Rect from, Rect to)
    {
        // 카메라가 비스듬한 이동 경로나 넓은 비율을 찍어도 외벽 뒤의 공간이 드러나지 않게 한다.
        // 방 공유 벽에는 실내 그림이 겹치지 않도록 두 구멍을 원래 크기 그대로 남긴다.
        const float margin = 28f;
        float[] columns = { Mathf.Min(from.xMin, to.xMin) - margin, from.xMin, from.xMax,
            to.xMin, to.xMax, Mathf.Max(from.xMax, to.xMax) + margin };
        float[] rows = { Mathf.Min(from.yMin, to.yMin) - margin, from.yMin, from.yMax,
            to.yMin, to.yMax, Mathf.Max(from.yMax, to.yMax) + margin };
        System.Array.Sort(columns);
        System.Array.Sort(rows);
        vertices.Clear();
        colors.Clear();
        triangles.Clear();
        bool travelSideways = Mathf.Abs(from.center.x - to.center.x) >= Mathf.Abs(from.center.y - to.center.y);
        var graphite = new Color(.012f, .018f, .027f, 1f);
        var shadow = new Color(.020f, .031f, .043f, 1f);
        var ribs = new Color(.039f, .059f, .075f, 1f);
        var lip = new Color(.060f, .078f, .090f, 1f);
        for (int y = 1; y < rows.Length; y++)
            for (int x = 1; x < columns.Length; x++)
            {
                var panel = Rect.MinMaxRect(columns[x - 1], rows[y - 1], columns[x], rows[y]);
                if (panel.width <= .005f || panel.height <= .005f
                    || from.Contains(panel.center) || to.Contains(panel.center)) continue;
                Quad(panel, graphite, shadow);
                AddRibs(panel, travelSideways, ribs, lip);
            }
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        root.SetActive(true);
    }

    private void AddRibs(Rect panel, bool vertical, Color ribs, Color lip)
    {
        const float step = 5f, thick = .28f;
        float min = vertical ? panel.xMin : panel.yMin;
        float max = vertical ? panel.xMax : panel.yMax;
        for (float p = Mathf.Ceil(min / step) * step; p + thick <= max; p += step)
        {
            if (vertical)
            {
                Quad(new Rect(p, panel.yMin, thick, panel.height), ribs, ribs);
                Quad(new Rect(p, panel.yMin, .043f, panel.height), lip, lip);
            }
            else
            {
                Quad(new Rect(panel.xMin, p, panel.width, thick), ribs, ribs);
                Quad(new Rect(panel.xMin, p + thick - .043f, panel.width, .043f), lip, lip);
            }
        }
    }

    private void Quad(Rect panel, Color bottom, Color top)
    {
        int index = vertices.Count;
        vertices.Add(new Vector3(panel.xMin, panel.yMin));
        vertices.Add(new Vector3(panel.xMin, panel.yMax));
        vertices.Add(new Vector3(panel.xMax, panel.yMax));
        vertices.Add(new Vector3(panel.xMax, panel.yMin));
        colors.Add(bottom);
        colors.Add(top);
        colors.Add(top);
        colors.Add(bottom);
        triangles.Add(index);
        triangles.Add(index + 1);
        triangles.Add(index + 2);
        triangles.Add(index);
        triangles.Add(index + 2);
        triangles.Add(index + 3);
    }

    public void Hide()
    {
        if (root.activeSelf) root.SetActive(false);
    }

    public void Release()
    {
        Object.Destroy(mesh);
        Object.Destroy(root);
    }
}
