using UnityEngine;

/// <summary>
/// Nightmare가 코어에서 쏘는 유도탄(블랙홀을 닮은 탄)의 외형을 움직인다. 표시 전용이며 이동·판정과는 무관하다.
/// - 외형은 작은 검은 원판(사건의 지평선)과 그 가장자리의 빛 고리다. Nightmare 코어의 지평선과 같은 재질 계열을 쓴다.
/// - 풀에서 나올 때마다 점에서 자라나고(코어에서 떨어져 나오는 모습), 숨 쉬듯 맥동하며 빛 고리가 미세하게 깜빡인다.
///   Player를 쫓기 시작하면(Chase) 빛 고리가 밝아진다. 비행 단계는 유도탄(ConstructorHomingMissile)에서 읽기만 한다.
/// - 물리 루트는 건드리지 않는다. 외형 묶음(body)의 배율과 Renderer의 색·세기만 바꾼다.
/// - 표시용 난수는 시간의 함수(PerlinNoise)로 만든다(게임 로직의 난수 순서를 건드리지 않는다).
/// </summary>
[DisallowMultipleComponent]
public sealed class NightmareVoidMissileVisual : MonoBehaviour
{
    [Header("연결")]
    [SerializeField, Tooltip("비행 단계를 읽을 유도탄(같은 오브젝트). 읽기만 한다")] private ConstructorHomingMissile missile;
    [SerializeField, Tooltip("원판과 빛 고리를 묶은 Transform. 이 배율로 자라고 맥동한다")] private Transform body;
    [SerializeField, Tooltip("검은 원판")] private SpriteRenderer horizonRenderer;
    [SerializeField, Tooltip("가장자리의 빛 고리(가산 발광)")] private SpriteRenderer ringRenderer;

    [Header("모습")]
    [SerializeField, Min(.01f), Tooltip("점에서 제 크기로 자라는 시간(초)")] private float growTime = .18f;
    [SerializeField, Range(0f, .3f), Tooltip("맥동으로 커졌다 작아지는 비율")] private float pulseAmount = .07f;
    [SerializeField, Min(0f), Tooltip("맥동 속도(초당 횟수)")] private float pulseRate = 2.4f;
    [SerializeField, Range(0f, 1f), Tooltip("검은 원판의 불투명도")] private float horizonOpacity = 1f;
    [SerializeField, Range(0f, .6f), Tooltip("빛 고리의 미세한 깜빡임")] private float ringFlicker = .2f;
    [SerializeField, Min(0f), Tooltip("Player를 쫓는 동안 빛 고리의 밝기 배율")] private float chaseGlow = 1.5f;
    [SerializeField, Min(.01f), Tooltip("밝기가 바뀌는 데 걸리는 시간(초)")] private float glowTime = .25f;

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    private MaterialPropertyBlock block;
    private Vector3 restScale = Vector3.one;
    private bool hasRestScale;
    private float age, glow = 1f, seed;

    /// <summary>자라난 정도(0 ~ 1). 검증용.</summary>
    public float Grow => Mathf.Clamp01(age / growTime);
    /// <summary>빛 고리에 넣은 밝기 배율(깜빡임 제외). 검증용.</summary>
    public float Glow => glow;
    public SpriteRenderer Horizon => horizonRenderer;
    public SpriteRenderer Ring => ringRenderer;

    private void Awake()
    {
        CacheRestScale();
        // 개체마다 맥동·깜빡임의 박자를 어긋나게 한다(두 발이 똑같이 숨 쉬지 않게). 전역 난수는 쓰지 않는다.
        seed = (GetInstanceID() & 1023) * .137f;
    }

    private void OnEnable()
    {
        // 풀에서 다시 나올 때마다 점에서 자란다.
        CacheRestScale();
        age = 0f;
        glow = 1f;
        Apply();
    }

    private void LateUpdate()
    {
        float delta = Time.deltaTime;
        age += delta;
        bool chasing = missile != null && (missile.CurrentPhase == ConstructorHomingMissile.FlightPhase.Chase || missile.CurrentPhase == ConstructorHomingMissile.FlightPhase.Coast);
        glow = Mathf.MoveTowards(glow, chasing ? chaseGlow : 1f, delta * Mathf.Abs(chaseGlow - 1f) / glowTime);
        Apply();
    }

    /// <summary>미리보기·검증용: 지정한 시각의 모습을 넣는다(다 자란 뒤의 맥동·깜빡임).</summary>
    public void ApplyForPreview(float time)
    {
        CacheRestScale();
        age = Mathf.Max(time, growTime);
        Apply();
    }

    private void CacheRestScale()
    {
        if (hasRestScale || body == null) return;
        restScale = body.localScale;
        hasRestScale = true;
    }

    private void Apply()
    {
        float grow = Grow;
        // 자랄 때는 빠르게 커졌다가 살짝 넘친 뒤 자리를 잡는다.
        float size = grow < 1f ? Mathf.Sin(grow * Mathf.PI * .5f) * (1f + .25f * Mathf.Sin(grow * Mathf.PI)) : 1f;
        size *= 1f + pulseAmount * Mathf.Sin((age * pulseRate + seed) * Mathf.PI * 2f);
        if (body != null) body.localScale = restScale * Mathf.Max(0f, size);

        if (horizonRenderer != null) horizonRenderer.color = new Color(0f, 0f, 0f, horizonOpacity * Mathf.Clamp01(grow * 2f));

        if (ringRenderer == null) return;
        Material material = ringRenderer.sharedMaterial;
        if (material == null || !material.HasProperty(IntensityId)) return;
        float flicker = 1f + ringFlicker * (Mathf.PerlinNoise(seed, age * 9f) * 2f - 1f);
        block ??= new MaterialPropertyBlock();
        ringRenderer.GetPropertyBlock(block);
        block.SetFloat(IntensityId, material.GetFloat(IntensityId) * glow * flicker * Mathf.Clamp01(grow * 2f));
        ringRenderer.SetPropertyBlock(block);
    }
}
