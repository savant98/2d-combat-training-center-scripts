using System.Collections.Generic;
using UnityEngine;

/// <summary>자동 조준이 마지막으로 고른 조준점의 종류(검증·디버그용).</summary>
public enum PlayerAutoAimPoint { None, Center, Level, Upper, Lower }

// Player 원거리 자동 조준의 표적·조준점 고르기(2026-11-26).
// 사용자 요청: 무장 전환 튜토리얼(W5)에서 자동 조준을 켜면 낮은 틈 너머 표적을 맞히지 못한다. 자동 조준 때 핸드캐논도 자동 조준한다.
// 원인: 예전에는 시야 검사 없이 가장 가까운 적의 판정 가운데(bounds.center)를 겨눴다. W5 더미(판정 높이 4칸)는 틈으로 아래 약 1칸만 보이는데
//   가운데를 겨눠 위로 8~11도 쏘았고, 탄이 가운데 벽(아랫면이 수평탄 윗변보다 0.037칸 위)에 막혔다.
// 규칙(blockLayer가 0이 아닐 때):
// - 후보: 소유자 모터의 공격 범위 질의(OverlapAttackCircle, Player 중심·반지름 range)로 모으고 RailEcho.IsTarget으로 거른 뒤,
//   Player 중심에서 판정 가운데까지 가까운 순서로 MaxCandidates개만 본다(RailEcho.FindNearest와 같은 거리 기준·같은 거리면 먼저 찾은 것).
// - 후보마다 조준점을 차례로 시험하고 처음 트인 점을 쓴다.
//   ① 가운데(트였으면 예전과 같은 결과) ② 총구 높이 점: 총구 높이가 판정 안쪽(가장자리에서 inset만큼 들인 범위)이면 방향을 정확히 수평(±1, 0)으로 둔다
//   (탄이 수평으로만 움직여 틈 높이를 그대로 지난다) ③ 판정 위쪽·아래쪽 안쪽 점(총구에 가까운 쪽부터 — 총구 높이를 판정 안쪽으로 자른 점이 곧 가까운 쪽 점이다).
//   한 점도 트이지 않으면 다음 후보로 넘어간다. 아무도 없으면 false(호출자가 마우스 조준이나 기본 방향을 쓴다).
// - 시야: 기존 모터의 static CircleCastEnvironment(투사체 규칙: 통과 발판은 지나간다)로 origin에서 조준 방향으로
//   '판정을 probeRadius만큼 넓힌 상자'에 들어가는 거리까지 쓸어 본다(진입 거리는 계산으로 구한다). 지형에 닿지 않으면 트인 것이다.
//   origin이 넓힌 상자 안이면(겹친 적) 바로 트인 것으로 본다. probeRadius는 탄의 지형 판정 크기(ProbeRadius)이고, 0이면 직선이다(레일건).
// - blockLayer가 0이면 시야를 보지 않는다: 예전처럼 RailEcho.FindNearest가 고른 가장 가까운 적의 가운데를 겨눈다(핸드캐논 — 부채꼴 피해가 지형을 무시한다).
// 물리 질의는 모두 기존 모터의 질의를 쓴다(규칙: Ability·무기 코드에서 Physics2D를 직접 부르지 않는다). 호출마다 결과를 새로 구한다(캐시 없음).
public static class PlayerAutoAim
{
    /// <summary>시야를 시험하는 후보 수(가까운 순서). 후보 4 × 조준점 4 = 최대 16번의 지형 질의.</summary>
    public const int MaxCandidates = 4;
    /// <summary>위쪽·아래쪽 안쪽 점과 총구 높이 범위를 판정 가장자리에서 들이는 거리(칸). 판정 반높이의 절반을 넘지 않는다.</summary>
    public const float PointInset = 0.2f;
    /// <summary>탄의 지형 판정은 Collider 상자를 이만큼 줄인 변에서 광선을 쏜다(MovingEntityBase.skinWidth 기본값).</summary>
    public const float ProjectileSkin = 0.01f;
    private const float MinSqrOffset = 0.0001f;   // 예전 TryGetRangedAim과 같은 '방향을 정할 수 없는' 거리(제곱)
    private const float MinLevelOffset = 0.01f;   // 총구 높이 점: 가로 거리가 이보다 작으면 수평 방향을 정하지 않는다

