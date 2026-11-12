using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Wave 격자 편집기의 환경 장치 그림·크기·공격 범위를 Prefab과 배치 설정별로 한 번만 계산해 둔다.
/// 그림은 회전·해상도 단계마다 텍스처로 한 번 그려 두고, 다시 그릴 때는 텍스처만 놓는다.
/// 예전에는 다시 그릴 때마다 Mesh 정점을 복사하고 삼각형을 하나씩 안티앨리어싱 다각형으로 그려 창이 느려졌다.
/// </summary>
public static class WaveDevicePreviewCache
{
    private const int Padding = 2;
    private const int MaxTextureSize = 1024;
    private const int MaxTextures = 96;

    /// <summary>회전하기 전 장치 원점 기준의 그림 정보.</summary>
    public sealed class Shape
    {
        public Bounds Bounds;
        public bool HasSprites;
        public readonly List<(Color color, Vector2[] vertices)> Parts = new();
    }

    private static readonly Dictionary<string, Shape> shapes = new();
    private static readonly Dictionary<string, Texture2D> textures = new();
    private static readonly Dictionary<string, (bool found, WaveDeviceRange range)> ranges = new();
    private static Material material;

    /// <summary>캐시를 쓰지 않은 그림 계산 횟수(검증용).</summary>
    public static int ShapeBuilds { get; private set; }
    public static int TextureBuilds { get; private set; }
    public static int CachedTextures => textures.Count;

    [InitializeOnLoadMethod]
    private static void RegisterRelease() => AssemblyReloadEvents.beforeAssemblyReload += Release;

    /// <summary>스크립트를 다시 불러오기 전에 만든 텍스처와 재질을 모두 지운다.</summary>
    public static void Release()
    {
        Invalidate();
        if (material != null) UnityEngine.Object.DestroyImmediate(material);
        material = null;
    }

    public static void Invalidate()
    {
        foreach (var texture in textures.Values)
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
        textures.Clear();
        shapes.Clear();
        ranges.Clear();
    }

    // 그림에 영향을 주는 설정만 남긴다. 모양이 설정과 무관한 장치나 속도·대기 같은 값은 빼서 같은 그림을 다시 쓴다.
    private static WaveDeviceSetting[] ShapeSettings(GameObject prefab, WaveDeviceSetting[] settings)
    {
        if (settings == null || settings.Length == 0 || !prefab.TryGetComponent<IWaveDeviceSettings>(out var device) || !device.SettingsChangeShape)
            return WaveDeviceSettings.None;
        return WaveDeviceSettings.ShapeOnly(settings, device.Parameters);
    }

    private static string Key(GameObject prefab, WaveDeviceSetting[] settings, bool shapeOnly)
    {
        string signature = WaveDeviceSettings.Signature(shapeOnly ? ShapeSettings(prefab, settings) : settings);
        return prefab.GetInstanceID() + "|" + signature;
    }

