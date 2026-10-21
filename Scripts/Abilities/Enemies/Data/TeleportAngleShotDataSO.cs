using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Teleport AngleShot")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "TeleportAngleShotData")]
public class TeleportAngleShotDataSO : EnemyAbilityDataSO
{
    [Header("Teleport")]
    public float teleportTriggerTime = 0.2f;
    public Vector2 teleportOffset = new Vector2(4f, 1f);
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public FeedbackDataSO teleportStartFx;
    public FeedbackDataSO teleportEndFx;
    public LayerMask teleportBlockMask;

    [Header("Post Teleport Retreat")]
    public float postTeleportMoveSpeed = 8f;
    public float moveDeadZone = 0.05f;
    public float standPositionCheckRadius = 0.3f;

    [Header("Shot")]
    public AngleChargeShot projectilePrefab;
    public WeaponBaseSO.DamageInfo projectileDamageInfo;
    public float projectileSpeed = 8f;
    public LayerMask projectileCollideLayer;
    public int shotCount = 2;
    public float shotAngleFromVertical = 30f;
    public float multiShotSpacing = 0.6f;
    public Vector2 shotSpawnOffset = new Vector2(0f, 3f);

    [Header("Shot Spawn Validation")]
    public float shotSpawnCheckRadius = 0.2f;
    public LayerMask shotSpawnBlockMask;
    public LayerMask shotSpawnOverlapMask;
    public float shotSpawnResolveStep = 0.25f;
    public int shotSpawnResolveTries = 4;

    [Header("Ground Check")]
    public float groundCheckStartHeight = 5f;
    public float groundCheckDistance = 50f;
    public LayerMask groundCheckMask;
}
