using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// 물리 틱의 실제 포즈 사이를 렌더 순간에만 보간한다. Player와 공격·피격 Collider는 옮기지 않는다.
internal sealed class WaveCarryPresentation : IDisposable
{
    private readonly PlayerController target;
    private readonly WavePlayerContainment box;
    private readonly Camera view;
    private readonly Transform[] graphics;
    private readonly Vector3[] savedPositions;
    private Vector3 previousPosition, currentPosition;
    private Rect previousArea, currentArea;
    private double sampleTime;
    private bool applied, disposed;

    public WaveCarryPresentation(PlayerController player, WavePlayerContainment containment)
    {
        target = player; box = containment; view = Camera.main;
        // Collider가 있는 가지의 조상은 표시용 이동 대상에서 제외한다.
        var root = target.facingModel.transform;
        var physics = new HashSet<Transform>();
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (!(component is Collider2D) && !(component is Rigidbody2D)) continue;
            for (var node = component.transform; node != null && node.IsChildOf(root); node = node.parent) physics.Add(node);
        }
        var roots = new HashSet<Transform>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var node = renderer.transform;
            if (physics.Contains(node)) continue;
            while (node.parent != null && node.parent.IsChildOf(root) && !physics.Contains(node.parent)) node = node.parent;
            roots.Add(node);
        }
        graphics = roots.Where(node => !roots.Any(other => other != node && node.IsChildOf(other))).ToArray();
        savedPositions = new Vector3[graphics.Length];
        ResetPose();
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }

    public void ResetPose()
    {
        Restore();
        if (target == null) return;
        previousPosition = currentPosition = target.transform.position;
        previousArea = currentArea = target.Motor.Confinement;
        sampleTime = Time.fixedTimeAsDouble;
    }
    // WaitForFixedUpdate 뒤에서 부른다. 예약한 ShiftConfinement가 실제 Motor.Move에 반영된 포즈다.
    public void SamplePhysics()
    {
        if (disposed || target == null) return;
        double time = Time.fixedTimeAsDouble;
        if (time == sampleTime) return;
        previousPosition = currentPosition; currentPosition = target.transform.position;
        previousArea = currentArea; currentArea = target.Motor.Confinement;
        sampleTime = time;
    }
    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (disposed || camera != view || target == null || !target.Motor.IsConfined) return;
        Restore();
        float alpha = Mathf.Clamp01((float)((Time.timeAsDouble - sampleTime) / Time.fixedDeltaTime));
        var offset = Vector3.Lerp(previousPosition, currentPosition, alpha) - target.transform.position;
        for (int i = 0; i < graphics.Length; i++)
        {
            var node = graphics[i];
            if (node == null) continue;
            savedPositions[i] = node.localPosition;
            node.localPosition += node.parent != null ? node.parent.InverseTransformVector(offset) : offset;
        }
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
        for (int i = 0; i < graphics.Length; i++) if (graphics[i] != null) graphics[i].localPosition = savedPositions[i];
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
