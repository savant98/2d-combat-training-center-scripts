using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Scene 배치 없이 WaveData의 생성 좌표·시각과 공통 지형 이벤트를 편집한다.
public sealed class WaveEditorWindow : EditorWindow
{
    [SerializeField] private WaveDataSO data;
    [SerializeField] private int waveIndex;
    [SerializeField] private int selected = -1;
    [SerializeField] private int eventIndex = -1;
    [SerializeField] private bool terrainSession;
    [SerializeField] private float zoom = 22;
    [SerializeField] private Vector2 pan;
    private Vector2 rightScroll, paletteScroll, timelineScroll;
    private int mode;
    private float time;
    private GameObject brush;
    [SerializeField] private int brushRotationSteps;
    private static readonly string[] rotationLabels = { "0°", "90°", "180°", "270°" };
    private readonly List<GameObject> palette = new();
    private string search = "";
    private bool showRoom;
    private GameObject pendingBackground;
    private readonly List<GameObject> backgrounds = new();
    private bool showRaw;
    private int terrainTarget;
    private int selectedBatch = -1;
    private WaveTerrainOperation timedOperation;
    private int creationStyle = 1;
    private float initialRemovalTime = 1;
    private bool showCustom;
    private bool showTerrainCustom;
    private int dragIndex = -1;
    // 환경 장치를 잡은 지점에서 장치 기준점까지의 거리. 2×2 장치를 가장자리로 잡고 조금 끌어도 제자리에 있게 한다.
    private Vector2 dragOffset;
    private Vector2Int? selectedCell;
    private Vector2Int? lastPaint;
    private bool gridGesture;
    private int gridControl;
    private Rect canvas;
    private SerializedObject serialized;
    private GUIStyle markerStyle;
    private GUIStyle cardNameStyle;
    [SerializeField] private float paletteWidth = 238;
    [SerializeField] private float detailsWidth = 340;
    [SerializeField] private float timelineHeight = 140;
    [SerializeField] private float iconSize = 64;
    [SerializeField] private int paletteCategory;
    [SerializeField] private bool editPlayerStart;
    [SerializeField] private bool editClearPortal;
    // 배치 모드에서 다음에 놓을 장치의 세부 설정. 새 배치에만 복사하며 Prefab 기본값은 바꾸지 않는다.
    [SerializeField] private WaveDeviceSetting[] brushSettings = WaveDeviceSettings.None;
    // 0 이상이면 그 번호의 복사본을 붙이는 중이다.
    [SerializeField] private int copyBrush = -1;
    [SerializeField] private float objectPaneHeight = 330;
    private Vector2 objectScroll;
    // 선택 / 이동 모드에서 빈 곳을 드래그해 고른 칸 범위(복사용).
    private Vector2Int? areaStart, areaEnd;
    private bool areaDragging;
    private int lastDirty = -1;
    private bool wasLoadingPreviews;
    private GUIStyle objectPaneStyle, objectHeaderStyle;
    private Texture2D objectPaneTexture, objectHeaderTexture;
    // 우클릭 복사의 확인과 보관 칸 선택. 검증 도구가 바꿔 끼운다. 창을 띄울 수 없는 batchmode에서는 묻지 않고 취소한다.
    internal static Func<string, bool> ConfirmCopy = message => !Application.isBatchMode && EditorUtility.DisplayDialog("셀 복사", message, "복사", "취소");
    internal static Action<WaveCopyClip, Action<int>> ChooseCopySlot = (clip, choose) => { if (!Application.isBatchMode) WaveCopySlotWindow.Open(clip, choose); };
    private Rect leftSplitter, rightSplitter;
    private readonly WaveGridGeometry geometry = new();
    private Wave Current => data != null && data.waves != null && waveIndex >= 0 && waveIndex < data.waves.Length ? data.waves[waveIndex] : null;
    public WaveDataSO Data => data;

