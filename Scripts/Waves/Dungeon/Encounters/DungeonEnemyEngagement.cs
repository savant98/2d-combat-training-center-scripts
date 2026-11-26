using UnityEngine;

/// <summary>
/// 던전 전투 방에서 나온 비행 무리 적(EnemyFlockMember가 있는 Drone·Swarmer·Bomber·Sniper)의 무리·감지 설정(6차 T1).
/// DungeonManager가 <see cref="DungeonEncounter.EnemySpawned"/>에서 생성 연출(PlayAnchor)보다 먼저 부른다(잠긴 방 RoomFight·재입장 순찰 PatrolFight 공통).
/// - 무리 키 = <c>Dungeon:{층 순번}:{방 id}:{종류 flockId}</c>: 방·종류 단위로 나눠 다른 방·다른 종류와 대형·좌우·리더를 섞지 않는다.
///   방문 번호는 넣지 않는다(한 번에 한 방의 적만 살아 있고, 구성원이 모두 빠진 무리는 EnemyFlock.Leave가 사전에서 지운다).
/// - 잠긴 방(<see cref="DungeonEncounterRulesSO.engageOnLock"/> 켬): 감지 거리 0(제한 없음) — 등장 연출이 끝난 첫 순찰 프레임에 교전한다.
///   끄면 Prefab 감지 거리(생성 자리 기준)를 그대로 쓴다.
/// - 재입장 순찰: Prefab 감지 거리 그대로 + <see cref="DungeonEncounterRulesSO.patrolShareDetection"/>이면 한 마리가 감지할 때 같은 무리 전체가 교전한다.
/// - 규칙 SO가 없는 손 배치 던전(W2 보스 방)은 두 설정 모두 켠 것과 같다.
/// 물리 질의·위치 변경은 없다. EnemyFlockMember가 없는 지상 적은 그대로 둔다(지상 감지 규칙은 이번 범위 밖).
/// </summary>
public static class DungeonEnemyEngagement
{
    /// <summary>던전 무리 키의 접두어(검사용).</summary>
    public const string KeyPrefix = "Dungeon:";

    /// <summary>방·종류 단위 무리 키.</summary>
    public static string FlockKey(int floorSerial, int roomId, string flockId) => $"{KeyPrefix}{floorSerial}:{roomId}:{flockId}";

    /// <summary>방 하나의 무리 키 접두어(그 방의 모든 종류).</summary>
    public static string RoomKeyPrefix(int floorSerial, int roomId) => $"{KeyPrefix}{floorSerial}:{roomId}:";

    /// <summary>잠긴 전투 방에서 즉시 교전하는지(규칙 SO가 없으면 켬).</summary>
    public static bool EngageOnLock(DungeonEncounterRulesSO rules) => rules == null || rules.engageOnLock;

    /// <summary>재입장 순찰에서 무리 감지를 공유하는지(규칙 SO가 없으면 켬).</summary>
    public static bool ShareOnPatrol(DungeonEncounterRulesSO rules) => rules == null || rules.patrolShareDetection;

    /// <summary>
    /// 막 생성된 적 하나를 설정한다(생성 위치 배치가 끝난 뒤, 등장 연출 중). 비행 무리 적이 아니면 false.
    /// position은 생성 자리(감지 기준점 — 순찰은 이 자리에서 Prefab 감지 거리 안이면 감지).
    /// </summary>
    public static bool Configure(EnemyController enemy, Vector3 position, int floorSerial, int roomId, bool patrol, DungeonEncounterRulesSO rules)
    {
        if (enemy == null || !enemy.TryGetComponent(out EnemyFlockMember member)) return false;
        bool engage = !patrol && EngageOnLock(rules);
        float radius = engage ? 0f : member.BaseDetectionDistance;
        bool share = patrol && ShareOnPatrol(rules);
        member.ConfigureEncounterGroup(FlockKey(floorSerial, roomId, member.FlockId), position, radius, share);
        return true;
    }
}
