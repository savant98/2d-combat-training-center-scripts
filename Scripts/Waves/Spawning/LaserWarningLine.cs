using UnityEngine;

/// <summary>
/// 레이저 발사 직전 빔이 지나갈 자리에 보이는 얇은 붉은 예고선이다.
/// - 실행 중에 처음 보일 때만 자식 LineRenderer를 만든다(기준 Prefab을 고치지 않는다). 예고 시간이 0인 발사기는 아무것도 만들지 않는다.
/// - 모양: 예고 구간 동안 얇게 깜박이다(끝으로 갈수록 빠르게) 발사 직전 짧은 구간에서 굵어지며 깜박임을 멈춘다.
/// - 경로는 부르는 쪽(LaserSpawner)이 발사와 같은 모터 질의로 구해 넘긴다. 이 클래스는 그리기만 한다.
/// - Material은 빔 Prefab의 가장 위 층 LineRenderer(코어 선) 것을 그대로 쓰고, 없으면 기본 Sprite 셰이더로 하나 만든다.
/// </summary>
public sealed class LaserWarningLine
{
    public const string ObjectName = "__LaserWarningLine";
    /// <summary>위험 빨강. 편집기 범위 표시(1, .22, .16)와 같은 색이다.</summary>
    public static readonly Color DangerRed = new Color(1f, .22f, .16f, 1f);

    private const float ThickLeadMax = .15f;      // 발사 직전 굵어지는 구간의 최대 길이(초)
    private const float ThickLeadRatio = .35f;    // 예고 구간이 짧으면 그 길이의 이 비율까지만 굵어진다
    private const float ThinWidthRatio = .22f;    // 빔 폭에 대한 얇은 선의 폭
    private const float ThickWidthRatio = .65f;   // 빔 폭에 대한 굵어진 선의 폭
    private const float MinWidth = .04f;
    private const float BlinkOnAlpha = .9f, BlinkOffAlpha = .28f;

    private static Material fallbackMaterial;

    private readonly Transform owner;
    private readonly Component beamTemplate;
    private LineRenderer line;

    public LaserWarningLine(Transform owner, Component beamTemplate)
    {
        this.owner = owner;
        this.beamTemplate = beamTemplate;
    }

    public bool Visible => line != null && line.enabled;
    /// <summary>지금 그린 선의 폭(검사용). 보이지 않으면 0이다.</summary>
    public float Width => Visible ? line.startWidth : 0f;
    /// <summary>지금 그린 선의 알파(검사용). 보이지 않으면 0이다.</summary>
    public float Alpha => Visible ? line.startColor.a : 0f;
    public Vector2 Start => Visible ? (Vector2)line.GetPosition(0) : Vector2.zero;
    public Vector2 End => Visible ? (Vector2)line.GetPosition(1) : Vector2.zero;

    /// <summary>
    /// 예고선을 보인다. elapsed = 예고 구간이 시작된 뒤 지난 시간, window = 예고 구간 전체 길이(초), beamWidth = 실제 빔의 기본 폭.
    /// </summary>
    public void Show(Vector2 start, Vector2 end, float elapsed, float window, float beamWidth)
    {
        if (owner == null) return;
        if (line == null && !Create()) return;
        window = Mathf.Max(.0001f, window);
        elapsed = Mathf.Clamp(elapsed, 0f, window);
        float thin = Mathf.Max(MinWidth, beamWidth * ThinWidthRatio);
        float thick = Mathf.Max(thin, beamWidth * ThickWidthRatio);
        float remaining = window - elapsed;
        float thickLead = Mathf.Min(ThickLeadMax, window * ThickLeadRatio);
        float width, alpha;
        if (remaining <= thickLead)
        {
            // 발사 직전: 깜박임을 멈추고 실제 빔 폭 쪽으로 굵어진다.
            float k = 1f - remaining / Mathf.Max(.0001f, thickLead);
            width = Mathf.Lerp(thin, thick, k * k * (3f - 2f * k));
            alpha = 1f;
        }
        else
        {
            // 깜박임은 끝으로 갈수록 빨라진다(용접 팔 노즐 깜박임과 같은 리듬).
            float t = elapsed / window;
            float period = Mathf.Lerp(.16f, .07f, t);
            bool on = Mathf.Repeat(elapsed, period) < period * .55f;
            width = thin;
            alpha = on ? BlinkOnAlpha : BlinkOffAlpha;
        }
        var color = new Color(DangerRed.r, DangerRed.g, DangerRed.b, alpha);
        line.startColor = line.endColor = color;
        line.startWidth = line.endWidth = width;
        line.SetPosition(0, new Vector3(start.x, start.y, owner.position.z));
        line.SetPosition(1, new Vector3(end.x, end.y, owner.position.z));
        if (!line.enabled) line.enabled = true;
    }

    public void Hide()
    {
        if (line != null && line.enabled) line.enabled = false;
    }

    private bool Create()
    {
        LineRenderer reference = null;
        if (beamTemplate != null)
            foreach (var candidate in beamTemplate.GetComponentsInChildren<LineRenderer>(true))
                if (candidate != null && candidate.sharedMaterial != null && (reference == null || candidate.sortingOrder > reference.sortingOrder))
                    reference = candidate;
        var material = reference != null ? reference.sharedMaterial : Fallback();
        if (material == null) return false;
        var node = new GameObject(ObjectName);
        node.layer = owner.gameObject.layer;
        node.transform.SetParent(owner, false);
        line = node.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.numCapVertices = 0;
        line.numCornerVertices = 0;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        line.sharedMaterial = material;
        if (reference != null)
        {
            line.sortingLayerID = reference.sortingLayerID;
            line.sortingOrder = reference.sortingOrder;
        }
        line.enabled = false;
        return true;
    }

    private static Material Fallback()
    {
        if (fallbackMaterial != null) return fallbackMaterial;
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;
        fallbackMaterial = new Material(shader) { name = "LaserWarningLine (Runtime)", hideFlags = HideFlags.DontSave };
        return fallbackMaterial;
    }
}
