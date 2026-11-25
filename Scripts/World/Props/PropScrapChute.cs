using System;
using System.Collections.Generic;
using UnityEngine;

// 폐자재 슈트(N05, 천장 함정). 대기(5~9초 무작위) → 예고(0.8초: 붉은 표시등 깜박임 + 바닥 예고선, 끝에 해치가 열림)
// → 덩어리 낙하(물리 없음 — 표시 부품만 옮긴다) → 바닥 충돌(파편·먼지·소리) → 해치 닫힘 → 대기를 되풀이한다.
// - 떨어질 바닥은 루트 RaycastMotor2D.RaycastEnvironment(아래, Platform 레이어)로 낙하마다 찾는다(OneWay 발판은 지나간다). 없으면 maxDrop까지 떨어진다.
// - 피해: 덩어리가 지나간 구간(지난 판정 이후 이동한 거리 + 덩어리 높이)을 OverlapAttackBox(Player)로 묻고 WaveTrapDamage(복귀 없음)로 한 번 준다.
//   예고 없이 맞는 일이 없도록 피해는 낙하 단계에서만 준다(예고 0.8초 뒤).
// - 작동 조건(배치 설정): 항상 / 방 클리어 전까지. 클리어 신호가 오면 지금 낙하를 마치고 쉰다.
// - 소리(임시 기존 효과음): 해치 열림 UiPanelClank, 충돌 AdminFallingCubeImpactFeedback.
[DisallowMultipleComponent]
public sealed class PropScrapChute : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings, IDungeonRoomPowered
{
    public enum Phase { Idle, Warning, Falling, Closing, Resting }

    public const string IdleMinKey = "chuteIdleMin";
    public const string IdleMaxKey = "chuteIdleMax";
    public const string WarningKey = "chuteWarning";
    public const string DelayKey = "chuteDelay";

    [Header("배치 기본값")]
    [SerializeField, Min(.2f)] private float idleMin = 5f;
    [SerializeField, Min(.2f)] private float idleMax = 9f;
    [SerializeField, Min(.2f)] private float warningTime = .8f;
    [SerializeField, Min(0f), Tooltip("방에 들어온 뒤 첫 예고까지(초). 같은 방의 슈트를 엇갈리게 둔다.")] private float firstDelay = 2f;
    [SerializeField, Min(0f)] private float damage = 12f;
    [SerializeField] private int powerMode = HazardPowerGate.UntilClear;

    [Header("낙하")]
    [SerializeField, Min(1f), Tooltip("낙하 가속(칸/초²).")] private float gravity = 34f;
    [SerializeField, Min(1f), Tooltip("최고 낙하 속도(칸/초).")] private float maxFallSpeed = 24f;
    [SerializeField, Min(1f), Tooltip("바닥을 못 찾을 때 떨어지는 거리(칸).")] private float maxDrop = 40f;
    [SerializeField, Min(.1f), Tooltip("피해 폭(칸).")] private float damageWidth = 1.2f;
    [SerializeField, Min(.1f), Tooltip("덩어리 높이(칸) — 바닥에 닿는 아랫면 계산과 피해 상자 높이.")] private float chunkHeight = .8f;
    [SerializeField, Min(.05f)] private float hatchOpenTime = .2f;
    [SerializeField, Min(.05f)] private float hatchCloseTime = .35f;
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private LayerMask floorMask;

    [Header("구성(생성 도구가 연결)")]
    [SerializeField, Tooltip("떨어지는 덩어리(쉬는 동안 슈트 몸통 뒤에 숨어 있다).")] private Transform chunk;
    [SerializeField] private Transform[] hatches = Array.Empty<Transform>();
    [SerializeField, Tooltip("해치가 열린 각도(도). 짝수 번째는 부호를 바꾼다.")] private float hatchOpenAngle = -80f;
    [SerializeField, Tooltip("예고 동안 깜박이는 붉은 표시등.")] private Renderer[] dangerLamps = Array.Empty<Renderer>();
    [SerializeField, Tooltip("바닥 예고선(낙하 지점으로 옮긴다).")] private Transform marker;
    [SerializeField] private Renderer[] markerRenderers = Array.Empty<Renderer>();
    [SerializeField, Tooltip("해치가 열릴 때 새는 먼지.")] private ParticleSystem throatDust;
    [SerializeField, Tooltip("바닥 충돌 먼지(충돌 지점으로 옮긴다).")] private ParticleSystem impactDust;
    [SerializeField, Tooltip("바닥 충돌 파편(충돌 지점으로 옮긴다).")] private ParticleSystem impactDebris;
    [SerializeField, Min(0f)] private float idleLampGlow = .25f;
    [SerializeField, Min(0f)] private float warningLampGlow = 1.6f;
    [SerializeField, Min(.05f)] private float blinkPeriod = .16f;

