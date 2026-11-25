using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 파괴형 던전 Prop의 체력.
// - 기존 무기 판정(근접 MeleeHitbox·탄·레이저·레일건·검기 등)이 그대로 맞히도록 EnemyHealth를 상속한다(Enemy 레이어 hurtbox + 부모의 EnemyHealth).
// - 적 전용 처리를 피하려고 TakeDamage를 base 호출 없이 다시 구현한다: 넉백·정적 EnemyHealth.DamageApplied(훈련 통계·해머 방어막)·
//   enemyUI(이 Prop에는 없다 — base는 null 검사 없이 부른다)·피해 숫자를 모두 거치지 않는다. 쉴드 체계(enemyShieldSystem)는 두지 않는다.
// - 자동 조준·별창 유도·드론 표적은 IDungeonPropTarget 표식으로 뺀다(무기 파일 가드, DungeonPropTargeting.IsProp).
//   스킬 게이지 적립(PlayerWeaponAbility.OnWeaponHitEnemy)은 맞은 대상을 받지 않으므로, 무기 출처가 있는 피격을 같은 프레임 동안 기억하고
//   OnWeaponHitEnemy가 ConsumeRewardHit(무기, 소유자, hitPoint)로 확인해 적립을 건너뛴다(규칙은 DungeonPropHitLog 참고).
// - 맞으면 표시 묶음이 짧게 흔들리고 발광 부품이 번쩍인다(물리 몸체·Collider는 움직이지 않는다). 체력 0이면 Break:
//   파편 효과·표시 숨김·모든 Collider 끔·Broken 이벤트 → returnDelay 뒤 풀 반환. 풀에서 다시 꺼내면 체력·상태를 처음으로 되돌린다.
// - 화상(EnemyBurn)은 Prop에도 붙어 틱 피해를 준다(불에 타는 상자). 부서지거나 다시 꺼낼 때 지운다.
// - 4차(R9): 같은 루트에 중력 본체(DungeonPropBody)가 있으면 부서지는 순간 Collider를 끄기 전에 멈춘다(꺼진 몸통으로 광선을 쏘지 않게).
//   맞을 때 맞은 자리에서 hitParticles를 튀기고, 부서질 때 breakParticles(파편·불꽃·유리 조각)를 한 번 내보낸다(모두 표시 묶음 밖·월드 공간).
//   잔해 표시(brokenVisual) 안의 불씨(brokenGlowRenderers)는 부서진 뒤 풀 반환까지 서서히 어두워진다. 크기 변형 없음.
[DisallowMultipleComponent]
public class DestructibleProp : EnemyHealth, IDungeonPropTarget, IDungeonBreakable, IDungeonPropPart, IDungeonPropSettings
{
    public const string DurabilityKey = "propDurability";

    [Header("파괴형 Prop")]
    [Tooltip("기본 체력(배치별 설정 '내구도'로 바꿀 수 있다). 해머 1·2·3타 = 10·24·32.")]
    [SerializeField, Min(1f)] private float durability = 30f;
    [Tooltip("맞을 때마다 내는 작은 효과(불꽃 등). 비워도 된다.")]
    [SerializeField] private FeedbackDataSO hitFeedback;
    [Tooltip("부서질 때 내는 파편 효과. hurtbox 중심에서 재생한다.")]
    [SerializeField] private FeedbackDataSO breakFeedback;
    [Tooltip("맞을 때 번쩍이는 발광 부품(흰 외곽선 등). 비우면 번쩍임 없음.")]
    [SerializeField] private Renderer[] flashRenderers = Array.Empty<Renderer>();
    [Tooltip("번쩍일 때 밝기 배율(원래 밝기에 더하는 양).")]
    [SerializeField, Min(0f)] private float flashBoost = 1.6f;
    [SerializeField, Min(0.01f)] private float flashTime = 0.12f;
    [Tooltip("피격 흔들림 폭(칸). 표시 묶음만 흔든다.")]
    [SerializeField, Min(0f)] private float hitShakeDistance = 0.05f;
    [SerializeField, Min(0.01f)] private float hitShakeTime = 0.14f;
    [Tooltip("부서진 뒤 남기는 잔해 표시(선택, 평소 꺼 둔다). 표시 묶음(Visual) 밖에 둔다. 풀 반환 전까지 보인다.")]
    [SerializeField] private GameObject brokenVisual;
    [Tooltip("잔해 표시 안의 발광 부품(불씨·남은 유리). 부서진 뒤 풀 반환까지 서서히 어두워진다. 비워도 된다.")]
    [SerializeField] private Renderer[] brokenGlowRenderers = Array.Empty<Renderer>();
    [Tooltip("맞을 때 맞은 자리에서 튀는 입자(월드 공간, Emit으로만 낸다). 표시 묶음 밖에 둔다. 비워도 된다.")]
    [SerializeField] private ParticleSystem hitParticles;
    [SerializeField, Min(0)] private int hitParticleCount = 6;
    [Tooltip("부서질 때 한 번 내보내는 입자 묶음(파편·불꽃·유리 조각). 표시 묶음 밖(루트 아래)에 둔다.")]
    [SerializeField] private ParticleBurst[] breakParticles = Array.Empty<ParticleBurst>();
    [Tooltip("부서진 뒤 스스로 풀로 돌아가기까지의 시간(초). 0보다 작으면 스스로 돌아가지 않는다(방 정리가 돌려보낸다).")]
    [SerializeField] private float returnDelay = 0.6f;
    [Tooltip("피해량·번쩍임 기준 위치로 쓰는 hurtbox(Enemy 레이어 트리거). 비우면 자식에서 Enemy 레이어 Collider를 찾는다.")]
    [SerializeField] private Collider2D hurtbox;

