using UnityEngine;

// 적 무기의 휘두르기·찌르기 궤적을 월드에 남기는 표시 전용 컴포넌트(TrailRenderer 대체).
// - 휘두르기(호): Animation이 arcHead·arcTail(회전 중심의 부모 기준 각도, 0 = 위, + = 반시계)과 arcAlpha를 구동한다.
//   호 전체를 Enemy/SwingArc 셰이더가 한 번에 그리므로 프레임마다 점을 찍는 Trail처럼 각지지 않고 프레임률과 무관하다.
//   호는 방출하는 동안 회전 중심을 따라가고, 방출이 끝나면 그 자리에 남는다. 베고 난 뒤 순간이동·돌진으로 본체가 떠나도 잔상은 제자리에서 사라진다.
// - 찌르기·돌진(직선): Animation이 thrust를 0.5보다 크게 두는 동안 날 끝의 월드 위치를 기록하고,
//   streakDuration초 전 위치에서 지금 위치까지를 Enemy/ThrustStreak 셰이더로 그린다. 직선은 두 점만 있으면 되므로 프레임률과 무관하다.
// - 입자(선택): 이번 프레임에 날이 새로 휩쓴 범위(각도·거리)에 고르게 뿌린다. 개수가 시간이 아니라 휩쓴 양에 비례하므로 프레임률과 무관하다.
// - 위치는 화면용 보간 포즈(FixedMotionBody)를 따른다. 본체 그림은 렌더 때 고정 틱 사이로 보간되므로, 실제 Transform 위치를 쓰면
//   빠르게 움직일 때 궤적이 화면의 검과 어긋난다. 그래서 FixedMotionPresentation(실행 순서 99)이 이 프레임의 보간 포즈를 계산한 뒤에 읽는다.
// 물리·판정에는 관여하지 않는다. 표시 개체는 본체 계층 밖(월드)에 두고 본체가 꺼지면 함께 숨긴다.
[DefaultExecutionOrder(100)]
public sealed class EnemySwingTrail : MonoBehaviour
{
    [Header("Animation이 구동하는 값")]
    [Tooltip("호의 머리 각도(도). 방출 중에는 날의 각도, 방출이 끝나면 마지막 날의 각도")]
    public float arcHead;
    [Tooltip("호의 꼬리 각도(도). 머리와 같으면 호가 없다")]
    public float arcTail;
    [Tooltip("호의 밝기. 0이면 꺼짐, 1이면 날이 호의 머리에 있는 방출 중, 그 사이는 방출이 끝나고 남은 잔상")]
    public float arcAlpha;
    [Tooltip("0.5보다 크면 찌르기·돌진 궤적을 방출한다")]
    public float thrust;

    [Header("연결")]
    [SerializeField] private Transform pivot;                 // 회전 중심. 각도는 이 Transform의 부모 기준이다
    [SerializeField] private Transform weapon;                // 날 끝·날 범위의 기준
    [SerializeField] private Vector2 tipLocal = new Vector2(0f, 12f);
    [SerializeField] private SpriteRenderer arcPrefab;
    [SerializeField] private SpriteRenderer streakPrefab;
    [SerializeField, Min(0.02f)] private float streakDuration = 0.2f;

    [Header("입자(선택)")]
    [SerializeField] private ParticleSystem sparks;
    [SerializeField] private Vector2 arcSparkRadius = new Vector2(5.25f, 13.25f);    // 회전 중심 기준 반지름 범위
    [SerializeField, Min(0f)] private float sparksPerDegree = 1.2f;
    [SerializeField] private Vector2 bladeRange = new Vector2(4f, 12f);              // weapon 기준 날의 범위(y)
    [SerializeField, Min(0f)] private float sparksPerUnit = 13f;
    [SerializeField, Min(0f)] private float sparkHalfWidth = 0.5f;
    [SerializeField, Min(0f)] private float sparkSpeed = 5f;

