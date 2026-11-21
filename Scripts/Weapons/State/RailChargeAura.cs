using UnityEngine;

// 레일건 충전(Gear 1) 지속 표시(2026-11-21 무장 특성 4차, 사용자 요청 "레일건 충전 지속시간도 이펙트로 표시를 해야해" — 사용자 화면 검수 전 시안).
// 옛 표시는 총열 덧그림의 은은한 분홍 전류(RailgunBarrelVisual)뿐이라 충전 중인지·얼마나 남았는지 잘 보이지 않았다.
// - Player 몸 둘레를 도는 전류 궤도(가로로 납작한 타원 — 아래 반은 앞이라 밝고, 위 반은 뒤라 어둡다)와 그 위를 도는 충전 칸.
//   칸 수 = 남은 초(올림), 충전 5초 = 5칸. 1초가 지날 때마다 한 칸이 번쩍 부풀며 터지고(그 자리에서 작은 전류 불꽃), 남은 칸 수로 남은 시간을 읽는다.
//   남은 시간이 blinkFrom(1.25초)보다 적으면 궤도 전체가 깜빡이며 끝날수록 빨라진다.
// - 충전이 시작되면 궤도가 몸 쪽에서 펼쳐지며 번쩍이고, 칸이 다 터지면(충전 끝·과열 잠금으로 끊김) 사라진다.
// - 레일건 무장을 들었을 때만 보인다(판단값: 충전은 레일건 공격에만 걸리고, 다른 무장을 든 동안의 충전·열은 PlayerTraitGaugeVisual의
//   발 아래 열 칩과 HUD 띠가 보여 준다). 사망·외형 숨김 때는 바로 숨긴다.
// - 그림: 흰 사각형 Sprite + Player/RailChargeAura(절차형, MaterialPropertyBlock) 하나. 칸이 터지는 불꽃은 Pops 입자(월드 공간, Emit).
// - Player 루트 아래(좌우 반전 밖) 'RailChargeAura' 하나. RailgunWeaponSO.chargeAuraPrefab을 실행 중 복제해 붙인다(Player.prefab 무수정).
//   붙인 뒤 FixedMotionBody.RefreshGraphics로 다시 모아, 렌더 때 Player와 함께 보간 위치로 옮겨진다. 시간은 Time.deltaTime(히트스톱 때 함께 멈춘다).
[DefaultExecutionOrder(130)]
public sealed class RailChargeAura : MonoBehaviour
{
    public const string ObjectName = "RailChargeAura";
    public const string RailgunArmamentId = "Railgun";

    private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
    private static readonly int RadiiId = Shader.PropertyToID("_Radii");
    private static readonly int CellsId = Shader.PropertyToID("_Cells");
    private static readonly int LeftId = Shader.PropertyToID("_Left");
    private static readonly int PhaseId = Shader.PropertyToID("_Phase");
    private static readonly int AppearId = Shader.PropertyToID("_Appear");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int ClockId = Shader.PropertyToID("_Clock");
    private static readonly int PopTimeId = Shader.PropertyToID("_PopTime");

    [SerializeField] private SpriteRenderer orbit;
    [SerializeField] private ParticleSystem pops;
    [Header("Orbit (몸 가운데 기준 칸)")]
    [SerializeField] private Vector2 center = new(0f, 0.32f);          // 가슴 높이(총열은 몸 가운데 높이 — 궤도 앞쪽이 총열 위로 지나가게)
    [SerializeField] private Vector2 radii = new(1.1f, 0.3f);           // Player 충돌체 지름 1.5칸 — 몸 양옆으로 0.35칸 나온다
    [SerializeField] private float spin = 2.4f;                          // 칸이 도는 빠르기(라디안/초, 한 바퀴 약 2.6초)
    [SerializeField, Min(0f)] private float margin = 0.45f;              // 사각형이 궤도 밖으로 더 덮는 칸(번짐)
    [Header("Time")]
    [SerializeField, Min(0.01f)] private float appearTime = 0.25f;
    [SerializeField, Min(0.01f)] private float fadeTime = 0.15f;
    [SerializeField, Min(0.01f)] private float popTime = 0.2f;           // 칸이 터지는 데 걸리는 시간(그 초의 마지막 popTime 동안)
    [SerializeField, Min(0f)] private float blinkFrom = 1.25f;
    [SerializeField] private Vector2 blinkRate = new(4f, 10f);           // 깜빡임(초당): 남은 시간이 blinkFrom일 때 → 0일 때
    [SerializeField, Range(0f, 1f)] private float blinkLow = 0.22f;      // 깜빡임의 꺼진 쪽 밝기
    [Header("Pops")]
    [SerializeField, Min(0)] private int popSparks = 7;
    [SerializeField] private Vector2 popSpeed = new(1.5f, 4f);

