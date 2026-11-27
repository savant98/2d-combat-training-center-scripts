using System.Collections.Generic;
using System.Reflection;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

// 모든 가상 카메라(CinemachineCamera)에 공통으로 붙는 Player 프레이밍 확장(CinemachineExtension 상속).
// FixedMotionPresentation이 카메라 갱신 전에 모든 VCam에 붙이고(Ensure), 각 VCam은 Body 단계 직후·Confiner 앞에서 카메라 위치를 정한다.
// Follow가 Player이고(보간 대리 MotionTarget_*는 원래 대상으로 확인) Body가 CinemachineFollow인 VCam만 적용한다. 다른 대상(보스 소개·시설 포커스·
// 컷씬)으로 바뀌면 blendTime 동안 원래 Cinemachine 위치로 섞는다. VCam의 Lens·Follow·Damping·Confiner 설정은 바꾸지 않는다(FollowOffset은 목표에 더한다).
// - 세로: 서 있으면 몸보다 groundLift 위(발밑은 최소 minRowsBelowFeet칸 보장). 점프는 jumpWindow 높이까지 카메라를 두고 착지할 때 맞춘다(점프 창).
//   서 있던 높이보다 fallLookDepth 넘게 떨어지면 낙하 속도에 따라 아래를 보이고, 떨어지거나 빠르게 오를 때는 속도만큼 앞세워 바짝 따라간다.
// - 가로: 몸이 데드존(deadZoneX) 안에서 움직이면 카메라 기준점이 움직이지 않는다. 움직이는 동안 그 방향으로 moveLead만큼 천천히 앞세운다.
// - 멈춰 서서 ↑·↓를 lookDelay 넘게 누르면 위·아래를 더 본다. 추적 지연은 maxLag를 넘지 않고, 순간이동은 OnTargetObjectWarped로 함께 옮긴다.
// 설정은 CameraFramingSO(Resources/Camera/CameraFraming).
[DisallowMultipleComponent]
[AddComponentMenu("Cinemachine/Procedural/Extensions/Player Camera Framing")]
public sealed class PlayerCameraFraming : CinemachineExtension
{
    /// <summary>프레이밍 상태(검증·디버그용 읽기 전용 값).</summary>
    public struct State
    {
        public int frame;
        public float weight;
        public bool grounded, settled, dropping, rising;
        public float referenceY, frameY, lead, look, anchorX, feedX;
        public Vector2 aim, target, framed, bodyVelocity, output;
    }

    private static readonly PropertyInfo ExtensionsProperty =
        typeof(CinemachineVirtualCameraBase).GetProperty("Extensions", BindingFlags.Instance | BindingFlags.NonPublic);
    private static State lastState;

    /// <summary>가장 최근에 Player를 프레이밍한 VCam의 상태.</summary>
    public static State Last => lastState;

    /// <summary>Play 중 비교용 켜기·끄기(저장하지 않는다). null이면 설정 에셋의 framingEnabled를 따른다.</summary>
    public static bool? RuntimeOverride { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        RuntimeOverride = null;
        lastState = default;
    }

    private bool hasState;
    private int lastFrame = -10;
    private float weight;
    private Vector2 current, velocity, lastBody, bodyVelocity, feedVelocity, aim, aimVelocity;
    private float anchorX, referenceY, groundY, hangTime, pendingTime, sustain;
    private int leadDirection = 1, pendingDirection, lookSign;
    private float lead, leadVelocity, frameY, frameVelocity, look, lookVelocity, lookHeld;
    private float halfHeight = -1f;

    /// <summary>VCam에 프레이밍 확장이 없으면 붙인다. Player를 따라가지 않는 VCam에서는 아무 일도 하지 않는다.</summary>
    public static void Ensure(CinemachineCamera camera)
    {
        if (camera == null || camera.TryGetComponent<PlayerCameraFraming>(out _)) return;
        camera.gameObject.AddComponent<PlayerCameraFraming>();
    }

    protected override void ConnectToVcam(bool connect)
    {
        base.ConnectToVcam(connect);
        // Prefab에 먼저 붙은 확장(Confiner2D 등)보다 앞에서 위치를 정해야 경계 보정이 프레이밍 결과에 적용된다.
        if (connect && ComponentOwner != null && ExtensionsProperty?.GetValue(ComponentOwner) is List<CinemachineExtension> list && list.Remove(this))
            list.Insert(0, this);
    }

    public override void OnTargetObjectWarped(CinemachineVirtualCameraBase vcam, Transform target, Vector3 positionDelta)
    {
        base.OnTargetObjectWarped(vcam, target, positionDelta);
        if (!hasState) return;
        Vector2 d = positionDelta;
        current += d;
        lastBody += d;
        anchorX += d.x;
        referenceY += d.y;
        groundY += d.y;
    }