    [MenuItem("Tools/Training/Wave 격자 편집기")]
    public static void Open() => GetWindow<WaveEditorWindow>("Wave 격자 편집기");
    public void Select(WaveDataSO value)
    {
        pendingBackground = value != null ? value.backgroundPrefab : null;
        areaStart = areaEnd = null; copyBrush = -1;
        data = value; waveIndex = 0; selected = -1; selectedBatch = -1; selectedCell = null; time = 0; eventIndex = -1; timedOperation = WaveTerrainOperation.Add; creationStyle = 1; ResetGesture(); serialized = null;
        if (value != null) pan = new Vector2(-value.room.bounds.center.x, value.room.bounds.center.y) * zoom;
        Repaint();
    }
    private void OnEnable()
    {
        minSize = new Vector2(780, 500);
        maxSize = new Vector2(10000, 10000);
        Undo.undoRedoPerformed += Refresh;
        ReloadPalette();
    }
    // 예전에는 입력이 없어도 초당 10번 창 전체를 다시 그렸다. 입력 때는 Unity가 다시 그리므로,
    // 다른 곳(Inspector·Undo)에서 데이터가 바뀌었거나 Enemy 미리보기 이미지를 불러오는 중일 때만 다시 그린다.
    private void OnInspectorUpdate()
    {
        int dirty = data != null ? EditorUtility.GetDirtyCount(data) : -1;
        bool loading = AssetPreview.IsLoadingAssetPreviews();
        // 불러오기가 끝난 다음 틱에도 한 번 더 그려, 마지막에 도착한 미리보기가 화면에 남지 않는 일을 막는다.
        if (dirty == lastDirty && !loading && !wasLoadingPreviews) return;
        lastDirty = dirty;
        wasLoadingPreviews = loading;
        Repaint();
    }
    private void OnProjectChange() { ReloadPalette(); geometry.Invalidate(); WavePrefabVisuals.Invalidate(); Repaint(); }
    private void OnDisable()
    {
        Undo.undoRedoPerformed -= Refresh; ResetGesture();
        if (objectPaneTexture != null) DestroyImmediate(objectPaneTexture);
        if (objectHeaderTexture != null) DestroyImmediate(objectHeaderTexture);
        objectPaneStyle = objectHeaderStyle = null;
    }
    private void OnLostFocus() => ResetGesture();
    private void Refresh() { serialized = null; geometry.Invalidate(); Repaint(); }
    private void ReloadPalette()
    {
        palette.Clear();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (WavePrefabVisuals.IsPaletteItem(prefab)) palette.Add(prefab);
        }
        palette.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        backgrounds.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Environment" }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab.name.Contains("Backdrop") && WaveRoomBackground.IsBackground(prefab)) backgrounds.Add(prefab);
        }
    }
    private void OnGUI()
    {
        if (markerStyle == null) markerStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white }, clipping = TextClipping.Clip };
        if (cardNameStyle == null) cardNameStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, wordWrap = true, clipping = TextClipping.Clip };
        paletteWidth = Mathf.Clamp(paletteWidth, 150, Mathf.Min(480, position.width * .35f));
        detailsWidth = Mathf.Clamp(detailsWidth, 220, Mathf.Max(220, position.width - paletteWidth - 220 - 12));
        Toolbar();
        if (Current == null) { EditorGUILayout.HelpBox("WaveData를 선택하거나 새로 만드세요. 기존 데이터는 좌표와 생성 순서를 그대로 읽습니다.", MessageType.Info); return; }
        RotationShortcut();
        using (new EditorGUILayout.HorizontalScope())
        {
            Palette();
            leftSplitter = Splitter(true);
            using (new EditorGUILayout.VerticalScope())
            {
                int nextMode = GUILayout.Toolbar(mode, new[] { "선택 / 이동", "배치", "지우기" });
                if (nextMode != mode) { mode = nextMode; ResetGesture(); }
                float middleWidth = position.width - paletteWidth - detailsWidth - 12;
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    terrainSession = GUILayout.Toggle(terrainSession, new GUIContent("지형·발판 편집", "Wave 시작 지형 또는 시간별 지형 편집을 선택합니다."), EditorStyles.toolbarButton);
                    if (middleWidth >= 330) PointEditPicker();
                    if (GUILayout.Button("전체 보기", EditorStyles.toolbarButton)) Fit();
                }
                if (middleWidth < 330) PointEditPicker();
                if (terrainSession)
                {
                    int target = terrainTarget == 1 ? 0 : 1;
                    int nextTarget = GUILayout.SelectionGrid(target,new[] {
                        new GUIContent("Wave 시작 지형","처음부터 있을 바닥·벽·발판을 칠합니다."),
                        new GUIContent("시간별 지형 편집","기존 지형과 새 발판의 생성·소멸·수명·Wave 종료 후 유지를 함께 편집합니다.") },middleWidth<340?1:2,EditorStyles.miniButton);
                    if(nextTarget!=target){terrainTarget=nextTarget==0?1:2;selectedBatch=-1;eventIndex=-1;selectedCell=null;ResetGesture();}
                    EditorGUILayout.HelpBox(terrainTarget==1?"처음부터 있을 전체 맵입니다. Wave 전환 때 달라진 칸만 기계팔 패널 연출로 끼우고 뺍니다(기본 지형·생성 발판·OneWay 모두).":$"{time:G9}초 예약 · 칸을 선택해 생성/소멸 시각과 수명을 설정하세요. 지우기는 예약 취소입니다. 생성·소멸은 묶음 중심에서 가까운 칸부터 기계팔 패널로 끼우고 뺍니다.",MessageType.None);
                }
                canvas = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                DrawGrid();
                GridInput();
                ResizeTimeline();
                Timeline();
            }
            rightSplitter = Splitter(false);
            Details();
        }
    }
    private Vector2 DefaultPortalPoint()
    {
        var template = data.roomTemplate != null ? data.roomTemplate : AssetDatabase.LoadAssetAtPath<TrainingRoomContent>("Assets/Prefabs/Training/TrainingRoomBase.prefab");
        return template != null && template.clearPortal != null
            ? (Vector2)template.transform.InverseTransformPoint(template.clearPortal.transform.position)
            : new Vector2(data.room.bounds.xMax - 3, data.room.bounds.yMin + 1.5f);
    }
    private void PointEditPicker()
    {
        int current = editClearPortal ? 2 : editPlayerStart ? 1 : 0;
        int next = EditorGUILayout.Popup(current, new[] { "오브젝트 / 지형", "Wave 시작점 이동", "완료 포탈 이동" }, GUILayout.Width(132));
        if (next != current) SetPointEditTarget(next);
    }
    private void SetPointEditTarget(int target)
    {
        editPlayerStart = target == 1;
        editClearPortal = target == 2;
        if (target != 0) mode = 1;
        ResetGesture();
    }
    private void Toolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            var next = (WaveDataSO)EditorGUILayout.ObjectField(data, typeof(WaveDataSO), false, GUILayout.MinWidth(180));
            if (next != data) Select(next);
            if (GUILayout.Button("새 WaveData", EditorStyles.toolbarButton))
            {
                string path = EditorUtility.SaveFilePanelInProject("새 WaveData", "NewWave", "asset", "저장 위치");
                if (!string.IsNullOrEmpty(path)) Select(WaveAuthoring.Create(path, AssetDatabase.LoadAssetAtPath<TrainingRoomContent>("Assets/Prefabs/Training/TrainingRoomBase.prefab")));
            }
            if (data == null) return;
            int nextWave = EditorGUILayout.Popup(waveIndex, Enumerable.Range(0, data.waves?.Length ?? 0).Select(i => $"Wave {i + 1}").ToArray(), GUILayout.Width(100));
            if (nextWave != waveIndex) { waveIndex = nextWave; selected = -1; selectedBatch = -1; eventIndex = -1; selectedCell = null; areaStart = areaEnd = null; ResetGesture(); }
            if (GUILayout.Button("+ Wave", EditorStyles.toolbarButton)) Mutate(() => { var list = (data.waves ?? Array.Empty<Wave>()).ToList(); list.Add(new Wave { spawnObjects = Array.Empty<WaveSpawnInfo>() }); data.waves = list.ToArray(); waveIndex = list.Count - 1; selected = -1; selectedBatch = -1; selectedCell = null; eventIndex = -1; areaStart = areaEnd = null; ResetGesture(); });
            if (GUILayout.Button("복제", EditorStyles.toolbarButton) && Current != null) Mutate(() => { var copy = JsonUtility.FromJson<Wave>(JsonUtility.ToJson(Current)); var list = data.waves.ToList(); list.Insert(waveIndex + 1, copy); data.waves = list.ToArray(); waveIndex++; selected = -1; selectedBatch = -1; selectedCell = null; eventIndex = -1; ResetGesture(); });
            if (GUILayout.Button("삭제", EditorStyles.toolbarButton) && data.waves.Length > 1) Mutate(() => { var list = data.waves.ToList(); list.RemoveAt(waveIndex); data.waves = list.ToArray(); waveIndex = Mathf.Min(waveIndex, list.Count - 1); selected = -1; selectedBatch = -1; selectedCell = null; eventIndex = -1; areaStart = areaEnd = null; ResetGesture(); });
            if (GUILayout.Button("검사", EditorStyles.toolbarButton)) EditorUtility.DisplayDialog("WaveData 검사", string.Join("\n", WaveAuthoring.Validate(data).DefaultIfEmpty("데이터 검사 통과")), "확인");
            // 편집은 더티 표시만 하고 디스크에는 저장 버튼을 누를 때 이 WaveData만 쓴다.
            if (GUILayout.Button(new GUIContent("저장", "이 WaveData를 디스크에 저장합니다. 다른 에셋은 저장하지 않습니다."), EditorStyles.toolbarButton)) AssetDatabase.SaveAssetIfDirty(data);
        }
    }
    private void Palette()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(paletteWidth)))
        {
            GUILayout.Label("생성 Prefab 팔레트", EditorStyles.boldLabel);
            if (GUILayout.Button("공통 마감 규칙 보기")) Selection.activeObject = WaveRoomRulesSO.Default;
            if (GUILayout.Button("상세 사용법 열기")) Application.OpenURL(System.IO.Path.GetFullPath("docs/WAVE_EDITOR.md"));
            paletteCategory = GUILayout.Toolbar(paletteCategory, new[] { "Enemy", "Platform" });
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            iconSize = EditorGUILayout.Slider("미리보기 크기", iconSize, 40, 100);
            if (WavePrefabVisuals.IsEnvironmentTool(brush))
                EditorGUILayout.HelpBox("셀 중심에 배치 · R: 반시계 90° 회전\n방향·세부 설정은 오른쪽 '배치 오브젝트 상세'에서 정합니다.", MessageType.None);
            var category = paletteCategory == 0 ? WavePrefabVisuals.Category.Enemy : WavePrefabVisuals.Category.Platform;
            var items = palette.Where(p => p != null && WavePrefabVisuals.Classify(p) == category && p.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            GUILayout.Label($"{items.Length}개 · 클릭 후 격자에 배치", EditorStyles.miniLabel);
            paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll);
            float cardWidth = iconSize + 12;
            int columns = Mathf.Max(1, Mathf.FloorToInt((paletteWidth - 20) / cardWidth));
            for (int row = 0; row * columns < items.Length; row++)
            {
                using (new EditorGUILayout.HorizontalScope())
                    for (int column = 0; column < columns && row * columns + column < items.Length; column++)
                    {
                        var item = items[row * columns + column];
                        var rect = GUILayoutUtility.GetRect(cardWidth, iconSize + 40, GUILayout.Width(cardWidth));
                        var background = GUI.backgroundColor;
                        if (brush == item) GUI.backgroundColor = new Color(.35f, .75f, 1);
                        if (GUI.Button(rect, new GUIContent("", item.name))) UseBrush(item);
                        GUI.backgroundColor = background;
                        WavePrefabVisuals.Draw(new Rect(rect.x + 6, rect.y + 4, iconSize, iconSize), item, false, item == brush && WavePrefabVisuals.IsEnvironmentTool(item) ? brushRotationSteps : 0, item == brush ? brushSettings : null);
                        var cellView = item.GetComponent<WaveTerrainCellView>();
                        GUI.Label(new Rect(rect.x + 3, rect.y + iconSize + 6, rect.width - 6, 32), new GUIContent(cellView != null && !cellView.ownVisual ? cellView.kind.ToString() : item.name, item.name), cardNameStyle);
                    }
            }
            EditorGUILayout.EndScrollView();
            CopySlots();
            EditorGUILayout.HelpBox("한 칸 = 1 Unit\n휠: 확대/축소 · 가운데 버튼: 이동\n경계 막대를 드래그해 영역 크기 조절\n녹색 셀: 실제 지형 · 파란 표식: 플레이어 시작", MessageType.None);
        }
    }
    private Rect Splitter(bool left)
    {
        var rect = GUILayoutUtility.GetRect(6, 6, GUILayout.ExpandHeight(true), GUILayout.Width(6));
        EditorGUI.DrawRect(rect, new Color(.12f, .15f, .19f));
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
        int id = GUIUtility.GetControlID(left ? 731 : 732, FocusType.Passive);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
        if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id)
        {
            if (left) paletteWidth += e.delta.x; else detailsWidth -= e.delta.x;
            e.Use(); Repaint();
        }
        if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
        return rect;
    }
    private void ResizeTimeline()
    {
        var rect = GUILayoutUtility.GetRect(40, 6, GUILayout.Height(6), GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(.12f, .15f, .19f));
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);
        int id = GUIUtility.GetControlID(733, FocusType.Passive);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
        if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id) { timelineHeight = Mathf.Clamp(timelineHeight - e.delta.y, 70, position.height * .45f); e.Use(); Repaint(); }
        if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
    }
    private Vector2 Screen(Vector2 point) => canvas.center + pan + new Vector2(point.x, -point.y) * zoom;
    private Vector2 World(Vector2 point) { var p = (point - canvas.center - pan) / zoom; return new Vector2(p.x, -p.y); }
    private WaveRoomLayout Layout => WaveRoomState.LayoutAt(data, waveIndex);
    private Rect LayoutBounds => Layout.bounds;
    private Vector2 Offset => Layout.spawnOffset;
    private void Fit()
    {
        Vector2 min = LayoutBounds.min - Vector2.one * 3, max = LayoutBounds.max + Vector2.one * 3;
        if (data.overrideClearPortalPosition)
        {
            min = Vector2.Min(min, data.clearPortalPosition - Vector2.one * 2);
            max = Vector2.Max(max, data.clearPortalPosition + Vector2.one * 2);
        }
        foreach (var spawn in Current.spawnObjects ?? Array.Empty<WaveSpawnInfo>())
        {
            var point = spawn.spawnPosition + Offset;
            min = Vector2.Min(min, point - Vector2.one);
            max = Vector2.Max(max, point + Vector2.one);
        }
        if (data.cellMap.enabled)
            foreach (var cell in geometry.Cells(data, waveIndex, time)) { min = Vector2.Min(min, cell); max = Vector2.Max(max, cell + Vector2Int.one); }
        var bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        zoom = Mathf.Clamp(Mathf.Min((canvas.width - 40) / bounds.width, (canvas.height - 40) / bounds.height), 3, 64);
        pan = new Vector2(-bounds.center.x, bounds.center.y) * zoom;
    }
    private static Color TimingColor(float value) => Color.HSVToRGB(Mathf.Repeat(Mathf.Round(value * 1000) * .6180339f, 1), .6f, .85f);
    private void DrawGrid()
    {
        EditorGUI.DrawRect(canvas, new Color(.075f, .09f, .12f));
        GUI.BeginClip(canvas);
        Handles.BeginGUI();
        Vector2 Local(Vector2 p) => Screen(p) - canvas.position;
        Vector2 low = World(canvas.min), high = World(canvas.max);
        Handles.color = new Color(.22f, .27f, .32f);
        int stride = zoom < 8 ? 5 : 1;
        for (int x = Mathf.FloorToInt(low.x / stride) * stride; x < high.x; x += stride) Handles.DrawLine(Local(new Vector2(x, low.y)), Local(new Vector2(x, high.y)));
        for (int y = Mathf.FloorToInt(high.y / stride) * stride; y < low.y; y += stride) Handles.DrawLine(Local(new Vector2(low.x, y)), Local(new Vector2(high.x, y)));
        Handles.EndGUI();
        // 지형 상태는 WaveData·Wave·편집 시각이 바뀔 때만 다시 계산한다.
        var styledCells = data.cellMap.enabled ? geometry.CachedStyledCells(data, waveIndex, time) : null;
        var assemblies = data.cellMap.enabled ? geometry.CachedAssemblies(data, waveIndex, time) : null;
        foreach (var cell in styledCells != null ? (IEnumerable<Vector2Int>)styledCells.Keys : geometry.CachedCells(data, waveIndex, time))
        {
            Vector2 p = Local(new Vector2(cell.x, cell.y + 1));
            var rect = new Rect(p + Vector2.one * .6f, Vector2.one * Mathf.Max(1, zoom - 1.2f));
            if (rect.Overlaps(new Rect(Vector2.zero, canvas.size)))
            {
                if (styledCells != null)
                {
                    var styled = styledCells[cell];
                    WavePrefabVisuals.DrawCell(new Rect(p,Vector2.one*zoom),styled,data.cellMap,ConnectionMask(styledCells,assemblies,styled,cell));
                }
                else EditorGUI.DrawRect(rect,new Color(.16f,.65f,.36f,.9f));
            }
        }
        if(selectedCell.HasValue)
        {
            var p=Local(selectedCell.Value+Vector2Int.up);var rect=new Rect(p,Vector2.one*zoom);
            Handles.BeginGUI();Handles.color=Color.white;
            Handles.DrawAAPolyLine(3,new Vector3(rect.xMin,rect.yMin),new Vector3(rect.xMax,rect.yMin),new Vector3(rect.xMax,rect.yMax),new Vector3(rect.xMin,rect.yMax),new Vector3(rect.xMin,rect.yMin));Handles.EndGUI();
        }
        foreach (var row in Current.cellBatches.Select((batch, index) => (batch, index)))
        {
            if (row.batch.time > time || row.batch.lifetime > 0 && time >= row.batch.time + row.batch.lifetime) continue;
            Handles.BeginGUI(); Handles.color = row.index == selectedBatch ? Color.white : TimingColor(row.batch.time);
            foreach (var cell in row.batch.cells)
            {
                Vector2 p = Local(cell.position + Vector2.up); var rect = new Rect(p, Vector2.one * zoom);
                if (!rect.Overlaps(new Rect(Vector2.zero, canvas.size))) continue;
                Handles.DrawAAPolyLine(2, new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin), new Vector3(rect.xMax, rect.yMax), new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMin, rect.yMin));
            }
            Handles.EndGUI();
        }
        foreach (var entry in Current.terrainEvents ?? Array.Empty<WaveTerrainEvent>())
        {
            if (entry.time > time || entry.custom || entry.operation==WaveTerrainOperation.Add && entry.lifetime>0 && time>=entry.time+entry.lifetime) continue;
            foreach (var cell in entry.cells)
            {
                Vector2 p = Local(new Vector2(cell.x, cell.y + 1));
                var rect = new Rect(p, Vector2.one * zoom);
                Handles.BeginGUI();
                Handles.color = entry.operation == WaveTerrainOperation.Add ? Color.cyan : new Color(1, .35f, .3f);
                Handles.DrawAAPolyLine(2, new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin), new Vector3(rect.xMax, rect.yMax), new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMin, rect.yMin));
                Handles.EndGUI();
            }
        }
        float[] times = WaveAuthoring.Times(Current);
        var view = new Rect(Vector2.zero, canvas.size);
        for (int i = 0; i < (Current.spawnObjects?.Length ?? 0); i++)
        {
            var spawn = Current.spawnObjects[i];
            var global = MarkerRect(i);
            var rect = new Rect(global.position - canvas.position, global.size);
            // 한 칸보다 큰 환경 도구는 배경을 놓인 칸에만 깔고, 그림과 작동 범위는 실제 크기로 덧그린다.
            var visualGlobal = VisualRect(i);
            var visual = new Rect(visualGlobal.position - canvas.position, visualGlobal.size);
            // 편집 시각에 아직 없거나 수명이 끝난 항목은 흐리게 그린다. 그 칸에는 다른 장치를 설치할 수 있다.
            bool alive = Alive(i, times);
            var previousColor = GUI.color;
            if (!alive) GUI.color = new Color(1, 1, 1, .28f);
            if (alive || i == selected) WavePrefabVisuals.DrawRange(spawn.prefab, spawn.rotationSteps, Local(spawn.spawnPosition + Offset), zoom, spawn.deviceSettings);
            if (rect.Overlaps(view) || visual.Overlaps(view))
            {
                var frame = new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6);
                EditorGUI.DrawRect(frame, i == selected ? Color.white : TimingColor(times[i]) * new Color(1, 1, 1, alive ? 1 : .35f));
                EditorGUI.DrawRect(rect, new Color(.09f, .11f, .15f, alive ? .96f : .4f));
                WavePrefabVisuals.Draw(visual, spawn.prefab, true, spawn.rotationSteps, spawn.deviceSettings);
                GUI.Label(new Rect(rect.x - 30, rect.yMax + 2, rect.width + 60, 18), spawn.prefab != null ? spawn.prefab.name : "참조 없음", markerStyle);
                if (spawn.isBossEnemy) GUI.Label(new Rect(rect.xMax - 15, rect.y, 15, 18), "B", markerStyle);
            }
            GUI.color = previousColor;
        }
        if (mode == 0 && AreaRect() is RectInt area)
        {
            var corner = Local(new Vector2(area.xMin, area.yMax));
            var box = new Rect(corner, new Vector2(area.width, area.height) * zoom);
            EditorGUI.DrawRect(box, new Color(.3f, .85f, 1f, .12f));
            Handles.BeginGUI(); Handles.color = new Color(.4f, .9f, 1f);
            Handles.DrawAAPolyLine(2, new Vector3(box.xMin, box.yMin), new Vector3(box.xMax, box.yMin), new Vector3(box.xMax, box.yMax), new Vector3(box.xMin, box.yMax), new Vector3(box.xMin, box.yMin));
            Handles.EndGUI();
        }
        DrawSpawnPoint(Local(WaveRoomState.PlayerStartAt(data, waveIndex)), new Color(.2f, .7f, 1), $"Wave {waveIndex + 1} 시작점" + (Current.teleportAtStart ? " · Spawn" : ""));
        DrawSpawnPoint(Local(Offset), new Color(1, .72f, .2f), "Enemy / 발판 생성 기준");
        if (data.overrideClearPortalPosition)
            DrawSpawnPoint(Local(data.clearPortalPosition), new Color(.75f, .9f, .35f), "전체 Wave 완료 · Lobby 포탈");
        GUI.EndClip();
    }
    // 배경·테두리·이름표·선택 판정에 쓰는 영역.
    private Rect MarkerRect(int index)
    {
        var spawn = Current.spawnObjects[index];
        if (WavePrefabVisuals.UsesCompactMarker(spawn.prefab))
        {
            var cells = Occupied(spawn);
            return new Rect(Screen(new Vector2(cells.xMin, cells.yMax)), (Vector2)cells.size * zoom);
        }
        return VisualRect(index);
    }
    // Prefab의 보이는 부품 전체가 차지하는 실제 크기.
    private Rect VisualRect(int index)
    {
        var spawn = Current.spawnObjects[index];
        var bounds = WavePrefabVisuals.RotatedBounds(spawn.prefab, spawn.rotationSteps, spawn.deviceSettings);
        var min = Screen(spawn.spawnPosition + Offset + new Vector2(bounds.min.x, bounds.max.y));
        return new Rect(min, new Vector2(bounds.size.x, bounds.size.y) * zoom);
    }
    private void DrawSpawnPoint(Vector2 point, Color color, string label)
    {
        var rect = new Rect(point - Vector2.one * 10, Vector2.one * 20);
        EditorGUI.DrawRect(rect, color);
        GUI.Label(rect, "+", markerStyle);
        GUI.Label(new Rect(point.x - 88, point.y + 14, 176, 18), label, markerStyle);
    }
    private void UseBrush(GameObject value)
    {
        editPlayerStart = editClearPortal = false;
        copyBrush = -1;
        if (value != brush) brushSettings = WaveDeviceSettings.None;
        brush = value;if (WavePrefabVisuals.Classify(value) == WavePrefabVisuals.Category.Enemy || WavePrefabVisuals.IsEnvironmentTool(value)) { terrainSession = false;selected = -1;selectedBatch = -1;eventIndex = -1;selectedCell = null; }ResetGesture();
    }
    private static int NormalizeRotation(int value) => ((value % 4) + 4) % 4;
    private void RotationShortcut()
    {
        var e = Event.current;
        if (e.type != EventType.KeyDown || e.keyCode != KeyCode.R || e.control || e.command || e.alt || EditorGUIUtility.editingTextField || GUIUtility.hotControl != 0) return;
        if (mode == 0 && selected >= 0 && selected < (Current.spawnObjects?.Length ?? 0) && WavePrefabVisuals.IsEnvironmentTool(Current.spawnObjects[selected].prefab))
        {
            int index = selected, next = NormalizeRotation(Current.spawnObjects[index].rotationSteps + 1);
            if (RotationBlocked(index, next) is int blocking && blocking >= 0)
                ShowNotification(new GUIContent($"돌리면 #{blocking + 1} {Current.spawnObjects[blocking].prefab?.name}과 같은 칸을 차지해 돌릴 수 없습니다."));
            else Mutate(() => Current.spawnObjects[index].rotationSteps = next);
        }
        else if (mode == 1 && WavePrefabVisuals.IsEnvironmentTool(brush))
        {
            brushRotationSteps = NormalizeRotation(brushRotationSteps + 1);
            Repaint();
        }
        else return;
        e.Use();
    }
    private void ResetGesture()
    {
        if (gridControl != 0 && GUIUtility.hotControl == gridControl) GUIUtility.hotControl = 0;
        gridGesture = false; gridControl = 0; dragIndex = -1; lastPaint = null; areaDragging = false;
    }
    private void GridInput()
    {
        var e = Event.current;
        int id = GUIUtility.GetControlID(734, FocusType.Passive);
        if (e.rawType == EventType.MouseUp) { ResetGesture(); return; }
        bool inside = canvas.Contains(e.mousePosition);
        if (!inside && !gridGesture) return;
        if (inside && e.type == EventType.ScrollWheel)
        {
            Vector2 before = World(e.mousePosition);
            zoom = Mathf.Clamp(zoom * Mathf.Pow(1.12f, -e.delta.y), 3, 70);
            pan += e.mousePosition - Screen(before); e.Use(); Repaint(); return;
        }
        if (inside && e.type == EventType.MouseDrag && e.button == 2) { pan += e.delta; e.Use(); Repaint(); return; }
        if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
        {
            var prefab = DragAndDrop.objectReferences.OfType<GameObject>().FirstOrDefault(WavePrefabVisuals.IsPaletteItem);
            if (!inside || prefab == null) return;
            DragAndDrop.visualMode = mode == 1 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (mode == 1 && e.type == EventType.DragPerform) { UseBrush(prefab); DragAndDrop.AcceptDrag(); EditGrid(World(e.mousePosition), false); }
            e.Use(); return;
        }
        // 우클릭은 데이터를 바꾸지 않고, 그 셀(또는 드래그로 고른 범위)의 오브젝트를 복사본 칸에 담을지 묻는다.
        if (e.type == EventType.MouseDown && e.button == 1 && inside && GUIUtility.hotControl == 0)
        {
            ContextCopy(World(e.mousePosition)); e.Use(); return;
        }
        // 좌클릭으로 격자 안에서 시작한 제스처만 데이터를 바꾼다. 우클릭은 삭제하지 않는다.
        if (e.button != 0) return;
        if (e.type == EventType.MouseDown && inside && GUIUtility.hotControl == 0)
        {
            GUIUtility.hotControl = id; gridControl = id; gridGesture = true;
            EditGrid(World(e.mousePosition), false); e.Use(); Repaint();
        }
        else if (e.type == EventType.MouseDrag && inside && gridGesture && GUIUtility.hotControl == gridControl)
        {
            EditGrid(World(e.mousePosition), true); e.Use(); Repaint();
        }
    }
    private void EditGrid(Vector2 point, bool drag)
    {
        var cell = Vector2Int.FloorToInt(point);
        if (mode == 0)
        {
            if (!drag) SelectGrid(point);
            else if (dragIndex >= 0) MoveSpawn(dragIndex, point);
            else if (areaDragging) { areaEnd = cell; Repaint(); }
            return;
        }
        if (lastPaint == cell) return;
        lastPaint = cell;
        if (mode == 2) { EraseGrid(point); return; }
        if (mode != 1) return;
        if (editClearPortal) Mutate(() => { data.overrideClearPortalPosition = true; data.clearPortalPosition = Vector2Int.RoundToInt(point); });
        else if (editPlayerStart) Mutate(() => { Current.overridePlayerStart = true; Current.playerStart = Vector2Int.RoundToInt(point); });
        else if (copyBrush >= 0) { if (!drag) PasteCopy(cell); }
        else if (WavePrefabVisuals.IsEnvironmentTool(brush)) Place(point);
        else if (terrainSession) Paint(cell, false);
        else if (!drag || brush != null && brush.GetComponent<WaveTerrainCellView>() != null) Place(point);
    }
    private int BatchAt(Vector2Int cell)
    {
        bool Contains(int i) => i >= 0 && i < Current.cellBatches.Length && Current.cellBatches[i].cells.Any(c => WaveGridGeometry.PresentedCell(data,waveIndex,time,c) == cell);
        if (Contains(selectedBatch)) return selectedBatch;
        return Enumerable.Range(0,Current.cellBatches.Length)
            .Where(i => Contains(i) && Current.cellBatches[i].time <= time && (Current.cellBatches[i].lifetime <= 0 || time < Current.cellBatches[i].time + Current.cellBatches[i].lifetime))
            .OrderBy(i => Current.cellBatches[i].time).ThenBy(i => i).DefaultIfEmpty(-1).Last();
    }
    private int TerrainAt(Vector2Int cell)
    {
        bool Contains(int i) => i >= 0 && i < Current.terrainEvents.Length && Current.terrainEvents[i].cells.Contains(cell);
        if (terrainSession && terrainTarget != 1 && Contains(eventIndex)) return eventIndex;
        return Enumerable.Range(0,Current.terrainEvents.Length).Where(i => Contains(i) && Current.terrainEvents[i].time <= time && (Current.terrainEvents[i].operation!=WaveTerrainOperation.Add || Current.terrainEvents[i].lifetime<=0 || time<Current.terrainEvents[i].time+Current.terrainEvents[i].lifetime))
            .OrderBy(i => Current.terrainEvents[i].time).ThenBy(i => i).DefaultIfEmpty(-1).Last();
    }
    private void SelectGrid(Vector2 point)
    {
        int spawn = Hit(point);
        var cell = Vector2Int.FloorToInt(point);
        if (spawn >= 0)
        {
            SelectTimelineEntry(0,spawn); dragIndex = spawn; areaStart = areaEnd = cell; areaDragging = false;
            var picked = Current.spawnObjects[spawn];
            dragOffset = WavePrefabVisuals.IsEnvironmentTool(picked.prefab) ? picked.spawnPosition + Offset - point : Vector2.zero;
            return;
        }
        // 빈 곳에서 시작한 드래그는 복사할 칸 범위를 고른다.
        areaStart = areaEnd = cell; areaDragging = true;
        int entry = !terrainSession || terrainTarget != 1 ? TerrainAt(cell) : -1;
        int batch = BatchAt(cell);
        bool eventNewer = entry >= 0 && (batch < 0 || Current.terrainEvents[entry].time >= Current.cellBatches[batch].time);
        if (eventNewer && (!terrainSession || terrainTarget != 1)) SelectTimelineEntry(2,entry);
        else if (batch >= 0 && (!terrainSession || terrainTarget != 1)) SelectTimelineEntry(1,batch);
        else { selected = -1; selectedBatch = -1; eventIndex = -1; }
        selectedCell = cell; initialRemovalTime = Mathf.Max(1,time); dragIndex = -1;
    }
    private void EraseGrid(Vector2 point)
    {
        var cell = Vector2Int.FloorToInt(point);
        if (terrainSession && terrainTarget == 1) { PaintCell(cell,true);return; }
        int hit = Hit(point);
        if (!terrainSession && hit >= 0) { Mutate(() => { WaveAuthoring.SetTime(Current,hit,0,true); selected = -1; }); return; }
        int batch = BatchAt(cell),entry = TerrainAt(cell);
        bool explicitBatch = selectedBatch >= 0 && batch == selectedBatch;
        if (entry >= 0 && !explicitBatch && (batch < 0 || Current.terrainEvents[entry].time >= Current.cellBatches[batch].time)) CancelTerrainCell(cell);
        else if (batch >= 0) CancelBatchCell(cell);
        else if (!terrainSession && WaveCellState.Initial(data,waveIndex).Any(c => WaveGridGeometry.PresentedCell(data,waveIndex,time,c) == cell)) PaintCell(cell,true);
    }
    private void CancelBatchCell(Vector2Int position)
    {
        int index = BatchAt(position); if (index < 0) return;
        Mutate(() =>
        {
            var batch = Current.cellBatches[index];
            batch.cells = batch.cells.Where(c => WaveGridGeometry.PresentedCell(data,waveIndex,time,c) != position).ToArray();
            selectedBatch = index; selected = -1; selectedCell = position;
            if (batch.cells.Length == 0) { Current.cellBatches = Current.cellBatches.Where((b,i) => i != index).ToArray(); selectedBatch = -1; selectedCell = null; }
        });
    }
    private void CancelTerrainCell(Vector2Int position)
    {
        int index = TerrainAt(position); if (index < 0) return;
        Mutate(() =>
        {
            var entry = Current.terrainEvents[index];entry.cells = entry.cells.Where(c => c != position).ToArray();
            eventIndex = index; selected = -1; selectedBatch = -1; selectedCell = position;
            if (entry.cells.Length == 0 && !entry.custom) { Current.terrainEvents = Current.terrainEvents.Where((e,i) => i != index).ToArray(); eventIndex = -1; selectedCell = null; }
        });
    }
    private int Hit(Vector2 point)
    {
        var screen = Screen(point);
        var times = WaveAuthoring.Times(Current);
        int fallback = -1;
        for (int i = (Current.spawnObjects?.Length ?? 0) - 1; i >= 0; i--)
        {
            var marker = MarkerRect(i);
            var size = Vector2.Max(marker.size, Vector2.one * 12);
            if (!new Rect(marker.center - size * .5f, size).Contains(screen)) continue;
            if (Alive(i, times)) return i;
            if (fallback < 0) fallback = i;
        }
        return fallback;
    }
    private void Place(Vector2 point)
    {
        if (brush == null || !EditorUtility.IsPersistent(brush) || !WavePrefabVisuals.IsPaletteItem(brush)) return;
        if (data.cellMap.enabled && brush.GetComponent<WaveTerrainCellView>() != null) { if (terrainSession && terrainTarget == 1) PaintCell(Vector2Int.FloorToInt(point), false); else PaintTimedCell(Vector2Int.FloorToInt(point)); return; }
        bool device = WavePrefabVisuals.IsEnvironmentTool(brush);
        int steps = device ? NormalizeRotation(brushRotationSteps) : 0;
        var target = WavePrefabVisuals.Placement(brush, point, steps);
        float lifetime = 0;
        if (device && !FitLifetime(WavePrefabVisuals.Cells(target, WavePrefabVisuals.Footprint(brush, steps)), time, -1, out lifetime)) return;
        Mutate(() =>
        {
            var list = (Current.spawnObjects ?? Array.Empty<WaveSpawnInfo>()).ToList();
            list.Add(new WaveSpawnInfo { prefab = brush, spawnPosition = target - Offset, rotationSteps = steps,
                isRequiredToClear = brush.GetComponent<EnemyController>() != null, lifetime = lifetime,
                deviceSettings = device && brushSettings != null ? (WaveDeviceSetting[])brushSettings.Clone() : WaveDeviceSettings.None });
            Current.spawnObjects = list.ToArray();
            selected = WaveAuthoring.SetTime(Current, list.Count - 1, time);
            selectedBatch = -1; selectedCell = null;
        });
    }
    private void PaintCell(Vector2Int position, bool erase)
    {
        Mutate(() =>
        {
            if (!Current.overrideCellMap) Current.cells = WaveCellState.Initial(data, waveIndex).ToArray();
            Current.overrideCellMap = true;
            var cells = Current.cells.ToList();
            var targets = cells.Where(c => WaveGridGeometry.PresentedCell(data, waveIndex, time, c) == position).ToArray();
            var previous = targets.OrderBy(c => c.group).LastOrDefault();
            cells.RemoveAll(c => targets.Contains(c));
            if (erase) { }
            else
            {
                var cell = previous; cell.position = targets.Length > 0 ? previous.position : position;
                cell.kind = brush != null && brush.GetComponent<WaveTerrainCellView>() != null ? brush.GetComponent<WaveTerrainCellView>().kind : WaveCellKind.Platform;
                cell.style = BrushStyle(cell.kind == WaveCellKind.OneWayPlatform ? 1 : 0);
                cells.Add(cell);
            }
            Current.cells = cells.OrderBy(c => c.position.y).ThenBy(c => c.position.x).ToArray();
            selectedCell = position; selectedBatch = -1; selected = -1;
        });
    }
    // 전용 그림 셀이 이웃과 이어지는 방향. 실행 중(WaveCellTerrain.RefreshOutlines)과 같은 규칙이다:
    // 같은 종류·같은 외형·같은 코스 묶음(group)이고, 생성 발광 외형과 OneWay는 같은 생성 묶음일 때만 잇는다(OneWay는 좌우만).
    private int ConnectionMask(Dictionary<Vector2Int, WaveMapCell> cells, Dictionary<Vector2Int, int> assemblies, WaveMapCell cell, Vector2Int position)
    {
        var styles = data.cellMap.styles ?? Array.Empty<WaveCellStyle>();
        var own = cell.style >= 0 && cell.style < styles.Length && styles[cell.style] != null ? styles[cell.style].prefab : null;
        bool ownVisual = own != null && own.kind == cell.kind ? own.ownVisual : cell.kind == WaveCellKind.OneWayPlatform && data.cellMap.oneWayPrefab != null && data.cellMap.oneWayPrefab.ownVisual;
        if (!ownVisual) return 0;
        bool byAssembly = cell.kind == WaveCellKind.OneWayPlatform || cell.style >= 0 && cell.style < styles.Length && styles[cell.style] != null && styles[cell.style].generated;
        int Assembly(Vector2Int at) => assemblies != null && assemblies.TryGetValue(at, out var id) ? id : 0;
        int mine = Assembly(position);
        bool Same(Vector2Int other) => cells.TryGetValue(other, out var near) && near.kind == cell.kind && near.style == cell.style && near.group == cell.group
            && (!byAssembly || Assembly(other) == mine);
        if (cell.kind == WaveCellKind.OneWayPlatform)
            return (Same(position + Vector2Int.left) ? WaveTerrainCellView.Left : 0) | (Same(position + Vector2Int.right) ? WaveTerrainCellView.Right : 0);
        return WaveTerrainCellView.ConnectionMask(position, Same);
    }
    // 지금 브러시가 자기 그림을 쓰는 셀 Prefab(시범 발판 등)이면 그 Prefab 전용 스타일 번호를, 아니면 fallback을 쓴다.
    private int BrushStyle(int fallback) => ResolveBrushStyle(fallback, true);
    // 브러시가 칠할 스타일 번호. 자기 그림을 쓰는 셀 Prefab(새 생성 발판)이면 그 Prefab을 쓰는 스타일(보통 생성 발광 1번)을 찾고,
    // 없으면 create일 때만 끝에 더한다. 맵 기본 OneWay Prefab은 따로 스타일을 만들지 않는다.
    // 일반 브러시는 fallback을 쓰되, 그것이 전용 Prefab 스타일이면 생성 발광(1번)을 쓴다.
    private int ResolveBrushStyle(int fallback, bool create)
    {
        var view = brush != null ? brush.GetComponent<WaveTerrainCellView>() : null;
        var styles = data.cellMap.styles ?? Array.Empty<WaveCellStyle>();
        bool mapOneWay = view != null && view.kind == WaveCellKind.OneWayPlatform && data.cellMap.oneWayPrefab == view;
        if (view != null && view.ownVisual && !mapOneWay)
        {
            for (int i = 0; i < styles.Length; i++) if (styles[i] != null && styles[i].prefab == view) return i;
            return create ? EnsureStyle(view) : -1;
        }
        bool own = fallback >= 0 && fallback < styles.Length && styles[fallback] != null && styles[fallback].prefab != null;
        return own ? Mathf.Min(1, styles.Length - 1) : fallback;
    }
    // 브러시가 칠할 외형이 이 예약의 외형과 다르면 true. 서로 다른 외형을 같은 예약에 섞지 않는다.
    private bool StyleMismatch(int style) => ResolveBrushStyle(style, false) != style;
    // 이 Prefab을 쓰는 스타일 번호. 없으면 생성 발광 스타일을 본떠 끝에 더한다. 기존 번호의 의미는 바뀌지 않는다(Mutate 안에서 부른다).
    private int EnsureStyle(WaveTerrainCellView view)
    {
        var styles = data.cellMap.styles ?? Array.Empty<WaveCellStyle>();
        for (int i = 0; i < styles.Length; i++) if (styles[i] != null && styles[i].prefab == view) return i;
        // 본뜰 스타일: 생성 외형(1번 관례) → 아무 생성 스타일 → 마지막 스타일. FinalWave의 투명 스타일(끝 번호)을 본뜨지 않게 한다.
        var source = styles.FirstOrDefault(s => s != null && s.generated && s.prefab == null)
            ?? (styles.Length > 1 && styles[1] != null && styles[1].generated ? styles[1] : null)
            ?? styles.FirstOrDefault(s => s != null && s.generated && s.color.a > 0)
            ?? styles.LastOrDefault(s => s != null) ?? new WaveCellStyle { generated = true, layer = 6, sortingOrder = 1 };
        var style = new WaveCellStyle { tile = source.tile, material = source.material, color = source.color, transform = source.transform,
            sortingLayer = source.sortingLayer, sortingOrder = source.sortingOrder, layer = source.layer, generated = true, prefab = view };
        data.cellMap.styles = styles.Append(style).ToArray();
        return styles.Length;
    }
    private string[] StyleNames()
    {
        var styles = data.cellMap.styles ?? Array.Empty<WaveCellStyle>();
        var names = new string[Mathf.Max(2, styles.Length)];
        for (int i = 0; i < names.Length; i++)
            names[i] = i < styles.Length && styles[i] != null && styles[i].prefab != null ? $"{i}: {styles[i].prefab.name}" : i == 0 ? "기본 지형" : i == 1 ? "생성 발광" : $"스타일 {i}";
        return names;
    }
    private void PaintBatchCell(Vector2Int position, bool erase)
    {
        if (erase) { CancelBatchCell(position); return; }
        Mutate(() =>
        {
            if (selectedBatch < 0 || selectedBatch >= Current.cellBatches.Length || Mathf.Abs(Current.cellBatches[selectedBatch].time - time) > .0005f)
                selectedBatch = Array.FindIndex(Current.cellBatches, b => Mathf.Abs(b.time - time) <= .0005f);
            if (selectedBatch < 0)
            {
                if (erase) return;
                var list = Current.cellBatches.ToList(); list.Add(new WaveCellBatch { label = $"{time:0.###}초 생성 발판", time = time });
                Current.cellBatches = list.ToArray(); selectedBatch = list.Count - 1;
            }
            var batch = Current.cellBatches[selectedBatch]; var cells = batch.cells.ToList();
            cells.RemoveAll(c => c.position == position);
            if (!erase) cells.Add(new WaveMapCell { position = position, style = BrushStyle(creationStyle), kind = brush != null && brush.GetComponent<WaveTerrainCellView>() != null ? brush.GetComponent<WaveTerrainCellView>().kind : WaveCellKind.Platform });
            batch.cells = cells.ToArray(); selectedCell = position; selected = -1; eventIndex = -1;
        });
    }
    private void Paint(Vector2Int cell,bool erase)
    {
        if(!data.cellMap.enabled){PaintLegacy(cell,erase);return;}
        if(terrainTarget==1){PaintCell(cell,erase);return;}
        if(erase){EraseGrid((Vector2)cell+Vector2.one*.5f);return;}
        PaintTimedCell(cell);
    }
    private void PaintTimedCell(Vector2Int cell)
    {
        if(timedOperation==WaveTerrainOperation.Add)
        {
            if(eventIndex>=0 && eventIndex<Current.terrainEvents.Length)
            {
                var entry=Current.terrainEvents[eventIndex];
                if(!entry.custom && entry.operation==WaveTerrainOperation.Add && Mathf.Abs(entry.time-time)<=.0005f && (brush==null || brush.GetComponent<WaveTerrainCellView>()==null || entry.cellKind==brush.GetComponent<WaveTerrainCellView>().kind && !StyleMismatch(entry.cellStyle)))
                { Mutate(()=>{entry.cells=entry.cells.Union(new[]{cell}).ToArray();selectedCell=cell;});return; }
            }
            PaintBatchCell(cell,false);return;
        }
        int index=eventIndex;
        if(index<0 || index>=Current.terrainEvents.Length || Current.terrainEvents[index].custom || Current.terrainEvents[index].operation!=WaveTerrainOperation.Remove || Mathf.Abs(Current.terrainEvents[index].time-time)>.0005f)
            index=Array.FindIndex(Current.terrainEvents,e=>!e.custom && e.operation==WaveTerrainOperation.Remove && Mathf.Abs(e.time-time)<=.0005f);
        Mutate(()=>
        {
            if(index<0){var list=Current.terrainEvents.ToList();list.Add(new WaveTerrainEvent{label=$"{time:G9}초 소멸",time=time,operation=WaveTerrainOperation.Remove});Current.terrainEvents=list.ToArray();index=list.Count-1;}
            var entry=Current.terrainEvents[index];entry.cells=entry.cells.Union(new[]{cell}).ToArray();
            eventIndex=index;selectedBatch=-1;selected=-1;selectedCell=cell;
        });
    }
    private void AddTerrainReservation()
    {
        Mutate(()=>
        {
            if(timedOperation==WaveTerrainOperation.Add)
            { var list=Current.cellBatches.ToList();list.Add(new WaveCellBatch{label=$"{time:G9}초 생성",time=time});Current.cellBatches=list.ToArray();selectedBatch=list.Count-1;eventIndex=-1; }
            else
            { var list=Current.terrainEvents.ToList();list.Add(new WaveTerrainEvent{label=$"{time:G9}초 소멸",time=time,operation=WaveTerrainOperation.Remove});Current.terrainEvents=list.ToArray();eventIndex=list.Count-1;selectedBatch=-1; }
            terrainSession=true;terrainTarget=2;selected=-1;selectedCell=null;
        });
    }
    private void AddCustomTerrainReservation()
    {
        Mutate(()=>{var list=Current.terrainEvents.ToList();list.Add(new WaveTerrainEvent{label=$"{time:G9}초 커스텀 지형",time=time,custom=true});Current.terrainEvents=list.ToArray();eventIndex=list.Count-1;selectedBatch=-1;selected=-1;selectedCell=null;terrainSession=true;terrainTarget=2;});
    }
    private bool SetReservationEnd(float end)
    {
        float birth=selectedBatch>=0?Current.cellBatches[selectedBatch].time:eventIndex>=0?Current.terrainEvents[eventIndex].time:0;
        if(!float.IsFinite(end)||end<=birth)return false;
        return SetReservationLifetime(end-birth);
    }
    private bool SetReservationLifetime(float value)
    {
        if(!float.IsFinite(value)||value<0)return false;
        if(selectedBatch>=0 && selectedBatch<Current.cellBatches.Length){Mutate(()=>Current.cellBatches[selectedBatch].lifetime=value);return true;}
        if(eventIndex>=0 && eventIndex<Current.terrainEvents.Length && !Current.terrainEvents[eventIndex].custom && Current.terrainEvents[eventIndex].operation==WaveTerrainOperation.Add)
        {Mutate(()=>Current.terrainEvents[eventIndex].lifetime=value);return true;}
        return false;
    }
    private void ScheduleInitialRemoval(float end)
    {
        if(!selectedCell.HasValue || !float.IsFinite(end) || end<0)return;
        var initial=WaveCellState.Initial(data,waveIndex);
        var sources=initial.Where(c=>WaveGridGeometry.PresentedCell(data,waveIndex,time,c)==selectedCell.Value).ToArray();
        var positions=sources.Select(c=>WaveGridGeometry.PresentedCell(data,waveIndex,end,c)).Distinct().ToArray();
        if(positions.Length==0)return;
        Mutate(()=>
        {
            var list=Current.terrainEvents.ToList();list.Add(new WaveTerrainEvent{label=$"처음부터 존재 → {end:G9}초 소멸",time=end,operation=WaveTerrainOperation.Remove,cells=positions});Current.terrainEvents=list.ToArray();
            selectedBatch=-1;eventIndex=list.Count-1;time=end;timedOperation=WaveTerrainOperation.Remove;terrainSession=true;terrainTarget=2;selectedCell=positions[0];
        });
    }
    private void PaintLegacy(Vector2Int cell, bool erase)
    {
        if (data.cellMap.enabled && terrainTarget == 2) { PaintBatchCell(cell, erase); return; }
        if (data.cellMap.enabled && terrainTarget == 1) { PaintCell(cell, erase); return; }
        if (erase && terrainTarget == 0) { CancelTerrainCell(cell); return; }
        Mutate(() =>
        {
            if (terrainTarget == 1)
            {
                if (!Current.changeTerrain) Current.room = JsonUtility.FromJson<WaveRoomLayout>(JsonUtility.ToJson(Layout));
                Current.changeTerrain = true;
                if (data.customRoom && !Current.room.paintPlatforms && Current.room.platformCells.Length > 0
                    && data.roomTemplate != null && data.roomTemplate.GetComponent<TrainingRoomLayoutController>() != null)
                    Current.room.platformCells = WaveGridGeometry.FixedPlatformCells(data.roomTemplate).ToArray();
                Current.room.paintPlatforms = true;
                var initial = Current.room.platformCells.ToList();
                if (erase) initial.Remove(cell); else if (!initial.Contains(cell)) initial.Add(cell);
                Current.room.platformCells = initial.ToArray();
                var removed = Current.room.removedCells.ToList();
                if (erase && !removed.Contains(cell)) removed.Add(cell);
                if (!erase) removed.Remove(cell);
                Current.room.removedCells = removed.ToArray();
                return;
            }
            if (eventIndex < 0 || eventIndex >= Current.terrainEvents.Length || Mathf.Abs(Current.terrainEvents[eventIndex].time - time) > .0005f)
                eventIndex = Array.FindIndex(Current.terrainEvents, e => Mathf.Abs(e.time - time) <= .0005f);
            if (eventIndex < 0)
            {
                if (erase) return;
                var events = Current.terrainEvents.ToList(); events.Add(new WaveTerrainEvent { label = $"{time:0.###}초 지형 변경", time = time });
                Current.terrainEvents = events.ToArray(); eventIndex = events.Count - 1;
            }
            var entry = Current.terrainEvents[eventIndex];
            if (brush != null && brush.GetComponent<WaveTerrainCellView>() != null) entry.cellKind = brush.GetComponent<WaveTerrainCellView>().kind;
            var cells = entry.cells.ToList();
            if (erase) cells.Remove(cell); else if (!cells.Contains(cell)) cells.Add(cell);
            entry.cells = cells.ToArray(); selected = -1; selectedBatch = -1; selectedCell = null;
        });
    }
    private (int kind,int index,float time,string label)[] TimelineItems()
    {
        var items = new List<(int kind,int index,float time,string label)>();
        var times = WaveAuthoring.Times(Current);
        for(int i=0;i<times.Length;i++) items.Add((0,i,times[i],Current.spawnObjects[i].prefab != null?Current.spawnObjects[i].prefab.name:"참조 없음"));
        for(int i=0;i<Current.cellBatches.Length;i++) items.Add((1,i,Current.cellBatches[i].time,$"생성 {Current.cellBatches[i].label} · {Current.cellBatches[i].cells.Length}칸"));
        for(int i=0;i<Current.terrainEvents.Length;i++) items.Add((2,i,Current.terrainEvents[i].time,$"{(Current.terrainEvents[i].custom?"커스텀":Current.terrainEvents[i].operation == WaveTerrainOperation.Add?"생성":"소멸")} {Current.terrainEvents[i].label} · {Current.terrainEvents[i].cells.Length}칸"));
        return items.Where(i=>float.IsFinite(i.time) && i.time>=0).OrderBy(i=>i.time).ThenBy(i=>i.kind).ThenBy(i=>i.index).ToArray();
    }
    private void ChangeSpawnTime(int index,float value)
    {
        Mutate(()=>{selected=WaveAuthoring.SetTime(Current,index,value);time=WaveAuthoring.Times(Current)[selected];});
    }
    private void SetEditSecond(int second)
    {
        time = Mathf.Max(0,second);selected = -1;selectedBatch = -1;eventIndex = -1;selectedCell = null;ResetGesture();Repaint();
    }
    private void SelectTimelineEntry(int kind,int index)
    {
        selected = -1;selectedBatch = -1;eventIndex = -1;selectedCell = null;
        if(kind == 0) { selected = index;time = WaveAuthoring.Times(Current)[index];terrainSession = false; }
        if(kind == 1) { selectedBatch = index;time = Current.cellBatches[index].time;terrainSession = true;terrainTarget = 2;timedOperation = WaveTerrainOperation.Add;if(Current.cellBatches[index].cells.Length>0)creationStyle=Current.cellBatches[index].cells[0].style; }
        if(kind == 2) { eventIndex = index;time = Current.terrainEvents[index].time;terrainSession = true;terrainTarget = 2;timedOperation = Current.terrainEvents[index].operation;creationStyle=Current.terrainEvents[index].cellStyle; }
        Repaint();
    }
    private void Timeline()
    {
        var items = TimelineItems();
        float endTime = Mathf.Max(30,(Current.spawnObjects ?? Array.Empty<WaveSpawnInfo>()).Sum(s=>s.spawnDelay)+Current.startDelay);
        foreach(var batch in Current.cellBatches) endTime = Mathf.Max(endTime,batch.time+Mathf.Max(batch.duration,batch.lifetime));
        foreach(var entry in Current.terrainEvents) endTime = Mathf.Max(endTime,entry.time+Mathf.Max(entry.duration,entry.lifetime));
        int second = Mathf.FloorToInt(time);
        int nextSecond = EditorGUILayout.IntSlider(new GUIContent("편집 초 (1초 단위)","세부 시각은 아래 항목을 선택해 미리 보고 오른쪽 상세에서 수정합니다."),second,0,Mathf.CeilToInt(endTime));
        if(nextSecond != second) SetEditSecond(nextSecond);
        EditorGUILayout.LabelField($"선택 시각 {time:G9}초 · 세부 값 수정은 오른쪽 상세",EditorStyles.miniLabel);
        timelineScroll = EditorGUILayout.BeginScrollView(timelineScroll,GUILayout.Height(timelineHeight));
        foreach(var bucket in items.GroupBy(i=>Mathf.FloorToInt(i.time)))
        {
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if(GUILayout.Button($"{bucket.Key}초 구간 · {bucket.Count()}개",EditorStyles.miniButton)) SetEditSecond(bucket.Key);
                foreach(var moment in bucket.GroupBy(i=>i.time))
                {
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        var old = GUI.backgroundColor;GUI.backgroundColor = TimingColor(moment.Key);
                        if(GUILayout.Button($"{moment.Key:G9}초",GUILayout.Width(105))) { var first = moment.First();SelectTimelineEntry(first.kind,first.index); }
                        GUI.backgroundColor = old;
                        foreach(var item in moment)
                            if(GUILayout.Button(new GUIContent(item.label,$"{item.time:G9}초 항목 선택 · 값 수정은 오른쪽 상세"),EditorStyles.miniButton,GUILayout.MaxWidth(190))) SelectTimelineEntry(item.kind,item.index);
                    }
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }
    private void Details()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(detailsWidth)))
        {
            if (serialized == null || serialized.targetObject != data) serialized = new SerializedObject(data);
            serialized.Update();
            // 위: 배치 오브젝트 상세(색을 달리한 별도 칸). 아래: Wave 상세.
            ObjectPane();
            ResizeObjectPane();
            // ObjectPane은 바꾼 값을 ApplyModifiedProperties로 반영하거나 Mutate로 serialized를 새로 만든다. 다시 Update하지 않는다.
            if (serialized == null || serialized.targetObject != data) serialized = new SerializedObject(data);
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);
            var wave = serialized.FindProperty("waves").GetArrayElementAtIndex(waveIndex);
            GUILayout.Label($"Wave {waveIndex + 1} 상세", EditorStyles.boldLabel);
            if (data.cellMap.enabled)
            {
                EditorGUILayout.HelpBox("Platform / OneWayPlatform은 한 칸입니다. 팔레트에서 고르고 칠한 셀이 실제 맵이 됩니다. 환경 도구는 셀 중심에 배치하고 90°씩 회전합니다. 생성 항목 상세에서 시각·수명·Wave 종료 후 유지를 설정하세요. Enemy 이미지는 실제 월드 크기입니다.", MessageType.None);
                if (selectedCell.HasValue)
                {
                    EditorGUILayout.LabelField("선택한 셀", selectedCell.Value.ToString());
                    using (new EditorGUI.DisabledScope(mode != 2))
                        if (GUILayout.Button(new GUIContent("선택한 배치/예약 칸 취소", "지우기 모드에서만 편집 데이터를 삭제합니다. 실행 중 지형 소멸 예약과 다릅니다.")))
                        { serialized.ApplyModifiedProperties(); if (terrainSession && terrainTarget != 1 && eventIndex >= 0) CancelTerrainCell(selectedCell.Value); else if (selectedBatch >= 0) CancelBatchCell(selectedCell.Value); else PaintCell(selectedCell.Value,true); }
                    if (mode != 2) EditorGUILayout.HelpBox("배치/예약을 취소하려면 상단 지우기를 선택하세요.",MessageType.None);
                }
            }
            var startOverride = wave.FindPropertyRelative("overridePlayerStart");
            var startPoint = wave.FindPropertyRelative("playerStart");
            EditorGUI.BeginChangeCheck();
            Vector2 nextStart = EditorGUILayout.Vector2Field("Wave 시작 좌표", startOverride.boolValue ? startPoint.vector2Value : data.playerStart);
            if (EditorGUI.EndChangeCheck()) { startOverride.boolValue = true; startPoint.vector2Value = nextStart; }
            Property(wave, "teleportAtStart", "시작 시 텔레포트 · Spawn 연출");
            EditorGUILayout.HelpBox("지형 준비 중에는 Player를 구속 상자에 가둬 이동만 막습니다(공격·회복·드론 소환은 계속 쓸 수 있고 진행 중인 무장도 끊기지 않습니다). 체크하면 준비가 끝난 뒤 시작 좌표로 이동하고 기존 Spawn 연출을 재생한 다음 Wave를 시작합니다. 체크하지 않으면 현재 위치를 유지합니다.", MessageType.None);
            GUILayout.Space(6);
            GUILayout.Label("모든 Wave 완료 · Lobby 복귀", EditorStyles.boldLabel);
            var portalOverride = serialized.FindProperty("overrideClearPortalPosition");
            bool previousPortalOverride = portalOverride.boolValue;
            EditorGUILayout.PropertyField(portalOverride, new GUIContent("완료 포탈 위치 직접 지정"));
            if (!previousPortalOverride && portalOverride.boolValue) serialized.FindProperty("clearPortalPosition").vector2Value = DefaultPortalPoint();
            if (portalOverride.boolValue) EditorGUILayout.PropertyField(serialized.FindProperty("clearPortalPosition"), new GUIContent("Lobby 복귀 포탈 좌표"));
            EditorGUILayout.HelpBox("전체 Wave에서 공유하는 완료 포탈 위치입니다. 상단 완료 포탈 이동을 선택하면 격자에서도 옮길 수 있습니다. Lobby 도착 위치는 유지합니다.", MessageType.None);
            EditorGUILayout.LabelField("Enemy / 발판 생성 기준", $"({Offset.x:0.###}, {Offset.y:0.###})");
            Property(wave, "clearType", "클리어 조건");
            if (Current.clearType == WaveClearType.SurviveForTime) Property(wave, "surviveTime", "생존 시간 (초)");
            if (Current.clearType == WaveClearType.CustomTrigger) Property(wave, "customTriggerKey", "목표 도달 / 완료 키");
            Property(wave, "clearDelay", "클리어 후 대기 (초)");
            Property(wave, "startDelay", "첫 생성 대기 (초)");
            Property(wave, "custom", "커스텀 연출 사용");
            if (Current.custom) Property(wave, "customDescription", "커스텀 규칙 설명");
            showRoom = EditorGUILayout.Foldout(showRoom, "Room / 배경 / 초기 지형", true);
            if (showRoom)
            {
                EditorGUILayout.PropertyField(serialized.FindProperty("roomTemplate"), new GUIContent("Room 콘텐츠 (카메라·진행 포함)"));
                EditorGUILayout.PropertyField(serialized.FindProperty("customRoom"), new GUIContent("원본 커스텀 Room 보존"));
                if (data.customRoom)
                {
                    EditorGUILayout.PropertyField(serialized.FindProperty("adaptCameraToRoom"), new GUIContent("카메라 범위를 실내 크기에 맞춤"));
                    bool measuredRoom = data.roomTemplate != null && data.roomTemplate.GetComponentInChildren<TrainingRoomPresentation>(true) != null;
                    EditorGUILayout.HelpBox(measuredRoom
                        ? "이 Room은 실내 치수 연출(TrainingRoomPresentation)을 가진 커스텀 Room입니다. 위 설정을 켜면 저장된 실내 범위에 카메라를 맞춥니다(관리자 평가)."
                        : "이 커스텀 Room은 코스 카메라·마무리 연출을 유지하면서, 일반 Platform으로 둘러싸인 연결 공간에 카메라·배경·통창을 자동으로 맞춥니다. 위 설정은 실내 치수 연출을 가진 Room(관리자 평가)에서만 쓰입니다.", MessageType.None);
                }
                else EditorGUILayout.HelpBox("일반 Room은 일반 Platform으로 둘러싸인 연결 공간에 카메라·배경·통창을 자동으로 맞춥니다. OneWayPlatform은 경계를 막지 않습니다. 외부로 열렸거나 공간을 계산할 수 없으면 저장된 실내 범위를 사용합니다.", MessageType.None);
                int backgroundIndex = backgrounds.IndexOf(pendingBackground) + 1;
                int nextBackground = EditorGUILayout.Popup("배경 선택", backgroundIndex, new[] { "원본 배경 유지" }.Concat(backgrounds.Select(p => p.name)).ToArray());
                if (nextBackground != backgroundIndex) pendingBackground = nextBackground == 0 ? null : backgrounds[nextBackground - 1];
                pendingBackground = (GameObject)EditorGUILayout.ObjectField("배경 Prefab", pendingBackground, typeof(GameObject), false);
                if (GUILayout.Button("선택한 배경 적용")) ApplySelectedBackground();
                EditorGUILayout.PropertyField(serialized.FindProperty("training"), new GUIContent("훈련 기록 / 보상"));
                EditorGUILayout.PropertyField(serialized.FindProperty("playerStart"), new GUIContent("미지정 Wave의 기본 시작 좌표"));
                if (data.cellMap.enabled) RoomProperties(serialized.FindProperty("room"));
                else EditorGUILayout.PropertyField(serialized.FindProperty("room"), new GUIContent("기본 Room"), true);
                Property(wave, "changeTerrain", "Wave 시작 시 지형 변경");
                if (Current.changeTerrain)
                {
                    if (data.cellMap.enabled) RoomProperties(wave.FindPropertyRelative("room"));
                    else EditorGUILayout.PropertyField(wave.FindPropertyRelative("room"), new GUIContent("변경 Room"), true);
                }
                if (data.customRoom) EditorGUILayout.HelpBox("원본 카메라·코스 마무리는 모듈로 보존됩니다. 아래 공통 지형 이벤트는 함께 실행됩니다. 모듈 원본은 Prefab Mode에서 편집할 수 있습니다.", MessageType.Info);
            }
            if(data.cellMap.enabled && (selectedBatch>=0 || eventIndex>=0 || terrainSession && terrainTarget!=1)) TerrainDetails();
            showRaw = EditorGUILayout.Foldout(showRaw, "전체 생성 목록 / 원본 순서", true);
            if (showRaw) EditorGUILayout.PropertyField(serialized.FindProperty("waves").GetArrayElementAtIndex(waveIndex).FindPropertyRelative("spawnObjects"), true);
            serialized.ApplyModifiedProperties();
            if (Current.custom || data.customRoom)
            {
                showCustom = EditorGUILayout.Foldout(showCustom, "커스텀 모듈 상세 (Tool에서 편집)", true);
                if (showCustom && data.roomTemplate != null)
                {
                    foreach (var stage in data.roomTemplate.GetComponentsInChildren<TrainingCourseStage>(true))
                    {
                        GUILayout.Label(stage.CourseKey, EditorStyles.boldLabel);
                        var stageObject = new SerializedObject(stage);
                        stageObject.Update();
                        var property = stageObject.GetIterator();
                        if (property.NextVisible(true)) while (property.NextVisible(false)) EditorGUILayout.PropertyField(property, true);
                        if (stageObject.ApplyModifiedProperties()) PrefabUtility.SavePrefabAsset(data.roomTemplate.gameObject);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
    private void LifecycleFields(SerializedProperty value,float birth)
    {
        var lifetime=value.FindPropertyRelative("lifetime");
        bool enabled=lifetime.floatValue>0;
        bool next=EditorGUILayout.Toggle(new GUIContent("소멸 예약","끄면 시간 제한 없이 유지합니다. 켜면 수명 또는 소멸 시각을 설정합니다."),enabled);
        if(next!=enabled)lifetime.floatValue=next?1:0;
        if(next)
        {
            float old=lifetime.floatValue;
            float life=EditorGUILayout.FloatField("수명 (초)",old);
            if(float.IsFinite(life)&&life>0)lifetime.floatValue=life;
            else EditorGUILayout.HelpBox("수명은 0보다 커야 합니다. 시간 제한을 없애려면 소멸 예약을 끄세요.",MessageType.Warning);
            float end=birth+lifetime.floatValue;
            float after=EditorGUILayout.FloatField("소멸 시각 (초)",end);
            if(after!=end)
            {
                if(float.IsFinite(after)&&after>birth)lifetime.floatValue=after-birth;
                else EditorGUILayout.HelpBox("소멸 시각은 생성 시각보다 뒤여야 합니다.",MessageType.Warning);
            }
        }
        else EditorGUILayout.LabelField("소멸 시각","시간 제한 없음");
        Property(value,"keepAfterWave","Wave 종료 후 유지");
    }
    private void TerrainDetails()
    {
        GUILayout.Space(12);GUILayout.Label("시간별 지형 편집",EditorStyles.boldLabel);
        int operation=EditorGUILayout.Popup("새로 칠할 동작",(int)timedOperation,new[]{"생성","소멸"});
        if(operation!=(int)timedOperation){serialized.ApplyModifiedProperties();timedOperation=(WaveTerrainOperation)operation;selectedBatch=-1;eventIndex=-1;selectedCell=null;ResetGesture();}
        if(GUILayout.Button("+ 지형 예약")){serialized.ApplyModifiedProperties();AddTerrainReservation();serialized.Update();}
        showTerrainCustom=EditorGUILayout.Foldout(showTerrainCustom,"고급: 커스텀 지형 규칙",true);
        if(showTerrainCustom && GUILayout.Button("+ 커스텀 예약")){serialized.ApplyModifiedProperties();AddCustomTerrainReservation();serialized.Update();}
        var list=TimelineItems().Where(i=>i.kind!=0).ToArray();
        int active=Array.FindIndex(list,i=>i.kind==1?i.index==selectedBatch:i.index==eventIndex);
        int choice=EditorGUILayout.Popup("예약",active+1,new[]{"예약 선택"}.Concat(list.Select(i=>$"{i.time:G9}초 · {i.label}")).ToArray())-1;
        if(choice!=active){serialized.ApplyModifiedProperties();if(choice>=0)SelectTimelineEntry(list[choice].kind,list[choice].index);else{selectedBatch=-1;eventIndex=-1;}serialized.Update();}
        SerializedProperty value=null;bool remove=false,custom=false;float previousTime=0;
        var wave=serialized.FindProperty("waves").GetArrayElementAtIndex(waveIndex);
        if(selectedBatch>=0 && selectedBatch<Current.cellBatches.Length)
        {value=wave.FindPropertyRelative("cellBatches").GetArrayElementAtIndex(selectedBatch);previousTime=Current.cellBatches[selectedBatch].time;}
        else if(eventIndex>=0 && eventIndex<Current.terrainEvents.Length)
        {value=wave.FindPropertyRelative("terrainEvents").GetArrayElementAtIndex(eventIndex);previousTime=Current.terrainEvents[eventIndex].time;remove=Current.terrainEvents[eventIndex].operation==WaveTerrainOperation.Remove;custom=Current.terrainEvents[eventIndex].custom;}
        if(value!=null)
        {
            Property(value,"label","이름");
            if(custom)
            {
                Property(value,"time","실행 시각 (초)");Property(value,"customPrefab","커스텀 규칙 Prefab");
                EditorGUILayout.HelpBox("기존 커스텀 규칙으로 실행합니다. 생성·소멸은 해당 Prefab의 규칙을 따릅니다.",MessageType.None);
            }
            else if(remove)
            {
                EditorGUILayout.LabelField("생성 시각","처음부터 있거나 이전에 생성된 지형");
                Property(value,"time","소멸 시각 (초)");Property(value,"duration","소멸 애니메이션 (초)");
            }
            else
            {
                Property(value,"time","생성 시각 (초)");Property(value,"duration","등장 애니메이션 (초)");
                float birth=value.FindPropertyRelative("time").floatValue;LifecycleFields(value,birth);
                if(selectedBatch<0)Property(value,"cellKind","셀 종류 (이 예약 전체)");
                int style=selectedBatch>=0?(Current.cellBatches[selectedBatch].cells.Length>0?Current.cellBatches[selectedBatch].cells[0].style:creationStyle):value.FindPropertyRelative("cellStyle").intValue;
                var styleNames=StyleNames();
                int shown=Mathf.Clamp(style,0,styleNames.Length-1);
                int next=EditorGUILayout.Popup("외형",shown,styleNames);
                if(next!=shown)
                {
                    serialized.ApplyModifiedProperties();creationStyle=next;
                    if(selectedBatch>=0)Mutate(()=>Current.cellBatches[selectedBatch].cells=Current.cellBatches[selectedBatch].cells.Select(c=>{c.style=next;return c;}).ToArray());
                    else Mutate(()=>Current.terrainEvents[eventIndex].cellStyle=next);
                    serialized.Update();
                }
            }
            serialized.ApplyModifiedProperties();
            float nextTime=selectedBatch>=0?Current.cellBatches[selectedBatch].time:Current.terrainEvents[eventIndex].time;
            if(nextTime!=previousTime)time=Mathf.Max(0,nextTime);
            int count=selectedBatch>=0?Current.cellBatches[selectedBatch].cells.Length:Current.terrainEvents[eventIndex].cells.Length;
            EditorGUILayout.LabelField("예약 칸",$"{count}칸 · 이 예약의 모든 칸에 적용");
            using(new EditorGUI.DisabledScope(mode!=2))
                if(GUILayout.Button("선택한 지형 예약 취소"))
                {serialized.ApplyModifiedProperties();Mutate(()=>{if(selectedBatch>=0)Current.cellBatches=Current.cellBatches.Where((b,i)=>i!=selectedBatch).ToArray();else Current.terrainEvents=Current.terrainEvents.Where((e,i)=>i!=eventIndex).ToArray();selectedBatch=-1;eventIndex=-1;selectedCell=null;});}
        }
        else if(selectedCell.HasValue && WaveCellState.Initial(data,waveIndex).Any(c=>WaveGridGeometry.PresentedCell(data,waveIndex,time,c)==selectedCell.Value))
        {
            EditorGUILayout.LabelField("생성 시각","처음부터 존재");
            float end=EditorGUILayout.FloatField("소멸 시각 (초)",initialRemovalTime);
            if(float.IsFinite(end)&&end>=0)initialRemovalTime=end;
            if(GUILayout.Button("소멸 예약 추가")){serialized.ApplyModifiedProperties();ScheduleInitialRemoval(initialRemovalTime);}
        }
        else EditorGUILayout.HelpBox("생성할 칸은 배치로 칠하고 오른쪽에서 생성·소멸·수명을 정하세요. 기존 바닥은 선택 후 소멸 예약을 추가하세요. 지우기는 예약 자체를 취소합니다.",MessageType.None);
    }
    private void ApplySelectedBackground()
    {
        if (pendingBackground != null && !WaveRoomBackground.IsBackground(pendingBackground))
        { EditorUtility.DisplayDialog("배경 확인", "카메라·Collider·Room·WaveManager가 없는 배경 Prefab을 선택하세요.", "확인"); return; }
        serialized?.ApplyModifiedProperties(); Mutate(() => data.backgroundPrefab = pendingBackground);
        foreach (var terrain in UnityEngine.Object.FindObjectsByType<WaveCellTerrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (terrain.Data == data) terrain.ApplyBackground();
        if(EditorUtility.IsPersistent(data)) AssetDatabase.SaveAssetIfDirty(data);
    }
    private static void RoomProperties(SerializedProperty room)
    {
        Property(room, "bounds", "카메라 실내 범위");
        Property(room, "spawnOffset", "Enemy 생성 기준점");
        Property(room, "faceCenter", "Enemy 중앙 바라보기");
    }
    private static void Property(SerializedProperty parent, string field, string label) => EditorGUILayout.PropertyField(parent.FindPropertyRelative(field), new GUIContent(label), true);
    // ───────────────────────────── 배치 오브젝트 상세 (Wave 상세와 분리한 칸) ─────────────────────────────
    private void ObjectPane()
    {
        EnsurePaneStyles();
        using (new EditorGUILayout.VerticalScope(objectPaneStyle, GUILayout.Height(objectPaneHeight)))
        {
            GUILayout.Label("배치 오브젝트 상세", objectHeaderStyle);
            objectScroll = EditorGUILayout.BeginScrollView(objectScroll);
            bool hasSelection = selected >= 0 && selected < (Current.spawnObjects?.Length ?? 0);
            bool brushPane = mode == 1 && (copyBrush >= 0 || WavePrefabVisuals.IsEnvironmentTool(brush));
            if (brushPane)
            {
                if (copyBrush >= 0) CopyBrushDetails(); else BrushDetails();
                if (hasSelection) { GUILayout.Space(8); GUILayout.Label("방금 배치·선택한 항목", EditorStyles.miniBoldLabel); }
            }
            if (hasSelection) SelectedSpawnDetails();
            else if (!brushPane)
                EditorGUILayout.HelpBox("선택 / 이동 모드에서 배치한 오브젝트를 누르면 방향·세부 설정·생성 시각·수명을 여기서 바꿉니다.\n배치 모드에서 팔레트의 장치를 고르면 배치할 값을 미리 정합니다.\n빈 곳을 드래그해 칸 범위를 고르고 우클릭하면 그 안의 오브젝트를 복사합니다.", MessageType.None);
            EditorGUILayout.EndScrollView();
        }
    }
    private void SelectedSpawnDetails()
    {
        var wave = serialized.FindProperty("waves").GetArrayElementAtIndex(waveIndex);
        var info = Current.spawnObjects[selected];
        int previousRotation = info.rotationSteps;
        GUILayout.Label($"#{selected + 1} {(info.prefab != null ? info.prefab.name : "참조 없음")}", EditorStyles.boldLabel);
        var spawn = wave.FindPropertyRelative("spawnObjects").GetArrayElementAtIndex(selected);
        bool device = WavePrefabVisuals.IsEnvironmentTool(info.prefab);
        WaveDeviceSetting[] nextSettings = null;
        if (device)
        {
            var rotation = spawn.FindPropertyRelative("rotationSteps");
            rotation.intValue = EditorGUILayout.Popup("설치 방향 · R", NormalizeRotation(rotation.intValue), rotationLabels);
            var edited = DeviceSettingsFields(info.prefab, info.deviceSettings, out bool settingsChanged);
            if (settingsChanged) nextSettings = edited;
            EditorGUILayout.HelpBox("이 배치에만 저장합니다. Prefab 기본값과 팔레트 항목은 바뀌지 않습니다. 0°는 Prefab 원본 방향이며 장치 중심(한 칸은 셀 중심, 2×2는 격자 꼭짓점)을 기준으로 반시계 90°씩 회전합니다.", MessageType.None);
        }
        var previousPrefab = info.prefab; var previousPosition = info.spawnPosition; float previousLifetime = info.lifetime;
        foreach (var pair in new[] { ("prefab", "Prefab"), ("spawnPosition", "생성 좌표 (원본 보존)"), ("isRequiredToClear", "처치 필수 목표"), ("isBossEnemy", "보스 등장"), ("keepAfterWave", "Wave 종료 후 유지"), ("lifetime", "생성 후 유지 제한 (0=무제한)"), ("goalKeyOverride", "목표 도달 키 (빈 값=원본)") }) Property(spawn, pair.Item1, pair.Item2);
        float originalTime = WaveAuthoring.Times(Current)[selected];
        bool preparation = info.prefab != null && info.prefab.GetComponent<TrainingCourseActivator>() != null;
        float nextTime = originalTime;
        if (preparation) EditorGUILayout.HelpBox("코스 열림은 Wave 시작 전 지형 준비 이벤트입니다. 개별 시작점 이동은 Wave 상세의 시작 좌표·텔레포트 설정을 사용합니다. 아래 대기 시간은 준비가 끝난 뒤 나머지 생성 타임라인에 적용합니다.", MessageType.None);
        else nextTime = EditorGUILayout.FloatField("실제 생성 시각 (초)", originalTime);
        Property(spawn, "spawnDelay", "이 항목 생성 후 대기 (초)");
        if (device)
        {
            int other = Conflict(Occupied(info), originalTime, info.lifetime, selected);
            if (other >= 0) EditorGUILayout.HelpBox($"같은 칸의 #{other + 1} {Current.spawnObjects[other].prefab?.name}과 있는 시간이 겹칩니다. 먼저 생기는 쪽의 수명을 정하거나 생성 시각을 옮기세요.", MessageType.Warning);
        }
        serialized.ApplyModifiedProperties();
        // 상세 칸에서 위치·수명·Prefab을 바꿔도 드래그·배치와 같은 같은 칸 규칙을 지킨다. 어기면 이전 값으로 되돌린다.
        var changedSpawn = Current.spawnObjects[selected];
        // 회전은 차지하는 칸이 바뀔 때(가로·세로가 다른 장치)만 같은 칸 규칙을 다시 본다. R 키 회전과 같은 규칙이다.
        bool placementChanged = changedSpawn.prefab != previousPrefab || changedSpawn.spawnPosition != previousPosition || !Mathf.Approximately(changedSpawn.lifetime, previousLifetime)
            || WavePrefabVisuals.Footprint(changedSpawn.prefab, changedSpawn.rotationSteps) != WavePrefabVisuals.Footprint(changedSpawn.prefab, previousRotation);
        var previousSettings = changedSpawn.deviceSettings;
        if (changedSpawn.prefab != previousPrefab && WavePrefabVisuals.IsEnvironmentTool(changedSpawn.prefab))
        {
            // 크기가 다른 장치로 바꾸면 기준점을 새 크기에 맞추고, 새 장치에 없는 설정은 버린다.
            changedSpawn.spawnPosition = WavePrefabVisuals.Placement(changedSpawn.prefab, changedSpawn.spawnPosition + Offset, changedSpawn.rotationSteps) - Offset;
            changedSpawn.deviceSettings = KeepSettings(changedSpawn.prefab, changedSpawn.deviceSettings);
        }
        if (placementChanged && WavePrefabVisuals.IsEnvironmentTool(changedSpawn.prefab))
        {
            int blocking = Conflict(Occupied(changedSpawn), originalTime, changedSpawn.lifetime, selected);
            if (blocking >= 0)
            {
                changedSpawn.prefab = previousPrefab; changedSpawn.spawnPosition = previousPosition; changedSpawn.lifetime = previousLifetime;
                changedSpawn.rotationSteps = previousRotation; changedSpawn.deviceSettings = previousSettings;
                EditorUtility.SetDirty(data); serialized = new SerializedObject(data);
                ShowNotification(new GUIContent($"같은 칸의 #{blocking + 1} {Current.spawnObjects[blocking].prefab?.name}과 있는 시간이 겹쳐 되돌렸습니다."));
            }
        }
        if (nextSettings != null) { int index = selected; Mutate(() => Current.spawnObjects[index].deviceSettings = nextSettings); }
        if (nextTime != originalTime && device)
        {
            int blocking = Conflict(Occupied(Current.spawnObjects[selected]), nextTime, Current.spawnObjects[selected].lifetime, selected);
            if (blocking >= 0) { ShowNotification(new GUIContent($"그 시각에는 같은 칸의 #{blocking + 1} {Current.spawnObjects[blocking].prefab?.name}이 있어 옮길 수 없습니다.")); nextTime = originalTime; }
        }
        if (nextTime != originalTime) ChangeSpawnTime(selected, nextTime);
        var prefab = selected >= 0 && selected < Current.spawnObjects.Length ? Current.spawnObjects[selected].prefab : null;
        if (prefab != null && GUILayout.Button("선택 Prefab 원본 상세 열기")) AssetDatabase.OpenAsset(prefab);
        if (prefab != null && prefab.GetComponentInChildren<TutorialGoal>(true) != null) EditorGUILayout.HelpBox("목표 Prefab의 triggerKey와 Wave 완료 키를 맞추세요. 기존 코스는 도달 키와 마무리 완료 키가 다릅니다.", MessageType.Info);
    }
    private void BrushDetails()
    {
        GUILayout.Label($"배치할 값 · {brush.name}", EditorStyles.boldLabel);
        brushRotationSteps = EditorGUILayout.Popup("배치 방향 · R", NormalizeRotation(brushRotationSteps), rotationLabels);
        brushSettings = DeviceSettingsFields(brush, brushSettings, out _);
        EditorGUILayout.HelpBox("여기서 정한 값은 앞으로 배치하는 항목에만 저장합니다. Prefab 기본값은 바뀌지 않습니다.\n같은 칸에는 한 번에 하나만 둘 수 있습니다. 먼저 있던 장치의 수명이 끝난 시각부터 설치할 수 있습니다.", MessageType.None);
    }
    private void CopyBrushDetails()
    {
        var clip = WaveCopySlots.instance[copyBrush];
        GUILayout.Label($"복사본 {copyBrush + 1} 붙이기", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("범위", $"{clip.size.x}×{clip.size.y}칸 · {clip.items.Length}개");
        foreach (var item in clip.items.Take(12))
            EditorGUILayout.LabelField($"+{item.timeOffset:0.##}초", $"{(item.info.prefab != null ? item.info.prefab.name : "참조 없음")} ({item.offset.x:0.#}, {item.offset.y:0.#})");
        if (clip.items.Length > 12) EditorGUILayout.LabelField("", $"외 {clip.items.Length - 12}개");
        EditorGUILayout.HelpBox("격자 칸을 누르면 복사한 범위의 왼쪽 아래 칸이 그 칸에 오도록 배치합니다. 생성 시각은 지금 편집 시각에서 원래 간격을 유지하고, 방향·세부 설정·수명도 그대로 복사합니다.", MessageType.None);
    }
    // 장치의 세부 설정 칸. 기본값과 다른 값만 돌려준다. 공격 범위 표시는 바뀐 값으로 바로 다시 계산된다.
    private WaveDeviceSetting[] DeviceSettingsFields(GameObject prefab, WaveDeviceSetting[] current, out bool changed)
    {
        changed = false;
        current ??= WaveDeviceSettings.None;
        if (prefab == null || !prefab.TryGetComponent<IWaveDeviceSettings>(out var device) || device.Parameters.Length == 0)
        {
            EditorGUILayout.LabelField("세부 설정", "없음 · 방향만 바꿀 수 있습니다");
            return current;
        }
        GUILayout.Label("세부 설정", EditorStyles.miniBoldLabel);
        foreach (var parameter in device.Parameters)
        {
            float value = WaveDeviceSettings.Value(current, parameter);
            bool custom = WaveDeviceSettings.Has(current, parameter.Key);
            using (new EditorGUILayout.HorizontalScope())
            {
                var label = new GUIContent($"{parameter.Label} ({parameter.Unit})", $"{parameter.Tooltip}\n기본값 {parameter.Default:0.##}{parameter.Unit} · 범위 {parameter.Min:0.##}~{parameter.Max:0.##}");
                float next = EditorGUILayout.Slider(label, value, parameter.Min, parameter.Max);
                using (new EditorGUI.DisabledScope(!custom))
                    if (GUILayout.Button(new GUIContent("기본", $"Prefab 기본값 {parameter.Default:0.##}{parameter.Unit}로 되돌립니다."), EditorStyles.miniButton, GUILayout.Width(38))) next = parameter.Default;
                next = parameter.Clamp(next);
                if (!Mathf.Approximately(next, value)) { current = WaveDeviceSettings.With(current, parameter, next); changed = true; }
            }
        }
        if (WaveDevicePreviewCache.TryGetRange(prefab, current, out var range))
            EditorGUILayout.LabelField("공격·작동 범위", range.Shape == WaveEnvironmentTool.RangeShape.Circle ? $"반지름 {range.Size.x:0.##}칸" : $"{range.Size.x:0.##} × {range.Size.y:0.##}칸");
        return current;
    }
    private void ResizeObjectPane()
    {
        var rect = GUILayoutUtility.GetRect(40, 6, GUILayout.Height(6), GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(.12f, .15f, .19f));
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);
        int id = GUIUtility.GetControlID(735, FocusType.Passive);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
        if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id) { objectPaneHeight = Mathf.Clamp(objectPaneHeight + e.delta.y, 120, position.height * .75f); e.Use(); Repaint(); }
        if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
    }
    private void EnsurePaneStyles()
    {
        if (objectPaneStyle != null && objectPaneTexture != null && objectHeaderTexture != null) return;
        objectPaneTexture = SolidTexture(new Color(.07f, .15f, .17f));
        objectHeaderTexture = SolidTexture(new Color(.10f, .34f, .38f));
        objectPaneStyle = new GUIStyle { normal = { background = objectPaneTexture }, padding = new RectOffset(6, 6, 4, 6), margin = new RectOffset(0, 0, 0, 0) };
        objectHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { background = objectHeaderTexture, textColor = new Color(.85f, .97f, 1f) }, padding = new RectOffset(6, 6, 3, 3), margin = new RectOffset(0, 0, 0, 4) };
    }
    private static Texture2D SolidTexture(Color color)
    {
        var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color); texture.Apply();
        return texture;
    }

    // ───────────────────────────── 같은 칸 중복 금지 ─────────────────────────────
    private bool Alive(int index, float[] times) => WaveAuthoring.Alive(time, times[index], Current.spawnObjects[index].lifetime);
    // 오브젝트가 차지하는 칸. 환경 장치는 놓인 칸, Enemy는 발이 닿는 칸이다.
    private Vector2Int AnchorCell(WaveSpawnInfo spawn)
    {
        var point = spawn.spawnPosition + Offset;
        if (spawn.prefab == null || WavePrefabVisuals.IsEnvironmentTool(spawn.prefab)) return Vector2Int.FloorToInt(point);
        var shape = WavePrefabVisuals.LocalBounds(spawn.prefab, true);
        return Vector2Int.FloorToInt(point + new Vector2(shape.center.x, shape.min.y + .001f));
    }
    // 오브젝트가 차지하는 칸들. 환경 장치는 점유 크기(2×2 등)만큼, Enemy는 발이 닿는 한 칸이다.
    private RectInt Occupied(WaveSpawnInfo spawn)
    {
        if (spawn.prefab != null && WavePrefabVisuals.IsEnvironmentTool(spawn.prefab))
            return WavePrefabVisuals.Cells(spawn.spawnPosition + Offset, WavePrefabVisuals.Footprint(spawn.prefab, spawn.rotationSteps));
        return new RectInt(AnchorCell(spawn), Vector2Int.one);
    }
    // 장치를 next 방향으로 돌렸을 때 같은 칸·같은 시간에 걸리는 다른 장치. 가로·세로가 같은 장치는 늘 -1이다.
    private int RotationBlocked(int index, int next)
    {
        var spawn = Current.spawnObjects[index];
        if (WavePrefabVisuals.Footprint(spawn.prefab, next) == WavePrefabVisuals.Footprint(spawn.prefab, spawn.rotationSteps)) return -1;
        var turned = WavePrefabVisuals.Cells(spawn.spawnPosition + Offset, WavePrefabVisuals.Footprint(spawn.prefab, next));
        return Conflict(turned, WaveAuthoring.Times(Current)[index], spawn.lifetime, index);
    }
    // 다른 장치로 바꿀 때 새 장치가 받는 설정만 남긴다(값은 새 장치의 범위로 다시 맞춘다).
    private static WaveDeviceSetting[] KeepSettings(GameObject prefab, WaveDeviceSetting[] settings)
    {
        if (settings == null || settings.Length == 0 || prefab == null || !prefab.TryGetComponent<IWaveDeviceSettings>(out var device)) return WaveDeviceSettings.None;
        var result = WaveDeviceSettings.None;
        foreach (var parameter in device.Parameters)
            if (WaveDeviceSettings.Has(settings, parameter.Key)) result = WaveDeviceSettings.With(result, parameter, WaveDeviceSettings.Value(settings, parameter));
        return result;
    }
    // 차지하는 칸이 겹치고 [start, start+lifetime)과 있는 시간이 겹치는 다른 장치의 번호. 없으면 -1.
    private int Conflict(RectInt cells, float start, float lifetime, int ignore)
    {
        var spawns = Current.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
        var times = WaveAuthoring.Times(Current);
        for (int i = 0; i < spawns.Length; i++)
        {
            if (i == ignore || !WavePrefabVisuals.IsEnvironmentTool(spawns[i].prefab) || !cells.Overlaps(Occupied(spawns[i]))) continue;
            if (WaveAuthoring.Overlaps(start, lifetime, times[i], spawns[i].lifetime)) return i;
        }
        return -1;
    }
    // 새 장치를 start에 둘 수 있으면 true. 지금 그 칸에 있는 장치가 있으면 그 수명이 끝난 뒤에만 둔다.
    // 뒤에 같은 칸 장치가 예약돼 있으면, 그 전까지만 있도록 새 장치의 수명을 정한다.
    private bool FitLifetime(RectInt cells, float start, int ignore, out float lifetime)
    {
        lifetime = 0;
        var spawns = Current.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
        var times = WaveAuthoring.Times(Current);
        float next = float.PositiveInfinity;
        var cell = cells.position;
        for (int i = 0; i < spawns.Length; i++)
        {
            if (i == ignore || !WavePrefabVisuals.IsEnvironmentTool(spawns[i].prefab) || !cells.Overlaps(Occupied(spawns[i]))) continue;
            float end = spawns[i].lifetime > 0 ? times[i] + spawns[i].lifetime : float.PositiveInfinity;
            if (times[i] <= start + .0001f)
            {
                if (end <= start + .0001f) continue;
                string name = spawns[i].prefab != null ? spawns[i].prefab.name : "항목";
                ShowNotification(new GUIContent(float.IsInfinity(end)
                    ? $"({cell.x}, {cell.y}) 칸에는 #{i + 1} {name}이 있습니다.\n그 장치의 수명을 정하면 수명이 끝난 시각부터 설치할 수 있습니다."
                    : $"({cell.x}, {cell.y}) 칸에는 #{i + 1} {name}이 {end:0.##}초까지 있습니다.\n편집 시각을 {end:0.##}초 이후로 옮겨 설치하세요."));
                return false;
            }
            next = Mathf.Min(next, times[i]);
        }
        if (!float.IsInfinity(next))
        {
            lifetime = next - start;
            ShowNotification(new GUIContent($"{next:0.##}초에 같은 칸 장치가 생기므로 수명을 {lifetime:0.##}초로 정했습니다."));
        }
        return true;
    }
    private void MoveSpawn(int index, Vector2 point)
    {
        var spawn = Current.spawnObjects[index];
        var target = WavePrefabVisuals.Placement(spawn.prefab, point + (index == dragIndex ? dragOffset : Vector2.zero), spawn.rotationSteps) - Offset;
        if (target == spawn.spawnPosition) return;
        // 같은 시간대에 다른 장치가 있는 칸으로는 옮기지 않는다.
        if (WavePrefabVisuals.IsEnvironmentTool(spawn.prefab)
            && Conflict(WavePrefabVisuals.Cells(target + Offset, WavePrefabVisuals.Footprint(spawn.prefab, spawn.rotationSteps)), WaveAuthoring.Times(Current)[index], spawn.lifetime, index) >= 0) return;
        Mutate(() => Current.spawnObjects[index].spawnPosition = target);
    }

    // ───────────────────────────── 범위 선택·복사·붙이기 ─────────────────────────────
    // 두 칸 이상을 고른 경우의 범위. 한 칸 선택은 기존 선택 표시를 쓴다.
    private RectInt? AreaRect()
    {
        if (!areaStart.HasValue || !areaEnd.HasValue) return null;
        var min = Vector2Int.Min(areaStart.Value, areaEnd.Value);
        var max = Vector2Int.Max(areaStart.Value, areaEnd.Value);
        if (min == max) return null;
        return new RectInt(min, max - min + Vector2Int.one);
    }
    private void ContextCopy(Vector2 point)
    {
        var cell = Vector2Int.FloorToInt(point);
        var area = AreaRect();
        // 고른 범위는 선택 / 이동 모드에서 보일 때만 쓴다. 다른 모드에서는 우클릭한 셀만 복사한다.
        bool useArea = mode == 0 && area.HasValue && area.Value.Contains(cell);
        var region = useArea ? area.Value : new RectInt(cell, Vector2Int.one);
        var times = WaveAuthoring.Times(Current);
        var picked = new List<int>();
        for (int i = 0; i < (Current.spawnObjects?.Length ?? 0); i++)
            if (Current.spawnObjects[i].prefab != null && Alive(i, times) && region.Overlaps(Occupied(Current.spawnObjects[i]))) picked.Add(i);
        // 여러 칸을 차지하는 Enemy는 발이 닿는 칸이 아니라 몸통을 우클릭해도 그 항목을 복사한다.
        if (!useArea && picked.Count == 0)
        {
            int hit = Hit(point);
            if (hit >= 0 && Alive(hit, times) && Current.spawnObjects[hit].prefab != null)
            {
                picked.Add(hit);
                region = Occupied(Current.spawnObjects[hit]);
            }
        }
        if (picked.Count == 0)
        {
            ShowNotification(new GUIContent(useArea ? "고른 범위에 지금 시각의 오브젝트가 없습니다." : "이 셀에는 지금 시각의 오브젝트가 없습니다."));
            return;
        }
        // 한 칸 복사는 그 칸을 차지한 오브젝트 전체(2×2 장치 등)를 범위로 삼는다. 붙일 때 누른 칸에 그 왼쪽 아래 칸이 온다.
        if (!useArea)
            foreach (int i in picked)
            {
                var cells = Occupied(Current.spawnObjects[i]);
                var min = Vector2Int.Min(region.min, cells.min); var max = Vector2Int.Max(region.max, cells.max);
                region = new RectInt(min, max - min);
            }
        string names = string.Join(", ", picked.Take(4).Select(i => Current.spawnObjects[i].prefab.name)) + (picked.Count > 4 ? $" 외 {picked.Count - 4}개" : "");
        string message = useArea
            ? $"고른 {region.width}×{region.height}칸의 오브젝트 {picked.Count}개({names}) 정보를 복사하겠습니까?"
            : $"선택된 셀 ({cell.x}, {cell.y})의 정보({names})를 복사하겠습니까?";
        if (!ConfirmCopy(message)) return;
        float first = picked.Min(i => times[i]);
        Vector2 origin = region.position;
        var clip = new WaveCopyClip
        {
            label = names, size = region.size,
            items = picked.Select(i => new WaveCopyItem { info = Clone(Current.spawnObjects[i]), offset = Current.spawnObjects[i].spawnPosition + Offset - origin, timeOffset = times[i] - first }).ToArray()
        };
        ChooseCopySlot(clip, slot => StoreCopy(slot, clip));
    }
    private void StoreCopy(int slot, WaveCopyClip clip)
    {
        WaveCopySlots.instance.Set(slot, clip);
        UseCopyBrush(slot);
        ShowNotification(new GUIContent($"복사본 {slot + 1}번 칸에 담았습니다.\n배치 모드에서 격자를 누르면 붙입니다."));
    }
    private void UseCopyBrush(int slot)
    {
        if (WaveCopySlots.instance[slot].IsEmpty) return;
        copyBrush = slot; brush = null; editPlayerStart = editClearPortal = false;
        mode = 1; terrainSession = false; selected = -1; selectedBatch = -1; eventIndex = -1; selectedCell = null;
        ResetGesture(); Repaint();
    }
    private void PasteCopy(Vector2Int cell)
    {
        var clip = WaveCopySlots.instance[copyBrush];
        if (clip.IsEmpty) return;
        // 모든 장치가 들어갈 수 있을 때만 붙인다(일부만 붙지 않게).
        var planned = new List<(RectInt cells, float start, float life)>();
        foreach (var item in clip.items)
        {
            if (item.info.prefab == null || !WavePrefabVisuals.IsEnvironmentTool(item.info.prefab)) continue;
            var cells = WavePrefabVisuals.Cells(cell + item.offset, WavePrefabVisuals.Footprint(item.info.prefab, item.info.rotationSteps));
            var target = cells.position;
            float start = time + item.timeOffset;
            int other = Conflict(cells, start, item.info.lifetime, -1);
            if (other >= 0 || planned.Any(p => p.cells.Overlaps(cells) && WaveAuthoring.Overlaps(p.start, p.life, start, item.info.lifetime)))
            {
                ShowNotification(new GUIContent($"({target.x}, {target.y}) 칸에 같은 시간대의 장치가 있어 붙일 수 없습니다." + (other >= 0 ? $"\n#{other + 1} {Current.spawnObjects[other].prefab?.name}" : "")));
                return;
            }
            planned.Add((cells, start, item.info.lifetime));
        }
        Mutate(() =>
        {
            foreach (var item in clip.items)
            {
                if (item.info.prefab == null) continue;
                var info = Clone(item.info);
                info.spawnPosition = (Vector2)cell + item.offset - Offset;
                info.spawnDelay = 0;
                var list = (Current.spawnObjects ?? Array.Empty<WaveSpawnInfo>()).ToList();
                list.Add(info);
                Current.spawnObjects = list.ToArray();
                selected = WaveAuthoring.SetTime(Current, list.Count - 1, time + item.timeOffset);
            }
            selectedBatch = -1; selectedCell = null;
        });
    }
    private static WaveSpawnInfo Clone(WaveSpawnInfo source)
    {
        var copy = JsonUtility.FromJson<WaveSpawnInfo>(JsonUtility.ToJson(source));
        copy.prefab = source.prefab;
        copy.deviceSettings = source.deviceSettings != null ? (WaveDeviceSetting[])source.deviceSettings.Clone() : WaveDeviceSettings.None;
        return copy;
    }
    // 팔레트 왼쪽 아래의 복사본 3칸.
    private void CopySlots()
    {
        GUILayout.Label(new GUIContent("복사본 (3칸)", "격자 셀을 우클릭하거나, 선택 / 이동 모드에서 빈 곳을 드래그해 고른 범위를 우클릭하면 복사합니다."), EditorStyles.boldLabel);
        var slots = WaveCopySlots.instance;
        float width = Mathf.Max(40, (paletteWidth - 18) / WaveCopySlots.Count);
        using (new EditorGUILayout.HorizontalScope())
            for (int i = 0; i < WaveCopySlots.Count; i++)
            {
                var clip = slots[i];
                var rect = GUILayoutUtility.GetRect(width, 64, GUILayout.Width(width));
                var background = GUI.backgroundColor;
                if (copyBrush == i) GUI.backgroundColor = new Color(.35f, .75f, 1);
                using (new EditorGUI.DisabledScope(clip.IsEmpty))
                    if (GUI.Button(rect, new GUIContent("", clip.IsEmpty ? "비어 있음" : $"{clip.size.x}×{clip.size.y}칸 · {clip.label}"))) UseCopyBrush(i);
                GUI.backgroundColor = background;
                if (!clip.IsEmpty) DrawClip(new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 22), clip);
                GUI.Label(new Rect(rect.x + 2, rect.yMax - 17, rect.width - 4, 16), clip.IsEmpty ? $"{i + 1} · 비어 있음" : $"{i + 1} · {clip.items.Length}개", cardNameStyle);
            }
        if (copyBrush >= 0)
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("붙이기 끝내기", EditorStyles.miniButton)) { copyBrush = -1; ResetGesture(); }
                if (GUILayout.Button("이 칸 비우기", EditorStyles.miniButton)) { slots.Clear(copyBrush); copyBrush = -1; }
            }
    }
    // 복사한 범위 전체가 카드 안에 들어오도록 줄여 그린다.
    private void DrawClip(Rect rect, WaveCopyClip clip)
    {
        var area = new Rect(0, 0, Mathf.Max(1, clip.size.x), Mathf.Max(1, clip.size.y));
        var parts = new List<(WaveCopyItem item, Bounds bounds)>();
        foreach (var item in clip.items)
        {
            if (item.info.prefab == null) continue;
            var bounds = WavePrefabVisuals.RotatedBounds(item.info.prefab, item.info.rotationSteps, item.info.deviceSettings);
            area = Rect.MinMaxRect(Mathf.Min(area.xMin, item.offset.x + bounds.min.x), Mathf.Min(area.yMin, item.offset.y + bounds.min.y),
                Mathf.Max(area.xMax, item.offset.x + bounds.max.x), Mathf.Max(area.yMax, item.offset.y + bounds.max.y));
            parts.Add((item, bounds));
        }
        float scale = Mathf.Min(rect.width / Mathf.Max(.01f, area.width), rect.height / Mathf.Max(.01f, area.height));
        var bottomLeft = new Vector2(rect.center.x - area.width * scale * .5f, rect.center.y + area.height * scale * .5f);
        foreach (var (item, bounds) in parts)
        {
            var corner = bottomLeft + new Vector2((item.offset.x + bounds.min.x - area.xMin) * scale, -(item.offset.y + bounds.max.y - area.yMin) * scale);
            WavePrefabVisuals.Draw(new Rect(corner, new Vector2(bounds.size.x, bounds.size.y) * scale), item.info.prefab, true, item.info.rotationSteps, item.info.deviceSettings);
        }
    }
    private void Mutate(Action change)
    {
        Undo.RecordObject(data, "Wave 격자 편집");
        change(); EditorUtility.SetDirty(data); serialized = new SerializedObject(data); Repaint();
    }
}

[CustomEditor(typeof(WaveDataSO))]
public sealed class WaveDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Wave 격자 편집기에서 열기", GUILayout.Height(30))) GetWindow();
        DrawDefaultInspector();
    }
    private void GetWindow() => EditorWindow.GetWindow<WaveEditorWindow>("Wave 격자 편집기").Select((WaveDataSO)target);
}
