using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

// Scene에는 이 Host와 Room 하나만 남기고, WaveData가 선택한 모듈을 교체한다.
[DisallowMultipleComponent]
public sealed class TrainingRoomHost : MonoBehaviour, ILoadingSource
{
    [Serializable]
    public sealed class Binding
    {
        public TrainingRoomContent template;
        public string path;
        public string componentType;
        public int componentIndex;
        public string property;
        public UnityEngine.Object target;
    }
    public WaveDataSO defaultData;
    public TrainingRoomContent defaultTemplate;
    public Binding[] bindings = Array.Empty<Binding>();
    public Transform entryPoint;
    public TrainingRoomContent Content { get; private set; }
    // 고른(요청한) WaveData 원본. 던전 Wave가 있으면 실제 진행은 Dungeon.Data(실행 사본)로 하고, 이 값은 원본 그대로 둔다
    // (훈련 선택·회귀가 '입장한 WaveData = 고른 훈련'을 이 값으로 비교한다).
    public WaveDataSO Data { get; private set; }
    // 던전 Wave의 실행 데이터(입장마다 새 구조). 던전이 없거나 부팅 로드면 null. WaveManager·지형은 Dungeon.Data로 진행한다.
    public DungeonRun Dungeon { get; private set; }
    /// <summary>방 내용이 실제로 진행하는 WaveData(던전이면 실행 사본, 아니면 Data).</summary>
    public WaveDataSO PlayData => Dungeon != null && Dungeon.Data != null ? Dungeon.Data : Data;
    // Stage(엘리베이터 출발 때 미리 구성)로 만든 던전 내용을 아직 입장(Load)에 쓰지 않았는지. 같은 원본으로 Load하면 그대로 쓴다.
    private bool stagedDungeon;
    // 지금 방 내용을 만들 때 WaveData가 고른 방 배경(WaveDataSO.roomBackdrop). Tool에서 바꾼 뒤 같은 WaveData로 다시 들어오면 방 내용을 새로 만든다.
    private GameObject contentBackdrop;
    private const string BackdropName = "TrainingBackdrop";

    // ── 입장 로딩 ──
    // TrainingRoom에 들어올 때 한 번, Wave가 넘어갈 때 쓸 것(반복 배경 모듈, 지형 캐시, 적·장치 풀)을 미리 만들어 둔다.
    // 방 전환 연출은 이 동안 암전을 유지하고(RoomTransitionContext.WaitForLoading), Wave는 로딩이 끝난 뒤 시작한다(Room.ActivateRoom).
    // 같은 WaveData로 다시 들어오거나 재도전할 때는 이미 만들어 둔 것을 그대로 쓰므로 다시 하지 않는다.
    public bool IsLoading { get; private set; }
    /// <summary>입장 로딩 진행률(0~1).</summary>
    public float LoadingProgress { get; private set; }
    /// <summary>지금 하고 있는 로딩 단계 이름(TERRAIN·BACKDROP·UNITS).</summary>
    public string LoadingStep { get; private set; } = "";
    private Coroutine loading;
    // 진행률에서 지형·배경이 차지하는 몫. 나머지는 적·장치 풀이다.
    private const float TerrainShare = .6f;
    private const float PoolFrameBudget = .006f;
    private const int MaxEnemiesPerPrefab = 12;

