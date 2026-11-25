using UnityEngine;
using UnityEngine.Rendering;

// Driller(드릴러)의 기계 자세를 한곳에서 구동한다.
// - 자세: 몸 전체(Rig)의 회전과 땅속 깊이. 눕기(드릴 발사)·뒤집기(땅파기 분출)·순찰 흔들림을 모두 여기서 계산한다.
// - 텔레스코프 팔: 마디(Sliced Sprite)가 차례로 미끄러져 나오고 드릴이 그 끝에 붙어 나간다. 마디 폭과 부품 Scale은 바꾸지 않는다.
// - 드릴 회전: 나선 홈 Sprite를 위상에 맞춰 바꾸고 코어 둘레 톱니를 돌린다.
// - 땅파기 Mask: 바닥선 위만 보이게 해 몸이 땅속으로 들어가고 나오는 모습을 자른다.
// 패턴은 목표 값만 정하고 실제 Transform은 Animator 다음(LateUpdate)에 이 컴포넌트가 쓴다. 이동·충돌은 모터가 그대로 맡는다.
[DefaultExecutionOrder(250)]
public sealed class DrillerRig : MonoBehaviour
{
    public enum PosePivot { Center, Body }

    [Header("Parts")]
    [SerializeField] private EnemyController controller;
    [Tooltip("FacingObject 아래에서 몸 전체(Model)를 회전·이동시키는 노드입니다.")]
    [SerializeField] private Transform rig;
    [Tooltip("팔의 시작점(목 아래)입니다. 팔은 이 노드의 아래(-Y) 방향으로 뻗습니다.")]
    [SerializeField] private Transform arm;
    [SerializeField] private Transform drill;
    [Tooltip("몸 쪽(굵은 마디)부터 드릴 쪽(가는 마디) 순서입니다. Draw Mode는 Sliced입니다.")]
    [SerializeField] private SpriteRenderer[] sleeves;
    [SerializeField] private SpriteRenderer grooves;
    [SerializeField] private Sprite[] grooveFrames;
    [SerializeField] private Transform gear;
    [Tooltip("땅속에 있는 동안 끄는 피격 Collider입니다(모터 Collider는 루트에 따로 있습니다).")]
    [SerializeField] private Collider2D hitBox;

    [Header("Burrow Mask")]
    [SerializeField] private SpriteMask burrowMask;
    [Tooltip("땅파기 동안 바닥선 위만 보이게 자를 Renderer입니다(외형과 피격 깜빡임 실루엣).")]
    [SerializeField] private SpriteRenderer[] maskedRenderers;
    [Tooltip("땅속에서 이동하는 동안 바닥에 보이는 균열 표시입니다.")]
    [SerializeField] private SpriteRenderer digTrail;

    [Header("Geometry (root local, upright, facing right)")]
    [SerializeField] private float feetY = -1f;
    [Tooltip("눕기(드릴 발사) 회전 중심. 몸통 컵의 가운데입니다.")]
    [SerializeField] private Vector2 bodyPivot = new(0f, .62f);
    [Tooltip("흔들림·뒤집기 회전 중심. 드릴 끝과 몸통 윗면의 가운데입니다.")]
    [SerializeField] private Vector2 centerPivot = new(0f, -.01f);
    [Tooltip("바닥에 닿는 점과 벽 쪽 끝을 찾을 외곽 점(직립 자세)입니다.")]
    [SerializeField] private Vector2[] hull =
    {
        new(0f, -1f), new(-.4f, -.04f), new(.4f, -.04f), new(-.36f, .14f), new(.36f, .14f),
        new(-.56f, .24f), new(.56f, .24f), new(-.64f, .4f), new(.64f, .4f), new(-.64f, .98f), new(.64f, .98f)
    };
    [SerializeField, Min(.1f)] private float drillLength = .96f;
    [SerializeField, Min(0f)] private float minSleeveLength = .07f;
    [Tooltip("벽 쪽 여유를 줄 때 외형과 벽 사이에 남기는 거리(칸)입니다.")]
    [SerializeField, Min(0f)] private float wallMargin = .04f;

    [Header("Spin")]
    [SerializeField, Min(.1f)] private float spinResponse = 6f;
    [SerializeField, Min(.1f)] private float groovePitchesPerTurn = 2f;
    [SerializeField] private float gearTurnsPerDrillTurn = .35f;
    [SerializeField, Min(1f)] private float maxGearStepDegrees = 22f;
    [Tooltip("한 프레임에 넘기는 홈 그림 수의 상한(거꾸로 도는 착시 방지).")]
    [SerializeField, Min(.5f)] private float maxGrooveFramesPerStep = 2f;
    [SerializeField, Min(0f)] private float idleSpin = 1.2f;
    [SerializeField, Min(0f)] private float spinPerMoveSpeed = .35f;

