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

    // 진행 중에는 지금 끝냈을 때의 예상 등급, 끝난 뒤에는 확정 등급이다.
    public Grade CurrentPredictedGrade => training == null ? Grade.Poor
        : ended ? LastRunResult.grade : training.Evaluate(hitCount, Time.time - startTime);

    // [추가] Room 등에서 연출 시 가져갈 최근 실행 결과
    public RunResult LastRunResult { get; private set; }

    public event Action<RunResult> OnRunEnded;

    // 기록 중인 판이 있는지(Begin부터 종료까지).
    public bool IsRunActive => training != null && !ended;
    // 이번 판의 전투 기록(가한 피해, 받은 피해, 무장별 피해와 회복). Begin에서 비우고 판이 끝나면 멈춘다. UI는 읽기만 한다.
    public TrainingCombatStats Stats { get; } = new TrainingCombatStats();
    // 마지막 판이 패배로 끝난 이유(클리어했으면 None).
    public TrainingFailReason LastFailReason { get; private set; }

    private PlayerHealth playerHealth;
    private WaveManager boundWaves;      // 이 판을 시작한 WaveManager(없으면 성적 미흡 감시를 하지 않는다)
    private float startTime;
    private float lastTimeSeconds;
    private int hitCount;
    private bool ended;
    private bool killPending;            // 성적 미흡 확정 뒤 사망 처리 대기(피격 처리 도중을 피한다)
    private bool defeatResultPending;    // 패배 결과 화면이 아직 가져가지 않은 결과

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
        killPending = false;
        defeatResultPending = false;
        startTime = Time.time;
        boundWaves = WaveManager.ActiveInstance;
        Stats.Reset();

        if (playerHealth == null)
            playerHealth = GameManager.Instance.Player.Health;

        playerHealth.OnPlayerDeath += HandlePlayerDeath;
        playerHealth.OnDamaged += HandlePlayerDamaged;
        playerHealth.OnDamageApplied += HandlePlayerDamageApplied;
        playerHealth.OnHealed += HandlePlayerHealed;
        EnemyHealth.DamageApplied += HandleEnemyDamaged;
        EnemyShieldSystem.ShieldDamageApplied += HandleEnemyDamaged;

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
            playerHealth.OnDamageApplied -= HandlePlayerDamageApplied;
            playerHealth.OnHealed -= HandlePlayerHealed;
        }
        EnemyHealth.DamageApplied -= HandleEnemyDamaged;
        EnemyShieldSystem.ShieldDamageApplied -= HandleEnemyDamaged;

        if (WaveManager.ActiveInstance != null)
            WaveManager.ActiveInstance.OnAllWavesCleared -= HandleClear;
        if (boundWaves != null)
            boundWaves.OnAllWavesCleared -= HandleClear;
    }

    private void Update() => Watch();
    private void LateUpdate() => Watch();

    // 성적이 미흡이 되면 패배를 확정하고 플레이어를 사망시킨다
    // (2026-11-10 사용자 요청 "성적이 미흡이 되면 자동으로 player가 사망. 그 사이에 최종 라운드를 승리해도 패배 시퀀스로").
    // 이 판을 시작한 Wave가 진행 중일 때만 감시한다.
    private void Watch()
    {
        if (killPending)
        {
            killPending = false;
            if (playerHealth != null) playerHealth.ForceDeath();
            return;
        }
        if (!IsRunActive || !IsBoundWaveRunning()) return;
        if (training.Evaluate(hitCount, Time.time - startTime) == Grade.Poor)
            Fail();
    }

    private bool IsBoundWaveRunning() => boundWaves != null && WaveManager.ActiveInstance == boundWaves;

    // 패배를 먼저 확정해 그 뒤의 Wave 클리어가 결과를 뒤집지 못하게 하고, 사망 처리는 이번 프레임의 Update·LateUpdate로 넘긴다.
    private void Fail()
    {
        // 피격만으로 미흡이면 피격 한도 초과, 아니면 제한 시간 초과다.
        TrainingFailReason reason = training.Evaluate(hitCount, 0f) == Grade.Poor
            ? TrainingFailReason.HitLimit : TrainingFailReason.TimeLimit;
        EndRun(false, reason);
        killPending = true;
    }

    // 이번 사망이 기록 중이던 판을 끝냈으면 그 결과를 한 번 넘긴다(패배 결과 화면용). 기록이 없던 사망이면 false.
    public bool TryTakeDefeatResult(out RunResult result)
    {
        result = LastRunResult;
        if (!defeatResultPending) return false;
        defeatResultPending = false;
        return true;
    }

    private void HandlePlayerDamaged(WeaponBaseSO.DamageInfo damageInfo)
    {
        if (ended) return;
        hitCount++;
        // 이 피격으로 성적이 미흡이 되면 그 자리에서 패배를 확정한다(같은 프레임에 마지막 적이 쓰러져도 훈련 완료가 되지 않는다).
        if (IsBoundWaveRunning() && training.Evaluate(hitCount, Time.time - startTime) == Grade.Poor)
        {
            Fail();
            // 피격 처리 도중이므로 PlayerHealth가 이 피격을 끝내는 자리에서 바로 사망 처리한다(피격 경직·히트스톱·위치 되돌림 없이).
            killPending = false;
            playerHealth.ForceDeath();
        }
    }

    private void HandlePlayerDamageApplied(WeaponBaseSO.DamageInfo damageInfo, float applied)
    {
        if (!ended) Stats.RecordTaken(applied);
    }

    private void HandlePlayerHealed(WeaponBaseSO.DamageInfo damageInfo, float applied)
    {
        if (!ended) Stats.RecordHealing(damageInfo.sourceWeapon, applied);
    }

    private void HandleEnemyDamaged(EnemyHealth target, WeaponBaseSO.DamageInfo damageInfo, float applied)
    {
        if (!ended) Stats.RecordDealt(damageInfo.sourceWeapon, applied);
    }

    private void HandlePlayerDeath()
    {
        if (ended) return;
        EndRun(false, TrainingFailReason.Destroyed);
    }

    private void HandleClear()
    {
        if (ended) return;
        // 클리어한 순간의 성적이 미흡이면(제한 시간, 피격 한도를 넘는 것과 거의 동시) 훈련 완료가 아니라 패배다.
        if (training.Evaluate(hitCount, Time.time - startTime) == Grade.Poor)
        {
            Fail();
            return;
        }
        EndRun(true, TrainingFailReason.None);
    }

    private void EndRun(bool cleared, TrainingFailReason failReason)
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
        LastFailReason = cleared ? TrainingFailReason.None : failReason;
        defeatResultPending = !cleared;
        DataManager.Instance.UpdateTrainingRecord(result);
        OnRunEnded?.Invoke(result);

        // 패배하면 Wave 진행을 멈춘다(사망 연출 뒤에서 Wave가 클리어되거나 다음 Wave로 넘어가지 않게).
        if (!cleared && boundWaves != null)
            boundWaves.HaltForPlayerDefeat();

        // Training 진행 중 전용 스코어보드 닫기
        UIManager.Instance.ShowTrainingScoreBoard(false, this);
    }
}