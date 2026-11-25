using System;
using UnityEngine;

/// <summary>
/// 부서지는 던전 Prop. DungeonManager(방 세우기)가 구독해 부서진 Prop을 기억하고, 그 방에 다시 들어와도 세우지 않는다.
/// </summary>
public interface IDungeonBreakable
{
    bool IsBroken { get; }
    event Action<IDungeonBreakable> Broken;
}

/// <summary>
/// 무기·드론이 '적'으로 보면 안 되는 표적(파괴형 Prop의 체력). 파괴형 Prop은 기존 무기 판정을 쓰려고 EnemyHealth를 상속하므로,
/// 자동 조준·별창 유도·드론 표적·스킬 게이지 적립에서 이 표식으로 뺀다(사용자 승인 2026-11-23, 무기 파일 4곳 한 줄 가드).
/// </summary>
public interface IDungeonPropTarget { }

public static class DungeonPropTargeting
{
    /// <summary>target(체력 컴포넌트 등)이 파괴형 Prop이면 true. null이면 false.</summary>
    public static bool IsProp(object target) => target is IDungeonPropTarget;
}

/// <summary>미니맵처럼 던전 상태를 그리는 화면 요소. DungeonManager가 생성한 뒤 Bind로 연결한다.</summary>
public interface IDungeonMapView
{
    void Bind(IDungeonMapSource source);
    void Unbind();
}

/// <summary>
/// 던전 UI Prefab을 MainScene을 고치지 않고 화면 UI Canvas(Screen Space-Camera 'UI') 아래에 실행 중 생성한다.
/// TrainingRoomLoadingPanel.Show와 같은 방식(ScreenEffect.OverlayRoot의 부모 = UI Canvas). TitleScene처럼 ScreenEffect가 없으면 null.
/// </summary>
public static class DungeonUiPlacement
{
    public enum Slot
    {
        /// <summary>훈련 상황판(TrainingScoreboard) 바로 뒤 — 결과 보드·암전·일시정지가 위를 덮는다(미니맵).</summary>
        AfterScoreboard,
        /// <summary>암전(FadePanel) 바로 앞 — HUD·미니맵을 덮고 암전·일시정지에는 덮인다(관 통과 오버레이).</summary>
        BeforeFade,
    }

    public static GameObject Instantiate(GameObject prefab, Slot slot)
    {
        if (prefab == null) return null;
        var effect = UnityEngine.Object.FindFirstObjectByType<ScreenEffect>(UnityEngine.FindObjectsInactive.Include);
        if (effect == null || effect.OverlayRoot == null || effect.OverlayRoot.parent == null) return null;
        var canvas = effect.OverlayRoot.parent;
        var instance = UnityEngine.Object.Instantiate(prefab, canvas, false);
        int index;
        if (slot == Slot.AfterScoreboard)
        {
            var board = canvas.Find("TrainingScoreboard");
            index = board != null ? board.GetSiblingIndex() + 1 : 0;
        }
        else
        {
            var fade = canvas.Find("FadePanel");
            index = fade != null ? fade.GetSiblingIndex() : effect.OverlayRoot.GetSiblingIndex();
        }
        instance.transform.SetSiblingIndex(Mathf.Clamp(index, 0, canvas.childCount - 1));
        return instance;
    }
}

// ───────────── 던전 2차 계약(2026-11-24) ─────────────

/// <summary>방 전투 단계(UI 상태 띠용).</summary>
public enum DungeonRoomPhase
{
    /// <summary>던전 밖이거나 적이 없는 방.</summary>
    None = 0,
    /// <summary>잠기고 적이 남아 있음(붉은 'HOSTILES n').</summary>
    Hostile = 1,
    /// <summary>방금 클리어(녹색 'ROOM CLEAR').</summary>
    Cleared = 2,
    /// <summary>
    /// 클리어한 방에 다시 들어옴(4차 R18): 순찰 적이 있으나 출입구를 잠그지 않는다(통과 가능). 클리어 수·RoomCleared·시설 켜기·ROOM CLEAR에 세지 않는다.
    /// 상태 띠는 어두운 앰버 'HOSTILES n · BYPASS'를 잠깐 보이고 접으며, 적 위치 마커는 끈다.
    /// </summary>
    Patrol = 3,
}

