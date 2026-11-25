using System;
using UnityEngine;

/// <summary>
/// 던전 텔레포트 시설(중계기). 템플릿 맵 층에 Wave 격자 Tool로 놓고(루트 WaveEnvironmentTool, 점유 6×4칸 — 3차), 생성기가 짝지은 것만 작동한다.
/// 짝이 없으면 꺼진 받침으로 선다. 그 방을 클리어하면 작동(사용자 결정 2026-11-24), 상호작용 키로 짝 방으로 이동한다.
/// 계약(DESIGN_V2): 아래 공개 API는 바꾸지 않는다. 표시·Animation·소리·상호작용은 영역 facilities가 이 클래스에서 채운다.
/// </summary>
/// <remarks>
/// - 모양(3차, 6×4): 맨 아래 칸 = 고체 받침대(6×1, Platform 레이어 — 바닥에서 한 칸 올라선 단, 앞 계단 띠·환기창), 위 세 칸 = 양옆 코일 기둥 + 위 아치 보 +
///   서로 반대로 도는 고리 두 개 + 아치 꼭대기 표시등. Player는 받침대 윗면(루트 y − StandOffsetY = −1)에 선다. 서는 높이는 StandOffsetY 하나로 모은다
///   (DungeonManager·편집기 검사·템플릿 도구는 이 상수를 읽는다).
/// - 상태 표시: Dormant(짝 없음) = 발광 거의 꺼짐·정지 / Locked(짝 있음·미클리어) = 흐린 파랑·정지·안테나 느린 점멸 /
///   Ready = 고리 두 개가 반대로 돌고, 안테나 점멸, 바닥 문양 빛이 가운데로 흐르고, 코일 빛이 아래에서 위로 차오르고, 입자가 오르고, 웅웅거림(거리 감쇠 루프 — 임시 LobbyCctvHum) /
///   Busy = 출발(PlayIntake): 고리 가속·빛 기둥·충전 입자·포털 열림 소리, 도착(PlayEject): 빛 기둥 번쩍임·파란 입자 폭발·도착 소리 후 원래 상태로.
/// - 상호작용(자식 'Interact' Layer 8 트리거의 FacilityInteractPoint): 받침 위에서 키를 누르면 DungeonFacilityHost.Current.TryBeginTeleport(this).
///   던전 밖이거나 거절되면 짧은 거절음. 이동 연출 자체는 TeleportPassageEffectSO가 한다(DungeonRulesSO.teleportPassage).
/// - 파랑은 중립 기능부(WAVE_ENVIRONMENT_DESIGN), 몸체는 흑연 + 흰 발광. 크기 늘이기 없음(회전·이동·켜고 끄기·밝기만).
/// </remarks>
[DisallowMultipleComponent]
public class DungeonTeleporter : WorldEntityBase, IDungeonPassagePoint, IDungeonRoomPowered, IFacilityInteractTarget, IFacilityHighlightSource
{
    public enum PadState { Dormant = 0, Locked = 1, Ready = 2, Busy = 3 }

    /// <summary>
    /// 루트에서 Player가 서는 받침 윗면까지 아래로 내려간 거리(받침 윗면 y = 루트 y − StandOffsetY). 서는 높이는 이 상수 하나로 모은다(3차 계약).
    /// 6×4 패드(기준점 = 격자 꼭짓점, 받침대 = 맨 아래 칸 y −2~−1)라 1이다(2차 4×3 패드는 0.5).
    /// </summary>
    public const float StandOffsetY = 1f;

    public PadState State { get; protected set; } = PadState.Dormant;
    /// <summary>이 시설이 속한 방·짝 정보(DungeonRoomStage가 세울 때 채운다). LinkedRoom &lt; 0이면 꺼진 받침.</summary>
    public int RoomId { get; set; } = -1;
    public int Pad { get; set; } = -1;
    public int LinkedRoom { get; set; } = -1;
    public int LinkedPad { get; set; } = -1;
    public bool IsLinked => LinkedRoom >= 0;

    /// <summary>패드 위 Player 몸통 중심(출발·도착 모두).</summary>
    public virtual Vector2 RecessPoint => (Vector2)transform.position + new Vector2(0f, -StandOffsetY + .755f);
    public virtual Vector2 StandPoint => RecessPoint;
    public virtual Vector2 Outward => Vector2.up;