    [Header("효과(임시: 기존 효과음)")]
    [SerializeField] private SoundOptions hatchSound;
    [SerializeField] private FeedbackDataSO impactFeedback;

    private readonly WaveTrapDamage trapDamage = new WaveTrapDamage();
    private readonly HazardPowerGate power = new HazardPowerGate();
    private readonly HashSet<PlayerHealth> hitThisDrop = new HashSet<PlayerHealth>();
    private Collider2D[] hits = new Collider2D[8];
    private RaycastMotor2D motor;
    private HazardLights lamps, markerLights;
    private Quaternion[] hatchHome = Array.Empty<Quaternion>();
    private Vector3 chunkHome, markerHome, impactDustHome, impactDebrisHome;
    private float activeIdleMin, activeIdleMax, activeWarning, activeDelay, activeDamage;
    private Phase phase = Phase.Resting;
    private float phaseTime;
    private float waitTime;
    private float dropDistance;
    private float lastDamageDistance;
    private float hatchOpen;
    private Vector2 dropOrigin;
    private Vector2 dropDirection = Vector2.down;
    private bool cached;
    private bool live;

    public Phase CurrentPhase => phase;
    /// <summary>이번 대여에서 바닥에 닿은 낙하 수(검사용).</summary>
    public int Drops { get; private set; }
    public int PlayerHits { get; private set; }
    /// <summary>지난 예고가 시작된 Time.time(검사용).</summary>
    public float LastWarningTime { get; private set; } = -1f;
    public float LastHitTime { get; private set; } = -1f;
    public Vector2 LandingPoint { get; private set; }
    public bool IsRunning => live && power.Running;
    public float WarningTime => activeWarning;

    private void Awake()
    {
        Cache();
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();   // 정적 질의만 쓴다
        if (playerMask.value == 0) playerMask = LayerMask.GetMask("Player");
        if (floorMask.value == 0) floorMask = LayerMask.GetMask("Platform");
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        ReadSettings(WaveDeviceSettings.None);
        lamps = new HazardLights(dangerLamps);
        markerLights = new HazardLights(markerRenderers);
        hatchHome = new Quaternion[hatches.Length];
        for (int i = 0; i < hatches.Length; i++) hatchHome[i] = hatches[i] != null ? hatches[i].localRotation : Quaternion.identity;
        if (chunk != null) chunkHome = chunk.localPosition;
        if (marker != null) markerHome = marker.localPosition;
        if (impactDust != null) impactDustHome = impactDust.transform.localPosition;
        if (impactDebris != null) impactDebrisHome = impactDebris.transform.localPosition;
    }

