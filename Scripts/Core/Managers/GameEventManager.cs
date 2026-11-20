using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>게임 진행 이벤트 이름. 세이브의 완료 기록(SaveData.completedEvents)에는 이 이름이 문자열로 남는다.</summary>
public enum GameEventId
{
    IntroFinalRoom,      // 최초 시작: 가짜 Lobby → 경로 이탈 경고 → 가로등길 → Nightmare → 패배 → Lobby
    TerminalTutorial,    // Lobby 도착 뒤: 단말기 열기 → 메시지 수신함의 입소 환영 메일 확인 → 훈련 메뉴
    UnlockUpgrade,       // 업그레이드 시설 해금(전기가 차올라 다시 가동)
    UnlockSelfTraining,  // 자율훈련장 해금(부품이 채워져 가동)
    UnlockFieldElevator, // 각개 전투 엘리베이터 등장(앞선 훈련을 모두 수료하고 Lobby로 돌아오면). Scene에는 정수로 남으므로 새 값은 끝에만 더한다
}

/// <summary>
/// 게임 진행 이벤트를 한곳에서 총괄한다(2026-11-15, 사용자 요청 "게임 이벤트 시스템을 총괄 관리하는 매니저 스크립트.
/// intro에서 finalRoom으로 가고, 휴대용 단말기를 열게 하는 Tutorial도 이벤트에 포함").
/// - 실행자는 그대로 둔다: 최초 시작 도입부는 NightmareIntroSequence, 단말기 안내는 LobbyOnboarding, 시설 해금 연출은 LobbyStationUnlock.
///   이 매니저는 어떤 이벤트를 언제 시작할지, 끝났는지를 정하고 기록한다.
/// - 시설 해금: 지정한 훈련을 지정한 등급 이상으로 수료하고 Lobby로 돌아오면 그 시설의 해금 이벤트가 따로따로 차례로 재생된다.
///   카메라가 시설 쪽으로 옮겨 가 해금 Animation과 소리를 보여 주고 돌아온다. 조건(requiredTrainingId)이 비어 있으면 자동으로 일어나지 않는다
///   (사용자: "추후 특정 훈련을 통과 이상으로 클리어하고 Lobby로 복귀하면 발생시킬 거야" — 지금은 조건 자리만 두고 시험 실행 진입점을 둔다).
/// - 이미 끝난 해금은 세이브에 남고, 다음 접속에서 시설이 바로 가동 상태로 선다.
/// - 각개 전투 엘리베이터(2026-11-20): 1~7 훈련을 모두 수료하고 Lobby로 돌아오면 천장 해치가 열리고 엘리베이터가 내려오는 연출(FieldElevator)을 한 번 재생한다.
///   시설 해금과 실행자·조건이 달라 elevatorEvent 칸에 따로 둔다. 연출 동안 Lobby 카메라의 렌즈를 넓혀 천장 해치와 위층 바닥을 한 화면에 담는다.
/// MainScene의 GameManager 개체에 붙는다. UI는 읽기 전용 상태만 본다.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameEventManager : MonoBehaviour
{
    [Serializable]
    public sealed class StationUnlockEvent
    {
        public GameEventId id = GameEventId.UnlockUpgrade;
        [Tooltip("해금 연출을 맡는 시설(LobbyStationUnlock)")] public LobbyStationUnlock unlock;
        [Tooltip("이 훈련(trainingId)을 아래 등급 이상으로 수료하고 Lobby로 돌아오면 발생한다. 비우면 자동으로 발생하지 않는다")] public string requiredTrainingId;
        public Grade minimumGrade = Grade.Pass;
    }

    /// <summary>각개 전투 엘리베이터 등장 이벤트. 실행자·조건이 시설 해금과 달라 따로 둔다(시설 해금 칸의 형식은 도구·회귀가 읽으므로 바꾸지 않는다).</summary>
    [Serializable]
    public sealed class ElevatorRevealEvent
    {
        public GameEventId id = GameEventId.UnlockFieldElevator;
        [Tooltip("천장 해치가 열리고 케이지가 내려오는 연출을 맡는 Lobby 엘리베이터")] public FieldElevator elevator;
        [Tooltip("훈련 목록(TrainingCatalogSO) 진행 사슬에서 이 훈련보다 앞선 훈련을 모두 수료하고 Lobby로 돌아오면 발생한다. 비우면 자동으로 발생하지 않는다")]
        public string beforeTrainingId = "FieldCombat";
        [Min(1f), Tooltip("연출 동안 Lobby 카메라의 Orthographic Size. 천장 해치와 위층 바닥을 한 화면에 담는다")] public float cameraSize = 9f;
    }

    [SerializeField] private NightmareIntroSequence intro;
    [SerializeField, Tooltip("단말기 안내(InventoryTerminal 안의 Onboarding)")] private LobbyOnboarding onboarding;
    [SerializeField] private StationUnlockEvent[] unlockEvents = Array.Empty<StationUnlockEvent>();
    [SerializeField, Tooltip("각개 전투 엘리베이터 등장(시설 해금과 따로 줄을 서서 그 뒤에 재생된다)")] private ElevatorRevealEvent elevatorEvent = new ElevatorRevealEvent();

    [Header("해금 연출의 카메라")]
    [SerializeField, Min(0f), Tooltip("Lobby에 도착한 뒤 이벤트를 시작하기까지 기다리는 시간")] private float lobbySettleDelay = .8f;
    [SerializeField, Min(0f), Tooltip("카메라가 시설로 옮겨 간 뒤 연출을 시작하기까지")] private float cameraArriveTime = 1.2f;
    [SerializeField, Min(0f), Tooltip("연출이 끝나고 카메라가 Player에게 돌아오기 전 머무는 시간")] private float cameraHoldTime = .6f;
    [SerializeField, Tooltip("카메라가 비출 자리 = 시설 위치 + 이 값")] private Vector2 focusOffset = new Vector2(0f, 1.2f);

    public static GameEventManager Instance { get; private set; }
    /// <summary>지금 진행 중인 매니저 이벤트(해금 연출). 없으면 null.</summary>
    public GameEventId? CurrentEvent { get; private set; }
    public bool IsEventRunning => CurrentEvent.HasValue;
    public NightmareIntroSequence Intro => intro;
    public LobbyOnboarding Onboarding => onboarding;
    public IReadOnlyList<StationUnlockEvent> UnlockEvents => unlockEvents;
    public ElevatorRevealEvent ElevatorEvent => elevatorEvent;

    private Coroutine lobbyRoutine;
    private Transform focusTarget;
    private bool ownsBlock;
    // 엘리베이터 연출이 넓히기 전의 Lobby 렌즈 크기(넓히지 않았으면 -1)
    private float savedLobbyLens = -1f;
    private readonly Queue<GameEventId> pending = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        if (intro == null) intro = GetComponent<NightmareIntroSequence>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (RoomManager.Instance != null) RoomManager.Instance.OnRoomChanged -= HandleRoomChanged;
    }

    private void Start()
    {
        if (RoomManager.Instance != null) RoomManager.Instance.OnRoomChanged += HandleRoomChanged;
        ApplySavedUnlocks();
    }

    private void OnDisable()
    {
        if (lobbyRoutine != null) { StopCoroutine(lobbyRoutine); lobbyRoutine = null; }
        EndCameraFocus();
        RestoreLobbyLens();
        ReleaseBlock();
        CurrentEvent = null;
    }

    // ===================== 최초 시작 =====================

    /// <summary>MainScene이 시작될 때 GameManager가 부른다. 최초 시작 도입부를 시작했으면 true(그러면 Lobby로 바로 보내지 않는다).</summary>
    public bool TryStartGame(RoomTransitionEffectSO transition)
    {
        return intro != null && intro.TryBegin(transition);
    }

    // ===================== 상태 =====================

    /// <summary>이벤트가 이미 끝났는가. 도입부는 세이브의 최초 시작 표시, 단말기 안내는 안내 단계, 해금은 완료 기록을 본다.</summary>
    public bool IsCompleted(GameEventId id)
    {
        var data = DataManager.Instance;
        if (data == null || !data.HasSelectedSlot) return false;
        return id switch
        {
            GameEventId.IntroFinalRoom => !data.CurrentSaveData.isFirstStart,
            GameEventId.TerminalTutorial => !data.CurrentSaveData.isFirstStart && data.CurrentSaveData.onboardingStep >= SaveData.OnboardingDone,
            _ => data.HasCompletedEvent(id.ToString()),
        };
    }

    /// <summary>지금 다른 이벤트를 시작해도 되는가: 도입부·단말기 안내·전환·다른 연출이 없고 Player가 살아 있을 때.</summary>
    public bool CanStartEvent
    {
        get
        {
            var game = GameManager.Instance;
            if (game == null || game.Player == null || game.Player.Health.IsDead) return false;
            if (IsEventRunning || LobbyDisguise.IsActive) return false;
            if (intro != null && intro.CurrentStage != NightmareIntroSequence.SequenceStage.Dormant && intro.CurrentStage != NightmareIntroSequence.SequenceStage.Completed) return false;
            if (onboarding != null && (onboarding.IsFocusing || onboarding.Step < SaveData.OnboardingDone)) return false;
            if (RoomTransitionManager.Instance == null || RoomTransitionManager.Instance.IsTransitioning) return false;
            if (RoomManager.Instance == null || RoomManager.Instance.CurrentRoom != game.LobbyRoom) return false;
            return UIManager.Instance != null && !UIManager.Instance.HasActiveInputUI && !UIManager.Instance.BlocksGameplayInput;
        }
    }

    /// <summary>해금 이벤트의 조건(지정 훈련을 지정 등급 이상으로 수료)을 만족하는가.</summary>
    public static bool MeetsCondition(StationUnlockEvent unlock)
    {
        if (unlock == null || string.IsNullOrEmpty(unlock.requiredTrainingId) || DataManager.Instance == null) return false;
        var record = DataManager.Instance.GetTrainingRoomRecord(unlock.requiredTrainingId);
        return DataManager.IsCleared(record) && record.bestGrade >= unlock.minimumGrade;
    }

    /// <summary>엘리베이터 등장 조건(훈련 목록의 진행 사슬에서 beforeTrainingId보다 앞선 훈련을 모두 수료)을 만족하는가. 목록 에셋이 없으면 false.</summary>
    public static bool MeetsCondition(ElevatorRevealEvent reveal)
    {
        if (reveal == null || string.IsNullOrEmpty(reveal.beforeTrainingId) || DataManager.Instance == null) return false;
        var catalog = TrainingCatalogSO.Load();
        return catalog != null && catalog.AllProgressionCleared(reveal.beforeTrainingId, DataManager.Instance);
    }

    private bool IsElevator(GameEventId id) => elevatorEvent != null && elevatorEvent.elevator != null && id == elevatorEvent.id;

    // 이미 끝난 해금은 시설을 바로 가동 상태로 둔다(연출 없이).
    private void ApplySavedUnlocks()
    {
        foreach (var unlock in unlockEvents)
            if (unlock != null && unlock.unlock != null && unlock.unlock.Station != null && IsCompleted(unlock.id))
                unlock.unlock.Station.SetPowered(true);
        // 엘리베이터: 끝난 등장은 내려와 있는 상태로(연출 없이), 아직이면 해치가 닫히고 케이지가 없는 상태로 둔다.
        if (elevatorEvent != null && elevatorEvent.elevator != null && !elevatorEvent.elevator.IsPlaying)
            elevatorEvent.elevator.ApplyRevealed(IsCompleted(elevatorEvent.id));
    }

    // ===================== Lobby 도착 =====================

    private void HandleRoomChanged(RoomType type)
    {
        var game = GameManager.Instance;
        if (game == null || RoomManager.Instance.CurrentRoom != game.LobbyRoom) return;
        ApplySavedUnlocks();
        if (lobbyRoutine == null) lobbyRoutine = StartCoroutine(LobbyArrivalRoutine());
    }

    private IEnumerator LobbyArrivalRoutine()
    {
        try
        {
            // 전환이 끝나 입력 막기가 모두 풀린 뒤에 시작한다(전환 끝의 ClearGameplayBlocks가 우리 막기를 지우지 않게).
            while (RoomTransitionManager.Instance != null && RoomTransitionManager.Instance.IsTransitioning) yield return null;
            float wait = 0f;
            while (wait < lobbySettleDelay) { wait += Time.deltaTime; yield return null; }

            foreach (var unlock in unlockEvents)
                if (unlock != null && !IsCompleted(unlock.id) && MeetsCondition(unlock) && !pending.Contains(unlock.id))
                    pending.Enqueue(unlock.id);
            // 엘리베이터 등장은 시설 해금 뒤에 줄을 선다.
            if (elevatorEvent != null && elevatorEvent.elevator != null && !IsCompleted(elevatorEvent.id) && MeetsCondition(elevatorEvent) && !pending.Contains(elevatorEvent.id))
                pending.Enqueue(elevatorEvent.id);

            // 조건을 만족한 해금은 하나씩 따로 재생한다.
            while (pending.Count > 0)
            {
                while (!CanStartEvent)
                {
                    var game = GameManager.Instance;
                    if (game == null || RoomManager.Instance.CurrentRoom != game.LobbyRoom) { pending.Clear(); yield break; }
                    yield return null;
                }
                var id = pending.Dequeue();
                // 연출 루틴을 Unity의 중첩 코루틴으로 맡기지 않고 직접 돌린다. 연출 루틴 안에서 예외가 나도 그 루틴과 이 루틴의 finally
                // (입력 막기·카메라·렌즈 복구, lobbyRoutine 비우기)가 돌아 다음 Lobby 도착 이벤트가 막히지 않는다.
                for (var step = IsElevator(id) ? PlayElevatorRoutine(true) : PlayUnlockRoutine(id, true); step.MoveNext();) yield return step.Current;
            }
        }
        finally
        {
            lobbyRoutine = null;
        }
    }

    // ===================== 해금 연출 =====================

    /// <summary>시험용: 조건과 상관없이 해금 연출을 지금 재생한다(record = 끝나면 세이브에 남긴다).</summary>
    public bool TriggerUnlock(GameEventId id, bool record = false)
    {
        if (!CanStartEvent) return false;
        if (IsElevator(id)) { StartCoroutine(PlayElevatorRoutine(record)); return true; }
        if (Find(id) == null) return false;
        StartCoroutine(PlayUnlockRoutine(id, record));
        return true;
    }

    [ContextMenu("시험: 업그레이드 해금 연출")]
    private void TestUnlockUpgrade() => TriggerUnlock(GameEventId.UnlockUpgrade);
    [ContextMenu("시험: 자율훈련장 해금 연출")]
    private void TestUnlockSelfTraining() => TriggerUnlock(GameEventId.UnlockSelfTraining);
    [ContextMenu("시험: 각개 전투 엘리베이터 등장 연출")]
    private void TestRevealFieldElevator() => TriggerUnlock(GameEventId.UnlockFieldElevator);

    private StationUnlockEvent Find(GameEventId id)
    {
        foreach (var unlock in unlockEvents) if (unlock != null && unlock.id == id && unlock.unlock != null) return unlock;
        return null;
    }

    private IEnumerator PlayUnlockRoutine(GameEventId id, bool record)
    {
        var unlock = Find(id);
        if (unlock == null || unlock.unlock.Station == null || unlock.unlock.Station.IsPowered)
        {
            if (record && unlock != null && DataManager.Instance != null) DataManager.Instance.CompleteEvent(id.ToString());
            yield break;
        }
        CurrentEvent = id;
        AcquireBlock();
        try
        {
            var player = GameManager.Instance.Player;
            player.Velocity = new Vector2(0f, player.Velocity.y);
            BeginCameraFocus(unlock.unlock.Focus.position + (Vector3)focusOffset);
            float t = 0f;
            while (t < cameraArriveTime) { t += Time.deltaTime; yield return null; }

            yield return unlock.unlock.Play();
            if (record && DataManager.Instance != null) DataManager.Instance.CompleteEvent(id.ToString());

            t = 0f;
            while (t < cameraHoldTime) { t += Time.deltaTime; yield return null; }
            EndCameraFocus();
            t = 0f;
            while (t < cameraArriveTime * .8f) { t += Time.deltaTime; yield return null; }
        }
        finally
        {
            EndCameraFocus();
            ReleaseBlock();
            CurrentEvent = null;
        }
    }

    // 엘리베이터 등장 연출: 해금 연출과 같은 틀(입력 막기 → 카메라를 옮김 → 실행자 연출 → 기록 → 복귀)에 렌즈 넓히기만 더한다.
    // 실행자(FieldElevator)는 꺼지면 그 자리에서 등장 상태로 확정한다(멈춘 코루틴의 finally는 돌지 않는다).
    // 실행자 연출은 중첩 코루틴(yield return IEnumerator)으로 넘기지 않는다: 안쪽 예외는 바깥 finally를 실행하지 않아 입력 막기가 영구히 남는다.
    private IEnumerator PlayElevatorRoutine(bool record)
    {
        var reveal = elevatorEvent;
        var elevator = reveal != null ? reveal.elevator : null;
        if (elevator == null || elevator.IsRevealed || elevator.IsPlaying)
        {
            if (record && elevator != null && DataManager.Instance != null) DataManager.Instance.CompleteEvent(reveal.id.ToString());
            yield break;
        }
        CurrentEvent = reveal.id;
        AcquireBlock();
        try
        {
            var player = GameManager.Instance.Player;
            player.Velocity = new Vector2(0f, player.Velocity.y);
            BeginCameraFocus(FollowPointFor(elevator.Focus.position));
            float from = LobbyLens(), wide = Mathf.Max(1f, reveal.cameraSize);
            float t = 0f;
            while (t < cameraArriveTime)
            {
                t += Time.deltaTime;
                SetLobbyLens(Mathf.Lerp(from, wide, Ease(t / Mathf.Max(.0001f, cameraArriveTime))));
                yield return null;
            }
            SetLobbyLens(wide);

            // 실행자 연출도 직접 돌린다(PlayReveal은 null만 넘긴다). 안쪽에서 예외가 나면 아래 finally까지 올라와 입력 막기·렌즈·카메라를 되돌린다.
            for (var step = elevator.PlayReveal(); step.MoveNext();) yield return step.Current;
            if (record && DataManager.Instance != null) DataManager.Instance.CompleteEvent(reveal.id.ToString());

            t = 0f;
            while (t < cameraHoldTime) { t += Time.deltaTime; yield return null; }
            EndCameraFocus();
            float back = savedLobbyLens > 0f ? savedLobbyLens : from, duration = Mathf.Max(.0001f, cameraArriveTime * .8f);
            t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                SetLobbyLens(Mathf.Lerp(wide, back, Ease(t / duration)));
                yield return null;
            }
            RestoreLobbyLens();
        }
        finally
        {
            EndCameraFocus();
            RestoreLobbyLens();
            ReleaseBlock();
            CurrentEvent = null;
        }
    }

    // 0~1을 처음과 끝이 느린 곡선으로 바꾼다(Mathf.SmoothStep은 보간 함수라 쓰지 않는다).
    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // 카메라 중심이 center에 오게 하는 Follow 지점: Lobby 카메라의 FollowOffset만큼 뺀다(오프셋을 상수로 박지 않는다).
    private static Vector3 FollowPointFor(Vector3 center)
    {
        var lobby = GameManager.Instance != null ? GameManager.Instance.LobbyRoom : null;
        var follow = lobby != null && lobby.VirtualCamera != null ? lobby.VirtualCamera.GetComponent<CinemachineFollow>() : null;
        Vector3 offset = follow != null ? follow.FollowOffset : Vector3.zero;
        return new Vector3(center.x - offset.x, center.y - offset.y, center.z);
    }

    private static CinemachineCamera LobbyCamera()
    {
        var lobby = GameManager.Instance != null ? GameManager.Instance.LobbyRoom : null;
        return lobby != null ? lobby.VirtualCamera : null;
    }

    private static float LobbyLens()
    {
        var camera = LobbyCamera();
        return camera != null ? camera.Lens.OrthographicSize : 7f;
    }

    // Lobby 렌즈를 바꾼다(처음 바꿀 때 원래 크기를 적어 둔다). 범위 제한이 새 렌즈로 다시 계산되게 Confiner의 렌즈 캐시를 지운다(Room.SetCameraSize와 같다).
    private void SetLobbyLens(float size)
    {
        var camera = LobbyCamera();
        if (camera == null) return;
        if (savedLobbyLens < 0f) savedLobbyLens = camera.Lens.OrthographicSize;
        camera.Lens.OrthographicSize = size;
        var confiner = camera.GetComponent<CinemachineConfiner2D>();
        if (confiner != null) confiner.InvalidateLensCache();
    }

    private void RestoreLobbyLens()
    {
        if (savedLobbyLens < 0f) return;
        float size = savedLobbyLens;
        savedLobbyLens = -1f;
        var camera = LobbyCamera();
        if (camera == null) return;
        camera.Lens.OrthographicSize = size;
        var confiner = camera.GetComponent<CinemachineConfiner2D>();
        if (confiner != null) confiner.InvalidateLensCache();
    }

    // Lobby 카메라가 따라가는 대상을 시설 쪽 빈 지점으로 바꿔 부드럽게 옮겨 간다(카메라 감쇠 1초, 방 범위 제한은 그대로).
    private void BeginCameraFocus(Vector3 point)
    {
        var lobby = GameManager.Instance != null ? GameManager.Instance.LobbyRoom : null;
        if (lobby == null || lobby.VirtualCamera == null) return;
        if (focusTarget == null) focusTarget = new GameObject("EventCameraFocus").transform;
        focusTarget.position = point;
        lobby.VirtualCamera.Follow = focusTarget;
    }

    private void EndCameraFocus()
    {
        var game = GameManager.Instance;
        if (game == null || game.LobbyRoom == null || game.LobbyRoom.VirtualCamera == null || game.Player == null) return;
        if (focusTarget != null && game.LobbyRoom.VirtualCamera.Follow == focusTarget)
            game.LobbyRoom.VirtualCamera.Follow = game.Player.transform;
    }

    public Transform FocusTarget => focusTarget;

    private void AcquireBlock()
    {
        if (ownsBlock || UIManager.Instance == null) return;
        UIManager.Instance.PushGameplayBlock();
        UIManager.Instance.PauseInputBlocked = true;
        UIManager.Instance.HUDVisible(false);
        ownsBlock = true;
    }

    private void ReleaseBlock()
    {
        if (!ownsBlock) return;
        ownsBlock = false;
        if (UIManager.Instance == null) return;
        UIManager.Instance.PopGameplayBlock();
        UIManager.Instance.PauseInputBlocked = false;
        UIManager.Instance.HUDVisible(true);
    }
}
