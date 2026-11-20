using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class WeaponTraitBurst : MonoBehaviour
{
    private const int Segments = 24;
    private const int MaximumBursts = 40;
    private static readonly Stack<WeaponTraitBurst> Available = new();
    private static Material effectMaterial;
    private static int active;
    private readonly List<Vector3> vertices = new(220);
    private readonly List<Color> colors = new(220);
    private readonly List<int> indices = new(330);
    private Mesh mesh;
    private float life, size, elapsed, spread, seed;
    private int sparks;
    private Color tint;

    private void Awake()
    {
        mesh = new Mesh { name = "WeaponTraitBurst" };
        mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = GetComponent<MeshRenderer>();
        if (effectMaterial == null)
            effectMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
        renderer.sharedMaterial = effectMaterial;
        renderer.sortingLayerName = "Particle";
        renderer.sortingOrder = 34;
    }

    public static void Spawn(Vector3 position, Quaternion rotation, Color color, float radius, float duration,
        int sparkCount, float spread)
    {
        if (active >= MaximumBursts) return;
        WeaponTraitBurst burst = null;
        while (Available.Count > 0 && burst == null) burst = Available.Pop();
        if (burst == null) burst = new GameObject("WeaponTraitBurst").AddComponent<WeaponTraitBurst>();
        burst.transform.SetPositionAndRotation(position, rotation);
        burst.tint = color;
        burst.elapsed = 0f;
        burst.life = duration;
        burst.size = radius;
        burst.sparks = sparkCount;
        burst.spread = spread;
        burst.seed = position.x * 0.63f + position.y * 0.91f + Time.time * 2.1f;
        burst.gameObject.SetActive(true);
        burst.Draw(0f);
    }

    private void OnEnable() => active++;
    private void OnDisable() => active--;
    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }

    private void LateUpdate()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= life)
        {
            gameObject.SetActive(false);
            Available.Push(this);
            return;
        }
        Draw(elapsed / life);
    }

    private void Draw(float t)
    {
        vertices.Clear();
        colors.Clear();
        indices.Clear();
        float fade = (1f - t) * (1f - t);
        float pulse = 1f - (1f - t) * (1f - t);
        Color glow = tint;
        glow.a *= fade * 0.28f;
        Color hot = Color.Lerp(tint, Color.white, 0.72f);
        hot.a *= fade * 0.88f;
        if (spread >= 3f)
        {
            float radius = size * (0.16f + 0.84f * pulse);
            Ring(radius, size * (0.23f - t * 0.10f), glow);
            Ring(radius, size * 0.038f, hot);
        }
        float flash = size * (0.16f + 0.27f * pulse);
        Quad(Vector2.left * flash, Vector2.up * flash * 0.42f, Vector2.right * flash,
            Vector2.down * flash * 0.42f, hot);
        for (int i = 0; i < sparks; i++)
        {
            float offset = Mathf.Repeat(i * 0.618034f + seed, 1f);
            float angle = (offset - 0.5f) * spread * 2f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 normal = Vector2.Perpendicular(direction);
            float reach = size * (0.23f + 0.78f * Mathf.Repeat(i * 0.37f, 1f)) * pulse;
            Vector2 tip = direction * reach + Vector2.down * (size * 0.15f * t * t);
            float length = size * (0.08f + offset * 0.23f) * fade;
            float width = size * (0.015f + 0.03f * offset) * (1f - t);
            Quad(tip - direction * length, tip + normal * width, tip + direction * width,
                tip - normal * width, i % 3 == 0 ? hot : new Color(tint.r, tint.g, tint.b, fade));
        }
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(indices, 0);
    }

    private void Ring(float radius, float width, Color color)
    {
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            float b = (i + 1) * Mathf.PI * 2f / Segments;
            Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 v = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
            Quad(u * (radius - width * 0.5f), u * (radius + width * 0.5f),
                v * (radius + width * 0.5f), v * (radius - width * 0.5f), color);
        }
    }

    private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        for (int i = 0; i < 4; i++) colors.Add(color);
        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }
}
