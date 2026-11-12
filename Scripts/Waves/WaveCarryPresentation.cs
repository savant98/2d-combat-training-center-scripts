using System;
using UnityEngine;
using UnityEngine.Rendering;

// 공통 FixedMotionBody와 같은 물리 틱 사이에서 구속 상자 표시만 보간한다.
internal sealed class WaveCarryPresentation : IDisposable
{
    private readonly PlayerController target;
    private readonly WavePlayerContainment box;
    private readonly Camera view;
    private readonly FixedMotionBody motion;
    private Rect previousArea, currentArea;
    private double sampleTime;
    private bool applied, disposed;

    public WaveCarryPresentation(PlayerController player, WavePlayerContainment containment)
    {
        target = player; box = containment; view = Camera.main;
        motion = FixedMotionBody.Attach(player.transform);
        ResetPose();
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }

    public void ResetPose()
    {
        Restore();
        if (target == null) return;
        previousArea = currentArea = target.Motor.Confinement;
        sampleTime = Time.fixedTimeAsDouble;
    }
    // WaitForFixedUpdate 뒤에서 부른다. 예약한 ShiftConfinement가 실제 Motor.Move에 반영된 포즈다.
    public void SamplePhysics()
    {
        if (disposed || target == null) return;
        double time = Time.fixedTimeAsDouble;
        if (time == sampleTime) return;
        previousArea = currentArea; currentArea = target.Motor.Confinement;
        sampleTime = time;
    }
    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (disposed || camera != view || target == null || !target.Motor.IsConfined) return;
        Restore();
        float alpha = motion != null ? motion.InterpolationAlpha : 1f;
        applied = true;
        if (box != null && box.IsEngaged && box.Target == target)
            box.SetCarryRenderArea(new Rect(Vector2.Lerp(previousArea.position, currentArea.position, alpha),
                Vector2.Lerp(previousArea.size, currentArea.size, alpha)));
    }
    private void EndCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera == view) Restore();
    }
    private void Restore()
    {
        if (!applied) return;
        applied = false;
        if (box != null) box.SetCarryRenderArea(null);
    }
    public void Dispose()
    {
        if (disposed) return;
        Restore(); disposed = true;
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
    }
}
