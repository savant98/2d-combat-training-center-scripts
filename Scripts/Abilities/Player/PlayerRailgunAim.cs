using UnityEngine;

// 레일건 모형(Player의 WeaponModel/RailgunModel)의 총신을 쏘는 방향으로 돌린다(2026-11-18, Railgun 무장 1차 시안).
// 레일건(RailgunWeaponSO)이 준비를 시작할 때와 쏘는 순간 Aim으로 방향을 넘긴다. 위 30도 사격이면 총신도 30도 들고, 총구(RailgunMuzzlePoint)도 함께 돈다.
// - 회전은 모형의 부모(WeaponModel)가 아니라 그 위(Model) 기준으로 정한다. Clip이 WeaponModel에 주는 반동 회전이 그 위에 더해져 보인다.
//   부모의 좌우 반전(FacingModel 배율 -1)은 InverseTransformVector가 함께 맞춘다(PlayerStarShotAim과 같은 방식).
// - Animator는 이 모형의 회전을 기록하지 않는다(레일건 Clip은 WeaponModel·코어만 움직인다). 그래서 공격이 끝나면 직접 원래 회전으로 되돌린다.
//   키를 누르고 있어 다음 사격이 곧바로 이어질 때 한 프레임 수평으로 돌아갔다가 다시 드는 깜빡임이 없도록, 공격이 끝나고 releaseDelay 뒤에 되돌린다.
// - 총열 과열·충전 표시(2026-11-21 무장 특성 3차): 옛 총신 축 위 LineRenderer 한 줄(DrawHeat)을 지우고, 같은 모형에 RailgunBarrelVisual을
//   실행 중에 붙인다(Player.prefab 무수정). 총이 실제로 달아오르는 덧그림·증기·연기·불티·충전 전류는 그 컴포넌트가 그린다.
// - 4차(2026-11-21): 충전 추가타가 총구가 아니라 Player 둘레의 소환 문양(RailEchoShot)에서 나가게 바뀌어, 추가 사격을 위해 공격 사이에도
//   총을 들고 있게 하던 처리(HoldForEcho·IsEchoHold)를 지웠다. 이제 총은 실제 공격의 조준만 따른다.
// Animator 적용 뒤에 돌리도록 실행 순서를 늦춘다(PlayerStarShotAim과 같다).
[DefaultExecutionOrder(120)]
public sealed class PlayerRailgunAim : MonoBehaviour
{
    [SerializeField] private Transform muzzle;                       // RailgunMuzzlePoint(이 모형의 자식)
    [SerializeField, Min(0f)] private float releaseDelay = 0.08f;    // 공격이 끝난 뒤 수평으로 되돌리기까지(초)

    private PlayerWeaponAbility ability;
    private PlayerController owner;
    private RailgunBarrelVisual barrel;
    private Quaternion restRotation = Quaternion.identity;
    private float scaleSign;
    private bool hasRest, aiming, precise;
    private int shotFrame = -1;
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

    /// <summary>이 모형의 총열 과열·충전 표시(없으면 지금 붙인다 — 꺼진 모형이어도 된다).</summary>
    public RailgunBarrelVisual Barrel
    {
        get
        {
            if (barrel == null)
            {
                barrel = GetComponent<RailgunBarrelVisual>();
                if (barrel == null) barrel = gameObject.AddComponent<RailgunBarrelVisual>();
            }
            return barrel;
        }
    }

    private void Awake()
    {
        if (!hasRest)
        {
            restRotation = transform.localRotation;
            scaleSign = Mathf.Sign(transform.localScale.x);
            hasRest = true;
        }
        _ = Barrel;
    }

    // 조준 방향(월드)을 정하고 그 자리에서 총신을 돌린다. 모형이 꺼져 있어도 회전은 적용된다.
    public void Aim(Vector2 worldDirection, bool exactBarrel = false)
    {
        if (worldDirection.sqrMagnitude < 1e-6f) return;
        if (!hasRest)
        {
            restRotation = transform.localRotation;   // Awake 전(꺼진 모형)에 처음 불린 경우
            scaleSign = Mathf.Sign(transform.localScale.x);
            hasRest = true;
        }
        precise = exactBarrel;
        if (precise) shotFrame = Time.frameCount;
        direction = worldDirection.normalized;
        aiming = true;
        idleSince = -1f;
        Apply();
    }

    private void LateUpdate()
    {
        var attack = Ability;
        if (owner != null && (attack == null || !attack.IsActive || attack.IsRailgunPrimaryActive)
            && shotFrame != Time.frameCount && owner.TryGetMouseAim(transform.position, out var mouseDirection))
        {
            if (!aiming) direction = ((Vector2)transform.TransformVector(Vector3.right)).normalized;
            direction = PlayerController.SmoothAim(direction, mouseDirection);
            precise = aiming = true;
            idleSince = -1f;
            Apply();
            return;
        }
        if (!aiming) return;
        if (attack == null || attack.AttackPhase == AbilityState.None)
        {
            if (idleSince < 0f) idleSince = Time.time;
            if (Time.time - idleSince >= releaseDelay) Restore();
        }
        else idleSince = -1f;
        if (aiming) Apply();
    }

    private void Apply()
    {
        Transform parent = transform.parent;
        if (parent == null) return;
        Transform reference = !precise && parent.parent != null ? parent.parent : parent;
        int flip = precise && direction.x * (owner != null ? owner.Facing : Mathf.Sign(reference.lossyScale.x)) < 0f ? -1 : 1;
        var scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * scaleSign * flip;
        transform.localScale = scale;
        Vector3 local = reference.InverseTransformVector(direction * flip);
        if (local.sqrMagnitude < 1e-8f) return;
        transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg);
    }

    public void Restore()
    {
        aiming = false;
        idleSince = -1f;
        precise = false;
        shotFrame = -1;
        if (hasRest)
        {
            transform.localRotation = restRotation;
            var scale = transform.localScale;
            scale.x = Mathf.Abs(scale.x) * scaleSign;
            transform.localScale = scale;
        }
    }

    private void OnDisable() => Restore();

    private PlayerWeaponAbility Ability
    {
        get
        {
            if (ability == null)
            {
                if (owner == null) owner = GetComponentInParent<PlayerController>(true);
                if (owner != null && owner.AbilityManager != null) ability = owner.AbilityManager.AttackAbility;
            }
            return ability;
        }
    }
}
