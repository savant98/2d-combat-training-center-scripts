using UnityEngine;

// 피해가 있는 몸통의 실제 Collider 외곽을 표시한다. 준비·회수·중단에는 잔상을 남기지 않는다.
[DefaultExecutionOrder(110)]
[DisallowMultipleComponent]
public sealed class EnemyBodyDangerVisual : MonoBehaviour
{
    [SerializeField] private TouchDamage source;
    [SerializeField] private Material material;
    private LineRenderer outline;
    private Collider2D body;
    private Vector3[] points;
    private Vector2[] polygon;
    public bool IsVisible => outline != null && outline.enabled;
    public LineRenderer Outline => outline;

    private void Awake()
    {
        body = source != null ? source.GetComponent<Collider2D>() : null;
        if (body is PolygonCollider2D shape) polygon = shape.GetPath(0);
        int count = body is CircleCollider2D ? 64 : polygon != null ? polygon.Length : 4;
        points = new Vector3[count];
        var view = new GameObject(name + "_BodyDanger");
        view.layer = gameObject.layer;
        outline = view.AddComponent<LineRenderer>();
        outline.sharedMaterial = material;
        outline.useWorldSpace = true;
        outline.loop = true;
        outline.positionCount = count;
        outline.numCornerVertices = 3;
        outline.textureMode = LineTextureMode.Stretch;
        outline.sortingLayerName = "Particle";
        outline.sortingOrder = 3;
        outline.enabled = false;
    }

    private void LateUpdate()
    {
        if (outline == null) return;
        outline.enabled = source != null && source.IsBodyContact && source.CurrentDamage > 0f;
        if (!outline.enabled || body == null) return;
        FixedMotionBody.GetPresentationPose(body.transform, out var position, out var rotation);
        Quaternion turn = rotation * Quaternion.Inverse(body.transform.rotation);
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 local;
            if (body is CircleCollider2D circle)
            {
                float angle = i * Mathf.PI * 2f / points.Length;
                // CircleCollider2D는 비균일 배율에서도 큰 축 기준 원형이다.
                Vector3 scale = body.transform.lossyScale;
                float radius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                Vector3 world = body.bounds.center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
                points[i] = position + turn * (world - body.transform.position);
                continue;
            }
            else if (body is BoxCollider2D box)
            {
                local = box.offset + Vector2.Scale(box.size * .5f,
                    new Vector2(i == 0 || i == 3 ? -1 : 1, i < 2 ? -1 : 1));
            }
            else if (polygon != null) local = body.offset + polygon[i];
            else { outline.enabled = false; return; }
            points[i] = position + turn * (body.transform.TransformPoint(local) - body.transform.position);
        }
        outline.widthMultiplier = Mathf.Clamp(Mathf.Min(body.bounds.size.x, body.bounds.size.y) * .055f, .045f, .12f);
        outline.SetPositions(points);
    }

    private void OnDisable()
    {
        if (outline != null) outline.enabled = false;
    }

    private void OnDestroy()
    {
        if (outline != null) Destroy(outline.gameObject);
    }
}
