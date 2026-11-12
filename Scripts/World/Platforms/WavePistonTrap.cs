using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 받침에서 가시 큐브를 세게 밀어냈다가 천천히 되돌리기를 반복하는 함정이다.
/// 큐브는 움직이는 고체가 아니라 피해 영역이며, 뻗는 동안 맞은 플레이어를 진행 방향으로 밀어낸다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // 캐릭터가 이번 틱의 이동을 마친 뒤에 판정한다.
public sealed class WavePistonTrap : WaveEnvironmentDevice
{
    public enum Phase { Rest, Windup, Strike, Rebound, Hold, Retract }

    [Header("구동 부품 (등장·퇴장 Clip이 다루지 않는 시각 부품)")]
    [Tooltip("가시 큐브. 부모 기준 +Y로 움직인다.")]
    [SerializeField] private Transform head;
    [Tooltip("큐브를 따라 움직이는 부품과, 뻗은 거리 중 따라가는 비율.")]
    [SerializeField] private Transform[] slides = Array.Empty<Transform>();
    [SerializeField] private float[] slideShares = Array.Empty<float>();
    [Tooltip("길이 1로 만든 실린더. localScale.y에 뻗은 거리 × 비율을 더해 늘인다.")]
    [SerializeField] private Transform[] stretches = Array.Empty<Transform>();
    [SerializeField] private float[] stretchShares = Array.Empty<float>();

    [Header("왕복 (칸·초)")]
    [Tooltip("기본 뻗는 거리(칸). 배치마다 Wave 격자 편집기에서 바꿀 수 있다.")]
    [SerializeField, Min(0f)] private float stroke = 2f;
    [Tooltip("기본 피스톤 속도 배율. 예비 동작·타격·되튐·유지·복귀 시간을 이 값으로 나눈다. 타격 사이 대기 시간(restTime)은 그대로다.")]
    [SerializeField, Min(.1f)] private float speed = 1f;
    [Tooltip("등장을 마친 뒤 첫 타격을 준비하기까지의 대기.")]
    [SerializeField, Min(0f)] private float startDelay = .5f;
    [Tooltip("기본 공격 대기. 큐브를 넣은 뒤 다음 타격 준비까지 쉬는 시간(초). 배치마다 Wave 격자 편집기에서 바꿀 수 있다.")]
    [SerializeField, Min(0f)] private float restTime = 1.2f;
    [Tooltip("타격 직전에 큐브를 뒤로 당기는 예비 동작. 경고를 겸한다.")]
    [SerializeField, Min(0f)] private float windupTime = .35f;
    [SerializeField, Min(0f)] private float windupPull = .06f;
    [SerializeField, Min(.01f)] private float strikeTime = .12f;
    [Tooltip("끝에 부딪힌 뒤 한 번 되튀는 시간과 거리.")]
    [SerializeField, Min(0f)] private float reboundTime = .16f;
    [SerializeField, Min(0f)] private float reboundAmount = .07f;
    [SerializeField, Min(0f)] private float holdTime = .5f;
    [SerializeField, Min(.01f)] private float retractTime = .7f;
    [Tooltip("퇴장 전에 뻗어 있던 큐브를 넣는 속도(칸/초).")]
    [SerializeField, Min(.1f)] private float stowSpeed = 9f;

    [Header("피해")]
    [SerializeField, Min(0f)] private float damage = 10f;
    [Tooltip("큐브 중심 기준의 피해 영역.")]
    [SerializeField] private Vector2 damageOffset;
    [SerializeField] private Vector2 damageSize = new Vector2(.84f, .84f);
    [Tooltip("뻗는 큐브에 맞은 플레이어가 진행 방향으로 밀려나는 속도(칸/초).")]
    [SerializeField, Min(0f)] private float pushSpeed = 16f;
    [SerializeField] private LayerMask targetMask;

