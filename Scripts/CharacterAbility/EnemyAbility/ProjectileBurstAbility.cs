using UnityEngine;

public class ProjectileBurstAbility : AiAbility
{
    private ProjectileBurstData burstData;

    private Transform playerTr;

    private int firedCount;
    private float shotTimer;

    private void Awake()
    {
        burstData = (ProjectileBurstData)data;
    }

    protected override void OnWindupStart()
    {
        playerTr = GameManager.Instance.Player.transform;

        if (burstData.facePlayerOnWindup)
            FacePlayer();

        controller.Velocity = Vector2.zero;
    }

    protected override void OnExecuteStart()
    {
        firedCount = 0;
        shotTimer = burstData.interval; // 시작하자마자 1발 쏘기 위함

        controller.Velocity = Vector2.zero;
    }

    protected override void OnExecute(float t)
    {
        controller.Velocity = Vector2.zero;

        if (firedCount >= burstData.count)
            return;

        shotTimer += Time.deltaTime;

        if (shotTimer < burstData.interval)
            return;

        shotTimer = 0f;

        if (burstData.facePlayerBeforeEachShot)
            FacePlayer();

        FireProjectile();
        firedCount++;
    }

    protected override void OnRecoverStart()
    {
        controller.Velocity = Vector2.zero;
    }

    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;
    }

    private void FireProjectile()
    {
        if (burstData.projectilePrefab == null)
            return;

        Transform spawnPoint =
            burstData.useExecutePoint && executePoint != null
                ? executePoint
                : controller.transform;

        ProjectileBase projectile = WorldEntityPool.Instance.Spawn(
            burstData.projectilePrefab,
            spawnPoint.position,
            spawnPoint.rotation);

        projectile.Initialize(
            controller,
            burstData.damageInfo,
            burstData.projectileSpeed,
            burstData.count,
            burstData.collideLayer);
    }

    private void FacePlayer()
    {
        if (playerTr == null)
            return;

        float dx = playerTr.position.x - controller.transform.position.x;

        if (Mathf.Abs(dx) <= 0.001f)
            return;

        controller.FacingUpdate(dx > 0f ? 1 : -1);
    }
}