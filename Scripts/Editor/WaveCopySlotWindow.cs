using System;
using UnityEditor;
using UnityEngine;

/// <summary>복사한 셀 정보를 3개의 복사본 칸 중 어디에 보관할지 고르는 작은 창.</summary>
public sealed class WaveCopySlotWindow : EditorWindow
{
    private WaveCopyClip clip;
    private Action<int> choose;

    public static void Open(WaveCopyClip clip, Action<int> choose)
    {
        var window = CreateInstance<WaveCopySlotWindow>();
        window.titleContent = new GUIContent("복사본 저장 위치");
        window.clip = clip;
        window.choose = choose;
        window.minSize = window.maxSize = new Vector2(340, 214);
        window.ShowUtility();
        window.Focus();
    }

    private void OnGUI()
    {
        if (clip == null || choose == null) { Close(); return; }
        EditorGUILayout.LabelField("복사한 정보를 보관할 칸을 고르세요.", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"{clip.size.x}×{clip.size.y}칸 · 오브젝트 {clip.items.Length}개", EditorStyles.miniLabel);
        EditorGUILayout.LabelField(clip.label, EditorStyles.wordWrappedMiniLabel);
        GUILayout.Space(4);
        var slots = WaveCopySlots.instance;
        for (int i = 0; i < WaveCopySlots.Count; i++)
        {
            var current = slots[i];
            string text = current.IsEmpty ? $"{i + 1}번 칸 · 비어 있음" : $"{i + 1}번 칸 · {current.items.Length}개 덮어쓰기";
            if (GUILayout.Button(new GUIContent(text, current.IsEmpty ? "" : current.label), GUILayout.Height(30)))
            {
                var callback = choose;
                choose = null;
                Close();
                callback(i);
                return;
            }
        }
        GUILayout.Space(2);
        if (GUILayout.Button("취소")) Close();
    }
}
