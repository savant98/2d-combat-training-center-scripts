using System.Collections.Generic;
using UnityEngine;

// 이동 전 어댑터는 Velocity만 공급하고 실제 이동 뒤에는 같은 모터로 접촉을 검사한다.
[DefaultExecutionOrder(100)]
public class FlyingBombChargeAbility : FlyingAbility
{
    [SerializeField] private AiAbility nextAbility;
    private FlyingBombChargeDataSO chargeData;
    private Transform castOrigin;
    private Vector2 previousPosition;
    private float previousAngle;
    private Vector2 chargeDirection;
    private Vector2 retreatDirection;
    private Vector2 recoveryDirection;
    private Vector2 retreatStart;
    private float retreatElapsed;
    private float scheduledRetreatStep;
    private bool savedIgnoreExternal;
    private bool ownsMotion;
    private ContactFilter2D environmentFilter;
    private ContactFilter2D contactFilter;
    private ContactFilter2D playerFilter;
    private readonly List<RaycastHit2D> pathHits = new(16);
    private readonly List<Collider2D> overlaps = new(16);
    private readonly HashSet<PlayerHealth> damaged = new();
    public bool HasDetonated { get; private set; }
    public Vector2 ChargeDirection => chargeDirection;
    public Vector2 RetreatDirection => retreatDirection;
    public float RetreatElapsed => retreatElapsed;
    public Vector2 LastRetreatDisplacement { get; private set; }
    public Vector2 RetreatPoint => retreatStart + retreatDirection * chargeData.retreatDistance;
    public float ExplosionRadius => chargeData.explosionDiameter * 0.5f;
    public bool IsPreparing => State == AbilityState.Windup;
    public bool IsRepositioning => State == AbilityState.Windup;
    public bool IsReloading => State == AbilityState.Recover && HasDetonated;

    protected override void Awake()
    {
        base.Awake();
        chargeData = (FlyingBombChargeDataSO)data;
        var body = GetComponent<EnemyController>().Health.GetComponent<Collider2D>();
        castOrigin = body.attachedRigidbody != null ? body.attachedRigidbody.transform : body.transform;
        contactFilter.SetLayerMask(LayerMask.GetMask("Player", "Platform"));contactFilter.useTriggers = true;
        playerFilter.SetLayerMask(LayerMask.GetMask("Player"));playerFilter.useTriggers = true;
        environmentFilter.SetLayerMask(LayerMask.GetMask("Platform"));environmentFilter.useTriggers = true;
    }

    public override bool CanBegin()
    {
        AcquireTarget();
        return HasTarget && ((Vector2)target.position - (Vector2)transform.position).sqrMagnitude
            <= chargeData.detectionDistance * chargeData.detectionDistance;
    }

    protected override void OnWindupStart()
    {
        ReleaseMotion();
        HasDetonated = false;
        retreatElapsed = 0f;scheduledRetreatStep = 0f;LastRetreatDisplacement = Vector2.zero;
        AcquireTarget();
        SetFlockRole(EnemyFlockRole.Chase);
        retreatStart = transform.position;
        Vector2 away = HasTarget ? retreatStart - (Vector2)target.position : Vector2.up;
        retreatDirection = ResolveRetreatDirection(away);
        savedIgnoreExternal = controller.IgnoreExternalForces;ownsMotion = true;controller.IgnoreExternalForces = true;
        controller.CanTouchDamageActive(false);
        controller.Velocity = retreatDirection * (chargeData.retreatDistance / chargeData.retreatDuration);
        animator.Play("Base Layer.Windup", 0, 0f);
        feedbackPlayer.Play(chargeData.preparationFeedback, windupPoint ? windupPoint : transform);
    }

    protected override void OnWindup(float t) => FaceTarget();
    protected override bool CanFinishWindup(float elapsed) => retreatElapsed >= chargeData.retreatDuration - 0.00001f;

    // 시작점을 잠가 Player 이동·기존 거리와 무관하게 같은 이동량을 사용한다.
    private Vector2 ResolveRetreatDirection(Vector2 away)
    {
        float x = Mathf.Abs(away.x) > .01f ? Mathf.Sign(away.x) : controller.Facing;
        float y = away.y >= 0f ? 1f : -1f;
        Vector2 fallback = new Vector2(x * .6f, y * .8f);
        float bestFree = -1f;
        RaycastMotor2D.PrepareAttackQueries();
        for (int pass = 0; pass < 2; pass++)
        for (int vertical = 0; vertical < 2; vertical++)
        for (int horizontal = 0; horizontal < 2; horizontal++)
        for (int slope = 0; slope < 3; slope++)
        {
            float vx = slope == 0 ? .6f : slope == 1 ? .35f : 0f;
            Vector2 direction = new(vx * x * (horizontal == 0 ? 1 : -1), Mathf.Sqrt(1f-vx*vx) * y * (vertical == 0 ? 1 : -1));
            Vector2 move = direction * chargeData.retreatDistance;
            if ((away + move).sqrMagnitude < away.sqrMagnitude) continue;
            if (pass == 0 && Mathf.Abs(away.y + move.y) < chargeData.minimumVerticalSeparation) continue;
            controller.Motor.OverlapAttackCollider((Vector2)castOrigin.position + move, castOrigin.eulerAngles.z, environmentFilter, overlaps);
            controller.Motor.CastAttackPath(castOrigin.position, castOrigin.eulerAngles.z, move, environmentFilter, pathHits);
            float free = chargeData.retreatDistance;
            foreach (var hit in pathHits) free = Mathf.Min(free, hit.distance);
            if (overlaps.Count == 0 && pathHits.Count == 0) return direction;
            if (free > bestFree) { bestFree = free;fallback = direction; }
        }
        // 모든 방향이 막힌 경우에도 정지 대기를 만들지 않는다. 실제 지형 충돌은 기존 모터가 우선한다.
        return fallback;
    }

