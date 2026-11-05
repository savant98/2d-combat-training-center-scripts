using UnityEngine;

// 비행 패턴(FlyingAbility)의 공통 이동값이다.
// 목표점까지는 가까워질수록 감속하고(Arrival), 개체별 난수 위치를 Lerp로 따라가는 흔들림과 같은 무리끼리의 분리를 더한다.
public abstract class FlyingAbilityDataSO : EnemyAbilityDataSO
{
    [Header("Flight Speed")]
    [Tooltip("플레이어 MoveAbility 속도에 곱하는 최고 비행 속도 비율입니다.")]
    [Min(0f)] public float playerSpeedRatio = 1f;
    [Tooltip("플레이어 속도를 읽을 수 없을 때 쓰는 최고 비행 속도(칸/초)입니다.")]
    [Min(0f)] public float fallbackSpeed = 6f;
    [Tooltip("속도를 바꿀 때의 가속도(칸/초²)입니다. 넉백 뒤 원래 경로로 돌아오는 빠르기도 정합니다.")]
    [Min(0.01f)] public float acceleration = 18f;
    [Tooltip("목표점에서 이 거리(칸) 안으로 들어오면 거리에 비례해 감속합니다. 지나쳤다 돌아오는 진동을 막습니다.")]
    [Min(0.01f)] public float slowingDistance = 2f;

    [Header("Fly Wander")]
    [Tooltip("목표점 주변에서 난수로 고르는 흔들림 범위의 반지름(x, y 칸)입니다.")]
    public Vector2 wanderRadius = new(0.7f, 0.5f);
    [Tooltip("다음 난수 흔들림 위치를 고르는 간격의 최소·최대(초)입니다.")]
    public Vector2 wanderInterval = new(0.2f, 0.5f);
    [Tooltip("현재 흔들림 위치가 새 난수 위치를 따라가는 Lerp 속도(1/초)입니다.")]
    [Min(0f)] public float wanderLerpSpeed = 7f;

    [Header("Separation")]
    [Tooltip("같은 무리 구성원의 중심이 이 거리(칸)보다 가까우면 서로 밀어냅니다.")]
    [Min(0f)] public float separationDistance = 1.4f;
    [Tooltip("중심 거리가 separationDistance의 절반일 때 밀어내는 속도(칸/초)입니다. 더 가까우면 최대 4배까지 커지고, separationDistance에서 0이 됩니다.")]
    [Min(0f)] public float separationStrength = 6f;
    [Tooltip("밀어내는 방향을 반시계로 돌리는 각도(도)입니다. 정면으로 마주친 구성원이 옆으로 비켜 가게 하지만, 같은 지점에 모인 구성원은 서로 맴돕니다(Swarmer 25). 0이면 서로 밀어내기만 합니다(Drone).")]
    [Range(0f, 45f)] public float separationSidestep = 25f;
}
