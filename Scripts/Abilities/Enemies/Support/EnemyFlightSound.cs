using System.Collections.Generic;
using UnityEngine;

// 비행 중인 적의 반복 비행음이다(Swarmer의 프로펠러 소리). 재생은 SoundManager.PlayLoop가 맡는다.
// 여러 마리가 함께 날 때는 FeedbackData의 Sound와 같은 혼잡(Crowd) 기준을 반복음에 맞게 적용한다.
// - 같은 Clip을 쓰는 적끼리 카메라 중심에 가까운 순서로 순위를 매기고, 앞 순위 maxSimultaneous개만 소리를 낸다.
//   자리가 바뀔 때는 줄어드는 소리와 커지는 소리를 fadeTime 동안 겹쳐 빈틈 없이 넘긴다.
// - 순위 n의 음량은 volume × crowdedVolumeMultiplier^n이다(SoundManager 단발음의 혼잡 감쇠와 같은 식).
//   SoundManager는 반복음을 시작할 때 한 번만 이 값을 정하므로, 가까운 적이 죽거나 다가와 순위가 바뀌면 여기서 다시 계산한다.
//   음량은 fadeTime 동안 부드럽게 따라가고, 동시 수에서 밀려났던 적은 자리가 나면 다시 소리를 낸다.
// - 카메라 중심에서 멀어질수록 작아지고, 빠르게 날수록 음높이가 오른다.
//   개체마다 음높이(randomizePitch)와 반복 시작 위치를 달리해 여러 마리가 겹쳐도 한 소리로 뭉치지 않는다.
// - 등장 연출 등으로 AiBrain이 멈춘 동안과 일시정지(timeScale 0) 중에는 소리를 줄이고, 비활성화(Pool 반환)되면 반복음을 반환한다.
// 공용 AudioSource는 volume·pitch·위치만 바꾼다. SoundManager가 재생할 때마다 다시 정하는 값이라 다른 소리에 남지 않는다.
public class EnemyFlightSound : MonoBehaviour
{
    // 이미 소리를 내는 적은 이만큼(칸) 더 가까운 적이 나타나야 자리를 내준다. 무리 안에서 거리가 비슷한 적끼리 번갈아 켜지지 않게 한다.
    private const float VoiceKeepBonus = 3f;
    // SoundManager가 재생을 거절하면(동시 수 초과, 효과음 차단 등) 이 간격(초)으로 다시 시도한다.
    private const float RetryInterval = 0.1f;
    // 자리가 바뀔 때 줄어드는 소리와 커지는 소리가 겹치도록 SoundManager의 동시 수 제한에는 이만큼 여유를 둔다.
    // 들리는 수는 Update가 maxSimultaneous로 정하므로 여유분은 바뀌는 순간(fadeTime)에만 쓰인다.
    private const int HandoverSpare = 1;

    private sealed class Group
    {
        public readonly List<EnemyFlightSound> members = new();
        public int rankedFrame = -1;
    }

    private static readonly Dictionary<AudioClip, Group> groups = new();

    // Domain Reload를 끈 Play 반복에서도 이전 실행의 순위 목록이 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetGroups() => groups.Clear();

    [Tooltip("비행 상태와 속도를 읽을 적입니다. 비워 두면 같은 오브젝트나 부모에서 찾습니다.")]
    [SerializeField] private EnemyController controller;
    [Tooltip("비행 반복음입니다. maxSimultaneous는 동시에 들리는 최대 수, crowdedVolumeMultiplier는 순위마다 곱하는 음량 비율입니다. randomizePitch를 켜면 개체마다 pitchRange 안의 음높이를 씁니다.")]
    [SerializeField] private SoundOptions sound = new();

    [Header("Distance")]
    [Tooltip("카메라 중심에서 이 거리(칸)까지는 원래 음량입니다.")]
    [SerializeField, Min(0f)] private float fullVolumeDistance = 8f;
    [Tooltip("카메라 중심에서 이 거리(칸)부터 들리지 않습니다.")]
    [SerializeField, Min(0f)] private float silentDistance = 20f;

    [Header("Speed Pitch")]
    [Tooltip("이 비행 속도(칸/초)에서 음높이가 가장 높습니다.")]
    [SerializeField, Min(0.01f)] private float speedForMaxPitch = 7.2f;
    [Tooltip("가장 빠를 때 더하는 음높이 비율입니다. 0.12면 12% 높아집니다.")]
    [SerializeField, Range(0f, 1f)] private float maxPitchRise = 0.12f;
    [Tooltip("음높이가 속도 변화를 따라가는 속도(1/초)입니다.")]
    [SerializeField, Min(0f)] private float pitchResponse = 6f;

    [Header("Fade")]
    [Tooltip("소리를 켜고 끄거나 순위가 바뀔 때 음량이 바뀌는 시간(초)입니다.")]
    [SerializeField, Min(0.01f)] private float fadeTime = 0.2f;

    private Group group;
    private SoundHandle handle;
    private SoundOptions silentStart;
    private int rank = int.MaxValue;
    private float listenerDistance;
    private float volume;
    private float basePitch = 1f;
    private float pitchScale = 1f;
    private float nextAcquireTime;

    // 검증과 확인용 읽기 전용 상태
    public bool IsVoiced => handle != null && handle.IsValid;
    public int Rank => rank;
    public float CurrentVolume => volume;
    public float CurrentPitch => basePitch * pitchScale;
    public SoundOptions Sound => sound;

