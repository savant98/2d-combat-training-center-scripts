using UnityEngine;

// 조준점은 플레이어 주변 월드 좌표에서 정속으로 따라간다. 적을 중심으로 각도를 보간하지 않는다.
[DefaultExecutionOrder(150)]
public class SniperLaserAbility : AiAbility
{
    [SerializeField] private AiAbility nextAbility;
    [SerializeField] private Transform aimReticle;
    [SerializeField] private Transform aimMount;
    [SerializeField] private Transform muzzle;

    private Transform target;
    private LaserPreviewLine preview;
    private LaserBeam laser;
    private uint laserSpawnVersion;
    private LineRenderer previewRenderer;
    private Vector2 aimPoint;
    private Vector2 firedDirection;
    private bool hasTarget;
    private bool reloadCompleted;
    private bool usedSinceSpawn;

    private SniperLaserDataSO LaserData => (SniperLaserDataSO)data;
    private Vector2 Origin => muzzle != null ? (Vector2)muzzle.position : executePoint != null ? (Vector2)executePoint.position : (Vector2)transform.position;
    public Vector2 AimPoint => aimPoint;
    public Vector2 FiredDirection => firedDirection;
    private bool HasDetectedTarget
    {
        get
        {
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            return player != null && player.gameObject.activeInHierarchy
                && GetComponent<EnemyFlockMember>().TryDetectPlayer(player.transform.position);
        }
    }
    public bool CanBeginShot => HasDetectedTarget && (!usedSinceSpawn || IsReady());

    // 자동 반환된 빔이 재대여된 경우 총구 추종·중단 정리가 새 발사에 간섭하지 않게 한다.
    private LaserBeam OwnedLaser => laser != null && laser.gameObject.activeInHierarchy
        && laser.SpawnVersion == laserSpawnVersion ? laser : null;

    protected override void OnWindupStart()
    {
        ReturnEffects();
        reloadCompleted = false;
        usedSinceSpawn = true;
        GetComponent<EnemyFlockMember>()?.SetRole(EnemyFlockRole.Chase);
        controller.Velocity = Vector2.zero;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        target = player != null ? player.transform : null;
        hasTarget = target != null && target.gameObject.activeInHierarchy && HasDetectedTarget;
        aimPoint = hasTarget ? (Vector2)target.position : Origin;
        if (aimReticle != null) aimReticle.gameObject.SetActive(hasTarget);
        if (!hasTarget) return;
        if (LaserData.previewPrefab != null)
        {
            preview = WorldEntityPool.Instance.Spawn(LaserData.previewPrefab, Origin, Quaternion.identity);
            previewRenderer = preview.GetComponentInChildren<LineRenderer>(true);
            // 자동 반환을 끄고 패턴이 소유한다. 다른 개체가 재대여한 효과를 잘못 반환하지 않게 한다.
            preview.Show(Origin, AimDirection(), LaserData.laserLength, LaserData.previewWidth,
                LaserData.blockMask, LaserData.previewColor, -1f);
        }
        UpdateAimVisual();
    }

    protected override void OnWindup(float t)
    {
        controller.Velocity = Vector2.zero;
        hasTarget = target != null && target.gameObject.activeInHierarchy && HasDetectedTarget;
        if (!hasTarget) { ReturnEffects(); return; }
        aimPoint = Vector2.MoveTowards(aimPoint, target.position, LaserData.aimPointSpeed * Time.deltaTime);
        float dx = aimPoint.x - Origin.x;
        if (Mathf.Abs(dx) > 0.01f) controller.FacingUpdate(dx > 0f ? 1 : -1);
        UpdateAimVisual();
        preview?.UpdatePreviewVisual(Mathf.Max(0f, data.windupTime - t), data.windupTime);
    }

    private Vector2 AimDirection()
    {
        Vector2 delta = aimPoint - Origin;
        return delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.down;
    }

    private void UpdateAimVisual()
    {
        OrientMount(aimPoint);
        preview?.SetOrigin(Origin);
        preview?.SetDirection(AimDirection());
    }

    // LaserPreviewLine의 LateUpdate 뒤에 실제 벽 차단 끝점을 읽는다. 새 물리 질의를 만들지 않는다.
    private void LateUpdate()
    {
        if (State == AbilityState.Windup && previewRenderer != null && previewRenderer.enabled && aimReticle != null)
            aimReticle.position = previewRenderer.GetPosition(1);
        if (State == AbilityState.None && target != null && brain != null && !brain.IsPaused && HasDetectedTarget)
            OrientMount(target.position);
    }

    private void OrientMount(Vector2 point)
    {
        if (aimMount == null || aimMount.parent == null) return;
        Vector2 direction = point - (Vector2)aimMount.position;
        if (direction.sqrMagnitude < 0.0001f) return;
        Vector3 local = aimMount.parent.InverseTransformVector(direction);
        aimMount.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg);
    }

    protected override void OnExecuteStart()
    {
        // 마지막으로 보인 조준점 방향을 그대로 발사하며 발사 중에는 플레이어를 추적하지 않는다.
        firedDirection = AimDirection();
        ReturnEffects();
        if (!hasTarget || LaserData.laserPrefab == null) return;
        laser = WorldEntityPool.Instance.Spawn(LaserData.laserPrefab, Origin, Quaternion.identity);
        laserSpawnVersion = laser.SpawnVersion;
        laser.Fire(LaserData.damageInfo, Origin, firedDirection, LaserData.laserLength, data.executeTime,
            LaserData.blockMask, LaserData.damageMask, LaserData.laserWidth, controller);
    }

    protected override void OnExecute(float t)
    {
        controller.Velocity = Vector2.zero;
        OwnedLaser?.SetOrigin(Origin);
    }
    protected override void OnRecoverStart() => ReturnEffects();

    protected override bool CanFinishRecover(float elapsed)
    {
        reloadCompleted = base.CanFinishRecover(elapsed);
        return reloadCompleted;
    }

    protected override void OnStop()
    {
        ReturnEffects();
        controller.Velocity = Vector2.zero;
        if (nextAbility is SniperPatrolAbility patrol)
        {
            if (!brain.IsPaused && reloadCompleted) patrol.RequestTeleportAfterReload();
            else patrol.ClearPostReloadTeleport();
        }
        if (!brain.IsPaused && nextAbility != null)
        {
            brain.ForceNextAbility(nextAbility);
        }
        reloadCompleted = false;
        if (brain.IsPaused) GetComponent<EnemyFlockMember>()?.SetRole(EnemyFlockRole.None);
    }

    private void OnDisable()
    {
        ReturnEffects();
        reloadCompleted = false;
        usedSinceSpawn = false;
        GetComponent<EnemyFlockMember>()?.SetRole(EnemyFlockRole.None);
        if (nextAbility is SniperPatrolAbility patrol) patrol.ClearPostReloadTeleport();
    }

    private void ReturnEffects()
    {
        if (preview != null) { preview.ForceReturnToPool(); preview = null; }
        previewRenderer = null;
        OwnedLaser?.ForceReturnToPool();
        laser = null;
        laserSpawnVersion = 0;
        if (aimReticle != null) aimReticle.gameObject.SetActive(false);
    }
}
