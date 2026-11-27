using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 추격 분쇄기(ChasingGrinder)의 보이는 몸체다. 판정과 추격은 ChasingGrinder가 맡고, 여기서는 그 위치에 놓여
/// 분쇄 날의 Animation(공회전·앞뒤 왕복)을 돌리고 불꽃과 갈린 조각을 뿌린다.
/// 이 개체의 +Y가 분쇄기가 나아가는 방향이고, 원점은 분쇄선(분쇄 날이 가장 멀리 나왔을 때의 끝) 가운데다.
/// 풀이 분쇄기 개체를 여러 개 만들어 두어도 무거운 몸체는 Prefab마다 필요한 수만 만들어 빌려 쓴다.
/// 격자 편집기에서 띠 폭을 바꾼 분쇄기는 빌릴 때 몸체를 그 폭에 맞춘다(Fit). 늘이지 않고, 폭에 따라 달라지는 몸체·소켓은
/// ChasingGrinderLayout으로 다시 그리고 톱날·드릴·기어는 간격에 맞춰 옮기거나 복제한다. Prefab의 폭이면 아무것도 바꾸지 않는다.
/// 설치형 고정 모드(SetFixedMode — 던전 분쇄 투입구 PropShredder가 빌려 쓸 때):
///   - Design에 SortingGroup(Default 층 FixedDesignOrder = -2)을 켜서 자식 Renderer의 원래 정렬 순서(20~60)를 압축 없이 100% 보존한다.
///     (드릴·가시·뒷톱날 깊이가 추격 분쇄기와 완전히 동일하게 유지되며 지형 Default 0~1 뒤·방 벽 배경 -38~-14 앞에 그려진다.)
///   - 검정 사각형(FixedPitShade) 대신 구덩이 상단은 방 배경이 자연스럽게 드러나게 두고, 하단 소켓 받침(Throat, Default -6~-4)과
///     구덩이 양옆·하단 테두리를 감싸는 볼트 체결 외곽 프레임(OuterFrame, Default 3~5)으로 설치형 마감을 입힌다.
///   - 돌려줄 때(Release) SortingGroup·설치 외곽 프레임을 끄고 원래 묶음·빨강으로 되돌려 추격 분쇄기가 그대로 다시 빌릴 수 있다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ChasingGrinderBody : MonoBehaviour
{
    [SerializeField, Tooltip("분쇄 날을 돌리고 앞뒤로 움직이는 Animator. 속도로 빠르기를 바꾼다.")]
    private Animator animator;
    [SerializeField, Min(.5f), Tooltip("분쇄 띠의 길이(칸). 구간 안쪽 폭과 같다.")]
    private float span = 26f;

    [Header("입자")]
    [Tooltip("날에서 계속 튀는 불꽃. 띠 전체에 걸쳐 드문드문 나온다.")]
    [SerializeField] private ParticleSystem bladeSparks;
    [Tooltip("갈리는 지점에서 튀는 불꽃. 코드가 필요한 만큼 뿌린다.")]
    [SerializeField] private ParticleSystem grindSparks;
    [Tooltip("갈려 나온 조각. 코드가 필요한 만큼 뿌린다.")]
    [SerializeField] private ParticleSystem chips;
    [Tooltip("갈릴 때 이는 먼지. 코드가 필요한 만큼 뿌린다.")]
    [SerializeField] private ParticleSystem dust;
    [Tooltip("생성 발판 조각의 색(발판의 호박색 발광).")]
    [SerializeField] private Color slabChipColor = new Color(1f, .55f, .12f, .95f);
    [Tooltip("적 조각의 색(적의 붉은 발광).")]
    [SerializeField] private Color enemyChipColor = new Color(1f, .16f, .08f, .95f);
    [Tooltip("평소 띠 1칸에서 1초에 튀는 불꽃 수.")]
    [SerializeField, Min(0f)] private float bladeSparkRate = 1.4f;
    [Tooltip("바닥·벽을 뚫고 나오는 동안 띠 1칸에서 1초에 튀는 불꽃 수.")]
    [SerializeField, Min(0f)] private float breachSparkRate = 9f;
    [Tooltip("날에 닿은 발판 한 칸에서 1초에 튀는 불꽃 수.")]
    [SerializeField, Min(0f)] private float contactSparkRate = 26f;

    public float Span => span;
    public Animator Animator => animator;
    public bool IsRented { get; private set; }

    private float contactCarry;
    private float sparkRate = -1f;
    private bool sparking;

    // ───────────── 빌려 쓰기 ─────────────
    private static readonly Dictionary<ChasingGrinderBody, List<ChasingGrinderBody>> made = new();
    private static Transform poolRoot;
    // 폭(0.01칸 단위)별로 다시 그린 몸체 Mesh. Prefab에 든 폭은 Prefab의 Mesh 에셋을 그대로 넣어 둔다(새로 만들지 않는다).
    private static readonly Dictionary<int, Dictionary<(ChasingGrinderLayout.Group, string), Mesh>> fittedMeshes = new();
    private static readonly List<Mesh> generatedMeshes = new();
    // Prefab마다 지금까지 예열한 가장 넓은 폭의 앞줄 톱날 수. 나중에 만든 몸체에도 그만큼의 부품을 준비한다.
    private static readonly Dictionary<ChasingGrinderBody, int> unitCapacity = new();
    // 설치형 고정 모드: 폭(0.01칸 단위)별 외곽 프레임·소켓 받침 Mesh(6개: Throat/Frame × Interior/Outline/Detail).
    private static readonly Dictionary<int, Mesh[]> fixedCasingMeshes = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // 도메인 리로드를 끈 Play에서도 지난 실행의 목록이 남지 않게 한다.
        made.Clear();
        poolRoot = null;
        foreach (var mesh in generatedMeshes)
            if (mesh != null) Destroy(mesh);
        generatedMeshes.Clear();
        fittedMeshes.Clear();
        unitCapacity.Clear();
        fixedCasingMeshes.Clear();
    }

    private static int SpanKey(float span) => Mathf.RoundToInt(span * 100f);

    private static List<ChasingGrinderBody> Made(ChasingGrinderBody prefab)
    {
        if (!made.TryGetValue(prefab, out var list)) made[prefab] = list = new List<ChasingGrinderBody>();
        // 씬이 바뀌어 사라진 몸체는 목록에서 뺀다.
        list.RemoveAll(item => item == null);
        return list;
    }

    private static ChasingGrinderBody Create(ChasingGrinderBody prefab)
    {
        if (poolRoot == null) poolRoot = new GameObject("ChasingGrinderBodies").transform;
        var body = Instantiate(prefab, poolRoot);
        body.name = prefab.name;
        body.gameObject.SetActive(false);
        return body;
    }

    /// <summary>
    /// 이 Prefab의 몸체를 count개(최소 1개)까지 만들어 둔다(입장 로딩 때). 처음 나오는 순간에 만들지 않게 한다.
    /// span을 주면 그 폭의 Mesh와 그 폭에 필요한 만큼의 톱날·드릴도 미리 만들어 둔다(나올 때는 바꿔 끼우기만 한다).
    /// </summary>
    public static void Prewarm(ChasingGrinderBody prefab, float span = 0f, int count = 1)
    {
        if (prefab == null) return;
        var list = Made(prefab);
        while (list.Count < Mathf.Max(1, count)) list.Add(Create(prefab));
        int saws = unitCapacity.TryGetValue(prefab, out int capacity) ? capacity : 0;
        if (span > 0f)
        {
            span = ChasingGrinderLayout.ClampSpan(span);
            // 첫 몸체가 Prefab의 Mesh를 먼저 등록해야 Prefab과 같은 폭을 새로 그리지 않는다.
            foreach (var body in list) body.EnsureRig();
            MeshesFor(span);
            saws = Mathf.Max(saws, ChasingGrinderLayout.SawCount(span));
        }
        if (saws <= 0) return;
        // 예열 순서와 관계없이 모든 몸체가 지금까지 예열한 가장 넓은 폭의 부품을 갖게 한다(나올 때 복제하지 않는다).
        unitCapacity[prefab] = saws;
        foreach (var body in list) body.EnsureUnits(saws);
    }

    /// <summary>
    /// 쉬고 있는 몸체를 꺼내 span 폭에 맞춰 준다. 같은 폭으로 맞춰 둔 몸체를 먼저 고르고, 없으면 새로 만든다. 다 쓰면 Release로 돌려준다.
    /// span이 0 이하면 Prefab의 폭이다.
    /// </summary>
    public static ChasingGrinderBody Rent(ChasingGrinderBody prefab, float span = 0f)
    {
        if (prefab == null) return null;
        var list = Made(prefab);
        if (span <= 0f) span = prefab.span;
        span = ChasingGrinderLayout.ClampSpan(span);
        ChasingGrinderBody body = null;
        foreach (var item in list)
            if (!item.IsRented && SpanKey(item.span) == SpanKey(span)) { body = item; break; }
        if (body == null)
            foreach (var item in list)
                if (!item.IsRented) { body = item; break; }
        if (body == null) list.Add(body = Create(prefab));
        body.IsRented = true;
        body.contactCarry = 0f;
        body.sparkRate = -1f;
        body.sparking = false;
        body.gameObject.SetActive(true);
        body.Fit(span);
        body.ClearParticles();
        return body;
    }

    /// <summary>몸체를 끄고 다음에 다시 쓸 수 있게 돌려준다. 고정 모드·꺼 둔 빨강·구동 빠르기는 원래대로 되돌린다.</summary>
    public void Release()
    {
        IsRented = false;
        if (!dangerOn) SetDangerLights(true);
        if (fixedMode) SetFixedMode(false);
        if (animator != null) animator.speed = 1f;
        // 빌린 쪽이 자식으로 옮겨 두었으면(던전 분쇄 투입구) 몸체 풀 묶음으로 되돌린다(묶음이 씬 정리로 없어졌으면 다시 만든다).
        if (transform.parent == null || transform.parent != poolRoot)
        {
            if (poolRoot == null) poolRoot = new GameObject("ChasingGrinderBodies").transform;
            transform.SetParent(poolRoot, false);
        }
        ClearParticles();
        gameObject.SetActive(false);
    }

    private void ClearParticles()
    {
        sparking = false;
        if (bladeSparks != null) bladeSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (grindSparks != null) grindSparks.Clear(true);
        if (chips != null) chips.Clear(true);
        if (dust != null) dust.Clear(true);
    }

    // ───────────── 폭 맞추기 ─────────────
    // 폭에 따라 다시 그리는 Mesh 노드(Design/Back·Housing·Sockets·SawSockets 바로 아래)와 부품 노드의 부모.
    private readonly Dictionary<(ChasingGrinderLayout.Group, string), MeshFilter> fitFilters = new();
    private Transform blades, housing;
    private bool rigReady;

    private void EnsureRig()
    {
        if (rigReady) return;
        rigReady = true;
        var design = transform.Find("Design");
        if (design == null) return;
        blades = design.Find("Blades");
        foreach (ChasingGrinderLayout.Group group in System.Enum.GetValues(typeof(ChasingGrinderLayout.Group)))
        {
            var node = design.Find(ChasingGrinderLayout.GroupName(group));
            if (node == null) continue;
            if (group == ChasingGrinderLayout.Group.Housing) housing = node;
            // 기어 노드는 Mesh를 자식에 두므로 여기 들지 않는다(옮기기만 한다).
            foreach (Transform child in node)
                if (child.TryGetComponent<MeshFilter>(out var filter)) fitFilters[(group, child.name)] = filter;
        }
        // Prefab에 든 폭은 Prefab의 Mesh 에셋을 그 폭의 몫으로 등록해, 그 폭으로 되돌아갈 때 새로 그리지 않는다.
        int key = SpanKey(span);
        if (fittedMeshes.ContainsKey(key)) return;
        var own = new Dictionary<(ChasingGrinderLayout.Group, string), Mesh>();
        foreach (var row in fitFilters) own[row.Key] = row.Value.sharedMesh;
        fittedMeshes[key] = own;
    }

    private static Dictionary<(ChasingGrinderLayout.Group, string), Mesh> MeshesFor(float span)
    {
        int key = SpanKey(span);
        if (fittedMeshes.TryGetValue(key, out var meshes)) return meshes;
        meshes = ChasingGrinderLayout.BuildMeshes(span, "ChasingGrinder_W" + key);
        generatedMeshes.AddRange(meshes.Values);
        fittedMeshes[key] = meshes;
        return meshes;
    }

    // 앞줄 톱날 saws개 몫의 톱날·드릴·뒷줄 톱날·기어가 모두 있게 한다. 모자라면 같은 모양의 부품(짝수·홀수 번째)을 복제한다.
    // 이름이 분쇄 날 Animation의 경로와 같아 복제한 부품도 같은 Clip으로 움직인다(Clip에는 MaxSaws개 몫의 곡선이 있다).
    private bool EnsureUnits(int saws)
    {
        EnsureRig();
        if (blades == null || housing == null) return false;
        // 빨강 끔 상태에서 복제하면 꺼진 Renderer가 복제본의 '원래 값'이 되므로, 복제하는 동안만 원래대로 되돌린다.
        // 고정 모드 정렬은 Design SortingGroup이 담당하므로 자식 Renderer 정렬은 건드리지 않는다.
        bool wasDark = !dangerOn;
        if (wasDark) SetDangerLights(true);
        try { return AddUnits(saws); }
        finally
        {
            if (wasDark) SetDangerLights(false);
        }
    }

    private bool AddUnits(int saws)
    {
        bool added = false;
        for (int i = 0; i < saws; i++)
        {
            added |= Unit(blades, "Saw" + i, "Saw" + (i % 2));
            added |= Unit(housing, "Gear" + i, "Gear0");
        }
        for (int j = 1; j < saws; j++)
        {
            added |= Unit(blades, "Rear" + j, j % 2 == 1 ? "Rear1" : "Rear2");
            added |= Unit(blades, "Drill" + j, "Drill1");
            added |= Unit(housing, "Pinion" + j, "Pinion1");
        }
        return added;
    }

    private static bool Unit(Transform parent, string name, string template)
    {
        if (parent.Find(name) != null) return false;
        var source = parent.Find(template);
        if (source == null) return false;
        var copy = Instantiate(source.gameObject, parent);
        copy.name = name;
        // 꺼진 채 둔다. 그 부품을 쓰는 폭으로 맞출 때 Arrange가 자리에 놓고 켠다(예열만 하고 Prefab 폭으로 빌린 몸체에 겹쳐 보이지 않게 한다).
        copy.SetActive(false);
        return true;
    }

    /// <summary>몸체를 span 폭(칸)에 맞춘다. 지금 폭과 같으면 아무것도 하지 않는다.</summary>
    public void Fit(float span)
    {
        span = ChasingGrinderLayout.ClampSpan(span);
        EnsureRig();
        if (SpanKey(span) == SpanKey(this.span)) return;
        var meshes = MeshesFor(span);
        foreach (var row in fitFilters) row.Value.sharedMesh = meshes.TryGetValue(row.Key, out var mesh) ? mesh : null;
        int saws = ChasingGrinderLayout.SawCount(span);
        EnsureUnits(saws);
        Arrange(blades, "Saw", 0, saws, false, 0f, span);
        Arrange(blades, "Rear", 1, saws, true, 0f, span);
        Arrange(blades, "Drill", 1, saws, true, 0f, span);
        Arrange(housing, "Gear", 0, saws, false, ChasingGrinderLayout.GearY, span);
        Arrange(housing, "Pinion", 1, saws, true, ChasingGrinderLayout.GearY, span);
        this.span = span;
        // 띠 전체에서 불꽃이 나오게 한다(빈도는 SetSparks가 폭에 맞춰 다시 정한다).
        if (bladeSparks != null)
        {
            var shape = bladeSparks.shape;
            var scale = shape.scale;
            scale.x = span - .4f;
            shape.scale = scale;
            var main = bladeSparks.main;
            main.maxParticles = Mathf.RoundToInt(span * 5f);
        }
        sparkRate = -1f;
        // 복제했거나 켜고 끈 부품을 Animation이 다시 찾게 한다.
        if (animator != null && animator.isActiveAndEnabled) animator.Rebind();
        if (fixedMode) RefreshFixedCasing();
    }

    // prefix + 번호 부품 가운데 first..saws-1번을 간격에 맞춰 놓고 켜며, 나머지는 끈다. joint면 톱날 사이 자리에 놓는다.
    private static void Arrange(Transform parent, string prefix, int first, int saws, bool joint, float y, float span)
    {
        if (parent == null) return;
        for (int i = first; i <= ChasingGrinderLayout.MaxSaws; i++)
        {
            var node = parent.Find(prefix + i);
            if (node == null) continue;
            bool used = i < saws;
            if (used) node.localPosition = new Vector3(joint ? ChasingGrinderLayout.JointX(span, i) : ChasingGrinderLayout.SawX(span, i), y, 0f);
            if (node.gameObject.activeSelf != used) node.gameObject.SetActive(used);
        }
    }

    // ───────────── 배치·구동 ─────────────
    /// <summary>분쇄선 가운데(edgeCenter)에 놓고, +Y가 나아가는 방향(forward)을 보게 한다.</summary>
    public void Place(Vector2 edgeCenter, Vector2 forward)
    {
        transform.SetPositionAndRotation(new Vector3(edgeCenter.x, edgeCenter.y, transform.position.z),
            Quaternion.LookRotation(Vector3.forward, forward));
    }

    // ───────────── 설치형 고정 모드(던전 분쇄 투입구) ─────────────
    /// <summary>고정 모드 정렬 기준(Default 층). 방 벽 배경(-38~-14)보다 앞, 바닥 지형(0·1)보다 뒤, 설치 외곽 프레임은 지형 앞(3~5·Player 뒤).</summary>
    public const int FixedOrderMin = -13, FixedOrderMax = -1;
    public const int FixedThroatOrder = -6;
    public const int FixedDesignOrder = -2;
    public const int FixedFrameOrder = 3;
    /// <summary>설치 구덩이 깊이(칸, 분쇄선부터 아래로). 바닥 지형이 이 아래로 나가는 축을 가린다.</summary>
    public const float FixedPitDepth = 2f;
    private const string FixedCasingName = "FixedCasing";
    private static readonly string[] FixedHiddenGroups = { "Back", "Housing", "Sockets", "SawSockets" };
    private static readonly string[] CasingPartNames =
    {
        "ThroatInterior", "ThroatOutline", "ThroatDetail",
        "FrameInterior", "FrameOutline", "FrameDetail",
    };
    private readonly List<(GameObject node, bool active)> fixedHidden = new();
    private readonly List<(Renderer renderer, bool enabled)> dangerParts = new();
    private bool fixedMode;
    private bool dangerOn = true;
    private SortingGroup designSortingGroup;
    private Transform fixedCasing;
    private MeshFilter[] casingFilters;
    private MeshRenderer[] casingRenderers;

    /// <summary>고정 모드(던전 분쇄 투입구)로 빌려 쓰는 중.</summary>
    public bool IsFixed => fixedMode;
    /// <summary>날·미끄럼판의 빨강(Damage 재질 Renderer)이 켜져 있는지.</summary>
    public bool DangerLightsOn => dangerOn;
    /// <summary>고정 모드에서 Design 내부 정렬(20~60)을 압축 없이 지형 뒤(Default -2)로 묶는 SortingGroup(검사용).</summary>
    public SortingGroup FixedSortingGroup => designSortingGroup;
    /// <summary>고정 모드의 설치형 외곽 프레임·소켓 받침 루트(없으면 null, 검사용).</summary>
    public Transform FixedCasing => fixedCasing;

    /// <summary>
    /// 고정 모드를 켜고 끈다. 켜면 Design에 SortingGroup(Default -2)을 켜서 추격 분쇄기 날·드릴의 원래 정렬 순서(20~60)를
    /// 그대로 유지한 채 지형 뒤에 배치하고, 검정 사각형 없이 설치형 소켓 받침(Default -6~-4)과 외곽 프레임(Default 3~5)을 켠다.
    /// 끄면 SortingGroup·설치 외곽 프레임을 끄고 원래 묶음으로 되돌린다.
    /// </summary>
    public void SetFixedMode(bool on)
    {
        if (on == fixedMode) return;
        EnsureRig();
        var design = transform.Find("Design");
        if (on)
        {
            fixedMode = true;
            fixedHidden.Clear();
            if (design != null)
            {
                foreach (string group in FixedHiddenGroups)
                {
                    var node = design.Find(group);
                    if (node == null) continue;
                    fixedHidden.Add((node.gameObject, node.gameObject.activeSelf));
                    node.gameObject.SetActive(false);
                }
                if (designSortingGroup == null)
                {
                    designSortingGroup = design.GetComponent<SortingGroup>();
                    if (designSortingGroup == null) designSortingGroup = design.gameObject.AddComponent<SortingGroup>();
                }
                designSortingGroup.sortingLayerID = SortingLayer.NameToID("Default");
                designSortingGroup.sortingOrder = FixedDesignOrder;
                designSortingGroup.enabled = true;
            }
            RefreshFixedCasing();
            return;
        }
        if (fixedCasing != null)
        {
            fixedCasing.localPosition = Vector3.zero;
            fixedCasing.gameObject.SetActive(false);
        }
        if (designSortingGroup != null) designSortingGroup.enabled = false;
        foreach (var (node, active) in fixedHidden)
            if (node != null) node.SetActive(active);
        fixedHidden.Clear();
        fixedMode = false;
    }

    // 설치형 외곽 프레임(Default 3~5) + 하단 소켓 받침(Default -6~-4):
    // 구덩이 상단 뒷배경은 열어 두어 어색한 검정 사각형을 없애고, 추격 분쇄기와 동일한 Machine 재질로 외곽만 깔끔하게 마감한다.
    private void RefreshFixedCasing()
    {
        if (!fixedMode) return;
        if (fixedCasing == null)
        {
            var node = transform.Find(FixedCasingName);
            if (node == null)
            {
                node = new GameObject(FixedCasingName).transform;
                node.SetParent(transform, false);
            }
            fixedCasing = node;
            ResolveMachineMaterials(out var interiorMat, out var outlineMat, out var detailMat);
            var mats = new[] { interiorMat, outlineMat, detailMat, interiorMat, outlineMat, detailMat };
            var orders = new[]
            {
                FixedThroatOrder, FixedThroatOrder + 1, FixedThroatOrder + 2,
                FixedFrameOrder, FixedFrameOrder + 1, FixedFrameOrder + 2,
            };
            int defaultLayer = SortingLayer.NameToID("Default");
            casingFilters = new MeshFilter[CasingPartNames.Length];
            casingRenderers = new MeshRenderer[CasingPartNames.Length];
            for (int i = 0; i < CasingPartNames.Length; i++)
            {
                var part = fixedCasing.Find(CasingPartNames[i]);
                if (part == null)
                {
                    part = new GameObject(CasingPartNames[i]).transform;
                    part.SetParent(fixedCasing, false);
                }
                var mf = part.GetComponent<MeshFilter>();
                if (mf == null) mf = part.gameObject.AddComponent<MeshFilter>();
                var mr = part.GetComponent<MeshRenderer>();
                if (mr == null) mr = part.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mats[i];
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.sortingLayerID = defaultLayer;
                mr.sortingOrder = orders[i];
                casingFilters[i] = mf;
                casingRenderers[i] = mr;
            }
        }
        int key = SpanKey(span);
        if (!fixedCasingMeshes.TryGetValue(key, out var meshes) || meshes == null || meshes.Length != CasingPartNames.Length || meshes[0] == null)
        {
            meshes = BuildFixedCasingMeshes(span, key);
            foreach (var m in meshes) generatedMeshes.Add(m);
            fixedCasingMeshes[key] = meshes;
        }
        for (int i = 0; i < CasingPartNames.Length; i++)
        {
            casingFilters[i].sharedMesh = meshes[i];
            casingRenderers[i].enabled = meshes[i].vertexCount > 0;
        }
        fixedCasing.gameObject.SetActive(true);
    }

    private void ResolveMachineMaterials(out Material interior, out Material outline, out Material detail)
    {
        interior = outline = detail = null;
        var root = housing != null ? housing : transform.Find("Design");
        if (root == null) return;
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mat = mr.sharedMaterial;
            if (mat == null) continue;
            if (interior == null && mat.name.StartsWith("MachineInterior", System.StringComparison.Ordinal)) interior = mat;
            else if (outline == null && mat.name.StartsWith("MachineOutline", System.StringComparison.Ordinal)) outline = mat;
            else if (detail == null && mat.name.StartsWith("MachineDetail", System.StringComparison.Ordinal)) detail = mat;
        }
        if (interior == null) interior = outline;
        if (outline == null) outline = interior;
        if (detail == null) detail = outline;
    }

    // 폭 span 기준 설치형 외곽 마감·소켓 받침 Mesh 6종을 생성한다(폭별 1회 캐시).
    private static Mesh[] BuildFixedCasingMeshes(float span, int key)
    {
        float half = span * .5f;
        int saws = ChasingGrinderLayout.SawCount(span);
        const float pitY = -FixedPitDepth;   // -2.0 (구덩이 바닥선)

        var buckets = new (List<Vector3> v, List<int> t, List<Vector2> uv)[CasingPartNames.Length];
        for (int i = 0; i < buckets.Length; i++)
            buckets[i] = (new List<Vector3>(), new List<int>(), new List<Vector2>());

        void AddShape(int idx, Vector2[] poly)
        {
            var (v, t, uv) = buckets[idx];
            int start = v.Count;
            var sv = new List<Vector3>();
            var st = new List<int>();
            ChasingGrinderLayout.ShapeMesh(poly, sv, st);
            v.AddRange(sv);
            foreach (int tri in st) t.Add(start + tri);
            for (int k = 0; k < sv.Count; k++) uv.Add(Vector2.zero);
        }

        void AddStroke(int idx, Vector2[] line, float width, bool closed)
        {
            var (v, t, uv) = buckets[idx];
            int start = v.Count;
            var sv = new List<Vector3>();
            var st = new List<int>();
            var suv = new List<Vector2>();
            ChasingGrinderLayout.StrokeMesh(new[] { line }, width, closed, sv, st, suv);
            v.AddRange(sv);
            foreach (int tri in st) t.Add(start + tri);
            uv.AddRange(suv);
        }

        void AddSegments(int idx, List<(Vector2 a, Vector2 b)> segs, float width)
        {
            if (segs.Count == 0) return;
            var (v, t, uv) = buckets[idx];
            int start = v.Count;
            var sv = new List<Vector3>();
            var st = new List<int>();
            ChasingGrinderLayout.SegmentMesh(segs, width, sv, st);
            v.AddRange(sv);
            foreach (int tri in st) t.Add(start + tri);
            for (int k = 0; k < sv.Count; k++) uv.Add(Vector2.zero);
        }

        void AddPanel(int interiorIdx, int outlineIdx, Vector2[] poly, float lineW)
        {
            AddShape(interiorIdx, poly);
            AddStroke(outlineIdx, poly, lineW, true);
        }

        // ── 1) 하단 소켓 받침(Throat, Default -6~-4, Design 뒤): 상단 구덩이 배경은 비워 검정 사각형을 없애고 하단 기계 받침만 배치 ──
        var throatBed = ChasingGrinderLayout.Chamfer(-half, pitY - .06f, half, pitY + .52f, .06f);
        AddPanel(0, 1, throatBed, .04f);
        var throatLines = new List<(Vector2, Vector2)>
        {
            (new Vector2(-half + .18f, pitY + .34f), new Vector2(half - .18f, pitY + .34f)),
        };
        // 앞줄 톱날·드릴 축이 드나드는 소켓 칼라(ChasingGrinderLayout의 SawSockets·Sockets 문법)
        for (int i = 0; i < saws; i++)
        {
            float sx = ChasingGrinderLayout.SawX(span, i);
            AddPanel(0, 1, ChasingGrinderLayout.Chamfer(sx - .24f, pitY - .04f, sx + .24f, pitY + .64f, .045f), .03f);
            throatLines.Add((new Vector2(sx - .13f, pitY + .48f), new Vector2(sx + .13f, pitY + .48f)));
        }
        for (int j = 1; j < saws; j++)
        {
            float jx = ChasingGrinderLayout.JointX(span, j);
            AddPanel(0, 1, ChasingGrinderLayout.Chamfer(jx - .44f, pitY - .04f, jx + .44f, pitY + .60f, .045f), .03f);
            throatLines.Add((new Vector2(jx - .20f, pitY + .44f), new Vector2(jx + .20f, pitY + .44f)));
        }
        // 구덩이 좌우 내벽의 얇은 수직 레일(배경을 막지 않으면서 기계식 구덩이 깊이감을 부여)
        var wallGuides = new List<(Vector2, Vector2)>
        {
            (new Vector2(-half + .05f, pitY), new Vector2(-half + .05f, -.04f)),
            (new Vector2(half - .05f, pitY), new Vector2(half - .05f, -.04f)),
        };
        AddSegments(1, wallGuides, .06f);
        AddSegments(2, throatLines, .018f);

        // ── 2) 설치형 외곽 마감 프레임(OuterFrame, Default 3~5, 지형 앞·Player 뒤): 양옆 기둥·상단 코너 브래킷·하단 실 빔 ──
        var frameDetails = new List<(Vector2, Vector2)>();
        foreach (int side in new[] { -1, 1 })
        {
            float x0 = side < 0 ? -half - .16f : half - .10f;
            float x1 = side < 0 ? -half + .10f : half + .16f;
            // 좌우 수직 외곽 장갑판
            AddPanel(3, 4, ChasingGrinderLayout.Chamfer(x0, pitY - .14f, x1, .06f, .045f), .042f);
            // 구덩이 입구 상단 바닥 턱에 볼트 체결되는 L자 코너 브래킷
            float bx0 = side < 0 ? -half - .34f : half - .10f;
            float bx1 = side < 0 ? -half + .10f : half + .34f;
            AddPanel(3, 4, ChasingGrinderLayout.Chamfer(bx0, -.14f, bx1, .08f, .04f), .04f);

            float seamX = side < 0 ? -half - .03f : half + .03f;
            frameDetails.Add((new Vector2(seamX, pitY + .02f), new Vector2(seamX, -.16f)));
            // 측면 볼트 리벳 4개 + 상단 브래킷 볼트 1개
            for (int r = 0; r < 4; r++)
            {
                float by = Mathf.Lerp(pitY + .18f, -.28f, r / 3f);
                AddShape(5, ChasingGrinderLayout.Box(seamX - .022f, by - .022f, seamX + .022f, by + .022f));
            }
            float capBoltX = side < 0 ? -half - .22f : half + .22f;
            AddShape(5, ChasingGrinderLayout.Box(capBoltX - .026f, -.056f, capBoltX + .026f, -.004f));
        }
        // 구덩이 하단 바닥 이음새를 덮는 가로 실 빔(정지 시 날이 이 아래로 깔끔하게 수납됨)
        AddPanel(3, 4, ChasingGrinderLayout.Chamfer(-half - .16f, pitY - .16f, half + .16f, pitY + .10f, .045f), .042f);
        frameDetails.Add((new Vector2(-half + .06f, pitY - .02f), new Vector2(half - .06f, pitY - .02f)));
        int bolts = Mathf.Max(3, Mathf.RoundToInt(span));
        for (int b = 0; b < bolts; b++)
        {
            float bx = Mathf.Lerp(-half + .28f, half - .28f, bolts > 1 ? b / (float)(bolts - 1) : .5f);
            AddShape(5, ChasingGrinderLayout.Box(bx - .024f, pitY - .10f, bx + .024f, pitY - .052f));
        }
        AddSegments(5, frameDetails, .016f);

        var result = new Mesh[CasingPartNames.Length];
        for (int i = 0; i < CasingPartNames.Length; i++)
        {
            var mesh = new Mesh { name = "ChasingGrinder_Fixed_" + CasingPartNames[i] + "_W" + key };
            mesh.SetVertices(buckets[i].v);
            mesh.SetTriangles(buckets[i].t, 0);
            mesh.SetUVs(0, buckets[i].uv);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            result[i] = mesh;
        }
        return result;
    }

    /// <summary>
    /// 고정 모드 배치: 분쇄선을 edgeCenter에서 forward 반대쪽으로 sink칸 내린 자리에 둔다(정지 때 날 띠가 구덩이 아래로 가라앉는다).
    /// 설치형 외곽 프레임과 소켓 받침은 가라앉지 않고 구덩이 테두리에 고정된다.
    /// </summary>
    public void PlaceFixed(Vector2 edgeCenter, Vector2 forward, float sink)
    {
        var direction = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector2.up;
        sink = Mathf.Max(0f, sink);
        Place(edgeCenter - direction * sink, direction);
        if (fixedCasing != null) fixedCasing.localPosition = new Vector3(0f, sink, 0f);
    }

    /// <summary>날·미끄럼판의 빨강(Damage 재질 Renderer)을 켜고 끈다. 끌 때는 Renderer를 끈다(알파 블렌드 재질은 밝기 0이 검은 선으로 남는다).</summary>
    public void SetDangerLights(bool on)
    {
        if (on == dangerOn) return;
        dangerOn = on;
        if (on)
        {
            foreach (var (renderer, enabled) in dangerParts)
                if (renderer != null) renderer.enabled = enabled;
            dangerParts.Clear();
            return;
        }
        dangerParts.Clear();
        var design = transform.Find("Design");
        if (design == null) return;
        foreach (var renderer in design.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer is ParticleSystemRenderer) continue;
            var material = renderer.sharedMaterial;
            if (material == null || !material.name.StartsWith("Damage", System.StringComparison.Ordinal)) continue;
            dangerParts.Add((renderer, renderer.enabled));
            renderer.enabled = false;
        }
    }

    /// <summary>분쇄 날 Animation의 빠르기(1 = 쫓아오는 동안, 0 = 멈춤).</summary>
    public void SetDrive(float speed)
    {
        if (animator != null && !Mathf.Approximately(animator.speed, speed)) animator.speed = speed;
    }

    /// <summary>날에서 계속 튀는 불꽃을 켜고 끈다. breach면 바닥·벽을 뚫는 동안의 많은 불꽃을 낸다.</summary>
    public void SetSparks(bool on, float gain, bool breach)
    {
        if (bladeSparks == null) return;
        bool play = on && gain > 0f;
        if (play != sparking)
        {
            sparking = play;
            if (play) bladeSparks.Play(true);
            else bladeSparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        if (!play) return;
        float rate = span * (breach ? breachSparkRate : bladeSparkRate);
        if (Mathf.Approximately(rate, sparkRate)) return;
        sparkRate = rate;
        var emission = bladeSparks.emission;
        emission.rateOverTime = rate;
    }

    // ───────────── 갈리는 효과 ─────────────
    /// <summary>날에 닿아 있는 발판 한 칸에서 그 프레임의 불꽃을 뿌린다. point는 분쇄선이 그 칸을 지나는 자리다.</summary>
    public void EmitContact(Vector2 point, Vector2 forward, float delta, float gain)
    {
        if (grindSparks == null || gain <= 0f) return;
        contactCarry += contactSparkRate * delta * gain;
        int count = (int)contactCarry;
        if (count <= 0) return;
        contactCarry -= count;
        Scatter(grindSparks, point, forward, count, .45f, 1.6f, 5.5f, 2.6f, null);
    }

    /// <summary>발판 한 줄이 갈려 떨어져 나오는 순간의 조각·불꽃·먼지.</summary>
    public void EmitSlabBreak(Vector2 point, Vector2 forward, float gain)
    {
        if (gain <= 0f) return;
        Scatter(chips, point, forward, 7, .5f, 2.2f, 6.5f, 3.2f, slabChipColor);
        Scatter(grindSparks, point, forward, 9, .5f, 2.5f, 7.5f, 3.4f, null);
        Scatter(dust, point, forward, 2, .4f, .4f, 1.4f, .9f, null);
    }

    /// <summary>적이 갈려 흩어지는 순간의 조각·불꽃·먼지.</summary>
    public void EmitShred(Vector2 point, Vector2 forward, float gain)
    {
        if (gain <= 0f) return;
        Scatter(chips, point, forward, 14, .4f, 3f, 9f, 5f, enemyChipColor);
        Scatter(grindSparks, point, forward, 18, .4f, 3f, 9.5f, 5.5f, null);
        Scatter(dust, point, forward, 4, .4f, .5f, 1.8f, 1.2f, null);
    }

    // 입자를 나아가는 방향으로 튀어 오르게 뿌린다. 입자는 World 공간이라 몸체가 움직여도 그 자리에 남는다.
    private static void Scatter(ParticleSystem system, Vector2 point, Vector2 forward, int count, float spread,
        float speedMin, float speedMax, float sideSpeed, Color? color)
    {
        if (system == null || count <= 0) return;
        Vector2 side = new Vector2(-forward.y, forward.x);
        var emit = new ParticleSystem.EmitParams();
        if (color.HasValue) emit.startColor = color.Value;
        for (int i = 0; i < count; i++)
        {
            Vector2 position = point + side * Random.Range(-spread, spread) + forward * Random.Range(-.1f, .1f);
            Vector2 velocity = forward * Random.Range(speedMin, speedMax) + side * Random.Range(-sideSpeed, sideSpeed);
            emit.position = new Vector3(position.x, position.y, system.transform.position.z);
            emit.velocity = velocity;
            system.Emit(emit, 1);
        }
    }
}
