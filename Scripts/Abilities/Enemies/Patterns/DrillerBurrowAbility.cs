using System;
using UnityEngine;

// Driller 땅파기.
// 준비: 드릴 회전을 올리며 제자리에서 바닥을 파고든다. 몸의 절반쯤 들어가면 피격 판정을 끄고 무적이 된다.
// 실행: 땅속(모터 Collider는 바닥 위 그대로)에서 플레이어 아래로 이동 → 드릴 끝을 바닥 위로 내밀어 예고 → 드릴부터 튀어올라 공중에서 반 바퀴 돌아 착지.
// 회수: 착지 뒤 흔들림을 가라앉힌다.
// 이동은 controller.Velocity만 바꾸고, 바닥 끝·벽은 모터의 HasFloorAhead·OnWallHit로 판단한다. 표시는 DrillerRig가 맡는다.
public sealed class DrillerBurrowAbility : AiAbility
{
    public enum Phase { None, Digging, Travel, Warning, Airborne, Landed }

    [SerializeField] private DrillerRig rig;

    private DrillerBurrowDataSO settings;
    private Transform player;
    private Phase phase;
    private float phaseStart;
    private float groundY;
    private float nextFeedbackTime;
    private bool hidden;
    private IDisposable invulnerability;
    private bool wallSubscribed, landedSubscribed;
    private bool blocked;
    private float groundLostTime;
    private float airStart, airDuration;
    private float launchDepth;
    // 분출 뒤 한 번이라도 바닥에서 떨어졌는지. 그 뒤의 첫 접지를 착지로 본다(낮은 천장에서 바로 내려앉아도 놓치지 않는다).
    private bool leftGround;
    private float recoverFromAngle;
    private SoundHandle loopHandle;
    private float loopVolume;
    private float loopFadeStart = -1f;

    public Phase CurrentPhase => phase;
    public bool IsHidden => hidden;
    public float GroundY => groundY;
    // 검증용: 마지막 분출 속도와 계산한 체공 시간
    public Vector2 LaunchVelocity { get; private set; }
    public float PlannedAirTime => airDuration;

    // 튀어나와 몸 전체가 바닥선 위로 올라온 때(땅파기 Mask가 꺼진 뒤)부터 착지까지가 실제 공격 구간이다.
    // 몸통 위험 외곽선은 Mask로 자를 수 없으므로(별도 개체) 땅속 부분이 남아 있는 동안에는 켜지 않는다.
    public override bool IsBodyAttackActive => State == AbilityState.Execute && phase == Phase.Airborne
        && rig != null && !rig.IsMaskActive;

    private void Awake()
    {
        settings = (DrillerBurrowDataSO)data;
        if (rig == null) rig = GetComponent<DrillerRig>();
    }

    private void OnDisable() => Cleanup();

    protected override void OnWindupStart()
    {
        var target = GameManager.Instance != null ? GameManager.Instance.Player : null;
        player = target != null ? target.transform : null;
        if (rig == null || !controller.IsGrounded)
        {
            StopAbility();
            return;
        }
        if (player != null)
            controller.FacingUpdate(player.position.x >= transform.position.x ? 1 : -1);
        controller.IgnoreExternalForces = true;
        groundY = FeetWorldY();
        rig.ResetMechanism();
        rig.Pivot = DrillerRig.PosePivot.Center;
        rig.IdleWobble = false;
        rig.SpinTarget = settings.digSpin;
        rig.BeginBurrowMask(groundY);
        phase = Phase.Digging;
        phaseStart = Time.time;
        nextFeedbackTime = Time.time + settings.sinkDelay;
        blocked = false;
        groundLostTime = 0f;
    }

    protected override void OnWindup(float t)
    {
        // 순찰 속도를 짧게 줄이고 제자리에서 파고든다.
        controller.Velocity.x = Mathf.MoveTowards(controller.Velocity.x, 0f, settings.travelAcceleration * Time.deltaTime);
        float span = Mathf.Max(.01f, data.windupTime - settings.sinkDelay);
        float sink = Mathf.Clamp01((t - settings.sinkDelay) / span);
        float depth = settings.hiddenDepth * sink * sink;
        rig.BurrowDepth = depth;
        // 파고드는 동안만 짧게 떨린다(Scale은 쓰지 않는다).
        float shake = t >= settings.sinkDelay ? settings.digShakeDegrees * Mathf.Sin(t * 70f) * (1f - sink * .5f) : 0f;
        rig.PoseAngle = shake;
        if (!hidden && depth >= settings.hiddenDepth * settings.hideRatio) SetHidden(true);
        if (t >= settings.sinkDelay && Time.time >= nextFeedbackTime)
        {
            nextFeedbackTime = Time.time + settings.digInterval;
            PlayAtFeet(settings.digFeedback);
        }
    }

