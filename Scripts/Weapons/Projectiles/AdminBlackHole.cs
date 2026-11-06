using UnityEngine;

// 블랙홀 끌림 설정. 거리에 따라 목표 끌림 속도(플레이어 실제 이동 속도 × 비율)를 정하고, 플레이어 외부 힘을 그 속도까지만 채운다.
// 비율이 1보다 조금 크면 반대 방향키를 누르고 있어도 조금씩 빨려 들어가고, 외부 힘을 무시하는 대시로만 벗어날 수 있다.
[System.Serializable]
public struct BlackHolePullSettings
{
    [Tooltip("끌림이 닿는 최대 거리(칸)")]
    public float radius;
    [Tooltip("이 거리 안에서는 가장 세게 끈다(칸)")]
    public float coreRadius;
    [Tooltip("가장자리 끌림 = 플레이어 이동 속도 × 이 비율. 1보다 크면 걸어서는 벗어나지 못한다")]
    public float edgeSpeedRatio;
    [Tooltip("중심 근처 끌림 = 플레이어 이동 속도 × 이 비율")]
    public float coreSpeedRatio;
    [Tooltip("플레이어 이동 속도를 찾지 못했을 때 쓰는 기준 속도(칸/초)")]
    public float fallbackMoveSpeed;
    [Tooltip("외부 힘을 목표 속도까지 채우는 가속도(칸/초²). 플레이어 외부 힘 감쇠(30)보다 커야 끌림이 쌓인다")]
    public float acceleration;
    [Tooltip("땅 위에서는 들어 올리지 않는다(공중에서만 블랙홀 쪽 위로도 끈다). 수평 끌림은 땅 위·공중 모두 같다")]
    public bool groundedHorizontalOnly;
    [Tooltip("수평 끌림이 줄어드는 중심 폭(칸). 블랙홀 바로 아래에서 좌우로 떨리지 않게 한다")]
    public float groundCenterDeadZone;

    public static BlackHolePullSettings Default => new()
    {
        radius = 18f,
        coreRadius = 3.5f,
        edgeSpeedRatio = 1.06f,
        coreSpeedRatio = 1.2f,
        fallbackMoveSpeed = 6f,
        acceleration = 60f,
        groundedHorizontalOnly = true,
        groundCenterDeadZone = 1.5f,
    };

    // 플레이어 중심에서 블랙홀 중심까지의 벡터와 플레이어 이동 속도로 목표 끌림 속도를 구한다.
    public Vector2 Evaluate(Vector2 toCenter, bool grounded, float strength, float moveSpeed)
    {
        float distance = toCenter.magnitude;
        if (distance <= 0.0001f || distance > radius || strength <= 0f)
            return Vector2.zero;

        float baseSpeed = moveSpeed > 0f ? moveSpeed : fallbackMoveSpeed;
        float t = Mathf.InverseLerp(radius, Mathf.Min(coreRadius, radius - 0.01f), distance);
        float speed = baseSpeed * Mathf.Lerp(edgeSpeedRatio, coreSpeedRatio, t * t * (3f - 2f * t)) * strength;
        // 수평: 땅 위·공중 모두 같은 세기라 걷기·점프로는 버티지 못한다. 바로 아래 좁은 폭에서만 줄여 좌우로 떨리지 않게 한다.
        float side = Mathf.Clamp01(Mathf.Abs(toCenter.x) / Mathf.Max(0.01f, groundCenterDeadZone));
        float x = Mathf.Sign(toCenter.x) * speed * side;
        // 수직: 블랙홀 쪽 반지름 방향 성분. 땅 위에서는 들어 올리지 않는다(아래로 누르는 성분만 남긴다).
        float y = speed * toCenter.y / distance;
        if (groundedHorizontalOnly && grounded)
            y = Mathf.Min(0f, y);
        return new Vector2(x, y);
    }
}

// 블랙홀 바로 아래(바닥 ~ 블랙홀 중심) 기둥 모양 피해 장판. 끌려 들어가는 것 자체를 경계하게 한다.
[System.Serializable]
public struct BlackHoleFieldSettings
{
    public bool enabled;
    [Tooltip("장판 폭(칸). 플레이어 몸(1.5)이 조금이라도 닿으면 맞는다")]
    public float width;
    public WeaponBaseSO.DamageInfo damageInfo;
    [Tooltip("피해를 시도하는 간격(초). 실제 연속 피해는 플레이어 피격 경직·무적(0.3 + 0.5초)이 막는다")]
    public float tickInterval;
    [Tooltip("블랙홀이 열린 뒤 피해가 시작될 때까지(초)")]
    public float armDelay;
    [Tooltip("피해를 받는 대상 Layer(Player)")]
    public LayerMask targetLayer;
    [Tooltip("블랙홀 아래 바닥을 찾는 최대 거리(칸)")]
    public float floorSearchDistance;

