using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 환경 장치의 시각 부품만 구동하며 풀 재사용 때 기준 자세와 케이블 속성을 복구한다.
/// 6차 T9: 방 클리어로 멈춘 함정은 Frozen으로 기어·배선 흐름을 세운다(자세는 마지막 시간 그대로). 감속해 세우는 장치(톱날)는
/// Seek로 시간을 직접 정해 같은 식(기준 자세 × 시간 × 속도)으로 기어를 놓는다. 꺼지거나 풀에서 다시 꺼내면 Frozen은 풀린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class WaveMachineMotion : MonoBehaviour
{
    public Transform[] gears = Array.Empty<Transform>();
    public float[] gearSpeeds = Array.Empty<float>();
    public Renderer[] cables = Array.Empty<Renderer>();
    public Vector2 scrollVelocity = new Vector2(-.35f, 0f);

    public float MotionTime { get; private set; }
    public Vector2 CableOffset { get; private set; }
    /// <summary>true면 Update가 시간을 진행하지 않는다(기어·배선 흐름 정지). OnEnable·Restore에서 false로 돌아간다.</summary>
    public bool Frozen { get; set; }

    private static readonly int ScrollOffsetId = Shader.PropertyToID("_ScrollOffset");
    private Transform[] boundGears = Array.Empty<Transform>();
    private Quaternion[] restRotations = Array.Empty<Quaternion>();
    private readonly List<CableState> cableStates = new List<CableState>();
    private bool bound;
    private WaveEnvironmentLifecycle lifecycle;

    private sealed class CableState
    {
        public Renderer Renderer;
        public MaterialPropertyBlock Original;
        public MaterialPropertyBlock Animated;
        public Vector4 BaseOffset;
        public bool HadProperties;
    }

    /// <summary>편집기 생성 도구나 실행 중 조립한 시각 부품을 연결한다.</summary>
    public void Configure(Transform[] visualGears, float[] degreesPerSecond,
        Renderer[] cableRenderers, Vector2 velocity)
    {
        Restore();
        gears = visualGears ?? Array.Empty<Transform>();
        gearSpeeds = degreesPerSecond ?? Array.Empty<float>();
        cables = cableRenderers ?? Array.Empty<Renderer>();
        scrollVelocity = velocity;
        if (Application.isPlaying && isActiveAndEnabled) Bind();
    }

    private void OnEnable()
    {
        if (Application.isPlaying) Bind();
    }

    private void Bind()
    {
        if (bound) Restore();
        MotionTime = 0f;
        CableOffset = Vector2.zero;
        lifecycle = GetComponent<WaveEnvironmentLifecycle>();
        boundGears = gears != null ? (Transform[])gears.Clone() : Array.Empty<Transform>();
        restRotations = new Quaternion[boundGears.Length];
        for (int i = 0; i < boundGears.Length; i++)
        {
            var gear = boundGears[i];
            // 장치 루트나 물리 부품은 시각 애니메이션 대상으로 받지 않는다.
            if (gear == null || gear == transform || !gear.IsChildOf(transform)
                || gear.GetComponentInChildren<Collider2D>(true) != null)
            {
                boundGears[i] = null;
                continue;
            }
            restRotations[i] = gear.localRotation;
        }

        var seen = new HashSet<Renderer>();
        foreach (var renderer in cables ?? Array.Empty<Renderer>())
        {
            if (renderer == null || !seen.Add(renderer)
                || !renderer.transform.IsChildOf(transform)) continue;
            var material = renderer.sharedMaterial;
            if (material == null || !material.HasProperty(ScrollOffsetId)) continue;
            var original = new MaterialPropertyBlock();
            var animated = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(original);
            renderer.GetPropertyBlock(animated);
            cableStates.Add(new CableState
            {
                Renderer = renderer,
                Original = original,
                Animated = animated,
                HadProperties = !original.isEmpty,
                BaseOffset = original.HasProperty(ScrollOffsetId)
                    ? original.GetVector(ScrollOffsetId) : material.GetVector(ScrollOffsetId)
            });
        }
        bound = true;
        ApplyCables();
    }

    private void Update()
    {
        if (!bound || Frozen || (lifecycle != null && !lifecycle.IsReady)) return;
        float delta = Time.deltaTime;
        if (delta <= 0f) return;
        Advance(delta);
    }

    /// <summary>
    /// 시간을 motionTime으로 옮기고 기어·배선 흐름을 그 시간에 맞춰 놓는다(6차 T9 — 감속 정지). 조금 되돌아가는 정지 자세도 받는다.
    /// Frozen이어도 놓는다(정지 감속은 Frozen을 켠 채 이것으로 움직인다).
    /// </summary>
    public void Seek(float motionTime)
    {
        if (!bound) return;
        float delta = motionTime - MotionTime;
        if (Mathf.Abs(delta) < 1e-6f) return;
        Advance(delta);
    }

    private void Advance(float delta)
    {
        MotionTime += delta;
        for (int i = 0; i < boundGears.Length; i++)
            if (boundGears[i] != null)
            {
                float speed = gearSpeeds != null && i < gearSpeeds.Length ? gearSpeeds[i] : 0f;
                boundGears[i].localRotation = restRotations[i]
                    * Quaternion.Euler(0f, 0f, Mathf.Repeat(MotionTime * speed, 360f));
            }
        CableOffset = new Vector2(
            Mathf.Repeat(CableOffset.x + scrollVelocity.x * delta, 1f),
            Mathf.Repeat(CableOffset.y + scrollVelocity.y * delta, 1f));
        ApplyCables();
    }

    private void ApplyCables()
    {
        foreach (var cable in cableStates)
        {
            if (cable.Renderer == null) continue;
            var offset = cable.BaseOffset + new Vector4(CableOffset.x, CableOffset.y, 0f, 0f);
            cable.Animated.SetVector(ScrollOffsetId, offset);
            // 공유 Material 에셋과 다른 장치의 흐름에는 쓰지 않는다.
            cable.Renderer.SetPropertyBlock(cable.Animated);
        }
    }

    private void OnDisable() => Restore();
    private void OnDestroy() => Restore();

    private void Restore()
    {
        if (bound)
        {
            for (int i = 0; i < boundGears.Length; i++)
                if (boundGears[i] != null) boundGears[i].localRotation = restRotations[i];
            foreach (var cable in cableStates)
                if (cable.Renderer != null)
                    cable.Renderer.SetPropertyBlock(cable.HadProperties ? cable.Original : null);
        }
        cableStates.Clear();
        boundGears = Array.Empty<Transform>();
        restRotations = Array.Empty<Quaternion>();
        bound = false;
        Frozen = false;
        MotionTime = 0f;
        CableOffset = Vector2.zero;
    }
}
