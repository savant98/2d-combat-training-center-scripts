using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaveManager : MonoBehaviour
{
    [SerializeField] private WaveDataSO waveData;
    [SerializeField] private Transform spawnRoot;
    [SerializeField] private TrainingDefinitionSO currentTrainingDef;

    public static WaveManager ActiveInstance { get; private set; }
    public Action OnAllWavesCleared;
    public int currentWaveIndex = 0;
    public int aliveTargets = 0;

    private Coroutine coroutine;
    private bool isWaveActive = false;
    private bool isSpawnActive = false;
    private Wave currentWave;
    private List<EnemyController> activeEnemies = new List<EnemyController>();
    private List<WorldEntityBase> currentWaveEntities = new List<WorldEntityBase>();


    public void BeginWaves()
    {
        currentWaveIndex = 0;
        ActiveInstance = this;

        if (coroutine != null)
            StopCoroutine(coroutine);

        if (TrainingRunTracker.Instance != null && currentTrainingDef != null)
            TrainingRunTracker.Instance.Begin(currentTrainingDef);

        coroutine = StartCoroutine(NextWaveDelay());
    }
    public void RestartWaves()
    {
        GameManager.Instance.Player.ResetPlayerStatus();
        UIManager.Instance.ShowBossHP(false);
        BeginWaves();
    }
    public void ForceClear()
    {
        ClearActiveWaveHandlers();
        ClearCurrentWaveEntities();
        WorldEntityPool.Instance.DespawnAll();
        UIManager.Instance.ShowBossHP(false);
        currentWaveIndex = 0;
        aliveTargets = 0;
        isWaveActive = false;
        StopAllCoroutines();

        if (ActiveInstance == this)
            ActiveInstance = null;
    }
    private IEnumerator NextWaveDelay()
    {
        UIManager.Instance.StartWavePanelEffect(currentWaveIndex + 1);

        yield return new WaitForSeconds(2f);

        UIManager.Instance.ShowBossHP(false);
        SpawnWave(waveData.waves[currentWaveIndex]);
    }
    private void SpawnWave(Wave wave)
    {
        currentWave = wave;
        aliveTargets = 0;
        isWaveActive = true;
        isSpawnActive = true;
        activeEnemies.Clear();

        // 1. 오브젝트 생성 루틴 시작
        coroutine = StartCoroutine(SpawnRoutine(wave));

        // 2. Wave 클리어 조건별 분기 처리 시작
        if (wave.clearType == WaveClearType.SurviveForTime)
        {
            StartCoroutine(SurviveTimerRoutine(wave.surviveTime));
        }
    }
    private IEnumerator SpawnRoutine(Wave wave)
    {
        // Spawn Enemy
        foreach (var info in wave.spawnObjects)
        {
            // spawn 위치 설정
            Vector3 spawnPos = new(spawnRoot.position.x + info.spawnPosition.x, spawnRoot.position.y + info.spawnPosition.y, 0);
            GameObject spawnedObj = null;

            // Enemy인 경우
            if (info.prefab.TryGetComponent(out EnemyController enemyPrefab))
            {
                EnemyController enemyInstance = EnemyPool.Instance.Spawn(enemyPrefab, spawnPos);
                enemyInstance.Health.IsBossEnemy = info.isBossEnemy;

                if (info.isBossEnemy)
                {
                    StartCoroutine(RoomManager.Instance.CurrentRoom.ActivateBossEntryCamera(enemyInstance.transform));
                }

                // Enemy 죽으면 알리는 코드
                if (wave.clearType == WaveClearType.DefeatAllTargets && info.isRequiredToClear)
                {
                    aliveTargets++;
                    activeEnemies.Add(enemyInstance);

                    enemyInstance.SetWaveDeathHandler(() =>
                    {
                        HandleEnemyDeath(enemyInstance);
                    });
                }
                else // 필수 타겟이 아닌 경우 사망시 일반 반환
                {
                    enemyInstance.SetWaveDeathHandler(() =>
                    {
                        EnemyPool.Instance.Despawn(enemyInstance);
                    });
                }

                spawnedObj = enemyInstance.gameObject;
            }
            else if (info.prefab.TryGetComponent(out WorldEntityBase worldEntityPrefab)) // WorldEntity인 경우
            {
                WorldEntityBase worldEntityInstance = WorldEntityPool.Instance.Spawn(worldEntityPrefab, spawnPos, Quaternion.identity);
                
                if (!info.keepAfterWave)
                {
                    currentWaveEntities.Add(worldEntityInstance);
                }

                spawnedObj = worldEntityInstance.gameObject;

                // 파괴 가능한 장애물
                if (wave.clearType == WaveClearType.DefeatAllTargets && info.isRequiredToClear)
                {
                    if (spawnedObj.TryGetComponent(out HealthBase health))
                    {
                        aliveTargets++;
                        // 파괴 가능한 오브젝트가 생기면 델리게이트 구독
                    }
                }
            }
            else // 둘 다 아닌 경우
            {
                spawnedObj = Instantiate(info.prefab, spawnPos, Quaternion.identity);
            }

            if (info.spawnDelay > 0f)
            {
                yield return new WaitForSeconds(info.spawnDelay);
            }
        }

        isSpawnActive = false;
    }

    private void HandleEnemyDeath(EnemyController deadEnemy)
    {
        if (!isWaveActive || currentWave.clearType != WaveClearType.DefeatAllTargets)
            return;

        EnemyPool.Instance.Despawn(deadEnemy);
        activeEnemies.Remove(deadEnemy);

        aliveTargets--;

        if (aliveTargets > 0 || isSpawnActive)
            return;

        CompleteWave();
    }

    private IEnumerator SurviveTimerRoutine(float time)
    {
        yield return new WaitForSeconds(time);

        if (isWaveActive && currentWave.clearType == WaveClearType.SurviveForTime)
        {
            CompleteWave();    
        }
    }

    public void HandleCustomTrigger(string triggerKey)
    {
        if (!isWaveActive || currentWave.clearType != WaveClearType.CustomTrigger)
            return;

        if (currentWave.customTriggerKey == triggerKey)
        {
            CompleteWave();
        }
    }

    private void ClearActiveWaveHandlers()
    {
        foreach (var enemy in activeEnemies)
        {
            if (enemy != null)
            {
                enemy.ClearWaveDeathHandler();
            }
        }
        activeEnemies.Clear();
    }

    private void CompleteWave()
    {
        isWaveActive = false;
        ClearActiveWaveHandlers();
        ClearCurrentWaveEntities();

        currentWaveIndex++;

        if (currentWaveIndex >= waveData.waves.Length)
        {
            // 마지막 웨이브 완료
            Debug.Log("All Waves Cleared!");
            GameManager.Instance.Player.ResetPlayerStatus();
            WorldEntityPool.Instance.DespawnAll();

            OnAllWavesCleared?.Invoke();

            if (ActiveInstance == this)
                ActiveInstance = null;
        }
        else
        {
            // 다음 웨이브로 이동
            coroutine = StartCoroutine(NextWaveDelay());
        }
    }
    private void ClearCurrentWaveEntities()
    {
        foreach (var entity in currentWaveEntities)
        {
            if (entity != null && entity.gameObject.activeInHierarchy)
            {
                // 이 함수가 호출되면 각자의 애니메이션을 재생하며 스르륵 사라집니다.
                entity.ForceReturnToPool();
            }
        }
        currentWaveEntities.Clear(); // 비워주기
    }
}
