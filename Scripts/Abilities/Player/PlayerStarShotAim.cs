using System.Collections.Generic;
using UnityEngine;

// Animator의 반동 뒤 총신을 조준하고 같은 총구에서 탄과 섬광을 함께 내보낸다.
// 머신건(MachineGunWeaponSO, 2026-11-18 2차): 총신은 조준 방향에 둔 채 탄 방향만 흩뜨리고(탄착군), 섬광은 무기의 청록 섬광(muzzleFlash)을 쓴다.
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
    private bool aimed;
    private int nextMachineGunSound;
    private float scatterStreakStart, lastScatterShot = float.NegativeInfinity;   // 머신건 연사를 이어 온 시간(흩어짐이 커지는 정도)
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
        muzzleRestPosition = muzzle.localPosition; muzzleRestRotation = muzzle.localRotation;
        tip = gun.InverseTransformPoint(muzzle.position);
    }
    public bool Queue(RangedWeaponSO weapon, WeaponBaseSO.DamageInfo damage, Vector2 direction)
    {
        if (gun == null || muzzle == null) return false;
        pending.Add(new Request { weapon = weapon, damage = damage, direction = direction });
        return true;
    }
    private void Apply(Vector2 direction)
    {
        // 부모의 좌우 반전과 2.5도 반동까지 포함해 실제 총신 축을 맞춘다.
        Vector3 local = gun.parent.InverseTransformVector(direction);
        gun.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg);
        muzzle.SetPositionAndRotation(gun.TransformPoint(tip), Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg));
        aimed = true;
    }
    private void LateUpdate()
    {
        if (ability == null || !ability.IsStarShotActive || gun == null) { Restore(); return; }
        Apply(ability.PrimaryFireDirection);
        if (ability.AttackPhase != AbilityState.Execute) { pending.Clear(); return; }
        foreach (var shot in pending)
        {
            Apply(shot.direction);
            var machineGun = shot.weapon as MachineGunWeaponSO;
            Vector2 fire = shot.direction;
            Quaternion rotation = muzzle.rotation;
            if (machineGun != null)
            {
                // 총신(위에서 Apply)은 조준 방향 그대로, 탄만 흩뜨린다. 탄은 날아가는 방향으로 눕혀 길쭉한 탄두가 진행 방향을 향하게 한다.
                if (Time.time - lastScatterShot > machineGun.spreadResetGap) scatterStreakStart = Time.time;
                lastScatterShot = Time.time;
                fire = machineGun.Scatter(shot.direction, Time.time - scatterStreakStart);
                rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(fire.y, fire.x) * Mathf.Rad2Deg);
            }
            if (!ability.UsePrimaryRound(shot.weapon)) continue;
            owner.AddRecoil(shot.weapon.recoilPower, owner.Facing);
            var projectile = WorldEntityPool.Instance.Spawn(shot.weapon.projectilePrefab, muzzle.position, rotation);
            if (projectile is not StarShot star) continue;
            star.SetFireDirection(fire);
            star.SetMuzzlePosition(muzzle.position);
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
    public void Restore()
    {
        pending.Clear();
        if (!aimed) return;
        if (gun != null) gun.localRotation = gunRestRotation;
        if (muzzle != null) { muzzle.localPosition = muzzleRestPosition; muzzle.localRotation = muzzleRestRotation; }
        aimed = false;
    }
    private void OnDisable() => Restore();
}
