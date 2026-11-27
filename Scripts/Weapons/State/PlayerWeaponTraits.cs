using System;
using UnityEngine;

public sealed class PlayerWeaponTraits
{
    // 2026-11-21 사용자 지시: 라이플 탄창 30발(3점사 → 단발 연사)
    public const int MagazineCapacity = 30;
    // 2026-11-21 사용자 지시 3차: 작열탄 10발
    public const int IncendiaryCapacity = 10;
    public const int MachineGunHitsPerBurn = 3;
    public const int ResolveCapacity = 3;
    public const float BarrierCapacity = 30f;
    public const float BarrierRebuildDelay = 7f;
    public const float ReloadDuration = 3f;
    public const float OverheatDuration = 10f;
    // 2026-11-21 사용자 지시 3차: 레일건 연속 10발에 과열 상한, 0.5초 뒤 빠르게 식어 99% → 0까지 3초
    public const int RailShotsToOverheat = 10;
    public const float RailHeatPerShot = 1f / RailShotsToOverheat;
    public const float CoolingDelay = 0.5f;
    public const float CoolingRate = 1f / 3f;
    /// <summary>레일건 충전(Gear 1) 지속 시간(초).</summary>
    public const float ChargeDuration = 10f;   // 5차 사용자 지시: 5 → 10초
    /// <summary>레일건 충전(Gear 1) 발동 시 증가하는 과열 게이지 비율(30%).</summary>
    public const float ChargeHeatGain = 0.3f;
    /// <summary>방어막이 가득 차는 평타 환산 횟수(2026-11-21 5차 사용자 지시: 평타 10번, 회전 망치 3틱 = 평타 1번, 지면 평탄화 적중 = 평타 5번, 반격 성공 = 즉시).</summary>
    public const int BarrierHitsToFull = 10;
    // 2026-11-21 사용자 지시 3차: 탄창을 다 쓰거나, 마지막 라이플 탄 뒤 이 시간 동안 쏘지 않으면 장전한다(무기 교체는 장전 조건이 아니다).
    public const float IdleReloadDelay = 3.5f;
    /// <summary>방어막 외피 반지름(칸). 피격 연출을 외피 위에서 낸다 — HammerBarrierVisual의 막 반지름과 같은 값으로 둔다.</summary>
    public const float BarrierHitRadius = HammerBarrierVisual.DefaultRadius;

    /// <summary>해머 방어막의 상태 변화(외피 연출·HUD가 구독한다).</summary>
    public enum BarrierEventType { Raised, Hit, Broken, Refilled }

    private readonly PlayerController player;
    private float reloadEnd;
    private float lockoutEnd;
    private float chargeEnd;
    private float barrierRebuildAt;
    private float heat;
    private float coolingStart;
    private bool venting;
    private int machineGunHits;
    public float BarrierFlashTime { get; private set; } = -10f;
    public Vector2 BarrierHitDirection { get; private set; }

    public PlayerWeaponTraits(PlayerController player) => this.player = player;

    public event Action Changed;
    /// <summary>방어막이 생김·피격·파괴·재충전될 때 한 번씩(같은 프레임의 Changed보다 먼저) 발생한다.</summary>
    public event Action<BarrierEventType> BarrierEvent;
    public uint Generation { get; private set; }
    public float BarrierCharge { get; private set; }
    public float Barrier { get; private set; }
    private bool CanBuildBarrier => Time.time >= barrierRebuildAt;
    public BarrierEventType LastBarrierEvent { get; private set; }
    public float BarrierEventTime { get; private set; } = -10f;
    public int RifleAmmo { get; private set; } = MagazineCapacity;
    public int IncendiaryAmmo { get; private set; }
    public int Resolve { get; private set; }
    public float ReloadRemaining => Mathf.Max(0f, reloadEnd - Time.time);
    public bool IsReloading => reloadEnd > 0f;
    /// <summary>장전 진행률(0 = 막 시작, 1 = 끝 또는 장전 중 아님).</summary>
    public float ReloadProgress => reloadEnd > 0f ? 1f - Mathf.Clamp01(ReloadRemaining / ReloadDuration) : 1f;
    /// <summary>마지막 탄을 쏴서 탄창이 빈 시각(탄창 표시의 '탄클립 빠짐' 연출용).</summary>
    public float MagazineEmptyTime { get; private set; } = -10f;
    /// <summary>마지막 라이플 탄을 쏜 시각(무발사 장전 기준).</summary>
    public float LastRifleShotTime { get; private set; } = -10f;
    public float OverheatRemaining => Mathf.Max(0f, lockoutEnd - Time.time);
    public float ChargeRemaining => Mathf.Max(0f, chargeEnd - Time.time);
    public float Heat => OverheatRemaining > 0f ? 1f : heat;
    public bool IsRailCharged => ChargeRemaining > 0f;
    /// <summary>
    /// 라이플 자리를 들고 있는지(PlayerWeaponAbility가 정한다). 장전이 끝나는 재장전 소리는 라이플을 든 동안만 낸다.
    /// 비어 있으면 늘 든 것으로 본다.
    /// </summary>
    public Func<bool> IsRifleHeld { get; set; }

