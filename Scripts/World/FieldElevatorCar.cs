using UnityEngine;

/// <summary>
/// 각개 전투 엘리베이터의 케이지(2026-11-20). 사용자 요청: "Lobby맨위에서 천장이 갈라지면서 엘리베이터가 내려오고 … 엘리베이터를 타고 실시간으로 이동".
/// - 문(좌우로 미는 격자 문 두 짝)은 Animator(bool DoorsOpen)가 이동 Clip으로 연다·닫는다. 크기를 바꾸는 Clip은 쓰지 않는다.
/// - 케이블(LineRenderer 두 줄)은 지붕 도르래에서 위쪽 매단 점까지 이어지고, 움직인 거리만큼 눈금이 흐르고(_ScrollOffset, MaterialPropertyBlock) 도르래가 돈다.
/// - 지붕 경고등(빨강)은 움직이는 동안만 켜지고 불꽃 쐐기가 돈다. 안쪽 표시판(파랑 등 5개·화살표)은 방향과 로딩 진행률을 보여 준다.
/// 위치는 이 컴포넌트가 몰지 않는다. 등장 연출은 FieldElevator가, 이동은 FieldElevatorRide가 루트를 옮기고 AddTravel로 알린다.
/// 판정(Collider)이 없는 표시용 장치다. Player는 모터의 이동 구속으로 실려 가며 케이지와 충돌하지 않는다.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(100)]
public sealed class FieldElevatorCar : MonoBehaviour
{
    public enum PanelMode { Idle, Up, Down, Hold }

    private static readonly int DoorsOpenId = Animator.StringToHash("DoorsOpen");
    private static readonly int OpenedState = Animator.StringToHash("DoorsOpened");
    private static readonly int ClosedState = Animator.StringToHash("DoorsClosed");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int ScrollId = Shader.PropertyToID("_ScrollOffset");

    [Header("구성")]
    [SerializeField, Tooltip("문 Animator(FieldElevatorCar.controller, bool DoorsOpen)")] private Animator animator;
    [SerializeField, Tooltip("케이블 두 줄(세계 좌표, 점 2개)")] private LineRenderer[] cables = new LineRenderer[0];
    [SerializeField, Tooltip("케이블 아래 끝(지붕 도르래 꼭대기). cables와 같은 순서")] private Transform[] cableAnchors = new Transform[0];
    [SerializeField, Tooltip("지붕 도르래(움직인 거리만큼 돈다)")] private Transform[] sheaves = new Transform[0];
    [SerializeField, Min(.01f)] private float sheaveRadius = .22f;
    [SerializeField, Tooltip("케이블 위 끝의 좌우 벌림(매단 점에서 ±)")] private float cableSpread = .4f;
    [SerializeField, Tooltip("지붕 경고등(움직일 때만 켜짐)")] private GameObject[] beacons = new GameObject[0];
    [SerializeField, Tooltip("경고등 불꽃 쐐기(스크립트가 돌린다)")] private Transform[] beaconFlares = new Transform[0];
    [SerializeField, Tooltip("불꽃 쐐기 회전 속도(°/초)")] private float beaconSpin = 540f;
    [SerializeField, Tooltip("안쪽 표시판의 등 5개(아래에서 위 순서)")] private Renderer[] panelLamps = new Renderer[0];
    [SerializeField] private GameObject arrowUp, arrowDown;
    [SerializeField, Tooltip("표시판 등의 꺼진·켜진 밝기(_Intensity)")] private Vector2 panelIntensity = new Vector2(.12f, 1.6f);
    [SerializeField, Tooltip("방향 표시가 한 칸씩 옮겨 가는 빠르기(칸/초)")] private float panelChase = 7f;
    [SerializeField, Tooltip("착지 먼지")] private ParticleSystem landingDust;

    [Header("소리")]
    [SerializeField, Tooltip("0 문 구동, 1 문 잠김, 2 문 풀림(문 Clip의 Animation Event PlayCarCue)")] private SoundOptions[] cues = new SoundOptions[0];
    [SerializeField, Tooltip("케이블 눈금이 움직인 거리에 따라 흐르는 배율")] private float cableTile = 1f;

    private MaterialPropertyBlock block;
    private bool doorsOpen = true;
    private bool pendingInstant = true;
    private bool beaconsOn;
    private Vector3? cableTop;
    private float scroll;
    private float sheaveAngle;
    private PanelMode panel = PanelMode.Idle;
    private float panelProgress;
    private float panelClock;
    private readonly Vector3[] linePoints = new Vector3[2];

