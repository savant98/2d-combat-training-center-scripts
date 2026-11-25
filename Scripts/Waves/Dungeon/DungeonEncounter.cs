using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 방 하나의 적 구성(Enemy 템플릿 Wave 항목)을 시각 순서대로 생성하고 필수 적 처치를 센다(DESIGN.md §2.3).
/// 시간 규칙은 WaveManager.SpawnRoutine과 같다: 시작 대기 뒤 항목을 차례로 놓고, 항목의 spawnDelay는 그 항목 '뒤'의 대기다.
/// 적은 WaveManager.SpawnEnemy(지금 Wave의 적으로 등록 → Wave가 끝나면 함께 회수), 장치는 WorldEntityPool + 배치별 설정.
/// 생성을 마쳤고 남은 필수 적이 0이면 <see cref="Cleared"/>를 한 번 알린다.
/// v2: 필수 적 수를 처음부터 안다(<see cref="RequiredTotal"/>, 아직 나오지 않은 것 = <see cref="RequiredPending"/>).
/// UI 상태 띠의 'HOSTILES n'은 <see cref="RequiredRemaining"/>(= 아직 안 나온 필수 + 살아 있는 필수)를 쓴다.
/// 4차(DESIGN_V4 §2.8): 동적 구성은 <see cref="DungeonEncounterPacing"/>을 받아 '파도'(spawnDelay가 0인 항목이 이어지는 묶음) 단위로 생성한다 —
/// 살아 있는 적 + 다음 파도가 동시 생존 상한을 넘으면 대기, 파도의 적이 Player에 너무 가까우면 잠깐 대기(한도를 넘기면 relocate 대리자로 먼 자리를 다시 고름),
/// 같은 Bulwark가 살아 있으면 둘째는 쓰러지거나 일정 시간 뒤.
/// 순찰(재입장, <see cref="IsBypass"/>)은 같은 방식으로 돌고 처치를 세지만 잠금·클리어 처리는 DungeonManager가 건너뛴다. pacing이 없으면 3차 그대로다.
/// </summary>
public sealed class DungeonEncounter
{
    private readonly WaveManager waves;
    private readonly WaveSpawnInfo[] items;
    private readonly Vector3 origin;
    private readonly float startDelay;
    private readonly Transform looseParent;
    private readonly DungeonEncounterPacing pacing;
    private readonly List<EnemyController> enemies = new();
    private readonly List<(WorldEntityBase entity, uint lease)> entities = new();
    private readonly List<GameObject> loose = new();
    private readonly HashSet<EnemyController> required = new();
    // 살아 있는 필수 적(생성 순서). 마커가 읽는 목록이라 HashSet과 따로 순서를 지킨다.
    private readonly List<EnemyController> requiredList = new();
    // 4차: 생성한 적과 그 Prefab(같은 Bulwark가 살아 있는지 보는 데만 쓴다).
    private readonly List<(EnemyController enemy, GameObject source)> spawnedFrom = new();
    // 4차 통합: 마지막으로 soloPrefab(Bulwark)을 낸 시각. 둘째의 대기는 이 시각부터 잰다(구성기가 이미 6초 뒤 파도에 두므로 대기가 겹쳐 12초가 되지 않게).
    private float lastSoloSpawnTime = float.NegativeInfinity;
    private int requiredAlive;
    private int requiredPending;
    private bool spawningDone;
    private bool stopped;
    private bool cleared;

    /// <summary>생성을 마쳤고 필수 적을 모두 처치했다(한 번만).</summary>
    public event Action Cleared;
    /// <summary>남은 필수 적 수나 살아 있는 필수 적 목록, 생성 완료 여부가 바뀌었다(남은 수, 전체 수). 멈춘 뒤에는 알리지 않는다.</summary>
    public event Action<int, int> RequiredChanged;
    /// <summary>적 하나를 생성했다(적, 생성 위치 월드). 생성 연출 앵커(IDungeonSpawnAnchor) 연결용.</summary>
    public event Action<EnemyController, Vector3> EnemySpawned;

