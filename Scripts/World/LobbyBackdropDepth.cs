using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>로비의 각 후경을 저해상도로 렌더링하고 깊이별 흐림과 명도로 합성한다.</summary>
[DisallowMultipleComponent]
public class LobbyBackdropDepth : MonoBehaviour
{
    [Serializable]
    private class DepthBand
    {
        public Transform Root;
        public Transform[] AdditionalRoots;
        public string CaptureLayer;
        [Range(.2f, 1f)] public float Resolution = .5f;
        [Range(0f, 12f)] public float BlurPixels = 2f;
        [Range(0f, 1f)] public float Brightness = .6f;
        public Color Tint = Color.white;
    }

    [SerializeField] private Shader depthShader;
    [SerializeField] private DepthBand[] bands;

    private class BandResources
    {
        public DepthBand Settings;
        public Camera Camera;
        public MeshRenderer Surface;
        public Material Material;
        public RenderTexture Raw;
        public RenderTexture Blurred;
        public Transform[] Objects;
        public int[] OriginalLayers;
    }

    private BandResources[] resources;
    private Camera mainCamera;
    private int originalMask;
    private int captureMask;
    private Mesh surfaceMesh;

    private void OnEnable()
    {
        if (!Application.isPlaying || depthShader == null || !depthShader.isSupported || bands == null) return;
        mainCamera = Camera.main;
        if (mainCamera == null || !mainCamera.orthographic) return;

        // 플레이 카메라만 후경 원본을 제외한다. Scene 편집 화면에서는 원본 설비를 직접 조정한다.
        originalMask = mainCamera.cullingMask;
        surfaceMesh = CreateSurfaceMesh();
        resources = new BandResources[bands.Length];
        for (int i = 0; i < bands.Length; i++)
        {
            var settings = bands[i];
            int layer = LayerMask.NameToLayer(settings.CaptureLayer);
            if (settings.Root == null || layer < 0) continue;
            captureMask |= 1 << layer;
            var resource = new BandResources { Settings = settings };
            resources[i] = resource;
            var objects = new HashSet<Transform>(settings.Root.GetComponentsInChildren<Transform>(true));
            if (settings.AdditionalRoots != null)
                foreach (var extra in settings.AdditionalRoots)
                    if (extra != null) objects.UnionWith(extra.GetComponentsInChildren<Transform>(true));
            resource.Objects = new Transform[objects.Count];
            objects.CopyTo(resource.Objects);
            resource.OriginalLayers = new int[resource.Objects.Length];
            for (int j = 0; j < resource.Objects.Length; j++)
            {
                resource.OriginalLayers[j] = resource.Objects[j].gameObject.layer;
                resource.Objects[j].gameObject.layer = layer;
            }

            var capture = new GameObject("LobbyDepthCamera_" + settings.Root.name);
            capture.hideFlags = HideFlags.HideAndDontSave;
            resource.Camera = capture.AddComponent<Camera>();
            var cameraData = capture.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;
            cameraData.requiresColorOption = CameraOverrideOption.Off;
            cameraData.requiresDepthOption = CameraOverrideOption.Off;
            cameraData.volumeLayerMask = 0;
            resource.Camera.cullingMask = 1 << layer;
            resource.Camera.depth = mainCamera.depth - bands.Length + i - 1;
            resource.Camera.clearFlags = CameraClearFlags.SolidColor;
            resource.Camera.backgroundColor = Color.clear;
            resource.Camera.allowHDR = true;
            resource.Camera.allowMSAA = false;
            resource.Camera.useOcclusionCulling = false;

            resource.Material = new Material(depthShader) { hideFlags = HideFlags.HideAndDontSave };
            var surface = new GameObject("LobbyDepthSurface_" + settings.Root.name);
            surface.hideFlags = HideFlags.HideAndDontSave;
            surface.layer = gameObject.layer;
            surface.AddComponent<MeshFilter>().sharedMesh = surfaceMesh;
            resource.Surface = surface.AddComponent<MeshRenderer>();
            resource.Surface.sharedMaterial = resource.Material;
            resource.Surface.sortingOrder = -5;
            resource.Surface.shadowCastingMode = ShadowCastingMode.Off;
            resource.Surface.receiveShadows = false;
            resource.Surface.lightProbeUsage = LightProbeUsage.Off;
            resource.Surface.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        mainCamera.cullingMask &= ~captureMask;
        RenderPipelineManager.beginCameraRendering += PrepareCamera;
        RenderPipelineManager.endCameraRendering += BlurCamera;
        LateUpdate();
    }

    private void LateUpdate()
    {
        if (resources == null || mainCamera == null) return;
        foreach (var resource in resources)
            if (resource != null) Synchronize(resource);
    }

    private void PrepareCamera(ScriptableRenderContext context, Camera camera)
    {
        if (resources == null || mainCamera == null) return;
        // Cinemachine과 픽셀 스냅 이후의 카메라 위치를 캡처와 합성 면에 함께 반영한다.
        if (camera == mainCamera)
        {
            foreach (var resource in resources)
                if (resource != null) PositionSurface(resource);
            return;
        }
        foreach (var resource in resources)
            if (resource != null && resource.Camera == camera) Synchronize(resource);
    }

    private void Synchronize(BandResources resource)
    {
        int width = Mathf.Max(16, Mathf.RoundToInt(mainCamera.pixelWidth * resource.Settings.Resolution));
        int height = Mathf.Max(16, Mathf.RoundToInt(mainCamera.pixelHeight * resource.Settings.Resolution));
        bool requiresBlur = resource.Settings.BlurPixels > .01f;
        if (resource.Raw == null || resource.Raw.width != width || resource.Raw.height != height)
        {
            resource.Camera.targetTexture = null;
            ReleaseTexture(resource.Raw);
            ReleaseTexture(resource.Blurred);
            resource.Raw = CreateTexture(width, height, 24, "LobbyDepthRaw");
            resource.Blurred = null;
            resource.Camera.targetTexture = resource.Raw;
        }
        if (requiresBlur && resource.Blurred == null)
            resource.Blurred = CreateTexture(width, height, 0, "LobbyDepthBlurred");
        else if (!requiresBlur && resource.Blurred != null)
        {
            ReleaseTexture(resource.Blurred);
            resource.Blurred = null;
        }
        resource.Material.mainTexture = requiresBlur ? resource.Blurred : resource.Raw;
        resource.Camera.transform.SetPositionAndRotation(mainCamera.transform.position, mainCamera.transform.rotation);
        resource.Camera.orthographic = true;
        resource.Camera.orthographicSize = mainCamera.orthographicSize;
        resource.Camera.aspect = mainCamera.aspect;
        resource.Camera.nearClipPlane = mainCamera.nearClipPlane;
        resource.Camera.farClipPlane = mainCamera.farClipPlane;
        resource.Material.SetFloat("_BlurRadius", resource.Settings.BlurPixels * height / 1080f);
        resource.Material.SetFloat("_Brightness", resource.Settings.Brightness);
        resource.Material.SetColor("_Tint", resource.Settings.Tint);
        PositionSurface(resource);
    }

    private void PositionSurface(BandResources resource)
    {
        var surface = resource.Surface.transform;
        Vector3 position = mainCamera.transform.position;
        position.z = resource.Settings.Root.position.z;
        surface.SetPositionAndRotation(position, mainCamera.transform.rotation);
        surface.localScale = new Vector3(mainCamera.orthographicSize * mainCamera.aspect * 2,
            mainCamera.orthographicSize * 2, 1);
    }

    private void BlurCamera(ScriptableRenderContext context, Camera camera)
    {
        if (resources == null) return;
        foreach (var resource in resources)
        {
            if (resource == null || resource.Camera != camera || resource.Raw == null || resource.Blurred == null) continue;
            var command = CommandBufferPool.Get("Lobby Gaussian Blur");
            command.Blit(resource.Raw, resource.Blurred, resource.Material, 0);
            context.ExecuteCommandBuffer(command);
            CommandBufferPool.Release(command);
            break;
        }
    }

    private static RenderTexture CreateTexture(int width, int height, int depth, string name)
    {
        var texture = new RenderTexture(width, height, depth, RenderTextureFormat.ARGBHalf)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.Create();
        return texture;
    }

    private static Mesh CreateSurfaceMesh()
    {
        var mesh = new Mesh { name = "LobbyDepthSurface", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(-.5f, .5f),
            new Vector3(.5f, .5f), new Vector3(.5f, -.5f) };
        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void ReleaseTexture(RenderTexture texture)
    {
        if (texture == null) return;
        texture.Release();
        Destroy(texture);
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= PrepareCamera;
        RenderPipelineManager.endCameraRendering -= BlurCamera;
        if (mainCamera != null) mainCamera.cullingMask = (mainCamera.cullingMask & ~captureMask) | (originalMask & captureMask);
        captureMask = 0;
        if (resources == null) return;
        foreach (var resource in resources)
        {
            if (resource == null) continue;
            for (int i = 0; i < resource.Objects.Length; i++)
                if (resource.Objects[i] != null) resource.Objects[i].gameObject.layer = resource.OriginalLayers[i];
            if (resource.Camera != null)
            {
                resource.Camera.enabled = false;
                resource.Camera.targetTexture = null;
                Destroy(resource.Camera.gameObject);
            }
            if (resource.Surface != null)
            {
                resource.Surface.enabled = false;
                Destroy(resource.Surface.gameObject);
            }
            ReleaseTexture(resource.Raw);
            ReleaseTexture(resource.Blurred);
            Destroy(resource.Material);
        }
        Destroy(surfaceMesh);
        resources = null;
        mainCamera = null;
    }
}
