using UnityEngine;

// 방이 현재 방이 되면 지정한 BGM을 반복 재생하고, 음악이 없는 방으로 떠나면 줄여서 멈춘다.
// 음악이 있는 다른 방으로 옮길 때는 그 방의 플레이어가 교차 전환을 맡아 처리 순서에 영향받지 않는다.
// 보스 음악·Title에서 이어 온 곡처럼 다른 소유자가 재생한 곡은 멈추지 않는다.
[DisallowMultipleComponent]
[RequireComponent(typeof(Room))]
public sealed class RoomBgmPlayer : MonoBehaviour
{
    [SerializeField] private AudioClip bgmClip;
    [SerializeField, Range(0f, 1f), Tooltip("기존 Title·Nightmare BGM과 같은 기준의 AudioSource 음량입니다.")]
    private float volume = .38f;
    [SerializeField, Min(0f), Tooltip("이전 곡을 줄이는 시간과 이 곡을 올리는 시간(초)입니다.")]
    private float fadeDuration = .8f;

    private Room room;
    private RoomManager subscribedManager;

    public AudioClip Clip => bgmClip;
    public float Volume => volume;
    public bool HasMusic => enabled && bgmClip != null;

    // Room은 떠날 때 자신의 GameObject를 끄므로, 구독은 활성 상태와 무관하게 파괴될 때까지 유지한다.
    // 그래야 이미 떠난 방도 다음 방에 음악이 없을 때 자기 곡을 줄일 수 있다.
    private void Awake()
    {
        room = GetComponent<Room>();
        Subscribe();
    }

    private void OnEnable() => Subscribe();

    private void Start()
    {
        // RoomManager가 이 컴포넌트보다 늦게 준비되는 경우와 이미 이 방에 있는 경우를 함께 처리한다.
        Subscribe();
        if (HasMusic && RoomManager.Instance != null && RoomManager.Instance.CurrentRoom == room) Play();
    }

    private void OnDestroy()
    {
        if (subscribedManager != null) subscribedManager.OnRoomChanged -= HandleRoomChanged;
        subscribedManager = null;
    }

    private void Subscribe()
    {
        var manager = RoomManager.Instance;
        if (manager == null || manager == subscribedManager) return;
        if (subscribedManager != null) subscribedManager.OnRoomChanged -= HandleRoomChanged;
        manager.OnRoomChanged += HandleRoomChanged;
        subscribedManager = manager;
    }

    private void HandleRoomChanged(RoomType _)
    {
        if (!HasMusic) return;
        var current = RoomManager.Instance != null ? RoomManager.Instance.CurrentRoom : null;
        if (current == room) { Play(); return; }
        if (current != null && current.TryGetComponent<RoomBgmPlayer>(out var next) && next.HasMusic) return;
        if (SoundManager.Instance != null) SoundManager.Instance.FadeOutBGM(bgmClip, fadeDuration);
    }

    private void Play()
    {
        var sound = SoundManager.Instance;
        if (sound == null) return;
        if (sound.CurrentBgmClip == bgmClip && sound.IsBgmPlaying && !sound.IsBgmDistant
            && Mathf.Abs(sound.CurrentBgmVolume - volume) < .001f) return;
        sound.ChangeBGM(bgmClip, fadeDuration, volume, true);
    }
}
