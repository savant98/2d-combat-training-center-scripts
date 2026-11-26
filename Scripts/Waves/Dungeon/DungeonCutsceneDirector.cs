using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 던전 컷씬 감독(6차 DESIGN_V6 T11). DungeonManager가 Configure에서 같은 GameObject에 붙이고 연결만 한다(비대화 금지 — 연출 순서는 모두 여기).
/// Wave를 넘는 코루틴을 여기서 돌린다(DungeonManager.EndFloor의 StopAllCoroutines가 끊지 않게).
/// 공통 기반(<see cref="Begin"/>·<see cref="Cleanup"/>): 게임 입력 막기(UIManager.PushGameplayBlock — 참조 카운트)·일시정지(Esc) 막기·PlayerHUD 숨김(HUDVisible —
/// 던전 HUD도 DungeonHudGate로 함께 숨음)·위아래 검은 띠(CinematicLetterbox 임대)·보호 구속(자식 GameObject의 WavePlayerStart.HoldForPassage — WaveManager holder와 따로,
/// 피해 무시·잡기 차단)·Player 숨김(Player 아래 Renderer.forceRenderingOff·월드 Canvas — Player.prefab 무수정)·덮개 전환(관 속 오버레이 또는 암전)·카메라 Follow 바꾸기.
/// 모든 연출은 한 겹 순서로 직접 돌리고(안쪽 순서를 yield return IEnumerator로 넘기지 않는다) finally에서 원복한다. 바깥에서 끊으면(<see cref="Abort"/>) 같은 원복을 직접 부른다.
/// - 레버 컷씬(<see cref="TryPlayLeverReveal"/>, 모든 레버): 당김을 보여 줌 → 관 속 덮개(던전 지도에서 시작 방 쪽) → 시작 방 미리보기(레버 방 개체는 반납하지 않고 보류 —
///   DungeonRoomStage.BeginPreview, 방 상태 변경 없음) → 카메라를 출구 문으로 → 볼트 풀림·램프 점등(마지막 레버는 봉인·문짝 열림, PropExitGate.SetProgress) → 머묾 →
///   덮개(반대 방향) → 레버 방 복원(보류 해제 — Wave 장치 재조립·경로 크레인 집 복귀·중력 Prop 위치 손실 없음) → 끝. 문이 진행을 보였으면 지연 재생 기록(MarkExitShown)을 남긴다.
///   끊기면(패배·재도전·퇴장) 레버 방을 되살리고 기록을 남기지 않는다 → 시작 방에 돌아왔을 때 4차 지연 재생이 대신한다.
/// - 문 컷씬(<see cref="PlayExitEntry"/>): 흡입(문 실물 또는 직접) → 문틀·문짝을 Player 앞에 그리고 문짝 닫힘 → 맞물리면 Player 숨김 → 봉인 원위치 → '닫히고 불 켜진 문'.
///   그동안 SetFacilityBusy로 Wave 종료를 미룬다. 다음 Wave 시작 방에 도착 자리(DungeonArrivalSlot)가 있으면 실행 사본의 다음 Wave teleportAtStart = false(크레인 생략,
///   WaveManager 무수정) — 출구 자리와 같은 방 좌표면 정렬안(닫힌 문을 화면에 둔 채 지형 패널 연출로 방이 바뀌고 같은 자리에 도착 문이 불 켜진 채 선다),
///   다르면 암전안(암전 동안 도착 문 안쪽으로 순간이동). 'WAVE n' 판 끝 0.3초 전 도착 문이 열리고 Player가 걸어 나온 뒤 문은 닫히고 흐려진다(장식).
///   도착 자리가 없거나 마지막 Wave면 Wave 전환까지 닫힌 문 안에 있다가 다시 보이고 기존 크레인 경로를 탄다.
/// 시간은 게임 시간(성적 시계에 포함 — 구조 그대로). 물리 질의 없음(걸어 나올 자리 확인은 기존 모터 질의 IsBodyBlockedAt), 위치는 구속 영역 옮기기·기존 순간이동 경로로만 바꾼다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonCutsceneDirector : MonoBehaviour
{
    public enum Kind { None, LeverReveal, ExitEntry }
    public enum ArrivalPlan { None, Aligned, Blackout }
    public enum Step { Idle, LeverWatch, CoverIn, Preview, CoverOut, Return, Intake, Closing, Blackout, Waiting, ArrivalWait, ArrivalOpen, WalkOut }

    // 다음 던전 시작(FloorStarted)을 기다리는 최대 시간(초, 판단값 — Wave 종료 대기 1.2초 + 지형 패널 최대 약 3초보다 넉넉히).
    private const float ArrivalWaitLimit = 20f;
    // 문 실물 높이의 절반(5×6 자리 가운데 → 바닥) — 자리 가운데에서 바닥까지 3칸.
    private const float SlotHalfHeight = 3f;
    // 바닥에서 몸통 중심까지(PipePassage·DungeonManager와 같은 값).
    private const float BodyAboveFeet = .755f;

    private static int playing;
    /// <summary>던전 컷씬이 하나라도 진행 중인지(UI가 난이도 알림 등을 컷씬 뒤로 미룰 때 읽는다 — 읽기 전용).</summary>
    public static bool AnyPlaying => playing > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => playing = 0;

    private DungeonManager dungeon;
    private WaveManager waves;
    private WavePlayerStart hold;
    private CinematicLetterbox.Lease letterbox;
    private bool ownsBlock, hudHidden, busy, counted, fading, previewing;
    private readonly List<(Renderer renderer, bool off)> hiddenPlayer = new();
    private readonly List<(Canvas canvas, bool on)> hiddenCanvases = new();
    private Transform focus;
    private bool followChanged;
    private DungeonPassageOverlay overlay;
    private Coroutine routine;
    private PropExitGate exitGate;
    private DungeonRoomStage.DetachedPart detached;
    private int exitWave = -1, nextWave = -1;
    private bool teleportChanged, savedTeleport;
    private bool arrivalReady;
    private float floorStartedAt = -1f;
    private int boltTotal;
    private Vector2 arrivalBodyLocal;
    private int standSide = 1;

    public Kind Current { get; private set; }
    public Step CurrentStep { get; private set; }
    public bool IsPlaying => Current != Kind.None;
    /// <summary>던전 진행(방 이동 감지·시설 사용)을 막아야 하는지 — 컷씬이 도는 동안 true.</summary>
    public bool BlocksDungeon => Current != Kind.None;
    /// <summary>마지막 문 컷씬의 도착 방식(검사용).</summary>
    public ArrivalPlan LastPlan { get; private set; }
    /// <summary>끝까지 마친 레버 컷씬·문 닫힘·문 도착 수(검사용).</summary>
    public int LeverReveals { get; private set; }
    public int ExitClosures { get; private set; }
    public int Arrivals { get; private set; }
    /// <summary>끊긴 수와 마지막 까닭(검사용).</summary>
    public int Aborts { get; private set; }
    public string LastAbort { get; private set; }
    /// <summary>마지막 컷씬 길이(게임 초, 검사·성적 기준 재측정용).</summary>
    public float LastDuration { get; private set; }
    /// <summary>지금 Player를 숨기고 있는지.</summary>
    public bool PlayerHidden => hiddenPlayer.Count > 0;
    /// <summary>이 감독의 보호 구속(검사용).</summary>
    public bool Holding => hold != null && hold.IsHoldingPlayer;
    /// <summary>진행 중인 문 컷씬의 출구 문·도착 문(검사용, 없으면 null).</summary>
    public PropExitGate ExitGate => exitGate;
    public PropExitGate ArrivalGate { get; private set; }
    /// <summary>카메라 초점(컷씬이 Follow로 쓰는 빈 지점, 검사용).</summary>
    public Transform Focus => focus;

    /// <summary>DungeonManager가 Configure에서 부른다. 던전 시작(FloorStarted)을 들어 도착 문을 맞춘다.</summary>
    public void Bind(DungeonManager manager, WaveManager waveManager)
    {
        if (dungeon != null) dungeon.FloorStarted -= OnFloorStarted;
        dungeon = manager;
        waves = waveManager;
        if (dungeon != null) dungeon.FloorStarted += OnFloorStarted;
    }

    private void OnDisable() => Abort("비활성화");

    private void OnDestroy()
    {
        Abort("파괴");
        if (dungeon != null) dungeon.FloorStarted -= OnFloorStarted;
        if (overlay != null) Destroy(overlay.gameObject);
        if (focus != null) Destroy(focus.gameObject);
    }

    // ── 레버 컷씬 ──

    /// <summary>
    /// 레버(목표)가 받아들여진 직후 DungeonManager.TryCompleteObjective가 부른다. 규칙에서 켜져 있고, 시작 방 출구를 쓰는 던전이며 지금 방이 출구 방이 아니면
    /// 출구 방 미리보기 컷씬을 시작하고 true(그러면 부르는 쪽은 PushExit 지연 재생을 하지 않는다 — 문 진행은 감독이 보이고 기록한다).
    /// </summary>
    public bool TryPlayLeverReveal(IDungeonObjectivePoint point)
    {
        var rules = DungeonRulesSO.Default;
        if (rules == null || !rules.leverCutscene || IsPlaying || dungeon == null || !isActiveAndEnabled) return false;
        var floor = dungeon.Floor;
        var objectives = dungeon.Objectives;
        if (floor == null || !objectives.UsesExit || objectives.ExitEntered || dungeon.CurrentRoom == objectives.ExitRoom) return false;
        var start = floor.Room(objectives.ExitRoom);
        var player = Player();
        if (start == null || !start.HasExit || player == null || player.Motor == null) return false;
        int leverRoom = dungeon.CurrentRoom;
        Current = Kind.LeverReveal;
        routine = StartCoroutine(LeverRevealRoutine(leverRoom, start.Id));
        // 첫 프레임에서 실패해 원복됐으면(Current = None) 부르는 쪽이 지연 재생으로 대신한다.
        return Current == Kind.LeverReveal;
    }

    private IEnumerator LeverRevealRoutine(int leverRoom, int startRoom)
    {
        float began = Time.time;
        bool finished = false;
        var rules = DungeonRulesSO.Default;
        try
        {
            var player = Player();
            Begin(Kind.LeverReveal, player);
            // 1) 레버 당김(손잡이 .6초 + 빗장)을 보여 준다. 카메라는 Player 그대로.
            CurrentStep = Step.LeverWatch;
            for (float t = 0f; t < rules.leverWatchTime; t += Time.deltaTime)
            {
                if (LeverBroken()) yield break;
                yield return null;
            }
            // 2) 덮기(던전 지도에서 시작 방 쪽으로 흐르는 관 속).
            int direction = GridDirection(leverRoom, startRoom);
            CurrentStep = Step.CoverIn;
            StartCover(rules, direction);
            for (float t = 0f; t < rules.cutsceneCoverTime; t += Time.deltaTime)
            {
                if (LeverBroken()) yield break;
                DriveCover(Ease(t / rules.cutsceneCoverTime), 0f);
                yield return null;
            }
            DriveCover(1f, 0f);
            // 3) 덮인 동안: Player 숨김 → 시작 방 미리보기(레버 방은 보류) → 카메라를 출구 문 앞으로 → 문에 지금 진행을 밀어 넣는다(풀림 연출 시작).
            HidePlayer(player);
            previewing = dungeon.BeginExitPreview();
            var exit = previewing && dungeon.Stage != null ? dungeon.Stage.Exit : null;
            var gate = exit as PropExitGate;
            var objectives = dungeon.Objectives;
            int done = objectives.Done, total = objectives.Total;
            bool unlocked = objectives.ExitUnlocked;
            if (exit is Component gateComponent && gateComponent != null)
            {
                FocusCamera(gateComponent.transform.position + Vector3.down * SlotHalfHeight);
                try { exit.SetProgress(done, total, unlocked, false); }
                catch (System.Exception e) { Debug.LogException(e, this); exit = null; }
            }
            // 4) 걷기(문 풀림 대기 시간과 겹친다).
            CurrentStep = Step.Preview;
            StartReveal(rules);
            for (float t = 0f; t < rules.cutsceneRevealTime; t += Time.deltaTime)
            {
                if (LeverBroken()) yield break;
                DriveCover(1f, Ease(t / rules.cutsceneRevealTime));
                yield return null;
            }
            HideCover(false);
            // 5) 문 연출(볼트 → 마지막이면 봉인·문짝)이 끝날 때까지 → 지연 재생 기록 → 머묾.
            if (exit != null)
            {
                for (float t = 0f; t < rules.exitRevealLimit && gate != null && gate.Sequencing; t += Time.deltaTime)
                {
                    if (LeverBroken()) yield break;
                    yield return null;
                }
                objectives.MarkExitShown(done, unlocked);
                for (float t = 0f; t < rules.exitRevealHold; t += Time.deltaTime)
                {
                    if (LeverBroken()) yield break;
                    yield return null;
                }
            }
            // 6) 덮기(반대 방향) → 레버 방 복원 → 카메라·Player 원복 → 걷기.
            CurrentStep = Step.CoverOut;
            StartCover(rules, -direction);
            for (float t = 0f; t < rules.cutsceneCoverTime; t += Time.deltaTime)
            {
                if (LeverBroken()) yield break;
                DriveCover(Ease(t / rules.cutsceneCoverTime), 0f);
                yield return null;
            }
            DriveCover(1f, 0f);
            if (previewing) { previewing = false; dungeon.EndExitPreview(); }
            RestoreCamera();
            ShowPlayer();
            CurrentStep = Step.Return;
            StartReveal(rules);
            for (float t = 0f; t < rules.cutsceneRevealTime; t += Time.deltaTime)
            {
                if (LeverBroken()) yield break;
                DriveCover(1f, Ease(t / rules.cutsceneRevealTime));
                yield return null;
            }
            HideCover(false);
            LeverReveals++;
            finished = true;
        }
        finally
        {
            LastDuration = Time.time - began;
            Cleanup(finished ? null : "레버 컷씬 끊김");
        }
    }

    // 레버 컷씬을 멈춰야 하는지: 던전이 끝났거나 패배로 멈춤(Wave 정지·Player 사망).
    private bool LeverBroken() => dungeon == null || dungeon.Floor == null || dungeon.IsHalted || PlayerGone();

    // ── 문 컷씬 ──

    /// <summary>
    /// 출구 입장이 받아들여진 직후 DungeonManager.TryEnterExit가 부른다(문 실물의 흡입이 시작되기 전). 규칙에서 켜져 있으면 문 컷씬을 시작한다.
    /// 그 자리에서 SetFacilityBusy로 Wave 종료(FloorClearRoutine)를 문이 닫힐 때까지 미룬다.
    /// </summary>
    public void PlayExitEntry(IDungeonExitPoint exit)
    {
        var rules = DungeonRulesSO.Default;
        if (rules == null || !rules.exitCutscene || dungeon == null || !isActiveAndEnabled) return;
        var gate = exit as PropExitGate;
        var player = Player();
        if (gate == null || player == null || player.Motor == null) return;
        if (IsPlaying) Abort("출구 입장");
        Current = Kind.ExitEntry;
        routine = StartCoroutine(ExitEntryRoutine(gate));
    }

    private IEnumerator ExitEntryRoutine(PropExitGate gate)
    {
        float began = Time.time;
        bool finished = false;
        var rules = DungeonRulesSO.Default;
        try
        {
            var player = Player();
            exitGate = gate;
            exitWave = waves != null ? waves.currentWaveIndex : -1;
            nextWave = exitWave + 1;
            boltTotal = Mathf.Max(0, gate.Total);
            Begin(Kind.ExitEntry, player);
            // 1) 흡입: 문 실물(상호작용 경로)이 하면 다음 프레임부터 Entering — 끝나기를 기다린다. 아니면(접점을 바로 부른 경우) 같은 식으로 직접 한다.
            CurrentStep = Step.Intake;
            yield return null;
            if (gate.CurrentState == PropExitGate.State.Entering)
            {
                for (float t = 0f; t < gate.IntakeDuration + .5f && gate.CurrentState == PropExitGate.State.Entering; t += Time.deltaTime)
                {
                    if (EntryBroken()) yield break;
                    yield return null;
                }
            }
            else if (player != null && player.Motor != null && player.Motor.IsConfined)
            {
                var tick = new WaitForFixedUpdate();
                var motor = player.Motor;
                float start = motor.ConfinedBody.center.x, end = gate.transform.position.x, last = start;
                for (float time = 0f; time < gate.IntakeDuration;)
                {
                    yield return tick;
                    if (EntryBroken() || !motor.IsConfined) yield break;
                    time += Time.fixedDeltaTime;
                    float k = Mathf.Clamp01(time / gate.IntakeDuration);
                    float x = Mathf.Lerp(start, end, k * k);
                    motor.ShiftConfinement(new Vector2(x - last, 0f));
                    last = x;
                }
            }
            // 2) 문틀·문짝을 Player 앞에 그리고 문을 닫는다 → 문짝이 맞물리면 Player를 숨긴다 → 봉인 원위치 → Sealed(불 켜짐).
            CurrentStep = Step.Closing;
            gate.SetLeavesFront(true);
            if (!gate.PlayCloseAfterEntry()) yield break;
            for (float t = 0f; t < gate.LeafDuration + gate.SealReturnDuration + 1f && !gate.IsSealed; t += Time.deltaTime)
            {
                if (EntryBroken()) yield break;
                if (!PlayerHidden && gate.LeafOpen <= .02f) HidePlayer(player);
                yield return null;
            }
            HidePlayer(player);
            gate.SetLeavesFront(false);
            ExitClosures++;
            // 3) 다음 Wave 도착 방식.
            LastPlan = PlanArrival(rules);
            if (LastPlan == ArrivalPlan.None)
            {
                // 크레인 경로(다음 Wave가 없거나 그 시작 방에 도착 자리가 없음): Wave가 넘어가면(구속은 WaveManager 준비가 이어받는다) 다시 보이고 끝낸다.
                CurrentStep = Step.Waiting;
                SetBusy(false);
                for (float t = 0f; waves != null && waves.currentWaveIndex == exitWave && t < ArrivalWaitLimit; t += Time.deltaTime)
                {
                    if (EntryBroken()) yield break;
                    yield return null;
                }
                finished = true;
                yield break;
            }
            // 다음 Wave 실행 사본은 크레인 없이(teleportAtStart = false — WavePlayerStart.BeginCarry가 바로 돌아간다). 재도전·새 입장은 DungeonRunBuilder가 되돌린다.
            var data = dungeon.Run.Data;
            savedTeleport = data.waves[nextWave].teleportAtStart;
            data.waves[nextWave].teleportAtStart = false;
            teleportChanged = true;
            // 닫힌 W1 문은 단계에서 떼어 화면에 둔다(Wave 종료의 Leave가 거두지 않게). 정렬안은 W2 문이 같은 자리에 선 프레임에 반납한다.
            detached = dungeon.Stage != null ? dungeon.Stage.DetachExit() : default;
            if (LastPlan == ArrivalPlan.Blackout)
            {
                // 암전안: 암전 동안 다음 던전 시작 방 도착 문 안쪽으로 옮긴다(아직 앞 Wave 지형이지만 보호 구속 중이라 떨어지지 않는다).
                CurrentStep = Step.Blackout;
                if (UIManager.Instance != null) { UIManager.Instance.StartScreenFade(1f, rules.arrivalFadeTime); fading = true; }
                for (float t = 0f; t < rules.arrivalFadeTime; t += Time.deltaTime)
                {
                    if (EntryBroken()) yield break;
                    yield return null;
                }
                WarpBody(player, (Vector2)dungeon.OriginWorld + arrivalBodyLocal);
                ReleaseDetached(true);
            }
            // 4) Wave 종료를 허락한다 → CompleteWave → 지형 패널(크레인 없음) → 다음 던전 시작(OnFloorStarted가 도착 문을 불 켠 채 맞춘다).
            CurrentStep = Step.Waiting;
            arrivalReady = false;
            SetBusy(false);
            for (float t = 0f; !arrivalReady; t += Time.deltaTime)
            {
                if (EntryBroken() || t > ArrivalWaitLimit) yield break;
                yield return null;
            }
            // 5) 도착: 'WAVE n' 판 끝 0.3초 전(시작 안내 + arrivalOpenDelay)에 문이 열린다. 암전안이면 먼저 걷는다.
            CurrentStep = Step.ArrivalWait;
            if (fading && UIManager.Instance != null) { UIManager.Instance.StartScreenFade(0f, rules.arrivalFadeTime); fading = false; }
            while (Time.time < floorStartedAt + rules.arrivalOpenDelay)
            {
                if (EntryBroken()) yield break;
                yield return null;
            }
            CurrentStep = Step.ArrivalOpen;
            var arrival = ArrivalGate;
            if (arrival != null)
            {
                arrival.SetLeavesFront(true);
                ShowPlayer();
                arrival.PlayArrivalOpen();
                for (float t = 0f; t < arrival.LeafDuration + .5f && arrival.CurrentState == PropExitGate.State.ArrivalOpening; t += Time.deltaTime)
                {
                    if (EntryBroken()) yield break;
                    yield return null;
                }
                arrival.SetLeavesFront(false);
            }
            else ShowPlayer();
            // 6) 걸어 나온다(구속 영역을 옮겨 몸통을 문 가운데 → 방 안쪽 선 자리로, 감속).
            CurrentStep = Step.WalkOut;
            if (player != null && player.Motor != null && player.Motor.IsConfined)
            {
                var tick = new WaitForFixedUpdate();
                var motor = player.Motor;
                Vector2 start = motor.ConfinedBody.center;
                int side = standSide;
                Vector2 end = start + new Vector2(side * rules.arrivalStandDistance, 0f), last = start;
                for (float time = 0f; time < rules.arrivalWalkTime;)
                {
                    yield return tick;
                    if (EntryBroken() || !motor.IsConfined) yield break;
                    time += Time.fixedDeltaTime;
                    float k = Mathf.Clamp01(time / rules.arrivalWalkTime);
                    Vector2 point = Vector2.LerpUnclamped(start, end, 1f - (1f - k) * (1f - k));
                    motor.ShiftConfinement(point - last);
                    last = point;
                    Face(player, side);
                }
                yield return tick;
            }
            // 걸어 나온 뒤 문은 닫히고 흐려진다(장식 — Player를 붙잡지 않는다).
            if (arrival != null) arrival.PlayCloseBehind();
            Arrivals++;
            finished = true;
        }
        finally
        {
            LastDuration = Time.time - began;
            Cleanup(finished ? null : "문 컷씬 끊김");
        }
    }

    // 문 컷씬을 멈춰야 하는지: Wave 사이(IsWaveActive 거짓)에도 흐르므로 Wave 정지가 아니라 Player 사망·패배 화면·감독 연결 끊김으로 본다.
    private bool EntryBroken()
    {
        if (dungeon == null || waves == null || !waves.isActiveAndEnabled || PlayerGone()) return true;
        var ui = UIManager.Instance;
        return ui != null && ui.IsDeathUIOpen;
    }

    // 다음 Wave 도착 방식: 다음 Wave가 던전이고 그 시작 방에 도착 자리가 있으면 문 도착 — 출구 자리와 같은 방 좌표·회전이면 정렬안, 아니면 암전안.
    private ArrivalPlan PlanArrival(DungeonRulesSO rules)
    {
        var run = dungeon.Run;
        var floor = dungeon.Floor;
        if (run == null || run.Data == null || run.Data.waves == null || floor == null || waves == null) return ArrivalPlan.None;
        if (nextWave <= 0 || nextWave >= run.Data.waves.Length || run.Data.waves[nextWave] == null) return ArrivalPlan.None;
        var nextFloor = run.FloorFor(nextWave);
        var start = nextFloor != null ? nextFloor.Room(nextFloor.layout.startRoom) : null;
        if (start == null || !start.HasArrival) return ArrivalPlan.None;
        Vector2 center = start.arrival.spawnPosition;
        arrivalBodyLocal = new Vector2(center.x, center.y - SlotHalfHeight + BodyAboveFeet);
        var exitRoom = floor.Room(dungeon.Objectives.ExitRoom);
        bool aligned = exitRoom != null && exitRoom.HasExit && (exitRoom.exit.spawnPosition - center).sqrMagnitude < .0001f
            && (((exitRoom.exit.rotationSteps % 4) + 4) % 4) == (((start.arrival.rotationSteps % 4) + 4) % 4);
        return aligned ? ArrivalPlan.Aligned : ArrivalPlan.Blackout;
    }

    // 다음 던전을 세운 직후(BeginFloor의 FloorStarted, WaveIntroStarted와 같은 프레임): 도착 문을 '닫히고 불 켜진 문'으로 맞추고,
    // '시작 위치 복귀' 목적지를 걸어 나와 설 자리로 바꾸고, 정렬안이면 떼어 둔 W1 문을 같은 프레임에 반납한다(같은 자리·같은 모습이라 바뀐 것이 보이지 않는다).
    private void OnFloorStarted()
    {
        if (Current != Kind.ExitEntry || CurrentStep != Step.Waiting || LastPlan == ArrivalPlan.None || dungeon == null) return;
        var floor = dungeon.Floor;
        if (floor == null || floor.waveIndex != nextWave) return;
        var rules = DungeonRulesSO.Default;
        var gate = dungeon.Stage != null ? dungeon.Stage.Arrival : null;
        if (gate != null)
        {
            try { gate.SetArrivalSealed(true, true, boltTotal); }
            catch (System.Exception e) { Debug.LogException(e, this); gate = null; }
        }
        ArrivalGate = gate;
        var player = Player();
        standSide = StandSide(player, gate, rules);
        float distance = rules != null ? rules.arrivalStandDistance : 2.2f;
        // '시작 위치 복귀'(함정·추격 분쇄기의 WavePlayerReturn, 끊긴 뒤 SettleIfBuried)는 이 좌표에 Player Transform(기준점)을 그대로 놓는다
        // (WavePlayerStart.Relocate → PlayerController.ResetPlayerPosition). Player 기준점은 몸통 중심(루트 Collider offset 0)이라 발(바닥 윗면)을 적으면
        // 복귀한 몸이 바닥에 약 0.755칸 묻힌다. 걸어 나와 설 몸통 중심에서 '몸통 중심 − 기준점'을 빼 그 자리의 Transform 위치를 적는다(fix6d).
        Vector2 standBody = arrivalBodyLocal + new Vector2(standSide * distance, 0f);
        dungeon.OverrideWaveStart(standBody - BodyOffset(player));
        ReleaseDetached(true);
        // 카메라는 Player(문 안)를 따른다 — 방이 바뀌었으니 보간 없이 바로 맞춘다.
        InvalidateCamera(RoomCamera());
        floorStartedAt = Time.time;
        arrivalReady = true;
    }

    // 걸어 나올 쪽(+1 오른쪽 / -1 왼쪽): 방 실내 가운데 쪽을 먼저, 그 자리가 막혀 있으면(기존 모터 질의) 반대쪽.
    private int StandSide(PlayerController player, PropExitGate gate, DungeonRulesSO rules)
    {
        var floor = dungeon != null ? dungeon.Floor : null;
        var room = floor != null ? floor.Room(floor.layout.startRoom) : null;
        float origin = dungeon != null ? dungeon.OriginWorld.x : 0f;
        float doorX = gate != null ? gate.transform.position.x : origin + arrivalBodyLocal.x;
        float centerX = room != null ? origin + room.interior.center.x : doorX + 1f;
        int side = centerX >= doorX ? 1 : -1;
        float distance = rules != null ? rules.arrivalStandDistance : 2.2f;
        var motor = player != null ? player.Motor : null;
        if (motor == null) return side;
        // 몸통이 문 가운데에 있다고 보고 옆 자리를 묻는다(지금 위치 기준 이동량).
        float bodyX = motor.IsConfined ? motor.ConfinedBody.center.x : player.transform.position.x;
        float toDoor = doorX - bodyX;
        if (motor.IsBodyBlockedAt(new Vector2(toDoor + side * distance, 0f)) && !motor.IsBodyBlockedAt(new Vector2(toDoor - side * distance, 0f))) side = -side;
        return side;
    }

    // ── 끊기 ──

    /// <summary>진행 중인 컷씬을 끊고 원복한다(재도전·퇴장·비활성화 — DungeonManager가 부른다). 진행 중이 아니면 아무것도 하지 않는다.</summary>
    public void Abort(string reason)
    {
        if (Current == Kind.None && routine == null) return;
        if (routine != null) StopCoroutine(routine);
        routine = null;
        Cleanup(string.IsNullOrEmpty(reason) ? "중단" : reason);
    }

    // ── 공통 기반 ──

    private void Begin(Kind kind, PlayerController player)
    {
        Current = kind;
        if (!counted) { counted = true; playing++; }
        var rules = DungeonRulesSO.Default;
        var ui = UIManager.Instance;
        if (ui != null && !ownsBlock)
        {
            ui.PushGameplayBlock();
            ui.PauseInputBlocked = true;
            ownsBlock = true;
        }
        if (ui != null && !hudHidden)
        {
            ui.HUDVisible(false);
            hudHidden = true;
        }
        if (letterbox == null && rules != null && rules.letterboxPrefab != null)
        {
            try { letterbox = CinematicLetterbox.Acquire(rules.letterboxPrefab, this); }
            catch (System.Exception e) { Debug.LogException(e, this); letterbox = null; }
        }
        HoldPlayer(player);
        SetBusy(true);
    }

    // 원복(끝·끊김 공통, 여러 번 불러도 된다). reason이 있으면 끊긴 것이다.
    private void Cleanup(string reason)
    {
        routine = null;
        bool aborted = reason != null && Current != Kind.None;
        if (aborted) { Aborts++; LastAbort = reason; }
        if (previewing)
        {
            previewing = false;
            try { if (dungeon != null) dungeon.EndExitPreview(); }
            catch (System.Exception e) { Debug.LogException(e, this); }
        }
        if (exitGate != null) exitGate.SetLeavesFront(false);
        if (ArrivalGate != null) ArrivalGate.SetLeavesFront(false);
        HideCover(true);
        RestoreCamera();
        ShowPlayer();
        ReleaseDetached(true);
        // 끊긴 뒤 다음 Wave 준비가 아직 시작되지 않았으면 크레인 생략을 되돌린다(재도전은 DungeonRunBuilder가 어차피 되돌린다).
        if (teleportChanged && aborted && waves != null && waves.currentWaveIndex <= exitWave && dungeon != null && dungeon.Run != null && dungeon.Run.Data != null
            && nextWave >= 0 && nextWave < dungeon.Run.Data.waves.Length)
            dungeon.Run.Data.waves[nextWave].teleportAtStart = savedTeleport;
        teleportChanged = false;
        SetBusy(false);
        var player = Player();
        ReleaseHold();
        if (aborted) SettleIfBuried(player);
        var ui = UIManager.Instance;
        if (ownsBlock)
        {
            ownsBlock = false;
            if (ui != null)
            {
                ui.PopGameplayBlock();
                ui.PauseInputBlocked = false;
            }
        }
        if (hudHidden)
        {
            hudHidden = false;
            if (ui != null) ui.HUDVisible(true);
        }
        if (letterbox != null)
        {
            letterbox.Dispose();
            letterbox = null;
        }
        if (counted) { counted = false; playing = Mathf.Max(0, playing - 1); }
        Current = Kind.None;
        CurrentStep = Step.Idle;
        exitGate = null;
        ArrivalGate = null;
        arrivalReady = false;
        floorStartedAt = -1f;
    }

    private void SetBusy(bool on)
    {
        if (busy == on) return;
        busy = on;
        if (dungeon != null) dungeon.SetFacilityBusy(this, on);
    }

    // 감독 전용 holder(자식 GameObject의 WavePlayerStart — WaveManager·문 실물 holder와 따로)로 보호 구속한다. 대시·눌러 둔 입력은 지운다.
    private void HoldPlayer(PlayerController player)
    {
        if (player == null || player.Motor == null) return;
        if (hold == null)
        {
            var child = transform.Find("DungeonCutsceneHold");
            if (child == null)
            {
                child = new GameObject("DungeonCutsceneHold").transform;
                child.SetParent(transform, false);
            }
            if (!child.TryGetComponent(out hold)) hold = child.gameObject.AddComponent<WavePlayerStart>();
        }
        if (hold.IsHoldingPlayer) return;
        var abilities = player.AbilityManager;
        if (abilities != null)
        {
            if (abilities.DashAbility != null) { abilities.DashAbility.Interrupt(); abilities.DashAbility.ClearPendingInput(); }
            if (abilities.JumpAbility != null) abilities.JumpAbility.ClearPendingInput();
        }
        hold.HoldForPassage(true);
        player.Velocity = Vector2.zero;
        player.externalForce = Vector2.zero;
    }

    private void ReleaseHold()
    {
        if (hold != null && hold.IsHoldingPlayer) hold.Release();
    }

    // 끊긴 뒤 Player 몸이 지형 속이면(미리보기·문 안·암전 이동 도중) 지금 Wave 시작 좌표로 옮긴다(기존 순간이동 경로, WavePlayerStart.SettleInterrupted와 같은 규칙).
    private void SettleIfBuried(PlayerController player)
    {
        if (player == null || player.Motor == null || player.Motor.IsConfined || waves == null || waves.Data == null || waves.Data.waves == null) return;
        if (!player.Motor.IsBodyBlockedAt(Vector2.zero)) return;
        int index = Mathf.Clamp(waves.currentWaveIndex, 0, waves.Data.waves.Length - 1);
        var coordinates = dungeon != null && dungeon.Terrain != null ? dungeon.Terrain.transform : transform;
        WavePlayerStart.Relocate(player, coordinates.TransformPoint(WaveRoomState.PlayerStartAt(waves.Data, index)), false);
        InvalidateCamera(RoomCamera());
    }

    // Player 아래 Renderer·월드 Canvas를 숨긴다(원래 값을 적어 두고 ShowPlayer가 되돌린다). Player.prefab은 고치지 않는다.
    private void HidePlayer(PlayerController player)
    {
        if (player == null || hiddenPlayer.Count > 0) return;
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            hiddenPlayer.Add((r, r.forceRenderingOff));
            r.forceRenderingOff = true;
        }
        foreach (var canvas in player.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas == null) continue;
            hiddenCanvases.Add((canvas, canvas.enabled));
            canvas.enabled = false;
        }
    }

    private void ShowPlayer()
    {
        foreach (var (r, off) in hiddenPlayer) if (r != null) r.forceRenderingOff = off;
        foreach (var (canvas, on) in hiddenCanvases) if (canvas != null) canvas.enabled = on;
        hiddenPlayer.Clear();
        hiddenCanvases.Clear();
    }

    private void ReleaseDetached(bool immediate)
    {
        if (!detached.IsValid) return;
        var part = detached;
        detached = default;
        try { part.Release(immediate); }
        catch (System.Exception e) { Debug.LogException(e, this); }
    }

    // ── 카메라 ──

    private CinemachineCamera RoomCamera()
    {
        var room = dungeon != null ? dungeon.GetComponentInParent<Room>() : null;
        if (room == null && RoomManager.Instance != null) room = RoomManager.Instance.CurrentRoom;
        return room != null ? room.VirtualCamera : null;
    }

    // 카메라 Follow를 빈 지점(point = 그 자리에 Player가 서 있는 것처럼 — 발 높이)으로 바꾸고 보간 없이 바로 맞춘다. 렌즈는 바꾸지 않는다.
    private void FocusCamera(Vector3 point)
    {
        var camera = RoomCamera();
        if (camera == null) return;
        if (focus == null)
        {
            focus = new GameObject("DungeonCutsceneFocus").transform;
            focus.SetParent(transform, false);
        }
        focus.position = point;
        camera.Follow = focus;
        followChanged = true;
        InvalidateCamera(camera);
    }

    private void RestoreCamera()
    {
        if (!followChanged) return;
        followChanged = false;
        var camera = RoomCamera();
        var player = Player();
        if (camera != null && focus != null && camera.Follow == focus && player != null) camera.Follow = player.transform;
        InvalidateCamera(camera);
    }

    private static void InvalidateCamera(CinemachineCamera camera)
    {
        if (camera != null) camera.PreviousStateIsValid = false;
        var view = Camera.main;
        if (view != null && view.TryGetComponent(out CinemachineBrain brain) && brain.ActiveVirtualCamera is CinemachineVirtualCameraBase active)
            active.PreviousStateIsValid = false;
    }

    // ── 덮개(관 속 오버레이 또는 암전) ──

    private void StartCover(DungeonRulesSO rules, int direction)
    {
        if (rules != null && rules.cutsceneCover == DungeonCutsceneCover.Fade)
        {
            if (UIManager.Instance != null) { UIManager.Instance.StartScreenFade(1f, rules.cutsceneCoverTime); fading = true; }
            return;
        }
        var screen = Overlay(rules);
        if (screen != null) screen.Begin(new Vector2(direction, 0f));
    }

    private void StartReveal(DungeonRulesSO rules)
    {
        if (!fading || rules == null || rules.cutsceneCover != DungeonCutsceneCover.Fade) return;
        if (UIManager.Instance != null) UIManager.Instance.StartScreenFade(0f, rules.cutsceneRevealTime);
        fading = false;
    }

    private void DriveCover(float head, float tail)
    {
        if (overlay != null && (head > 0f || overlay.IsShowing)) overlay.SetCoverage(head, tail);
    }

    // 덮개를 걷는다. restoreFade면(끊김) 덮어 둔 암전도 걷는다.
    private void HideCover(bool restoreFade)
    {
        if (overlay != null) overlay.Hide();
        if (restoreFade && fading)
        {
            fading = false;
            if (UIManager.Instance != null) UIManager.Instance.StartScreenFade(0f, .2f);
        }
    }

    private DungeonPassageOverlay Overlay(DungeonRulesSO rules)
    {
        if (overlay != null) return overlay;
        var prefab = rules != null ? rules.passageOverlayPrefab : null;
        var instance = DungeonUiPlacement.Instantiate(prefab, DungeonUiPlacement.Slot.BeforeFade);
        if (instance == null) return null;
        if (!instance.TryGetComponent(out overlay))
        {
            Destroy(instance);
            overlay = null;
            return null;
        }
        overlay.Hide();
        return overlay;
    }

    // ── 도우미 ──

    private static PlayerController Player() => GameManager.Instance != null ? GameManager.Instance.Player : null;

    private static bool PlayerGone()
    {
        var player = Player();
        return player == null || !player.isActiveAndEnabled || (player.Health != null && player.Health.IsDead);
    }

    // 던전 지도에서 from 방 → to 방 가로 방향(+1 오른쪽, 같으면 +1).
    private int GridDirection(int from, int to)
    {
        var layout = dungeon != null && dungeon.Floor != null ? dungeon.Floor.layout : null;
        var a = layout != null ? layout.Room(from) : null;
        var b = layout != null ? layout.Room(to) : null;
        int dx = a != null && b != null ? b.gridOrigin.x - a.gridOrigin.x : 0;
        return dx < 0 ? -1 : 1;
    }

    // 몸통 중심이 body(월드)에 오도록 기존 순간이동 경로(WavePlayerStart.Relocate — 구속 영역도 함께 옮겨진다)로 옮기고 카메라를 바로 맞춘다.
    private void WarpBody(PlayerController player, Vector2 body)
    {
        if (player == null) return;
        var motor = player.Motor;
        Vector2 center = motor != null && motor.IsConfined ? motor.ConfinedBody.center : (Vector2)player.transform.position + Vector2.up * BodyAboveFeet;
        Vector2 offset = center - (Vector2)player.transform.position;
        WavePlayerStart.Relocate(player, new Vector3(body.x - offset.x, body.y - offset.y, player.transform.position.z), false);
        InvalidateCamera(RoomCamera());
    }

    // 몸통 중심 − Transform(Player 기준점). 구속 중이면 모터 몸통(WarpBody와 같은 계산), 아니면 Collider offset(물리 질의 없음),
    // Player가 없으면 예전 가정(기준점 = 발)과 같은 값을 돌려준다.
    private static Vector2 BodyOffset(PlayerController player)
    {
        if (player == null) return Vector2.up * BodyAboveFeet;
        Vector2 origin = player.transform.position;
        // 몸통 오프셋은 Player 자신의 Collider(기준점 대비 위치)로 잰다. 구속 상자 중심은 쓰지 않는다 — 던전이 바뀌는 순간(OnFloorStarted)에는
        // 구속 상자가 앞 던전 자리에 남아 있어 Transform과 수십 칸 어긋날 수 있다(dPlay F2: 시작 위치가 20칸 위로 적힘, fix6h).
        if (player.TryGetComponent(out Collider2D body)) return (Vector2)body.transform.TransformPoint(body.offset) - origin;
        return Vector2.up * BodyAboveFeet;
    }

    // Player가 sign 쪽을 보게 한다(마우스 조준 중이면 그대로 — PipePassageEffectSO와 같은 규칙).
    private static void Face(PlayerController player, float sign)
    {
        if (player == null || Mathf.Abs(sign) < .01f || player.UsesMouseAim || player.LockFacing) return;
        int facing = sign > 0f ? 1 : -1;
        player.FacingUpdate(facing);
        if (player.facingModel != null) player.facingModel.transform.localScale = new Vector3(facing, 1f, 1f);
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
