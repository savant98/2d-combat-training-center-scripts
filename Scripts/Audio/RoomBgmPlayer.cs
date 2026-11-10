using UnityEngine;

// 방이 현재 방이 되면 지정한 BGM을 반복 재생하고, 음악이 없는 방으로 떠나면 줄여서 멈춘다.
// 음악이 있는 다른 방으로 옮길 때는 그 방의 플레이어가 교차 전환을 맡아 처리 순서에 영향받지 않는다.
// 보스 음악·Title에서 이어 온 곡처럼 다른 소유자가 재생한 곡은 멈추지 않는다.
// 이 방의 웨이브에 보스가 등장하면 공용 보스 BGM으로 바꾸고, 보스가 쓰러지거나 정리되면 방 BGM으로 돌아온다.
// 전용 BGM이 있는 보스(EnemySpawnController에 전주·메인이 지정된 Nightmare 등)는 자기 곡을 직접 재생하므로 건너뛴다.
[DisallowMultipleComponent]
public sealed class RoomBgmPlayer : MonoBehaviour
{
    [SerializeField] private AudioClip bgmClip;
    [SerializeField, Range(0f, 1f), Tooltip("기존 Title·Nightmare BGM과 같은 기준의 AudioSource 음량입니다.")]
    private float volume = .38f;
    [SerializeField, Min(0f), Tooltip("이전 곡을 줄이는 시간과 이 곡을 올리는 시간(초)입니다.")]
    private float fadeDuration = .8f;

    [Header("Boss")]
    [SerializeField, Tooltip("이 방에서 전용 BGM이 없는 보스가 등장하면 재생하는 공용 보스 BGM입니다.")]
    private AudioClip bossBgmClip;
    [SerializeField, Range(0f, 1f)] private float bossVolume = .38f;

    private Room room;
    private RoomManager subscribedManager;
    private WaveManager subscribedWaves;
    private EnemyController currentBoss;

    public AudioClip Clip => bgmClip;
    public float Volume => volume;
    public AudioClip BossClip => bossBgmClip;
    public bool HasMusic => enabled && bgmClip != null;
    // 공용 보스 BGM으로 맞서는 보스가 아직 남아 있는지 알려 준다.
    public bool IsBossMusicActive => bossBgmClip != null && currentBoss != null && currentBoss.gameObject.activeInHierarchy;

    private bool IsCurrentRoom => RoomManager.Instance != null && RoomManager.Instance.CurrentRoom == room;

    // Room은 떠날 때 자신의 GameObject를 끄므로, 구독은 활성 상태와 무관하게 파괴될 때까지 유지한다.
    // 그래야 이미 떠난 방도 다음 방에 음악이 없을 때 자기 곡을 줄일 수 있다.
    private void Awake()
    {
        room = GetComponentInParent<Room>(true);
        Subscribe();
    }

    private void OnEnable() => Subscribe();

    private void Start()
    {
        // RoomManager가 이 컴포넌트보다 늦게 준비되는 경우와 이미 이 방에 있는 경우를 함께 처리한다.
        Subscribe();
        if (HasMusic && IsCurrentRoom) Play();
    }

    private void Update()
    {
        // 보스가 쓰러져 풀로 돌아가거나 정리되면 방 BGM으로 돌아온다.
        if (currentBoss == null || currentBoss.gameObject.activeInHierarchy) return;
        currentBoss = null;
        if (IsCurrentRoom) Play();
    }

    private void OnDestroy()
    {
        if (subscribedManager != null) subscribedManager.OnRoomChanged -= HandleRoomChanged;
        subscribedManager = null;
        if (subscribedWaves != null)
        {
            subscribedWaves.EnemySpawned -= HandleEnemySpawned;
            subscribedWaves.WavesReset -= HandleWavesReset;
        }
        subscribedWaves = null;
    }

    private void Subscribe()
    {
        var waves = room != null ? room.WaveManager : null;
        if (waves != null && waves != subscribedWaves)
        {
            if (subscribedWaves != null)
            {
                subscribedWaves.EnemySpawned -= HandleEnemySpawned;
                subscribedWaves.WavesReset -= HandleWavesReset;
            }
            waves.EnemySpawned += HandleEnemySpawned;
            waves.WavesReset += HandleWavesReset;
            subscribedWaves = waves;
        }

        var manager = RoomManager.Instance;
        if (manager == null || manager == subscribedManager) return;
        if (subscribedManager != null) subscribedManager.OnRoomChanged -= HandleRoomChanged;
        manager.OnRoomChanged += HandleRoomChanged;
        subscribedManager = manager;
    }

    private void HandleRoomChanged(RoomType _)
    {
        if (!HasMusic && bossBgmClip == null) return;
        if (IsCurrentRoom) { if (HasMusic || IsBossMusicActive) Play(); return; }
        var current = RoomManager.Instance != null ? RoomManager.Instance.CurrentRoom : null;
        var next = current != null ? current.GetComponentInChildren<RoomBgmPlayer>(true) : null;
        if (next != null && next.HasMusic) return;
        var sound = SoundManager.Instance;
        if (sound == null) return;
        if (bgmClip != null) sound.FadeOutBGM(bgmClip, fadeDuration);
        if (bossBgmClip != null) sound.FadeOutBGM(bossBgmClip, fadeDuration);
    }

    // WaveManager가 보스 표시를 정한 직후에 알린다. 전용 BGM이 있는 보스는 자기 곡을 재생하므로 건너뛴다.
    private void HandleEnemySpawned(EnemyController enemy)
    {
        if (!enabled || bossBgmClip == null || enemy == null || enemy.Health == null || !enemy.Health.IsBossEnemy) return;
        if (enemy.TryGetComponent<EnemySpawnController>(out var spawn) && spawn.HasDedicatedBossMusic) return;
        currentBoss = enemy;
        if (IsCurrentRoom) Play();
    }

    // 웨이브를 처음부터 시작하거나 강제로 정리하면 보스전도 끝난 것으로 보고 방 BGM으로 돌아온다.
    private void HandleWavesReset()
    {
        bool wasBoss = currentBoss != null;
        currentBoss = null;
        if (wasBoss && IsCurrentRoom) Play();
    }

    private void Play()
    {
        var sound = SoundManager.Instance;
        if (sound == null) return;
        bool boss = IsBossMusicActive;
        var clip = boss ? bossBgmClip : bgmClip;
        float target = boss ? bossVolume : volume;
        if (clip == null)
        {
            // 방 BGM 없이 보스 BGM만 둔 방은 보스전이 끝나면 보스 곡을 줄여 멈춘다.
            if (bossBgmClip != null) sound.FadeOutBGM(bossBgmClip, fadeDuration);
            return;
        }
        if (sound.CurrentBgmClip == clip && sound.IsBgmPlaying && !sound.IsBgmDistant
            && Mathf.Abs(sound.CurrentBgmVolume - target) < .001f) return;
        sound.ChangeBGM(clip, fadeDuration, target, true);
    }
}
