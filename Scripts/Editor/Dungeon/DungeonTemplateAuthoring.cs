using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 새 던전 방 템플릿을 만든다(Tools/Training/던전 방 템플릿 만들기). 결투장 기준 기본 방(실내 26×17 + 벽 3칸) + 맵 층 Wave 1개 + 적 구성 Wave 1개.
/// 출입구 Prefab이 있으면 좌우 벽 바닥 높이에 출입구를 하나씩 놓는다.
/// </summary>
public static class DungeonTemplateAuthoring
{
    public const string DefaultFolder = "Assets/Data/Dungeon/Templates";
    public const string DoorPrefabPath = "Assets/Prefabs/Environment/WaveTools/DungeonPipeDoor.prefab";
    /// <summary>목표 SO 기본 폴더(DESIGN_V3 이름표: CombatLeverObjective.asset이 놓이는 곳).</summary>
    public const string ObjectiveFolder = "Assets/Data/Dungeon/Objectives";
    /// <summary>레버 실물 Prefab(영역 facilities3가 만든다). 있으면 새 레버 목표에 미리 지정한다.</summary>
    public const string LeverPrefabPath = "Assets/Prefabs/Environment/DungeonProps/DungeonPropObjectiveLever.prefab";
    /// <summary>4차 출구 문 Prefab(영역 objective4가 만든다). 있으면 새 레버 목표의 exitPrefab에 미리 지정한다(마무리 방식은 기본 '즉시 클리어').</summary>
    public const string ExitPrefabPath = "Assets/Prefabs/Environment/DungeonProps/DungeonPropExitGate.prefab";
    /// <summary>4차 적 생성 규칙 SO 기본 폴더(DESIGN_V4 §5: CombatEncounterRules.asset이 놓이는 곳).</summary>
    public const string EncounterRulesFolder = "Assets/Data/Dungeon/EncounterRules";
    /// <summary>적 Prefab 폴더(새 적 생성 규칙의 종류 Prefab을 이름으로 찾는다).</summary>
    public const string EnemyFolder = "Assets/Prefabs/Enemies";
    private const string RoomContentPath = "Assets/Prefabs/Training/TrainingRoomBase.prefab";

    /// <summary>메모리에만 있는 새 템플릿(저장하지 않는다). door가 있으면 좌우 벽에 출입구를 놓는다.</summary>
    public static DungeonRoomTemplateSO NewTemplate(GameObject door = null)
    {
        var template = ScriptableObject.CreateInstance<DungeonRoomTemplateSO>();
        var content = AssetDatabase.LoadAssetAtPath<TrainingRoomContent>(RoomContentPath);
        template.roomTemplate = content;
        try { template.cellMap = WaveAuthoring.BaseCellMap(content); }
        catch { UnityEngine.Object.DestroyImmediate(template); throw; }
        var room = WaveAuthoring.BaseRoom;
        template.room = new WaveRoomLayout { bounds = new Rect(room.x, room.y, room.width, room.height), spawnOffset = Vector2.zero };
        template.shape = DungeonRoomShape.Single;
        template.role = DungeonRoomRole.Normal;
        template.allowAsStart = true;
        var start = template.playerStart;
        var mapItems = new List<WaveSpawnInfo>();
        if (door != null)
        {
            // 오른쪽 벽: 실내 xMax부터 3칸 중 안쪽 2칸(기준점 = 2×3칸 가운데). 왼쪽 벽은 180° 회전.
            float y = room.yMin + DungeonDoor.Height * .5f;
            mapItems.Add(new WaveSpawnInfo { prefab = door, isRequiredToClear = false, spawnPosition = new Vector2(room.xMax + DungeonDoor.Depth * .5f, y), rotationSteps = 0 });
            mapItems.Add(new WaveSpawnInfo { prefab = door, isRequiredToClear = false, spawnPosition = new Vector2(room.xMin - DungeonDoor.Depth * .5f, y), rotationSteps = 2 });
        }
        template.waves = new[]
        {
            new Wave { overridePlayerStart = true, playerStart = start, spawnObjects = mapItems.ToArray() },
            new Wave { overridePlayerStart = true, playerStart = start, overrideCellMap = false, changeTerrain = false, clearType = WaveClearType.DefeatAllTargets, spawnObjects = Array.Empty<WaveSpawnInfo>() },
        };
        template.encounters = new[] { new DungeonEncounterInfo { name = "적 구성 1", tier = 1, weight = 1 } };
        return template;
    }

    /// <summary>6차(T16): 템플릿을 같은 폴더에 복제한다(새 GUID — 이름 뒤 번호). 새 에셋을 돌려준다(실패하면 null).</summary>
    public static DungeonRoomTemplateSO Duplicate(DungeonRoomTemplateSO template)
    {
        string path = template != null ? AssetDatabase.GetAssetPath(template) : null;
        if (string.IsNullOrEmpty(path)) return null;
        string target = AssetDatabase.GenerateUniqueAssetPath(path);
        if (!AssetDatabase.CopyAsset(path, target)) return null;
        return AssetDatabase.LoadAssetAtPath<DungeonRoomTemplateSO>(target);
    }
    /// <summary>6차(T16): 템플릿 에셋 이름을 바꾼다(GUID 유지 — 던전 WaveData의 후보 참조가 끊기지 않는다). 실패하면 이유, 성공하면 null.</summary>
    public static string Rename(DungeonRoomTemplateSO template, string newName)
    {
        string path = template != null ? AssetDatabase.GetAssetPath(template) : null;
        if (string.IsNullOrEmpty(path)) return "에셋이 아닙니다.";
        newName = (newName ?? "").Trim();
        if (newName.Length == 0) return "이름이 비었습니다.";
        if (newName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) return "파일 이름에 쓸 수 없는 글자가 있습니다.";
        string error = AssetDatabase.RenameAsset(path, newName);
        return string.IsNullOrEmpty(error) ? null : error;
    }
    /// <summary>6차(T16): folder(테마 폴더)에 새 템플릿을 만든다(기본 이름 + 번호, 그 에셋만 저장).</summary>
    public static DungeonRoomTemplateSO CreateInFolder(string folder, string name = "DungeonRoomTemplate")
    {
        EnsureFolder(folder);
        return CreateAsset(AssetDatabase.GenerateUniqueAssetPath(folder + "/" + (string.IsNullOrWhiteSpace(name) ? "DungeonRoomTemplate" : name.Trim()) + ".asset"));
    }

