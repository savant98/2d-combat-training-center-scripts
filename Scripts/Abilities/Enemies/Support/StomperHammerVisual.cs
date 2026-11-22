using System.Collections.Generic;
using UnityEngine;

// Animator가 swingProgress를 기록하고, 해머는 손잡이 끝의 고정 월드 축으로 돈다.
// 두 포물선 대시에서 해머 머리의 내리치기만 별도 공격 판정을 가진다.
// Model의 몸통 회전·부모 반전·점프 이동이 별도로 생성한 해머에 전파되지 않는다.
[DefaultExecutionOrder(-1000)]
public class StomperHammerVisual : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private EnemyController owner;
    [SerializeField] private ParabolicDashAbility ability;
    [SerializeField] private BoxCollider2D bodyCollider;
    [SerializeField] private GameObject visualPrefab;
    [SerializeField] private Transform impactPoint;

    [Header("공격 외형")]
    [SerializeField] private Material dangerMaterial;
    [SerializeField] private Material swingArcMaterial;
    [SerializeField] private Sprite swingArcSprite;
    [SerializeField, Range(0f, 1f)] private float visualOpacity = 1f;
    public float VisualOpacity => visualOpacity;

    [Header("Animator 구동")]
    [Range(0f, 1f)] public float swingProgress;

    // 단위 외형의 손잡이 끝은 y=0, 머리 안쪽은 y=0.72, 바깥 타격면은 y=1-x²다.
    const float Length = 6.25f;
    const float HeadHalfWidth = 0.22f;
    const float HeadBottom = 0.72f;
    const float HeadTop = 1f;
    const float SweepBoxTop = .95f;
    // 원본 Windup의 0→1/3→2/3→1초 박자에 맞춰 낮은 준비 자세→들기→젖힘→타격으로 돈다.
    // swingProgress는 현재 회전 범위(20°~-180°)의 정규화 값이며 원본 관절 값은 복사하지 않는다.
    const float RestAngle = -135f;
    const float RaisedAngle = 20f;
    const float DownImpactAngle = -180f;
    const float BodySideClearance = 0.175f;
    const float ImpactFadeDuration = 0.5f;
    const float WindupProgressLimit = 0.999f;
    const float DownswingStart = 2f / 3f;
    const float MaximumSweepStep = 5f;
    const float TrailDuration = .2f;
    const float ArcQuadSize = 14f;

    static readonly int ArcHeadId = Shader.PropertyToID("_Head");
    static readonly int ArcTailId = Shader.PropertyToID("_Tail");
    static readonly int ArcAlphaId = Shader.PropertyToID("_Alpha");
    static readonly int ArcTipScaleId = Shader.PropertyToID("_TipScale");

    GameObject detachedVisual;
    PolygonCollider2D attackHead;
    LineRenderer dangerOutline;
    SpriteRenderer swingArc;
    MaterialPropertyBlock arcProperties;
    Vector2[] headPath;
    Vector3[] outlinePositions;
    LayerMask playerLayer;
    ContactFilter2D playerFilter;
    readonly List<Collider2D> headHits = new List<Collider2D>(4);
    readonly Collider2D[] sweepHits = new Collider2D[8];
    readonly HashSet<PlayerHealth> playersStruck = new HashSet<PlayerHealth>();
    readonly List<Vector2> trailSamples = new List<Vector2>(32);
    float windupStartedAt;
    float lastAttackAngle;
    float trailHead;
    StomperHammerAfterimage afterimage;
    bool cycleActive;
    int ownerFacing = 1;
    int strikeSide = -1;
    Vector3 fixedPivot;
    Vector3 contactPoint;
    float contactAngle;
    float floorY;

    public Vector3 PivotWorldPosition => detachedVisual != null ? detachedVisual.transform.position : fixedPivot;
    public Vector3 InitialPivotWorldPosition => fixedPivot;
    public Vector3 ImpactPointWorldPosition => contactPoint;
    // 손잡이 축과 실제 머리의 위치를 따로 읽어 회전 궤적과 타격점을 검증한다.
    public Vector3 HeadCenterWorldPosition => ShaftPointWorld((HeadBottom + HeadTop) * 0.5f);
    public Vector3 HeadTipWorldPosition => ShaftPointWorld(HeadTop);
    public Vector3 HeadFaceCenterWorldPosition => ShaftPointWorld(HeadTop);
    public float PivotHeightAboveGround => fixedPivot.y - floorY;
    public float SideClearance { get; private set; }
    public int FacingDirection => ownerFacing;
    public int StrikeSideDirection => strikeSide;
    public Transform DetachedVisual => detachedVisual != null ? detachedVisual.transform : null;
    public float WorldLength => Length;
    public float GroundGap { get; private set; }
    public float ImpactGroundGap { get; private set; }
    public float MinimumSurfaceGap { get; private set; }
    public float NearImpactMinimumSurfaceGap { get; private set; }
    public float MaxPivotDrift { get; private set; }
    public float SwingAngle { get; private set; }
    public float ImpactAngle => contactAngle;
    public float RestPoseAngle => RestAngle;
    public float TargetGroundY => floorY;
    public int ImpactFrame { get; private set; } = -1;
    public float ImpactTime { get; private set; } = -1f;
    public int CycleCount { get; private set; }
    public StomperHammerAfterimage Afterimage => afterimage;
    public bool IsHeadAttackActive => attackHead != null && attackHead.enabled && attackHead.gameObject.activeInHierarchy;
    public bool IsDangerOutlineVisible => dangerOutline != null && dangerOutline.enabled;
    public bool IsSwingTrailVisible => swingArc != null && swingArc.enabled;
    public bool IsPrimaryVisible => detachedVisual != null && detachedVisual.activeSelf;
    public bool IsVisible => IsPrimaryVisible || (afterimage != null && afterimage.IsVisible);

    public void Configure(EnemyController owner, ParabolicDashAbility ability, BoxCollider2D bodyCollider,
        GameObject visualPrefab, Transform impactPoint)
    {
        this.owner = owner;
        this.ability = ability;
        this.bodyCollider = bodyCollider;
        this.visualPrefab = visualPrefab;
        this.impactPoint = impactPoint;
    }

    void Awake()
    {
        playerLayer = LayerMask.GetMask("Player");
        playerFilter.SetLayerMask(playerLayer);
        playerFilter.useTriggers = true;
    }

    void OnEnable()
    {
        cycleActive = false;
        swingProgress = (RaisedAngle - RestAngle) / (RaisedAngle - DownImpactAngle);
        Hide();
    }

    // AiBrain.Update가 Execute Feedback을 재생하기 전에 예측 접점을 월드 위치에 고정한다.
    void Update()
    {
        // 중앙 목표도 같은 ParabolicDash 인스턴스의 해머 경로를 사용한다.
        if (owner != null && owner.Brain != null && owner.Brain.current is ParabolicDashAbility currentDash)
            ability = currentDash;
        if (!CanDisplay()) { EndCycle(); return; }
        if (ability.State == AbilityState.Windup && (!cycleActive || ImpactFrame >= 0)) BeginCycle();
        if (cycleActive && impactPoint != null) impactPoint.position = contactPoint;
    }

    void LateUpdate()
    {
        if (owner != null && owner.Brain != null && owner.Brain.current is ParabolicDashAbility currentDash)
            ability = currentDash;
        if (!CanDisplay()) { EndCycle(); return; }
        if (ability.State == AbilityState.Windup)
        {
            // 패턴이 이번 Update에서 처음 시작했을 때도 같은 프레임에 축을 잡는다.
            if (!cycleActive || ImpactFrame >= 0) BeginCycle();
            SetVisible(true);
            // 준비 Clip의 끝 키를 먼저 읽어도 실제 접촉은 Execute 첫 프레임에만 일어난다.
            ApplyPose(Mathf.Lerp(RaisedAngle, contactAngle, Mathf.Min(Mathf.Clamp01(swingProgress), WindupProgressLimit)));
            // 낮게 시작하는 준비 자세도 진행도가 높으므로 실제 내려치기가 시작되는 마지막 1/3만 검사한다.
            if (Time.time - windupStartedAt >= ability.Data.windupTime * DownswingStart)
                TickHeadSweep();
        }
        else if (ability.State == AbilityState.Execute && cycleActive)
        {
            // Animator 전이의 한 프레임 지연과 관계없이 기존 점프가 시작한 프레임에 접지한다.
            if (ImpactFrame < 0)
            {
                ImpactFrame = Time.frameCount;
                ImpactTime = Time.time;
                SetVisible(true);
                ApplyPose(contactAngle);
                ImpactGroundGap = GroundGap;
                TickHeadSweep();
                SetHeadAttackActive(false);
                // 잔상에는 피해 판정이 없고, 점프나 Pool 반환과 독립적으로 사라진다.
                if (CanDisplay())
                    afterimage = StomperHammerAfterimage.Spawn(detachedVisual.transform, ImpactFadeDuration);
                SetVisible(false);
            }
        }
        else if (!cycleActive || ImpactFrame < 0 || ability.State != AbilityState.Recover) EndCycle();
        DrawSwingTrail();
        if (cycleActive && impactPoint != null) impactPoint.position = contactPoint;
    }

    bool CanDisplay()
    {
        return owner != null && ability != null && bodyCollider != null && visualPrefab != null
            && owner.gameObject.activeInHierarchy && owner.Brain != null && !owner.Brain.IsPaused
            && owner.Brain.current == ability;
    }

    void BeginCycle()
    {
        cycleActive = true;
        windupStartedAt = Time.time;
        lastAttackAngle = RaisedAngle;
        playersStruck.Clear();
        trailSamples.Clear();
        ownerFacing = owner.Facing >= 0 ? 1 : -1;
        // 원래 점프 방향은 그대로 두고, 해머만 그 반대쪽에서 몸체 옆 지면을 친다.
        strikeSide = -ownerFacing;
        Bounds bodyBounds = bodyCollider.bounds;
        floorY = bodyBounds.min.y;
        // 타격 머리가 몸체의 옆에 놓이도록, 바깥 변에서 머리 반폭과 여유만큼 축을 잡는다.
        float bodyEdge = strikeSide > 0 ? bodyBounds.max.x : bodyBounds.min.x;
        float pivotX = bodyEdge + strikeSide * (HeadHalfWidth * Length + BodySideClearance);
        if (owner.Motor != null)
        {
            Vector2 origin = new Vector2(pivotX, floorY + 0.75f);
            var floor = owner.Motor.RaycastEnvironment(origin, Vector2.down, Length + 3f);
            if (floor.collider != null) floorY = floor.point.y;
        }
        // 위쪽 머리가 180도 돌아 축 바로 아래 지면에 닿으려면 축은 길이만큼 높아야 한다.
        // 이 위치는 이번 Windup 시작에 한 번만 잡고 Execute가 끝날 때까지 바꾸지 않는다.
        fixedPivot = new Vector3(pivotX, floorY + Length, owner.transform.position.z);
        SideClearance = Mathf.Abs(pivotX - bodyEdge) - HeadHalfWidth * Length;
        ImpactFrame = -1;
        ImpactTime = -1f;
        MaxPivotDrift = 0f;
        ImpactGroundGap = float.NaN;
        NearImpactMinimumSurfaceGap = float.PositiveInfinity;
        CycleCount++;

        contactAngle = DownImpactAngle;
        // 한쪽 모서리가 아닌, 아래로 돌아온 머리 타격면의 중앙을 Feedback 지점으로 쓴다.
        contactPoint = new Vector3(fixedPivot.x, floorY, fixedPivot.z);
        EnsureVisual();
        if (impactPoint != null) impactPoint.position = contactPoint;
    }

    Vector3 ShaftPointWorld(float unitHeight)
    {
        float radians = SwingAngle * Mathf.Deg2Rad;
        return fixedPivot + new Vector3(-strikeSide * Mathf.Sin(radians) * Length * unitHeight,
            Mathf.Cos(radians) * Length * unitHeight, 0f);
    }

    // 머리 곡면 전체의 가장 낮은 높이. 사각 모서리 대신 실제 볼록한 타격면을 계산한다.
    float SurfaceGapAtAngle(float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians), cos = Mathf.Cos(radians);
        float minimum;
        if (cos >= 0f)
            minimum = HeadBottom * cos - HeadHalfWidth * Mathf.Abs(sin);
        else
        {
            // x·sinθ + (1-x²)·cosθ의 최솟값은 x=sinθ/(2cosθ) 또는 머리 양 끝이다.
            float x = Mathf.Clamp(sin / (2f * cos), -HeadHalfWidth, HeadHalfWidth);
            minimum = x * sin + (1f - x * x) * cos;
        }
        return fixedPivot.y - floorY + Length * minimum;
    }

    void EnsureVisual()
    {
        if (detachedVisual != null) return;
        detachedVisual = Instantiate(visualPrefab);
        detachedVisual.name = owner.name + "_StomperHammerVisual";
        detachedVisual.transform.SetParent(null, false);
        detachedVisual.SetActive(false);
        attackHead = detachedVisual.GetComponent<PolygonCollider2D>();
        if (attackHead != null)
        {
            headPath = attackHead.GetPath(0);
            outlinePositions = new Vector3[headPath.Length];
            if (dangerMaterial != null)
            {
                var outlineObject = new GameObject(owner.name + "_HammerDanger");
                dangerOutline = outlineObject.AddComponent<LineRenderer>();
                dangerOutline.sharedMaterial = dangerMaterial;
                dangerOutline.loop = true;
                dangerOutline.positionCount = headPath.Length;
                dangerOutline.numCornerVertices = 3;
                dangerOutline.sortingLayerName = "Particle";
                dangerOutline.sortingOrder = 3;
                dangerOutline.widthMultiplier = .12f;
                dangerOutline.enabled = false;
            }
        }
        if (swingArcSprite != null && swingArcMaterial != null)
        {
            var trailObject = new GameObject(owner.name + "_HammerSwingArc");
            swingArc = trailObject.AddComponent<SpriteRenderer>();
            swingArc.sprite = swingArcSprite;
            swingArc.sharedMaterial = swingArcMaterial;
            swingArc.sortingLayerName = "Player";
            swingArc.sortingOrder = 1;
            swingArc.enabled = false;

            arcProperties = new MaterialPropertyBlock();
            arcProperties.SetFloat(Shader.PropertyToID("_Arms"), 1f);
            arcProperties.SetFloat(Shader.PropertyToID("_QuadSize"), ArcQuadSize);
            arcProperties.SetFloat(Shader.PropertyToID("_Outer"), Length);
            arcProperties.SetFloat(Shader.PropertyToID("_HeadWidth"), Length * (HeadTop - HeadBottom));
            arcProperties.SetFloat(Shader.PropertyToID("_TailWidth"), .06f);
            arcProperties.SetFloat(Shader.PropertyToID("_RimWidth"), .062f);
        }
        if (visualOpacity >= 1f) return;
        // 별도 표시 개체에만 적용한다. Additive 셰이더도 흐려지도록 알파와 발광을 함께 낮춘다.
        foreach (var sprite in detachedVisual.GetComponentsInChildren<SpriteRenderer>(true))
        {
            var color = sprite.color; color.a *= visualOpacity; sprite.color = color;
            var block = new MaterialPropertyBlock(); sprite.GetPropertyBlock(block);
            int intensity = Shader.PropertyToID("_Intensity");
            float baseline = block.HasProperty(intensity) ? block.GetFloat(intensity)
                : sprite.sharedMaterial != null && sprite.sharedMaterial.HasProperty(intensity)
                    ? sprite.sharedMaterial.GetFloat(intensity) : 1f;
            block.SetFloat(intensity, baseline * visualOpacity); sprite.SetPropertyBlock(block);
        }
    }

    void ApplyPose(float angle)
    {
        if (detachedVisual == null) return;
        var target = detachedVisual.transform;
        target.SetPositionAndRotation(fixedPivot, Quaternion.Euler(0f, 0f, angle * strikeSide));
        target.localScale = new Vector3(Length * strikeSide, Length, 1f);
        SwingAngle = angle;
        GroundGap = HeadFaceCenterWorldPosition.y - floorY;
        MinimumSurfaceGap = SurfaceGapAtAngle(angle);
        if (ImpactFrame < 0 && angle <= -150f)
            NearImpactMinimumSurfaceGap = Mathf.Min(NearImpactMinimumSurfaceGap, MinimumSurfaceGap);
        MaxPivotDrift = Mathf.Max(MaxPivotDrift, Vector3.Distance(target.position, fixedPivot));
    }

    void TickHeadSweep()
    {
        RecordSwingSample(SwingAngle * strikeSide);
        if (attackHead == null || !owner.CanDealContactDamage || owner.Motor == null
            || !(ability.Data is ParabolicDashDataSO settings) || settings.hammerDamage <= 0f
            || owner.ResolveContactDamage(settings.hammerDamage) <= 0f)
        {
            SetHeadAttackActive(false);
            return;
        }

        SetHeadAttackActive(true);
        RaycastMotor2D.InvalidateAttackQuerySync();
        // 가변 프레임 사이에는 머리 곡면에 들어가는 직사각형만 이어서 검사한다.
        // 현재 자세에서는 실제 다각형을 사용해 붉은 경계에서만 맞는다.
        int steps = Mathf.CeilToInt(Mathf.Abs(lastAttackAngle - SwingAngle) / MaximumSweepStep);
        Vector2 sweepSize = new Vector2(HeadHalfWidth * 2f, SweepBoxTop - HeadBottom) * Length;
        Vector3 localSweepCenter = Vector3.up * ((SweepBoxTop + HeadBottom) * .5f * Length);
        for (int i = 1; i < steps; i++)
        {
            float angle = Mathf.Lerp(lastAttackAngle, SwingAngle, (float)i / steps) * strikeSide;
            Vector2 center = fixedPivot + Quaternion.Euler(0f, 0f, angle) * localSweepCenter;
            int count = owner.Motor.OverlapAttackBox(center, sweepSize, angle, sweepHits, playerLayer);
            for (int hitIndex = 0; hitIndex < count; hitIndex++)
                ApplyHeadContact(sweepHits[hitIndex], settings.hammerDamage);
        }
        owner.Motor.OverlapAttackCollider(attackHead, playerFilter, headHits);
        foreach (var hit in headHits)
            ApplyHeadContact(hit, settings.hammerDamage);
        lastAttackAngle = SwingAngle;
    }

    void ApplyHeadContact(Collider2D hit, float damage)
    {
        if (!CanDisplay() || !owner.CanDealContactDamage) return;
        var health = hit.GetComponentInParent<PlayerHealth>();
        if (health != null && playersStruck.Add(health))
            owner.RequestTouchDamage(health, damage, false);
    }

    void SetHeadAttackActive(bool active)
    {
        if (attackHead != null) attackHead.enabled = active;
        if (dangerOutline == null) return;
        dangerOutline.enabled = active;
        if (!active) return;
        for (int i = 0; i < headPath.Length; i++)
            outlinePositions[i] = attackHead.transform.TransformPoint(headPath[i]);
        dangerOutline.SetPositions(outlinePositions);
    }

    void RecordSwingSample(float angle)
    {
        if (swingArc == null) return;
        if (trailSamples.Count == 0)
            trailSamples.Add(new Vector2(windupStartedAt + ability.Data.windupTime * DownswingStart, RaisedAngle * strikeSide));
        trailHead = angle;
        trailSamples.Add(new Vector2(Time.time, angle));
    }

    void DrawSwingTrail()
    {
        if (swingArc == null) return;
        if (trailSamples.Count < 2) { swingArc.enabled = false; return; }

        float tailTime = Time.time - TrailDuration;
        while (trailSamples.Count > 2 && trailSamples[1].x <= tailTime)
            trailSamples.RemoveAt(0);
        Vector2 first = trailSamples[0];
        Vector2 second = trailSamples[1];
        float tail = Mathf.Lerp(first.y, second.y, Mathf.InverseLerp(first.x, second.x, tailTime));
        float alpha = ImpactFrame < 0 ? 1f : 1f - Mathf.Clamp01((Time.time - ImpactTime) / TrailDuration);
        swingArc.enabled = alpha > 0f && Mathf.Abs(trailHead - tail) >= .5f;
        if (!swingArc.enabled) return;

        swingArc.transform.position = fixedPivot;
        swingArc.transform.localScale = new Vector3(ArcQuadSize / swingArcSprite.bounds.size.x,
            ArcQuadSize / swingArcSprite.bounds.size.y, 1f);
        arcProperties.SetFloat(ArcHeadId, trailHead);
        arcProperties.SetFloat(ArcTailId, tail);
        arcProperties.SetFloat(ArcAlphaId, alpha);
        arcProperties.SetFloat(ArcTipScaleId, ImpactFrame < 0 ? 1f : 0f);
        swingArc.SetPropertyBlock(arcProperties);
    }

    void SetVisible(bool visible)
    {
        if (detachedVisual != null && detachedVisual.activeSelf != visible) detachedVisual.SetActive(visible);
    }

    void Hide() => SetVisible(false);
    void EndCycle()
    {
        cycleActive = false;
        SetHeadAttackActive(false);
        if (swingArc != null) swingArc.enabled = false;
        trailSamples.Clear();
        Hide();
    }
    void OnDisable() => EndCycle();
    void OnDestroy()
    {
        if (detachedVisual != null) Destroy(detachedVisual);
        if (dangerOutline != null) Destroy(dangerOutline.gameObject);
        if (swingArc != null) Destroy(swingArc.gameObject);
    }
}
