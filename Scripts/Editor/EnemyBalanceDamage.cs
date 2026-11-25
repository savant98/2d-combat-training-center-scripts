#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed partial class EnemyBalanceWindow
{
    [SerializeField] private bool showContactDamage = true;
    [SerializeField] private List<GameObject> editedContactPrefabs = new List<GameObject>();

    private void DrawContactDamage(EnemyEntry entry)
    {
        showContactDamage = EditorGUILayout.Foldout(showContactDamage, "몸통 · 부위별 접촉 피해", true);
        if (!showContactDamage) return;
        var contacts = entry.Prefab.GetComponentsInChildren<TouchDamage>(true);
        if (contacts.Length == 0)
        {
            EditorGUILayout.HelpBox("접촉 피해가 없는 Enemy입니다.", MessageType.None);
            return;
        }
        var enemySettings = Serialized(entry.Prefab);
        EditorGUILayout.PropertyField(enemySettings.FindProperty("continuousBodyDamage"), new GUIContent("상시 몸통 접촉 피해"));
        if (enemySettings.ApplyModifiedProperties() && !editedContactPrefabs.Contains(entry.Prefab.gameObject))
            editedContactPrefabs.Add(entry.Prefab.gameObject);
        foreach (var contact in contacts)
        {
            var settings = Serialized(contact);
            var damage = settings.FindProperty("damage");
            string path = AnimationUtility.CalculateTransformPath(contact.transform, entry.Prefab.transform);
            string label = contact.IsBodyContact ? "상시 몸통 피해" : contact.name + " 무기 접촉 피해";
            using (new EditorGUI.DisabledScope(contact.IsBodyContact && !entry.Prefab.ContinuousBodyDamage))
                DrawDamageValue(damage, new GUIContent(label, path));
            if (settings.ApplyModifiedProperties() && !editedContactPrefabs.Contains(entry.Prefab.gameObject))
                editedContactPrefabs.Add(entry.Prefab.gameObject);
        }
        EditorGUILayout.LabelField("상시 몸통 피해는 Swarmer·Bulwark만 사용합니다. 다른 적의 몸통 공격은 패턴별 몸통 공격 피해에서 설정합니다.", EditorStyles.wordWrappedMiniLabel);
    }

    private void SaveContactPrefabs()
    {
        foreach (var prefab in editedContactPrefabs)
        {
            if (prefab == null) continue;
            // Prefab 자식의 변경도 루트에 저장한다. Scene 인스턴스는 수정하지 않는다.
            PrefabUtility.SavePrefabAsset(prefab);
        }
    }

    // 중첩 장판·투사체·폭발의 실제 피해 필드까지 찾고 시간·반경 같은 수치는 제외한다.
    private static List<string> DamagePaths(SerializedObject settings)
    {
        var paths = new List<string>();
        var property = settings.GetIterator();
        while (property.NextVisible(true))
        {
            if (property.propertyType != SerializedPropertyType.Float) continue;
            if (property.name == "damage" || property.name == "explosionDamage") paths.Add(property.propertyPath);
        }
        return paths;
    }

    private static void DrawDamageValue(SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginChangeCheck();
        float next = EditorGUILayout.FloatField(label, property.floatValue);
        if (EditorGUI.EndChangeCheck()) property.floatValue = float.IsFinite(next) ? Mathf.Max(0f, next) : 0f;
    }

    private static string DamageLabel(string path)
    {
        switch (path)
        {
            case "projectileDamageInfo.damage": return "총알 · 투사체 피해";
            case "laserDamageInfo.damage": return "레이저 피해";
            case "slamDamageInfo.damage": return "내려찍기 피해";
            case "waveDamage.damage": return "충격파 피해";
            case "field.damageInfo.damage": return "장판 피해";
            case "cubeDamageInfo.damage": return "낙하 큐브 피해";
            case "slashDamageInfo.damage": return "참격 피해";
            case "missileDamageInfo.damage": return "유도탄 직격 피해";
            case "missileExplosionDamageInfo.damage": return "유도탄 폭발 피해";
            case "drillDamageInfo.damage": return "드릴 피해";
            case "explosionDamageInfo.damage": case "explosionDamage": return "폭발 피해";
            case "damage.damage": case "damageInfo.damage": return "공격 · 투사체 피해";
            default: return ObjectNames.NicifyVariableName(path.Replace(".damage", "")) + " 피해";
        }
    }

    private void DrawPatternDamage(EnemyAbilityDataSO data)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("패턴 피해 설정", EditorStyles.boldLabel);
        var settings = Serialized(data);
        EditorGUILayout.PropertyField(settings.FindProperty("bodyAttackEnabled"), new GUIContent("몸통 공격 사용"));
        if (settings.FindProperty("bodyAttackEnabled").boolValue)
            DrawDamageValue(settings.FindProperty("bodyAttackDamage"), new GUIContent("실제 공격 구간 몸통 피해"));
        EditorGUILayout.PropertyField(settings.FindProperty("overrideContactDamage"), new GUIContent("검 · 다리 무기 피해 별도 지정"));
        if (settings.FindProperty("overrideContactDamage").boolValue)
            DrawDamageValue(settings.FindProperty("contactDamage"), new GUIContent("검 · 다리 무기 접촉 피해"));
        foreach (string path in DamagePaths(settings))
            DrawDamageValue(settings.FindProperty(path), new GUIContent(DamageLabel(path), path));
        if (settings.ApplyModifiedProperties()) MarkEdited(data);
        EditorGUILayout.LabelField("몸통 피해는 패턴 코드가 지정한 실제 돌진·낙하 구간에서만 적용됩니다. 검·다리, 총알·레이저·폭발은 각각 별도 피해값을 사용합니다.", EditorStyles.wordWrappedMiniLabel);
    }
}
#endif
