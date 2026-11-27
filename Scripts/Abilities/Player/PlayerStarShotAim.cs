using System.Collections.Generic;
using UnityEngine;

// Animator의 반동 뒤 총신을 조준하고 같은 총구에서 탄과 섬광을 함께 내보낸다.
// 머신건(MachineGunWeaponSO, 2026-11-18 2차): 총신은 조준 방향에 둔 채 탄 방향만 흩뜨리고(탄착군), 섬광은 무기의 청록 섬광(muzzleFlash)을 쓴다.
// 작열탄(IncendiaryRifleWeaponSO, 2026-11-21): 레일건과 같은 총구 들림 반동이 보이도록 총신 기준을 조부모(Model)로 둔다(raisedFrame).
[DefaultExecutionOrder(120)]
public sealed class PlayerStarShotAim : MonoBehaviour
{
    private struct Request
    {
        public RangedWeaponSO weapon;
        public WeaponBaseSO.DamageInfo damage;
        public Vector2 direction;
    }
    private readonly List<Request> pending = new();
    private PlayerController owner;
    private PlayerWeaponAbility ability;
    private Transform gun, muzzle;
    private AnimationFeedbackReceiver receiver;
    private Vector3 tip, muzzleRestPosition;
    private Quaternion gunRestRotation, muzzleRestRotation;
    private float gunScaleSign;
    private Vector2 displayedDirection;
    private const float AutoAimReturnDuration = .22f;
    private bool aimed, autoAimApplied;
    private float autoAimReturnTime;
    private Vector2 lastAutoAim;
    private Vector3 lastAutoMuzzlePosition;
    private Quaternion lastAutoMuzzleRotation;
    // 작열탄(IncendiaryRifleWeaponSO) 사격 중이면 총신 회전을 조부모(Model) 기준으로 정해 Clip의 총구 들림 반동(WeaponModel 회전)이 보이게 한다
    // (PlayerRailgunAim과 같은 방식, 2026-11-21 "레일건과 똑같은 반동"). 라이플·머신건은 지금처럼 부모(WeaponModel) 기준으로 반동 회전을 상쇄한다.
    // 실제로 쏜 탄의 무기로 정하고 공격이 끝날 때(Restore)까지 유지한다 — 장착이 끝나는 마지막 발의 회수 동안에도 기준이 바뀌지 않게.
    private bool raisedFrame;
    private int nextMachineGunSound;
    private float scatterStreakStart, lastScatterShot = float.NegativeInfinity;   // 머신건 연사를 이어 온 시간(흩어짐이 커지는 정도)
    // 핸드캐논 자동 조준(2026-11-26, 사용자 요청 "자동조준 시 핸드캐논도 자동조준"): 자동 조준으로 쏜 방향을 실행·회수 동안 유지한다.
    // 키보드 조작에서는 마우스 분기가 없어 저장하지 않으면 같은 프레임에 Restore가 총신을 되돌린다. 핸드캐논이 끝나면(또는 다음 준비가 시작되면) 지운다.
    private Vector2 cannonAim;
    private bool hasCannonAim;
    public Transform Muzzle => muzzle;
    public bool IsModelVisible => gun != null && gun.gameObject.activeInHierarchy;
    public Vector3 BarrelTip => gun != null ? gun.TransformPoint(tip) : transform.position;
    public Vector2 BarrelDirection => gun != null ? ((Vector2)gun.TransformVector(Vector3.right)).normalized : Vector2.right;

    public void Initialize(PlayerController player, PlayerWeaponAbility source)
    {
        owner = player; ability = source;
        gun = owner.model.transform.Find("WeaponModel/StarModel");
        muzzle = owner.model.transform.parent.Find("StarMuzzlePoint");
        receiver = owner.model.GetComponent<AnimationFeedbackReceiver>();
        if (gun == null || muzzle == null) return;
        gunRestRotation = gun.localRotation;
        gunScaleSign = Mathf.Sign(gun.localScale.x);
        muzzleRestPosition = muzzle.localPosition; muzzleRestRotation = muzzle.localRotation;
        tip = gun.InverseTransformPoint(muzzle.position);
    }
    public bool Queue(RangedWeaponSO weapon, WeaponBaseSO.DamageInfo damage, Vector2 direction)
    {
        if (gun == null || muzzle == null) return false;
        pending.Add(new Request { weapon = weapon, damage = damage, direction = direction });
        return true;
    }
    private void Apply(Vector2 direction, bool precise = false)
    {
        // 부모의 좌우 반전과 2.5도 반동까지 포함해 실제 총신 축을 맞춘다.
        // 작열탄은 조부모 기준이라 부모(WeaponModel)의 반동 회전이 그 위에 더해져 보인다(좌우 반전은 InverseTransformVector가 함께 맞춘다).
        Transform reference = !precise && raisedFrame && gun.parent.parent != null ? gun.parent.parent : gun.parent;
        int flip = precise && direction.x * owner.Facing < 0f ? -1 : 1;
        var scale = gun.localScale;
        scale.x = Mathf.Abs(scale.x) * gunScaleSign * flip;
        gun.localScale = scale;
        Vector3 local = reference.InverseTransformVector(direction * flip);
        gun.localRotation = Quaternion.Euler(0f, 0f, PlayerController.AimAngle(local));
        muzzle.SetPositionAndRotation(gun.TransformPoint(tip), Quaternion.Euler(0f, 0f, PlayerController.AimAngle(direction)));
        displayedDirection = direction;
        aimed = true;
    }
    public void AlignHandCannon(Vector2 direction)
    {
        if (IsModelVisible && muzzle != null) Apply(direction, true);
    }
    // hold = 자동 조준으로 정한 방향: 핸드캐논이 끝날 때까지 총신을 이 방향에 둔다. 마우스 조준(hold false)은 예전처럼 LateUpdate가 마우스를 따라간다.
    public void AlignHandCannon(Vector2 direction, bool hold)
    {
        if (hold)
        {
            cannonAim = direction;
            hasCannonAim = true;
        }
        AlignHandCannon(direction);
    }

