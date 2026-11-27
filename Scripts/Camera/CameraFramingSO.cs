using UnityEngine;

// Player를 따라가는 가상 카메라 공통 프레이밍(PlayerCameraFraming) 수치. 실행 중에는 Resources/Camera/CameraFraming을 읽는다.
// 거리는 모두 기준 렌즈(referenceOrthographicSize = 세로 반시야 7칸)에서의 칸 수이고, 보스 줌처럼 렌즈가 바뀌면 같은 비율로 줄이거나 늘린다.
// 기본값 근거(2026-11-27 조사): Dead Cells 개발자 공개 엔진(gameBase)의 데드존(화면 폭 4%·높이 10%)·관성 추적(속도 지연 약 0.2초),
// Hollow Knight 재현 자료의 바라보는 방향 선행·낙하 따라잡기, Super Mario World·Rayman의 점프 높이 창. 수치는 이 게임 화면(세로 14칸)에 맞춘 판단값이다.
[CreateAssetMenu(fileName = "CameraFraming", menuName = "Camera/Camera Framing")]
public sealed class CameraFramingSO : ScriptableObject
{
    private static CameraFramingSO cached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => cached = null;

    public static CameraFramingSO Default => cached != null ? cached : cached = Resources.Load<CameraFramingSO>("Camera/CameraFraming");

    [Tooltip("끄면 예전처럼 Cinemachine 출력(Player = 화면 중앙)을 그대로 쓴다. Play 중에 켜고 끄며 비교할 수 있다.")]
    public bool framingEnabled = true;
    [Tooltip("아래 칸 수를 그대로 쓰는 렌즈 크기(직교 반높이).")]
    public float referenceOrthographicSize = 7f;

    [Header("세로 — 서 있을 때")]
    [Tooltip("땅에 서 있을 때 카메라 중심을 몸 중심보다 이만큼(칸) 위에 둔다. 0.9면 Player가 화면 아래에서 약 44% 높이에 선다.")]
    public float groundLift = .9f;
    [Tooltip("서 있을 때(크레인 매달림 포함) 발밑으로 최소 이 칸 수는 늘 화면에 보이게 한다. 위 보기·마우스 조준 선행도 이 아래로는 Player를 내리지 않는다.")]
    public float minRowsBelowFeet = 3f;
    [Tooltip("점프로 이 높이(칸)까지 오르는 동안은 카메라 높이를 그대로 두고 착지할 때 새 높이로 맞춘다. 기본값은 누르고 있는 1단 점프 높이(약 3.3칸).")]
    public float jumpWindow = 3.4f;
    [Tooltip("공중에서 세로 속도가 거의 0인 채 이 시간(초)이 지나면(크레인 매달림 등) 서 있는 것처럼 그 높이에 맞춘다.")]
    public float hangSettleTime = .15f;

    [Header("세로 — 아래로 떨어질 때·빠르게 오를 때")]
    [Tooltip("서 있던 높이보다 이만큼(칸) 넘게 떨어진 뒤에만 아래 보기를 시작한다(층 사이 짧은 낙하·계단은 화면이 출렁이지 않게).")]
    public float fallLookDepth = 2.5f;
    [Tooltip("긴 낙하에서 카메라를 몸보다 이만큼(칸) 아래로 내려 착지할 곳을 보여 준다(낙하 속도에 따라 섞음).")]
    public float fallLookDown = 1.5f;
    [Tooltip("아래 보기가 시작되는 낙하 속도와 다 내려가는 낙하 속도(칸/초).")]
    public Vector2 fallSpeedRange = new(8f, 20f);
    [Tooltip("이보다 빠르게(칸/초) 오르면(크레인·점프대) 점프 창 없이 바로 따라간다.")]
    public float fastRiseSpeed = 11f;
    [Tooltip("착지 뒤 서 있을 때 높이로 돌아가는 시간(초). 길수록 화면이 덜 출렁인다.")]
    public float landingSettleTime = .45f;

