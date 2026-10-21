using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Laser Shot")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "LaserShotData")]
public class LaserShotDataSO : EnemyAbilityDataSO
{
    [Header("Teleport")]
    public float teleportTriggerTime = 0.15f;
    public Vector2 teleportOffset = new Vector2(4f, 3f);
    public LayerMask teleportBlockMask;
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public FeedbackDataSO teleportStartFx;
    public FeedbackDataSO teleportEndFx;

    [Header("Follow")]
    [Min(0f)] public float followSpeed = 8f;
    [Min(0f)] public float followDeadZone = 0.05f;

    [Header("Laser")]
    public LaserBeam laserPrefab;
    public float laserLength = 14f;
    public float laserWidth = 0.25f;
    public LayerMask laserBlockMask;
    public LayerMask laserDamageMask;
    public WeaponBaseSO.DamageInfo laserDamageInfo;

    [Header("Laser Direction")]
    public Vector2 rightSideShootDirection = new Vector2(-1f, -1f);
    public Vector2 leftSideShootDirection = new Vector2(1f, -1f);

    [Header("Preview Line")]
    public LaserPreviewLine previewLinePrefab;
    public Color previewLineColor = Color.red;
    public float previewLineWidth = 0.08f;

    [Header("Caster Emission")]
    public Color idleEmissionColor = Color.black;
    public Color chargeEmissionColor = Color.yellow;
    public Color fireEmissionColor = Color.red;
}