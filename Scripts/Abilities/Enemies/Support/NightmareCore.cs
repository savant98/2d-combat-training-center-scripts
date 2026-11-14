using UnityEngine;

/// <summary>
/// Nightmare의 코어를 움직인다. 표시 전용이며 판정·이동과는 무관하다.
/// - 코어는 플레이어의 몸(톱니 고리 + 바이저가 달린 원판)을 붉은·주황색으로 물들인 모습이고, 그 뒤에 Admin의 블랙홀에서 가져온
///   사건의 지평선(검은 원판)과 그 가장자리의 빛 고리가 놓인다. 검은 원판은 코어 뒤의 배경을 가려, 가산 발광인 톱니·원판·가시가 밝은 배경에 묻히지 않게 한다.
///   이 컴포넌트는 원판(Hub)에 붙어 원판의 자세와 빛, 톱니 고리의 회전, 지평선의 크기와 빛 고리의 밝기를 한곳에서 정한다.
/// - Animation Clip은 이 컴포넌트의 값만 움직인다(가동·지평선·열기·섬광·조준·밀림·맥동). Transform과 Material 값은 여기서 계산해 넣는다.
///   Model 루트가 패턴에 따라 90도씩 돌고 좌우로 뒤집히므로, 조준·밀림은 화면 기준으로 계산한다.
/// - 평소에는 바이저가 Player를 겨눈다. Clip이 track을 낮추면 Clip이 정한 방향(검의 끝, 벨 자리)을 본다.
/// - 가시 묶음이 돌면(검을 휘두를 때) 톱니 고리가 맞물린 것처럼 반대로 돈다. 가시의 움직임 자체는 읽기만 한다.
/// - 지평선은 힘을 모으면 커지고(horizon) 가동이 꺼지면 사라진다. 톱니가 가로로 펼쳐지는 배율(등장)을 그대로 따라간다.
///   빛 고리는 열기와 섬광에 따라 밝아지고 Admin의 블랙홀처럼 미세하게 깜빡인다.
/// - 지평선 둘레에는 Admin의 블랙홀처럼 나선 무늬의 강착원반 두 장(안쪽은 빠르게, 바깥쪽은 느리게)이 늘 돈다.
///   화면 기준 각도로 돌려 Model 루트의 90도 전환·좌우 반전에 끊기지 않고, 열기가 오르면 빨라지고 밝아진다. 크기·드러남은 지평선을 따른다.
/// - 코어 둘레는 Admin의 블랙홀과 같은 노말맵 굴절로 일렁인다. Nightmare의 발광 파츠만 따로 그려 굴절시키므로(SelfDistortionSource)
///   Player·배경·다른 개체는 일그러지지 않는다. 세기는 가동과 열기를 따라간다(평소에는 약하게, 힘을 모으면 강하게).
///   빛 고리는 가산 발광이라 함께 일렁이고, 검은 원판은 뒤를 가리는 그림이라 대상에 넣지 않는다.
/// - 패턴이 탄·레이저를 쏘는 순간에는 Fire를 부른다. 바이저가 쏘는 방향으로 돌며 번쩍이고 원판이 반동으로 밀리며,
///   지평선과 빛 고리도 함께 번쩍이며 살짝 부푼다(발사 박자는 Clip이 아니라 실제 발사에 맞춘다).
/// - 새로 추가한 값이므로 Nightmare의 모든 Clip에 곡선을 기록한다(패턴 시작 때의 재바인딩으로 직전 값이 기본값이 되지 않게).
/// </summary>
[DisallowMultipleComponent]
public sealed class NightmareCore : MonoBehaviour
{
    [Header("Clip이 움직이는 값")]
    [Range(0f, 1f), Tooltip("가동 정도. 0 = 꺼짐(어둡고 톱니가 멈추며 지평선이 사라진다), 1 = 켜짐")] public float power = 1f;
    [Range(0f, 2f), Tooltip("사건의 지평선의 크기·세기. 1이 평소, 힘을 모으면 커지고 0이면 사라진다")] public float horizon = 1f;
    [Range(0f, 1.5f), Tooltip("열기. 빛의 세기, 톱니가 도는 속도, 빛 고리의 밝기")] public float heat = .5f;
    [Range(0f, 1f), Tooltip("섬광")] public float flash;
    [Range(-1f, 1f), Tooltip("Clip이 정한 조준 방향. 보스가 보는 쪽이 +x")] public float aimX;
    [Range(-1f, 1f)] public float aimY;
    [Range(0f, 1f), Tooltip("1이면 Player를 겨누고, 0이면 Clip이 정한 방향만 본다")] public float track = 1f;
    [Tooltip("원판이 밀린 거리(칸). 보스가 보는 쪽이 +x. 휘두르거나 쏠 때의 무게")] public float shiftX;
    public float shiftY;
    [Tooltip("맥동. 원판의 배율에 더하는 값(0이면 그대로). 지평선도 같은 비율로 부푼다")] public float pulse;

