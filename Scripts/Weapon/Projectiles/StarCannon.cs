using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

public class StarCannon : ProjectileBase
{
    private Vector2 localMoveDir;
    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        Vector2 worldDir = transform.TransformDirection(localMoveDir);

        velocity = worldDir * speed;
    }
    public override void Initialize(CharacterController2D owner, WeaponBase.DamageInfo damageInfo, float speed, int count, LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);
        localMoveDir = new Vector2(owner.Facing, -0.5f).normalized;

        transform.rotation = Quaternion.Euler(0f, 0f, 120f * owner.Facing);
        transform.DORotate(new Vector3(0f, 0f, count * -10f * owner.Facing), 0.3f);
    }
}
