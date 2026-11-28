using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 7차 V6: 현장 단말기 정보 판이 그리는 훈련 상태(읽기 전용 값). 실행 중에는 <see cref="FromTracker"/>(TrainingRunTracker.Instance)로 만들고,
/// 검증은 <see cref="DungeonTerminalStatusPanel.ApplyTraining"/>으로 가짜 값을 넣는다(Edit Mode 회귀는 추적기가 없어 이 경로를 쓴다).
/// 한도는 '통과' 등급 규칙(TrainingBoardStyle.GetPassLimits — 코너 간략판과 같은 기준), 눈금은 통과보다 높은 등급(우수·무결점) 규칙이다. −1 = 제한 없음.
/// </summary>
public struct DungeonTerminalTrainingState
{
    public bool active;
    public Grade grade;
    public int hits;
    public float elapsed;
    public int passHits;
    public float passTime;
    public int excellentHits, flawlessHits;
    public float excellentTime, flawlessTime;

    /// <summary>훈련 기록 없음.</summary>
    public static DungeonTerminalTrainingState None => new DungeonTerminalTrainingState
    {
        active = false, grade = Grade.Poor, passHits = -1, passTime = -1f, excellentHits = -1, flawlessHits = -1, excellentTime = -1f, flawlessTime = -1f,
    };

    /// <summary>훈련 정의 규칙과 지금 값으로 만든다(정의가 없으면 기록 없음).</summary>
    public static DungeonTerminalTrainingState From(TrainingDefinitionSO training, int hits, float elapsed, Grade grade, bool active)
    {
        var s = None;
        if (training == null || !active) return s;
        s.active = true;
        s.grade = grade;
        s.hits = Mathf.Max(0, hits);
        s.elapsed = Mathf.Max(0f, elapsed);
        TrainingBoardStyle.GetPassLimits(training, out s.passHits, out s.passTime);
        if (training.rules != null)
            foreach (var r in training.rules)
            {
                if (r.grade == Grade.Excellent) { s.excellentHits = r.maxHits; s.excellentTime = r.maxTimeSeconds; }
                else if (r.grade == Grade.Flawless) { s.flawlessHits = r.maxHits; s.flawlessTime = r.maxTimeSeconds; }
            }
        return s;
    }

    /// <summary>TrainingRunTracker에서 읽는다(없거나 기록 중이 아니면 기록 없음). 열 때·원본이 바뀔 때만 부른다.</summary>
    public static DungeonTerminalTrainingState FromTracker(TrainingRunTracker tracker)
    {
        if (tracker == null || tracker.TrainingDefinition == null || !tracker.IsRunActive) return None;
        return From(tracker.TrainingDefinition, tracker.CurrentHItCount, tracker.CurrentTimeSeconds, tracker.CurrentPredictedGrade, true);
    }

    /// <summary>등급 g를 지키는 피격·시간 한도(미흡이면 둘 다 −1이고 false).</summary>
    public bool RuleFor(Grade g, out int maxHits, out float maxTime)
    {
        maxHits = -1;
        maxTime = -1f;
        switch (g)
        {
            case Grade.Flawless: maxHits = flawlessHits; maxTime = flawlessTime; return true;
            case Grade.Excellent: maxHits = excellentHits; maxTime = excellentTime; return true;
            case Grade.Pass: maxHits = passHits; maxTime = passTime; return true;
            default: return false;
        }
    }
}

