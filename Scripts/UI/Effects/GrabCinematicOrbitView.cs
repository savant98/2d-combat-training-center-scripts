using UnityEngine;

// 표시 배우와 배경만 움직이며 실제 캐릭터·모터에는 접근하지 않는다.
public sealed class GrabCinematicOrbitView
{
    private readonly Transform stage, bossRig, bossModel, core, playerRig, playerModel, head, port, stem;
    private readonly Transform recoilRoot, recoilBoss, recoilArm, recoilPlayer;
    private readonly AnimationClip recoilClip;
    private readonly Vector3 origin, direction;
    private float radius;
    private float flightTravel, worldMix, worldAltitude, flightScale=.0025f, targetFootX;
    private Vector3 contactShift;
    public float Radius => radius;
    public float FlightTravel => flightTravel;
    public Vector3 FlightOffset => Vector3.up*(flightTravel*(1-worldMix)+worldAltitude*worldMix);
    public void SetRadius(float value) => radius=value;
    public void SetFlightScale(float value) => flightScale=value;
    public void SetGameplayContact(float weight,float floorY,float footX,float altitude)
    { worldMix=weight; worldAltitude=altitude; PlatformY=floorY; targetFootX=footX; platformPrepared=true; }
    private float OrbitSign => direction.x < 0 ? 1 : -1;
    private readonly LineRenderer[] streaks;
    private readonly float[] streakX, streakY, streakLength;
    private readonly TrailRenderer trail;
    private Vector3 trailOrigin;
    private Vector3[] trailPositions = new Vector3[128];
    private GrabCinematicThrusterView thrusters;
    private readonly Transform risePose;
    private readonly SpriteRenderer playerCircle;
    private AnimationClip riseClip;
    private float riseProgress = 1, contactDegrees = 25;
    private bool rising, contactLocked, platformPrepared;
    public bool RocketIsEmitting => thrusters != null && thrusters.IsEmitting;
    public float ThrusterDeployment => thrusters != null ? thrusters.Deployment : 0;
    public int RocketParticleCount => thrusters != null ? thrusters.ParticleCount : 0;
    public bool ThrustersDetached => thrusters != null && thrusters.IsDetached;
    public int DetachedThrusterCount => thrusters != null ? thrusters.DetachedCount : 0;
    public float ThrusterDrop => thrusters != null ? thrusters.MaximumDrop : 0;
    public float ThrusterMountGap => thrusters != null ? thrusters.AttachedGap : 0;
    public void DetachThrusters() => thrusters?.Detach();
    public float RiseShake => risePose.localPosition.magnitude + Mathf.Abs(Mathf.DeltaAngle(0,risePose.localEulerAngles.z));
    public float PlatformY { get; private set; }
    public bool PlatformIsVisible => worldMix>.999f;
    public Vector3 PlayerFootPosition
    {
        get
        {
            if (playerCircle == null || playerCircle.sprite == null) return playerModel.position + Vector3.down*.75f;
            var bounds = playerCircle.sprite.bounds;
            Vector3 x = playerCircle.transform.TransformVector(Vector3.right*bounds.extents.x);
            Vector3 y = playerCircle.transform.TransformVector(Vector3.up*bounds.extents.y);
            float support = Mathf.Max(.001f,Mathf.Sqrt(x.y*x.y+y.y*y.y));
            return playerCircle.transform.TransformPoint(bounds.center) - (x*x.y+y*y.y)/support;
        }
    }
    public void ConfigureTravel(GameObject thrusterPrefab, AnimationClip clip, float contactAngle)
    {
        thrusters = new GrabCinematicThrusterView(bossModel,thrusterPrefab); riseClip = clip; contactDegrees = contactAngle;
        PlatformY = origin.y - radius*Mathf.Sin(contactAngle*Mathf.Deg2Rad)-.75f;
    }
    public void SetRise(float progress, bool active, bool firing) { riseProgress = progress; rising = active; thrusters?.SetFiring(firing); }
    public void ShowPlatform(bool visible)
    {
        if (!visible || platformPrepared || playerCircle == null || playerCircle.sprite == null) return;
        // 충돌 전 중립 몸체를 기준으로 평면을 고정해 squash가 바닥을 움직이지 않게 한다.
        var bounds=playerCircle.sprite.bounds;
        var rotation=Quaternion.Euler(0,0,OrbitSign*(contactDegrees-angle));
        Vector3 x=rotation*playerCircle.transform.TransformVector(Vector3.right*bounds.extents.x);
        Vector3 y=rotation*playerCircle.transform.TransformVector(Vector3.up*bounds.extents.y);
        Vector3 offset=rotation*(playerCircle.transform.TransformPoint(bounds.center)-playerModel.position);
        PlatformY=origin.y+OrbitOffset(contactDegrees).y+offset.y-Mathf.Sqrt(x.y*x.y+y.y*y.y);
        platformPrepared=true;
    }
    private float angle, scroll, motionSpeed, impactAt = -100, shakeDuration, shakeStrength;
    public Vector3 PlayerPosition { get; private set; }
    public Vector3 CorePosition => core.position;
    public Vector3 HandPosition => port != null ? port.position : core.position;
    public float MotionSpeed => motionSpeed;
    public float OrbitAngle => angle;
    public bool TrailIsEmitting => trail != null && trail.emitting;
    public int TrailPointCount => trail != null ? trail.positionCount : 0;
    public int ImpactEffectCount { get; private set; }
    public Vector3 ImpactPosition { get; private set; }
    public Vector3 ShakeOffset { get; private set; }
    public float RecoilAmount { get; private set; }
    public float GroundCenterY => PlatformY;
    public GrabCinematicOrbitView(Transform stage, Transform bossRig, Transform bossModel, Transform core,
        Transform playerRig, Transform playerModel, Vector3 direction, float radius, float aspect,
        TrailRenderer trailPrefab, Material motionMaterial, AnimationClip recoilClip)
    {
        this.stage = stage; this.bossRig = bossRig; this.bossModel = bossModel; this.core = core;
        this.playerRig = playerRig; this.playerModel = playerModel; this.radius = radius; this.recoilClip = recoilClip;
        origin = stage.position;
        risePose = new GameObject("GrabRisePose").transform; risePose.SetParent(stage,false);
        playerCircle = playerModel.Find("Circle")?.GetComponent<SpriteRenderer>(); this.direction = new Vector3(direction.x < 0 ? -1 : 1, 0, 0);
        head = bossModel.Find("MineLauncher"); port = head != null ? head.Find("Port") : null; stem = bossModel.Find("MineLauncherStem");
        recoilRoot = new GameObject("GrabRecoilPose").transform; recoilRoot.SetParent(stage, false);
        recoilBoss = RecoilNode("Boss"); recoilArm = RecoilNode("Arm"); recoilPlayer = RecoilNode("Player");
        if (trailPrefab != null)
        {
            trail = Object.Instantiate(trailPrefab, stage); trail.name = "GrabHandRedTrail";
            SetLayer(trail.transform, LayerMask.NameToLayer("UI")); trail.emitting = false; trail.Clear();
            trailOrigin = core.position;
        }
        streaks = new LineRenderer[28]; streakX = new float[28]; streakY = new float[28]; streakLength = new float[28];
        for (int i = 0; i < streaks.Length; i++)
        {
            var line = new GameObject("VerticalMotionLine" + i).AddComponent<LineRenderer>();
            line.transform.SetParent(stage, false); line.gameObject.layer = LayerMask.NameToLayer("UI");
            line.sharedMaterial = motionMaterial; line.useWorldSpace = false; line.positionCount = 2;
            line.widthMultiplier = .018f + i % 3 * .009f; line.sortingOrder = -1000;
            line.startColor = new Color(.3f, .25f, .28f, .45f); line.endColor = new Color(.3f, .25f, .28f, 0);
            line.enabled = false; streaks[i] = line;
            streakY[i] = Mathf.Repeat(i * 7.73f, (radius + 5) * 2); streakLength[i] = 2.5f + i % 5 * 1.15f;
        }
        SetAspect(aspect);
    }
    private Transform RecoilNode(string name)
    { var node = new GameObject(name).transform; node.SetParent(recoilRoot, false); return node; }
    public void SetAspect(float aspect)
    {
        float width = (radius + 3) * aspect;
        for (int i = 0; i < streakX.Length; i++) streakX[i] = Mathf.Lerp(-width, width, Mathf.Repeat(i * .618034f, 1));
    }
    public void SetVerticalMotion(float speed) => motionSpeed = speed;
    public void SetAngle(float degrees) { angle = degrees; if (trail != null && impactAt < 0) trail.emitting = true; }
    public float NextGroundContactAngle(float current)
    {
        return contactDegrees + 360 * Mathf.Ceil((current - contactDegrees + .001f) / 360);
    }
    private Vector3 OrbitOffset(float degrees, float lengthScale = 1)
    {
        float radians = (OrbitSign * degrees + (direction.x < 0 ? 180 : 0)) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0) * (radius * lengthScale);
    }
    public void UpdatePose(float deltaTime, float weight = 1)
    {
        thrusters?.Update(deltaTime);
        if (riseClip != null) riseClip.SampleAnimation(risePose.gameObject, (rising ? riseProgress : 1)*riseClip.length);
        float flightStrength=Mathf.Clamp01(-motionSpeed/180);
        risePose.localPosition*=flightStrength; risePose.localRotation=Quaternion.Slerp(Quaternion.identity,risePose.localRotation,flightStrength);
        flightTravel-=motionSpeed*deltaTime*flightScale;
        Vector3 center = origin + FlightOffset + risePose.localPosition + contactShift;
        float riseTilt = Mathf.DeltaAngle(0,risePose.localEulerAngles.z);
        if (recoilClip != null && impactAt > 0) recoilClip.SampleAnimation(recoilRoot.gameObject, Mathf.Min(Time.unscaledTime - impactAt, recoilClip.length));
        RecoilAmount = Mathf.Abs(Mathf.DeltaAngle(0, recoilArm.localEulerAngles.z)) + Mathf.Abs(1 - recoilPlayer.localScale.y);
        bossModel.localScale = Vector3.Scale(bossModel.localScale, Vector3.Lerp(Vector3.one, recoilBoss.localScale, weight));
        bossRig.rotation = Quaternion.Euler(0, 0, riseTilt + OrbitSign * (angle + Mathf.DeltaAngle(0, recoilBoss.localEulerAngles.z)));
        bossRig.position += center + new Vector3(recoilBoss.localPosition.x*direction.x,recoilBoss.localPosition.y,recoilBoss.localPosition.z) - core.position;
        // 반동은 관절 각도로 표현하고 포획팔의 물리적인 길이는 바꾸지 않는다.
        Vector3 endpoint = core.position + Quaternion.Euler(0,0,riseTilt)*OrbitOffset(angle + Mathf.DeltaAngle(0, recoilArm.localEulerAngles.z));
        playerRig.rotation = Quaternion.Euler(0, 0, riseTilt + OrbitSign * (angle + Mathf.DeltaAngle(0, recoilPlayer.localEulerAngles.z)));
        playerRig.position += endpoint - playerModel.position;
        playerModel.localScale = Vector3.Scale(playerModel.localScale, Vector3.Lerp(Vector3.one, recoilPlayer.localScale, weight));
        if (contactLocked)
        {
            float lift = Mathf.Max(0,PlatformY-PlayerFootPosition.y);
            bossRig.position += Vector3.up*lift; playerRig.position += Vector3.up*lift; endpoint += Vector3.up*lift;
        }
        PoseArm(endpoint);
        bossRig.localPosition = Vector3.Lerp(Vector3.zero, bossRig.localPosition, weight);
        bossRig.localRotation = Quaternion.Slerp(Quaternion.identity, bossRig.localRotation, weight);
        playerRig.localPosition = Vector3.Lerp(Vector3.zero, playerRig.localPosition, weight);
        playerRig.localRotation = Quaternion.Slerp(Quaternion.identity, playerRig.localRotation, weight);
        Vector3 held=playerModel.position-core.position;
        if(held.sqrMagnitude>.001f) playerRig.position+=core.position+held.normalized*radius-playerModel.position;
        PoseArm(playerModel.position);
        PlayerPosition = playerModel.position;
        UpdateTrail();
        scroll += motionSpeed * deltaTime;
        float height = (radius + 5) * 2;
        for (int i = 0; i < streaks.Length; i++)
        {
            var line = streaks[i]; line.enabled = Mathf.Abs(motionSpeed) > .01f;
            float y = Mathf.Repeat(streakY[i] + scroll, height) - height * .5f;
            line.SetPosition(0, new Vector3(streakX[i], y, 3)); line.SetPosition(1, new Vector3(streakX[i], y + streakLength[i], 3));
        }
        float shake = Mathf.Clamp01(1 - (Time.unscaledTime - impactAt) / Mathf.Max(.001f, shakeDuration));
        ShakeOffset = new Vector3(Mathf.Sin(Time.unscaledTime * 113), Mathf.Cos(Time.unscaledTime * 137)) * (shakeStrength * shake);
    }
    private void UpdateTrail()
    {
        if (trail == null) return;
        Vector3 delta = core.position - trailOrigin;
        if (trail.positionCount > 0 && delta.sqrMagnitude > 0)
        {
            if (trailPositions.Length < trail.positionCount)
                trailPositions = new Vector3[Mathf.NextPowerOfTwo(trail.positionCount)];
            int count = trail.GetPositions(trailPositions);
            // 코어의 이동만 과거 점에 더해 회전 궤적이 지역좌표의 원을 유지하게 한다.
            for (int i = 0; i < count; i++) trailPositions[i] += delta;
            trail.SetPositions(trailPositions);
        }
        trailOrigin = core.position;
        trail.transform.position = PlayerPosition;
    }
    private void PoseArm(Vector3 target)
    {
        if (head == null || port == null || stem == null) return;
        Vector3 a = bossModel.InverseTransformPoint(core.position), b = bossModel.InverseTransformPoint(target), delta = b - a;
        float rotation = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90;
        b -= Quaternion.Euler(0, 0, rotation) * (port.localPosition * .32f); delta = b - a;
        stem.localPosition = (a + b) * .5f; stem.localRotation = Quaternion.Euler(0, 0, rotation);
        stem.localScale = new Vector3(.22f, new Vector2(delta.x, delta.y).magnitude, 1);
        head.localPosition = b; head.localRotation = Quaternion.Euler(0, 0, rotation); head.localScale = Vector3.one * .32f;
        head.gameObject.SetActive(true); stem.gameObject.SetActive(true);
    }
    public void ShowImpact(FeedbackDataSO feedback)
    {
        motionSpeed = 0; if (trail != null) trail.emitting = false;
        ShowPlatform(true); impactAt = Time.unscaledTime; UpdatePose(0);
        contactShift=new Vector3(worldMix>.999f ? targetFootX-PlayerFootPosition.x : 0,PlatformY-PlayerFootPosition.y,0); bossRig.position+=contactShift; playerRig.position+=contactShift;
        PlayerPosition=playerModel.position; PoseArm(PlayerPosition); contactLocked=true;
        UpdateTrail();
        ImpactPosition=PlayerFootPosition;
        shakeDuration = feedback != null ? feedback.shakeDuration : .28f; shakeStrength = feedback != null ? feedback.shakeIntensity * .16f : .2f;
        if (feedback == null || feedback.particlePrefabs == null) return;
        foreach (var prefab in feedback.particlePrefabs)
        {
            if (prefab == null) continue;
            var effect = Object.Instantiate(prefab, ImpactPosition, Quaternion.identity, stage);
            effect.name = "GrabImpact_" + prefab.name; SetLayer(effect.transform, LayerMask.NameToLayer("UI"));
            foreach (var renderer in effect.GetComponentsInChildren<ParticleSystemRenderer>(true)) renderer.sortingOrder += 100;
            foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true)) particle.Play();
            ImpactEffectCount++;
        }
    }
    private static void SetLayer(Transform target, int layer)
    { target.gameObject.layer = layer; foreach (Transform child in target) SetLayer(child, layer); }
}
