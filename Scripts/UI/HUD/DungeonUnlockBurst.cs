using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 방 클리어 '잠금 해제' 연출(던전형 Wave 방 클리어 피드백 개편). DungeonRoomStatus Prefab 안 부품(UnlockBurst)이고,
/// 이벤트 구독은 부모 <see cref="DungeonRoomStatus"/>가 맡는다(OnRoomCleared → <see cref="Play"/>, OnRoomUnlocked → <see cref="Beat"/>, LateUpdate → <see cref="Advance"/>).
/// 띠 상태 기계(BandState·대기열)에 들어가지 않는 평행 연출이라 띠 순서(ROOM CLEAR → THREAT → EXIT → SECTOR)는 그대로다.
/// - 파란 격자 스윕(ClearVignette): 화면 전체를 덮는 전용 UI 셰이더(UI/DungeonClearGridSweep, 일반 알파 혼합 + 제한된 발광 강도 ≤ 1.05)로
///   파란색 격자가 왼쪽에서 오른쪽으로 빠르게 지나가며, 자기 Canvas(overrideSorting)로 UI Canvas 바로 아래(root − 1)에 그려 HUD를 물들이지 않는다.
///   시간표(실제 시간): 0.06초에 최고 알파 0.55 → 0.42초 유지(그 사이 0.58초에 걸쳐 왼→오 격자 스윕) → 0.95초 동안 OutCubic으로 사라짐.
///   문 열림(RoomUnlocked, 보통 +0.6초) 때 +0.2 맥동(0.45초).
/// - 문구(Banner): 상태 띠 바로 아래(anchor·pivot (0.5,1), (0,-50), 360×56) 판(DungeonStatusPlateGraphic Clear) + 파란 빛줄 + '잠금 해제!'(esamanru, 공백 = 여백 태그).
///   0.22초 가로로 열림(OutBack, 세로 불변) → 1.3초까지 유지 → 0.35초 사라짐(전체 약 1.65초). 빛줄은 0.45초 한 번 쓸고, 문 열림 순간 글자가 흰빛 쪽으로 30% 번쩍인다.
///   띠가 보스 바 쪽으로 옮겨 간 동안에는 같은 x만큼 따라간다(DungeonRoomStatus가 넘긴다).
/// - 언제: RoomCleared(잠금이 있던 방 — 보스 방 포함)일 때만. 순찰 전멸·적 없는 동적 방·출구 입장 클리어는 DungeonManager가 RoomCleared를 올리지 않으므로 뜨지 않는다.
/// - 보임: 던전 HUD 판정(DungeonHudGate)·패배 정지 아님(던전 성공 SECTOR CLEAR 중에는 마저 보임)을 부모가 계산해 넘긴다. 숨겨도 시간표는 계속 흐른다.
/// - 소리: DungeonManager.roomClearFeedback(기계문 철컥 열림 음원 DungeonDoorClearClack)과 doorUnlockFeedback이 함께 재생된다.
/// 매 프레임 Image·CanvasGroup 알파, 전용 런타임 머티리얼 Float, RectTransform 자리·가로 배율만 바꾼다(할당 없음). Prefab 부품은 tools/Unity/DungeonRoomStatusSetup.cs가 만든다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonUnlockBurst : MonoBehaviour
{
    private static readonly int SweepProgressId = Shader.PropertyToID("_SweepProgress");
    private static readonly int BeatPulseId = Shader.PropertyToID("_BeatPulse");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    [Header("Parts")]
    [Tooltip("부품 루트 CanvasGroup(ignoreParentGroups — 띠 페이드와 독립)")]
    [SerializeField] private CanvasGroup group;
    [Tooltip("화면 전체 파란 격자 스윕의 중첩 Canvas(World Space로 저장, 실행 때 UI Canvas 바로 아래 순서)")]
    [SerializeField] private Canvas vignetteCanvas;
    [Tooltip("화면 전체 파란 격자 스윕 Image(UI/DungeonClearGridSweep)")]
    [SerializeField] private Image vignette;
    [SerializeField] private RectTransform banner;
    [SerializeField] private CanvasGroup bannerGroup;
    [SerializeField] private DungeonStatusPlateGraphic plate;
    [Tooltip("파란 빛줄(TrainingBeam + TrainingBoardGlow)")]
    [SerializeField] private Image sweep;
    [SerializeField] private TMP_Text label;

    [Header("Text (정적 글꼴 — DungeonUiText.Glyphs 안, 공백은 여백 태그)")]
    [SerializeField] private string unlockLabel = "잠금 해제!";

    [Header("Color")]
    [Tooltip("클리어 파란색(#38B6FF, 파란 격자 스윕·띠 ROOM CLEAR와 통일)")]
    [SerializeField] private Color color = new Color32(0x38, 0xb6, 0xff, 0xff);

    [Header("파란 격자 스윕 시간표(실제 시간, 초)")]
    [SerializeField, Min(.01f)] private float vignetteRise = .06f;
    [SerializeField, Range(0f, 1f)] private float vignettePeak = .55f;
    [SerializeField, Min(0f)] private float vignetteHold = .42f;
    [SerializeField, Min(.01f)] private float vignetteDecay = .95f;
    [Tooltip("파란 격자가 화면 왼쪽에서 오른쪽 끝까지 지나가는 시간(초)")]
    [SerializeField, Min(.1f)] private float gridSweepTime = .58f;
    [Tooltip("격자 스윕 발광 강도 상한(과도한 Bloom 방지 위해 1.05 이하 유지)")]
    [SerializeField, Range(.2f, 1.05f)] private float gridIntensity = .95f;
    [Tooltip("문 열림(RoomUnlocked) 맥동 세기(알파 더하기)")]
    [SerializeField, Range(0f, 1f)] private float beatAlpha = .2f;
    [SerializeField, Min(.05f)] private float beatTime = .45f;

    [Header("문구 시간표(실제 시간, 초)")]
    [SerializeField, Min(.01f)] private float bannerOpen = .22f;
    [Tooltip("이 시각까지 유지한 뒤 사라지기 시작(열림 포함)")]
    [SerializeField, Min(0f)] private float bannerHold = 1.3f;
    [SerializeField, Min(.01f)] private float bannerFade = .35f;
    [SerializeField, Min(.01f)] private float sweepTime = .45f;
    [Tooltip("문 열림 순간 글자가 흰빛 쪽으로 번쩍이는 세기")]
    [SerializeField, Range(0f, 1f)] private float beatFlash = .3f;
    [Tooltip("보임·숨김 전환 시간")]
    [SerializeField, Min(.01f)] private float visibilityFade = .06f;

    private bool initialized;
    private bool playing;
    private float time;
    private float beatAt = -1f;
    private float visibility;
    private float shift;
    private Vector2 bannerBase;
    private string shownLabel;
    private Material runtimeGridMaterial;

    // ===== 검증·디버그가 읽는 값 =====
    public bool IsPlaying => playing;
    /// <summary>재생 시작 뒤 흐른 실제 시간(초).</summary>
    public float PlayTime => time;
    /// <summary>파란 격자 왼쪽→오른쪽 스윕 진행률(0~1).</summary>
    public float SweepProgress { get; private set; }
    /// <summary>파란 격자 스윕 길이(초).</summary>
    public float GridSweepTime => gridSweepTime;
    /// <summary>격자 발광 강도(과도한 발광 억제 상한 확인용).</summary>
    public float GridIntensity => gridIntensity;
    /// <summary>문 열림 맥동이 들어왔는지(이번 재생에서).</summary>
    public bool HasBeat => beatAt >= 0f;
    public float Visibility => visibility;
    public float VignetteAlpha => vignette != null && vignette.enabled ? vignette.color.a : 0f;
    public float BannerAlpha => bannerGroup != null ? bannerGroup.alpha : 0f;
    public Color BurstColor => color;
    public string UnlockLabel => unlockLabel;
    public CanvasGroup Group => group;
    public Canvas VignetteCanvas => vignetteCanvas;
    public Image Vignette => vignette;
    public RectTransform Banner => banner;
    public CanvasGroup BannerGroup => bannerGroup;
    public DungeonStatusPlateGraphic Plate => plate;
    public Image Sweep => sweep;
    public TMP_Text Label => label;
    public Vector2 BannerBasePosition => bannerBase;
    /// <summary>한 번 재생의 전체 길이(초): 가장자리 빛과 문구 중 긴 쪽.</summary>
    public float Duration => Mathf.Max(vignetteRise + vignetteHold + vignetteDecay, bannerHold + bannerFade);

    private void Awake() => EnsureInit();

    private void Start() => ApplySorting();

    /// <summary>부품을 초기 상태(모두 안 보임)로 둔다. 부모가 먼저 불러도 된다.</summary>
    public void EnsureInit()
    {
        if (initialized) return;
        initialized = true;
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = 1f;
            group.ignoreParentGroups = true;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        // 중첩 Canvas는 Prefab을 따로 열면 최상위 Canvas로 저장될 수 있다 — 어떤 값으로 저장됐든 부모(부품 루트 = Canvas 전체)에 맞춘다(위험 빛과 같은 방식).
        if (vignetteCanvas != null && vignetteCanvas.transform is RectTransform vrt)
        {
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.pivot = new Vector2(.5f, .5f);
            vrt.anchoredPosition3D = Vector3.zero;
            vrt.sizeDelta = Vector2.zero;
            vrt.localScale = Vector3.one;
            vrt.localRotation = Quaternion.identity;
        }
        if (banner != null) bannerBase = banner.anchoredPosition;
        shownLabel = DungeonUiText.Static(unlockLabel);
        if (plate != null) plate.CurrentTone = DungeonStatusPlateGraphic.Tone.Clear;
        // 원본 머티리얼 에셋을 오염시키지 않고 0-할당으로 _SweepProgress를 갱신할 런타임 인스턴스를 한 번만 만든다.
        if (vignette != null && vignette.material != null && vignette.material.HasProperty(SweepProgressId))
        {
            runtimeGridMaterial = new Material(vignette.material) { hideFlags = HideFlags.DontSave };
            runtimeGridMaterial.SetFloat(SweepProgressId, 0f);
            runtimeGridMaterial.SetFloat(BeatPulseId, 0f);
            runtimeGridMaterial.SetFloat(IntensityId, gridIntensity);
            vignette.material = runtimeGridMaterial;
        }
        Draw();
    }

    private void OnDestroy()
    {
        if (runtimeGridMaterial != null)
        {
            if (Application.isPlaying) Destroy(runtimeGridMaterial);
            else DestroyImmediate(runtimeGridMaterial);
            runtimeGridMaterial = null;
        }
    }

    /// <summary>UI Canvas 바로 아래 순서로 그린다(같은 Sorting Layer, 순서 −1 — DangerVignetteUI.ApplySorting과 같다).</summary>
    public void ApplySorting()
    {
        if (vignetteCanvas == null) return;
        var root = vignetteCanvas.rootCanvas;
        if (root == null || root == vignetteCanvas) return;
        vignetteCanvas.overrideSorting = true;
        vignetteCanvas.sortingLayerID = root.sortingLayerID;
        vignetteCanvas.sortingOrder = root.sortingOrder - 1;
        vignetteCanvas.vertexColorAlwaysGammaSpace = root.vertexColorAlwaysGammaSpace;
    }

    /// <summary>방 클리어(잠금이 있던 방) 순간: 처음부터 다시 재생한다.</summary>
    public void Play()
    {
        EnsureInit();
        playing = true;
        time = 0f;
        beatAt = -1f;
        if (label != null && label.text != shownLabel) label.text = shownLabel;
        if (plate != null) plate.CurrentTone = DungeonStatusPlateGraphic.Tone.Clear;
        Draw();
    }

    /// <summary>문 열림(RoomUnlocked) 순간: 재생 중이면 가장자리 빛을 한 번 더 맥동시키고 글자를 번쩍인다.</summary>
    public void Beat()
    {
        EnsureInit();
        if (!playing) return;
        beatAt = time;
    }

    /// <summary>바로 끈다(새 던전 시작·연결 해제).</summary>
    public void Stop()
    {
        EnsureInit();
        playing = false;
        time = 0f;
        beatAt = -1f;
        visibility = 0f;
        Draw();
    }

    /// <summary>실제 시간 dt만큼 진행한다. show = 던전 HUD를 보여도 되는지, shiftX = 띠의 보스 바 쪽 가로 이동(px).</summary>
    public void Advance(float deltaTime, bool show, float shiftX)
    {
        EnsureInit();
        deltaTime = Mathf.Max(0f, deltaTime);
        shift = shiftX;
        if (playing)
        {
            time += deltaTime;
            if (time >= Duration && (beatAt < 0f || time >= beatAt + beatTime)) playing = false;
        }
        visibility = Mathf.MoveTowards(visibility, show && playing ? 1f : 0f, deltaTime / visibilityFade);
        if (!playing && visibility <= 0f) visibility = 0f;
        Draw();
    }

    /// <summary>시각 t(재생 시작 뒤)의 가장자리 빛 알파(맥동 제외).</summary>
    public float VignetteBaseAlpha(float t)
    {
        if (t < 0f) return 0f;
        float rise = TrainingBoardMotion.Seg(t, 0f, vignetteRise);
        float holdEnd = vignetteRise + vignetteHold;
        float decay = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, holdEnd, holdEnd + vignetteDecay));
        return vignettePeak * rise * (1f - decay);
    }

    // 맥동(0→1→0): 빨리 오르고(0.06초) beatTime까지 줄어든다.
    private float BeatWeight()
    {
        if (beatAt < 0f) return 0f;
        float b = time - beatAt;
        if (b < 0f || b >= beatTime) return 0f;
        return TrainingBoardMotion.Seg(b, 0f, .06f) * (1f - TrainingBoardMotion.Seg(b, .06f, beatTime));
    }

    private void Draw()
    {
        float t = time;
        float beat = playing ? BeatWeight() : 0f;
        SweepProgress = playing ? TrainingBoardMotion.OutCubic(Mathf.Clamp01(t / Mathf.Max(.05f, gridSweepTime))) : 0f;
        if (vignette != null)
        {
            float a = playing ? Mathf.Clamp01(VignetteBaseAlpha(t) + beatAlpha * beat) * visibility : 0f;
            bool on = a > .001f;
            if (vignette.enabled != on) vignette.enabled = on;
            if (on)
            {
                Color c = color;
                c.a = a;
                vignette.color = c;
                if (runtimeGridMaterial != null)
                {
                    runtimeGridMaterial.SetFloat(SweepProgressId, SweepProgress);
                    runtimeGridMaterial.SetFloat(BeatPulseId, beat);
                    runtimeGridMaterial.SetFloat(IntensityId, gridIntensity);
                }
            }
        }

        float bannerA = playing ? TrainingBoardMotion.Seg(t, 0f, .08f) * (1f - TrainingBoardMotion.Seg(t, bannerHold, bannerHold + bannerFade)) * visibility : 0f;
        if (bannerGroup != null && !Mathf.Approximately(bannerGroup.alpha, bannerA)) bannerGroup.alpha = bannerA;
        if (banner != null)
        {
            // 세로로는 늘이지 않는다(띠 규칙) — 가로로 열림만.
            float sx = playing ? Mathf.Lerp(.55f, 1f, TrainingBoardMotion.OutBack(TrainingBoardMotion.Seg(t, 0f, bannerOpen))) : 1f;
            var scale = new Vector3(sx, 1f, 1f);
            if (banner.localScale != scale) banner.localScale = scale;
            var position = bannerBase + new Vector2(shift, 0f);
            if (banner.anchoredPosition != position) banner.anchoredPosition = position;
        }
        float flash = playing ? TrainingBoardMotion.Seg(t, 0f, .04f) * (1f - TrainingBoardMotion.Seg(t, .06f, sweepTime)) : 0f;
        if (sweep != null)
        {
            Color s = color;
            s.a = .9f * Mathf.Max(flash, .5f * beat);
            sweep.color = s;
        }
        if (label != null) label.color = Color.Lerp(color, Color.white, Mathf.Max(.55f * flash, beatFlash * beat));
    }
}