    public static Shape Get(GameObject prefab, WaveDeviceSetting[] settings)
    {
        string key = Key(prefab, settings, true);
        if (shapes.TryGetValue(key, out var cached)) return cached;
        var shape = new Shape();
        ShapeBuilds++;
        settings = ShapeSettings(prefab, settings);
        bool customShape = settings.Length > 0;
        if (!customShape) Read(prefab, shape);
        else
        {
            // 설정을 적용한 모양은 비활성 부모 아래 복제본에서 읽는다. Awake·OnEnable이 돌지 않아 실행 중인 게임에 영향이 없다.
            var holder = new GameObject("WaveDevicePreview") { hideFlags = HideFlags.HideAndDontSave };
            holder.SetActive(false);
            try
            {
                var instance = UnityEngine.Object.Instantiate(prefab, holder.transform);
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                WaveDeviceSettings.Apply(instance, settings);
                Read(instance, shape);
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }
        shapes[key] = shape;
        return shape;
    }

    public static Bounds RotatedBounds(GameObject prefab, WaveDeviceSetting[] settings, int rotationSteps)
    {
        var source = Get(prefab, settings).Bounds;
        var rotation = Quaternion.Euler(0, 0, rotationSteps * 90f);
        var result = new Bounds(rotation * source.center, Vector3.zero);
        foreach (float x in new[] { source.min.x, source.max.x })
            foreach (float y in new[] { source.min.y, source.max.y })
                result.Encapsulate(rotation * new Vector3(x, y, 0));
        return result;
    }

    /// <summary>설정에 맞는 공격·작동 범위. 장치가 범위를 내지 않으면 WaveEnvironmentTool의 고정 값을 쓴다.</summary>
    public static bool TryGetRange(GameObject prefab, WaveDeviceSetting[] settings, out WaveDeviceRange range)
    {
        range = default;
        if (prefab == null) return false;
        string key = Key(prefab, settings, false);
        if (ranges.TryGetValue(key, out var cached)) { range = cached.range; return cached.found; }
        bool found = false;
        if (prefab.TryGetComponent<IWaveDeviceSettings>(out var device) && device.TryGetRange(settings ?? WaveDeviceSettings.None, out range)) found = true;
        else if (prefab.TryGetComponent<WaveEnvironmentTool>(out var tool) && tool.Range != WaveEnvironmentTool.RangeShape.None)
        {
            range = new WaveDeviceRange(tool.Range, tool.RangeCenter, tool.RangeSize, tool.RangeColor);
            found = true;
        }
        ranges[key] = (found, range);
        return found;
    }

    private static void Read(GameObject root, Shape shape)
    {
        var toRoot = Matrix4x4.Scale(root.transform.localScale) * root.transform.worldToLocalMatrix;
        var renderers = new List<Renderer>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            if (Visible(renderer, root.transform)) renderers.Add(renderer);
        // 실제 화면과 같은 앞뒤 순서로 겹친다.
        renderers.Sort((a, b) =>
        {
            int layer = SortingLayer.GetLayerValueFromID(a.sortingLayerID).CompareTo(SortingLayer.GetLayerValueFromID(b.sortingLayerID));
            return layer != 0 ? layer : a.sortingOrder.CompareTo(b.sortingOrder);
        });
        bool found = false;
        // uGUI 목표 Panel은 Renderer가 없으므로 원본 Graphic의 벡터 메시를 읽는다.
        foreach (var panel in root.GetComponentsInChildren<WaveGoalPanelGraphic>(true))
        {
            var mesh = new Mesh();
            try
            {
                panel.BuildPanelMesh(mesh);
                var matrix = toRoot * panel.transform.localToWorldMatrix;
                Encapsulate(ref shape.Bounds, ref found, matrix, mesh.bounds);
                var vertices = mesh.vertices; var colors = mesh.colors; var indices = mesh.triangles;
                for (int i = 0; i < indices.Length; i += 3)
                    shape.Parts.Add((colors[indices[i]], new[] { (Vector2)matrix.MultiplyPoint3x4(vertices[indices[i]]),
                        (Vector2)matrix.MultiplyPoint3x4(vertices[indices[i + 1]]), (Vector2)matrix.MultiplyPoint3x4(vertices[indices[i + 2]]) }));
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        foreach (var renderer in renderers)
        {
            var matrix = toRoot * renderer.transform.localToWorldMatrix;
            if (renderer is SpriteRenderer) { shape.HasSprites = true; Encapsulate(ref shape.Bounds, ref found, matrix, renderer.localBounds); continue; }
            if (!(renderer is MeshRenderer) || !renderer.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
            var mesh = filter.sharedMesh;
            // 기존 편집기의 크기 계산(Renderer 범위의 8꼭짓점)과 같은 값을 쓴다.
            Encapsulate(ref shape.Bounds, ref found, matrix, mesh.bounds);
            var vertices = mesh.vertices;
            var materials = renderer.sharedMaterials;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var source = submesh < materials.Length ? materials[submesh] : null;
                var color = source != null && source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                    : source != null && source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
                var triangles = mesh.GetTriangles(submesh);
                var points = new Vector2[triangles.Length];
                for (int i = 0; i < triangles.Length; i++) points[i] = matrix.MultiplyPoint3x4(vertices[triangles[i]]);
                shape.Parts.Add((color, points));
            }
        }
        if (!found) shape.Bounds = new Bounds(Vector3.zero, Vector3.one);
    }

    private static void Encapsulate(ref Bounds result, ref bool found, Matrix4x4 matrix, Bounds local)
    {
        foreach (float x in new[] { local.min.x, local.max.x })
            foreach (float y in new[] { local.min.y, local.max.y })
                foreach (float z in new[] { local.min.z, local.max.z })
                {
                    var point = matrix.MultiplyPoint3x4(new Vector3(x, y, z));
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                    else result.Encapsulate(point);
                }
    }

    // 기존 편집기와 같은 기준: 켜져 있고 루트까지 활성이며 입자·궤적이 아닌 Renderer.
    private static bool Visible(Renderer renderer, Transform root)
    {
        if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer is TrailRenderer) return false;
        for (var node = renderer.transform; node != null; node = node.parent)
        {
            if (!node.gameObject.activeSelf) return false;
            if (node == root) break;
        }
        if (renderer is SpriteRenderer sprite) return sprite.sprite != null && sprite.color.a > 0;
        return renderer is MeshRenderer;
    }

    /// <summary>회전한 장치 그림. pixelsPerUnit은 2의 거듭제곱 단계로 맞춰 확대할 때마다 새로 그리지 않는다.</summary>
    public static Texture2D Picture(GameObject prefab, WaveDeviceSetting[] settings, int rotationSteps, float pixelsPerUnit, out Rect worldRect)
    {
        var shape = Get(prefab, settings);
        var bounds = RotatedBounds(prefab, settings, rotationSteps);
        int ppu = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(Mathf.Max(1f, pixelsPerUnit))), 8, 128);
        float largest = Mathf.Max(bounds.size.x, bounds.size.y, .01f);
        while (ppu > 8 && largest * ppu + Padding * 2 > MaxTextureSize) ppu /= 2;
        float pad = Padding / (float)ppu;
        worldRect = Rect.MinMaxRect(bounds.min.x - pad, bounds.min.y - pad, bounds.max.x + pad, bounds.max.y + pad);
        string key = Key(prefab, settings, true) + "|" + rotationSteps + "|" + ppu;
        if (textures.TryGetValue(key, out var cached) && cached != null) return cached;
        if (textures.Count >= MaxTextures) Invalidate(); // 드물게만 가득 찬다. 필요한 그림은 다음 그리기에서 다시 만든다.
        var texture = Render(shape, rotationSteps, ppu, worldRect);
        textures[key] = texture;
        return texture;
    }

