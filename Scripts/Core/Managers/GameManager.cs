using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Level")]
    [SerializeField] private GameObject playerObject;
    [SerializeField] private GameObject spawnPoint;
    [SerializeField] private Room StartRoom;

    [Header("RoomTransition")]
    [SerializeField] private RoomTransitionEffectSO firstSpawnTransitionEffect;
    [SerializeField] private RoomTransitionEffectSO retryTransitionEffect;
    [SerializeField] private RoomTransitionEffectSO deathTransitionEffect;

    [HideInInspector] public PlayerController Player;
    public static GameManager Instance { get; private set; }
    private void OnDrawGizmos()
    {
        if (spawnPoint == null)
            return;

        Gizmos.color = Color.green;

        Gizmos.DrawWireSphere(spawnPoint.transform.position, 0.5f);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (spawnPoint == null)
            return;

        Player = Instantiate(playerObject, spawnPoint.transform.position, Quaternion.identity).GetComponent<PlayerController>();
    }
    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
    private void Start()
    {
        if (Player == null)
            return;

        Player.Health.OnPlayerDeath += HandlePlayerDeath;

        // spawn Transition
        RoomTransitionManager.Instance.RequestTransition(
            new RoomTransitionRequest
            {
                targetRoom = StartRoom,
                teleportPoint = spawnPoint.transform,
                transitionEffect = firstSpawnTransitionEffect
            });
    }
    private void HandlePlayerDeath()
    {
        StartCoroutine(PlayerDeathRoutine());
    }
    private IEnumerator PlayerDeathRoutine()
    {
        // 입력 차단
        UIManager.Instance.PushGameplayBlock();

        // Death Transition 실행
        yield return deathTransitionEffect.Play(new RoomTransitionContext { /* room 이동하지 않음 */});

        // Death UI 표시
        UIManager.Instance.ShowDeathUI(true);
    }
    public void RequestReturnToLobby()
    {
        // Wave 정리
        RoomManager.Instance.CurrentRoom.WaveManager?.ForceClear();
        StartCoroutine(EnemyDespawnNextFrame());

        UIManager.Instance.ShowDeathUI(false);
        Player.Health.ResetHealth();

        RoomTransitionManager.Instance.RequestTransition(
            new RoomTransitionRequest
            {
                targetRoom = StartRoom,
                teleportPoint = spawnPoint.transform,
                transitionEffect = firstSpawnTransitionEffect
            });
    }
    public void RequestRetry()
    {
        StartCoroutine(RequestRetryRoutine());
    }
    private IEnumerator RequestRetryRoutine()
    {
        UIManager.Instance.ShowDeathUI(false);
        // Enemy Despawn

        RoomManager.Instance.CurrentRoom.WaveManager?.ForceClear();
        StartCoroutine(EnemyDespawnNextFrame());

        yield return retryTransitionEffect.Play(new RoomTransitionContext());

        // 여유 시간
        yield return new WaitForSeconds(0.25f);

        UIManager.Instance.PopGameplayBlock();

        // Wave 재시작
        var currentRoom = RoomManager.Instance.CurrentRoom;
        if (currentRoom.roomType == RoomType.SelfTraining)
        {
            var practice = currentRoom.GetComponentInChildren<SelfTrainingController>();
            if (practice != null)
            {
                practice.CancelAnalysis();
                practice.Configure(practice.DummyCount, practice.UseShield, practice.ShieldCapacity);
            }
        }
        else currentRoom.WaveManager.RestartWaves();
    }
    private IEnumerator EnemyDespawnNextFrame()
    {
        EnemyPool.Instance.FreezeAll();
        yield return null;
        EnemyPool.Instance.DespawnAll();
    }
}