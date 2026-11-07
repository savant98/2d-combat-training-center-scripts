using System.Collections.Generic;
using UnityEngine;
using System;

[DefaultExecutionOrder(-50)]
public sealed class StomperWrestlingGrabAbility : AiAbility
{
    public enum GrabPhase { None, Extend, Blackout, Rise, Spin, Descent, ImpactFrame, Slam, Impact, Fade, Finished }
    private StomperWrestlingGrabDataSO Settings => (StomperWrestlingGrabDataSO)data;
    private StomperWrestlingGrabVisual visual;
    private PlayerController player;
    private PlayerGrabController grab;
    private GrabCinematicPresentation presentation;
    private uint session;
    private readonly List<Collider2D> hits = new List<Collider2D>();
    private float phaseAt, angle, contactAngle, nextWindAngle, descentAt;
    private Vector3 playerModelOffset;
    private Collider2D playerBody;
    public int WindCueCount { get; private set; }
    private Vector3 armTarget;
    private bool gravity, ignoreExternal, ownsMovement, visualImpactReady;
    private SoundHandle warningLoop, rocketLoop;
    private RaycastHit2D platformContact;
    private StomperGrabLandingPlan landing;
    private IDisposable bossProtection;
    private Vector2 bossTransit, playerTransit;
    private bool handoffStarted, gameplayCut;
    private float flightAt, handoffWaitAt;
    public Vector2 LandingCenter => landing.Floor;
    public bool HandoffStarted => handoffStarted;
    public bool BossProtected => controller.Health.IsDamageBlocked;
    public Collider2D ContactPlatform => platformContact.collider;
    public bool RocketSoundIsPlaying => rocketLoop != null && rocketLoop.IsValid;
    public GrabPhase CurrentPhase { get; private set; }
    public bool Captured { get; private set; }
    public int ImpactCount { get; private set; }
    public float SpinDegrees => angle;
    public bool AppliedDamage { get; private set; }
    public uint GrabSession => session;
    public Vector3 ImpactPosition { get; private set; }
    public bool WarningIsPlaying => warningLoop != null && warningLoop.IsValid;
    protected override void OnWindupStart()
    {
        visual = GetComponent<StomperWrestlingGrabVisual>();
        visual.Initialize(controller); visual.Begin(Settings);
        player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        grab = null; presentation = null; session = 0;
        Captured = AppliedDamage = visualImpactReady = false; ImpactCount = 0; angle = 0;
        handoffStarted=gameplayCut=false; handoffWaitAt=-1; landing=default; CurrentPhase = GrabPhase.None; platformContact = default; WindCueCount = 0; nextWindAngle = 360;
        gravity = controller.GravityOn; ignoreExternal = controller.IgnoreExternalForces; ownsMovement = true;
        controller.GravityOn = false; controller.IgnoreExternalForces = true;
        controller.Velocity = controller.externalForce = Vector2.zero;
        if (player != null) controller.FacingUpdate(player.transform.position.x >= transform.position.x ? 1 : -1);
        StopWarning();
        if (SoundManager.Instance != null && Settings.warningLoopSound != null)
            warningLoop = SoundManager.Instance.PlayLoop(Settings.warningLoopSound, transform.position, transform);
    }
    protected override void OnWindup(float elapsed) => controller.Velocity = Vector2.zero;
    protected override void OnExecuteStart()
    {
        StopWarning(); Enter(GrabPhase.Extend);
        Vector3 delta = player != null ? player.model.transform.position - visual.CorePosition : Vector3.right * controller.Facing;
        if (delta.sqrMagnitude < .001f) delta = Vector3.right * controller.Facing;
        armTarget = visual.CorePosition + Vector3.ClampMagnitude(delta, Settings.grabRadius);
        if (player == null) return;
        var filter = new ContactFilter2D(); filter.SetLayerMask(1 << LayerMask.NameToLayer("Player")); filter.useTriggers = true;
        controller.Motor.OverlapAttackCircle(visual.CorePosition, Settings.grabRadius, filter, hits);
        bool inRange = false;
        foreach (var hit in hits) if (hit.GetComponentInParent<PlayerController>() == player) { inRange = true; break; }
        if (!inRange) return;
        controller.FacingUpdate(delta.x>=0 ? 1 : -1);
        if(!StomperGrabLandingPlan.TryCreate(controller,player,visual.CorePosition,Settings.impactContactAngle,Settings.grabRadius,out landing)) return;
        grab = player.AbilityManager.Grab;
        if (grab == null || !grab.TryBegin(this, Settings.playerGrab, out session)) return;
        contactAngle=Settings.ContactAngle(landing.ContactDegrees);
        Captured = true; bossProtection=controller.Health.AcquireInvulnerability(this); playerModelOffset = player.model.transform.position - player.transform.position;
        playerBody = player.GetComponent<Collider2D>();
        controller.FacingUpdate(delta.x >= 0 ? 1 : -1);
        armTarget = visual.CorePosition + Vector3.right * controller.Facing * Settings.grabRadius;
        visual.PoseArm(armTarget, 1); visual.SetPreview(false);
        grab.SetVelocity(this, session, CaptureVelocity());
        player.OnLanded += PlayerLanded;
        feedbackPlayer.Play(Settings.gripFeedback, player.transform);
        presentation = grab.Presentation;
        presentation.ConfigureCameraShots(Settings);
        presentation.BeginOrbitView(controller.model.transform, player.model.transform, visual.CoreTransform,
            Settings.grabRadius, Settings.blackoutDuration, Settings.spinTrailPrefab, Settings.motionLineMaterial, Settings.recoilClip, Vector3.right * controller.Facing,
            Settings.trainingBackdrop, Settings.travelBackdropMaterial, Settings.roomExtensionMaterial, Settings.rocketThrusterPrefab, Settings.riseClip,
            landing.ContactDegrees, Settings.flightMotionScale, Settings.cameraLeadDistance);
        float playerHeight=Mathf.Min(landing.RoomBounds.max.y-1,Mathf.Max(player.transform.position.y,landing.PlayerRoot.y+3));
        float bossHeight=Mathf.Min(landing.RoomBounds.max.y-1.5f,Mathf.Max(transform.position.y,landing.BossRoot.y)+1.5f);
        playerTransit=new Vector2(landing.PlayerRoot.x,playerHeight); bossTransit=new Vector2(landing.BossRoot.x,bossHeight);
        Enter(GrabPhase.Blackout);
    }
    protected override void OnExecute(float elapsed)
    {
        if (Captured && (grab == null || !grab.IsOwnedBy(this, session))) { StopMotionSounds(); CurrentPhase = GrabPhase.Finished; return; }
        float t = Time.time - phaseAt;
        controller.Velocity = Vector2.zero;
        switch (CurrentPhase)
        {
            case GrabPhase.Extend:
                visual.PoseArm(armTarget, Mathf.Clamp01(t / Settings.extendDuration));
                if (t >= Settings.extendDuration) CurrentPhase = GrabPhase.Finished;
                break;
            case GrabPhase.Blackout:
                if (t >= Settings.blackoutDuration)
                {
                    Enter(GrabPhase.Rise); visual.TrackTarget(player.model.transform); flightAt=Time.time; presentation.BeginRise(Settings.fakeRiseDuration+Settings.verticalDecelerationDuration,Settings.fakeRiseDuration); StartRocket();
                }
                break;
            case GrabPhase.Rise:
                UpdateFlight(Time.time-flightAt);
                if(t>=Settings.fakeRiseDuration)
                { presentation.DetachThrusters(); rocketLoop?.Stop(); rocketLoop=null; Enter(GrabPhase.Spin); }
                break;
            case GrabPhase.Spin:
                angle=Settings.EvaluateSpinAngle(t); presentation.SetOrbitAngle(angle); TickWind();
                UpdateFlight(Time.time-flightAt);
                if(t>=Settings.spinDuration)
                { contactAngle=Settings.ContactAngle(landing.ContactDegrees); descentAt=Time.time; Enter(GrabPhase.Descent); }
                break;
            case GrabPhase.Descent:
            case GrabPhase.ImpactFrame:
            case GrabPhase.Slam:
                float descent = Time.time - descentAt; if(!UpdateDescent(descent)) break;
                GrabPhase phase = descent >= Settings.ImpactFrameEnd(landing.ContactDegrees) ? GrabPhase.Slam
                    : descent >= Settings.ImpactFrameStart(landing.ContactDegrees) ? GrabPhase.ImpactFrame : GrabPhase.Descent;
                if (phase != CurrentPhase)
                {
                    Enter(phase);
                    // 불투명한 임팩트 화면 뒤에서 바를 정리해 게임 화면 복귀 때 다시 보이지 않게 한다.
                    if(phase==GrabPhase.ImpactFrame) grab.HideLetterbox(this,session,.001f);
                }
                bool frameVisible=phase==GrabPhase.ImpactFrame;
                presentation.SetImpactFrame(frameVisible,Mathf.InverseLerp(Settings.ImpactFrameStart(landing.ContactDegrees),Settings.ImpactFrameEnd(landing.ContactDegrees),descent));
                if(phase==GrabPhase.Slam && !gameplayCut)
                {
                    gameplayCut=true;
                    presentation.CutToGameplay();
                    grab.HideLetterbox(this,session,.001f);
                }
                visualImpactReady = descent >= Settings.DescentTime(landing.ContactDegrees) && angle >= contactAngle - .001f;
                if (visualImpactReady && HasPlayerFloorContact()) PlayerLanded();
                if (CurrentPhase == GrabPhase.Slam && Time.time-phaseAt >= Settings.travelTimeout && ImpactCount == 0) CurrentPhase = GrabPhase.Finished;
                break;
            case GrabPhase.Impact:
                if (t >= Settings.impactHoldDuration)
                {
                    presentation.ReturnToGameplay(Settings.fadeOutDuration); Enter(GrabPhase.Fade);
                }
                break;
            case GrabPhase.Fade:
                if (t >= Settings.fadeOutDuration)
                {
                    ReleasePlayer(); CurrentPhase = GrabPhase.Finished;
                }
                break;
        }
    }
    private void FixedUpdate()
    {
        if (State != AbilityState.Execute || brain.IsPaused || !Captured || grab == null || !grab.IsOwnedBy(this, session)) return;
        controller.Velocity = Vector2.zero;
        if(CurrentPhase==GrabPhase.Blackout)
        { grab.SetVelocity(this,session,CaptureVelocity()); return; }
        controller.Velocity=MoveToLanding(controller,landing.BossRoot,bossTransit,controller.GetComponent<Collider2D>());
        Vector2 velocity=MoveToLanding(player,landing.PlayerRoot,playerTransit,playerBody);
        if(handoffStarted && Mathf.Abs(player.transform.position.x-landing.PlayerRoot.x)<.08f) velocity=new Vector2(0,-2);
        if(CurrentPhase==GrabPhase.Slam) velocity.y=-Settings.slamSpeed;
        if(CurrentPhase==GrabPhase.Impact || CurrentPhase==GrabPhase.Fade || CurrentPhase==GrabPhase.Finished) velocity=Vector2.zero;
        grab.SetVelocity(this,session,velocity);
    }
    private Vector2 MoveToLanding(CharacterController2D actor,Vector2 target,Vector2 transit,Collider2D body)
    {
        Vector2 current=actor.transform.position;
        Vector2 next=target;
        if(Mathf.Abs(current.x-target.x)>.08f)
            next=current.y<transit.y-.08f ? new Vector2(current.x,transit.y) : transit;
        float step=Mathf.Max(.02f,Mathf.Min(body.bounds.extents.x,body.bounds.extents.y)*.5f);
        return Vector2.ClampMagnitude(next-current,step)/Time.fixedDeltaTime;
    }
    private Vector2 CaptureVelocity()
    {
        Vector2 remaining=armTarget-playerModelOffset-player.transform.position;
        float step=Mathf.Max(.02f,Mathf.Min(playerBody.bounds.extents.x,playerBody.bounds.extents.y)*.5f);
        return Vector2.ClampMagnitude(remaining,step)/Time.fixedDeltaTime;
    }
    private void UpdateFlight(float elapsed)
    {
        float speed=Settings.EvaluateFlightScroll(elapsed);
        float upDuration=Settings.fakeRiseDuration+Settings.verticalDecelerationDuration;
        bool upward=elapsed<upDuration;
        bool rocketFiring=CurrentPhase==GrabPhase.Rise;
        presentation.SetTravel(speed,Mathf.Clamp01(elapsed/upDuration),upward,rocketFiring);
        if(!upward) { UpdateVisualAltitude(); TryBeginDescentView(); }
        if(RocketSoundIsPlaying)
        {
            var source=rocketLoop.GetSource();
            float flow=Mathf.Clamp01(-speed/Settings.riseScrollSpeed);
            source.pitch=Mathf.Lerp(.8f,1.15f,flow); source.volume=Settings.rocketLoopSound.volume*flow;
        }
        if(!rocketFiring) { rocketLoop?.Stop(); rocketLoop=null; }
    }
    private void TickWind()
    {
        if (angle < nextWindAngle) return;
        // 저속 프레임에서 지나친 임계값은 소비하되 같은 프레임 소리 폭주는 막는다.
        nextWindAngle = (Mathf.Floor(angle / 360) + 1) * 360;
        feedbackPlayer.Play(Settings.spinWindFeedback, controller.transform); WindCueCount++;
    }
    private bool UpdateDescent(float elapsed)
    {
        angle = Mathf.Min(contactAngle, Settings.EvaluateDescentAngle(elapsed,landing.ContactDegrees));
        presentation.SetOrbitAngle(angle); TickWind();
        UpdateVisualAltitude();
        if(!TryBeginDescentView())
        {
            if(handoffWaitAt<0) handoffWaitAt=Time.unscaledTime;
            presentation.SetTravel(Settings.descentScrollSpeed,.9f,false);
            if(Time.unscaledTime-handoffWaitAt>Settings.travelTimeout) CurrentPhase=GrabPhase.Finished;
            descentAt+=Time.deltaTime; return false;
        }
        presentation.SetTravel(Settings.descentScrollSpeed, 1 - Mathf.Clamp01(elapsed / Settings.DescentTime(landing.ContactDegrees)), false);
        return true;
    }
    private void UpdateVisualAltitude()
    {
        float beforeImpact=Mathf.Max(0,contactAngle-angle)/Mathf.Max(.1f,Settings.maxAngularSpeed);
        presentation.SetGameplayAltitude(beforeImpact*Settings.descentVisualSpeed);
        presentation.SetImpactFrameCountdown(beforeImpact-(Settings.DescentTime(landing.ContactDegrees)-Settings.ImpactFrameStart(landing.ContactDegrees)));
    }
    private bool TryBeginDescentView()
    {
        if(handoffStarted) return true;
        if(Vector2.Distance(player.transform.position,landing.PlayerRoot)>=.2f || Vector2.Distance(transform.position,landing.BossRoot)>=.2f) return false;
        float beforeCut=Settings.ImpactFrameEnd(landing.ContactDegrees)+(CurrentPhase==GrabPhase.Spin
            ? Mathf.Max(0,Settings.spinDuration-(Time.time-phaseAt)) : -(Time.time-descentAt));
        float duration=Mathf.Min(Settings.gameplayHandoffDuration,Mathf.Max(.01f,beforeCut-.02f));
        handoffStarted=true;
        presentation.BeginGameplayHandoff(landing.Core,landing.Floor,landing.Radius,landing.RoomBounds,duration);
        return true;
    }
    private void StartRocket()
    {
        if (SoundManager.Instance == null) return;
        SoundManager.Instance.PlaySound(Settings.rocketIgnitionSound,transform.position);
        rocketLoop = SoundManager.Instance.PlayLoop(Settings.rocketLoopSound,transform.position,transform);
    }
    private void StopMotionSounds() { rocketLoop?.Stop(); rocketLoop = null; }
    private bool HasPlayerFloorContact()
    {
        if (!player.IsGrounded) return false;
        var floor = player.Motor.RaycastEnvironment(playerBody.bounds.center,Vector2.down,playerBody.bounds.extents.y+.25f);
        if (floor.collider == null) return false;
        platformContact = floor; return true;
    }
    private void PlayerLanded()
    {
        if (State != AbilityState.Execute || CurrentPhase != GrabPhase.Slam || !Captured || !visualImpactReady || ImpactCount > 0
            || grab == null || !grab.IsOwnedBy(this, session) || !HasPlayerFloorContact()) return;
        ImpactCount = 1; ImpactPosition = new Vector3(landing.Floor.x,platformContact.point.y,player.transform.position.z);
        grab.SetVelocity(this, session, Vector2.zero);
        presentation.ShowImpact(Settings.impactFeedback);
        feedbackPlayer.Play(Settings.impactFeedback, ImpactPosition);
        if (SoundManager.Instance != null) SoundManager.Instance.PlaySound(Settings.impactExplosionSound, ImpactPosition);
        AppliedDamage = grab.ApplyDamage(this, session, Settings.damage, ImpactPosition);
        Enter(GrabPhase.Impact);
    }
    private void Enter(GrabPhase phase) { CurrentPhase = phase; phaseAt = Time.time; }
    protected override bool CanFinishExecute(float elapsed) => CurrentPhase == GrabPhase.Finished;
    protected override void OnRecoverStart()
    {
        StopMotionSounds(); armTarget=player!=null ? player.model.transform.position : armTarget; visual.TrackTarget(null); ReleasePlayer(); visual.SetPreview(false);
        controller.Velocity = Vector2.zero; controller.GravityOn = gravity;
    }
    protected override void OnRecover(float elapsed) => visual.PoseArm(armTarget, 1 - Mathf.Clamp01(elapsed / data.recoverTime));
    private void ReleasePlayer()
    {
        if (player != null) player.OnLanded -= PlayerLanded;
        if (grab != null) grab.Release(this, session);
    }
    private void StopWarning() { warningLoop?.Stop(); warningLoop = null; }
    protected override void OnStop() => Cleanup();
    private void OnDisable() => Cleanup();
    private void Cleanup()
    {
        StopWarning(); StopMotionSounds(); bossProtection?.Dispose(); bossProtection=null;
        if (!ownsMovement) return; ownsMovement = false;
        ReleasePlayer(); if (visual != null) visual.End();
        controller.GravityOn = gravity; controller.IgnoreExternalForces = ignoreExternal;
        controller.Velocity = controller.externalForce = Vector2.zero;
        CurrentPhase = GrabPhase.None;
    }
}
