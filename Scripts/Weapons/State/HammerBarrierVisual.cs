using UnityEngine;

// 해머 방어막 외피(2026-11-21, 사용자 요청 "쉴드가 다 차면 전개되는 보호막은 지금 반격 이펙트를 재사용하는데, 얇은 보호막 느낌으로
// (반격 성공 시 보호막과 비슷하지만 다르게)"). 판정은 없다 — 흡수·파괴·충전은 PlayerWeaponTraits가 맡고, 이 컴포넌트는 그 상태를 보여 주기만 한다.
// - 모양은 Player/HammerBarrier 셰이더가 그린다: 매끈한 원형 막(톱니 없음) + 아주 옅은 안쪽(프레넬) + 얇고 밝은 테두리 +
//   테두리 근처의 옅은 육각 셀 격자 + 천천히 지나가는 빛 띠. 남은 양(Barrier/60)이 줄수록 테두리·격자가 옅어지고,
//   30% 아래에서는 가끔 깜빡이며 가는 균열선이 자란다. 반격 쉴드(굵은 12톱니 + 어두운 유리 + 분홍 빛)와 같은 계열이지만 선·질감·색이 다르다.
// - 렌더러 두 장이 같은 재질을 쓰고 _Layer로 그릴 것을 나눈다.
//   막(HammerBarrier): Sorting Layer Player, 순서 -1 — 본체 뒤, 반격 쉴드(Player 0)보다도 뒤라 반격 창에서 겹쳐도 순서가 정해진다.
//   테두리(HammerBarrier/Rim): Particle, 순서 -4 — 본체 앞·무기(Particle 0~3) 뒤에 얇은 가산 선만 그린다.
// - Player 루트의 자식, 로컬 원점에 둔다. FixedMotionBody가 렌더 때 이 가지를 보간 위치로 함께 옮긴다(몸 옆 배터리 표시와 같은 방식).
//   렌더러를 만든 뒤 FixedMotionBody.RefreshGraphics로 다시 모으게 해, 처음 그린 뒤에 만들어져도 보간에서 빠지지 않는다.
// - 상태(CounterShield.cs 방식): 전개(Raise: 0.6 → 1 EaseOutBack + 흰 섬광 + 격자 훑음) → 유지(Hold) / 파괴(Break: 1.1배로 부풀며 사라짐).
//   피격(파문 + 근처 셀 점등 + 짧은 섬광)·재충전(테두리 맥동 + 테두리를 도는 빛 + 격자 훑음)은 유지 위에 겹치는 짧은 연출이다.
//   PlayerWeaponTraits.BarrierEvent를 구독한다(Bind에서 연결, Traits가 바뀌면 옮기고, OnDestroy에서 끊는다).
// - 반격 성공(PlayerCounterState.IsSuccess·IsProtected) 동안은 반격 잔광과 두 겹으로 겹치지 않게 숨기고, 끝나면 0.2초에 걸쳐 다시 보인다.
//   그사이 방어막이 생기거나 다시 찼으면(FillBarrier) 다시 보일 때 전개·재충전 연출과 ShieldRefill 큐(입자만)를 낸다.
//   FillBarrier는 반격 성공 연출과 겹치지 않게 입자 없이 큐를 재생하므로, 재충전 입자는 여기서 낸다.
// - Player가 쓰러졌거나 외형(model)이 꺼져 있으면 숨긴다. 시간은 Time.deltaTime이라 히트스톱 동안 함께 멈춘다. 값은 MaterialPropertyBlock으로 넘긴다.
// - 4차(2026-11-21): 막 반지름 1.35 → 0.95칸(DefaultRadius, 사용자 "Player보다 살짝 큰 정도"). 피해를 방어막 양만큼 전부 막는 규칙이 되어
//   한 번에 막는 양의 폭이 넓어졌으므로 피격 섬광·눌림·파문 세기를 막은 양(hitFullAbsorb 30에서 최대)에 비례시킨다.
[DefaultExecutionOrder(131)]
public sealed class HammerBarrierVisual : MonoBehaviour
{
    private enum Phase { Hidden, Raise, Hold, Break }

    public const string ShellName = "HammerBarrier";
    public const string RimName = "Rim";
    public const string ShaderName = "Player/HammerBarrier";

    private static readonly int RadiusId = Shader.PropertyToID("_Radius");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int ChargeId = Shader.PropertyToID("_Charge");
    private static readonly int HitId = Shader.PropertyToID("_Hit");
    private static readonly int SweepId = Shader.PropertyToID("_Sweep");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int CrackId = Shader.PropertyToID("_Crack");
    private static readonly int ClockId = Shader.PropertyToID("_Clock");
    private static readonly int LayerId = Shader.PropertyToID("_Layer");
    private const float Never = 1000f;

