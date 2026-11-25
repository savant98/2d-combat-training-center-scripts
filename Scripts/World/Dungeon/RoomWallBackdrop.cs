using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 던전 정적 벽면 키트(배관 속·환풍 덕트·전선 트렁크·설비 벽면). WaveRoomBackground.ApplyRoom이 방 연결 공간(실내)에 맞춘다(Fit).
/// - 조각(Piece): 생성 도구(tools/Unity/DungeonWallKitSetup.cs)가 만든 정점색 Mesh. 늘이지 않고 축마다 반복(Tile)·단계(Steps)·끝(Min/Max)·가운데(Mid)로 놓은 뒤
///   재질·정렬 순서가 같은 것끼리 Mesh 한 장으로 합친다(정렬 순서마다 Renderer 하나). 합친 결과는 방 크기마다 캐시해 같은 방에 다시 들어오면 Mesh만 바꿔 낀다.
/// - 케이블(Cable): 발광 신호선은 LineRenderer로 길이에 맞춰 그리고, 후경 케이블과 같은 규칙(Tile (10, 0)·_ScrollOffset)으로 흐르게 한다.
/// - 설비(Feature): 팬·접속함·모니터처럼 움직이는 부품은 템플릿을 복제해 놓고 회전·이동·켜고 끄기만 한다(크기 변형 없음).
/// - 방향: AlongLongAxis면 세로로 긴 방에서 설계 좌표(u = 길이, v = 단면)를 90도 돌린다(배관이 세로로 선다).
/// - 맞춤 목표(Target): 실내 x ±1, 아래 -3(카메라 Confiner가 보는 범위, WaveRoomBackground.Fit과 같은 사각형) + margin.
/// - 이름 규칙: 이 컴포넌트가 만드는 개체와 키트 Prefab 이름에 'Backdrop'을 쓰지 않는다(WaveRoomBackground.Configure가 이름으로 원본 배경을 수집한다).
/// - 물리·판정 없음(Collider 없음). 위치를 옮기는 것은 이 키트의 장식 부품뿐이다.
/// </summary>
[DisallowMultipleComponent]
public sealed class RoomWallBackdrop : MonoBehaviour
{
    public enum Orientation
    {
        /// <summary>설계 좌표 그대로(u = 가로, v = 세로).</summary>
        Fixed = 0,
        /// <summary>실내가 세로로 길면 설계 좌표를 90도 돌려 u(길이)를 세로로 둔다.</summary>
        AlongLongAxis = 1,
    }

    public enum AxisMode
    {
        /// <summary>범위를 덮도록 period마다 반복한다(넘치는 조각은 화면 밖·지형 뒤).</summary>
        Tile = 0,
        /// <summary>범위(양 끝 inset 안쪽)를 period마다 채우고 마지막 조각을 끝에 맞춘다(겹침 허용, 넘침 없음).</summary>
        Steps = 1,
        /// <summary>범위 시작 + offset에 하나.</summary>
        Min = 2,
        /// <summary>범위 끝 - offset에 하나(mirror면 이 축으로 뒤집어 안쪽을 향한다).</summary>
        Max = 3,
        /// <summary>범위 가운데 + offset에 하나.</summary>
        Mid = 4,
    }

    public enum AxisRange
    {
        /// <summary>맞춤 목표(실내 + 카메라 여백 + margin).</summary>
        Target = 0,
        /// <summary>실내(연결 공간).</summary>
        Interior = 1,
    }

    [Serializable]
    public struct Axis
    {
        public AxisMode mode;
        public AxisRange range;
        [Tooltip("Tile·Steps 간격(칸).")] public float period;
        [Tooltip("기준점에서의 거리(칸). Tile은 반복의 시작 위치를 민다.")] public float offset;
        [Tooltip("Steps의 양 끝 여백(칸).")] public float inset;
        [Tooltip("Tile 반복을 범위 가운데에서 시작한다(좌우 대칭).")] public bool centered;
        [Tooltip("Max일 때 이 축으로 뒤집는다(바닥용 조각을 천장에 거꾸로 쓰는 등).")] public bool mirror;

        public static Axis Tile(float period, AxisRange range = AxisRange.Target, bool centered = false, float offset = 0f)
            => new Axis { mode = AxisMode.Tile, range = range, period = period, centered = centered, offset = offset };
        public static Axis Steps(float period, float inset = 0f, AxisRange range = AxisRange.Interior)
            => new Axis { mode = AxisMode.Steps, range = range, period = period, inset = inset };
        public static Axis Min(float offset = 0f, AxisRange range = AxisRange.Interior)
            => new Axis { mode = AxisMode.Min, range = range, offset = offset };
        public static Axis Max(float offset = 0f, bool mirror = true, AxisRange range = AxisRange.Interior)
            => new Axis { mode = AxisMode.Max, range = range, offset = offset, mirror = mirror };
        public static Axis Mid(float offset = 0f, AxisRange range = AxisRange.Interior)
            => new Axis { mode = AxisMode.Mid, range = range, offset = offset };
    }