    protected override void OnExecuteStart()
    {
        SetHidden(true);
        rig.PoseAngle = 0f;
        rig.BurrowDepth = settings.hiddenDepth;
        rig.SpinTarget = settings.travelSpin;
        rig.SetDigTrail(true);
        SetWallSubscription(true);
        StartLoop();
        phase = Phase.Travel;
        phaseStart = Time.time;
        nextFeedbackTime = Time.time;
    }

    protected override void OnExecute(float t)
    {
        UpdateLoop();
        switch (phase)
        {
            case Phase.Travel: UpdateTravel(); break;
            case Phase.Warning: UpdateWarning(); break;
            case Phase.Airborne: UpdateAirborne(); break;
        }
    }

    private void UpdateTravel()
    {
        if (!controller.IsGrounded)
        {
            // 땅속에 있는 동안 발밑이 사라지면(지형 변경) 예고·분출 없이 그 자리에서 드러나 떨어진다.
            groundLostTime += Time.deltaTime;
            if (groundLostTime >= .12f) { Surface(); return; }
        }
        else
        {
            groundLostTime = 0f;
            groundY = FeetWorldY();
            rig.HoldMaskAt(groundY);
        }

        float deltaX = player != null ? player.position.x - transform.position.x : 0f;
        int direction = deltaX >= 0f ? 1 : -1;
        bool arrived = Mathf.Abs(deltaX) <= settings.arriveDistance;
        bool ledge = controller.IsGrounded && controller.Motor != null
            && !controller.Motor.HasFloorAhead(direction, .05f, .5f);
        bool timedOut = Time.time - phaseStart >= settings.maxTravelTime;
        if (arrived || timedOut || blocked || ledge)
        {
            BeginWarning();
            return;
        }

        if (controller.Facing != direction) controller.FacingUpdate(direction);
        // 도착 직전에는 남은 거리에 맞춰 줄여 지나치지 않게 한다.
        float speed = Mathf.Min(settings.travelSpeed, Mathf.Abs(deltaX) * 8f);
        controller.Velocity.x = Mathf.MoveTowards(controller.Velocity.x, direction * speed, settings.travelAcceleration * Time.deltaTime);
        if (Time.time >= nextFeedbackTime)
        {
            nextFeedbackTime = Time.time + settings.trailInterval;
            PlayAtFeet(settings.trailFeedback);
        }
    }

    private void BeginWarning()
    {
        controller.Velocity.x = 0f;
        SetWallSubscription(false);
        phase = Phase.Warning;
        phaseStart = Time.time;
        // 땅속에서 뒤집어 드릴 끝이 위를 향하게 둔다(아직 보이지 않는 깊이).
        rig.Pivot = DrillerRig.PosePivot.Center;
        rig.GroundSnap = true;
        rig.PoseAngle = 180f;
        rig.BurrowDepth = settings.hiddenDepth;
        rig.SpinTarget = settings.peekSpin;
        if (player != null) controller.FacingUpdate(player.position.x >= transform.position.x ? 1 : -1);
        PlayAtFeet(settings.peekFeedback);
    }

    private void UpdateWarning()
    {
        controller.Velocity.x = 0f;
        if (!controller.IsGrounded)
        {
            groundLostTime += Time.deltaTime;
            if (groundLostTime >= .12f) { Surface(); return; }
        }
        else
        {
            groundLostTime = 0f;
            groundY = FeetWorldY();
            rig.HoldMaskAt(groundY);
        }
        float t = Time.time - phaseStart;
        float rise = Mathf.Clamp01(t / settings.peekRiseTime);
        rise = 1f - (1f - rise) * (1f - rise);
        rig.BurrowDepth = Mathf.Lerp(settings.hiddenDepth, PeekDepth, rise);
        if (t >= settings.warningTime) Erupt();
    }

    // 뒤집힌 몸(드릴 끝이 위)에서 드릴 끝만 peekHeight만큼 바닥 위에 보이는 깊이
    private float PeekDepth => Mathf.Max(0f, rig.VisualHeight - settings.peekHeight);

