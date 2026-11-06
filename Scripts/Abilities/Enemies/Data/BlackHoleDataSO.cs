using UnityEngine;

// Admin 블랙홀 패턴 데이터.
// 준비: 룸 중앙으로 순간이동한 뒤 몸통 중심에 블랙홀이 맺힌다(공간 왜곡만, 끌림 없음).
// 실행: 블랙홀이 열려 플레이어를 끌어당기고(플레이어 이동 속도를 조금 넘어 대시로만 벗어남), 블랙홀 바로 아래 기둥 장판이
//       닿으면 피해를 준다. 그동안 천장에서 플레이어 크기의 큐브가 맵 전체에 떨어진다.
// 회수: 블랙홀이 붕괴한다. 이미 떨어지는 큐브는 그대로 떨어져 부서진다.
[CreateAssetMenu(menuName = "Enemy/Ability Data/Black Hole")]
public class BlackHoleDataSO : EnemyAbilityDataSO
{
    [Header("Teleport")]
    public float teleportTriggerTime = 0.2f;
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public FeedbackDataSO teleportStartFeedback;
    public FeedbackDataSO teleportEndFeedback;

    [Header("Room Center")]
    [Tooltip("플레이어 위치에서 좌우 벽을 찾는 최대 거리(칸)")]
    public float wallSearchDistance = 40f;
    [Tooltip("바닥·천장을 찾는 최대 거리(칸)")]
    public float verticalSearchDistance = 60f;
    [Tooltip("루트 기준 몸통 중심(HitBox 원 중심). 블랙홀도 여기에 맺힌다")]
    public Vector2 bodyCenterOffset = new(0f, 0.35f);
    [Tooltip("몸통 반지름(톱니 끝 = HitBox 원 반지름)")]
    public float bodyRadius = 2.5f;
    [Tooltip("몸통과 바닥·천장 사이에 남길 최소 거리(칸)")]
    public float verticalClearance = 1.5f;
    [Tooltip("천장을 찾지 못한 룸에서 바닥 위 중심 높이(칸)")]
    public float fallbackCenterHeight = 8f;
    [Tooltip("현재 카메라 경계(CinemachineConfiner2D) 밖의 벽·천장은 룸 범위로 보지 않는다")]
    public bool clampToCameraBounds = true;

    [Header("Black Hole")]
    public AdminBlackHole blackHolePrefab;
    public BlackHolePullSettings pull = BlackHolePullSettings.Default;
    [Tooltip("블랙홀 바로 아래(바닥 ~ 블랙홀 중심) 피해 장판")]
    public BlackHoleFieldSettings field = BlackHoleFieldSettings.Default;

    [Header("Cube Rain")]
    public AdminFallingCube cubePrefab;
    public WeaponBaseSO.DamageInfo cubeDamageInfo = new() { damage = 12f, damageType = DamageType.Normal };
    [Tooltip("큐브가 부딪히는 지형(플레이어와 같은 Platform + TransparentPlatform)")]
    public LayerMask cubeCollideLayer = (1 << 6) | (1 << 12);
    [Tooltip("블랙홀이 열린 뒤 첫 큐브까지 기다리는 시간(초)")]
    public float cubeStartDelay = 0.6f;
    [Tooltip("큐브 생성 간격(초)")]
    public float cubeInterval = 0.3f;
    [Range(0f, 0.9f)] public float cubeIntervalJitter = 0.25f;
    [Tooltip("큐브가 떨어지는 줄 간격(칸). 큐브 폭 1.5 + 틈")]
    public float cubeLaneWidth = 1.9f;
    [Tooltip("벽과 큐브 사이 여유(칸)")]
    public float cubeWallMargin = 0.9f;
    [Tooltip("천장 아래 큐브 생성 높이(칸, 큐브 중심까지)")]
    public float cubeCeilingOffset = 1.1f;
    [Tooltip("천장을 찾지 못했을 때 블랙홀 중심 위 생성 높이(칸)")]
    public float cubeFallbackHeight = 9f;
    [Tooltip("플레이어가 선 줄을 고를 확률")]
    [Range(0f, 1f)] public float cubePlayerTargetChance = 0.3f;
    [Tooltip("최근에 고른 줄을 다시 고르지 않는 개수")]
    [Min(0)] public int cubeRecentLaneMemory = 2;
    public float cubeGravity = 28f;
    public float cubeMaxFallSpeed = 22f;
}
