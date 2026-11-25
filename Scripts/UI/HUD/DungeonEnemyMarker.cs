using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 던전 적 위치 마커 하나(2026-11-24 던전형 Wave 2차). 표시 전용 MaskableGraphic이고 입력을 받지 않는다.
/// - Edge: 대상이 화면 밖이면 화면 가장자리(위쪽 HUD를 뺀 영역)에서 대상 쪽을 가리키는 이중 화살촉(WaveGoalIndicator·LobbyGuideArrow와 같은 모양, 0.7배).
/// - Over: 대상이 화면 안이면 머리 위에서 아래를 가리키는 작은 갈매기(약 28px).
/// 두 모양 모두 +X를 가리키게 그리고 RectTransform 회전으로 방향을 정한다. 자리·대상·페이드는 <see cref="DungeonEnemyMarkers"/>가 정한다.
/// 모양이 바뀔 때만 Mesh를 다시 만들고, 자리·회전은 RectTransform, 페이드는 CanvasRenderer 알파로 바꾼다.
/// LobbyGuideArrow·WaveGoalIndicator는 고치지 않는다(Lobby·Wave 목표 회귀 보호) — 화살 모양은 그 둘과 같은 좌표를 복사했다.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DungeonEnemyMarker : MaskableGraphic
{
    public enum Shape
    {
        Edge = 0,
        Over = 1,
    }

    [SerializeField] private Shape shape = Shape.Edge;
    [Tooltip("가장자리 화살의 크기(Lobby 화살 대비)")]
    [SerializeField, Range(.3f, 1.5f)] private float edgeScale = .7f;
    [Tooltip("머리 위 갈매기의 크기(Lobby 화살 대비)")]
    [SerializeField, Range(.3f, 1.5f)] private float overScale = .62f;

    /// <summary>가리키는 적(풀에서 비어 있으면 null).</summary>
    public EnemyController Target { get; set; }
    /// <summary>이번 프레임에 보여야 하는가(아니면 페이드아웃).</summary>
    public bool Wanted { get; set; }
    /// <summary>지금 알파(0~1, 실제 시간 페이드).</summary>
    public float Alpha { get; private set; }
    /// <summary>마지막으로 놓은 화면 좌표(px)와 회전(°). 검증이 읽는다.</summary>
    public Vector2 ScreenPoint { get; set; }
    public float Angle { get; set; }
    public bool IsEdge => shape == Shape.Edge;
    /// <summary>마지막으로 대상 쪽 자리를 계산했는지(카메라가 없으면 false).</summary>
    public bool Placed { get; set; }

    public Shape DrawShape
    {
        get => shape;
        set { if (shape != value) { shape = value; SetVerticesDirty(); } }
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    /// <summary>알파를 정하고 CanvasRenderer에 반영한다.</summary>
    public void SetAlpha(float alpha)
    {
        Alpha = Mathf.Clamp01(alpha);
        canvasRenderer.SetAlpha(Alpha);
    }

    /// <summary>풀로 돌려보낸다(대상·자리 비움, 알파 0).</summary>
    public void Release()
    {
        Target = null;
        Wanted = false;
        Placed = false;
        SetAlpha(0f);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var shade = new Color32(9, 19, 27, 245);
        Color32 tint = color;
        if (shape == Shape.Edge)
        {
            // +X를 가리키는 이중 화살촉(WaveGoalIndicator와 같은 좌표) × edgeScale: 어두운 바탕 → 색 면 → 흰 꼬리
            float s = edgeScale;
            Quad(vh, s, new Vector2(-25, -22), new Vector2(7, -22), new Vector2(30, 0), new Vector2(-2, 0), shade);
            Quad(vh, s, new Vector2(-2, 0), new Vector2(30, 0), new Vector2(7, 22), new Vector2(-25, 22), shade);
            Quad(vh, s, new Vector2(-18, -16), new Vector2(5, -16), new Vector2(22, 0), new Vector2(-1, 0), tint);
            Quad(vh, s, new Vector2(-1, 0), new Vector2(22, 0), new Vector2(5, 16), new Vector2(-18, 16), tint);
            Color32 tail = new Color(1f, 1f, 1f, color.a);
            Quad(vh, s, new Vector2(-26, -12), new Vector2(-22, -12), new Vector2(-9, 0), new Vector2(-13, 0), tail);
            Quad(vh, s, new Vector2(-13, 0), new Vector2(-9, 0), new Vector2(-22, 12), new Vector2(-26, 12), tail);
        }
        else
        {
            // +X를 가리키는 홑 갈매기 × overScale(회전 -90°로 아래를 가리킨다): 어두운 바탕 → 색 면
            float s = overScale;
            Quad(vh, s, new Vector2(-16, -26), new Vector2(-2, -26), new Vector2(20, 0), new Vector2(6, 0), shade);
            Quad(vh, s, new Vector2(6, 0), new Vector2(20, 0), new Vector2(-2, 26), new Vector2(-16, 26), shade);
            Quad(vh, s, new Vector2(-10, -22), new Vector2(-1, -22), new Vector2(17.5f, 0), new Vector2(8.5f, 0), tint);
            Quad(vh, s, new Vector2(8.5f, 0), new Vector2(17.5f, 0), new Vector2(-1, 22), new Vector2(-10, 22), tint);
        }
    }

    private static void Quad(VertexHelper vh, float scale, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a * scale, tint, Vector2.zero); vh.AddVert(b * scale, tint, Vector2.zero);
        vh.AddVert(c * scale, tint, Vector2.zero); vh.AddVert(d * scale, tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }

    /// <summary>지금 모양으로 Mesh를 만들어 output에 채운다(검증용).</summary>
    public int BuildMesh(Mesh output)
    {
        using (var vh = new VertexHelper())
        {
            OnPopulateMesh(vh);
            vh.FillMesh(output);
            return vh.currentVertCount;
        }
    }
}
