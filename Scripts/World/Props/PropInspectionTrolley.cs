using System;
using UnityEngine;

/// <summary>
/// 점검 트롤리(DungeonPropInspectionTrolley, 2×1 장식)의 천장 레일 왕복과 아래 방향 흰 스캔 빛(2026-11-24, 던전 2차 decor).
/// - 왕복: 놓인 자리(0)를 가운데로 왼쪽 끝·오른쪽 끝을 오가며(0 → +반 → 0 → -반 …) 멈출 때마다 아래를 훑는다. 왕복 거리는 배치별 설정.
///   움직이는 것은 표시 묶음 안의 Carriage(위치)·바퀴·구동 기어(회전)뿐이다(Collider 없는 장식, 크기 늘이기 없음).
/// - 스캔 빛: 멈춘 동안 Scan 부품의 Renderer를 켜고 밝기를 올렸다 내리며 좌우로 쓸어 본다. 끈 상태는 Renderer를 끈다
///   (알파 혼합 재질은 밝기 0이 검은 면으로 남는다 — WAVE_ENVIRONMENT_DESIGN).
/// - 소리: 움직이는 동안만 반복음(DungeonPropLoopSound.SetLoopActive). 화면 밖이면 멈춘다(DungeonDecorView).
/// - 격자 편집기: '왕복 거리' 항목과 실제로 오가는 폭의 범위 상자(중립 파랑).
/// </summary>
[DisallowMultipleComponent]
public sealed class PropInspectionTrolley : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings
{
    public const string TravelKey = "travel";

    private enum Phase { Scan, Pause, Move }

    [Header("부품")]
    [SerializeField, Tooltip("레일을 따라 움직이는 몸체(표시 묶음 안)")] private Transform carriage;
    [SerializeField] private Transform[] wheels = Array.Empty<Transform>();
    [SerializeField, Min(.01f)] private float wheelRadius = .12f;
    [SerializeField] private Transform driveGear;
    [SerializeField, Min(.01f)] private float driveGearRadius = .14f;
    [SerializeField, Tooltip("스캔 빛의 회전 축(렌즈 자리)")] private Transform scanPivot;
    [SerializeField] private Renderer[] scanRenderers = Array.Empty<Renderer>();

    [Header("왕복")]
    [SerializeField, Min(0f), Tooltip("기본 왕복 거리(칸). 왼쪽 끝에서 오른쪽 끝까지.")] private float defaultTravel = 6f;
    [SerializeField, Min(1f)] private float maxTravel = 24f;
    [SerializeField, Min(.1f), Tooltip("평균 이동 속도(칸/초)")] private float speed = 1.4f;
    [SerializeField, Min(.1f)] private float scanTime = 1.6f;
    [SerializeField, Min(0f)] private float pauseTime = .35f;
    [SerializeField, Tooltip("스캔 빛이 좌우로 쓰는 각도(도)")] private float scanSweepDegrees = 14f;

    [Header("소리")]
    [SerializeField] private DungeonPropLoopSound sound;

    [Header("격자 편집기 범위 표시")]
    [SerializeField] private Color rangeColor = new Color(.45f, .78f, 1f, .8f);

    private float travel = -1f;
    private Phase phase;
    private float phaseTime, fromX, toX, moveDuration, x;
    private int stop;
    private DungeonPropGlow scanGlow;
    private bool cached;

    public float Travel { get { if (travel < 0f) travel = Mathf.Clamp(defaultTravel, 0f, maxTravel); return travel; } }
    /// <summary>Carriage의 지금 가로 자리(놓인 자리 기준, 칸).</summary>
    public float X => x;
    public bool IsMoving => phase == Phase.Move;
    public bool IsScanning => phase == Phase.Scan && scanRenderers != null && scanRenderers.Length > 0 && scanRenderers[0] != null && scanRenderers[0].enabled;
    public bool WasAnimating { get; private set; }

    private void Awake()
    {
        Cache();
        if (sound == null) sound = GetComponent<DungeonPropLoopSound>();
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        scanRenderers ??= Array.Empty<Renderer>();
        scanGlow = new DungeonPropGlow(scanRenderers);
    }

    // ── 배치별 설정 ─────────────────────────────

    private WaveDeviceParameter Parameter => new WaveDeviceParameter(TravelKey, "왕복 거리", "왼쪽 끝에서 오른쪽 끝까지 오가는 거리(칸). 0이면 제자리에서 훑기만 한다.",
        "칸", 0f, maxTravel, 1f, Mathf.Clamp(defaultTravel, 0f, maxTravel), true);

