using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class SwordWave : MonoBehaviour
{
    private const float FlightDuration = 0.5f;
    private const float HitInterval = 0.07f;
    private readonly Collider2D[] overlaps = new Collider2D[64];
    private readonly HashSet<EnemyHealth> targets = new();
    private PlayerController player;
    private WeaponBaseSO.DamageInfo hit;
    private Action<CharacterController2D, Vector2> onHit;
    private bool empowered, refunded;
    private int activeSequences;
    private PlayerWeaponTraits traits;
    private uint generation;
    private bool SameLife => player != null && !player.Health.IsDead && traits.Generation == generation;

    public void Begin(PlayerController owner, Vector2 origin, Vector2 end, Vector2 direction,
        LayerMask mask, WeaponBaseSO.DamageInfo damage, bool enhanced,
        Action<CharacterController2D, Vector2> onHitEnemy)
    {
        player = owner;
        traits = owner.AbilityManager.AttackAbility.Traits;
        generation = traits.Generation;
        hit = damage;
        onHit = onHitEnemy;
        empowered = enhanced;
        StartCoroutine(Fly(origin, end, direction, mask));
    }

    private IEnumerator Fly(Vector2 origin, Vector2 end, Vector2 direction, LayerMask mask)
    {
        var material = WeaponTraitFeedbackSO.SlashMaterial;
        var blade = WeaponTraitEffects.MakeSprite("SwordWaveBlade", transform, material, 36);
        var afterimage = WeaponTraitEffects.MakeSprite("SwordWaveAfterimage", transform, material, 35);
        blade.enabled = afterimage.enabled = material != null;
        var properties = new MaterialPropertyBlock();
        float size = empowered ? 1.55f : 0.95f;
        var previous = origin;
        float start = Time.time;
        float angle = Vector2.SignedAngle(Vector2.right, direction);
        while (SameLife)
        {
            float progress = Mathf.Clamp01((Time.time - start) / FlightDuration);
            Vector2 current = Vector2.Lerp(origin, end, progress);
            Vector2 shadow = Vector2.Lerp(origin, end, Mathf.Max(0f, progress - 0.085f));
            DrawBlade(afterimage, properties, shadow, angle, size * 0.91f, progress, true);
            DrawBlade(blade, properties, current, angle, size, progress, false);
            RaycastMotor2D.InvalidateAttackQuerySync();
            int count = player.Motor.OverlapAttackBox((previous + current) * 0.5f,
                new Vector2(Mathf.Max(0.1f, Vector2.Distance(previous, current) + size * 0.6f), size * 2f),
                angle, overlaps, mask);
            for (int i = 0; i < count && SameLife; i++)
            {
                var collider = overlaps[i];
                overlaps[i] = null;
                var enemy = collider != null ? collider.GetComponentInParent<EnemyHealth>() : null;
                if (enemy != null && !targets.Contains(enemy))
                    StartCoroutine(Strike(enemy, collider.ClosestPoint(current)));
            }
            if (progress >= 1f) break;
            previous = current;
            yield return null;
        }
        Destroy(blade.gameObject);
        Destroy(afterimage.gameObject);
        if (SameLife)
            WeaponTraitFeedbackSO.Play(WeaponTraitCue.SwordWaveLand, end, direction, empowered ? 1.6f : 1f);
        while (activeSequences > 0) yield return null;
        Destroy(gameObject);
    }

    private IEnumerator Strike(EnemyHealth enemy, Vector2 point)
    {
        if (!WeaponTraitEffects.ApplyEnemyHit(enemy, hit, point) || !SameLife) yield break;
        targets.Add(enemy);
        if (empowered && !refunded)
        {
            refunded = true;
            player.AbilityManager.AttackAbility.Traits.ChangeResolve(2);
        }
        onHit?.Invoke(player, point);
        var cue = empowered ? WeaponTraitCue.SwordWaveHeavyHit : WeaponTraitCue.SwordWaveHit;
        WeaponTraitFeedbackSO.Play(cue, point, Vector2.right * hit.knockbackFacing);
        activeSequences++;
        var followUp = hit;
        followUp.knockbackFacing = 0;
        var delay = new WaitForSeconds(HitInterval);
        for (int i = 1; i < (empowered ? 6 : 3); i++)
        {
            yield return delay;
            if (!SameLife || !WeaponTraitEffects.ApplyEnemyHit(enemy, followUp, point) || !SameLife) break;
            WeaponTraitFeedbackSO.Play(cue, point, Vector2.right * hit.knockbackFacing, 0.75f);
        }
        activeSequences--;
    }

    private void DrawBlade(SpriteRenderer renderer, MaterialPropertyBlock properties, Vector2 center,
        float angle, float size, float progress, bool shadow)
    {
        float amount = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.78f, 1f, progress));
        renderer.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, 0f, angle));
        renderer.transform.localScale = new Vector3(size * 3.4f, size * 3.6f, 1f);
        properties.SetFloat("_Head", 76f + progress * 8f);
        properties.SetFloat("_Tail", -78f - progress * 8f);
        properties.SetFloat("_RadiusUnit", 0.61f);
        properties.SetFloat("_Width", empowered ? 0.82f : 0.66f);
        properties.SetFloat("_Alpha", amount * (shadow ? 0.36f : 1f));
        properties.SetFloat("_Flare", shadow ? 0f : 0.7f * amount);
        properties.SetColor("_HeadColor", empowered ? new Color(1.65f, 0.92f, 0.34f) : new Color(0.75f, 0.43f, 1.42f));
        properties.SetColor("_TailColor", empowered ? new Color(1.3f, 0.35f, 0.18f) : new Color(0.1f, 0.75f, 1.1f));
        renderer.SetPropertyBlock(properties);
    }
}
