using Unity.Cinemachine;
using UnityEngine;
using System.Collections;
using DG.Tweening;

public class Room : MonoBehaviour
{
    [Header("Room Components")]
    public RoomType roomType;
    [SerializeField] private GameObject rootObject;
    [SerializeField] private WaveManager waveManager;
    [SerializeField] private RoomTrigger clearPortalPrefab;

    [Header("Camera")]
    [SerializeField] private CinemachineCamera virtualCamera;   // 해당 방이 담당하는 카메라

    [Header("Effects")]
    [SerializeField] private FeedbackDataSO spawnPortalFeedback;

    public WaveManager WaveManager => waveManager;
    public CinemachineCamera VirtualCamera => virtualCamera;
    public GameObject ContentRoot => rootObject;
    public bool SuppressAutomaticWaves { get; set; }
    public Vector3 SpawnPoint => currentSpawnPoint;

    private float defaultOrthographicSize = 7f;
    private float waitForBossEntryTime = 3.5f;
    private Tween bossEntryCameraTween;
    private bool ownsBossWarning;
    private uint bossEntryVersion;
    private Coroutine bossPhaseRoutine;
    private Transform phasePreviousFollow;
    private Transform phaseFocus;
    private Material phaseWarningMaterial;
    private Vector3 currentSpawnPoint;
    private Coroutine waveCompleteRoutine;
    public void ReleaseContent()
    {
        if (waveManager != null)
        {
            waveManager.OnAllWavesCleared -= HandleWavesCleared;
            waveManager.WavesReset -= CancelWaveComplete;
        }
        StopAllCoroutines();
        waveCompleteRoutine = null;
        ResetBossEntryCamera();
    }
    private void HandleWavesCleared()
    {
        CancelWaveComplete();
        waveCompleteRoutine = StartCoroutine(WaveCompleteRoutine());
    }
    // Wave가 다시 시작되거나 강제로 끝나면(재도전·로비 복귀) 진행 중이던 완료 연출을 멈춘다.
    private void CancelWaveComplete()
    {
        if (waveCompleteRoutine != null) StopCoroutine(waveCompleteRoutine);
        waveCompleteRoutine = null;
    }
    // 완료 연출 도중 플레이어가 패배(사망·훈련 성적 미흡)했으면 패배 연출만 진행한다.
    private static bool IsPlayerDefeated()
    {
        return GameManager.Instance != null && GameManager.Instance.Player != null && GameManager.Instance.Player.Health.IsDead;
    }
    public void BindContent(TrainingRoomContent content)
    {
        rootObject = content.gameObject;
        waveManager = content.waves;
        virtualCamera = content.roomCamera;
        clearPortalPrefab = content.clearPortal;
        defaultOrthographicSize = virtualCamera.Lens.OrthographicSize;
        virtualCamera.enabled = false;
        waveManager.OnAllWavesCleared += HandleWavesCleared;
        waveManager.WavesReset += CancelWaveComplete;
    }
    private void Awake()
    {
        if (GetComponent<RoomViewRules>() == null) gameObject.AddComponent<RoomViewRules>();
        var host = GetComponent<TrainingRoomHost>();
        if (host != null) { host.Load(host.defaultData); rootObject.SetActive(false); return; }
        defaultOrthographicSize = virtualCamera.Lens.OrthographicSize;
        virtualCamera.enabled = false;

        if (waveManager != null)
        {
            waveManager.OnAllWavesCleared += HandleWavesCleared;
            waveManager.WavesReset += CancelWaveComplete;
        }

        rootObject.SetActive(false);
    }
    public void ActivateRoom(Vector3 spawnPoint)
    {
        rootObject.SetActive(true);

        if (virtualCamera != null)
            virtualCamera.Priority = 20; // 활성화

        if (virtualCamera.Follow == null)
            virtualCamera.Follow = GameManager.Instance.Player.transform;

        if (roomType == RoomType.Training && !SuppressAutomaticWaves)
        {
            // 입장 로딩(Wave에 쓸 것을 미리 만들어 두는 단계)이 진행 중이면 끝난 뒤에 Wave를 시작한다.
            var host = GetComponent<TrainingRoomHost>();
            if (host != null && host.IsLoading) StartCoroutine(BeginWavesAfterLoading(host));
            else waveManager.BeginWaves(); // Wave 시작
        }

        virtualCamera.enabled = true;
        currentSpawnPoint = spawnPoint;

        defaultOrthographicSize = virtualCamera.Lens.OrthographicSize;
    }
    private IEnumerator BeginWavesAfterLoading(TrainingRoomHost host)
    {
        var content = rootObject;
        while (host != null && host.IsLoading) yield return null;
        // 로딩 도중 방을 떠났거나 내용이 바뀌었으면 시작하지 않는다.
        if (content == rootObject && rootObject.activeInHierarchy && RoomManager.Instance.CurrentRoom == this && !SuppressAutomaticWaves)
            waveManager.BeginWaves();
    }
    public IEnumerator ActivateBossEntryCamera(Transform bossTr)
    {
        ResetBossEntryCamera();
        uint entryVersion = bossEntryVersion;
        UIManager.Instance.ShowWarningLine();
        ownsBossWarning = true;

        virtualCamera.Follow = bossTr;
        bossEntryCameraTween = DOTween.To(() => virtualCamera.Lens.OrthographicSize,
            SetCameraSize,
            defaultOrthographicSize * 0.75f,
            0.3f);

        yield return new WaitForSeconds(waitForBossEntryTime);

        if (entryVersion != bossEntryVersion) yield break;

        if (bossTr == null || !bossTr.gameObject.activeInHierarchy || RoomManager.Instance.CurrentRoom != this)
        {
            ResetBossEntryCamera();
            yield break;
        }
        UIManager.Instance.HideWarningLine();
        ownsBossWarning = false;
        UIManager.Instance.ShowBossHP(true, bossTr.GetComponent<EnemyController>());
        virtualCamera.Follow = GameManager.Instance.Player.transform;
        bossEntryCameraTween = DOTween.To(() => virtualCamera.Lens.OrthographicSize,
            SetCameraSize,
            defaultOrthographicSize,
            0.3f);
    }
    public void PresentBossPhase(EnemyController boss)
    {
        if (ownsBossWarning || virtualCamera == null || !virtualCamera.isActiveAndEnabled
            || virtualCamera.Follow == null || RoomManager.Instance?.CurrentRoom != this) return;

        CancelBossPhase();
        bossPhaseRoutine = StartCoroutine(ShowBossPhase(boss, virtualCamera.Follow));
    }

