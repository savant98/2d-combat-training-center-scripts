using System.Collections;
using UnityEngine;

// 고지서 Intro 직후의 일회성 연출 전투만 담당한다. 일반 Nightmare·훈련 재도전과 분리한다.
// 패배한 뒤에는 Lobby로 보낸다(2026-11-14: 옛 Tutorial 방은 쓰지 않는다). 그때 세이브의 최초 시작 표시를 지우고,
// 그다음 안내(단말기 메일 → 훈련 메뉴)는 Lobby 쪽 안내가 세이브의 진행 단계를 보고 이어받는다.
// 2026-11-15: 가로등길 앞에 '가짜 Lobby'를 둔다. 도착하면 Lobby와 똑같은 곳(설비 없이 지형·배경만)이고, 오른쪽으로 8칸 가면
// 노이즈와 함께 "경고: 경로를 이탈했습니다. 즉시 복귀하세요."(입장 불가 창 형식)가 뜨며, 창을 닫으면 노이즈와 함께 가로등길로 바뀐다.
// 이 흐름의 시작·순서는 GameEventManager가 총괄한다(이 컴포넌트는 실행자다).
// 2026-11-26: 사용자 요청으로 이 도입부는 기본으로 꺼 둔다(GameEventManager.IsFirstStartIntroEnabled). 연출 코드·에셋·MainScene 배치는 지우지 않으며,
// 꺼져 있으면 GameEventManager가 TryBegin 대신 SkipFirstStart를 불러 정리만 하고 진짜 Lobby에서 시작한다(TryBegin을 직접 부르는 회귀는 그대로 연출을 건다).
[DisallowMultipleComponent]
public sealed class NightmareIntroSequence : MonoBehaviour
{
    public enum SequenceStage { Dormant, DisguisedLobby, RouteWarning, Entering, Exploring, Shattering, BossEntry, Fighting, Defeat, Guidance, MovingToLobby, Completed }

    [SerializeField] private Room finalRoom;
    [SerializeField] private Transform finalSpawnPoint;
    [SerializeField] private EnemyController nightmarePrefab;
    [SerializeField] private NightmareDefeatTerminal terminal;
    [SerializeField] private NightmareBackdrop backdrop;
    [SerializeField] private NightmareScreenEffects screenEffects;
    [SerializeField] private NightmareArrivalTransitionSO arrivalTransition;

    [Header("가짜 Lobby(2026-11-15)")]
    [SerializeField, Tooltip("도입부 처음에 위장해 쓸 Lobby. 비우면 예전처럼 바로 가로등길로 간다")] private LobbyDisguise lobbyDisguise;
    [SerializeField, Tooltip("경로 이탈 경고 창(Lobby 입장 불가 창과 같은 Prefab)")] private GameObject routeWarningPopupPrefab;
    [SerializeField, Tooltip("경고 창이 뜰 때의 소리")] private FeedbackDataSO routeWarningFeedback;
    [SerializeField, Min(1f), Tooltip("시작 지점에서 오른쪽으로 이만큼(칸) 가면 경고")] private float routeWarningDistance = 8f;
    [SerializeField] private string routeWarningTitle = "경고";
    [SerializeField, TextArea] private string routeWarningMessage = "경로를 이탈했습니다.\n즉시 복귀하세요.";
    [SerializeField, Tooltip("경고 창의 Canvas 정렬. 노이즈 화면(1900)보다 위에 그린다")] private int routeWarningSortingOrder = 1950;

    private static bool startRequested;
    private float disguiseStartX;
    private Coroutine routeWarningRoutine;
    private GameObject routePopup;
    private EnemyController nightmare;
    private RoomTransitionEffectSO spawnTransition;
    private GameTimeController pauseOwner;
    private UIManager inputOwner;
    private bool ownsPause, ownsGameplayBlock, subscribed, previousRecordingSuppression;
    private bool previousWaveSuppression;
    private Coroutine revealRoutine;
    private System.IDisposable defeatSfxScope;

