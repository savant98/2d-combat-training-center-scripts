using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 레일건(Railgun 무장의 기본 무기, 2026-11-18 1차 시안).
// 사용자 지시: Player 무장을 4종으로 정리 — Railgun = 기본 '레일건'(새로 만듦) / Gear 1단계 없음 / 2단계 별창 발사 / 3단계 파괴 광선.
//   "현재 개발되지 않은 무장들은 개발된 무장을 참고해서 만들어봐." 무장 설명: "충전한 관통탄으로 일직선을 꿰뚫는다."
// - 손맛: Rifle(StarShot, 단발 연사)과 다르게 한 발씩 쏜다. 짧은 충전(준비 단계, 모형의 코어가 밝아진다) → 실행 첫 프레임에
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
// - 무장 특성(2026-11-21 3차, 사용자 요청 — 사용자 검수 전 시안):
//   · 과열: 한 발마다 PlayerWeaponTraits.RailHeatPerShot(0.1) → 연속 10발에 과열 잠금. 총열이 달아오르는 표시는 RailgunBarrelVisual(모형에 붙음)이 맡는다.
//   · 충전 추가타: 레일건 충전(기어 1단계, RailgunCharge) 상태로 쏘면 본 사격 뒤 echoInterval(약 0.067초)마다 echoCount(2)발의 미니 레일 사격을 쏜다.
//     별창의 미니 별창·파괴 광선의 미니 레이저 값도 이 에셋이 가진다(같은 무장의 충전 추가타).
//   · RailgunWeaponSO.FindFor로 Player의 레일건 무기 에셋을 찾는다(별창·파괴 광선은 Railgun 무장에만 있으므로 실행 중인 자리의 기본 무기가 레일건이다).
// - 4차(2026-11-21, 사용자 요청 "추가타는 총구가 아니라 플레이어 주위에서 날아가는 거야, 시간차로, 각 공격 이펙트의 미니 사이즈로" — 사용자 화면 검수 전 시안):
//   추가타는 총구에서 같은 방향으로 나가지 않고 Player 몸 위쪽·아래쪽 바깥에 작은 소환 문양(RailEchoShot)이 나타나 본 사격이 맞힌 적
//   (사라졌으면 조준 방향의 가장 가까운 적, 없으면 본 사격 끝점)으로 모이듯 쏜다(규칙·판정은 RailEcho). 총을 들고 있게 하던 처리(HoldForEcho)·추가타 반동은 없앴다.
//   피해 배율·발 수는 3차 값 그대로(밸런스 담당이 이 에셋에서 조정한다). 충전 지속 표시(RailChargeAura)의 Prefab도 이 에셋이 가진다.
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

    [Header("Charged Echo (레일건 충전 추가타 — 4차: Player 둘레에서 목표로 모여드는 미니 공격)")]
    [Tooltip("미니 레일 사격 한 발(소환 문양 + 본 레일건과 같은 재질의 가는 빛줄기)")]
    public RailEchoShot echoShotPrefab;
    [Min(0)] public int echoCount = 2;                              // 충전 사격 한 발 뒤 미니 레일 사격 수
    [Min(0.01f)] public float echoInterval = 1f / 15f;             // 돌진베기 연타와 같은 간격. 미니 별창·미니 레이저에도 사용한다.
    [Min(0f)] public float echoDamageScale = 0.45f;                 // 미니 레일 사격 피해 = 본 사격 × 이 값
    [Min(0.01f)] public float echoHalfWidth = 0.24f;                // 미니 레일 사격 판정 반폭(칸)
    [Min(0.1f)] public float echoSearchHalfWidth = 2.5f;            // 맞힌 적이 사라졌을 때 '조준 방향의 가장 가까운 적'을 찾는 상자 반폭(칸, 판단값)

    [Header("Charged Echo - Star Spear (미니 별창)")]
    public StarSpear miniSpearPrefab;
    [Tooltip("미니 별창 소환 문양(별 문양 — 미니 별창을 들고 있다가 쏜다)")]
    public RailEchoShot miniSpearShotPrefab;
    [Min(0)] public int miniSpearCount = 2;                         // 본 창 하나마다 뒤따르는 미니 별창 수
    [Min(0f)] public float miniSpearDamageScale = 0.45f;            // 미니 별창 피해 = 별창 피해 × 이 값
    [Min(0f)] public float miniSpearRadiusScale = 0.4f;             // 미니 별창 폭발 반경 = 별창 폭발 반경 × 이 값(3.75 → 1.5칸)

    [Header("Charged Echo - Laser (미니 레이저)")]
    [Tooltip("미니 레이저 한 발(조준선 → 본 레이저와 같은 재질의 작은 레이저)")]
    public RailEchoShot miniLaserShotPrefab;
    [Min(0)] public int miniLaserCount = 2;                         // 미니 레이저 한 묶음(순차 타격)의 발 수
    [Min(0f)] public float miniLaserDamageScale = 0.65f;            // 미니 레이저 피해 = 파괴 광선 한 틱 피해 × 이 값
    [Min(0.01f)] public float miniLaserHalfWidth = 0.35f;           // 미니 레이저 판정 반폭(칸)

    [Header("Charge Aura (충전 지속 표시, 4차)")]
    [Tooltip("Player 몸 둘레의 충전 궤도·칸(RailChargeAura) — 충전할 때 Player 아래에 한 번 복제한다")]
    public RailChargeAura chargeAuraPrefab;

    [Header("Barrel Heat (총열 과열·충전 표시, RailgunBarrelVisual)")]
    [Tooltip("총 Sprite 덧그림(Player/RailBarrelHeat _Mode 0)")]
    public Material barrelHeatMaterial;
    [Tooltip("총구·통풍구 흰 증기(_Mode 1)")]
    public Material steamMaterial;
    [Tooltip("불티·전류 불꽃(_Mode 2, 색은 입자 색)")]
    public Material sparkMaterial;
    [Tooltip("과열 잠금 연기(_Mode 3)")]
    public Material smokeMaterial;

    // 무기 SO는 Player마다 공유되지만 한 번 쏘는 동안만 쓰는 임시 버퍼다(직렬화하지 않는다).
    [NonSerialized] private Collider2D[] overlapResults;
    [NonSerialized] private HashSet<HealthBase> damagedTargets;

    // 검증·디버그용: 마지막으로 쏜 빛줄기
    public Vector2 LastOrigin { get; private set; }
    public Vector2 LastDirection { get; private set; }
    public float LastLength { get; private set; }
    public bool LastBlocked { get; private set; }
    public int LastHitCount { get; private set; }
    public bool LastCharged { get; private set; }
    // 검증·디버그용: 마지막 추가 레일 사격
    public Vector2 LastEchoOrigin { get; private set; }
    public Vector2 LastEchoDirection { get; private set; }
    public int LastEchoHitCount { get; private set; }

    // 준비 시작: 총신을 미리 조준 방향으로 든다(충전하는 동안 위 30도로 들고 있다).
    public override void OnWindupStarted(CharacterController2D controller)
    {
        if (controller == null) return;
        var aim = FindAim(controller);
        if (aim == null) return;
        if (controller is PlayerController player && player.UsesMouseAim) return;
        bool assisted = TryGetAim(controller, aim, out var direction);
        aim.Aim(assisted ? direction : ResolveDirection(controller), assisted);
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

        Vector2 center = controller.transform.position;
        var aim = FindAim(controller);
        bool assisted = TryGetAim(controller, aim, out var direction);
        if (!assisted) direction = ResolveDirection(controller);
        Vector2 muzzle;
        if (aim != null && aim.Muzzle != null)
        {
            aim.Aim(direction, assisted);   // 이 프레임의 조준을 먼저 적용해 총구 위치가 조준 방향과 맞게 한다
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
        LastCharged = charged;
        WeaponBaseSO.DamageInfo info = ShotInfo(controller);
        if (charged && echoShotPrefab != null && echoCount > 0)
        {
            info.popupGroup = NewPopupGroup();
            info.popupAnchorOnly = true;
        }
        LastHitCount = ApplyDamage(controller, from, direction, reach, angle, muzzle, length, info, out Collider2D firstHit);
        // 2026-11-21 사용자 지시 3차: 연속 10발에 과열 상한(한 발 = RailHeatPerShot)
        traits?.AddRailHeat(PlayerWeaponTraits.RailHeatPerShot);

        controller.AddRecoil(assisted ? recoilPower * Mathf.Abs(direction.x) : recoilPower,
            assisted && direction.x != 0f ? (direction.x > 0f ? 1 : -1) : controller.Facing);
        if (aim != null) aim.Barrel.Shot();

        if (fireFeedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.PlayExact(fireFeedback, muzzle, Quaternion.Euler(0f, 0f, angle));

        if (tracerPrefab != null && WorldEntityPool.Instance != null)
        {
            var tracer = WorldEntityPool.Instance.Spawn(tracerPrefab, muzzle, Quaternion.Euler(0f, 0f, angle));
            tracer.Play(muzzle, direction, length, blocked);
        }

        // 충전 추가타: 이 사격으로 과열 잠금이 걸렸으면(충전이 끊김) 쏘지 않는다.
        if (charged && traits.OverheatRemaining <= 0f && controller is PlayerController player)
            BeginEcho(player, muzzle, direction, length, info, firstHit);
    }

    // 미니 레일 사격 echoCount발: Player 둘레 자리에서 본 사격이 맞힌 적(총구에서 가장 가까운 적)으로 모여든다.
    private void BeginEcho(PlayerController player, Vector2 muzzle, Vector2 direction, float length, WeaponBaseSO.DamageInfo shot, Collider2D firstHit)
    {
        if (echoShotPrefab == null || echoCount <= 0) return;
        shot.damage *= echoDamageScale;
        shot.knockbackFacing = 0;
        RailEcho.Begin(new RailEchoRequest
        {
            owner = player,
            rail = this,
            kind = RailEchoKind.Rail,
            prefab = echoShotPrefab,
            fireSound = fireFeedback?.sound,
            damage = shot,
            count = echoCount,
            interval = echoInterval,
            facing = player.Facing,
            target = firstHit,
            fallbackPoint = muzzle + direction * length,
            searchDirection = direction,
            searchRange = maxRange,
            searchHalfWidth = echoSearchHalfWidth,
            range = maxRange,
            halfWidth = echoHalfWidth,
            platformLayer = platformLayer,
            damageMask = damageMask,
        });
    }

    /// <summary>미니 레일 사격을 쏜 순간(RailEchoShot): 검증·디버그용 기록.</summary>
    public void RecordEcho(Vector2 origin, Vector2 direction, int hits)
    {
        LastEchoOrigin = origin;
        LastEchoDirection = direction;
        LastEchoHitCount = hits;
    }

    // 쏜 순간의 피해 정보: 넉백 출처는 WorldEntityBase.Initialize가 Player 탄에 넣는 값과 같다.
    private WeaponBaseSO.DamageInfo ShotInfo(CharacterController2D controller)
    {
        WeaponBaseSO.DamageInfo info = WithSource(damageInfo);
        if (controller.Faction == Faction.Player)
        {
            info.knockbackOrigin = controller.transform.position;
            info.knockbackFacing = controller.Facing;
        }
        else info.knockbackFacing = 0;
        return info;
    }

    // 몸 중심 높이의 총구 축(from)에서 끝(reach)까지 덮는 회전 상자 안의 적에게 한 번씩 피해를 준다. 맞은 적 수를 돌려준다.
    // firstHit = 맞힌 적 가운데 총구에서 가장 가까운 것(충전 추가타의 목표).
    private int ApplyDamage(CharacterController2D controller, Vector2 from, Vector2 direction, float reach, float angle, Vector2 muzzle, float length,
        WeaponBaseSO.DamageInfo info, out Collider2D firstHit)
    {
        firstHit = null;
        if (reach <= 0f) return 0;
        Vector2 boxCenter = from + direction * (reach * 0.5f);
        var size = new Vector2(reach, hitHalfWidth * 2f);

        // 이 프레임에 움직인 Transform을 판정에 반영한다(실행 단계는 Update에서 시작된다).
        RaycastMotor2D.InvalidateAttackQuerySync();
        int count = controller.Motor.OverlapAttackBox(boxCenter, size, angle, overlapResults, damageMask);
        if (count <= 0) return 0;

        int hits = 0;
        float nearest = float.MaxValue;
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
            if (along < nearest)
            {
                nearest = along;
                firstHit = hit;
            }

            WeaponTraitEffects.ApplyEnemyHit(enemy, info, hitPoint);
            OnHitEnemy?.Invoke(controller, hitPoint);
            if (hitFeedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(hitFeedback, hitPoint);
            hits++;
        }
        damagedTargets.Clear();
        return hits;
    }

    // 기본 공격의 조준 방향. 보는 쪽과 어긋나면(다른 자리의 지난 값) 보는 쪽 수평.
    // 자동 조준 시야(2026-11-26): 빛줄기를 막는 지형(platformLayer) 뒤의 적은 고르지 않는다. 벽 판정이 반지름 0 광선이므로 시야도 직선(반지름 0)으로 본다.
    private bool TryGetAim(CharacterController2D controller, PlayerRailgunAim aim, out Vector2 direction)
    {
        direction = default;
        return controller is PlayerController player && player.TryGetRangedAim(
            aim != null ? aim.transform.position : player.transform.position, maxRange, damageMask, platformLayer, 0f, out direction, out _);
    }

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

    /// <summary>Player 모형의 레일건 총신 조준(꺼진 모형이어도 찾는다). 없으면 null.</summary>
    public static PlayerRailgunAim FindAim(CharacterController2D controller)
    {
        if (controller == null || controller.model == null) return null;
        Transform model = controller.model.transform.Find(ModelPath);
        return model != null ? model.GetComponent<PlayerRailgunAim>() : null;
    }

    /// <summary>
    /// Player의 레일건 무기 에셋(실행용 사본). 지금 실행 중인 자리의 기본 무기가 레일건이면 그것을, 아니면 레일건을 든 다른 자리를 찾는다.
    /// 별창·파괴 광선·레일건 충전은 Railgun 무장의 Gear 기술이라 실행 자리의 기본 무기가 곧 레일건이다. 없으면 null.
    /// </summary>
    public static RailgunWeaponSO FindFor(CharacterController2D owner)
    {
        var attack = WeaponTraitEffects.Attack(owner);
        if (attack == null) return null;
        if (attack.GetWeaponSet(attack.ExecutingSetIndex)?.normal is RailgunWeaponSO executing) return executing;
        for (int i = 0; i < attack.WeaponSetCount; i++)
            if (attack.GetWeaponSet(i)?.normal is RailgunWeaponSO rail) return rail;
        return null;
    }
}
