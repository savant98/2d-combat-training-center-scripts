using UnityEngine;

// 목표의 상태를 읽어 화면 밖에서만 표시하는 비상호작용 안내 화살표다.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WaveGoalIndicator : UnityEngine.UI.MaskableGraphic
{
    [SerializeField] private TutorialGoal goal;
    [SerializeField] private Canvas indicatorCanvas;
    [SerializeField, Min(0)] private float edgePadding = 42;
    private Camera viewCamera;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    private void LateUpdate()
    {
        if (indicatorCanvas == null) return;
        if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = Camera.main;
        if (goal == null || !goal.IsWaveGoalActive || viewCamera == null)
        {
            indicatorCanvas.enabled = false;
            return;
        }
        Vector3 projected = viewCamera.WorldToScreenPoint(goal.transform.position);
        float padding = Mathf.Max(edgePadding, rectTransform.rect.size.magnitude * .5f + 8) * indicatorCanvas.scaleFactor;
        bool show = TryPlace(viewCamera.pixelRect, Screen.safeArea, projected, padding, out var point, out var angle);
        indicatorCanvas.enabled = show;
        if (!show) return;
        var parent = rectTransform.parent as RectTransform;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, point, null, out var local))
            rectTransform.localPosition = new Vector3(local.x, local.y, 0);
        rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
    }

    /// <summary>화면 밖 좌표를 안전 영역의 가장 가까운 가장자리에 놓고 목표 방향을 반환한다.</summary>
    public static bool TryPlace(Rect viewport, Rect safeArea, Vector3 projected, float padding, out Vector2 point, out float angle)
    {
        point = default;
        angle = 0;
        if (projected.z > 0 && viewport.Contains(projected)) return false;
        var area = Rect.MinMaxRect(Mathf.Max(viewport.xMin, safeArea.xMin), Mathf.Max(viewport.yMin, safeArea.yMin),
            Mathf.Min(viewport.xMax, safeArea.xMax), Mathf.Min(viewport.yMax, safeArea.yMax));
        if (area.width <= 0 || area.height <= 0) return false;
        padding = Mathf.Clamp(padding, 0, Mathf.Min(area.width, area.height) * .45f);
        var inner = Rect.MinMaxRect(area.xMin + padding, area.yMin + padding, area.xMax - padding, area.yMax - padding);
        Vector2 target = projected;
        if (projected.z <= 0)
        {
            Vector2 direction = viewport.center - target;
            if (direction.sqrMagnitude < .001f) direction = Vector2.down;
            target = viewport.center + direction.normalized * Mathf.Max(viewport.width, viewport.height) * 2;
        }
        point = new Vector2(Mathf.Clamp(target.x, inner.xMin, inner.xMax), Mathf.Clamp(target.y, inner.yMin, inner.yMax));
        Vector2 aim = target - point;
        angle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
        return true;
    }

    protected override void OnDisable()
    {
        if (indicatorCanvas != null) indicatorCanvas.enabled = false;
        base.OnDisable();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        // +X를 가리키는 이중 화살촉이다. 회전해도 화면 가장자리 안에 들어온다.
        Quad(vh, new Vector2(-25, -22), new Vector2(7, -22), new Vector2(30, 0), new Vector2(-2, 0), new Color32(9, 19, 27, 245));
        Quad(vh, new Vector2(-2, 0), new Vector2(30, 0), new Vector2(7, 22), new Vector2(-25, 22), new Color32(9, 19, 27, 245));
        Quad(vh, new Vector2(-18, -16), new Vector2(5, -16), new Vector2(22, 0), new Vector2(-1, 0), color);
        Quad(vh, new Vector2(-1, 0), new Vector2(22, 0), new Vector2(5, 16), new Vector2(-18, 16), color);
        Quad(vh, new Vector2(-26, -12), new Vector2(-22, -12), new Vector2(-9, 0), new Vector2(-13, 0), Color.white);
        Quad(vh, new Vector2(-13, 0), new Vector2(-9, 0), new Vector2(-22, 12), new Vector2(-26, 12), Color.white);
    }

    private static void Quad(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero);
        vh.AddVert(c, tint, Vector2.zero); vh.AddVert(d, tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }
}
