using System.Collections.Generic;
using UnityEngine;

// 작열탄(Rifle Gear 2 '작열탄 발사' 동안의 기본 공격). 관통하며 한 탄이 같은 적을 한 번만 맞힌다.
// 2026-11-21 사용자 결정: 무조건 주던 50% 추가타를 없애고, 이 탄을 맞기 전에 화상이 1스택 이상이었던 적을 맞히면 폭발을 낸다.
// 그다음 화상을 1스택 부여한다. 탄 외형(불꽃·연기 궤적)은 IncendiaryBullet Prefab이 맡는다.
// 2026-11-21 사용자 지시 4차 "작열탄의 추가 폭발이 크기가 별창폭발의 폭발범위만큼 커지고, 추가 폭발을 맞으면 그 대상도 화상스택이 올라가도록":
// - 폭발은 맞은 자리를 중심으로 반경 IncendiaryRifleWeaponSO.blastRadius(별창 폭발 반경, 3.75칸) 안의 모든 적에게
//   탄 피해 × blastDamageScale을 한 번씩 준다. 범위 질의는 별창 폭발처럼 탄 모터의 OverlapAttackCircle을 쓴다.
//   넉백은 없고(추가타 규칙), 기술 게이지(OnHitEnemy)는 직격에서만 준다(화상 기폭과 같음).
// - 폭발에 피해가 들어간 적도 화상 1스택을 받는다. 한 탄이 한 적에게 주는 것은 최대로 직격 1회·폭발 피해 1회·화상 1스택이다.
//   관통하며 화상 대상을 여럿 맞혀 폭발이 여러 번 나도, 이미 이 탄의 폭발을 맞은 적은 다시 맞지 않는다(연출은 폭발마다 낸다).
// - '맞기 전 화상'은 이 탄이 그 적에게 처음 닿기 직전(폭발이든 직격이든)의 스택이다. 이 탄의 폭발로 처음 붙은 화상은 직격 판정에 세지 않는다.
// - 폭발 피해가 막혀도(직격으로 이미 죽음 등) 연출(IncendiaryBlast 큐)은 낸다.
public sealed class IncendiaryShot : StarShot
{
    private readonly HashSet<EnemyHealth> targets = new();                // 직격한 적
    private readonly HashSet<EnemyHealth> blasted = new();                // 이 탄의 폭발 피해를 처리한 적
    private readonly HashSet<EnemyHealth> burned = new();                 // 이 탄이 화상을 붙인 적
    private readonly Dictionary<EnemyHealth, int> stacksBefore = new();   // 이 탄이 처음 닿기 직전의 화상 스택
    private readonly List<Collider2D> blastHits = new(16);
    private ContactFilter2D blastFilter;

    /// <summary>검증용: 지금까지 모든 작열탄이 낸 폭발 수.</summary>
    public static int TotalBlasts { get; private set; }
    /// <summary>검증용: 마지막 폭발의 중심·반경·피해가 들어간 적 수.</summary>
    public static Vector2 LastBlastCenter { get; private set; }
    public static float LastBlastRadius { get; private set; }
    public static int LastBlastDamaged { get; private set; }

    /// <summary>이 탄의 폭발 반경(무기 데이터가 없으면 별창 기본 반경).</summary>
    public float BlastRadius => damageInfo.sourceWeapon is IncendiaryRifleWeaponSO weapon ? weapon.blastRadius : IncendiaryRifleWeaponSO.DefaultBlastRadius;

    protected override void Awake()
    {
        base.Awake();
        blastFilter = new ContactFilter2D();
        blastFilter.SetLayerMask(1 << LayerMask.NameToLayer("Enemy"));
        blastFilter.useTriggers = true;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        targets.Clear();
        blasted.Clear();
        burned.Clear();
        stacksBefore.Clear();
    }

    protected override void HitTarget(HealthBase health, Vector2 hitPoint)
    {
        if (health is not EnemyHealth enemy || !targets.Add(enemy)) return;
        // 이 탄이 닿기 전(그리고 기본 피해로 죽어 스택이 지워지기 전)의 스택으로 판정한다.
        int stacks = StacksBeforeThisShot(enemy);
        bool directHit = WeaponTraitEffects.ApplyEnemyHit(enemy, damageInfo, hitPoint);
        // 화상 중인 브레이크 가능 대상에게는 일반 피해가 막혀도 폭발을 발동한다.
        bool canBlast = directHit || (enemy.enemyShieldSystem?.IsBreakable == true && !enemy.IsDamageBlocked);
        if (stacks >= 1 && ownerFaction == Faction.Player && canBlast) Blast(enemy, hitPoint);
        if (!directHit) return;

        Burn(enemy);
        OnHitEnemy?.Invoke(owner, hitPoint);
        WeaponTraitFeedbackSO.Play(WeaponTraitCue.IncendiaryHit, hitPoint, velocity);
    }

    private int StacksBeforeThisShot(EnemyHealth enemy)
    {
        if (stacksBefore.TryGetValue(enemy, out int stacks)) return stacks;
        stacks = enemy.TryGetComponent<EnemyBurn>(out var burn) ? burn.Stacks : 0;
        stacksBefore[enemy] = stacks;
        return stacks;
    }

    // center를 중심으로 반경 안의 적마다 폭발 피해 1회(직격 대상 먼저). 폭발 연출은 피해와 관계없이 낸다.
    private void Blast(EnemyHealth direct, Vector2 center)
    {
        var weapon = damageInfo.sourceWeapon as IncendiaryRifleWeaponSO;
        float radius = BlastRadius;
        var blast = damageInfo;
        blast.damage *= weapon != null ? weapon.blastDamageScale : 1f;
        blast.damageType = DamageType.Skill;
        blast.knockbackFacing = 0;
        blast.knockbackOrigin = center;

        int damaged = 0;
        if (BlastTarget(direct, blast, center)) damaged++;
        if (motor != null && radius > 0f && blast.damage > 0f)
        {
            RaycastMotor2D.InvalidateAttackQuerySync();
            motor.OverlapAttackCircle(center, radius, blastFilter, blastHits);
            foreach (var hit in blastHits)
            {
                if (hit == null) continue;
                var enemy = hit.GetComponentInParent<EnemyHealth>();
                if (enemy == null || blasted.Contains(enemy)) continue;
                if (BlastTarget(enemy, blast, hit.ClosestPoint(center))) damaged++;
            }
        }
        TotalBlasts++;
        LastBlastCenter = center;
        LastBlastRadius = radius;
        LastBlastDamaged = damaged;
        WeaponTraitFeedbackSO.Play(WeaponTraitCue.IncendiaryBlast, center);
    }

    // 이 탄에서 적마다 폭발 피해 1회. 피해가 들어가면 화상 1스택(이 탄에서 적마다 한 번).
    private bool BlastTarget(EnemyHealth enemy, WeaponBaseSO.DamageInfo blast, Vector2 point)
    {
        if (!blasted.Add(enemy)) return false;
        StacksBeforeThisShot(enemy);   // 폭발로 처음 닿는 적: 화상이 붙기 전 스택을 기억해 둔다(나중에 직격할 때의 판정용)
        if (!WeaponTraitEffects.ApplyEnemyHit(enemy, blast, point)) return false;
        Burn(enemy);
        return true;
    }

    private void Burn(EnemyHealth enemy)
    {
        if (burned.Add(enemy)) EnemyBurn.Apply(enemy);
    }
}
