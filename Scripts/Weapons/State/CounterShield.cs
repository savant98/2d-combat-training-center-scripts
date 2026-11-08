using UnityEngine;

// 반격(PlayerCounter)이 Player 둘레에 펼치는 쉴드 외형(2026-11-08, 사용자 요청 "player 주변에 쉴드를 생성하고, 반격에 성공하면 주변에 충격파").
// 판정은 없다. 반격 성공 판정은 지금까지처럼 PlayerHealth → PlayerCounterState.TryCounter가 맡고, 이 개체는 그 상태를 보여 주기만 한다.
// - 전개(Raise) → 유지(Guard): 반격 창이 열려 있는 동안. 창이 닫히면 깜빡이며 꺼진다(Expire) — 쉴드가 보이는 동안만 반격이 된다는 것을 그대로 보여 준다.
// - 타격을 받으면 수축(Parry): 흰빛으로 번쩍이며 안쪽으로 줄어든다. 타격 방향을 알면(탄·근접 판정) 그쪽 테두리가 더 밝다(접촉 피해는 Player 중심을 넘겨 방향이 없다).
// - 방출(Release): 충격파가 나가는 순간 잔광(Afterglow)으로 바뀐다. 잔광은 성공 무적이 끝날 때까지 얇은 고리로 남아 무적 시간을 보여 주고,
//   끝나기 warnTime 전부터 깜빡여 곧 끝남을 알린다.
// Player가 잡히는 등으로 반격이 도중에 끝나면(코루틴이 멈춰도) PlayerCounterState 상태를 보고 스스로 꺼진다.
// 시간은 Time.deltaTime이라 히트스톱 동안 멈춘다. 모양은 Player/CounterShield 셰이더가 그리고, 이 스크립트는 값만 MaterialPropertyBlock으로 넘긴다.
[DefaultExecutionOrder(110)]
public class CounterShield : WorldEntityBase
{
    private enum Phase { None, Raise, Guard, Expire, Parry, Afterglow, Fade }

    private static readonly int RadiusId = Shader.PropertyToID("_Radius");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int GlassId = Shader.PropertyToID("_Glass");
    private static readonly int ThinId = Shader.PropertyToID("_Thin");
    private static readonly int SpinId = Shader.PropertyToID("_Spin");
    private static readonly int HitId = Shader.PropertyToID("_Hit");

    [Header("Shape")]
    [SerializeField] private SpriteRenderer shell;
    [SerializeField, Min(0.1f)] private float radius = 1.45f;           // 쉴드 반지름(칸). 본체 반지름 0.75의 약 2배
    [SerializeField] private float spinSpeed = 1.2f;                    // 테두리 회전(라디안/초)

    [Header("Timing")]
    [SerializeField, Min(0.01f)] private float raiseTime = 0.08f;       // 펼쳐지는 시간
    [SerializeField, Min(0.01f)] private float expireTime = 0.18f;      // 반격 창이 닫힌 뒤 꺼지는 시간
    [SerializeField, Min(0.01f)] private float fadeTime = 0.12f;        // 잔광이 꺼지는 시간
    [SerializeField, Min(0f)] private float warnTime = 0.25f;           // 무적이 끝나기 이만큼 전부터 잔광이 깜빡인다

    [Header("Look")]
    [SerializeField, Range(0.3f, 1f)] private float parryOpen = 0.82f;  // 수축이 끝났을 때 크기 비율
    [SerializeField, Range(0f, 1f)] private float afterglowAlpha = 0.65f;

    private MaterialPropertyBlock block;
    private PlayerCounterState counter;
    private Phase phase = Phase.None;
    private float phaseAge, spin, parryCompress = 0.1f;
    private float halfSize = 2.2f;
    private Vector2 hitDirection;
    private float hitAmount;

    // 값들(MaterialPropertyBlock으로 넘긴다)
    private float open, alpha, flash, glass, thin;

    public bool IsRunning => phase != Phase.None && !isReturning;
    public bool IsGuarding => phase == Phase.Raise || phase == Phase.Guard;
    public bool IsParrying => phase == Phase.Parry;
    public bool IsAfterglow => phase == Phase.Afterglow;
    public float WorldRadius => radius * open;
    public float CurrentAlpha => alpha;

    protected override void Awake()
    {
        base.Awake();
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        if (shell == null) shell = GetComponentInChildren<SpriteRenderer>(true);
        if (shell != null && shell.sprite != null)
            halfSize = shell.sprite.bounds.extents.x * shell.transform.localScale.x;   // 프레팹 크기와 값이 어긋나지 않게 실제 크기에서 구한다
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        phase = Phase.None;
        phaseAge = spin = 0f;
        hitAmount = 0f;
        hitDirection = Vector2.zero;
        open = alpha = flash = glass = thin = 0f;
        counter = null;
        Apply();
    }

    public void Begin(CharacterController2D owner, PlayerCounterState counter)
    {
        this.owner = owner;
        this.counter = counter;
        Enter(Phase.Raise);
        Evaluate();
        Apply();
    }

