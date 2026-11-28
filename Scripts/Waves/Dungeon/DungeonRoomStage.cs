using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 던전 방 하나를 세우고 거둔다(DungeonManager 소유, DESIGN.md §2.3). 지금 세워 둔 방은 언제나 하나다.
/// 세우기: (필요하면) 지형 셀 즉시 교체 → 방 배경(정적 벽면/시차) → Prop(부서진 것 제외) → 텔레포트 시설 → 목표 실물(배정된 슬롯, v3) → 연결된 출입구
/// → 시설 전원(즉시) → 완료한 목표는 완료 모습(즉시).
/// 거두기: 즉시 회수(관 통과로 화면이 가려진 동안) 또는 퇴장 연출.
/// 위치는 Wave 생성 기준점(spawnRoot) + 템플릿 좌표다(모든 방이 같은 방 로컬 원점에 선다).
/// 4차(DESIGN_V4 §2.6·§2.8): 시작 방 출구 문(IDungeonExitPoint — 출구 자리 room.exit에 목표 SO의 ExitPrefab)을 세우고, 동적 적 구성의 생성 후보 계산에 쓸
/// 세운 Prop의 경계(고체 Prop·위험 범위·크레인 경로·시설 자리)를 방 로컬 좌표로 내준다(Collider2D.bounds 읽기 — 물리 질의 없음).
/// 6차(DESIGN_V6 T11):
/// - 도착 문: 방에 도착 자리(room.arrival — DungeonArrivalSlot)가 있고 부르는 쪽이 arrivalPrefab을 주면 출구 문 Prefab을 도착 모드(닫히고 흐림)로 세운다(<see cref="Arrival"/>).
/// - 출구 문 떼어 내기(<see cref="DetachExit"/>): 문에서 다음 Wave로 넘어갈 때 닫힌 출구 문을 Leave가 거두지 않게 떼어 부르는 쪽(컷씬 감독)이 들고 있다가 반납한다.
/// - 미리보기(<see cref="BeginPreview"/>·<see cref="EndPreview"/>): 지금 방의 개체를 반납하지 않고 '보류'한 채(Renderer 끔·Collider2D 끔·중력 본체 정지·
///   Animator 정지·소리 일시정지·월드 Canvas 끔 — 개체·상태·위치는 그대로) 셀만 다른 방(시작 방)으로 바꾸고 그 방을 미리보기 전용 목록으로 세운다.
///   끝나면 미리보기 개체를 거두고 셀을 되돌린 뒤 보류를 푼다(Wave 장치 재조립·경로 크레인 집 복귀·중력 Prop 위치 손실 없음). 미리보기는 방 상태를 바꾸지 않는다
///   (부서짐 기록 구독 없음). 미리보기 중 Leave·Enter가 오면 미리보기를 먼저 거두고 보류한 방을 되살린 뒤 평소처럼 거둔다(새는 개체 없음).
///   T9(traps6) 함정 목록을 이 단계에 더할 때는 SuspendedRoom에도 같은 목록을 두고 보류·복원 때 함께 옮긴다(보류 방은 클리어 방이라 함정은 이미 정지).
/// </summary>
public sealed class DungeonRoomStage
{
    private struct Placed
    {
        public WorldEntityBase entity;
        public uint lease;
        public GameObject loose;
        public IDungeonBreakable breakable;
        public Action<IDungeonBreakable> handler;
        /// <summary>방 데이터 props 번호(4차 — 생성 후보 계산이 배치 정보를 다시 찾는다).</summary>
        public int index;
    }

    // 방 클리어에 반응하는 부품 하나(Prop·텔레포트 시설 안의 IDungeonRoomPowered). 풀로 돌아간 개체는 건드리지 않는다.
    private struct PoweredPart
    {
        public IDungeonRoomPowered part;
        public Component component;
        public WorldEntityBase owner;
        public uint lease;
    }

    private struct AnchorPart
    {
        public IDungeonSpawnAnchor anchor;
        public Component component;
        public WorldEntityBase owner;
        public uint lease;
        /// <summary>방 데이터 props 번호(텔레포트 시설 등 Prop이 아니면 -1).</summary>
        public int propIndex;
    }

    // 6차 T9(traps6): 방 클리어에 멈추는 함정 하나(Prop·Wave 장치 안의 IDungeonRoomHazard). 풀로 돌아간 개체는 건드리지 않는다.
    private struct HazardPart
    {
        public IDungeonRoomHazard part;
        public Component component;
        public WorldEntityBase owner;
        public uint lease;
    }

    // 목표 실물(레버 등) 하나. 풀 개체면 owner·lease, 아니면 loose(직접 만든 개체)다.
    private struct ObjectivePart
    {
        public IDungeonObjectivePoint point;
        public Component component;
        public WorldEntityBase owner;
        public uint lease;
        public GameObject loose;
    }

    private readonly WaveCellTerrain terrain;
    private readonly Func<Vector3> origin;
    private readonly Transform looseParent;
    private readonly List<Placed> props = new();
    private readonly List<(DungeonDoor door, uint lease)> doors = new();
    private readonly List<DungeonDoor> doorList = new();
    private readonly List<(DungeonTeleporter pad, uint lease)> teleporters = new();
    private readonly List<DungeonTeleporter> teleporterList = new();
    private readonly List<PoweredPart> powered = new();
    private readonly List<IDungeonRoomPowered> poweredList = new();
    private readonly HashSet<IDungeonRoomPowered> poweredOn = new();
    private readonly List<HazardPart> hazards = new();
    private readonly List<IDungeonRoomHazard> hazardList = new();
    private readonly List<AnchorPart> anchors = new();
    private readonly List<IDungeonSpawnAnchor> anchorList = new();
    private readonly List<ObjectivePart> objectives = new();
    private readonly List<IDungeonObjectivePoint> objectiveList = new();
    // 4차 출구 문(시작 방에만, 없으면 point = null).
    private ObjectivePart exitPart;
    private IDungeonExitPoint exitPoint;
    // 6차 도착 문(던전 시작 방 도착 자리에만, 없으면 gate = null).
    private ObjectivePart arrivalPart;
    private PropExitGate arrivalGate;
    // 6차 미리보기: 보류한 방(지금 목록은 미리보기 방). 미리보기 중이 아니면 null.
    private SuspendedRoom suspended;
    // 미리보기 방을 세우는 중(부서짐 기록 구독을 건너뛴다).
    private bool buildingPreview;

