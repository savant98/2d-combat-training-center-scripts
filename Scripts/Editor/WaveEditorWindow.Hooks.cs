using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 6차(DESIGN_V6 T16·T17): Wave 격자 편집기의 파생 창 훅과 두 창 공통의 배치 도우미.
// - 던전 템플릿 편집기(DungeonTemplateEditorWindow : WaveEditorWindow)가 덮어쓰는 protected virtual 훅. 기본값은 기존 Wave 격자 편집기 동작 그대로다.
// - 파생 창이 쓰는 protected 접근자(본체 private 필드는 이름·접근 수준을 바꾸지 않는다 — 회귀 도구가 리플렉션으로 쓴다).
// - 정확한 형식만 찾는 창 열기(OpenStandard): Unity 6000.3 GetWindow<T>는 파생 형식 창도 돌려주므로(includeInheritingClasses), 열린 템플릿 편집기를 잡지 않게 한다.
// - 커서 유령 미리보기(배치 모드에서 놓일 자리·차지 칸·허용 회전·설치면을 초록/빨강으로), 허용 회전 Popup.
public partial class WaveEditorWindow
{
    public const string StandardTitle = "Wave 격자 편집기";

    /// <summary>
    /// 형식이 정확히 WaveEditorWindow인 창을 찾아 보여 준다(없으면 새로 만든다). 메뉴·Inspector 열기 버튼·'던전 방 템플릿 만들기'가 쓴다.
    /// tools의 회귀 도구는 창 없는 Editor에서 GetWindow&lt;WaveEditorWindow&gt;를 그대로 쓴다(템플릿 편집기를 열지 않은 상태 — 6차 보고서 목록).
    /// </summary>
    public static WaveEditorWindow OpenStandard()
    {
        foreach (var window in Resources.FindObjectsOfTypeAll<WaveEditorWindow>())
        {
            if (window == null || window.GetType() != typeof(WaveEditorWindow)) continue;
            window.titleContent = new GUIContent(StandardTitle);
            window.Show();
            window.Focus();
            return window;
        }
        return CreateWindow<WaveEditorWindow>(StandardTitle);
    }

    // ───────────────────────────── 파생 창 훅(기본값 = 기존 동작) ─────────────────────────────
    /// <summary>창 맨 위 툴바.</summary>
    protected virtual void DrawToolbar() => Toolbar();
    /// <summary>편집할 데이터가 없을 때.</summary>
    protected virtual void EmptyStateGui() => EditorGUILayout.HelpBox("WaveData를 선택하거나 새로 만드세요. 기존 데이터는 좌표와 생성 순서를 그대로 읽습니다.", MessageType.Info);
    /// <summary>왼쪽 열(기본: 생성 Prefab 팔레트).</summary>
    protected virtual void DrawLeftColumn() => Palette();
    /// <summary>격자 위 작은 툴바(지형·발판 편집 · 점 편집 · 전체 보기).</summary>
    protected virtual void GridToolbarGui(float middleWidth) => StandardGridToolbar(middleWidth);
    /// <summary>격자 아래 타임라인을 그릴지.</summary>
    protected virtual bool ShowTimeline => true;
    /// <summary>오른쪽 열(기본: 배치 오브젝트 상세 + Wave 상세).</summary>
    protected virtual void DrawDetailsColumn() => Details();
    /// <summary>팔레트에 보일 항목인지(탭 분류·검색 뒤에 거른다).</summary>
    protected virtual bool PaletteVisible(GameObject prefab) => true;
    /// <summary>셀 브러시를 늘 '처음 지형'(PaintCell)으로 칠할지(템플릿 맵 층 — U3: 0초 생성 예약이 생기지 않게).</summary>
    protected virtual bool ForceInitialTerrain => false;
    /// <summary>지형 편집을 허용할지. initial = 처음 지형(PaintCell), 아니면 시간별 예약. 막을 때는 알림을 띄우고 false.</summary>
    protected virtual bool TerrainEditAllowed(bool initial) => true;
    /// <summary>허용 회전·설치면이 맞지 않는 Prop 배치를 막을지(기존 창은 유령 미리보기로만 알린다).</summary>
    protected virtual bool BlockInvalidPlacement => false;
    /// <summary>배치 상세에 생성 시각·수명·처치 필수·보스·유지·생성 후 대기를 보일지(템플릿 맵 층은 실행이 쓰지 않아 숨긴다).</summary>
    protected virtual bool ShowSpawnTiming => true;
    /// <summary>Select 뒤에 부른다(파생 창이 목록 선택을 맞춘다).</summary>
    protected virtual void OnDataSelected() { }
    /// <summary>격자 클릭으로 Wave 시작점을 옮긴 뒤 부른다(템플릿 편집기는 모든 층의 시작 좌표를 맞춘다).</summary>
    protected virtual void OnPlayerStartEdited() { }
    /// <summary>격자의 시작점 표식 이름표(템플릿 맵 층은 '입장 기준점').</summary>
    protected virtual string StartPointLabel() => $"Wave {waveIndex + 1} 시작점" + (Current != null && Current.teleportAtStart ? " · Spawn" : "");

