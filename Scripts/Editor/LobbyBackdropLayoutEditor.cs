#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>개발자가 로비 소품의 배치와 장식 동선을 편집하는 전용 창이다.</summary>
[InitializeOnLoad]
public class LobbyBackdropLayoutEditor : EditorWindow
{
    private Vector2 scroll;
    private bool showConnections = true;
    private bool showAnchors;
    private bool preview;
    private float previewTime;
    private bool ownsAnimationMode;
    private static bool baking;

    static LobbyBackdropLayoutEditor()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorSceneManager.sceneSaving += OnSceneSaving;
    }

    [MenuItem("Tools/Lobby/배치 편집기")]
    public static void Open()
    {
        var window = GetWindow<LobbyBackdropLayoutEditor>("Lobby 배치");
        window.minSize = new Vector2(420, 460);
        window.Show();
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += DrawConnections;
        Undo.undoRedoPerformed += OnUndo;
    }

    private void OnDisable()
    {
        StopPreview();
        SceneView.duringSceneGui -= DrawConnections;
        Undo.undoRedoPerformed -= OnUndo;
    }

    private void OnGUI()
    {
        var root = LobbyBackdropLayoutBaker.FindBackdrop();
        EditorGUILayout.LabelField("AI 구성요소 개발 → 개발자 배치 → AI 마감", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Edit Mode에서 소품을 선택하고 Scene의 이동 도구(W)나 아래 X·Y로 배치하세요. 저장하거나 Play를 누르면 연결점에 맞춰 화물과 레이저 경로를 갱신합니다.", MessageType.Info);
        if (!LobbyBackdropLayoutBaker.IsReady(root))
        {
            EditorGUILayout.HelpBox("MainScene의 LobbyBackdrop을 열어주세요.", MessageType.Warning);
            return;
        }

        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("로비 전체 보기")) Focus(root.position);
                if (GUILayout.Button("경로 갱신")) RefreshPreview(root);
                if (GUILayout.Button("배치 저장")) Save(root);
            }
            showConnections = EditorGUILayout.ToggleLeft("Scene에 소품 이름과 연결 동선 표시", showConnections);
            showAnchors = EditorGUILayout.ToggleLeft("세부 연결점도 직접 조정", showAnchors);
            foreach (string warning in LobbyBackdropLayoutBaker.Validate(root))
                EditorGUILayout.HelpBox(warning, MessageType.Warning);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < LobbyBackdropLayoutBaker.PropPaths.Length; i++)
                DrawPosition(root.Find(LobbyBackdropLayoutBaker.PropPaths[i]), LobbyBackdropLayoutBaker.PropLabels[i]);
            var sign = root.parent.Find("Furniture/LobbySign");
            if (sign != null) DrawPosition(sign, "LOBBY 간판 (Furniture 전경)");
            var window = root.parent.Find("Furniture/LobbyObservationWindow");
            if (window != null)
            {
                DrawPosition(window, "공장 관찰창 (Furniture 전경)");
                var presentation = window.GetComponent<LobbyObservationWindow>();
                if (presentation != null)
                {
                    EditorGUI.BeginChangeCheck();
                    float opacity = EditorGUILayout.Slider("통창 불투명도", presentation.Opacity, 0f, 1f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(presentation, "통창 불투명도 조정");
                        presentation.Opacity = opacity;
                        EditorUtility.SetDirty(presentation);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(presentation);
                        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
                    }
                }
            }
            if (showAnchors)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("연결점", EditorStyles.boldLabel);
                for (int i = 0; i < LobbyBackdropLayoutBaker.AnchorPaths.Length; i++)
                    DrawPosition(root.Find(LobbyBackdropLayoutBaker.AnchorPaths[i]), LobbyBackdropLayoutBaker.AnchorLabels[i]);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            bool nextPreview = EditorGUILayout.ToggleLeft("장식 애니메이션 미리보기", preview);
            if (nextPreview != preview)
            {
                preview = nextPreview;
                if (preview) RefreshPreview(root);
                else StopPreview();
            }
            using (new EditorGUI.DisabledScope(!preview))
            {
                EditorGUI.BeginChangeCheck();
                previewTime = EditorGUILayout.Slider("순환 시간", previewTime, 0, 40);
                if (EditorGUI.EndChangeCheck()) RefreshPreview(root);
            }
        }
    }

    private void DrawPosition(Transform prop, string label)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(label, EditorStyles.miniButton))
                {
                    Selection.activeGameObject = prop.gameObject;
                    EditorGUIUtility.PingObject(prop.gameObject);
                    Tools.current = Tool.Move;
                }
                if (GUILayout.Button("보기", GUILayout.Width(45))) Focus(prop.position);
            }
            Vector2 current = prop.position;
            EditorGUI.BeginChangeCheck();
            Vector2 changed = EditorGUILayout.Vector2Field("월드 위치", current);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(prop, "로비 소품 위치 조정");
                prop.position = new Vector3(changed.x, changed.y, prop.position.z);
                PrefabUtility.RecordPrefabInstancePropertyModifications(prop);
                EditorSceneManager.MarkSceneDirty(prop.gameObject.scene);
                if (preview) RefreshPreview(LobbyBackdropLayoutBaker.FindBackdrop());
                SceneView.RepaintAll();
            }
        }
    }

    private static void Focus(Vector3 position)
    {
        var view = SceneView.lastActiveSceneView ?? GetWindow<SceneView>();
        view.in2DMode = true;
        view.LookAt(new Vector3(position.x, position.y, 0), Quaternion.identity, 7, true);
        view.Show();
        view.Repaint();
    }

    private void RefreshPreview(Transform root)
    {
        try
        {
            // 다른 Animation 창이 소유한 미리보기 상태는 종료하지 않는다.
            if (AnimationMode.InAnimationMode() && !ownsAnimationMode)
            {
                ShowNotification(new GUIContent("다른 Animation 미리보기를 먼저 종료하세요."));
                preview = false;
                return;
            }
            var clip = LobbyBackdropLayoutBaker.Bake(root);
            if (!preview) return;
            if (!ownsAnimationMode)
            {
                AnimationMode.StartAnimationMode();
                ownsAnimationMode = true;
            }
            AnimationMode.BeginSampling();
            try { AnimationMode.SampleAnimationClip(root.gameObject, clip, previewTime); }
            finally { AnimationMode.EndSampling(); }
            SceneView.RepaintAll();
        }
        catch (Exception error) { Debug.LogException(error); StopPreview(); }
    }

    private void StopPreview()
    {
        if (ownsAnimationMode && AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        ownsAnimationMode = false;
        preview = false;
    }

    private void Save(Transform root)
    {
        StopPreview();
        if (Bake(root, true))
        {
            EditorSceneManager.SaveScene(root.gameObject.scene);
            ShowNotification(new GUIContent("소품 배치와 동선 저장 완료"));
        }
    }

    private void OnUndo()
    {
        if (preview) RefreshPreview(LobbyBackdropLayoutBaker.FindBackdrop());
        Repaint();
    }

    private void OnInspectorUpdate()
    {
        if (preview && !EditorApplication.isPlayingOrWillChangePlaymode)
            RefreshPreview(LobbyBackdropLayoutBaker.FindBackdrop());
        Repaint();
    }

    private void DrawConnections(SceneView view)
    {
        if (!showConnections || EditorApplication.isPlaying) return;
        var root = LobbyBackdropLayoutBaker.FindBackdrop();
        if (!LobbyBackdropLayoutBaker.IsReady(root)) return;
        Handles.color = new Color(.2f, .85f, .55f);
        for (int i = 0; i < LobbyBackdropLayoutBaker.PropPaths.Length; i++)
        {
            var t = root.Find(LobbyBackdropLayoutBaker.PropPaths[i]);
            Handles.Label(t.position + Vector3.up * .25f, LobbyBackdropLayoutBaker.PropLabels[i]);
        }
        var sign = root.parent.Find("Furniture/LobbySign");
        if (sign != null) Handles.Label(sign.position, "LOBBY 간판 (Furniture 전경)");
        var window = root.parent.Find("Furniture/LobbyObservationWindow");
        if (window != null) Handles.Label(window.position, "공장 관찰창 (Furniture 전경)");
        Handles.color = new Color(.3f, .7f, 1);
        foreach (var pair in new[] { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 3, 4 }, new[] { 4, 5 }, new[] { 5, 6 } })
            Handles.DrawDottedLine(root.Find(LobbyBackdropLayoutBaker.AnchorPaths[pair[0]]).position,
                root.Find(LobbyBackdropLayoutBaker.AnchorPaths[pair[1]]).position, 5);
        if (showAnchors)
        {
            for (int i = 0; i < LobbyBackdropLayoutBaker.AnchorPaths.Length; i++)
            {
                var t = root.Find(LobbyBackdropLayoutBaker.AnchorPaths[i]);
                Handles.DrawWireDisc(t.position, Vector3.forward, .12f);
                Handles.Label(t.position, LobbyBackdropLayoutBaker.AnchorLabels[i]);
            }
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;
        foreach (var window in Resources.FindObjectsOfTypeAll<LobbyBackdropLayoutEditor>()) window.StopPreview();
        var root = LobbyBackdropLayoutBaker.FindBackdrop();
        if (LobbyBackdropLayoutBaker.IsReady(root) && !Bake(root, true)) EditorApplication.isPlaying = false;
    }

    private static void OnSceneSaving(Scene scene, string path)
    {
        foreach (var window in Resources.FindObjectsOfTypeAll<LobbyBackdropLayoutEditor>()) window.StopPreview();
        var root = LobbyBackdropLayoutBaker.FindBackdrop();
        if (root == null || root.gameObject.scene != scene || !LobbyBackdropLayoutBaker.IsReady(root)) return;
        if (Bake(root, false)) EditorApplication.delayCall += AssetDatabase.SaveAssets;
    }

    private static bool Bake(Transform root, bool saveAssets)
    {
        if (baking) return false;
        baking = true;
        try { LobbyBackdropLayoutBaker.Bake(root, saveAssets); return true; }
        catch (Exception error) { Debug.LogException(error); return false; }
        finally { baking = false; }
    }
}
#endif
