using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// SelfDistortionSource 목록을 관리하고, 보이는 원천이 있을 때만 메인 카메라 렌더러에 자기 왜곡 패스를 넣는다.
// 공용 렌더 설정(Renderer2D.asset의 Renderer Feature)과 개체의 Layer는 바꾸지 않는다.
// 대상 Renderer는 메인 카메라를 그리는 동안만 평소 그리기에서 빼고(Renderer.forceRenderingOff, 끝나면 되돌린다)
// 패스가 따로 그린 뒤 굴절시켜 화면에 더한다. 다른 카메라(Scene 뷰, 물 반사, 미리보기)에서는 평소대로 그려진다.
public static class SelfDistortionRenderer
{
    public const int MaxSources = 2;

    private static readonly List<SelfDistortionSource> sources = new();
    private static readonly List<Renderer> hidden = new();
    private static SelfDistortionPass pass;
    private static bool subscribed;
    private static Camera hiddenCamera;

    public static int SourceCount => sources.Count;
    // 검증용: 패스를 기록한 누적 횟수, 마지막 카메라에서 보인 원천 수와 따로 그린 Renderer 수, 첫 원천의 매개변수(UV x·y, 반지름, 세기)
    public static int RecordedPassCount { get; internal set; }
    public static int LastVisibleCount { get; private set; }
    public static int LastDrawnCount => pass != null ? pass.DrawCount : 0;
    public static Vector4 LastCenter0 => pass != null ? pass.Center0 : Vector4.zero;
    /// <summary>검증용: 켜면 왜곡 세기와 상관없이 패스를 넣지 않는다(평소 그리기와 비교하려는 것).</summary>
    public static bool Suppressed { get; set; }

    // Domain Reload를 끈 Play 설정에서도 이전 Play의 구독·목록이 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        sources.Clear();
        SetSubscribed(false);
        Restore();
        RecordedPassCount = 0;
        LastVisibleCount = 0;
        Suppressed = false;
        pass?.Dispose();
        pass = null;
    }

    public static void Register(SelfDistortionSource source)
    {
        if (source == null || sources.Contains(source))
            return;

        sources.Add(source);
        SetSubscribed(true);
    }

    public static void Unregister(SelfDistortionSource source)
    {
        sources.Remove(source);
        if (sources.Count == 0)
        {
            SetSubscribed(false);
            Restore();
        }
    }

    private static void SetSubscribed(bool value)
    {
        if (subscribed == value)
            return;

        subscribed = value;
        if (value)
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }
        else
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        }
    }

    private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        Restore();
        // 게임 화면(메인 카메라)만 다룬다. Scene 뷰·미리보기·화면 안 모니터용 카메라는 평소대로 그린다.
        if (Suppressed || camera.cameraType != CameraType.Game || !camera.CompareTag("MainCamera"))
            return;

        var cameraData = camera.GetUniversalAdditionalCameraData();
        if (cameraData == null || cameraData.scriptableRenderer == null)
            return;

        pass ??= new SelfDistortionPass();
        LastVisibleCount = pass.Setup(camera, sources);
        if (LastVisibleCount == 0)
            return;

        // 패스가 따로 그릴 Renderer는 이 카메라의 평소 그리기에서 뺀다(같은 것이 두 번 더해지지 않게).
        pass.CollectRenderers(hidden);
        for (int i = 0; i < hidden.Count; i++)
            hidden[i].forceRenderingOff = true;
        hiddenCamera = camera;
        cameraData.scriptableRenderer.EnqueuePass(pass);
    }

    private static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera == hiddenCamera)
            Restore();
    }

    private static void Restore()
    {
        for (int i = 0; i < hidden.Count; i++)
            if (hidden[i] != null)
                hidden[i].forceRenderingOff = false;
        hidden.Clear();
        hiddenCamera = null;
    }
}
