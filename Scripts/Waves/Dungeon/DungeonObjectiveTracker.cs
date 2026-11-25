using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 던전 하나의 목표(레버 등) 진행 상태(생성 규칙 v3-12). DungeonManager가 하나 갖고 던전 시작(Begin)·끝(End)에 맞춰 바꾸며,
/// IDungeonObjectiveSource는 이 값을 그대로 위임한다(DungeonManager에는 연결만 둔다). 순수 C#이고 Unity 개체를 잡지 않는다.
/// 목표 = 실행 데이터에서 배정된 목표 슬롯(room.objectives 중 index ≥ 0). 완료 수가 목표 SO의 RequiredCount(배치 수)에 닿으면 IsComplete.
/// 4차(DESIGN_V4 §2.6 R11): 목표 SO의 finish가 ExitInStartRoom이고 시작 방에 출구 자리가 있으면 '레버 n/3 → 출구 해금 → 출구 입장' 단계를 갖는다
/// (UsesExit). 이때 던전 조건은 IsComplete가 아니라 IsFinished(= 출구에 들어감)다. 출구 방식인데 시작 방에 출구 자리가 없으면 즉시 방식으로 대신한다
/// (RequiresExit && !ExitPlaced — 경고는 DungeonManager가 낸다). ExitShownDone·ExitShownUnlocked는 문이 마지막으로 보인 진행(지연 재생용)이다.
/// </summary>
public sealed class DungeonObjectiveTracker
{
    private sealed class Entry
    {
        public int room;
        public int slot;
        public int index;
        public bool done;
        public bool known;
    }

    private readonly List<Entry> entries = new();
    private readonly List<DungeonObjectiveMarker> markers = new();
    private DungeonObjectiveSO objective;
    private Func<int, bool> isKnown;
    private int required;
    private int done;
    // 4차 출구 상태.
    private bool requiresExit;
    private int exitRoom = -1;
    private bool exitEntered;
    private int exitShownDone;
    private bool exitShownUnlocked;

    /// <summary>목표가 있는 던전인지(목표 SO가 있고 하나 이상 배치됨).</summary>
    public bool HasObjective => objective != null && entries.Count > 0;
    public DungeonObjectiveSO Objective => objective;
    public string Title => objective != null ? objective.title : string.Empty;
    public string ShortLabel => objective != null ? objective.shortLabel : string.Empty;
    public Color Color => objective != null ? objective.color : new Color(1f, .69f, .13f);
    /// <summary>클리어에 필요한 수(목표 SO의 RequiredCount, 배치 수 이하).</summary>
    public int Total => HasObjective ? required : 0;
    public int Done => HasObjective ? done : 0;
    /// <summary>배치한 목표 수(표식 수).</summary>
    public int Placed => entries.Count;
    public bool IsComplete => HasObjective && required > 0 && done >= required;
    /// <summary>표식(진행 번호 순서). 던전 시작 때 만들고 완료·알게 됨이 바뀔 때 고친다.</summary>
    public IReadOnlyList<DungeonObjectiveMarker> Markers => markers;

    // ── 출구(4차) ──
    /// <summary>목표 SO가 시작 방 출구 방식을 요구하는지(finish = ExitInStartRoom이고 출구 Prefab이 있으며 목표가 배치됨).</summary>
    public bool RequiresExit => requiresExit;
    /// <summary>시작 방에 출구 자리(DungeonExitSlot)가 있어 출구 문을 세울 수 있는지.</summary>
    public bool ExitPlaced => requiresExit && exitRoom >= 0;
    /// <summary>실제로 출구 단계를 쓰는지(요구 + 자리 있음). 아니면 3차처럼 마지막 목표에서 바로 끝난다.</summary>
    public bool UsesExit => ExitPlaced;
    /// <summary>출구가 선 방 id(시작 방, 출구를 쓰지 않으면 -1).</summary>
    public int ExitRoom => UsesExit ? exitRoom : -1;
    /// <summary>목표를 모두 채워 출구가 열렸는지.</summary>
    public bool ExitUnlocked => UsesExit && IsComplete;
    /// <summary>Player가 출구에 들어갔는지.</summary>
    public bool ExitEntered => UsesExit && exitEntered;
    /// <summary>던전 조건(목표 쪽): 출구를 쓰면 출구 입장, 아니면 목표 완료.</summary>
    public bool IsFinished => UsesExit ? exitEntered && IsComplete : IsComplete;
    /// <summary>문이 마지막으로 보인 완료 수(시작 방에 돌아왔을 때 이 값 이후의 풀림만 연출한다).</summary>
    public int ExitShownDone => exitShownDone;
    /// <summary>문이 마지막으로 보인 해금 여부.</summary>
    public bool ExitShownUnlocked => exitShownUnlocked;
    /// <summary>출구 짧은 표기(목표 SO의 exitShortLabel).</summary>
    public string ExitShortLabel => objective != null ? objective.exitShortLabel : string.Empty;
    /// <summary>출구 해금 뒤 제목(목표 SO의 exitTitle).</summary>
    public string ExitTitle => objective != null ? objective.exitTitle : string.Empty;
    /// <summary>출구 해금·입장·유무가 바뀌었다(던전 시작·끝 포함).</summary>
    public event Action ExitChanged;

    /// <summary>진행·표식·목표 유무가 바뀌었다(던전 시작·끝 포함).</summary>
    public event Action Changed;
    /// <summary>목표 하나를 완료했다(진행 번호).</summary>
    public event Action<int> Completed;