    [Serializable]
    public struct Piece
    {
        public string name;
        [Tooltip("정점색 Mesh(Read/Write 가능). 기준점이 놓이는 자리 기준 좌표로 그린다.")] public Mesh mesh;
        public Material material;
        public int sortingOrder;
        [Tooltip("길이 방향 놓기.")] public Axis u;
        [Tooltip("단면 방향 놓기.")] public Axis v;
    }

    [Serializable]
    public struct Cable
    {
        public string name;
        [Tooltip("_ScrollOffset이 있으면 흐른다(실행 중 사본 하나를 이 케이블의 선들이 함께 쓴다).")] public Material material;
        public Color color;
        public float width;
        public int sortingOrder;
        [Tooltip("true면 길이(u) 방향으로 맞춤 목표 끝에서 끝까지, false면 단면(v) 방향으로 긋는다.")] public bool alongLength;
        [Tooltip("선을 놓는 자리(가로지르는 축). Tile이면 선이 여러 개가 된다.")] public Axis cross;
        [Tooltip("화면에서 가로로 놓인 선만 지지점 사이에서 아래로 처진다(칸).")] public float sag;
        [Tooltip("지지점 간격(칸). 0이면 처지지 않는다.")] public float supportSpacing;
        [Tooltip("지지점 반복의 시작(맞춤 목표 가운데 기준, 칸).")] public float supportOffset;
        [Tooltip("_ScrollOffset 초당 변화(부호 = 방향).")] public float scrollSpeed;
    }

    [Serializable]
    public struct Feature
    {
        public string name;
        [Tooltip("꺼 둔 템플릿(이 키트의 자식). 놓을 때 복제해 켠다.")] public GameObject template;
        public Axis u;
        public Axis v;
        [Tooltip("돌리는 자식 경로(비우면 없음).")] public string rotor;
        [Tooltip("초당 회전(도). 음수면 시계 방향.")] public float spin;
        [Tooltip("깜박이는 Renderer 자식 경로(비우면 없음).")] public string lamp;
        public float blinkPeriod;
        [Range(0f, 1f)] public float blinkOn;
        [Tooltip("왕복 없이 한 방향으로 미끄러지는 자식 경로(모니터 주사선 등, 비우면 없음).")] public string slider;
        public Vector2 slideFrom;
        public Vector2 slideTo;
        public float slidePeriod;
        [Tooltip("반복음(LobbyPropAmbience)의 자리를 카메라에 가장 가까운 이 설비로 옮긴다.")] public bool sound;
    }

    [SerializeField] private Orientation orientation = Orientation.Fixed;
    [SerializeField, Min(0f), Tooltip("카메라 가시 범위 밖에 더 덮는 여유(칸).")] private float margin = 1f;
    [SerializeField] private Piece[] pieces = Array.Empty<Piece>();
    [SerializeField] private Cable[] cables = Array.Empty<Cable>();
    [SerializeField] private Feature[] features = Array.Empty<Feature>();
    [Header("먼지 흐름(선택)")]
    [SerializeField] private ParticleSystem dust;
    [SerializeField, Min(0f), Tooltip("실내 1칸²당 초당 입자 수.")] private float dustPerArea = .04f;
    [SerializeField, Min(0f)] private float dustMaxRate = 32f;
    [SerializeField, Tooltip("길이 방향 흐름 속도(칸/초).")] private float dustSpeed = .45f;
    [Header("소리(선택)")]
    [SerializeField, Tooltip("LobbyPropAmbience의 anchor. 카메라에 가장 가까운 소리 설비(없으면 실내 안의 가장 가까운 점)로 옮긴다.")]
    private Transform soundAnchor;
    [Header("캐시")]
    [SerializeField, Min(1)] private int cacheLimit = 12;
    [SerializeField, Min(1000)] private int vertexBudget = 240000;

    private sealed class Bucket
    {
        public Material material;
        public int order;
        public MeshFilter filter;
        public MeshRenderer renderer;
    }

    private sealed class PieceData
    {
        public Vector3[] vertices;
        public Color[] colors;
        public int[] triangles;
        public Bounds bounds;
    }

    private sealed class Placed
    {
        public GameObject root;
        public Transform rotor, slider;
        public Renderer lamp;
        public Vector3 sliderHome;
        public float phase, phaseDegrees, phase01;
        public bool lampOn = true;
    }

    private static readonly int ScrollOffsetID = Shader.PropertyToID("_ScrollOffset");
    private const string LayerRootName = "WallLayers", CableRootName = "WallCables", PlacedRootName = "WallPlaced";

