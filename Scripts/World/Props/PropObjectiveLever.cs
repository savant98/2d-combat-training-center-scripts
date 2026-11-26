using System;
using Unity.Cinemachine;
using UnityEngine;

// 던전 목표 레버(IDungeonObjectivePoint, DESIGN_V4 §2.5 R10). footprint 4×6, 뒤 벽에 볼트로 고정한 대형 배전반(DungeonProp 루트 + 이 부품, 충돌 없음).
// - 템플릿에는 자리 표식(DungeonObjectiveSlot 4×6)만 두고, 목표가 배정된 슬롯에만 목표 SO(LeverObjectiveSO.PrefabFor)가 이 Prefab을 세운다.
//   세운 쪽(DungeonRoomStage)이 RoomId·Slot·Index를 채우고, 그 방의 completedObjectives로 SetCompleted(true, true)를 즉시 적용한다.
// - 적을 모두 처치한 뒤에만 당긴다(FacilityAccess.CanUseObjective — 싸우는 동안은 잠겨 있다). 7차(DESIGN_V7 V5): 레버가 남은 방은 적 전멸 뒤에도 문이 잠긴 '레버 대기'이고
//   (호스트 IDungeonObjectiveGate.CanCompleteObjectives), 레버를 당겨야 방 클리어된다. 완료는 IDungeonObjectiveHost.TryCompleteObjective로 요청하고
//   받아들여지면 그때 SetCompleted(true, false)로 당기는 연출을 한다. 던전 밖(목표 호스트 없음)에서는 꺼진 채 서 있다.
// - 상태: Locked(방 미클리어 — 흐린 앰버·붉은 표시등) → Ready(앰버 강조·빛기둥·경고등 회전) → Pulling(0.6초: 손잡이가 내려가며 지속 흔들림이 오르고 불꽃)
//   → 빗장이 걸리는 순간 강한 임펄스(마지막 레버는 1.3배) → Done(전력 서지 0.5초: 표시등 → 배선 → 경고등 순으로 켜진 뒤 녹색 표시등·빛기둥 꺼짐).
// - 흔들림은 기존 FeedbackPlayer 경로만 쓴다. 지속 흔들림 = BeginHeldShake/EndHeldShake(합산 배율을 거치지 않는 원래 세기),
//   임펄스 = 흔들림만 담은 실행 중 FeedbackDataSO 사본을 PlayExact로(거리 감쇠 없음 — 당긴 Player가 바로 앞에 있다). 실효 세기 = 요청 × 합산 배율(MainScene 0.35, 상한 0.5).
//   게임패드 진동은 프로젝트에 없어 넣지 않는다(DESIGN_V4 §1.2).
// - 물리 질의 없음(거리 판정은 FacilityInteractPoint.InRange). 크기 늘이기 없음(회전·이동·밝기만 — 기계형 규칙). 소리는 기존 효과음 재사용.
[DisallowMultipleComponent]
public sealed class PropObjectiveLever : MonoBehaviour, IDungeonPropPart, IDungeonRoomPowered, IFacilityInteractTarget, IDungeonObjectivePoint, IFacilityHighlightSource, IFacilityBusy
{
    public enum State { Locked, Ready, Pulling, Done }

    /// <summary>전력 서지 단계(Done 진입 뒤). 0 없음, 1 표시등, 2 배선, 3 경고등, 4 끝(녹색 표시등).</summary>
    public const int SurgeNone = 0, SurgeIndicators = 1, SurgeWiring = 2, SurgeBeacon = 3, SurgeComplete = 4;

    [Header("움직임")]
    [SerializeField, Min(.05f), Tooltip("손잡이를 끝까지 당기는 시간(초).")] private float pullDuration = .6f;
    [SerializeField, Tooltip("손잡이를 다 당겼을 때 회전(도, 음수 = 시계 방향).")] private float pulledAngle = -125f;
    [SerializeField, Min(.05f), Tooltip("빗장이 걸린 뒤 전력 서지(표시등 → 배선 → 경고등) 시간(초).")] private float surgeDuration = .5f;
    [SerializeField, Min(.05f), Tooltip("서지가 끝난 순간 완료 빛이 번쩍였다가 가라앉는 시간(초).")] private float flashTime = 1.1f;
    [SerializeField, Tooltip("경고등 반사판 회전 속도(도/초, Ready). 당기는 동안은 두 배.")] private float beaconSpin = 240f;
    [SerializeField, Min(.1f), Tooltip("경고등 깜박임 주기(초, Ready).")] private float beaconBlink = .6f;
    [SerializeField, Min(.1f), Tooltip("빛기둥 빛 덩어리가 오르는 높이(칸).")] private float moteRise = 1.15f;
    [SerializeField, Min(.05f), Tooltip("빛 덩어리 하나가 오르는 빠르기(회/초).")] private float moteSpeed = .8f;