    // CharacterController2D보다 먼저 호출한다. 마지막 부분 틱까지 적분해 0.5초·고정 이동량을 맞춘다.
    public void PrepareMotorStep()
    {
        if (brain == null || brain.IsPaused || !IsRunning) return;
        if (State == AbilityState.Windup)
        {
            scheduledRetreatStep = Mathf.Min(Time.fixedDeltaTime, Mathf.Max(0, chargeData.retreatDuration-retreatElapsed));
            controller.Velocity = retreatDirection * (chargeData.retreatDistance / chargeData.retreatDuration)
                * (scheduledRetreatStep / Time.fixedDeltaTime);
        }
        else if (State == AbilityState.Execute) controller.Velocity = chargeDirection * chargeData.chargeSpeed;
        else if (State == AbilityState.Recover) controller.Velocity = recoveryDirection * chargeData.recoverySpeed;
    }

    protected override void OnExecuteStart()
    {
        LastRetreatDisplacement = (Vector2)transform.position-retreatStart;
        chargeDirection = HasTarget ? ((Vector2)target.position-(Vector2)transform.position).normalized : -retreatDirection;
        if (chargeDirection.sqrMagnitude < .001f) chargeDirection = -retreatDirection;
        previousPosition = castOrigin.position;previousAngle = castOrigin.eulerAngles.z;
        controller.Velocity = chargeDirection * chargeData.chargeSpeed;
        // 후퇴가 끝난 물리 틱에서 발진 애니메이션을 즉시 시작한다.
        animator.Play("Base Layer.Execute", 0, 0f);
    }
    protected override void OnExecute(float t) { }

    private void FixedUpdate()
    {
        if (brain == null || brain.IsPaused) return;
        if (State == AbilityState.Windup)
        {
            retreatElapsed = Mathf.Min(chargeData.retreatDuration, retreatElapsed + scheduledRetreatStep);
            scheduledRetreatStep = 0f;
            if (CanFinishWindup(0)) UpdateAbility();
            return;
        }
        if (State != AbilityState.Execute || HasDetonated) return;
        RaycastMotor2D.PrepareAttackQueries();
        Vector2 position = castOrigin.position;
        controller.Motor.OverlapAttackCollider(position, castOrigin.eulerAngles.z, contactFilter, overlaps);
        bool contact = overlaps.Count > 0;
        if (!contact)
        {
            controller.Motor.CastAttackPath(previousPosition, previousAngle, position-previousPosition+chargeDirection*.035f, contactFilter, pathHits);
            contact = pathHits.Count > 0;
        }
        previousPosition=position;previousAngle=castOrigin.eulerAngles.z;
        if(contact) Detonate();
    }

    private void Detonate()
    {
        if (HasDetonated) return;
        HasDetonated=true;
        Vector3 center=transform.position;damaged.Clear();
        controller.Motor.OverlapAttackCircle(center,ExplosionRadius,playerFilter,overlaps);
        var damage=new WeaponBaseSO.DamageInfo{damage=chargeData.explosionDamage,damageType=DamageType.Normal};
        foreach(var hit in overlaps)
        {
            var health=hit.GetComponentInParent<PlayerHealth>();
            if(health!=null&&damaged.Add(health))health.TakeDamage(damage,center);
        }
        feedbackPlayer.PlayExact(chargeData.explosionFeedback,center,Quaternion.identity);
        ForceRecover();
    }

    protected override void OnRecoverStart()
    {
        ReleaseMotion();
        recoveryDirection=-chargeDirection;
        if(recoveryDirection.sqrMagnitude<.001f)recoveryDirection=retreatDirection;
        RefreshRecoveryDirection();
        controller.Velocity=recoveryDirection*chargeData.recoverySpeed;
        animator.Play("Base Layer.Recover",0,0f);
    }
    protected override void OnRecover(float t) => RefreshRecoveryDirection();
    private void RefreshRecoveryDirection()
    {
        RaycastMotor2D.PrepareAttackQueries();
        controller.Motor.CastAttackPath(castOrigin.position,castOrigin.eulerAngles.z,recoveryDirection*.7f,environmentFilter,pathHits);
        if(pathHits.Count==0)return;
        Vector2 best=recoveryDirection;float distance=-1f;
        for(int i=0;i<8;i++)
        {
            float a=i*Mathf.PI*.25f;Vector2 direction=new(Mathf.Cos(a),Mathf.Sin(a));
            controller.Motor.CastAttackPath(castOrigin.position,castOrigin.eulerAngles.z,direction*.7f,environmentFilter,pathHits);
            float free=.7f;foreach(var hit in pathHits)free=Mathf.Min(free,hit.distance);
            if(free>distance){distance=free;best=direction;}
        }
        recoveryDirection=best;
    }
    protected override bool CanFinishRecover(float elapsed)=>elapsed>=(HasDetonated?data.recoverTime:.2f);
    private void ReleaseMotion(){if(!ownsMotion)return;controller.IgnoreExternalForces=savedIgnoreExternal;ownsMotion=false;}
    private void OnDisable() => ReleaseMotion();
    protected override void OnStop()
    {
        ReleaseMotion();
        if(brain!=null&&brain.IsPaused)controller.Velocity=Vector2.zero;
        SetFlockRole(EnemyFlockRole.None);
        ReserveNext(nextAbility);
    }
}