    private static Texture2D Render(Shape shape, int rotationSteps, int ppu, Rect worldRect)
    {
        TextureBuilds++;
        int width = Mathf.Clamp(Mathf.CeilToInt(worldRect.width * ppu), 1, MaxTextureSize);
        int height = Mathf.Clamp(Mathf.CeilToInt(worldRect.height * ppu), 1, MaxTextureSize);
        var rotation = Quaternion.Euler(0, 0, rotationSteps * 90f);
        var target = RenderTexture.GetTemporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
        { sRGB = false, msaaSamples = 4, useMipMap = false });
        var previous = RenderTexture.active;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
        { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "WaveDevicePreview" };
        try
        {
            RenderTexture.active = target;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, width, 0, height);
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            Material.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            float sx = width / worldRect.width, sy = height / worldRect.height;
            foreach (var part in shape.Parts)
            {
                // 투명한 바탕 위에 겹쳐도 알파가 맞도록 미리 곱한 색으로 그리고, 읽은 뒤 되돌린다.
                var c = part.color;
                GL.Color(new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a));
                foreach (var vertex in part.vertices)
                {
                    Vector2 p = rotation * (Vector3)vertex;
                    GL.Vertex3((p.x - worldRect.xMin) * sx, (p.y - worldRect.yMin) * sy, 0);
                }
            }
            GL.End();
            GL.PopMatrix();
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            var pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                if (p.a == 0 || p.a == 255) continue;
                float inverse = 255f / p.a;
                pixels[i] = new Color32((byte)Mathf.Min(255, p.r * inverse), (byte)Mathf.Min(255, p.g * inverse), (byte)Mathf.Min(255, p.b * inverse), p.a);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
        return texture;
    }

    private static Material Material
    {
        get
        {
            if (material != null) return material;
            material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)CompareFunction.Always);
            return material;
        }
    }
}