    public bool IsCleared => cleared;
    public bool IsStopped => stopped;
    public int RequiredAlive => requiredAlive;
    /// <summary>적 구성의 필수 적 전체 수(생성자에서 항목을 세어 정한다).</summary>
    public int RequiredTotal { get; }
    /// <summary>아직 생성하지 않은 필수 적 수.</summary>
    public int RequiredPending => requiredPending;
    /// <summary>남은 필수 적 = 아직 안 나온 것 + 살아 있는 것.</summary>
    public int RequiredRemaining => requiredPending + requiredAlive;
    /// <summary>항목 생성을 모두 마쳤는지.</summary>
    public bool SpawningDone => spawningDone;
    /// <summary>지금 살아 있는 이 방의 적(필수·비필수).</summary>
    public IReadOnlyList<EnemyController> Enemies => enemies;
    /// <summary>지금 살아 있는 필수 적(생성 순서).</summary>
    public IReadOnlyList<EnemyController> RequiredEnemies => requiredList;
    /// <summary>클리어한 방 재입장의 순찰 구성인지(4차 R18 — 잠그지 않고 클리어 수·RoomCleared에 세지 않는다. 판단은 DungeonManager).</summary>
    public bool IsBypass { get; }
    /// <summary>동적 구성의 생성 속도 규칙(없으면 null — 손 배치 구성).</summary>
    public DungeonEncounterPacing Pacing => pacing;
    /// <summary>지금 동시 생존 상한 때문에 다음 파도를 기다리는 중인지(검사용).</summary>
    public bool IsHoldingForCap { get; private set; }
    /// <summary>지금까지 살아 있던 적 수의 최댓값(검사용 — 동시 생존 상한 확인).</summary>
    public int PeakAlive { get; private set; }
    /// <summary>실제로 생성된 필수 적 수(생성 실패는 세지 않는다). 0이면 싸운 적 없이 클리어된 구성이다(난이도 첫 클리어에 세지 않음 — DungeonManager).</summary>
    public int RequiredSpawned { get; private set; }
    /// <summary>Player 가까움 대기 뒤 자리를 다시 골라 낸 파도 수(검사용).</summary>
    public int RelocatedWaves { get; private set; }

    public DungeonEncounter(WaveManager waves, WaveSpawnInfo[] items, Vector3 origin, float startDelay, Transform looseParent)
        : this(waves, items, origin, startDelay, looseParent, null, false) { }

    /// <summary>4차: 동적 구성(pacing)·순찰(bypass)용. pacing이 null이면 3차와 같은 순서·시간으로 돈다.</summary>
    public DungeonEncounter(WaveManager waves, WaveSpawnInfo[] items, Vector3 origin, float startDelay, Transform looseParent, DungeonEncounterPacing pacing, bool bypass)
    {
        this.waves = waves;
        this.items = items ?? Array.Empty<WaveSpawnInfo>();
        this.origin = origin;
        this.startDelay = Mathf.Max(0f, startDelay);
        this.looseParent = looseParent;
        this.pacing = pacing;
        IsBypass = bypass;
        int total = 0;
        foreach (var info in this.items) if (IsRequiredEnemy(info)) total++;
        RequiredTotal = total;
        requiredPending = total;
    }

    /// <summary>항목이 처치 필수 적인지(Prefab에 EnemyController가 있고 isRequiredToClear).</summary>
    public static bool IsRequiredEnemy(WaveSpawnInfo info)
        => info != null && info.isRequiredToClear && info.prefab != null && info.prefab.GetComponent<EnemyController>() != null;

