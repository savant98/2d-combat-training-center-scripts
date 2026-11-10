using DG.Tweening;
using UnityEngine;

// TrainingRoom 실시간 상황판(2026-11-10 리메이크, 사용자 요청 "오른쪽 위 실시간 상황판을 현재 PlayerHUD 디자인을 참고해 리메이크.
// 현재 성적이 떨어지면 화면에 커다랗게 표시. Wave가 넘어갈 때 현재 상황판을 화면 중앙에 Animation 효과와 함께 약 2초").
// 이 Prefab(MainScene의 TrainingScoreboard 인스턴스, UIManager.trainingScoreboardUI)이 훈련 중 화면 연출을 함께 맡는다.
// - 오른쪽 위 코너판(TrainingStatusView): 현재 성적·남은 시간·남은 피격·Wave 진행. 켜질 때 오른쪽에서 밀려 들어온다.
// - 등급 하락 알림(TrainingGradeAlertUI): TrainingRunTracker의 읽기 전용 예상 등급이 떨어지면 띄운다(게임 상태는 바꾸지 않는다).
// - 한도 경고(TrainingLimitWarningUI): 곧 미흡이 될 때(남은 시간 1분·30초·10초 카운트, 남은 피격 5 이하) 화면에 크게 알린다.
// - 위험 빛(DangerVignetteUI): 남은 피격이 1 이하이거나 체력 30% 이하일 때 화면 네 변이 붉게 숨 쉰다. 훈련 기록이 없는 방에서도 체력 조건으로 뜬다.
// - Wave 전환(WaveTransitionPanelUI): UIManager.StartWavePanelEffect가 PlayWaveTransition을 부른다. 훈련 기록이 없는 Wave
//   (Nightmare 등)에서도 Wave 패널은 나와야 하므로 알림·Wave 전환은 루트 CanvasGroup(코너판 표시)을 따르지 않는다(ignoreParentGroups).
// MainScene 인스턴스는 옛 루트 배치(가운데 100×100, pivot 1,1)와 시작 알파 0을 재정의로 들고 있다. 알파는 그대로 쓰고, 배치는 Awake에서 Canvas 전체로 편다.
public class TrainingScoreboardUI : MonoBehaviour
{
    [Header("Scoreboard")]
    [SerializeField] private CanvasGroup scoreBoardCanvasGroup;
    [SerializeField] private TrainingStatusView cornerView;
    [SerializeField] private float fadeDuration = 0.6f;
    [SerializeField] private float bootSlide = 70f;
    [SerializeField] private float bootDuration = 0.45f;
    [SerializeField] private float bossBarMargin = 20f;
    [SerializeField] private float avoidDuration = 0.35f;

    [Header("Alerts")]
    [SerializeField] private TrainingGradeAlertUI gradeAlert;
    [SerializeField] private WaveTransitionPanelUI waveTransition;
    [SerializeField] private TrainingLimitWarningUI limitWarning;
    [SerializeField] private DangerVignetteUI dangerVignette;

    private TrainingRunTracker tracker;
    private bool scoreBoardOn;
    private Grade lastGrade;
    private int hitLimit = -1;
    private float timeLimit = -1f;
    private int waveTotal, waveNumber;
    private Vector2 cornerBase;
    private float bootTime = -1f;
    private RectTransform bossBar;
    private float avoidFrom, avoidTo, avoidOffset;
    private float avoidTime = -1f;
    private const float BossWarningDuration = 3f;      // 보스 등장 경고(WarningLineEffect) 길이, 게임 시간
    private float bossWarningUntil = -1f;

    public bool IsRunActive => scoreBoardOn && tracker != null && tracker.IsRunActive;
    public TrainingGradeAlertUI GradeAlert => gradeAlert;
    public WaveTransitionPanelUI WaveTransition => waveTransition;
    public TrainingLimitWarningUI LimitWarning => limitWarning;
    public DangerVignetteUI DangerVignette => dangerVignette;

