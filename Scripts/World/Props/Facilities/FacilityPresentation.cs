using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 시설의 반복 소리(구동음·웅웅거림). 카메라와의 거리로 음량을 줄이고(DungeonPropFeedback와 같은 7~24칸 규칙 — 7차: 가까운·먼 거리를 따로 받는 오버로드), 들리지 않으면 음원을 반납한다.
/// SoundManager.PlayLoop 경로를 그대로 쓰므로 효과음 음량 설정·믹서를 따른다. 새 음원 파일은 쓰지 않는다(임시 기존 효과음).
/// 7차 V3(겹침 감쇠 결함 수정): SoundManager.PlayLoop는 같은 Clip이 이미 k개 울리면 시작 음량에 crowdedVolumeMultiplier^k를 곱한다. 예전에는 다음 프레임부터
/// source.volume = 음량 × 거리 배율로 덮어써 그 겹침 배율이 사라졌다. 이제 시작 직후 그 배율(CrowdScale)을 기억하고 매 프레임 함께 곱한다(멈췄다 다시 켜면 다시 잰다).
/// 겹침 배율은 시작 순간의 동시 재생 수로 정해진다(SoundManager와 같은 규칙 — 먼저 울리던 소리가 멈춰도 이미 울리는 소리는 그대로).
/// </summary>
public sealed class FacilityLoopSound
{
    private SoundHandle handle;
    private SoundOptions playing;
    private float crowd = 1f;

    public bool IsPlaying => handle != null && handle.IsValid;
    /// <summary>7차 V3: 지금 재생에 곱하는 겹침 배율(시작 때 SoundManager가 정한 값, 재생 중이 아니면 1 — 검사용).</summary>
    public float CrowdScale => crowd;
    /// <summary>지금 음원 음량(재생 중이 아니면 0 — 검사용).</summary>
    public float CurrentVolume
    {
        get
        {
            var source = IsPlaying ? handle.GetSource() : null;
            return source != null ? source.volume : 0f;
        }
    }

    /// <summary>매 프레임 부른다. active가 false이거나 멀면 멈춘다. level은 0~1 추가 배율(가속·감속). 거리 감쇠는 기본 7~24칸.</summary>
    public void Update(SoundOptions options, Vector3 position, bool active, float level = 1f)
        => Update(options, position, active, level, DungeonPropFeedback.NearDistance, DungeonPropFeedback.FarDistance);

    /// <summary>
    /// 7차 V3: 거리 감쇠 범위를 따로 받는다(near 안 원음, far 밖 정지, 사이 smoothstep — DungeonPropFeedback.DistanceGain 오버로드).
    /// 텔레포트 받침 웅웅거림은 받침 근처 4~14칸만 들리게 한다(DungeonTeleporter.humNear·humFar).
    /// </summary>
    public void Update(SoundOptions options, Vector3 position, bool active, float level, float near, float far)
    {
        float gain = active && options != null && options.clip != null ? DungeonPropFeedback.DistanceGain(position, near, far) * Mathf.Clamp01(level) : 0f;
        if (gain <= .001f)
        {
            Stop();
            return;
        }
        if (playing != options) Stop();
        if (!IsPlaying)
        {
            if (SoundManager.Instance == null) return;
            handle = SoundManager.Instance.PlayLoop(options, position);
            playing = options;
            if (handle == null) return;
            // SoundManager가 시작 음량에 곱한 겹침 배율(같은 Clip이 먼저 k개 울리면 crowdedVolumeMultiplier^k)을 기억한다.
            var started = handle.GetSource();
            crowd = started != null && options.volume > 1e-4f ? Mathf.Clamp01(started.volume / options.volume) : 1f;
        }
        handle.SetPosition(position);
        var source = handle.GetSource();
        if (source != null) source.volume = options.volume * gain * crowd;
    }

    public void Stop()
    {
        if (handle != null && handle.IsValid) handle.Stop();
        handle = null;
        playing = null;
        crowd = 1f;
    }
}

