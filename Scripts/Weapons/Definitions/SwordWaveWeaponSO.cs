using System;
using System.Collections;
using UnityEngine;

// 검기방출(칼 Gear 2단계).
// 도약은 기존 모터 경로로 오르고, 정점에서 내려긋는 동작에 맞춰 큰 초승달을 내보낸다.
// 필살을 2스택 소비한 강화 검기는 타격 수와 틱당 피해가 증가한다.
public sealed class SwordWaveWeaponSO : WeaponBaseSO
{
    [Header("Wave")]
    [Tooltip("초승달 검기(풀 개체). 도약이 시작될 때 꺼내 무적을 쥐고, 발사 순간 날아간다.")]
    [SerializeField] private SwordWave wavePrefab;
    [SerializeField] private LayerMask damageMask;

    [Header("Leap")]
    [Tooltip("도약 높이(칸). 일반 점프 최고 높이(약 3.3칸)보다 높다.")]
    [SerializeField, Min(0.5f)] private float leapHeight = 4f;
    [Tooltip("도약 시간(초). 사용자 지시 '점프는 굉장히 빠르기 때문에(0.2초)' — 이 동안 무적.")]
    [SerializeField, Min(0.05f)] private float leapTime = 0.2f;
    [Tooltip("정점에서 멈춰 칼을 젖히는 시간(초).")]
    [SerializeField, Min(0f)] private float apexHang = 0.06f;
    [Tooltip("내려긋기 시작부터 검기가 나가는 순간까지(초) — 칼이 앞 아래를 지나는 순간.")]
    [SerializeField, Min(0f)] private float slashLead = 0.038f;
    [Tooltip("검기를 낸 뒤 공중에 멈춰 있는 시간(초). 그 뒤 회수로 넘어가 떨어진다.")]
    [SerializeField, Min(0f)] private float afterLaunchHold = 0.04f;

    [Header("Launch")]
    [Tooltip("몸 중심에서 초승달 앞점까지(칸, 보는 방향 앞·위).")]
    [SerializeField] private float launchForward = 1.05f;
    [SerializeField] private float launchUp = 0.1f;
    [Tooltip("수평에서 아래로 기운 발사 각도(도).")]
    [SerializeField, Range(0f, 80f)] private float launchAngle = 22f;
    [Tooltip("강화 검기에 쓰는 필살 스택(옛 규칙 2).")]
    [SerializeField, Min(1)] private int empowerCost = 2;
    [Tooltip("필살을 소비한 강화 검기의 틱당 피해 배율.")]
    [SerializeField, Min(1f)] private float empoweredDamageMultiplier = 1.5f;

    public SwordWave WavePrefab => wavePrefab;
    public float LeapHeight => leapHeight;
    public float LeapTime => leapTime;
    public float ApexHang => apexHang;
    public float SlashLead => slashLead;
    public float AfterLaunchHold => afterLaunchHold;
    public float LaunchAngle => launchAngle;
    public int EmpowerCost => empowerCost;
    public float EmpoweredDamageMultiplier => empoweredDamageMultiplier;
    /// <summary>실행 단계 시작부터 검기가 나가는 순간까지(초).</summary>
    public float LaunchTime => leapTime + apexHang + slashLead;

    public override string PresentationWeaponName => "Sword";
    public override bool RecoverEndsAtIdle => true;

    // 처음 쓰는 순간 풀에 검기 개체를 미리 채워 둔다(준비 단계 동안).
    public override void OnWindupStarted(CharacterController2D controller)
    {
        if (wavePrefab != null && WorldEntityPool.Instance != null) WorldEntityPool.Instance.Prewarm(wavePrefab);
    }

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        if (controller is not PlayerController player || player.Motor == null) yield break;
        var attack = player.AbilityManager.AttackAbility;
        var traits = attack.Traits;
        int facing = player.Facing >= 0 ? 1 : -1;
        float start = Time.time;

        // ---- 도약(무적)
        // 무적과 판정은 검기 개체가 쥔다. 개체를 만들 수 없으면(생성 도구 전 wavePrefab 미연결·풀 없음) 무적 없는 도약이나
        // 검기 없이 필살만 소비하는 일이 없도록 기술을 바로 끝낸다.
        SwordWave wave = wavePrefab != null && WorldEntityPool.Instance != null
            ? WorldEntityPool.Instance.Spawn(wavePrefab, player.transform.position, Quaternion.identity)
            : null;
        if (wave == null)
        {
            Debug.LogWarning($"[SwordWave] 검기 개체를 만들 수 없어 기술을 건너뜁니다(wavePrefab {(wavePrefab != null ? wavePrefab.name : "없음")}).");
            // StartExecute가 이 코루틴을 시작하는 도중에 회수를 부르면 그 뒤 실행 상태 값이 회수를 덮어쓴다 — 한 프레임 뒤에 부른다.
            yield return null;
            forceRecover?.Invoke();
            yield break;
        }
        wave.Initialize(player, WithSource(damageInfo, player));
        wave.BeginLeap(player);
        WeaponTraitFeedbackSO.Play(WeaponTraitCue.SwordWaveLeap, Feet(player));
        player.GravityOn = false;
        player.IgnoreExternalForces = true;
        var leap = new LeapRun(player, leapHeight, leapTime);
        Coroutine steps = attack.StartCoroutine(leap.FixedSteps());
        while (!leap.Done && Time.time - start < leapTime + 0.1f)
        {
            if (wave != null) wave.Hold();
            yield return null;
        }
        attack.StopCoroutine(steps);
        player.Velocity = Vector2.zero;
        if (wave != null) wave.EndLeap();   // 무적은 도약하는 동안만

