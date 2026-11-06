using UnityEngine;

// Admin 블랙홀 패턴의 낙하 큐브(플레이어와 같은 1.5칸). 천장 아래에서 나타나 중력으로 곧게 떨어진다.
// 이동·바닥 판정은 MovingEntityBase의 RaycastMotor2D가, 플레이어 판정은 ProjectileBase의 경로 판정(모터 CastAttackPath)이 맡는다.
// 떨어질 자리는 생성할 때 모터의 RaycastEnvironment로 찾아 바닥 경고(표식 + 위로 자라는 낙하 경로 기둥)를 둔다.
// 경고는 처음부터 잘 보이는 밝기로 크게 나타났다 줄어들고, 착지까지 남은 시간이 줄수록 커지고 밝아지며 맥동이 빨라진다.
// 착지 직전에는 흰빛으로 번쩍인다. 남은 시간은 지금 높이·속도에서 중력과 최대 낙하 속도로 계산한다.
// 바닥이나 플레이어에 닿으면 부서진다(플레이어가 무적이면 지나친다).
public sealed class AdminFallingCube : ProjectileBase
{
    [Header("Fall")]
    [SerializeField] private Transform visualRoot;
    [SerializeField, Min(0.1f)] private float gravity = 28f;
    [SerializeField, Min(0.1f)] private float maxFallSpeed = 22f;
    [Tooltip("떨어지며 도는 속도의 최대값(도/초, 개체마다 무작위)")]
    [SerializeField, Min(0f)] private float spinSpeedRange = 40f;
    [SerializeField, Min(0.01f)] private float appearDuration = 0.14f;
    [SerializeField, Min(0.5f)] private float lifeTime = 5f;

    [Header("Warning")]
    [SerializeField] private Transform warning;
    [Tooltip("떨어질 자리 위로 이어지는 낙하 경로 기둥(경고의 자식, 나타날 때 위로 자라난다)")]
    [SerializeField] private Transform warningColumn;
    [SerializeField] private SpriteRenderer[] warningRenderers;
    [SerializeField, Min(1f)] private float warningSearchDistance = 60f;
    [Tooltip("생성 직후 경고 크기(착지 순간 1)")]
    [SerializeField, Range(0.1f, 1f)] private float warningMinScale = 0.85f;
    [Tooltip("생성 직후 경고 밝기(착지 순간 1)")]
    [SerializeField, Range(0f, 1f)] private float warningMinIntensity = 0.6f;
    [Tooltip("나타날 때 이 배율로 크게 시작해 warningAppearDuration 동안 줄어든다")]
    [SerializeField, Min(1f)] private float warningAppearScale = 1.3f;
    [SerializeField, Min(0.01f)] private float warningAppearDuration = 0.12f;
    [Tooltip("낙하 경로 기둥이 위로 자라나는 시간(초)")]
    [SerializeField, Min(0.01f)] private float warningColumnGrowDuration = 0.25f;
    [Tooltip("맥동 횟수(Hz): 생성 직후(x) → 착지 직전(y). 착지가 가까울수록 빨라진다")]
    [SerializeField] private Vector2 warningPulseRate = new(3f, 12f);
    [Tooltip("맥동으로 어두워지는 깊이(0이면 맥동 없음)")]
    [SerializeField, Range(0f, 1f)] private float warningPulseDepth = 0.45f;
    [Tooltip("착지 이 시간(초) 전부터 흰빛으로 번쩍인다")]
    [SerializeField, Min(0f)] private float warningFlashTime = 0.15f;
    [SerializeField] private Color warningFlashColor = Color.white;
    [Tooltip("번쩍일 때 흰빛으로 바뀌는 정도")]
    [SerializeField, Range(0f, 1f)] private float warningFlashAmount = 0.7f;

    [Header("Impact")]
    [SerializeField] private FeedbackDataSO impactFeedback;

    public bool HasLandingPoint => hasLanding;
    public Vector2 LandingPoint => landingPoint;
    public Vector2 SpawnPosition { get; private set; }
    public bool Shattered => shattered;
    public bool HitPlayer { get; private set; }
    public float Age => age;
    public Vector2 CurrentVelocity => velocity;
    public float HalfHeight => col != null ? col.bounds.extents.y : 0.75f;
    // 검증용: 착지까지 남은 시간(지금 높이·속도 기준)과 생성 때 계산한 전체 낙하 시간, 현재 경고 밝기 배율·맥동 횟수·번쩍임
    public float RemainingFallTime { get; private set; }
    public float TotalFallTime { get; private set; }
    public float WarningIntensity { get; private set; }
    public float WarningPulseHz { get; private set; }
    public float WarningFlash { get; private set; }

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private MaterialPropertyBlock block;
    private float[] warningBase;
    private Color[] warningColor;
    private Vector3 visualScale = Vector3.one;
    private Vector3 columnScale = Vector3.one;
    private Quaternion visualRotation = Quaternion.identity;
    private float activeGravity, activeMaxFall, spin, age, warningPhase;
    private bool initialized, shattered, hasLanding;
    private Vector2 landingPoint;

