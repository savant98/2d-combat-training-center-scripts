using UnityEngine;

/// <summary>
/// Lobby 안내가 쓰는 길잡이 화살표(UI). 대상이 화면 밖이면 가장 가까운 화면 가장자리에서 그 방향을 가리키고,
/// 화면 안이면 대상의 위에서 아래를 가리키며 까딱인다. 표시 전용이고 입력을 받지 않는다. 실제 시간으로 움직인다.
/// 화면 밖일 때의 자리 계산은 WaveGoalIndicator와 같은 규칙을 쓴다.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class LobbyGuideArrow : UnityEngine.UI.MaskableGraphic
{
    [SerializeField, Tooltip("화살표 옆에 함께 놓는 이름표(돌지 않는다)")] private RectTransform label;
    [SerializeField, Min(0f), Tooltip("화면 가장자리에서 떨어지는 거리(px, 기준 해상도)")] private float edgePadding = 96f;
    [SerializeField, Tooltip("화면 안에 있을 때 대상(입구)에서 위로 띄우는 높이(칸). Player의 머리보다 높게, 위에 걸린 간판과 겹치지 않게 둔다")] private float hoverHeight = 2.4f;
    [SerializeField, Min(0f), Tooltip("까딱이는 폭(px)과 빠르기")] private float bobDistance = 14f;
    [SerializeField, Min(0f)] private float bobSpeed = 5f;
    [SerializeField, Tooltip("화살표 중심에서 이름표의 가까운 변까지의 거리(px)")] private float labelOffset = 42f;

    private Canvas rootCanvas;
    private Camera viewCamera;

    /// <summary>가리킬 대상. 비우면 그리지 않는다.</summary>
    public Transform Target { get; set; }
    /// <summary>지금 대상이 화면 안에 있어 그 위에서 가리키고 있는가.</summary>
    public bool IsOverTarget { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    private void LateUpdate()
    {
        if (rootCanvas == null)
        {
            var parentCanvas = GetComponentInParent<Canvas>();
            rootCanvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
        }
        if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = Camera.main;
        var parent = rectTransform.parent as RectTransform;
        if (Target == null || rootCanvas == null || viewCamera == null || parent == null) return;

        float scale = Mathf.Max(.01f, rootCanvas.scaleFactor);
        Vector3 projected = viewCamera.WorldToScreenPoint(Target.position);
        float bob = Mathf.Sin(Time.unscaledTime * bobSpeed) * bobDistance * scale;
        Vector2 point, towards;
        float angle;
        if (WaveGoalIndicator.TryPlace(viewCamera.pixelRect, Screen.safeArea, projected, edgePadding * scale, out point, out angle))
        {
            // 화면 밖: 가장자리에서 대상 쪽을 가리키며 그 방향으로 까딱인다.
            IsOverTarget = false;
            towards = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            point -= towards * (bobDistance * scale - bob);
        }
        else
        {
            // 화면 안: 대상 위에서 아래를 가리킨다.
            IsOverTarget = true;
            Vector3 above = viewCamera.WorldToScreenPoint(Target.position + Vector3.up * hoverHeight);
            point = new Vector2(above.x, above.y + bobDistance * scale + bob);
            angle = -90f;
            towards = Vector2.down;
        }

        Camera uiCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, point, uiCamera, out var local))
            rectTransform.localPosition = new Vector3(local.x, local.y, 0f);
        rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        if (label != null)
        {
            // 이름표는 화살표의 뒤쪽(가리키는 반대쪽)에 바로 세워 둔다. 옆을 가리킬 때도 화살표와 겹치지 않게 이름표의 반 크기만큼 더 물린다.
            Vector2 half = label.rect.size * .5f;
            float reach = labelOffset + Mathf.Abs(towards.x) * half.x + Mathf.Abs(towards.y) * half.y;
            Vector2 behind = point - towards * (reach * scale);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(label.parent as RectTransform, behind, uiCamera, out var labelLocal))
                label.localPosition = new Vector3(labelLocal.x, labelLocal.y, 0f);
        }
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        // +X를 가리키는 이중 화살촉(WaveGoalIndicator와 같은 모양): 어두운 바탕 → 색 면 → 흰 꼬리
        var shade = new Color32(9, 19, 27, 245);
        Quad(vh, new Vector2(-25, -22), new Vector2(7, -22), new Vector2(30, 0), new Vector2(-2, 0), shade);
        Quad(vh, new Vector2(-2, 0), new Vector2(30, 0), new Vector2(7, 22), new Vector2(-25, 22), shade);
        Quad(vh, new Vector2(-18, -16), new Vector2(5, -16), new Vector2(22, 0), new Vector2(-1, 0), color);
        Quad(vh, new Vector2(-1, 0), new Vector2(22, 0), new Vector2(5, 16), new Vector2(-18, 16), color);
        Color32 tail = new Color(1f, 1f, 1f, color.a);
        Quad(vh, new Vector2(-26, -12), new Vector2(-22, -12), new Vector2(-9, 0), new Vector2(-13, 0), tail);
        Quad(vh, new Vector2(-13, 0), new Vector2(-9, 0), new Vector2(-22, 12), new Vector2(-26, 12), tail);
    }

    private static void Quad(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero);
        vh.AddVert(c, tint, Vector2.zero); vh.AddVert(d, tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }
}
