using System;
using UnityEngine;

/// <summary>
/// (선택) 상호작용 강조(FacilityHighlight)가 '쓸 수 있음' 표현(깜박임·빛 점)을 보일지 묻는 시설. 지금 쓸 수 있는 상태(대기·준비)일 때 true.
/// 구현이 없으면 늘 쓸 수 있는 것으로 본다. 범위 안 밝힘은 FacilityInteractPoint.HighlightChanged(또는 SetFocus)가 정한다.
/// </summary>
public interface IFacilityHighlightSource
{
    bool FacilityReady { get; }
}

/// <summary>
/// 상호작용 시설 공용 강조 효과(DESIGN_V4 §2.1 R4 — 모든 상호작용 대상 공통). 시설 루트에 붙고, 생성 도구가 테두리 Mesh·빛 점을 연결한다.
/// - 테두리: 상호작용 대상의 윤곽을 따라 그린 굵은 선 Mesh(굵기 0.07, Wave/EnvironmentTheme). 색 = tint(기본 채도 높은 청록 — 선형 ≈ .25, .95, 1,
///   레버·출구 문은 SetTint로 앰버). 흰 발광 외곽(Trim, 세기 1.5)보다 확실히 밝고 다른 색이라 멀리서도 '쓸 수 있는 장치'로 읽힌다.
/// - 상태별 세기(실제 발광 세기 = 재질 _Intensity × 밝기 배율, 아래 값은 실제 세기):
///   · 쓸 수 있음(IFacilityHighlightSource.FacilityReady): 1.2초 주기 '두 번 깜박임'(바닥 .35 ↔ 최고 2.9) + 테두리를 따라 도는 빛 점 2개(이동만, 크기 변형 없음).
///   · 범위 안(지점의 키 안내가 켜짐 — HighlightChanged, 또는 붙기 대상 선택 SetFocus): 깜박임 대신 꾸준히 2.2 + 빛 점.
///   · 쓸 수 없음(FacilityReady false — FacilityAccess.Allows false·방전·사용 중): 흐린 정적 선 .25, 깜박임·빛 점 없음.
/// - 쉐브론(▼)은 4차에서 완전히 없앴다(R3 — 범위 안에서는 기존 키 안내만 보인다). chevrons·chevronOffsets는 옛 생성 도구 호환용으로만 남긴 빈 직렬화 필드다(쓰지 않는다).
/// - 밝기는 FacilityLights(MaterialPropertyBlock _Intensity)로, 색은 같은 블록의 _EmissionColor로 바꾼다. 거의 0이면 Renderer를 끈다.
///   Prefab에는 Renderer를 꺼 둔 채 저장해 격자 Tool 미리보기에 강조선이 보이지 않게 하고, 실행 중에만 켠다.
/// - 물리 질의 없음. 풀 반환(비활성)에서 구독을 풀고 밝기·색을 재질 값으로 되돌린다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]   // 시설이 부품을 옮긴 뒤(크레인 LateUpdate) 빛 점을 놓는다.
public sealed class FacilityHighlight : MonoBehaviour
{
    public enum Mode { Off = 0, Disabled = 1, Idle = 2, Active = 3 }

    [Header("구성(생성 도구가 연결)")]
    [SerializeField] private FacilityInteractPoint[] points = Array.Empty<FacilityInteractPoint>();
    [SerializeField, Tooltip("상호작용 대상의 윤곽 선 Mesh.")] private Renderer[] outline = Array.Empty<Renderer>();
    [SerializeField, Tooltip("테두리를 따라 도는 빛 점(cometSpace의 자식, 이동만).")] private Transform[] comets = Array.Empty<Transform>();
    [SerializeField, Tooltip("빛 점이 도는 테두리 다각형(cometSpace 로컬, 닫힌 다각형).")] private Vector2[] cometPath = Array.Empty<Vector2>();
    [SerializeField, Tooltip("빛 점의 부모(테두리 부품). 테두리가 시설과 함께 움직이면(크레인 머리) 빛 점도 따라간다.")] private Transform cometSpace;
    [SerializeField, HideInInspector, Tooltip("(4차: 쓰지 않음 — 옛 생성 도구 호환용, 비워 둔다)")] private Transform[] chevrons = Array.Empty<Transform>();
    [SerializeField, HideInInspector, Tooltip("(4차: 쓰지 않음 — 옛 생성 도구 호환용, 비워 둔다)")] private Vector2[] chevronOffsets = Array.Empty<Vector2>();

