using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추격 분쇄기 몸체에서 띠 폭(span)에 따라 모양이 달라지는 부분(어둠·몸체·소켓·양 끝 미끄럼판)의 벡터 형상이다.
/// 생성 도구(tools/Unity/ChasingGrinderSetup)가 Prefab을 만들 때와, 격자 편집기에서 폭을 바꾼 분쇄기가 실행 중에
/// 몸체를 그 폭에 맞출 때(ChasingGrinderBody.Fit) 같은 함수를 쓴다. 늘이지 않고 그 폭으로 다시 그린다.
/// 톱날·드릴·기어처럼 폭과 무관한 부품은 Prefab에 든 것을 옮기거나 복제해 쓴다.
/// 좌표는 몸체 기준이다(+Y가 나아가는 방향, 원점은 분쇄선 가운데).
/// </summary>
public static class ChasingGrinderLayout
{
    public const float OutlineWidth = .05f;     // 다른 환경 도구와 같은 외곽선 굵기
    public const float HousingFront = -3f;      // 몸체 앞면. 분쇄선에서 여기까지가 분쇄 날이 드러난 띠다
    public const float HousingBack = -4.4f;
    public const float VoidDepth = 46f;         // 몸체 뒤로 이어지는 어둠의 길이(화면 높이보다 길게)
    public const float UnitPitch = 2.6f;        // 톱날 한 개가 맡는 띠 길이의 기준
    public const float TipInset = .05f;         // 날 끝의 외곽선이 분쇄선을 넘지 않게 날을 이만큼 뒤에 둔다
    public const float GearY = HousingFront - .96f;
    /// <summary>몸체 하나가 받을 수 있는 앞줄 톱날 수. 분쇄 날 Animation에 이 수만큼의 곡선이 들어 있다.</summary>
    public const int MaxSaws = 24;
    /// <summary>띠 폭의 범위(칸). 격자 편집기의 폭 설정도 이 안에서 고른다.</summary>
    public const float MinSpan = 4f, MaxSpan = 60f;

    // 정렬 순서(BaseOrder에 더한다). 몸체 → 뒷줄 톱날 → 드릴 실린더·소켓 → 앞줄 톱날 → 드릴 순으로 겹친다.
    public const int OVoid = 0, ODepth = 1, OHousing = 2, ORearSaw = 8, ORod = 19, OSocket = 21, OSaw = 24, OSawSocket = 32, ODrill = 36;

    /// <summary>폭에 따라 다시 그리는 묶음. 몸체 Prefab의 Design 아래 같은 이름의 노드다.</summary>
    public enum Group { Back, Housing, Sockets, SawSockets }
    /// <summary>부품의 재질. 이름은 재질 에셋 이름과 같고, 합친 Mesh 노드의 이름(재질 이름 + 묶음 안의 정렬 순서)에 쓴다.</summary>
    public enum Ink { Interior, Outline, Detail, Cable, Danger, DangerFill, Void }

    private static readonly string[] inkNames = { "MachineInterior", "MachineOutline", "MachineDetail", "MachineCable", "DamageOutline", "DamageInterior", "GrinderVoid" };
    private static readonly string[] groupNames = { "Back", "Housing", "Sockets", "SawSockets" };

    public static string InkName(Ink ink) => inkNames[(int)ink];
    public static string GroupName(Group group) => groupNames[(int)group];
    public static int BaseOrder(Group group) => group switch
    {
        Group.Housing => OHousing,
        Group.Sockets => OSocket,
        Group.SawSockets => OSawSocket,
        _ => OVoid
    };
    /// <summary>합친 Mesh 노드의 이름. 생성 도구가 붙인 이름과 같다.</summary>
    public static string Label(Group group, Ink ink, int order, string name) => name ?? InkName(ink) + (order - BaseOrder(group));

