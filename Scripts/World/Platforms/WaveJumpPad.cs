using System;
using UnityEngine;

/// <summary>
/// 발판 위에 올라선 플레이어를 일반 점프의 정해진 배수 높이까지 쏘아 올린다.
/// 위치를 직접 옮기지 않고 기존 이동 경로의 Velocity에 발사 속도를 넣는다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // 캐릭터가 이번 틱의 이동·착지를 마친 뒤에 감지한다.
public sealed class WaveJumpPad : WaveEnvironmentDevice
{
    [Header("발사")]
    [Tooltip("일반 점프의 속도·중력. 여기서 일반 점프 높이와 발사 속도를 계산한다.")]
    [SerializeField] private PlayerJumpDataSO jumpData;
    [Tooltip("기본 높이 배율. 버튼을 끝까지 누른 일반 점프 높이의 몇 배까지 올릴지다. 배치마다 Wave 격자 편집기에서 바꿀 수 있다.")]
    [SerializeField, Min(1f)] private float heightMultiplier = 3.5f;
    [Tooltip("옆으로 설치했을 때 미는 외부 힘을 위로 쏠 때와 같은 거리가 되게 계산하는 데 쓰는 플레이어 값(외부 속도 상한·감쇠).")]
    [SerializeField, Min(.1f)] private float externalSpeedCap = 20f;
    [SerializeField, Min(.1f)] private float externalDamp = 30f;
    [Tooltip("옆으로 밀리는 동안 반대 방향을 누르면 밀림을 더 빨리 줄이는 정도(칸/초²). 밀려가는 중에도 조작할 수 있게 한다.")]
    [SerializeField, Min(0f)] private float steerBrake = 60f;
    [Tooltip("장치 원점 기준의 감지 영역. 윗면을 발판 윗면에 맞춰, 내려오는 플레이어가 발판에 닿는 순간 쏜다. 몸의 중심이 이 폭 안에 들어와야 한다.")]
    [SerializeField] private Vector2 senseOffset = new Vector2(0f, -.325f);
    [SerializeField] private Vector2 senseSize = new Vector2(.9f, .35f);
    [Tooltip("한 번 쏜 뒤 다시 쏠 수 있을 때까지의 시간(초).")]
    [SerializeField, Min(0f)] private float rearmTime = .25f;
    [SerializeField] private LayerMask targetMask;

    [Header("연출 (등장·퇴장 Clip이 다루지 않는 시각 부품)")]
    [Tooltip("발사 순간 튀어 올랐다가 가라앉는 발판.")]
    [SerializeField] private Transform plate;
    [SerializeField, Min(0f)] private float plateRise = .34f;
    [SerializeField, Min(.01f)] private float plateRiseTime = .04f;
    [SerializeField, Min(.01f)] private float plateSettleTime = .5f;
    [Tooltip("발판을 받치는 스프링. localScale.y를 발판 높이에 맞춰 늘인다.")]
    [SerializeField] private Transform[] springs = Array.Empty<Transform>();
    [Tooltip("스프링 Mesh의 원래 길이(칸).")]
    [SerializeField, Min(.01f)] private float springLength = .12f;
    [Tooltip("발사 방향으로 흐르는 표시.")]
    [SerializeField] private Transform[] chevrons = Array.Empty<Transform>();
    [SerializeField, Min(0f)] private float chevronTravel = .2f;
    [SerializeField, Min(.05f)] private float chevronPeriod = .8f;
    [SerializeField] private FeedbackDataSO launchFeedback;
    [SerializeField] private Vector2 feedbackOffset = new Vector2(0f, -.2f);

    public int LaunchCount { get; private set; }
    /// <summary>이 개체에 적용된 높이 배율.</summary>
    public float HeightMultiplier => currentMultiplier;

    private float currentMultiplier = 3.5f;
    private WaveDeviceParameter HeightParameter => new WaveDeviceParameter("height", "점프 높이",
        "버튼을 끝까지 누른 일반 점프 높이의 몇 배까지 쏘아 올릴지입니다. 옆으로 설치하면 같은 거리만큼 밀어냅니다.", "배", 1f, 8f, .5f, heightMultiplier);
    public override WaveDeviceParameter[] Parameters => new[] { HeightParameter };

    public override void ApplySettings(WaveDeviceSetting[] overrides) => currentMultiplier = WaveDeviceSettings.Value(overrides, HeightParameter);