    [Header("표시(생성 도구가 연결)")]
    [SerializeField, Tooltip("0번은 시계 방향, 1번은 반시계 방향으로 돈다.")] private Transform[] rings = Array.Empty<Transform>();
    [SerializeField, Min(0f)] private float idleRingSpeed = 45f;
    [SerializeField, Min(0f)] private float chargeRingSpeed = 780f;
    [SerializeField] private Renderer[] blueLights = Array.Empty<Renderer>();
    [SerializeField] private Renderer[] antennaLamps = Array.Empty<Renderer>();
    [SerializeField, Tooltip("바닥 문양 조각(바깥 → 가운데 순서, 좌우 짝).")] private Renderer[] glyphSegments = Array.Empty<Renderer>();
    [SerializeField] private Renderer[] beamColumn = Array.Empty<Renderer>();
    [SerializeField, Tooltip("코일 기둥의 빛 고리(아래 → 위 순서, 좌우 짝). 준비되면 아래에서 위로 차오른다.")] private Renderer[] coilSegments = Array.Empty<Renderer>();
    [SerializeField, Tooltip("아치 꼭대기 표시등·아치 보 발광.")] private Renderer[] archLights = Array.Empty<Renderer>();
    [SerializeField] private ParticleSystem idleMotes;
    [SerializeField] private ParticleSystem chargeParticles;
    [SerializeField] private ParticleSystem arrivalBurst;
    [SerializeField] private FacilityInteractPoint interactPoint;
    [SerializeField, Min(.5f), Tooltip("Player 발이 받침 위에 있다고 보는 가로 반폭.")] private float padHalfWidth = 2.4f;

    [Header("소리(임시: 기존 효과음)")]
    [SerializeField] private SoundOptions humLoop;
    [SerializeField] private SoundOptions intakeSound;
    [SerializeField] private SoundOptions ejectSound;
    [SerializeField] private SoundOptions powerOnSound;

    [Header("시간(초)")]
    [SerializeField, Min(.1f), Tooltip("출발 중(Busy) 표시를 유지하는 최대 시간. 연출이 끊겨 방이 바뀌지 않으면 원래 상태로 돌아온다.")] private float busyTimeout = 3f;
    [SerializeField, Min(.05f)] private float ejectDuration = .7f;
    [SerializeField, Min(.05f)] private float powerOnDuration = .6f;

    private bool powered;
    private bool cached;
    private bool live;
    private float spin0, spin1, spinRate;
    private float clock;
    private float busyUntil;
    private float charge;              // 0~1 출발 충전(빛 기둥·고리 가속)
    private float ejectTime = -1f;     // 도착 연출 경과(음수면 없음)
    private float powerOnTime = -1f;   // 켜짐 연출 경과
    private Quaternion[] ringHome = Array.Empty<Quaternion>();
    private FacilityLights blue, antenna, glyph, beam, coil, arch;
    private readonly FacilityLoopSound hum = new FacilityLoopSound();

    public FacilityInteractPoint InteractPoint => interactPoint;
    public bool FacilityReady => live && State == PadState.Ready;
    /// <summary>TryBeginTeleport를 요청한 횟수(검사용, 대여마다 0).</summary>
    public int TeleportRequests { get; private set; }
    /// <summary>도착 연출을 낸 횟수(검사용).</summary>
    public int Ejects { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        Cache();
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        ringHome = new Quaternion[rings.Length];
        for (int i = 0; i < rings.Length; i++) ringHome[i] = rings[i] != null ? rings[i].localRotation : Quaternion.identity;
        blue = new FacilityLights(blueLights);
        antenna = new FacilityLights(antennaLamps);
        glyph = new FacilityLights(glyphSegments);
        beam = new FacilityLights(beamColumn);
        coil = new FacilityLights(coilSegments);
        arch = new FacilityLights(archLights);
    }

    public virtual void SetRoomPowered(bool powered, bool instant)
    {
        Cache();
        bool was = this.powered && IsLinked;
        this.powered = powered;
        if (State != PadState.Busy) State = !IsLinked ? PadState.Dormant : powered ? PadState.Ready : PadState.Locked;
        if (!instant && powered && IsLinked && !was)
        {
            powerOnTime = 0f;
            FacilityFeedback.Play(powerOnSound, transform.position);
        }
        if (instant) powerOnTime = -1f;
        Apply(0f);
    }

    /// <summary>출발: 고리가 빨라지고 빛 기둥이 서며 충전 입자가 오른다(TeleportPassageEffectSO가 정렬 직후 부른다).</summary>
    public virtual void PlayIntake()
    {
        Cache();
        State = PadState.Busy;
        busyUntil = Time.time + busyTimeout;
        charge = Mathf.Max(charge, .01f);
        interactPoint?.HideHint();
        FacilityFeedback.Emit(chargeParticles, true);
        FacilityFeedback.Play(intakeSound, RecessPoint);
    }

