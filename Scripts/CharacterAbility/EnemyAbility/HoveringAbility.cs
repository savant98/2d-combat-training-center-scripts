using UnityEngine;

public class HoveringAbility : AiAbility
{
    // Hover Settings
    private Vector2 desiredOffset = new(2.5f, 2.0f);
    private float maxHoverTime = 10f;

    // Desired Offset Switch
    private float sideSwitchCooldown = 1.5f;
    private float sideSwitchChance = 0.25f;

    // Speed Settings
    private float hoverSpeed = 5f;
    private float acceleration = 6f;
    private float jitterAmount = 0.3f;
    private float jitterSpeed = 2f;

    // Attack Settings
    [SerializeField] private AiAbility attackAbility;

    private float minAttackTime = 1.5f;
    private float attackHeightToTolerance = 4f;
    private float minAttackXDistance = 5f;
    private float maxAttackXDistance = 10f;

    private Transform target;
    private float hoverStartTime;
    private float lastSideSwitchTime;
    private int sideSign = 1;

    private bool attackReserved;

    private HoveringData hoveringData;

    private void Awake()
    {
        hoveringData = (HoveringData)data;

        desiredOffset = hoveringData.desiredOffset;
        maxHoverTime = hoveringData.maxHoverTime;

        sideSwitchCooldown = hoveringData.sideSwitchCooldown;
        sideSwitchChance = hoveringData.sideSwitchChance;

        hoverSpeed = hoveringData.hoverSpeed;
        acceleration = hoveringData.acceleration;
        jitterAmount = hoveringData.jitterAmount;
        jitterSpeed = hoveringData.jitterSpeed;

        minAttackTime = hoveringData.minAttackTime;
        attackHeightToTolerance = hoveringData.attackHeightToTolerance;
        minAttackXDistance = hoveringData.minAttackXDistance;
        maxAttackXDistance = hoveringData.maxAttackXDistance;
    }

    protected override void OnExecuteStart()
    {
        target = GameManager.Instance.Player.transform;

        hoverStartTime = Time.time;
        lastSideSwitchTime = Time.time;

        sideSign = Random.value < 0.5f ? -1 : 1;
        attackReserved = false;

        controller.Velocity = Vector2.zero;
    }

    protected override void OnExecute(float t)
    {
        if (target == null)
            return;

        FaceTarget();
        TrySwitchSide();
        Hovering();

        if (CanAttack() || IsMaxHoverTimeOver())
        {
            StartAttackAfterHover();
        }
    }

    protected override void OnRecoverStart()
    {
        controller.Velocity *= 0.3f;
    }

    protected override void OnRecover(float t)
    {
        controller.Velocity *= 0.95f;
    }

    protected override void OnStop()
    {
        attackReserved = false;
    }

    private void Hovering()
    {
        Vector2 baseOffset = new Vector2(
            desiredOffset.x * sideSign,
            desiredOffset.y);

        Vector2 baseTargetPos = (Vector2)target.position + baseOffset;

        Vector2 jitter = new Vector2(
            Mathf.Sin(Time.time * jitterSpeed),
            Mathf.Cos(Time.time * jitterSpeed * 1.3f)
        ) * jitterAmount;

        Vector2 finalTarget = baseTargetPos + jitter;

        Vector2 toTarget = finalTarget - (Vector2)transform.position;

        Vector2 desiredVelocity =
            toTarget.normalized * hoverSpeed;

        controller.Velocity = Vector2.Lerp(
            controller.Velocity,
            desiredVelocity,
            Time.deltaTime * acceleration
        );
    }

    private void TrySwitchSide()
    {
        if (Time.time < lastSideSwitchTime + sideSwitchCooldown)
            return;

        if (Random.value < sideSwitchChance)
        {
            sideSign *= -1;
            lastSideSwitchTime = Time.time;
        }
    }

    private bool CanAttack()
    {
        if (target == null)
            return false;

        Vector2 diff = transform.position - target.position;

        bool xOk =
            Mathf.Abs(diff.x) >= minAttackXDistance &&
            Mathf.Abs(diff.x) <= maxAttackXDistance;

        bool yOk =
            diff.y >= 0f &&
            diff.y <= attackHeightToTolerance;

        bool timeOk =
            Time.time - hoverStartTime >= minAttackTime;

        return xOk && yOk && timeOk;
    }

    private bool IsMaxHoverTimeOver()
    {
        return Time.time - hoverStartTime >= maxHoverTime;
    }

    private void StartAttackAfterHover()
    {
        if (attackReserved)
            return;

        attackReserved = true;

        if (attackAbility != null)
            brain.ForceNextAbility(attackAbility);

        controller.Velocity *= 0.3f;

        ForceRecover();
    }

    private void FaceTarget()
    {
        if (target == null)
            return;

        float dx = target.position.x - transform.position.x;

        if (Mathf.Abs(dx) <= 0.001f)
            return;

        controller.FacingUpdate(dx > 0f ? 1 : -1);
    }
}