    public DungeonRoomData Room { get; private set; }
    public DungeonRoomState State { get; private set; }
    /// <summary>지금 세워 둔 출입구(연결된 것만).</summary>
    public IReadOnlyList<DungeonDoor> Doors => doorList;
    /// <summary>지금 세워 둔 텔레포트 시설(짝 없는 꺼진 받침 포함).</summary>
    public IReadOnlyList<DungeonTeleporter> Teleporters => teleporterList;
    /// <summary>지금 방의 방 클리어 반응 부품(Prop·시설). 세울 때 모은 목록이다.</summary>
    public IReadOnlyList<IDungeonRoomPowered> PoweredParts => poweredList;
    /// <summary>6차 T9: 지금 방의 함정(방 클리어에 멈추는 IDungeonRoomHazard). 세울 때 모은 목록이다(미리보기 중이면 미리보기 방의 것).</summary>
    public IReadOnlyList<IDungeonRoomHazard> Hazards => hazardList;
    /// <summary>지금 방 Prop의 적 생성 연출 앵커.</summary>
    public IReadOnlyList<IDungeonSpawnAnchor> SpawnAnchors => anchorList;
    /// <summary>지금 방에 세운 목표 실물(배정된 슬롯마다 하나, IDungeonObjectivePoint가 없는 Prefab은 빠진다).</summary>
    public IReadOnlyList<IDungeonObjectivePoint> ObjectivePoints => objectiveList;
    /// <summary>지금 방에 세운 출구 문(4차, 시작 방 출구 방식일 때 시작 방에만). 없거나 풀로 돌아갔으면 null.</summary>
    public IDungeonExitPoint Exit => exitPoint != null && AliveExit() ? exitPoint : null;
    /// <summary>6차: 지금 세운 도착 문(도착 모드 출구 문 — 던전 시작 방 도착 자리에만). 없거나 풀로 돌아갔으면 null.</summary>
    public PropExitGate Arrival => arrivalGate != null && Alive(arrivalPart) ? arrivalGate : null;
    /// <summary>6차: 다른 방을 미리보기로 세워 둔 중인지(그동안 Room·목록은 미리보기 방, 보류한 방은 <see cref="HeldRoom"/>).</summary>
    public bool IsPreviewing => suspended != null;
    /// <summary>6차: 미리보기 동안 보류한 방(미리보기 중이 아니면 null).</summary>
    public DungeonRoomData HeldRoom => suspended != null ? suspended.room : null;
    /// <summary>6차: 보류한 개체 루트 수(검사용 — 미리보기 중이 아니면 0).</summary>
    public int HeldObjectCount => suspended != null ? suspended.roots.Count : 0;
    /// <summary>마지막으로 이 방에 적용한 배경 종류(Parallax = 시차 배경 유지·ClearRoom). 배경 구현이 없으면 Auto.</summary>
    public DungeonBackdropMode AppliedBackdrop { get; private set; } = DungeonBackdropMode.Auto;
    /// <summary>마지막으로 보낸 방 배경 요청(ApplyRoom). Wall이 아니면 mode = Parallax.</summary>
    public DungeonRoomBackdropRequest LastBackdropRequest { get; private set; }
    /// <summary>Prop이 부서졌다(방 id, 방 데이터 props 번호).</summary>
    public event Action<int, int> PropBroken;

    /// <param name="terrain">셀을 바꿀 지형(없으면 셀 교체를 건너뛴다).</param>
    /// <param name="origin">Wave 생성 기준점의 월드 위치(spawnRoot.position).</param>
    /// <param name="looseParent">풀 개체가 아닌 Prop을 둘 부모(방 내용이 없어질 때 함께 없어진다).</param>
    public DungeonRoomStage(WaveCellTerrain terrain, Func<Vector3> origin, Transform looseParent)
    {
        this.terrain = terrain;
        this.origin = origin ?? (() => Vector3.zero);
        this.looseParent = looseParent;
    }

    /// <summary>
    /// 방을 세운다. 세워 둔 방이 있으면 먼저 즉시 거둔다. showCells면 지형 셀을 패널 연출 없이 바로 바꾸고 카메라·배경을 roomPoint(방 로컬)에서 찾은 공간에 맞춘다.
    /// doorState는 출입구마다 처음 상태(잠금·봉인)를 정한다(null이면 열림). 시설 전원은 state.cleared로 즉시 맞춘다.
    /// objective가 있으면 배정된 목표 슬롯에 objective.PrefabFor(index)를 세우고 RoomId·Slot·Index를 채운다(state.completedObjectives면 완료 모습).
    /// exitPrefab이 있고 방에 출구 자리(room.exit)가 있으면 그 자리에 출구 문을 세우고 RoomId를 채운다(4차 — 진행은 부르는 쪽이 SetProgress로 밀어 넣는다).
    /// </summary>
    /// <remarks>6차: arrivalPrefab이 있고 방에 도착 자리(room.arrival)가 있으면 그 자리에 출구 문을 도착 모드(닫히고 흐림)로 세운다(<see cref="Arrival"/>).</remarks>
    public void Enter(DungeonRoomData room, DungeonRoomState state, bool showCells, Vector2 roomPoint, Func<DungeonDoorPlacement, DungeonDoor.DoorState> doorState,
        DungeonObjectiveSO objective = null, GameObject exitPrefab = null, GameObject arrivalPrefab = null)
    {
        Leave(true);
        Build(room, state, showCells, roomPoint, doorState, objective, exitPrefab, arrivalPrefab);
    }

