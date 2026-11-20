using UnityEngine;

[DefaultExecutionOrder(130)]
public sealed class PlayerWeaponTraitVisual : MonoBehaviour
{
    private PlayerController player;
    private PlayerWeaponAbility attack;
    private PlayerStarShotAim rifle;
    private SpriteRenderer barrier;
    private MaterialPropertyBlock barrierBlock;
    private LineRenderer barrelTop, barrelBottom;

    public void Bind(PlayerController owner, PlayerWeaponAbility ability, PlayerStarShotAim aim)
    {
        player = owner;
        attack = ability;
        rifle = aim;
    }

    private void LateUpdate()
    {
        if (attack == null) return;
        DrawBarrier();
        DrawIncendiaryMount();
    }

    private void DrawBarrier()
    {
        float remaining = attack.Traits.Barrier;
        if (remaining <= 0f)
        {
            if (barrier != null) barrier.enabled = false;
            return;
        }
        var material = WeaponTraitFeedbackSO.BarrierMaterial;
        if (material == null) return;
        if (barrier == null)
        {
            barrier = WeaponTraitEffects.MakeSprite("HammerBarrier", transform, material, 26);
            barrierBlock = new MaterialPropertyBlock();
        }
        barrier.enabled = true;
        float fill = remaining / PlayerWeaponTraits.BarrierCapacity;
        float flash = Mathf.Clamp01(1f - (Time.time - attack.Traits.BarrierFlashTime) / 0.29f);
        Vector2 direction = attack.Traits.BarrierHitDirection;
        FixedMotionBody.GetPresentationPose(player.transform, out var center, out _);
        barrier.transform.position = center;
        barrier.transform.localScale = new Vector3(4.18f, 4.52f, 1f) * (1f + flash * 0.04f);
        barrierBlock.SetFloat("_Radius", Mathf.Lerp(0.61f, 0.72f, fill));
        barrierBlock.SetFloat("_Alpha", 0.57f + fill * 0.27f);
        barrierBlock.SetFloat("_Flash", flash * flash);
        barrierBlock.SetFloat("_Glass", 0.43f + fill * 0.33f);
        barrierBlock.SetFloat("_Thin", 0.09f);
        barrierBlock.SetFloat("_Spin", Time.time * 0.50f);
        barrierBlock.SetVector("_Hit", new Vector4(direction.x, direction.y, flash, 0f));
        barrier.SetPropertyBlock(barrierBlock);
    }

    private void DrawIncendiaryMount()
    {
        if (attack.PrimaryOverrideSetIndex != attack.CurrentSetIndex
            || attack.PrimaryOverrideWeapon is not IncendiaryRifleWeaponSO || rifle.Muzzle == null
            || !rifle.IsModelVisible)
        {
            if (barrelTop != null) barrelTop.enabled = barrelBottom.enabled = false;
            return;
        }
        if (barrelTop == null)
        {
            barrelTop = WeaponTraitEffects.MakeLine("IncendiaryBarrelTop", transform);
            barrelBottom = WeaponTraitEffects.MakeLine("IncendiaryBarrelBottom", transform);
        }
        barrelTop.enabled = barrelBottom.enabled = true;
        Vector3 end = rifle.BarrelTip;
        Vector3 heading = rifle.BarrelDirection;
        Vector3 offset = Vector2.Perpendicular(heading) * 0.18f;
        float glow = 0.74f + Mathf.Sin(Time.time * 11f) * 0.16f;
        float length = 0.55f + attack.Traits.IncendiaryAmmo * 0.043f;
        var hot = Color.Lerp(WeaponTraitEffects.HeatColor, new Color(1f, 0.75f, 0.30f), glow);
        WeaponTraitEffects.SetLine(barrelTop, end - heading * length + offset, end + offset, 0.16f, hot);
        WeaponTraitEffects.SetLine(barrelBottom, end - heading * length - offset, end - offset, 0.16f, hot);
    }
}
