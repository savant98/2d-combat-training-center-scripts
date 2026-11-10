using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Tilemaps;

// 한 칸 데이터가 표시·충돌·전환의 유일한 원본이다. 기존 코스 Transform은 운동만 맡는다.
public sealed class WaveCellTerrain : MonoBehaviour
{
    private sealed class Channel
    {
        public int group;
        public Vector2 origin;
        public Transform parent;
        public Tilemap map;
        public TilemapCollider2D collider;
        public CompositeCollider2D composite;
    }
    private WaveDataSO data;
    private WaveManager waves;
    private readonly Dictionary<WaveMapCell, WaveTerrainCellView> pool = new();
    private readonly Dictionary<WaveMapCell, Channel> owners = new();
    private readonly Dictionary<(int group, int row, WaveCellKind kind, int style), Channel> channels = new();
    private readonly HashSet<WaveMapCell> active = new();
    private readonly HashSet<WaveMapCell> initial = new();
    private readonly HashSet<WaveMapCell> persistent = new();
    private readonly List<WaveCellBatch> temporary = new();
    private readonly Dictionary<WaveMapCell, int> generations = new();
    private readonly Dictionary<WaveMapCell, (float time, int token)> expiry = new();
    private readonly Dictionary<WaveMapCell, int> removing = new();
    private Tile oneWayTile;
    private readonly Dictionary<WaveMapCell, int> outlineGroups = new();
    private int nextOutlineGroup;
    private readonly Dictionary<WaveMapCell, Vector2> animationPivots = new();
    private sealed class TerrainOutline
    {
        public LineRenderer line;
        public Vector2[] points;
        public WaveMapCell[] sources;
    }
    private readonly List<TerrainOutline> terrainOutlines = new();
    public WaveDataSO Data => data;
    private readonly List<GameObject> customObjects = new();
    private Transform staticRoot;
    private CinemachineConfiner2D roomConfiner;
    private BoxCollider2D originalCameraBounds;
    public bool IsPreloaded { get; private set; }
    public bool IsTransitioning { get; private set; }
    public int PreloadedCount => pool.Count;
    public IReadOnlyCollection<WaveMapCell> ActiveCells => active;

    public void Configure(WaveDataSO source, WaveManager manager)
    {
        data = source; waves = manager;
        var content = GetComponent<TrainingRoomContent>();
        roomConfiner = content != null && content.roomCamera != null ? content.roomCamera.GetComponent<CinemachineConfiner2D>() : null;
        if (roomConfiner == null) roomConfiner = GetComponentsInChildren<CinemachineConfiner2D>(true).FirstOrDefault(c => c.name == "VC");
        if (roomConfiner != null) originalCameraBounds = roomConfiner.BoundingShape2D as BoxCollider2D;
        foreach (var map in GetComponentsInChildren<Tilemap>(true))
        {
            var collision = map.GetComponent<TilemapCollider2D>();
            if (collision == null) continue;
            var render = map.GetComponent<TilemapRenderer>(); if (render != null) render.enabled = false;
            map.ClearAllTiles();
            if (collision != null) { collision.compositeOperation = Collider2D.CompositeOperation.None; collision.enabled = false; }
            var composite = map.GetComponent<CompositeCollider2D>();
            if (composite != null)
            {
                // 원본 Composite의 저장된 형상은 재사용하지 않는다. 이동 Transform만 보존한다.
                if (composite.attachedRigidbody != null) composite.attachedRigidbody.simulated = false;
                Destroy(composite);
            }
        }
        foreach (var line in GetComponentsInChildren<LineRenderer>(true))
            if (line.name == "PlatformLine" || line.name.StartsWith("PlatformLine (")) line.enabled = false;
        var background = gameObject.AddComponent<WaveRoomBackground>(); background.Configure(data);
        staticRoot = new GameObject("CellTerrain").transform; staticRoot.SetParent(transform, false);
        oneWayTile = ScriptableObject.CreateInstance<Tile>();
        oneWayTile.colliderType = Tile.ColliderType.Grid;
        var all = new HashSet<WaveMapCell>(data.cellMap.cells);
        foreach (var wave in data.waves)
        {
            all.UnionWith(wave.cells);
            foreach (var batch in wave.cellBatches) all.UnionWith(batch.cells);
            foreach (var entry in wave.terrainEvents ?? Array.Empty<WaveTerrainEvent>())
                foreach (var position in entry.cells) all.Add(new WaveMapCell { position = position, kind = entry.cellKind, style = entry.cellStyle });
        }
        foreach (var cell in all.OrderBy(c => c.group).ThenBy(c => c.position.y).ThenBy(c => c.position.x)) Preload(cell);
        IsPreloaded = true;
        waves.WavesReset += ResetMap; waves.WaveStarted += BeginEvents; waves.WaveEnded += EndEvents; waves.EnemySpawned += FaceCenter;
        ResetMap();
    }

