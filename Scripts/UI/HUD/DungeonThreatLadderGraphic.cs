using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 7차(DESIGN_V7 §3.3) 난이도 공통 모양 '세로 5칸 사다리 + ▲'을 그리는 생성 Mesh 그래픽(contracts7 소유 — 영역은 고치지 않는다).
/// 그리기는 <see cref="DungeonThreatGlyph"/> 한 곳이다. 현장 단말기 난이도 게이지(V6, map7 — DungeonFieldTerminalSetup이 만든다)와
/// 난이도 상승 계기(V8, flow7 — DungeonRoomStatusSetup이 만든다)가 같은 컴포넌트를 쓴다.
/// - Part.Ladder: 칸 5개 + (drawPointer면) 지금 단계 칸 옆 ▲.
/// - Part.Pointer: 이 Graphic 사각형 가운데에 ▲ 하나(오르는 연출은 이 Graphic의 RectTransform만 옮긴다 — Mesh 재생성 없음).
/// 상태(SetState)가 바뀔 때만 Mesh를 다시 만든다. 알파 맥동·나타남은 CanvasRenderer/CanvasGroup 알파로 한다(할당 없음).
/// 재질은 기본 UI 재질(정점 색). 레이 판정은 생성 도구가 끈다(raycastTarget = false). 크기 변형 없음.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DungeonThreatLadderGraphic : MaskableGraphic
{
    public enum Part
    {
        Ladder = 0,
        Pointer = 1,
    }

    [SerializeField] private Part part = Part.Ladder;
    [SerializeField] private DungeonThreatGlyph.Look look = DungeonThreatGlyph.Look.Default;
    [SerializeField, Tooltip("지금 단계(색·▲ 자리).")] private DungeonThreatLevel level = DungeonThreatLevel.Intro;
    [SerializeField, Tooltip("Ladder: 지금 단계 칸 옆 자리에 ▲를 그릴지. 움직이는 ▲는 Part.Pointer Graphic을 따로 두고 끈다.")] private bool drawPointer = true;

    // -1 = 입문부터 지금 단계까지 자동.
    private int litMask = -1;
    private int igniteMask;

    public Part CurrentPart => part;
    public DungeonThreatGlyph.Look CurrentLook => look;
    public DungeonThreatLevel Level => level;
    /// <summary>지금 켜진 칸 비트(bit i = 칸 i).</summary>
    public int LitMask => litMask < 0 ? DungeonThreatGlyph.MaskUpTo(level) : litMask;
    /// <summary>지금 흰빛으로 섞는 칸 비트.</summary>
    public int IgniteMask => igniteMask;
    public bool DrawPointer => drawPointer;

    /// <summary>상태를 바꾼다(같으면 다시 그리지 않는다). lit &lt; 0이면 입문부터 newLevel까지 켠다.</summary>
    public void SetState(DungeonThreatLevel newLevel, int lit = -1, int ignite = 0)
    {
        lit = lit < 0 ? -1 : lit & DungeonThreatGlyph.AllCells;
        ignite &= DungeonThreatGlyph.AllCells;
        if (newLevel == level && lit == litMask && ignite == igniteMask) return;
        level = newLevel;
        litMask = lit;
        igniteMask = ignite;
        SetVerticesDirty();
    }

    /// <summary>Ladder의 ▲ 표시를 켜고 끈다(바뀔 때만 다시 그린다).</summary>
    public void SetPointerVisible(bool visible)
    {
        if (drawPointer == visible) return;
        drawPointer = visible;
        SetVerticesDirty();
    }

    /// <summary>모양 값을 바꾼다(생성 도구·검증용 — 실행 중 매 프레임 부르지 않는다).</summary>
    public void SetLook(DungeonThreatGlyph.Look value)
    {
        look = value;
        SetVerticesDirty();
    }

    /// <summary>칸 step의 사각형(이 Graphic의 로컬 rect 기준).</summary>
    public Rect CellRect(int step) => DungeonThreatGlyph.CellRect(rectTransform.rect, step, look);

    /// <summary>▲ 가운데(이 Graphic의 로컬 rect 기준, position = 칸 위치 0~4 — 소수 허용).</summary>
    public Vector2 PointerCenter(float position) => DungeonThreatGlyph.PointerCenter(rectTransform.rect, position, look);

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var r = rectTransform.rect;
        if (part == Part.Pointer)
        {
            float half = look.pointerHalf > 0f ? look.pointerHalf : r.height * .5f;
            half = Mathf.Min(half, r.width * .5f - 1.5f, r.height * .5f - 1.5f);
            DungeonThreatGlyph.WritePointer(vh, r.center, half, DungeonThreatGlyph.PointerColor(level), look.slotFill);
            return;
        }
        DungeonThreatGlyph.WriteLadder(vh, r, level, LitMask, igniteMask, drawPointer, look);
    }

    /// <summary>지금 상태로 Mesh를 만들어 output에 채운다(검증용, 화면 갱신과 무관). 정점 수를 돌려준다.</summary>
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