    // ───────────────────────────── 파생 창용 접근자 ─────────────────────────────
    protected WaveDataSO EditData => data;
    protected Wave CurrentWave => Current;
    protected int EditWaveIndex => waveIndex;
    protected int EditMode { get => mode; set { mode = Mathf.Clamp(value, 0, 2); ResetGesture(); } }
    protected int SelectedSpawn { get => selected; set => selected = value; }
    protected float EditTime { get => time; set => time = Mathf.Max(0f, value); }
    protected bool TerrainSessionOn { get => terrainSession; set => terrainSession = value; }
    protected bool EditingPlayerStart => editPlayerStart;
    protected GameObject Brush => brush;
    protected Vector2 SpawnOffset => Offset;
    protected float PaletteColumnWidth => paletteWidth;
    protected float DetailsColumnWidth => detailsWidth;
    protected SerializedObject SerializedData
    {
        get
        {
            if (data == null) return null;
            if (serialized == null || serialized.targetObject != data) serialized = new SerializedObject(data);
            return serialized;
        }
    }
    protected void ForgetSerialized() => serialized = null;
    protected void ApplySerialized() => serialized?.ApplyModifiedProperties();
    protected void MutateData(Action change) => Mutate(change);
    protected void SetPointEdit(int target) => SetPointEditTarget(target);
    protected void FitView() => Fit();
    /// <summary>Wave(층)를 바꾸고 선택·제스처를 비운다.</summary>
    protected void SelectLayer(int index)
    {
        if (data?.waves == null || data.waves.Length == 0) return;
        waveIndex = Mathf.Clamp(index, 0, data.waves.Length - 1);
        ClearSelection();
    }
    protected void ClearSelection()
    {
        selected = -1; selectedBatch = -1; eventIndex = -1; selectedCell = null; areaStart = areaEnd = null;
        ResetGesture();
        Repaint();
    }
    /// <summary>월드(방) 좌표 point를 격자 가운데로 옮긴다.</summary>
    protected void FocusWorldPoint(Vector2 point)
    {
        pan = new Vector2(-point.x, point.y) * zoom;
        Repaint();
    }
    protected void DrawPalettePane() => Palette();
    protected void DrawObjectPane() { ObjectPane(); ResizeObjectPane(); }
    protected void DrawStandardToolbar() => Toolbar();
    protected void DrawStandardGridToolbar(float middleWidth) => StandardGridToolbar(middleWidth);
    protected void Notify(string message) => ShowNotification(new GUIContent(message));

    // ───────────────────────────── 허용 회전(카탈로그) ─────────────────────────────
    /// <summary>카탈로그 허용 회전·설치면 규칙을 적용할 항목(없으면 null): 템플릿이면 카탈로그 항목 전부, 일반 WaveData면 던전 Prop만.
    /// 6차 이전 Wave 장치(JumpPadCell 등)는 일반 Wave에서 예전처럼 네 방향(벽 점프대 90°·270°)으로 둔다.</summary>
    private DungeonPropCatalogSO.Entry CatalogRule(GameObject prefab)
        => prefab != null && (data is DungeonRoomTemplateSO || prefab.GetComponent<DungeonProp>() != null) ? DungeonPropCatalogSO.Lookup(prefab) : null;
    private int NextRotationFor(GameObject prefab, int from) => CatalogRule(prefab)?.NextAllowed(from) ?? NormalizeRotation(from + 1);
    private int FitRotationFor(GameObject prefab, int value) => CatalogRule(prefab)?.FirstAllowed(value) ?? NormalizeRotation(value);
    /// <summary>허용 회전만 보이는 방향 Popup(카탈로그 밖이면 네 방향). 지금 값이 허용 밖이면 '(허용 안 됨)'으로 보이고 그대로 둔다.</summary>
    private int RotationField(string label, GameObject prefab, int value)
    {
        int current = NormalizeRotation(value);
        var entry = CatalogRule(prefab);
        if (entry == null) return EditorGUILayout.Popup(label + " · R", current, rotationLabels);
        var allowed = Enumerable.Range(0, 4).Where(entry.Allows).ToList();
        if (allowed.Count == 0) allowed.Add(0);
        var labels = allowed.Select(r => rotationLabels[r]).ToList();
        int shown = allowed.IndexOf(current);
        if (shown < 0) { labels.Add(rotationLabels[current] + " (허용 안 됨)"); shown = labels.Count - 1; }
        int picked = EditorGUILayout.Popup(new GUIContent(label + " · R", $"이 Prop의 허용 회전: {string.Join("·", allowed.Select(r => rotationLabels[r]))}(Prop 카탈로그)."), shown, labels.Select(l => new GUIContent(l)).ToArray());
        return picked < allowed.Count ? allowed[picked] : current;
    }

