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

    [SerializeField] private NightmareIntroSequence intro;
    [SerializeField, Tooltip("단말기 안내(InventoryTerminal 안의 Onboarding)")] private LobbyOnboarding onboarding;
    [SerializeField] private StationUnlockEvent[] unlockEvents = Array.Empty<StationUnlockEvent>();

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

    private Coroutine lobbyRoutine;
    private Transform focusTarget;
    private bool ownsBlock;
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

    // 이미 끝난 해금은 시설을 바로 가동 상태로 둔다(연출 없이).
    private void ApplySavedUnlocks()
    {
        foreach (var unlock in unlockEvents)
            if (unlock != null && unlock.unlock != null && unlock.unlock.Station != null && IsCompleted(unlock.id))
                unlock.unlock.Station.SetPowered(true);
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
                yield return PlayUnlockRoutine(id, true);
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
        if (!CanStartEvent || Find(id) == null) return false;
        StartCoroutine(PlayUnlockRoutine(id, record));
        return true;
    }

    [ContextMenu("시험: 업그레이드 해금 연출")]
    private void TestUnlockUpgrade() => TriggerUnlock(GameEventId.UnlockUpgrade);
    [ContextMenu("시험: 자율훈련장 해금 연출")]
    private void TestUnlockSelfTraining() => TriggerUnlock(GameEventId.UnlockSelfTraining);

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
