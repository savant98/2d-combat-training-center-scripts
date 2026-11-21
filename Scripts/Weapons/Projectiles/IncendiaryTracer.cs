using UnityEngine;

// 작열탄(IncendiaryBullet.prefab) 비행 자국(2026-11-21 무장 특성 피드백 개편 시안, 사용자 요청 "탄 Trail은 연기가 길게 늘어지게, 탄속 더 빠르게").
// 별창(StarSpear.RecordPath)과 같은 방식이다. 탄속 60칸/초면 고정 단계(0.01초)마다 0.6칸씩 뛰어 TrailRenderer의 자동 점이 띄엄띄엄해지므로,
// 고정 단계마다 지나간 점을 직접 넣고(AddPosition) 그 구간을 따라 연기 덩어리·불씨를 일정 간격으로 뿌린다(월드 공간 Emit).
// - smokeTrail: 긴 연기 리본(Player/IncendiarySmokeTrail). 탄 Prefab의 첫 TrailRenderer여야 한다 — ProjectileBase.ReturnToPool이
//   풀로 돌아갈 때 첫 TrailRenderer 하나만 떼어 남기므로, 나머지 자국(streak·smoke·embers)은 모두 이 Trail의 자식으로 둔다.
//   그래서 탄이 벽에 닿거나 사거리 끝에서 사라진 뒤에도 연기가 남았다가 흩어진다.
// - streak: 탄 바로 뒤의 짧은 주황 빛(가산, Player/StarShotTrail).
// - 자동 점 생성은 Prefab의 minVertexDistance를 아주 크게 해 막는다(점은 모두 여기서 넣음).
//   TrailRenderer는 emitting이 꺼진 동안 들어온 점을 그리지 않으므로 비행 중에만 켜 두고, 마지막 점(벽·사거리 끝)은 잠깐 켜고 넣은 뒤 끈다.
// - 탄 자체(IncendiaryShot)의 이동·판정은 건드리지 않는다. 이 컴포넌트는 탄이 고정 단계에서 움직인 뒤(실행 순서 150) 위치만 읽는다.
// - 탄 외형(탄심·불꽃 혀·빛)은 Prefab의 Body(Player/IncendiaryRound)·Glow가 그린다.
[DefaultExecutionOrder(150)]
public sealed class IncendiaryTracer : MonoBehaviour
{
    [SerializeField] private TrailRenderer smokeTrail;                       // 긴 연기 리본(첫 TrailRenderer)
    [SerializeField] private TrailRenderer streak;                           // smokeTrail 자식: 탄 뒤 짧은 주황 빛
    [SerializeField] private ParticleSystem smoke;                           // smokeTrail 자식: 연기 덩어리(월드 공간)
    [SerializeField] private ParticleSystem embers;                          // smokeTrail 자식: 불씨(월드 공간)
    [SerializeField, Min(0.05f)] private float smokeSpacing = 0.75f;
    [SerializeField, Min(0f)] private float smokeJitter = 0.07f;
    [SerializeField] private Vector2 smokeDrift = new Vector2(0.3f, 1.4f);   // 연기가 비행 방향으로 밀리는 처음 속도(칸/초) 범위
    [SerializeField, Min(0)] private int launchSmokeCount = 3;               // 총구 앞에 한 번에 피는 연기
    [SerializeField, Min(1f)] private float launchSmokeScale = 1.5f;
    [SerializeField, Min(0.05f)] private float emberSpacing = 0.6f;
    [SerializeField] private Vector2 emberSpeed = new Vector2(1.5f, 4.5f);   // 불씨가 옆으로 튀는 속도(칸/초) 범위

    private bool begun;
    private Vector2 lastPoint;
    private float smokeDistance, emberDistance;

    public TrailRenderer SmokeTrail => smokeTrail;
    public TrailRenderer Streak => streak;
    public ParticleSystem Smoke => smoke;
    public ParticleSystem Embers => embers;
    public bool IsRecording => begun;

    private void OnEnable() => begun = false;

    // 풀에서 꺼낸 뒤 Initialize가 Trail을 켠 다음의 첫 고정 단계(또는 그 전에 오는 LateUpdate)에 자국을 시작한다.
    private void FixedUpdate()
    {
        if (!CanRecord()) return;
        if (!begun) Begin(transform.position);
        else Record(transform.position);
    }

    private void LateUpdate()
    {
        if (!begun && CanRecord()) Begin(transform.position);
    }

