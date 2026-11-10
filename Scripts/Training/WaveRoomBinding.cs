using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Animations;

// Unity의 직렬화 경로(Array.data 포함)를 따라 Scene 외부 참조만 복구한다.
public static class WaveRoomBinding
{
    public static void Apply(object target, string path, UnityEngine.Object value)
    {
        if (target is Canvas canvas && path == "m_Camera") { canvas.worldCamera = value as Camera; return; }
        if (target is IConstraint constraint && path.StartsWith("m_Sources.Array.data[") && path.EndsWith(".sourceTransform"))
        {
            int start = path.IndexOf('[') + 1;
            int index = int.Parse(path.Substring(start, path.IndexOf(']') - start));
            var source = constraint.GetSource(index);
            source.sourceTransform = value as Transform;
            constraint.SetSource(index, source);
            return;
        }
        string[] parts = path.Replace(".Array.data[", "[").Split('.');
        Set(target, parts, 0, value);
    }

    private static void Set(object target, string[] parts, int index, object value)
    {
        if (target == null) return;
        string name = parts[index];
        int bracket = name.IndexOf('[');
        int element = bracket < 0 ? -1 : int.Parse(name.Substring(bracket + 1).TrimEnd(']'));
        if (bracket >= 0) name = name.Substring(0, bracket);
        FieldInfo field = null;
        for (var type = target.GetType(); type != null && field == null; type = type.BaseType)
            field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (field == null) return;
        object child = field.GetValue(target);
        if (element >= 0)
        {
            var list = child as IList;
            if (list == null || element >= list.Count) return;
            if (index == parts.Length - 1) list[element] = value;
            else { object item = list[element]; Set(item, parts, index + 1, value); list[element] = item; }
        }
        else if (index == parts.Length - 1) field.SetValue(target, value);
        else { Set(child, parts, index + 1, value); field.SetValue(target, child); }
    }
}
