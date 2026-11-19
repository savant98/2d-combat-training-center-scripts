using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 일격필살이 소유한 시간 정지와 외형만 복구한다. 메뉴의 별도 정지는 유지한다.
public sealed class SwordFinisherCinematic : MonoBehaviour
{
    private struct Order { public Renderer renderer; public int layer, order; }
    private struct CrackStroke
    {
        public LineRenderer renderer;
        public Vector3[] points;
        public float revealFrom, revealTo;
    }

    private readonly List<Order> orders = new();
    private readonly List<CrackStroke> cracks = new();
    private CharacterController2D actor;
    private GameTimeController time;
    private AnimatorUpdateMode updateMode;
    private float animatorSpeed;
    private GameObject backdrop;
    private Sprite sprite;
    private Texture2D texture;
    private Material lineMaterial;
    private bool ownsPause;
    private int layer;
    public int Generation { get; private set; }
    public bool IsRunning => ownsPause;
    public static bool MenuBlocked => UIManager.Instance != null && UIManager.Instance.BlocksGameplayInput;
    public static SwordFinisherCinematic For(CharacterController2D owner) => owner != null ? owner.GetComponent<SwordFinisherCinematic>() : null;
    public static float DeltaFor(CharacterController2D owner)
    {
        var cinematic = For(owner);
        return cinematic != null && cinematic.IsRunning ? (MenuBlocked ? 0f : Time.unscaledDeltaTime) : Time.deltaTime;
    }
    public static void Begin(CharacterController2D owner)
    {
        if (owner == null) return;
        var cinematic = For(owner);
        if (cinematic == null) cinematic = owner.gameObject.AddComponent<SwordFinisherCinematic>();
        cinematic.StartPresentation(owner);
    }
    public static void End(CharacterController2D owner)
    {
        var cinematic = For(owner);
        if (cinematic != null) cinematic.Finish();
    }
    public static void End(CharacterController2D owner, int generation)
    {
        var cinematic = For(owner);
        if (cinematic != null && cinematic.Generation == generation) cinematic.Finish();
    }
    private void StartPresentation(CharacterController2D owner)
    {
        Finish();
        Generation++;
        actor = owner;
        time = GameTimeController.Instance;
        if (time == null) return;
        updateMode = actor.animator.updateMode;
        animatorSpeed = actor.animator.speed;
        actor.animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        time.PushPause(); ownsPause = true;
        var layers = SortingLayer.layers;
        layer = layers[layers.Length - 1].id;
        texture = new Texture2D(1, 1); texture.SetPixel(0, 0, Color.white); texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * .5f, 1f);
        backdrop = new GameObject("FinisherDarkness");
        var black = backdrop.AddComponent<SpriteRenderer>();
        black.sprite = sprite; black.color = new Color(0f, 0f, .012f, .94f);
        black.sortingLayerID = layer; black.sortingOrder = 30000;
        var foreground = new List<Renderer>(owner.GetComponentsInChildren<Renderer>(true));
        foreach (var enemy in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            foreground.AddRange(enemy.GetComponentsInChildren<Renderer>(true));
        Promote(foreground.ToArray());
        PositionBackdrop();
    }
    public void Promote(Renderer[] renderers)
    {
        foreach (var renderer in renderers.OrderBy(r => SortingLayer.GetLayerValueFromID(r.sortingLayerID)).ThenBy(r => r.sortingOrder))
        {
            bool exists = false;
            foreach (var order in orders) if (order.renderer == renderer) { exists = true; break; }
            if (exists) continue;
            orders.Add(new Order { renderer = renderer, layer = renderer.sortingLayerID, order = renderer.sortingOrder });
            renderer.sortingLayerID = layer;
            renderer.sortingOrder = 31000 + Mathf.Min(orders.Count, 900);
        }
    }
    private void PositionBackdrop()
    {
        var camera = Camera.main;
        if (backdrop == null || camera == null) return;
        Vector3 center = camera.ViewportToWorldPoint(new Vector3(.5f, .5f, Mathf.Abs(camera.transform.position.z)));
        center.z = 0f; backdrop.transform.position = center;
        backdrop.transform.localScale = new Vector3(camera.orthographicSize * camera.aspect * 2.2f, camera.orthographicSize * 2.2f, 1f);
    }
    public void Crack(Vector2 start, Vector2 direction, float length)
    {
        if (!ownsPause || cracks.Count > 0) return;
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) return;
        lineMaterial = new Material(shader);
        Vector2 normal = new Vector2(-direction.y, direction.x);

