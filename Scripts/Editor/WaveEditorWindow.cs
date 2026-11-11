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
    private Rect leftSplitter, rightSplitter;
    private readonly WaveGridGeometry geometry = new();
    private Wave Current => data != null && data.waves != null && waveIndex >= 0 && waveIndex < data.waves.Length ? data.waves[waveIndex] : null;
    public WaveDataSO Data => data;

    [MenuItem("Tools/Training/Wave 격자 편집기")]
    public static void Open() => GetWindow<WaveEditorWindow>("Wave 격자 편집기");
    public void Select(WaveDataSO value)
    {
        pendingBackground = value != null ? value.backgroundPrefab : null;
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
    private void OnInspectorUpdate() { Repaint(); }
    private void OnProjectChange() { ReloadPalette(); geometry.Invalidate(); WavePrefabVisuals.Invalidate(); Repaint(); }
    private void OnDisable() { Undo.undoRedoPerformed -= Refresh; ResetGesture(); }
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
                    EditorGUILayout.HelpBox(terrainTarget==1?"처음부터 있을 전체 맵입니다. Wave 전환 때 달라진 칸만 애니메이션으로 교체합니다.":$"{time:G9}초 예약 · 칸을 선택해 생성/소멸 시각과 수명을 설정하세요. 지우기는 예약 취소입니다.",MessageType.None);
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
            if (nextWave != waveIndex) { waveIndex = nextWave; selected = -1; selectedBatch = -1; eventIndex = -1; selectedCell = null; ResetGesture(); }
            if (GUILayout.Button("+ Wave", EditorStyles.toolbarButton)) Mutate(() => { var list = (data.waves ?? Array.Empty<Wave>()).ToList(); list.Add(new Wave { spawnObjects = Array.Empty<WaveSpawnInfo>() }); data.waves = list.ToArray(); waveIndex = list.Count - 1; selected = -1; selectedBatch = -1; selectedCell = null; eventIndex = -1; ResetGesture(); });
            if (GUILayout.Button("복제", EditorStyles.toolbarButton) && Current != null) Mutate(() => { var copy = JsonUtility.FromJson<Wave>(JsonUtility.ToJson(Current)); var list = data.waves.ToList(); list.Insert(waveIndex + 1, copy); data.waves = list.ToArray(); waveIndex++; selected = -1; selectedBatch = -1; selectedCell = null; eventIndex = -1; ResetGesture(); });
            if (GUILayout.Button("삭제", EditorStyles.toolbarButton) && data.waves.Length > 1) Mutate(() => { var list = data.waves.ToList(); list.RemoveAt(waveIndex); data.waves = list.ToArray(); waveIndex = Mathf.Min(waveIndex, list.Count - 1); selected = -1; selectedBatch = -1; selectedCell = null; eventIndex = -1; ResetGesture(); });
            if (GUILayout.Button("검사", EditorStyles.toolbarButton)) EditorUtility.DisplayDialog("WaveData 검사", string.Join("\n", WaveAuthoring.Validate(data).DefaultIfEmpty("데이터 검사 통과")), "확인");
            if (GUILayout.Button("저장", EditorStyles.toolbarButton)) AssetDatabase.SaveAssets();
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
            {
                brushRotationSteps = EditorGUILayout.Popup("배치 방향", NormalizeRotation(brushRotationSteps), rotationLabels);
                EditorGUILayout.HelpBox("셀 중심에 배치 · R: 반시계 90° 회전\n수명·생성 시각은 배치 항목 상세에서 설정합니다.", MessageType.None);
            }
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
                        WavePrefabVisuals.Draw(new Rect(rect.x + 6, rect.y + 4, iconSize, iconSize), item, false, item == brush && WavePrefabVisuals.IsEnvironmentTool(item) ? brushRotationSteps : 0);
                        GUI.Label(new Rect(rect.x + 3, rect.y + iconSize + 6, rect.width - 6, 32), new GUIContent(item.GetComponent<WaveTerrainCellView>() != null ? item.GetComponent<WaveTerrainCellView>().kind.ToString() : item.name, item.name), cardNameStyle);
                    }
            }
            EditorGUILayout.EndScrollView();
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
        var styledCells = data.cellMap.enabled ? geometry.StyledCells(data, waveIndex, time) : null;
        var typedCells = styledCells?.ToDictionary(row => row.Key, row => row.Value.kind);
        foreach (var cell in typedCells != null ? (IEnumerable<Vector2Int>)typedCells.Keys : geometry.Cells(data, waveIndex, time))
        {
            Vector2 p = Local(new Vector2(cell.x, cell.y + 1));
            var rect = new Rect(p + Vector2.one * .6f, Vector2.one * Mathf.Max(1, zoom - 1.2f));
            if (rect.Overlaps(new Rect(Vector2.zero, canvas.size)))
            {
                if (styledCells != null) WavePrefabVisuals.DrawCell(new Rect(p,Vector2.one*zoom),styledCells[cell],data.cellMap);
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
        for (int i = 0; i < (Current.spawnObjects?.Length ?? 0); i++)
        {
            var spawn = Current.spawnObjects[i];
            var global = MarkerRect(i);
            var rect = new Rect(global.position - canvas.position, global.size);
            var frame = new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6);
            EditorGUI.DrawRect(frame, i == selected ? Color.white : TimingColor(times[i]));
            EditorGUI.DrawRect(rect, new Color(.09f, .11f, .15f, .96f));
            WavePrefabVisuals.Draw(rect, spawn.prefab, true, spawn.rotationSteps);
            GUI.Label(new Rect(rect.x - 30, rect.yMax + 2, rect.width + 60, 18), spawn.prefab != null ? spawn.prefab.name : "참조 없음", markerStyle);
            if (spawn.isBossEnemy) GUI.Label(new Rect(rect.xMax - 15, rect.y, 15, 18), "B", markerStyle);
        }
        DrawSpawnPoint(Local(WaveRoomState.PlayerStartAt(data, waveIndex)), new Color(.2f, .7f, 1), $"Wave {waveIndex + 1} 시작점" + (Current.teleportAtStart ? " · Spawn" : ""));
        DrawSpawnPoint(Local(Offset), new Color(1, .72f, .2f), "Enemy / 발판 생성 기준");
        if (data.overrideClearPortalPosition)
            DrawSpawnPoint(Local(data.clearPortalPosition), new Color(.75f, .9f, .35f), "전체 Wave 완료 · Lobby 포탈");
        GUI.EndClip();
    }
    private Rect MarkerRect(int index)
    {
        var spawn = Current.spawnObjects[index];
        var bounds = WavePrefabVisuals.RotatedBounds(spawn.prefab, spawn.rotationSteps);
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
        brush = value;if (WavePrefabVisuals.Classify(value) == WavePrefabVisuals.Category.Enemy || WavePrefabVisuals.IsEnvironmentTool(value)) { terrainSession = false;selected = -1;selectedBatch = -1;eventIndex = -1;selectedCell = null; }ResetGesture();
    }
    private static int NormalizeRotation(int value) => ((value % 4) + 4) % 4;
    private void RotationShortcut()
    {
        var e = Event.current;
        if (e.type != EventType.KeyDown || e.keyCode != KeyCode.R || e.control || e.command || e.alt || EditorGUIUtility.editingTextField || GUIUtility.hotControl != 0) return;
        if (mode == 0 && selected >= 0 && selected < (Current.spawnObjects?.Length ?? 0) && WavePrefabVisuals.IsEnvironmentTool(Current.spawnObjects[selected].prefab))
            Mutate(() => Current.spawnObjects[selected].rotationSteps = NormalizeRotation(Current.spawnObjects[selected].rotationSteps + 1));
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
        gridGesture = false; gridControl = 0; dragIndex = -1; lastPaint = null;
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
            else if (dragIndex >= 0) Mutate(() => Current.spawnObjects[dragIndex].spawnPosition = WavePrefabVisuals.Placement(Current.spawnObjects[dragIndex].prefab, point) - Offset);
            return;
        }
        if (lastPaint == cell) return;
        lastPaint = cell;
        if (mode == 2) { EraseGrid(point); return; }
        if (mode != 1) return;
        if (editClearPortal) Mutate(() => { data.overrideClearPortalPosition = true; data.clearPortalPosition = Vector2Int.RoundToInt(point); });
        else if (editPlayerStart) Mutate(() => { Current.overridePlayerStart = true; Current.playerStart = Vector2Int.RoundToInt(point); });
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
        if (spawn >= 0) { SelectTimelineEntry(0,spawn); dragIndex = spawn; return; }
        var cell = Vector2Int.FloorToInt(point);
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
        for (int i = (Current.spawnObjects?.Length ?? 0) - 1; i >= 0; i--)
            if (new Rect(MarkerRect(i).center - Vector2.Max(MarkerRect(i).size, Vector2.one * 12) * .5f, Vector2.Max(MarkerRect(i).size, Vector2.one * 12)).Contains(Screen(point))) return i;
        return -1;
    }
    private void Place(Vector2 point)
    {
        if (brush == null || !EditorUtility.IsPersistent(brush) || !WavePrefabVisuals.IsPaletteItem(brush)) return;
        if (data.cellMap.enabled && brush.GetComponent<WaveTerrainCellView>() != null) { if (terrainSession && terrainTarget == 1) PaintCell(Vector2Int.FloorToInt(point), false); else PaintTimedCell(Vector2Int.FloorToInt(point)); return; }
        Mutate(() =>
        {
            var list = (Current.spawnObjects ?? Array.Empty<WaveSpawnInfo>()).ToList();
            list.Add(new WaveSpawnInfo { prefab = brush, spawnPosition = WavePrefabVisuals.Placement(brush, point) - Offset, rotationSteps = WavePrefabVisuals.IsEnvironmentTool(brush) ? NormalizeRotation(brushRotationSteps) : 0, isRequiredToClear = brush.GetComponent<EnemyController>() != null });
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
                cell.style = cell.kind == WaveCellKind.OneWayPlatform ? 1 : 0;
                cells.Add(cell);
            }
            Current.cells = cells.OrderBy(c => c.position.y).ThenBy(c => c.position.x).ToArray();
            selectedCell = position; selectedBatch = -1; selected = -1;
        });
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
            if (!erase) cells.Add(new WaveMapCell { position = position, style = creationStyle, kind = brush != null && brush.GetComponent<WaveTerrainCellView>() != null ? brush.GetComponent<WaveTerrainCellView>().kind : WaveCellKind.Platform });
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
                if(!entry.custom && entry.operation==WaveTerrainOperation.Add && Mathf.Abs(entry.time-time)<=.0005f && (brush==null || brush.GetComponent<WaveTerrainCellView>()==null || entry.cellKind==brush.GetComponent<WaveTerrainCellView>().kind))
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
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);
            if (serialized == null || serialized.targetObject != data) serialized = new SerializedObject(data);
            serialized.Update();
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
            EditorGUILayout.HelpBox("지형 준비 중에는 이동·공격을 잠급니다. 체크하면 준비가 끝난 뒤 시작 좌표로 이동하고 기존 Spawn 연출을 재생한 다음 Wave를 시작합니다. 체크하지 않으면 현재 위치를 유지합니다.", MessageType.None);
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
                    EditorGUILayout.HelpBox("커스텀 Room은 기존 카메라·코스 연출을 유지합니다. 실내 크기가 변하는 관리자 평가에서 위 설정을 사용합니다.", MessageType.None);
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
            if (selected >= 0 && selected < (Current.spawnObjects?.Length ?? 0))
            {
                GUILayout.Space(12); GUILayout.Label($"선택한 생성 항목 #{selected + 1}", EditorStyles.boldLabel);
                var spawn = wave.FindPropertyRelative("spawnObjects").GetArrayElementAtIndex(selected);
                foreach (var pair in new[] { ("prefab", "Prefab"), ("spawnPosition", "생성 좌표 (원본 보존)"), ("isRequiredToClear", "처치 필수 목표"), ("isBossEnemy", "보스 등장"), ("keepAfterWave", "Wave 종료 후 유지"), ("lifetime", "생성 후 유지 제한 (0=무제한)"), ("goalKeyOverride", "목표 도달 키 (빈 값=원본)") }) Property(spawn, pair.Item1, pair.Item2);
                if (WavePrefabVisuals.IsEnvironmentTool(Current.spawnObjects[selected].prefab))
                {
                    var rotation = spawn.FindPropertyRelative("rotationSteps");
                    rotation.intValue = EditorGUILayout.Popup("설치 방향 · R", NormalizeRotation(rotation.intValue), rotationLabels);
                    EditorGUILayout.HelpBox("0°는 Prefab 원본 방향입니다. 셀 중심을 기준으로 반시계 90°씩 회전합니다.", MessageType.None);
                }
                float originalTime = WaveAuthoring.Times(Current)[selected];
                bool preparation = Current.spawnObjects[selected].prefab != null && Current.spawnObjects[selected].prefab.GetComponent<TrainingCourseActivator>() != null;
                float nextTime = originalTime;
                if (preparation) EditorGUILayout.HelpBox("코스 열림은 Wave 시작 전 지형 준비 이벤트입니다. 개별 시작점 이동은 위의 Wave 시작 좌표·텔레포트 설정을 사용합니다. 아래 대기 시간은 준비가 끝난 뒤 나머지 생성 타임라인에 적용합니다.", MessageType.None);
                else nextTime = EditorGUILayout.FloatField("실제 생성 시각 (초)", originalTime);
                Property(spawn, "spawnDelay", "이 항목 생성 후 대기 (초)");
                serialized.ApplyModifiedProperties();
                if (nextTime != originalTime) ChangeSpawnTime(selected,nextTime);
                var prefab = Current.spawnObjects[selected].prefab;
                if (prefab != null && GUILayout.Button("선택 Prefab 원본 상세 열기")) AssetDatabase.OpenAsset(prefab);
                if (prefab != null && prefab.GetComponentInChildren<TutorialGoal>(true) != null) EditorGUILayout.HelpBox("목표 Prefab의 triggerKey와 Wave 완료 키를 맞추세요. 기존 코스는 도달 키와 마무리 완료 키가 다릅니다.", MessageType.Info);
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
                int next=EditorGUILayout.Popup("외형",Mathf.Clamp(style,0,1),new[]{"기본 지형","생성 발광"});
                if(next!=Mathf.Clamp(style,0,1))
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
