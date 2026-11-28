using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Lobby 위층의 전투 훈련 엘리베이터 정박대. 훈련 메뉴에서 전투 훈련을 고르면 내려온다.
/// - 등장 연출(PlayReveal): 경고등·경고음 → 빗장이 빠지고 판이 내려앉았다가 갈라짐(Animator, 이동·회전 Clip) → 케이블에 매달린 케이지가 내려와
///   위층 바닥에 착지(완충기·먼지·흔들림). 문은 탑승 입구와 상호작용하면 열린다.
///   카메라·입력 막기·세이브 기록은 GameEventManager가 맡는다.
///   실행 번호(run)와 OnDisable 확정은 LobbyStationUnlock과 같은 관례다(멈춘 코루틴의 finally는 돌지 않는다).
/// - 탑승(RequestRide): 훈련 목록에서 전투 훈련 WaveData를 찾아 ElevatorRideTransition으로 방 전환을 요청한다. 이동 연출은 Scene 루트의 FieldElevatorRide가 맡는다.
/// 루트는 배치용(위치만)이며 위층 플랫폼 윗면(Y -28)을 따라 X로만 옮긴다. 해치 모형·천장 위 단면·먼지·케이블 매단 점·카메라 중심은 루트의 자식이라 함께 따라가고,
/// 이동 장치는 루트 위치로 갱도 원점(CeilingTopY)을 다시 계산한다. Y는 Lobby의 위층 바닥(-28)과 천장 아랫면(-12)이 정하므로 옮기지 않는다:
/// 해치 모형(Model/Hatch, 루트 +16)은 hatchHeight와 묶이지 않은 도구 상수 자리다. Lobby 높이가 바뀌면 FieldElevatorSetup의 BaySpot·HatchHeight를 고쳐 다시 만든다.
/// 판정(Collider)은 탑승 입구의 Trigger 하나뿐이다.
/// </summary>
[DisallowMultipleComponent]
public sealed class FieldElevator : MonoBehaviour
{
    private static readonly int OpenId = Animator.StringToHash("Open");
    private static readonly int AlarmId = Animator.StringToHash("Alarm");
    private static readonly int ClosedState = Animator.StringToHash("Closed");
    private static readonly int OpenedState = Animator.StringToHash("Opened");

    [Header("구성")]
    [SerializeField, Tooltip("해치·경고등 Animator(FieldElevatorBay.controller, bool Open·Alarm)")] private Animator bayAnimator;
    [SerializeField] private FieldElevatorCar car;
    [SerializeField, Tooltip("케이지가 정박하는 자리(케이지를 다시 붙이는 부모)")] private Transform carSlot;
    [SerializeField] private FieldElevatorTrigger boarding;
    [SerializeField, Tooltip("등장 연출의 카메라 중심")] private Transform focus;
    [SerializeField, Tooltip("Lobby에서 케이블이 매달린 높이(화면 밖, 천장 위)")] private Transform cableTop;
    [SerializeField, Tooltip("착지 완충기 머리(아래로 눌렸다 돌아온다)")] private Transform[] buffers = new Transform[0];
    [SerializeField, Tooltip("해치가 풀릴 때 떨어지는 먼지")] private ParticleSystem hatchDust;
    [SerializeField, Tooltip("Scene 루트의 이동 장치")] private FieldElevatorRide ride;

    [Header("목적지")]
    [SerializeField, Tooltip("전투 훈련를 불러올 방(TrainingRoom)")] private Room targetRoom;
    [SerializeField, Tooltip("훈련 목록에서 WaveData를 찾는 키")] private string destinationTrainingId = "CombatBasic";
    [SerializeField, Tooltip("훈련 목록 자체를 불러올 수 없을 때 쓰는 WaveData")] private WaveDataSO fallbackWaveData;
    [SerializeField] private RoomTransitionEffectSO rideTransition;

    [Header("치수(칸)")]
    [SerializeField, Tooltip("위층 바닥 윗면 → 천장 아랫면. 해치 모형(Model/Hatch)의 높이와 같아야 한다(도구가 기록, 검사가 대조)")] private float hatchHeight = 16f;
    [SerializeField, Tooltip("천장 타일 두께")] private float ceilingThickness = 1f;