/// <summary>
/// 방 전투 상태를 UI(상태 띠·적 위치 마커)에 내주는 읽기 전용 접점. DungeonManager가 구현한다(미니맵용 IDungeonMapSource와 분리 — 미니맵 회귀의 가짜 구현 보호).
/// </summary>
public interface IDungeonCombatSource
{
    DungeonRoomPhase Phase { get; }
    /// <summary>남은 필수 적(아직 나오지 않은 것 포함).</summary>
    int HostilesRemaining { get; }
    int HostilesTotal { get; }
    /// <summary>적 구성의 생성이 모두 끝났는지.</summary>
    bool HostilesAllSpawned { get; }
    /// <summary>살아 있는 필수 적(마커 대상).</summary>
    System.Collections.Generic.IReadOnlyList<EnemyController> HostileEnemies { get; }
    bool IsBossRoom { get; }
    /// <summary>Wave 진행이 멈췄거나(패배) 던전이 끝나 표시를 거둘 때.</summary>
    bool IsHalted { get; }
    event Action RoomHostile;
    event Action<int, int> HostilesChanged;
    event Action<int> RoomCleared;
    event Action RoomUnlocked;
    event Action FloorCleared;
    event Action FloorStarted;
    event Action FloorEnded;
}

/// <summary>
/// 방 클리어에 반응하는 시설·Prop(텔레포트·크레인 훅·화물 승강기·셔터·수리대 등). DungeonRoomStage가 방을 세울 때 그 방의 클리어 여부로
/// 즉시 적용(instant = true)하고, 방을 클리어한 순간에는 Player에 가까운 것부터 차례로 켠다(instant = false). 던전 밖(기본 진행 Wave)에서는 늘 켜짐으로 본다.
/// </summary>
public interface IDungeonRoomPowered
{
    void SetRoomPowered(bool powered, bool instant);
}

/// <summary>방 이동의 출발·도착 지점(출입구 관 입구, 텔레포트 시설). 연출은 이 점들로 Player를 옮긴다.</summary>
public interface IDungeonPassagePoint
{
    /// <summary>들어간 Player 몸통 중심(월드).</summary>
    Vector2 RecessPoint { get; }
    /// <summary>나와서 선 Player 몸통 중심(월드).</summary>
    Vector2 StandPoint { get; }
    /// <summary>나가는 방향(출입구: 방 밖, 텔레포트: 위).</summary>
    Vector2 Outward { get; }
    void PlayIntake();
    void PlayEject();
}

/// <summary>방 배경 교체 요청(정적 벽면 방).</summary>
public struct DungeonRoomBackdropRequest
{
    public DungeonBackdropMode mode;
    public DungeonWallKit kit;
    /// <summary>키트 Prefab(DungeonRulesSO.WallKitPrefab). null이면 시차 배경을 유지한다.</summary>
    public GameObject kitPrefab;
    /// <summary>방 연결 공간(방 로컬). 벽면을 이 크기에 맞춘다.</summary>
    public Rect bounds;
}

/// <summary>
/// 방 배경을 바꾸는 쪽(WaveRoomBackground가 구현). DungeonRoomStage가 셀을 바꾼 직후(화면이 덮인 동안) ApplyRoom을 부르고,
/// 다음 Wave 준비·초기화(ApplyWave)는 덮어쓰기를 스스로 푼다. 시차 배경 방이면 ClearRoom.
/// </summary>
public interface IDungeonRoomBackdrop
{
    void ApplyRoom(DungeonRoomBackdropRequest request);
    void ClearRoom();
}

/// <summary>적 생성 연출 앵커(제작 부스·수리 거치대·중계 비컨). 적 구성 항목의 생성 위치가 WorldArea 안이면 그 적이 나올 때 PlaySpawn을 부른다.</summary>
public interface IDungeonSpawnAnchor
{
    Rect WorldArea { get; }
    void PlaySpawn(EnemyController enemy);
}

/// <summary>(선택) 이미 적을 내보낸 앵커를 클리어한 방에 다시 세울 때 '내보낸 뒤' 모습(빈 거치대·열린 집게)으로 두는 앵커.</summary>
public interface IDungeonSpentAnchor
{
    void MarkSpent();
}

