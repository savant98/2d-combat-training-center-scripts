using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 레일건(Railgun 무장의 기본 무기, 2026-11-18 1차 시안).
// 사용자 지시: Player 무장을 4종으로 정리 — Railgun = 기본 '레일건'(새로 만듦) / Gear 1단계 없음 / 2단계 별창 발사 / 3단계 파괴 광선.
//   "현재 개발되지 않은 무장들은 개발된 무장을 참고해서 만들어봐." 무장 설명: "충전한 관통탄으로 일직선을 꿰뚫는다."
// - 손맛: Rifle(StarShot, 별탄 3점사)과 다르게 한 발씩 쏜다. 짧은 충전(준비 단계, 모형의 코어가 밝아진다) → 실행 첫 프레임에
//   화면을 가르는 얇은 빛줄기가 즉시 관통(히트스캔) → 잔광. 키를 누르고 있으면 PlayerWeaponAbility가 기본 공격처럼 이어 쏜다(쿨다운 = cooldown).
// - 방향: PlayerWeaponAbility.PrimaryFireDirection(기본 무기일 때 위를 누르고 쏘면 위 30도). 보는 쪽과 어긋난 값(기본 무기 자리가 아닐 때 남은
//   지난 값)이면 보는 쪽 수평으로 쏜다.
// - 출발점: 레일건 모형의 총구(WeaponModel/RailgunModel/RailgunMuzzlePoint). 쏘는 순간 모형의 PlayerRailgunAim이 총신을 조준 방향으로 맞춘 뒤 읽는다.
//   모형이 없으면 Player 중심에서 조준 방향으로 fallbackMuzzleOffset.x, 위로 fallbackMuzzleOffset.y만큼 떨어진 곳이다.
// - 판정(규칙: RaycastMotor2D 기반, 위치를 직접 바꾸지 않는다):
//   벽은 모터의 RaycastEnvironment로 찾는다(통과 발판은 지나간다). 총구가 얇은 벽을 넘어가 있어도 막히도록 몸 중심 높이의 총구 축(총구를 조준선 위로
//   몸 쪽에 내린 점)에서 한 줄을 쏜다(PlayerLaserBeam.UpdateBlock과 같은 방식).
//   적은 그 점에서 벽(또는 최대 사거리)까지 덮는 회전 상자를 모터의 OverlapAttackBox로 찾는다. 겹친 모든 적에게 한 번씩 피해를 준다(관통).
//   피해·쉴드·소환 잠금·적중 Feedback·적중 보상(OnHitEnemy) 처리는 PlayerLaserBeam.ApplyDamage와 ProjectileBase.ApplyHitDamage를 따른다.
//   넉백 출처(knockbackOrigin·knockbackFacing)는 WorldEntityBase.Initialize가 Player 탄에 넣는 값과 같게 쏜 순간의 Player 위치·방향이다.
// - 보이는 빛줄기·잔광·전류 입자는 풀 개체 RailgunTracer가 그린다(판정과 분리). 발사 섬광·흔들림·소리는 총구에서 조준 방향으로 돌려 재생한다.
[CreateAssetMenu(menuName = "Weapon/Railgun")]
public class RailgunWeaponSO : WeaponBaseSO
{
    public const string ModelPath = "WeaponModel/RailgunModel";   // Player 모형(Model) 아래 레일건 모형 경로
    public const string MuzzleName = "RailgunMuzzlePoint";

    [Header("Railgun")]
    [Tooltip("빛줄기를 막는 지형 레이어. 통과 발판은 지나간다.")]
    public LayerMask platformLayer;
    [Tooltip("피해를 줄 적 판정 레이어")]
    public LayerMask damageMask;
    [Min(1f)] public float maxRange = 30f;                          // 막히지 않을 때 빛줄기 길이(총구부터, 칸)
    [Min(0.01f)] public float hitHalfWidth = 0.3f;                  // 판정 상자의 반폭(칸). 보이는 빛줄기보다 조금 넉넉하다
    public Vector2 fallbackMuzzleOffset = new Vector2(1.7f, 0f);    // 모형을 못 찾을 때의 총구(조준 방향으로 x, 위로 y)
    public RailgunTracer tracerPrefab;
    [Tooltip("쏘는 순간 총구에서 조준 방향으로 돌려 재생(섬광·흔들림·소리)")]
    public FeedbackDataSO fireFeedback;
    [Tooltip("맞은 적마다 맞은 곳에서 재생")]
    public FeedbackDataSO hitFeedback;

    // 무기 SO는 Player마다 공유되지만 한 번 쏘는 동안만 쓰는 임시 버퍼다(직렬화하지 않는다).
    [NonSerialized] private Collider2D[] overlapResults;
    [NonSerialized] private HashSet<HealthBase> damagedTargets;

    // 검증·디버그용: 마지막으로 쏜 빛줄기
    public Vector2 LastOrigin { get; private set; }
    public Vector2 LastDirection { get; private set; }
    public float LastLength { get; private set; }
    public bool LastBlocked { get; private set; }
    public int LastHitCount { get; private set; }

    // 준비 시작: 총신을 미리 조준 방향으로 든다(충전하는 동안 위 30도로 들고 있다).
    public override void OnWindupStarted(CharacterController2D controller)
    {
        if (controller == null) return;
        var aim = FindAim(controller);
        if (aim != null) aim.Aim(ResolveDirection(controller));
    }

