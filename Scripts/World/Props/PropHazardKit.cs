using System;
using System.Collections.Generic;
using UnityEngine;

// 던전 함정·적 생성 앵커 Prop(영역 hazards: 분쇄 투입구·분쇄기 하우징·낙석 구조물(7차 — 옛 폐자재 슈트)·용접 팔·접속함·파괴 격자·폐자재 더미·제작 부스·수리 거치대·중계 비컨)이
// 함께 쓰는 작은 도우미. 판정은 모두 각 Prop 루트의 RaycastMotor2D 공격 질의(OverlapAttackBox/Circle·RaycastEnvironment)로 하고,
// 위치를 옮기는 것은 Prop 자신의 표시 부품뿐이다(캐릭터 위치는 건드리지 않는다).
// - 소리: 새 음원 없이 기존 효과음을 임시로 쓴다(DESIGN_V2 §1). 한 번 소리는 DungeonPropFeedback과 같은 거리 7~24칸 감쇠, 반복음은 HazardLoopSound.
// - 6차 T9: 던전 함정은 방이 클리어되면 멈춘다(IDungeonRoomHazard — DungeonHazardHalt·HazardHalt). 신호가 없으면(던전 밖) 늘 작동한다.
//   예전 '작동 조건(항상/방 클리어 전까지)' 설정은 없앴다(템플릿에 남은 hazardPower 값은 읽지 않는다).
public static class PropHazardKit
{

    /// <summary>
    /// 검사용: true면 화면 밖이어도 움직임을 갱신한다(창 없는 Editor는 Renderer.isVisible이 늘 false일 수 있다). 게임 코드에서는 켜지 않는다.
    /// </summary>
    public static bool IgnoreVisibility { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IgnoreVisibility = false;

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

    /// <summary>Player 몸통 범위(PlayerBody). Player가 없으면 false.</summary>
    public static bool TryPlayerBody(out PlayerController player, out Bounds body)
    {
        player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || player.Health == null || !player.gameObject.activeInHierarchy)
        {
            body = default;
            return false;
        }
        body = PlayerBody(player);
        return true;
    }

    // PlayerBody가 매번 다시 쓰는 목록·레이어 번호(물리 틱마다 불려도 배열 할당·문자열 검색을 하지 않는다).
    private static readonly List<Collider2D> bodyScratch = new List<Collider2D>();
    private static int playerLayerCache = int.MinValue;

