using UnityEngine;

/// <summary>
/// 던전 시작 방 도착 문 자리 표식(5×6칸, 6차 T11 — 출구 자리 표식 DungeonExitSlot과 같은 규격·설치 규칙, 루트 WaveEnvironmentTool로 Wave 격자 Tool 팔레트에 들어간다).
/// 템플릿 맵 층에 놓아 '앞 던전의 출구 문으로 들어간 Player가 여기서 걸어 나온다'만 알린다. 실행 중에는 이 표식을 생성하지 않는다 —
/// DungeonRunBuilder가 Prop 목록에서 빼고 DungeonRoomData.arrival로 옮기며, 그 방이 던전 시작 방이면 출구 문 Prefab을 도착 모드로 세운다
/// (DungeonRulesSO.arrivalGatePrefab, 없으면 이 훈련의 목표 SO ExitPrefab — 겉모습이 앞 던전 출구 문과 같다).
/// 앞 던전 출구 자리와 같은 방 좌표에 두면(정렬안) 닫힌 문을 화면에 둔 채 지형이 바뀌고, 다르면 암전 동안 이 자리로 옮긴다(DungeonCutsceneDirector).
/// 시작 전용 템플릿에만 1개를 둔다(템플릿 규칙 — 출구 자리와 같다).
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonArrivalSlot : MonoBehaviour
{
    /// <summary>표식이 차지하는 칸 수(가로·세로). 출구 문 실물(DungeonPropExitGate)·출구 자리와 같다.</summary>
    public static readonly Vector2Int Size = new Vector2Int(5, 6);
}
