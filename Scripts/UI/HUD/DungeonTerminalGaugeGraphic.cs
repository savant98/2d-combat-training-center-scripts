using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// 7차(2026-11-26, DESIGN_V7 §1.6 V6) 현장 단말기 '계기판형' 정보 판의 생성 Mesh 그래픽. MaskableGraphic + OnPopulateMesh, 기본 UI 재질(정점 색 = 화면색 sRGB),
/// 레이 차단 끔(생성 도구가 정한다). 한 클래스가 아래 부품(Part) 중 하나를 그린다. 값 설정 함수는 값이 같으면 다시 그리지 않는다(<see cref="DirtyCount"/>로 검증).
/// - Chip: 판(<see cref="DungeonMapMesh.WritePlate"/> — 45° 깎인 모서리·얇은 외곽선·밝은 꺾쇠, 머리말 없음). 강조색(등급 색) 판 또는 중립 청록 판.
/// - Ladder4: 성적 등급 4칸 비스듬한(20°) 사다리(미흡 → 무결점, 코너판 TrainingStatusView.PaintLadder와 같은 규칙 — 지금 칸 1.0·아래 칸 0.5·위 칸 빈 칸).
/// - CellBar: 남은 피격 — 한도 ≤ 48이면 20° 비스듬한 칸(남은 양만큼 켬, 왼쪽부터), 넘으면 연속 바 + 5칸마다 틱. 더 높은 등급을 잃는 지점에 등급색 세로 눈금 2개.
/// - Track: 남은 시간 바 바탕(빈 칸 + 1분 틱 — 길면 5분). 채움은 생성 도구가 둔 Fill 자식 Image의 anchorMax.x(Mesh 재생성 없음), 눈금은 Marks 부품이 위에 그린다.
/// - Marks: 비율 자리(0~1) 등급색 세로 눈금 2개(Track 위).
/// - RoomStrip: 구역 클리어 칩 띠(최대 16, 넘으면 연속 바). 칩 상태는 <see cref="RoomKindMask"/> 종류 + 지금 방·레버 대기·휴식 비트. 지금 방 = 흰 꺾쇠 + 위 작은 흰 마름모(색이 아니라 모양).
/// - LeverRow·Conduit·ExitBadge: 같은 사각형(목표 영역)을 함께 쓰는 세 층 — 레버 배지 줄(지도와 같은 <see cref="DungeonMapMesh.WriteObjectiveBadge"/>, 지금 방 레버 = 흰 꺾쇠),
///   배지 아래 세로선 → 가로 버스선 → 출구 배지(완료 레버 세로선·해금된 버스 = 앰버), 출구 배지(문틀 + 램프 = 레버 수). 자리는 <see cref="LeverCenter"/>·<see cref="ExitCenter"/>·<see cref="ConduitPoint"/>.
/// - Pips: 작은 깎인 칸 점(난이도 '다음 단계까지' 남은 방 — 채운 점/빈 점).
/// - Flash: '지난번 열람 뒤 바뀐 것' 강조 사각형·사각 꼴 목록(부르는 쪽이 대상 부품의 자리 함수로 채운다). 알파는 부르는 쪽이 CanvasRenderer로 움직인다.
/// 시각 언어: 얇은 발광선 Sci-fi(굵은 부품·톱니·액체 효과 없음), 바탕 채움은 중립 남색, 테마색은 선·칸·글자에만. 크기 변형 없음. 할당 없음(고정 버퍼).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DungeonTerminalGaugeGraphic : MaskableGraphic
{
    public enum Part
    {
        Chip = 0,
        Ladder4 = 1,
        CellBar = 2,
        Track = 3,
        RoomStrip = 4,
        LeverRow = 5,
        Conduit = 6,
        ExitBadge = 7,
        Flash = 8,
        Pips = 9,
        Marks = 10,
        ActionButton = 11,
        IconShield = 12,
        IconClock = 13,
        IconSector = 14,
        IconThreat = 15,
        IconObjective = 16,
        SciFiFrame = 17,
        Reticle = 18,
        ActiveFrame = 19,
        PopupFrame = 20,
    }

    /// <summary>ActionButton 색상 톤(0 청록·1 앰버·2 경고 빨강).</summary>
    public const int ToneCyan = 0, ToneAmber = 1, ToneDanger = 2;
    /// <summary>ActionButton 좌측 아이콘 종류(0 없음·1 지도·2 재시작·3 포기·4 확인·5 취소).</summary>
    public const int IconNone = 0, IconMap = 1, IconRestart = 2, IconGiveUp = 3, IconCheck = 4, IconCancel = 5;
    /// <summary>MapActiveFrame 표시 모드(0 숨김·1 구역 지도 대형 버튼 선택 하이라이트·2 상세 맵 탐색 활성).</summary>
    public const int MapFrameHidden = 0, MapFrameSelected = 1, MapFrameDetail = 2;

    /// <summary>비스듬한 칸으로 그리는 피격 한도 상한(넘으면 연속 바).</summary>
    public const int MaxSlantCells = 48;
    /// <summary>구역 칩 상한(넘으면 연속 바).</summary>
    public const int MaxRoomChips = 16;
    /// <summary>레버 배지 상한(출구 램프 상한 <see cref="DungeonMapMesh.MaxExitLamps"/>와 같다).</summary>
    public const int MaxLevers = 6;
    /// <summary>비스듬한 칸 기울기(tan 20°, PlayerHUD 체력 칸·코너판 피격 칸과 같다).</summary>
    public const float Slant = .364f;
    /// <summary>Flash 사각 꼴 상한.</summary>
    public const int MaxFlashQuads = 64;

    // 구역 칩 상태(하위 3비트 = 종류, 위 비트 = 덧붙임)
    public const int RoomUnknown = 0, RoomKnown = 1, RoomVisited = 2, RoomCleared = 3, RoomBoss = 4, RoomBossCleared = 5;
    public const int RoomKindMask = 7, RoomCurrent = 1 << 3, RoomLever = 1 << 4, RoomRest = 1 << 5;

    // 색(화면색 — 던전 UI Canvas는 정점 색 감마 공간)
    public static readonly Color32 Neutral = new Color32(8, 12, 18, 205);          // 중립 남색 바탕(테마색 채움 금지)
    public static readonly Color32 Line = new Color32(43, 200, 226, 255);          // #2bc8e2
    public static readonly Color32 LineDim = new Color32(23, 75, 92, 255);         // #174b5c
    public static readonly Color32 LineBright = new Color32(143, 244, 255, 255);   // #8ff4ff
    public static readonly Color32 Frame = new Color32(244, 248, 255, 255);        // 지금 방·지금 레버 꺾쇠(큰 지도 Current 층과 같은 흰색)
    public static readonly Color32 Amber = new Color32(255, 176, 33, 255);         // 목표 앰버 #FFB021
    public static readonly Color32 AmberDim = new Color32(110, 72, 14, 255);
    public static readonly Color32 ClearGreen = new Color32(51, 242, 89, 255);     // #33f259
    public static readonly Color32 Uncleared = new Color32(255, 179, 71, 255);     // 방문·미클리어 점 #ffb347(지도와 같다)
    public static readonly Color32 BossRed = new Color32(255, 90, 60, 255);        // #ff5a3c
    public static readonly Color32 TickDark = new Color32(6, 10, 16, 210);

    [SerializeField] private Part part = Part.Chip;

    // Chip
    private Color32 chipAccent = new Color32(43, 200, 226, 255);
    private bool chipNeutral = true;
    private DungeonMapMesh.Style chipStyle, neutralStyle;
    // Ladder4
    private int ladderGrade = -1, ladderVisible = 4;
    // CellBar
    private int cellTotal, cellLit, cellVisible, cellMarkA = -1, cellMarkB = -1;
    private Color32 cellColor = new Color32(102, 246, 255, 255), cellMarkColorA, cellMarkColorB;
    // Track
    private float trackLimit = -1f;
    // Marks
    private float markA = -1f, markB = -1f;
    private Color32 markColorA, markColorB;
    // RoomStrip
    private readonly int[] rooms = new int[MaxRoomChips];
    private int roomCount, roomVisible, roomOverflowTotal, roomOverflowCleared;
    // LeverRow / Conduit / ExitBadge
    private int leverCount, leverDone, leverKnown, leverCurrent, leverVisible;
    private bool exitHas, exitOpen, conduitOpen;
    private int exitLit, exitTotal;
    // Pips
    private int pipTotal, pipFilled;
    private Color32 pipColor = new Color32(255, 222, 51, 255);
    // Flash
    private readonly Vector2[] flash = new Vector2[MaxFlashQuads * 4];
    private int flashQuads;
    private Color32 flashColor = new Color32(255, 255, 255, 255);
    // ActionButton / Icon / Popup / MapActiveFrame
    private bool buttonEnabled = true, buttonHover, buttonFocused;
    private int buttonTone = ToneAmber, buttonIcon = IconRestart;
    private int mapFrameMode = MapFrameHidden;
    private Color32 iconColor = new Color32(43, 200, 226, 255);
    private bool popupDanger;
    private static readonly Color32 FocusFill = new Color32(17, 43, 53, 255);
    private static readonly Color32 HoverFill = new Color32(13, 33, 43, 255);

    private int dirtyCount, lastVertexCount;

    // 깎인 육각형 정적 버퍼(할당 없음 — UI 갱신은 주 스레드뿐)
    private static readonly Vector2[] poly = new Vector2[6];

    public Part DrawPart
    {
        get => part;
        set { if (part != value) { part = value; MarkDirty(); } }
    }

    /// <summary>값이 바뀌어 다시 그리게 한 횟수(검증용 — 열린 채 가만히 있으면 늘지 않아야 한다).</summary>
    public int DirtyCount => dirtyCount;
    public int LastVertexCount => lastVertexCount;
    public Color32 ChipAccent => chipAccent;
    public bool ChipNeutral => chipNeutral;
    public int LadderGrade => ladderGrade;
    public int LadderVisible => ladderVisible;
    public int CellTotal => cellTotal;
    public int CellLit => cellLit;
    public int CellVisible => cellVisible;
    /// <summary>지금 켜져 보이는 칸 수(= min(남은 양, 연출 진행)).</summary>
    public int CellShown => Mathf.Clamp(Mathf.Min(cellLit, cellVisible), 0, cellTotal);
    public Color32 CellColor => cellColor;
    public int CellMarkA => cellMarkA;
    public int CellMarkB => cellMarkB;
    public float TrackLimit => trackLimit;
    public float MarkA => markA;
    public float MarkB => markB;
    public int RoomCount => roomCount;
    public int RoomVisible => roomVisible;
    public int RoomCode(int slot) => slot >= 0 && slot < roomCount ? rooms[slot] : -1;
    public int LeverCount => leverCount;
    public int LeverDoneMask => leverDone;
    public int LeverKnownMask => leverKnown;
    public int LeverCurrentMask => leverCurrent;
    public int LeverVisible => leverVisible;
    public bool ExitHas => exitHas;
    public bool ExitOpen => exitOpen;
    public int ExitLit => exitLit;
    public int ExitTotal => exitTotal;
    public bool ConduitOpen => conduitOpen;
    public int PipTotal => pipTotal;
    public int PipFilled => pipFilled;
    public int FlashQuads => flashQuads;
    public bool ButtonEnabled => buttonEnabled;
    public bool ButtonHover => buttonHover;
    public bool ButtonFocused => buttonFocused;
    public bool ButtonDanger => buttonTone == ToneDanger;
    public int ButtonTone => buttonTone;
    public int ButtonIcon => buttonIcon;
    public int MapFrameMode => mapFrameMode;
    public Color32 IconColor => iconColor;
    public bool PopupDanger => popupDanger;

    private void MarkDirty()
    {
        dirtyCount++;
        SetVerticesDirty();
    }

    private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

    // ===== 값 설정(같으면 다시 그리지 않는다) =====

    /// <summary>Chip: 강조색 판(neutral이면 중립 청록 판).</summary>
    public void SetChip(Color32 accent, bool neutral)
    {
        if (chipNeutral == neutral && (neutral || Same(chipAccent, accent))) return;
        if (!Same(chipAccent, accent)) chipStyle = null;
        chipAccent = accent;
        chipNeutral = neutral;
        MarkDirty();
    }

    /// <summary>Ladder4: grade = 지금 등급(0 미흡 … 3 무결점, −1 = 없음), visible = 아래부터 켤 칸 수(열림 연출).</summary>
    public void SetLadder(int grade, int visible)
    {
        grade = Mathf.Clamp(grade, -1, 3);
        visible = Mathf.Clamp(visible, 0, 4);
        if (grade == ladderGrade && visible == ladderVisible) return;
        ladderGrade = grade;
        ladderVisible = visible;
        MarkDirty();
    }

    /// <summary>CellBar: total = 통과 한도(칸 수), lit = 남은 양, visible = 연출이 켠 칸 수, mark = 그 칸 수 지점의 등급 눈금(−1 = 없음).</summary>
    public void SetCells(int total, int lit, int visible, Color32 color, int markAt, Color32 markColor, int markAt2, Color32 markColor2)
    {
        total = Mathf.Max(0, total);
        lit = Mathf.Clamp(lit, 0, total);
        visible = Mathf.Clamp(visible, 0, total);
        markAt = markAt > 0 && markAt < total ? markAt : -1;
        markAt2 = markAt2 > 0 && markAt2 < total ? markAt2 : -1;
        if (total == cellTotal && lit == cellLit && visible == cellVisible && Same(color, cellColor) && markAt == cellMarkA && markAt2 == cellMarkB
            && Same(markColor, cellMarkColorA) && Same(markColor2, cellMarkColorB)) return;
        cellTotal = total;
        cellLit = lit;
        cellVisible = visible;
        cellColor = color;
        cellMarkA = markAt;
        cellMarkB = markAt2;
        cellMarkColorA = markColor;
        cellMarkColorB = markColor2;
        MarkDirty();
    }

    /// <summary>CellBar의 연출 진행(켠 칸 수)만 바꾼다.</summary>
    public void SetCellVisible(int visible)
    {
        visible = Mathf.Clamp(visible, 0, cellTotal);
        if (visible == cellVisible) return;
        cellVisible = visible;
        MarkDirty();
    }

    /// <summary>Track: 시간 한도(초, ≤ 0이면 틱 없음 — 바탕만).</summary>
    public void SetTrack(float limitSeconds)
    {
        if (Mathf.Abs(limitSeconds - trackLimit) < .001f) return;
        trackLimit = limitSeconds;
        MarkDirty();
    }

    /// <summary>Marks: 비율 자리(0~1, 범위 밖이면 없음)의 등급색 세로 눈금 2개.</summary>
    public void SetMarks(float a, Color32 colorA, float b, Color32 colorB)
    {
        a = a > 0f && a < 1f ? a : -1f;
        b = b > 0f && b < 1f ? b : -1f;
        if (Mathf.Abs(a - markA) < 1e-4f && Mathf.Abs(b - markB) < 1e-4f && Same(colorA, markColorA) && Same(colorB, markColorB)) return;
        markA = a;
        markB = b;
        markColorA = colorA;
        markColorB = colorB;
        MarkDirty();
    }

    /// <summary>RoomStrip: 칩 상태(최대 16 — 넘으면 overflowTotal·overflowCleared로 연속 바), visible = 연출이 켠 칩 수.</summary>
    public void SetRooms(int[] codes, int count, int visible, int overflowTotal = 0, int overflowCleared = 0)
    {
        count = Mathf.Clamp(count, 0, MaxRoomChips);
        visible = Mathf.Clamp(visible, 0, Mathf.Max(count, overflowTotal > 0 ? 1 : 0));
        bool same = count == roomCount && visible == roomVisible && overflowTotal == roomOverflowTotal && overflowCleared == roomOverflowCleared;
        if (same && codes != null)
            for (int i = 0; i < count; i++) if (rooms[i] != codes[i]) { same = false; break; }
        if (same) return;
        roomCount = count;
        roomVisible = visible;
        roomOverflowTotal = Mathf.Max(0, overflowTotal);
        roomOverflowCleared = Mathf.Clamp(overflowCleared, 0, roomOverflowTotal);
        for (int i = 0; i < MaxRoomChips; i++) rooms[i] = codes != null && i < count ? codes[i] : 0;
        MarkDirty();
    }

    /// <summary>RoomStrip의 연출 진행(켠 칩 수)만 바꾼다.</summary>
    public void SetRoomVisible(int visible)
    {
        visible = Mathf.Clamp(visible, 0, Mathf.Max(roomCount, roomOverflowTotal > 0 ? 1 : 0));
        if (visible == roomVisible) return;
        roomVisible = visible;
        MarkDirty();
    }

    /// <summary>LeverRow·Conduit: 레버 수·완료·발견·지금 방 레버 비트(bit i = 진행 번호 i), visible = 연출이 보인 배지 수.</summary>
    public void SetLevers(int count, int doneMask, int knownMask, int currentMask, int visible)
    {
        count = Mathf.Clamp(count, 0, MaxLevers);
        int all = (1 << count) - 1;
        doneMask &= all; knownMask &= all; currentMask &= all;
        visible = Mathf.Clamp(visible, 0, count);
        if (count == leverCount && doneMask == leverDone && knownMask == leverKnown && currentMask == leverCurrent && visible == leverVisible) return;
        leverCount = count;
        leverDone = doneMask;
        leverKnown = knownMask;
        leverCurrent = currentMask;
        leverVisible = visible;
        MarkDirty();
    }

    /// <summary>Conduit: 버스선이 해금(앰버)됐는지.</summary>
    public void SetConduitOpen(bool open)
    {
        if (open == conduitOpen) return;
        conduitOpen = open;
        MarkDirty();
    }

    /// <summary>ExitBadge: 출구 유무·해금·램프(켜진 수 / 레버 수).</summary>
    public void SetExit(bool has, bool open, int lit, int total)
    {
        total = Mathf.Clamp(total, 0, DungeonMapMesh.MaxExitLamps);
        lit = Mathf.Clamp(lit, 0, total);
        if (has == exitHas && open == exitOpen && lit == exitLit && total == exitTotal) return;
        exitHas = has;
        exitOpen = open;
        exitLit = lit;
        exitTotal = total;
        MarkDirty();
    }

    /// <summary>Pips: total개 중 filled개 채운 작은 칸 점(최대 10).</summary>
    public void SetPips(int total, int filled, Color32 color)
    {
        total = Mathf.Clamp(total, 0, 10);
        filled = Mathf.Clamp(filled, 0, total);
        if (total == pipTotal && filled == pipFilled && Same(color, pipColor)) return;
        pipTotal = total;
        pipFilled = filled;
        pipColor = color;
        MarkDirty();
    }

    /// <summary>Flash: 목록을 비운다(다시 그리기는 <see cref="FlashCommit"/>에서).</summary>
    public void FlashClear() => flashQuads = 0;

    /// <summary>Flash: 사각 꼴 하나(시계/반시계 아무 순서의 볼록 네 점)를 더한다. 상한을 넘으면 버린다.</summary>
    public void FlashAddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        if (flashQuads >= MaxFlashQuads) return;
        int i = flashQuads * 4;
        flash[i] = a; flash[i + 1] = b; flash[i + 2] = c; flash[i + 3] = d;
        flashQuads++;
    }

    /// <summary>Flash: 축 정렬 사각형 하나를 더한다.</summary>
    public void FlashAddRect(Rect r) => FlashAddQuad(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax));

    /// <summary>Flash: 색을 정하고 다시 그린다(목록을 바꾼 뒤 한 번).</summary>
    public void FlashCommit(Color32 color)
    {
        flashColor = color;
        MarkDirty();
    }

    /// <summary>ActionButton: 활성·hover·키보드 포커스·위험(포기) 상태(기존 호환).</summary>
    public void SetButton(bool enabled, bool hover, bool focused = false, bool danger = false)
    {
        SetButtonStyle(enabled, hover, focused, danger ? ToneDanger : ToneAmber, danger ? IconGiveUp : IconRestart);
    }

    /// <summary>ActionButton: 활성·hover·키보드 포커스·색상 톤·아이콘 종류 지정(값이 같으면 다시 그리지 않는다).</summary>
    public void SetButtonStyle(bool enabled, bool hover, bool focused, int tone, int icon)
    {
        hover = enabled && hover;
        focused = enabled && focused;
        if (buttonEnabled == enabled && buttonHover == hover && buttonFocused == focused && buttonTone == tone && buttonIcon == icon) return;
        if (Application.isPlaying && (buttonFocused || buttonHover) != (focused || hover))
        {
            float scale = focused || hover ? TrainingBoardStyle.FocusScale : 1f;
            rectTransform.DOKill();
            if (isActiveAndEnabled)
                rectTransform.DOScale(scale, .16f).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);
            else rectTransform.localScale = Vector3.one * scale;
        }
        buttonEnabled = enabled;
        buttonHover = hover;
        buttonFocused = focused;
        buttonTone = tone;
        buttonIcon = icon;
        MarkDirty();
    }

    /// <summary>Sci-Fi 벡터 아이콘·조준 고리 색을 정한다(같으면 다시 그리지 않는다).</summary>
    public void SetIconColor(Color32 color)
    {
        if (Same(iconColor, color)) return;
        iconColor = color;
        MarkDirty();
    }

    /// <summary>확인 팝업 프레임의 경고 톤(재시작 = 앰버, 포기 = 빨강)을 정한다.</summary>
    public void SetPopupDanger(bool danger)
    {
        if (popupDanger == danger) return;
        popupDanger = danger;
        MarkDirty();
    }

    /// <summary>MapActiveFrame 표시 모드(숨김·대형 지도 버튼 선택 하이라이트·상세 맵 탐색 활성)를 정한다.</summary>
    public void SetMapFrameMode(int mode)
    {
        mode = Mathf.Clamp(mode, MapFrameHidden, MapFrameDetail);
        if (mapFrameMode == mode) return;
        mapFrameMode = mode;
        MarkDirty();
    }

    // ===== 자리(부품 사각형 r 기준 — 부르는 쪽·검증이 같은 함수를 쓴다) =====

    /// <summary>Ladder4 칸 i(0 미흡 … 3 무결점)의 사각 꼴(아래 왼쪽부터 반시계).</summary>
    public static void LadderQuad(Rect r, int i, out Vector2 a, out Vector2 b, out Vector2 c, out Vector2 d)
    {
        float slant = r.height * Slant, gap = 4f;
        float w = Mathf.Max(1f, (r.width - slant - gap * 3f) / 4f);
        float x = r.xMin + Mathf.Clamp(i, 0, 3) * (w + gap);
        a = new Vector2(x, r.yMin); b = new Vector2(x + w, r.yMin);
        c = new Vector2(x + w + slant, r.yMax); d = new Vector2(x + slant, r.yMax);
    }

    // CellBar 칸 띠(바 높이 = 사각형 가운데 60%, 위아래는 눈금 자리)
    private static Rect CellBand(Rect r) => new Rect(r.xMin, r.yMin + r.height * .2f, r.width, r.height * .6f);

    private static void CellMetrics(Rect r, int total, out float slant, out float w, out float gap)
    {
        var band = CellBand(r);
        slant = band.height * Slant;
        int n = Mathf.Max(1, total);
        float room = band.width - slant;
        gap = Mathf.Clamp(room / n * .24f, 1.2f, 3f);
        w = Mathf.Max(.5f, (room - gap * (n - 1)) / n);
    }

    /// <summary>CellBar 칸 index의 사각 꼴(아래 왼쪽부터 반시계). 연속 바(한도 &gt; 48)면 그 칸이 차지하는 구간.</summary>
    public static void CellQuad(Rect r, int total, int index, out Vector2 a, out Vector2 b, out Vector2 c, out Vector2 d)
    {
        var band = CellBand(r);
        if (total > MaxSlantCells)
        {
            float x0 = Mathf.Lerp(band.xMin, band.xMax, index / (float)total), x1 = Mathf.Lerp(band.xMin, band.xMax, (index + 1) / (float)total);
            a = new Vector2(x0, band.yMin); b = new Vector2(x1, band.yMin); c = new Vector2(x1, band.yMax); d = new Vector2(x0, band.yMax);
            return;
        }
        CellMetrics(r, total, out float slant, out float w, out float gap);
        float x = band.xMin + index * (w + gap);
        a = new Vector2(x, band.yMin); b = new Vector2(x + w, band.yMin);
        c = new Vector2(x + w + slant, band.yMax); d = new Vector2(x + slant, band.yMax);
    }

    /// <summary>CellBar 눈금 x(칸 at 앞 경계 — at칸이 남으면 그 등급을 유지).</summary>
    public static float CellMarkX(Rect r, int total, int at)
    {
        var band = CellBand(r);
        if (total <= 0) return band.xMin;
        if (total > MaxSlantCells) return Mathf.Lerp(band.xMin, band.xMax, at / (float)total);
        CellMetrics(r, total, out float slant, out float w, out float gap);
        return band.xMin + at * (w + gap) - gap * .5f + slant * .5f;
    }

    /// <summary>RoomStrip 칩 slot의 사각 꼴(칩 18×14 비스듬, 간격 3 — 사각형 아래쪽, 위는 지금 방 마름모 자리).</summary>
    public static void RoomChipQuad(Rect r, int slot, out Vector2 a, out Vector2 b, out Vector2 c, out Vector2 d)
    {
        float h = Mathf.Min(14f, r.height - 10f), slant = h * Slant, w = 18f, gap = 3f;
        float y0 = r.yMin + 2f;
        float x = r.xMin + 3f + slot * (w + gap);
        a = new Vector2(x, y0); b = new Vector2(x + w, y0);
        c = new Vector2(x + w + slant, y0 + h); d = new Vector2(x + slant, y0 + h);
    }

    /// <summary>RoomStrip에 칩으로 그릴 수 있는 최대 수(사각형 폭 기준).</summary>
    public static int RoomChipCapacity(Rect r) => Mathf.Clamp(Mathf.FloorToInt((r.width - 6f - Mathf.Min(14f, r.height - 10f) * Slant + 3f) / 21f), 0, MaxRoomChips);

    /// <summary>레버 배지 한 변(px) — 레버 수가 많으면 줄인다.</summary>
    public static float LeverSide(Rect r, int count)
    {
        float room = LeverRowWidth(r);
        int n = Mathf.Max(1, count);
        return Mathf.Clamp(room / n - 30f, 22f, 40f);
    }

    private static float LeverRowWidth(Rect r) => Mathf.Max(40f, r.width - ExitAreaWidth - 8f);
    private const float ExitAreaWidth = 112f, ExitSide = 52f;

    /// <summary>레버 배지 i의 가운데(목표 영역 사각형 기준).</summary>
    public static Vector2 LeverCenter(Rect r, int count, int i)
    {
        float side = LeverSide(r, count);
        int n = Mathf.Max(1, count);
        float pitch = Mathf.Min(side + 34f, (LeverRowWidth(r) - side) / Mathf.Max(1, n - 1));
        if (n == 1) pitch = 0f;
        return new Vector2(r.xMin + 8f + side * .5f + Mathf.Clamp(i, 0, n - 1) * pitch, r.yMax - 6f - side * .5f);
    }

    /// <summary>레버 상태 글자 가운데(배지 아래).</summary>
    public static Vector2 LeverLabelPoint(Rect r, int count, int i)
    {
        var c = LeverCenter(r, count, i);
        return new Vector2(c.x, c.y - LeverSide(r, count) * .5f - 10f);
    }

    /// <summary>출구 배지 가운데(목표 영역 오른쪽).</summary>
    public static Vector2 ExitCenter(Rect r) => new Vector2(r.xMax - ExitAreaWidth * .5f, r.yMax - 14f - ExitSide * .5f);

    /// <summary>출구 배지 한 변(px).</summary>
    public static float ExitBadgeSide => ExitSide;

    /// <summary>버스선 높이(y).</summary>
    public static float BusY(Rect r, int count) => LeverLabelPoint(r, count, 0).y - 16f;

    /// <summary>
    /// 레버 i에서 출구까지 전류 경로의 점(u 0 = 레버 배지 아래 글자 밑, 0.3 = 버스선, 0.9 = 출구 아래, 1 = 출구 배지 아래 끝). 부르는 쪽이 전류 빛 Image 자리로 쓴다.
    /// </summary>
    public static Vector2 ConduitPoint(Rect r, int count, int i, float u)
    {
        var top = new Vector2(LeverCenter(r, count, i).x, LeverLabelPoint(r, count, i).y - 8f);
        float bus = BusY(r, count);
        var e = ExitCenter(r);
        var joint = new Vector2(top.x, bus);
        var corner = new Vector2(e.x, bus);
        var end = new Vector2(e.x, e.y - ExitSide * .5f);
        u = Mathf.Clamp01(u);
        if (u < .3f) return Vector2.Lerp(top, joint, u / .3f);
        if (u < .9f) return Vector2.Lerp(joint, corner, (u - .3f) / .6f);
        return Vector2.Lerp(corner, end, (u - .9f) / .1f);
    }

    // ===== 그리기 =====

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var r = rectTransform.rect;
        switch (part)
        {
            case Part.Chip: WriteChip(vh, r); break;
            case Part.Ladder4: WriteLadder4(vh, r); break;
            case Part.CellBar: WriteCellBar(vh, r); break;
            case Part.Track: WriteTrack(vh, r); break;
            case Part.Marks: WriteMarks(vh, r); break;
            case Part.RoomStrip: WriteRoomStrip(vh, r); break;
            case Part.LeverRow: WriteLeverRow(vh, r); break;
            case Part.Conduit: WriteConduit(vh, r); break;
            case Part.ExitBadge: WriteExit(vh, r); break;
            case Part.Pips: WritePips(vh, r); break;
            case Part.Flash: WriteFlash(vh); break;
            case Part.ActionButton: WriteActionButton(vh, r); break;
            case Part.IconShield: WriteIconShield(vh, r); break;
            case Part.IconClock: WriteIconClock(vh, r); break;
            case Part.IconSector: WriteIconSector(vh, r); break;
            case Part.IconThreat: WriteIconThreat(vh, r); break;
            case Part.IconObjective: WriteIconObjective(vh, r); break;
            case Part.SciFiFrame: WriteSciFiFrame(vh, r); break;
            case Part.Reticle: WriteReticle(vh, r); break;
            case Part.ActiveFrame: WriteActiveFrame(vh, r); break;
            case Part.PopupFrame: WritePopupFrame(vh, r); break;
        }
        lastVertexCount = vh.currentVertCount;
    }

    private void WriteChip(VertexHelper vh, Rect r)
    {
        DungeonMapMesh.Style style;
        if (chipNeutral)
            style = neutralStyle ??= new DungeonMapMesh.Style { plateCut = 10f, plateInnerInset = 4f, bracketLength = 10f };
        else
            style = chipStyle ??= Compact(DungeonStatusPlateGraphic.AccentStyle(chipAccent));
        DungeonMapMesh.WritePlate(vh, r, style, 0f);
    }

    private static DungeonMapMesh.Style Compact(DungeonMapMesh.Style s)
    {
        s.plateCut = 10f;
        s.plateInnerInset = 4f;
        s.bracketLength = 10f;
        return s;
    }

    private void WriteLadder4(VertexHelper vh, Rect r)
    {
        for (int i = 0; i < 4; i++)
        {
            LadderQuad(r, i, out var a, out var b, out var c, out var d);
            bool lit = ladderGrade >= 0 && i <= ladderGrade && i < ladderVisible;
            if (lit)
            {
                Color32 col = TrainingBoardStyle.GradeColor((Grade)i);
                col.a = (byte)(i == ladderGrade ? 255 : 128);
                Draw.Quad(vh, a, b, c, d, col);
                Color32 edge = Color.Lerp(TrainingBoardStyle.GradeColor((Grade)i), Color.white, .45f);
                edge.a = col.a;
                Draw.QuadOutline(vh, a, b, c, d, 1f, edge);
            }
            else
            {
                Draw.Quad(vh, a, b, c, d, Neutral);
                Draw.QuadOutline(vh, a, b, c, d, 1f, LineDim);
            }
        }
    }

    private void WriteCellBar(VertexHelper vh, Rect r)
    {
        if (cellTotal <= 0) return;
        int shown = CellShown;
        if (cellTotal > MaxSlantCells)
        {
            var band = CellBand(r);
            Draw.Rect(vh, band, Neutral);
            Draw.Outline(vh, band, 1f, LineDim);
            if (shown > 0)
            {
                float x = Mathf.Lerp(band.xMin, band.xMax, shown / (float)cellTotal);
                Draw.Rect(vh, Rect.MinMaxRect(band.xMin + 1f, band.yMin + 1f, Mathf.Max(band.xMin + 1f, x - 1f), band.yMax - 1f), cellColor);
            }
            for (int k = 5; k < cellTotal; k += 5)
            {
                float x = Mathf.Lerp(band.xMin, band.xMax, k / (float)cellTotal);
                Draw.Rect(vh, Rect.MinMaxRect(x - .5f, band.yMin + 1f, x + .5f, band.yMax - 1f), TickDark);
            }
        }
        else
        {
            Color32 slot = LineDim;
            slot.a = 200;
            for (int i = 0; i < cellTotal; i++)
            {
                CellQuad(r, cellTotal, i, out var a, out var b, out var c, out var d);
                Draw.Quad(vh, a, b, c, d, i < shown ? cellColor : slot);
            }
        }
        MarkLine(vh, r, cellMarkA, cellMarkColorA);
        MarkLine(vh, r, cellMarkB, cellMarkColorB);
    }

    private void MarkLine(VertexHelper vh, Rect r, int at, Color32 color)
    {
        if (at <= 0 || at >= cellTotal) return;
        float x = Mathf.Clamp(CellMarkX(r, cellTotal, at), r.xMin + 1f, r.xMax - 1f);
        Draw.Rect(vh, Rect.MinMaxRect(x - 1f, r.yMin, x + 1f, r.yMax), color);
    }

    private void WriteTrack(VertexHelper vh, Rect r)
    {
        Draw.Rect(vh, r, Neutral);
        Draw.Outline(vh, r, 1f, LineDim);
        if (trackLimit <= 0f) return;
        float step = trackLimit / 60f > 30f ? 300f : 60f;
        for (float s = step; s < trackLimit - .01f; s += step)
        {
            float x = Mathf.Lerp(r.xMin, r.xMax, s / trackLimit);
            Color32 tick = LineDim;
            Draw.Rect(vh, Rect.MinMaxRect(x - .5f, r.yMin + 1f, x + .5f, r.yMin + r.height * .4f), tick);
        }
    }

    private void WriteMarks(VertexHelper vh, Rect r)
    {
        if (markA > 0f) { float x = Mathf.Clamp(Mathf.Lerp(r.xMin, r.xMax, markA), r.xMin + 1f, r.xMax - 1f); Draw.Rect(vh, Rect.MinMaxRect(x - 1f, r.yMin, x + 1f, r.yMax), markColorA); }
        if (markB > 0f) { float x = Mathf.Clamp(Mathf.Lerp(r.xMin, r.xMax, markB), r.xMin + 1f, r.xMax - 1f); Draw.Rect(vh, Rect.MinMaxRect(x - 1f, r.yMin, x + 1f, r.yMax), markColorB); }
    }

    private void WriteRoomStrip(VertexHelper vh, Rect r)
    {
        if (roomOverflowTotal > 0 && roomCount == 0)
        {
            // 방이 칩 상한을 넘으면 연속 바(클리어 비율)
            if (roomVisible <= 0) return;
            var band = new Rect(r.xMin + 2f, r.yMin + 4f, r.width - 4f, Mathf.Min(10f, r.height - 8f));
            Draw.Rect(vh, band, Neutral);
            Draw.Outline(vh, band, 1f, LineDim);
            float x = Mathf.Lerp(band.xMin + 1f, band.xMax - 1f, roomOverflowCleared / (float)roomOverflowTotal);
            if (x > band.xMin + 1.5f) Draw.Rect(vh, Rect.MinMaxRect(band.xMin + 1f, band.yMin + 1f, x, band.yMax - 1f), ClearGreen);
            return;
        }
        for (int i = 0; i < roomCount && i < roomVisible; i++)
        {
            RoomChipQuad(r, i, out var a, out var b, out var c, out var d);
            if (d.x > r.xMax + .01f || c.x > r.xMax + .01f) break;
            int code = rooms[i];
            int kind = code & RoomKindMask;
            var center = (a + c) * .5f;
            switch (kind)
            {
                case RoomCleared:
                {
                    Color32 g = ClearGreen; g.a = 220;
                    Draw.Quad(vh, a, b, c, d, g);
                    break;
                }
                case RoomVisited:
                    Draw.Quad(vh, a, b, c, d, Neutral);
                    Draw.QuadOutline(vh, a, b, c, d, 1f, Line);
                    break;
                case RoomKnown:
                {
                    Draw.Quad(vh, a, b, c, d, Neutral);
                    Color32 dim = Line; dim.a = 130;
                    Draw.QuadOutline(vh, a, b, c, d, 1f, dim);
                    break;
                }
                case RoomBoss:
                case RoomBossCleared:
                {
                    Draw.Quad(vh, a, b, c, d, Neutral);
                    Color32 edge = kind == RoomBossCleared ? ClearGreen : BossRed;
                    Draw.QuadOutline(vh, a, b, c, d, 1f, edge);
                    Draw.Diamond(vh, center, 4.5f, edge);
                    break;
                }
                default:
                {
                    Draw.Quad(vh, a, b, c, d, Neutral);
                    Color32 dim = LineDim; dim.a = 200;
                    Draw.QuadOutline(vh, a, b, c, d, 1f, dim);
                    break;
                }
            }
            if ((code & RoomRest) != 0) Draw.Cross(vh, center, 3.5f, 1.5f, kind == RoomCleared ? new Color32(226, 255, 234, 255) : ClearGreen);
            else if (kind == RoomVisited) Draw.Rect(vh, new Rect(center.x - 2f, center.y - 2f, 4f, 4f), Uncleared);
            if ((code & RoomLever) != 0) Draw.Rect(vh, new Rect(c.x - 6.5f, c.y - 5f, 4f, 4f), Amber);
            if ((code & RoomCurrent) != 0)
            {
                float l = Mathf.Min(a.x, d.x) - 2f, rr = Mathf.Max(b.x, c.x) + 2f, bo = a.y - 2f, to = d.y + 2f;
                Draw.Brackets(vh, Rect.MinMaxRect(l, Mathf.Max(r.yMin, bo), Mathf.Min(r.xMax, rr), to), 4f, 1.5f, Frame);
                Draw.Diamond(vh, new Vector2(center.x, Mathf.Min(r.yMax - 3.5f, to + 4f)), 3f, Frame);
            }
        }
    }

    private void WriteLeverRow(VertexHelper vh, Rect r)
    {
        if (leverCount <= 0) return;
        float side = LeverSide(r, leverCount);
        for (int i = 0; i < leverCount && i < leverVisible; i++)
        {
            var c = LeverCenter(r, leverCount, i);
            bool done = (leverDone & (1 << i)) != 0;
            bool known = (leverKnown & (1 << i)) != 0;
            DungeonMapMesh.WriteObjectiveBadge(vh, r, c, side, DungeonDetailMarkerKind.Objective, done, done || known, false, 0, 0);
            if ((leverCurrent & (1 << i)) != 0)
            {
                float h = side * .5f + 4f;
                Draw.Brackets(vh, Rect.MinMaxRect(Mathf.Max(r.xMin, c.x - h), Mathf.Max(r.yMin, c.y - h), Mathf.Min(r.xMax, c.x + h), Mathf.Min(r.yMax, c.y + h)), 7f, 2f, Frame);
            }
        }
    }

    private void WriteConduit(VertexHelper vh, Rect r)
    {
        if (leverCount <= 0 || !exitHas) return;
        float bus = BusY(r, leverCount);
        var e = ExitCenter(r);
        Color32 dim = LineDim;
        Color32 lit = Amber;
        var first = ConduitPoint(r, leverCount, 0, 0f);
        // 버스선(첫 레버 아래 → 출구 아래) + 출구로 올라가는 짧은 선
        Draw.Rect(vh, Rect.MinMaxRect(first.x - 1f, bus - 1f, e.x + 1f, bus + 1f), conduitOpen ? lit : dim);
        Draw.Rect(vh, Rect.MinMaxRect(e.x - 1f, bus, e.x + 1f, e.y - ExitSide * .5f), conduitOpen ? lit : dim);
        for (int i = 0; i < leverCount && i < leverVisible; i++)
        {
            var top = ConduitPoint(r, leverCount, i, 0f);
            bool done = (leverDone & (1 << i)) != 0;
            Draw.Rect(vh, Rect.MinMaxRect(top.x - 1f, bus, top.x + 1f, top.y), done ? lit : dim);
            Draw.Rect(vh, new Rect(top.x - 2f, bus - 2f, 4f, 4f), done ? lit : dim);   // 접점
        }
    }

    private void WriteExit(VertexHelper vh, Rect r)
    {
        if (!exitHas) return;
        DungeonMapMesh.WriteObjectiveBadge(vh, r, ExitCenter(r), ExitSide, DungeonDetailMarkerKind.Exit, false, false, exitOpen, exitLit, exitTotal);
    }

    private void WritePips(VertexHelper vh, Rect r)
    {
        float s = Mathf.Min(8f, r.height - 2f), gap = 4f;
        float y = r.center.y;
        for (int i = 0; i < pipTotal; i++)
        {
            float x = r.xMin + 1f + i * (s + gap);
            if (x + s > r.xMax) break;
            var cell = new Rect(x, y - s * .5f, s, s);
            if (i < pipFilled) Draw.Chamfer(vh, cell, 2f, pipColor);
            else
            {
                Draw.Chamfer(vh, cell, 2f, Neutral);
                Color32 edge = pipColor; edge.a = 140;
                Draw.ChamferOutline(vh, cell, 2f, 1f, edge);
            }
        }
    }

    private void WriteFlash(VertexHelper vh)
    {
        for (int q = 0; q < flashQuads; q++)
        {
            int i = q * 4;
            Draw.Quad(vh, flash[i], flash[i + 1], flash[i + 2], flash[i + 3], flashColor);
        }
    }

    // 세이브 슬롯과 같은 절단 모서리와 왼쪽 선택선을 쓰고, 위험 행동은 아이콘 색으로 구분한다.
    private void WriteActionButton(VertexHelper vh, Rect r)
    {
        if (r.width <= 4f || r.height <= 4f) return;
        bool focused = buttonEnabled && buttonFocused;
        bool hovered = buttonEnabled && buttonHover;
        Color32 back = focused ? FocusFill : hovered ? HoverFill : (Color32)TrainingBoardStyle.SlotFill;
        Color32 edge = focused ? LineBright : hovered ? Line : LineDim;
        Color32 accent = !buttonEnabled ? LineDim : buttonTone == ToneDanger ? BossRed : focused ? LineBright : Line;
        float cut = Mathf.Min(10f, r.height * .24f);
        Draw.Chamfer(vh, r, cut, back);
        Draw.ChamferOutline(vh, r, cut, focused ? 2.4f : 1.2f, edge);
        if (focused || hovered)
            Draw.Rect(vh, new Rect(r.xMin + 7f, r.yMin + r.height * .23f, 2.8f, r.height * .54f), edge);

        if (buttonIcon == IconNone) return;
        float radius = Mathf.Min(7.5f, r.height * .23f);
        var iconCenter = new Vector2(r.xMin + 24f, r.center.y);
        if (iconCenter.x + radius >= r.xMax - 10f || radius < 3f) return;

        switch (buttonIcon)
        {
            case IconMap:
            {
                // 상세 맵: 2x2 전술 구역 격자 + 외곽 조준점
                float g = radius * .85f;
                float cell = g * .72f;
                float off = g * .48f;
                Color32 fill = accent; fill.a = 150;
                Draw.Rect(vh, new Rect(iconCenter.x - off - cell * .5f, iconCenter.y + off - cell * .5f, cell, cell), accent);
                Draw.Outline(vh, new Rect(iconCenter.x + off - cell * .5f, iconCenter.y + off - cell * .5f, cell, cell), 1.3f, accent);
                Draw.Outline(vh, new Rect(iconCenter.x - off - cell * .5f, iconCenter.y - off - cell * .5f, cell, cell), 1.3f, accent);
                Draw.Rect(vh, new Rect(iconCenter.x + off - cell * .5f, iconCenter.y - off - cell * .5f, cell, cell), fill);
                break;
            }
            case IconGiveUp:
            {
                // 포기(로비 복귀) 문 + 바깥 화살표 아이콘
                float hw = radius * .85f, hh = radius * .95f;
                Vector2 tl = new Vector2(iconCenter.x - hw * .2f, iconCenter.y + hh);
                Vector2 tr = new Vector2(iconCenter.x + hw, iconCenter.y + hh);
                Vector2 br = new Vector2(iconCenter.x + hw, iconCenter.y - hh);
                Vector2 bl = new Vector2(iconCenter.x - hw * .2f, iconCenter.y - hh);
                Draw.Line(vh, tl, tr, 1.7f, accent);
                Draw.Line(vh, tr, br, 1.7f, accent);
                Draw.Line(vh, br, bl, 1.7f, accent);
                Vector2 shaftStart = new Vector2(iconCenter.x + hw * .45f, iconCenter.y);
                Vector2 tip = new Vector2(iconCenter.x - hw * 1.05f, iconCenter.y);
                Draw.Line(vh, shaftStart, tip, 1.8f, accent);
                Draw.Line(vh, tip, tip + new Vector2(3.6f, 3.0f), 1.8f, accent);
                Draw.Line(vh, tip, tip + new Vector2(3.6f, -3.0f), 1.8f, accent);
                break;
            }
            case IconCheck:
            {
                Vector2 a = iconCenter + new Vector2(-radius * .8f, 0f);
                Vector2 b = iconCenter + new Vector2(-radius * .15f, -radius * .65f);
                Vector2 c = iconCenter + new Vector2(radius * .9f, radius * .65f);
                Draw.Line(vh, a, b, 2f, accent);
                Draw.Line(vh, b, c, 2f, accent);
                break;
            }
            case IconCancel:
            {
                float d = radius * .72f;
                Draw.Line(vh, iconCenter + new Vector2(-d, -d), iconCenter + new Vector2(d, d), 1.9f, accent);
                Draw.Line(vh, iconCenter + new Vector2(-d, d), iconCenter + new Vector2(d, -d), 1.9f, accent);
                break;
            }
            default:
            {
                // 재시작 되돌리기 고리 아이콘(원호 7분절 + 화살촉)
                const int segs = 7;
                float startRad = 50f * Mathf.Deg2Rad;
                float sweepRad = 275f * Mathf.Deg2Rad;
                Vector2 prev = iconCenter + new Vector2(Mathf.Cos(startRad), Mathf.Sin(startRad)) * radius;
                for (int s = 1; s <= segs; s++)
                {
                    float a = startRad + sweepRad * (s / (float)segs);
                    Vector2 cur = iconCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                    Draw.Line(vh, prev, cur, 1.8f, accent);
                    prev = cur;
                }
                Vector2 tip = iconCenter + new Vector2(Mathf.Cos(startRad), Mathf.Sin(startRad)) * radius;
                Draw.Line(vh, tip, tip + new Vector2(-4.2f, 1.2f), 1.8f, accent);
                Draw.Line(vh, tip, tip + new Vector2(-1.2f, 4.2f), 1.8f, accent);
                break;
            }
        }
    }

    // ===== Sci-Fi 벡터 아이콘·오버레이·팝업 =====

    // 남은 피격 방패 아이콘(깎인 방패 외곽 + 내부 V 셰브론)
    private void WriteIconShield(VertexHelper vh, Rect r)
    {
        var c = r.center;
        float hw = r.width * .38f, hh = r.height * .44f;
        Vector2 top = new Vector2(c.x, c.y + hh);
        Vector2 tr = new Vector2(c.x + hw, c.y + hh * .52f);
        Vector2 mr = new Vector2(c.x + hw * .86f, c.y - hh * .25f);
        Vector2 bot = new Vector2(c.x, c.y - hh);
        Vector2 ml = new Vector2(c.x - hw * .86f, c.y - hh * .25f);
        Vector2 tl = new Vector2(c.x - hw, c.y + hh * .52f);
        Color32 fill = iconColor; fill.a = 45;
        Draw.Quad(vh, tl, top, tr, c, fill);
        Draw.Quad(vh, tl, c, ml, bot, fill);
        Draw.Quad(vh, c, tr, mr, bot, fill);
        Draw.Line(vh, top, tr, 1.6f, iconColor);
        Draw.Line(vh, tr, mr, 1.6f, iconColor);
        Draw.Line(vh, mr, bot, 1.6f, iconColor);
        Draw.Line(vh, bot, ml, 1.6f, iconColor);
        Draw.Line(vh, ml, tl, 1.6f, iconColor);
        Draw.Line(vh, tl, top, 1.6f, iconColor);
        // 내부 방어 셰브론
        Draw.Line(vh, new Vector2(c.x - hw * .46f, c.y + hh * .08f), new Vector2(c.x, c.y - hh * .32f), 1.6f, Frame);
        Draw.Line(vh, new Vector2(c.x, c.y - hh * .32f), new Vector2(c.x + hw * .46f, c.y + hh * .08f), 1.6f, Frame);
    }

    // 남은 시간 크로노미터 아이콘(8각 링 + 4방향 눈금 + 중심점 — 바늘은 자식 Image가 회전)
    private void WriteIconClock(VertexHelper vh, Rect r)
    {
        var c = r.center;
        float rad = Mathf.Min(r.width, r.height) * .42f;
        const int segs = 8;
        Vector2 prev = c + new Vector2(rad, 0f);
        for (int s = 1; s <= segs; s++)
        {
            float a = s * Mathf.PI * 2f / segs;
            Vector2 cur = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
            Draw.Line(vh, prev, cur, 1.6f, iconColor);
            prev = cur;
        }
        // 상단 크라운 + 4방향 눈금 + 중심점
        Draw.Rect(vh, new Rect(c.x - 2.2f, c.y + rad, 4.4f, 1.6f), iconColor);
        Draw.Rect(vh, new Rect(c.x - .7f, c.y + rad - 3.2f, 1.4f, 2.4f), Frame);
        Draw.Rect(vh, new Rect(c.x - .7f, c.y - rad + .8f, 1.4f, 2.4f), iconColor);
        Draw.Rect(vh, new Rect(c.x - rad + .8f, c.y - .7f, 2.4f, 1.4f), iconColor);
        Draw.Rect(vh, new Rect(c.x + rad - 3.2f, c.y - .7f, 2.4f, 1.4f), iconColor);
        Draw.Rect(vh, new Rect(c.x - 1.3f, c.y - 1.3f, 2.6f, 2.6f), Frame);
    }

    // 구역 클리어 3x3 전술 섹터 아이콘
    private void WriteIconSector(VertexHelper vh, Rect r)
    {
        var c = r.center;
        float span = Mathf.Min(r.width, r.height) * .78f;
        float gap = 2f;
        float cell = (span - gap * 2f) / 3f;
        float x0 = c.x - span * .5f, y0 = c.y - span * .5f;
        Color32 dim = iconColor; dim.a = 120;
        for (int gy = 0; gy < 3; gy++)
            for (int gx = 0; gx < 3; gx++)
            {
                var cellRect = new Rect(x0 + gx * (cell + gap), y0 + gy * (cell + gap), cell, cell);
                bool center = gx == 1 && gy == 1;
                bool cross = gx == 1 || gy == 1;
                if (center) Draw.Rect(vh, cellRect, Frame);
                else if (cross) Draw.Rect(vh, cellRect, iconColor);
                else Draw.Outline(vh, cellRect, 1.1f, dim);
            }
    }

    // 난이도 경고 삼각형 + 셰브론 아이콘
    private void WriteIconThreat(VertexHelper vh, Rect r)
    {
        var c = r.center;
        float hw = r.width * .44f, hh = r.height * .42f;
        Vector2 top = new Vector2(c.x, c.y + hh);
        Vector2 br = new Vector2(c.x + hw, c.y - hh * .85f);
        Vector2 bl = new Vector2(c.x - hw, c.y - hh * .85f);
        Color32 fill = iconColor; fill.a = 48;
        int idx = vh.currentVertCount;
        vh.AddVert(top, fill, Vector2.zero); vh.AddVert(br, fill, Vector2.zero); vh.AddVert(bl, fill, Vector2.zero);
        vh.AddTriangle(idx, idx + 1, idx + 2);
        Draw.Line(vh, top, br, 1.6f, iconColor);
        Draw.Line(vh, br, bl, 1.6f, iconColor);
        Draw.Line(vh, bl, top, 1.6f, iconColor);
        Draw.Rect(vh, new Rect(c.x - 1f, c.y - hh * .25f, 2f, hh * .78f), Frame);
        Draw.Rect(vh, new Rect(c.x - 1.1f, c.y - hh * .62f, 2.2f, 2.2f), Frame);
    }

    // 목표 전술 마름모 조준 아이콘
    private void WriteIconObjective(VertexHelper vh, Rect r)
    {
        var c = r.center;
        float rad = Mathf.Min(r.width, r.height) * .42f;
        Vector2 t = new Vector2(c.x, c.y + rad), rr = new Vector2(c.x + rad, c.y);
        Vector2 b = new Vector2(c.x, c.y - rad), l = new Vector2(c.x - rad, c.y);
        Draw.Line(vh, t, rr, 1.6f, iconColor);
        Draw.Line(vh, rr, b, 1.6f, iconColor);
        Draw.Line(vh, b, l, 1.6f, iconColor);
        Draw.Line(vh, l, t, 1.6f, iconColor);
        Draw.Diamond(vh, c, rad * .36f, Frame);
    }

    // 계기판의 머리말과 동작 버튼만 얇은 선으로 나눈다.
    private void WriteSciFiFrame(VertexHelper vh, Rect r)
    {
        if (r.width < 40f || r.height < 72f) return;
        Draw.Rect(vh, new Rect(r.xMin + 20f, r.yMax - 35f, 72f, 1.5f), Line);
        Draw.Rect(vh, new Rect(r.xMin + 20f, r.yMin + 68f, r.width - 40f, 1f), LineDim);
    }

    // 등급 칩 회전 Sci-Fi 홀로그램 조준 고리(분절 호 4개 + 십자 눈금)
    private void WriteReticle(VertexHelper vh, Rect r)
    {
        var c = r.center;
        float outer = Mathf.Min(r.width, r.height) * .40f;
        float inner = outer * .72f;
        Color32 soft = iconColor; soft.a = 150;
        for (int q = 0; q < 4; q++)
        {
            float baseDeg = q * 90f + 12f;
            const int steps = 4;
            for (int s = 0; s < steps; s++)
            {
                float a0 = (baseDeg + s * 16.5f) * Mathf.Deg2Rad;
                float a1 = (baseDeg + (s + 1) * 16.5f) * Mathf.Deg2Rad;
                Draw.Line(vh, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * outer, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * outer, 1.4f, iconColor);
            }
            float tick = (q * 90f) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(tick), Mathf.Sin(tick));
            Draw.Line(vh, c + dir * inner, c + dir * (outer + 1.8f), 1.4f, soft);
        }
        // 내부 점선 사각 마름모
        Draw.Diamond(vh, c, inner * .24f, soft);
    }

    // 구역 지도 대형 버튼 선택 하이라이트(MapFrameSelected) 및 상세 맵 탐색 활성 프레임(MapFrameDetail)
    private void WriteActiveFrame(VertexHelper vh, Rect r)
    {
        if (r.width < 20f || r.height < 20f || mapFrameMode == MapFrameHidden) return;
        bool selected = mapFrameMode == MapFrameSelected;
        Draw.ChamferOutline(vh, r, 12f, selected ? 2.4f : 1.6f, selected ? LineBright : Line);
        Draw.Rect(vh, new Rect(r.xMin + 6f, r.yMax - 76f, 3f, 48f), selected ? LineBright : Line);
    }

    // 재시작·포기 확인 팝업 Sci-Fi 모달 프레임
    private void WritePopupFrame(VertexHelper vh, Rect r)
    {
        if (r.width < 40f || r.height < 40f) return;
        Color32 accent = popupDanger ? BossRed : Line;
        Draw.Chamfer(vh, r, 18f, TrainingBoardStyle.Surface);
        Draw.ChamferOutline(vh, r, 18f, 1.8f, Line);
        var inner = Rect.MinMaxRect(r.xMin + 7f, r.yMin + 7f, r.xMax - 7f, r.yMax - 7f);
        Draw.ChamferOutline(vh, inner, 14f, 1f, LineDim);
        Draw.Rect(vh, new Rect(r.xMin + 24f, r.yMax - 66f, r.width - 48f, 1f), LineDim);
        Draw.Rect(vh, new Rect(r.xMin + 24f, r.yMax - 66f, 78f, 2f), accent);
        var iconRect = new Rect(r.xMin + 27f, r.yMax - 48f, 24f, 24f);
        var saved = iconColor;
        iconColor = accent;
        WriteIconThreat(vh, iconRect);
        iconColor = saved;
    }

    /// <summary>지금 상태로 Mesh를 만들어 output에 채운다(검증용, 화면 갱신과 무관). 정점 수를 돌려준다.</summary>
    public int BuildMesh(Mesh output)
    {
        using (var vh = new VertexHelper())
        {
            OnPopulateMesh(vh);
            vh.FillMesh(output);
            return vh.currentVertCount;
        }
    }

    /// <summary>
    /// 단말기 생성 Mesh의 기본 도형(할당 없음 — 정적 버퍼). 현장 단말기 지도 조작 버튼(DungeonFieldTerminalMapGraphic Controls)도 쓴다.
    /// </summary>
    public static class Draw
    {
        public static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
        {
            if (tint.a == 0) return;
            int i = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero);
            vh.AddVert(c, tint, Vector2.zero); vh.AddVert(d, tint, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        public static void Rect(VertexHelper vh, UnityEngine.Rect r, Color32 tint)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            Quad(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), tint);
        }

        /// <summary>사각형 안쪽으로 두께 t의 테두리.</summary>
        public static void Outline(VertexHelper vh, UnityEngine.Rect r, float t, Color32 tint)
        {
            Rect(vh, UnityEngine.Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMin + t), tint);
            Rect(vh, UnityEngine.Rect.MinMaxRect(r.xMin, r.yMax - t, r.xMax, r.yMax), tint);
            Rect(vh, UnityEngine.Rect.MinMaxRect(r.xMin, r.yMin + t, r.xMin + t, r.yMax - t), tint);
            Rect(vh, UnityEngine.Rect.MinMaxRect(r.xMax - t, r.yMin + t, r.xMax, r.yMax - t), tint);
        }

        /// <summary>사각 꼴 테두리(안쪽으로 두께 t — 꼭짓점 쪽으로 줄인 안쪽 꼴과의 띠). 볼록 꼴만.</summary>
        public static void QuadOutline(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t, Color32 tint)
        {
            if (tint.a == 0) return;
            var center = (a + b + c + d) * .25f;
            Vector2 ia = Inner(a, center, t), ib = Inner(b, center, t), ic = Inner(c, center, t), id = Inner(d, center, t);
            Band(vh, a, b, ib, ia, tint);
            Band(vh, b, c, ic, ib, tint);
            Band(vh, c, d, id, ic, tint);
            Band(vh, d, a, ia, id, tint);
        }

        private static Vector2 Inner(Vector2 p, Vector2 center, float t)
        {
            var d = center - p;
            float len = d.magnitude;
            return len <= t * 1.5f ? center : p + d / len * (t * 1.4142f);
        }

        private static void Band(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint) => Quad(vh, a, b, c, d, tint);

        /// <summary>선분(끝을 늘이지 않음).</summary>
        public static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 tint)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f || tint.a == 0) return;
            d /= len;
            Vector2 n = new Vector2(-d.y, d.x) * (width * .5f);
            Quad(vh, a - n, b - n, b + n, a + n, tint);
        }

        public static void Diamond(VertexHelper vh, Vector2 c, float half, Color32 tint)
        {
            Quad(vh, new Vector2(c.x, c.y - half), new Vector2(c.x + half, c.y), new Vector2(c.x, c.y + half), new Vector2(c.x - half, c.y), tint);
        }

        public static void Cross(VertexHelper vh, Vector2 c, float arm, float thick, Color32 tint)
        {
            float h = thick * .5f;
            Rect(vh, UnityEngine.Rect.MinMaxRect(c.x - arm, c.y - h, c.x + arm, c.y + h), tint);
            Rect(vh, UnityEngine.Rect.MinMaxRect(c.x - h, c.y - arm, c.x + h, c.y - h), tint);
            Rect(vh, UnityEngine.Rect.MinMaxRect(c.x - h, c.y + h, c.x + h, c.y + arm), tint);
        }

        /// <summary>사각형 네 귀퉁이의 ㄱ자 꺾쇠(길이 len, 굵기 w — 사각형 안쪽에 그린다).</summary>
        public static void Brackets(VertexHelper vh, UnityEngine.Rect r, float len, float w, Color32 tint)
        {
            float k = Mathf.Min(len, Mathf.Min(r.width, r.height) * .45f);
            float l = r.xMin, rr = r.xMax, b = r.yMin, t = r.yMax;
            Rect(vh, UnityEngine.Rect.MinMaxRect(l, t - w, l + k, t), tint);
            Rect(vh, UnityEngine.Rect.MinMaxRect(l, t - k, l + w, t - w), tint);
            Rect(vh, UnityEngine.Rect.MinMaxRect(rr - k, t - w, rr, t), tint);
            Rect(vh, UnityEngine.Rect.MinMaxRect(rr - w, t - k, rr, t - w), tint);
            Rect(vh, UnityEngine.Rect.MinMaxRect(l, b, l + k, b + w), tint);
            Rect(vh, UnityEngine.Rect.MinMaxRect(l, b + w, l + w, b + k), tint);
            Rect(vh, UnityEngine.Rect.MinMaxRect(rr - k, b, rr, b + w), tint);
            Rect(vh, UnityEngine.Rect.MinMaxRect(rr - w, b + w, rr, b + k), tint);
        }

        // 왼쪽 위·오른쪽 아래를 깎은 육각형(시계 방향, DungeonMapMesh.Chamfered와 같은 순서) — 정적 버퍼.
        private static void ChamferPoly(UnityEngine.Rect r, float cut)
        {
            cut = Mathf.Min(cut, r.width * .45f, r.height * .45f);
            poly[0] = new Vector2(r.xMin, r.yMax - cut);
            poly[1] = new Vector2(r.xMin + cut, r.yMax);
            poly[2] = new Vector2(r.xMax, r.yMax);
            poly[3] = new Vector2(r.xMax, r.yMin + cut);
            poly[4] = new Vector2(r.xMax - cut, r.yMin);
            poly[5] = new Vector2(r.xMin, r.yMin);
        }

        /// <summary>깎인 칸 채움(왼쪽 위·오른쪽 아래 45°).</summary>
        public static void Chamfer(VertexHelper vh, UnityEngine.Rect r, float cut, Color32 tint)
        {
            if (tint.a == 0 || r.width <= 0f || r.height <= 0f) return;
            ChamferPoly(r, cut);
            int i = vh.currentVertCount;
            for (int k = 0; k < poly.Length; k++) vh.AddVert(poly[k], tint, Vector2.zero);
            for (int k = 1; k < poly.Length - 1; k++) vh.AddTriangle(i, i + k, i + k + 1);
        }

        /// <summary>깎인 칸 테두리(안쪽으로 두께 w).</summary>
        public static void ChamferOutline(VertexHelper vh, UnityEngine.Rect r, float cut, float w, Color32 tint)
        {
            if (tint.a == 0 || r.width <= 0f || r.height <= 0f) return;
            ChamferPoly(r, cut);
            for (int k = 0; k < poly.Length; k++)
            {
                Vector2 a = poly[k], b = poly[(k + 1) % poly.Length];
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                d /= len;
                // 시계 방향 꼴의 안쪽 법선 = 오른쪽(+90° 시계) = (d.y, -d.x)
                Vector2 n = new Vector2(d.y, -d.x) * w;
                Quad(vh, a, b, b + n, a + n, tint);
            }
        }
    }
}
