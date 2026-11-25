using System;
using UnityEngine;

/// <summary>
/// 길이를 배치마다 정하는 분절 장식(매달린 화물의 사슬, 벽면 전선 다발)(2026-11-24, 던전 2차 decor).
/// - 길이: 칸 하나에 조각 하나(segments)를 미리 만들어 두고, 배치별 설정 값만큼 앞에서부터 켠다(크기 늘이기 없음 — 켜고 끄기만).
///   fixedSegments(흔들리지 않는 뒤판 등)는 같은 수로 켜고 끈다. endCap(위쪽 집게·덮개)은 start + step × 길이 + endCapOffset으로 옮긴다.
/// - 흔들림(표시만): 양 끝이 고정된 줄처럼 가운데가 가장 크게 옆으로 움직이는 정상파(조각 위치 이동 + 기울기 회전). Collider는 건드리지 않는다.
///   swayBurstInterval.x가 0보다 크면 그 간격마다 한 번씩만 흔들리고(전선 다발 '가끔 흔들림'), 0이면 늘 흔들린다(사슬).
/// - 신호 흐름(선택): flowRenderers(조각마다 하나)를 켜진 길이 안에서 빛 덩어리가 지나가듯 밝게 한다(DungeonPropGlow — 공유 재질 무수정).
/// - 화면 밖이면 계산을 건너뛴다(DungeonDecorView). 풀에서 다시 꺼내면 처음 자세로 돌아간다.
/// - 격자 편집기: 길이 항목(IDungeonPropSettings)과 실제 길이를 보여 주는 범위 상자(중립 파랑).
/// </summary>
[DisallowMultipleComponent]
public sealed class PropSegmentRun : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings
{
    [Header("길이(배치별 설정)")]
    [SerializeField] private string settingKey = "length";
    [SerializeField] private string settingLabel = "길이";
    [SerializeField] private string settingTooltip = "";
    [SerializeField, Tooltip("흔들리는 조각(0 = 고정 끝부터). 칸 하나에 하나.")] private Transform[] segments = Array.Empty<Transform>();
    [SerializeField, Tooltip("흔들리지 않고 길이에 맞춰 켜고 끄기만 하는 조각(segments와 같은 순서). 비워도 된다.")] private Transform[] fixedSegments = Array.Empty<Transform>();
    [SerializeField, Tooltip("길이 끝으로 옮기는 집게·덮개(비워도 된다)")] private Transform endCap;
    [SerializeField, Tooltip("첫 조각의 축 자리(조각 부모 좌표 = Visual 좌표)")] private Vector2 start;
    [SerializeField, Tooltip("조각 하나의 길이·방향")] private Vector2 step = Vector2.up;
    [SerializeField, Tooltip("끝 덮개 자리 = start + step × 길이 + 이 값")] private Vector2 endCapOffset;
    [SerializeField, Min(1)] private int minCount = 1;
    [SerializeField, Min(1)] private int defaultCount = 4;

    [Header("흔들림(표시만)")]
    [SerializeField, Min(0f), Tooltip("가운데 조각이 옆으로 움직이는 최대 거리(칸)")] private float swayAmplitude = .035f;
    [SerializeField, Min(.1f), Tooltip("한 번 왕복하는 시간(초)")] private float swayPeriod = 3.4f;
    [SerializeField, Tooltip("x가 0보다 크면 x~y초마다 한 번만 흔들린다. 0이면 늘 흔들린다.")] private Vector2 swayBurstInterval = Vector2.zero;
    [SerializeField, Min(.1f), Tooltip("한 번 흔들리는 시간(초)")] private float swayBurstLength = 2.4f;

    [Header("신호 흐름(선택)")]
    [SerializeField, Tooltip("조각마다 하나, segments와 같은 순서의 발광 Renderer")] private Renderer[] flowRenderers = Array.Empty<Renderer>();
    [SerializeField, Tooltip("초당 지나가는 조각 수")] private float flowSpeed = 2.6f;
    [SerializeField, Min(.1f), Tooltip("빛 덩어리 폭(조각 수)")] private float flowWidth = 1.8f;
    [SerializeField, Range(.02f, 1f), Tooltip("빛이 지나가지 않을 때의 밝기 배율(0이면 알파 혼합 재질이 검은 선으로 남아 0.02 이상)")] private float flowLow = .12f;
    [SerializeField, Min(0f)] private float flowHigh = 1f;