    // 타격을 받은 순간(히트스톱이 걸리기 전 프레임부터 번쩍이도록 바로 값을 넣는다)
    public void Parry(Vector3 hitPosition, float compressTime)
    {
        if (!IsGuarding) return;
        Vector2 toHit = hitPosition - transform.position;
        hitDirection = toHit.sqrMagnitude > 0.04f ? toHit.normalized : Vector2.zero;
        hitAmount = hitDirection == Vector2.zero ? 0f : 1f;
        parryCompress = Mathf.Max(0.001f, compressTime);
        Enter(Phase.Parry);
        Evaluate();
        Apply();
    }

    // 충격파가 나가는 순간. 이후는 성공 무적이 끝날 때까지 잔광으로 남는다.
    public void Release()
    {
        if (phase != Phase.Parry) return;
        Enter(Phase.Afterglow);
        Evaluate();
        Apply();
    }

    // 반격 창이 타격 없이 닫힌 순간
    public void Expire()
    {
        if (IsGuarding) Enter(Phase.Expire);
    }

    private void Enter(Phase next)
    {
        phase = next;
        phaseAge = 0f;
    }

    private void LateUpdate()
    {
        if (phase == Phase.None || isReturning) return;
        if (owner == null || !owner.gameObject.activeInHierarchy)
        {
            ForceReturnToPool();
            return;
        }

        transform.position = owner.transform.position;
        float dt = Time.deltaTime;
        phaseAge += dt;
        spin += spinSpeed * dt;

        // 잡기 등으로 반격 코루틴이 멈춰도 상태를 보고 정리한다.
        if (IsGuarding && (counter == null || !counter.IsWaiting)) Enter(Phase.Expire);
        else if ((phase == Phase.Parry || phase == Phase.Afterglow) && (counter == null || !counter.IsProtected)) Enter(Phase.Fade);

        if (phase == Phase.Raise && phaseAge >= raiseTime) Enter(Phase.Guard);
        if ((phase == Phase.Expire && phaseAge >= expireTime) || (phase == Phase.Fade && phaseAge >= fadeTime))
        {
            phase = Phase.None;
            ForceReturnToPool();
            return;
        }

        Evaluate();
        Apply();
    }

    private void Evaluate()
    {
        float t;
        switch (phase)
        {
            case Phase.Raise:
                t = Mathf.Clamp01(phaseAge / raiseTime);
                open = Mathf.LerpUnclamped(0.55f, 1f, EaseOutBack(t));
                alpha = Mathf.Clamp01(phaseAge / 0.03f);
                flash = 1f - t;
                glass = 1f;
                thin = 0f;
                break;
            case Phase.Guard:
                open = 1f + 0.012f * Mathf.Sin(phaseAge * Mathf.PI * 16f);
                alpha = 1f;
                flash = 0f;
                glass = 1f;
                thin = 0f;
                break;
            case Phase.Expire:
                t = Mathf.Clamp01(phaseAge / expireTime);
                open = Mathf.Lerp(1f, 0.9f, t);
                alpha = (1f - t) * (Mathf.Repeat(phaseAge, 0.07f) < 0.035f ? 1f : 0.45f);   // 전원이 나가듯 깜빡이며 꺼진다
                flash = 0f;
                glass = 1f - t;
                thin = 0f;
                break;
            case Phase.Parry:
                t = Mathf.Clamp01(phaseAge / parryCompress);
                open = Mathf.Lerp(1f, parryOpen, t * t);
                alpha = 1f;
                flash = Mathf.Lerp(1f, 0.55f, t);
                glass = 1f;
                thin = 0f;
                hitAmount = hitDirection == Vector2.zero ? 0f : Mathf.Lerp(1f, 0.5f, t);
                break;
            case Phase.Afterglow:
                open = 1f + 0.02f * Mathf.Sin(phaseAge * Mathf.PI * 4f);
                alpha = Mathf.Clamp01(phaseAge / 0.1f) * afterglowAlpha;
                if (counter != null && counter.ProtectionRemaining < warnTime && Mathf.Repeat(phaseAge, 0.07f) >= 0.035f)
                    alpha *= 0.25f;
                flash = 0f;
                glass = 0f;
                thin = 1f;
                hitAmount = 0f;
                break;
            case Phase.Fade:
                t = Mathf.Clamp01(phaseAge / fadeTime);
                open = Mathf.Lerp(1f, 0.92f, t);
                alpha = (1f - t) * afterglowAlpha;
                flash = 0f;
                glass = 0f;
                thin = 1f;
                hitAmount = 0f;
                break;
        }
    }

    private void Apply()
    {
        if (shell == null) return;
        shell.GetPropertyBlock(block);
        block.SetFloat(RadiusId, radius * open / halfSize);
        block.SetFloat(AlphaId, alpha);
        block.SetFloat(FlashId, flash);
        block.SetFloat(GlassId, glass);
        block.SetFloat(ThinId, thin);
        block.SetFloat(SpinId, spin);
        block.SetVector(HitId, new Vector4(hitDirection.x, hitDirection.y, hitAmount, 0f));
        shell.SetPropertyBlock(block);
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