/// <summary>
/// 7차(2026-11-26, DESIGN_V7 §1.6 V6) 현장 단말기 오른쪽 정보 판 '계기판형'(understand7/terminal.md 1안). 글자 나열 대신 위젯 7개로 보인다:
/// ① 성적 등급 칩(등급 이름 + 4칸 비스듬한 사다리 + 빛) ② 난이도 게이지(공용 세로 5칸 사다리 + ▲ — <see cref="DungeonThreatLadderGraphic"/>, '적 출현 x1.30', 다음 단계까지 점)
/// ③ 남은 피격 Bar(남은 양 — 코너 간략판 숫자와 같은 값, 등급 눈금이 '등급 기준' 표를 대신) ④ 남은 시간 Bar ⑤ 구역 클리어 칩 띠(상태순, 지금 방 = 흰 꺾쇠 + 마름모,
/// 레버 대기 방 = 앰버 점) ⑥ 레버 배지 줄(지금 방 레버 꺾쇠, 상태 글자) → 버스선 ⑦ 출구 배지(램프 = 레버 수).
/// - 표시만 한다. 게임 상태를 바꾸지 않고 DungeonManager를 참조하지 않는다 — 단말기가 넘긴 읽기 전용 원본(IDungeonMapSource·IDungeonObjectiveSource·IDungeonThreatSource·
///   IDungeonExitSource와 선택 인터페이스 IDungeonObjectiveGate·IDungeonThreatLadder)과 TrainingRunTracker.Instance만 읽는다.
/// - 열림 연출(<see cref="PlayIntro"/> → <see cref="Advance"/>, 실제 시간 — 단말기는 열면 일시정지): 약 1.0초 동안 위젯이 차례로 켜진다(시간표 상수 아래). 무음.
///   지난번 열람 뒤 바뀐 것을 강조한다: 잃은 피격 칸 붉게 → 0.45초 식음, 줄어든 시간 흰빛 띠, 떨어진 등급 칸 붉게, 새로 클리어한 칩 녹색 번쩍, 새로 당긴 레버 번쩍 → 전류 → 출구 램프,
///   오른 난이도 칸 점화(0.15초). 기억은 층(FloorNumber)이 바뀌거나 새 훈련이 시작되면 비운다.
/// - Mesh는 정수 상태가 바뀔 때만 다시 만든다(각 그래픽의 Set* — 같으면 무시). 맥동·빛·전류는 CanvasRenderer 알파·RectTransform 자리만. 매 프레임 할당 0.
///   글자는 <see cref="Refresh"/>(열 때·원본이 바뀔 때)에서만 정한다.
/// Prefab 부품은 tools/Unity/DungeonFieldTerminalSetup.cs가 만든다(InfoPanel 위 이 컴포넌트 + 위젯 자식).
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonTerminalStatusPanel : MonoBehaviour
{
    // ===== 열림 시간표(초, PlayIntro 순간 t = 0 — 판단값) =====
    public const float GradeStart = .20f, GradeStep = .05f, GlowFlash = .35f;
    public const float ThreatStart = .26f, ThreatStep = .06f, IgniteTime = .15f;
    public const float HitStart = .32f, HitFillDuration = .40f, LostFlash = .45f;
    public const float TimeStart = .38f, TimeFillDuration = .40f;
    public const float RoomStart = .45f, RoomStep = .03f, RoomFlashDuration = .9f;
    public const float LeverStart = .55f, LeverStep = .08f, LeverFlashDuration = .9f, SparkTime = .3f, LampStep = .1f;
    /// <summary>
    /// 열림 연출 전체 길이(이 뒤는 최종 모습 + 상시 연출). 위젯이 켜지는 부분은 약 1.0초 안에 끝나고(레버 3개 기준 0.8초), 그 뒤는 '지난번 열람 뒤' 레버 전류·출구 램프·
    /// 번쩍임이 식는 꼬리다(레버 6개·모두 새로 당긴 경우도 이 안에 끝난다).
    /// </summary>
    public const float IntroLength = 2.0f;
    // ===== 상시 연출 주기(초) =====
    public const float GlowPeriod = 1.6f, LadderPeriod = 1.2f, SweepPeriod = 2.6f, SweepTime = .6f, DangerPeriod = .8f, ExitPulsePeriod = 1.1f, ExitSparkPeriod = 2f;
    public const float ScanPeriod = 4.2f, ScanTime = 2.4f;
    /// <summary>위험 문턱(코너판과 같은 값): 남은 피격 5 이하·남은 시간 60초 이하이면 숫자 경고색 + 깜박임.</summary>
    public const int DangerHits = 5;
    public const float DangerSeconds = 60f;
    /// <summary>시간 바 채움 영역이 Track 사각형 안으로 들어간 양(px, 생성 도구 FillArea와 같다).</summary>
    public static readonly Vector2 TimeFillInset = new Vector2(2f, 3f);

    [Header("① 성적 등급 칩")]
    [SerializeField] private RectTransform gradeRoot;
    [SerializeField] private DungeonTerminalGaugeGraphic gradePlate;
    [SerializeField] private TMP_Text gradeName;
    [SerializeField] private TMP_Text gradeNote;
    [SerializeField] private DungeonTerminalGaugeGraphic gradeLadder;
    [SerializeField] private DungeonTerminalGaugeGraphic gradeFlash;
    [SerializeField] private Image gradeGlow;
    [SerializeField, Min(10f)] private float gradeChipWidth = 196f;
    [SerializeField, Min(10f)] private float gradeChipWideWidth = 472f;

    [Header("② 난이도 게이지")]
    [SerializeField] private CanvasGroup threatGroup;
    [SerializeField] private DungeonThreatLadderGraphic threatLadder;
    [SerializeField] private TMP_Text threatName;
    [SerializeField] private TMP_Text threatDensity;
    [SerializeField] private TMP_Text threatNext;
    [SerializeField] private DungeonTerminalGaugeGraphic threatPips;

    [Header("③ 남은 피격")]
    [SerializeField] private TMP_Text hitLabel;
    [SerializeField] private TMP_Text hitValue;
    [SerializeField] private TMP_Text hitKeep;
    [SerializeField] private CanvasGroup hitBarGroup;
    [SerializeField] private DungeonTerminalGaugeGraphic hitBar;
    [SerializeField] private DungeonTerminalGaugeGraphic hitFlash;
    [SerializeField] private TMP_Text[] hitMarkLabels = new TMP_Text[0];

    [Header("④ 남은 시간")]
    [SerializeField] private TMP_Text timeLabel;
    [SerializeField] private TMP_Text timeValue;
    [SerializeField] private TMP_Text timeKeep;
    [SerializeField] private CanvasGroup timeBarGroup;
    [SerializeField] private DungeonTerminalGaugeGraphic timeTrack;
    [SerializeField] private DungeonTerminalGaugeGraphic timeMarks;
    [SerializeField] private DungeonTerminalGaugeGraphic timeFlash;
    [SerializeField] private RectTransform timeFill;
    [SerializeField] private Image timeFillImage;
    [SerializeField] private RectTransform timeHead;
    [SerializeField] private TMP_Text[] timeMarkLabels = new TMP_Text[0];

    [Header("점검 빛(③·④ 위를 훑음)")]
    [SerializeField] private RectTransform sweep;
    [SerializeField] private Image sweepImage;
    [Tooltip("점검 빛이 지나가는 x 범위(이 판 RectTransform 기준 anchoredPosition)")]
    [SerializeField] private Vector2 sweepRange = new Vector2(-236f, 236f);

    [Header("⑤ 구역 클리어")]
    [SerializeField] private TMP_Text roomLabel;
    [SerializeField] private DungeonTerminalGaugeGraphic roomStrip;
    [SerializeField] private DungeonTerminalGaugeGraphic roomFlash;

    [Header("⑥⑦ 목표·출구")]
    [SerializeField] private GameObject dividerObject;
    [SerializeField] private GameObject objectiveHeaderObject;
    [SerializeField] private TMP_Text objectiveTitle;
    [SerializeField] private TMP_Text objectiveCount;
    [SerializeField] private CanvasGroup objectiveGroup;
    [SerializeField] private DungeonTerminalGaugeGraphic leverRow;
    [SerializeField] private DungeonTerminalGaugeGraphic conduit;
    [SerializeField] private DungeonTerminalGaugeGraphic exitBadge;
    [SerializeField] private DungeonTerminalGaugeGraphic leverFlash;
    [SerializeField] private TMP_Text[] leverLabels = new TMP_Text[0];
    [SerializeField] private TMP_Text exitText;
    [SerializeField] private RectTransform spark;
    [SerializeField] private Image sparkImage;

    [Header("Sci-Fi 아이콘·애니메이션")]
    [SerializeField] private DungeonTerminalGaugeGraphic scifiFrame;
    [SerializeField] private DungeonTerminalGaugeGraphic gradeReticle;
    [SerializeField] private DungeonTerminalGaugeGraphic threatIcon;
    [SerializeField] private DungeonTerminalGaugeGraphic hitIcon;
    [SerializeField] private DungeonTerminalGaugeGraphic timeIcon;
    [SerializeField] private RectTransform clockHand;
    [SerializeField] private DungeonTerminalGaugeGraphic roomIcon;
    [SerializeField] private DungeonTerminalGaugeGraphic objectiveIcon;
    [SerializeField] private RectTransform[] telemetryBars = new RectTransform[0];
    [SerializeField] private RectTransform scanBar;
    [SerializeField] private Image scanBarImage;
    [SerializeField] private Vector2 scanRangeY = new Vector2(-42f, -490f);

    private static readonly Color Dim = new Color32(120, 160, 172, 255);
    private static readonly Color Warning = new Color32(255, 90, 60, 255);
    private static readonly Color32 LostRed = new Color32(255, 42, 31, 235);
    private static readonly Color32 TimeWhite = new Color32(230, 250, 255, 200);
    private static readonly Color32 RoomGreen = new Color32(170, 255, 190, 230);
    private static readonly Color32 LeverWhite = new Color32(255, 236, 190, 210);

    // 보이는 값 한 벌(정수·실수만 — 복사해도 할당 없음)
    private struct View
    {
        public bool valid, hasMap, training, threat, objective, exit, exitOpen;
        public int floorKey;
        public Grade grade;
        public int hits, hitLimit, hitsLeft, markHitA, markHitB;
        public float elapsed, timeLimit, remaining, markTimeA, markTimeB;
        public DungeonThreatLevel level;
        public int cleared, nextAt, pipTotal, pipFilled;
        public float density;
        public int roomCount, roomsCleared, roomsTotal, overflowTotal, overflowCleared;
        public ulong clearedRoomMask;
        public int leverCount, leverDone, leverKnown, leverCurrent, done, total;
    }

    private View cur, memory, prev;
    private readonly int[] roomCodes = new int[DungeonTerminalGaugeGraphic.MaxRoomChips];
    private readonly int[] roomIds = new int[DungeonTerminalGaugeGraphic.MaxRoomChips];
    private readonly StringBuilder builder = new StringBuilder(256);
    private bool overrideActive;
    private DungeonTerminalTrainingState overrideState = DungeonTerminalTrainingState.None;
    private DungeonTerminalTrainingState training = DungeonTerminalTrainingState.None;

    private bool introActive, hasPrev;
    private float introTime = IntroLength, clock;
    // 지난번 열람 뒤 강조
    private bool hitLost, gradeLost, timeLost, newExit;
    private int roomNewFirst = -1, leverNewFirst = -1, leverNewCount, igniteMask;
    private float sparkStart = -1f;
    private int refreshCount, introCount;
    private CanvasRenderer headRenderer;

    // ===== 검증이 읽는 값 =====
    public bool IntroActive => introActive;
    public float IntroTime => introTime;
    public bool HasPrevious => hasPrev;
    public int RefreshCount => refreshCount;
    public int IntroCount => introCount;
    public bool TrainingShown => cur.training;
    public Grade GradeShown => cur.grade;
    public int HitsLeft => cur.hitsLeft;
    public int HitLimit => cur.hitLimit;
    public int HitMarkA => cur.markHitA;
    public int HitMarkB => cur.markHitB;
    public float TimeRemaining => cur.remaining;
    public float TimeLimit => cur.timeLimit;
    public float TimeMarkA => cur.markTimeA;
    public float TimeMarkB => cur.markTimeB;
    public bool ThreatShown => cur.threat;
    public int PipTotal => cur.pipTotal;
    public int PipFilled => cur.pipFilled;
    public int RoomsCleared => cur.roomsCleared;
    public int RoomsTotal => cur.roomsTotal;
    public int RoomSlotId(int slot) => slot >= 0 && slot < cur.roomCount ? roomIds[slot] : -1;
    public bool ObjectiveShown => cur.objective;
    public bool HitLostFlash => hitLost;
    public bool GradeLostFlash => gradeLost;
    public bool TimeLostFlash => timeLost;
    public int NewRoomFirstSlot => roomNewFirst;
    public int NewLeverCount => leverNewCount;
    public int IgniteMask => igniteMask;
    public bool NewExit => newExit;
    public float SparkStart => sparkStart;
    public float TimeFillRatio => timeFill != null ? timeFill.anchorMax.x : -1f;
    public Vector2 SparkPosition => spark != null ? spark.anchoredPosition : Vector2.zero;
    public float SparkAlpha => sparkImage != null ? sparkImage.canvasRenderer.GetAlpha() : 0f;

    public DungeonTerminalGaugeGraphic GradePlate => gradePlate;
    public DungeonTerminalGaugeGraphic GradeLadder => gradeLadder;
    public DungeonTerminalGaugeGraphic GradeFlash => gradeFlash;
    public Image GradeGlow => gradeGlow;
    public TMP_Text GradeName => gradeName;
    public TMP_Text GradeNote => gradeNote;
    public RectTransform GradeRoot => gradeRoot;
    public CanvasGroup ThreatGroup => threatGroup;
    public DungeonThreatLadderGraphic ThreatLadder => threatLadder;
    public TMP_Text ThreatName => threatName;
    public TMP_Text ThreatDensity => threatDensity;
    public TMP_Text ThreatNext => threatNext;
    public DungeonTerminalGaugeGraphic ThreatPips => threatPips;
    public TMP_Text HitLabel => hitLabel;
    public TMP_Text HitValue => hitValue;
    public TMP_Text HitKeep => hitKeep;
    public CanvasGroup HitBarGroup => hitBarGroup;
    public DungeonTerminalGaugeGraphic HitBar => hitBar;
    public DungeonTerminalGaugeGraphic HitFlash => hitFlash;
    public TMP_Text[] HitMarkLabels => hitMarkLabels;
    public TMP_Text TimeLabel => timeLabel;
    public TMP_Text TimeValue => timeValue;
    public TMP_Text TimeKeep => timeKeep;
    public CanvasGroup TimeBarGroup => timeBarGroup;
    public DungeonTerminalGaugeGraphic TimeTrack => timeTrack;
    public DungeonTerminalGaugeGraphic TimeMarks => timeMarks;
    public DungeonTerminalGaugeGraphic TimeFlash => timeFlash;
    public RectTransform TimeFill => timeFill;
    public TMP_Text[] TimeMarkLabels => timeMarkLabels;
    public RectTransform Sweep => sweep;
    public Image SweepImage => sweepImage;
    public TMP_Text RoomLabel => roomLabel;
    public DungeonTerminalGaugeGraphic RoomStrip => roomStrip;
    public DungeonTerminalGaugeGraphic RoomFlash => roomFlash;
    public TMP_Text ObjectiveTitle => objectiveTitle;
    public TMP_Text ObjectiveCount => objectiveCount;
    public CanvasGroup ObjectiveGroup => objectiveGroup;
    public DungeonTerminalGaugeGraphic LeverRow => leverRow;
    public DungeonTerminalGaugeGraphic Conduit => conduit;
    public DungeonTerminalGaugeGraphic ExitBadge => exitBadge;
    public DungeonTerminalGaugeGraphic LeverFlash => leverFlash;
    public TMP_Text[] LeverLabels => leverLabels;
    public TMP_Text ExitText => exitText;
    public RectTransform Spark => spark;
    public DungeonTerminalGaugeGraphic SciFiFrame => scifiFrame;
    public DungeonTerminalGaugeGraphic GradeReticle => gradeReticle;
    public DungeonTerminalGaugeGraphic ThreatIcon => threatIcon;
    public DungeonTerminalGaugeGraphic HitIcon => hitIcon;
    public DungeonTerminalGaugeGraphic TimeIcon => timeIcon;
    public RectTransform ClockHand => clockHand;
    public DungeonTerminalGaugeGraphic RoomIcon => roomIcon;
    public DungeonTerminalGaugeGraphic ObjectiveIcon => objectiveIcon;
    public RectTransform[] TelemetryBars => telemetryBars;
    public RectTransform ScanBar => scanBar;
    public Image ScanBarImage => scanBarImage;

    /// <summary>검증용: 가짜 훈련 상태를 넣는다(다음 Refresh부터 추적기 대신 쓴다).</summary>
    public void ApplyTraining(in DungeonTerminalTrainingState state)
    {
        overrideActive = true;
        overrideState = state;
    }

    /// <summary>검증용: 가짜 훈련 상태를 거두고 추적기를 다시 읽는다.</summary>
    public void ClearTrainingOverride()
    {
        overrideActive = false;
        overrideState = DungeonTerminalTrainingState.None;
    }

    /// <summary>'지난번 열람' 기억을 비운다(검증용 — 실행 중에는 층·훈련이 바뀔 때 저절로 비운다).</summary>
    public void ResetMemory()
    {
        memory = default;
        hasPrev = false;
    }

    // ===== 갱신 =====

    /// <summary>원본을 읽어 글자와 위젯 상태를 정한다(열 때·원본이 바뀔 때 — 단말기가 부른다). 원본은 살아 있는 것만 넘긴다(없으면 null).</summary>
    public void Refresh(IDungeonMapSource map, IDungeonObjectiveSource objectives, IDungeonThreatSource threat, IDungeonExitSource exit)
    {
        refreshCount++;
        Gather(map, objectives, threat, exit);
        ApplyTexts();
        ApplyStatic();
        ApplyFrame(introActive ? introTime : IntroLength);
    }

    /// <summary>열림 연출을 처음부터 시작한다(단말기 Open — Refresh 뒤). 지난번 열람 값과 비교해 강조를 정하고 이번 값을 기억한다.</summary>
    public void PlayIntro()
    {
        introCount++;
        bool sameRun = memory.valid && memory.floorKey == cur.floorKey && cur.floorKey != 0 && memory.training == cur.training
                       && (!cur.training || (cur.hits >= memory.hits && cur.elapsed + .5f >= memory.elapsed));
        hasPrev = sameRun;
        prev = memory;
        BuildHighlights();
        memory = cur;
        memory.valid = true;
        introActive = true;
        introTime = 0f;
        ApplyFrame(0f);
        Ambient();
    }

    /// <summary>실제 시간 dt만큼 열림 연출·상시 연출을 진행한다(단말기 Advance가 열려 있는 동안 부른다). 할당 없음.</summary>
    public void Advance(float deltaTime)
    {
        deltaTime = Mathf.Max(0f, deltaTime);
        clock += deltaTime;
        if (introActive)
        {
            introTime += deltaTime;
            if (introTime >= IntroLength)
            {
                introTime = IntroLength;
                introActive = false;
            }
            ApplyFrame(introTime);
        }
        Ambient();
    }

    private void Gather(IDungeonMapSource map, IDungeonObjectiveSource objectives, IDungeonThreatSource threat, IDungeonExitSource exit)
    {
        var v = new View();
        v.hasMap = map != null && map.HasFloor;
        v.floorKey = v.hasMap ? Mathf.Max(1, map.FloorNumber) : 0;

        // 훈련(코너 간략판과 같은 의미: 남은 양)
        training = overrideActive ? overrideState : DungeonTerminalTrainingState.FromTracker(TrainingRunTracker.Instance);
        v.training = training.active;
        v.grade = training.active ? training.grade : Grade.Poor;
        v.hits = training.hits;
        v.elapsed = training.elapsed;
        v.hitLimit = training.active ? training.passHits : -1;
        v.hitsLeft = v.hitLimit >= 0 ? Mathf.Max(0, v.hitLimit - training.hits) : -1;
        v.markHitA = MarkCells(v.hitLimit, training.excellentHits);
        v.markHitB = MarkCells(v.hitLimit, training.flawlessHits);
        v.timeLimit = training.active ? training.passTime : -1f;
        v.remaining = v.timeLimit > 0f ? Mathf.Max(0f, v.timeLimit - training.elapsed) : -1f;
        v.markTimeA = MarkRatio(v.timeLimit, training.excellentTime);
        v.markTimeB = MarkRatio(v.timeLimit, training.flawlessTime);

        // 난이도
        v.threat = threat != null && threat.HasThreat;
        if (v.threat)
        {
            v.level = threat.ThreatLevel;
            v.cleared = Mathf.Max(0, threat.ClearedRooms);
            v.nextAt = threat.NextLevelAt;
            v.density = threat.DensityMultiplier;
            if (v.nextAt >= 0)
            {
                var ladder = threat as IDungeonThreatLadder ?? map as IDungeonThreatLadder;
                if (ladder != null)
                {
                    int start = Mathf.Max(0, ladder.ThreatThreshold(v.level));
                    v.pipTotal = Mathf.Max(0, v.nextAt - start);
                    v.pipFilled = Mathf.Clamp(v.cleared - start, 0, v.pipTotal);
                }
                else
                {
                    v.pipTotal = Mathf.Max(0, v.nextAt - v.cleared);
                    v.pipFilled = 0;
                }
            }
        }

        // 구역(시작 방 제외 — 머리말 '구역 클리어 n/m'과 같은 셈), 상태순: 클리어 → 지금 방 → 방문 미클리어 → 알게 됨 → 모름, 보스는 끝
        var rooms = map != null && map.HasFloor ? map.Rooms : null;
        int current = map != null ? map.CurrentRoom : -1;
        var gate = map as IDungeonObjectiveGate ?? objectives as IDungeonObjectiveGate;
        int awaiting = gate != null && gate.AwaitingObjective ? gate.AwaitingRoom : -1;
        if (rooms != null)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (room == null || room.kind == DungeonRoomKind.Start) continue;
                v.roomsTotal++;
                if (room.cleared)
                {
                    v.roomsCleared++;
                    if (room.id >= 0 && room.id < 64) v.clearedRoomMask |= 1UL << room.id;
                }
            }
            int capacity = roomStrip != null ? DungeonTerminalGaugeGraphic.RoomChipCapacity(roomStrip.rectTransform.rect) : DungeonTerminalGaugeGraphic.MaxRoomChips;
            if (v.roomsTotal > capacity)
            {
                v.overflowTotal = v.roomsTotal;
                v.overflowCleared = v.roomsCleared;
            }
            else
                for (int pass = 0; pass < 6; pass++)
                    for (int i = 0; i < rooms.Count; i++)
                    {
                        var room = rooms[i];
                        if (room == null || room.kind == DungeonRoomKind.Start || RoomPass(room, current) != pass) continue;
                        if (v.roomCount >= roomCodes.Length) break;
                        roomCodes[v.roomCount] = RoomCode(room, current, awaiting);
                        roomIds[v.roomCount] = room.id;
                        v.roomCount++;
                    }
        }

        // 목표·출구
        v.objective = objectives != null && objectives.HasObjective;
        if (v.objective)
        {
            objectivesTitle = objectives.ObjectiveTitle ?? "";
            objectiveColor = objectives.ObjectiveColor;
            v.total = Mathf.Max(0, objectives.ObjectiveTotal);
            v.done = Mathf.Clamp(objectives.ObjectiveDone, 0, v.total);
            v.leverCount = Mathf.Min(v.total, DungeonTerminalGaugeGraphic.MaxLevers);
            var markers = objectives.ObjectiveMarkers;
            if (markers != null)
                for (int i = 0; i < markers.Count; i++)
                {
                    var m = markers[i];
                    if (m.index < 0 || m.index >= v.leverCount) continue;
                    int bit = 1 << m.index;
                    if (m.done) v.leverDone |= bit;
                    if (m.known) v.leverKnown |= bit;
                    if (m.room == current && current >= 0) v.leverCurrent |= bit;
                }
        }
        v.exit = exit != null && exit.HasExit;
        v.exitOpen = v.exit && exit.ExitUnlocked;
        cur = v;
    }

    private static int MarkCells(int passHits, int ruleHits) => passHits > 0 && ruleHits >= 0 && ruleHits < passHits ? passHits - ruleHits : -1;

    private static float MarkRatio(float passTime, float ruleTime) => passTime > 0f && ruleTime >= 0f && ruleTime < passTime ? 1f - ruleTime / passTime : -1f;

    // 0 클리어(보스 아님·지금 방 아님) · 1 지금 방(보스 아님) · 2 방문 미클리어 · 3 알게 됨 · 4 모름 · 5 보스
    private static int RoomPass(DungeonMapRoom room, int current)
    {
        if (room.kind == DungeonRoomKind.Boss) return 5;
        if (room.id == current) return 1;
        if (room.cleared) return 0;
        if (room.visited) return 2;
        return room.known ? 3 : 4;
    }

    private static int RoomCode(DungeonMapRoom room, int current, int awaiting)
    {
        int kind;
        if (room.kind == DungeonRoomKind.Boss) kind = room.cleared ? DungeonTerminalGaugeGraphic.RoomBossCleared : DungeonTerminalGaugeGraphic.RoomBoss;
        else if (room.cleared) kind = DungeonTerminalGaugeGraphic.RoomCleared;
        else if (room.visited) kind = DungeonTerminalGaugeGraphic.RoomVisited;
        else kind = room.known ? DungeonTerminalGaugeGraphic.RoomKnown : DungeonTerminalGaugeGraphic.RoomUnknown;
        if (room.id == current) kind |= DungeonTerminalGaugeGraphic.RoomCurrent;
        if (room.id == awaiting && awaiting >= 0) kind |= DungeonTerminalGaugeGraphic.RoomLever;
        if (room.rest) kind |= DungeonTerminalGaugeGraphic.RoomRest;
        return kind;
    }

    // ===== 글자(Refresh 때만) =====

    private void ApplyTexts()
    {
        // ① 등급
        if (gradeName != null)
        {
            gradeName.text = cur.training ? TrainingBoardStyle.GradeLabel(cur.grade) : "-";
            gradeName.color = cur.training ? TrainingBoardStyle.GradeColor(cur.grade) : Dim;
        }
        if (gradeNote != null)
        {
            if (gradeNote.enabled == cur.training) gradeNote.enabled = !cur.training;
            if (!cur.training) gradeNote.text = DungeonUiText.Static("기록 없음");
        }

        // ② 난이도(아이콘이 '난이도'를 대체하고 간결한 배율·NEXT/MAX만 표시)
        if (cur.threat)
        {
            if (threatName != null)
            {
                builder.Clear();
                builder.AppendColorTag(DungeonThreatText.Color(cur.level)).AppendStatic(DungeonThreatText.Label(cur.level)).Append("</color>");
                threatName.text = builder.ToString();
            }
            if (threatDensity != null)
            {
                builder.Clear();
                builder.AppendMultiplier(cur.density);
                threatDensity.text = builder.ToString();
            }
            if (threatNext != null) threatNext.text = cur.nextAt < 0 ? "MAX" : "NEXT";
        }

        if (hitLabel != null) hitLabel.text = DungeonUiText.Static("남은 피격");
        if (hitValue != null)
        {
            builder.Clear();
            if (!cur.training) builder.Append('-');
            else if (cur.hitLimit < 0) builder.AppendStatic("제한 없음");
            else builder.Append("<mspace=0.6em>").Append(cur.hitsLeft).Append("</mspace>").Append(DungeonUiText.SpaceTag).Append('/').Append(DungeonUiText.SpaceTag)
                        .Append(cur.hitLimit);
            hitValue.text = builder.ToString();
            hitValue.color = cur.training && cur.hitLimit >= 0 && cur.hitsLeft <= DangerHits ? Warning : Color.white;
        }
        if (hitKeep != null)
        {
            builder.Clear();
            Color keepColor = TrainingBoardStyle.GradeColor(cur.grade);
            if (cur.training)
            {
                if (!training.RuleFor(cur.grade, out int maxHits, out _)) { builder.Append("--"); keepColor = Warning; }
                else if (maxHits >= 0)
                {
                    int keep = Mathf.Max(0, maxHits - cur.hits);
                    builder.AppendStatic(TrainingBoardStyle.GradeLabel(cur.grade)).Append(DungeonUiText.SpaceTag).Append('+').Append(keep);
                    if (keep <= DangerHits && cur.grade == Grade.Pass) keepColor = Warning;
                }
            }
            hitKeep.text = builder.ToString();
            hitKeep.color = keepColor;
        }
        PlaceMarkLabels(hitMarkLabels, hitBar, cur.training && cur.hitLimit > 0, true);

        if (timeLabel != null) timeLabel.text = DungeonUiText.Static(cur.timeLimit > 0f || !cur.training ? "남은 시간" : "경과 시간");
        if (timeValue != null)
        {
            builder.Clear();
            if (!cur.training) builder.Append('-');
            else if (cur.timeLimit > 0f)
                builder.Append("<mspace=0.6em>").AppendClock(Mathf.Ceil(cur.remaining - .0001f)).Append("</mspace>").Append(DungeonUiText.SpaceTag).Append('/')
                       .Append(DungeonUiText.SpaceTag).AppendClock(cur.timeLimit);
            else builder.Append("<mspace=0.6em>").AppendClock(cur.elapsed).Append("</mspace>");
            timeValue.text = builder.ToString();
            timeValue.color = cur.training && cur.timeLimit > 0f && cur.remaining <= DangerSeconds ? Warning : Color.white;
        }
        if (timeKeep != null)
        {
            builder.Clear();
            if (cur.training && training.RuleFor(cur.grade, out _, out float maxTime) && maxTime >= 0f)
                builder.AppendStatic(TrainingBoardStyle.GradeLabel(cur.grade)).Append(DungeonUiText.SpaceTag).AppendClock(maxTime - cur.elapsed);
            timeKeep.text = builder.ToString();
            timeKeep.color = TrainingBoardStyle.GradeColor(cur.grade);
        }
        PlaceMarkLabels(timeMarkLabels, timeTrack, cur.training && cur.timeLimit > 0f, false);

        // ⑤ 구역(3x3 섹터 아이콘 + 간결 숫자 n/m)
        if (roomIcon != null && roomIcon.enabled != cur.hasMap) roomIcon.enabled = cur.hasMap;
        if (roomLabel != null)
        {
            if (roomLabel.enabled != cur.hasMap) roomLabel.enabled = cur.hasMap;
            if (cur.hasMap)
            {
                builder.Clear();
                if (roomIcon == null) builder.AppendStatic("구역 클리어 ");
                builder.Append(cur.roomsCleared).Append('/').Append(cur.roomsTotal);
                roomLabel.text = builder.ToString();
            }
            else roomLabel.text = "";
        }

        // ⑥⑦ 목표·출구(마름모 아이콘이 '목표' 머리말 대체, 레버 상태는 OK/!/? 기호로 간결화)
        bool showObjectiveSection = cur.hasMap || cur.objective;
        if (dividerObject != null && dividerObject.activeSelf != showObjectiveSection) dividerObject.SetActive(showObjectiveSection);
        if (objectiveHeaderObject != null && objectiveHeaderObject.activeSelf != (showObjectiveSection && objectiveIcon == null))
            objectiveHeaderObject.SetActive(showObjectiveSection && objectiveIcon == null);
        if (objectiveIcon != null && objectiveIcon.enabled != showObjectiveSection) objectiveIcon.enabled = showObjectiveSection;
        if (objectiveTitle != null)
        {
            if (objectiveTitle.enabled != showObjectiveSection) objectiveTitle.enabled = showObjectiveSection;
            if (!showObjectiveSection) objectiveTitle.text = "";
            else
            {
                builder.Clear();
                if (!cur.objective) builder.AppendColorTag(Dim).AppendStatic("목표 없음").Append("</color>");
                else
                {
                    string title = objectivesTitle;
                    builder.AppendStatic(string.IsNullOrEmpty(title) ? "목표" : title);
                }
                objectiveTitle.text = builder.ToString();
                objectiveTitle.color = cur.objective ? (Color)objectiveColor : Dim;
            }
        }
        if (objectiveCount != null)
        {
            if (objectiveCount.enabled != showObjectiveSection) objectiveCount.enabled = showObjectiveSection;
            builder.Clear();
            if (showObjectiveSection && cur.objective) builder.Append(cur.done).Append('/').Append(cur.total);
            objectiveCount.text = builder.ToString();
        }
        if (leverLabels != null)
        {
            var area = leverRow != null ? leverRow.rectTransform.rect : default;
            for (int i = 0; i < leverLabels.Length; i++)
            {
                var label = leverLabels[i];
                if (label == null) continue;
                bool show = cur.objective && i < cur.leverCount;
                if (label.enabled != show) label.enabled = show;
                if (!show) continue;
                bool isDone = (cur.leverDone & (1 << i)) != 0, known = (cur.leverKnown & (1 << i)) != 0;
                label.text = isDone ? "OK" : known ? "!" : "?";
                label.color = isDone ? (Color)objectiveColor : known ? Color.white : Dim;
                if (leverRow != null)
                {
                    // 글자는 LeverRow 그래픽의 자식(anchor 가운데) — 부모 사각형 가운데 기준 자리
                    var p = DungeonTerminalGaugeGraphic.LeverLabelPoint(area, cur.leverCount, i) - area.center;
                    var rt = label.rectTransform;
                    if ((rt.anchoredPosition - p).sqrMagnitude > 1e-4f) rt.anchoredPosition = p;
                }
            }
        }
        if (exitText != null)
        {
            builder.Clear();
            if (cur.objective && cur.exit)
            {
                builder.AppendStatic("출구").Append(DungeonUiText.SpaceTag);
                if (cur.exitOpen) builder.AppendColorTag(objectiveColor).AppendStatic("개방").Append("</color>").AppendSeparator().AppendStatic("시작 방");
                else builder.AppendColorTag(Dim).AppendStatic("잠김").Append(DungeonUiText.SpaceTag).Append(cur.done).Append('/').Append(cur.total).Append("</color>");
            }
            exitText.text = builder.ToString();
        }
    }

    // 원본에서 읽은 목표 제목·색(Gather가 정한다 — 글자는 ApplyTexts에서만 쓴다).
    private string objectivesTitle = "";
    private Color32 objectiveColor = new Color32(255, 176, 33, 255);

    // 등급 눈금 이름표(우수·무결점): 눈금 x 아래. 가까우면(46px) 뒤 것을 숨긴다. 바가 없으면 모두 숨긴다.
    private void PlaceMarkLabels(TMP_Text[] labels, DungeonTerminalGaugeGraphic bar, bool visible, bool cells)
    {
        if (labels == null) return;
        var r = bar != null ? bar.rectTransform.rect : default;
        float lastX = float.MinValue;
        for (int i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            if (label == null) continue;
            Grade g = i == 0 ? Grade.Excellent : Grade.Flawless;
            float x = float.NaN;
            if (visible && bar != null)
            {
                if (cells)
                {
                    int at = i == 0 ? cur.markHitA : cur.markHitB;
                    if (at > 0) x = DungeonTerminalGaugeGraphic.CellMarkX(r, cur.hitLimit, at);
                }
                else
                {
                    float at = i == 0 ? cur.markTimeA : cur.markTimeB;
                    if (at > 0f) x = Mathf.Lerp(r.xMin, r.xMax, at);
                }
            }
            bool show = !float.IsNaN(x) && Mathf.Abs(x - lastX) >= 46f;
            if (label.enabled != show) label.enabled = show;
            if (!show) continue;
            lastX = x;
            label.text = TrainingBoardStyle.GradeLabel(g);
            label.color = TrainingBoardStyle.GradeColor(g);
            // 이름표는 바 그래픽의 자식(anchor 가운데) — 부모 사각형 가운데 기준 자리
            var p = new Vector2(Mathf.Clamp(x, r.xMin + 18f, r.xMax - 18f), r.yMin - 9f) - r.center;
            var rt = label.rectTransform;
            if ((rt.anchoredPosition - p).sqrMagnitude > 1e-4f) rt.anchoredPosition = p;
        }
    }

    // ===== 위젯 정적 상태(Refresh 때만) =====

    private void ApplyStatic()
    {
        // ① 칩: 등급 색 판(기록 없으면 중립 판), 난이도가 없으면 넓게
        if (gradePlate != null) gradePlate.SetChip(TrainingBoardStyle.GradeColor(cur.grade), !cur.training);
        if (gradeRoot != null)
        {
            float w = cur.threat ? gradeChipWidth : gradeChipWideWidth;
            if (Mathf.Abs(gradeRoot.sizeDelta.x - w) > .01f) gradeRoot.sizeDelta = new Vector2(w, gradeRoot.sizeDelta.y);
        }
        if (gradeGlow != null)
        {
            Color c = cur.training ? TrainingBoardStyle.GradeColor(cur.grade) : Dim;
            c.a = 1f;
            if (gradeGlow.color != c) gradeGlow.color = c;
        }
        // ② 난이도
        if (threatGroup != null) threatGroup.alpha = cur.threat ? 1f : 0f;
        if (threatPips != null)
            threatPips.SetPips(cur.threat && cur.nextAt >= 0 ? cur.pipTotal : 0, cur.pipFilled, DungeonThreatText.Color(cur.level));
        // ③④ 바 보임
        if (hitBarGroup != null) hitBarGroup.alpha = cur.training && cur.hitLimit > 0 ? 1f : 0f;
        if (timeBarGroup != null) timeBarGroup.alpha = cur.training && cur.timeLimit > 0f ? 1f : 0f;
        if (timeTrack != null) timeTrack.SetTrack(cur.training ? cur.timeLimit : -1f);
        if (timeMarks != null)
            timeMarks.SetMarks(cur.markTimeA, TrainingBoardStyle.GradeColor(Grade.Excellent), cur.markTimeB, TrainingBoardStyle.GradeColor(Grade.Flawless));
        if (timeFillImage != null)
        {
            Color c = TrainingBoardStyle.GradeColor(cur.grade);
            if (timeFillImage.color != c) timeFillImage.color = c;
        }
        // ⑤ 구역
        if (roomStrip != null) roomStrip.SetRooms(roomCodes, cur.roomCount, roomStrip.RoomVisible, cur.overflowTotal, cur.overflowCleared);
        // ⑥⑦ 목표
        if (objectiveGroup != null) objectiveGroup.alpha = cur.objective ? 1f : 0f;
        // Sci-Fi 아이콘 색 갱신(값이 같으면 Mesh를 다시 만들지 않음)
        if (gradeReticle != null) gradeReticle.SetIconColor(cur.training ? (Color32)TrainingBoardStyle.GradeColor(cur.grade) : (Color32)Dim);
        if (threatIcon != null) threatIcon.SetIconColor(cur.threat ? (Color32)DungeonThreatText.Color(cur.level) : (Color32)Dim);
        bool hitDanger = cur.training && cur.hitLimit >= 0 && cur.hitsLeft <= DangerHits;
        bool timeDanger = cur.training && cur.timeLimit > 0f && cur.remaining <= DangerSeconds;
        if (hitIcon != null) hitIcon.SetIconColor(hitDanger ? (Color32)Warning : DungeonTerminalGaugeGraphic.Line);
        if (timeIcon != null) timeIcon.SetIconColor(timeDanger ? (Color32)Warning : DungeonTerminalGaugeGraphic.Line);
        if (roomIcon != null) roomIcon.SetIconColor(cur.hasMap ? DungeonTerminalGaugeGraphic.Line : (Color32)Dim);
        if (objectiveIcon != null) objectiveIcon.SetIconColor(cur.objective ? objectiveColor : (Color32)Dim);
    }

    // ===== '지난번 열람 뒤' 강조(PlayIntro 때만 — Flash 목록을 한 번 만든다) =====

    private void BuildHighlights()
    {
        hitLost = gradeLost = timeLost = newExit = false;
        roomNewFirst = leverNewFirst = -1;
        leverNewCount = 0;
        igniteMask = 0;
        sparkStart = -1f;
        if (hitFlash != null) hitFlash.FlashClear();
        if (gradeFlash != null) gradeFlash.FlashClear();
        if (timeFlash != null) timeFlash.FlashClear();
        if (roomFlash != null) roomFlash.FlashClear();
        if (leverFlash != null) leverFlash.FlashClear();
        if (hasPrev)
        {
            // 잃은 피격 칸(남은 양이 줄어든 구간)
            if (cur.training && prev.training && cur.hitLimit == prev.hitLimit && cur.hitLimit > 0 && cur.hitLimit <= DungeonTerminalGaugeGraphic.MaxSlantCells
                && prev.hitsLeft > cur.hitsLeft && hitFlash != null && hitBar != null)
            {
                var r = hitFlash.rectTransform.rect;   // Flash 층 자기 사각형(대상 바와 같은 크기·자리)
                for (int i = Mathf.Max(0, cur.hitsLeft); i < prev.hitsLeft && i < cur.hitLimit; i++)
                {
                    DungeonTerminalGaugeGraphic.CellQuad(r, cur.hitLimit, i, out var a, out var b, out var c, out var d);
                    hitFlash.FlashAddQuad(a, b, c, d);
                }
                hitLost = hitFlash.FlashQuads > 0;
            }
            // 떨어진 등급 칸
            if (cur.training && prev.training && prev.grade > cur.grade && gradeFlash != null && gradeLadder != null)
            {
                var r = gradeFlash.rectTransform.rect;
                for (int i = (int)cur.grade + 1; i <= (int)prev.grade; i++)
                {
                    DungeonTerminalGaugeGraphic.LadderQuad(r, i, out var a, out var b, out var c, out var d);
                    gradeFlash.FlashAddQuad(a, b, c, d);
                }
                gradeLost = gradeFlash.FlashQuads > 0;
            }
            // 줄어든 시간(흰빛 띠)
            if (cur.training && prev.training && cur.timeLimit > 0f && Mathf.Abs(cur.timeLimit - prev.timeLimit) < .01f && prev.remaining > cur.remaining + .5f
                && timeFlash != null && timeTrack != null)
            {
                // Flash 층의 자기 사각형 기준(Track 위에 펴진 자식 — 크기는 Track과 같고 피벗만 다르다)
                var r = timeFlash.rectTransform.rect;
                var band = Rect.MinMaxRect(r.xMin + TimeFillInset.x, r.yMin + TimeFillInset.y, r.xMax - TimeFillInset.x, r.yMax - TimeFillInset.y);
                float x0 = Mathf.Lerp(band.xMin, band.xMax, cur.remaining / cur.timeLimit), x1 = Mathf.Lerp(band.xMin, band.xMax, prev.remaining / prev.timeLimit);
                if (x1 - x0 > .5f)
                {
                    timeFlash.FlashAddRect(Rect.MinMaxRect(x0, band.yMin, x1, band.yMax));
                    timeLost = true;
                }
            }
            // 새로 클리어한 구역 칩
            if (roomFlash != null && roomStrip != null && cur.overflowTotal == 0)
            {
                var r = roomFlash.rectTransform.rect;
                for (int s = 0; s < cur.roomCount; s++)
                {
                    int id = roomIds[s];
                    if (id < 0 || id >= 64) continue;
                    ulong bit = 1UL << id;
                    if ((cur.clearedRoomMask & bit) == 0 || (prev.clearedRoomMask & bit) != 0) continue;
                    DungeonTerminalGaugeGraphic.RoomChipQuad(r, s, out var a, out var b, out var c, out var d);
                    roomFlash.FlashAddQuad(a + new Vector2(-1.5f, -1.5f), b + new Vector2(1.5f, -1.5f), c + new Vector2(1.5f, 1.5f), d + new Vector2(-1.5f, 1.5f));
                    if (roomNewFirst < 0) roomNewFirst = s;
                }
            }
            // 새로 당긴 레버 → 전류 → 출구 램프
            if (cur.objective && prev.objective && cur.leverCount == prev.leverCount)
            {
                int fresh = cur.leverDone & ~prev.leverDone;
                if (fresh != 0)
                {
                    var r = leverFlash != null ? leverFlash.rectTransform.rect : leverRow != null ? leverRow.rectTransform.rect : default;
                    float side = DungeonTerminalGaugeGraphic.LeverSide(r, cur.leverCount);
                    for (int i = 0; i < cur.leverCount; i++)
                    {
                        if ((fresh & (1 << i)) == 0) continue;
                        leverNewCount++;
                        if (leverNewFirst < 0) leverNewFirst = i;
                        if (leverFlash != null && leverRow != null)
                        {
                            var c = DungeonTerminalGaugeGraphic.LeverCenter(r, cur.leverCount, i);
                            float h = side * .5f + 2f;
                            leverFlash.FlashAddRect(Rect.MinMaxRect(c.x - h, c.y - h, c.x + h, c.y + h));
                        }
                    }
                    sparkStart = LeverStart + cur.leverCount * LeverStep + .15f;
                }
                newExit = !prev.exitOpen && cur.exitOpen;
            }
            // 오른 난이도 칸(지난 단계 위 ~ 지금 단계)
            if (cur.threat && prev.threat && cur.level > prev.level)
                for (int i = (int)prev.level + 1; i <= (int)cur.level; i++) igniteMask |= 1 << i;
        }
        if (hitFlash != null) hitFlash.FlashCommit(LostRed);
        if (gradeFlash != null) gradeFlash.FlashCommit(LostRed);
        if (timeFlash != null) timeFlash.FlashCommit(TimeWhite);
        if (roomFlash != null) roomFlash.FlashCommit(RoomGreen);
        if (leverFlash != null) leverFlash.FlashCommit(LeverWhite);
    }

    // ===== 시간표(결정적 — 같은 t면 같은 모습) =====

    private static int Steps(float t, float start, float step, int max) => t < start ? 0 : Mathf.Min(max, 1 + Mathf.FloorToInt((t - start) / step + 1e-4f));

    private static float Fade(float t, float start, float length) => t < start ? 0f : 1f - TrainingBoardMotion.Smooth(TrainingBoardMotion.Seg(t, start, start + length));

    private void ApplyFrame(float t)
    {
        bool final = t >= IntroLength;
        // ① 등급 사다리
        if (gradeLadder != null) gradeLadder.SetLadder(cur.training ? (int)cur.grade : -1, final ? 4 : Steps(t, GradeStart, GradeStep, 4));
        if (gradeFlash != null) gradeFlash.canvasRenderer.SetAlpha(gradeLost ? Fade(t, GradeStart, LostFlash) : 0f);

        // ② 난이도 사다리(아래부터 차례로, 오른 칸은 잠깐 점화)
        if (threatLadder != null && cur.threat)
        {
            int top = Mathf.Clamp((int)cur.level, 0, DungeonThreatGlyph.StepCount - 1) + 1;
            int lit = final ? top : Steps(t, ThreatStart, ThreatStep, top);
            int ignite = 0;
            if (!final)
                for (int i = 0; i < lit; i++)
                {
                    if ((igniteMask & (1 << i)) == 0) continue;
                    float at = ThreatStart + i * ThreatStep;
                    if (t >= at && t < at + IgniteTime) ignite |= 1 << i;
                }
            threatLadder.SetState(cur.level, (1 << lit) - 1, ignite);
            threatLadder.SetPointerVisible(lit >= top);
        }

        // ③ 남은 피격(채움: 처음 0 → 지금, 지난 값이 더 크면 지금 값으로 떨어지고 잃은 칸이 붉게 식는다)
        if (hitBar != null)
        {
            int total = cur.training && cur.hitLimit > 0 ? cur.hitLimit : 0;
            int target = Mathf.Clamp(cur.hitsLeft, 0, total);
            int from = hasPrev && prev.training && prev.hitLimit == cur.hitLimit ? Mathf.Clamp(prev.hitsLeft, 0, total) : 0;
            int shown;
            if (final) shown = target;
            else if (t < HitStart) shown = from;
            else if (from >= target) shown = target;
            else shown = Mathf.RoundToInt(Mathf.Lerp(from, target, TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, HitStart, HitStart + HitFillDuration))));
            Color32 color = TrainingBoardStyle.GradeColor(cur.grade);
            hitBar.SetCells(total, shown, total, color, cur.markHitA, TrainingBoardStyle.GradeColor(Grade.Excellent), cur.markHitB, TrainingBoardStyle.GradeColor(Grade.Flawless));
        }
        if (hitFlash != null) hitFlash.canvasRenderer.SetAlpha(hitLost ? Fade(t, HitStart, LostFlash) : 0f);

        // ④ 남은 시간(Fill anchorMax.x만)
        float ratio = 0f;
        if (cur.training && cur.timeLimit > 0f)
        {
            float target = Mathf.Clamp01(cur.remaining / cur.timeLimit);
            float from = hasPrev && prev.training && Mathf.Abs(prev.timeLimit - cur.timeLimit) < .01f ? Mathf.Clamp01(prev.remaining / prev.timeLimit) : 0f;
            if (final) ratio = target;
            else if (t < TimeStart) ratio = from;
            else if (from >= target) ratio = target;
            else ratio = Mathf.Lerp(from, target, TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, TimeStart, TimeStart + TimeFillDuration)));
        }
        SetTimeRatio(ratio);
        if (timeFlash != null) timeFlash.canvasRenderer.SetAlpha(timeLost ? Fade(t, TimeStart, LostFlash) : 0f);

        // ⑤ 구역 칩(차례로)
        if (roomStrip != null)
        {
            int count = cur.overflowTotal > 0 ? 1 : cur.roomCount;
            roomStrip.SetRoomVisible(final ? count : Steps(t, RoomStart, RoomStep, count));
        }
        if (roomFlash != null)
            roomFlash.canvasRenderer.SetAlpha(roomNewFirst >= 0 ? DungeonMinimap.ClearPulseAlpha(t - (RoomStart + roomNewFirst * RoomStep), RoomFlashDuration) : 0f);

        // ⑥ 레버 배지·버스선(차례로), 새로 당긴 레버 번쩍
        int levers = cur.objective ? cur.leverCount : 0;
        int leverVisible = final ? levers : Steps(t, LeverStart, LeverStep, levers);
        if (leverRow != null) leverRow.SetLevers(levers, cur.leverDone, cur.leverKnown, cur.leverCurrent, leverVisible);
        if (conduit != null) conduit.SetLevers(levers, cur.leverDone, cur.leverKnown, cur.leverCurrent, leverVisible);
        if (leverFlash != null)
            leverFlash.canvasRenderer.SetAlpha(leverNewFirst >= 0 ? DungeonMinimap.ClearPulseAlpha(t - (LeverStart + leverNewFirst * LeverStep), LeverFlashDuration) : 0f);

        // ⑦ 출구: 전류가 닿은 뒤 램프가 하나씩 켜지고, 새로 해금됐으면 마지막에 열린다
        int lamps = cur.done;
        bool open = cur.exitOpen;
        if (!final && hasPrev)
        {
            if (sparkStart >= 0f)
            {
                float landed = sparkStart + SparkTime;
                int added = t < landed ? 0 : Mathf.Min(leverNewCount, 1 + Mathf.FloorToInt((t - landed) / LampStep));
                lamps = Mathf.Clamp(prev.done + added, 0, cur.done);
                if (newExit) open = t >= landed + leverNewCount * LampStep + .1f;
            }
            else if (newExit) open = t >= LeverStart + levers * LeverStep + .2f;
        }
        if (exitBadge != null) exitBadge.SetExit(cur.objective && cur.exit, open, lamps, cur.total);
        if (conduit != null)
        {
            conduit.SetExit(cur.objective && cur.exit, open, lamps, cur.total);
            conduit.SetConduitOpen(open);
        }
    }

    private void SetTimeRatio(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        if (timeFill != null && Mathf.Abs(timeFill.anchorMax.x - ratio) > 1e-5f) timeFill.anchorMax = new Vector2(ratio, timeFill.anchorMax.y);
        if (timeHead != null)
        {
            if (Mathf.Abs(timeHead.anchorMin.x - ratio) > 1e-5f)
            {
                timeHead.anchorMin = new Vector2(ratio, timeHead.anchorMin.y);
                timeHead.anchorMax = new Vector2(ratio, timeHead.anchorMax.y);
            }
            // 앞끝 2px: 켜고 끄기는 알파로(SetActive는 연출 도중 할당·Canvas 재등록을 부를 수 있다)
            bool on = ratio > .001f && ratio < .999f && cur.training && cur.timeLimit > 0f;
            if (headRenderer == null) headRenderer = timeHead.GetComponent<CanvasRenderer>();
            if (headRenderer != null) headRenderer.SetAlpha(on ? 1f : 0f);
        }
    }

    // ===== 상시 연출(매 프레임 — 알파·자리만, 할당 없음) =====

    private void Ambient()
    {
        float t = introActive ? introTime : IntroLength;
        // ① 등급 빛: 열 때 번쩍(.9 → .28) → 숨쉬기
        if (gradeGlow != null)
        {
            float glow;
            if (!cur.training || t < GradeStart) glow = 0f;
            else if (t < GradeStart + GlowFlash) glow = Mathf.Lerp(.9f, .28f, TrainingBoardMotion.Seg(t, GradeStart, GradeStart + GlowFlash));
            else glow = .28f + .08f * Mathf.Sin(clock * Mathf.PI * 2f / GlowPeriod);
            gradeGlow.canvasRenderer.SetAlpha(glow);
        }
        // ② 지금 단계 맥동(사다리 전체 알파 — Mesh 그대로)
        if (threatLadder != null)
            threatLadder.canvasRenderer.SetAlpha(t >= IntroLength * .5f ? .84f + .16f * (.5f + .5f * Mathf.Cos(clock * Mathf.PI * 2f / LadderPeriod)) : 1f);
        // ③④ 위험 깜박임(숫자 + 아이콘 알파)
        bool hitDanger = cur.training && cur.hitLimit >= 0 && cur.hitsLeft <= DangerHits;
        bool timeDanger = cur.training && cur.timeLimit > 0f && cur.remaining <= DangerSeconds;
        float hitAlpha = hitDanger ? Blink(DangerPeriod) : 1f;
        float timeAlpha = timeDanger ? Blink(DangerPeriod) : 1f;
        if (hitValue != null) hitValue.canvasRenderer.SetAlpha(hitAlpha);
        if (hitIcon != null) hitIcon.canvasRenderer.SetAlpha(hitAlpha);
        if (timeValue != null) timeValue.canvasRenderer.SetAlpha(timeAlpha);
        if (timeIcon != null) timeIcon.canvasRenderer.SetAlpha(timeAlpha);
        // Sci-Fi 등급 홀로그램 조준 고리 회전 + 숨쉬기
        if (gradeReticle != null)
        {
            gradeReticle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -clock * 24f);
            float ra = cur.training ? .44f + .24f * (.5f + .5f * Mathf.Sin(clock * 2.4f)) : .18f;
            gradeReticle.canvasRenderer.SetAlpha(ra);
        }
        // Sci-Fi 시계 바늘 회전(위험 구간에서 가속)
        if (clockHand != null)
        {
            float speed = timeDanger ? 140f : 42f;
            clockHand.localRotation = Quaternion.Euler(0f, 0f, -clock * speed);
        }
        // Sci-Fi 상단 텔레메트리 파형 막대(할당 없음)
        if (telemetryBars != null)
        {
            for (int i = 0; i < telemetryBars.Length; i++)
            {
                var bar = telemetryBars[i];
                if (bar == null) continue;
                float sy = .25f + .75f * (.5f + .5f * Mathf.Sin(clock * (3.2f + i * .7f) + i * 1.15f));
                bar.localScale = new Vector3(1f, sy, 1f);
            }
        }
        // Sci-Fi 세로 홀로그램 스캔 바(판 전체를 부드럽게 위→아래 훑음)
        if (scanBar != null && scanBarImage != null)
        {
            float phase = Mathf.Repeat(clock, ScanPeriod);
            float scanAlpha = 0f;
            if (phase < ScanTime)
            {
                float u = phase / ScanTime;
                float y = Mathf.Lerp(scanRangeY.x, scanRangeY.y, u);
                var pos = scanBar.anchoredPosition;
                if (Mathf.Abs(pos.y - y) > 1e-3f) scanBar.anchoredPosition = new Vector2(pos.x, y);
                scanAlpha = .16f * TrainingBoardMotion.Pulse(phase, 0f, ScanTime);
            }
            scanBarImage.canvasRenderer.SetAlpha(scanAlpha);
        }
        // 점검 빛: 2.6초마다 바 위를 한 번 훑는다(열림 연출이 끝난 뒤)
        if (sweep != null && sweepImage != null)
        {
            float alpha = 0f;
            if (cur.training && (cur.hitLimit > 0 || cur.timeLimit > 0f) && t >= 1f)
            {
                float phase = Mathf.Repeat(clock, SweepPeriod);
                if (phase < SweepTime)
                {
                    float x = Mathf.Lerp(sweepRange.x, sweepRange.y, phase / SweepTime);
                    if (Mathf.Abs(sweep.anchoredPosition.x - x) > 1e-3f) sweep.anchoredPosition = new Vector2(x, sweep.anchoredPosition.y);
                    alpha = .55f * TrainingBoardMotion.Pulse(phase, 0f, SweepTime);
                }
            }
            sweepImage.canvasRenderer.SetAlpha(alpha);
        }
        // ⑦ 출구 개방 맥동 + 목표 아이콘 맥동 + 전류
        bool open = exitBadge != null && exitBadge.ExitOpen;
        if (exitBadge != null)
            exitBadge.canvasRenderer.SetAlpha(open && t >= IntroLength ? .7f + .3f * (.5f + .5f * Mathf.Cos(clock * Mathf.PI * 2f / ExitPulsePeriod)) : 1f);
        if (objectiveIcon != null)
            objectiveIcon.canvasRenderer.SetAlpha(open && t >= IntroLength ? .65f + .35f * (.5f + .5f * Mathf.Cos(clock * Mathf.PI * 2f / ExitPulsePeriod)) : 1f);
        UpdateSpark(t, open);
    }

    private float Blink(float period) => .45f + .55f * (.5f + .5f * Mathf.Cos(clock * Mathf.PI * 2f / period));

    // 전류 빛: 새로 당긴 레버가 있으면 그 레버에서 출구까지 한 번(0.3초), 개방 뒤에는 2초마다 첫 레버에서 출구까지.
    private void UpdateSpark(float t, bool open)
    {
        if (spark == null || sparkImage == null) return;
        float alpha = 0f;
        var area = leverRow != null ? leverRow.rectTransform.rect : default;
        int levers = cur.objective ? cur.leverCount : 0;
        if (levers > 0 && cur.exit && leverRow != null)
        {
            float u = -1f;
            int lever = 0;
            if (sparkStart >= 0f && introActive && t >= sparkStart && t < sparkStart + SparkTime)
            {
                u = (t - sparkStart) / SparkTime;
                lever = Mathf.Max(0, leverNewFirst);
            }
            else if (open && !introActive)
            {
                float phase = Mathf.Repeat(clock, ExitSparkPeriod);
                if (phase < .6f) u = phase / .6f;
            }
            if (u >= 0f)
            {
                // 전류 빛은 LeverRow 그래픽의 자식(anchor 가운데) — 부모 사각형 가운데 기준 자리
                var p = DungeonTerminalGaugeGraphic.ConduitPoint(area, levers, lever, u) - area.center;
                if ((spark.anchoredPosition - p).sqrMagnitude > 1e-4f) spark.anchoredPosition = p;
                alpha = 1f - .4f * u;
            }
        }
        sparkImage.canvasRenderer.SetAlpha(alpha);
    }
}
