using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 붙기 크레인 공통(DESIGN_V4 §2.2~2.4 · 5차 DESIGN_V5 §1 크레인) — 경로 크레인 PropCraneHook·고정 크레인 PropCraneAnchor가 상속한다.
/// - 배치: footprint 2×4, (x, y) = 왼쪽 아래 칸, 집 발 = (x + 1, y) → 루트(footprint 가운데) 기준 로컬 (0, −2)(HangFeetLocal — 경로 크레인 매달림·차지 칸 기준).
///   몸 1.5×1.5 + 머리가 그 위 4칸 안. 7차 V11: 고정 크레인은 ZipFeet를 재정의해 몸 중심이 그래플 고리 중심에 오게 매단다(PropCraneAnchor.HangLocal (0, −0.655)).
/// - 타는 방법은 붙기(FacilityZip)뿐이다. Interact 트리거가 없고(InteractAbility의 '가까운 Collider 하나' 선택과 겹치지 않는다), 붙기 대상으로 '선택'됐을 때만
///   크레인 쪽(경로 크레인 = 케이지 안, 고정 크레인 = 고리 안)에 기존 키 안내(RoomTrigger 사본)를 띄운다. 도착하면 FacilityRide.BeginHandoff로 넘겨받는다 —
///   보호 없는 구속(피해를 받고 잡기만 막음), 입력을 막지 않아 매달린 채 무장을 쓴다. 매달림은 구속 바닥 접지라 점프 횟수가 다시 찬다(기존 동작).
/// - 내리기: 점프 → 내리며 점프(JumpAbility.Jumped, 못 읽은 프레임엔 InputManager.Pressed(Jump) — 3차와 같음), 대시 → 내리며 그 방향 대시(DashAbility.IsActive를 보고
///   구속만 푼다 — 대시 속도는 대시가 이어서 넣는다), 상호작용(↑) → 다른 크레인으로 연쇄 붙기(FacilityZip이 ReleaseForZip을 부른다).
///   던전 조건을 채운 순간(FacilityAccess.CombatAllows false) 스스로 내려 준다. 사망·Wave 정지·구속 해제(FacilityRide.Interrupted)면 끊는다.
/// - 5차 표시(S6): 반경 고리를 없앴다. 선택된 대상에만 Player → 대상 직선(흐르는 점선, 굵기 0.05, 크레인 쪽으로 3칸/초) + 표적 괄호 4개(이동·회전만)를 그린다.
///   ↑를 받아 확정을 기다리는 0.08초는 밝게(1.6), 평소 선택은 0.9. 반경 안이지만 선택되지 않은 크레인은 흐린 표적 괄호(0.3)만 둔다.
///   6차 T4(크레인을 가리키게): 직선·표적 괄호의 기준은 '매달릴 몸 자리'가 아니라 크레인의 잡는 부분 = 표시 기준점 G(AimPoint — 판정·이동 목표와 무관,
///   붙기 반경·점수·이동은 몸 중심 그대로). 고정 크레인 = 그래플 고리 중심(흔들림을 따라감), 경로 크레인 = 머리(스프레더) 중심, 빈 도크 = 생겨날 머리 자리.
///   표적 괄호는 G를 두른 종류별 크기 상자(AimFrameHalf — 생성 도구가 크기별 Mesh를 굽는다, Scale 미사용), 직선 끝 = 몸 중심 → G 선이 그 상자와 만나는 점
///   (시작 여백 0.55·최소 길이 0.35 유지 — 가까우면 시작 여백부터 줄이고, 상자 밖 길이가 0.35보다 짧으면 괄호만).
/// - 5차 붙기 이동 연출(S5, 대시와 다르게): 출발 때 몸 중심의 십자 섬광 0.08초 + 청록 불꽃 + 출발음(TerrainPanelDrive 피치 1.5),
///   와이어 장력(가닥 간격 0.12 → 0.04로 모이며 밝기 1.2 → 2.0), 출발점 → 몸의 곧은 견인 띠(파랑 꼬리 → 청록 → 흰 머리, 정점만 다시 채움), 크레인 쪽으로 모이는 속도선.
///   도착하면 띠 꼬리가 0.12초에 머리를 따라잡으며 사라진다. 히트스톱은 넣지 않는다.
/// - 5차 탑승 타격감(S7): 도착 화면 흔들림 요청 0.45(경로 크레인 × 1.2, 0.5초 안 연쇄 × 0.6 — DungeonObjectiveShake 임펄스, 거리 감쇠 없음),
///   도착음 TerrainPanelLock 피치 0.9 + 기존 ContainmentLock(경로 크레인은 LobbyUpgradeAssembleBolt 추가), 크레인 흔들림 = 회전만 감쇠 진동
///   A·e^(−t/0.3)·sin(2πt/0.45), A = 4°(부호 = 붙기 진행 x 방향). Player는 움직이지 않는다. 기계 몸체는 회전·이동·밝기만 바꾼다(Scale 변형 없음).
/// - 물리 질의 없음(이동은 FacilityRide·FacilityZip이 모터 구속으로만 한다). 표시 부품의 Transform 이동·회전은 표시 전용이다.
/// </summary>
[DefaultExecutionOrder(100)]
public abstract class ZipCraneBase : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings, IDungeonRoomPowered, IFacilityHighlightSource, IZipAnchor
{
    public const string ZipRadiusKey = "zipRadius";
    /// <summary>
    /// 루트 기준 집 발(로컬). footprint 2×4의 아래 변 가운데 — 경로 크레인 매달림(경로 점 0)·차지 칸·격자 Tool 기둥 그림의 기준.
    /// 7차 V11: 고정 크레인의 매달림은 PropCraneAnchor.HangLocal(ZipFeet 재정의 — 1.345칸 위, 몸 중심 = 그래플 고리 중심)이다.
    /// </summary>
    public static readonly Vector2 HangFeetLocal = new Vector2(0f, -2f);
    /// <summary>Player 몸통 반높이(1.51의 절반 — 직선·표적 괄호·키 안내 기준).</summary>
    public const float BodyHalfHeight = .755f;
    /// <summary>5차 판단값: 도착 화면 흔들림 요청 세기·연쇄 감쇠(DESIGN_V5 §1).</summary>
    public const float GrabShake = .45f, ChainShakeScale = .6f, ChainShakeWindow = .5f;
    /// <summary>5차 판단값: 크레인 흔들림(회전만) 진폭·주기·감쇠 시간.</summary>
    public const float KickDegrees = 4f, KickPeriod = .45f, KickDecay = .3f;

