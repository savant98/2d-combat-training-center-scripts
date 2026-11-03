using System.Collections;
using UnityEngine;

// 고지서 Intro 직후의 일회성 연출 전투만 담당한다. 일반 Nightmare·훈련 재도전과 분리한다.
[DisallowMultipleComponent]
public sealed class NightmareIntroSequence : MonoBehaviour
{
    public enum SequenceStage { Dormant, Entering, Exploring, Shattering, BossEntry, Fighting, Defeat, Guidance, MovingToTutorial, Completed }

    [SerializeField] private Room finalRoom;
    [SerializeField] private Transform finalSpawnPoint;
    [SerializeField] private Room tutorialRoom;
    [SerializeField] private Transform tutorialSpawnPoint;
    [SerializeField] private EnemyController nightmarePrefab;
    [SerializeField] private NightmareDefeatTerminal terminal;
    [SerializeField] private NightmareBackdrop backdrop;
    [SerializeField] private NightmareScreenEffects screenEffects;
    [SerializeField] private NightmareArrivalTransitionSO arrivalTransition;

    private static bool startRequested;
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
    public Room TutorialRoom => tutorialRoom;
    public NightmareDefeatTerminal Terminal => terminal;
    public NightmareScreenEffects ScreenEffects => screenEffects;
    public NightmareBackdrop Backdrop => backdrop;
    public bool IsEncounterActive => CurrentStage >= SequenceStage.Entering && CurrentStage <= SequenceStage.Fighting;

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
        if (finalRoom == null || finalRoom.WaveManager == null || finalSpawnPoint == null
            || tutorialRoom == null || tutorialSpawnPoint == null || nightmarePrefab == null || terminal == null
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
        terminal.ContinueRequested += ContinueToTutorial;
        subscribed = true;
        CurrentStage = SequenceStage.Entering;
        RoomTransitionManager.Instance.RequestTransition(new RoomTransitionRequest {
            targetRoom = finalRoom, teleportPoint = backdrop.EntrySpawnPoint, transitionEffect = arrivalTransition
        });
        return true;
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
            || CurrentStage == SequenceStage.MovingToTutorial) return true;
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

    private void ContinueToTutorial()
    {
        if (CurrentStage != SequenceStage.Guidance && CurrentStage != SequenceStage.Defeat) return;
        CurrentStage = SequenceStage.MovingToTutorial;
        StartCoroutine(MoveToTutorialRoutine());
    }

    private IEnumerator MoveToTutorialRoutine()
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
        RoomTransitionManager.Instance.RequestTransition(new RoomTransitionRequest {
            targetRoom = tutorialRoom, teleportPoint = tutorialSpawnPoint, transitionEffect = spawnTransition
        });
        while (RoomManager.Instance.CurrentRoom != tutorialRoom) yield return null;

        // 새 방의 기존 암전이 걷히기 시작할 때 단말기와 전용 검은 배경을 함께 닫는다.
        terminal.Dismiss();
        while (RoomTransitionManager.Instance.IsTransitioning) yield return null;
        ReleaseEncounter();
        CurrentStage = SequenceStage.Completed;
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
            if (terminal != null) terminal.ContinueRequested -= ContinueToTutorial;
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
