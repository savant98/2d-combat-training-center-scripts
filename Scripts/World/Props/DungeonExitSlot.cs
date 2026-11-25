using UnityEngine;

/// <summary>
/// 던전 시작 방 출구 자리 표식(5×6칸, (x, y) = 왼쪽 아래 칸, 루트 WaveEnvironmentTool로 Wave 격자 Tool 팔레트에 들어간다 — 4차 R11).
/// 템플릿 맵 층에 놓아 '여기에 출구 문을 세울 수 있음'만 알린다. DungeonObjectiveSlot과 같은 방식으로 실행 중에는 이 표식을 생성하지 않는다 —
/// DungeonRunBuilder가 Prop 목록에서 빼고 DungeonRoomData.exit로 옮기며, 목표 SO가 시작 방 출구 방식(finish = ExitInStartRoom)이면
/// 그 자리에 목표 SO의 출구 문(DungeonObjectiveSO.ExitPrefab)을 세운다. 시작 전용 템플릿만 1개를 둔다(템플릿 규칙 v4-8).
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonExitSlot : MonoBehaviour
{
    /// <summary>표식이 차지하는 칸 수(가로·세로). 출구 문 실물(DungeonPropExitGate)과 같다.</summary>
    public static readonly Vector2Int Size = new Vector2Int(5, 6);
}
