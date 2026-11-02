using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 패널 틈의 개방량을 후경 표면 조명과 설비 실루엣의 투영 그림자에 함께 반영한다.
[DefaultExecutionOrder(5000), DisallowMultipleComponent]
public sealed class NightmareFactoryLightRig : MonoBehaviour
{
    [SerializeField] private Renderer[] openings;
    [SerializeField] private Renderer[] casters;
    [SerializeField] private Transform receiverBand;
    [SerializeField] private Material shadowMaterial;
    private readonly Vector4[] lights = new Vector4[8];
    private readonly float[] lightScores = new float[8];
    private readonly List<Shadow> shadows = new List<Shadow>();
    private readonly List<Mesh> spriteMeshes = new List<Mesh>();
    private MaterialPropertyBlock properties;
    private int lightCount;
    private int preparedFrame = -1;
    public int ActiveOpeningCount => lightCount;
    public int ShadowCount => shadows.Count;

    private sealed class Shadow
    {
        public Renderer Source;
        public MeshRenderer Output;
        public MaterialPropertyBlock Properties;
        public int Slot;
    }

    private void OnEnable()
    {
        if (!Application.isPlaying || shadowMaterial == null || receiverBand == null) return;
        properties = new MaterialPropertyBlock();
        if (casters != null) foreach (var source in casters)
        {
            if (source == null || source.sharedMaterial == null) continue;
            Mesh mesh = null;
            Texture main = source.sharedMaterial.mainTexture;
            if (source is SpriteRenderer sprite && sprite.sprite != null)
            {
                if (sprite.flipX || sprite.flipY || sprite.drawMode != SpriteDrawMode.Simple) continue;
                var vertices = sprite.sprite.vertices; var indices = sprite.sprite.triangles;
                var positions = new Vector3[vertices.Length]; var triangles = new int[indices.Length];
                for (int i = 0; i < positions.Length; i++) positions[i] = vertices[i];
                for (int i = 0; i < indices.Length; i++) triangles[i] = indices[i];
                mesh = new Mesh { name = "NightmareShadowSilhouette", hideFlags = HideFlags.DontSave };
                mesh.vertices = positions; mesh.uv = sprite.sprite.uv; mesh.triangles = triangles;
                mesh.colors = System.Array.ConvertAll(positions, _ => Color.white);
                mesh.RecalculateBounds(); spriteMeshes.Add(mesh); main = sprite.sprite.texture;
            }
            else if (source.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
            if (mesh == null) continue;
            for (int slot = 0; slot < 2; slot++)
            {
                var output = new GameObject("OpeningShadow") { hideFlags = HideFlags.DontSave };
                output.transform.SetParent(source.transform, false);
                output.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = output.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = shadowMaterial; renderer.sortingLayerID = source.sortingLayerID;
                renderer.sortingOrder = -4; renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false; renderer.lightProbeUsage = LightProbeUsage.Off;
                var block = new MaterialPropertyBlock(); block.SetTexture("_MainTex", main != null ? main : Texture2D.whiteTexture);
                if (source.sharedMaterial.HasProperty("_EmissionTex")) block.SetTexture("_EmissionTex", source.sharedMaterial.GetTexture("_EmissionTex") ?? Texture2D.whiteTexture);
                block.SetFloat("_UseEmissionMask", source.sharedMaterial.HasProperty("_UseEmissionMask") ? source.sharedMaterial.GetFloat("_UseEmissionMask") : 0);
                Vector2 scale = source.sharedMaterial.mainTextureScale, offset = source.sharedMaterial.mainTextureOffset;
                block.SetVector("_MainTex_ST", source.sharedMaterial.HasProperty("_SurfaceUV") ? source.sharedMaterial.GetVector("_SurfaceUV") : new Vector4(scale.x, scale.y, offset.x, offset.y));
                block.SetVector("_ScrollOffset", source.sharedMaterial.HasProperty("_ScrollOffset") ? source.sharedMaterial.GetVector("_ScrollOffset") : Vector4.zero);
                shadows.Add(new Shadow { Source = source, Output = renderer, Properties = block, Slot = slot });
            }
        }
        RenderPipelineManager.beginCameraRendering += PrepareCamera;
    }

    private void PrepareCamera(ScriptableRenderContext context, Camera camera)
    {
        if (preparedFrame == Time.frameCount) return;
        preparedFrame = Time.frameCount;
        RefreshLighting();
    }

    private void LateUpdate() { if (properties != null) RefreshLighting(); }

    private void RefreshLighting()
    {
        lightCount = 0;
        if (openings != null) foreach (var source in openings)
        {
            if (source == null || !source.enabled || !source.gameObject.activeInHierarchy) continue;
            source.GetPropertyBlock(properties); float visibility = properties.GetFloat("_Visibility");
            if (visibility <= .01f) continue;
            var position = source.bounds.center;
            var candidate = new Vector4(position.x, position.y, position.z, visibility);
            float score = visibility / (1 + (Camera.main != null ? Mathf.Abs(position.x - Camera.main.transform.position.x) * .08f : 0));
            int insert = lightCount;
            while (insert > 0 && lightScores[insert - 1] < score) insert--;
            if (insert >= lights.Length) continue;
            for (int i = Mathf.Min(lightCount, lights.Length - 1); i > insert; i--) { lights[i] = lights[i - 1]; lightScores[i] = lightScores[i - 1]; }
            lights[insert] = candidate; lightScores[insert] = score; lightCount = Mathf.Min(lightCount + 1, lights.Length);
        }
        Shader.SetGlobalInt("_NightmareOpeningCount", lightCount);
        Shader.SetGlobalVectorArray("_NightmareOpeningPositions", lights);
        foreach (var shadow in shadows)
        {
            if (shadow.Source == null || shadow.Output == null) continue;
            var source = shadow.Source; var center = source.bounds.center;
            int first = -1, second = -1; float firstScore = 0, secondScore = 0;
            for (int i = 0; i < lightCount; i++)
            {
                float distance = Vector2.Distance(center, lights[i]);
                float score = lights[i].w * Mathf.Pow(Mathf.Clamp01(1 - distance / 9), 2);
                if (lights[i].z <= center.z + .5f) continue;
                if (score > firstScore) { second = first; secondScore = firstScore; first = i; firstScore = score; }
                else if (score > secondScore) { second = i; secondScore = score; }
            }
            int light = shadow.Slot == 0 ? first : second;
            source.GetPropertyBlock(properties);
            float alpha = properties.HasFloat("_Visibility") ? properties.GetFloat("_Visibility") : 1;
            if (source is SpriteRenderer sprite) alpha *= sprite.color.a;
            if (source.sharedMaterial.HasProperty("_Color")) alpha *= source.sharedMaterial.GetColor("_Color").a;
            shadow.Output.enabled = source.enabled && source.gameObject.activeInHierarchy && light >= 0 && alpha > .01f;
            if (!shadow.Output.enabled) continue;
            shadow.Output.gameObject.layer = receiverBand.gameObject.layer;
            float receiver = Mathf.Min(receiverBand.position.z, center.z - 3);
            shadow.Properties.SetVector("_LightPosition", lights[light]);
            shadow.Properties.SetFloat("_ReceiverZ", receiver);
            shadow.Properties.SetFloat("_ShadowOpacity", alpha * Mathf.Min(.7f, (shadow.Slot == 0 ? firstScore : secondScore) * 2.2f));
            shadow.Output.SetPropertyBlock(shadow.Properties);
            // 정점 셰이더가 확대·투영한 그림자까지 컬링 범위에 포함한다.
            shadow.Output.bounds = new Bounds(new Vector3(center.x, center.y, receiver), new Vector3(80, 60, 4));
        }
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= PrepareCamera;
        Shader.SetGlobalInt("_NightmareOpeningCount", 0); lightCount = 0; preparedFrame = -1;
        foreach (var shadow in shadows) if (shadow.Output != null)
        { shadow.Output.enabled = false; shadow.Output.transform.SetParent(null, false); Destroy(shadow.Output.gameObject); }
        foreach (var mesh in spriteMeshes) Destroy(mesh);
        shadows.Clear(); spriteMeshes.Clear(); properties = null;
    }
}