    protected override void Awake()
    {
        base.Awake();
        if (visualRoot == null)
            visualRoot = transform.Find("Visual");
        if (visualRoot != null)
        {
            visualScale = visualRoot.localScale;
            visualRotation = visualRoot.localRotation;
        }

        if (warningColumn != null)
            columnScale = warningColumn.localScale;

        block = new MaterialPropertyBlock();
        int count = warningRenderers != null ? warningRenderers.Length : 0;
        warningBase = new float[count];
        warningColor = new Color[count];
        for (int i = 0; i < count; i++)
        {
            var material = warningRenderers[i] != null ? warningRenderers[i].sharedMaterial : null;
            warningBase[i] = material != null && material.HasProperty(IntensityId) ? material.GetFloat(IntensityId) : 1f;
            warningColor[i] = material != null && material.HasProperty(EmissionColorId) ? material.GetColor(EmissionColorId) : Color.white;
        }

        activeGravity = gravity;
        activeMaxFall = maxFallSpeed;
        SetWarningVisible(false);
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        initialized = shattered = hasLanding = false;
        HitPlayer = false;
        age = 0f;
        velocity = Vector2.zero;
        activeGravity = gravity;
        activeMaxFall = maxFallSpeed;
        SetWarningVisible(false);
        if (visualRoot != null)
        {
            visualRoot.localScale = Vector3.zero;
            visualRoot.localRotation = visualRotation;
        }
    }

    // 풀에서 꺼낸 뒤 Initialize 전에 부른다. 중력은 아래 방향 가속도의 크기다.
    public void ConfigureFall(float fallGravity, float fallMaxSpeed)
    {
        activeGravity = Mathf.Max(0.1f, fallGravity);
        activeMaxFall = Mathf.Max(0.1f, fallMaxSpeed);
    }

    public override void Initialize(CharacterController2D owner, WeaponBaseSO.DamageInfo damageInfo,
        float speed, int count, LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);
        velocity = Vector2.zero;
        age = 0f;
        shattered = false;
        HitPlayer = false;
        spin = Random.Range(-spinSpeedRange, spinSpeedRange);
        warningPhase = 0f;
        SpawnPosition = transform.position;