    /// <summary>부서질 때 한 번 내보내는 입자 하나(입자 계통 + 개수).</summary>
    [Serializable]
    public struct ParticleBurst
    {
        public ParticleSystem system;
        [Min(0)] public int count;
    }

    private const float EmberFloor = .08f;      // 불씨가 꺼져 갈 때의 마지막 밝기 배율
    private const float EmberFadeTime = 1.5f;   // 스스로 돌아가지 않는 Prop(returnDelay < 0)의 불씨가 어두워지는 시간

    private DungeonProp prop;
    private DungeonPropBody body;
    private DungeonPropGlow glow, brokenGlow;
    private Coroutine hitRoutine, returnRoutine;
    private bool broken;
    private float lastAppliedMax = -1f;

    /// <summary>
    /// 무기 적중 보상(PlayerWeaponAbility.OnWeaponHitEnemy)이 부르는 확인: 이번 프레임에 같은 무기(source)가 파괴형 Prop을 때린 적중이면
    /// 그 기록을 지우고 true. 보상 호출은 맞은 대상을 넘기지 않아 무기 출처·피격 지점·피해 순서로 맞춘다(DungeonPropHitLog).
    /// </summary>
    public static bool ConsumeRewardHit(WeaponBaseSO source, CharacterController2D owner, Vector2 hitPoint) =>
        DungeonPropHitLog.ConsumeRewardHit(source, owner, hitPoint);

    public bool IsBroken => broken;
    public event Action<IDungeonBreakable> Broken;
    public DungeonProp Prop => prop != null ? prop : prop = GetComponent<DungeonProp>();
    public Collider2D Hurtbox => hurtbox;
    /// <summary>부서진 위치(hurtbox 중심). 폭발 부품이 읽는다.</summary>
    public Vector3 Center => hurtbox != null ? hurtbox.bounds.center : transform.position;
    /// <summary>마지막으로 받은 피해 정보(폭발 연쇄 판정용).</summary>
    public WeaponBaseSO.DamageInfo LastDamage { get; private set; }
    /// <summary>잔해 표시(없으면 null). 검사가 부서진 뒤 켜졌는지 본다.</summary>
    public GameObject BrokenVisual => brokenVisual;
    /// <summary>피격 입자(없으면 null).</summary>
    public ParticleSystem HitParticles => hitParticles;
    /// <summary>마지막으로 부서질 때 내보낸 입자 수(검사용).</summary>
    public int LastBreakParticles { get; private set; }
    /// <summary>이번 대여에서 피격 입자를 낸 횟수(검사용).</summary>
    public int HitEffects { get; private set; }
    private bool HasEmbers => brokenGlow != null && brokenGlow.Count > 0;

