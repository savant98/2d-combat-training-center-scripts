using UnityEngine;

// Animator가 swingProgress를 기록하고, 표시 전용 해머는 손잡이 끝의 고정 월드 축으로 돈다.
// 적의 이동·충돌·공격 타이밍은 기존 ParabolicDashAbility와 RaycastMotor2D가 그대로 담당한다.
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

    [Header("Animator 구동")]
    [Range(0f, 1f)] public float swingProgress;

    // 단위 외형의 손잡이 끝은 y=0, 머리 안쪽은 y=0.72, 바깥 타격면은 y=1-x²다.
    const float Length = 6.25f;
    const float HeadHalfWidth = 0.22f;
    const float HeadBottom = 0.72f;
    const float HeadTop = 1f;
    // 원본 Windup의 0→1/3→2/3→1초 박자에 맞춰 낮은 준비 자세→들기→젖힘→타격으로 돈다.
    // swingProgress는 현재 회전 범위(20°~-180°)의 정규화 값이며 원본 관절 값은 복사하지 않는다.
    const float RestAngle = -135f;
    const float RaisedAngle = 20f;
    const float DownImpactAngle = -180f;
    const float BodySideClearance = 0.175f;
    const float ImpactFadeDuration = 0.5f;
    const float WindupProgressLimit = 0.999f;

    GameObject detachedVisual;
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
                // 최종 타격 자세를 그 프레임에 복제한다. 잔상에는 소유자 참조가 없어 점프·Pool 반환과 독립적이다.
                afterimage = StomperHammerAfterimage.Spawn(detachedVisual.transform, ImpactFadeDuration);
                SetVisible(false);
            }
        }
        else EndCycle();
        if (cycleActive && impactPoint != null) impactPoint.position = contactPoint;
    }

    bool CanDisplay()
    {
        return owner != null && ability != null && bodyCollider != null && visualPrefab != null
            && owner.gameObject.activeInHierarchy && owner.Brain != null && !owner.Brain.IsPaused;
    }

    void BeginCycle()
    {
        cycleActive = true;
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

    void SetVisible(bool visible)
    {
        if (detachedVisual != null && detachedVisual.activeSelf != visible) detachedVisual.SetActive(visible);
    }

    void Hide() => SetVisible(false);
    void EndCycle() { cycleActive = false; Hide(); }
    void OnDisable() => EndCycle();
    void OnDestroy()
    {
        if (detachedVisual != null) Destroy(detachedVisual);
    }
}