    /// <summary>
    /// 막 반지름(칸). 2026-11-21 4차 사용자 지시 "쉴드 이펙트가 Player보다 너무 크니 Player 크기보다 살짝 큰 정도로" → 1.35에서 0.95로 줄였다.
    /// Player 몸(PlayerGear Sprite 톱니 끝·CapsuleCollider2D 1.5×1.5)의 반지름 0.75보다 0.2칸 크다. 피격 연출 자리(PlayerWeaponTraits.BarrierHitRadius)와
    /// 전개·피격·파괴·재충전 효과 크기(tools/Unity/WeaponTraitBarrierSetup.cs)가 이 값을 따른다.
    /// </summary>
    public const float DefaultRadius = 0.95f;
    /// <summary>흰 사각형의 반 크기(칸). 막 반지름 대비 옛 비율(1.95 / 1.35)을 지킨다 — 파괴 때 1.1배로 부푼 막과 테두리 번짐이 사각형 가장자리(94%) 안에 든다.</summary>
    public const float DefaultQuadHalfSize = 1.4f;

    [Header("Shape")]
    [Tooltip("막 반지름(칸). Player 몸 반지름 0.75, 반격 쉴드 1.125, 반격 패리 1.45. 4차(2026-11-21)부터 0.95(Player보다 살짝 크게)")]
    [SerializeField, Min(0.1f)] private float radius = DefaultRadius;
    [Tooltip("흰 사각형의 반 크기(칸). 막 반지름 × 파괴 때 부풂 × 번짐이 들어가야 한다")]
    [SerializeField, Min(0.1f)] private float quadHalfSize = DefaultQuadHalfSize;
    [SerializeField] private int shellOrder = -1;     // Sorting Layer Player(본체 뒤, 반격 쉴드(0)보다도 뒤)
    [SerializeField] private int rimOrder = -4;       // Sorting Layer Particle(본체 앞, 무기 뒤)

    [Header("Timing")]
    [SerializeField, Min(0.01f)] private float raiseTime = 0.18f;
    [SerializeField, Min(0.01f)] private float breakTime = 0.15f;
    [SerializeField, Min(0.01f)] private float hitTime = 0.32f;
    [SerializeField, Min(0.01f)] private float sweepTime = 0.42f;
    [SerializeField, Min(0.01f)] private float pulseTime = 0.5f;
    [SerializeField, Min(0.01f)] private float flashTime = 0.22f;
    [Tooltip("반격 성공 잔광이 끝난 뒤 다시 보이기까지")]
    [SerializeField, Min(0.01f)] private float revealTime = 0.2f;
    [Tooltip("남은 양 표시가 실제 값을 따라가는 빠르기(초당 비율)")]
    [SerializeField, Min(0.01f)] private float chargeSpeed = 2.5f;

    [Header("Look")]
    [SerializeField, Range(0.1f, 1f)] private float raiseStart = 0.6f;
    [SerializeField, Range(1f, 1.5f)] private float breakGrow = 1.1f;
    [Tooltip("이 비율 아래에서 가끔 깜빡이고 균열선이 자란다")]
    [SerializeField, Range(0.05f, 0.9f)] private float lowCharge = 0.3f;
    [SerializeField, Range(0f, 0.2f)] private float hitCompress = 0.035f;
    [SerializeField, Range(0f, 0.2f)] private float pulseGrow = 0.025f;
    [Tooltip("한 번에 막은 양이 이 값 이상이면 피격 연출(섬광·눌림·파문)이 가장 세다. 4차부터 피해를 방어막 양만큼 전부 막아 한 번에 최대 60까지 줄어든다")]
    [SerializeField, Min(1f)] private float hitFullAbsorb = 30f;

    private PlayerController player;
    private PlayerWeaponAbility attack;
    private PlayerWeaponTraits traits;
    private PlayerCounterState counter;
    private PlayerHealth health;
    private SpriteRenderer shell, rim;
    private MaterialPropertyBlock block;
    private bool materialReady;
    private uint generation;

    private Phase phase = Phase.Hidden;
    private float phaseAge, clock, flash;
    private float hitAge = Never, sweepAge = Never, pulseAge = Never;
    private Vector2 hitDirection;
    private float shownCharge;
    private float reveal = 1f;
    private bool hiddenByCounter;
    private bool pending;
    private PlayerWeaponTraits.BarrierEventType pendingEvent;
    private float flickerNext, flickerEnd;
    private float open = 1f, alpha;
    // 피격 세기(막은 양 / hitFullAbsorb, 0~1)와 그것을 재는 직전 방어막 값
    private float hitStrength = 1f, lastBarrier;

