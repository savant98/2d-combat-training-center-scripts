using System;
using System.Collections.Generic;
using UnityEngine;

// 크레인 레일(N09, 이동 시설의 경로 — 장식). 천장 아래에 걸린 I형 보와 양 끝 정지대.
// - 길이(2~60칸)는 배치별 설정. 왼쪽 끝은 footprint 왼쪽 변에 고정되고 오른쪽으로 늘어난다(한 칸 조각 Mesh를 이어 붙임 — 크기 늘이기 없음).
// - 4차(DESIGN_V4 §1.2): 장식으로만 남는다(경로 크레인 PropCraneHook는 자체 빛 통로를 그리고 레일을 찾지 않는다). 살아 있는 레일 목록(Find)은 남겨 둔다 — 물리 질의 없음.
//   레일은 Collider가 없다(탄·이동을 막지 않음).
// - 방을 클리어해 전원이 켜지면 보 아래의 파란 신호선을 따라 빛 덩어리가 흐르고, 양 끝 정지대 표시등이 깜박인다. 꺼져 있으면 어둡게 멈춘다.
//   소리는 없다(움직이는 훅이 소리를 맡는다).
[DisallowMultipleComponent]
public sealed class PropCraneRail : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings, IDungeonRoomPowered
{
    public const string LengthKey = "railLength";
    public const int MinLength = 2, MaxLength = 60;

    private static readonly List<PropCraneRail> live = new List<PropCraneRail>();

    [Header("배치 기본값")]
    [SerializeField, Range(MinLength, MaxLength)] private int length = 4;

    [Header("구성(생성 도구가 연결)")]
    [SerializeField, Min(1)] private int footprintCells = 4;
    [SerializeField] private MeshFilter beam;
    [SerializeField] private Mesh beamTile;
    [SerializeField] private MeshFilter signal;
    [SerializeField] private Mesh signalTile;
    [SerializeField] private Transform capRight;
    [SerializeField, Tooltip("보를 따라 흐르는 신호 빛 덩어리(이동).")] private Transform pulse;
    [SerializeField, Min(.1f)] private float pulseSpeed = 7f;
    [SerializeField] private Renderer[] signalLights = Array.Empty<Renderer>();
    [SerializeField] private Renderer[] endLamps = Array.Empty<Renderer>();
    [SerializeField, Min(.05f)] private float lampPeriod = 1.1f;
    [SerializeField, Tooltip("보 아랫면(루트 기준 y). 훅 트롤리 바퀴가 이 높이에 걸린다.")] private float beamUnderside = -.12f;

    private int activeLength;
    private bool cached;
    private bool isLive;
    private bool powered = true;
    private float power = 1f;
    private float clock;
    private Vector3 capRightHome, pulseHome;
    private Mesh beamMesh, signalMesh;
    private FacilityLights signalLamp, lamps;
    private Renderer[] visibilityProbes = Array.Empty<Renderer>();

    public int Length => cached ? activeLength : length;
    public bool Powered => powered;
    public bool IsLive => isLive;
    private float LeftLocal => -footprintCells * .5f;
    /// <summary>레일 양 끝(월드 x).</summary>
    public float LeftX => transform.TransformPoint(new Vector3(LeftLocal, 0f, 0f)).x;
    public float RightX => transform.TransformPoint(new Vector3(LeftLocal + Length, 0f, 0f)).x;
    /// <summary>트롤리 바퀴가 걸리는 보 아랫면 높이(월드).</summary>
    public float UndersideY => transform.TransformPoint(new Vector3(0f, beamUnderside, 0f)).y;

    /// <summary>
    /// point(훅 꼭대기) 바로 위에서 x 범위가 그 점을 덮는 가장 가까운 살아 있는 레일. 없으면 null. maxRise칸보다 높으면 보지 않는다.
    /// </summary>
    public static PropCraneRail Find(Vector2 point, float maxRise = 24f)
    {
        PropCraneRail best = null;
        float bestRise = float.MaxValue;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var rail = live[i];
            if (rail == null || !rail.isLive || !rail.isActiveAndEnabled) { live.RemoveAt(i); continue; }
            float rise = rail.UndersideY - point.y;
            if (rise < -.05f || rise > maxRise || rise >= bestRise) continue;
            if (point.x < rail.LeftX - .05f || point.x > rail.RightX + .05f) continue;
            best = rail;
            bestRise = rise;
        }
        return best;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetLive() => live.Clear();

