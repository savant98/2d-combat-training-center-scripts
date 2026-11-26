using System;
using System.Collections;
using UnityEngine;

// 수리 정비대(N08, 회복 — 초록, 3차 외형 재디자인·기능 그대로). 방을 클리어한 뒤 앞에 서서 상호작용 키를 누르면 최대 체력의 30%를 회복한다(던전 한 층에 한 번).
// - 사용 횟수: 던전 안이면 DungeonFacilityHost.TryUseFloorToken(TokenKey)(층 단위, 같은 층의 다른 수리대와 공유). 던전 밖이면 개체(대여)마다 한 번.
//   체력이 가득하면 쓰지 않고 거절한다(기회를 날리지 않게). 6차 T2(D4): 체력이 가득하면 키 안내도 숨긴다 — 같은 ↑는 거절음 대신 근처 크레인 붙기가 받는다
//   (FacilityInteractPoint.Reject의 붙기 양보). 수리 중(Repairing 1.1초)에는 IFacilityBusy로 알려 크레인 직선·붙기를 막는다.
// - 회복: HealthBase.Heal(PlayerHealth가 체력 갱신·초록 숫자·본체 초록 번쩍임을 낸다) + 회복 실행 효과(BurstHeal) 임시 재사용.
// - 외형: 위 갠트리 보에 매달린 정비 팔 2개(왼쪽 = 용접 토치, 오른쪽 = 렌치), 오른쪽 기둥의 녹색 십자 + 렌치 표지판, 갠트리를 오가는 스캔 헤드(초록 스캔 빔),
//   받침의 초록 진단 고리. 초록은 회복 기능부(표지판·스캔·진단 고리·회복 입자)에만, 몸체는 흑연 + 흰 발광.
// - 상태 표시: 꺼짐(미클리어) = 초록 낮게·팔 접힘 / 준비 = 표지판 맥동·진단 고리 회전·팔이 살짝 흔들림·준비음 /
//   수리 중 = 팔이 내려와(이동) 손목이 Player 쪽으로 꺾이고(회전) 토치 끝이 번쩍이며 불꽃 입자, 렌치는 톱니처럼 끊어 돌고, 스캔 헤드가 좌우로 훑으며
//   초록 빔, 발치에서 녹색 회복 입자 / 방전(사용 뒤) = 발광 꺼짐·팔 접힘·상태등만 흐리게. 크기 늘이기 없음(이동·회전·밝기만).
[DisallowMultipleComponent]
public sealed class PropRepairStation : MonoBehaviour, IDungeonPropPart, IDungeonRoomPowered, IFacilityInteractTarget, IFacilityHighlightSource, IFacilityBusy
{
    public const string TokenKey = "DungeonRepairStation";

    public enum State { Dormant, Ready, Repairing, Spent }

    [Header("회복")]
    [SerializeField, Range(0f, 1f), Tooltip("최대 체력에 대한 회복 비율.")] private float healFraction = .3f;
    [SerializeField, Min(.1f)] private float repairDuration = 1.1f;
    [SerializeField, Tooltip("회복을 넣는 순간(수리 시작부터 초).")] private float healMoment = .55f;