    /// <summary>항목을 생성한다. 부르는 쪽(DungeonManager)의 코루틴으로 돌린다.</summary>
    public IEnumerator Run()
    {
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);
        if (pacing == null)
        {
            foreach (var info in items)
            {
                if (ShouldStop()) yield break;
                if (info == null || info.prefab == null) continue;
                Spawn(info);
                if (info.spawnDelay > 0f) yield return new WaitForSeconds(info.spawnDelay);
            }
        }
        else
        {
            // 파도 = spawnDelay가 0인 항목이 이어지다 0보다 큰 항목(그 파도의 마지막)에서 끝나는 묶음.
            int index = 0;
            while (index < items.Length)
            {
                if (ShouldStop()) { IsHoldingForCap = false; yield break; }
                int end = index;
                while (end < items.Length - 1 && (items[end] == null || items[end].spawnDelay <= 0f)) end++;
                float waitedClose = 0f, waitedSolo = 0f;
                int relocations = 0;
                // 이 파도의 다시 고른 자리(방 로컬, 파도 항목 순서). null이면 구성 자리 그대로.
                Vector2[] moved = null;
                while (true)
                {
                    if (ShouldStop()) { IsHoldingForCap = false; yield break; }
                    bool capFull = CapFull(index, end);
                    IsHoldingForCap = capFull;
                    bool near = WaveTooClose(index, end, moved);
                    // 4차 검토: 가까움 대기 한도를 넘기면 구성 자리 그대로 내지 않고, 같은 방 후보에서 Player에서 먼 자리를 다시 고른다(동적 구성의 대리자만).
                    // 다 옮기지 못하면 한도만큼 더 미룬 뒤 다시 고른다. 시도를 다 쓰면(작은 방 등) 그때의 자리로 낸다 — 방이 잠긴 채 남지 않게.
                    if (near && waitedClose >= pacing.closeWaitLimit && pacing.relocate != null && relocations < pacing.relocateAttempts)
                    {
                        moved ??= WavePositions(index, end);
                        TryRelocateWave(index, end, relocations++, moved);
                        waitedClose = 0f;
                        near = WaveTooClose(index, end, moved);
                        if (!near) RelocatedWaves++;
                    }
                    bool close = near && waitedClose < pacing.closeWaitLimit;
                    bool solo = waitedSolo < pacing.soloWaitLimit && Time.time - lastSoloSpawnTime < pacing.soloWaitLimit && SoloBlocked(index, end);
                    if (!capFull && !close && !solo) break;
                    if (close) waitedClose += Time.deltaTime;
                    if (solo) waitedSolo += Time.deltaTime;
                    yield return null;
                }
                IsHoldingForCap = false;
                for (int i = index; i <= end; i++)
                {
                    if (ShouldStop()) yield break;
                    var info = items[i];
                    if (info == null || info.prefab == null) continue;
                    Spawn(info, moved != null ? moved[i - index] : info.spawnPosition);
                }
                PeakAlive = Mathf.Max(PeakAlive, AliveEnemies());
                float delay = items[end] != null ? items[end].spawnDelay : 0f;
                index = end + 1;
                if (delay > 0f && index < items.Length) yield return new WaitForSeconds(delay);
            }
        }
        if (ShouldStop()) yield break;
        spawningDone = true;
        // 생성 실패 등으로 남은 대기 수가 있으면 0으로 맞춘다(없는 적을 기다리지 않게).
        requiredPending = 0;
        NotifyChanged();
        TryClear();
    }

    /// <summary>
    /// 생성을 멈춘다. despawn이면 살아 있는 적과 장치도 거둔다(immediate면 장치를 바로 풀로, 아니면 퇴장 연출). 그 뒤의 처치는 세지 않는다.
    /// </summary>
    public void Stop(bool despawn, bool immediate)
    {
        stopped = true;
        if (!despawn) return;
        foreach (var enemy in enemies.ToArray())
        {
            if (enemy == null) continue;
            enemy.ClearWaveDeathHandler();
            if (EnemyPool.Instance != null && enemy.gameObject.activeInHierarchy) EnemyPool.Instance.Despawn(enemy);
        }
        enemies.Clear();
        required.Clear();
        requiredList.Clear();
        spawnedFrom.Clear();
        RetractDevices(immediate);
    }

    /// <summary>이 구성이 놓은 장치(함정 등)만 거둔다. 방을 클리어하면 퇴장 연출로 접는다.</summary>
    public void RetractDevices(bool immediate)
    {
        foreach (var (entity, lease) in entities)
        {
            if (entity == null || entity.PoolLease != lease || !entity.gameObject.activeSelf || WorldEntityPool.Instance == null) continue;
            if (immediate && WorldEntityPool.Instance != null && entity.poolPrefab != null) WorldEntityPool.Instance.Return(entity.poolPrefab, entity);
            else entity.ForceReturnToPool();
        }
        entities.Clear();
        foreach (var item in loose) if (item != null) Object.Destroy(item);
        loose.Clear();
    }

    // ── 4차 파도 규칙 ──
    // 살아 있는 이 구성의 적 + 이 파도의 적이 상한을 넘는지(살아 있는 적이 없으면 넘어도 낸다 — 한 파도가 상한보다 커도 멈추지 않게).
    private bool CapFull(int from, int to)
    {
        if (pacing == null || pacing.concurrentCap <= 0) return false;
        int alive = AliveEnemies();
        if (alive <= 0) return false;
        return alive + EnemiesIn(from, to) > pacing.concurrentCap;
    }

    // 이 파도의 적 생성 위치 중 하나라도 지금 Player에 너무 가까운지(부르는 쪽 판정, 물리 질의 없음). moved가 있으면 다시 고른 자리로 본다.
    private bool WaveTooClose(int from, int to, Vector2[] moved)
    {
        if (pacing?.tooClose == null) return false;
        for (int i = from; i <= to; i++)
        {
            var info = items[i];
            if (info == null || info.prefab == null || info.prefab.GetComponent<EnemyController>() == null) continue;
            Vector3 position = origin + (Vector3)(moved != null ? moved[i - from] : info.spawnPosition);
            position.z = 0f;
            bool near;
            try { near = pacing.tooClose(position); }
            catch (Exception e) { Debug.LogException(e); return false; }
            if (near) return true;
        }
        return false;
    }

    // 파도 항목의 구성 자리(방 로컬) 사본.
    private Vector2[] WavePositions(int from, int to)
    {
        var result = new Vector2[to - from + 1];
        for (int i = from; i <= to; i++) result[i - from] = items[i] != null ? items[i].spawnPosition : Vector2.zero;
        return result;
    }

    // 대리자로 파도 자리를 다시 고른다(부르는 쪽 예외는 기록하고 실패로 본다). 대리자는 옮긴 항목의 자리만 positions에 쓴다.
    private bool TryRelocateWave(int from, int to, int attempt, Vector2[] positions)
    {
        try { return pacing.relocate(items, from, to, attempt, positions); }
        catch (Exception e) { Debug.LogException(e); return false; }
    }

    // 이 파도에 soloPrefab이 있고, 같은 Prefab에서 나온 적이 아직 살아 있는지.
    private bool SoloBlocked(int from, int to)
    {
        var solo = pacing?.soloPrefab;
        if (solo == null) return false;
        bool inWave = false;
        for (int i = from; i <= to && !inWave; i++) inWave = items[i] != null && items[i].prefab == solo;
        if (!inWave) return false;
        foreach (var (enemy, source) in spawnedFrom)
            if (source == solo && enemy != null && enemy.gameObject.activeInHierarchy && enemies.Contains(enemy)) return true;
        return false;
    }

    private int AliveEnemies()
    {
        int alive = 0;
        foreach (var enemy in enemies) if (enemy != null && enemy.gameObject.activeInHierarchy) alive++;
        return alive;
    }

    private int EnemiesIn(int from, int to)
    {
        int count = 0;
        for (int i = from; i <= to; i++)
            if (items[i] != null && items[i].prefab != null && items[i].prefab.GetComponent<EnemyController>() != null) count++;
        return count;
    }

    private bool ShouldStop()
    {
        if (stopped || waves == null || !waves.IsWaveActive) return true;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        return player != null && player.Health != null && player.Health.IsDead;
    }

    private void Spawn(WaveSpawnInfo info) => Spawn(info, info.spawnPosition);

    // local = 방 로컬 생성 자리(보통 info.spawnPosition, 파도 자리 다시 고르기면 새 자리). 항목 자체는 고치지 않는다.
    private void Spawn(WaveSpawnInfo info, Vector2 local)
    {
        Vector3 position = origin + (Vector3)local;
        position.z = 0f;
        GameObject spawned = null;
        if (info.prefab.GetComponent<EnemyController>() != null)
        {
            bool isRequired = info.isRequiredToClear;
            if (isRequired) requiredPending = Mathf.Max(0, requiredPending - 1);
            var enemy = waves.SpawnEnemy(info, position);
            if (enemy == null)
            {
                // 생성하지 못한 필수 적은 처치한 것으로 본다(없는 적 때문에 방이 잠긴 채 남지 않게, v1과 같은 결과).
                if (isRequired) { NotifyChanged(); TryClear(); }
                return;
            }
            enemies.Add(enemy);
            if (pacing?.soloPrefab != null && info.prefab == pacing.soloPrefab) { spawnedFrom.Add((enemy, info.prefab)); lastSoloSpawnTime = Time.time; }
            if (isRequired)
            {
                RequiredSpawned++;
                requiredAlive++;
                required.Add(enemy);
                requiredList.Add(enemy);
                enemy.SetWaveDeathHandler(() => OnRequiredGone(enemy));
            }
            else enemy.SetWaveDeathHandler(() => OnOptionalGone(enemy));
            spawned = enemy.gameObject;
            Arm(spawned, info.lifetime);
            if (isRequired) NotifyChanged();
            RaiseSpawned(enemy, position);
            return;
        }
        if (info.prefab.TryGetComponent(out WorldEntityBase prefab) && WorldEntityPool.Instance != null)
        {
            var entity = WorldEntityPool.Instance.Spawn(prefab, position, info.Rotation);
            if (entity == null) return;
            WaveDeviceSettings.Apply(entity.gameObject, info.deviceSettings);
            entities.Add((entity, entity.PoolLease));
            spawned = entity.gameObject;
            // 장치(WorldEntity)는 클리어 조건에 넣지 않는다(WaveManager도 감소 경로가 없다). 보고서 참고.
        }
        else
        {
            spawned = Object.Instantiate(info.prefab, position, info.Rotation, looseParent);
            loose.Add(spawned);
        }
        Arm(spawned, info.lifetime);
    }

    // 수명(초)이 있는 항목은 WaveSpawnLifetime으로 만료시킨다(WaveManager.Expire와 같은 규칙: 필수 적의 만료는 처치로 센다).
    private void Arm(GameObject item, float lifetime)
    {
        if (item == null) return;
        var timer = item.GetComponent<WaveSpawnLifetime>();
        if (lifetime <= 0f) { if (timer != null) timer.Arm(0f, null); return; }
        if (timer == null) timer = item.AddComponent<WaveSpawnLifetime>();
        timer.Arm(lifetime, () => Expire(item));
    }

    private void Expire(GameObject item)
    {
        if (item == null || !item.activeInHierarchy) return;
        if (item.TryGetComponent(out EnemyController enemy))
        {
            if (!enemies.Contains(enemy)) return;
            // 처치 처리기를 그대로 불러 필수 적이면 처치로 센다.
            bool isRequired = required.Contains(enemy);
            enemy.ClearWaveDeathHandler();
            if (isRequired) OnRequiredGone(enemy);
            else OnOptionalGone(enemy);
        }
        else if (item.TryGetComponent(out WorldEntityBase entity)) entity.ForceReturnToPool();
        else Object.Destroy(item);
    }

    private void OnRequiredGone(EnemyController enemy)
    {
        if (EnemyPool.Instance != null) EnemyPool.Instance.Despawn(enemy);
        if (!enemies.Remove(enemy)) return;
        required.Remove(enemy);
        requiredList.Remove(enemy);
        if (stopped) return;
        requiredAlive = Mathf.Max(0, requiredAlive - 1);
        NotifyChanged();
        TryClear();
    }

    private void OnOptionalGone(EnemyController enemy)
    {
        if (EnemyPool.Instance != null) EnemyPool.Instance.Despawn(enemy);
        enemies.Remove(enemy);
    }

    private void TryClear()
    {
        if (cleared || stopped || !spawningDone || requiredAlive > 0) return;
        if (waves == null || !waves.IsWaveActive) return;
        cleared = true;
        Cleared?.Invoke();
    }

    // UI 쪽 처리기에서 예외가 나도 생성·처치 흐름은 이어진다.
    private void NotifyChanged()
    {
        if (stopped || RequiredChanged == null) return;
        try { RequiredChanged.Invoke(RequiredRemaining, RequiredTotal); }
        catch (Exception e) { Debug.LogException(e); }
    }

    private void RaiseSpawned(EnemyController enemy, Vector3 position)
    {
        if (stopped || EnemySpawned == null) return;
        try { EnemySpawned.Invoke(enemy, position); }
        catch (Exception e) { Debug.LogException(e); }
    }
}

