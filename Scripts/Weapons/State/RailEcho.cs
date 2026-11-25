using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>레일건 충전 추가타의 종류(본 공격): 기본 사격 = 미니 레일 사격, 별창 = 미니 별창, 파괴 광선 = 미니 레이저.</summary>
public enum RailEchoKind { Rail, Spear, Laser }

/// <summary>
/// 추가타 한 묶음(본 공격 한 번)의 값. RailEcho.Begin에 넘기면 count발의 RailEchoShot이 같은 요청을 함께 읽는다.
/// 피해(damage)는 이미 배율을 곱하고 넉백을 뺀 한 발 피해다.
/// </summary>
public sealed class RailEchoRequest
{
    public PlayerController owner;
    public RailgunWeaponSO rail;
    public RailEchoKind kind;
    public RailEchoShot prefab;
    public SoundOptions fireSound;
    public WeaponBaseSO.DamageInfo damage;
    public int count;
    public float interval;
    public int slotBase;                        // 이 묶음의 첫 발이 쓰는 자리 번호(별창은 본 창마다 다른 자리부터)
    public float facing = 1f;                   // 본 공격 순간 보는 쪽(자리 좌우를 정한다 — 그 뒤 돌아서도 자리는 그대로)

    // 목표: 쏘는 순간 liveTarget → target(본 공격이 맞힌 적) → 조준 방향의 가장 가까운 적 → fallbackPoint 순서로 정한다.
    public Collider2D target;
    public Func<Collider2D> liveTarget;
    public Vector2 fallbackPoint;
    public Vector2 searchDirection;             // 0이면 Player 둘레 원(searchRange 반지름)에서 찾는다(별창)
    public float searchRange, searchHalfWidth;

    // 판정(기본 사격·파괴 광선): 쏘는 자리에서 목표 쪽으로 관통하는 직선
    public float range, halfWidth;
    public LayerMask platformLayer, damageMask;

    // 별창: 미니 별창(StarSpear Variant)과 그 비행 값
    public StarSpearWeaponSO spearWeapon;
    public StarSpear spearPrefab;
    public float spearRadiusScale = 1f;
    public int spearIndex;

    public Func<bool> guard;                    // false가 되면 아직 쏘지 않은 추가타를 버린다(파괴 광선: 본 레이저가 꺼짐)
    public Action<RailEchoShot, int> onFired;   // 쏜 순간(빛줄기가 피해를 준 적 수 — 별창은 0)
    public Action<RailEchoShot> onDropped;      // 쏘기 전에 버려짐
}

// 레일건 충전 추가타 공용 규칙(2026-11-21 무장 특성 4차, 사용자 요청 — 사용자 화면 검수 전 시안).
// 사용자 요청 원문 요지: "레일건 충전상태에서 일반공격시 지금은 똑같은 레일건이 2발 더 나가는데, 이게 아니라 플레이어 주위에서 날아가는 거야
//   (그림: 총구가 아니라 Player 몸 위쪽 바깥·아래쪽 바깥(바닥 근처)의 점에서 두 줄이 목표로 모여든다). 별창발사와 레이저발사 시의 추가타도 마찬가지.
//   지금은 이펙트가 잘 안 보여. 시간차로 날아가게 만들어. 일반공격 추가타는 일반공격, 별창은 별창, 레이저는 레이저 이펙트를 참고해서 미니 사이즈로."
// - 본 공격 한 번마다 Begin이 count발의 RailEchoShot(풀 개체)을 꺼낸다. k번째 발은 본 공격 (k + 1) × interval 뒤에 쏘고, 그 조금 전부터
//   Player 둘레의 자리(RailEchoShot.slots — 몸 위쪽·아래쪽 바깥)에 작은 소환 문양이 나타나 목표를 겨눈다. 시간차라 한 발씩 따로 보인다.
// - 목표: 본 공격이 맞힌 적 → (사라졌으면) 조준 방향의 가장 가까운 적 → (없으면) 본 공격 끝점. 자리마다 쏘는 방향이 달라 목표로 모여든다.
// - 멈춤: Player가 꺼지거나 쓰러지거나 무장 특성이 초기화되면(PlayerWeaponTraits.Generation), 기본 사격 추가타는 과열 잠금이 걸리면,
//   파괴 광선 추가타는 본 레이저를 중단하면 예약된 발을 버린다. 정상 종료 직전에 예약된 발은 마저 쏜다. 무장을 바꿔도(레일건을 내려놓아도) 이미 시작한 추가타는 끝까지 쏜다 —
//   총구가 아니라 Player 둘레에서 나가고, 본 공격 때 이미 정해진 몫이기 때문이다(판단값).
// - 시간은 Time.deltaTime이라 히트스톱 때 함께 멈춘다. 기술 게이지(OnHitEnemy)·넉백은 주지 않는다.
// 판정 공용 함수 Cast: 벽은 모터의 RaycastEnvironment, 적은 모터의 OverlapAttackBox(물리 판정 규칙). 목표 찾기도 모터의 OverlapAttackBox·OverlapAttackCircle.
public static class RailEcho
{
    /// <summary>아직 쏘지 않은(소환 대기·소환 중) 추가타 수(검증용, 세 종류 모두).</summary>
    public static int ActiveCount { get; internal set; }
    /// <summary>지금까지 쏜 추가타의 누적 수(검증용, 세 종류 모두).</summary>
    public static int TotalFired { get; internal set; }

