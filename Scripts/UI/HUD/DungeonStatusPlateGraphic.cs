using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 방 상태 띠의 판(2026-11-24 던전형 Wave 2차). 미니맵 판과 같은 판 언어(<see cref="DungeonMapMesh.WritePlate"/>: 반투명 어두운 판,
/// 왼쪽 위·오른쪽 아래 45° 깎인 모서리, 얇은 외곽선, 밝은 꺾쇠)를 상태 색으로 그린다. 머리말 띠는 없다.
/// - Hostile: 붉은 선(#ff5a3c, TrainingBoardStyle.Warning) — 'HOSTILES n'
/// - Clear: 녹색 선(#33f259, 등급 '통과' 색) — 'ROOM CLEAR'
/// - Sector: 금색 선(#ffd166, TrainingBoardStyle.Gold) — 'SECTOR CLEAR'
/// - 4차(2026-11-25, DESIGN_V4 §2.6·§2.8):
///   Patrol: 어두운 앰버 선 — 재입장 순찰 'HOSTILES n · BYPASS'(잠기지 않음)
///   Exit: 앰버 선(목표 색 #FFB021) — 'EXIT UNLOCKED'
///   Threat: 난이도 단계 색(<see cref="Accent"/>, DungeonThreatText.Color) — 'THREAT 중급'. <see cref="RiseGlyph"/>가 켜지면 왼쪽 아이콘 자리에 위로 향한 삼각형(▲)을 같은 Mesh에 그린다
///   (정적 글꼴에 ▲ 글자가 없어서다).
/// 상태·강조색·삼각형이 바뀔 때만 Mesh를 다시 만든다. 재질은 기본 UI 재질(정점 색).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DungeonStatusPlateGraphic : MaskableGraphic
{
    public enum Tone
    {
        Hostile = 0,
        Clear = 1,
        Sector = 2,
        Patrol = 3,
        Exit = 4,
        Threat = 5,
    }

    [SerializeField] private Tone tone = Tone.Hostile;
    [Tooltip("▲ 삼각형 가운데(판 사각형 가운데 기준 px) — 띠의 경고 아이콘 자리")]
    [SerializeField] private Vector2 glyphOffset = new Vector2(-186f, 0f);
    [Tooltip("▲ 삼각형 반폭(px)")]
    [SerializeField, Min(2f)] private float glyphHalf = 9f;

    private static DungeonMapMesh.Style hostileStyle, clearStyle, sectorStyle, patrolStyle, exitStyle;
    private DungeonMapMesh.Style threatStyle;
    private Color32 accent = new Color32(255, 135, 31, 255);
    private bool riseGlyph;

    public Tone CurrentTone
    {
        get => tone;
        set { if (tone != value) { tone = value; SetVerticesDirty(); } }
    }

    /// <summary>Threat 판의 강조색(난이도 단계 색). 바뀔 때만 다시 만든다.</summary>
    public Color32 Accent
    {
        get => accent;
        set
        {
            if (accent.r == value.r && accent.g == value.g && accent.b == value.b && accent.a == value.a) return;
            accent = value;
            threatStyle = null;
            if (tone == Tone.Threat) SetVerticesDirty();
        }
    }

    /// <summary>왼쪽 아이콘 자리에 ▲ 삼각형을 그릴지(단계 상승 'THREAT ▲').</summary>
    public bool RiseGlyph
    {
        get => riseGlyph;
        set { if (riseGlyph != value) { riseGlyph = value; SetVerticesDirty(); } }
    }

    public Vector2 GlyphOffset => glyphOffset;
    public float GlyphHalf => glyphHalf;

    /// <summary>상태별 판 색·두께(1080p 기준 px). 34px 높이 띠에 맞게 깎임·꺾쇠를 줄였다. Threat은 기본 강조색(주황)으로 만든 것을 돌려준다.</summary>
    public static DungeonMapMesh.Style StyleFor(Tone t)
    {
        switch (t)
        {
            case Tone.Clear:
                return clearStyle ??= Make(new Color32(6, 16, 26, 219), new Color32(56, 182, 255, 255), new Color32(20, 74, 118, 255), new Color32(175, 232, 255, 255));
            case Tone.Sector:
                return sectorStyle ??= Make(new Color32(22, 17, 6, 219), new Color32(255, 209, 102, 255), new Color32(96, 72, 24, 255), new Color32(255, 236, 178, 255));
            case Tone.Patrol:
                // 어두운 앰버: 붉은 HOSTILES보다 덜 급하게(통과 가능) 보인다.
                return patrolStyle ??= Make(new Color32(16, 11, 4, 214), new Color32(184, 118, 26, 255), new Color32(70, 44, 10, 255), new Color32(226, 166, 72, 255));
            case Tone.Exit:
                return exitStyle ??= Make(new Color32(22, 15, 4, 219), new Color32(255, 176, 33, 255), new Color32(110, 72, 14, 255), new Color32(255, 231, 179, 255));
            case Tone.Threat:
                return AccentStyle(new Color32(255, 135, 31, 255));
            default:
                return hostileStyle ??= Make(new Color32(20, 9, 11, 219), new Color32(255, 90, 60, 255), new Color32(92, 26, 20, 255), new Color32(255, 140, 110, 255));
        }
    }

    /// <summary>강조색 하나로 판 색을 만든다(채움 = 강조색 8% + 어두운 바탕, 흐린 선 = 38%, 밝은 선 = 흰색 쪽 55%).</summary>
    public static DungeonMapMesh.Style AccentStyle(Color32 c)
    {
        Color a = c;
        Color32 fill = new Color(a.r * .08f + .03f, a.g * .08f + .03f, a.b * .08f + .03f, 219f / 255f);
        Color32 dim = new Color(a.r * .38f, a.g * .38f, a.b * .38f, 1f);
        Color32 bright = Color.Lerp(a, Color.white, .55f);
        Color32 line = new Color(a.r, a.g, a.b, 1f);
        return Make(fill, line, dim, bright);
    }

    private static DungeonMapMesh.Style Make(Color32 fill, Color32 line, Color32 lineDim, Color32 lineBright) => new DungeonMapMesh.Style
    {
        plateFill = fill,
        line = line,
        lineDim = lineDim,
        lineBright = lineBright,
        plateCut = 10f,
        plateInnerInset = 4f,
        bracketLength = 10f,
    };

    private DungeonMapMesh.Style CurrentStyle()
    {
        if (tone != Tone.Threat) return StyleFor(tone);
        return threatStyle ??= AccentStyle(accent);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var r = rectTransform.rect;
        var style = CurrentStyle();
        DungeonMapMesh.WritePlate(vh, r, style, 0f);
        if (riseGlyph) WriteRise(vh, r, style);
    }

    // 위로 향한 삼각형(▲): 어두운 바탕 한 겹 + 밝은 색 면. 판 사각형 안으로 붙잡는다(높이가 모자라면 줄인다).
    private void WriteRise(VertexHelper vh, Rect r, DungeonMapMesh.Style style)
    {
        float h = Mathf.Min(glyphHalf, r.height * .5f - 3f, r.width * .5f - 3f);
        if (h < 1.5f) return;
        var c = r.center + glyphOffset;
        c.x = Mathf.Clamp(c.x, r.xMin + h + 2f, r.xMax - h - 2f);
        c.y = Mathf.Clamp(c.y, r.yMin + h + 2f, r.yMax - h - 2f);
        Triangle(vh, c, h + 1.5f, style.plateFill);
        Triangle(vh, c, h, style.lineBright);
        Triangle(vh, c + new Vector2(0f, -h * .15f), h * .5f, style.line);
    }

    private static void Triangle(VertexHelper vh, Vector2 c, float h, Color32 tint)
    {
        int i = vh.currentVertCount;
        vh.AddVert(new Vector2(c.x - h, c.y - h * .8f), tint, Vector2.zero);
        vh.AddVert(new Vector2(c.x, c.y + h), tint, Vector2.zero);
        vh.AddVert(new Vector2(c.x + h, c.y - h * .8f), tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
    }

    /// <summary>지금 상태로 Mesh를 만들어 output에 채운다(검증용, 화면 갱신과 무관).</summary>
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