    private bool IsFlying => controller != null && controller.Brain != null && !controller.Brain.IsPaused;

    private void Awake()
    {
        if (controller == null)
            controller = GetComponentInParent<EnemyController>();
    }

    private void OnEnable()
    {
        rank = int.MaxValue;
        volume = 0f;
        pitchScale = 1f;
        nextAcquireTime = 0f;
        if (sound == null || sound.clip == null)
            return;

        if (!groups.TryGetValue(sound.clip, out group))
        {
            group = new Group();
            groups.Add(sound.clip, group);
        }
        group.members.Add(this);
    }

    private void OnDisable()
    {
        Release();
        if (group == null)
            return;

        group.members.Remove(this);
        if (group.members.Count == 0 && sound != null && sound.clip != null)
            groups.Remove(sound.clip);
        group = null;
        rank = int.MaxValue;
    }

    private void Update()
    {
        if (group == null)
            return;

        RankGroup(group, ListenerPosition());

        int voices = Mathf.Max(1, sound.maxSimultaneous);
        bool audible = IsFlying && Time.timeScale > 0f && rank < voices;
        float target = audible ? sound.volume * Mathf.Pow(sound.crowdedVolumeMultiplier, rank) * DistanceGain() : 0f;

        // 다른 곳에서 효과음을 모두 멈춰 이미 반환된 경우
        if (handle != null && !handle.IsValid)
            Release();

        if (handle == null && target > 0f && Time.unscaledTime >= nextAcquireTime)
            Acquire();

        // 소리를 얻기 전에는 음량을 0에 두어, 얻은 뒤 항상 fadeTime 동안 커지게 한다.
        if (handle == null)
            return;

        float step = Mathf.Max(sound.volume, 0.0001f) / fadeTime * Time.unscaledDeltaTime;
        volume = Mathf.MoveTowards(volume, target, step);
        if (target <= 0f && volume <= 0f)
        {
            Release();
            return;
        }

        AudioSource source = handle.GetSource();
        source.volume = volume;
        source.pitch = basePitch * UpdatePitchScale();
        handle.SetPosition(transform.position);
    }

    // 같은 Clip의 적을 프레임마다 한 번 정렬한다. 비행 중이 아닌 적은 순위에서 빠진다.
    private static void RankGroup(Group target, Vector2 listener)
    {
        if (target.rankedFrame == Time.frameCount)
            return;
        target.rankedFrame = Time.frameCount;

        foreach (EnemyFlightSound member in target.members)
            member.listenerDistance = ((Vector2)member.transform.position - listener).magnitude;

        target.members.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));

        int next = 0;
        foreach (EnemyFlightSound member in target.members)
            member.rank = member.IsFlying ? next++ : int.MaxValue;
    }

    private float SortKey => !IsFlying ? float.MaxValue : listenerDistance - (IsVoiced ? VoiceKeepBonus : 0f);

    private Vector2 ListenerPosition()
    {
        Camera view = Camera.main;
        return view != null ? (Vector2)view.transform.position : (Vector2)transform.position;
    }

    // 카메라 중심에서 fullVolumeDistance까지는 1, silentDistance에서 0으로 부드럽게 줄어든다.
    private float DistanceGain()
    {
        if (silentDistance <= fullVolumeDistance)
            return listenerDistance <= fullVolumeDistance ? 1f : 0f;

        float t = Mathf.InverseLerp(fullVolumeDistance, silentDistance, listenerDistance);
        return 1f - t * t * (3f - 2f * t);
    }

    private float UpdatePitchScale()
    {
        Vector2 velocity = controller != null
            ? controller.Velocity + (controller.IgnoreExternalForces ? Vector2.zero : controller.externalForce)
            : Vector2.zero;
        float goal = 1f + maxPitchRise * Mathf.Clamp01(velocity.magnitude / speedForMaxPitch);
        pitchScale = Mathf.Lerp(pitchScale, goal, 1f - Mathf.Exp(-pitchResponse * Time.unscaledDeltaTime));
        return pitchScale;
    }

    // 음량 0으로 시작해 Update가 목표 음량까지 올린다. SoundManager가 동시 수를 세고 개체별 음높이를 고른다.
    private void Acquire()
    {
        if (SoundManager.Instance == null || sound.clip == null)
            return;

        silentStart ??= SilentCopy(sound, sound.maxSimultaneous + HandoverSpare);
        handle = SoundManager.Instance.PlayLoop(silentStart, transform.position);
        if (handle == null)
        {
            nextAcquireTime = Time.unscaledTime + RetryInterval;
            return;
        }

        AudioSource source = handle.GetSource();
        basePitch = source.pitch;
        source.timeSamples = Random.Range(0, sound.clip.samples);
        source.volume = volume;
    }

    private void Release()
    {
        if (handle != null && handle.IsValid)
            handle.Stop();
        handle = null;
        volume = 0f;
    }

    private static SoundOptions SilentCopy(SoundOptions source, int maxSimultaneous) => new SoundOptions
    {
        bus = source.bus,
        clip = source.clip,
        volume = 0f,
        pitch = source.pitch,
        spatialBlend = source.spatialBlend,
        randomizePitch = source.randomizePitch,
        pitchRange = source.pitchRange,
        minInterval = source.minInterval,
        maxSimultaneous = maxSimultaneous,
        crowdedVolumeMultiplier = source.crowdedVolumeMultiplier
    };
}
