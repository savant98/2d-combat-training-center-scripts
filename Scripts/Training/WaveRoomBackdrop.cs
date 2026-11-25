using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;

// Comprehensive의 기능 모듈 전체를 복제해 Animator 경로·제작 흐름·케이블 참조를 함께 보존한다.
// 한 번 만든 모듈은 없애지 않는다. 지금 공간에 필요 없으면 꺼 두었다가 다시 필요해지면 켠다(모듈 하나가 오브젝트 수백 개라 새로 만드는 비용이 크다).
// TrainingRoom 입장 로딩(Prewarm)이 모든 Wave의 공간에 필요한 모듈을 미리 만들어 두므로, Wave 도중에는 켜고 끄기만 한다.
// Brain(실행 순서 100)과 최종 경계·픽셀 보정이 끝난 같은 프레임의 카메라를 읽는다.
[DefaultExecutionOrder(200)]
public sealed class WaveRoomBackdrop : MonoBehaviour
{
    private sealed class Module
    {
        public Transform root;
        public Transform depth;
        public bool shown;
        public Layer layer;
        public Vector2Int cell;
        // 화면에서 멀 때 멈추는 대상. 이 모듈에서 실제로 켜 둔 것만 담는다(FarStructure처럼 처음부터 끈 것은 null).
        public Animator animator;
        public AnimatorCullingMode cullingMode;
        public TrainingFabricationFlow flow;
        public LobbyCableScroll scroll;
        // false면 화면에서 멀어 Animator·제작 흐름·케이블 흐름을 멈춰 둔 상태다.
        public bool awake = true;
        // 마지막으로 켠 프레임. Renderer 범위는 켠 다음 프레임부터 잰다.
        public int shownFrame;
        // 이 모듈로 Renderer 범위를 재 보았는지(그릴 것이 없어 실패했어도 다시 재지 않는다).
        public bool footprintTried;
        // 던전 정적 벽면 방(SetSuppressed) 동안 그리지 않게 해 둔 모듈인지와, 처음 감출 때 한 번 모은 Renderer 목록.
        public bool suppressed;
        public Renderer[] renderers;
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
        // 모듈 Renderer가 차지하는 범위(depth 원점 기준, 이 컴포넌트의 로컬 좌표). [0]은 cell.y == 0 모듈, [1]은 외곽판을 끈 그 밖의 모듈. 재기 전에는 null이다.
        public readonly Rect?[] footprints = new Rect?[2];
    }
    private readonly List<Layer> layers = new();
    private Transform art;
    private Transform template;
    private Vector3 artHome;
    private Vector3 origin;
    private Rect sourceBounds = new Rect(-4, -3, 26, 17);
    private Transform window;
    // 통창을 숨긴 동안 Resize가 둘레 케이블을 맞추지 못했는지(다시 켜질 때 맞춘다).
    private bool windowCablePending;
    private readonly List<Mesh> meshes = new();
    private readonly List<Vector3[]> vertices = new();
    private Camera view;
    private bool visible = true;
    // 던전 정적 벽면 방(WaveRoomBackground.ApplyRoom) 동안 시차 배경을 그리지 않는다. SetBackgroundVisible(visible)과는 별개 플래그다.
    // renderer.enabled는 WaveRoomBackground.originalRenderers가 저장·복원하므로 쓰지 않고 forceRenderingOff로만 감춘다. 감추며 바꾼 Renderer만 기억해 되돌린다.
    private bool suppressed;
    private readonly List<Renderer> forcedOff = new();
    private Rect currentBounds;
    private bool hasBounds;
    private Vector2 viewportExtent = new Vector2(12, 8);
    // 원본 + 지금 공간에 켜 둔 반복 모듈 수.
    public int ModuleCount => 1 + layers.Sum(l => l.modules.Values.Count(m => m.shown));
    // 원본 + 지금까지 만들어 둔 반복 모듈 수(꺼 둔 것 포함).
    public int CachedModuleCount => 1 + layers.Sum(l => l.modules.Count);
    // 켜 둔 반복 모듈 중 화면 근처라 Animator·제작 흐름·케이블 흐름이 도는 모듈 수(나머지는 화면에서 멀어 멈춰 둔 것, 검증용).
    public int AwakeModuleCount => layers.Sum(l => l.modules.Values.Count(m => m.shown && m.awake));
    /// <summary>던전 정적 벽면 방 때문에 시차 배경을 감춰 둔 상태인지(SetSuppressed).</summary>
    public bool Suppressed => suppressed;
    // 필요 없어진 모듈은 한꺼번에 끄지 않고 프레임마다 조금씩 끈다.
    private readonly Queue<Module> retiring = new();
    private const int ShowPerFrame = 3, RetirePerFrame = 4;
    // 화면 밖 모듈을 가릴 때 잰 Renderer 범위에 더하는 여유(칸). MeasureViewport가 이미 1칸을 더하므로 화면 가장자리에서 5칸 이상 떨어져야 먼 것으로 본다.
    // 정지 자세로 잰 범위 밖으로 나가는 움직임(제작 흐름 크레인이 제품을 depth 기준 y -6까지 내리는 것 등 약 2칸)보다 넉넉하게 잡는다.
    private const float WakeMargin = 4f;
    private static readonly List<Renderer> footprintRenderers = new();

