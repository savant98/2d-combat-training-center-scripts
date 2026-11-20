using UnityEngine;

// 레일건 모형(Player의 WeaponModel/RailgunModel)의 총신을 쏘는 방향으로 돌린다(2026-11-18, Railgun 무장 1차 시안).
// 레일건(RailgunWeaponSO)이 준비를 시작할 때와 쏘는 순간 Aim으로 방향을 넘긴다. 위 30도 사격이면 총신도 30도 들고, 총구(RailgunMuzzlePoint)도 함께 돈다.
// - 회전은 모형의 부모(WeaponModel)가 아니라 그 위(Model) 기준으로 정한다. Clip이 WeaponModel에 주는 반동 회전이 그 위에 더해져 보인다.
//   부모의 좌우 반전(FacingModel 배율 -1)은 InverseTransformVector가 함께 맞춘다(PlayerStarShotAim과 같은 방식).
// - Animator는 이 모형의 회전을 기록하지 않는다(레일건 Clip은 WeaponModel·코어만 움직인다). 그래서 공격이 끝나면 직접 원래 회전으로 되돌린다.
//   키를 누르고 있어 다음 사격이 곧바로 이어질 때 한 프레임 수평으로 돌아갔다가 다시 드는 깜빡임이 없도록, 공격이 끝나고 releaseDelay 뒤에 되돌린다.
// Animator 적용 뒤에 돌리도록 실행 순서를 늦춘다(PlayerStarShotAim과 같다).
[DefaultExecutionOrder(120)]
public sealed class PlayerRailgunAim : MonoBehaviour
{
    [SerializeField] private Transform muzzle;                       // RailgunMuzzlePoint(이 모형의 자식)
    [SerializeField, Min(0f)] private float releaseDelay = 0.08f;    // 공격이 끝난 뒤 수평으로 되돌리기까지(초)

    private PlayerWeaponAbility ability;
    private LineRenderer heatLine;
    private Quaternion restRotation = Quaternion.identity;
    private bool hasRest, aiming;
    private float idleSince = -1f;
    private Vector2 direction = Vector2.right;

    public Transform Muzzle
    {
        get
        {
            if (muzzle == null) muzzle = transform.Find(RailgunWeaponSO.MuzzleName);
            return muzzle;
        }
    }
    public bool IsAiming => aiming;
    public Vector2 Direction => direction;

    private void Awake()
    {
        if (!hasRest)
        {
            restRotation = transform.localRotation;
            hasRest = true;
        }
    }

    // 조준 방향(월드)을 정하고 그 자리에서 총신을 돌린다. 모형이 꺼져 있어도 회전은 적용된다.
    public void Aim(Vector2 worldDirection)
    {
        if (worldDirection.sqrMagnitude < 1e-6f) return;
        if (!hasRest)
        {
            restRotation = transform.localRotation;   // Awake 전(꺼진 모형)에 처음 불린 경우
            hasRest = true;
        }
        direction = worldDirection.normalized;
        aiming = true;
        idleSince = -1f;
        Apply();
    }

    private void LateUpdate()
    {
        var attack = Ability;
        if (aiming)
        {
            if (attack == null || attack.AttackPhase == AbilityState.None)
            {
                if (idleSince < 0f) idleSince = Time.time;
                if (Time.time - idleSince >= releaseDelay) Restore();
            }
            else idleSince = -1f;
            if (aiming) Apply();
        }
        DrawHeat(attack?.Traits);
    }

    private void DrawHeat(PlayerWeaponTraits traits)
    {
        float heat = traits?.Heat ?? 0f;
        if (heat <= 0f || Muzzle == null)
        {
            if (heatLine != null) heatLine.enabled = false;
            return;
        }
        heatLine ??= WeaponTraitEffects.MakeLine("RailgunBarrelHeat", transform);
        heatLine.enabled = true;
        Vector3 axis = transform.TransformVector(Vector3.right).normalized;
        Color color = Color.Lerp(new Color(0.54f, 0.15f, 0.05f), WeaponTraitEffects.HeatColor, heat);
        if (traits.IsRailCharged) color = Color.Lerp(color, WeaponTraitEffects.RailColor, 0.55f);
        WeaponTraitEffects.SetLine(heatLine, Muzzle.position - axis * (0.2f + heat * 0.75f),
            Muzzle.position - axis * 0.08f, 0.07f + 0.07f * heat, color);
    }

    private void Apply()
    {
        Transform parent = transform.parent;
        if (parent == null) return;
        Transform reference = parent.parent != null ? parent.parent : parent;
        Vector3 local = reference.InverseTransformVector(direction);
        if (local.sqrMagnitude < 1e-8f) return;
        transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg);
    }

    public void Restore()
    {
        aiming = false;
        idleSince = -1f;
        if (hasRest) transform.localRotation = restRotation;
    }

    private void OnDisable() => Restore();

    private PlayerWeaponAbility Ability
    {
        get
        {
            if (ability == null)
            {
                var player = GetComponentInParent<PlayerController>(true);
                if (player != null && player.AbilityManager != null) ability = player.AbilityManager.AttackAbility;
            }
            return ability;
        }
    }
}