    /// <summary>6차 T5: 호출 이동(Calling)은 없앴다 — 빈 도크를 고르면 크레인이 그 도크에 즉시 생긴다(PropCraneHook.Summon).</summary>
    public enum Phase { Idle, Riding, Waiting, Returning }

    [Header("붙기")]
    [SerializeField, Range(4f, 14f), Tooltip("붙기 반경 기본값(칸, 배치 설정 zipRadius).")] protected float zipRadius = 8f;
    [SerializeField, Tooltip("전투 중에도 쓰는지(Anytime) 방 클리어 뒤에만 쓰는지.")] protected FacilityUse use = FacilityUse.Anytime;
    [SerializeField, Tooltip("매달린 채 점프하면 내리며 점프한다.")] protected bool jumpToDismount = true;
    [SerializeField, Tooltip("매달린 채 대시하면 내리며 그 방향으로 대시한다.")] protected bool dashToDismount = true;

    [Header("구성(생성 도구가 연결)")]
    [SerializeField, Tooltip("훅 머리(기울기 회전).")] protected Transform head;
    [SerializeField] protected Transform[] claws = Array.Empty<Transform>();
    [SerializeField] protected float clawClosedAngle = 24f;
    [SerializeField, Tooltip("운반 케이지 브래킷(경로 크레인만 — 고정 크레인은 비운다).")] protected Transform[] cageBrackets = Array.Empty<Transform>();
    [SerializeField] protected float cageSpread = .3f;
    [SerializeField] protected Renderer[] cageLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("훅 비컨(대기 점멸, 선택되면 빠르게).")] protected Renderer[] beaconLights = Array.Empty<Renderer>();
    [SerializeField] protected Renderer[] powerLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("붙는 동안 훅에서 Player로 뻗는 와이어(실행 중 다시 그림).")] protected MeshFilter zipWire;
    [SerializeField] protected Renderer[] zipWireLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("와이어가 나가는 훅 끝(머리 아래).")] protected Transform hookPoint;
    [SerializeField, Tooltip("선택됐을 때만 보이는 키 안내(RoomTrigger 버튼 그림 사본, 크레인 쪽으로 따라간다).")] protected CanvasGroup zipPrompt;
    [SerializeField] protected ParticleSystem arrivalSparks;
    [SerializeField, Tooltip("점프로 내릴 때 넣는 점프 속도·횟수(Player_Jump_Data).")] protected PlayerJumpDataSO jumpData;

    [Header("5차 붙기 표시(생성 도구가 연결)")]
    [SerializeField, Tooltip("Player → 선택 대상 직선(흐르는 점선, 실행 중 다시 그림).")] protected MeshFilter aimLine;
    [SerializeField] protected Renderer[] aimLineLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("표적 괄호 4개 묶음(원점 = 표시 기준점 G — 크레인의 잡는 부분, 이동·회전만).")] protected Transform aimBrackets;
    [SerializeField] protected Renderer[] aimBracketLights = Array.Empty<Renderer>();

    [Header("5차 붙기 이동 연출(생성 도구가 연결)")]
    [SerializeField, Tooltip("견인 띠 꼬리(파랑).")] protected MeshFilter towBand;
    [SerializeField] protected Renderer[] towBandLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("견인 띠 가운데(청록).")] protected MeshFilter towCore;
    [SerializeField] protected Renderer[] towCoreLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("견인 띠 머리(흰색).")] protected MeshFilter towHead;
    [SerializeField] protected Renderer[] towHeadLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("와이어 걸림 십자 섬광(원점 = 섬광 가운데, 이동·회전만).")] protected Transform launchFlash;
    [SerializeField] protected Renderer[] launchFlashLights = Array.Empty<Renderer>();
    [SerializeField, Tooltip("크레인 쪽으로 모이는 속도선(Stretch 입자, 코드가 Emit).")] protected ParticleSystem zipStreaks;
    [SerializeField, Tooltip("와이어 걸림 청록 불꽃(코드가 Emit).")] protected ParticleSystem zipFlare;

    [Header("표시")]
    [SerializeField, Min(0f)] protected float wireWidth = .03f;
    [SerializeField, Min(0f)] protected float wireSpacing = .12f;
    [SerializeField, Min(0f), Tooltip("장력: 가닥이 모인 간격.")] protected float wireTightSpacing = .04f;
    [SerializeField, Min(.01f)] protected float wireExtendTime = .05f;
    [SerializeField, Min(0f)] protected float swayDegrees = 2.2f;
    [SerializeField, Min(0f)] protected float aimWidth = .05f;
    [SerializeField, Min(.01f)] protected float aimDash = .32f;
    [SerializeField, Min(0f)] protected float aimGap = .18f;
    [SerializeField, Min(0f), Tooltip("점선이 크레인 쪽으로 흐르는 속도(칸/초).")] protected float aimFlow = 3f;
    [SerializeField, Min(0f), Tooltip("직선 시작을 몸 중심에서 띄우는 거리(칸).")] protected float aimStartGap = .55f;
    // 6차 T4: 직선 끝 여백(aimEndGap)은 없앴다 — 직선은 표적 괄호 상자 가장자리에서 끝난다.
    [SerializeField, Min(0f)] protected float bracketSpin = 30f;          // 도/초(선택됐을 때만)
    [SerializeField, Min(0f)] protected float candidateLevel = .3f;       // 반경 안이지만 선택되지 않은 크레인의 표적 괄호 밝기
    [SerializeField, Min(0f)] protected float focusLevel = .9f;
    [SerializeField, Min(0f)] protected float lockedLevel = 1.6f;

    [Header("효과(기존 효과음·효과 재사용)")]
    [SerializeField] protected FeedbackDataSO grabFeedback;
    [SerializeField] protected FeedbackDataSO releaseFeedback;
    [SerializeField] protected SoundOptions powerOnSound;
    [SerializeField, Tooltip("5차 붙기 출발음(TerrainPanelDrive 피치 1.5).")] protected SoundOptions zipLaunchSound;
    [SerializeField, Tooltip("5차 도착 체결음(TerrainPanelLock 피치 0.9 — ContainmentLock과 겹친다).")] protected SoundOptions grabLockSound;
    [SerializeField, Tooltip("5차 무거운 체결음(경로 크레인만 LobbyUpgradeAssembleBolt).")] protected SoundOptions grabHeavySound;

    protected FacilityRide ride;
    protected bool cached;
    protected bool isLive;
    protected bool powered = true;
    protected float power = 1f;
    protected Phase phase;
    protected float phaseTime;
    protected float clock;
    protected float lean;
    protected float cageClose, clawClose;
    protected Vector2 travelVelocity;
    protected float activeRadius = 8f;
    private bool focused;
    private PlayerController incoming;
    private float incomingTime;
    private Vector2 zipStartBody;
    private Vector2 lastIncomingBody;
    private Quaternion[] clawHome = Array.Empty<Quaternion>();
    private Vector3[] cageHome = Array.Empty<Vector3>();
    private FacilityLights cageLamp, beaconLamp, powerLamp, wireLamp, aimLamp, bracketLamp, towBandLamp, towCoreLamp, towHeadLamp, flashLamp;
    private Mesh wireBuilt, aimBuilt, towBandBuilt, towCoreBuilt, towHeadBuilt;
    private string wireMeshName, aimMeshName, towBandName, towCoreName, towHeadName;   // Mesh 이름은 처음 그릴 때 한 번만 만든다(매 프레임 문자열 할당 방지, fix4zip)
    private bool wireShown, aimShown, towShown;
    private float aimOffset;
    private float flashTime = -1f;
    private float towFade = -1f;                 // 도착 뒤 꼬리가 머리를 따라잡는 시간(−1 = 진행 중 아님)
    private float kickTime = -1f, kickSign = 1f;
    private static float lastGrabShake = -999f;
    private JumpAbility jumpSource;
    private FacilityHighlight highlight;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => lastGrabShake = -999f;

    public Phase CurrentPhase => phase;
    public FacilityUse Use => use;
    public FacilityRide Ride => ride;
    public bool IsRiding => ride != null && ride.IsRiding;
    public bool Powered => powered;
    /// <summary>점프·대시로 내린 횟수(검사용, 대여마다 0).</summary>
    public int Dismounts { get; protected set; }
    /// <summary>대시로 내린 횟수(검사용).</summary>
    public int DashDismounts { get; protected set; }
    /// <summary>조건 충족으로 스스로 내려 준 횟수(검사용).</summary>
    public int AutoDrops { get; protected set; }
    /// <summary>붙기 대상으로 선택된 표시 중인지(검사용).</summary>
    public bool ZipFocused => focused;
    /// <summary>키 안내 투명도(검사용).</summary>
    public float PromptAlpha => zipPrompt != null ? zipPrompt.alpha : 0f;
    /// <summary>키 안내 자리(월드, 검사용).</summary>
    public Vector2 PromptPosition => zipPrompt != null ? (Vector2)zipPrompt.transform.position : Vector2.zero;
    /// <summary>비컨 밝기(검사용).</summary>
    public float BeaconLevel { get; private set; }
    /// <summary>와이어가 보이는지(검사용).</summary>
    public bool WireShown => wireShown;
    /// <summary>와이어 가닥 간격·밝기(검사용 — 장력 표현).</summary>
    public float WireSpacingNow { get; private set; }
    public float WireLevel { get; private set; }
    /// <summary>5차: 직선이 보이는지·밝기·시작/끝(월드, 검사용).</summary>
    public bool AimShown => aimShown;
    public float AimLevel { get; private set; }
    public Vector2 AimStart { get; private set; }
    public Vector2 AimEnd { get; private set; }
    /// <summary>5차: 표적 괄호 밝기·중심(월드, 검사용). 6차 T4: 중심 = 표시 기준점 G.</summary>
    public float BracketLevel { get; private set; }
    public Vector2 BracketCenter => aimBrackets != null ? (Vector2)aimBrackets.position : Vector2.zero;
    /// <summary>6차 T4: 표적 괄호 상자 반폭·반높이(종류별 — 고정 ±1.2 × ±1.2(7차 V11 — 커진 고리), 경로·도크 ±1.25 × ±0.6).</summary>
    public Vector2 BracketHalfSize => AimFrameHalf;
    /// <summary>6차 T4: 이 크레인 자신의 표시 기준점 G(월드 — 잡는 부분, 검사용).</summary>
    public Vector2 AimPointNow => AimPoint;
    /// <summary>5차: 견인 띠가 보이는지·섬광 횟수·속도선 수(검사용).</summary>
    public bool TowShown => towShown;
    public int LaunchFlashes { get; private set; }
    public int StreakEmits { get; private set; }
    /// <summary>5차: 도착 흔들림 임펄스 횟수·마지막 요청 세기(검사용).</summary>
    public int GrabImpulses { get; private set; }
    public float LastGrabImpulse { get; private set; }
    /// <summary>5차: 지금 크레인 흔들림 각도(도, 회전만)·마지막 도착 뒤 최대 절댓값(검사용).</summary>
    public float KickAngle { get; private set; }
    public float KickPeak { get; private set; }

    /// <summary>집(점 0)의 매달린 발(월드).</summary>
    public Vector2 HomeFeet => transform.TransformPoint(HangFeetLocal);

    // ── IZipAnchor ─────────────────────────────

    public virtual bool ZipAvailable => isLive && isActiveAndEnabled && phase == Phase.Idle && !IsRiding && Usable && FacilityAccess.Allows(use);
    public virtual Vector2 ZipFeet => HomeFeet;
    public float ZipRadius => activeRadius;
    public Vector2 ZipHookPoint => hookPoint != null ? (Vector2)hookPoint.position : HomeFeet + Vector2.up * 1.6f;
    public bool HasRider(PlayerController player) => player != null && ride != null && ride.IsRiding && ride.Rider == player;
    public bool FacilityReady => ZipAvailable;
    protected bool Usable => use == FacilityUse.Anytime || powered;
    /// <summary>쓸 수 있는 시점인지(살아 있음·켜짐·전원·사용 시점 허락 — 단계와 무관). 경로 크레인의 도크 호출 지점이 쓴다.</summary>
    protected bool ReadyToServe => isLive && isActiveAndEnabled && Usable && FacilityAccess.Allows(use);
    /// <summary>6차 T5: Player가 이 크레인으로 붙어 오는 중인지(BeginZipIn ~ EndZipIn — 그동안 크레인을 다른 도크로 옮기지 않는다).</summary>
    protected bool ZipIncoming => incoming != null;

    public void SetZipFocus(bool on)
    {
        if (focused == on) return;
        focused = on;
        RefreshFocus();
    }

    // 선택 표시(키 안내·강조)를 지금 선택 상태로 맞춘다(경로 크레인은 도크 호출 지점 선택도 포함).
    protected void RefreshFocus()
    {
        bool on = AnyFocus;
        if (highlight != null) highlight.SetFocus(on);
        if (zipPrompt == null) return;
        zipPrompt.DOKill();
        zipPrompt.DOFade(on ? 1f : 0f, on ? .2f : .15f);
    }

    /// <summary>이 크레인(또는 그 도크 호출 지점)이 선택돼 있는지.</summary>
    protected virtual bool AnyFocus => focused;

    public void BeginZipIn(PlayerController player)
    {
        incoming = player;
        incomingTime = 0f;
        towFade = -1f;
        if (player == null) return;
        zipStartBody = lastIncomingBody = ShownBodyCenter(player);
        // 출발 연출: 몸 중심의 십자 섬광(크레인 쪽으로 향함)·청록 불꽃·출발음.
        Vector2 toward = ZipHookPoint - zipStartBody;
        if (launchFlash != null)
        {
            launchFlash.position = new Vector3(zipStartBody.x, zipStartBody.y, launchFlash.position.z);
            launchFlash.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(toward.y, toward.x) * Mathf.Rad2Deg);
        }
        flashTime = 0f;
        LaunchFlashes++;
        if (zipFlare != null)
        {
            zipFlare.transform.position = new Vector3(zipStartBody.x, zipStartBody.y, zipFlare.transform.position.z);
            FacilityFeedback.Burst(zipFlare, 6);
        }
        FacilityFeedback.Play(zipLaunchSound, zipStartBody);
    }

    public void EndZipIn(bool arrived)
    {
        var player = incoming;
        incoming = null;
        HideWire();
        if (!arrived) { HideTow(); return; }
        // 견인 띠: 꼬리가 0.12초에 머리를 따라잡으며 사라진다.
        towFade = towShown ? 0f : -1f;
        Vector2 at = ZipHookPoint;
        FacilityFeedback.Play(grabFeedback, at);
        FacilityFeedback.Play(grabLockSound, at);
        FacilityFeedback.Play(grabHeavySound, at);
        FacilityFeedback.Burst(arrivalSparks, 14);
        // 화면 흔들림(거리 감쇠 없음 — 붙은 사람이 Player라 카메라가 따라온다). 0.5초 안 연쇄면 약하게.
        float strength = GrabShake * ShakeScale * (Time.time - lastGrabShake < ChainShakeWindow ? ChainShakeScale : 1f);
        lastGrabShake = Time.time;
        if (DungeonObjectiveShake.Impulse(strength, 0f, at)) { GrabImpulses++; LastGrabImpulse = strength; }
        // 크레인 흔들림(회전만): 부호 = 붙기 진행 x 방향.
        float dx = ZipFeet.x - (player != null ? zipStartBody.x : ZipFeet.x);
        kickSign = Mathf.Abs(dx) > .05f ? Mathf.Sign(dx) : 1f;
        kickTime = 0f;
        KickPeak = 0f;
        OnGrabbed();
    }

    /// <summary>5차: 화면 흔들림 배율(경로 크레인 1.2 — 무거운 운반기).</summary>
    protected virtual float ShakeScale => 1f;

    /// <summary>5차: 도착 체결 표시(파생: 고정 크레인 잠금등 차례 켜짐, 경로 크레인 케이지 표시등 번쩍임).</summary>
    protected virtual void OnGrabbed() { }

    public bool AcceptZip(PlayerController player)
    {
        Cache();
        if (!isLive || !isActiveAndEnabled || phase != Phase.Idle || IsRiding || !Usable) return false;
        if (!ride.BeginHandoff(player, use, false)) return false;
        WatchJump(jumpToDismount && player.AbilityManager != null ? player.AbilityManager.JumpAbility : null);
        SetZipFocus(false);
        OnRideStarted();
        return IsRiding;
    }

    public void ReleaseForZip(PlayerController player)
    {
        if (!HasRider(player)) return;
        EndRide(false);
    }

    // ── 수명 ─────────────────────────────

    protected virtual void Awake() => Cache();

    protected void Cache()
    {
        if (cached) return;
        cached = true;
        ride = new FacilityRide(this);
        clawHome = new Quaternion[claws.Length];
        for (int i = 0; i < claws.Length; i++) clawHome[i] = claws[i] != null ? claws[i].localRotation : Quaternion.identity;
        cageHome = new Vector3[cageBrackets.Length];
        for (int i = 0; i < cageBrackets.Length; i++) cageHome[i] = cageBrackets[i] != null ? cageBrackets[i].localPosition : Vector3.zero;
        cageLamp = new FacilityLights(cageLights);
        beaconLamp = new FacilityLights(beaconLights);
        powerLamp = new FacilityLights(powerLights);
        wireLamp = new FacilityLights(zipWireLights);
        aimLamp = new FacilityLights(aimLineLights);
        bracketLamp = new FacilityLights(aimBracketLights);
        towBandLamp = new FacilityLights(towBandLights);
        towCoreLamp = new FacilityLights(towCoreLights);
        towHeadLamp = new FacilityLights(towHeadLights);
        flashLamp = new FacilityLights(launchFlashLights);
        highlight = GetComponent<FacilityHighlight>();
        if (zipPrompt != null)
        {
            WorldInteractionBindingHint.Bind(zipPrompt);
            zipPrompt.alpha = 0f;
        }
        OnCache();
        ReadSettings(WaveDeviceSettings.None);
    }

    /// <summary>파생 시설의 추가 캐시(Cache 안에서 한 번).</summary>
    protected virtual void OnCache() { }

    protected virtual void OnDisable()
    {
        // 풀 반환(SetActive false)·장면 정리로 코루틴이 끊겨도 구속을 남기지 않는다.
        EndRide(true);
        FacilityZip.Unregister(this);
        SetZipFocus(false);
        incoming = null;
    }

    // 6차: 대여 중(isLive)에 꺼졌다가 다시 켜지면(방 보류 등 — 풀 반환은 OnPropReturning이 먼저 isLive를 끈다) OnDisable이 내린 붙기 목록에 다시 올린다.
    // 처음 켜질 때·풀에서 다시 나올 때는 OnPropSpawned가 올린다.
    protected virtual void OnEnable()
    {
        if (!isLive || !Application.isPlaying) return;
        FacilityZip.Register(this);
        OnReenabled();
    }

    /// <summary>6차: 대여 중 다시 켜졌을 때 파생 시설의 추가 등록(경로 크레인은 도크 호출 지점).</summary>
    protected virtual void OnReenabled() { }

    protected virtual void OnDestroy()
    {
        EndRide(true);
        FacilityZip.Unregister(this);
        FacilityMesh.Release(ref wireBuilt);
        FacilityMesh.Release(ref aimBuilt);
        FacilityMesh.Release(ref towBandBuilt);
        FacilityMesh.Release(ref towCoreBuilt);
        FacilityMesh.Release(ref towHeadBuilt);
    }

    public virtual void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        isLive = true;
        powered = true;
        power = 1f;
        phase = Phase.Idle;
        phaseTime = 0f;
        clock = 0f;
        lean = 0f;
        cageClose = clawClose = 0f;
        travelVelocity = Vector2.zero;
        Dismounts = DashDismounts = AutoDrops = 0;
        GrabImpulses = LaunchFlashes = StreakEmits = 0;
        LastGrabImpulse = KickAngle = KickPeak = 0f;
        kickTime = flashTime = towFade = -1f;
        focused = false;
        incoming = null;
        if (zipPrompt != null) { zipPrompt.DOKill(); zipPrompt.alpha = 0f; }
        HideWire();
        HideAim();
        HideTow();
        flashLamp?.Set(0f);
        OnSpawned();
        ApplyPose(0f);
        FacilityZip.Register(this);
    }

    /// <summary>파생 시설의 생성 초기화(OnPropSpawned 안, 등록 전).</summary>
    protected virtual void OnSpawned() { }

    public virtual void OnPropReturning(DungeonProp prop)
    {
        EndRide(true);
        FacilityZip.Unregister(this);
        SetZipFocus(false);
        isLive = false;
        incoming = null;
        HideWire();
        HideAim();
        HideTow();
        cageLamp?.Restore();
        beaconLamp?.Restore();
        powerLamp?.Restore();
        wireLamp?.Restore();
        aimLamp?.Restore();
        bracketLamp?.Restore();
        towBandLamp?.Restore();
        towCoreLamp?.Restore();
        towHeadLamp?.Restore();
        flashLamp?.Restore();
        FacilityFeedback.Emit(zipStreaks, false);
        OnReturning();
    }

    /// <summary>파생 시설의 반환 정리.</summary>
    protected virtual void OnReturning() { }

    public void SetRoomPowered(bool on, bool instant)
    {
        Cache();
        bool was = powered;
        powered = on;
        if (instant) power = on ? 1f : 0f;
        else if (on && !was) FacilityFeedback.Play(powerOnSound, transform.position);
    }

    // ── 탑승 ─────────────────────────────

    /// <summary>인계를 받은 직후(파생: 경로 크레인은 출발 지연 뒤 이동, 고정 크레인은 매달림 대기).</summary>
    protected abstract void OnRideStarted();

    /// <summary>탑승이 끝난 직후(파생: 경로 크레인은 도착 도크에서 쉬거나 출발 도크로 복귀).</summary>
    protected abstract void OnRideEnded(bool interrupted);

    /// <summary>탑승을 끝낸다(여러 번 불러도 한 번만). interrupted면 몸이 지형과 겹칠 때 출발 자리로 옮긴 뒤 푼다(FacilityRide.End).</summary>
    protected void EndRide(bool interrupted)
    {
        WatchJump(null);
        bool was = ride != null && ride.IsRiding;
        StopRideMotion();
        ride?.End(interrupted && was);
        if (!was) return;
        FacilityFeedback.Play(releaseFeedback, ZipHookPoint);
        OnRideEnded(interrupted);
    }

    /// <summary>진행 중인 탑승 이동(코루틴)을 멈춘다.</summary>
    protected virtual void StopRideMotion() { }

    private void WatchJump(JumpAbility jump)
    {
        if (jumpSource == jump) return;
        if (jumpSource != null) jumpSource.Jumped -= OnRiderJumped;
        jumpSource = jump;
        if (jumpSource != null) jumpSource.Jumped += OnRiderJumped;
    }

    // Player 자신의 점프가 나갔다(입력을 막지 않으므로 구속 안에서 점프 속도가 이미 들어갔다) → 구속을 풀어 그대로 뛰어오르게 한다.
    private void OnRiderJumped(bool extraJump) => Dismount(false);

    /// <summary>
    /// 탑승을 끝내고 뛰어내린다(점프 키와 같은 경로, 검사에서도 쓴다). applyJump면 점프 속도·점프 Trigger를 직접 넣는다
    /// (Player의 점프가 입력을 읽지 못했을 때). 탑승 중이 아니면 false.
    /// </summary>
    public bool Dismount(bool applyJump)
    {
        if (ride == null || !ride.IsRiding) return false;
        var player = ride.Rider;
        EndRide(false);
        Dismounts++;
        var jump = player != null && player.AbilityManager != null ? player.AbilityManager.JumpAbility : null;
        int total = jumpData != null ? jumpData.maxJumpCount : 2;
        if (applyJump && player != null)
        {
            if (player.AbilityManager != null && player.AbilityManager.DashAbility != null) player.AbilityManager.DashAbility.Interrupt();
            player.Velocity.y = jumpData != null ? jumpData.jumpForce : 10f;
            if (jump != null) jump.ClearPendingInput();
            if (player.animator != null) player.animator.SetTrigger("Jump");
        }
        // 크레인에서 뛰어오른 점프는 땅에서 뛴 것으로 친다(공중 점프 한 번이 남는다 — WaveJumpPad와 같은 방식).
        if (jump != null) jump.currentJumpCount = Mathf.Max(jump.currentJumpCount, Mathf.Max(0, total - 1));
        return true;
    }

    /// <summary>대시로 내린다(대시 속도는 대시가 이미 넣었다 — 구속만 푼다). 탑승 중이 아니면 false. 검사에서도 쓴다.</summary>
    public bool DismountForDash()
    {
        if (ride == null || !ride.IsRiding) return false;
        EndRide(false);
        Dismounts++;
        DashDismounts++;
        return true;
    }

    // 던전 조건을 채운 순간 스스로 내려 준다(그 자리에서 떨어진다 — 몸이 지형과 겹치면 출발 자리로).
    private void Drop()
    {
        if (ride == null || !ride.IsRiding) return;
        bool blocked = ride.BlockedAt(ride.Feet);
        EndRide(blocked);
        AutoDrops++;
    }

    // ── 매 프레임 ─────────────────────────────

    protected virtual void Update()
    {
        if (!isLive) return;
        float dt = Time.deltaTime;
        if (ride != null && ride.IsRiding)
        {
            var rider = ride.Rider;
            var dash = rider != null && rider.AbilityManager != null ? rider.AbilityManager.DashAbility : null;
            if (ride.Interrupted) EndRide(true);
            else if (!FacilityAccess.CombatAllows) Drop();
            else if (dashToDismount && dash != null && dash.IsActive) DismountForDash();
            // 점프 키로 내리기: 보통은 Player 자신의 점프(Jumped 이벤트)가 먼저 받는다. 그 점프가 입력을 읽지 않은 프레임(키가 아직 소비되지 않음)에만 직접 읽는다.
            else if (jumpToDismount && rider != null && rider.CanReceiveInput && InputManager.Pressed(Button.Jump)) Dismount(true);
        }
        phaseTime += dt;
        clock += dt;
        if (incoming != null) incomingTime += dt;
        power = Mathf.MoveTowards(power, Usable ? 1f : 0f, dt / .4f);
        bool holding = IsRiding;
        clawClose = Mathf.MoveTowards(clawClose, holding ? 1f : 0f, dt / .16f);
        cageClose = Mathf.MoveTowards(cageClose, holding ? 1f : 0f, dt / .2f);
        TickKick(dt);
        TickMotion(dt);
        UpdateLights(dt);
    }

    /// <summary>파생 시설의 매 프레임 움직임(복귀·빛 통로 등).</summary>
    protected virtual void TickMotion(float dt) { }

    // 도착 뒤 감쇠 진동(회전 각도만): θ(t) = A·e^(−t/τ)·sin(2πt/T). 약 0.9초 뒤 5% 미만이 되면 멈춘다.
    private void TickKick(float dt)
    {
        if (kickTime < 0f) { KickAngle = 0f; return; }
        kickTime += dt;
        float envelope = Mathf.Exp(-kickTime / KickDecay);
        KickAngle = kickSign * KickDegrees * envelope * Mathf.Sin(2f * Mathf.PI * kickTime / KickPeriod);
        KickPeak = Mathf.Max(KickPeak, Mathf.Abs(KickAngle));
        if (envelope < .02f) { kickTime = -1f; KickAngle = 0f; }
    }

    private void UpdateLights(float dt)
    {
        bool available = ZipAvailable;
        // 비컨: 선택되면 빠르게(0.3초), 대기 중엔 천천히(1초) 점멸, 탑승 중 켜짐, 복귀·사용 불가면 흐리게.
        float beacon;
        if (IsRiding) beacon = 1f;
        else if (!available) beacon = .2f * Mathf.Max(.25f, power);
        else
        {
            float period = focused ? .3f : 1f;
            float wave = Mathf.Repeat(clock, period) / period;
            beacon = wave < .45f ? 1f : .22f;
        }
        BeaconLevel = beacon;
        beaconLamp?.Set(beacon);
        powerLamp?.Set(Mathf.Lerp(.12f, 1f, power));
        cageLamp?.Set(CageLevel());
        // 십자 섬광 0.08초(밝기만).
        if (flashTime >= 0f)
        {
            flashTime += dt;
            float t = flashTime / .08f;
            flashLamp?.Set(t < 1f ? 2.2f * (1f - t) : 0f);
            if (t >= 1f) flashTime = -1f;
        }
    }

    /// <summary>케이지 표시등 밝기(파생: 경로 크레인은 체결 때 0.2초 번쩍임).</summary>
    protected virtual float CageLevel() => cageClose * 1.4f;

    protected virtual void LateUpdate()
    {
        if (!isLive) return;
        float dt = Time.deltaTime;
        ApplyPose(dt);
        DrawWire();
        DrawTow(dt);
        DrawAim(dt);
        PlacePrompt();
    }

    /// <summary>머리·집게·케이지 자세(파생 시설이 머리·수레 자리를 먼저 놓는다).</summary>
    protected virtual void ApplyPose(float dt)
    {
        if (head != null)
        {
            float wantLean = phase == Phase.Riding ? Mathf.Clamp(-travelVelocity.x * .9f, -10f, 10f) * .35f : IsRiding ? 0f : swayDegrees * power * Mathf.Sin(clock * 1.7f);
            lean = dt > 0f ? Mathf.Lerp(lean, wantLean, 1f - Mathf.Exp(-dt * 6f)) : wantLean;
            head.localRotation = Quaternion.Euler(0f, 0f, HeadLeansItself ? lean + KickAngle : 0f);
        }
        float clawEase = clawClose * clawClose * (3f - 2f * clawClose);
        for (int i = 0; i < claws.Length; i++)
            if (claws[i] != null) claws[i].localRotation = clawHome[i] * Quaternion.Euler(0f, 0f, (i % 2 == 0 ? -1f : 1f) * clawClosedAngle * clawEase);
        float cageEase = cageClose * cageClose * (3f - 2f * cageClose);
        for (int i = 0; i < cageBrackets.Length; i++)
        {
            if (cageBrackets[i] == null) continue;
            Vector3 home = cageHome[i];
            float side = Mathf.Abs(home.x) > .001f ? Mathf.Sign(home.x) : 0f;
            cageBrackets[i].localPosition = home + new Vector3(side * cageSpread * (1f - cageEase), .15f * (1f - cageEase), 0f);
            if (cageBrackets[i].gameObject.activeSelf != cageClose > .01f) cageBrackets[i].gameObject.SetActive(cageClose > .01f);
        }
    }

    /// <summary>머리 자체가 기울어지는지(고정 크레인은 매단 팔 전체가 흔들려 머리는 그대로 둔다 — 흔들림은 Swing 회전에 더한다).</summary>
    protected virtual bool HeadLeansItself => true;

    /// <summary>탑승 중 Player의 화면 보간 발 위치(물리 위치를 쓰면 프레임마다 계단처럼 끊긴다).</summary>
    protected bool TryRiderShownFeet(out Vector2 feet)
    {
        feet = default;
        var rider = ride != null ? ride.Rider : null;
        if (rider == null) return false;
        FixedMotionBody.GetPresentationPose(rider.transform, out var shown, out _);
        Rect body = FacilityAccess.BodyRect(rider);
        feet = (Vector2)shown + new Vector2(body.center.x, body.yMin) - (Vector2)rider.transform.position;
        return true;
    }

    /// <summary>Player 몸 중심의 화면 보간 위치(표시용).</summary>
    protected static Vector2 ShownBodyCenter(PlayerController player)
    {
        FixedMotionBody.GetPresentationPose(player.transform, out var shown, out _);
        Rect body = FacilityAccess.BodyRect(player);
        return (Vector2)shown + body.center - (Vector2)player.transform.position;
    }

    // ── 직선·표적 괄호(5차 S6 · 6차 T4) ─────────────────────────────

    /// <summary>
    /// 6차 T4: 이 크레인 자신의 표시 기준점 G(월드 — 크레인의 잡는 부분). 판정·이동 목표와 무관하다(붙기 반경·점수·이동은 매달림 몸 중심).
    /// 기본 = 매달림 몸 중심(파생이 고리 중심·머리 중심으로 바꾼다).
    /// </summary>
    protected virtual Vector2 AimPoint => ZipFeet + Vector2.up * BodyHalfHeight;

    /// <summary>6차 T4: 표적 괄호 상자 반폭·반높이(생성 도구가 같은 값으로 괄호 Mesh를 굽는다).</summary>
    protected virtual Vector2 AimFrameHalf => new Vector2(.8f, .8f);

    /// <summary>6차 T4: 선택된 동안 표적 괄호가 도는지(직사각형 괄호는 돌리지 않는다 — 직선 끝이 상자 가장자리에 맞게).</summary>
    protected virtual bool BracketSpins => true;

    /// <summary>
    /// 이 크레인이 지금 그릴 선택 표시(center = 표시 기준점 G, half = 괄호 상자 반크기, 밝기). 선택된 크레인 자신은 0.9(확정 대기 1.6),
    /// 파생(경로 크레인)은 선택된 도크 호출 지점을 그린다. 없으면 false.
    /// </summary>
    protected virtual bool TryAimTarget(out Vector2 center, out Vector2 half, out float level)
    {
        center = AimPoint;
        half = AimFrameHalf;
        level = 0f;
        if (!focused || incoming != null || IsRiding) return false;
        var runner = FacilityZip.Runner;
        level = runner != null && ReferenceEquals(runner.Locked, this) ? lockedLevel : focusLevel;
        return true;
    }

    // 여백을 줄인 뒤에도 남길 직선 최소 길이(칸). 괄호 상자 밖 길이가 이보다 짧으면 표적 괄호만 보인다.
    private const float MinAimLength = .35f;

    /// <summary>
    /// 6차 T4: G에서 dir 반대쪽(몸 쪽)으로 가다 반크기 half 상자 가장자리를 만나는 거리 t = min(hx/|dx|, hy/|dy|). 몸 중심 → G 선의 끝을 G − dir × t에 둔다.
    /// </summary>
    public static float FrameEdge(Vector2 dir, Vector2 half)
    {
        float tx = Mathf.Abs(dir.x) > 1e-5f ? half.x / Mathf.Abs(dir.x) : float.MaxValue;
        float ty = Mathf.Abs(dir.y) > 1e-5f ? half.y / Mathf.Abs(dir.y) : float.MaxValue;
        float t = Mathf.Min(tx, ty);
        return t < float.MaxValue ? t : 0f;
    }

    private void DrawAim(float dt)
    {
        var runner = FacilityZip.Runner;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        bool shown = runner != null && runner.CandidatesShown && player != null;
        if (shown && TryAimTarget(out var center, out var half, out float level))
        {
            Vector2 body = ShownBodyCenter(player);
            Vector2 delta = center - body;
            float distance = delta.magnitude;
            Vector2 dir = distance > 1e-4f ? delta / distance : Vector2.right;
            // 6차 T4: 직선 끝 = 표적 괄호 상자 가장자리. 상자 밖 길이(reach)가 짧으면 시작 여백부터 줄여 최소 길이 MinAimLength를 남긴다.
            float edge = FrameEdge(dir, half);
            float reach = distance - edge;
            float startGap = Mathf.Clamp(reach - MinAimLength, 0f, aimStartGap);
            Vector2 from = body + dir * startGap, to = center - dir * edge;
            bool locked = level >= lockedLevel - 1e-3f;
            aimOffset += aimFlow * dt;
            if (aimLine != null && reach - startGap >= MinAimLength - 1e-4f)
            {
                var space = aimLine.transform;
                if (aimMeshName == null) aimMeshName = name + "_AimLine";
                // 확정 대기(누른 순간 고정) 동안은 실선 — 틈을 0으로.
                aimLine.sharedMesh = aimBuilt = FacilityMesh.DashedSegment(aimBuilt, space.InverseTransformPoint(from), space.InverseTransformPoint(to),
                    aimWidth, aimDash, locked ? 0f : aimGap, aimOffset, Color.white, aimMeshName);
                aimShown = true;
                AimLevel = level;
                AimStart = from;
                AimEnd = to;
                aimLamp?.Set(level);
            }
            else HideAimLine();
            PlaceBrackets(center, level, locked || !BracketSpins ? 0f : bracketSpin * dt);
            return;
        }
        HideAimLine();
        // 반경 안이지만 선택되지 않은 크레인: 흐린 표적 괄호만(회전 없음). 반경은 매달림 몸 중심 기준, 괄호 자리는 G(6차 T4).
        if (shown && !AnyFocus && ZipAvailable && incoming == null && !IsRiding)
        {
            Vector2 own = ZipFeet + Vector2.up * BodyHalfHeight;
            if (Vector2.Distance(FacilityAccess.BodyRect(player).center, own) <= activeRadius)
            {
                PlaceBrackets(AimPoint, candidateLevel, 0f);
                return;
            }
        }
        HideBrackets();
    }

    private void PlaceBrackets(Vector2 center, float level, float spin)
    {
        BracketLevel = level;
        bracketLamp?.Set(level);
        if (aimBrackets == null) return;
        aimBrackets.position = new Vector3(center.x, center.y, aimBrackets.position.z);
        if (spin != 0f) aimBrackets.localRotation *= Quaternion.Euler(0f, 0f, spin);
    }

    private void HideAimLine()
    {
        if (!aimShown) return;
        aimShown = false;
        AimLevel = 0f;
        aimLamp?.Set(0f);
        if (aimBuilt != null) aimBuilt.Clear();
    }

    private void HideBrackets()
    {
        if (BracketLevel <= 0f) return;
        BracketLevel = 0f;
        bracketLamp?.Set(0f);
    }

    private void HideAim()
    {
        HideAimLine();
        BracketLevel = .01f;
        HideBrackets();
    }

    // 키 안내를 크레인 쪽(파생이 정한 자리)으로 옮긴다(표시 전용 — 선택된 동안만. 선택이 풀려 사라지는 동안은 그 자리에 둔다).
    private void PlacePrompt()
    {
        if (zipPrompt == null || !AnyFocus) return;
        Vector2 at = PromptPoint;
        var t = zipPrompt.transform;
        t.position = new Vector3(at.x, at.y, t.position.z);
    }

    /// <summary>키 안내 자리(월드). 기본 = 매달린 발 + 0.1(6차: 경로 크레인은 머리 바로 아래·선택된 도크의 같은 자리, 고정 크레인은 고리 안).</summary>
    protected virtual Vector2 PromptPoint => ZipFeet + Vector2.up * .1f;

    // ── 와이어·견인 띠(5차 S5) ─────────────────────────────

    private void DrawWire()
    {
        if (zipWire == null) return;
        if (incoming == null) { HideWire(); return; }
        var space = zipWire.transform;
        Vector2 from = ZipHookPoint;
        Vector2 to = ShownBodyCenter(incoming);
        float extend = Mathf.Clamp01(incomingTime / wireExtendTime);
        Vector2 end = Vector2.Lerp(from, to, extend);
        // 장력: 다 뻗은 뒤 0.12초 동안 가닥이 모이며 밝아진다.
        float tension = Mathf.Clamp01((incomingTime - wireExtendTime) / .12f);
        WireSpacingNow = Mathf.Lerp(wireSpacing, wireTightSpacing, tension);
        WireLevel = Mathf.Lerp(1.2f, 2f, tension);
        if (wireMeshName == null) wireMeshName = name + "_ZipWire";
        zipWire.sharedMesh = wireBuilt = FacilityMesh.Strands(wireBuilt, space.InverseTransformPoint(from), space.InverseTransformPoint(end), wireWidth, WireSpacingNow, 2, Color.white, wireMeshName);
        wireShown = true;
        wireLamp?.Set(WireLevel);
        EmitStreaks(to, from);
    }

    private void HideWire()
    {
        if (!wireShown) return;
        wireShown = false;
        wireLamp?.Set(0f);
        if (wireBuilt != null) wireBuilt.Clear();
    }

    // 크레인 쪽으로 모이는 속도선: 몸 둘레·뒤쪽에서 크레인 방향 속도로 프레임마다 2개(월드 공간, Stretch 입자). 할당 없음(EmitParams는 구조체).
    private void EmitStreaks(Vector2 body, Vector2 crane)
    {
        if (zipStreaks == null) return;
        Vector2 delta = crane - body;
        float distance = delta.magnitude;
        if (distance < .3f) return;
        Vector2 dir = delta / distance, side = new Vector2(-dir.y, dir.x);
        var emit = new ParticleSystem.EmitParams();
        for (int i = 0; i < 2; i++)
        {
            Vector2 at = body + side * UnityEngine.Random.Range(-.75f, .75f) - dir * UnityEngine.Random.Range(.2f, .9f);
            emit.position = new Vector3(at.x, at.y, zipStreaks.transform.position.z);
            emit.velocity = (Vector3)(dir * UnityEngine.Random.Range(16f, 24f));
            emit.applyShapeToPosition = false;
            zipStreaks.Emit(emit, 1);
            StreakEmits++;
        }
    }

    // 견인 띠: 출발점(꼬리) → 지금 몸 중심(머리). 파랑 넓은 꼬리 → 청록 가운데 → 흰 머리. 도착 뒤에는 꼬리가 0.12초에 머리를 따라잡는다.
    private void DrawTow(float dt)
    {
        if (towBand == null && towCore == null && towHead == null) return;
        Vector2 headAt, tailAt;
        if (incoming != null)
        {
            headAt = lastIncomingBody = ShownBodyCenter(incoming);
            tailAt = zipStartBody;
        }
        else if (towFade >= 0f)
        {
            towFade += dt;
            float t = Mathf.Clamp01(towFade / .12f);
            headAt = lastIncomingBody;
            tailAt = Vector2.Lerp(zipStartBody, headAt, t * t * (3f - 2f * t));
            if (t >= 1f) { towFade = -1f; HideTow(); return; }
        }
        else { HideTow(); return; }
        Vector2 along = headAt - tailAt;
        float length = along.magnitude;
        if (length < .05f) { HideTow(); return; }
        Vector2 dir = along / length;
        Vector2 coreFrom = Vector2.Lerp(tailAt, headAt, .35f);
        if (towBandName == null) { towBandName = name + "_TowBand"; towCoreName = name + "_TowCore"; towHeadName = name + "_TowHead"; }
        if (towBand != null)
        {
            var s = towBand.transform;
            towBand.sharedMesh = towBandBuilt = FacilityMesh.Taper(towBandBuilt, s.InverseTransformPoint(tailAt), s.InverseTransformPoint(headAt), .04f, .3f, Color.white, towBandName);
        }
        if (towCore != null)
        {
            var s = towCore.transform;
            towCore.sharedMesh = towCoreBuilt = FacilityMesh.Taper(towCoreBuilt, s.InverseTransformPoint(coreFrom), s.InverseTransformPoint(headAt), .02f, .14f, Color.white, towCoreName);
        }
        if (towHead != null)
        {
            var s = towHead.transform;
            towHead.sharedMesh = towHeadBuilt = FacilityMesh.Taper(towHeadBuilt, s.InverseTransformPoint(headAt - dir * Mathf.Min(.45f, length)), s.InverseTransformPoint(headAt + dir * .06f), .02f, .18f, Color.white, towHeadName);
        }
        towShown = true;
        float fade = towFade >= 0f ? 1f - Mathf.Clamp01(towFade / .12f) : 1f;
        towBandLamp?.Set(.9f * fade);
        towCoreLamp?.Set(1.4f * fade);
        towHeadLamp?.Set(2f * fade);
    }

    private void HideTow()
    {
        if (!towShown) return;
        towShown = false;
        towBandLamp?.Set(0f);
        towCoreLamp?.Set(0f);
        towHeadLamp?.Set(0f);
        if (towBandBuilt != null) towBandBuilt.Clear();
        if (towCoreBuilt != null) towCoreBuilt.Clear();
        if (towHeadBuilt != null) towHeadBuilt.Clear();
    }

    // ── 배치별 설정 ─────────────────────────────

    protected WaveDeviceParameter RadiusParameter => new WaveDeviceParameter(ZipRadiusKey, "붙기 반경", "상호작용 키(↑)로 이 크레인에 붙을 수 있는 거리입니다(Player 몸 중심 ~ 매달림 몸 중심).", "칸", 4f, 14f, .5f, zipRadius);

    public abstract WaveDeviceParameter[] PropParameters { get; }

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        ReadSettings(overrides);
        if (!isLive) return;
        OnSettingsApplied();
    }

    /// <summary>실행 중 배치 설정이 바뀐 뒤(파생: 빛 통로·기둥 Mesh를 다시 그린다).</summary>
    protected virtual void OnSettingsApplied() { }

    protected virtual void ReadSettings(WaveDeviceSetting[] overrides)
    {
        activeRadius = WaveDeviceSettings.Value(overrides, RadiusParameter);
    }

    /// <summary>격자 Tool 범위 미리보기: 붙기 반경 원(중심 = 집 발의 매달림 몸 중심, 청록 — 편집기용이라 5차에도 유지. 7차 V11: 고정 크레인은 재정의로 고리 중심).</summary>
    public virtual bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        float radius = WaveDeviceSettings.Value(overrides, RadiusParameter);
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Circle, HangFeetLocal + Vector2.up * BodyHalfHeight, new Vector2(radius, radius), new Color(.25f, .95f, 1f, .9f));
        return true;
    }
}