    public void AddBarrierCharge(float amount)
    {
        if (!CanBuildBarrier || Barrier > 0f || amount <= 0f) return;
        BarrierCharge = Mathf.Min(BarrierCapacity, BarrierCharge + amount);
        if (BarrierCharge >= BarrierCapacity)
        {
            Barrier = BarrierCapacity;
            BarrierCharge = 0f;
            BarrierFlashTime = Time.time;
            BarrierHitDirection = Vector2.zero;
            Play(WeaponTraitCue.ShieldRaise);
            RaiseBarrierEvent(BarrierEventType.Raised);
        }
        else Play(WeaponTraitCue.ShieldCharge);   // 소리 없음(2026-11-21 사용자 지시), 작은 시각 효과만
        Changed?.Invoke();
    }

    private object lastBarrierSource;
    private int lastBarrierFrame = -1;
    private float lastBarrierTime = -10f;

    /// <summary>
    /// 해머 무기 적중을 평타 환산 횟수(hits)만큼 방어막 충전에 더한다(평타 1번 = 용량의 1/BarrierHitsToFull).
    /// 같은 출처(source)가 같은 프레임에 여러 적을 맞혀도 한 번으로 센다. window(초)를 주면 그 시간 안의 같은 출처 적중도 한 번으로 센다(지면 평탄화는 한 번 쓸 때 한 번).
    /// </summary>
    public void AddBarrierHits(float hits, object source, float window = 0f)
    {
        if (!CanBuildBarrier || hits <= 0f) return;
        if (source != null && ReferenceEquals(source, lastBarrierSource)
            && (Time.frameCount == lastBarrierFrame || Time.time - lastBarrierTime < window)) return;
        lastBarrierSource = source;
        lastBarrierFrame = Time.frameCount;
        lastBarrierTime = Time.time;
        AddBarrierCharge(hits * BarrierCapacity / BarrierHitsToFull);
    }

