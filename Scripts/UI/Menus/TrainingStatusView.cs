using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 훈련 상황 한 장(2026-11-10 TrainingRoom 상황판 리메이크). 오른쪽 위 코너판이 쓴다.
// (Wave 전환 때의 중앙판도 같은 배치를 썼으나 2026-11-11에 성적만 보여 주는 판으로 바뀌어 WaveTransitionPanelUI가 직접 그린다.)
// 행: 현재 성적(등급 글자 + 등급 사다리 4칸) / 남은 시간(등급이 시간으로 떨어지는 지점에 눈금이 있는 바) / 남은 피격(비스듬한 칸).
// 켜진 칸·바·등급 글자는 현재 등급 색이라, 등급이 떨어지면 판 전체의 색이 바뀐다. 표시만 하고 게임 상태는 바꾸지 않는다.
public struct TrainingStatusSnapshot
{
    public Grade grade;
    public float timeSeconds;   // 시간 제한이 있으면 남은 시간, 없으면 경과 시간
    public int hitsLeft;        // 통과 등급을 잃기까지 남은 피격(제한 없으면 -1)
    public int waveNumber;      // 진행 중(또는 곧 시작할) Wave 번호, 1부터(모르면 0)
    public int waveTotal;       // 전체 Wave 수(모르면 0)
}

public class TrainingStatusView : MonoBehaviour
{
    [Header("Text")]
    [SerializeField] private TextMeshProUGUI waveText;
    [SerializeField] private TextMeshProUGUI gradeText;
    [SerializeField] private TextMeshProUGUI timeLabel;
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI hitText;

    [Header("Grade")]
    [SerializeField] private Image[] ladder;            // 미흡·통과·우수·무결점 순
    [SerializeField] private Image gradeGlow;
    [SerializeField] private Sprite litSprite;
    [SerializeField] private Sprite slotSprite;

    [Header("Time Bar")]
    [SerializeField] private GameObject timeBar;
    [SerializeField] private RectTransform timeFill;
    [SerializeField] private Image timeFillImage;
    [SerializeField] private RectTransform timeHead;
    [SerializeField] private RectTransform timeMarkRoot;
    [SerializeField] private Image timeMarkTemplate;

    [Header("Hit Cells")]
    [SerializeField] private RectTransform cellRoot;
    [SerializeField] private Image cellTemplate;
    [SerializeField] private float cellGap = 3f;
    [SerializeField] private int maxCells = 20;

    [Header("Motion")]
    [SerializeField] private float cellFlashDuration = 0.45f;
    [SerializeField] private float gradePunchScale = 1.4f;
    [SerializeField] private float gradePunchDuration = 0.35f;
    [SerializeField] private float gradeGlowAlpha = 0.28f;

    [Header("Danger (곧 미흡: 숫자를 경고색으로)")]
    [SerializeField] private int dangerHits = 5;
    [SerializeField] private float dangerSeconds = 60f;

    private const string MonoOpen = "<mspace=0.6em>";
    private Image[] cells = new Image[0];
    private float[] cellFlash = new float[0];
    private int hitLimit = -1;
    private float timeLimit = -1f;
    private bool hasState;
    private TrainingStatusSnapshot shown;
    private int shownTimeValue = int.MinValue;
    private float punch = -1f;
    private bool hitDanger, timeDanger;

    public RectTransform Rect => (RectTransform)transform;

    // 훈련이 바뀔 때(상황판을 켤 때) 통과 기준으로 칸 수·바 눈금을 다시 만든다.
    public void Configure(TrainingDefinitionSO training)
    {
        TrainingBoardStyle.GetPassLimits(training, out hitLimit, out timeLimit);
        if (timeLabel != null) timeLabel.text = timeLimit >= 0f ? "남은 시간" : "경과 시간";
        if (timeBar != null) timeBar.SetActive(timeLimit > 0f);
        BuildTimeMarks(training);
        BuildCells();
        hasState = false;
        shownTimeValue = int.MinValue;
        punch = -1f;
        hitDanger = timeDanger = false;
        if (hitText != null) hitText.color = Color.white;
        if (timeText != null) timeText.color = Color.white;
        if (gradeText != null) gradeText.rectTransform.localScale = Vector3.one;
    }