    // 실행 첫 프레임에 바로 쏜다(StartCoroutine이 첫 yield 전까지를 그 자리에서 실행한다).
    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        Fire(controller);
        yield break;
    }

    private void Fire(CharacterController2D controller)
    {
        if (controller == null || controller.Motor == null) return;
        overlapResults ??= new Collider2D[64];
        damagedTargets ??= new HashSet<HealthBase>();

        Vector2 direction = ResolveDirection(controller);
        Vector2 center = controller.transform.position;
        var aim = FindAim(controller);
        Vector2 muzzle;
        if (aim != null && aim.Muzzle != null)
        {
            aim.Aim(direction);   // 이 프레임의 조준을 먼저 적용해 총구 위치가 조준 방향과 맞게 한다
            muzzle = aim.Muzzle.position;
        }
        else
            muzzle = center + direction * fallbackMuzzleOffset.x + Vector2.up * fallbackMuzzleOffset.y;

        // 몸 중심 높이의 총구 축: 총구를 조준선 위로 몸 쪽에 내린 점. 여기서 벽을 찾고 판정 상자도 여기서 시작한다.
        float back = Mathf.Max(0f, Vector2.Dot(muzzle - center, direction));
        Vector2 from = muzzle - direction * back;
        RaycastHit2D wall = controller.Motor.RaycastEnvironment(from, direction, maxRange + back, platformLayer);
        bool blocked = wall.collider != null;
        float reach = blocked ? wall.distance : maxRange + back;   // from에서 끝까지
        float length = Mathf.Max(0f, reach - back);                 // 총구에서 끝까지(보이는 빛줄기)
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        LastOrigin = muzzle;
        LastDirection = direction;
        LastLength = length;
        LastBlocked = blocked;
        var traits = WeaponTraitEffects.Attack(controller)?.Traits;
        bool charged = traits != null && traits.IsRailCharged;
        LastHitCount = ApplyDamage(controller, from, direction, reach, angle, muzzle, length, charged);
        traits?.AddRailHeat(0.2f);

        controller.AddRecoil(recoilPower, controller.Facing);

        if (fireFeedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.PlayExact(fireFeedback, muzzle, Quaternion.Euler(0f, 0f, angle));

        if (tracerPrefab != null && WorldEntityPool.Instance != null)
        {
            var tracer = WorldEntityPool.Instance.Spawn(tracerPrefab, muzzle, Quaternion.Euler(0f, 0f, angle));
            tracer.Play(muzzle, direction, length, blocked);
        }
    }

    // 몸 중심 높이의 총구 축(from)에서 끝(reach)까지 덮는 회전 상자 안의 적에게 한 번씩 피해를 준다. 맞은 적 수를 돌려준다.
    private int ApplyDamage(CharacterController2D controller, Vector2 from, Vector2 direction, float reach, float angle, Vector2 muzzle, float length, bool charged)
    {
        if (reach <= 0f) return 0;
        Vector2 boxCenter = from + direction * (reach * 0.5f);
        var size = new Vector2(reach, hitHalfWidth * 2f);

        // 이 프레임에 움직인 Transform을 판정에 반영한다(실행 단계는 Update에서 시작된다).
        RaycastMotor2D.InvalidateAttackQuerySync();
        int count = controller.Motor.OverlapAttackBox(boxCenter, size, angle, overlapResults, damageMask);
        if (count <= 0) return 0;

        WeaponBaseSO.DamageInfo info = WithSource(damageInfo);
        if (controller.Faction == Faction.Player)
        {
            info.knockbackOrigin = controller.transform.position;
            info.knockbackFacing = controller.Facing;
        }
        else info.knockbackFacing = 0;

        int hits = 0;
        damagedTargets.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = overlapResults[i];
            overlapResults[i] = null;
            if (hit == null) continue;
            HealthBase health = hit.GetComponentInParent<HealthBase>();
            if (health == null || !damagedTargets.Add(health)) continue;
            if (health is not EnemyHealth enemy || health.IsInvincible) continue;
            if (enemy.enemyShieldSystem != null && enemy.enemyShieldSystem.IsSpawnLocked) continue;

            // 맞은 위치: 표적 중심을 빛줄기 중심선에 내린 점(총구 뒤로 붙은 적은 총구)
            float along = Mathf.Clamp(Vector2.Dot((Vector2)hit.bounds.center - muzzle, direction), 0f, length);
            Vector2 hitPoint = muzzle + direction * along;

            if (WeaponTraitEffects.ApplyEnemyHit(enemy, info, hitPoint) && charged)
                WeaponTraitEffects.RailFollowUp(controller, muzzle, enemy, enemy.transform.position, info);
            OnHitEnemy?.Invoke(controller, hitPoint);
            if (hitFeedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(hitFeedback, hitPoint);
            hits++;
        }
        damagedTargets.Clear();
        return hits;
    }

    // 기본 공격의 조준 방향. 보는 쪽과 어긋나면(다른 자리의 지난 값) 보는 쪽 수평.
    private static Vector2 ResolveDirection(CharacterController2D controller)
    {
        float facing = controller.Facing < 0 ? -1f : 1f;
        if (controller is PlayerController player && player.AbilityManager != null && player.AbilityManager.AttackAbility != null)
        {
            Vector2 aim = player.AbilityManager.AttackAbility.PrimaryFireDirection;
            if (aim.sqrMagnitude > 0.0001f && Mathf.Abs(aim.x) > 0.0001f && Mathf.Sign(aim.x) == facing)
                return aim.normalized;
        }
        return new Vector2(facing, 0f);
    }

    private static PlayerRailgunAim FindAim(CharacterController2D controller)
    {
        if (controller == null || controller.model == null) return null;
        Transform model = controller.model.transform.Find(ModelPath);
        return model != null ? model.GetComponent<PlayerRailgunAim>() : null;
    }
}