    public void ApplyBackground() => GetComponent<WaveRoomBackground>()?.Apply(data.backgroundPrefab);
    private WaveCellStyle Style(WaveMapCell cell) => data.cellMap.styles[Mathf.Clamp(cell.style, 0, data.cellMap.styles.Length - 1)];
    private void RefreshOutlines()
    {
        foreach (var outline in terrainOutlines) if(outline.line != null) outline.line.enabled=false;
        terrainOutlines.Clear();
        foreach(var cell in active)
            if(cell.kind==WaveCellKind.Platform) pool[cell].SetOutline(0);
            else foreach(var line in pool[cell].edges) line.enabled=line.name!="SurfaceLine";
        foreach(var group in active.Where(c=>c.kind==WaveCellKind.Platform && Style(c).color.a > 0)
            .GroupBy(c=>(c.group,c.style,assembly:Style(c).generated && outlineGroups.TryGetValue(c,out int id)?id:0)))
        {
            var cells=group.OrderBy(c=>c.position.y).ThenBy(c=>c.position.x).ToArray();
            var cellByPosition=cells.ToDictionary(c=>c.position);
            var rules=WaveRoomRulesSO.Default;
            bool generated=Style(cells[0]).generated;
            float inset=generated?0:rules != null?rules.terrainLineInset:.025f;
            int rendererIndex=0;
            foreach(var contour in WaveCellContours.Trace(cells.Select(c=>c.position),inset,generated))
            {
                // 연결된 덩어리에 연속 선을 써 원본 UV·발광 패턴을 보존한다.
                var owner=cells[rendererIndex++];var line=pool[owner].edges[0];
                var center=(Vector2)owner.position+Vector2.one*.5f;
                if (!generated && rules != null)
                {
                    line.sharedMaterial=rules.terrainLineMaterial;
                    line.widthMultiplier=rules.terrainLineWidth;
                    line.sortingOrder=Style(owner).sortingOrder;
                    line.transform.SetParent(owners[owner].parent,false);
                    line.transform.localPosition=Vector3.zero;
                    // 직선 중간 칸도 자신의 양 끝을 움직인다. 셀 경계의 점을 생략하면 선만 먼저 완성된다.
                    var points=new List<Vector2>();var sources=new List<WaveMapCell>();
                    for(int i=0;i<contour.Length;i++)
                    {
                        var a=contour[i];var b=contour[(i+1)%contour.Length];var middle=(a+b)*.5f;
                        var direction=(b-a).normalized;
                        var key=Vector2Int.FloorToInt(middle+new Vector2(-direction.y,direction.x)*.001f);
                        var source=cellByPosition.TryGetValue(key,out var found)?found:cells.OrderBy(c=>Vector2.SqrMagnitude((Vector2)c.position+Vector2.one*.5f-middle)).First();
                        points.Add(a);points.Add(b);sources.Add(source);sources.Add(source);
                    }
                    terrainOutlines.Add(new TerrainOutline{line=line,points=points.ToArray(),sources=sources.ToArray()});
                    line.positionCount=points.Count;line.loop=true;line.enabled=true;continue;
                }
                line.positionCount=contour.Length;line.SetPositions(contour.Select(p=>(Vector3)(p-center)).ToArray());line.loop=true;line.enabled=true;
            }
        }
        foreach(var row in active.Where(c=>c.kind==WaveCellKind.OneWayPlatform).GroupBy(c=>(c.group,c.style,c.position.y,assembly:outlineGroups.TryGetValue(c,out int id)?id:0)))
        {
            var run=new List<WaveMapCell>();
            void Finish()
            {
                if(run.Count==0)return;var owner=run[0];var line=pool[owner].edges.First(l=>l.name=="SurfaceLine");
                line.positionCount=2;line.SetPositions(new[]{new Vector3(-.5f,.5f),new Vector3(run[run.Count-1].position.x-owner.position.x+.5f,.5f)});line.loop=false;line.enabled=true;run.Clear();
            }
            foreach(var cell in row.OrderBy(c=>c.position.x)){if(run.Count>0&&cell.position.x!=run[run.Count-1].position.x+1)Finish();run.Add(cell);}Finish();
        }
        UpdateTerrainOutlines();
    }
    private void LateUpdate() => UpdateTerrainOutlines();
    private void UpdateTerrainOutlines()
    {
        foreach(var outline in terrainOutlines)
            for(int i=0;i<outline.points.Length;i++)
            {
                var source=outline.sources[i];var view=pool[source];
                var local=outline.points[i]-(Vector2)source.position-Vector2.one*.5f;
                outline.line.SetPosition(i,outline.line.transform.InverseTransformPoint(view.transform.TransformPoint(local)));
            }
    }
    private Channel GetChannel(WaveMapCell cell)
    {
        var key = (cell.group, cell.kind == WaveCellKind.OneWayPlatform ? cell.position.y : int.MinValue, cell.kind, cell.style);
        if (channels.TryGetValue(key, out var cached)) return cached;
        Transform anchor = staticRoot;
        Vector2 origin = Vector2.zero;
        if (cell.group > 0 && cell.group <= data.cellMap.groups.Length)
        {
            var binding = data.cellMap.groups[cell.group - 1];
            anchor = transform.Find(binding.path); if (anchor == null) anchor = staticRoot; origin = binding.origin;
        }
        var node = new GameObject($"{cell.kind}_{cell.group}_{key.Item2}");
        node.transform.SetParent(anchor, false);
        node.layer = cell.kind == WaveCellKind.OneWayPlatform ? LayerMask.NameToLayer("TransparentPlatform") : Style(cell).layer;
        GameObject physicsRoot = node;
        var body = physicsRoot.GetComponent<Rigidbody2D>();
        if (body == null) { body = physicsRoot.AddComponent<Rigidbody2D>(); body.bodyType = cell.group > 0 ? RigidbodyType2D.Kinematic : RigidbodyType2D.Static; }
        var composite = physicsRoot.GetComponent<CompositeCollider2D>();
        if (composite == null) composite = physicsRoot.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        var settings = (cell.kind == WaveCellKind.Platform ? data.cellMap.platformPrefab : data.cellMap.oneWayPrefab).GetComponent<CompositeCollider2D>();
        composite.generationType = settings.generationType;
        composite.vertexDistance = settings.vertexDistance; composite.offsetDistance = settings.offsetDistance; composite.edgeRadius = settings.edgeRadius;
        if (cell.kind == WaveCellKind.OneWayPlatform)
        {
            node.AddComponent<OneWayPlatform>(); node.GetComponent<BoxCollider2D>().enabled = false;
        }
        node.AddComponent<Grid>();
        var mapNode = new GameObject("CollisionCells"); mapNode.layer = node.layer; mapNode.transform.SetParent(node.transform, false);
        if (cell.kind == WaveCellKind.OneWayPlatform)
        {
            mapNode.transform.localPosition = new Vector3(0, cell.position.y - origin.y + .8f);
            mapNode.transform.localScale = new Vector3(1, .2f, 1);
        }
        var map = mapNode.AddComponent<Tilemap>();
        var collider = mapNode.AddComponent<TilemapCollider2D>(); collider.compositeOperation = Collider2D.CompositeOperation.Merge;
        var channel = new Channel { group = cell.group, origin = origin, parent = node.transform, map = map, collider = collider, composite = composite };
        channels.Add(key, channel); return channel;
    }
    private void Preload(WaveMapCell cell)
    {
        if (pool.ContainsKey(cell)) return;
        var owner = GetChannel(cell);
        var prefab = cell.kind == WaveCellKind.Platform ? data.cellMap.platformPrefab : data.cellMap.oneWayPrefab;
        var view = prefab.CreateVisual(owner.parent);
        view.ApplyStyle(Style(cell));
        view.name = $"{cell.kind}_{cell.position.x}_{cell.position.y}";
        view.transform.localPosition = (Vector2)cell.position - owner.origin + Vector2.one * .5f;
        view.gameObject.SetActive(false); pool.Add(cell, view); owners.Add(cell, owner);
    }
    private void Collision(WaveMapCell cell, bool enabled)
    {
        var owner = owners[cell];
        var point = Vector2Int.RoundToInt((Vector2)cell.position - owner.origin);
        if (cell.kind == WaveCellKind.OneWayPlatform) point.y = 0;
        var coordinate = (Vector3Int)point;
        owner.map.SetTile(coordinate, enabled ? cell.kind == WaveCellKind.Platform ? Style(cell).tile : oneWayTile : null);
        if (enabled && cell.kind == WaveCellKind.Platform)
        {
            owner.map.SetTileFlags(coordinate, TileFlags.None);
            owner.map.SetTransformMatrix(coordinate, Style(cell).transform);
        }
    }
    private void Rebuild()
    {
        RefreshOutlines();
        var owned = new HashSet<CompositeCollider2D>(channels.Values.Select(c => c.composite));
        foreach (var old in GetComponentsInChildren<CompositeCollider2D>(true))
            if (!owned.Contains(old) && old.attachedRigidbody != null) old.attachedRigidbody.simulated = false;
        foreach (var channel in channels.Values) if (channel.collider != null && channel.collider.isActiveAndEnabled) channel.collider.ProcessTilemapChanges();
        foreach (var composite in channels.Values.Select(c => c.composite).Distinct())
            if (composite != null && composite.gameObject.activeInHierarchy && composite.generationType == CompositeCollider2D.GenerationType.Manual) composite.GenerateGeometry();
    }
    private void OnEnable() { if (IsPreloaded) Rebuild(); }
    private void Start() { if (IsPreloaded) Rebuild(); }