    public void Refresh(TrainingStatusSnapshot s)
    {
        bool first = !hasState;     // Configure 뒤 첫 갱신은 연출 없이 값을 그대로 그린다

        if (first || s.grade != shown.grade)
        {
            if (!first && s.grade < shown.grade) punch = 0f;
            ApplyGrade(s.grade);
        }

        int timeValue = timeLimit >= 0f ? Mathf.CeilToInt(s.timeSeconds - 0.0001f) : Mathf.FloorToInt(s.timeSeconds);
        if (first || timeValue != shownTimeValue)
        {
            shownTimeValue = timeValue;
            if (timeText != null) timeText.text = MonoOpen + TrainingBoardStyle.FormatTime(s.timeSeconds, timeLimit >= 0f);
        }
        if (timeLimit > 0f && timeFill != null)
        {
            float ratio = Mathf.Clamp01(s.timeSeconds / timeLimit);
            timeFill.anchorMax = new Vector2(ratio, 1f);
            if (timeHead != null)
            {
                timeHead.anchorMin = new Vector2(ratio, 0f);
                timeHead.anchorMax = new Vector2(ratio, 1f);
                timeHead.gameObject.SetActive(ratio > 0.001f && ratio < 0.999f);
            }
        }

        if (first || s.hitsLeft != shown.hitsLeft)
        {
            if (hitText != null) hitText.text = hitLimit < 0 ? "-" : MonoOpen + s.hitsLeft;
            if (!first)
                for (int i = Mathf.Max(0, s.hitsLeft); i < Mathf.Min(cells.Length, shown.hitsLeft); i++)
                    cellFlash[i] = 0f;      // 방금 잃은 칸이 붉게 번쩍이고 빈 칸으로 식는다
            PaintCells(s.hitsLeft, s.grade);
        }

        // 통과 한도에 가까워지면(남은 피격 5 이하, 남은 시간 1분 이하) 숫자를 경고색으로 바꾼다.
        bool hitNow = hitLimit >= 0 && s.hitsLeft <= dangerHits;
        if (hitNow != hitDanger)
        {
            hitDanger = hitNow;
            if (hitText != null) hitText.color = hitNow ? (Color)TrainingBoardStyle.Warning : Color.white;
        }
        bool timeNow = timeLimit > 0f && s.timeSeconds <= dangerSeconds;
        if (timeNow != timeDanger)
        {
            timeDanger = timeNow;
            if (timeText != null) timeText.color = timeNow ? (Color)TrainingBoardStyle.Warning : Color.white;
        }

        if ((first || s.waveNumber != shown.waveNumber || s.waveTotal != shown.waveTotal) && waveText != null)
            waveText.text = s.waveTotal > 0 ? $"WAVE {Mathf.Max(1, s.waveNumber):00} / {s.waveTotal:00}" : "";

        shown = s;
        hasState = true;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        for (int i = 0; i < cells.Length; i++)
        {
            if (cellFlash[i] < 0f) continue;
            cellFlash[i] += dt;
            float k = TrainingBoardMotion.Seg(cellFlash[i], 0f, cellFlashDuration);
            Color slot = TrainingBoardStyle.LineDim;
            cells[i].color = Color.Lerp(TrainingBoardStyle.Warning, slot, TrainingBoardMotion.Smooth(k));
            cells[i].sprite = k < 0.5f ? litSprite : slotSprite;
            if (k >= 1f) cellFlash[i] = -1f;
        }

        if (punch >= 0f && gradeText != null)
        {
            punch += dt;
            float k = TrainingBoardMotion.Seg(punch, 0f, gradePunchDuration);
            gradeText.rectTransform.localScale = Vector3.one * Mathf.Lerp(gradePunchScale, 1f, TrainingBoardMotion.OutBack(k));
            if (gradeGlow != null)
            {
                Color c = gradeGlow.color;
                c.a = Mathf.Lerp(gradeGlowAlpha * 3f, gradeGlowAlpha, k);
                gradeGlow.color = c;
            }
            if (k >= 1f) punch = -1f;
        }
    }

