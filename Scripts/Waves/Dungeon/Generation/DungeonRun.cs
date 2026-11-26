using System.Collections.Generic;
using UnityEngine;

/// <summary>방 하나의 출입구 배치(템플릿 좌표 = 방 로컬 좌표, 모든 방이 같은 자리에 선다).</summary>
public sealed class DungeonDoorPlacement
{
    public int socket;
    public DungeonDoorSocket info;
    public GameObject prefab;
    /// <summary>건너편 방 id(-1이면 연결 안 됨 → 출입구를 세우지 않고 벽을 남긴다).</summary>
    public int toRoom = -1;
    public int toSocket = -1;
    public bool Connected => toRoom >= 0;
}

/// <summary>방 하나의 텔레포트 시설 배치(맵 층 항목에서 읽음). 짝이 없으면 꺼진 받침으로 세운다.</summary>
public sealed class DungeonTeleportPlacement
{
    /// <summary>템플릿 맵 층의 텔레포트 항목 순번(0부터).</summary>
    public int pad;
    public WaveSpawnInfo info;
    public int toRoom = -1;
    public int toPad = -1;
    public bool Linked => toRoom >= 0;
}

/// <summary>
/// 방 하나의 목표 슬롯 배치(템플릿 맵 층의 DungeonObjectiveSlot 표식에서 읽음). 표식 자체는 세우지 않고,
/// 목표가 배정된 슬롯(index ≥ 0)에만 목표 SO의 실물(PrefabFor(index))을 세운다.
/// </summary>
public sealed class DungeonObjectivePlacement
{
    /// <summary>템플릿 맵 층의 목표 슬롯 순번(0부터). DungeonRoomState.completedObjectives의 키.</summary>
    public int slot;
    /// <summary>슬롯 표식의 맵 층 항목(위치 등). 실물을 세울 때 prefab만 바꿔 쓴다.</summary>
    public WaveSpawnInfo info;
    /// <summary>목표 진행 번호(0부터). -1이면 배정되지 않은 빈 슬롯(아무것도 세우지 않는다).</summary>
    public int index = -1;
    public bool Assigned => index >= 0;
}

