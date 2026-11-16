using System.Collections.Generic;
using UnityEngine;

// Animator의 반동 뒤 총신을 조준하고 같은 총구에서 탄과 섬광을 함께 내보낸다.
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
    public Transform Muzzle => muzzle;
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
            var projectile = WorldEntityPool.Instance.Spawn(shot.weapon.projectilePrefab, muzzle.position, muzzle.rotation);
            if (projectile is not StarShot star) continue;
            star.SetFireDirection(shot.direction);
            star.SetMuzzlePosition(muzzle.position);
            star.Initialize(owner, shot.damage, shot.weapon.projectileSpeed, shot.weapon.count, shot.weapon.platformLayer);
            var weapon = shot.weapon;
            star.OnHitEnemy += (player, point) => weapon.OnHitEnemy?.Invoke(player, point);
            if (receiver != null) receiver.PlayAnimationFeedback("StarGunMuzzle");
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
