using UnityEngine;

// 거리를 유지하던 비행 적이 플레이어에게 다가가는 추적 패턴이다. 돌진이나 예고 효과 없이 비행 속도(플레이어 이동 속도 × 비율)로 접근한다.
// 닿거나(contactDistance) 추적 시간이 끝나면 Recover 동안 플레이어 반대쪽으로 물러난 뒤 다음 패턴(거리 유지)을 예약한다.
// 추적 중에도 같은 무리와 서로 밀어내므로 여러 마리가 플레이어의 한 점에 겹치지 않고 둘러싼다.
public class FlyingChaseAbility : FlyingAbility
{
    [Tooltip("추적이 끝난 뒤 실행할 패턴입니다(Swarmer는 FlyingPatrolAbility).")]
    [SerializeField] private AiAbility nextAbility;

    private FlyingChaseDataSO chaseData;
    private float chaseDuration;
    private float contactTime = -1f;

    public float ChaseDuration => chaseDuration;
    public bool HasTouched => contactTime >= 0f;

    protected override void Awake()
    {
        base.Awake();
        chaseData = (FlyingChaseDataSO)data;
    }

    protected override void OnWindupStart()
    {
        AcquireTarget();
        BeginFlight();
        chaseDuration = Random.Range(chaseData.chaseDuration.x, chaseData.chaseDuration.y);
        contactTime = -1f;
        SetFlockRole(EnemyFlockRole.Chase);
    }

    protected override void OnWindup(float t) => Approach();
    protected override void OnExecute(float t) => Approach();

    protected override bool CanFinishExecute(float elapsed)
    {
        if (!HasTarget || elapsed >= chaseDuration)
            return true;

        return chaseData.endOnContact && contactTime >= 0f && Time.time - contactTime >= chaseData.contactHoldTime;
    }

    protected override void OnRecover(float t) => Retreat();

    protected override void OnStop()
    {
        SetFlockRole(EnemyFlockRole.None);
        ReserveNext(nextAbility);
    }

    private void Approach()
    {
        if (!HasTarget)
        {
            Hover();
            return;
        }

        FaceTarget();
        Vector2 player = target.position;
        float contactDistance = chaseData.contactDistance;
        if (contactTime < 0f && ((Vector2)transform.position - player).sqrMagnitude <= contactDistance * contactDistance)
            contactTime = Time.time;

        SteerTowards(player + UpdateWander(), MaxSpeed);
    }

    // 플레이어 반대쪽 retreatDistance 지점으로 물러난다. 대상이 없으면 제자리에서 감속한다.
    private void Retreat()
    {
        if (!HasTarget || chaseData.retreatDistance <= 0f)
        {
            Hover();
            return;
        }

        Vector2 position = transform.position;
        Vector2 away = position - (Vector2)target.position;
        Vector2 direction = away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.up;
        SteerTowards(position + direction * chaseData.retreatDistance, MaxSpeed);
    }
}
