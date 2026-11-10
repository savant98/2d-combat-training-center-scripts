using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 성적이 곧 미흡이 될 때의 경고(2026-11-10, 사용자 요청 "곧 미흡에 도달할 것 같을 때도 화면에 크게 알려줘야 해.
// 시간이 1분 남았을 때 1분 경고, 30초에 한 번, 10초부터는 매 초 세도록. 피격의 경우 5번 남으면 계속 띄워줘").
// - 제한 시간: 남은 시간이 60초·30초를 지나는 순간 띠가 잠깐 뜨고, 10초부터는 큰 숫자가 매 초 찍힌다.
// - 피격 한도: 남은 피격이 5 이하인 동안 화면 위쪽 띠가 계속 켜져 있고, 맞을 때마다 번쩍인다.
//   화면 네 변의 붉은 빛(DangerVignetteUI)은 남은 피격이 1 이하일 때만 켠다(2026-11-11, 사용자 요청 "피격 횟수가 5회 미만일 때
//   나오는 vignette 효과는 1회 남았을 때 나오게 하고, hp 30% 이하일 때 나오는 건 유지해").
// - 소리(2026-11-11, 사용자 요청 "새로운 Feedback을 만들어서 사용해. 간단한 SCI-FI ui 소리, 너무 튀면 안 돼. 시간의 경우 1분 남았을 때 한번,
//   30초 남았을 때 한번, 10~0초에 시계가 째깍거리는 소리를 넣어서 압박을 줘"): 띠가 뜰 때 짧은 경고음, 제한 시간 띠에는 째깍 한 번이 뒤따르고,
//   10초부터는 매 초 째·깍이 번갈아 울리다가 마지막 3초는 반 초마다 울린다. 0초에는 등급 하락 알림(미흡)의 소리가 이어받는다.
// 값은 상황판(TrainingScoreboardUI)이 매 프레임 넘기는 읽기 전용 수치(게임 시간 기준)이고, 연출은 히트스톱에도 멈추지 않게 실제 시간으로 진행한다.
// 등급 하락 알림·Wave 전환 성적판이 떠 있는 동안에는 흐려져 자리를 비켜 준다.
public class TrainingLimitWarningUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;

    [Header("Hit Limit (남은 피격 5 이하 동안 계속)")]
    [SerializeField] private CanvasGroup hitGroup;
    [SerializeField] private RectTransform hitBand;
    [SerializeField] private Image hitFlash;
    [SerializeField] private TextMeshProUGUI hitTitle;
    [SerializeField] private TextMeshProUGUI hitCountText;
    [SerializeField] private DangerVignetteUI vignette;

    [Header("Time Limit")]
    [SerializeField] private CanvasGroup noticeGroup;
    [SerializeField] private RectTransform noticeBand;
    [SerializeField] private Image noticeFlash;
    [SerializeField] private TextMeshProUGUI noticeText;
    [SerializeField] private CanvasGroup countGroup;
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private RectTransform countRing;
    [SerializeField] private Image countRingImage;

    [Header("Rules")]
    [SerializeField] private int hitThreshold = 5;
    [SerializeField] private int vignetteHitThreshold = 1;      // 화면 네 변의 빛은 남은 피격이 이 값 이하일 때만
    [SerializeField] private float firstTimeWarning = 60f;
    [SerializeField] private float secondTimeWarning = 30f;
    [SerializeField] private int countdownFrom = 10;
    [SerializeField] private float noticeDuration = 2.4f;
    [SerializeField] private float dimmedAlpha = 0.18f;

    [Header("Feedback")]
    [SerializeField] private FeedbackDataSO warningFeedback;        // 띠가 뜰 때의 짧은 경고음
    [SerializeField] private FeedbackDataSO clockTickFeedback;      // 째
    [SerializeField] private FeedbackDataSO clockTockFeedback;      // 깍(같은 소리를 낮게)
    [SerializeField] private float noticeClockDelay = 0.45f;        // 제한 시간 띠가 뜬 뒤 째깍까지(실제 시간)
    [SerializeField] private float clockHalfInterval = 0.5f;        // 째 → 깍 간격
    [SerializeField] private int fastCountFrom = 3;                 // 이 초부터는 반 초마다 울린다

    private bool started;
    private float prevTimeLeft;
    private int shownHits = -1;
    private bool hitVisible;
    private float hitIntro = -1f, hitPulse = -1f;
    private float noticeTime = -1f;
    private int countValue;
    private float countPulse = -1f;
    private float dim;
    private float clickTimer;
    private int clicksLeft;
    private bool nextIsTock;

    public bool IsHitWarningVisible => hitVisible;
    public bool IsNoticePlaying => noticeTime >= 0f;
    public int CountdownValue => countValue;
    public float NoticeDuration => noticeDuration;
    public int WarningSoundCount { get; private set; }      // 판 시작부터 낸 경고음·째깍 횟수(검사용)
    public int ClockSoundCount { get; private set; }

    private void Awake()
    {
        if (!started) HideAll();
    }

    // 판 시작: 상태를 비운다. 시작할 때 이미 지나 있는 시간 경고는 건너뛴다.
    public void Begin()
    {
        started = false;
        shownHits = -1;
        hitVisible = false;
        hitIntro = hitPulse = noticeTime = countPulse = -1f;
        countValue = 0;
        dim = 0f;
        clicksLeft = 0;
        nextIsTock = false;
        WarningSoundCount = ClockSoundCount = 0;
        HideAll();
    }

    public void Stop()
    {
        Begin();
    }

    // timeLeft: 통과 등급을 잃기까지 남은 시간(제한 없으면 음수). hitsLeft: 더 맞아도 되는 횟수(제한 없으면 음수).
    // dimWeight: 다른 알림이 떠 있는 정도(0~1).
    public void Tick(float timeLeft, int hitsLeft, float dimWeight)
    {
        dim = Mathf.Clamp01(dimWeight);

        if (timeLeft >= 0f)
        {
            if (!started) prevTimeLeft = timeLeft;
            if (prevTimeLeft > firstTimeWarning && timeLeft <= firstTimeWarning) ShowNotice(firstTimeWarning);
            else if (prevTimeLeft > secondTimeWarning && timeLeft <= secondTimeWarning) ShowNotice(secondTimeWarning);
            prevTimeLeft = timeLeft;

            int count = timeLeft > 0f && timeLeft <= countdownFrom ? Mathf.CeilToInt(timeLeft - 0.0001f) : 0;
            if (count != countValue)
            {
                countValue = count;
                if (count > 0)
                {
                    countText.text = count.ToString();
                    countPulse = 0f;
                    noticeTime = -1f;       // 카운트가 시작되면 띠 경고는 걷는다
                    // 매 초 째·깍을 번갈아 울리고, 마지막 3초는 반 초 뒤에 한 번 더 울려 두 배로 빨라진다.
                    PlayClock();
                    clicksLeft = count <= fastCountFrom ? 1 : 0;
                    clickTimer = clockHalfInterval;
                }
            }
        }
        else countValue = 0;

        bool visible = hitsLeft >= 0 && hitsLeft <= hitThreshold;
        if (visible)
        {
            if (!hitVisible)
            {
                hitIntro = 0f;
                if (started) PlayWarning();      // 판을 시작할 때 이미 한도 근처인 훈련은 소리 없이 띠만 켠다
            }
            if (hitsLeft != shownHits)
            {
                if (hitVisible && hitsLeft < shownHits)
                {
                    hitPulse = 0f;
                    if (vignette != null && hitsLeft <= vignetteHitThreshold) vignette.Pulse();
                    if (hitsLeft == 0) PlayWarning();       // "다음 피격 시 훈련 실패"로 바뀌는 순간
                }
                shownHits = hitsLeft;
                hitCountText.text = hitsLeft.ToString();
                hitTitle.text = hitsLeft > 0 ? "피격 한도 임박" : "다음 피격 시 훈련 실패";
            }
        }
        hitVisible = visible;
        if (vignette != null) vignette.SetLimitWarning(hitVisible && shownHits <= vignetteHitThreshold, shownHits);
        started = true;
    }

    private void ShowNotice(float seconds)
    {
        int s = Mathf.RoundToInt(seconds);
        string label = s >= 60 && s % 60 == 0 ? $"{s / 60}분" : $"{s}초";
        noticeText.text = $"제한 시간 <color=#ffd166><size=135%>{label}</size></color> 남음";
        noticeTime = 0f;
        // 경고음 뒤에 시계가 한 번 째깍거린다.
        PlayWarning();
        nextIsTock = false;
        clicksLeft = 2;
        clickTimer = noticeClockDelay;
    }

    private void PlayWarning()
    {
        WarningSoundCount++;
        Play(warningFeedback);
    }

    private void PlayClock()
    {
        ClockSoundCount++;
        Play(nextIsTock ? clockTockFeedback : clockTickFeedback);
        nextIsTock = !nextIsTock;
    }

    private void Play(FeedbackDataSO feedback)
    {
        if (feedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.Play(feedback, transform);
    }

    private void Update()
    {
        if (!started) return;
        float dt = Time.unscaledDeltaTime;
        if (hitIntro >= 0f) { hitIntro += dt; if (hitIntro > 0.3f) hitIntro = -1f; }
        if (hitPulse >= 0f) { hitPulse += dt; if (hitPulse > 0.4f) hitPulse = -1f; }
        if (noticeTime >= 0f) { noticeTime += dt; if (noticeTime >= noticeDuration) noticeTime = -1f; }
        if (countPulse >= 0f) countPulse += dt;
        // 예약된 째깍은 일시정지 동안 미룬다(카운트는 게임 시간이라 멈춰 있는데 메뉴 위에서 시계만 가면 어긋난다). 히트스톱에는 멈추지 않는다.
        bool paused = GameTimeController.Instance != null && GameTimeController.Instance.IsPaused;
        if (clicksLeft > 0 && !paused)
        {
            clickTimer -= dt;
            if (clickTimer <= 0f)
            {
                clicksLeft--;
                clickTimer += clockHalfInterval;
                PlayClock();
            }
        }
        Apply();
    }

    // 지금 상태의 모습을 그린다.
    public void Apply()
    {
        group.alpha = Mathf.Lerp(1f, dimmedAlpha, dim);

        // 피격 한도: 띠가 가로로 열리며 나타나고, 맞을 때마다 튀며 번쩍인다(남은 피격 1 이하의 화면 네 변 빛은 DangerVignetteUI가 그린다).
        float intro = hitIntro >= 0f ? TrainingBoardMotion.Seg(hitIntro, 0f, 0.22f) : 1f;
        float pulse = hitPulse >= 0f ? 1f - TrainingBoardMotion.Seg(hitPulse, 0f, 0.4f) : 0f;
        hitGroup.alpha = hitVisible ? intro : 0f;
        float open = Mathf.Lerp(0.55f, 1f, TrainingBoardMotion.OutBack(intro));
        hitBand.localScale = new Vector3(open * (1f + 0.1f * pulse), 1f + 0.16f * pulse, 1f);
        Color flash = TrainingBoardStyle.Warning;
        flash.a = hitVisible ? Mathf.Max(0.9f * pulse, hitIntro >= 0f ? 0.8f * (1f - intro) : 0f) : 0f;
        hitFlash.color = flash;
        hitCountText.rectTransform.localScale = Vector3.one * (1f + 0.45f * pulse);
        hitCountText.color = Color.Lerp(shownHits <= 1 ? (Color)TrainingBoardStyle.Warning : Color.white, Color.white, pulse);


        // 제한 시간 띠: 가로로 열려 번쩍이고 잠시 머문 뒤 걷힌다.
        if (noticeTime >= 0f)
        {
            float t = noticeTime;
            float exit = TrainingBoardMotion.Smooth(TrainingBoardMotion.Seg(t, noticeDuration - 0.3f, noticeDuration));
            noticeGroup.alpha = TrainingBoardMotion.Seg(t, 0f, 0.05f) * (1f - exit);
            float sx = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0f, 0.14f));
            float sy = Mathf.Lerp(0.1f, 1f, TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0.08f, 0.22f))) * (1f - 0.9f * exit);
            noticeBand.localScale = new Vector3(sx, sy, 1f);
            Color nf = TrainingBoardStyle.Warning;
            nf.a = 0.9f * TrainingBoardMotion.Seg(t, 0f, 0.04f) * (1f - TrainingBoardMotion.Seg(t, 0.06f, 0.45f));
            noticeFlash.color = nf;
        }
        else noticeGroup.alpha = 0f;

        // 10초 카운트: 매 초 큰 숫자가 찍히고(크게 → 제 크기) 고리가 퍼진다. 3초부터 붉어진다.
        if (countValue > 0)
        {
            float t = Mathf.Max(0f, countPulse);
            float stamp = TrainingBoardMotion.Seg(t, 0f, 0.22f);
            countGroup.alpha = 1f;
            countText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.7f, 1f, TrainingBoardMotion.OutBack(stamp));
            Color c = countValue <= 3 ? (Color)TrainingBoardStyle.Warning : (Color)TrainingBoardStyle.Gold;
            c = Color.Lerp(Color.white, c, TrainingBoardMotion.Seg(t, 0.02f, 0.2f));
            c.a = Mathf.Lerp(1f, 0.62f, TrainingBoardMotion.Seg(t, 0.3f, 0.8f));
            countText.color = c;
            float burst = TrainingBoardMotion.Seg(t, 0f, 0.55f);
            Color ring = countValue <= 3 ? (Color)TrainingBoardStyle.Warning : (Color)TrainingBoardStyle.Gold;
            ring.a = 0.75f * (1f - burst);
            countRingImage.color = ring;
            countRing.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.25f, TrainingBoardMotion.OutCubic(burst));
        }
        else countGroup.alpha = 0f;
    }

    private void HideAll()
    {
        if (group != null) group.alpha = 1f;
        if (hitGroup != null) hitGroup.alpha = 0f;
        if (noticeGroup != null) noticeGroup.alpha = 0f;
        if (countGroup != null) countGroup.alpha = 0f;
        if (hitFlash != null) hitFlash.color = Color.clear;
        if (vignette != null) vignette.SetLimitWarning(false, 0);
    }
}