    private void LateUpdate()
    {
        if (ability == null || !IsModelVisible || muzzle == null) { Restore(); return; }
        var activeWeapon = ability.IsStarShotActive ? ability.EquippedStarShot : null;
        Vector2 fireAim = default;
        Collider2D target = null;
        // 자동 조준 시야(2026-11-26): 탄을 막는 지형(platformLayer)과 탄의 지형 판정 반지름을 넘겨, 가려진 조준점·표적을 건너뛴다(PlayerAutoAim).
        bool hasFireAim = activeWeapon != null && owner.TryGetRangedAim(gun.position,
            activeWeapon.projectileSpeed * ((StarShot)activeWeapon.projectilePrefab).LifeTime,
            1 << LayerMask.NameToLayer("Enemy"), activeWeapon.platformLayer, PlayerAutoAim.ProbeRadius(activeWeapon.projectilePrefab),
            out fireAim, out target);

        var cannon = ability.ActiveHandCannon;
        if (cannon == null) hasCannonAim = false;   // 핸드캐논이 끝나면 저장한 방향을 지운다
        Vector2 aimOrigin = cannon != null ? cannon.AimOrigin(owner) : gun.position;
        bool tracksMouse = cannon != null || ((!ability.IsActive || ability.IsStarShotActive) && ability.EquippedStarShot != null);
        Vector2 cannonDirection = default;
        bool cannonAssisted = cannon != null && TryGetCannonAim(cannon, aimOrigin, out cannonDirection);
        if (target != null)
        {
            Apply(fireAim, true);
            autoAimApplied = true;
            autoAimReturnTime = 0f;
            lastAutoAim = fireAim;
            lastAutoMuzzlePosition = muzzle.localPosition;
            lastAutoMuzzleRotation = muzzle.localRotation;
        }
        else if (cannonAssisted)
        {
            // 핸드캐논 자동 조준: 라이플 자동 조준과 같은 상태를 남겨, 핸드캐논이 끝나면 아래 ReturnFromAutoAim이 기본 자세로 보간한다.
            Apply(cannonDirection, true);
            autoAimApplied = true;
            autoAimReturnTime = 0f;
            lastAutoAim = cannonDirection;
            lastAutoMuzzlePosition = muzzle.localPosition;
            lastAutoMuzzleRotation = muzzle.localRotation;
        }
        else if (tracksMouse && owner.TryGetMouseAim(aimOrigin, out var mouseDirection))
        {
            autoAimApplied = false;
            Apply(PlayerController.SmoothAim(aimed ? displayedDirection : BarrelDirection, mouseDirection), true);
        }
        else if (autoAimApplied && (activeWeapon != null || !ability.IsActive))
            ReturnFromAutoAim(activeWeapon != null);
        else if (activeWeapon != null)
            Apply(ability.PrimaryFireDirection);
        else
        {
            Restore();
            return;
        }
        if (!ability.IsStarShotActive || ability.AttackPhase != AbilityState.Execute) { pending.Clear(); return; }
        foreach (var shot in pending)
        {
            raisedFrame = shot.weapon is IncendiaryRifleWeaponSO;
            Vector2 shotDirection = hasFireAim ? fireAim : shot.direction;
            if (!ability.UsePrimaryRound(shot.weapon)) continue;
            autoAimApplied = target != null;
            Apply(shotDirection, hasFireAim);
            var machineGun = shot.weapon as MachineGunWeaponSO;
            Vector2 fire = shotDirection;
            Quaternion rotation = muzzle.rotation;
            if (machineGun != null)
            {
                // 총신(위에서 Apply)은 조준 방향 그대로, 탄만 흩뜨린다. 탄은 날아가는 방향으로 눕혀 길쭉한 탄두가 진행 방향을 향하게 한다.
                if (Time.time - lastScatterShot > machineGun.spreadResetGap) scatterStreakStart = Time.time;
                lastScatterShot = Time.time;
                fire = machineGun.Scatter(shotDirection, Time.time - scatterStreakStart);
                rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(fire.y, fire.x) * Mathf.Rad2Deg);
            }
            owner.AddRecoil(hasFireAim ? shot.weapon.recoilPower * Mathf.Abs(shotDirection.x) : shot.weapon.recoilPower,
                hasFireAim && shotDirection.x != 0f ? (shotDirection.x > 0f ? 1 : -1) : owner.Facing);
            Vector3 spawn = muzzle.position;
            // 표적이 총구보다 가까우면 날아가는 판정은 총신 안쪽에서 시작하고 섬광은 총구에 둔다.
            if (target != null && Vector2.Dot((Vector2)(target.bounds.center - spawn), shotDirection) < 0f)
                spawn = new Vector3(gun.position.x, gun.position.y, spawn.z);
            var projectile = WorldEntityPool.Instance.Spawn(shot.weapon.projectilePrefab, spawn, rotation);
            if (projectile is not StarShot star) continue;
            star.SetFireDirection(fire);
            star.SetMuzzlePosition(muzzle.position, spawn);
            star.Initialize(owner, shot.damage, shot.weapon.projectileSpeed, shot.weapon.count, shot.weapon.platformLayer);
            var weapon = shot.weapon;
            star.OnHitEnemy += (player, point) => weapon.OnHitEnemy?.Invoke(player, point);
            if (machineGun != null && machineGun.muzzleFlash != null && FeedbackPlayer.Instance != null)
                FeedbackPlayer.Instance.Play(machineGun.muzzleFlash, muzzle);   // StarGunMuzzle 키와 같은 재생(총구 자리·회전 + 보정)
            else if (weapon is IncendiaryRifleWeaponSO)
                WeaponTraitFeedbackSO.Play(WeaponTraitCue.IncendiaryFire, muzzle.position, fire);
            else
            {
                if (receiver != null) receiver.PlayAnimationFeedback("StarGunMuzzle");
                if (weapon.weaponName == "StarShot")
                    WeaponTraitFeedbackSO.Play(WeaponTraitCue.RifleRound, muzzle.position - (Vector3)fire * 0.48f,
                        Vector2.Perpendicular(fire) * owner.Facing);
            }

            var shotSounds = machineGun?.shotSounds;
            if (shotSounds != null && shotSounds.Length > 0)
            {
                int index = nextMachineGunSound % shotSounds.Length;
                SoundManager.Instance?.PlaySound(shotSounds[index], muzzle.position);
                nextMachineGunSound = (index + 1) % shotSounds.Length;
            }
        }
        pending.Clear();
    }
    // 핸드캐논 자동 조준 방향. 쏘기 전 준비 단계에는 자동 조준 표적(시야 검사 없음 — 부채꼴 피해가 지형을 무시한다) 쪽,
    // 쏜 뒤(실행·회수)에는 자동 조준으로 쏜 방향(AlignHandCannon hold). 자동 조준이 꺼져 있거나 표적이 없거나 마우스로 쏜 경우는 false(아래 분기가 예전처럼 맡는다).
    private bool TryGetCannonAim(HandCannonWeaponSO cannon, Vector2 origin, out Vector2 direction)
    {
        direction = default;
        if (ability.AttackPhase == AbilityState.Windup)
        {
            hasCannonAim = false;   // 준비 중이면 남은 값은 앞선 핸드캐논의 방향이다
            return owner.TryGetRangedAim(origin, cannon.AutoAimRange, cannon.damageMask, 0, 0f, out direction, out var aimTarget)
                && aimTarget != null;
        }
        if (!hasCannonAim) return false;
        direction = cannonAim;
        return true;
    }
    private void ReturnFromAutoAim(bool attacking)
    {
        autoAimReturnTime += Time.deltaTime;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(autoAimReturnTime / AutoAimReturnDuration));
        if (t >= 1f)
        {
            autoAimApplied = false;
            if (attacking) Apply(ability.PrimaryFireDirection);
            else Restore();
            return;
        }

        Vector2 restDirection = attacking ? ability.PrimaryFireDirection
            : gun.parent.TransformVector(gunRestRotation * (Vector3.right * gunScaleSign));
        float angle = Mathf.LerpAngle(PlayerController.AimAngle(lastAutoAim), PlayerController.AimAngle(restDirection), t);
        Apply(Quaternion.Euler(0f, 0f, angle) * Vector3.right, true);
        if (!attacking)
        {
            muzzle.localPosition = Vector3.Lerp(lastAutoMuzzlePosition, muzzleRestPosition, t);
            muzzle.localRotation = Quaternion.Slerp(lastAutoMuzzleRotation, muzzleRestRotation, t);
        }
    }

    public void EndBurst()
    {
        pending.Clear();
        if (!autoAimApplied) Restore();
    }

    public void Restore()
    {
        pending.Clear();
        autoAimApplied = false;
        raisedFrame = false;
        hasCannonAim = false;
        if (!aimed) return;
        if (gun != null)
        {
            gun.localRotation = gunRestRotation;
            var scale = gun.localScale;
            scale.x = Mathf.Abs(scale.x) * gunScaleSign;
            gun.localScale = scale;
        }
        if (muzzle != null) { muzzle.localPosition = muzzleRestPosition; muzzle.localRotation = muzzleRestRotation; }
        aimed = false;
    }
    private void OnDisable() => Restore();
}
