using System.Collections.Generic;
using System.Reflection;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>설명용 영역만 화면 좌표로 투영하고 현재 방 카메라를 잠시 빌립니다. 게임 오브젝트는 이동시키지 않습니다.</summary>
internal sealed class TutorialFocusPresentation
{
    private readonly RectTransform parent, proxy;
    private readonly Canvas canvas;
    private readonly Vector3[] corners = new Vector3[4];
    private readonly List<(CanvasGroup group, float alpha)> shownGroups = new();
    private Transform cameraTarget;
    private CinemachineCamera roomCamera;
    private Transform originalFollow;
    private CinemachineBrain brain;
    private bool originalIgnoreTimeScale, cameraFocused;
    private PlayerHUD hud;
    private EnemyController enemy;
    private TutorialGoal goal;
    private TerminalEquipmentGlyph glyph;
    private RectTransform glyphRoot;
    private TextMeshProUGUI keyLabel;
    private readonly TMP_FontAsset font;
    private string bindingText;

    public TutorialFocusPresentation(RectTransform root, TMP_FontAsset labelFont)
    {
        parent = root;
        font = labelFont;
        canvas = root.GetComponentInParent<Canvas>();
        proxy = new GameObject("TutorialFocusTarget", typeof(RectTransform)).GetComponent<RectTransform>();
        proxy.SetParent(root, false);
        proxy.anchorMin = proxy.anchorMax = proxy.pivot = new Vector2(.5f, .5f);
    }

    public void BeginStep()
    {
        RestoreGroups();
        enemy = null;
        goal = null;
        cameraFocused = false;
        if (glyphRoot != null) glyphRoot.gameObject.SetActive(false);
        RestoreFollow();
    }

    public bool TryTarget(TutorialFocusStep step, WaveManager owner, PlayerController player, TutorialAction action, out RectTransform target)
    {
        target = proxy;
        if (step == null || owner == null) return false;
        var origin = owner.SpawnRoot != null ? owner.SpawnRoot.position : owner.transform.position;
        switch (step.target)
        {
            case TutorialFocusTarget.GridArea:
            case TutorialFocusTarget.Platform:
                return ProjectWorld(new Bounds(origin + (Vector3)step.focusCenter,
                    new Vector3(Mathf.Max(.1f, Mathf.Abs(step.focusSize.x)), Mathf.Max(.1f, Mathf.Abs(step.focusSize.y)), 0)), owner);
            case TutorialFocusTarget.Player:
                return player != null && ProjectWorld(ComponentBounds(player, new Vector2(2f, 3f)), owner);
            case TutorialFocusTarget.Goal:
                if (goal == null || !goal.IsWaveGoalActive)
                    foreach (var candidate in Object.FindObjectsByType<TutorialGoal>(FindObjectsSortMode.None))
                        if (candidate.IsWaveGoalActive) { goal = candidate; break; }
                return goal != null && ProjectWorld(ComponentBounds(goal, new Vector2(2f, 3f)), owner);
            case TutorialFocusTarget.Enemy:
            case TutorialFocusTarget.EnemyShield:
            case TutorialFocusTarget.EnemyBreak:
                if (enemy == null || !enemy.gameObject.activeInHierarchy) enemy = NearestEnemy(origin + (Vector3)step.focusCenter);
                if (enemy == null) return false;
                if (step.target == TutorialFocusTarget.Enemy) return ProjectWorld(ComponentBounds(enemy, new Vector2(3f, 3f)), owner);
                var enemyUI = enemy.GetComponentInChildren<EnemyUI>(true);
                string field = step.target == TutorialFocusTarget.EnemyShield ? "shieldGroup" : "knockDownGroup";
                var group = ReadField(enemyUI, field) as CanvasGroup;
                if (group == null) return false;
                Reveal(group);
                FocusCameraIfNeeded(group.transform.position, owner);
                return ProjectRect(group.transform as RectTransform);
            case TutorialFocusTarget.LessonIcon:
                EnsureGlyph(action);
                target = glyphRoot;
                return true;
            default:
                return ProjectRect(HudRect(step.target));
        }
    }