    private static readonly int HeadId = Shader.PropertyToID("_Head"), TailId = Shader.PropertyToID("_Tail"), AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize"), OuterId = Shader.PropertyToID("_Outer"), LengthId = Shader.PropertyToID("_Length");
    private static readonly int HeadWidthId = Shader.PropertyToID("_HeadWidth"), GlowWidthId = Shader.PropertyToID("_GlowWidth"), TipScaleId = Shader.PropertyToID("_TipScale");

    private SpriteRenderer arcView, streakView;
    private MaterialPropertyBlock block;
    private FixedMotionBody body;
    // 표시용 난수. 전역 UnityEngine.Random을 쓰면 게임 로직의 난수 순서가 바뀌므로 따로 둔다.
    private readonly System.Random random = new System.Random();
    private float arcQuad, streakWidth, streakMargin;
    // 호: 방출이 끝난 자리와 방향
    private bool arcShown;
    private Vector3 arcAnchor;
    private Quaternion arcRotation;
    private float arcMirror = 1f, arcScale = 1f, lastSparkHead, sparkCarry;
    // 직선: 날 끝 위치 기록(시간순 링). 0.2초를 144fps에서 약 29칸, 1000fps에서도 200칸으로 담는다.
    private readonly float[] times = new float[256];
    private readonly Vector3[] points = new Vector3[256];
    private int first, count;
    private bool streakEmitting;
    private Vector3 streakHead, lastBase, lastTip;