    [Header("Idle Wobble")]
    [SerializeField, Min(0f)] private float wobbleDegrees = 2.2f;
    [SerializeField, Min(0f)] private float wobbleSpeed = 5.5f;
    [SerializeField, Min(0f)] private float movingWobbleBoost = .45f;

    // 패턴이 정하는 목표 값. ResetMechanism으로 기본 자세(직립·흔들림)로 돌아간다.
    public float PoseAngle { get; set; }
    public PosePivot Pivot { get; set; }
    public bool GroundSnap { get; set; } = true;
    public float BurrowDepth { get; set; }
    public float Extension { get; set; }
    // 0보다 작으면 순찰 회전(이동 속도에 비례)을 쓴다.
    public float SpinTarget { get; set; } = -1f;
    public bool IdleWobble { get; set; } = true;

    public float Spin { get; private set; }
    public float DrillLength => drillLength;
    public float FeetY => feetY;
    // 직립 자세의 몸 높이(드릴 끝~몸통 윗면)
    public float VisualHeight
    {
        get
        {
            if (hull == null || hull.Length == 0) return 0f;
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            foreach (var point in hull) { low = Mathf.Min(low, point.y); high = Mathf.Max(high, point.y); }
            return high - low;
        }
    }
    public bool IsMaskActive => maskActive;
    public bool IsHittable => hitBox == null || hitBox.enabled;
    public Transform Arm => arm;
    public Transform Drill => drill;
    public int GrooveFrame => Mathf.FloorToInt(grooveFrame);
    // 검증용: 마지막으로 적용한 자세
    public float AppliedAngle { get; private set; }
    public Vector2 AppliedOffset { get; private set; }

    private float grooveFrame;
    private float gearAngle;
    private float wobblePhase;
    private float[] sleeveWidths;
    private bool maskActive;
    private float maskGroundY;
    private SortingGroup isolation;
    private bool isolationOwned;
    private SpriteMaskInteraction[] savedInteractions;
    private FixedMotionBody motionBody;
    // 벽까지의 여유(월드, 루트 중심 기준). 무한대면 제한 없음.
    private float roomLeft = float.PositiveInfinity, roomRight = float.PositiveInfinity;

    private void Awake()
    {
        if (controller == null) controller = GetComponent<EnemyController>();
        sleeveWidths = new float[sleeves != null ? sleeves.Length : 0];
        for (int i = 0; i < sleeveWidths.Length; i++)
            if (sleeves[i] != null) sleeveWidths[i] = sleeves[i].size.x;
        if (burrowMask != null) burrowMask.enabled = false;
        if (digTrail != null) digTrail.enabled = false;
    }

    private void OnEnable()
    {
        ResetMechanism();
        Spin = idleSpin;
        ApplyPose();
    }

    private void OnDisable()
    {
        // Pool 반환·사망 때 Mask와 피격 Collider를 평소 상태로 되돌린다.
        EndBurrowMask();
        SetDigTrail(false);
        SetHittable(true);
        ResetMechanism();
        ApplyPose();
    }

    /// <summary>기본 자세(직립, 순찰 흔들림, 팔 접힘, 순찰 회전, 벽 여유 없음)로 돌아간다. Mask·피격 상태는 바꾸지 않는다.</summary>
    public void ResetMechanism()
    {
        PoseAngle = 0f;
        Pivot = PosePivot.Center;
        GroundSnap = true;
        BurrowDepth = 0f;
        Extension = 0f;
        SpinTarget = -1f;
        IdleWobble = true;
        ClearSideRoom();
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        float speed = controller != null ? Mathf.Abs(controller.Velocity.x) : 0f;
        float target = SpinTarget >= 0f ? SpinTarget : idleSpin + speed * spinPerMoveSpeed;
        Spin = Mathf.Lerp(Spin, target, 1f - Mathf.Exp(-spinResponse * dt));
        AdvanceSpin(Spin * dt);
        wobblePhase += wobbleSpeed * (1f + speed * movingWobbleBoost) * dt;
        ApplyPose();
        HoldMask();
        FollowIsolationOrder();
    }

    private void AdvanceSpin(float turns)
    {
        if (grooves != null && grooveFrames != null && grooveFrames.Length > 0)
        {
            int count = grooveFrames.Length;
            float step = Mathf.Clamp(turns * groovePitchesPerTurn * count, -maxGrooveFramesPerStep, maxGrooveFramesPerStep);
            grooveFrame = Mathf.Repeat(grooveFrame + step, count);
            var frame = grooveFrames[Mathf.Clamp(Mathf.FloorToInt(grooveFrame), 0, count - 1)];
            if (grooves.sprite != frame) grooves.sprite = frame;
        }
        if (gear != null)
        {
            float degrees = Mathf.Clamp(turns * 360f * gearTurnsPerDrillTurn, -maxGearStepDegrees, maxGearStepDegrees);
            gearAngle = Mathf.Repeat(gearAngle + degrees, 360f);
            gear.localRotation = Quaternion.Euler(0f, 0f, -gearAngle);
        }
    }