    /// <summary>
    /// Player 몸통(Player 레이어의 트리거 아닌 Collider)의 범위. 못 찾으면 위치 둘레 0.8×1.6칸.
    /// (6차 T7: 폭발 Prop(PropExplosive)을 지우며 이리로 옮겼다 — 물리 질의 없이 Collider.bounds만 읽는다.)
    /// </summary>
    public static Bounds PlayerBody(PlayerController player)
    {
        if (playerLayerCache == int.MinValue) playerLayerCache = LayerMask.NameToLayer("Player");
        int playerLayer = playerLayerCache;
        Collider2D best = null;
        player.GetComponentsInChildren(bodyScratch);
        foreach (var c in bodyScratch)
        {
            if (c == null || !c.enabled || c.isTrigger) continue;
            if (best == null || c.gameObject.layer == playerLayer && best.gameObject.layer != playerLayer) best = c;
            if (c.transform == player.transform) { best = c; break; }
        }
        bodyScratch.Clear();
        if (best != null) return best.bounds;
        return new Bounds(player.transform.position, new Vector3(0.8f, 1.6f, 0.1f));
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

/// <summary>
/// 6차 T9: 던전 방 함정의 정지 신호(IDungeonRoomHazard)를 보내는 곳. DungeonRoomStage가 부른다.
/// - Send: 함정 하나에 정지(halted)·작동 신호. instant = 방에 들어올 때 이미 클리어 상태라 바로 정지 자세로. 판정은 그 호출 안에서 바로 꺼진다.
/// - HaltWave: 방 클리어 순간 같은 프레임에 모든 함정의 판정을 끄고, 외형은 기준점(Player)에 가까운 순서로 interval초씩 늦게 꺼져 가는 '정전 물결'.
///   지연은 신호를 받는 함정이 호출 안에서 VisualDelay로 읽는다(계약 IDungeonRoomHazard는 그대로 두고 지연만 곁들인다).
/// </summary>
public static class DungeonHazardHalt
{
    /// <summary>정전 물결 간격(초, 설계 판단값).</summary>
    public const float WaveInterval = .04f;

    /// <summary>지금 보내는 신호의 외형 정지 지연(초). SetHazardHalted 안에서만 의미가 있다(그 밖에서는 0).</summary>
    public static float VisualDelay { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => VisualDelay = 0f;

    /// <summary>함정 하나에 신호를 보낸다. 예외는 기록만 하고 넘긴다(한 함정의 오류가 다른 함정의 정지를 막지 않게).</summary>
    public static void Send(IDungeonRoomHazard hazard, bool halted, bool instant, float visualDelay = 0f)
    {
        if (hazard == null || hazard is UnityEngine.Object unity && unity == null) return;
        float previous = VisualDelay;
        VisualDelay = instant ? 0f : Mathf.Max(0f, visualDelay);
        try { hazard.SetHazardHalted(halted, instant); }
        catch (Exception e) { Debug.LogException(e); }
        finally { VisualDelay = previous; }
    }

    /// <summary>
    /// 클리어 순간의 정지 물결: 모든 함정에 같은 프레임에 정지 신호를 보내(판정 즉시 끔) 외형만 origin에 가까운 순서로 interval초씩 늦춘다.
    /// 보낸 함정 수를 돌려준다. 위치를 알 수 없는 함정(컴포넌트 아님)은 맨 뒤에 둔다.
    /// </summary>
    public static int HaltWave(IEnumerable<IDungeonRoomHazard> hazards, Vector2 origin, float interval = WaveInterval)
    {
        if (hazards == null) return 0;
        var ordered = new List<(IDungeonRoomHazard hazard, float distance)>();
        foreach (var hazard in hazards)
        {
            if (hazard == null || hazard is UnityEngine.Object unity && unity == null) continue;
            float distance = hazard is Component component ? ((Vector2)component.transform.position - origin).sqrMagnitude : float.MaxValue;
            ordered.Add((hazard, distance));
        }
        ordered.Sort((a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < ordered.Count; i++) Send(ordered[i].hazard, true, false, Mathf.Max(0f, interval) * i);
        return ordered.Count;
    }
}

/// <summary>
/// 함정 하나의 정지 상태(6차 T9). 판정은 Halted로 바로 막고, 외형은 Blend(0 = 작동 자세 ~ 1 = 정지 자세)로 감는다.
/// 신호를 받기 전(던전 밖)에는 HasSignal = false·Halted = false라 늘 작동한다. 풀에서 다시 꺼낼 때 Reset.
/// </summary>
public sealed class HazardHalt
{
    private float delay;

    public bool HasSignal { get; private set; }
    /// <summary>정지(판정 끔). 신호를 받은 순간 바뀐다.</summary>
    public bool Halted { get; private set; }
    /// <summary>0 = 작동 자세 ~ 1 = 정지 자세.</summary>
    public float Blend { get; private set; }
    /// <summary>정지 신호를 받았고 정전 물결 지연도 지나 외형이 정지 쪽으로 가는 중이거나 끝났다.</summary>
    public bool VisualHalting => Halted && delay <= 0f;
    /// <summary>외형이 목표 자세에 닿았다.</summary>
    public bool Settled => Halted ? Blend >= 1f : Blend <= 0f;

    public void Reset()
    {
        HasSignal = Halted = false;
        Blend = 0f;
        delay = 0f;
    }

    /// <summary>신호를 받는다. 정지 여부가 바뀌었으면 true. instant면 외형도 바로 끝 자세다. 지연은 DungeonHazardHalt.VisualDelay.</summary>
    public bool Signal(bool halted, bool instant)
    {
        HasSignal = true;
        bool changed = halted != Halted;
        Halted = halted;
        if (instant)
        {
            delay = 0f;
            Blend = halted ? 1f : 0f;
        }
        else if (changed) delay = halted ? DungeonHazardHalt.VisualDelay : 0f;
        return changed;
    }

    /// <summary>
    /// 외형을 한 프레임 진행한다. 정지면 지연이 지난 뒤 duration초에 걸쳐 Blend를 1로, 작동이면 0으로 옮긴다.
    /// started = 이번 호출에서 외형 정지가 막 시작됐다(지연이 막 끝났거나 지연 없이 시작). settled = 이번 호출에서 끝 자세에 막 닿았다.
    /// 끝 자세에 거의 닿으면(Mathf.Approximately) 정확히 끝값(0·1)으로 맞춘다. dt/duration을 여러 번 더하면 float 누적 오차로 목표 바로 앞
    /// (예: 0.9999995)에 남을 수 있는데, 예전처럼 그 값을 '닿음'으로만 보고 멈추면 Blend >= 1f로 정지 자세를 판단하는 쪽
    /// (분쇄 투입구의 빨강 끄기·가시의 InHaltedPose)이 영영 끝나지 않는다. dt가 일정할 때(일시정지 Step·고정 프레임) 결정적으로 생긴다.
    /// </summary>
    public float Step(float dt, float duration, out bool started, out bool settled)
    {
        started = settled = false;
        if (Halted && delay > 0f)
        {
            delay -= dt;
            if (delay > 0f) return Blend;
            started = true;
        }
        else if (Halted && Blend <= 0f) started = true;
        float target = Halted ? 1f : 0f;
        if (Blend == target) return Blend;
        Blend = duration > 0f ? Mathf.MoveTowards(Blend, target, dt / duration) : target;
        if (Mathf.Approximately(Blend, target)) Blend = target;   // 누적 오차로 목표 바로 앞에 멈추지 않게 끝값으로 맞춘다
        settled = Blend == target;   // 위에서 이미 끝값이면 돌아갔으므로 '이번 호출에서 막 닿음'이다
        return Blend;
    }
}

/// <summary>
/// Renderer 묶음 켜고 끄기(6차 T9 — 정지 때 꺼 둘 위험부 빨강·표시등). 알파 블렌드 재질은 밝기 0이 검은 선으로 남으므로 Renderer를 끈다.
/// 켤 때는 처음(Prefab에 저장된) 켜짐 상태로 돌아간다(처음부터 꺼져 있던 Renderer는 켜지 않는다).
/// </summary>
public sealed class HazardRendererSwitch
{
    private readonly Renderer[] renderers;
    private readonly bool[] home;

    public HazardRendererSwitch(Renderer[] targets)
    {
        renderers = targets ?? Array.Empty<Renderer>();
        home = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) home[i] = renderers[i] != null && renderers[i].enabled;
    }

    /// <summary>root 아래(비활성 포함)에서 첫 재질 이름이 prefix로 시작하는 Renderer. 움직임 보정 대리 Renderer(__MotionPresentation_)는 뺀다.</summary>
    public static HazardRendererSwitch ByMaterial(Transform root, string prefix)
    {
        var found = new List<Renderer>();
        if (root != null)
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer is ParticleSystemRenderer || renderer.name.StartsWith("__MotionPresentation_", StringComparison.Ordinal)) continue;
                var material = renderer.sharedMaterial;
                if (material != null && material.name.StartsWith(prefix, StringComparison.Ordinal)) found.Add(renderer);
            }
        return new HazardRendererSwitch(found.ToArray());
    }

    public int Count => renderers.Length;
    public bool IsOn { get; private set; } = true;
    public IReadOnlyList<Renderer> Renderers => renderers;

    public void Set(bool on)
    {
        if (on == IsOn) return;
        IsOn = on;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = on && home[i];
    }

    /// <summary>처음 상태로 되돌린다(풀 반환·재사용).</summary>
    public void Restore()
    {
        IsOn = true;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = home[i];
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