    // 검증·연출이 읽기만 하는 값
    public bool IsVisible => shell != null && shell.enabled;
    public bool IsMaterialReady => materialReady;
    public string PhaseName => phase.ToString();
    public float WorldRadius => radius * open;
    public float CurrentAlpha => alpha;
    /// <summary>마지막 피격의 세기(막은 양 / hitFullAbsorb, 0~1).</summary>
    public float LastHitStrength => hitStrength;
    public bool HiddenByCounter => hiddenByCounter;
    public SpriteRenderer Shell => shell;
    public SpriteRenderer RimRenderer => rim;

    private void Awake()
    {
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
    }

    public void Bind(PlayerController owner, PlayerWeaponAbility ability, PlayerStarShotAim aim)
    {
        player = owner;
        attack = ability;
        health = owner != null ? owner.Health : null;
        counter = owner != null ? owner.PlayerCounterState : null;
        if (block == null) block = new MaterialPropertyBlock();
        BuildRenderers();
        Subscribe(attack != null ? attack.Traits : null);
        Snap();
        SetRenderers(false);
        var motion = GetComponentInParent<FixedMotionBody>();
        if (motion != null) motion.RefreshGraphics();
    }

    private void OnDestroy()
    {
        if (traits != null) traits.BarrierEvent -= OnBarrierEvent;
        traits = null;
    }

    private void BuildRenderers()
    {
        var material = WeaponTraitFeedbackSO.BarrierMaterial;
        // 생성 도구(WeaponTraitBarrierSetup)를 돌리기 전에는 표가 아직 반격 쉴드 재질을 가리킨다. 그때는 그리지 않는다.
        materialReady = material != null && material.HasProperty(LayerId) && material.HasProperty(ChargeId);

        var found = transform.Find(ShellName);
        shell = found != null ? found.GetComponent<SpriteRenderer>() : null;
        if (shell == null) shell = WeaponTraitEffects.MakeSprite(ShellName, transform, null, shellOrder);
        var parentScale = transform.lossyScale;
        float size = quadHalfSize * 2f;
        shell.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        shell.transform.localScale = new Vector3(size / Mathf.Max(1e-4f, Mathf.Abs(parentScale.x)),
            size / Mathf.Max(1e-4f, Mathf.Abs(parentScale.y)), 1f);
        shell.sortingLayerName = "Player";
        shell.sortingOrder = shellOrder;

        var rimFound = shell.transform.Find(RimName);
        rim = rimFound != null ? rimFound.GetComponent<SpriteRenderer>() : null;
        if (rim == null) rim = WeaponTraitEffects.MakeSprite(RimName, shell.transform, null, rimOrder);
        rim.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        rim.transform.localScale = Vector3.one;
        rim.sortingLayerName = "Particle";
        rim.sortingOrder = rimOrder;

        if (materialReady)
        {
            shell.sharedMaterial = material;
            rim.sharedMaterial = material;
        }
    }

    private void Subscribe(PlayerWeaponTraits next)
    {
        if (traits == next) return;
        if (traits != null) traits.BarrierEvent -= OnBarrierEvent;
        traits = next;
        if (traits == null) return;
        traits.BarrierEvent += OnBarrierEvent;
        generation = traits.Generation;
    }

    // 지금 값에 바로 맞춘다(연결 직후·초기화 뒤). 연출 없이 보이거나 숨는다.
    private void Snap()
    {
        float barrier = traits != null ? traits.Barrier : 0f;
        phase = barrier > 0f ? Phase.Hold : Phase.Hidden;
        phaseAge = flash = 0f;
        hitAge = sweepAge = pulseAge = Never;
        pending = false;
        shownCharge = Charge01();
        reveal = 1f;
        flickerEnd = 0f;
        flickerNext = clock + 0.3f;
        open = 1f;
        hitStrength = 1f;
        lastBarrier = barrier;
    }

    private float Charge01() =>
        traits != null ? Mathf.Clamp01(traits.Barrier / PlayerWeaponTraits.BarrierCapacity) : 0f;

    // 반격 성공(FillBarrier)으로 생기거나 다시 찬 방어막인가. 성공 콜백 안에서 부르므로 이때 IsSuccess가 켜져 있다.
    private bool CounterLinked() => counter != null && (counter.IsSuccess || counter.IsProtected);