    [Header("연결")]
    [SerializeField, Tooltip("원판(이 오브젝트)")] private SpriteRenderer hubRenderer;
    [SerializeField, Tooltip("톱니 고리(Core). 원판의 자리·배율의 기준이며 이 Transform을 돌린다")] private Transform gear;
    [SerializeField] private SpriteRenderer gearRenderer;
    [SerializeField, Tooltip("사건의 지평선(검은 원판). 톱니·가시보다 뒤에서 배경을 가린다")] private SpriteRenderer horizonRenderer;
    [SerializeField, Tooltip("지평선 가장자리의 빛 고리(가산 발광)")] private SpriteRenderer ringRenderer;
    [SerializeField, Tooltip("지평선 둘레를 빠르게 도는 안쪽 강착원반(가산 발광, Admin 블랙홀의 AccretionInner와 같은 나선)")] private SpriteRenderer diskInnerRenderer;
    [SerializeField, Tooltip("안쪽 원반 바깥을 느리게 도는 바깥쪽 강착원반(가산 발광, Admin 블랙홀의 AccretionOuter와 같은 나선)")] private SpriteRenderer diskOuterRenderer;
    [SerializeField, Tooltip("좌우 반전의 기준(FacingObject). 비워 두면 뒤집히지 않은 것으로 본다")] private Transform facing;
    [SerializeField, Tooltip("가시 묶음의 회전축(OuterCircle). 읽기만 한다")] private Transform swing;
    [SerializeField, Tooltip("코어 둘레의 자기 왜곡(Nightmare의 발광 파츠만 굴절시킨다)")] private SelfDistortionSource distortion;

    [Header("조준")]
    [SerializeField, Min(.1f), Tooltip("Player가 이 거리(칸)보다 멀면 끝까지 쏠린다")] private float fullAimDistance = 7f;
    [SerializeField, Min(.01f), Tooltip("Player를 따라 도는 데 걸리는 시간")] private float followTime = .12f;
    [SerializeField, Min(0f), Tooltip("겨눈 쪽으로 원판이 쏠리는 거리(칸)")] private float leanDistance = .03f;
    [SerializeField, Range(0f, 10f), Tooltip("바이저의 미세한 떨림(도)")] private float tremble = 1.5f;

    [Header("톱니 고리")]
    [SerializeField, Tooltip("평소 도는 속도(도/초). 열기가 오르면 빨라진다")] private float idleSpin = 28f;
    [SerializeField, Tooltip("가시 묶음이 돌 때 맞물려 도는 비율(음수 = 반대 방향)")] private float gearRatio = -1.5f;
    [SerializeField, Min(0f), Tooltip("가시 묶음의 회전으로 보지 않는 한 프레임의 각도(패턴이 바뀔 때의 자세 전환)")] private float swingJumpLimit = 50f;
    [SerializeField, Min(0f), Tooltip("톱니가 도는 속도의 한도(도/초). 너무 빠르면 톱니가 멈추거나 거꾸로 도는 것처럼 보인다")] private float maxSpin = 720f;

