using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class FullViewportDimmer : MonoBehaviour
{
    private RectTransform surface;

    private void OnEnable()
    {
        surface = (RectTransform)transform;
        if (TryGetComponent<MaskableGraphic>(out var graphic)) graphic.maskable = false;
        Canvas.preWillRenderCanvases += FitViewport;
        FitViewport();
    }

    private void OnDisable()
    {
        Canvas.preWillRenderCanvases -= FitViewport;
    }

    private void FitViewport()
    {
        var parent = surface.parent as RectTransform;
        var canvas = GetComponentInParent<Canvas>();
        if (parent == null || canvas == null) return;
        canvas = canvas.rootCanvas;

        bool overlay = canvas.renderMode == RenderMode.ScreenSpaceOverlay;
        Camera camera = overlay ? null : canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        if (!overlay && camera == null) return;

        Rect pixels = overlay ? canvas.pixelRect : camera.pixelRect;
        if (!Project(new Vector2(pixels.xMin, pixels.yMin), out Vector2 bottomLeft)
            || !Project(new Vector2(pixels.xMax, pixels.yMin), out Vector2 bottomRight)
            || !Project(new Vector2(pixels.xMin, pixels.yMax), out Vector2 topLeft)
            || !Project(new Vector2(pixels.xMax, pixels.yMax), out Vector2 topRight)) return;

        Vector2 min = Vector2.Min(Vector2.Min(bottomLeft, bottomRight), Vector2.Min(topLeft, topRight));
        Vector2 max = Vector2.Max(Vector2.Max(bottomLeft, bottomRight), Vector2.Max(topLeft, topRight));
        Vector2 size = max - min + 2f * Vector2.one;
        Vector2 position = min - parent.rect.min - Vector2.one + Vector2.Scale(size, surface.pivot);
        if (surface.anchorMin != Vector2.zero) surface.anchorMin = Vector2.zero;
        if (surface.anchorMax != Vector2.zero) surface.anchorMax = Vector2.zero;
        if (surface.sizeDelta != size) surface.sizeDelta = size;
        if (surface.anchoredPosition != position) surface.anchoredPosition = position;

        bool Project(Vector2 pixel, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pixel, camera, out local);
    }
}
