using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 각개 전투 엘리베이터의 실시간 이동(2026-11-20). 사용자 요청: "탑승하면, 각개전투 교장으로 이동하고, 이때는 텔레포트하는게 아니라
/// 엘리베이터를 타고 실시간으로 이동하는 연출을 해줘. 이 때 배경은 흰색 + 검은색 조합으로 만들고, 분위기가 완전 딴판이어야해. 배경은 어두워서 잘 안보여야해."
/// - MainScene 루트에 늘 켜져 있고, 이동할 때만 표시 장치(Rig: 어두운 흑백 수직 갱도 두 개·위쪽 분기점과 트롤리·바깥 채움·이동 카메라)를 켠다.
///   방은 한 번에 하나만 켜지고 방을 떠나면 방 아래가 모두 꺼지므로, 이동 중에 보여야 할 것은 방 밖(여기)에 둔다.
/// - 흐름: 탑승(Player를 케이지 가운데로) → 문 닫힘 → Lobby 천장 해치로 상승(갱도 A) → 분기점에 멈춘 순간(경고등·표시판·집게도 멈춘 정지 화면) 방을 바꾸고 갱도·케이지·Player·카메라를
///   같은 프레임에 교장 위쪽으로 옮김(화면은 그대로) → 트롤리가 옆 갱도 B로 옮김 → 교장 천장을 지나 Wave 1 시작 좌표에 착지 → 문 열림·해제·Wave 시작 → 케이지가 떠남.
///   암전·텔레포트 효과를 쓰지 않는다. 입장 로딩은 분기점·가로 이송·하강 동안 진행되고(로딩 표시는 숨긴다), 끝나지 않았으면 천장 위 대기점에서 멈춰 기다린다.
/// - 물리 규칙: Player를 직접 옮기지 않는다. 모터의 이동 구속(ConfineAroundBody → 물리 틱마다 ShiftConfinement → ClearConfinement)으로만 실어 나른다
///   (WavePlayerStart의 크레인과 같은 방식). 방을 바꾸며 ChangeRoom이 순간이동시킨 자리는 같은 프레임에 ResetPlayerPosition(RefitConfinement 포함)으로 맞춘다.
/// - 케이지·카메라 기준점은 구속 영역을 물리 틱마다 표본으로 모아 FixedMotionBody와 같은 비율로 보간한 자리에 둔다(Player 그림과 케이지 바닥이 어긋나지 않는다).
/// 전환은 ElevatorRideTransitionSO가 Begin으로 시작한다. 정리(Cleanup)는 finally와 OnDisable이 함께 부르며 여러 번 불러도 같다.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(98)]
public sealed class FieldElevatorRide : MonoBehaviour
{
    public enum Phase { Idle, Boarding, Ascending, Junction, Transferring, Descending, Holding, Landing, Releasing, Departing }

    [Header("구성")]
    [SerializeField, Tooltip("Lobby의 엘리베이터 정박대")] private FieldElevator elevator;
    [SerializeField, Tooltip("이동 중에만 켜는 표시 장치")] private GameObject rig;
    [SerializeField, Tooltip("이동 중 케이지가 붙는 부모")] private Transform carHolder;
    [SerializeField] private Transform columnA, columnB, trolley, trolleySheave, clampL, clampR, counterweightA, counterweightB;
    [SerializeField] private Transform[] trolleyWheels = new Transform[0];
    [SerializeField] private LineRenderer counterweightRopeA, counterweightRopeB;
    [SerializeField, Tooltip("실행 중 만드는 바깥 채움(방 밖을 어둡게 덮는 사각형들)")] private MeshFilter exteriorFill;
    [SerializeField] private CinemachineCamera rideCamera;
    [SerializeField, Tooltip("이동 카메라가 따라가는 기준점")] private Transform cameraAnchor;
    [SerializeField, Tooltip("이동 카메라 범위(실행 중 월드 사각형으로 맞춘다)")] private BoxCollider2D rideBounds;

    [Header("갱도 치수(칸)")]
    [SerializeField, Min(4f), Tooltip("천장 윗면에서 분기점까지의 갱도 높이(양쪽 같음)")] private float columnHeight = 20f;
    [SerializeField, Tooltip("분기점에서 옆 갱도까지의 가로 거리")] private float transferOffset = 8f;
    [SerializeField, Min(0f), Tooltip("교장 천장 두께(실내 윗변 → 천장 윗면)")] private float fieldCeilingThickness = 3f;
    [SerializeField, Min(1f)] private float fillHalfWidth = 30f;
    [SerializeField, Min(0f)] private float fillTopMargin = 12f;
    [SerializeField, Min(0f)] private float fillBottomMargin = 6f;
    [SerializeField, Tooltip("케이지를 들어 집게에 물리는 높이")] private float liftHeight = .3f;
    [SerializeField, Tooltip("평형추의 가장 낮은·높은 자리(갱도 바닥 기준)")] private Vector2 counterweightRange = new Vector2(2.5f, 22.5f);
    [SerializeField, Tooltip("레일 이음매 간격(이만큼 지날 때마다 틱 소리)")] private float beamSpacing = 4f;
    [SerializeField, Min(.05f)] private float wheelRadius = .2f;

    [Header("움직임")]
    [SerializeField, Min(.5f), Tooltip("최고 속도(칸/초)")] private float cruiseSpeed = 14f;
    [SerializeField, Min(.5f)] private float acceleration = 14f;
    [SerializeField, Min(.5f)] private float ascentBraking = 11.7f;
    [SerializeField, Min(.5f)] private float descentBraking = 9f;
    [SerializeField, Min(0f), Tooltip("입장 로딩이 남았을 때 교장 천장 윗면에서 이만큼 위에서 멈춰 기다린다")] private float holdClearance = 4f;
    [SerializeField, Min(.05f)] private float boardTime = .45f;
    [SerializeField, Min(0f)] private float spinUpTime = .4f;
    [SerializeField, Min(0f)] private float junctionHold = .5f;
    [SerializeField, Min(.05f)] private float clampTime = .3f;
    [SerializeField, Min(.1f)] private float transferTime = 1.6f;
    [SerializeField, Min(0f)] private float landingSettle = .4f;
    [SerializeField, Min(.1f)] private float doorTime = .6f;
    [SerializeField, Min(0f), Tooltip("착지·해제 뒤 케이지가 떠나기 시작할 때까지")] private float departDelay = .6f;
    [SerializeField, Min(.5f)] private float departSpeed = 10f;
    [SerializeField, Min(.5f)] private float departAcceleration = 12f;
    [SerializeField, Min(0f), Tooltip("떠나는 케이지가 교장 천장 윗면에서 이만큼 올라가면 장치를 끈다")] private float departClearance = 8f;
    [SerializeField, Min(0f)] private float sideMargin = .12f, topMargin = .12f;