    private IEnumerator ShowBossPhase(EnemyController boss, Transform previousFollow)
    {
        const float focusTime = 0.26f;
        const float holdTime = 0.36f;
        const float returnTime = 0.38f;
        var health = boss.Health;
        uint lifeVersion = health.LifeVersion;
        var hitbox = health.GetComponent<Collider2D>();
        phasePreviousFollow = previousFollow;
        phaseFocus = new GameObject("BossPhaseFocus").transform;
        phaseFocus.SetParent(transform, false);
        phaseFocus.position = previousFollow.position;
        virtualCamera.Follow = phaseFocus;

        Transform warning = new GameObject("BossPhaseWarning").transform;
        warning.SetParent(phaseFocus, false);
        var marks = CreatePhaseWarning(warning);
        var warningColor = new Color(1f, 0.32f, 0.16f);
        float elapsed = 0f;
        while (elapsed < focusTime + holdTime + returnTime && virtualCamera.Follow == phaseFocus
            && health != null && health.isActiveAndEnabled && health.currentHP > 0f && health.LifeVersion == lifeVersion
            && previousFollow != null)
        {
            float towardBoss = Mathf.SmoothStep(0f, 1f, elapsed / focusTime);
            float towardPlayer = Mathf.SmoothStep(0f, 1f, (elapsed - focusTime - holdTime) / returnTime);
            phaseFocus.position = Vector3.Lerp(previousFollow.position, boss.transform.position, towardBoss * (1f - towardPlayer));
            var bounds = hitbox != null ? hitbox.bounds : new Bounds(boss.transform.position, Vector3.one * 4f);
            warning.position = new Vector3(bounds.center.x, bounds.max.y + 0.48f, bounds.center.z);
            float pulse = Mathf.Sin(Mathf.PI * Mathf.Clamp01(elapsed / (focusTime + holdTime + 0.1f)));
            warning.localScale = Vector3.one * Mathf.Lerp(0.78f, 1.05f, towardBoss);
            warningColor.a = pulse;
            foreach (var mark in marks) mark.startColor = mark.endColor = warningColor;
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }

        bossPhaseRoutine = null;
        ReleaseBossPhase();
    }

