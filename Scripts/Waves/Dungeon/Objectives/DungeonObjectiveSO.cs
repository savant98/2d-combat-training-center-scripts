using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>목표를 모두 채운 뒤 던전을 끝내는 방식(4차 R11).</summary>
public enum DungeonObjectiveFinish
{
    /// <summary>마지막 목표를 채운 순간 던전 클리어(3차까지의 동작).</summary>
    Immediate = 0,
    /// <summary>마지막 목표를 채우면 시작 방의 출구 문(ExitPrefab)이 해금되고, 그 문에 들어가야 던전 클리어.</summary>
    ExitInStartRoom = 1,
}

/// <summary>
/// 던전 목표 모듈(추상). 템플릿에는 자리 표식(DungeonObjectiveSlot)만 두고, 어느 방·슬롯에 무엇을 세울지는 이 SO가 정한다.
/// 다른 훈련은 SO만 바꿔 끼운다(예: 레버 개수·다른 실물 Prefab). DungeonWaveSettings.objective로 연결하고 클리어 조건을 Objective로 둔다.
/// </summary>
public abstract class DungeonObjectiveSO : ScriptableObject
{
    /// <summary>목표 제목(목표 패널·현장 단말기).</summary>
    public string title = "제어 레버 작동";
    /// <summary>짧은 표기(목표 패널 'LEVERS 1/3').</summary>
    public string shortLabel = "LEVERS";
    /// <summary>목표 색(앰버 #FFB020 계열).</summary>
    public Color color = new Color(1f, .69f, .13f);

    /// <summary>목표를 모두 채운 뒤 끝내는 방식(4차). 기본 Immediate = 기존 동작.</summary>
    [Header("끝내는 방식(4차)")]
    [Tooltip("Immediate = 마지막 목표를 채우면 바로 클리어, ExitInStartRoom = 시작 방 출구 문에 들어가야 클리어합니다.")]
    public DungeonObjectiveFinish finish = DungeonObjectiveFinish.Immediate;
    /// <summary>시작 방 출구 문 실물 Prefab(IDungeonExitPoint를 가진 Prop, 예: DungeonPropExitGate). finish = ExitInStartRoom일 때 시작 방의 출구 자리(DungeonExitSlot)에 세운다.</summary>
    [Tooltip("시작 방 출구 자리(DungeonExitSlot)에 세울 출구 문 Prefab입니다.")]
    public GameObject exitPrefab;
    /// <summary>출구 해금 뒤 목표 제목(목표 패널·현장 단말기).</summary>
    public string exitTitle = "출구로 이동";
    /// <summary>출구 짧은 표기(목표 패널 'EXIT OPEN' 등).</summary>
    public string exitShortLabel = "EXIT";

    /// <summary>
    /// 7차(DESIGN_V7 V5): 이 목표가 방 클리어에 필요한지. 켜면 목표(레버)가 남은 방은 적을 모두 처치해도 출입구가 잠긴 채 '레버 대기'(상태 띠 PULL LEVER)가 되고,
    /// 그 방 목표를 채워야 방 클리어(ROOM CLEAR·난이도 첫 클리어·시설 전원·문 열림)된다. 끄면 6차까지처럼 적 전멸 = 방 클리어. 기존 에셋에 없는 필드라 처음 읽을 때 true다.
    /// </summary>
    [Header("방 클리어(7차)")]
    [Tooltip("켜면 이 목표(레버)가 남은 방은 적을 모두 처치해도 문이 잠긴 채 'PULL LEVER' 대기가 되고, 목표를 채워야 방 클리어(문 열림)됩니다.")]
    public bool gatesRoomClear = true;

    /// <summary>출구 문 Prefab(기본 exitPrefab). finish가 Immediate여도 값은 그대로 돌려준다 — 쓸지는 실행 쪽이 finish로 정한다.</summary>
    public virtual GameObject ExitPrefab => exitPrefab;

    /// <summary>시작 방 출구 방식이고 출구 Prefab이 연결됐는지.</summary>
    public bool UsesStartRoomExit => finish == DungeonObjectiveFinish.ExitInStartRoom && ExitPrefab != null;

    /// <summary>
    /// 생성 단계(순수 C#, 텔레포트 배치 뒤 별도 난수 seed ^ ObjectiveSeedSalt)에서 목표 방·슬롯을 고른다.
    /// 고르지 못하면 false(그 생성 시도 실패 → 생성기가 시드 +1로 다시 시도). 고른 결과를 result에 채우고 사유는 notes에 적는다.
    /// </summary>
    public abstract bool TryPlace(DungeonObjectivePlanContext context, System.Random rng, List<DungeonObjectiveAssignment> result, StringBuilder notes);

    /// <summary>진행 번호 index(0부터)의 목표에 세울 실물 Prefab(IDungeonObjectivePoint를 가진 Prop).</summary>
    public abstract GameObject PrefabFor(int index);

    /// <summary>배치한 수(placed) 중 클리어에 필요한 수. 기본은 전부.</summary>
    public virtual int RequiredCount(int placed) => placed;
}

/// <summary>목표 배치에 주는 생성 단계 정보(DungeonGenerator가 채운다).</summary>
public sealed class DungeonObjectivePlanContext
{
    /// <summary>방 배치·연결·텔레포트까지 끝난 던전.</summary>
    public DungeonLayout layout;
    /// <summary>방 id별 시작 방에서의 BFS 거리(연결 출입구 기준, 닿지 않으면 -1).</summary>
    public int[] distanceFromStart;
    /// <summary>방의 템플릿 맵 층에 놓인 목표 슬롯 수.</summary>
    public Func<DungeonRoomNode, int> slotCount;
    /// <summary>텔레포트 짝짓기로 덧붙인 방의 첫 id(이 값 이상 = 덧붙인 끝점 방). 덧붙인 방이 없으면 방 수와 같다(core3 추가 — 기존 계약 불변).</summary>
    public int appendedFrom = int.MaxValue;
}

/// <summary>목표 하나의 배정(방 id·템플릿 슬롯 순번·진행 번호).</summary>
[Serializable]
public struct DungeonObjectiveAssignment
{
    public int room;
    public int slot;
    public int index;
}