    [Header("등장 연출")]
    [SerializeField, Min(.1f), Tooltip("해치 열림 Clip 길이(도구가 기록)")] private float openingDuration = 3f;
    [SerializeField, Tooltip("케이지 바닥이 출발하는 높이(바닥 기준, 화면 위 밖)")] private float revealStartHeight = 17.6f;
    [SerializeField, Min(.1f), Tooltip("하강 속도(칸/초)")] private float revealSpeed = 6f;
    [SerializeField, Min(.1f)] private float revealAcceleration = 12f;
    [SerializeField, Min(.1f)] private float revealBraking = 7f;
    [SerializeField, Min(0f), Tooltip("착지 뒤 경고등을 끄기 전 대기 시간")] private float landingSettle = .4f;
    [SerializeField, Tooltip("완충기 눌림 깊이")] private float bufferStroke = .12f;
    [SerializeField, Min(.05f)] private float bufferTime = .35f;
    [SerializeField, Min(0f), Tooltip("착지 연출 끝의 대기 시간")] private float finishHold = .25f;

    [Header("소리")]
    [SerializeField, Tooltip("0 경고, 1 볼트, 2 풀림, 3 구동, 4 잠김(해치 열림 Clip의 Animation Event PlayBayCue)")] private SoundOptions[] bayCues = new SoundOptions[0];
    [SerializeField, Tooltip("케이지가 내려오는 동안의 반복음")] private SoundOptions motorLoop;
    [SerializeField, Tooltip("탑승할 수 있게 되었을 때")] private SoundOptions readySound;
    [SerializeField, Tooltip("해치가 풀릴 때(흔들림)")] private FeedbackDataSO hatchFeedback;
    [SerializeField, Tooltip("케이지가 착지할 때(소리·흔들림)")] private FeedbackDataSO landingFeedback;

    private bool revealed;
    private bool dockPending;
    private int run;
    private Vector3[] bufferHomes;
    private float bufferKick = -1f;
    private SoundHandle revealLoop;

    public bool IsRevealed => revealed;
    public bool IsPlaying { get; private set; }
    public Transform Focus => focus != null ? focus : transform;
    public FieldElevatorCar Car => car;
    public Transform CarSlot => carSlot;
    public FieldElevatorTrigger Boarding => boarding;
    public FieldElevatorRide Ride => ride;
    public Room TargetRoom => targetRoom;
    public Animator BayAnimator => bayAnimator;
    public RoomTransitionEffectSO RideTransition => rideTransition;
    public WaveDataSO FallbackWaveData => fallbackWaveData;
    public string DestinationTrainingId => destinationTrainingId;
    public float FloorY => transform.position.y;
    public float CeilingBottomY => transform.position.y + hatchHeight;
    public float CeilingTopY => transform.position.y + hatchHeight + ceilingThickness;
    public float RevealStartHeight => revealStartHeight;
    /// <summary>케이지가 정박 자리에 붙어 있고 등장한 상태인가.</summary>
    public bool IsDocked => revealed && car != null && carSlot != null && car.transform.parent == carSlot;
    /// <summary>정박한 케이지가 문을 열거나 탑승하는 입력을 받을 수 있는가.</summary>
    public bool CanInteractAtDock => isActiveAndEnabled && IsDocked && !IsPlaying && car.isActiveAndEnabled && car.DoorsSettled
                                    && ride != null && ride.isActiveAndEnabled && !ride.IsRiding
                                    && RoomTransitionManager.Instance != null && !RoomTransitionManager.Instance.IsTransitioning;
    /// <summary>지금 탑승해 출발할 수 있는가.</summary>
    public bool CanBoard => CanInteractAtDock && car.DoorsOpen;

    private void Awake()
    {
        if (bayAnimator == null) bayAnimator = GetComponent<Animator>();
        CacheBuffers();
    }

    // 켜지는 도중에는 부모를 바꿀 수 없으므로, 케이지가 다른 곳(이동 장치)에 남아 있으면 첫 Update에서 정박시킨다.
    private void OnEnable() => ApplyVisualState(false);

