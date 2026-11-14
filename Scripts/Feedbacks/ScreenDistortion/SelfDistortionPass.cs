using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// 자기 왜곡 패스: 원천이 등록한 Renderer만 빈 그림(_SelfDistortionLayer)에 다시 그린 뒤, 그 그림을 노말맵으로 굴절시켜 화면에 더한다.
// - 화면 색을 읽지 않으므로 배경·Player·다른 개체는 한 픽셀도 달라지지 않는다. 달라지는 것은 따로 그린 Renderer의 모습뿐이다.
// - 대상은 가산 발광이라 "빈 그림에 그린 것을 화면에 더하기"가 평소 그리기와 같은 색이 된다(세기가 0인 자리는 그대로 더해진다).
//   평소 그리기에서는 SelfDistortionRenderer가 이 카메라를 그리는 동안 대상을 빼 둔다.
// - 화면 좌표·반지름의 규칙은 ScreenDistortionPass와 같다(Blit UV = 뷰포트 좌표, 반지름은 화면 높이 비율).
// - 2D 렌더러의 BeforeRenderingPostProcessing 시점이라 Bloom은 굴절된 모습에 그대로 걸린다.
public sealed class SelfDistortionPass : ScriptableRenderPass
{
    private static readonly int CentersId = Shader.PropertyToID("_SelfDistortionCenters");
    private static readonly int ShapesId = Shader.PropertyToID("_SelfDistortionShapes");
    private static readonly int CountId = Shader.PropertyToID("_SelfDistortionCount");
    private static readonly int AxisId = Shader.PropertyToID("_SelfDistortionAxis");
    private static readonly ShaderTagId LightModeTag = new("LightMode");
    private static readonly ShaderTagId Universal2DTag = new("Universal2D");

    private struct Item
    {
        public Renderer renderer;
        public Material material;
        public int pass;
    }

    private sealed class LayerData
    {
        public List<Item> items;
    }

    private sealed class CompositeData
    {
        public TextureHandle layer;
        public Material material;
    }

    private readonly Vector4[] centers = new Vector4[SelfDistortionRenderer.MaxSources];
    private readonly Vector4[] shapes = new Vector4[SelfDistortionRenderer.MaxSources];
    private readonly List<Item> items = new();
    private readonly Dictionary<Shader, int> passIndices = new();
    private Material material;
    private Material template;
    private int count;
    private Vector4 axis;

    public Vector4 Center0 => centers[0];
    public int DrawCount => items.Count;

    public SelfDistortionPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        requiresIntermediateTexture = true;
        profilingSampler = new ProfilingSampler("Self Distortion");
    }

    // 이 카메라에서 보이는 원천 수를 돌려준다(0이면 패스를 넣지 않는다). 따로 그릴 Renderer도 함께 모은다.
    public int Setup(Camera camera, List<SelfDistortionSource> sources)
    {
        count = 0;
        items.Clear();
        Material first = null;
        Matrix4x4 viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
        float aspect = Mathf.Max(0.0001f, camera.aspect);

        for (int i = 0; i < sources.Count && count < SelfDistortionRenderer.MaxSources; i++)
        {
            var source = sources[i];
            if (source == null || !source.isActiveAndEnabled || source.strength <= 0.001f || source.Material == null)
                continue;

            Vector3 world = source.Center;
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

            int before = items.Count;
            var targets = source.Targets;
            for (int t = 0; targets != null && t < targets.Length; t++)
            {
                var renderer = targets[t];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                var shared = renderer.sharedMaterial;
                if (shared == null)
                    continue;
                items.Add(new Item { renderer = renderer, material = shared, pass = FindPass(shared) });
            }
            if (items.Count == before)
                continue;                                   // 그릴 것이 없으면(순간이동으로 모두 숨은 동안) 원천도 넣지 않는다

            centers[count] = new Vector4(uv.x, uv.y, radius, Mathf.Clamp01(source.strength));
            shapes[count] = new Vector4(Mathf.Min(source.innerRadius, source.radius * 0.95f) * unitToHeight,
                source.twist, source.normalStrength * unitToHeight, source.Rotation);
            first ??= source.Material;
            count++;
        }

        for (int i = count; i < SelfDistortionRenderer.MaxSources; i++)
        {
            centers[i] = Vector4.zero;
            shapes[i] = Vector4.zero;
        }

        if (count == 0)
        {
            items.Clear();
            return 0;
        }

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

    public void CollectRenderers(List<Renderer> output)
    {
        for (int i = 0; i < items.Count; i++)
            output.Add(items[i].renderer);
    }

    // 2D 렌더러가 평소 쓰는 패스(LightMode = Universal2D)를 찾는다. 없으면 첫 패스.
    private int FindPass(Material shared)
    {
        Shader shader = shared.shader;
        if (passIndices.TryGetValue(shader, out int index))
            return index;

        index = 0;
        for (int i = 0; i < shader.passCount; i++)
        {
            if (shader.FindPassTagValue(i, LightModeTag) == Universal2DTag)
            {
                index = i;
                break;
            }
        }
        passIndices[shader] = index;
        return index;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (material == null || count == 0 || items.Count == 0)
            return;

        var resources = frameData.Get<UniversalResourceData>();
        if (resources.isActiveTargetBackBuffer)
            return;

        material.SetVectorArray(CentersId, centers);
        material.SetVectorArray(ShapesId, shapes);
        material.SetFloat(CountId, count);
        material.SetVector(AxisId, axis);

        var desc = renderGraph.GetTextureDesc(resources.activeColorTexture);
        desc.name = "_SelfDistortionLayer";
        desc.clearBuffer = true;
        desc.clearColor = Color.clear;
        desc.depthBufferBits = DepthBits.None;
        desc.msaaSamples = MSAASamples.None;
        TextureHandle layer = renderGraph.CreateTexture(desc);

        // 1) 대상 Renderer만 빈 그림에 그린다(카메라의 뷰·투영은 그대로다).
        using (var builder = renderGraph.AddRasterRenderPass<LayerData>("Self Distortion Layer", out var data, profilingSampler))
        {
            data.items = items;
            builder.SetRenderAttachment(layer, 0, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (LayerData d, RasterGraphContext context) =>
            {
                for (int i = 0; i < d.items.Count; i++)
                {
                    var item = d.items[i];
                    if (item.renderer != null)
                        context.cmd.DrawRenderer(item.renderer, item.material, 0, item.pass);
                }
            });
        }

        // 2) 그 그림을 굴절시켜 화면에 더한다.
        using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("Self Distortion Composite", out var data, profilingSampler))
        {
            data.layer = layer;
            data.material = material;
            builder.UseTexture(layer, AccessFlags.Read);
            builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (CompositeData d, RasterGraphContext context) =>
                Blitter.BlitTexture(context.cmd, d.layer, new Vector4(1f, 1f, 0f, 0f), d.material, 0));
        }
        SelfDistortionRenderer.RecordedPassCount++;
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
