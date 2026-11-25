using System;
using UnityEngine;

// 폐자재 더미(N21, 고체 발판 3×2). 계단처럼 밟고 오르는 폐기장 발판이다. 고체 판정(SolidMount)은 셀 경계에 맞춘 상자 하나다.
// - Player가 윗면에 내려앉으면 위에 얹힌 조각들이 흔들리고(회전만, 줄어드는 진동), 먼지가 일고 작은 조각이 굴러 떨어진다(입자).
//   소리(임시 기존 효과음): 착지 SubImpactDrop, 굴러 떨어지는 조각 MetalRicochetTick(조금 늦게).
// - 착지 감지는 루트 RaycastMotor2D.OverlapAttackBox(Player)로 윗면 위 얇은 띠를 묻고, Player 몸 아랫변이 윗면 높이에 있을 때만 '올라섬'으로 본다.
//   올라서 있지 않다가 올라선 순간이 착지다(쿨다운 안에서는 한 번). 물리 이동은 하지 않는다.
[DisallowMultipleComponent]
public sealed class PropScrapHeap : MonoBehaviour, IDungeonPropPart
{
    [Header("감지")]
    [SerializeField, Tooltip("윗면 높이(루트 기준).")] private float topLocal = 1f;
    [SerializeField, Min(.5f), Tooltip("윗면 폭(칸).")] private float topWidth = 3f;
    [SerializeField, Min(.01f), Tooltip("발 아랫변과 윗면의 높이 차 허용(칸).")] private float standTolerance = .12f;
    [SerializeField, Min(0f)] private float cooldown = .35f;
    [SerializeField] private LayerMask playerMask;

    [Header("흔들림")]
    [SerializeField] private Transform[] loosePieces = Array.Empty<Transform>();
    [SerializeField, Tooltip("조각마다 최대 흔들림 각도(도).")] private float wobbleAngle = 7f;
    [SerializeField, Min(.05f), Tooltip("흔들림이 줄어드는 시간(초).")] private float wobbleDecay = .32f;
    [SerializeField, Min(.03f), Tooltip("한 번 흔들리는 주기(초).")] private float wobblePeriod = .16f;

    [Header("효과")]
    [SerializeField] private ParticleSystem dust;
    [SerializeField] private ParticleSystem pebbles;
    [SerializeField] private SoundOptions landSound;
    [SerializeField] private SoundOptions tickSound;
    [SerializeField, Min(0f)] private float tickDelay = .14f;

    private Collider2D[] hits = new Collider2D[4];
    private RaycastMotor2D motor;
    private Quaternion[] pieceHome = Array.Empty<Quaternion>();
    private float[] pieceWeight = Array.Empty<float>();
    private float wobbleStart = -10f;
    private float lastLanding = -10f;
    private float pendingTick = -1f;
    private bool onTop;
    private bool cached;
    private bool live;

    /// <summary>이번 대여에서 감지한 착지 수(검사용).</summary>
    public int Landings { get; private set; }
    public bool PlayerOnTop => onTop;
    public float TopWorldY => transform.TransformPoint(new Vector3(0f, topLocal, 0f)).y;

    private void Awake()
    {
        Cache();
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();   // 정적 공격 질의만 쓴다
        if (playerMask.value == 0) playerMask = LayerMask.GetMask("Player");
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        pieceHome = new Quaternion[loosePieces.Length];
        pieceWeight = new float[loosePieces.Length];
        for (int i = 0; i < loosePieces.Length; i++)
        {
            pieceHome[i] = loosePieces[i] != null ? loosePieces[i].localRotation : Quaternion.identity;
            pieceWeight[i] = (i % 2 == 0 ? 1f : -1f) * (1f - .22f * (i % 3));
        }
    }

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        live = true;
        onTop = false;
        Landings = 0;
        wobbleStart = lastLanding = -10f;
        pendingTick = -1f;
        for (int i = 0; i < loosePieces.Length; i++) if (loosePieces[i] != null) loosePieces[i].localRotation = pieceHome[i];
        PropHazardKit.Stop(dust, true);
        PropHazardKit.Stop(pebbles, true);
    }

    public void OnPropReturning(DungeonProp prop)
    {
        live = false;
        for (int i = 0; i < loosePieces.Length; i++) if (loosePieces[i] != null) loosePieces[i].localRotation = pieceHome[i];
    }

    private void FixedUpdate()
    {
        if (!live) return;
        bool now = PlayerStanding(out Vector2 feet);
        if (now && !onTop && Time.time - lastLanding >= cooldown) Land(feet);
        onTop = now;
    }

    private bool PlayerStanding(out Vector2 feet)
    {
        feet = default;
        // 윗면에 Player가 걸릴 때만 몸통을 잰다(더미마다 물리 틱마다 몸통 Collider를 훑지 않게 질의를 먼저 한다).
        float top = TopWorldY;
        Vector2 center = transform.TransformPoint(new Vector3(0f, topLocal + .2f, 0f));
        int count = PropHazardKit.OverlapBox(motor, center, new Vector2(Mathf.Max(.1f, topWidth - .1f), .4f), transform.eulerAngles.z, ref hits, playerMask);
        bool found = false;
        for (int i = 0; i < count; i++) { if (hits[i] != null && hits[i].GetComponentInParent<PlayerHealth>() != null) found = true; hits[i] = null; }
        if (!found) return false;
        if (!PropHazardKit.TryPlayerBody(out _, out var body)) return false;
        if (Mathf.Abs(body.min.y - top) > standTolerance) return false;
        feet = new Vector2(Mathf.Clamp(body.center.x, transform.position.x - topWidth * .5f, transform.position.x + topWidth * .5f), top);
        return true;
    }

    /// <summary>착지 반응을 지금 재생한다(감지 대신 검사·연출에서 부를 수 있다).</summary>
    public void Land(Vector2 feet)
    {
        Landings++;
        lastLanding = Time.time;
        wobbleStart = Time.time;
        PropHazardKit.PlayClip(landSound, feet, .7f);
        if (dust != null) { dust.transform.position = new Vector3(feet.x, feet.y, dust.transform.position.z); dust.Emit(8); }
        if (pebbles != null) { pebbles.transform.position = new Vector3(feet.x, feet.y + .05f, pebbles.transform.position.z); pebbles.Emit(UnityEngine.Random.Range(3, 6)); }
        pendingTick = Time.time + tickDelay;
    }

    private void Update()
    {
        if (!live) return;
        if (pendingTick > 0f && Time.time >= pendingTick)
        {
            pendingTick = -1f;
            PropHazardKit.PlayClip(tickSound, transform.position, .6f);
        }
        float t = Time.time - wobbleStart;
        if (t < 0f || t > wobbleDecay * 4f)
        {
            if (t <= wobbleDecay * 4f + .1f)
                for (int i = 0; i < loosePieces.Length; i++) if (loosePieces[i] != null) loosePieces[i].localRotation = pieceHome[i];
            return;
        }
        float envelope = Mathf.Exp(-t / wobbleDecay);
        for (int i = 0; i < loosePieces.Length; i++)
        {
            if (loosePieces[i] == null) continue;
            float angle = wobbleAngle * pieceWeight[i] * envelope * Mathf.Sin((t / wobblePeriod + i * .23f) * Mathf.PI * 2f);
            loosePieces[i].localRotation = pieceHome[i] * Quaternion.Euler(0f, 0f, angle);
        }
    }

    /// <summary>지금 흔들리는 중인지(검사용).</summary>
    public bool IsWobbling => Time.time - wobbleStart <= wobbleDecay * 4f;
}