    private void OnBarrierEvent(PlayerWeaponTraits.BarrierEventType type)
    {
        // 사건 직전 방어막 값(피격 세기 계산용). 사건은 PlayerWeaponTraits가 값을 바꾼 뒤에 부른다.
        float previous = lastBarrier;
        lastBarrier = traits != null ? traits.Barrier : 0f;
        switch (type)
        {
            case PlayerWeaponTraits.BarrierEventType.Raised:
            case PlayerWeaponTraits.BarrierEventType.Refilled:
                if (CounterLinked())
                {
                    // 반격 잔광이 끝나 다시 보일 때 연출한다. 생김(Raised)이 재충전보다 우선이다.
                    if (!pending || type == PlayerWeaponTraits.BarrierEventType.Raised) pendingEvent = type;
                    pending = true;
                    return;
                }
                if (type == PlayerWeaponTraits.BarrierEventType.Raised || phase == Phase.Hidden || phase == Phase.Break) BeginRaise();
                else BeginRefill();
                if (type == PlayerWeaponTraits.BarrierEventType.Refilled) PlayRefillParticles();
                break;
            case PlayerWeaponTraits.BarrierEventType.Hit:
            {
                // 4차(2026-11-21): 피해를 방어막 양만큼 전부 막아 한 번에 깎이는 양의 폭이 넓어졌다(1 ~ 59) → 막은 양에 비례해 섬광·눌림·파문을 키운다.
                hitStrength = Mathf.Clamp01(Mathf.Max(0f, previous - lastBarrier) / hitFullAbsorb);
                hitAge = 0f;
                hitDirection = traits != null ? traits.BarrierHitDirection : Vector2.zero;
                flash = Mathf.Max(flash, Mathf.Lerp(0.4f, 0.85f, hitStrength));
                break;
            }
            case PlayerWeaponTraits.BarrierEventType.Broken:
                hitDirection = traits != null ? traits.BarrierHitDirection : Vector2.zero;
                if (phase == Phase.Hidden) break;
                Enter(Phase.Break);
                flash = 1f;
                break;
        }
    }

    private void BeginRaise()
    {
        Enter(Phase.Raise);
        flash = 1f;
        sweepAge = 0f;
        pulseAge = hitAge = Never;
        shownCharge = Charge01();
        flickerNext = clock + 0.3f;
    }

    private void BeginRefill()
    {
        pulseAge = 0f;
        sweepAge = 0f;
        flash = Mathf.Max(flash, 0.45f);
        flickerNext = clock + 0.3f;
    }

    private void PlayRefillParticles()
    {
        int facing = player != null ? player.Facing : 1;
        WeaponTraitFeedbackSO.Play(WeaponTraitCue.ShieldRefill, transform.position, Vector2.right * (facing >= 0 ? 1f : -1f));
    }

    private void Enter(Phase next)
    {
        phase = next;
        phaseAge = 0f;
    }