    [Header("사건의 지평선")]
    [SerializeField, Min(.01f), Tooltip("평소(지평선 값 1) 지평선의 반지름(칸). 톱니 고리(약 1.04칸)보다 조금 크게 둔다")] private float horizonRadius = 1.4f;
    [SerializeField, Min(.01f), Tooltip("지평선·빛 고리 그림이 배율 1일 때 그리는 반지름(칸). Admin 블랙홀의 그림은 1.62칸이다")] private float horizonSpriteRadius = 1.62f;
    [SerializeField, Min(.01f), Tooltip("톱니 고리의 평소 배율. 톱니가 가로로 펼쳐지는 비율(지금 배율 ÷ 이 값)을 지평선이 따라간다")] private float gearRestScale = 2f;
    [SerializeField, Range(0f, 1f), Tooltip("지평선 값이 0에 가까울 때의 크기(평소 대비 비율). 값이 1까지 오르며 평소 크기가 된다")] private float horizonMinScale = .55f;
    [SerializeField, Min(0f), Tooltip("지평선 값이 1을 넘을 때 값 1당 커지는 비율")] private float horizonGrowth = .3f;
    [SerializeField, Range(.01f, 1f), Tooltip("지평선 값이 0에서 이 값까지 오르는 동안 나타난다(이 값 이상이면 다 보인다)")] private float horizonFadeIn = .4f;
    [SerializeField, Range(0f, 1f), Tooltip("검은 원판의 불투명도")] private float horizonOpacity = 1f;
    [SerializeField, Min(0f), Tooltip("지평선의 크기가 새 값으로 따라가는 시간(초). Clip이 값을 끊는 곳(쏘는 순간)에서 한 프레임에 튀지 않고 빠르게 열린다. 0이면 바로 바뀐다")] private float horizonSmoothTime = .045f;
    [SerializeField, Min(0f), Tooltip("맥동·발사 때 지평선이 원판을 따라 부푸는 비율(1이면 원판과 같은 비율)")] private float horizonPulse = 1f;
    [SerializeField, Min(0f), Tooltip("열기가 가장 낮을 때 빛 고리의 밝기 비율")] private float ringCoolGlow = .7f;
    [SerializeField, Min(0f), Tooltip("열기가 가장 높을 때 빛 고리의 밝기 비율")] private float ringHotGlow = 1.45f;
    [SerializeField, Range(0f, .6f), Tooltip("빛 고리의 미세한 깜빡임(Admin 블랙홀과 같은 방식)")] private float ringFlicker = .18f;
    [SerializeField, Min(0f), Tooltip("섬광 때 빛 고리의 세기에 더하는 비율")] private float ringFlashBoost = .6f;

    [Header("강착원반(회전)")]
    [SerializeField, Tooltip("안쪽 원반이 도는 속도(도/초, +가 반시계). Admin 블랙홀과 같은 210")] private float diskInnerSpin = 210f;
    [SerializeField, Tooltip("바깥쪽 원반이 도는 속도(도/초). Admin 블랙홀과 같은 95. 두 원반이 다른 속도로 돌아 소용돌이가 겹쳐 흐른다")] private float diskOuterSpin = 95f;
    [SerializeField, Min(0f), Tooltip("열기가 가장 낮을 때 도는 속도의 배율(대기 열기 0.5에서 약 1배)")] private float diskCoolSpin = .75f;
    [SerializeField, Min(0f), Tooltip("열기가 가장 높을 때(힘을 모을 때) 도는 속도의 배율")] private float diskHotSpin = 1.6f;
    [SerializeField, Min(0f), Tooltip("열기가 가장 낮을 때 원반의 밝기 비율")] private float diskCoolGlow = .7f;
    [SerializeField, Min(0f), Tooltip("열기가 가장 높을 때 원반의 밝기 비율")] private float diskHotGlow = 1.35f;
    [SerializeField, Min(0f), Tooltip("섬광 때 원반의 세기에 더하는 비율")] private float diskFlashBoost = .4f;