    /// <summary>도착: 빛 기둥이 번쩍이고 파란 입자가 터진 뒤 원래 상태로(도착 방은 미클리어면 Locked).</summary>
    public virtual void PlayEject()
    {
        Cache();
        Ejects++;
        ejectTime = 0f;
        charge = 0f;
        FacilityFeedback.Emit(chargeParticles, false);
        FacilityFeedback.Burst(arrivalBurst, 36);
        FacilityFeedback.Play(ejectSound, RecessPoint);
        spinRate = chargeRingSpeed * .6f;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        RoomId = Pad = LinkedRoom = LinkedPad = -1;
        State = PadState.Dormant;
        Cache();
        live = true;
        powered = false;
        TeleportRequests = 0;
        Ejects = 0;
        spin0 = spin1 = spinRate = 0f;
        clock = 0f;
        charge = 0f;
        ejectTime = powerOnTime = -1f;
        for (int i = 0; i < rings.Length; i++) if (rings[i] != null) rings[i].localRotation = ringHome[i];
        StopParticles(true);
        Apply(0f);
    }

    public override void ForceReturnToPool()
    {
        Shutdown();
        base.ForceReturnToPool();
    }

    private void OnDisable() => Shutdown();

    private void Shutdown()
    {
        live = false;
        hum.Stop();
        StopParticles(true);
        blue?.Restore();
        antenna?.Restore();
        glyph?.Restore();
        beam?.Restore();
        coil?.Restore();
        arch?.Restore();
    }

    private void StopParticles(bool clear)
    {
        var mode = clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting;
        if (idleMotes != null) idleMotes.Stop(true, mode);
        if (chargeParticles != null) chargeParticles.Stop(true, mode);
        if (arrivalBurst != null && clear) arrivalBurst.Stop(true, mode);
    }

    // ── 상호작용 ─────────────────────────────

    /// <summary>Player 발이 받침 윗면 위에 있는지.</summary>
    public bool OnPad(PlayerController player)
    {
        if (player == null) return false;
        Vector2 feet = FacilityAccess.Feet(player);
        float top = transform.position.y - StandOffsetY;
        return Mathf.Abs(feet.x - transform.position.x) <= padHalfWidth && feet.y >= top - .2f && feet.y <= top + .35f;
    }

    public bool ShowFacilityHint(PlayerController player, FacilityInteractPoint point)
        => live && State == PadState.Ready && FacilityAccess.InDungeon && OnPad(player) && player.IsGrounded && FacilityAccess.CanUse(player);

    public void FacilityInteract(PlayerController player, FacilityInteractPoint point)
    {
        if (!TryRequestTeleport(player)) point?.Reject();
    }

    /// <summary>짝 방으로 이동을 요청한다(상호작용 키와 같은 경로, 검사에서도 쓴다). 던전 밖이거나 거절되면 false.</summary>
    public bool TryRequestTeleport(PlayerController player)
    {
        if (!live || State != PadState.Ready || !OnPad(player) || !player.IsGrounded || !FacilityAccess.CanUse(player)) return false;
        var host = DungeonFacilityHost.Current;
        if (host == null) return false;
        TeleportRequests++;
        return host.TryBeginTeleport(this);
    }

    // ── 표시 ─────────────────────────────

    private void Update()
    {
        if (!live) return;
        float dt = Time.deltaTime;
        clock += dt;
        if (State == PadState.Busy && Time.time > busyUntil && ejectTime < 0f)
        {
            // 연출이 방을 바꾸지 못하고 끝났다 → 원래 상태로.
            State = !IsLinked ? PadState.Dormant : powered ? PadState.Ready : PadState.Locked;
            charge = 0f;
            FacilityFeedback.Emit(chargeParticles, false);
        }
        if (State == PadState.Busy && ejectTime < 0f) charge = Mathf.MoveTowards(charge, 1f, dt / .6f);
        if (ejectTime >= 0f)
        {
            ejectTime += dt;
            if (ejectTime >= ejectDuration)
            {
                ejectTime = -1f;
                if (State == PadState.Busy) State = !IsLinked ? PadState.Dormant : powered ? PadState.Ready : PadState.Locked;
            }
        }
        if (powerOnTime >= 0f) { powerOnTime += dt; if (powerOnTime >= powerOnDuration) powerOnTime = -1f; }
        Apply(dt);
        hum.Update(humLoop, transform.position, State == PadState.Ready || State == PadState.Busy, State == PadState.Busy ? 1f : .75f);
        FacilityFeedback.Emit(idleMotes, State == PadState.Ready);
    }

