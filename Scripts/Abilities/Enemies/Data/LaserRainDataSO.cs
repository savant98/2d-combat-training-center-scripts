using UnityEngine;

// Admin 레이저 비 패턴 데이터(LaserRainAbility).
// 레이저·예고선 Prefab과 피해·폭·지속 시간은 Admin의 다른 레이저 패턴(LaserShot·TeleportSweepLaser)과 같은 값을 쓴다.
[CreateAssetMenu(menuName = "Enemy/Ability Data/Laser Rain")]
public class LaserRainDataSO : EnemyAbilityDataSO
{
    [Header("Teleport")]
    [Tooltip("준비 시작부터 순간이동을 시작할 때까지의 시간")]
    public float teleportTriggerTime = 0.2f;
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public FeedbackDataSO teleportStartFeedback;
    public FeedbackDataSO teleportEndFeedback;
    [Tooltip("플레이어 몸 중심에서 Admin 몸통 중심까지 수평 거리. 왼쪽·오른쪽 중 무작위로 고르고, 그 자리에 벽이 가까우면 반대편에 나타난다")]
    public float teleportDistance = 7.5f;
    [Tooltip("순간이동한 자리의 바닥에서 몸통 중심까지 높이(몸통 반지름 2.5 + 바닥과의 틈)")]
    public float bodyHeightAboveFloor = 2.9f;
    [Tooltip("루트 위치에서 몸통(HitBox 원) 중심까지")]
    public Vector2 bodyCenterOffset = new(0f, 0.35f);
    public float bodyRadius = 2.5f;
    [Tooltip("순간이동 자리의 몸통과 벽 사이 최소 틈. 이만큼 들어갈 공간이 없으면 벽이 가깝다고 보고 반대편을 쓴다")]
    public float wallClearance = 0.5f;
    public float wallSearchDistance = 40f;
    public float verticalSearchDistance = 60f;
    [Tooltip("현재 카메라 경계(CinemachineConfiner2D) 밖은 룸으로 보지 않는다")]
    public bool clampToCameraBounds = true;

    [Header("Retreat")]
    [Tooltip("다시 나타난 순간 플레이어 반대쪽으로 물러나는 속도(칸/초). 실행·회수 동안 감속해 멈춘다")]
    public float retreatSpeed = 3f;
    public float retreatDeceleration = 2f;

    [Header("Laser Rain")]
    [Min(1)] public int laserCount = 5;
    [Tooltip("레이저 사이 발사 간격(초). 예고선도 같은 간격으로 펼쳐진다")]
    public float laserInterval = 0.1f;
    [Tooltip("레이저 줄 슬롯 간격(칸). 3번째 줄이 순간이동을 시작한 순간의 플레이어 위치이고, 첫 줄은 Admin 반대쪽 끝이다(Admin이 왼쪽이면 오른쪽 끝부터)")]
    public float laserSpacing = 2.4f;
    [Tooltip("각 레이저의 예고선이 나타나서 발사될 때까지의 시간")]
    public float previewLeadTime = 0.8f;
    [Tooltip("레이저 시작점을 천장에서 내리는 거리. 레이저·예고선의 막힘 판정 원(레이저는 발사 순간 폭의 2.5배)이 천장에 겹치면 시작점에서 막히므로 그 반지름보다 크게 둔다")]
    public float laserTopMargin = 1.2f;
    [Tooltip("천장·카메라 경계가 없을 때 플레이어 위로 레이저를 시작할 높이")]
    public float fallbackTopHeight = 14f;
    [Tooltip("레이저 줄과 벽(또는 카메라 경계) 사이 최소 틈. 이보다 벽에 가까운 슬롯은 벽으로 보고 묶음을 옮긴다")]
    public float laserEdgeMargin = 0.6f;

    [Header("Preview")]
    public LaserPreviewLine previewLinePrefab;
    public float previewLineWidth = 1f;
    public Color previewLineColor = Color.red;

    [Header("Laser")]
    public LaserBeam laserPrefab;
    public WeaponBaseSO.DamageInfo laserDamageInfo;
    public float laserLength = 40f;
    public float laserWidth = 0.85f;
    [Tooltip("한 발이 화면에 남는 시간(단발 레이저)")]
    public float laserDuration = 0.25f;
    public LayerMask laserBlockMask;
    public LayerMask laserDamageMask;
}