    public static BlackHoleFieldSettings Default => new()
    {
        enabled = true,
        width = 4f,
        damageInfo = new WeaponBaseSO.DamageInfo { damage = 10f, damageType = DamageType.Normal },
        tickInterval = 0.25f,
        armDelay = 0.15f,
        targetLayer = 1 << 9,
        floorSearchDistance = 60f,
    };
}

// Admin 블랙홀(월드 풀 개체). BlackHoleAbility가 Admin 몸통 중심에 꺼내 단계별로 조작한다.
// - 맺힘(준비): 공간 왜곡만 서서히 커지고 안쪽으로 모이는 입자가 흐른다. 아래 피해 장판 자리는 흐린 경고로만 보인다. 끌림은 없다.
// - 열림(실행): 사건의 지평선·광자 고리·강착원반이 자라고 플레이어를 끌어당긴다. 아래 장판이 켜져 닿으면 피해를 준다.
// - 붕괴(회수·중단): 끌림과 장판이 먼저 꺼지고 원반이 안으로 말려 든 뒤 붕괴 효과를 내고 풀로 돌아간다.
// 장판 판정은 소유자(Admin) 모터의 OverlapAttackBox를 쓰고(Ability·개체에서 Physics2D 직접 질의 없음), 바닥은 같은 모터의 RaycastEnvironment로 찾는다.
// 장판 외형(몸통·흐름·바닥 경계·위험 입자·전기 줄기)은 ScreenDistortionRenderer.ProtectedLayer에 두어 화면 왜곡 뒤에 그려진다.
// 흡입음은 맺힘과 함께 SoundManager 반복음으로 켜 단계에 따라 키우고 줄이며, 붕괴가 끝나면 멈춘다.
// 끌림은 기존 이동 경로를 그대로 쓴다: PlayerController.externalForce를 목표 속도까지만 채우면
// CharacterController2D.FixedUpdate가 Velocity와 합쳐 RaycastMotor2D.Move로 옮기고(벽·바닥 판정은 모터) 감쇠시킨다.
// 대시처럼 IgnoreExternalForces가 켜진 동안은 모터가 외부 힘을 쓰지 않으므로 끌림에서 벗어난다.
[DefaultExecutionOrder(-50)]
public sealed class AdminBlackHole : WorldEntityBase
{
    public enum Phase { None, Forming, Active, Collapsing, Released }

    [Header("Parts")]
    [SerializeField] private Transform horizon;
    [SerializeField] private SpriteRenderer horizonRenderer;
    [SerializeField] private Transform photonRing;
    [SerializeField] private SpriteRenderer photonRenderer;
    [SerializeField] private Transform accretionInner;
    [SerializeField] private SpriteRenderer accretionInnerRenderer;
    [SerializeField] private Transform accretionOuter;
    [SerializeField] private SpriteRenderer accretionOuterRenderer;
    [SerializeField] private ParticleSystem formingParticles;
    [SerializeField] private ParticleSystem[] activeParticles;
    [SerializeField] private ScreenDistortionSource distortion;

    [Header("Damage Field Parts")]
    [Tooltip("장판 기준점(아래 가운데). 실행 중 블랙홀 아래 바닥으로 옮긴다")]
    [SerializeField] private Transform fieldRoot;
    [Tooltip("기둥 몸통(GlowMultiply, 아래 가운데 피벗). 높이에 맞춰 세로 배율을 바꾼다")]
    [SerializeField] private SpriteRenderer fieldBodyRenderer;
    [Tooltip("위로 빨려 드는 흐름(GlowAdd, _ScrollOffset)")]
    [SerializeField] private SpriteRenderer fieldFlowRenderer;
    [Tooltip("바닥 경계 표시(GlowMultiply)")]
    [SerializeField] private SpriteRenderer fieldFloorRenderer;
    [SerializeField] private ParticleSystem fieldParticles;
    [Tooltip("기둥 Sprite의 기본 높이(칸)")]
    [SerializeField, Min(0.1f)] private float fieldSpriteHeight = 8f;
    [Tooltip("기둥·바닥 표시 Sprite의 기본 폭(칸)")]
    [SerializeField, Min(0.1f)] private float fieldSpriteWidth = 4f;
    [Tooltip("흐름 마스크가 위로 흐르는 속도(UV/초)")]
    [SerializeField] private float fieldFlowSpeed = 1.4f;
    [Tooltip("맺힘 동안 장판 경고 밝기(비율)")]
    [SerializeField, Range(0f, 1f)] private float fieldWarningIntensity = 0.35f;
    [Tooltip("장판 외형이 블랙홀 중심보다 이만큼(칸) 아래에서 끝난다. 왜곡 뒤에 그려지므로 사건의 지평선(반지름 1.62)을 덮지 않게 한다(피해 범위는 중심까지)")]
    [SerializeField, Min(0f)] private float fieldVisualTopInset = 1.75f;

