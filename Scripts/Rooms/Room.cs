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
        if (virtualCamera != null) ResetBossEntryCamera();
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
            waveManager.BeginWaves(); // Wave 시작
        }

        virtualCamera.enabled = true;
        currentSpawnPoint = spawnPoint;

        defaultOrthographicSize = virtualCamera.Lens.OrthographicSize;
    }
    public IEnumerator ActivateBossEntryCamera(Transform bossTr)
    {
        ResetBossEntryCamera();
        UIManager.Instance.ShowWarningLine();

        virtualCamera.Follow = bossTr;
        bossEntryCameraTween = DOTween.To(() => virtualCamera.Lens.OrthographicSize,
            SetCameraSize,
            defaultOrthographicSize * 0.75f,
            0.3f);

        yield return new WaitForSeconds(waitForBossEntryTime);

        if (bossTr == null || !bossTr.gameObject.activeInHierarchy || RoomManager.Instance.CurrentRoom != this)
        {
            ResetBossEntryCamera();
            yield break;
        }
        UIManager.Instance.ShowBossHP(true);
        virtualCamera.Follow = GameManager.Instance.Player.transform;
        bossEntryCameraTween = DOTween.To(() => virtualCamera.Lens.OrthographicSize,
            SetCameraSize,
            defaultOrthographicSize,
            0.3f);
    }
    private void SetCameraSize(float size)
    {
        virtualCamera.Lens.OrthographicSize = size;
        virtualCamera.GetComponent<CinemachineConfiner2D>()?.InvalidateLensCache();
    }
    public void ResetBossEntryCamera()
    {
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
