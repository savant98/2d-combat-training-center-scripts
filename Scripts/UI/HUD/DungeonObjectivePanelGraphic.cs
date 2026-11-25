using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 던전 목표 패널·현장 단말기 목표 칸의 생성 Mesh 그래픽(3차, 2026-11-24). MaskableGraphic + OnPopulateMesh, 기본 UI 재질(정점 색).
/// - Plate: 앰버 판(미니맵과 같은 판 언어 — 반투명 어두운 판·45° 깎인 모서리·앰버 외곽선·밝은 꺾쇠, 머리말 없음).
/// - Cells: 진행 칸 total개(완료 = 앰버 채움 + 밝은 테두리, 미완료 = 어두운 채움 + 흐린 앰버 테두리). 오른쪽(또는 왼쪽) 정렬, 가운데 높이.
/// - Flash: 방금 완료한 칸 하나의 번쩍임(흰빛 앰버 채움 + 굵은 밝은 테두리). 패널이 CanvasRenderer 알파로 한 번 번쩍인다.
/// 진행이 바뀔 때만 Mesh를 다시 만든다.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DungeonObjectivePanelGraphic : MaskableGraphic
{
    public enum Part
    {
        Plate = 0,
        Cells = 1,
        Flash = 2,
    }

    /// <summary>칸 수 상한(목표 SO가 이보다 많이 정해도 이만큼만 그린다).</summary>
    public const int MaxCells = 12;

    [SerializeField] private Part part = Part.Cells;
    [SerializeField] private bool alignRight = true;
    [SerializeField, Min(2f)] private float cellWidth = 14f;
    [SerializeField, Min(0f)] private float cellGap = 5f;
    [Tooltip("칸 높이 / 사각형 높이")]
    [SerializeField, Range(.2f, 1f)] private float cellHeightRatio = .62f;

    // 앰버(목표 색 1, .69, .13 = #FFB021)와 판 색
    public static readonly Color32 Amber = new Color32(255, 176, 33, 255);
    public static readonly Color32 AmberBright = new Color32(255, 231, 179, 255);
    public static readonly Color32 AmberDim = new Color32(122, 82, 20, 255);
    public static readonly Color32 CellEmpty = new Color32(26, 19, 8, 230);
    public static readonly Color32 FlashFill = new Color32(255, 240, 205, 235);

    private static DungeonMapMesh.Style plateStyle;
    /// <summary>앰버 판 스타일(미니맵 판의 청록 선을 앰버로 바꾼 것).</summary>
    public static DungeonMapMesh.Style PlateStyle => plateStyle ??= new DungeonMapMesh.Style
    {
        plateFill = new Color32(16, 12, 6, 219),
        line = Amber,
        lineDim = AmberDim,
        lineBright = AmberBright,
    };

    private int total;
    private int doneMask;
    private int flashIndex = -1;
    private Color32 tint = Amber;
    private int lastVertexCount;

    public Part DrawPart
    {
        get => part;
        set { if (part != value) { part = value; SetVerticesDirty(); } }
    }

    public bool AlignRight
    {
        get => alignRight;
        set { if (alignRight != value) { alignRight = value; SetVerticesDirty(); } }
    }

    public int Total => total;
    public int DoneMask => doneMask;
    public int FlashIndex => flashIndex;
    public int LastVertexCount => lastVertexCount;

    /// <summary>칸 수·완료 비트(index 비트 = 완료)·색을 정한다. 같으면 다시 만들지 않는다.</summary>
    public void SetProgress(int count, int completedMask, Color32 color)
    {
        count = Mathf.Clamp(count, 0, MaxCells);
        if (count == total && completedMask == doneMask && color.r == tint.r && color.g == tint.g && color.b == tint.b && color.a == tint.a) return;
        total = count;
        doneMask = completedMask;
        tint = color;
        SetVerticesDirty();
    }

    /// <summary>Flash 층이 그릴 칸(-1이면 비움).</summary>
    public void SetFlash(int index)
    {
        if (flashIndex == index) return;
        flashIndex = index;
        SetVerticesDirty();
    }

    /// <summary>칸 index의 사각형(이 RectTransform 좌표).</summary>
    public Rect CellRect(int index)
    {
        var r = rectTransform.rect;
        float h = Mathf.Max(2f, r.height * cellHeightRatio);
        float width = total * cellWidth + Mathf.Max(0, total - 1) * cellGap;
        float x0 = alignRight ? r.xMax - width : r.xMin;
        float x = x0 + index * (cellWidth + cellGap);
        return new Rect(x, r.center.y - h * .5f, cellWidth, h);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var r = rectTransform.rect;
        switch (part)
        {
            case Part.Plate:
                DungeonMapMesh.WritePlate(vh, r, PlateStyle, 0f);
                break;
            case Part.Cells:
                for (int i = 0; i < total; i++)
                {
                    bool done = (doneMask & (1 << i)) != 0;
                    var cell = CellRect(i);
                    Quad(vh, cell, done ? tint : CellEmpty);
                    Frame(vh, cell, 1.5f, done ? (Color32)Color32.Lerp(tint, AmberBright, .55f) : (Color32)Color32.Lerp(tint, CellEmpty, .55f));
                }
                break;
            case Part.Flash:
                if (flashIndex >= 0 && flashIndex < total)
                {
                    var cell = CellRect(flashIndex);
                    var glow = new Rect(cell.xMin - 3f, cell.yMin - 3f, cell.width + 6f, cell.height + 6f);
                    Quad(vh, cell, FlashFill);
                    Frame(vh, glow, 2.5f, AmberBright);
                }
                break;
        }
        lastVertexCount = vh.currentVertCount;
    }

    private static void Quad(VertexHelper vh, Rect r, Color32 c)
    {
        if (r.width <= 0f || r.height <= 0f) return;
        int i = vh.currentVertCount;
        vh.AddVert(new Vector2(r.xMin, r.yMin), c, Vector2.zero);
        vh.AddVert(new Vector2(r.xMax, r.yMin), c, Vector2.zero);
        vh.AddVert(new Vector2(r.xMax, r.yMax), c, Vector2.zero);
        vh.AddVert(new Vector2(r.xMin, r.yMax), c, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }

    private static void Frame(VertexHelper vh, Rect r, float t, Color32 c)
    {
        Quad(vh, Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMin + t), c);
        Quad(vh, Rect.MinMaxRect(r.xMin, r.yMax - t, r.xMax, r.yMax), c);
        Quad(vh, Rect.MinMaxRect(r.xMin, r.yMin + t, r.xMin + t, r.yMax - t), c);
        Quad(vh, Rect.MinMaxRect(r.xMax - t, r.yMin + t, r.xMax, r.yMax - t), c);
    }

    /// <summary>지금 상태로 Mesh를 만들어 output에 채운다(검증용).</summary>
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