    // 방 하나를 지금 목록에 세운다(Enter·미리보기 공용 — 먼저 거두지 않는다).
    private void Build(DungeonRoomData room, DungeonRoomState state, bool showCells, Vector2 roomPoint, Func<DungeonDoorPlacement, DungeonDoor.DoorState> doorState,
        DungeonObjectiveSO objective, GameObject exitPrefab, GameObject arrivalPrefab)
    {
        if (room == null) return;
        Room = room;
        State = state;
        if (showCells && terrain != null) terrain.ShowRoom(room.cells, roomPoint);
        // 셀을 바꾼 직후(관 통과 덮개 아래) 방 배경을 맞춘다. 연결 공간은 ShowRoom이 방금 다시 찾았다.
        ApplyBackdrop(room);
        var at = origin();
        for (int i = 0; i < room.props.Length; i++)
        {
            var info = room.props[i];
            if (info == null || info.prefab == null) continue;
            if (state != null && state.destroyedProps.Contains(i)) continue;
            SpawnProp(info, at + (Vector3)info.spawnPosition, room.Id, i);
        }
        if (exitPrefab != null && room.HasExit) SpawnExit(room.exit, exitPrefab, at, room.Id);
        if (arrivalPrefab != null && room.HasArrival) SpawnArrival(room.arrival, arrivalPrefab, at, room.Id);
        foreach (var placement in room.teleports ?? Array.Empty<DungeonTeleportPlacement>())
            SpawnTeleporter(placement, at, room.Id);
        if (objective != null)
            foreach (var placement in room.objectives ?? Array.Empty<DungeonObjectivePlacement>())
                SpawnObjective(placement, at, room.Id, objective);
        foreach (var placement in room.doors)
        {
            if (placement == null || !placement.Connected || placement.prefab == null) continue;
            var prefab = placement.prefab.GetComponent<DungeonDoor>();
            if (prefab == null || WorldEntityPool.Instance == null) continue;
            var steps = ((placement.info.rotationSteps % 4) + 4) % 4;
            var entity = WorldEntityPool.Instance.Spawn(prefab, at + (Vector3)placement.info.position, Quaternion.Euler(0, 0, steps * 90f));
            if (!(entity is DungeonDoor door)) { if (entity != null) entity.ForceReturnToPool(); continue; }
            door.RoomId = room.Id;
            door.Socket = placement.socket;
            door.SetState(doorState != null ? doorState(placement) : DungeonDoor.DoorState.Open, true);
            doors.Add((door, door.PoolLease));
            doorList.Add(door);
        }
        // 이 방의 클리어 여부로 시설 전원을 바로 맞춘다(연출 없음).
        bool on = state != null && state.cleared;
        foreach (var part in powered) Apply(part, on, true);
        // 6차 T9: 함정은 이 방의 클리어 여부로 바로 맞춘다(클리어한 방 = 처음부터 정지 자세 — 재입장 순찰 중에도 멈춰 있다. 신호가 없으면 던전 밖처럼 작동).
        // 7차 V5: 레버 대기 방(objectiveHold — 적 전멸 순간 이미 멈춤)도 정지 자세로 세운다(안전장치 — 대기 중에는 방을 떠날 수 없다).
        foreach (var part in hazards) ApplyHazard(part, on || (state != null && state.objectiveHold));
        // 이미 적을 내보낸 앵커는 '내보낸 뒤' 모습으로 세운다(처치한 로봇이 거치대에 다시 잠든 채 보이지 않게).
        if (state != null && state.spentAnchors.Count > 0)
            foreach (var entry in anchors)
                if (entry.propIndex >= 0 && state.spentAnchors.Contains(entry.propIndex) && entry.anchor is IDungeonSpentAnchor spent)
                {
                    try { spent.MarkSpent(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
        // 목표 실물: 이 방에서 이미 완료한 슬롯은 완료 모습, 아니면 미완료 모습으로 바로 맞춘다(풀에서 다시 꺼낸 개체에 이전 상태가 남지 않게).
        foreach (var part in objectives)
        {
            if (part.point == null || !Alive(part)) continue;
            bool completed = state != null && state.completedObjectives.Contains(part.point.Slot);
            try { part.point.SetCompleted(completed, true); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>socket번 템플릿 출입구에 세운 출입구(없으면 null).</summary>
    public DungeonDoor DoorFor(int socket)
    {
        foreach (var (door, lease) in doors)
            if (Alive(door, lease) && door.Socket == socket) return door;
        return null;
    }

    /// <summary>pad번 텔레포트 항목에 세운 시설(없으면 null).</summary>
    public DungeonTeleporter TeleporterFor(int pad)
    {
        foreach (var (teleporter, lease) in teleporters)
            if (Alive(teleporter, lease) && teleporter.Pad == pad) return teleporter;
        return null;
    }

    /// <summary>이 단계가 세워 둔(아직 풀로 돌아가지 않은) 텔레포트 시설인지.</summary>
    public bool Owns(DungeonTeleporter teleporter)
    {
        foreach (var (pad, lease) in teleporters)
            if (ReferenceEquals(pad, teleporter) && Alive(pad, lease)) return true;
        return false;
    }

    /// <summary>이 단계가 세워 둔(아직 풀로 돌아가지 않은) 목표 실물인지.</summary>
    public bool Owns(IDungeonObjectivePoint point)
    {
        if (point == null) return false;
        foreach (var part in objectives)
            if (ReferenceEquals(part.point, point) && Alive(part)) return true;
        return false;
    }

    /// <summary>이 단계가 세워 둔(아직 풀로 돌아가지 않은) 출구 문인지(4차).</summary>
    public bool Owns(IDungeonExitPoint exit) => exit != null && ReferenceEquals(exit, exitPoint) && AliveExit();

    /// <summary>
    /// 세운 Prop에서 동적 적 구성의 생성 후보를 막는 영역을 방 로컬 좌표(월드 − origin)로 모은다(4차 DESIGN_V4 §2.8, Collider2D.bounds 읽기만 — 물리 질의 없음).
    /// solids = 발판·파괴형 Prop의 고체 경계(받침·막힘). exclusions = 위험 장치(함정 Prop·맵 층 장치)의 점유 칸·작동 범위·경계 + margin칸,
    /// 크레인(배치 설정 cranePathCount·zipRadius가 있는 장치)의 점유 칸과 경로 쓸기 상자(발 ~ 발 + 4칸, 폭 2칸), 상호작용 시설의 점유 칸.
    /// 부서진 Prop은 빠진다(세우지 않았으므로). 움직인 중력 Prop은 지금 경계를 읽는다.
    /// </summary>
    public void CollectSpawnBlockers(List<Rect> solids, List<Rect> exclusions, float margin = 1f)
    {
        if (Room == null) return;
        Vector2 at = origin();
        foreach (var placed in props)
        {
            GameObject target = placed.entity != null ? (Alive(placed.entity, placed.lease) ? placed.entity.gameObject : null) : placed.loose;
            if (target == null || !target.activeInHierarchy) continue;
            var info = placed.index >= 0 && placed.index < Room.props.Length ? Room.props[placed.index] : null;
            if (info == null) continue;
            try { DungeonSpawnBlockers.Collect(target, info, at, solids, exclusions, margin); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>slot번 목표 슬롯에 세운 실물(없으면 null).</summary>
    public IDungeonObjectivePoint ObjectiveFor(int slot)
    {
        foreach (var part in objectives)
            if (part.point != null && Alive(part) && part.point.Slot == slot) return part.point;
        return null;
    }

    /// <summary>
    /// 세워 둔 출입구의 상태를 다시 정한다. silent면 출입구마다 열림↔닫힘 효과음을 내지 않는다
    /// (방 전체 잠금·해제처럼 여러 출입구가 함께 바뀔 때 부르는 쪽이 한 번만 낸다. 같은 소리가 겹치거나 먼 출입구의 작은 소리가 남지 않게).
    /// </summary>
    public void SetDoorStates(Func<DungeonDoorPlacement, DungeonDoor.DoorState> doorState, bool instant, bool silent = false)
    {
        if (Room == null || doorState == null) return;
        foreach (var (door, lease) in doors)
        {
            if (!Alive(door, lease) || door.Socket < 0 || door.Socket >= Room.doors.Length) continue;
            var next = doorState(Room.doors[door.Socket]);
            if (door.State != next) door.SetState(next, instant, silent);
        }
    }

    /// <summary>부품 하나의 전원을 바꾼다(풀로 돌아간 개체는 건너뛴다). 클리어 순간 DungeonManager가 Player에 가까운 것부터 차례로 부른다.</summary>
    public void SetPowered(IDungeonRoomPowered part, bool on, bool instant)
    {
        foreach (var entry in powered)
            if (ReferenceEquals(entry.part, part)) { Apply(entry, on, instant); return; }
    }

    /// <summary>이 단계가 마지막으로 켠 부품인지(회귀 검사용).</summary>
    public bool IsPoweredOn(IDungeonRoomPowered part) => part != null && poweredOn.Contains(part);

    /// <summary>부품의 월드 위치(가까운 순서 정렬용). 컴포넌트가 아니면 null.</summary>
    public Vector3? PartPosition(IDungeonRoomPowered part)
    {
        foreach (var entry in powered)
            if (ReferenceEquals(entry.part, part)) return entry.component != null ? entry.component.transform.position : (Vector3?)null;
        return null;
    }

    /// <summary>살아 있는 생성 앵커 중 position(월드)을 담는 첫 앵커(없으면 null).</summary>
    public IDungeonSpawnAnchor AnchorAt(Vector3 position) => AnchorAt(position, out _);

    /// <summary>살아 있는 생성 앵커 중 position(월드)을 담는 첫 앵커와 그 Prop 번호(없으면 null·-1).</summary>
    public IDungeonSpawnAnchor AnchorAt(Vector3 position, out int propIndex)
    {
        propIndex = -1;
        foreach (var entry in anchors)
        {
            if (!ReferenceEquals(entry.component, null) && (entry.component == null || !entry.component.gameObject.activeInHierarchy)) continue;
            if (entry.owner != null && !Alive(entry.owner, entry.lease)) continue;
            Rect area;
            try { area = entry.anchor.WorldArea; }
            catch (Exception e) { Debug.LogException(e); continue; }
            if (area.Contains((Vector2)position)) { propIndex = entry.propIndex; return entry.anchor; }
        }
        return null;
    }

    /// <summary>
    /// 세워 둔 방을 거둔다. immediate면 바로 풀로 돌려보낸다(관 통과로 화면이 가려진 동안). 아니면 각자의 퇴장 연출을 재생한다(던전 끝).
    /// 지형 셀과 방 배경은 그대로 둔다(다음 방 세우기나 다음 Wave 지형 준비·배경 초기화가 바꾼다).
    /// </summary>
    public void Leave(bool immediate)
    {
        // 6차: 미리보기 중이면 미리보기 개체를 먼저 거두고 보류한 방을 되살린 뒤 아래에서 함께 거둔다.
        if (suspended != null) RestoreHeld(false, default);
        ReturnLive(immediate);
    }

    // 지금 목록(세운 방 또는 미리보기 방)의 개체를 모두 거두고 목록을 비운다.
    private void ReturnLive(bool immediate)
    {
        foreach (var placed in props)
        {
            if (placed.breakable != null && placed.handler != null) placed.breakable.Broken -= placed.handler;
            if (placed.entity != null) Return(placed.entity, placed.lease, immediate);
            else if (placed.loose != null) Object.Destroy(placed.loose);
        }
        props.Clear();
        foreach (var (teleporter, lease) in teleporters) Return(teleporter, lease, immediate);
        teleporters.Clear();
        teleporterList.Clear();
        foreach (var (door, lease) in doors) Return(door, lease, immediate);
        doors.Clear();
        doorList.Clear();
        foreach (var part in objectives)
        {
            if (part.owner != null) Return(part.owner, part.lease, immediate);
            else if (part.loose != null) Object.Destroy(part.loose);
        }
        objectives.Clear();
        objectiveList.Clear();
        if (exitPart.owner != null) Return(exitPart.owner, exitPart.lease, immediate);
        else if (exitPart.loose != null) Object.Destroy(exitPart.loose);
        exitPart = default;
        exitPoint = null;
        if (arrivalPart.owner != null) Return(arrivalPart.owner, arrivalPart.lease, immediate);
        else if (arrivalPart.loose != null) Object.Destroy(arrivalPart.loose);
        arrivalPart = default;
        arrivalGate = null;
        powered.Clear();
        poweredList.Clear();
        poweredOn.Clear();
        hazards.Clear();
        hazardList.Clear();
        anchors.Clear();
        anchorList.Clear();
        Room = null;
        State = null;
    }

    // ── 6차: 미리보기(보류)·출구 문 떼어 내기 ──

    /// <summary>
    /// 지금 방을 보류하고 room(보통 던전 시작 방 = 출구 방)을 미리보기로 세운다(6차 레버 컷씬). 지금 방의 개체는 반납하지 않고 숨긴다
    /// (Renderer 끔·Collider2D 끔·중력 본체 등 CharacterController2D 정지·Animator 정지·재생 중 소리 일시정지·월드 Canvas 끔 — 개체·상태·위치 유지).
    /// 셀은 room.cells로 즉시 바꾸고 카메라·배경 범위는 roomPoint(방 로컬)에서 찾은 공간에 맞춘다. 미리보기 방 개체는 방 상태를 바꾸지 않는다(부서짐 기록 구독 없음).
    /// 세운 방이 없거나 이미 미리보기 중이면 false(아무것도 바꾸지 않는다).
    /// </summary>
    public bool BeginPreview(DungeonRoomData room, DungeonRoomState state, Vector2 roomPoint, Func<DungeonDoorPlacement, DungeonDoor.DoorState> doorState,
        DungeonObjectiveSO objective = null, GameObject exitPrefab = null, GameObject arrivalPrefab = null)
    {
        if (room == null || Room == null || suspended != null) return false;
        var held = new SuspendedRoom();
        // 지금 목록을 보류한 방으로 옮긴다(개체는 그대로 둔다).
        held.props.AddRange(props); props.Clear();
        held.doors.AddRange(doors); doors.Clear();
        held.doorList.AddRange(doorList); doorList.Clear();
        held.teleporters.AddRange(teleporters); teleporters.Clear();
        held.teleporterList.AddRange(teleporterList); teleporterList.Clear();
        held.powered.AddRange(powered); powered.Clear();
        held.poweredList.AddRange(poweredList); poweredList.Clear();
        held.poweredOn.UnionWith(poweredOn); poweredOn.Clear();
        held.hazards.AddRange(hazards); hazards.Clear();
        held.hazardList.AddRange(hazardList); hazardList.Clear();
        held.anchors.AddRange(anchors); anchors.Clear();
        held.anchorList.AddRange(anchorList); anchorList.Clear();
        held.objectives.AddRange(objectives); objectives.Clear();
        held.objectiveList.AddRange(objectiveList); objectiveList.Clear();
        held.exitPart = exitPart; held.exitPoint = exitPoint; exitPart = default; exitPoint = null;
        held.arrivalPart = arrivalPart; held.arrivalGate = arrivalGate; arrivalPart = default; arrivalGate = null;
        held.room = Room; held.state = State;
        held.backdrop = AppliedBackdrop; held.backdropRequest = LastBackdropRequest;
        // 보류: 개체마다 지금 상태를 적어 두고 숨긴다.
        foreach (var placed in held.props) HoldPart(held, placed.entity, placed.lease, placed.loose);
        foreach (var (door, lease) in held.doors) HoldPart(held, door, lease, null);
        foreach (var (pad, lease) in held.teleporters) HoldPart(held, pad, lease, null);
        foreach (var part in held.objectives) HoldPart(held, part.owner, part.lease, part.loose);
        HoldPart(held, held.exitPart.owner, held.exitPart.lease, held.exitPart.loose);
        HoldPart(held, held.arrivalPart.owner, held.arrivalPart.lease, held.arrivalPart.loose);
        suspended = held;
        RaycastMotor2D.InvalidateAttackQuerySync();
        Room = null;
        State = null;
        buildingPreview = true;
        try { Build(room, state, true, roomPoint, doorState, objective, exitPrefab, arrivalPrefab); }
        finally { buildingPreview = false; }
        return true;
    }

    /// <summary>
    /// 미리보기를 거두고 보류한 방을 되살린다(6차): 미리보기 개체 반납 → 셀을 보류한 방으로(roomPoint = 그 방 로컬, 보통 Player 몸통) → 배경 → 보류 해제
    /// → 미리보기 동안 끊긴 시설 전원 차례 켜기를 마저 즉시 맞춤(클리어 방). 미리보기 중이 아니면 아무것도 하지 않는다.
    /// </summary>
    public void EndPreview(Vector2 roomPoint)
    {
        if (suspended == null) return;
        RestoreHeld(true, roomPoint);
    }

    // 미리보기 개체를 거두고 보류한 방의 목록·개체를 되살린다. showCells면 셀·배경도 그 방으로 되돌린다(Leave에서 부를 때는 다음 세우기·Wave 준비가 바꾼다).
    private void RestoreHeld(bool showCells, Vector2 roomPoint)
    {
        var held = suspended;
        suspended = null;
        ReturnLive(true);
        props.AddRange(held.props);
        doors.AddRange(held.doors);
        doorList.AddRange(held.doorList);
        teleporters.AddRange(held.teleporters);
        teleporterList.AddRange(held.teleporterList);
        powered.AddRange(held.powered);
        poweredList.AddRange(held.poweredList);
        poweredOn.UnionWith(held.poweredOn);
        // 6차 T9: 보류 중에는 개체를 끄지 않으므로 함정 정지 상태가 그대로 남는다(다시 보내지 않는다).
        hazards.AddRange(held.hazards);
        hazardList.AddRange(held.hazardList);
        anchors.AddRange(held.anchors);
        anchorList.AddRange(held.anchorList);
        objectives.AddRange(held.objectives);
        objectiveList.AddRange(held.objectiveList);
        exitPart = held.exitPart;
        exitPoint = held.exitPoint;
        arrivalPart = held.arrivalPart;
        arrivalGate = held.arrivalGate;
        Room = held.room;
        State = held.state;
        if (showCells && Room != null)
        {
            if (terrain != null) terrain.ShowRoom(Room.cells, roomPoint);
            ApplyBackdrop(Room);
        }
        else
        {
            AppliedBackdrop = held.backdrop;
            LastBackdropRequest = held.backdropRequest;
        }
        held.Release();
        RaycastMotor2D.InvalidateAttackQuerySync();
        // 클리어 방의 시설 전원은 DungeonManager가 Player에 가까운 순서로 켜는데(PowerUpRoom), 방이 바뀌면 그 순서가 멈춘다 — 남은 것을 바로 켠다.
        if (State != null && State.cleared)
            foreach (var part in powered)
                if (part.part != null && !poweredOn.Contains(part.part)) Apply(part, true, true);
    }

    // 보류할 개체 하나(풀 개체면 같은 대여일 때만).
    private static void HoldPart(SuspendedRoom held, WorldEntityBase owner, uint lease, GameObject loose)
    {
        if (owner != null) { if (Alive(owner, lease)) held.Hold(owner.gameObject, owner, lease); }
        else if (loose != null) held.Hold(loose, null, 0);
    }

    /// <summary>
    /// 6차: 지금 세운 출구 문을 이 단계에서 떼어 낸다(Leave가 거두지 않는다). 컷씬 감독이 닫힌 문을 화면에 둔 채 다음 Wave로 넘어간 뒤
    /// <see cref="DetachedPart.Release"/>로 반납한다. 출구 문이 없으면 빈 값(IsValid = false).
    /// </summary>
    public DetachedPart DetachExit()
    {
        if (exitPoint == null || !AliveExit()) return default;
        var part = new DetachedPart(exitPart.component, exitPart.owner, exitPart.lease, exitPart.loose);
        exitPart = default;
        exitPoint = null;
        return part;
    }

    /// <summary>6차: 단계에서 떼어 낸 개체(풀 개체면 대여 번호로 같은 대여인지 확인한다 — 이미 돌아갔거나 다시 빌려 간 개체는 건드리지 않는다).</summary>
    public readonly struct DetachedPart
    {
        public readonly Component component;
        private readonly WorldEntityBase owner;
        private readonly uint lease;
        private readonly GameObject loose;

        internal DetachedPart(Component component, WorldEntityBase owner, uint lease, GameObject loose)
        {
            this.component = component;
            this.owner = owner;
            this.lease = lease;
            this.loose = loose;
        }

        public bool IsValid => owner != null || loose != null;
        public bool IsAlive => owner != null ? DungeonRoomStage.Alive(owner, lease) : loose != null;

        /// <summary>풀로 돌려보낸다(immediate면 바로, 아니면 퇴장 연출). 직접 만든 개체면 지운다.</summary>
        public void Release(bool immediate)
        {
            if (owner != null) Return(owner, lease, immediate);
            else if (loose != null) Object.Destroy(loose);
        }
    }

    /// <summary>방 배경을 시차 배경으로 되돌린다(재도전으로 새 시작 방을 바로 세울 때 등).</summary>
    public void ClearBackdrop()
    {
        var target = BackdropTarget();
        if (target == null) { AppliedBackdrop = DungeonBackdropMode.Auto; return; }
        try { target.ClearRoom(); AppliedBackdrop = DungeonBackdropMode.Parallax; }
        catch (Exception e) { Debug.LogException(e); }
        LastBackdropRequest = new DungeonRoomBackdropRequest { mode = DungeonBackdropMode.Parallax };
    }

    // ── 배경 ──
    // Wall 방이고 키트 Prefab이 있으면 정적 벽면을, 아니면 시차 배경(ClearRoom)을 요청한다. 배경 구현(IDungeonRoomBackdrop)이 없으면 건너뛴다.
    private void ApplyBackdrop(DungeonRoomData room)
    {
        var target = BackdropTarget();
        if (target == null) { AppliedBackdrop = DungeonBackdropMode.Auto; return; }
        var rules = DungeonRulesSO.Default;
        var kit = room.backdrop == DungeonBackdropMode.Wall && rules != null ? rules.WallKitPrefab(room.wallKit) : null;
        try
        {
            if (kit != null)
            {
                var request = new DungeonRoomBackdropRequest
                {
                    mode = DungeonBackdropMode.Wall,
                    kit = room.wallKit,
                    kitPrefab = kit,
                    bounds = terrain != null && terrain.ConnectedBounds.HasValue ? terrain.ConnectedBounds.Value : room.interior,
                };
                LastBackdropRequest = request;
                target.ApplyRoom(request);
                AppliedBackdrop = DungeonBackdropMode.Wall;
            }
            else
            {
                LastBackdropRequest = new DungeonRoomBackdropRequest { mode = DungeonBackdropMode.Parallax, kit = room.wallKit, bounds = room.interior };
                target.ClearRoom();
                AppliedBackdrop = DungeonBackdropMode.Parallax;
            }
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    // 방 배경 구현(WaveRoomBackground가 IDungeonRoomBackdrop을 구현하면 지형과 같은 GameObject에 있다). 없으면 null.
    private IDungeonRoomBackdrop BackdropTarget()
    {
        if (terrain == null) return null;
        var target = terrain.GetComponent<IDungeonRoomBackdrop>();
        return target is Object unity && unity == null ? null : target;
    }

    // ── 세우기 ──
    private void SpawnProp(WaveSpawnInfo info, Vector3 position, int roomId, int index)
    {
        var placed = new Placed();
        GameObject target;
        if (info.prefab.TryGetComponent(out WorldEntityBase prefab) && WorldEntityPool.Instance != null)
        {
            var entity = WorldEntityPool.Instance.Spawn(prefab, position, info.Rotation);
            if (entity == null) return;
            // 배치마다 저장한 장치 설정을 이 개체에만 적용한다(WaveManager와 같은 경로).
            WaveDeviceSettings.Apply(entity.gameObject, info.deviceSettings);
            placed.entity = entity;
            placed.lease = entity.PoolLease;
            target = entity.gameObject;
        }
        else
        {
            target = Object.Instantiate(info.prefab, position, info.Rotation, looseParent);
            placed.loose = target;
        }
        var breakable = buildingPreview ? null : target.GetComponentInChildren<IDungeonBreakable>(true);
        if (breakable != null)
        {
            placed.breakable = breakable;
            placed.handler = broken => OnBroken(broken, roomId, index);
            breakable.Broken += placed.handler;
        }
        placed.index = index;
        props.Add(placed);
        Collect(target, placed.entity, placed.lease, index);
    }

    private void SpawnTeleporter(DungeonTeleportPlacement placement, Vector3 at, int roomId)
    {
        if (placement == null || placement.info == null || placement.info.prefab == null || WorldEntityPool.Instance == null) return;
        var prefab = placement.info.prefab.GetComponent<DungeonTeleporter>();
        if (prefab == null) return;
        var entity = WorldEntityPool.Instance.Spawn(prefab, at + (Vector3)placement.info.spawnPosition, placement.info.Rotation);
        if (!(entity is DungeonTeleporter pad)) { if (entity != null) entity.ForceReturnToPool(); return; }
        WaveDeviceSettings.Apply(pad.gameObject, placement.info.deviceSettings);
        // 풀에서 꺼낼 때 초기화되므로 꺼낸 뒤에 채운다.
        pad.RoomId = roomId;
        pad.Pad = placement.pad;
        pad.LinkedRoom = placement.toRoom;
        pad.LinkedPad = placement.toPad;
        teleporters.Add((pad, pad.PoolLease));
        teleporterList.Add(pad);
        Collect(pad.gameObject, pad, pad.PoolLease);
    }

    // 배정된 목표 슬롯에 목표 SO의 실물을 세운다(텔레포트 시설과 같은 방식: 풀 개체면 풀에서, 아니면 직접 만든다).
    // 표식(슬롯)의 위치·회전만 쓰고 장치 설정은 옮기지 않는다(표식과 실물은 다른 장치다). 실물의 방 클리어 반응 부품도 모은다.
    private void SpawnObjective(DungeonObjectivePlacement placement, Vector3 at, int roomId, DungeonObjectiveSO objective)
    {
        if (placement == null || !placement.Assigned || placement.info == null) return;
        GameObject prefab;
        try { prefab = objective.PrefabFor(placement.index); }
        catch (Exception e) { Debug.LogException(e); return; }
        if (prefab == null) return;
        var position = at + (Vector3)placement.info.spawnPosition;
        var part = new ObjectivePart();
        GameObject target;
        if (prefab.TryGetComponent(out WorldEntityBase pooled) && WorldEntityPool.Instance != null)
        {
            var entity = WorldEntityPool.Instance.Spawn(pooled, position, placement.info.Rotation);
            if (entity == null) return;
            part.owner = entity;
            part.lease = entity.PoolLease;
            target = entity.gameObject;
        }
        else
        {
            target = Object.Instantiate(prefab, position, placement.info.Rotation, looseParent);
            part.loose = target;
        }
        // 풀에서 꺼낼 때 초기화되므로 꺼낸 뒤에 채운다.
        var point = target.GetComponentInChildren<IDungeonObjectivePoint>(true);
        if (point != null)
        {
            point.RoomId = roomId;
            point.Slot = placement.slot;
            point.Index = placement.index;
            part.point = point;
            part.component = point as Component;
            objectiveList.Add(point);
        }
        else Debug.LogWarning($"[DungeonRoomStage] 목표 실물 '{prefab.name}'에 IDungeonObjectivePoint가 없어 완료할 수 없습니다.", prefab);
        objectives.Add(part);
        Collect(target, part.owner, part.lease);
    }

    // 4차: 시작 방 출구 자리에 출구 문을 세운다(목표 실물과 같은 방식: 풀 개체면 풀에서, 아니면 직접 만든다). 표식의 위치·회전만 쓴다.
    private void SpawnExit(WaveSpawnInfo slot, GameObject prefab, Vector3 at, int roomId)
    {
        if (slot == null || prefab == null) return;
        var position = at + (Vector3)slot.spawnPosition;
        var part = new ObjectivePart();
        GameObject target;
        if (prefab.TryGetComponent(out WorldEntityBase pooled) && WorldEntityPool.Instance != null)
        {
            var entity = WorldEntityPool.Instance.Spawn(pooled, position, slot.Rotation);
            if (entity == null) return;
            part.owner = entity;
            part.lease = entity.PoolLease;
            target = entity.gameObject;
        }
        else
        {
            target = Object.Instantiate(prefab, position, slot.Rotation, looseParent);
            part.loose = target;
        }
        // 풀에서 꺼낼 때 초기화되므로 꺼낸 뒤에 채운다.
        var point = target.GetComponentInChildren<IDungeonExitPoint>(true);
        if (point != null)
        {
            point.RoomId = roomId;
            part.component = point as Component;
            exitPoint = point;
        }
        else Debug.LogWarning($"[DungeonRoomStage] 출구 문 '{prefab.name}'에 IDungeonExitPoint가 없어 들어갈 수 없습니다.", prefab);
        exitPart = part;
        Collect(target, part.owner, part.lease);
    }

    // 6차: 던전 시작 방 도착 자리에 출구 문 Prefab을 도착 모드(닫히고 흐림 — 장식)로 세운다(출구 문과 같은 방식). 컷씬 감독이 도착 때 불을 켜고 연다.
    private void SpawnArrival(WaveSpawnInfo slot, GameObject prefab, Vector3 at, int roomId)
    {
        if (slot == null || prefab == null) return;
        var position = at + (Vector3)slot.spawnPosition;
        var part = new ObjectivePart();
        GameObject target;
        if (prefab.TryGetComponent(out WorldEntityBase pooled) && WorldEntityPool.Instance != null)
        {
            var entity = WorldEntityPool.Instance.Spawn(pooled, position, slot.Rotation);
            if (entity == null) return;
            part.owner = entity;
            part.lease = entity.PoolLease;
            target = entity.gameObject;
        }
        else
        {
            target = Object.Instantiate(prefab, position, slot.Rotation, looseParent);
            part.loose = target;
        }
        // 풀에서 꺼낼 때 초기화되므로 꺼낸 뒤에 도착 모드로 바꾼다.
        var gate = target.GetComponentInChildren<PropExitGate>(true);
        if (gate != null)
        {
            gate.RoomId = roomId;
            part.component = gate;
            arrivalGate = gate;
            try { gate.SetArrivalSealed(false, true); }
            catch (Exception e) { Debug.LogException(e); }
        }
        else Debug.LogWarning($"[DungeonRoomStage] 도착 문 '{prefab.name}'에 PropExitGate가 없어 도착 문으로 쓸 수 없습니다.", prefab);
        arrivalPart = part;
        Collect(target, part.owner, part.lease);
    }

    private bool AliveExit()
    {
        if (exitPoint == null) return false;
        return Alive(exitPart);
    }

    // 개체 안의 방 클리어 반응 부품과 생성 앵커를 모은다(같은 부품은 한 번만). propIndex = 방 데이터 props 번호(Prop이 아니면 -1).
    private void Collect(GameObject target, WorldEntityBase owner, uint lease, int propIndex = -1)
    {
        if (target == null) return;
        foreach (var part in target.GetComponentsInChildren<IDungeonRoomPowered>(true))
        {
            if (part == null || poweredList.Contains(part)) continue;
            powered.Add(new PoweredPart { part = part, component = part as Component, owner = owner, lease = lease });
            poweredList.Add(part);
        }
        var roomHazards = target.GetComponentsInChildren<IDungeonRoomHazard>(true);
        if (roomHazards.Length > 0) DungeonHazardSorting.MoveInFrontOfProps(target);
        foreach (var part in roomHazards)
        {
            if (part == null || hazardList.Contains(part)) continue;
            hazards.Add(new HazardPart { part = part, component = part as Component, owner = owner, lease = lease });
            hazardList.Add(part);
        }
        foreach (var anchor in target.GetComponentsInChildren<IDungeonSpawnAnchor>(true))
        {
            if (anchor == null || anchorList.Contains(anchor)) continue;
            anchors.Add(new AnchorPart { anchor = anchor, component = anchor as Component, owner = owner, lease = lease, propIndex = propIndex });
            anchorList.Add(anchor);
        }
    }

    /// <summary>
    /// 6차 T9: 방 클리어 순간(ROOM CLEAR와 같은 프레임) 지금 방의 함정을 모두 멈춘다. 판정은 이 호출 안에서 모두 꺼지고,
    /// 외형은 from(Player)에 가까운 순서로 DungeonHazardHalt.WaveInterval(0.04초) 간격으로 꺼져 간다(정전 물결). 멈춘 함정 수를 돌려준다.
    /// </summary>
    public int HaltHazards(Vector3 from)
    {
        var live = new List<IDungeonRoomHazard>(hazards.Count);
        foreach (var entry in hazards) if (Alive(entry)) live.Add(entry.part);
        return DungeonHazardHalt.HaltWave(live, from, DungeonHazardHalt.WaveInterval);
    }

    private static bool Alive(HazardPart entry)
    {
        if (entry.part == null) return false;
        if (!ReferenceEquals(entry.component, null) && entry.component == null) return false;   // 파괴된 컴포넌트
        return entry.owner == null || Alive(entry.owner, entry.lease);
    }

    // 방에 들어올 때(세우기): 클리어한 방이면 바로 정지 자세, 아니면 작동(신호만 — 던전 안).
    private static void ApplyHazard(HazardPart entry, bool cleared)
    {
        if (Alive(entry)) DungeonHazardHalt.Send(entry.part, cleared, true);
    }

    private void Apply(PoweredPart entry, bool on, bool instant)
    {
        if (entry.part == null) return;
        if (!ReferenceEquals(entry.component, null) && entry.component == null) return;   // 파괴된 컴포넌트
        if (entry.owner != null && !Alive(entry.owner, entry.lease)) return;
        try { entry.part.SetRoomPowered(on, instant); }
        catch (Exception e) { Debug.LogException(e); return; }
        if (on) poweredOn.Add(entry.part); else poweredOn.Remove(entry.part);
    }

    private void OnBroken(IDungeonBreakable broken, int roomId, int index)
    {
        for (int i = 0; i < props.Count; i++)
        {
            if (!ReferenceEquals(props[i].breakable, broken)) continue;
            var placed = props[i];
            if (placed.handler != null) broken.Broken -= placed.handler;
            placed.breakable = null; placed.handler = null;
            props[i] = placed;
            break;
        }
        if (State != null && Room != null && Room.Id == roomId) State.destroyedProps.Add(index);
        PropBroken?.Invoke(roomId, index);
    }

    private static bool Alive(ObjectivePart part)
    {
        if (!ReferenceEquals(part.component, null) && part.component == null) return false;   // 파괴된 컴포넌트
        if (part.owner != null) return Alive(part.owner, part.lease);
        return part.loose != null;
    }

    // 풀에서 다시 빌려 간 개체(빌린 번호가 바뀐 것)나 이미 돌아간 개체는 건드리지 않는다.
    private static bool Alive(WorldEntityBase entity, uint lease) => entity != null && entity.PoolLease == lease && entity.gameObject.activeSelf;

    private static void Return(WorldEntityBase entity, uint lease, bool immediate)
    {
        // 풀이 이미 없어졌으면(씬을 내리는 중) 돌려보낼 곳이 없다.
        if (!Alive(entity, lease) || WorldEntityPool.Instance == null) return;
        if (immediate && WorldEntityPool.Instance != null && entity.poolPrefab != null) WorldEntityPool.Instance.Return(entity.poolPrefab, entity);
        else entity.ForceReturnToPool();
    }

    // ── 6차 보류한 방 ──
    // 미리보기 동안 보류한 방의 목록과, 개체마다 끈 부품의 원래 상태. 풀로 돌아가 다시 쓰인 개체는 되돌리지 않는다(대여 번호 확인).
    private sealed class SuspendedRoom
    {
        public readonly List<Placed> props = new();
        public readonly List<(DungeonDoor door, uint lease)> doors = new();
        public readonly List<DungeonDoor> doorList = new();
        public readonly List<(DungeonTeleporter pad, uint lease)> teleporters = new();
        public readonly List<DungeonTeleporter> teleporterList = new();
        public readonly List<PoweredPart> powered = new();
        public readonly List<IDungeonRoomPowered> poweredList = new();
        public readonly HashSet<IDungeonRoomPowered> poweredOn = new();
        public readonly List<HazardPart> hazards = new();
        public readonly List<IDungeonRoomHazard> hazardList = new();
        public readonly List<AnchorPart> anchors = new();
        public readonly List<IDungeonSpawnAnchor> anchorList = new();
        public readonly List<ObjectivePart> objectives = new();
        public readonly List<IDungeonObjectivePoint> objectiveList = new();
        public ObjectivePart exitPart;
        public IDungeonExitPoint exitPoint;
        public ObjectivePart arrivalPart;
        public PropExitGate arrivalGate;
        public DungeonRoomData room;
        public DungeonRoomState state;
        public DungeonBackdropMode backdrop;
        public DungeonRoomBackdropRequest backdropRequest;
        public readonly List<HeldObject> roots = new();

        // 개체 하나를 숨긴다(같은 루트는 한 번만). 보이는 것·물리 판정·움직임·소리만 멈추고 상태 값은 건드리지 않는다.
        public void Hold(GameObject root, WorldEntityBase owner, uint lease)
        {
            if (root == null) return;
            foreach (var entry in roots) if (entry.root == root) return;
            var held = new HeldObject { root = root, owner = owner, lease = lease };
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                held.renderers.Add((r, r.forceRenderingOff));
                r.forceRenderingOff = true;
            }
            foreach (var c in root.GetComponentsInChildren<Collider2D>(true))
            {
                if (c == null) continue;
                held.colliders.Add((c, c.enabled));
                c.enabled = false;
            }
            // 모터로 움직이는 본체(중력 Prop 등)는 셀이 바뀐 동안 떨어지지 않게 멈춘다(CharacterController2D는 OnEnable·OnDisable이 없어 켜고 끄기만 한다).
            foreach (var body in root.GetComponentsInChildren<CharacterController2D>(true))
            {
                if (body == null) continue;
                held.behaviours.Add((body, body.enabled));
                body.enabled = false;
            }
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas == null) continue;
                held.behaviours.Add((canvas, canvas.enabled));
                canvas.enabled = false;
            }
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (animator == null) continue;
                held.animators.Add((animator, animator.speed));
                animator.speed = 0f;
            }
            foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
            {
                if (source == null || !source.isPlaying) continue;
                source.Pause();
                held.paused.Add(source);
            }
            roots.Add(held);
        }

        // 숨긴 개체를 원래 상태로 되돌린다. 보류 중 스스로 풀로 돌아간 개체(부서진 상자 등 — 같은 대여·비활성)도 되돌린다:
        // 풀 초기화(OnSpawnedFromPool)는 forceRenderingOff·Animator.speed를 되돌리지 않으므로, 그 사이 다시 빌려 간 개체여도 이 둘은 되돌린다.
        public void Release()
        {
            foreach (var held in roots)
            {
                if (held.root == null) continue;
                bool sameLease = held.owner == null || held.owner.PoolLease == held.lease;
                foreach (var (r, off) in held.renderers) if (r != null) r.forceRenderingOff = off;
                foreach (var (a, speed) in held.animators) if (a != null) a.speed = speed;
                if (!sameLease) continue;   // 다시 빌려 간 개체의 Collider·켜짐 상태는 새 대여가 정한다
                foreach (var (c, on) in held.colliders) if (c != null) c.enabled = on;
                foreach (var (b, on) in held.behaviours) if (b != null) b.enabled = on;
                if (held.root.activeInHierarchy)   // 풀에 들어가 꺼진 개체의 소리는 이어 틀지 않는다
                    foreach (var source in held.paused) if (source != null) source.UnPause();
            }
            roots.Clear();
        }
    }

    private sealed class HeldObject
    {
        public GameObject root;
        public WorldEntityBase owner;
        public uint lease;
        public readonly List<(Renderer renderer, bool off)> renderers = new();
        public readonly List<(Collider2D collider, bool on)> colliders = new();
        public readonly List<(Behaviour behaviour, bool on)> behaviours = new();
        public readonly List<(Animator animator, float speed)> animators = new();
        public readonly List<AudioSource> paused = new();
    }
}
