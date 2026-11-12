using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 지형 패널 연출에서 패널 뒤에 보이는 것(수납구와 기계팔)과 맞물림 섬광을 그린다.
// 움직이는 패널이 수백 장이어도 Renderer 두 개(뒤: 수납구·팔, 앞: 섬광)의 Mesh를 매 프레임 다시 채워 한 번에 그린다.
// 패널 자체(칸 그림과 테두리 선)는 WaveTerrainCellView가 그린다.
[DisallowMultipleComponent]
public sealed class WaveTerrainPanelRig : MonoBehaviour
{
    // 선형 색. Wave 환경 장치와 같은 흑연색 몸체에 흰 발광 포인트를 쓴다.
    private static readonly Color BayFloor = new Color(.006f, .007f, .009f, 1f);
    private static readonly Color BayTop = new Color(.014f, .016f, .020f, 1f);
    private static readonly Color BaySide = new Color(.024f, .026f, .031f, 1f);
    private static readonly Color BayBottom = new Color(.036f, .039f, .046f, 1f);
    private static readonly Color BayRim = new Color(.20f, .21f, .23f, 1f);
    private static readonly Color ArmBody = new Color(.062f, .068f, .080f, 1f);
    private static readonly Color ArmEdge = new Color(.40f, .41f, .44f, 1f);
    private static readonly Color ArmRod = new Color(.27f, .28f, .31f, 1f);
    private static readonly Color JointBody = new Color(.030f, .033f, .039f, 1f);
    private static readonly Color JointGlow = new Color(1.7f, 1.8f, 1.9f, 1f);
    /// <summary>맞물림 섬광의 색. 일반 지형은 지형 선의 초록, 생성 외형·OneWay는 그 발판 발광의 주황 계열이다.</summary>
    public static readonly Color TerrainFlash = new Color(.55f, 1f, .62f, 1f);
    public static readonly Color GeneratedFlash = new Color(1f, .66f, .34f, 1f);
    private const float InnerBay = .68f;        // 수납구 깊은 면의 크기(바깥 테두리에 대한 비율)
    private const float SegmentLength = .27f;   // 팔 한 마디 길이(칸)

    private sealed class Layer
    {
        public Mesh mesh;
        public MeshRenderer renderer;
        public readonly List<Vector3> vertices = new();
        public readonly List<Color> colors = new();
        public readonly List<int> triangles = new();
        public bool filled;
    }
    private Layer back, front;
    public int PanelCount { get; private set; }

    public void Configure(Material body, Material flash, int sortingLayer, int backOrder, int frontOrder)
    {
        back ??= Create("PanelRigBack");
        front ??= Create("PanelRigFront");
        back.renderer.sharedMaterial = body; back.renderer.sortingLayerID = sortingLayer; back.renderer.sortingOrder = backOrder;
        front.renderer.sharedMaterial = flash; front.renderer.sortingLayerID = sortingLayer; front.renderer.sortingOrder = frontOrder;
        back.renderer.enabled = body != null;
        front.renderer.enabled = flash != null;
    }
    private Layer Create(string name)
    {
        var node = new GameObject(name);
        node.transform.SetParent(transform, false);
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.MarkDynamic();
        node.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = node.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        return new Layer { mesh = mesh, renderer = renderer };
    }

    public void Begin()
    {
        PanelCount = 0;
        if (back == null) return;
        Reset(back); Reset(front);
    }
    private static void Reset(Layer layer) { layer.vertices.Clear(); layer.colors.Clear(); layer.triangles.Clear(); }

    // cell: 패널 중심을 원점으로 하는 좌표를 이 Rig의 로컬 좌표로 옮기는 행렬. half: 패널의 반 크기(한 칸은 .5, .5, OneWay는 얇은 판).
    public void Add(Matrix4x4 cell, in WaveTerrainPanelMotion.Pose pose, uint seed, Vector2 half, Color flash)
    {
        if (back == null) return;
        PanelCount++;
        if (pose.bay > .001f) Bay(cell, pose.bay, half);
        if (pose.arm > .001f && pose.bay > .001f) Arm(cell, pose, seed, half);
        if (pose.flash > .001f) Flash(cell, pose, half, flash);
    }

