using UnityEngine;

// 파괴형 던전 Prop의 중력 본체(DESIGN_V4 §2.9, R9).
// - Enemy와 같은 이동 경로만 쓴다: Velocity → CharacterController2D.FixedUpdate → RaycastMotor2D.Move → transform.position += 결과.
//   위치를 직접 대입하지 않고 Physics2D 질의도 하지 않는다(받침 확인은 모터의 세로 광선이 매 틱 한다 — 받침이 부서지면 다음 틱에 떨어진다).
// - 세로로만 움직인다: 가로 속도 0, 외력(넉백·컨베이어·피스톤·폭발) 무시. 캐릭터는 충돌 대상이 아니라 떨어지는 Prop은 캐릭터를 지나간다(피해 없음).
// - 충돌 마스크 = 지상 적과 같음(Platform) + 통과 발판(OneWay) 지원: 지형 셀·생성 발판·다른 상자의 고체(SolidMount)·OneWay(파괴 격자·정비 발판)
//   위에 선다. 움직이는 발판은 통과한다(적과 같음).
// - 모터 몸통: 고체(SolidMount — 충돌 마스크 안의 트리거 아닌 Collider)가 있으면 그 고체, 없으면 Enemy 레이어 hurtbox(직렬화 col).
//   고체를 몸통으로 쓸 때는 모터의 IgnoreOwnBodyHits를 켠다(광선이 자기 고체 안에서 시작해도 자기 몸통은 건너뛴다).
//   4차 검토 12·13: hurtbox(고체 + 사방 0.04 — 탄이 벽 판정보다 먼저 맞히게 둔 피격 여유)를 몸통으로 쓰면
//   (12) 몸통 바닥이 고체 바닥보다 0.04 아래라 OneWay 윗면에 바로 세운 상자를 모터가 '이미 발판 아래'로 보고 떨어뜨렸고,
//   (13) 맨 왼쪽 아래 광선이 footprint 밖 0.025칸(왼쪽 이웃 고체 안)에서 시작해 거리 0으로 맞아 매 틱 skin만큼 이웃을 타고 올라갔다.
//   고체 몸통은 footprint와 같아 광선이 footprint 안에서 시작하고, 바닥에 붙어 선다(뜨는 틈 없음, 쌓아도 틈 없음).
// - 부서지는 순간 멈춘다(DestructibleProp.Break가 Collider를 끄기 전에 Halt). 풀에서 꺼낼 때 속도·외력·통과 발판 상태를 지우고 다시 켠다.
// - 배치별 설정 '중력'(떨어짐/고정): 고정이면 본체를 꺼 둔다(선반 위·일부러 공중에 둘 배치). 편집기 미리보기(Edit Mode)에서는 값만 기억한다.
// - 아주 멀리 떨어지면(출발 높이보다 FallLimit칸 아래) 멈춘다(방 밖으로 빠진 경우의 안전장치).
[DisallowMultipleComponent]
public sealed class DungeonPropBody : CharacterController2D, IDungeonPropPart, IDungeonPropSettings
{
    public const string GravityKey = "propGravity";
    private static readonly string[] GravityOptions = { "떨어짐", "고정" };
    private const float FallLimit = 80f;

    [Header("중력 본체")]
    [Tooltip("배치 설정 '중력'의 기본값. 켜면 받침이 없을 때 떨어진다.")]
    [SerializeField] private bool gravityOn = true;
    [Tooltip("중력 가속도(칸/초²). 지상 적(EnemyController 기본 -30)과 같다.")]
    [SerializeField] private float gravity = -30f;
    [SerializeField, Min(1f)] private float maxFallSpeed = 22f;
    [Tooltip("떨어져 착지할 때 내는 효과(기존 효과음 재사용). 비워도 된다.")]
    [SerializeField] private FeedbackDataSO landFeedback;
    [Tooltip("이 속도(칸/초) 이상으로 떨어져 착지했을 때만 착지 효과를 낸다(방에 세울 때의 미세 정착은 조용히).")]
    [SerializeField, Min(0f)] private float landFeedbackSpeed = 6f;

    private bool activeGravity = true;
    private bool halted;
    private bool live;
    private float peakFallSpeed;
    private float startY;

    /// <summary>배치 설정이 '떨어짐'이면 true.</summary>
    public bool GravityActive => activeGravity;
    /// <summary>부서져 멈췄으면 true(다음 대여에서 풀린다).</summary>
    public bool IsHalted => halted;
    /// <summary>이번 대여에서 착지 효과를 낸 횟수(검사용).</summary>
    public int LandingImpacts { get; private set; }
    /// <summary>마지막 착지 직전의 낙하 속도(칸/초, 검사용).</summary>
    public float LastLandingSpeed { get; private set; }
    /// <summary>모터 몸통(고체가 있으면 고체, 없으면 hurtbox) — 쌓인 높이를 잴 때 쓴다.</summary>
    public Collider2D BodyCollider => col;
    /// <summary>고체(SolidMount)를 모터 몸통으로 쓰면 true(검사용).</summary>
    public bool UsesSolidBody { get; private set; }

