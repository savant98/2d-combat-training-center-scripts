using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Lobby 천장을 통과한 케이지가 수직 갱도를 지나 각개 전투 교장의 첫 Wave 시작점으로 올라온다.
/// 고속 구간에서는 아래로 지나가는 수직 잔상과 갱도 구조물, 음높이와 진동으로 속도를 느끼게 한다.
/// 가장 어두울 때 방을 바꾸고 케이지와 카메라, 이동 구속 영역을 같은 양만큼 옮긴다.
/// 실제 이동은 모터의 구속 영역으로 처리하고, 케이지와 카메라는 그 위치를 보간한다.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(98)]
public sealed class FieldElevatorRide : MonoBehaviour
{
    public enum Phase { Idle, Boarding, Ascending, Rush, Arriving, Holding, Landing, Releasing, Departing }

    [Header("구성")]
    [SerializeField, Tooltip("Lobby의 엘리베이터 정박대")] private FieldElevator elevator;
    [SerializeField, Tooltip("이동 중에만 켜는 표시 장치")] private GameObject rig;
    [SerializeField, Tooltip("이동 중 케이지가 붙는 부모")] private Transform carHolder;
    [SerializeField] private Transform columnA, columnB, trolley;
    [SerializeField, Tooltip("실행 중 만드는 바깥 채움(방 밖을 어둡게 덮는 사각형들)")] private MeshFilter exteriorFill;
    [SerializeField] private CinemachineCamera rideCamera;
    [SerializeField, Tooltip("이동 카메라가 따라가는 기준점")] private Transform cameraAnchor;
    [SerializeField, Tooltip("이동 카메라 범위(실행 중 월드 사각형으로 맞춘다)")] private BoxCollider2D rideBounds;

    [Header("갱도 치수(칸)")]
    [SerializeField, Min(4f)] private float columnHeight = 20f;
    [SerializeField, Min(1f)] private float fillHalfWidth = 30f;
    [SerializeField, Min(0f)] private float fillTopMargin = 12f;
    [SerializeField, Min(.5f)] private float beamSpacing = 4f;

    [Header("움직임")]
    [SerializeField, Min(.5f), Tooltip("최고 속도(칸/초)")] private float cruiseSpeed = 14f;
    [SerializeField, Min(.05f)] private float boardTime = .45f;
    [SerializeField, Min(0f)] private float spinUpTime = .4f;

    [SerializeField, Min(0f)] private float landingSettle = .4f;
    [SerializeField, Min(.1f)] private float doorTime = .6f;
    [SerializeField, Min(0f), Tooltip("착지·해제 뒤 케이지가 떠나기 시작할 때까지")] private float departDelay = .6f;
    [SerializeField, Min(.5f)] private float departSpeed = 10f;
    [SerializeField, Min(.5f)] private float departAcceleration = 12f;
    [SerializeField, Min(0f), Tooltip("떠나는 케이지가 교장 천장보다 이만큼 높이 올라가면 장치를 끈다")] private float departClearance = 8f;
    [SerializeField, Min(0f)] private float sideMargin = .12f, topMargin = .12f;

    [Header("카메라")]
    [SerializeField] private int ridePriority = 40;
    [SerializeField] private int idlePriority = -1;
    [SerializeField, Min(1f)] private float rideLens = 7f;
    [SerializeField, Tooltip("올라갈 때 카메라가 케이지 바닥보다 위에 둘 높이(위를 보이게)")] private float ascentLift = 4f;
    [SerializeField, Min(.5f), Tooltip("착지점 아래에서 교장 카메라의 화면으로 맞추기 시작하는 거리")] private float framingStart = 8f;
    [SerializeField, Min(0f), Tooltip("교장 카메라로 넘긴 뒤 장치를 끄기까지 기다리는 시간(블렌드 1초보다 길게)")] private float blendOutWait = 1.1f;