    public bool ArcIsVisible => arcView != null && arcView.enabled;
    public bool StreakIsVisible => streakView != null && streakView.enabled;
    public SpriteRenderer ArcRenderer => arcView;
    public SpriteRenderer StreakRenderer => streakView;
    public float StreakLength { get; private set; }
    public Vector3 StreakHead => streakHead;
    public int SparkCount { get; private set; }

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        EnsureViews();
    }

    // 표시 개체는 씬 루트에 있어 씬 정리로 먼저 사라질 수 있다. 없어졌으면 다시 만든다.
    private void EnsureViews()
    {
        if (arcView == null && arcPrefab != null)
        {
            arcView = CreateView(arcPrefab, "_SwingArc");
            var look = arcView.sharedMaterial;
            // 사각형은 바깥 테두리와 빛 번짐까지 덮는다(Material 값은 회전 중심의 부모 기준 칸).
            arcQuad = look != null ? (look.GetFloat(OuterId) + look.GetFloat(GlowWidthId) * 6f + 0.1f) * 2f : 2f;
        }
        if (streakView == null && streakPrefab != null)
        {
            streakView = CreateView(streakPrefab, "_ThrustStreak");
            var look = streakView.sharedMaterial;
            streakMargin = look != null ? look.GetFloat(GlowWidthId) * 6f + 0.1f : 1f;
            streakWidth = look != null ? look.GetFloat(HeadWidthId) + streakMargin * 2f : 2f;
        }
    }

    private SpriteRenderer CreateView(SpriteRenderer prefab, string suffix)
    {
        var view = Instantiate(prefab);
        view.name = name + suffix;
        view.gameObject.layer = gameObject.layer;
        view.enabled = false;
        return view;
    }

    private void OnEnable() => ResetTrail();
    private void OnDisable() => ResetTrail();

    private void OnDestroy()
    {
        if (arcView != null) Destroy(arcView.gameObject);
        if (streakView != null) Destroy(streakView.gameObject);
    }

    private void ResetTrail()
    {
        arcShown = streakEmitting = false;
        count = first = 0;
        sparkCarry = 0f;
        StreakLength = 0f;
        if (arcView != null) arcView.enabled = false;
        if (streakView != null) streakView.enabled = false;
    }

    // Animator가 이 프레임의 값을 쓰고, FixedMotionPresentation이 보간 포즈를 계산한 뒤에 읽는다.
    private void LateUpdate()
    {
        EnsureViews();
        UpdateArc();
        UpdateStreak();
    }

    // 실제 Transform 기준 월드 위치·회전을 이 프레임의 화면용 보간 포즈로 옮긴다(본체에 FixedMotionBody가 없으면 그대로).
    private Vector3 Present(Vector3 world) => PresentTurn() * (world - BodyPosition()) + PresentedBodyPosition();
    private Quaternion PresentTurn()
    {
        if (!HasBody()) return Quaternion.identity;
        return body.PresentationRotation * Quaternion.Inverse(body.transform.rotation);
    }
    private Vector3 BodyPosition() => HasBody() ? body.transform.position : Vector3.zero;
    private Vector3 PresentedBodyPosition() => HasBody() ? body.PresentationPosition : Vector3.zero;
    private bool HasBody()
    {
        if (body == null) body = GetComponentInParent<FixedMotionBody>();
        return body != null && body.isActiveAndEnabled;
    }

    private void UpdateArc()
    {
        if (arcView == null || pivot == null) return;
        bool on = arcAlpha > 0f && Mathf.Abs(arcHead - arcTail) >= 0.5f;
        if (!on)
        {
            if (arcShown) { arcShown = false; arcView.enabled = false; }
            return;
        }
        // 약속: 날이 호의 머리에 있는 동안(방출 중)에는 arcAlpha가 1이고, 방출이 끝나 남은 잔상은 1보다 작다.
        bool emitting = arcAlpha >= 0.999f;
        if (!arcShown || emitting)
        {
            // 방출 중에는 회전 중심을 따라간다. 방출이 끝나면 그 자리·방향·좌우 반전을 유지해, 본체가 순간이동·돌진으로 떠나도 호는 남는다.
            Transform frame = pivot.parent != null ? pivot.parent : pivot;
            arcAnchor = Present(pivot.position);
            arcRotation = PresentTurn() * frame.rotation;
            arcMirror = frame.lossyScale.x < 0f ? -1f : 1f;
            arcScale = Mathf.Abs(frame.lossyScale.y);
        }
        if (!arcShown) { arcShown = true; lastSparkHead = arcTail; }
        float size = arcQuad * arcScale;
        arcView.transform.SetPositionAndRotation(arcAnchor, arcRotation);
        arcView.transform.localScale = new Vector3(size * arcMirror, size, 1f);
        arcView.GetPropertyBlock(block);
        block.SetFloat(QuadSizeId, arcQuad);
        block.SetFloat(HeadId, arcHead);
        block.SetFloat(TailId, arcTail);
        block.SetFloat(AlphaId, Mathf.Clamp01(arcAlpha));
        block.SetFloat(TipScaleId, emitting ? 1f : 0f);       // 날 끝 섬광은 날이 머리에 있을 때만
        arcView.SetPropertyBlock(block);
        arcView.enabled = true;
        if (sparks != null && sparksPerDegree > 0f) EmitArcSparks(lastSparkHead, arcHead);
        lastSparkHead = arcHead;
    }

    // 이번 프레임에 날이 새로 휩쓴 각도 범위에 입자를 고르게 뿌린다(각도당 개수가 일정하다).
    private void EmitArcSparks(float from, float to)
    {
        sparkCarry += Mathf.Abs(to - from) * sparksPerDegree;
        int amount = Mathf.FloorToInt(sparkCarry);
        if (amount <= 0) return;
        sparkCarry -= amount;
        for (int i = 0; i < amount; i++)
        {
            float radians = Range(from, to) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
            Vector2 local = direction * Range(arcSparkRadius.x, arcSparkRadius.y)
                + new Vector2(direction.y, -direction.x) * Range(-sparkHalfWidth, sparkHalfWidth);
            local.x *= arcMirror;
            EmitSpark(arcAnchor + arcRotation * (Vector3)(local * arcScale));
        }
    }

    private void UpdateStreak()
    {
        if (streakView == null || weapon == null) return;
        float now = Time.time;
        bool emitting = thrust > 0.5f;
        if (emitting)
        {
            Vector3 tip = Present(weapon.TransformPoint(tipLocal));
            Vector3 root = Present(weapon.TransformPoint(new Vector3(tipLocal.x, bladeRange.x, 0f)));
            if (!streakEmitting) { count = first = 0; lastBase = root; lastTip = tip; }
            Record(now, tip);
            streakHead = tip;
            if (sparks != null && sparksPerUnit > 0f) EmitThrustSparks(root, tip);
            lastBase = root; lastTip = tip;
        }
        streakEmitting = emitting;
        if (count == 0) { streakView.enabled = false; StreakLength = 0f; return; }

        // 꼬리는 streakDuration초 전의 날 끝이다. 방출이 끝나면 머리는 멈추고 꼬리가 따라잡아 사라진다.
        Vector3 tail = Sample(now - streakDuration);
        Vector3 span = streakHead - tail;
        span.z = 0f;
        float length = span.magnitude;
        StreakLength = length;
        if (length < 0.05f)
        {
            streakView.enabled = false;
            if (!emitting) count = first = 0;
            return;
        }
        Transform view = streakView.transform;
        view.SetPositionAndRotation((streakHead + tail) * 0.5f, Quaternion.Euler(0f, 0f, Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg - 90f));
        float quadLength = length + streakMargin * 2f;
        view.localScale = new Vector3(streakWidth, quadLength, 1f);
        streakView.GetPropertyBlock(block);
        block.SetVector(QuadSizeId, new Vector4(streakWidth, quadLength, 0f, 0f));
        block.SetFloat(LengthId, length);
        block.SetFloat(AlphaId, 1f);
        block.SetFloat(TipScaleId, emitting ? 1f : 0f);
        streakView.SetPropertyBlock(block);
        streakView.enabled = true;
    }

    // 이번 프레임에 날(뿌리 ~ 끝)이 지나간 띠에 입자를 고르게 뿌린다(이동 거리당 개수가 일정하다).
    private void EmitThrustSparks(Vector3 root, Vector3 tip)
    {
        sparkCarry += Vector2.Distance(lastTip, tip) * sparksPerUnit;
        int amount = Mathf.FloorToInt(sparkCarry);
        if (amount <= 0) return;
        sparkCarry -= amount;
        Vector2 blade = tip - root;
        Vector3 side = blade.sqrMagnitude > 0.0001f ? (Vector3)(new Vector2(-blade.y, blade.x).normalized) : Vector3.up;
        for (int i = 0; i < amount; i++)
        {
            float moment = (float)random.NextDouble(), along = (float)random.NextDouble();
            Vector3 position = Vector3.Lerp(Vector3.Lerp(lastBase, root, moment), Vector3.Lerp(lastTip, tip, moment), along);
            EmitSpark(position + side * Range(-sparkHalfWidth, sparkHalfWidth));
        }
    }

    private void EmitSpark(Vector3 position)
    {
        float angle = Range(0f, Mathf.PI * 2f), speed = sparkSpeed * Mathf.Sqrt((float)random.NextDouble());
        var emit = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed,
            applyShapeToPosition = false,
        };
        sparks.Emit(emit, 1);
        SparkCount++;
    }

    private float Range(float min, float max) => min + (max - min) * (float)random.NextDouble();

    private void Record(float time, Vector3 point)
    {
        int length = times.Length;
        if (count > 0)
        {
            int last = (first + count - 1) % length;
            if (time <= times[last]) { points[last] = point; return; }
        }
        // streakDuration보다 오래된 기록은 그 경계를 보간할 하나만 남긴다.
        while (count > 1 && times[(first + 1) % length] <= time - streakDuration) { first = (first + 1) % length; count--; }
        if (count == length) { first = (first + 1) % length; count--; }
        int index = (first + count) % length;
        times[index] = time; points[index] = point; count++;
    }

    private Vector3 Sample(float time)
    {
        int length = times.Length;
        // 방출을 시작하기 전의 시각은 시작한 자리로 본다(시작한 뒤 지나간 길만 그린다).
        if (time <= times[first]) return points[first];
        for (int i = 1; i < count; i++)
        {
            int next = (first + i) % length;
            if (time > times[next]) continue;
            int previous = (first + i - 1) % length;
            return Vector3.Lerp(points[previous], points[next], Mathf.InverseLerp(times[previous], times[next], time));
        }
        return points[(first + count - 1) % length];
    }
}