        // 떨어질 자리: 큐브 중심에서 아래로 모터와 같은 지형 Layer를 찾는다(Ability가 아닌 모터 질의).
        var hit = motor.RaycastEnvironment(transform.position, Vector2.down, warningSearchDistance);
        hasLanding = hit.collider != null && hit.distance > 0.001f;
        landingPoint = hasLanding ? hit.point : (Vector2)transform.position + Vector2.down * warningSearchDistance;
        TotalFallTime = FallTime(FallHeight(), 0f);
        RemainingFallTime = TotalFallTime;
        initialized = true;
        SetWarningVisible(hasLanding);
        UpdatePresentation(0f);
    }

    protected override void FixedUpdate()
    {
        if (!initialized || isReturning)
            return;

        age += Time.fixedDeltaTime;
        if (age >= lifeTime)
        {
            ReturnToPool();
            return;
        }

        velocity.x = 0f;
        velocity.y = Mathf.Max(velocity.y - activeGravity * Time.fixedDeltaTime, -activeMaxFall);
        base.FixedUpdate();
    }

    private void LateUpdate()
    {
        if (initialized && !isReturning)
            UpdatePresentation(Time.deltaTime);
    }

    protected override void HandleCollision(RaycastCollisionInfo2D info)
    {
        if (!initialized || isReturning || shattered)
            return;

        // 바닥에 닿기 전에 지나온 경로의 플레이어 판정을 먼저 확정한다.
        if (info.down && velocity.y <= 0f)
        {
            CheckMovementHits();
            if (!isReturning && !shattered)
                Shatter(new Vector2(transform.position.x, transform.position.y - HalfHeight));
        }
        // 벽·천장에는 반응하지 않는다(수직으로만 떨어진다).
    }

    protected override void HitTarget(HealthBase health, Vector2 hitPoint)
    {
        if (!initialized || shattered || isReturning)
            return;

        HitPlayer = true;
        ApplyHitDamage(health, hitPoint);
        Shatter(transform.position);
    }

    protected override void ReturnToPool()
    {
        if (isReturning)
            return;

        initialized = false;
        velocity = Vector2.zero;
        SetWarningVisible(false);
        base.ReturnToPool();
    }

    private void OnDisable()
    {
        initialized = false;
        SetWarningVisible(false);
    }

    private void Shatter(Vector2 point)
    {
        if (shattered)
            return;

        shattered = true;
        if (impactFeedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.PlayExact(impactFeedback, point, Quaternion.identity);
        ReturnToPool();
    }

    private void UpdatePresentation(float dt)
    {
        if (visualRoot != null)
        {
            float appear = Mathf.Clamp01(age / appearDuration);
            visualRoot.localScale = visualScale * EaseOutBack(appear);
            visualRoot.localRotation = visualRotation * Quaternion.Euler(0f, 0f, spin * age);
        }

        if (!hasLanding || warning == null)
            return;

        // 경고는 큐브 자식이지만 바닥 자리에 고정한다(큐브 배율 1, 회전 없음).
        warning.SetPositionAndRotation(new Vector3(landingPoint.x, landingPoint.y, warning.position.z), Quaternion.identity);

        // 진행도: 생성 때 계산한 전체 낙하 시간 중 지나간 비율(착지 순간 1)
        RemainingFallTime = FallTime(FallHeight(), -velocity.y);
        float progress = TotalFallTime > 0.0001f ? 1f - Mathf.Clamp01(RemainingFallTime / TotalFallTime) : 1f;

        // 크기: 나타날 때 크게 시작해 줄어든 뒤, 착지가 가까울수록 제 크기(1)로 커진다.
        float pop = Mathf.Lerp(warningAppearScale, 1f, EaseOut(Mathf.Clamp01(age / warningAppearDuration)));
        warning.localScale = Vector3.one * (Mathf.Lerp(warningMinScale, 1f, progress) * pop);

        // 낙하 경로 기둥: 바닥에서 위로 자라난다(가로는 경고 크기를 따른다).
        if (warningColumn != null)
        {
            float grow = EaseOut(Mathf.Clamp01(age / warningColumnGrowDuration));
            warningColumn.localScale = new Vector3(columnScale.x, columnScale.y * Mathf.Lerp(0.2f, 1f, grow), columnScale.z);
        }

        // 밝기: 처음부터 잘 보이는 밝기로 시작해 착지가 가까울수록 밝아지고 맥동이 빨라진다. 착지 직전에는 흰빛으로 번쩍인다.
        WarningPulseHz = Mathf.Lerp(warningPulseRate.x, warningPulseRate.y, progress * progress);
        warningPhase = Mathf.Repeat(warningPhase + WarningPulseHz * dt, 1f);
        float pulse = 1f - warningPulseDepth * (0.5f - 0.5f * Mathf.Cos(warningPhase * 2f * Mathf.PI));
        WarningFlash = warningFlashTime > 0f ? Mathf.Clamp01(1f - RemainingFallTime / warningFlashTime) : 0f;
        WarningIntensity = Mathf.Lerp(warningMinIntensity, 1f, progress) * Mathf.Lerp(pulse, 1f, WarningFlash);
        for (int i = 0; i < warningBase.Length; i++)
        {
            var renderer = warningRenderers[i];
            if (renderer == null)
                continue;
            renderer.GetPropertyBlock(block);
            block.SetFloat(IntensityId, warningBase[i] * WarningIntensity);
            block.SetColor(EmissionColorId, Color.Lerp(warningColor[i], warningFlashColor, WarningFlash * warningFlashAmount));
            renderer.SetPropertyBlock(block);
        }
    }

    // 큐브 아랫면에서 떨어질 자리(바닥)까지의 높이
    private float FallHeight() => Mathf.Max(0f, transform.position.y - HalfHeight - landingPoint.y);

    // 높이 h를 지금 낙하 속도 v(아래가 양수)에서 중력·최대 낙하 속도로 떨어지는 데 걸리는 시간
    private float FallTime(float h, float v)
    {
        if (h <= 0f)
            return 0f;

        float g = Mathf.Max(0.1f, activeGravity);
        float vMax = Mathf.Max(0.1f, activeMaxFall);
        v = Mathf.Clamp(v, 0f, vMax);
        float accelTime = (vMax - v) / g;
        float accelDistance = v * accelTime + 0.5f * g * accelTime * accelTime;
        if (h <= accelDistance)
            return (Mathf.Sqrt(v * v + 2f * g * h) - v) / g;
        return accelTime + (h - accelDistance) / vMax;
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    private void SetWarningVisible(bool visible)
    {
        if (warningRenderers == null)
            return;
        foreach (var renderer in warningRenderers)
            if (renderer != null) renderer.enabled = visible;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