    /// <summary>마지막 TryFind가 고른 조준점(실패하면 None). 검증·디버그용.</summary>
    public static PlayerAutoAimPoint LastPoint { get; private set; }

    private static readonly List<Collider2D> Overlaps = new(32);
    private static readonly Collider2D[] Nearest = new Collider2D[MaxCandidates];
    private static readonly float[] NearestDistance = new float[MaxCandidates];
    private static readonly Dictionary<ProjectileBase, float> ProbeRadii = new();

    // 편집기의 도메인 리로드가 꺼져 있어도 Play마다 비운다(Prefab을 고친 뒤 지난 반지름이 남지 않게).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Overlaps.Clear();
        System.Array.Clear(Nearest, 0, Nearest.Length);
        ProbeRadii.Clear();
        LastPoint = PlayerAutoAimPoint.None;
    }

    /// <summary>
    /// 자동 조준 방향과 표적을 고른다. origin = 탄이 나가는 총신 기준점, range = Player 중심에서 찾는 반지름,
    /// blockLayer = 탄을 막는 지형(0이면 시야 검사 없음), probeRadius = 탄의 지형 판정 반지름. 없으면 false.
    /// </summary>
    public static bool TryFind(CharacterController2D owner, Vector2 origin, float range, LayerMask targets, LayerMask blockLayer,
        float probeRadius, out Vector2 direction, out Collider2D target)
    {
        direction = default;
        target = null;
        LastPoint = PlayerAutoAimPoint.None;
        if (owner == null || owner.Motor == null || range <= 0f) return false;
        Vector2 from = owner.transform.position;

        if (blockLayer.value == 0)
        {
            // 시야를 보지 않는다: 예전 TryGetRangedAim과 같은 결과(가장 가까운 적 하나의 가운데, 너무 가까우면 실패).
            target = RailEcho.FindNearest(owner, from, Vector2.zero, range, 0f, targets);
            if (target == null) return false;
            Vector2 offset = (Vector2)target.bounds.center - origin;
            if (offset.sqrMagnitude < MinSqrOffset)
            {
                target = null;
                return false;
            }
            direction = offset.normalized;
            LastPoint = PlayerAutoAimPoint.Center;
            return true;
        }

        probeRadius = Mathf.Max(0f, probeRadius);
        int count = Collect(owner, from, range, targets);
        try
        {
            for (int i = 0; i < count; i++)
            {
                Collider2D candidate = Nearest[i];
                if (candidate == null) continue;
                if (!TryCandidate(candidate.bounds, origin, blockLayer, probeRadius, out direction, out var point)) continue;
                target = candidate;
                LastPoint = point;
                return true;
            }
        }
        finally
        {
            // 정적 버퍼에 적 참조를 남기지 않는다.
            System.Array.Clear(Nearest, 0, count);
        }
        direction = default;
        return false;
    }

    /// <summary>
    /// 탄 Prefab의 지형 판정 반지름: 원 Collider 반지름 × |배율| − skin(라이플 탄 약 0.199칸). 원 Collider가 없으면 0(직선).
    /// Prefab마다 한 번만 읽는다.
    /// </summary>
    public static float ProbeRadius(ProjectileBase prefab)
    {
        if (prefab == null) return 0f;
        if (ProbeRadii.TryGetValue(prefab, out float radius)) return radius;
        radius = 0f;
        if (prefab.TryGetComponent<CircleCollider2D>(out var circle))
        {
            Vector3 scale = circle.transform.lossyScale;
            radius = Mathf.Max(0f, circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)) - ProjectileSkin);
        }
        ProbeRadii[prefab] = radius;
        return radius;
    }

    // 후보를 Player 중심에서 판정 가운데까지 가까운 순서로 MaxCandidates개 모은다(RailEcho.FindNearest와 같은 질의·거름).
    private static int Collect(CharacterController2D owner, Vector2 from, float range, LayerMask targets)
    {
        RaycastMotor2D.InvalidateAttackQuerySync();   // 이 프레임에 움직인 Transform을 판정에 반영한다
        var filter = new ContactFilter2D();
        filter.SetLayerMask(targets);
        filter.useTriggers = true;
        owner.Motor.OverlapAttackCircle(from, range, filter, Overlaps);
        int count = 0;
        foreach (var c in Overlaps)
        {
            if (!RailEcho.IsTarget(c)) continue;
            float distance = ((Vector2)c.bounds.center - from).sqrMagnitude;
            int at = count;
            while (at > 0 && distance < NearestDistance[at - 1]) at--;   // 같은 거리면 먼저 찾은 것이 앞
            if (at >= MaxCandidates) continue;
            for (int k = Mathf.Min(count, MaxCandidates - 1); k > at; k--)
            {
                Nearest[k] = Nearest[k - 1];
                NearestDistance[k] = NearestDistance[k - 1];
            }
            Nearest[at] = c;
            NearestDistance[at] = distance;
            if (count < MaxCandidates) count++;
        }
        Overlaps.Clear();
        return count;
    }

    // 한 후보의 조준점을 차례로 시험한다: 가운데 → 총구 높이(수평) → 위쪽·아래쪽 안쪽 점(총구에 가까운 쪽부터).
    private static bool TryCandidate(Bounds bounds, Vector2 origin, LayerMask blockLayer, float probeRadius,
        out Vector2 direction, out PlayerAutoAimPoint point)
    {
        Vector2 center = bounds.center;
        point = PlayerAutoAimPoint.Center;
        if (TryPoint(origin, center, bounds, blockLayer, probeRadius, out direction)) return true;

        float inset = Mathf.Min(PointInset, bounds.extents.y * 0.5f);
        float low = bounds.min.y + inset, high = bounds.max.y - inset;
        float dx = center.x - origin.x;
        if (origin.y >= low && origin.y <= high && Mathf.Abs(dx) > MinLevelOffset)
        {
            point = PlayerAutoAimPoint.Level;
            direction = new Vector2(Mathf.Sign(dx), 0f);
            if (IsOpen(origin, direction, bounds, blockLayer, probeRadius)) return true;
        }

        bool upperFirst = origin.y >= center.y;
        for (int k = 0; k < 2; k++)
        {
            bool upper = (k == 0) == upperFirst;
            point = upper ? PlayerAutoAimPoint.Upper : PlayerAutoAimPoint.Lower;
            if (TryPoint(origin, new Vector2(center.x, upper ? high : low), bounds, blockLayer, probeRadius, out direction)) return true;
        }
        point = PlayerAutoAimPoint.None;
        direction = default;
        return false;
    }

    private static bool TryPoint(Vector2 origin, Vector2 aimPoint, Bounds bounds, LayerMask blockLayer, float probeRadius, out Vector2 direction)
    {
        Vector2 offset = aimPoint - origin;
        if (offset.sqrMagnitude < MinSqrOffset)
        {
            direction = default;
            return false;
        }
        direction = offset.normalized;
        return IsOpen(origin, direction, bounds, blockLayer, probeRadius);
    }

    // origin에서 direction으로 '판정을 probeRadius만큼 넓힌 상자'에 들어가기 전까지 지형에 닿지 않으면 트인 것이다.
    private static bool IsOpen(Vector2 origin, Vector2 direction, Bounds bounds, LayerMask blockLayer, float probeRadius)
    {
        if (!TryEntry(origin, direction, bounds, probeRadius, out float entry)) return false;
        if (entry <= 0f) return true;   // origin이 넓힌 상자 안(겹친 적)
        return RaycastMotor2D.CircleCastEnvironment(origin, probeRadius, direction, entry, blockLayer).collider == null;
    }

    // 광선(origin, direction)이 bounds를 pad만큼 넓힌 상자에 처음 들어가는 거리(슬래브 방식). 상자를 지나지 않으면 false, origin이 안이면 0.
    private static bool TryEntry(Vector2 origin, Vector2 direction, Bounds bounds, float pad, out float entry)
    {
        Vector2 min = (Vector2)bounds.min - new Vector2(pad, pad);
        Vector2 max = (Vector2)bounds.max + new Vector2(pad, pad);
        float near = 0f, far = float.PositiveInfinity;
        for (int axis = 0; axis < 2; axis++)
        {
            float o = origin[axis], d = direction[axis];
            if (Mathf.Abs(d) < 1e-6f)
            {
                if (o < min[axis] || o > max[axis]) { entry = 0f; return false; }
                continue;
            }
            float t1 = (min[axis] - o) / d, t2 = (max[axis] - o) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);
            near = Mathf.Max(near, t1);
            far = Mathf.Min(far, t2);
            if (near > far) { entry = 0f; return false; }
        }
        entry = near;
        return true;
    }
}
