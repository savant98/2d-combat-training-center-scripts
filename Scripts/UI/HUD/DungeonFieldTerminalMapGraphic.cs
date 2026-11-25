using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 현장 단말기 큰 지도의 생성 Mesh 그래픽(3차, 2026-11-24). MaskableGraphic + OnPopulateMesh, 기본 UI 재질(정점 색).
/// - Map: 알게 된 방 실루엣을 실제 비율로 단위 칸 블록 안에 놓고 출입구끼리 꺾인 선·텔레포트 점선·표식을 그린다(<see cref="DungeonMapMesh.WriteFloorMap"/>).
///   단말기를 열 때·지도 이동 때·원본이 바뀔 때만 다시 만든다.
/// - Player: Player 마름모(자기 사각형 가운데). 단말기가 anchoredPosition만 옮긴다.
/// - Legend: 범례 칩(현재 방·잠긴 출입구·텔레포트·수리대·목표·Player)을 legendSpacing 간격으로. 글자는 생성 도구가 같은 간격에 둔 TMP다.
/// - 5차(2026-11-25, DESIGN_V5 §1 S9): Legend 9칸(현재 방·잠긴 출입구·텔레포트·수리대·레버·출구·크레인 경로·크레인 고정·현재 위치, 간격 90),
///   Player는 빛 고리 + 바라보는 쪽 꼬리(+x — 왼쪽은 단말기가 localScale.x를 뒤집는다), 새 층 Current = 지금 방 굵은 밝은 테두리 + 꺾쇠 4개 + 위 표지 삼각형,
///   지금 방이 영역 밖이면 가장자리 화살(<see cref="DungeonMapMesh.WriteCurrentFrame"/>). Current는 지도를 다시 만들 때만 Mesh를 만들고 단말기가 알파로 맥동시킨다.
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
    }

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
        }
        lastVertexCount = vh.currentVertCount;
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
