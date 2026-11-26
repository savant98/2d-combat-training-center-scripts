using System.Collections.Generic;
using UnityEngine;

// 적의 발광 외곽선 옆에 대비 선을 하나 더 그어 배경 위에서 굵고 또렷하게 보이게 한다(2026-11-26).
// 대상 Renderer마다 자식 SpriteRenderer를 두고, 미리 구운 거리장 Sprite(EnemyOutlineLibrarySO)를 Enemy/OutlineStroke 재질로 그린다.
// - 자세·Animation·좌우 반전·화면 보간(FixedMotionBody)은 자식이라 그대로 따른다.
// - 원본의 표시 여부·Sprite·재질 마스크·9-slice 크기·SpriteMask 상호작용을 매 프레임 옮겨 와 등장·순간이동·땅파기 같은 연출을 따라간다.
//   forceRenderingOff는 끄지 않고 같은 값으로 옮긴다(잡기 연출이 원본을 숨기고 복사본을 그릴 때 대비 선 복사본도 함께 그려지게).
// - Clip이 발광을 거의 0으로 줄인 부품만 숨긴다. 맥동·감광 같은 밝기 변화는 따르지 않는다(검은 띠가 깜빡이지 않게).
// - 실행 순서 300은 DrillerRig(250)·StomperCoreCrossVisual(200) 같은 자세·표시 구동기 뒤다.
// - 정렬: Player 레이어의 대비 선은 모든 적이 같은 순서(SharedOrder -5)를 쓴다. 던전 배경 소품(-70~-11)과 상자류 소품 대부분보다 위,
//   적 부품(최저 -4: Stomper 도약 추진기, Driller 팔·Drone 돛대 선 -2)·피격 섬광(-1)·Player 대시 효과(-3)·Admin 블랙홀(-5~-3)보다 아래라서
//   한 적의 검은 띠가 겹친 다른 적의 발광이나 피격 섬광, 전투 효과를 덮지 않는다(같은 순서끼리는 z로 앞선다).
//   단, 등장 Mask·Driller 땅파기 동안 루트 SortingGroup이 켜지면 그룹 순서(0·1)에서 한 덩어리로 그려진다(알려진 한계).
//   다른 레이어(Constructor 일부)는 그 레이어에서 켜진 발광 Renderer 중 가장 낮은 순서 바로 아래에 둔다. 발광은 가산이므로 선 위에 빛이 더해진다.
// 색·두께 같은 모양은 공유 재질이 정한다. 불투명도는 SpriteRenderer 정점 색 알파로 넘겨 MaterialPropertyBlock 없이 배칭되게 한다.
// 대상과 연결은 tools/Unity/EnemyOutlineStrokeSetup.cs가 Prefab에 기록한다.
[DefaultExecutionOrder(300)]
[DisallowMultipleComponent]
public sealed class EnemyOutlineStroke : MonoBehaviour
{
    private const string StrokeName = "__OutlineStroke";
    // Player 레이어 대비 선의 공통 정렬 순서(재질이 여럿이면 이 아래로 쌓는다)
    public const int SharedOrder = -5;
    private static int playerLayer = int.MinValue;
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionTexId = Shader.PropertyToID("_EmissionTex");

    [SerializeField] private EnemyOutlineLibrarySO library;
    [Tooltip("대비 선 재질. 앞의 것이 아래층이다.")]
    [SerializeField] private Material[] materials = System.Array.Empty<Material>();
    [Tooltip("대비 선을 그을 발광 부품")]
    [SerializeField] private SpriteRenderer[] sources = System.Array.Empty<SpriteRenderer>();
    [Tooltip("Model이 없는 개체(사망 효과·떨어지는 폭탄)의 정렬 기준 루트. 비우면 EnemyController.model 또는 이 개체")]
    [SerializeField] private Transform sortingRoot;

    private sealed class Link
    {
        public SpriteRenderer source;
        public SpriteRenderer[] strokes;
        public Sprite sprite;
        public Material material;
        public Texture mask;
        public EnemyOutlineLibrarySO.Entry entry;
        public bool mapped;
        public float baseIntensity = 1f, baseEmission = 1f;
        public bool shown, forced, flipX, flipY;
        public Texture materialMask;
        public SpriteMaskInteraction maskInteraction = (SpriteMaskInteraction)(-1);
        public float opacity = -1f;
        public Sprite strokeSprite;
    }

    private readonly List<Link> links = new();
    private MaterialPropertyBlock block;
    private bool built;