    /// <summary>지금 목표 값으로 Transform을 바로 맞춘다. 패턴이 같은 프레임에 드릴 위치를 읽거나 몸통 판정을 쓰기 전에 부른다.</summary>
    public void ApplyPose()
    {
        if (rig == null) return;
        float angle = PoseAngle + (IdleWobble ? wobbleDegrees * Mathf.Sin(wobblePhase) : 0f);
        Vector2 pivot = Pivot == PosePivot.Body ? bodyPivot : centerPivot;
        var rotation = Quaternion.Euler(0f, 0f, angle);
        Vector2 offset = pivot - (Vector2)(rotation * pivot);
        if (hull != null && hull.Length > 0)
        {
            float lowest = float.PositiveInfinity, left = float.PositiveInfinity, right = float.NegativeInfinity;
            foreach (var point in hull)
            {
                Vector2 p = (Vector2)(rotation * point) + offset;
                lowest = Mathf.Min(lowest, p.y);
                left = Mathf.Min(left, p.x);
                right = Mathf.Max(right, p.x);
            }
            if (GroundSnap) offset.y += feetY - lowest;
            offset.x += SideShift(left, right);
        }
        offset.y -= Mathf.Max(0f, BurrowDepth);
        rig.localPosition = new Vector3(offset.x, offset.y, rig.localPosition.z);
        rig.localRotation = rotation;
        AppliedAngle = angle;
        AppliedOffset = offset;
        ApplyExtension(Extension);
    }

    // 회전한 외형이 양옆 벽 안에 들도록 Rig를 옆으로 옮길 양(Facing 기준 로컬). 양쪽이 다 좁으면 가운데에 둔다.
    private float SideShift(float left, float right)
    {
        if (float.IsInfinity(roomLeft) && float.IsInfinity(roomRight)) return 0f;
        int facing = controller != null && controller.Facing < 0 ? -1 : 1;
        // 월드 왼쪽·오른쪽 여유를 Facing 로컬의 뒤(−)·앞(+)으로 바꾼다.
        float back = facing > 0 ? roomLeft : roomRight;
        float front = facing > 0 ? roomRight : roomLeft;
        float low = -back + wallMargin, high = front - wallMargin;
        if (high - low < right - left) return (low + high) * .5f - (left + right) * .5f;
        if (right > high) return high - right;
        if (left < low) return low - left;
        return 0f;
    }

    /// <summary>모터 질의로 루트 중심 높이(와 추가 높이)에서 양옆 벽까지의 거리를 재서 자세가 벽을 넘지 않게 한다.</summary>
    public void MeasureSideRoom(float maxDistance, params float[] heightOffsets)
    {
        if (controller == null || controller.Motor == null) { ClearSideRoom(); return; }
        roomLeft = roomRight = float.PositiveInfinity;
        Vector2 root = transform.position;
        foreach (float height in heightOffsets)
        {
            Vector2 origin = root + Vector2.up * height;
            var left = controller.Motor.RaycastEnvironment(origin, Vector2.left, maxDistance);
            var right = controller.Motor.RaycastEnvironment(origin, Vector2.right, maxDistance);
            if (left.collider != null) roomLeft = Mathf.Min(roomLeft, left.distance);
            if (right.collider != null) roomRight = Mathf.Min(roomRight, right.distance);
        }
    }

    public void ClearSideRoom() => roomLeft = roomRight = float.PositiveInfinity;

    private void ApplyExtension(float value)
    {
        float extension = Mathf.Max(0f, value);
        if (drill != null) drill.localPosition = new Vector3(0f, -extension, drill.localPosition.z);
        if (sleeves == null) return;
        int count = sleeves.Length;
        for (int i = 0; i < count; i++)
        {
            var sleeve = sleeves[i];
            if (sleeve == null) continue;
            float from = extension * i / count, to = extension * (i + 1) / count;
            float length = to - from;
            bool visible = length >= minSleeveLength;
            if (sleeve.enabled != visible) sleeve.enabled = visible;
            if (!visible) continue;
            sleeve.size = new Vector2(sleeveWidths != null && i < sleeveWidths.Length ? sleeveWidths[i] : sleeve.size.x, length);
            sleeve.transform.localPosition = new Vector3(0f, -(from + to) * .5f, sleeve.transform.localPosition.z);
        }
    }

