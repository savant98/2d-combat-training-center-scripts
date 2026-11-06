using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// 화면 왜곡에서 제외할 개체(ScreenDistortionRenderer.ProtectedLayer)를 왜곡 패스 뒤에 그린다.
// 메인 카메라 컬링에서 그 Layer를 잠시 빼고(ScreenDistortionRenderer가 되돌린다) 따로 컬링한 결과를
// 2D 렌더러와 같은 셰이더 태그(Universal2D·SRPDefaultUnlit)와 정렬(CommonTransparent)로 그린다.
// 같은 BeforeRenderingPostProcessing 시점이며 왜곡 패스 다음에 넣으므로 Bloom은 그대로 받는다.
public sealed class ScreenDistortionProtectedPass : ScriptableRenderPass
{
    private static readonly List<ShaderTagId> ShaderTags = new()
    {
        new ShaderTagId("Universal2D"),
        new ShaderTagId("SRPDefaultUnlit"),
    };

    private sealed class PassData
    {
        public RendererListHandle rendererList;
    }

    private CullingResults cullResults;
    private bool ready;

    public ScreenDistortionProtectedPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        requiresIntermediateTexture = true;
        profilingSampler = new ProfilingSampler("Screen Distortion Protected");
    }

    public void Setup(CullingResults results)
    {
        cullResults = results;
        ready = true;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (!ready)
            return;
        ready = false;

        var resources = frameData.Get<UniversalResourceData>();
        var renderingData = frameData.Get<UniversalRenderingData>();
        var cameraData = frameData.Get<UniversalCameraData>();
        var lightData = frameData.Get<UniversalLightData>();

        var drawSettings = CreateDrawingSettings(ShaderTags, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
        var filterSettings = new FilteringSettings(RenderQueueRange.all, ScreenDistortionRenderer.ProtectedLayerMask);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("Screen Distortion Protected", out var passData, profilingSampler))
        {
            passData.rendererList = renderGraph.CreateRendererList(new RendererListParams(cullResults, drawSettings, filterSettings));
            builder.UseRendererList(passData.rendererList);
            builder.SetRenderAttachment(resources.activeColorTexture, 0);
            if (resources.activeDepthTexture.IsValid())
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
            builder.SetRenderFunc((PassData data, RasterGraphContext context) => context.cmd.DrawRendererList(data.rendererList));
        }
        ScreenDistortionRenderer.ProtectedPassCount++;
    }
}
