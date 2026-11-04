using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Ground Patrol")]
public class GroundPatrolDataSO : EnemyAbilityDataSO
{
    [Header("Patrol Speed")]
    [Tooltip("플레이어 MoveAbility 속도에 곱하는 순찰 속도 비율입니다.")]
    [Min(0f)] public float playerSpeedRatio = 0.5f;
    [Tooltip("플레이어 속도를 읽을 수 없을 때 사용하는 순찰 속도(칸/초)입니다.")]
    [Min(0f)] public float fallbackSpeed = 3f;
    [Tooltip("정지 상태에서 순찰 속도에 도달하는 시간(초)입니다. 벽에서 방향을 바꾼 뒤에도 같은 시간으로 다시 가속합니다.")]
    [Min(0.01f)] public float accelerationTime = 0.25f;

    [Header("Direction")]
    [Tooltip("플레이어를 발견해 다른 패턴(Roller는 돌진)으로 넘어갔다가 순찰로 돌아올 때 플레이어 쪽으로 출발합니다. 끄면 마지막으로 바라본 방향으로 이어 갑니다.")]
    public bool facePlayerAfterOtherPattern = true;
}
