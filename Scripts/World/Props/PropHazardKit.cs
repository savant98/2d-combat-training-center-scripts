using System;
using UnityEngine;

// 던전 함정·적 생성 앵커 Prop(영역 hazards: 분쇄 투입구·분쇄기 하우징·폐자재 슈트·용접 팔·접속함·파괴 격자·폐자재 더미·제작 부스·수리 거치대·중계 비컨)이
// 함께 쓰는 작은 도우미. 판정은 모두 각 Prop 루트의 RaycastMotor2D 공격 질의(OverlapAttackBox/Circle·RaycastEnvironment)로 하고,
// 위치를 옮기는 것은 Prop 자신의 표시 부품뿐이다(캐릭터 위치는 건드리지 않는다).
// - 소리: 새 음원 없이 기존 효과음을 임시로 쓴다(DESIGN_V2 §1). 한 번 소리는 DungeonPropFeedback과 같은 거리 7~24칸 감쇠, 반복음은 HazardLoopSound.
// - 작동 조건: '항상' 또는 '방 클리어 전까지'(IDungeonRoomPowered 신호). 던전 밖(신호 없음)에서는 늘 작동한다.
public static class PropHazardKit
{
    public const string PowerKey = "hazardPower";
    private static readonly string[] PowerOptions = { "항상 작동", "방 클리어 전까지 작동" };

    /// <summary>
    /// 검사용: true면 화면 밖이어도 움직임을 갱신한다(창 없는 Editor는 Renderer.isVisible이 늘 false일 수 있다). 게임 코드에서는 켜지 않는다.
    /// </summary>
    public static bool IgnoreVisibility { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IgnoreVisibility = false;

    /// <summary>'작동 조건' 배치 설정 항목. 0 = 항상, 1 = 방 클리어 전까지.</summary>
    public static WaveDeviceParameter PowerParameter(int defaultMode) => new WaveDeviceParameter(PowerKey, "작동 조건",
        "항상 작동하거나, 방을 클리어하기 전까지만 작동합니다. 클리어 뒤 멈추는 함정은 지금 동작을 마치고 쉽니다. 던전 밖에서는 늘 작동합니다.",
        PowerOptions, defaultMode);

    /// <summary>기존 효과음 하나를 거리 감쇠로 재생한다(공유 SoundOptions는 바꾸지 않고 복사본을 쓴다).</summary>
    public static void PlayClip(SoundOptions options, Vector3 position, float volume = 1f)
    {
        if (options == null || options.clip == null || SoundManager.Instance == null) return;
        float gain = DungeonPropFeedback.DistanceGain(position) * volume;
        if (gain <= .001f) return;
        var copy = options.WithPitchMultiplier(1f);
        copy.volume = options.volume * Mathf.Clamp01(gain);
        SoundManager.Instance.PlaySFX(copy, position);
    }

    public static void PlayFeedback(FeedbackDataSO data, Vector3 position)
    {
        if (data != null) DungeonPropFeedback.Play(data, position, Quaternion.identity);
    }

    /// <summary>하나라도 카메라에 보이면 true(IgnoreVisibility면 늘 true).</summary>
    public static bool AnyVisible(Renderer[] probes)
    {
        if (IgnoreVisibility) return true;
        if (probes == null) return false;
        for (int i = 0; i < probes.Length; i++)
            if (probes[i] != null && probes[i].isVisible) return true;
        return false;
    }

    /// <summary>DungeonProp 표시 묶음(없으면 자기 자신) 아래의 Renderer 전부.</summary>
    public static Renderer[] VisualRenderers(Component owner)
    {
        if (owner == null) return Array.Empty<Renderer>();
        var prop = owner.GetComponent<DungeonProp>();
        var root = prop != null && prop.VisualRoot != null ? prop.VisualRoot : owner.transform;
        return root.GetComponentsInChildren<Renderer>(true);
    }

    /// <summary>모터의 공격 상자 질의(결과 배열이 모자라면 늘린다).</summary>
    public static int OverlapBox(RaycastMotor2D motor, Vector2 center, Vector2 size, float angle, ref Collider2D[] hits, LayerMask mask)
    {
        if (motor == null || size.x <= 0f || size.y <= 0f) return 0;
        int count;
        do
        {
            count = motor.OverlapAttackBox(center, size, angle, hits, mask);
            if (count < hits.Length) break;
            Array.Resize(ref hits, hits.Length * 2);
        } while (true);
        return count;
    }

    /// <summary>Player 몸통 범위(PropExplosive.PlayerBody와 같다). Player가 없으면 false.</summary>
    public static bool TryPlayerBody(out PlayerController player, out Bounds body)
    {
        player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || player.Health == null || !player.gameObject.activeInHierarchy)
        {
            body = default;
            return false;
        }
        body = PropExplosive.PlayerBody(player);
        return true;
    }