    [Header("흔들림(기존 FeedbackPlayer 경로)")]
    [SerializeField, Tooltip("당기는 동안의 지속 흔들림 세기(BeginHeldShake 원래 값). 진행 0·35·70%에서 차례로 올린다.")]
    private float[] heldShakeSteps = { .05f, .09f, .14f };
    [SerializeField, Min(0f), Tooltip("빗장이 걸리는 순간 임펄스 요청 세기(FeedbackPlayer 합산 배율 0.35를 곱하면 실효 약 0.32).")] private float latchShake = .9f;
    [SerializeField, Min(0f), Tooltip("빗장 임펄스 지속 시간(초).")] private float latchShakeTime = .25f;
    [SerializeField, Min(1f), Tooltip("던전의 마지막 레버(출구 해금)일 때 임펄스 배율.")] private float finalShakeScale = 1.3f;

    [Header("구성(생성 도구가 연결)")]
    [SerializeField] private Transform handle;
    [SerializeField, Tooltip("손잡이가 끝까지 내려가면 들어가 잠그는 빗장(이동).")] private Transform latch;
    [SerializeField] private Vector3 latchLocked = new Vector3(-.3f, 0f, 0f);
    [SerializeField, Tooltip("경고등(앰버 둥근 등).")] private Renderer[] beacon = Array.Empty<Renderer>();
    [SerializeField, Tooltip("경고등 안에서 도는 반사판(회전만).")] private Transform beaconReflector;
    [SerializeField] private Renderer[] reflectorLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("패널 표시등 3줄(앰버, 아랫줄 왼쪽부터 — 서지 때 이 순서로 켜진다).")] private Renderer[] indicators = Array.Empty<Renderer>();
    [SerializeField, Tooltip("배선 덕트 빛(아래에서 위 순서).")] private Renderer[] wiring = Array.Empty<Renderer>();
    [SerializeField, Tooltip("잠김 표시등(붉은 점 — 방 미클리어).")] private Renderer[] lockLamps = Array.Empty<Renderer>();
    [SerializeField, Tooltip("완료 표시등(녹색 — 서지가 끝나면 켜짐).")] private Renderer[] doneLamps = Array.Empty<Renderer>();
    [SerializeField, Tooltip("패널 위 앰버 빛기둥(Ready에서만).")] private Renderer[] pillar = Array.Empty<Renderer>();
    [SerializeField, Tooltip("빛기둥을 따라 오르는 빛 덩어리(이동만).")] private Transform[] pillarMotes = Array.Empty<Transform>();
    [SerializeField] private Renderer[] moteLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("완료 빛(앰버 후광·광선). 서지가 끝난 순간 번쩍인다.")] private Renderer[] doneFlash = Array.Empty<Renderer>();
    [SerializeField, Tooltip("흰 외곽 발광(잠김이면 흐리게).")] private Renderer[] trimLights = Array.Empty<Renderer>();
    [SerializeField] private ParticleSystem sparks;
    [SerializeField] private ParticleSystem doneBurst;
    [SerializeField] private FacilityInteractPoint interactPoint;
    [SerializeField, Tooltip("목표 UI 마커를 둘 자리(루트 기준, 패널 위).")] private Vector2 markerOffset = new Vector2(0f, 3.3f);

    [Header("효과(기존 효과음 재사용)")]
    [SerializeField, Tooltip("당기기 시작 효과(소리·작은 흔들림).")] private FeedbackDataSO pullFeedback;
    [SerializeField] private SoundOptions latchSound;
    [SerializeField] private SoundOptions doneSound;
    [SerializeField] private SoundOptions readySound;

    private bool cached;
    private bool isLive;
    private bool powered;
    private bool completed;
    private bool finalLever;
    private State state;
    private float pullTime;
    private float surge = 1f;       // 서지 진행(0~1, 1 = 끝)
    private float latchAge = -1f;   // 빗장이 걸린 뒤 경과(음수면 없음)
    private float flash = -1f;      // 완료 빛 경과(음수면 없음)
    private float clock;
    private float pillarLevel;
    private float reflectorAngle;
    private int heldStep = -1;
    private CinemachineImpulseManager.ImpulseEvent heldShake;
    private Quaternion handleHome, reflectorHome;
    private Vector3 latchHome;
    private Vector3[] moteHome = Array.Empty<Vector3>();
    private FacilityLights beaconLamp, reflectorLamp, indicatorLamp, wiringLamp, lockLamp, doneLamp, pillarLamp, moteLamp, flashLamp, trimLamp;

    public int RoomId { get; set; } = -1;
    public int Slot { get; set; } = -1;
    public int Index { get; set; } = -1;
    public bool IsCompleted => completed;
    public State CurrentState => state;
    public FacilityInteractPoint InteractPoint => interactPoint;
    /// <summary>목표 UI 마커 자리(월드, 패널 위). 목표 대상 목록(IDungeonObjectiveTargets)의 위치로 쓴다.</summary>
    public Vector3 MarkerPoint => transform.TransformPoint(markerOffset);
    /// <summary>키 안내가 지금 보이는지(UI 마커는 이때 숨긴다 — R3).</summary>
    public bool HintVisible => interactPoint != null && interactPoint.HintVisible;
    /// <summary>호스트에 완료를 요청한 횟수(검사용, 대여마다 0).</summary>
    public int Requests { get; private set; }
    /// <summary>손잡이 회전 정도(0 위 ~ 1 다 당김, 검사용).</summary>
    public float HandlePull { get; private set; }
    /// <summary>전력 서지 단계(SurgeNone~SurgeComplete, 검사용).</summary>
    public int SurgeStage { get; private set; }
    /// <summary>지금 지속 흔들림 세기(없으면 0, 검사용).</summary>
    public float HeldShakeLevel => heldShake != null && heldStep >= 0 && heldStep < heldShakeSteps.Length ? heldShakeSteps[heldStep] : 0f;
    public bool HeldShakeActive => heldShake != null;
    /// <summary>빗장 임펄스를 낸 횟수(FeedbackPlayer가 있을 때만 센다, 검사용).</summary>
    public int Impulses { get; private set; }
    /// <summary>마지막 빗장 임펄스 요청 세기(마지막 레버면 × finalShakeScale, 검사용).</summary>
    public float LastImpulse { get; private set; }
    /// <summary>이번 당기기가 던전의 마지막 레버였는지(검사용).</summary>
    public bool WasFinal => finalLever;
    /// <summary>빛기둥 밝기 배율(검사용).</summary>
    public float PillarLevel => pillarLevel;
    /// <summary>경고등 반사판 각도(도, 검사용).</summary>
    public float BeaconAngle => reflectorAngle;
    public float PullDuration => pullDuration;
    public float SurgeDuration => surgeDuration;
    public float LatchShake => latchShake;
    public float FinalShakeScale => finalShakeScale;
    public bool FacilityReady => isLive && state == State.Ready && HostReady;
    private static bool HostReady => DungeonFacilityHost.Current is IDungeonObjectiveHost;
    /// <summary>
    /// 6차 T2: 손잡이를 당기는 동안 남은 시간(초), 아니면 0(검사·표시용 — 크레인 잠금은 IFacilityBusy.FacilityBusy가 맡는다).
    /// 레버 컷씬(DungeonCutsceneDirector)이 켜져 있으면 컷씬이 입력을 막으므로 이 잠금은 컷씬을 끈 경우에만 실제로 쓰인다.
    /// </summary>
    public float FacilityBusySeconds => state == State.Pulling ? Mathf.Max(0f, pullDuration - pullTime) : 0f;
    /// <summary>손잡이를 당기는 중인지(6차 T2).</summary>
    public bool IsOperating => state == State.Pulling;
    /// <summary>6차 T2(IFacilityBusy): 당기는 동안 크레인 직선·붙기를 막는다(레버 컷씬이 켜져 있으면 입력 차단과 겹쳐 무해 — 컷씬을 끈 경우에 쓰인다).</summary>
    public bool FacilityBusy => isLive && IsOperating;

    private void Awake() => Cache();

    private void Cache()
    {
        if (cached) return;
        cached = true;
        if (handle != null) handleHome = handle.localRotation;
        if (latch != null) latchHome = latch.localPosition;
        if (beaconReflector != null) reflectorHome = beaconReflector.localRotation;
        moteHome = new Vector3[pillarMotes.Length];
        for (int i = 0; i < pillarMotes.Length; i++) moteHome[i] = pillarMotes[i] != null ? pillarMotes[i].localPosition : Vector3.zero;
        beaconLamp = new FacilityLights(beacon);
        reflectorLamp = new FacilityLights(reflectorLights);
        indicatorLamp = new FacilityLights(indicators);
        wiringLamp = new FacilityLights(wiring);
        lockLamp = new FacilityLights(lockLamps);
        doneLamp = new FacilityLights(doneLamps);
        pillarLamp = new FacilityLights(pillar);
        moteLamp = new FacilityLights(moteLights);
        flashLamp = new FacilityLights(doneFlash);
        trimLamp = new FacilityLights(trimLights);
    }

    private void OnDisable()
    {
        // 당기는 도중 꺼지면(관 통과로 방이 거둬짐) 완료 상태는 이미 호스트에 적혔으므로 완료 모습으로 둔다.
        if (state == State.Pulling) { state = State.Done; HandlePull = 1f; }
        if (state == State.Done) { surge = 1f; SurgeStage = SurgeComplete; }
        StopHeldShake();
        FacilityFeedback.Emit(sparks, false);
    }

    // ── 수명 ─────────────────────────────

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        isLive = true;
        RoomId = Slot = Index = -1;
        powered = false;
        completed = false;
        finalLever = false;
        state = State.Locked;
        pullTime = 0f;
        surge = 1f;
        SurgeStage = SurgeNone;
        latchAge = -1f;
        flash = -1f;
        clock = 0f;
        pillarLevel = 0f;
        reflectorAngle = 0f;
        Requests = 0;
        Impulses = 0;
        LastImpulse = 0f;
        HandlePull = 0f;
        StopHeldShake();
        if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (doneBurst != null) doneBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        Apply();
    }

    public void OnPropReturning(DungeonProp prop)
    {
        isLive = false;
        StopHeldShake();
        if (beaconReflector != null) beaconReflector.localRotation = reflectorHome;
        for (int i = 0; i < pillarMotes.Length; i++) if (pillarMotes[i] != null) pillarMotes[i].localPosition = moteHome[i];
        beaconLamp?.Restore();
        reflectorLamp?.Restore();
        indicatorLamp?.Restore();
        wiringLamp?.Restore();
        lockLamp?.Restore();
        doneLamp?.Restore();
        pillarLamp?.Restore();
        moteLamp?.Restore();
        flashLamp?.Restore();
        trimLamp?.Restore();
    }

    public void SetRoomPowered(bool on, bool instant)
    {
        Cache();
        bool was = powered;
        powered = on;
        if (completed) return;
        state = on ? State.Ready : State.Locked;
        if (instant) pillarLevel = on ? 1f : 0f;
        if (on && !was && !instant) FacilityFeedback.Play(readySound, transform.position);
        Apply();
    }

    /// <summary>완료 모습을 적용한다(계약). instant면 연출 없이 바로(다시 세울 때), 아니면 손잡이를 당기는 연출과 함께. 같은 값이면 아무 일도 없다.</summary>
    public void SetCompleted(bool done, bool instant)
    {
        Cache();
        if (done)
        {
            if (completed && (state == State.Pulling || state == State.Done) && !instant) return;
            completed = true;
            interactPoint?.HideHint();
            if (instant)
            {
                StopHeldShake();
                state = State.Done;
                HandlePull = 1f;
                surge = 1f;
                SurgeStage = SurgeComplete;
                latchAge = -1f;
                flash = -1f;
                pillarLevel = 0f;
                FacilityFeedback.Emit(sparks, false);
            }
            else
            {
                finalLever = IsFinalNow();
                state = State.Pulling;
                pullTime = 0f;
                HandlePull = 0f;
                SurgeStage = SurgeNone;
                FacilityFeedback.Play(pullFeedback, HandlePivot);
                SetHeldStep(0);
            }
        }
        else
        {
            StopHeldShake();
            completed = false;
            finalLever = false;
            state = powered ? State.Ready : State.Locked;
            HandlePull = 0f;
            surge = 1f;
            SurgeStage = SurgeNone;
            latchAge = -1f;
            flash = -1f;
            FacilityFeedback.Emit(sparks, false);
        }
        Apply();
    }

    // 호스트가 완료를 받아들인 뒤 진행이 다 찼으면 마지막 레버(출구 해금). 진행 원본은 선택 인터페이스로만 읽는다.
    private static bool IsFinalNow()
    {
        var host = DungeonFacilityHost.Current;
        if (host is IDungeonExitSource exit && exit.HasExit) return exit.ExitUnlocked;
        return host is IDungeonObjectiveSource source && source.HasObjective && source.ObjectiveTotal > 0 && source.ObjectiveDone >= source.ObjectiveTotal;
    }

    // ── 상호작용 ─────────────────────────────

    public bool ShowFacilityHint(PlayerController player, FacilityInteractPoint point)
        => isLive && state == State.Ready && powered && HostReady && FacilityAccess.CanUseObjective(player) && player.IsGrounded;

    public void FacilityInteract(PlayerController player, FacilityInteractPoint point)
    {
        if (!TryPull(player)) point?.Reject();
    }

    /// <summary>레버를 당긴다(상호작용 키와 같은 경로, 검사에서도 쓴다). 호스트가 완료를 받아들이면 true.</summary>
    public bool TryPull(PlayerController player)
    {
        Cache();
        if (!isLive || state != State.Ready || !powered || !isActiveAndEnabled) return false;
        if (!FacilityAccess.CanUseObjective(player)) return false;
        if (!(DungeonFacilityHost.Current is IDungeonObjectiveHost host)) return false;
        Requests++;
        if (!host.TryCompleteObjective(this)) return false;
        // 계약: 받아들여지면 실물이 완료 모습을 적용한다(호스트가 먼저 불렀다면 SetCompleted가 같은 값이라 다시 하지 않는다).
        SetCompleted(true, false);
        return true;
    }

    private Vector3 HandlePivot => handle != null ? handle.position : transform.position;

    // ── 흔들림 ─────────────────────────────

    // 지속 흔들림을 step 단계로 올린다(앞 단계는 감쇠하며 사라진다 — EndHeldShake).
    private void SetHeldStep(int step)
    {
        if (step == heldStep && heldShake != null) return;
        var player = FeedbackPlayer.Instance;
        var previous = heldShake;
        heldShake = null;
        heldStep = step;
        if (player != null && step >= 0 && step < heldShakeSteps.Length && heldShakeSteps[step] > 0f)
            heldShake = player.BeginHeldShake(heldShakeSteps[step], HandlePivot);
        if (previous != null && player != null) player.EndHeldShake(previous);
    }

    private void StopHeldShake()
    {
        if (heldShake != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.EndHeldShake(heldShake);
        heldShake = null;
        heldStep = -1;
    }

    // ── 표시 ─────────────────────────────

    private void Update()
    {
        if (!isLive) return;
        float dt = Time.deltaTime;
        clock += dt;
        if (state == State.Pulling) StepPull(dt);
        else if (state == State.Done && surge < 1f) StepSurge(dt);
        if (latchAge >= 0f) latchAge += dt;
        if (flash >= 0f)
        {
            flash += dt;
            if (flash >= flashTime) flash = -1f;
        }
        bool spin = state == State.Ready || state == State.Pulling || (state == State.Done && SurgeStage == SurgeBeacon);
        if (spin) reflectorAngle = Mathf.Repeat(reflectorAngle + beaconSpin * (state == State.Ready ? 1f : 2f) * dt, 360f);
        float pillarWant = state == State.Ready || state == State.Pulling ? 1f : state == State.Done && surge < 1f ? 1f - surge : 0f;
        pillarLevel = Mathf.MoveTowards(pillarLevel, pillarWant, dt / .25f);
        Apply();
    }

    private void StepPull(float dt)
    {
        pullTime += dt;
        float t = Mathf.Clamp01(pullTime / pullDuration);
        // 무겁게 내려간다(처음 느리고 가운데 빠르고 끝에서 걸림).
        HandlePull = t * t * (3f - 2f * t);
        SetHeldStep(t < .35f ? 0 : t < .7f ? 1 : 2);
        FacilityFeedback.Emit(sparks, t > .35f && t < 1f);
        if (t >= 1f) Latch();
    }

    // 빗장이 걸린다: 지속 흔들림을 끝내고 강한 임펄스 1회 → 전력 서지 시작.
    private void Latch()
    {
        StopHeldShake();
        state = State.Done;
        HandlePull = 1f;
        surge = 0f;
        SurgeStage = SurgeIndicators;
        latchAge = 0f;
        FacilityFeedback.Emit(sparks, false);
        FacilityFeedback.Burst(sparks, 18);
        LastImpulse = latchShake * (finalLever ? finalShakeScale : 1f);
        if (DungeonObjectiveShake.Impulse(LastImpulse, latchShakeTime, HandlePivot)) Impulses++;
        FacilityFeedback.Play(latchSound, HandlePivot);
    }

    // 표시등(0~40%) → 배선(40~80%) → 경고등(80~100%) → 끝(녹색 표시등·완료 빛·완료음).
    private void StepSurge(float dt)
    {
        surge = Mathf.Min(1f, surge + dt / surgeDuration);
        int stage = surge < .4f ? SurgeIndicators : surge < .8f ? SurgeWiring : surge < 1f ? SurgeBeacon : SurgeComplete;
        if (stage == SurgeComplete && SurgeStage != SurgeComplete)
        {
            flash = 0f;
            FacilityFeedback.Burst(doneBurst, 28);
            FacilityFeedback.Play(doneSound, transform.position);
        }
        SurgeStage = stage;
    }

    private void Apply()
    {
        float recoil = latchAge >= 0f && latchAge < .18f ? Mathf.Sin(latchAge / .18f * Mathf.PI) * .05f : 0f;
        if (handle != null) handle.localRotation = handleHome * Quaternion.Euler(0f, 0f, pulledAngle * (HandlePull - recoil));
        if (latch != null)
        {
            float locked = state == State.Done ? (latchAge >= 0f ? Mathf.Clamp01(latchAge / .08f) : 1f) : 0f;
            latch.localPosition = latchHome + latchLocked * locked;
        }
        if (beaconReflector != null) beaconReflector.localRotation = reflectorHome * Quaternion.Euler(0f, 0f, reflectorAngle);
        bool host = HostReady;
        bool blinkOn = Mathf.Repeat(clock, beaconBlink) < beaconBlink * .55f;
        float beaconLevel, reflectorLevel;
        switch (state)
        {
            case State.Ready:
                beaconLevel = host ? (blinkOn ? 1.35f : .3f) : .1f;
                reflectorLevel = host ? .9f : 0f;
                break;
            case State.Pulling:
                beaconLevel = Mathf.Repeat(clock, .12f) < .06f ? 1.5f : .6f;
                reflectorLevel = 1.2f;
                break;
            case State.Done:
                if (SurgeStage == SurgeBeacon) { beaconLevel = 1.8f; reflectorLevel = 1.5f; }
                else if (SurgeStage == SurgeComplete) { beaconLevel = 1f; reflectorLevel = 0f; }
                else { beaconLevel = .25f; reflectorLevel = 0f; }
                break;
            default:
                beaconLevel = .06f;
                reflectorLevel = 0f;
                break;
        }
        beaconLamp?.Set(beaconLevel);
        reflectorLamp?.Set(reflectorLevel);
        trimLamp?.Set(state == State.Locked ? .35f : 1f);
        lockLamp?.Set(state == State.Locked ? .7f + .3f * Mathf.Sin(clock * 2.4f) : 0f);
        float burst = flash >= 0f ? 1f - flash / flashTime : 0f;
        doneLamp?.Set(state == State.Done && SurgeStage == SurgeComplete ? 1.1f + .8f * burst : 0f);
        flashLamp?.Set(burst > 0f ? 1.8f * burst * burst : 0f);

        // 표시등: 잠김 흐림, Ready 아래 → 위로 흐름, 당기는 중 빠른 흐름, 서지 표시등 단계에서 차례로 켜짐.
        if (indicatorLamp != null && indicatorLamp.Count > 0)
        {
            int count = indicatorLamp.Count;
            float head = Mathf.Repeat(clock * (state == State.Pulling ? 9f : 4f), count + 2f);
            float lit = SurgeStage == SurgeIndicators ? surge / .4f * count : SurgeStage >= SurgeWiring ? count : 0f;
            for (int i = 0; i < count; i++)
            {
                float level;
                if (state == State.Done) level = SurgeStage == SurgeComplete ? .7f : i < lit ? 1.3f : .15f;
                else if ((state == State.Ready && host) || state == State.Pulling) { float k = Mathf.Clamp01(1f - Mathf.Abs(i - head) / 1.4f); level = Mathf.Lerp(.15f, 1.1f, k); }
                else level = .08f;
                indicatorLamp.SetOne(i, level);
            }
        }
        // 배선: 서지 배선 단계에서 아래 → 위로 켜지고 그 뒤 은은하게 남는다.
        if (wiringLamp != null && wiringLamp.Count > 0)
        {
            int count = wiringLamp.Count;
            float lit = SurgeStage == SurgeWiring ? (surge - .4f) / .4f * count : SurgeStage >= SurgeBeacon ? count : 0f;
            for (int i = 0; i < count; i++)
            {
                float level;
                if (state == State.Done) level = SurgeStage == SurgeComplete ? .45f : i < lit ? 1.4f : .06f;
                else level = state == State.Locked ? .04f : .12f;
                wiringLamp.SetOne(i, level);
            }
        }
        // 빛기둥과 오르는 빛 덩어리(이동만, 크기 변형 없음).
        float pulse = .85f + .15f * Mathf.Sin(clock * 3.1f);
        pillarLamp?.Set(pillarLevel * pulse);
        for (int i = 0; i < pillarMotes.Length; i++)
        {
            var mote = pillarMotes[i];
            if (mote == null) continue;
            float phase = Mathf.Repeat(clock * moteSpeed + i / (float)Mathf.Max(1, pillarMotes.Length), 1f);
            mote.localPosition = moteHome[i] + Vector3.up * (phase * moteRise);
            if (moteLamp != null && i < moteLamp.Count) moteLamp.SetOne(i, pillarLevel * 1.3f * Mathf.Sin(phase * Mathf.PI));
        }
    }
}

