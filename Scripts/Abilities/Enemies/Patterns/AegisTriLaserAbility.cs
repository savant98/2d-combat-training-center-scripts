using UnityEngine;

// 본체가 정지한 채 세 방향을 예고하고 발사한다. 벽·피해 판정은 기존 레이저의 모터 경로를 쓴다.
public sealed class AegisTriLaserAbility : FlyingAbility
{
    [SerializeField] private AiAbility nextAbility;
    private AegisTriLaserDataSO settings;
    private readonly LaserPreviewLine[] previews = new LaserPreviewLine[3];
    private readonly LaserBeam[] beams = new LaserBeam[3];
    private readonly uint[] beamVersions = new uint[3];
    private readonly Vector2[] directions = new Vector2[3];
    private Vector2 aimPoint;
    private bool ownsMovement;
    private bool previousIgnoreForces;

    public override bool IsBodyAttackActive => false;
    public int FiredCount { get; private set; }
    public bool AimLocked { get; private set; }
    public Vector2 ShotCenter { get; private set; }
    public Vector2 GetDirection(int index) => directions[index];
    public Vector2 GetOrigin(int index) => ShotCenter + directions[index] * settings.muzzleDistance;
    public LaserPreviewLine GetPreview(int index) => previews[index];
    public LaserBeam GetBeam(int index) => beams[index] != null && beams[index].gameObject.activeInHierarchy
        && beams[index].SpawnVersion == beamVersions[index] ? beams[index] : null;

    protected override void Awake()
    {
        base.Awake();
        settings = (AegisTriLaserDataSO)data;
    }

    public override bool CanBegin()
    {
        AcquireTarget();
        if (!HasTarget || settings.laserPrefab == null || settings.previewPrefab == null) return false;
        Vector2 delta = target.position - transform.position;
        return delta.magnitude >= settings.minRange && delta.magnitude <= settings.maxRange
            && controller.Motor.RaycastEnvironment(transform.position, delta.normalized, delta.magnitude).collider == null;
    }

    protected override void OnWindupStart()
    {
        ReturnEffects();
        AcquireTarget();
        FiredCount = 0;
        AimLocked = false;
        ShotCenter = transform.position;
        aimPoint = HasTarget ? (Vector2)target.position : ShotCenter + Vector2.down;
        previousIgnoreForces = controller.IgnoreExternalForces;
        ownsMovement = true;
        controller.IgnoreExternalForces = true;
        controller.Velocity = Vector2.zero;
        SetFlockRole(EnemyFlockRole.Chase);
        FaceTarget();
        UpdateDirections();
        if (!HasTarget || WorldEntityPool.Instance == null || settings.previewPrefab == null) return;
        for (int i = 0; i < 3; i++)
        {
            previews[i] = WorldEntityPool.Instance.Spawn(settings.previewPrefab, GetOrigin(i), Quaternion.identity);
            // 자동 반환을 끄고 이 패턴만 수명을 관리한다.
            previews[i].Show(GetOrigin(i), directions[i], settings.laserLength, settings.previewWidth,
                settings.blockMask, settings.previewColor, -1f);
        }
    }

    protected override void OnWindup(float elapsed)
    {
        controller.Velocity = Vector2.zero;
        if (!HasTarget) { ReturnEffects(); return; }
        AimLocked = elapsed >= Mathf.Max(0f, data.windupTime - settings.aimLockDuration);
        if (!AimLocked)
        {
            aimPoint = Vector2.MoveTowards(aimPoint, target.position, settings.aimPointSpeed * Time.deltaTime);
            UpdateDirections();
        }
        for (int i = 0; i < 3; i++)
        {
            if (previews[i] == null) continue;
            previews[i].SetOrigin(GetOrigin(i));
            previews[i].SetDirection(directions[i]);
            previews[i].UpdatePreviewVisual(Mathf.Max(0f, data.windupTime - elapsed), data.windupTime);
        }
    }

    private void UpdateDirections()
    {
        Vector2 delta = aimPoint - ShotCenter;
        float angle = delta.sqrMagnitude > .0001f ? Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg : -90f;
        for (int i = 0; i < 3; i++)
        {
            float radians = (angle + (i - 1) * settings.spreadAngle) * Mathf.Deg2Rad;
            directions[i] = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }
    }

    protected override void OnExecuteStart()
    {
        ReturnEffects();
        AimLocked = true;
        if (!HasTarget || settings.laserPrefab == null || WorldEntityPool.Instance == null) return;
        // 발사 직전에 다시 조준하지 않아 마지막 예고와 세 빔의 방향이 일치한다.
        for (int i = 0; i < 3; i++)
        {
            var beam = WorldEntityPool.Instance.Spawn(settings.laserPrefab, GetOrigin(i), Quaternion.identity);
            beams[i] = beam;
            beamVersions[i] = beam.SpawnVersion;
            beam.Fire(settings.damageInfo, GetOrigin(i), directions[i], settings.laserLength,
                data.executeTime, settings.blockMask, settings.damageMask, settings.laserWidth, controller);
            FiredCount++;
        }
    }

    protected override void OnExecute(float elapsed) => controller.Velocity = Vector2.zero;
    protected override void OnRecoverStart() => ReturnEffects();
    protected override void OnRecover(float elapsed) => controller.Velocity = Vector2.zero;

    protected override void OnStop()
    {
        ReturnEffects();
        RestoreMovement();
        controller.Velocity = Vector2.zero;
        SetFlockRole(EnemyFlockRole.None);
        ReserveNext(nextAbility);
    }

    private void ReturnEffects()
    {
        for (int i = 0; i < 3; i++)
        {
            if (previews[i] != null && previews[i].gameObject.activeInHierarchy) previews[i].ForceReturnToPool();
            previews[i] = null;
            GetBeam(i)?.ForceReturnToPool();
            beams[i] = null;
            beamVersions[i] = 0;
        }
    }

    private void RestoreMovement()
    {
        if (!ownsMovement || controller == null) return;
        controller.IgnoreExternalForces = previousIgnoreForces;
        ownsMovement = false;
    }

    private void OnDisable()
    {
        ReturnEffects();
        RestoreMovement();
    }
}
