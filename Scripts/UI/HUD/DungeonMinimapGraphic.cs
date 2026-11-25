using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 던전 미니맵의 생성 Mesh 그래픽(기존 HUD처럼 MaskableGraphic + OnPopulateMesh). 한 클래스가 세 층 중 하나를 그린다.
/// - Plate: 판(반투명 어두운 판·45° 깎인 모서리·얇은 청록 외곽·밝은 꺾쇠·머리말 띠). 정적.
/// - Map: 방 배치도(채움·외곽·다리·표식). 방 상태·화면 이동이 바뀔 때만 다시 만든다.
/// - Highlight: 현재 방 강조(옅은 채움 + 굵은 밝은 외곽). DungeonMinimap이 CanvasRenderer 알파로 느리게 맥동시킨다(Mesh 재생성 없음).
/// - ClearPulse(2차): 방금 클리어한 방의 녹색 펄스(옅은 초록 채움 + 굵은 밝은 초록 외곽). DungeonMinimap이 CanvasRenderer 알파로 한 번 번쩍인다.
/// 5차(2026-11-25): 미니맵 아래 구역 띠(SectorArea·SectorHighlight)도 같은 Map·Highlight 층으로 그린다.
/// Mesh는 <see cref="DungeonMapMesh"/>가 쓰고, 모든 정점은 이 RectTransform 사각형 안에 있다.
/// 재질은 기본 UI 재질(정점 색 = sRGB, Canvas가 감마 정점 색을 처리).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DungeonMinimapGraphic : MaskableGraphic
{
    public enum Part
    {
        Plate = 0,
        Map = 1,
        Highlight = 2,
        ClearPulse = 3,
    }

    [SerializeField] private Part part = Part.Map;
    [Tooltip("Plate: 머리말 높이(0이면 머리말 띠·선 없음)")]
    [SerializeField, Min(0f)] private float headerHeight = 36f;

    private IReadOnlyList<DungeonMapRoom> rooms;
    private int currentRoom = -1;
    private int pulseRoom = -1;
    private DungeonMapMesh.View view;
    private bool hasMap;
    private DungeonMapMesh.MapStats lastStats;
    private int lastVertexCount;
    private int setMapCount;

    public Part DrawPart
    {
        get => part;
        set { if (part != value) { part = value; SetVerticesDirty(); } }
    }

    public float HeaderHeight => headerHeight;
    /// <summary>마지막으로 만든 Map 층 결과(검증용).</summary>
    public DungeonMapMesh.MapStats LastStats => lastStats;
    /// <summary>마지막으로 만든 Mesh의 정점 수.</summary>
    public int LastVertexCount => lastVertexCount;
    public bool HasMap => hasMap;
    public int CurrentRoom => currentRoom;
    public DungeonMapMesh.View View => view;
    /// <summary>ClearPulse 층이 그리는 방 id(-1이면 없음).</summary>
    public int PulseRoom => pulseRoom;
    /// <summary>5차: SetMap을 부른 횟수(검증용 — 구역 띠가 '방 이동 때만 다시 만든다' 확인).</summary>
    public int SetMapCount => setMapCount;

    /// <summary>ClearPulse 층이 그릴 방을 정한다(-1이면 비움). 배치도는 SetMap으로 함께 받는다.</summary>
    public void SetPulseRoom(int roomId)
    {
        if (pulseRoom == roomId) return;
        pulseRoom = roomId;
        SetVerticesDirty();
    }

    /// <summary>그릴 방 목록·현재 방·화면 배치를 넘기고 다시 그리게 한다(Map·Highlight 층).</summary>
    public void SetMap(IReadOnlyList<DungeonMapRoom> mapRooms, int current, DungeonMapMesh.View mapView)
    {
        rooms = mapRooms;
        currentRoom = current;
        view = mapView;
        hasMap = mapRooms != null;
        setMapCount++;
        SetVerticesDirty();
    }

    public void ClearMap()
    {
        if (!hasMap && rooms == null) return;
        rooms = null;
        currentRoom = -1;
        hasMap = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var r = rectTransform.rect;
        switch (part)
        {
            case Part.Plate:
                DungeonMapMesh.WritePlate(vh, r, null, headerHeight);
                break;
            case Part.Map:
                lastStats = hasMap ? DungeonMapMesh.WriteMap(vh, rooms, currentRoom, r, view) : default;
                break;
            case Part.Highlight:
                if (hasMap) DungeonMapMesh.WriteHighlight(vh, rooms, currentRoom, r, view);
                break;
            case Part.ClearPulse:
                if (hasMap && pulseRoom >= 0) DungeonMapMesh.WriteClearPulse(vh, rooms, pulseRoom, r, view);
                break;
        }
        lastVertexCount = vh.currentVertCount;
    }

    /// <summary>지금 상태로 Mesh를 만들어 output에 채운다(검증·편집기 미리보기용, 화면 갱신과 무관).</summary>
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
