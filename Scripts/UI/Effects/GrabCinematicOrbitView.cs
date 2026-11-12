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
    // 회전 궤적: 지난 arcDuration초 동안 손끝이 지나간 호 전체를 Enemy/SwingArc 셰이더로 한 번에 그린다.
    // 프레임마다 점을 찍지 않으므로 프레임률과 무관하게 같은 원호이고, 코어를 중심에 두어 상승·하강 중에도 코어 상대 원형을 유지한다.
    private static readonly int ArcHeadId = Shader.PropertyToID("_Head"), ArcTailId = Shader.PropertyToID("_Tail"), ArcAlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int ArcQuadSizeId = Shader.PropertyToID("_QuadSize"), ArcOuterId = Shader.PropertyToID("_Outer");
    private static readonly int ArcHeadWidthId = Shader.PropertyToID("_HeadWidth"), ArcGlowWidthId = Shader.PropertyToID("_GlowWidth"), ArcSymmetricId = Shader.PropertyToID("_Symmetric");
    private static readonly int ArcGlowGainId = Shader.PropertyToID("_GlowGain"), ArcTipScaleId = Shader.PropertyToID("_TipScale"), ArcRimWidthId = Shader.PropertyToID("_RimWidth");
    private readonly SpriteRenderer arc;
    private readonly MaterialPropertyBlock arcProperties;
    private readonly float arcDuration, arcOuterOffset, arcMargin, arcSymmetric, arcLineWidth;
    private bool arcOutlineOnly;
    // 흑백 임팩트 화면은 밝은 곳을 모두 검은 실루엣으로 바꾼다. 넓은 궤적이 Player 실루엣을 덮지 않게 그동안은 가는 선만 그린다.
    public void SetArcOutlineOnly(bool value) => arcOutlineOnly = value;
    private readonly float[] arcTimes = new float[256], arcAngles = new float[256];
    private int arcFirst, arcCount;
    private bool arcStarted, arcFrozen;
    private Vector3 arcDirection = Vector3.right;
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
    public bool ArcIsVisible => arc != null && arc.enabled;
    public float ArcSweep { get; private set; }
    public float ArcDuration => arcDuration;
    public SpriteRenderer ArcRenderer => arc;
    public int ImpactEffectCount { get; private set; }
    public Vector3 ImpactPosition { get; private set; }
    public Vector3 ShakeOffset { get; private set; }
    public float RecoilAmount { get; private set; }
    public float GroundCenterY => PlatformY;
    public GrabCinematicOrbitView(Transform stage, Transform bossRig, Transform bossModel, Transform core,
        Transform playerRig, Transform playerModel, Vector3 direction, float radius, float aspect,
        SpriteRenderer arcPrefab, float arcDuration, Material motionMaterial, AnimationClip recoilClip)
    {
        this.stage = stage; this.bossRig = bossRig; this.bossModel = bossModel; this.core = core;
        this.playerRig = playerRig; this.playerModel = playerModel; this.radius = radius; this.recoilClip = recoilClip;
        origin = stage.position;
        risePose = new GameObject("GrabRisePose").transform; risePose.SetParent(stage,false);
        playerCircle = playerModel.Find("Circle")?.GetComponent<SpriteRenderer>(); this.direction = new Vector3(direction.x < 0 ? -1 : 1, 0, 0);
        head = bossModel.Find("MineLauncher"); port = head != null ? head.Find("Port") : null; stem = bossModel.Find("MineLauncherStem");
        recoilRoot = new GameObject("GrabRecoilPose").transform; recoilRoot.SetParent(stage, false);
        recoilBoss = RecoilNode("Boss"); recoilArm = RecoilNode("Arm"); recoilPlayer = RecoilNode("Player");
        this.arcDuration = Mathf.Max(.02f, arcDuration);
        if (arcPrefab != null)
        {
            arc = Object.Instantiate(arcPrefab, stage); arc.name = "GrabHandSwingArc";
            SetLayer(arc.transform, LayerMask.NameToLayer("UI")); arc.enabled = false;
            arcProperties = new MaterialPropertyBlock();
            // 손끝이 띠의 가운데 선(대칭형) 또는 바깥 테두리를 지나도록 반지름을 정하고, 사각형은 머리 폭과 빛 번짐까지 덮는다.
            var look = arc.sharedMaterial;
            float headWidth = look != null ? look.GetFloat(ArcHeadWidthId) : 1;
            arcSymmetric = look != null ? look.GetFloat(ArcSymmetricId) : 1;
            arcOuterOffset = headWidth * .5f * arcSymmetric;
            arcMargin = arcOuterOffset + (look != null ? look.GetFloat(ArcGlowWidthId) * 6 : 1) + .1f;
            arcLineWidth = look != null ? look.GetFloat(ArcRimWidthId) * 1.5f : .2f;
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
    public void SetAngle(float degrees) { angle = degrees; arcStarted = true; }
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
        UpdateArc();
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
    private void UpdateArc()
    {
        if (arc == null) return;
        float now = Time.time, trailing = angle;
        if (arcStarted) { RecordArcAngle(now, angle); trailing = SampleArcAngle(now - arcDuration); }
        // 꼬리는 arcDuration초 전의 각도다. 회전이 멈추면(충돌·대기) 꼬리가 머리를 따라잡아 같은 시간 뒤에 사라진다.
        ArcSweep = angle - trailing;
        // 충돌 전에는 실제 손끝 방향을 따른다. 충돌 뒤 남은 잔상은 반동에 흔들리지 않게 충돌 순간의 방향에 두고 코어의 이동만 따라간다.
        Vector3 hand = PlayerPosition - core.position; hand.z = 0;
        if (!arcFrozen && hand.sqrMagnitude > .0001f) arcDirection = hand;
        if (impactAt > 0) arcFrozen = true;
        bool visible = arcStarted && Mathf.Abs(ArcSweep) >= .5f;
        arc.enabled = visible;
        if (!visible) return;
        float size = (radius + arcMargin) * 2;
        arc.transform.SetPositionAndRotation(core.position, Quaternion.Euler(0, 0, Mathf.Atan2(arcDirection.y, arcDirection.x) * Mathf.Rad2Deg - 90));
        arc.transform.localScale = new Vector3(size, size, 1);
        // 셰이더 각도는 사각형 기준이다(0 = 손끝). 화면에서 도는 방향(OrbitSign)의 반대쪽으로 꼬리를 둔다.
        arcProperties.Clear();
        arcProperties.SetFloat(ArcQuadSizeId, size); arcProperties.SetFloat(ArcOuterId, radius + arcOuterOffset);
        arcProperties.SetFloat(ArcHeadId, 0); arcProperties.SetFloat(ArcTailId, -OrbitSign * ArcSweep); arcProperties.SetFloat(ArcAlphaId, 1);
        // 충돌 뒤 남은 잔상에는 손끝 섬광을 그리지 않는다.
        arcProperties.SetFloat(ArcTipScaleId, arcFrozen || arcOutlineOnly ? 0 : 1);
        if (arcOutlineOnly)
        {
            arcProperties.SetFloat(ArcHeadWidthId, arcLineWidth); arcProperties.SetFloat(ArcGlowGainId, 0);
            arcProperties.SetFloat(ArcOuterId, radius + arcLineWidth * .5f * arcSymmetric);
        }
        arc.SetPropertyBlock(arcProperties);
    }
    private void RecordArcAngle(float time, float degrees)
    {
        int length = arcTimes.Length;
        if (arcCount > 0)
        {
            int last = (arcFirst + arcCount - 1) % length;
            if (time <= arcTimes[last]) { arcAngles[last] = degrees; return; }   // 같은 프레임의 두 번째 갱신
        }
        // arcDuration보다 오래된 기록은 그 경계를 보간할 하나만 남긴다.
        while (arcCount > 1 && arcTimes[(arcFirst + 1) % length] <= time - arcDuration) { arcFirst = (arcFirst + 1) % length; arcCount--; }
        if (arcCount == length) { arcFirst = (arcFirst + 1) % length; arcCount--; }
        int index = (arcFirst + arcCount) % length;
        arcTimes[index] = time; arcAngles[index] = degrees; arcCount++;
    }
    private float SampleArcAngle(float time)
    {
        if (arcCount == 0) return angle;
        int length = arcTimes.Length;
        // 궤적을 켜기 전의 시각은 켠 순간의 각도로 본다(켠 뒤 지나간 호만 그린다).
        if (time <= arcTimes[arcFirst]) return arcAngles[arcFirst];
        for (int i = 1; i < arcCount; i++)
        {
            int next = (arcFirst + i) % length;
            if (time > arcTimes[next]) continue;
            int previous = (arcFirst + i - 1) % length;
            return Mathf.Lerp(arcAngles[previous], arcAngles[next], Mathf.InverseLerp(arcTimes[previous], arcTimes[next], time));
        }
        return arcAngles[(arcFirst + arcCount - 1) % length];
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
        motionSpeed = 0;
        ShowPlatform(true); impactAt = Time.unscaledTime; UpdatePose(0);
        contactShift=new Vector3(worldMix>.999f ? targetFootX-PlayerFootPosition.x : 0,PlatformY-PlayerFootPosition.y,0); bossRig.position+=contactShift; playerRig.position+=contactShift;
        PlayerPosition=playerModel.position; PoseArm(PlayerPosition); contactLocked=true;
        UpdateArc();
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
