using UnityEngine;
using System.Collections;

public class MovingEntityBase : WorldEntityBase
{
    [Header("Physics Settings")]
    [SerializeField] private float skinWidth = 0.01f;
    [SerializeField][Range(4, 8)] private int horizontalRayCount = 4;
    [SerializeField][Range(4, 8)] private int verticalRayCount = 4;

    protected LayerMask collideLayer;
    protected float speed;
    protected RaycastMotor2D motor;
    protected Vector2 velocity;

    private bool ignoreRaycastCheck = true;

    protected override void Awake()
    {
        base.Awake();

        motor = gameObject.AddComponent<RaycastMotor2D>();
        motor.SetRaycastMotor2D(col, collideLayer, skinWidth, horizontalRayCount, verticalRayCount);
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        ignoreRaycastCheck = true;
        StartCoroutine(EnableGroundCheckNextFrame());
    }

    private IEnumerator EnableGroundCheckNextFrame()
    {
        yield return new WaitForFixedUpdate();
        ignoreRaycastCheck = false;
    }

    public virtual void Initialize(
        CharacterController2D owner,
        WeaponBase.DamageInfo damageInfo,
        float speed,
        int count,
        LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, count);

        this.speed = speed;
        this.collideLayer = collideLayer;

        if (motor != null)
            motor.SetCollideLayer(collideLayer);
    }

    protected virtual void FixedUpdate()
    {
        if (ignoreRaycastCheck)
            return;

        Vector2 move = velocity * Time.fixedDeltaTime;
        RaycastMoveResult2D result = motor.Move(move);

        transform.position += (Vector3)result.move;
        HandleCollision(result.info);
    }

    protected virtual void HandleCollision(RaycastCollisionInfo2D info)
    {
        if (info.left || info.right || info.up || info.down)
            OnHitSomething();
    }

    protected virtual void OnHitSomething() {}
}
