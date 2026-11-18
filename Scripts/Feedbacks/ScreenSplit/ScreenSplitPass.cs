using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// 현재 화면 색을 복사한 뒤 화면 가르기 Material(Hidden/2DCTC/ScreenSplit)로 원래 대상에 다시 그린다(2026-11-18).
// 블랙홀 왜곡(ScreenDistortionPass)과 같은 Render Graph 2단계(복사 → Blit)다. 같은 이유로 선의 화면 위치는 카메라 투영(뷰포트 좌표)을
// 그대로 Blit UV로 쓴다 — URP 17의 중간 렌더 텍스처는 아래가 V=0이라 UV와 뷰포트 좌표가 같고, GL.GetGPUProjectionMatrix를 쓰면 D3D에서 상하가 뒤집힌다.
// 길이는 화면 높이 비율로 넘기고, 셰이더는 (UV 차이 × (가로세로비, 1))로 화면 비율과 무관하게 월드 방향 그대로의 선을 만든다.
public sealed class ScreenSplitPass : ScriptableRenderPass
{
    private static readonly int LineId = Shader.PropertyToID("_SplitLine");
    private static readonly int ShapeId = Shader.PropertyToID("_SplitShape");
    private static readonly int AxisId = Shader.PropertyToID("_SplitAxis");

    private Material material;
    private Material templateSource;
    private Vector4 line;
    private Vector4 shape;
    private Vector4 axis;
    private bool ready;

    public Vector4 Line => line;
    public Vector4 Shape => shape;
    public float Aspect => axis.x;

    public ScreenSplitPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        requiresIntermediateTexture = true;
        profilingSampler = new ProfilingSampler("Screen Split");
    }

    // 이 카메라에서 화면이 실제로 갈라지면 true(선이 화면을 지나지 않거나 아무것도 벌어지지 않았으면 패스를 넣지 않는다).
    public bool Setup(Camera camera, Material template, in ScreenSplitState state)
    {
        ready = false;
        if (template == null || state.direction.sqrMagnitude < 1e-8f)
            return false;
        if (state.gap <= 0f && state.slide <= 0f && state.strength <= 0f)
            return false;

        Matrix4x4 viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
        float aspect = Mathf.Max(0.0001f, camera.aspect);
        Vector3 world = new Vector3(state.point.x, state.point.y, 0f);
        Vector3 ahead = world + new Vector3(state.direction.x, state.direction.y, 0f);
        if (!TryProject(viewProjection, world, out Vector2 uv) || !TryProject(viewProjection, ahead, out Vector2 uvAhead))
            return false;

        // 화면 방향(가로세로비를 곱한 공간). 직교 카메라는 월드 방향과 같다.
        Vector2 direction = new Vector2((uvAhead.x - uv.x) * aspect, uvAhead.y - uv.y);
        if (direction.sqrMagnitude < 1e-12f)
            return false;
        direction.Normalize();

        // 선이 화면(네 모서리)을 지나지 않으면 화면 전체가 한쪽으로 밀려 보이므로 넣지 않는다.
        Vector2 normal = new Vector2(-direction.y, direction.x);
        float reach = Mathf.Max(0f, state.gap) + Mathf.Max(0f, state.slide) + Mathf.Max(0f, state.jag);
        float s00 = Vector2.Dot(new Vector2((0f - uv.x) * aspect, 0f - uv.y), normal);
        float s10 = Vector2.Dot(new Vector2((1f - uv.x) * aspect, 0f - uv.y), normal);
        float s01 = Vector2.Dot(new Vector2((0f - uv.x) * aspect, 1f - uv.y), normal);
        float s11 = Vector2.Dot(new Vector2((1f - uv.x) * aspect, 1f - uv.y), normal);
        float min = Mathf.Min(Mathf.Min(s00, s10), Mathf.Min(s01, s11));
        float max = Mathf.Max(Mathf.Max(s00, s10), Mathf.Max(s01, s11));
        if (min > reach || max < -reach)
            return false;

        line = new Vector4(uv.x, uv.y, direction.x, direction.y);
        shape = new Vector4(Mathf.Max(0f, state.gap), Mathf.Max(0f, state.slide), Mathf.Max(0f, state.jag), Mathf.Clamp01(state.strength));
        axis = new Vector4(aspect, 1f, 0f, 0f);

        // 공용 Material 원본은 실행 중 바꾸지 않는다. 원본을 복사한 실행용 사본에만 값을 쓴다.
        if (material == null || templateSource != template)
        {
            CoreUtils.Destroy(material);
            material = new Material(template) { name = template.name + " (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            templateSource = template;
        }

        ready = true;
        return true;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (material == null || !ready)
            return;

        var resources = frameData.Get<UniversalResourceData>();
        if (resources.isActiveTargetBackBuffer)
            return;

        material.SetVector(LineId, line);
        material.SetVector(ShapeId, shape);
        material.SetVector(AxisId, axis);

        var desc = renderGraph.GetTextureDesc(resources.cameraColor);
        desc.name = "_ScreenSplitSource";
        desc.clearBuffer = false;
        TextureHandle copy = renderGraph.CreateTexture(desc);

        renderGraph.AddBlitPass(resources.activeColorTexture, copy, Vector2.one, Vector2.zero, passName: "Screen Split Copy");
        renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(copy, resources.activeColorTexture, material, 0),
            passName: "Screen Split");
        ScreenSplitRenderer.RecordedPassCount++;
    }

    public void Dispose()
    {
        CoreUtils.Destroy(material);
        material = null;
        templateSource = null;
        ready = false;
    }

    private static bool TryProject(Matrix4x4 viewProjection, Vector3 world, out Vector2 uv)
    {
        Vector4 clip = viewProjection * new Vector4(world.x, world.y, world.z, 1f);
        if (clip.w <= 0.0001f)
        {
            uv = Vector2.zero;
            return false;
        }

        uv = new Vector2(clip.x / clip.w * 0.5f + 0.5f, clip.y / clip.w * 0.5f + 0.5f);
        return true;
    }
}