    // 탄이 풀로 돌아갈 때(벽·사거리 끝): ProjectileBase가 Trail을 떼어 두고 emitting을 끈 뒤 탄을 끈다. 사라진 자리까지 자국을 잇는다.
    private void OnDisable()
    {
        if (!begun) return;
        begun = false;
        if (smokeTrail == null || !smokeTrail.gameObject.activeInHierarchy || smokeTrail.transform.IsChildOf(transform)) return;
        smokeTrail.emitting = true;
        if (streak != null) streak.emitting = true;
        Record(transform.position);
        smokeTrail.emitting = false;
        if (streak != null) streak.emitting = false;
    }

    private bool CanRecord() => smokeTrail != null && smokeTrail.gameObject.activeInHierarchy && smokeTrail.transform.IsChildOf(transform);

    private void Begin(Vector2 start)
    {
        begun = true;
        lastPoint = start;
        smokeDistance = smokeSpacing * 0.5f;   // 첫 연기는 출발 뒤 반 간격에서
        emberDistance = emberSpacing * 0.5f;
        Vector3 point = PathPoint(start);
        smokeTrail.Clear();
        smokeTrail.emitting = true;
        smokeTrail.AddPosition(point);
        if (streak != null)
        {
            streak.Clear();
            streak.emitting = true;
            streak.AddPosition(point);
        }
        if (embers != null)
        {
            embers.Clear();
            embers.Play();
        }
        if (smoke == null) return;
        smoke.Clear();
        smoke.Play();
        Vector2 heading = transform.right;
        for (int i = 0; i < launchSmokeCount; i++)
            EmitSmoke(start + heading * Random.Range(0.1f, 0.6f) + Random.insideUnitCircle * 0.15f, heading, launchSmokeScale, 0.35f);
    }

    // 지난 기록 지점에서 to까지 지나간 길을 궤적에 더하고, 그 구간에 연기·불씨를 간격마다 뿌린다.
    private void Record(Vector2 to)
    {
        Vector2 from = lastPoint;
        lastPoint = to;
        Vector2 delta = to - from;
        float length = delta.magnitude;
        // 움직이지 않은 구간(사거리 끝 반환·첫 고정 단계)은 같은 점을 또 넣지 않는다(겹친 점에서 궤적 모서리가 퇴화한다).
        if (length < 0.0001f) return;
        Vector3 point = PathPoint(to);
        smokeTrail.AddPosition(point);
        if (streak != null) streak.AddPosition(point);
        Vector2 direction = delta / length;
        if (smoke != null)
        {
            float s = smokeSpacing - smokeDistance;
            for (; s <= length; s += smokeSpacing) EmitSmoke(from + direction * s, direction, 1f, 1f);
            smokeDistance = length - (s - smokeSpacing);
        }
        if (embers != null)
        {
            float s = emberSpacing - emberDistance;
            for (; s <= length; s += emberSpacing) EmitEmber(from + direction * s, direction);
            emberDistance = length - (s - emberSpacing);
        }
    }

    private void EmitSmoke(Vector2 at, Vector2 direction, float sizeScale, float driftScale)
    {
        Vector2 side = new Vector2(-direction.y, direction.x);
        var p = new ParticleSystem.EmitParams
        {
            position = PathPoint(at + side * Random.Range(-smokeJitter, smokeJitter)),
            velocity = direction * (Random.Range(smokeDrift.x, smokeDrift.y) * driftScale) + side * Random.Range(-0.25f, 0.25f),
        };
        if (sizeScale != 1f)
        {
            var size = smoke.main.startSize;
            p.startSize = Random.Range(size.constantMin, size.constantMax) * sizeScale;
        }
        smoke.Emit(p, 1);
    }

    private void EmitEmber(Vector2 at, Vector2 direction)
    {
        Vector2 side = new Vector2(-direction.y, direction.x) * (Random.value < 0.5f ? -1f : 1f);
        var p = new ParticleSystem.EmitParams
        {
            position = PathPoint(at),
            velocity = side * Random.Range(emberSpeed.x, emberSpeed.y) + direction * Random.Range(-0.5f, 3f) + Vector2.up * Random.Range(0f, 1.2f),
        };
        embers.Emit(p, 1);
    }

    private Vector3 PathPoint(Vector2 at) => new Vector3(at.x, at.y, transform.position.z);
}
