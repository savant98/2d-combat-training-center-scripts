using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 부서지면 폭발하는 Prop(폭발성 냉각 캔). 같은 루트의 DestructibleProp가 Broken을 내면 터진다.
// - Player: GameManager의 Player 몸통 Collider 범위(bounds)와 폭발 중심의 거리로 판정한다(물리 질의 없음).
//   피해는 WaveTrapDamage(함정 피해 공통 — IsApplying 표식·실제 적중 판정·선택형 시작 위치 복귀)로 PlayerHealth.TakeDamage를 부른다.
// - 적·다른 파괴형 Prop: RaycastMotor2D.OverlapAttackCircle(Enemy 레이어, 트리거 포함) → 부모의 EnemyHealth → 대상마다 한 번
//   WeaponTraitEffects.ApplyEnemyHit(쉴드 유무 자동 처리, 무적·생성 잠금·사망 제외). Wave가 만든 개체는 ownerFaction이 늘 Enemy라
//   기존 탄의 진영 분기를 쓰면 Player만 맞으므로 두 경로를 직접 부른다. 피해 출처(sourceWeapon)는 비워 넉백이 없고,
//   피해 종류는 Normal이라 환경 폭발이 적을 브레이크시키지 않는다(판단값).
// - 연쇄: 폭발 피해로 부서진 다른 폭발 Prop은 chainDelay 뒤에 터진다(같은 프레임 재귀 없음, 연달아 터지는 소리·화면).
//   Prop마다 한 번만 터진다.
// - 공격 질의 모터: 같은 루트에 중력 본체(DungeonPropBody, CharacterController2D)가 있으면 그 본체의 모터를 쓴다(모터를 둘 두지 않는다 — 4차 R9).
//   본체가 없을 때만 자기 정적 질의용 모터를 붙인다(예전과 같음). Awake 순서와 상관없게 폭발 순간에 찾는다.
[DisallowMultipleComponent]
[RequireComponent(typeof(DestructibleProp))]
public sealed class PropExplosive : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings
{
    public const string RadiusKey = "blastRadius";
    public const string PlayerDamageKey = "damage";          // 함정 피해량과 같은 열쇠(격자 편집기에서 '피해량')
    public const string EnemyDamageKey = "blastEnemyDamage";

    [Header("폭발")]
    [SerializeField, Min(0.1f)] private float radius = 2.5f;
    [Tooltip("Player가 받는 피해.")]
    [SerializeField, Min(0f)] private float playerDamage = 15f;
    [Tooltip("적·다른 파괴형 Prop이 받는 피해.")]
    [SerializeField, Min(0f)] private float enemyDamage = 40f;
    [Tooltip("폭발 중심(루트 기준). 비우면 DestructibleProp의 hurtbox 중심.")]
    [SerializeField] private Transform blastOrigin;
    [Tooltip("폭발 효과(파티클·소리·흔들림). 파괴형의 파편 효과와 함께 재생한다.")]
    [SerializeField] private FeedbackDataSO explosionFeedback;
    [Tooltip("다른 폭발에 맞아 부서졌을 때 터지기까지의 지연(초).")]
    [SerializeField, Min(0f)] private float chainDelay = 0.12f;
    [SerializeField] private LayerMask enemyMask;

    private readonly WaveTrapDamage trapDamage = new WaveTrapDamage();
    private readonly List<Collider2D> hits = new List<Collider2D>(16);
    private readonly HashSet<EnemyHealth> damaged = new HashSet<EnemyHealth>();
    private DestructibleProp health;
    private RaycastMotor2D motor;
    private Coroutine chainRoutine;
    private bool exploded;
    private float activeRadius, activeEnemyDamage;

    /// <summary>지금 폭발 피해를 주는 중이면 true(이 동안 부서진 폭발 Prop은 연쇄로 미뤄 터진다).</summary>
    public static bool IsBlasting => blastDepth > 0;
    private static int blastDepth;

