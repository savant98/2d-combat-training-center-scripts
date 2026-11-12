using UnityEngine;

// 도약 내려찍기 패턴 데이터(Stomper 2페이즈).
// 본체는 단단한 기계라 모양이 변하지 않는다. 움직임은 로켓 추진기가 만든다.
// 준비(windupTime): 추진기가 아래로 뻗어 본체를 들어 올리고 예열한다(분사구 발치에서 불길이 새어 나온다).
// 실행: 추진기가 분사해 표시만 화면 위로 솟구쳐 사라진다. 실제 몸은 그동안 모터로 룸 중앙 착지점까지 옮긴다.
//       사라진 뒤 룸 중앙의 사각형 피격 범위를 warningDuration 동안 예고하고, 예고가 끝나는 순간에 맞춰
//       추진기를 위로 단 채 화면 위에서 나타나 내려찍는다. 착지와 함께 범위 전체가 폭발하며 한 번 피해를 준다.
// 회수(recoverTime): 추진기가 떨어져 나가고, 기운 본체가 자세 제어 분사 두 번으로 바로 선다.
// executeTime은 쓰지 않는다(착지하면 끝난다).
[CreateAssetMenu(menuName = "Enemy/Ability Data/Leap Slam")]
public sealed class LeapSlamDataSO : EnemyAbilityDataSO
{
    [Header("추진기 · 본체를 움직이는 물체")]
    [Tooltip("프레임에 붙일 추진기 Prefab(잡기 공격의 추진기와 같은 것)")]
    public GameObject thrusterPrefab;
    [Tooltip("추진기가 아래로 뻗는 길이(프레임 로컬 단위). 준비 때 분사구가 바닥을 짚어 본체를 그만큼 들어 올린다")]
    [Min(0.05f)] public float thrusterStroke = 0.2f;
    [Tooltip("준비를 시작한 뒤 추진기가 다 뻗을 때까지의 시간(초)")]
    [Min(0.01f)] public float thrusterDeployTime = 0.28f;
    [Tooltip("예열 동안 분사구 발치에서 불길을 내보내는 간격(초). 발사가 가까울수록 불길이 커진다")]
    [Min(0.01f)] public float preheatWashInterval = 0.045f;
    [Tooltip("솟구칠 때의 분사 불꽃 크기")]
    [Min(0f)] public float launchFlameScale = 2.2f;
    [Tooltip("내려찍을 때의 분사 불꽃 크기")]
    [Min(0f)] public float diveFlameScale = 2.2f;
    [Tooltip("솟구치는 순간의 점화음")]
    public SoundOptions launchFlameSound;

    [Header("도약 · 표시만 화면 밖으로")]
    [Tooltip("표시가 솟구치기 시작하는 속도(칸/초)")]
    [Min(1f)] public float launchSpeed = 55f;
    [Tooltip("솟구치는 동안의 가속(칸/초²)")]
    [Min(0f)] public float launchAcceleration = 900f;
    [Tooltip("몸체가 화면 위쪽 끝을 이만큼 넘으면 사라진 것으로 본다(칸). 아래에 달린 추진기와 분사 불꽃까지 화면을 벗어날 여유")]
    [Min(0f)] public float offscreenMargin = 5.5f;
    [Tooltip("카메라를 찾지 못했을 때 사라지는 높이(칸)")]
    [Min(1f)] public float fallbackOffscreenHeight = 22f;
    [Tooltip("도약이 이 시간을 넘기면 화면 밖 여부와 관계없이 다음 단계로 간다(초)")]
    [Min(0.05f)] public float launchTimeout = 0.6f;
    public FeedbackDataSO launchFeedback;

