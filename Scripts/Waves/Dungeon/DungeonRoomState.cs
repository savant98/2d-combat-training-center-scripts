using System.Collections.Generic;

/// <summary>
/// 던전 방 하나의 진행 상태(DungeonManager가 던전마다 새로 만든다). 미니맵에는 DungeonMapRoom으로 옮겨 보인다.
/// </summary>
public sealed class DungeonRoomState
{
    /// <summary>인접한 방에 들어가 존재를 알게 되었는지.</summary>
    public bool discovered;
    /// <summary>들어가 본 적이 있는지.</summary>
    public bool visited;
    /// <summary>이 방에 들어온 횟수(4차, 들어올 때마다 1 증가 — 첫 방문 뒤 1). 재입장 순찰 구성의 시드 '방문 순번'에 쓴다.</summary>
    public int visits;
    /// <summary>클리어했는지(적 구성이 없는 방은 들어가면 바로 클리어).</summary>
    public bool cleared;
    /// <summary>난이도에 센 몇 번째 첫 클리어인지(4차, 0부터 — DungeonThreatTracker.RecordFirstClear의 반환값). 세지 않은 방(미클리어·시작·적 없는 방)은 -1.</summary>
    public int clearOrder = -1;
    /// <summary>적과 싸우는 동안 출입구가 잠겨 있는지.</summary>
    public bool locked;
    /// <summary>
    /// 7차(DESIGN_V7 V5): 적을 모두 처치했지만 이 방에 아직 당기지 않은 레버가 남아 '레버 대기' 중인지. 이때 cleared는 false, locked는 true(문 잠김 유지)다.
    /// 레버를 모두 당기고 방 클리어 처리(DungeonRoomClearGate → 기존 클리어 흐름)를 시작하기 직전에 false가 된다. 재도전·던전 종료는 방 상태를 새로 만든다.
    /// </summary>
    public bool objectiveHold;
    /// <summary>부서진 Prop(방 데이터 props 번호). 다시 들어와도 세우지 않는다.</summary>
    public readonly HashSet<int> destroyedProps = new();
    /// <summary>적을 내보낸 생성 앵커 Prop(방 데이터 props 번호). 다시 들어오면 '내보낸 뒤' 모습으로 세운다.</summary>
    public readonly HashSet<int> spentAnchors = new();
    /// <summary>완료한 목표 실물(레버 등)의 슬롯 번호(방 데이터 objectives의 slot). 다시 들어오면 '완료' 모습으로 세운다.</summary>
    public readonly HashSet<int> completedObjectives = new();
    /// <summary>이 방의 텔레포트 시설로 짝 방에 간 횟수(기록용).</summary>
    public int teleportUses;
    /// <summary>이 방에 들어와 텔레포트 시설로 도착한 횟수(기록용).</summary>
    public int teleportArrivals;
    /// <summary>이 방에서 쓴 던전당 1회 토큰(수리대 등, 기록용). 실제 1회 제한은 DungeonManager가 던전 단위로 센다.</summary>
    public readonly HashSet<string> usedTokens = new();
}