/// <summary>실행 중 방 하나를 세우는 데 필요한 것(입장 때 미리 계산).</summary>
public sealed class DungeonRoomData
{
    public DungeonRoomNode node;
    /// <summary>이 방에서 켤 지형 칸(템플릿 맵 층 − 연결된 출입구의 파는 칸).</summary>
    public WaveMapCell[] cells = System.Array.Empty<WaveMapCell>();
    /// <summary>실내 외접 범위(방 로컬). 셀 공간을 찾지 못할 때의 대체 범위.</summary>
    public Rect interior;
    /// <summary>시작 방이면 Wave 시작 좌표(템플릿 맵 층 시작 좌표).</summary>
    public Vector2 entry;
    public DungeonDoorPlacement[] doors = System.Array.Empty<DungeonDoorPlacement>();
    /// <summary>맵 층의 적이 아닌 항목(Prop·고정 함정). 출입구는 빠진다. 방에 들어올 때 세운다.</summary>
    public WaveSpawnInfo[] props = System.Array.Empty<WaveSpawnInfo>();
    /// <summary>고른 적 구성 항목(적 + 그 구성의 함정). 클리어 안 한 방에 들어오면 시각 순서대로 생성한다.</summary>
    public WaveSpawnInfo[] encounter = System.Array.Empty<WaveSpawnInfo>();
    /// <summary>텔레포트 시설(맵 층에 놓인 것). 짝지은 것만 Linked.</summary>
    public DungeonTeleportPlacement[] teleports = System.Array.Empty<DungeonTeleportPlacement>();
    /// <summary>목표 슬롯(맵 층에 놓인 표식 전부). 배정된 것만 Assigned. 표식은 props에서 빠진다.</summary>
    public DungeonObjectivePlacement[] objectives = System.Array.Empty<DungeonObjectivePlacement>();
    /// <summary>
    /// 출구 자리(4차 R11): 템플릿 맵 층의 DungeonExitSlot 표식 항목(템플릿 좌표 = 방 로컬, 월드 아님). 표식은 props에서 빠지고,
    /// 목표 SO가 시작 방 출구 방식이면 이 자리에 ExitPrefab을 세운다(prefab만 바꿔 쓴다). 없으면 null.
    /// </summary>
    public WaveSpawnInfo exit;
    /// <summary>출구 자리가 있는지.</summary>
    public bool HasExit => exit != null;
    /// <summary>
    /// 도착 문 자리(6차 T11): 템플릿 맵 층의 DungeonArrivalSlot 표식 항목(템플릿 좌표 = 방 로컬). 표식은 props에서 빠지고,
    /// 이 방이 던전 시작 방이면 출구 문 Prefab을 도착 모드로 세운다(앞 던전 출구에 들어간 Player가 여기서 걸어 나온다). 없으면 null.
    /// </summary>
    public WaveSpawnInfo arrival;
    /// <summary>도착 문 자리가 있는지.</summary>
    public bool HasArrival => arrival != null;
    /// <summary>
    /// 적 생성 구역(4차): 템플릿 맵 층의 DungeonSpawnZone 표식 항목(템플릿 좌표, 배치 설정 spawnZoneKind·Width·Height·Weight 포함).
    /// 표식은 props에서 빠지고 실행 때 세우지 않는다. 동적 적 구성의 후보 지점 편향(DungeonSpawnPointMap)에 쓴다.
    /// </summary>
    public List<WaveSpawnInfo> spawnZones = new List<WaveSpawnInfo>();
    private bool? fight;
    /// <summary>
    /// 이 방이 싸우는 방인지(4차 명시 값). 동적 구성(Dynamic) 방은 encounter가 비어 있어도 true여야 한다 — 생성 단계(DungeonRunBuilder)가 채운다.
    /// 채우지 않았으면 기존 규칙(encounter가 하나 이상)으로 본다.
    /// </summary>
    public bool HasFight
    {
        get => fight ?? (encounter != null && encounter.Length > 0);
        set => fight = value;
    }
    /// <summary>이 방의 배경(Auto를 푼 값)과 정적 벽면 키트.</summary>
    public DungeonBackdropMode backdrop = DungeonBackdropMode.Parallax;
    public DungeonWallKit wallKit = DungeonWallKit.None;
    public DungeonRoomSize SizeClass => node != null && node.template != null ? node.template.sizeClass : DungeonRoomSize.Large;
    public int Id => node != null ? node.id : -1;
    public DungeonRoomKind Kind => node != null ? node.kind : DungeonRoomKind.Combat;
}

/// <summary>던전 하나(= Wave 하나)의 실행 데이터.</summary>
public sealed class DungeonFloor
{
    public int waveIndex;
    public DungeonWaveSettings settings;
    public DungeonLayout layout;
    public DungeonRoomData[] rooms = System.Array.Empty<DungeonRoomData>();
    public DungeonPassageEffectSO passage;
    /// <summary>이 던전의 목표 모듈(설정의 objective, 클리어 조건이 Objective일 때만). 없으면 null.</summary>
    public DungeonObjectiveSO objective;
    public DungeonRoomData Room(int id) => id >= 0 && id < rooms.Length ? rooms[id] : null;
    /// <summary>
    /// 동적 적 구성(4차 encounterMode = Dynamic, 규칙 연결됨)을 쓰는 던전인지. 동적 전투 방의 room.encounter는 비어 있고 HasFight만 true다
    /// (보스 방은 손 배치 구성 그대로).
    /// </summary>
    public bool Dynamic => settings != null && settings.UsesDynamicEncounters;
    /// <summary>동적 구성 규칙(Dynamic이 아니면 null).</summary>
    public DungeonEncounterRulesSO EncounterRules => Dynamic ? settings.encounterRules : null;
}