    private static readonly Collider2D[] OverlapResults = new Collider2D[64];
    private static readonly List<Collider2D> CircleResults = new(32);
    private static readonly HashSet<HealthBase> Damaged = new();

    /// <summary>추가타 한 묶음을 시작한다. 꺼낸 발 수를 돌려준다(값이 모자라면 0).</summary>
    public static int Begin(RailEchoRequest request)
    {
        if (request == null || request.owner == null || request.prefab == null || request.count <= 0 || WorldEntityPool.Instance == null) return 0;
        var traits = request.owner.AbilityManager?.AttackAbility?.Traits;
        if (traits == null) return 0;
        if (request.kind == RailEchoKind.Spear && (request.spearPrefab == null || request.spearWeapon == null)) return 0;
        if (request.damage.popupGroup == 0) request.damage.popupGroup = WeaponBaseSO.NewPopupGroup();
        request.damage.popupAnchorOnly = false;
        if (request.fireSound?.clip != null)
        {
            request.fireSound = request.fireSound.WithPitchMultiplier(1.2f);
            // 본타와 같은 클립을 짧게 연달아 쓰므로 추가타에만 중복 제한을 완화한다.
            request.fireSound.minInterval = 0f;
            int voices = request.kind == RailEchoKind.Laser ? 24 : 16;
            request.fireSound.maxSimultaneous = Mathf.Max(request.fireSound.maxSimultaneous, voices);
        }
        request.facing = request.facing < 0f ? -1f : 1f;
        float interval = Mathf.Max(0.01f, request.interval);
        for (int k = 0; k < request.count; k++)
        {
            var shot = WorldEntityPool.Instance.Spawn(request.prefab, request.owner.transform.position, Quaternion.identity);
            shot.Begin(request, k, (k + 1) * interval, traits);
        }
        return request.count;
    }

    /// <summary>쫓을 수 있는 적인가: 켜져 있고 살아 있으며 생성 중·무적이 아니다.</summary>
    public static bool IsTarget(Collider2D target)
    {
        if (!StarSpear.IsTrackable(target)) return false;
        var health = target.GetComponentInParent<HealthBase>();
        if (DungeonPropTargeting.IsProp(health)) return false;   // 파괴형 던전 Prop은 자동 조준·레일건 표적이 아니다
        return health is EnemyHealth && !health.IsInvincible && health.currentHP > 0f;
    }

