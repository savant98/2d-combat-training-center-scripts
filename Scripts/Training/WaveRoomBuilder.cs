using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Tilemaps;

// 모든 일반 훈련이 쓰는 단위 셀 지형 생성·등장·소멸 규칙이다.
public sealed class WaveRoomBuilder : MonoBehaviour
{
    private TrainingRoomContent content;
    private WaveDataSO data;
    private Tilemap shell;
    private Tilemap platforms;
    private BoxCollider2D cameraBounds;
    private LineRenderer line;
    private SpriteRenderer shadow;
    private Rect bounds;
    private bool moving;
    private readonly Dictionary<Vector3Int, Coroutine> motions = new();
    private readonly Dictionary<(Tilemap map, Vector3Int cell), (TileBase tile, Matrix4x4 matrix, TileFlags flags)> removedTiles = new();
    private readonly List<GameObject> customObjects = new();
    private readonly Dictionary<BoxCollider2D, List<GameObject>> splitPlatforms = new();
    private readonly Dictionary<LineRenderer, bool> splitLines = new();
    public Rect Bounds => bounds;
    public Tilemap Platforms => platforms;

    public void Configure(TrainingRoomContent module, WaveDataSO source)
    {
        content = module;
        data = source;
        if (!source.customRoom) RequireBounds(source.room.bounds);
        if (source.customRoom)
        {
            var eventGrid = new GameObject("TerrainEventGrid").AddComponent<Grid>();
            eventGrid.transform.SetParent(transform, false);
            platforms = Map(eventGrid.transform, "Platforms");
            content.waves.WaveStarted += StartEvents;
            content.waves.WavesReset += ResetRoom;
            content.waves.WaveEnded += CancelEvents;
            return;
        }
        foreach (var map in GetComponentsInChildren<Tilemap>(true)) map.gameObject.SetActive(false);
        foreach (var old in GetComponentsInChildren<TrainingCourseStage>(true)) old.gameObject.SetActive(false);
        foreach (var old in GetComponentsInChildren<TrainingRoomPresentation>(true)) old.enabled = false;
        var grid = new GameObject("GeneratedGrid").AddComponent<Grid>();
        grid.transform.SetParent(transform, false);
        shell = Map(grid.transform, "Shell");
        platforms = Map(grid.transform, "Platforms");
        var cameraNode = new GameObject("GeneratedCameraBounds");
        cameraNode.transform.SetParent(transform, false);
        cameraBounds = cameraNode.AddComponent<BoxCollider2D>();
        cameraBounds.isTrigger = true;
        var confiner = content.roomCamera.GetComponent<CinemachineConfiner2D>();
        if (confiner != null) confiner.BoundingShape2D = cameraBounds;
        var edge = new GameObject("PlatformLine");
        edge.transform.SetParent(transform, false);
        line = edge.AddComponent<LineRenderer>();
        line.sharedMaterial = content.lineMaterial;
        line.useWorldSpace = false;
        line.loop = true;
        line.widthMultiplier = .05f;
        var shade = new GameObject("FloorBlinder");
        shade.transform.SetParent(transform, false);
        shadow = shade.AddComponent<SpriteRenderer>();
        shadow.sprite = content.blinderSprite;
        shadow.sharedMaterial = content.blinderMaterial;
        shadow.sortingOrder = 1;
        // 원본 지형 마감은 중복으로 그리지 않는다.
        var oldShade = transform.Find("Grid/TrainingRoomFadeSprite");
        if (oldShade != null) oldShade.gameObject.SetActive(false);
        var backdrop = gameObject.AddComponent<WaveRoomBackdrop>();
        backdrop.Configure(transform);
        Apply(source.room.bounds);
        backdrop.Resize(source.room.bounds);
        content.spawnRoot.localPosition = source.room.spawnOffset;
        foreach (var cell in source.room.platformCells) platforms.SetTile((Vector3Int)cell, content.terrainTile);
        content.waves.WaveStarted += StartEvents;
        content.waves.WavesReset += ResetRoom;
        content.waves.EnemySpawned += FaceCenter;
        content.waves.WaveEnded += CancelEvents;
    }

