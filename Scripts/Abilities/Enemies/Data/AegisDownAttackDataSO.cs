using UnityEngine;

// Aegis 내려찍기 데이터. Admin 내려찍기(DownAttackDataSO)와 흐름·판정(RedCube)·소리·이펙트가 같고 체공만 더 길다.
[CreateAssetMenu(menuName = "Enemy/Ability Data/Aegis Down Attack")]
public sealed class AegisDownAttackDataSO : FlyingAbilityDataSO
{
    [Header("순간이동")]
    [Tooltip("준비 시작 뒤 순간이동을 시작하는 시각(초)")]
    [Min(0f)] public float teleportTriggerTime = .08f;
    [Min(0f)] public float disappearDelay = .06f;
    [Min(0f)] public float appearDelay = .25f;
    public FeedbackDataSO teleportFeedback;

    [Header("머리 위 자리")]
    [Tooltip("플레이어가 선 바닥에서 몸 중심까지의 높이(칸). 천장이 낮으면 그 아래로 줄인다")]
    [Min(1f)] public float hoverHeight = 7f;
    [Tooltip("몸 반지름(HitBox 원 반지름). 루트가 몸 중심이다")]
    [Min(.1f)] public float bodyRadius = 1.8f;
    [Min(0f)] public float ceilingMargin = .5f;
    [Min(0f)] public float wallMargin = .3f;
    [Tooltip("몸 아래에서 착지 바닥까지의 최소 거리(칸). 이보다 낮으면 시작하지 않는다")]
    [Min(0f)] public float minimumDrop = 2.5f;
    [Min(1f)] public float floorSearchDistance = 30f;
    [Tooltip("이 거리 안에 플레이어가 있을 때만 시작한다(칸)")]
    [Min(1f)] public float maxStartDistance = 26f;
    [Tooltip("현재 카메라 경계(CinemachineConfiner2D) 위로는 나타나지 않는다")]
    public bool clampToCameraBounds = true;

    [Header("체공")]
    [Tooltip("체공 중 플레이어를 따라가는 최고 속도(칸/초)")]
    [Min(0f)] public float followSpeed = 5.5f;
    [Tooltip("내려찍기 직전에 자리를 고정하는 시간(초). 이 동안 피할 수 있다")]
    [Min(0f)] public float aimLockDuration = .4f;
    [Tooltip("자리를 고정한 동안 살짝 떠오르는 속도(칸/초)")]
    public float lockRiseSpeed = .8f;

    [Header("예고")]
    public LaserPreviewLine previewPrefab;
    public Color previewColor = Color.red;
    [Min(.01f)] public float previewWidth = .08f;

    [Header("내려찍기")]
    [Min(1f)] public float slamSpeed = 40f;

    [Header("회수")]
    [Tooltip("착지 뒤 바닥에 머무는 시간(초)")]
    [Min(0f)] public float recoverHoldTime = .25f;
    [Tooltip("회수 동안 다시 떠오르는 높이(칸)")]
    [Min(0f)] public float recoverRiseHeight = 2f;

    [Header("착지 판정(Admin RedCube)")]
    [Tooltip("착지할 때 만드는 근접 판정(Admin 내려찍기와 같은 RedCubeHitbox)")]
    public MeleeHitbox meleeHitbox;
    public WeaponBaseSO.DamageInfo damageInfo;
    [Tooltip("착지 바닥 위 판정 생성 높이(칸). Admin은 몸 바닥보다 0.02칸 위에 만든다")]
    public float hitboxFloorOffset = .02f;
}