    protected override void Awake()
    {
        // EnemyHealth.Awake: healthData 없음 → maxHP 유지, EnemyController 없음(넉백 없음), enemyShieldSystem 없음 → 아무 의존도 요구하지 않는다.
        maxHP = durability;
        base.Awake();
        prop = GetComponent<DungeonProp>();
        body = GetComponent<DungeonPropBody>();
        if (hurtbox == null) hurtbox = FindHurtbox();
        glow = new DungeonPropGlow(flashRenderers);
        brokenGlow = new DungeonPropGlow(brokenGlowRenderers);
        DungeonPropHitLog.EnsureHooked();   // 실제 적 피해 순서 기록(적 보상이 Prop 기록에 먹히지 않게)
        if (enemyShieldSystem != null)
        {
            Debug.LogWarning($"[DestructibleProp] {name}: 파괴형 Prop에는 쉴드를 두지 않는다. 연결을 무시한다.", this);
            enemyShieldSystem = null;
        }
        IsBossEnemy = false;
        currentHP = maxHP;
    }

    private Collider2D FindHurtbox()
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        foreach (var c in GetComponentsInChildren<Collider2D>(true))
            if (c != null && c.gameObject.layer == enemyLayer) return c;
        return null;
    }

    // ── IDungeonPropPart ─────────────────────────────

    public void OnPropSpawned(DungeonProp owner)
    {
        prop = owner;
        StopRoutines();
        broken = false;
        IsInvincible = false;
        if (brokenVisual != null) brokenVisual.SetActive(false);
        glow?.Clear();
        brokenGlow?.Clear();
        ClearParticles();
        LastBreakParticles = 0;
        HitEffects = 0;
        ResetHealth();
    }

    public void OnPropReturning(DungeonProp owner)
    {
        StopRoutines();
        glow?.Clear();
        brokenGlow?.Clear();
        GetComponent<EnemyBurn>()?.Clear();
        Broken = null;   // 방 정리가 다음 대여에서 새로 구독한다
    }

    private void StopRoutines()
    {
        if (hitRoutine != null) StopCoroutine(hitRoutine);
        if (returnRoutine != null) StopCoroutine(returnRoutine);
        hitRoutine = returnRoutine = null;
        var p = Prop;
        if (p != null && p.VisualRoot != null) p.VisualRoot.localPosition = p.VisualHome;
    }

    // ── 체력 ─────────────────────────────

    public override void ResetHealth()
    {
        // base(EnemyHealth.ResetHealth)는 hitSprites를 null 검사 없이 훑는다. 같은 일을 여기서 안전하게 한다.
        currentHP = maxHP;
        GetComponent<EnemyBurn>()?.Clear();
    }

    public override void TakeDamage(WeaponBaseSO.DamageInfo damageInfo, Vector3 hitPosition)
    {
        // 피해가 막혀도 무기는 곧바로 OnHitEnemy를 부른다 — 적립 건너뛰기 기록은 먼저 남긴다(무기 출처가 없는 피해는 기록하지 않는다).
        DungeonPropHitLog.Record(this, damageInfo, hitPosition);
        if (broken || IsDamageBlocked || !isActiveAndEnabled) return;
        float damage = damageInfo.damage;
        if (!(damage > 0f) || float.IsInfinity(damage)) return;
        LastDamage = damageInfo;
        currentHP = Mathf.Max(0f, currentHP - damage);
        if (currentHP <= 0f)
        {
            Break(hitPosition);
            return;
        }
        if (hitFeedback != null) DungeonPropFeedback.Play(hitFeedback, hitPosition, Quaternion.identity);
        EmitHit(hitPosition, hitParticleCount);
        if (hitRoutine != null) StopCoroutine(hitRoutine);
        hitRoutine = StartCoroutine(HitReaction(hitPosition));
    }

    protected override void Die() => Break(Center);

    /// <summary>체력과 관계없이 지금 부순다(검사·연출용). 이미 부서졌으면 무시한다.</summary>
    public void Break(Vector3 point)
    {
        if (broken) return;
        broken = true;
        currentHP = 0f;
        StopRoutines();
        glow?.Clear();
        GetComponent<EnemyBurn>()?.Clear();
        Vector3 center = Center;   // Collider를 끄기 전에 읽는다
        if (body != null) body.Halt();   // 중력 본체: 꺼질 몸통으로 광선을 쏘지 않게 먼저 멈춘다
        var p = Prop;
        if (p != null)
        {
            p.SetCollidersEnabled(false);
            p.SetVisualVisible(false);
        }
        else
        {
            foreach (var c in GetComponentsInChildren<Collider2D>(true)) c.enabled = false;
            RaycastMotor2D.InvalidateAttackQuerySync();
        }
        if (brokenVisual != null) brokenVisual.SetActive(true);
        if (breakFeedback != null) DungeonPropFeedback.Play(breakFeedback, center, Quaternion.identity);
        EmitHit(point, hitParticleCount * 2);
        EmitBreak();
        Broken?.Invoke(this);
        OnDeath?.Invoke();
        if ((returnDelay >= 0f || HasEmbers) && isActiveAndEnabled) returnRoutine = StartCoroutine(ReturnAfterBreak());
    }

    // 맞은 자리에서 피격 입자를 튀긴다(입자 계통의 모양은 그 자리 기준으로 더한다).
    private void EmitHit(Vector3 point, int count)
    {
        if (hitParticles == null || count <= 0 || !hitParticles.gameObject.activeInHierarchy) return;
        var emit = new ParticleSystem.EmitParams
        {
            position = new Vector3(point.x, point.y, hitParticles.transform.position.z),
            applyShapeToPosition = true,
        };
        hitParticles.Emit(emit, count);
        HitEffects++;
    }

    private void EmitBreak()
    {
        int total = 0;
        if (breakParticles != null)
            foreach (var burst in breakParticles)
            {
                if (burst.system == null || burst.count <= 0 || !burst.system.gameObject.activeInHierarchy) continue;
                burst.system.Emit(burst.count);
                total += burst.count;
            }
        LastBreakParticles = total;
    }

    private void ClearParticles()
    {
        if (hitParticles != null) hitParticles.Clear(true);
        if (breakParticles == null) return;
        foreach (var burst in breakParticles)
            if (burst.system != null) burst.system.Clear(true);
    }

    private IEnumerator ReturnAfterBreak()
    {
        // 잔해 불씨: 풀 반환까지(스스로 돌아가지 않으면 EmberFadeTime 동안) 밝기를 1 → EmberFloor로 줄인다.
        float fade = returnDelay >= 0f ? returnDelay : HasEmbers ? EmberFadeTime : 0f;
        for (float t = 0f; t < fade; t += Time.deltaTime)
        {
            if (HasEmbers)
            {
                float k = Mathf.Clamp01(t / fade);
                brokenGlow.Set(Mathf.Lerp(1f, EmberFloor, k * k * (3f - 2f * k)));
            }
            yield return null;
        }
        if (HasEmbers) brokenGlow.Set(EmberFloor);
        if (returnDelay < 0f) { returnRoutine = null; yield break; }   // 방 정리가 돌려보낸다
        // 연쇄 폭발처럼 부서진 뒤에도 할 일이 남은 부품이 있으면 끝날 때까지 기다린다.
        var explosive = GetComponent<PropExplosive>();
        while (explosive != null && explosive.IsPending) yield return null;
        returnRoutine = null;
        var p = Prop;
        if (p != null) p.ForceReturnToPool();
    }

    private IEnumerator HitReaction(Vector3 hitPosition)
    {
        var p = Prop;
        Transform visual = p != null ? p.VisualRoot : null;
        Vector3 home = p != null ? p.VisualHome : Vector3.zero;
        // 맞은 쪽 반대로 먼저 밀렸다가 줄어드는 흔들림(표시만).
        Vector2 away = (Vector2)(Center - hitPosition);
        float side = Mathf.Abs(away.x) > 1e-3f ? Mathf.Sign(away.x) : 1f;
        float duration = Mathf.Max(hitShakeTime, flashTime);
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float shake = t < hitShakeTime ? 1f - t / hitShakeTime : 0f;
            if (visual != null)
            {
                float wave = Mathf.Cos(t / hitShakeTime * Mathf.PI * 3f);
                visual.localPosition = home + new Vector3(side * hitShakeDistance * shake * wave, 0f, 0f);
            }
            if (glow != null && glow.Count > 0)
            {
                float flash = t < flashTime ? 1f - t / flashTime : 0f;
                glow.Set(1f + flashBoost * flash * flash);
            }
            yield return null;
        }
        if (visual != null) visual.localPosition = home;
        glow?.Clear();
        hitRoutine = null;
    }

    // ── 배치별 설정 ─────────────────────────────

    public WaveDeviceParameter DurabilityParameter => new WaveDeviceParameter(
        DurabilityKey, "내구도", "부서지기까지 받는 피해량입니다(해머 1·2·3타 = 10·24·32).", "", 1f, 5000f, 1f, durability);

    public WaveDeviceParameter[] PropParameters => new[] { DurabilityParameter };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        float value = WaveDeviceSettings.Value(overrides, DurabilityParameter);
        bool untouched = !Application.isPlaying || lastAppliedMax < 0f || Mathf.Approximately(currentHP, lastAppliedMax) || Mathf.Approximately(currentHP, maxHP);
        maxHP = value;
        lastAppliedMax = value;
        if (!broken && untouched) currentHP = value;
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        range = default;
        return false;
    }
}

