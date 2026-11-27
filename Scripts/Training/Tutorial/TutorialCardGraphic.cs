using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 기본 무장 소개 영상의 모션그래픽 언어(둥근 채운 판·부드러운 그림자·상단 광택·좌측 포인트 바·
/// 3D 입체 키캡·둥근 모서리 괄호·리더 라인·단계 진행 점·진행 막대)를 외부 텍스처 없이 UI Mesh로 그린다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TutorialCardGraphic : MaskableGraphic
{
    public enum Shape
    {
        Card,
        Keycap,
        BracketBox,
        LeaderLine,
        Pips,
        Dot,
        PillBar,
        Checkmark,
        Reticle
    }

    private const int CornerSegments = 6;

    [SerializeField] private Shape shape = Shape.Card;
    [SerializeField] private float cornerRadius = 20f;
    [SerializeField] private Color fillColor = new Color(0.055f, 0.067f, 0.086f, 0.92f);
    [SerializeField] private Color borderColor = new Color(1f, 1f, 1f, 0.14f);
    [SerializeField] private float borderWidth = 1.5f;
    [SerializeField] private Color accentColor = new Color(0.30f, 0.85f, 1f, 1f);
    [SerializeField] private float accentWidth = 7f;
    [SerializeField] private float shadowAlpha = 0.44f;
    [SerializeField] private float shadowSpread = 14f;
    [SerializeField] private float shadowOffsetY = -6f;
    [SerializeField] private float sheenAlpha = 0.08f;

    // 상태 매개변수(키캡 눌림, 괄호 잠금 진행, 진행 점, 진행 막대, 리더 라인)
    private float pressed;
    private float progress = 1f;
    private int pipCount = 1;
    private int pipActive = 1;
    private float pipPop;
    private Vector2 lineStart;
    private Vector2 lineEnd;
    private float bracketCorner = 26f;

    public override Texture mainTexture => Texture2D.whiteTexture;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    public void ConfigureCard(float radius, Color fill, Color border, float borderThickness,
        Color accent, float accentThickness = 7f, float shadow = 0.44f, float spread = 14f, float sheen = 0.08f)
    {
        float r = Mathf.Max(0f, radius);
        float bw = Mathf.Max(0f, borderThickness);
        float aw = Mathf.Max(0f, accentThickness);
        float sa = Mathf.Clamp01(shadow);
        float ss = Mathf.Max(0f, spread);
        float sh = Mathf.Clamp01(sheen);
        if (shape == Shape.Card && Mathf.Approximately(cornerRadius, r) && fillColor == fill && borderColor == border
            && Mathf.Approximately(borderWidth, bw) && accentColor == accent && Mathf.Approximately(accentWidth, aw)
            && Mathf.Approximately(shadowAlpha, sa) && Mathf.Approximately(shadowSpread, ss) && Mathf.Approximately(sheenAlpha, sh))
            return;
        shape = Shape.Card;
        cornerRadius = r;
        fillColor = fill;
        borderColor = border;
        borderWidth = bw;
        accentColor = accent;
        accentWidth = aw;
        shadowAlpha = sa;
        shadowSpread = ss;
        sheenAlpha = sh;
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void SetCardAccent(Color accent, Color border)
    {
        if (accentColor == accent && borderColor == border) return;
        accentColor = accent;
        borderColor = border;
        SetVerticesDirty();
    }

    public void ConfigureKeycap(Color accent, float radius = 8f)
    {
        shape = Shape.Keycap;
        accentColor = accent;
        cornerRadius = Mathf.Max(2f, radius);
        shadowAlpha = 0.42f;
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void SetKeycapState(float pressedValue, Color accent)
    {
        float clamped = Mathf.Clamp01(pressedValue);
        if (Mathf.Approximately(pressed, clamped) && accentColor == accent) return;
        pressed = clamped;
        accentColor = accent;
        SetVerticesDirty();
    }

    public void ConfigureBracketBox(Color accent, float stroke = 4f, float cornerLength = 26f)
    {
        shape = Shape.BracketBox;
        accentColor = accent;
        borderWidth = Mathf.Max(1f, stroke);
        bracketCorner = Mathf.Max(8f, cornerLength);
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void SetBracketState(float appearProgress, Color accent)
    {
        float clamped = Mathf.Clamp01(appearProgress);
        if (Mathf.Approximately(progress, clamped) && accentColor == accent) return;
        progress = clamped;
        accentColor = accent;
        SetVerticesDirty();
    }

    public void ConfigureLeaderLine(Color accent, float stroke = 3f)
    {
        shape = Shape.LeaderLine;
        accentColor = accent;
        borderWidth = Mathf.Max(1f, stroke);
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void SetLeaderLine(Vector2 from, Vector2 to, float drawProgress, Color accent)
    {
        float clamped = Mathf.Clamp01(drawProgress);
        if (lineStart == from && lineEnd == to && Mathf.Approximately(progress, clamped) && accentColor == accent) return;
        lineStart = from;
        lineEnd = to;
        progress = clamped;
        accentColor = accent;
        SetVerticesDirty();
    }

    public void ConfigurePips(int total, int active, Color accent, float popProgress = 0f)
    {
        int cnt = Mathf.Max(1, total);
        int act = Mathf.Clamp(active, 0, cnt);
        float pop = Mathf.Clamp01(popProgress);
        if (shape == Shape.Pips && pipCount == cnt && pipActive == act && accentColor == accent && Mathf.Approximately(pipPop, pop))
            return;
        shape = Shape.Pips;
        pipCount = cnt;
        pipActive = act;
        accentColor = accent;
        pipPop = pop;
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void ConfigureDot(Color accent)
    {
        if (shape == Shape.Dot && accentColor == accent) return;
        shape = Shape.Dot;
        accentColor = accent;
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void ConfigurePillBar(Color trackColor, Color fillStart, Color fillEnd, float value)
    {
        shape = Shape.PillBar;
        fillColor = trackColor;
        accentColor = fillStart;
        borderColor = fillEnd;
        progress = Mathf.Clamp01(value);
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void SetPillBarProgress(float value)
    {
        float clamped = Mathf.Clamp01(value);
        if (Mathf.Approximately(progress, clamped)) return;
        progress = clamped;
        SetVerticesDirty();
    }

    public void ConfigureCheckmark(Color accent, float stroke = 4.2f, float drawProgress = 1f)
    {
        float bw = Mathf.Max(1.5f, stroke);
        float p = Mathf.Clamp01(drawProgress);
        if (shape == Shape.Checkmark && accentColor == accent && Mathf.Approximately(borderWidth, bw) && Mathf.Approximately(progress, p))
            return;
        shape = Shape.Checkmark;
        accentColor = accent;
        borderWidth = bw;
        progress = p;
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void ConfigureReticle(Color accent, bool active, float stroke = 1.8f)
    {
        float bw = Mathf.Max(1f, stroke);
        float p = active ? 1f : 0f;
        if (shape == Shape.Reticle && accentColor == accent && Mathf.Approximately(borderWidth, bw) && Mathf.Approximately(progress, p))
            return;
        shape = Shape.Reticle;
        accentColor = accent;
        borderWidth = bw;
        progress = p;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0.5f || rect.height <= 0.5f) return;
        float alpha = color.a;
        if (alpha <= 0.001f) return;

        switch (shape)
        {
            case Shape.Card: DrawCard(vh, rect, alpha); break;
            case Shape.Keycap: DrawKeycap(vh, rect, alpha); break;
            case Shape.BracketBox: DrawBracketBox(vh, rect, alpha); break;
            case Shape.LeaderLine: DrawLeaderLine(vh, rect, alpha); break;
            case Shape.Pips: DrawPips(vh, rect, alpha); break;
            case Shape.Dot: DrawDot(vh, rect, alpha); break;
            case Shape.PillBar: DrawPillBar(vh, rect, alpha); break;
            case Shape.Checkmark: DrawCheckmark(vh, rect, alpha); break;
            case Shape.Reticle: DrawReticle(vh, rect, alpha); break;
        }
    }

    // ---------------------------------------------------------------- 카드
    private void DrawCard(VertexHelper vh, Rect rect, float alpha)
    {
        float r = ClampRadius(rect, cornerRadius);

        // 다층 소프트 그림자(블러 근사)
        if (shadowAlpha > 0.001f && shadowSpread > 0.5f)
        {
            const int layers = 4;
            for (int i = layers; i >= 1; i--)
            {
                float t = (float)i / layers;
                float expand = shadowSpread * t;
                float a = shadowAlpha * alpha * (1f - t * 0.72f) * 0.34f;
                var sr = new Rect(rect.xMin - expand, rect.yMin - expand + shadowOffsetY,
                    rect.width + expand * 2f, rect.height + expand * 2f);
                AddRoundedRect(vh, sr, r + expand * 0.65f, new Color(0f, 0f, 0f, a));
            }
        }

        // 어두운 무채색 바탕
        Color body = fillColor;
        body.a *= alpha;
        AddRoundedRect(vh, rect, r, body);

        // 상단 미세 광택 그라데이션(gfx.py:190)
        if (sheenAlpha > 0.001f)
        {
            Color top = new Color(1f, 1f, 1f, sheenAlpha * alpha);
            Color bottom = new Color(1f, 1f, 1f, 0f);
            AddRoundedRectVerticalGradient(vh, rect, r, top, bottom);
        }

        // 좌측 강조 세로 막대(카드 왼쪽 둥근 모서리에 맞춰 클리핑)
        if (accentWidth > 0.5f && accentColor.a > 0.001f)
        {
            Color acc = accentColor;
            acc.a *= alpha;
            float w = Mathf.Min(accentWidth, rect.width * 0.25f);
            AddLeftAccentBar(vh, rect, r, w, acc);
        }

        // 얇은 외곽 테두리
        if (borderWidth > 0.1f && borderColor.a > 0.001f)
        {
            Color rim = borderColor;
            rim.a *= alpha;
            AddRoundedBorder(vh, rect, r, borderWidth, rim);
        }
    }

    // ---------------------------------------------------------------- 3D 입체 키캡(gfx.py:232-250)
    private void DrawKeycap(VertexHelper vh, Rect rect, float alpha)
    {
        float h = rect.height;
        float r = ClampRadius(rect, Mathf.Min(cornerRadius, h * 0.22f));
        float fullDepth = Mathf.Clamp(h * 0.13f, 2.5f, 6.5f);
        float pressShift = fullDepth * 0.72f * pressed;
        float lipDepth = fullDepth - pressShift;

        // 부드러운 바닥 그림자
        if (shadowAlpha > 0.001f)
        {
            var sh1 = new Rect(rect.xMin - 3f, rect.yMin - 5f, rect.width + 6f, rect.height + 4f);
            AddRoundedRect(vh, sh1, r + 2f, new Color(0f, 0f, 0f, shadowAlpha * alpha * 0.35f));
            var sh2 = new Rect(rect.xMin - 1f, rect.yMin - 2.5f, rect.width + 2f, rect.height + 1f);
            AddRoundedRect(vh, sh2, r + 1f, new Color(0f, 0f, 0f, shadowAlpha * alpha * 0.55f));
        }

        // 입체 받침(아래 턱)
        Color lipColor = Color.Lerp(new Color(0.604f, 0.651f, 0.698f, 1f), accentColor * 0.68f, pressed);
        lipColor.a = alpha;
        var baseRect = new Rect(rect.xMin, rect.yMin, rect.width, Mathf.Max(2f, rect.height - pressShift));
        AddRoundedRect(vh, baseRect, r, lipColor);

        // 키캡 윗면(누르면 강조색으로 점등)
        Color whiteFace = new Color(0.957f, 0.969f, 0.980f, 1f);
        Color faceColor = Color.Lerp(whiteFace, accentColor, pressed > 0.25f ? Mathf.SmoothStep(0f, 1f, pressed) : 0f);
        faceColor.a = alpha;
        var topRect = new Rect(rect.xMin, rect.yMin + lipDepth, rect.width, Mathf.Max(2f, rect.height - fullDepth));
        AddRoundedRect(vh, topRect, r, faceColor);

        // 윗면 상단 하이라이트 반사
        if (topRect.height > 6f)
        {
            var hiRect = new Rect(topRect.xMin + 1.5f, topRect.yMin + topRect.height * 0.52f,
                Mathf.Max(1f, topRect.width - 3f), topRect.height * 0.44f);
            AddRoundedRect(vh, hiRect, Mathf.Max(1f, r * 0.75f), new Color(1f, 1f, 1f, (0.45f - 0.18f * pressed) * alpha));
        }

        // 눌렸을 때 은은한 외곽 발광
        if (pressed > 0.05f)
        {
            Color glow = accentColor;
            glow.a = 0.42f * pressed * alpha;
            AddRoundedBorder(vh, new Rect(rect.xMin - 2f, rect.yMin - 2f, rect.width + 4f, rect.height + 4f),
                r + 2f, 2.5f, glow);
        }
    }

    // ---------------------------------------------------------------- 둥근 모서리 괄호(gfx.py:473-490)
    private void DrawBracketBox(VertexHelper vh, Rect rect, float alpha)
    {
        if (progress <= 0.001f) return;
        float e = OutBack(progress);
        float inset = (1f - e) * 22f;
        float x0 = rect.xMin - inset;
        float y0 = rect.yMin - inset;
        float x1 = rect.xMax + inset;
        float y1 = rect.yMax + inset;
        float w = x1 - x0;
        float h = y1 - y0;
        float k = Mathf.Min(bracketCorner, w * 0.36f, h * 0.36f);
        float r = Mathf.Min(8f, k * 0.45f);
        float a = alpha * Mathf.Clamp01(progress * 2f);

        Color shadow = new Color(0f, 0f, 0f, a * 0.48f);
        Color glow = accentColor;
        glow.a *= a * 0.28f;
        Color main = accentColor;
        main.a *= a;

        DrawCornerBracket(vh, x0, y1, 1f, -1f, k, r, borderWidth + 5f, shadow);
        DrawCornerBracket(vh, x1, y1, -1f, -1f, k, r, borderWidth + 5f, shadow);
        DrawCornerBracket(vh, x0, y0, 1f, 1f, k, r, borderWidth + 5f, shadow);
        DrawCornerBracket(vh, x1, y0, -1f, 1f, k, r, borderWidth + 5f, shadow);

        DrawCornerBracket(vh, x0, y1, 1f, -1f, k, r, borderWidth + 3.5f, glow);
        DrawCornerBracket(vh, x1, y1, -1f, -1f, k, r, borderWidth + 3.5f, glow);
        DrawCornerBracket(vh, x0, y0, 1f, 1f, k, r, borderWidth + 3.5f, glow);
        DrawCornerBracket(vh, x1, y0, -1f, 1f, k, r, borderWidth + 3.5f, glow);

        DrawCornerBracket(vh, x0, y1, 1f, -1f, k, r, borderWidth, main);
        DrawCornerBracket(vh, x1, y1, -1f, -1f, k, r, borderWidth, main);
        DrawCornerBracket(vh, x0, y0, 1f, 1f, k, r, borderWidth, main);
        DrawCornerBracket(vh, x1, y0, -1f, 1f, k, r, borderWidth, main);
    }

    private static void DrawCornerBracket(VertexHelper vh, float px, float py, float sx, float sy,
        float arm, float r, float thickness, Color col)
    {
        // 세로 팔 → 둥근 모서리 호 → 가로 팔을 폴리라인으로 그린다.
        const int segs = 4;
        Vector2 prev = new Vector2(px, py + sy * arm);
        Vector2 cornerStart = new Vector2(px, py + sy * r);
        AddSegment(vh, prev, cornerStart, thickness, col);
        prev = cornerStart;
        Vector2 center = new Vector2(px + sx * r, py + sy * r);
        for (int i = 1; i <= segs; i++)
        {
            float t = (float)i / segs;
            float ang = t * Mathf.PI * 0.5f;
            Vector2 next = new Vector2(
                center.x - sx * Mathf.Cos(ang) * r,
                center.y - sy * Mathf.Sin(ang) * r);
            AddSegment(vh, prev, next, thickness, col);
            prev = next;
        }
        Vector2 end = new Vector2(px + sx * arm, py);
        AddSegment(vh, prev, end, thickness, col);
    }

    // ---------------------------------------------------------------- 리더 라인(gfx.py:437-455)
    private void DrawLeaderLine(VertexHelper vh, Rect rect, float alpha)
    {
        if (progress <= 0.001f) return;
        float dotP = OutBack(Mathf.Clamp01(progress / 0.35f));
        float lineP = OutCubic(Mathf.Clamp01((progress - 0.15f) / 0.65f));

        Color shadow = new Color(0f, 0f, 0f, alpha * 0.45f);
        Color main = accentColor;
        main.a *= alpha;
        Color halo = accentColor;
        halo.a *= alpha * 0.32f;

        if (lineP > 0.001f)
        {
            Vector2 end = Vector2.Lerp(lineStart, lineEnd, lineP);
            AddSegment(vh, lineStart, end, borderWidth + 3.5f, shadow);
            AddSegment(vh, lineStart, end, borderWidth, main);
        }

        if (dotP > 0.001f)
        {
            AddCircle(vh, lineStart, 11f * dotP, 16, halo);
            AddCircle(vh, lineStart, 6.5f * dotP, 16, shadow);
            AddCircle(vh, lineStart, 5f * dotP, 16, main);
        }
    }

    // ---------------------------------------------------------------- 단계 진행 점(gfx.py:421-435)
    private void DrawPips(VertexHelper vh, Rect rect, float alpha)
    {
        float r = Mathf.Min(rect.height * 0.36f, 5f);
        float gap = r * 1.9f;
        float step = r * 2f + gap;
        float startX = rect.xMin + r + 2f;
        float cy = rect.center.y;

        Color activeCol = accentColor;
        activeCol.a *= alpha;
        Color haloCol = accentColor;
        haloCol.a *= alpha * 0.25f;
        Color idleCol = new Color(1f, 1f, 1f, 0.36f * alpha);

        for (int i = 0; i < pipCount; i++)
        {
            Vector2 c = new Vector2(startX + i * step, cy);
            if (i < pipActive)
            {
                float s = 1f;
                if (i == pipActive - 1 && pipPop > 0f && pipPop < 1f)
                    s = 1f + 0.35f * Mathf.Sin(Mathf.PI * pipPop);
                AddCircle(vh, c, r * 1.85f * s, 14, haloCol);
                AddCircle(vh, c, r * s, 14, activeCol);
            }
            else
            {
                AddRing(vh, c, r - 0.8f, 1.6f, 14, idleCol);
            }
        }
    }

    // ---------------------------------------------------------------- 상태 점(templates.py:149-150)
    private void DrawDot(VertexHelper vh, Rect rect, float alpha)
    {
        float r = Mathf.Min(rect.width, rect.height) * 0.30f;
        Color halo = accentColor;
        halo.a *= alpha * 0.28f;
        Color core = accentColor;
        core.a *= alpha;
        AddCircle(vh, rect.center, r * 1.75f, 16, halo);
        AddCircle(vh, rect.center, r, 16, core);
    }

    // ---------------------------------------------------------------- 둥근 진행 막대(gfx.py:492-501)
    private void DrawPillBar(VertexHelper vh, Rect rect, float alpha)
    {
        float r = rect.height * 0.5f;
        Color track = fillColor;
        track.a *= alpha;
        AddRoundedRect(vh, rect, r, track);

        float fw = rect.width * Mathf.Clamp01(progress);
        if (fw > 1.5f)
        {
            var fillRect = new Rect(rect.xMin, rect.yMin, fw, rect.height);
            Color c0 = accentColor; c0.a *= alpha;
            Color c1 = borderColor; c1.a *= alpha;
            AddRoundedRectHorizontalGradient(vh, fillRect, Mathf.Min(r, fw * 0.5f), c0, c1);
        }
    }

    // ---------------------------------------------------------------- 체크마크(실습 성공 배지)
    private void DrawCheckmark(VertexHelper vh, Rect rect, float alpha)
    {
        if (progress <= 0.001f) return;
        Color col = accentColor;
        col.a *= alpha;
        Vector2 p0 = new Vector2(rect.xMin + rect.width * 0.20f, rect.yMin + rect.height * 0.52f);
        Vector2 p1 = new Vector2(rect.xMin + rect.width * 0.43f, rect.yMin + rect.height * 0.26f);
        Vector2 p2 = new Vector2(rect.xMin + rect.width * 0.82f, rect.yMin + rect.height * 0.76f);
        float first = Mathf.Clamp01(progress / 0.42f);
        Vector2 mid = Vector2.Lerp(p0, p1, first);
        AddSegment(vh, p0, mid, borderWidth, col);
        AddCircle(vh, p0, borderWidth * 0.5f, 10, col);
        AddCircle(vh, mid, borderWidth * 0.5f, 10, col);
        if (progress > 0.40f)
        {
            float second = Mathf.Clamp01((progress - 0.40f) / 0.60f);
            Vector2 end = Vector2.Lerp(p1, p2, second);
            AddSegment(vh, p1, end, borderWidth, col);
            AddCircle(vh, end, borderWidth * 0.5f, 10, col);
        }
    }

    // ---------------------------------------------------------------- 조준경 아이콘(자동 조준 HUD 상시 배지)
    private void DrawReticle(VertexHelper vh, Rect rect, float alpha)
    {
        Vector2 c = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.34f;
        bool active = progress >= 0.5f;
        Color main = accentColor;
        main.a *= alpha;

        if (active)
        {
            Color halo = accentColor;
            halo.a *= alpha * 0.22f;
            AddCircle(vh, c, radius * 1.25f, 20, halo);
        }

        AddRing(vh, c, radius, borderWidth, 24, main);
        float tickIn = radius * (active ? 0.52f : 0.78f);
        float tickOut = radius * 1.38f;
        AddSegment(vh, c + Vector2.up * tickIn, c + Vector2.up * tickOut, borderWidth, main);
        AddSegment(vh, c + Vector2.down * tickIn, c + Vector2.down * tickOut, borderWidth, main);
        AddSegment(vh, c + Vector2.left * tickIn, c + Vector2.left * tickOut, borderWidth, main);
        AddSegment(vh, c + Vector2.right * tickIn, c + Vector2.right * tickOut, borderWidth, main);
        if (active) AddCircle(vh, c, Mathf.Max(2f, borderWidth * 1.15f), 12, main);
    }

    // ---------------------------------------------------------------- 기하 원시 함수
    private static float ClampRadius(Rect rect, float radius) =>
        Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);

    private static void AddRoundedRect(VertexHelper vh, Rect rect, float r, Color col)
    {
        if (rect.width <= 0f || rect.height <= 0f || col.a <= 0.001f) return;
        r = ClampRadius(rect, r);
        Color32 c32 = col;
        int center = vh.currentVertCount;
        vh.AddVert(rect.center, c32, Vector2.zero);
        int ringStart = vh.currentVertCount;
        int count = EmitRoundedPerimeter(vh, rect, r, c32);
        for (int i = 0; i < count; i++)
            vh.AddTriangle(center, ringStart + i, ringStart + (i + 1) % count);
    }

    private static void AddRoundedRectVerticalGradient(VertexHelper vh, Rect rect, float r, Color top, Color bottom)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        r = ClampRadius(rect, r);
        int center = vh.currentVertCount;
        Color mid = Color.Lerp(bottom, top, 0.5f);
        vh.AddVert(rect.center, (Color32)mid, Vector2.zero);
        int ringStart = vh.currentVertCount;
        int count = EmitRoundedPerimeterGradient(vh, rect, r, top, bottom, true);
        for (int i = 0; i < count; i++)
            vh.AddTriangle(center, ringStart + i, ringStart + (i + 1) % count);
    }

    private static void AddRoundedRectHorizontalGradient(VertexHelper vh, Rect rect, float r, Color left, Color right)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        r = ClampRadius(rect, r);
        int center = vh.currentVertCount;
        Color mid = Color.Lerp(left, right, 0.5f);
        vh.AddVert(rect.center, (Color32)mid, Vector2.zero);
        int ringStart = vh.currentVertCount;
        int count = EmitRoundedPerimeterGradient(vh, rect, r, left, right, false);
        for (int i = 0; i < count; i++)
            vh.AddTriangle(center, ringStart + i, ringStart + (i + 1) % count);
    }

    private static void AddLeftAccentBar(VertexHelper vh, Rect rect, float r, float width, Color col)
    {
        // 왼쪽 가장자리의 둥근 모서리 곡선을 따라 폭 width만큼 띠를 채운다.
        Color32 c32 = col;
        float xCut = rect.xMin + width;
        float x0 = rect.xMin;
        float y0 = rect.yMin;
        float y1 = rect.yMax;
        var pts = new System.Collections.Generic.List<Vector2>(CornerSegments * 2 + 4);
        // 왼쪽 위 모서리(위에서 왼쪽으로)
        float cx = x0 + r, cyTop = y1 - r, cyBot = y0 + r;
        for (int i = 0; i <= CornerSegments; i++)
        {
            float ang = Mathf.Lerp(Mathf.PI * 0.5f, Mathf.PI, (float)i / CornerSegments);
            float px = cx + Mathf.Cos(ang) * r;
            float py = cyTop + Mathf.Sin(ang) * r;
            if (px <= xCut + 0.01f) pts.Add(new Vector2(px, py));
        }
        // 왼쪽 아래 모서리(왼쪽에서 아래로)
        for (int i = 0; i <= CornerSegments; i++)
        {
            float ang = Mathf.Lerp(Mathf.PI, Mathf.PI * 1.5f, (float)i / CornerSegments);
            float px = cx + Mathf.Cos(ang) * r;
            float py = cyBot + Mathf.Sin(ang) * r;
            if (px <= xCut + 0.01f) pts.Add(new Vector2(px, py));
        }
        // 아래 절단선 교점과 위 절단선 교점
        float dx = Mathf.Max(0f, r - width);
        float yInset = r > 0.01f && width < r ? r - Mathf.Sqrt(Mathf.Max(0f, r * r - dx * dx)) : 0f;
        pts.Add(new Vector2(xCut, y0 + yInset));
        pts.Add(new Vector2(xCut, y1 - yInset));
        if (pts.Count < 3) return;

        Vector2 sum = Vector2.zero;
        for (int i = 0; i < pts.Count; i++) sum += pts[i];
        int center = vh.currentVertCount;
        vh.AddVert(sum / pts.Count, c32, Vector2.zero);
        int start = vh.currentVertCount;
        for (int i = 0; i < pts.Count; i++) vh.AddVert(pts[i], c32, Vector2.zero);
        for (int i = 0; i < pts.Count; i++)
            vh.AddTriangle(center, start + i, start + (i + 1) % pts.Count);
    }

    private static void AddRoundedBorder(VertexHelper vh, Rect rect, float r, float thickness, Color col)
    {
        float t = Mathf.Min(thickness, Mathf.Min(rect.width, rect.height) * 0.45f);
        var inner = new Rect(rect.xMin + t, rect.yMin + t, rect.width - t * 2f, rect.height - t * 2f);
        float ir = Mathf.Max(0f, r - t);
        Color32 c32 = col;
        int outerStart = vh.currentVertCount;
        int count = EmitRoundedPerimeter(vh, rect, r, c32);
        int innerStart = vh.currentVertCount;
        EmitRoundedPerimeter(vh, inner, ir, c32);
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            vh.AddTriangle(outerStart + i, outerStart + next, innerStart + next);
            vh.AddTriangle(outerStart + i, innerStart + next, innerStart + i);
        }
    }

    private static int EmitRoundedPerimeter(VertexHelper vh, Rect rect, float r, Color32 col)
    {
        int count = 0;
        EmitArc(vh, rect.xMax - r, rect.yMax - r, r, 0f, Mathf.PI * 0.5f, col, ref count);
        EmitArc(vh, rect.xMin + r, rect.yMax - r, r, Mathf.PI * 0.5f, Mathf.PI, col, ref count);
        EmitArc(vh, rect.xMin + r, rect.yMin + r, r, Mathf.PI, Mathf.PI * 1.5f, col, ref count);
        EmitArc(vh, rect.xMax - r, rect.yMin + r, r, Mathf.PI * 1.5f, Mathf.PI * 2f, col, ref count);
        return count;
    }

    private static int EmitRoundedPerimeterGradient(VertexHelper vh, Rect rect, float r, Color a, Color b, bool vertical)
    {
        int count = 0;
        EmitArcGradient(vh, rect, rect.xMax - r, rect.yMax - r, r, 0f, Mathf.PI * 0.5f, a, b, vertical, ref count);
        EmitArcGradient(vh, rect, rect.xMin + r, rect.yMax - r, r, Mathf.PI * 0.5f, Mathf.PI, a, b, vertical, ref count);
        EmitArcGradient(vh, rect, rect.xMin + r, rect.yMin + r, r, Mathf.PI, Mathf.PI * 1.5f, a, b, vertical, ref count);
        EmitArcGradient(vh, rect, rect.xMax - r, rect.yMin + r, r, Mathf.PI * 1.5f, Mathf.PI * 2f, a, b, vertical, ref count);
        return count;
    }

    private static void EmitArc(VertexHelper vh, float cx, float cy, float r, float a0, float a1, Color32 col, ref int count)
    {
        for (int i = 0; i <= CornerSegments; i++)
        {
            float ang = Mathf.Lerp(a0, a1, (float)i / CornerSegments);
            vh.AddVert(new Vector3(cx + Mathf.Cos(ang) * r, cy + Mathf.Sin(ang) * r), col, Vector2.zero);
            count++;
        }
    }

    private static void EmitArcGradient(VertexHelper vh, Rect rect, float cx, float cy, float r, float a0, float a1,
        Color a, Color b, bool vertical, ref int count)
    {
        for (int i = 0; i <= CornerSegments; i++)
        {
            float ang = Mathf.Lerp(a0, a1, (float)i / CornerSegments);
            float px = cx + Mathf.Cos(ang) * r;
            float py = cy + Mathf.Sin(ang) * r;
            float t = vertical
                ? Mathf.InverseLerp(rect.yMin, rect.yMax, py)
                : Mathf.InverseLerp(rect.xMin, rect.xMax, px);
            Color c = vertical ? Color.Lerp(b, a, t) : Color.Lerp(a, b, t);
            vh.AddVert(new Vector3(px, py), (Color32)c, Vector2.zero);
            count++;
        }
    }

    private static void AddCircle(VertexHelper vh, Vector2 center, float radius, int sides, Color col)
    {
        if (radius <= 0.1f || col.a <= 0.001f) return;
        Color32 c32 = col;
        int cIdx = vh.currentVertCount;
        vh.AddVert(center, c32, Vector2.zero);
        int start = vh.currentVertCount;
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            vh.AddVert(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, c32, Vector2.zero);
        }
        for (int i = 0; i < sides; i++)
            vh.AddTriangle(cIdx, start + i, start + (i + 1) % sides);
    }

    private static void AddRing(VertexHelper vh, Vector2 center, float radius, float thickness, int sides, Color col)
    {
        if (radius <= 0.1f || col.a <= 0.001f) return;
        Color32 c32 = col;
        float rOut = radius + thickness * 0.5f;
        float rIn = Mathf.Max(0.1f, radius - thickness * 0.5f);
        int start = vh.currentVertCount;
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            vh.AddVert(center + dir * rOut, c32, Vector2.zero);
            vh.AddVert(center + dir * rIn, c32, Vector2.zero);
        }
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            int o0 = start + i * 2, i0 = o0 + 1;
            int o1 = start + next * 2, i1 = o1 + 1;
            vh.AddTriangle(o0, o1, i1);
            vh.AddTriangle(o0, i1, i0);
        }
    }

    private static void AddSegment(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color col)
    {
        Vector2 delta = b - a;
        if (delta.sqrMagnitude < 0.0001f || col.a <= 0.001f) return;
        Vector2 n = new Vector2(-delta.y, delta.x).normalized * (thickness * 0.5f);
        Color32 c32 = col;
        int idx = vh.currentVertCount;
        vh.AddVert(a - n, c32, Vector2.zero);
        vh.AddVert(a + n, c32, Vector2.zero);
        vh.AddVert(b + n, c32, Vector2.zero);
        vh.AddVert(b - n, c32, Vector2.zero);
        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx, idx + 2, idx + 3);
    }

    public static float OutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    public static float OutBack(float t, float overshoot = 1.65f)
    {
        t = Mathf.Clamp01(t) - 1f;
        return 1f + t * t * ((overshoot + 1f) * t + overshoot);
    }
}