    private Tilemap Map(Transform parent, string name)
    {
        var node = new GameObject(name);
        node.layer = LayerMask.NameToLayer("Platform");
        node.transform.SetParent(parent, false);
        var map = node.AddComponent<Tilemap>();
        node.AddComponent<TilemapRenderer>();
        node.AddComponent<TilemapCollider2D>();
        return map;
    }

    public IEnumerator Prepare(Wave wave)
    {
        CancelEvents();
        foreach (var item in customObjects) if (item != null) Destroy(item);
        customObjects.Clear();
        if (data.customRoom)
        {
            var layout = WaveRoomState.LayoutAt(data, content.waves.currentWaveIndex);
            yield return TransitionPlatforms(layout.paintPlatforms ? layout.platformCells : Array.Empty<Vector2Int>());
            foreach (var cell in layout.removedCells) RemoveOriginalCell(cell, .55f);
            if (layout.removedCells.Length > 0) yield return new WaitForSeconds(.55f);
            yield break;
        }
        if (!wave.changeTerrain)
        {
            var inherited = WaveRoomState.LayoutAt(data, content.waves.currentWaveIndex);
            yield return TransitionPlatforms(inherited.platformCells);
            foreach (var cell in inherited.removedCells) RemoveOriginalCell(cell, .55f);
            if (inherited.removedCells.Length > 0) yield return new WaitForSeconds(.55f);
            yield break;
        }
        RequireBounds(wave.room.bounds);
        moving = true;
        if (wave.room.centerPlayer && GameManager.Instance != null)
            GameManager.Instance.Player.ResetPlayerPosition(transform.TransformPoint(data.playerStart));
        Rect previous = bounds, next = wave.room.bounds;
        float duration = Mathf.Max(0f, wave.room.duration);
        for (float time = 0; time < duration; time += Time.fixedDeltaTime)
        {
            float t = Mathf.SmoothStep(0, 1, time / duration);
            Apply(Rect.MinMaxRect(Mathf.Lerp(previous.xMin, next.xMin, t), Mathf.Lerp(previous.yMin, next.yMin, t),
                Mathf.Lerp(previous.xMax, next.xMax, t), Mathf.Lerp(previous.yMax, next.yMax, t)));
            yield return new WaitForFixedUpdate();
        }
        Apply(next);
        GetComponent<WaveRoomBackdrop>().Resize(next);
        content.spawnRoot.localPosition = wave.room.spawnOffset;
        yield return TransitionPlatforms(wave.room.platformCells);
        foreach (var cell in wave.room.removedCells) RemoveOriginalCell(cell, .55f);
        if (wave.room.removedCells.Length > 0) yield return new WaitForSeconds(.55f);
        moving = false;
    }

    private IEnumerator TransitionPlatforms(Vector2Int[] cells)
    {
        var wanted = new HashSet<Vector2Int>(cells);
        bool changed = false;
        foreach (var cell in platforms.cellBounds.allPositionsWithin)
            if (platforms.HasTile(cell) && !wanted.Contains((Vector2Int)cell)) { StartCell(cell, false, .55f); changed = true; }
        foreach (var cell in wanted)
            if (!platforms.HasTile((Vector3Int)cell)) { StartCell((Vector3Int)cell, true, .55f); changed = true; }
        if (changed) while (motions.Count > 0) yield return new WaitForFixedUpdate();
    }

