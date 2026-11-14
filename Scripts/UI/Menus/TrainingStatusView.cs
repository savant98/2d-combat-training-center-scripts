using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 훈련 상황 한 장(2026-11-10 TrainingRoom 상황판 리메이크). 오른쪽 위 코너판이 쓴다.
// (Wave 전환 때의 중앙판도 같은 배치를 썼으나 2026-11-11에 성적만 보여 주는 판으로 바뀌어 WaveTransitionPanelUI가 직접 그린다.)
// 행: 현재 성적(등급 글자 + 등급 사다리 4칸) / 남은 시간(등급이 시간으로 떨어지는 지점에 눈금이 있는 바) / 남은 피격(비스듬한 칸).
// 켜진 칸·바·등급 글자는 현재 등급 색이라, 등급이 떨어지면 판 전체의 색이 바뀐다. 표시만 하고 게임 상태는 바꾸지 않는다.
// 보스 HP 바가 켜진 동안에는 오른쪽 위 구석의 간략판(등급 글자 / 시간·피격 숫자)으로 접는다(2026-11-15 사용자 요청 "보스 HUD와 겹쳐
// 현재 성적을 아래로 내린 것 때문에 화면이 복잡해졌다. 보스 전투 중에는 간략화"). 판(9-slice)은 Scale로 줄이지 않고 오른쪽 위 기준으로 크기만 줄인다.
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

    [Header("Compact (보스 전투 중 간략판)")]
    [SerializeField] private RectTransform plate;           // 9-slice 판(오른쪽 위 기준 고정 크기). 접을 때 크기만 줄인다
    [SerializeField] private CanvasGroup[] fullRows;        // 원래 판의 행(머리말·현재 성적·남은 시간·남은 피격)
    [SerializeField] private CanvasGroup compactGroup;      // 간략판(오른쪽 위 구석). 이 사각형의 크기가 접힌 판의 크기다
    [SerializeField] private TextMeshProUGUI compactGradeText;
    [SerializeField] private TextMeshProUGUI compactTimeText;
    [SerializeField] private TextMeshProUGUI compactHitText;
    [SerializeField] private float compactDuration = 0.35f; // 실제 시간

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

    // 접기 전환: 행이 먼저 사라지고(앞 0.15초) 판이 오른쪽 위 구석으로 줄며(0.05~0.35초, OutCubic) 간략판이 뜬다(뒤 0.15초).
    // 펼 때는 반대 순서다. 도중에 방향이 바뀌면 지금 모습에서 이어 간다.
    private const float CompactFadeShare = 0.15f / 0.35f, CompactSizeDelayShare = 0.05f / 0.35f;
    private bool compact;
    private float compactTime = -1f;
    private float rowsAlpha = 1f, compactAlpha;
    private float rowsFrom = 1f, compactFrom;
    private Vector2 plateSize, plateFrom;

    public RectTransform Rect => (RectTransform)transform;
    // 보스 바가 켜져 간략판으로 접혔거나 접히는 중인지(목표 상태)
    public bool IsCompact => compact;
    public bool IsCompactTransitioning => compactTime >= 0f;
    // 지금 향하는 상태의 판 크기(접힘이면 간략판, 아니면 원래 판). TrainingScoreboardUI가 보스 바와의 겹침을 이 크기로 본다.
    public Vector2 TargetSize => compact ? CompactSize : FullSize;
    private Vector2 FullSize => Rect.rect.size;
    private Vector2 CompactSize => compactGroup != null ? ((RectTransform)compactGroup.transform).rect.size : FullSize;
    private bool HasCompact => plate != null && compactGroup != null;

    private void Awake()
    {
        plateSize = FullSize;
    }

    // 보스 바가 켜진 동안 오른쪽 위 구석의 간략판으로 접는다(TrainingScoreboardUI.SetBossBarArea가 부른다). Configure·Refresh는 이 상태를 바꾸지 않는다.
    // instant면 연출 없이 바로 바꾼다(상황판이 보이지 않을 때). 같은 상태를 다시 받으면 진행 중인 전환을 이어 간다.
    // 판·간략판이 연결되지 않은 Prefab이면 원래 판 그대로 둔다(TargetSize가 원래 크기라 보스 바 아래로 비키는 옛 동작이 남는다).
    public void SetCompact(bool on, bool instant)
    {
        if (!HasCompact) return;
        if (on != compact)
        {
            compact = on;
            rowsFrom = rowsAlpha;
            compactFrom = compactAlpha;
            plateFrom = plateSize;
            compactTime = 0f;
        }
        if (!instant) return;
        compactTime = -1f;
        rowsAlpha = on ? 0f : 1f;
        compactAlpha = on ? 1f : 0f;
        plateSize = TargetSize;
        ApplyCompact();
    }

    // 접기·펼치기 전환을 dt(실제 시간)만큼 진행한다. Update가 부르고, 검증은 직접 불러 정해진 시각의 모습을 본다.
    public void StepCompact(float dt)
    {
        if (compactTime < 0f) return;
        compactTime += dt;
        float t = compactDuration > 0f ? compactTime / compactDuration : 1f;
        float leave = TrainingBoardMotion.Seg(t, 0f, CompactFadeShare);         // 먼저 사라지는 쪽
        float enter = TrainingBoardMotion.Seg(t, 1f - CompactFadeShare, 1f);    // 나중에 뜨는 쪽
        float size = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, CompactSizeDelayShare, 1f));
        if (compact)
        {
            rowsAlpha = Mathf.Lerp(rowsFrom, 0f, leave);
            compactAlpha = Mathf.Lerp(compactFrom, 1f, enter);
        }
        else
        {
            compactAlpha = Mathf.Lerp(compactFrom, 0f, leave);
            rowsAlpha = Mathf.Lerp(rowsFrom, 1f, enter);
        }
        plateSize = Vector2.Lerp(plateFrom, TargetSize, size);
        ApplyCompact();
        if (t >= 1f) compactTime = -1f;
    }

    private void ApplyCompact()
    {
        if (plate != null) plate.sizeDelta = plateSize;
        if (fullRows != null)
            foreach (var row in fullRows)
                if (row != null) row.alpha = rowsAlpha;
        if (compactGroup != null) compactGroup.alpha = compactAlpha;
    }

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
        if (compactHitText != null) compactHitText.color = Color.white;
        if (compactTimeText != null) compactTimeText.color = Color.white;
        if (compactGradeText != null) compactGradeText.rectTransform.localScale = Vector3.one;
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
            string time = MonoOpen + TrainingBoardStyle.FormatTime(s.timeSeconds, timeLimit >= 0f);
            if (timeText != null) timeText.text = time;
            if (compactTimeText != null) compactTimeText.text = time;
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
            string hits = hitLimit < 0 ? "-" : MonoOpen + s.hitsLeft;
            if (hitText != null) hitText.text = hits;
            if (compactHitText != null) compactHitText.text = hits;
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
            Color c = hitNow ? (Color)TrainingBoardStyle.Warning : Color.white;
            if (hitText != null) hitText.color = c;
            if (compactHitText != null) compactHitText.color = c;
        }
        bool timeNow = timeLimit > 0f && s.timeSeconds <= dangerSeconds;
        if (timeNow != timeDanger)
        {
            timeDanger = timeNow;
            Color c = timeNow ? (Color)TrainingBoardStyle.Warning : Color.white;
            if (timeText != null) timeText.color = c;
            if (compactTimeText != null) compactTimeText.color = c;
        }

        if ((first || s.waveNumber != shown.waveNumber || s.waveTotal != shown.waveTotal) && waveText != null)
            waveText.text = s.waveTotal > 0 ? $"WAVE {Mathf.Max(1, s.waveNumber):00} / {s.waveTotal:00}" : "";

        shown = s;
        hasState = true;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        StepCompact(dt);

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

        if (punch >= 0f)
        {
            punch += dt;
            float k = TrainingBoardMotion.Seg(punch, 0f, gradePunchDuration);
            Vector3 scale = Vector3.one * Mathf.Lerp(gradePunchScale, 1f, TrainingBoardMotion.OutBack(k));
            if (gradeText != null) gradeText.rectTransform.localScale = scale;
            if (compactGradeText != null) compactGradeText.rectTransform.localScale = scale;   // 간략판 등급 글자도 같이 튄다
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
        if (compactGradeText != null)
        {
            compactGradeText.text = TrainingBoardStyle.GradeLabel(grade);
            compactGradeText.color = color;
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
