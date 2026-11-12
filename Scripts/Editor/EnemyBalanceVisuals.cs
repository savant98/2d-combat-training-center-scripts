#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>원본 Enemy 미리보기와 패턴 의미를 나타내는 편집기 전용 아이콘을 관리한다.</summary>
public static class EnemyBalanceVisuals
{
    private const int Size = 64;
    private static readonly Dictionary<string, Texture2D> patternIcons = new Dictionary<string, Texture2D>();
    private static readonly Dictionary<int, Sprite> fallbackSprites = new Dictionary<int, Sprite>();

    public static Texture2D GetEnemyPreview(EnemyController prefab)
    {
        if (prefab == null) return null;
        // Unity의 에셋 미리보기 캐시를 사용하며 원본 Prefab과 Material은 변경하지 않는다.
        var preview = AssetPreview.GetAssetPreview(prefab.gameObject);
        if (preview != null) return preview;
        int id = prefab.GetInstanceID();
        if (!fallbackSprites.TryGetValue(id, out var sprite))
        {
            float largestArea = 0;
            foreach (var renderer in prefab.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.sprite == null || !renderer.enabled || renderer.color.a < .01f) continue;
                Vector2 size = renderer.sprite.bounds.size;
                Vector3 scale = renderer.transform.lossyScale;
                float area = Mathf.Abs(size.x * size.y * scale.x * scale.y);
                if (area <= largestArea) continue;
                largestArea = area;
                sprite = renderer.sprite;
            }
            fallbackSprites[id] = sprite;
        }
        if (sprite == null) return null;
        return AssetPreview.GetAssetPreview(sprite) ?? AssetPreview.GetMiniThumbnail(sprite);
    }

    public static bool IsLoadingPreview(EnemyController prefab)
        => prefab != null && AssetPreview.IsLoadingAssetPreview(prefab.gameObject.GetInstanceID());

    public static Texture2D GetPatternIcon(AiAbility ability)
    {
        if (ability == null) return null;
        string key = ability.GetType().Name + "/" + ability.AbilityKey + "/" + (ability.Data != null ? ability.Data.name : string.Empty);
        if (patternIcons.TryGetValue(key, out var cached) && cached != null) return cached;
        uint hash = Hash(key);
        var color = Color.HSVToRGB((hash % 360) / 360f, .44f, .98f);
        var canvas = new IconCanvas();
        string kind = key.ToLowerInvariant();
        canvas.Circle(new Vector2(32, 33), 26f, 1.25f, new Color(color.r, color.g, color.b, .20f));

        if (kind.Contains("minepreparation"))
        {
            DrawJump(canvas, color);
            canvas.Disc(new Vector2(32, 17), 3, color);
        }
        else if (kind.Contains("mine")) DrawMine(canvas, color);
        else if (kind.Contains("blackhole")) DrawOrbit(canvas, color, true);
        else if (kind.Contains("cubebomb") || kind.Contains("bomb")) DrawBomb(canvas, color);
        else if (kind.Contains("cubebounce") || kind.Contains("bounce")) DrawBounce(canvas, color);
        else if (kind.Contains("shockwave") || kind.Contains("groundwave") || kind.Contains("centralarea")) DrawWave(canvas, color);
        else if (kind.Contains("leapslam")) DrawLeapSlam(canvas, color);
        else if (kind.Contains("slam") || kind.Contains("downattack")) DrawSlam(canvas, color);
        else if (kind.Contains("laser")) DrawLaser(canvas, color, kind.Contains("rain") || kind.Contains("storm"));
        else if (kind.Contains("teleport") || kind.Contains("backjump")) DrawTeleport(canvas, color);
        else if (kind.Contains("jump") || kind.Contains("parabolic")) DrawJump(canvas, color);
        else if (kind.Contains("melee") || kind.Contains("slash") || kind.Contains("grab")) DrawMelee(canvas, color);
        else if (kind.Contains("patrol") || kind.Contains("hover") || kind.Contains("spin")) DrawOrbit(canvas, color, false);
        else if (kind.Contains("shoot") || kind.Contains("shot") || kind.Contains("missile") || kind.Contains("burst")) DrawShot(canvas, color);
        else DrawDash(canvas, color, kind.Contains("zigzag"));

        // 같은 동작 계열도 키별 색과 작은 표식으로 구별한다. 텍스트 이름은 카드에서 함께 표시한다.
        for (int i = 0; i < 4; i++)
            canvas.Disc(new Vector2(23 + i * 6, 5), (hash & (1u << i)) != 0 ? 1.6f : .65f, color);
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "EnemyPatternIcon_" + ability.AbilityKey,
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels(canvas.Pixels);
        texture.Apply(false, true);
        patternIcons[key] = texture;
        return texture;
    }

    public static void Clear()
    {
        foreach (var texture in patternIcons.Values)
            if (texture != null) Object.DestroyImmediate(texture);
        patternIcons.Clear();
        fallbackSprites.Clear();
        // AssetPreview가 반환한 텍스처는 Unity 소유이므로 삭제하지 않는다.
    }

    private static uint Hash(string key)
    {
        uint result = 2166136261;
        foreach (char character in key) result = (result ^ character) * 16777619;
        return result;
    }

    private static void Arrow(IconCanvas c, Vector2 from, Vector2 to, Color color, float width = 3f)
    {
        c.Line(from, to, width, color);
        Vector2 back = (from - to).normalized * 8f;
        Vector2 side = new Vector2(-back.y, back.x) * .6f;
        c.Line(to, to + back + side, width, color);
        c.Line(to, to + back - side, width, color);
    }

    private static void DrawDash(IconCanvas c, Color color, bool zigzag)
    {
        if (zigzag)
        {
            c.Line(new Vector2(12, 23), new Vector2(28, 42), 3f, color);
            c.Line(new Vector2(28, 42), new Vector2(34, 24), 3f, color);
            Arrow(c, new Vector2(34, 24), new Vector2(52, 43), color);
        }
        else
        {
            Arrow(c, new Vector2(17, 33), new Vector2(51, 33), color, 4f);
            c.Line(new Vector2(11, 24), new Vector2(25, 24), 2f, color);
            c.Line(new Vector2(11, 42), new Vector2(25, 42), 2f, color);
        }
    }

    private static void DrawLaser(IconCanvas c, Color color, bool multiple)
    {
        for (int n = multiple ? -1 : 0; n <= (multiple ? 1 : 0); n++)
        {
            float x = 32 + n * 13;
            c.Line(new Vector2(x, 15), new Vector2(x, 49), 7f, new Color(color.r, color.g, color.b, .22f));
            c.Line(new Vector2(x, 15), new Vector2(x, 49), 2.5f, color);
            c.Line(new Vector2(x - 4, 49), new Vector2(x + 4, 49), 2f, color);
        }
    }

    private static void DrawBomb(IconCanvas c, Color color)
    {
        c.Circle(new Vector2(30, 28), 13, 3f, color);
        c.Line(new Vector2(35, 40), new Vector2(42, 48), 3f, color);
        c.Line(new Vector2(39, 53), new Vector2(44, 50), 2f, color);
        c.Line(new Vector2(46, 47), new Vector2(51, 47), 2f, color);
        c.Line(new Vector2(44, 53), new Vector2(47, 57), 2f, color);
        c.Line(new Vector2(23, 34), new Vector2(21, 28), 2f, Color.white);
    }

    private static void DrawMine(IconCanvas c, Color color)
    {
        c.Line(new Vector2(12, 19), new Vector2(52, 19), 2f, color);
        c.Arc(new Vector2(32, 20), 14, 0, 180, 3f, color);
        c.Line(new Vector2(32, 34), new Vector2(32, 43), 3f, color);
        c.Line(new Vector2(16, 31), new Vector2(11, 36), 2f, color);
        c.Line(new Vector2(48, 31), new Vector2(53, 36), 2f, color);
        c.Disc(new Vector2(32, 49), 2f, color);
    }

    private static void DrawWave(IconCanvas c, Color color)
    {
        c.Line(new Vector2(11, 18), new Vector2(53, 18), 2f, color);
        c.Arc(new Vector2(32, 18), 9, 0, 180, 2.5f, color);
        c.Arc(new Vector2(32, 18), 18, 0, 180, 2.5f, color);
        c.Arc(new Vector2(32, 18), 27, 20, 160, 2.5f, color);
    }

    private static void DrawBounce(IconCanvas c, Color color)
    {
        c.Line(new Vector2(10, 16), new Vector2(54, 16), 2f, color);
        c.Arc(new Vector2(23, 18), 11, 15, 165, 2.5f, color);
        c.Arc(new Vector2(44, 18), 9, 15, 165, 2.5f, color);
        c.Box(new Rect(16, 39, 14, 14), 2.5f, color);
        c.Line(new Vector2(27, 35), new Vector2(33, 29), 2f, color);
    }

    private static void DrawSlam(IconCanvas c, Color color)
    {
        Arrow(c, new Vector2(32, 51), new Vector2(32, 24), color, 4f);
        c.Line(new Vector2(13, 16), new Vector2(51, 16), 3f, color);
        c.Line(new Vector2(17, 24), new Vector2(12, 29), 2f, color);
        c.Line(new Vector2(47, 24), new Vector2(52, 29), 2f, color);
    }

    // 화면 밖으로 솟았다가 넓은 사각 범위 한가운데로 내려찍는다.
    private static void DrawLeapSlam(IconCanvas c, Color color)
    {
        c.Box(new Rect(11, 14, 42, 26), 2f, new Color(color.r, color.g, color.b, .55f));
        Arrow(c, new Vector2(20, 30), new Vector2(20, 55), color, 2.5f);
        Arrow(c, new Vector2(40, 57), new Vector2(40, 22), color, 4f);
        c.Line(new Vector2(31, 15), new Vector2(26, 20), 2f, color);
        c.Line(new Vector2(49, 15), new Vector2(54, 20), 2f, color);
    }

    private static void DrawJump(IconCanvas c, Color color)
    {
        c.Arc(new Vector2(32, 22), 19, 10, 170, 3f, color);
        Arrow(c, new Vector2(48, 35), new Vector2(51, 23), color, 2.5f);
        c.Line(new Vector2(10, 16), new Vector2(23, 16), 2f, color);
        c.Line(new Vector2(44, 16), new Vector2(55, 16), 2f, color);
    }

    private static void DrawTeleport(IconCanvas c, Color color)
    {
        c.Arc(new Vector2(18, 33), 17, 100, 260, 3f, color);
        c.Arc(new Vector2(46, 33), 17, -80, 80, 3f, color);
        Arrow(c, new Vector2(22, 33), new Vector2(44, 33), color, 2.5f);
        c.Line(new Vector2(31, 47), new Vector2(33, 51), 2f, color);
    }

    private static void DrawMelee(IconCanvas c, Color color)
    {
        c.Line(new Vector2(18, 18), new Vector2(45, 48), 5f, color);
        c.Line(new Vector2(15, 28), new Vector2(29, 15), 3f, color);
        c.Line(new Vector2(43, 47), new Vector2(51, 51), 3f, color);
        c.Arc(new Vector2(24, 35), 25, -30, 70, 2f, new Color(color.r, color.g, color.b, .5f));
    }

    private static void DrawOrbit(IconCanvas c, Color color, bool blackHole)
    {
        c.Arc(new Vector2(32, 33), 19, 30, 325, 3f, color);
        Arrow(c, new Vector2(45, 48), new Vector2(49, 42), color, 2f);
        if (blackHole)
        {
            c.Circle(new Vector2(32, 33), 10, 2.5f, color);
            c.Disc(new Vector2(32, 33), 4, color);
        }
        else c.Box(new Rect(26, 27, 12, 12), 2f, color);
    }

    private static void DrawShot(IconCanvas c, Color color)
    {
        c.Disc(new Vector2(17, 33), 4, color);
        for (int i = -1; i <= 1; i++)
        {
            var end = new Vector2(48, 33 + i * 16);
            c.Line(new Vector2(25, 33 + i * 3), end - new Vector2(5, 0), 2.5f, color);
            c.Disc(end, 3f, color);
        }
    }

    private sealed class IconCanvas
    {
        public readonly Color[] Pixels = new Color[Size * Size];

        public void Line(Vector2 from, Vector2 to, float width, Color color)
        {
            float radius = width * .5f;
            Vector2 span = to - from;
            float length = span.sqrMagnitude;
            Paint(Mathf.Min(from.x, to.x) - radius - 1, Mathf.Min(from.y, to.y) - radius - 1,
                Mathf.Max(from.x, to.x) + radius + 1, Mathf.Max(from.y, to.y) + radius + 1,
                p => Vector2.Distance(p, from + span * (length > 0 ? Mathf.Clamp01(Vector2.Dot(p - from, span) / length) : 0)), radius, color);
        }

        public void Disc(Vector2 center, float radius, Color color)
            => Paint(center.x - radius - 1, center.y - radius - 1, center.x + radius + 1, center.y + radius + 1,
                p => Vector2.Distance(p, center), radius, color);

        public void Circle(Vector2 center, float radius, float width, Color color)
            => Paint(center.x - radius - width, center.y - radius - width, center.x + radius + width, center.y + radius + width,
                p => Mathf.Abs(Vector2.Distance(p, center) - radius), width * .5f, color);

        public void Arc(Vector2 center, float radius, float from, float to, float width, Color color)
        {
            int steps = Mathf.CeilToInt(Mathf.Abs(to - from) / 9f);
            Vector2 previous = center + Direction(from) * radius;
            for (int i = 1; i <= steps; i++)
            {
                Vector2 next = center + Direction(Mathf.Lerp(from, to, i / (float)steps)) * radius;
                Line(previous, next, width, color);
                previous = next;
            }
        }

        public void Box(Rect rect, float width, Color color)
        {
            Line(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), width, color);
            Line(new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), width, color);
            Line(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), width, color);
            Line(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin), width, color);
        }

        private static Vector2 Direction(float degrees)
            => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

        private void Paint(float minX, float minY, float maxX, float maxY, System.Func<Vector2, float> distance, float radius, Color color)
        {
            for (int y = Mathf.Max(0, Mathf.FloorToInt(minY)); y <= Mathf.Min(Size - 1, Mathf.CeilToInt(maxY)); y++)
                for (int x = Mathf.Max(0, Mathf.FloorToInt(minX)); x <= Mathf.Min(Size - 1, Mathf.CeilToInt(maxX)); x++)
                {
                    float alpha = Mathf.Clamp01(radius + .75f - distance(new Vector2(x + .5f, y + .5f))) * color.a;
                    if (alpha <= 0) continue;
                    int index = y * Size + x;
                    Color old = Pixels[index];
                    float combined = alpha + old.a * (1 - alpha);
                    Pixels[index] = new Color((color.r * alpha + old.r * old.a * (1 - alpha)) / combined,
                        (color.g * alpha + old.g * old.a * (1 - alpha)) / combined,
                        (color.b * alpha + old.b * old.a * (1 - alpha)) / combined, combined);
                }
        }
    }
}
#endif