        // ---- 정점 체공 + 내려긋기
        float launchAt = LaunchTime;
        while (Time.time - start < launchAt)
        {
            player.Velocity = Vector2.zero;
            if (wave != null) wave.Hold();
            yield return null;
        }

        // ---- 발사
        bool empowered = traits.Resolve >= empowerCost;
        if (empowered) traits.ChangeResolve(-empowerCost);
        Vector2 center = player.transform.position;
        Vector2 front = center + new Vector2(facing * launchForward, launchUp);
        float radians = launchAngle * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(facing * Mathf.Cos(radians), -Mathf.Sin(radians));
        WeaponTraitFeedbackSO.Play(empowered ? WeaponTraitCue.SwordWaveEmpowered : WeaponTraitCue.SwordWave, front, direction);
        if (wave != null)
        {
            var info = WithSource(damageInfo, player);
            info.popupGroup = NewPopupGroup();
            if (empowered) info.damage *= empoweredDamageMultiplier;
            info.knockbackOrigin = center;
            info.knockbackFacing = facing;
            wave.Launch(front, facing, launchAngle, empowered, info, damageMask,
                (owner, point) => OnHitEnemy?.Invoke(owner, point));
        }

        // ---- 잠깐 멈춘 뒤 회수(중력)
        while (Time.time - start < launchAt + afterLaunchHold)
        {
            player.Velocity = Vector2.zero;
            yield return null;
        }
        player.IgnoreExternalForces = false;
        forceRecover?.Invoke();
    }

    private static Vector2 Feet(PlayerController player)
    {
        var body = player.GetComponent<Collider2D>();
        return body != null && body.enabled ? new Vector2(body.bounds.center.x, body.bounds.min.y) : (Vector2)player.transform.position;
    }

    // 도약 한 번의 속도 제어. 목표 높이 = leapHeight × (1 - (1 - t/T)²)(처음 2h/T칸/초로 솟아 정점에서 멈춤).
    // 물리 틱마다(FixedUpdate 뒤) 다음 틱의 목표 높이까지 남은 거리로 Velocity.y를 정한다 — 한 프레임에 틱이 여러 번 돌아도 시간대로 오른다.
    // 실제 이동은 CharacterController2D.FixedUpdate → RaycastMotor2D.Move가 한다. 천장에 막혀 틱 두 번 동안 못 오르면 그 자리에서 끝낸다.
    private sealed class LeapRun
    {
        private const float StuckMove = 0.001f;
        private const int StuckTicks = 2;
        private readonly PlayerController player;
        private readonly float height, duration, startY;
        private float elapsed;

        public bool Done { get; private set; }

        public LeapRun(PlayerController player, float height, float duration)
        {
            this.player = player;
            this.height = height;
            this.duration = Mathf.Max(0.02f, duration);
            startY = player.transform.position.y;
            Apply();
        }

        private void Apply()
        {
            float dt = Mathf.Max(Time.fixedDeltaTime, 1e-4f);
            float next = Mathf.Min(elapsed + dt, duration);
            float u = next / duration;
            float target = startY + height * (1f - (1f - u) * (1f - u));
            player.Velocity = new Vector2(0f, Mathf.Max(0f, (target - player.transform.position.y) / dt));
        }

        public IEnumerator FixedSteps()
        {
            var wait = new WaitForFixedUpdate();
            int stuck = 0;
            while (!Done)
            {
                float before = player.transform.position.y;
                yield return wait;
                elapsed += Time.fixedDeltaTime;
                // 천장에 막히면 모터가 Velocity.y를 0으로 지우므로 속도가 아니라 실제로 오른 거리로 본다(틱마다 0.04칸 이상 오르는 곡선이다).
                bool rose = player.transform.position.y > before + StuckMove;
                stuck = rose ? 0 : stuck + 1;
                if (elapsed >= duration - 1e-4f || stuck >= StuckTicks)
                {
                    Done = true;
                    player.Velocity = Vector2.zero;
                    yield break;
                }
                Apply();
            }
        }
    }
}
