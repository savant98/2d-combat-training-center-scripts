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
        // 착지점 계산은 Nightmare의 베기 지뢰와 함께 쓰는 MineLandingPlanner가 한다.
        if (mineData.distributeLandingPoints && minePrefab is LandMine)
            MineLandingPlanner.Plan(controller.Motor, (Vector2)transform.position + Vector2.up * .1f, mineCount,
                mineData.scatterRadius, mineData.minimumSeparation, mineData.landingJitter, landingPoints);
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
}
