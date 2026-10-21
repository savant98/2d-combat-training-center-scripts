using UnityEngine;
using static WeaponBase;

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
    private LandMineData mineData;

    private void Awake()
    {
        mineData = (LandMineData)data;
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
    }
    protected override void OnExecute(float t)
    {
        if (currentCount >= mineCount + 1 || Time.time < lastTimeShot + scatterInterval)
            return;

        // 발사체 발사
        var projectile = WorldEntityPool.Instance.Spawn(minePrefab, controller.transform.position, controller.transform.rotation);
        projectile.Initialize(controller, damageInfo, launchSpeed, currentCount, platformLayer);

        currentCount++;
        lastTimeShot = Time.time;
    }
}