    private void Apply(Rect next)
    {
        bounds = next;
        shell.ClearAllTiles();
        int left = Mathf.FloorToInt(next.xMin), right = Mathf.CeilToInt(next.xMax);
        int bottom = Mathf.FloorToInt(next.yMin), top = Mathf.CeilToInt(next.yMax);
        for (int x = left - 3; x < right + 3; x++)
            for (int y = bottom - 3; y < top + 3; y++)
                if (x < left || x >= right || y < bottom || y >= top)
                    shell.SetTile(new Vector3Int(x, y), content.terrainTile);
        line.positionCount = 4;
        line.SetPositions(new[] { new Vector3(next.xMin, next.yMin), new Vector3(next.xMax, next.yMin),
            new Vector3(next.xMax, next.yMax), new Vector3(next.xMin, next.yMax) });
        if (shadow.sprite != null)
        {
            shadow.transform.localPosition = new Vector3(next.center.x, next.yMin - 1.5f);
            shadow.transform.localRotation = Quaternion.Euler(0, 0, 90);
            var size = shadow.sprite.bounds.size;
            shadow.transform.localScale = new Vector3(3 / size.x, (next.width + 2) / size.y, 1);
        }
        cameraBounds.size = next.size + new Vector2(2, 3);
        cameraBounds.transform.localPosition = next.center + Vector2.down * 1.5f;
        content.roomCamera.GetComponent<CinemachineConfiner2D>()?.InvalidateBoundingShapeCache();
        if (moving && GameManager.Instance != null && GameManager.Instance.Player != null)
        {
            var player = GameManager.Instance.Player;
            var body = player.GetComponent<Collider2D>().bounds;
            Vector3 local = transform.InverseTransformPoint(body.center);
            Vector3 safe = local;
            safe.x = Mathf.Clamp(safe.x, next.xMin + body.extents.x + .02f, next.xMax - body.extents.x - .02f);
            safe.y = Mathf.Clamp(safe.y, next.yMin + body.extents.y + .02f, next.yMax - body.extents.y - .02f);
            if ((safe - local).sqrMagnitude > .0001f)
            {
                Vector3 from = player.transform.position;
                player.ResetPlayerPosition(from + transform.TransformVector(safe - local));
                CinemachineCore.OnTargetObjectWarped(player.transform, player.transform.position - from);
            }
        }
        if (content.clearPortal != null)
        {
            var point = content.clearPortal.TeleportPoint;
            Vector3 kept = point != null ? point.position : Vector3.zero;
            content.clearPortal.transform.position = transform.TransformPoint(new Vector3(next.xMax - 3, next.yMin + 1.5f));
            if (point != null) point.position = kept;
        }
    }