    public bool DoorsOpen => doorsOpen;
    public bool BeaconsOn => beaconsOn;
    public PanelMode Panel => panel;
    public Animator Animator => animator;
    /// <summary>문 상태가 끝까지 열렸거나 닫힌 채 멈췄는가(전이 중이 아님).</summary>
    public bool DoorsSettled
    {
        get
        {
            if (animator == null || !animator.isActiveAndEnabled || !animator.isInitialized) return true;
            if (animator.IsInTransition(0)) return false;
            int state = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            return doorsOpen ? state == OpenedState : state == ClosedState;
        }
    }

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        if (animator == null) animator = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        // Animator는 꺼지는 동안 상태를 잃을 수 있으므로 켜질 때 지금 값으로 다시 세운다(움직이는 중이 아니므로 바로 그 자세로).
        pendingInstant = true;
        ApplyDoors(true);
        ApplyBeacons();
        ApplyCable();
    }

    /// <summary>문을 연다·닫는다. instant면 Clip을 건너뛰고 그 자세로 바로 선다(소리 없음). 꺼져 있으면 값만 적고 켜질 때 적용한다.</summary>
    public void SetDoors(bool open, bool instant = false)
    {
        doorsOpen = open;
        if (instant) pendingInstant = true;
        ApplyDoors(instant);
    }

    private void ApplyDoors(bool instant)
    {
        if (animator == null || !animator.isActiveAndEnabled || !animator.isInitialized) return;
        animator.SetBool(DoorsOpenId, doorsOpen);
        if (!instant || !pendingInstant) return;
        pendingInstant = false;
        animator.Play(doorsOpen ? OpenedState : ClosedState, 0, 0f);
        animator.Update(0f);
    }

    /// <summary>케이블 위 끝(매단 점). null이면 케이블을 숨긴다.</summary>
    public void SetCableTop(Vector3? worldTop)
    {
        cableTop = worldTop;
        ApplyCable();
    }

    /// <summary>케이지가 움직인 거리(위 = 양수). 도르래가 돌고 케이블 눈금이 흐른다.</summary>
    public void AddTravel(float distance)
    {
        if (distance == 0f) return;
        sheaveAngle = Mathf.Repeat(sheaveAngle + distance / sheaveRadius * Mathf.Rad2Deg, 360f);
        foreach (var sheave in sheaves) if (sheave != null) sheave.localRotation = Quaternion.Euler(0f, 0f, sheaveAngle);
        scroll = Mathf.Repeat(scroll + distance * cableTile, 1000f);
        if (block == null) block = new MaterialPropertyBlock();
        foreach (var cable in cables)
        {
            if (cable == null) continue;
            cable.GetPropertyBlock(block);
            block.SetVector(ScrollId, new Vector4(scroll, 0f, 0f, 0f));
            cable.SetPropertyBlock(block);
        }
    }

    public void SetBeacons(bool on)
    {
        beaconsOn = on;
        ApplyBeacons();
    }

    private void ApplyBeacons()
    {
        foreach (var beacon in beacons) if (beacon != null && beacon.activeSelf != beaconsOn) beacon.SetActive(beaconsOn);
    }

    /// <summary>안쪽 표시판: Idle = 꺼짐, Up/Down = 방향 화살표와 등이 한 칸씩 흐름, Hold = 진행률만큼 등이 켜짐(입장 로딩을 기다리는 동안).</summary>
    public void SetPanel(PanelMode mode, float progress = 0f)
    {
        panel = mode;
        panelProgress = Mathf.Clamp01(progress);
        if (arrowUp != null && arrowUp.activeSelf != (mode == PanelMode.Up)) arrowUp.SetActive(mode == PanelMode.Up);
        if (arrowDown != null && arrowDown.activeSelf != (mode == PanelMode.Down)) arrowDown.SetActive(mode == PanelMode.Down);
    }

    public void PlayLandingDust()
    {
        if (landingDust != null) { landingDust.Clear(true); landingDust.Play(true); }
    }

    /// <summary>문 Clip의 Animation Event: 0 문 구동, 1 문 잠김, 2 문 풀림.</summary>
    public void PlayCarCue(int index)
    {
        if (cues == null || index < 0 || index >= cues.Length) return;
        var sound = cues[index];
        if (sound != null && sound.clip != null && SoundManager.Instance != null) SoundManager.Instance.PlaySound(sound, transform.position);
    }

    private void LateUpdate()
    {
        // 켜질 때 Animator가 아직 준비되지 않아 자세를 세우지 못했으면 여기서 세운다.
        if (pendingInstant) ApplyDoors(true);
        if (beaconsOn)
        {
            float step = beaconSpin * Time.deltaTime;
            foreach (var flare in beaconFlares) if (flare != null) flare.Rotate(0f, 0f, -step, Space.Self);
        }
        ApplyCable();
        ApplyPanel();
    }

    private void ApplyCable()
    {
        bool visible = cableTop.HasValue;
        for (int i = 0; i < cables.Length; i++)
        {
            var cable = cables[i];
            if (cable == null) continue;
            if (cable.enabled != visible) cable.enabled = visible;
            if (!visible) continue;
            var anchor = i < cableAnchors.Length && cableAnchors[i] != null ? cableAnchors[i] : transform;
            float side = cables.Length > 1 ? (i == 0 ? -1f : 1f) : 0f;
            linePoints[0] = anchor.position;
            linePoints[1] = cableTop.Value + new Vector3(side * cableSpread, 0f, 0f);
            linePoints[1].z = linePoints[0].z;
            if (cable.positionCount != 2) cable.positionCount = 2;
            cable.SetPositions(linePoints);
        }
    }

    private void ApplyPanel()
    {
        if (panelLamps == null || panelLamps.Length == 0) return;
        if (block == null) block = new MaterialPropertyBlock();
        panelClock = Mathf.Repeat(panelClock + Time.deltaTime, 600f);
        int count = panelLamps.Length;
        int lit = Mathf.CeilToInt(panelProgress * count - .001f);
        int chase = Mathf.FloorToInt(panelClock * panelChase) % count;
        for (int i = 0; i < count; i++)
        {
            var lamp = panelLamps[i];
            if (lamp == null) continue;
            bool on = panel switch
            {
                PanelMode.Up => i == chase || i == (chase + count - 1) % count,
                PanelMode.Down => count - 1 - i == chase || count - 1 - i == (chase + count - 1) % count,
                PanelMode.Hold => i < lit,
                _ => false,
            };
            lamp.GetPropertyBlock(block);
            block.SetFloat(IntensityId, on ? panelIntensity.y : panelIntensity.x);
            lamp.SetPropertyBlock(block);
        }
    }
}