    /// <summary>
    /// 반격 성공 시 방어막을 가득 채운다. 파괴 후 재생성 대기 중에는 충전할 수 없다.
    /// 없던 방어막이 생기면 전개 연출·전개음을 내되 반격 성공 연출과 겹치는 화면 효과는 뺀다. 남아 있던 방어막이면 재충전 연출만 낸다.
    /// </summary>
    public void FillBarrier()
    {
        if (!CanBuildBarrier) return;
        bool raised = Barrier <= 0f;
        Barrier = BarrierCapacity;
        BarrierCharge = 0f;
        BarrierFlashTime = Time.time;
        BarrierHitDirection = Vector2.zero;
        if (raised)
        {
            Play(WeaponTraitCue.ShieldRaise, false);
            RaiseBarrierEvent(BarrierEventType.Raised);
        }
        else
        {
            Play(WeaponTraitCue.ShieldRefill, false);
            RaiseBarrierEvent(BarrierEventType.Refilled);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// 받는 피해를 방어막 양만큼 먼저 막는다(2026-11-21 4차 사용자 지시 "쉴드 50일 때 40을 받으면 체력은 그대로, 쉴드만 40 깎임").
    /// 방어막보다 큰 피해는 방어막을 모두 쓰고 남은 만큼만 돌려준다(체력 피해). 돌려주는 값 = 체력에 들어갈 피해.
    /// </summary>
    public float Absorb(float damage, Vector3? hitPoint = null)
    {
        if (Barrier <= 0f || damage <= 0f) return damage;
        float absorbed = Mathf.Min(Barrier, damage);
        Barrier -= absorbed;
        Vector3 center = player.transform.position;
        Vector2 offset = hitPoint.GetValueOrDefault(center) - center;
        BarrierHitDirection = offset.sqrMagnitude > 0.01f ? offset.normalized : Vector2.right * -player.Facing;
        BarrierFlashTime = Time.time;
        if (Barrier > 0f)
        {
            WeaponTraitFeedbackSO.Play(WeaponTraitCue.ShieldHit, center + (Vector3)BarrierHitDirection * BarrierHitRadius,
                BarrierHitDirection, Mathf.Lerp(0.7f, 1.35f, Mathf.Clamp01(absorbed / 24f)));
            RaiseBarrierEvent(BarrierEventType.Hit);
        }
        else
        {
            Barrier = 0f;
            barrierRebuildAt = Time.time + BarrierRebuildDelay;
            Play(WeaponTraitCue.ShieldBreak);
            RaiseBarrierEvent(BarrierEventType.Broken);
        }
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

    /// <summary>
    /// 덜 찬 탄창의 장전을 시작한다(마지막 탄 뒤 IdleReloadDelay 동안 쏘지 않았을 때 Tick이 부른다). 소리·효과는 내지 않는다.
    /// 장전 중임은 Player 주변·HUD의 탄창 아이콘이 빨갛게 차오르며 보여 준다(다른 무장을 든 동안에도).
    /// 탄창을 다 써서 시작하는 장전은 UseRifleRound가 '탄클립 빠짐'(MagazineEmpty) 소리를 낸 뒤 바로 잡는다.
    /// </summary>
    public void StartReload()
    {
        if (reloadEnd > 0f || RifleAmmo >= MagazineCapacity) return;
        reloadEnd = Time.time + ReloadDuration;
        Changed?.Invoke();
    }

    public bool UseRifleRound()
    {
        if (reloadEnd > 0f || RifleAmmo <= 0) return false;
        RifleAmmo--;
        LastRifleShotTime = Time.time;
        if (RifleAmmo == 0)
        {
            // 탄창을 다 쓰면 탄클립이 빠지는 소리 → 3초 뒤 탄이 채워질 때 재장전 소리(2026-11-21 사용자 지시)
            MagazineEmptyTime = Time.time;
            Play(WeaponTraitCue.MagazineEmpty);
            reloadEnd = Time.time + ReloadDuration;
        }
        Changed?.Invoke();
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

    public void AddRailHeat(float amount) => AddRailHeat(amount, true);

    private void AddRailHeat(float amount, bool shotTick)
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
        else if (shotTick)
        {
            PlayRail(WeaponTraitCue.RailHeat);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// 레일건 충전(Gear 1): 과열 게이지를 30%(ChargeHeatGain) 올리고 ChargeDuration 동안 레일건 계열 공격을 강화한다.
    /// 증가 결과 과열 임계(100%)에 도달하면 기존 과열 잠금 규칙을 그대로 따른다.
    /// 과열 잠금 중에는 이 기술을 쓸 수 없다(PlayerWeaponAbility.IsWeaponTraitReady).
    /// </summary>
    public void ChargeRail()
    {
        if (OverheatRemaining > 0f) return;
        chargeEnd = Time.time + ChargeDuration;
        AddRailHeat(ChargeHeatGain, false);
        if (OverheatRemaining <= 0f) PlayRail(WeaponTraitCue.RailCharge);
    }

    public void Tick()
    {
        // 덜 찬 탄창은 마지막 탄 뒤 IdleReloadDelay 동안 쏘지 않으면 장전한다(다른 무장을 든 동안에도 진행).
        if (reloadEnd <= 0f && RifleAmmo < MagazineCapacity && LastRifleShotTime > 0f && Time.time - LastRifleShotTime >= IdleReloadDelay)
            StartReload();
        if (reloadEnd > 0f && Time.time >= reloadEnd)
        {
            reloadEnd = 0f;
            RifleAmmo = MagazineCapacity;
            // 다른 무장을 든 동안 장전이 끝나면 소리 없이 채운다.
            if (IsRifleHeld == null || IsRifleHeld()) Play(WeaponTraitCue.ReloadEnd);
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

    private void RaiseBarrierEvent(BarrierEventType type)
    {
        LastBarrierEvent = type;
        BarrierEventTime = Time.time;
        BarrierEvent?.Invoke(type);
    }

    private void Play(WeaponTraitCue cue, bool cameraEffects = true) =>
        WeaponTraitFeedbackSO.Play(cue, player.transform.position, Vector2.right * player.Facing, 1f, cameraEffects);

    private void PlayRail(WeaponTraitCue cue)
    {
        var model = player.model.transform.Find(RailgunWeaponSO.ModelPath);
        var aim = model != null ? model.GetComponent<PlayerRailgunAim>() : null;
        var muzzle = aim != null ? aim.Muzzle : null;
        WeaponTraitFeedbackSO.Play(cue, muzzle != null && muzzle.gameObject.activeInHierarchy
            ? muzzle.position : player.transform.position, Vector2.right * player.Facing);
    }

    // 탄이 어느 적에 맞았는지에 관계없이 현재 플레이어의 유효 적중을 세어 매 세 번째 적에게 화상을 붙인다.
    public bool RecordMachineGunHit()
    {
        machineGunHits = (machineGunHits + 1) % MachineGunHitsPerBurn;
        return machineGunHits == 0;
    }

    public void Reset(bool preserveAttackGeneration = false)
    {
        // 보스 처치 때는 상태를 채우면서 이미 발사한 검기와 미니 레이저의 연출을 이어 간다.
        if (!preserveAttackGeneration) Generation++;
        machineGunHits = 0;
        reloadEnd = lockoutEnd = chargeEnd = heat = 0f;
        Barrier = BarrierCharge = 0f;
        barrierRebuildAt = 0f;
        lastBarrierSource = null;
        lastBarrierFrame = -1;
        lastBarrierTime = -10f;
        BarrierFlashTime = -10f;
        BarrierHitDirection = Vector2.zero;
        BarrierEventTime = -10f;
        MagazineEmptyTime = -10f;
        LastRifleShotTime = -10f;
        venting = false;
        coolingStart = 0f;
        Resolve = IncendiaryAmmo = 0;
        RifleAmmo = MagazineCapacity;
        Changed?.Invoke();
    }
}
