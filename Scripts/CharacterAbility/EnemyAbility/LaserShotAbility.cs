using UnityEngine;

public class LaserShotAbility : AiAbility
{
    [Header("References")]
    [SerializeField] private Renderer[] casterRenderers;

    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    private EnemyTeleporter teleporter;
    private Transform playerTr;
    private Material[] casterMaterials;

    private bool teleportTriggered;
    private bool teleportFinished;
    private bool teleportedToRightSide;
    private Vector2 teleportTarget;

    private LaserPreviewLine activePreview;
    private LaserBeam activeLaser;

    private LaserShotData LaserData => (LaserShotData)data;

    private void Awake()
    {
        casterMaterials = new Material[casterRenderers.Length];
        for (int i = 0; i < casterRenderers.Length; i++)
        {
            casterMaterials[i] = casterRenderers[i].material;
        }

        SetCasterEmission(LaserData.idleEmissionColor);
    }

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        playerTr = GameManager.Instance.Player.transform;

        teleportTriggered = false;
        teleportFinished = false;

        activeLaser = null;
        activePreview = null;

        controller.Velocity = Vector2.zero;
        SetCasterEmission(LaserData.idleEmissionColor);
    }

    protected override void OnWindup(float t)
    {
        if (!teleportTriggered && t < LaserData.teleportTriggerTime)
            return;

        if (!teleportTriggered && t >= LaserData.teleportTriggerTime)
        {
            teleportTriggered = true;

            teleportTarget = GetTeleportTarget(playerTr.position, controller.transform.position, out bool targetIsRightSide);
            teleportedToRightSide = targetIsRightSide;

            teleporter.TeleportSequence(
                teleportTarget,
                feedbackPlayer,
                LaserData.teleportStartFx,
                LaserData.teleportEndFx,
                LaserData.teleportDisappearDelay,
                LaserData.teleportAppearDelay,
                () =>
                {
                    teleportFinished = true;
                    controller.Velocity = Vector2.zero;
                    controller.FacingUpdate(teleportedToRightSide ? -1 : 1);
                    SpawnPreview();
                    UpdatePreview();
                });

            return;
        }

        if (!teleportFinished)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        FollowPlayerOffset();

        float normalized = Mathf.InverseLerp(LaserData.teleportTriggerTime, data.windupTime, t);
        Color color = Color.Lerp(LaserData.idleEmissionColor, LaserData.chargeEmissionColor, normalized);

        float remaining = data.windupTime - t;
        UpdatePreview();
        activePreview?.UpdatePreviewVisual(remaining, data.windupTime);

        SetCasterEmission(color);
    }

    protected override void OnExecuteStart()
    {
        FollowPlayerOffset();
        FireLaser();
        UpdateActiveLaser();
        ReturnPreview();
    }
    protected override void OnExecute(float t)
    {
        FollowPlayerOffset();
        UpdateActiveLaser();
        SetCasterEmission(LaserData.fireEmissionColor);
    }
    protected override void OnRecoverStart()
    {
        controller.Velocity = Vector2.zero;
        SetCasterEmission(LaserData.idleEmissionColor);

        ReturnPreview();
    }
    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;

        if (activeLaser != null)
            activeLaser.ForceReturnToPool();

        ReturnPreview();
        SetCasterEmission(LaserData.idleEmissionColor);
    }
    private void SpawnPreview()
    {
        if (LaserData.previewLinePrefab == null)
            return;

        if (activePreview != null)
            return;

        activePreview = WorldEntityPool.Instance.Spawn(
            LaserData.previewLinePrefab,
            executePoint.position,
            Quaternion.identity);

        activePreview.Show(
            executePoint.position,
            GetLaserDirection(),
            LaserData.laserLength,
            LaserData.previewLineWidth,
            LaserData.laserBlockMask,
            LaserData.previewLineColor,
            data.windupTime);
    }

    private void UpdatePreview()
    {
        if (activePreview == null)
            return;

        activePreview.SetOrigin(executePoint.position);
        activePreview.SetDirection(GetLaserDirection());
    }
    private Vector2 GetLaserDirection()
    {
        return teleportedToRightSide
            ? new Vector2(-1f, -1f).normalized
            : new Vector2(1f, -1f).normalized;
    }
    private void ReturnPreview()
    {
        if (activePreview == null)
            return;

        activePreview.ForceReturnToPool();
        activePreview = null;
    }
    private void FollowPlayerOffset()
    {
        if (playerTr == null)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        Vector2 desiredOffset = GetFollowOffset();
        Vector2 desiredPos = (Vector2)playerTr.position + desiredOffset;
        Vector2 currentPos = controller.transform.position;
        Vector2 delta = desiredPos - currentPos;

        float vx = 0f;
        float vy = 0f;

        if (Mathf.Abs(delta.x) > LaserData.followDeadZone)
            vx = Mathf.Sign(delta.x) * LaserData.followSpeed;

        if (Mathf.Abs(delta.y) > LaserData.followDeadZone)
            vy = Mathf.Sign(delta.y) * LaserData.followSpeed;

        controller.Velocity = new Vector2(vx, vy);
    }

    private Vector2 GetFollowOffset()
    {
        float x = Mathf.Abs(LaserData.teleportOffset.x);
        float y = LaserData.teleportOffset.y;

        return teleportedToRightSide
            ? new Vector2(x, y)
            : new Vector2(-x, y);
    }

    private void UpdateActiveLaser()
    {
        if (activeLaser == null)
            return;

        activeLaser.SetOrigin(executePoint.position);
    }
    private Vector2 GetTeleportTarget(Vector2 playerPos, Vector2 enemyPos, out bool targetIsRightSide)
    {
        bool enemyIsLeftOfPlayer = enemyPos.x < playerPos.x;

        float rightX = playerPos.x + LaserData.teleportOffset.x;
        float leftX = playerPos.x - LaserData.teleportOffset.x;

        Vector2 rightTarget = new Vector2(
            rightX,
            GetGroundY(rightX) + LaserData.teleportOffset.y);

        Vector2 leftTarget = new Vector2(
            leftX,
            GetGroundY(leftX) + LaserData.teleportOffset.y);

        if (enemyIsLeftOfPlayer)
        {
            if (!IsBlocked(playerPos, rightTarget))
            {
                targetIsRightSide = true;
                return rightTarget;
            }

            targetIsRightSide = false;
            return leftTarget;
        }
        else
        {
            if (!IsBlocked(playerPos, leftTarget))
            {
                targetIsRightSide = false;
                return leftTarget;
            }

            targetIsRightSide = true;
            return rightTarget;
        }
    }
    private float GetGroundY(float x)
    {
        Vector2 origin = new Vector2(x, playerTr.position.y + 5f);

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            Vector2.down,
            50f,
            LaserData.teleportBlockMask);

        if (hit.collider != null)
            return hit.point.y;

        return playerTr.position.y;
    }
    private bool IsBlocked(Vector2 from, Vector2 to)
    {
        RaycastHit2D hit = Physics2D.Linecast(from, to, LaserData.teleportBlockMask);
        return hit.collider != null;
    }

    private void FireLaser()
    {
        if (LaserData.laserPrefab == null)
            return;

        Vector2 direction = GetLaserDirection();

        activeLaser = WorldEntityPool.Instance.Spawn(LaserData.laserPrefab, executePoint.position, Quaternion.identity);
        activeLaser.Fire(
            LaserData.laserDamageInfo,
            executePoint.position,
            direction,
            LaserData.laserLength,
            data.executeTime,
            LaserData.laserBlockMask,
            LaserData.laserDamageMask,
            LaserData.laserWidth,
            controller);
    }

    private void SetCasterEmission(Color color)
    {
        if (casterRenderers == null)
            return;

        for (int i = 0; i < casterMaterials.Length; i++)
        {
            casterMaterials[i].SetColor(EmissionColorID, color);
        }
    }
}