    public static void Play(ParticleSystem system)
    {
        if (system != null && !system.isPlaying) system.Play(true);
    }

    public static void Stop(ParticleSystem system, bool clear = false)
    {
        if (system != null) system.Stop(true, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
    }

    public static void Play(ParticleSystem[] systems)
    {
        if (systems == null) return;
        foreach (var system in systems) Play(system);
    }

    public static void Stop(ParticleSystem[] systems, bool clear = false)
    {
        if (systems == null) return;
        foreach (var system in systems) Stop(system, clear);
    }

    /// <summary>입자를 count개 한 번에 낸다(position이 있으면 그 월드 위치로 옮겨서).</summary>
    public static void Burst(ParticleSystem system, int count, Vector3? position = null)
    {
        if (system == null || count <= 0) return;
        if (position.HasValue) system.transform.position = position.Value;
        if (!system.gameObject.activeInHierarchy) return;
        system.Emit(count);
    }

    /// <summary>0~1 부드러운 곡선(시작·끝 속도 0).</summary>
    public static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}

/// <summary>'항상 / 방 클리어 전까지' 작동 조건 상태. IDungeonRoomPowered 신호를 받으면 그 방 안으로 본다.</summary>
public sealed class HazardPowerGate
{
    public const int Always = 0, UntilClear = 1;
    private bool signal;
    private bool cleared;

    public int Mode { get; set; }
    /// <summary>지금 작동해야 하는지.</summary>
    public bool Running => Mode != UntilClear || !signal || !cleared;
    public bool HasSignal => signal;

    public void SetRoomPowered(bool powered)
    {
        signal = true;
        cleared = powered;
    }

    /// <summary>풀에서 다시 꺼낼 때(신호 없음 = 던전 밖으로 본다).</summary>
    public void Reset()
    {
        signal = false;
        cleared = false;
    }
}

/// <summary>
/// 거리 감쇠 반복음(임시 기존 효과음). 매 프레임 Update로 위치·음량을 맞추고, 멀거나 꺼지면 멈춰 공용 동시 재생 자리를 비운다.
/// </summary>
public sealed class HazardLoopSound
{
    private SoundHandle handle;
    private SoundOptions playing;

    public bool IsPlaying => handle != null && handle.IsValid;

    public void Update(SoundOptions options, Vector3 position, bool active, float level = 1f)
    {
        float gain = active && options != null && options.clip != null ? DungeonPropFeedback.DistanceGain(position) * Mathf.Clamp01(level) : 0f;
        if (gain <= .001f)
        {
            Stop();
            return;
        }
        if (playing != options) Stop();
        if (!IsPlaying)
        {
            if (SoundManager.Instance == null) return;
            handle = SoundManager.Instance.PlayLoop(options, position);
            playing = options;
            if (handle == null) return;
        }
        handle.SetPosition(position);
        var source = handle.GetSource();
        if (source != null) source.volume = options.volume * gain;
    }

    public void Stop()
    {
        if (handle != null && handle.IsValid) handle.Stop();
        handle = null;
        playing = null;
    }
}

/// <summary>
/// 발광 부품 묶음의 밝기(DungeonPropGlow — MaterialPropertyBlock, 공유 재질 무수정). 거의 0이면 Renderer를 끈다
/// (WaveTools 계열 재질은 알파 블렌드라 밝기 0이 검은 선으로 남는다).
/// </summary>
public sealed class HazardLights
{
    private readonly Renderer[] renderers;
    private readonly DungeonPropGlow glow;
    private readonly bool[] home;
    private float last = float.NaN;

    public HazardLights(Renderer[] targets)
    {
        renderers = targets ?? Array.Empty<Renderer>();
        glow = new DungeonPropGlow(renderers);
        home = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) home[i] = renderers[i] != null && renderers[i].enabled;
    }

    public int Count => renderers.Length;
    public float Level => last;

    public void Set(float level)
    {
        if (Mathf.Approximately(level, last)) return;
        last = level;
        bool on = level > .02f;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = on;
        if (on) glow.Set(level);
    }

    /// <summary>Prefab 상태(켜짐·재질 밝기)로 되돌린다.</summary>
    public void Restore()
    {
        last = float.NaN;
        glow.Clear();
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = home[i];
    }
}