    [Header("빛")]
    [SerializeField, Range(0f, 1f), Tooltip("꺼졌을 때의 밝기 비율")] private float offBrightness = .12f;
    [SerializeField, ColorUsage(false, true), Tooltip("섬광의 색")] private Color flashColor = new Color(4.5f, 3.4f, 1.7f, 1f);
    [SerializeField, Min(0f), Tooltip("섬광 때 세기에 더하는 비율")] private float flashBoost = .6f;

    [Header("코어 둘레의 왜곡")]
    [SerializeField, Range(0f, 1f), Tooltip("평소(대기)의 왜곡 세기. 열기가 오르면 1까지 강해진다")] private float idleDistortion = .5f;

    [Header("쏘는 순간(Fire)")]
    [SerializeField, Min(0f), Tooltip("쏜 방향을 겨누는 시간")] private float fireAimTime = .14f;
    [SerializeField, Min(0f), Tooltip("반동으로 원판이 밀리는 거리(칸)")] private float fireKick = .05f;
    [SerializeField, Range(0f, 1f)] private float fireFlash = .55f;
    [SerializeField, Range(0f, .3f), Tooltip("쏘는 순간 원판과 지평선이 부푸는 정도")] private float firePulse = .07f;
    [SerializeField, Min(.01f), Tooltip("반동·섬광이 가라앉는 시간")] private float fireFade = .07f;
    [SerializeField, Tooltip("쏠 때 톱니가 튕기듯 도는 속도(도/초)")] private float gearJolt = -520f;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    // 빛 고리가 깜빡이는 무늬의 자리(표시용. 게임 로직의 난수 순서를 건드리지 않게 시간의 함수로 만든다)
    private const float FlickerSeed = 41.7f;

    private MaterialPropertyBlock block;
    private float aimAngle, aimVelocity, aimStrength, gearAngle, gearJoltRate, clock;
    private float lastSwingLocal, horizonSize;
    // 강착원반의 화면 기준 각도(도). 시작 각도는 두 원반이 겹쳐 보이지 않게 어긋나게 둔다.
    private float diskInnerAngle, diskOuterAngle = 137f;
    private bool hasSwing, hasHorizonSize;
    // 쏘는 순간의 반응(화면 기준)
    private Vector2 firedDirection, kick;
    private float fireAimLeft, firedFlash, firedPulse;

    /// <summary>바이저가 향하는 방향(화면 기준, 도, -180 ~ 180). 오른쪽이 0, 반시계가 +.</summary>
    public float AimAngle => aimAngle;
    public Vector2 AimDirection => new Vector2(Mathf.Cos(aimAngle * Mathf.Deg2Rad), Mathf.Sin(aimAngle * Mathf.Deg2Rad));
    /// <summary>쏜 직후의 반응이 남은 정도(0 ~ 1). 검증용.</summary>
    public float FireResponse => Mathf.Max(firedFlash / Mathf.Max(fireFlash, 1e-4f), kick.magnitude / Mathf.Max(fireKick, 1e-4f));
    /// <summary>톱니 고리의 각도(도, 0 ~ 360).</summary>
    public float GearAngle => gearAngle;
    /// <summary>코어의 중심(월드). 코어에서 무언가를 쏠 때의 출발점.</summary>
    public Vector2 Center => Origin;
    public SpriteRenderer Renderer => hubRenderer;
    public SpriteRenderer Horizon => horizonRenderer;
    public SpriteRenderer Ring => ringRenderer;
    public SelfDistortionSource Distortion => distortion;
    /// <summary>지금 그려지는 지평선의 반지름(칸, 톱니가 펼쳐진 비율은 빼고 잰 값). 검증용.</summary>
    public float HorizonRadius { get; private set; }
    /// <summary>지평선이 드러난 정도(0 ~ 1). 가동이 꺼지거나 지평선 값이 0이면 0이다. 검증용.</summary>
    public float HorizonShown { get; private set; }
    /// <summary>빛 고리에 넣은 밝기 비율(Material의 세기에 곱한 값, 섬광 제외). 검증용.</summary>
    public float RingGlow { get; private set; }
    public SpriteRenderer DiskInner => diskInnerRenderer;
    public SpriteRenderer DiskOuter => diskOuterRenderer;
    /// <summary>강착원반의 화면 기준 각도(도, 0 ~ 360, 반시계가 +). 검증용.</summary>
    public float DiskInnerAngle => diskInnerAngle;
    public float DiskOuterAngle => diskOuterAngle;
    /// <summary>강착원반에 넣은 밝기 비율(Material의 세기에 곱한 값, 섬광 제외). 검증용.</summary>
    public float DiskGlow { get; private set; }