/// <summary>
/// 시설이 던전 진행을 묻고 요청하는 접점(DungeonManager가 구현, BeginFloor부터 EndFloor까지 Current에 있다). 던전 밖이면 Current는 null.
/// </summary>
public interface IDungeonFacilityHost
{
    /// <summary>시설을 쓸 수 있는지(지금 방 클리어·관 통과 아님·던전 진행 중·패배 아님).</summary>
    bool CanUseFacilities { get; }
    /// <summary>던전(층)당 한 번만 쓰는 것(수리대 등)을 쓴다. 이미 썼으면 false.</summary>
    bool TryUseFloorToken(string key);
    /// <summary>텔레포트 시설에서 짝 방으로 이동을 시작한다(그 방 클리어 뒤에만).</summary>
    bool TryBeginTeleport(DungeonTeleporter from);
    /// <summary>시설이 Player를 붙잡고 있는 동안 true로 알린다(던전 클리어 뒤 다음 Wave 준비가 끝날 때까지 기다리게).</summary>
    void SetFacilityBusy(object owner, bool busy);
}

/// <summary>
/// 던전 훈련이 진행 중인지(입장부터 퇴장까지 — Wave 사이·재도전 중에도 true). 층 사이에 깜박이면 안 되는 표시(코너판 간략판 등)가 읽는다.
/// DungeonManager가 켜질 때 등록하고 꺼질 때 푼다(읽기 전용 구독 — UI는 이 값만 본다).
/// </summary>
public static class DungeonRunState
{
    private static UnityEngine.Object owner;
    public static bool Active => owner != null;
    public static void Enter(UnityEngine.Object who) => owner = who;
    public static void Exit(UnityEngine.Object who) { if (ReferenceEquals(owner, who)) owner = null; }
}

public static class DungeonFacilityHost
{
    /// <summary>지금 진행 중인 던전의 시설 접점(없으면 null).</summary>
    public static IDungeonFacilityHost Current { get; set; }
}

/// <summary>
/// 방 상태 띠·적 위치 마커처럼 방 전투 상태를 그리는 화면 요소(DungeonRulesSO.roomStatusPrefab의 컴포넌트가 구현). DungeonManager가 생성한 뒤
/// GetComponentInChildren으로 찾아 Bind한다(리플렉션 없음). 미니맵의 IDungeonMapView와 같은 방식이다.
/// </summary>
public interface IDungeonCombatView
{
    void Bind(IDungeonCombatSource source);
    void Unbind();
}

// ───────────── 던전 3차 계약(2026-11-24, DESIGN_V3 §3) ─────────────

/// <summary>
/// 던전 목표 실물(레버 등). DungeonRoomStage가 목표 SO의 Prefab(PrefabFor(index))을 세울 때 RoomId·Slot·Index를 채우고,
/// 그 방의 completedObjectives로 '완료' 모습을 즉시 적용한다(instant = true).
/// </summary>
public interface IDungeonObjectivePoint
{
    /// <summary>이 실물이 선 방 id.</summary>
    int RoomId { get; set; }
    /// <summary>템플릿 맵 층의 목표 슬롯 순번(DungeonRoomState.completedObjectives의 키).</summary>
    int Slot { get; set; }
    /// <summary>목표 진행 번호(0부터, DungeonObjectiveAssignment.index).</summary>
    int Index { get; set; }
    bool IsCompleted { get; }
    /// <summary>완료 모습을 적용한다. instant면 연출 없이 바로(다시 세울 때), 아니면 당기는 연출과 함께.</summary>
    void SetCompleted(bool done, bool instant);
}

/// <summary>
/// 목표 실물이 '완료'를 요청하는 접점(DungeonManager가 구현). 실물은 DungeonFacilityHost.Current as IDungeonObjectiveHost로 찾는다.
/// </summary>
public interface IDungeonObjectiveHost
{
    /// <summary>시설 사용 조건·지금 방·소유 확인 뒤 목표를 완료한다. 받아들이면 true(그때 실물이 SetCompleted(true, false)를 적용).</summary>
    bool TryCompleteObjective(IDungeonObjectivePoint point);
}

/// <summary>지도·미니맵의 목표 표식 하나(방·진행 번호·완료 여부·그 방을 알게 됐는지).</summary>
public readonly struct DungeonObjectiveMarker
{
    public readonly int room, index;
    public readonly bool done, known;

    public DungeonObjectiveMarker(int room, int index, bool done, bool known)
    {
        this.room = room;
        this.index = index;
        this.done = done;
        this.known = known;
    }
}

