using System;
using UnityEngine;

public sealed class PlayerWeaponTraits
{
    public const int MagazineCapacity = 15;
    public const int IncendiaryCapacity = 6;
    public const int ResolveCapacity = 3;
    public const float BarrierCapacity = 60f;
    public const float ReloadDuration = 3f;
    public const float OverheatDuration = 10f;
    public const float CoolingDelay = 0.52f;
    public const float CoolingRate = 0.95f;

    private readonly PlayerController player;
    private float reloadEnd;
    private float lockoutEnd;
    private float chargeEnd;
    private float heat;
    private float coolingStart;
    private bool venting;
    public float BarrierFlashTime { get; private set; } = -10f;
    public Vector2 BarrierHitDirection { get; private set; }

    public PlayerWeaponTraits(PlayerController player) => this.player = player;

    public event Action Changed;
    public uint Generation { get; private set; }
    public float BarrierCharge { get; private set; }
    public float Barrier { get; private set; }
    public int RifleAmmo { get; private set; } = MagazineCapacity;
    public int IncendiaryAmmo { get; private set; }
    public int Resolve { get; private set; }
    public float ReloadRemaining => Mathf.Max(0f, reloadEnd - Time.time);
    public float OverheatRemaining => Mathf.Max(0f, lockoutEnd - Time.time);
    public float ChargeRemaining => Mathf.Max(0f, chargeEnd - Time.time);
    public float Heat => OverheatRemaining > 0f ? 1f : heat;
    public bool IsRailCharged => ChargeRemaining > 0f;

    public void AddBarrierCharge(float amount)
    {
        if (Barrier > 0f || amount <= 0f) return;
        BarrierCharge = Mathf.Min(BarrierCapacity, BarrierCharge + amount);
        if (BarrierCharge >= BarrierCapacity)
        {
            Barrier = BarrierCapacity;
            BarrierCharge = 0f;
            BarrierFlashTime = Time.time;
            BarrierHitDirection = Vector2.zero;
            Play(WeaponTraitCue.ShieldRaise);
        }
        else Play(WeaponTraitCue.ShieldCharge);
        Changed?.Invoke();
    }

    public float Absorb(float damage, Vector3? hitPoint = null)
    {
        if (Barrier <= 0f || damage <= 0f) return damage;
        float absorbed = Mathf.Min(Barrier, damage * 0.5f);
        Barrier -= absorbed;
        Vector3 center = player.transform.position;
        Vector2 offset = hitPoint.GetValueOrDefault(center) - center;
        BarrierHitDirection = offset.sqrMagnitude > 0.01f ? offset.normalized : Vector2.right * -player.Facing;
        BarrierFlashTime = Time.time;
        if (Barrier > 0f)
            WeaponTraitFeedbackSO.Play(WeaponTraitCue.ShieldHit, center + (Vector3)BarrierHitDirection * 1.2f,
                BarrierHitDirection, Mathf.Lerp(0.7f, 1.35f, Mathf.Clamp01(absorbed / 24f)));
        else Play(WeaponTraitCue.ShieldBreak);
        Changed?.Invoke();
        return damage - absorbed;
    }

    public void ChangeResolve(int amount)
    {
        int next = Mathf.Clamp(Resolve + amount, 0, ResolveCapacity);
        if (next == Resolve) return;
        Resolve = next;
        Play(amount > 0 ? WeaponTraitCue.ResolveGain : WeaponTraitCue.ResolveSpend);
        Changed?.Invoke();
    }

    public void StartReload()
    {
        if (reloadEnd > 0f || RifleAmmo >= MagazineCapacity) return;
        reloadEnd = Time.time + ReloadDuration;
        Play(WeaponTraitCue.ReloadStart);
        Changed?.Invoke();
    }

    public bool UseRifleRound()
    {
        if (reloadEnd > 0f || RifleAmmo <= 0) return false;
        RifleAmmo--;
        if (RifleAmmo == 0)
        {
            Play(WeaponTraitCue.MagazineEmpty);
            StartReload();
        }
        else Changed?.Invoke();
        return true;
    }

    public void LoadIncendiary()
    {
        IncendiaryAmmo = IncendiaryCapacity;
        Play(WeaponTraitCue.IncendiaryMount);
        Changed?.Invoke();
    }

    public bool UseIncendiaryRound()
    {
        if (IncendiaryAmmo <= 0) return false;
        IncendiaryAmmo--;
        if (IncendiaryAmmo == 0) Play(WeaponTraitCue.MagazineEmpty);
        Changed?.Invoke();
        return true;
    }

    public void AddRailHeat(float amount)
    {
        if (OverheatRemaining > 0f) return;
        heat = Mathf.Min(1f, heat + amount);
        coolingStart = Time.time + CoolingDelay;
        venting = false;
        if (heat >= 0.999f)
        {
            heat = 0f;
            lockoutEnd = Time.time + OverheatDuration;
            chargeEnd = 0f;
            player.AddKnockback(new Vector2(-player.Facing * 15f, 6f));
            PlayRail(WeaponTraitCue.RailOverheat);
        }
        else if (amount < 0.3f)
        {
            PlayRail(WeaponTraitCue.RailHeat);
        }
        Changed?.Invoke();
    }

    public void ChargeRail()
    {
        AddRailHeat(0.30f);
        if (OverheatRemaining > 0f) return;
        chargeEnd = Time.time + 5f;
        PlayRail(WeaponTraitCue.RailCharge);
        Changed?.Invoke();
    }

    public void Tick()
    {
        if (reloadEnd > 0f && Time.time >= reloadEnd)
        {
            reloadEnd = 0f;
            RifleAmmo = MagazineCapacity;
            Play(WeaponTraitCue.ReloadEnd);
            Changed?.Invoke();
        }
        if (lockoutEnd > 0f)
        {
            if (Time.time < lockoutEnd) return;
            lockoutEnd = 0f;
            PlayRail(WeaponTraitCue.RailReady);
            Changed?.Invoke();
        }
        if (heat <= 0f || Time.time < coolingStart) return;
        if (!venting && heat >= 0.16f) PlayRail(WeaponTraitCue.RailVent);
        venting = true;
        heat = Mathf.MoveTowards(heat, 0f, CoolingRate * Time.deltaTime);
        Changed?.Invoke();
    }

    private void Play(WeaponTraitCue cue) =>
        WeaponTraitFeedbackSO.Play(cue, player.transform.position, Vector2.right * player.Facing);

    private void PlayRail(WeaponTraitCue cue)
    {
        var model = player.model.transform.Find(RailgunWeaponSO.ModelPath);
        var aim = model != null ? model.GetComponent<PlayerRailgunAim>() : null;
        var muzzle = aim != null ? aim.Muzzle : null;
        WeaponTraitFeedbackSO.Play(cue, muzzle != null && muzzle.gameObject.activeInHierarchy
            ? muzzle.position : player.transform.position, Vector2.right * player.Facing);
    }

    public void Reset()
    {
        Generation++;
        reloadEnd = lockoutEnd = chargeEnd = heat = 0f;
        Barrier = BarrierCharge = 0f;
        BarrierFlashTime = -10f;
        BarrierHitDirection = Vector2.zero;
        venting = false;
        coolingStart = 0f;
        Resolve = IncendiaryAmmo = 0;
        RifleAmmo = MagazineCapacity;
        Changed?.Invoke();
    }
}
