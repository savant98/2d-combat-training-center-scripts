using UnityEngine;
using static WeaponBaseSO;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Charge And Fire")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "ChargeAndFireData")]
public class ChargeAndFireDataSO : EnemyAbilityDataSO
{
    [Header("Teleport")]
    public float teleportTriggerTime = 0.15f;
    public Vector2 teleportOffset = new Vector2(2.5f, 3.5f);
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public FeedbackDataSO teleportStartFx;
    public FeedbackDataSO teleportEndFx;
    public LayerMask teleportBlockMask;
    public float groundCheckStartHeight = 5f;
    public float groundCheckDistance = 20f;

    [Header("Preview")]
    public LaserPreviewLine previewLinePrefab;
    public float previewLineLength = 10f;
    public float previewLineWidth = 0.2f;
    public Color previewLineColor = Color.red;
    public LayerMask previewBlockMask;

    [Header("Execute - Slash")]
    [Range(0f, 89f)] public float slashAngleFromDown = 45f;
    public float slashSpeed = 16f;
    public float slashMaxDuration = 0.3f;
    public float slashGroundHitThreshold = 0.1f;
    public MeleeHitbox meleeHitbox;
    public DamageInfo slashDamageInfo;
    public FeedbackDataSO slashFeedback;

    [Header("Slash Homing Missiles")]
    [Tooltip("베기가 끝나는 순간 코어 중심에서 쏘는 유도탄. 비워 두면 쏘지 않는다")]
    public ConstructorHomingMissile missilePrefab;
    [Min(1)] public int missileCount = 2;
    [Range(0f, 90f), Tooltip("위쪽을 기준으로 좌우로 벌리는 각도(도). 2발이면 왼쪽·오른쪽으로 이만큼씩 기울여 쏜다")]
    public float missileSpreadAngle = 40f;
    [Min(0f), Tooltip("쏘는 방향으로 이 거리(칸) 안에 벽이 있으면 위쪽으로 세워 쏜다. 0이면 확인하지 않는다")]
    public float missileClearance = 3.5f;
    [Min(0.01f), Tooltip("추적할 때의 최고 속도(칸/초)")] public float missileSpeed = 14f;
    public LayerMask missileCollideLayer = 1 << 6;
    [Tooltip("직격 피해")] public DamageInfo missileDamageInfo = new DamageInfo { damage = 10f };
    [Min(0f)] public float missileExplosionRadius = 1.5f;
    [Tooltip("폭발 범위 피해. 직격한 대상에게는 직격 피해만 들어간다")]
    public DamageInfo missileExplosionDamageInfo = new DamageInfo { damage = 10f };
    public FeedbackDataSO missileLaunchFeedback;
    [Range(0f, 1f), Tooltip("쏘는 순간 코어가 받는 반동의 세기")] public float missileRecoil = 0.8f;

    [Header("Slash Homing Missiles - Guidance")]
    [Min(0f), Tooltip("쏜 방향으로 퍼져 나가며 느려지는 시간")] public float missileLaunchDuration = 0.5f;
    [Min(0f)] public float missileLaunchTurnRateDegrees = 20f;
    [Min(0f), Tooltip("가장 느린 속도로 Player 쪽으로 방향을 잡는 시간")] public float missileSlowDuration = 0.5f;
    [Range(0f, 1f)] public float missileSlowSpeedRatio = 3f / 14f;
    [Min(0f)] public float missileSlowTurnRateDegrees = 180f;
    [Min(0f), Tooltip("Player를 추적하는 시간. 지나면 방향을 바꾸지 않는다")] public float missileTrackingDuration = 3.75f;
    [Min(0f)] public float missileTurnRateDegrees = 180f;
    [Min(0f)] public float missileAccelerationDuration = 0.9f;
    [Range(0f, 1f)] public float missileInitialSpeedRatio = 6f / 14f;
    [Min(0.01f), Tooltip("이 시간이 지나면 그 자리에서 폭발한다")] public float missileLifeTime = 5f;

    [Header("Execute - Slash Pause")]
    public float slashPauseDuration = 0.1f;

    [Header("Execute - Reverse Dash")]
    public float reverseDashSpeed = 18f;
    public float reverseDashDuration = 0.2f;
    public float reverseDashDeadZone = 0.05f;
    public FeedbackDataSO sparkFeedback;
    public float sparkInterval = 0.1f;

    [Header("Execute - Brake And Fire")]
    public float brakeDeceleration = 60f;
    public float brakeBeforeFireTime = 0.3f;

    [Header("Laser")]
    public LaserBeam laserPrefab;
    public WeaponBaseSO.DamageInfo laserDamageInfo;
    public float laserLength = 12f;
    public float laserDuration = 0.25f;
    public float laserWidth = 0.25f;
    [Range(0f, 45f)] public float laserAngleOffset = 15f;
    public Color laserEmissionColor = Color.red;
    public LayerMask laserBlockMask;
    public LayerMask laserDamageMask;
    public FeedbackDataSO laserFireFeedback;
}