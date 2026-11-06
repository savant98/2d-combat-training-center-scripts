using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Tracking Slam")]
public sealed class TrackingSlamDataSO : EnemyAbilityDataSO
{
    [Header("반복 내려찍기")]
    [Min(1)] public int slamCount = 3;
    [Min(0.1f)] public float heightAboveFloor = 7f;
    [Min(0.1f)] public float riseSpeed = 18f;
    [Min(0.01f)] public float trackingDuration = 1.5f;
    [Min(0.1f)] public float trackingAcceleration = 24f;
    [Min(0.1f)] public float trackingMaxSpeed = 12f;
    [Min(0.1f)] public float slamSpeed = 48f;
    [Min(0.1f)] public float travelTimeout = 3f;
    [Min(1f)] public float floorSearchDistance = 80f;

    [Header("공중 단계 애니메이션")]
    public AnimationClip riseClip;
    public AnimationClip trackingClip;
    public AnimationClip slamClip;
    public AnimationClip restPoseClip;
    public FeedbackDataSO riseFeedback;

    [Header("내려찍기 예고선")]
    public LaserPreviewLine previewLinePrefab;
    [Min(.01f)] public float previewLineWidth = 2.5f;
    public Color previewLineColor = Color.red;

    [Header("Constructor 지면 충격파")]
    public ConstructorGroundWave groundWavePrefab;
    public LayerMask platformLayer = 64;
    public WeaponBaseSO.DamageInfo waveDamage = new WeaponBaseSO.DamageInfo { damage = 12f };
    public FeedbackDataSO landingFeedback;
}