    /// <summary>path에 새 템플릿 에셋을 만들고 그 에셋만 저장한다.</summary>
    public static DungeonRoomTemplateSO CreateAsset(string path)
    {
        var template = NewTemplate(AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath));
        AssetDatabase.CreateAsset(template, path);
        AssetDatabase.SaveAssetIfDirty(template);
        return template;
    }

    /// <summary>
    /// folder에 새 레버 목표 SO(LeverObjectiveSO, 기본값 — 레버 3개)를 만들고 그 에셋만 저장한다. 레버 Prefab이 있으면 지정한다.
    /// 격자 편집기 '새 레버 목표' 버튼이 부른다(Undo: 에셋 생성은 Undo로 지우지 않으며, 설정의 연결만 Details Apply로 기록된다).
    /// </summary>
    public static LeverObjectiveSO CreateLeverObjective(string folder)
    {
        EnsureFolder(folder);
        var objective = ScriptableObject.CreateInstance<LeverObjectiveSO>();
        objective.leverPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeverPrefabPath);
        objective.exitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ExitPrefabPath);
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/LeverObjective.asset");
        AssetDatabase.CreateAsset(objective, path);
        AssetDatabase.SaveAssetIfDirty(objective);
        return objective;
    }

    /// <summary>
    /// folder에 새 적 생성 규칙 SO(DungeonEncounterRulesSO, 기본값 — DESIGN_V4 §2.8)를 만들고 그 에셋만 저장한다. 종류(Roller·Swarmer·Drone·Jumper)와 Bulwark의 Prefab은
    /// EnemyFolder에서 같은 이름의 적 Prefab이 있으면 지정한다(실제 연결의 정본은 생성 도구 DungeonEncounterRulesSetup — 영역 encounter4).
    /// 격자 편집기 '새 적 생성 규칙' 버튼이 부른다(Undo: 에셋 생성은 Undo로 지우지 않으며, 설정의 연결만 Details Apply로 기록된다).
    /// </summary>
    public static DungeonEncounterRulesSO CreateEncounterRules(string folder)
    {
        EnsureFolder(folder);
        var rules = ScriptableObject.CreateInstance<DungeonEncounterRulesSO>();
        FillEncounterPrefabs(rules);
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/DungeonEncounterRules.asset");
        AssetDatabase.CreateAsset(rules, path);
        AssetDatabase.SaveAssetIfDirty(rules);
        return rules;
    }
    /// <summary>비어 있는 종류·Bulwark Prefab을 이름이 같은 적 Prefab(EnemyFolder/이름.prefab, EnemyController 있음)으로 채운다. 채운 수를 돌려준다.</summary>
    public static int FillEncounterPrefabs(DungeonEncounterRulesSO rules)
    {
        if (rules == null) return 0;
        int filled = 0;
        GameObject Enemy(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{EnemyFolder}/{name.Trim()}.prefab");
            return prefab != null && prefab.GetComponent<EnemyController>() != null ? prefab : null;
        }
        foreach (var species in rules.species ?? Array.Empty<DungeonSpeciesRule>())
        {
            if (species == null || species.prefab != null) continue;
            species.prefab = Enemy(species.name);
            if (species.prefab != null) filled++;
        }
        if (rules.bulwark != null && rules.bulwark.prefab == null)
        {
            rules.bulwark.prefab = Enemy("Bulwark");
            if (rules.bulwark.prefab != null) filled++;
        }
        return filled;
    }

    [MenuItem("Tools/Training/던전 방 템플릿 만들기")]
    private static void CreateFromMenu()
    {
        string folder = SelectedFolder() ?? DefaultFolder;
        try
        {
            EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/DungeonRoomTemplate.asset");
            var template = CreateAsset(path);
            Selection.activeObject = template;
            EditorGUIUtility.PingObject(template);
            // 6차: 열린 던전 템플릿 편집기(파생 창)를 잡지 않도록 정확한 형식의 Wave 격자 편집기를 연다.
            WaveEditorWindow.OpenStandard().Select(template);
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog("던전 방 템플릿 만들기", "만들지 못했습니다: " + exception.Message, "확인");
        }
    }
    // Project 창에서 고른 폴더(또는 고른 에셋의 폴더). 없으면 null.
    private static string SelectedFolder()
    {
        foreach (var guid in Selection.assetGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets", StringComparison.Ordinal)) continue;
            if (AssetDatabase.IsValidFolder(path)) return path;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && AssetDatabase.IsValidFolder(parent)) return parent;
        }
        return null;
    }
    internal static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(parent)) throw new InvalidOperationException("폴더를 만들 수 없습니다: " + folder);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}
