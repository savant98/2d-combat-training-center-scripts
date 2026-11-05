using UnityEngine;

// 비행 패턴의 공통 이동이다. 이동은 EnemyController.Velocity만 바꾸고, 실제 이동과 충돌은 기존 RaycastMotor2D가 처리한다.
// - 도착(Arrival): 목표점에 가까워질수록 감속해 지나쳤다 돌아오는 진동 없이 멈춘다(Reynolds, Steering Behaviors 1999).
// - 흔들림: 개체마다 정해진 간격으로 새 난수 위치를 고르고 그 위치를 Lerp로 따라가 파리처럼 조금씩 움직인다.
// - 분리: 같은 무리(EnemyFlockMember)의 구성원끼리 가까울수록 강하게 밀어내 한 점에 겹치지 않게 한다.
// 도착·흔들림 속도는 가속도로 제한해 넉백(외부 힘)을 받은 뒤에도 자연스럽게 원래 경로로 돌아온다.
// 분리 속도는 현재 위치로 바로 정해지므로 가속도 제한 없이 더한다. 빠르게 엇갈려도 제때 비켜 간다.
[RequireComponent(typeof(EnemyFlockMember))]
public abstract class FlyingAbility : AiAbility
{
    protected FlyingAbilityDataSO flightData;
    protected EnemyFlockMember flockMember;
    protected Transform target;

    private MoveAbility playerMove;
    private Vector2 steerVelocity;
    private Vector2 wanderOffset;
    private Vector2 wanderGoal;
    private float nextWanderTime;

    protected EnemyFlock Flock => flockMember != null ? flockMember.Flock : null;
    protected bool HasTarget => target != null && target.gameObject.activeInHierarchy;

    // 최고 비행 속도(칸/초)다. 플레이어 이동 속도를 따라가며, 읽을 수 없으면 데이터의 기본 속도를 쓴다.
    public float MaxSpeed => playerMove != null
        ? playerMove.MoveSpeed * flightData.playerSpeedRatio
        : flightData.fallbackSpeed;

    protected virtual void Awake()
    {
        flightData = (FlyingAbilityDataSO)data;
        flockMember = GetComponent<EnemyFlockMember>();
    }

    // 패턴을 시작할 때 플레이어와 이동 속도를 다시 읽는다. Pool 재사용이나 플레이어 재생성 뒤에도 현재 대상을 쓴다.
    protected void AcquireTarget()
    {
        PlayerController player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        target = player != null ? player.transform : null;
        playerMove = player != null ? player.GetComponent<MoveAbility>() : null;
    }

    protected void SetFlockRole(EnemyFlockRole role)
    {
        if (flockMember != null)
            flockMember.SetRole(role);
    }

    // 패턴을 시작할 때 현재 속도에서 이어서 조향한다. 흔들림은 처음부터 다시 고른다.
    protected void BeginFlight()
    {
        steerVelocity = controller.Velocity;
        wanderOffset = Vector2.zero;
        wanderGoal = Vector2.zero;
        nextWanderTime = 0f;
    }

    // 파리 같은 흔들림: 간격마다 범위 안의 난수 위치를 새로 고르고, 현재 흔들림 위치가 그 위치를 Lerp로 따라간다.
    protected Vector2 UpdateWander()
    {
        if (Time.time >= nextWanderTime)
        {
            Vector2 unit = Random.insideUnitCircle;
            wanderGoal = new Vector2(unit.x * flightData.wanderRadius.x, unit.y * flightData.wanderRadius.y);
            nextWanderTime = Time.time + Random.Range(flightData.wanderInterval.x, flightData.wanderInterval.y);
        }

        float follow = 1f - Mathf.Exp(-flightData.wanderLerpSpeed * Time.deltaTime);
        wanderOffset = Vector2.Lerp(wanderOffset, wanderGoal, follow);
        return wanderOffset;
    }

    // 도착 속도를 가속도 제한 안에서 따라가고, 분리 속도를 더해 Velocity를 정한다.
    protected void SteerTowards(Vector2 point, float maxSpeed)
    {
        Vector2 toPoint = point - (Vector2)transform.position;
        float distance = toPoint.magnitude;
        Vector2 desired = Vector2.zero;
        if (distance > 0.0001f)
        {
            float speed = Mathf.Min(maxSpeed, maxSpeed * distance / flightData.slowingDistance);
            desired = toPoint / distance * speed;
        }

        ApplyVelocity(desired);
    }

    // 목표 없이 제자리에서 감속한다. 분리는 계속 적용한다.
    protected void Hover() => ApplyVelocity(Vector2.zero);

    protected void FaceTarget()
    {
        if (!HasTarget)
            return;

        float dx = target.position.x - transform.position.x;
        if (Mathf.Abs(dx) > 0.01f)
            controller.FacingUpdate(dx > 0f ? 1 : -1);
    }

    // 거리 유지(FlyingPatrolAbility)가 이 패턴을 예약하기 전에 묻는다. 사거리·사격선처럼 시작 조건이 있는 패턴만 override한다.
    // false인 동안 거리 유지가 이어지고, 유지 시간이 지난 뒤 조건을 채우는 순간 넘어온다.
    public virtual bool CanBegin() => true;

    // 다음 패턴을 예약한다. 등장·사망·Freeze로 AiBrain이 정지해 멈춘 경우에는 예약하지 않는다.
    protected void ReserveNext(AiAbility next)
    {
        if (next != null && brain != null && !brain.IsPaused)
            brain.ForceNextAbility(next);
    }

    private void ApplyVelocity(Vector2 desired)
    {
        steerVelocity = Vector2.MoveTowards(steerVelocity, desired, flightData.acceleration * Time.deltaTime);
        controller.Velocity = steerVelocity + Separation();
    }

    private Vector2 Separation()
    {
        EnemyFlock flock = Flock;
        return flock != null
            ? flock.GetSeparation(flockMember, flightData.separationDistance, flightData.separationStrength, flightData.separationSidestep)
            : Vector2.zero;
    }
}