    public WaveDeviceParameter[] PropParameters => new[] { Parameter };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        travel = WaveDeviceSettings.Value(overrides, Parameter);
        ResetCycle();
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        float t = WaveDeviceSettings.Value(overrides, Parameter);
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, Vector2.zero, new Vector2(t + 2f, 1f), rangeColor);
        return true;
    }

    // ── 수명 ─────────────────────────────

    public void OnPropSpawned(DungeonProp prop) => ResetCycle();

    public void OnPropReturning(DungeonProp prop)
    {
        if (sound != null) sound.SetLoopActive(false);
        SetScan(0f);
    }

    private void ResetCycle()
    {
        Cache();
        phase = Phase.Scan;
        // 여러 트롤리가 같은 박자로 움직이지 않게 놓인 자리로 첫 훑기 시간을 줄인다(배치값을 다시 적용해도 같은 값).
        Vector2 at = transform.position;
        phaseTime = Application.isPlaying ? Mathf.Repeat(at.x * .53f + at.y * .29f, scanTime * .8f) : 0f;
        stop = 0;
        x = fromX = toX = 0f;
        if (carriage != null) carriage.localPosition = new Vector3(0f, carriage.localPosition.y, carriage.localPosition.z);
        foreach (var wheel in wheels) if (wheel != null) wheel.localRotation = Quaternion.identity;
        if (driveGear != null) driveGear.localRotation = Quaternion.identity;
        if (scanPivot != null) scanPivot.localRotation = Quaternion.identity;
        SetScan(0f);
    }

    // 멈춤 자리 순서: 0 → +반 → 0 → -반 → …(왕복 거리 0이면 제자리만)
    private float StopX(int index)
    {
        float half = Travel * .5f;
        if (half <= .01f) return 0f;
        switch (index & 3)
        {
            case 1: return half;
            case 3: return -half;
            default: return 0f;
        }
    }

    private void Update()
    {
        WasAnimating = false;
        Vector2 center = transform.position;
        if (!DungeonDecorView.Near(center, new Vector2(Travel * .5f + 1f, 2f)))
        {
            if (sound != null) sound.SetLoopActive(false);
            return;
        }
        WasAnimating = true;
        float dt = Time.deltaTime;
        phaseTime += dt;
        switch (phase)
        {
            case Phase.Scan:
            {
                float t = Mathf.Clamp01(phaseTime / scanTime);
                // 0.2초에 걸쳐 켜지고 끝 0.2초에 꺼진다.
                float fade = Mathf.Min(1f, phaseTime / .2f, Mathf.Max(0f, scanTime - phaseTime) / .2f);
                SetScan(fade);
                if (scanPivot != null) scanPivot.localRotation = Quaternion.Euler(0f, 0f, scanSweepDegrees * Mathf.Sin(t * 2f * Mathf.PI));
                if (phaseTime >= scanTime)
                {
                    SetScan(0f);
                    if (scanPivot != null) scanPivot.localRotation = Quaternion.identity;
                    phase = Phase.Pause;
                    phaseTime = 0f;
                }
                break;
            }
            case Phase.Pause:
                if (phaseTime >= pauseTime)
                {
                    stop++;
                    fromX = x;
                    toX = StopX(stop);
                    float distance = Mathf.Abs(toX - fromX);
                    phaseTime = 0f;
                    if (distance <= .01f) { phase = Phase.Scan; break; }
                    moveDuration = Mathf.Max(.4f, distance / speed);
                    phase = Phase.Move;
                }
                break;
            case Phase.Move:
            {
                float t = Mathf.Clamp01(phaseTime / moveDuration);
                float eased = t * t * (3f - 2f * t);
                float next = Mathf.Lerp(fromX, toX, eased);
                Roll(next - x);
                x = next;
                if (carriage != null) carriage.localPosition = new Vector3(x, carriage.localPosition.y, carriage.localPosition.z);
                if (t >= 1f)
                {
                    phase = Phase.Scan;
                    phaseTime = 0f;
                }
                break;
            }
        }
        // 가속·감속에 맞춰 구동음 세기를 바꾼다.
        float level = phase == Phase.Move ? .5f + .5f * Mathf.Sin(Mathf.Clamp01(phaseTime / Mathf.Max(.01f, moveDuration)) * Mathf.PI) : 0f;
        if (sound != null) sound.SetLoopActive(phase == Phase.Move, level);
    }

    // 바퀴는 레일 위를 구르고(이동 거리 / 반지름), 구동 기어는 반대 방향으로 돈다.
    private void Roll(float dx)
    {
        float wheelDegrees = -dx / wheelRadius * Mathf.Rad2Deg;
        foreach (var wheel in wheels) if (wheel != null) wheel.localRotation *= Quaternion.Euler(0f, 0f, wheelDegrees);
        if (driveGear != null) driveGear.localRotation *= Quaternion.Euler(0f, 0f, dx / driveGearRadius * Mathf.Rad2Deg);
    }

    private void SetScan(float level)
    {
        Cache();
        bool on = level > .02f;
        for (int i = 0; i < scanRenderers.Length; i++)
            if (scanRenderers[i] != null && scanRenderers[i].enabled != on) scanRenderers[i].enabled = on;
        if (on) scanGlow.Set(level);
        else scanGlow.Clear();
    }
}
