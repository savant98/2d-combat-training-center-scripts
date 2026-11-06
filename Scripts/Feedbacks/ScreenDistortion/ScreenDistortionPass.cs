using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// 현재 화면 색을 복사한 뒤 왜곡 Material로 원래 대상에 다시 그린다(URP FullScreenPassRendererFeature의 Render Graph 경로와 같은 2단계).
// 원천의 화면 위치는 카메라 투영(뷰포트 좌표)을 그대로 Blit UV로 쓴다. URP 17은 중간 렌더 텍스처를 Unity 규칙(아래가 V=0,
// RenderTextureUVOriginStrategy.BottomLeft)으로 저장하므로 UV와 뷰포트 좌표가 같다. 클립 공간에 그리는 렌즈 플레어와 달리
// GL.GetGPUProjectionMatrix(proj, true)를 쓰면 D3D에서 상하가 뒤집힌 자리에 나타난다(2026-11-06 렌더 비교로 확인).
// 반지름은 화면 높이 비율로 넘기고, 셰이더는 (UV 차이 × (가로세로비, 1))로 월드 방향의 원을 만든다.
public sealed class ScreenDistortionPass : ScriptableRenderPass
{
    private static readonly int CentersId = Shader.PropertyToID("_DistortionCenters");
    private static readonly int ShapesId = Shader.PropertyToID("_DistortionShapes");
    private static readonly int SpinsId = Shader.PropertyToID("_DistortionSpins");
    private static readonly int CountId = Shader.PropertyToID("_DistortionCount");
    private static readonly int AxisId = Shader.PropertyToID("_DistortionAxis");

    private readonly Vector4[] centers = new Vector4[ScreenDistortionRenderer.MaxSources];
    private readonly Vector4[] shapes = new Vector4[ScreenDistortionRenderer.MaxSources];
    private readonly Vector4[] spins = new Vector4[ScreenDistortionRenderer.MaxSources];
    private Material material;
    private Material template;
    private int count;
    private Vector4 axis;

    public Vector4 Center0 => centers[0];
    public Vector4 Axis => axis;

    public ScreenDistortionPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        requiresIntermediateTexture = true;
        profilingSampler = new ProfilingSampler("Screen Distortion");
    }

    // 이 카메라에서 보이는 원천 수를 돌려준다(0이면 패스를 넣지 않는다).
    public int Setup(Camera camera, List<ScreenDistortionSource> sources)
    {
        count = 0;
        Material first = null;
        Matrix4x4 viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
        float aspect = Mathf.Max(0.0001f, camera.aspect);

        for (int i = 0; i < sources.Count && count < ScreenDistortionRenderer.MaxSources; i++)
        {
            var source = sources[i];
            if (source == null || !source.isActiveAndEnabled || source.strength <= 0.001f || source.Material == null)
                continue;

            Vector3 world = source.transform.position;
            if (!TryProject(viewProjection, world, out Vector2 uv))
                continue;

            // 월드 1칸이 화면 높이의 몇 배인지(직교 카메라는 1 / (2 × orthographicSize))
            float unitToHeight = camera.orthographic
                ? 1f / (2f * Mathf.Max(0.0001f, camera.orthographicSize))
                : TryProject(viewProjection, world + camera.transform.up, out Vector2 up) ? Mathf.Abs(up.y - uv.y) : 0f;
            float radius = source.radius * unitToHeight;
            if (radius <= 0f)
                continue;

            // 원이 화면에 전혀 걸치지 않으면 넣지 않는다.
            if (uv.x + radius / aspect < 0f || uv.x - radius / aspect > 1f || uv.y + radius < 0f || uv.y - radius > 1f)
                continue;

            centers[count] = new Vector4(uv.x, uv.y, radius, Mathf.Clamp01(source.strength));
            shapes[count] = new Vector4(Mathf.Min(source.innerRadius, source.radius * 0.95f) * unitToHeight,
                source.lens, source.twist, source.normalStrength * unitToHeight);
            spins[count] = new Vector4(source.Rotation, 0f, 0f, 0f);
            first ??= source.Material;
            count++;
        }

        for (int i = count; i < ScreenDistortionRenderer.MaxSources; i++)
        {
            centers[i] = Vector4.zero;
            shapes[i] = Vector4.zero;
            spins[i] = Vector4.zero;
        }

        if (count == 0)
            return 0;

        // 공용 Material 원본은 실행 중 바꾸지 않는다. 원천의 Material을 복사한 실행용 사본에만 값을 쓴다.
        if (material == null || template != first)
        {
            CoreUtils.Destroy(material);
            material = new Material(first) { name = first.name + " (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            template = first;
        }

        axis = new Vector4(aspect, 1f, 0f, 0f);
        return count;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (material == null || count == 0)
            return;

        var resources = frameData.Get<UniversalResourceData>();
        if (resources.isActiveTargetBackBuffer)
            return;

        material.SetVectorArray(CentersId, centers);
        material.SetVectorArray(ShapesId, shapes);
        material.SetVectorArray(SpinsId, spins);
        material.SetFloat(CountId, count);
        material.SetVector(AxisId, axis);

        var desc = renderGraph.GetTextureDesc(resources.cameraColor);
        desc.name = "_ScreenDistortionSource";
        desc.clearBuffer = false;
        TextureHandle copy = renderGraph.CreateTexture(desc);

        renderGraph.AddBlitPass(resources.activeColorTexture, copy, Vector2.one, Vector2.zero, passName: "Screen Distortion Copy");
        renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(copy, resources.activeColorTexture, material, 0),
            passName: "Screen Distortion");
        ScreenDistortionRenderer.RecordedPassCount++;
    }

    public void Dispose()
    {
        CoreUtils.Destroy(material);
        material = null;
        template = null;
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
