using UnityEngine;
using System.Collections.Generic;
using static WeaponBaseSO;

public class MineScatterAbility : AiAbility
{
    // Scatter Settings
    private ProjectileBase minePrefab;
    private int mineCount = 12;
    private float launchSpeed = 8f;
    private float scatterInterval = 0.2f;

    // Mine Settings
    private DamageInfo damageInfo;
    private LayerMask platformLayer;

    private float lastTimeShot = 999f;
    private int currentCount = 0;
    private LandMineDataSO mineData;
    private readonly List<Vector2> landingPoints = new List<Vector2>();
    public int PlannedMineCount => landingPoints.Count;
    public int FiredMineCount { get; private set; }
    public IReadOnlyList<Vector2> LandingPoints => landingPoints;

    private void Awake()
    {
        mineData = (LandMineDataSO)data;
        minePrefab = mineData.minePrefab;
        mineCount = mineData.mineCount;
        launchSpeed = mineData.launchSpeed;
        scatterInterval = mineData.scatterInterval;
        damageInfo = mineData.damageInfo;
        platformLayer = mineData.platformLayer;
    }

    protected override void OnExecuteStart()
    {
        lastTimeShot = Time.time;
        currentCount = 1;
        FiredMineCount = 0;
        landingPoints.Clear();
        if (mineData.distributeLandingPoints && minePrefab is LandMine) PlanLandingPoints();
    }
    protected override void OnExecute(float t)
    {
        int total = mineData.distributeLandingPoints && minePrefab is LandMine ? landingPoints.Count : mineCount;
        if (currentCount >= total + 1 || Time.time < lastTimeShot + scatterInterval)
            return;

        // 발사체 발사
        var projectile = WorldEntityPool.Instance.Spawn(minePrefab, controller.transform.position, controller.transform.rotation);
        if (mineData.distributeLandingPoints && projectile is LandMine mine)
            mine.InitializeTowardGround(controller, damageInfo, landingPoints[currentCount - 1], mineData.scatterFlightTime, currentCount, platformLayer);
        else projectile.Initialize(controller, damageInfo, launchSpeed, currentCount, platformLayer);
        FiredMineCount++;

        currentCount++;
        lastTimeShot = Time.time;
    }

    private void PlanLandingPoints()
    {
        RaycastMotor2D.InvalidateAttackQuerySync(); RaycastMotor2D.PrepareAttackQueries();
        Vector2 origin = (Vector2)transform.position + Vector2.up * .1f;
        var left = controller.Motor.RaycastEnvironment(origin, Vector2.left, 100);
        var right = controller.Motor.RaycastEnvironment(origin, Vector2.right, 100);
        float min = origin.x - mineData.scatterRadius, max = origin.x + mineData.scatterRadius;
        if (left.collider != null) min = Mathf.Max(min, left.point.x + .8f);
        if (right.collider != null) max = Mathf.Min(max, right.point.x - .8f);
        if (max <= min || mineCount <= 0) return;
        var occupied = new List<float>();
        foreach (var mine in Object.FindObjectsByType<LandMine>(FindObjectsSortMode.None))
            if (mine.IsFlying && mine.HasLandingTarget) occupied.Add(mine.PlannedLandingPoint.x);
            else if (!mine.IsFlying) occupied.Add(mine.transform.position.x);
        float separation = Mathf.Max(.1f, mineData.minimumSeparation);
        occupied.RemoveAll(x => x < min - separation || x > max + separation);
        occupied.Sort();
        var sorted = new List<Vector2>();
        if (occupied.Count == 0)
        {
            int count = Mathf.Min(mineCount, Mathf.Max(1, Mathf.FloorToInt((max - min) / separation)));
            float width = (max - min) / count;
            // 다음 살포도 사이에 들어올 수 있도록 첫 살포의 난수 폭을 제한한다.
            float jitter = Mathf.Min(mineData.landingJitter * width,
                Mathf.Max(0, (width - 2 * separation) * .5f - .01f));
            for (int i = 0; i < count; i++)
                AddLandingPoint(min + (i + .5f) * width + Random.Range(-jitter, jitter), origin.y, sorted);
        }
        else
        {
            // 이미 놓인 지뢰 사이의 빈 구간 전체에서 안전한 후보를 먼저 만든다.
            var candidates = new List<float>();
            float start = min;
            foreach (float position in occupied)
            {
                AddAvailableSlots(start, Mathf.Min(max, position - separation), separation, candidates);
                start = Mathf.Max(start, position + separation);
            }
            AddAvailableSlots(start, max, separation, candidates);
            var leftPoints = new List<Vector2>();
            var rightPoints = new List<Vector2>();
            foreach (float x in candidates)
                AddLandingPoint(x, origin.y, x < origin.x ? leftPoints : rightPoints);
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
            landingPoints.Add(sorted[lo]);
            if (lo != hi) landingPoints.Add(sorted[hi]);
        }
    }

    private void AddLandingPoint(float x, float y, List<Vector2> points)
    {
        var floor = controller.Motor.RaycastEnvironment(new Vector2(x, y), Vector2.down, 80);
        if (floor.collider != null) points.Add(floor.point);
    }

    private static void AddSelectedLandings(List<Vector2> candidates, int count, List<Vector2> points)
    {
        for (int i = 0; i < count; i++)
            points.Add(candidates[Mathf.FloorToInt((i + .5f) * candidates.Count / count)]);
    }

    private void AddAvailableSlots(float min, float max, float separation, List<float> slots)
    {
        if (max < min) return;
        int count = Mathf.FloorToInt((max - min) / separation) + 1;
        float span = (count - 1) * separation;
        float slack = Mathf.Max(0, (max - min - span) * .5f);
        float jitter = Mathf.Min(slack, mineData.landingJitter * separation);
        float start = (min + max - span) * .5f + Random.Range(-jitter, jitter);
        for (int i = 0; i < count; i++) slots.Add(start + i * separation);
    }
}
