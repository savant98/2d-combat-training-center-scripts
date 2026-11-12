using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;

// Comprehensive의 기능 모듈 전체를 복제해 Animator 경로·제작 흐름·케이블 참조를 함께 보존한다.
// 한 번 만든 모듈은 없애지 않는다. 지금 공간에 필요 없으면 꺼 두었다가 다시 필요해지면 켠다(모듈 하나가 오브젝트 수백 개라 새로 만드는 비용이 크다).
// TrainingRoom 입장 로딩(Prewarm)이 모든 Wave의 공간에 필요한 모듈을 미리 만들어 두므로, Wave 도중에는 켜고 끄기만 한다.
public sealed class WaveRoomBackdrop : MonoBehaviour
{
    private sealed class Module
    {
        public Transform root;
        public Transform depth;
        public bool shown;
        public Layer layer;
        public Vector2Int cell;
    }
    private sealed class Layer
    {
        public string name;
        public Transform root;
        public Transform original;
        public Vector3 home;
        public float parallax;
        public Vector2 repeat;
        // 지금까지 만든 모듈(꺼 둔 것 포함).
        public readonly Dictionary<Vector2Int, Module> modules = new();
        // 지금 공간에 필요한 모듈 번호. 아직 켜지 않은(또는 만들지 않은) 것은 화면에 걸릴 때 바로, 그 밖에는 프레임마다 조금씩 켠다.
        public readonly HashSet<Vector2Int> wanted = new();
        // 필요하지만 아직 켜지 않은 모듈 수.
        public int pending;
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
    // 원본 + 지금 공간에 켜 둔 반복 모듈 수.
    public int ModuleCount => 1 + layers.Sum(l => l.modules.Values.Count(m => m.shown));
    // 원본 + 지금까지 만들어 둔 반복 모듈 수(꺼 둔 것 포함).
    public int CachedModuleCount => 1 + layers.Sum(l => l.modules.Count);
    // 필요 없어진 모듈은 한꺼번에 끄지 않고 프레임마다 조금씩 끈다.
    private readonly Queue<Module> retiring = new();
    private const int ShowPerFrame = 3, RetirePerFrame = 4;

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
    private Module CreateModule(Layer layer, Vector2Int cell, bool shown)
    {
        // 원본(template)이 꺼져 있으므로 복제본도 꺼진 채 만들어진다. 아래 설정을 마친 뒤 필요할 때만 켠다.
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
        if (shown) copy.gameObject.SetActive(true);
        return new Module { root = copy, depth = depth, shown = shown, layer = layer, cell = cell };
    }
    // 이 공간에서 카메라가 양 끝에 갔을 때 화면에 걸리는 모듈 번호를 모은다.
    private void CollectWanted(Layer layer, Rect bounds, HashSet<Vector2Int> wanted)
    {
        // 원본 시차만큼 카메라를 따라가므로 잔여 이동량과 화면 여유만 반복하면 된다.
        float rest = 1 - layer.parallax;
        int left = Mathf.Min(0, Mathf.FloorToInt(((bounds.xMin - origin.x) * rest - viewportExtent.x) / layer.repeat.x + .5f));
        int right = Mathf.Max(0, Mathf.CeilToInt(((bounds.xMax - origin.x) * rest + viewportExtent.x) / layer.repeat.x - .5f));
        int bottom = Mathf.Min(0, Mathf.FloorToInt(((bounds.yMin - origin.y) * rest - viewportExtent.y) / layer.repeat.y + .5f));
        int top = Mathf.Max(0, Mathf.CeilToInt(((bounds.yMax - origin.y) * rest + viewportExtent.y) / layer.repeat.y - .5f));
        for (int x = left; x <= right; x++)
            for (int y = bottom; y <= top; y++)
            {
                var cell = new Vector2Int(x, y);
                if (cell != Vector2Int.zero) wanted.Add(cell);
            }
    }
    /// <summary>앞으로 쓰일 공간들에 필요한 모듈을 미리 만들어 꺼 둔다(TrainingRoom 입장 로딩). report는 0~1 진행률이다.</summary>
    public System.Collections.IEnumerator Prewarm(IReadOnlyList<Rect> spaces, System.Action<float> report, float frameBudget = .006f)
    {
        if (template == null || art == null) yield break;
        viewportExtent = Vector2.Max(viewportExtent, MeasureViewport());
        origin = artHome;
        var jobs = new List<(Layer layer, Vector2Int cell)>();
        var wanted = new HashSet<Vector2Int>();
        foreach (var layer in layers)
        {
            wanted.Clear();
            foreach (var space in spaces) CollectWanted(layer, space, wanted);
            foreach (var cell in wanted) if (!layer.modules.ContainsKey(cell)) jobs.Add((layer, cell));
        }
        float slice = Time.realtimeSinceStartup;
        for (int i = 0; i < jobs.Count; i++)
        {
            var (layer, cell) = jobs[i];
            // 로딩 도중 LateUpdate가 먼저 만든 모듈은 건너뛴다.
            if (!layer.modules.ContainsKey(cell))
            {
                var module = CreateModule(layer, cell, false);
                module.root.localPosition = origin + new Vector3(cell.x * layer.repeat.x, cell.y * layer.repeat.y);
                layer.modules.Add(cell, module);
            }
            report?.Invoke((i + 1f) / jobs.Count);
            if (Time.realtimeSinceStartup - slice < frameBudget) continue;
            yield return null;
            slice = Time.realtimeSinceStartup;
        }
        report?.Invoke(1f);
    }
    public void Resize(Rect bounds)
    {
        currentBounds = bounds;
        hasBounds = true;
        viewportExtent = Vector2.Max(viewportExtent, MeasureViewport());
        // 후경의 기준점은 원래 방에 고정한다. 방이 한쪽으로 넓어지거나 다른 곳으로 옮겨 가도 이미 보이던 후경은 제자리에 있고,
        // 새로 보이게 되는 쪽에만 반복 모듈이 더해진다(기준점을 방 가운데로 옮기면 넓어지는 순간 후경이 통째로 건너뛴다).
        origin = artHome;
        if (art != null) art.localPosition = origin;
        retiring.Clear();
        foreach (var layer in layers)
        {
            layer.wanted.Clear();
            CollectWanted(layer, bounds, layer.wanted);
            layer.pending = 0;
            foreach (var cell in layer.wanted)
            {
                if (!layer.modules.TryGetValue(cell, out var module)) { layer.pending++; continue; }
                module.root.localPosition = origin + new Vector3(cell.x * layer.repeat.x, cell.y * layer.repeat.y);
                if (!module.shown) layer.pending++;
            }
            // 이 공간에 필요 없는 모듈은 없애지 않고 꺼 둔다(다시 넓어질 때 그대로 켠다).
            foreach (var row in layer.modules)
                if (row.Value.shown && !layer.wanted.Contains(row.Key)) retiring.Enqueue(row.Value);
        }
        // 지금 화면에 걸리는 모듈은 바로 켠다(없으면 만든다). 나머지는 LateUpdate가 이어서 켠다.
        BuildModules(false);
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
    // 지금 공간에 필요한데 아직 켜지 않은 반복 모듈을 켠다. 화면에 걸리는 것은 모두 바로 켜고, 그 밖의 것은 프레임마다 조금씩 켠다.
    // 입장 로딩이 미리 만들어 두지 못한 모듈(예상하지 못한 공간)은 여기서 만든다. 모듈 하나가 설비 전체의 복제라 화면 밖의 것은 한 프레임에 하나만 만든다.
    private void BuildModules(bool allowOffscreen)
    {
        if (template == null) return;
        if (view == null) view = Camera.main;
        bool sighted = visible && view != null;
        Vector3 viewPoint = sighted ? transform.InverseTransformPoint(view.transform.position) : origin;
        Vector3 delta = viewPoint - origin; delta.z = 0;
        Vector2 extent = MeasureViewport();
        int shownOffscreen = 0; bool builtOffscreen = false;
        foreach (var layer in layers)
        {
            if (layer.pending <= 0) continue;
            Vector2 drift = (Vector2)delta * layer.parallax;
            foreach (var cell in layer.wanted)
            {
                layer.modules.TryGetValue(cell, out var module);
                if (module != null && module.shown) continue;
                Vector2 center = (Vector2)origin + new Vector2(cell.x * layer.repeat.x, cell.y * layer.repeat.y) + drift;
                bool onScreen = sighted && Mathf.Abs(center.x - viewPoint.x) < layer.repeat.x * .5f + extent.x && Mathf.Abs(center.y - viewPoint.y) < layer.repeat.y * .5f + extent.y;
                if (!onScreen)
                {
                    if (!allowOffscreen) continue;
                    if (module == null) { if (builtOffscreen || shownOffscreen > 0) continue; builtOffscreen = true; }
                    else { if (builtOffscreen || shownOffscreen >= ShowPerFrame) continue; shownOffscreen++; }
                }
                if (module == null)
                {
                    module = CreateModule(layer, cell, true);
                    layer.modules.Add(cell, module);
                }
                else
                {
                    module.root.gameObject.SetActive(true);
                    module.shown = true;
                }
                module.root.localPosition = origin + new Vector3(cell.x * layer.repeat.x, cell.y * layer.repeat.y);
                module.depth.localPosition = layer.home + delta * layer.parallax;
                layer.pending--;
            }
        }
    }
    // 필요 없어진 모듈을 조금씩 끈다. 그사이 다시 필요해진 모듈은 그대로 둔다.
    private void RetireModules()
    {
        for (int i = 0; i < RetirePerFrame && retiring.Count > 0; i++)
        {
            var module = retiring.Dequeue();
            if (module.root == null || !module.shown) continue;
            if (module.layer.wanted.Contains(module.cell)) continue;
            module.root.gameObject.SetActive(false);
            module.shown = false;
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
        // 배경을 숨긴 동안에도 모듈은 마저 켜 둔다(다시 보일 때 비어 있지 않게).
        BuildModules(true);
        RetireModules();
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
            foreach (var module in layer.modules.Values) if (module.shown) module.depth.localPosition = layer.home + delta * layer.parallax;
        }
    }
    private void OnDestroy() { foreach (var mesh in meshes) if (mesh != null) Destroy(mesh); }
}