/// <summary>
/// 파괴형 Prop 적중이 기술 게이지를 올리지 않게 하는 같은 프레임 기록. 무기의 적중 보상(OnHitEnemy → PlayerWeaponAbility.OnWeaponHitEnemy)은
/// 맞은 대상을 넘기지 않는다. 대신 보상 무기는 모두 '대상 피해 → (부가 피해) → 그 대상의 OnHitEnemy'를 한 호출 안에서 차례로 부른다
/// (MeleeHitbox·ProjectileBase·IncendiaryShot·LandMine·StarSpear·PlayerLaserBeam·RailgunWeaponSO·HandCannonWeaponSO·SwordWave·SwordDashSlash).
/// 일격필살(SwordFinisherSlash)은 4차(R1)부터 대상을 모을 때 파괴형 Prop을 빼므로 이 기록을 남기지 않는다.
/// - Prop 기록: 무기 출처(sourceWeapon)가 있는 피해만 남긴다. 화상 틱·Prop 폭발·증기 배출구처럼 출처 없는 피해는 OnHitEnemy가 따르지 않으므로 남기지 않는다.
/// - 적 기록: 무기 출처가 있는 피해로 실제 적의 체력·쉴드가 깎이면(EnemyHealth.DamageApplied·EnemyShieldSystem.ShieldDamageApplied) 순서만 남긴다.
///   작열탄 직격처럼 '적 피해 → 폭발 부가 피해(Prop) → 그 적의 OnHitEnemy' 순서이면, 앞선 적 기록이 보상 지점을 덮으므로 적 보상으로 본다.
/// - 보상 확인(같은 프레임·같은 무기 출처·같은 소유자만): Prop 기록은 피격 지점이 보상 지점과 같을 때(1e-4칸), 또는 지뢰처럼 보상 지점이
///   그 hurtbox에서 피격 지점에 가장 가까운 점일 때만 맞는다(범위 일치 없음). 맞은 Prop 기록보다 앞선 적 기록 가운데 그 적의 몸
///   (Enemy 레이어 Collider 경계 + 0.05칸)이 보상 지점을 덮는 것이 있으면 적 적중으로 보고 그 적 기록과 가려진 Prop 기록을 지운다.
///   맞는 Prop 기록이 없으면 적 적중이다(보상 지점을 덮는 적 기록이 있으면 그것을 지운다).
/// - 맞은 기록 하나만 지운다(한 번의 휘두름이 Prop과 적을 모두 맞히면 각 보상이 자기 기록을 하나씩 가져간다).
/// </summary>
public static class DungeonPropHitLog
{
    private struct Entry
    {
        public bool isProp;
        public int frame;
        public WeaponBaseSO source;
        public CharacterController2D owner;
        public Vector2 point;        // Prop: 피격 지점
        public Vector2 edgePoint;    // Prop: hurtbox에서 피격 지점에 가장 가까운 점(지뢰처럼 보상 지점을 Collider.ClosestPoint로 잡는 무기)
        public bool hasEdge;
        public EnemyHealth enemy;    // 적 기록: 맞은 적(이벤트에 지점이 없어 몸 경계로 본다)
    }