    private void OnEnable()
    {
        aimAngle = aimVelocity = aimStrength = 0f;
        gearAngle = gearJoltRate = 0f;
        hasSwing = hasHorizonSize = false;
        kick = Vector2.zero;
        fireAimLeft = firedFlash = firedPulse = 0f;
    }

    /// <summary>
    /// 코어가 무언가를 쏘는 순간에 패턴이 부른다. 바이저가 그 방향으로 돌며 번쩍이고, 원판이 반동으로 반대쪽으로 밀리며 톱니가 튕긴다.
    /// 지평선과 빛 고리도 함께 번쩍이며 살짝 부푼다. 방향이 없으면(0) 번쩍이기만 한다. strength는 0 ~ 1(연사는 약하게).
    /// </summary>
    public void Fire(Vector2 worldDirection, float strength = 1f)
    {
        strength = Mathf.Clamp01(strength);
        if (worldDirection.sqrMagnitude > 1e-6f)
        {
            firedDirection = worldDirection.normalized;
            fireAimLeft = fireAimTime;
            kick = -firedDirection * (fireKick * strength);
        }
        firedFlash = Mathf.Max(firedFlash, fireFlash * strength);
        firedPulse = Mathf.Max(firedPulse, firePulse * strength);
        gearJoltRate += gearJolt * strength;
    }

    private void LateUpdate()
    {
        float delta = Time.deltaTime;
        clock += delta;
        float sign = FacingSign;

        // 쏜 직후의 반응이 가라앉는다
        float fade = Mathf.Exp(-delta / fireFade);
        kick *= fade;
        firedFlash *= fade;
        firedPulse *= fade;
        gearJoltRate *= Mathf.Exp(-delta / (fireFade * 2f));
        fireAimLeft = Mathf.Max(0f, fireAimLeft - delta);
        float fireWeight = fireAimTime > 0f ? Mathf.Clamp01(fireAimLeft / (fireAimTime * .4f)) : 0f;

        // ---- 조준: Clip이 정한 방향과 Player 방향을 섞고, 쏜 직후에는 쏜 방향이 먼저다 ----
        Vector2 origin = Origin;
        Transform player = GameManager.Instance != null && GameManager.Instance.Player != null ? GameManager.Instance.Player.transform : null;
        Vector2 toPlayer = player != null ? (Vector2)player.position - origin : Vector2.zero;
        Vector2 target = Vector2.Lerp(new Vector2(aimX * sign, aimY), PlayerAim(toPlayer), Mathf.Clamp01(track));
        target = Vector2.Lerp(target, firedDirection, fireWeight);
        // Clip이 정한 방향과 쏘는 방향은 박자가 어긋나지 않게 거의 바로 돌고, Player는 조금 늦게 따라 돈다.
        float smooth = Mathf.Lerp(.02f, followTime, Mathf.Clamp01(track) * (1f - fireWeight));
        float strength = Mathf.Clamp01(target.magnitude);
        if (strength > .05f)
            aimAngle = Mathf.DeltaAngle(0f, Mathf.SmoothDampAngle(aimAngle, Mathf.Atan2(target.y, target.x) * Mathf.Rad2Deg, ref aimVelocity, smooth, Mathf.Infinity, Mathf.Max(delta, 1e-5f)));
        else
            aimVelocity = 0f;                                   // 겨눌 곳이 없으면 보던 쪽을 그대로 본다
        aimStrength = Mathf.MoveTowards(aimStrength, strength, delta / .08f);

        // ---- 가시 묶음의 회전(읽기만): 톱니는 Model 안에서의 회전에 맞물린다 ----
        float swingLocalRate = 0f;
        if (swing != null)
        {
            float local = swing.localEulerAngles.z;
            if (hasSwing && delta > 1e-5f)
            {
                float localStep = Mathf.DeltaAngle(lastSwingLocal, local);
                if (Mathf.Abs(localStep) < swingJumpLimit) swingLocalRate = localStep / delta;
            }
            lastSwingLocal = local;
            hasSwing = true;
        }
        float lit = Mathf.Clamp01(power);
        float spin = Mathf.Clamp((IdleSpinRate + gearJoltRate) * lit + swingLocalRate * gearRatio, -maxSpin, maxSpin);
        gearAngle = Mathf.Repeat(gearAngle + spin * delta, 360f);

        // 강착원반: Admin 블랙홀처럼 두 원반이 서로 다른 속도로 늘 돈다. 화면 기준 각도라 Model 루트의 90도 전환·좌우 반전과 무관하다.
        float diskRate = DiskSpinRate(lit);
        diskInnerAngle = Mathf.Repeat(diskInnerAngle + diskInnerSpin * diskRate * delta, 360f);
        diskOuterAngle = Mathf.Repeat(diskOuterAngle + diskOuterSpin * diskRate * delta, 360f);

        Apply(aimAngle + Tremble(clock), sign, clock, delta);
        Warp(lit);
    }

