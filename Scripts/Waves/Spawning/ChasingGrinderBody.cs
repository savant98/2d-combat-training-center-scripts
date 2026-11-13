using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추격 분쇄기(ChasingGrinder)의 보이는 몸체다. 판정과 추격은 ChasingGrinder가 맡고, 여기서는 그 위치에 놓여
/// 분쇄 날의 Animation(공회전·앞뒤 왕복)을 돌리고 불꽃과 갈린 조각을 뿌린다.
/// 이 개체의 +Y가 분쇄기가 나아가는 방향이고, 원점은 분쇄선(분쇄 날이 가장 멀리 나왔을 때의 끝) 가운데다.
/// 풀이 분쇄기 개체를 여러 개 만들어 두어도 무거운 몸체는 Prefab마다 필요한 수만 만들어 빌려 쓴다.
/// 격자 편집기에서 띠 폭을 바꾼 분쇄기는 빌릴 때 몸체를 그 폭에 맞춘다(Fit). 늘이지 않고, 폭에 따라 달라지는 몸체·소켓은
/// ChasingGrinderLayout으로 다시 그리고 톱날·드릴·기어는 간격에 맞춰 옮기거나 복제한다. Prefab의 폭이면 아무것도 바꾸지 않는다.
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

    /// <summary>몸체를 끄고 다음에 다시 쓸 수 있게 돌려준다.</summary>
    public void Release()
    {
        IsRented = false;
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
