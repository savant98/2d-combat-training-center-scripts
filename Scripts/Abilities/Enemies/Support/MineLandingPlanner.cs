using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지뢰를 고르게 뿌릴 착지점을 정한다(Stomper의 지뢰 살포에서 쓰던 계산을 Nightmare의 베기 지뢰도 함께 쓰도록 옮긴 것).
/// 벽과 바닥은 모터의 환경 질의로 확인하고, 이미 놓였거나 날아가는 지뢰의 자리를 피해 빈 구간을 고른 간격으로 채운다.
/// 결과는 바깥부터 좌우를 번갈아 쏘는 순서다.
/// </summary>
public static class MineLandingPlanner
{
    private const float WallMargin = .8f;

    /// <param name="origin">살포 기준점(좌우 벽을 찾는 높이). 이 점의 x를 가운데로 본다</param>
    /// <param name="mineCount">최대 개수. 자리가 모자라면 더 적게 정한다</param>
    /// <param name="results">발사 순서대로 채워지는 착지점(지면 위의 점). 호출 전에 비운다</param>
    public static void Plan(RaycastMotor2D motor, Vector2 origin, int mineCount, float scatterRadius, float minimumSeparation, float landingJitter, List<Vector2> results)
    {
        results.Clear();
        RaycastMotor2D.InvalidateAttackQuerySync(); RaycastMotor2D.PrepareAttackQueries();
        var left = motor.RaycastEnvironment(origin, Vector2.left, 100);
        var right = motor.RaycastEnvironment(origin, Vector2.right, 100);
        float min = origin.x - scatterRadius, max = origin.x + scatterRadius;
        if (left.collider != null) min = Mathf.Max(min, left.point.x + WallMargin);
        if (right.collider != null) max = Mathf.Min(max, right.point.x - WallMargin);
        if (max <= min || mineCount <= 0) return;
        var occupied = new List<float>();
        foreach (var mine in Object.FindObjectsByType<LandMine>(FindObjectsSortMode.None))
            if (mine.IsFlying && mine.HasLandingTarget) occupied.Add(mine.PlannedLandingPoint.x);
            else if (!mine.IsFlying) occupied.Add(mine.transform.position.x);
        float separation = Mathf.Max(.1f, minimumSeparation);
        occupied.RemoveAll(x => x < min - separation || x > max + separation);
        occupied.Sort();
        var sorted = new List<Vector2>();
        if (occupied.Count == 0)
        {
            int count = Mathf.Min(mineCount, Mathf.Max(1, Mathf.FloorToInt((max - min) / separation)));
            float width = (max - min) / count;
            // 다음 살포도 사이에 들어올 수 있도록 첫 살포의 난수 폭을 제한한다.
            float jitter = Mathf.Min(landingJitter * width,
                Mathf.Max(0, (width - 2 * separation) * .5f - .01f));
            for (int i = 0; i < count; i++)
                AddLandingPoint(motor, min + (i + .5f) * width + Random.Range(-jitter, jitter), origin.y, sorted);
        }
        else
        {
            // 이미 놓인 지뢰 사이의 빈 구간 전체에서 안전한 후보를 먼저 만든다.
            var candidates = new List<float>();
            float start = min;
            foreach (float position in occupied)
            {
                AddAvailableSlots(start, Mathf.Min(max, position - separation), separation, landingJitter, candidates);
                start = Mathf.Max(start, position + separation);
            }
            AddAvailableSlots(start, max, separation, landingJitter, candidates);
            var leftPoints = new List<Vector2>();
            var rightPoints = new List<Vector2>();
            foreach (float x in candidates)
                AddLandingPoint(motor, x, origin.y, x < origin.x ? leftPoints : rightPoints);
            int count = Mathf.Min(mineCount, leftPoints.Count + rightPoints.Count);
            int leftCount = Mathf.Min(count / 2, leftPoints.Count);
            int rightCount = Mathf.Min(count - leftCount, rightPoints.Count);
            leftCount = Mathf.Min(count - rightCount, leftPoints.Count);
            AddSelectedLandings(leftPoints, leftCount, sorted);
            AddSelectedLandings(rightPoints, rightCount, sorted);
        }
        // 바깥부터 좌우를 번갈아 쏴 시간상으로도 한쪽에만 몰리지 않게 한다.
        for (int lo = 0, hi = sorted.Count - 1; lo <= hi; lo++, hi--)
        {
            results.Add(sorted[lo]);
            if (lo != hi) results.Add(sorted[hi]);
        }
    }

    private static void AddLandingPoint(RaycastMotor2D motor, float x, float y, List<Vector2> points)
    {
        var floor = motor.RaycastEnvironment(new Vector2(x, y), Vector2.down, 80);
        if (floor.collider != null) points.Add(floor.point);
    }

    private static void AddSelectedLandings(List<Vector2> candidates, int count, List<Vector2> points)
    {
        for (int i = 0; i < count; i++)
            points.Add(candidates[Mathf.FloorToInt((i + .5f) * candidates.Count / count)]);
    }

    private static void AddAvailableSlots(float min, float max, float separation, float landingJitter, List<float> slots)
    {
        if (max < min) return;
        int count = Mathf.FloorToInt((max - min) / separation) + 1;
        float span = (count - 1) * separation;
        float slack = Mathf.Max(0, (max - min - span) * .5f);
        float jitter = Mathf.Min(slack, landingJitter * separation);
        float start = (min + max - span) * .5f + Random.Range(-jitter, jitter);
        for (int i = 0; i < count; i++) slots.Add(start + i * separation);
    }
}
