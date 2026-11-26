using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 던전 템플릿 편집기(6차 DESIGN_V6 T16 — 메뉴 Tools/Training/던전 템플릿 편집기). Wave 격자 편집기(WaveEditorWindow)를 그대로 물려받아 격자·팔레트·배치·Undo·복사를 쓰고,
/// 템플릿 정본(Assets/Data/Dungeon/Templates/&lt;테마&gt;/*.asset)을 고치는 데 필요한 것만 보인다.
/// - 왼쪽: 템플릿 목록(테마 폴더별 · 모양·실내 크기·꼬리표·검사 오류/경고 수·사용처 · 검색 · 새 템플릿 · 복제 · 이름 바꾸기(GUID 유지)) + 생성 Prefab 팔레트
///   (템플릿에 쓰지 않는 Wave 장치·직접 두지 않는 실물·테마 금지 Prop 숨김, 맵 층은 적 숨김, 적 구성 층은 적만).
/// - 가운데: 격자(커서 유령 미리보기 · 검사 위반 칸 빨강 점선). 맵 층의 셀 브러시는 늘 '처음 지형'을 칠한다(0초 생성 예약이 생기지 않게 — U3).
///   '지형 잠금'(기본 켬)이 켜져 있으면 칠하지 않는다. 적 구성 층에서는 지형을 칠할 수 없다. 맵 층은 타임라인을 숨긴다(실행이 생성 시각을 쓰지 않음).
/// - 오른쪽: 템플릿 메타(모양·역할·컨셉·크기·방향·배경·꼬리표·설계 메모) → 배치 오브젝트 상세(경로 크레인 경로 패널 포함) → 층 정보(입장 기준점·적 구성) → 검사 결과(누르면 그 배치로 이동).
/// - 숨긴 것(tool.md §5.3): 새 WaveData·일반 WaveData 검사, 공통 기본 지형(Room 범위·선 재질·Wave별 지형 변경)·시간별 지형 예약, Room 콘텐츠·배경·통창·BGM·훈련 기록,
///   시작 시 텔레포트·크레인 운반·완료 포탈·클리어 조건·튜토리얼·대기·커스텀 연출, Wave 진행 연출·던전 설정, 맵 층 생성 시각·수명·처치 필수·보스·유지.
/// 저장은 이 템플릿만(SaveAssetIfDirty). 검사는 단일 검사기(DungeonTemplateValidation) — 테마 폴더 실물은 빌더와 같은 기준.
/// </summary>
public sealed class DungeonTemplateEditorWindow : WaveEditorWindow
{
    public const string Title = "던전 템플릿 편집기";

    [SerializeField] private string templateRoot = DungeonTemplateValidation.TemplatesRoot;
    [SerializeField] private bool terrainLock = true;
    [SerializeField] private float listHeight = 260f;
    [SerializeField] private string listSearch = "";
    [SerializeField] private bool showMeta = true, showLayer = true, showIssues = true;
    private Vector2 listScroll, detailScroll;
    private DungeonRoomTemplateSO renameTarget;
    private string renameText = "";
    private List<Row> rows;
    private Dictionary<DungeonRoomTemplateSO, List<string>> usage;
    private string lastNotice;
    private double lastNoticeTime;
    private GUIStyle rowStyle, rowSelectedStyle;

    private sealed class Row
    {
        public DungeonRoomTemplateSO Template;
        public string Path, Theme;
        public int Errors = -1, Warnings = -1, Dirty = -1;
        public string Size = "";
    }

    [MenuItem("Tools/Training/던전 템플릿 편집기")]
    public static void OpenFromMenu() => Open(null);
    /// <summary>템플릿 편집기를 열고 template을 고른다(null이면 창만).</summary>
    public static DungeonTemplateEditorWindow Open(DungeonRoomTemplateSO template)
    {
        var window = GetWindow<DungeonTemplateEditorWindow>(Title);
        if (template != null) window.Select(template);
        window.Show();
        window.Focus();
        return window;
    }

