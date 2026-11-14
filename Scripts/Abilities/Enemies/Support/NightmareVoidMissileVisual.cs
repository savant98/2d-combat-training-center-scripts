using UnityEngine;

/// <summary>
/// Nightmare가 코어에서 쏘는 유도탄(블랙홀을 닮은 탄)의 외형을 움직인다. 표시 전용이며 이동·판정과는 무관하다.
/// - 외형은 검은 원판(사건의 지평선)과 그 가장자리의 빛 고리, 그 둘레를 서로 다른 속도로 도는 나선 강착원반 두 장이다.
///   Nightmare 코어의 지평선·강착원반과 같은 재질 계열을 쓴다. 원반은 비행 방향과 무관하게 화면 기준으로 늘 같은 방향으로 돈다.
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
    [SerializeField, Tooltip("빛 고리 둘레를 빠르게 도는 안쪽 강착원반(가산 발광, 원판 묶음 안)")] private SpriteRenderer diskInnerRenderer;
    [SerializeField, Tooltip("안쪽 원반 바깥을 느리게 도는 바깥쪽 강착원반(가산 발광, 원판 묶음 안)")] private SpriteRenderer diskOuterRenderer;

    [Header("모습")]
    [SerializeField, Min(.01f), Tooltip("점에서 제 크기로 자라는 시간(초)")] private float growTime = .18f;
    [SerializeField, Range(0f, .3f), Tooltip("맥동으로 커졌다 작아지는 비율")] private float pulseAmount = .07f;
    [SerializeField, Min(0f), Tooltip("맥동 속도(초당 횟수)")] private float pulseRate = 2.4f;
    [SerializeField, Range(0f, 1f), Tooltip("검은 원판의 불투명도")] private float horizonOpacity = 1f;
    [SerializeField, Range(0f, .6f), Tooltip("빛 고리의 미세한 깜빡임")] private float ringFlicker = .2f;
    [SerializeField, Min(0f), Tooltip("Player를 쫓는 동안 빛 고리의 밝기 배율")] private float chaseGlow = 1.5f;
    [SerializeField, Min(.01f), Tooltip("밝기가 바뀌는 데 걸리는 시간(초)")] private float glowTime = .25f;

    [Header("강착원반(회전)")]
    [SerializeField, Tooltip("안쪽 원반이 도는 속도(도/초, +가 반시계). Nightmare 코어·Admin 블랙홀과 같은 210")] private float diskInnerSpin = 210f;
    [SerializeField, Tooltip("바깥쪽 원반이 도는 속도(도/초). Nightmare 코어·Admin 블랙홀과 같은 95")] private float diskOuterSpin = 95f;

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    private MaterialPropertyBlock block;
    private Vector3 restScale = Vector3.one;
    private bool hasRestScale;
    private float age, glow = 1f, seed;
    private float diskInnerAngle, diskOuterAngle;

    /// <summary>자라난 정도(0 ~ 1). 검증용.</summary>
    public float Grow => Mathf.Clamp01(age / growTime);
    /// <summary>빛 고리에 넣은 밝기 배율(깜빡임 제외). 검증용.</summary>
    public float Glow => glow;
    public SpriteRenderer Horizon => horizonRenderer;
    public SpriteRenderer Ring => ringRenderer;
    public SpriteRenderer DiskInner => diskInnerRenderer;
    public SpriteRenderer DiskOuter => diskOuterRenderer;
    /// <summary>강착원반의 화면 기준 각도(도, 0 ~ 360, 반시계가 +). 검증용.</summary>
    public float DiskInnerAngle => diskInnerAngle;
    public float DiskOuterAngle => diskOuterAngle;

    private void Awake()
    {
        CacheRestScale();
        // 개체마다 맥동·깜빡임의 박자를 어긋나게 한다(두 발이 똑같이 숨 쉬지 않게). 전역 난수는 쓰지 않는다.
        seed = (GetInstanceID() & 1023) * .137f;
    }

    private void OnEnable()
    {
        // 풀에서 다시 나올 때마다 점에서 자란다. 원반의 시작 각도도 개체마다 어긋나게 둔다(두 발이 같은 무늬로 돌지 않게).
        CacheRestScale();
        age = 0f;
        glow = 1f;
        diskInnerAngle = Mathf.Repeat(seed * 263f, 360f);
        diskOuterAngle = Mathf.Repeat(seed * 263f + 137f, 360f);
        Apply();
    }

    private void LateUpdate()
    {
        float delta = Time.deltaTime;
        age += delta;
        // 강착원반은 비행 방향(Visual의 회전)과 무관하게 화면 기준으로 늘 같은 방향으로 돈다.
        diskInnerAngle = Mathf.Repeat(diskInnerAngle + diskInnerSpin * delta, 360f);
        diskOuterAngle = Mathf.Repeat(diskOuterAngle + diskOuterSpin * delta, 360f);
        bool chasing = missile != null && (missile.CurrentPhase == ConstructorHomingMissile.FlightPhase.Chase || missile.CurrentPhase == ConstructorHomingMissile.FlightPhase.Coast);
        glow = Mathf.MoveTowards(glow, chasing ? chaseGlow : 1f, delta * Mathf.Abs(chaseGlow - 1f) / glowTime);
        Apply();
    }

    /// <summary>미리보기·검증용: 지정한 시각의 모습을 넣는다(다 자란 뒤의 맥동·깜빡임).</summary>
    public void ApplyForPreview(float time)
    {
        CacheRestScale();
        age = Mathf.Max(time, growTime);
        diskInnerAngle = Mathf.Repeat(time * diskInnerSpin, 360f);
        diskOuterAngle = Mathf.Repeat(137f + time * diskOuterSpin, 360f);
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

        float shown = Mathf.Clamp01(grow * 2f);
        float flicker = 1f + ringFlicker * (Mathf.PerlinNoise(seed, age * 9f) * 2f - 1f);
        SetIntensity(ringRenderer, glow * flicker * shown);

        // 강착원반: 원판 묶음과 함께 자라고 맥동하며, 화면 기준 각도로 돈다(비행 방향으로 도는 Visual의 회전을 덮어쓴다).
        // 쫓기 시작하면 빛 고리와 함께 밝아진다(깜빡임은 빛 고리만).
        Spin(diskInnerRenderer, diskInnerAngle, glow * shown);
        Spin(diskOuterRenderer, diskOuterAngle, glow * shown);
    }

    private void Spin(SpriteRenderer disk, float angle, float brightness)
    {
        if (disk == null) return;
        disk.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        SetIntensity(disk, brightness);
    }

    private void SetIntensity(SpriteRenderer target, float brightness)
    {
        if (target == null) return;
        Material material = target.sharedMaterial;
        if (material == null || !material.HasProperty(IntensityId)) return;
        block ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(block);
        block.SetFloat(IntensityId, material.GetFloat(IntensityId) * brightness);
        target.SetPropertyBlock(block);
    }
}
