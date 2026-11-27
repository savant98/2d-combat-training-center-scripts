using UnityEngine;

public class DroneEnemyBullet : ProjectileBase
{
    [Tooltip("적이 바라보는 방향에서 탄이 벗어날 수 있는 최대 각도(도)입니다. 180이면 모든 방향으로 쏩니다. Drone은 쏠 때 FlyingShootDataSO의 사격 각도로 바꿉니다.")]
    [SerializeField, Range(0f, 180f)] private float fireConeAngle = 45f;

    [Tooltip("비행 중 4각 별 코어 비주얼(DroneCoreVisual)의 초당 회전 각도(도)입니다.")]
    [SerializeField, Min(0f)] private float visualSpinSpeed = 540f;

    // 쏘는 적이 Initialize 전에 사격 각도·최대 이동 거리를 정한다(FlyingShootAbility).
    public float FireConeAngle
    {
        get => fireConeAngle;
        set => fireConeAngle = Mathf.Clamp(value, 0f, 180f);
    }

    public float MaxTravelDistance
    {
        get => maxTravelDistance;
        set => maxTravelDistance = Mathf.Max(0f, value);
    }

    public float TraveledDistance => traveledDistance;
    public float VisualSpinSpeed => visualSpinSpeed;
    public float VisualRotationZ => visualTransform != null ? visualTransform.localEulerAngles.z : 0f;

    private float maxTravelDistance;
    private float traveledDistance;
    private Transform targetTr;
    private Transform ownerTr;
    private Transform visualTransform;
    private float visualSpinAngle;
    private float spinDirection = -1f;

    private Vector2 targetDir;
    private Vector2 ownerForward;

    private void EnsureVisualTransform()
    {
        if (visualTransform == null)
            visualTransform = transform.Find("DroneCoreVisual");
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        maxTravelDistance = 0f;
        traveledDistance = 0f;
        EnsureVisualTransform();
        visualSpinAngle = 0f;
        if (visualTransform != null)
            visualTransform.localRotation = Quaternion.identity;
    }

    public override void Initialize(CharacterController2D owner, WeaponBaseSO.DamageInfo damageInfo, float speed, int count, LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);
        traveledDistance = 0f;
        EnsureVisualTransform();
        visualSpinAngle = 0f;
        if (visualTransform != null)
            visualTransform.localRotation = Quaternion.identity;

        targetTr = GameManager.Instance.Player.transform;
        ownerTr = owner.transform;

        // 적 기준 → 플레이어 방향
        Vector2 desiredDir =
            ((Vector2)targetTr.position - (Vector2)ownerTr.position).normalized;

        // 적의 정면
        ownerForward = new Vector2(owner.Facing, 0);

        float signedAngle = Vector2.SignedAngle(ownerForward, desiredDir);

        // 정면 기준 Clamp
        signedAngle = Mathf.Clamp(signedAngle, -fireConeAngle, fireConeAngle);

        // 최종 발사 방향 확정 (한 번만)
        targetDir = (Vector2)(Quaternion.Euler(0f, 0f, signedAngle) * ownerForward).normalized;
        spinDirection = targetDir.x >= 0f ? -1f : 1f;

        velocity = targetDir * speed;
    }

    private void LateUpdate()
    {
        if (isReturning || visualSpinSpeed <= 0f) return;
        EnsureVisualTransform();
        if (visualTransform == null) return;

        visualSpinAngle = Mathf.Repeat(visualSpinAngle + spinDirection * visualSpinSpeed * Time.deltaTime, 360f);
        visualTransform.localRotation = Quaternion.Euler(0f, 0f, visualSpinAngle);
    }

    protected override void FixedUpdate()
    {
        Vector2 before = transform.position;
        base.FixedUpdate();
        if (!gameObject.activeSelf || isReturning || maxTravelDistance <= 0f) return;

        traveledDistance += Vector2.Distance(before, (Vector2)transform.position);
        if (traveledDistance >= maxTravelDistance)
            ForceReturnToPool();
    }
}
