#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>모든 Enemy 프리팹의 실제 설정 SO와 실행 중 패턴 상태를 한곳에서 확인한다.</summary>
public sealed partial class EnemyBalanceWindow : EditorWindow
{
    private sealed class EnemyEntry
    {
        public string Path;
        public EnemyController Prefab;
        public EnemyHealth Health;
        public AiBrain Brain;
        public AiAbility[] Abilities;
        public EnemyShieldSystem Shield;
        public AiPatternProfileSO Profile;
    }

    private readonly List<EnemyEntry> enemies = new List<EnemyEntry>();
    private readonly Dictionary<int, SerializedObject> serialized = new Dictionary<int, SerializedObject>();
    private readonly Dictionary<int, List<string>> assetUsers = new Dictionary<int, List<string>>();
    private readonly HashSet<string> expanded = new HashSet<string>();
    [SerializeField] private List<ScriptableObject> editedAssets = new List<ScriptableObject>();
    private readonly Dictionary<string, ScriptableObject> pendingReferences = new Dictionary<string, ScriptableObject>();
    private Vector2 listScroll;
    private Vector2 detailScroll;
    private string search = string.Empty;
    private string selectedPath;
    private EnemyController runtimeEnemy;
    [SerializeField] private int selectedPatternIndex;
    // 페이즈를 나눈 Enemy에서 보고 있는 페이즈. 이때 selectedPatternIndex는 그 페이즈의 등록 순번이다.
    [SerializeField] private int selectedPhaseIndex;
    [SerializeField] private bool showVitals;
    [SerializeField] private bool showProfile;
    private GUIStyle cardTitleStyle;
    private GUIStyle cardValueStyle;
    private GUIStyle enemyNameStyle;
    private GUIStyle badgeStyle;
    private static readonly string[] CommonPatternFields = {
        "baseWeight", "weightChangeOnUse", "weightRecoveryPerSecond", "cooldown"
    };

    [MenuItem("Tools/Enemy/밸런스 및 AI 설정")]
    public static void Open()
    {
        var window = GetWindow<EnemyBalanceWindow>("Enemy 밸런스");
        window.minSize = new Vector2(920, 560);
        window.Show();
    }

    private void OnEnable()
    {
        Refresh();
        Undo.undoRedoPerformed += Repaint;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= Repaint;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        ClearSerialized();
        EnemyBalanceVisuals.Clear();
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        ClearSerialized();
        Repaint();
    }

    private void OnInspectorUpdate()
    {
        Repaint();
    }

    private void ClearSerialized()
    {
        foreach (var item in serialized.Values) item.Dispose();
        serialized.Clear();
    }

    private SerializedObject Serialized(UnityEngine.Object target)
    {
        int id = target.GetInstanceID();
        if (!serialized.TryGetValue(id, out var result))
        {
            result = new SerializedObject(target);
            serialized.Add(id, result);
        }
        result.UpdateIfRequiredOrScript();
        return result;
    }

    private static ScriptableObject ReadAsset(UnityEngine.Object target, string field)
    {
        if (target == null) return null;
        using (var value = new SerializedObject(target))
            return value.FindProperty(field)?.objectReferenceValue as ScriptableObject;
    }