    public void Configure(Transform content) => Configure(content, sourceBounds);
    public void Configure(Transform content, Rect originalBounds)
    {
        sourceBounds = originalBounds;
        art = content.Find("TrainingBackdrop");
        if (art == null) return;
        artHome = art.localPosition;
        var old = art.GetComponent<LobbyBackdropParallax>();
        if (old != null) old.enabled = false;
        var pattern = art.GetComponent<WaveBackdropPattern>();
        AddLayer("FarStructure", pattern != null ? pattern.FarFollow : .92f, pattern != null ? pattern.Repeat : new Vector2(60, 34));
        AddLayer("TrainingChambers", pattern != null ? pattern.FrameFollow : .68f, pattern != null ? pattern.Repeat : new Vector2(24, 17.5f));
        AddLayer("RecoveryMachinery", pattern != null ? pattern.EquipmentFollow : .4f, pattern != null ? pattern.Repeat : new Vector2(32, 17.5f));
        foreach (var constraint in art.GetComponentsInChildren<PositionConstraint>(true)) constraint.enabled = false;
        foreach (var layer in layers) HideExtensions(layer.original);
        // 실행 중 변형된 제작물 자세·임시 Material을 다음 복제의 원본으로 쓰지 않는다.
        var staging = new GameObject("WaveBackdropTemplate");
        staging.transform.SetParent(transform, false);
        staging.SetActive(false);
        template = Instantiate(art, staging.transform, false);
        template.gameObject.SetActive(false);
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
        var module = new Module { root = copy, depth = depth, shown = shown, layer = layer, cell = cell };
        // 화면에서 멀 때 멈출 대상은 이 모듈에서 실제로 도는 것만 기억한다(위에서 끈 것은 건드리지 않는다).
        var ambient = copy.GetComponent<Animator>();
        if (ambient != null && ambient.enabled) { module.animator = ambient; module.cullingMode = ambient.cullingMode; }
        if (flow != null && flow.enabled) module.flow = flow;
        var cables = depth.GetComponent<LobbyCableScroll>();
        if (cables == null) cables = copy.GetComponent<LobbyCableScroll>();
        if (cables != null && cables.enabled) module.scroll = cables;
        if (shown) { module.shownFrame = Time.frameCount; copy.gameObject.SetActive(true); }
        return module;
    }
    // 이 공간에서 카메라가 양 끝에 갔을 때 화면에 걸리는 모듈 번호를 모은다.
    private void CollectWanted(Layer layer, Rect bounds, HashSet<Vector2Int> wanted)
    {
        // 화면에 남는 시차만큼만 반복한다. 맵 크기에 비례해 배경 복제 수를 늘리지 않는다.
        Vector2 min = Residual(layer, bounds.min - (Vector2)origin, viewportExtent);
        Vector2 max = Residual(layer, bounds.max - (Vector2)origin, viewportExtent);
        int left = Mathf.Min(0, Mathf.FloorToInt((min.x - viewportExtent.x) / layer.repeat.x + .5f));
        int right = Mathf.Max(0, Mathf.CeilToInt((max.x + viewportExtent.x) / layer.repeat.x - .5f));
        int bottom = Mathf.Min(0, Mathf.FloorToInt((min.y - viewportExtent.y) / layer.repeat.y + .5f));
        int top = Mathf.Max(0, Mathf.CeilToInt((max.y + viewportExtent.y) / layer.repeat.y - .5f));
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
            windowCablePending = false;
        }
        // 통창을 통째로 숨긴 동안(WaveDataSO.hideObservationWindow)에는 꺼진 케이블을 찾지 못한다. 위치와 Mesh는 위에서 이미 맞췄으므로
        // 케이블만 다시 켜질 때(RefreshWindow) 맞추도록 표시해 둔다. 방 내용이 아직 켜지기 전(staging)의 호출은 숨기지 않았을 때도 케이블을 건드리지 않으므로 표시하지 않는다.
        else if (!window.gameObject.activeSelf && window.parent != null && window.parent.gameObject.activeInHierarchy) windowCablePending = true;
    }
    /// <summary>숨겨 두었던 통창을 다시 켠 직후에 부른다. 숨긴 동안 건너뛴 둘레 케이블을 지금 공간에 맞춘다(건너뛴 적이 없으면 아무것도 하지 않는다).</summary>
    public void RefreshWindow()
    {
        if (window == null || !windowCablePending || !hasBounds) return;
        var cable = window.GetComponentInChildren<LineRenderer>();
        if (cable == null) return;
        cable.positionCount = 4;
        cable.SetPositions(new[] { new Vector3(-currentBounds.width / 2, 0), new Vector3(currentBounds.width / 2, 0),
            new Vector3(currentBounds.width / 2, currentBounds.height), new Vector3(-currentBounds.width / 2, currentBounds.height) });
        windowCablePending = false;
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
            Vector2 drift = Follow(layer, delta);
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
                    // 멈춰 둔 채 꺼졌던 모듈은 켜기 전에 깨운다. 켜지는 프레임부터 지금처럼 원래 Culling 설정의 Animator와 제작 흐름이 돈다.
                    SetAwake(module, true);
                    module.root.gameObject.SetActive(true);
                    module.shown = true;
                    module.shownFrame = Time.frameCount;
                }
                module.root.localPosition = origin + new Vector3(cell.x * layer.repeat.x, cell.y * layer.repeat.y);
                module.depth.localPosition = layer.home + (Vector3)Follow(layer, delta);
                // 감춘 동안 켜는 모듈도 그리지 않고 멈춰 둔다(벽면 방에서 Resize가 모듈을 켜도 화면에 나오지 않는다).
                if (suppressed) SuppressModule(module);
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
        // 다시 켜질 때 지금과 같은 상태로 시작하도록, 켜고 끄기 전에 멈춰 둔 모듈을 모두 깨운다.
        WakeAll();
        if (art != null) art.gameObject.SetActive(visible && !suppressed);
        foreach (var layer in layers) layer.root.gameObject.SetActive(visible);
    }
    /// <summary>
    /// 던전 정적 벽면 방(WaveRoomBackground.ApplyRoom)이 시차 배경을 감추고(true) 되돌린다(false).
    /// - 반복 모듈: 켜고 끄기(SetActive)하면 다시 켤 때 Animator 재바인딩·케이블 Material 재생성·제작 흐름 재시작이 생기므로, Renderer.forceRenderingOff + SetAwake(false)로
    ///   그리기와 움직임만 멈춘다(상태는 그대로 남아 돌아올 때 끊기지 않는다). 감춘 동안 새로 켜는 모듈에도 같은 처리를 한다(BuildModules).
    /// - 원본 art: SetActive(false). 잡기 연출 캡처(GrabCinematicBackdropView)가 이름으로 찾지 못해 Prefab 대체 경로를 탄다(forceRenderingOff Renderer는 복사에서 빠지므로).
    /// - renderer.enabled는 쓰지 않는다(WaveRoomBackground.originalRenderers가 저장·복원한다).
    /// - 되돌리면 감추며 바꾼 Renderer만 원래대로 돌리고, 깨우기는 다음 LateUpdate가 화면 거리로 정한다. 시차·모듈 생성 규칙은 그대로다.
    /// </summary>
    public void SetSuppressed(bool value)
    {
        if (suppressed == value) return;
        suppressed = value;
        if (art != null) art.gameObject.SetActive(visible && !suppressed);
        if (suppressed)
        {
            foreach (var layer in layers)
                foreach (var module in layer.modules.Values)
                    if (module.shown) SuppressModule(module);
            return;
        }
        foreach (var renderer in forcedOff) if (renderer != null) renderer.forceRenderingOff = false;
        forcedOff.Clear();
        foreach (var layer in layers)
            foreach (var module in layer.modules.Values) module.suppressed = false;
    }
    private void SuppressModule(Module module)
    {
        if (module.root == null) return;
        if (!module.suppressed)
        {
            module.suppressed = true;
            // 모듈 하나가 Renderer 수백 개라 처음 감출 때만 모은다(다른 훈련은 감추지 않으므로 이 비용이 없다).
            if (module.renderers == null) module.renderers = module.root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in module.renderers)
                if (renderer != null && !renderer.forceRenderingOff) { renderer.forceRenderingOff = true; forcedOff.Add(renderer); }
        }
        // 감춘 채 꺼졌다 다시 켜진 모듈은 BuildModules가 막 깨웠으므로 다시 멈춘다.
        SetAwake(module, false);
    }
    // 가까운 이동에는 원래 레이어 가중치를 적용한다. 멀리 이동해도 양축의 시차가 남도록
    // 일부는 선형으로 유지하고, 나머지는 반복 모듈의 수와 이음새 노출을 줄이도록 완만하게 제한한다.
    private Vector2 Residual(Layer layer, Vector2 delta) => Residual(layer, delta, MeasureViewport());
    private static Vector2 Residual(Layer layer, Vector2 delta, Vector2 extent)
    {
        float rest = Mathf.Clamp01(1f - layer.parallax);
        Vector2 travel = delta * rest;
        Vector2 limit = new Vector2(extent.x * (.1f + .5f * rest), extent.y * (.25f + .6f * rest));
        Vector2 limited = new Vector2(
            limit.x * (float)System.Math.Tanh(travel.x / limit.x),
            limit.y * (float)System.Math.Tanh(travel.y / limit.y));
        return Vector2.Lerp(limited, travel, .35f);
    }
    private Vector2 Follow(Layer layer, Vector2 delta) => delta - Residual(layer, delta);
    private static Vector2 Follow(Layer layer, Vector2 delta, Vector2 extent) => delta - Residual(layer, delta, extent);

    private void LateUpdate()
    {
        // 배경을 숨긴 동안에도 모듈은 마저 켜 둔다(다시 보일 때 비어 있지 않게).
        BuildModules(true);
        RetireModules();
        // 감춘 동안(던전 벽면 방)에는 숨긴 동안처럼 시차·깨우기를 건너뛴다(모듈 준비만 이어 간다).
        if (!visible || suppressed) return;
        if (view == null) view = Camera.main;
        if (view == null) return;
        var required = MeasureViewport();
        if (hasBounds && (required.x > viewportExtent.x + .25f || required.y > viewportExtent.y + .25f)) Resize(currentBounds);
        // 화면 범위는 Resize 뒤에 프레임마다 한 번만 잰다(같은 프레임에는 카메라가 그대로라 층·모듈마다 다시 잰 값과 같다).
        Vector2 extent = MeasureViewport();
        Vector3 viewPoint = transform.InverseTransformPoint(view.transform.position);
        Vector3 delta = viewPoint - origin;
        delta.z = 0;
        bool measured = false;
        foreach (var layer in layers)
        {
            // 한 층의 원본과 반복 모듈은 모두 같은 시차 위치를 쓰므로 층마다 한 번만 구한다.
            Vector3 depth = layer.home + (Vector3)Follow(layer, delta, extent);
            SetLocalPosition(layer.original, depth);
            foreach (var module in layer.modules.Values)
            {
                if (!module.shown) continue;
                // 올바른 위치가 화면(+여유) 근처인 모듈만 움직임을 유지한다. 깨어 있는 모듈은 1칸 더 멀어져야 멈춘다(경계에서 매 프레임 바뀌지 않게).
                bool near = IsNear(module, depth, viewPoint, extent, module.awake ? WakeMargin + 1f : WakeMargin);
                // 위치는 올바른 자리나 지금 놓인 자리 중 하나라도 화면 근처면 이번 프레임 값으로 맞춘다. 둘 다 멀면 화면에 보이지 않으므로 이전 자리에 둔다.
                // (시차 층은 카메라를 따라오지만 맞추지 않은 모듈은 제자리에 있으므로, 카메라가 다가가면 지금 놓인 자리가 먼저 화면에 들어올 수 있다.)
                Vector3 current = module.depth.localPosition;
                if ((near || IsNear(module, current, viewPoint, extent, WakeMargin))
                    && (current.x != depth.x || current.y != depth.y || current.z != depth.z))
                    module.depth.localPosition = depth;
                SetAwake(module, near);
                // 범위를 아직 모르는 종류는 켜진 모듈 하나로 잰다(한 프레임에 하나만).
                if (!measured && !module.footprintTried && !layer.footprints[Kind(module)].HasValue
                    && Time.frameCount > module.shownFrame && module.root.gameObject.activeInHierarchy)
                {
                    MeasureFootprint(module);
                    measured = true;
                }
            }
        }
    }
    // 같은 값이면 쓰지 않는다. Transform setter는 같은 값이어도 하위 Renderer 수백 개를 변경된 것으로 처리한다.
    private static void SetLocalPosition(Transform target, Vector3 value)
    {
        Vector3 current = target.localPosition;
        if (current.x != value.x || current.y != value.y || current.z != value.z) target.localPosition = value;
    }
    private static int Kind(Module module) => module.cell.y == 0 ? 0 : 1;
    // depth를 localPosition에 두었을 때 depth 원점의 이 컴포넌트 로컬 좌표(layer.root는 이 컴포넌트 원점에 그대로 있다).
    private static Vector2 DepthAnchor(Module module, Vector3 localPosition)
    {
        var root = module.root;
        return root.localPosition + root.localRotation * Vector3.Scale(root.localScale, localPosition);
    }
    // 모듈 Renderer 범위 + margin이 화면 범위(MeasureViewport, 1칸 여유 포함)와 겹치면 true.
    // 범위를 아직 재지 못한 종류의 모듈은 늘 true로 두어 지금과 똑같이 위치를 맞추고 움직이게 한다.
    private static bool IsNear(Module module, Vector3 depthPosition, Vector3 viewPoint, Vector2 extent, float margin)
    {
        var footprint = module.layer.footprints[Kind(module)];
        if (!footprint.HasValue) return true;
        Vector2 anchor = DepthAnchor(module, depthPosition);
        Rect area = footprint.Value;
        return anchor.x + area.xMax + margin > viewPoint.x - extent.x && anchor.x + area.xMin - margin < viewPoint.x + extent.x
            && anchor.y + area.yMax + margin > viewPoint.y - extent.y && anchor.y + area.yMin - margin < viewPoint.y + extent.y;
    }
    // 켜진 모듈 하나의 Renderer 범위를 depth 원점 기준으로 재 같은 종류(층, 외곽판 유무)의 모듈 전체에 쓴다.
    // 반복 모듈은 모두 같은 template의 복제이고 종류마다 같은 개체를 끄므로 구성이 같다. 꺼져 있던 제품·레이저와 움직이는 부품은 WakeMargin이 덮는다.
    private void MeasureFootprint(Module module)
    {
        module.footprintTried = true;
        module.depth.GetComponentsInChildren(false, footprintRenderers);
        Matrix4x4 toLocal = transform.worldToLocalMatrix;
        Vector2 anchor = DepthAnchor(module, module.depth.localPosition);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
        foreach (var item in footprintRenderers)
        {
            if (!item.enabled) continue;
            Bounds bounds = item.bounds;
            // 아직 그린 것이 없는 Renderer(입자 등)는 범위가 비어 있으므로 개체 위치로 대신한다.
            if (bounds.size == Vector3.zero) bounds = new Bounds(item.transform.position, Vector3.zero);
            Vector3 low = bounds.min, high = bounds.max;
            for (int i = 0; i < 4; i++)
            {
                Vector2 point = toLocal.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? low.x : high.x, (i & 2) == 0 ? low.y : high.y, bounds.center.z));
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }
        footprintRenderers.Clear();
        if (min.x > max.x) return;
        module.layer.footprints[Kind(module)] = Rect.MinMaxRect(min.x - anchor.x, min.y - anchor.y, max.x - anchor.x, max.y - anchor.y);
    }
    // 화면에서 먼 모듈의 Animator·제작 흐름·케이블 흐름을 멈추고, 가까워지면 멈춘 자리에서 이어 간다(처음부터 다시 시작하지 않아 튀지 않는다).
    // Animator Clip(TrainingBackdrop_Ambient_Loop)은 이벤트·전이·파라미터가 없는 8초 반복이라 멈췄다 이어 가도 놓치는 것이 없다.
    // CullCompletely는 Unity가 Renderer가 보인다고 판단하면 그대로 돌리므로, 먼 동안에도 보이는 경우에는 멈추지 않는다.
    private static void SetAwake(Module module, bool awake)
    {
        if (module.awake == awake) return;
        module.awake = awake;
        if (module.animator != null) module.animator.cullingMode = awake ? module.cullingMode : AnimatorCullingMode.CullCompletely;
        if (module.flow != null) module.flow.Paused = !awake;
        if (module.scroll != null) module.scroll.Paused = !awake;
    }
    private void WakeAll()
    {
        foreach (var layer in layers)
            foreach (var module in layer.modules.Values) SetAwake(module, true);
    }
    // 방이 꺼졌다 다시 켜질 때도 원래 설정으로 시작하도록 깨워 둔다.
    private void OnDisable() => WakeAll();
    private void OnDestroy() { foreach (var mesh in meshes) if (mesh != null) Destroy(mesh); }
}