    [Header("세기(실제 발광 세기 — 재질 _Intensity로 나눠 배율로 쓴다)")]
    [SerializeField, Min(0f)] private float idleFloor = .35f;
    [SerializeField, Min(0f)] private float idlePeak = 2.9f;
    [SerializeField, Min(.1f), Tooltip("두 번 깜박임 한 주기(초).")] private float blinkPeriod = 1.2f;
    [SerializeField, Min(0f)] private float activeLevel = 2.2f;
    [SerializeField, Min(0f)] private float disabledLevel = .25f;
    [SerializeField, Min(0f), Tooltip("빛 점 세기(쓸 수 있음·범위 안).")] private float cometLevel = 2.6f;
    [SerializeField, Min(.01f), Tooltip("밝기가 바뀌는 시간(초).")] private float fadeTime = .12f;

    [Header("빛 점")]
    [SerializeField, Min(.1f), Tooltip("빛 점이 테두리를 한 바퀴 도는 시간(초). 빠르기는 아래 범위로 자른다.")] private float lapTime = 2.4f;
    [SerializeField, Min(0f)] private float minCometSpeed = 1.5f;
    [SerializeField, Min(0f)] private float maxCometSpeed = 6f;

    [Header("색(4차)")]
    [SerializeField, Tooltip("테두리·빛 점 발광 색(선형 값). 기본 채도 높은 청록, 레버·출구 문은 앰버(1, .69, .13). _Intensity·_EmissionColor가 있는 재질(Wave/EnvironmentTheme)의 발광 색을 이 값으로 바꾼다.")]
    private Color tint = new Color(.25f, .95f, 1f, 1f);

    private bool cached;
    private FacilityLights outlineLights;
    private FacilityLights cometLights;
    private bool[] lit = Array.Empty<bool>();
    private Action<bool>[] handlers = Array.Empty<Action<bool>>();
    private IFacilityHighlightSource source;
    private float outlineLevel;        // 밝기 배율(재질 세기에 곱한다)
    private float cometShown;          // 빛 점 밝기 배율
    private float baseIntensity = 1.8f;
    private float clock;
    private float cometDistance;
    private float loopLength;
    private bool focus;
    private Mode mode;
    private bool tintApplied;
    private MaterialPropertyBlock tintBlock;
    private static readonly int TintIntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int TintEmissionId = Shader.PropertyToID("_EmissionColor");

    /// <summary>지금 테두리 발광 색(선형 값, 4차).</summary>
    public Color Tint => tint;

    /// <summary>
    /// 테두리·빛 점 발광 색을 바꾼다(4차 — 레버·출구 문은 앰버). 밝기 배율(FacilityLights의 _Intensity)과 따로, 같은 MaterialPropertyBlock의 _EmissionColor만 바꾼다.
    /// _Intensity가 없는 재질(밝기를 _EmissionColor·_Color로 내는 재질)은 밝기 표시와 겹치므로 색을 바꾸지 않는다.
    /// </summary>
    public void SetTint(Color c)
    {
        tint = c;
        tintApplied = false;
        if (isActiveAndEnabled && cached) ApplyTint();
    }

    /// <summary>
    /// 지점이 없는 대상(붙기 크레인)의 '범위 안' 표시. 붙기 대상으로 선택된 동안 true를 주면 깜박임 대신 꾸준히 밝힌다(키 안내는 시설이 띄운다).
    /// </summary>
    public void SetFocus(bool on)
    {
        if (focus == on) return;
        focus = on;
        Signals++;
    }

    // 테두리·빛 점 Renderer의 발광 색을 tint로(공유 재질은 건드리지 않는다). 풀 반환 때 FacilityLights.Restore가 블록을 지우므로 다시 켜질 때 또 적용한다.
    private void ApplyTint()
    {
        tintApplied = true;
        tintBlock ??= new MaterialPropertyBlock();
        TintAll(outline);
        foreach (var comet in comets)
            if (comet != null) TintAll(comet.GetComponentsInChildren<Renderer>(true));
    }

