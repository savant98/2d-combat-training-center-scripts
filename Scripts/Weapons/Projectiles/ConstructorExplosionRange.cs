using UnityEngine;

// 피해 판정과 별개인 범위 표시다. 폭발이 실제 사용한 반경을 받아 원을 그린다.
public sealed class ConstructorExplosionRange : WorldEntityBase
{
    [SerializeField] private Transform visualRoot;
    [SerializeField] private LineRenderer outline;
    [SerializeField] private SpriteRenderer fill;
    [SerializeField, Range(24, 128)] private int circleSegments = 64;
    [SerializeField, Min(0.01f)] private float outlineWidth = 0.08f;

    public float Radius { get; private set; }
    public float Age => age;
    public bool IsDisplaying => initialized && !isReturning;
    public bool IsWarning { get; private set; }
    public int LeaseId { get; private set; }

    private Vector3[] points;
    private Vector3 fillRestScale;
    private Color outlineStartColor;
    private Color outlineEndColor;
    private Color fillColor;
    private float age;
    private float lifeTime;
    private bool initialized;

    protected override void Awake()
    {
        base.Awake();
        if (visualRoot == null) visualRoot = transform.Find("Visual");
        if (outline == null) outline = GetComponentInChildren<LineRenderer>(true);
        if (fill != null)
        {
            fillRestScale = fill.transform.localScale;
            fillColor = fill.color;
        }
        if (outline != null)
        {
            outlineStartColor = outline.startColor;
            outlineEndColor = outline.endColor;
        }
        if (col != null) col.enabled = false;
        HideVisual();
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        initialized = false;
        IsWarning = false;
        unchecked { LeaseId++; }
        age = Radius = 0f;
        owner = null;
        HideVisual();
    }

    public void InitializeRange(float radius, float duration, CharacterController2D source = null)
        => InitializeDisplay(radius, duration, source, false);

    public void InitializeWarning(float radius, float duration, CharacterController2D source = null)
        => InitializeDisplay(radius, duration, source, true);

    private void InitializeDisplay(float radius, float duration, CharacterController2D source, bool warning)
    {
        Radius = Mathf.Max(0f, radius);
        lifeTime = Mathf.Max(0.05f, duration);
        age = 0f;
        IsWarning = warning;
        owner = source;
        if (Radius <= 0f || outline == null || WorldEntityPool.Instance == null)
        {
            ReturnToPool();
            return;
        }
        initialized = true;
        if (visualRoot != null) visualRoot.localScale = Vector3.one;
        int count = Mathf.Clamp(circleSegments, 24, 128);
        if (points == null || points.Length != count) points = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.PI * 2f * i / count;
            points[i] = new Vector3(Mathf.Cos(angle) * Radius, Mathf.Sin(angle) * Radius, 0f);
        }
        // 원의 좌표만 반경으로 계산해 외곽선의 두께는 폭발 크기와 관계없이 유지한다.
        outline.useWorldSpace = false;
        outline.loop = true;
        outline.positionCount = count;
        outline.SetPositions(points);
        outline.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        outline.widthMultiplier = outlineWidth;
        outline.enabled = true;
        if (fill != null)
        {
            fill.transform.localScale = fillRestScale * Radius;
            fill.enabled = true;
        }
        UpdateFade();
    }

    private void Update()
    {
        if (!initialized || isReturning) return;
        age += Time.deltaTime;
        if (age >= lifeTime) { ReturnToPool(); return; }
        UpdateFade();
    }

    private void UpdateFade()
    {
        if (IsWarning)
        {
            float progress = Mathf.Clamp01(age / lifeTime);
            // 폭발이 가까워질수록 빠르게 밝아지는 예고선이다. 표시 반경은 움직이지 않는다.
            float phase = (age * 3f + age * age * 4f / lifeTime) * Mathf.PI * 2f;
            float pulse = 0.72f + Mathf.Sin(phase) * 0.28f;
            var startWarning = outlineStartColor;
            var endWarning = outlineEndColor;
            startWarning.a *= pulse;
            endWarning.a *= pulse;
            outline.startColor = startWarning;
            outline.endColor = endWarning;
            if (fill != null)
            {
                var warningFill = fillColor;
                warningFill.a *= pulse * Mathf.Lerp(0.4f, 0.8f, progress);
                fill.color = warningFill;
            }
            return;
        }
        float alpha = 1f - Mathf.SmoothStep(0f, 1f, age / lifeTime);
        var start = outlineStartColor; start.a *= alpha;
        var end = outlineEndColor; end.a *= alpha;
        outline.startColor = start;
        outline.endColor = end;
        if (fill != null)
        {
            var tint = fillColor; tint.a *= alpha;
            fill.color = tint;
        }
    }

    private void HideVisual()
    {
        if (outline != null) outline.enabled = false;
        if (fill != null)
        {
            fill.enabled = false;
            fill.transform.localScale = fillRestScale;
            fill.color = fillColor;
        }
    }

    protected override void ReturnToPool()
    {
        if (isReturning) return;
        initialized = false;
        IsWarning = false;
        HideVisual();
        base.ReturnToPool();
    }

    public override void ForceReturnToPool()
    {
        if (isReturning) return;
        base.ForceReturnToPool();
    }

    private void OnDisable()
    {
        initialized = false;
        IsWarning = false;
        HideVisual();
    }
}
