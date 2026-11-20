using System.Collections.Generic;
using UnityEngine;

public sealed class IncendiaryShot : StarShot
{
    private readonly HashSet<EnemyHealth> targets = new();
    private SpriteRenderer flame;

    protected override void Awake()
    {
        base.Awake();
        flame = WeaponTraitEffects.MakeSprite("IncendiaryFlame", transform, null, 37);
        flame.sprite = WeaponTraitEffects.FlameSprite;
        flame.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
        flame.transform.localScale = new Vector3(1.12f, 2.45f, 1f);
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        targets.Clear();
    }

    protected override void HitTarget(HealthBase health, Vector2 hitPoint)
    {
        if (health is not EnemyHealth enemy || !targets.Add(enemy)) return;
        if (!WeaponTraitEffects.ApplyEnemyHit(enemy, damageInfo, hitPoint)) return;

        var impact = damageInfo;
        impact.damage *= 0.5f;
        impact.knockbackFacing = 0;
        WeaponTraitEffects.ApplyEnemyHit(enemy, impact, hitPoint);
        EnemyBurn.Apply(enemy);
        OnHitEnemy?.Invoke(owner, hitPoint);
        WeaponTraitFeedbackSO.Play(WeaponTraitCue.IncendiaryHit, hitPoint, velocity);
    }
}