    public override bool TryGetRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        // 칸 바닥에서 재는 발이 오르는 높이. 장치와 함께 회전해 옆 설치에서는 밀려나는 거리를 가리킨다.
        float height = NormalHeight * WaveDeviceSettings.Value(overrides, HeightParameter);
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, new Vector2(0f, -.5f + height * .5f), new Vector2(.6f, height), new Color(.25f, .55f, 1f, .85f));
        return true;
    }

    /// <summary>버튼을 끝까지 누른 일반 점프가 고정 틱 적분으로 실제 오르는 높이(칸).</summary>
    private float NormalHeight
    {
        get
        {
            float jump = jumpData != null ? jumpData.jumpForce : 10f;
            float low = Mathf.Max(.01f, Mathf.Abs(jumpData != null ? jumpData.lowGravity : -15f));
            return Mathf.Max(0f, jump * jump / (2f * low) - jump * Time.fixedDeltaTime * .5f);
        }
    }

    /// <summary>옆으로 설치했을 때 위로 쏠 때와 같은 거리를 밀어내는 외부 힘. 상한까지는 등속, 그 아래는 감쇠로 줄어든다.</summary>
    public float SideLaunchForce
    {
        get
        {
            float distance = NormalHeight * currentMultiplier;
            float capped = externalSpeedCap * externalSpeedCap / (2f * externalDamp);
            if (distance <= capped) return Mathf.Sqrt(2f * externalDamp * distance);
            return externalSpeedCap + (distance - capped) * externalDamp / externalSpeedCap;
        }
    }

    /// <summary>
    /// 발사 속도(칸/초). 고정 틱 적분은 속도를 먼저 줄인 뒤 이동하므로 연속식보다 v·dt/2만큼 낮게 오른다.
    /// 일반 점프 높이와 목표 높이에 같은 보정을 적용해 실제로 오르는 높이의 비율을 맞춘다.
    /// </summary>
    public float LaunchSpeed
    {
        get
        {
            float high = Mathf.Max(.01f, Mathf.Abs(jumpData != null ? jumpData.highGravity : -55f));
            float step = Time.fixedDeltaTime;
            float targetHeight = NormalHeight * currentMultiplier;
            // 발사한 뒤에는 점프 버튼과 무관하게 높은 중력만 적용되도록 만들어 둔다(Launch 참고).
            float half = high * step * .5f;
            return half + Mathf.Sqrt(half * half + 2f * high * targetHeight);
        }
    }

    private Collider2D[] hits = new Collider2D[8];
    private Vector3 plateRest;
    private Vector3[] springRest = Array.Empty<Vector3>();
    private Vector3[] chevronRestPosition = Array.Empty<Vector3>();
    private Vector3[] chevronRestScale = Array.Empty<Vector3>();
    private float nextLaunchTime;
    private float launchedAt = float.NegativeInfinity;
    private float flowTime;
    private PlayerController sidePushed;
    private float sideDirection;

    protected override void Awake()
    {
        base.Awake();
        if (targetMask.value == 0) targetMask = LayerMask.GetMask("Player");
        if (plate != null) plateRest = plate.localPosition;
        springRest = new Vector3[springs.Length];
        for (int i = 0; i < springs.Length; i++) if (springs[i] != null) springRest[i] = springs[i].localScale;
        chevronRestPosition = new Vector3[chevrons.Length];
        chevronRestScale = new Vector3[chevrons.Length];
        for (int i = 0; i < chevrons.Length; i++)
            if (chevrons[i] != null)
            {
                chevronRestPosition[i] = chevrons[i].localPosition;
                chevronRestScale[i] = chevrons[i].localScale;
            }
    }

    protected override void ResetDevice()
    {
        sidePushed = null;
        nextLaunchTime = 0f;
        launchedAt = float.NegativeInfinity;
        flowTime = 0f;
        LaunchCount = 0;
    }

    protected override void OnDeviceLeaving() => sidePushed = null;

    private void FixedUpdate()
    {
        SteerSidePush();
        if (!IsOperating || Time.time < nextLaunchTime) return;
        Vector2 center = transform.TransformPoint(senseOffset);
        var scale = transform.lossyScale;
        Vector2 size = new Vector2(Mathf.Abs(senseSize.x * scale.x), Mathf.Abs(senseSize.y * scale.y));
        int count;
        do
        {
            count = motor.OverlapAttackBox(center, size, transform.eulerAngles.z, hits, targetMask);
            if (count < hits.Length) break;
            Array.Resize(ref hits, hits.Length * 2);
        } while (true);

        for (int i = 0; i < count; i++)
        {
            if (hits[i] == null) continue;
            var player = hits[i].GetComponentInParent<PlayerController>();
            if (player == null || !CanLaunch(player, hits[i])) continue;
            Launch(player);
            return;
        }
    }

    private bool CanLaunch(PlayerController player, Collider2D body)
    {
        // 입력이 막힌 동안(Wave 시작 연출·UI·잡기)에는 속도가 매 프레임 0으로 되돌려진다.
        if (!player.CanReceiveInput) return false;
        if (player.Health != null && player.Health.IsDead) return false;
        Vector2 up = transform.up;
        // 발판 쪽으로 다가오거나 멈춰 있을 때만 쏜다. 발사 방향으로 지나가는 중에는 반응하지 않는다.
        if (Vector2.Dot(player.Velocity, up) > .5f) return false;
        Vector2 local = transform.InverseTransformPoint(body.bounds.center);
        return Mathf.Abs(local.x - senseOffset.x) <= senseSize.x * .5f;
    }

    private void Launch(PlayerController player)
    {
        var abilities = player.AbilityManager;
        if (abilities != null)
        {
            // 대시·공격은 속도를 덮어쓰거나 중력을 끄므로 끝낸다. 전부 끊으면 기어 충전·반격 준비까지 지워지므로 필요한 것만 끊는다.
            if (abilities.DashAbility != null) abilities.DashAbility.Interrupt();
            if (abilities.AttackAbility != null && abilities.AttackAbility.AttackPhase != AbilityState.None) abilities.AttackAbility.Interrupt();
            if (abilities.JumpAbility != null)
            {
                // 점프 버튼을 누른 채 밟아도 낮은 중력이 적용되지 않게 해 높이를 일정하게 한다.
                abilities.JumpAbility.Interrupt();
                abilities.JumpAbility.ClearPendingInput();
            }
        }

        Vector2 up = transform.up;
        if (Mathf.Abs(up.y) > .01f) player.Velocity.y = up.y * LaunchSpeed;
        // 가로 속도는 이동 Ability가 매 프레임 덮어쓰므로, 옆으로 설치한 경우에는 외부 힘으로 민다. 이동 입력은 그 위에 그대로 더해진다.
        sidePushed = null;
        if (Mathf.Abs(up.x) > .01f)
        {
            sideDirection = Mathf.Sign(up.x);
            player.externalForce.x = sideDirection * SideLaunchForce;
            sidePushed = player;
        }
        // 발사된 뒤에도 이동·점프·대시·공격은 막지 않는다. 땅을 밟은 것처럼 공중 점프 한 번을 채워 준다.
        if (abilities != null && abilities.JumpAbility != null)
        {
            int total = jumpData != null ? jumpData.maxJumpCount : 2;
            abilities.JumpAbility.currentJumpCount = Mathf.Max(abilities.JumpAbility.currentJumpCount, Mathf.Max(0, total - 1));
        }

        launchedAt = Time.time;
        nextLaunchTime = Time.time + rearmTime;
        LaunchCount++;
        PlayFeedback(launchFeedback, transform.TransformPoint(feedbackOffset), transform.rotation);
    }

    // 옆으로 밀려가는 동안 반대 방향을 누르면 밀림을 더 빨리 줄인다. 같은 방향을 누르거나 손을 떼면 원래대로 밀린다.
    private void SteerSidePush()
    {
        if (sidePushed == null) return;
        float push = sidePushed.externalForce.x * sideDirection;
        if (push <= 0f || !sidePushed.isActiveAndEnabled) { sidePushed = null; return; }
        float input = sidePushed.MoveInput.x;
        if (input * sideDirection < -.1f)
            sidePushed.externalForce.x = sideDirection * Mathf.MoveTowards(push, 0f, steerBrake * Mathf.Abs(input) * Time.fixedDeltaTime);
    }

    // 등장을 마치는 프레임에 WaveEnvironmentLifecycle이 저장된 자세(세 표시가 한 자리에 겹친 상태)로 되돌린 것을 바로 덮어쓴다.
    protected override void OnDeviceReady() => ApplyChevrons();

    private void Update()
    {
        float delta = Time.deltaTime;
        if (delta <= 0f) return;
        // 표시는 등장 중에도 흘러, 등장이 끝나는 순간 겹쳐 있던 표시가 갈라지며 튀지 않게 한다.
        if (!IsLeaving && (lifecycle == null || lifecycle.IsAppearing || lifecycle.IsReady))
        {
            flowTime += delta;
            ApplyChevrons();
        }
        if (!IsOperating) return;

        float lift = PlateLift(Time.time - launchedAt);
        if (plate != null) plate.localPosition = plateRest + Vector3.up * lift;
        for (int i = 0; i < springs.Length; i++)
            if (springs[i] != null)
            {
                var scale = springRest[i];
                scale.y *= Mathf.Max(.1f, 1f + lift / springLength);
                springs[i].localScale = scale;
            }
    }

    private void ApplyChevrons()
    {
        for (int i = 0; i < chevrons.Length; i++)
            if (chevrons[i] != null)
            {
                // 아래에서 커지며 올라가다 위에서 사라지기를 서로 엇갈려 반복한다.
                float phase = Mathf.Repeat(flowTime / chevronPeriod + (float)i / chevrons.Length, 1f);
                chevrons[i].localPosition = chevronRestPosition[i] + Vector3.up * (chevronTravel * phase);
                chevrons[i].localScale = chevronRestScale[i] * Mathf.Sin(Mathf.PI * phase);
            }
    }

    private float PlateLift(float elapsed)
    {
        if (elapsed < 0f || elapsed > plateRiseTime + plateSettleTime) return 0f;
        if (elapsed < plateRiseTime) return plateRise * (elapsed / plateRiseTime);
        // 터지듯 솟은 뒤 흔들리며 가라앉는다. 끝에서는 정확히 원위치다.
        float t = (elapsed - plateRiseTime) / plateSettleTime;
        return plateRise * Mathf.Exp(-7f * t) * Mathf.Cos(t * Mathf.PI * 5f) * (1f - t);
    }
}
