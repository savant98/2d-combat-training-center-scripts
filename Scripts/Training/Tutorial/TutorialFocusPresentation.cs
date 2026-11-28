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
    private Transform cameraTarget;
    private CinemachineCamera roomCamera;
    private Transform originalFollow;
    private CinemachineBrain brain;
    private bool originalIgnoreTimeScale, cameraFocused;
    private PlayerHUD hud;
    private EnemyController enemy;
    private EnemyUI previewHud;
    private RectTransform previewTarget;
    private TutorialGoal goal;
    private TerminalEquipmentGlyph glyph;
    private RectTransform glyphRoot;
    private TutorialCardGraphic glyphTile;
    private TutorialCardGraphic keyChip;
    private TextMeshProUGUI keyLabel;
    private readonly TMP_FontAsset font;
    private string bindingText;
    private Color accentColor = new Color(1f, .72f, .3f, 1f);

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
        RestoreEnemyPreview();
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
                if (goal == null || !goal.IsPresented)
                    foreach (var candidate in Object.FindObjectsByType<TutorialGoal>(FindObjectsSortMode.None))
                        if (candidate.IsPresented) { goal = candidate; break; }
                return goal != null && ProjectWorld(ComponentBounds(goal, new Vector2(2f, 3f)), owner);
            case TutorialFocusTarget.Enemy:
            case TutorialFocusTarget.EnemyShield:
            case TutorialFocusTarget.EnemyBreak:
                bool requiresShield = step.target != TutorialFocusTarget.Enemy;
                if (enemy == null || !enemy.gameObject.activeInHierarchy)
                    enemy = NearestEnemy(owner, origin + (Vector3)step.focusCenter, requiresShield);
                if (enemy == null) return false;
                if (!requiresShield) return ProjectWorld(ComponentBounds(enemy, new Vector2(3f, 3f)), owner);
                if (previewTarget == null || !previewTarget.gameObject.activeInHierarchy)
                {
                    RestoreEnemyPreview();
                    previewHud = enemy.GetComponentInChildren<EnemyUI>();
                    if (previewHud != null) previewTarget = previewHud.BeginTutorialPreview(step.target == TutorialFocusTarget.EnemyBreak);
                }
                if (previewTarget == null) return false;
                FocusCameraIfNeeded(previewTarget.position, owner);
                return ProjectRect(previewTarget);
            case TutorialFocusTarget.LessonIcon:
                EnsureGlyph(action);
                target = glyphRoot;
                return true;
            case TutorialFocusTarget.GearStages:
            case TutorialFocusTarget.WeaponSet:
                return ProjectHudUnion(step.target);
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
            glyphRoot.sizeDelta = new Vector2(430f, 232f);

            // 어두운 둥근 아이콘 타일(기본 무장 소개 영상 SkillCard 타일 언어)
            var tileRect = new GameObject("TutorialLessonIconTile", typeof(RectTransform), typeof(CanvasRenderer), typeof(TutorialCardGraphic)).GetComponent<RectTransform>();
            tileRect.SetParent(glyphRoot, false);
            tileRect.anchorMin = tileRect.anchorMax = tileRect.pivot = new Vector2(.5f, .5f);
            tileRect.anchoredPosition = new Vector2(0f, 36f);
            tileRect.sizeDelta = new Vector2(138f, 138f);
            glyphTile = tileRect.GetComponent<TutorialCardGraphic>();

            glyph = new GameObject("TutorialLessonIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(TerminalEquipmentGlyph)).GetComponent<TerminalEquipmentGlyph>();
            glyph.rectTransform.SetParent(tileRect, false);
            glyph.rectTransform.anchorMin = glyph.rectTransform.anchorMax = glyph.rectTransform.pivot = new Vector2(.5f, .5f);
            glyph.rectTransform.anchoredPosition = Vector2.zero;
            glyph.rectTransform.sizeDelta = new Vector2(96f, 96f);

            var chipRect = new GameObject("CurrentBindingChip", typeof(RectTransform), typeof(CanvasRenderer), typeof(TutorialCardGraphic)).GetComponent<RectTransform>();
            chipRect.SetParent(glyphRoot, false);
            chipRect.anchorMin = chipRect.anchorMax = chipRect.pivot = new Vector2(.5f, .5f);
            chipRect.anchoredPosition = new Vector2(0f, -74f);
            chipRect.sizeDelta = new Vector2(400f, 56f);
            keyChip = chipRect.GetComponent<TutorialCardGraphic>();

            keyLabel = new GameObject("CurrentBinding", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            keyLabel.rectTransform.SetParent(chipRect, false);
            keyLabel.rectTransform.anchorMin = keyLabel.rectTransform.anchorMax = keyLabel.rectTransform.pivot = new Vector2(.5f, .5f);
            keyLabel.rectTransform.anchoredPosition = Vector2.zero;
            keyLabel.rectTransform.sizeDelta = new Vector2(376f, 50f);
            keyLabel.font = font;
            keyLabel.alignment = TextAlignmentOptions.Center;
            keyLabel.enableAutoSizing = true;
            keyLabel.fontSizeMin = 16f;
            keyLabel.fontSizeMax = 24f;
            keyLabel.raycastTarget = false;
        }
        TerminalEquipmentGlyph.Glyph kind = TutorialGuideTheme.DefaultGlyph(action);
        if (glyphTile != null) glyphTile.ConfigureCard(26f, TutorialGuideTheme.TileFill, accentColor, 3f, accentColor, 0f, 0.45f, 12f, 0.08f);
        if (keyChip != null) keyChip.ConfigureCard(28f, TutorialGuideTheme.InkFill, new Color(accentColor.r, accentColor.g, accentColor.b, 0.55f), 2f, accentColor, 0f, 0.35f, 8f, 0.06f);
        glyph.Configure(kind, accentColor, 3.4f);
        glyphRoot.gameObject.SetActive(true);
        keyLabel.text = bindingText;
    }

    public void SetAccent(Color accent) => accentColor = accent;
    public void SetBindingText(string value) { bindingText = value; if (keyLabel != null) keyLabel.text = value; }

    private RectTransform HudRect(TutorialFocusTarget kind)
    {
        if (hud == null) hud = Object.FindFirstObjectByType<PlayerHUD>();
        if (hud == null) return null;
        if (kind == TutorialFocusTarget.AutoAim) return hud.AutoAimWidget?.BadgeRect;
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

    // 여러 HUD 칸을 한 번에 강조한다(GearStages = 기어 1~3단계, WeaponSet = 기본·기어 1~3·보조). 구멍은 하나이므로
    // 같은 HUD Canvas에 있는 칸들의 world 모서리 합집합을 proxy에 투영한다. 칸이 잠겨 아이콘이 꺼져 있어도 GameObject는 켜져 있다.
    private static readonly TutorialFocusTarget[] GearStageSlots = { TutorialFocusTarget.GearOne, TutorialFocusTarget.GearTwo, TutorialFocusTarget.GearThree };
    private static readonly TutorialFocusTarget[] WeaponSetSlots =
        { TutorialFocusTarget.PrimaryWeapon, TutorialFocusTarget.GearOne, TutorialFocusTarget.GearTwo, TutorialFocusTarget.GearThree, TutorialFocusTarget.SecondaryWeapon };
    private readonly Vector3[] slotCorners = new Vector3[4];

    private bool ProjectHudUnion(TutorialFocusTarget kind)
    {
        Canvas sourceCanvas = null;
        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        bool any = false;
        foreach (var slot in kind == TutorialFocusTarget.GearStages ? GearStageSlots : WeaponSetSlots)
        {
            var rect = SlotFrame(HudRect(slot));
            if (rect == null || !rect.gameObject.activeInHierarchy) continue;
            var slotCanvas = rect.GetComponentInParent<Canvas>();
            if (any && slotCanvas != sourceCanvas) continue;   // 다른 Canvas의 칸은 좌표계가 달라 합치지 않는다
            sourceCanvas = slotCanvas;
            rect.GetWorldCorners(slotCorners);
            foreach (var corner in slotCorners) { min = Vector3.Min(min, corner); max = Vector3.Max(max, corner); }
            any = true;
        }
        if (!any) return false;
        bool world = sourceCanvas != null && sourceCanvas.renderMode == RenderMode.WorldSpace;
        Camera sourceCamera = sourceCanvas != null && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? sourceCanvas.worldCamera != null ? sourceCanvas.worldCamera : Camera.main : null;
        if (world && sourceCamera == null) return false;
        float z = (min.z + max.z) * .5f;
        corners[0] = new Vector3(min.x, min.y, z);
        corners[1] = new Vector3(min.x, max.y, z);
        corners[2] = new Vector3(max.x, max.y, z);
        corners[3] = new Vector3(max.x, min.y, z);
        return ProjectCorners(sourceCamera, world);
    }

    // 칸 아이콘(기어 50×50 등) 대신 그 칸의 배경(GearNBackground 100×100 등)을 쓴다. 부모가 칸보다 훨씬 큰 묶음이면 아이콘 그대로 쓴다.
    private static RectTransform SlotFrame(RectTransform icon)
    {
        if (icon == null) return null;
        var frame = icon.parent as RectTransform;
        if (frame == null) return icon;
        Vector2 own = icon.rect.size, outer = frame.rect.size;
        return outer.x <= Mathf.Max(own.x, 1f) * 2.5f && outer.y <= Mathf.Max(own.y, 1f) * 2.5f ? frame : icon;
    }

    private static object ReadField(object value, string name) => value != null
        ? value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value) : null;

    private void RestoreEnemyPreview()
    {
        if (previewHud != null) previewHud.EndTutorialPreview();
        previewHud = null;
        previewTarget = null;
    }

    private static EnemyController NearestEnemy(WaveManager owner, Vector3 point, bool requiresShield)
    {
        EnemyController nearest = null;
        float distance = float.PositiveInfinity;
        foreach (var candidate in owner.EnemiesInCurrentWave)
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy) continue;
            if (requiresShield && candidate.Health?.enemyShieldSystem == null) continue;
            float value = (candidate.transform.position - point).sqrMagnitude;
            if (value >= distance) continue;
            nearest = candidate;
            distance = value;
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
        RestoreEnemyPreview();
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