    // 코어 둘레의 왜곡: 가동과 열기를 따라간다.
    private void Warp(float lit)
    {
        if (distortion == null) return;
        distortion.strength = lit * Mathf.Lerp(idleDistortion, 1f, Mathf.InverseLerp(.5f, 1.25f, heat));
    }

    /// <summary>미리보기·검증용: Player 없이 Clip의 값만으로 지정한 시각의 모습을 넣는다.</summary>
    public void ApplyForPreview(float time)
    {
        var clip = new Vector2(aimX * FacingSign, aimY);
        Vector2 target = Vector2.Lerp(clip, new Vector2(.92f, -.38f), Mathf.Clamp01(track));   // Player가 앞쪽 아래에 있다고 본다
        aimStrength = Mathf.Clamp01(target.magnitude);
        if (aimStrength > .05f) aimAngle = Mathf.Atan2(target.y, target.x) * Mathf.Rad2Deg;
        gearAngle = Mathf.Repeat(time * IdleSpinRate * Mathf.Clamp01(power), 360f);
        float diskRate = DiskSpinRate(Mathf.Clamp01(power));
        diskInnerAngle = Mathf.Repeat(time * diskInnerSpin * diskRate, 360f);
        diskOuterAngle = Mathf.Repeat(137f + time * diskOuterSpin * diskRate, 360f);
        Apply(aimAngle, FacingSign, time, -1f);
        Warp(Mathf.Clamp01(power));
    }

    private float FacingSign => facing != null && facing.localScale.x < 0f ? -1f : 1f;

    // 열기가 오르면 톱니가 눈에 띄게 빨라진다(대기 0.5 → 0.7배, 힘을 모을 때 1.25 → 2.8배).
    private float IdleSpinRate => idleSpin * (.3f + 1.6f * heat * heat);

    // 강착원반이 도는 속도의 배율: 열기를 따라 빨라지고(힘을 모을 때 소용돌이가 거세진다), 가동이 꺼지면 멈춘다.
    private float DiskSpinRate(float lit) => Mathf.Lerp(diskCoolSpin, diskHotSpin, Mathf.Clamp01(heat / 1.3f)) * lit;

    private Vector2 Origin => gear != null ? (Vector2)gear.position : (Vector2)transform.parent.position;

    private Vector2 PlayerAim(Vector2 toPlayer)
    {
        float distance = toPlayer.magnitude;
        if (distance < 1e-3f) return Vector2.zero;
        // 가까이 있으면 덜 쏠리고, 멀수록 끝까지 쏠린다
        return toPlayer / distance * Mathf.Pow(Mathf.Clamp01(distance / fullAimDistance), .6f);
    }