    public static float ClampSpan(float span) => Mathf.Clamp(float.IsFinite(span) ? span : MinSpan, MinSpan, MaxSpan);
    /// <summary>앞줄 톱날 수. 드릴과 뒷줄 톱날은 그 사이마다 하나씩(SawCount - 1개) 놓인다.</summary>
    public static int SawCount(float span) => Mathf.Clamp(Mathf.RoundToInt(span / UnitPitch), 2, MaxSaws);
    /// <summary>앞줄 톱날 i의 가로 위치.</summary>
    public static float SawX(float span, int index)
    {
        int saws = SawCount(span);
        float pitch = span / saws, half = span * .5f;
        return -half + pitch * (index + .5f);
    }
    /// <summary>톱날 사이 j(1부터)의 가로 위치. 드릴·뒷줄 톱날·작은 기어가 놓인다.</summary>
    public static float JointX(float span, int index)
    {
        int saws = SawCount(span);
        float pitch = span / saws, half = span * .5f;
        return -half + pitch * index;
    }

    /// <summary>부품 하나를 받는다. uv가 null이면 모두 0이다. name이 있으면 그 이름의 노드로 따로 합친다.</summary>
    public delegate void Sink(Group group, Ink ink, int order, List<Vector3> vertices, List<int> triangles, List<Vector2> uv, string name);

    /// <summary>폭에 따라 달라지는 부품을 생성 도구와 같은 순서로 내보낸다. 같은 묶음·재질·순서의 부품은 받는 쪽이 이 순서대로 이어 붙인다.</summary>
    public static void Build(float span, Sink sink)
    {
        int saws = SawCount(span);
        float pitch = span / saws, half = span * .5f;

        // 몸체 뒤는 통로를 가득 채운 어둠이다. 분쇄기가 지나간 자리는 다시 보이지 않는다.
        Shape(sink, Group.Back, Box(-half, -VoidDepth, half, HousingFront), Ink.Void, OVoid);
        Depth(sink, half, pitch);
        Housing(sink, half, pitch, saws);
        // 앞줄 톱날의 팔이 드나드는 소켓.
        for (int i = 0; i < saws; i++)
        {
            var at = new Vector2(-half + pitch * (i + .5f), 0);
            Frame(sink, Group.SawSockets, At(Chamfer(-.24f, HousingFront - .5f, .24f, HousingFront + .16f, .05f), at), OSawSocket, .03f);
            Segments(sink, Group.SawSockets, At(new List<(Vector2, Vector2)> { (new Vector2(-.13f, HousingFront - .32f), new Vector2(.13f, HousingFront - .32f)) }, at), .02f, Ink.Detail, OSawSocket + 2);
        }
        // 드릴 실린더가 드나드는 소켓. 뒤에 놓인 뒷줄 톱날의 팔도 이 소켓으로 들어간다.
        for (int j = 1; j < saws; j++)
        {
            var at = new Vector2(-half + pitch * j, 0);
            Frame(sink, Group.Sockets, At(Chamfer(-.52f, HousingFront - .55f, .52f, HousingFront + .1f, .05f), at), OSocket, .03f);
            Segments(sink, Group.Sockets, At(new List<(Vector2, Vector2)> { (new Vector2(-.2f, HousingFront - .36f), new Vector2(.2f, HousingFront - .36f)) }, at), .02f, Ink.Detail, OSocket + 2);
        }
        foreach (int side in new[] { -1, 1 }) EndShoe(sink, side, half);
    }

    /// <summary>
    /// 이 폭의 부품을 묶음·재질·순서별 Mesh로 합친다. 열쇠는 (묶음, 노드 이름)이다. 생성 도구의 Mesh 에셋과 같은 정점·삼각형·UV를 만든다.
    /// </summary>
    public static Dictionary<(Group group, string label), Mesh> BuildMeshes(float span, string meshName)
    {
        var order = new List<(Group group, string label)>();
        var parts = new Dictionary<(Group, string), (List<Vector3> vertices, List<int> triangles, List<Vector2> uv)>();
        Build(span, (group, ink, sortOrder, vertices, triangles, uv, name) =>
        {
            var key = (group, Label(group, ink, sortOrder, name));
            if (!parts.TryGetValue(key, out var batch))
            {
                parts[key] = batch = (new List<Vector3>(), new List<int>(), new List<Vector2>());
                order.Add(key);
            }
            int first = batch.vertices.Count;
            batch.vertices.AddRange(vertices);
            foreach (int index in triangles) batch.triangles.Add(first + index);
            if (uv != null) batch.uv.AddRange(uv); else for (int i = 0; i < vertices.Count; i++) batch.uv.Add(Vector2.zero);
        });
        var result = new Dictionary<(Group, string), Mesh>();
        foreach (var key in order)
        {
            var batch = parts[key];
            var mesh = new Mesh { name = meshName + "_" + GroupName(key.Item1) + "_" + key.Item2 };
            mesh.SetVertices(batch.vertices); mesh.SetTriangles(batch.triangles, 0); mesh.SetUVs(0, batch.uv);
            mesh.RecalculateBounds(); mesh.RecalculateNormals();
            result[key] = mesh;
        }
        return result;
    }