    private LineRenderer[] CreatePhaseWarning(Transform marker)
    {
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) return System.Array.Empty<LineRenderer>();
        phaseWarningMaterial = new Material(shader);
        return new[]
        {
            CreateMark(marker, "Outline", 0.034f, true, new Vector3(0f, 0.36f), new Vector3(0.43f, -0.22f), new Vector3(-0.43f, -0.22f)),
            CreateMark(marker, "Stem", 0.059f, false, new Vector3(0f, 0.18f), new Vector3(0f, -0.012f)),
            CreateMark(marker, "Dot", 0.066f, false, new Vector3(0f, -0.124f), new Vector3(0f, -0.087f))
        };
    }

    private LineRenderer CreateMark(Transform marker, string name, float width, bool loop, params Vector3[] points)
    {
        var mark = new GameObject(name).AddComponent<LineRenderer>();
        mark.transform.SetParent(marker, false);
        mark.useWorldSpace = false;
        mark.sortingLayerName = "Particle";
        mark.sortingOrder = 40;
        mark.sharedMaterial = phaseWarningMaterial;
        mark.widthMultiplier = width;
        mark.numCornerVertices = 2;
        mark.numCapVertices = 3;
        mark.loop = loop;
        mark.positionCount = points.Length;
        mark.SetPositions(points);
        return mark;
    }

    private void CancelBossPhase()
    {
        if (bossPhaseRoutine != null) StopCoroutine(bossPhaseRoutine);
        bossPhaseRoutine = null;
        ReleaseBossPhase();
    }

    private void ReleaseBossPhase()
    {
        if (phaseFocus != null)
        {
            if (virtualCamera != null && virtualCamera.Follow == phaseFocus) virtualCamera.Follow = phasePreviousFollow;
            Destroy(phaseFocus.gameObject);
        }
        if (phaseWarningMaterial != null) Destroy(phaseWarningMaterial);
        phaseFocus = null;
        phasePreviousFollow = null;
        phaseWarningMaterial = null;
    }

    private void OnDisable() => CancelBossPhase();

    private void SetCameraSize(float size)
    {
        virtualCamera.Lens.OrthographicSize = size;
        virtualCamera.GetComponent<CinemachineConfiner2D>()?.InvalidateLensCache();
    }
    public void ResetBossEntryCamera()
    {
        CancelBossPhase();
        bossEntryVersion++;
        if (ownsBossWarning && UIManager.Instance != null) UIManager.Instance.HideWarningLine();
        ownsBossWarning = false;
        bossEntryCameraTween?.Kill();
        bossEntryCameraTween = null;
        if (virtualCamera == null) return;
        SetCameraSize(defaultOrthographicSize);
        if (GameManager.Instance != null && GameManager.Instance.Player != null)
            virtualCamera.Follow = GameManager.Instance.Player.transform;
    }

    public void DeactivateRoom()
    {
        ResetBossEntryCamera();
        if (virtualCamera != null)
            virtualCamera.Priority = 1; // 비활성화

        if (clearPortalPrefab != null)
            clearPortalPrefab.gameObject.SetActive(false);

        virtualCamera.enabled = false;
        rootObject.SetActive(false);
    }

    private IEnumerator WaveCompleteRoutine()
    {
        // Wave Clear이후 여유 시간
        yield return new WaitForSeconds(2f);
        if (IsPlayerDefeated()) yield break;
        UIManager.Instance.ShowBossHP(false);
        UIManager.Instance.StartTrainingCompletePanelEffect();

        yield return new WaitForSeconds(2f);
        if (IsPlayerDefeated()) yield break;

        if (TrainingRunTracker.Instance != null && TrainingRunTracker.Instance.LastRunResult.cleared)
        {
            UIManager.Instance.ShowCompleteScoreBoard(true, TrainingRunTracker.Instance.LastRunResult);

            yield return new WaitUntil(() => !UIManager.Instance.IsCompleteBoardOpen);
        }

        yield return new WaitForSeconds(1f);
        if (IsPlayerDefeated()) yield break;

        var terrain = waveManager != null ? waveManager.GetComponentInParent<WaveCellTerrain>(true) : null;
        WaveCompletionPortal.Apply(waveManager != null ? waveManager.Data : null, clearPortalPrefab,
            terrain != null ? terrain.transform : transform);
        FeedbackPlayer.Instance.Play(spawnPortalFeedback, clearPortalPrefab.transform);
        clearPortalPrefab.gameObject.SetActive(true);
    }


}
