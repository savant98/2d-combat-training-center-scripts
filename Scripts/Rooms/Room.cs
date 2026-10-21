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
    [SerializeField] private FeedbackData spawnPortalFeedback;

    public WaveManager WaveManager => waveManager;
    public Vector3 SpawnPoint => currentSpawnPoint;

    private float defaultOrthographicSize = 7f;
    private float waitForBossEntryTime = 3.5f;
    private Vector3 currentSpawnPoint;
    private void Awake()
    {
        virtualCamera.enabled = false;

        if (waveManager != null)
            waveManager.OnAllWavesCleared += () => { StartCoroutine(WaveCompleteRoutine()); };

        rootObject.SetActive(false);
    }
    public void ActivateRoom(Vector3 spawnPoint)
    {
        rootObject.SetActive(true);

        if (virtualCamera != null)
            virtualCamera.Priority = 20; // 활성화

        if (virtualCamera.Follow == null)
            virtualCamera.Follow = GameManager.Instance.Player.transform;

        if (roomType == RoomType.Training)
        {
            waveManager.BeginWaves(); // Wave 시작
        }

        virtualCamera.enabled = true;
        currentSpawnPoint = spawnPoint;

        defaultOrthographicSize = virtualCamera.Lens.OrthographicSize;
    }
    public IEnumerator ActivateBossEntryCamera(Transform bossTr)
    {
        UIManager.Instance.ShowWarningLine();

        virtualCamera.Follow = bossTr;
        DOTween.To(() => virtualCamera.Lens.OrthographicSize,
            x => virtualCamera.Lens.OrthographicSize = x,
            defaultOrthographicSize * 0.75f,
            0.3f);

        yield return new WaitForSeconds(waitForBossEntryTime);

        UIManager.Instance.ShowBossHP(true);
        virtualCamera.Follow = GameManager.Instance.Player.transform;
        DOTween.To(() => virtualCamera.Lens.OrthographicSize,
            x => virtualCamera.Lens.OrthographicSize = x,
            defaultOrthographicSize,
            0.3f);
    }
    public void DeactivateRoom()
    {
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
        UIManager.Instance.ShowBossHP(false);
        UIManager.Instance.StartTrainingCompletePanelEffect();

        yield return new WaitForSeconds(2f);

        if (TrainingRunTracker.Instance != null && TrainingRunTracker.Instance.LastRunResult.cleared)
        {
            UIManager.Instance.ShowCompleteScoreBoard(true, TrainingRunTracker.Instance.LastRunResult);

            yield return new WaitUntil(() => !UIManager.Instance.IsCompleteBoardOpen);
        }

        yield return new WaitForSeconds(1f);

        FeedbackPlayer.Instance.Play(spawnPortalFeedback, clearPortalPrefab.transform);
        clearPortalPrefab.gameObject.SetActive(true);
    }


}