    [Header("Damage Field Danger")]
    [Tooltip("양쪽 경계선을 따라 튀는 불꽃")]
    [SerializeField] private ParticleSystem fieldEdgeSparksLeft;
    [SerializeField] private ParticleSystem fieldEdgeSparksRight;
    [Tooltip("바닥 경계에서 튀어 오르는 불씨")]
    [SerializeField] private ParticleSystem fieldFloorEmbers;
    [Tooltip("기둥 안에서 짧게 튀는 전기")]
    [SerializeField] private ParticleSystem fieldCrackle;
    [Tooltip("맺힘 동안 위험 입자를 이 비율로만 낸다(경고)")]
    [SerializeField, Range(0f, 1f)] private float fieldDangerWarningRate = 0.2f;
    [Tooltip("양쪽 경계를 따라 흐르는 전기 줄기(실행 중에만)")]
    [SerializeField] private LineRenderer fieldArcLeft;
    [SerializeField] private LineRenderer fieldArcRight;
    [Tooltip("전기 줄기 모양을 다시 정하는 간격(초)")]
    [SerializeField, Min(0.01f)] private float fieldArcInterval = 0.045f;
    [Tooltip("전기 줄기가 경계선에서 흔들리는 폭(칸)")]
    [SerializeField, Min(0f)] private float fieldArcJitter = 0.14f;

    [Header("Sound")]
    [Tooltip("블랙홀이 빨아들이는 반복음. 맺힘 동안 커지고 붕괴 동안 줄어든다")]
    [SerializeField] private SoundOptions suctionSound;
    [Tooltip("맺힘 동안 흡입음 크기(비율)")]
    [SerializeField, Range(0f, 1f)] private float formingSuctionVolume = 0.35f;

    [Header("Look")]
    [Tooltip("안쪽 원반 회전(도/초, 양수는 반시계)")]
    [SerializeField] private float innerSpin = 210f;
    [SerializeField] private float outerSpin = 95f;
    [Tooltip("광자 고리 밝기 흔들림(비율)")]
    [SerializeField, Range(0f, 0.6f)] private float photonFlicker = 0.18f;
    [SerializeField, Range(0f, 1f)] private float formingDistortion = 0.32f;
    [SerializeField, Range(0f, 1f)] private float formingRingScale = 0.35f;

    [Header("Timing")]
    [SerializeField, Min(0.01f)] private float igniteDuration = 0.35f;
    [SerializeField, Min(0.01f)] private float pullRampDuration = 0.6f;
    [SerializeField, Min(0.01f)] private float collapseDuration = 0.4f;
    [Tooltip("붕괴 뒤 남은 입자가 사라질 때까지 기다렸다가 풀로 돌아간다(초)")]
    [SerializeField, Min(0f)] private float releaseDelay = 0.9f;

    [Header("Feedback")]
    [SerializeField] private FeedbackDataSO igniteFeedback;
    [SerializeField] private FeedbackDataSO collapseFeedback;

    public Phase CurrentPhase { get; private set; }
    public float PullStrength { get; private set; }
    public float DistortionStrength => distortion != null ? distortion.strength : 0f;
    public float OpenAmount => open;
    // 검증용: 마지막 물리 단계에서 정한 목표 끌림 속도와 플레이어 외부 힘, 끌림 기준 이동 속도
    public Vector2 LastPull { get; private set; }
    public Vector2 LastExternalForce { get; private set; }
    public float LastMoveSpeed { get; private set; }
    public BlackHolePullSettings PullSettings => pull;
    // 검증용: 피해 장판 상태와 범위(월드), 실제로 피해를 준 횟수
    public bool FieldArmed => fieldReady && field.enabled && CurrentPhase == Phase.Active && phaseTime >= field.armDelay;
    public Rect FieldRect => fieldRect;
    public float FieldVisualHeight { get; private set; }
    public int FieldHitCount { get; private set; }

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int ScrollOffsetId = Shader.PropertyToID("_ScrollOffset");

