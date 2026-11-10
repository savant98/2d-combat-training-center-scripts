using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 예상 등급이 떨어진 순간 화면 위쪽에 크게 띄우는 알림(2026-11-10, 사용자 요청 "현재 성적이 떨어지면 화면에 커다랗게 표시.
// 예를 들어 무결점에서 우수로 내려가면 GradeDown -> 우수").
// 붉은 띠가 열리며 번쩍이고 "GRADE DOWN"이 찍힌 뒤, 이전 등급(흐림·취소선) → 셰브론 → 새 등급(큰 글자·등급 색)이 이어진다.
// 피격 히트스톱 중에도 멈추지 않게 실제 시간으로 진행한다. 상황판(TrainingScoreboardUI)이 등급 변화를 보고 Play를 부른다.
// 뜰 때 짧게 내려가는 알림음을 낸다(2026-11-11, 사용자 요청 "새로운 Feedback을 만들어서 사용해. 간단한 SCI-FI ui 소리, 너무 튀면 안 돼").
public class TrainingGradeAlertUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform band;
    [SerializeField] private Image flash;
    [SerializeField] private RectTransform title;
    [SerializeField] private CanvasGroup titleGroup;
    [SerializeField] private RectTransform titleLineLeft;
    [SerializeField] private RectTransform titleLineRight;
    [SerializeField] private TextMeshProUGUI fromText;
    [SerializeField] private RectTransform strike;
    [SerializeField] private Image[] chevrons;
    [SerializeField] private TextMeshProUGUI toText;
    [SerializeField] private Image toGlow;
    [SerializeField] private Image ring;
    [SerializeField] private FeedbackDataSO gradeDownFeedback;

    [Header("Timing (실제 시간, 초)")]
    [SerializeField] private float holdEnd = 1.55f;
    [SerializeField] private float duration = 1.85f;

    private float time = -1f;
    private Color toColor = Color.white;
    private Color fromColor = Color.white;
    private Vector2 fromBase, toBase;
    private float lineWidth, strikeWidth;
    private bool cached;

    public bool IsPlaying => time >= 0f;
    public float Duration => duration;

    private void Awake()
    {
        Cache();
        if (time < 0f && group != null) group.alpha = 0f;
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        fromBase = fromText.rectTransform.anchoredPosition;
        toBase = toText.rectTransform.anchoredPosition;
        lineWidth = titleLineLeft.sizeDelta.x;
        strikeWidth = strike.sizeDelta.x;
    }

    public void Play(Grade from, Grade to)
    {
        Cache();
        fromText.text = TrainingBoardStyle.GradeLabel(from);
        toText.text = TrainingBoardStyle.GradeLabel(to);
        fromColor = Color.Lerp(TrainingBoardStyle.GradeColor(from), new Color(0.75f, 0.8f, 0.85f, 1f), 0.45f);
        toColor = TrainingBoardStyle.GradeColor(to);
        fromText.ForceMeshUpdate();
        strikeWidth = Mathf.Max(60f, fromText.preferredWidth + 18f);
        time = 0f;
        Evaluate(0f);
        if (gradeDownFeedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.Play(gradeDownFeedback, transform);
    }

    public void Stop()
    {
        time = -1f;
        if (group != null) group.alpha = 0f;
    }

    private void Update()
    {
        if (time < 0f) return;
        time += Time.unscaledDeltaTime;
        if (time >= duration)
        {
            Stop();
            return;
        }
        Evaluate(time);
    }

    // 시간 t의 모습. 띠 열림 0~0.2, GRADE DOWN 0.06~0.24, 이전 등급 0.18~, 셰브론 0.26~, 새 등급 도장 0.42~0.62, 유지, 닫힘 holdEnd~duration.
    public void Evaluate(float t)
    {
        Cache();
        float exit = TrainingBoardMotion.Smooth(TrainingBoardMotion.Seg(t, holdEnd, duration));
        group.alpha = 1f - exit;

        float sx = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0f, 0.12f));
        float sy = Mathf.Lerp(0.08f, 1f, TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0.08f, 0.2f))) * (1f - 0.92f * exit);
        band.localScale = new Vector3(sx, sy, 1f);

        Color f = TrainingBoardStyle.Warning;
        f.a = 0.85f * TrainingBoardMotion.Seg(t, 0f, 0.04f) * (1f - TrainingBoardMotion.Seg(t, 0.06f, 0.4f));
        flash.color = f;

        float titleIn = TrainingBoardMotion.Seg(t, 0.06f, 0.24f);
        titleGroup.alpha = TrainingBoardMotion.Seg(t, 0.06f, 0.11f);
        title.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, TrainingBoardMotion.OutBack(titleIn));
        float line = lineWidth * TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0.1f, 0.36f));
        titleLineLeft.sizeDelta = new Vector2(line, titleLineLeft.sizeDelta.y);
        titleLineRight.sizeDelta = new Vector2(line, titleLineRight.sizeDelta.y);

        Color fc = fromColor;
        fc.a = 0.8f * TrainingBoardMotion.Seg(t, 0.18f, 0.32f);
        fromText.color = fc;
        fromText.rectTransform.anchoredPosition = fromBase + new Vector2(Mathf.Lerp(24f, 0f, TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0.18f, 0.36f))), 0f);
        strike.sizeDelta = new Vector2(strikeWidth * TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0.52f, 0.66f)), strike.sizeDelta.y);

        for (int i = 0; i < chevrons.Length; i++)
        {
            float on = TrainingBoardMotion.Seg(t, 0.26f + 0.07f * i, 0.34f + 0.07f * i);
            float flow = 0.55f + 0.45f * Mathf.Sin(t * 9f - i * 1.3f);
            Color c = TrainingBoardStyle.Warning;
            c.a = on * flow;
            chevrons[i].color = c;
        }

        float stamp = TrainingBoardMotion.Seg(t, 0.42f, 0.62f);
        Color tc = Color.Lerp(Color.white, toColor, TrainingBoardMotion.Seg(t, 0.46f, 0.75f));
        tc.a = TrainingBoardMotion.Seg(t, 0.42f, 0.46f);
        toText.color = tc;
        toText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.9f, 1f, TrainingBoardMotion.OutBack(stamp));
        float glitch = t > 0.44f && t < 0.6f ? ((int)(t * 60f) % 2 == 0 ? 1f : -1f) * 7f * (1f - TrainingBoardMotion.Seg(t, 0.44f, 0.6f)) : 0f;
        toText.rectTransform.anchoredPosition = toBase + new Vector2(glitch, 0f);

        Color g = toColor;
        g.a = 0.5f * TrainingBoardMotion.Seg(t, 0.42f, 0.5f) * (1f - 0.45f * TrainingBoardMotion.Seg(t, 0.5f, 0.95f));
        toGlow.color = g;
        toGlow.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, TrainingBoardMotion.OutCubic(stamp));

        float burst = TrainingBoardMotion.Seg(t, 0.44f, 0.85f);
        Color r = toColor;
        r.a = 0.9f * (burst > 0f ? 1f - burst : 0f);
        ring.color = r;
        ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.9f, TrainingBoardMotion.OutCubic(burst));
    }
}