    public EnemyOutlineLibrarySO Library => library;
    public IReadOnlyList<Material> Materials => materials;
    public IReadOnlyList<SpriteRenderer> Sources => sources;
    public int StrokeCount { get; private set; }

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        Build();
    }

    // 실행 중 다른 도구가 대상을 정해 붙일 때 쓴다(미리보기·검증).
    public void Configure(EnemyOutlineLibrarySO outlineLibrary, Material[] strokeMaterials, SpriteRenderer[] targets)
    {
        library = outlineLibrary;
        materials = strokeMaterials ?? System.Array.Empty<Material>();
        sources = targets ?? System.Array.Empty<SpriteRenderer>();
        Clear();
        block ??= new MaterialPropertyBlock();
        Build();
    }

    // 대상 Renderer의 대비 선(재질 층 순서). 검증 도구가 쓴다.
    public bool TryGetStrokes(SpriteRenderer source, out IReadOnlyList<SpriteRenderer> strokes)
    {
        foreach (var link in links)
            if (link.source == source) { strokes = link.strokes; return true; }
        strokes = null;
        return false;
    }

    private void Build()
    {
        if (built || library == null || materials == null || materials.Length == 0 || sources == null || sources.Length == 0) return;
        built = true;
        if (playerLayer == int.MinValue) playerLayer = SortingLayer.NameToID("Player");
        var floors = SortingFloors();
        foreach (var source in sources)
        {
            if (source == null) continue;
            int floor;
            if (source.sortingLayerID == playerLayer) floor = SharedOrder + 1;
            else if (!floors.TryGetValue(source.sortingLayerID, out floor)) floor = source.sortingOrder;
            var link = new Link { source = source, strokes = new SpriteRenderer[materials.Length] };
            for (int i = 0; i < materials.Length; i++)
            {
                var go = new GameObject(StrokeName) { layer = source.gameObject.layer, hideFlags = HideFlags.DontSave };
                go.transform.SetParent(source.transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, DepthNudge(source.transform));
                var stroke = go.AddComponent<SpriteRenderer>();
                stroke.sharedMaterial = materials[i];
                stroke.sortingLayerID = source.sortingLayerID;
                // 모든 발광보다 먼저 그린다. 앞의 재질이 더 아래층이다.
                stroke.sortingOrder = floor - materials.Length + i;
                stroke.enabled = false;
                link.strokes[i] = stroke;
                StrokeCount++;
            }
            links.Add(link);
        }

        // 화면 보간이 이미 Renderer 목록을 모았다면 새 자식까지 다시 모은다.
        if (TryGetComponent<FixedMotionBody>(out var body)) body.RefreshGraphics();
    }

    // 정렬 레이어별 기준 순서: 정렬 루트 아래에서 켜진 발광 Renderer(Sprite·Line·Trail·입자 등) 중 가장 낮은 순서.
    private Dictionary<int, int> SortingFloors()
    {
        Transform root = sortingRoot;
        if (root == null)
        {
            var controller = GetComponent<EnemyController>();
            root = controller != null && controller.model != null ? controller.model.transform : transform;
        }
        var floors = new Dictionary<int, int>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.name.StartsWith("__", System.StringComparison.Ordinal)) continue;
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (renderer is SpriteRenderer sprite && sprite.color.a <= .01f) continue;
            var shared = renderer.sharedMaterial;
            if (shared == null || !(shared.HasProperty(EmissionTexId) || shared.shader.name.IndexOf("Glow", System.StringComparison.Ordinal) >= 0)) continue;
            int layer = renderer.sortingLayerID;
            floors[layer] = floors.TryGetValue(layer, out int current) ? Mathf.Min(current, renderer.sortingOrder) : renderer.sortingOrder;
        }
        return floors;
    }

    private void Clear()
    {
        foreach (var link in links)
            foreach (var stroke in link.strokes)
                if (stroke != null) Destroy(stroke.gameObject);
        links.Clear();
        StrokeCount = 0;
        built = false;
    }

    private void LateUpdate()
    {
        foreach (var link in links) Sync(link);
    }

    private void Sync(Link link)
    {
        var source = link.source;
        float opacity = Visibility(link);
        bool shown = opacity > .001f;
        bool forced = shown && source.forceRenderingOff;
        bool sliced = shown && source.drawMode != SpriteDrawMode.Simple;
        Vector2 size = default, offset = default;
        if (sliced) SlicedLayout(link, out size, out offset);

        var first = link.strokes.Length > 0 ? link.strokes[0] : null;
        bool drifted = first != null && (first.enabled != shown || (shown && first.forceRenderingOff != forced));
        bool changed = drifted || shown != link.shown || forced != link.forced || (shown && (link.strokeSprite != link.entry.stroke
            || Mathf.Abs(opacity - link.opacity) > .002f || source.flipX != link.flipX || source.flipY != link.flipY
            || source.maskInteraction != link.maskInteraction));
        if (!changed && !sliced) return;
        link.shown = shown;
        link.forced = forced;
        if (shown)
        {
            link.strokeSprite = link.entry.stroke;
            link.opacity = opacity;
            link.flipX = source.flipX;
            link.flipY = source.flipY;
            link.maskInteraction = source.maskInteraction;
        }

        foreach (var stroke in link.strokes)
        {
            if (stroke == null) continue;
            stroke.enabled = shown;
            if (!shown) continue;
            stroke.forceRenderingOff = forced;
            stroke.sprite = link.entry.stroke;
            stroke.color = new Color(1f, 1f, 1f, opacity);
            stroke.flipX = source.flipX;
            stroke.flipY = source.flipY;
            stroke.maskInteraction = source.maskInteraction;
            if (sliced)
            {
                if (stroke.drawMode != source.drawMode) stroke.drawMode = source.drawMode;
                stroke.size = size;
                stroke.transform.localPosition = new Vector3(offset.x, offset.y, stroke.transform.localPosition.z);
            }
            else if (stroke.drawMode != SpriteDrawMode.Simple)
            {
                stroke.drawMode = SpriteDrawMode.Simple;
                stroke.transform.localPosition = new Vector3(0f, 0f, stroke.transform.localPosition.z);
            }
        }
    }

    // 같은 정렬 순서의 소품·불투명 몸체보다 뒤에 그려지도록 카메라 쪽(월드 -z)으로 0.001칸 당긴 로컬 z.
    private static float DepthNudge(Transform parent)
    {
        float scale = Mathf.Abs(parent.lossyScale.z);
        return -.001f / (scale > .0001f ? scale : 1f);
    }

    // 9-slice: 원본 크기에 여백을 더하고, 기준점 차이만큼 옮겨 원본 사각형을 여백만큼 감싸게 한다.
    private static void SlicedLayout(Link link, out Vector2 size, out Vector2 offset)
    {
        var source = link.source;
        var stroke = link.entry.stroke;
        float pad = link.entry.padding / Mathf.Max(stroke.pixelsPerUnit, .0001f);
        Vector2 s = source.size;
        size = s + new Vector2(pad * 2f, pad * 2f);
        Vector2 sourcePivot = source.sprite.pivot / source.sprite.rect.size;
        Vector2 strokePivot = stroke.pivot / stroke.rect.size;
        float x = strokePivot.x * size.x - sourcePivot.x * s.x - pad;
        float y = strokePivot.y * size.y - sourcePivot.y * s.y - pad;
        offset = new Vector2(source.flipX ? -x : x, source.flipY ? -y : y);
    }

    // 0이면 숨김. 원본이 꺼졌거나, 배율이 0이거나, 거리장이 없거나, Clip이 발광을 기본값의 5% 아래로 줄인 경우.
    // 기본값의 25% 이상이면 그대로 보인다(맥동·감광 연출에서 검은 띠가 흔들리지 않게). 그 사이만 부드럽게 줄인다.
    private float Visibility(Link link)
    {
        var source = link.source;
        if (source == null || !source.enabled || !source.gameObject.activeInHierarchy) return 0f;
        Vector3 scale = source.transform.lossyScale;
        if (Mathf.Abs(scale.x) < .0001f || Mathf.Abs(scale.y) < .0001f) return 0f;
        bool hasBlock = source.HasPropertyBlock();
        if (hasBlock) source.GetPropertyBlock(block);

        var material = source.sharedMaterial;
        if (material != link.material)
        {
            link.material = material;
            link.baseIntensity = material != null && material.HasProperty(IntensityId) ? Mathf.Max(material.GetFloat(IntensityId), .0001f) : 1f;
            link.baseEmission = material != null && material.HasProperty(EmissionColorId) ? Mathf.Max(MaxChannel(material.GetColor(EmissionColorId)), .0001f) : 1f;
            link.materialMask = material != null && material.HasProperty(EmissionTexId) ? material.GetTexture(EmissionTexId) : null;
            link.sprite = null;
        }
        Texture mask = hasBlock && block.HasTexture(EmissionTexId) ? block.GetTexture(EmissionTexId) : link.materialMask;
        if (source.sprite != link.sprite || mask != link.mask)
        {
            link.sprite = source.sprite;
            link.mask = mask;
            link.mapped = library.TryFind(link.sprite, mask, out link.entry);
        }
        if (!link.mapped) return 0f;

        float ratio = 1f;
        if (hasBlock)
        {
            if (block.HasFloat(IntensityId)) ratio *= block.GetFloat(IntensityId) / link.baseIntensity;
            // SetColor·SetVector 어느 쪽으로 넣었든 같은 칸에 저장되므로 원래 값 그대로 읽는다.
            if (block.HasVector(EmissionColorId)) ratio *= MaxChannel(block.GetVector(EmissionColorId)) / link.baseEmission;
        }
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.05f, .25f, ratio));
    }

    private void OnDisable()
    {
        foreach (var link in links)
        {
            link.shown = false;
            foreach (var stroke in link.strokes)
                if (stroke != null) stroke.enabled = false;
        }
    }

    private void OnDestroy()
    {
        foreach (var link in links)
            foreach (var stroke in link.strokes)
                if (stroke != null) Destroy(stroke.gameObject);
        links.Clear();
    }

    private static float MaxChannel(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));
    private static float MaxChannel(Vector4 c) => Mathf.Max(c.x, Mathf.Max(c.y, c.z));
}