/// <summary>
/// 발광 부품 묶음의 밝기(전원 켜짐·방전 표시). DungeonPropGlow로 MaterialPropertyBlock 밝기를 바꾸고, 거의 0이면 Renderer를 끈다
/// (WaveTools 계열 재질은 알파 블렌드라 밝기 0이 검은 선으로 남는다 — WAVE_ENVIRONMENT_DESIGN).
/// 밝기가 0보다 크면 Prefab에 꺼 둔 채 저장한 Renderer(빛 기둥·스캔 빔·강조선처럼 평소 숨기는 부품)도 켠다. Restore는 Prefab에 저장된 켜짐 상태로 되돌린다.
/// (3차 수정: 2차에서는 꺼 둔 채 저장한 Renderer를 영영 켜지 않아 텔레포트 빛 기둥·수리 스캔 빔이 보이지 않았다.)
/// </summary>
public sealed class FacilityLights
{
    private readonly Renderer[] renderers;
    private readonly DungeonPropGlow glow;
    private readonly bool[] home;     // Prefab에 저장된 켜짐(Restore가 되돌리는 값)
    private float last = float.NaN;

    public FacilityLights(Renderer[] targets)
    {
        renderers = targets ?? Array.Empty<Renderer>();
        glow = new DungeonPropGlow(renderers);
        home = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) home[i] = renderers[i] != null && renderers[i].enabled;
    }

    public int Count => renderers.Length;
    public float Level => last;

    public void Set(float level)
    {
        if (Mathf.Abs(level - last) < .002f) return;
        last = level;
        bool visible = level > .02f;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = visible;
        if (visible) glow.Set(level);
    }

    public void SetOne(int index, float level)
    {
        if (index < 0 || index >= renderers.Length || renderers[index] == null) return;
        bool visible = level > .02f;
        renderers[index].enabled = visible;
        if (visible) glow.SetOne(index, level);
        last = float.NaN;
    }

    /// <summary>재질 값으로 되돌리고 Renderer를 Prefab 상태로 켠다(풀 반환).</summary>
    public void Restore()
    {
        glow.Clear();
        for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = home[i];
        last = float.NaN;
    }
}

/// <summary>
/// 길이를 배치별로 바꾸는 시설(컨베이어·크레인 레일)의 표시 Mesh를 실행 중에 만든다.
/// 생성 도구가 만든 '한 칸 조각' Mesh(정점 x ∈ [0, 1], 왼쪽 끝 기준)를 이어 붙인다. 늘이기(Scale)가 아니라 같은 조각을 반복해 놓는 것이다.
/// 4차: 경로 크레인의 빛 통로(다각선 여러 가닥·띠·관절 고리)와 붙기 반경 고리(점선 원)도 배치 설정대로 실행 중에 새로 그린다(정점을 새로 채운다 — 크기 늘이기 아님).
/// 5차: 반경 고리는 크레인에서 쓰지 않는다(DashedRing은 남겨 둔다). 붙기 직선(흐르는 점선)·견인 띠·선로 침목·고정 크레인 망원 기둥을 더했다.
/// 만든 Mesh는 개체마다 하나이고, 부른 쪽이 Release로 지운다.
/// </summary>
public static class FacilityMesh
{
    private static readonly List<CombineInstance> parts = new List<CombineInstance>();

    /// <summary>tile을 step 간격으로 count번 이어 붙여 target에 쓴다(origin = 첫 조각의 자리). target이 null이면 새로 만든다.</summary>
    public static Mesh Tile(Mesh target, Mesh tile, int count, Vector3 origin, Vector3 step, string name)
    {
        if (tile == null) return target;
        if (target == null)
        {
            target = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            target.MarkDynamic();
        }
        target.Clear();
        count = Mathf.Max(0, count);
        if (count == 0) return target;
        parts.Clear();
        for (int i = 0; i < count; i++)
            parts.Add(new CombineInstance { mesh = tile, subMeshIndex = 0, transform = Matrix4x4.Translate(origin + step * i) });
        if (tile.vertexCount * count > 65000) target.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        target.CombineMeshes(parts.ToArray(), true, true, false);
        parts.Clear();
        target.RecalculateBounds();
        return target;
    }

    // Strands가 매번 다시 쓰는 버퍼(메인 스레드 전용). 크레인 탑승 중 매 프레임 불려도 배열을 새로 만들지 않는다.
    private static readonly List<Vector3> strandVertices = new List<Vector3>();
    private static readonly List<Color> strandColors = new List<Color>();
    private static readonly List<Vector2> strandUvs = new List<Vector2>();
    private static readonly List<int> strandTriangles = new List<int>();