    public void End()
    {
        if (back == null) return;
        Upload(back); Upload(front);
    }
    public void Clear()
    {
        if (back == null) return;
        Begin(); End();
    }
    private static void Upload(Layer layer)
    {
        if (layer.vertices.Count == 0 && !layer.filled) return;
        layer.mesh.Clear();
        layer.mesh.SetVertices(layer.vertices);
        layer.mesh.SetColors(layer.colors);
        layer.mesh.SetTriangles(layer.triangles, 0, false);
        // 방 어디에서 움직여도 잘리지 않게 넉넉히 잡는다(화면 밖 패널은 호출 쪽에서 넣지 않는다).
        layer.mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        layer.filled = layer.vertices.Count > 0;
    }

    // 패널이 고정된 뒤 이웃과 맞닿은 이음매 선이 잠깐 남았다가 사라진다(가산). mask는 위·오른쪽·아래·왼쪽 순서의 비트다.
    public void AddSeam(Matrix4x4 cell, int mask, Color color, float inset, float width)
    {
        if (front == null || mask == 0) return;
        float half = .5f - inset;
        Vector2 a = new Vector2(-half, -half), b = new Vector2(half, -half), c = new Vector2(half, half), d = new Vector2(-half, half);
        if ((mask & 1) != 0) Stroke(front, cell, d, c, width, width, color);
        if ((mask & 2) != 0) Stroke(front, cell, b, c, width, width, color);
        if ((mask & 4) != 0) Stroke(front, cell, a, b, width, width, color);
        if ((mask & 8) != 0) Stroke(front, cell, a, d, width, width, color);
    }

    // 수납구: 패널이 빠진 자리의 벽 안쪽. 안쪽 네 벽을 밝기가 다른 사다리꼴로 그려 깊이가 보이게 한다.
    private void Bay(Matrix4x4 cell, float open, Vector2 half)
    {
        Vector2 outer = half * open, inner = half * (InnerBay * open);
        Vector2 a = new Vector2(-outer.x, -outer.y), b = new Vector2(outer.x, -outer.y), c = new Vector2(outer.x, outer.y), d = new Vector2(-outer.x, outer.y);
        Vector2 ia = new Vector2(-inner.x, -inner.y), ib = new Vector2(inner.x, -inner.y), ic = new Vector2(inner.x, inner.y), id = new Vector2(-inner.x, inner.y);
        Quad(back, cell, a, b, ib, ia, BayBottom);
        Quad(back, cell, b, c, ic, ib, BaySide);
        Quad(back, cell, c, d, id, ic, BayTop);
        Quad(back, cell, d, a, ia, id, BaySide);
        Quad(back, cell, ia, ib, ic, id, BayFloor);
        float rim = .018f * open;
        Stroke(back, cell, a, b, rim, rim, BayRim); Stroke(back, cell, b, c, rim, rim, BayRim);
        Stroke(back, cell, c, d, rim, rim, BayRim); Stroke(back, cell, d, a, rim, rim, BayRim);
    }

    // 기계팔: 수납구 깊은 면의 어깨에서 패널 뒷면까지 두 마디. 위팔은 굵은 실린더, 아래팔은 가는 로드다.
    private void Arm(Matrix4x4 cell, in WaveTerrainPanelMotion.Pose pose, uint seed, Vector2 half)
    {
        Vector2 direction = WaveTerrainPanelMotion.Direction(seed);
        // 얇은 수납구(OneWay)에서는 어깨가 수납구 밖으로 나가지 않게 세로를 줄인다.
        Vector2 shoulder = Vector2.Scale(-direction * (.11f * pose.bay), new Vector2(1f, Mathf.Clamp01(half.y / Mathf.Max(.0001f, half.x))));
        Vector2 wrist = pose.offset;
        Vector2 elbow = Elbow(shoulder, wrist, WaveTerrainPanelMotion.Sign(seed, 3));
        float near = Mathf.InverseLerp(WaveTerrainPanelMotion.StowedScale, 1f, Mathf.Min(pose.scale, 1f));
        float visible = pose.arm;
        Color Shade(Color color, float depth) { color.r *= depth; color.g *= depth; color.b *= depth; color.a *= visible; return color; }
        // 깊은 쪽(어깨)은 어둡고 패널 쪽은 밝다.
        Stroke(back, cell, shoulder, elbow, .082f, .070f, Shade(ArmBody, .75f));
        Stroke(back, cell, shoulder, elbow, .020f, .018f, Shade(ArmEdge, .55f));
        float rod = Mathf.Lerp(.040f, .052f, near);
        Stroke(back, cell, elbow, wrist, rod, rod, Shade(ArmRod, Mathf.Lerp(.7f, 1f, near)));
        Box(back, cell, shoulder, .050f, 45f, Shade(JointBody, 1f));
        Box(back, cell, elbow, .046f, 45f, Shade(JointBody, 1f));
        Box(back, cell, elbow, .020f, 45f, Shade(JointGlow, 1f));
        float plate = Mathf.Lerp(.055f, .075f, near);
        Box(back, cell, wrist, plate, pose.angle, Shade(ArmBody, 1f));
        Box(back, cell, wrist, plate * .42f, pose.angle, Shade(JointGlow, .8f));
    }