    private void Awake() => Cache();

    private void Cache()
    {
        if (cached) return;
        cached = true;
        if (capRight != null) capRightHome = capRight.localPosition;
        if (pulse != null) pulseHome = pulse.localPosition;
        signalLamp = new FacilityLights(signalLights);
        lamps = new FacilityLights(endLamps);
        var prop = GetComponent<DungeonProp>();
        var root = prop != null && prop.VisualRoot != null ? prop.VisualRoot : transform;
        visibilityProbes = root.GetComponentsInChildren<Renderer>(true);
        ReadSettings(WaveDeviceSettings.None);
    }

    private void OnDestroy()
    {
        live.Remove(this);
        FacilityMesh.Release(ref beamMesh);
        FacilityMesh.Release(ref signalMesh);
    }

    private void OnDisable() => live.Remove(this);

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        isLive = true;
        powered = true;
        power = 1f;
        clock = 0f;
        if (!live.Contains(this)) live.Add(this);
        Apply();
    }

    public void OnPropReturning(DungeonProp prop)
    {
        isLive = false;
        live.Remove(this);
        signalLamp?.Restore();
        lamps?.Restore();
    }

    public void SetRoomPowered(bool on, bool instant)
    {
        Cache();
        powered = on;
        if (instant) power = on ? 1f : 0f;
        Apply();
    }

    private bool Visible()
    {
        foreach (var probe in visibilityProbes)
            if (probe != null && probe.isVisible) return true;
        return false;
    }

    private void Update()
    {
        if (!isLive) return;
        power = Mathf.MoveTowards(power, powered ? 1f : 0f, Time.deltaTime / .35f);
        if (!Visible()) { Apply(); return; }
        clock += Time.deltaTime * power;
        Apply();
    }

    private void Apply()
    {
        signalLamp?.Set(Mathf.Lerp(.12f, .55f, power));
        if (lamps != null)
        {
            bool on = Mathf.Repeat(clock, lampPeriod) < lampPeriod * .55f;
            lamps.Set(power <= .01f ? .1f : on ? power : .2f * power + .05f);
        }
        if (pulse != null)
        {
            int cells = Mathf.Max(1, Length);
            float travel = Mathf.Repeat(clock * pulseSpeed, cells + 2f) - 1f;   // 끝에서 한 칸 쉬고 다시
            pulse.localPosition = pulseHome + new Vector3(Mathf.Clamp(travel, -.5f, cells - .5f), 0f, 0f);
            pulse.gameObject.SetActive(power > .05f && travel > -.5f && travel < cells - .5f);
        }
    }

    // ── 배치별 설정 ─────────────────────────────

    private WaveDeviceParameter LengthParameter => new WaveDeviceParameter(LengthKey, "길이", "레일 길이(칸)입니다. 왼쪽 끝은 그대로 두고 오른쪽으로 늘어납니다. 크레인 훅은 이 레일의 양 끝 사이를 오갑니다.", "칸", MinLength, MaxLength, 1f, length, true);

    public WaveDeviceParameter[] PropParameters => new[] { LengthParameter };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        ReadSettings(overrides);
        if (Application.isPlaying && cached) Rebuild();
    }

    private void ReadSettings(WaveDeviceSetting[] overrides) => activeLength = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, LengthParameter));

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        int cells = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, LengthParameter));
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, new Vector2(LeftLocal + cells * .5f, 0f), new Vector2(cells, 1f), new Color(.2f, .55f, 1f, .9f));
        return true;
    }

    private void Rebuild()
    {
        int cells = Mathf.Clamp(activeLength, MinLength, MaxLength);
        float left = LeftLocal;
        if (beam != null && beamTile != null) beam.sharedMesh = beamMesh = FacilityMesh.Tile(beamMesh, beamTile, cells, new Vector3(left, 0f, 0f), Vector3.right, name + "_Beam");
        if (signal != null && signalTile != null) signal.sharedMesh = signalMesh = FacilityMesh.Tile(signalMesh, signalTile, cells, new Vector3(left, 0f, 0f), Vector3.right, name + "_Signal");
        if (capRight != null) capRight.localPosition = capRightHome + new Vector3(cells - footprintCells, 0f, 0f);
        var prop = GetComponent<DungeonProp>();
        var root = prop != null && prop.VisualRoot != null ? prop.VisualRoot : transform;
        visibilityProbes = root.GetComponentsInChildren<Renderer>(true);
    }
}