/// <summary>
/// 던전 목표 진행을 UI(목표 패널·현장 단말기·미니맵)에 내주는 읽기 전용 접점. DungeonManager가 구현한다(진행은 DungeonObjectiveTracker에 위임).
/// </summary>
public interface IDungeonObjectiveSource
{
    /// <summary>지금 던전에 목표가 있는지(클리어 조건이 Objective이고 배치됨).</summary>
    bool HasObjective { get; }
    /// <summary>목표 제목(예: "제어 레버 작동").</summary>
    string ObjectiveTitle { get; }
    /// <summary>짧은 표기(예: "LEVERS").</summary>
    string ObjectiveShortLabel { get; }
    int ObjectiveDone { get; }
    int ObjectiveTotal { get; }
    /// <summary>목표 색(앰버 #FFB020 계열).</summary>
    Color ObjectiveColor { get; }
    System.Collections.Generic.IReadOnlyList<DungeonObjectiveMarker> ObjectiveMarkers { get; }
    /// <summary>진행·표식·목표 유무가 바뀌었을 때(던전 시작·끝 포함).</summary>
    event Action ObjectiveChanged;
    /// <summary>목표 하나를 완료한 순간(인자 = 진행 번호 index).</summary>
    event Action<int> ObjectiveCompleted;
}

/// <summary>목표 패널처럼 목표 진행을 그리는 화면 요소(DungeonRulesSO.objectivePanelPrefab의 컴포넌트가 구현). DungeonManager가 생성한 뒤 Bind한다.</summary>
public interface IDungeonObjectiveView
{
    void Bind(IDungeonObjectiveSource source);
    void Unbind();
}

/// <summary>방 상세 지도의 표식 종류.</summary>
public enum DungeonDetailMarkerKind
{
    Teleport = 0,
    Repair = 1,
    Objective = 2,
    Facility = 3,
    /// <summary>시작 방 출구 문(4차 R11). index는 0, toRoom은 -1.</summary>
    Exit = 4,
    /// <summary>
    /// 5차(S9): 경로 크레인(PropCraneHook). local = 집(점 0)의 매달린 발(방 로컬), index = <see cref="DungeonRoomDetail.cranePaths"/>의 경로 번호(없으면 -1), toRoom은 -1.
    /// </summary>
    CranePath = 5,
    /// <summary>5차(S9): 고정 크레인(PropCraneAnchor). local = 매달린 발(방 로컬), index는 Prop 번호, toRoom은 -1.</summary>
    CraneAnchor = 6,
    /// <summary>5차(S9): 점프대(WaveJumpPad). local = 배치 자리(방 로컬), index는 Prop 번호, toRoom은 -1.</summary>
    JumpPad = 7,
}

/// <summary>방 상세 지도의 출입구 하나(방 로컬 좌표).</summary>
public struct DungeonRoomDetailDoor
{
    public Vector2 local;
    public DungeonDoorSide side;
    /// <summary>템플릿 출입구 번호(DoorState 조회 키).</summary>
    public int socket;
    /// <summary>건너편 방 id(-1이면 연결 안 됨).</summary>
    public int toRoom;
}

/// <summary>방 상세 지도의 표식 하나(방 로컬 좌표).</summary>
public struct DungeonRoomDetailPoint
{
    public Vector2 local;
    public DungeonDetailMarkerKind kind;
    /// <summary>종류별 번호(텔레포트 순번·목표 진행 번호 등).</summary>
    public int index;
    /// <summary>텔레포트면 짝 방 id(없으면 -1).</summary>
    public int toRoom;
}

/// <summary>
/// 방 하나의 상세 지도 데이터(방 로컬 좌표). 던전 시작 때 한 번 만들고 고치지 않는다 — 바뀌는 상태(출입구 상태·Player 위치·목표 완료)는
/// IDungeonDetailSource·IDungeonObjectiveSource로 따로 묻는다.
/// </summary>
public sealed class DungeonRoomDetail
{
    public int id;
    /// <summary>이 방에서 켜는 지형 칸(DungeonRoomData.cells와 같음).</summary>
    public WaveMapCell[] cells;
    /// <summary>셀 좌표 외접 범위.</summary>
    public RectInt cellBounds;
    /// <summary>실내 외접 범위(방 로컬).</summary>
    public Rect interior;
    public DungeonRoomDetailDoor[] doors;
    public DungeonRoomDetailPoint[] points;
    /// <summary>
    /// 5차(S9, 선택): 경로 크레인 경로(방 로컬 매달린 발 목록 — 점 0 = 집, 마지막 = 끝 도크). <see cref="DungeonDetailMarkerKind.CranePath"/> 표식의 index가 이 배열의 번호다.
    /// 없으면 null(옛 원본·회귀의 가짜 상세) — UI는 경로 없이 훅 표식만 그린다.
    /// </summary>
    public Vector2[][] cranePaths;
}

