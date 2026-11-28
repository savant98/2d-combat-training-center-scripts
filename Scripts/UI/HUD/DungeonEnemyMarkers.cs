using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 남은 필수 적의 위치 마커 관리자(2026-11-24 던전형 Wave 2차, 요청 6 "적이 얼마 안 남으면 위치 표시, 화면 밖이면 화살표").
/// <see cref="DungeonRoomStatus"/>가 실제 시간으로 <see cref="Advance"/>를 부른다(직접 Update하지 않는다 — 시계 하나).
/// - 기준(DESIGN_V2 §1): 생성 완료 && 남은 필수 ≤ markerThreshold && 1마리 이상 처치(남은 수 &lt; 총수)이면 살아 있는 필수 적 모두를 표시한다.
///   예외: 필수 적이 화면 밖에 offscreenRevealDelay초 넘게 있으면 수와 관계없이 그 적의 화살을 띄운다.
/// - 자리: 화면 밖이면 <see cref="WaveGoalIndicator.TryPlace"/>(수정 없이 재사용)에 Screen.safeArea 대신 '위쪽 HUD를 뺀 사각형'을 넘긴다
///   (비보스 60px, 보스 바가 보이면 180px, Canvas scaleFactor 곱함). 화면 안이면 머리 위(몸 Collider bounds.max.y + 0.5칸)에서 아래를 가리킨다.
/// - 몸 크기는 Collider2D.bounds를 읽기만 한다(물리 질의 아님, StageHUD·BreakDamageCounterView와 같은 순서로 Collider를 고른다).
/// - 풀: 마커 6개를 미리 만들고 모자라면 template을 복제해 늘린다. 던전 동안 파괴하지 않고 알파로 숨긴다. 매 프레임 할당·LINQ 없음.
/// - 시간: 실제 시간. 나타남 0.25초, 사라짐 0.15초.
/// - 4차(2026-11-25, DESIGN_V4 §2.8 R18): 재입장 순찰(Phase = Patrol — 잠기지 않고 지나갈 수 있는 방)에서는 부르는 쪽이 active를 주어도 마커를 켜지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonEnemyMarkers : MonoBehaviour
{
    [Header("Parts")]
    [Tooltip("복제할 마커(비활성 상태로 둔다)")]
    [SerializeField] private DungeonEnemyMarker template;
    [SerializeField, Min(0)] private int prewarm = 6;

    [Header("Rule (DungeonRules 값이 있으면 DungeonRoomStatus가 덮어쓴다)")]
    [SerializeField, Min(0)] private int markerThreshold = 3;
    [SerializeField, Min(0f)] private float offscreenRevealDelay = 4f;

    [Header("Layout (기준 해상도 px)")]
    [Tooltip("화면 위쪽에서 빼는 HUD 띠 높이(방 상태 띠·PlayerHUD 위 끝)")]
    [SerializeField, Min(0f)] private float topInset = 60f;
    [Tooltip("보스 바가 보일 때 위쪽에서 빼는 높이(보스 HUD y -44~-162 아래)")]
    [SerializeField, Min(0f)] private float bossTopInset = 180f;
    [Tooltip("화면 가장자리에서 화살 중심까지 거리")]
    [SerializeField, Min(0f)] private float edgePadding = 44f;
    [Tooltip("머리 위 갈매기를 몸 위 끝에서 띄우는 높이(월드 칸)")]
    [SerializeField, Min(0f)] private float headClearance = .5f;
    [Tooltip("Collider를 찾지 못한 적의 머리 높이(월드 칸, 발 기준)")]
    [SerializeField, Min(0f)] private float fallbackHeadHeight = 1.4f;
    [SerializeField, Min(0f)] private float bobDistance = 8f;
    [SerializeField, Min(0f)] private float bobSpeed = 5f;

    [Header("Motion (실제 시간)")]
    [SerializeField, Min(.01f)] private float fadeInTime = .25f;
    [SerializeField, Min(.01f)] private float fadeOutTime = .15f;

    private struct Track
    {
        public EnemyController enemy;
        public Collider2D body;
        public float offscreen;
        public bool seen;
    }

    private readonly List<Track> tracks = new List<Track>();
    private readonly List<DungeonEnemyMarker> pool = new List<DungeonEnemyMarker>();
    private bool initialized;
    private float clock;
    private Canvas rootCanvas;
    private Camera viewCamera;

    // ===== 검증·조절 =====
    public IReadOnlyList<DungeonEnemyMarker> Pool => pool;
    public int Threshold { get => markerThreshold; set => markerThreshold = Mathf.Max(0, value); }
    public float RevealDelay { get => offscreenRevealDelay; set => offscreenRevealDelay = Mathf.Max(0f, value); }
    public float TopInset => topInset;
    public float BossTopInset => bossTopInset;
    public float EdgePadding => edgePadding;
    public float HeadClearance => headClearance;
    public float FallbackHeadHeight => fallbackHeadHeight;
    /// <summary>보스 바가 보이는가(DungeonRoomStatus가 매 프레임 넣는다). 참이면 위쪽에서 bossTopInset을 뺀다.</summary>
    public bool BossBarVisible { get; set; }
    /// <summary>검증용: 카메라·안전 영역·Canvas 배율을 직접 준다(null이면 Camera.main·Screen.safeArea·루트 Canvas).</summary>
    public Camera ViewCameraOverride { get; set; }
    public Rect? SafeAreaOverride { get; set; }
    public float? ScaleOverride { get; set; }
    /// <summary>지금 마지막 판정이 '모두 표시' 조건이었는지.</summary>
    public bool MarkingAll { get; private set; }

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

    /// <summary>적 하나에 붙은 마커(없으면 null).</summary>
    public DungeonEnemyMarker MarkerFor(EnemyController enemy)
    {
        if (enemy == null) return null;
        for (int i = 0; i < pool.Count; i++) if (pool[i] != null && pool[i].Target == enemy) return pool[i];
        return null;
    }

    private void Awake() => EnsureInit();

    public void EnsureInit()
    {
        if (initialized) return;
        initialized = true;
        if (template != null)
        {
            template.gameObject.SetActive(false);
            for (int i = 0; i < prewarm; i++) Grow();
        }
    }

    private DungeonEnemyMarker Grow()
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

    /// <summary>'모두 표시' 기준: 생성 완료 && 0 &lt; 남은 수 ≤ threshold && 남은 수 &lt; 총수(1마리 이상 처치).</summary>
    public static bool ShouldMarkAll(bool spawningDone, int remaining, int total, int threshold) =>
        spawningDone && remaining > 0 && remaining <= threshold && remaining < total;

    /// <summary>위쪽 HUD를 뺀 안전 영역(화면 px).</summary>
    public static Rect HudSafeArea(Rect viewport, Rect safeArea, float topInsetPx)
    {
        float top = Mathf.Min(safeArea.yMax, viewport.yMax) - Mathf.Max(0f, topInsetPx);
        float bottom = Mathf.Max(safeArea.yMin, viewport.yMin);
        return Rect.MinMaxRect(safeArea.xMin, bottom, safeArea.xMax, Mathf.Max(bottom + 1f, top));
    }

    /// <summary>
    /// 마커 자리(화면 px). 몸 중심이 화면 밖이면 WaveGoalIndicator.TryPlace로 HUD를 뺀 영역 가장자리에 놓고 대상 쪽 각도를 준다(true).
    /// 화면 안이면 머리 위 점에 놓고 아래(-90°)를 가리킨다(false). 이때도 HUD 아래·영역 안으로 붙잡는다.
    /// </summary>
    public static bool ComputePlacement(Rect viewport, Rect safeArea, float topInsetPx, Vector3 projectedBody, Vector3 projectedHead, float paddingPx,
                                        out Vector2 point, out float angle)
    {
        var safe = HudSafeArea(viewport, safeArea, topInsetPx);
        if (WaveGoalIndicator.TryPlace(viewport, safe, projectedBody, paddingPx, out point, out angle)) return true;
        float half = Mathf.Min(paddingPx * .5f, Mathf.Min(safe.width, safe.height) * .45f);
        point = new Vector2(Mathf.Clamp(projectedHead.x, safe.xMin + half, safe.xMax - half), Mathf.Clamp(projectedHead.y, safe.yMin + half, safe.yMax - half));
        angle = -90f;
        return false;
    }

    /// <summary>
    /// 실제 시간 dt만큼 진행한다. active가 false(방 상태가 HOSTILES가 아님·패배·던전 밖)이거나 원본 단계가 Patrol(재입장 순찰)이면 모든 마커를 페이드아웃한다.
    /// </summary>
    public void Advance(float deltaTime, IDungeonCombatSource source, bool active)
    {
        // 순찰 방(통과 가능)은 표시하지 않는다(DESIGN_V4 §2.8 '적 마커는 끔').
        bool show = active && source != null && source.Phase != DungeonRoomPhase.Patrol;
        AdvanceEnemies(deltaTime, show ? source.HostileEnemies : null,
            show && ShouldMarkAll(source.HostilesAllSpawned, source.HostilesRemaining, source.HostilesTotal, markerThreshold));
    }

    /// <summary>일반 Wave에서 살아 있는 적을 표시할 때도 같은 위치·화살표·페이드 처리를 사용한다.</summary>
    public void AdvanceEnemies(float deltaTime, IReadOnlyList<EnemyController> enemies, bool markAll)
    {
        EnsureInit();
        deltaTime = Mathf.Max(0f, deltaTime);
        clock += deltaTime;
        for (int i = 0; i < pool.Count; i++) if (pool[i] != null) pool[i].Wanted = false;

        var cam = ResolveCamera();
        MarkingAll = false;
        if (enemies != null && cam != null)
        {
            MarkingAll = markAll;
            for (int t = 0; t < tracks.Count; t++) { var tr = tracks[t]; tr.seen = false; tracks[t] = tr; }

            Rect viewport = cam.pixelRect;
            Rect safe = SafeAreaOverride ?? Screen.safeArea;
            float scale = ResolveScale();
            float top = (BossBarVisible ? bossTopInset : topInset) * scale;
            float padding = edgePadding * scale;

            for (int e = 0; e < enemies.Count; e++)
            {
                var enemy = enemies[e];
                if (!IsAlive(enemy)) continue;
                int index = FindTrack(enemy);
                Track track;
                if (index < 0)
                {
                    track = new Track { enemy = enemy, body = FindBody(enemy) };
                    tracks.Add(track);
                    index = tracks.Count - 1;
                }
                else track = tracks[index];
                track.seen = true;

                BodyPoints(enemy, track.body, out var center, out var head);
                Vector3 projectedBody = cam.WorldToScreenPoint(center);
                bool onScreen = projectedBody.z > 0f && viewport.Contains(projectedBody);
                track.offscreen = onScreen ? 0f : track.offscreen + deltaTime;
                tracks[index] = track;

                if (!MarkingAll && track.offscreen < offscreenRevealDelay) continue;
                var marker = MarkerFor(enemy) ?? TakeFree();
                if (marker == null) continue;
                marker.Target = enemy;
                marker.Wanted = true;
                Vector3 projectedHead = cam.WorldToScreenPoint(head);
                bool edge = ComputePlacement(viewport, safe, top, projectedBody, projectedHead, padding, out var point, out var angle);
                Apply(marker, edge, point, angle, scale);
            }

            // 목록에서 빠진(처치·비활성) 적의 기록을 지운다.
            for (int t = tracks.Count - 1; t >= 0; t--) if (!tracks[t].seen) tracks.RemoveAt(t);
        }
        else tracks.Clear();

        // 페이드(실제 시간). 다 사라진 마커는 풀로 돌려보낸다.
        for (int i = 0; i < pool.Count; i++)
        {
            var marker = pool[i];
            if (marker == null) continue;
            if (marker.Wanted && marker.Target != null && !IsAlive(marker.Target)) marker.Wanted = false;
            float target = marker.Wanted ? 1f : 0f;
            float rate = deltaTime / (marker.Wanted ? fadeInTime : fadeOutTime);
            marker.SetAlpha(Mathf.MoveTowards(marker.Alpha, target, rate));
            if (!marker.Wanted && marker.Alpha <= 0f && marker.Target != null) marker.Release();
        }
    }

    /// <summary>모든 마커를 즉시 숨기고 기록을 지운다(던전 시작·Unbind).</summary>
    public void Clear()
    {
        EnsureInit();
        tracks.Clear();
        for (int i = 0; i < pool.Count; i++) if (pool[i] != null) pool[i].Release();
        MarkingAll = false;
    }

    private DungeonEnemyMarker TakeFree()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            var marker = pool[i];
            if (marker != null && marker.Target == null && !marker.Wanted) return marker;
        }
        // 사라지는 중인 마커는 빼앗지 않는다(깜박임 방지). 모자라면 늘린다.
        return Grow();
    }

    private int FindTrack(EnemyController enemy)
    {
        for (int i = 0; i < tracks.Count; i++) if (tracks[i].enemy == enemy) return i;
        return -1;
    }

    private void Apply(DungeonEnemyMarker marker, bool edge, Vector2 point, float angle, float scale)
    {
        marker.DrawShape = edge ? DungeonEnemyMarker.Shape.Edge : DungeonEnemyMarker.Shape.Over;
        // 까딱임: 마커마다 위상을 달리해 같은 박자로 흔들리지 않게 한다. 화면 밖이면 가리키는 쪽으로, 화면 안이면 위아래로.
        float phase = marker.transform.GetSiblingIndex() * 1.3f;
        float bob = (Mathf.Sin(clock * bobSpeed + phase) * .5f + .5f) * bobDistance * scale;
        Vector2 towards = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
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

    private Canvas ResolveCanvas()
    {
        if (rootCanvas == null)
        {
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

    private static bool IsAlive(EnemyController enemy)
    {
        if (enemy == null || !enemy.gameObject.activeInHierarchy) return false;
        var health = enemy.Health;
        return health == null || health.currentHP > 0f;
    }

    /// <summary>몸 중심과 머리 위 점(월드). Collider가 있으면 bounds(가운데, 위 끝 + headClearance), 없으면 발 위치 + fallbackHeadHeight.</summary>
    public void BodyPoints(EnemyController enemy, Collider2D body, out Vector3 center, out Vector3 head)
    {
        if (body != null && body.enabled && body.gameObject.activeInHierarchy)
        {
            var b = body.bounds;
            if (b.size.sqrMagnitude > 1e-6f)
            {
                center = b.center;
                head = new Vector3(b.center.x, b.max.y + headClearance, b.center.z);
                return;
            }
        }
        var p = enemy.transform.position;
        center = p + Vector3.up * (fallbackHeadHeight * .5f);
        head = p + Vector3.up * (fallbackHeadHeight + headClearance);
    }

    // 몸 크기를 읽을 Collider2D: 루트(모터 몸통) → 피격 판정(EnemyHealth 개체) → 켜진 자식 Collider 가운데 가장 큰 것(StageHUD·BreakDamageCounterView와 같은 순서).
    public static Collider2D FindBody(EnemyController target)
    {
        if (target == null) return null;
        var found = target.GetComponent<Collider2D>();
        if (found != null) return found;
        if (target.Health != null)
        {
            found = target.Health.GetComponent<Collider2D>();
            if (found != null) return found;
        }
        float largest = 0f;
        foreach (var each in target.GetComponentsInChildren<Collider2D>())
        {
            if (!each.enabled) continue;
            var size = each.bounds.size;
            if (size.x * size.y <= largest) continue;
            largest = size.x * size.y;
            found = each;
        }
        return found;
    }
}