    /// <summary>터지기를 기다리는 중(연쇄 지연)이거나 폭발에 맞은 Player를 시작 위치로 옮기는 중이면 true. DestructibleProp가 풀 반환을 미룬다.</summary>
    public bool IsPending => chainRoutine != null || trapDamage.IsReturning;
    public bool HasExploded => exploded;
    public float Radius => activeRadius > 0f ? activeRadius : radius;
    /// <summary>마지막 폭발에서 실제로 피해가 들어간 적·Prop 수와 Player 피해 여부(검사용).</summary>
    public int LastEnemyHits { get; private set; }
    public bool LastPlayerHit { get; private set; }
    public Vector2 LastCenter { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDepth() => blastDepth = 0;

    private void Awake()
    {
        health = GetComponent<DestructibleProp>();
        ResolveMotor();
        if (enemyMask.value == 0) enemyMask = LayerMask.GetMask("Enemy");
        ApplyPropSettings(WaveDeviceSettings.None);
        if (health != null) health.Broken += OnBroken;
    }

    private void OnDestroy()
    {
        if (health != null) health.Broken -= OnBroken;
    }

    // 공격 질의에 쓸 모터. 중력 본체가 있으면 그 모터(본체 Awake가 아직이면 다음에 다시 찾는다), 없으면 자기 것(없으면 붙인다).
    private RaycastMotor2D ResolveMotor()
    {
        var body = GetComponent<CharacterController2D>();
        if (body != null)
        {
            if (body.Motor != null) motor = body.Motor;
            else if (motor == null) motor = GetComponent<RaycastMotor2D>();
            return motor;
        }
        if (motor == null) motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();   // 정적 공격 질의만 쓴다(이동하지 않는다)
        return motor;
    }

    public void OnPropSpawned(DungeonProp prop)
    {
        StopChain();
        exploded = false;
        LastEnemyHits = 0;
        LastPlayerHit = false;
        trapDamage.Cancel();
        // DestructibleProp.OnPropReturning이 Broken 구독자를 지우므로 대여마다 다시 건다.
        if (health != null) { health.Broken -= OnBroken; health.Broken += OnBroken; }
    }

    public void OnPropReturning(DungeonProp prop)
    {
        StopChain();
        trapDamage.Cancel();
    }

    private void OnDisable()
    {
        chainRoutine = null;   // 꺼지면 코루틴도 멈춘다
    }

    private void StopChain()
    {
        if (chainRoutine != null) StopCoroutine(chainRoutine);
        chainRoutine = null;
    }

    private void OnBroken(IDungeonBreakable _)
    {
        if (exploded || chainRoutine != null) return;
        if (IsBlasting && chainDelay > 0f && isActiveAndEnabled) chainRoutine = StartCoroutine(ChainExplode());
        else Explode();
    }

    private IEnumerator ChainExplode()
    {
        yield return new WaitForSeconds(chainDelay);
        chainRoutine = null;
        Explode();
    }

    /// <summary>지금 터진다(한 번만).</summary>
    public void Explode()
    {
        if (exploded) return;
        exploded = true;
        Vector2 center = blastOrigin != null ? (Vector2)blastOrigin.position : (Vector2)(health != null ? health.Center : transform.position);
        float r = Radius;
        LastCenter = center;
        LastEnemyHits = 0;
        LastPlayerHit = false;
        if (explosionFeedback != null) DungeonPropFeedback.Play(explosionFeedback, center, Quaternion.identity);

        blastDepth++;
        try
        {
            DamagePlayer(center, r);
            DamageEnemies(center, r);
        }
        finally { blastDepth--; }
    }

    private void DamagePlayer(Vector2 center, float r)
    {
        if (trapDamage.Damage <= 0f && !trapDamage.ReturnToStart) return;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || player.Health == null || !player.gameObject.activeInHierarchy) return;
        Bounds body = PlayerBody(player);
        Vector3 closest = body.ClosestPoint(new Vector3(center.x, center.y, body.center.z));
        if (((Vector2)closest - center).sqrMagnitude > r * r) return;
        var info = new WeaponBaseSO.DamageInfo { damage = trapDamage.Damage, damageType = DamageType.Normal, knockbackOrigin = center };
        LastPlayerHit = trapDamage.TryDamage(this, player.Health, info, closest);
    }

