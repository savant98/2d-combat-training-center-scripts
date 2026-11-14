using System;
using UnityEngine;

/// <summary>
/// Lobby 장식 소품(고장 난 승강기 입구·CCTV 중계 화면·훈련생 순위 전광판)의 주변음을 낸다(2026-11-15). 소리 전용이며 판정·입력과 무관하다.
/// - 반복음(loop): 켜져 있는 동안 카메라와의 거리에 따라 음량이 줄고(WaveEnvironmentDevice.DistanceGain과 같은 계산), 들리지 않는 거리면 멈춘다.
/// - 짧은 소리(cues): 공회전 Clip의 Animation Event가 PlayCue(번호)를 불러 동작(불꽃·기어 덜컥·카메라 회전·행 훑기)에 맞춰 낸다.
///   randomInterval의 x가 0보다 크면 그 간격(x~y초)마다 무작위로도 낸다.
/// - 소리 파일이 아직 없으면(Clip이 비어 있으면) 아무 소리도 내지 않는다. 값은 tools/Unity/LobbyPropDesign.cs가 Prefab에 적는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyPropAmbience : MonoBehaviour
{
    [Header("반복음")]
    [SerializeField] private SoundOptions loop = new SoundOptions();
    [SerializeField, Min(0f), Tooltip("반복음이 켜지고 꺼질 때 음량이 따라가는 시간(초)")] private float loopFadeTime = .4f;

    [Header("짧은 소리")]
    [SerializeField, Tooltip("Animation Event의 PlayCue(번호)가 내는 소리")] private SoundOptions[] cues = Array.Empty<SoundOptions>();
    [SerializeField, Tooltip("x~y초마다 cues 가운데 하나를 무작위로 낸다. x가 0 이하이면 쓰지 않는다(Animation Event만 쓴다)")]
    private Vector2 randomInterval = Vector2.zero;

    [Header("거리에 따른 세기")]
    [SerializeField, Min(0f), Tooltip("카메라에서 이 거리(칸) 안이면 원래 세기로 낸다")] private float soundNearDistance = 6f;
    [SerializeField, Min(0f), Tooltip("카메라에서 이 거리(칸)부터는 소리를 내지 않는다")] private float soundFarDistance = 20f;
    [SerializeField, Tooltip("거리를 재고 소리를 내는 자리(비우면 이 개체의 자리)")] private Transform anchor;

    // SoundManager.PlaySound는 받은 값을 바로 AudioSource에 옮기므로, 거리 감쇠를 곱한 사본 하나를 계속 다시 쓴다.
    private readonly SoundOptions scratch = new SoundOptions();
    private SoundHandle loopHandle;
    private float loopBaseVolume, loopGain, nextLoopRetry, nextRandomCue;

    /// <summary>반복음이 지금 울리고 있는가(검증용).</summary>
    public bool IsLoopPlaying => loopHandle != null && loopHandle.IsValid;
    /// <summary>반복음에 지금 곱하고 있는 거리 감쇠(0~1, 검증용).</summary>
    public float LoopGain => loopGain;
    /// <summary>켜진 뒤 실제로 낸 짧은 소리의 수(검증용).</summary>
    public int CuesPlayed { get; private set; }
    public int CueCount => cues != null ? cues.Length : 0;

    /// <summary>생성 도구가 Prefab에 소리 설정을 적을 때 쓴다(Edit Mode).</summary>
    public void Configure(SoundOptions loopSound, SoundOptions[] cueSounds, Vector2 interval, float near, float far, Transform soundAnchor = null)
    {
        loop = loopSound != null ? loopSound : new SoundOptions();
        cues = cueSounds != null ? cueSounds : Array.Empty<SoundOptions>();
        randomInterval = interval;
        soundNearDistance = Mathf.Max(0f, near);
        soundFarDistance = Mathf.Max(soundNearDistance, far);
        anchor = soundAnchor;
    }

    /// <summary>짧은 소리 하나를 거리 감쇠를 곱해 낸다. 공회전 Clip의 Animation Event가 부른다.</summary>
    public void PlayCue(int index)
    {
        if (!isActiveAndEnabled || cues == null || index < 0 || index >= cues.Length) return;
        var sound = cues[index];
        if (sound == null || sound.clip == null || SoundManager.Instance == null) return;
        Vector2 position = SoundPosition;
        float gain = DistanceGain(position);
        // 들리지 않는 소리는 같은 Clip의 재생 간격·동시 재생 자리를 차지하지 않게 요청하지 않는다.
        if (gain <= 0f) return;
        Copy(sound, scratch);
        scratch.volume = sound.volume * gain;
        SoundManager.Instance.PlaySound(scratch, position);
        CuesPlayed++;
    }

    private Vector2 SoundPosition => anchor != null ? (Vector2)anchor.position : (Vector2)transform.position;

    private void OnEnable()
    {
        ScheduleRandomCue();
    }

    private void OnDisable()
    {
        // 방이 꺼지면 반복음도 바로 멈춘다(다시 켜지면 거리에 맞춰 서서히 들어온다).
        StopLoop();
        loopGain = 0f;
        nextLoopRetry = 0f;
    }

    private void Update()
    {
        UpdateLoop();
        if (randomInterval.x > 0f && cues != null && cues.Length > 0 && Time.unscaledTime >= nextRandomCue)
        {
            ScheduleRandomCue();
            PlayCue(UnityEngine.Random.Range(0, cues.Length));
        }
    }

    private void ScheduleRandomCue()
    {
        float low = Mathf.Max(.1f, randomInterval.x), high = Mathf.Max(low, randomInterval.y);
        nextRandomCue = Time.unscaledTime + UnityEngine.Random.Range(low, high);
    }

    private void UpdateLoop()
    {
        bool wanted = loop != null && loop.clip != null;
        Vector2 position = SoundPosition;
        float target = wanted ? DistanceGain(position) : 0f;
        float step = loopFadeTime > 0f ? Time.unscaledDeltaTime / loopFadeTime : 1f;
        loopGain = Mathf.MoveTowards(loopGain, target, step);
        if (loopGain <= 0f)
        {
            StopLoop();
            return;
        }
        if (loopHandle == null || !loopHandle.IsValid)
        {
            loopHandle = null;
            if (!wanted || SoundManager.Instance == null || Time.unscaledTime < nextLoopRetry) return;
            // SFX가 잠시 막혀 있거나 동시 재생 수가 찼으면 조금 뒤에 다시 시도한다.
            nextLoopRetry = Time.unscaledTime + .25f;
            loopHandle = SoundManager.Instance.PlayLoop(loop, position, null);
            if (loopHandle == null || !loopHandle.IsValid)
            {
                loopHandle = null;
                return;
            }
            loopBaseVolume = loopHandle.GetSource().volume;
        }
        loopHandle.SetPosition(position);
        loopHandle.GetSource().volume = loopBaseVolume * loopGain;
    }

    private void StopLoop()
    {
        if (loopHandle != null && loopHandle.IsValid) loopHandle.Stop();
        loopHandle = null;
    }

    /// <summary>카메라와의 거리에 따라 1(가까움)에서 0(멂)으로 줄어드는 세기. WaveEnvironmentDevice.DistanceGain과 같은 계산이다.</summary>
    private float DistanceGain(Vector2 position)
    {
        var listener = Camera.main;
        if (listener == null) return 1f;
        // 카메라 깊이가 2D 음량에 영향을 주지 않도록 XY 거리만 쓴다.
        float distance = Vector2.Distance(listener.transform.position, position);
        float near = Mathf.Max(0f, soundNearDistance);
        float far = Mathf.Max(near, soundFarDistance);
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
