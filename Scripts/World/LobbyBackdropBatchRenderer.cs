using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>편집용 소품과 애니메이션을 유지하며 같은 재질의 장식 형상을 실행 중 묶어 그린다.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(2000)]
public class LobbyBackdropBatchRenderer : MonoBehaviour
{
    private class Part
    {
        public Renderer Source;
        public Vector3[] Vertices;
        public Vector2[] UV;
        public Color[] Colors;
        public int[] Indices;
        public int Offset;
    }

    private class Batch
    {
        public Transform Root;
        public Material Material;
        public Texture Texture;
        public int Layer;
        public int Order;
        public readonly List<Part> Parts = new List<Part>();
        public Vector3[] Positions;
        public Mesh Mesh;
        public GameObject Output;
    }

    [SerializeField, HideInInspector] private bool layoutPrepared;
    [SerializeField, HideInInspector] private Transform[] animatedRoots = Array.Empty<Transform>();
    [SerializeField, HideInInspector] private Renderer[] unbatchedRenderers = Array.Empty<Renderer>();
    private readonly HashSet<Transform> animationRoots = new HashSet<Transform>();
    private readonly List<Batch> batches = new List<Batch>();
    private readonly List<Renderer> originals = new List<Renderer>();
    private static readonly int MainTextureID = Shader.PropertyToID("_MainTex");