    private BlackHolePullSettings pull;
    private BlackHoleFieldSettings field;
    private MaterialPropertyBlock block;
    private Vector3 horizonScale, photonScale, innerScale, outerScale;
    private float photonBase, innerBase, outerBase;
    private float fieldBodyBase, fieldFlowBase, fieldFloorBase, fieldScroll, nextFieldHit, fieldAmount, flowAmount;
    private float holeLift;
    private float[] dangerBaseRates;
    private ParticleSystem[] dangerParticles;
    private float arcTimer, arcAmount, arcBase, arcHalf, arcHeight, suctionLevel, collapseSuction;
    private SoundHandle suctionHandle;
    private bool fieldReady;
    private Rect fieldRect;
    private MoveAbility playerMove;
    private PlayerController cachedPlayer;
    private readonly Collider2D[] fieldHits = new Collider2D[8];
    private float phaseTime, formDuration, releaseTimer;
    private float open, form, ring, disk, glow;
    private float collapseOpen, collapseRing, collapseDisk, collapseDistortion, collapsePull, collapseField, collapseFlow;
    private float spinInner, spinOuter, flickerSeed;

    protected override void Awake()
    {
        base.Awake();
        block = new MaterialPropertyBlock();
        horizonScale = horizon != null ? horizon.localScale : Vector3.one;
        photonScale = photonRing != null ? photonRing.localScale : Vector3.one;
        innerScale = accretionInner != null ? accretionInner.localScale : Vector3.one;
        outerScale = accretionOuter != null ? accretionOuter.localScale : Vector3.one;
        photonBase = BaseIntensity(photonRenderer);
        innerBase = BaseIntensity(accretionInnerRenderer);
        outerBase = BaseIntensity(accretionOuterRenderer);
        fieldBodyBase = BaseIntensity(fieldBodyRenderer);
        fieldFlowBase = BaseIntensity(fieldFlowRenderer);
        fieldFloorBase = BaseIntensity(fieldFloorRenderer);
        arcBase = BaseIntensity(fieldArcLeft);
        dangerParticles = new[] { fieldEdgeSparksLeft, fieldEdgeSparksRight, fieldFloorEmbers, fieldCrackle };
        dangerBaseRates = new float[dangerParticles.Length];
        for (int i = 0; i < dangerParticles.Length; i++)
            dangerBaseRates[i] = dangerParticles[i] != null ? dangerParticles[i].emission.rateOverTimeMultiplier : 0f;
        ResetState();
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        ResetState();
    }

    // 준비 단계: 공간 왜곡만 formDuration 동안 커지고, 아래 장판 자리가 흐린 경고로 나타난다.
    public void BeginForming(CharacterController2D owner, BlackHolePullSettings settings, BlackHoleFieldSettings fieldSettings, float duration)
    {
        Initialize(owner, default);
        pull = settings;
        field = fieldSettings;
        formDuration = Mathf.Max(0.05f, duration);
        CurrentPhase = Phase.Forming;
        phaseTime = 0f;
        FieldHitCount = 0;
        nextFieldHit = 0f;
        PlaceField();
        if (formingParticles != null)
            formingParticles.Play(true);
        // 장판 자리에는 맺힘 동안 위험 입자를 약하게 내어 경고한다.
        SetDangerRate(fieldDangerWarningRate);
        if (fieldReady)
            foreach (var ps in dangerParticles)
                if (ps != null) ps.Play(true);
        StartSuction();
    }