    /// <summary>두 점 사이 굵기 width의 띠(가는 선 여러 가닥)를 그린다. 케이블처럼 길이가 바뀌는 선에 쓴다.</summary>
    public static Mesh Strands(Mesh target, Vector2 from, Vector2 to, float width, float spacing, int strands, Color color, string name)
    {
        if (target == null)
        {
            target = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            target.MarkDynamic();
        }
        target.Clear();
        Vector2 along = to - from;
        if (along.sqrMagnitude < 1e-6f || strands <= 0) return target;
        Vector2 dir = along.normalized, side = new Vector2(-dir.y, dir.x);
        strandVertices.Clear();
        strandColors.Clear();
        strandUvs.Clear();
        strandTriangles.Clear();
        float first = -(strands - 1) * spacing * .5f;
        for (int s = 0; s < strands; s++)
        {
            Vector2 offset = side * (first + s * spacing);
            Vector2 half = side * width * .5f;
            int v = s * 4;
            strandVertices.Add(from + offset - half); strandVertices.Add(from + offset + half);
            strandVertices.Add(to + offset + half); strandVertices.Add(to + offset - half);
            for (int k = 0; k < 4; k++) { strandColors.Add(color); strandUvs.Add(Vector2.zero); }
            strandTriangles.Add(v); strandTriangles.Add(v + 1); strandTriangles.Add(v + 2);
            strandTriangles.Add(v); strandTriangles.Add(v + 2); strandTriangles.Add(v + 3);
        }
        target.SetVertices(strandVertices);
        target.SetColors(strandColors);
        target.SetUVs(0, strandUvs);
        target.SetTriangles(strandTriangles, 0);
        target.RecalculateBounds();
        return target;
    }

    // ── 4차: 다각선·고리 ─────────────────────────────

    private static Mesh Begin(Mesh target, string name)
    {
        if (target == null)
        {
            target = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            target.MarkDynamic();
        }
        target.Clear();
        strandVertices.Clear();
        strandColors.Clear();
        strandUvs.Clear();
        strandTriangles.Clear();
        return target;
    }

    private static Mesh Finish(Mesh target)
    {
        if (strandVertices.Count == 0) return target;
        if (strandVertices.Count > 65000) target.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        target.SetVertices(strandVertices);
        target.SetColors(strandColors);
        target.SetUVs(0, strandUvs);
        target.SetTriangles(strandTriangles, 0);
        target.RecalculateBounds();
        return target;
    }

    // 굵기 width의 열린 다각선 한 줄(꺾이는 점은 두 변의 법선 평균으로 이어 붙인다 — 날카로운 꺾임은 0.3배까지만 벌린다).
    private static void AddLine(IList<Vector2> points, Vector2 offset, float width, Color color)
    {
        int count = points.Count;
        int first = strandVertices.Count;
        for (int i = 0; i < count; i++)
        {
            Vector2 previous = i > 0 ? points[i - 1] : points[i];
            Vector2 next = i < count - 1 ? points[i + 1] : points[i];
            Vector2 incoming = (points[i] - previous).normalized, outgoing = (next - points[i]).normalized;
            if (incoming == Vector2.zero) incoming = outgoing;
            if (outgoing == Vector2.zero) outgoing = incoming;
            Vector2 a = new Vector2(-incoming.y, incoming.x), c = new Vector2(-outgoing.y, outgoing.x);
            Vector2 normal = (a + c).normalized;
            if (normal == Vector2.zero) normal = c;
            float amount = width * .5f / Mathf.Max(.3f, Vector2.Dot(normal, c));
            // 가닥 간격(offset)은 경로 쪽 법선 방향으로 둔다(x = 옆 간격, 꺾여도 같은 쪽).
            Vector2 shift = normal * (offset.x / Mathf.Max(.3f, Vector2.Dot(normal, c))) + Vector2.up * offset.y;
            strandVertices.Add(points[i] + shift + normal * amount); strandVertices.Add(points[i] + shift - normal * amount);
            for (int k = 0; k < 2; k++) { strandColors.Add(color); strandUvs.Add(Vector2.zero); }
        }
        for (int i = 0; i < count - 1; i++)
        {
            int a = first + i * 2, c = first + (i + 1) * 2;
            strandTriangles.Add(a); strandTriangles.Add(c); strandTriangles.Add(a + 1);
            strandTriangles.Add(a + 1); strandTriangles.Add(c); strandTriangles.Add(c + 1);
        }
    }

