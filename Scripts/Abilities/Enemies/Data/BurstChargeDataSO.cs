using UnityEngine;

// Windup 시간은 제자리 공회전, Execute 시간은 가속 돌진, Recover 시간은 제동을 포함한 후딜레이다.
[CreateAssetMenu(menuName = "Enemy/Ability Data/Burst Charge")]
public class BurstChargeDataSO : EnemyAbilityDataSO
{
    [Header("Charge Speed")]
    [Tooltip("돌진 속도의 기준이 되는 플레이어 대시 데이터입니다. 플레이어 DashAbility가 쓰는 에셋(Player_Dash_Data)을 연결합니다. 대시 속도는 dashForce(칸/초)입니다.")]
    public PlayerDashDataSO playerDashData;
    [Tooltip("플레이어 대시 속도(dashForce)에 곱하는 돌진 속도 비율입니다.")]
    [Min(0f)] public float playerDashSpeedRatio = 0.7f;
    [Tooltip("플레이어 대시 데이터가 비어 있을 때 사용하는 돌진 속도(칸/초)입니다.")]
    [Min(0f)] public float fallbackSpeed = 21f;
    [Tooltip("Execute 시작 후 돌진 속도에 도달하는 시간(초)입니다. 바퀴가 노면을 잡으며 가속하는 구간입니다.")]
    [Min(0.01f)] public float accelerationTime = 0.2f;

    [Header("Charge Distance")]
    [Tooltip("발진부터 제동을 마칠 때까지의 목표 이동 거리(칸)입니다. 지금까지 간 거리, 제동으로 넘어가는 한 프레임 동안 더 갈 거리, 현재 속도에서 제동으로 더 미끄러질 거리(속도×recoverBrakeTime÷2)의 합이 이 값에 닿으면 Execute를 끝내고 제동(후딜레이)으로 넘어갑니다. 속도는 바꾸지 않고 최고 속도로 달리는 시간만 줄어듭니다. 0이면 Execute 시간 동안 돌진합니다.")]
    [Min(0f)] public float chargeDistance = 8f;

    [Header("Brake")]
    [Tooltip("준비 동작 시작 시 남아 있던 순찰 속도를 멈추는 시간(초)입니다.")]
    [Min(0.01f)] public float windupBrakeTime = 0.12f;
    [Tooltip("후딜레이 시작 후 돌진 속도를 멈추는 시간(초)입니다.")]
    [Min(0.01f)] public float recoverBrakeTime = 0.3f;

    [Header("Rev")]
    [Tooltip("공회전을 끊어 올릴 때(Windup Clip의 OnChargeRev 이벤트)와 발진할 때 바퀴 뒤 바닥에서 튀는 불씨 효과입니다.")]
    public FeedbackDataSO revFeedback;

    [Header("Spark")]
    public FeedbackDataSO sparkFeedback;
    [Tooltip("돌진·제동 중 바닥 불꽃 간격(초)입니다. 준비 동작에서는 rev마다 한 번 튑니다.")]
    [Min(0.01f)] public float sparkInterval = 0.08f;
    [Tooltip("후딜레이 제동 중 이 속도(칸/초) 이상으로 미끄러지는 동안 불꽃을 냅니다.")]
    [Min(0f)] public float skidSparkMinSpeed = 2f;

    // 소리는 녹음 그대로 재생한다. 음높이·재생 속도를 코드에서 바꾸지 않는다.
    // 공회전 소리와 돌진 소리를 나누고, 움직임이 끝난 뒤 소리가 남지 않게 한다.
    [Header("Rev Sound")]
    [Tooltip("공회전(rev) 순서대로 하나씩 재생하는 차지 소리 조각입니다. 한 차지 소리를 rev 간격대로 나눠 이어 들리게 하고, 마지막 조각은 발진 순간에 끝나게 둡니다. rev가 더 많으면 마지막 소리를 다시 씁니다.")]
    public SoundOptions[] revSounds = new SoundOptions[0];

    [Header("Dash Sound")]
    [Tooltip("발진 순간 한 번 재생하는 돌진 소리입니다. 발진부터 미끄러짐이 끝날 때까지의 시간 안에 끝나는 소리를 씁니다.")]
    public SoundOptions dashSound;
    [Tooltip("벽에 막혔거나 제동 미끄러짐이 끝났는데(skidSparkMinSpeed 미만) 돌진 소리가 남아 있으면 이 시간(초) 동안 줄여 끝냅니다.")]
    [Min(0.01f)] public float dashSoundStopFade = 0.08f;

    [Header("Preview (Optional)")]
    [Tooltip("비워 두면 예고선을 생성하지 않습니다. Roller는 Model의 방향 화살표를 Animation으로 표시합니다.")]
    public LaserPreviewLine prevLinePrefab;
    [Tooltip("예고선 폭입니다. 막힘 판정이 폭/2 반지름의 CircleCast이므로 바닥에서 몸 중심까지의 높이보다 작게 둡니다.")]
    public float prevLineWidth = 0.6f;
    [Tooltip("예고선 길이(칸)입니다.")]
    [Min(0.1f)] public float prevLineLength = 2.4f;
    [Tooltip("몸 중심에서 진행 방향으로 이만큼 떨어진 곳부터 예고선을 그립니다.")]
    [Min(0f)] public float prevLineStartOffset = 0.8f;
    public Color prevLineColor = Color.red;
    public LayerMask prevLineBlockMask;
}
