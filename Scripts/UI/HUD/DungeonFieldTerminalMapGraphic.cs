using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 현장 단말기 큰 지도의 생성 Mesh 그래픽(3차, 2026-11-24). MaskableGraphic + OnPopulateMesh, 기본 UI 재질(정점 색).
/// - Map: 알게 된 방 실루엣을 실제 비율로 단위 칸 블록 안에 놓고 출입구끼리 꺾인 선·텔레포트 점선·표식을 그린다(<see cref="DungeonMapMesh.WriteFloorMap"/>).
///   단말기를 열 때·지도 이동 때·원본이 바뀔 때만 다시 만든다.
/// - Player: Player 마름모(자기 사각형 가운데). 단말기가 anchoredPosition만 옮긴다.
/// - Legend: 범례 칩(현재 방·잠긴 출입구·텔레포트·수리대·목표·Player)을 legendSpacing 간격으로. 글자는 생성 도구가 같은 간격에 둔 TMP다.
/// - 5차(2026-11-25, DESIGN_V5 §1 S9): Player는 빛 고리 + 바라보는 쪽 꼬리(+x — 왼쪽은 단말기가 localScale.x를 뒤집는다), 새 층 Current = 지금 방 굵은 밝은 테두리 + 꺾쇠 4개 + 위 표지 삼각형,
///   지금 방이 영역 밖이면 가장자리 화살(<see cref="DungeonMapMesh.WriteCurrentFrame"/>). Current는 지도를 다시 만들 때만 Mesh를 만들고 단말기가 알파로 맥동시킨다.
/// - 7차(2026-11-26, DESIGN_V7 §1.3·§1.4): Legend 7칸(현재 방·잠긴 출입구·텔레포트·수리대·레버·출구·현재 위치, 간격은 생성 도구가 정한다 — 110).
///   새 층 Controls = 지도 머리줄 오른쪽 조작 버튼 3칸([−] 축소·[+] 확대·[현재 방] 조준 아이콘 — 어두운 칩 + 청록 테두리, 마우스를 올리면 밝게, 못 쓰면 흐림) + 왼쪽 배율 눈금 3칸.
///   아이콘은 Mesh(글꼴 기호에 기대지 않음). <see cref="SetControls"/> 값이 바뀔 때만 다시 그린다. 클릭 판정은 생성 도구가 같은 자리(<see cref="ControlRect"/>)에 둔
///   투명 hit Image + DungeonFieldTerminalPointer가 맡는다(이 그래픽은 레이 차단 끔).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DungeonFieldTerminalMapGraphic : MaskableGraphic
{
    public enum Part
    {
        Map = 0,
        Player = 1,
        Legend = 2,
        /// <summary>5차: 지금 방 맥동 테두리·꺾쇠·표지 삼각형(또는 화면 밖 화살).</summary>
        Current = 3,
        /// <summary>7차: 지도 조작 버튼 3칸([−]·[+]·[현재 방]) + 배율 눈금.</summary>
        Controls = 4,
    }

    /// <summary>7차 Controls 버튼 순서(왼쪽부터). 단말기·포인터·생성 도구가 같은 번호를 쓴다.</summary>
    public const int ControlZoomOut = 0, ControlZoomIn = 1, ControlRecenter = 2, ControlCount = 3;
    /// <summary>7차 Controls 버튼 한 칸 크기·간격·배율 눈금 자리(px). 버튼은 사각형 오른쪽 끝부터 왼쪽으로 놓는다.</summary>
    public static readonly Vector2 ControlSize = new Vector2(28f, 24f);
    public const float ControlGap = 6f, ControlTickGap = 8f, ControlTickSize = 6f, ControlTickSpacing = 3f;
    /// <summary>Controls 사각형 폭 = 버튼 3칸 + 간격 + 눈금 3칸(생성 도구가 이 폭으로 만든다).</summary>
    public static float ControlsWidth => ControlCount * ControlSize.x + (ControlCount - 1) * ControlGap + ControlTickGap + 3f * ControlTickSize + 2f * ControlTickSpacing + 2f;

    private static readonly Color32 ControlBack = new Color32(6, 14, 24, 235);
    private static readonly Color32 ControlLine = new Color32(43, 200, 226, 255);
    private static readonly Color32 ControlBright = new Color32(143, 244, 255, 255);
    private static readonly Color32 ControlDim = new Color32(23, 75, 92, 255);
    private static readonly Color32 ControlDimIcon = new Color32(60, 110, 124, 255);
    private static readonly Color32 ControlHoverBack = new Color32(14, 40, 54, 240);

    [SerializeField] private Part part = Part.Map;
    [Tooltip("Legend: 칩 간격(px)")]
    [SerializeField, Min(10f)] private float legendSpacing = 90f;
    [Tooltip("Player: 마름모 반대각선(px)")]
    [SerializeField, Min(1f)] private float playerHalf = 9f;
    [Tooltip("5차 Player: 빛 고리 + 바라보는 쪽 꼬리를 그린다")]
    [SerializeField] private bool playerHeading = true;

    private DungeonMapMesh.FloorModel model;
    private DungeonMapMesh.FloorView view;
    private int currentRoom = -1;
    private IDungeonDetailSource states;
    private IReadOnlyList<DungeonObjectiveMarker> objectives;
    private bool hasMap;
    private DungeonMapMesh.FloorMapStats lastStats;
    private int lastVertexCount, lastLegendChips;
    // 5차 Current 층
    private Rect currentBox;
    private bool hasCurrent;
    private DungeonMapMesh.CurrentFrameStats lastCurrent;
    private int setCurrentCount;
    // 7차 Controls 층
    private int controlLevel, controlLevels = 1, controlHover = -1, controlsBuilt;
    private bool controlZoomIn, controlZoomOut;

    public Part DrawPart
    {
        get => part;
        set { if (part != value) { part = value; SetVerticesDirty(); } }
    }

    public float LegendSpacing => legendSpacing;
    public bool HasMap => hasMap;
    public DungeonMapMesh.FloorView View => view;
    public DungeonMapMesh.FloorModel Model => model;
    public DungeonMapMesh.FloorMapStats LastStats => lastStats;
    public int LastVertexCount => lastVertexCount;
    public int LastLegendChips => lastLegendChips;
    public bool PlayerHeading => playerHeading;
    /// <summary>Current 층: 지금 방 실루엣 범위(화면 = 이 사각형 좌표)·있는지·마지막 결과·SetCurrent 횟수(검증용).</summary>
    public Rect CurrentBox => currentBox;
    public bool HasCurrent => hasCurrent;
    public DungeonMapMesh.CurrentFrameStats LastCurrent => lastCurrent;
    public int SetCurrentCount => setCurrentCount;
    /// <summary>7차 Controls: 지금 배율 단계·쓸 수 있는 단계 수·마우스가 올라간 버튼(−1 = 없음)·확대/축소 버튼을 쓸 수 있는지·다시 그린 횟수(검증용).</summary>
    public int ControlLevel => controlLevel;
    public int ControlLevels => controlLevels;
    public int ControlHover => controlHover;
    public bool ControlZoomInEnabled => controlZoomIn;
    public bool ControlZoomOutEnabled => controlZoomOut;
    public int ControlsBuiltCount => controlsBuilt;

    /// <summary>7차 Controls: 값이 바뀔 때만 다시 그린다(hover = 버튼 번호, −1 = 없음).</summary>
    public void SetControls(int level, int levels, int hover, bool zoomInEnabled, bool zoomOutEnabled)
    {
        levels = Mathf.Clamp(levels, 1, 3);
        level = Mathf.Clamp(level, 0, levels - 1);
        hover = hover >= 0 && hover < ControlCount ? hover : -1;
        if (level == controlLevel && levels == controlLevels && hover == controlHover && zoomInEnabled == controlZoomIn && zoomOutEnabled == controlZoomOut) return;
        controlLevel = level;
        controlLevels = levels;
        controlHover = hover;
        controlZoomIn = zoomInEnabled;
        controlZoomOut = zoomOutEnabled;
        controlsBuilt++;
        SetVerticesDirty();
    }

    /// <summary>7차 Controls 버튼 index(0 축소·1 확대·2 현재 방)의 사각형(이 그래픽 사각형 r 기준 — 오른쪽 끝부터).</summary>
    public static Rect ControlRect(Rect r, int index)
    {
        int fromRight = ControlCount - 1 - Mathf.Clamp(index, 0, ControlCount - 1);
        float xMax = r.xMax - fromRight * (ControlSize.x + ControlGap);
        return new Rect(xMax - ControlSize.x, r.center.y - ControlSize.y * .5f, ControlSize.x, ControlSize.y);
    }

    /// <summary>7차 배율 눈금 i(0 전체 … 2 확대)의 사각형(버튼 왼쪽).</summary>
    public static Rect ControlTickRect(Rect r, int i)
    {
        float right = ControlRect(r, 0).xMin - ControlTickGap;
        float x = right - (3 - Mathf.Clamp(i, 0, 2)) * ControlTickSize - (2 - Mathf.Clamp(i, 0, 2)) * ControlTickSpacing;
        float h = ControlTickSize + i * 2f;
        return new Rect(x, r.center.y - h * .5f, ControlTickSize, h);
    }

    /// <summary>5차 Current 층: 지금 방 범위(이 사각형 좌표)를 넘기고 다시 그리게 한다. has가 거짓이면 비운다.</summary>
    public void SetCurrent(Rect box, bool has)
    {
        currentBox = box;
        hasCurrent = has;
        setCurrentCount++;
        SetVerticesDirty();
    }

    public void SetFloor(DungeonMapMesh.FloorModel floorModel, int current, DungeonMapMesh.FloorView floorView, IDungeonDetailSource doorStates,
                         IReadOnlyList<DungeonObjectiveMarker> objectiveMarkers)
    {
        model = floorModel;
        currentRoom = current;
        view = floorView;
        states = doorStates;
        objectives = objectiveMarkers;
        hasMap = floorModel != null;
        SetVerticesDirty();
    }

    public void ClearFloor()
    {
        if (!hasMap && model == null) return;
        model = null;
        states = null;
        objectives = null;
        hasMap = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var r = rectTransform.rect;
        switch (part)
        {
            case Part.Map:
                lastStats = hasMap ? DungeonMapMesh.WriteFloorMap(vh, model, currentRoom, r, view, states, objectives) : default;
                break;
            case Part.Player:
            {
                float room = Mathf.Min(r.width, r.height) * .5f;
                DungeonMapMesh.WritePlayerMarker(vh, r.center, Mathf.Min(playerHalf, room / DungeonMapMesh.PlayerExtent(1f, playerHeading)), null, playerHeading);
                break;
            }
            case Part.Current:
                lastCurrent = hasCurrent ? DungeonMapMesh.WriteCurrentFrame(vh, r, currentBox) : default;
                break;
            case Part.Legend:
                lastLegendChips = DungeonMapMesh.WriteLegend(vh, r, legendSpacing);
                break;
            case Part.Controls:
                WriteControls(vh, r);
                break;
        }
        lastVertexCount = vh.currentVertCount;
    }

    // 7차: 조작 버튼 3칸 + 배율 눈금. 깎인 칩(왼쪽 위·오른쪽 아래 45°) + 얇은 테두리 + 얇은 선 아이콘(HUD 판 언어 — 굵은 부품 없음).
    private void WriteControls(VertexHelper vh, Rect r)
    {
        for (int i = 0; i < ControlCount; i++)
        {
            var cell = ControlRect(r, i);
            bool enabled = i == ControlZoomIn ? controlZoomIn : i == ControlZoomOut ? controlZoomOut : true;
            bool hover = enabled && controlHover == i;
            DungeonTerminalGaugeGraphic.Draw.Chamfer(vh, cell, 5f, hover ? ControlHoverBack : ControlBack);
            DungeonTerminalGaugeGraphic.Draw.ChamferOutline(vh, cell, 5f, hover ? 1.5f : 1f, enabled ? (hover ? ControlBright : ControlLine) : ControlDim);
            Color32 icon = enabled ? (hover ? ControlBright : ControlLine) : ControlDimIcon;
            var c = cell.center;
            float arm = 6f, t = 1.6f;
            switch (i)
            {
                case ControlZoomOut:
                    DungeonTerminalGaugeGraphic.Draw.Rect(vh, Rect.MinMaxRect(c.x - arm, c.y - t * .5f, c.x + arm, c.y + t * .5f), icon);
                    break;
                case ControlZoomIn:
                    DungeonTerminalGaugeGraphic.Draw.Cross(vh, c, arm, t, icon);
                    break;
                default:
                {
                    // 조준 아이콘: 가운데 작은 네모 테두리 + 네 방향 짧은 틱(지금 방으로 가운데 맞춤)
                    var box = new Rect(c.x - 4f, c.y - 4f, 8f, 8f);
                    DungeonTerminalGaugeGraphic.Draw.Outline(vh, box, 1.2f, icon);
                    DungeonTerminalGaugeGraphic.Draw.Rect(vh, Rect.MinMaxRect(c.x - .7f, c.y + 5.5f, c.x + .7f, c.y + 8.5f), icon);
                    DungeonTerminalGaugeGraphic.Draw.Rect(vh, Rect.MinMaxRect(c.x - .7f, c.y - 8.5f, c.x + .7f, c.y - 5.5f), icon);
                    DungeonTerminalGaugeGraphic.Draw.Rect(vh, Rect.MinMaxRect(c.x - 8.5f, c.y - .7f, c.x - 5.5f, c.y + .7f), icon);
                    DungeonTerminalGaugeGraphic.Draw.Rect(vh, Rect.MinMaxRect(c.x + 5.5f, c.y - .7f, c.x + 8.5f, c.y + .7f), icon);
                    DungeonTerminalGaugeGraphic.Draw.Rect(vh, new Rect(c.x - 1f, c.y - 1f, 2f, 2f), icon);
                    break;
                }
            }
        }
        // 배율 눈금: 쓸 수 있는 단계까지 테두리, 지금 단계까지 채움(지금 칸 밝게)
        for (int i = 0; i < 3; i++)
        {
            var tick = ControlTickRect(r, i);
            bool available = i < controlLevels;
            bool lit = available && i <= controlLevel;
            if (lit) DungeonTerminalGaugeGraphic.Draw.Rect(vh, tick, i == controlLevel ? ControlBright : ControlLine);
            else
            {
                DungeonTerminalGaugeGraphic.Draw.Rect(vh, tick, ControlBack);
                DungeonTerminalGaugeGraphic.Draw.Outline(vh, tick, 1f, available ? ControlLine : ControlDim);
            }
        }
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
