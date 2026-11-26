using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 던전 주요 목표 마커 관리자(4차, 2026-11-25, DESIGN_V4 §2.5 R10·§2.6 R11). 지금 방의 레버·출구 문 실물 위치를 화면 UI 마커로 보인다.
/// 게임플레이(DungeonManager)가 <see cref="IDungeonObjectiveTargets"/>로 내주는 읽기 전용 목록을 매 프레임 읽어 그리기만 한다(AGENTS.md UI 접점 규칙).
/// DungeonManager가 Resources/UI/DungeonObjectiveMarkers Prefab(DungeonRules.objectiveMarkersPrefab)을 UI Canvas에 만들고 IDungeonObjectiveTargetsView.Bind로 연결한다.
/// - 상태별 모양(<see cref="DungeonObjectiveMarkerGraphic"/>):
///   Locked(방 미클리어·출구 해금 전) = 화면 안에서만 흐린 앰버 자물쇠(화면 밖 화살은 띄우지 않는다 — 붉은 적 화살과 헷갈리지 않게),
///   Ready(쓸 수 있음) = 화면 안이면 대상 자리의 밝은 앰버 마름모(+ 레버·문 글리프, 위아래 까딱임·밝기 맥동), 화면 밖이면 위쪽 HUD를 뺀 가장자리의 앰버 이중 화살촉,
///   Done(완료) = 표시하지 않는다(있던 마커는 페이드아웃).
/// - 키 안내 우선(R3): 그 대상의 상호작용 키 안내가 보이면 마커를 숨긴다. 대상 자리(world) 아래 hintMatchBelow ~ 위 hintMatchAbove, 좌우 hintMatchX 안에서
///   HintVisible인 <see cref="FacilityInteractPoint.Live"/> 지점을 찾는다(읽기 전용 — 물리 질의 없음, Area는 Collider 크기 읽기).
/// - 자리: 화면 밖 판정·가장자리 자리는 <see cref="DungeonEnemyMarkers.ComputePlacement"/>(위쪽 HUD를 뺀 사각형 + WaveGoalIndicator.TryPlace)를 그대로 쓴다.
/// - 보임: 원본 있음 && <see cref="DungeonHudGate"/>(HUD 보임·패배 화면·결과 보드 아님). 루트 CanvasGroup으로 한 번에 페이드한다.
/// - 풀: 마커 4개를 미리 만들고 모자라면 template을 복제해 늘린다. 던전 동안 파괴하지 않고 알파로 숨긴다. 매 프레임 할당·LINQ 없음.
/// - 시간: 실제 시간(Time.unscaledDeltaTime), 카메라가 움직인 뒤인 LateUpdate에서 진행한다.
/// - 7차(DESIGN_V7 V5): 원본이 IDungeonObjectiveGate이고 레버 대기 중(AwaitingObjective — 적 전멸 뒤 레버가 남아 문이 잠김)이면 Ready 레버 마커를
///   더 빠르고 깊게 맥동시켜 강조한다(awaitPulsePeriod·awaitPulseMin — 판단값). 모양·자리 규칙은 그대로다(화면 밖이면 가장자리 앰버 화살).
/// Prefab은 tools/Unity/DungeonRoomStatusSetup.cs가 함께 만든다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonObjectiveMarkers : MonoBehaviour, IDungeonObjectiveTargetsView
{
    public const string ResourcePath = "UI/DungeonObjectiveMarkers";

    [Header("Parts")]
    [SerializeField] private CanvasGroup group;
    [Tooltip("복제할 마커(비활성 상태로 둔다)")]
    [SerializeField] private DungeonObjectiveMarkerGraphic template;
    [SerializeField, Min(0)] private int prewarm = 4;

    [Header("Layout (기준 해상도 px)")]
    [Tooltip("화면 위쪽에서 빼는 HUD 띠 높이(방 상태 띠·PlayerHUD 위 끝 — 적 위치 마커와 같은 값)")]
    [SerializeField, Min(0f)] private float topInset = 60f;
    [Tooltip("화면 가장자리에서 화살 중심까지 거리")]
    [SerializeField, Min(0f)] private float edgePadding = 44f;
    [Tooltip("화면 안 마커를 대상 자리 위로 띄우는 높이(월드 칸)")]
    [SerializeField, Min(0f)] private float overLift = .4f;
    [SerializeField, Min(0f)] private float bobDistance = 6f;
    [SerializeField, Min(0f)] private float bobSpeed = 3.2f;
    [Tooltip("Ready 마커 밝기 맥동 최저(1 = 맥동 없음)")]
    [SerializeField, Range(0f, 1f)] private float readyPulseMin = .72f;
    [SerializeField, Min(.1f)] private float readyPulsePeriod = 1.2f;
    [Tooltip("Locked 자물쇠 밝기")]
    [SerializeField, Range(0f, 1f)] private float lockedBrightness = .6f;
    [Tooltip("7차 레버 대기 중 Ready 레버 마커 맥동 주기(초)·최저 밝기(강조)")]
    [SerializeField, Min(.1f)] private float awaitPulsePeriod = .6f;
    [SerializeField, Range(0f, 1f)] private float awaitPulseMin = .45f;

    [Header("키 안내 짝짓기(월드 칸)")]
    [SerializeField, Min(0f)] private float hintMatchX = 2.5f;
    [SerializeField, Min(0f)] private float hintMatchBelow = 2f;
    [SerializeField, Min(0f)] private float hintMatchAbove = 9f;

    [Header("Motion (실제 시간)")]
    [SerializeField, Min(.01f)] private float fadeInTime = .25f;
    [SerializeField, Min(.01f)] private float fadeOutTime = .2f;
    [SerializeField, Min(.01f)] private float groupFadeTime = .2f;

    private IDungeonObjectiveTargets source;
    private readonly List<DungeonObjectiveMarkerGraphic> pool = new List<DungeonObjectiveMarkerGraphic>();
    private bool initialized;
    private float clock, groupAlpha;
    private Canvas rootCanvas;
    private bool canvasSearched;
    private Transform searchedParent;
    private Camera viewCamera;
    private int changes;

    // ===== 검증·조절 =====
    public IDungeonObjectiveTargets Source => source;
    public IReadOnlyList<DungeonObjectiveMarkerGraphic> Pool => pool;
    public float TopInset => topInset;
    public float EdgePadding => edgePadding;
    public float OverLift => overLift;
    public float GroupAlpha => groupAlpha;
    /// <summary>TargetsChanged를 받은 횟수(검증용).</summary>
    public int Changes => changes;
    /// <summary>7차: 마지막 Advance에서 레버 대기 강조를 했는지(검증용).</summary>
    public bool AwaitEmphasis { get; private set; }
    public float AwaitPulsePeriod => awaitPulsePeriod;
    public float AwaitPulseMin => awaitPulseMin;
    /// <summary>검증용: 카메라·안전 영역·Canvas 배율을 직접 준다(null이면 Camera.main·Screen.safeArea·루트 Canvas).</summary>
    public Camera ViewCameraOverride { get; set; }
    public Rect? SafeAreaOverride { get; set; }
    public float? ScaleOverride { get; set; }
    /// <summary>HUD 숨김을 따를 CanvasGroup을 직접 지정한다(검증용).</summary>
    public CanvasGroup HudGroupOverride { get; set; }
    /// <summary>검증용: 대상 자리(world)의 키 안내가 보이는지 직접 답한다(null이면 FacilityInteractPoint.Live를 읽는다).</summary>
    public Func<Vector3, bool> HintOverride { get; set; }

    /// <summary>보이는(알파 &gt; 0) 마커 수.</summary>
    public int VisibleCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < pool.Count; i++) if (pool[i] != null && pool[i].Alpha > 0f) n++;
            return n;
        }
    }

    /// <summary>이번 프레임에 보여야 하는 마커 수.</summary>
    public int WantedCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < pool.Count; i++) if (pool[i] != null && pool[i].Wanted) n++;
            return n;
        }
    }

    /// <summary>대상 하나의 열쇠(종류 × 64 + 번호).</summary>
    public static int KeyOf(DungeonObjectiveTargetKind kind, int index) => (int)kind * 64 + Mathf.Clamp(index, 0, 63);

    /// <summary>대상 하나에 붙은 마커(없으면 null).</summary>
    public DungeonObjectiveMarkerGraphic MarkerFor(DungeonObjectiveTargetKind kind, int index)
    {
        int key = KeyOf(kind, index);
        for (int i = 0; i < pool.Count; i++) if (pool[i] != null && pool[i].Key == key) return pool[i];
        return null;
    }

    private void Awake() => EnsureInit();

    public void EnsureInit()
    {
        if (initialized) return;
        initialized = true;
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = groupAlpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        if (template != null)
        {
            template.gameObject.SetActive(false);
            for (int i = 0; i < prewarm; i++) Grow();
        }
    }

    private void OnDestroy() => Unbind();

    // ===== 연결(IDungeonObjectiveTargetsView) =====

    public void Bind(IDungeonObjectiveTargets targets)
    {
        EnsureInit();
        if (ReferenceEquals(source, targets)) return;
        Unbind();
        source = targets;
        if (source != null) source.TargetsChanged += OnTargetsChanged;
    }

    public void Unbind()
    {
        if (source != null) source.TargetsChanged -= OnTargetsChanged;
        source = null;
        if (initialized) Clear();
    }

    private void OnTargetsChanged() => changes++;

    /// <summary>모든 마커를 즉시 숨긴다(Unbind·검증).</summary>
    public void Clear()
    {
        EnsureInit();
        for (int i = 0; i < pool.Count; i++) if (pool[i] != null) pool[i].Release();
    }

    private DungeonObjectiveMarkerGraphic Grow()
    {
        if (template == null) return null;
        var marker = Instantiate(template, transform, false);
        marker.name = "Marker" + pool.Count;
        marker.gameObject.SetActive(true);
        marker.raycastTarget = false;
        marker.Release();
        pool.Add(marker);
        return marker;
    }

    private DungeonObjectiveMarkerGraphic TakeFree()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            var marker = pool[i];
            if (marker != null && marker.Key < 0 && !marker.Wanted) return marker;
        }
        // 사라지는 중인 마커는 빼앗지 않는다(깜박임 방지). 모자라면 늘린다.
        return Grow();
    }

    // ===== 진행 =====

    private void LateUpdate() => Advance(Time.unscaledDeltaTime);

    /// <summary>실제 시간 dt만큼 진행한다(LateUpdate가 부른다. 검증은 직접 부른다). 할당하지 않는다.</summary>
    public void Advance(float deltaTime)
    {
        EnsureInit();
        deltaTime = Mathf.Max(0f, deltaTime);
        clock += deltaTime;
        bool alive = SourceAlive();

        bool show = alive && DungeonHudGate.ShouldShowDungeonUi(HudGroupOverride);
        groupAlpha = Mathf.MoveTowards(groupAlpha, show ? 1f : 0f, deltaTime / groupFadeTime);
        if (group != null && !Mathf.Approximately(group.alpha, groupAlpha)) group.alpha = groupAlpha;

        for (int i = 0; i < pool.Count; i++) if (pool[i] != null) pool[i].Wanted = false;
        AwaitEmphasis = false;
        var cam = ResolveCamera();
        var targets = alive ? source.CurrentTargets : null;
        if (targets != null && cam != null)
        {
            Rect viewport = cam.pixelRect;
            Rect safe = SafeAreaOverride ?? Screen.safeArea;
            float scale = ResolveScale();
            float top = topInset * scale;
            float padding = edgePadding * scale;
            float readyBright = Mathf.Lerp(readyPulseMin, 1f, .5f + .5f * Mathf.Cos(clock * Mathf.PI * 2f / readyPulsePeriod));
            // 7차 V5: 레버 대기 중이면 레버 마커를 더 빠르고 깊게 맥동(형 확인만 — 할당 없음).
            bool awaiting = source is IDungeonObjectiveGate gate && gate.AwaitingObjective;
            float awaitBright = Mathf.Lerp(awaitPulseMin, 1f, .5f + .5f * Mathf.Cos(clock * Mathf.PI * 2f / awaitPulsePeriod));
            AwaitEmphasis = awaiting;
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target.state == DungeonObjectiveTargetState.Done) continue;
                if (HintVisibleAt(target.world)) continue;                       // 키 안내가 보이면 마커는 숨긴다(R3)
                bool locked = target.state == DungeonObjectiveTargetState.Locked;
                Vector3 world = target.world + Vector3.up * overLift;
                Vector3 projected = cam.WorldToScreenPoint(world);
                bool edge = DungeonEnemyMarkers.ComputePlacement(viewport, safe, top, projected, projected, padding, out var point, out var angle);
                if (locked && edge) continue;                                     // 잠긴 대상은 화면 밖 화살을 띄우지 않는다
                var marker = MarkerFor(target.kind, target.index) ?? TakeFree();
                if (marker == null) continue;
                marker.Key = KeyOf(target.kind, target.index);
                marker.Wanted = true;
                marker.TargetState = target.state;
                marker.Glyph = target.kind;
                marker.DrawShape = edge ? DungeonObjectiveMarkerGraphic.Shape.Edge : locked ? DungeonObjectiveMarkerGraphic.Shape.Locked : DungeonObjectiveMarkerGraphic.Shape.Over;
                marker.Brightness = locked ? lockedBrightness : awaiting && target.kind == DungeonObjectiveTargetKind.Lever ? awaitBright : readyBright;
                Apply(marker, edge, locked, point, edge ? angle : 0f, scale);
            }
        }

        // 페이드(실제 시간). 다 사라진 마커는 풀로 돌려보낸다.
        for (int i = 0; i < pool.Count; i++)
        {
            var marker = pool[i];
            if (marker == null) continue;
            float target = marker.Wanted ? 1f : 0f;
            float rate = deltaTime / (marker.Wanted ? fadeInTime : fadeOutTime);
            marker.SetAlpha(Mathf.MoveTowards(marker.Alpha, target, rate));
            if (!marker.Wanted && marker.Alpha <= 0f && marker.Key >= 0) marker.Release();
        }
    }

    /// <summary>대상 자리 world 근처에 키 안내가 보이는 상호작용 지점이 있는지(대상 아래 hintMatchBelow ~ 위 hintMatchAbove, 좌우 hintMatchX).</summary>
    public bool HintVisibleAt(Vector3 world)
    {
        if (HintOverride != null) return HintOverride(world);
        var live = FacilityInteractPoint.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var p = live[i];
            if (p == null || !p.HintVisible) continue;
            var c = p.Area.center;
            float dx = Mathf.Abs(world.x - c.x), dy = world.y - c.y;
            if (dx <= hintMatchX && dy >= -hintMatchBelow && dy <= hintMatchAbove) return true;
        }
        return false;
    }

    private void Apply(DungeonObjectiveMarkerGraphic marker, bool edge, bool locked, Vector2 point, float angle, float scale)
    {
        // 까딱임: 화면 밖이면 가리키는 쪽으로, 화면 안 Ready면 위아래로, 자물쇠는 멈춤. 마커마다 위상을 달리한다.
        float phase = marker.transform.GetSiblingIndex() * 1.7f;
        float bob = locked ? 0f : (Mathf.Sin(clock * bobSpeed + phase) * .5f + .5f) * bobDistance * scale;
        Vector2 towards = edge ? new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) : Vector2.down;
        Vector2 shown = point - towards * bob;
        marker.ScreenPoint = point;
        marker.Angle = angle;
        marker.Placed = true;

        var parent = transform as RectTransform;
        var canvas = ResolveCanvas();
        if (parent != null && canvas != null)
        {
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, shown, uiCamera, out var local))
                marker.rectTransform.localPosition = new Vector3(local.x, local.y, 0f);
        }
        marker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private Camera ResolveCamera()
    {
        if (ViewCameraOverride != null) return ViewCameraOverride;
        if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = Camera.main;
        return viewCamera;
    }

    // 루트 Canvas는 부모가 바뀔 때만 다시 찾는다(Canvas 밖에 둔 검증용 개체가 매 프레임 검색·할당하지 않게).
    private Canvas ResolveCanvas()
    {
        if (rootCanvas == null && (!canvasSearched || transform.parent != searchedParent))
        {
            canvasSearched = true;
            searchedParent = transform.parent;
            var parentCanvas = GetComponentInParent<Canvas>();
            rootCanvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
        }
        return rootCanvas;
    }

    private float ResolveScale()
    {
        if (ScaleOverride.HasValue) return Mathf.Max(.01f, ScaleOverride.Value);
        var canvas = ResolveCanvas();
        return canvas != null ? Mathf.Max(.01f, canvas.scaleFactor) : 1f;
    }

    private bool SourceAlive()
    {
        if (source == null) return false;
        if (source is UnityEngine.Object unityObject && unityObject == null) { source = null; return false; }
        return true;
    }
}