    [Header("피격 범위 · 룸 중앙 사각형")]
    [Tooltip("사라진 뒤 피격 범위를 예고하는 시간(초). 이 시간이 끝나는 순간 착지한다")]
    [Min(0.1f)] public float warningDuration = 2f;
    [Tooltip("좌우 벽과 범위 사이에 남기는 폭 = 플레이어 폭 + 이 여백(칸). 플레이어 하나가 설 자리에 약간의 여백만 남긴다")]
    [Min(0f)] public float sideSafePadding = 0.5f;
    [Tooltip("플레이어를 찾지 못했을 때 쓰는 플레이어 폭(칸)")]
    [Min(0.1f)] public float fallbackPlayerWidth = 1.5f;
    [Tooltip("룸 폭에 대한 범위 폭의 상한 비율. 1이면 좌우 여백만 뺀 폭 전체를 쓴다")]
    [Range(0.1f, 1f)] public float areaWidthRatio = 1f;
    [Tooltip("룸 높이(바닥 ~ 천장)에 대한 범위 높이의 비율")]
    [Range(0.1f, 1f)] public float areaHeightRatio = 1f;
    [Tooltip("범위를 세로로 놓는 자리. 0은 바닥에 붙이고 0.5는 룸 한가운데, 1은 천장에 붙인다")]
    [Range(0f, 1f)] public float areaVerticalAnchor = 0.5f;
    [Tooltip("예고 표시보다 실제 판정을 안쪽으로 줄이는 폭(칸). 경계에 걸친 피격을 줄인다")]
    [Min(0f)] public float areaHitInset = 0.2f;
    public LeapSlamAreaVisual areaVisualPrefab;
    [Tooltip("예고 동안 반복하는 경고음. 착지가 가까울수록 높아진다")]
    public SoundOptions warningLoopSound;
    [Tooltip("경고음 높이: 예고 시작(x) → 착지 직전(y)")]
    public Vector2 warningLoopPitch = new Vector2(0.9f, 1.35f);

    [Header("룸 범위 찾기")]
    [Tooltip("좌우 벽을 찾는 최대 거리(칸)")]
    [Min(1f)] public float wallSearchDistance = 60f;
    [Tooltip("바닥·천장을 찾는 최대 거리(칸)")]
    [Min(1f)] public float verticalSearchDistance = 60f;
    [Tooltip("현재 카메라 경계(CinemachineConfiner2D) 밖은 룸으로 보지 않는다")]
    public bool clampToCameraBounds = true;
    [Tooltip("천장을 찾지 못한 룸의 높이(칸)")]
    [Min(1f)] public float fallbackRoomHeight = 14f;

    [Header("실제 몸 이동 · 모터")]
    [Tooltip("착지점으로 옮길 때 바닥 위로 띄우는 높이(칸). 중간 지형을 넘는다")]
    [Min(0f)] public float transitClearance = 3.5f;
    [Tooltip("예고가 끝났는데도 착지점에 닿지 못했을 때 더 기다리는 시간(초). 지나면 지금 자리 바로 아래에 내려찍는다")]
    [Min(0f)] public float transitGraceTime = 1.5f;

    [Header("내려찍기")]
    [Tooltip("표시가 화면 위에서 내려오는 속도(칸/초)")]
    [Min(1f)] public float diveSpeed = 110f;
    public WeaponBaseSO.DamageInfo damage = new WeaponBaseSO.DamageInfo { damage = 40f, damageType = DamageType.Normal };
    [Tooltip("착지점에서 재생한다(충격 입자·폭발음·화면 흔들림)")]
    public FeedbackDataSO impactFeedback;
    [Tooltip("폭발음에 겹치는 저음")]
    public SoundOptions impactSubSound;
    [Tooltip("내려오기 시작할 때의 바람 소리")]
    public SoundOptions diveSound;

    [Header("화염")]
    [Tooltip("불덩이가 되어 내려오기 시작할 때의 점화음")]
    public SoundOptions diveFlameSound;
    [Tooltip("폭발 뒤 화염이 타는 동안 반복하는 소리. fireLoopDuration 동안 줄어들며 꺼진다")]
    public SoundOptions fireLoopSound;
    [Min(0f)] public float fireLoopDuration = 1.5f;

    [Header("회수 · 자세 제어 분사")]
    [Tooltip("회수를 시작한 뒤 첫 분사의 시각(초). 기운 쪽 옆면에서 뿜어 본체를 반대로 넘긴다. 회수 Clip의 기울기와 맞춘다")]
    [Min(0f)] public float firstJetTime = 1.08f;
    [Tooltip("두 번째 분사의 시각(초). 반대쪽 옆면에서 뿜어 넘어가던 본체를 바로 세운다")]
    [Min(0f)] public float secondJetTime = 1.34f;
    [Tooltip("분사 한 번의 길이(초)")]
    [Min(0.01f)] public float jetDuration = 0.14f;
    public SoundOptions jetSound;
}