        // 절단면 중앙에서 양쪽으로 균열이 뻗고, 본선이 지나간 곳부터 가지가 번진다.
        const int segments = 44;
        const int middle = segments / 2;
        const float seamDuration = .74f;
        var seam = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float offset = Mathf.Sin(i * 8.91f) * .065f + Mathf.Sin(i * 3.27f) * .036f;
            seam[i] = start + direction * (length * i / segments) + normal * offset;
        }

        var seamColor = new Color(.74f, .91f, 1f, .95f);
        for (int side = -1; side <= 1; side += 2)
        {
            var points = new Vector3[middle + 1];
            for (int j = 0; j <= middle; j++)
                points[j] = seam[middle + side * j];
            AddCrackStroke(points, .046f, .03f, seamColor, seamColor, 0f, seamDuration);
        }

        var rootColor = new Color(.88f, .92f, 1f, .93f);
        var tipColor = new Color(.58f, .79f, 1f, .64f);
        const int branches = 20;
        for (int i = 0; i < branches; i++)
        {
            int seamIndex = (i * segments + middle) / branches;
            float arrival = Mathf.Abs(seamIndex - middle) / (float)middle * seamDuration;
            float from = arrival + .034f + (i % 3) * .028f;
            float until = Mathf.Min(1f, from + .25f + (i % 4) * .035f);
            var points = new Vector3[5];
            Vector2 root = seam[seamIndex];
            float sign = i % 2 == 0 ? 1f : -1f;
            for (int j = 0; j < points.Length; j++)
                points[j] = root + normal * (sign * j * (.21f + (i % 5) * .078f))
                    + direction * (Mathf.Sin(i * 7f + j * 3f) * j * .13f + sign * j * .19f);

            AddCrackStroke(points, .031f, .008f, rootColor, tipColor, from, until);
            if (i % 3 == 0)
            {
                Vector2 fork = points[2];
                float forkFrom = Mathf.Lerp(from, until, .67f);
                AddCrackStroke(new Vector3[]
                {
                    fork,
                    fork + direction * sign * .24f + normal * sign * .20f,
                    fork + direction * sign * .55f + normal * sign * .28f,
                }, .019f, .004f, rootColor, tipColor, forkFrom, Mathf.Min(1f, forkFrom + .22f));
            }
        }
    }

    public void SetCrackProgress(float progress)
    {
        foreach (var stroke in cracks)
        {
            float visible = Mathf.InverseLerp(stroke.revealFrom, stroke.revealTo, progress);
            var line = stroke.renderer;
            if (visible <= 0f) continue;

            float cursor = visible * (stroke.points.Length - 1);
            int last = Mathf.Min(Mathf.FloorToInt(cursor), stroke.points.Length - 2);
            line.positionCount = last + 2;
            for (int i = 0; i <= last; i++)
                line.SetPosition(i, stroke.points[i]);
            line.SetPosition(last + 1, Vector3.Lerp(stroke.points[last], stroke.points[last + 1], cursor - last));
            line.enabled = true;
        }
    }

    private void AddCrackStroke(Vector3[] points, float startWidth, float endWidth, Color startColor, Color endColor,
        float revealFrom, float revealTo)
    {
        var node = new GameObject("GlassCrack");
        node.transform.SetParent(backdrop.transform, false);
        var line = node.AddComponent<LineRenderer>();
        line.enabled = false;
        line.sharedMaterial = lineMaterial;
        line.useWorldSpace = true;
        line.sortingLayerID = layer;
        line.sortingOrder = 32000;
        line.startWidth = startWidth;
        line.endWidth = endWidth;
        line.startColor = startColor;
        line.endColor = endColor;
        cracks.Add(new CrackStroke { renderer = line, points = points, revealFrom = revealFrom, revealTo = revealTo });
    }

    private void LateUpdate()
    {
        if (!ownsPause) return;
        if (actor == null || !actor.gameObject.activeInHierarchy) { Finish(); return; }
        actor.animator.speed = MenuBlocked ? 0f : animatorSpeed;
        PositionBackdrop();
    }
    public void Finish()
    {
        foreach (var order in orders)
            if (order.renderer != null) { order.renderer.sortingLayerID = order.layer; order.renderer.sortingOrder = order.order; }
        orders.Clear(); cracks.Clear();
        if (backdrop != null)
        {
            backdrop.SetActive(false);
            Destroy(backdrop);
        }
        if (sprite != null) Destroy(sprite);
        if (texture != null) Destroy(texture);
        if (lineMaterial != null) Destroy(lineMaterial);
        if (ownsPause)
        {
            ownsPause = false;
            if (actor != null && actor.animator != null) { actor.animator.updateMode = updateMode; actor.animator.speed = animatorSpeed; }
            if (time != null) time.PopPause();
        }
    }
    private void OnDisable() => Finish();
}