/// <summary>
/// 훈련 한 판의 던전 실행 데이터. <see cref="DungeonRunBuilder.Build"/>가 원본 WaveData의 실행 사본(<see cref="Data"/>)과 함께 만든다.
/// </summary>
public sealed class DungeonRun
{
    /// <summary>실행 사본(원본 에셋을 Instantiate한 것). WaveManager·WaveCellTerrain은 이 사본으로 진행한다.</summary>
    public WaveDataSO Data;
    public WaveDataSO Source;
    public int Seed;
    /// <summary>Wave 번호별 던전(던전이 아닌 Wave는 null). 길이 = Data.waves.Length.</summary>
    public DungeonFloor[] floors = System.Array.Empty<DungeonFloor>();

    public DungeonFloor FloorFor(int waveIndex) => waveIndex >= 0 && waveIndex < floors.Length ? floors[waveIndex] : null;

    /// <summary>예열 힌트 한 Prefab당 상한(풀 예열이 지나치게 커지지 않게).</summary>
    public const int PrewarmHintCap = 12;
    private readonly Dictionary<GameObject, int> prewarmHints = new Dictionary<GameObject, int>();
    /// <summary>
    /// 예열 힌트(4차): 동적 적 구성처럼 입장 때 항목을 알 수 없는 것의 Prefab별 '한 방에서 최악의 수'(상한 PrewarmHintCap).
    /// 생성 단계(DungeonRunBuilder)가 HintPrewarm으로 채우고, 입장 로딩의 풀 예열(TrainingRoomHost)이 room.encounter 수와 함께 큰 쪽을 쓴다.
    /// </summary>
    public IReadOnlyDictionary<GameObject, int> PrewarmHints => prewarmHints;

    /// <summary>prefab을 한 방에 최대 count개 꺼낼 수 있다고 알린다. 이미 더 큰 값이 있으면 그대로 둔다.</summary>
    public void HintPrewarm(GameObject prefab, int count)
    {
        if (prefab == null || count <= 0) return;
        count = Mathf.Min(count, PrewarmHintCap);
        if (!prewarmHints.TryGetValue(prefab, out int known) || known < count) prewarmHints[prefab] = count;
    }

    /// <summary>모든 던전의 모든 방 셀(지형 풀 등록용, 중복 포함).</summary>
    public IEnumerable<WaveMapCell> AllCells()
    {
        foreach (var floor in floors)
        {
            if (floor == null) continue;
            foreach (var room in floor.rooms)
                foreach (var cell in room.cells) yield return cell;
        }
    }

    /// <summary>모든 던전의 모든 방 항목(풀 예열용).</summary>
    public IEnumerable<WaveSpawnInfo> AllSpawns()
    {
        foreach (var floor in floors)
        {
            if (floor == null) continue;
            foreach (var room in floor.rooms)
            {
                foreach (var info in room.props) yield return info;
                foreach (var info in room.encounter) yield return info;
                foreach (var door in room.doors) if (door.Connected && door.prefab != null) yield return new WaveSpawnInfo { prefab = door.prefab, isRequiredToClear = false };
                // 텔레포트 시설도 풀에 미리 만든다(방을 세울 때 처음 생성되는 끊김 방지).
                foreach (var pad in room.teleports) if (pad?.info?.prefab != null) yield return new WaveSpawnInfo { prefab = pad.info.prefab, isRequiredToClear = false };
                // 배정된 목표 슬롯의 실물(레버 등)도 미리 만든다.
                if (floor.objective != null)
                    foreach (var slot in room.objectives)
                    {
                        if (slot == null || !slot.Assigned) continue;
                        var prefab = floor.objective.PrefabFor(slot.index);
                        if (prefab != null) yield return new WaveSpawnInfo { prefab = prefab, isRequiredToClear = false };
                    }
                // 시작 방 출구 문(4차)도 미리 만든다(출구는 시작 방에만 세운다).
                if (floor.objective != null && floor.objective.UsesStartRoomExit && room.HasExit && floor.layout != null && room.Id == floor.layout.startRoom)
                    yield return new WaveSpawnInfo { prefab = floor.objective.ExitPrefab, isRequiredToClear = false };
            }
        }
    }
}
