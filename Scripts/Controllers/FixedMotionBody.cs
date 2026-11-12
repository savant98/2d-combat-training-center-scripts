using System.Collections.Generic;
using UnityEngine;

// 실제 Transform과 Collider는 고정 틱에 두고, 화면용 포즈만 이전 두 물리 결과 사이에서 계산한다.
[DisallowMultipleComponent]
public sealed class FixedMotionBody : MonoBehaviour
{
    private Vector3 previousPosition, currentPosition, previousScale, currentScale;
    private Quaternion previousRotation, currentRotation;
    private double sampleTime;
    private double sampleStep = .01;
    private readonly List<MotionRendererBinding> graphics = new();
    private bool initialized;
    private bool hasPresented;
    public Vector3 PresentationPosition { get; private set; }
    public Quaternion PresentationRotation { get; private set; }
    public Vector3 PresentationScale { get; private set; }
    public float InterpolationAlpha { get; private set; }

    public static void GetPresentationPose(Transform target, out Vector3 position, out Quaternion rotation)
    {
        position = target.position; rotation = target.rotation;
        var body = target.GetComponentInParent<FixedMotionBody>();
        if (body == null || !body.isActiveAndEnabled) return;
        var turn = body.PresentationRotation * Quaternion.Inverse(body.transform.rotation);
        position = body.PresentationPosition + turn * (position - body.transform.position);
        rotation = turn * rotation;
    }

    public static FixedMotionBody Attach(Transform root)
    {
        if (!Application.isPlaying || root == null) return null;
        return root.TryGetComponent<FixedMotionBody>(out var body) ? body : root.gameObject.AddComponent<FixedMotionBody>();
    }
    private void OnEnable()
    {
        ResetPose();
        FixedMotionPresentation.Register(this);
    }
    private void OnDisable()
    {
        RestoreGraphics();
        FixedMotionPresentation.Unregister(this);
    }
    private void OnDestroy()
    {
        foreach (var binding in graphics) binding.Dispose();
        graphics.Clear();
    }
    public void ResetPose()
    {
        RestoreGraphics();
        previousPosition = currentPosition = PresentationPosition = transform.position;
        previousRotation = currentRotation = PresentationRotation = transform.rotation;
        previousScale = currentScale = PresentationScale = transform.localScale;
        sampleTime = Time.fixedTimeAsDouble;
        if (Time.fixedDeltaTime > 0) sampleStep = Time.fixedDeltaTime;
        InterpolationAlpha = 0;
        hasPresented = false;
    }
    public void RefreshGraphics()
    {
        RestoreGraphics();
        foreach (var binding in graphics) binding.Dispose();
        graphics.Clear(); initialized = false;
    }
    // 모든 FixedUpdate와 모터의 실제 Transform 반영이 끝난 뒤 공통 샘플러가 호출한다.
    internal void SamplePhysics()
    {
        if (Time.fixedTimeAsDouble == sampleTime) return;
        if (!hasPresented)
        {
            ResetPose();
            return;
        }
        previousPosition = currentPosition; currentPosition = transform.position;
        previousRotation = currentRotation; currentRotation = transform.rotation;
        previousScale = currentScale; currentScale = transform.localScale;
        sampleStep = System.Math.Max(.000001, Time.fixedTimeAsDouble - sampleTime);
        sampleTime = Time.fixedTimeAsDouble;
    }
    internal void PrepareFrame()
    {
        // Pool 배치·방 전환·Update 기반 연출은 이전 물리 위치에서 보간하지 않는다.
        if (transform.position != currentPosition) previousPosition = currentPosition = transform.position;
        if (transform.rotation != currentRotation) previousRotation = currentRotation = transform.rotation;
        if (transform.localScale != currentScale) previousScale = currentScale = transform.localScale;
        float alpha = Mathf.Clamp01((float)((Time.timeAsDouble - sampleTime) / sampleStep));
        InterpolationAlpha = alpha;
        PresentationPosition = Vector3.Lerp(previousPosition, currentPosition, alpha);
        PresentationRotation = Quaternion.Slerp(previousRotation, currentRotation, alpha);
        PresentationScale = Vector3.Lerp(previousScale, currentScale, alpha);
        hasPresented = true;
    }
    internal void DrawGraphics()
    {
        if (!initialized)
        {
            initialized = true;
            MotionRendererBinding.Collect(this, graphics);
        }
        var raw = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        if (Mathf.Abs(raw.determinant) < .0000001f) return;
        Vector3 scale = transform.lossyScale;
        var local = transform.localScale;
        if (Mathf.Abs(local.x) > .00001f) scale.x *= PresentationScale.x / local.x;
        if (Mathf.Abs(local.y) > .00001f) scale.y *= PresentationScale.y / local.y;
        if (Mathf.Abs(local.z) > .00001f) scale.z *= PresentationScale.z / local.z;
        var delta = Matrix4x4.TRS(PresentationPosition, PresentationRotation, scale) * raw.inverse;
        var rotation = PresentationRotation * Quaternion.Inverse(transform.rotation);
        foreach (var binding in graphics) binding.Apply(delta, rotation);
    }
    internal void RestoreGraphics()
    {
        foreach (var binding in graphics) binding.Restore();
    }
}