    /// <summary>템플릿 목록을 찾을 폴더(기본 Assets/Data/Dungeon/Templates — 회귀 도구가 임시 폴더로 바꾼다).</summary>
    public string TemplateRoot
    {
        get => templateRoot;
        set { templateRoot = string.IsNullOrWhiteSpace(value) ? DungeonTemplateValidation.TemplatesRoot : value.Replace('\\', '/').TrimEnd('/'); rows = null; usage = null; Repaint(); }
    }
    /// <summary>지형 잠금(기본 켬). 켜져 있으면 맵 층 지형을 칠하지 않는다.</summary>
    public bool TerrainLock { get => terrainLock; set { terrainLock = value; Repaint(); } }
    public DungeonRoomTemplateSO Template => EditData as DungeonRoomTemplateSO;
    private bool MapLayer => EditWaveIndex == 0;
    private string Theme => Template != null ? DungeonTemplateValidation.OptionsFor(Template).Theme : null;

    private void OnFocus() { rows = null; usage = null; }

    // ───────────────────────────── 훅 ─────────────────────────────
    protected override bool ForceInitialTerrain => MapLayer;
    protected override bool BlockInvalidPlacement => true;
    protected override bool ShowSpawnTiming => !MapLayer;
    protected override bool ShowTimeline => !MapLayer;
    protected override bool TerrainEditAllowed(bool initial)
    {
        string reason = TerrainBlockReason(initial);
        if (reason == null) return true;
        NotifyOnce(reason);
        return false;
    }
    protected override bool TerrainPreviewAllowed(bool initial) => TerrainBlockReason(initial) == null;
    private string TerrainBlockReason(bool initial)
    {
        if (!(EditData is DungeonRoomTemplateSO)) return null;
        if (!MapLayer) return "적 구성 층에서는 지형을 칠하지 않습니다(맵 층 지형을 그대로 씁니다).";
        if (!initial) return "템플릿은 처음 지형만 씁니다(시간별 지형 예약은 실행이 무시합니다).";
        if (terrainLock) return "지형 잠금이 켜져 있습니다. 격자 위 '지형 잠금'을 끄고 칠하세요.";
        return null;
    }
    private void NotifyOnce(string message)
    {
        double now = EditorApplication.timeSinceStartup;
        if (message == lastNotice && now - lastNoticeTime < 1.5) return;
        lastNotice = message; lastNoticeTime = now;
        Notify(message);
    }
    protected override bool PaletteVisible(GameObject prefab)
    {
        if (prefab == null) return false;
        var catalog = DungeonPropCatalogSO.Active;
        if (catalog.HiddenInTemplateTool(prefab) || WavePrefabVisuals.DeletedFacilityName(prefab) != null) return false;
        if (WavePrefabVisuals.Classify(prefab) == WavePrefabVisuals.Category.Goal) return false;
        var entry = catalog.Find(prefab);
        if (entry != null && !entry.templateAllowed) return false;
        string theme = Theme;
        if (!string.IsNullOrEmpty(theme) && catalog.Forbidden(theme, prefab.name, out _)) return false;
        bool enemy = prefab.GetComponent<EnemyController>() != null;
        // 맵 층: 지형·Prop·함정·시설·표식(적 없음). 적 구성 층: 적만(6차 T8 — 함정 금지, 지형은 맵 층 그대로).
        return MapLayer ? !enemy : enemy;
    }
    protected override string StartPointLabel() => MapLayer ? "입장 기준점(맵 층 시작 좌표)" : "입장 기준점(맵 층과 같음)";
    protected override void OnPlayerStartEdited() => SyncStartToAllLayers();
    protected override void OnDataSelected()
    {
        EditTime = 0f;
        if (Template != null) TerrainSessionOn = false;
    }
    // 모든 층의 시작 좌표를 맵 층과 같게 둔다(빌더 규약 — 실행은 맵 층 시작 좌표를 입장 기준점으로 쓴다).
    private void SyncStartToAllLayers()
    {
        var template = Template;
        if (template?.waves == null || template.waves.Length == 0) return;
        var start = WaveRoomState.PlayerStartAt(template, 0);
        if (template.waves.All(w => w != null && w.overridePlayerStart && w.playerStart == start)) return;
        MutateData(() =>
        {
            foreach (var wave in template.waves)
            {
                if (wave == null) continue;
                wave.overridePlayerStart = true;
                wave.playerStart = start;
            }
        });
    }

