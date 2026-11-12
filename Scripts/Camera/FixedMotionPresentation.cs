using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.LowLevel;

// 카메라 계산 중에만 표시용 target을 전달하고 게임에서 읽는 Follow 참조는 원래대로 유지한다.
[DefaultExecutionOrder(99)]
public sealed class FixedMotionPresentation : MonoBehaviour
{
    private static FixedMotionPresentation instance;
    private readonly List<FixedMotionBody> bodies = new();
    [SerializeField] private List<BrainSettings> brains = new();
    private readonly Dictionary<Transform, Transform> targets = new();
    private readonly List<(CinemachineCamera camera, CameraTarget target)> cameras = new();
    private bool drawing;
    private readonly List<Camera> drawingCameras = new();
    private sealed class PostFixedMotionSample { }
    [Serializable]
    private struct BrainSettings
    {
        public CinemachineBrain brain;
        public CinemachineBrain.UpdateMethods update;
        public CinemachineBrain.BrainUpdateMethods blend;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;
    internal static void Register(FixedMotionBody body)
    {
        if (instance == null)
        {
            instance = FindFirstObjectByType<FixedMotionPresentation>(FindObjectsInactive.Include);
            if (instance == null)
            {
                var node = new GameObject("FixedMotionPresentation");
                DontDestroyOnLoad(node);
                instance = node.AddComponent<FixedMotionPresentation>();
            }
        }
        if (!instance.bodies.Contains(body)) instance.bodies.Add(body);
    }
    internal static void Unregister(FixedMotionBody body)
    {
        if (instance != null) instance.bodies.Remove(body);
    }
    private void OnEnable()
    {
        instance = this;
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
        SetSamplingLoop(true);
    }
    private static void SetSamplingLoop(bool enabled)
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        UpdateLoop(ref loop, enabled);
        PlayerLoop.SetPlayerLoop(loop);
    }
    private static void UpdateLoop(ref PlayerLoopSystem system, bool enabled)
    {
        if (system.subSystemList == null) return;
        if (system.type == typeof(UnityEngine.PlayerLoop.FixedUpdate))
        {
            var children = new List<PlayerLoopSystem>(system.subSystemList);
            children.RemoveAll(item => item.type == typeof(PostFixedMotionSample));
            if (enabled) children.Add(new PlayerLoopSystem { type = typeof(PostFixedMotionSample), updateDelegate = SamplePhysics });
            system.subSystemList = children.ToArray();
            return;
        }
        for (int i = 0; i < system.subSystemList.Length; i++) UpdateLoop(ref system.subSystemList[i], enabled);
    }
    private static void SamplePhysics()
    {
        if (instance == null || !instance.isActiveAndEnabled) return;
        foreach (var body in instance.bodies) if (body != null) body.SamplePhysics();
    }
    private Transform PresentationTarget(Transform target)
    {
        if (target == null) return null;
        var body = target.GetComponentInParent<FixedMotionBody>();
        if (body == null || !body.isActiveAndEnabled) return target;
        if (!targets.TryGetValue(target, out var proxy) || proxy == null)
        {
            proxy = new GameObject("MotionTarget_" + target.name).transform;
            proxy.SetParent(transform, false);
            targets[target] = proxy;
        }
        FixedMotionBody.GetPresentationPose(target, out var position, out var rotation);
        proxy.SetPositionAndRotation(position, rotation);
        return proxy;
    }
    private void LateUpdate()
    {
        foreach (var body in bodies) if (body != null) body.PrepareFrame();
        // 활성 카메라 목록은 Cinemachine이 보관한다. 대규모 방 전체를 매 프레임 검색하지 않는다.
        cameras.Clear();
        try
        {
            for (int i = 0; i < CinemachineCore.VirtualCameraCount; i++)
            {
                if (!(CinemachineCore.GetVirtualCamera(i) is CinemachineCamera camera)) continue;
                var saved = camera.Target;
                var replaced = saved;
                replaced.TrackingTarget = PresentationTarget(saved.TrackingTarget);
                if (saved.CustomLookAtTarget) replaced.LookAtTarget = PresentationTarget(saved.LookAtTarget);
                cameras.Add((camera, saved));
                camera.Target = replaced;
            }
            for (int i = 0; i < CinemachineBrain.ActiveBrainCount; i++)
            {
                var brain = CinemachineBrain.GetActiveBrain(i);
                if (brain == null || !brain.isActiveAndEnabled) continue;
                bool saved = false;
                foreach (var settings in brains) if (settings.brain == brain) { saved = true; break; }
                if (!saved) brains.Add(new BrainSettings { brain = brain, update = brain.UpdateMethod, blend = brain.BlendUpdateMethod });
                brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
                brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
                brain.ManualUpdate();
            }
        }
        finally
        {
            foreach (var saved in cameras) if (saved.camera != null) saved.camera.Target = saved.target;
        }
    }
    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null || camera.cameraType != CameraType.Game) return;
        drawingCameras.Add(camera);
        if (drawing) return;
        drawing = true;
        try { foreach (var body in bodies) if (body != null) body.DrawGraphics(); }
        catch { Restore(); throw; }
    }
    private void EndCamera(ScriptableRenderContext context, Camera camera)
    {
        int index = drawingCameras.LastIndexOf(camera);
        if (index < 0) return;
        drawingCameras.RemoveAt(index);
        if (drawingCameras.Count == 0) Restore();
    }
    private void Restore()
    {
        if (!drawing) return;
        drawing = false;
        drawingCameras.Clear();
        foreach (var body in bodies) if (body != null) body.RestoreGraphics();
    }
    private void OnDisable()
    {
        Restore();
        SetSamplingLoop(false);
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        foreach (var item in brains)
            if (item.brain != null) { item.brain.UpdateMethod = item.update; item.brain.BlendUpdateMethod = item.blend; }
    }
}
