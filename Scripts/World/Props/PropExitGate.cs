using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 던전 시작 방 출구 문(IDungeonExitPoint, DESIGN_V4 §2.6 R11). footprint 5×6, 뒤 벽 문(DungeonProp 루트 + 이 부품, 충돌 없음).
// - 템플릿의 출구 자리 표식(DungeonExitSlot 5×6)에 목표 SO의 ExitPrefab으로 세운다. 세운 쪽이 RoomId를 채우고 진행을 밀어 넣는다
//   (SetProgress(done, total, unlocked, instant) — Prop은 매니저 이벤트를 구독하지 않는다).
// - 레버 수 표시 = 잠금 볼트: 문짝을 가로지르는 볼트가 레버 수(최대 6, 남는 것은 숨김)만큼, 볼트마다 기둥에 앰버 램프, 상인방에 같은 수의 램프 줄('n/3', 글자 없이 Mesh).
//   잠김 = 볼트 끼움·램프 흐림, 풀림 = 볼트가 기둥 쪽으로 빠져 기둥 뒤로 숨고 램프가 켜진다.
// - 해금 연출(instant가 아니면 '아직 보이지 않은 풀림'만): 잠깐 뒤(revealDelay) 0.25초 간격으로 볼트가 빠짐(볼트마다 임펄스) → 모두 풀렸으면
//   봉인 원판 한 바퀴 회전 0.5초 → 문짝 두 장이 좌우로 미끄러져 열림 0.6초(이동만) → 문 안 유도등 → 흔들림. 열린 뒤에는 FacilityHighlight(앰버)가 강조한다.
// - 입장: 상호작용 키 → IDungeonExitHost.TryEnterExit(this). 받아들이면 문간으로 흡입(이 문 루트의 자체 WavePlayerStart.HoldForPassage(true) — 보호 구속,
//   물리 틱마다 RaycastMotor2D.ShiftConfinement 0.35초, 위치 직접 대입 없음). 구속은 Wave 종료가 푼다: WaveManager.CompleteWave는 자기 holder만
//   ReleasePlayer로 풀므로 이 문은 같은 순간 올라오는 WaveEnded(또는 Wave 멈춤·패배)를 받아 자기 holder를 푼다. 시설이 꺼지면(풀 반환) WavePlayerStart.OnDisable이
//   스스로 풀고, 한도(holdLimit)를 넘거나 Player가 사라져도 푼다(실패 시 해제). Wave 진행 장치가 없으면(Lobby 시험) 흡입 뒤 noWaveRelease초에 푼다.
//   흡입 동안만 SetFacilityBusy(this, true) — 던전 클리어 대기(FloorClearRoutine)가 흡입이 끝나기를 기다리게 한다.
// - 크기 늘이기 없음(회전·이동·밝기만 — 기계형 규칙). 물리 질의 없음. 소리는 기존 효과음(LobbyUpgradeAssembleBolt·LobbyUpgradeAssembleLock·UpgradeUnlock) 재사용.
[DisallowMultipleComponent]
public sealed class PropExitGate : MonoBehaviour, IDungeonPropPart, IDungeonExitPoint, IFacilityInteractTarget, IFacilityHighlightSource
{
    public enum State { Locked, Unlocking, Open, Entering, Entered }
    public const int MaxBolts = 6;

    private enum Phase { Idle, Delay, Bolts, Seal, Leaves }

