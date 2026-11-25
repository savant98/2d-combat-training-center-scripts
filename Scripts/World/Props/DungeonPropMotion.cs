using System;
using UnityEngine;

// Prop의 작은 공회전 연출(Animator 없이 가볍게): 기어 회전, 표시등 깜박임, 케이블 발광 흐름.
// - 모두 회전·Renderer 켜고 끄기·발광 밝기만 쓴다(크기 변형 없음 — 기계는 늘이지 않는다).
// - 화면 밖이면 멈춘다: visibilityProbes(비우면 표시 묶음의 Renderer 전부) 중 하나도 카메라에 보이지 않으면 갱신을 건너뛴다.
//   부서져 표시 묶음이 꺼지면 Renderer도 보이지 않으므로 함께 멈춘다.
// - 풀에서 다시 꺼내면 처음 자세·켜짐 상태로 돌아간다.
[DisallowMultipleComponent]
public sealed class DungeonPropMotion : MonoBehaviour, IDungeonPropPart
{
    [Serializable]
    public struct GearSpin
    {
        public Transform target;
        [Tooltip("초당 회전(도). 음수면 시계 방향.")]
        public float degreesPerSecond;
    }

    [Serializable]
    public struct Blinker
    {
        public Renderer renderer;
        [Min(0.05f)] public float period;
        [Range(0f, 1f), Tooltip("한 주기 중 켜져 있는 비율.")] public float onRatio;
        [Tooltip("주기 시작 위치(초).")] public float phase;
    }

    [Serializable]
    public struct FlowChain
    {
        [Tooltip("흐름 순서대로 놓은 발광 조각.")]
        public Renderer[] segments;
        [Tooltip("초당 지나가는 조각 수.")]
        public float speed;
        [Tooltip("빛 덩어리 폭(조각 수).")]
        [Min(0.1f)] public float width;
        [Tooltip("빛이 지나가지 않을 때의 밝기 배율.")]
        [Range(0f, 1f)] public float low;
        [Tooltip("빛이 지나갈 때의 밝기 배율.")]
        [Min(0f)] public float high;
    }

    [SerializeField] private GearSpin[] gears = Array.Empty<GearSpin>();
    [SerializeField] private Blinker[] blinkers = Array.Empty<Blinker>();
    [SerializeField] private FlowChain[] flows = Array.Empty<FlowChain>();
    [Tooltip("화면에 보이는지 확인할 Renderer. 비우면 DungeonProp 표시 묶음의 Renderer 전부.")]
    [SerializeField] private Renderer[] visibilityProbes = Array.Empty<Renderer>();

    private Quaternion[] gearHome = Array.Empty<Quaternion>();
    private bool[] blinkerHome = Array.Empty<bool>();
    private DungeonPropGlow[] flowGlows = Array.Empty<DungeonPropGlow>();
    private float clock;
    private bool cached;

    /// <summary>마지막 프레임에 화면에 보여 갱신했는지(검사용).</summary>
    public bool WasAnimating { get; private set; }

    private void Awake() => Cache();

    private void Cache()
    {
        if (cached) return;
        cached = true;
        gearHome = new Quaternion[gears.Length];
        for (int i = 0; i < gears.Length; i++) gearHome[i] = gears[i].target != null ? gears[i].target.localRotation : Quaternion.identity;
        blinkerHome = new bool[blinkers.Length];
        for (int i = 0; i < blinkers.Length; i++) blinkerHome[i] = blinkers[i].renderer != null && blinkers[i].renderer.enabled;
        flowGlows = new DungeonPropGlow[flows.Length];
        for (int i = 0; i < flows.Length; i++) flowGlows[i] = new DungeonPropGlow(flows[i].segments);
        if (visibilityProbes == null || visibilityProbes.Length == 0)
        {
            var prop = GetComponent<DungeonProp>();
            var root = prop != null && prop.VisualRoot != null ? prop.VisualRoot : transform;
            visibilityProbes = root.GetComponentsInChildren<Renderer>(true);
        }
    }

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        clock = 0f;
        for (int i = 0; i < gears.Length; i++) if (gears[i].target != null) gears[i].target.localRotation = gearHome[i];
        for (int i = 0; i < blinkers.Length; i++) if (blinkers[i].renderer != null) blinkers[i].renderer.enabled = blinkerHome[i];
        foreach (var glow in flowGlows) glow?.Clear();
    }

    public void OnPropReturning(DungeonProp prop)
    {
        foreach (var glow in flowGlows) glow?.Clear();
    }

    private bool Visible()
    {
        foreach (var probe in visibilityProbes)
            if (probe != null && probe.isVisible) return true;
        return false;
    }

    private void Update()
    {
        WasAnimating = false;
        if (!Visible()) return;
        WasAnimating = true;
        float dt = Time.deltaTime;
        clock += dt;
        for (int i = 0; i < gears.Length; i++)
        {
            var target = gears[i].target;
            if (target != null) target.localRotation *= Quaternion.Euler(0f, 0f, gears[i].degreesPerSecond * dt);
        }
        for (int i = 0; i < blinkers.Length; i++)
        {
            var b = blinkers[i];
            if (b.renderer == null || !blinkerHome[i]) continue;
            float period = Mathf.Max(0.05f, b.period);
            b.renderer.enabled = Mathf.Repeat(clock + b.phase, period) < period * Mathf.Clamp01(b.onRatio <= 0f ? 0.5f : b.onRatio);
        }
        for (int i = 0; i < flows.Length; i++)
        {
            var flow = flows[i];
            var glow = flowGlows[i];
            int count = flow.segments != null ? flow.segments.Length : 0;
            if (glow == null || count == 0) continue;
            float head = Mathf.Repeat(clock * flow.speed, count + flow.width);   // 빛 덩어리 위치(조각 순번, 끝을 지나 한 폭 쉬고 다시)
            float low = flow.low, high = flow.high <= 0f ? 1f : flow.high;
            for (int s = 0; s < count; s++)
            {
                float distance = Mathf.Abs(s - head);
                float k = Mathf.Clamp01(1f - distance / Mathf.Max(0.1f, flow.width));
                glow.SetOne(s, Mathf.Lerp(low, high, k * k * (3f - 2f * k)));
            }
        }
    }
}
