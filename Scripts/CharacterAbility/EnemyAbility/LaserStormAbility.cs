using System.Collections.Generic;
using UnityEngine;

public class LaserStormAbility : AiAbility
{
    [Header("References")]
    [SerializeField] private Renderer[] casterRenderers;
    [SerializeField] private Transform[] firePoints = new Transform[8];

    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    private EnemyTeleporter teleporter;
    private Transform playerTr;
    private Material[] casterMaterials;

    private bool teleportTriggered;
    private bool teleportFinished;
    private Vector2 teleportTarget;

    private bool lasersFired;

    private readonly List<LaserBeam> activeLasers = new();
    private readonly List<LaserPreviewLine> activePreviews = new();

    private LaserStormData StormData => (LaserStormData)data;

    private void Awake()
    {
        casterMaterials = new Material[casterRenderers.Length];

        for (int i = 0; i < casterRenderers.Length; i++)
        {
            if (casterRenderers[i] != null)
                casterMaterials[i] = casterRenderers[i].material;
        }

        SetCasterEmission(StormData.idleEmissionColor);
    }

    protected override void OnWindupStart()
    {
        teleporter = GetComponent<EnemyTeleporter>();
        playerTr = GameManager.Instance.Player.transform;

        teleportTriggered = false;
        teleportFinished = false;
        teleportTarget = Vector2.zero;

        lasersFired = false;

        controller.Velocity = Vector2.zero;

        ReturnAllPreviews();
        ReturnAllLasers();
        SetCasterEmission(StormData.idleEmissionColor);
    }

    protected override void OnWindup(float t)
    {
        if (!teleportTriggered && t < StormData.teleportTriggerTime)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        if (!teleportTriggered && t >= StormData.teleportTriggerTime)
        {
            teleportTriggered = true;
            controller.Velocity = Vector2.zero;

            teleportTarget = GetTeleportTarget();

            teleporter.TeleportSequence(
                teleportTarget,
                feedbackPlayer,
                StormData.teleportStartFx,
                StormData.teleportEndFx,
                StormData.teleportDisappearDelay,
                StormData.teleportAppearDelay,
                () =>
                {
                    teleportFinished = true;
                    controller.Velocity = Vector2.zero;
                    FacePlayer();
                });

            return;
        }

        if (!teleportFinished)
        {
            controller.Velocity = Vector2.zero;
            return;
        }

        controller.Velocity = Vector2.zero;

        float normalized = Mathf.InverseLerp(StormData.teleportTriggerTime, data.windupTime, t);
        Color color = Color.Lerp(StormData.idleEmissionColor, StormData.chargeEmissionColor, normalized);
        SetCasterEmission(color);
    }

    protected override void OnExecuteStart()
    {
        controller.Velocity = Vector2.zero;
        lasersFired = false;

        ReturnAllPreviews();
        ReturnAllLasers();

        SpawnAllPreviews();
        SetCasterEmission(StormData.chargeEmissionColor);
    }

    protected override void OnExecute(float t)
    {
        controller.Velocity = Vector2.zero;

        float previewDuration = Mathf.Min(StormData.previewDuration, data.executeTime);
        float remainingPreview = Mathf.Max(0f, previewDuration - t);

        if (!lasersFired)
        {
            UpdateAllPreviews(remainingPreview, Mathf.Max(0.0001f, previewDuration));

            if (t >= previewDuration)
            {
                ReturnAllPreviews();
                FireAllLasers();
                lasersFired = true;
                SetCasterEmission(StormData.fireEmissionColor);
            }
            else
            {
                SetCasterEmission(StormData.chargeEmissionColor);
            }

            return;
        }

        SetCasterEmission(StormData.fireEmissionColor);
    }

    protected override void OnRecoverStart()
    {
        controller.Velocity = Vector2.zero;
        ReturnAllPreviews();
        ReturnAllLasers();
        SetCasterEmission(StormData.idleEmissionColor);
    }

    protected override void OnRecover(float t)
    {
        controller.Velocity = Vector2.zero;
    }

    protected override void OnStop()
    {
        controller.Velocity = Vector2.zero;
        ReturnAllPreviews();
        ReturnAllLasers();
        SetCasterEmission(StormData.idleEmissionColor);
    }

    private void SpawnAllPreviews()
    {
        if (StormData.previewLinePrefab == null)
            return;

        float previewDuration = Mathf.Min(StormData.previewDuration, data.executeTime);

        for (int i = 0; i < firePoints.Length; i++)
        {
            Transform point = firePoints[i];

            if (point == null)
                continue;

            SpawnPreview(point.position, point.up, previewDuration);
            SpawnPreview(point.position, -point.up, previewDuration);
        }
    }