    // 팔 시작점과 방향(월드). ApplyPose 뒤에 읽는다.
    public Vector2 ArmOriginWorld => arm != null ? (Vector2)arm.position : (Vector2)transform.position;
    public Vector2 ArmDirectionWorld => arm != null ? ((Vector2)arm.TransformDirection(Vector3.down)).normalized : Vector2.right;
    public Vector2 DrillTipWorld => drill != null ? (Vector2)drill.TransformPoint(new Vector3(0f, -drillLength, 0f)) : ArmOriginWorld;

    // 검증용: 지금 자세의 외형 외곽 점(월드)
    public Vector2[] HullWorld()
    {
        if (hull == null || rig == null) return new Vector2[0];
        var model = rig.childCount > 0 ? rig.GetChild(0) : rig;
        var points = new Vector2[hull.Length];
        for (int i = 0; i < hull.Length; i++) points[i] = model.TransformPoint(hull[i]);
        return points;
    }

    public void SetHittable(bool value)
    {
        if (hitBox != null && hitBox.enabled != value) hitBox.enabled = value;
    }

    public void SetDigTrail(bool value)
    {
        if (digTrail != null && digTrail.enabled != value) digTrail.enabled = value;
    }

    /// <summary>바닥선(월드 y) 위만 보이게 한다. 같은 자리의 다른 적에 Mask가 섞이지 않도록 이 적을 Sorting Group으로 묶는다.</summary>
    public void BeginBurrowMask(float groundY)
    {
        maskGroundY = groundY;
        if (maskActive || burrowMask == null) { HoldMask(); return; }
        maskActive = true;
        if (isolation == null)
        {
            isolation = GetComponent<SortingGroup>();
            if (isolation == null)
            {
                isolation = gameObject.AddComponent<SortingGroup>();
                isolation.enabled = false;
            }
        }
        isolationOwned = !isolation.enabled;
        if (isolationOwned)
        {
            FollowIsolationOrder();
            isolation.enabled = true;
        }
        int count = maskedRenderers != null ? maskedRenderers.Length : 0;
        if (savedInteractions == null || savedInteractions.Length != count) savedInteractions = new SpriteMaskInteraction[count];
        for (int i = 0; i < count; i++)
        {
            if (maskedRenderers[i] == null) continue;
            savedInteractions[i] = maskedRenderers[i].maskInteraction;
            maskedRenderers[i].maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        }
        burrowMask.enabled = true;
        HoldMask();
    }

    public void HoldMaskAt(float groundY)
    {
        maskGroundY = groundY;
        HoldMask();
    }

    public void EndBurrowMask()
    {
        if (!maskActive) return;
        maskActive = false;
        if (burrowMask != null) burrowMask.enabled = false;
        int count = maskedRenderers != null ? maskedRenderers.Length : 0;
        for (int i = 0; i < count && savedInteractions != null && i < savedInteractions.Length; i++)
            if (maskedRenderers[i] != null) maskedRenderers[i].maskInteraction = savedInteractions[i];
        if (isolation != null && isolationOwned) isolation.enabled = false;
        isolationOwned = false;
    }

    // 묶음은 본체가 지금 그려지는 순서를 따른다. 일격필살 연출처럼 다른 곳이 Renderer 정렬을 올리면 묶음도 같이 올라간다.
    private void FollowIsolationOrder()
    {
        if (isolation == null || !isolationOwned || maskedRenderers == null || maskedRenderers.Length == 0 || maskedRenderers[0] == null) return;
        var reference = maskedRenderers[0];
        if (isolation.sortingLayerID != reference.sortingLayerID) isolation.sortingLayerID = reference.sortingLayerID;
        if (isolation.sortingOrder != reference.sortingOrder) isolation.sortingOrder = reference.sortingOrder;
    }

    // Mask 아래 변을 바닥선에 둔다. 화면 보간(FixedMotionBody)이 렌더 때 루트 차이만큼 옮기므로 그만큼 미리 빼 둔다.
    private void HoldMask()
    {
        if (!maskActive || burrowMask == null) return;
        var mask = burrowMask.transform;
        float halfHeight = burrowMask.sprite != null ? burrowMask.sprite.bounds.extents.y * Mathf.Abs(mask.lossyScale.y) : Mathf.Abs(mask.lossyScale.y) * .5f;
        float presentationShift = 0f;
        if (motionBody == null) motionBody = GetComponent<FixedMotionBody>();
        if (motionBody != null && motionBody.isActiveAndEnabled)
            presentationShift = motionBody.PresentationPosition.y - transform.position.y;
        mask.position = new Vector3(transform.position.x, maskGroundY + halfHeight - presentationShift, mask.position.z);
    }
}