    /// <summary>Player 몸통(Player 레이어의 트리거 아닌 Collider)의 범위. 못 찾으면 위치 둘레 0.8×1.6칸.</summary>
    // PlayerBody가 매번 다시 쓰는 목록·레이어 번호(물리 틱마다 불려도 배열 할당·문자열 검색을 하지 않는다).
    private static readonly System.Collections.Generic.List<Collider2D> bodyScratch = new System.Collections.Generic.List<Collider2D>();
    private static int playerLayerCache = int.MinValue;

    public static Bounds PlayerBody(PlayerController player)
    {
        if (playerLayerCache == int.MinValue) playerLayerCache = LayerMask.NameToLayer("Player");
        int playerLayer = playerLayerCache;
        Collider2D best = null;
        player.GetComponentsInChildren(bodyScratch);
        foreach (var c in bodyScratch)
        {
            if (c == null || !c.enabled || c.isTrigger) continue;
            if (best == null || c.gameObject.layer == playerLayer && best.gameObject.layer != playerLayer) best = c;
            if (c.transform == player.transform) { best = c; break; }
        }
        bodyScratch.Clear();
        if (best != null) return best.bounds;
        return new Bounds(player.transform.position, new Vector3(0.8f, 1.6f, 0.1f));
    }

    private void DamageEnemies(Vector2 center, float r)
    {
        if (activeEnemyDamage <= 0f || ResolveMotor() == null) return;
        var filter = new ContactFilter2D();
        filter.SetLayerMask(enemyMask);
        filter.useTriggers = true;
        RaycastMotor2D.InvalidateAttackQuerySync();
        motor.OverlapAttackCircle(center, r, filter, hits);
        damaged.Clear();
        // 목록을 먼저 복사한다: 맞은 Prop이 부서지며 다른 질의를 부르면 같은 목록이 지워질 수 있다.
        var targets = new List<(EnemyHealth enemy, Vector2 point)>(hits.Count);
        foreach (var hit in hits)
        {
            if (hit == null) continue;
            var enemy = hit.GetComponentInParent<EnemyHealth>();
            if (enemy == null || enemy == health || !damaged.Add(enemy)) continue;
            targets.Add((enemy, hit.ClosestPoint(center)));
        }
        hits.Clear();
        var info = new WeaponBaseSO.DamageInfo
        {
            damage = activeEnemyDamage,
            damageType = DamageType.Normal,
            knockbackOrigin = center,
            knockbackFacing = 0,
        };
        foreach (var (enemy, point) in targets)
            if (WeaponTraitEffects.ApplyEnemyHit(enemy, info, point)) LastEnemyHits++;
    }

    // ── 배치별 설정 ─────────────────────────────

    private WaveDeviceParameter RadiusParameter => new WaveDeviceParameter(
        RadiusKey, "폭발 반경", "부서질 때 터지는 원의 반지름(칸)입니다.", "칸", 0.5f, 8f, 0.25f, radius, true);
    private WaveDeviceParameter EnemyDamageParameter => new WaveDeviceParameter(
        EnemyDamageKey, "적 피해량", "폭발 반경 안의 적과 다른 파괴형 Prop이 받는 피해입니다.", "", 0f, 10000f, 1f, enemyDamage);

    public WaveDeviceParameter[] PropParameters => new[]
    {
        WaveTrapDamage.DamageParameter(playerDamage), EnemyDamageParameter, RadiusParameter, WaveTrapDamage.ReturnParameter,
    };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        trapDamage.ApplySettings(overrides, playerDamage);
        activeEnemyDamage = WaveDeviceSettings.Value(overrides, EnemyDamageParameter);
        activeRadius = WaveDeviceSettings.Value(overrides, RadiusParameter);
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        float r = WaveDeviceSettings.Value(overrides, RadiusParameter);
        Vector2 center = blastOrigin != null ? (Vector2)transform.InverseTransformPoint(blastOrigin.position) : Vector2.zero;
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Circle, center, new Vector2(r, r), new Color(1f, .22f, .16f, .9f));
        return true;
    }
}