    private void LateUpdate()
    {
        if (shell == null || rim == null) return;
        var current = attack != null ? attack.Traits : null;
        if (current != traits)
        {
            Subscribe(current);
            Snap();
        }
        if (traits != null && traits.Generation != generation)
        {
            generation = traits.Generation;
            Snap();
        }

        bool shown = materialReady && traits != null && player != null && player.gameObject.activeInHierarchy
            && (health == null || !health.IsDead) && (player.model == null || player.model.activeInHierarchy);
        if (!shown)
        {
            SetRenderers(false);
            return;
        }

        float dt = Time.deltaTime;
        clock += dt;
        float barrier = traits.Barrier;
        lastBarrier = barrier;     // 사건 없이 바뀐 값(초기화·재충전 등)도 다음 피격 세기의 기준이 된다

        // 반격 성공 잔광 동안 숨기고, 끝나면 다시 보인다(그사이 생김·재충전이 있었으면 이때 연출).
        // 숨김은 바로 한다: 반격 성공 순간에는 히트스톱으로 Time.deltaTime이 거의 0이라 페이드로 숨기면 섬광 동안 두 겹이 겹쳐 보인다.
        bool counterNow = CounterLinked();
        if (counterNow) reveal = 0f;
        else
        {
            if (pending)
            {
                if (barrier > 0f)
                {
                    if (pendingEvent == PlayerWeaponTraits.BarrierEventType.Raised || phase == Phase.Hidden || phase == Phase.Break) BeginRaise();
                    else BeginRefill();
                    PlayRefillParticles();
                }
                pending = false;
            }
            reveal = Mathf.MoveTowards(reveal, 1f, dt / revealTime);
        }
        hiddenByCounter = counterNow;

        // 이벤트 없이 바뀐 경우(늦게 연결됨·초기화 등)도 실제 값에 맞춘다.
        if (barrier > 0f)
        {
            if ((phase == Phase.Hidden || phase == Phase.Break) && !pending && !counterNow) BeginRaise();
        }
        else if (phase == Phase.Raise || phase == Phase.Hold) phase = Phase.Hidden;

        phaseAge += dt;
        hitAge = Mathf.Min(hitAge + dt, Never);
        sweepAge = Mathf.Min(sweepAge + dt, Never);
        pulseAge = Mathf.Min(pulseAge + dt, Never);
        flash = Mathf.Max(0f, flash - dt / flashTime);
        if (phase == Phase.Raise && phaseAge >= raiseTime) Enter(Phase.Hold);
        if (phase == Phase.Break && phaseAge >= breakTime) Enter(Phase.Hidden);
        shownCharge = Mathf.MoveTowards(shownCharge, phase == Phase.Break ? 0f : Charge01(), chargeSpeed * dt);

        if (phase == Phase.Hidden)
        {
            alpha = 0f;
            SetRenderers(false);
            return;
        }

        float t;
        float a = 1f;
        open = 1f;
        switch (phase)
        {
            case Phase.Raise:
                t = Mathf.Clamp01(phaseAge / raiseTime);
                open = Mathf.LerpUnclamped(raiseStart, 1f, EaseOutBack(t));
                a = Mathf.Clamp01(phaseAge / 0.05f);
                break;
            case Phase.Break:
                t = Mathf.Clamp01(phaseAge / breakTime);
                open = Mathf.Lerp(1f, breakGrow, 1f - (1f - t) * (1f - t));
                a = (1f - t) * Mathf.Sqrt(1f - t);
                break;
        }
        if (hitAge < 0.12f)
        {
            float k = 1f - hitAge / 0.12f;
            open *= 1f - hitCompress * Mathf.Lerp(0.6f, 1.6f, hitStrength) * k * k;   // 맞은 순간 살짝 눌렸다 돌아온다(많이 막을수록 깊게)
        }
        if (pulseAge < pulseTime) open *= 1f + pulseGrow * Mathf.Sin(Mathf.PI * pulseAge / pulseTime);

        float crack = phase == Phase.Break ? 1f : Mathf.Clamp01((lowCharge - shownCharge) / lowCharge);
        if (phase == Phase.Hold && shownCharge < lowCharge && reveal >= 0.99f)
        {
            // 남은 양이 적을수록 자주, 전원이 잠깐 나가듯 깜빡인다
            if (clock >= flickerNext)
            {
                flickerEnd = clock + Random.Range(0.04f, 0.08f);
                flickerNext = clock + Mathf.Lerp(0.45f, 1.6f, shownCharge / lowCharge) * Random.Range(0.7f, 1.3f);
            }
            if (clock < flickerEnd) a *= 0.3f;
        }
        else if (clock >= flickerNext) flickerNext = clock + 0.3f;   // 30% 밑으로 내려간 직후 바로 깜빡이지 않게

        a *= reveal;
        alpha = a;
        if (a <= 0.002f)
        {
            SetRenderers(false);
            return;
        }
        SetRenderers(true);

        bool hitting = hitAge < hitTime && hitDirection.sqrMagnitude > 0.01f;
        var hit = new Vector4(hitDirection.x, hitDirection.y, hitting ? hitAge / hitTime : 1f, hitting ? Mathf.Lerp(0.65f, 1f, hitStrength) : 0f);
        float sweep = sweepAge < sweepTime ? sweepAge / sweepTime : -1f;
        float pulse = pulseAge < pulseTime ? pulseAge / pulseTime : -1f;
        float normalizedRadius = radius * open / quadHalfSize;
        Write(shell, 0f, normalizedRadius, a, hit, sweep, pulse, crack);
        Write(rim, 1f, normalizedRadius, a, hit, sweep, pulse, crack);
    }

    private void Write(SpriteRenderer target, float layer, float normalizedRadius, float a, Vector4 hit, float sweep, float pulse, float crack)
    {
        target.GetPropertyBlock(block);
        block.SetFloat(LayerId, layer);
        block.SetFloat(RadiusId, normalizedRadius);
        block.SetFloat(AlphaId, a);
        block.SetFloat(FlashId, flash);
        block.SetFloat(ChargeId, shownCharge);
        block.SetVector(HitId, hit);
        block.SetFloat(SweepId, sweep);
        block.SetFloat(PulseId, pulse);
        block.SetFloat(CrackId, crack);
        block.SetFloat(ClockId, clock);
        target.SetPropertyBlock(block);
    }

    private void SetRenderers(bool on)
    {
        if (shell != null && shell.enabled != on) shell.enabled = on;
        if (rim != null && rim.enabled != on) rim.enabled = on;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