    [Header("카메라")]
    [SerializeField] private int ridePriority = 40;
    [SerializeField] private int idlePriority = -1;
    [SerializeField, Min(1f)] private float rideLens = 7f;
    [SerializeField, Tooltip("올라갈 때 카메라가 케이지 바닥보다 위에 둘 높이(위를 보이게)")] private float ascentLift = 4f;
    [SerializeField, Tooltip("내려갈 때 카메라 높이(아래를 보이게)")] private float descentLift = 1f;
    [SerializeField, Min(.5f), Tooltip("케이지 바닥이 교장 천장 윗면에서 이만큼 위로 내려오면 교장 카메라 화면으로 맞춰 가기 시작한다")] private float framingStart = 8f;
    [SerializeField, Min(0f), Tooltip("교장 카메라로 넘긴 뒤 장치를 끄기까지 기다리는 시간(블렌드 1초보다 길게)")] private float blendOutWait = 1.1f;

    [Header("소리")]
    [SerializeField] private SoundOptions spinUpSound;
    [SerializeField, Tooltip("이동 중 반복음(케이지를 따라감)")] private SoundOptions motorLoop;
    [SerializeField, Tooltip("가로 이송 중 반복음")] private SoundOptions transferLoop;
    [SerializeField, Tooltip("레일 이음매를 지날 때")] private SoundOptions railTick;
    [SerializeField, Tooltip("집게가 지붕을 물 때")] private SoundOptions clampSound;
    [SerializeField, Tooltip("집게가 풀릴 때")] private SoundOptions releaseSound;
    [SerializeField, Tooltip("가로 이송을 시작할 때")] private SoundOptions transferSound;
    [SerializeField, Tooltip("분기점에서 걸쇠가 물릴 때(소리·흔들림)")] private FeedbackDataSO latchFeedback;
    [SerializeField, Tooltip("교장 바닥에 착지할 때(소리·흔들림)")] private FeedbackDataSO landingFeedback;
    [SerializeField, Min(0f), Tooltip("움직이는 동안의 지속 흔들림 세기")] private float rideShake = .1f;

    [Header("BGM")]
    [SerializeField, Range(0f, 1f), Tooltip("출발할 때 Lobby BGM을 멀리 들리게 줄이는 음량")] private float lobbyBgmDistanceVolume = .12f;
    [SerializeField, Tooltip("출발할 때 Lobby BGM의 고음 상한(Hz)")] private float lobbyBgmCutoff = 700f;
    [SerializeField, Min(0f)] private float lobbyBgmRamp = 2.5f;
    [SerializeField, Min(0f), Tooltip("방을 바꿀 때 Lobby BGM을 줄여 멈추는 시간")] private float lobbyBgmFadeOut = 1.5f;

    public static FieldElevatorRide Instance { get; private set; }

    public Phase CurrentPhase { get; private set; } = Phase.Idle;
    /// <summary>탑승부터 구속 해제·Wave 시작까지.</summary>
    public bool IsRiding { get; private set; }
    /// <summary>케이지가 이동 장치에 붙어 있는가(떠나는 연출 포함).</summary>
    public bool HoldsCar => elevator != null && elevator.Car != null && carHolder != null && elevator.Car.transform.parent == carHolder;
    public bool HasSwitchedRoom { get; private set; }
    public bool WavesBegun { get; private set; }
    public int SwitchFrame { get; private set; } = -1;
    /// <summary>분기점에서 방을 바꾸는 데 걸린 시간(ms, 방 전환·방 내용 불러오기·옮기기). 아직 바꾸지 않았으면 -1. 검증에서 끊김을 잰다.</summary>
    public float SwitchMilliseconds { get; private set; } = -1f;
    public float CarSpeed { get; private set; }
    public Vector3 LastRelocation { get; private set; }
    public float LandingY { get; private set; }
    public float FieldCeilingTop { get; private set; }
    public float HoldClearance => holdClearance;
    public float HoldSeconds { get; private set; }
    public bool LoadingPanelHidden { get; private set; }
    public Vector2 LobbyOrigin { get; private set; }
    public Vector2 FieldOrigin { get; private set; }
    public float CruiseSpeed => cruiseSpeed;
    public CinemachineCamera RideCamera => rideCamera;
    public Transform CameraAnchor => cameraAnchor;
    public BoxCollider2D RideBounds => rideBounds;
    public GameObject Rig => rig;
    public FieldElevator Elevator => elevator;

    private FieldElevator armed, source;
    private PlayerController player;
    private RaycastMotor2D motor;
    private Room room;
    private TrainingRoomHost host;
    private Coroutine rideRoutine, departRoutine;
    private Rect previousArea, currentArea;
    private double sampleTime;
    private bool following;
    private float transferProgress;
    private float playerAboveFloor = .75f;
    private Vector2 trainingOffset;
    private Rect interior;
    private float railDistance;
    private float clampAngle = 60f, clampTarget = 60f;
    private const float ClampOpen = 60f, ClampClosed = 0f;
    private float trolleyHomeY;
    private bool trolleyHomeKnown;
    private bool parkPending;

