using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// 던전 목표 패널(3차, 2026-11-24, DESIGN_V3 '평소 HUD'): 훈련 상황판(던전 중 간략판) 바로 아래에 'LEVERS 1/3' + 진행 칸을 앰버로 보인다.
/// 게임플레이(DungeonManager)가 <see cref="IDungeonObjectiveSource"/>로 내주는 읽기 전용 상태·이벤트를 구독해 그리기만 한다(AGENTS.md UI 접점 규칙).
/// DungeonManager가 Resources/UI/DungeonObjectivePanel Prefab(DungeonRules.objectivePanelPrefab)을 UI Canvas의 TrainingScoreboard 바로 다음 형제로 만들고
/// IDungeonObjectiveView.Bind로 연결한다(MainScene·TrainingScoreboard.prefab 무수정).
/// - 보임: (목표가 있음(HasObjective) || 난이도 줄이 있음(HasThreat)) && <see cref="DungeonHudGate"/>(HUD 보임·패배 화면·결과 보드 아님) && 출구에 들어가지 않음.
///   페이드 0.2초/0.25초(실제 시간).
/// - 완료 순간(ObjectiveCompleted): 그 칸이 흰빛 앰버로 한 번 번쩍이고(0.9초, 미니맵 클리어 펄스와 같은 시간표) 글자가 밝아졌다 돌아온다. 모두 완료하면 판 테두리가 밝게 유지된다.
/// - 4차(2026-11-25, DESIGN_V4 §2.6·§2.8): 원본이 <see cref="IDungeonThreatSource"/>·<see cref="IDungeonExitSource"/>도 구현하면(선택 인터페이스, 'is'로 확인) 함께 구독한다.
///   · 첫 줄 난이도: 'THREAT 중급'(단계 색 — DungeonThreatText) + 오른쪽 5칸 막대(지금 단계까지 채움, 같은 Cells 그래픽). 목표가 없는 Wave(W2)도 이 줄만 보인다.
///   · 두 줄이면 판을 stackedHeight(54)로 늘리고(아래로 — 피벗 (1,1)) 줄 간격을 좁힌다. 한 줄이면 3차 크기(46) 그대로.
///   · 출구 해금 뒤 목표 줄 = 'EXIT OPEN'(앰버) + 칸 모두 켜짐 + 글자·판 테두리 앰버 맥동(exitPulsePeriod). 출구에 들어가면(ExitEntered) 패널을 걷는다.
/// - 글자: 정적 글꼴 esamanru(공백은 여백 태그, 영문 대문자·숫자, 난이도 단계 한글 이름). 진행·단계가 바뀔 때만 글자를 다시 만든다 — 매 프레임 할당 없음.
/// Prefab은 tools/Unity/DungeonFieldTerminalSetup.cs가 만든다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonObjectivePanel : MonoBehaviour, IDungeonObjectiveView, IDungeonThreatView
{
    public const string ResourcePath = "UI/DungeonObjectivePanel";

    [Header("Parts")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private DungeonObjectivePanelGraphic plate;
    [SerializeField] private DungeonObjectivePanelGraphic cells;
    [SerializeField] private DungeonObjectivePanelGraphic flash;
    [SerializeField] private TMP_Text labelText;

    [Header("4차: 난이도 줄(비어 있으면 그리지 않는다)")]
    [SerializeField] private TMP_Text threatText;
    [SerializeField] private DungeonObjectivePanelGraphic threatBar;
    [SerializeField] private string threatLabel = "THREAT";
    [SerializeField] private string exitOpenLabel = "EXIT OPEN";

    [Header("4차: 줄 배치(px) — 한 줄 = 3차 크기, 두 줄 = 아래로 늘림")]
    [SerializeField, Min(10f)] private float singleHeight = 46f;
    [SerializeField, Min(10f)] private float stackedHeight = 54f;
    [Tooltip("두 줄일 때 위·아래 여백(줄 높이 = (stackedHeight − 2 × 여백) / 2)")]
    [SerializeField, Min(0f)] private float stackedPadding = 5f;

    [Header("Motion (실제 시간)")]
    [SerializeField, Min(.01f)] private float fadeInTime = .2f;
    [SerializeField, Min(.01f)] private float fadeOutTime = .25f;
    [SerializeField, Min(.1f)] private float flashTime = .9f;
    [Tooltip("모두 완료한 뒤 판 테두리 밝기(판 CanvasRenderer 색 곱)")]
    [SerializeField, Range(0f, 1f)] private float idlePlateAlpha = .85f;
    [Tooltip("4차: 출구 해금 뒤 앰버 맥동 주기(초)")]
    [SerializeField, Min(.1f)] private float exitPulsePeriod = 1.1f;

    private IDungeonObjectiveSource source;
    private IDungeonThreatSource threatSource;
    private IDungeonExitSource exitSource;
    private bool initialized;
    private float clock, alpha;
    private int shownDone = -1, shownTotal = -1;
    private string shownLabel;
    private Color shownColor;
    private bool shownExitOpen;
    private int flashIndex = -1;
    private float flashStart;
    private readonly StringBuilder builder = new StringBuilder(64);
    private Color labelBase = DungeonObjectivePanelGraphic.AmberBright;
    private bool labelGlowing;
    // 4차
    private int shownThreat = -1;
    private bool threatRow, objectiveRow, exitOpen, exitEntered;
    private int layoutRows = -1;
    private bool layoutThreat;

    // ===== 검증이 읽는 값 =====
    public IDungeonObjectiveSource Source => source;
    public IDungeonThreatSource ThreatSource => threatSource;
    public IDungeonExitSource ExitSource => exitSource;
    public float Alpha => alpha;
    public int ShownDone => shownDone;
    public int ShownTotal => shownTotal;
    public int FlashIndex => flashIndex;
    public float FlashTime => flashTime;
    public TMP_Text LabelText => labelText;
    public TMP_Text ThreatText => threatText;
    public DungeonObjectivePanelGraphic CellsGraphic => cells;
    public DungeonObjectivePanelGraphic FlashGraphic => flash;
    public DungeonObjectivePanelGraphic PlateGraphic => plate;
    public DungeonObjectivePanelGraphic ThreatBar => threatBar;
    /// <summary>지금 난이도 줄·목표 줄을 그리는지, 출구 해금 표시 중인지(검증용).</summary>
    public bool ThreatRowVisible => threatRow;
    public bool ObjectiveRowVisible => objectiveRow;
    public bool ExitOpenShown => exitOpen;
    public float SingleHeight => singleHeight;
    public float StackedHeight => stackedHeight;
    /// <summary>HUD 숨김을 따를 CanvasGroup을 직접 지정한다(검증용).</summary>
    public CanvasGroup HudGroupOverride { get; set; }

    private void Awake() => EnsureInit();

    private void EnsureInit()
    {
        if (initialized) return;
        initialized = true;
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        if (flash != null)
        {
            flash.SetFlash(-1);
            flash.canvasRenderer.SetAlpha(0f);
        }
        if (labelText != null) labelBase = labelText.color;
        if (threatText != null) { threatText.richText = true; threatText.text = ""; }
        if (threatBar != null) threatBar.SetProgress(0, 0, DungeonObjectivePanelGraphic.Amber);
    }

    private void OnDestroy() => Unbind();

    public void Bind(IDungeonObjectiveSource objectives)
    {
        EnsureInit();
        if (ReferenceEquals(source, objectives)) { Refresh(); return; }
        Unbind();
        source = objectives;
        if (source == null) return;
        source.ObjectiveChanged += Refresh;
        source.ObjectiveCompleted += OnCompleted;
        // 4차 선택 인터페이스: 원본(DungeonManager)이 난이도·출구도 내주면 함께 구독한다.
        if (objectives is IDungeonThreatSource threats) BindThreat(threats);
        if (objectives is IDungeonExitSource exits) BindExit(exits);
        Refresh();
    }

    /// <summary>모든 원본(목표·난이도·출구) 연결을 끊는다.</summary>
    public void Unbind()
    {
        if (source != null)
        {
            source.ObjectiveChanged -= Refresh;
            source.ObjectiveCompleted -= OnCompleted;
        }
        source = null;
        UnbindThreat();
        UnbindExit();
        shownDone = shownTotal = -1;
        shownExitOpen = false;
        flashIndex = -1;
        if (initialized && flash != null)
        {
            flash.SetFlash(-1);
            flash.canvasRenderer.SetAlpha(0f);
        }
    }

    // IDungeonThreatView(명시 구현 — Bind(IDungeonObjectiveSource) 호출이 모호해지지 않게)
    void IDungeonThreatView.Bind(IDungeonThreatSource threats) => BindThreat(threats);
    void IDungeonThreatView.Unbind() => UnbindThreat();

    /// <summary>난이도 원본을 연결한다(같은 원본이면 그대로).</summary>
    public void BindThreat(IDungeonThreatSource threats)
    {
        EnsureInit();
        if (!ReferenceEquals(threatSource, threats))
        {
            UnbindThreat();
            threatSource = threats;
            if (threatSource != null) threatSource.ThreatChanged += OnThreatChanged;
        }
        Refresh();
    }

    /// <summary>출구 원본을 연결한다(같은 원본이면 그대로).</summary>
    public void BindExit(IDungeonExitSource exits)
    {
        EnsureInit();
        if (!ReferenceEquals(exitSource, exits))
        {
            UnbindExit();
            exitSource = exits;
            if (exitSource != null) exitSource.ExitChanged += Refresh;
        }
        Refresh();
    }

    private void UnbindThreat()
    {
        if (threatSource != null) threatSource.ThreatChanged -= OnThreatChanged;
        threatSource = null;
        shownThreat = -1;
    }

    private void UnbindExit()
    {
        if (exitSource != null) exitSource.ExitChanged -= Refresh;
        exitSource = null;
        exitOpen = exitEntered = false;
    }

    private void OnThreatChanged(DungeonThreatLevel previous, DungeonThreatLevel next) => Refresh();

    private void OnCompleted(int index)
    {
        Refresh();
        StartFlash(index);
    }

    /// <summary>목표 index 칸을 한 번 번쩍인다(완료 순간). 이벤트가 부르며 검증도 직접 부른다.</summary>
    public void StartFlash(int index)
    {
        EnsureInit();
        if (index < 0 || flash == null) return;
        flashIndex = index;
        flashStart = clock;
        flash.SetFlash(index);
        flash.canvasRenderer.SetAlpha(0f);
    }

    /// <summary>원본 진행을 다시 읽는다(바뀐 경우에만 글자·칸을 다시 만든다).</summary>
    public void Refresh()
    {
        EnsureInit();
        RefreshExit();
        RefreshThreat();
        RefreshObjective();
        ApplyLayout();
    }

    private void RefreshExit()
    {
        bool has = ExitAlive() && exitSource.HasExit;
        exitOpen = has && exitSource.ExitUnlocked;
        exitEntered = has && exitSource.ExitEntered;
    }

    // 난이도 줄: 단계가 바뀔 때만 글자·막대를 다시 만든다. 줄이 없으면(원본 없음·HasThreat 거짓·Prefab에 부품 없음) 비운다.
    private void RefreshThreat()
    {
        bool want = threatText != null && ThreatAlive() && threatSource.HasThreat;
        if (!want)
        {
            if (threatRow)
            {
                threatRow = false;
                shownThreat = -1;
                if (threatText != null) threatText.text = "";
                if (threatBar != null) threatBar.SetProgress(0, 0, DungeonObjectivePanelGraphic.Amber);
            }
            return;
        }
        threatRow = true;
        int level = Mathf.Clamp((int)threatSource.ThreatLevel, 0, ThreatLevels - 1);
        if (level == shownThreat) return;
        shownThreat = level;
        var lv = (DungeonThreatLevel)level;
        Color color = DungeonThreatText.Color(lv);
        builder.Clear();
        builder.AppendColorTag(color).AppendStatic(threatLabel).Append(DungeonUiText.SpaceTag).Append(DungeonThreatText.Label(lv)).Append("</color>");
        threatText.text = builder.ToString();
        if (threatBar != null) threatBar.SetProgress(ThreatLevels, (1 << (level + 1)) - 1, color);
    }

    /// <summary>난이도 막대 칸 수(입문~특급 5단계).</summary>
    public const int ThreatLevels = 5;

    private void RefreshObjective()
    {
        if (!SourceAlive() || !source.HasObjective)
        {
            // 목표가 끝나면 페이드아웃 동안 마지막 표시를 유지한다(줄 배치에서만 뺀다).
            // 단 난이도 줄만 남는 경우(W2)에는 그 줄과 겹치지 않게 목표 줄을 비운다.
            objectiveRow = false;
            if (threatRow) ClearObjectiveRow();
            return;
        }
        objectiveRow = true;
        int total = Mathf.Clamp(source.ObjectiveTotal, 0, DungeonObjectivePanelGraphic.MaxCells);
        int done = Mathf.Clamp(source.ObjectiveDone, 0, total);
        string label = source.ObjectiveShortLabel;
        Color color = source.ObjectiveColor;
        // 출구 해금 뒤에는 칸을 모두 켠다(레버를 다 당겼다).
        int mask = exitOpen ? (1 << total) - 1 : DoneMask(source, total);
        if (cells != null) cells.SetProgress(total, mask, color);
        if (flash != null) flash.SetProgress(total, mask, color);
        if (done == shownDone && total == shownTotal && label == shownLabel && color == shownColor && exitOpen == shownExitOpen) return;
        shownDone = done;
        shownTotal = total;
        shownLabel = label;
        shownColor = color;
        shownExitOpen = exitOpen;
        if (labelText != null)
        {
            builder.Clear();
            if (exitOpen)
                builder.AppendColorTag(color).AppendStatic(exitOpenLabel).Append("</color>");
            else
                // 짧은 표기는 목표 색 태그, 진행 수는 글자 기본색(밝은 앰버 — 완료 순간 흰빛으로 번쩍임).
                builder.AppendColorTag(color).AppendStatic(string.IsNullOrEmpty(label) ? "OBJECTIVE" : label).Append("</color>").Append(DungeonUiText.SpaceTag)
                       .Append(done).Append('/').Append(total);
            labelText.text = builder.ToString();
        }
    }

    private void ClearObjectiveRow()
    {
        if (shownTotal < 0 && shownDone < 0) return;
        shownDone = shownTotal = -1;
        shownLabel = null;
        shownExitOpen = false;
        if (labelText != null) labelText.text = "";
        if (cells != null) cells.SetProgress(0, 0, DungeonObjectivePanelGraphic.Amber);
        if (flash != null) flash.SetProgress(0, 0, DungeonObjectivePanelGraphic.Amber);
    }

    // 줄 배치: 두 줄이면 판을 늘리고 위에서부터 난이도 → 목표 순서로 둔다. 바뀔 때만 RectTransform을 고친다.
    private void ApplyLayout()
    {
        bool threat = threatRow;
        int rows = (threat ? 1 : 0) + (objectiveRow ? 1 : 0);
        if (rows == 0) return;                        // 걷히는 중: 마지막 배치를 유지한다
        if (rows == layoutRows && threat == layoutThreat) return;
        layoutRows = rows;
        layoutThreat = threat;
        float height = rows > 1 ? stackedHeight : singleHeight;
        float row = rows > 1 ? Mathf.Max(1f, (stackedHeight - 2f * stackedPadding) * .5f) : singleHeight;
        float top = rows > 1 ? stackedPadding : 0f;
        var rt = transform as RectTransform;
        if (rt != null && !Mathf.Approximately(rt.sizeDelta.y, height)) rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        float firstY = -(top + row * .5f);
        float secondY = -(top + row * 1.5f);
        float objectiveY = threat ? secondY : firstY;
        if (threatText != null) SetRowY(threatText.rectTransform, firstY);
        if (threatBar != null) SetRowY(threatBar.rectTransform, firstY);
        if (labelText != null) SetRowY(labelText.rectTransform, objectiveY);
        if (cells != null) SetRowY(cells.rectTransform, objectiveY);
        if (flash != null) SetRowY(flash.rectTransform, objectiveY);
    }

    // 줄 하나를 판 위 끝 기준 y에 둔다(가로 앵커·피벗은 그대로, 세로는 위 끝 기준·가운데 피벗).
    private static void SetRowY(RectTransform rt, float y)
    {
        if (rt == null) return;
        var min = rt.anchorMin; var max = rt.anchorMax; var pivot = rt.pivot;
        if (min.y != 1f || max.y != 1f) { rt.anchorMin = new Vector2(min.x, 1f); rt.anchorMax = new Vector2(max.x, 1f); }
        if (pivot.y != .5f) rt.pivot = new Vector2(pivot.x, .5f);
        var p = rt.anchoredPosition;
        if (!Mathf.Approximately(p.y, y)) rt.anchoredPosition = new Vector2(p.x, y);
    }

    // 완료한 칸 비트. 표식 목록(진행 번호 index·완료)을 우선하고, 없으면 완료 수만큼 앞에서부터 채운다.
    private static int DoneMask(IDungeonObjectiveSource objectives, int total)
    {
        int mask = 0;
        var markers = objectives.ObjectiveMarkers;
        if (markers != null && markers.Count > 0)
        {
            for (int i = 0; i < markers.Count; i++)
                if (markers[i].done && markers[i].index >= 0 && markers[i].index < total) mask |= 1 << markers[i].index;
            return mask;
        }
        int done = Mathf.Clamp(objectives.ObjectiveDone, 0, total);
        for (int i = 0; i < done; i++) mask |= 1 << i;
        return mask;
    }

    private bool SourceAlive()
    {
        if (source == null) return false;
        if (source is Object unityObject && unityObject == null) { source = null; return false; }
        return true;
    }

    private bool ThreatAlive()
    {
        if (threatSource == null) return false;
        if (threatSource is Object unityObject && unityObject == null) { threatSource = null; return false; }
        return true;
    }

    private bool ExitAlive()
    {
        if (exitSource == null) return false;
        if (exitSource is Object unityObject && unityObject == null) { exitSource = null; return false; }
        return true;
    }

    private void Update() => Advance(Time.unscaledDeltaTime);

    /// <summary>실제 시간 dt만큼 페이드·번쩍임·출구 맥동을 진행한다(Update가 부른다. 검증은 직접 부른다). 할당하지 않는다.</summary>
    public void Advance(float deltaTime)
    {
        EnsureInit();
        deltaTime = Mathf.Max(0f, deltaTime);
        clock += deltaTime;
        bool show = ShouldShow();
        alpha = Mathf.MoveTowards(alpha, show ? 1f : 0f, deltaTime / (show ? fadeInTime : fadeOutTime));
        if (group != null && !Mathf.Approximately(group.alpha, alpha)) group.alpha = alpha;

        float glow = 0f;
        if (flash != null && flashIndex >= 0)
        {
            float t = clock - flashStart;
            glow = DungeonMinimap.ClearPulseAlpha(t, flashTime);
            flash.canvasRenderer.SetAlpha(glow);
            if (t >= flashTime)
            {
                flashIndex = -1;
                flash.SetFlash(-1);
                flash.canvasRenderer.SetAlpha(0f);
            }
        }
        // 출구 해금 뒤 앰버 맥동(글자 흰빛 쪽으로·판 테두리 밝기). 번쩍임이 있으면 그쪽이 이긴다.
        float pulse = exitOpen && objectiveRow ? .5f - .5f * Mathf.Cos(clock * Mathf.PI * 2f / exitPulsePeriod) : 0f;
        float lit = Mathf.Max(glow, pulse * .6f);
        if (labelText != null && (lit > 0f || labelGlowing))
        {
            labelText.color = Color.Lerp(labelBase, Color.white, lit * .7f);
            labelGlowing = lit > 0f;
        }
        if (plate != null)
        {
            bool complete = shownTotal > 0 && shownDone >= shownTotal;
            float plateAlpha = exitOpen && objectiveRow ? Mathf.Lerp(idlePlateAlpha, 1f, pulse)
                             : complete ? 1f : Mathf.Lerp(idlePlateAlpha, 1f, glow);
            plate.canvasRenderer.SetAlpha(plateAlpha);
        }
    }

    /// <summary>지금 보여야 하는가((목표 있음 || 난이도 줄 있음) && 출구에 들어가지 않음 && HUD 보임 && 패배 화면·결과 보드 아님).</summary>
    public bool ShouldShow()
    {
        bool objective = SourceAlive() && source.HasObjective;
        bool threat = threatText != null && ThreatAlive() && threatSource.HasThreat;
        if (!objective && !threat) return false;
        if (ExitAlive() && exitSource.HasExit && exitSource.ExitEntered) return false;
        return DungeonHudGate.ShouldShowDungeonUi(HudGroupOverride);
    }
}
