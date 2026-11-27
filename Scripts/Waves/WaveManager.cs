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
    private WavePlayerStart PlayerStartSequence
    {
        get
        {
            if (playerStartSequence == null && !TryGetComponent(out playerStartSequence)) playerStartSequence = gameObject.AddComponent<WavePlayerStart>();
            return playerStartSequence;
        }
    }
    // 크레인으로 옮긴 Wave는 구속 상자를 0초 배치 직후까지 남긴다. 그 상자를 남긴 준비의 번호다(0이면 남긴 상자가 없다).
    // 준비마다 새 번호를 쓰므로, 늦게 도착한 해제가 그 사이 다시 잡은 다음 Wave 준비의 구속을 끊지 않는다.
    private int heldUntilSpawn;
    private int preparationSerial;
    // 지형 준비 중인지: 구속을 시작한 때부터 크레인 도착과 카메라 정착까지다. 크레인으로 옮긴 Wave가 0초 배치를 기다리며
    // 상자만 남겨 둔 동안(시작 안내 포함)은 준비가 끝난 것으로 본다. 코스 시작 위치 리셋과 복귀 순간이동 판단이 이 의미를 쓴다.
    public bool IsPreparingWave => playerStartSequence != null && playerStartSequence.IsHoldingPlayer && heldUntilSpawn == 0;
    public bool NextWaveTeleports => waveData != null && currentWaveIndex + 1 < waveData.waves.Length && waveData.waves[currentWaveIndex + 1].teleportAtStart;
    public event Action<Wave> WaveStarted;
    /// <summary>지형과 이동 준비가 끝나고 시작 안내가 표시되는 시점이다. Wave 번호는 1부터 시작한다.</summary>
    public event Action<int> WaveIntroStarted;
    public event Action WaveEnded;
    private readonly List<GameObject> looseObjects = new();
    private readonly HashSet<EnemyController> currentWaveEnemies = new();
    private readonly HashSet<TutorialGoal> activeGoals = new();
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
    public bool IsHalted { get; private set; }
    public bool IsWaveActive => isWaveActive;
    public int TotalWaveCount => waveData != null && waveData.waves != null ? waveData.waves.Length : 0;
    public int currentWaveIndex = 0;
    public int aliveTargets = 0;

    private Coroutine coroutine;
    private bool isWaveActive = false;
    private bool isSpawnActive = false;
    private bool bossDefeatedThisWave;
    private PlayerWeaponAbility tutorialStorageAbility;
    private Action restoreTutorialArmament;
    private readonly List<EnemyHealth> tutorialStorageTargets = new();
    internal bool TutorialSkillStored { get; private set; }
    internal bool TutorialStoredSkillUsed { get; private set; }
    private Wave currentWave;
    private List<EnemyController> activeEnemies = new List<EnemyController>();
    private List<WorldEntityBase> currentWaveEntities = new List<WorldEntityBase>();


    public void BeginWaves(bool showWaveIntro = true)
    {
        IsHalted = false;
        ReleasePlayer();
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
        IsHalted = false;
        ReleasePlayer();
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

        UIManager.Instance.ShowBossHP(false);
        yield return PrepareWave(true);
    }
    private IEnumerator PrepareAndSpawn() => PrepareWave(false);
    private IEnumerator PrepareWave(bool showWaveIntro)
    {
        var wave = waveData.waves[currentWaveIndex];
        int preparation = ++preparationSerial;
        heldUntilSpawn = 0;
        PlayerStartSequence.Hold();
        // 이 준비가 구속을 풀 책임이 있는지. 크레인으로 옮기지 않은 Wave는 지형 준비 직후에 풀고, 옮긴 Wave는 Wave 시작 때 SpawnRoutine에 넘긴다.
        bool ownsHold = true;
        try
        {
            var cells = GetComponentInParent<WaveCellTerrain>(true);
            var room = GetComponentInParent<Room>(true);
            var coordinates = cells != null ? cells.transform : room != null ? room.transform : transform;
            // 시작 좌표로 옮기는 Wave: 지형이 바뀌는 동안 크레인이 구속 상자를 들어 옮긴다(둘을 함께 진행한다).
            PlayerStartSequence.BeginCarry(waveData, currentWaveIndex, coordinates);
            // 이번 준비에서 크레인이 실제로 옮기는지(시작 좌표 근처이고 새 지형도 없으면 옮기지 않는다). 크레인 이동은 여기서 바로 시작된다.
            bool carried = PlayerStartSequence.IsCarrying;
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
            // 크레인으로 옮긴 Wave는 시작 안내 동안 상자를 남겨 두었다가 0초 항목을 모두 놓은 직후에 푼다(SpawnRoutine).
            // 옮기지 않은 Wave는 지형 준비가 끝나면 바로 푼다(바뀔 지형이 없으면 상자가 보이기 전에 풀린다).
            if (carried) heldUntilSpawn = preparation;
            else { ownsHold = false; ReleasePlayer(); }
            if (showWaveIntro)
            {
                WaveIntroStarted?.Invoke(currentWaveIndex + 1);
                UIManager.Instance.StartWavePanelEffect(currentWaveIndex + 1);
                yield return new WaitForSeconds(2f);
            }
            // 여기부터 해제는 SpawnRoutine이 맡는다. SpawnWave 안에서 Wave가 바로 끝나 다음 Wave 준비가 구속을 다시 잡을 수 있으므로
            // 이 준비는 아래 finally에서도 무조건 풀지 않는다.
            ownsHold = false;
            SpawnWave(wave);
        }
        finally
        {
            // 준비나 안내 도중 예외로 끊겼으면 구속을 푼다. Wave 시작 도중 예외로 SpawnRoutine이 상자를 넘겨받지 못했으면 그 상자만 푼다
            // (이미 풀렸거나 다음 준비가 다시 잡았으면 아무것도 하지 않는다). StopAllCoroutines로 끊긴 경우는 여기로 오지 않고 끊은 쪽이 직접 푼다.
            if (ownsHold) ReleasePlayer();
            else ReleaseAfterSpawn(preparation);
        }
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
    // 이 WaveManager가 잡은 구속을 푼다(초기화·중단·Wave 종료). 0초 배치 뒤에 풀려고 남겨 둔 상자도 함께 정리한다.
    private void ReleasePlayer()
    {
        heldUntilSpawn = 0;
        if (playerStartSequence != null) playerStartSequence.Release();
    }
    // 크레인으로 옮긴 Wave가 남겨 둔 상자를 푼다. 같은 준비가 남긴 상자일 때 한 번만 풀고,
    // 이미 풀렸거나 그 사이 다음 준비가 구속을 다시 잡았으면 아무것도 하지 않는다.
    private void ReleaseAfterSpawn(int preparation)
    {
        if (preparation != 0 && preparation == heldUntilSpawn) ReleasePlayer();
    }
    private void OnDisable()
    {
        StopTutorialPractice();
        StopAllCoroutines();
        isWaveActive = isSpawnActive = false;
        ReleasePlayer();
        preparedCourses.Clear();
    }
    // 플레이어가 패배했을 때(사망·훈련 성적 미흡) Wave 진행을 멈춘다. 그 뒤의 처치·목표 도달은 Wave를 끝내지 않고
    // 다음 Wave 준비도 진행되지 않는다. 적과 지형은 그대로 두고, 재도전·로비 복귀의 ForceClear·BeginWaves가 정리한다.
    public void HaltForPlayerDefeat()
    {
        IsHalted = true;
        StopTutorialPractice();
        StopAllCoroutines();
        // 보스 등장 카메라(보스를 따라가며 확대)가 도는 중이었으면 그 코루틴도 함께 끊기므로 카메라를 플레이어로 돌려 둔다.
        if (RoomManager.Instance != null && RoomManager.Instance.CurrentRoom != null) RoomManager.Instance.CurrentRoom.ResetBossEntryCamera();
        isWaveActive = isSpawnActive = false;
        ReleasePlayer();
        preparedCourses.Clear();
    }
    private void SpawnWave(Wave wave)
    {
        currentWave = wave;
        bossDefeatedThisWave = false;
        aliveTargets = 0;
        isWaveActive = true;
        isSpawnActive = true;
        activeEnemies.Clear();
        foreach (var prepared in preparedCourses) InitializeSpawn(prepared.Value.gameObject, prepared.Key);
        BeginTutorialStoragePractice(wave);
        var tutorial = wave.tutorial;
        if (tutorial != null && tutorial.enabled && tutorial.action == TutorialAction.SwapWeapon && tutorial.practiceArmament != null)
            restoreTutorialArmament = GameManager.Instance?.Player?.AbilityManager?.AttackAbility?.BeginTutorialWeaponSwap(tutorial.practiceArmament);
        TutorialWaveGuide.Begin(this, wave.tutorial);
        WaveStarted?.Invoke(wave);

        // 1. 오브젝트 생성 루틴 시작(크레인으로 옮긴 Wave면 0초 항목을 모두 놓은 직후에 남겨 둔 상자를 푼다)
        coroutine = StartCoroutine(SpawnRoutine(wave, heldUntilSpawn));

        // 2. Wave 클리어 조건별 분기 처리 시작
        if (wave.clearType == WaveClearType.SurviveForTime)
        {
            StartCoroutine(SurviveTimerRoutine(wave.surviveTime));
        }
    }
    private IEnumerator SpawnRoutine(Wave wave, int heldPreparation)
    {
        // 크레인으로 옮긴 Wave의 남겨 둔 상자는 생성 시각 0인 항목을 모두 놓은 직후, 첫 대기 직전(같은 프레임)에 푼다.
        // 0초 항목이 없으면(첫 생성 대기 > 0) Wave 시계 0초에, 항목이 하나도 없으면 목록을 다 돈 직후(Wave 완료 판정 전)에 푼다.
        // 예외나 중간 종료로 빠져나가도 finally가 푼다. 같은 준비의 상자를 한 번만 풀므로 여러 곳에서 불러도 된다.
        try
        {
            float sequenceStart = Time.time;
            if (wave.startDelay > 0)
            {
                ReleaseAfterSpawn(heldPreparation);
                yield return new WaitForSeconds(wave.startDelay);
            }
            // Spawn Enemy
            foreach (var info in wave.spawnObjects ?? Array.Empty<WaveSpawnInfo>())
            {
                if (!isWaveActive) yield break;
                if (info.prefab == null) continue;
                if (preparedCourses.ContainsKey(info))
                {
                    if (info.spawnDelay > 0)
                    {
                        ReleaseAfterSpawn(heldPreparation);
                        yield return new WaitForSeconds(info.spawnDelay);
                    }
                    continue;
                }
                // spawn 위치 설정
                Vector3 spawnPos = new(spawnRoot.position.x + info.spawnPosition.x, spawnRoot.position.y + info.spawnPosition.y, 0);
                GameObject spawnedObj = null;

                // Enemy인 경우
                if (info.prefab.TryGetComponent(out EnemyController enemyPrefab))
                {
                    EnemyController enemyInstance = EnemyPool.Instance.Spawn(enemyPrefab, spawnPos, info.isBossEnemy);
                    if (!info.keepAfterWave) currentWaveEnemies.Add(enemyInstance);
                    else currentWaveEnemies.Remove(enemyInstance);
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
                        if (wave.tutorial != null && wave.tutorial.enabled && wave.tutorial.action == TutorialAction.SwapWeapon && wave.tutorial.practiceArmament != null)
                            enemyInstance.Health?.RequireDamageWeapon(wave.tutorial.practiceArmament.normal);
                        if (tutorialStorageAbility != null && !TutorialStoredSkillUsed && enemyInstance.Health != null)
                        {
                            // 저장과 호출을 익히기 전까지 게이지를 다시 채울 표적을 남겨 둔다.
                            enemyInstance.Health.SetMinimumHealth(1f);
                            tutorialStorageTargets.Add(enemyInstance.Health);
                        }

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
                    ReleaseAfterSpawn(heldPreparation);
                    yield return new WaitForSeconds(info.spawnDelay);
                }
            }
            ReleaseAfterSpawn(heldPreparation);

            float cellsReady = 0;
            foreach (var batch in wave.cellBatches) cellsReady = Mathf.Max(cellsReady, batch.time + batch.duration);
            if (Time.time < sequenceStart + cellsReady) yield return new WaitForSeconds(sequenceStart + cellsReady - Time.time);
            isSpawnActive = false;
            if (isWaveActive && wave.clearType == WaveClearType.DefeatAllTargets && aliveTargets == 0)
                CompleteWave();
        }
        finally { ReleaseAfterSpawn(heldPreparation); }
    }

    private void InitializeSpawn(GameObject item, WaveSpawnInfo info)
    {
        foreach (var goal in item.GetComponentsInChildren<TutorialGoal>(true))
        {
            goal.ConfigureWaveGoal(info.goalKeyOverride, this, currentWave.clearType == WaveClearType.ReachGoal);
            if (currentWave.clearType == WaveClearType.ReachGoal) activeGoals.Add(goal);
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

        if (deadEnemy.Health.IsBossEnemy && deadEnemy.Health.currentHP <= 0f)
            bossDefeatedThisWave = true;

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

    public bool IsGoalActive(TutorialGoal goal) => goal != null && goal.isActiveAndEnabled && isWaveActive && currentWave != null
        && currentWave.clearType == WaveClearType.ReachGoal && activeGoals.Contains(goal);

    public bool CanReachGoal(TutorialGoal goal) => IsGoalActive(goal)
        && TutorialWaveGuide.CanComplete(this)
        && (GameManager.Instance == null || GameManager.Instance.Player == null || !GameManager.Instance.Player.Health.IsDead);

    public void HandleGoalReached(TutorialGoal goal)
    {
        if (CanReachGoal(goal)) CompleteWave();
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
        // 0초 배치를 기다리던 상자가 남아 있었으면 이 잠금(또는 해제)이 이어받으므로 남긴 상자 번호는 비운다.
        heldUntilSpawn = 0;
        if (currentWaveIndex + 1 < waveData.waves.Length && currentWaveEntities.Exists(e => e is TrainingCourseActivator)) PlayerStartSequence.Hold();
        else ReleasePlayer();
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
            GameManager.Instance.Player.ResetPlayerStatus(preserveExecutingAttack: bossDefeatedThisWave);
            WorldEntityPool.Instance.DespawnAll(preservePlayerAttacks: bossDefeatedThisWave);

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
    private void BeginTutorialStoragePractice(Wave wave)
    {
        if (wave.clearType != WaveClearType.DefeatAllTargets || wave.tutorial == null || !wave.tutorial.enabled
            || wave.tutorial.action != TutorialAction.SkillStorage) return;
        tutorialStorageAbility = GameManager.Instance?.Player?.AbilityManager?.AttackAbility;
        if (tutorialStorageAbility == null) return;
        tutorialStorageAbility.SkillStorageFinished += OnTutorialSkillStored;
        tutorialStorageAbility.StoredSkillUsed += OnTutorialStoredSkillUsed;
    }

    private void OnTutorialSkillStored(bool completed) => TutorialSkillStored |= completed;

    private void OnTutorialStoredSkillUsed()
    {
        if (!TutorialSkillStored) return;
        TutorialStoredSkillUsed = true;
        ReleaseTutorialStorageTargets();
    }

    private void ReleaseTutorialStorageTargets()
    {
        foreach (var target in tutorialStorageTargets)
            if (target != null) target.SetMinimumHealth(0f);
        tutorialStorageTargets.Clear();
    }

    private void StopTutorialPractice()
    {
        var restoreArmament = restoreTutorialArmament;
        restoreTutorialArmament = null;
        restoreArmament?.Invoke();
        if (tutorialStorageAbility != null)
        {
            tutorialStorageAbility.SkillStorageFinished -= OnTutorialSkillStored;
            tutorialStorageAbility.StoredSkillUsed -= OnTutorialStoredSkillUsed;
            tutorialStorageAbility = null;
        }
        ReleaseTutorialStorageTargets();
        TutorialSkillStored = TutorialStoredSkillUsed = false;
    }

    private void ClearCurrentWaveEntities()
    {
        StopTutorialPractice();
        activeGoals.Clear();
        foreach (var enemy in currentWaveEnemies)
            if (enemy != null && enemy.gameObject.activeInHierarchy) EnemyPool.Instance.Despawn(enemy);
        currentWaveEnemies.Clear();
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

    // 던전 방(DungeonEncounter)이 적 1체를 지금 Wave의 적으로 생성한다. Wave가 끝나면 다른 적과 함께 회수된다(keepAfterWave 제외).
    // 처치 처리기(SetWaveDeathHandler)는 부른 쪽이 건다. 보스면 보스 등장 카메라를 이 WaveManager에서 실행한다(Wave 종료·패배 때 함께 멈춘다).
    // 던전 진행 자체는 DungeonManager가 맡고, 이 클래스에는 생성 API 하나만 둔다.
    public EnemyController SpawnEnemy(WaveSpawnInfo info, Vector3 worldPosition)
    {
        if (info == null || info.prefab == null || EnemyPool.Instance == null) return null;
        if (!info.prefab.TryGetComponent(out EnemyController enemyPrefab)) return null;
        EnemyController enemyInstance = EnemyPool.Instance.Spawn(enemyPrefab, worldPosition, info.isBossEnemy);
        if (enemyInstance == null) return null;
        if (!info.keepAfterWave) currentWaveEnemies.Add(enemyInstance);
        else currentWaveEnemies.Remove(enemyInstance);
        EnemySpawned?.Invoke(enemyInstance);
        if (info.isBossEnemy && RoomManager.Instance != null && RoomManager.Instance.CurrentRoom != null)
            StartCoroutine(RoomManager.Instance.CurrentRoom.ActivateBossEntryCamera(enemyInstance.transform));
        return enemyInstance;
    }
    // Wave가 진행 중인지(시작 뒤 클리어·패배·초기화 전). 던전 진행이 방 이동·적 생성 가능 여부를 읽는다.
}
