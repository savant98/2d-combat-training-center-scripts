using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    public WaveDataSO Data { get; private set; }

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
        loading = StartCoroutine(LoadingRoutine(terrain, Data));
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
        yield return WarmPools(data, value => { LoadingProgress = TerrainShare + (1f - TerrainShare) * value; LoadingStep = "UNITS"; });
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
    private static IEnumerator WarmPools(WaveDataSO data, Action<float> report)
    {
        var enemies = new Dictionary<EnemyController, int>();
        var entities = new List<WorldEntityBase>();
        foreach (var wave in data.waves)
        {
            // 한 Wave에서 같은 적이 가장 많이 나오는 수만큼 준비한다(Wave가 넘어가면 풀로 돌아온다).
            var counts = new Dictionary<EnemyController, int>();
            foreach (var info in wave.spawnObjects ?? Array.Empty<WaveSpawnInfo>())
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

    public void Load(WaveDataSO data)
    {
        if (data == null) data = defaultData;
        if (Content != null && Data == data)
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
        Data = data;
        var template = data.roomTemplate != null ? data.roomTemplate : defaultTemplate;
        // 초기화 전에 참조와 WaveData를 복구하기 위해 비활성 부모에서 만든다.
        var staging = new GameObject("ContentStaging");
        staging.SetActive(false);
        staging.transform.SetParent(transform, false);
        Content = Instantiate(template, staging.transform);
        Content.name = "TrainingContent";
        Content.transform.localPosition = Vector3.zero;
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
        Content.waves.Configure(data, Content.spawnRoot, data.training);
        if (!data.customRoom)
        {
            foreach (var stage in Content.GetComponentsInChildren<TrainingCourseStage>(true)) stage.enabled = false;
            foreach (var layout in Content.GetComponentsInChildren<TrainingRoomLayoutController>(true)) layout.enabled = false;
        }
        if (data.cellMap.enabled)
        {
            foreach (var layout in Content.GetComponentsInChildren<TrainingRoomLayoutController>(true)) layout.enabled = false;
            Content.gameObject.AddComponent<WaveCellTerrain>().Configure(data, Content.waves);
        }
        else
        {
            var builder = Content.gameObject.AddComponent<WaveRoomBuilder>();
            builder.Configure(Content, data);
        }
        owner.BindContent(Content);
        entryPoint.localPosition = WaveRoomState.EntryPoint(data);
        Content.transform.SetParent(transform, false);
        Destroy(staging);
    }
}