    private PlayerController player;
    private PlayerWeaponAbility attack;
    private MaterialPropertyBlock block;
    private float quadSize = 6.2f;
    private float alpha, appearAge = 10f, phase, blinkPhase, lastLeft;
    private int lastAlive = -1;

    // ===== 검증·디버그용 =====
    public bool IsShowing => orbit != null && orbit.enabled;
    public float ShownAlpha => alpha;
    public float SecondsLeft => lastLeft;
    /// <summary>지금 보이는 충전 칸 수(터지는 중인 칸 제외) = 남은 초(올림).</summary>
    public int AliveCells { get; private set; }
    public int CellCount => Mathf.Clamp(Mathf.CeilToInt(PlayerWeaponTraits.ChargeDuration - 1e-3f), 1, 10);
    public bool IsBlinking { get; private set; }
    public int PopCount { get; private set; }

    /// <summary>Player 루트 아래 충전 표시를 찾고, 없으면 레일건 무기 에셋의 chargeAuraPrefab으로 만든다. 만들 수 없으면 null.</summary>
    public static RailChargeAura Ensure(CharacterController2D owner)
    {
        if (owner is not PlayerController p) return null;
        var existing = p.transform.Find(ObjectName);
        if (existing != null && existing.TryGetComponent(out RailChargeAura found)) return found;
        var rail = RailgunWeaponSO.FindFor(p);
        if (rail == null || rail.chargeAuraPrefab == null) return null;
        var aura = Instantiate(rail.chargeAuraPrefab, p.transform, false);
        aura.name = ObjectName;
        aura.gameObject.hideFlags = HideFlags.DontSave;
        aura.Bind(p);
        if (p.TryGetComponent(out FixedMotionBody motion)) motion.RefreshGraphics();
        return aura;
    }