    private void OnDisable()
    {
        // 연출 도중 꺼져(방을 떠남·Scene 종료) 코루틴이 멈춰도 등장 상태로 확정한다.
        if (IsPlaying) Finish();
        StopRevealLoop();
        ResetBuffers();
    }

    private void Update()
    {
        if (dockPending) ApplyVisualState(true);
        if (bufferKick < 0f || bufferHomes == null) return;
        bufferKick += Time.deltaTime;
        float k = Mathf.Clamp01(bufferKick / bufferTime);
        // 빠르게 눌렸다가(앞 20%) 천천히 돌아온다.
        float depth = k < .2f ? k / .2f : 1f - Ease((k - .2f) / .8f);
        for (int i = 0; i < buffers.Length; i++)
            if (buffers[i] != null) buffers[i].localPosition = bufferHomes[i] + Vector3.down * bufferStroke * depth;
        if (k >= 1f) { bufferKick = -1f; ResetBuffers(); }
    }

    // ===================== 상태 =====================

    /// <summary>등장한 상태(케이지가 내려와 문이 닫혀 있음)와 숨긴 상태(해치 닫힘·케이지 없음)를 연출 없이 세운다. 꺼져 있으면 켜질 때 적용한다.</summary>
    public void ApplyRevealed(bool value)
    {
        revealed = value && ResolveDestination() != null;
        // 케이지가 아직 이동 장치에 있으면(교장에서 떠나는 도중에 돌아옴) 먼저 정박 자리로 돌려 둔다. 꺼지는 중에는 계층을 바꾸지 않는다.
        if (gameObject.activeInHierarchy && ride != null && ride.HoldsCar && !ride.IsRiding) ride.ForceReturnCar();
        if (isActiveAndEnabled) ApplyVisualState();
    }

    private void ApplyVisualState(bool reparent = true)
    {
        dockPending = false;
        if (IsPlaying) return;
        if (bayAnimator != null && bayAnimator.isActiveAndEnabled && bayAnimator.isInitialized)
        {
            bayAnimator.SetBool(OpenId, revealed);
            bayAnimator.SetBool(AlarmId, false);
            bayAnimator.Play(revealed ? OpenedState : ClosedState, 0, 0f);
            bayAnimator.Update(0f);
        }
        if (revealed) dockPending = !DockCar(reparent);
        else if (car != null && carSlot != null && car.transform.parent == carSlot && car.gameObject.activeSelf) car.gameObject.SetActive(false);
        if (boarding != null && boarding.gameObject.activeSelf != revealed) boarding.gameObject.SetActive(revealed);
    }

    /// <summary>해치 경고등(Animator Alarm). 이동 장치가 출발할 때 켜고 케이지가 천장을 지나면 끈다.</summary>
    public void SetAlarm(bool on)
    {
        if (bayAnimator != null && bayAnimator.isActiveAndEnabled && bayAnimator.isInitialized) bayAnimator.SetBool(AlarmId, on);
    }

    /// <summary>착지 완충기 머리를 눌렀다가 되돌린다(이동, 크기 변화 없음).</summary>
    public void KickBuffers()
    {
        CacheBuffers();
        bufferKick = 0f;
    }

    private void CacheBuffers()
    {
        if (bufferHomes != null && bufferHomes.Length == buffers.Length) return;
        bufferHomes = new Vector3[buffers.Length];
        for (int i = 0; i < buffers.Length; i++) bufferHomes[i] = buffers[i] != null ? buffers[i].localPosition : Vector3.zero;
    }

    private void ResetBuffers()
    {
        if (bufferHomes == null) return;
        for (int i = 0; i < buffers.Length && i < bufferHomes.Length; i++) if (buffers[i] != null) buffers[i].localPosition = bufferHomes[i];
    }