    // ───────────── 폭에 따라 달라지는 부품 ─────────────
    // 어둠 속으로 사라지는 뼈대. 몸체 뒤로 깊이가 있다는 것만 알려 주는 어두운 사선이다.
    private static void Depth(Sink sink, float half, float pitch)
    {
        var beams = new List<(Vector2, Vector2)>();
        const float top = HousingBack, bottom = HousingBack - 11f, lean = .62f;
        float run = (top - bottom) * lean;
        for (float x = -half - run + pitch * .75f; x < half; x += pitch * 1.5f)
        {
            // 통로 밖으로 나가는 부분은 잘라 낸다.
            float x0 = x, y0 = top, x1 = x + run, y1 = bottom;
            if (x0 < -half) { y0 = Mathf.Lerp(top, bottom, (-half - x0) / run); x0 = -half; }
            if (x1 > half) { y1 = Mathf.Lerp(top, bottom, (half - x) / run); x1 = half; }
            if (x1 - x0 > .3f) beams.Add((new Vector2(x0, y0), new Vector2(x1, y1)));
        }
        Segments(sink, Group.Back, beams, .12f, Ink.Outline, ODepth);
        var ribs = new List<(Vector2, Vector2)>
        {
            (new Vector2(-half, HousingBack - 3.6f), new Vector2(half, HousingBack - 3.6f)),
            (new Vector2(-half, HousingBack - 8.2f), new Vector2(half, HousingBack - 8.2f))
        };
        Segments(sink, Group.Back, ribs, .07f, Ink.Outline, ODepth);
    }

    // 분쇄 날을 밀어내는 몸체. 어두운 본체에 흰 발광선·레일·방열 핀·전원선을 둔다(구동 기어는 Prefab의 기어를 옮겨 쓴다).
    private static void Housing(Sink sink, float half, float pitch, int saws)
    {
        Frame(sink, Group.Housing, Chamfer(-half, HousingBack, half, HousingFront, .12f), OHousing, OutlineWidth);
        // 앞면의 흰 발광선은 분쇄 날이 드나드는 소켓 자리마다 끊긴다.
        var gaps = new List<(float x, float w)>();
        for (int i = 0; i < saws; i++) gaps.Add((-half + pitch * (i + .5f), .3f));
        for (int j = 1; j < saws; j++) gaps.Add((-half + pitch * j, .58f));
        gaps.Sort((a, b) => a.x.CompareTo(b.x));
        var lines = new List<(Vector2, Vector2)>();
        float cursor = -half + .5f;
        foreach (var gap in gaps)
        {
            if (gap.x - gap.w > cursor + .08f) lines.Add((new Vector2(cursor, HousingFront - .13f), new Vector2(gap.x - gap.w, HousingFront - .13f)));
            cursor = gap.x + gap.w;
        }
        if (half - .5f > cursor + .08f) lines.Add((new Vector2(cursor, HousingFront - .13f), new Vector2(half - .5f, HousingFront - .13f)));
        Segments(sink, Group.Housing, lines, .022f, Ink.Detail, OHousing + 2);
        // 어둠 위에서도 몸체의 윤곽이 읽히도록 뒷면과 양 끝에 가는 흰 발광선을 두르고, 가운데에는 레일과 눈금을 둔다.
        const float railY = HousingFront - .62f, cableY = HousingBack + .22f;
        var trim = new List<(Vector2, Vector2)>
        {
            (new Vector2(-half + .62f, HousingBack + .09f), new Vector2(half - .62f, HousingBack + .09f)),
            (new Vector2(-half + .62f, HousingBack + .09f), new Vector2(-half + .62f, HousingFront - .13f)),
            (new Vector2(half - .62f, HousingBack + .09f), new Vector2(half - .62f, HousingFront - .13f)),
            (new Vector2(-half + .5f, railY), new Vector2(half - .5f, railY))
        };
        for (float x = -half + pitch * .25f; x < half - .2f; x += pitch * .25f)
            trim.Add((new Vector2(x, railY), new Vector2(x, railY + (Mathf.Abs(Mathf.Repeat(x + half, pitch) - pitch * .5f) < .02f ? .16f : .08f))));
        Segments(sink, Group.Housing, trim, .014f, Ink.Detail, OHousing + 2);
        // 뒷면 방열 핀
        var fins = new List<(Vector2, Vector2)>();
        for (float x = -half + .35f; x < half - .3f; x += .26f) fins.Add((new Vector2(x, HousingBack - .22f), new Vector2(x, HousingBack - .02f)));
        Segments(sink, Group.Housing, fins, .06f, Ink.Outline, OHousing + 1);
        // 전원선은 몸체 뒤쪽을 따라 흐른다. 흐름은 Animation이 재질 오프셋으로 움직인다.
        var wire = new[] { new Vector2(-half + .55f, cableY), new Vector2(half - .55f, cableY) };
        Stroke(sink, Group.Housing, wire, .05f, Ink.Interior, OHousing, false);
        Stroke(sink, Group.Housing, wire, .026f, Ink.Cable, OHousing + 3, false, "PowerCable");
    }

