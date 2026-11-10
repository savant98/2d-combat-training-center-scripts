using UnityEngine;
using UnityEngine.UI;

// 모든 Wave를 클리어했을 때의 클리어 패널(2026-11-10, 사용자 요청 "모든 Wave를 클리어했을 때의 클리어 연출도 신규 디자인에 맞게 전부").
// Wave 전환 패널과 같은 기계 언어로 짠다. 조준경 코어가 화면 위에서 줄을 끌고 내려오고, 위로 세워져 있던 팔이 진자처럼
// 90 → -10 → 5 → 0°로 떨어져 서면 "훈련 완료"가 찍히고 테두리가 번쩍이며 고리가 퍼진다. 2초 뒤 팔이 다시 접히며 빠진다.
// 움직임 키는 옛 MainScene TrainingCompletePanel(TrainingCompleteStart.anim, 2.667초, 2026-11-11 삭제·Git 이력)과 같다.
// Room.WaveCompleteRoutine이 이 패널 시작 2초 뒤 결과 보드를 열고(옛 Clip의 접힘 시작과 같은 시점), 결과 보드가 패널 위에 그려진다.
// 시간은 Room의 대기(WaitForSeconds)와 맞추려고 게임 시간을 쓴다.
public class TrainingClearPanelUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform rig;
    [SerializeField] private RectTransform cable;
    [SerializeField] private RectTransform core;
    [SerializeField] private RectTransform coreRing;
    [SerializeField] private Image coreGlow;
    [SerializeField] private CanvasGroup textGroup;
    [SerializeField] private RectTransform titleRoot;
    [SerializeField] private Image armFlash;
    [SerializeField] private Image armScan;
    [SerializeField] private Image burstRing;
    [SerializeField] private FeedbackDataSO completeFeedback;
    [SerializeField] private FeedbackDataSO panelOnFeedback;
    [SerializeField] private float coreX = -410f;
    [SerializeField] private float coreY = 75f;
    [SerializeField] private float armWidth = 820f;

    // 옛 TrainingCompleteStart.anim 키(초)
    private static readonly float[] MoveTimes = { 0f, 0.5f, 2.333f, 2.667f };
    private static readonly float[] RotateTimes = { 0f, 0.5f, 0.583f, 0.833f, 1f, 1.167f, 2f, 2.333f };
    private static readonly float[] RotateValues = { 90f, 90f, 88f, -10f, 5f, 0f, 0f, 90f };
    private static readonly float[] RotateSlopes = { 0f, 0f, -24f, 0f, 0f, 0f, 0f, 0f };     // 0.583초 키만 기울기 -24°/초(옛 Clip)
    private static readonly float[] TextTimes = { 0f, 0.833f, 1f, 2f, 2.333f };
    private static readonly float[] TextValues = { 0f, 0f, 1f, 1f, 0f };
    private static readonly float[] AlphaTimes = { 0f, 2.333f, 2.667f };
    private static readonly float[] AlphaValues = { 1f, 1f, 0f };
    public const float ClipLength = 2.667f;
    private const float CompleteSound = 0.5f, PanelOnSound = 2f, SettleTime = 1f;

    private float time = -1f;
    private bool firedComplete, firedPanelOn;
    private readonly float[] moveValues = new float[4];

    public bool IsPlaying => time >= 0f;

    private void Awake()
    {
        if (time < 0f && group != null) group.alpha = 0f;
    }

    public void Play()
    {
        firedComplete = firedPanelOn = false;
        time = 0f;
        Evaluate(0f);
    }

    public void Stop()
    {
        time = -1f;
        if (group != null) group.alpha = 0f;
    }

    private void Update()
    {
        if (time < 0f) return;
        time += Time.deltaTime;
        if (!firedComplete && time >= CompleteSound) { firedComplete = true; PlayFeedback(completeFeedback); }
        if (!firedPanelOn && time >= PanelOnSound) { firedPanelOn = true; PlayFeedback(panelOnFeedback); }
        if (time >= ClipLength)
        {
            Stop();
            return;
        }
        Evaluate(time);
    }

    private void PlayFeedback(FeedbackDataSO feedback)
    {
        if (feedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.Play(feedback, transform);
    }

    public void Evaluate(float t)
    {
        group.alpha = TrainingBoardMotion.Keys(t, AlphaTimes, AlphaValues);

        // 코어: 화면 위쪽 밖 → coreY. 줄은 코어에서 화면 위 끝까지 이어진다(Canvas 높이가 화면비마다 달라 매번 잰다).
        float half = rig.parent is RectTransform parent ? parent.rect.height * 0.5f : 540f;
        float start = half + 80f;
        moveValues[0] = moveValues[3] = start;
        moveValues[1] = moveValues[2] = coreY;
        float y = TrainingBoardMotion.Keys(t, MoveTimes, moveValues);
        core.anchoredPosition = new Vector2(coreX, y);
        cable.anchoredPosition = new Vector2(coreX, y);
        cable.sizeDelta = new Vector2(cable.sizeDelta.x, Mathf.Max(0f, half - y + 20f));
        core.localEulerAngles = new Vector3(0f, 0f, TrainingBoardMotion.Keys(t, RotateTimes, RotateValues, RotateSlopes));
        coreRing.localEulerAngles = new Vector3(0f, 0f, -45f * t);

        float arrive = TrainingBoardMotion.Pulse(t, 0.42f, 0.85f);
        float settle = TrainingBoardMotion.Pulse(t, SettleTime - 0.05f, SettleTime + 0.5f);
        Color glow = TrainingBoardStyle.Line;
        glow.a = 0.45f + 0.55f * Mathf.Max(arrive, settle);
        coreGlow.color = glow;
        coreGlow.rectTransform.localScale = Vector3.one * (1f + 0.4f * Mathf.Max(arrive, settle));

        textGroup.alpha = TrainingBoardMotion.Keys(t, TextTimes, TextValues);
        titleRoot.localScale = Vector3.one * Mathf.Lerp(1.18f, 1f, TrainingBoardMotion.OutBack(TrainingBoardMotion.Seg(t, 0.833f, 1.08f)));

        // 팔이 서는 순간 테두리 번쩍임 + 팔을 훑는 빛 + 코어에서 퍼지는 고리
        Color f = Color.white;
        f.a = 0.95f * TrainingBoardMotion.Seg(t, SettleTime - 0.04f, SettleTime) * (1f - TrainingBoardMotion.Seg(t, SettleTime, SettleTime + 0.35f));
        armFlash.color = f;
        float sweep = TrainingBoardMotion.Seg(t, SettleTime, SettleTime + 0.5f);
        var scanRect = armScan.rectTransform;
        scanRect.anchoredPosition = new Vector2(Mathf.Lerp(0f, armWidth, sweep), scanRect.anchoredPosition.y);
        Color s = TrainingBoardStyle.LineBright;
        s.a = 0.85f * TrainingBoardMotion.Pulse(t, SettleTime, SettleTime + 0.5f);
        armScan.color = s;

        float burst = TrainingBoardMotion.Seg(t, 1.167f, 1.75f);
        Color r = TrainingBoardStyle.LineBright;
        r.a = burst > 0f ? 0.9f * (1f - burst) : 0f;
        burstRing.color = r;
        burstRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 3.2f, TrainingBoardMotion.OutCubic(burst));
    }
}
