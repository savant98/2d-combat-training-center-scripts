using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 레버 목표(W1: 레버 3개를 모두 당기면 클리어). 전투 방(시작·보스·휴식 제외)의 목표 슬롯에 방당 1개씩,
/// 시작에서 멀고 서로 흩어지게 놓는다(생성 규칙 v3-12, 배치 알고리즘은 같은 폴더의 정적 DungeonObjectiveRoomPicker).
/// </summary>
/// <remarks>
/// 진행 번호(index)는 시작에서 가까운 레버부터 0, 1, 2…다(같은 거리면 방 id 순서). 레버 Prefab이 비어 있으면 배치하지 않는다(실물 없는 목표로
/// 던전이 끝나지 않게 — 그 시도는 실패, 32번 모두 실패하면 생성 실패로 알린다).
/// </remarks>
[CreateAssetMenu(menuName = "Training/Dungeon Objective/Lever")]
public class LeverObjectiveSO : DungeonObjectiveSO
{
    [Tooltip("목표 슬롯에 세울 레버 실물 Prefab(DungeonPropObjectiveLever).")]
    public GameObject leverPrefab;
    [Tooltip("놓을 레버 수.")]
    [Min(1)] public int count = 3;
    [Tooltip("레버 방이 시작 방에서 떨어져야 하는 최소 BFS 거리.")]
    [Min(0)] public int minStartDistance = 2;
    [Tooltip("최소 거리 = max(minStartDistance, round(이 비율 × 최대 거리)).")]
    [Range(0f, 1f)] public float startDistanceRatio = .34f;
    [Tooltip("레버 방끼리의 최소 BFS 거리(모자라면 3 → 2 → 1 순으로 완화).")]
    [Min(1)] public int minSpacing = 3;
    [Tooltip("휴식 방(Rest 꼬리표)에도 놓을지.")]
    public bool allowRestRooms = false;
    [Tooltip("덧붙인 방(고리·추가 노드)에도 놓을지.")]
    public bool allowAppendedNodes = true;

    public override bool TryPlace(DungeonObjectivePlanContext context, System.Random rng, List<DungeonObjectiveAssignment> result, StringBuilder notes)
    {
        result.Clear();
        if (leverPrefab == null)
        {
            notes?.AppendLine($"[목표] {name}: 레버 Prefab(leverPrefab)이 비어 있어 배치하지 않았습니다.");
            return false;
        }
        var picks = new List<DungeonObjectiveRoomPicker.Pick>();
        var rules = new DungeonObjectiveRoomPicker.Rules
        {
            count = Mathf.Max(1, count),
            minStartDistance = minStartDistance,
            startDistanceRatio = startDistanceRatio,
            minSpacing = minSpacing,
            allowRestRooms = allowRestRooms,
            allowAppendedNodes = allowAppendedNodes,
        };
        if (!DungeonObjectiveRoomPicker.TryPick(context, rng, rules, picks, notes, string.IsNullOrEmpty(title) ? name : title)) return false;
        for (int i = 0; i < picks.Count; i++)
            result.Add(new DungeonObjectiveAssignment { room = picks[i].room, slot = picks[i].slot, index = i });
        return true;
    }

    public override GameObject PrefabFor(int index) => leverPrefab;
}