    private void Apply(float dt)
    {
        float wantRate = State == PadState.Ready ? idleRingSpeed : State == PadState.Busy ? Mathf.Lerp(idleRingSpeed, chargeRingSpeed, charge * charge) : 0f;
        if (ejectTime >= 0f) wantRate = Mathf.Lerp(chargeRingSpeed * .6f, idleRingSpeed, Mathf.Clamp01(ejectTime / ejectDuration));
        spinRate = dt > 0f ? Mathf.MoveTowards(spinRate, wantRate, dt * 900f) : wantRate;
        spin0 -= spinRate * dt;
        spin1 += spinRate * .7f * dt;
        for (int i = 0; i < rings.Length; i++)
            if (rings[i] != null) rings[i].localRotation = ringHome[i] * Quaternion.Euler(0f, 0f, i % 2 == 0 ? spin0 : spin1);

        // 켜짐 연출: 0.6초 동안 밝기가 계단식으로 올라온다(표시등 차례 점등).
        float boot = powerOnTime >= 0f ? Mathf.Clamp01(powerOnTime / powerOnDuration) : 1f;
        float baseLevel;
        switch (State)
        {
            case PadState.Ready: baseLevel = (.82f + .18f * Mathf.Sin(clock * 2.6f)) * Mathf.Floor(boot * 4f + 1f) / 5f; break;
            case PadState.Busy: baseLevel = 1f + charge * .8f; break;
            case PadState.Locked: baseLevel = .28f; break;
            default: baseLevel = .08f; break;
        }
        if (ejectTime >= 0f) baseLevel = Mathf.Max(baseLevel, 1.8f * (1f - ejectTime / ejectDuration));
        blue?.Set(baseLevel);

        if (antenna != null)
        {
            float period = State == PadState.Ready ? .9f : State == PadState.Busy ? .18f : 2.4f;
            bool on = Mathf.Repeat(clock, period) < period * (State == PadState.Locked ? .2f : .5f);
            float level = State == PadState.Dormant ? 0f : on ? (State == PadState.Locked ? .45f : 1.2f) : .12f;
            antenna.Set(level);
        }

        if (glyph != null && glyph.Count > 0)
        {
            // 좌우 짝 조각(바깥 → 가운데)을 따라 빛 덩어리가 가운데로 흐른다.
            int pairs = Mathf.Max(1, glyph.Count / 2);
            float speed = State == PadState.Busy ? 9f : 2.6f;
            float head = Mathf.Repeat(clock * speed, pairs + 1.5f);
            for (int i = 0; i < glyph.Count; i++)
            {
                int step = i / 2;
                float k = Mathf.Clamp01(1f - Mathf.Abs(step - head) / 1.2f);
                float level = State == PadState.Ready || State == PadState.Busy ? Mathf.Lerp(.2f, 1.3f, k * k * (3f - 2f * k)) : State == PadState.Locked ? .1f : 0f;
                glyph.SetOne(i, level);
            }
        }

        if (coil != null && coil.Count > 0)
        {
            // 코일: 좌우 짝(아래 → 위)이 아래부터 차오르며 위로 흐른다. 출발 충전 중에는 빠르게, 잠김이면 흐리게 멈춘다.
            int steps = Mathf.Max(1, coil.Count / 2);
            float rate = State == PadState.Busy ? 10f : 3f;
            float head = Mathf.Repeat(clock * rate, steps + 1.5f);
            for (int i = 0; i < coil.Count; i++)
            {
                int step = i / 2;
                float k = Mathf.Clamp01(1f - Mathf.Abs(step - head) / 1.4f);
                float level = State == PadState.Ready || State == PadState.Busy ? Mathf.Lerp(.25f, 1.35f, k) : State == PadState.Locked ? .12f : 0f;
                if (ejectTime >= 0f) level = Mathf.Max(level, 1.5f * (1f - ejectTime / ejectDuration));
                coil.SetOne(i, level);
            }
        }
        arch?.Set(State == PadState.Dormant ? .06f : State == PadState.Locked ? .3f : baseLevel * .9f);

        float column = State == PadState.Busy ? charge * charge * 1.2f : 0f;
        if (ejectTime >= 0f) column = 1.4f * (1f - ejectTime / ejectDuration);
        beam?.Set(column);
    }
}