    private void Awake()
    {
        var rt = (RectTransform)transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;
        if (cornerView != null) cornerBase = cornerView.Rect.anchoredPosition;
    }

    public void ShowTrainingScoreboard(TrainingRunTracker tracker)
    {
        this.tracker = tracker;
        scoreBoardOn = true;

        var training = tracker.TrainingDefinition;
        TrainingBoardStyle.GetPassLimits(training, out hitLimit, out timeLimit);
        ReadWave();
        cornerView.Configure(training);

        var snapshot = BuildSnapshot();
        lastGrade = snapshot.grade;
        cornerView.Refresh(snapshot);
        if (limitWarning != null) limitWarning.Begin();
        bossWarningUntil = -1f;

        bootTime = 0f;
        scoreBoardCanvasGroup.DOKill();
        scoreBoardCanvasGroup.DOFade(1f, fadeDuration).SetUpdate(true);
    }

    public void HideTrainingScoreboard()
    {
        scoreBoardOn = false;
        if (limitWarning != null) limitWarning.Stop();
        // 패배로 판이 끝났으면 진행 중이던 Wave 전환 패널과 중앙 성적판을 걷는다(사망 연출 동안 멈춘 채 남아 화면을 가리지 않게).
        if (waveTransition != null && tracker != null && tracker.LastFailReason != TrainingFailReason.None)
            waveTransition.Stop();
        // 성적이 미흡이 되어 판이 끝났으면(자동 사망) 그 순간을 등급 하락 알림으로 알려 왜 패배했는지 보이게 한다.
        if (gradeAlert != null && tracker != null && lastGrade > Grade.Poor
            && (tracker.LastFailReason == TrainingFailReason.HitLimit || tracker.LastFailReason == TrainingFailReason.TimeLimit))
        {
            gradeAlert.Play(lastGrade, Grade.Poor);
            lastGrade = Grade.Poor;
        }
        scoreBoardCanvasGroup.DOKill();
        scoreBoardCanvasGroup.DOFade(0f, fadeDuration).SetUpdate(true);
    }

    // 보스 HP 바(UIManager.ShowBossHP)가 켜지면 그 사각형을, 꺼지면 null을 받는다.
    // 보스 바는 Canvas 가운데 기준이라 16:9·16:10에서는 오른쪽 위 코너판과 겹친다. 겹칠 때만 코너판을 바 아래로 내린다.
    public void SetBossBarArea(RectTransform bar)
    {
        bossBar = bar;
        Retarget(AvoidTarget());
    }

    // 보스 등장 경고(UIManager.ShowWarningLine)가 시작될 때 부른다. 경고 글자판이 한도 경고와 같은 자리라 그동안 한도 경고를 흐리게 한다.
    public void NotifyBossWarning()
    {
        bossWarningUntil = Time.time + BossWarningDuration;
    }

    // UIManager.StartWavePanelEffect에서 부른다. waveNumber는 곧 시작할 Wave(1부터).
    // 훈련 기록 중이면 Wave 패널 아래에 현재 성적만 크게 띄운다(남은 시간·피격은 코너판에 그대로 있다).
    public bool PlayWaveTransition(int waveNumber)
    {
        if (waveTransition == null) return false;
        int total = 0;
        var manager = WaveManager.ActiveInstance;
        if (manager != null && manager.Data != null && manager.Data.waves != null) total = manager.Data.waves.Length;

        bool board = IsRunActive;
        waveTransition.Play(waveNumber, total, board, board ? tracker.CurrentPredictedGrade : Grade.Poor);
        return true;
    }

