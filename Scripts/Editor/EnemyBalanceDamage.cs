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
        foreach (var contact in contacts)
        {
            var settings = Serialized(contact);
            var damage = settings.FindProperty("damage");
            string path = AnimationUtility.CalculateTransformPath(contact.transform, entry.Prefab.transform);
            string label = contact.name == "TouchDamageBox" ? "몸통 피해" : contact.name + " 접촉 피해";
            DrawDamageValue(damage, new GUIContent(label, path));
            if (settings.ApplyModifiedProperties() && !editedContactPrefabs.Contains(entry.Prefab.gameObject))
                editedContactPrefabs.Add(entry.Prefab.gameObject);
        }
        EditorGUILayout.LabelField("Prefab 기본값입니다. 실행 중 패턴의 접촉 피해를 별도로 지정하면 그 값이 우선합니다.", EditorStyles.wordWrappedMiniLabel);
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
            case "mineDamageInfo.damage": return "지뢰 피해";
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
        EditorGUILayout.PropertyField(settings.FindProperty("overrideContactDamage"), new GUIContent("패턴 접촉 피해 별도 지정"));
        if (settings.FindProperty("overrideContactDamage").boolValue)
            DrawDamageValue(settings.FindProperty("contactDamage"), new GUIContent("몸통 · 검 · 돌진 접촉 피해"));
        foreach (string path in DamagePaths(settings))
            DrawDamageValue(settings.FindProperty(path), new GUIContent(DamageLabel(path), path));
        if (settings.ApplyModifiedProperties()) MarkEdited(data);
        EditorGUILayout.LabelField("패턴 SO의 피해값이며 모든 페이즈에 공통 적용됩니다. 총알·레이저·폭발은 표시된 각 항목에서 따로 정합니다.", EditorStyles.wordWrappedMiniLabel);
    }
}
#endif