    private void OnEnable()
    {
        if (!Application.isPlaying || !layoutPrepared) return;
        animationRoots.UnionWith(animatedRoots);
        var excluded = new HashSet<Renderer>(unbatchedRenderers);
        var groups = new Dictionary<(Transform, Material, Texture, int, int, int), Batch>();
        // 케이블과 입자는 기존 Renderer를 유지한다. 합성용 카메라가 사용하는 Layer도 그대로 따른다.
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || excluded.Contains(renderer) || renderer.sharedMaterials.Length != 1) continue;
            var material = renderer.sharedMaterial;
            if (material == null || !CanCombine(renderer, material)) continue;
            Part part = ReadPart(renderer);
            if (part == null) continue;
            var root = FindBatchRoot(renderer.transform);
            if (root == null) continue;
            var sprite = renderer as SpriteRenderer;
            Texture texture = sprite != null ? sprite.sprite.texture : material.mainTexture;
            // 알파 면은 깊이 순서를 세분화한다. 더하기 발광끼리는 겹치는 순서에 영향이 없다.
            int slice = material.shader.name == "Sprites/Default"
                ? Mathf.RoundToInt(root.InverseTransformPoint(renderer.bounds.center).z * 4) : 0;
            var key = (root, material, texture, renderer.sortingLayerID, renderer.sortingOrder, slice);
            if (!groups.TryGetValue(key, out var batch))
            {
                batch = new Batch { Root = root, Material = material, Texture = texture,
                    Layer = renderer.sortingLayerID, Order = renderer.sortingOrder };
                groups.Add(key, batch);
            }
            batch.Parts.Add(part);
        }
        foreach (var batch in groups.Values)
        {
            if (batch.Parts.Count < 2) continue;
            Build(batch);
            batches.Add(batch);
            foreach (var part in batch.Parts)
            {
                originals.Add(part.Source);
                part.Source.enabled = false;
            }
        }
    }

    private Transform FindBatchRoot(Transform source)
    {
        var band = source;
        while (band.parent != null && band.parent != transform) band = band.parent;
        if (band.parent != transform) return null;
        var prop = source;
        while (prop.parent != band && prop != band) prop = prop.parent;
        // 가장 가까운 애니메이션 관절 아래에서만 묶으면 결합 Mesh도 원래 관절을 따라 움직인다.
        for (var current = source; current != prop; current = current.parent)
            if (animationRoots.Contains(current)) return current;
        return prop;
    }

    private static bool CanCombine(Renderer renderer, Material material)
    {
        if (!renderer.HasPropertyBlock()) return true;
        var sprite = renderer as SpriteRenderer;
        if (sprite == null || sprite.sprite == null) return false;
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        // Unity가 Sprite에 자동으로 넣는 텍스처는 UV와 함께 옮긴다. 사용자 지정 재질 덮어쓰기는 원본을 유지한다.
        for (int i = 0; i < material.shader.GetPropertyCount(); i++)
        {
            string name = material.shader.GetPropertyName(i);
            if (name != "_MainTex" && properties.HasProperty(name)) return false;
        }
        return !properties.HasTexture(MainTextureID) || properties.GetTexture(MainTextureID) == sprite.sprite.texture;
    }

    private static Part ReadPart(Renderer renderer)
    {
        var sprite = renderer as SpriteRenderer;
        if (sprite != null)
        {
            if (sprite.sprite == null || sprite.drawMode != SpriteDrawMode.Simple || sprite.maskInteraction != SpriteMaskInteraction.None) return null;
            var vertices = sprite.sprite.vertices;
            var triangles = sprite.sprite.triangles;
            var part = new Part { Source = renderer, Vertices = new Vector3[vertices.Length],
                UV = sprite.sprite.uv, Colors = new Color[vertices.Length], Indices = new int[triangles.Length] };
            for (int i = 0; i < vertices.Length; i++)
            {
                part.Vertices[i] = new Vector3(sprite.flipX ? -vertices[i].x : vertices[i].x,
                    sprite.flipY ? -vertices[i].y : vertices[i].y, 0);
                part.Colors[i] = sprite.color;
            }
            for (int i = 0; i < triangles.Length; i++) part.Indices[i] = triangles[i];
            if (sprite.flipX != sprite.flipY)
                for (int i = 0; i < part.Indices.Length; i += 3)
                    (part.Indices[i + 1], part.Indices[i + 2]) = (part.Indices[i + 2], part.Indices[i + 1]);
            return part;
        }
        var filter = renderer.GetComponent<MeshFilter>();
        if (!(renderer is MeshRenderer) || filter == null || filter.sharedMesh == null
            || !filter.sharedMesh.isReadable || filter.sharedMesh.subMeshCount != 1) return null;
        var mesh = filter.sharedMesh;
        return new Part { Source = renderer, Vertices = mesh.vertices,
            UV = mesh.uv, Colors = mesh.colors, Indices = mesh.triangles };
    }

    private static void Build(Batch batch)
    {
        batch.Parts.Sort((a, b) => b.Source.bounds.center.z.CompareTo(a.Source.bounds.center.z));
        int vertexCount = 0;
        int indexCount = 0;
        foreach (var part in batch.Parts)
        {
            part.Offset = vertexCount;
            vertexCount += part.Vertices.Length;
            indexCount += part.Indices.Length;
        }
        batch.Positions = new Vector3[vertexCount];
        var uv = new Vector2[vertexCount];
        var colors = new Color[vertexCount];
        var indices = new int[indexCount];
        int cursor = 0;
        foreach (var part in batch.Parts)
        {
            Matrix4x4 relative = batch.Root.worldToLocalMatrix * part.Source.localToWorldMatrix;
            for (int i = 0; i < part.Vertices.Length; i++)
            {
                batch.Positions[part.Offset + i] = relative.MultiplyPoint3x4(part.Vertices[i]);
                uv[part.Offset + i] = part.UV.Length > i ? part.UV[i] : Vector2.zero;
                colors[part.Offset + i] = part.Colors.Length > i ? part.Colors[i] : Color.white;
            }
            bool mirrored = (batch.Root.worldToLocalMatrix * part.Source.localToWorldMatrix).determinant < 0;
            for (int i = 0; i < part.Indices.Length; i += 3)
            {
                indices[cursor++] = part.Offset + part.Indices[i];
                indices[cursor++] = part.Offset + part.Indices[i + (mirrored ? 2 : 1)];
                indices[cursor++] = part.Offset + part.Indices[i + (mirrored ? 1 : 2)];
            }
        }
        batch.Mesh = new Mesh { name = "LobbyBatchMesh", hideFlags = HideFlags.DontSave,
            indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        batch.Mesh.vertices = batch.Positions;
        batch.Mesh.uv = uv;
        batch.Mesh.colors = colors;
        batch.Mesh.triangles = indices;
        batch.Output = new GameObject("LobbyBatch_" + batch.Material.name) { hideFlags = HideFlags.DontSave };
        batch.Output.transform.SetParent(batch.Root, false);
        batch.Output.layer = batch.Parts[0].Source.gameObject.layer;
        batch.Output.AddComponent<MeshFilter>().sharedMesh = batch.Mesh;
        var renderer = batch.Output.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = batch.Material;
        renderer.sortingLayerID = batch.Layer;
        renderer.sortingOrder = batch.Order;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        if (batch.Texture != batch.Material.mainTexture)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetTexture(MainTextureID, batch.Texture);
            renderer.SetPropertyBlock(properties);
        }
    }

    private void OnDisable()
    {
        foreach (var renderer in originals)
            if (renderer != null) renderer.enabled = true;
        foreach (var batch in batches)
        {
            if (batch.Output != null)
            {
                // 같은 프레임에 다시 켜도 삭제 대기 Mesh가 다음 결합 대상에 들어가지 않게 한다.
                batch.Output.GetComponent<Renderer>().enabled = false;
                batch.Output.SetActive(false);
            }
            Destroy(batch.Output);
            Destroy(batch.Mesh);
        }
        originals.Clear();
        batches.Clear();
        animationRoots.Clear();
    }
}