    [Header("소리")]
    [SerializeField] private SoundOptions spinUpSound;
    [SerializeField, Tooltip("이동 중 반복음(케이지를 따라감)")] private SoundOptions motorLoop;
    [SerializeField, Tooltip("레일 이음매를 지날 때")] private SoundOptions railTick;
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
    /// <summary>갱도에서 방을 바꾸는 데 걸린 시간(ms, 방 전환·방 내용 불러오기·옮기기). 아직 바꾸지 않았으면 -1. 검증에서 끊김을 잰다.</summary>
    public float SwitchMilliseconds { get; private set; } = -1f;
    public float CarSpeed { get; private set; }
    public Vector3 LastRelocation { get; private set; }
    public float LandingY { get; private set; }
    public float FieldFloorY { get; private set; }
    public float ShaftDarkness { get; private set; }
    public const float AccelerationSeconds = 3f;
    public const float RushSeconds = 3f;
    public const float ArrivalSeconds = 3.5f;
    public float HoldSeconds { get; private set; }
    public bool LoadingPanelHidden { get; private set; }
    public Vector2 LobbyOrigin { get; private set; }
    public Vector2 FieldOrigin { get; private set; }
    public float CruiseSpeed => peakSpeed > 0f ? peakSpeed : cruiseSpeed;
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
    private float playerAboveFloor = .75f;
    private Vector2 trainingOffset;
    private Rect interior;
    private float railDistance;
    private float peakSpeed, shaftUrgency, arrivalProgress;
    private float shaftMinY, shaftMaxY;
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
    private Transform shaftVisuals;
    private MeshRenderer darkRenderer, beamRenderer;
    private Mesh darkMesh, beamMesh;
    private CinemachineImpulseListener rideImpulse;
    private float impulseGain, motorPitch, motorVolume, beamScroll;
    private readonly List<Vector3> beamVertices = new List<Vector3>(512);
    private readonly List<Color> beamColors = new List<Color>(512);
    private readonly List<int> beamTriangles = new List<int>(768);
    private static readonly int TintId = Shader.PropertyToID("_Color");
    private static readonly Color VoidColor = new Color(.003f, .003f, .003f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    private void Awake()
    {
        if (Instance == null || Instance == this) Instance = this;
        if (rig != null && rig.activeSelf) rig.SetActive(false);
        if (rideCamera != null) rideCamera.Priority = idlePriority;
        if (rideCamera != null && rideCamera.TryGetComponent(out rideImpulse)) impulseGain = rideImpulse.Gain;
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
        if (darkMesh != null) Destroy(darkMesh);
        if (beamMesh != null) Destroy(beamMesh);
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
            railDistance = beamScroll = arrivalProgress = 0f;
            // 카메라의 화면 하단까지 천장 위에 들어간 뒤 좌표계를 옮긴다.
            float clearance = Mathf.Max(0f, LobbyOrigin.y + Mathf.Max(5f, rideLens - ascentLift + 2f) - source.FloorY);
            peakSpeed = Mathf.Max(cruiseSpeed, clearance * 2f / AccelerationSeconds);
            var lobbyBox = LobbyBounds();
            float halfWidth = ShaftHalfWidth();
            float leftFill = Mathf.Min(-halfWidth, lobbyBox.xMin - LobbyOrigin.x), rightFill = Mathf.Max(halfWidth, lobbyBox.xMax - LobbyOrigin.x);
            float rise = peakSpeed * AccelerationSeconds * .5f + source.FloorY - LobbyOrigin.y;
            var lobbyFill = Rect.MinMaxRect(leftFill, 0f, rightFill, Mathf.Max(columnHeight, rise) + Mathf.Max(fillTopMargin, rideLens * 2f));
            BuildFill(lobbyFill);
            SetBounds(Union(lobbyBox, Offset(lobbyFill, LobbyOrigin)));
            rig.SetActive(true);
            SetStationShaft(true);
            CreateShaftVisuals();
            SetRushEffects(false, 0f);
            source.DetachCar(carHolder);
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
            for (var step = Haul(new Vector2(currentArea.center.x, currentArea.yMin), new Vector2(carFloor.x, source.FloorY), boardTime); step.MoveNext();)
                yield return step.Current;
            following = true;

            // ---- 4. 문 닫힘 ----
            car.SetDoors(false);
            for (float t = 0f; t < doorTime + .3f && !(t > .05f && car.DoorsSettled); t += Time.deltaTime) yield return null;

            // 닫힌 문 뒤에서 교장 개체를 미리 구성해 질주 중의 동기 생성 비용을 줄인다.
            if (RoomManager.Instance.CurrentRoom != room) host.Stage(context.request.waveData);

            // ---- 5. 출발 준비: 경고등·표시판·구동음·흔들림, Lobby BGM은 멀리 들리게 ----
            PlaySound(spinUpSound, car.transform.position);
            car.SetBeacons(true);
            car.SetPanel(FieldElevatorCar.PanelMode.Up);
            StartMotor(motorLoop, car.transform);
            StartShake(car.transform.position);
            var sound = SoundManager.Instance;
            if (sound != null && sound.CurrentBgmClip != null) sound.SetBGMDistance(sound.CurrentBgmClip, lobbyBgmDistanceVolume, lobbyBgmCutoff, lobbyBgmRamp);
            for (float t = 0f; t < spinUpTime; t += Time.deltaTime) yield return null;

            CurrentPhase = Phase.Ascending;
            float accelerationDistance = peakSpeed * AccelerationSeconds;
            for (var step = TravelUp(AccelerationSeconds, t => accelerationDistance * RampDistance(t),
                t => SetRushEffects(false, Ease(t))); step.MoveNext();)
                yield return step.Current;

            source.SetAlarm(false);
            SetStationShaft(false);
            SetRushEffects(true, 1f);
            CurrentPhase = Phase.Rush;
            // 갱도 잔상의 위치와 속도를 유지한 채 어두운 프레임에서 좌표계를 교장 쪽으로 옮긴다.
            yield return null;
            SwitchRoom(context);

            float rushDistance = peakSpeed * RushSeconds;
            for (var step = TravelUp(RushSeconds, t => rushDistance * t); step.MoveNext();)
                yield return step.Current;

            if (host.IsLoading)
            {
                CurrentPhase = Phase.Holding;
                CarSpeed = 0f;
                float warned = 0f;
                while (host.IsLoading)
                {
                    // 바닥 아래에서 로딩을 기다리는 동안에도 갱도 잔상은 정속으로 흐른다.
                    HoldSeconds += Time.deltaTime;
                    if (HoldSeconds - warned >= 15f)
                    {
                        warned = HoldSeconds;
                        Debug.LogWarning($"[FieldElevatorRide] 입장 로딩 {HoldSeconds:0}초 경과(진행률 {host.LoadingProgress:P0}).", this);
                    }
                    yield return null;
                }
            }

            CurrentPhase = Phase.Arriving;
            LandingY = ResolveLandingY();
            float brakeDistance = peakSpeed * ArrivalSeconds;
            float correction = LandingY - currentArea.yMin - brakeDistance * .5f;
            for (var step = TravelUp(ArrivalSeconds,
                t => brakeDistance * (t - RampDistance(t)) + correction * SmoothArrival(t), t =>
                {
                    arrivalProgress = t;
                    // 바닥 아래의 불투명한 갱도가 화면 안에서 내려가며 교장을 드러낸다.
                    SetRushEffects(false, 1f - Ease(t));
                }); step.MoveNext();)
                yield return step.Current;
            SetRushEffects(false, 0f);

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

    // 갱도 조명만 보일 때 교장 바닥 아래로 옮긴다.
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

        // 교장을 다시 방문할 때도 갱도가 가리고 있는 동안 첫 Wave의 배경·지형을 맞춘다.
        if (host.Content.TryGetComponent(out WaveCellTerrain terrain)) terrain.ResetForArrival();
        Vector2 entry = host.entryPoint.position;
        interior = InteriorRect();
        var roomFollow = room.VirtualCamera != null ? room.VirtualCamera.GetComponent<CinemachineFollow>() : null;
        trainingOffset = roomFollow != null ? (Vector2)roomFollow.FollowOffset : Vector2.zero;
        FieldFloorY = interior.yMin;
        float climb = peakSpeed * (RushSeconds + ArrivalSeconds * .5f);
        FieldOrigin = new Vector2(entry.x, entry.y - playerAboveFloor - climb);
        Vector3 delta = (Vector3)(FieldOrigin - new Vector2(currentArea.center.x, currentArea.yMin));
        rig.transform.position += delta;
        player.ResetPlayerPosition(playerBefore + delta);
        ResetSamples(motor.Confinement);
        LastRelocation = delta;
        PlaceCar(new Vector2(currentArea.center.x, currentArea.yMin));
        UpdateAnchor();
        CinemachineCore.OnTargetObjectWarped(cameraAnchor, delta);
        rideCamera.PreviousStateIsValid = false;
        if (view != null) view.transform.position = cameraBefore + delta;

        float halfWidth = ShaftHalfWidth();
        float verticalMargin = Mathf.Max(fillTopMargin, rideLens + 2f);
        var outer = Rect.MinMaxRect(Mathf.Min(interior.xMin, entry.x - halfWidth),
            FieldOrigin.y - verticalMargin, Mathf.Max(interior.xMax, entry.x + halfWidth), interior.yMax + verticalMargin);
        var origin = (Vector2)rig.transform.position;
        BuildFill(Subtract(Offset(outer, -origin), Offset(interior, -origin)));
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

    // 첫 Wave 시작점 아래의 실제 바닥을 사용하는 동안 몸을 놓을 자리도 검사한다.
    private float ResolveLandingY()
    {
        Vector2 entry = host.entryPoint.position;
        var feet = new Vector2(entry.x, entry.y - playerAboveFloor);
        float above = Mathf.Min(1.2f, Mathf.Max(.1f, currentArea.height - topMargin));
        var floor = motor.RaycastFloorBelow(feet, above, 3f);
        if (floor.collider == null || floor.distance <= .02f) return feet.y;
        float deck = floor.point.y + .012f;
        var bodyDestination = new Vector2(entry.x, deck + playerAboveFloor);
        return motor.IsBodyBlockedAt(bodyDestination - (Vector2)player.transform.position) ? feet.y : deck;
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

    // 승객이 내리면 케이지는 같은 축으로 교장 천장 위까지 올라가 화면 밖에서 정박 상태로 돌아간다.
    private IEnumerator DepartRoutine()
    {
        try
        {
            CurrentPhase = Phase.Departing;
            for (float t = 0f; t < departDelay; t += Time.deltaTime) yield return null;
            var car = elevator != null ? elevator.Car : null;
            if (car == null || !HoldsCar) yield break;
            car.SetDoors(false);
            for (float t = 0f; !car.DoorsSettled && t < doorTime + .3f; t += Time.deltaTime)
                yield return null;
            car.SetPanel(FieldElevatorCar.PanelMode.Up);
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
                if (car.transform.position.y > interior.yMax + departClearance && Time.time - started > blendOutWait) break;
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
        SetRushEffects(false, 0f);
        if (!IsRiding) CurrentPhase = Phase.Idle;
        if (!hierarchy) { parkPending = true; return; }
        parkPending = false;
        var dock = source != null ? source : elevator;
        if (dock != null && dock.Car != null && carHolder != null && dock.Car.transform.parent == carHolder) dock.DockCar();
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
            // 전환이 중단되어 교장 바깥이나 지형에 남은 Player를 시작점으로 복귀시킨다.
            if (afterSwitch && player != null && host.entryPoint != null && (player.transform.position.y < interior.yMin
                || player.transform.position.y > interior.yMax || motor.IsBodyBlockedAt(Vector2.zero)))
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

    // 각 구간의 속도 곡선을 적분한 위치를 기존 모터의 물리 틱에 적용한다.
    private IEnumerator TravelUp(float duration, Func<float, float> distance, Action<float> present = null)
    {
        float from = currentArea.yMin;
        var tick = new WaitForFixedUpdate();
        int count = Mathf.Max(1, Mathf.RoundToInt(duration / Time.fixedDeltaTime));
        for (int index = 1; index <= count; index++)
        {
            if (!motor.IsConfined) throw new InvalidOperationException("이동 중에 이동 구속이 풀렸습니다.");
            float progress = (float)index / count;
            float step = from + distance(progress) - currentArea.yMin;
            CarSpeed = step / Time.fixedDeltaTime;
            motor.ShiftConfinement(Vector2.up * step);
            present?.Invoke(progress);
            Moved(step);
            yield return tick;
            Sample();
        }
    }

    private static float RampDistance(float t) => t * t * t * (1f - t * .5f);

    // 레이로 확인한 바닥 높이 차이를 끝점의 속도·가속도를 바꾸지 않고 흡수한다.
    private static float SmoothArrival(float t) => t * t * t * (10f + t * (-15f + t * 6f));

    // 구속 영역의 바닥 가운데를 from에서 to로 옮긴다(처음과 끝이 느림).
    private IEnumerator Haul(Vector2 from, Vector2 to, float duration)
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
        float spacing = Mathf.Lerp(beamSpacing, beamSpacing * .48f, shaftUrgency);
        if (railDistance >= spacing)
        {
            railDistance %= spacing;
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
            car.SetCableTop(car.transform.position + Vector3.up * (rideLens * 2f));
            if (shake != null) shake.Position = car.transform.position;
        }
        UpdateShaftVisuals();
    }

    private void PlaceCar(Vector2 floor)
    {
        var car = elevator != null ? elevator.Car : null;
        if (car == null || !HoldsCar) return;
        var t = car.transform;
        t.position = new Vector3(floor.x, floor.y, t.position.z);
    }

    // 상승 중에는 케이지 위를 비추고 입구에 접근하면 교장 카메라 화면에 맞춘다.
    private void UpdateAnchor()
    {
        if (cameraAnchor == null) return;
        var car = elevator != null ? elevator.Car : null;
        Vector2 floor = car != null ? (Vector2)car.transform.position : (Vector2)cameraAnchor.position;
        Vector2 anchor = floor + Vector2.up * ascentLift;
        if (HasSwitchedRoom && CurrentPhase >= Phase.Arriving && CurrentPhase != Phase.Holding)
        {
            float distanceProgress = Mathf.InverseLerp(LandingY - framingStart, LandingY, floor.y);
            float timeProgress = Mathf.InverseLerp(.35f, 1f, arrivalProgress);
            float progress = SmoothArrival(Mathf.Min(distanceProgress, timeProgress));
            Vector2 view = RoomClamped(new Vector2(floor.x, floor.y + playerAboveFloor) + trainingOffset);
            anchor = Vector2.Lerp(anchor, view, progress);
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

    private void SetStationShaft(bool visible)
    {
        if (columnA != null) columnA.gameObject.SetActive(visible);
        if (columnB != null) columnB.gameObject.SetActive(false);
        if (trolley != null && trolley.parent != null) trolley.parent.gameObject.SetActive(false);
    }

    // Default 층의 갱도와 방 위에 어둠을 얹는다. Player 층의 케이지와 조명은 계속 보인다.
    private void CreateShaftVisuals()
    {
        if (shaftVisuals != null || exteriorFill == null || !exteriorFill.TryGetComponent<MeshRenderer>(out var fill)) return;
        var root = new GameObject("ShaftVisuals");
        shaftVisuals = root.transform;
        shaftVisuals.SetParent(rig.transform, false);

        darkMesh = new Mesh { name = "ElevatorShaftDarkness" };
        darkMesh.vertices = new[] { new Vector3(-1f, -1f), new Vector3(-1f, 1f), new Vector3(1f, 1f), new Vector3(1f, -1f) };
        darkMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        darkMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        darkMesh.RecalculateBounds();
        // 방 배경과 지형 위, 케이지와 Player 아래에 갱도의 불투명한 경계를 그린다.
        fill.sortingOrder = 31997;
        darkRenderer = AddShaftRenderer("Darkness", darkMesh, fill.sharedMaterial, 31998);
        var darknessProperties = new MaterialPropertyBlock();
        darknessProperties.SetColor(TintId, VoidColor);
        darkRenderer.SetPropertyBlock(darknessProperties);

        beamMesh = new Mesh { name = "ElevatorShaftBeams" };
        beamMesh.MarkDynamic();
        beamRenderer = AddShaftRenderer("PassingBeams", beamMesh, fill.sharedMaterial, 31999);
    }

    private MeshRenderer AddShaftRenderer(string name, Mesh mesh, Material material, int order)
    {
        var node = new GameObject(name);
        node.transform.SetParent(shaftVisuals, false);
        node.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = node.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = order;
        renderer.enabled = false;
        return renderer;
    }

    private void UpdateShaftVisuals()
    {
        if (shaftVisuals == null || (!darkRenderer.enabled && !beamRenderer.enabled)) return;
        var camera = Camera.main;
        float height = Mathf.Max(rideLens, camera != null ? camera.orthographicSize : rideLens);
        float width = height * (camera != null ? camera.aspect : 16f / 9f);
        shaftVisuals.position = new Vector3(cameraAnchor.position.x, cameraAnchor.position.y, shaftVisuals.position.z);
        darkRenderer.transform.localScale = new Vector3(width + 5f, height + 5f, 1f);
        if (!beamRenderer.enabled) return;

        const float spacing = 3f;
        beamScroll = Mathf.Repeat(beamScroll + Time.deltaTime * Mathf.Lerp(10f, 62f, shaftUrgency), 192f);
        beamVertices.Clear();
        beamColors.Clear();
        beamTriangles.Clear();
        float extentY = height + 4f;
        float edge = width + 5f;
        shaftMinY = -extentY;
        shaftMaxY = extentY;
        if (!darkRenderer.enabled)
        {
            // 잔상과 레일도 천장 위 또는 교장 바닥 아래의 갱도에서만 보인다.
            if (HasSwitchedRoom) shaftMaxY = Mathf.Min(shaftMaxY, FieldFloorY - shaftVisuals.position.y);
            else shaftMinY = Mathf.Max(shaftMinY, LobbyOrigin.y - shaftVisuals.position.y);
        }
        float light = shaftUrgency * .23f;
        Color rail = new Color(.15f, .17f, .18f, light * .52f);
        AddShaftBeam(-3.5f, -extentY, -3.36f, extentY, rail);
        AddShaftBeam(3.36f, -extentY, 3.5f, extentY, rail);
        int count = Mathf.CeilToInt((extentY + spacing * 4f) / spacing);
        for (int i = -count; i <= count; i++)
        {
            float y = i * spacing - Mathf.Repeat(beamScroll * .5f, spacing * 4f);
            float thickness = i % 4 == 0 ? .31f : .15f;
            Color beam = new Color(.23f, .25f, .27f, light * (i % 4 == 0 ? .78f : .47f));
            AddShaftBeam(-edge, y - thickness, -2.45f, y, beam);
            AddShaftBeam(2.45f, y - thickness, edge, y, beam);
        }
        AddSpeedStreaks(width, extentY);
        beamMesh.Clear();
        beamMesh.SetVertices(beamVertices);
        beamMesh.SetColors(beamColors);
        beamMesh.SetTriangles(beamTriangles, 0);
    }

    private void AddSpeedStreaks(float width, float height)
    {
        // 시야 바깥쪽의 가까운 잔상과 케이지 옆의 느린 잔상을 한 Mesh에서 그린다.
        const float interval = 8f;
        float laneWidth = Mathf.Max(.4f, width - 2.9f);
        for (int side = -1; side <= 1; side += 2)
        for (int lane = 0; lane < 7; lane++)
        {
            bool near = lane % 3 != 0;
            float x = side * (2.9f + laneWidth * (.08f + lane * .14f));
            float phase = lane * 3.19f + (side > 0 ? 1.82f : 0f);
            float offset = Mathf.Repeat(beamScroll * (near ? 1f : .5f) + phase, interval);
            float length = Mathf.Lerp(.6f, near ? 5.6f : 3.2f, shaftUrgency) * (.72f + .1f * (lane % 4));
            float halfWidth = near ? .033f + .008f * (lane % 3) : .021f;
            Color color = new Color(.62f, .72f, .83f, shaftUrgency * (near ? .61f : .31f));
            int count = Mathf.CeilToInt((height + length + interval) / interval);
            for (int row = -count; row <= count; row++)
            {
                float bottom = row * interval - offset;
                float low = Mathf.Max(bottom, shaftMinY);
                float high = Mathf.Min(bottom + length, shaftMaxY);
                if (low >= high) continue;
                AddSpeedStreak(x, low, high - low, halfWidth, color);
            }
        }
    }

    private void AddSpeedStreak(float x, float y, float length, float width, Color color)
    {
        int first = beamVertices.Count;
        Color clear = color;
        clear.a = 0f;
        beamVertices.Add(new Vector3(x - width * .4f, y));
        beamVertices.Add(new Vector3(x - width, y + length * .23f));
        beamVertices.Add(new Vector3(x - width * .24f, y + length));
        beamVertices.Add(new Vector3(x + width * .24f, y + length));
        beamVertices.Add(new Vector3(x + width, y + length * .23f));
        beamVertices.Add(new Vector3(x + width * .4f, y));
        beamColors.Add(clear);
        beamColors.Add(color);
        beamColors.Add(clear);
        beamColors.Add(clear);
        beamColors.Add(color);
        beamColors.Add(clear);
        beamTriangles.Add(first); beamTriangles.Add(first + 1); beamTriangles.Add(first + 4);
        beamTriangles.Add(first); beamTriangles.Add(first + 4); beamTriangles.Add(first + 5);
        beamTriangles.Add(first + 1); beamTriangles.Add(first + 2); beamTriangles.Add(first + 3);
        beamTriangles.Add(first + 1); beamTriangles.Add(first + 3); beamTriangles.Add(first + 4);
    }

    private void AddShaftBeam(float left, float bottom, float right, float top, Color color)
    {
        bottom = Mathf.Max(bottom, shaftMinY);
        top = Mathf.Min(top, shaftMaxY);
        if (bottom >= top) return;
        int first = beamVertices.Count;
        beamVertices.Add(new Vector3(left, bottom));
        beamVertices.Add(new Vector3(left, top));
        beamVertices.Add(new Vector3(right, top));
        beamVertices.Add(new Vector3(right, bottom));
        for (int i = 0; i < 4; i++) beamColors.Add(color);
        beamTriangles.Add(first); beamTriangles.Add(first + 1); beamTriangles.Add(first + 2);
        beamTriangles.Add(first); beamTriangles.Add(first + 2); beamTriangles.Add(first + 3);
    }

    // ===================== 바깥 채움 =====================

    private float ShaftHalfWidth()
    {
        var camera = Camera.main;
        return Mathf.Max(fillHalfWidth, rideLens * (camera != null ? camera.aspect : 16f / 9f) + 5f);
    }

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
        var audio = loop?.GetSource();
        if (audio != null) { motorPitch = audio.pitch; motorVolume = audio.volume; }
    }

    private void SetRushEffects(bool covered, float urgency)
    {
        ShaftDarkness = covered ? 1f : 0f;
        shaftUrgency = urgency;
        if (darkRenderer != null)
        {
            darkRenderer.enabled = covered;
            beamRenderer.enabled = urgency > .015f;
        }
        var audio = loop?.GetSource();
        if (audio != null)
        {
            audio.pitch = motorPitch * Mathf.Lerp(1f, 1.65f, urgency);
            audio.volume = Mathf.Clamp01(motorVolume * Mathf.Lerp(1f, 1.45f, urgency));
        }
        if (rideImpulse != null) rideImpulse.Gain = impulseGain * Mathf.Lerp(1f, 3.2f, urgency);
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
