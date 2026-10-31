using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum SelfTrainingPhase { Free, Countdown, Measuring, Results }

/// <summary>Room의 활성 Root 수명을 따라 동적 표적과 한 번의 분석 구간을 관리한다.</summary>
public class SelfTrainingController : MonoBehaviour
{
    public const float CountdownSeconds = 3f;
    public const float MeasurementSeconds = 10f;

    [SerializeField] private EnemyController dummyPrefab;
    [SerializeField] private EnemyController shieldDummyPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField, Range(1, 3)] private int dummyCount = 1;
    [SerializeField] private bool useShield;
    [SerializeField] private int shieldCapacity = 100;

    private readonly List<TrainingDummyHealth> dummies = new List<TrainingDummyHealth>(3);
    private double countdownEnd;
    private double measurementStart;
    private bool spawning;

    public SelfTrainingPhase Phase { get; private set; }
    public SelfTrainingStatistics Statistics { get; } = new SelfTrainingStatistics();
    public int DummyCount => dummyCount;
    public bool UseShield => useShield;
    public int ShieldCapacity => shieldCapacity;
    public IReadOnlyList<TrainingDummyHealth> Dummies => dummies;
    public bool IsAnalysisRunning => Phase == SelfTrainingPhase.Countdown || Phase == SelfTrainingPhase.Measuring;
    public float CountdownRemaining => Phase == SelfTrainingPhase.Countdown ? Mathf.Max(0f, (float)(countdownEnd - Time.timeAsDouble)) : 0f;
    public float Elapsed => Phase == SelfTrainingPhase.Measuring ? Mathf.Clamp((float)(Time.timeAsDouble - measurementStart), 0f, MeasurementSeconds) : Statistics.Duration;
    public float Remaining => Mathf.Max(0f, MeasurementSeconds - Elapsed);
    public bool TargetsReady
    {
        get
        {
            if (spawning || dummies.Count != dummyCount) return false;
            foreach (var dummy in dummies)
                if (dummy == null || !dummy.gameObject.activeInHierarchy || dummy.IsInvincible) return false;
            return true;
        }
    }

    private void OnEnable()
    {
        Phase = SelfTrainingPhase.Free;
        Statistics.Begin(Time.timeAsDouble);
        StartCoroutine(SpawnWhenReady());
    }

    private IEnumerator SpawnWhenReady()
    {
        spawning = true;
        // 최초 Scene 로딩 시 Pool의 Awake와 Room의 비활성화가 먼저 완료되도록 한다.
        yield return null;
        if (EnemyPool.Instance != null) SpawnTargets();
        spawning = false;
    }

    private void Update()
    {
        SynchronizeTime(Time.timeAsDouble);
    }

    public void SynchronizeTime(double now)
    {
        if (Phase == SelfTrainingPhase.Countdown && now >= countdownEnd)
        {
            measurementStart = countdownEnd;
            Statistics.Begin(measurementStart);
            ResetTargets();
            Phase = SelfTrainingPhase.Measuring;
        }
        if (Phase != SelfTrainingPhase.Measuring) return;
        double end = measurementStart + MeasurementSeconds;
        if (now >= end)
        {
            Statistics.Complete(end);
            Phase = SelfTrainingPhase.Results;
        }
        else Statistics.Advance(now);
    }

    public bool StartAnalysis()
    {
        if (!isActiveAndEnabled || IsAnalysisRunning || !TargetsReady) return false;
        ResetTargets();
        Statistics.Begin(Time.timeAsDouble);
        countdownEnd = Time.timeAsDouble + CountdownSeconds;
        Phase = SelfTrainingPhase.Countdown;
        return true;
    }

    public void CancelAnalysis()
    {
        Phase = SelfTrainingPhase.Free;
        Statistics.Begin(Time.timeAsDouble);
        ResetTargets();
    }

    public bool Configure(int count, bool shield, int capacity)
    {
        if (IsAnalysisRunning || count < 1 || count > 3 ||
            (capacity != 10 && capacity != 100 && capacity != 300 && capacity != 500)) return false;
        if ((shield ? shieldDummyPrefab : dummyPrefab) == null || spawnPoints == null || spawnPoints.Length < count) return false;
        dummyCount = count;
        useShield = shield;
        shieldCapacity = capacity;
        Phase = SelfTrainingPhase.Free;
        Statistics.Begin(Time.timeAsDouble);
        if (isActiveAndEnabled && EnemyPool.Instance != null)
        {
            DespawnTargets();
            SpawnTargets();
        }
        return true;
    }

    public void ResetPractice()
    {
        if (IsAnalysisRunning) return;
        CancelAnalysis();
    }

    private void SpawnTargets()
    {
        if (dummies.Count > 0) return;
        EnemyController prefab = useShield ? shieldDummyPrefab : dummyPrefab;
        if (prefab == null || spawnPoints == null) return;
        for (int i = 0; i < dummyCount && i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] == null) continue;
            EnemyController enemy = EnemyPool.Instance.Spawn(prefab, spawnPoints[i].position);
            TrainingDummyHealth health = enemy.Health as TrainingDummyHealth;
            if (health == null)
            {
                EnemyPool.Instance.Despawn(enemy);
                Debug.LogError("자율훈련 표적 Prefab에 TrainingDummyHealth가 필요합니다.", prefab);
                continue;
            }
            health.InitializePractice(this, shieldCapacity);
            health.Damaged += HandleDamage;
            enemy.CanTouchDamageActive(false);
            dummies.Add(health);
        }
    }

    private void HandleDamage(TrainingDummyHealth dummy, WeaponBaseSO.DamageInfo info)
    {
        double now = Time.timeAsDouble;
        SynchronizeTime(now);
        if (Phase == SelfTrainingPhase.Measuring)
            Statistics.Record(info, now);
    }

    private void ResetTargets()
    {
        foreach (var dummy in dummies)
            if (dummy != null) dummy.ResetPractice();
    }

    private void DespawnTargets()
    {
        foreach (var dummy in dummies)
        {
            if (dummy == null) continue;
            dummy.Damaged -= HandleDamage;
            EnemyController enemy = dummy.GetComponentInParent<EnemyController>(true);
            if (EnemyPool.Instance != null && enemy != null)
                EnemyPool.Instance.Despawn(enemy);
        }
        dummies.Clear();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        spawning = false;
        Phase = SelfTrainingPhase.Free;
        DespawnTargets();
    }
}
