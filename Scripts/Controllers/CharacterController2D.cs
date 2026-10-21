using UnityEngine;
using System;

public abstract class CharacterController2D : MonoBehaviour
{
    [Header("Faction")]
    public Faction Faction;

    [Header("Model")]
    public GameObject model;
    public GameObject facingModel;
    public bool LockFacing = false;
    public Animator animator;
    public AnimatorOverrideController overrideAnimator;

    [Header("Collision")]
    [SerializeField] protected Collider2D col;
    [SerializeField] protected bool floorDetectionSpeedControl = true;
    [SerializeField] protected LayerMask collisionLayer;
    [SerializeField] protected float skinWidth = 0.015f;
    [SerializeField][Range(2, 100)] protected int horizontalRayCount = 4;
    [SerializeField][Range(2, 100)] protected int verticalRayCount = 4;

    [Header("Velocity Setting")]
    [SerializeField] private float externalDamp = 15f; // 외부 힘 감쇠 속도
    [SerializeField] private float maxExternalSpeed = 20f;
    public Vector2 knockbackForce = Vector2.one;
    public Vector2 Velocity; // 입력한 속도
    public Vector2 externalForce; // 반동, 넉백 같은 외부 힘 누적

    public bool IsGrounded { get; protected set; }
    public bool IgnoreExternalForces { get; set; } = false; // 대시 중 등에 사용
    public int Facing { get; protected set; } = 1;

    public event Action OnWallHit;
    public event Action OnLanded;
    public event Action OnFalled;

    protected RaycastMotor2D motor;
    public RaycastMotor2D Motor => motor;

    private bool wasGrounded;
    protected virtual void Awake()
    {
        motor = gameObject.AddComponent<RaycastMotor2D>();
        motor.SetRaycastMotor2D(col, collisionLayer, skinWidth, horizontalRayCount, verticalRayCount);
    }
    public void AddRecoil(float power, int facing)
    {
        externalForce.x += -facing * power;
    }
    public void AddKnockback(Vector2 force)
    {
        externalForce += force;
    }
    protected void ColliderActive(bool value)
    {
        col.enabled = value;
    }
    private void FixedUpdate()
    {
        // 외부 힘 무시
        Vector2 finalExternal = IgnoreExternalForces ? Vector2.zero : externalForce;

        // 외부 힘 속도 제한
        finalExternal = Vector2.ClampMagnitude(finalExternal, maxExternalSpeed);

        // 최종 속도 계산
        Vector2 finalVelocity = CalculateVelocity() + finalExternal;
        Vector2 desiredMove = finalVelocity * Time.fixedDeltaTime;

        RaycastMoveResult2D result = motor.Move(desiredMove);

        transform.position += (Vector3)result.move;
        HandleCollisionResult(result.info);

        //외부 힘 감쇠
        externalForce = Vector2.MoveTowards(externalForce, Vector2.zero, externalDamp * Time.fixedDeltaTime);
    }

    /// <summary>
    /// 자식 클래스가 자신의 속도를 결정함
    /// </summary>
    /// 
    private void HandleCollisionResult(RaycastCollisionInfo2D info)
    {
        if ((info.up || info.down) && floorDetectionSpeedControl)
            Velocity.y = 0;

        bool groundedNow = info.down && Velocity.y <= 0;

        if (!wasGrounded && groundedNow)
            OnLanded?.Invoke();

        if (wasGrounded && !groundedNow)
            OnFalled?.Invoke();

        wasGrounded = groundedNow;
        IsGrounded = groundedNow;

        if (!LockFacing)
        {
            if (Velocity.x > 0.01f) Facing = 1;
            else if (Velocity.x < -0.01f) Facing = -1;

            facingModel.transform.localScale = new Vector3(Facing, 1, 1);
        }

        if (info.left || info.right)
            OnWallHit?.Invoke();
    }

    protected abstract Vector2 CalculateVelocity();
}