    private void EnsureGlyph(TutorialAction action)
    {
        if (glyph == null)
        {
            glyphRoot = new GameObject("TutorialLessonIconGroup", typeof(RectTransform)).GetComponent<RectTransform>();
            glyphRoot.SetParent(parent, false);
            glyphRoot.anchorMin = glyphRoot.anchorMax = glyphRoot.pivot = new Vector2(.5f, .5f);
            glyphRoot.sizeDelta = new Vector2(430f, 220f);
            glyph = new GameObject("TutorialLessonIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(TerminalEquipmentGlyph)).GetComponent<TerminalEquipmentGlyph>();
            glyph.rectTransform.SetParent(glyphRoot, false);
            glyph.rectTransform.anchorMin = glyph.rectTransform.anchorMax = glyph.rectTransform.pivot = new Vector2(.5f, .5f);
            glyph.rectTransform.anchoredPosition = new Vector2(0f, 32f);
            glyph.rectTransform.sizeDelta = new Vector2(120f, 120f);
            keyLabel = new GameObject("CurrentBinding", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            keyLabel.rectTransform.SetParent(glyphRoot, false);
            keyLabel.rectTransform.anchorMin = keyLabel.rectTransform.anchorMax = keyLabel.rectTransform.pivot = new Vector2(.5f, .5f);
            keyLabel.rectTransform.anchoredPosition = new Vector2(0f, -72f);
            keyLabel.rectTransform.sizeDelta = new Vector2(420f, 60f);
            keyLabel.font = font;
            keyLabel.alignment = TextAlignmentOptions.Center;
            keyLabel.enableAutoSizing = true;
            keyLabel.fontSizeMin = 18f;
            keyLabel.fontSizeMax = 28f;
            keyLabel.raycastTarget = false;
        }
        TerminalEquipmentGlyph.Glyph kind;
        switch (action)
        {
            case TutorialAction.Jump:
            case TutorialAction.DoubleJump: kind = TerminalEquipmentGlyph.Glyph.Jump; break;
            case TutorialAction.Dash: kind = TerminalEquipmentGlyph.Glyph.Dash; break;
            case TutorialAction.SwapWeapon: kind = TerminalEquipmentGlyph.Glyph.Swap; break;
            case TutorialAction.SecondaryWeapon: kind = TerminalEquipmentGlyph.Glyph.Secondary; break;
            case TutorialAction.Gear: kind = TerminalEquipmentGlyph.Glyph.Gear; break;
            case TutorialAction.Shield: kind = TerminalEquipmentGlyph.Glyph.Shield; break;
            case TutorialAction.Break: kind = TerminalEquipmentGlyph.Glyph.Break; break;
            default: kind = TerminalEquipmentGlyph.Glyph.Move; break;
        }
        glyph.Configure(kind, new Color(1f, .72f, .3f));
        glyphRoot.gameObject.SetActive(true);
        keyLabel.text = bindingText;
    }

    public void SetBindingText(string value) { bindingText = value; if (keyLabel != null) keyLabel.text = value; }

    private RectTransform HudRect(TutorialFocusTarget kind)
    {
        if (hud == null) hud = Object.FindFirstObjectByType<PlayerHUD>();
        if (hud == null) return null;
        if (kind == TutorialFocusTarget.SkillGauge) return (ReadField(hud, "skillGaugeBar") as UnityEngine.UI.Image)?.rectTransform;
        string name = kind switch
        {
            TutorialFocusTarget.PrimaryWeapon => "normalSlot",
            TutorialFocusTarget.SecondaryWeapon => "secondarySlot",
            TutorialFocusTarget.GearOne => "gearStage1Slot",
            TutorialFocusTarget.GearTwo => "gearStage2Slot",
            TutorialFocusTarget.GearThree => "gearStage3Slot",
            _ => null
        };
        var slot = name != null ? ReadField(hud, name) : null;
        return (ReadField(slot, "icon") as UnityEngine.UI.Image)?.rectTransform;
    }

    private static object ReadField(object value, string name) => value != null
        ? value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value) : null;

    private void Reveal(CanvasGroup group)
    {
        foreach (var item in shownGroups) if (item.group == group) return;
        shownGroups.Add((group, group.alpha));
        group.alpha = 1f;
    }

    private void RestoreGroups()
    {
        foreach (var item in shownGroups) if (item.group != null) item.group.alpha = item.alpha;
        shownGroups.Clear();
    }

    private static EnemyController NearestEnemy(Vector3 point)
    {
        EnemyController nearest = null;
        float distance = float.PositiveInfinity;
        foreach (var candidate in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
        {
            float value = (candidate.transform.position - point).sqrMagnitude;
            if (candidate.gameObject.activeInHierarchy && value < distance) { nearest = candidate; distance = value; }
        }
        return nearest;
    }

    private static Bounds ComponentBounds(Component value, Vector2 fallbackSize)
    {
        var collider = value.GetComponent<Collider2D>() ?? value.GetComponentInChildren<Collider2D>();
        return collider != null && collider.enabled ? collider.bounds : new Bounds(value.transform.position, fallbackSize);
    }

    private bool ProjectWorld(Bounds bounds, WaveManager owner)
    {
        var camera = Camera.main;
        if (camera == null) return false;
        // 큰 영상 벽은 화면 아래쪽에 두어 위쪽 설명이 재생면을 가리지 않게 합니다.
        bool large = camera.orthographic && bounds.size.y > camera.orthographicSize * .6f;
        Vector3 point = bounds.center + (large ? Vector3.up * camera.orthographicSize * .34f : Vector3.zero);
        FocusCameraIfNeeded(point, owner, large);
        corners[0] = new Vector3(bounds.min.x, bounds.min.y, bounds.center.z);
        corners[1] = new Vector3(bounds.min.x, bounds.max.y, bounds.center.z);
        corners[2] = new Vector3(bounds.max.x, bounds.max.y, bounds.center.z);
        corners[3] = new Vector3(bounds.max.x, bounds.min.y, bounds.center.z);
        return ProjectCorners(camera, true);
    }

    private bool ProjectRect(RectTransform source)
    {
        if (source == null || !source.gameObject.activeInHierarchy) return false;
        var sourceCanvas = source.GetComponentInParent<Canvas>();
        bool world = sourceCanvas != null && sourceCanvas.renderMode == RenderMode.WorldSpace;
        Camera sourceCamera = sourceCanvas != null && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? sourceCanvas.worldCamera != null ? sourceCanvas.worldCamera : Camera.main : null;
        if (world && sourceCamera == null) return false;
        source.GetWorldCorners(corners);
        return ProjectCorners(sourceCamera, world);
    }

    private bool ProjectCorners(Camera sourceCamera, bool world)
    {
        Camera destinationCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 screen = world ? sourceCamera.WorldToScreenPoint(corners[i]) : RectTransformUtility.WorldToScreenPoint(sourceCamera, corners[i]);
            if (world && screen.z <= 0f) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, destinationCamera, out var local)) return false;
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
        var full = parent.rect;
        min = Vector2.Max(min, full.min);
        max = Vector2.Min(max, full.max);
        if (max.x - min.x < 2f || max.y - min.y < 2f) return false;
        proxy.localPosition = (min + max) * .5f;
        proxy.sizeDelta = max - min;
        return true;
    }