/// <summary>
/// 던전 목표(레버·출구 문)의 화면 흔들림. 기존 FeedbackPlayer 임펄스 경로를 그대로 쓰고, 공유 FeedbackDataSO는 바꾸지 않는다
/// (흔들림만 담은 실행 중 사본 하나를 세기만 바꿔 PlayExact로 보낸다). 거리 감쇠는 하지 않는다 — 목표를 작동한 Player가 바로 앞에 있어
/// DungeonPropFeedback의 카메라 거리 감쇠(7~24칸)가 세기를 깎지 않게 한다. 실효 세기 = 요청 × FeedbackPlayer 합산 배율(MainScene 0.35, 창 상한 0.5).
/// </summary>
public static class DungeonObjectiveShake
{
    private static FeedbackDataSO pulse;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPulse() => pulse = null;

    /// <summary>임펄스 1회(duration > 0이면 지속형). FeedbackPlayer가 없거나 세기가 0이면 false.</summary>
    public static bool Impulse(float intensity, float duration, Vector3 position)
    {
        var player = FeedbackPlayer.Instance;
        if (player == null || intensity <= 0f) return false;
        if (pulse == null)
        {
            pulse = ScriptableObject.CreateInstance<FeedbackDataSO>();
            pulse.name = "DungeonObjectiveShake (Instance)";
            pulse.hideFlags = HideFlags.DontSave;
            pulse.particlePrefabs = Array.Empty<GameObject>();
            pulse.sound = null;
            pulse.useHitStop = false;
        }
        pulse.shakeIntensity = intensity;
        pulse.shakeDuration = Mathf.Max(0f, duration);
        player.PlayExact(pulse, position, Quaternion.identity);
        return true;
    }
}