    /// <summary>해치 열림 Clip의 Animation Event: 0 경고, 1 볼트, 2 풀림(흔들림·먼지), 3 구동, 4 잠김.</summary>
    public void PlayBayCue(int index)
    {
        if (bayCues != null && index >= 0 && index < bayCues.Length) PlaySound(bayCues[index], Focus.position);
        if (index == 2)
        {
            if (hatchFeedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(hatchFeedback, Focus.position);
            if (hatchDust != null) { hatchDust.Clear(true); hatchDust.Play(true); }
        }
    }

    // ===================== 케이지 정박 =====================

    /// <summary>케이지를 holder 아래로 옮긴다(월드 자세 유지). 이동 장치가 출발할 때 부른다.</summary>
    internal void DetachCar(Transform holder)
    {
        if (car == null || holder == null) return;
        car.transform.SetParent(holder, true);
    }

    /// <summary>케이지를 정박 자리에 문이 닫힌 채 다시 붙인다(등장 상태일 때만 보인다). reparent가 거짓이고 케이지가 다른 부모 아래에 있으면 아무것도 하지 않고 false.</summary>
    internal bool DockCar(bool reparent = true)
    {
        if (car == null || carSlot == null) return true;
        var t = car.transform;
        if (t.parent != carSlot)
        {
            if (!reparent) return false;
            t.SetParent(carSlot, false);
        }
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        car.SetDoors(false, true);
        car.SetCableTop(cableTop != null ? cableTop.position : (Vector3?)null);
        car.SetBeacons(false);
        car.SetPanel(FieldElevatorCar.PanelMode.Idle);
        if (car.gameObject.activeSelf != revealed) car.gameObject.SetActive(revealed);
        return true;
    }

    internal void ClearCallAfterRide()
    {
        revealed = false;
        if (boarding != null) boarding.gameObject.SetActive(false);
    }

    // ===================== 등장 연출 =====================

    /// <summary>천장 해치가 열리고 케이지가 내려오는 등장 연출. 끝나면 등장 상태로 확정한다. 이미 등장했으면 바로 끝난다.</summary>
    public IEnumerator PlayReveal()
    {
        if (IsPlaying || revealed || car == null || carSlot == null) yield break;
        int id = ++run;
        IsPlaying = true;
        try
        {
            // 닫힌 해치에서 시작한다: 케이지는 화면 위 밖에서 문을 닫고 기다린다.
            if (bayAnimator != null && bayAnimator.isInitialized)
            {
                bayAnimator.SetBool(OpenId, false);
                bayAnimator.SetBool(AlarmId, false);
                bayAnimator.Play(ClosedState, 0, 0f);
                bayAnimator.Update(0f);
            }
            if (boarding != null) boarding.gameObject.SetActive(false);
            var t = car.transform;
            if (t.parent != carSlot) t.SetParent(carSlot, false);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            t.localPosition = new Vector3(0f, revealStartHeight, 0f);
            car.gameObject.SetActive(false);

            // 1) 경고등·경고음 → 빗장이 빠지고 판이 내려앉았다가 좌우로 갈라진다(Animator의 Opening Clip, Event로 소리).
            if (bayAnimator != null) { bayAnimator.SetBool(AlarmId, true); bayAnimator.SetBool(OpenId, true); }
            float time = 0f;
            while (time < openingDuration)
            {
                if (!Running(id)) yield break;
                time += Time.deltaTime;
                yield return null;
            }

            // 2) 케이블에 매달린 케이지가 내려온다(가속 → 일정 속도 → 감속, 화면 위 밖에서 출발).
            car.gameObject.SetActive(true);
            car.SetDoors(false, true);
            car.SetBeacons(true);
            car.SetPanel(FieldElevatorCar.PanelMode.Down);
            car.SetCableTop(cableTop != null ? cableTop.position : (Vector3?)null);
            revealLoop = PlayLoop(motorLoop, t);
            float height = revealStartHeight, speed = 0f;
            while (height > 0f)
            {
                if (!Running(id)) yield break;
                float dt = Time.deltaTime;
                float limit = Mathf.Min(revealSpeed, Mathf.Sqrt(2f * revealBraking * height));
                speed = speed < limit ? Mathf.Min(limit, speed + revealAcceleration * dt) : limit;
                float step = Mathf.Min(height, Mathf.Max(speed, .05f) * dt);
                height -= step;
                if (height < .0005f) height = 0f;
                t.localPosition = new Vector3(0f, height, 0f);
                car.AddTravel(-step);
                car.SetCableTop(cableTop != null ? cableTop.position : (Vector3?)null);
                yield return null;
            }

            // 3) 착지: 완충기가 눌리고 먼지가 일고 흔들린다.
            StopRevealLoop();
            if (landingFeedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(landingFeedback, t.position);
            car.PlayLandingDust();
            car.SetBeacons(false);
            car.SetPanel(FieldElevatorCar.PanelMode.Idle);
            KickBuffers();
            time = 0f;
            while (time < landingSettle) { if (!Running(id)) yield break; time += Time.deltaTime; yield return null; }

            // 4) 경고등을 끄고 착지 상태를 확정한다.
            SetAlarm(false);
            PlaySound(readySound, t.position);
            revealed = true;
            if (boarding != null) boarding.gameObject.SetActive(true);
            time = 0f;
            while (time < finishHold) { if (!Running(id)) yield break; time += Time.deltaTime; yield return null; }
        }
        finally
        {
            if (run == id) Finish();
        }
    }

    // 도중에 끝났거나(OnDisable) 밖에서 상태를 바꿨으면 멈춘다.
    private bool Running(int id) => IsPlaying && run == id && isActiveAndEnabled;

    private void Finish()
    {
        if (!IsPlaying) return;
        IsPlaying = false;
        StopRevealLoop();
        ResetBuffers();
        bufferKick = -1f;
        ApplyRevealed(true);
    }

    // ===================== 탑승 =====================

    /// <summary>훈련 목록에서 전투 훈련 WaveData를 찾는다. 목록이 있으면 등록된 목적지만 사용한다.</summary>
    public WaveDataSO ResolveDestination()
    {
        var catalog = TrainingCatalogSO.Load();
        return catalog != null
            ? (catalog.ElevatorTraining ?? catalog.Find(destinationTrainingId))?.waveData
            : fallbackWaveData;
    }

    /// <summary>탑승해 전투 훈련장으로 출발한다(방 전환을 요청). 출발하지 못하면 false.</summary>
    public bool RequestRide()
    {
        if (!CanBoard) return false;
        var host = targetRoom != null ? targetRoom.GetComponent<TrainingRoomHost>() : null;
        var data = ResolveDestination();
        if (host == null || host.entryPoint == null || data == null || rideTransition == null)
        {
            Debug.LogError("[FieldElevator] 목적지 방(TrainingRoomHost·입장 지점)·WaveData·전환 효과 가운데 빠진 것이 있어 출발하지 못했습니다.", this);
            return false;
        }
        ride.Arm(this);
        RoomTransitionManager.Instance.RequestTransition(new RoomTransitionRequest
        {
            targetRoom = targetRoom,
            waveData = data,
            teleportPoint = host.entryPoint,
            transitionEffect = rideTransition,
        });
        bool started = RoomTransitionManager.Instance.IsTransitioning;
        if (!started) ride.Disarm(this);
        return started;
    }

    [ContextMenu("시험: 등장 상태로 두기")]
    private void TestReveal() => ApplyRevealed(true);
    [ContextMenu("시험: 숨김 상태로 두기")]
    private void TestHide() => ApplyRevealed(false);
    [ContextMenu("시험: 바로 탑승(전투 훈련로 이동)")]
    private void TestRide()
    {
        if (!revealed) ApplyRevealed(true);
        if (!CanInteractAtDock) return;
        car.SetDoors(true, true);
        RequestRide();
    }

    // ===================== 도우미 =====================

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private void StopRevealLoop()
    {
        if (revealLoop != null && SoundManager.Instance != null) SoundManager.Instance.StopLoop(revealLoop);
        revealLoop = null;
    }

    private static SoundHandle PlayLoop(SoundOptions sound, Transform follow)
    {
        if (sound == null || sound.clip == null || SoundManager.Instance == null) return null;
        return SoundManager.Instance.PlayLoop(sound, follow.position, follow);
    }

    private static void PlaySound(SoundOptions sound, Vector3 position)
    {
        if (sound != null && sound.clip != null && SoundManager.Instance != null) SoundManager.Instance.PlaySound(sound, position);
    }
}
