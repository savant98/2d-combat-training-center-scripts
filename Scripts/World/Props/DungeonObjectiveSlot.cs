using UnityEngine;

/// <summary>
/// 던전 목표 자리 표식(4×6칸 뒤 벽, (x, y) = 왼쪽 아래 칸, 루트 WaveEnvironmentTool로 Wave 격자 Tool 팔레트에 들어간다 — 4차 R10).
/// 템플릿 맵 층에 놓아 '여기에 목표를 세울 수 있음'만 알린다. 앞 바닥(아랫줄 바로 밑 칸)에 서서 쓰는 대형 배전반 레버(DungeonPropObjectiveLever)와 같은 크기다.
/// 실행 중에는 이 표식을 생성하지 않는다 — DungeonRunBuilder가 Prop 목록에서 빼고 DungeonRoomData.objectives로 옮기며,
/// 목표가 배정된 슬롯에만 목표 SO의 실물(DungeonObjectiveSO.PrefabFor)을 세운다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonObjectiveSlot : MonoBehaviour
{
    /// <summary>표식이 차지하는 칸 수(가로·세로, 4차). 목표 레버 실물(DungeonPropObjectiveLever)과 같다.</summary>
    public static readonly Vector2Int Size = new Vector2Int(4, 6);
}