    [Header("효과")]
    [SerializeField] private FeedbackDataSO chargeFeedback;
    [SerializeField] private FeedbackDataSO impactFeedback;
    [SerializeField] private FeedbackDataSO retractFeedback;
    [Tooltip("큐브 중심 기준으로 충격 효과가 나오는 앞면 위치.")]
    [SerializeField] private Vector2 impactOffset = new Vector2(0f, .42f);

    public Phase CurrentPhase { get; private set; }
    /// <summary>수납 위치에서 큐브가 나온 거리(칸). 예비 동작 중에는 음수다.</summary>
    public float Extension { get; private set; }
    public int StrikeCount { get; private set; }

    private Collider2D[] hits = new Collider2D[8];
    private readonly HashSet<PlayerHealth> damagedPlayers = new();
    private Vector3 headRest;
    private Vector3[] slideRest = Array.Empty<Vector3>();
    private Vector3[] stretchRest = Array.Empty<Vector3>();
    private float phaseTime;
    private bool stowing;
    private float currentStroke = 2f, currentSpeed = 1f, currentRest = 1.2f;
    // 등장 직후의 첫 대기는 공격 대기 대신 startDelay만큼만 쉰다.
    private bool firstRest = true;

    private WaveDeviceParameter StrokeParameter => new WaveDeviceParameter("stroke", "뻗는 거리",
        "가시 큐브가 받침에서 나오는 칸 수입니다. 타격·복귀 시간은 거리에 비례해 같은 빠르기로 움직입니다.", "칸", 1f, 6f, 1f, stroke);
    private WaveDeviceParameter SpeedParameter => new WaveDeviceParameter("speed", "피스톤 속도",
        "1이면 기본입니다. 2면 예비 동작·타격·복귀가 두 배 빨라집니다. 타격 사이 공격 대기는 그대로입니다.", "배", .5f, 3f, .1f, speed);
    private WaveDeviceParameter RestParameter => new WaveDeviceParameter("rest", "공격 대기",
        "타격하고 큐브를 다시 넣은 뒤 다음 타격 준비까지 쉬는 시간입니다. 짧을수록 자주 칩니다. 피스톤 속도와 관계없이 이 시간만큼 쉽니다.", "초", 0f, 8f, .1f, restTime);
    public override WaveDeviceParameter[] Parameters => new[] { StrokeParameter, SpeedParameter, RestParameter };
    /// <summary>이 개체에 적용된 뻗는 거리(칸).</summary>
    public float Stroke => currentStroke;
    /// <summary>이 개체에 적용된 속도 배율.</summary>
    public float Speed => currentSpeed;
    /// <summary>이 개체에 적용된 공격 대기(초).</summary>
    public float RestTime => currentRest;

    public override void ApplySettings(WaveDeviceSetting[] overrides)
    {
        currentStroke = WaveDeviceSettings.Value(overrides, StrokeParameter);
        currentSpeed = Mathf.Max(.1f, WaveDeviceSettings.Value(overrides, SpeedParameter));
        currentRest = Mathf.Max(0f, WaveDeviceSettings.Value(overrides, RestParameter));
    }

