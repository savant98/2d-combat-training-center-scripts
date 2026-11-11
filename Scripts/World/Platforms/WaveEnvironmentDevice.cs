using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스스로 움직이는 환경 장치(피스톤·톱날·점프대)가 함께 쓰는 기반이다.
/// 풀에서 꺼낸 뒤의 등장·퇴장 순서와, 카메라에서 먼 장치의 소리·흔들림을 줄이는 효과 재생을 맡는다.
/// </summary>
public abstract class WaveEnvironmentDevice : WorldEntityBase, IWaveDeviceSettings
{
    [Header("거리에 따른 효과 세기")]
    [Tooltip("카메라에서 이 거리(칸) 안이면 소리와 흔들림을 원래 세기로 낸다.")]
    [SerializeField, Min(0f)] private float soundNearDistance = 7f;
    [Tooltip("카메라에서 이 거리(칸)부터는 소리와 흔들림을 내지 않는다.")]
    [SerializeField, Min(0f)] private float soundFarDistance = 24f;

    protected RaycastMotor2D motor;
    protected WaveEnvironmentLifecycle lifecycle;

    private readonly Dictionary<FeedbackDataSO, FeedbackDataSO> distanceFeedbacks = new();
    private bool leaving;
    private bool damageApplied;
    private System.Action<WeaponBaseSO.DamageInfo, float> damageListener;

    // SoundManager는 같은 Clip의 짧은 간격 요청 중 먼저 온 것만 받는다. 같은 시각에 도는 장치 중 먼 쪽이 먼저 처리되면
    // 가까운 장치의 소리까지 작아지므로, 한 프레임의 요청을 Clip별로 모아 가장 가까운 장치의 세기로 한 번만 재생한다.
    private sealed class PendingSound
    {
        public readonly SoundOptions Options = new SoundOptions();
        public Vector2 Position;
        public float Gain;
        public int Frame = -1;
        public bool Queued;
    }
    private static readonly Dictionary<AudioClip, PendingSound> pendingSounds = new();
    private static bool anyPendingSound;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPendingSounds()
    {
        // 도메인 리로드를 끈 Play에서도 지난 실행의 요청이 남지 않게 한다.
        pendingSounds.Clear();
        anyPendingSound = false;
    }

    /// <summary>등장을 마치고 퇴장을 시작하기 전까지만 true다. 판정과 작동은 이 값으로 막는다.</summary>
    protected bool IsOperating => !leaving && (lifecycle == null || lifecycle.IsReady);
    /// <summary>풀 반환 요청을 받은 뒤부터 true다.</summary>
    protected bool IsLeaving => leaving;

    protected override void Awake()
    {
        base.Awake();
        lifecycle = GetComponent<WaveEnvironmentLifecycle>();
        // 모든 판정은 기존 모터의 질의를 거친다.
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        leaving = false;
        ResetDevice();
        // 풀에서 다시 꺼낸 개체는 Prefab 기본값에서 시작한다. 배치별 값은 WaveManager가 곧바로 적용한다.
        ApplySettings(WaveDeviceSettings.None);
        if (lifecycle != null) lifecycle.BeginSpawn(OnDeviceReady);
        else OnDeviceReady();
    }

    public override void ForceReturnToPool()
    {
        if (isReturning || leaving) return;
        leaving = true;
        OnDeviceLeaving();
        if (lifecycle == null) { base.ForceReturnToPool(); return; }
        // 장치가 움직이던 부품을 먼저 제자리에 넣어야 하면 그 뒤에 BeginDespawn을 직접 부른다.
        if (!HoldDespawn()) BeginDespawn();
    }

    /// <summary>접히는 퇴장 애니메이션을 시작하고, 끝나면 풀로 돌아간다.</summary>
    protected void BeginDespawn()
    {
        if (lifecycle != null) lifecycle.BeginDespawn(ReturnAfterDespawn);
        else base.ForceReturnToPool();
    }

    private void ReturnAfterDespawn() => base.ForceReturnToPool();

    protected virtual void OnDisable()
    {
        // 풀 확장으로 잠깐 켜졌다 꺼질 때와 부모가 꺼질 때도 지난 작동 상태를 남기지 않는다.
        if (!Application.isPlaying) return;
        leaving = false;
        ResetDevice();
        ApplySettings(WaveDeviceSettings.None);
    }

    private void LateUpdate() => FlushSounds();

    protected virtual void OnDestroy()
    {
        foreach (var copy in distanceFeedbacks.Values)
            if (copy != null) Destroy(copy);
        distanceFeedbacks.Clear();
    }