    // 두 마디 길이로 닿는 팔꿈치 위치. 멀면 곧게 펴고, 가까우면 한쪽으로 꺾는다.
    private static Vector2 Elbow(Vector2 shoulder, Vector2 wrist, float bend)
    {
        Vector2 span = wrist - shoulder;
        float distance = span.magnitude;
        if (distance < .0001f) return shoulder + new Vector2(0f, SegmentLength * bend);
        Vector2 along = span / distance;
        Vector2 across = new Vector2(-along.y, along.x) * bend;
        if (distance >= SegmentLength * 2f) return shoulder + span * .5f;
        float half = distance * .5f;
        return shoulder + along * half + across * Mathf.Sqrt(SegmentLength * SegmentLength - half * half);
    }

    // 맞물리거나 풀리는 순간 패널 전체가 한 번 밝아진다(가산).
    private void Flash(Matrix4x4 cell, in WaveTerrainPanelMotion.Pose pose, Vector2 half, Color tint)
    {
        Vector2 extent = half * (1.04f * pose.scale);
        var color = tint * (pose.flash * .55f);
        color.a = 1f;
        var turn = Quaternion.Euler(0f, 0f, pose.angle);
        Vector2 center = pose.offset;
        Vector2 Corner(float x, float y) => center + (Vector2)(turn * new Vector3(x * extent.x, y * extent.y));
        Quad(front, cell, Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1), color);
    }

    private static void Box(Layer layer, Matrix4x4 cell, Vector2 center, float half, float angle, Color color)
    {
        var turn = Quaternion.Euler(0f, 0f, angle);
        Vector2 Corner(float x, float y) => center + (Vector2)(turn * new Vector3(x * half, y * half));
        Quad(layer, cell, Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1), color);
    }
    // 두 점을 잇는 띠. 양 끝의 굵기를 따로 줄 수 있다.
    private static void Stroke(Layer layer, Matrix4x4 cell, Vector2 from, Vector2 to, float fromWidth, float toWidth, Color color)
    {
        Vector2 along = to - from;
        if (along.sqrMagnitude < 1e-10f) return;
        Vector2 across = new Vector2(-along.y, along.x).normalized;
        Quad(layer, cell, from - across * (fromWidth * .5f), to - across * (toWidth * .5f), to + across * (toWidth * .5f), from + across * (fromWidth * .5f), color);
    }
    private static void Quad(Layer layer, Matrix4x4 cell, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int index = layer.vertices.Count;
        layer.vertices.Add(cell.MultiplyPoint3x4(a)); layer.vertices.Add(cell.MultiplyPoint3x4(b));
        layer.vertices.Add(cell.MultiplyPoint3x4(c)); layer.vertices.Add(cell.MultiplyPoint3x4(d));
        layer.colors.Add(color); layer.colors.Add(color); layer.colors.Add(color); layer.colors.Add(color);
        layer.triangles.Add(index); layer.triangles.Add(index + 1); layer.triangles.Add(index + 2);
        layer.triangles.Add(index); layer.triangles.Add(index + 2); layer.triangles.Add(index + 3);
    }

    private void OnDestroy()
    {
        if (back != null && back.mesh != null) Destroy(back.mesh);
        if (front != null && front.mesh != null) Destroy(front.mesh);
    }
}