/// <summary>
/// 파도 자리 다시 고르기(4차 검토). DungeonEncounter가 Player 가까움 대기 한도를 넘긴 파도에 부른다.
/// items = 구성 항목 전체(방 로컬 spawnPosition — 고치지 않는다), [from, to] = 이번 파도, attempt = 이 파도에서 몇 번째 시도(0부터, 결정적 난수 시드에 섞는다),
/// positions = 파도 항목의 방 로컬 자리(길이 to − from + 1, 부르기 전 지금 자리로 채워 둔다 — 옮긴 항목만 바꾼다). 가까운 적이 남지 않으면 true.
/// </summary>
public delegate bool DungeonWaveRelocator(IReadOnlyList<WaveSpawnInfo> items, int from, int to, int attempt, Vector2[] positions);

/// <summary>
/// 동적 적 구성의 생성 속도 규칙(4차 DESIGN_V4 §2.8). DungeonManager가 규칙 SO(DungeonEncounterRulesSO)의 값으로 채워 DungeonEncounter에 넘긴다.
/// </summary>
public sealed class DungeonEncounterPacing
{
    /// <summary>살아 있는 이 구성의 적 상한(0 = 없음). 살아 있는 적 + 다음 파도의 적이 이 값을 넘으면 기다린다(살아 있는 적이 없으면 넘어도 낸다).</summary>
    public int concurrentCap;
    /// <summary>생성 위치(월드)가 지금 Player에 너무 가까운지(동적 구성의 실행 중 거리 제한). null이면 보지 않는다.</summary>
    public Func<Vector3, bool> tooClose;
    /// <summary>tooClose로 한 파도를 미루는 최대 시간(초). 지나면 relocate가 있으면 자리를 다시 고르고(실패하면 이만큼 더 미룸), 없으면 그대로 낸다.</summary>
    public float closeWaitLimit = 2f;
    /// <summary>파도 자리 다시 고르기(선택 — 동적 구성만, 손 배치 구성은 null로 3차 그대로). <see cref="DungeonWaveRelocator"/>.</summary>
    public DungeonWaveRelocator relocate;
    /// <summary>한 파도에서 relocate를 부르는 최대 횟수. 다 쓰면 한도만큼 더 기다린 뒤 그때의 자리로 낸다(방이 잠긴 채 남지 않게).</summary>
    public int relocateAttempts = 3;
    /// <summary>방에 하나씩 먼저 내는 Prefab(Bulwark). 같은 Prefab에서 나온 적이 살아 있으면 그 Prefab이 든 파도를 기다린다.</summary>
    public GameObject soloPrefab;
    /// <summary>soloPrefab 대기 최대 시간(초, Bulwark 둘째 — 첫째가 쓰러지거나 첫째를 낸 뒤 이 시간이 지나면).</summary>
    public float soloWaitLimit = 6f;
}
