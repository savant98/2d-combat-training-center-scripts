using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방전된 Lobby 설비(업그레이드·자율훈련장)가 다시 조립되어 가동되는 해금 연출(2026-11-15 2차). 사용자 요청: "UpgradeMenu와 SelfTrainingMenu의
/// Animation효과를 더 능동적으로 바꿔줘. 지금은 거의 모든 배치가 움직이지않잖아? 기계가 조립되는 식으로 만들어줘(트랜스포머, 아이언맨의 슈트 입는장면 참고)".
/// - 방전 중의 장치는 분해·수납된 자세로 서 있다(LobbyStation이 조립 Clip의 0초를 적용한다).
/// - 연출: 접근 불가 푯말의 판이 기둥을 따라 내려앉고 전원이 끊긴 표지처럼 꺼진다 → 조립 Clip을 실제 시간으로 재생한다
///   (받침 전개 → 타워 기립·회전 잠금 → 갠트리 상승·걸쇠 → 상징판 장착 → 장치별 고유 동작 → 명판·투사 팔) → 전원이 아래에서 위로 들어오고 공회전이 돌기 시작한다.
///   PowerSurge(이름 유지) = 업그레이드 조립: 무겁고 딱딱하게, 작업 팔이 해머 머리를 조이며 불꽃이 튄다.
///   PartsAssembly(이름 유지) = 자율훈련장 조립: 빠르고 탄력 있게, 갠트리가 두 단계로 오르고 더미가 튀어 서며 계측 막대·바늘·훑는 선이 교정된다.
/// - 부품이 잠기는 순간의 소리와 불(박자)은 cues 배열이 정한다(tools/Unity/LobbyStationAssembly.cs가 Clip과 함께 채운다).
///   AnimationClip.SampleAnimation은 Clip의 Event를 부르지 않으므로 Event 대신 데이터로 둔다.
/// 끝나면 LobbyStation.SetPowered(true)로 확정한다. 도중에 이 개체가 꺼지면(방을 나감 등) 그 자리에서 해금 상태로 확정한다.
/// 카메라 이동과 입력 막기는 GameEventManager가 맡는다. 시간은 Time.deltaTime이다(연출 중 게임 시간은 멈추지 않는다).
/// 표시용 Transform만 움직인다(판정·이동과 무관하다).
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyStationUnlock : MonoBehaviour
{
    // 이름은 LobbyEventSetup·GameEventManager 연결이 찾으므로 그대로 둔다. PowerSurge = 업그레이드 조립, PartsAssembly = 자율훈련장 조립.
    public enum Style { PowerSurge, PartsAssembly }
    public enum CueKind { Move, Lock, Bolt, Light }

    [Serializable]
    public struct AssemblyCue
    {
        [Tooltip("조립 Clip 안의 시각(초)")] public float time;
        [Tooltip("Move = 움직이기 시작(구동음), Lock = 제자리에 잠김(철컥), Bolt = 볼트·작업 팔이 조임, Light = 소리 없이 불만")] public CueKind kind;
        [Tooltip("이 경로(장치 기준) 아래의 빛나는 부품을 이 순간 켠다. 더 깊은 경로의 박자가 따로 있으면 그쪽이 맡는다. 비우면 불은 켜지 않는다")] public string targetPath;
    }

    [SerializeField] private LobbyStation station;
    [SerializeField] private Style style = Style.PowerSurge;
    [SerializeField, Tooltip("카메라가 비출 자리(설비 가운데 아래쪽). 비우면 설비 위치")] private Transform focus;
    [SerializeField, Tooltip("조립 박자: 부품이 잠기는 순간의 소리와 불(시간순). 조립 Clip은 LobbyStation의 assembleClip을 쓴다")] private AssemblyCue[] cues = Array.Empty<AssemblyCue>();

    [Header("소리")]
    [SerializeField, Tooltip("부품이 움직이기 시작할 때(구동음)")] private SoundOptions moveSound;
    [SerializeField, Tooltip("부품이 제자리에 잠길 때(철컥)")] private SoundOptions lockSound;
    [SerializeField, Tooltip("볼트·작업 팔이 조일 때")] private SoundOptions boltSound;
    [SerializeField, Tooltip("마지막 전원 점등 때 한 번")] private SoundOptions unlockSound;

    [Header("시간(초)")]
    [SerializeField, Min(.1f), Tooltip("접근 불가 푯말을 걷는 시간(판이 내려앉고 꺼진다)")] private float barrierStowTime = .55f;
    [SerializeField, Min(.1f), Tooltip("조립이 끝난 뒤 전원이 아래에서 위로 들어오는 시간")] private float powerTime = .5f;
    [SerializeField, Min(.1f)] private float spinUpTime = 1f;
    [SerializeField, Min(0f)] private float finishHold = .45f;

    [Header("빛")]
    [SerializeField, Tooltip("켜지는 순간 밝기 배율")] private float flashGain = 2.6f;
    [SerializeField, Min(.05f)] private float flashDecay = .35f;

    // 푯말의 판으로 보는 높이(이보다 위에서 시작하는 Renderer)와, 판이 내려앉는 높이(설비 바닥에서, 칸)
    const float BoardFrom = 1.5f, BoardRest = .2f;

    private sealed class LitPart
    {
        public Renderer Renderer;
        public Material Lit;
        public float BaseIntensity;
        public int Cue = -1;
        public bool On;
    }

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private MaterialPropertyBlock block;
    private readonly List<LitPart> lit = new();
    private readonly Dictionary<Renderer, float> flashes = new();
    private readonly List<(Transform transform, Vector3 position)> board = new();
    private readonly List<(Renderer renderer, bool enabled)> barrierRenderers = new();
    private int run;

    public LobbyStation Station => station;
    public Style UnlockStyle => style;
    public Transform Focus => focus != null ? focus : transform;
    public bool IsPlaying { get; private set; }
    // 검증용: 지금까지 불이 들어온 부품 수
    public int LitSoFar { get; private set; }
    /// <summary>검증용: 조립 Clip에서 지금 적용한 시각(초). 조립 전·후에는 -1.</summary>
    public float AssembleTime { get; private set; } = -1f;
    public IReadOnlyList<AssemblyCue> Cues => cues;

    private void Reset() => station = GetComponent<LobbyStation>();

    private void OnDisable()
    {
        // 방을 나가거나 꺼져 코루틴이 멈춰도 해금 상태는 확정한다(멈춘 코루틴의 finally는 돌지 않는다).
        if (IsPlaying) Finish();
    }

    /// <summary>해금 연출을 재생하고 끝나면 해금 상태로 만든다. 이미 해금돼 있으면 바로 끝난다.</summary>
    public IEnumerator Play()
    {
        if (station == null || station.IsPowered || IsPlaying) yield break;
        int id = ++run;
        IsPlaying = true;
        LitSoFar = 0;
        if (block == null) block = new MaterialPropertyBlock();
        Collect();
        try
        {
            yield return StowBarrier(id);
            yield return Assemble(id);
            yield return PowerOn(id);
            yield return SpinUp(id);
            if (!Running(id)) yield break;
            // 마지막에 전체가 한 번 번쩍이고 확정한다.
            foreach (var part in lit) flashes[part.Renderer] = 1f;
            float t = 0f;
            while (t < finishHold && Running(id)) { t += Time.deltaTime; Decay(); yield return null; }
        }
        finally
        {
            if (run == id) Finish();
        }
    }

    // 도중에 끝났거나(OnDisable) 밖에서 전원을 켰으면 멈춘다.
    private bool Running(int id) => IsPlaying && run == id && station != null && !station.IsPowered;

    // 빛나는 부품마다 그 불을 켤 박자를 정한다(부품을 품은 가장 깊은 경로의 박자).
    private void Collect()
    {
        lit.Clear();
        flashes.Clear();
        int count = cues != null ? cues.Length : 0;
        var targets = new Transform[count];
        for (int i = 0; i < count; i++)
            targets[i] = string.IsNullOrEmpty(cues[i].targetPath) ? null : station.transform.Find(cues[i].targetPath);
        for (int i = 0; i < station.LitCount; i++)
        {
            var r = station.LitRenderer(i);
            var m = station.LitMaterial(i);
            if (r == null || m == null) continue;
            var part = new LitPart { Renderer = r, Lit = m, BaseIntensity = m.HasProperty(IntensityId) ? m.GetFloat(IntensityId) : 1f };
            int depth = -1;
            for (int c = 0; c < count; c++)
            {
                var target = targets[c];
                if (target == null || !r.transform.IsChildOf(target)) continue;
                int d = Depth(target);
                if (d > depth) { depth = d; part.Cue = c; }
            }
            lit.Add(part);
        }
    }

    private int Depth(Transform target)
    {
        int depth = 0;
        for (var t = target; t != null && t != station.transform; t = t.parent) depth++;
        return depth;
    }

    // 접근 불가 푯말: 판이 기둥을 따라 미끄러져 내려앉아 철컥 걸리고, 전원이 끊긴 표지처럼 깜빡이다 꺼진다(크기는 바꾸지 않는다).
    private IEnumerator StowBarrier(int id)
    {
        var barrier = station.Barrier;
        if (barrier == null || !barrier.activeInHierarchy) yield break;
        var root = barrier.transform;
        board.Clear();
        barrierRenderers.Clear();
        float bottom = float.MaxValue;
        foreach (var r in barrier.GetComponentsInChildren<Renderer>())
        {
            barrierRenderers.Add((r, r.enabled));
            float low = r.bounds.min.y - root.position.y;
            if (r.transform.parent != root || low < BoardFrom) continue;
            board.Add((r.transform, r.transform.localPosition));
            bottom = Mathf.Min(bottom, low);
        }
        float drop = board.Count > 0 ? Mathf.Max(0f, bottom - BoardRest) : 0f;
        bool landed = false;
        float t = 0f;
        while (t < barrierStowTime)
        {
            if (!Running(id)) yield break;
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / barrierStowTime);
            float fall = k < .6f ? (k / .6f) * (k / .6f) : 1f;
            float bounce = k >= .6f && k < .8f ? Mathf.Sin((k - .6f) / .2f * Mathf.PI) * .06f : 0f;
            foreach (var (part, home) in board) part.localPosition = home + Vector3.down * (drop * fall - bounce);
            if (!landed && k >= .6f) { landed = true; PlaySound(lockSound); }
            bool on = k < .75f || Mathf.Repeat(t * 24f, 1f) < .45f;
            foreach (var (r, enabled) in barrierRenderers) if (r != null) r.enabled = enabled && on;
            yield return null;
        }
        barrier.SetActive(false);
        RestoreBarrier();
    }

    private void RestoreBarrier()
    {
        foreach (var (part, home) in board) if (part != null) part.localPosition = home;
        foreach (var (r, enabled) in barrierRenderers) if (r != null) r.enabled = enabled;
        board.Clear();
        barrierRenderers.Clear();
    }

    // 조립 Clip을 실제 시간으로 적용한다(Animator는 끈 채). 박자 시각이 지나면 소리를 내고 그 부품의 불을 켠다.
    private IEnumerator Assemble(int id)
    {
        if (!Running(id)) yield break;
        var clip = station.AssembleClip;
        int count = cues != null ? cues.Length : 0;
        var order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        Array.Sort(order, (a, b) => cues[a].time != cues[b].time ? cues[a].time.CompareTo(cues[b].time) : a.CompareTo(b));
        int next = 0;
        if (clip != null)
        {
            var animator = station.Animator;
            if (animator != null) animator.enabled = false;
            float t = 0f;
            while (true)
            {
                if (!Running(id)) yield break;
                bool done = t >= clip.length;
                AssembleTime = t;
                clip.SampleAnimation(station.gameObject, t);
                station.NotifyAssemblyPose(done);
                while (next < count && cues[order[next]].time <= t) Fire(order[next++]);
                Decay();
                if (done) break;
                yield return null;
                t = Mathf.Min(t + Time.deltaTime, clip.length);
            }
        }
        while (next < count) Fire(order[next++]);
        AssembleTime = -1f;
    }

    private void Fire(int index)
    {
        var cue = cues[index];
        switch (cue.kind)
        {
            case CueKind.Move: PlaySound(moveSound); break;
            case CueKind.Lock: PlaySound(lockSound); break;
            case CueKind.Bolt: PlaySound(boltSound); break;
        }
        foreach (var part in lit) if (part.Cue == index && !part.On) LightUp(part);
    }

    // 전원이 아래에서 위로 들어온다: 지나간 부품이 한 번씩 번쩍이고, 아직 꺼져 있던 부품은 여기서 켜진다.
    private IEnumerator PowerOn(int id)
    {
        if (!Running(id)) yield break;
        PlaySound(unlockSound);
        if (lit.Count == 0) yield break;
        var order = new List<(LitPart part, float y)>();
        float min = float.MaxValue, max = float.MinValue;
        foreach (var part in lit)
        {
            var bounds = part.Renderer.bounds;
            order.Add((part, bounds.center.y));
            min = Mathf.Min(min, bounds.min.y);
            max = Mathf.Max(max, bounds.max.y);
        }
        order.Sort((a, b) => a.y.CompareTo(b.y));
        int passed = 0;
        float t = 0f;
        while (t < powerTime)
        {
            if (!Running(id)) yield break;
            t += Time.deltaTime;
            float front = Mathf.Lerp(min - .2f, max + .2f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / powerTime)));
            while (passed < order.Count && order[passed].y <= front)
            {
                var part = order[passed++].part;
                if (!part.On) LightUp(part);
                else flashes[part.Renderer] = Mathf.Max(flashes.TryGetValue(part.Renderer, out float f) ? f : 0f, .8f);
            }
            Decay();
            yield return null;
        }
        foreach (var part in lit) if (!part.On) LightUp(part);
    }

    private IEnumerator SpinUp(int id)
    {
        if (!Running(id)) yield break;
        var animator = station.Animator;
        if (animator == null) yield break;
        animator.enabled = true;
        animator.speed = 0f;
        float t = 0f;
        while (t < spinUpTime)
        {
            if (!Running(id)) yield break;
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / spinUpTime);
            animator.speed = k * k;
            Decay();
            yield return null;
        }
        animator.speed = 1f;
    }

    private void LightUp(LitPart part)
    {
        if (part.Renderer == null) return;
        part.Renderer.sharedMaterial = part.Lit;
        part.Renderer.enabled = true;
        part.On = true;
        flashes[part.Renderer] = 1f;
        LitSoFar++;
    }

    private void Decay()
    {
        float step = Time.deltaTime / flashDecay;
        foreach (var part in lit)
        {
            if (part.Renderer == null || !flashes.TryGetValue(part.Renderer, out float f)) continue;
            f = Mathf.Max(0f, f - step);
            flashes[part.Renderer] = f;
            part.Renderer.GetPropertyBlock(block);
            block.SetFloat(IntensityId, part.BaseIntensity * (1f + (flashGain - 1f) * f));
            part.Renderer.SetPropertyBlock(block);
        }
    }

    private void PlaySound(SoundOptions sound)
    {
        if (sound != null && sound.clip != null && SoundManager.Instance != null) SoundManager.Instance.PlaySound(sound, Focus.position);
    }

    private void Finish()
    {
        if (!IsPlaying) return;
        IsPlaying = false;
        AssembleTime = -1f;
        RestoreBarrier();
        foreach (var part in lit)
            if (part.Renderer != null) { part.Renderer.enabled = true; part.Renderer.SetPropertyBlock(null); }
        flashes.Clear();
        if (station == null) return;
        if (station.Animator != null) station.Animator.speed = 1f;
        // 조립 도중에 멈췄으면 LobbyStation이 조립 완료 자세로 되돌린다(NotifyAssemblyPose).
        station.SetPowered(true);
    }
}
