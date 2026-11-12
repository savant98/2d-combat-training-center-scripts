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
    // 지금 Wave의 초기맵. Wave마다 만들어 둔 집합(initialCaches)을 가리키므로 고치지 않는다.
    private HashSet<WaveMapCell> initial = new();
    // 초기맵 배열마다 한 번만 만드는 조회용 집합. 같은 배열을 이어 쓰는 Wave는 함께 쓴다.
    private sealed class InitialCache
    {
        // 만들 때의 배열 내용 지문. 실행 중에 같은 배열의 칸을 고치면(편집기) 달라지므로 다시 만든다.
        public ulong signature;
        public HashSet<WaveMapCell> cells;
        public HashSet<(Vector2Int, int)> keys;
        public readonly List<(WaveMapCell cell, Vector2 pivot)> pivots = new();
    }
    private readonly Dictionary<WaveMapCell[], InitialCache> initialCaches = new();
    // 연출 중이거나 충돌을 아직 맞추지 않은 칸. Wave가 끝날 때 이 칸들만 제자리 모습으로 되돌린다(켜진 칸 전부를 훑지 않는다).
    private readonly HashSet<WaveMapCell> unsettled = new();
    // 켜진 Platform 칸이나 그룹 위치가 바뀌어 연결 공간을 다시 구해야 하는지.
    private bool solidsDirty = true;
    // 마지막으로 구한 연결 공간의 빈 칸들. 기준점이 이 안에서만 움직였으면 공간을 다시 찾지 않는다.
    private readonly HashSet<Vector2Int> roomRegion = new();
    private int maxStyle;
    private readonly HashSet<WaveMapCell> persistent = new();
    private readonly List<WaveCellBatch> temporary = new();
    private readonly Dictionary<WaveMapCell, int> generations = new();
    private readonly Dictionary<WaveMapCell, (float time, int token)> expiry = new();
    private readonly Dictionary<WaveMapCell, int> removing = new();
    private Tile oneWayTile;
    // 구형 Template의 이동 기능 없이 좌표 보존용으로 복원한 그룹은 같은 고정 지형이다.
    private readonly HashSet<int> fixedOutlineGroups = new();
    private readonly Dictionary<WaveMapCell, int> outlineGroups = new();
    private int nextOutlineGroup;
    private readonly Dictionary<WaveMapCell, Vector2> animationPivots = new();
    // 묶음마다 만든 덩어리 외곽선: 그때의 요약값과 쓰고 있는 선(칸의 테두리 선을 빌려 온 것).
    private sealed class OutlineChunk { public ulong signature; public readonly List<LineRenderer> lines = new(); }
    private readonly Dictionary<(int group, int style, int assembly), OutlineChunk> outlineChunks = new();
    private readonly Dictionary<(int group, int style, int assembly), ulong> outlineSums = new();
    // 묶음마다 패널로 움직이지 않는 칸만의 요약값. 덩어리 외곽선의 모양은 이 칸들로 정해지므로 모양 캐시의 열쇠로 쓴다.
    private readonly Dictionary<(int group, int style, int assembly), ulong> outlineFixedSums = new();
    private ulong oneWaySignature;
    private bool oneWayBuilt;
    // 큰 기본 지형 덩어리의 외곽선 모양(윤곽을 따라간 결과). 같은 칸 구성이 다시 나오면(Wave마다의 시작 지형) 윤곽을 다시 따라가지 않고 그대로 쓴다.
    // 입장 로딩이 Wave마다의 시작 지형 모양을 미리 넣어 둔다.
    private sealed class OutlineShape { public WaveMapCell[] owners; public Vector3[][] positions; }
    private readonly Dictionary<(int group, int style, int assembly), Dictionary<ulong, OutlineShape>> outlineShapes = new();
    private const int ShapeCacheMinimum = 200, ShapeCacheLimit = 12;
    // 기계팔 패널로 끼우거나 빼는 중인 칸의 진행 상태. 자세는 LateUpdate가 시간으로 계산해 적용한다.
    private sealed class PanelMotion
    {
        public float start;     // 시차를 더한 시작 시각(Time.time)
        public float length;    // 패널 한 장의 연출 시간(초)
        public bool install;    // 끼우기(true)·빼기(false)
        public uint seed;       // 칸마다 다른 방향·기울기
        public bool shown;      // 화면 안이어서 수납구·팔·소리를 낸다
        public bool sounded;    // 맞물림·풀림 소리를 이미 냈다
        public Vector2 pivot;   // 패널 그림의 중심(칸 중심 기준). OneWay는 칸 위쪽의 얇은 판이다
        public Vector2 half;    // 패널의 반 크기
        public Vector2 reach;   // 수납 위치까지의 거리 배율(얇은 판은 세로로 덜 벗어난다)
        public float tilt;      // 기울기 배율
        public Color flash;     // 맞물림 섬광의 색
    }
    private readonly Dictionary<WaveMapCell, PanelMotion> panels = new();
    private WaveTerrainPanelRig rig;
    private bool rigFilled;
    // 패널 연출의 시계. 지형 갱신이 몰려 튄 프레임에도 연출이 건너뛰지 않도록 평소 프레임보다 훨씬 긴 프레임은 잘라서 흐른다.
    // 계속 느린 환경(30fps 미만)에서는 평소 프레임 시간이 따라 올라가 실제 시간과 같이 흐른다.
    private float panelClock;
    private float usualFrame = 1f / 60f;
    private const float PanelClockStep = 1f / 30f;
    // 충돌 Tile이 놓인 칸. 같은 상태를 다시 쓰지 않아 충돌 형상을 불필요하게 다시 만들지 않는다.
    private readonly HashSet<WaveMapCell> solidCells = new();
    private int legacyCompositeChecks = 3;
    // 끼운 패널이 덩어리 외곽선으로 합쳐질 때, 이웃과 맞닿은 이음매 선을 잠깐 남겼다가 지운다.
    private struct Seam { public Matrix4x4 cell; public int mask; public float start; public Color color; public float inset; public float width; }
    private readonly List<Seam> seams = new();
    private const float SeamFade = .3f;
    // 길이를 정한 묶음에서 패널 한 장이 쓰는 시간의 비율. 나머지는 칸 사이의 시차다.
    private const float PanelShare = .7f;
    private Camera viewCamera;
    private Rect? backdropBounds;
    public int MovingPanelCount => panels.Count;
    public WaveDataSO Data => data;
    private readonly List<GameObject> customObjects = new();
    private Transform staticRoot;
    private CinemachineConfiner2D roomConfiner;
    private BoxCollider2D originalCameraBounds;
    private bool automaticRoomSpace;
    private WaveRoomBackdrop automaticBackdrop;
    private HashSet<Vector2Int> roomSolids;
    private Vector2Int roomSeed;
    private Transform[] groupAnchors = Array.Empty<Transform>();
    private Vector3[] groupPositions = Array.Empty<Vector3>();
    private TrainingCourseStage[] courseStages = Array.Empty<TrainingCourseStage>();
    private bool roomSpaceDirty;
    private bool roomSpaceReset;
    // 크레인이 Player를 옮기는 동안: 출발·도착 지점(방 좌표)과 들어 올린 높이. 카메라와 배경 범위에 경로까지 넣는다.
    private bool carrying;
    private Vector2 carryFrom, carryTo;
    private float carryLift;
    // 다음 연결 공간 갱신은 지형이 그대로여도 건너뛰지 않는다(옮기기를 시작하고 끝낼 때 범위가 달라진다).
    private bool forceRoomSpace;
    // 커스텀 실내(TrainingRoomPresentation)의 카메라 범위: 지금 적용한 것과, 옮기기가 끝난 뒤 맞출 것.
    private Rect? presentationCamera, pendingPresentationCamera;
    // 옮기는 동안에는 방 카메라가 상자를 바짝 따라간다(평소 감쇠로는 빠르게 옮겨지는 상자가 화면 밖으로 나간다). 끝나면 원래 값으로 되돌린다.
    private const float CarryCameraDamping = .3f;
    private CinemachineFollow carryFollow;
    private Vector3 carryFollowDamping;
    private CinemachineBrain carryBrain;
    private CinemachineBrain.UpdateMethods carryBrainUpdate;
    private CinemachineBrain.BrainUpdateMethods carryBrainBlendUpdate;
    // 지형 공간은 즉시 확정하고, 도착 카메라의 축소만 짧게 완화한다. 확장은 이동 경로를 먼저 확보한다.
    private const float CameraSettleDuration = .5f;
    private Vector2 cameraSettleFromSize, cameraSettleToSize;
    private Vector3 cameraSettleFromPosition, cameraSettleToPosition;
    private float cameraSettleTime;
    public bool IsCameraSettling { get; private set; }
    public Rect? ConnectedBounds { get; private set; }
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
        // 실내 치수 연출(TrainingRoomPresentation)을 따로 가진 방만 저장된 실내 범위를 쓴다(관리자 평가).
        // 그 밖의 방은 커스텀 코스가 있어도 셀로 만든 연결 공간에 카메라·배경·통창을 맞춘다.
        automaticRoomSpace = content != null && originalCameraBounds != null
            && (!data.customRoom || GetComponentInChildren<TrainingRoomPresentation>(true) == null);
        if (automaticRoomSpace)
        {
            automaticBackdrop = gameObject.AddComponent<WaveRoomBackdrop>();
            automaticBackdrop.Configure(transform, data.room.bounds);
            // 통창·케이블은 이쪽이 연결 공간에 맞춰 늘리므로 코스의 장식 늘리기와 겹치지 않게 한다.
            courseStages = GetComponentsInChildren<TrainingCourseStage>(true);
            foreach (var stage in courseStages) stage.AutomaticRoomSpace = true;
        }
        // 구형 TrainingRoom의 Tilemap을 삭제해도 저장된 그룹 원점에서 같은 배치를 복원한다.
        // 전용 방에 실제 앵커가 있으면 기존 코스 이동 연결을 그대로 사용한다.
        Transform generatedGroups = null;
        groupAnchors = data.cellMap.groups.Select((group, index) =>
        {
            var anchor = string.IsNullOrEmpty(group.path) ? null : transform.Find(group.path);
            if (anchor != null) return anchor;
            if (generatedGroups == null)
            {
                generatedGroups = new GameObject("CellGroupAnchors").transform;
                generatedGroups.SetParent(transform, false);
            }
            anchor = new GameObject("CellGroup_" + (index + 1)).transform;
            anchor.SetParent(generatedGroups, false);
            anchor.localPosition = group.origin;
            fixedOutlineGroups.Add(index + 1);
            return anchor;
        }).ToArray();
        groupPositions = groupAnchors.Select(a => a != null ? a.localPosition : Vector3.zero).ToArray();
        var background = gameObject.AddComponent<WaveRoomBackground>(); background.Configure(data);
        staticRoot = new GameObject("CellTerrain").transform; staticRoot.SetParent(transform, false);
        var rigNode = new GameObject("CellPanelRig"); rigNode.transform.SetParent(transform, false);
        rig = rigNode.AddComponent<WaveTerrainPanelRig>();
        var rules = WaveRoomRulesSO.Default;
        // 수납구·팔은 일반 지형(순서 0) 바로 뒤, 맞물림 섬광은 지형 선 위에 그린다.
        if (rules != null) rig.Configure(rules.panelRigMaterial, rules.panelFlashMaterial, data.cellMap.styles.Length > 0 ? data.cellMap.styles[0].sortingLayer : 0, -1, 2);
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
    // 스타일에 같은 종류의 전용 Prefab이 있으면 그것을, 없으면 맵 기본 Prefab을 쓴다.
    private WaveTerrainCellView Source(WaveMapCell cell)
    {
        var own = data.cellMap.styles.Length > 0 ? Style(cell).prefab : null;
        if (own != null && own.kind == cell.kind) return own;
        return cell.kind == WaveCellKind.Platform ? data.cellMap.platformPrefab : data.cellMap.oneWayPrefab;
    }
    // 덩어리 외곽선은 묶음(그룹·외형·생성 묶음 번호)마다 따로 기억하고, 요약값이 바뀐 묶음만 다시 만든다.
    // 요약값은 그 묶음의 켜진 칸과 패널로 움직이는 칸으로 정해진다. 수천 칸짜리 기본 지형을 시간별 발판이 생기고 사라질 때마다 다시 만들지 않는다.
    private static ulong OutlineHash(WaveMapCell cell, bool panel, int assembly)
    {
        unchecked
        {
            ulong h = (ulong)(uint)cell.GetHashCode() * 0x9E3779B97F4A7C15ul;
            h ^= (ulong)(uint)assembly * 0xD6E8FEB86659FD93ul;
            if (panel) h = h * 31 + 0x165667B19E3779F9ul;
            h ^= h >> 29;
            // 칸 수도 합에 들어가도록 상수를 더한다.
            return h * 0xBF58476D1CE4E5B9ul + 0x9E3779B97F4A7C15ul;
        }
    }
    private (int group, int style, int assembly) OutlineKey(WaveMapCell cell)
    {
        bool generated = Style(cell).generated;
        // 좌표 보존용 그룹 경계를 지형의 노출 면으로 그리지 않는다. 붙거나 겹친 일반 칸은 합집합의 윤곽만 남긴다.
        // 실제 이동용 Template 앵커와 생성 발판은 기존의 독립 묶음·연출을 유지한다.
        int group = !generated && fixedOutlineGroups.Contains(cell.group) ? 0 : cell.group;
        return (group, cell.style, generated && outlineGroups.TryGetValue(cell, out int id) ? id : 0);
    }
    private void RefreshOutlines()
    {
        outlineSums.Clear(); outlineFixedSums.Clear();
        ulong oneWay = 0;
        (int, int, int) lastKey = default; ulong lastSum = 0, lastFixed = 0; bool hasLast = false;
        foreach (var cell in active)
        {
            bool panel = panels.Count > 0 && panels.ContainsKey(cell);
            if (cell.kind != WaveCellKind.Platform)
            {
                unchecked { oneWay += OutlineHash(cell, panel, outlineGroups.TryGetValue(cell, out int row) ? row : 0); }
                continue;
            }
            if (Style(cell).color.a <= 0) continue;
            var key = OutlineKey(cell);
            ulong hash = OutlineHash(cell, panel, key.assembly);
            ulong fixedHash = panel ? 0 : hash;
            // 대부분의 칸이 같은 묶음(기본 지형)이라 직전 묶음의 합은 따로 들고 있다가 묶음이 바뀔 때만 적는다.
            if (hasLast && key == lastKey) { unchecked { lastSum += hash; lastFixed += fixedHash; } continue; }
            if (hasLast) { outlineSums[lastKey] = lastSum; outlineFixedSums[lastKey] = lastFixed; }
            lastKey = key; hasLast = true;
            unchecked
            {
                lastSum = (outlineSums.TryGetValue(key, out ulong sum) ? sum : 0) + hash;
                lastFixed = (outlineFixedSums.TryGetValue(key, out ulong fixedSum) ? fixedSum : 0) + fixedHash;
            }
        }
        if (hasLast) { outlineSums[lastKey] = lastSum; outlineFixedSums[lastKey] = lastFixed; }

        // 칸이 하나도 남지 않은 묶음: 빌려 쓰던 선을 끈다.
        List<(int, int, int)> gone = null;
        foreach (var row in outlineChunks) if (!outlineSums.ContainsKey(row.Key)) (gone ??= new List<(int, int, int)>()).Add(row.Key);
        if (gone != null)
            foreach (var key in gone)
            {
                foreach (var line in outlineChunks[key].lines) if (line != null) line.enabled = false;
                outlineChunks.Remove(key);
            }
        // 바뀐 묶음만 칸을 모아 다시 만든다.
        Dictionary<(int, int, int), List<WaveMapCell>> changed = null;
        foreach (var row in outlineSums)
        {
            if (outlineChunks.TryGetValue(row.Key, out var built) && built.signature == row.Value) continue;
            // 고정된 칸의 구성이 같은 모양을 이미 알고 있으면 칸을 모으지 않고 그대로 적용한다.
            // 패널로 움직이는 칸이 달라도 남은 덩어리가 같으면 같은 모양이다(Wave 전환의 시작과 끝 모두 입장 로딩이 넣어 둔 모양에 맞는다).
            if (outlineShapes.TryGetValue(row.Key, out var shapes) && shapes.TryGetValue(outlineFixedSums[row.Key], out var shape)) { ApplyOutlineShape(OutlineChunkFor(row.Key, row.Value), shape); continue; }
            (changed ??= new Dictionary<(int, int, int), List<WaveMapCell>>())[row.Key] = new List<WaveMapCell>();
        }
        if (changed != null)
        {
            foreach (var cell in active)
                if (cell.kind == WaveCellKind.Platform && Style(cell).color.a > 0 && changed.TryGetValue(OutlineKey(cell), out var members)) members.Add(cell);
            foreach (var row in changed) BuildOutlineChunk(row.Key, row.Value, outlineSums[row.Key], outlineFixedSums[row.Key]);
        }
        if (oneWayBuilt && oneWay == oneWaySignature) return;
        oneWayBuilt = true; oneWaySignature = oneWay;
        BuildOneWayLines();
    }
    // 묶음의 외곽선 기록을 새 요약값으로 바꾸고, 쓰고 있던 선을 끈다.
    private OutlineChunk OutlineChunkFor((int group, int style, int assembly) key, ulong signature)
    {
        if (!outlineChunks.TryGetValue(key, out var chunk)) outlineChunks[key] = chunk = new OutlineChunk();
        foreach (var line in chunk.lines) if (line != null) line.enabled = false;
        chunk.lines.Clear();
        chunk.signature = signature;
        return chunk;
    }
    // 기본 지형 덩어리(아래에서 위, 왼쪽에서 오른쪽 순서의 칸)의 윤곽을 따라가 선마다의 점을 만든다.
    private OutlineShape TraceOutlineShape(WaveMapCell[] cells, float inset)
    {
        var lineOwners = new List<WaveMapCell>(); var positions = new List<Vector3[]>();
        int index = 0;
        foreach (var contour in WaveCellContours.Trace(cells.Select(c => c.position), inset, false))
        {
            // 연결된 덩어리에 연속 선을 써 원본 UV·발광 패턴을 보존한다.
            var owner = cells[index++];
            // 변마다 양 끝 점을 넣는다(셀 경계의 점까지 있는 예전과 같은 점 구성이라 선의 무늬 위치가 그대로다).
            // 덩어리 외곽선은 고정된 칸만으로 만들므로 좌표는 칸 좌표에서 그룹 원점만 빼면 된다(움직이는 칸은 자기 테두리를 따로 그린다).
            var origin = owners[owner].origin;
            var points = new Vector3[contour.Length * 2];
            for (int i = 0; i < contour.Length; i++) { points[i * 2] = contour[i] - origin; points[i * 2 + 1] = contour[(i + 1) % contour.Length] - origin; }
            lineOwners.Add(owner); positions.Add(points);
        }
        return new OutlineShape { owners = lineOwners.ToArray(), positions = positions.ToArray() };
    }
    // 기본 지형 덩어리의 외곽선을 칸의 테두리 선(빌려 온 것)에 적용한다.
    private void ApplyOutlineShape(OutlineChunk chunk, OutlineShape shape)
    {
        var rules = WaveRoomRulesSO.Default;
        for (int i = 0; i < shape.owners.Length; i++)
        {
            var owner = shape.owners[i]; var line = pool[owner].edges[0];
            chunk.lines.Add(line);
            line.sharedMaterial = rules.terrainLineMaterial;
            line.widthMultiplier = rules.terrainLineWidth;
            line.sortingOrder = Style(owner).sortingOrder;
            line.transform.SetParent(owners[owner].parent, false);
            line.transform.localPosition = Vector3.zero;
            line.positionCount = shape.positions[i].Length; line.SetPositions(shape.positions[i]); line.loop = true; line.enabled = true;
        }
    }
    private void RememberOutlineShape((int group, int style, int assembly) key, ulong signature, OutlineShape shape)
    {
        if (!outlineShapes.TryGetValue(key, out var shapes)) outlineShapes[key] = shapes = new Dictionary<ulong, OutlineShape>();
        if (shapes.Count >= ShapeCacheLimit && !shapes.ContainsKey(signature)) shapes.Clear();
        shapes[signature] = shape;
    }
    // 한 묶음의 덩어리 외곽선과 이어진 그림을 다시 만든다.
    // signature: 이 묶음의 요약값(다시 만들지 판단). shapeKey: 고정된 칸만의 요약값(모양 캐시의 열쇠).
    private void BuildOutlineChunk((int group, int style, int assembly) key, List<WaveMapCell> members, ulong signature, ulong shapeKey)
    {
        var chunk = OutlineChunkFor(key, signature);
        // 전용 그림 셀은 외곽선과 같은 묶음 안의 이웃에 맞춰 이어진 그림을 고른다(RuleTile처럼 놓기만 하면 이어진다).
        // 패널로 움직이는 칸까지 넣은 완성된 모양으로 골라, 패널이 맞물릴 때 그림이 바뀌지 않게 한다.
        if (pool[members[0]].ownVisual)
        {
            var cellByPosition = new HashSet<Vector2Int>(members.Select(c => c.position));
            foreach (var cell in members) pool[cell].SetConnection(WaveTerrainCellView.ConnectionMask(cell.position, cellByPosition.Contains));
        }
        // 패널로 움직이는 칸은 자기 테두리를 따로 그리므로 덩어리 외곽선에서 뺀다. 남은 덩어리는 그 칸과 맞닿은 면에도 선이 생긴다.
        var cells = members.Where(c => !panels.ContainsKey(c)).OrderBy(c => c.position.y).ThenBy(c => c.position.x).ToArray();
        foreach (var cell in cells) pool[cell].SetOutline(0);
        if (cells.Length == 0) return;
        var rules = WaveRoomRulesSO.Default;
        bool generated = Style(cells[0]).generated;
        if (!generated && rules != null)
        {
            // 기본 지형: 윤곽을 따라간 모양을 기억해 두고(큰 덩어리만) 적용한다.
            var shape = TraceOutlineShape(cells, rules.terrainLineInset);
            // 전용 그림 셀(이웃에 맞춰 그림을 고르는 것)은 기억하지 않는다. 캐시로 건너뛰면 위의 그림 고르기도 건너뛰게 된다.
            if (cells.Length >= ShapeCacheMinimum && !pool[members[0]].ownVisual) RememberOutlineShape(key, shapeKey, shape);
            ApplyOutlineShape(chunk, shape);
            return;
        }
        float inset = generated ? 0 : .025f;
        int rendererIndex = 0;
        foreach (var contour in WaveCellContours.Trace(cells.Select(c => c.position), inset, generated))
        {
            // 연결된 덩어리에 연속 선을 써 원본 UV·발광 패턴을 보존한다. 생성 외형의 선은 칸 아래에 그대로 둔다.
            var owner = cells[rendererIndex++]; var line = pool[owner].edges[0];
            var center = (Vector2)owner.position + Vector2.one * .5f;
            chunk.lines.Add(line);
            line.positionCount = contour.Length; line.SetPositions(contour.Select(p => (Vector3)(p - center)).ToArray()); line.loop = true; line.enabled = true;
        }
    }
    // OneWay 발판의 윗면 선: 같은 높이에서 이어진 칸마다 선 하나. OneWay는 수가 적어 바뀔 때마다 전부 다시 만든다.
    private void BuildOneWayLines()
    {
        foreach (var cell in active)
            if (cell.kind == WaveCellKind.OneWayPlatform && !panels.ContainsKey(cell))
                foreach (var line in pool[cell].edges) line.enabled = line.name != "SurfaceLine";
        foreach(var row in active.Where(c=>c.kind==WaveCellKind.OneWayPlatform).GroupBy(c=>(c.group,c.style,c.position.y,assembly:outlineGroups.TryGetValue(c,out int id)?id:0)))
        {
            var run=new List<WaveMapCell>();
            void Finish()
            {
                if(run.Count==0)return;var owner=run[0];var line=pool[owner].edges.First(l=>l.name=="SurfaceLine");
                line.positionCount=2;line.SetPositions(new[]{new Vector3(-.5f,.5f),new Vector3(run[run.Count-1].position.x-owner.position.x+.5f,.5f)});line.loop=false;line.enabled=true;run.Clear();
            }
            var rowCells=new HashSet<int>(row.Select(c=>c.position.x));
            foreach(var cell in row) pool[cell].SetConnection((rowCells.Contains(cell.position.x-1)?WaveTerrainCellView.Left:0)|(rowCells.Contains(cell.position.x+1)?WaveTerrainCellView.Right:0));
            // 패널로 움직이는 칸은 자기 윗면 선을 따로 그리므로 이어진 선에서 뺀다.
            foreach(var cell in row.Where(c=>!panels.ContainsKey(c)).OrderBy(c=>c.position.x)){if(run.Count>0&&cell.position.x!=run[run.Count-1].position.x+1)Finish();run.Add(cell);}Finish();
        }
    }
    private void Update()
    {
        float delta = Time.deltaTime;
        panelClock += Mathf.Min(delta, Mathf.Max(PanelClockStep, usualFrame * 1.5f));
        usualFrame = Mathf.Lerp(usualFrame, delta, .1f);
    }
    private void LateUpdate()
    {
        UpdateCameraSettlement();
        UpdatePanels();
        TrackGroupMotion();
    }
    private IEnumerator WaitPanels(float length)
    {
        float end = panelClock + length;
        while (panelClock < end) yield return null;
    }

    // 기계팔 패널로 움직이는 칸: 실제로 보이는 지형 칸이다. 생성 외형 발판과 OneWay 발판은 공통 규칙에서 끌 수 있다(끄면 Prefab의 등장·소멸 곡선을 쓴다).
    private bool IsPanel(WaveMapCell cell)
    {
        var style = Style(cell);
        if (cell.kind == WaveCellKind.Platform && style.color.a <= 0) return false;
        if (cell.kind == WaveCellKind.Platform && !style.generated) return true;
        var rules = WaveRoomRulesSO.Default;
        return rules == null || rules.panelGeneratedTerrain;
    }
    // 묶음 중심(확대 중심)의 방 좌표. 묶음으로 생성한 칸은 이 점에서 가까운 순서로 끼우고 뺀다.
    private Vector2 RoomPivot(WaveMapCell cell, Vector2 pivot)
    {
        var owner = owners[cell];
        return owner.group == 0 ? pivot : (Vector2)transform.InverseTransformPoint(owner.parent.TransformPoint(pivot - owner.origin));
    }
    // 칸 중심의 방(이 Transform) 좌표. 그룹에 속한 칸은 그룹 Transform의 지금 위치를 따른다.
    // 그룹에 속하지 않은 칸의 채널은 이 Transform 바로 아래에 원점 그대로 놓이므로 변환 없이 구한다(수천 칸을 훑을 때의 비용).
    private Vector2 RoomPoint(WaveMapCell cell)
    {
        var owner = owners[cell];
        return owner.group == 0 ? CellCenter(cell) : (Vector2)transform.InverseTransformPoint(owner.parent.TransformPoint(CellCenter(cell)));
    }
    // 지금 화면에 보이는 범위(방 좌표). 카메라가 없으면 전부 보이는 것으로 본다.
    private Rect VisibleArea(float margin)
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null || !viewCamera.orthographic) return new Rect(-100000, -100000, 200000, 200000);
        Vector2 center = transform.InverseTransformPoint(viewCamera.transform.position);
        var right = transform.InverseTransformVector(viewCamera.transform.right * viewCamera.orthographicSize * viewCamera.aspect);
        var up = transform.InverseTransformVector(viewCamera.transform.up * viewCamera.orthographicSize);
        var half = new Vector2(Mathf.Abs(right.x) + Mathf.Abs(up.x) + margin, Mathf.Abs(right.y) + Mathf.Abs(up.y) + margin);
        return new Rect(center - half, half * 2);
    }
    // 방 좌표 칸마다 보이는 Platform이 몇 장 겹쳐 있는지. 살펴볼 칸(around)이 놓인 범위 안만 센다.
    private Dictionary<Vector2Int, int> Occupancy(IReadOnlyList<WaveMapCell> around)
    {
        var map = new Dictionary<Vector2Int, int>();
        if (around.Count == 0) return map;
        Vector2 min = RoomPoint(around[0]), max = min;
        for (int i = 1; i < around.Count; i++) { Vector2 point = RoomPoint(around[i]); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        min -= Vector2.one; max += Vector2.one;
        foreach (var cell in active)
        {
            if (cell.kind != WaveCellKind.Platform || Style(cell).color.a <= 0) continue;
            // 그룹에 속하지 않은 칸은 칸 좌표가 곧 방 좌표다(RoomPoint의 조회를 거치지 않는다).
            Vector2 point = cell.group == 0 ? (Vector2)cell.position + Vector2.one * .5f : RoomPoint(cell);
            if (point.x < min.x || point.x > max.x || point.y < min.y || point.y > max.y) continue;
            var key = Vector2Int.FloorToInt(point);
            map[key] = map.TryGetValue(key, out int count) ? count + 1 : 1;
        }
        return map;
    }
    // 패널이 퍼져 나가는 기준점: Player가 있는 자리(없으면 화면 가운데).
    private Vector2 PanelFocus()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player != null) return transform.InverseTransformPoint(player.transform.position);
        return VisibleArea(0).center;
    }
    // 패널 묶음의 전체 길이: 패널 한 장의 시간에, 화면 안에서 바뀌는 범위에 비례한 시차를 더한다. 화면 안에 없으면 0이다(기다리지 않는다).
    private float PanelDuration(IReadOnlyList<WaveMapCell> cells)
    {
        Rect view = VisibleArea(3f);
        Vector2 focus = PanelFocus();
        float near = float.MaxValue, far = float.MinValue;
        foreach (var cell in cells)
        {
            if (!IsPanel(cell)) continue;
            Vector2 point = RoomPoint(cell);
            if (!view.Contains(point)) continue;
            float distance = Vector2.Distance(point, focus);
            near = Mathf.Min(near, distance); far = Mathf.Max(far, distance);
        }
        if (far < near) return 0f;
        return WaveTerrainPanelMotion.SingleDuration + (far - near > .01f ? Mathf.Clamp((far - near) * .045f, .15f, .8f) : 0f);
    }
    // 패널 연출을 예약한다. 화면에 보이는 칸은 기준점에서 가까운 순서로 시차를 두고, 안 보이는 칸은 바로 시작한다.
    // single: 패널 한 장의 시간. 나머지(duration - single)가 시차로 쓰인다.
    // pivotOrigins: 켜면 칸마다 자기 묶음의 확대 중심을 시차의 기준으로 삼는다(여러 묶음을 한꺼번에 예약해도 묶음마다 중심에서 번진다). 끄면 Player가 있는 자리가 기준이다.
    private void SchedulePanels(IReadOnlyList<WaveMapCell> cells, bool install, float duration, float single, bool pivotOrigins = false, bool driveSound = true)
    {
        if (cells.Count == 0) return;
        var rules = WaveRoomRulesSO.Default;
        Rect view = VisibleArea(3f);
        Vector2 focus = PanelFocus();
        var distances = new float[cells.Count];
        float near = float.MaxValue, far = float.MinValue;
        Vector2 sum = Vector2.zero; int shownCount = 0;
        // 묶음 중심 기준일 때는 거리 범위를 중심(묶음)마다 따로 잡는다. 크기가 다른 묶음을 한꺼번에 예약해도
        // 묶음마다 자기 중심에서 가장자리까지 같은 시간에 번진다(묶음을 하나씩 예약하던 때와 같은 모습).
        var origins = pivotOrigins ? new Vector2[cells.Count] : null;
        var ranges = pivotOrigins ? new Dictionary<Vector2, Vector2>() : null;
        for (int i = 0; i < cells.Count; i++)
        {
            Vector2 point = RoomPoint(cells[i]);
            distances[i] = -1;
            if (!view.Contains(point)) continue;
            Vector2 origin = pivotOrigins && animationPivots.TryGetValue(cells[i], out var pivot) ? RoomPivot(cells[i], pivot) : focus;
            distances[i] = Vector2.Distance(point, origin);
            near = Mathf.Min(near, distances[i]); far = Mathf.Max(far, distances[i]);
            sum += point; shownCount++;
            if (ranges == null) continue;
            origins[i] = origin;
            ranges[origin] = ranges.TryGetValue(origin, out var range)
                ? new Vector2(Mathf.Min(range.x, distances[i]), Mathf.Max(range.y, distances[i])) : new Vector2(distances[i], distances[i]);
        }
        single = Mathf.Clamp(single, 0f, duration);
        float stagger = Mathf.Max(0f, duration - single);
        float span = far - near;
        float now = panelClock;
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            uint seed = WaveTerrainPanelMotion.Seed(cell.position, cell.group);
            bool shown = distances[i] >= 0;
            float low = near, width = span;
            if (shown && ranges != null) { var range = ranges[origins[i]]; low = range.x; width = range.y - range.x; }
            // 가까운 칸부터 물결처럼 번지되, 같은 거리의 칸이 한꺼번에 움직이지 않게 조금씩 어긋낸다.
            float delay = shown && width > .01f ? stagger * (.88f * (distances[i] - low) / width + .12f * WaveTerrainPanelMotion.Unit(seed, 5)) : 0f;
            var view2 = pool[cell];
            // 얇은 판(OneWay)은 수납구도 얇으므로 덜 벗어나고 덜 기운다.
            bool thin = cell.kind == WaveCellKind.OneWayPlatform;
            panels[cell] = new PanelMotion
            {
                start = now + delay, length = single, install = install, seed = seed, shown = shown,
                pivot = view2.PanelPivot, half = view2.PanelHalf, reach = thin ? new Vector2(1f, .4f) : Vector2.one, tilt = thin ? .5f : 1f,
                flash = thin || Style(cell).generated ? WaveTerrainPanelRig.GeneratedFlash : WaveTerrainPanelRig.TerrainFlash,
            };
            view2.ShowPanelOutline(rules != null ? rules.terrainLineMaterial : null, rules != null ? rules.terrainLineInset : .025f,
                rules != null ? rules.terrainLineWidth : .05f, Style(cell).sortingOrder);
            if (install) view2.SetVisible(false);
        }
        if (driveSound && shownCount > 0 && rules != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.Play(rules.panelDriveFeedback, transform.TransformPoint(sum / shownCount));
    }
    // 패널 연출을 끝내고 칸을 제자리 모습으로 되돌린다.
    private void SettlePanel(WaveMapCell cell)
    {
        panels.Remove(cell);
        var view = pool[cell];
        view.HidePanelOutline();
        view.SetAmount(1);
        view.transform.localPosition = CellCenter(cell);
    }
    private void ClearPanels()
    {
        foreach (var cell in panels.Keys.ToArray()) SettlePanel(cell);
        seams.Clear();
        if (rig != null && rigFilled) { rig.Clear(); rigFilled = false; }
    }
    // 방금 끼운 패널이 같은 덩어리의 이웃과 맞닿은 변(이음매)을 기록한다. 덩어리 외곽선으로 바뀐 뒤 잠깐 남았다가 사라진다.
    private void AddSeams(IReadOnlyList<WaveMapCell> cells)
    {
        if (cells.Count == 0) return;
        Rect view = VisibleArea(1f);
        var toRig = rig.transform.worldToLocalMatrix;
        var rules = WaveRoomRulesSO.Default;
        foreach (var cell in cells)
        {
            // OneWay는 윗면 선 하나뿐이라 이음매가 없다.
            if (cell.kind != WaveCellKind.Platform || !view.Contains(RoomPoint(cell))) continue;
            bool generated = Style(cell).generated;
            int assembly = generated && outlineGroups.TryGetValue(cell, out int id) ? id : 0;
            // 같은 덩어리 외곽선으로 합쳐지는 이웃과 맞닿은 변만 이음매다(생성 외형은 같은 묶음끼리만 합쳐진다).
            bool Joined(Vector2Int step)
            {
                var next = cell; next.position += step;
                return active.Contains(next) && (!generated || (outlineGroups.TryGetValue(next, out int other) ? other : 0) == assembly);
            }
            int mask = (Joined(Vector2Int.up) ? 1 : 0) | (Joined(Vector2Int.right) ? 2 : 0) | (Joined(Vector2Int.down) ? 4 : 0) | (Joined(Vector2Int.left) ? 8 : 0);
            if (mask == 0) continue;
            var line = generated && pool[cell].edges.Length > 0 ? pool[cell].edges[0].sharedMaterial : rules != null ? rules.terrainLineMaterial : null;
            seams.Add(new Seam
            {
                cell = toRig * owners[cell].parent.localToWorldMatrix * Matrix4x4.Translate(CellCenter(cell)), mask = mask, start = panelClock,
                color = LineColor(line), inset = generated ? 0f : rules != null ? rules.terrainLineInset : .025f,
                width = generated ? pool[cell].outlineWidth : rules != null ? rules.terrainLineWidth : .05f,
            });
        }
    }
    // 외곽선 재질과 같은 발광색(재질의 발광색 × 세기).
    private static Color LineColor(Material material)
    {
        if (material == null || !material.HasProperty("_EmissionColor")) return new Color(.88f, 4.5f, .88f, 1f);
        var color = material.GetColor("_EmissionColor") * (material.HasProperty("_Intensity") ? material.GetFloat("_Intensity") : 1f);
        color.a = 1f;
        return color;
    }
    // 움직이는 패널의 자세를 이 프레임의 시간으로 정하고, 화면 안 패널의 수납구·팔·섬광을 한 Mesh로 그린다.
    private void UpdatePanels()
    {
        if (panels.Count == 0 && seams.Count == 0)
        {
            if (rigFilled) { rig.Clear(); rigFilled = false; }
            return;
        }
        var rules = WaveRoomRulesSO.Default;
        var toRig = rig.transform.worldToLocalMatrix;
        float now = panelClock;
        rig.Begin();
        foreach (var row in panels)
        {
            var cell = row.Key; var motion = row.Value; var view = pool[cell];
            Vector3 center = CellCenter(cell);
            float t = (now - motion.start) / Mathf.Max(.0001f, motion.length);
            if (t <= 0f)
            {
                // 차례가 오기 전: 끼울 칸은 아직 없고, 뺄 칸은 제자리에서 테두리(이음매)만 드러난다.
                if (motion.install) view.SetVisible(false);
                else { view.SetPanelPose(center, 0, 1, 1); view.SetPanelOutline(1); }
                continue;
            }
            if (t >= 1f)
            {
                // 차례가 끝난 뒤: 끼운 칸은 제자리에 테두리를 두고, 뺀 칸은 숨긴다. 묶음 전체가 끝나면 덩어리 외곽선으로 바뀐다.
                if (motion.install) { view.SetPanelPose(center, 0, 1, 1); view.SetPanelOutline(1); }
                else view.SetVisible(false);
                continue;
            }
            var pose = WaveTerrainPanelMotion.Evaluate(t, motion.install, motion.seed);
            pose.offset = Vector2.Scale(pose.offset, motion.reach);
            pose.angle *= motion.tilt;
            // 패널 그림의 중심을 축으로 기울이고 키운다(OneWay의 판은 칸 중심이 아니라 위쪽에 있다).
            Vector3 pivot = motion.pivot;
            Vector3 turned = Quaternion.Euler(0f, 0f, pose.angle) * (pivot * pose.scale);
            // 움직이는 동안은 고정된 이웃 칸보다 앞에 그린다(들린 패널이 이웃을 살짝 덮는다).
            view.SetPanelPose(center + pivot - turned + (Vector3)pose.offset + Vector3.back * .02f, pose.angle, pose.scale, pose.brightness);
            view.SetPanelOutline(pose.outline);
            if (!motion.shown) continue;
            rig.Add(toRig * owners[cell].parent.localToWorldMatrix * Matrix4x4.Translate(center + pivot), pose, motion.seed, motion.half, motion.flash);
            if (motion.sounded || t < (motion.install ? WaveTerrainPanelMotion.LockTime : WaveTerrainPanelMotion.UnlockTime)) continue;
            motion.sounded = true;
            if (rules != null && FeedbackPlayer.Instance != null)
                FeedbackPlayer.Instance.Play(motion.install ? rules.panelLockFeedback : rules.panelReleaseFeedback, owners[cell].parent.TransformPoint(center + pivot));
        }
        if (seams.Count > 0)
        {
            for (int i = seams.Count - 1; i >= 0; i--)
            {
                var seam = seams[i];
                float fade = 1f - (now - seam.start) / SeamFade;
                if (fade <= 0f) { seams.RemoveAt(i); continue; }
                // 선이 가늘어지며 꺼진다(가산 혼합이라 색만 줄인다).
                var color = seam.color * (fade * fade);
                color.a = 1f;
                rig.AddSeam(seam.cell, seam.mask, color, seam.inset, seam.width * Mathf.Lerp(.35f, 1f, fade));
            }
        }
        rig.End();
        rigFilled = true;
    }
    // 코스가 그룹 Transform(천장·게이트·벽)을 옮기면 움직임이 멈춘 프레임에 연결 공간을 다시 구한다.
    // 지형 초기화와 코스 초기화는 같은 이벤트를 듣기 때문에 순서와 관계없이 마지막 위치를 따라간다.
    private void TrackGroupMotion()
    {
        if (!automaticRoomSpace) return;
        bool moved = false;
        for (int i = 0; i < groupAnchors.Length; i++)
        {
            if (groupAnchors[i] == null) continue;
            var position = groupAnchors[i].localPosition;
            if (position == groupPositions[i]) continue;
            groupPositions[i] = position; moved = true;
        }
        if (moved) solidsDirty = true;
        if (moved) { roomSpaceDirty = true; return; }
        if (!roomSpaceDirty) return;
        // 조각들이 시차를 두고 움직이는 동안에는 중간 모양으로 공간을 바꾸지 않는다.
        foreach (var stage in courseStages) if (stage != null && stage.IsMoving) return;
        roomSpaceDirty = false;
        RefreshRoomSpace(roomSpaceReset);
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
            anchor = groupAnchors[cell.group - 1]; origin = binding.origin;
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
        var settings = Source(cell).GetComponent<CompositeCollider2D>();
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
        var view = Source(cell).CreateVisual(owner.parent);
        view.ApplyStyle(Style(cell));
        view.name = $"{cell.kind}_{cell.position.x}_{cell.position.y}";
        view.transform.localPosition = (Vector2)cell.position - owner.origin + Vector2.one * .5f;
        view.gameObject.SetActive(false); pool.Add(cell, view); owners.Add(cell, owner);
        if (cell.style > maxStyle) maxStyle = cell.style;
    }
    private void Collision(WaveMapCell cell, bool enabled)
    {
        // 이미 같은 상태면 Tile을 다시 쓰지 않는다. 같은 Tile을 다시 써도 충돌 형상을 처음부터 다시 만든다.
        if (enabled ? !solidCells.Add(cell) : !solidCells.Remove(cell)) return;
        var owner = owners[cell];
        var point = Vector2Int.RoundToInt((Vector2)cell.position - owner.origin);
        if (cell.kind == WaveCellKind.OneWayPlatform) point.y = 0;
        var coordinate = (Vector3Int)point;
        // 전용 Prefab이 충돌 Tile을 정하면(시범 생성 발판: 셀 경계와 같은 Grid 충돌) 그것을 쓴다.
        var platformTile = Source(cell).collisionTile != null ? Source(cell).collisionTile : Style(cell).tile;
        owner.map.SetTile(coordinate, enabled ? cell.kind == WaveCellKind.Platform ? platformTile : oneWayTile : null);
        if (enabled && cell.kind == WaveCellKind.Platform)
        {
            owner.map.SetTileFlags(coordinate, TileFlags.None);
            owner.map.SetTransformMatrix(coordinate, Style(cell).transform);
        }
    }
    private void Rebuild(bool resetting = false)
    {
        RebuildTerrain();
        RefreshRoomSpace(resetting);
    }
    // 외곽선과 충돌 형상만 다시 만든다(연결 공간은 그대로 둔다).
    private void RebuildTerrain()
    {
        RefreshOutlines();
        ProcessCollision();
    }
    // 바뀐 충돌 Tile을 지금 물리에 반영한다.
    private void ProcessCollision()
    {
        // 이 지형이 만들지 않은 Composite는 물리에서 뺀다. 원본 Tilemap의 것은 Configure에서 지웠으므로(파괴는 그 프레임 끝)
        // 처음 몇 번과, 커스텀 예약 Prefab이 놓여 있는 동안에만 전체를 훑는다.
        if (legacyCompositeChecks > 0 || customObjects.Count > 0)
        {
            if (legacyCompositeChecks > 0) legacyCompositeChecks--;
            var owned = new HashSet<CompositeCollider2D>(channels.Values.Select(c => c.composite));
            foreach (var old in GetComponentsInChildren<CompositeCollider2D>(true))
                if (!owned.Contains(old) && old.attachedRigidbody != null) old.attachedRigidbody.simulated = false;
        }
        foreach (var channel in channels.Values) if (channel.collider != null && channel.collider.isActiveAndEnabled) channel.collider.ProcessTilemapChanges();
        foreach (var composite in channels.Values.Select(c => c.composite).Distinct())
            if (composite != null && composite.gameObject.activeInHierarchy && composite.generationType == CompositeCollider2D.GenerationType.Manual) composite.GenerateGeometry();
    }
    private void OnEnable() { if (IsPreloaded) Rebuild(); }
    private void Start() { if (IsPreloaded) Rebuild(); }
    // 방을 떠나면 진행 중이던 패널 연출을 제자리 모습으로 마친다(코루틴은 비활성화와 함께 멈춘다).
    private void OnDisable()
    {
        RestoreCarryCameraUpdate();
        CancelCameraSettlement(true);
        if (IsPreloaded) ClearPanels();
    }

    private void ResetMap()
    {
        RestoreCarryCameraUpdate();
        CancelCameraSettlement(true);
        roomSpaceReset = true;
        StopAllCoroutines(); ClearPanels(); ClearCustom(); temporary.Clear(); persistent.Clear(); outlineGroups.Clear(); animationPivots.Clear(); nextOutlineGroup = 0; expiry.Clear(); generations.Clear(); removing.Clear(); IsTransitioning = false;
        ApplyBackground();
        roomSolids = null;
        ConnectedBounds = null;
        backdropBounds = null;
        ApplyRoomPresentation(WaveRoomState.LayoutAt(data, 0));
        var basis = InitialFor(data.cellMap.cells);
        initial = basis.cells; ApplyInitialPivots(basis);
        // 기본 맵에 없는 칸만 끄고, 빠져 있는 칸만 켠다. 이미 제자리에 켜져 있는 칸(대부분)은 건드리지 않는다.
        foreach (var cell in active.ToArray())
            if (!initial.Contains(cell)) { pool[cell].gameObject.SetActive(false); Collision(cell, false); active.Remove(cell); }
        foreach (var cell in data.cellMap.cells)
        {
            bool added = active.Add(cell);
            if (!added && !unsettled.Contains(cell)) continue;
            Preload(cell);
            if (added) pool[cell].gameObject.SetActive(true);
            pool[cell].SetAmount(1); pool[cell].transform.localPosition = CellCenter(cell); Collision(cell, true);
        }
        unsettled.Clear();
        solidsDirty = true;
        Rebuild(true);
        // 다음 Wave 준비 전까지 코스가 지형을 되돌려도 첫 Wave 시작점 기준으로 공간을 고른다.
        roomSpaceReset = true;
    }

    public IEnumerator Prepare(int index)
    {
        roomSpaceReset = false;
        // EndEvents가 외곽선·충돌·연결 공간까지 다시 맞춘다.
        EndEvents();
        var next = InitialFor(WaveCellState.Initial(data, index));
        initial = next.cells; ApplyInitialPivots(next);
        // 새 초기맵이 소유한 셀에는 이전 임시 생성의 수명을 넘기지 않는다.
        var initialKeys = next.keys;
        if (expiry.Count > 0) foreach (var cell in expiry.Keys.Where(c => initialKeys.Contains((c.position, c.group))).ToArray()) expiry.Remove(cell);
        if (persistent.Count > 0) persistent.RemoveWhere(c => initialKeys.Contains((c.position, c.group)));
        var desired = initial;
        if (persistent.Count > 0) { desired = new HashSet<WaveMapCell>(initial); desired.UnionWith(persistent); }
        var remove = active.Where(c => !desired.Contains(c)).ToArray();
        var add = desired.Where(c => !active.Contains(c)).ToArray();
        IsTransitioning = true;
        // 벽이 빠지기 전에, 바뀐 뒤의 연결 공간까지 배경·통창을 미리 넓혀 둔다. 패널이 빠진 자리 뒤로 배경이 바로 보인다.
        if (remove.Length > 0) ExpandBackdropFor(Solids(desired), WaveRoomState.PlayerStartAt(data, index));
        // 확장 바닥을 먼저 완성한다. 새 경로가 생기기 전에 기존 바닥을 제거하지 않는다.
        // 일반 지형은 칸마다 기계팔 패널로 끼우고 빼므로, 화면 안에서 바뀌는 범위만큼 시차가 붙는다(길이 -1 = 자동).
        // 정리·끼우기·빼기의 준비 작업(외곽선·충돌 다시 만들기)이 한 프레임에 몰리지 않게 한 프레임씩 띄운다.
        if (add.Length > 0) { yield return null; yield return Animate(add, true, -1); }
        if (remove.Length > 0) { yield return null; yield return Animate(remove, false, -1); }
        var layout = WaveRoomState.LayoutAt(data, index);
        if (waves.SpawnRoot != null) waves.SpawnRoot.position = transform.TransformPoint(layout.spawnOffset);
        ApplyRoomPresentation(layout);
        // 끼우기·빼기가 LateUpdate로 미뤄 둔 연결 공간 갱신을 여기서 먼저 한다. 아래 판단이 바뀐 뒤의 공간을 봐야 한다(전환 중이라 배경은 아직 줄지 않는다).
        if (roomSpaceDirty && (add.Length > 0 || remove.Length > 0)) { roomSpaceDirty = false; RefreshRoomSpace(); }
        // 전환 동안 넓혀 두었던 배경·통창을 바뀐 뒤의 공간에 맞춘다. 줄일 것이 있으면 지형을 다시 만든 프레임과 겹치지 않게 한 프레임 띄운다.
        if (automaticRoomSpace && backdropBounds.HasValue && ConnectedBounds.HasValue && backdropBounds.Value != ConnectedBounds.Value) yield return null;
        IsTransitioning = false;
        RefreshRoomSpace();
    }
    public void RefreshPlayerSpace() => RefreshRoomSpace();
    /// <summary>
    /// 크레인이 Player를 from에서 to(방 좌표)로 옮기기 시작한다. 끝낼 때까지 카메라와 배경이 출발한 공간, 도착할 공간, 그 사이 경로를 모두 담는다.
    /// 지형이 바뀌어 두 공간이 나뉘거나 이어져도 옮겨지는 Player가 화면에서 벗어나지 않는다. EndCarry로 끝낸다.
    /// </summary>
    public void BeginCarry(Vector2 from, Vector2 to, float lift)
    {
        CancelCameraSettlement(true);
        carrying = true; carryFrom = from; carryTo = to; carryLift = Mathf.Max(0f, lift);
        if (carryBrain == null)
        {
            var camera = Camera.main;
            if (camera != null && camera.TryGetComponent(out carryBrain))
            {
                carryBrainUpdate = carryBrain.UpdateMethod;
                carryBrainBlendUpdate = carryBrain.BlendUpdateMethod;
                // 물리 틱으로 움직이는 Player를 SmartUpdate가 Fixed로 분류하면 빠른 하강에서 같은 카메라 프레임이 반복된다.
                // 이동 물리는 유지하고 Follow 감쇠와 최종 출력만 렌더 프레임마다 계산한다.
                carryBrain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
                carryBrain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
            }
        }
        if (carryFollow == null && roomConfiner != null && roomConfiner.TryGetComponent(out carryFollow))
        {
            carryFollowDamping = carryFollow.TrackerSettings.PositionDamping;
            var damping = carryFollowDamping;
            damping.x = Mathf.Min(damping.x, CarryCameraDamping); damping.y = Mathf.Min(damping.y, CarryCameraDamping);
            carryFollow.TrackerSettings.PositionDamping = damping;
        }
        forceRoomSpace = true;
        RefreshRoomSpace();
    }
    /// <summary>
    /// 크레인 이동이 끝났다. 카메라와 배경을 Player가 있는 공간에 맞춘다.
    /// 도중에 끊겨 Player가 시작 좌표에 닿지 못했으면(arrived = false) 커스텀 실내의 카메라 범위는 새 실내 크기로 줄이지 않고 넓혀 둔 채로 둔다
    /// (줄이면 옮겨지던 자리에 남은 Player가 범위 밖에 놓인다. 다음 준비·초기화가 다시 맞춘다).
    /// </summary>
    public void EndCarry(bool arrived = true)
    {
        if (!carrying) return;
        carrying = false;
        if (carryFollow != null) { carryFollow.TrackerSettings.PositionDamping = carryFollowDamping; carryFollow = null; }
        forceRoomSpace = true;
        RefreshRoomSpace(immediateCamera: !arrived);
        if (arrived && pendingPresentationCamera.HasValue) SetPresentationCamera(pendingPresentationCamera.Value);
        pendingPresentationCamera = null;
        if (!IsCameraSettling) RestoreCarryCameraUpdate();
    }
    private void RestoreCarryCameraUpdate()
    {
        if (carryBrain == null) return;
        carryBrain.UpdateMethod = carryBrainUpdate;
        carryBrain.BlendUpdateMethod = carryBrainBlendUpdate;
        carryBrain = null;
    }
    /// <summary>
    /// index번째 Wave로 넘어가면 방 좌표 사각형 area에 지형(Platform) 칸이 새로 생기는지. 그 Wave의 지형을 준비하기 전에 묻는다.
    /// 시작 좌표 근처에 서 있어 옮기지 않으려는 Player의 자리가 새 지형에 묻히는지 볼 때 쓴다.
    /// </summary>
    public bool WillAddTerrain(int index, Rect area)
    {
        Vector2Int min = Vector2Int.FloorToInt(area.min), max = Vector2Int.FloorToInt(area.max);
        foreach (var cell in InitialFor(WaveCellState.Initial(data, index)).cells)
        {
            // 그룹 위치를 따라가는 칸은 미리 만들어 둔 것만 자리를 알 수 있다(실행 중 편집으로 방금 더한 칸은 건너뛴다).
            if (cell.kind != WaveCellKind.Platform || active.Contains(cell) || (cell.group != 0 && !owners.ContainsKey(cell))) continue;
            var at = RoomCell(cell);
            if (at.x < min.x || at.x > max.x || at.y < min.y || at.y > max.y) continue;
            // 같은 자리에 이미 다른 지형 칸이 켜져 있으면 새로 막히는 것이 아니다.
            bool occupied = false;
            foreach (var other in active)
            {
                if (other.kind != WaveCellKind.Platform || RoomCell(other) != at) continue;
                occupied = true;
                break;
            }
            if (!occupied) return true;
        }
        return false;
    }
    // 칸이 놓인 방 좌표 칸. 그룹에 묶이지 않은 칸은 저장된 좌표가 곧 방 좌표라 변환을 건너뛴다(수천 칸을 훑을 때 쓴다).
    private Vector2Int RoomCell(WaveMapCell cell) => cell.group == 0 ? cell.position : Vector2Int.FloorToInt(RoomPoint(cell));
    // 옮기는 경로를 감싸는 범위(들어 올린 높이와 상자 크기만큼 여유를 둔다).
    private Rect CarryPath()
        => Rect.MinMaxRect(Mathf.Min(carryFrom.x, carryTo.x) - 2f, Mathf.Min(carryFrom.y, carryTo.y) - 1f,
            Mathf.Max(carryFrom.x, carryTo.x) + 2f, Mathf.Max(carryFrom.y, carryTo.y) + carryLift + 3f);

    /// <summary>입장 로딩을 마쳤는지. 같은 방 내용으로 다시 들어오면 다시 하지 않는다.</summary>
    public bool IsWarm { get; private set; }
    private const float WarmFrameBudget = .006f;
    /// <summary>
    /// TrainingRoom 입장 로딩: Wave가 넘어갈 때 쓸 것을 미리 만들고 계산해 둔다. report는 (0~1 진행률, 단계 이름)이다.
    /// 하지 않아도 동작은 같다(그때그때 만든다). 여러 프레임에 나눠 실행하므로 StartCoroutine으로 부른다.
    /// </summary>
    public IEnumerator Warm(Action<float, string> report)
    {
        if (IsWarm) yield break;
        float slice = Time.realtimeSinceStartup;
        report?.Invoke(0f, "TERRAIN");
        // 1. Wave마다의 시작 지형: 조회용 집합과 큰 덩어리의 외곽선 모양
        var sources = new List<WaveMapCell[]> { data.cellMap.cells };
        for (int i = 0; i < data.waves.Length; i++)
        {
            var source = WaveCellState.Initial(data, i);
            if (!sources.Contains(source)) sources.Add(source);
        }
        for (int i = 0; i < sources.Count; i++)
        {
            InitialFor(sources[i]);
            WarmOutlineShapes(sources[i]);
            report?.Invoke(.3f * (i + 1) / sources.Count, "TERRAIN");
            if (Time.realtimeSinceStartup - slice < WarmFrameBudget) continue;
            yield return null; slice = Time.realtimeSinceStartup;
        }
        // 2. 앞으로 나올 연결 공간을 미리 구해, 거기에 필요한 반복 배경 모듈을 모두 만들어 둔다
        if (automaticRoomSpace && automaticBackdrop != null)
        {
            var spaces = PredictSpaces();
            yield return null;
            yield return automaticBackdrop.Prewarm(spaces, value => report?.Invoke(.3f + .65f * value, "BACKDROP"), WarmFrameBudget);
        }
        // 3. 처음 실행될 때 번역되는 코드를 미리 한 번씩 부른다
        WarmCode();
        IsWarm = true;
        report?.Invoke(1f, "TERRAIN");
    }
    // 이 시작 지형의 큰 기본 지형 덩어리 모양을 미리 따라가 둔다(Wave가 이 지형으로 바뀌는 순간에 다시 따라가지 않는다).
    private void WarmOutlineShapes(WaveMapCell[] source)
    {
        var rules = WaveRoomRulesSO.Default;
        if (rules == null) return;
        foreach (var group in source.Where(c => c.kind == WaveCellKind.Platform && !Style(c).generated && Style(c).color.a > 0).GroupBy(OutlineKey))
        {
            var cells = group.Distinct().OrderBy(c => c.position.y).ThenBy(c => c.position.x).ToArray();
            if (cells.Length < ShapeCacheMinimum) continue;
            ulong signature = 0;
            foreach (var cell in cells) unchecked { signature += OutlineHash(cell, false, 0); }
            if (outlineShapes.TryGetValue(group.Key, out var shapes) && shapes.ContainsKey(signature)) continue;
            foreach (var cell in cells) Preload(cell);
            if (pool[cells[0]].ownVisual) continue;
            RememberOutlineShape(group.Key, signature, TraceOutlineShape(cells, rules.terrainLineInset));
        }
    }
    // Wave마다의 시작 지형과 코스가 열린 모습에서 생기는 연결 공간을 미리 구한다(지형은 건드리지 않는다).
    // 코스 조각은 잠깐 열린 자리로 옮겨 공간만 구하고 곧바로 되돌린다. 예측이 빗나간 공간의 모듈은 그때 만든다.
    private List<Rect> PredictSpaces()
    {
        var spaces = new List<Rect>();
        Rect? last = null;
        void Add(HashSet<Vector2Int> solids, Vector2 seed)
        {
            if (!WaveCellRoomSpace.TryGetBounds(solids, seed, out var bounds)) return;
            if (!spaces.Contains(bounds)) spaces.Add(bounds);
            // 전환 도중에는 앞뒤 공간을 합친 범위까지 배경을 넓혀 둔다.
            if (last.HasValue && last.Value != bounds) { var union = Union(last.Value, bounds); if (!spaces.Contains(union)) spaces.Add(union); }
            last = bounds;
        }
        var anchors = new List<(Transform anchor, Vector3 rest)>();
        void Open(TrainingCourseStage stage)
        {
            for (int i = 0; i < stage.SegmentCount; i++)
            {
                var map = stage.SegmentTilemap(i);
                if (map == null) continue;
                anchors.Add((map.transform, map.transform.localPosition));
                map.transform.localPosition += stage.SegmentOpenOffset(i);
            }
        }
        WaveMapCell[] lastSource = null; HashSet<Vector2Int> rest = null;
        try
        {
            for (int i = 0; i < data.waves.Length; i++)
            {
                var source = WaveCellState.Initial(data, i);
                if (!ReferenceEquals(source, lastSource)) { foreach (var cell in source) Preload(cell); rest = Solids(source); lastSource = source; }
                Vector2 start = WaveRoomState.PlayerStartAt(data, i);
                Add(rest, start);
                foreach (var info in data.waves[i].spawnObjects ?? Array.Empty<WaveSpawnInfo>())
                {
                    var activator = info.prefab != null ? info.prefab.GetComponent<TrainingCourseActivator>() : null;
                    var stage = activator != null ? Array.Find(courseStages, s => s != null && s.CourseKey == activator.CourseKey) : null;
                    if (stage == null) continue;
                    Open(stage);
                    Add(Solids(source), start);
                    if (stage.HandoffStage != null)
                    {
                        // 이 구간을 마치면 그 자리에서 다음 구간이 열린다(달리기 → Arena). 다음 Wave의 시작점에서 본 공간이다.
                        Open(stage.HandoffStage);
                        Add(Solids(source), i + 1 < data.waves.Length ? WaveRoomState.PlayerStartAt(data, i + 1) : start);
                    }
                    for (int k = anchors.Count - 1; k >= 0; k--) anchors[k].anchor.localPosition = anchors[k].rest;
                    anchors.Clear();
                    last = null;
                    Add(rest, start);
                }
            }
        }
        finally
        {
            for (int k = anchors.Count - 1; k >= 0; k--) anchors[k].anchor.localPosition = anchors[k].rest;
        }
        return spaces;
    }
    // 빈 입력으로 한 번씩 불러 둔다. 처음 Wave가 넘어가는 프레임에 코드 번역(JIT)이 몰리지 않는다. 상태는 바뀌지 않는다.
    private void WarmCode()
    {
        var none = Array.Empty<WaveMapCell>();
        var run = Animate(none, true, 0); while (run.MoveNext()) { }
        run = Animate(none, false, 0); while (run.MoveNext()) { }
        run = RelocateGroup(null, Vector3.zero, () => { }, 0); while (run.MoveNext()) { }
        SchedulePanels(none, true, 0, 0);
        PanelDuration(none); Occupancy(none); AddSeams(none); SettleGroup(null);
        for (int i = 0; i <= 4; i++) { WaveTerrainPanelMotion.Evaluate(i / 4f, true, 7u); WaveTerrainPanelMotion.Evaluate(i / 4f, false, 7u); }
        if (rig == null) return;
        // 수납구·팔·섬광·이음매 Mesh를 한 번 채웠다가 같은 프레임에 비운다(화면에는 그려지지 않는다).
        var far = Matrix4x4.Translate(new Vector3(100000f, 100000f, 0f));
        rig.Begin();
        rig.Add(far, WaveTerrainPanelMotion.Evaluate(.5f, true, 7u), 7u, new Vector2(.5f, .5f), WaveTerrainPanelRig.TerrainFlash);
        rig.Add(far, WaveTerrainPanelMotion.Evaluate(.86f, true, 9u), 9u, new Vector2(.5f, .125f), WaveTerrainPanelRig.GeneratedFlash);
        rig.AddSeam(far, 15, Color.white, .025f, .05f);
        rig.End();
        rig.Clear();
        rigFilled = false;
    }
    // 일반 Platform이 차지한 방 좌표 칸. 그룹에 속한 벽도 현재 Transform 위치로 합친다(movedGroup은 shift만큼 옮긴 것으로 본다).
    // 셀의 style·group 번호는 경계를 나누지 않는다.
    private HashSet<Vector2Int> Solids(IEnumerable<WaveMapCell> cells, int movedGroup = 0, Vector2 shift = default)
        => new HashSet<Vector2Int>(cells.Where(c => c.kind == WaveCellKind.Platform)
            .Select(c => Vector2Int.FloorToInt(RoomPoint(c) + (movedGroup > 0 && c.group == movedGroup ? shift : Vector2.zero))));
    // 지형이 바뀌기 전에 배경·통창만 미리 넓힌다. 줄이는 것은 지형이 다 바뀐 뒤 RefreshRoomSpace가 한다.
    private void ExpandBackdropFor(HashSet<Vector2Int> solids, Vector2 start)
    {
        if (!automaticRoomSpace || !ConnectedBounds.HasValue) return;
        Rect union = backdropBounds ?? ConnectedBounds.Value;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player != null && WaveCellRoomSpace.TryGetBounds(solids, transform.InverseTransformPoint(player.transform.position), out var around)) union = Union(union, around);
        if (WaveCellRoomSpace.TryGetBounds(solids, start, out var next)) union = Union(union, next);
        if (backdropBounds.HasValue && backdropBounds.Value == union) return;
        backdropBounds = union;
        automaticBackdrop.Resize(union);
    }
    private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
    private void RefreshRoomSpace(bool resetting = false, bool immediateCamera = false)
    {
        if (!automaticRoomSpace) return;
        // 켜진 Platform 칸과 그룹 위치가 그대로면 지난번에 모은 것을 그대로 쓴다(수천 칸을 호출마다 다시 모으지 않는다).
        bool unchanged = !solidsDirty && roomSolids != null;
        var solids = unchanged ? roomSolids : Solids(active);
        solidsDirty = false;
        Vector2 start = WaveRoomState.PlayerStartAt(data, resetting ? 0 : waves.currentWaveIndex);
        Vector2 seed = start;
        var room = GetComponentInParent<Room>();
        if (!resetting && ConnectedBounds.HasValue && room != null && RoomManager.Instance != null && RoomManager.Instance.CurrentRoom == room
            && GameManager.Instance != null && GameManager.Instance.Player != null)
            seed = transform.InverseTransformPoint(GameManager.Instance.Player.transform.position);
        var seedCell = Vector2Int.FloorToInt(seed);
        // 미리 넓혀 둔 배경이 남아 있으면 공간이 그대로여도 다시 맞춘다.
        bool backdropMatches = backdropBounds.HasValue && ConnectedBounds.HasValue && backdropBounds.Value == ConnectedBounds.Value;
        if (!unchanged && roomSolids != null && roomSolids.SetEquals(solids)) unchanged = true;
        bool force = forceRoomSpace; forceRoomSpace = false;
        // 지형이 그대로이고 기준점이 지난번에 찾은 공간 안에 있으면 같은 공간이다(옮기는 중에는 범위가 경로를 따라 정해지므로 건너뛰지 않는다).
        if (!force && !carrying && unchanged && backdropMatches && (roomSeed == seedCell || roomRegion.Contains(seedCell))) { roomSolids = solids; roomSeed = seedCell; return; }
        roomSolids = solids;
        roomSeed = seedCell;
        var layout = WaveRoomState.LayoutAt(data, resetting ? 0 : waves.currentWaveIndex);
        roomRegion.Clear();
        if (!WaveCellRoomSpace.TryGetBounds(solids, seed, out var bounds, roomRegion))
        {
            roomRegion.Clear();
            if (!WaveCellRoomSpace.TryGetBounds(solids, start, out bounds, roomRegion)) { roomRegion.Clear(); bounds = layout.bounds; }
        }
        if (carrying)
        {
            // 크레인이 옮기는 동안: 출발한 공간과 도착할 공간, 그 사이 경로까지 담는다(Player는 지형을 지나 옮겨진다).
            if (WaveCellRoomSpace.TryGetBounds(solids, carryFrom, out var leaving)) bounds = Union(bounds, leaving);
            if (WaveCellRoomSpace.TryGetBounds(solids, carryTo, out var arriving)) bounds = Union(bounds, arriving);
            bounds = Union(bounds, CarryPath());
        }
        // Wave 전환 중에는 미리 넓혀 둔 배경·통창을 줄이지 않는다(끼우기가 먼저 끝나도 뒤이어 빠질 벽 뒤에 배경이 있어야 한다).
        // 전환이 끝나면 Prepare가 한 번 더 불러 실제 공간에 맞춘다.
        bool cameraChanged = !ConnectedBounds.HasValue || ConnectedBounds.Value != bounds;
        if (cameraChanged) SetCameraBounds(bounds, resetting || immediateCamera || !ConnectedBounds.HasValue);
        // 정착 중인 카메라가 아직 이전 범위를 볼 수 있으므로 배경·통창은 마지막에 줄인다.
        Rect backdrop = (IsTransitioning || IsCameraSettling) && backdropBounds.HasValue ? Union(backdropBounds.Value, bounds) : bounds;
        if (ConnectedBounds.HasValue && ConnectedBounds.Value == bounds && backdropBounds.HasValue && backdropBounds.Value == backdrop) return;
        ConnectedBounds = bounds;
        if (backdropBounds.HasValue && backdropBounds.Value == backdrop) return;
        backdropBounds = backdrop;
        automaticBackdrop.Resize(backdrop);
    }
    private void SetCameraBounds(Rect bounds, bool immediate = false)
    {
        var frame = originalCameraBounds.transform;
        var horizontal = frame.InverseTransformVector(transform.TransformVector(Vector3.right * (bounds.width + 2)));
        var vertical = frame.InverseTransformVector(transform.TransformVector(Vector3.up * (bounds.height + 3)));
        var size = new Vector2(Mathf.Abs(horizontal.x) + Mathf.Abs(vertical.x), Mathf.Abs(horizontal.y) + Mathf.Abs(vertical.y));
        Vector3 center = transform.TransformPoint(bounds.center + Vector2.down * 1.5f);
        Vector3 position = center - frame.TransformVector(originalCameraBounds.offset);
        position.z = frame.position.z;
        ApplyCameraBounds(size, position, immediate);
    }
    private void ApplyRoomPresentation(WaveRoomLayout layout)
    {
        if (automaticRoomSpace) return;
        if (data.adaptCameraToRoom)
        {
            var presentation = GetComponent<TrainingRoomPresentation>();
            if (presentation != null) presentation.Apply(layout.bounds.width * .5f, layout.bounds.height);
            if (originalCameraBounds != null)
            {
                // 크레인이 Player를 옮기는 중이면 지금 범위와 합쳐 두고, 다 옮긴 뒤(EndCarry)에 새 실내 크기로 맞춘다.
                // 방이 줄어드는 Wave에서 아직 옮겨지고 있는 Player가 카메라 범위 밖에 남지 않는다.
                if (carrying && presentationCamera.HasValue && presentationCamera.Value != layout.bounds)
                {
                    pendingPresentationCamera = layout.bounds;
                    SetPresentationCamera(Union(Union(presentationCamera.Value, layout.bounds), CarryPath()));
                }
                else { pendingPresentationCamera = null; SetPresentationCamera(layout.bounds); }
            }
        }
    }
    private void SetPresentationCamera(Rect bounds)
    {
        bool immediate = roomSpaceReset || !presentationCamera.HasValue;
        presentationCamera = bounds;
        Vector3 center = transform.TransformPoint(bounds.center + Vector2.down * 1.5f);
        Vector3 position = center - originalCameraBounds.transform.TransformVector(originalCameraBounds.offset);
        position.z = originalCameraBounds.transform.position.z;
        ApplyCameraBounds(bounds.size + new Vector2(2, 3), position, immediate);
    }
    private void ApplyCameraBounds(Vector2 size, Vector3 position, bool immediate)
    {
        if (IsCameraSettling && size == cameraSettleToSize && position == cameraSettleToPosition && !immediate) return;
        var frame = originalCameraBounds.transform;
        Vector2 shift = frame.InverseTransformVector(position - frame.position);
        var oldHalf = originalCameraBounds.size * .5f;
        var nextHalf = size * .5f;
        bool expands = shift.x - nextHalf.x <= -oldHalf.x && shift.x + nextHalf.x >= oldHalf.x
            && shift.y - nextHalf.y <= -oldHalf.y && shift.y + nextHalf.y >= oldHalf.y;
        var room = GetComponentInParent<Room>();
        bool live = Application.isPlaying && isActiveAndEnabled && room != null
            && RoomManager.Instance != null && RoomManager.Instance.CurrentRoom == room;
        if (immediate || carrying || expands || !live)
        {
            IsCameraSettling = false;
            WriteCameraBounds(size, position);
            if (!carrying) RestoreCarryCameraUpdate();
            return;
        }
        cameraSettleFromSize = originalCameraBounds.size;
        cameraSettleFromPosition = frame.position;
        cameraSettleToSize = size;
        cameraSettleToPosition = position;
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera != null && viewCamera.orthographic)
        {
            // 이동 경로의 넓은 여백 대신 현재 화면과 목적지만 담은 범위에서 시작한다.
            // 그래야 경계가 화면에 닿는 마지막 순간에만 카메라가 빠르게 밀리지 않는다.
            var right = frame.InverseTransformVector(viewCamera.transform.right * viewCamera.orthographicSize * viewCamera.aspect);
            var up = frame.InverseTransformVector(viewCamera.transform.up * viewCamera.orthographicSize);
            var extent = new Vector2(Mathf.Abs(right.x) + Mathf.Abs(up.x), Mathf.Abs(right.y) + Mathf.Abs(up.y)) + Vector2.one * .05f;
            Vector2 cameraPoint = frame.InverseTransformPoint(viewCamera.transform.position);
            Vector2 targetCenter = (Vector2)frame.InverseTransformPoint(position) + originalCameraBounds.offset;
            Vector2 min = Vector2.Min(targetCenter - nextHalf, cameraPoint - extent);
            Vector2 max = Vector2.Max(targetCenter + nextHalf, cameraPoint + extent);
            cameraSettleFromSize = max - min;
            cameraSettleFromPosition = frame.TransformPoint((min + max) * .5f - originalCameraBounds.offset);
            cameraSettleFromPosition.z = position.z;
            WriteCameraBounds(cameraSettleFromSize, cameraSettleFromPosition);
        }
        if (cameraSettleFromSize == size && cameraSettleFromPosition == position)
        {
            IsCameraSettling = false;
            WriteCameraBounds(size, position);
            RestoreCarryCameraUpdate();
            return;
        }
        cameraSettleTime = 0;
        IsCameraSettling = true;
    }
    private void WriteCameraBounds(Vector2 size, Vector3 position)
    {
        bool resized = originalCameraBounds.size != size;
        originalCameraBounds.size = size;
        originalCameraBounds.transform.position = position;
        // 위치 이동만으로는 형상 캐시를 다시 굽지 않는다. 크기가 바뀌는 짧은 정착 구간에서만 갱신한다.
        if (resized) roomConfiner.InvalidateBoundingShapeCache();
    }
    private void UpdateCameraSettlement()
    {
        if (!IsCameraSettling) return;
        cameraSettleTime += Time.deltaTime;
        float t = Mathf.Clamp01(cameraSettleTime / CameraSettleDuration);
        // 시작과 끝의 속도·가속도가 모두 0인 곡선으로 마지막 경계 Clamp도 서서히 멈춘다.
        float eased = t * t * t * (t * (t * 6f - 15f) + 10f);
        WriteCameraBounds(Vector2.Lerp(cameraSettleFromSize, cameraSettleToSize, eased),
            Vector3.Lerp(cameraSettleFromPosition, cameraSettleToPosition, eased));
        if (t < 1f) return;
        IsCameraSettling = false;
        RestoreCarryCameraUpdate();
        RefreshRoomSpace();
    }
    private void CancelCameraSettlement(bool complete)
    {
        if (!IsCameraSettling) return;
        IsCameraSettling = false;
        if (complete && originalCameraBounds != null) WriteCameraBounds(cameraSettleToSize, cameraSettleToPosition);
    }
    // 초기맵 배열의 조회용 집합과 OneWay 줄의 확대 중심을 한 번만 만든다.
    private InitialCache InitialFor(WaveMapCell[] source)
    {
        // 배열을 한 번 훑어 지문을 구하는 비용은 집합을 다시 만드는 것의 수십 분의 일이다.
        ulong signature = (ulong)source.Length;
        unchecked
        {
            foreach (var cell in source)
            {
                signature = signature * 1099511628211UL + (ulong)(uint)cell.position.x;
                signature = signature * 1099511628211UL + (ulong)(uint)cell.position.y;
                signature = signature * 1099511628211UL + (ulong)(uint)cell.group;
                signature = signature * 1099511628211UL + ((ulong)(uint)cell.style << 1 | (ulong)(uint)cell.kind);
            }
        }
        if (initialCaches.TryGetValue(source, out var cache) && cache.signature == signature) return cache;
        cache = new InitialCache { signature = signature, cells = new HashSet<WaveMapCell>(source), keys = new HashSet<(Vector2Int, int)>() };
        foreach (var cell in source) cache.keys.Add((cell.position, cell.group));
        foreach (var row in cache.cells.Where(c=>c.kind==WaveCellKind.OneWayPlatform).GroupBy(c=>(c.group,c.style,c.position.y)))
        {
            var run=new List<WaveMapCell>();
            void Finish()
            {
                if(run.Count==0)return;
                var pivot=new Vector2((run[0].position.x+run[run.Count-1].position.x+1)*.5f,run[0].position.y+.5f);
                foreach(var cell in run)cache.pivots.Add((cell,pivot));run.Clear();
            }
            foreach(var cell in row.OrderBy(c=>c.position.x)) { if(run.Count>0&&cell.position.x!=run[run.Count-1].position.x+1)Finish();run.Add(cell); }
            Finish();
        }
        initialCaches[source] = cache;
        return cache;
    }
    private void ApplyInitialPivots(InitialCache cache)
    {
        foreach (var (cell, pivot) in cache.pivots) animationPivots[cell] = pivot;
    }
    private Vector2 CellCenter(WaveMapCell cell) => (Vector2)cell.position - owners[cell].origin + Vector2.one * .5f;
    private IEnumerator Animate(WaveMapCell[] cells, bool add, float duration, float expiresAt = 0, bool keep = false, bool driveSound = true)
    {
        cells = cells.Distinct().Where(c => add || active.Contains(c)).ToArray();
        var visible = new HashSet<WaveMapCell>(cells.Where(active.Contains));
        var tokens = new Dictionary<WaveMapCell, int>();
        if (cells.Length == 0) yield break;
        solidsDirty = true;
        foreach (var cell in cells)
        {
            Preload(cell);
            unsettled.Add(cell);
            // 다른 묶음이 이 칸을 끼우거나 빼던 중이면 그 패널 연출을 여기서 마감한다(그 묶음은 아래에서 순번을 넘겨받아 끝 처리를 건너뛴다).
            // 마감하지 않으면 연출 항목이 남아 칸이 숨겨지거나 한 칸 테두리가 켜진 채 남는다. 새 연출이 필요하면 아래에서 다시 예약한다.
            if (panels.ContainsKey(cell)) SettlePanel(cell);
            int token = generations.TryGetValue(cell, out var oldToken) ? oldToken + 1 : 1;
            generations[cell] = token; tokens[cell] = token; expiry.Remove(cell);
            if (add) removing.Remove(cell); else removing[cell] = token;
            if (!add && cell.kind == WaveCellKind.Platform && Style(cell).generated) Collision(cell,false);
            if (add && keep) persistent.Add(cell); else persistent.Remove(cell);
            if (add && expiresAt > 0) expiry[cell] = (expiresAt, token);
            if (add)
            {
                // 새 칸이 놓일 자리(같은 위치·그룹)에 이미 켜져 있는 다른 종류·외형의 칸을 끈다.
                // 켜진 칸 전체를 훑지 않고, 있을 수 있는 종류·외형 조합을 하나씩 찾아본다.
                for (int kind = 0; kind < 2; kind++)
                    for (int style = 0; style <= maxStyle; style++)
                    {
                        if ((WaveCellKind)kind == cell.kind && style == cell.style) continue;
                        var old = new WaveMapCell { position = cell.position, group = cell.group, kind = (WaveCellKind)kind, style = style };
                        if (!active.Contains(old)) continue;
                        if (initial.Contains(old) && (initial.Contains(cell) || Style(cell).generated && old.kind == cell.kind)) continue;
                        if (panels.ContainsKey(old)) SettlePanel(old);
                        pool[old].gameObject.SetActive(false); Collision(old, false); active.Remove(old); expiry.Remove(old); persistent.Remove(old); generations[old] = generations.TryGetValue(old, out var prior) ? prior + 1 : 1;
                        unsettled.Remove(old);
                    }
                active.Add(cell);
                pool[cell].gameObject.SetActive(true); pool[cell].SetAmount(visible.Contains(cell) ? 1 : 0);
                if (cell.kind == WaveCellKind.OneWayPlatform) Collision(cell,true);
            }
        }
        // 일반 지형 칸은 기계팔 패널로 움직인다. 자세는 LateUpdate가 정하고, 여기서는 묶음 전체의 시간만 기다린다.
        // 같은 자리에 다른 지형이 이미 보이는 칸(그룹 지형과 일반 지형이 겹쳐 바뀌는 칸)은 모양이 그대로이므로 연출 없이 바꾼다.
        var panelCells = cells.Where(c => IsPanel(c) && (!add || !visible.Contains(c))).ToArray();
        var occupied = Occupancy(panelCells);
        bool Covered(WaveMapCell c) => occupied.TryGetValue(Vector2Int.FloorToInt(RoomPoint(c)), out int count) && count > 1;
        var moving = panelCells.Where(c => !Covered(c)).ToArray();
        var movingSet = new HashSet<WaveMapCell>(moving);
        var instant = new HashSet<WaveMapCell>(panelCells.Where(Covered));
        // 길이를 정하지 않은 묶음(Wave 사이 전환)은 화면 안에서 실제로 움직이는 패널 범위에 맞춘다. 움직일 것이 없으면 바로 끝난다.
        // 길이를 정한 묶음(시간별 생성·소멸)은 그 시간 안에서 패널 한 장을 조금 짧게 잡아, 묶음 중심에서 가까운 칸부터 차례로 움직이게 한다.
        float single = WaveTerrainPanelMotion.SingleDuration;
        bool pivotOrigins = false;
        if (duration < 0)
        {
            duration = moving.Length > 0 ? PanelDuration(moving) : 0f;
            if (cells.Any(c => !IsPanel(c) && (!add || !visible.Contains(c)))) duration = Mathf.Max(duration, .55f);
            // 화면 안에서 움직일 패널이 하나도 없으면 패널로 예약하지 않고 바로 바꾼다(예약했다가 곧바로 풀면 덩어리 외곽선을 두 번 다시 만든다).
            if (duration <= 0f && moving.Length > 0)
            {
                foreach (var cell in moving) instant.Add(cell);
                moving = Array.Empty<WaveMapCell>(); movingSet.Clear();
            }
        }
        else
        {
            single = Mathf.Min(single, duration * PanelShare);
            pivotOrigins = true;
        }
        if (add) foreach (var cell in instant) pool[cell].SetAmount(1);
        float panelEnd = panelClock + duration;
        SchedulePanels(moving, add, duration, single, pivotOrigins, driveSound);
        RefreshOutlines();
        // OneWay는 등장 시작부터, 생성 발판은 빼기 시작부터 충돌이 바뀐다. 연결 공간은 이 프레임의 LateUpdate가 한 번만 다시 구한다.
        if (cells.Any(c=>c.kind==WaveCellKind.OneWayPlatform&&add || c.kind==WaveCellKind.Platform&&!add&&Style(c).generated)) { ProcessCollision(); roomSpaceDirty = true; }
        for (float time = 0; time < duration; time += Time.fixedDeltaTime)
        {
            float t = time / duration;
            foreach (var cell in cells) if (generations[cell] == tokens[cell] && (!add || !visible.Contains(cell)) && !movingSet.Contains(cell) && !instant.Contains(cell))
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
        // 긴 프레임 때문에 패널 시계가 뒤처졌으면 마지막 패널이 끝날 때까지 기다린다.
        if (moving.Length > 0) while (panelClock < panelEnd) yield return null;
        foreach (var cell in cells)
        {
            if (generations[cell] != tokens[cell]) continue;
            if (movingSet.Contains(cell)) { panels.Remove(cell); pool[cell].HidePanelOutline(); }
            pool[cell].SetAmount(1); pool[cell].transform.localPosition = CellCenter(cell); pool[cell].gameObject.SetActive(add); Collision(cell, add);
            unsettled.Remove(cell);
            if (!add) removing.Remove(cell);
            if (!add) active.Remove(cell);
        }
        if (add) AddSeams(moving);
        solidsDirty = true;
        RebuildTerrain();
        // 여러 묶음이 같은 프레임에 끝나도 연결 공간은 그 프레임의 LateUpdate가 한 번만 다시 구한다.
        roomSpaceDirty = true;
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
        // 묶음 등장음을 이미 냈으므로 패널 구동음은 겹치지 않는다(칸마다의 맞물림 소리는 난다).
        yield return Animate(batch.cells, true, batch.duration, batch.lifetime > 0 ? Time.time + batch.lifetime : 0, batch.keepAfterWave, false);
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
        StopAllCoroutines(); ClearPanels(); ClearCustom();
        foreach (var row in removing.ToArray())
            if (generations.TryGetValue(row.Key, out var token) && token == row.Value)
            { active.Remove(row.Key); pool[row.Key].gameObject.SetActive(false); Collision(row.Key, false); solidsDirty = true; }
        removing.Clear();
        // 연출이 끝나지 않았던 칸만 제자리 모습·충돌로 되돌린다. 나머지 켜진 칸은 이미 그 상태다.
        foreach (var cell in unsettled)
            if (active.Contains(cell) && pool.TryGetValue(cell, out var view)) { view.SetAmount(1); view.transform.localPosition=CellCenter(cell); Collision(cell, true); }
        unsettled.Clear();
        Rebuild();
    }
    private void FixedUpdate()
    {
        if (expiry.Count == 0) return;
        // 같은 틱에 수명이 끝난 칸은 한 묶음으로 없앤다(패널 연출의 구동음·외곽선 갱신을 칸마다 반복하지 않는다).
        List<WaveMapCell> expired = null;
        foreach (var row in expiry.Where(e => e.Value.time <= Time.time).ToArray())
        {
            expiry.Remove(row.Key);
            if (active.Contains(row.Key) && generations.TryGetValue(row.Key, out var token) && token == row.Value.token)
                (expired ??= new List<WaveMapCell>()).Add(row.Key);
        }
        if (expired != null) StartCoroutine(Animate(expired.ToArray(), false, .55f));
    }
    private int GroupOf(Transform anchor) => anchor != null ? Array.IndexOf(groupAnchors, anchor) + 1 : 0;
    // 이 Transform이 셀 그룹(코스가 옮기는 지형 조각)이고 패널로 옮길 칸이 있는지.
    public bool CanRelocate(Transform anchor)
    {
        int group = GroupOf(anchor);
        return group > 0 && active.Any(c => c.group == group && IsPanel(c));
    }
    // 이 조각의 칸에 남아 있는 패널 연출을 제자리 모습으로 마친다. 조각 이동이 도중에 끊긴 뒤(코스가 열리다 닫힐 때) 부른다.
    public void SettleGroup(Transform anchor)
    {
        int group = GroupOf(anchor);
        if (group <= 0 || panels.Count == 0) return;
        bool settled = false;
        foreach (var cell in panels.Keys.Where(c => c.group == group).ToArray()) { SettlePanel(cell); settled = true; }
        if (settled) RefreshOutlines();
    }
    // 코스가 지형 조각(그룹 Transform)을 옮길 때 통째로 미끄러뜨리지 않고, 조각의 칸을 패널로 떼어 낸 뒤 옮긴 자리에 다시 끼운다.
    // snap은 조각을 목표 위치에 놓는 호출부의 동작이다(Kinematic 지형 이동). 화면 밖이거나 다른 지형에 묻히는 쪽은 연출 없이 넘어간다.
    public IEnumerator RelocateGroup(Transform anchor, Vector3 targetLocal, Action snap, float duration)
    {
        int group = GroupOf(anchor);
        var cells = group > 0 ? active.Where(c => c.group == group && IsPanel(c)).ToArray() : Array.Empty<WaveMapCell>();
        if (cells.Length == 0) { snap(); yield break; }
        // 이전 이동이 도중에 끊겼으면 제자리 모습에서 다시 시작한다.
        foreach (var cell in cells) if (panels.ContainsKey(cell)) SettlePanel(cell);
        Vector3 worldShift = anchor.parent != null ? anchor.parent.TransformVector(targetLocal - anchor.localPosition) : targetLocal - anchor.localPosition;
        Vector2 shift = transform.InverseTransformVector(worldShift);
        // 조각이 빠진 뒤 드러날 공간까지 배경·통창을 미리 넓힌다.
        ExpandBackdropFor(Solids(active, group, shift), WaveRoomState.PlayerStartAt(data, waves.currentWaveIndex));
        var others = Solids(active.Where(c => c.group != group));
        Rect view = VisibleArea(3f);
        bool Shown(WaveMapCell cell, Vector2 offset)
        {
            Vector2 point = RoomPoint(cell) + offset;
            return view.Contains(point) && !others.Contains(Vector2Int.FloorToInt(point));
        }
        var leaving = cells.Where(c => Shown(c, Vector2.zero)).ToArray();
        var arriving = cells.Where(c => Shown(c, shift)).ToArray();
        float part = leaving.Length > 0 && arriving.Length > 0 ? duration * .5f : duration;
        if (leaving.Length > 0)
        {
            float length = Mathf.Min(part, PanelDuration(leaving));
            SchedulePanels(leaving, false, length, WaveTerrainPanelMotion.SingleDuration);
            RefreshOutlines();
            yield return WaitPanels(length);
        }
        foreach (var cell in cells) if (panels.ContainsKey(cell)) SettlePanel(cell);
        snap();
        if (arriving.Length > 0)
        {
            float length = Mathf.Min(part, PanelDuration(arriving));
            SchedulePanels(arriving, true, length, WaveTerrainPanelMotion.SingleDuration);
            RefreshOutlines();
            yield return WaitPanels(length);
            foreach (var cell in arriving) if (panels.ContainsKey(cell)) SettlePanel(cell);
            AddSeams(arriving);
        }
        // 조각을 옮겨도 충돌 Tile은 그대로라 외곽선만 다시 맞춘다. 연결 공간은 코스의 모든 조각이 멈춘 뒤 한 번만 다시 구한다(TrackGroupMotion).
        RefreshOutlines();
        roomSpaceDirty = true;
    }
    private void FaceCenter(EnemyController enemy)
    {
        var layout = WaveRoomState.LayoutAt(data, waves.currentWaveIndex);
        if (layout.faceCenter) enemy.FacingUpdate(enemy.transform.position.x < transform.TransformPoint(layout.bounds.center).x ? 1 : -1);
    }
    public IEnumerator VanishCourseCells(Vector2 direction, float interval)
    {
        if (interval <= 0)
        {
            // 한꺼번에 없앨 때는 묶음마다 따로 예약하지 않는다(묶음 수만큼 외곽선·충돌·연결 공간을 되풀이해 한 프레임이 길어진다).
            // 한 번에 예약해도 칸마다 자기 묶음의 중심과 거리 범위로 시차를 잡으므로 묶음마다 번져 나가는 모습은 같다(SchedulePanels).
            var all = temporary.SelectMany(b => b.cells).ToArray();
            persistent.ExceptWith(all);
            if (all.Length > 0) StartCoroutine(Animate(all, false, .55f));
        }
        else
            foreach (var batch in temporary.OrderByDescending(b => b.cells.Length > 0 ? Vector2.Dot(b.cells[0].position, direction) : 0).ToArray())
            {
                StartCoroutine(Animate(batch.cells, false, .55f)); persistent.ExceptWith(batch.cells);
                yield return new WaitForSeconds(interval);
            }
        yield return new WaitForSeconds(.55f); temporary.Clear();
    }
    public Bounds LocalBounds(Transform source)
    {
        int group = GroupOf(source);
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