    // 블랙홀 바로 아래 바닥(소유자 모터의 지형 Layer)을 찾아 장판 기둥을 바닥 ~ 블랙홀 중심에 맞춘다.
    private void PlaceField()
    {
        fieldReady = false;
        if (!field.enabled || owner == null || owner.Motor == null || fieldRoot == null)
            return;

        Vector2 center = transform.position;
        var floor = owner.Motor.RaycastEnvironment(center, Vector2.down, Mathf.Max(1f, field.floorSearchDistance));
        if (floor.collider == null || floor.distance <= 0.05f)
            return;

        float height = center.y - floor.point.y;
        float width = Mathf.Max(0.1f, field.width);
        fieldRect = new Rect(center.x - width * 0.5f, floor.point.y, width, height);
        fieldRoot.position = new Vector3(center.x, floor.point.y, fieldRoot.position.z);
        // 외형은 사건의 지평선 아래에서 끝낸다(피해 판정 fieldRect는 블랙홀 중심까지).
        float visualHeight = Mathf.Max(1f, height - fieldVisualTopInset);
        FieldVisualHeight = visualHeight;
        float heightScale = visualHeight / Mathf.Max(0.1f, fieldSpriteHeight);
        float widthScale = width / Mathf.Max(0.1f, fieldSpriteWidth);
        if (fieldBodyRenderer != null) fieldBodyRenderer.transform.localScale = new Vector3(widthScale, heightScale, 1f);
        if (fieldFlowRenderer != null) fieldFlowRenderer.transform.localScale = new Vector3(widthScale, heightScale, 1f);
        if (fieldFloorRenderer != null) fieldFloorRenderer.transform.localScale = new Vector3(widthScale, 1f, 1f);
        if (fieldParticles != null)
        {
            var shape = fieldParticles.shape;
            shape.scale = new Vector3(width * 0.92f, 0.1f, 0f);
        }

        // 위험 표시: 경계선 불꽃·바닥 불씨·기둥 안 전기를 실제 장판 크기에 맞춘다(기준점은 바닥 가운데).
        float half = width * 0.5f;
        SetBox(fieldEdgeSparksLeft, new Vector3(-half, visualHeight * 0.5f, 0f), new Vector3(0.06f, visualHeight, 0f));
        SetBox(fieldEdgeSparksRight, new Vector3(half, visualHeight * 0.5f, 0f), new Vector3(0.06f, visualHeight, 0f));
        SetBox(fieldFloorEmbers, new Vector3(0f, 0.06f, 0f), new Vector3(width * 0.95f, 0.06f, 0f));
        SetBox(fieldCrackle, new Vector3(0f, visualHeight * 0.5f, 0f), new Vector3(width * 0.9f, visualHeight * 0.95f, 0f));
        arcHalf = half;
        arcHeight = visualHeight;
        fieldReady = true;
    }

    private static void SetBox(ParticleSystem ps, Vector3 position, Vector3 scale)
    {
        if (ps == null)
            return;
        var shape = ps.shape;
        shape.position = position;
        shape.scale = scale;
    }

    private void SetDangerRate(float ratio)
    {
        if (dangerParticles == null)
            return;
        for (int i = 0; i < dangerParticles.Length; i++)
        {
            if (dangerParticles[i] == null)
                continue;
            var emission = dangerParticles[i].emission;
            emission.rateOverTimeMultiplier = dangerBaseRates[i] * ratio;
        }
    }

    // 전기 줄기: 바닥 ~ 기둥 위까지 경계선을 따라 흔들리는 꺾인 선을 간격마다 다시 그린다.
    private void UpdateArcs(float dt)
    {
        arcTimer -= dt;
        bool redraw = arcTimer <= 0f;
        if (redraw)
            arcTimer = fieldArcInterval;

        DrawArc(fieldArcLeft, -arcHalf, redraw);
        DrawArc(fieldArcRight, arcHalf, redraw);
    }

    private void DrawArc(LineRenderer arc, float x, bool redraw)
    {
        if (arc == null)
            return;

        bool visible = fieldReady && arcAmount > 0.001f;
        arc.enabled = visible;
        if (!visible)
            return;

        if (redraw)
        {
            int count = arc.positionCount;
            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? (float)i / (count - 1) : 0f;
                float jitter = (i == 0 || i == count - 1) ? 0f : Random.Range(-fieldArcJitter, fieldArcJitter);
                arc.SetPosition(i, new Vector3(x + jitter, t * arcHeight * 0.96f, 0f));
            }
        }

