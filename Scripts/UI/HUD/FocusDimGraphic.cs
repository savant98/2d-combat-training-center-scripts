using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 전체를 어둡게 덮고 한 곳만 밝게 남기는 막(2026-11-14). 안내가 "여기만 누르면 된다"는 것을 보여 줄 때 쓴다(Lobby 안내).
/// - 가리킬 대상(RectTransform)을 주면 그 사각형만 뚫고 나머지를 어둡게 한다. 대상이 움직이면 구멍도 따라간다(매 프레임 대상의 지금 자리를 읽는다).
/// - 대상이 바뀌면 구멍이 새 자리로 미끄러져 간다. 대상을 비우면 막이 걷힌다.
/// - 구멍의 가장자리는 조금 번지게 하고(딱딱한 경계가 눈에 걸리지 않게), 구멍 둘레에 가는 밝은 선을 둘러 눈길을 모은다.
/// - 표시 전용이다. 입력은 가로채지 않는다(입력 제한은 안내 쪽이 따로 건다). 실제 시간으로 움직인다(게임 시간이 멈춘 메뉴 위에서도 쓴다).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class FocusDimGraphic : MaskableGraphic
{
    [SerializeField, Range(0f, 1f), Tooltip("가장 어두울 때의 불투명도")] private float dimAlpha = .78f;
    [SerializeField] private Color dimColor = new Color(.008f, .014f, .028f, 1f);
    [SerializeField, Min(0f), Tooltip("구멍 가장자리가 번지는 폭(화면 단위)")] private float feather = 26f;
    [SerializeField, Min(0f), Tooltip("구멍 둘레의 밝은 선의 굵기(0이면 그리지 않는다)")] private float rimWidth = 3f;
    [SerializeField] private Color rimColor = new Color(1f, 1f, 1f, .85f);
    [SerializeField, Min(.01f), Tooltip("막이 드리우고 걷히는 데 걸리는 시간(초)")] private float fadeTime = .22f;
    [SerializeField, Min(.01f), Tooltip("구멍이 새 대상으로 옮겨 가는 데 걸리는 시간(초)")] private float moveTime = .2f;

    private RectTransform target;
    private Vector2 padding;
    private Rect hole, wantedHole;
    private float visibility, holeOpen;
    private bool holeKnown;
    private readonly Vector3[] corners = new Vector3[4];

    /// <summary>지금 어둡게 덮고 있는 정도(0 = 걷힘, 1 = 다 드리움).</summary>
    public float Visibility => visibility;
    public RectTransform Target => target;
    /// <summary>지금 뚫려 있는 구멍(이 막의 좌표).</summary>
    public Rect Hole => hole;
    public bool IsActive { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    /// <summary>대상만 밝게 남기고 어둡게 덮는다. padding은 대상 둘레에 더 남기는 여백(화면 단위)이다.</summary>
    public void Focus(RectTransform newTarget, Vector2 newPadding)
    {
        IsActive = true;
        target = newTarget;
        padding = newPadding;
    }

    /// <summary>구멍 없이 전체를 어둡게 덮는다(대상이 아직 화면에 자리 잡지 않았을 때).</summary>
    public void Cover()
    {
        IsActive = true;
        target = null;
    }

    /// <summary>막을 걷는다.</summary>
    public void Clear()
    {
        IsActive = false;
        target = null;
    }

    /// <summary>움직임 없이 지금 상태로 바로 맞춘다(꺼질 때, 처음 켤 때).</summary>
    public void Snap()
    {
        visibility = IsActive ? 1f : 0f;
        bool wanted = TryReadTarget(out wantedHole);
        holeOpen = wanted ? 1f : 0f;
        if (wanted) { hole = wantedHole; holeKnown = true; }
        SetVerticesDirty();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        visibility = 0f; holeOpen = 0f; holeKnown = false;
    }

    private bool TryReadTarget(out Rect rect)
    {
        rect = default;
        if (target == null || !target.gameObject.activeInHierarchy) return false;
        target.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 4; i++)
        {
            Vector2 local = rectTransform.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, local); max = Vector2.Max(max, local);
        }
        rect = Rect.MinMaxRect(min.x - padding.x, min.y - padding.y, max.x + padding.x, max.y + padding.y);
        return rect.width > 1f && rect.height > 1f;
    }

    private void LateUpdate()
    {
        float delta = Time.unscaledDeltaTime;
        float before = visibility, beforeOpen = holeOpen;
        Rect beforeHole = hole;
        visibility = Mathf.MoveTowards(visibility, IsActive ? 1f : 0f, delta / fadeTime);
        bool wanted = TryReadTarget(out wantedHole);
        if (wanted)
        {
            if (!holeKnown || holeOpen <= 0f) { hole = wantedHole; holeKnown = true; }
            else
            {
                float t = 1f - Mathf.Exp(-delta / (moveTime * .35f));
                hole = Rect.MinMaxRect(Mathf.Lerp(hole.xMin, wantedHole.xMin, t), Mathf.Lerp(hole.yMin, wantedHole.yMin, t),
                    Mathf.Lerp(hole.xMax, wantedHole.xMax, t), Mathf.Lerp(hole.yMax, wantedHole.yMax, t));
            }
        }
        holeOpen = Mathf.MoveTowards(holeOpen, wanted && IsActive ? 1f : 0f, delta / moveTime);
        if (!Mathf.Approximately(before, visibility) || !Mathf.Approximately(beforeOpen, holeOpen) || beforeHole != hole) SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (visibility <= 0f) return;
        Rect full = rectTransform.rect;
        Color32 dark = new Color(dimColor.r, dimColor.g, dimColor.b, dimAlpha * visibility * color.a);
        if (!holeKnown || holeOpen <= 0f)
        {
            Quad(vh, full.xMin, full.yMin, full.xMax, full.yMax, dark, dark, dark, dark);
            return;
        }
        // 구멍이 열리는 정도만큼 가운데가 밝아진다(막 전체가 깜빡이지 않게 구멍의 밝기만 바꾼다).
        Color32 clear = new Color(dimColor.r, dimColor.g, dimColor.b, dimAlpha * visibility * color.a * (1f - holeOpen));
        float x0 = Mathf.Clamp(hole.xMin, full.xMin, full.xMax), x1 = Mathf.Clamp(hole.xMax, full.xMin, full.xMax);
        float y0 = Mathf.Clamp(hole.yMin, full.yMin, full.yMax), y1 = Mathf.Clamp(hole.yMax, full.yMin, full.yMax);
        float f = Mathf.Max(0f, feather);
        float ox0 = Mathf.Max(full.xMin, x0 - f), ox1 = Mathf.Min(full.xMax, x1 + f), oy0 = Mathf.Max(full.yMin, y0 - f), oy1 = Mathf.Min(full.yMax, y1 + f);
        // 바깥(다 어두운 곳): 위·아래 띠와 좌·우 띠
        Quad(vh, full.xMin, oy1, full.xMax, full.yMax, dark, dark, dark, dark);
        Quad(vh, full.xMin, full.yMin, full.xMax, oy0, dark, dark, dark, dark);
        Quad(vh, full.xMin, oy0, ox0, oy1, dark, dark, dark, dark);
        Quad(vh, ox1, oy0, full.xMax, oy1, dark, dark, dark, dark);
        // 번지는 테(바깥은 어둡고 구멍 쪽은 밝다): 네 변과 네 모서리
        Quad(vh, ox0, y0, x0, y1, dark, dark, clear, clear);            // 왼쪽 (좌하, 좌상, 우상, 우하)
        Quad(vh, x1, y0, ox1, y1, clear, clear, dark, dark);            // 오른쪽
        Quad(vh, x0, oy0, x1, y0, dark, clear, clear, dark);            // 아래
        Quad(vh, x0, y1, x1, oy1, clear, dark, dark, clear);            // 위
        Quad(vh, ox0, oy0, x0, y0, dark, dark, clear, dark);            // 왼쪽 아래
        Quad(vh, x1, oy0, ox1, y0, dark, clear, dark, dark);            // 오른쪽 아래
        Quad(vh, ox0, y1, x0, oy1, dark, dark, dark, clear);            // 왼쪽 위
        Quad(vh, x1, y1, ox1, oy1, clear, dark, dark, dark);            // 오른쪽 위
        // 구멍 안(열리는 동안에만 옅게 남는다)
        if (clear.a > 0) Quad(vh, x0, y0, x1, y1, clear, clear, clear, clear);
        // 구멍 둘레의 밝은 선
        if (rimWidth > 0f && rimColor.a > 0f)
        {
            Color32 rim = new Color(rimColor.r, rimColor.g, rimColor.b, rimColor.a * visibility * holeOpen);
            float w = rimWidth;
            Quad(vh, x0 - w, y1, x1 + w, y1 + w, rim, rim, rim, rim);
            Quad(vh, x0 - w, y0 - w, x1 + w, y0, rim, rim, rim, rim);
            Quad(vh, x0 - w, y0, x0, y1, rim, rim, rim, rim);
            Quad(vh, x1, y0, x1 + w, y1, rim, rim, rim, rim);
        }
    }

    // 꼭짓점 색의 순서: 왼쪽 아래, 왼쪽 위, 오른쪽 위, 오른쪽 아래
    private static void Quad(VertexHelper vh, float xMin, float yMin, float xMax, float yMax, Color32 bottomLeft, Color32 topLeft, Color32 topRight, Color32 bottomRight)
    {
        if (xMax <= xMin || yMax <= yMin) return;
        int index = vh.currentVertCount;
        vh.AddVert(new Vector3(xMin, yMin), bottomLeft, Vector2.zero);
        vh.AddVert(new Vector3(xMin, yMax), topLeft, Vector2.zero);
        vh.AddVert(new Vector3(xMax, yMax), topRight, Vector2.zero);
        vh.AddVert(new Vector3(xMax, yMin), bottomRight, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index, index + 2, index + 3);
    }
}
