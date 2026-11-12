using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 훈련 결과 보드, 패배 화면, 클리어 패널(2026-11-10 리메이크). PlayerHUD 사이파이 판으로 짰다.
// - 클리어 패널(TrainingClearPanelUI): UIManager.StartTrainingCompletePanelEffect가 PlayClearPanel을 부른다.
// - 결과 보드(승리): 판이 가로선에서 세로로 열리고, 피격 횟수와 걸린 시간이 세어 올라가고, 골드가 옮겨지고,
//   오른쪽 조준경 고리 안에 등급이 도장처럼 찍힌다. 끝나면 "닫기" 안내가 켜지고 입력을 받는다(Jump, Attack으로 닫는다).
// - 2쪽(사용자 요청 "내가 넣은 총 데미지, 받은 피해, 무장별 피해량(힐량)을 같이 표시. 우측의 성적은 내버려 두고 왼쪽 세부사항을 2페이지로"):
//   왼쪽 영역만 1쪽(피격 횟수, 걸린 시간, 획득 골드)과 2쪽(가한 피해, 받은 피해, 회복량, 무장별 기록)으로 넘긴다. 상하(승리 화면은 좌우도)로 넘긴다.
//   무장 줄에는 실제 피해와 함께 가한 피해 전체 대비 비율(%)과 비율 막대를 적는다(2026-11-11, 사용자 요청 "정산 화면에서 데미지를 %로 계산해서
//   실제 데미지와 함께 보여줘. 이미 자율훈련장에서 그런 걸 쓰고 있어"). 계산과 표기는 자율 훈련장의 기여 비율과 같다.
// - Wave 시간: 요약·전투 기록 다음 쪽에 이번 판의 완료/중단/미진행 Wave와 소요 시간을 표시한다.
// - 패배 화면(사용자 요청 "DestroyUI도 전면 개편. 패배 시에도 훈련 종료 시 panel처럼 결과창을"): UIManager.ShowDeathUI가 ShowDefeat를 부른다.
//   붉은 "훈련 실패" 띠와 사유, 같은 결과 보드(등급 미흡, 3행은 실패 사유), 재도전 / 포기 메뉴(TrainingDefeatMenu)로 짠다.
//   훈련 기록이 없는 사망(자율 훈련 등)은 결과 보드 없이 띠와 메뉴만 띄운다. MainScene의 옛 PlayerDeathMenu는 2026-11-11에 지웠다.
// 계약(Room.WaveCompleteRoutine과 Wave 회귀 테스트가 쓴다): ShowResult, Hide, IsOpen(Hide에서 바로 false), IUIInputReceiver.
// 공개 중에는 UI 입력을 코드에서 막고, 공개 도중 닫혀도 반드시 푼다. 연출은 실제 시간으로 진행한다.
public class TrainingCompleteBoardUI : MonoBehaviour, IUIInputReceiver
{
    private enum BoardMode { Victory, Defeat, DefeatNoRecord }

