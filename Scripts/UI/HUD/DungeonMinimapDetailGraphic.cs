using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 던전 미니맵 '지금 방 자세히'(3차, 2026-11-24)의 생성 Mesh 그래픽. 한 클래스가 네 층 중 하나를 그린다(MaskableGraphic + OnPopulateMesh, 기본 UI 재질).
/// - Room: 지금 방 실루엣(실내 공기 채움·공기와 맞닿은 벽 테두리·OneWay 선). 방이 바뀔 때만 다시 만든다.
/// - Marks: 연결된 출입구와 상태(열림·잠김·봉인)·텔레포트·수리대·목표(완료 여부)·시설 표식. 방 변경·DetailChanged·ObjectiveChanged 때 다시 만든다.
/// - Pulse: 방 클리어 순간 녹색 펄스(실루엣을 초록으로). DungeonMinimap이 CanvasRenderer 알파로 한 번 번쩍인다.
/// - Player: 작은 흰 마름모 + 청록 테두리(자기 사각형 가운데). Mesh는 고정이고 DungeonMinimap이 anchoredPosition만 매 프레임 옮긴다.
/// - Guide(4차, 2026-11-25): 출구 해금 뒤 안내 — 지금 방이 출구 방이면 밝은 출구 표식, 아니면 출구 쪽 연결 출입구 테두리 + 겹 화살
///   (<see cref="DungeonMapMesh.WriteExitGuide"/>). 출구 상태·방이 바뀔 때만 다시 만들고, DungeonMinimap이 CanvasRenderer 알파로 맥동시킨다.
/// - 5차(2026-11-25, DESIGN_V5 §1 S9): Marks는 고정 크기 배지형 픽토그램(점프대·출구 램프·이웃 방 정보 점 포함 — 방 목록은 <see cref="SetRooms"/>, 7차 V2: 크레인 2종은 그리지 않음),
///   Player는 반대각선 8 + 빛 고리 + 바라보는 쪽 꼬리(+x — 왼쪽은 DungeonMinimap이 localScale.x를 뒤집는다), 새 층 Ping = 1.6초마다 퍼지는 고리
///   (Mesh 고정, DungeonMinimap이 localScale·알파만 바꾼다).
/// Mesh는 <see cref="DungeonMapMesh"/>가 쓰고, Room·Marks·Pulse의 정점은 이 RectTransform 사각형 안으로 잘린다.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DungeonMinimapDetailGraphic : MaskableGraphic
{
    public enum Part
    {
        Room = 0,
        Marks = 1,
        Pulse = 2,
        Player = 3,
        Guide = 4,
        /// <summary>5차: Player 핑 고리(자기 사각형 가운데, 반경 = 짧은 변 × pingRadius).</summary>
        Ping = 5,
    }

    [SerializeField] private Part part = Part.Room;
    [Tooltip("Player: 마름모 반대각선(px, 5차 8)")]
    [SerializeField, Min(1f)] private float playerHalf = 8f;
    [Tooltip("5차 Player: 빛 고리 + 바라보는 쪽 꼬리를 그린다")]
    [SerializeField] private bool playerHeading = true;
    [Tooltip("5차 Ping: 고리 반경(사각형 짧은 변의 반 대비)")]
    [SerializeField, Range(.1f, 1f)] private float pingRadius = .7f;

    private DungeonMapMesh.RoomSilhouette silhouette;
    private DungeonRoomDetail detail;
    private DungeonMapMesh.RoomView view;
    private IDungeonDetailSource states;
    private IReadOnlyList<DungeonObjectiveMarker> objectives;
    private IReadOnlyList<DungeonMapRoom> rooms;
    private bool hasRoom;
    private DungeonMapMesh.RoomMarkStats lastMarks;
    private int lastVertexCount;
    private int setCount;
    // 4차 출구 안내(Guide 층)
    private int guideSocket = -1;
    private bool exitHere;
    private DungeonMapMesh.ExitGuideStats lastGuide;

    public Part DrawPart
    {
        get => part;
        set { if (part != value) { part = value; SetVerticesDirty(); } }
    }

    public bool HasRoom => hasRoom;
    public DungeonMapMesh.RoomView View => view;
    public DungeonMapMesh.RoomSilhouette Silhouette => silhouette;
    /// <summary>마지막으로 만든 Marks 층 결과(검증용).</summary>
    public DungeonMapMesh.RoomMarkStats LastMarks => lastMarks;
    public int LastVertexCount => lastVertexCount;
    public float PlayerHalf => playerHalf;
    public bool PlayerHeading => playerHeading;
    /// <summary>Marks 층이 이웃 방 정보 점·레버 사용 가능 여부에 쓰는 방 목록(없으면 null).</summary>
    public IReadOnlyList<DungeonMapRoom> Rooms => rooms;
    /// <summary>SetRoom을 부른 횟수(검증용 — '방이 바뀔 때만 다시 만든다' 확인).</summary>
    public int SetCount => setCount;
    /// <summary>Guide 층: 안내하는 출입구 socket(-1 = 없음)·지금 방이 출구 방인지·마지막 결과(검증용).</summary>
    public int GuideSocket => guideSocket;
    public bool ExitHere => exitHere;
    public DungeonMapMesh.ExitGuideStats LastGuide => lastGuide;
    /// <summary>Guide 층에 그릴 것이 있는지.</summary>
    public bool HasGuide => hasRoom && (guideSocket >= 0 || exitHere);

    /// <summary>그릴 방을 넘기고 다시 그리게 한다(Room·Marks·Pulse 층). states·objectives는 Marks 층만 쓴다.</summary>
    public void SetRoom(DungeonMapMesh.RoomSilhouette roomSilhouette, DungeonRoomDetail roomDetail, DungeonMapMesh.RoomView roomView,
                        IDungeonDetailSource doorStates, IReadOnlyList<DungeonObjectiveMarker> objectiveMarkers)
    {
        silhouette = roomSilhouette;
        detail = roomDetail;
        view = roomView;
        states = doorStates;
        objectives = objectiveMarkers;
        hasRoom = roomSilhouette != null || roomDetail != null;
        setCount++;
        SetVerticesDirty();
    }

    /// <summary>
    /// 5차: Marks 층의 방 목록(이웃 방 정보 점·이 방 클리어 여부 — DungeonMinimap의 사본). 같은 목록이면 다시 만들지 않는다(SetRoom이 함께 다시 만든다).
    /// </summary>
    public void SetRooms(IReadOnlyList<DungeonMapRoom> mapRooms)
    {
        if (ReferenceEquals(rooms, mapRooms)) return;
        rooms = mapRooms;
        SetVerticesDirty();
    }

    /// <summary>Guide 층: 그릴 방과 안내(출입구 socket, 지금 방이 출구 방인지)를 넘긴다. 같은 값이면 다시 만들지 않는다.</summary>
    public void SetGuide(DungeonRoomDetail roomDetail, DungeonMapMesh.RoomView roomView, int socket, bool exitInRoom)
    {
        bool want = roomDetail != null;
        if (want == hasRoom && ReferenceEquals(detail, roomDetail) && socket == guideSocket && exitInRoom == exitHere
            && view.cell == roomView.cell && view.origin == roomView.origin) return;
        detail = roomDetail;
        view = roomView;
        guideSocket = socket;
        exitHere = exitInRoom;
        hasRoom = want;
        setCount++;
        SetVerticesDirty();
    }

    public void ClearRoom()
    {
        if (!hasRoom && silhouette == null && detail == null) return;
        silhouette = null;
        detail = null;
        states = null;
        objectives = null;
        hasRoom = false;
        guideSocket = -1;
        exitHere = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var r = rectTransform.rect;
        var style = DungeonMapMesh.Style.Default;
        int clipped = 0;
        switch (part)
        {
            case Part.Room:
                if (hasRoom) DungeonMapMesh.WriteSilhouette(vh, silhouette, r, view, style.roomAir, style.roomWall, style.oneWayLine, style, ref clipped);
                break;
            case Part.Marks:
                lastMarks = hasRoom ? DungeonMapMesh.WriteRoomMarks(vh, detail, states, objectives, r, view, style, true, rooms) : default;
                break;
            case Part.Pulse:
                if (hasRoom) DungeonMapMesh.WriteSilhouette(vh, silhouette, r, view, style.clearFill, style.clearLine, default, style, ref clipped);
                break;
            case Part.Player:
            {
                // 꼬리·빛 고리까지 사각형 안에 들어오게 반대각선을 줄인다.
                float room = Mathf.Min(r.width, r.height) * .5f;
                float half = Mathf.Min(playerHalf, room / (DungeonMapMesh.PlayerExtent(1f, playerHeading)));
                DungeonMapMesh.WritePlayerMarker(vh, r.center, half, style, playerHeading);
                break;
            }
            case Part.Ping:
                DungeonMapMesh.WritePing(vh, r.center, Mathf.Min(r.width, r.height) * .5f * pingRadius, style);
                break;
            case Part.Guide:
                lastGuide = HasGuide ? DungeonMapMesh.WriteExitGuide(vh, detail, guideSocket, exitHere, r, view, style) : default;
                break;
        }
        lastVertexCount = vh.currentVertCount;
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
