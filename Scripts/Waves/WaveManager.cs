using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaveManager : MonoBehaviour
{
    [SerializeField] private WaveDataSO waveData;
    [SerializeField] private Transform spawnRoot;
    [SerializeField] private TrainingDefinitionSO currentTrainingDef;

    [SerializeField] private TrainingRoomLayoutController roomLayout;

    public WaveDataSO Data => waveData;
    public IReadOnlyList<WorldEntityBase> WorldEntities => currentWaveEntities;
    public Transform SpawnRoot => spawnRoot;
    private WavePlayerStart playerStartSequence;
    private readonly Dictionary<WaveSpawnInfo, WorldEntityBase> preparedCourses = new();
    private WavePlayerStart PlayerStartSequence => playerStartSequence != null ? playerStartSequence
        : playerStartSequence = gameObject.GetComponent<WavePlayerStart>() ?? gameObject.AddComponent<WavePlayerStart>();
    public bool IsPreparingWave => playerStartSequence != null && playerStartSequence.IsHoldingPlayer;
    public bool NextWaveTeleports => waveData != null && currentWaveIndex + 1 < waveData.waves.Length && waveData.waves[currentWaveIndex + 1].teleportAtStart;
    public event Action<Wave> WaveStarted;
    public event Action WaveEnded;
    private readonly List<GameObject> looseObjects = new();
    public void Configure(WaveDataSO data, Transform root, TrainingDefinitionSO training)
    {
        waveData = data;
        spawnRoot = root;
        currentTrainingDef = training;
    }
    public static WaveManager ActiveInstance { get; private set; }
    public Action OnAllWavesCleared;
    public event Action<EnemyController> EnemySpawned;
    // 목표 지점 등이 보낸 사용자 트리거를 알린다. 훈련 구간은 도달 키를 받아 마무리 연출을 진행한 뒤 완료 키로 웨이브를 끝낸다.
    public event Action<string> CustomTriggerRaised;
    // 웨이브를 처음부터 시작하거나 강제로 정리할 때 알린다. 훈련 구간은 이때 바뀐 지형·카메라를 원래대로 되돌린다.
    public event Action WavesReset;
    public bool SuppressTrainingRecording { get; set; }
    public int currentWaveIndex = 0;
    public int aliveTargets = 0;

    private Coroutine coroutine;
    private bool isWaveActive = false;
    private bool isSpawnActive = false;
    private Wave currentWave;
    private List<EnemyController> activeEnemies = new List<EnemyController>();
    private List<WorldEntityBase> currentWaveEntities = new List<WorldEntityBase>();


    public void BeginWaves(bool showWaveIntro = true)
    {
        playerStartSequence?.Release();
        preparedCourses.Clear();
        if (waveData.cellMap.enabled && GetComponentInParent<WaveCellTerrain>(true) == null)
        {
            var room = GetComponentInParent<Room>(true);
            room.gameObject.AddComponent<WaveCellTerrain>().Configure(waveData, this);
        }
        StopAllCoroutines();
        ClearActiveWaveHandlers();
        ClearCurrentWaveEntities();
        isWaveActive = false;
        WavesReset?.Invoke();
        currentWaveIndex = 0;
        ActiveInstance = this;

        if (coroutine != null)
            StopCoroutine(coroutine);

        if (!SuppressTrainingRecording && TrainingRunTracker.Instance != null && currentTrainingDef != null)
            TrainingRunTracker.Instance.Begin(currentTrainingDef);

        if (showWaveIntro) coroutine = StartCoroutine(NextWaveDelay());
        else coroutine = StartCoroutine(PrepareAndSpawn());
    }
    public void RestartWaves()
    {
        GameManager.Instance.Player.ResetPlayerStatus();
        UIManager.Instance.ShowBossHP(false);
        BeginWaves();
    }
    public void ForceClear()
    {
        playerStartSequence?.Release();
        preparedCourses.Clear();
        WavesReset?.Invoke();
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
    private IEnumerator NextWaveDelay(float clearDelay = 0f)
    {
        // 앞 웨이브의 정리(발판 소멸·지형 복귀)를 기다린 뒤 다음 웨이브 안내를 시작한다.
        if (clearDelay > 0f)
            yield return new WaitForSeconds(clearDelay);

        UIManager.Instance.StartWavePanelEffect(currentWaveIndex + 1);

        yield return new WaitForSeconds(2f);

        UIManager.Instance.ShowBossHP(false);
        yield return PrepareAndSpawn();
    }
    private IEnumerator PrepareAndSpawn()
    {
        var wave = waveData.waves[currentWaveIndex];
        PlayerStartSequence.Hold();
        try
        {
            var cells = GetComponentInParent<WaveCellTerrain>(true);
            var room = GetComponentInParent<Room>(true);
            var coordinates = cells != null ? cells.transform : room != null ? room.transform : transform;
            // 시작 좌표로 옮기는 Wave: 지형이 바뀌는 동안 크레인이 구속 상자를 들어 옮긴다(둘을 함께 진행한다).
            PlayerStartSequence.BeginCarry(waveData, currentWaveIndex, coordinates);
            // 안쪽 준비 순서는 Unity에 맡기지 않고(yield return 순서) 직접 돌린다. 맡기면 안쪽에서 예외가 났을 때 이 순서가 그대로 멈춰
            // 아래 finally가 실행되지 않고, Player가 구속된 채(옮기는 Wave면 크레인에 들린 채) 남는다.
            if (cells != null) { for (var step = Unwound(cells.Prepare(currentWaveIndex)); step.MoveNext();) yield return step.Current; }
            else
            {
                if (roomLayout != null && roomLayout.isActiveAndEnabled) { for (var step = Unwound(roomLayout.PrepareWave(currentWaveIndex)); step.MoveNext();) yield return step.Current; }
                var builder = GetComponentInParent<WaveRoomBuilder>();
                if (builder != null) { for (var step = Unwound(builder.Prepare(waveData.waves[currentWaveIndex])); step.MoveNext();) yield return step.Current; }
            }
            // 코스 벽의 열림도 공통 준비 단계에 넣고 나머지 예약 시각은 Wave 시작 이후로 유지한다.
            foreach (var info in wave.spawnObjects ?? Array.Empty<WaveSpawnInfo>())
            {
                if (info.prefab == null || info.prefab.GetComponent<TrainingCourseActivator>() == null) continue;
                var entity = WorldEntityPool.Instance.Spawn(info.prefab.GetComponent<WorldEntityBase>(), spawnRoot.position + (Vector3)info.spawnPosition, info.Rotation);
                if (!info.keepAfterWave) currentWaveEntities.Add(entity);
                preparedCourses.Add(info, entity);
            }
            if (room != null)
            {
                // 방 전체(셀·배경 모듈 수만 개)를 훑지 않고 방이 찾아 둔 구간 목록을 쓴다.
                var stages = RoomViewRules.CourseStagesIn(room);
                while (Array.Exists(stages, stage => stage != null && stage.IsMoving)) yield return null;
            }
            // 지형이 다 바뀐 뒤에 내려놓는다. 들고 옮기는 쪽이 보통 더 오래 걸려 지형이 먼저 끝난다.
            for (var step = Unwound(PlayerStartSequence.FinishCarry()); step.MoveNext();) yield return step.Current;
        }
        finally { playerStartSequence?.Release(); }
        SpawnWave(wave);
    }
    // 여러 겹의 순서(순서 안에서 다시 순서를 넘기는 것)를 한 겹으로 펴서 돌린다. 기다리는 대상(null·WaitForSeconds 등)은 그대로 넘긴다.
    // 어느 깊이에서 예외가 나도 이 순서를 돌리던 쪽의 finally까지 풀려 나온다.
    private static IEnumerator Unwound(IEnumerator routine)
    {
        var levels = new Stack<IEnumerator>();
        levels.Push(routine);
        while (levels.Count > 0)
        {
            var top = levels.Peek();
            if (!top.MoveNext()) { levels.Pop(); continue; }
            if (top.Current is IEnumerator inner) levels.Push(inner);
            else yield return top.Current;
        }
    }
    private void OnDisable()
    {
        StopAllCoroutines();
        isWaveActive = isSpawnActive = false;
        playerStartSequence?.Release();
        preparedCourses.Clear();
    }
    // 플레이어가 패배했을 때(사망·훈련 성적 미흡) Wave 진행을 멈춘다. 그 뒤의 처치·목표 도달은 Wave를 끝내지 않고
    // 다음 Wave 준비도 진행되지 않는다. 적과 지형은 그대로 두고, 재도전·로비 복귀의 ForceClear·BeginWaves가 정리한다.
    public void HaltForPlayerDefeat()
    {
        StopAllCoroutines();
        // 보스 등장 카메라(보스를 따라가며 확대)가 도는 중이었으면 그 코루틴도 함께 끊기므로 카메라를 플레이어로 돌려 둔다.
        if (RoomManager.Instance != null && RoomManager.Instance.CurrentRoom != null) RoomManager.Instance.CurrentRoom.ResetBossEntryCamera();
        isWaveActive = isSpawnActive = false;
        playerStartSequence?.Release();
        preparedCourses.Clear();
    }
    private void SpawnWave(Wave wave)
    {
        currentWave = wave;
        aliveTargets = 0;
        isWaveActive = true;
        isSpawnActive = true;
        activeEnemies.Clear();
        foreach (var prepared in preparedCourses) InitializeSpawn(prepared.Value.gameObject, prepared.Key);
        WaveStarted?.Invoke(wave);

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
        float sequenceStart = Time.time;
        if (wave.startDelay > 0) yield return new WaitForSeconds(wave.startDelay);
        // Spawn Enemy
        foreach (var info in wave.spawnObjects ?? Array.Empty<WaveSpawnInfo>())
        {
            if (!isWaveActive) yield break;
            if (info.prefab == null) continue;
            if (preparedCourses.ContainsKey(info))
            {
                if (info.spawnDelay > 0) yield return new WaitForSeconds(info.spawnDelay);
                continue;
            }
            // spawn 위치 설정
            Vector3 spawnPos = new(spawnRoot.position.x + info.spawnPosition.x, spawnRoot.position.y + info.spawnPosition.y, 0);
            GameObject spawnedObj = null;

            // Enemy인 경우
            if (info.prefab.TryGetComponent(out EnemyController enemyPrefab))
            {
                EnemyController enemyInstance = EnemyPool.Instance.Spawn(enemyPrefab, spawnPos);
                enemyInstance.Health.IsBossEnemy = info.isBossEnemy;
                EnemySpawned?.Invoke(enemyInstance);

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
                WorldEntityBase worldEntityInstance = WorldEntityPool.Instance.Spawn(worldEntityPrefab, spawnPos, info.Rotation);
                // 배치마다 저장한 장치 설정(뻗는 거리·팔 길이·점프 높이 등)을 이 개체에만 적용한다.
                WaveDeviceSettings.Apply(worldEntityInstance.gameObject, info.deviceSettings);
                
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
                spawnedObj = Instantiate(info.prefab, spawnPos, info.Rotation);
                if (!info.keepAfterWave) looseObjects.Add(spawnedObj);
            }

            if (spawnedObj != null) InitializeSpawn(spawnedObj, info);

            if (info.spawnDelay > 0f)
            {
                yield return new WaitForSeconds(info.spawnDelay);
            }
        }

        float cellsReady = 0;
        foreach (var batch in wave.cellBatches) cellsReady = Mathf.Max(cellsReady, batch.time + batch.duration);
        if (Time.time < sequenceStart + cellsReady) yield return new WaitForSeconds(sequenceStart + cellsReady - Time.time);
        isSpawnActive = false;
        if (isWaveActive && wave.clearType == WaveClearType.DefeatAllTargets && aliveTargets == 0)
            CompleteWave();
    }

    private void InitializeSpawn(GameObject item, WaveSpawnInfo info)
    {
        foreach (var goal in item.GetComponentsInChildren<TutorialGoal>(true))
        {
            goal.ConfigureWaveGoal(info.goalKeyOverride);
            goal.InitializeGoalSetting();
        }
        var lifetime = item.GetComponent<WaveSpawnLifetime>();
        if (lifetime == null) lifetime = item.AddComponent<WaveSpawnLifetime>();
        lifetime.Arm(info.lifetime, () => Expire(item));
    }
    private void Expire(GameObject item)
    {
        if (item == null || !item.activeInHierarchy) return;
        if (item.TryGetComponent(out EnemyController enemy))
        {
            if (activeEnemies.Contains(enemy)) HandleEnemyDeath(enemy);
            else EnemyPool.Instance.Despawn(enemy);
        }
        else if (item.TryGetComponent(out WorldEntityBase entity)) entity.ForceReturnToPool();
        else Destroy(item);
    }

    private void HandleEnemyDeath(EnemyController deadEnemy)
    {
        if (isWaveActive && currentWave.clearType != WaveClearType.DefeatAllTargets)
            return;

        EnemyPool.Instance.Despawn(deadEnemy);
        activeEnemies.Remove(deadEnemy);

        // 패배로 Wave가 멈춘 뒤(HaltForPlayerDefeat) 처치된 표적은 회수만 하고 Wave 진행에는 반영하지 않는다.
        if (!isWaveActive)
            return;

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
        CustomTriggerRaised?.Invoke(triggerKey);

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
        // 사망한 플레이어는 Wave를 끝낼 수 없다(사망 뒤 남은 공격·드론이 마지막 적을 처치해도 훈련 완료로 넘어가지 않는다).
        if (GameManager.Instance != null && GameManager.Instance.Player != null && GameManager.Instance.Player.Health.IsDead) return;
        // 이전 코스가 닫히는 순간부터 다음 지형·Spawn 완료까지 같은 잠금을 유지한다.
        if (currentWaveIndex + 1 < waveData.waves.Length && currentWaveEntities.Exists(e => e is TrainingCourseActivator)) PlayerStartSequence.Hold();
        else playerStartSequence?.Release();
        preparedCourses.Clear();
        isWaveActive = false;
        StopAllCoroutines();
        if (RoomManager.Instance != null && RoomManager.Instance.CurrentRoom != null) RoomManager.Instance.CurrentRoom.ResetBossEntryCamera();
        WaveEnded?.Invoke();
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
            coroutine = StartCoroutine(NextWaveDelay(currentWave != null ? currentWave.clearDelay : 0f));
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
        foreach (var item in looseObjects) if (item != null) Destroy(item);
        looseObjects.Clear();
        currentWaveEntities.Clear(); // 비워주기
    }
}
