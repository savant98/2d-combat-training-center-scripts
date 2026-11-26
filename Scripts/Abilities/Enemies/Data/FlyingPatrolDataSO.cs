using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Flying Patrol")]
public class FlyingPatrolDataSO : FlyingAbilityDataSO
{
    [Header("Group")]
    [Tooltip("켜면 무리 전체가 좌우를 함께 바꾸고 감지 후 대형을 이루며 동시 공격 수를 제한합니다. 끄면 개체마다 좌우를 정하고 분리만 적용합니다.")]
    public bool moveAsFlock = true;

    [Header("Hold Point")]
    [Tooltip("플레이어 기준 거리 유지 지점입니다. x는 좌우 거리, y는 높이(칸)입니다.")]
    public Vector2 playerOffset = new(5f, 5f);
    [Tooltip("플레이어의 왼쪽·오른쪽을 바꾸는 간격(초)입니다. 무리로 움직이지 않으면 개체마다 이 간격의 ±25% 안에서 따로 바꿉니다.")]
    [Min(0.1f)] public float sideSwitchInterval = 3.5f;
    [Tooltip("천장·벽과 띄우는 거리(칸)입니다. 유지 지점이 막히면 이만큼 방 안쪽으로 당깁니다.")]
    [Min(0f)] public float wallMargin = 1.2f;
    [Tooltip("정한 쪽의 벽까지 남은 거리가 이보다 짧으면 이번에는 반대쪽에서 유지합니다(칸).")]
    [Min(0f)] public float minSideSpace = 2.5f;

    [Header("Formation")]
    [Tooltip("무리 중심 구성원을 둘러싼 고리에서 칸 사이의 간격(칸)입니다.")]
    [Min(0.1f)] public float formationSpacing = 1.6f;
    [Tooltip("고리의 세로 비율입니다. 1보다 작으면 옆으로 넓은 타원이 됩니다.")]
    [Range(0.3f, 1f)] public float formationHeightRatio = 0.85f;

    [Header("Path-first Formation")]
    [Tooltip("켜면 무리의 추종 구성원은 자기 칸까지 몸이 지나갈 직선이 열려 있고 기준 구성원이 직선으로 날고 있을 때만 대형(기준 구성원 + 고리 칸)을 따릅니다. 아니면 유지 지점 기준의 가상 칸으로 길찾기 경로를 따라가고, 추종 구성원도 사격선이 막히면 공격 위치를 다시 고릅니다. 끄면 5차 대형(늘 기준 구성원 + 고리 칸)입니다.")]
    public bool formationPathFirst = true;
    [Tooltip("대형이 다시 가능해진 뒤 이 시간(초) 동안 계속 가능해야 대형으로 돌아갑니다(경계에서 대형·길찾기가 번갈아 바뀌지 않게).")]
    [Min(0f)] public float formationRejoinDelay = 0.5f;
    [Tooltip("추종 구성원과 자기 대형 칸 사이가 고리 간격 × 이 값(칸)보다 멀면 대형 대신 길찾기로 모입니다.")]
    [Min(0.5f)] public float formationRegroupDistance = 2.5f;
    [Tooltip("길찾기(경로·국소 우회) 중에는 같은 무리끼리 밀어내는 분리 힘에 이 배율을 곱합니다(좁은 통로에서 서로를 벽으로 밀지 않게). 1이면 그대로입니다.")]
    [Range(0f, 1f)] public float pathSeparationScale = 0.5f;
    [Tooltip("기준 구성원이 직선이 아닌 회피(경로·국소)로 이 시간(초)보다 오래 날거나 도달 불가가 나면 막힌 것으로 보고, 막히지 않은 구성원 가운데 유지 지점에 가까운 것으로 기준 구성원을 바꿉니다. 0이면 바꾸지 않습니다.")]
    [Min(0f)] public float leaderBlockedDemote = 1.5f;

    [Header("Duration")]
    [Tooltip("거리를 유지하는 시간의 최소·최대(초)입니다. 끝나면 다음 패턴(추적)을 예약합니다.")]
    public Vector2 holdDuration = new(3f, 5f);
    [Tooltip("같은 무리에서 동시에 추적할 수 있는 최대 수입니다. 가득 차면 자리가 날 때까지 거리 유지를 이어 갑니다. 0이면 제한하지 않습니다.")]
    [Min(0)] public int maxConcurrentChasers = 3;

    [Header("Check Interval")]
    [Tooltip("유지 시간이 끝난 뒤 다음 패턴이 사격(FlyingShootAbility — Drone)일 때 그 시작 조건(사거리·사격선)을 다시 확인하는 간격(초)입니다. 끝난 첫 프레임에는 바로 한 번 확인합니다. 0이면 매 프레임 확인합니다. Aegis 순찰은 아래 candidateCheckInterval을 씁니다.")]
    [Min(0f)] public float attackCheckInterval = 0.1f;
    [Tooltip("유지 시간이 끝난 뒤 공격 후보(시작 조건)를 다시 평가하는 간격(초)입니다. 끝난 첫 프레임에는 바로 한 번 평가합니다. 0이면 매 프레임 평가합니다. 후보가 여럿이고 확인이 비싼 Aegis 순찰(AegisPatrolAbility)만 사용합니다.")]
    [Min(0f)] public float candidateCheckInterval = 0.2f;
}
