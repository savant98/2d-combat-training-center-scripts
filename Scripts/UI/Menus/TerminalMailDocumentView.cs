using UnityEngine;

/// <summary>수신 문서를 화면 절반 너비·90% 높이로 맞추고 원래 폰 양식을 스크롤한다.</summary>
public sealed class TerminalMailDocumentView : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.ScrollRect scroll;
    [SerializeField] private RectTransform form;
    [SerializeField] private CanvasGroup savedSignature;
    private RectTransform frame, canvasRect;
    private Vector2 lastViewport;
    private float lastFormHeight;
    private void OnEnable()
    {
        frame = (RectTransform)transform;
        var canvas = GetComponentInParent<Canvas>();
        canvasRect = canvas != null ? (RectTransform)canvas.rootCanvas.transform : null;
        Fit();
        if (scroll != null) { scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; }
        // 보관 문서에서는 타이틀의 서명 입력·연출을 다시 실행하지 않는다.
        if (savedSignature != null) savedSignature.alpha = 0;
    }
    private void LateUpdate() { Fit(); }
    private void Fit()
    {
        if (frame == null || canvasRect == null || scroll == null || form == null) return;
        float x = canvasRect.InverseTransformVector(frame.TransformVector(Vector3.right)).magnitude;
        float y = canvasRect.InverseTransformVector(frame.TransformVector(Vector3.up)).magnitude;
        Vector2 size = new Vector2(canvasRect.rect.width * .5f / Mathf.Max(.001f, x), canvasRect.rect.height * .9f / Mathf.Max(.001f, y));
        bool resized = (frame.sizeDelta - size).sqrMagnitude > .1f;
        if (resized) frame.sizeDelta = size;
        if (!resized && lastViewport == scroll.viewport.rect.size && Mathf.Approximately(lastFormHeight, form.rect.height)) return;
        float scale = Mathf.Min(1.4f, Mathf.Max(.25f, (scroll.viewport.rect.width - 24) / 500f));
        form.localScale = Vector3.one * scale;
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(form);
        float height = Mathf.Max(form.rect.height, UnityEngine.UI.LayoutUtility.GetPreferredHeight(form));
        scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(scroll.viewport.rect.height, height * scale + 32));
        lastViewport = scroll.viewport.rect.size; lastFormHeight = form.rect.height;
    }
    public void ScrollBy(float direction)
    {
        if (scroll == null) return;
        float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
        if (overflow <= 0) return;
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + direction * 160f / overflow);
    }
}
