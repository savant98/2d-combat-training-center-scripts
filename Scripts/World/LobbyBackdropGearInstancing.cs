using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>동일한 기어 형상을 공유 Mesh로 그려 원래 애니메이션·정렬을 유지하며 GPU instancing을 사용한다.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1500)]
public class LobbyBackdropGearInstancing : MonoBehaviour
{
    // 빌드에서도 instancing 변형을 유지하도록 활성화된 재질 에셋을 참조한다.
    [SerializeField] private Material instancedMaterial;
    private readonly List<SpriteRenderer> originals = new List<SpriteRenderer>();
    private readonly List<GameObject> outputs = new List<GameObject>();
    private readonly Dictionary<Sprite, Mesh> meshes = new Dictionary<Sprite, Mesh>();
    private readonly Dictionary<(Texture, Texture), Material> materials = new Dictionary<(Texture, Texture), Material>();

    private void OnEnable()
    {
        if (!Application.isPlaying || !SystemInfo.supportsInstancing
            || instancedMaterial == null || !instancedMaterial.shader.isSupported) return;

        var properties = new MaterialPropertyBlock();
        foreach (var sprite in GetComponentsInChildren<SpriteRenderer>(true))
        {
            var source = sprite.sharedMaterial;
            if (!sprite.enabled || sprite.sprite == null || source == null
                || source.shader.name != "Shader Graphs/GlowMultiply" || sprite.color != Color.white
                || sprite.flipX || sprite.flipY || sprite.drawMode != SpriteDrawMode.Simple
                || sprite.maskInteraction != SpriteMaskInteraction.None
                || !source.HasProperty("_EmissionTex") || HasCustomProperties(sprite, source)) continue;
            var mask = source.GetTexture("_EmissionTex");
            if (mask == null || !mask.name.Contains("Cogwheel")) continue;

            if (!meshes.TryGetValue(sprite.sprite, out var mesh))
            {
                var vertices = sprite.sprite.vertices;
                var indices = sprite.sprite.triangles;
                var positions = new Vector3[vertices.Length];
                var triangles = new int[indices.Length];
                for (int i = 0; i < positions.Length; i++) positions[i] = vertices[i];
                for (int i = 0; i < triangles.Length; i++) triangles[i] = indices[i];
                mesh = new Mesh { name = "LobbyInstancedGearMesh", hideFlags = HideFlags.DontSave };
                mesh.vertices = positions;
                mesh.uv = sprite.sprite.uv;
                mesh.triangles = triangles;
                mesh.RecalculateBounds();
                meshes.Add(sprite.sprite, mesh);
            }

            var key = ((Texture)sprite.sprite.texture, mask);
            if (!materials.TryGetValue(key, out var material))
            {
                material = new Material(instancedMaterial)
                {
                    name = "LobbyGear (LobbyInstanced)",
                    hideFlags = HideFlags.DontSave,
                    enableInstancing = true
                };
                material.mainTexture = sprite.sprite.texture;
                material.SetTexture("_EmissionTex", mask);
                materials.Add(key, material);
            }

            var output = new GameObject("LobbyInstancedGear") { hideFlags = HideFlags.DontSave };
            output.transform.SetParent(sprite.transform, false);
            output.layer = sprite.gameObject.layer;
            output.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = output.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingLayerID = sprite.sortingLayerID;
            renderer.sortingOrder = sprite.sortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // 재질은 공유하고 색상·강도만 instanced property로 전달해 재질 전환도 줄인다.
            // SetColor의 sRGB 변환을 거치지 않고 원본 HDR 값을 보존한다.
            properties.SetVector("_InstanceColor", source.GetColor("_EmissionColor"));
            properties.SetFloat("_InstanceIntensity", source.GetFloat("_Intensity"));
            renderer.SetPropertyBlock(properties);
            originals.Add(sprite);
            outputs.Add(output);
            sprite.enabled = false;
        }
    }

    private static bool HasCustomProperties(SpriteRenderer sprite, Material material)
    {
        if (!sprite.HasPropertyBlock()) return false;
        var block = new MaterialPropertyBlock();
        sprite.GetPropertyBlock(block);
        for (int i = 0; i < material.shader.GetPropertyCount(); i++)
        {
            string name = material.shader.GetPropertyName(i);
            if (name != "_MainTex" && block.HasProperty(name)) return true;
        }
        return block.HasTexture("_MainTex") && block.GetTexture("_MainTex") != sprite.sprite.texture;
    }

    private void OnDisable()
    {
        foreach (var sprite in originals)
            if (sprite != null) sprite.enabled = true;
        foreach (var output in outputs)
        {
            if (output == null) continue;
            output.GetComponent<Renderer>().enabled = false;
            output.SetActive(false);
            Destroy(output);
        }
        foreach (var mesh in meshes.Values) Destroy(mesh);
        foreach (var material in materials.Values) Destroy(material);
        originals.Clear();
        outputs.Clear();
        meshes.Clear();
        materials.Clear();
    }
}