    [Header("Result Board")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI hitText;
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI gradeText;
    [SerializeField] private TextMeshProUGUI goldText;
    [SerializeField] private TextMeshProUGUI earnedText;
    [SerializeField] private TextMeshProUGUI trainingNameText;

    [Header("Layout")]
    [SerializeField] private Image dim;
    [SerializeField] private RectTransform panel;
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private Image panelScan;
    [SerializeField] private CanvasGroup headerGroup;
    [SerializeField] private TextMeshProUGUI headerTagText;
    [SerializeField] private TextMeshProUGUI headerStatusText;
    [SerializeField] private Image headerAccent;
    [SerializeField] private CanvasGroup[] rows;
    [SerializeField] private CanvasGroup gradeGroup;
    [SerializeField] private RectTransform gradeRing;
    [SerializeField] private Image gradeRingImage;
    [SerializeField] private Image gradeGlow;
    [SerializeField] private Image gradeFlash;
    [SerializeField] private Image[] gradeLadder;
    [SerializeField] private Sprite ladderLitSprite;
    [SerializeField] private Sprite ladderSlotSprite;
    [SerializeField] private CanvasGroup promptGroup;
    [SerializeField] private Image[] promptChevrons;
    [SerializeField] private GameObject closePrompt;

    [Header("Page 1 (요약)")]
    [SerializeField] private CanvasGroup page1Group;
    [SerializeField] private TextMeshProUGUI thirdRowLabel;
    [SerializeField] private GameObject goldRoot;
    [SerializeField] private TextMeshProUGUI failReasonText;

    [Header("Page 2 (전투 기록)")]
    [SerializeField] private CanvasGroup page2Group;
    [SerializeField] private TextMeshProUGUI dealtText;
    [SerializeField] private TextMeshProUGUI takenText;
    [SerializeField] private TextMeshProUGUI healedText;
    [SerializeField] private CanvasGroup[] weaponRows;
    [SerializeField] private Image[] weaponIcons;
    [SerializeField] private TextMeshProUGUI[] weaponNames;
    [SerializeField] private TextMeshProUGUI[] weaponValues;
    [SerializeField] private TextMeshProUGUI[] weaponRatios;
    [SerializeField] private RectTransform[] weaponRatioFills;
    [SerializeField] private GameObject noWeaponRecord;
    [SerializeField] private Image[] pagePips;
    [SerializeField] private Image[] pageChevrons;
    [SerializeField] private TextMeshProUGUI pageText;

    [Header("Page 3 (Wave별 시간)")]
    [SerializeField] private TrainingWaveTimesPageUI waveTimesPage;

    [Header("Defeat")]
    [SerializeField] private TrainingDefeatMenu defeatMenu;
    [SerializeField] private RectTransform menuRoot;
    [SerializeField] private CanvasGroup menuGroup;
    [SerializeField] private GameObject menuBackplate;
    [SerializeField] private RectTransform[] menuButtons;
    [SerializeField] private RectTransform defeatTitle;
    [SerializeField] private CanvasGroup defeatTitleGroup;
    [SerializeField] private Image defeatTitleFlash;
    [SerializeField] private TextMeshProUGUI defeatReasonText;
    [SerializeField] private Image[] defeatEdgeGlows;
    [SerializeField] private Vector2 defeatPanelOffset = new Vector2(0f, -20f);
    [SerializeField] private Vector2 menuPosOnBoard = new Vector2(262f, -254f);
    [SerializeField] private Vector2 menuPosNoRecord = new Vector2(0f, -60f);
    [SerializeField] private Vector2 titlePosOnBoard = new Vector2(0f, 340f);
    [SerializeField] private Vector2 titlePosNoRecord = new Vector2(0f, 80f);

    [Header("Clear Panel")]
    [SerializeField] private TrainingClearPanelUI clearPanel;

    [Header("Timing (실제 시간, 초)")]
    [SerializeField] private float goldAnimationDuration = 0.5f;
    [SerializeField] private float dimAlpha = 0.82f;
    [SerializeField] private float inputReleaseTime = 2.4f;
    [SerializeField] private float closeDuration = 0.32f;
    [SerializeField] private float pageDuration = 0.18f;
    [SerializeField] private float defeatBoardDelay = 0.2f;        // 패배: 띠가 먼저 뜨고 보드가 뒤따른다
    [SerializeField] private float defeatBoardSpeed = 1.35f;       // 패배: 보드 공개를 조금 빠르게
    [SerializeField] private float noRecordReleaseTime = 0.6f;     // 기록 없는 패배: 띠와 메뉴만

    // 보드 공개 시간표(초): 판 0~0.32, 머리말 0.3, 행 0.45부터 0.28 간격, 골드 이체 1.4~, 등급 고리 1.35, 도장 1.9, 안내 inputReleaseTime
    private const float RowStart = 0.45f, RowStep = 0.28f, GoldMove = 1.4f, RingIn = 1.35f, Stamp = 1.9f;

    private BoardMode mode;
    private float revealTime = -1f;
    private float closeTime = -1f;
    private bool blockingInput;
    private bool menuOpen;
    private int hits;
    private float seconds;
    private Grade grade;
    private int startGold, targetGold, earnedGold;
    private int shownHits = -1, shownSeconds = -1, shownGold = int.MinValue, shownEarned = int.MinValue;
    private int page, shownPage = -1;
    private float pageBlend;
    private Vector2[] rowBase = new Vector2[0];
    private Vector2 headerBase, panelBase, page1Base, page2Base;
    private bool cached;
    private readonly List<TrainingWeaponRecord> recordBuffer = new List<TrainingWeaponRecord>();
    private float[] ratioValues = new float[0];

    public bool IsActive => panelRoot != null && panelRoot.activeSelf;

    // 결과 보드(승리)가 지금 열려 있는지(Room의 코루틴이 닫힐 때까지 기다리는 데 쓴다). Hide에서 바로 false가 된다.
    public bool IsOpen { get; private set; }
    // 패배 화면이 열려 있는지.
    public bool IsDefeatOpen { get; private set; }
    public bool HasDefeatScreen => defeatMenu != null;
    public int Page => page;
    public int PageCount => 2 + (waveTimesPage != null ? waveTimesPage.PageCount : 0);

    private float ReleaseTime => mode == BoardMode.Victory ? inputReleaseTime
        : mode == BoardMode.Defeat ? defeatBoardDelay + inputReleaseTime / defeatBoardSpeed
        : noRecordReleaseTime;

    public bool PlayClearPanel()
    {
        if (clearPanel == null) return false;
        clearPanel.Play();
        return true;
    }

    private void Awake()
    {
        Cache();
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        rowBase = new Vector2[rows.Length];
        for (int i = 0; i < rows.Length; i++) rowBase[i] = ((RectTransform)rows[i].transform).anchoredPosition;
        headerBase = ((RectTransform)headerGroup.transform).anchoredPosition;
        panelBase = panel.anchoredPosition;
        page1Base = ((RectTransform)page1Group.transform).anchoredPosition;
        page2Base = ((RectTransform)page2Group.transform).anchoredPosition;
    }

    public void ShowResult(RunResult result)
    {
        if (IsDefeatOpen) return;
        Cache();
        mode = BoardMode.Victory;
        panelRoot.SetActive(true);
        IsOpen = true;
        closeTime = -1f;

        LoadResult(result);
        ApplyMode(TrainingFailReason.None);
        FillStats();
        StartReveal();

        SetInputBlock(true);
        UIManager.Instance.PushInputTarget(this, UIInputLayer.System);
    }

    public void Hide()
    {
        if (!IsOpen) return;

        IsOpen = false;
        revealTime = -1f;
        SetInputBlock(false);
        closeTime = 0f;

        // UI 입력 대상 해제
        UIManager.Instance.RemoveInputTarget(this);
    }

    // 사망 뒤 패배 화면을 연다. 이번 사망이 기록 중이던 훈련을 끝냈으면 결과 보드를 함께 보여 준다.
    public bool ShowDefeat()
    {
        if (defeatMenu == null) return false;
        if (IsDefeatOpen) return true;
        Cache();

        if (IsOpen)
        {
            IsOpen = false;
            UIManager.Instance.RemoveInputTarget(this);
        }
        if (clearPanel != null) clearPanel.Stop();

        TrainingRunTracker tracker = TrainingRunTracker.Instance;
        TrainingFailReason reason = TrainingFailReason.Destroyed;
        if (tracker != null && tracker.TryTakeDefeatResult(out RunResult result))
        {
            mode = BoardMode.Defeat;
            reason = tracker.LastFailReason;
            LoadResult(result);
            FillStats();
        }
        else mode = BoardMode.DefeatNoRecord;

        panelRoot.SetActive(true);
        IsDefeatOpen = true;
        closeTime = -1f;
        ApplyMode(reason);
        StartReveal();
        SetInputBlock(true);

        // 사망 전환이 켜는 붉은 주사선(WireFrameOverlay)은 이 화면 위에 그려지므로 걷는다. 붉은 분위기는 화면 가장자리 빛이 잇는다.
        UIManager.Instance.StartWireframeEffect(0f, Color.red, 0.3f);
        return true;
    }

    // 재도전, 로비 복귀(GameManager가 ShowDeathUI(false)로 부른다). 열려 있지 않으면 아무 일도 하지 않는다.
    public void HideDefeat()
    {
        if (!IsDefeatOpen) return;

        IsDefeatOpen = false;
        revealTime = -1f;
        SetInputBlock(false);
        CloseDefeatMenu();
        closeTime = 0f;
    }

    private void OnDisable()
    {
        // 공개 도중 꺼져도 UI 입력 차단과 메뉴 입력 대상이 남지 않게 한다.
        SetInputBlock(false);
        CloseDefeatMenu();
    }

    private void SetInputBlock(bool block)
    {
        if (blockingInput == block) return;
        blockingInput = block;
        if (UIManager.Instance != null) UIManager.Instance.SetUIInputBlock(block);
    }

    private void OpenDefeatMenu()
    {
        if (menuOpen) return;
        menuOpen = true;
        defeatMenu.OnSelected += HandleDefeatSelect;
        defeatMenu.PageRequested += HandlePageRequest;
        defeatMenu.Open();
        menuGroup.blocksRaycasts = true;
    }

    private void CloseDefeatMenu()
    {
        if (!menuOpen) return;
        menuOpen = false;
        defeatMenu.OnSelected -= HandleDefeatSelect;
        defeatMenu.PageRequested -= HandlePageRequest;
        menuGroup.blocksRaycasts = false;
        if (UIManager.Instance != null) defeatMenu.Close();
    }

    // 0 = 재도전, 1 = 포기(로비로 복귀). 옛 사망 메뉴와 같은 순서다.
    private void HandleDefeatSelect(int index)
    {
        if (!IsDefeatOpen || GameManager.Instance == null) return;
        if (index == 0) GameManager.Instance.RequestRetry();
        else if (index == 1) GameManager.Instance.RequestReturnToLobby();
    }

    private void HandlePageRequest(int delta)
    {
        SetPage(page + (delta > 0 ? 1 : -1));
    }

    private void SetPage(int value)
    {
        if (mode == BoardMode.DefeatNoRecord) return;
        page = Mathf.Clamp(value, 0, PageCount - 1);
        if (page >= 2 && waveTimesPage != null) waveTimesPage.ShowPage(page - 2);
    }

    private void LoadResult(RunResult result)
    {
        if (waveTimesPage != null)
        {
            var tracker = TrainingRunTracker.Instance;
            bool sameRun = tracker != null && tracker.LastRunResult.trainingId == result.trainingId
                && tracker.LastRunResult.endedAtIso == result.endedAtIso;
            waveTimesPage.SetRecords(sameRun ? tracker.WaveTimes : null, sameRun ? tracker.WaveCount : 0, result.timeSeconds);
        }
        hits = result.hitCount;
        seconds = result.timeSeconds;
        grade = result.grade;

        // EndRun이 보상을 먼저 더했다. 지금 골드 - 보상에서 시작해 보상만큼 옮긴다.
        targetGold = DataManager.Instance != null ? DataManager.Instance.CurrentGold : result.earnedGold;
        earnedGold = Mathf.Max(0, result.earnedGold);
        startGold = targetGold - earnedGold;

        if (trainingNameText != null) trainingNameText.text = TrainingName(result.trainingId);
        if (gradeText != null)
        {
            gradeText.text = TrainingBoardStyle.GradeLabel(grade);
            gradeText.color = TrainingBoardStyle.GradeColor(grade);
        }
        Color ring = TrainingBoardStyle.GradeColor(grade);
        ring.a = 0.85f;
        if (gradeRingImage != null) gradeRingImage.color = ring;
        if (earnedText != null) earnedText.gameObject.SetActive(earnedGold > 0);
    }

    // 모드에 따라 머리말, 3행, 아래 줄, 띠, 메뉴를 바꾼다.
    private void ApplyMode(TrainingFailReason reason)
    {
        bool defeat = mode != BoardMode.Victory;
        bool noRecord = mode == BoardMode.DefeatNoRecord;
        Color accent = defeat ? (Color)TrainingBoardStyle.Warning : (Color)TrainingBoardStyle.Line;
        string reasonLabel = TrainingBoardStyle.FailReasonLabel(reason);

        panel.gameObject.SetActive(!noRecord);
        panel.anchoredPosition = panelBase + (defeat ? defeatPanelOffset : Vector2.zero);
        headerTagText.text = defeat ? "TRAINING FAILED" : "TRAINING RESULT";
        headerTagText.color = accent;
        headerStatusText.text = defeat ? "RUN TERMINATED" : "ALL WAVES CLEAR";
        headerStatusText.color = defeat ? (Color)TrainingBoardStyle.Warning : (Color)TrainingBoardStyle.Gold;
        headerAccent.color = defeat ? (Color)TrainingBoardStyle.Warning : (Color)TrainingBoardStyle.LineBright;

        thirdRowLabel.text = defeat ? "실패 사유" : "획득 골드";
        goldRoot.SetActive(!defeat);
        failReasonText.gameObject.SetActive(defeat);
        failReasonText.text = reasonLabel;

        closePrompt.SetActive(!defeat);
        menuRoot.gameObject.SetActive(defeat);
        menuBackplate.SetActive(noRecord);
        menuRoot.anchoredPosition = noRecord ? menuPosNoRecord : menuPosOnBoard;
        menuGroup.alpha = 0f;
        menuGroup.blocksRaycasts = false;       // 메뉴가 열리기 전에는 포인터를 받지 않는다(열린 뒤 포인터 진입이 새로 나가 강조가 맞는다)
        for (int i = 0; i < menuButtons.Length; i++) menuButtons[i].localScale = Vector3.one;

        defeatTitle.gameObject.SetActive(defeat);
        defeatTitle.anchoredPosition = noRecord ? titlePosNoRecord : titlePosOnBoard;
        defeatTitleGroup.alpha = 0f;
        defeatReasonText.text = reasonLabel;
        for (int i = 0; i < defeatEdgeGlows.Length; i++)
        {
            defeatEdgeGlows[i].gameObject.SetActive(defeat);
            defeatEdgeGlows[i].color = Color.clear;
        }

        page = 0;
        pageBlend = 0f;
        shownPage = -1;
    }

    private void StartReveal()
    {
        shownHits = shownSeconds = -1;
        shownGold = shownEarned = int.MinValue;
        revealTime = 0f;
        EvaluateReveal(0f);
    }

    // 2쪽: 이번 판의 전투 기록(TrainingRunTracker가 모은 읽기 전용 값).
    private void FillStats()
    {
        TrainingRunTracker tracker = TrainingRunTracker.Instance;
        TrainingCombatStats stats = tracker != null ? tracker.Stats : null;
        recordBuffer.Clear();
        if (stats != null) stats.CopySorted(recordBuffer);

        // 합계는 화면의 무장 줄(각각 반올림한 값)을 더한 것과 같게 적는다. 쉴드 재생 때문에 적용량이 소수라 따로 반올림하면 어긋난다.
        int dealtSum = 0, healedSum = 0;
        float listedHealing = 0f;
        for (int i = 0; i < recordBuffer.Count; i++)
        {
            dealtSum += Mathf.RoundToInt(recordBuffer[i].Damage);
            healedSum += Mathf.RoundToInt(recordBuffer[i].Healing);
            listedHealing += recordBuffer[i].Healing;
        }
        if (stats != null) healedSum += Mathf.RoundToInt(Mathf.Max(0f, stats.Healing - listedHealing));     // 출처 무장이 없는 회복
        dealtText.text = TrainingBoardStyle.FormatAmount(dealtSum);
        takenText.text = TrainingBoardStyle.FormatAmount(stats != null ? stats.DamageTaken : 0f);
        healedText.text = TrainingBoardStyle.FormatAmount(healedSum);
        if (ratioValues.Length != weaponRows.Length) ratioValues = new float[weaponRows.Length];
        float totalDealt = stats != null ? stats.DamageDealt : 0f;
        for (int i = 0; i < weaponRows.Length; i++)
        {
            bool used = i < recordBuffer.Count;
            weaponRows[i].gameObject.SetActive(used);
            ratioValues[i] = 0f;
            if (!used) continue;

            TrainingWeaponRecord record = recordBuffer[i];
            Sprite icon = record.Weapon != null ? record.Weapon.weaponSprite : null;
            weaponIcons[i].sprite = icon;
            weaponIcons[i].enabled = icon != null;
            weaponNames[i].text = record.Name;

            // 피해는 흰색, 회복은 초록색 +로 적는다.
            string value = record.Damage > 0f || record.Healing <= 0f ? TrainingBoardStyle.FormatAmount(record.Damage) : "";
            if (record.Healing > 0f)
                value += (value.Length > 0 ? " " : "") + "<color=#33f259>+" + TrainingBoardStyle.FormatAmount(record.Healing) + "</color>";
            weaponValues[i].text = value;

            // 피해 비율: 이번 판에 가한 전체 피해 대비(자율 훈련장의 기여 비율과 같은 계산). 피해가 없는 줄(회복 전용)은 비워 둔다.
            bool dealt = record.Damage > 0f && totalDealt > 0f;
            ratioValues[i] = dealt ? Mathf.Clamp01(record.Damage / totalDealt) : 0f;
            weaponRatios[i].text = dealt ? TrainingBoardStyle.FormatPercent(ratioValues[i]) : "";
        }
        noWeaponRecord.SetActive(recordBuffer.Count == 0);
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        pageBlend = Mathf.MoveTowards(pageBlend, page, dt / Mathf.Max(0.01f, pageDuration));

        if (closeTime >= 0f)
        {
            closeTime += dt;
            EvaluateClose(closeTime);
            if (closeTime >= closeDuration)
            {
                closeTime = -1f;
                panelRoot.SetActive(false);
            }
            return;
        }
        if ((!IsOpen && !IsDefeatOpen) || revealTime < 0f) return;

        revealTime += dt;
        EvaluateReveal(revealTime);
        if (blockingInput && revealTime >= ReleaseTime)
        {
            SetInputBlock(false);
            if (mode != BoardMode.Victory) OpenDefeatMenu();
        }
    }

    // 공개 연출: 시간 t의 모습.
    public void EvaluateReveal(float t)
    {
        Color d = Color.black;
        d.a = dimAlpha * TrainingBoardMotion.Seg(t, 0f, 0.25f);
        dim.color = d;

        if (mode != BoardMode.Victory)
        {
            EvaluateDefeatTitle(t);
            menuGroup.alpha = TrainingBoardMotion.Seg(t, ReleaseTime - 0.15f, ReleaseTime + 0.1f);
            if (mode == BoardMode.DefeatNoRecord) return;
        }
        EvaluateBoard(mode == BoardMode.Victory ? t : Mathf.Max(0f, (t - defeatBoardDelay) * defeatBoardSpeed));
    }

    // 패배 띠: 가로로 열리며 번쩍이고, 화면 네 변에 붉은 빛이 숨 쉰다(위험 빛 DangerVignetteUI와 같은 Sprite 한 장).
    private void EvaluateDefeatTitle(float t)
    {
        float sx = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0f, 0.14f));
        float sy = Mathf.Lerp(0.1f, 1f, TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0.08f, 0.24f)));
        defeatTitle.localScale = new Vector3(sx, sy, 1f);
        defeatTitleGroup.alpha = TrainingBoardMotion.Seg(t, 0f, 0.05f);
        Color flash = TrainingBoardStyle.Warning;
        flash.a = 0.9f * TrainingBoardMotion.Seg(t, 0f, 0.04f) * (1f - TrainingBoardMotion.Seg(t, 0.06f, 0.5f));
        defeatTitleFlash.color = flash;

        Color edge = TrainingBoardStyle.Warning;
        edge.a = TrainingBoardMotion.Seg(t, 0f, 0.4f) * Mathf.Lerp(0.2f, 0.4f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.4f));
        for (int i = 0; i < defeatEdgeGlows.Length; i++) defeatEdgeGlows[i].color = edge;
    }

    // 결과 보드: 보드 시간 t의 모습(패배 화면은 조금 늦게, 조금 빠르게 흐른다).
    private void EvaluateBoard(float t)
    {
        float sx = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0f, 0.16f));
        float sy = Mathf.Lerp(0.035f, 1f, TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0.12f, 0.32f)));
        panel.localScale = new Vector3(sx, sy, 1f);
        panelGroup.alpha = TrainingBoardMotion.Seg(t, 0f, 0.04f);

        float scan = TrainingBoardMotion.Seg(t, 0.3f, 0.8f);
        panelScan.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, -panel.rect.height, scan));
        Color s = mode == BoardMode.Victory ? (Color)TrainingBoardStyle.Line : (Color)TrainingBoardStyle.Warning;
        s.a = 0.55f * TrainingBoardMotion.Pulse(t, 0.3f, 0.8f);
        panelScan.color = s;

        float head = TrainingBoardMotion.Seg(t, 0.3f, 0.48f);
        headerGroup.alpha = head;
        ((RectTransform)headerGroup.transform).anchoredPosition = headerBase + new Vector2(Mathf.Lerp(-24f, 0f, TrainingBoardMotion.OutCubic(head)), 0f);

        for (int i = 0; i < rows.Length; i++)
        {
            float k = TrainingBoardMotion.Seg(t, RowStart + RowStep * i, RowStart + RowStep * i + 0.25f);
            rows[i].alpha = TrainingBoardMotion.Seg(t, RowStart + RowStep * i, RowStart + RowStep * i + 0.16f);
            ((RectTransform)rows[i].transform).anchoredPosition = rowBase[i] + new Vector2(Mathf.Lerp(-40f, 0f, TrainingBoardMotion.OutCubic(k)), 0f);
        }

        // 피격 횟수, 걸린 시간은 세어 올라간다
        int h = Mathf.RoundToInt(hits * TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, RowStart + 0.05f, RowStart + 0.4f)));
        if (h != shownHits)
        {
            shownHits = h;
            hitText.text = $"{h}<size=50%><alpha=#99> 회</size>";
        }
        float shownTime = seconds * TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, RowStart + RowStep + 0.05f, RowStart + RowStep + 0.45f));
        int secondsValue = Mathf.FloorToInt(shownTime);
        if (secondsValue != shownSeconds)
        {
            shownSeconds = secondsValue;
            timeText.text = TrainingBoardStyle.FormatTime(shownTime, false);
        }

        // 골드: 시작 골드에서 보상이 옮겨지며 총액이 오른다(+보상은 0까지 줄어든다). 패배 화면은 이 줄에 실패 사유를 적는다.
        if (mode == BoardMode.Victory)
        {
            float move = earnedGold > 0 ? TrainingBoardMotion.Seg(t, GoldMove, GoldMove + goldAnimationDuration) : 1f;
            int gold = Mathf.RoundToInt(Mathf.Lerp(startGold, targetGold, move));
            int left = Mathf.RoundToInt(Mathf.Lerp(earnedGold, 0f, move));
            if (gold != shownGold)
            {
                shownGold = gold;
                goldText.text = $"x{gold}";
            }
            if (earnedText != null && earnedGold > 0)
            {
                if (left != shownEarned)
                {
                    shownEarned = left;
                    earnedText.text = $"+{left}";
                }
                float pop = TrainingBoardMotion.Seg(t, GoldMove - 0.2f, GoldMove - 0.05f);
                Color e = TrainingBoardStyle.Gold;
                e.a = pop * (move >= 1f ? 0.45f : 1f);
                earnedText.color = e;
                earnedText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, TrainingBoardMotion.OutBack(pop));
            }
        }

        // 등급: 고리가 돌며 들어오고, 등급 글자가 도장처럼 찍히고, 사다리가 아래 등급부터 켜진다
        float ringIn = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, RingIn, RingIn + 0.45f));
        gradeGroup.alpha = TrainingBoardMotion.Seg(t, RingIn, RingIn + 0.15f);
        float punch = 0.12f * TrainingBoardMotion.Seg(t, Stamp, Stamp + 0.02f) * (1f - TrainingBoardMotion.Seg(t, Stamp + 0.02f, Stamp + 0.3f));
        gradeRing.localScale = Vector3.one * (Mathf.Lerp(0.6f, 1f, ringIn) + punch);
        gradeRing.localEulerAngles = new Vector3(0f, 0f, 140f * (1f - ringIn) - 8f * t);

        float stamp = TrainingBoardMotion.Seg(t, Stamp, Stamp + 0.22f);
        Color gc = TrainingBoardStyle.GradeColor(grade);
        gc.a = TrainingBoardMotion.Seg(t, Stamp, Stamp + 0.04f);
        gradeText.color = gc;
        gradeText.rectTransform.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, TrainingBoardMotion.OutBack(stamp));

        Color flash = Color.white;
        flash.a = 0.9f * TrainingBoardMotion.Seg(t, Stamp, Stamp + 0.02f) * (1f - TrainingBoardMotion.Seg(t, Stamp + 0.02f, Stamp + 0.4f));
        gradeFlash.color = flash;
        Color glow = TrainingBoardStyle.GradeColor(grade);
        glow.a = 0.32f * TrainingBoardMotion.Seg(t, Stamp, Stamp + 0.2f);
        gradeGlow.color = glow;

        for (int i = 0; i < gradeLadder.Length; i++)
        {
            bool reached = t >= Stamp + 0.05f + 0.07f * i;
            Grade g = (Grade)i;
            bool on = reached && g <= grade;
            Color c = on ? TrainingBoardStyle.GradeColor(g) : TrainingBoardStyle.LineDim;
            c.a = on ? (g == grade ? 1f : 0.5f) : 0.8f;
            gradeLadder[i].color = c;
            gradeLadder[i].sprite = on ? ladderLitSprite : ladderSlotSprite;
        }

        promptGroup.alpha = TrainingBoardMotion.Seg(t, inputReleaseTime - 0.1f, inputReleaseTime + 0.15f);
        for (int i = 0; i < promptChevrons.Length; i++)
        {
            Color c = TrainingBoardStyle.Line;
            c.a = 0.35f + 0.65f * Mathf.Clamp01(Mathf.Sin(Time.unscaledTime * 5f - i * 0.9f));
            promptChevrons[i].color = c;
        }

        ApplyPage();
    }

    // 요약, 전투 기록, Wave 시간 순서로 넘긴다. 전투 기록의 무장 줄과 비율 막대는 기존 연출을 유지한다.
    private void ApplyPage()
    {
        float first = TrainingBoardMotion.Smooth(Mathf.Clamp01(1f - Mathf.Abs(pageBlend)));
        float second = TrainingBoardMotion.Smooth(Mathf.Clamp01(1f - Mathf.Abs(pageBlend - 1f)));
        page1Group.alpha = first;
        page2Group.alpha = second;
        ((RectTransform)page1Group.transform).anchoredPosition = page1Base + new Vector2(0f, 18f * (1f - first));
        ((RectTransform)page2Group.transform).anchoredPosition = page2Base + new Vector2(0f, -18f * (1f - second));
        if (waveTimesPage != null) waveTimesPage.Group.alpha = TrainingBoardMotion.Smooth(Mathf.Clamp01(pageBlend - 1f));
        for (int i = 0; i < weaponRows.Length; i++)
        {
            float start = 0.2f + 0.035f * i;
            weaponRows[i].alpha = TrainingBoardMotion.Seg(pageBlend, start, start + 0.35f);
            float grow = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(pageBlend, start, Mathf.Min(1f, start + 0.5f)));
            RectTransform fill = weaponRatioFills[i];
            fill.anchorMax = new Vector2((i < ratioValues.Length ? ratioValues[i] : 0f) * grow, fill.anchorMax.y);
        }

        // 이전/다음으로 갈 수 있는 쪽의 셰브론만 깜빡인다.
        float blink = 0.4f + 0.6f * Mathf.Clamp01(Mathf.Sin(Time.unscaledTime * 5f));
        for (int i = 0; i < pageChevrons.Length; i++)
        {
            bool canGo = i == 0 ? page > 0 : page < PageCount - 1;
            Color c = canGo ? (Color)TrainingBoardStyle.Line : (Color)TrainingBoardStyle.LineDim;
            c.a = canGo ? blink : 0.5f;
            pageChevrons[i].color = c;
        }

        if (page == shownPage) return;
        shownPage = page;
        pageText.text = $"PAGE {page + 1} / {PageCount}";
        for (int i = 0; i < pagePips.Length; i++)
        {
            pagePips[i].sprite = i == Mathf.Min(page, 2) ? ladderLitSprite : ladderSlotSprite;
            pagePips[i].color = i == Mathf.Min(page, 2) ? (Color)TrainingBoardStyle.LineBright : (Color)TrainingBoardStyle.LineDim;
        }
    }

    // 닫힘: 판이 세로로 접혀 가로선이 된 뒤 사라지고 배경이 걷힌다. 패배 화면은 띠와 메뉴도 함께 사라진다.
    private void EvaluateClose(float t)
    {
        float k = TrainingBoardMotion.Seg(t, 0f, closeDuration);
        Color d = dim.color;
        d.a = Mathf.Min(d.a, dimAlpha * (1f - k));
        dim.color = d;
        float sy = Mathf.Max(0.035f, 1f - TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(k, 0f, 0.55f)));
        float sx = 1f - TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(k, 0.5f, 1f));
        panel.localScale = new Vector3(Mathf.Min(panel.localScale.x, sx), Mathf.Min(panel.localScale.y, sy), 1f);
        panelGroup.alpha = Mathf.Min(panelGroup.alpha, 1f - TrainingBoardMotion.Seg(k, 0.8f, 1f));

        float fade = 1f - TrainingBoardMotion.Seg(k, 0f, 0.6f);
        defeatTitleGroup.alpha = Mathf.Min(defeatTitleGroup.alpha, fade);
        menuGroup.alpha = Mathf.Min(menuGroup.alpha, fade);
        for (int i = 0; i < defeatEdgeGlows.Length; i++)
        {
            Color c = defeatEdgeGlows[i].color;
            c.a = Mathf.Min(c.a, 0.4f * fade);
            defeatEdgeGlows[i].color = c;
        }
    }

    private static string TrainingName(string trainingId)
    {
        var tracker = TrainingRunTracker.Instance;
        if (tracker != null && tracker.TrainingDefinition != null && tracker.TrainingDefinition.trainingId == trainingId
            && !string.IsNullOrEmpty(tracker.TrainingDefinition.displayName))
            return tracker.TrainingDefinition.displayName;
        return trainingId;
    }

    #region IUIInputReceiver 구현 (결과 보드가 승리로 열려 있을 때. 패배 화면의 입력은 TrainingDefeatMenu가 받는다)

    public void OnConfirm()
    {
        // 확인 키(Jump/Space/A버튼 등) 누르면 UI 닫기
        Hide();
    }

    public void OnCancel()
    {
        // 취소 키(Attack/ESC/B버튼 등) 누르면 UI 닫기
        Hide();
    }

    // 아래/오른쪽은 다음 쪽, 위/왼쪽은 이전 쪽으로 이동한다.
    public void OnMove(Vector2 dir)
    {
        if (!IsOpen) return;
        if (dir.y < -0.5f || dir.x > 0.5f) SetPage(page + 1);
        else if (dir.y > 0.5f || dir.x < -0.5f) SetPage(page - 1);
    }

    #endregion
}