    [Header("격자 편집기 범위 표시")]
    [SerializeField, Min(.05f)] private float rangeWidth = .5f;
    [SerializeField] private Color rangeColor = new Color(.45f, .78f, 1f, .8f);

    private int count = -1;
    private float clock, burstTime, nextBurst;
    private DungeonPropGlow glow;

    /// <summary>바깥 부품(매달린 화물의 착지 흔들림)이 곱하는 흔들림 세기. 1 = 기본.</summary>
    public float SwayScale { get; set; } = 1f;
    /// <summary>켜져 있는 조각 수(배치별 길이).</summary>
    public int ActiveCount { get { EnsureCount(); return count; } }
    public int MaxCount => segments != null ? segments.Length : 0;
    public int DefaultCount => Mathf.Clamp(defaultCount, Mathf.Min(minCount, MaxCount), Mathf.Max(1, MaxCount));
    public string SettingKey => settingKey;
    public Vector2 StartPoint => start;
    public Vector2 StepVector => step;
    public Transform EndCap => endCap;
    /// <summary>마지막 프레임에 화면 근처라 움직임을 계산했는지(검사용).</summary>
    public bool WasAnimating { get; private set; }

    private void Awake() => EnsureCount();

    private void EnsureCount()
    {
        if (count >= 0) return;
        count = DefaultCount;
    }

    /// <summary>i번 조각이 원래 자리에서 옆으로 움직인 거리(검사용).</summary>
    public float SegmentOffset(int index)
    {
        if (segments == null || index < 0 || index >= segments.Length || segments[index] == null) return 0f;
        Vector2 offset = (Vector2)segments[index].localPosition - Home(index);
        return Vector2.Dot(offset, Perpendicular);
    }

    /// <summary>다음 프레임부터 한 번 흔들린다(검사용·외부 반응용).</summary>
    public void TriggerBurst() => burstTime = 0f;

    private Vector2 Perpendicular
    {
        get
        {
            Vector2 axis = step.sqrMagnitude > 1e-6f ? step.normalized : Vector2.up;
            return new Vector2(axis.y, -axis.x);
        }
    }

    private Vector2 Home(int index) => start + step * index;

    // ── 배치별 설정 ─────────────────────────────

    private WaveDeviceParameter Parameter => new WaveDeviceParameter(settingKey, settingLabel, settingTooltip, "칸",
        Mathf.Min(minCount, Mathf.Max(1, MaxCount)), Mathf.Max(1, MaxCount), 1f, DefaultCount, true);