    // 표시용 떨림(게임 로직의 난수 순서를 건드리지 않게 시간의 함수로 만든다). 뜨거울수록 크게 떤다.
    private float Tremble(float time) => (Mathf.PerlinNoise(time * 5.7f, 3.7f) - .5f) * 2f * tremble * (.4f + Mathf.Clamp01(heat)) * Mathf.Clamp01(power);

    // delta가 음수이면 이어 붙이는 값(지평선의 크기)을 지금 값으로 바로 맞춘다(미리보기).
    private void Apply(float shownAngle, float sign, float time, float delta)
    {
        // 톱니 고리: Model 안에서 돈다(12톱니·네 구멍이 90도 대칭이라 Model 루트의 90도 전환이나 좌우 반전이 보이지 않는다).
        Vector3 gearScale = new Vector3(gearRestScale, gearRestScale, 1f);
        if (gear != null)
        {
            gear.localRotation = Quaternion.Euler(0f, 0f, gearAngle);
            gearScale = gear.localScale;                        // 등장 때 가로로 펼쳐지는 배율(Clip)
        }

        // 원판: 톱니와 같은 배율로, 화면 기준 조준 각도로 선다. 부모가 좌우로 뒤집히면 배율의 부호로 되돌린다
        // (월드 회전은 Unity가 부모의 뒤집힘까지 셈해 넣으므로 각도는 그대로 준다).
        float size = Mathf.Max(0f, 1f + pulse + firedPulse);
        float radians = shownAngle * Mathf.Deg2Rad;
        var aim = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        transform.localScale = new Vector3(gearScale.x * size * sign, gearScale.y * size, 1f);
        transform.rotation = Quaternion.Euler(0f, 0f, shownAngle);
        Vector2 offset = new Vector2(shiftX * sign, shiftY) + kick + aim * (leanDistance * aimStrength);
        Vector2 origin = Origin;
        transform.position = new Vector3(origin.x + offset.x, origin.y + offset.y, transform.position.z);

        // 빛: Material의 발광색(순간이동 때 EnemyTeleporter가 흰색으로 바꾼다)을 기준으로 가동·열기·섬광을 얹는다.
        float lit = Mathf.Clamp01(power);
        float brightness = Mathf.Lerp(offBrightness, 1f, lit) * Mathf.Lerp(.8f, 1.35f, Mathf.Clamp01(heat / 1.3f));
        float shownFlash = Mathf.Clamp01(flash + firedFlash);
        Glow(hubRenderer, brightness, shownFlash, flashBoost);
        Glow(gearRenderer, brightness, shownFlash * .6f, flashBoost);

        ApplyHorizon(gearScale, sign, lit, shownFlash, time, delta);
    }