    // ── IDungeonPropPart ─────────────────────────────

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        live = true;
        power.Reset();
        power.Mode = powerMode;
        trapDamage.Cancel();
        Drops = 0;
        PlayerHits = 0;
        LastWarningTime = LastHitTime = -1f;
        ResetPose();
        Enter(Phase.Idle, activeDelay);
    }

    public void OnPropReturning(DungeonProp prop)
    {
        live = false;
        trapDamage.Cancel();
        ResetPose();
        lamps.Restore();
        markerLights.Restore();
    }

    private void OnDisable() => trapDamage.Cancel();

    private void ResetPose()
    {
        hatchOpen = 0f;
        ApplyHatches();
        if (chunk != null) chunk.localPosition = chunkHome;
        if (marker != null) marker.localPosition = markerHome;
        if (impactDust != null) impactDust.transform.localPosition = impactDustHome;
        if (impactDebris != null) impactDebris.transform.localPosition = impactDebrisHome;
        PropHazardKit.Stop(throatDust, true);
        markerLights?.Set(0f);
    }

    public void SetRoomPowered(bool powered, bool instant)
    {
        power.SetRoomPowered(powered);
        if (!live) return;
        if (!power.Running && (phase == Phase.Idle || phase == Phase.Warning))
        {
            // 예고 중이었다면 떨어뜨리지 않고 닫는다.
            markerLights.Set(0f);
            Enter(phase == Phase.Warning ? Phase.Closing : Phase.Resting, 0f);
        }
        else if (power.Running && phase == Phase.Resting) Enter(Phase.Idle, instant ? activeDelay : NextIdle());
    }

    // ── 단계 ─────────────────────────────

    private float NextIdle() => UnityEngine.Random.Range(Mathf.Min(activeIdleMin, activeIdleMax), Mathf.Max(activeIdleMin, activeIdleMax));

    private void Enter(Phase next, float wait)
    {
        phase = next;
        phaseTime = 0f;
        waitTime = wait;
        switch (next)
        {
            case Phase.Warning:
                LastWarningTime = Time.time;
                PlaceMarker();
                break;
            case Phase.Falling:
                hitThisDrop.Clear();
                lastDamageDistance = 0f;
                PropHazardKit.Burst(throatDust, 6);
                break;
            case Phase.Closing:
                markerLights.Set(0f);
                break;
            case Phase.Resting:
                markerLights.Set(0f);
                break;
        }
    }

    // 낙하 시작점·방향·바닥까지 거리를 정하고 예고선을 그 바닥에 놓는다.
    private void PlaceMarker()
    {
        dropDirection = -(Vector2)transform.up;
        dropOrigin = chunk != null ? chunk.TransformPoint(Vector3.zero) : transform.position;
        float half = chunkHeight * .5f;
        var floor = motor != null ? motor.RaycastEnvironment(dropOrigin, dropDirection, maxDrop, floorMask) : default;
        float surface = floor.collider != null ? floor.distance : maxDrop;
        dropDistance = Mathf.Max(0f, surface - half);
        LandingPoint = dropOrigin + dropDirection * surface;
        if (marker != null)
        {
            marker.position = new Vector3(LandingPoint.x, LandingPoint.y, marker.position.z);
            marker.rotation = transform.rotation;
        }
    }

    private void Update()
    {
        if (!live) return;
        float dt = Time.deltaTime;
        phaseTime += dt;
        switch (phase)
        {
            case Phase.Idle:
                lamps.Set(idleLampGlow);
                if (!power.Running) { Enter(Phase.Resting, 0f); break; }
                if (phaseTime >= waitTime) Enter(Phase.Warning, activeWarning);
                break;
            case Phase.Warning:
            {
                bool on = Mathf.Repeat(phaseTime, blinkPeriod) < blinkPeriod * .5f;
                lamps.Set(on ? warningLampGlow : warningLampGlow * .35f);
                markerLights.Set(on ? 1.2f : .45f);
                float openFrom = Mathf.Max(0f, activeWarning - hatchOpenTime);
                if (phaseTime >= openFrom && hatchOpen <= 0f) PropHazardKit.PlayClip(hatchSound, transform.position);
                if (phaseTime >= openFrom) hatchOpen = Mathf.Clamp01(hatchOpen + dt / hatchOpenTime);
                ApplyHatches();
                if (phaseTime >= activeWarning) { hatchOpen = 1f; ApplyHatches(); Enter(Phase.Falling, 0f); }
                break;
            }
            case Phase.Falling:
            {
                lamps.Set(warningLampGlow);
                markerLights.Set(1.2f);
                float d = FallDistance(phaseTime);
                if (chunk != null) chunk.position = (Vector3)(dropOrigin + dropDirection * d) + Vector3.forward * chunk.position.z;
                if (d >= dropDistance) Land();
                break;
            }
            case Phase.Closing:
                lamps.Set(idleLampGlow);
                hatchOpen = Mathf.Clamp01(hatchOpen - dt / hatchCloseTime);
                ApplyHatches();
                if (hatchOpen <= 0f)
                {
                    if (chunk != null) chunk.localPosition = chunkHome;
                    Enter(power.Running ? Phase.Idle : Phase.Resting, NextIdle());
                }
                break;
            case Phase.Resting:
                lamps.Set(0f);
                hatchOpen = Mathf.Clamp01(hatchOpen - dt / hatchCloseTime);
                ApplyHatches();
                if (power.Running) Enter(Phase.Idle, NextIdle());
                break;
        }
    }

    private float FallDistance(float t)
    {
        float reach = maxFallSpeed / gravity;               // 최고 속도에 닿는 시간
        if (t <= reach) return .5f * gravity * t * t;
        return .5f * gravity * reach * reach + maxFallSpeed * (t - reach);
    }

    private void Land()
    {
        Drops++;
        if (chunk != null) chunk.position = (Vector3)(dropOrigin + dropDirection * dropDistance) + Vector3.forward * chunk.position.z;
        DamageAlong(dropDistance, true);
        PropHazardKit.PlayFeedback(impactFeedback, LandingPoint);
        if (impactDust != null) { impactDust.transform.position = LandingPoint; impactDust.Emit(14); }
        if (impactDebris != null) { impactDebris.transform.position = LandingPoint; impactDebris.Emit(9); }
        // 덩어리는 부서져 흩어진 것으로 보고 원래 자리(몸통 뒤)로 숨긴다.
        if (chunk != null) chunk.localPosition = chunkHome;
        Enter(Phase.Closing, 0f);
    }

    private void FixedUpdate()
    {
        if (!live || phase != Phase.Falling) return;
        float d = Mathf.Min(FallDistance(phaseTime + Time.fixedDeltaTime * .5f), dropDistance);
        DamageAlong(d, false);
    }

    // 지난 판정 위치부터 d까지 덩어리가 쓸고 지나간 상자를 묻는다(빠르게 떨어져도 건너뛰지 않게).
    private void DamageAlong(float d, bool landing)
    {
        if (activeDamage <= 0f) { lastDamageDistance = d; return; }
        float from = Mathf.Min(lastDamageDistance, d) - chunkHeight * .5f;
        float to = d + chunkHeight * .5f + (landing ? .1f : 0f);
        lastDamageDistance = d;
        float length = Mathf.Max(.05f, to - from);
        Vector2 center = dropOrigin + dropDirection * ((from + to) * .5f);
        var size = new Vector2(damageWidth, length);
        int count = PropHazardKit.OverlapBox(motor, center, size, transform.eulerAngles.z, ref hits, playerMask);
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            hits[i] = null;
            if (hit == null) continue;
            var player = hit.GetComponentInParent<PlayerHealth>();
            if (player == null || !hitThisDrop.Add(player)) continue;
            Vector2 point = hit.ClosestPoint(center);
            var info = new WeaponBaseSO.DamageInfo { damage = activeDamage, damageType = DamageType.Normal, knockbackOrigin = point };
            if (trapDamage.TryDamage(this, player, info, point, activeDamage, false))
            {
                PlayerHits++;
                LastHitTime = Time.time;
            }
        }
    }

    private void ApplyHatches()
    {
        float eased = PropHazardKit.Smooth(hatchOpen);
        for (int i = 0; i < hatches.Length; i++)
        {
            if (hatches[i] == null) continue;
            float sign = i % 2 == 1 ? -1f : 1f;
            hatches[i].localRotation = hatchHome[i] * Quaternion.Euler(0f, 0f, hatchOpenAngle * sign * eased);
        }
    }

    // ── 배치별 설정 ─────────────────────────────

    private WaveDeviceParameter IdleMinParameter => new WaveDeviceParameter(IdleMinKey, "최소 대기", "낙하가 끝난 뒤 다음 예고까지 기다리는 가장 짧은 시간입니다.", "초", .2f, 30f, .1f, idleMin);
    private WaveDeviceParameter IdleMaxParameter => new WaveDeviceParameter(IdleMaxKey, "최대 대기", "다음 예고까지 기다리는 가장 긴 시간입니다(최소와 같으면 일정한 주기).", "초", .2f, 30f, .1f, idleMax);
    private WaveDeviceParameter WarningParameter => new WaveDeviceParameter(WarningKey, "예고 시간", "붉은 표시등과 바닥 예고선이 깜박이는 시간입니다. 끝에 해치가 열리며 떨어집니다.", "초", .2f, 5f, .05f, warningTime);
    private WaveDeviceParameter DelayParameter => new WaveDeviceParameter(DelayKey, "첫 대기", "방에 들어온 뒤 첫 예고까지 기다리는 시간입니다. 여러 슈트를 엇갈리게 둘 때 씁니다.", "초", 0f, 30f, .1f, firstDelay);

    public WaveDeviceParameter[] PropParameters => new[]
    {
        WaveTrapDamage.DamageParameter(damage), IdleMinParameter, IdleMaxParameter, WarningParameter, DelayParameter, PropHazardKit.PowerParameter(powerMode),
    };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        ReadSettings(overrides);
        if (live && phase == Phase.Idle && Drops == 0 && phaseTime < .01f) waitTime = activeDelay;   // 생성 직후 배치값이 오면 첫 대기를 맞춘다
        power.Mode = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, PropHazardKit.PowerParameter(powerMode)));
    }

    private void ReadSettings(WaveDeviceSetting[] overrides)
    {
        activeDamage = WaveDeviceSettings.Value(overrides, WaveTrapDamage.DamageParameter(damage));
        activeIdleMin = WaveDeviceSettings.Value(overrides, IdleMinParameter);
        activeIdleMax = WaveDeviceSettings.Value(overrides, IdleMaxParameter);
        activeWarning = WaveDeviceSettings.Value(overrides, WarningParameter);
        activeDelay = WaveDeviceSettings.Value(overrides, DelayParameter);
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        // 편집기에는 바로 아래 6칸만 보인다(실제로는 바닥까지 떨어진다).
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, new Vector2(0f, -4f), new Vector2(damageWidth, 6f), new Color(1f, .22f, .16f, .9f));
        return true;
    }
}