    private void Update()
    {
        if (cornerView != null)
        {
            float slide = 0f;
            if (bootTime >= 0f)
            {
                bootTime += Time.unscaledDeltaTime;
                float k = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(bootTime, 0f, bootDuration));
                slide = bootSlide * (1f - k);
                if (k >= 1f) bootTime = -1f;
            }
            if (bossBar != null) Retarget(AvoidTarget());     // 해상도가 바뀌어도 다시 맞춘다
            if (avoidTime >= 0f)
            {
                avoidTime += Time.unscaledDeltaTime;
                float k = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(avoidTime, 0f, avoidDuration));
                avoidOffset = Mathf.Lerp(avoidFrom, avoidTo, k);
                if (k >= 1f) avoidTime = -1f;
            }
            cornerView.Rect.anchoredPosition = cornerBase + new Vector2(slide, avoidOffset);
        }

        if (!IsRunActive) return;

        var snapshot = BuildSnapshot();
        cornerView.Refresh(snapshot);
        if (waveTransition != null) waveTransition.RefreshGrade(snapshot.grade);
        if (limitWarning != null)
        {
            // 등급 하락 알림·Wave 전환 성적판·보스 등장 경고가 떠 있는 동안에는 경고를 흐리게 해 자리를 비킨다.
            float busy = Mathf.Max(gradeAlert != null && gradeAlert.IsPlaying ? 1f : 0f, waveTransition != null ? waveTransition.BoardWeight : 0f);
            if (Time.time < bossWarningUntil) busy = 1f;
            limitWarning.Tick(timeLimit >= 0f ? snapshot.timeSeconds : -1f, snapshot.hitsLeft, busy);
        }

        // 예상 등급은 피격 수와 경과 시간에만 따라 내려간다(Begin에서 다시 시작하면 Show가 기준을 새로 잡는다).
        if (snapshot.grade < lastGrade && gradeAlert != null)
            gradeAlert.Play(lastGrade, snapshot.grade);
        lastGrade = snapshot.grade;
    }

    private TrainingStatusSnapshot BuildSnapshot()
    {
        float elapsed = tracker.CurrentTimeSeconds;
        ReadWave();
        return new TrainingStatusSnapshot
        {
            grade = tracker.CurrentPredictedGrade,
            timeSeconds = timeLimit >= 0f ? Mathf.Max(0f, timeLimit - elapsed) : elapsed,
            hitsLeft = hitLimit >= 0 ? Mathf.Max(0, hitLimit - tracker.CurrentHItCount) : -1,
            waveNumber = waveNumber,
            waveTotal = waveTotal,
        };
    }

    private void Retarget(float target)
    {
        if (Mathf.Abs(target - avoidTo) < 0.5f) return;
        avoidFrom = avoidOffset;
        avoidTo = target;
        avoidTime = 0f;
    }

    // 코너판이 원래 자리에서 보스 바(+여백)와 겹치면 판 위쪽 끝이 바 아래로 가는 만큼(음수), 아니면 0.
    private float AvoidTarget()
    {
        if (bossBar == null || cornerView == null) return 0f;
        var space = (RectTransform)transform;
        var corners = new Vector3[4];
        bossBar.GetWorldCorners(corners);
        Vector2 barMin = space.InverseTransformPoint(corners[0]), barMax = space.InverseTransformPoint(corners[2]);
        var view = cornerView.Rect;
        Vector2 size = Vector2.Scale(view.rect.size, view.localScale);
        float top = space.rect.yMax + cornerBase.y, right = space.rect.xMax + cornerBase.x;
        float bottom = top - size.y, left = right - size.x;
        bool overlapX = left < barMax.x + bossBarMargin && right > barMin.x - bossBarMargin;
        bool overlapY = bottom < barMax.y + bossBarMargin && top > barMin.y - bossBarMargin;
        return overlapX && overlapY ? (barMin.y - bossBarMargin) - top : 0f;
    }

    // 진행 중인 WaveManager에서 Wave 진행을 읽는다. 모든 Wave가 끝나 ActiveInstance가 비면 마지막 값을 유지한다.
    private void ReadWave()
    {
        var manager = WaveManager.ActiveInstance;
        if (manager == null || manager.Data == null || manager.Data.waves == null || manager.Data.waves.Length == 0) return;
        waveTotal = manager.Data.waves.Length;
        waveNumber = Mathf.Min(Mathf.Clamp(manager.currentWaveIndex, 0, waveTotal) + 1, waveTotal);
    }
}
