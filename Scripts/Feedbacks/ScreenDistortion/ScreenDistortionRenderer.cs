using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// ScreenDistortionSource 목록을 관리하고, 보이는 원천이 있을 때만 메인 카메라 렌더러에 왜곡 패스를 넣는다.
// 공용 렌더 설정(Renderer2D.asset의 Renderer Feature·Camera Sorting Layer Texture)은 바꾸지 않는다.
// URP는 beginCameraRendering 뒤에 렌더러 패스 큐를 렌더링이 끝날 때만 비우므로, 여기서 EnqueuePass한 패스가
// 그 카메라의 Render Graph에 기록된다(2D 렌더러는 BeforeRenderingPostProcessing 시점, Bloom보다 먼저).
// 원천이 있는 동안 ProtectedLayer(TransparentFX)의 개체는 메인 컬링에서 잠시 빼고 왜곡 뒤에 따로 그려 일그러지지 않게 한다
// (카메라 컬링 마스크는 실행 중에만 바꾸고 endCameraRendering에서 되돌린다. Scene·에셋은 바꾸지 않는다).
public static class ScreenDistortionRenderer
{
    public const int MaxSources = 4;
    // 왜곡하지 않을 개체의 Layer. TransparentFX는 프로젝트에서 쓰지 않던 기본 Layer이고 메인 카메라가 그린다.
    // 이 Layer에는 Collider 없는 외형만 둔다(블랙홀 아래 장판, 낙하 큐브의 외형·바닥 경고).
    public const int ProtectedLayer = 1;
    public static int ProtectedLayerMask => 1 << ProtectedLayer;

    private static readonly List<ScreenDistortionSource> sources = new();
    private static ScreenDistortionPass pass;
    private static ScreenDistortionProtectedPass protectedPass;
    private static bool subscribed;
    private static Camera maskedCamera;
    private static int maskedCullingMask;

    public static int SourceCount => sources.Count;
    // 검증용: 패스를 기록한 누적 횟수, 마지막 카메라에서 보인 원천 수와 첫 원천의 매개변수(UV x·y, 반지름, 세기)
    public static int RecordedPassCount { get; internal set; }
    public static int ProtectedPassCount { get; internal set; }
    public static int LastVisibleCount { get; private set; }
    public static Vector4 LastCenter0 => pass != null ? pass.Center0 : Vector4.zero;
    public static Vector4 LastAxis => pass != null ? pass.Axis : Vector4.zero;

    // Domain Reload를 끈 Play 설정에서도 이전 Play의 구독·목록이 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        sources.Clear();
        SetSubscribed(false);
        RestoreCullingMask();
        RecordedPassCount = 0;
        ProtectedPassCount = 0;
        LastVisibleCount = 0;
        pass?.Dispose();
        pass = null;
        protectedPass = null;
    }

    public static void Register(ScreenDistortionSource source)
    {
        if (source == null || sources.Contains(source))
            return;

        sources.Add(source);
        SetSubscribed(true);
    }

    public static void Unregister(ScreenDistortionSource source)
    {
        sources.Remove(source);
        if (sources.Count == 0)
        {
            SetSubscribed(false);
            RestoreCullingMask();
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
        // 게임 화면(메인 카메라)만 왜곡한다. Scene 뷰·미리보기·화면 안 모니터용 카메라는 제외한다.
        if (camera.cameraType != CameraType.Game || !camera.CompareTag("MainCamera"))
            return;

        var cameraData = camera.GetUniversalAdditionalCameraData();
        if (cameraData == null || cameraData.scriptableRenderer == null)
            return;

        pass ??= new ScreenDistortionPass();
        protectedPass ??= new ScreenDistortionProtectedPass();
        LastVisibleCount = pass.Setup(camera, sources);
        if (LastVisibleCount > 0)
            cameraData.scriptableRenderer.EnqueuePass(pass);

        // 보호 Layer는 따로 컬링해 왜곡 다음에 그리고, 메인 컬링에서는 이 카메라를 그리는 동안만 뺀다.
        if ((camera.cullingMask & ProtectedLayerMask) != 0 && camera.TryGetCullingParameters(out var cullingParameters))
        {
            cullingParameters.cullingMask = (uint)ProtectedLayerMask;
            protectedPass.Setup(context.Cull(ref cullingParameters));
            RestoreCullingMask();
            maskedCamera = camera;
            maskedCullingMask = camera.cullingMask;
            camera.cullingMask &= ~ProtectedLayerMask;
            cameraData.scriptableRenderer.EnqueuePass(protectedPass);
        }
    }

    private static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera == maskedCamera)
            RestoreCullingMask();
    }

    private static void RestoreCullingMask()
    {
        if (maskedCamera != null)
            maskedCamera.cullingMask = maskedCullingMask;
        maskedCamera = null;
    }
}