    /// <summary>
    /// 던전 시작: 실행 데이터에서 배정된 목표 슬롯을 읽는다. known(방 id) = 그 방을 알게 됐는지(미니맵 discovered).
    /// 목표가 없는 던전이면 비운 채로 Changed만 알린다.
    /// </summary>
    public void Begin(DungeonFloor floor, Func<int, bool> known)
    {
        entries.Clear();
        markers.Clear();
        done = 0;
        required = 0;
        requiresExit = false;
        exitRoom = -1;
        exitEntered = false;
        exitShownDone = 0;
        exitShownUnlocked = false;
        isKnown = known;
        objective = floor != null ? floor.objective : null;
        if (objective != null && floor.rooms != null)
        {
            foreach (var room in floor.rooms)
            {
                if (room == null || room.objectives == null) continue;
                foreach (var slot in room.objectives)
                    if (slot != null && slot.Assigned) entries.Add(new Entry { room = room.Id, slot = slot.slot, index = slot.index });
            }
            entries.Sort((a, b) => a.index.CompareTo(b.index));
            int wanted;
            try { wanted = objective.RequiredCount(entries.Count); }
            catch (Exception e) { Debug.LogException(e); wanted = entries.Count; }
            required = Mathf.Clamp(wanted, 0, entries.Count);
            // 4차 출구: 목표가 하나 이상 배치됐고 SO가 시작 방 출구 방식이면 출구를 요구한다. 시작 방에 출구 자리가 있어야 실제로 쓴다.
            requiresExit = entries.Count > 0 && objective.UsesStartRoomExit;
            if (requiresExit && floor.layout != null)
            {
                var start = floor.Room(floor.layout.startRoom);
                if (start != null && start.HasExit) exitRoom = start.Id;
            }
        }
        RefreshKnown(false);
        Rebuild();
        Raise();
        RaiseExit();
    }

    /// <summary>던전 끝: 목표를 비우고(있었으면) Changed를 알린다.</summary>
    public void End()
    {
        bool had = objective != null || entries.Count > 0;
        bool hadExit = requiresExit;
        entries.Clear();
        markers.Clear();
        objective = null;
        isKnown = null;
        done = 0;
        required = 0;
        requiresExit = false;
        exitRoom = -1;
        exitEntered = false;
        exitShownDone = 0;
        exitShownUnlocked = false;
        if (had) Raise();
        if (hadExit) RaiseExit();
    }

    /// <summary>
    /// 출구 입장을 기록한다(4차). 출구를 쓰고, 해금됐고, 아직 들어가지 않았을 때만 true(ExitChanged를 알린다).
    /// 시설 사용 조건·지금 방 확인은 부르는 쪽(DungeonManager.TryEnterExit)이 한다.
    /// </summary>
    public bool TryEnterExit()
    {
        if (!ExitUnlocked || exitEntered) return false;
        exitEntered = true;
        RaiseExit();
        return true;
    }

    /// <summary>문이 지금 진행을 보였다고 기록한다(시작 방 지연 재생 뒤). 다음에 다시 세울 때 이 값으로 즉시 맞춘다.</summary>
    public void MarkExitShown(int shownDone, bool shownUnlocked)
    {
        exitShownDone = Mathf.Clamp(shownDone, 0, Mathf.Max(0, required));
        exitShownUnlocked = shownUnlocked;
    }

    /// <summary>그 방·슬롯의 목표 진행 번호(없으면 -1).</summary>
    public int IndexAt(int room, int slot)
    {
        foreach (var entry in entries) if (entry.room == room && entry.slot == slot) return entry.index;
        return -1;
    }

    public bool IsDone(int index)
    {
        foreach (var entry in entries) if (entry.index == index) return entry.done;
        return false;
    }

    /// <summary>
    /// 목표 하나를 완료한다. 그 방·슬롯·번호가 배정과 맞고 아직 완료하지 않았을 때만 true(Completed·Changed를 알린다).
    /// </summary>
    public bool TryComplete(int room, int slot, int index)
    {
        if (!HasObjective) return false;
        foreach (var entry in entries)
        {
            if (entry.index != index) continue;
            if (entry.room != room || entry.slot != slot || entry.done) return false;
            entry.done = true;
            // 완료한 목표의 방은 당연히 알고 있다.
            entry.known = true;
            done++;
            Rebuild();
            try { Completed?.Invoke(index); }
            catch (Exception e) { Debug.LogException(e); }
            Raise();
            // 마지막 목표로 출구가 열렸으면 출구 상태도 알린다(원격 HUD 알림 — 문은 시작 방에 돌아왔을 때 지연 재생).
            if (ExitUnlocked) RaiseExit();
            return true;
        }
        return false;
    }

    /// <summary>방을 알게 됨(discovered)이 바뀌었을 수 있을 때 부른다. 표식의 known이 바뀌었으면 Changed를 알린다.</summary>
    public void RefreshKnown() => RefreshKnown(true);

    private void RefreshKnown(bool raise)
    {
        if (entries.Count == 0) return;
        bool changed = false;
        foreach (var entry in entries)
        {
            bool known = entry.done || (isKnown != null && SafeKnown(entry.room));
            if (known == entry.known) continue;
            entry.known = known;
            changed = true;
        }
        if (!changed || !raise) return;
        Rebuild();
        Raise();
    }

    private bool SafeKnown(int room)
    {
        try { return isKnown(room); }
        catch (Exception e) { Debug.LogException(e); return false; }
    }

    private void Rebuild()
    {
        markers.Clear();
        foreach (var entry in entries) markers.Add(new DungeonObjectiveMarker(entry.room, entry.index, entry.done, entry.known));
    }

    private void Raise()
    {
        try { Changed?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    private void RaiseExit()
    {
        try { ExitChanged?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }
}