/// <summary>
/// 5차(S9, 선택 인터페이스): Player가 바라보는 쪽(미니맵·큰 지도 Player 표식의 꼬리). DungeonManager가 구현하고 UI는 'source is IDungeonPlayerHeadingSource'로 확인한다
/// (기존 IDungeonDetailSource에 멤버를 더하지 않는다 — 회귀의 가짜 구현 보호).
/// </summary>
public interface IDungeonPlayerHeadingSource
{
    /// <summary>바라보는 쪽(+1 = 오른쪽, -1 = 왼쪽). 던전 밖이거나 Player가 없으면 false.</summary>
    bool TryGetPlayerFacing(out int facing);
}

/// <summary>지금 방 자세히(미니맵)·큰 지도(현장 단말기)에 방 상세를 내주는 읽기 전용 접점. DungeonManager가 구현한다.</summary>
public interface IDungeonDetailSource
{
    /// <summary>방 상세(없으면 null).</summary>
    DungeonRoomDetail RoomDetail(int roomId);
    DungeonDoor.DoorState DoorState(int roomId, int socket);
    /// <summary>Player 위치(지금 방 로컬). 던전 밖이거나 Player가 없으면 false.</summary>
    bool TryGetPlayerLocal(out Vector2 local);
    /// <summary>출입구 상태 등 상세 표시에 영향을 주는 값이 바뀌었을 때.</summary>
    event Action DetailChanged;
}

public interface IDungeonDetailView
{
    void Bind(IDungeonDetailSource source);
    void Unbind();
}

/// <summary>
/// (선택) 시설이 목적지·전투 중 사용 조건을 묻는 접점. DungeonManager가 IDungeonFacilityHost와 함께 구현하고,
/// 시설은 DungeonFacilityHost.Current as IDungeonFacilityRoute로 찾는다(구현이 없으면 기존 규칙).
/// </summary>
public interface IDungeonFacilityRoute
{
    /// <summary>지금 방에 선 연결 출입구(DungeonDoor). 시설은 DungeonDoor로 캐스트해 FloorY를 읽어도 된다.</summary>
    System.Collections.Generic.IReadOnlyList<IDungeonPassagePoint> RoomPassages { get; }
    /// <summary>
    /// 전투 중 시설(FacilityUse.Anytime)을 쓸 수 있는지: 던전 진행·Wave 진행(준비 아님)·관 통과 아님·패배 아님·층 조건 미충족(방 클리어 불문).
    /// </summary>
    bool CanUseFacilitiesInCombat { get; }
}

// ───────────── 던전 4차 계약(2026-11-25, DESIGN_V4 §4) ─────────────
// 기존 인터페이스(IDungeonObjectiveHost·IDungeonCombatSource·IDungeonObjectiveSource 등)에는 멤버를 더하지 않는다(회귀의 가짜 구현 보호).
// 아래는 모두 선택 인터페이스다 — 쓰는 쪽은 'source is IDungeonExitSource' 처럼 확인한다.

/// <summary>
/// 시작 방 출구 문 실물(PropExitGate). DungeonRoomStage가 목표 SO의 ExitPrefab을 출구 자리(DungeonExitSlot)에 세울 때 RoomId를 채우고,
/// 그 순간의 목표 진행을 SetProgress(…, instant: true)로 밀어 넣는다(push). 이후 진행이 바뀌면 DungeonManager가 다시 밀어 넣는다.
/// </summary>
public interface IDungeonExitPoint
{
    /// <summary>이 문이 선 방 id(시작 방).</summary>
    int RoomId { get; set; }
    /// <summary>문이 열려(볼트가 모두 빠지고 문짝이 열림) 들어갈 수 있는지.</summary>
    bool IsOpen { get; }
    /// <summary>
    /// 목표 진행을 적용한다. done/total = 완료한 레버 수/필요 수(볼트·램프 표시), unlocked = 출구 해금 여부.
    /// instant면 연출 없이 바로(방을 세울 때), 아니면 아직 보이지 않은 풀림만 차례로 재생한다(시작 방에 돌아왔을 때의 지연 재생).
    /// </summary>
    void SetProgress(int done, int total, bool unlocked, bool instant);
}