    [Header("가로 — 데드존·진행 방향 선행")]
    [Tooltip("몸이 카메라 기준점에서 이 거리(칸) 안에서 움직이면 카메라가 따라가지 않는다(공격 전진·넉백·짧은 좌우 오가기에 화면이 흔들리지 않게). Dead Cells 개발자 엔진의 화면 폭 4%.")]
    public float deadZoneX = 1f;
    [Tooltip("멈춰 있을 때 데드존 기준점이 몸 쪽으로 돌아오는 속도(칸/초). 천천히 가운데로 돌아와 제자리 방향 전환에는 화면이 움직이지 않는다.")]
    public float recenterSpeed = .8f;
    [Tooltip("이 시간(초) 넘게 한 방향으로 계속 움직여야 진행 방향 선행과 속도 앞세우기를 켠다(전투 중 짧게 오가는 움직임에는 켜지 않는다).")]
    public float sustainTime = .3f;
    [Tooltip("계속 움직이는 동안 그 방향으로 카메라를 이만큼(칸) 앞세운다. 데드존만큼은 몸 쪽으로 남으므로 실제 앞섬은 이 값 − 데드존 + 속도 앞세우기다.")]
    public float moveLead = 1.5f;
    [Tooltip("멈춰 있을 때 바라보는 방향으로 남기는 선행(칸). 0이면 멈추면 가운데로 돌아온다(제자리 방향 전환에 화면이 움직이지 않는다).")]
    public float idleLead;
    [Tooltip("반대 방향으로 이 시간(초) 이상 이어서 움직여야 선행 방향을 바꾼다.")]
    public float turnDelay = .35f;
    [Tooltip("선행 거리가 바뀌는 시간(초). 길수록 방향을 바꿀 때 화면이 천천히 넘어간다.")]
    public float leadSmoothTime = .8f;

    [Header("둘러보기·마우스 조준")]
    [Tooltip("멈춰 서서 ↑를 누르고 있으면 위로 더 보는 거리(칸, 발밑 최소 칸 수 안에서). 0이면 끔.")]
    public float lookUp = 1f;
    [Tooltip("멈춰 서서 ↓를 누르고 있으면 아래로 더 보는 거리(칸). 0이면 끔.")]
    public float lookDown = 2.5f;
    [Tooltip("둘러보기가 시작되기까지 누르고 있어야 하는 시간(초). 위 조준·상호작용처럼 짧게 누르는 입력에는 움직이지 않는다.")]
    public float lookDelay = .6f;
    [Tooltip("둘러보기가 옮겨 가는 시간(초).")]
    public float lookSmoothTime = .45f;
    [Tooltip("마우스 조준 모드에서 커서가 화면 끝에 있을 때 그쪽으로 당기는 최대 거리(칸, 가로·세로).")]
    public Vector2 mouseAimLead = new(2.5f, 1f);

    [Header("따라가기")]
    [Tooltip("가로 추적 시간(초). 예전 Cinemachine 감쇠 1(시간 상수 약 0.22초)과 비슷한 부드러움이다. 걷는 속도만큼은 미리 앞세워 지연을 없앤다.")]
    public float smoothTimeX = .22f;
    [Tooltip("세로 추적 시간(초) — 착지 뒤 새 높이로 옮길 때.")]
    public float smoothTimeY = .3f;
    [Tooltip("떨어지거나 빠르게 오를 때의 세로 추적 시간(초). 이때는 이동 속도만큼 미리 앞세워 Player가 화면 끝으로 밀리지 않게 한다.")]
    public float fastSmoothTimeY = .1f;
    [Tooltip("미리 앞세우는 속도를 거르는 시간(초). 대시처럼 짧게 튀는 속도에 화면이 앞으로 쏠렸다 돌아오지 않게 한다.")]
    public float velocityFilterTime = .2f;
    [Tooltip("목표 위치보다 이 거리(칸) 넘게 뒤처지지 않는다(대시·크레인처럼 빠른 이동에서 Player가 화면 끝으로 밀리지 않게).")]
    public Vector2 maxLag = new(3f, 2.5f);
    [Tooltip("한 프레임에 이보다 멀리(칸) 옮겨지면 순간이동으로 보고 바로 맞춘다.")]
    public float snapDistance = 5f;
    [Tooltip("보스 소개·컷씬처럼 Player가 아닌 대상을 비추다 돌아올 때(또는 그 반대) 섞는 시간(초).")]
    public float blendTime = .3f;
}
