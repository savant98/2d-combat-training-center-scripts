using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Wave 전환 패널 + 중앙 성적판(2026-11-10, 사용자 요청 "Wave가 넘어갈 때 현재 상황판을 화면 중앙에 Animation 효과와 함께 약 2초 띄우기.
// Wave 넘어가는 panel도 같이 리메이크, 이 경우는 기존 Animation 느낌은 살려줘야 해").
// - Wave 패널: 옛 MainScene WavePanel(WavePanelStart.anim, 2026-11-11 삭제·Git 이력)의 움직임을 그대로 쓴다. 발광 코어가 화면 왼쪽 끝에서 줄을 끌고 들어오고,
//   코어에 접혀 있던 팔이 0 → 190 → 175 → 180°로 젖혀져 걸쇠에 걸리듯 서고, 글자가 나타났다가 반대 순서로 접혀 빠진다.
//   키 값·시각·속도는 옛 Clip 그대로(1배, 2.917초, 구간마다 smoothstep)이고 모양만 PlayerHUD 사이파이 판(조준경 코어, 얇은 청록 이중선, 각진 판)으로 바꿨다.
//   다음 Wave는 옛날처럼 패널 시작 2초 뒤부터 준비되고, 패널은 그 뒤 접혀 빠진다.
// - 중앙 성적판(2026-11-11, 사용자 요청 "Wave가 넘어갈 때 피격 횟수, 남은 시간, 성적을 알려주고 있는데 화면이 빠르게 지나가니까 성적만 알려줘"):
//   훈련 기록 중일 때만 Wave 패널 아래에 현재 성적만 크게 띄운다. 가로선이 펼쳐진 뒤 세로로 열리고(0.25~0.6초), 등급 글자가 도장처럼 찍히고,
//   2.0초부터 닫혀 2.3초에 사라진다(약 2초). 팔이 접히기(2.333초) 전에 닫히고, 젖혀질 때 판 앞을 지나가지 않게 판을 팔 위에 그린다.
//   떠 있는 동안 등급이 떨어지면 글자·색·사다리가 바로 바뀌고 한 번 튄다. 남은 시간·남은 피격은 오른쪽 위 코너판에 그대로 있다.
// 시간은 WaveManager의 다음 Wave 대기(WaitForSeconds)와 맞추려고 게임 시간(Time.deltaTime)을 쓴다(옛 Animator Normal과 같다).
public class WaveTransitionPanelUI : MonoBehaviour
{
    [Header("Wave Panel")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform wavePanel;
    [SerializeField] private RectTransform supportLine;
    [SerializeField] private RectTransform core;
    [SerializeField] private RectTransform coreRing;
    [SerializeField] private Image coreGlow;
    [SerializeField] private CanvasGroup titleGroup;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI tagText;
    [SerializeField] private Image armFlash;
    [SerializeField] private Image armScan;
    [SerializeField] private FeedbackDataSO panelOnFeedback;
    [SerializeField] private float playbackSpeed = 1f;
    [SerializeField] private float coreX = -228f;
    [SerializeField] private float armWidth = 500f;

    [Header("Grade Board (훈련 기록 중일 때만)")]
    [SerializeField] private RectTransform board;
    [SerializeField] private CanvasGroup boardGroup;
    [SerializeField] private CanvasGroup boardHeaderGroup;
    [SerializeField] private TextMeshProUGUI boardHeaderText;
    [SerializeField] private TextMeshProUGUI boardGradeText;
    [SerializeField] private Image boardGradeGlow;
    [SerializeField] private CanvasGroup boardLadderGroup;
    [SerializeField] private Image[] boardLadder;            // 미흡·통과·우수·무결점 순
    [SerializeField] private Sprite ladderLitSprite;
    [SerializeField] private Sprite ladderSlotSprite;
    [SerializeField] private Image boardScan;
    [SerializeField] private float gradeGlowAlpha = 0.3f;
    [SerializeField] private float gradePunchScale = 1.35f;
    [SerializeField] private float gradePunchDuration = 0.35f;      // 실제 시간(히트스톱에도 멈추지 않게)

    // 옛 WavePanelStart.anim 키(초, 옛 Clip 시간)
    private static readonly float[] RotateTimes = { 0f, 0.5f, 1.083f, 1.167f, 1.333f, 2.333f, 2.667f };
    private static readonly float[] RotateValues = { 0f, 0f, 190f, 175f, 180f, 180f, 0f };
    private static readonly float[] MoveTimes = { 0f, 0.5f, 2.667f, 2.917f };
    private static readonly float[] TitleTimes = { 0f, 1.333f, 1.667f, 2.333f, 2.5f };
    private static readonly float[] TitleValues = { 0f, 0f, 1f, 1f, 0f };
    private static readonly float[] AlphaTimes = { 0f, 2.867f, 2.917f };
    private static readonly float[] AlphaValues = { 1f, 1f, 0f };
    public const float ClipLength = 2.917f;
    private const float PanelOnIn = 0.5f, PanelOnOut = 2.333f, LockTime = 1.333f;
    // 중앙 성적판(Clip 시간 = 실제 시간): 0.25~0.6 열림, 0.5 머리말, 0.6~0.8 등급 도장, 2.0~2.3 닫힘(약 2초)
    private const float BoardOpen = 0.25f, BoardHeader = 0.5f, BoardStamp = 0.6f, BoardClose = 2f, BoardGone = 2.3f;

    private float time = -1f;
    private bool showBoard;
    private bool firedIn, firedOut;
    private Vector3 boardScale = Vector3.one;
    private Vector2 headerBase;
    private Grade boardGrade;
    private float punch = -1f;
    private bool cached;
    private readonly float[] moveValues = new float[4];

    public bool IsPlaying => time >= 0f;
    public bool IsBoardVisible => IsPlaying && showBoard && time < BoardGone;
    // 중앙 성적판이 얼마나 떠 있는지(0~1). Wave 전환 연출이 도는 동안 한도 경고를 그만큼 흐리게 한다(10초 카운트·제한 시간 띠가 Wave 패널과 겹친다).
    public float BoardWeight => IsBoardVisible
        ? TrainingBoardMotion.Seg(time, BoardOpen, 0.6f) * (1f - TrainingBoardMotion.Seg(time, BoardClose, BoardGone)) : 0f;
    public float RealDuration => ClipLength / Mathf.Max(0.01f, playbackSpeed);
    public Grade BoardGrade => boardGrade;

    private void Awake()
    {
        Cache();
        if (time < 0f) Hide();
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        if (board != null) boardScale = board.localScale;
        if (boardHeaderGroup != null) headerBase = ((RectTransform)boardHeaderGroup.transform).anchoredPosition;
    }

    // waveNumber: 곧 시작할 Wave(1부터). grade(현재 예상 등급)는 showGradeBoard일 때만 쓴다.
    public void Play(int waveNumber, int waveTotal, bool showGradeBoard, Grade grade)
    {
        Cache();
        bool final = waveTotal >= 2 && waveNumber >= waveTotal;
        titleText.text = final ? "최종 훈련" : $"훈련 {waveNumber}";
        tagText.text = waveTotal > 0
            ? $"{(final ? "FINAL WAVE" : "WAVE")} {waveNumber:00} / {waveTotal:00}"
            : $"WAVE {waveNumber:00}";

        showBoard = showGradeBoard && board != null;
        if (board != null) board.gameObject.SetActive(showBoard);
        if (showBoard)
        {
            boardHeaderText.text = waveNumber <= 1 ? "TRAINING START" : "STATUS REPORT";
            punch = -1f;
            ApplyGrade(grade);
        }

        firedIn = firedOut = false;
        time = 0f;
        Evaluate(0f);
    }

    // 상황판(TrainingScoreboardUI)이 매 프레임 현재 예상 등급을 넘긴다. 떠 있는 동안 등급이 떨어지면 바로 바꾸고 한 번 튄다.
    public void RefreshGrade(Grade grade)
    {
        if (!IsBoardVisible || grade == boardGrade) return;
        if (grade < boardGrade) punch = 0f;
        ApplyGrade(grade);
    }

    private void ApplyGrade(Grade grade)
    {
        boardGrade = grade;
        boardGradeText.text = TrainingBoardStyle.GradeLabel(grade);
        TrainingStatusView.PaintLadder(boardLadder, grade, ladderLitSprite, ladderSlotSprite);
    }

    public void Stop()
    {
        time = -1f;
        punch = -1f;
        Hide();
    }

    private void Hide()
    {
        if (group != null) group.alpha = 0f;
        if (board != null) board.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (time < 0f) return;
        time += Time.deltaTime * playbackSpeed;
        if (punch >= 0f)
        {
            punch += Time.unscaledDeltaTime;
            if (punch >= gradePunchDuration) punch = -1f;
        }
        if (!firedIn && time >= PanelOnIn) { firedIn = true; PlayFeedback(); }
        if (!firedOut && time >= PanelOnOut) { firedOut = true; PlayFeedback(); }
        if (time >= ClipLength)
        {
            Stop();
            return;
        }
        Evaluate(time);
    }

    private void PlayFeedback()
    {
        if (panelOnFeedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.Play(panelOnFeedback, transform);
    }

    // 옛 Clip 시간 t의 모습.
    public void Evaluate(float t)
    {
        Cache();
        group.alpha = TrainingBoardMotion.Keys(t, AlphaTimes, AlphaValues);

        // 코어: 화면 왼쪽 끝(옛 키 -950, Canvas 폭 1920) → coreX, 줄은 왼쪽 끝에서 코어까지 따라온다
        float half = wavePanel.rect.width * 0.5f;
        float start = -half + 10f;
        moveValues[0] = moveValues[3] = start;
        moveValues[1] = moveValues[2] = coreX;
        float x = TrainingBoardMotion.Keys(t, MoveTimes, moveValues);
        core.anchoredPosition = new Vector2(x, 0f);
        supportLine.sizeDelta = new Vector2(Mathf.Max(0f, half + x), supportLine.sizeDelta.y);
        core.localEulerAngles = new Vector3(0f, 0f, TrainingBoardMotion.Keys(t, RotateTimes, RotateValues));
        coreRing.localEulerAngles = new Vector3(0f, 0f, -55f * t);

        Color glow = TrainingBoardStyle.Line;
        glow.a = 0.45f + 0.55f * TrainingBoardMotion.Pulse(t, 0.42f, 0.85f);
        coreGlow.color = glow;
        coreGlow.rectTransform.localScale = Vector3.one * (1f + 0.35f * TrainingBoardMotion.Pulse(t, 0.42f, 0.85f));

        titleGroup.alpha = TrainingBoardMotion.Keys(t, TitleTimes, TitleValues);

        // 팔이 걸쇠에 걸리는 순간(1.333) 테두리가 번쩍이고 빛이 팔을 훑는다
        Color f = Color.white;
        f.a = 0.9f * TrainingBoardMotion.Seg(t, LockTime - 0.04f, LockTime) * (1f - TrainingBoardMotion.Seg(t, LockTime, LockTime + 0.3f));
        armFlash.color = f;
        float sweep = TrainingBoardMotion.Seg(t, LockTime, LockTime + 0.42f);
        var scanRect = armScan.rectTransform;
        // 팔은 코어와 함께 180° 돌아 있어 팔 좌표 0(코어 쪽) → -armWidth가 화면에서 코어 → 바깥 방향이다.
        scanRect.anchoredPosition = new Vector2(Mathf.Lerp(0f, -armWidth, sweep), scanRect.anchoredPosition.y);
        Color s = TrainingBoardStyle.LineBright;
        s.a = 0.8f * TrainingBoardMotion.Pulse(t, LockTime, LockTime + 0.42f);
        armScan.color = s;

        if (showBoard) EvaluateBoard(t);
    }

    private void EvaluateBoard(float t)
    {
        float close = TrainingBoardMotion.Seg(t, BoardClose, BoardClose + 0.16f);
        float sx = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, BoardOpen, 0.42f)) * (1f - TrainingBoardMotion.Smooth(TrainingBoardMotion.Seg(t, BoardClose + 0.14f, BoardGone)));
        float sy = Mathf.Lerp(0.04f, 1f, TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0.38f, 0.6f))) * (1f - 0.96f * TrainingBoardMotion.Smooth(close));
        board.localScale = new Vector3(boardScale.x * sx, boardScale.y * sy, boardScale.z);
        boardGroup.alpha = TrainingBoardMotion.Seg(t, BoardOpen, BoardOpen + 0.04f) * (1f - TrainingBoardMotion.Seg(t, BoardGone - 0.08f, BoardGone));

        // 머리말이 왼쪽에서 들어온다
        float head = TrainingBoardMotion.Seg(t, BoardHeader, BoardHeader + 0.16f);
        boardHeaderGroup.alpha = head;
        ((RectTransform)boardHeaderGroup.transform).anchoredPosition = headerBase + new Vector2(Mathf.Lerp(-18f, 0f, TrainingBoardMotion.OutCubic(head)), 0f);

        // 등급 글자: 크게 떴다가 제 크기로 찍히며 흰색에서 등급 색이 된다. 떠 있는 동안 등급이 떨어지면 한 번 튄다(punch).
        Color gradeColor = TrainingBoardStyle.GradeColor(boardGrade);
        float stamp = TrainingBoardMotion.Seg(t, BoardStamp, BoardStamp + 0.2f);
        float kick = punch >= 0f ? 1f - TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(punch, 0f, gradePunchDuration)) : 0f;
        boardGradeText.rectTransform.localScale = Vector3.one * (Mathf.Lerp(1.7f, 1f, TrainingBoardMotion.OutBack(stamp)) * (1f + (gradePunchScale - 1f) * kick));
        Color text = Color.Lerp(Color.white, gradeColor, Mathf.Max(TrainingBoardMotion.Seg(t, BoardStamp + 0.04f, BoardStamp + 0.3f) - kick, 0f));
        text.a = TrainingBoardMotion.Seg(t, BoardStamp, BoardStamp + 0.04f);
        boardGradeText.color = text;
        Color textGlow = gradeColor;
        textGlow.a = Mathf.Clamp01(gradeGlowAlpha * (TrainingBoardMotion.Seg(t, BoardStamp, BoardStamp + 0.08f)
            * (1f + 1.6f * TrainingBoardMotion.Pulse(t, BoardStamp, BoardStamp + 0.45f)) + 2f * kick));
        boardGradeGlow.color = textGlow;
        boardLadderGroup.alpha = TrainingBoardMotion.Seg(t, BoardStamp + 0.12f, BoardStamp + 0.3f);

        // 열린 직후 위에서 아래로 한 번 훑는 빛
        float scan = TrainingBoardMotion.Seg(t, 0.6f, 1.05f);
        float h = board.rect.height;
        boardScan.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, -h, scan));
        Color s = TrainingBoardStyle.Line;
        s.a = 0.6f * TrainingBoardMotion.Pulse(t, 0.6f, 1.05f);
        boardScan.color = s;
    }
}