/// <summary>출구 문이 '입장'을 요청하는 접점(DungeonManager 구현). 실물은 DungeonFacilityHost.Current as IDungeonExitHost로 찾는다.</summary>
public interface IDungeonExitHost
{
    /// <summary>해금·시설 사용 조건·지금 방 확인 뒤 출구 입장(흡입 → SECTOR CLEAR → Dungeon.Clear)을 시작한다. 받아들이면 true.</summary>
    bool TryEnterExit(IDungeonExitPoint exit);
}

/// <summary>
/// 출구 상태를 UI(상태 띠·목표 패널·현장 단말기·미니맵)에 내주는 읽기 전용 접점. DungeonManager가 구현한다(선택 인터페이스).
/// 출구가 없는 던전(목표 finish = Immediate 또는 목표 없음)이면 HasExit = false.
/// </summary>
public interface IDungeonExitSource
{
    /// <summary>이 던전에 시작 방 출구가 있는지.</summary>
    bool HasExit { get; }
    /// <summary>목표를 모두 채워 출구가 해금됐는지.</summary>
    bool ExitUnlocked { get; }
    /// <summary>Player가 출구에 들어갔는지(던전 클리어 진행 중).</summary>
    bool ExitEntered { get; }
    /// <summary>출구가 선 방 id(없으면 -1).</summary>
    int ExitRoom { get; }
    /// <summary>짧은 표기(목표 SO의 exitShortLabel, 예: "EXIT").</summary>
    string ExitShortLabel { get; }
    /// <summary>지금 방에서 출구 방 쪽으로 가는 연결 출입구의 socket(템플릿 출입구 번호). 지금 방이 출구 방이거나 해금 전이면 false.</summary>
    bool TryGetExitGuide(out int socket);
    /// <summary>해금·입장·출구 유무가 바뀌었을 때(던전 시작·끝 포함).</summary>
    event Action ExitChanged;
}

/// <summary>목표 대상 실물의 종류(UI 마커).</summary>
public enum DungeonObjectiveTargetKind
{
    /// <summary>목표 레버.</summary>
    Lever = 0,
    /// <summary>시작 방 출구 문.</summary>
    Exit = 1,
}

/// <summary>목표 대상의 상태(UI 마커 모양).</summary>
public enum DungeonObjectiveTargetState
{
    /// <summary>아직 쓸 수 없음(방 미클리어·출구 해금 전) — 흐린 잠금 표식.</summary>
    Locked = 0,
    /// <summary>쓸 수 있음 — 밝은 앰버, 화면 밖이면 가장자리 화살표.</summary>
    Ready = 1,
    /// <summary>완료 — 표시하지 않는다.</summary>
    Done = 2,
}

/// <summary>지금 방의 목표 대상 하나(월드 좌표 = 마커를 띄울 자리).</summary>
public readonly struct DungeonObjectiveTarget
{
    /// <summary>마커 기준 자리(월드). 보통 실물의 상호작용 지점 위.</summary>
    public readonly Vector3 world;
    public readonly DungeonObjectiveTargetKind kind;
    public readonly DungeonObjectiveTargetState state;
    /// <summary>레버면 목표 진행 번호(0부터), 출구면 0.</summary>
    public readonly int index;

    public DungeonObjectiveTarget(Vector3 world, DungeonObjectiveTargetKind kind, DungeonObjectiveTargetState state, int index)
    {
        this.world = world;
        this.kind = kind;
        this.state = state;
        this.index = index;
    }
}