    private const float ExactDistance = 1e-4f;
    private const float EnemyBodyMargin = 0.05f;
    private static readonly List<Entry> entries = new List<Entry>(16);
    private static readonly List<Collider2D> bodyBuffer = new List<Collider2D>(8);
    private static int enemyLayer = -2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEntries()
    {
        entries.Clear();
        enemyLayer = -2;
    }

    /// <summary>
    /// 실제 적 피해 이벤트를 구독한다. 두 이벤트는 SubsystemRegistration에서 비워지므로 파괴형 Prop이 깨어날 때마다 다시 건다(중복 없이).
    /// </summary>
    public static void EnsureHooked()
    {
        EnemyHealth.DamageApplied -= OnEnemyDamaged;
        EnemyHealth.DamageApplied += OnEnemyDamaged;
        EnemyShieldSystem.ShieldDamageApplied -= OnEnemyDamaged;
        EnemyShieldSystem.ShieldDamageApplied += OnEnemyDamaged;
    }

    private static void OnEnemyDamaged(EnemyHealth enemy, WeaponBaseSO.DamageInfo info, float applied)
    {
        if (enemy == null || enemy is DestructibleProp || info.sourceWeapon == null) return;
        int frame = Time.frameCount;
        Prune(frame);
        entries.Add(new Entry { isProp = false, frame = frame, source = info.sourceWeapon, owner = info.sourceOwner, enemy = enemy });
    }