    public SequenceStage CurrentStage { get; private set; }
    public EnemyController Nightmare => nightmare;
    public Room FinalRoom => finalRoom;
    public NightmareDefeatTerminal Terminal => terminal;
    public NightmareScreenEffects ScreenEffects => screenEffects;
    public NightmareBackdrop Backdrop => backdrop;
    public bool IsEncounterActive => CurrentStage >= SequenceStage.Entering && CurrentStage <= SequenceStage.Fighting;
    /// <summary>가짜 Lobby에 있거나 경로 이탈 경고를 띄운 단계인가.</summary>
    public bool IsDisguisePhase => CurrentStage == SequenceStage.DisguisedLobby || CurrentStage == SequenceStage.RouteWarning;
    public LobbyDisguise LobbyDisguise => lobbyDisguise;
    public float RouteWarningX => disguiseStartX + routeWarningDistance;
    public GameObject RouteWarningPopup => routePopup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => startRequested = false;

    /// <summary>이번 Title에서 실제로 Intro를 재생한 경우만 다음 MainScene에 한 번 전달한다.</summary>
    public static void QueueNextStart(bool introPlayed) => startRequested = introPlayed;

    /// <summary>다음 MainScene이 이 연출로 시작될 예정인지 알려 준다. Title BGM을 FinalRoom까지 이어 갈지 정할 때 쓴다.</summary>
    public static bool IsStartQueued => startRequested;

    public bool TryBegin(RoomTransitionEffectSO transition)
    {
        bool requested = startRequested;
        startRequested = false;
        if (!requested) return false;
        if (!isActiveAndEnabled) { FadeOutCarriedTitleMusic(.5f); return false; }
        var game = GameManager.Instance;
        if (finalRoom == null || finalRoom.WaveManager == null || finalSpawnPoint == null
            || game == null || game.LobbyRoom == null || game.LobbySpawnPoint == null || nightmarePrefab == null || terminal == null
            || backdrop == null || screenEffects == null || arrivalTransition == null)
        {
            Debug.LogError("Nightmare Intro의 방·등장 위치·단말기 참조가 누락되었습니다.", this);
            FadeOutCarriedTitleMusic(.5f);
            return false;
        }

        spawnTransition = transition;
        nightmare = null;
        previousRecordingSuppression = finalRoom.WaveManager.SuppressTrainingRecording;
        previousWaveSuppression = finalRoom.SuppressAutomaticWaves;
        finalRoom.WaveManager.SuppressTrainingRecording = true;
        finalRoom.SuppressAutomaticWaves = true;
        backdrop.PrepareForIntro(GameManager.Instance.Player.transform);
        finalRoom.WaveManager.EnemySpawned += HandleEnemySpawned;
        RoomManager.Instance.OnRoomChanged += HandleRoomChanged;
        terminal.ContinueRequested += ContinueToLobby;
        subscribed = true;
        if (lobbyDisguise != null)
        {
            // 먼저 Lobby와 똑같이 생긴 곳(위장한 실제 Lobby)에 도착한다.
            lobbyDisguise.SetDisguised(true);
            disguiseStartX = game.LobbySpawnPoint.position.x;
            CurrentStage = SequenceStage.DisguisedLobby;
            RoomTransitionManager.Instance.RequestTransition(new RoomTransitionRequest {
                targetRoom = game.LobbyRoom, teleportPoint = game.LobbySpawnPoint, transitionEffect = arrivalTransition
            });
            return true;
        }
        CurrentStage = SequenceStage.Entering;
        RoomTransitionManager.Instance.RequestTransition(new RoomTransitionRequest {
            targetRoom = finalRoom, teleportPoint = backdrop.EntrySpawnPoint, transitionEffect = arrivalTransition
        });
        return true;
    }

    /// <summary>
    /// 도입부를 꺼 두었을 때(2026-11-26) MainScene이 시작되면 GameEventManager가 TryBegin 대신 부른다. 연출은 시작하지 않고(단계는 Dormant 그대로),
    /// 도입부를 마쳤을 때 하던 정리만 한다: 예약을 소비하고, Title에서 멀리 들리게 이어 온 곡을 줄이고, 세이브의 최초 시작 표시를 지운다.
    /// 그 뒤 GameManager가 기존 세이브처럼 진짜 Lobby로 보내고, Lobby 안내(단말기 → 메일 → 훈련 메뉴)가 이어진다.
    /// </summary>
    public void SkipFirstStart()
    {
        // 예약이 남으면 IsStartQueued가 계속 true다.
        startRequested = false;
        // '시작하지 못함' 경로와 같은 처리다. Lobby에 들어가면 Lobby BGM이 이어서 재생된다(위장하지 않았으니 RoomBgmPlayer가 켜져 있다).
        FadeOutCarriedTitleMusic(.5f);
        // 예약 여부와 상관없이 최초 시작 표시가 남은 세이브면 지운다(편집기에서 MainScene을 바로 연 경우, 도입부 도중에 끈 옛 세이브 포함).
        // 지우지 않으면 Lobby 안내가 시작되지 않는다(LobbyOnboarding.Step은 최초 시작 세이브를 '끝남'으로 본다).
        var data = DataManager.Instance;
        if (data != null && data.HasSelectedSlot && data.CurrentSaveData.isFirstStart) data.CompleteFirstStart();
    }