    public override bool TryGetRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        // 큐브는 네 면에 가시가 있어 앞면까지의 거리(impactOffset)가 곧 반폭이다.
        float reach = WaveDeviceSettings.Value(overrides, StrokeParameter);
        float half = Mathf.Abs(impactOffset.y);
        float rest = head != null ? head.localPosition.y : 0f;
        float low = rest - half, high = rest + reach + half;
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, new Vector2(0f, (low + high) * .5f),
            new Vector2(half * 2f, high - low), new Color(1f, .22f, .16f, .9f));
        return true;
    }

    protected override void Awake()
    {
        base.Awake();
        if (targetMask.value == 0) targetMask = LayerMask.GetMask("Player");
        // Prefab에 저장된 수납 자세를 기준으로 삼는다.
        if (head != null) headRest = head.localPosition;
        slideRest = new Vector3[slides.Length];
        for (int i = 0; i < slides.Length; i++) if (slides[i] != null) slideRest[i] = slides[i].localPosition;
        stretchRest = new Vector3[stretches.Length];
        for (int i = 0; i < stretches.Length; i++) if (stretches[i] != null) stretchRest[i] = stretches[i].localScale;
        FixedMotionBody.Attach(head);
        foreach (var part in slides) FixedMotionBody.Attach(part);
        foreach (var part in stretches) FixedMotionBody.Attach(part);
    }

    protected override void ResetDevice()
    {
        CurrentPhase = Phase.Rest;
        phaseTime = 0f;
        Extension = 0f;
        StrikeCount = 0;
        stowing = false;
        firstRest = true;
    }

    protected override void OnDeviceReady()
    {
        CurrentPhase = Phase.Rest;
        // 첫 대기는 공격 대기와 관계없이 startDelay만큼만 쉰다. 설정을 등장 완료 뒤에 적용해도 첫 타격 시각이 같다.
        phaseTime = 0f;
        firstRest = true;
    }

    protected override bool HoldDespawn()
    {
        // 뻗은 채로 접히지 않도록 큐브를 먼저 넣는다. 그동안 피해는 주지 않는다.
        if (head == null || lifecycle == null || !lifecycle.IsReady || Mathf.Abs(Extension) < .001f) return false;
        stowing = true;
        return true;
    }

    private void FixedUpdate()
    {
        if (head == null) return;
        if (IsLeaving)
        {
            if (!stowing) return;
            Extension = Mathf.MoveTowards(Extension, 0f, stowSpeed * Time.fixedDeltaTime);
            ApplyExtension(Extension);
            if (Mathf.Abs(Extension) > 0f) return;
            stowing = false;
            BeginDespawn();
            return;
        }
        if (!IsOperating) return;

        float previous = Extension;
        Advance(Time.fixedDeltaTime);
        Extension = Evaluate(CurrentPhase, Progress());
        ApplyExtension(Extension);
        DamageSwept(previous, Extension);
    }

    private void Advance(float delta)
    {
        phaseTime += delta;
        // 길이가 0인 단계는 같은 틱에 건너뛴다. 모든 단계가 0이어도 한 바퀴만 돈다.
        for (int guard = 0; guard < 6 && phaseTime >= Duration(CurrentPhase); guard++)
        {
            phaseTime -= Duration(CurrentPhase);
            if (CurrentPhase == Phase.Rest) firstRest = false;
            Enter(CurrentPhase == Phase.Retract ? Phase.Rest : CurrentPhase + 1);
        }
    }

    private void Enter(Phase next)
    {
        CurrentPhase = next;
        switch (next)
        {
            case Phase.Windup:
                PlayFeedback(chargeFeedback, HeadPoint(0f, Vector2.zero), transform.rotation);
                break;
            case Phase.Rebound:
                // 큐브가 끝까지 뻗은 바로 그 물리 틱에 한 번만 충격을 낸다.
                StrikeCount++;
                PlayFeedback(impactFeedback, HeadPoint(currentStroke, impactOffset), transform.rotation);
                break;
            case Phase.Retract:
                PlayFeedback(retractFeedback, HeadPoint(currentStroke, Vector2.zero), transform.rotation);
                break;
        }
    }

    private float Duration(Phase phase)
    {
        // 타격·복귀는 기본 거리에서 정한 시간을 거리에 비례해 늘려 같은 빠르기를 유지하고, 움직이는 단계는 속도 배율로 나눈다.
        float distance = stroke > 0f ? currentStroke / stroke : 1f;
        switch (phase)
        {
            case Phase.Rest: return firstRest ? startDelay : currentRest;
            case Phase.Windup: return windupTime / currentSpeed;
            case Phase.Strike: return strikeTime * distance / currentSpeed;
            case Phase.Rebound: return reboundTime / currentSpeed;
            case Phase.Hold: return holdTime / currentSpeed;
            default: return retractTime * distance / currentSpeed;
        }
    }

    private float Progress()
    {
        float duration = Duration(CurrentPhase);
        return duration > 0f ? Mathf.Clamp01(phaseTime / duration) : 1f;
    }

    private float Evaluate(Phase phase, float progress)
    {
        switch (phase)
        {
            case Phase.Windup:
                return -windupPull * Mathf.SmoothStep(0f, 1f, progress);
            case Phase.Strike:
                // 끝으로 갈수록 빨라져 가장 빠른 순간에 멎는다.
                return Mathf.LerpUnclamped(-windupPull, currentStroke, progress * progress);
            case Phase.Rebound:
                return currentStroke - reboundAmount * Mathf.Sin(Mathf.PI * progress);
            case Phase.Hold:
                return currentStroke;
            case Phase.Retract:
                return currentStroke * (1f - Mathf.SmoothStep(0f, 1f, progress));
            default:
                return 0f;
        }
    }

    private void ApplyExtension(float extension)
    {
        head.localPosition = headRest + Vector3.up * extension;
        for (int i = 0; i < slides.Length; i++)
            if (slides[i] != null)
                slides[i].localPosition = slideRest[i] + Vector3.up * (extension * Share(slideShares, i));
        // 실린더는 수납 길이보다 짧아지지 않는다. 예비 동작의 당김은 큐브만 움직인다.
        float reach = Mathf.Max(0f, extension);
        for (int i = 0; i < stretches.Length; i++)
            if (stretches[i] != null)
            {
                var scale = stretchRest[i];
                scale.y += reach * Share(stretchShares, i);
                stretches[i].localScale = scale;
            }
    }

    private static float Share(float[] shares, int index) => shares != null && index < shares.Length ? shares[index] : 1f;

    private Vector3 HeadPoint(float extension, Vector2 offset)
    {
        var frame = head.parent != null ? head.parent : transform;
        return frame.TransformPoint(headRest + Vector3.up * extension + (Vector3)offset);
    }

    private void DamageSwept(float previous, float current)
    {
        if (damage <= 0f) return;
        var frame = head.parent != null ? head.parent : transform;
        var scale = frame.lossyScale;
        // 한 틱 동안 큐브가 지나간 구간 전체를 한 상자로 덮어, 빠른 타격이 대상을 건너뛰지 않게 한다.
        float travel = current - previous;
        Vector2 center = HeadPoint((previous + current) * .5f, damageOffset);
        Vector2 size = new Vector2(Mathf.Abs(damageSize.x * scale.x), Mathf.Abs((damageSize.y + Mathf.Abs(travel)) * scale.y));
        float angle = frame.eulerAngles.z;
        int count;
        do
        {
            count = motor.OverlapAttackBox(center, size, angle, hits, targetMask);
            if (count < hits.Length) break;
            Array.Resize(ref hits, hits.Length * 2);
        } while (true);
        if (count == 0) return;

        damagedPlayers.Clear();
        var info = new WeaponBaseSO.DamageInfo { damage = damage, damageType = DamageType.Normal };
        for (int i = 0; i < count; i++)
        {
            if (hits[i] == null) continue;
            var player = hits[i].GetComponentInParent<PlayerHealth>();
            if (player == null || !damagedPlayers.Add(player)) continue;
            // 반격·무적으로 막힌 타격은 밀어내지 않는다.
            if (TryDamage(player, info, hits[i].ClosestPoint(center)) && travel > 0f && !player.IsDead) Push(player, frame.up);
        }
    }

    private void Push(PlayerHealth player, Vector2 direction)
    {
        if (pushSpeed <= 0f) return;
        var controller = player.GetComponentInParent<PlayerController>();
        // 튜토리얼 되돌림처럼 외부 힘을 막아 둔 동안 쌓으면, 막는 상태가 풀릴 때 뒤늦게 밀린다.
        if (controller == null || controller.IgnoreExternalForces) return;
        // 피격 넉백은 바라보는 반대쪽으로 들어가므로, 큐브가 미는 방향으로 바꿔 준다. 벽 충돌은 모터가 처리한다.
        controller.externalForce = direction.normalized * pushSpeed;
    }
}
