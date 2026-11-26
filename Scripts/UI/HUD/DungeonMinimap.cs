using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 던전 미니맵(오른쪽 위, 훈련 코너판 아래). 게임플레이(DungeonManager)가 <see cref="IDungeonMapSource"/>로 내주는 읽기 전용 상태·이벤트를 구독해 그리기만 한다
/// (AGENTS.md UI 접점 규칙). DungeonManager가 Resources/UI/DungeonMinimap Prefab을 UI Canvas의 TrainingScoreboard 바로 다음 형제로 만들고 Bind한다(MainScene 무수정).
/// - 그림: <see cref="DungeonMinimapGraphic"/> 세 층(판·배치도·현재 방 강조). 방 상태·현재 방이 바뀔 때만 배치도 Mesh를 다시 만든다.
///   2차(2026-11-24): 넷째 층 ClearPulse — 방을 클리어한 순간 그 방이 녹색으로 한 번 번쩍인다(clearPulseTime, 실제 시간).
///   Prefab에 이 층이 없으면(1차 Prefab) 실행 중 MapArea 아래 마지막 자식으로 만든다. 작은 방·텔레포트 연결·휴식 표식은 배치도 층이 그린다.
/// - 3차(2026-11-24, DESIGN_V3 '평소 HUD'): 평소 미니맵 = '지금 방 자세히'. 원본이 <see cref="IDungeonDetailSource"/>도 구현하면(DungeonManager) Bind에서 스스로 연결하고,
///   지금 방의 셀 실루엣(<see cref="DungeonMinimapDetailGraphic"/> Room — 방이 바뀔 때만 Mesh 재생성), 연결 출입구와 상태·텔레포트·수리대·목표 표식(Marks —
///   방 변경·DetailChanged·ObjectiveChanged 때만), 방 클리어 녹색 펄스(Pulse), Player 점(Player — Mesh 고정, anchoredPosition만 매 프레임)을 그린다.
///   방 상세가 없으면(원본이 상세를 내주지 않거나 그 방 상세가 null) 단위 칸 배치도로 돌아간다. 목표 표식의 완료 여부는 원본이 <see cref="IDungeonObjectiveSource"/>를
///   구현하면 그 표식 목록을 읽는다. 큰 지도(던전 전체)는 현장 단말기(<see cref="DungeonFieldTerminal"/>)가 맡는다.
///   상세 층이 Prefab에 없으면(2차 Prefab) 실행 중 MapArea 아래에 만든다.
/// - 4차(2026-11-25, DESIGN_V4 §2.6): 원본이 <see cref="IDungeonExitSource"/>도 구현하면(선택 인터페이스, 'is'로 확인) 함께 구독한다.
///   시작 방 출구 표식은 표식 층이 그린다(잠김 = 흐린 앰버, 열림 = 밝은 앰버 — <see cref="DungeonMapMesh.WriteRoomMarks"/>). 출구 해금 뒤에는 안내 층(Guide)이
///   지금 방이 출구 방이면 밝은 출구 표식 + 테두리를, 아니면 TryGetExitGuide가 준 출입구에 테두리 + 겹 화살을 그리고 exitPulsePeriod로 맥동한다(Mesh는 그대로, 알파만).
///   Guide 층이 Prefab에 없으면(3차 Prefab) 실행 중 MapArea 아래 Player 바로 앞에 만든다.
/// - 화면 이동(배치도 모드): 칸 크기는 표시할 방들이 판에 맞게 자동(최대 maxCell). 던전이 커서 넘치면 현재 방을 가운데로 둔다. 방이 바뀌면 panTime 동안 부드럽게 옮긴다.
///   새 던전(층)이 시작되면 바로 맞춘다.
/// - 보임: 던전 진행 중 && PlayerHUD가 보임(UIManager.HUDVisible이 바꾸는 PlayerHUD CanvasGroup 알파) && 패배 화면·결과 보드가 열려 있지 않음.
///   HUD·가리는 화면 판정은 방 상태 띠와 함께 쓰는 <see cref="DungeonHudGate"/>가 한다.
/// - 시간: 실제 시간(Time.unscaledDeltaTime) — 히트스톱·사망 정지(timeScale 0)·일시정지 중에도 일관. 매 프레임 할당 없음.
/// - 5차(2026-11-25, DESIGN_V5 §1 S9): 표식은 고정 크기 배지형 픽토그램(점프대·출구 램프·출입구 이웃 방 정보 점 — 표식 층에 방 목록 사본을 넘긴다).
///   7차(2026-11-26, DESIGN_V7 V2): 경로 크레인·고정 크레인 표식은 그리지 않는다(DungeonMapMesh가 빈 case로 건너뜀 — 데이터는 계약대로 남는다).
///   Player 표식 = 반대각선 8 + 빛 고리 + 바라보는 쪽 꼬리(원본이 <see cref="IDungeonPlayerHeadingSource"/>도 구현하면 — 선택 인터페이스 — 바뀔 때만 localScale.x를 ±1로,
///   Mesh 재생성 없음) + 1.6초마다 퍼지는 핑 고리(PlayerPing 층, localScale·알파만). 판 아래 구역 띠(판 300×276): 왼쪽 = 던전 전체 단위 칸 배치도(SectorArea·지금 방 맥동,
///   MapChanged·RoomEntered 등 Refresh 때만 다시 만듦), 오른쪽 = 지금 방 이름(START ROOM·COMBAT ROOM·BOSS ROOM)·상태(CLEAR·HOSTILE). 구역 띠 부품이 없으면(4차 Prefab) 건너뛴다.
/// Prefab은 tools/Unity/DungeonMinimapSetup.cs가 만든다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonMinimap : MonoBehaviour, IDungeonMapView, IDungeonDetailView, IDungeonObjectiveView
{
    public const string ResourcePath = "UI/DungeonMinimap";

    [Header("Parts")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private DungeonMinimapGraphic plate;
    [SerializeField] private DungeonMinimapGraphic map;
    [SerializeField] private DungeonMinimapGraphic highlight;
    [Tooltip("방 클리어 녹색 펄스 층(비어 있으면 실행 중 MapArea 아래에 만든다)")]
    [SerializeField] private DungeonMinimapGraphic clearPulse;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text floorText;

    [Header("지금 방 자세히(3차 — 비어 있으면 실행 중 MapArea 아래에 만든다)")]
    [SerializeField] private DungeonMinimapDetailGraphic roomLayer;
    [SerializeField] private DungeonMinimapDetailGraphic marksLayer;
    [SerializeField] private DungeonMinimapDetailGraphic roomPulseLayer;
    [SerializeField] private DungeonMinimapDetailGraphic playerLayer;
    [Tooltip("4차 출구 안내 층(비어 있으면 실행 중 Player 바로 앞에 만든다)")]
    [SerializeField] private DungeonMinimapDetailGraphic guideLayer;
    [Tooltip("5차 Player 핑 고리 층(비어 있으면 핑 없음)")]
    [SerializeField] private DungeonMinimapDetailGraphic pingLayer;

    [Header("5차 구역 띠(판 아래 — 비어 있으면 없음)")]
    [Tooltip("던전 전체 단위 칸 배치도(DungeonMinimapGraphic Map)")]
    [SerializeField] private DungeonMinimapGraphic sectorMap;
    [Tooltip("구역 띠의 지금 방 맥동(DungeonMinimapGraphic Highlight)")]
    [SerializeField] private DungeonMinimapGraphic sectorHighlight;
    [SerializeField] private TMP_Text zoneText;
    [SerializeField] private TMP_Text zoneStateText;
    [SerializeField] private string startRoomLabel = "START ROOM";
    [SerializeField] private string combatRoomLabel = "COMBAT ROOM";
    [SerializeField] private string bossRoomLabel = "BOSS ROOM";
    [SerializeField] private string clearStateLabel = "CLEAR";
    [SerializeField] private string hostileStateLabel = "HOSTILE";
    [SerializeField] private Color zoneClearColor = new Color32(51, 242, 89, 255);
    [SerializeField] private Color zoneHostileColor = new Color32(255, 179, 71, 255);
    [SerializeField, Min(1f)] private float sectorMaxCell = 12f;
    [SerializeField, Min(1f)] private float sectorMinCell = 3f;

    [Header("Labels (정적 글꼴 — 영문 대문자·숫자만)")]
    [SerializeField] private string title = "SECTOR MAP";
    [Tooltip("지금 방 자세히 모드의 제목")]
    [SerializeField] private string detailTitle = "ROOM SCAN";
    [Tooltip("층 표시 형식: {0} = 던전 번호, {1} = 던전 수")]
    [SerializeField] private string floorFormat = "F{0}/{1}";
    [SerializeField] private string clearedSuffix = " CLEAR";

    [Header("Layout")]
    [SerializeField, Min(4f)] private float maxCell = 26f;
    [SerializeField, Min(2f)] private float minCell = 12f;
    [SerializeField, Range(0f, .5f)] private float gapRatio = .18f;
    [SerializeField, Min(0f)] private float padding = 4f;
    [Tooltip("지금 방 자세히: 판 가장자리 여백(px)")]
    [SerializeField, Min(0f)] private float detailPadding = 6f;
    [Tooltip("지금 방 자세히: 셀 한 칸 최대 px(작은 방이 지나치게 커지지 않게)")]
    [SerializeField, Min(1f)] private float detailMaxCell = 9f;

    [Header("Motion (실제 시간)")]
    [SerializeField, Min(.01f)] private float fadeInTime = .2f;
    [SerializeField, Min(.01f)] private float fadeOutTime = .25f;
    [SerializeField, Min(.01f)] private float panTime = .25f;
    [Tooltip("현재 방 강조의 맥동 주기(초)")]
    [SerializeField, Min(.1f)] private float pulsePeriod = 1.6f;
    [SerializeField, Range(0f, 1f)] private float pulseMin = .2f;
    [SerializeField, Range(0f, 1f)] private float pulseMax = .85f;
    [Tooltip("방 클리어 녹색 펄스 길이(초, 실제 시간): 한 번 크게 번쩍이고 작게 한 번 더 번쩍인 뒤 사라진다")]
    [SerializeField, Min(.1f)] private float clearPulseTime = .9f;
    [Tooltip("Player 점 깜박임 주기(초, 실제 시간)")]
    [SerializeField, Min(.1f)] private float playerBlinkPeriod = 1.1f;
    [Tooltip("4차 출구 안내 맥동 주기(초, 실제 시간)")]
    [SerializeField, Min(.1f)] private float exitPulsePeriod = 1f;
    [SerializeField, Range(0f, 1f)] private float exitPulseMin = .35f;
    [Tooltip("5차 Player 핑 주기(초, 실제 시간) — 0.6배 → 1.8배로 퍼지며 사라진다")]
    [SerializeField, Min(.2f)] private float pingPeriod = 1.6f;
    [SerializeField, Range(0f, 1f)] private float pingAlpha = .8f;

    private IDungeonMapSource source;
    private IDungeonDetailSource detailSource;
    private IDungeonObjectiveSource objectiveSource;
    private IDungeonExitSource exitSource;
    private IDungeonPlayerHeadingSource headingSource;
    private readonly System.Collections.Generic.List<DungeonMapRoom> shownRooms = new System.Collections.Generic.List<DungeonMapRoom>();
    private readonly System.Collections.Generic.Dictionary<int, DungeonMapMesh.RoomSilhouette> silhouettes = new System.Collections.Generic.Dictionary<int, DungeonMapMesh.RoomSilhouette>();
    private int shownCurrent = -1;
    private bool initialized;
    private float clock;
    private float alpha;
    private int floorKey;             // 0 = 던전 없음, 그 밖 = 던전 번호(바뀌면 화면 이동 없이 바로 맞춘다)
    private bool floorCleared;
    private DungeonMapMesh.View shownView, fromView, targetView;
    private bool hasView, panning;
    private float panStart;
    private int pulseRoom = -1;
    private float pulseStart;
    // 지금 방 자세히
    private bool detailMode;
    private int detailRoom = -1;
    private DungeonRoomDetail detailRef;
    private DungeonMapMesh.RoomSilhouette detailSilhouette;
    private DungeonMapMesh.RoomView detailView;
    private int detailPulseRoom = -1;
    private float detailPulseStart;
    private bool playerVisible;
    // 5차: Player 방향·핑·구역 띠
    private int facing = 1;
    private float pingScale = 1f;
    private string zoneStart, zoneCombat, zoneBoss;
    private bool sectorShown;

    // ===== 검증·디버그가 읽는 값 =====
    public IDungeonMapSource Source => source;
    public IDungeonDetailSource DetailSource => detailSource;
    public IDungeonObjectiveSource ObjectiveSource => objectiveSource;
    public IDungeonExitSource ExitSource => exitSource;
    /// <summary>지금 그리는 방 상태 사본과 현재 방(던전이 끝난 뒤에도 페이드아웃 동안 남는다).</summary>
    public System.Collections.Generic.IReadOnlyList<DungeonMapRoom> ShownRooms => shownRooms;
    public int ShownCurrentRoom => shownCurrent;
    public float Alpha => alpha;
    public bool IsPanning => panning;
    public DungeonMapMesh.View CurrentView => shownView;
    public DungeonMapMesh.View TargetView => targetView;
    public DungeonMinimapGraphic PlateGraphic => plate;
    public DungeonMinimapGraphic MapGraphic => map;
    public DungeonMinimapGraphic HighlightGraphic => highlight;
    public DungeonMinimapGraphic ClearPulseGraphic => clearPulse;
    /// <summary>녹색 펄스 중인 방 id(-1이면 없음).</summary>
    public int PulseRoom => pulseRoom;
    public float ClearPulseTime => clearPulseTime;
    public TMP_Text TitleText => titleText;
    public TMP_Text FloorText => floorText;
    public float PanTime => panTime;
    public DungeonMapMesh.FitOptions FitOptions => new DungeonMapMesh.FitOptions { maxCell = maxCell, minCell = minCell, gapRatio = gapRatio, padding = padding };
    /// <summary>지금 방 자세히 모드인지(방 상세가 있을 때).</summary>
    public bool IsDetailMode => detailMode;
    public int DetailRoom => detailRoom;
    public DungeonMapMesh.RoomView DetailView => detailView;
    public DungeonMapMesh.RoomSilhouette DetailSilhouette => detailSilhouette;
    public DungeonMinimapDetailGraphic RoomLayer => roomLayer;
    public DungeonMinimapDetailGraphic MarksLayer => marksLayer;
    public DungeonMinimapDetailGraphic RoomPulseLayer => roomPulseLayer;
    public DungeonMinimapDetailGraphic PlayerLayer => playerLayer;
    public DungeonMinimapDetailGraphic GuideLayer => guideLayer;
    public float ExitPulsePeriod => exitPulsePeriod;
    /// <summary>지금 방 자세히의 녹색 펄스 중인 방(-1이면 없음).</summary>
    public int DetailPulseRoom => detailPulseRoom;
    public bool PlayerVisible => playerVisible;
    /// <summary>캐시한 방 실루엣 수(던전이 바뀌면 비운다).</summary>
    public int CachedSilhouettes => silhouettes.Count;
    // 5차
    public IDungeonPlayerHeadingSource HeadingSource => headingSource;
    /// <summary>Player 표식이 바라보는 쪽(+1/−1 — localScale.x).</summary>
    public int PlayerFacing => facing;
    public DungeonMinimapDetailGraphic PingLayer => pingLayer;
    /// <summary>지금 핑 고리 배율(0.6~1.8).</summary>
    public float PingScale => pingScale;
    public float PingPeriod => pingPeriod;
    public DungeonMinimapGraphic SectorMapGraphic => sectorMap;
    public DungeonMinimapGraphic SectorHighlightGraphic => sectorHighlight;
    public TMP_Text ZoneText => zoneText;
    public TMP_Text ZoneStateText => zoneStateText;
    /// <summary>구역 띠 배치도를 그리고 있는지(지금 방 자세히 모드일 때).</summary>
    public bool SectorShown => sectorShown;

    /// <summary>HUD 숨김을 따를 CanvasGroup을 직접 지정한다(검증용). null이면 장면의 PlayerHUD CanvasGroup을 찾는다.</summary>
    public CanvasGroup HudGroupOverride { get; set; }

    private void Awake() => EnsureInit();

    private void EnsureInit()
    {
        if (initialized) return;
        initialized = true;
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        // 정적 글꼴에 공백이 없어 여백 태그를 쓴다 — 옛 Prefab(richText 꺼짐)에서도 태그가 글자로 보이지 않게 켠다.
        if (titleText != null) { titleText.richText = true; titleText.text = DungeonUiText.Static(title); }
        if (floorText != null) floorText.richText = true;
        // 5차 구역 띠 글자(공백 여백 태그 변환은 한 번만 — Refresh 때 할당 없음)
        zoneStart = DungeonUiText.Static(startRoomLabel);
        zoneCombat = DungeonUiText.Static(combatRoomLabel);
        zoneBoss = DungeonUiText.Static(bossRoomLabel);
        if (zoneText != null) zoneText.richText = true;
        if (zoneStateText != null) zoneStateText.richText = true;
        if (clearPulse == null && map != null) clearPulse = CreatePulseLayer(map);
        if (map != null)
        {
            if (roomLayer == null) roomLayer = CreateDetailLayer(map, "RoomDetail", DungeonMinimapDetailGraphic.Part.Room, false);
            if (marksLayer == null) marksLayer = CreateDetailLayer(map, "RoomMarks", DungeonMinimapDetailGraphic.Part.Marks, false);
            if (roomPulseLayer == null) roomPulseLayer = CreateDetailLayer(map, "RoomPulse", DungeonMinimapDetailGraphic.Part.Pulse, false);
            if (playerLayer == null) playerLayer = CreateDetailLayer(map, "Player", DungeonMinimapDetailGraphic.Part.Player, true);
            if (guideLayer == null)
            {
                guideLayer = CreateDetailLayer(map, "ExitGuide", DungeonMinimapDetailGraphic.Part.Guide, false);
                // Player 점이 안내 위에 보이도록 Player 바로 앞에 둔다.
                if (playerLayer != null && playerLayer.transform.parent == guideLayer.transform.parent)
                    guideLayer.transform.SetSiblingIndex(playerLayer.transform.GetSiblingIndex());
            }
        }
        if (map != null) map.ClearMap();
        if (highlight != null) highlight.ClearMap();
        ResetPulseLayer();
        ClearDetailLayers();
        ClearSector();
    }

    // 5차 구역 띠를 비운다(글자도 지운다).
    private void ClearSector()
    {
        sectorShown = false;
        if (sectorMap != null) sectorMap.ClearMap();
        if (sectorHighlight != null) sectorHighlight.ClearMap();
        if (zoneText != null && !string.IsNullOrEmpty(zoneText.text)) zoneText.text = string.Empty;
        if (zoneStateText != null && !string.IsNullOrEmpty(zoneStateText.text)) zoneStateText.text = string.Empty;
    }

    // 1차 Prefab에는 펄스 층이 없다: MapArea와 같은 사각형의 마지막 자식으로 만든다(Highlight 위에 그린다).
    private static DungeonMinimapGraphic CreatePulseLayer(DungeonMinimapGraphic mapGraphic)
    {
        var go = new GameObject("ClearPulse", typeof(RectTransform), typeof(CanvasRenderer));
        go.layer = mapGraphic.gameObject.layer;
        go.transform.SetParent(mapGraphic.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(.5f, .5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling();
        var graphic = go.AddComponent<DungeonMinimapGraphic>();
        graphic.DrawPart = DungeonMinimapGraphic.Part.ClearPulse;
        graphic.raycastTarget = false;
        return graphic;
    }

    // 2차 Prefab에는 지금 방 자세히 층이 없다: MapArea 아래 마지막 자식으로 만든다(Player는 가운데 기준 16×16, 나머지는 MapArea에 펴짐).
    private static DungeonMinimapDetailGraphic CreateDetailLayer(DungeonMinimapGraphic mapGraphic, string name, DungeonMinimapDetailGraphic.Part part, bool marker)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.layer = mapGraphic.gameObject.layer;
        go.transform.SetParent(mapGraphic.transform, false);
        var rt = (RectTransform)go.transform;
        if (marker)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(16f, 16f);
        }
        else
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(.5f, .5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        rt.SetAsLastSibling();
        var graphic = go.AddComponent<DungeonMinimapDetailGraphic>();
        graphic.DrawPart = part;
        graphic.raycastTarget = false;
        return graphic;
    }

    private void ResetPulseLayer()
    {
        pulseRoom = -1;
        if (clearPulse == null) return;
        clearPulse.ClearMap();
        clearPulse.SetPulseRoom(-1);
        clearPulse.canvasRenderer.SetAlpha(0f);
    }

    private void ClearDetailLayers()
    {
        detailRoom = -1;
        detailRef = null;
        detailSilhouette = null;
        detailPulseRoom = -1;
        playerVisible = false;
        if (roomLayer != null) roomLayer.ClearRoom();
        if (marksLayer != null) marksLayer.ClearRoom();
        if (roomPulseLayer != null)
        {
            roomPulseLayer.ClearRoom();
            roomPulseLayer.canvasRenderer.SetAlpha(0f);
        }
        if (playerLayer != null) playerLayer.canvasRenderer.SetAlpha(0f);
        if (pingLayer != null) pingLayer.canvasRenderer.SetAlpha(0f);
        if (guideLayer != null)
        {
            guideLayer.ClearRoom();
            guideLayer.canvasRenderer.SetAlpha(0f);
        }
    }

    private void OnDestroy() => Unbind();

    // ===== IDungeonMapView =====

    public void Bind(IDungeonMapSource mapSource)
    {
        EnsureInit();
        if (ReferenceEquals(source, mapSource)) { Refresh(true); return; }
        Unbind();
        source = mapSource;
        if (source == null) return;
        source.MapChanged += OnMapChanged;
        source.RoomEntered += OnRoomEntered;
        source.RoomCleared += OnRoomCleared;
        source.FloorCleared += OnFloorCleared;
        floorKey = 0;
        floorCleared = false;
        // 원본이 방 상세·목표도 내주면(DungeonManager) 함께 연결한다 — DungeonManager가 IDungeonDetailView·IDungeonObjectiveView로 따로 Bind해도 같은 결과다.
        if (mapSource is IDungeonDetailSource details) BindDetail(details, false);
        if (mapSource is IDungeonObjectiveSource objectives) BindObjectives(objectives, false);
        if (mapSource is IDungeonExitSource exits) BindExit(exits, false);
        if (headingSource == null && mapSource is IDungeonPlayerHeadingSource heading) headingSource = heading;
        Refresh(true);
    }

    /// <summary>모든 원본(지도·상세·목표) 연결을 끊는다(IDungeonMapView·IDungeonDetailView·IDungeonObjectiveView 공통).</summary>
    public void Unbind()
    {
        if (source != null)
        {
            source.MapChanged -= OnMapChanged;
            source.RoomEntered -= OnRoomEntered;
            source.RoomCleared -= OnRoomCleared;
            source.FloorCleared -= OnFloorCleared;
        }
        UnbindDetail();
        UnbindObjectives();
        UnbindExit();
        headingSource = null;
        source = null;
        floorKey = 0;
        floorCleared = false;
        hasView = panning = false;
        shownRooms.Clear();
        shownCurrent = -1;
        silhouettes.Clear();
        detailMode = false;
        if (initialized)
        {
            if (map != null) map.ClearMap();
            if (highlight != null) highlight.ClearMap();
            ResetPulseLayer();
            ClearDetailLayers();
            ClearSector();
        }
    }

    // ===== IDungeonDetailView·IDungeonObjectiveView(명시 구현 — Bind(IDungeonMapSource) 호출이 모호해지지 않게) =====

    void IDungeonDetailView.Bind(IDungeonDetailSource details) => BindDetail(details, true);
    void IDungeonObjectiveView.Bind(IDungeonObjectiveSource objectives) => BindObjectives(objectives, true);

    /// <summary>방 상세 원본을 연결한다(같은 원본이면 그대로). refresh면 바로 다시 그린다.</summary>
    public void BindDetail(IDungeonDetailSource details, bool refresh = true)
    {
        EnsureInit();
        if (!ReferenceEquals(detailSource, details))
        {
            UnbindDetail();
            detailSource = details;
            if (detailSource != null) detailSource.DetailChanged += OnDetailChanged;
            // 5차: 상세 원본이 Player 방향도 내주면(DungeonManager) 함께 쓴다(선택 인터페이스).
            if (details is IDungeonPlayerHeadingSource heading) headingSource = heading;
        }
        if (refresh) Refresh(false);
    }

    /// <summary>목표 원본을 연결한다(미니맵은 목표 표식의 완료 여부만 읽는다).</summary>
    public void BindObjectives(IDungeonObjectiveSource objectives, bool refresh = true)
    {
        EnsureInit();
        if (!ReferenceEquals(objectiveSource, objectives))
        {
            UnbindObjectives();
            objectiveSource = objectives;
            if (objectiveSource != null) objectiveSource.ObjectiveChanged += OnObjectiveChanged;
        }
        if (refresh) RefreshMarks();
    }

    /// <summary>출구 원본을 연결한다(4차 — 출구 표식 열림·안내 층).</summary>
    public void BindExit(IDungeonExitSource exits, bool refresh = true)
    {
        EnsureInit();
        if (!ReferenceEquals(exitSource, exits))
        {
            UnbindExit();
            exitSource = exits;
            if (exitSource != null) exitSource.ExitChanged += OnExitChanged;
        }
        if (refresh) RefreshMarks();
    }

    private void UnbindExit()
    {
        if (exitSource != null) exitSource.ExitChanged -= OnExitChanged;
        exitSource = null;
    }

    private void UnbindDetail()
    {
        if (detailSource != null) detailSource.DetailChanged -= OnDetailChanged;
        detailSource = null;
    }

    private void UnbindObjectives()
    {
        if (objectiveSource != null) objectiveSource.ObjectiveChanged -= OnObjectiveChanged;
        objectiveSource = null;
    }

    private void OnMapChanged() => Refresh(false);
    private void OnRoomEntered(int id) => Refresh(false);
    private void OnDetailChanged() => Refresh(false);
    private void OnObjectiveChanged() => RefreshMarks();
    private void OnExitChanged() => RefreshMarks();
    private void OnRoomCleared(int id)
    {
        Refresh(false);
        if (detailMode && id == detailRoom) StartDetailPulse(id);
        else StartClearPulse(id);
    }

    /// <summary>그 방을 녹색으로 한 번 번쩍인다(방 클리어 순간). 이벤트가 부르며 검증도 직접 부른다.</summary>
    public void StartClearPulse(int roomId)
    {
        EnsureInit();
        if (roomId < 0 || clearPulse == null) return;
        pulseRoom = roomId;
        pulseStart = clock;
        clearPulse.SetPulseRoom(roomId);
        clearPulse.canvasRenderer.SetAlpha(0f);
    }

    /// <summary>지금 방 자세히: 그 방 실루엣을 녹색으로 한 번 번쩍인다(지금 방일 때만).</summary>
    public void StartDetailPulse(int roomId)
    {
        EnsureInit();
        if (!detailMode || roomId != detailRoom || roomPulseLayer == null) return;
        detailPulseRoom = roomId;
        detailPulseStart = clock;
        roomPulseLayer.canvasRenderer.SetAlpha(0f);
    }

    /// <summary>펄스 시작 뒤 t초의 알파: 기본 0.9초 시간표 = 0~0.45초 큰 번쩍임 + 0.4~0.9초 작은 번쩍임(0.55배). duration에 비례해 늘이고 줄인다.</summary>
    public static float ClearPulseAlpha(float t, float duration)
    {
        if (t < 0f || t >= duration) return 0f;
        float u = t * .9f / Mathf.Max(.1f, duration);
        return Mathf.Max(TrainingBoardMotion.Pulse(u, 0f, .45f), .55f * TrainingBoardMotion.Pulse(u, .4f, .9f));
    }
    private void OnFloorCleared()
    {
        floorCleared = true;
        UpdateFloorLabel();
    }

    // ===== 갱신 =====

    /// <summary>원본 상태를 다시 읽어 그림을 갱신한다. snap이면 화면 이동 없이 바로 맞춘다(새 던전이면 언제나 바로).</summary>
    public void Refresh(bool snap)
    {
        EnsureInit();
        if (!SourceAlive() || !source.HasFloor || source.Rooms == null || source.Rooms.Count == 0)
        {
            // 던전이 끝났다: 마지막 그림(사본)을 남긴 채 페이드아웃만 한다. 다음 던전은 화면 이동 없이 바로 맞춘다.
            floorKey = 0;
            panning = false;
            if (shownRooms.Count == 0)
            {
                if (map != null) map.ClearMap();
                if (highlight != null) highlight.ClearMap();
                ClearSector();
            }
            return;
        }

        int key = Mathf.Max(1, source.FloorNumber);
        if (key != floorKey)
        {
            floorKey = key;
            floorCleared = false;
            snap = true;
            // 새 던전: 방 상세가 새로 만들어지므로 실루엣 캐시를 비운다.
            silhouettes.Clear();
            ClearDetailLayers();
        }

        TakeSnapshot();
        if (RefreshDetail())
        {
            // 지금 방 자세히: 배치도 층은 비운다(화면 이동 없음).
            panning = false;
            hasView = false;
            PushMap();
            UpdateFloorLabel();
            UpdateTitle();
            RefreshSector();
            return;
        }
        var area = map != null ? map.rectTransform.rect : new Rect(0f, 0f, 272f, 152f);
        var target = DungeonMapMesh.Fit(shownRooms, shownCurrent, area, FitOptions);
        if (snap || !hasView)
        {
            shownView = targetView = target;
            hasView = true;
            panning = false;
        }
        else if (!target.Approximately(targetView))
        {
            fromView = shownView;
            targetView = target;
            panStart = clock;
            panning = true;
        }
        PushMap();
        UpdateFloorLabel();
        UpdateTitle();
        RefreshSector();
    }

    // 5차 구역 띠: 지금 방 자세히 모드면 던전 전체 단위 칸 배치도(지금 방 맥동)를 왼쪽에, 지금 방 이름·상태를 오른쪽에 둔다.
    // 배치도 모드(방 상세 없음)에서는 위 판이 이미 던전 전체를 그리므로 배치도는 비우고 글자만 둔다. Refresh 때만 불린다(매 프레임 아님).
    private void RefreshSector()
    {
        if (sectorMap != null)
        {
            if (detailMode && shownRooms.Count > 0)
            {
                var area = sectorMap.rectTransform.rect;
                var options = new DungeonMapMesh.FitOptions { maxCell = sectorMaxCell, minCell = sectorMinCell, gapRatio = gapRatio, padding = 3f };
                var sectorView = DungeonMapMesh.Fit(shownRooms, shownCurrent, area, options);
                sectorMap.SetMap(shownRooms, shownCurrent, sectorView);
                if (sectorHighlight != null) sectorHighlight.SetMap(shownRooms, shownCurrent, sectorView);
                sectorShown = true;
            }
            else
            {
                sectorShown = false;
                sectorMap.ClearMap();
                if (sectorHighlight != null) sectorHighlight.ClearMap();
            }
        }
        DungeonMapRoom current = null;
        for (int i = 0; i < shownRooms.Count; i++) if (shownRooms[i] != null && shownRooms[i].id == shownCurrent) { current = shownRooms[i]; break; }
        if (zoneText != null)
        {
            string name = current == null ? string.Empty : current.kind == DungeonRoomKind.Start ? zoneStart : current.kind == DungeonRoomKind.Boss ? zoneBoss : zoneCombat;
            if (zoneText.text != name) zoneText.text = name;
        }
        if (zoneStateText != null)
        {
            string state = current == null ? string.Empty : current.cleared ? clearStateLabel : hostileStateLabel;
            if (zoneStateText.text != state) zoneStateText.text = state;
            var color = current != null && current.cleared ? zoneClearColor : zoneHostileColor;
            if (zoneStateText.color != color) zoneStateText.color = color;
        }
    }

    // 지금 방 상세가 있으면 그 방을 그리고 true. 방이 바뀌었을 때만 실루엣·Room·Pulse 층을 다시 만들고, 표식 층은 늘 다시 만든다.
    private bool RefreshDetail()
    {
        int current = shownCurrent;
        var detail = DetailAlive() && current >= 0 ? detailSource.RoomDetail(current) : null;
        bool want = detail != null && roomLayer != null;
        if (want != detailMode)
        {
            detailMode = want;
            if (!want) ClearDetailLayers();
        }
        if (!detailMode) return false;
        if (current != detailRoom || !ReferenceEquals(detail, detailRef))
        {
            detailRoom = current;
            detailRef = detail;
            detailSilhouette = SilhouetteFor(detail);
            var area = roomLayer.rectTransform.rect;
            detailView = DungeonMapMesh.FitRoom(detailSilhouette, area, detailPadding, detailMaxCell);
            roomLayer.SetRoom(detailSilhouette, detail, detailView, null, null);
            if (roomPulseLayer != null)
            {
                roomPulseLayer.SetRoom(detailSilhouette, detail, detailView, null, null);
                roomPulseLayer.canvasRenderer.SetAlpha(0f);
            }
            detailPulseRoom = -1;
        }
        RefreshMarks();
        return true;
    }

    // 표식 층만 다시 만든다(출입구 상태·목표 완료·출구 해금이 바뀌었을 때). 출구 안내 층도 함께 맞춘다.
    private void RefreshMarks()
    {
        if (!detailMode || marksLayer == null || detailRef == null) return;
        marksLayer.SetRooms(shownRooms);
        marksLayer.SetRoom(detailSilhouette, detailRef, detailView, DetailAlive() ? detailSource : null, ObjectiveAlive() ? objectiveSource.ObjectiveMarkers : null);
        RefreshGuide();
    }

    // 출구 안내: 해금 뒤(들어가기 전)에만 — 지금 방이 출구 방이면 출구 표식, 아니면 원본이 준 출구 쪽 출입구.
    private void RefreshGuide()
    {
        if (guideLayer == null) return;
        int socket = -1;
        bool here = false;
        if (detailMode && detailRef != null && ExitAlive() && exitSource.HasExit && exitSource.ExitUnlocked && !exitSource.ExitEntered)
        {
            if (exitSource.ExitRoom == detailRoom) here = true;
            else if (exitSource.TryGetExitGuide(out int s)) socket = s;
        }
        if (socket < 0 && !here) guideLayer.ClearRoom();
        else guideLayer.SetGuide(detailRef, detailView, socket, here);
    }

    private DungeonMapMesh.RoomSilhouette SilhouetteFor(DungeonRoomDetail detail)
    {
        if (silhouettes.TryGetValue(detail.id, out var cached) && ReferenceEquals(cached.source, detail)) return cached;
        var built = DungeonMapMesh.BuildSilhouette(detail);
        silhouettes[detail.id] = built;
        return built;
    }

    private void PushMap()
    {
        if (detailMode)
        {
            if (map != null) map.ClearMap();
            if (highlight != null) highlight.ClearMap();
            if (clearPulse != null && pulseRoom < 0) clearPulse.ClearMap();
            return;
        }
        if (map != null) map.SetMap(shownRooms, shownCurrent, shownView);
        if (highlight != null) highlight.SetMap(shownRooms, shownCurrent, shownView);
        if (clearPulse != null) clearPulse.SetMap(shownRooms, shownCurrent, shownView);
    }

    // 원본 방 목록을 사본으로 옮긴다. 원본(DungeonManager)은 던전이 끝나면 목록을 비우므로, 사본이 있어야 페이드아웃 동안 마지막 그림을 유지한다.
    // 단위 칸·연결 배열은 원본이 던전 시작 때 한 번 만들고 고치지 않으므로 참조만 옮긴다.
    private void TakeSnapshot()
    {
        var rooms = source.Rooms;
        while (shownRooms.Count < rooms.Count) shownRooms.Add(new DungeonMapRoom());
        if (shownRooms.Count > rooms.Count) shownRooms.RemoveRange(rooms.Count, shownRooms.Count - rooms.Count);
        for (int i = 0; i < rooms.Count; i++)
        {
            var from = rooms[i];
            var to = shownRooms[i];
            if (from == null) { to.id = -1; to.units = Array.Empty<Vector2Int>(); to.links = Array.Empty<(Vector2Int, DungeonDoorSide, int)>(); to.known = to.visited = false; to.teleportTo = -1; to.rest = false; continue; }
            to.id = from.id;
            to.kind = from.kind;
            to.units = from.units ?? Array.Empty<Vector2Int>();
            to.links = from.links ?? Array.Empty<(Vector2Int, DungeonDoorSide, int)>();
            to.known = from.known;
            to.visited = from.visited;
            to.cleared = from.cleared;
            to.sealedRoom = from.sealedRoom;
            to.sizeClass = from.sizeClass;
            to.teleportTo = from.teleportTo;
            to.rest = from.rest;
        }
        shownCurrent = source.CurrentRoom;
    }

    private void UpdateFloorLabel()
    {
        if (floorText == null) return;
        if (!SourceAlive() || !source.HasFloor) return;   // 던전이 끝나면 페이드아웃 동안 마지막 표시를 유지한다
        string label = string.Format(floorFormat, Mathf.Max(1, source.FloorNumber), Mathf.Max(1, source.FloorCount));
        if (floorCleared) label += clearedSuffix;
        // esamanru 정적 글꼴에는 공백 글자가 없다 → TMP 여백 태그(방 상태 띠와 같은 방식).
        string shown = DungeonUiText.Static(label);
        if (floorText.text != shown) floorText.text = shown;
    }

    private void UpdateTitle()
    {
        if (titleText == null) return;
        string shown = DungeonUiText.Static(detailMode ? detailTitle : title);
        if (titleText.text != shown) titleText.text = shown;
    }

    private bool SourceAlive()
    {
        if (source == null) return false;
        // 원본이 파괴된 Unity 객체(DungeonManager)면 끊는다.
        if (source is UnityEngine.Object unityObject && unityObject == null)
        {
            source = null;
            return false;
        }
        return true;
    }

    private bool DetailAlive()
    {
        if (detailSource == null) return false;
        if (detailSource is UnityEngine.Object unityObject && unityObject == null) { detailSource = null; return false; }
        return true;
    }

    private bool ObjectiveAlive()
    {
        if (objectiveSource == null) return false;
        if (objectiveSource is UnityEngine.Object unityObject && unityObject == null) { objectiveSource = null; return false; }
        return true;
    }

    private bool ExitAlive()
    {
        if (exitSource == null) return false;
        if (exitSource is UnityEngine.Object unityObject && unityObject == null) { exitSource = null; return false; }
        return true;
    }

    private void Update() => Advance(Time.unscaledDeltaTime);

    /// <summary>실제 시간 dt만큼 페이드·화면 이동·맥동·Player 점을 진행한다(Update가 부른다. 검증은 직접 부른다). 할당하지 않는다.</summary>
    public void Advance(float deltaTime)
    {
        EnsureInit();
        deltaTime = Mathf.Max(0f, deltaTime);
        clock += deltaTime;

        // 보임
        bool show = ShouldShow();
        float targetAlpha = show ? 1f : 0f;
        float rate = deltaTime / (show ? fadeInTime : fadeOutTime);
        alpha = Mathf.MoveTowards(alpha, targetAlpha, rate);
        if (group != null && !Mathf.Approximately(group.alpha, alpha)) group.alpha = alpha;

        // 화면 이동
        if (panning)
        {
            float t = Mathf.Clamp01((clock - panStart) / panTime);
            float e = t * t * (3f - 2f * t);
            shownView = DungeonMapMesh.View.Lerp(fromView, targetView, e);
            if (t >= 1f)
            {
                shownView = targetView;
                panning = false;
            }
            PushMap();
        }

        // 현재 방 맥동(Mesh는 그대로, CanvasRenderer 알파만) — 5차 구역 띠도 같은 박자
        {
            float wave = .5f - .5f * Mathf.Cos(clock * Mathf.PI * 2f / pulsePeriod);
            float pulse = Mathf.Lerp(pulseMin, pulseMax, wave);
            if (highlight != null) highlight.canvasRenderer.SetAlpha(pulse);
            if (sectorHighlight != null) sectorHighlight.canvasRenderer.SetAlpha(sectorShown ? pulse : 0f);
        }

        // 방 클리어 녹색 펄스(Mesh는 그대로, CanvasRenderer 알파만). 끝나면 층을 비운다.
        if (clearPulse != null && pulseRoom >= 0)
        {
            float t = clock - pulseStart;
            clearPulse.canvasRenderer.SetAlpha(ClearPulseAlpha(t, clearPulseTime));
            if (t >= clearPulseTime)
            {
                pulseRoom = -1;
                clearPulse.SetPulseRoom(-1);
            }
        }

        // 지금 방 자세히: 클리어 펄스·Player 점(anchoredPosition만 — Mesh 재생성 없음)
        if (roomPulseLayer != null && detailPulseRoom >= 0)
        {
            float t = clock - detailPulseStart;
            roomPulseLayer.canvasRenderer.SetAlpha(ClearPulseAlpha(t, clearPulseTime));
            if (t >= clearPulseTime) detailPulseRoom = -1;
        }
        // 출구 안내 맥동(Mesh는 그대로, 알파만)
        if (guideLayer != null)
        {
            float guide = 0f;
            if (detailMode && guideLayer.HasGuide)
            {
                float wave = .5f - .5f * Mathf.Cos(clock * Mathf.PI * 2f / exitPulsePeriod);
                guide = Mathf.Lerp(exitPulseMin, 1f, wave);
            }
            guideLayer.canvasRenderer.SetAlpha(guide);
        }
        UpdatePlayerMarker();
    }

    private void UpdatePlayerMarker()
    {
        if (playerLayer == null) return;
        bool visible = false;
        if (detailMode && detailRef != null && SourceAlive() && source.HasFloor && source.CurrentRoom == detailRoom && DetailAlive()
            && detailSource.TryGetPlayerLocal(out var local))
        {
            var area = roomLayer.rectTransform.rect;
            var p = detailView.ToScreen(local);
            p.x = Mathf.Clamp(p.x, area.xMin + 2f, area.xMax - 2f);
            p.y = Mathf.Clamp(p.y, area.yMin + 2f, area.yMax - 2f);
            var anchored = p - area.center;
            var rt = playerLayer.rectTransform;
            if ((rt.anchoredPosition - anchored).sqrMagnitude > 1e-4f) rt.anchoredPosition = anchored;
            if (pingLayer != null)
            {
                var pingRt = pingLayer.rectTransform;
                if ((pingRt.anchoredPosition - anchored).sqrMagnitude > 1e-4f) pingRt.anchoredPosition = anchored;
            }
            visible = true;
        }
        playerVisible = visible;
        float blink = visible ? Mathf.Lerp(.6f, 1f, .5f + .5f * Mathf.Cos(clock * Mathf.PI * 2f / playerBlinkPeriod)) : 0f;
        playerLayer.canvasRenderer.SetAlpha(blink);

        // 5차: 바라보는 쪽(바뀔 때만 localScale.x — Mesh 재생성 없음)
        if (visible && HeadingAlive() && headingSource.TryGetPlayerFacing(out int f))
        {
            int want = f < 0 ? -1 : 1;
            if (want != facing)
            {
                facing = want;
                playerLayer.rectTransform.localScale = new Vector3(facing, 1f, 1f);
            }
        }
        // 5차: 핑 고리 — 주기마다 0.6배 → 1.8배로 퍼지며 사라진다(localScale·알파만)
        if (pingLayer != null)
        {
            float k = Mathf.Repeat(clock, pingPeriod) / pingPeriod;
            pingScale = Mathf.Lerp(.6f, 1.8f, TrainingBoardMotion.OutCubic(k));
            var pingRt = pingLayer.rectTransform;
            var scale = new Vector3(pingScale, pingScale, 1f);
            if (pingRt.localScale != scale) pingRt.localScale = scale;
            pingLayer.canvasRenderer.SetAlpha(visible ? pingAlpha * (1f - k) : 0f);
        }
    }

    private bool HeadingAlive()
    {
        if (headingSource == null) return false;
        if (headingSource is UnityEngine.Object unityObject && unityObject == null) { headingSource = null; return false; }
        return true;
    }

    /// <summary>지금 보여야 하는가(던전 진행 중 && HUD 보임 && 패배 화면·결과 보드 아님).</summary>
    public bool ShouldShow()
    {
        if (!SourceAlive() || !source.HasFloor || source.Rooms == null || source.Rooms.Count == 0) return false;
        return DungeonHudGate.ShouldShowDungeonUi(HudGroupOverride);
    }
}