    private bool prepared;
    private readonly List<Bucket> buckets = new();
    private int[] pieceBucket = Array.Empty<int>();
    private PieceData[] pieceData = Array.Empty<PieceData>();
    private Transform layerRoot, cableRoot, placedRoot;
    private List<LineRenderer>[] cableLines = Array.Empty<List<LineRenderer>>();
    private Material[] cableMaterials = Array.Empty<Material>();
    private float[] cableOffsets = Array.Empty<float>();
    private List<Placed>[] placed = Array.Empty<List<Placed>>();
    private int[] placedActive = Array.Empty<int>();
    private readonly Dictionary<Rect, Mesh[]> cache = new();
    private readonly List<Rect> cacheOrder = new();
    private Rect fitted, pending;
    private bool hasFit, hasPending, turned, budgetWarned;
    private float clock;
    private static readonly List<float> uAnchors = new(), vAnchors = new(), crossAnchors = new();
    private static readonly List<Vector3> pathPoints = new();
    private static readonly List<float> supports = new();

    /// <summary>지금 맞춘 실내(연결 공간, 부모 로컬). HasFit이 false면 의미 없음.</summary>
    public Rect FittedInterior => fitted;
    public bool HasFit => hasFit;
    /// <summary>지금 맞춤에서 설계 좌표를 90도 돌렸는지.</summary>
    public bool Turned => turned;
    /// <summary>LateUpdate에서 다시 맞출 실내가 남아 있는지(SetRoomBounds가 RequestFit을 부른 뒤).</summary>
    public bool HasPendingFit => hasPending;
    public int CachedLayouts => cache.Count;
    public Orientation KitOrientation => orientation;
    public float Margin => margin;
    public IReadOnlyList<Piece> Pieces => pieces;
    public IReadOnlyList<Cable> Cables => cables;
    public IReadOnlyList<Feature> Features => features;
    public ParticleSystem Dust => dust;
    public Transform SoundAnchor => soundAnchor;

    /// <summary>합친 조각 Renderer(정렬 순서마다 하나). 맞춘 적이 없으면 비어 있다.</summary>
    public IEnumerable<MeshRenderer> LayerRenderers
    {
        get { foreach (var bucket in buckets) if (bucket.renderer != null) yield return bucket.renderer; }
    }

    /// <summary>지금 낀 합친 Mesh의 정점 수 합(검사용).</summary>
    public int VertexCount
    {
        get
        {
            int count = 0;
            foreach (var bucket in buckets) if (bucket.filter != null && bucket.filter.sharedMesh != null) count += bucket.filter.sharedMesh.vertexCount;
            return count;
        }
    }

    /// <summary>지금 켜 둔 설비 수(검사용).</summary>
    public int PlacedCount
    {
        get { int count = 0; foreach (int n in placedActive) count += n; return count; }
    }

    /// <summary>지금 켜 둔 케이블 선 수(검사용).</summary>
    public int CableLineCount
    {
        get
        {
            int count = 0;
            foreach (var list in cableLines) foreach (var line in list) if (line != null && line.gameObject.activeSelf) count++;
            return count;
        }
    }

