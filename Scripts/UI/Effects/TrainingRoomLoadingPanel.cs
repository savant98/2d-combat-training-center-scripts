using TMPro;
using UnityEngine;

// 로딩 표시. 화면 전환의 암전(화면 페이드·와이어프레임) 위에 진행 막대와 단계 이름을 그린다.
// TrainingRoom 입장 로딩(TrainingRoomHost)과 타이틀 → 메인 씬 전환 로딩(SceneLoadingSequence)이 함께 쓴다.
// 로딩 자체는 그쪽(ILoadingSource)이 진행하고, 이 패널은 그 진행률을 읽어 보여 주기만 한다. 로딩이 끝나면 스스로 사라진다.
// Prefab은 tools/Unity/WaveLoadingSetup.cs가 만들고 공통 규칙(WaveRoomRulesSO.roomLoadingPanel)이 참조한다.
[DisallowMultipleComponent]
public sealed class TrainingRoomLoadingPanel : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField, Tooltip("진행 막대. 가로 길이를 진행률에 맞춘다(왼쪽 기준).")] private RectTransform fill;
    [SerializeField, Tooltip("진행 막대 끝의 밝은 표시.")] private RectTransform head;
    [SerializeField, Tooltip("막대 위를 흐르는 빛. 로딩이 멈춘 것처럼 보이지 않게 한다.")] private RectTransform sweep;
    [SerializeField] private TMP_Text stepLabel;
    [SerializeField] private TMP_Text percentLabel;
    [SerializeField, Tooltip("제목 옆의 설명 글자. 무엇을 불러오는지에 따라 아래 두 문구 가운데 하나를 적는다.")] private TMP_Text captionLabel;
    [SerializeField, Tooltip("TrainingRoom 입장 로딩의 설명(정적 글꼴에 있는 글자만).")] private string roomCaption = "";
    [SerializeField, Tooltip("씬 전환 로딩의 설명(정적 글꼴에 있는 글자만).")] private string sceneCaption = "";
    [SerializeField, Min(1f)] private float trackWidth = 480f;
    [SerializeField, Min(.01f)] private float fadeIn = .12f;
    [SerializeField, Min(.01f)] private float fadeOut = .25f;
    [SerializeField, Min(.1f), Tooltip("막대가 진행률을 따라가는 빠르기(초당 비율). 값이 한 번에 뛰어도 막대는 부드럽게 찬다.")] private float fillSpeed = 3f;
    [SerializeField, Min(.1f)] private float sweepPeriod = .9f;

    private static TrainingRoomLoadingPanel instance;
    private ILoadingSource source;
    private float displayed;
    private float clock;
    private int shownPercent = -1;
    private string shownStep;

    public bool IsShowing => gameObject.activeSelf && source != null && source.IsLoading;
    public float DisplayedProgress => displayed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance() => instance = null;

    /// <summary>로딩 표시를 켠다. 지금 씬의 화면 효과(암전)가 놓인 Canvas에서 와이어프레임 바로 위에 둔다.</summary>
    public static TrainingRoomLoadingPanel Show(ILoadingSource source)
    {
        var rules = WaveRoomRulesSO.Default;
        if (source == null || rules == null || rules.roomLoadingPanel == null) return null;
        if (instance == null)
        {
            var effect = FindFirstObjectByType<ScreenEffect>(FindObjectsInactive.Include);
            if (effect == null) return null;
            var anchor = effect.OverlayRoot;
            instance = Instantiate(rules.roomLoadingPanel, anchor.parent, false);
            instance.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);
        }
        instance.Begin(source);
        return instance;
    }

    private void Begin(ILoadingSource target)
    {
        source = target;
        displayed = 0f; clock = 0f; shownPercent = -1; shownStep = null;
        if (captionLabel != null)
        {
            string caption = target is SceneLoadingSequence ? sceneCaption : roomCaption;
            if (!string.IsNullOrEmpty(caption)) captionLabel.text = caption;
        }
        if (group != null) group.alpha = 0f;
        gameObject.SetActive(true);
        Refresh();
    }

    private void Update()
    {
        // 로딩은 게임 시간과 무관하게 진행되므로 실제 시간으로 움직인다.
        float delta = Mathf.Min(Time.unscaledDeltaTime, .05f);
        bool loading = source != null && source.IsLoading;
        clock += delta;
        displayed = Mathf.MoveTowards(displayed, loading ? source.LoadingProgress : 1f, fillSpeed * delta);
        if (group != null) group.alpha = Mathf.MoveTowards(group.alpha, loading ? 1f : 0f, delta / (loading ? fadeIn : fadeOut));
        Refresh();
        if (!loading && (group == null || group.alpha <= 0f)) { source = null; gameObject.SetActive(false); }
    }

    private void Refresh()
    {
        float width = trackWidth * Mathf.Clamp01(displayed);
        if (fill != null) fill.sizeDelta = new Vector2(width, fill.sizeDelta.y);
        if (head != null) head.anchoredPosition = new Vector2(width, head.anchoredPosition.y);
        if (sweep != null)
        {
            // 빛은 찬 구간 안에서만 왼쪽에서 오른쪽으로 흐른다.
            float travel = Mathf.Repeat(clock / sweepPeriod, 1f);
            sweep.anchoredPosition = new Vector2(Mathf.Max(0f, width - sweep.sizeDelta.x) * travel, sweep.anchoredPosition.y);
            sweep.gameObject.SetActive(width > sweep.sizeDelta.x);
        }
        int percent = Mathf.FloorToInt(Mathf.Clamp01(displayed) * 100f);
        if (percentLabel != null && percent != shownPercent) { shownPercent = percent; percentLabel.SetText("{0}%", percent); }
        string step = source != null ? source.LoadingStep : null;
        if (stepLabel != null && step != shownStep) { shownStep = step; stepLabel.text = StepText(step); }
    }

    // 정적 글꼴에 있는 글자만 쓰도록 단계 이름은 영문 대문자로 적는다.
    private static string StepText(string step) => step switch
    {
        "TERRAIN" => "TERRAIN CACHE",
        "BACKDROP" => "BACKDROP MODULES",
        "UNITS" => "UNIT POOL",
        SceneLoadingSequence.LoadStep => "FACILITY DATA",
        SceneLoadingSequence.StartupStep => "SYSTEM STARTUP",
        _ => "PREPARING",
    };

    private void OnDestroy() { if (instance == this) instance = null; }
}