    private void Refresh()
    {
        ClearSerialized();
        enemies.Clear();
        assetUsers.Clear();
        pendingReferences.Clear();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var enemy = root != null ? root.GetComponent<EnemyController>() : null;
            if (enemy == null) continue;
            var entry = new EnemyEntry
            {
                Path = path,
                Prefab = enemy,
                Health = enemy.Health != null ? enemy.Health : root.GetComponentInChildren<EnemyHealth>(true),
                Brain = root.GetComponent<AiBrain>(),
                Abilities = root.GetComponentsInChildren<AiAbility>(true)
            };
            entry.Shield = entry.Health != null ? entry.Health.enemyShieldSystem : null;
            enemies.Add(entry);
            RegisterUser(ReadAsset(entry.Health, "healthData"), enemy.name + " / 체력");
            RegisterUser(ReadAsset(entry.Shield, "data"), enemy.name + " / 쉴드");
            for (int i = 0; i < entry.Abilities.Length; i++)
            {
                var ability = entry.Abilities[i];
                RegisterUser(ReadAsset(ability, "data"), enemy.name + " / 컴포넌트 #" + (i + 1) + " " + ability.GetType().Name);
            }
            var director = root.GetComponent<AiPatternDirector>();
            var profile = ReadAsset(director, "profile");
            entry.Profile = profile as AiPatternProfileSO;
            RegisterUser(profile, enemy.name + " / Profile");
            RegisterRuleUsers(profile, enemy.name, new HashSet<int>());
        }
        enemies.Sort((a, b) => string.Compare(a.Prefab.name, b.Prefab.name, StringComparison.Ordinal));
        if (enemies.All(e => e.Path != selectedPath)) selectedPath = enemies.FirstOrDefault()?.Path;
        Repaint();
    }

    private void RegisterRuleUsers(ScriptableObject asset, string enemyName, HashSet<int> visited)
    {
        if (asset == null || !visited.Add(asset.GetInstanceID())) return;
        using (var settings = new SerializedObject(asset))
        {
            var property = settings.GetIterator();
            while (property.NextVisible(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                var child = property.objectReferenceValue as ScriptableObject;
                if (!(child is AiPatternRuleSO) && !(child is AiPatternScoreRuleSO)) continue;
                RegisterUser(child, enemyName + " / 규칙 / " + child.name);
                RegisterRuleUsers(child, enemyName, visited);
            }
        }
    }

    private void RegisterUser(ScriptableObject asset, string user)
    {
        if (asset == null) return;
        int id = asset.GetInstanceID();
        if (!assetUsers.TryGetValue(id, out var users))
        {
            users = new List<string>();
            assetUsers.Add(id, users);
        }
        if (!users.Contains(user)) users.Add(user);
    }

    private void OnGUI()
    {
        EnsureStyles();
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label($"Enemy {enemies.Count}종", GUILayout.Width(110));
            search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
            if (GUILayout.Button("목록 갱신", EditorStyles.toolbarButton, GUILayout.Width(80))) Refresh();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("설정 저장", EditorStyles.toolbarButton, GUILayout.Width(70))) SaveEditedAssets();
        }
        EditorGUILayout.HelpBox("몸통 접촉 피해와 패턴별 AI·공격·총알 피해를 조절하세요. 변경 후 설정 저장을 누릅니다.", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawList();
            using (new EditorGUILayout.VerticalScope())
            {
                detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                var entry = enemies.FirstOrDefault(e => e.Path == selectedPath);
                if (entry != null) DrawEnemy(entry);
                else EditorGUILayout.HelpBox("EnemyController가 있는 프리팹을 찾지 못했습니다.", MessageType.Info);
                EditorGUILayout.EndScrollView();
            }
        }
    }

    private void SaveEditedAssets()
    {
        SaveContactPrefabs();
        foreach (var asset in editedAssets)
            if (asset != null && EditorUtility.IsDirty(asset)) AssetDatabase.SaveAssetIfDirty(asset);
    }

    private void EnsureStyles()
    {
        if (cardTitleStyle != null) return;
        cardTitleStyle = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true, fontSize = 12 };
        cardValueStyle = new GUIStyle(EditorStyles.label) { fontSize = 11 };
        enemyNameStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
        badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
        badgeStyle.normal.textColor = Color.black;
    }

    private static string EnemyListLabel(EnemyEntry entry)
    {
        var data = ReadAsset(entry.Health, "healthData");
        float hp = data != null ? ReadFloat(data, "maxHP") : entry.Health != null ? entry.Health.maxHP : 0f;
        float shield = IsShieldActive(entry) ? ReadFloat(ReadAsset(entry.Shield, "data"), "maxShield") : 0f;
        return $"체력 {hp:0.##}   쉴드 {shield:0.##}";
    }

    private void DrawList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(270)))
        {
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            foreach (var entry in enemies)
            {
                if (!string.IsNullOrWhiteSpace(search) && entry.Prefab.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                Rect row = GUILayoutUtility.GetRect(0, 78, GUILayout.ExpandWidth(true));
                bool selected = entry.Path == selectedPath;
                var previous = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = new Color(.4f, .72f, 1f);
                bool clicked = GUI.Button(row, GUIContent.none);
                GUI.backgroundColor = previous;
                DrawPreview(new Rect(row.x + 7, row.y + 7, 62, 62), entry.Prefab);
                GUI.Label(new Rect(row.x + 77, row.y + 9, row.width - 83, 38),
                    new GUIContent(entry.Prefab.name.Replace("_", "\n"), entry.Prefab.name), cardTitleStyle);
                GUI.Label(new Rect(row.x + 77, row.y + 49, row.width - 83, 21), EnemyListLabel(entry), cardValueStyle);
                if (clicked && !selected)
                {
                    selectedPath = entry.Path;
                    selectedPatternIndex = 0;
                    selectedPhaseIndex = 0;
                    detailScroll = Vector2.zero;
                }
                GUILayout.Space(4);
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private static void DrawPreview(Rect rect, EnemyController prefab)
    {
        EditorGUI.DrawRect(rect, new Color(.08f, .09f, .12f, 1f));
        var preview = EnemyBalanceVisuals.GetEnemyPreview(prefab);
        if (preview != null) GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, true);
        else GUI.Label(rect, "준비 중", EditorStyles.centeredGreyMiniLabel);
    }

    private static bool IsShieldActive(EnemyEntry entry)
    {
        if (entry.Shield == null || !entry.Shield.enabled) return false;
        for (Transform part = entry.Shield.transform; part != null; part = part.parent)
            if (!part.gameObject.activeSelf) return false;
        return true;
    }

    private static float ReadFloat(UnityEngine.Object asset, string field)
    {
        if (asset == null) return 0;
        using (var value = new SerializedObject(asset)) return value.FindProperty(field)?.floatValue ?? 0;
    }

    private void DrawEnemy(EnemyEntry entry)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            Rect preview = GUILayoutUtility.GetRect(128, 128, GUILayout.Width(128));
            DrawPreview(preview, entry.Prefab);
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Space(12);
                EditorGUILayout.LabelField(entry.Prefab.name, enemyNameStyle);
                EditorGUILayout.LabelField(EnemyListLabel(entry));
                GUILayout.Space(8);
                DrawReference(entry.Prefab, "Enemy Prefab");
            }
        }
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            showVitals = EditorGUILayout.Foldout(showVitals, "체력 · 쉴드 설정", true);
            if (showVitals) DrawVitals(entry);
            DrawContactDamage(entry);
        }
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("패턴 선택", EditorStyles.boldLabel);
        // 페이즈를 나눈 Enemy는 페이즈마다 등록한 패턴과 그 페이즈의 선택 수치를 보여 준다.
        bool phased = DrawPhasedPatterns(entry);
        if (!phased) DrawPatternCards(entry);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (!phased) DrawSelectedPattern(entry);
            EditorGUILayout.Space(8);
            showProfile = EditorGUILayout.Foldout(showProfile, "PatternProfile · 예외 조건", true);
            if (showProfile) DrawProfile(entry);
        }
        if (EditorApplication.isPlaying)
        {
            EditorGUILayout.Space();
            DrawRuntime();
        }
    }

    private void DrawVitals(EnemyEntry entry)
    {
        var health = ReadAsset(entry.Health, "healthData");
        DrawConnection(entry, entry.Health, "healthData", typeof(EnemyHealthDataSO), "체력 SO 연결");
        if (health != null) DrawAsset(health, "체력", false);
        else if (entry.Health != null && GUILayout.Button("현재 체력으로 SO 생성 및 연결")) CreateHealth(entry);
        if (entry.Health is TrainingDummyHealth)
            EditorGUILayout.HelpBox("자율훈련에서 선택한 쉴드 용량은 인스턴스에 따로 적용됩니다.", MessageType.Info);
        if (entry.Shield == null) { EditorGUILayout.LabelField("쉴드", "없음"); return; }
        if (!IsShieldActive(entry)) EditorGUILayout.HelpBox("비활성 쉴드의 설정입니다. 현재 쉴드량에는 적용되지 않습니다.", MessageType.Info);
        DrawConnection(entry, entry.Shield, "data", typeof(EnemyShieldDataSO), "쉴드 SO 연결");
        DrawAsset(ReadAsset(entry.Shield, "data"), "쉴드 · 회복 · Break", true);
        EditorGUILayout.HelpBox("체력·쉴드 SO는 Enemy마다 독립적으로 연결됩니다. 다른 Enemy의 SO를 고르면 값을 복제한 뒤 연결합니다.", MessageType.None);
    }

    private static string PatternName(AiAbility ability)
    {
        if (ability == null) return "연결 없음";
        string key = ability.AbilityKey;
        string name = string.IsNullOrWhiteSpace(key) ? ability.GetType().Name.Replace("Ability", "")
            : key.Substring(key.LastIndexOf('.') + 1);
        return ObjectNames.NicifyVariableName(name.Replace('_', ' '));
    }

    private static string[] PatternSummary(AiAbility ability)
    {
        var data = ability != null ? ability.Data : null;
        if (data == null) return new[] { "SO 연결 없음" };
        return new[] {
            $"가중치 {data.baseWeight:0.##}   사용 {data.weightChangeOnUse:+0.##;-0.##;0}",
            $"회복 {data.weightRecoveryPerSecond:0.##}/초   쿨타임 {data.cooldown:0.##}초"
        };
    }

    private void SelectPattern(EnemyEntry entry, int index)
    {
        selectedPatternIndex = Mathf.Clamp(index, 0, Mathf.Max(0, entry.Abilities.Length - 1));
        Repaint();
    }

    private AiAbility SelectedPattern(EnemyEntry entry)
    {
        if (entry.Abilities.Length == 0) return null;
        selectedPatternIndex = Mathf.Clamp(selectedPatternIndex, 0, entry.Abilities.Length - 1);
        return entry.Abilities[selectedPatternIndex];
    }

    private void DrawPatternCards(EnemyEntry entry)
    {
        if (entry.Abilities.Length == 0)
        {
            EditorGUILayout.HelpBox("AI 패턴이 없는 Enemy입니다.", MessageType.Info);
            return;
        }
        int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 310f) / 268f));
        for (int first = 0; first < entry.Abilities.Length; first += columns)
        {
            Rect row = GUILayoutUtility.GetRect(0, 116, GUILayout.ExpandWidth(true));
            float width = (row.width - (columns - 1) * 7f) / columns;
            for (int column = 0; column < columns && first + column < entry.Abilities.Length; column++)
            {
                int index = first + column;
                var ability = entry.Abilities[index];
                var rect = new Rect(row.x + column * (width + 7), row.y, width, row.height);
                if (DrawCard(rect, index == selectedPatternIndex, ability, PatternName(ability), ability.AbilityKey, PatternSummary(ability), null))
                    SelectPattern(entry, index);
            }
            GUILayout.Space(7);
        }
    }

    // 패턴 카드 하나를 그리고 눌렸는지 돌려준다. badge는 카드 오른쪽의 작은 표식이다.
    private bool DrawCard(Rect rect, bool selected, AiAbility ability, string title, string tooltip, string[] summary, string badge)
    {
        var previous = GUI.backgroundColor;
        if (selected) GUI.backgroundColor = new Color(.4f, .72f, 1f);
        bool clicked = GUI.Button(rect, new GUIContent("", tooltip));
        GUI.backgroundColor = previous;
        if (selected) EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height), new Color(.25f, .65f, 1f));
        var icon = EnemyBalanceVisuals.GetPatternIcon(ability);
        if (icon != null) GUI.DrawTexture(new Rect(rect.x + 10, rect.y + 14, 54, 54), icon, ScaleMode.ScaleToFit, true);
        GUI.Label(new Rect(rect.x + 73, rect.y + 10, rect.width - 82, 39), title, cardTitleStyle);
        for (int line = 0; line < summary.Length; line++)
            GUI.Label(new Rect(rect.x + 12, rect.y + 72 + line * 18, rect.width - 20, 19), summary[line], cardValueStyle);
        if (!string.IsNullOrEmpty(badge))
        {
            float badgeWidth = badgeStyle.CalcSize(new GUIContent(badge)).x + 10f;
            var area = new Rect(rect.xMax - badgeWidth - 8f, rect.y + 50f, badgeWidth, 17f);
            EditorGUI.DrawRect(area, new Color(1f, .6f, .15f, .92f));
            GUI.Label(area, badge, badgeStyle);
        }
        return clicked;
    }

    private void MarkEdited(ScriptableObject asset)
    {
        if (asset != null && !editedAssets.Contains(asset)) editedAssets.Add(asset);
    }

    // AiBrain과 같은 규칙으로 키의 패턴을 찾는다. 같은 키가 여러 개면 AiBrain 목록의 앞쪽이다.
    private static AiAbility FindAbility(EnemyEntry entry, string abilityKey)
    {
        if (string.IsNullOrEmpty(abilityKey)) return null;
        if (entry.Brain != null)
            foreach (var ability in entry.Brain.Abilities)
                if (ability != null && ability.AbilityKey == abilityKey) return ability;
        foreach (var ability in entry.Abilities)
            if (ability != null && ability.AbilityKey == abilityKey) return ability;
        return null;
    }

    private static string PhaseTitle(SerializedProperty phase, int index)
    {
        string name = phase.FindPropertyRelative("displayName").stringValue;
        if (string.IsNullOrWhiteSpace(name)) name = (index + 1) + "페이즈";
        string condition = index == 0 ? "스폰부터"
            : "체력 " + (phase.FindPropertyRelative("enterBelowHealthRatio").floatValue * 100f).ToString("0.#") + "% 미만";
        return name + " · " + condition;
    }

    private static string[] PhaseSummary(SerializedProperty pattern)
    {
        float use = pattern.FindPropertyRelative("weightChangeOnUse").floatValue;
        return new[] {
            $"가중치 {pattern.FindPropertyRelative("baseWeight").floatValue:0.##}   사용 {use:+0.##;-0.##;0}",
            $"회복 {pattern.FindPropertyRelative("weightRecoveryPerSecond").floatValue:0.##}/초   쿨타임 {pattern.FindPropertyRelative("cooldown").floatValue:0.##}초"
        };
    }

    private static bool IsRegistered(SerializedProperty patterns, string abilityKey)
    {
        for (int i = 0; i < patterns.arraySize; i++)
            if (patterns.GetArrayElementAtIndex(i).FindPropertyRelative("abilityKey").stringValue == abilityKey) return true;
        return false;
    }

    // 패턴 SO의 공통 수치를 페이즈 등록의 초기값으로 옮긴다. 이후에는 페이즈마다 따로 조절한다.
    private static void WritePattern(SerializedProperty pattern, AiAbility ability, bool resetEntryFlag)
    {
        var data = ability.Data;
        pattern.FindPropertyRelative("abilityKey").stringValue = ability.AbilityKey;
        if (resetEntryFlag) pattern.FindPropertyRelative("useOnPhaseEnter").boolValue = false;
        pattern.FindPropertyRelative("baseWeight").floatValue = data.baseWeight;
        pattern.FindPropertyRelative("weightChangeOnUse").floatValue = data.weightChangeOnUse;
        pattern.FindPropertyRelative("weightRecoveryPerSecond").floatValue = data.weightRecoveryPerSecond;
        pattern.FindPropertyRelative("cooldown").floatValue = data.cooldown;
    }

    private static bool CanRegister(AiAbility ability)
        => ability != null && ability.Data != null && !string.IsNullOrWhiteSpace(ability.AbilityKey);

    /// <summary>페이즈 탭·진입 조건·페이즈별 패턴 카드·선택한 패턴의 페이즈 수치를 그린다. 페이즈를 쓰지 않으면 false다.</summary>
    private bool DrawPhasedPatterns(EnemyEntry entry)
    {
        bool locked = EditorApplication.isPlayingOrWillChangePlaymode;
        if (entry.Profile == null || !entry.Profile.UsesPhases)
        {
            if (entry.Abilities.Length == 0) return false;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("페이즈 구분 없음 · 모든 패턴을 처음부터 같은 수치로 고릅니다.", EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(locked))
                    if (GUILayout.Button(new GUIContent("페이즈 나누기", "지금 패턴과 SO의 공통 수치를 1페이즈로 등록합니다. 이후 체력 비율로 들어가는 페이즈를 추가할 수 있습니다."), GUILayout.Width(110)))
                        BeginPhases(entry);
            }
            return false;
        }
        var settings = Serialized(entry.Profile);
        var phases = settings.FindProperty("phases");
        selectedPhaseIndex = Mathf.Clamp(selectedPhaseIndex, 0, phases.arraySize - 1);
        using (new EditorGUILayout.HorizontalScope())
        {
            for (int i = 0; i < phases.arraySize; i++)
            {
                bool selected = i == selectedPhaseIndex;
                var previous = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = new Color(.4f, .72f, 1f);
                bool pressed = GUILayout.Toggle(selected, PhaseTitle(phases.GetArrayElementAtIndex(i), i), EditorStyles.miniButton,
                    GUILayout.Height(24), GUILayout.MinWidth(150));
                GUI.backgroundColor = previous;
                if (pressed && !selected)
                {
                    selectedPhaseIndex = i;
                    selectedPatternIndex = 0;
                    GUI.FocusControl(null);
                }
            }
            using (new EditorGUI.DisabledScope(locked))
                if (GUILayout.Button("+ 페이즈 추가", EditorStyles.miniButton, GUILayout.Height(24), GUILayout.Width(100)))
                    AddPhase(entry, phases);
            GUILayout.FlexibleSpace();
        }
        var phase = phases.GetArrayElementAtIndex(selectedPhaseIndex);
        using (new EditorGUI.DisabledScope(locked))
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawSharedWarning(entry.Profile);
            EditorGUILayout.PropertyField(phase.FindPropertyRelative("displayName"), new GUIContent("페이즈 이름"));
            if (selectedPhaseIndex == 0) EditorGUILayout.LabelField("진입 조건", "스폰부터 적용");
            else
            {
                var ratio = phase.FindPropertyRelative("enterBelowHealthRatio");
                EditorGUI.BeginChangeCheck();
                float percent = EditorGUILayout.Slider(new GUIContent("진입 조건 · 체력 % 미만",
                    "체력 비율이 이 값 미만이 되면 이 페이즈로 들어갑니다. 실행 중인 패턴이 끝난 뒤 넘어가며, 한번 들어온 페이즈는 되돌아가지 않습니다."),
                    ratio.floatValue * 100f, 0f, 100f);
                if (EditorGUI.EndChangeCheck()) ratio.floatValue = Mathf.Round(percent * 10f) / 1000f;
                if (selectedPhaseIndex > 1 && ratio.floatValue >= phases.GetArrayElementAtIndex(selectedPhaseIndex - 1)
                    .FindPropertyRelative("enterBelowHealthRatio").floatValue)
                    EditorGUILayout.HelpBox("앞 페이즈보다 낮은 체력 비율이어야 차례로 지나갑니다. 지금 값이면 앞 페이즈를 건너뜁니다.", MessageType.Warning);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(phases.arraySize == 1 ? "페이즈 구분 해제" : "이 페이즈 삭제", GUILayout.Width(120)))
                    RemovePhase(entry, phases, selectedPhaseIndex);
            }
        }
        DrawPhaseCards(entry, phase);
        using (new EditorGUI.DisabledScope(locked)) DrawSelectedPhasePattern(entry, phase, selectedPhaseIndex);
        if (settings.ApplyModifiedProperties()) MarkEdited(entry.Profile);
        return true;
    }

    private void DrawPhaseCards(EnemyEntry entry, SerializedProperty phase)
    {
        var patterns = phase.FindPropertyRelative("patterns");
        if (patterns.arraySize == 0)
            EditorGUILayout.HelpBox("이 페이즈에 등록한 패턴이 없습니다. 아래에서 패턴을 등록하세요.", MessageType.Info);
        selectedPatternIndex = Mathf.Clamp(selectedPatternIndex, 0, Mathf.Max(0, patterns.arraySize - 1));
        int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 310f) / 268f));
        for (int first = 0; first < patterns.arraySize; first += columns)
        {
            Rect row = GUILayoutUtility.GetRect(0, 116, GUILayout.ExpandWidth(true));
            float width = (row.width - (columns - 1) * 7f) / columns;
            for (int column = 0; column < columns && first + column < patterns.arraySize; column++)
            {
                int index = first + column;
                var pattern = patterns.GetArrayElementAtIndex(index);
                string key = pattern.FindPropertyRelative("abilityKey").stringValue;
                var ability = FindAbility(entry, key);
                string badge = pattern.FindPropertyRelative("useOnPhaseEnter").boolValue ? "진입 시 최우선"
                    : ability != null && !ability.AllowWeightedSelection ? "보조 패턴" : null;
                var rect = new Rect(row.x + column * (width + 7), row.y, width, row.height);
                if (DrawCard(rect, index == selectedPatternIndex, ability, ability != null ? PatternName(ability) : "패턴 없음", key,
                    PhaseSummary(pattern), badge))
                {
                    selectedPatternIndex = index;
                    GUI.FocusControl(null);
                    Repaint();
                }
            }
            GUILayout.Space(7);
        }
        var unregistered = entry.Abilities.Where(a => CanRegister(a) && !IsRegistered(patterns, a.AbilityKey))
            .GroupBy(a => a.AbilityKey).Select(group => group.First()).ToList();
        if (unregistered.Count == 0) return;
        EditorGUILayout.LabelField("이 페이즈에 등록하지 않은 패턴 · 점수 선택에서 빠집니다.", EditorStyles.miniBoldLabel);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            const int perRow = 4;
            for (int first = 0; first < unregistered.Count; first += perRow)
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int i = first; i < Mathf.Min(first + perRow, unregistered.Count); i++)
                    {
                        var ability = unregistered[i];
                        if (!GUILayout.Button(new GUIContent("+ " + PatternName(ability), ability.AbilityKey + " 등록"), GUILayout.Width(170))) continue;
                        int index = patterns.arraySize++;
                        WritePattern(patterns.GetArrayElementAtIndex(index), ability, true);
                        patterns.serializedObject.ApplyModifiedProperties();
                        MarkEdited(entry.Profile);
                        selectedPatternIndex = index;
                        GUIUtility.ExitGUI();
                    }
                    GUILayout.FlexibleSpace();
                }
        }
    }

    private void DrawSelectedPhasePattern(EnemyEntry entry, SerializedProperty phase, int phaseIndex)
    {
        var patterns = phase.FindPropertyRelative("patterns");
        if (patterns.arraySize == 0) return;
        var pattern = patterns.GetArrayElementAtIndex(Mathf.Clamp(selectedPatternIndex, 0, patterns.arraySize - 1));
        string key = pattern.FindPropertyRelative("abilityKey").stringValue;
        var ability = FindAbility(entry, key);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("선택한 패턴 · " + (ability != null ? PatternName(ability) : "패턴 없음") + "   |   " + PhaseTitle(phase, phaseIndex), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("패턴 키", key);
            if (ability == null)
                EditorGUILayout.HelpBox("이 키의 패턴이 Prefab에 없습니다. 등록을 해제하거나 패턴 SO의 키를 확인하세요.", MessageType.Warning);
            else
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.Toggle("가중치 자동 선택 대상", ability.AllowWeightedSelection);
                if (!ability.AllowWeightedSelection)
                    EditorGUILayout.HelpBox("다른 패턴이나 전환 규칙이 불러 쓰는 보조 패턴입니다. 점수 경쟁에는 참여하지 않고 아래 수치(쿨타임 등)만 적용됩니다.", MessageType.None);
                if (entry.Brain == null || !entry.Brain.Abilities.Contains(ability))
                    EditorGUILayout.HelpBox("AiBrain에 등록되지 않은 패턴입니다.", MessageType.Warning);
            }
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("이 페이즈의 선택 수치", EditorStyles.miniBoldLabel);
            foreach (string field in CommonPatternFields)
                EditorGUILayout.PropertyField(pattern.FindPropertyRelative(field), new GUIContent(Label(field)));
            EditorGUILayout.PropertyField(pattern.FindPropertyRelative("useOnPhaseEnter"), new GUIContent(Label("useOnPhaseEnter"),
                "이 페이즈에 들어온 뒤 다른 패턴보다 먼저 한 번 사용합니다. 쿨타임을 기다리지 않습니다."));
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(ability == null || ability.Data == null))
                    if (GUILayout.Button(new GUIContent("SO 공통 수치 가져오기", "패턴 SO의 가중치·쿨타임을 이 페이즈 수치로 덮어씁니다."), GUILayout.Width(150)))
                        WritePattern(pattern, ability, false);
                if (GUILayout.Button("이 페이즈에서 등록 해제", GUILayout.Width(150)))
                {
                    patterns.DeleteArrayElementAtIndex(Mathf.Clamp(selectedPatternIndex, 0, patterns.arraySize - 1));
                    patterns.serializedObject.ApplyModifiedProperties();
                    MarkEdited(entry.Profile);
                    selectedPatternIndex = Mathf.Max(0, selectedPatternIndex - 1);
                    GUIUtility.ExitGUI();
                }
            }
            if (ability == null) return;
            EditorGUILayout.Space(4);
            var data = ability.Data;
            DrawConnection(entry, ability, "data", data != null ? data.GetType() : typeof(EnemyAbilityDataSO), "패턴 SO 연결");
            if (data == null) return;
            DrawReference(data, "고유 설정 SO");
            DrawSharedWarning(data);
            DrawPatternDamage(data);
            EditorGUILayout.LabelField("페이즈를 나눈 Enemy는 위 페이즈 수치로 선택합니다. SO의 공통 수치는 페이즈에 등록하지 않은 패턴에만 쓰입니다. 고유 설정은 SO의 선택 버튼으로 Inspector에서 편집합니다.",
                EditorStyles.wordWrappedMiniLabel);
        }
    }

    // 지금 패턴 구성과 SO의 공통 수치를 1페이즈로 옮겨 페이즈 구분을 시작한다. Profile이 없으면 만들어 연결한다.
    private void BeginPhases(EnemyEntry entry)
    {
        var profile = entry.Profile;
        if (profile == null)
        {
            profile = CreateProfileAsset(entry, entry.Prefab.GetComponent<AiPatternDirector>());
            if (profile == null) return;
        }
        using (var settings = new SerializedObject(profile))
        {
            var phases = settings.FindProperty("phases");
            phases.ClearArray();
            phases.arraySize = 1;
            var first = phases.GetArrayElementAtIndex(0);
            first.FindPropertyRelative("displayName").stringValue = "1페이즈";
            first.FindPropertyRelative("enterBelowHealthRatio").floatValue = 1f;
            var patterns = first.FindPropertyRelative("patterns");
            patterns.ClearArray();
            // AiBrain 등록 순서를 따른다. 점수와 사용 이력이 같을 때의 순서는 여전히 AiBrain 목록이 정한다.
            IEnumerable<AiAbility> ordered = entry.Abilities;
            if (entry.Brain != null) ordered = entry.Brain.Abilities.Concat(entry.Abilities);
            foreach (var ability in ordered)
            {
                if (!CanRegister(ability) || IsRegistered(patterns, ability.AbilityKey)) continue;
                WritePattern(patterns.GetArrayElementAtIndex(patterns.arraySize++), ability, true);
            }
            settings.ApplyModifiedProperties();
        }
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssetIfDirty(profile);
        // 바로 저장하지만, Undo로 되돌린 뒤 'SO 저장'을 누르면 되돌린 상태도 저장되도록 편집 목록에 넣는다.
        MarkEdited(profile);
        selectedPhaseIndex = 0;
        selectedPatternIndex = 0;
        Refresh();
        GUIUtility.ExitGUI();
    }

    private void AddPhase(EnemyEntry entry, SerializedProperty phases)
    {
        // 배열을 늘리면 마지막 페이즈가 복제된다. 같은 패턴·수치에서 시작해 이 페이즈만 따로 조절한다.
        int index = phases.arraySize++;
        var phase = phases.GetArrayElementAtIndex(index);
        phase.FindPropertyRelative("displayName").stringValue = (index + 1) + "페이즈";
        float previous = index > 1 ? phases.GetArrayElementAtIndex(index - 1).FindPropertyRelative("enterBelowHealthRatio").floatValue : 1f;
        phase.FindPropertyRelative("enterBelowHealthRatio").floatValue = Mathf.Round(previous * 50f) / 100f;
        var patterns = phase.FindPropertyRelative("patterns");
        for (int i = 0; i < patterns.arraySize; i++)
            patterns.GetArrayElementAtIndex(i).FindPropertyRelative("useOnPhaseEnter").boolValue = false;
        phases.serializedObject.ApplyModifiedProperties();
        MarkEdited(entry.Profile);
        selectedPhaseIndex = index;
        selectedPatternIndex = 0;
        GUIUtility.ExitGUI();
    }

    private void RemovePhase(EnemyEntry entry, SerializedProperty phases, int index)
    {
        string message = phases.arraySize == 1
            ? "페이즈 구분을 해제하면 모든 패턴이 다시 SO의 공통 수치로 선택됩니다."
            : "'" + PhaseTitle(phases.GetArrayElementAtIndex(index), index) + "'의 패턴 등록과 수치를 지웁니다.";
        if (!EditorUtility.DisplayDialog("페이즈 삭제", message + "\nUndo로 되돌릴 수 있습니다.", "삭제", "취소")) return;
        DeletePhase(entry, phases, index);
        GUIUtility.ExitGUI();
    }

    private void DeletePhase(EnemyEntry entry, SerializedProperty phases, int index)
    {
        phases.DeleteArrayElementAtIndex(index);
        phases.serializedObject.ApplyModifiedProperties();
        MarkEdited(entry.Profile);
        selectedPhaseIndex = Mathf.Max(0, index - 1);
        selectedPatternIndex = 0;
    }

    private void DrawSelectedPattern(EnemyEntry entry)
    {
        var ability = SelectedPattern(entry);
        if (ability == null) return;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("선택한 패턴 · " + PatternName(ability), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("패턴 키", ability.AbilityKey);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.Toggle("가중치 자동 선택 대상", ability.AllowWeightedSelection);
            if (entry.Brain == null || !entry.Brain.Abilities.Contains(ability))
                EditorGUILayout.HelpBox("AiBrain에 등록되지 않은 패턴입니다.", MessageType.Warning);
            var data = ability.Data;
            DrawConnection(entry, ability, "data", data != null ? data.GetType() : typeof(EnemyAbilityDataSO), "패턴 SO 연결");
            if (data == null) return;
            DrawReference(data, "고유 설정 SO");
            DrawSharedWarning(data);
            var settings = Serialized(data);
            foreach (string field in CommonPatternFields)
                EditorGUILayout.PropertyField(settings.FindProperty(field), new GUIContent(Label(field)));
            if (settings.ApplyModifiedProperties() && !editedAssets.Contains(data)) editedAssets.Add(data);
            DrawPatternDamage(data);
            EditorGUILayout.LabelField("그 밖의 고유 설정은 위 SO의 선택 버튼으로 Inspector에서 편집합니다.", EditorStyles.wordWrappedMiniLabel);
        }
    }

    private void DrawProfile(EnemyEntry entry)
    {
        var director = entry.Prefab.GetComponent<AiPatternDirector>();
        if (director != null)
        {
            var directorData = Serialized(director);
            var enabled = directorData.FindProperty("enableDirector");
            if (enabled != null && !enabled.boolValue)
                EditorGUILayout.HelpBox("현재 Prefab의 AiPatternDirector가 비활성 상태입니다.", MessageType.Warning);
            DrawConnection(entry, director, "profile", typeof(AiPatternProfileSO), "Profile SO 연결");
            var profile = ReadAsset(director, "profile");
            DrawAsset(profile, "PatternProfile", true);
            if (profile == null && GUILayout.Button("빈 PatternProfile 생성 및 연결")) CreateProfile(entry, director);
        }
        else
        {
            DrawConnection(entry, null, "profile", typeof(AiPatternProfileSO), "Profile SO 연결");
            if (GUILayout.Button("빈 PatternProfile 생성 및 Director 추가")) CreateProfile(entry, null);
        }
    }

    private void DrawReference(UnityEngine.Object asset, string label)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(label, asset, typeof(UnityEngine.Object), false);
            if (GUILayout.Button("찾기", GUILayout.Width(45))) EditorGUIUtility.PingObject(asset);
            if (GUILayout.Button("선택", GUILayout.Width(45))) Selection.activeObject = asset;
        }
        if (asset != null) EditorGUILayout.SelectableLabel(AssetDatabase.GetAssetPath(asset), EditorStyles.miniLabel, GUILayout.Height(18));
    }

    private void DrawConnection(EnemyEntry entry, Component component, string field, Type assetType, string label)
    {
        if (component == null && field != "profile") return;
        var currentAsset = ReadAsset(component, field);
        string key = entry.Path + "/" + (component != null ? component.GetInstanceID() : 0) + "/" + field;
        if (!pendingReferences.TryGetValue(key, out var pending)) pending = currentAsset;
        using (new EditorGUILayout.HorizontalScope())
        {
            var next = EditorGUILayout.ObjectField(label, pending, assetType, false) as ScriptableObject;
            if (next != pending) pendingReferences[key] = next;
            bool valid = next != null && EditorUtility.IsPersistent(next) && assetType.IsInstanceOfType(next);
            using (new EditorGUI.DisabledScope(!valid || next == currentAsset))
            {
                if (GUILayout.Button(component == null ? "Director 추가 및 연결" : "연결 적용", GUILayout.Width(component == null ? 140 : 80)))
                {
                    if (ApplyReference(entry, component, field, next, assetType))
                    {
                        Refresh();
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }
    }

    private static Component ResolveLoadedComponent(GameObject loadedRoot, GameObject assetRoot, Component original)
    {
        // 이름이 같은 자식과 동일 타입 Ability가 여러 개여도 같은 직렬화 대상을 고른다.
        var indices = new List<int>();
        Transform part = original.transform;
        while (part != assetRoot.transform)
        {
            if (part == null) throw new InvalidOperationException("대상 Component가 선택한 Prefab 안에 없습니다.");
            indices.Add(part.GetSiblingIndex());
            part = part.parent;
        }
        part = loadedRoot.transform;
        for (int i = indices.Count - 1; i >= 0; i--) part = part.GetChild(indices[i]);
        var originals = original.gameObject.GetComponents(original.GetType());
        int componentIndex = Array.IndexOf(originals, original);
        var targets = part.GetComponents(original.GetType());
        if (componentIndex < 0 || componentIndex >= targets.Length)
            throw new InvalidOperationException("Prefab의 Component 대응이 바뀌었습니다. 목록을 갱신하세요.");
        return targets[componentIndex];
    }

    private bool ApplyReference(EnemyEntry entry, Component original, string field, ScriptableObject asset, Type requiredType)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || asset == null || !EditorUtility.IsPersistent(asset)
            || !requiredType.IsInstanceOfType(asset)) return false;
        GameObject contents = null;
        try
        {
            asset = IndependentVital(entry, asset);
            contents = PrefabUtility.LoadPrefabContents(entry.Path);
            Component target = original != null
                ? ResolveLoadedComponent(contents, entry.Prefab.gameObject, original)
                : contents.GetComponent<AiPatternDirector>() ?? contents.AddComponent<AiPatternDirector>();
            using (var settings = new SerializedObject(target))
            {
                var reference = settings.FindProperty(field);
                if (reference == null || reference.propertyType != SerializedPropertyType.ObjectReference)
                    throw new InvalidOperationException("연결할 직렬화 필드를 찾지 못했습니다: " + field);
                reference.objectReferenceValue = asset;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(contents, entry.Path, out bool success);
            if (!success) throw new InvalidOperationException("Prefab 저장에 실패했습니다: " + entry.Path);
            return true;
        }
        catch (Exception error)
        {
            Debug.LogError("[Enemy 밸런스] SO 연결 실패: " + error.Message);
            ShowNotification(new GUIContent("연결 실패 · Console을 확인하세요."));
            return false;
        }
        finally
        {
            if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private ScriptableObject IndependentVital(EnemyEntry entry, ScriptableObject asset)
    {
        if (!(asset is EnemyHealthDataSO) && !(asset is EnemyShieldDataSO)) return asset;
        bool shared = enemies.Any(other => other.Path != entry.Path
            && (other.Prefab.GetComponentsInChildren<EnemyHealth>(true).Any(health => ReadAsset(health, "healthData") == asset)
                || other.Prefab.GetComponentsInChildren<EnemyShieldSystem>(true).Any(shield => ReadAsset(shield, "data") == asset)));
        if (!shared) return asset;
        string folder = asset is EnemyHealthDataSO ? "Assets/Data/Health/Enemies" : "Assets/Data/Health/Shields";
        EnsureFolder(folder);
        string suffix = asset is EnemyHealthDataSO ? "Health" : "Shield";
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + entry.Prefab.name + suffix + ".asset");
        // 디스크 복사 대신 현재 메모리 값을 먼저 복제해 미저장 수치도 보존한다.
        var copy = Instantiate(asset);
        copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(copy, path);
        AssetDatabase.SaveAssetIfDirty(copy);
        return copy;
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string parent = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string child = parent + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(child)) AssetDatabase.CreateFolder(parent, parts[i]);
            parent = child;
        }
    }

    private void CreateHealth(EnemyEntry entry)
    {
        EnsureFolder("Assets/Data/Health/Enemies");
        var asset = ScriptableObject.CreateInstance<EnemyHealthDataSO>();
        asset.maxHP = Mathf.Max(1f, entry.Health.maxHP);
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Data/Health/Enemies/" + entry.Prefab.name + "Health.asset");
        AssetDatabase.CreateAsset(asset, path);
        if (ApplyReference(entry, entry.Health, "healthData", asset, typeof(EnemyHealthDataSO)))
        {
            Refresh();
            GUIUtility.ExitGUI();
        }
        EditorGUIUtility.PingObject(asset);
    }

    private void CreateProfile(EnemyEntry entry, AiPatternDirector director)
    {
        var asset = CreateProfileAsset(entry, director);
        if (asset == null) return;
        Refresh();
        EditorGUIUtility.PingObject(asset);
        GUIUtility.ExitGUI();
    }

    // 빈 Profile을 만들어 Director에 연결한다(Director가 없으면 추가). 연결에 실패하면 null이다.
    private AiPatternProfileSO CreateProfileAsset(EnemyEntry entry, AiPatternDirector director)
    {
        string folder = "Assets/Data/Abilities/Enemies/" + entry.Prefab.name + "/Selection";
        EnsureFolder(folder);
        var asset = ScriptableObject.CreateInstance<AiPatternProfileSO>();
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + entry.Prefab.name + "PatternProfile.asset");
        AssetDatabase.CreateAsset(asset, path);
        if (!ApplyReference(entry, director, "profile", asset, typeof(AiPatternProfileSO)))
        {
            EditorGUIUtility.PingObject(asset);
            return null;
        }
        return asset;
    }

    private void DrawAsset(ScriptableObject asset, string title, bool foldout)
    {
        if (asset == null)
        {
            EditorGUILayout.HelpBox(title + " SO가 연결되지 않았습니다.", MessageType.Warning);
            return;
        }
        string key = selectedPath + "/" + title + "/" + asset.GetInstanceID();
        if (foldout)
        {
            bool open = expanded.Contains(key);
            bool next = EditorGUILayout.Foldout(open, title + " · " + asset.name, true);
            if (next) expanded.Add(key); else expanded.Remove(key);
            if (!next) return;
        }
        DrawReference(asset, title);
        DrawSharedWarning(asset);
        DrawProperties(asset, 0, new HashSet<int>());
    }

    private void DrawSharedWarning(ScriptableObject asset)
    {
        if (assetUsers.TryGetValue(asset.GetInstanceID(), out var users) && users.Count > 1)
            EditorGUILayout.HelpBox("공유 SO입니다. 변경 시 함께 적용: " + string.Join(", ", users), MessageType.Warning);
    }

    private void DrawProperties(ScriptableObject asset, int depth, HashSet<int> visited)
    {
        if (asset == null || !visited.Add(asset.GetInstanceID())) return;
        var settings = Serialized(asset);
        var iterator = settings.GetIterator();
        bool children = true;
        while (iterator.NextVisible(children))
        {
            children = false;
            if (iterator.name == "m_Script") continue;
            // 페이즈별 패턴은 위 '패턴 선택'의 페이즈 탭에서 편집한다.
            if (asset is AiPatternProfileSO && iterator.name == "phases")
            {
                EditorGUILayout.LabelField(Label("phases"), iterator.arraySize > 0
                    ? iterator.arraySize + "개 · 위 '패턴 선택'의 페이즈 탭에서 편집합니다." : "구분 없음");
                continue;
            }
            EditorGUILayout.PropertyField(iterator, new GUIContent(Label(iterator.name), iterator.tooltip), true);
        }
        if (settings.ApplyModifiedProperties() && !editedAssets.Contains(asset)) editedAssets.Add(asset);
        if (depth >= 2) return;
        // Profile의 연결된 규칙 SO도 동일한 Undo 경로로 편집하되 순환 참조는 끊는다.
        iterator = settings.GetIterator();
        while (iterator.NextVisible(true))
        {
            if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
            var child = iterator.objectReferenceValue as ScriptableObject;
            if (!(child is AiPatternRuleSO) && !(child is AiPatternScoreRuleSO)) continue;
            if (visited.Contains(child.GetInstanceID())) continue;
            string key = asset.GetInstanceID() + "/" + iterator.propertyPath;
            bool open = expanded.Contains(key);
            bool next = EditorGUILayout.Foldout(open, "규칙 편집 · " + child.name, true);
            if (next) expanded.Add(key); else expanded.Remove(key);
            if (!next) continue;
            using (new EditorGUI.IndentLevelScope())
            {
                DrawReference(child, "규칙 SO");
                DrawSharedWarning(child);
                DrawProperties(child, depth + 1, visited);
            }
        }
    }

    private static string Label(string name)
    {
        switch (name)
        {
            case "maxHP": return "최대 체력";
            case "maxShield": return "최대 쉴드";
            case "shieldRegenRate": return "쉴드 회복량 / 초";
            case "regenDelay": return "피격 후 회복 지연 / 초";
            case "breakDuration": return "Break 지속 / 초";
            case "breakableDuration": return "Break 가능 시간 / 초";
            case "breakFailedRecoveryValue": return "Break 실패 시 회복 비율";
            case "abilityKey": return "패턴 키";
            case "baseWeight": return "기본 가중치";
            case "weightChangeOnUse": return "사용 시 가중치 증감";
            case "weightRecoveryPerSecond": return "가중치 회복량 / 초";
            case "cooldown": return "종료 후 쿨타임 / 초";
            case "windupTime": return "준비 시간 / 초";
            case "executeTime": return "실행 시간 / 초";
            case "recoverTime": return "회수 시간 / 초";
            case "forceRecoverOnLand": return "착지 시 회수 단계로 전환";
            case "rules": return "기존 패턴 전환 규칙";
            case "scoreRules": return "점수 및 절대 우선순위 규칙";
            case "isEnabled": return "규칙 사용";
            case "operation": return "점수 연산 · 절대 우선권";
            case "value": return "보정 값";
            case "priority": return "절대 우선순위";
            case "oncePerSpawn": return "스폰마다 한 번만 적용";
            case "ignoreCooldown": return "절대 우선권의 쿨타임 무시";
            case "minimumHealthRatio": return "최소 체력 비율";
            case "maximumHealthRatio": return "최대 체력 비율";
            case "usePlayerDistance": return "플레이어 수평 거리 조건 사용";
            case "minimumDistanceX": return "최소 수평 거리";
            case "maximumDistanceX": return "최대 수평 거리";
            case "previousAbilityKey": return "직전 시작 패턴 키 · 빈 값은 제한 없음";
            case "phases": return "페이즈";
            case "useOnPhaseEnter": return "페이즈 진입 시 최우선 사용";
            default: return ObjectNames.NicifyVariableName(name);
        }
    }

    private void DrawRuntime()
    {
        EditorGUILayout.LabelField("Play 중 인스턴스 상태", EditorStyles.boldLabel);
        runtimeEnemy = (EnemyController)EditorGUILayout.ObjectField("관찰할 Enemy", runtimeEnemy, typeof(EnemyController), true);
        if (!EditorApplication.isPlaying || runtimeEnemy == null || EditorUtility.IsPersistent(runtimeEnemy))
        {
            EditorGUILayout.HelpBox("Play 중 Hierarchy의 Enemy 인스턴스를 지정하면 현재 점수와 남은 쿨타임을 읽습니다.", MessageType.Info);
            return;
        }
        var brain = runtimeEnemy.GetComponent<AiBrain>();
        EditorGUILayout.LabelField("현재 패턴", brain != null && brain.current != null ? brain.current.GetType().Name : "없음");
        // 페이즈는 패턴을 고를 때 갱신된 값을 읽기만 한다. 표시를 위해 다시 평가하지 않는다.
        var director = runtimeEnemy.GetComponent<AiPatternDirector>();
        bool phased = director != null && director.Profile != null && director.Profile.UsesPhases;
        if (phased)
        {
            int phaseIndex = director.PhaseIndex;
            string phaseName = phaseIndex >= 0 && phaseIndex < director.Profile.Phases.Count && director.Profile.Phases[phaseIndex] != null
                ? director.Profile.Phases[phaseIndex].displayName : "첫 선택 전";
            EditorGUILayout.LabelField("현재 페이즈", phaseIndex >= 0 ? (phaseIndex + 1) + "번째 · " + phaseName : phaseName);
        }
        if (runtimeEnemy.Health != null)
        {
            EditorGUILayout.LabelField("현재 체력", $"{runtimeEnemy.Health.currentHP:0.##} / {runtimeEnemy.Health.maxHP:0.##}");
            var shield = runtimeEnemy.Health.enemyShieldSystem;
            if (shield != null && shield.isActiveAndEnabled)
                EditorGUILayout.LabelField("현재 쉴드", $"{shield.CurrentShield:0.##} / {shield.MaximumShield:0.##}");
        }
        foreach (var ability in runtimeEnemy.GetComponentsInChildren<AiAbility>(true))
        {
            string note = phased && director.PhaseIndex >= 0 && !ability.HasSelectionTuning ? " | 이 페이즈에 미등록" : string.Empty;
            EditorGUILayout.LabelField(ability.GetType().Name, $"점수 {ability.CurrentWeight:0.##} | 쿨타임 {ability.CooldownRemaining:0.##}초 | {ability.State}{note}");
        }
    }

}
#endif