    [Header("구성(생성 도구가 연결)")]
    [SerializeField, Tooltip("잠금 볼트 막대 6개(아래부터). 원점 = 막대 가운데, 잠김 자리는 문짝 이음매(x = 0).")] private Transform[] bolts = Array.Empty<Transform>();
    [SerializeField, Tooltip("볼트마다 기둥에 붙은 램프 자리(bolts와 같은 순서).")] private Transform[] boltLampMounts = Array.Empty<Transform>();
    [SerializeField] private Renderer[] boltLamps = Array.Empty<Renderer>();
    [SerializeField, Tooltip("상인방 램프 줄 자리(6개).")] private Transform[] rowLampMounts = Array.Empty<Transform>();
    [SerializeField] private Renderer[] rowLamps = Array.Empty<Renderer>();
    [SerializeField] private Transform leafLeft;
    [SerializeField] private Transform leafRight;
    [SerializeField, Tooltip("봉인 원판 반쪽(각 문짝의 자식, 원점 = 원판 중심 — 함께 돌면 원판 하나로 보인다).")] private Transform sealLeft;
    [SerializeField] private Transform sealRight;
    [SerializeField] private Renderer[] sealLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("문 안 유도등(바닥 → 양옆 위 순서로 흐른다).")] private Renderer[] guideLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("문 안 은은한 빛(열린 뒤·입장 때 밝아짐).")] private Renderer[] innerGlow = Array.Empty<Renderer>();
    [SerializeField, Tooltip("상인방 위 출구 표지(글자 없는 문·화살 그림).")] private Renderer[] signLights = Array.Empty<Renderer>();
    [SerializeField] private Renderer[] trimLights = Array.Empty<Renderer>();
    [SerializeField] private ParticleSystem sealSparks;
    [SerializeField] private ParticleSystem leafDust;
    [SerializeField] private ParticleSystem openBurst;
    [SerializeField] private FacilityInteractPoint interactPoint;

    [Header("배치(루트 기준 칸)")]
    [SerializeField, Tooltip("볼트 막대 가운데 높이 범위(아래·위). 보이는 볼트 수만큼 고르게 나눈다.")] private Vector2 boltRange = new Vector2(-2.55f, -.35f);
    [SerializeField, Min(0f), Tooltip("볼트가 기둥 쪽으로 빠지는 거리(짝수 번째는 왼쪽, 홀수 번째는 오른쪽).")] private float boltTravel = 1.9f;
    [SerializeField, Tooltip("볼트 램프 x(기둥 가운데)와 볼트보다 높은 정도.")] private Vector2 boltLamp = new Vector2(1.9f, .2f);
    [SerializeField, Min(.1f), Tooltip("상인방 램프 사이 간격.")] private float rowSpacing = .5f;
    [SerializeField, Min(0f), Tooltip("문짝이 좌우로 미끄러지는 거리.")] private float leafTravel = 1.2f;
    [SerializeField, Min(0), Tooltip("진행을 받기 전 기본 볼트 수(격자 Tool 미리보기와 같게).")] private int defaultTotal = 3;
    [SerializeField, Tooltip("목표 UI 마커를 둘 자리(루트 기준, 상인방 위).")] private Vector2 markerOffset = new Vector2(0f, 3.3f);

    [Header("해금 연출 시간(초)")]
    [SerializeField, Min(0f), Tooltip("진행을 받은 뒤 첫 볼트가 빠지기까지(방에 들어온 Player가 문을 볼 틈).")] private float revealDelay = .45f;
    [SerializeField, Min(.05f)] private float boltInterval = .25f;
    [SerializeField, Min(.05f), Tooltip("볼트 하나가 빠지는 시간.")] private float boltSlide = .2f;
    [SerializeField, Min(.05f)] private float sealDuration = .5f;
    [SerializeField, Tooltip("봉인 원판 회전(도, 한 바퀴 = 반쪽 둘이 제자리로 돌아와 이음매에서 갈라진다).")] private float sealTurn = -360f;
    [SerializeField, Min(.05f)] private float leafDuration = .6f;

    [Header("흔들림(기존 FeedbackPlayer 경로, 요청 세기 — 실효 = × 합산 배율 0.35)")]
    [SerializeField, Min(0f)] private float boltShake = .4f;
    [SerializeField, Min(0f)] private float openShake = .9f;
    [SerializeField, Min(0f)] private float openShakeTime = .25f;

    [Header("입장")]
    [SerializeField, Min(.05f), Tooltip("문간으로 빨려 들어가는 시간(초).")] private float intakeDuration = .35f;
    [SerializeField, Min(0f), Tooltip("Wave 진행 장치가 없을 때(Lobby 시험) 흡입 뒤 구속을 푸는 시간(초).")] private float noWaveRelease = .6f;
    [SerializeField, Min(1f), Tooltip("Wave 종료를 기다리는 최대 시간(초). 넘으면 구속을 푼다(실패 시 해제).")] private float holdLimit = 20f;

    [Header("효과(기존 효과음 재사용)")]
    [SerializeField] private SoundOptions boltSound;
    [SerializeField] private SoundOptions sealSound;
    [SerializeField] private SoundOptions openSound;
    [SerializeField, Tooltip("문간으로 흡입될 때(구속 잠금 효과).")] private FeedbackDataSO enterFeedback;

    private bool cached;
    private bool isLive;
    private State state;
    private Phase phase;
    private int total, visible, targetDone, shownDone;
    private bool targetUnlocked;
    private readonly float[] boltOut = new float[MaxBolts];
    private readonly bool[] boltMoving = new bool[MaxBolts];
    private float phaseTime, nextBoltAt, lastBoltAt;
    private float sealT, leafT;
    private float clock, enterFlash = -1f;
    private Vector3 leafLeftHome, leafRightHome;
    private Quaternion sealLeftHome, sealRightHome;
    private Vector3[] boltHome = Array.Empty<Vector3>();
    private FacilityLights boltLampLights, rowLampLights, sealLamp, guideLamp, innerLamp, signLamp, trimLamp;
    private readonly List<(string label, float time)> phaseLog = new List<(string, float)>();
    private WavePlayerStart hold;
    private WaveManager holdWaves;
    private IDungeonFacilityHost busyHost;
    private Coroutine entering;

    public int RoomId { get; set; } = -1;
    public bool IsOpen => state == State.Open;
    public State CurrentState => state;
    public FacilityInteractPoint InteractPoint => interactPoint;
    /// <summary>목표 UI 마커 자리(월드, 상인방 위). 목표 대상 목록(Exit 종류)의 위치로 쓴다.</summary>
    public Vector3 MarkerPoint => transform.TransformPoint(markerOffset);
    /// <summary>키 안내가 지금 보이는지(UI 마커는 이때 숨긴다 — R3).</summary>
    public bool HintVisible => interactPoint != null && interactPoint.HintVisible;
    /// <summary>표시하는 레버 수(볼트 수, 최대 6).</summary>
    public int Total => total;
    public int VisibleBolts => visible;
    /// <summary>풀림을 보이기 시작한 볼트 수(지연 재생 기준 — 이보다 많은 진행만 새로 연출한다).</summary>
    public int ShownDone => shownDone;
    public float BoltOut(int index) => index >= 0 && index < MaxBolts ? boltOut[index] : 0f;
    public bool BoltVisible(int index) => index >= 0 && index < bolts.Length && bolts[index] != null && bolts[index].gameObject.activeSelf;
    /// <summary>봉인 원판 각도(도, 검사용).</summary>
    public float SealAngle => sealTurn * Ease(sealT);
    /// <summary>문짝 열림(0 닫힘 ~ 1 열림, 검사용).</summary>
    public float LeafOpen => Ease(leafT);
    public bool Sequencing => phase != Phase.Idle;
    /// <summary>연출 단계 기록('bolt0'·'seal'·'leaves'·'open', 시각 = Time.time). 대여마다 비운다(검사용).</summary>
    public IReadOnlyList<(string label, float time)> PhaseLog => phaseLog;
    public int Impulses { get; private set; }
    public float LastImpulse { get; private set; }
    /// <summary>호스트에 입장을 요청한 횟수(검사용).</summary>
    public int EnterRequests { get; private set; }
    /// <summary>입장이 받아들여졌는지(대여마다 false).</summary>
    public bool Entered => state == State.Entering || state == State.Entered;
    /// <summary>이 문이 지금 Player를 구속하고 있는지(검사용).</summary>
    public bool Holding => hold != null && hold.IsHoldingPlayer;
    public float RevealDelay => revealDelay;
    public float BoltInterval => boltInterval;
    public float BoltSlide => boltSlide;
    public float SealDuration => sealDuration;
    public float LeafDuration => leafDuration;
    public float IntakeDuration => intakeDuration;
    public float NoWaveRelease => noWaveRelease;
    public float BoltShake => boltShake;
    public float OpenShake => openShake;
    public bool FacilityReady => isLive && state == State.Open && HostReady;
    private static bool HostReady => DungeonFacilityHost.Current is IDungeonExitHost;

    private static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    private void Awake() => Cache();

    private void Cache()
    {
        if (cached) return;
        cached = true;
        if (leafLeft != null) leafLeftHome = leafLeft.localPosition;
        if (leafRight != null) leafRightHome = leafRight.localPosition;
        if (sealLeft != null) sealLeftHome = sealLeft.localRotation;
        if (sealRight != null) sealRightHome = sealRight.localRotation;
        boltHome = new Vector3[bolts.Length];
        for (int i = 0; i < bolts.Length; i++) boltHome[i] = bolts[i] != null ? bolts[i].localPosition : Vector3.zero;
        boltLampLights = new FacilityLights(boltLamps);
        rowLampLights = new FacilityLights(rowLamps);
        sealLamp = new FacilityLights(sealLights);
        guideLamp = new FacilityLights(guideLights);
        innerLamp = new FacilityLights(innerGlow);
        signLamp = new FacilityLights(signLights);
        trimLamp = new FacilityLights(trimLights);
    }

    private void OnDisable()
    {
        // 풀 반환·방 거둠: 진행 중인 연출은 끝난 모습으로, 흡입 중이면 구속·바쁨을 푼다(실패 시 해제). 코루틴은 비활성화로 이미 멈췄다.
        entering = null;
        if (phase != Phase.Idle) FinishSequence();
        ReleaseHold();
        EndBusy();
        if (state == State.Entering) state = State.Entered;
    }

    // ── 수명 ─────────────────────────────

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        isLive = true;
        RoomId = -1;
        state = State.Locked;
        phase = Phase.Idle;
        phaseLog.Clear();
        Impulses = 0;
        LastImpulse = 0f;
        EnterRequests = 0;
        clock = 0f;
        enterFlash = -1f;
        entering = null;
        targetUnlocked = false;
        if (leafDust != null) leafDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (sealSparks != null) sealSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (openBurst != null) openBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ApplyInstant(0, defaultTotal, false);
    }

    public void OnPropReturning(DungeonProp prop)
    {
        isLive = false;
        ReleaseHold();
        EndBusy();
        boltLampLights?.Restore();
        rowLampLights?.Restore();
        sealLamp?.Restore();
        guideLamp?.Restore();
        innerLamp?.Restore();
        signLamp?.Restore();
        trimLamp?.Restore();
    }

    // ── 진행(IDungeonExitPoint) ─────────────────────────────

    /// <summary>
    /// 진행을 밀어 넣는다(계약). total = 레버 수(볼트 수, 최대 6), done = 완료 수, unlocked = 출구 해금.
    /// instant면 그 모습으로 바로(방을 세울 때 '이미 보인' 진행), 아니면 아직 보이지 않은 풀림만 차례로 연출한다. 진행이 줄었으면 바로 맞춘다.
    /// </summary>
    public void SetProgress(int done, int total, bool unlocked, bool instant)
    {
        Cache();
        if (state == State.Entering || state == State.Entered) return;
        total = Mathf.Max(0, total);
        done = Mathf.Clamp(done, 0, total);
        if (instant || done < shownDone || Mathf.Min(total, MaxBolts) != visible || (!unlocked && state == State.Open))
        {
            ApplyInstant(done, total, unlocked);
            return;
        }
        this.total = total;
        targetDone = done;
        targetUnlocked = unlocked;
        if (phase == Phase.Idle && (shownDone < Mathf.Min(targetDone, visible) || (targetUnlocked && state != State.Open)))
        {
            phase = Phase.Delay;
            phaseTime = 0f;
            if (targetUnlocked) state = State.Unlocking;
        }
        else if (phase != Phase.Idle && targetUnlocked) state = State.Unlocking;
    }

    // 진행 모습을 연출 없이 적용한다(볼트 배치·풀림·봉인·문짝).
    private void ApplyInstant(int done, int newTotal, bool unlocked)
    {
        phase = Phase.Idle;
        total = Mathf.Max(0, newTotal);
        done = Mathf.Clamp(done, 0, total);
        targetDone = done;
        targetUnlocked = unlocked;
        Layout(Mathf.Min(total, MaxBolts));
        shownDone = Mathf.Min(done, visible);
        for (int i = 0; i < MaxBolts; i++) { boltOut[i] = i < shownDone ? 1f : 0f; boltMoving[i] = false; }
        sealT = leafT = unlocked ? 1f : 0f;
        state = unlocked ? State.Open : State.Locked;
        EmitAll(false);
        Apply();
    }

    // 보이는 볼트 수만큼 높이를 고르게 나누고 볼트·램프·상인방 램프를 배치한다(나머지는 숨김). 이동만.
    private void Layout(int count)
    {
        visible = Mathf.Clamp(count, 0, Mathf.Min(MaxBolts, bolts.Length));
        float span = boltRange.y - boltRange.x;
        for (int i = 0; i < bolts.Length; i++)
        {
            bool show = i < visible;
            float y = visible > 0 ? boltRange.x + (i + .5f) * span / visible : boltRange.x;
            if (bolts[i] != null)
            {
                bolts[i].gameObject.SetActive(show);
                boltHome[i] = new Vector3(0f, y, boltHome[i].z);
            }
            if (i < boltLampMounts.Length && boltLampMounts[i] != null)
            {
                boltLampMounts[i].gameObject.SetActive(show);
                float side = Side(i);
                var p = boltLampMounts[i].localPosition;
                boltLampMounts[i].localPosition = new Vector3(side * boltLamp.x, y + boltLamp.y, p.z);
            }
        }
        for (int i = 0; i < rowLampMounts.Length; i++)
        {
            if (rowLampMounts[i] == null) continue;
            bool show = i < visible;
            rowLampMounts[i].gameObject.SetActive(show);
            var p = rowLampMounts[i].localPosition;
            rowLampMounts[i].localPosition = new Vector3((i - (visible - 1) * .5f) * rowSpacing, p.y, p.z);
        }
    }

    private static float Side(int index) => index % 2 == 0 ? -1f : 1f;

    private void Log(string label) => phaseLog.Add((label, Time.time));

    private void Impulse(float intensity, float duration, Vector3 position)
    {
        LastImpulse = intensity;
        if (DungeonObjectiveShake.Impulse(intensity, duration, position)) Impulses++;
    }

    private void Update()
    {
        if (!isLive) return;
        float dt = Time.deltaTime;
        clock += dt;
        if (enterFlash >= 0f) enterFlash += dt;
        StepSequence(dt);
        for (int i = 0; i < MaxBolts; i++)
        {
            if (!boltMoving[i]) continue;
            boltOut[i] = Mathf.Min(1f, boltOut[i] + dt / boltSlide);
            if (boltOut[i] >= 1f) boltMoving[i] = false;
        }
        Apply();
    }

    private void StepSequence(float dt)
    {
        switch (phase)
        {
            case Phase.Delay:
                phaseTime += dt;
                if (phaseTime < revealDelay) break;
                phase = Phase.Bolts;
                phaseTime = 0f;
                nextBoltAt = 0f;
                lastBoltAt = -boltSlide;
                StepBolts(0f);   // 첫 볼트는 지연이 끝난 그 프레임에 빠지기 시작한다
                break;
            case Phase.Bolts:
                StepBolts(dt);
                break;
            case Phase.Seal:
                phaseTime += dt;
                sealT = Mathf.Clamp01(phaseTime / sealDuration);
                if (sealT < 1f) break;
                FacilityFeedback.Emit(sealSparks, false);
                phase = Phase.Leaves;
                phaseTime = 0f;
                Log("leaves");
                FacilityFeedback.Play(openSound, transform.position);
                FacilityFeedback.Emit(leafDust, true);
                break;
            case Phase.Leaves:
                phaseTime += dt;
                leafT = Mathf.Clamp01(phaseTime / leafDuration);
                if (leafT < 1f) break;
                FacilityFeedback.Emit(leafDust, false);
                FinishSequence();
                break;
        }
    }

    // 볼트 단계: 아직 보이지 않은 풀림을 boltInterval 간격으로 하나씩 빼고(볼트마다 임펄스·소리), 마지막 볼트가 다 빠지면
    // 해금이면 봉인 원판으로, 아니면 잠김 대기로 돌아간다.
    private void StepBolts(float dt)
    {
        phaseTime += dt;
        int goal = Mathf.Min(targetDone, visible);
        while (shownDone < goal && phaseTime >= nextBoltAt)
        {
            int i = shownDone++;
            boltMoving[i] = true;
            lastBoltAt = phaseTime;
            nextBoltAt = phaseTime + boltInterval;
            Log("bolt" + i);
            Vector3 at = bolts[i] != null ? bolts[i].position : transform.position;
            Impulse(boltShake, 0f, at);
            FacilityFeedback.Play(boltSound, at);
        }
        if (shownDone < goal || phaseTime < lastBoltAt + boltSlide) return;
        if (targetUnlocked && shownDone >= visible)
        {
            phase = Phase.Seal;
            phaseTime = 0f;
            state = State.Unlocking;
            Log("seal");
            FacilityFeedback.Play(sealSound, SealPosition);
            FacilityFeedback.Emit(sealSparks, true);
        }
        else
        {
            phase = Phase.Idle;
            state = State.Locked;
        }
    }

    // 연출을 끝낸 모습으로(열림이면 유도등·흔들림까지). 끊긴 경우(OnDisable)에도 같은 결과가 남게 한다.
    private void FinishSequence()
    {
        int goal = Mathf.Min(targetDone, visible);
        for (int i = 0; i < MaxBolts; i++) { if (i < goal) boltOut[i] = 1f; boltMoving[i] = false; }
        shownDone = Mathf.Max(shownDone, goal);
        bool open = targetUnlocked && shownDone >= visible;
        bool wasOpen = state == State.Open;
        phase = Phase.Idle;
        EmitAll(false);
        if (!open) { state = State.Locked; return; }
        sealT = leafT = 1f;
        state = State.Open;
        if (wasOpen || !isActiveAndEnabled) return;
        Log("open");
        FacilityFeedback.Burst(openBurst, 32);
        Impulse(openShake, openShakeTime, transform.position);
    }

    private void EmitAll(bool on)
    {
        FacilityFeedback.Emit(sealSparks, on);
        FacilityFeedback.Emit(leafDust, on);
    }

    private Vector3 SealPosition => sealLeft != null ? sealLeft.position : transform.position;

    // ── 상호작용 ─────────────────────────────

    public bool ShowFacilityHint(PlayerController player, FacilityInteractPoint point)
        => isLive && state == State.Open && HostReady && FacilityAccess.CanUse(player) && player.IsGrounded;

    public void FacilityInteract(PlayerController player, FacilityInteractPoint point)
    {
        if (!TryEnter(player)) point?.Reject();
    }

    /// <summary>열린 문에 들어간다(상호작용 키와 같은 경로, 검사에서도 쓴다). 호스트가 받아들이면 흡입을 시작하고 true.</summary>
    public bool TryEnter(PlayerController player)
    {
        Cache();
        if (!isLive || state != State.Open || !isActiveAndEnabled) return false;
        if (!FacilityAccess.CanUse(player)) return false;
        if (!(DungeonFacilityHost.Current is IDungeonExitHost host)) return false;
        EnterRequests++;
        if (!host.TryEnterExit(this)) return false;
        state = State.Entering;
        interactPoint?.HideHint();
        enterFlash = 0f;
        entering = StartCoroutine(EnterRoutine(player));
        return true;
    }

    private IEnumerator EnterRoutine(PlayerController player)
    {
        var tick = new WaitForFixedUpdate();
        bool held = BeginHold(player);
        busyHost = DungeonFacilityHost.Current;
        busyHost?.SetFacilityBusy(this, true);
        FacilityFeedback.Play(enterFeedback, transform.position);
        if (held)
        {
            // 흡입: 몸통 중심 x를 문 가운데로(가속 — 빨려 들어가듯). 바닥 높이는 그대로 둔다(문 앞 바닥에 서서 들어간다).
            var motor = player.Motor;
            float start = motor.ConfinedBody.center.x, end = transform.position.x, last = start;
            for (float time = 0f; time < intakeDuration;)
            {
                yield return tick;
                if (!StillHeld(player)) break;
                time += Time.fixedDeltaTime;
                float t = Mathf.Clamp01(time / intakeDuration);
                float x = Mathf.Lerp(start, end, t * t);
                motor.ShiftConfinement(new Vector2(x - last, 0f));
                last = x;
            }
        }
        EndBusy();
        state = State.Entered;
        if (!held) { entering = null; yield break; }
        // Wave 종료(WaveEnded·멈춤)가 풀 때까지 보호 구속을 유지한다. Wave 진행 장치가 없으면 잠깐 뒤, 한도를 넘으면 푼다.
        for (float waited = 0f; StillHeld(player); waited += Time.deltaTime)
        {
            if (holdWaves == null ? waited >= noWaveRelease : !holdWaves.IsWaveActive) break;
            if (waited >= holdLimit)
            {
                Debug.LogWarning($"[PropExitGate] Wave 종료를 {holdLimit:0}초 기다렸지만 끝나지 않아 구속을 풉니다.", this);
                break;
            }
            yield return null;
        }
        ReleaseHold();
        entering = null;
    }

    // 이 문 루트의 WavePlayerStart로 보호 구속(FacilityRide와 같은 방식 — WaveManager holder를 쓰지 않아 IsPreparingWave를 건드리지 않는다).
    private bool BeginHold(PlayerController player)
    {
        if (player == null || player.Motor == null) return false;
        if (hold == null && !TryGetComponent(out hold)) hold = gameObject.AddComponent<WavePlayerStart>();
        var abilities = player.AbilityManager;
        if (abilities != null)
        {
            if (abilities.DashAbility != null) { abilities.DashAbility.Interrupt(); abilities.DashAbility.ClearPendingInput(); }
            if (abilities.JumpAbility != null) abilities.JumpAbility.ClearPendingInput();
        }
        hold.HoldForPassage(true);
        if (!hold.IsHoldingPlayer || !player.Motor.IsConfined)
        {
            if (hold.IsHoldingPlayer) hold.Release();
            return false;
        }
        player.Velocity = Vector2.zero;
        player.externalForce = Vector2.zero;
        holdWaves = WaveManager.ActiveInstance;
        if (holdWaves != null) holdWaves.WaveEnded += OnWaveEnded;
        return true;
    }

    private bool StillHeld(PlayerController player)
        => hold != null && hold.IsHoldingPlayer && player != null && player.isActiveAndEnabled && player.Motor != null && player.Motor.IsConfined
           && (player.Health == null || !player.Health.IsDead);

    // WaveManager.CompleteWave가 자기 구속을 푼(ReleasePlayer) 직후 올라온다 — 같은 순간 이 문의 구속도 푼다.
    private void OnWaveEnded() => ReleaseHold();

    private void ReleaseHold()
    {
        if (holdWaves != null) holdWaves.WaveEnded -= OnWaveEnded;
        holdWaves = null;
        if (hold != null && hold.IsHoldingPlayer) hold.Release();
    }

    private void EndBusy()
    {
        var host = busyHost;
        busyHost = null;
        host?.SetFacilityBusy(this, false);
    }

    // ── 표시 ─────────────────────────────

    private void Apply()
    {
        for (int i = 0; i < bolts.Length; i++)
        {
            if (bolts[i] == null || !bolts[i].gameObject.activeSelf) continue;
            bolts[i].localPosition = boltHome[i] + new Vector3(Side(i) * boltTravel * Ease(boltOut[i]), 0f, 0f);
        }
        float angle = SealAngle;
        if (sealLeft != null) sealLeft.localRotation = sealLeftHome * Quaternion.Euler(0f, 0f, angle);
        if (sealRight != null) sealRight.localRotation = sealRightHome * Quaternion.Euler(0f, 0f, angle);
        float leaf = LeafOpen;
        if (leafLeft != null) leafLeft.localPosition = leafLeftHome + new Vector3(-leafTravel * leaf, 0f, 0f);
        if (leafRight != null) leafRight.localPosition = leafRightHome + new Vector3(leafTravel * leaf, 0f, 0f);

        bool open = state == State.Open || state == State.Entering || state == State.Entered;
        float openPulse = .8f + .35f * Mathf.Sin(clock * 3.4f);
        // 볼트 램프: 풀린 볼트만 켜짐(빠지는 동안 차오름), 열린 뒤 함께 맥동.
        if (boltLampLights != null)
            for (int i = 0; i < boltLampLights.Count; i++)
                boltLampLights.SetOne(i, i >= visible ? 0f : open ? 1.1f * openPulse : Mathf.Lerp(.14f, 1.3f, boltOut[i]));
        // 상인방 램프 줄('n/3'): 풀린 수만큼 켜짐, 열린 뒤 줄 전체가 1.2초 주기로 반복 표시.
        if (rowLampLights != null)
        {
            float sweep = Mathf.Repeat(clock / 1.2f, 1f) * (visible + 1);
            for (int i = 0; i < rowLampLights.Count; i++)
            {
                float level;
                if (i >= visible) level = 0f;
                else if (open) level = i < sweep ? 1.3f : .5f;
                else level = i < shownDone && boltOut[i] >= .99f ? 1.2f : .12f;
                rowLampLights.SetOne(i, level);
            }
        }
        sealLamp?.Set(phase == Phase.Seal ? 1.6f : open ? .25f : state == State.Unlocking ? .9f : .35f + .1f * Mathf.Sin(clock * 2f));
        // 유도등: 문짝이 열리면서 켜지고, 열린 뒤 바닥 → 위로 흐른다.
        if (guideLamp != null && guideLamp.Count > 0)
        {
            int count = guideLamp.Count;
            float head = Mathf.Repeat(clock * 7f, count + 3f);
            for (int i = 0; i < count; i++)
            {
                float level = 0f;
                if (open) { float k = Mathf.Clamp01(1f - Mathf.Abs(i - head) / 2f); level = Mathf.Lerp(.35f, 1.4f, k); }
                else if (phase == Phase.Leaves) level = .35f * leaf;
                guideLamp.SetOne(i, level);
            }
        }
        float flashKick = enterFlash >= 0f && enterFlash < .6f ? 1f - enterFlash / .6f : 0f;
        innerLamp?.Set(open ? .55f + .15f * Mathf.Sin(clock * 2.2f) + 1.2f * flashKick : phase == Phase.Leaves ? .55f * leaf : .05f);
        signLamp?.Set(open ? openPulse : state == State.Unlocking ? .7f : .25f);
        trimLamp?.Set(open || state == State.Unlocking ? 1f : .45f);
    }
}
