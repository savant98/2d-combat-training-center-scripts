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

    private Transform sfxRoot;
    private Coroutine bgmFadeRoutine;

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

        bgmSource.clip = clip;
        bgmSource.volume = volume;
        bgmSource.loop = loop;
        bgmSource.Play();
    }

    public void StopBGM()
    {
        bgmSource.Stop();
    }

    public void ChangeBGM(AudioClip clip, float fadeDuration = 0.5f, float volume = 1f, bool loop = true)
    {
        if (clip == null) return;

        if (bgmFadeRoutine != null)
            StopCoroutine(bgmFadeRoutine);

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
        if (sound == null || sound.clip == null)
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

        source.spatialBlend = sound.spatialBlend;
        source.outputAudioMixerGroup = sfxMixerGroup;

        source.Play();

        StartCoroutine(ReturnSfxWhenFinished(source, clip));
    }
    public SoundHandle PlayLoop(SoundOptions sound, Vector3 position, Transform followTarget = null)
    {
        if (sound == null || sound.clip == null)
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

        source.spatialBlend = sound.spatialBlend;
        source.outputAudioMixerGroup = sfxMixerGroup;
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

        source.clip = null;
        source.transform.localPosition = Vector3.zero;

        activeSfxSources.Remove(source);
        sfxPool.Enqueue(source);

        if (activeClipCounts.ContainsKey(clip))
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