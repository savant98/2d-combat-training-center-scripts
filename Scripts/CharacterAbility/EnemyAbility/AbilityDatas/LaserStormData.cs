using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Laser Storm Data")]
public class LaserStormData : EnemyAbilityData
{
    [Header("Teleport")]
    public float teleportTriggerTime = 0.2f;
    public Vector2 teleportOffset = new Vector2(0f, 3f);
    public LayerMask teleportBlockMask;
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public FeedbackData teleportStartFx;
    public FeedbackData teleportEndFx;

    [Header("Ground Check")]
    public float groundCheckStartHeight = 5f;
    public float groundCheckDistance = 50f;

    [Header("Facing")]
    public float faceDeadZone = 0.05f;

    [Header("Preview")]
    public LaserPreviewLine previewLinePrefab;
    public float previewDuration = 0.5f;
    public float previewLineWidth = 0.2f;
    public Color previewLineColor = Color.red;

    [Header("Laser")]
    public LaserBeam laserPrefab;
    public WeaponBase.DamageInfo laserDamageInfo;
    public float laserLength = 20f;
    public float laserWidth = 0.2f;
    public LayerMask laserBlockMask;
    public LayerMask laserDamageMask;

    [Header("Emission")]
    public Color idleEmissionColor = Color.black;
    public Color chargeEmissionColor = Color.red;
    public Color fireEmissionColor = Color.white;
}