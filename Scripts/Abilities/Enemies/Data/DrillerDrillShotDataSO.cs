using UnityEngine;

// Driller 드릴 발사: 몸을 뒤로 90도 눕혀 드릴을 정면으로 겨누고(준비) → 텔레스코프 피스톤 팔로 드릴을 쏘아 보냈다가(실행) → 거두고 다시 선다(회수).
// 드릴 머리가 지나간 구간 전체를 모터 질의로 검사하고, 한 번 쏠 때 Player마다 한 번만 맞힌다.
[CreateAssetMenu(menuName = "Enemy/Ability Data/Driller Drill Shot")]
public class DrillerDrillShotDataSO : EnemyAbilityDataSO
{
    [Header("Aim (Windup)")]
    [Tooltip("몸이 뒤로 90도 눕는 시간(초)입니다.")]
    [Min(.05f)] public float tiltTime = .3f;
    [Tooltip("다 누운 뒤 몸이 살짝 튀었다 가라앉는 각도(도)와 시간(초)입니다.")]
    [Min(0f)] public float tiltBounceDegrees = 5f;
    [Min(.01f)] public float tiltBounceTime = .14f;

    [Header("Preview Line")]
    public LaserPreviewLine previewLinePrefab;
    [Min(.01f)] public float previewWidth = .7f;
    public Color previewColor = new(1f, .05f, .05f, 1f);
    public LayerMask previewBlockMask = 64;

    [Header("Fire (Execute)")]
    [Tooltip("팔이 뻗는 최대 길이(칸)입니다. 드릴 끝은 여기에 드릴 길이만큼 더 나갑니다.")]
    [Min(0f)] public float maxExtension = 4f;
    [Tooltip("팔이 최대 길이까지 뻗는 시간(초)입니다.")]
    [Min(.01f)] public float extendTime = .14f;
    [Tooltip("벽 앞에서 드릴 끝이 남겨 두는 거리(칸)입니다.")]
    [Min(0f)] public float wallMargin = .05f;

    [Header("Damage")]
    public WeaponBaseSO.DamageInfo drillDamageInfo = new() { damage = 20f };
    [Tooltip("드릴 판정 상자의 두께(칸)입니다. 드릴 밑동 지름에 맞춥니다.")]
    [Min(.05f)] public float drillHitThickness = .75f;

    [Header("Retract (Recover)")]
    [Tooltip("팔을 거두는 시간(초)입니다. 그 뒤 standUpTime 동안 다시 섭니다. 둘의 합이 recoverTime보다 길면 그만큼 기다립니다.")]
    [Min(.01f)] public float retractTime = .45f;
    [Min(.01f)] public float standUpTime = .4f;

    [Header("Drill Spin (turns/s)")]
    [Min(0f)] public float aimSpin = 9f;
    [Min(0f)] public float fireSpin = 13f;
    [Min(0f)] public float retractSpin = 4f;

    [Header("Feedback")]
    [Tooltip("다 누워 바닥에 닿을 때 재생합니다.")]
    public FeedbackDataSO tiltFeedback;
    [Tooltip("드릴을 쏠 때 팔 시작점에서 재생합니다.")]
    public FeedbackDataSO fireFeedback;
    [Tooltip("드릴이 벽에 닿았을 때 닿은 자리에서 재생합니다.")]
    public FeedbackDataSO wallHitFeedback;
    [Tooltip("팔을 거두기 시작할 때 재생합니다.")]
    public FeedbackDataSO retractFeedback;
    [Tooltip("다시 일어설 때 재생합니다.")]
    public FeedbackDataSO standFeedback;
}