    [Header("구성(생성 도구가 연결)")]
    [SerializeField, Tooltip("받침의 진단 고리(회전).")] private Transform ring;
    [SerializeField] private float ringSpeed = 70f;
    [SerializeField, Tooltip("정비 팔(0 = 용접 토치, 1 = 렌치). 수리 때 아래로 내려온다.")] private Transform[] arms = Array.Empty<Transform>();
    [SerializeField] private Vector3 armLowered = new Vector3(0f, -.3f, 0f);
    [SerializeField, Tooltip("팔 끝 손목(arms와 같은 순서). 수리 때 Player 쪽으로 꺾인다.")] private Transform[] wrists = Array.Empty<Transform>();
    [SerializeField, Tooltip("손목이 꺾이는 각도(arms와 같은 순서, 도).")] private float[] wristAngles = Array.Empty<float>();
    [SerializeField, Tooltip("렌치 머리(수리 때 톱니처럼 끊어 돈다).")] private Transform wrench;
    [SerializeField, Tooltip("갠트리를 오가는 스캔 헤드.")] private Transform scanArm;
    [SerializeField, Tooltip("스캔 헤드가 훑는 가로 폭(칸, 한쪽).")] private float scanSweep = .45f;
    [SerializeField, Tooltip("녹색 표지판·방출선 등 회복 발광.")] private Renderer[] greenLights = Array.Empty<Renderer>();
    [SerializeField] private Renderer[] scanBeam = Array.Empty<Renderer>();
    [SerializeField] private Renderer[] statusLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("용접 토치 끝(수리 때 번쩍임).")] private Renderer[] torchTip = Array.Empty<Renderer>();
    [SerializeField] private ParticleSystem repairParticles;
    [SerializeField, Tooltip("용접 불꽃(수리 때).")] private ParticleSystem sparks;
    [SerializeField] private FacilityInteractPoint interactPoint;

    [Header("효과(임시: 기존 효과)")]
    [SerializeField] private FeedbackDataSO healFeedback;
    [SerializeField] private SoundOptions readySound;
    [SerializeField] private SoundOptions idleLoop;
    [SerializeField, Tooltip("수리를 시작할 때 팔이 내려오며 나는 소리(볼트 조임).")] private SoundOptions workSound;

    private bool cached;
    private bool isLive;
    private bool powered = true;
    private bool usedHere;
    private float power = 1f;
    private float spin;
    private float clock;
    private float arm;
    private float repairTime;
    private State state;
    private Coroutine routine;
    // 표식을 쓰고 아직 회복하지 못한 Player. 풀 반환·비활성으로 코루틴이 멈추면 finally가 돌지 않으므로 여기서 마저 회복한다.
    private PlayerController pendingHeal;
    private Vector3 scanHome;
    private Vector3[] armHome = Array.Empty<Vector3>();
    private Quaternion[] wristHome = Array.Empty<Quaternion>();
    private Quaternion ringHome, wrenchHome;
    private FacilityLights green, beam, status, torch;
    private readonly FacilityLoopSound loop = new FacilityLoopSound();

    public State CurrentState => state;
    public float HealFraction => healFraction;
    /// <summary>회복을 넣은 횟수(검사용, 대여마다 0).</summary>
    public int Heals { get; private set; }
    public FacilityInteractPoint InteractPoint => interactPoint;
    public bool FacilityReady => isLive && state == State.Ready && powered;
    /// <summary>정비 팔이 내려온 정도(0 접힘 ~ 1 내려옴, 검사용).</summary>
    public float ArmExtension => arm;
    /// <summary>6차 T2: 수리 중인지(IFacilityBusy — 그동안 크레인 직선·붙기를 막는다).</summary>
    public bool FacilityBusy => isLive && state == State.Repairing;

    private void Awake() => Cache();

    private void Cache()
    {
        if (cached) return;
        cached = true;
        if (scanArm != null) scanHome = scanArm.localPosition;
        if (ring != null) ringHome = ring.localRotation;
        if (wrench != null) wrenchHome = wrench.localRotation;
        armHome = new Vector3[arms.Length];
        for (int i = 0; i < arms.Length; i++) armHome[i] = arms[i] != null ? arms[i].localPosition : Vector3.zero;
        wristHome = new Quaternion[wrists.Length];
        for (int i = 0; i < wrists.Length; i++) wristHome[i] = wrists[i] != null ? wrists[i].localRotation : Quaternion.identity;
        green = new FacilityLights(greenLights);
        beam = new FacilityLights(scanBeam);
        status = new FacilityLights(statusLights);
        torch = new FacilityLights(torchTip);
    }

    private void OnDisable()
    {
        SettlePendingHeal();
        if (routine != null) { StopCoroutine(routine); routine = null; }
        if (state == State.Repairing) state = State.Spent;
        FacilityFeedback.Emit(sparks, false);
        loop.Stop();
    }

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        isLive = true;
        powered = true;
        power = 1f;
        usedHere = false;
        Heals = 0;
        spin = clock = arm = repairTime = 0f;
        if (ring != null) ring.localRotation = ringHome;
        if (wrench != null) wrench.localRotation = wrenchHome;
        if (repairParticles != null) repairParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        state = FacilityAccess.IsFloorTokenUsed(TokenKey) ? State.Spent : State.Ready;
        Apply();
    }

    public void OnPropReturning(DungeonProp prop)
    {
        SettlePendingHeal();
        if (routine != null) { StopCoroutine(routine); routine = null; }
        isLive = false;
        loop.Stop();
        green?.Restore();
        beam?.Restore();
        status?.Restore();
        torch?.Restore();
    }

    public void SetRoomPowered(bool on, bool instant)
    {
        Cache();
        bool was = powered;
        powered = on;
        if (instant) power = on ? 1f : 0f;
        if (state == State.Spent || state == State.Repairing) return;
        state = on ? State.Ready : State.Dormant;
        if (on && !was && !instant) FacilityFeedback.Play(readySound, transform.position);
        Apply();
    }

    // ── 상호작용 ─────────────────────────────

    // 6차 T2(D4): 체력이 가득하면 안내를 띄우지 않는다(쓰지 않는 시설이 크레인 붙기를 가로막지 않게).
    public bool ShowFacilityHint(PlayerController player, FacilityInteractPoint point)
        => isLive && state == State.Ready && powered && FacilityAccess.CanUse(player) && player.IsGrounded && NeedsRepair(player);

    private static bool NeedsRepair(PlayerController player)
    {
        var health = player != null ? player.Health : null;
        return health != null && health.currentHP < health.maxHP - .01f;
    }

    public void FacilityInteract(PlayerController player, FacilityInteractPoint point)
    {
        if (!TryRepair(player)) point?.Reject();
    }

    /// <summary>회복을 시작한다(상호작용 키와 같은 경로, 검사에서도 쓴다). 쓰지 못하면 false.</summary>
    public bool TryRepair(PlayerController player)
    {
        Cache();
        if (!isLive || state != State.Ready || !powered || !isActiveAndEnabled) return false;
        if (!FacilityAccess.CanUse(player) || player.Health == null) return false;
        if (!NeedsRepair(player)) return false;                               // 가득하면 기회를 쓰지 않는다
        if (usedHere) { state = State.Spent; Apply(); return false; }
        var host = DungeonFacilityHost.Current;
        if (host != null && !host.TryUseFloorToken(TokenKey))
        {
            // 같은 층에서 이미 썼다(다른 수리대 포함) → 방전으로 보인다.
            state = State.Spent;
            Apply();
            return false;
        }
        usedHere = true;
        interactPoint?.HideHint();
        state = State.Repairing;
        repairTime = 0f;
        pendingHeal = player;
        FacilityFeedback.Play(workSound, transform.position);
        routine = StartCoroutine(RepairRoutine(player));
        return true;
    }

    private IEnumerator RepairRoutine(PlayerController player)
    {
        bool healed = false;
        try
        {
            FacilityFeedback.Emit(repairParticles, true);
            for (float t = 0f; t < repairDuration; t += Time.deltaTime)
            {
                // 팔이 다 내려온 뒤부터 토치 불꽃을 튀긴다.
                FacilityFeedback.Emit(sparks, arm > .8f);
                if (!healed && t >= healMoment) { healed = true; pendingHeal = null; Heal(player); }
                yield return null;
            }
        }
        finally
        {
            // 끊겨도 이미 쓴 기회는 회복으로 돌려준다(풀 반환·비활성은 SettlePendingHeal이 먼저 처리한다).
            if (!healed) SettlePendingHeal();
            routine = null;
            FacilityFeedback.Emit(repairParticles, false);
            FacilityFeedback.Emit(sparks, false);
            state = State.Spent;
            Apply();
        }
    }

    // 표식을 쓴 뒤 회복 전에 끊기면 그 자리에서 회복한다(관 통과·Wave 종료로 방이 거둬지는 경우).
    private void SettlePendingHeal()
    {
        var player = pendingHeal;
        pendingHeal = null;
        if (player != null) Heal(player);
    }

    private void Heal(PlayerController player)
    {
        if (player == null || player.Health == null || player.Health.IsDead) return;
        float amount = Mathf.Max(1f, player.Health.maxHP * healFraction);
        player.Health.Heal(new WeaponBaseSO.DamageInfo { damage = amount, damageType = DamageType.Normal });
        Heals++;
        if (healFeedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.PlayExact(healFeedback, player.transform.position, Quaternion.identity);
    }

    // ── 표시 ─────────────────────────────

    private void Update()
    {
        if (!isLive) return;
        float dt = Time.deltaTime;
        clock += dt;
        if (state == State.Repairing) repairTime += dt;
        power = Mathf.MoveTowards(power, powered ? 1f : 0f, dt / .4f);
        float ringRate = state == State.Ready ? ringSpeed : state == State.Repairing ? ringSpeed * 4f : 0f;
        spin += ringRate * power * dt;
        arm = Mathf.MoveTowards(arm, state == State.Repairing ? 1f : 0f, dt / .22f);
        loop.Update(idleLoop, transform.position, state == State.Ready && power > .5f, .8f);
        Apply();
    }

    private void Apply()
    {
        if (ring != null) ring.localRotation = ringHome * Quaternion.Euler(0f, 0f, -spin);
        float e = arm * arm * (3f - 2f * arm);
        // 대기 중에는 팔이 살짝 흔들린다(준비 상태일 때만 — 방전·꺼짐이면 멈춘다).
        float idleSway = state == State.Ready ? Mathf.Sin(clock * 1.6f) * 2.5f * power : 0f;
        for (int i = 0; i < arms.Length; i++)
        {
            if (arms[i] == null) continue;
            arms[i].localPosition = armHome[i] + armLowered * e * (i == 0 ? 1f : .85f);
        }
        for (int i = 0; i < wrists.Length; i++)
        {
            if (wrists[i] == null) continue;
            float bend = i < wristAngles.Length ? wristAngles[i] : 0f;
            // 수리 중 토치(0)는 조금씩 흔들며 용접한다.
            float work = state == State.Repairing && i == 0 ? Mathf.Sin(repairTime * 22f) * 6f * e : 0f;
            wrists[i].localRotation = wristHome[i] * Quaternion.Euler(0f, 0f, bend * e + work + idleSway * (i == 0 ? 1f : -1f));
        }
        if (wrench != null)
        {
            // 렌치는 0.12초마다 30도씩 끊어 돈다(래칫).
            float steps = state == State.Repairing ? Mathf.Floor(repairTime / .12f) : 0f;
            wrench.localRotation = wrenchHome * Quaternion.Euler(0f, 0f, -30f * steps);
        }
        if (scanArm != null)
            scanArm.localPosition = scanHome + new Vector3(state == State.Repairing ? Mathf.Sin(repairTime * 7f) * scanSweep * e : 0f, 0f, 0f);
        float pulse = .8f + .2f * Mathf.Sin(clock * 3.2f);
        switch (state)
        {
            case State.Ready: green?.Set(Mathf.Lerp(.15f, 1f, power) * pulse); status?.Set(power); break;
            case State.Repairing: green?.Set(1.4f); status?.Set(1f); break;
            case State.Spent: green?.Set(0f); status?.Set(.18f); break;
            default: green?.Set(.15f); status?.Set(.25f); break;
        }
        beam?.Set(state == State.Repairing ? (Mathf.Repeat(clock, .16f) < .1f ? 1.2f : .7f) * e : 0f);
        torch?.Set(state == State.Repairing && e > .8f ? (Mathf.Repeat(clock, .09f) < .05f ? 1.6f : .6f) : 0f);
    }
}
