using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 난이도 상승 계기(2026-11-26 던전형 Wave 7차, DESIGN_V7 V8). 훈련 등급 하락 알림(위 가운데 붉은 띠 — TrainingGradeAlertUI, main 그대로)과 구분되도록
/// 다른 자리·다른 모양으로 띄운다: 오른쪽 HUD 열 미니맵 바로 아래(anchor·pivot (1,1), (−28,−470), 300×288)의 판 하나.
/// DungeonRoomStatus Prefab 안 부품(ThreatRise)이고, 이벤트 구독·순서는 부모 <see cref="DungeonRoomStatus"/>가 맡는다
/// (ROOM CLEAR + roomToSectorDelay에 <see cref="Play"/>, 다음 전투 방 HOSTILES에 <see cref="Fold"/>, FloorStarted·재도전·패배·Unbind에 <see cref="Stop"/>, LateUpdate에 <see cref="Advance"/>).
/// 띠 상태 기계와 따로 흐르는 평행 연출이라 띠 순서(ROOM CLEAR → EXIT → SECTOR)는 그대로이고, 등급 알림과 서로 기다리지 않는다(자리가 다르다).
/// 구성(판 안, 위 → 아래):
/// - 머리줄: ▲(DungeonThreatLadderGraphic Part.Pointer) + '난이도 상승'(24pt, 새 단계 색) + 오른쪽 작은 꼬리표(13pt, DungeonRulesSO.threatAlertTitle — 기본 'THREAT').
/// - 사다리: 세로 5칸(아래 입문 → 위 특급, 단계 색 — 현장 단말기 난이도 게이지와 같은 공용 그리기 <see cref="DungeonThreatGlyph"/>) + 칸 오른쪽 단계 이름 5개(지금 단계만 밝게)
///   + 움직이는 ▲(Part.Pointer — RectTransform 자리만 옮김) + 빛 기둥(TrainingBeamVertical — sizeDelta로만 길어짐) + 위로 흐르는 셰브론 3개(TrainingChevron 90°)
///   + 새 칸 점화(IgniteMask → 단계 색)·고리(TrainingRing)·빛(TrainingGlowDot) + 큰 단계 이름 54pt(이전 칸 높이 → 새 칸 높이로 오르고 점화 때 글자 교체 1회).
/// - 아래 줄: '적 출현' + 이전 배율(회색) + ▸(셰브론 Image) + 새 배율(26pt, 단계 색). 규칙 SO threatAlertDetail이 꺼져 있으면 숨긴다.
/// 시간표(실제 시간 — 일시정지·컷씬 중에는 부모가 paused로 멈춘다): 0~.22 오른쪽 40px에서 밀려 들어옴·칸이 아래부터 차례로 켜짐 / .2~.4 머리줄 /
/// .4~1.0 빛 기둥 솟음·셰브론·▲·큰 이름 상승(두 단계 이상이면 중간 칸 차례 점화) / .95 새 칸 점화·고리·빛·이름 교체·소리(threatAlertFeedback) /
/// 1.2~1.5 아래 줄 / 1.5~3.2 유지(새 칸 숨쉬기 2번, 셰브론 느리게) / 3.2~3.65 오른쪽으로 밀려 나가며 사라짐.
/// 시각 언어: 얇은 발광선 Sci-fi 판(DungeonStatusPlateGraphic.AccentStyle — 새 단계 색 선), 깎인 칸, 굵은 부품·톱니·액체 효과 없음. 판·글자는 크기 변형 없이 자리·알파로만 움직인다.
/// 매 프레임 할당 없음: 글자는 Play 때만 만들고, 사다리 Mesh는 정수 상태(켜진 칸·점화 칸)가 바뀔 때만 다시 만든다(그 밖에는 알파·자리·sizeDelta·색만).
/// Prefab 부품은 tools/Unity/DungeonRoomStatusSetup.cs가 만든다(재질은 공유 TrainingBoardGlow — 수정 없음, 새 텍스처 없음).
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonThreatRise : MonoBehaviour
{
    [Header("Parts")]
    [Tooltip("부품 루트 CanvasGroup(ignoreParentGroups — 띠 페이드와 독립)")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform panel;
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private DungeonStatusPlateGraphic plate;
    [SerializeField] private CanvasGroup headGroup;
    [SerializeField] private DungeonThreatLadderGraphic headGlyph;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text tagText;
    [SerializeField] private DungeonThreatLadderGraphic ladder;
    [Tooltip("움직이는 ▲(Part.Pointer, 사다리 가운데 기준 자리)")]
    [SerializeField] private DungeonThreatLadderGraphic pointer;
    [Tooltip("칸 오른쪽 단계 이름 5개(0 = 입문 … 4 = 특급)")]
    [SerializeField] private TMP_Text[] stepLabels = new TMP_Text[DungeonThreatGlyph.StepCount];
    [Tooltip("빛 기둥(TrainingBeamVertical + TrainingBoardGlow, pivot 아래 가운데)")]
    [SerializeField] private Image beam;
    [Tooltip("위로 흐르는 셰브론(TrainingChevron 90° + TrainingBoardGlow)")]
    [SerializeField] private Image[] chevrons = new Image[3];
    [SerializeField] private Image ring;
    [SerializeField] private Image cellGlow;
    [SerializeField] private TMP_Text bigName;
    [SerializeField] private CanvasGroup footGroup;
    [SerializeField] private TMP_Text footLabel;
    [SerializeField] private TMP_Text fromDensityText;
    [SerializeField] private Image footArrow;
    [SerializeField] private TMP_Text toDensityText;

    [Header("Text (정적 글꼴 — 공백은 여백 태그)")]
    [SerializeField] private string title = "난이도 상승";
    [Tooltip("규칙 SO threatAlertTitle이 비었을 때의 꼬리표")]
    [SerializeField] private string defaultTag = "THREAT";
    [SerializeField] private string footCaption = "적 출현";

    [Header("Look")]
    [SerializeField, Min(1f)] private float beamWidth = 30f;
    [SerializeField, Min(1f)] private float ringFrom = 36f;
    [SerializeField, Min(1f)] private float ringTo = 132f;
    [Tooltip("점화 때 큰 이름이 위로 넘쳤다 돌아오는 높이(px)")]
    [SerializeField, Min(0f)] private float bigNameLift = 8f;
    [Tooltip("큰 이름 가운데가 사다리 위·아래 끝에서 떨어지는 최소 거리(px — 머리줄·아래 줄과 겹치지 않게)")]
    [SerializeField, Min(0f)] private float bigNameMargin = 28f;
    [Tooltip("지금 단계가 아닌 켜진 단계 이름 밝기")]
    [SerializeField, Range(0f, 1f)] private float pastLabelAlpha = .45f;
    [Tooltip("켜지지 않은 단계 이름 밝기")]
    [SerializeField, Range(0f, 1f)] private float slotLabelAlpha = .28f;
    [Tooltip("이전 단계 이름·이전 배율 글자 색(회색 쪽)")]
    [SerializeField] private Color fromGray = new Color(.66f, .71f, .78f, 1f);

    [Header("시간표(실제 시간, 초)")]
    [SerializeField, Min(.01f)] private float slideIn = .22f;
    [SerializeField, Min(0f)] private float slideDistance = 40f;
    [Tooltip("칸이 아래부터 차례로 켜지는 간격")]
    [SerializeField, Min(0f)] private float cellStep = .04f;
    [SerializeField, Min(0f)] private float headStart = .2f;
    [SerializeField, Min(0f)] private float headEnd = .4f;
    [SerializeField, Min(0f)] private float riseStart = .4f;
    [SerializeField, Min(0f)] private float riseEnd = 1f;
    [SerializeField, Min(0f)] private float igniteAt = .95f;
    [Tooltip("점화 칸이 흰빛으로 섞여 있는 시간")]
    [SerializeField, Min(.01f)] private float igniteTime = .15f;
    [SerializeField, Min(.01f)] private float ringTime = .45f;
    [SerializeField, Min(.01f)] private float stampTime = .3f;
    [SerializeField, Min(0f)] private float footStart = 1.2f;
    [SerializeField, Min(0f)] private float footEnd = 1.5f;
    [SerializeField, Min(0f)] private float holdEnd = 3.2f;
    [SerializeField, Min(.1f)] private float duration = 3.65f;
    [Tooltip("새 칸 숨쉬기 주기·횟수(유지 구간)")]
    [SerializeField, Min(.1f)] private float breathPeriod = .8f;
    [SerializeField, Min(0)] private int breathCount = 2;
    [Tooltip("셰브론 흐름 속도(바퀴/초): 오르는 동안 / 유지 동안")]
    [SerializeField, Min(0f)] private float chevronRise = 1.6f;
    [SerializeField, Min(0f)] private float chevronHold = .5f;
    [Tooltip("다음 전투 방(HOSTILES)에서 접히는 시간")]
    [SerializeField, Min(.01f)] private float foldTime = .3f;
    [Tooltip("보임·숨김 전환 시간(HUD 숨김 등)")]
    [SerializeField, Min(.01f)] private float visibilityFade = .06f;

    private bool initialized;
    private bool playing, folding, swapped, sounded, showDetail;
    private float time, foldClock, visibility, chevronPhase;
    private DungeonThreatLevel from = DungeonThreatLevel.Intro, to = DungeonThreatLevel.Intro;
    private float fromDensity = 1f, toDensity = 1f;
    private FeedbackDataSO sound;
    private Vector2 panelBase, bigNameBase;
    private int labelLevel = -2, labelLit = -2;
    private string detailText = string.Empty;
    private Color toColor = Color.white, fromColor = Color.white;

    // ===== 검증·디버그가 읽는 값 =====
    public bool IsPlaying => playing;
    public bool IsFolding => folding;
    /// <summary>재생 시작 뒤 흐른 실제 시간(초, 멈춘 동안은 늘지 않는다).</summary>
    public float PlayTime => time;
    public DungeonThreatLevel From => from;
    public DungeonThreatLevel To => to;
    public float FromDensity => fromDensity;
    public float ToDensity => toDensity;
    public float Duration => duration;
    public float HoldEnd => holdEnd;
    public float IgniteAt => igniteAt;
    public float RiseEnd => riseEnd;
    public float FootEnd => footEnd;
    public float FoldTime => foldTime;
    public float Visibility => visibility;
    public float PanelAlpha => panelGroup != null ? panelGroup.alpha : 0f;
    public float FootAlpha => footGroup != null ? footGroup.alpha : 0f;
    /// <summary>▲가 가리키는 칸 위치(0 = 입문 … 4 = 특급, 소수).</summary>
    public float PointerPosition => RisePosition(time);
    public float PointerY => pointer != null ? pointer.rectTransform.anchoredPosition.y : 0f;
    public float BigNameY => bigName != null ? bigName.rectTransform.anchoredPosition.y : 0f;
    public string BigNameText => bigName != null ? bigName.text : null;
    /// <summary>이번 재생에서 큰 이름을 바꾼 수(점화 때 1회).</summary>
    public int NameSwaps { get; private set; }
    /// <summary>이번 재생에서 점화 소리를 낸(또는 낼 차례가 지난) 수.</summary>
    public int Sounds { get; private set; }
    /// <summary>아래 줄 글자(보이는 글 — 검증용, 공백 = 공백): '적 출현 x1.00 ▸ x1.30'.</summary>
    public string DetailText => detailText;
    public string TitleLabel => titleText != null ? titleText.text : null;
    public string TagLabel => tagText != null ? tagText.text : null;
    public RectTransform Panel => panel;
    public CanvasGroup Group => group;
    public CanvasGroup PanelGroup => panelGroup;
    public DungeonStatusPlateGraphic Plate => plate;
    public DungeonThreatLadderGraphic Ladder => ladder;
    public DungeonThreatLadderGraphic Pointer => pointer;
    public DungeonThreatLadderGraphic HeadGlyph => headGlyph;
    public Image Beam => beam;
    public Image Ring => ring;
    public Image CellGlow => cellGlow;
    public TMP_Text BigName => bigName;
    public TMP_Text FromDensityLabel => fromDensityText;
    public TMP_Text ToDensityLabel => toDensityText;
    public TMP_Text StepLabel(int step) => stepLabels != null && step >= 0 && step < stepLabels.Length ? stepLabels[step] : null;
    public int ChevronCount => chevrons != null ? chevrons.Length : 0;
    public Image Chevron(int i) => chevrons != null && i >= 0 && i < chevrons.Length ? chevrons[i] : null;
    public string Title => title;
    public string FootCaption => footCaption;

    private void Awake() => EnsureInit();

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
        if (panel != null) panelBase = panel.anchoredPosition;
        if (bigName != null) bigNameBase = bigName.rectTransform.anchoredPosition;
        if (stepLabels != null)
            for (int i = 0; i < stepLabels.Length; i++)
                if (stepLabels[i] != null) stepLabels[i].text = DungeonThreatText.Label((DungeonThreatLevel)Mathf.Clamp(i, 0, DungeonThreatGlyph.StepCount - 1));
        if (titleText != null) titleText.text = DungeonUiText.Static(title);
        if (footLabel != null) footLabel.text = DungeonUiText.Static(footCaption);
        if (plate != null) { plate.CurrentTone = DungeonStatusPlateGraphic.Tone.Threat; plate.RiseGlyph = false; }
        Draw();
    }

    /// <summary>
    /// from → to 단계 상승을 처음부터 재생한다. fromDensity·toDensity = 이전·새 단계의 적 수 배율(아래 줄). 오르지 않으면(to ≤ from) false.
    /// 꼬리표·아래 줄 켜기·소리는 DungeonRulesSO(threatAlertTitle·threatAlertDetail·threatAlertFeedback)에서 읽는다. 글자는 여기서만 만든다.
    /// </summary>
    public bool Play(DungeonThreatLevel fromLevel, DungeonThreatLevel toLevel, float fromMultiplier, float toMultiplier)
    {
        EnsureInit();
        if (toLevel <= fromLevel) return false;
        from = fromLevel;
        to = toLevel;
        fromDensity = fromMultiplier;
        toDensity = toMultiplier;
        var rules = DungeonRulesSO.Default;
        sound = rules != null ? rules.threatAlertFeedback : null;
        showDetail = rules == null || rules.threatAlertDetail;
        string tag = rules != null && !string.IsNullOrEmpty(rules.threatAlertTitle) ? rules.threatAlertTitle : defaultTag;
        toColor = DungeonThreatText.Color(to);
        fromColor = Color.Lerp(DungeonThreatText.Color(from), fromGray, .55f);
        fromColor.a = 1f;
        if (titleText != null) { titleText.text = DungeonUiText.Static(title); titleText.color = toColor; }
        if (tagText != null) { tagText.text = DungeonUiText.Static(tag); tagText.color = Dim(toColor, .75f); }
        if (plate != null)
        {
            plate.CurrentTone = DungeonStatusPlateGraphic.Tone.Threat;
            plate.Accent = (Color32)toColor;
            plate.RiseGlyph = false;
        }
        if (headGlyph != null) headGlyph.SetState(to);
        if (pointer != null) pointer.SetState(to);
        if (bigName != null) { bigName.text = DungeonThreatText.Label(from); bigName.color = fromColor; }
        string fromText = DungeonThreatAlert.DensityText(fromDensity), toText = DungeonThreatAlert.DensityText(toDensity);
        if (footLabel != null) footLabel.text = DungeonUiText.Static(footCaption);
        if (fromDensityText != null) { fromDensityText.text = fromText; fromDensityText.color = fromGray; }
        if (toDensityText != null) { toDensityText.text = toText; toDensityText.color = toColor; }
        if (footArrow != null) footArrow.color = WithAlpha(toColor, 1f);
        detailText = footCaption + " " + fromText + " ▸ " + toText;
        playing = true;
        folding = false;
        swapped = sounded = false;
        time = foldClock = chevronPhase = 0f;
        NameSwaps = Sounds = 0;
        labelLevel = labelLit = -2;
        Draw();
        return true;
    }

    /// <summary>다음 전투 방(HOSTILES)에 들어왔다: foldTime 안에 밀려 나가며 사라진다(재생 중일 때만).</summary>
    public void Fold()
    {
        if (!playing || folding) return;
        folding = true;
        foldClock = 0f;
    }

    /// <summary>바로 끈다(새 던전·재도전·패배·연결 해제): 모든 부품 알파 0·자리 원위치.</summary>
    public void Stop()
    {
        EnsureInit();
        playing = folding = false;
        time = foldClock = chevronPhase = 0f;
        visibility = 0f;
        Draw();
    }

    /// <summary>
    /// 실제 시간 dt만큼 진행한다. show = 던전 HUD를 보여도 되는지(아니면 빠르게 숨긴다), paused = 시간표를 멈출지(컷씬·HUD 숨김·일시정지·현장 단말기 —
    /// 다시 보이면 이어서 진행한다). 할당하지 않는다.
    /// </summary>
    public void Advance(float deltaTime, bool show, bool paused)
    {
        EnsureInit();
        deltaTime = Mathf.Max(0f, deltaTime);
        if (playing && !paused)
        {
            if (folding)
            {
                foldClock += deltaTime;
                if (foldClock >= foldTime) { Stop(); return; }
            }
            else
            {
                time += deltaTime;
                chevronPhase += deltaTime * (time < footEnd ? chevronRise : chevronHold);
                if (time >= duration) { Stop(); return; }
            }
            if (!sounded && time >= igniteAt)
            {
                sounded = true;
                Sounds++;
                if (sound != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(sound, transform);
            }
        }
        visibility = Mathf.MoveTowards(visibility, show && playing ? 1f : 0f, deltaTime / visibilityFade);
        Draw();
    }

    // ▲·큰 이름이 가리키는 칸 위치(이전 칸 → 새 칸, OutCubic).
    private float RisePosition(float t)
    {
        float k = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, riseStart, riseEnd));
        return Mathf.Lerp((int)from, (int)to, k);
    }

    // 칸 위치 step에 ▲가 닿는 시각(OutCubic의 역함수 — 중간 칸 차례 점화).
    private float ReachTime(int step)
    {
        int span = (int)to - (int)from;
        if (span <= 0) return riseEnd;
        float y = Mathf.Clamp01((step - (int)from) / (float)span);
        float x = 1f - Mathf.Pow(1f - y, 1f / 3f);
        return Mathf.Lerp(riseStart, riseEnd, x);
    }

    private void Draw()
    {
        float t = time;
        float exit = TrainingBoardMotion.Seg(t, holdEnd, duration);
        float fold = folding ? TrainingBoardMotion.Seg(foldClock, 0f, foldTime) : 0f;
        float enter = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0f, slideIn));
        float panelA = playing ? TrainingBoardMotion.Seg(t, 0f, .12f) * (1f - exit) * (1f - fold) * visibility : 0f;
        SetAlpha(panelGroup, panelA);
        if (panel != null)
        {
            float slide = playing ? slideDistance * ((1f - enter) + exit * exit * exit + fold) : 0f;
            SetPosition(panel, panelBase + new Vector2(slide, 0f));
        }
        if (!playing)
        {
            SetAlpha(headGroup, 0f);
            SetAlpha(footGroup, 0f);
            SetImage(beam, 0f, toColor);
            SetImage(ring, 0f, toColor);
            SetImage(cellGlow, 0f, toColor);
            if (chevrons != null) for (int i = 0; i < chevrons.Length; i++) SetImage(chevrons[i], 0f, toColor);
            if (pointer != null) pointer.canvasRenderer.SetAlpha(0f);
            if (bigName != null) SetPosition(bigName.rectTransform, bigNameBase);
            return;
        }

        int fromStep = (int)from, toStep = (int)to;
        float p = RisePosition(t);
        // 켜진 칸: 처음엔 입문부터 이전 칸까지 아래 → 위로 차례로, 오르는 동안 ▲가 지난 중간 칸, 점화 순간 새 칸.
        int top = t < riseStart ? Mathf.Min(fromStep, cellStep > 0f ? Mathf.FloorToInt(t / cellStep) : fromStep)
                : t >= igniteAt ? toStep : Mathf.Clamp(Mathf.FloorToInt(p + 1e-3f), fromStep, toStep - 1);
        int lit = (1 << (top + 1)) - 1;
        int ignite = t >= igniteAt && t < igniteAt + igniteTime ? 1 << toStep : 0;
        for (int k = fromStep + 1; k < toStep; k++)
        {
            float reach = ReachTime(k);
            if (t >= reach && t < reach + igniteTime) ignite |= 1 << k;
        }
        var current = (DungeonThreatLevel)Mathf.Clamp(top, 0, DungeonThreatGlyph.StepCount - 1);
        if (ladder != null) ladder.SetState(current, lit, ignite);
        if (labelLevel != top || labelLit != lit) ApplyLabels(top, lit);

        SetAlpha(headGroup, TrainingBoardMotion.Seg(t, headStart, headEnd));
        SetAlpha(footGroup, showDetail ? TrainingBoardMotion.Seg(t, footStart, footEnd) : 0f);
        if (ladder == null) return;
        var rect = ladder.rectTransform.rect;
        Vector2 c = rect.center;
        Rect fromCell = ladder.CellRect(fromStep), toCell = ladder.CellRect(toStep);
        float columnX = fromCell.center.x - c.x;

        if (pointer != null)
        {
            pointer.canvasRenderer.SetAlpha(TrainingBoardMotion.Seg(t, headStart, headEnd));
            SetPosition(pointer.rectTransform, ladder.PointerCenter(p) - c);
        }
        // 빛 기둥: 이전 칸 아래에서 새 칸 위까지 아래 → 위로 길어진다(sizeDelta). 점화 뒤 흐리게 남는다.
        float rise = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, riseStart, riseEnd));
        if (beam != null)
        {
            var brt = beam.rectTransform;
            SetPosition(brt, new Vector2(columnX, fromCell.yMin - c.y));
            var size = new Vector2(beamWidth, Mathf.Max(0f, (toCell.yMax - fromCell.yMin) * rise));
            if (brt.sizeDelta != size) brt.sizeDelta = size;
            float a = TrainingBoardMotion.Seg(t, riseStart, riseStart + .08f) * Mathf.Lerp(.85f, .3f, TrainingBoardMotion.Seg(t, igniteAt + .1f, footEnd));
            SetImage(beam, a, toColor);
        }
        // 위로 흐르는 셰브론(이전 칸 아래 → 새 칸 위 + 10px, 차례로).
        if (chevrons != null)
        {
            float y0 = fromCell.yMin - c.y, y1 = toCell.yMax + 10f - c.y;
            float strength = TrainingBoardMotion.Seg(t, riseStart, riseStart + .1f) * (t < footEnd ? .9f : .45f);
            for (int i = 0; i < chevrons.Length; i++)
            {
                var chevron = chevrons[i];
                if (chevron == null) continue;
                float ph = Mathf.Repeat(chevronPhase + i / (float)chevrons.Length, 1f);
                SetPosition(chevron.rectTransform, new Vector2(columnX, Mathf.Lerp(y0, y1, ph)));
                SetImage(chevron, strength * Mathf.Sin(ph * Mathf.PI), Color.Lerp(toColor, Color.white, .3f));
            }
        }
        // 새 칸 점화: 고리 터짐(sizeDelta)·빛(점화 번쩍임 + 유지 구간 숨쉬기).
        Vector2 cell = toCell.center - c;
        if (ring != null)
        {
            float k = TrainingBoardMotion.Seg(t, igniteAt, igniteAt + ringTime);
            var rrt = ring.rectTransform;
            SetPosition(rrt, cell);
            float r = Mathf.Lerp(ringFrom, ringTo, TrainingBoardMotion.OutCubic(k));
            var size = new Vector2(r, r);
            if (rrt.sizeDelta != size) rrt.sizeDelta = size;
            SetImage(ring, t >= igniteAt && k < 1f ? .85f * (1f - k) : 0f, Color.Lerp(toColor, Color.white, .35f));
        }
        if (cellGlow != null)
        {
            SetPosition(cellGlow.rectTransform, cell);
            float flash = t >= igniteAt ? .9f * (1f - TrainingBoardMotion.Seg(t, igniteAt + .05f, igniteAt + .6f)) : 0f;
            float breathEnd = footEnd + breathCount * breathPeriod;
            float breath = t >= footEnd && t < breathEnd ? .35f * (.5f - .5f * Mathf.Cos((t - footEnd) / breathPeriod * Mathf.PI * 2f)) : 0f;
            SetImage(cellGlow, Mathf.Max(flash, breath), toColor);
        }
        // 큰 단계 이름: ▲와 같은 높이로 오르고(사다리 위·아래 끝 여백 안), 점화 순간 이전 → 새 이름(글자 교체 1회)·위로 살짝 넘쳤다 자리 잡음.
        if (bigName != null)
        {
            if (!swapped && t >= igniteAt)
            {
                swapped = true;
                NameSwaps++;
                bigName.text = DungeonThreatText.Label(to);
            }
            float y = Mathf.Clamp(ladder.PointerCenter(p).y - c.y, rect.yMin - c.y + bigNameMargin, rect.yMax - c.y - bigNameMargin);
            float stamp = bigNameLift * Mathf.Sin(Mathf.PI * TrainingBoardMotion.Seg(t, igniteAt, igniteAt + stampTime));
            SetPosition(bigName.rectTransform, new Vector2(bigNameBase.x, y + stamp));
            var nameColor = swapped ? Color.Lerp(Color.white, toColor, TrainingBoardMotion.Seg(t, igniteAt, igniteAt + .2f)) : fromColor;
            if (bigName.color != nameColor) bigName.color = nameColor;
        }
    }

    // 단계 이름 5개의 밝기(지금 단계만 밝게, 켜진 지난 단계는 흐리게, 꺼진 단계는 더 흐리게) — 상태가 바뀔 때만.
    private void ApplyLabels(int current, int lit)
    {
        labelLevel = current;
        labelLit = lit;
        if (stepLabels == null) return;
        for (int i = 0; i < stepLabels.Length; i++)
        {
            var label = stepLabels[i];
            if (label == null) continue;
            Color c = DungeonThreatText.Color((DungeonThreatLevel)Mathf.Clamp(i, 0, DungeonThreatGlyph.StepCount - 1));
            bool on = (lit & (1 << i)) != 0;
            c = i == current ? Color.Lerp(c, Color.white, .2f) : Dim(c, on ? pastLabelAlpha : slotLabelAlpha);
            if (label.color != c) label.color = c;
        }
    }

    private static Color Dim(Color c, float alpha)
    {
        c.a = alpha;
        return c;
    }

    private static Color WithAlpha(Color c, float alpha)
    {
        c.a = alpha;
        return c;
    }

    private static void SetAlpha(CanvasGroup g, float alpha)
    {
        if (g != null && !Mathf.Approximately(g.alpha, alpha)) g.alpha = alpha;
    }

    private static void SetPosition(RectTransform rt, Vector2 position)
    {
        if (rt != null && rt.anchoredPosition != position) rt.anchoredPosition = position;
    }

    private static void SetImage(Image image, float alpha, Color color)
    {
        if (image == null) return;
        color.a = Mathf.Clamp01(alpha);
        if (image.color != color) image.color = color;
    }
}
