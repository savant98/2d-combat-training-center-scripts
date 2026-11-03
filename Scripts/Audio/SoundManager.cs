using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Mixer")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string masterVolumeParam = "MasterVolume";
    [SerializeField] private string bgmVolumeParam = "BGMVolume";
    [SerializeField, Range(-80f, 0f)] private float defaultBgmVolumeDb = -15f;
    [SerializeField] private string sfxVolumeParam = "SFXVolume";

    [Header("Audio Sources")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioMixerGroup bgmMixerGroup;
    [SerializeField] private AudioMixerGroup sfxMixerGroup;

    [Header("SFX Pool")]
    [SerializeField] private int initialSfxPoolSize = 20;
    [SerializeField] private int maxSfxPoolSize = 50;

    private readonly Queue<AudioSource> sfxPool = new();
    private readonly List<AudioSource> activeSfxSources = new();

    private readonly Dictionary<AudioClip, float> lastPlayTimes = new();
    private readonly Dictionary<AudioClip, int> activeClipCounts = new();
    private readonly Dictionary<SoundHandle, Coroutine> loopFollowRoutines = new();
    private readonly Dictionary<SoundHandle, AudioClip> loopHandleClips = new();

    private readonly Dictionary<AudioSource, SoundBus> sourceBuses = new();
    private readonly Dictionary<AudioSource, Coroutine> returnRoutines = new();
    private readonly Dictionary<int, AudioClip> sfxExceptions = new();
    private int nextSfxScope;

    // 차단은 중첩 가능하다. 모든 소유자가 허용한 Clip만 통과한다.
    public System.IDisposable SuppressSfxExcept(AudioClip allowedClip)
    {
        int id = ++nextSfxScope;
        sfxExceptions.Add(id, allowedClip);
        return new SfxScope(() => sfxExceptions.Remove(id));
    }

    private sealed class SfxScope : System.IDisposable
    {
        private System.Action release;
        public SfxScope(System.Action release) { this.release = release; }
        public void Dispose() { var action = release; release = null; action?.Invoke(); }
    }

    private bool IsSfxSuppressed(SoundOptions options)
    {
        if (options.bus != SoundBus.SFX) return false;
        foreach (var allowed in sfxExceptions.Values)
            if (options.clip != allowed) return true;
        return false;
    }

    // pooled BGM과 전용 BGM Source는 보존한다. 반환 루틴도 취소해 즉시 재사용을 안전하게 한다.
    public void StopAllSfx()
    {
        foreach (var handle in new List<SoundHandle>(loopHandleClips.Keys))
            if (sourceBuses.TryGetValue(handle.GetSource(), out var bus) && bus == SoundBus.SFX)
                StopLoop(handle);
        foreach (var source in new List<AudioSource>(activeSfxSources))
        {
            if (!sourceBuses.TryGetValue(source, out var bus) || bus != SoundBus.SFX) continue;
            if (returnRoutines.TryGetValue(source, out var routine)) StopCoroutine(routine);
            ReturnOneShot(source, source.clip);
        }
    }

    private Transform sfxRoot;
    private Coroutine bgmFadeRoutine;
    private AudioSource scheduledBgmSource;
    private AudioClip sequencePreludeClip, sequenceLoopClip;
    private double sequenceStartDspTime, sequenceSwitchDspTime, pausedBgmRemaining;
    private bool sequencePending, sequencePaused;
    private int bgmPlaybackId, pausedBgmSamples;
    private bool earlyBgmVolumeWasSet;
    private float earlyBgmVolumeValue;
    private Coroutine bgmDistanceRoutine;
    private const float OpenCutoffFrequency = 22000f;

    // 현재 곡이 멀리서 들리도록 낮춰진 상태다. 새 곡을 시작하거나 멈추면 원음으로 돌아간다.
    public bool IsBgmDistant { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        sfxRoot = new GameObject("SFXPool").transform;
        sfxRoot.SetParent(transform);

        if (bgmSource == null)
        {
            bgmSource = gameObject.AddComponent<AudioSource>();
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
        }

        bgmSource.outputAudioMixerGroup = bgmMixerGroup;

        for (int i = 0; i < initialSfxPoolSize; i++)
            CreateSfxSource();
    }

    private void Start()
    {
        // Mixer 초기화 이후 기본값 또는 먼저 지정한 BGM 음량을 적용한다.
        if (Instance != this || audioMixer == null)
            return;

        if (earlyBgmVolumeWasSet)
            SetMixerVolume(bgmVolumeParam, earlyBgmVolumeValue);
        else
            audioMixer.SetFloat(bgmVolumeParam, defaultBgmVolumeDb);
    }

    private void OnDisable()
    {
        // 도메인 재로드 없이 다시 실행해도 이전 실행의 초기 설정을 재사용하지 않는다.
        earlyBgmVolumeWasSet = false;
        if (Instance == this) StopBGM();
    }

    private AudioSource CreateSfxSource()
    {
        if (activeSfxSources.Count + sfxPool.Count >= maxSfxPoolSize)
            return null;

        GameObject obj = new GameObject("SFXSource");
        obj.transform.SetParent(sfxRoot);

        AudioSource source = obj.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.outputAudioMixerGroup = sfxMixerGroup;

        sfxPool.Enqueue(source);
        return source;
    }

    private AudioSource GetSfxSource()
    {
        if (sfxPool.Count <= 0)
            CreateSfxSource();

        if (sfxPool.Count <= 0)
            return null;

        AudioSource source = sfxPool.Dequeue();
        activeSfxSources.Add(source);
        return source;
    }

    public void PlayBGM(AudioClip clip, float volume = 1f, bool loop = true)
    {
        if (clip == null) return;
        StopBGM();
        bgmSource.clip = clip;
        bgmSource.volume = volume;
        bgmSource.loop = loop;
        bgmSource.Play();
    }

    // 전주와 메인을 오디오 시계에 함께 예약한다. 반환 번호로 해당 재생만 정리한다.
    public int PlayBGMWithPrelude(AudioClip prelude, AudioClip loopClip, double loopStartSeconds, float volume = 1f)
    {
        if (prelude == null || loopClip == null || loopStartSeconds <= 0 || bgmSource == null) return 0;
        StopBGM();
        if (scheduledBgmSource == null)
        {
            var child = new GameObject("BGMTransition");
            child.transform.SetParent(transform, false);
            scheduledBgmSource = child.AddComponent<AudioSource>();
            scheduledBgmSource.playOnAwake = false;
        }
        scheduledBgmSource.outputAudioMixerGroup = bgmMixerGroup;
        scheduledBgmSource.spatialBlend = 0f;
        scheduledBgmSource.pitch = 1f;
        scheduledBgmSource.priority = 0;
        scheduledBgmSource.clip = loopClip;
        scheduledBgmSource.volume = volume;
        scheduledBgmSource.loop = true;
        bgmSource.outputAudioMixerGroup = bgmMixerGroup;
        bgmSource.spatialBlend = 0f;
        bgmSource.pitch = 1f;
        bgmSource.priority = 0;
        bgmSource.clip = prelude;
        bgmSource.volume = volume;
        bgmSource.loop = false;
        sequencePreludeClip = prelude;
        sequenceLoopClip = loopClip;
        sequencePending = true;
        sequenceStartDspTime = AudioSettings.dspTime + .1;
        // 직렬화된 float의 오차를 전주 샘플 경계로 맞춘다.
        double duration = System.Math.Round(loopStartSeconds * prelude.frequency) / prelude.frequency;
        sequenceSwitchDspTime = sequenceStartDspTime + duration;
        bgmSource.PlayScheduled(sequenceStartDspTime);
        bgmSource.SetScheduledEndTime(sequenceSwitchDspTime);
        scheduledBgmSource.PlayScheduled(sequenceSwitchDspTime);
        return bgmPlaybackId;
    }

    private void Update() => PromoteScheduledBgm();

    private void PromoteScheduledBgm()
    {
        if (!sequencePending || sequencePaused || AudioSettings.dspTime < sequenceSwitchDspTime) return;
        var previous = bgmSource;
        bgmSource = scheduledBgmSource;
        scheduledBgmSource = previous;
        previous.Stop();
        previous.clip = null;
        sequencePending = false;
    }

    private bool MatchesBgm(AudioClip clip)
    {
        PromoteScheduledBgm();
        return clip != null && ((bgmSource != null && bgmSource.clip == clip)
            || sequencePreludeClip == clip || sequenceLoopClip == clip);
    }

    private void CancelScheduledBgm()
    {
        if (scheduledBgmSource != null) { scheduledBgmSource.Stop(); scheduledBgmSource.clip = null; }
        sequencePending = false;
        sequencePaused = false;
        sequencePreludeClip = null;
        sequenceLoopClip = null;
    }

    public void StopBGM()
    {
        CancelBgmFade();
        ResetBgmDistance();
        CancelScheduledBgm();
        if (bgmSource != null) bgmSource.Stop();
        bgmPlaybackId++;
    }

    public void StopBGM(int playbackId)
    {
        if (playbackId != 0 && playbackId == bgmPlaybackId) StopBGM();
    }

    // 전주·메인 어느 쪽을 지정해도 같은 재생 묶음만 정지한다.
    public void StopBGM(AudioClip expectedClip)
    {
        if (MatchesBgm(expectedClip)) StopBGM();
    }

    public AudioClip CurrentBgmClip { get { PromoteScheduledBgm(); return bgmSource != null ? bgmSource.clip : null; } }
    public float CurrentBgmVolume { get { PromoteScheduledBgm(); return bgmSource != null ? bgmSource.volume : 0f; } }
    public bool IsBgmPlaying { get { PromoteScheduledBgm(); return bgmSource != null && bgmSource.isPlaying; } }

    // 현재 곡을 줄이는 동안 예약된 메인이 뒤늦게 시작하지 않도록 함께 취소한다.
    public void FadeOutBGM(AudioClip expectedClip, float duration)
    {
        if (!MatchesBgm(expectedClip)) return;
        CancelBgmFade();
        StopBgmDistanceRamp();
        CancelScheduledBgm();
        if (duration <= 0f || !isActiveAndEnabled) { bgmSource.volume = 0f; bgmSource.Stop(); ResetBgmDistance(); return; }
        bgmFadeRoutine = StartCoroutine(FadeOutBgmRoutine(bgmSource.clip, duration));
    }

    private void CancelBgmFade()
    {
        if (bgmFadeRoutine != null) StopCoroutine(bgmFadeRoutine);
        bgmFadeRoutine = null;
    }

    private IEnumerator FadeOutBgmRoutine(AudioClip expectedClip, float duration)
    {
        float volume = bgmSource.volume, elapsed = 0f;
        double started = Time.unscaledTimeAsDouble;
        while (elapsed < duration && bgmSource.clip == expectedClip)
        {
            bgmSource.volume = Mathf.Lerp(volume, 0f, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            yield return null;
            elapsed = (float)(Time.unscaledTimeAsDouble - started);
        }
        if (bgmSource.clip == expectedClip) { bgmSource.volume = 0f; bgmSource.Stop(); ResetBgmDistance(); }
        bgmFadeRoutine = null;
    }

    // 곡을 끊지 않고 음량과 고음을 함께 줄여 멀리서 들리는 것처럼 만든다.
    // 장면이 바뀌어도 이어지며, 새 곡을 시작하거나 멈추면 원음으로 돌아간다.
    public void SetBGMDistance(AudioClip expectedClip, float targetVolume, float cutoffFrequency, float duration)
    {
        if (!MatchesBgm(expectedClip) || sequencePending || bgmSource == null || !bgmSource.isPlaying) return;
        CancelBgmFade();
        StopBgmDistanceRamp();
        if (!bgmSource.TryGetComponent<AudioLowPassFilter>(out var filter))
            filter = bgmSource.gameObject.AddComponent<AudioLowPassFilter>();
        if (!filter.enabled) { filter.cutoffFrequency = OpenCutoffFrequency; filter.enabled = true; }
        IsBgmDistant = true;
        targetVolume = Mathf.Clamp01(targetVolume);
        cutoffFrequency = Mathf.Clamp(cutoffFrequency, 10f, OpenCutoffFrequency);
        if (duration <= 0f || !isActiveAndEnabled) { bgmSource.volume = targetVolume; filter.cutoffFrequency = cutoffFrequency; return; }
        bgmDistanceRoutine = StartCoroutine(BgmDistanceRoutine(bgmSource.clip, filter, targetVolume, cutoffFrequency, duration));
    }

    private IEnumerator BgmDistanceRoutine(AudioClip expectedClip, AudioLowPassFilter filter, float targetVolume, float targetCutoff, float duration)
    {
        float startVolume = bgmSource.volume, startCutoff = filter.cutoffFrequency, elapsed = 0f;
        double started = Time.unscaledTimeAsDouble;
        while (elapsed < duration && bgmSource.clip == expectedClip)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            bgmSource.volume = Mathf.Lerp(startVolume, targetVolume, t);
            // 고음 상한은 귀에 고르게 들리도록 배율로 줄인다.
            filter.cutoffFrequency = startCutoff * Mathf.Pow(targetCutoff / startCutoff, t);
            yield return null;
            elapsed = (float)(Time.unscaledTimeAsDouble - started);
        }
        if (bgmSource.clip == expectedClip) { bgmSource.volume = targetVolume; filter.cutoffFrequency = targetCutoff; }
        bgmDistanceRoutine = null;
    }

    private void StopBgmDistanceRamp()
    {
        if (bgmDistanceRoutine != null) StopCoroutine(bgmDistanceRoutine);
        bgmDistanceRoutine = null;
    }

    // 먼 소리 처리를 걷어 다음 곡이 원음으로 시작되게 한다.
    private void ResetBgmDistance()
    {
        StopBgmDistanceRamp();
        IsBgmDistant = false;
        DisableDistanceFilter(bgmSource);
        DisableDistanceFilter(scheduledBgmSource);
    }

    private static void DisableDistanceFilter(AudioSource source)
    {
        if (source != null && source.TryGetComponent<AudioLowPassFilter>(out var filter)) filter.enabled = false;
    }

    public void SetBGMPaused(AudioClip expectedClip, bool paused)
    {
        if (!MatchesBgm(expectedClip)) return;
        if (sequencePending)
        {
            if (sequencePaused == paused) return;
            if (paused)
            {
                pausedBgmSamples = bgmSource.timeSamples;
                pausedBgmRemaining = System.Math.Max(0, sequenceSwitchDspTime - System.Math.Max(AudioSettings.dspTime, sequenceStartDspTime));
                bgmSource.Stop();
                scheduledBgmSource.Stop();
                sequencePaused = true;
            }
            else
            {
                sequenceStartDspTime = AudioSettings.dspTime + .1;
                sequenceSwitchDspTime = sequenceStartDspTime + pausedBgmRemaining;
                bgmSource.timeSamples = pausedBgmSamples;
                bgmSource.PlayScheduled(sequenceStartDspTime);
                bgmSource.SetScheduledEndTime(sequenceSwitchDspTime);
                scheduledBgmSource.PlayScheduled(sequenceSwitchDspTime);
                sequencePaused = false;
            }
            return;
        }
        if (paused) bgmSource.Pause();
        else bgmSource.UnPause();
    }

    public void ChangeBGM(AudioClip clip, float fadeDuration = 0.5f, float volume = 1f, bool loop = true)
    {
        if (clip == null) return;
        PromoteScheduledBgm();
        CancelBgmFade();
        StopBgmDistanceRamp();
        CancelScheduledBgm();
        bgmPlaybackId++;
        bgmFadeRoutine = StartCoroutine(ChangeBGMRoutine(clip, fadeDuration, volume, loop));
    }

    private IEnumerator ChangeBGMRoutine(AudioClip nextClip, float fadeDuration, float targetVolume, bool loop)
    {
        float startVolume = bgmSource.volume;
        if (bgmSource.isPlaying && fadeDuration > 0f)
        {
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                bgmSource.volume = Mathf.Lerp(startVolume, 0f, t / fadeDuration);
                yield return null;
            }
        }
        ResetBgmDistance();
        bgmSource.clip = nextClip;
        bgmSource.loop = loop;
        bgmSource.Play();
        if (fadeDuration > 0f)
        {
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                bgmSource.volume = Mathf.Lerp(0f, targetVolume, t / fadeDuration);
                yield return null;
            }
        }
        bgmSource.volume = targetVolume;
        bgmFadeRoutine = null;
    }


    public void PlaySFX(SoundOptions sound, Vector3 position)
    {
        PlaySound(sound, position);
    }

    public void PlaySound(SoundOptions sound, Vector3 position)
    {
        if (sound == null || sound.clip == null || IsSfxSuppressed(sound))
            return;

        AudioClip clip = sound.clip;

        if (lastPlayTimes.TryGetValue(clip, out float lastTime))
        {
            if (Time.unscaledTime - lastTime < sound.minInterval)
                return;
        }

        activeClipCounts.TryGetValue(clip, out int activeCount);

        if (activeCount >= sound.maxSimultaneous)
            return;

        AudioSource source = GetSfxSource();
        if (source == null)
            return;

        lastPlayTimes[clip] = Time.unscaledTime;
        activeClipCounts[clip] = activeCount + 1;

        source.transform.position = position;
        source.clip = clip;
        source.volume = GetCrowdedVolume(sound.volume, activeCount, sound.crowdedVolumeMultiplier);
        source.pitch = sound.randomizePitch
            ? Random.Range(sound.pitchRange.x, sound.pitchRange.y)
            : sound.pitch;

        sourceBuses[source] = sound.bus;
        source.spatialBlend = sound.spatialBlend;
        source.outputAudioMixerGroup = sound.bus == SoundBus.BGM ? bgmMixerGroup : sfxMixerGroup;

        source.Play();

        returnRoutines[source] = StartCoroutine(ReturnSfxWhenFinished(source, clip));
    }
    public SoundHandle PlayLoop(SoundOptions sound, Vector3 position, Transform followTarget = null)
    {
        if (sound == null || sound.clip == null || IsSfxSuppressed(sound))
            return null;

        AudioClip clip = sound.clip;

        activeClipCounts.TryGetValue(clip, out int activeCount);

        if (activeCount >= sound.maxSimultaneous)
            return null;

        AudioSource source = GetSfxSource();
        if (source == null)
            return null;

        activeClipCounts[clip] = activeCount + 1;

        source.transform.position = position;
        source.clip = clip;
        source.volume = GetCrowdedVolume(sound.volume, activeCount, sound.crowdedVolumeMultiplier);
        source.pitch = sound.randomizePitch
            ? Random.Range(sound.pitchRange.x, sound.pitchRange.y)
            : sound.pitch;

        sourceBuses[source] = sound.bus;
        source.spatialBlend = sound.spatialBlend;
        source.outputAudioMixerGroup = sound.bus == SoundBus.BGM ? bgmMixerGroup : sfxMixerGroup;
        source.loop = true;
        source.Play();

        SoundHandle handle = new SoundHandle(source, this);
        loopHandleClips[handle] = clip;

        if (followTarget != null)
        {
            Coroutine routine = StartCoroutine(FollowLoopSound(handle, followTarget));
            loopFollowRoutines[handle] = routine;
        }

        return handle;
    }
    public void StopLoop(SoundHandle handle)
    {
        if (handle == null || !handle.IsValid)
            return;

        if (loopFollowRoutines.TryGetValue(handle, out Coroutine routine))
        {
            if (routine != null)
                StopCoroutine(routine);

            loopFollowRoutines.Remove(handle);
        }

        if (loopHandleClips.TryGetValue(handle, out AudioClip clip))
        {
            if (activeClipCounts.ContainsKey(clip))
            {
                activeClipCounts[clip]--;

                if (activeClipCounts[clip] <= 0)
                    activeClipCounts.Remove(clip);
            }

            loopHandleClips.Remove(handle);
        }

        AudioSource source = handle.GetSource();

        source.Stop();
        source.loop = false;
        source.clip = null;
        source.transform.SetParent(sfxRoot);
        source.transform.localPosition = Vector3.zero;

        sourceBuses.Remove(source);
        activeSfxSources.Remove(source);
        sfxPool.Enqueue(source);

        handle.Clear();
    }
    private IEnumerator FollowLoopSound(SoundHandle handle, Transform followTarget)
    {
        while (handle != null && handle.IsValid && followTarget != null)
        {
            handle.SetPosition(followTarget.position);
            yield return null;
        }

        if (handle != null && handle.IsValid)
            handle.Stop();
    }
    private float GetCrowdedVolume(float baseVolume, int activeCount, float multiplier)
    {
        if (activeCount <= 0)
            return baseVolume;

        return baseVolume * Mathf.Pow(multiplier, activeCount);
    }
    private IEnumerator ReturnSfxWhenFinished(AudioSource source, AudioClip clip)
    {
        yield return new WaitWhile(() => source.isPlaying);

        if (source.loop)
            yield break;

        ReturnOneShot(source, clip);
    }

    private void ReturnOneShot(AudioSource source, AudioClip clip)
    {
        if (!activeSfxSources.Remove(source)) return;
        returnRoutines.Remove(source);
        sourceBuses.Remove(source);
        source.Stop();
        source.loop = false;
        source.clip = null;
        source.transform.SetParent(sfxRoot);
        source.transform.localPosition = Vector3.zero;
        sfxPool.Enqueue(source);

        if (clip != null && activeClipCounts.ContainsKey(clip))
        {
            activeClipCounts[clip]--;

            if (activeClipCounts[clip] <= 0)
                activeClipCounts.Remove(clip);
        }
    }

    public void SetMasterVolume(float value01)
    {
        SetMixerVolume(masterVolumeParam, value01);
    }

    public void SetBGMVolume(float value01)
    {
        earlyBgmVolumeWasSet = true;
        earlyBgmVolumeValue = value01;
        SetMixerVolume(bgmVolumeParam, value01);
    }

    public void SetSFXVolume(float value01)
    {
        SetMixerVolume(sfxVolumeParam, value01);
    }

    private void SetMixerVolume(string paramName, float value01)
    {
        if (audioMixer == null) return;

        value01 = Mathf.Clamp01(value01);

        float db = value01 <= 0.0001f
            ? -80f
            : Mathf.Log10(value01) * 20f;

        audioMixer.SetFloat(paramName, db);
    }
}