    // ───────────────────────────── 커서 유령 미리보기(T17 — 두 창 공통) ─────────────────────────────
    private Vector2? hoverPoint;
    private GUIStyle ghostLabelStyle;
    /// <summary>격자 위 마우스 위치(월드). 배치 모드에서 반 칸 넘게 움직이면 다시 그린다(wantsMouseMove).</summary>
    private void TrackHover(Event e)
    {
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
        {
            Vector2? next = canvas.Contains(e.mousePosition) ? World(e.mousePosition) : (Vector2?)null;
            bool changed = next.HasValue != hoverPoint.HasValue
                || next.HasValue && Vector2Int.FloorToInt(next.Value * 2f) != Vector2Int.FloorToInt(hoverPoint.Value * 2f);
            hoverPoint = next;
            if (changed && mode == 1 && e.type == EventType.MouseMove) Repaint();
        }
        else if (e.type == EventType.MouseLeaveWindow && hoverPoint.HasValue) { hoverPoint = null; Repaint(); }
    }
    /// <summary>회귀 도구용: 마우스 위치를 직접 정한다(창 없는 Editor는 MouseMove가 없다).</summary>
    internal void SetHoverPoint(Vector2? point) { hoverPoint = point; Repaint(); }

    /// <summary>유령 판정 결과(회귀 도구가 읽는다): 놓일 기준점·차지 칸·문제(없으면 null).</summary>
    internal (bool ok, Vector2 target, RectInt cells, string problem) GhostState()
    {
        if (!hoverPoint.HasValue || brush == null || Current == null) return (false, default, default, "브러시·마우스 위치 없음");
        var point = hoverPoint.Value;
        if (brush.GetComponent<WaveTerrainCellView>() != null)
        {
            var cell = Vector2Int.FloorToInt(point);
            bool initial = ForceInitialTerrain || terrainSession && terrainTarget == 1;
            return (true, cell, new RectInt(cell, Vector2Int.one), TerrainPreviewAllowed(initial) ? null : "지형 칠하기 막힘");
        }
        bool device = WavePrefabVisuals.IsEnvironmentTool(brush);
        int steps = device ? NormalizeRotation(brushRotationSteps) : 0;
        var target = WavePrefabVisuals.Placement(brush, point, steps);
        var cells = device ? WavePrefabVisuals.OccupiedCells(brush, target, steps, brushSettings) : new RectInt(AnchorCell(new WaveSpawnInfo { prefab = brush, spawnPosition = target - Offset }), Vector2Int.one);
        string problem = device ? PlacementProblemAt(brush, cells, steps, -1) : null;
        return (problem == null, target, cells, problem);
    }
    // 지형 칠하기 막힘 여부(알림 없이 — 미리보기용). 파생 창의 TerrainEditAllowed는 알림을 띄우므로 따로 본다.
    protected virtual bool TerrainPreviewAllowed(bool initial) => true;