    private void ResetMap()
    {
        StopAllCoroutines(); ClearCustom(); temporary.Clear(); persistent.Clear(); outlineGroups.Clear(); animationPivots.Clear(); nextOutlineGroup = 0; expiry.Clear(); generations.Clear(); removing.Clear(); IsTransitioning = false;
        ApplyBackground();
        ApplyRoomPresentation(WaveRoomState.LayoutAt(data, 0));
        foreach (var cell in active.ToArray()) { pool[cell].gameObject.SetActive(false); Collision(cell, false); }
        active.Clear(); initial.Clear(); initial.UnionWith(data.cellMap.cells); SetInitialPivots();
        foreach (var cell in data.cellMap.cells)
        {
            Preload(cell); active.Add(cell); pool[cell].gameObject.SetActive(true); pool[cell].SetAmount(1); pool[cell].transform.localPosition = CellCenter(cell); Collision(cell, true);
        }
        Rebuild();
    }

    public IEnumerator Prepare(int index)
    {
        EndEvents();
        Rebuild();
        initial.Clear(); initial.UnionWith(WaveCellState.Initial(data, index)); SetInitialPivots();
        // 새 초기맵이 소유한 셀에는 이전 임시 생성의 수명을 넘기지 않는다.
        var initialKeys = new HashSet<(Vector2Int, int)>(initial.Select(c => (c.position, c.group)));
        foreach (var cell in expiry.Keys.Where(c => initialKeys.Contains((c.position, c.group))).ToArray()) expiry.Remove(cell);
        persistent.RemoveWhere(c => initialKeys.Contains((c.position, c.group)));
        var desired = new HashSet<WaveMapCell>(initial); desired.UnionWith(persistent);
        var remove = active.Except(desired).ToArray();
        var add = desired.Except(active).ToArray();
        IsTransitioning = true;
        // 확장 바닥을 먼저 완성한다. 새 경로가 생기기 전에 기존 바닥을 제거하지 않는다.
        if (add.Length > 0) yield return Animate(add, true, .55f);
        if (remove.Length > 0) yield return Animate(remove, false, .55f);
        var layout = WaveRoomState.LayoutAt(data, index);
        if (waves.SpawnRoot != null) waves.SpawnRoot.position = transform.TransformPoint(layout.spawnOffset);
        ApplyRoomPresentation(layout);
        if (layout.centerPlayer && GameManager.Instance != null)
        {
            var player = GameManager.Instance.Player; var before = player.transform.position;
            player.ResetPlayerPosition(transform.TransformPoint(data.playerStart));
            CinemachineCore.OnTargetObjectWarped(player.transform, player.transform.position - before);
        }
        IsTransitioning = false;
    }
    private void ApplyRoomPresentation(WaveRoomLayout layout)
    {
        if (data.adaptCameraToRoom)
        {
            var presentation = GetComponent<TrainingRoomPresentation>();
            if (presentation != null) presentation.Apply(layout.bounds.width * .5f, layout.bounds.height);
            if (originalCameraBounds != null)
            {
                originalCameraBounds.size = layout.bounds.size + new Vector2(2, 3);
                Vector3 center = transform.TransformPoint(layout.bounds.center + Vector2.down * 1.5f);
                Vector3 position = center - originalCameraBounds.transform.TransformVector(originalCameraBounds.offset);
                position.z = originalCameraBounds.transform.position.z;
                originalCameraBounds.transform.position = position;
                roomConfiner.InvalidateBoundingShapeCache();
            }
        }
    }
    private void SetInitialPivots()
    {
        foreach (var row in initial.Where(c=>c.kind==WaveCellKind.OneWayPlatform).GroupBy(c=>(c.group,c.style,c.position.y)))
        {
            var run=new List<WaveMapCell>();
            void Finish()
            {
                if(run.Count==0)return;
                var pivot=new Vector2((run[0].position.x+run[run.Count-1].position.x+1)*.5f,run[0].position.y+.5f);
                foreach(var cell in run)animationPivots[cell]=pivot;run.Clear();
            }
            foreach(var cell in row.OrderBy(c=>c.position.x)) { if(run.Count>0&&cell.position.x!=run[run.Count-1].position.x+1)Finish();run.Add(cell); }
            Finish();
        }
    }
    private Vector2 CellCenter(WaveMapCell cell) => (Vector2)cell.position - owners[cell].origin + Vector2.one * .5f;
    private IEnumerator Animate(WaveMapCell[] cells, bool add, float duration, float expiresAt = 0, bool keep = false)
    {
        cells = cells.Distinct().Where(c => add || active.Contains(c)).ToArray();
        var visible = new HashSet<WaveMapCell>(cells.Where(active.Contains));
        var tokens = new Dictionary<WaveMapCell, int>();
        if (cells.Length == 0) yield break;
        foreach (var cell in cells)
        {
            Preload(cell);
            int token = generations.TryGetValue(cell, out var oldToken) ? oldToken + 1 : 1;
            generations[cell] = token; tokens[cell] = token; expiry.Remove(cell);
            if (add) removing.Remove(cell); else removing[cell] = token;
            if (!add && cell.kind == WaveCellKind.Platform && Style(cell).generated) Collision(cell,false);
            if (add && keep) persistent.Add(cell); else persistent.Remove(cell);
            if (add && expiresAt > 0) expiry[cell] = (expiresAt, token);
            if (add)
                foreach (var old in active.Where(c => c.position == cell.position && c.group == cell.group && (c.kind != cell.kind || c.style != cell.style)
                    && !(initial.Contains(c) && (initial.Contains(cell) || Style(cell).generated && c.kind == cell.kind))).ToArray())
                {
                    pool[old].gameObject.SetActive(false); Collision(old, false); active.Remove(old); expiry.Remove(old); persistent.Remove(old); generations[old] = generations.TryGetValue(old, out var prior) ? prior + 1 : 1;
                }
            if (add)
            {
                active.Add(cell); pool[cell].gameObject.SetActive(true); pool[cell].SetAmount(visible.Contains(cell) ? 1 : 0);
                if (cell.kind == WaveCellKind.OneWayPlatform) Collision(cell,true);
            }
        }
        RefreshOutlines();
        if (cells.Any(c=>c.kind==WaveCellKind.OneWayPlatform&&add || c.kind==WaveCellKind.Platform&&!add&&Style(c).generated)) Rebuild();
        for (float time = 0; time < duration; time += Time.fixedDeltaTime)
        {
            float t = time / duration;
            foreach (var cell in cells) if (generations[cell] == tokens[cell] && (!add || !visible.Contains(cell)))
            {
                var view=pool[cell]; view.SetProgress(t,add,time);
                if (animationPivots.TryGetValue(cell,out var pivot))
                {
                    Vector2 localPivot = pivot - owners[cell].origin;
                    view.transform.localPosition = localPivot + Vector2.Scale(CellCenter(cell)-localPivot,view.transform.localScale);
                }
            }
            yield return new WaitForFixedUpdate();
        }
        foreach (var cell in cells)
        {
            if (generations[cell] != tokens[cell]) continue;
            pool[cell].SetAmount(1); pool[cell].transform.localPosition = CellCenter(cell); pool[cell].gameObject.SetActive(add); Collision(cell, add);
            if (!add) removing.Remove(cell);
            if (!add) active.Remove(cell);
        }
        Rebuild();
    }