    /// <summary>
    /// from에서 가장 가까운 적. direction이 0이 아니면 from에서 direction으로 range만큼 뻗은 상자(반폭 halfWidth) 안에서,
    /// 0이면 반지름 range인 원 안에서 찾는다(모터의 공격 범위 질의). 없으면 null.
    /// </summary>
    public static Collider2D FindNearest(CharacterController2D owner, Vector2 from, Vector2 direction, float range, float halfWidth, LayerMask mask)
    {
        if (owner == null || owner.Motor == null || range <= 0f) return null;
        RaycastMotor2D.InvalidateAttackQuerySync();
        Collider2D best = null;
        float bestDistance = float.MaxValue;
        if (direction.sqrMagnitude > 1e-6f)
        {
            direction = direction.normalized;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            int count = owner.Motor.OverlapAttackBox(from + direction * (range * 0.5f), new Vector2(range, Mathf.Max(0.01f, halfWidth) * 2f), angle,
                OverlapResults, mask);
            for (int i = 0; i < count; i++)
            {
                Collider2D c = OverlapResults[i];
                OverlapResults[i] = null;
                Consider(c, from, ref best, ref bestDistance);
            }
        }
        else
        {
            var filter = new ContactFilter2D();
            filter.SetLayerMask(mask);
            filter.useTriggers = true;
            owner.Motor.OverlapAttackCircle(from, range, filter, CircleResults);
            foreach (var c in CircleResults) Consider(c, from, ref best, ref bestDistance);
            CircleResults.Clear();
        }
        return best;
    }

    private static void Consider(Collider2D c, Vector2 from, ref Collider2D best, ref float bestDistance)
    {
        if (!IsTarget(c)) return;
        float distance = ((Vector2)c.bounds.center - from).sqrMagnitude;
        if (distance >= bestDistance) return;
        bestDistance = distance;
        best = c;
    }

    /// <summary>
    /// 쏘는 자리(muzzle)에서 direction으로 한 줄을 쏜다. 벽은 몸 쪽으로 back만큼 물린 점에서 모터의 RaycastEnvironment로 찾고(통과 발판은 지나간다),
    /// 그 점에서 벽(또는 range)까지 덮는 회전 상자를 모터의 OverlapAttackBox로 찾아 겹친 적마다 한 번 피해를 준다(관통).
    /// 피해가 실제로 들어간 적마다 RailEchoHit 큐를 맞은 곳에서 재생하고 onHit을 부른다.
    /// length = 쏘는 자리에서 끝까지(보이는 빛줄기), blocked = 벽에 막혔는지. 돌려주는 값 = 피해가 들어간 적 수.
    /// </summary>
    public static int Cast(CharacterController2D owner, Vector2 muzzle, Vector2 direction, float back, float range, float halfWidth,
        LayerMask platformLayer, LayerMask damageMask, WeaponBaseSO.DamageInfo info, out float length, out bool blocked,
        Action<EnemyHealth, Vector2> onHit = null)
    {
        length = 0f;
        blocked = false;
        if (owner == null || owner.Motor == null) return 0;
        direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
        back = Mathf.Max(0f, back);
        Vector2 from = muzzle - direction * back;
        RaycastHit2D wall = owner.Motor.RaycastEnvironment(from, direction, range + back, platformLayer);
        blocked = wall.collider != null;
        float reach = blocked ? wall.distance : range + back;
        length = Mathf.Max(0f, reach - back);
        if (reach <= 0f || info.damage <= 0f) return 0;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Vector2 center = from + direction * (reach * 0.5f);
        RaycastMotor2D.InvalidateAttackQuerySync();   // 이 프레임에 움직인 Transform을 판정에 반영한다
        int count = owner.Motor.OverlapAttackBox(center, new Vector2(reach, Mathf.Max(0.01f, halfWidth) * 2f), angle, OverlapResults, damageMask);
        int hits = 0;
        Damaged.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider2D collider = OverlapResults[i];
            OverlapResults[i] = null;
            if (collider == null) continue;
            HealthBase health = collider.GetComponentInParent<HealthBase>();
            if (health == null || !Damaged.Add(health)) continue;
            if (health is not EnemyHealth enemy || health.IsInvincible) continue;
            if (enemy.enemyShieldSystem != null && enemy.enemyShieldSystem.IsSpawnLocked) continue;
            // 맞은 위치: 표적 중심을 빛줄기 중심선에 내린 점(쏘는 자리 뒤로 붙은 적은 쏘는 자리)
            float along = Mathf.Clamp(Vector2.Dot((Vector2)collider.bounds.center - muzzle, direction), 0f, length);
            Vector2 point = muzzle + direction * along;
            if (!WeaponTraitEffects.ApplyEnemyHit(enemy, info, point)) continue;
            hits++;
            WeaponTraitFeedbackSO.Play(WeaponTraitCue.RailEchoHit, point, direction);
            onHit?.Invoke(enemy, point);
        }
        Damaged.Clear();
        return hits;
    }
}
