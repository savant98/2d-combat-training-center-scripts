using System;
using UnityEngine;

public class TrainingRunTracker : MonoBehaviour
{
    public static TrainingRunTracker Instance { get; private set; }

    private TrainingDefinitionSO training;

    public int CurrentHItCount => hitCount;
    public TrainingDefinitionSO TrainingDefinition => training;

    public float CurrentTimeSeconds
    {
        get
        {
            if (ended)
                return lastTimeSeconds;
            return Time.time - startTime;
        }
    }

    public Grade CurrentPredictedGrade => training.Evaluate(hitCount, Time.time - startTime);

    // [추가] Room 등에서 연출 시 가져갈 최근 실행 결과
    public RunResult LastRunResult { get; private set; }

    public event Action<RunResult> OnRunEnded;

    private PlayerHealth playerHealth;
    private float startTime;
    private float lastTimeSeconds;
    private int hitCount;
    private bool ended;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void Begin(TrainingDefinitionSO trainingDefinition)
    {
        CleanupSubscriptions();

        this.training = trainingDefinition;
        hitCount = 0;
        ended = false;
        startTime = Time.time;

        if (playerHealth == null)
            playerHealth = GameManager.Instance.Player.Health;

        playerHealth.OnPlayerDeath += HandlePlayerDeath;
        playerHealth.OnDamaged += HandlePlayerDamaged;

        if (WaveManager.ActiveInstance != null)
            WaveManager.ActiveInstance.OnAllWavesCleared += HandleClear;

        // Training 진행 중 체크 UI 켜기
        UIManager.Instance.ShowTrainingScoreBoard(true, this);
    }

    private void OnDisable()
    {
        CleanupSubscriptions();
    }

    private void CleanupSubscriptions()
    {
        if (playerHealth != null)
        {
            playerHealth.OnPlayerDeath -= HandlePlayerDeath;
            playerHealth.OnDamaged -= HandlePlayerDamaged;
        }

        if (WaveManager.ActiveInstance != null)
            WaveManager.ActiveInstance.OnAllWavesCleared -= HandleClear;
    }

    private void HandlePlayerDamaged(WeaponBase.DamageInfo damageInfo)
    {
        if (ended) return;
        hitCount++;
    }

    private void HandlePlayerDeath()
    {
        if (ended) return;
        EndRun(cleared: false);
    }

    private void HandleClear()
    {
        if (ended) return;
        EndRun(cleared: true);
    }

    private void EndRun(bool cleared)
    {
        ended = true;

        float timeSeconds = Time.time - startTime;
        lastTimeSeconds = timeSeconds;
        Grade grade = cleared ? training.Evaluate(hitCount, timeSeconds) : Grade.Poor;

        int rewardGold = 0;

        if (cleared)
        {
            rewardGold = training.GetRewardGoldByGrade(grade);
            DataManager.Instance.AddGold(rewardGold);
        }

        RunResult result = new RunResult()
        {
            trainingId = training.trainingId,
            hitCount = hitCount,
            timeSeconds = timeSeconds,
            grade = grade,
            cleared = cleared,
            endedAtIso = DateTime.UtcNow.ToString("O"),
            earnedGold = rewardGold
        };

        // 결과 저장 및 기록
        LastRunResult = result;
        DataManager.Instance.UpdateTrainingRecord(result);
        OnRunEnded?.Invoke(result);

        // Training 진행 중 전용 스코어보드 닫기
        UIManager.Instance.ShowTrainingScoreBoard(false, this);
    }
}