    private void BeginEvents(Wave wave)
    {
        temporary.Clear();
        foreach (var batch in wave.cellBatches) StartCoroutine(Batch(batch));
        foreach (var entry in wave.terrainEvents ?? Array.Empty<WaveTerrainEvent>()) StartCoroutine(Event(entry));
    }
    private IEnumerator Batch(WaveCellBatch batch)
    {
        if (batch.time > 0) yield return new WaitForSeconds(batch.time);
        batch = new WaveCellBatch { label = batch.label, time = batch.time, duration = batch.duration, hasAnimationPivot = batch.hasAnimationPivot, animationPivot = batch.animationPivot, lifetime = batch.lifetime, keepAfterWave = batch.keepAfterWave, cells = batch.cells.Where(c => !initial.Contains(c)).ToArray() };
        temporary.Add(batch);
        var rules=WaveRoomRulesSO.Default;
        if (batch.cells.Length > 0 && rules != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.Play(rules.platformSpawnFeedback, transform.TransformPoint((Vector2)batch.cells[0].position+Vector2.one*.5f));
        int outlineGroup = ++nextOutlineGroup;
        Vector2 pivot = batch.hasAnimationPivot ? batch.animationPivot : batch.cells.Length > 0
            ? new Vector2((batch.cells.Min(c=>c.position.x)+batch.cells.Max(c=>c.position.x)+1)*.5f,(batch.cells.Min(c=>c.position.y)+batch.cells.Max(c=>c.position.y)+1)*.5f) : Vector2.zero;
        foreach (var cell in batch.cells) { outlineGroups[cell] = outlineGroup; animationPivots[cell]=pivot; }
        yield return Animate(batch.cells, true, batch.duration, batch.lifetime > 0 ? Time.time + batch.lifetime : 0, batch.keepAfterWave);
    }
    private IEnumerator Event(WaveTerrainEvent entry)
    {
        if (entry.time > 0) yield return new WaitForSeconds(entry.time);
        if (entry.custom) { if (entry.customPrefab != null) customObjects.Add(Instantiate(entry.customPrefab, transform)); yield break; }
        var positions = new HashSet<Vector2Int>(entry.cells);
        var cells = entry.operation == WaveTerrainOperation.Remove
            ? active.Where(c => positions.Contains(Vector2Int.FloorToInt((Vector2)transform.InverseTransformPoint(owners[c].parent.TransformPoint(CellCenter(c)))))).ToArray()
            : entry.cells.Select(p => new WaveMapCell { position = p, kind = entry.cellKind, style = entry.cellStyle }).ToArray();
        if (entry.operation == WaveTerrainOperation.Remove) persistent.ExceptWith(cells);
        yield return Animate(cells, entry.operation == WaveTerrainOperation.Add, entry.duration, entry.operation == WaveTerrainOperation.Add && entry.lifetime > 0 ? Time.time + entry.lifetime : 0, entry.operation == WaveTerrainOperation.Add && entry.keepAfterWave);
    }
    private void ClearCustom()
    {
        foreach (var item in customObjects) if (item != null) { item.SetActive(false); Destroy(item); }
        customObjects.Clear();
    }
    private void EndEvents()
    {
        StopAllCoroutines(); ClearCustom();
        foreach (var row in removing.ToArray())
            if (generations.TryGetValue(row.Key, out var token) && token == row.Value)
            { active.Remove(row.Key); pool[row.Key].gameObject.SetActive(false); Collision(row.Key, false); }
        removing.Clear();
        foreach (var cell in active) if (pool.TryGetValue(cell, out var view)) { view.SetAmount(1); view.transform.localPosition=CellCenter(cell); Collision(cell, true); }
        Rebuild();
    }
    private void FixedUpdate()
    {
        foreach (var row in expiry.Where(e => e.Value.time <= Time.time).ToArray())
        {
            expiry.Remove(row.Key);
            if (active.Contains(row.Key) && generations.TryGetValue(row.Key, out var token) && token == row.Value.token)
                StartCoroutine(Animate(new[] { row.Key }, false, .55f));
        }
    }
    private void FaceCenter(EnemyController enemy)
    {
        var layout = WaveRoomState.LayoutAt(data, waves.currentWaveIndex);
        if (layout.faceCenter) enemy.FacingUpdate(enemy.transform.position.x < transform.TransformPoint(layout.bounds.center).x ? 1 : -1);
    }
    public IEnumerator VanishCourseCells(Vector2 direction, float interval)
    {
        foreach (var batch in temporary.OrderByDescending(b => b.cells.Length > 0 ? Vector2.Dot(b.cells[0].position, direction) : 0).ToArray())
        {
            StartCoroutine(Animate(batch.cells, false, .55f)); persistent.ExceptWith(batch.cells);
            if (interval > 0) yield return new WaitForSeconds(interval);
        }
        yield return new WaitForSeconds(.55f); temporary.Clear();
    }
    public Bounds LocalBounds(Transform source)
    {
        int group = Array.FindIndex(data.cellMap.groups, g => transform.Find(g.path) == source) + 1;
        var cells = active.Where(c => c.group == group).ToArray();
        if (cells.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
        Vector2 origin = group > 0 ? data.cellMap.groups[group - 1].origin : Vector2.zero;
        Vector2 min = cells[0].position, max = min + Vector2.one;
        foreach (var cell in cells) { min = Vector2.Min(min, cell.position); max = Vector2.Max(max, cell.position + Vector2.one); }
        return new Bounds((min + max) * .5f - origin, max - min);
    }
    private void OnDestroy()
    {
        if (waves != null) { waves.WavesReset -= ResetMap; waves.WaveStarted -= BeginEvents; waves.WaveEnded -= EndEvents; waves.EnemySpawned -= FaceCenter; }
        if (oneWayTile != null) Destroy(oneWayTile);
    }
}
