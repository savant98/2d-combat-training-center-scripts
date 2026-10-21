using UnityEngine;

public class DroneEnemyBullet : ProjectileBase
{
    [SerializeField] private float fireConeAngle = 45f;

    private Transform targetTr;
    private Transform ownerTr;

    private Vector2 targetDir;
    private Vector2 ownerForward;
    public override void Initialize(CharacterController2D owner, WeaponBase.DamageInfo damageInfo, float speed, int count, LayerMask collideLayer)
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