    /// <summary>합친 조각 Mesh 범위의 합(이 키트 로컬, 검사용). 없으면 false.</summary>
    public bool TryGetLayerBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var bucket in buckets)
        {
            var mesh = bucket.filter != null ? bucket.filter.sharedMesh : null;
            if (mesh == null || mesh.vertexCount == 0) continue;
            if (!any) { bounds = mesh.bounds; any = true; }
            else bounds.Encapsulate(mesh.bounds);
        }
        return any;
    }

    /// <summary>실내(연결 공간)에서 덮을 사각형: 실내 x ±1, 아래 -3(카메라 가시 범위) + margin.</summary>
    public Rect TargetOf(Rect interior)
        => Rect.MinMaxRect(interior.xMin - 1f - margin, interior.yMin - 3f - margin, interior.xMax + 1f + margin, interior.yMax + margin);

    /// <summary>
    /// 메모리 키트·생성 도구가 정의를 한 번에 넣는다(Edit Mode 생성 도구는 이 값을 Prefab에 저장한다). 이미 맞춘 적이 있으면 실행 중 개체를 비우고 다시 시작한다.
    /// </summary>
    public void Define(Orientation kitOrientation, float kitMargin, Piece[] kitPieces, Cable[] kitCables, Feature[] kitFeatures,
        ParticleSystem kitDust = null, float perArea = .04f, float maxRate = 32f, float speed = .45f, Transform anchor = null)
    {
        if (prepared) ReleaseRuntime();
        orientation = kitOrientation;
        margin = Mathf.Max(0f, kitMargin);
        pieces = kitPieces ?? Array.Empty<Piece>();
        cables = kitCables ?? Array.Empty<Cable>();
        features = kitFeatures ?? Array.Empty<Feature>();
        dust = kitDust;
        dustPerArea = Mathf.Max(0f, perArea);
        dustMaxRate = Mathf.Max(0f, maxRate);
        dustSpeed = speed;
        soundAnchor = anchor;
    }

    /// <summary>실내에 바로 맞춘다(같은 실내면 아무것도 하지 않는다). 꺼진 키트에도 쓸 수 있다(켜기 전에 맞추면 첫 프레임부터 맞는 크기다).</summary>
    public void Fit(Rect interior) => Fit(interior, false);

    /// <summary>실내를 LateUpdate에서 맞추도록 미룬다. 같은 프레임 안에 키트가 꺼지면(다음 Wave 준비) 다시 맞추지 않는다.</summary>
    public void RequestFit(Rect interior)
    {
        if (!Valid(interior)) return;
        if (hasFit && interior == fitted) { hasPending = false; return; }
        pending = interior;
        hasPending = true;
    }

    /// <summary>미뤄 둔 맞춤을 지금 한다(Edit Mode 검사용). 한 것이 있으면 true.</summary>
    public bool FlushPendingFit()
    {
        if (!hasPending) return false;
        hasPending = false;
        Fit(pending, true);
        return true;
    }

    /// <summary>이 실내의 합친 Mesh를 미리 만들어 캐시에 둔다(입장 로딩용, 화면에는 바꾸지 않는다).</summary>
    public void Prewarm(Rect interior)
    {
        if (!Valid(interior)) return;
        Prepare();
        Layout(interior);
    }

    private static bool Valid(Rect r) => r.width > 0f && r.height > 0f && !float.IsNaN(r.x) && !float.IsInfinity(r.width) && !float.IsInfinity(r.height);

    private void Fit(Rect interior, bool restartDust)
    {
        if (!Valid(interior)) return;
        hasPending = false;
        if (hasFit && interior == fitted) return;
        Prepare();
        turned = Turn(interior);
        var meshes = Layout(interior);
        for (int i = 0; i < buckets.Count; i++)
            if (buckets[i].filter != null) buckets[i].filter.sharedMesh = meshes[i];
        PlaceCables(interior);
        PlaceFeatures(interior);
        PlaceDust(interior, restartDust);
        fitted = interior;
        hasFit = true;
    }

    private bool Turn(Rect interior) => orientation == Orientation.AlongLongAxis && interior.height > interior.width;

    // 설계 좌표(u, v) → 이 키트 로컬. 돌린 방향은 +90도(u가 위, v가 왼쪽).
    private static Vector3 ToLocal(Vector2 design, bool turn) => turn ? new Vector3(-design.y, design.x, 0f) : new Vector3(design.x, design.y, 0f);
    private static Rect ToDesign(Rect r, bool turn) => turn ? Rect.MinMaxRect(r.yMin, -r.xMax, r.yMax, -r.xMin) : r;

    // ───────── 준비 ─────────

    private void Prepare()
    {
        if (prepared) return;
        prepared = true;
        pieces ??= Array.Empty<Piece>();
        cables ??= Array.Empty<Cable>();
        features ??= Array.Empty<Feature>();
        layerRoot = Root(LayerRootName);
        cableRoot = Root(CableRootName);
        placedRoot = Root(PlacedRootName);

        // 재질·정렬 순서가 같은 조각을 한 Renderer로 묶는다. 정렬 순서가 낮은 것부터(같은 순서에서는 처음 나온 순서).
        buckets.Clear();
        var keys = new List<(Material material, int order, int first)>();
        for (int i = 0; i < pieces.Length; i++)
        {
            var piece = pieces[i];
            if (piece.mesh == null || piece.material == null) continue;
            if (keys.FindIndex(k => k.material == piece.material && k.order == piece.sortingOrder) < 0) keys.Add((piece.material, piece.sortingOrder, i));
        }
        keys.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.first.CompareTo(b.first));
        foreach (var key in keys)
        {
            var bucket = new Bucket { material = key.material, order = key.order };
            string name = "WallLayer_" + (key.order < 0 ? "m" + (-key.order) : key.order.ToString()) + "_" + buckets.Count;
            var child = layerRoot.Find(name);
            var node = child != null ? child.gameObject : new GameObject(name);
            node.transform.SetParent(layerRoot, false);
            bucket.filter = node.GetComponent<MeshFilter>();
            if (bucket.filter == null) bucket.filter = node.AddComponent<MeshFilter>();
            bucket.renderer = node.GetComponent<MeshRenderer>();
            if (bucket.renderer == null) bucket.renderer = node.AddComponent<MeshRenderer>();
            bucket.renderer.sharedMaterial = key.material;
            Sort(bucket.renderer, key.order);
            buckets.Add(bucket);
        }
        pieceBucket = new int[pieces.Length];
        pieceData = new PieceData[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            var piece = pieces[i];
            pieceBucket[i] = buckets.FindIndex(b => b.material == piece.material && b.order == piece.sortingOrder);
            if (pieceBucket[i] < 0) continue;
            if (!piece.mesh.isReadable)
            {
                Debug.LogError("벽면 키트 조각 Mesh를 읽을 수 없습니다(Read/Write 꺼짐): " + piece.mesh.name, this);
                continue;
            }
            var colors = piece.mesh.colors;
            var vertices = piece.mesh.vertices;
            if (colors == null || colors.Length != vertices.Length)
            {
                colors = new Color[vertices.Length];
                for (int c = 0; c < colors.Length; c++) colors[c] = Color.white;
            }
            pieceData[i] = new PieceData { vertices = vertices, colors = colors, triangles = piece.mesh.triangles, bounds = piece.mesh.bounds };
        }

        cableLines = new List<LineRenderer>[cables.Length];
        cableMaterials = new Material[cables.Length];
        cableOffsets = new float[cables.Length];
        for (int i = 0; i < cables.Length; i++)
        {
            cableLines[i] = new List<LineRenderer>();
            var source = cables[i].material;
            // 같은 흐름 재질을 쓰는 원본 에셋은 고치지 않는다. 케이블마다 실행 중 사본 하나를 그 선들이 함께 쓴다.
            if (source != null && source.HasProperty(ScrollOffsetID))
                cableMaterials[i] = new Material(source) { name = source.name + " (RoomWall)", hideFlags = HideFlags.DontSave };
        }
        placed = new List<Placed>[features.Length];
        placedActive = new int[features.Length];
        for (int i = 0; i < features.Length; i++) placed[i] = new List<Placed>();
    }

    private Transform Root(string name)
    {
        var found = transform.Find(name);
        if (found != null) return found;
        var node = new GameObject(name).transform;
        node.SetParent(transform, false);
        return node;
    }

    private static void Sort(Renderer renderer, int order)
    {
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = order;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // ───────── 조각 합치기 ─────────

    private Mesh[] Layout(Rect interior)
    {
        if (cache.TryGetValue(interior, out var hit))
        {
            cacheOrder.Remove(interior);
            cacheOrder.Add(interior);
            return hit;
        }
        var built = Build(interior);
        cache[interior] = built;
        cacheOrder.Add(interior);
        // 오래된 것부터 버린다(지금 낀 것은 남긴다).
        for (int i = 0; cache.Count > Mathf.Max(1, cacheLimit) && i < cacheOrder.Count; )
        {
            var old = cacheOrder[i];
            if ((hasFit && old == fitted) || old == interior) { i++; continue; }
            cacheOrder.RemoveAt(i);
            if (cache.TryGetValue(old, out var meshes)) foreach (var mesh in meshes) Release(mesh);
            cache.Remove(old);
        }
        return built;
    }

    private Mesh[] Build(Rect interior)
    {
        bool turn = Turn(interior);
        Rect di = ToDesign(interior, turn), dt = ToDesign(TargetOf(interior), turn);
        int count = buckets.Count;
        var vertices = new List<Vector3>[count];
        var colors = new List<Color>[count];
        var triangles = new List<int>[count];
        for (int b = 0; b < count; b++) { vertices[b] = new List<Vector3>(); colors[b] = new List<Color>(); triangles[b] = new List<int>(); }
        int total = 0;
        bool full = false;
        for (int i = 0; i < pieces.Length && !full; i++)
        {
            var data = pieceData[i];
            int b = pieceBucket[i];
            if (data == null || b < 0) continue;
            var piece = pieces[i];
            Anchors(piece.u, di.xMin, di.xMax, dt.xMin, dt.xMax, data.bounds.min.x, data.bounds.max.x, uAnchors);
            Anchors(piece.v, di.yMin, di.yMax, dt.yMin, dt.yMax, data.bounds.min.y, data.bounds.max.y, vAnchors);
            float mu = piece.u.mode == AxisMode.Max && piece.u.mirror ? -1f : 1f;
            float mv = piece.v.mode == AxisMode.Max && piece.v.mirror ? -1f : 1f;
            var vList = vertices[b]; var cList = colors[b]; var tList = triangles[b];
            foreach (float au in uAnchors)
            {
                if (full) break;
                foreach (float av in vAnchors)
                {
                    if (total + data.vertices.Length > vertexBudget)
                    {
                        if (!budgetWarned) Debug.LogWarning($"벽면 키트 정점 예산({vertexBudget}) 초과 — 나머지 조각을 놓지 않습니다: {name} {interior}", this);
                        budgetWarned = true;
                        full = true;
                        break;
                    }
                    int start = vList.Count;
                    for (int k = 0; k < data.vertices.Length; k++)
                    {
                        var p = data.vertices[k];
                        vList.Add(ToLocal(new Vector2(au + mu * p.x, av + mv * p.y), turn));
                        cList.Add(data.colors[k]);
                    }
                    foreach (int t in data.triangles) tList.Add(start + t);
                    total += data.vertices.Length;
                }
            }
        }
        var result = new Mesh[count];
        for (int b = 0; b < count; b++)
        {
            var mesh = new Mesh { name = "RoomWallLayer_" + buckets[b].order, hideFlags = HideFlags.DontSave };
            if (vertices[b].Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices[b]);
            mesh.SetColors(colors[b]);
            mesh.SetTriangles(triangles[b], 0);
            mesh.RecalculateBounds();
            result[b] = mesh;
        }
        return result;
    }

    // 축 하나의 기준점 목록. extent는 조각 범위(기준점 기준, 뒤집기 전). 결과는 기준점 좌표(설계 좌표).
    private static void Anchors(Axis axis, float interiorMin, float interiorMax, float targetMin, float targetMax, float extentMin, float extentMax, List<float> result)
    {
        result.Clear();
        float lo = axis.range == AxisRange.Interior ? interiorMin : targetMin;
        float hi = axis.range == AxisRange.Interior ? interiorMax : targetMax;
        if (hi < lo) return;
        if (axis.mode == AxisMode.Max && axis.mirror) { float swap = -extentMax; extentMax = -extentMin; extentMin = swap; }
        const int Cap = 2048;
        const float Epsilon = 1e-4f;
        float period = Mathf.Max(.05f, axis.period);
        switch (axis.mode)
        {
            case AxisMode.Min: result.Add(lo + axis.offset); return;
            case AxisMode.Max: result.Add(hi - axis.offset); return;
            case AxisMode.Mid: result.Add((lo + hi) * .5f + axis.offset); return;
            case AxisMode.Steps:
            {
                float first = lo + axis.inset - extentMin, last = hi - axis.inset - extentMax;
                if (last < first - Epsilon) return;
                for (float a = first; a <= last + Epsilon && result.Count < Cap; a += period) result.Add(a);
                if (result.Count > 0 && result[result.Count - 1] < last - Epsilon) result.Add(last);
                return;
            }
            default:
            {
                float origin = (axis.centered ? (lo + hi) * .5f : lo) + axis.offset;
                int kMin = Mathf.FloorToInt((lo - extentMax - origin) / period);
                int kMax = Mathf.CeilToInt((hi - extentMin - origin) / period);
                for (int k = kMin; k <= kMax && result.Count < Cap; k++)
                {
                    float a = origin + k * period;
                    if (a + extentMax >= lo - Epsilon && a + extentMin <= hi + Epsilon) result.Add(a);
                }
                return;
            }
        }
    }

    // ───────── 케이블 ─────────

    private void PlaceCables(Rect interior)
    {
        Rect target = TargetOf(interior);
        Rect di = ToDesign(interior, turned), dt = ToDesign(target, turned);
        for (int c = 0; c < cables.Length; c++)
        {
            var cable = cables[c];
            int used = 0;
            if (cable.material != null && cable.width > 0f)
            {
                if (cable.alongLength) Anchors(cable.cross, di.yMin, di.yMax, dt.yMin, dt.yMax, 0f, 0f, crossAnchors);
                else Anchors(cable.cross, di.xMin, di.xMax, dt.xMin, dt.xMax, 0f, 0f, crossAnchors);
                foreach (float anchor in crossAnchors)
                {
                    Vector2 from = cable.alongLength ? new Vector2(dt.xMin, anchor) : new Vector2(anchor, dt.yMin);
                    Vector2 to = cable.alongLength ? new Vector2(dt.xMax, anchor) : new Vector2(anchor, dt.yMax);
                    Path(ToLocal(from, turned), ToLocal(to, turned), cable, target);
                    var line = Line(c, used++);
                    line.positionCount = pathPoints.Count;
                    line.SetPositions(pathPoints.ToArray());
                }
            }
            var list = cableLines[c];
            for (int i = used; i < list.Count; i++) if (list[i] != null && list[i].gameObject.activeSelf) list[i].gameObject.SetActive(false);
        }
    }

    // 화면에서 가로로 놓인 선은 지지점 사이에서 포물선으로 처진다(지지점은 맞춤 목표 가운데 + supportOffset부터 supportSpacing마다).
    private static void Path(Vector3 a, Vector3 b, Cable cable, Rect target)
    {
        pathPoints.Clear();
        bool horizontal = Mathf.Abs(a.y - b.y) < 1e-3f;
        if (!horizontal || cable.sag <= 0f || cable.supportSpacing <= .05f)
        {
            pathPoints.Add(a);
            pathPoints.Add(b);
            return;
        }
        if (a.x > b.x) { var swap = a; a = b; b = swap; }
        supports.Clear();
        supports.Add(a.x);
        float origin = target.center.x + cable.supportOffset;
        int kMin = Mathf.CeilToInt((a.x - origin) / cable.supportSpacing), kMax = Mathf.FloorToInt((b.x - origin) / cable.supportSpacing);
        for (int k = kMin; k <= kMax && supports.Count < 512; k++)
        {
            float x = origin + k * cable.supportSpacing;
            if (x > a.x + 1e-3f && x < b.x - 1e-3f) supports.Add(x);
        }
        supports.Add(b.x);
        const int Segments = 8;
        pathPoints.Add(new Vector3(supports[0], a.y, 0f));
        for (int s = 1; s < supports.Count; s++)
        {
            float x0 = supports[s - 1], x1 = supports[s];
            float depth = cable.sag * Mathf.Clamp01((x1 - x0) / cable.supportSpacing);
            for (int i = 1; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                pathPoints.Add(new Vector3(Mathf.Lerp(x0, x1, t), a.y - depth * 4f * t * (1f - t), 0f));
            }
        }
    }

    private LineRenderer Line(int cable, int index)
    {
        var list = cableLines[cable];
        while (list.Count <= index)
        {
            var node = new GameObject(Clean(cables[cable].name, "Cable") + "_" + list.Count);
            node.transform.SetParent(cableRoot, false);
            var line = node.AddComponent<LineRenderer>();
            ConfigureLine(line, cable);
            list.Add(line);
        }
        var result = list[index];
        if (!result.gameObject.activeSelf) result.gameObject.SetActive(true);
        return result;
    }

    private void ConfigureLine(LineRenderer line, int index)
    {
        var cable = cables[index];
        line.useWorldSpace = false;
        line.loop = false;
        line.widthMultiplier = cable.width;
        line.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(cable.color, 0f), new GradientColorKey(cable.color, 1f) },
            new[] { new GradientAlphaKey(cable.color.a, 0f), new GradientAlphaKey(cable.color.a, 1f) });
        line.colorGradient = gradient;
        line.numCornerVertices = 2;
        line.numCapVertices = 0;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Tile;
        line.textureScale = new Vector2(10f, 0f);      // TutorialLineGlow 눈금 규칙(Tile (10, 0))
        line.generateLightingData = false;
        line.sharedMaterial = cableMaterials[index] != null ? cableMaterials[index] : cable.material;
        Sort(line, cable.sortingOrder);
    }

    private static string Clean(string value, string fallback)
    {
        if (string.IsNullOrEmpty(value)) return fallback;
        return value.Replace("Backdrop", "Wall");
    }

    // ───────── 설비 ─────────

    private void PlaceFeatures(Rect interior)
    {
        Rect di = ToDesign(interior, turned), dt = ToDesign(TargetOf(interior), turned);
        for (int f = 0; f < features.Length; f++)
        {
            var feature = features[f];
            int used = 0;
            if (feature.template != null)
            {
                Anchors(feature.u, di.xMin, di.xMax, dt.xMin, dt.xMax, 0f, 0f, uAnchors);
                Anchors(feature.v, di.yMin, di.yMax, dt.yMin, dt.yMax, 0f, 0f, vAnchors);
                foreach (float au in uAnchors)
                    foreach (float av in vAnchors)
                    {
                        if (used >= 256) break;
                        var item = PlacedAt(f, used++);
                        item.root.transform.localPosition = ToLocal(new Vector2(au, av), turned);
                        item.root.transform.localRotation = Quaternion.identity;
                    }
            }
            var list = placed[f];
            for (int i = used; i < list.Count; i++) if (list[i].root != null && list[i].root.activeSelf) list[i].root.SetActive(false);
            placedActive[f] = used;
        }
    }

    private Placed PlacedAt(int feature, int index)
    {
        var list = placed[feature];
        var definition = features[feature];
        while (list.Count <= index)
        {
            int n = list.Count;
            var copy = Instantiate(definition.template, placedRoot, false);
            copy.name = Clean(definition.name, "Feature") + "_" + n;
            var item = new Placed { root = copy };
            if (!string.IsNullOrEmpty(definition.rotor)) item.rotor = copy.transform.Find(definition.rotor);
            if (!string.IsNullOrEmpty(definition.lamp))
            {
                var lamp = copy.transform.Find(definition.lamp);
                item.lamp = lamp != null ? lamp.GetComponent<Renderer>() : null;
            }
            if (!string.IsNullOrEmpty(definition.slider))
            {
                item.slider = copy.transform.Find(definition.slider);
                if (item.slider != null) item.sliderHome = item.slider.localPosition;
            }
            // 같은 설비가 한꺼번에 깜박·회전하지 않게 자리마다 위상을 다르게 준다(황금비 간격 — 일정하지만 겹치지 않는다).
            item.phase01 = Mathf.Repeat(n * .618034f, 1f);
            item.phase = item.phase01 * Mathf.Max(.05f, definition.blinkPeriod);
            item.phaseDegrees = item.phase01 * 360f;
            list.Add(item);
        }
        var result = list[index];
        if (!result.root.activeSelf) result.root.SetActive(true);
        return result;
    }

    // ───────── 먼지 ─────────

    private void PlaceDust(Rect interior, bool restart)
    {
        if (dust == null) return;
        var shape = dust.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        Vector3 local = dust.transform.parent == transform ? dust.transform.localPosition : Vector3.zero;
        shape.position = new Vector3(interior.center.x - local.x, interior.center.y - local.y, 0f);
        shape.rotation = Vector3.zero;
        shape.scale = new Vector3(interior.width, interior.height, .1f);
        var velocity = dust.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(turned ? 0f : dustSpeed);
        velocity.y = new ParticleSystem.MinMaxCurve(turned ? dustSpeed : 0f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f);
        var emission = dust.emission;
        emission.enabled = true;
        emission.rateOverTime = Mathf.Clamp(interior.width * interior.height * dustPerArea, 1f, Mathf.Max(1f, dustMaxRate));
        if (restart && dust.gameObject.activeInHierarchy)
        {
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            dust.Play(true);
        }
    }

    // ───────── 실행 ─────────

    private void Update()
    {
        if (!hasFit) return;
        float delta = Time.deltaTime;
        clock += delta;
        for (int f = 0; f < features.Length && f < placed.Length; f++)
        {
            var feature = features[f];
            var list = placed[f];
            int active = placedActive[f];
            for (int i = 0; i < active && i < list.Count; i++)
            {
                var item = list[i];
                if (item.rotor != null && feature.spin != 0f)
                    item.rotor.localRotation = Quaternion.Euler(0f, 0f, Mathf.Repeat(item.phaseDegrees + feature.spin * clock, 360f));
                if (item.lamp != null && feature.blinkPeriod > .05f)
                {
                    bool on = Mathf.Repeat(clock + item.phase, feature.blinkPeriod) < feature.blinkPeriod * feature.blinkOn;
                    if (on != item.lampOn) { item.lamp.enabled = on; item.lampOn = on; }
                }
                if (item.slider != null && feature.slidePeriod > .05f)
                {
                    float t = Mathf.Repeat(clock / feature.slidePeriod + item.phase01, 1f);
                    item.slider.localPosition = item.sliderHome + (Vector3)Vector2.Lerp(feature.slideFrom, feature.slideTo, t);
                }
            }
        }
        for (int c = 0; c < cableMaterials.Length; c++)
        {
            var material = cableMaterials[c];
            if (material == null || cables[c].scrollSpeed == 0f) continue;
            cableOffsets[c] = Mathf.Repeat(cableOffsets[c] + cables[c].scrollSpeed * delta, 1f);
            material.SetVector(ScrollOffsetID, new Vector4(cableOffsets[c], 0f, 0f, 0f));
        }
    }

    private void LateUpdate()
    {
        // 지형 쪽이 같은 프레임에 공간을 다시 맞췄으면(SetRoomBounds → RequestFit) 여기서 한 번만 맞춘다.
        // 그사이 키트가 꺼졌으면(ApplyWave가 덮어쓰기를 풂) LateUpdate가 돌지 않아 쓸데없이 합치지 않는다.
        if (hasPending)
        {
            hasPending = false;
            Fit(pending, true);
        }
        UpdateSoundAnchor();
    }

    private void UpdateSoundAnchor()
    {
        if (soundAnchor == null || !hasFit) return;
        var view = Camera.main;
        if (view == null) return;
        Vector2 eye = view.transform.position;
        bool found = false;
        float best = float.PositiveInfinity;
        Vector3 point = soundAnchor.position;
        for (int f = 0; f < features.Length && f < placed.Length; f++)
        {
            if (!features[f].sound) continue;
            var list = placed[f];
            for (int i = 0; i < placedActive[f] && i < list.Count; i++)
            {
                Vector3 at = list[i].root.transform.position;
                float distance = ((Vector2)at - eye).sqrMagnitude;
                if (distance < best) { best = distance; point = at; found = true; }
            }
        }
        if (!found)
        {
            // 소리 설비가 없는 키트(배관 흐름 등)는 실내 안에서 카메라에 가장 가까운 점에서 낸다(실내에 있으면 늘 원래 세기).
            Vector3 local = transform.InverseTransformPoint(new Vector3(eye.x, eye.y, transform.position.z));
            local.x = Mathf.Clamp(local.x, fitted.xMin, fitted.xMax);
            local.y = Mathf.Clamp(local.y, fitted.yMin, fitted.yMax);
            point = transform.TransformPoint(local);
        }
        soundAnchor.position = new Vector3(point.x, point.y, soundAnchor.position.z);
    }

    /// <summary>
    /// 실행 중 만든 것(합친 Mesh 캐시·케이블 재질 사본·Layer/Cable/설비 개체)을 모두 지우고 처음 상태로 돌린다.
    /// Define이 부르고, Edit Mode 검사가 끝날 때 부른다(Edit Mode의 DestroyImmediate는 OnDestroy를 부르지 않는다).
    /// </summary>
    public void ReleaseRuntime()
    {
        foreach (var meshes in cache.Values) foreach (var mesh in meshes) Release(mesh);
        cache.Clear();
        cacheOrder.Clear();
        foreach (var material in cableMaterials) Release(material);
        foreach (var root in new[] { layerRoot, cableRoot, placedRoot })
            if (root != null)
            {
                if (Application.isPlaying) Destroy(root.gameObject);
                else DestroyImmediate(root.gameObject);
            }
        buckets.Clear();
        layerRoot = cableRoot = placedRoot = null;
        cableLines = Array.Empty<List<LineRenderer>>();
        cableMaterials = Array.Empty<Material>();
        cableOffsets = Array.Empty<float>();
        placed = Array.Empty<List<Placed>>();
        placedActive = Array.Empty<int>();
        pieceBucket = Array.Empty<int>();
        pieceData = Array.Empty<PieceData>();
        prepared = hasFit = hasPending = false;
    }

    private static void Release(UnityEngine.Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    private void OnDestroy()
    {
        foreach (var meshes in cache.Values) foreach (var mesh in meshes) Release(mesh);
        cache.Clear();
        foreach (var material in cableMaterials) Release(material);
    }
}