    // ───────────────────────────── 툴바 ─────────────────────────────
    protected override void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            var template = Template;
            var next = (DungeonRoomTemplateSO)EditorGUILayout.ObjectField(template, typeof(DungeonRoomTemplateSO), false, GUILayout.MinWidth(180));
            if (next != template) { ApplySerialized(); Select(next); GUIUtility.ExitGUI(); }
            if (template == null) { GUILayout.FlexibleSpace(); return; }
            var names = TemplateLayerNames();
            int layer = Mathf.Clamp(EditWaveIndex, 0, Mathf.Max(0, names.Length - 1));
            int picked = EditorGUILayout.Popup(layer, names, GUILayout.Width(230));
            if (picked != layer) SwitchLayer(picked);
            if (GUILayout.Button(new GUIContent("+ 적 구성", "맵 층 지형을 그대로 쓰는 새 적 구성 Wave를 만듭니다."), EditorStyles.toolbarButton)) { ApplySerialized(); TemplateAddEncounter(); GUIUtility.ExitGUI(); }
            using (new EditorGUI.DisabledScope(MapLayer))
            {
                if (GUILayout.Button(new GUIContent("복제", "지금 적 구성을 바로 뒤에 복제합니다(이름·난이도·가중치 포함)."), EditorStyles.toolbarButton)) { ApplySerialized(); TemplateDuplicateEncounter(); GUIUtility.ExitGUI(); }
                if (GUILayout.Button(new GUIContent("삭제", "지금 적 구성과 그 정보를 지웁니다."), EditorStyles.toolbarButton)) { ApplySerialized(); TemplateRemoveEncounter(); GUIUtility.ExitGUI(); }
            }
            if (GUILayout.Button(new GUIContent("템플릿 검사", "단일 검사기로 다시 검사합니다(테마 폴더 실물은 빌더와 같은 기준)."), EditorStyles.toolbarButton))
            {
                ApplySerialized();
                TemplateRevalidate();
                var issues = TemplateIssues(template);
                showIssues = true;
                Notify(issues.Count == 0 ? "템플릿 검사 통과" : $"템플릿 검사: 오류 {issues.Count(i => i.Error)} · 경고 {issues.Count(i => !i.Error)}");
                MarkRowDirty(template);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Wave 격자 편집기로", "같은 템플릿을 기존 Wave 격자 편집기(모든 Wave 항목)로 엽니다."), EditorStyles.toolbarButton)) { ApplySerialized(); WaveEditorWindow.OpenStandard().Select(template); }
            using (new EditorGUI.DisabledScope(!EditorUtility.IsPersistent(template)))
                if (GUILayout.Button(new GUIContent("저장", "이 템플릿만 디스크에 저장합니다(다른 에셋은 저장하지 않음)."), EditorStyles.toolbarButton)) Save();
        }
    }
    /// <summary>이 템플릿만 저장한다(회귀 도구도 이 경로를 쓴다).</summary>
    public void Save()
    {
        var template = Template;
        if (template == null || !EditorUtility.IsPersistent(template)) return;
        ApplySerialized();
        AssetDatabase.SaveAssetIfDirty(template);
        MarkRowDirty(template);
    }
    private void SwitchLayer(int index)
    {
        ApplySerialized();
        SelectLayer(index);
        if (index == 0) { EditTime = 0f; }
        else TerrainSessionOn = false;
        ForgetSerialized();
    }

    protected override void EmptyStateGui()
    {
        EditorGUILayout.HelpBox("왼쪽 목록에서 템플릿을 고르거나 '새 템플릿'을 누르세요. 원본은 Assets/Data/Dungeon/Templates/<테마>/*.asset이고, 이 창에서 고친 값이 실행에 쓰입니다(사양 JSON은 보관본).", MessageType.Info);
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(260f, PaletteColumnWidth))))
            DrawTemplateList(Mathf.Max(200f, position.height - 120f));
    }

    // ───────────────────────────── 왼쪽: 템플릿 목록 + 팔레트 ─────────────────────────────
    protected override void DrawLeftColumn()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(PaletteColumnWidth)))
        {
            DrawTemplateList(listHeight);
            ResizeList();
            DrawPalettePane();
        }
    }
    private void ResizeList()
    {
        var rect = GUILayoutUtility.GetRect(40, 6, GUILayout.Height(6), GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(.12f, .15f, .19f));
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);
        int id = GUIUtility.GetControlID(736, FocusType.Passive);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
        if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id) { listHeight = Mathf.Clamp(listHeight + e.delta.y, 120f, position.height * .6f); e.Use(); Repaint(); }
        if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
    }
    /// <summary>템플릿 목록(테마 폴더 → 이름순). 검사 결과 수는 화면을 그릴 때마다 한 개씩 채운다(처음 열 때 멈추지 않게).</summary>
    private void DrawTemplateList(float height)
    {
        EnsureRows();
        rowStyle ??= new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleLeft, wordWrap = true, richText = true, fixedHeight = 0, padding = new RectOffset(6, 4, 3, 3) };
        rowSelectedStyle ??= new GUIStyle(rowStyle) { fontStyle = FontStyle.Bold };
        GUILayout.Label(new GUIContent($"템플릿 목록 · {rows.Count}종", $"{templateRoot}/<테마>/ 아래 DungeonRoomTemplateSO. 원본은 이 에셋입니다(사양 JSON은 보관본)."), EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            listSearch = EditorGUILayout.TextField(listSearch, EditorStyles.toolbarSearchField);
            if (GUILayout.Button(new GUIContent("↻", "목록·검사 결과·사용처를 다시 읽습니다."), EditorStyles.miniButton, GUILayout.Width(22))) { rows = null; usage = null; TemplateRevalidate(); GUIUtility.ExitGUI(); }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("새 템플릿", "지금 테마 폴더(없으면 Combat)에 결투장 기준 새 템플릿을 만듭니다."), EditorStyles.miniButton))
            {
                ApplySerialized();
                string folder = Template != null ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(Template))?.Replace('\\', '/') : null;
                if (string.IsNullOrEmpty(folder) || !folder.StartsWith(templateRoot, StringComparison.Ordinal)) folder = templateRoot + "/Combat";
                var created = DungeonTemplateAuthoring.CreateInFolder(folder);
                rows = null; usage = null;
                if (created != null) Select(created);
                GUIUtility.ExitGUI();
            }
            using (new EditorGUI.DisabledScope(Template == null || !EditorUtility.IsPersistent(Template)))
            {
                if (GUILayout.Button(new GUIContent("복제", "같은 폴더에 새 GUID로 복제합니다. 던전 후보 목록에는 Wave 격자 편집기의 던전 설정에서 넣습니다."), EditorStyles.miniButton))
                {
                    ApplySerialized();
                    AssetDatabase.SaveAssetIfDirty(Template);
                    var copy = DungeonTemplateAuthoring.Duplicate(Template);
                    rows = null; usage = null;
                    if (copy != null) Select(copy);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(new GUIContent("이름 바꾸기", "에셋 이름만 바꿉니다(GUID 유지 — 던전 설정의 참조는 끊기지 않습니다)."), EditorStyles.miniButton)) { renameTarget = Template; renameText = Template.name; }
            }
        }
        if (renameTarget != null)
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                renameText = EditorGUILayout.TextField("새 이름", renameText);
                EditorGUILayout.LabelField("이름으로 찾는 곳(CombatDungeonSetup 후보 목록·NoCrane 예외 목록)이 있으면 함께 고칩니다.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("바꾸기", EditorStyles.miniButton))
                    {
                        ApplySerialized();
                        AssetDatabase.SaveAssetIfDirty(renameTarget);
                        string error = DungeonTemplateAuthoring.Rename(renameTarget, renameText);
                        if (error != null) Notify("이름을 바꾸지 못했습니다: " + error);
                        renameTarget = null; rows = null; usage = null;
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button("취소", EditorStyles.miniButton)) renameTarget = null;
                }
            }
        listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.Height(height));
        string theme = null;
        foreach (var row in rows)
        {
            if (row.Template == null) continue;
            if (!Matches(row)) continue;
            if (row.Theme != theme)
            {
                theme = row.Theme;
                GUILayout.Label(string.IsNullOrEmpty(theme) ? "(테마 폴더 없음)" : $"테마 · {theme}", EditorStyles.miniBoldLabel);
            }
            bool current = row.Template == Template;
            string counts = row.Errors < 0 ? "검사 중…" : row.Errors > 0 ? $"<color=#ff6b5e>오류 {row.Errors}</color> · 경고 {row.Warnings}" : row.Warnings > 0 ? $"<color=#ffc15e>경고 {row.Warnings}</color>" : "<color=#7be38f>통과</color>";
            int used = usage != null && usage.TryGetValue(row.Template, out var users) ? users.Count : 0;
            string tooltip = $"{row.Path}\n{DungeonTemplateMeta.Summary(row.Template)}" + (string.IsNullOrWhiteSpace(row.Template.concept) ? "" : "\n컨셉: " + row.Template.concept.Trim())
                + (used > 0 ? "\n사용처: " + string.Join(", ", usage[row.Template]) : "\n사용처 없음(던전 WaveData의 방 템플릿 후보에 없음)");
            string text = $"{(current ? "▶ " : "")}{row.Template.name}\n{DungeonShapes.DisplayName(row.Template.shape)} · {row.Size} · {DungeonTemplateMeta.TagsName(row.Template.tags)} · {counts} · 사용 {used}";
            if (GUILayout.Button(new GUIContent(text, tooltip), current ? rowSelectedStyle : rowStyle) && !current)
            {
                ApplySerialized();
                Select(row.Template);
                GUIUtility.ExitGUI();
            }
        }
        EditorGUILayout.EndScrollView();
    }
    private bool Matches(Row row)
    {
        if (string.IsNullOrWhiteSpace(listSearch)) return true;
        string query = listSearch.Trim();
        var t = row.Template;
        return t.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || (t.concept ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || DungeonTemplateMeta.TagsName(t.tags).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || DungeonShapes.DisplayName(t.shape).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || (row.Theme ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
    /// <summary>회귀 도구용: 지금 목록의 템플릿(테마 → 이름순).</summary>
    public List<DungeonRoomTemplateSO> ListedTemplates() { EnsureRows(); return rows.Select(r => r.Template).Where(t => t != null).ToList(); }
    /// <summary>회귀 도구용: 목록 행의 검사 결과 수(바로 잰다).</summary>
    public (int errors, int warnings) RowCounts(DungeonRoomTemplateSO template)
    {
        EnsureRows();
        var row = rows.FirstOrDefault(r => r.Template == template);
        if (row == null) return (-1, -1);
        Measure(row);
        return (row.Errors, row.Warnings);
    }
    /// <summary>회귀 도구용: template을 후보로 쓰는 던전 WaveData 이름.</summary>
    public List<string> UsageOf(DungeonRoomTemplateSO template) { EnsureUsage(); return usage.TryGetValue(template, out var list) ? list : new List<string>(); }

    private void EnsureRows()
    {
        if (rows == null)
        {
            rows = new List<Row>();
            if (AssetDatabase.IsValidFolder(templateRoot))
                foreach (string guid in AssetDatabase.FindAssets("t:DungeonRoomTemplateSO", new[] { templateRoot }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var template = AssetDatabase.LoadAssetAtPath<DungeonRoomTemplateSO>(path);
                    if (template == null) continue;
                    string rest = path.Length > templateRoot.Length + 1 ? path.Substring(templateRoot.Length + 1) : path;
                    int slash = rest.IndexOf('/');
                    rows.Add(new Row { Template = template, Path = path, Theme = slash > 0 ? rest.Substring(0, slash) : "" });
                }
            rows.Sort((a, b) => a.Theme != b.Theme ? string.CompareOrdinal(a.Theme, b.Theme) : string.CompareOrdinal(a.Template.name, b.Template.name));
            EnsureUsage();
        }
        // 검사 수는 한 번에 하나씩 채운다(Layout 이벤트에서만 — 다시 그리기를 부른다).
        if (Event.current != null && Event.current.type == EventType.Layout)
        {
            var pending = rows.FirstOrDefault(r => r.Template != null && r.Dirty != EditorUtility.GetDirtyCount(r.Template));
            if (pending != null) { Measure(pending); Repaint(); }
        }
    }
    private void Measure(Row row)
    {
        if (row?.Template == null) return;
        var issues = TemplateIssues(row.Template);
        row.Errors = issues.Count(i => i.Error);
        row.Warnings = issues.Count - row.Errors;
        row.Dirty = EditorUtility.GetDirtyCount(row.Template);
        row.Size = DungeonTemplateValidation.TryMeasure(row.Template, out _, out var interior) ? $"{interior.width}×{interior.height}" : "실내 ?";
    }
    private void MarkRowDirty(DungeonRoomTemplateSO template)
    {
        var row = rows?.FirstOrDefault(r => r.Template == template);
        if (row != null) row.Dirty = -1;
    }
    // 사용처: 던전 설정(templates·bossRoom·startTemplates)에 이 템플릿을 넣은 WaveData와 Wave 번호.
    private void EnsureUsage()
    {
        if (usage != null) return;
        usage = new Dictionary<DungeonRoomTemplateSO, List<string>>();
        foreach (string guid in AssetDatabase.FindAssets("t:WaveDataSO", new[] { "Assets/Data" }))
        {
            var data = AssetDatabase.LoadAssetAtPath<WaveDataSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null || data is DungeonRoomTemplateSO || data.waves == null) continue;
            for (int w = 0; w < data.waves.Length; w++)
            {
                var settings = data.waves[w]?.dungeon;
                if (data.waves[w] == null || data.waves[w].flowMode != WaveFlowMode.Dungeon || settings == null) continue;
                string label = $"{data.name} W{w + 1}";
                void Add(DungeonRoomTemplateSO t)
                {
                    if (t == null) return;
                    if (!usage.TryGetValue(t, out var list)) usage[t] = list = new List<string>();
                    if (!list.Contains(label)) list.Add(label);
                }
                foreach (var entry in settings.templates ?? Array.Empty<DungeonTemplateEntry>()) Add(entry?.template);
                if (settings.includeBossRoom) Add(settings.bossRoom);
                foreach (var start in settings.startTemplates ?? new List<DungeonRoomTemplateSO>()) Add(start);
            }
        }
    }

    // ───────────────────────────── 가운데: 격자 툴바 ─────────────────────────────
    protected override void GridToolbarGui(float middleWidth)
    {
        if (!(EditData is DungeonRoomTemplateSO)) { DrawStandardGridToolbar(middleWidth); return; }
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            using (new EditorGUI.DisabledScope(!MapLayer))
            {
                bool paint = GUILayout.Toggle(TerrainSessionOn && MapLayer, new GUIContent("지형 칠하기", "맵 층의 처음 지형(벽·바닥·OneWay)을 셀 브러시로 칠합니다. 지형 잠금을 꺼야 칠해집니다."), EditorStyles.toolbarButton);
                if (paint != TerrainSessionOn) TerrainSessionOn = paint;
                var previous = GUI.backgroundColor;
                if (terrainLock) GUI.backgroundColor = new Color(1f, .8f, .4f);
                bool locked = GUILayout.Toggle(terrainLock, new GUIContent(terrainLock ? "지형 잠금 켬" : "지형 잠금 끔", "실수로 지형을 바꾸지 않게 막습니다(기본 켬). 끄면 맵 층의 처음 지형을 칠·지울 수 있습니다."), EditorStyles.toolbarButton);
                GUI.backgroundColor = previous;
                if (locked != terrainLock) terrainLock = locked;
                bool entry = GUILayout.Toggle(EditingPlayerStart, new GUIContent("입장 기준점 이동", "격자를 누른 자리로 맵 층 시작 좌표(입장 기준점)를 옮깁니다(모든 층에 같은 값)."), EditorStyles.toolbarButton);
                if (entry != EditingPlayerStart) SetPointEdit(entry ? 1 : 0);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("전체 보기", EditorStyles.toolbarButton)) FitView();
        }
        if (!MapLayer) EditorGUILayout.HelpBox("적 구성 층: 지형·Prop은 맵 층 그대로(흐린 표시)입니다. 적만 놓습니다(6차 T8 — 함정은 맵 층에만). 생성 시각은 방이 잠긴 뒤부터 셉니다.", MessageType.None);
        else if (TerrainSessionOn) EditorGUILayout.HelpBox(terrainLock ? "지형 잠금이 켜져 있어 칠하지 않습니다. '지형 잠금 켬'을 눌러 끈 뒤 칠하세요." : "맵 층의 처음 지형을 칠합니다(배치 모드 = 칠하기, 지우기 모드 = 지우기). 시간별 예약은 만들지 않습니다.", terrainLock ? MessageType.Warning : MessageType.Info);
    }

    // ───────────────────────────── 오른쪽: 메타 → 배치 상세 → 층 정보 → 검사 결과 ─────────────────────────────
    protected override void DrawDetailsColumn()
    {
        var template = Template;
        if (template == null) { base.DrawDetailsColumn(); return; }
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(DetailsColumnWidth)))
        {
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            showMeta = EditorGUILayout.Foldout(showMeta, "템플릿 메타", true);
            if (showMeta)
            {
                var so = SerializedData;
                so.Update();
                DungeonSyncEncounterInfo(template);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) DungeonTemplateHeaderGui(template);
                ApplySerialized();
            }
            GUILayout.Space(4);
            SerializedData.Update();
            DrawObjectPane();
            showLayer = EditorGUILayout.Foldout(showLayer, MapLayer ? "맵 층 · 입장 기준점 · 적 구성 목록" : "적 구성 정보", true);
            if (showLayer)
            {
                SerializedData.Update();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (MapLayer) EntryPointGui(template);
                    DungeonLayerInfoGui(template);
                    EncounterListGui(template);
                    DungeonEncounterButtonsGui();
                }
                ApplySerialized();
            }
            showIssues = EditorGUILayout.Foldout(showIssues, "검사 결과", true);
            if (showIssues)
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) DungeonTemplateIssuesGui(template, 120);
            EditorGUILayout.EndScrollView();
        }
    }
    private void EntryPointGui(DungeonRoomTemplateSO template)
    {
        var start = WaveRoomState.PlayerStartAt(template, 0);
        EditorGUI.BeginChangeCheck();
        var next = EditorGUILayout.Vector2Field(new GUIContent("입장 기준점", "맵 층 시작 좌표(발 가운데 격자점 + 1칸). 실내 닫힘·도달성 검사의 시작점이고 실행 때 방 입장 기준점입니다. 모든 층에 같은 값을 둡니다."), start);
        if (EditorGUI.EndChangeCheck())
        {
            var snapped = new Vector2(Mathf.Round(next.x), Mathf.Round(next.y));
            ApplySerialized();
            MutateData(() =>
            {
                foreach (var wave in template.waves ?? Array.Empty<Wave>())
                {
                    if (wave == null) continue;
                    wave.overridePlayerStart = true;
                    wave.playerStart = snapped;
                }
            });
        }
    }
    private void EncounterListGui(DungeonRoomTemplateSO template)
    {
        if (template.EncounterCount == 0) { EditorGUILayout.LabelField("손 배치 적 구성 없음(동적 구성 전용 — W1 전투 방)", EditorStyles.miniLabel); return; }
        GUILayout.Label("적 구성", EditorStyles.miniBoldLabel);
        for (int number = 1; number <= template.EncounterCount; number++)
        {
            var info = template.EncounterInfo(number);
            var wave = template.EncounterWave(number);
            int enemies = wave?.spawnObjects?.Count(s => s?.prefab != null && s.prefab.GetComponent<EnemyController>() != null) ?? 0;
            using (new EditorGUILayout.HorizontalScope())
            {
                bool current = EditWaveIndex == number;
                GUILayout.Label($"{(current ? "▶ " : "")}{number}. {(info != null ? info.name : "이름 없음")} · 난이도 {(info != null ? info.tier : 1)} · 가중치 {(info != null ? info.weight : 1f):0.##} · 적 {enemies}", EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(current))
                    if (GUILayout.Button("열기", EditorStyles.miniButton, GUILayout.Width(36))) { SwitchLayer(number); GUIUtility.ExitGUI(); }
            }
        }
        if (!MapLayer && GUILayout.Button("맵 층으로", EditorStyles.miniButton)) { SwitchLayer(0); GUIUtility.ExitGUI(); }
    }
    /// <summary>회귀 도구용: 검사 결과 하나를 눌렀을 때와 같이 그 배치·칸으로 옮긴다.</summary>
    public void FocusIssue(DungeonTemplateIssue issue) => DungeonFocusIssue(issue);
    /// <summary>회귀 도구용: 층을 바꾼다(툴바와 같은 경로).</summary>
    public void OpenLayer(int index) => SwitchLayer(index);
}