    private void TintAll(Renderer[] renderers)
    {
        foreach (var renderer in renderers)
        {
            var material = renderer != null ? renderer.sharedMaterial : null;
            if (material == null || !material.HasProperty(TintIntensityId) || !material.HasProperty(TintEmissionId)) continue;
            renderer.GetPropertyBlock(tintBlock);
            tintBlock.SetVector(TintEmissionId, new Vector4(tint.r, tint.g, tint.b, 1f));
            renderer.SetPropertyBlock(tintBlock);
        }
    }

    /// <summary>범위 안 표시가 켜진 지점이 하나라도 있거나 붙기 대상으로 선택됐는지.</summary>
    public bool AnyHighlighted
    {
        get
        {
            if (focus) return true;
            for (int i = 0; i < lit.Length; i++) if (lit[i]) return true;
            return false;
        }
    }
    /// <summary>지금 테두리 밝기 배율(검사용, 재질 세기에 곱하는 값).</summary>
    public float OutlineLevel => outlineLevel;
    /// <summary>지금 테두리 실제 발광 세기(검사용 = 재질 세기 × 배율).</summary>
    public float OutlineIntensity => outlineLevel * baseIntensity;
    /// <summary>지금 빛 점 밝기 배율(검사용).</summary>
    public float CometLevel => cometShown;
    /// <summary>지금 표현 상태(검사용).</summary>
    public Mode CurrentMode => mode;
    /// <summary>붙기 대상 선택 표시 중인지(검사용).</summary>
    public bool Focused => focus;
    /// <summary>받은 강조 신호 수(검사용).</summary>
    public int Signals { get; private set; }
    public int PointCount => points.Length;
    public int CometCount => comets.Length;
    /// <summary>옛 쉐브론 연결 수(4차는 늘 0이어야 한다 — 생성 도구 Verify·회귀가 본다).</summary>
    public int LegacyChevronCount => (chevrons != null ? chevrons.Length : 0) + (chevronOffsets != null ? chevronOffsets.Length : 0);
    /// <summary>빛 점 index의 지금 자리(월드, 검사용).</summary>
    public Vector3 CometPosition(int index) => index >= 0 && index < comets.Length && comets[index] != null ? comets[index].position : Vector3.zero;
    /// <summary>지점의 순번(없으면 -1, 검사용).</summary>
    public int IndexOf(FacilityInteractPoint point) => Array.IndexOf(points, point);
    /// <summary>쓸 수 있음 상태의 깜박임 세기(실제 세기, 주기 안의 시각 t초). 검사·문서용.</summary>
    public float IdleIntensityAt(float t) => Mathf.Lerp(idleFloor, idlePeak, Blink(t));

    private void Cache()
    {
        if (cached) return;
        cached = true;
        source = GetComponentInParent<IFacilityHighlightSource>();
        // Prefab에는 꺼 둔 채 저장한다(FacilityLights가 밝기 > 0일 때 켜고, Restore로 꺼진 저장 상태로 되돌린다).
        outlineLights = new FacilityLights(outline);
        var cometRenderers = new System.Collections.Generic.List<Renderer>();
        foreach (var comet in comets)
            if (comet != null) cometRenderers.AddRange(comet.GetComponentsInChildren<Renderer>(true));
        cometLights = new FacilityLights(cometRenderers.ToArray());
        baseIntensity = 1.8f;
        foreach (var renderer in outline)
        {
            var material = renderer != null ? renderer.sharedMaterial : null;
            if (material != null && material.HasProperty(TintIntensityId)) { baseIntensity = Mathf.Max(.05f, material.GetFloat(TintIntensityId)); break; }
        }
        loopLength = FacilityMesh.LoopLength(cometPath);
        lit = new bool[points.Length];
        handlers = new Action<bool>[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            int index = i;
            handlers[i] = on => OnHighlight(index, on);
        }
    }

