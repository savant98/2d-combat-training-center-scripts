using UnityEngine;

/// <summary>
/// 매달린 화물(DungeonPropHangingCargo, 2×2 고체 + 사슬)의 표시 움직임과 착지 반응(2026-11-24, 던전 2차 decor).
/// - 판정: 화물 고체(SolidMount, Platform 레이어)는 셀 경계에 고정되어 움직이지 않는다. 엄폐·발판이자 Swarmer 진로·Roller 돌진을 막는다.
/// - 흔들림(표시만): 사슬은 PropSegmentRun이 정상파로, 걸쇠(hook)는 작은 각도로, 화물 몸체 표시는 1도 미만으로 함께 흔든다.
/// - 착지: 화물 윗변 위 얇은 띠를 시설 모터의 OverlapAttackBox(Player 레이어)로 묻는다(Physics2D 직접 질의 없음). 비어 있던 띠에 Player가
///   내려앉으면 먼지 입자·삐걱 소리를 내고 흔들림을 잠깐 키운다. 화면 근처일 때만 묻는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PropHangingCargo : MonoBehaviour, IDungeonPropPart
{
    [SerializeField, Tooltip("사슬(길이 설정·정상파 흔들림)")] private PropSegmentRun chain;
    [SerializeField, Tooltip("걸쇠(화물 윗변 가운데 축)")] private Transform hook;
    [SerializeField, Tooltip("화물 몸체 표시(축 = 걸쇠 자리). Collider는 따로 고정되어 있다.")] private Transform cargo;
    [SerializeField, Min(0f)] private float hookSwingDegrees = 3.5f;
    [SerializeField, Min(0f)] private float cargoTiltDegrees = .3f;
    [SerializeField, Min(.1f)] private float swayPeriod = 3.4f;

    [Header("착지")]
    [SerializeField, Tooltip("착지를 묻는 띠의 가운데(루트 좌표, 회전 전)")] private Vector2 senseCenter = new Vector2(0f, 1.15f);
    [SerializeField, Tooltip("착지를 묻는 띠의 크기. 맞닿은 옆면이 겹침으로 잡히지 않게 화물 폭보다 조금 좁다.")] private Vector2 senseSize = new Vector2(1.8f, .26f);
    [SerializeField] private LayerMask targetMask;
    [SerializeField, Tooltip("이 속도(칸/초)보다 빠르게 올라가는 중이면 착지로 보지 않는다")] private float maxRiseSpeed = .5f;
    [SerializeField] private ParticleSystem landingDust;
    [SerializeField, Min(0)] private int landingDustCount = 12;
    [SerializeField, Tooltip("착지 때 흔들림을 몇 배로 키울지(서서히 1로 돌아온다)")] private float landingKick = 2.2f;
    [SerializeField, Min(.01f), Tooltip("키운 흔들림이 1로 돌아오는 시간(초)")] private float kickRecovery = 1.6f;
    [SerializeField, Min(0f)] private float landingCooldown = .35f;
    [SerializeField] private DungeonPropLoopSound sound;
    [SerializeField, Tooltip("착지 때 낼 간헐음 번호(삐걱)")] private int creakCue;

    private RaycastMotor2D motor;
    private Collider2D[] hits = new Collider2D[8];
    private float clock, kick, lastLanding = float.NegativeInfinity;
    private bool occupied, nearScreen;

    /// <summary>풀에서 꺼낸 뒤 착지 반응 횟수(검사용).</summary>
    public int LandingCount { get; private set; }
    /// <summary>지금 Player가 윗변에 올라 있는가(검사용).</summary>
    public bool Occupied => occupied;
    public PropSegmentRun Chain => chain;
    public float HookAngle => hook != null ? Mathf.DeltaAngle(0f, hook.localEulerAngles.z) : 0f;
    public float CargoAngle => cargo != null ? Mathf.DeltaAngle(0f, cargo.localEulerAngles.z) : 0f;

    private void Awake()
    {
        motor = GetComponent<RaycastMotor2D>();
        if (targetMask.value == 0) targetMask = LayerMask.GetMask("Player");
        if (sound == null) sound = GetComponent<DungeonPropLoopSound>();
    }

    public void OnPropSpawned(DungeonProp prop)
    {
        clock = 0f;
        kick = 0f;
        occupied = false;
        nearScreen = false;
        LandingCount = 0;
        lastLanding = float.NegativeInfinity;
        if (hook != null) hook.localRotation = Quaternion.identity;
        if (cargo != null) cargo.localRotation = Quaternion.identity;
        if (landingDust != null) landingDust.Clear(true);
    }

    public void OnPropReturning(DungeonProp prop)
    {
        occupied = false;
        if (chain != null) chain.SwayScale = 1f;
    }

    private void Update()
    {
        int links = chain != null ? chain.ActiveCount : 0;
        Vector2 center = transform.TransformPoint(new Vector2(0f, links * .5f));
        nearScreen = DungeonDecorView.Near(center, new Vector2(1f, 1f + links * .5f));
        if (!nearScreen) return;
        float dt = Time.deltaTime;
        clock += dt;
        kick = Mathf.MoveTowards(kick, 0f, dt * Mathf.Max(0f, landingKick) / kickRecovery);
        float scale = 1f + kick;
        float w = 2f * Mathf.PI / Mathf.Max(.1f, swayPeriod);
        if (hook != null) hook.localRotation = Quaternion.Euler(0f, 0f, hookSwingDegrees * scale * Mathf.Sin(clock * w));
        if (cargo != null) cargo.localRotation = Quaternion.Euler(0f, 0f, cargoTiltDegrees * scale * Mathf.Sin(clock * w - .6f));
        if (chain != null) chain.SwayScale = scale;
    }

    private void FixedUpdate()
    {
        if (!nearScreen || motor == null) return;
        Vector2 center = transform.TransformPoint(senseCenter);
        var lossy = transform.lossyScale;
        Vector2 size = new Vector2(Mathf.Abs(senseSize.x * lossy.x), Mathf.Abs(senseSize.y * lossy.y));
        int count;
        do
        {
            count = motor.OverlapAttackBox(center, size, transform.eulerAngles.z, hits, targetMask);
            if (count < hits.Length) break;
            System.Array.Resize(ref hits, hits.Length * 2);
        } while (true);

        bool present = false, settling = false;
        for (int i = 0; i < count; i++)
        {
            var player = hits[i] != null ? hits[i].GetComponentInParent<PlayerController>() : null;
            if (player == null) continue;
            present = true;
            settling |= player.Velocity.y <= maxRiseSpeed;
        }
        if (present && !occupied && settling && Time.time - lastLanding >= landingCooldown) Land();
        occupied = present;
    }

    private void Land()
    {
        lastLanding = Time.time;
        LandingCount++;
        kick = Mathf.Max(kick, landingKick);
        if (landingDust != null && landingDustCount > 0) landingDust.Emit(landingDustCount);
        if (sound != null) sound.PlayCue(creakCue);
    }
}
