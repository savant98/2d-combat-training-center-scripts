using UnityEngine;

public class DroneEnemyBullet : ProjectileBase
{
    [Tooltip("적이 바라보는 방향에서 탄이 벗어날 수 있는 최대 각도(도)입니다. 180이면 모든 방향으로 쏩니다. Drone은 쏠 때 FlyingShootDataSO의 사격 각도로 바꿉니다.")]
    [SerializeField, Range(0f, 180f)] private float fireConeAngle = 45f;

    // 쏘는 적이 Initialize 전에 사격 각도를 정한다(FlyingShootAbility). 사격 시작 조건과 같은 값을 쓰게 한다.
    public float FireConeAngle
    {
        get => fireConeAngle;
        set => fireConeAngle = Mathf.Clamp(value, 0f, 180f);
    }

    private Transform targetTr;
    private Transform ownerTr;

    private Vector2 targetDir;
    private Vector2 ownerForward;
    public override void Initialize(CharacterController2D owner, WeaponBaseSO.DamageInfo damageInfo, float speed, int count, LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);

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

        velocity = targetDir * speed;
    }
}
