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

    [Header("Obstacle Avoidance")]
    [Tooltip("벽·칸막이·천장을 돌아가는 회피 설정입니다(EnemyFlightNavigator). 목표까지 직선이 열려 있으면 기존 조향과 같습니다.")]
    public FlightAvoidanceSettings avoidance = FlightAvoidanceSettings.Default;
}

// 비행 적 장애물 회피(EnemyFlightNavigator) 설정이다. 각 비행 패턴 SO에 들어 있어 Enemy Tool에서 '고유 설정 SO'를 선택해 Inspector로 고치고 '설정 저장'으로 저장한다.
// 이 필드가 없던 기존 에셋은 Default 값으로 읽힌다.
[System.Serializable]
public struct FlightAvoidanceSettings
{
    [Tooltip("장애물 회피를 사용합니다. 끄면 목표점으로 곧게 조향하는 기존 동작입니다.")]
    public bool enabled;
    [Tooltip("직선 확인·국소 회피를 다시 계획하는 간격(초)입니다. 막히면 기다리지 않고 바로 다시 계획합니다.")]
    [Min(0.05f)] public float replanInterval;
    [Tooltip("격자 경로를 다시 찾는 간격(초)입니다. 막히거나 목표가 크게 옮겨지면 바로 다시 찾습니다.")]
    [Min(0.1f)] public float pathInterval;
    [Tooltip("방 격자에서 경로를 찾아 돌아갑니다(U자·굽이 통로). 끄면 국소 회피만 씁니다.")]
    public bool usePath;
    [Tooltip("국소 회피에서 몸으로 쓸어 보는 방향 수입니다.")]
    [Range(8, 32)] public int sampleCount;
    [Tooltip("국소 회피에서 방향마다 쓸어 보는 거리(칸)입니다.")]
    [Min(0.5f)] public float lookAhead;
    [Tooltip("몸 외곽과 지형 사이에 남기는 여유(칸)입니다.")]
    [Min(0f)] public float clearance;
    [Tooltip("이 시간(초) 동안 stuckDistance보다 적게 움직였고 목표가 1.5칸보다 멀면 막힌 것으로 봅니다.")]
    [Min(0.1f)] public float stuckWindow;
    [Tooltip("막힘 판정 거리(칸)입니다.")]
    [Min(0f)] public float stuckDistance;
    [Tooltip("목표에서 비껴가는 우회를 시작하면 같은 편(왼쪽·오른쪽)을 이 시간(초) 동안 고수합니다(벽 따라가기).")]
    [Min(0f)] public float detourCommit;
    [Tooltip("유지 지점에 도착했는데 사격선이 이 시간(초)보다 오래 막혀 있으면 공격 위치를 다시 고릅니다(Drone·Aegis·Sniper).")]
    [Min(0f)] public float losGrace;
    [Tooltip("유지 지점·대형 칸 보정 결과를 다시 쓰는 시간(초)입니다. 좌우 전환·감지 변화·기준점 1칸 이상 이동이면 바로 다시 계산합니다.")]
    [Min(0f)] public float holdCacheTime;

    public static FlightAvoidanceSettings Default => new FlightAvoidanceSettings
    {
        enabled = true,
        replanInterval = .5f,
        pathInterval = 1f,
        usePath = true,
        sampleCount = 16,
        lookAhead = 4f,
        clearance = .3f,
        stuckWindow = .5f,
        stuckDistance = .3f,
        detourCommit = 2f,
        losGrace = 2f,
        holdCacheTime = .2f,
    };
}