    /// <summary>방 내용을 불러온 직후에 부른다. 아직 준비하지 않은 내용이면 입장 로딩을 시작한다.</summary>
    public void BeginLoading()
    {
        if (IsLoading || Content == null || !isActiveAndEnabled) return;
        var terrain = Content.GetComponent<WaveCellTerrain>();
        if (terrain == null || terrain.IsWarm) return;
        IsLoading = true; LoadingProgress = 0f; LoadingStep = "TERRAIN";
        loading = StartCoroutine(LoadingRoutine(terrain, PlayData));
    }
    private void CancelLoading()
    {
        if (loading != null) StopCoroutine(loading);
        loading = null; IsLoading = false;
    }
    // 이 오브젝트가 꺼지면 로딩 코루틴도 멈춘다. 로딩 중 상태로 남으면 다음 입장의 화면 전환이 끝나지 않으므로 여기서 푼다.
    // 마치지 못한 준비는 다음 입장 때 다시 한다(이미 만든 것은 건너뛴다).
    private void OnDisable() => CancelLoading();
    private IEnumerator LoadingRoutine(WaveCellTerrain terrain, WaveDataSO data)
    {
        // 표시를 켜지 못해도 로딩은 진행한다. 여기서 예외가 나가면 이 코루틴만 끝나고 로딩 중 상태가 남아 화면 전환이 끝나지 않는다.
        try { TrainingRoomLoadingPanel.Show(this); }
        catch (Exception exception) { Debug.LogException(exception); }
        // 도중에 문제가 생겨도 로딩 상태에 갇히지 않게 단계 전체를 감싸서 돌린다(Wave 시작과 화면 전환이 이 상태를 기다린다).
        yield return RunGuarded(LoadingSteps(terrain, data));
        LoadingProgress = 1f;
        loading = null; IsLoading = false;
    }
    private IEnumerator LoadingSteps(WaveCellTerrain terrain, WaveDataSO data)
    {
        // 방 내용이 켜진 첫 프레임(지형·배경의 첫 갱신)을 먼저 보낸다.
        yield return null;
        yield return terrain.Warm((value, step) => { LoadingProgress = value * TerrainShare; LoadingStep = step; });
        // 던전 방의 Prop·출입구·적 구성은 Wave 목록에 없으므로 방마다 묶어 함께 준비한다.
        var dungeon = Dungeon != null && Dungeon.Data == data ? Dungeon : null;
        yield return WarmPools(data, value => { LoadingProgress = TerrainShare + (1f - TerrainShare) * value; LoadingStep = "UNITS"; }, dungeon);
        WavePlayerStart.Prewarm();
    }
    // 중첩된 단계까지 직접 진행시키며 예외를 받아 낸다. 예외가 나면 남은 로딩을 건너뛴다(로딩 없이도 동작은 같다).
    private static IEnumerator RunGuarded(IEnumerator root)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            object current = null; bool moved = false, failed = false;
            try { moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current; }
            catch (Exception exception) { Debug.LogException(exception); failed = true; }
            if (failed) break;
            if (!moved) { stack.Pop(); continue; }
            if (current is IEnumerator nested) { stack.Push(nested); continue; }
            yield return current;
        }
    }
    // 이 WaveData의 Wave들이 꺼낼 적과 장치를 풀에 미리 만들어 둔다. 처음 등장하는 순간에 Prefab을 새로 만들지 않는다.
    private static IEnumerator WarmPools(WaveDataSO data, Action<float> report, DungeonRun dungeon = null)
    {
        var enemies = new Dictionary<EnemyController, int>();
        var entities = new List<WorldEntityBase>();
        // 한 묶음(Wave 하나, 던전이면 방 하나)씩 센다.
        var groups = new List<WaveSpawnInfo[]>();
        foreach (var wave in data.waves) groups.Add(wave.spawnObjects ?? Array.Empty<WaveSpawnInfo>());
        if (dungeon != null)
            foreach (var floor in dungeon.floors)
            {
                if (floor == null) continue;
                foreach (var room in floor.rooms)
                {
                    var items = new List<WaveSpawnInfo>(room.props);
                    items.AddRange(room.encounter);
                    foreach (var door in room.doors) if (door != null && door.Connected && door.prefab != null) items.Add(new WaveSpawnInfo { prefab = door.prefab, isRequiredToClear = false });
                    // 4차: 시작 방 출구 문(목표가 시작 방 출구 방식일 때)도 함께 준비한다.
                    if (floor.objective != null && floor.objective.UsesStartRoomExit && room.HasExit && floor.layout != null && room.Id == floor.layout.startRoom)
                        items.Add(new WaveSpawnInfo { prefab = floor.objective.ExitPrefab, isRequiredToClear = false });
                    groups.Add(items.ToArray());
                }
            }
        // 4차: 동적 적 구성은 입장 때 항목을 알 수 없으므로 예열 힌트(Prefab별 한 방 최악의 수, DungeonRun.PrewarmHints)를 한 묶음으로 센다.
        // 아래에서 방 묶음 수와 비교해 큰 쪽(Prefab당 최대 MaxEnemiesPerPrefab)을 쓴다.
        if (dungeon != null && dungeon.PrewarmHints.Count > 0)
        {
            var hinted = new List<WaveSpawnInfo>();
            foreach (var pair in dungeon.PrewarmHints)
                for (int k = 0; k < pair.Value; k++) hinted.Add(new WaveSpawnInfo { prefab = pair.Key, isRequiredToClear = false });
            groups.Add(hinted.ToArray());
        }
        foreach (var spawns in groups)
        {
            // 한 Wave에서 같은 적이 가장 많이 나오는 수만큼 준비한다(Wave가 넘어가면 풀로 돌아온다).
            var counts = new Dictionary<EnemyController, int>();
            foreach (var info in spawns)
            {
                if (info.prefab == null) continue;
                if (info.prefab.TryGetComponent(out EnemyController enemy)) { counts[enemy] = counts.TryGetValue(enemy, out int n) ? n + 1 : 1; continue; }
                if (!info.prefab.TryGetComponent(out WorldEntityBase entity)) continue;
                if (!entities.Contains(entity)) entities.Add(entity);
                // 장치가 꺼내는 것(코스 적 생성기의 적, 발사 장치의 발사체)도 함께 준비한다.
                foreach (var spawner in info.prefab.GetComponentsInChildren<CourseEnemySpawner>(true))
                    if (spawner.EnemyPrefab != null) counts[spawner.EnemyPrefab] = (counts.TryGetValue(spawner.EnemyPrefab, out int n) ? n : 0) + Mathf.Max(1, spawner.GroupSize);
                foreach (var spawner in info.prefab.GetComponentsInChildren<ObjectSpawner>(true))
                    if (spawner.entityBase != null && !entities.Contains(spawner.entityBase)) entities.Add(spawner.entityBase);
            }
            foreach (var row in counts) enemies[row.Key] = Mathf.Max(enemies.TryGetValue(row.Key, out int n) ? n : 0, Mathf.Min(row.Value, MaxEnemiesPerPrefab));
            // 추격 분쇄기는 배치마다 띠 폭이 달라, 그 폭의 몸체 Mesh·부품과 이 Wave에 함께 나오는 수만큼의 몸체를 미리 만들어 둔다.
            ChasingGrinder.WarmWave(spawns);
            // 6차 T15: 던전 분쇄 투입구가 빌려 쓰는 추격 분쇄기 몸체도 폭별로 미리 만든다(방 입장 때 Mesh를 새로 그리지 않게).
            PropShredder.WarmWave(spawns);
        }
        int total = entities.Count, done = 0;
        foreach (var row in enemies) total += row.Value;
        if (total == 0) { report?.Invoke(1f); yield break; }
        if (WorldEntityPool.Instance != null)
            foreach (var entity in entities)
            {
                // 한 종류를 채우면 여러 개를 한꺼번에 만들므로 종류마다 한 프레임을 쓴다.
                bool made = WorldEntityPool.Instance.Prewarm(entity);
                report?.Invoke(++done / (float)total);
                if (made) yield return null;
            }
        else done += entities.Count;
        if (EnemyPool.Instance == null) { report?.Invoke(1f); yield break; }
        float slice = Time.realtimeSinceStartup;
        foreach (var row in enemies)
            for (int i = 0; i < row.Value; i++)
            {
                EnemyPool.Instance.Prewarm(row.Key, row.Value, 1);
                report?.Invoke(++done / (float)total);
                if (Time.realtimeSinceStartup - slice < PoolFrameBudget) continue;
                yield return null; slice = Time.realtimeSinceStartup;
            }
        report?.Invoke(1f);
    }

    public void Load(WaveDataSO data) => LoadContent(data, true);

    // 다른 방에 있는 동안 교장 개체만 만들어 두고, 실제 입장 시 Room이 활성화한다.
    internal void Stage(WaveDataSO data) => LoadContent(data, false);

    private void LoadContent(WaveDataSO data, bool activateContent)
    {
        if (data == null) data = defaultData;
        // 기본 WaveData까지 비어 있으면(삭제·참조 끊김) 방을 만들 수 없다. 예외로 Room 초기화를 끊지 않고 알리기만 한다.
        if (data == null)
        {
            Debug.LogError("[TrainingRoomHost] 불러올 WaveData가 없습니다(요청과 defaultData가 모두 비어 있음).", this);
            return;
        }
        // 던전 Wave가 있으면 입장마다 원본의 실행 사본에 새 구조를 펼친다(원본 에셋은 바꾸지 않는다).
        // 부팅 로드(Room.Awake: 이 Host에 아직 방 내용이 없는 첫 로드)는 펼치지 않는다. 그때는 원본으로 방만 만들어 둔다.
        bool dungeon = Content != null && DungeonRunBuilder.HasDungeon(data);
        if (dungeon && !data.cellMap.enabled)
        {
            Debug.LogError($"[TrainingRoomHost] 던전 Wave는 셀 지형(cellMap.enabled)이 필요합니다. {data.name}을 원본 그대로 진행합니다.", data);
            dungeon = false;
        }
        if (dungeon)
        {
            // 엘리베이터가 출발 때 미리 구성(Stage)해 둔 같은 훈련의 던전이면 그대로 입장한다. 그 밖에는 재사용하지 않고 새로 만든다(매 입장 새 구조).
            if (stagedDungeon && Dungeon != null && Data == data && contentBackdrop == data.roomBackdrop)
            {
                if (activateContent) stagedDungeon = false;
                entryPoint.localPosition = WaveRoomState.EntryPoint(PlayData);
                return;
            }
        }
        else if (Content != null && Data == data && Dungeon == null && contentBackdrop == data.roomBackdrop)
        {
            // Tool에서 첫 Wave 좌표를 바꾼 뒤 같은 데이터로 다시 입장하는 경우도 갱신한다.
            entryPoint.localPosition = WaveRoomState.EntryPoint(data);
            return;
        }
        // 이전 내용의 입장 로딩이 진행 중이었으면 멈춘다(그 내용은 곧 없어진다).
        CancelLoading();
        var owner = GetComponent<Room>();
        owner.ReleaseContent();
        if (Content != null)
        {
            Content.gameObject.SetActive(false);
            Destroy(Content.gameObject);
        }
        ReleaseDungeon();
        var run = dungeon ? DungeonRunBuilder.Build(data, 0) : null;
        // 실제로 진행할 데이터. 던전이면 실행 사본이고, 만들지 못했으면 원본 그대로 진행한다.
        var play = run != null ? run.Data : data;
        Data = data;
        Dungeon = run;
        stagedDungeon = run != null && !activateContent;
        var template = play.roomTemplate != null ? play.roomTemplate : defaultTemplate;
        // 초기화 전에 참조와 WaveData를 복구하기 위해 비활성 부모에서 만든다.
        var staging = new GameObject("ContentStaging");
        staging.SetActive(false);
        staging.transform.SetParent(transform, false);
        Content = Instantiate(template, staging.transform);
        Content.name = "TrainingContent";
        Content.transform.localPosition = Vector3.zero;
        contentBackdrop = data.roomBackdrop;
        if (contentBackdrop != null) ReplaceBackdrop(Content.transform, contentBackdrop, staging.transform);
        foreach (var binding in bindings)
        {
            if (binding.template != template) continue;
            var node = string.IsNullOrEmpty(binding.path) ? Content.transform : Content.transform.Find(binding.path);
            if (node == null) continue;
            var type = Type.GetType(binding.componentType);
            if (type == null) continue;
            var components = node.GetComponents(type);
            if (binding.componentIndex >= components.Length) continue;
            // 직렬화 필드 경로는 Editor 이관 시 생성한 바인딩으로 런타임에 복구한다.
            WaveRoomBinding.Apply(components[binding.componentIndex], binding.property, binding.target);
        }
        Content.waves.Configure(play, Content.spawnRoot, play.training);
        if (!play.customRoom)
        {
            foreach (var stage in Content.GetComponentsInChildren<TrainingCourseStage>(true)) stage.enabled = false;
            foreach (var layout in Content.GetComponentsInChildren<TrainingRoomLayoutController>(true)) layout.enabled = false;
        }
        if (play.cellMap.enabled)
        {
            foreach (var layout in Content.GetComponentsInChildren<TrainingRoomLayoutController>(true)) layout.enabled = false;
            var terrain = Content.gameObject.AddComponent<WaveCellTerrain>();
            terrain.Configure(play, Content.waves);
            if (run != null)
            {
                // 모든 던전 방의 셀을 미리 만들고(방 이동 때 즉시 교체), 입장 로딩이 방마다의 배경 모듈도 미리 만들게 한다.
                terrain.RegisterCells(run.AllCells());
                terrain.AddPredictedSpaces(DungeonManager.PredictRoomSpaces(run));
                terrain.AddPredictedRoomKits(DungeonManager.PredictRoomKits(run));
                // 던전 진행은 WaveManager 이벤트를 듣는 별도 구성요소가 맡는다(지형이 먼저 이벤트를 받도록 지형 뒤에 붙인다).
                Content.waves.gameObject.AddComponent<DungeonManager>().Configure(run, Content.waves, terrain);
            }
        }
        else
        {
            var builder = Content.gameObject.AddComponent<WaveRoomBuilder>();
            builder.Configure(Content, play);
        }
        if (run == null && play.waves != null && play.waves.Length > 0)
            Content.waves.gameObject.AddComponent<WaveFieldTerminalHost>().Configure(Content.waves);
        owner.BindContent(Content);
        entryPoint.localPosition = WaveRoomState.EntryPoint(play);
        if (!activateContent) Content.gameObject.SetActive(false);
        Content.transform.SetParent(transform, false);
        Destroy(staging);
    }

    // 던전 실행 사본은 이 Host가 만든 런타임 데이터라 방 내용을 버릴 때(또는 Host가 없어질 때) 함께 없앤다(DontSave라 저절로 내려가지 않는다).
    private void ReleaseDungeon()
    {
        if (Dungeon != null && Dungeon.Data != null && Dungeon.Data != Dungeon.Source) Destroy(Dungeon.Data);
        Dungeon = null;
        stagedDungeon = false;
    }
    private void OnDestroy() => ReleaseDungeon();

    // WaveData가 고른 배경 Prefab을 방 내용의 'TrainingBackdrop' 자리에 끼운다(부모·형제 순서·위치·이름을 물려받는다).
    // 방 내용이 아직 꺼진 staging 안에 있을 때 바꾸므로, 뒤이어 붙는 반복 배경(WaveRoomBackdrop: 모듈·시차·화면 밖 정지·입장 로딩)과
    // 원본 배경 표시(WaveRoomBackground), 이름으로 배경을 찾는 잡기 연출 캡처(GrabCinematicBackdropView)가 모두 새 배경을 원본으로 삼는다.
    private static void ReplaceBackdrop(Transform content, GameObject prefab, Transform discard)
    {
        var old = content.Find(BackdropName);
        if (old == null)
        {
            Debug.LogWarning($"Room 콘텐츠에 '{BackdropName}'이 없어 방 배경({prefab.name})을 바꾸지 못했습니다. Room 원본 배경을 그대로 씁니다.", prefab);
            return;
        }
        if (!WaveRoomBackground.IsBackground(prefab))
        {
            Debug.LogError($"방 배경 Prefab({prefab.name})에는 카메라·Collider·Room·WaveManager를 포함할 수 없습니다. Room 원본 배경을 그대로 씁니다.", prefab);
            return;
        }
        var fresh = Instantiate(prefab, content, false).transform;
        fresh.name = BackdropName;
        fresh.localPosition = old.localPosition;
        fresh.SetSiblingIndex(old.GetSiblingIndex());
        // Room 템플릿이 중첩 배경에서 지워 둔 편집용 시차 부품(제약·구동 스크립트·기준점)은 새 배경에 있어도 같이 지운다.
        // 제약이 남아 있으면 WaveRoomBackdrop이 그 weight를 층의 시차 계수로 읽어 코드의 기본 계수와 달라진다.
        // 바로 뒤(같은 프레임)에 읽히므로 즉시 지운다. 아직 한 번도 켜지지 않은 개체라 Awake·OnDestroy는 불리지 않는다.
        foreach (var constraint in fresh.GetComponentsInChildren<PositionConstraint>(true)) DestroyImmediate(constraint);
        var parallax = fresh.GetComponent<LobbyBackdropParallax>();
        if (parallax != null) DestroyImmediate(parallax);
        var anchor = fresh.Find("ParallaxAnchor");
        if (anchor != null) DestroyImmediate(anchor.gameObject);
        // 옛 배경은 이름을 바꿔 곧 없어질 staging으로 옮긴다. Destroy는 프레임 끝에 처리되므로 제자리에 두면 같은 프레임의 Find에 먼저 걸린다.
        old.name = BackdropName + "_Replaced";
        old.gameObject.SetActive(false);
        old.SetParent(discard, false);
        Destroy(old.gameObject);
    }
}