/// <summary>
/// 지금 방의 목표 대상 실물 위치를 UI 마커(DungeonObjectiveMarkers)에 내주는 읽기 전용 접점. DungeonManager가 구현한다(선택 인터페이스).
/// 방을 옮기거나 대상 상태가 바뀌면 TargetsChanged를 올린다. 위치만 바뀌는 대상은 없다(레버·출구는 움직이지 않는다).
/// </summary>
public interface IDungeonObjectiveTargets
{
    /// <summary>지금 방의 대상(없으면 빈 목록, null 아님).</summary>
    System.Collections.Generic.IReadOnlyList<DungeonObjectiveTarget> CurrentTargets { get; }
    event Action TargetsChanged;
}

/// <summary>목표 대상 마커를 그리는 화면 요소(DungeonRulesSO.objectiveMarkersPrefab의 컴포넌트가 구현). DungeonManager가 생성한 뒤 Bind한다.</summary>
public interface IDungeonObjectiveTargetsView
{
    void Bind(IDungeonObjectiveTargets source);
    void Unbind();
}

/// <summary>실시간 난이도 5단계(4차 R17). 첫 클리어 방 수 문턱(기본 0·2·4·6·8)으로 오른다.</summary>
public enum DungeonThreatLevel
{
    /// <summary>입문.</summary>
    Intro = 0,
    /// <summary>하급.</summary>
    Low = 1,
    /// <summary>중급.</summary>
    Mid = 2,
    /// <summary>상급.</summary>
    High = 3,
    /// <summary>특급.</summary>
    Extreme = 4,
}

/// <summary>
/// 실시간 난이도를 UI(목표 패널 난이도 줄·현장 단말기·상태 띠 'THREAT ▲')에 내주는 읽기 전용 접점.
/// DungeonManager가 구현하고 계산은 DungeonThreatTracker에 위임한다(선택 인터페이스).
/// </summary>
public interface IDungeonThreatSource
{
    /// <summary>이 던전이 실시간 난이도(동적 적 구성, encounterMode = Dynamic)를 쓰는지.</summary>
    bool HasThreat { get; }
    DungeonThreatLevel ThreatLevel { get; }
    /// <summary>난이도에 센 첫 클리어 방 수(훈련 한 판 누적, 시작·적 없는 방·순찰 처치 제외).</summary>
    int ClearedRooms { get; }
    /// <summary>다음 단계가 되는 첫 클리어 방 수(최고 단계면 -1).</summary>
    int NextLevelAt { get; }
    /// <summary>지금 단계의 적 수 배율(기본 0.7·1.0·1.3·1.65·2.0).</summary>
    float DensityMultiplier { get; }
    /// <summary>단계가 바뀐 순간(이전, 새). 재도전으로 처음 단계로 돌아갈 때도 올린다.</summary>
    event Action<DungeonThreatLevel, DungeonThreatLevel> ThreatChanged;
}

/// <summary>난이도를 그리는 화면 요소. DungeonManager가 생성한 UI에서 GetComponentInChildren으로 찾아 Bind한다.</summary>
public interface IDungeonThreatView
{
    void Bind(IDungeonThreatSource source);
    void Unbind();
}

/// <summary>난이도 단계의 표시 글(한글 이름·영문 짧은 이름)과 색. UI 전부가 같은 표를 쓴다.</summary>
public static class DungeonThreatText
{
    private static readonly string[] labels = { "입문", "하급", "중급", "상급", "특급" };
    private static readonly string[] shorts = { "INTRO", "LOW", "MID", "HIGH", "EXTREME" };
    // 입문 청록·하급 녹색·중급 노랑·상급 주황·특급 빨강(DESIGN_V4 §5 — 화면에서 조정 가능한 판단값).
    private static readonly Color[] colors =
    {
        new Color(.25f, .92f, 1f),
        new Color(.36f, .95f, .42f),
        new Color(1f, .87f, .2f),
        new Color(1f, .55f, .12f),
        new Color(1f, .22f, .18f),
    };

    private static int Index(DungeonThreatLevel level) => Mathf.Clamp((int)level, 0, labels.Length - 1);

    /// <summary>한글 이름(입문·하급·중급·상급·특급).</summary>
    public static string Label(DungeonThreatLevel level) => labels[Index(level)];

    /// <summary>영문 짧은 이름(INTRO·LOW·MID·HIGH·EXTREME).</summary>
    public static string Short(DungeonThreatLevel level) => shorts[Index(level)];

    /// <summary>단계 색(감마 공간 UI 색).</summary>
    public static Color Color(DungeonThreatLevel level) => colors[Index(level)];
}