    /// <summary>
    /// 열린 다각선을 따라 굵기 width의 선 strands가닥을 spacing 간격으로 그린다(경로 크레인의 빛 통로 선·띠).
    /// strands가 1이면 가운데 한 줄(넓게 주면 띠). 점이 2개보다 적으면 빈 Mesh.
    /// </summary>
    public static Mesh Polyline(Mesh target, IList<Vector2> points, float width, float spacing, int strands, Color color, string name)
    {
        target = Begin(target, name);
        if (points == null || points.Count < 2 || strands <= 0) return target;
        float first = -(strands - 1) * spacing * .5f;
        for (int s = 0; s < strands; s++) AddLine(points, new Vector2(first + s * spacing, 0f), width, color);
        return Finish(target);
    }

    /// <summary>점마다 반지름 radius·굵기 width의 고리(segments 각)를 그린다(빛 통로의 꺾이는 점 관절).</summary>
    public static Mesh Rings(Mesh target, IList<Vector2> centers, float radius, float width, int segments, Color color, string name)
    {
        target = Begin(target, name);
        if (centers == null || centers.Count == 0) return target;
        segments = Mathf.Max(6, segments);
        float inner = Mathf.Max(0f, radius - width * .5f), outer = radius + width * .5f;
        foreach (var center in centers)
        {
            int first = strandVertices.Count;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                strandVertices.Add(center + dir * outer); strandVertices.Add(center + dir * inner);
                for (int k = 0; k < 2; k++) { strandColors.Add(color); strandUvs.Add(Vector2.zero); }
            }
            for (int i = 0; i < segments; i++)
            {
                int a = first + i * 2, c = first + ((i + 1) % segments) * 2;
                strandTriangles.Add(a); strandTriangles.Add(c); strandTriangles.Add(a + 1);
                strandTriangles.Add(a + 1); strandTriangles.Add(c); strandTriangles.Add(c + 1);
            }
        }
        return Finish(target);
    }

    /// <summary>
    /// 원점 center·반지름 radius의 점선 원(dashes개, 한 칸 중 fill만큼 칠함). 붙기 반경 고리 — 크기는 정점으로 정하고, 도는 움직임은 Transform 회전으로 준다.
    /// </summary>
    public static Mesh DashedRing(Mesh target, Vector2 center, float radius, float width, int dashes, float fill, Color color, string name)
    {
        target = Begin(target, name);
        if (radius <= 0f || dashes <= 0) return target;
        fill = Mathf.Clamp(fill, .05f, 1f);
        float step = Mathf.PI * 2f / dashes;
        int pieces = Mathf.Max(2, Mathf.CeilToInt(radius * step * fill / .35f));   // 긴 점선은 0.35칸마다 꺾어 원에 붙인다
        float inner = Mathf.Max(0f, radius - width * .5f), outer = radius + width * .5f;
        for (int d = 0; d < dashes; d++)
        {
            int first = strandVertices.Count;
            for (int k = 0; k <= pieces; k++)
            {
                float angle = d * step + step * fill * k / pieces;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                strandVertices.Add(center + dir * outer); strandVertices.Add(center + dir * inner);
                for (int n = 0; n < 2; n++) { strandColors.Add(color); strandUvs.Add(Vector2.zero); }
            }
            for (int k = 0; k < pieces; k++)
            {
                int a = first + k * 2, c = first + (k + 1) * 2;
                strandTriangles.Add(a); strandTriangles.Add(c); strandTriangles.Add(a + 1);
                strandTriangles.Add(a + 1); strandTriangles.Add(c); strandTriangles.Add(c + 1);
            }
        }
        return Finish(target);
    }

    // ── 5차: 붙기 직선·견인 띠·선로 침목·고정 크레인 기둥(정점을 새로 채운다 — 크기 늘이기 없음, 공유 버퍼라 매 프레임 할당 없음) ─────────────

    // 네 꼭짓점 사각형 하나(a → b 쪽 순서, 양쪽 반폭 halfA·halfB — 같으면 직사각형, 다르면 사다리꼴).
    private static void AddQuad(Vector2 a, Vector2 b, float halfA, float halfB, Color color)
    {
        Vector2 along = b - a;
        if (along.sqrMagnitude < 1e-10f) return;
        Vector2 side = new Vector2(-along.y, along.x).normalized;
        int v = strandVertices.Count;
        strandVertices.Add(a - side * halfA); strandVertices.Add(a + side * halfA);
        strandVertices.Add(b + side * halfB); strandVertices.Add(b - side * halfB);
        for (int k = 0; k < 4; k++) { strandColors.Add(color); strandUvs.Add(Vector2.zero); }
        strandTriangles.Add(v); strandTriangles.Add(v + 1); strandTriangles.Add(v + 2);
        strandTriangles.Add(v); strandTriangles.Add(v + 2); strandTriangles.Add(v + 3);
    }

    /// <summary>
    /// from → to 점선(굵기 width, 칠한 길이 dash, 틈 gap). offset(칸)만큼 무늬를 to 쪽으로 민다 — 매 프레임 offset을 늘리면 점선이 to 쪽으로 흐른다
    /// (5차 붙기 직선: 크레인 쪽으로 3칸/초). 무늬는 정점 위치로만 바뀐다(크기 변형 없음).
    /// </summary>
    public static Mesh DashedSegment(Mesh target, Vector2 from, Vector2 to, float width, float dash, float gap, float offset, Color color, string name)
    {
        target = Begin(target, name);
        float length = Vector2.Distance(from, to);
        if (length < 1e-4f || dash <= 0f) return Finish(target);
        Vector2 dir = (to - from) / length;
        float period = dash + Mathf.Max(0f, gap);
        float start = Mathf.Repeat(offset, period) - period;
        float half = width * .5f;
        for (float s = start; s < length; s += period)
        {
            float a = Mathf.Max(0f, s), b = Mathf.Min(length, s + dash);
            if (b - a < .01f) continue;
            AddQuad(from + dir * a, from + dir * b, half, half, color);
        }
        return Finish(target);
    }

    /// <summary>from → to 사다리꼴 띠 한 장(굵기 widthFrom → widthTo). 5차 견인 띠(꼬리 가늘고 머리 굵게)·흰 머리에 쓴다.</summary>
    public static Mesh Taper(Mesh target, Vector2 from, Vector2 to, float widthFrom, float widthTo, Color color, string name)
    {
        target = Begin(target, name);
        AddQuad(from, to, Mathf.Max(0f, widthFrom) * .5f, Mathf.Max(0f, widthTo) * .5f, color);
        return Finish(target);
    }

    /// <summary>
    /// 열린 다각선을 따라 spacing칸마다 다각선에 수직인 짧은 침목(길이 halfLength × 2, 굵기 width)을 그린다(5차 경로 크레인 레일이 '선로'로 읽히게).
    /// 첫 점·끝 점에는 두지 않는다(도크 표시가 차지한다).
    /// </summary>
    public static Mesh Ties(Mesh target, IList<Vector2> points, float spacing, float halfLength, float width, Color color, string name)
    {
        target = Begin(target, name);
        if (points == null || points.Count < 2 || spacing <= .05f) return Finish(target);
        float total = PathLength(points);
        for (float s = spacing; s < total - spacing * .5f; s += spacing)
        {
            Vector2 at = PointAlong(points, s, out var dir);
            Vector2 side = new Vector2(-dir.y, dir.x);
            AddQuad(at - side * halfLength, at + side * halfLength, width * .5f, width * .5f, color);
        }
        return Finish(target);
    }

    /// <summary>
    /// 5차 고정 크레인의 망원 기둥: from(아래 관절) → to(천장) 사이를 split 비율에서 나눠 아래는 굵은 바깥 관(outer), 위는 가는 안쪽 관(inner)으로 그리고,
    /// 바깥 관에는 collarEvery칸마다·이음매에는 칼라(폭 collarWidth, 두께 collarThickness)를 둔다. 길이가 바뀌면 정점을 새로 채운다(늘이기 없음).
    /// </summary>
    public static Mesh Pole(Mesh target, Vector2 from, Vector2 to, float outer, float inner, float split, float collarEvery, float collarWidth, float collarThickness,
        Color outerColor, Color innerColor, Color collarColor, string name)
    {
        target = Begin(target, name);
        float length = Vector2.Distance(from, to);
        if (length < .02f) return Finish(target);
        Vector2 dir = (to - from) / length;
        Vector2 joint = from + dir * (length * Mathf.Clamp01(split));
        AddQuad(joint, to, inner * .5f, inner * .5f, innerColor);
        AddQuad(from, joint, outer * .5f, outer * .5f, outerColor);
        float outerLength = Vector2.Distance(from, joint);
        if (collarEvery > .05f)
            for (float s = collarEvery; s < outerLength - collarThickness; s += collarEvery)
            {
                Vector2 at = from + dir * s;
                AddQuad(at - dir * collarThickness * .5f, at + dir * collarThickness * .5f, collarWidth * .5f, collarWidth * .5f, collarColor);
            }
        if (outerLength > collarThickness && length - outerLength > .01f)
            AddQuad(joint - dir * collarThickness, joint, collarWidth * .55f, collarWidth * .55f, collarColor);
        return Finish(target);
    }

    /// <summary>열린 다각선의 전체 길이.</summary>
    public static float PathLength(IList<Vector2> points)
    {
        float length = 0f;
        if (points == null) return 0f;
        for (int i = 1; i < points.Count; i++) length += Vector2.Distance(points[i - 1], points[i]);
        return length;
    }

    /// <summary>열린 다각선 위에서 처음부터 distance만큼 간 점(범위 밖이면 양 끝). direction = 그 구간의 진행 방향.</summary>
    public static Vector2 PointAlong(IList<Vector2> points, float distance, out Vector2 direction)
    {
        direction = Vector2.right;
        if (points == null || points.Count == 0) return Vector2.zero;
        if (points.Count == 1) return points[0];
        if (distance <= 0f)
        {
            direction = (points[1] - points[0]).normalized;
            return points[0];
        }
        for (int i = 1; i < points.Count; i++)
        {
            float segment = Vector2.Distance(points[i - 1], points[i]);
            if (segment > 1e-5f) direction = (points[i] - points[i - 1]) / segment;
            if (distance <= segment || i == points.Count - 1)
                return segment > 1e-5f ? Vector2.Lerp(points[i - 1], points[i], Mathf.Clamp01(distance / segment)) : points[i];
            distance -= segment;
        }
        return points[points.Count - 1];
    }

    /// <summary>닫힌 다각형 둘레에서 처음 꼭짓점부터 distance만큼 간 점(둘레 길이로 되감는다). 강조 테두리를 도는 빛 점이 쓴다.</summary>
    public static Vector2 PointAroundLoop(IList<Vector2> polygon, float distance)
    {
        if (polygon == null || polygon.Count == 0) return Vector2.zero;
        if (polygon.Count == 1) return polygon[0];
        float perimeter = LoopLength(polygon);
        if (perimeter <= 1e-5f) return polygon[0];
        distance = Mathf.Repeat(distance, perimeter);
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            float segment = Vector2.Distance(a, b);
            if (distance <= segment) return segment > 1e-5f ? Vector2.Lerp(a, b, distance / segment) : a;
            distance -= segment;
        }
        return polygon[0];
    }

    /// <summary>닫힌 다각형의 둘레 길이.</summary>
    public static float LoopLength(IList<Vector2> polygon)
    {
        if (polygon == null || polygon.Count < 2) return 0f;
        float length = 0f;
        for (int i = 0; i < polygon.Count; i++) length += Vector2.Distance(polygon[i], polygon[(i + 1) % polygon.Count]);
        return length;
    }

    public static void Release(ref Mesh mesh)
    {
        if (mesh == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
        else UnityEngine.Object.DestroyImmediate(mesh);
        mesh = null;
    }
}

/// <summary>Prop 효과를 거리 감쇠로 재생하는 짧은 도우미(FeedbackDataSO·SoundOptions).</summary>
public static class FacilityFeedback
{
    public static void Play(FeedbackDataSO data, Vector3 position)
    {
        if (data != null) DungeonPropFeedback.Play(data, position, Quaternion.identity);
    }

    public static void Play(SoundOptions sound, Vector3 position)
    {
        if (sound == null || sound.clip == null || SoundManager.Instance == null) return;
        float gain = DungeonPropFeedback.DistanceGain(position);
        if (gain <= 0f) return;
        var copy = sound.WithPitchMultiplier(1f);
        copy.volume = sound.volume * gain;
        SoundManager.Instance.PlaySound(copy, position);
    }

    public static void Burst(ParticleSystem system, int count)
    {
        if (system == null) return;
        if (count > 0) system.Emit(count);
        else if (!system.isPlaying) system.Play(true);
    }

    public static void Emit(ParticleSystem system, bool on)
    {
        if (system == null) return;
        if (on) { if (!system.isPlaying) system.Play(true); }
        else if (system.isPlaying) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