    private static void RequireBounds(Rect value)
    {
        if (float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.width) || float.IsNaN(value.height)
            || Mathf.Abs(value.x) > 10000 || Mathf.Abs(value.y) > 10000
            || value.width < 4 || value.height < 4 || value.width > 256 || value.height > 256)
            throw new ArgumentOutOfRangeException(nameof(value), "일반 Room 외곽은 유한한 좌표와 가로·세로 4~256칸이 필요합니다.");
    }

    private void StartEvents(Wave wave)
    {
        StartCoroutine(StartupCutouts());
        foreach (var entry in wave.terrainEvents ?? Array.Empty<WaveTerrainEvent>()) StartCoroutine(Event(entry));
    }
    private IEnumerator StartupCutouts()
    {
        // 같은 프레임에 생성되는 Prefab에도 초기 셀 삭제를 적용한다.
        yield return null;
        var layout = WaveRoomState.LayoutAt(data, content.waves.currentWaveIndex);
        foreach (var cell in layout.removedCells) RemoveOriginalCell(cell, .55f);
    }
    private IEnumerator Event(WaveTerrainEvent entry)
    {
        if (entry.time > 0) yield return new WaitForSeconds(entry.time);
        yield return null;
        if (entry.custom && entry.customPrefab != null)
            customObjects.Add(Instantiate(entry.customPrefab, transform));
        else foreach (var cell in entry.cells)
        {
            if (entry.operation == WaveTerrainOperation.Add) StartCell((Vector3Int)cell, true, entry.duration);
            else
            {
                StartCell((Vector3Int)cell, false, entry.duration);
                RemoveOriginalCell(cell, entry.duration);
            }
        }
    }
    private void RemoveOriginalCell(Vector2Int cell, float duration)
    {
        var maps = new HashSet<Tilemap>(GetComponentsInChildren<Tilemap>(true));
        var oneWays = new HashSet<OneWayPlatform>(GetComponentsInChildren<OneWayPlatform>(true));
        foreach (var entity in content.waves.WorldEntities)
            if (entity != null && entity.gameObject.activeInHierarchy)
            {
                foreach (var map in entity.GetComponentsInChildren<Tilemap>(true)) maps.Add(map);
                foreach (var surface in entity.GetComponentsInChildren<OneWayPlatform>(true)) oneWays.Add(surface);
            }
        var world = transform.TransformPoint((Vector3)(Vector2)cell + new Vector3(.5f, .5f));
        var area = new Rect((Vector2)world - Vector2.one * .5f, Vector2.one);
        foreach (var surface in oneWays) RemoveOneWayCell(surface, area, duration);
        foreach (var map in maps)
        {
            if (map == platforms || !map.gameObject.activeInHierarchy) continue;
            var local = map.WorldToCell(world);
            var tile = map.GetTile(local);
            if (tile == null) continue;
            removedTiles.TryAdd((map, local), (tile, map.GetTransformMatrix(local), map.GetTileFlags(local)));
            StartCoroutine(RemoveOriginal(map, local, duration));
        }
    }
    private void RemoveOneWayCell(OneWayPlatform surface, Rect area, float duration)
    {
        var original = surface.GetComponent<BoxCollider2D>();
        if (original == null || !surface.gameObject.activeInHierarchy) return;
        if (!splitPlatforms.TryGetValue(original, out var pieces))
        {
            var shape = original.bounds;
            if (!original.enabled || !area.Overlaps(new Rect(shape.min, shape.size))) return;
            pieces = new List<GameObject>();
            var lines = surface.GetComponentsInChildren<LineRenderer>(true);
            // 상면 판정과 얇은 높이는 유지하고 수정할 폭만 1칸 단위 Collider로 나눈다.
            for (int x = Mathf.FloorToInt(shape.min.x); x < Mathf.CeilToInt(shape.max.x); x++)
            {
                float min = Mathf.Max(x, shape.min.x), max = Mathf.Min(x + 1, shape.max.x);
                var node = new GameObject("TerrainCell");
                node.layer = original.gameObject.layer;
                node.transform.SetParent(surface.transform, false);
                node.transform.position = new Vector3((min + max) * .5f, shape.center.y, shape.center.z);
                var box = node.AddComponent<BoxCollider2D>();
                var scale = node.transform.lossyScale;
                box.size = new Vector2((max - min) / Mathf.Abs(scale.x), shape.size.y / Mathf.Abs(scale.y));
                foreach (var source in lines) CloneClippedLines(source, node.transform, min, max);
                pieces.Add(node);
            }
            foreach (var source in lines) { splitLines.TryAdd(source, source.enabled); source.enabled = false; }
            splitPlatforms.Add(original, pieces);
            original.enabled = false;
        }
        foreach (var piece in pieces)
        {
            if (piece == null || !piece.activeSelf) continue;
            var shape = piece.GetComponent<BoxCollider2D>().bounds;
            if (area.Overlaps(new Rect(shape.min, shape.size))) StartCoroutine(RemoveOneWayPiece(piece, duration));
        }
    }
    private void CloneClippedLines(LineRenderer source, Transform parent, float min, float max)
    {
        int count = source.positionCount;
        for (int i = 0; i < count - (source.loop ? 0 : 1); i++)
        {
            Vector3 a = source.GetPosition(i), b = source.GetPosition((i + 1) % count);
            if (!source.useWorldSpace) { a = source.transform.TransformPoint(a); b = source.transform.TransformPoint(b); }
            float lo = 0, hi = 1, delta = b.x - a.x;
            if (Mathf.Abs(delta) < .00001f) { if (a.x < min || a.x > max) continue; }
            else { lo = Mathf.Max(0, Mathf.Min((min - a.x) / delta, (max - a.x) / delta)); hi = Mathf.Min(1, Mathf.Max((min - a.x) / delta, (max - a.x) / delta)); }
            if (lo > hi) continue;
            var line = Instantiate(source, parent);
            line.transform.localPosition = Vector3.zero;
            line.transform.localRotation = Quaternion.identity;
            line.transform.localScale = Vector3.one;
            line.useWorldSpace = false; line.loop = false; line.positionCount = 2;
            line.SetPosition(0, parent.InverseTransformPoint(Vector3.Lerp(a, b, lo)));
            line.SetPosition(1, parent.InverseTransformPoint(Vector3.Lerp(a, b, hi)));
        }
    }
    private IEnumerator RemoveOneWayPiece(GameObject piece, float duration)
    {
        for (float time = 0; time < duration; time += Time.fixedDeltaTime)
        {
            if (piece == null) yield break;
            piece.transform.localScale = new Vector3(1, Mathf.Max(.01f, 1 - Mathf.SmoothStep(0, 1, time / duration)), 1);
            yield return new WaitForFixedUpdate();
        }
        if (piece != null) piece.SetActive(false);
    }
    private IEnumerator RemoveOriginal(Tilemap map, Vector3Int cell, float duration)
    {
        var original = map.GetTransformMatrix(cell);
        map.SetTileFlags(cell, TileFlags.None);
        for (float t = 0; t < duration; t += Time.fixedDeltaTime)
        {
            if (map == null) yield break;
            map.SetTransformMatrix(cell, original * Matrix4x4.Scale(new Vector3(1, Mathf.Max(.01f, 1 - Mathf.SmoothStep(0, 1, t / duration)), 1)));
            yield return new WaitForFixedUpdate();
        }
        if (map != null) { map.SetTransformMatrix(cell, original); map.SetTile(cell, null); }
    }
    private void StartCell(Vector3Int cell, bool add, float duration)
    {
        if (motions.TryGetValue(cell, out var routine) && routine != null) StopCoroutine(routine);
        if (duration <= 0)
        {
            platforms.SetTile(cell, add ? content.terrainTile : null);
            platforms.SetTransformMatrix(cell, Matrix4x4.identity);
            motions.Remove(cell);
            return;
        }
        motions[cell] = StartCoroutine(AnimateCell(cell, add, duration));
    }
    private IEnumerator AnimateCell(Vector3Int cell, bool add, float duration)
    {
        platforms.SetTile(cell, content.terrainTile);
        platforms.SetTileFlags(cell, TileFlags.None);
        for (float time = 0; time < duration; time += Time.fixedDeltaTime)
        {
            float t = Mathf.SmoothStep(0, 1, time / duration);
            float scale = add ? t : 1 - t;
            platforms.SetTransformMatrix(cell, Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, Mathf.Max(.01f, scale), 1)));
            yield return new WaitForFixedUpdate();
        }
        platforms.SetTransformMatrix(cell, Matrix4x4.identity);
        if (!add) platforms.SetTile(cell, null);
        motions.Remove(cell);
    }
    private void FaceCenter(EnemyController enemy)
    {
        var wave = data.waves[content.waves.currentWaveIndex];
        if (wave.changeTerrain && wave.room.faceCenter)
            enemy.FacingUpdate(enemy.transform.position.x < transform.TransformPoint(bounds.center).x ? 1 : -1);
    }
    private void ResetRoom()
    {
        CancelEvents();
        moving = false;
        platforms.ClearAllTiles();
        foreach (var item in customObjects) if (item != null) Destroy(item);
        customObjects.Clear();
        if (data.customRoom) return;
        content.spawnRoot.localPosition = data.room.spawnOffset;
        Apply(data.room.bounds);
        GetComponent<WaveRoomBackdrop>().Resize(data.room.bounds);
        foreach (var cell in data.room.platformCells) platforms.SetTile((Vector3Int)cell, content.terrainTile);
    }
    private void OnDestroy()
    {
        CancelEvents();
        if (content == null || content.waves == null) return;
        content.waves.WaveStarted -= StartEvents;
        content.waves.WavesReset -= ResetRoom;
        content.waves.EnemySpawned -= FaceCenter;
        content.waves.WaveEnded -= CancelEvents;
    }
    private void CancelEvents()
    {
        StopAllCoroutines();
        foreach (var cell in motions.Keys) platforms.SetTransformMatrix(cell, Matrix4x4.identity);
        motions.Clear();
        foreach (var item in removedTiles)
            if (item.Key.map != null) { item.Key.map.SetTile(item.Key.cell, item.Value.tile); item.Key.map.SetTransformMatrix(item.Key.cell, item.Value.matrix); item.Key.map.SetTileFlags(item.Key.cell, item.Value.flags); }
        removedTiles.Clear();
        foreach (var entry in splitPlatforms)
        {
            if (entry.Key != null) entry.Key.enabled = true;
            foreach (var piece in entry.Value) if (piece != null) { piece.SetActive(false); Destroy(piece); }
        }
        splitPlatforms.Clear();
        foreach (var entry in splitLines) if (entry.Key != null) entry.Key.enabled = entry.Value;
        splitLines.Clear();
    }
}
