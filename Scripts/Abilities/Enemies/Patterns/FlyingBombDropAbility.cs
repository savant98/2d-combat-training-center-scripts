using UnityEngine;

// 수직 정렬을 감지해 폭탄만 놓는다. 투하 때 잠깐 급감속하고 재장착부터 비행을 재개한다.
public class FlyingBombDropAbility : FlyingAbility
{
    [SerializeField] private AiAbility nextAbility;
    [SerializeField] private Transform dropPoint;
    private FlyingBombDropDataSO dropData;
    private int flightSide;
    private float brakeStartedAt;
    public bool IsBraking => State == AbilityState.Windup || State == AbilityState.Execute;
    public float BrakeElapsed => Time.time - brakeStartedAt;
    public bool Released { get; private set; }
    public BomberFallingBomb LastBomb { get; private set; }
    public Vector3 LastReleasePosition { get; private set; }

    protected override void Awake() { base.Awake(); dropData = (FlyingBombDropDataSO)data; }
    public override bool CanBegin()
    {
        AcquireTarget();
        if (!HasTarget || dropData.bombPrefab == null || !IsReady()) return false;
        Vector2 point = dropPoint != null ? dropPoint.position : transform.position;
        Vector2 difference = point - (Vector2)target.position;
        return Mathf.Abs(difference.x) <= dropData.alignmentTolerance
            && difference.y >= dropData.minimumDropHeight && difference.y <= dropData.maximumDropHeight;
    }
    protected override void OnWindupStart()
    {
        Released = false; LastBomb = null;
        AcquireTarget(); SetFlockRole(EnemyFlockRole.Chase);
        flightSide = Mathf.Abs(controller.Velocity.x) > .1f ? (int)Mathf.Sign(controller.Velocity.x) : controller.Facing;
        brakeStartedAt = Time.time;
        Brake();
        animator.Play("Base Layer.Windup", 0, 0f);
    }
    protected override void OnWindup(float t) => Brake();
    protected override void OnExecuteStart()
    {
        animator.Play("Base Layer.Execute", 0, 0f);
        if (!HasTarget || Released || WorldEntityPool.Instance == null || dropData.bombPrefab == null) return;
        LastReleasePosition = dropPoint != null ? dropPoint.position : transform.position + Vector3.down * .65f;
        LastBomb = WorldEntityPool.Instance.Spawn(dropData.bombPrefab, LastReleasePosition, Quaternion.identity);
        LastBomb.Initialize(controller, dropData.damageInfo, 0f, 0, dropData.bombCollideLayer);
        Released = true;
    }
    protected override void OnExecute(float t) => Brake();
    protected override bool CanFinishExecute(float t) => BrakeElapsed >= dropData.dropBrakeDuration - .0001f;
    protected override void OnRecoverStart()
    {
        BeginFlight();
        animator.Play("Base Layer.Recover", 0, 0f);
    }
    protected override void OnRecover(float t) => Cruise();
    protected override void OnStop()
    {
        if (brain != null && brain.IsPaused) controller.Velocity = Vector2.zero;
        var rack = GetComponent<BomberBombRack>(); if (rack != null) rack.ResetPose();
        if (animator != null && animator.isActiveAndEnabled) animator.Play("Base Layer.Idle", 0, 0f);
        SetFlockRole(EnemyFlockRole.None);
        ReserveNext(nextAbility);
    }
    private void Brake()
    {
        // 공용 조향의 군집 분리 속도를 더하지 않아 밀집 상태에서도 감속이 유지된다.
        Vector2 creep = Vector2.right * flightSide * (MaxSpeed * dropData.dropBrakeSpeedRatio);
        controller.Velocity = Vector2.MoveTowards(controller.Velocity, creep, dropData.dropBrakeDeceleration * Time.deltaTime);
    }
    private void Cruise()
    {
        if (!HasTarget) AcquireTarget();
        FaceTarget();
        Vector2 position = transform.position;
        if (controller.Motor.RaycastEnvironment(position, Vector2.right * flightSide, 1.1f).collider != null) flightSide = -flightSide;
        float height = HasTarget ? target.position.y + dropData.cruiseHeight : position.y;
        SteerTowards(new Vector2(position.x + flightSide * 3f, height) + UpdateWander(), MaxSpeed);
    }
}