    // 사건의 지평선과 빛 고리: 톱니와 같은 자리에 놓고, 톱니가 펼쳐진 비율(등장 때의 가로 펼침)을 그대로 따라간다.
    // 원형이라 Model 루트의 회전이나 좌우 반전은 보이지 않는다. 표시(enabled)는 건드리지 않는다(순간이동이 숨기고 되돌린다).
    private void ApplyHorizon(Vector3 gearScale, float sign, float lit, float shownFlash, float time, float delta)
    {
        float amount = Mathf.Max(0f, horizon) * Mathf.Sqrt(lit);                 // 가동이 꺼지면 지평선도 사라진다
        float shown = Mathf.Clamp01(amount / horizonFadeIn);
        float size = amount <= 1f ? Mathf.Lerp(horizonMinScale, 1f, amount) : 1f + (amount - 1f) * horizonGrowth;
        // Clip이 값을 끊는 곳(움츠렸다가 쏘는 순간)에서도 크기는 짧은 시간에 걸쳐 따라간다. 처음 그릴 때와 미리보기에서는 바로 맞춘다.
        if (delta < 0f || !hasHorizonSize || horizonSmoothTime <= 0f) horizonSize = size;
        else horizonSize = Mathf.Lerp(horizonSize, size, 1f - Mathf.Exp(-delta / horizonSmoothTime));
        hasHorizonSize = true;
        size = horizonSize * Mathf.Max(0f, 1f + (pulse + firedPulse) * horizonPulse);       // 원판이 부풀 때 함께 부푼다(맥동, 쏘는 순간)
        float radius = horizonRadius * size;
        float unit = radius / horizonSpriteRadius;
        var scale = new Vector3(unit * gearScale.x / gearRestScale, unit * gearScale.y / gearRestScale, 1f);
        Vector3 local = gear != null ? gear.localPosition : Vector3.zero;
        HorizonRadius = radius;
        HorizonShown = shown;

        if (horizonRenderer != null)
        {
            Place(horizonRenderer.transform, local, scale);
            horizonRenderer.color = new Color(0f, 0f, 0f, horizonOpacity * shown);
        }

        // 빛 고리: 열기에 따라 밝아지고 미세하게 깜빡인다. 섬광 때는 톱니·원판과 함께 번쩍인다.
        float flicker = 1f + ringFlicker * (Mathf.PerlinNoise(FlickerSeed, time * 9f) * 2f - 1f);
        RingGlow = Mathf.Lerp(offBrightness, 1f, lit) * Mathf.Lerp(ringCoolGlow, ringHotGlow, Mathf.Clamp01(heat / 1.3f)) * shown;
        if (ringRenderer != null)
        {
            Place(ringRenderer.transform, local, scale);
            Glow(ringRenderer, RingGlow * flicker, shownFlash * shown, ringFlashBoost);
        }

        // 강착원반: 그림은 평소 크기(지평선 값 1)로 그려 두고, 지평선과 같은 비율로 부풀고 줄며 함께 나타나고 사라진다.
        // 화면 기준 각도로 돌린다. 좌우로 뒤집힌 부모 아래에서는 배율의 부호로 거울상을 되돌려 나선이 감긴 방향을 늘 같게 둔다(원판과 같은 방식).
        DiskGlow = Mathf.Lerp(offBrightness, 1f, lit) * Mathf.Lerp(diskCoolGlow, diskHotGlow, Mathf.Clamp01(heat / 1.3f)) * shown;
        var diskScale = new Vector3(size * gearScale.x / gearRestScale * sign, size * gearScale.y / gearRestScale, 1f);
        Spin(diskInnerRenderer, local, diskScale, diskInnerAngle, shownFlash * shown);
        Spin(diskOuterRenderer, local, diskScale, diskOuterAngle, shownFlash * shown);
    }

    private void Spin(SpriteRenderer disk, Vector3 localPosition, Vector3 localScale, float angle, float shownFlash)
    {
        if (disk == null) return;
        Transform part = disk.transform;
        part.localPosition = new Vector3(localPosition.x, localPosition.y, part.localPosition.z);
        part.localScale = localScale;
        part.rotation = Quaternion.Euler(0f, 0f, angle);
        Glow(disk, DiskGlow, shownFlash, diskFlashBoost);
    }

    private static void Place(Transform part, Vector3 localPosition, Vector3 localScale)
    {
        part.localPosition = new Vector3(localPosition.x, localPosition.y, part.localPosition.z);
        part.localRotation = Quaternion.identity;
        part.localScale = localScale;
    }

    private void Glow(SpriteRenderer target, float brightness, float shownFlash, float boost)
    {
        if (target == null) return;
        Material material = target.sharedMaterial;
        if (material == null || !material.HasProperty(EmissionColorId) || !material.HasProperty(IntensityId)) return;
        block ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(block);
        // HDR 색은 SetVector로 넣는다(SetColor는 값을 감마 → 선형으로 바꿔 넣어 1을 넘는 값이 몇 배로 커진다).
        block.SetVector(EmissionColorId, Color.Lerp(material.GetColor(EmissionColorId), flashColor, shownFlash));
        block.SetFloat(IntensityId, material.GetFloat(IntensityId) * brightness * (1f + shownFlash * boost));
        target.SetPropertyBlock(block);
    }
}