    private void Erupt()
    {
        if (loopHandle != null && loopFadeStart < 0f) loopFadeStart = Time.time;
        rig.SetDigTrail(false);
        SetHidden(false);
        float g = Mathf.Max(.01f, -controller.Gravity);
        float feet = FeetWorldY();
        float height = settings.minJumpHeight;
        if (player != null) height = Mathf.Max(height, player.position.y - feet + settings.jumpAbovePlayer);
        height = Mathf.Clamp(height, settings.minJumpHeight, Mathf.Max(settings.minJumpHeight, settings.maxJumpHeight));
        float vy = Mathf.Sqrt(2f * g * height);
        airDuration = 2f * vy / g;
        float vx = 0f;
        if (player != null)
            vx = Mathf.Clamp((player.position.x - transform.position.x) / airDuration, -settings.maxHorizontalSpeed, settings.maxHorizontalSpeed);
        controller.Velocity = new Vector2(vx, vy);
        LaunchVelocity = controller.Velocity;
        launchDepth = rig.BurrowDepth;
        // 공중에서는 몸 가운데를 축으로 돌린다(땅에 맞추지 않아야 회전이 매끄럽다). 뒤집힌 자세에서 같은 높이 범위다.
        rig.GroundSnap = false;
        rig.SpinTarget = settings.airSpin;
        phase = Phase.Airborne;
        airStart = Time.time;
        leftGround = false;
        rig.MeasureSideRoom(3f, 0f, -.6f, .6f);
        SetLandedSubscription(true);
        if (settings.eruptFeedback != null && feedbackPlayer != null)
            feedbackPlayer.PlayExact(settings.eruptFeedback, new Vector3(transform.position.x, groundY, 0f), Quaternion.identity, controller);
    }

    private void UpdateAirborne()
    {
        float t = Time.time - airStart;
        float emerge = Mathf.Clamp01(t / settings.emergeTime);
        rig.BurrowDepth = launchDepth * (1f - emerge);
        // 드릴이 위를 향한 자세(180도)에서 앞으로 반 바퀴 돌아 바로 선다(0도).
        float flip = Mathf.Clamp01(t / Mathf.Max(.1f, airDuration * .9f));
        flip = flip * flip * (3f - 2f * flip);
        rig.PoseAngle = 180f * (1f - flip);
        // 몸 아래 끝이 바닥선 위로 다 올라오면 Mask를 끈다.
        if (rig.IsMaskActive && FeetWorldY() - rig.BurrowDepth > groundY + .02f) rig.EndBurrowMask();
        // 회전한 몸이 옆 벽을 넘지 않게 지금 자리에서 벽 여유를 다시 잰다.
        rig.MeasureSideRoom(3f, 0f, -.6f, .6f);
        if (!controller.IsGrounded) leftGround = true;
        else if ((leftGround || t > airDuration + .3f) && controller.Velocity.y <= 0f) phase = Phase.Landed;
        // 몸통 판정(실행 순서 110의 위험 외곽선 포함)이 이번 프레임 자세를 쓰도록 바로 적용한다.
        rig.ApplyPose();
    }

    // 물리 단계에서 바닥을 떠난 순간을 기록한다. 저 fps에서는 떠올랐다 내려앉는 일이 한 프레임 안에 끝날 수 있다.
    private void HandleFalled()
    {
        if (State == AbilityState.Execute && phase == Phase.Airborne) leftGround = true;
    }

    private void HandleLanded()
    {
        if (State != AbilityState.Execute || phase != Phase.Airborne || !leftGround) return;
        phase = Phase.Landed;
    }

    // 땅속에서 발밑을 잃었을 때: 숨김·Mask를 풀고 바로 선 자세로 떨어진다(보이지 않는 분출을 만들지 않는다).
    private void Surface()
    {
        if (loopHandle != null && loopFadeStart < 0f) loopFadeStart = Time.time;
        SetWallSubscription(false);
        rig.SetDigTrail(false);
        rig.EndBurrowMask();
        SetHidden(false);
        rig.BurrowDepth = 0f;
        rig.PoseAngle = 0f;
        rig.GroundSnap = true;
        controller.Velocity.x = 0f;
        phase = Phase.Landed;
    }

    protected override bool CanFinishExecute(float elapsed) => phase == Phase.Landed || elapsed >= data.executeTime;

    protected override void OnRecoverStart()
    {
        SetLandedSubscription(false);
        SetWallSubscription(false);
        StopLoop();
        rig.EndBurrowMask();
        rig.SetDigTrail(false);
        SetHidden(false);
        rig.BurrowDepth = 0f;
        rig.GroundSnap = true;
        // 낮은 천장 등으로 일찍 내려앉아 덜 돈 경우 남은 회전을 회수 앞부분에서 마저 돈다.
        recoverFromAngle = Mathf.Repeat(rig.PoseAngle + 180f, 360f) - 180f;
        rig.SpinTarget = -1f;
        controller.Velocity.x = 0f;
        phase = Phase.None;
    }