    public WaveDeviceParameter[] PropParameters => MaxCount > 0 ? new[] { Parameter } : Array.Empty<WaveDeviceParameter>();

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        if (MaxCount == 0) return;
        ApplyCount(Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, Parameter)));
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        range = default;
        if (MaxCount == 0) return false;
        int n = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, Parameter));
        Vector2 along = step * n;
        Vector2 across = Perpendicular * rangeWidth;
        Vector2 size = new Vector2(Mathf.Abs(along.x) + Mathf.Abs(across.x), Mathf.Abs(along.y) + Mathf.Abs(across.y));
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, start + along * .5f, size, rangeColor);
        return true;
    }

    /// <summary>앞에서부터 n개 조각을 켜고 끝 덮개를 옮긴다. Edit Mode(편집기 미리보기)에서도 불린다.</summary>
    public void ApplyCount(int n)
    {
        count = Mathf.Clamp(n, Mathf.Min(minCount, MaxCount), MaxCount);
        for (int i = 0; i < MaxCount; i++)
        {
            if (segments[i] != null)
            {
                segments[i].gameObject.SetActive(i < count);
                segments[i].localPosition = new Vector3(Home(i).x, Home(i).y, segments[i].localPosition.z);
                segments[i].localRotation = Quaternion.identity;
            }
            if (fixedSegments != null && i < fixedSegments.Length && fixedSegments[i] != null) fixedSegments[i].gameObject.SetActive(i < count);
        }
        if (endCap != null)
        {
            Vector2 at = start + step * count + endCapOffset;
            endCap.localPosition = new Vector3(at.x, at.y, endCap.localPosition.z);
        }
        glow?.Clear();
    }

    // ── 수명 ─────────────────────────────

    public void OnPropSpawned(DungeonProp prop)
    {
        EnsureCount();
        clock = 0f;
        SwayScale = 1f;
        burstTime = swayBurstLength;    // 흔들림 없이 시작한다
        ScheduleBurst();
        ApplyCount(count);              // 자세만 되돌린다(길이는 방금 적용된 배치값)
    }

    public void OnPropReturning(DungeonProp prop) => glow?.Clear();

    private void ScheduleBurst()
    {
        if (swayBurstInterval.x <= 0f) return;
        float low = Mathf.Max(.1f, swayBurstInterval.x), high = Mathf.Max(low, swayBurstInterval.y);
        nextBurst = Time.time + UnityEngine.Random.Range(low, high);
    }

    private void Update()
    {
        WasAnimating = false;
        EnsureCount();
        if (count <= 0 || segments == null) return;
        Vector2 along = step * count;
        Vector2 center = transform.TransformPoint(start + along * .5f);
        Vector2 half = new Vector2(Mathf.Abs(along.x) * .5f + 1f, Mathf.Abs(along.y) * .5f + 1f);
        if (!DungeonDecorView.Near(center, half)) return;
        WasAnimating = true;

        float dt = Time.deltaTime;
        clock += dt;
        float envelope = 1f;
        if (swayBurstInterval.x > 0f)
        {
            if (burstTime < swayBurstLength)
            {
                burstTime += dt;
                envelope = Mathf.Sin(Mathf.Clamp01(burstTime / swayBurstLength) * Mathf.PI);
                if (burstTime >= swayBurstLength) ScheduleBurst();
            }
            else
            {
                envelope = 0f;
                if (Time.time >= nextBurst) burstTime = 0f;
            }
        }

        // 양 끝 고정 정상파: 옆 이동 = A·sin(πs)·sin(ωt), 기울기 = 그 미분(조각 축이 줄의 접선을 따른다).
        float amplitude = swayAmplitude * envelope * Mathf.Max(0f, SwayScale);
        float wave = Mathf.Sin(clock * 2f * Mathf.PI / Mathf.Max(.1f, swayPeriod));
        float length = Mathf.Max(.01f, along.magnitude);
        Vector2 side = Perpendicular;
        for (int i = 0; i < count && i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment == null) continue;
            float s = (i + .5f) / count;
            float offset = amplitude * Mathf.Sin(Mathf.PI * s) * wave;
            float slope = amplitude * Mathf.PI / length * Mathf.Cos(Mathf.PI * s) * wave;
            Vector2 at = Home(i) + side * offset;
            segment.localPosition = new Vector3(at.x, at.y, segment.localPosition.z);
            segment.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan(slope) * Mathf.Rad2Deg);
        }

        if (flowRenderers == null || flowRenderers.Length == 0) return;
        glow ??= new DungeonPropGlow(flowRenderers);
        float head = Mathf.Repeat(clock * flowSpeed, count + flowWidth);   // 빛 덩어리 위치(조각 순번, 끝을 지나 한 폭 쉬고 다시)
        float high = flowHigh <= 0f ? 1f : flowHigh;
        for (int i = 0; i < count && i < flowRenderers.Length; i++)
        {
            float k = Mathf.Clamp01(1f - Mathf.Abs(i - head) / Mathf.Max(.1f, flowWidth));
            glow.SetOne(i, Mathf.Lerp(flowLow, high, k * k * (3f - 2f * k)));
        }
    }
}