    private void OnEnable()
    {
        Cache();
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null) continue;
            points[i].HighlightChanged -= handlers[i];
            points[i].HighlightChanged += handlers[i];
            lit[i] = points[i].Highlighted;
        }
        outlineLevel = 0f;
        cometShown = 0f;
        clock = 0f;
        cometDistance = 0f;
        focus = false;
        mode = Mode.Off;
        Signals = 0;
        outlineLights.Set(0f);
        cometLights.Set(0f);
        ApplyTint();
    }

    private void OnDisable()
    {
        if (!cached) return;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] != null) points[i].HighlightChanged -= handlers[i];
            lit[i] = false;
        }
        outlineLevel = 0f;
        cometShown = 0f;
        focus = false;
        mode = Mode.Off;
        // 재질 값과 Prefab 저장 상태(꺼짐)로 되돌린다.
        outlineLights.Restore();
        cometLights.Restore();
        // 색 블록도 지워 재질 값으로 돌린다(다시 켜지면 OnEnable에서 또 적용).
        if (tintApplied)
        {
            foreach (var renderer in outline)
                if (renderer != null) renderer.SetPropertyBlock(null);
            foreach (var comet in comets)
                if (comet != null)
                    foreach (var renderer in comet.GetComponentsInChildren<Renderer>(true)) renderer.SetPropertyBlock(null);
        }
        tintApplied = false;
    }

    // 지점의 범위 안 표시가 바뀌었다(비활성 중 OnDisable에서도 false로 올 수 있다 — 상태만 적는다).
    private void OnHighlight(int index, bool on)
    {
        if (index < 0 || index >= lit.Length) return;
        lit[index] = on;
        Signals++;
    }

    // 1.2초 주기 안의 '두 번 깜박임'(0~1). 빠르게 켜지고(0.05초) 천천히 꺼지는(0.17초) 빛이 0.26초 간격으로 두 번.
    private float Blink(float time)
    {
        float c = Mathf.Repeat(time, blinkPeriod);
        return Mathf.Max(Bump(c), Bump(c - .26f));
    }

    private static float Bump(float x)
    {
        if (x < 0f || x > .22f) return 0f;
        if (x < .05f) { float r = x / .05f; return r * r * (3f - 2f * r); }
        float f = 1f - (x - .05f) / .17f;
        return f * f;
    }

    private void LateUpdate()
    {
        if (!cached) return;
        float dt = Time.deltaTime;
        clock += dt;
        bool ready = source == null || source.FacilityReady;
        bool any = AnyHighlighted && ready;
        mode = any ? Mode.Active : ready ? Mode.Idle : Mode.Disabled;
        float wantIntensity = mode == Mode.Active ? activeLevel : mode == Mode.Idle ? Mathf.Lerp(idleFloor, idlePeak, Blink(clock)) : disabledLevel;
        float want = wantIntensity / baseIntensity;
        // 깜박임은 곡선 그대로 따른다(빠르게 켜지는 맛을 살린다). 상태가 바뀔 때만 fadeTime 동안 옮겨 간다.
        if (mode == Mode.Idle) outlineLevel = want;
        else outlineLevel = Mathf.MoveTowards(outlineLevel, want, Mathf.Max(.01f, idlePeak / baseIntensity) * dt / fadeTime);
        outlineLights.Set(outlineLevel);

        float cometWant = mode == Mode.Disabled || comets.Length == 0 ? 0f : cometLevel / baseIntensity;
        cometShown = Mathf.MoveTowards(cometShown, cometWant, Mathf.Max(.01f, cometLevel / baseIntensity) * dt / fadeTime);
        cometLights.Set(cometShown);
        if (cometShown > 0f && loopLength > 1e-3f && cometPath.Length >= 2)
        {
            float speed = Mathf.Clamp(loopLength / Mathf.Max(.1f, lapTime), minCometSpeed, maxCometSpeed);
            cometDistance = Mathf.Repeat(cometDistance + speed * dt, loopLength);
            for (int i = 0; i < comets.Length; i++)
            {
                var comet = comets[i];
                if (comet == null) continue;
                // 빛 점 여럿은 둘레에 고르게 나눠 같은 방향으로 돈다(위치만 바꾼다).
                Vector2 at = FacilityMesh.PointAroundLoop(cometPath, cometDistance + loopLength * i / comets.Length);
                comet.localPosition = new Vector3(at.x, at.y, comet.localPosition.z);
            }
        }
    }
}