    // 가짜 Lobby에서 오른쪽으로 걸어가면: 화면이 지직거리며 경고 창 → 닫으면 노이즈와 함께 가로등길로.
    private IEnumerator RouteWarningRoutine()
    {
        AcquireGameplayBlock();
        var player = GameManager.Instance.Player;
        player.Velocity = new Vector2(0f, player.Velocity.y);
        screenEffects.BeginStatic(.6f);
        float t = 0f;
        while (t < .45f)
        {
            t += Time.unscaledDeltaTime;
            screenEffects.SetSignal(.6f + .3f * Mathf.Sin(t * 40f), .5f);
            yield return null;
        }

        bool closed = false;
        UpgradePurchaseDialog dialog = null;
        if (routeWarningPopupPrefab != null)
        {
            routePopup = Instantiate(routeWarningPopupPrefab);
            var canvas = routePopup.GetComponent<Canvas>();
            if (canvas != null) canvas.sortingOrder = routeWarningSortingOrder;   // 노이즈 화면 위에
            dialog = routePopup.GetComponentInChildren<UpgradePurchaseDialog>(true);
            yield return null;   // 창의 Awake가 끝난 뒤 띄운다
        }
        if (dialog == null || !dialog.ShowWarning(routeWarningMessage, () => closed = true, routeWarningTitle)) closed = true;
        else if (routeWarningFeedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(routeWarningFeedback, player.transform);

        while (!closed && CurrentStage == SequenceStage.RouteWarning)
        {
            // 창이 떠 있는 동안 노이즈가 가늘게 흔들리고 가끔 크게 튄다(표시용이라 전역 난수를 쓰지 않는다).
            float time = Time.unscaledTime;
            float burst = Mathf.PerlinNoise(time * 7f, 3.1f) > .78f ? .35f : 0f;
            screenEffects.SetSignal(.26f + .14f * Mathf.PerlinNoise(time * 3f, .5f) + burst, .3f + burst * .6f);
            yield return null;
        }
        if (routePopup != null) Destroy(routePopup);
        routePopup = null;
        routeWarningRoutine = null;
        if (CurrentStage != SequenceStage.RouteWarning) yield break;

        // 창을 닫으면 노이즈가 차오르며 가로등길로 바뀐다(전환 효과가 같은 노이즈를 이어받는다).
        screenEffects.SetSignal(1f, .6f);
        // 전환이 끝날 때 모든 입력 막기를 지우므로 그 전에 내 것을 돌려준다.
        ReleaseGameplayBlock();
        CurrentStage = SequenceStage.Entering;
        RoomTransitionManager.Instance.RequestTransition(new RoomTransitionRequest {
            targetRoom = finalRoom, teleportPoint = backdrop.EntrySpawnPoint, transitionEffect = arrivalTransition
        });
    }

    private void HandleEnemySpawned(EnemyController enemy)
    {
        if (!IsEncounterActive || enemy == null || enemy.Poolkey != nightmarePrefab) return;
        nightmare = enemy;
        nightmare.Health.SetMinimumHealth(1f);
        CurrentStage = SequenceStage.Fighting;
    }

    private void Update()
    {
        if (CurrentStage == SequenceStage.DisguisedLobby && GameManager.Instance != null && RoomManager.Instance.CurrentRoom == GameManager.Instance.LobbyRoom
            && !RoomTransitionManager.Instance.IsTransitioning && GameManager.Instance.Player.Health.currentHP > 0
            && GameManager.Instance.Player.transform.position.x >= RouteWarningX)
        {
            CurrentStage = SequenceStage.RouteWarning;
            routeWarningRoutine = StartCoroutine(RouteWarningRoutine());
        }
        if (CurrentStage == SequenceStage.Entering && RoomManager.Instance.CurrentRoom == finalRoom
            && !RoomTransitionManager.Instance.IsTransitioning)
            CurrentStage = SequenceStage.Exploring;
        if (CurrentStage != SequenceStage.Exploring || GameManager.Instance.Player.Health.currentHP <= 0) return;
        if (GameManager.Instance.Player.transform.position.x >= backdrop.RevealWorldX)
        {
            CurrentStage = SequenceStage.Shattering;
            revealRoutine = StartCoroutine(RevealRoutine());
        }
    }

    private IEnumerator RevealRoutine()
    {
        AcquireGameplayBlock();
        screenEffects.BeginStatic();
        yield return backdrop.Shatter(t => screenEffects.SetSignal(Mathf.Sin(t * Mathf.PI) * .65f, .65f));
        if (CurrentStage != SequenceStage.Shattering) yield break;
        screenEffects.End();
        ReleaseGameplayBlock();
        CurrentStage = SequenceStage.BossEntry;
        finalRoom.WaveManager.BeginWaves(false);
    }

    private void HandleRoomChanged(RoomType roomType)
    {
        var current = RoomManager.Instance.CurrentRoom;
        // 가짜 Lobby를 떠나면 Lobby를 원래 모습으로 되돌린다(가로등길로 넘어간 경우 포함).
        var lobby = GameManager.Instance != null ? GameManager.Instance.LobbyRoom : null;
        if (lobbyDisguise != null && lobbyDisguise.IsDisguised && current != lobby)
            lobbyDisguise.SetDisguised(false);
        if (IsDisguisePhase && current != lobby)
        {
            ReleaseEncounter();
            CurrentStage = SequenceStage.Completed;
            return;
        }
        if (IsEncounterActive && current != finalRoom)
        {
            ReleaseEncounter();
            CurrentStage = SequenceStage.Completed;
        }
    }

    public bool TryHandlePlayerDeath()
    {
        if (!isActiveAndEnabled) return false;
        if (CurrentStage == SequenceStage.Defeat || CurrentStage == SequenceStage.Guidance
            || CurrentStage == SequenceStage.MovingToLobby) return true;
        if (!IsEncounterActive || RoomManager.Instance.CurrentRoom != finalRoom) return false;
        CurrentStage = SequenceStage.Defeat;
        if (nightmare != null && nightmare.TryGetComponent<EnemySpawnController>(out var musicOwner))
            musicOwner.StopBossMusic();
        // 보스 음악이 시작되기 전에 쓰러지면 이어 온 Title BGM도 이명 연출 전에 멈춘다.
        FadeOutCarriedTitleMusic(0f);
        // TakeDamage 뒤 같은 호출 스택에서 실행되는 Feedback도 이 시점부터 차단한다.
        if (SoundManager.Instance != null)
        {
            defeatSfxScope = SoundManager.Instance.SuppressSfxExcept(screenEffects.TinnitusClip);
            SoundManager.Instance.StopAllSfx();
        }
        if (revealRoutine != null) { StopCoroutine(revealRoutine); revealRoutine = null; }
        backdrop.CancelShatter();
        StartCoroutine(DefeatRoutine());
        return true;
    }

    private IEnumerator DefeatRoutine()
    {
        AcquireGameplayBlock();
        inputOwner.HUDVisible(false);
        inputOwner.ShowBossHP(false);
        finalRoom.WaveManager.ForceClear();
        EnemyPool.Instance.FreezeAll();
        WorldEntityPool.Instance.DespawnAll();
        pauseOwner = GameTimeController.Instance;
        if (pauseOwner != null) { pauseOwner.PushPause(); ownsPause = true; }

        // 이명과 흐림을 먼저 보여준 뒤 단말기 확인을 기다린다. Pause 중에도 진행한다.
        yield return screenEffects.PlayDefeat();
        if (CurrentStage != SequenceStage.Defeat) yield break;
        yield return terminal.ShowRoutine();
        if (CurrentStage != SequenceStage.Defeat) yield break;
        screenEffects.End();
        EnemyPool.Instance.DespawnAll();
        CurrentStage = SequenceStage.Guidance;
    }

    private void ContinueToLobby()
    {
        if (CurrentStage != SequenceStage.Guidance && CurrentStage != SequenceStage.Defeat) return;
        CurrentStage = SequenceStage.MovingToLobby;
        StartCoroutine(MoveToLobbyRoutine());
    }

    private IEnumerator MoveToLobbyRoutine()
    {
        EnemyPool.Instance.DespawnAll();
        ReleasePause();
        var player = GameManager.Instance.Player;
        player.AbilityManager.InterruptAllAbilities();
        player.Velocity = Vector2.zero;
        player.externalForce = Vector2.zero;
        player.ResetPlayerStatus();
        inputOwner.HUDVisible(true);

        // 이전 방 전환이 끝나기 전에 요청을 보내어 무시되는 경우를 방지한다.
        while (RoomTransitionManager.Instance.IsTransitioning) yield return null;
        ReleaseGameplayBlock();
        Room lobby = GameManager.Instance.LobbyRoom;
        RoomTransitionManager.Instance.RequestTransition(new RoomTransitionRequest {
            targetRoom = lobby, teleportPoint = GameManager.Instance.LobbySpawnPoint, transitionEffect = spawnTransition
        });
        while (RoomManager.Instance.CurrentRoom != lobby) yield return null;

        // 새 방의 기존 암전이 걷히기 시작할 때 단말기와 전용 검은 배경을 함께 닫는다.
        terminal.Dismiss();
        while (RoomTransitionManager.Instance.IsTransitioning) yield return null;
        ReleaseEncounter();
        CurrentStage = SequenceStage.Completed;
        // 도입부를 마쳤다. 다음 접속부터는 Lobby에서 시작한다(Lobby 안내의 진행 단계는 따로 남는다).
        if (DataManager.Instance != null) DataManager.Instance.CompleteFirstStart();
    }

    private void ReleasePause()
    {
        if (ownsPause && pauseOwner != null) pauseOwner.PopPause();
        ownsPause = false;
        pauseOwner = null;
    }

    private void ReleaseGameplayBlock()
    {
        if (ownsGameplayBlock && inputOwner != null) inputOwner.PopGameplayBlock();
        ownsGameplayBlock = false;
    }

    private void AcquireGameplayBlock()
    {
        if (ownsGameplayBlock) return;
        inputOwner = UIManager.Instance;
        inputOwner.PushGameplayBlock();
        ownsGameplayBlock = true;
    }

    private void ReleaseEncounter()
    {
        if (routeWarningRoutine != null) { StopCoroutine(routeWarningRoutine); routeWarningRoutine = null; }
        if (routePopup != null) { Destroy(routePopup); routePopup = null; }
        if (lobbyDisguise != null && lobbyDisguise.IsDisguised) lobbyDisguise.SetDisguised(false);
        if (nightmare != null) nightmare.Health.SetMinimumHealth(0f);
        if (subscribed)
        {
            if (finalRoom != null) finalRoom.SuppressAutomaticWaves = previousWaveSuppression;
            if (finalRoom != null && finalRoom.WaveManager != null)
            {
                finalRoom.WaveManager.SuppressTrainingRecording = previousRecordingSuppression;
                finalRoom.WaveManager.EnemySpawned -= HandleEnemySpawned;
            }
            if (RoomManager.Instance != null) RoomManager.Instance.OnRoomChanged -= HandleRoomChanged;
            if (terminal != null) terminal.ContinueRequested -= ContinueToLobby;
        }
        subscribed = false;
        ReleasePause();
        ReleaseGameplayBlock();
        if (screenEffects != null) screenEffects.End();
        defeatSfxScope?.Dispose();
        defeatSfxScope = null;
        if (backdrop != null) backdrop.ResetPresentation();
        FadeOutCarriedTitleMusic(.5f);
    }

    // Title에서 멀리 들리게 이어 온 곡은 보스 음악이 시작되면 그 곡으로 바뀐다.
    // 그 전에 연출이 끝나거나 시작하지 못하면 다른 방까지 끌고 가지 않는다.
    private static void FadeOutCarriedTitleMusic(float duration)
    {
        var sound = SoundManager.Instance;
        if (sound != null && sound.IsBgmDistant) sound.FadeOutBGM(sound.CurrentBgmClip, duration);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (CurrentStage != SequenceStage.Dormant && terminal != null) terminal.Dismiss();
        if (ownsGameplayBlock && inputOwner != null) inputOwner.HUDVisible(true);
        ReleaseEncounter();
        CurrentStage = SequenceStage.Dormant;
    }
}