    /// <summary>편집기에서 조정할 수 있는 항목. Default는 이 Prefab의 기본값이다.</summary>
    public virtual WaveDeviceParameter[] Parameters => System.Array.Empty<WaveDeviceParameter>();
    /// <summary>설정에 따라 장치의 모양이 바뀌면 true다.</summary>
    public virtual bool SettingsChangeShape => false;
    /// <summary>배치마다 저장한 값을 이 개체에 적용한다. 비어 있으면 Prefab 기본값이다. Edit Mode의 편집기 미리보기에서도 불린다.</summary>
    public virtual void ApplySettings(WaveDeviceSetting[] overrides) { }
    /// <summary>주어진 설정에서의 공격·작동 범위(장치 원점 기준, 회전 전).</summary>
    public virtual bool TryGetRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        range = default;
        return false;
    }

    /// <summary>작동 상태를 처음으로 되돌린다. 시각 부품의 자세는 WaveEnvironmentLifecycle이 되돌린다.</summary>
    protected abstract void ResetDevice();
    /// <summary>등장 애니메이션이 끝나 작동을 시작할 때 한 번 불린다.</summary>
    protected virtual void OnDeviceReady() { }
    /// <summary>풀 반환 요청을 받은 순간 한 번 불린다. 반복음 같은 지속 효과를 여기서 끈다.</summary>
    protected virtual void OnDeviceLeaving() { }
    /// <summary>true를 돌려주면 퇴장 애니메이션을 미루고, 준비가 끝났을 때 BeginDespawn을 직접 부른다.</summary>
    protected virtual bool HoldDespawn() => false;

    /// <summary>카메라와의 거리에 따라 1(가까움)에서 0(멂)으로 줄어드는 효과 세기.</summary>
    protected float DistanceGain(Vector2 position)
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

    /// <summary>플레이어에게 피해를 주고 실제로 적용됐는지 돌려준다. 체력 전후 비교는 같은 호출 안에서 체력을 되돌리는 Wave를 놓친다.</summary>
    protected bool TryDamage(PlayerHealth player, WeaponBaseSO.DamageInfo info, Vector3 hitPosition)
    {
        damageApplied = false;
        damageListener ??= (_, _) => damageApplied = true;
        player.OnDamageApplied += damageListener;
        try { player.TakeDamage(info, hitPosition); }
        finally { player.OnDamageApplied -= damageListener; }
        return damageApplied;
    }

    /// <summary>파티클은 그대로, 소리와 카메라 흔들림은 거리에 맞춰 줄여 재생한다.</summary>
    protected void PlayFeedback(FeedbackDataSO data, Vector3 position, Quaternion rotation)
    {
        if (data == null || FeedbackPlayer.Instance == null) return;
        float gain = DistanceGain(position);
        QueueSound(data.sound, position, gain);
        FeedbackPlayer.Instance.PlayExact(DistanceFeedback(data, gain), position, rotation);
    }

    private static void QueueSound(SoundOptions sound, Vector2 position, float gain)
    {
        // 들리지 않는 소리는 같은 Clip의 재생 간격과 동시 재생 자리를 차지하지 않게 요청 자체를 뺀다.
        if (sound == null || sound.clip == null || gain <= 0f) return;
        if (!pendingSounds.TryGetValue(sound.clip, out var pending)) pendingSounds[sound.clip] = pending = new PendingSound();
        if (pending.Queued && pending.Frame == Time.frameCount && gain <= pending.Gain) return;
        pending.Queued = true;
        pending.Frame = Time.frameCount;
        pending.Gain = gain;
        pending.Position = position;
        var options = pending.Options;
        options.bus = sound.bus; options.clip = sound.clip; options.volume = sound.volume * gain; options.pitch = sound.pitch;
        options.spatialBlend = sound.spatialBlend; options.randomizePitch = sound.randomizePitch; options.pitchRange = sound.pitchRange;
        options.minInterval = sound.minInterval; options.maxSimultaneous = sound.maxSimultaneous;
        options.crowdedVolumeMultiplier = sound.crowdedVolumeMultiplier;
        anyPendingSound = true;
    }

    private static void FlushSounds()
    {
        if (!anyPendingSound) return;
        anyPendingSound = false;
        foreach (var pending in pendingSounds.Values)
        {
            if (!pending.Queued) continue;
            pending.Queued = false;
            // 요청한 장치가 모두 꺼져 프레임을 넘긴 소리는 늦게 울리지 않게 버린다.
            if (pending.Frame == Time.frameCount && SoundManager.Instance != null)
                SoundManager.Instance.PlaySound(pending.Options, pending.Position);
        }
    }

    private FeedbackDataSO DistanceFeedback(FeedbackDataSO data, float gain)
    {
        if (!distanceFeedbacks.TryGetValue(data, out var copy) || copy == null)
        {
            // 공유 에셋을 바꾸지 않도록 장치마다 사본 하나를 만들어 계속 쓴다.
            copy = Instantiate(data);
            copy.name = data.name + " (Distance Instance)";
            copy.hideFlags = HideFlags.DontSave;
            if (copy.particlePrefabs == null) copy.particlePrefabs = System.Array.Empty<GameObject>();
            copy.sound = null; // 소리는 QueueSound가 프레임 끝에 한 번만 낸다.
            distanceFeedbacks[data] = copy;
        }
        // 카메라 흔들림에는 거리 감쇠가 없어, 화면 밖 장치가 화면을 흔들지 않도록 같은 비율로 줄인다.
        copy.shakeIntensity = data.shakeIntensity * gain;
        return copy;
    }
}