    private void FocusCameraIfNeeded(Vector3 point, WaveManager owner, bool force = false)
    {
        var camera = Camera.main;
        if (camera == null) return;
        var viewport = camera.WorldToViewportPoint(point);
        if (!force && !cameraFocused && viewport.z > 0f && viewport.x >= .2f && viewport.x <= .8f && viewport.y >= .2f && viewport.y <= .8f) return;
        if (roomCamera == null)
        {
            var room = owner.GetComponentInParent<Room>();
            if (room == null) room = RoomManager.Instance != null ? RoomManager.Instance.CurrentRoom : null;
            roomCamera = room != null ? room.VirtualCamera : null;
            if (roomCamera == null) return;
            originalFollow = roomCamera.Follow;
            brain = camera.GetComponent<CinemachineBrain>();
            if (brain != null) { originalIgnoreTimeScale = brain.IgnoreTimeScale; brain.IgnoreTimeScale = true; }
            cameraTarget = new GameObject("TutorialCameraFocus").transform;
        }
        cameraTarget.position = point;
        roomCamera.Follow = cameraTarget;
        cameraFocused = true;
    }

    public void RestoreFollow()
    {
        if (roomCamera != null && cameraTarget != null && roomCamera.Follow == cameraTarget) roomCamera.Follow = originalFollow;
        cameraFocused = false;
    }

    public bool IsPlayerVisible(PlayerController player)
    {
        var camera = Camera.main;
        if (camera == null || player == null) return true;
        var viewport = camera.WorldToViewportPoint(player.transform.position);
        return viewport.z > 0f && viewport.x >= .15f && viewport.x <= .85f && viewport.y >= .15f && viewport.y <= .85f;
    }

    public void End()
    {
        RestoreGroups();
        RestoreFollow();
        if (brain != null) brain.IgnoreTimeScale = originalIgnoreTimeScale;
        if (cameraTarget != null) Object.Destroy(cameraTarget.gameObject);
        cameraTarget = null;
        roomCamera = null;
        brain = null;
        if (glyphRoot != null) glyphRoot.gameObject.SetActive(false);
    }

    public void Dispose()
    {
        End();
        if (proxy != null) Object.Destroy(proxy.gameObject);
        if (glyphRoot != null) Object.Destroy(glyphRoot.gameObject);
    }
}