        // 가끔 한 번씩 꺼졌다 켜지며 지직거린다.
        float flicker = Random.value < 0.12f ? 0.25f : Random.Range(0.75f, 1.15f);
        arc.GetPropertyBlock(block);
        block.SetFloat(IntensityId, arcBase * arcAmount * flicker);
        arc.SetPropertyBlock(block);
    }

    private void StartSuction()
    {
        StopSuction();
        suctionLevel = 0f;
        if (suctionSound == null || suctionSound.clip == null || SoundManager.Instance == null)
            return;

        suctionHandle = SoundManager.Instance.PlayLoop(suctionSound, transform.position, transform);
        if (suctionHandle != null && suctionHandle.IsValid)
            suctionHandle.GetSource().volume = 0f;
    }

    private void UpdateSuction()
    {
        if (suctionHandle == null || !suctionHandle.IsValid)
            return;
        // SoundManager가 재생할 때마다 다시 정하는 volume만 바꾼다(공용 풀 AudioSource).
        suctionHandle.GetSource().volume = suctionSound.volume * Mathf.Clamp01(suctionLevel);
    }

    private void StopSuction()
    {
        suctionHandle?.Stop();
        suctionHandle = null;
    }

    // 검증용: 흡입음 재생 상태와 현재 음량
    public bool SuctionPlaying => suctionHandle != null && suctionHandle.IsValid;
    public float SuctionVolume => SuctionPlaying ? suctionHandle.GetSource().volume : 0f;

    // 실행 단계: 블랙홀이 열리고 끌림이 시작된다.
    public void Ignite()
    {
        if (CurrentPhase == Phase.Active || CurrentPhase == Phase.Collapsing || CurrentPhase == Phase.Released)
            return;

        if (CurrentPhase == Phase.None)
            form = 1f;

        CurrentPhase = Phase.Active;
        phaseTime = 0f;
        if (formingParticles != null)
            formingParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        foreach (var ps in activeParticles)
            if (ps != null) ps.Play(true);
        if (fieldReady && field.enabled && fieldParticles != null)
            fieldParticles.Play(true);
        SetDangerRate(1f);
        if (igniteFeedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.PlayExact(igniteFeedback, transform.position, Quaternion.identity);
    }

    // 회수·중단: 끌림을 끄고 안으로 말려 든 뒤 풀로 돌아간다. 여러 번 불러도 한 번만 진행한다.
    public void Collapse()
    {
        if (CurrentPhase == Phase.Collapsing || CurrentPhase == Phase.Released)
            return;

        if (CurrentPhase == Phase.None)
        {
            ReturnToPool();
            return;
        }

        collapseOpen = open;
        collapseRing = ring;
        collapseDisk = disk;
        collapseDistortion = DistortionStrength;
        collapsePull = PullStrength;
        collapseField = fieldAmount;
        collapseFlow = flowAmount;
        collapseSuction = suctionLevel;
        arcAmount = 0f;
        CurrentPhase = Phase.Collapsing;
        phaseTime = 0f;
        StopEmitting();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        phaseTime += dt;
        spinInner = Mathf.Repeat(spinInner + innerSpin * dt, 360f);
        spinOuter = Mathf.Repeat(spinOuter + outerSpin * dt, 360f);

        switch (CurrentPhase)
        {
            case Phase.Forming:
            {
                form = Smooth(phaseTime / formDuration);
                open = 0f;
                ring = formingRingScale * form;
                disk = 0f;
                glow = 0.6f * form;
                // 장판 자리는 흐린 경고로 맥동한다(아직 피해 없음).
                fieldAmount = fieldWarningIntensity * form * (0.8f + 0.2f * Mathf.Sin(phaseTime * 12f));
                flowAmount = 0f;
                arcAmount = 0f;
                suctionLevel = formingSuctionVolume * form;
                SetDistortion(formingDistortion * form);
                break;
            }
            case Phase.Active:
            {
                float grow = Smooth(phaseTime / igniteDuration);
                open = grow;
                ring = Mathf.Lerp(formingRingScale * form, 1f, grow);
                disk = grow;
                glow = Mathf.Lerp(0.6f * form, 1f, grow);
                fieldAmount = Mathf.Lerp(fieldWarningIntensity * form, 1f, grow);
                flowAmount = grow;
                // 전기 줄기는 장판 피해가 켜질 때(armDelay) 함께 켜진다.
                arcAmount = field.enabled && phaseTime >= field.armDelay ? 1f : 0f;
                suctionLevel = Mathf.Lerp(formingSuctionVolume * form, 1f, grow);
                PullStrength = Smooth(phaseTime / pullRampDuration);
                SetDistortion(Mathf.Lerp(formingDistortion * form, 1f, grow));
                break;
            }
            case Phase.Collapsing:
            {
                float k = Mathf.Clamp01(phaseTime / collapseDuration);
                float shrink = 1f - k * k;
                open = collapseOpen * shrink;
                ring = collapseRing * shrink;
                disk = collapseDisk * (1f - Smooth(k));
                // 사라지기 직전에 광자 고리가 한 번 더 밝아진다.
                glow = (1f + 1.6f * k) * shrink;
                fieldAmount = collapseField * (1f - Smooth(k));
                flowAmount = collapseFlow * (1f - Smooth(k));
                suctionLevel = collapseSuction * (1f - Smooth(k));
                PullStrength = collapsePull * (1f - Smooth(k * 2f));
                SetDistortion(collapseDistortion * (1f - k));
                if (k >= 1f)
                {
                    PullStrength = 0f;
                    open = ring = disk = glow = fieldAmount = flowAmount = suctionLevel = 0f;
                    StopSuction();
                    SetDistortion(0f);
                    CurrentPhase = Phase.Released;
                    releaseTimer = 0f;
                    if (collapseFeedback != null && FeedbackPlayer.Instance != null)
                        FeedbackPlayer.Instance.PlayExact(collapseFeedback, transform.position, Quaternion.identity);
                }
                break;
            }
            case Phase.Released:
                releaseTimer += dt;
                if (releaseTimer >= releaseDelay)
                {
                    ReturnToPool();
                    return;
                }
                break;
        }

        ApplyLook(CurrentPhase == Phase.Collapsing ? Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(phaseTime / collapseDuration)) : 1f);
        UpdateArcs(dt);
        UpdateSuction();
    }

    private void FixedUpdate()
    {
        LastPull = Vector2.zero;
        if (GameManager.Instance == null)
            return;

        var player = GameManager.Instance.Player;
        if (player == null || !player.isActiveAndEnabled)
            return;

        if (FieldArmed)
            ApplyField();

        if (PullStrength <= 0f)
            return;

        // 끌림 기준은 플레이어의 실제 이동 속도다(MoveAbility.MoveSpeed). 비율이 1을 조금 넘어 걸어서는 버티지 못한다.
        LastMoveSpeed = MoveSpeedOf(player);
        bool grounded = player.IsGrounded;

        // 블랙홀이 공중에서 더한 위쪽 힘은 땅에 닿으면 거둔다. 외부 힘 감쇠는 벡터 크기 기준이라 수평 끌림을 채우는 동안
        // 위쪽 성분이 거의 줄지 않아, 그대로 두면 착지할 때마다 다시 떠올라 바닥 위에 떠 있게 된다. 넉백 같은 다른 위쪽 힘은 남긴다.
        holeLift = Mathf.Min(holeLift, Mathf.Max(0f, player.externalForce.y));
        if (grounded && holeLift > 0f)
        {
            player.externalForce.y -= holeLift;
            holeLift = 0f;
        }

        Vector2 toCenter = (Vector2)transform.position - (Vector2)player.transform.position;
        Vector2 target = pull.Evaluate(toCenter, grounded, PullStrength, LastMoveSpeed);
        float speed = target.magnitude;
        if (speed <= 0.0001f)
            return;

        // 외부 힘의 끌림 방향 성분만 목표 속도까지 채운다. 넉백 같은 다른 외부 힘은 그대로 두고, 블랙홀이 꺼지면 기존 감쇠로 사라진다.
        Vector2 direction = target / speed;
        float along = Vector2.Dot(player.externalForce, direction);
        if (along < speed)
        {
            Vector2 add = direction * Mathf.Min(speed - along, Mathf.Max(0f, pull.acceleration) * Time.fixedDeltaTime);
            player.externalForce += add;
            if (add.y > 0f)
                holeLift += add.y;
        }
        LastPull = target;
        LastExternalForce = player.externalForce;
    }

    // 장판 기둥에 플레이어가 닿으면 피해를 준다. 판정은 소유자 모터의 OverlapAttackBox(트리거 포함, 대상 Layer만)를 쓴다.
    // 실제 연속 피해 간격은 PlayerHealth의 피격 경직·무적이 정한다(무적·반격 판정도 그쪽이 처리한다).
    private void ApplyField()
    {
        if (Time.time < nextFieldHit || owner == null || owner.Motor == null || field.damageInfo.damage <= 0f)
            return;

        nextFieldHit = Time.time + Mathf.Max(0.02f, field.tickInterval);
        RaycastMotor2D.PrepareAttackQueries();
        int count = owner.Motor.OverlapAttackBox(fieldRect.center, fieldRect.size, 0f, fieldHits, field.targetLayer);
        for (int i = 0; i < count; i++)
        {
            var health = fieldHits[i] != null ? fieldHits[i].GetComponentInParent<PlayerHealth>() : null;
            if (health == null || health.IsInvincible)
                continue;

            float before = health.currentHP;
            health.TakeDamage(field.damageInfo, new Vector3(fieldRect.center.x, health.transform.position.y, 0f));
            if (health.currentHP < before)
                FieldHitCount++;
            break;
        }
    }

    private float MoveSpeedOf(PlayerController player)
    {
        if (player != cachedPlayer)
        {
            cachedPlayer = player;
            playerMove = player.GetComponentInChildren<MoveAbility>();
        }
        return playerMove != null ? playerMove.MoveSpeed : 0f;
    }

    private void OnDisable()
    {
        PullStrength = 0f;
        LastPull = Vector2.zero;
        fieldReady = false;
        StopSuction();
        if (distortion != null)
            distortion.strength = 0f;
    }

    private void ResetState()
    {
        CurrentPhase = Phase.None;
        PullStrength = 0f;
        LastPull = Vector2.zero;
        LastExternalForce = Vector2.zero;
        phaseTime = releaseTimer = 0f;
        open = form = ring = disk = glow = fieldAmount = flowAmount = holeLift = 0f;
        fieldReady = false;
        FieldHitCount = 0;
        spinInner = Random.Range(0f, 360f);
        spinOuter = Random.Range(0f, 360f);
        flickerSeed = Random.Range(0f, 100f);
        SetDistortion(0f);
        if (formingParticles != null)
            formingParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var ps in activeParticles)
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (fieldParticles != null)
            fieldParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (dangerParticles != null)
            foreach (var ps in dangerParticles)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        arcAmount = suctionLevel = 0f;
        arcTimer = 0f;
        StopSuction();
        ApplyLook(1f);
        UpdateArcs(0f);
    }

    private void StopEmitting()
    {
        if (formingParticles != null)
            formingParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        foreach (var ps in activeParticles)
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (fieldParticles != null)
            fieldParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (dangerParticles != null)
            foreach (var ps in dangerParticles)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void SetDistortion(float value)
    {
        if (distortion != null)
            distortion.strength = Mathf.Clamp01(value);
    }

    private void ApplyLook(float diskScale)
    {
        SetScale(horizon, horizonRenderer, horizonScale * open, open);
        SetScale(photonRing, photonRenderer, photonScale * ring, ring);
        SetScale(accretionInner, accretionInnerRenderer, innerScale * diskScale, disk);
        SetScale(accretionOuter, accretionOuterRenderer, outerScale * diskScale, disk);
        if (accretionInner != null) accretionInner.localRotation = Quaternion.Euler(0f, 0f, spinInner);
        if (accretionOuter != null) accretionOuter.localRotation = Quaternion.Euler(0f, 0f, spinOuter);

        float flicker = 1f + photonFlicker * (Mathf.PerlinNoise(flickerSeed, Time.time * 9f) * 2f - 1f);
        SetIntensity(photonRenderer, photonBase * glow * flicker);
        SetIntensity(accretionInnerRenderer, innerBase * disk);
        SetIntensity(accretionOuterRenderer, outerBase * disk);

        // 피해 장판: 몸통·바닥 경계는 fieldAmount, 위로 빨려 드는 흐름은 flowAmount로 밝기를 정한다.
        bool showField = fieldReady && field.enabled;
        if (fieldBodyRenderer != null) fieldBodyRenderer.enabled = showField && fieldAmount > 0.001f;
        if (fieldFloorRenderer != null) fieldFloorRenderer.enabled = showField && fieldAmount > 0.001f;
        if (fieldFlowRenderer != null) fieldFlowRenderer.enabled = showField && flowAmount > 0.001f;
        SetIntensity(fieldBodyRenderer, fieldBodyBase * fieldAmount);
        SetIntensity(fieldFloorRenderer, fieldFloorBase * Mathf.Clamp01(fieldAmount * 1.3f));
        if (fieldFlowRenderer != null && fieldFlowRenderer.enabled)
        {
            fieldScroll = Mathf.Repeat(fieldScroll - fieldFlowSpeed * Time.deltaTime, 1f);
            fieldFlowRenderer.GetPropertyBlock(block);
            block.SetFloat(IntensityId, fieldFlowBase * flowAmount);
            block.SetVector(ScrollOffsetId, new Vector4(0f, fieldScroll, 0f, 0f));
            fieldFlowRenderer.SetPropertyBlock(block);
        }
    }

    private static void SetScale(Transform part, Renderer renderer, Vector3 scale, float amount)
    {
        if (part != null) part.localScale = scale;
        if (renderer != null) renderer.enabled = amount > 0.001f;
    }

    private void SetIntensity(SpriteRenderer renderer, float value)
    {
        if (renderer == null || !renderer.enabled)
            return;

        renderer.GetPropertyBlock(block);
        block.SetFloat(IntensityId, value);
        renderer.SetPropertyBlock(block);
    }

    private static float BaseIntensity(Renderer renderer)
    {
        var material = renderer != null ? renderer.sharedMaterial : null;
        return material != null && material.HasProperty(IntensityId) ? material.GetFloat(IntensityId) : 1f;
    }

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