    public static void Record(DestructibleProp prop, WeaponBaseSO.DamageInfo info, Vector3 point)
    {
        // 무기 출처가 없는 피해(화상 틱·Prop 폭발·증기 배출구)는 보상 호출이 따르지 않는다 — 남기면 같은 프레임의 다른 적 보상을 먹는다.
        if (info.sourceWeapon == null) return;
        int frame = Time.frameCount;
        Prune(frame);
        var box = prop != null ? prop.Hurtbox : null;
        bool hasEdge = box != null && box.enabled && box.gameObject.activeInHierarchy;
        entries.Add(new Entry
        {
            isProp = true,
            frame = frame,
            source = info.sourceWeapon,
            owner = info.sourceOwner,
            point = point,
            edgePoint = hasEdge ? box.ClosestPoint(point) : (Vector2)point,
            hasEdge = hasEdge,
        });
    }

    /// <summary>같은 프레임에 같은 무기(source)가 파괴형 Prop을 때린 적중이면 그 기록을 지우고 true. 적 적중이면 false.</summary>
    public static bool ConsumeRewardHit(WeaponBaseSO source, CharacterController2D owner, Vector2 hitPoint)
    {
        int frame = Time.frameCount;
        Prune(frame);
        if (entries.Count == 0 || source == null) return false;

        int prop = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (!e.isProp || !SameSource(e, source, owner)) continue;
            if (Near(e.point, hitPoint) || (e.hasEdge && Near(e.edgePoint, hitPoint))) { prop = i; break; }
        }

        // Prop 기록보다 앞선 같은 출처의 적 기록이 보상 지점을 덮으면 그 적의 적중이다(작열탄 직격 뒤 폭발이 Prop에 닿은 경우 등).
        // 이때 가려진 Prop 기록은 그 적중의 부가 피해라 자기 보상 호출이 없다 — 함께 지워 같은 프레임의 다음 적 보상을 먹지 않게 한다.
        int limit = prop >= 0 ? prop : entries.Count;
        for (int i = 0; i < limit; i++)
        {
            var e = entries[i];
            if (e.isProp || !SameSource(e, source, owner) || !Covers(e.enemy, hitPoint)) continue;
            if (prop >= 0) entries.RemoveAt(prop);   // prop > i이므로 먼저 지워도 i는 그대로다
            entries.RemoveAt(i);
            return false;
        }
        if (prop < 0) return false;
        entries.RemoveAt(prop);
        return true;
    }

    private static bool SameSource(in Entry e, WeaponBaseSO source, CharacterController2D owner) =>
        e.source == source && (e.owner == null || owner == null || e.owner == owner);

    private static bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude <= ExactDistance * ExactDistance;

    // 적의 몸(Enemy 레이어의 켜진 Collider 경계 + 여유)이 지점을 덮는지. 적이 죽어 몸을 읽을 수 없으면 덮는 것으로 본다(적 보상을 잃지 않게).
    private static bool Covers(EnemyHealth enemy, Vector2 point)
    {
        if (enemy == null || !enemy.isActiveAndEnabled) return true;
        if (enemyLayer == -2) enemyLayer = LayerMask.NameToLayer("Enemy");
        bodyBuffer.Clear();
        enemy.GetComponentsInChildren(false, bodyBuffer);
        bool readable = false, covers = false;
        foreach (var c in bodyBuffer)
        {
            if (c == null || !c.enabled || (enemyLayer >= 0 && c.gameObject.layer != enemyLayer)) continue;
            readable = true;
            var b = c.bounds;
            b.Expand(EnemyBodyMargin * 2f);
            if (b.Contains(new Vector3(point.x, point.y, b.center.z))) { covers = true; break; }
        }
        bodyBuffer.Clear();
        return covers || !readable;
    }

    private static void Prune(int frame)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].frame != frame) entries.RemoveAt(i);
    }
}
