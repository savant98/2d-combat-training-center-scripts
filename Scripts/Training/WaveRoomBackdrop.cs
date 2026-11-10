using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;

// Comprehensive의 기능 모듈 전체를 복제해 Animator 경로·제작 흐름·케이블 참조를 함께 보존한다.
public sealed class WaveRoomBackdrop : MonoBehaviour
{
    private sealed class Module
    {
        public Transform root;
        public Transform depth;
    }
    private sealed class Layer
    {
        public string name;
        public Transform root;
        public Transform original;
        public Vector3 home;
        public float parallax;
        public Vector2 repeat;
        public readonly Dictionary<Vector2Int, Module> modules = new();
    }
    private readonly List<Layer> layers = new();
    private Transform art;
    private Transform template;
    private Vector3 artHome;
    private Vector3 origin;
    private Rect sourceBounds = new Rect(-4, -3, 26, 17);
    private Transform window;
    private readonly List<Mesh> meshes = new();
    private readonly List<Vector3[]> vertices = new();
    private Camera view;
    private bool visible = true;
    private Rect currentBounds;
    private bool hasBounds;
    private Vector2 viewportExtent = new Vector2(12, 8);
    public int ModuleCount => 1 + layers.Sum(l => l.modules.Count);

    public void Configure(Transform content) => Configure(content, sourceBounds);
    public void Configure(Transform content, Rect originalBounds)
    {
        sourceBounds = originalBounds;
        art = content.Find("TrainingBackdrop");
        if (art == null) return;
        artHome = art.localPosition;
        var old = art.GetComponent<LobbyBackdropParallax>();
        if (old != null) old.enabled = false;
        AddLayer("FarStructure", .92f, new Vector2(60, 34));
        AddLayer("TrainingChambers", .68f, new Vector2(24, 17.5f));
        AddLayer("RecoveryMachinery", .4f, new Vector2(32, 17.5f));
        foreach (var constraint in art.GetComponentsInChildren<PositionConstraint>(true)) constraint.enabled = false;
        foreach (var layer in layers) HideExtensions(layer.original);
        // 실행 중 변형된 제작물 자세·임시 Material을 다음 복제의 원본으로 쓰지 않는다.
        var staging = new GameObject("WaveBackdropTemplate");
        staging.transform.SetParent(transform, false);
        staging.SetActive(false);
        template = Instantiate(art, staging.transform, false);
        template.gameObject.SetActive(false);
        var east = content.Find("TrainingBackdrop_East");
        if (east != null) east.gameObject.SetActive(false);
        window = content.Find("Furniture/TrainingObservationWindow");
        if (window == null) return;
        foreach (var filter in window.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            var copy = Instantiate(filter.sharedMesh);
            filter.sharedMesh = copy;
            meshes.Add(copy);
            vertices.Add(copy.vertices);
        }
    }
    private void AddLayer(string name, float factor, Vector2 repeat)
    {
        var source = art.Find(name);
        if (source == null) return;
        var constraint = source.GetComponent<PositionConstraint>();
        if (constraint != null && constraint.sourceCount > 0) factor = constraint.GetSource(0).weight;
        var root = new GameObject("Repeating" + name).transform;
        root.SetParent(transform, false);
        layers.Add(new Layer { name = name, root = root, original = source, home = source.localPosition, parallax = factor, repeat = repeat });
    }
    private static void HideExtensions(Transform depth)
    {
        // 일부 부스만 있는 확장 사본은 제작·이송 설비까지 포함한 완전한 모듈로 대체한다.
        foreach (string name in new[] { "UpperTierBays", "UpperTierBays2", "EastTierBays", "EastRecoveryCranes" })
        {
            var extra = depth.Find(name);
            if (extra != null) extra.gameObject.SetActive(false);
        }
    }
    private Vector2 MeasureViewport()
    {
        if (view == null) view = Camera.main;
        if (view == null || !view.orthographic) return viewportExtent;
        var right = transform.InverseTransformVector(view.transform.right * view.orthographicSize * view.aspect);
        var up = transform.InverseTransformVector(view.transform.up * view.orthographicSize);
        return new Vector2(Mathf.Abs(right.x) + Mathf.Abs(up.x) + 1, Mathf.Abs(right.y) + Mathf.Abs(up.y) + 1);
    }
    private Module CreateModule(Layer layer, Vector2Int cell)
    {
        var copy = Instantiate(template, layer.root, false);
        copy.name = "TrainingBackdrop_" + cell.x + "_" + cell.y;
        foreach (var item in layers)
            if (item.name != layer.name) copy.Find(item.name).gameObject.SetActive(false);
        var depth = copy.Find(layer.name);
        HideExtensions(depth);
        if (cell.y != 0)
            foreach (string name in new[] { "MiddleFloor", "TrainingCeiling", "RecoveryFloor", "RecoveryCeiling" })
            {
                // 큰 외곽판을 다른 층의 설비 앞에 끼워 넣지 않는다. 부스 자체의 지지부는 유지한다.
                var panel = depth.Find(name);
                if (panel != null) panel.gameObject.SetActive(false);
            }
        var flow = copy.GetComponent<TrainingFabricationFlow>();
        if (flow != null && layer.name != "TrainingChambers") flow.enabled = false;
        if (layer.name == "FarStructure")
        {
            var animator = copy.GetComponent<Animator>();
            if (animator != null) animator.enabled = false;
            var scroll = copy.GetComponent<LobbyCableScroll>();
            if (scroll != null) scroll.enabled = false;
        }
        copy.gameObject.SetActive(true);
        return new Module { root = copy, depth = depth };
    }
    public void Resize(Rect bounds)
    {
        currentBounds = bounds;
        hasBounds = true;
        viewportExtent = Vector2.Max(viewportExtent, MeasureViewport());
        origin = artHome + (Vector3)(bounds.center - sourceBounds.center);
        if (art != null) art.localPosition = origin;
        foreach (var layer in layers)
        {
            // 원본 시차만큼 카메라를 따라가므로 잔여 이동량과 화면 여유만 반복하면 된다.
            int width = Mathf.Max(0, Mathf.CeilToInt((bounds.width * .5f * (1 - layer.parallax) + viewportExtent.x - layer.repeat.x * .5f) / layer.repeat.x));
            int height = Mathf.Max(0, Mathf.CeilToInt((bounds.height * .5f * (1 - layer.parallax) + viewportExtent.y - layer.repeat.y * .5f) / layer.repeat.y));
            var wanted = new HashSet<Vector2Int>();
            for (int x = -width; x <= width; x++)
                for (int y = -height; y <= height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (cell == Vector2Int.zero) continue;
                    wanted.Add(cell);
                    if (!layer.modules.TryGetValue(cell, out var module)) layer.modules.Add(cell, module = CreateModule(layer, cell));
                    module.root.localPosition = origin + new Vector3(x * layer.repeat.x, y * layer.repeat.y);
                }
            foreach (var cell in layer.modules.Keys.Where(c => !wanted.Contains(c)).ToArray())
            {
                layer.modules[cell].root.gameObject.SetActive(false);
                Destroy(layer.modules[cell].root.gameObject);
                layer.modules.Remove(cell);
            }
        }
        if (window == null) return;
        window.localPosition = new Vector3(bounds.center.x, bounds.yMin, 1);
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
    public void SetBackgroundVisible(bool value)
    {
        visible = value;
        if (art != null) art.gameObject.SetActive(visible);
        foreach (var layer in layers) layer.root.gameObject.SetActive(visible);
    }
    private void LateUpdate()
    {
        if (!visible) return;
        if (view == null) view = Camera.main;
        if (view == null) return;
        var required = MeasureViewport();
        if (hasBounds && (required.x > viewportExtent.x + .25f || required.y > viewportExtent.y + .25f)) Resize(currentBounds);
        Vector3 delta = transform.InverseTransformPoint(view.transform.position) - origin;
        delta.z = 0;
        foreach (var layer in layers)
        {
            layer.original.localPosition = layer.home + delta * layer.parallax;
            foreach (var module in layer.modules.Values) module.depth.localPosition = layer.home + delta * layer.parallax;
        }
    }
    private void OnDestroy() { foreach (var mesh in meshes) if (mesh != null) Destroy(mesh); }
}
