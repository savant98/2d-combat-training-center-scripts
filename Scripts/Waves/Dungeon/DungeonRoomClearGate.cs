using System;
using UnityEngine;

/// <summary>
/// 7차(DESIGN_V7 V5) 레버 방의 '레버 대기' 흐름(적 전멸 ≠ 방 클리어). DungeonManager가 하나 갖고 연결만 한다(DungeonManager 비대화 방지 — 6차 DungeonCutsceneDirector와 같은 이유).
/// - Hold: 적을 모두 처치했지만 그 방에 아직 당기지 않은 레버가 남은 순간. 방 상태 objectiveHold = true(cleared false·locked true 유지 → 문 잠김)로 두고
///   Changed(방, true)를 올린다. 함정 정지·레버 전원·목표 대상은 부르는 쪽(DungeonManager.HoldForObjective)이 맞춘다.
/// - Release: 그 방 레버를 모두 당긴 순간. 레버 컷씬이 시작됐으면 컷씬이 끝날 때까지(cutsceneBlocking — 레버 방 복귀 뒤), 아니면 빗장 시간 LatchDelay(0.6초, 게임 시간) 뒤에
///   objectiveHold = false → Changed(방, false) → finish(방, 던전 순번, 적 구성)로 기존 방 클리어 흐름(ROOM CLEAR → 난이도 → 던전 조건 → 시설 전원 → 문 열림)을 잇는다.
///   적 구성(fight)은 Hold 때 붙잡아 둔다 — 레버 컷씬 미리보기가 DungeonManager의 지금 구성을 거두기 때문이다(난이도 첫 클리어 판정이 RequiredSpawned를 읽는다).
/// - Abandon: 재도전·패배 정리·던전 종료. 대기 중이었으면 objectiveHold = false · Changed(방, false)만 올린다(RoomCleared는 오지 않는다).
/// - 기다리는 동안 던전 순번·Wave 진행·지금 방이 바뀌면(stillValid 거짓) 버린다.
/// MonoBehaviour가 아니다(코루틴 없음): DungeonManager.Update가 Tick(Time.deltaTime)을 부른다. 대기가 없으면 Tick은 바로 돌아간다(매 프레임 할당 없음 — 콜백은 생성 때 한 번 받는다).
/// </summary>
public sealed class DungeonRoomClearGate
{
    public enum Stage
    {
        /// <summary>대기 없음.</summary>
        Idle = 0,
        /// <summary>레버 대기(레버가 남음 — 문 잠김, 'PULL LEVER').</summary>
        Awaiting = 1,
        /// <summary>레버를 다 당겼고 컷씬 끝·빗장 시간을 기다리는 중(아직 대기 — 'PULL LEVER' 유지).</summary>
        Releasing = 2,
    }

    /// <summary>레버 컷씬을 끈 설정에서 마지막 레버를 당긴 뒤 방 클리어 처리까지 기다리는 시간(초, 게임 시간) — PropObjectiveLever.pullDuration(손잡이 빗장)과 같은 값(판단값).</summary>
    public const float LatchDelay = .6f;

    private readonly Func<int, DungeonRoomState> roomState;
    private readonly Func<bool> cutsceneBlocking;
    private readonly Func<int, int, bool> stillValid;
    private readonly Action<int, int, DungeonEncounter> finish;
    private int serial;
    private DungeonEncounter fight;
    private bool waitCutscene;
    private float latch;

    /// <summary>
    /// roomState(방 id) = 그 방 상태(없으면 null), cutsceneBlocking = 던전 컷씬이 도는 중, stillValid(방 id, 던전 순번) = 아직 그 던전·그 방·Wave 진행 중인지,
    /// finish(방 id, 던전 순번, 적 구성) = 기존 방 클리어 처리(objectiveHold가 이미 false인 상태로 부른다).
    /// </summary>
    public DungeonRoomClearGate(Func<int, DungeonRoomState> roomState, Func<bool> cutsceneBlocking, Func<int, int, bool> stillValid, Action<int, int, DungeonEncounter> finish)
    {
        this.roomState = roomState;
        this.cutsceneBlocking = cutsceneBlocking;
        this.stillValid = stillValid;
        this.finish = finish;
    }