    protected override void OnRecover(float t)
    {
        controller.Velocity.x = 0f;
        float settle = Mathf.Clamp01(t / .18f);
        settle = settle * settle * (3f - 2f * settle);
        // 착지 충격으로 기울었다가 가라앉는다(본체 회전만 쓴다).
        rig.PoseAngle = recoverFromAngle * (1f - settle)
            + settings.landingSettleDegrees * Mathf.Exp(-t * 7f) * Mathf.Sin(t * 22f);
    }

    protected override void OnStop()
    {
        bool paused = brain != null && brain.IsPaused;
        Cleanup();
        if (paused) controller.Velocity = Vector2.zero;
    }

    private void Cleanup()
    {
        SetLandedSubscription(false);
        SetWallSubscription(false);
        StopLoop();
        SetHidden(false);
        if (rig != null)
        {
            rig.EndBurrowMask();
            rig.SetDigTrail(false);
            rig.ResetMechanism();
        }
        if (controller != null) controller.IgnoreExternalForces = false;
        phase = Phase.None;
    }

    private void SetHidden(bool value)
    {
        hidden = value;
        if (rig != null) rig.SetHittable(!value);
        if (value)
        {
            var health = controller != null ? controller.Health : null;
            if (invulnerability == null && health != null) invulnerability = health.AcquireInvulnerability(this);
        }
        else
        {
            invulnerability?.Dispose();
            invulnerability = null;
        }
    }

    private float FeetWorldY() => transform.position.y + (rig != null ? rig.FeetY : -1f);

    private void PlayAtFeet(FeedbackDataSO feedback)
    {
        if (feedback == null || feedbackPlayer == null) return;
        feedbackPlayer.PlayExact(feedback, new Vector3(transform.position.x, groundY, 0f), Quaternion.identity, controller);
    }

    private void HandleWallHit()
    {
        if (State != AbilityState.Execute || phase != Phase.Travel) return;
        float push = controller.Velocity.x;
        if (Mathf.Abs(push) > .001f && Mathf.Sign(push) == controller.Facing) blocked = true;
    }

    private void SetWallSubscription(bool value)
    {
        if (controller == null || wallSubscribed == value) return;
        if (value) controller.OnWallHit += HandleWallHit;
        else controller.OnWallHit -= HandleWallHit;
        wallSubscribed = value;
    }

    private void SetLandedSubscription(bool value)
    {
        if (controller == null || landedSubscribed == value) return;
        if (value) { controller.OnLanded += HandleLanded; controller.OnFalled += HandleFalled; }
        else { controller.OnLanded -= HandleLanded; controller.OnFalled -= HandleFalled; }
        landedSubscribed = value;
    }

    // 땅속 반복음은 적을 따라가고, 플레이어와 멀어지면 줄인다(적 효과음은 2D라 거리 감쇠를 직접 곱한다).
    private void StartLoop()
    {
        StopLoop();
        var sound = settings.undergroundLoop;
        if (sound == null || sound.clip == null || SoundManager.Instance == null) return;
        loopHandle = SoundManager.Instance.PlayLoop(sound, transform.position, transform);
        if (loopHandle == null || !loopHandle.IsValid) { loopHandle = null; return; }
        loopVolume = loopHandle.GetSource().volume;
        loopFadeStart = -1f;
        UpdateLoop();
    }

    private void UpdateLoop()
    {
        if (loopHandle == null) return;
        if (!loopHandle.IsValid) { loopHandle = null; return; }
        float gain = 1f;
        if (player != null)
        {
            float distance = Vector2.Distance(player.position, transform.position);
            gain = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 22f, distance));
        }
        if (loopFadeStart >= 0f)
        {
            float fade = settings.undergroundLoopFade > 0f ? 1f - (Time.time - loopFadeStart) / settings.undergroundLoopFade : 0f;
            if (fade <= 0f) { StopLoop(); return; }
            gain *= fade;
        }
        loopHandle.GetSource().volume = loopVolume * gain;
    }

    private void StopLoop()
    {
        if (loopHandle != null && loopHandle.IsValid) loopHandle.Stop();
        loopHandle = null;
        loopFadeStart = -1f;
    }
}