    // 이 이동이 맡은 것(정리할 때 되돌린다)
    private bool ownsUi, previousPause;
    private bool suppressOwned, previousSuppress;
    private bool confined;
    private readonly List<RoomBgmPlayer> mutedBgm = new List<RoomBgmPlayer>();
    // 방 내용마다 한 번만 찾은 BGM 플레이어(같은 내용을 같은 프레임에 다시 훑지 않는다)
    private GameObject bgmContent;
    private RoomBgmPlayer contentBgm;
    private SoundHandle loop;
    private CinemachineImpulseManager.ImpulseEvent shake;
    private Mesh fillMesh;
    private readonly List<Vector3> fillVertices = new List<Vector3>(24);
    private readonly List<Color> fillColors = new List<Color>(24);
    private readonly List<int> fillTriangles = new List<int>(36);
    private readonly Vector3[] ropePoints = new Vector3[2];
    private static readonly Color VoidColor = new Color(.003f, .003f, .003f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    private void Awake()
    {
        if (Instance == null || Instance == this) Instance = this;
        if (rig != null && rig.activeSelf) rig.SetActive(false);
        if (rideCamera != null) rideCamera.Priority = idlePriority;
        CacheTrolleyHome();
    }

    private void OnEnable()
    {
        if (Instance == null) Instance = this;
        // 꺼지는 동안 계층을 바꾸지 못해 남겨 둔 케이지는 다시 켜진 뒤 첫 LateUpdate에서 정박 자리로 돌려 둔다(켜지는 도중에는 계층을 바꾸지 않는다).
    }

    private void OnDisable()
    {
        // 멈춘 코루틴의 finally는 돌지 않으므로 여기서 정리한다(이동 중이면 중단으로, 떠나는 중이면 케이지를 정박 자리로).
        // 개체째 꺼지는 중에는 부모를 바꾸거나 자식을 끌 수 없으므로 계층은 그대로 두고, 다음에 켜지거나 Lobby에 돌아올 때 정박시킨다.
        rideRoutine = null;
        departRoutine = null;
        bool hierarchy = gameObject.activeInHierarchy;
        if (IsRiding) Cleanup(true, hierarchy);
        else if (HoldsCar || (rig != null && rig.activeSelf)) ParkRig(hierarchy);
        armed = null;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (fillMesh != null) Destroy(fillMesh);
        fillMesh = null;
    }

    // ===================== 시작 =====================

    /// <summary>다음 Begin이 쓸 출발지(탑승을 요청한 정박대)를 정한다.</summary>
    internal void Arm(FieldElevator from) => armed = from;
    internal void Disarm(FieldElevator from) { if (armed == from) armed = null; }

    /// <summary>
    /// 전환 효과(ElevatorRideTransitionSO)가 부른다. 이동을 시작했으면 true. 이동 장치·Player·목적지 방의 TrainingRoomHost가 없거나 이미 이동 중이면 false
    /// (그러면 전환 효과가 보통 입장으로 방을 바꾼다).
    /// </summary>
    public bool Begin(RoomTransitionContext context)
    {
        var from = armed;
        armed = null;
        if (IsRiding || from == null || !isActiveAndEnabled || context == null || context.request == null || context.request.targetRoom == null) return false;
        var target = context.request.targetRoom;
        var targetHost = target.GetComponent<TrainingRoomHost>();
        var who = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (targetHost == null || targetHost.entryPoint == null || who == null || rig == null || carHolder == null || rideCamera == null || from.Car == null) return false;
        // 지난 이동의 떠나는 연출이 남아 있으면 먼저 멈춘다. 남겨 두면 새 이동 도중 케이지를 계속 밀어 올리고, 끝의 finally가 장치를 끄거나
        // 케이지를 Lobby로 되돌린다. 멈춘 코루틴의 finally는 돌지 않으므로 정리는 아래 ParkRig가 맡는다.
        bool leftover = departRoutine != null || HoldsCar || parkPending;
        if (departRoutine != null) { StopCoroutine(departRoutine); departRoutine = null; }
        if (leftover) ParkRig();
        source = from;
        room = target;
        host = targetHost;
        player = who;
        motor = who.Motor;
        IsRiding = true;
        HasSwitchedRoom = false;
        WavesBegun = false;
        LoadingPanelHidden = false;
        SwitchFrame = -1;
        SwitchMilliseconds = -1f;
        HoldSeconds = 0f;
        CarSpeed = 0f;
        LastRelocation = Vector3.zero;
        rideRoutine = StartCoroutine(RideRoutine(context));
        return true;
    }

    // ===================== 이동 =====================

    private IEnumerator RideRoutine(RoomTransitionContext context)
    {
        bool completed = false;
        try
        {
            // ---- 1. 준비: 일시정지·HUD는 이 이동이 맡는다(게임 입력 막기는 전환 관리자가 이미 걸었다) ----
            CurrentPhase = Phase.Boarding;
            var ui = UIManager.Instance;
            if (ui != null)
            {
                previousPause = ui.PauseInputBlocked;
                ui.PauseInputBlocked = true;
                ui.HUDVisible(false);
                ownsUi = true;
            }
            if (player.AbilityManager != null) player.AbilityManager.InterruptAllAbilities();
            player.Velocity = Vector2.zero;
            player.externalForce = Vector2.zero;

            // ---- 2. 갱도 장치를 Lobby 위(천장 윗면)에 켠다 ----
            var car = source.Car;
            LobbyOrigin = new Vector2(source.transform.position.x, source.CeilingTopY);
            rig.transform.position = new Vector3(LobbyOrigin.x, LobbyOrigin.y, rig.transform.position.z);
            clampAngle = clampTarget = ClampOpen;
            transferProgress = 0f;
            railDistance = 0f;
            var lobbyBox = LobbyBounds();
            float leftFill = Mathf.Min(-fillHalfWidth, lobbyBox.xMin - LobbyOrigin.x), rightFill = Mathf.Max(transferOffset + fillHalfWidth, lobbyBox.xMax - LobbyOrigin.x);
            var lobbyFill = Rect.MinMaxRect(leftFill, 0f, rightFill, columnHeight + fillTopMargin);
            BuildFill(lobbyFill);
            SetBounds(Union(lobbyBox, Offset(lobbyFill, LobbyOrigin)));
            rig.SetActive(true);
            CacheTrolleyHome();
            source.DetachCar(carHolder);
            PlaceTrolley(0f);
            following = false;
            var lobbyCamera = GameManager.Instance.LobbyRoom != null ? GameManager.Instance.LobbyRoom.VirtualCamera : null;
            if (lobbyCamera != null)
            {
                var lens = lobbyCamera.Lens;
                lens.OrthographicSize = rideLens;
                rideCamera.Lens = lens;
            }
            else rideCamera.Lens.OrthographicSize = rideLens;
            UpdateAnchor();
            rideCamera.PreviousStateIsValid = false;
            rideCamera.Priority = ridePriority;
            source.SetAlarm(true);

            // ---- 3. 탑승: 구속 영역을 케이지 가운데 바닥으로 옮긴다(걸어 들어가지 않는다) ----
            motor.ConfineAroundBody(sideMargin, topMargin);
            confined = true;
            ResetSamples(motor.Confinement);
            playerAboveFloor = player.transform.position.y - currentArea.yMin;
            Vector2 carFloor = car.transform.position;
            for (var step = Haul(new Vector2(currentArea.center.x, currentArea.yMin), new Vector2(carFloor.x, source.FloorY), boardTime, false); step.MoveNext();)
                yield return step.Current;
            following = true;

            // ---- 4. 문 닫힘 ----
            car.SetDoors(false);
            for (float t = 0f; t < doorTime + .3f && !(t > .05f && car.DoorsSettled); t += Time.deltaTime) yield return null;

            // ---- 5. 출발 준비: 경고등·표시판·구동음·흔들림, Lobby BGM은 멀리 들리게 ----
            PlaySound(spinUpSound, car.transform.position);
            car.SetBeacons(true);
            car.SetPanel(FieldElevatorCar.PanelMode.Up);
            StartMotor(motorLoop, car.transform);
            StartShake(car.transform.position);
            var sound = SoundManager.Instance;
            if (sound != null && sound.CurrentBgmClip != null) sound.SetBGMDistance(sound.CurrentBgmClip, lobbyBgmDistanceVolume, lobbyBgmCutoff, lobbyBgmRamp);
            for (float t = 0f; t < spinUpTime; t += Time.deltaTime) yield return null;

            // ---- 6. 상승: Lobby 천장 해치를 지나 갱도 A 꼭대기(분기점)까지 ----
            CurrentPhase = Phase.Ascending;
            float junctionY = LobbyOrigin.y + columnHeight;
            bool alarmOff = false;
            for (var step = Travel(1f, () => junctionY, ascentBraking, null); step.MoveNext();)
            {
                if (!alarmOff && currentArea.yMin > source.CeilingTopY + 6f) { alarmOff = true; source.SetAlarm(false); }
                yield return step.Current;
            }
            if (!alarmOff) source.SetAlarm(false);

            // ---- 7. 분기점 도착: 반복음·흔들림을 끊고 경고등·표시판도 끈다(멈춘 동안 화면에 움직이는 부품이 없게). Update 단계로 넘어가 방을 바꾼다 ----
            CurrentPhase = Phase.Junction;
            CarSpeed = 0f;
            StopMotor();
            EndShake();
            car.SetBeacons(false);
            car.SetPanel(FieldElevatorCar.PanelMode.Idle);
            yield return null;

            // ---- 8. 방 전환(같은 프레임에 장치·케이지·Player·카메라를 교장 위쪽으로 옮긴다. 화면은 바뀌지 않는다) ----
            // 방 내용을 동기로 불러오면 이 프레임이 길어진다. 이 프레임과 긴 Δt를 받는 다음 프레임에는 움직이는 부품(집게·경고등·표시판·흔들림)이
            // 없게 해 멈춤이 정지 화면에 묻히게 한다. 걸쇠·집게는 그다음 프레임부터 움직인다(긴 Δt로 집게가 한 번에 닫히지 않게).
            SwitchRoom(context);
            yield return null;
            yield return null;

            // ---- 9. 분기점 대기: 걸쇠가 물리고(소리·흔들림) 집게가 지붕을 문다 ----
            if (latchFeedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(latchFeedback, car.transform.position);
            PlaySound(clampSound, car.transform.position);
            clampTarget = ClampClosed;
            for (float t = 0f; t < junctionHold; t += Time.deltaTime) { ShowLoading(car); yield return null; }

            // ---- 10. 들기 → 가로 이송(갱도 A → B) → 내리기(다시 움직이므로 경고등을 켠다) ----
            CurrentPhase = Phase.Transferring;
            car.SetBeacons(true);
            Vector2 atJunction = new Vector2(currentArea.center.x, currentArea.yMin);
            Vector2 lifted = atJunction + Vector2.up * liftHeight;
            Vector2 across = lifted + Vector2.right * transferOffset;
            for (var step = Haul(atJunction, lifted, clampTime, false); step.MoveNext();) { ShowLoading(car); yield return step.Current; }
            PlaySound(transferSound, car.transform.position);
            StartMotor(transferLoop != null && transferLoop.clip != null ? transferLoop : motorLoop, car.transform);
            StartShake(car.transform.position);
            for (var step = Haul(lifted, across, transferTime, true); step.MoveNext();) { ShowLoading(car); yield return step.Current; }
            transferProgress = 1f;
            StopMotor();
            EndShake();
            for (var step = Haul(across, across - Vector2.up * liftHeight, clampTime, false); step.MoveNext();) { ShowLoading(car); yield return step.Current; }
            clampTarget = ClampOpen;
            PlaySound(releaseSound, car.transform.position);

            // ---- 11. 하강: 교장 천장을 지나 Wave 1 시작 좌표의 바닥까지(로딩이 남으면 천장 위 대기점에서 기다린다) ----
            CurrentPhase = Phase.Descending;
            car.SetPanel(FieldElevatorCar.PanelMode.Down);
            car.SetBeacons(true);
            StartMotor(motorLoop, car.transform);
            StartShake(car.transform.position);
            LandingY = ResolveLandingY();
            bool held = false;
            float holdPoint = FieldCeilingTop + holdClearance, nextWarning = 15f;
            for (var step = Travel(-1f, () => LandingY, descentBraking, () => host.IsLoading ? holdPoint : (float?)null); step.MoveNext();)
            {
                if (host.IsLoading)
                {
                    held = true;
                    car.SetPanel(FieldElevatorCar.PanelMode.Hold, host.LoadingProgress);
                    if (CurrentPhase == Phase.Holding)
                    {
                        HoldSeconds += Time.fixedDeltaTime;
                        if (HoldSeconds > nextWarning)
                        {
                            nextWarning += 15f;
                            Debug.LogWarning($"[FieldElevatorRide] 입장 로딩을 {HoldSeconds:0}초째 기다리는 중입니다(진행률 {host.LoadingProgress:P0}).", this);
                        }
                    }
                }
                else if (held)
                {
                    // 로딩이 끝나면 지형이 다 준비된 자리에서 내릴 바닥을 다시 찾고 내려간다.
                    held = false;
                    LandingY = ResolveLandingY();
                    car.SetPanel(FieldElevatorCar.PanelMode.Down);
                }
                yield return step.Current;
            }

            // ---- 12. 착지: 흔들림·먼지 → 잠시 뒤 문 열림 ----
            CurrentPhase = Phase.Landing;
            CarSpeed = 0f;
            StopMotor();
            EndShake();
            if (landingFeedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(landingFeedback, car.transform.position);
            car.PlayLandingDust();
            car.SetBeacons(false);
            car.SetPanel(FieldElevatorCar.PanelMode.Idle);
            for (float t = 0f; t < landingSettle; t += Time.deltaTime) yield return null;
            car.SetDoors(true);
            for (float t = 0f; t < doorTime + .3f && !(t > .05f && car.DoorsSettled); t += Time.deltaTime) yield return null;

            // ---- 13. 해제: 구속을 풀고 교장 카메라·HUD·BGM을 돌려준 뒤 Wave를 시작한다 ----
            Release();
            completed = true;
        }
        finally
        {
            rideRoutine = null;
            if (!completed) Cleanup(true);
        }
    }

    // 분기점에 멈춘 프레임(Update 단계)에서 방을 바꾸고, ChangeRoom이 옮긴 것과 같은 프레임에 모두 교장 위쪽으로 옮긴다.
    // 이 프레임에는 동기 방 내용 불러오기가 몰리므로 방 전체를 훑는 검색(GetComponentsInChildren·Find…ByType)을 하지 않는다.
    private void SwitchRoom(RoomTransitionContext context)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        previousSuppress = room.SuppressAutomaticWaves;
        room.SuppressAutomaticWaves = true;
        suppressOwned = true;
        // 이미 있는 방 내용의 BGM 플레이어는 미리 끄고, 새로 만들어지는 내용의 것은 방 전환 알림에서 끈다(새 것은 이 처리기보다 나중에 구독한다).
        MuteRoomBgm(room.ContentRoot);
        var manager = RoomManager.Instance;
        var view = Camera.main;
        Vector3 cameraBefore = view != null ? view.transform.position : Vector3.zero;
        Vector3 playerBefore = player.transform.position;
        var sound = SoundManager.Instance;
        AudioClip lobbyClip = sound != null ? sound.CurrentBgmClip : null;
        manager.OnRoomChanged += HandleSwitched;
        try { context.SwitchRoom(); }
        finally { manager.OnRoomChanged -= HandleSwitched; }
        HasSwitchedRoom = manager.CurrentRoom == room;
        SwitchFrame = Time.frameCount;
        HideLoadingPanel();
        if (!HasSwitchedRoom) throw new InvalidOperationException("방 전환 뒤 지금 방이 목적지가 아닙니다.");

        // 교장 쪽 원점: Wave 1 시작 좌표(입장 지점) 위의 갱도 B 바닥 = 교장 천장 윗면. 분기점에 있는 지금은 갱도 A(그 왼쪽)에 있다.
        Vector2 entry = host.entryPoint.position;
        interior = InteriorRect();
        // 방 내용을 새로 만들었으면 방 카메라도 새것이다. 착지 화면을 맞출 때 쓸 그 카메라의 따라가기 오프셋을 여기서 읽는다.
        var roomFollow = room.VirtualCamera != null ? room.VirtualCamera.GetComponent<CinemachineFollow>() : null;
        trainingOffset = roomFollow != null ? (Vector2)roomFollow.FollowOffset : Vector2.zero;
        FieldCeilingTop = interior.yMax + fieldCeilingThickness;
        FieldOrigin = new Vector2(entry.x - transferOffset, FieldCeilingTop);
        Vector3 delta = (Vector3)(FieldOrigin - LobbyOrigin);
        rig.transform.position += delta;
        // ChangeRoom이 입장 지점으로 옮긴 Player를 같은 프레임에 케이지 안(옮긴 자리)으로 되돌리고 구속도 그 자리에 다시 잡는다.
        player.ResetPlayerPosition(playerBefore + delta);
        ResetSamples(motor.Confinement);
        LastRelocation = delta;
        PlaceCar(new Vector2(currentArea.center.x, currentArea.yMin));
        UpdateAnchor();
        CinemachineCore.OnTargetObjectWarped(cameraAnchor, delta);
        rideCamera.PreviousStateIsValid = false;
        // 이 프레임은 Brain이 꺼져 있어 카메라가 옛 자리를 그린다. 같은 양만큼 직접 옮긴다(다음 프레임부터 Brain이 이동 카메라를 그대로 비춘다).
        if (view != null) view.transform.position = cameraBefore + delta;

        // 교장 쪽 바깥 채움: 실내만 비우고 둘레를 어둡게 덮는다(벽·천장·바닥 셀은 채움보다 앞에 그려진다).
        Vector2 colB = FieldOrigin + Vector2.right * transferOffset;
        var outer = Rect.MinMaxRect(colB.x - fillHalfWidth, interior.yMin - fillBottomMargin, colB.x + fillHalfWidth, FieldCeilingTop + columnHeight + fillTopMargin);
        BuildFill(Subtract(Offset(outer, -FieldOrigin), Offset(interior, -FieldOrigin)));
        SetBounds(Union(RoomCameraBox(), outer));
        if (lobbyClip != null && sound != null) sound.FadeOutBGM(lobbyClip, lobbyBgmFadeOut);
        SwitchMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
    }

    private void HandleSwitched(RoomType _) => MuteRoomBgm(room != null ? room.ContentRoot : null);

    // 방 내용의 BGM 플레이어를 끈다. 훈련 방 내용(TrainingRoomBase)은 루트 개체에 RoomBgmPlayer를 두므로 루트만 보고,
    // 루트에 없을 때만 자식에서 첫 하나를 찾는다(방 내용 전체를 모으지 않는다). 같은 내용은 다시 찾지 않는다.
    private void MuteRoomBgm(GameObject content)
    {
        if (content == null) return;
        if (content != bgmContent)
        {
            bgmContent = content;
            contentBgm = content.GetComponent<RoomBgmPlayer>();
            if (contentBgm == null) contentBgm = content.GetComponentInChildren<RoomBgmPlayer>(true);
        }
        var bgm = contentBgm;
        if (bgm != null && bgm.enabled) { bgm.enabled = false; if (!mutedBgm.Contains(bgm)) mutedBgm.Add(bgm); }
    }

    // 입장 로딩 표시는 이동 연출 속에 묻는다: 방을 바꾼 프레임에 이 방의 로딩으로 켜진(알파 0) 표시를 바로 끈다. 한 번도 그려지지 않는다.
    // 표시는 하나뿐이고 자기 참조를 갖고 있으므로 Scene을 검색하지 않는다.
    private void HideLoadingPanel()
    {
        if (TrainingRoomLoadingPanel.Dismiss(host)) LoadingPanelHidden = true;
    }

    private void ShowLoading(FieldElevatorCar car)
    {
        if (host != null && host.IsLoading) car.SetPanel(FieldElevatorCar.PanelMode.Hold, host.LoadingProgress);
        else if (car.Panel == FieldElevatorCar.PanelMode.Hold || car.Panel == FieldElevatorCar.PanelMode.Up) car.SetPanel(FieldElevatorCar.PanelMode.Idle);
    }

    // 내려놓을 높이: 입장 지점에 선 몸의 발밑 근처에 실제 바닥이 있으면 그 위(크레인의 SettleHeight와 같은 규칙), 없으면 발 높이 그대로.
    private float ResolveLandingY()
    {
        Vector2 entry = host.entryPoint.position;
        var feet = new Vector2(entry.x, entry.y - playerAboveFloor);
        float above = Mathf.Min(1.2f, Mathf.Max(.1f, currentArea.height - topMargin));
        var floor = motor.RaycastFloorBelow(feet, above, 3f);
        return floor.collider != null && floor.distance > .02f ? floor.point.y + .012f : feet.y;
    }

    // 착지·문 열림 뒤: 구속 해제 → 교장 카메라로 넘김 → HUD·일시정지·BGM 복원 → Wave 자동 시작 막기 복원 → Wave 시작(시작 안내 포함) → 케이지 떠남.
    private void Release()
    {
        CurrentPhase = Phase.Releasing;
        if (confined && player != null)
        {
            motor.ClearConfinement();
            player.externalForce = Vector2.zero;
            player.Velocity = Vector2.zero;
        }
        confined = false;
        following = false;
        if (room.VirtualCamera != null) room.VirtualCamera.PreviousStateIsValid = false;
        rideCamera.Priority = idlePriority;
        RestoreUi();
        RestoreBgm();
        RestoreSuppress();
        if (room.WaveManager != null && RoomManager.Instance != null && RoomManager.Instance.CurrentRoom == room)
        {
            room.WaveManager.BeginWaves();
            WavesBegun = true;
        }
        IsRiding = false;
        departRoutine = StartCoroutine(DepartRoutine());
    }

    // 착지 뒤 케이지가 문을 연 채 갱도 B를 따라 올라가 천장 위로 사라진다. 그 뒤 장치를 끄고 케이지를 Lobby 정박 자리로 돌려 둔다(Lobby는 꺼져 있다).
    private IEnumerator DepartRoutine()
    {
        try
        {
            CurrentPhase = Phase.Departing;
            for (float t = 0f; t < departDelay; t += Time.deltaTime) yield return null;
            var car = elevator != null ? elevator.Car : null;
            if (car == null || !HoldsCar) yield break;
            car.SetBeacons(true);
            StartMotor(motorLoop, car.transform);
            float speed = 0f, started = Time.time;
            while (true)
            {
                // 케이지를 다른 곳(Lobby 정박 자리)이 가져갔으면 더 밀지 않고 끝낸다(finally의 ParkRig가 장치를 정리한다).
                if (!HoldsCar) yield break;
                float dt = Time.deltaTime;
                speed = Mathf.Min(departSpeed, speed + departAcceleration * dt);
                float step = speed * dt;
                car.transform.position += Vector3.up * step;
                car.AddTravel(step);
                CarSpeed = speed;
                if (car.transform.position.y > FieldCeilingTop + departClearance && Time.time - started > blendOutWait) break;
                yield return null;
            }
        }
        finally
        {
            departRoutine = null;
            ParkRig();
        }
    }

    // 장치를 끄고 케이지를 Lobby 정박 자리로 돌려 둔다(떠나는 연출 도중에 Lobby로 돌아왔을 때도 부른다).
    public void ForceReturnCar()
    {
        if (IsRiding) return;
        if (departRoutine != null) { StopCoroutine(departRoutine); departRoutine = null; }
        ParkRig(gameObject.activeInHierarchy);
    }

    private void ParkRig(bool hierarchy = true)
    {
        StopMotor();
        EndShake();
        following = false;
        CarSpeed = 0f;
        if (rideCamera != null) rideCamera.Priority = idlePriority;
        clampAngle = clampTarget = ClampOpen;
        transferProgress = 0f;
        if (!IsRiding) CurrentPhase = Phase.Idle;
        if (!hierarchy) { parkPending = true; return; }
        parkPending = false;
        var dock = source != null ? source : elevator;
        if (dock != null && dock.Car != null && carHolder != null && dock.Car.transform.parent == carHolder) dock.DockCar();
        if (trolley != null) PlaceTrolley(0f);
        if (rig != null && rig.activeSelf) rig.SetActive(false);
    }

    // 끝까지 가지 못했을 때(예외·비활성화) 맡은 것을 모두 되돌린다. 여러 번 불러도 같다.
    private void Cleanup(bool interrupted, bool hierarchy = true)
    {
        if (!IsRiding) return;
        try
        {
            StopMotor();
            EndShake();
            if (confined && player != null && motor != null)
            {
                motor.ClearConfinement();
                player.externalForce = Vector2.zero;
                player.Velocity = Vector2.zero;
            }
            confined = false;
            following = false;
            bool afterSwitch = interrupted && HasSwitchedRoom && !WavesBegun && room != null && host != null;
            // 방을 바꾼 뒤 갱도(실내 위)나 지형 속에 남았으면 입장 지점으로 옮긴다(이상 경로의 순간이동).
            if (afterSwitch && player != null && host.entryPoint != null && (player.transform.position.y > interior.yMax || motor.IsBodyBlockedAt(Vector2.zero)))
                player.ResetPlayerPosition(host.entryPoint.position);
            RestoreBgm();
            RestoreSuppress();
            // Wave가 시작되지 않았으면 로딩이 끝나는 대로 시작한다(보통 입장과 같다).
            if (afterSwitch && room.isActiveAndEnabled && RoomManager.Instance != null && RoomManager.Instance.CurrentRoom == room)
                room.StartCoroutine(BeginWavesWhenLoaded(room, host));
            RestoreUi();
            if (source != null) source.SetAlarm(false);
        }
        finally
        {
            IsRiding = false;
            ParkRig(hierarchy);
        }
    }

    private static IEnumerator BeginWavesWhenLoaded(Room target, TrainingRoomHost targetHost)
    {
        var content = target.ContentRoot;
        while (targetHost != null && targetHost.IsLoading) yield return null;
        if (target != null && content == target.ContentRoot && RoomManager.Instance != null && RoomManager.Instance.CurrentRoom == target
            && !target.SuppressAutomaticWaves && target.WaveManager != null)
            target.WaveManager.BeginWaves();
    }

    private void RestoreUi()
    {
        if (!ownsUi) return;
        ownsUi = false;
        var ui = UIManager.Instance;
        if (ui == null) return;
        ui.PauseInputBlocked = previousPause;
        ui.HUDVisible(true);
    }

    private void RestoreSuppress()
    {
        if (!suppressOwned) return;
        suppressOwned = false;
        if (room != null) room.SuppressAutomaticWaves = previousSuppress;
    }

    // 꺼 둔 방 BGM 플레이어를 켠다. 처음 켜지는 플레이어는 Start에서 곡을 올리므로, 한 프레임 뒤에도 곡이 없을 때만 다시 고르게 한다(곡을 두 번 다시 시작하지 않게).
    private void RestoreBgm()
    {
        if (mutedBgm.Count == 0) return;
        var list = mutedBgm.ToArray();
        mutedBgm.Clear();
        foreach (var bgm in list) if (bgm != null) bgm.enabled = true;
        if (isActiveAndEnabled) StartCoroutine(RefreshBgmNextFrame(list));
        else foreach (var bgm in list) if (bgm != null && bgm.isActiveAndEnabled) bgm.RefreshWaveMusic();
    }

    private static IEnumerator RefreshBgmNextFrame(RoomBgmPlayer[] list)
    {
        yield return null;
        var sound = SoundManager.Instance;
        foreach (var bgm in list)
        {
            if (bgm == null || !bgm.isActiveAndEnabled) continue;
            var clip = bgm.Clip;
            if (sound == null || clip == null || sound.CurrentBgmClip != clip || !sound.IsBgmPlaying) bgm.RefreshWaveMusic();
        }
    }

    // ===================== 물리 틱 이동(위치를 직접 바꾸지 않고 구속 영역만 옮긴다) =====================

    // 한 축(세로) 이동: 가속 → 일정 속도 → 감속. hold가 값을 주면 그 자리에서 멈출 수 있게 속도를 줄인다(입장 로딩 대기).
    private IEnumerator Travel(float direction, Func<float> target, float braking, Func<float?> hold)
    {
        var tick = new WaitForFixedUpdate();
        float speed = 0f;
        while (true)
        {
            yield return tick;
            Sample();
            if (!motor.IsConfined) throw new InvalidOperationException("이동 중에 이동 구속이 풀렸습니다.");
            float position = currentArea.yMin;
            float remain = (target() - position) * direction;
            if (remain <= .0005f) { CarSpeed = 0f; break; }
            float dt = Time.fixedDeltaTime;
            float limit = Mathf.Min(cruiseSpeed, Mathf.Sqrt(2f * braking * remain));
            float? stop = hold != null ? hold() : null;
            bool holding = false;
            if (stop.HasValue)
            {
                float ahead = (stop.Value - position) * direction;
                if (ahead > -.01f)
                {
                    limit = Mathf.Min(limit, Mathf.Sqrt(2f * braking * Mathf.Max(0f, ahead)));
                    holding = ahead < .01f;
                }
            }
            speed = speed < limit ? Mathf.Min(limit, speed + acceleration * dt) : limit;
            float step = Mathf.Min(speed * dt, remain);
            if (stop.HasValue && holding) step = 0f;
            CarSpeed = step / dt;
            if (holding && step <= 0f) { CurrentPhase = Phase.Holding; speed = 0f; continue; }
            if (CurrentPhase == Phase.Holding) CurrentPhase = Phase.Descending;
            motor.ShiftConfinement(new Vector2(0f, step * direction));
            Moved(step * direction);
        }
    }

    // 구속 영역의 바닥 가운데를 from에서 to로 옮긴다(처음과 끝이 느림). 끝나면 마지막 이동량이 반영된 틱에서 멈춘다.
    private IEnumerator Haul(Vector2 from, Vector2 to, float duration, bool transfer)
    {
        var tick = new WaitForFixedUpdate();
        float time = 0f;
        while (true)
        {
            yield return tick;
            Sample();
            if (!motor.IsConfined) throw new InvalidOperationException("이동 중에 이동 구속이 풀렸습니다.");
            time += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(time / duration);
            if (transfer) transferProgress = t;
            Vector2 point = Vector2.Lerp(from, to, t * t * (3f - 2f * t));
            Vector2 delta = point - new Vector2(currentArea.center.x, currentArea.yMin);
            if (t >= 1f && delta.sqrMagnitude < 1e-8f) { CarSpeed = 0f; break; }
            CarSpeed = delta.magnitude / Time.fixedDeltaTime;
            motor.ShiftConfinement(delta);
            Moved(delta.y);
        }
    }

    private void Moved(float dy)
    {
        var car = source != null ? source.Car : null;
        if (car != null && dy != 0f) car.AddTravel(dy);
        railDistance += Mathf.Abs(dy);
        if (railDistance >= beamSpacing)
        {
            railDistance -= beamSpacing;
            if (car != null) PlaySound(railTick, car.transform.position);
        }
    }

    // 직전 물리 틱의 Move가 반영된 구속 영역을 표본으로 남긴다(FixedMotionBody와 같은 시각 기준).
    private void Sample()
    {
        double time = Time.fixedTimeAsDouble;
        if (time == sampleTime) { currentArea = motor.Confinement; return; }
        previousArea = currentArea;
        currentArea = motor.Confinement;
        sampleTime = time;
    }

    private void ResetSamples(Rect area)
    {
        previousArea = currentArea = area;
        sampleTime = Time.fixedTimeAsDouble;
    }

    // ===================== 표시(LateUpdate 98: Brain 갱신(99) 직전) =====================

    private void LateUpdate()
    {
        if (parkPending && !IsRiding) ParkRig();
        if (rig == null || !rig.activeSelf) return;
        var car = elevator != null ? elevator.Car : null;
        if (following && car != null)
        {
            float alpha = Mathf.Clamp01((float)((Time.timeAsDouble - sampleTime) / Mathf.Max(.0001f, Time.fixedDeltaTime)));
            var r = new Rect(Vector2.Lerp(previousArea.position, currentArea.position, alpha), Vector2.Lerp(previousArea.size, currentArea.size, alpha));
            PlaceCar(new Vector2(r.center.x, r.yMin));
        }
        if (IsRiding) UpdateAnchor();
        if (car != null && HoldsCar)
        {
            float carLocalX = car.transform.position.x - rig.transform.position.x;
            PlaceTrolley(CurrentPhase == Phase.Departing ? transferOffset : Mathf.Clamp(carLocalX, 0f, transferOffset));
            if (trolleySheave != null) car.SetCableTop(trolleySheave.position);
            if (shake != null) shake.Position = car.transform.position;
        }
        clampAngle = Mathf.MoveTowards(clampAngle, clampTarget, (ClampOpen - ClampClosed) / Mathf.Max(.05f, clampTime) * Time.deltaTime);
        // 집게 팔은 위쪽 축에 매달려 열리면 바깥으로 벌어진다(왼쪽은 시계 방향, 오른쪽은 반시계 방향).
        if (clampL != null) clampL.localRotation = Quaternion.Euler(0f, 0f, -clampAngle);
        if (clampR != null) clampR.localRotation = Quaternion.Euler(0f, 0f, clampAngle);
        UpdateCounterweights(car);
    }

    private void PlaceCar(Vector2 floor)
    {
        var car = elevator != null ? elevator.Car : null;
        if (car == null || !HoldsCar) return;
        var t = car.transform;
        t.position = new Vector3(floor.x, floor.y, t.position.z);
    }

    // 카메라 기준점: 케이지 바닥 + 높이(오를 땐 위를, 내릴 땐 아래를 보이게). 하강 끝에는 교장 카메라가 비출 화면으로 맞춰 간다(넘길 때 튀지 않게).
    private void UpdateAnchor()
    {
        if (cameraAnchor == null) return;
        var car = elevator != null ? elevator.Car : null;
        Vector2 floor = car != null ? (Vector2)car.transform.position : (Vector2)cameraAnchor.position;
        float lift = CurrentPhase switch
        {
            Phase.Boarding or Phase.Ascending or Phase.Junction => ascentLift,
            Phase.Transferring => Mathf.Lerp(ascentLift, descentLift, Ease(transferProgress)),
            _ => descentLift,
        };
        Vector2 anchor = new Vector2(floor.x, floor.y + lift);
        if (HasSwitchedRoom && CurrentPhase >= Phase.Descending)
        {
            float start = FieldCeilingTop + framingStart;
            if (floor.y < start)
            {
                float s = Ease((start - floor.y) / Mathf.Max(.01f, start - LandingY));
                Vector2 view = RoomClamped(new Vector2(floor.x, floor.y + playerAboveFloor) + trainingOffset);
                anchor = Vector2.Lerp(anchor, view, s);
            }
        }
        cameraAnchor.position = new Vector3(anchor.x, anchor.y, cameraAnchor.position.z);
    }

    // 교장 카메라 범위(방 카메라의 Confiner 상자)에 이동 카메라 렌즈·화면비로 가둔 중심.
    private Vector2 RoomClamped(Vector2 point)
    {
        var area = RoomCameraBox();
        var view = Camera.main;
        float halfHeight = rideLens, halfWidth = rideLens * (view != null ? view.aspect : 16f / 9f);
        float x = area.width > halfWidth * 2f ? Mathf.Clamp(point.x, area.xMin + halfWidth, area.xMax - halfWidth) : area.center.x;
        float y = area.height > halfHeight * 2f ? Mathf.Clamp(point.y, area.yMin + halfHeight, area.yMax - halfHeight) : area.center.y;
        return new Vector2(x, y);
    }

    private Rect RoomCameraBox()
    {
        var confiner = room != null && room.VirtualCamera != null ? room.VirtualCamera.GetComponent<CinemachineConfiner2D>() : null;
        if (confiner != null && confiner.BoundingShape2D is BoxCollider2D box) return WorldRect(box);
        return Rect.MinMaxRect(interior.xMin - 1f, interior.yMin - 4.5f, interior.xMax + 1f, interior.yMax + 1f);
    }

    private Rect InteriorRect()
    {
        var data = host.Data;
        var local = data != null ? WaveRoomState.LayoutAt(data, 0).bounds : new Rect(-4f, -3f, 26f, 17f);
        Vector2 a = host.transform.TransformPoint(local.min), b = host.transform.TransformPoint(local.max);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    private static Rect LobbyBounds()
    {
        var lobby = GameManager.Instance != null ? GameManager.Instance.LobbyRoom : null;
        var confiner = lobby != null && lobby.VirtualCamera != null ? lobby.VirtualCamera.GetComponent<CinemachineConfiner2D>() : null;
        if (confiner != null && confiner.BoundingShape2D is BoxCollider2D box) return WorldRect(box);
        return Rect.MinMaxRect(-26.25f, -43f, 48.75f, -11f);
    }

    // Collider.bounds는 물리 동기화보다 늦을 수 있어 Transform·offset·size로 계산한다(회전 없는 상자).
    private static Rect WorldRect(BoxCollider2D box)
    {
        var t = box.transform;
        Vector2 center = t.TransformPoint(box.offset);
        Vector2 size = Vector2.Scale(box.size, new Vector2(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y)));
        return new Rect(center - size * .5f, size);
    }

    private void SetBounds(Rect world)
    {
        if (rideBounds == null) return;
        var t = rideBounds.transform;
        t.position = new Vector3(world.center.x, world.center.y, t.position.z);
        t.rotation = Quaternion.identity;
        rideBounds.offset = Vector2.zero;
        var scale = t.lossyScale;
        rideBounds.size = new Vector2(world.width / Mathf.Max(.0001f, Mathf.Abs(scale.x)), world.height / Mathf.Max(.0001f, Mathf.Abs(scale.y)));
        var confiner = rideCamera != null ? rideCamera.GetComponent<CinemachineConfiner2D>() : null;
        if (confiner != null) confiner.InvalidateBoundingShapeCache();
    }

    private void CacheTrolleyHome()
    {
        if (trolleyHomeKnown || trolley == null) return;
        trolleyHomeY = trolley.localPosition.y;
        trolleyHomeKnown = true;
    }

    private void PlaceTrolley(float x)
    {
        if (trolley == null) return;
        CacheTrolleyHome();
        trolley.localPosition = new Vector3(x, trolleyHomeY, trolley.localPosition.z);
        float angle = -x / wheelRadius * Mathf.Rad2Deg;
        foreach (var wheel in trolleyWheels) if (wheel != null) wheel.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    // 평형추: 케이지가 오르면 내려가고 내리면 오른다(케이지가 있는 갱도만). 줄은 갱도 꼭대기까지.
    private void UpdateCounterweights(FieldElevatorCar car)
    {
        float carLocalY = car != null && HoldsCar ? car.transform.position.y - rig.transform.position.y : -1f;
        float carLocalX = car != null && HoldsCar ? car.transform.position.x - rig.transform.position.x : 0f;
        bool onB = carLocalX > transferOffset * .5f;
        float k = Mathf.InverseLerp(0f, columnHeight, carLocalY);
        SetCounterweight(counterweightA, counterweightRopeA, onB ? 1f : k);
        SetCounterweight(counterweightB, counterweightRopeB, onB ? k : 1f);
    }

    private void SetCounterweight(Transform weight, LineRenderer rope, float carProgress)
    {
        if (weight == null) return;
        float y = Mathf.Lerp(counterweightRange.y, counterweightRange.x, carProgress);
        weight.localPosition = new Vector3(weight.localPosition.x, y, weight.localPosition.z);
        if (rope == null) return;
        ropePoints[0] = weight.position + Vector3.up * .9f;
        ropePoints[1] = new Vector3(weight.position.x, weight.parent.position.y + columnHeight + 5.4f, weight.position.z);
        if (rope.positionCount != 2) rope.positionCount = 2;
        rope.SetPositions(ropePoints);
    }

    // ===================== 바깥 채움 =====================

    private void BuildFill(params Rect[] rects)
    {
        if (exteriorFill == null) return;
        if (fillMesh == null)
        {
            fillMesh = new Mesh { name = "FieldElevatorExteriorFill" };
            fillMesh.MarkDynamic();
        }
        if (exteriorFill.sharedMesh != fillMesh) exteriorFill.sharedMesh = fillMesh;
        fillVertices.Clear(); fillColors.Clear(); fillTriangles.Clear();
        foreach (var r in rects)
        {
            if (r.width <= 0f || r.height <= 0f) continue;
            int first = fillVertices.Count;
            fillVertices.Add(new Vector3(r.xMin, r.yMin)); fillVertices.Add(new Vector3(r.xMin, r.yMax));
            fillVertices.Add(new Vector3(r.xMax, r.yMax)); fillVertices.Add(new Vector3(r.xMax, r.yMin));
            for (int i = 0; i < 4; i++) fillColors.Add(VoidColor);
            fillTriangles.Add(first); fillTriangles.Add(first + 1); fillTriangles.Add(first + 2);
            fillTriangles.Add(first); fillTriangles.Add(first + 2); fillTriangles.Add(first + 3);
        }
        fillMesh.Clear();
        fillMesh.SetVertices(fillVertices);
        fillMesh.SetColors(fillColors);
        fillMesh.SetTriangles(fillTriangles, 0);
        fillMesh.RecalculateBounds();
    }

    // 바깥 사각형에서 안쪽 사각형을 뺀 나머지(최대 4장: 왼쪽·오른쪽 띠와 그 사이의 위·아래)
    private static Rect[] Subtract(Rect outer, Rect hole)
    {
        var inner = Rect.MinMaxRect(Mathf.Max(outer.xMin, hole.xMin), Mathf.Max(outer.yMin, hole.yMin), Mathf.Min(outer.xMax, hole.xMax), Mathf.Min(outer.yMax, hole.yMax));
        if (inner.width <= 0f || inner.height <= 0f) return new[] { outer };
        return new[]
        {
            Rect.MinMaxRect(outer.xMin, outer.yMin, inner.xMin, outer.yMax),
            Rect.MinMaxRect(inner.xMax, outer.yMin, outer.xMax, outer.yMax),
            Rect.MinMaxRect(inner.xMin, outer.yMin, inner.xMax, inner.yMin),
            Rect.MinMaxRect(inner.xMin, inner.yMax, inner.xMax, outer.yMax),
        };
    }

    private static Rect Offset(Rect r, Vector2 by) => new Rect(r.position + by, r.size);
    private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    // ===================== 소리·흔들림 =====================

    private void StartMotor(SoundOptions sound, Transform follow)
    {
        StopMotor();
        if (sound == null || sound.clip == null || SoundManager.Instance == null || follow == null) return;
        loop = SoundManager.Instance.PlayLoop(sound, follow.position, follow);
    }

    private void StopMotor()
    {
        if (loop != null && SoundManager.Instance != null) SoundManager.Instance.StopLoop(loop);
        loop = null;
    }

    private void StartShake(Vector3 position)
    {
        EndShake();
        if (rideShake > 0f && FeedbackPlayer.Instance != null) shake = FeedbackPlayer.Instance.BeginHeldShake(rideShake, position);
    }

    private void EndShake()
    {
        if (shake != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.EndHeldShake(shake);
        shake = null;
    }

    private static void PlaySound(SoundOptions sound, Vector3 position)
    {
        if (sound != null && sound.clip != null && SoundManager.Instance != null) SoundManager.Instance.PlaySound(sound, position);
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