    private void ApplyGrade(Grade grade)
    {
        Color color = TrainingBoardStyle.GradeColor(grade);
        if (gradeText != null)
        {
            gradeText.text = TrainingBoardStyle.GradeLabel(grade);
            gradeText.color = color;
        }
        if (gradeGlow != null)
        {
            Color g = color;
            g.a = gradeGlowAlpha;
            gradeGlow.color = g;
        }
        PaintLadder(ladder, grade, litSprite, slotSprite);
        if (timeFillImage != null) timeFillImage.color = color;
        if (hasState) PaintCells(shown.hitsLeft, grade);
    }

    // 등급 사다리: 현재 등급까지 칸이 켜진다(현재 칸은 밝게, 아래 칸은 흐리게).
    public static void PaintLadder(Image[] segments, Grade grade, Sprite lit, Sprite slot)
    {
        if (segments == null) return;
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            Grade g = (Grade)i;
            bool on = g <= grade;
            Color c = on ? TrainingBoardStyle.GradeColor(g) : TrainingBoardStyle.LineDim;
            c.a = on ? (g == grade ? 1f : 0.5f) : 0.8f;
            segments[i].color = c;
            if (lit != null && slot != null) segments[i].sprite = on ? lit : slot;
        }
    }

    private void PaintCells(int hitsLeft, Grade grade)
    {
        Color on = TrainingBoardStyle.GradeColor(grade);
        for (int i = 0; i < cells.Length; i++)
        {
            if (cellFlash[i] >= 0f) continue;
            bool lit = i < hitsLeft;
            cells[i].sprite = lit ? litSprite : slotSprite;
            cells[i].color = lit ? on : TrainingBoardStyle.LineDim;
        }
    }

    // 시간 바 눈금: 더 높은 등급의 시간 조건이 끝나는 지점(남은 비율)에 그 등급 색으로 선다.
    private void BuildTimeMarks(TrainingDefinitionSO training)
    {
        if (timeMarkRoot == null || timeMarkTemplate == null) return;
        Clear(timeMarkRoot, timeMarkTemplate.transform);
        if (timeLimit <= 0f || training == null || training.rules == null) return;
        foreach (var rule in training.rules)
        {
            if (rule.grade <= Grade.Pass || rule.maxTimeSeconds < 0f || rule.maxTimeSeconds >= timeLimit) continue;
            var mark = Instantiate(timeMarkTemplate, timeMarkRoot);
            mark.gameObject.name = $"Mark_{rule.grade}";
            mark.gameObject.SetActive(true);
            float x = 1f - rule.maxTimeSeconds / timeLimit;
            var rt = mark.rectTransform;
            rt.anchorMin = new Vector2(x, rt.anchorMin.y);
            rt.anchorMax = new Vector2(x, rt.anchorMax.y);
            rt.anchoredPosition = new Vector2(0f, rt.anchoredPosition.y);
            Color c = TrainingBoardStyle.GradeColor(rule.grade);
            c.a = 0.9f;
            mark.color = c;
        }
    }

    // 피격 칸: 통과 기준 횟수만큼(1~maxCells). 그보다 많거나 제한이 없으면 숫자만 둔다.
    private void BuildCells()
    {
        if (cellRoot == null || cellTemplate == null) return;
        Clear(cellRoot, cellTemplate.transform);
        int n = hitLimit >= 1 && hitLimit <= maxCells ? hitLimit : 0;
        cells = new Image[n];
        cellFlash = new float[n];
        float width = cellRoot.rect.width, height = cellRoot.rect.height;
        float cw = n > 0 ? (width - cellGap * (n - 1)) / n : 0f;
        for (int i = 0; i < n; i++)
        {
            var cell = Instantiate(cellTemplate, cellRoot);
            cell.gameObject.name = $"Cell_{i + 1:00}";
            cell.gameObject.SetActive(true);
            var rt = cell.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(cw, height);
            rt.anchoredPosition = new Vector2(i * (cw + cellGap), 0f);
            cells[i] = cell;
            cellFlash[i] = -1f;
        }
    }

    private static void Clear(Transform root, Transform keep)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var child = root.GetChild(i);
            if (child == keep) continue;
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }
}