    public override void ForceCameraPosition(CinemachineVirtualCameraBase vcam, Vector3 pos, Quaternion rot) => hasState = false;

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage == CinemachineCore.Stage.Body) Frame(vcam, ref state, deltaTime);
    }

    private void Frame(CinemachineVirtualCameraBase vcam, ref CameraState state, float deltaTime)
    {
        var settings = CameraFramingSO.Default;
        var camera = vcam as CinemachineCamera;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        Vector3 output = state.RawPosition;
        int frame = Time.frameCount;
        if (settings == null || player == null || camera == null || !state.Lens.Orthographic)
        {
            weight = 0f;
            hasState = false;
            return;
        }
        if (frame == lastFrame)
        {
            // 같은 프레임에 이 VCam 갱신이 또 오면 상태를 더 진행하지 않고 같은 위치만 다시 놓는다.
            if (weight > 0f) state.RawPosition = Place(output);
            return;
        }
        bool follows = (RuntimeOverride ?? settings.framingEnabled) && camera.Follow != null && FixedMotionPresentation.SourceOf(camera.Follow) == player.transform
            && camera.GetCinemachineComponent(CinemachineCore.Stage.Body) is CinemachineFollow;
        bool continuous = hasState && deltaTime >= 0f && frame == lastFrame + 1;
        lastFrame = frame;
        float dt = deltaTime >= 0f ? deltaTime : Time.deltaTime;
        weight = continuous ? Mathf.MoveTowards(weight, follows ? 1f : 0f, dt / Mathf.Max(.01f, settings.blendTime)) : (follows ? 1f : 0f);

        FixedMotionBody.GetPresentationPose(player.transform, out var bodyPosition, out _);
        Vector2 body = bodyPosition;
        if (halfHeight < 0f)
            halfHeight = player.TryGetComponent<Collider2D>(out var collider) ? Mathf.Max(.1f, collider.bounds.extents.y) : .75f;
        float ortho = state.Lens.OrthographicSize;
        float scale = ortho / Mathf.Max(.01f, settings.referenceOrthographicSize);
        float snap = settings.snapDistance * Mathf.Max(1f, scale);
        bool cut = !continuous || (body - lastBody).sqrMagnitude > snap * snap;
        // 보간 위치로 잰 실제 이동 속도(운반·크레인처럼 모터 속도와 다른 이동도 포함). 앞세우기에는 대시처럼 짧게 튀는 값을 더 거른 속도를 쓴다.
        Vector2 measured = !cut && dt > 1e-5f ? (body - lastBody) / dt : Vector2.zero;
        bodyVelocity = cut ? measured : Vector2.Lerp(bodyVelocity, measured, 1f - Mathf.Exp(-dt / .05f));
        feedVelocity = cut ? Vector2.zero : Vector2.Lerp(feedVelocity, bodyVelocity, 1f - Mathf.Exp(-dt / Mathf.Max(.01f, settings.velocityFilterTime)));
        lastBody = body;
        hasState = true;

        if (weight <= 0f)
        {
            // Cinemachine 위치를 그대로 쓰는 동안은 내부 상태를 그 위치에 맞춰 둔다(돌아올 때 그 자리에서 섞기 시작한다).
            ResetOffsets();
            current = output;
            anchorX = body.x;
            referenceY = groundY = body.y;
            return;
        }

        Vector2 offset = camera.TryGetComponent<CinemachineFollow>(out var follow) ? (Vector2)follow.FollowOffset : Vector2.zero;
        // 점프를 누른 첫 틱은 아직 땅으로 읽히므로 위로 뜨는 속도가 있으면 서 있는 것으로 보지 않는다(모터 속도는 점프 즉시 바뀐다).
        bool grounded = player.IsGrounded && player.Velocity.y <= .5f;
        Vector2 v = bodyVelocity;

        // 1) 세로 기준 높이: 서 있거나(크레인 매달림처럼 공중에 멈춘 것 포함) 빠르게 오르면 몸 높이, 점프는 창 안에서 그대로, 기준 아래로 떨어지면 따라간다.
        hangTime = !grounded && Mathf.Abs(v.y) < .5f ? hangTime + dt : 0f;
        bool settled = grounded || hangTime >= settings.hangSettleTime;
        bool rising = !grounded && v.y > settings.fastRiseSpeed;
        float window = settings.jumpWindow * scale;
        if (cut || settled || rising) referenceY = groundY = body.y;
        else if (body.y > referenceY + window) referenceY = body.y - window;
        else if (body.y < referenceY) referenceY = body.y;
        // 서 있던 높이보다 fallLookDepth 넘게 떨어지는 중일 때만 아래를 본다(같은 발판으로 내려오는 점프·짧은 층 사이 낙하는 제외).
        bool dropping = !settled && v.y < 0f && body.y <= referenceY + 1e-4f && groundY - body.y > settings.fallLookDepth * scale;
        float fall = dropping ? Mathf.InverseLerp(settings.fallSpeedRange.x, settings.fallSpeedRange.y, -v.y) : 0f;
        float frameGoal = Mathf.Lerp(settings.groundLift, -settings.fallLookDown, fall) * scale;
        if (cut) { frameY = frameGoal; frameVelocity = 0f; }
        else frameY = Mathf.SmoothDamp(frameY, frameGoal, ref frameVelocity, frameGoal < frameY ? .25f : settings.landingSettleTime, Mathf.Infinity, dt);

        // 2) 가로: 데드존 기준점과 진행 방향 선행. 반대 방향이 turnDelay 동안 이어져야 선행 방향을 바꾸고,
        //    선행·속도 앞세우기는 한 방향으로 sustainTime 넘게 계속 움직일 때만 켠다(짧게 오가는 전투 움직임에는 화면을 움직이지 않는다).
        int moving = Mathf.Abs(v.x) > .75f ? (v.x > 0f ? 1 : -1) : 0;
        float deadZone = settings.deadZoneX * scale;
        if (cut) anchorX = body.x;
        else if (body.x > anchorX + deadZone) anchorX = body.x - deadZone;
        else if (body.x < anchorX - deadZone) anchorX = body.x + deadZone;
        else if (moving == 0) anchorX = Mathf.MoveTowards(anchorX, body.x, settings.recenterSpeed * scale * dt);
        int wanted = moving != 0 ? moving : (player.Facing >= 0 ? 1 : -1);
        if (cut) { leadDirection = wanted; pendingTime = 0f; }
        else if (wanted != leadDirection)
        {
            if (pendingDirection != wanted) { pendingDirection = wanted; pendingTime = 0f; }
            pendingTime += dt;
            if (pendingTime >= settings.turnDelay) { leadDirection = wanted; pendingTime = 0f; }
        }
        else pendingTime = 0f;
        sustain = moving != 0 && moving == leadDirection && !cut ? sustain + dt : 0f;
        float sustainWeight = Mathf.Clamp01((sustain - settings.sustainTime) / .3f);
        sustainWeight = sustainWeight * sustainWeight * (3f - 2f * sustainWeight);
        bool mouseAim = player.UsesMouseAim;
        float leadGoal = leadDirection * (moving != 0 ? settings.moveLead * sustainWeight : mouseAim ? 0f : settings.idleLead) * scale;
        if (cut) { lead = leadGoal; leadVelocity = 0f; }
        else lead = Mathf.SmoothDamp(lead, leadGoal, ref leadVelocity, settings.leadSmoothTime, Mathf.Infinity, dt);

        // 3) 둘러보기(멈춰 서서 ↑·↓ 유지)와 마우스 조준 선행(화면 기준 커서 위치라 카메라가 움직여도 되먹임이 없다)
        bool input = CanUseInput();
        int lookInput = 0;
        if (input && settled && moving == 0 && InputManager.Instance != null)
        {
            float y = InputManager.Instance.Move.y;
            lookInput = y > .5f ? 1 : y < -.5f ? -1 : 0;
        }
        if (lookInput != 0 && lookInput == lookSign) lookHeld += dt;
        else { lookSign = lookInput; lookHeld = 0f; }
        float lookGoal = lookSign != 0 && lookHeld >= settings.lookDelay ? (lookSign > 0 ? settings.lookUp : -settings.lookDown) * scale : 0f;
        if (cut) { look = lookGoal; lookVelocity = 0f; }
        else look = Mathf.SmoothDamp(look, lookGoal, ref lookVelocity, settings.lookSmoothTime, Mathf.Infinity, dt);
        Vector2 aimGoal = Vector2.zero;
        var view = Camera.main;
        if (mouseAim && input && Mouse.current != null && view != null)
        {
            Vector3 viewport = view.ScreenToViewportPoint(Mouse.current.position.ReadValue());
            aimGoal = new Vector2(Mathf.Clamp(viewport.x * 2f - 1f, -1f, 1f) * settings.mouseAimLead.x,
                                  Mathf.Clamp(viewport.y * 2f - 1f, -1f, 1f) * settings.mouseAimLead.y) * scale;
        }
        if (cut) { aim = aimGoal; aimVelocity = Vector2.zero; }
        else aim = Vector2.SmoothDamp(aim, aimGoal, ref aimVelocity, .35f, Mathf.Infinity, dt);

        // 4) 목표와 추적. 걷는 속도는 거른 값만큼 앞세워 추적 지연을 없애고, 서 있던 높이 아래로 떨어지거나 빠르게 오를 때는 세로도 앞세워 바짝 따라간다.
        bool fastY = rising || (!settled && v.y < 0f && body.y < groundY - .2f * scale);
        Vector2 maxLag = settings.maxLag * scale;
        float feedX = Mathf.Clamp(feedVelocity.x * settings.smoothTimeX, -maxLag.x * .5f, maxLag.x * .5f) * sustainWeight;
        float feedY = fastY ? Mathf.Clamp(v.y * settings.fastSmoothTimeY, -maxLag.y, maxLag.y) : 0f;
        var target = new Vector2(anchorX + offset.x + lead + aim.x + feedX, referenceY + offset.y + frameY + look + aim.y + feedY);
        // 서 있을 때는 발밑 minRowsBelowFeet칸이 늘 화면에 남게 카메라를 그보다 위로 올리지 않는다(위 보기·마우스 조준 선행 포함).
        if (settled) target.y = Mathf.Min(target.y, body.y - halfHeight + ortho - settings.minRowsBelowFeet * scale);
        if (cut)
        {
            current = target;
            velocity = Vector2.zero;
        }
        else
        {
            current.x = Mathf.SmoothDamp(current.x, target.x, ref velocity.x, settings.smoothTimeX, Mathf.Infinity, dt);
            current.y = Mathf.SmoothDamp(current.y, target.y, ref velocity.y, fastY ? settings.fastSmoothTimeY : settings.smoothTimeY, Mathf.Infinity, dt);
            if (Mathf.Abs(current.x - target.x) > maxLag.x) current.x = target.x + Mathf.Sign(current.x - target.x) * maxLag.x;
            if (Mathf.Abs(current.y - target.y) > maxLag.y) current.y = target.y + Mathf.Sign(current.y - target.y) * maxLag.y;
        }
        // 내부 위치가 Confiner 상자의 실제 한계를 넘어가면 그 축은 멈춘다(경계 밖에서 되돌아오는 지연이 없게).
        // Confiner2D의 감속 거리·모서리 감쇠는 이 뒤 출력에만 그대로 적용된다(그 보정은 내부 상태에 섞지 않는다).
        HardLimit(camera, state.Lens);
        state.RawPosition = Place(output);
        lastState = new State
        {
            frame = frame, weight = weight, grounded = grounded, settled = settled, dropping = dropping, rising = rising,
            referenceY = referenceY, frameY = frameY, lead = lead, look = look, anchorX = anchorX, feedX = feedX, aim = aim, target = target,
            framed = state.RawPosition, bodyVelocity = bodyVelocity, output = output,
        };
    }

    // VCam Confiner2D의 상자(BoxCollider2D) 안에서 화면이 머물 수 있는 중심 범위로 내부 위치를 자른다. 다각형·회전 범위는 Confiner2D가 출력에서 맡는다.
    private void HardLimit(CinemachineCamera camera, LensSettings lens)
    {
        if (!camera.TryGetComponent<CinemachineConfiner2D>(out var confiner) || !confiner.enabled || !(confiner.BoundingShape2D is BoxCollider2D box)) return;
        var frame = box.transform;
        if (Mathf.Abs(frame.eulerAngles.z) > .01f && Mathf.Abs(frame.eulerAngles.z - 360f) > .01f) return;
        Vector2 center = frame.TransformPoint(box.offset);
        Vector3 scale = frame.lossyScale;
        Vector2 half = new(Mathf.Abs(box.size.x * scale.x) * .5f, Mathf.Abs(box.size.y * scale.y) * .5f);
        float halfH = lens.OrthographicSize, halfW = halfH * lens.Aspect;
        float rangeX = Mathf.Max(0f, half.x - halfW), rangeY = Mathf.Max(0f, half.y - halfH);
        float x = Mathf.Clamp(current.x, center.x - rangeX, center.x + rangeX);
        float y = Mathf.Clamp(current.y, center.y - rangeY, center.y + rangeY);
        if (!Mathf.Approximately(x, current.x)) { current.x = x; velocity.x = 0f; }
        if (!Mathf.Approximately(y, current.y)) { current.y = y; velocity.y = 0f; }
    }

    private Vector3 Place(Vector3 output)
    {
        float w = weight >= 1f ? 1f : weight * weight * (3f - 2f * weight);
        return new Vector3(Mathf.Lerp(output.x, current.x, w), Mathf.Lerp(output.y, current.y, w), output.z);
    }

    private void ResetOffsets()
    {
        current = velocity = Vector2.zero;
        hangTime = pendingTime = sustain = 0f;
        lead = leadVelocity = frameY = frameVelocity = look = lookVelocity = lookHeld = 0f;
        lookSign = 0;
        aim = aimVelocity = Vector2.zero;
    }

    private static bool CanUseInput()
    {
        var ui = UIManager.Instance;
        return ui == null || (!ui.BlocksGameplayInput && !ui.HasActiveInputUI);
    }
}
