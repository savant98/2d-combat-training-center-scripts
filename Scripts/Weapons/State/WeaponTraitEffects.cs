using UnityEngine;

public static class WeaponTraitEffects
{
    public static readonly Color HeatColor = new Color(1f, 0.29f, 0.07f);
    public static readonly Color RailColor = new Color(1f, 0.43f, 0.82f);
    private static Material lineMaterial;
    private static Sprite quadSprite, flameSprite;

    public static SpriteRenderer MakeSprite(string name, Transform parent, Material material, int order)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var renderer = obj.AddComponent<SpriteRenderer>();
        if (quadSprite == null)
        {
            var white = Texture2D.whiteTexture;
            quadSprite = Sprite.Create(white, new Rect(0, 0, white.width, white.height), Vector2.one * 0.5f,
                white.width, 0, SpriteMeshType.FullRect);
            quadSprite.hideFlags = HideFlags.HideAndDontSave;
        }
        renderer.sprite = quadSprite;
        if (material != null) renderer.sharedMaterial = material;
        renderer.sortingLayerName = "Particle";
        renderer.sortingOrder = order;
        return renderer;
    }

    public static Sprite FlameSprite
    {
        get
        {
            if (flameSprite != null) return flameSprite;
            const int width = 36, height = 48;
            Vector2[] edge =
            {
                new(0.50f, 0.02f), new(0.19f, 0.10f), new(0.10f, 0.33f), new(0.16f, 0.57f),
                new(0.30f, 0.44f), new(0.40f, 0.70f), new(0.43f, 0.97f), new(0.65f, 0.79f),
                new(0.74f, 0.52f), new(0.84f, 0.63f), new(0.91f, 0.34f), new(0.80f, 0.14f)
            };
            Vector2[] core =
            {
                new(0.49f, 0.08f), new(0.33f, 0.17f), new(0.33f, 0.34f), new(0.53f, 0.62f),
                new(0.54f, 0.41f), new(0.68f, 0.29f), new(0.67f, 0.17f)
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Color sum = Color.clear;
                for (int sample = 0; sample < 4; sample++)
                {
                    var point = new Vector2((x + 0.25f + (sample % 2) * 0.5f) / width,
                        (y + 0.25f + (sample / 2) * 0.5f) / height);
                    if (!Inside(point, edge)) continue;
                    sum += Inside(point, core) ? new Color(1f, 0.90f, 0.40f)
                        : Color.Lerp(new Color(1f, 0.21f, 0.045f), new Color(1f, 0.67f, 0.12f), point.y);
                }
                if (sum.a > 0f) pixels[y * width + x] = new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a * 0.25f);
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "WeaponBurnIcon" };
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            flameSprite = Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.one * 0.5f, 115f, 0, SpriteMeshType.FullRect);
            flameSprite.hideFlags = HideFlags.HideAndDontSave;
            return flameSprite;
        }
    }

    private static bool Inside(Vector2 point, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Vector2 a = polygon[i], b = polygon[j];
            if ((a.y > point.y) != (b.y > point.y) &&
                point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    public static LineRenderer MakeLine(string name, Transform parent = null)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var line = obj.AddComponent<LineRenderer>();
        if (lineMaterial == null)
            lineMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
        line.sharedMaterial = lineMaterial;
        line.useWorldSpace = true;
        line.numCapVertices = 3;
        line.numCornerVertices = 3;
        line.sortingLayerName = "Particle";
        line.sortingOrder = 32;
        return line;
    }

    public static void SetLine(LineRenderer line, Vector3 from, Vector3 to, float width, Color color)
    {
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.startColor = Color.Lerp(color, Color.white, 0.25f);
        line.endColor = color;
        line.startWidth = width;
        line.endWidth = width * 0.75f;
    }

    public static bool ApplyEnemyHit(EnemyHealth enemy, WeaponBaseSO.DamageInfo info, Vector2 point)
    {
        if (enemy == null || !enemy.isActiveAndEnabled || enemy.currentHP <= 0f || enemy.IsDamageBlocked) return false;
        var shield = enemy.enemyShieldSystem;
        if (shield != null && shield.IsSpawnLocked) return false;
        float hp = enemy.currentHP;
        float before = shield != null ? shield.CurrentShield : 0f;
        int breaks = shield != null ? shield.SuccessfulBreakCount : 0;
        bool practiceHit = enemy is TrainingDummyHealth && info.damage > 0f
            && (shield == null || shield.IsBreaking);
        if (shield == null) enemy.TakeDamage(info, point);
        else shield.TakeDamage(info, point);
        return practiceHit || enemy.currentHP < hp
            || (shield != null && (shield.CurrentShield < before || shield.SuccessfulBreakCount > breaks));
    }

    public static PlayerWeaponAbility Attack(CharacterController2D owner) =>
        (owner as PlayerController)?.AbilityManager?.AttackAbility;

    public static void RailFollowUp(CharacterController2D owner, Vector2 origin, EnemyHealth enemy,
        Vector2 point, WeaponBaseSO.DamageInfo info)
    {
        Vector2 heading = (point - origin).normalized;
        Vector2 offset = Vector2.Perpendicular(heading) * 0.55f;
        info.damage *= 0.45f;
        info.knockbackFacing = 0;
        var attack = Attack(owner);
        var rail = info.sourceWeapon as RailgunWeaponSO
            ?? attack?.GetWeaponSet(attack.ExecutingSetIndex)?.normal as RailgunWeaponSO;
        WeaponTraitFeedbackSO.Play(WeaponTraitCue.RailEcho, origin, heading);
        for (int i = -1; i <= 1; i += 2)
        {
            Vector2 muzzle = origin + offset * i;
            Vector2 axis = (point - muzzle).normalized;
            if (rail != null && rail.tracerPrefab != null && WorldEntityPool.Instance != null)
            {
                var angle = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.right, axis));
                var tracer = WorldEntityPool.Instance.Spawn(rail.tracerPrefab, muzzle, angle);
                tracer.Play(muzzle, axis, Vector2.Distance(muzzle, point), false);
            }
            else
            {
                var line = MakeLine("RailgunFollowUp");
                SetLine(line, muzzle, point, 0.10f, RailColor);
                Object.Destroy(line.gameObject, 0.16f);
            }
            ApplyEnemyHit(enemy, info, point);
        }
    }
}
