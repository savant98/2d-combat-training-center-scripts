using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(100)]
public sealed class ConstructorGroundWave : WorldEntityBase
{
    [Header("Wave")]
    [SerializeField] private ConstructorWaveSegment segmentPrefab;
    [SerializeField, Min(0.1f)] private float travelSpeed = 11f;
    [SerializeField, Min(0.1f)] private float segmentSpacing = 0.95f;
    [SerializeField, Min(0.1f)] private float segmentLifetime = 0.55f;
    [SerializeField, Min(0.1f)] private float segmentHeight = 2.4f;
    [SerializeField, Min(0.1f)] private float maxTravelTime = 6f;
    [SerializeField, Min(0.1f)] private float floorProbeUp = 1f;
    [SerializeField, Min(0.1f)] private float floorProbeDown = 2f;

    public int SpawnedSegmentCount { get; private set; }
    public bool LeftComplete => !leftActive;
    public bool RightComplete => !rightActive;
    public Vector2 LeftFront => leftFront;
    public Vector2 RightFront => rightFront;
    public float Age => age;

    private RaycastMotor2D motor;
    private bool initialized;
    private bool leftActive;
    private bool rightActive;
    private Vector2 leftFront;
    private Vector2 rightFront;
    private float distanceBudget;
    private float age;
    private float lastSegmentAt;
    private HashSet<HealthBase> waveHitTargets;
    private readonly List<ConstructorWaveSegment> segments = new List<ConstructorWaveSegment>(48);

    protected override void Awake()
    {
        base.Awake();
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();
        if (col != null) col.enabled = false;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        initialized = leftActive = rightActive = false;
        age = distanceBudget = lastSegmentAt = 0f;
        SpawnedSegmentCount = 0;
        segments.Clear();
        waveHitTargets = null;
    }

    public void InitializeWave(CharacterController2D owner, WeaponBaseSO.DamageInfo damage, LayerMask platformLayer)
    {
        base.Initialize(owner, damage, 0);
        motor.SetCollideLayer(platformLayer);
        RaycastMotor2D.InvalidateAttackQuerySync();
        RaycastMotor2D.PrepareAttackQueries();
        var floor = motor.RaycastEnvironment((Vector2)transform.position + Vector2.up * floorProbeUp,
            Vector2.down, floorProbeUp + floorProbeDown);
        if (floor.collider == null || segmentPrefab == null || WorldEntityPool.Instance == null)
        {
            ReturnToPool();
            return;
        }
        // 대여마다 새 문맥을 사용해 이전 파도의 잔여 Segment와 다음 파도를 분리한다.
        waveHitTargets = new HashSet<HealthBase>();
        leftFront = rightFront = floor.point;
        leftActive = rightActive = initialized = true;
        SpawnSegment(floor.point);
    }

    private void FixedUpdate()
    {
        if (!initialized || isReturning) return;
        age += Time.fixedDeltaTime;
        if (age >= maxTravelTime) leftActive = rightActive = false;
        distanceBudget += travelSpeed * Time.fixedDeltaTime;
        RaycastMotor2D.PrepareAttackQueries();
        while (distanceBudget >= segmentSpacing && (leftActive || rightActive))
        {
            distanceBudget -= segmentSpacing;
            if (leftActive) AdvanceFront(ref leftFront, -1, ref leftActive);
            if (rightActive) AdvanceFront(ref rightFront, 1, ref rightActive);
        }
        if (!leftActive && !rightActive && age >= lastSegmentAt + segmentLifetime + Time.fixedDeltaTime)
            ReturnToPool();
    }

    private void AdvanceFront(ref Vector2 front, int direction, ref bool active)
    {
        // 파도 선단의 좌표만 계산한다. 생성된 큐브의 루트는 옆으로 이동시키지 않는다.
        var wall = motor.RaycastEnvironment(front + Vector2.up * 0.3f,
            Vector2.right * direction, segmentSpacing + 0.03f);
        if (wall.collider != null)
        {
            active = false;
            // 커진 큐브의 마지막 면을 벽 앞까지 마감해 간격 한 칸이 빈 채 끝나지 않게 한다.
            float edgeInset = segmentPrefab != null ? segmentPrefab.Width * 0.5f + 0.01f : segmentSpacing * 0.5f;
            float endX = wall.point.x - direction * edgeInset;
            if ((endX - front.x) * direction > 0.05f)
            {
                var endFloor = motor.RaycastEnvironment(new Vector2(endX, front.y + floorProbeUp),
                    Vector2.down, floorProbeUp + floorProbeDown);
                if (endFloor.collider != null && Mathf.Abs(endFloor.point.y - front.y) <= floorProbeUp)
                {
                    front = endFloor.point;
                    SpawnSegment(front);
                }
            }
            return;
        }
        var next = front + Vector2.right * (direction * segmentSpacing);
        var floor = motor.RaycastEnvironment(next + Vector2.up * floorProbeUp,
            Vector2.down, floorProbeUp + floorProbeDown);
        if (floor.collider == null || Mathf.Abs(floor.point.y - front.y) > floorProbeUp)
        {
            // 바닥이 끊기거나 올라갈 수 없는 높이면 공중에 파도를 만들지 않는다.
            active = false;
            return;
        }
        front = floor.point;
        SpawnSegment(front);
    }

    private void SpawnSegment(Vector2 surface)
    {
        var segment = WorldEntityPool.Instance.Spawn(segmentPrefab, surface, Quaternion.identity);
        segment.InitializeSegment(owner, damageInfo, segmentLifetime, segmentHeight, waveHitTargets);
        segments.Add(segment);
        SpawnedSegmentCount++;
        lastSegmentAt = age;
    }

    private void CancelSegments()
    {
        foreach (var segment in segments)
            if (segment != null) segment.CancelForWave(waveHitTargets);
        segments.Clear();
    }

    public override void ForceReturnToPool()
    {
        if (isReturning) return;
        CancelSegments();
        base.ForceReturnToPool();
    }

    protected override void ReturnToPool()
    {
        if (isReturning) return;
        initialized = leftActive = rightActive = false;
        base.ReturnToPool();
    }

    private void OnDisable()
    {
        if (initialized) CancelSegments();
        initialized = leftActive = rightActive = false;
    }
}
