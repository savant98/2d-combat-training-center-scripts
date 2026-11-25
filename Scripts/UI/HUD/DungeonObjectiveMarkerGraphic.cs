using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 던전 주요 목표 마커 하나(4차, 2026-11-25, DESIGN_V4 §2.5 R10·§2.6 R11). 표시 전용 MaskableGraphic이고 입력을 받지 않는다.
/// 계약의 DungeonObjectiveMarker(지도 표식 데이터 구조체)와 이름이 겹치지 않게 Graphic 접미사를 붙였다.
/// - Over: 대상이 화면 안이고 쓸 수 있으면(Ready) 밝은 앰버 마름모 + 안쪽 글리프(레버 = 손잡이 막대, 출구 = 문틀) + 아래를 가리키는 작은 꼬리.
/// - Edge: 대상이 화면 밖이고 Ready면 화면 가장자리에서 대상 쪽을 가리키는 앰버 이중 화살촉(적 위치 화살과 같은 좌표·0.7배 — 색으로 구분) + 뒤쪽 작은 마름모.
/// - Locked: 방 미클리어·출구 해금 전이면 흐린 앰버 자물쇠(몸통 + 고리). 화면 밖이면 띄우지 않는다(관리자 <see cref="DungeonObjectiveMarkers"/>가 정한다).
/// 모양·글리프가 바뀔 때만 Mesh를 다시 만들고, 자리·회전은 RectTransform, 페이드는 CanvasRenderer 알파로 바꾼다. 정점 색은 아래 고정 색(Graphic.color는 흰색 그대로).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DungeonObjectiveMarkerGraphic : MaskableGraphic
{
    public enum Shape
    {
        Edge = 0,
        Over = 1,
        Locked = 2,
    }

    [SerializeField] private Shape shape = Shape.Over;
    [SerializeField] private DungeonObjectiveTargetKind glyph = DungeonObjectiveTargetKind.Lever;
    [Tooltip("가장자리 화살 크기(Lobby 화살 대비 — 적 위치 화살과 같은 0.7)")]
    [SerializeField, Range(.3f, 1.5f)] private float edgeScale = .7f;
    [Tooltip("화면 안 마름모 반대각선(px)")]
    [SerializeField, Min(4f)] private float overHalf = 17f;
    [Tooltip("자물쇠 높이의 절반(px)")]
    [SerializeField, Min(4f)] private float lockHalf = 11f;

    public static readonly Color32 Amber = new Color32(255, 176, 33, 255);
    public static readonly Color32 AmberBright = new Color32(255, 231, 179, 255);
    public static readonly Color32 AmberDim = new Color32(150, 104, 30, 215);
    public static readonly Color32 Shade = new Color32(18, 12, 4, 235);

    /// <summary>가리키는 대상의 열쇠((종류, 번호)를 한 정수로 — 풀에서 비어 있으면 -1).</summary>
    public int Key { get; set; } = -1;
    /// <summary>이번 프레임에 보여야 하는가(아니면 페이드아웃).</summary>
    public bool Wanted { get; set; }
    /// <summary>지금 알파(0~1, 실제 시간 페이드).</summary>
    public float Alpha { get; private set; }
    /// <summary>모양별 밝기 곱(맥동 포함 — 관리자가 정한다).</summary>
    public float Brightness { get; set; } = 1f;
    /// <summary>마지막으로 놓은 화면 좌표(px, 까딱임 전)와 회전(°). 검증이 읽는다.</summary>
    public Vector2 ScreenPoint { get; set; }
    public float Angle { get; set; }
    /// <summary>마지막으로 대상 쪽 자리를 계산했는지(카메라가 없으면 false).</summary>
    public bool Placed { get; set; }
    /// <summary>마지막 대상 상태(검증용).</summary>
    public DungeonObjectiveTargetState TargetState { get; set; }
    public bool IsEdge => shape == Shape.Edge;

    public Shape DrawShape
    {
        get => shape;
        set { if (shape != value) { shape = value; SetVerticesDirty(); } }
    }

    public DungeonObjectiveTargetKind Glyph
    {
        get => glyph;
        set { if (glyph != value) { glyph = value; SetVerticesDirty(); } }
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    /// <summary>알파를 정하고 CanvasRenderer에 반영한다(밝기 곱 포함).</summary>
    public void SetAlpha(float value)
    {
        Alpha = Mathf.Clamp01(value);
        canvasRenderer.SetAlpha(Alpha * Mathf.Clamp01(Brightness));
    }

    /// <summary>풀로 돌려보낸다(대상·자리 비움, 알파 0).</summary>
    public void Release()
    {
        Key = -1;
        Wanted = false;
        Placed = false;
        Brightness = 1f;
        SetAlpha(0f);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        switch (shape)
        {
            case Shape.Edge:
            {
                // +X를 가리키는 이중 화살촉(DungeonEnemyMarker와 같은 좌표) × edgeScale: 어두운 바탕 → 앰버 면 → 밝은 꼬리 + 뒤쪽 작은 마름모(목표 표시)
                float s = edgeScale;
                Quad(vh, s, new Vector2(-25, -22), new Vector2(7, -22), new Vector2(30, 0), new Vector2(-2, 0), Shade);
                Quad(vh, s, new Vector2(-2, 0), new Vector2(30, 0), new Vector2(7, 22), new Vector2(-25, 22), Shade);
                Quad(vh, s, new Vector2(-18, -16), new Vector2(5, -16), new Vector2(22, 0), new Vector2(-1, 0), Amber);
                Quad(vh, s, new Vector2(-1, 0), new Vector2(22, 0), new Vector2(5, 16), new Vector2(-18, 16), Amber);
                Quad(vh, s, new Vector2(-26, -12), new Vector2(-22, -12), new Vector2(-9, 0), new Vector2(-13, 0), AmberBright);
                Quad(vh, s, new Vector2(-13, 0), new Vector2(-9, 0), new Vector2(-22, 12), new Vector2(-26, 12), AmberBright);
                Diamond(vh, new Vector2(-34f, 0f) * s, 6f * s, AmberBright);
                break;
            }
            case Shape.Over:
            {
                float h = overHalf;
                // 아래 꼬리(대상 쪽) → 마름모 바탕 → 앰버 마름모 → 글리프
                Tri(vh, new Vector2(-h * .38f, -h * .78f), new Vector2(h * .38f, -h * .78f), new Vector2(0f, -h * 1.32f), Amber);
                Diamond(vh, Vector2.zero, h + 2f, Shade);
                Diamond(vh, Vector2.zero, h, Amber);
                Diamond(vh, Vector2.zero, h * .7f, Shade);
                if (glyph == DungeonObjectiveTargetKind.Exit) ExitGlyph(vh, h * .46f, AmberBright);
                else LeverGlyph(vh, h * .46f, AmberBright);
                break;
            }
            default:
            {
                // 흐린 자물쇠: 몸통 + 고리(두 기둥 + 윗변). 글자 없이 Mesh.
                float h = lockHalf;
                float w = h * .9f;
                var body = Rect.MinMaxRect(-w, -h, w, h * .15f);
                Box(vh, Expand(body, 1.5f), Shade);
                Box(vh, body, AmberDim);
                Box(vh, Rect.MinMaxRect(-h * .12f, -h * .65f, h * .12f, -h * .2f), Shade);   // 열쇠 구멍
                float t = Mathf.Max(1.5f, h * .2f);
                float sx = w * .62f;
                Box(vh, Rect.MinMaxRect(-sx, h * .15f, -sx + t, h * .8f), AmberDim);
                Box(vh, Rect.MinMaxRect(sx - t, h * .15f, sx, h * .8f), AmberDim);
                Box(vh, Rect.MinMaxRect(-sx, h * .8f, sx, h * .8f + t), AmberDim);
                break;
            }
        }
    }

    // 레버 글리프: 기울어진 손잡이 막대 + 손잡이 머리 + 받침.
    private static void LeverGlyph(VertexHelper vh, float s, Color32 c)
    {
        Box(vh, Rect.MinMaxRect(-s * .9f, -s, s * .9f, -s * .62f), c);
        Quad(vh, 1f, new Vector2(-s * .18f, -s * .62f), new Vector2(s * .14f, -s * .62f), new Vector2(s * .62f, s * .62f), new Vector2(s * .3f, s * .62f), c);
        Diamond(vh, new Vector2(s * .5f, s * .72f), s * .32f, c);
    }

    // 출구 글리프: 문틀 + 가운데 빛 기둥.
    private static void ExitGlyph(VertexHelper vh, float s, Color32 c)
    {
        float w = s * .8f, t = Mathf.Max(1.2f, s * .22f);
        Box(vh, Rect.MinMaxRect(-w, -s, -w + t, s), c);
        Box(vh, Rect.MinMaxRect(w - t, -s, w, s), c);
        Box(vh, Rect.MinMaxRect(-w, s - t, w, s), c);
        Box(vh, Rect.MinMaxRect(-t * .5f, -s, t * .5f, s - t * 1.6f), c);
    }

    private static Rect Expand(Rect r, float d) => Rect.MinMaxRect(r.xMin - d, r.yMin - d, r.xMax + d, r.yMax + d);

    private static void Box(VertexHelper vh, Rect r, Color32 c)
    {
        if (r.width <= 0f || r.height <= 0f) return;
        Quad(vh, 1f, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), c);
    }

    private static void Diamond(VertexHelper vh, Vector2 c, float half, Color32 tint)
    {
        Quad(vh, 1f, c + new Vector2(0f, -half), c + new Vector2(half, 0f), c + new Vector2(0f, half), c + new Vector2(-half, 0f), tint);
    }

    private static void Tri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 tint)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero); vh.AddVert(c, tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
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
