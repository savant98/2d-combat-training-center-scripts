using UnityEngine;

// 핸드캐논 화상 기폭(2026-11-21 사용자 결정 '기폭형'). 핸드캐논 직격을 맞은 대상이 지니고 있던 화상 스택 n만큼
// 그 대상 한 명에게 연속 폭발 피해를 준다(스택은 HandCannonWeaponSO가 EnemyBurn.Detonate로 먼저 소모한다).
// - 리듬은 Stomper 사망 지속 폭발(Stomper_Destroy.anim의 DestroyRepeatFeedbackStart, 1/6초 간격)을 따른다. 첫 폭발은 직격 FirstDelay초 뒤.
// - 대상 한 명만 맞히므로 물리 질의를 하지 않는다. 대상 Collider의 경계만 읽어 폭발 자리(경계 안 중심 쪽 70%의 무작위 점)를 고른다.
// - 대상이 도중에 죽거나 꺼지면 피해는 멈추고, 남은 폭발 연출은 마지막으로 본 자리에서 끝까지 이어 보인다.
// - Player가 쓰러지거나 무장 특성이 초기화되면(PlayerWeaponTraits.Generation) 그 자리에서 멈춘다(SwordWave.SameLife와 같은 규칙).
// - 시간은 Time.deltaTime으로 재므로 히트스톱 동안 멈춘다. 기술 게이지(OnHitEnemy)는 주지 않는다.
// 실행기는 SwordWave처럼 new GameObject + AddComponent로 만들고 끝나면 스스로 지운다.
public sealed class BurnDetonation : MonoBehaviour
{
    // 2026-11-21 사용자 지시 3차: 폭발이 터지는 속도 1.5배(첫 0.1 → 0.0667초, 간격 1/6 → 1/9초)
    public const float FirstDelay = 0.1f / 1.5f;
    public const float Interval = 1f / 9f;
    private const float SpreadRatio = 0.7f;

    /// <summary>지금 진행 중인 기폭 실행기 수(검증용).</summary>
    public static int ActiveCount { get; private set; }

    private PlayerController player;
    private PlayerWeaponTraits traits;
    private uint generation;
    private bool hasOwner;
    private EnemyHealth target;
    private Collider2D body;
    private Bounds lastBounds;
    private WeaponBaseSO.DamageInfo hit;
    private float timer;
    private bool damageStopped;
    private bool counted;

    public EnemyHealth Target => target;
    /// <summary>아직 남은 폭발 수.</summary>
    public int Remaining { get; private set; }
    /// <summary>지금까지 낸 폭발 수.</summary>
    public int Exploded { get; private set; }
    /// <summary>지금까지 실제로 피해가 들어간 폭발 수.</summary>
    public int DamagedCount { get; private set; }

    private bool SameLife => !hasOwner
        || (player != null && player.Health != null && !player.Health.IsDead && (traits == null || traits.Generation == generation));
    private bool TargetAlive => target != null && target.isActiveAndEnabled && target.currentHP > 0f;

    /// <summary>
    /// 대상에게 count번 연속 폭발을 시작한다. damage는 한 번의 폭발 피해(출처·넉백 정보 포함)다.
    /// body는 폭발 자리를 고를 대상의 Collider(없으면 대상의 첫 Collider2D)다. startBounds는 직격 전에 읽어 둔 대상 경계로,
    /// 직격으로 대상이 죽어 꺼졌어도 그 자리에서 연출을 낸다(없으면 지금 body의 경계).
    /// </summary>
    public static BurnDetonation Begin(CharacterController2D owner, EnemyHealth target, Collider2D body, int count,
        WeaponBaseSO.DamageInfo damage, Bounds? startBounds = null)
    {
        if (target == null || count <= 0) return null;
        var detonation = new GameObject("BurnDetonation").AddComponent<BurnDetonation>();
        detonation.Setup(owner as PlayerController, target, body, count, damage, startBounds);
        return detonation;
    }

    private void Setup(PlayerController owner, EnemyHealth enemy, Collider2D collider, int count, WeaponBaseSO.DamageInfo damage,
        Bounds? startBounds)
    {
        player = owner;
        hasOwner = owner != null;
        traits = hasOwner ? owner.AbilityManager?.AttackAbility?.Traits : null;
        generation = traits != null ? traits.Generation : 0u;
        target = enemy;
        body = collider != null ? collider : enemy.GetComponentInChildren<Collider2D>();
        if (startBounds.HasValue) lastBounds = startBounds.Value;
        else if (body != null && body.enabled && body.gameObject.activeInHierarchy) lastBounds = body.bounds;
        else lastBounds = new Bounds(enemy.transform.position, Vector3.one);
        hit = damage;
        Remaining = count;
        timer = FirstDelay;
        if (!counted) { counted = true; ActiveCount++; }
        TrackTarget();
        transform.position = lastBounds.center;
    }

    private void Update()
    {
        if (!SameLife) { Destroy(gameObject); return; }
        TrackTarget();
        timer -= Time.deltaTime;
        while (Remaining > 0 && timer <= 0f)
        {
            Explode();
            Remaining--;
            timer += Interval;
        }
        if (Remaining <= 0) Destroy(gameObject);
    }

    // 대상이 살아 있는 동안은 대상을 따라가며 경계를 기억한다. 한 번이라도 죽거나 꺼지면(풀 반환 뒤 다른 적으로 다시 쓰일 수 있다) 피해를 영영 멈춘다.
    private void TrackTarget()
    {
        if (damageStopped) return;
        if (!TargetAlive) { damageStopped = true; return; }
        if (body != null && body.enabled && body.gameObject.activeInHierarchy) lastBounds = body.bounds;
        else lastBounds = new Bounds(target.transform.position, lastBounds.size);
        transform.position = lastBounds.center;
    }

    private void Explode()
    {
        Vector3 extents = lastBounds.extents * SpreadRatio;
        Vector3 center = lastBounds.center;
        var point = new Vector3(center.x + Random.Range(-extents.x, extents.x), center.y + Random.Range(-extents.y, extents.y), center.z);
        if (!damageStopped)
        {
            if (TargetAlive && WeaponTraitEffects.ApplyEnemyHit(target, hit, point)) DamagedCount++;
            if (!TargetAlive) damageStopped = true;
        }
        Exploded++;
        WeaponTraitFeedbackSO.Play(WeaponTraitCue.BurnDetonate, point, Vector2.up);
    }

    private void OnDestroy()
    {
        if (!counted) return;
        counted = false;
        ActiveCount = Mathf.Max(0, ActiveCount - 1);
    }
}
