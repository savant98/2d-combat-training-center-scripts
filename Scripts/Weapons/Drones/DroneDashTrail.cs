using UnityEngine;

// 돌격 드론의 비행 자국(2026-11-09, 사용자 요청 "AutoBreakDrone은 StarSpear의 것을 본따서 드론으로"). StarSpear 창의 비행 자국과 같은 방식이다.
// 아주 빠른 돌진은 고정 단계마다 여러 칸씩 움직여 프레임마다 보이는 자리가 띄엄띄엄하므로, 고정 단계마다 지나간 점을 궤적에 직접 더하고(AddPosition)
// 그 구간을 따라 연기·불꽃을 일정 간격으로 뿌린다(월드 공간).
// TrailRenderer는 emitting이 꺼진 동안 들어온 점을 끊긴 구간으로 보고 그리지 않으므로 돌진 중에만 emitting을 켜고, 자동 점 생성은 Prefab의 minVertexDistance를 아주 크게 해 막는다.
public class DroneDashTrail : MonoBehaviour
{
    [SerializeField] private TrailRenderer tracer;   // 지나간 길 전체를 남기는 가는 빛 선
    [SerializeField] private TrailRenderer streak;   // 드론 뒤의 짧은 빛 번짐
    [SerializeField] private ParticleSystem smoke;   // 지나간 길의 연기 자국(월드 공간)
    [SerializeField] private ParticleSystem sparks;  // 지나간 길에서 옆으로 튀는 불꽃(월드 공간)
    [SerializeField, Min(0.05f)] private float smokeSpacing = 0.45f;
    [SerializeField, Min(0f)] private float smokeJitter = 0.08f;
    [SerializeField] private Vector2 smokeDrift = new Vector2(0.3f, 1.2f);   // 연기가 비행 방향으로 밀리는 처음 속도(칸/초) 범위
    [SerializeField, Min(0)] private int launchSmokeCount = 4;               // 출발 자리에 한 번에 피어나는 연기
    [SerializeField, Min(1f)] private float launchSmokeScale = 1.6f;
    [SerializeField, Min(0.05f)] private float sparkSpacing = 0.9f;
    [SerializeField] private Vector2 sparkSpeed = new Vector2(2f, 6f);       // 불꽃이 옆으로 튀는 속도(칸/초) 범위

    private Vector2 lastPoint, direction;
    private float smokeDistance, sparkDistance;   // 마지막으로 연기·불꽃을 뿌린 뒤 지나온 거리
    private bool recording;

    public bool IsRecording => recording;
    public TrailRenderer Tracer => tracer;
    public TrailRenderer Streak => streak;
    public ParticleSystem Smoke => smoke;
    public ParticleSystem Sparks => sparks;

    // 출발 자리에서 궤적을 새로 시작하고, 출발 자리에 연기를 한 번에 피운다.
    public void Begin(Vector2 start, Vector2 dashDirection)
    {
        direction = dashDirection.sqrMagnitude > 0.000001f ? dashDirection.normalized : Vector2.right;
        lastPoint = start;
        smokeDistance = smokeSpacing * 0.5f;   // 첫 연기는 출발 뒤 반 간격에서
        sparkDistance = 0f;
        recording = true;
        Vector3 point = PathPoint(start);
        BeginLine(tracer, point);
        BeginLine(streak, point);
        if (sparks != null)
        {
            sparks.Clear();
            sparks.Play();
        }
        if (smoke == null) return;
        smoke.Clear();
        smoke.Play();
        for (int i = 0; i < launchSmokeCount; i++)
            EmitSmoke(start + Random.insideUnitCircle * 0.25f, -direction, launchSmokeScale);
    }

    // 지난 기록 지점에서 to까지 지나간 길을 궤적에 더하고, 그 구간에 연기·불꽃을 간격마다 뿌린다.
    public void Record(Vector2 to)
    {
        if (!recording) return;
        Vector2 from = lastPoint;
        lastPoint = to;
        Vector3 point = PathPoint(to);
        if (tracer != null) tracer.AddPosition(point);
        if (streak != null) streak.AddPosition(point);
        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.0001f) return;
        Vector2 along = delta / length;
        if (smoke != null)
        {
            float s = smokeSpacing - smokeDistance;
            for (; s <= length; s += smokeSpacing) EmitSmoke(from + along * s, along, 1f);
            smokeDistance = length - (s - smokeSpacing);
        }
        if (sparks != null)
        {
            float s = sparkSpacing - sparkDistance;
            for (; s <= length; s += sparkSpacing) EmitSpark(from + along * s, along);
            sparkDistance = length - (s - sparkSpacing);
        }
    }

    // 돌진이 끝났다. 넣은 점은 그대로 보이고, 이후 드론이 움직여도 궤적이 이어지지 않는다.
    public void End()
    {
        recording = false;
        if (tracer != null) tracer.emitting = false;
        if (streak != null) streak.emitting = false;
    }

    // 풀에서 다시 꺼낼 때 지난 자국을 지운다.
    public void Clear()
    {
        End();
        if (tracer != null) tracer.Clear();
        if (streak != null) streak.Clear();
        if (smoke != null) smoke.Clear();
        if (sparks != null) sparks.Clear();
    }

    private static void BeginLine(TrailRenderer line, Vector3 point)
    {
        if (line == null) return;
        line.Clear();
        line.emitting = true;
        line.AddPosition(point);
    }

    private void EmitSmoke(Vector2 at, Vector2 along, float sizeScale)
    {
        Vector2 side = new Vector2(-along.y, along.x);
        var p = new ParticleSystem.EmitParams
        {
            position = PathPoint(at + side * Random.Range(-smokeJitter, smokeJitter)),
            velocity = along * Random.Range(smokeDrift.x, smokeDrift.y) + side * Random.Range(-0.25f, 0.25f),
        };
        if (sizeScale != 1f)
        {
            var size = smoke.main.startSize;
            p.startSize = Random.Range(size.constantMin, size.constantMax) * sizeScale;
        }
        smoke.Emit(p, 1);
    }

    private void EmitSpark(Vector2 at, Vector2 along)
    {
        Vector2 side = new Vector2(-along.y, along.x) * (Random.value < 0.5f ? -1f : 1f);
        var p = new ParticleSystem.EmitParams
        {
            position = PathPoint(at),
            velocity = side * Random.Range(sparkSpeed.x, sparkSpeed.y) + along * Random.Range(-1f, 3f),
        };
        sparks.Emit(p, 1);
    }

    private Vector3 PathPoint(Vector2 at) => new Vector3(at.x, at.y, transform.position.z);
}
