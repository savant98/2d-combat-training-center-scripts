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

    [Header("Duration")]
    [Tooltip("거리를 유지하는 시간의 최소·최대(초)입니다. 끝나면 다음 패턴(추적)을 예약합니다.")]
    public Vector2 holdDuration = new(3f, 5f);
    [Tooltip("같은 무리에서 동시에 추적할 수 있는 최대 수입니다. 가득 차면 자리가 날 때까지 거리 유지를 이어 갑니다. 0이면 제한하지 않습니다.")]
    [Min(0)] public int maxConcurrentChasers = 3;
}
