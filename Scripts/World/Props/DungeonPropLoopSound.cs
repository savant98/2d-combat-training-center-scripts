using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 움직이는 던전 장식(기어·덕트 팬·점검 트롤리·분류 로봇 팔·전선 신호 흐름)의 거리 감쇠 반복음과 간헐음(2026-11-24, 던전 2차 decor).
/// - 반복음: 카메라와의 XY 거리가 near(기본 7칸) 안이면 원래 음량, far(기본 24칸)에서 0이다(DungeonPropFeedback·WaveEnvironmentDevice와 같은
///   부드러운 감쇠). 0까지 줄면 AudioSource를 SoundManager에 돌려준다 — 화면 밖 먼 장식은 소리 자리를 차지하지 않는다.
/// - 같은 Clip을 쓰는 장식이 여럿이면 가장 크게 들릴(가장 가까운) 하나만 낸다. Clip마다 목소리(SoundHandle) 하나를 두고 그 자리·음량·음높이를
///   지금 가장 가까운 장식에 맞춘다(같은 방의 팬 세 개가 겹쳐 커지거나 동시 재생 수를 채우지 않게).
/// - loopWhenIdle이 꺼져 있으면 움직임 부품이 SetLoopActive(true)로 켠 동안만(움직이는 동안만) 울린다.
/// - 간헐음(cues): PlayCue(번호)로 내거나 randomInterval마다 무작위로 낸다. 같은 거리 감쇠를 곱하고, 들리지 않으면 요청하지 않는다.
/// - Prop이 풀로 돌아가거나 꺼지면(방 교체) 그 Prop이 내던 반복음은 바로 멈춘다. 같은 Clip의 다른 장식이 남아 있으면 그쪽에서 서서히 다시 들어온다.
/// - 새 음원 파일은 쓰지 않는다. 값은 tools/Unity/DungeonDecorPropSetup.cs·DungeonPropSetup.cs가 Prefab에 적는다(임시 기존 효과음).
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonPropLoopSound : MonoBehaviour, IDungeonPropPart
{
    [Header("반복음")]
    [SerializeField] private SoundOptions loop = new SoundOptions();
    [SerializeField, Tooltip("켜면 늘 울린다. 끄면 움직임 부품이 SetLoopActive(true)로 켠 동안만 울린다.")]
    private bool loopWhenIdle = true;
    [SerializeField, Min(0f), Tooltip("반복음 음량이 목표를 따라가는 시간(초)")] private float loopFadeTime = .35f;

    [Header("간헐음")]
    [SerializeField, Tooltip("PlayCue(번호)가 내는 짧은 소리")] private SoundOptions[] cues = Array.Empty<SoundOptions>();
    [SerializeField, Tooltip("x~y초마다 cues 가운데 하나를 무작위로 낸다. x가 0 이하이면 쓰지 않는다.")]
    private Vector2 randomInterval = Vector2.zero;

    [Header("거리 감쇠")]
    [SerializeField, Min(0f), Tooltip("카메라에서 이 거리(칸) 안이면 원래 세기")] private float nearDistance = 7f;
    [SerializeField, Min(0f), Tooltip("카메라에서 이 거리(칸)부터 소리를 내지 않는다")] private float farDistance = 24f;
    [SerializeField, Tooltip("거리를 재고 소리를 내는 자리(비우면 이 개체)")] private Transform anchor;

    // Clip마다 하나인 반복음 목소리. 주인(owner)은 지금 가장 크게 들릴 장식이다.
    private sealed class Voice
    {
        public SoundHandle handle;
        public DungeonPropLoopSound owner;
        public DungeonPropLoopSound candidate;
        public float best;
        public float gain;
        public float retryAt;
        public Vector2 position;
    }

    private static readonly Dictionary<AudioClip, Voice> voices = new Dictionary<AudioClip, Voice>();
    private static readonly List<DungeonPropLoopSound> live = new List<DungeonPropLoopSound>();
    private static int tickFrame = -1;

    private readonly SoundOptions scratch = new SoundOptions();
    private bool registered;
    private bool active;
    private float level = 1f;
    private float nextRandomCue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // 도메인 리로드를 끈 Play에서도 지난 실행의 목소리·등록이 남지 않게 한다.
        voices.Clear();
        live.Clear();
        tickFrame = -1;
    }

    /// <summary>이번 프레임에 이 장식이 바라는 반복음 세기(거리 감쇠 × 움직임 세기, 0~1. 검사용).</summary>
    public float TargetGain { get; private set; }
    /// <summary>켜진 뒤 실제로 낸 간헐음 수(검사용).</summary>
    public int CuesPlayed { get; private set; }
    public AudioClip LoopClip => loop != null ? loop.clip : null;
    public float LoopVolume => loop != null ? loop.volume : 0f;
    public bool LoopWhenIdle => loopWhenIdle;
    public bool LoopActive => loopWhenIdle || active;
    public int CueCount => cues != null ? cues.Length : 0;
    public float NearDistance => nearDistance;
    public float FarDistance => farDistance;
    /// <summary>이 장식이 지금 자기 Clip의 목소리를 내고 있는가(검사용).</summary>
    public bool IsVoiceOwner => TryGetVoice(LoopClip, out var owner, out _) && owner == this;

    /// <summary>생성 도구가 Prefab에 소리 설정을 적을 때 쓴다(Edit Mode).</summary>
    public void Configure(SoundOptions loopSound, bool whenIdle, SoundOptions[] cueSounds, Vector2 interval, float near, float far, Transform soundAnchor = null)
    {
        loop = loopSound ?? new SoundOptions();
        loopWhenIdle = whenIdle;
        cues = cueSounds ?? Array.Empty<SoundOptions>();
        randomInterval = interval;
        nearDistance = Mathf.Max(0f, near);
        farDistance = Mathf.Max(nearDistance, far);
        anchor = soundAnchor;
    }

    /// <summary>움직이는 동안만 우는 반복음을 켜고 끈다. volumeLevel(0~1)은 거리 감쇠에 더 곱하는 세기(가속·감속).</summary>
    public void SetLoopActive(bool on, float volumeLevel = 1f)
    {
        active = on;
        level = Mathf.Clamp01(volumeLevel);
    }

    /// <summary>짧은 소리 하나를 거리 감쇠를 곱해 낸다.</summary>
    public void PlayCue(int index)
    {
        if (!isActiveAndEnabled || cues == null || index < 0 || index >= cues.Length) return;
        var sound = cues[index];
        if (sound == null || sound.clip == null || SoundManager.Instance == null) return;
        Vector2 position = SoundPosition;
        float gain = DistanceGain(position, ListenerPosition(out bool hasListener), hasListener);
        // 들리지 않는 소리는 같은 Clip의 재생 간격·동시 재생 자리를 차지하지 않게 요청하지 않는다.
        if (gain <= 0f) return;
        Copy(sound, scratch);
        scratch.volume = sound.volume * gain;
        SoundManager.Instance.PlaySound(scratch, position);
        CuesPlayed++;
    }

    /// <summary>지금 이 Clip의 반복음을 누가 얼마의 음량으로 내는가(검사용). 울리고 있지 않으면 false.</summary>
    public static bool TryGetVoice(AudioClip clip, out DungeonPropLoopSound owner, out float volume)
    {
        owner = null;
        volume = 0f;
        if (clip == null || !voices.TryGetValue(clip, out var voice) || voice.handle == null || !voice.handle.IsValid) return false;
        owner = voice.owner;
        var source = voice.handle.GetSource();
        volume = source != null ? source.volume : 0f;
        return true;
    }

    /// <summary>울리고 있는 반복음 목소리 수(검사용).</summary>
    public static int PlayingVoiceCount
    {
        get
        {
            int count = 0;
            foreach (var voice in voices.Values)
                if (voice.handle != null && voice.handle.IsValid) count++;
            return count;
        }
    }

    // ── 수명 ─────────────────────────────

    private void OnEnable()
    {
        Register();
        ScheduleRandomCue();
    }

    private void OnDisable() => Unregister();

    public void OnPropSpawned(DungeonProp prop)
    {
        active = false;
        level = 1f;
        Register();
        ScheduleRandomCue();
    }

    // 풀로 돌아가기 직전(방 교체 — 화면이 가려진 동안)에는 그 Prop이 내던 반복음을 바로 멈춘다.
    public void OnPropReturning(DungeonProp prop)
    {
        active = false;
        Unregister();
    }

    private void Register()
    {
        if (registered) return;
        registered = true;
        live.Add(this);
    }

    private void Unregister()
    {
        if (!registered) return;
        registered = false;
        live.Remove(this);
        TargetGain = 0f;
        foreach (var voice in voices.Values)
        {
            if (voice.owner == this) StopVoice(voice);
            if (voice.candidate == this) voice.candidate = null;
        }
    }

    private void Update()
    {
        if (!registered || randomInterval.x <= 0f || cues == null || cues.Length == 0) return;
        // Time.time을 쓴다: 일시정지(timeScale 0) 동안에는 간헐음을 내지 않는다.
        if (Time.time < nextRandomCue) return;
        ScheduleRandomCue();
        PlayCue(UnityEngine.Random.Range(0, cues.Length));
    }

    private void LateUpdate()
    {
        // 움직임 부품이 Update에서 SetLoopActive를 마친 뒤, 한 프레임에 한 번만 모든 목소리를 갱신한다.
        if (!registered || tickFrame == Time.frameCount) return;
        tickFrame = Time.frameCount;
        TickVoices();
    }

    private void ScheduleRandomCue()
    {
        if (randomInterval.x <= 0f) return;
        float low = Mathf.Max(.1f, randomInterval.x), high = Mathf.Max(low, randomInterval.y);
        nextRandomCue = Time.time + UnityEngine.Random.Range(low, high);
    }

    // ── 목소리 갱신 ─────────────────────────────

    private static void TickVoices()
    {
        Vector2 listener = ListenerPosition(out bool hasListener);
        float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);

        foreach (var voice in voices.Values)
        {
            voice.candidate = null;
            voice.best = 0f;
        }
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var sound = live[i];
            if (sound == null) { live.RemoveAt(i); continue; }
            var clip = sound.LoopClip;
            sound.TargetGain = 0f;
            if (clip == null) continue;
            if (sound.LoopActive)
                sound.TargetGain = DistanceGain(sound.SoundPosition, listener, hasListener, sound.nearDistance, sound.farDistance) * (sound.loopWhenIdle ? 1f : sound.level);
            if (!voices.TryGetValue(clip, out var voice)) voices[clip] = voice = new Voice();
            if (sound.TargetGain > voice.best)
            {
                voice.best = sound.TargetGain;
                voice.candidate = sound;
            }
        }

        foreach (var voice in voices.Values)
        {
            var candidate = voice.candidate;
            if (candidate != null)
            {
                if (voice.owner != candidate && voice.handle != null && voice.handle.IsValid)
                {
                    // 주인이 바뀌면 같은 목소리를 넘겨받고 음높이만 새 주인 것으로 맞춘다(다시 시작하지 않는다).
                    var source = voice.handle.GetSource();
                    if (source != null && candidate.loop != null && !candidate.loop.randomizePitch) source.pitch = candidate.loop.pitch;
                }
                voice.owner = candidate;
                voice.position = candidate.SoundPosition;
            }
            var owner = voice.owner;
            if (owner == null) { StopVoice(voice); continue; }

            float target = candidate != null ? voice.best : 0f;
            float fade = owner.loopFadeTime;
            voice.gain = fade > 0f ? Mathf.MoveTowards(voice.gain, target, dt / fade) : target;
            if (voice.gain <= 0f)
            {
                // 멀어졌거나 멈췄다 — 음원을 돌려준다.
                StopVoice(voice);
                continue;
            }
            if (voice.handle == null || !voice.handle.IsValid)
            {
                voice.handle = null;
                if (SoundManager.Instance == null || Time.unscaledTime < voice.retryAt) continue;
                // SFX가 잠시 막혀 있거나 동시 재생 수가 찼으면 조금 뒤에 다시 시도한다.
                voice.retryAt = Time.unscaledTime + .25f;
                voice.handle = SoundManager.Instance.PlayLoop(owner.loop, voice.position, null);
                if (voice.handle == null || !voice.handle.IsValid)
                {
                    voice.handle = null;
                    continue;
                }
            }
            voice.handle.SetPosition(voice.position);
            var audio = voice.handle.GetSource();
            if (audio != null) audio.volume = owner.loop.volume * voice.gain;
        }
    }

    private static void StopVoice(Voice voice)
    {
        if (voice.handle != null && voice.handle.IsValid) voice.handle.Stop();
        voice.handle = null;
        voice.owner = null;
        voice.gain = 0f;
    }

    // ── 거리 ─────────────────────────────

    private Vector2 SoundPosition => anchor != null ? (Vector2)anchor.position : (Vector2)transform.position;

    private static Vector2 ListenerPosition(out bool found)
    {
        var listener = Camera.main;
        found = listener != null;
        return found ? (Vector2)listener.transform.position : Vector2.zero;
    }

    private float DistanceGain(Vector2 position, Vector2 listener, bool hasListener) =>
        DistanceGain(position, listener, hasListener, nearDistance, farDistance);

    /// <summary>카메라와의 XY 거리에 따라 1(가까움)에서 0(멂)으로 줄어드는 세기. WaveEnvironmentDevice.DistanceGain과 같은 계산이다.</summary>
    public static float DistanceGain(Vector2 position, Vector2 listener, bool hasListener, float near, float far)
    {
        if (!hasListener) return 1f;
        float distance = Vector2.Distance(listener, position);
        near = Mathf.Max(0f, near);
        far = Mathf.Max(near, far);
        if (far <= near) return distance <= near ? 1f : 0f;
        float t = Mathf.InverseLerp(near, far, distance);
        return 1f - t * t * (3f - 2f * t);
    }

    private static void Copy(SoundOptions from, SoundOptions to)
    {
        to.bus = from.bus; to.clip = from.clip; to.volume = from.volume; to.pitch = from.pitch;
        to.spatialBlend = from.spatialBlend; to.randomizePitch = from.randomizePitch; to.pitchRange = from.pitchRange;
        to.minInterval = from.minInterval; to.maxSimultaneous = from.maxSimultaneous;
        to.crowdedVolumeMultiplier = from.crowdedVolumeMultiplier;
    }
}