    private void SpawnPreview(Vector2 origin, Vector2 direction, float previewDuration)
    {
        LaserPreviewLine preview = WorldEntityPool.Instance.Spawn(
            StormData.previewLinePrefab,
            origin,
            Quaternion.identity);

        preview.Show(
            origin,
            direction.normalized,
            StormData.laserLength,
            StormData.previewLineWidth,
            StormData.laserBlockMask,
            StormData.previewLineColor,
            previewDuration);

        activePreviews.Add(preview);
    }

    private void UpdateAllPreviews(float remaining, float total)
    {
        int previewIndex = 0;

        for (int i = 0; i < firePoints.Length; i++)
        {
            Transform point = firePoints[i];

            if (point == null)
                continue;

            Vector2 upDir = point.up.normalized;
            Vector2 downDir = -upDir;

            if (previewIndex < activePreviews.Count && activePreviews[previewIndex] != null)
            {
                activePreviews[previewIndex].SetOrigin(point.position);
                activePreviews[previewIndex].SetDirection(upDir);
                activePreviews[previewIndex].UpdatePreviewVisual(remaining, total);
            }
            previewIndex++;

            if (previewIndex < activePreviews.Count && activePreviews[previewIndex] != null)
            {
                activePreviews[previewIndex].SetOrigin(point.position);
                activePreviews[previewIndex].SetDirection(downDir);
                activePreviews[previewIndex].UpdatePreviewVisual(remaining, total);
            }
            previewIndex++;
        }
    }

    private void FireAllLasers()
    {
        ReturnAllLasers();

        if (StormData.laserPrefab == null)
            return;

        float previewDuration = Mathf.Min(StormData.previewDuration, data.executeTime);
        float laserDuration = Mathf.Max(0.01f, data.executeTime - previewDuration);

        for (int i = 0; i < firePoints.Length; i++)
        {
            Transform point = firePoints[i];

            if (point == null)
                continue;

            Vector2 upDir = point.up.normalized;
            Vector2 downDir = -upDir;

            SpawnLaser(point.position, upDir, laserDuration);
            SpawnLaser(point.position, downDir, laserDuration);
        }
    }

    private void SpawnLaser(Vector2 origin, Vector2 direction, float duration)
    {
        LaserBeam laser = WorldEntityPool.Instance.Spawn(
            StormData.laserPrefab,
            origin,
            Quaternion.identity);

        laser.Fire(
            StormData.laserDamageInfo,
            origin,
            direction.normalized,
            StormData.laserLength,
            duration,
            StormData.laserBlockMask,
            StormData.laserDamageMask,
            StormData.laserWidth,
            controller);

        activeLasers.Add(laser);
    }

    private void ReturnAllPreviews()
    {
        for (int i = 0; i < activePreviews.Count; i++)
        {
            if (activePreviews[i] != null)
                activePreviews[i].ForceReturnToPool();
        }

        activePreviews.Clear();
    }

    private void ReturnAllLasers()
    {
        for (int i = 0; i < activeLasers.Count; i++)
        {
            if (activeLasers[i] != null)
                activeLasers[i].ForceReturnToPool();
        }

        activeLasers.Clear();
    }

    private Vector2 GetTeleportTarget()
    {
        if (playerTr == null)
            return controller.transform.position;

        float targetX = playerTr.position.x + StormData.teleportOffset.x;
        float groundY = GetGroundY(targetX);

        return new Vector2(
            targetX,
            groundY + StormData.teleportOffset.y);
    }

    private float GetGroundY(float x)
    {
        if (playerTr == null)
            return controller.transform.position.y;

        Vector2 origin = new Vector2(x, playerTr.position.y + StormData.groundCheckStartHeight);

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            Vector2.down,
            StormData.groundCheckDistance,
            StormData.teleportBlockMask);

        if (hit.collider != null)
            return hit.point.y;

        return playerTr.position.y;
    }

    private void FacePlayer()
    {
        if (playerTr == null)
            return;

        float deltaX = playerTr.position.x - controller.transform.position.x;

        if (Mathf.Abs(deltaX) <= StormData.faceDeadZone)
            return;

        controller.FacingUpdate(deltaX > 0f ? 1 : -1);
    }

    private void SetCasterEmission(Color color)
    {
        if (casterMaterials == null)
            return;

        for (int i = 0; i < casterMaterials.Length; i++)
        {
            Material mat = casterMaterials[i];

            if (mat != null && mat.HasProperty(EmissionColorID))
                mat.SetColor(EmissionColorID, color);
        }
    }
}