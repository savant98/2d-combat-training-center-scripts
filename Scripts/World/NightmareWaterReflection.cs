using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 실제 월드 화면을 별도 카메라로 받아 수면 아래에 합성한다. UI와 반사면은 재촬영하지 않는다.
[DisallowMultipleComponent]
public sealed class NightmareWaterReflection : MonoBehaviour
{
    [SerializeField] private MeshRenderer surface;
    [SerializeField, Range(.25f, 1f)] private float resolution = .75f;
    [SerializeField, Range(0f, 8f)] private float blurPixels = 1.4f;
    private Camera source, capture;
    private RenderTexture raw;
    private Material originalMaterial, reflectionMaterial;
    private Mesh originalMesh, runtimeMesh;
    private MeshFilter filter;
    private Vector3[] vertices;
    private Vector2[] reflectedUV;
    public int CapturedFrames { get; private set; }
    public int LastCaptureFrame { get; private set; } = -1;
    public int TextureAllocations { get; private set; }
    public float WaterY => transform.position.y;
    public Camera CaptureCamera => capture;
    public RenderTexture ReflectionTexture => raw;
    public float BlurPixels => blurPixels;

    private void OnEnable()
    {
        if (!Application.isPlaying || surface == null) return;
        source = Camera.main;
        if (source == null || !source.orthographic) return;
        originalMaterial = surface.sharedMaterial;
        filter = surface.GetComponent<MeshFilter>();
        originalMesh = filter.sharedMesh;
        runtimeMesh = Instantiate(originalMesh);
        runtimeMesh.name = "NightmareReflectionSurfaceRuntime";
        runtimeMesh.hideFlags = HideFlags.HideAndDontSave;
        filter.sharedMesh = runtimeMesh;
        vertices = runtimeMesh.vertices;
        reflectedUV = new Vector2[vertices.Length];
        reflectionMaterial = new Material(originalMaterial) { hideFlags = HideFlags.HideAndDontSave };
        surface.sharedMaterial = reflectionMaterial;
        surface.enabled = false;
        var cameraObject = new GameObject("NightmareReflectionCamera") { hideFlags = HideFlags.HideAndDontSave };
        capture = cameraObject.AddComponent<Camera>();
        capture.orthographic = true;
        capture.clearFlags = CameraClearFlags.SolidColor;
        capture.backgroundColor = Color.black;
        capture.allowHDR = true;
        capture.allowMSAA = false;
        capture.useOcclusionCulling = false;
        var data = capture.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = data.renderShadows = false;
        data.requiresColorOption = data.requiresDepthOption = CameraOverrideOption.Off;
        data.volumeLayerMask = 0;
        CapturedFrames = TextureAllocations = 0;
        LastCaptureFrame = -1;
        Synchronize();
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }

    private void LateUpdate() { if (capture != null && source != null) Synchronize(); }

    private void Synchronize()
    {
        // 물 레이어는 반사 카메라에서만 제외한다. Main의 후경 마스크와 출력 설정은 소유하지 않는다.
        capture.cullingMask = source.cullingMask & ~(1 << surface.gameObject.layer);
        capture.depth = source.depth - 1f;
        capture.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        capture.orthographicSize = source.orthographicSize;
        capture.aspect = source.aspect;
        capture.nearClipPlane = source.nearClipPlane;
        capture.farClipPlane = source.farClipPlane;
        int width = Mathf.Max(32, Mathf.RoundToInt(source.pixelWidth * resolution));
        int height = Mathf.Max(32, Mathf.RoundToInt(source.pixelHeight * resolution));
        if (raw == null || raw.width != width || raw.height != height)
        {
            capture.targetTexture = null;
            surface.enabled = false;
            Release(raw);
            raw = Texture(width, height, 24, "NightmareReflectionRaw");
            capture.targetTexture = raw;
            reflectionMaterial.SetTexture("_ReflectionTex", raw);
            TextureAllocations++;
        }
        reflectionMaterial.SetFloat("_BlurPixels", blurPixels * height / 1080f);
        reflectionMaterial.SetFloat("_WaterY", WaterY);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = surface.transform.TransformPoint(vertices[i]);
            world.y = 2f * WaterY - world.y;
            var uv = capture.WorldToViewportPoint(world);
            reflectedUV[i] = new Vector2(uv.x, uv.y);
        }
        runtimeMesh.uv = reflectedUV;
    }

    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera == capture && source != null) Synchronize();
    }

    private void EndCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != capture || raw == null) return;
        // RenderGraph의 카메라 종료 직후 Blit은 최종 Display 경로에서 빈 결과가 될 수 있다.
        // 완성된 원본 RT를 수면 Shader에서 직접 흐리게 표본화해 출력 경로 차이를 없앤다.
        CapturedFrames++;
        LastCaptureFrame = Time.frameCount;
        surface.enabled = true;
    }

    private static RenderTexture Texture(int width, int height, int depth, string name)
    {
        var texture = new RenderTexture(width, height, depth, RenderTextureFormat.ARGBHalf) {
            name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.Create(); return texture;
    }

    private static void Release(RenderTexture texture)
    { if (texture == null) return; texture.Release(); Destroy(texture); }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        if (capture != null) { capture.enabled = false; capture.targetTexture = null; Destroy(capture.gameObject); }
        if (surface != null && reflectionMaterial != null) { surface.sharedMaterial = originalMaterial; surface.enabled = true; }
        if (filter != null && runtimeMesh != null) filter.sharedMesh = originalMesh;
        Release(raw);
        if (runtimeMesh != null) Destroy(runtimeMesh);
        if (reflectionMaterial != null) Destroy(reflectionMaterial);
        raw = null; capture = source = null;
        runtimeMesh = null; reflectionMaterial = null;
    }
}