    /// <summary>
    /// prefab을 steps로 cells에 둘 때의 문제(허용 회전·같은 칸·설치면). 없으면 null. ignore = 같은 칸 검사에서 뺄 항목 번호.
    /// 설치면은 카탈로그 Prop만 본다(지형 = 지금 편집 시각의 셀, 받침 = 같은 Wave(템플릿 적 구성 층이면 맵 층도)의 고체·윗면 칸).
    /// </summary>
    private string PlacementProblemAt(GameObject prefab, RectInt cells, int steps, int ignore)
    {
        var entry = CatalogRule(prefab);
        if (entry != null && !entry.Allows(steps)) return $"허용 회전 아님({string.Join("·", Enumerable.Range(0, 4).Where(entry.Allows).Select(r => rotationLabels[r]))})";
        if (WavePrefabVisuals.OccupiesCells(prefab))
        {
            int other = OccupantAt(cells, ignore);
            if (other >= 0) return $"같은 칸: #{other + 1} {DungeonPropCatalogSO.DisplayName(Current.spawnObjects[other].prefab)}";
        }
        if (entry == null || data == null || !data.cellMap.enabled) return null;
        var terrain = geometry.CachedStyledCells(data, waveIndex, time);
        var solid = new HashSet<Vector2Int>();
        var support = new HashSet<Vector2Int>();
        var times = WaveAuthoring.Times(Current);
        for (int i = 0; i < (Current.spawnObjects?.Length ?? 0); i++)
            if (i != ignore && Alive(i, times)) DungeonTemplateValidation.CollisionCells(Current.spawnObjects[i], Offset, solid, support);
        if (data is DungeonRoomTemplateSO && waveIndex > 0 && data.waves[0]?.spawnObjects != null)
            foreach (var spawn in data.waves[0].spawnObjects) DungeonTemplateValidation.CollisionCells(spawn, WaveRoomState.LayoutAt(data, 0).spawnOffset, solid, support);
        bool TerrainSolid(Vector2Int c) => terrain.TryGetValue(c, out var cell) && cell.kind == WaveCellKind.Platform;
        bool TerrainSupport(Vector2Int c) => terrain.ContainsKey(c);
        return DungeonTemplateValidation.PlacementProblem(prefab, cells, steps, TerrainSolid, TerrainSupport, solid.Contains, support.Contains);
    }
    // 지금 편집 시각에 cells를 차지한 다른 장치(없으면 -1). FitLifetime과 같은 판정(뒤에 생기는 장치는 막지 않는다).
    private int OccupantAt(RectInt cells, int ignore)
    {
        var spawns = Current?.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
        var times = WaveAuthoring.Times(Current);
        for (int i = 0; i < spawns.Length; i++)
        {
            if (i == ignore || !WavePrefabVisuals.OccupiesCells(spawns[i].prefab) || !cells.Overlaps(Occupied(spawns[i]))) continue;
            float end = spawns[i].lifetime > 0 ? times[i] + spawns[i].lifetime : float.PositiveInfinity;
            if (times[i] <= time + .0001f && end > time + .0001f) return i;
        }
        return -1;
    }

    /// <summary>DrawGrid의 GUI.BeginClip(canvas) 안에서 부른다(Repaint만).</summary>
    private void DrawPlacementGhost()
    {
        if (Event.current.type != EventType.Repaint || !hoverPoint.HasValue || mode != 1 || copyBrush >= 0 || editPlayerStart || editClearPortal || brush == null || Current == null) return;
        var (ok, target, cells, problem) = GhostState();
        Vector2 Local(Vector2 p) => Screen(p) - canvas.position;
        if (brush.GetComponent<WaveTerrainCellView>() == null)
        {
            bool device = WavePrefabVisuals.IsEnvironmentTool(brush);
            int steps = device ? NormalizeRotation(brushRotationSteps) : 0;
            var bounds = WavePrefabVisuals.RotatedBounds(brush, steps, brushSettings);
            var visual = new Rect(Local(target + new Vector2(bounds.min.x, bounds.max.y)), new Vector2(bounds.size.x, bounds.size.y) * zoom);
            var previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, .45f);
            try { WavePrefabVisuals.Draw(visual, brush, true, steps, brushSettings); }
            finally { GUI.color = previous; }
            if (device) WavePrefabVisuals.DrawRange(brush, steps, Local(target), zoom, brushSettings, .45f);
        }
        var color = ok ? new Color(.35f, .95f, .55f) : new Color(1f, .32f, .25f);
        var box = new Rect(Local(new Vector2(cells.xMin, cells.yMax)), (Vector2)cells.size * zoom);
        EditorGUI.DrawRect(box, new Color(color.r, color.g, color.b, .14f));
        Handles.BeginGUI();
        try
        {
            Handles.color = color;
            Handles.DrawAAPolyLine(2f, new Vector3(box.xMin, box.yMin), new Vector3(box.xMax, box.yMin), new Vector3(box.xMax, box.yMax), new Vector3(box.xMin, box.yMax), new Vector3(box.xMin, box.yMin));
        }
        finally { Handles.EndGUI(); }
        if (!ok && !string.IsNullOrEmpty(problem))
        {
            ghostLabelStyle ??= new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(1f, .45f, .4f) }, clipping = TextClipping.Overflow };
            GUI.Label(new Rect(box.x, box.yMax + 2f, Mathf.Max(box.width, 220f), 16f), problem, ghostLabelStyle);
        }
    }
}
