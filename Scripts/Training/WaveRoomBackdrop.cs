using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

// 기존 설비의 고정 간격을 양 축으로 반복한다. 배경 복제에는 Collider를 추가하지 않는다.
public sealed class WaveRoomBackdrop : MonoBehaviour
{
    private sealed class Layer
    {
        public Transform root;
        public float parallax;
        public Vector3 home;
        public readonly List<Transform> sources = new();
        public Vector2 repeat;
    }
    private readonly List<Layer> layers = new();
    private readonly List<GameObject> copies = new();
    private Transform window;
    private readonly List<Mesh> meshes = new();
    private readonly List<Vector3[]> vertices = new();
    private Camera view;
    private Vector2 center;

    public void Configure(Transform content)
    {
        var art = content.Find("TrainingBackdrop");
        if (art == null) return;
        foreach (var constraint in art.GetComponentsInChildren<PositionConstraint>(true)) constraint.enabled = false;
        var old = art.GetComponent<LobbyBackdropParallax>();
        if (old != null) old.enabled = false;
        AddLayer(art, "FarStructure", .06f, new Vector2(60, 34), new[] { "DistantArchitecture", "FarSignal0", "FarSignal1", "FarSignal2", "FarSignal3", "FarSignal4", "FarSignal5", "FarSignal6", "FarSignal7", "FarSignal8" });
        AddLayer(art, "TrainingChambers", .12f, new Vector2(24, 17.5f), new[] { "TrainingBay1Prop", "TrainingBay2Prop", "TrainingBay3Prop", "TrainingBay4Prop" });
        AddLayer(art, "RecoveryMachinery", .2f, new Vector2(32, 17.5f), new[] { "WestRecoveryCraneProp" });
        art.gameObject.SetActive(false);
        var east = content.Find("TrainingBackdrop_East");
        if (east != null) east.gameObject.SetActive(false);
        window = content.Find("Furniture/TrainingObservationWindow");
        if (window == null) return;
        window.localPosition = new Vector3(0, 0, 1);
        foreach (var filter in window.GetComponentsInChildren<MeshFilter>(true))
        {
            var copy = Instantiate(filter.sharedMesh);
            filter.sharedMesh = copy;
            meshes.Add(copy);
            vertices.Add(copy.vertices);
        }
    }

    private void AddLayer(Transform art, string name, float factor, Vector2 repeat, string[] names)
    {
        var source = art.Find(name);
        if (source == null) return;
        var node = new GameObject("Repeating" + name).transform;
        node.SetParent(transform, false);
        var layer = new Layer { root = node, parallax = factor, repeat = repeat, home = new Vector3(0, 0, source.position.z - transform.position.z) };
        foreach (string child in names)
        {
            var part = source.Find(child);
            if (part != null) layer.sources.Add(part);
        }
        layers.Add(layer);
    }

    public void Resize(Rect bounds)
    {
        center = bounds.center;
        foreach (var item in copies) if (item != null) Destroy(item);
        copies.Clear();
        foreach (var layer in layers)
        {
            layer.home.x = center.x;
            layer.home.y = center.y;
            layer.root.localPosition = layer.home;
            int width = Mathf.CeilToInt((bounds.width * .5f + 20) / layer.repeat.x);
            int height = Mathf.CeilToInt((bounds.height * .5f + 20) / layer.repeat.y);
            for (int x = -width; x <= width; x++)
                for (int y = -height; y <= height; y++)
                    foreach (var source in layer.sources)
                    {
                        var clone = Instantiate(source.gameObject, layer.root);
                        clone.transform.localPosition = source.localPosition + new Vector3(x * layer.repeat.x, y * layer.repeat.y, 0);
                        clone.SetActive(true);
                        copies.Add(clone);
                    }
        }
        if (window == null) return;
        window.localPosition = new Vector3(center.x, bounds.yMin, 1);
        for (int m = 0; m < meshes.Count; m++)
        {
            var points = (Vector3[])vertices[m].Clone();
            for (int i = 0; i < points.Length; i++)
            {
                points[i].x += Mathf.Sign(points[i].x) * (bounds.width * .5f - 13);
                if (points[i].y > 8.46f) points[i].y += bounds.height - 17;
            }
            meshes[m].vertices = points;
            meshes[m].RecalculateBounds();
        }
        var cable = window.GetComponentInChildren<LineRenderer>();
        if (cable != null)
        {
            cable.positionCount = 4;
            cable.SetPositions(new[] { new Vector3(-bounds.width / 2, 0), new Vector3(bounds.width / 2, 0),
                new Vector3(bounds.width / 2, bounds.height), new Vector3(-bounds.width / 2, bounds.height) });
        }
    }

    private void LateUpdate()
    {
        if (view == null) view = Camera.main;
        if (view == null) return;
        Vector3 delta = transform.InverseTransformPoint(view.transform.position) - (Vector3)center;
        delta.z = 0;
        foreach (var layer in layers) layer.root.localPosition = layer.home + delta * layer.parallax;
    }
    private void OnDestroy() { foreach (var mesh in meshes) if (mesh != null) Destroy(mesh); }
}