    public Stage Current { get; private set; }
    /// <summary>대기 중인 방 id(없으면 -1).</summary>
    public int Room { get; private set; } = -1;
    /// <summary>레버 대기 중인지(레버를 다 당긴 뒤 방 클리어 처리 직전까지 포함).</summary>
    public bool Awaiting => Current != Stage.Idle;
    /// <summary>Release 뒤 컷씬 끝을 기다리는지(검사용).</summary>
    public bool WaitingCutscene => Current == Stage.Releasing && waitCutscene;
    /// <summary>Release 뒤 흐른 빗장 시간(초, 검사용).</summary>
    public float LatchTime => latch;

    /// <summary>레버 대기가 시작(true)되거나 끝난(false) 순간(방 id, awaiting). IDungeonObjectiveGate.ObjectiveGateChanged가 이 이벤트를 그대로 내준다.</summary>
    public event Action<int, bool> Changed;

    /// <summary>방 room의 레버 대기를 시작한다(적 전멸 순간). 이미 다른 대기가 있으면 먼저 버린다.</summary>
    public void Hold(int room, int floorSerial, DungeonEncounter roomFight)
    {
        if (Current != Stage.Idle) Abandon();
        Room = room;
        serial = floorSerial;
        fight = roomFight;
        waitCutscene = false;
        latch = 0f;
        Current = Stage.Awaiting;
        var state = roomState != null ? roomState(room) : null;
        if (state != null) state.objectiveHold = true;
        Raise(room, true);
    }

    /// <summary>대기 방의 레버를 모두 당겼다. cutscene = 레버 컷씬이 시작됐는지(끝날 때까지 기다린다 — 아니면 LatchDelay초).</summary>
    public void Release(bool cutscene)
    {
        if (Current != Stage.Awaiting) return;
        Current = Stage.Releasing;
        waitCutscene = cutscene;
        latch = 0f;
    }

    /// <summary>한 프레임 진행(게임 시간 dt). 대기가 끝날 때가 되면 방 클리어 처리를 잇는다.</summary>
    public void Tick(float deltaTime)
    {
        if (Current != Stage.Releasing) return;
        if (stillValid != null && !stillValid(Room, serial)) { Abandon(); return; }
        if (waitCutscene)
        {
            // 컷씬(레버 방 미리보기 → 복귀)이 끝난 다음 프레임에 잇는다 — HUD가 돌아온 뒤 ROOM CLEAR가 보인다.
            if (cutsceneBlocking != null && cutsceneBlocking()) return;
        }
        else
        {
            latch += Mathf.Max(0f, deltaTime);
            if (latch < LatchDelay) return;
        }
        int room = Room, floorSerial = serial;
        var roomFight = fight;
        Clear(room);
        Raise(room, false);
        finish?.Invoke(room, floorSerial, roomFight);
    }

    /// <summary>대기를 버린다(재도전·패배 정리·던전 종료). 대기 중이었으면 Changed(방, false)를 올린다.</summary>
    public void Abandon()
    {
        if (Current == Stage.Idle) return;
        int room = Room;
        Clear(room);
        Raise(room, false);
    }

    private void Clear(int room)
    {
        var state = roomState != null ? roomState(room) : null;
        if (state != null) state.objectiveHold = false;
        Current = Stage.Idle;
        Room = -1;
        fight = null;
        waitCutscene = false;
        latch = 0f;
    }

    private void Raise(int room, bool awaiting)
    {
        var handler = Changed;
        if (handler == null) return;
        try { handler(room, awaiting); }
        catch (Exception e) { Debug.LogException(e); }
    }
}