    protected override void Awake()
    {
        LockFacing = true;              // facingModel이 없다(CharacterController2D.HandleCollisionResult가 건드리지 않게)
        IgnoreExternalForces = true;    // 가로 이동 없음
        Faction = Faction.Enemy;
        // 고체가 있으면 고체를 몸통으로(직렬화된 col = hurtbox는 고체 없는 Prop의 몸통). Prefab을 다시 만들지 않아도 실행 중에 고른다.
        var solid = FindSolidBody();
        UsesSolidBody = solid != null;
        if (solid != null) col = solid;
        if (col == null) col = FindHurtbox();
        if (col == null)
        {
            Debug.LogWarning($"[DungeonPropBody] {name}: 모터 몸통(고체 또는 Enemy 레이어 hurtbox)이 없어 중력을 끈다.", this);
            enabled = false;
            return;
        }
        base.Awake();                   // RaycastMotor2D 추가 + SetRaycastMotor2D(col) + FixedMotionBody
        motor.SupportsOneWayPlatforms = true;
        motor.SupportsMovingPlatforms = false;
        motor.IgnoreOwnBodyHits = true;   // 고체 몸통: 자기 고체에 맞은 광선은 건너뛴다(hurtbox 몸통은 마스크 밖이라 영향 없음)
        OnLanded += HandleLanded;
        activeGravity = gravityOn;
        enabled = false;                // 풀에서 꺼내질 때(OnPropSpawned) 켠다
    }

    // 루트 아래의 고체: 트리거가 아니고 충돌 마스크(Platform) 안에 있는 Collider. OneWay 면(TransparentPlatform)·hurtbox(Enemy 트리거)는 빠진다.
    private Collider2D FindSolidBody()
    {
        foreach (var c in GetComponentsInChildren<Collider2D>(true))
            if (c != null && !c.isTrigger && (collisionLayer.value & (1 << c.gameObject.layer)) != 0) return c;
        return null;
    }

    private Collider2D FindHurtbox()
    {
        var destructible = GetComponent<DestructibleProp>();
        if (destructible != null && destructible.Hurtbox != null) return destructible.Hurtbox;
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        foreach (var c in GetComponentsInChildren<Collider2D>(true))
            if (c != null && c.gameObject.layer == enemyLayer) return c;
        return null;
    }

    protected override Vector2 CalculateVelocity()
    {
        Velocity.x = 0f;
        Velocity.y = Mathf.Max(Velocity.y + gravity * Time.deltaTime, -maxFallSpeed);
        peakFallSpeed = Mathf.Max(peakFallSpeed, -Velocity.y);
        if (transform.position.y < startY - FallLimit) Halt();
        return halted ? Vector2.zero : Velocity;
    }

    private void HandleLanded()
    {
        float speed = peakFallSpeed;
        peakFallSpeed = 0f;
        if (speed < landFeedbackSpeed) return;
        LastLandingSpeed = speed;
        LandingImpacts++;
        if (landFeedback != null && col != null)
            DungeonPropFeedback.Play(landFeedback, new Vector3(col.bounds.center.x, col.bounds.min.y, transform.position.z), Quaternion.identity);
    }

    /// <summary>지금 자리에서 멈춘다(부서질 때). 다음 대여에서 OnPropSpawned가 풀어 준다.</summary>
    public void Halt()
    {
        halted = true;
        Velocity = Vector2.zero;
        externalForce = Vector2.zero;
        if (Application.isPlaying) enabled = false;
    }

    // ── IDungeonPropPart ─────────────────────────────

    public void OnPropSpawned(DungeonProp prop)
    {
        live = true;
        halted = false;
        Velocity = Vector2.zero;
        externalForce = Vector2.zero;
        peakFallSpeed = 0f;
        LandingImpacts = 0;
        LastLandingSpeed = 0f;
        startY = transform.position.y;
        motor?.ResetOneWayState();
        enabled = motor != null && activeGravity;
    }

    public void OnPropReturning(DungeonProp prop)
    {
        live = false;
        Velocity = Vector2.zero;
        externalForce = Vector2.zero;
        if (Application.isPlaying) enabled = false;
    }

    // ── 배치별 설정 ─────────────────────────────

    public WaveDeviceParameter GravityParameter => new WaveDeviceParameter(
        GravityKey, "중력", "떨어짐: 받침이 없거나 부서지면 아래로 떨어집니다. 고정: 놓은 자리에 그대로 있습니다(선반 위 등).",
        GravityOptions, gravityOn ? 0f : 1f);

    public WaveDeviceParameter[] PropParameters => new[] { GravityParameter };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        activeGravity = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, GravityParameter)) == 0;
        // 편집기 미리보기(Edit Mode)에서는 값만 기억한다. 실행 중에는 대여 중이고 멈추지 않았을 때만 켜고 끈다.
        if (!Application.isPlaying || !live || halted || motor == null) return;
        if (!activeGravity) Velocity = Vector2.zero;
        enabled = activeGravity;
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        range = default;
        return false;
    }
}
