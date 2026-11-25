using UnityEngine;

// Driller 땅파기: 드릴로 바닥을 파고들어(준비) → 땅속으로 플레이어 아래까지 이동 → 드릴 끝을 내밀어 예고 → 드릴부터 튀어나와 공중에서 반 바퀴 돌아 착지한다.
// 튀어나온 뒤 공중에 있는 동안만 몸통 공격(bodyAttackEnabled·bodyAttackDamage)이 유효하다.
[CreateAssetMenu(menuName = "Enemy/Ability Data/Driller Burrow")]
public class DrillerBurrowDataSO : EnemyAbilityDataSO
{
    [Header("Dig In (Windup)")]
    [Tooltip("준비 시작 뒤 드릴이 회전만 올리고 파고들기 시작할 때까지의 시간(초)입니다.")]
    [Min(0f)] public float sinkDelay = .18f;
    [Tooltip("몸 전체가 바닥선 아래로 숨는 깊이(칸)입니다. 몸 높이(약 1.98칸)보다 커야 합니다.")]
    [Min(.5f)] public float hiddenDepth = 2.2f;
    [Tooltip("이 비율만큼 파고들면 피격 판정을 끄고 무적이 됩니다.")]
    [Range(0f, 1f)] public float hideRatio = .45f;
    [Tooltip("파고드는 동안 몸이 떨리는 각도(도)입니다.")]
    [Min(0f)] public float digShakeDegrees = 2.5f;

    [Header("Underground Travel (Execute)")]
    [Tooltip("땅속에서 플레이어 쪽으로 이동하는 최고 속도(칸/초)입니다.")]
    [Min(0f)] public float travelSpeed = 8f;
    [Tooltip("땅속 이동 가속도(칸/초²)입니다.")]
    [Min(.1f)] public float travelAcceleration = 40f;
    [Tooltip("땅속 이동의 최대 시간(초)입니다. 지나면 그 자리에서 튀어나옵니다.")]
    [Min(0f)] public float maxTravelTime = 1.4f;
    [Tooltip("플레이어와의 수평 거리가 이 값 안이면 도착으로 봅니다(칸).")]
    [Min(0f)] public float arriveDistance = .25f;

    [Header("Warning")]
    [Tooltip("드릴 끝을 바닥 위로 내밀어 예고하는 시간(초)입니다.")]
    [Min(0f)] public float warningTime = .5f;
    [Tooltip("예고 동안 바닥 위로 나오는 드릴 끝의 높이(칸)입니다.")]
    [Min(0f)] public float peekHeight = .35f;
    [Tooltip("드릴 끝이 바닥 위로 올라오는 시간(초)입니다.")]
    [Min(.01f)] public float peekRiseTime = .12f;

    [Header("Eruption")]
    [Tooltip("튀어오르는 최소 높이(칸)입니다.")]
    [Min(0f)] public float minJumpHeight = 3.2f;
    [Tooltip("튀어오르는 최대 높이(칸)입니다.")]
    [Min(0f)] public float maxJumpHeight = 6.5f;
    [Tooltip("플레이어 높이보다 이만큼 더 올라가도록 높이를 정합니다(칸).")]
    public float jumpAbovePlayer = 1.4f;
    [Tooltip("튀어오를 때 플레이어 쪽 수평 속도의 상한(칸/초)입니다.")]
    [Min(0f)] public float maxHorizontalSpeed = 3f;
    [Tooltip("몸이 땅속 깊이에서 완전히 올라오는 시간(초)입니다.")]
    [Min(.01f)] public float emergeTime = .1f;
    [Tooltip("착지 뒤 흔들림 각도(도)입니다.")]
    [Min(0f)] public float landingSettleDegrees = 7f;

    [Header("Drill Spin (turns/s)")]
    [Min(0f)] public float digSpin = 7f;
    [Min(0f)] public float travelSpin = 8f;
    [Min(0f)] public float peekSpin = 11f;
    [Min(0f)] public float airSpin = 5f;

    [Header("Feedback")]
    [Tooltip("파고드는 동안 발밑에서 반복 재생하는 흙·불꽃입니다.")]
    public FeedbackDataSO digFeedback;
    [Min(.02f)] public float digInterval = .08f;
    [Tooltip("땅속 이동 중 바닥에서 반복 재생하는 흙 파편입니다.")]
    public FeedbackDataSO trailFeedback;
    [Min(.02f)] public float trailInterval = .09f;
    [Tooltip("드릴 끝이 바닥 위로 나올 때 재생합니다.")]
    public FeedbackDataSO peekFeedback;
    [Tooltip("드릴이 땅을 뚫고 튀어나올 때 재생합니다.")]
    public FeedbackDataSO eruptFeedback;

    [Header("Sound")]
    [Tooltip("땅속 이동 동안 반복 재생하는 소리입니다.")]
    public SoundOptions undergroundLoop;
    [Tooltip("땅속 반복음이 멈출 때 줄어드는 시간(초)입니다.")]
    [Min(0f)] public float undergroundLoopFade = .12f;
}