    // 띠의 양 끝: 벽을 타고 오르는 미끄럼판과 벽 쪽 틈을 메우는 긁개 날. 왼쪽 끝 기준으로 만들고 오른쪽은 좌우를 뒤집는다.
    private static void EndShoe(Sink sink, int side, float half)
    {
        Vector2 Place(Vector2 point) => new Vector2(side < 0 ? -half + point.x : half - point.x, point.y);
        Vector2[] Placed(Vector2[] points)
        {
            var result = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++) result[i] = Place(points[i]);
            return result;
        }
        Frame(sink, Group.Sockets, Placed(Chamfer(0f, HousingBack - .5f, .46f, -.42f, .08f)), OSocket, OutlineWidth);
        Segments(sink, Group.Sockets, new List<(Vector2, Vector2)> { (Place(new Vector2(.12f, HousingBack - .2f)), Place(new Vector2(.12f, -.75f))) }, .022f, Ink.Detail, OSocket + 2);
        var scraper = Placed(new[] { new Vector2(.04f, -.44f), new Vector2(.44f, -.44f), new Vector2(.11f, -.02f - TipInset) });
        Shape(sink, Group.Sockets, scraper, Ink.DangerFill, OSocket + 2);
        Stroke(sink, Group.Sockets, scraper, .03f, Ink.Danger, OSocket + 3, true);
    }

    // ───────────── 형상 ─────────────
    public static Vector2[] Box(float left, float bottom, float right, float top)
        => new[] { new Vector2(left, bottom), new Vector2(left, top), new Vector2(right, top), new Vector2(right, bottom) };
    public static Vector2[] Chamfer(float left, float bottom, float right, float top, float cut)
        => new[] { new Vector2(left + cut, bottom), new Vector2(left, bottom + cut), new Vector2(left, top - cut), new Vector2(left + cut, top),
            new Vector2(right - cut, top), new Vector2(right, top - cut), new Vector2(right, bottom + cut), new Vector2(right - cut, bottom) };
    // 여러 자리에 되풀이해 놓는 부품의 좌표를 옮긴다.
    public static Vector2[] At(Vector2[] points, Vector2 offset)
    {
        var result = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++) result[i] = points[i] + offset;
        return result;
    }
    public static List<(Vector2 a, Vector2 b)> At(List<(Vector2 a, Vector2 b)> segments, Vector2 offset)
    {
        var result = new List<(Vector2 a, Vector2 b)>(segments.Count);
        foreach (var segment in segments) result.Add((segment.a + offset, segment.b + offset));
        return result;
    }

    private static void Frame(Sink sink, Group group, Vector2[] points, int order, float width)
    {
        Shape(sink, group, points, Ink.Interior, order);
        Stroke(sink, group, points, width, Ink.Outline, order + 1, true);
    }

    /// <summary>선분마다 굵기만큼의 사각형.</summary>
    public static void SegmentMesh(List<(Vector2 a, Vector2 b)> segments, float width, List<Vector3> vertices, List<int> indices)
    {
        foreach (var segment in segments)
        {
            var axis = (segment.b - segment.a).normalized; var n = new Vector2(-axis.y, axis.x) * width * .5f; int v = vertices.Count;
            vertices.Add(segment.a + n); vertices.Add(segment.a - n); vertices.Add(segment.b + n); vertices.Add(segment.b - n);
            indices.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
        }
    }
    private static void Segments(Sink sink, Group group, List<(Vector2 a, Vector2 b)> segments, float width, Ink ink, int order)
    {
        var vertices = new List<Vector3>(); var indices = new List<int>();
        SegmentMesh(segments, width, vertices, indices);
        sink(group, ink, order, vertices, indices, null, null);
    }

    /// <summary>다각형을 중앙 팬으로 채운다. 볼록하거나 중심에서 별 모양인 다각형만 넣는다.</summary>
    public static void ShapeMesh(Vector2[] points, List<Vector3> vertices, List<int> indices)
    {
        var sum = Vector2.zero;
        foreach (var point in points) sum += point;
        vertices.Add(sum / points.Length);
        foreach (var point in points) vertices.Add(point);
        for (int i = 0; i < points.Length; i++) { indices.Add(0); indices.Add(1 + i); indices.Add(1 + (i + 1) % points.Length); }
    }
    private static void Shape(Sink sink, Group group, Vector2[] points, Ink ink, int order)
    {
        var vertices = new List<Vector3>(); var indices = new List<int>();
        ShapeMesh(points, vertices, indices);
        sink(group, ink, order, vertices, indices, null, null);
    }

    /// <summary>꺾인 선을 굵기만큼의 띠로 만든다. UV.x는 선을 따라 잰 길이, UV.y는 0(한쪽)·1(다른 쪽)이다.</summary>
    public static void StrokeMesh(IEnumerable<Vector2[]> lines, float width, bool closed, List<Vector3> vertices, List<int> indices, List<Vector2> uv)
    {
        foreach (var points in lines)
        {
            int first = vertices.Count; float length = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (i > 0) length += Vector2.Distance(points[i - 1], points[i]);
                uv.Add(new Vector2(length, 0)); uv.Add(new Vector2(length, 1));
                Vector2 prev = i > 0 ? points[i - 1] : closed ? points[points.Length - 1] : points[i];
                Vector2 next = i < points.Length - 1 ? points[i + 1] : closed ? points[0] : points[i];
                Vector2 incoming = (points[i] - prev).normalized, outgoing = (next - points[i]).normalized;
                if (incoming == Vector2.zero) incoming = outgoing;
                if (outgoing == Vector2.zero) outgoing = incoming;
                Vector2 a = new Vector2(-incoming.y, incoming.x), b = new Vector2(-outgoing.y, outgoing.x);
                Vector2 normal = (a + b).normalized; float amount = width * .5f / Mathf.Max(.3f, Vector2.Dot(normal, b));
                vertices.Add(points[i] + normal * amount); vertices.Add(points[i] - normal * amount);
            }
            for (int i = 0; i < (closed ? points.Length : points.Length - 1); i++)
            {
                int a = first + i * 2, b = first + ((i + 1) % points.Length) * 2;
                indices.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        }
    }
    private static void Stroke(Sink sink, Group group, Vector2[] points, float width, Ink ink, int order, bool closed, string name = null)
    {
        var vertices = new List<Vector3>(); var indices = new List<int>(); var uv = new List<Vector2>();
        StrokeMesh(new[] { points }, width, closed, vertices, indices, uv);
        sink(group, ink, order, vertices, indices, uv, name);
    }
}