    private void Bind(PlayerController p)
    {
        player = p;
        attack = p.AbilityManager != null ? p.AbilityManager.AttackAbility : null;
        int layer = p.model != null ? p.model.layer : p.gameObject.layer;
        foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        transform.localPosition = center;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
        if (pops != null)
        {
            pops.Clear();
            pops.Play();
        }
        Hide();
    }

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        if (orbit != null && orbit.sprite != null) quadSize = orbit.sprite.bounds.size.x;
    }

    private void LateUpdate()
    {
        if (player == null) player = GetComponentInParent<PlayerController>();
        if (player == null) return;
        if (attack == null && player.AbilityManager != null) attack = player.AbilityManager.AttackAbility;
        var traits = attack != null ? attack.Traits : null;
        float dt = Time.deltaTime;
        float left = traits != null ? traits.ChargeRemaining : 0f;
        bool charged = left > 0f;
        if (charged && (lastLeft <= 0f || left > lastLeft + 0.05f))
        {
            appearAge = 0f;   // 새 충전(또는 다시 충전): 궤도가 펼쳐지며 번쩍
            blinkPhase = 0f;
            lastAlive = -1;
        }
        lastLeft = left;

        bool visible = (player.Health == null || !player.Health.IsDead) && (player.model == null || player.model.activeInHierarchy);
        bool want = visible && charged && attack != null && attack.ActiveArmamentId == RailgunArmamentId;
        alpha = visible ? Mathf.MoveTowards(alpha, want ? 1f : 0f, dt / fadeTime) : 0f;
        appearAge += dt;
        phase = Mathf.Repeat(phase + spin * dt, Mathf.PI * 2f);

        // 칸: 그 초의 마지막 popTime 동안 터진다. 터지기 시작한 칸 자리에서 불꽃(보일 때만).
        int cells = CellCount;
        int alive = Mathf.Clamp(Mathf.CeilToInt(left - popTime - 1e-4f), 0, cells);
        if (lastAlive >= 0 && alive < lastAlive && alpha > 0.05f)
            for (int k = alive; k < lastAlive; k++) Pop(k, cells);
        lastAlive = alive;
        AliveCells = Mathf.Clamp(Mathf.CeilToInt(left - 1e-4f), 0, cells);

        IsBlinking = charged && left < blinkFrom;
        float blink = 1f;
        if (IsBlinking)
        {
            blinkPhase += Mathf.Lerp(blinkRate.y, blinkRate.x, Mathf.Clamp01(left / Mathf.Max(0.01f, blinkFrom))) * dt;
            blink = Mathf.Repeat(blinkPhase, 1f) < 0.55f ? 1f : blinkLow;
        }
        else blinkPhase = 0f;

        if (alpha <= 0.001f || orbit == null)
        {
            Hide();
            return;
        }
        Draw(left, cells, blink);
    }

    private void Draw(float left, int cells, float blink)
    {
        var size = new Vector2(2f * (radii.x + margin), 2f * (radii.y + margin));
        if (!orbit.enabled) orbit.enabled = true;
        orbit.transform.localPosition = Vector3.zero;
        orbit.transform.localRotation = Quaternion.identity;
        orbit.transform.localScale = new Vector3(size.x / quadSize, size.y / quadSize, 1f);
        orbit.GetPropertyBlock(block);
        block.SetVector(QuadSizeId, new Vector4(size.x, size.y, 0f, 0f));
        block.SetVector(RadiiId, new Vector4(radii.x, radii.y, 0f, 0f));
        block.SetFloat(CellsId, cells);
        block.SetFloat(LeftId, left);
        block.SetFloat(PhaseId, phase);
        block.SetFloat(AppearId, Mathf.Clamp01(appearAge / appearTime));
        block.SetFloat(AlphaId, alpha * blink);
        block.SetFloat(ClockId, Time.time);
        block.SetFloat(PopTimeId, popTime);
        orbit.SetPropertyBlock(block);
    }

    // 칸 k(셰이더와 같은 자리: 각도 = phase + k × 2π / cells)에서 분홍·청록 불꽃이 바깥으로 튄다.
    private void Pop(int k, int cells)
    {
        PopCount++;
        if (pops == null || popSparks <= 0) return;
        float angle = phase + k * Mathf.PI * 2f / cells;
        Vector3 local = new(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y, 0f);
        Vector3 world = transform.TransformPoint(local);
        // 파티클은 월드 공간에 바로 남으므로 Player의 보간(화면) 위치로 옮겨 낸다
        FixedMotionBody.GetPresentationPose(player.transform, out var presented, out _);
        world += presented - player.transform.position;
        Vector2 outward = new Vector2(Mathf.Cos(angle) / Mathf.Max(radii.x, 0.01f), Mathf.Sin(angle) / Mathf.Max(radii.y, 0.01f)).normalized;
        for (int i = 0; i < popSparks; i++)
        {
            Vector2 v = (outward + Random.insideUnitCircle * 0.9f).normalized * Random.Range(popSpeed.x, popSpeed.y);
            var p = new ParticleSystem.EmitParams
            {
                position = world + (Vector3)(Random.insideUnitCircle * 0.04f),
                velocity = v,
                startColor = Random.value < 0.6f ? new Color(1f, 0.43f, 0.82f, 1f) : new Color(0.3f, 0.88f, 1f, 1f),
                applyShapeToPosition = false,
            };
            pops.Emit(p, 1);
        }
    }

    private void Hide()
    {
        if (orbit != null && orbit.enabled) orbit.enabled = false;
    }

    private void OnDisable() => Hide();
}
