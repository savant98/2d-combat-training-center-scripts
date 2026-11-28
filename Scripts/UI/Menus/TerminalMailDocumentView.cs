using UnityEngine;

/// <summary>수신 문서를 화면 너비의 widthRatio(기본 절반)·90% 높이로 맞추고 원래 폰 양식을 스크롤한다(fitWholeForm이면 한 화면에 맞춘다).</summary>
public sealed class TerminalMailDocumentView : MonoBehaviour
{
    private const float MaxScale = 1.4f, MinScale = .25f;
    [SerializeField] private UnityEngine.UI.ScrollRect scroll;
    [SerializeField] private RectTransform form;
    [SerializeField] private CanvasGroup savedSignature;
    [Tooltip("켜면 양식 전체가 문서 창 높이 안에 들어오도록 배율을 높이까지 맞춰 스크롤 없이 한 화면에 보인다(입소 환영 문서). " +
             "끄면 지금처럼 폭에 맞추고 세로로 스크롤한다(소집 통지서).")]
    [SerializeField] private bool fitWholeForm;
    [Tooltip("문서 틀의 너비(화면 너비에 대한 비율). 입소 환영 문서는 2단 양식을 한 화면에 크게 담으려고 넓힌다(2026-11-15). 소집 통지서는 0.5")]
    [SerializeField, Range(.3f, .95f)] private float widthRatio = .5f;
    private RectTransform frame, viewportRect;
    private Vector2 lastViewport;
    private float lastFormHeight, lastFormWidth;
    public bool FitWholeForm => fitWholeForm;
    public float WidthRatio => widthRatio;
    private void OnEnable()
    {
        InputBindingHint.Bind(transform, "DocumentHint", fitWholeForm
            ? "{Cancel}  닫기" : "{MenuVertical} · 휠  문서 읽기     {Cancel}  닫기");
        frame = (RectTransform)transform;
        var terminal = GetComponentInParent<InventoryTerminalMenu>();
        viewportRect = terminal != null ? terminal.Viewport : null;
        if (viewportRect == null && GetComponentInParent<Canvas>() is Canvas canvas)
            viewportRect = (RectTransform)canvas.rootCanvas.transform;
        lastViewport = Vector2.zero;
        Fit();
        if (scroll != null) { scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; }
        // 보관 문서에서는 타이틀의 서명 입력·연출을 다시 실행하지 않는다.
        if (savedSignature != null) savedSignature.alpha = 0;
    }
    private void LateUpdate() { Fit(); }
    private void Fit()
    {
        if (frame == null || viewportRect == null || scroll == null || form == null) return;
        // 문서의 완성 크기는 팝업 무대 기준으로 잡아 펼침 애니메이션의 배율과 독립시킨다.
        float x = viewportRect.InverseTransformVector(frame.parent.TransformVector(Vector3.right)).magnitude;
        float y = viewportRect.InverseTransformVector(frame.parent.TransformVector(Vector3.up)).magnitude;
        Vector2 size = new Vector2(viewportRect.rect.width * widthRatio / Mathf.Max(.001f, x), viewportRect.rect.height * .9f / Mathf.Max(.001f, y));
        bool resized = (frame.sizeDelta - size).sqrMagnitude > .1f;
        if (resized) frame.sizeDelta = size;
        if (!resized && lastViewport == scroll.viewport.rect.size && Mathf.Approximately(lastFormHeight, form.rect.height)
            && Mathf.Approximately(lastFormWidth, form.rect.width)) return;
        // 배율은 양식의 실제 폭(기준 폭 상수가 아니라 form.rect.width)으로 정한다. 높이는 배율과 상관없이 먼저 구한다.
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(form);
        float height = Mathf.Max(form.rect.height, UnityEngine.UI.LayoutUtility.GetPreferredHeight(form));
        Rect viewport = scroll.viewport.rect;
        // 한 화면 문서는 높이로도 제한되므로 폭 배율 상한을 넓게 둔다(스크롤 문서는 예전 상한 그대로).
        float maxScale = fitWholeForm ? 2.2f : MaxScale;
        float scale = Mathf.Min(maxScale, Mathf.Max(MinScale, (viewport.width - 24) / Mathf.Max(1f, form.rect.width)));
        // 한 화면 문서는 높이에도 맞춘다(위아래 여백 16씩). 아주 작은 창에서 최소 배율에 걸리면 넘치는 만큼만 스크롤된다.
        if (fitWholeForm && height > 0) scale = Mathf.Max(MinScale, Mathf.Min(scale, (viewport.height - 32) / height));
        form.localScale = Vector3.one * scale;
        float scaled = height * scale;
        // 한 화면 문서는 남는 높이의 가운데에 둔다.
        if (fitWholeForm) form.anchoredPosition = new Vector2(form.anchoredPosition.x, -Mathf.Max(16f, (viewport.height - scaled) * .5f));
        scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(viewport.height, scaled + 32));
        lastViewport = viewport.size; lastFormHeight = form.rect.height; lastFormWidth = form.rect.width;
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
