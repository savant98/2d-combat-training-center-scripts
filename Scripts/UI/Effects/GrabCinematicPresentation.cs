using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Unity.Cinemachine;

// 원래 카메라를 변경하지 않고 표시 Renderer만 배우 전용 무대로 복제한다.
[DefaultExecutionOrder(300)]
public sealed class GrabCinematicPresentation : MonoBehaviour, IUIInputReceiver
{
    public enum Shot { PlayerCloseup, BossCloseup, Duo, DescentLead, ImpactFrame, LandingHold }
    private StomperWrestlingGrabDataSO shotSettings;
    private float viewStartedAt;
    private Vector3 initialViewPosition;
    private Quaternion initialViewRotation;
    private CinemachineBlend cameraBlend;
    private readonly ShotCamera blendSource=new ShotCamera();
    private readonly ShotCamera blendTarget=new ShotCamera();
    private Shot blendedShot;
    private float cameraBlendStartedAt;
    public Shot CameraShot { get; private set; }
    public float CameraBlendWeight => cameraBlend!=null ? cameraBlend.BlendWeight : 0;
    public float CameraBlendDuration => cameraBlend!=null ? cameraBlend.Duration : 0;
    public bool IsCameraBlending => cameraBlend!=null && !cameraBlend.IsComplete;
    public bool LandingViewReady => gameplayCamera!=null && gameplayCamera.IsSettled && WorldMix>.999f;
    public void ConfigureCameraShots(StomperWrestlingGrabDataSO settings) => shotSettings=settings;
    [SerializeField] private UnityEngine.UI.RawImage display;
    [SerializeField] private float transitionTime = .24f;
    [SerializeField] private UnityEngine.UI.Image blackout;
    [SerializeField] private Material actorComposite;
    private readonly Dictionary<Renderer, bool> originals = new Dictionary<Renderer, bool>();
    private readonly Dictionary<Renderer, bool> coverageHidden = new Dictionary<Renderer, bool>();
    private readonly List<Renderer> stageRenderers = new List<Renderer>();
    private Material compositeInstance;
    private RenderTexture coverageTexture;
    private Camera coverageCamera;
    private bool returning, closeRequested;
    private readonly List<SpriteCopy> sprites = new List<SpriteCopy>();
    private readonly List<LineCopy> lines = new List<LineCopy>();
    private readonly List<TransformCopy> transforms = new List<TransformCopy>();
    private ActorCopy firstCopy, secondCopy;
    private GrabCinematicOrbitView orbitView;
    private GrabCinematicBackdropView backdropView;
    public GrabCinematicBackdropView BackdropView => backdropView;
    private UIManager inputOwner;
    private Object owner;
    private bool active, ownsInput, releasing;
    private Camera source, actorCamera;
    private Transform actorA, actorB;
    private GameObject stage;
    private RenderTexture texture;
    private Vector3 reference;
    private readonly Vector3 stageOrigin = new Vector3(10000, 10000, 0);
    private float blend, from, target, changedAt;
    private float activeTransitionTime, zoomDuration, zoomTo;
    private float orbitRadius, orbitAspect, baseViewSize;
    private Vector3 worldCore;
    private Vector2 worldFloor;
    private float returnStartedAt, returnDuration;
    private bool worldHandoff;
    private bool gameplayCut, impactFrameActive;
    private float handoffStartedAt, handoffDuration, impactFrameProgress;
    private Canvas impactCanvas;
    private int originalCanvasOrder;
    private bool raisedImpactCanvas;
    private SpriteRenderer descentBorder;
    private Vector3 framingOffset;
    private float secondsUntilImpactFrame=float.PositiveInfinity;
    private float rocketFramingFrom,rocketFramingTo,rocketFramingChangedAt,rocketFramingDuration=.4f;
    private float rocketRiseStartedAt,rocketRiseEntryDuration,descentFramingSpan=1;
    public float SecondsUntilImpactFrame => secondsUntilImpactFrame;
    public float RocketFramingWeight => Mathf.Lerp(rocketFramingFrom,rocketFramingTo,
        Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-rocketFramingChangedAt)/Mathf.Max(.001f,rocketFramingDuration))));
    private float WorldMix => gameplayCut ? 1 : worldHandoff
        ? Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-handoffStartedAt)/Mathf.Max(.001f,handoffDuration))) : 0;
    private GrabGameplayCameraView gameplayCamera;
    private float gameplayAltitude;
    private float leadDistance=1.1f, travelLead;
    public float CameraLeadOffset => travelLead;
    public GrabGameplayCameraView GameplayCameraView => gameplayCamera;
    public float GameplayAltitude => gameplayAltitude;
    public float GameplayViewSize => gameplayCamera!=null ? gameplayCamera.ViewSize : baseViewSize;
    public GrabHighAltitudeRoomView HighRoomView => null;
    public bool HasGameplayView => worldHandoff && gameplayCut && !impactFrameActive;
    public bool ImpactFrameVisible => impactFrameActive && actorCamera!=null;
    public float ImpactFrameProgress => impactFrameProgress;
    public Vector3 DisplayFootWorld => worldCore+orbitView.PlayerFootPosition-stageOrigin;
    public Vector3 DisplayCoreWorld => worldCore+orbitView.CorePosition-stageOrigin;
    public float Enlargement => actorCamera!=null ? baseViewSize/actorCamera.orthographicSize : 1;
    public bool IsActive => active;
    public float ScreenBlend => blend;
    public float ActorOpacity => display.color.a;
    public RenderTexture CoverageTexture => coverageTexture;
    public RenderTexture ActorColorTexture => texture;
    public int CoverageExcludedCount { get; private set; }
    public int HiddenOriginalCount => originals.Count;
    public int ActorRendererCount => sprites.Count + lines.Count;
    public bool HasActorView => actorCamera != null;
    public float ViewSize => actorCamera != null ? actorCamera.orthographicSize : 0;
    public GrabCinematicOrbitView OrbitView => orbitView;
    public void Acquire(Object requester)
    {
        if (active || releasing) return;
        owner = requester; active = true; blackout.color = Color.clear; activeTransitionTime = transitionTime; display.color = new Color(1, 1, 1, 0);
        inputOwner = UIManager.Instance;
        if (inputOwner != null) { inputOwner.PushGameplayBlock(); inputOwner.PushInputTarget(this, UIInputLayer.System); ownsInput = true; }
    }
    public void BeginActorView(Transform firstModel, Transform secondModel)
    {
        if (!active || actorCamera != null) return;
        source = Camera.main; actorA = firstModel; actorB = secondModel;
        if (source == null || actorA == null || actorB == null) return;
        reference = actorA.position;
        stage = new GameObject("GrabActorStage"); stage.transform.position = stageOrigin;
        actorCamera = new GameObject("GrabActorCamera").AddComponent<Camera>();
        actorCamera.transform.SetParent(stage.transform, false); actorCamera.tag = "Untagged";
        actorCamera.orthographic = true; actorCamera.nearClipPlane = .1f; actorCamera.farClipPlane = 100;
        actorCamera.clearFlags = CameraClearFlags.SolidColor; actorCamera.backgroundColor = Color.black;
        actorCamera.cullingMask = 1 << LayerMask.NameToLayer("UI"); actorCamera.allowHDR = true; actorCamera.allowMSAA = false; actorCamera.depth=source.depth-2;
        var extra = actorCamera.GetUniversalAdditionalCameraData();
        extra.renderPostProcessing = true; extra.renderShadows = false;
        extra.volumeLayerMask = source.GetUniversalAdditionalCameraData().volumeLayerMask;
        firstCopy = CopyActor(actorA); secondCopy = CopyActor(actorB);
        coverageCamera = new GameObject("GrabCoverageCamera").AddComponent<Camera>(); coverageCamera.transform.SetParent(stage.transform, false);
        coverageCamera.tag = "Untagged"; coverageCamera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        compositeInstance = new Material(actorComposite); display.material = compositeInstance;
        ResizeTexture(); foreach (var pair in originals) pair.Key.forceRenderingOff = true;
        CinemachineCore.CameraUpdatedEvent.AddListener(CameraUpdated);
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += BeginCoverage;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += EndCoverage;
        display.color = Color.white; SetTarget(1);
    }
    public void BeginOrbitView(Transform firstModel, Transform secondModel, Transform pivot, float radius,
        float blackoutDuration, TrailRenderer trailPrefab, Material motionMaterial, AnimationClip recoilClip, Vector3 direction,
        GameObject trainingBackdrop, Material travelMaterial, Material roomMaterial, GameObject thrusterPrefab, AnimationClip riseClip, float contactAngle, float motionScale, float cameraLead)
    {
        BeginActorView(firstModel, secondModel);
        if (actorCamera == null) return;
        activeTransitionTime = blackoutDuration; orbitRadius = radius; orbitAspect = (float)texture.width / texture.height;
        baseViewSize=source.orthographicSize; leadDistance=cameraLead;
        viewStartedAt=Time.unscaledTime;
        initialViewPosition=stageOrigin+source.transform.position-reference+Vector3.back*20;
        initialViewRotation=source.transform.rotation;
        CameraShot=Shot.PlayerCloseup;
        zoomTo=CalculateOrbitSize(radius,orbitAspect,leadDistance);
        actorCamera.transform.SetPositionAndRotation(initialViewPosition,initialViewRotation);
        actorCamera.orthographicSize=baseViewSize; cameraBlend=null;
        var core = firstCopy.mapping.TryGetValue(pivot, out var found) ? found : firstCopy.model;
        foreach(var renderer in firstCopy.model.GetComponentsInChildren<SpriteRenderer>(true))
            if(renderer.name=="ModelBorderMask") { descentBorder=renderer; break; }
        orbitView = new GrabCinematicOrbitView(stage.transform, firstCopy.rig, firstCopy.model, core,
            secondCopy.rig, secondCopy.model, direction, radius, orbitAspect, trailPrefab, motionMaterial, recoilClip);
        orbitView.ConfigureTravel(thrusterPrefab,riseClip,contactAngle); orbitView.SetFlightScale(motionScale);
        backdropView = new GrabCinematicBackdropView(display.transform.parent, display.transform.GetSiblingIndex(), trainingBackdrop, travelMaterial, source);
    }
    public static float CalculateOrbitSize(float radius, float aspect,float lead=0)
    {
        // 세로 여백에는 레터박스와 배우의 몸체까지 포함한다.
        float extent = Mathf.Max(3.4f,radius+.9f+(lead>0 ? lead+.25f : 0));
        return Mathf.Max(extent / Mathf.Max(.1f, CinematicLetterbox.VisibleHeightRatio), extent / Mathf.Max(.1f, aspect));
    }
    public void BeginRise(float duration,float squeezeTime)
    {
        closeRequested = true; zoomDuration = duration;
    }
    public void SetTravel(float speed, float altitude, bool rising, bool rocketFiring=false)
    {
        orbitView?.SetVerticalMotion(speed); orbitView?.SetRise(altitude,rising,rocketFiring);
        backdropView?.SetTravel(speed, altitude, rising, Time.deltaTime);
        SetRocketFraming(rocketFiring);
        float target=rising ? leadDistance*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.4f,1,altitude))) : -leadDistance;
        travelLead=Mathf.MoveTowards(travelLead,target,Time.deltaTime*leadDistance/.16f);
    }
    public void DetachThrusters() { orbitView?.DetachThrusters(); SetRocketFraming(false); }
    public void SetGameplayAltitude(float value) => gameplayAltitude=Mathf.Max(0,value);
    public void SetImpactFrameCountdown(float value) => secondsUntilImpactFrame=value;
    private void SetRocketFraming(bool firing)
    {
        float next=firing ? 1 : 0;
        if(rocketFramingTo==next) return;
        rocketFramingFrom=RocketFramingWeight; rocketFramingTo=next; rocketFramingChangedAt=Time.unscaledTime;
        float configured=shotSettings!=null ? shotSettings.rocketFramingBlendDuration : .4f;
        float available=shotSettings!=null ? (firing ? shotSettings.fakeRiseDuration*.4f : shotSettings.verticalDecelerationDuration*.8f) : configured;
        rocketFramingDuration=Mathf.Max(.001f,Mathf.Min(configured,available));
        if(firing) { rocketRiseStartedAt=Time.unscaledTime; rocketRiseEntryDuration=rocketFramingDuration; }
    }
    public void BeginGameplayHandoff(Vector2 core,Vector2 floor,float radius,Bounds roomBounds,float duration)
    {
        if(worldHandoff || actorCamera==null) return;
        worldHandoff=true; worldCore=new Vector3(core.x,core.y,0); worldFloor=floor;
        gameplayCamera=new GrabGameplayCameraView(source,roomBounds);
        gameplayCamera.Configure(shotSettings!=null ? shotSettings.landingViewSize : baseViewSize,
            shotSettings!=null ? shotSettings.landingFloorViewport : .2f,floor);
        CameraShot=Shot.DescentLead;
        // 좌표와 카메라를 먼저 준비하고, 배경 표시는 전환 시점까지 유지한다.
        handoffStartedAt=Time.unscaledTime; handoffDuration=duration;
        descentFramingSpan=float.IsInfinity(secondsUntilImpactFrame) || float.IsNaN(secondsUntilImpactFrame)
            ? Mathf.Max(.001f,duration) : Mathf.Max(.001f,secondsUntilImpactFrame);
    }
    public void SetImpactFrame(bool visible,float progress)
    {
        if(visible && !impactFrameActive)
        {
            impactCanvas=GetComponent<Canvas>();
            if(impactCanvas!=null)
            {
                originalCanvasOrder=impactCanvas.sortingOrder;
                impactCanvas.sortingOrder=originalCanvasOrder+2; raisedImpactCanvas=true;
            }
        }
        else if(!visible) RestoreImpactLayer();
        impactFrameActive=visible; impactFrameProgress=Mathf.Clamp01(progress);
        if(compositeInstance!=null)
        {
            compositeInstance.SetFloat("_ImpactFrame",visible ? 1 : 0);
            compositeInstance.SetFloat("_ImpactProgress",impactFrameProgress);
        }
    }
    public void CutToGameplay()
    {
        if(!worldHandoff || gameplayCut) return;
        SetImpactFrame(false,1); gameplayCut=true;
        blend=from=target=0; blackout.color=Color.clear;
        // 임팩트 프레임 뒤에는 배경을 페이드하지 않고 같은 프레임에 원래 화면으로 전환한다.
        backdropView?.Update(0,source.aspect,source.orthographicSize,Vector2.zero);
        gameplayCamera?.UpdateLanding(1,Vector3.zero,1);
    }
    private void RestoreImpactLayer()
    {
        if(raisedImpactCanvas && impactCanvas!=null) impactCanvas.sortingOrder=originalCanvasOrder;
        raisedImpactCanvas=false;
    }
    public void SetVerticalMotion(float speed) => orbitView?.SetVerticalMotion(speed);
    public void SetOrbitAngle(float degrees) => orbitView?.SetAngle(degrees);
    public float NextGroundContactAngle(float angle) => orbitView != null ? orbitView.NextGroundContactAngle(angle) : angle;
    public void ShowImpact(FeedbackDataSO feedback) => orbitView?.ShowImpact(feedback);
    public void ReturnToGameplay() { returning=true; returnStartedAt=Time.unscaledTime; returnDuration=activeTransitionTime; if(!worldHandoff) SetTarget(0); }
    public void ReturnToGameplay(float duration) { activeTransitionTime = duration; ReturnToGameplay(); }
    private void SetTarget(float value) { from = blend; target = value; changedAt = Time.unscaledTime; }
    public void Release()
    {
        if (releasing) return;
        releasing = true; active = false; RemoveInput(); RestoreOriginals();
        blackout.color = Color.clear; display.color = Color.clear; DisposeGraphics(); Destroy(gameObject);
    }
    private void RestoreOriginals()
    { foreach (var pair in originals) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value; originals.Clear(); }
    private void BeginCoverage(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
    {
        if (camera != coverageCamera || stage == null) return;
        // 흑백 화면은 발광 재질도 실루엣으로 읽는다. 일반 화면의 투명 합성 규칙은 유지한다.
        if(impactFrameActive) { CoverageExcludedCount=0; return; }
        stage.GetComponentsInChildren(false, stageRenderers);
        foreach (var renderer in stageRenderers)
        {
            var material = renderer.sharedMaterial;
            if (material == null || material.shader == null) continue;
            string shader = material.shader.name;
            bool additive = shader.IndexOf("GlowAdd", System.StringComparison.OrdinalIgnoreCase) >= 0
                || shader.IndexOf("GlowMultiply", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!additive) continue;
            coverageHidden[renderer] = renderer.forceRenderingOff; renderer.forceRenderingOff = true;
        }
        CoverageExcludedCount = coverageHidden.Count;
    }
    private void EndCoverage(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
    { if (camera == coverageCamera) RestoreCoverage(); }
    private void RestoreCoverage()
    { foreach (var pair in coverageHidden) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value; coverageHidden.Clear(); }
    private ActorCopy CopyActor(Transform root)
    {
        var actor = root.GetComponentInParent<CharacterController2D>();
        Transform actorRoot = actor != null ? actor.transform : root;
        var mapping = new Dictionary<Transform, Transform>();
        var rig = new GameObject("ActorRig_" + root.name).transform; rig.SetParent(stage.transform, false);
        System.Func<Transform, Transform> ensure = null;
        ensure = original =>
        {
            if (mapping.TryGetValue(original, out var found)) return found;
            var go = new GameObject(original.name); go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(original == actorRoot ? rig : ensure(original.parent), false);
            mapping.Add(original, go.transform);
            transforms.Add(new TransformCopy { source = original, copy = go.transform, root = original == actorRoot });
            var sorting = original.GetComponent<UnityEngine.Rendering.SortingGroup>();
            if (sorting != null)
            {
                var copy = go.AddComponent<UnityEngine.Rendering.SortingGroup>();
                copy.sortingLayerID = sorting.sortingLayerID; copy.sortingOrder = sorting.sortingOrder;
            }
            return go.transform;
        };
        foreach (var original in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            originals[original] = original.forceRenderingOff;
            sprites.Add(new SpriteCopy { source = original, copy = ensure(original.transform).gameObject.AddComponent<SpriteRenderer>(), wasForcedOff = original.forceRenderingOff });
        }
        foreach (var original in root.GetComponentsInChildren<LineRenderer>(true))
        {
            originals[original] = original.forceRenderingOff;
            lines.Add(new LineCopy { source = original, copy = ensure(original.transform).gameObject.AddComponent<LineRenderer>(), wasForcedOff = original.forceRenderingOff });
        }
        foreach (var original in root.GetComponentsInChildren<Transform>(true)) ensure(original);
        foreach (var original in root.GetComponentsInChildren<Renderer>(true))
            if (!originals.ContainsKey(original)) originals[original] = original.forceRenderingOff;
        return new ActorCopy { rig = rig, model = ensure(root), mapping = mapping };
    }
    private void ResizeTexture()
    {
        int width = Mathf.Clamp(source!=null ? source.pixelWidth : Screen.width, 640, 1920);
        float aspect=source!=null ? source.aspect : (float)Screen.width/Mathf.Max(1,Screen.height);
        int height = Mathf.Max(1, Mathf.RoundToInt(width / Mathf.Max(.1f,aspect)));
        if (texture != null && texture.width == width && texture.height == height) return;
        if (actorCamera != null) actorCamera.targetTexture = null;
        if (coverageCamera != null) coverageCamera.targetTexture = null;
        if (texture != null) { texture.Release(); Destroy(texture); }
        if (coverageTexture != null) { coverageTexture.Release(); Destroy(coverageTexture); }
        texture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGBHalf) { name = "GrabActorView", filterMode = FilterMode.Bilinear };
        texture.Create(); actorCamera.targetTexture = texture; display.texture = texture;
        coverageTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGBHalf) { name = "GrabActorCoverage", filterMode = FilterMode.Bilinear };
        coverageTexture.Create(); coverageCamera.targetTexture = coverageTexture; compositeInstance.SetTexture("_CoverageTex", coverageTexture);
    }
    private void LateUpdate()
    {
        if (active && (owner == null || (owner is GameObject go && !go.activeInHierarchy)
            || (owner is Component c && !c.gameObject.activeInHierarchy) || (owner is Behaviour b && !b.isActiveAndEnabled))) { Release(); return; }
        float t = Mathf.Clamp01((Time.unscaledTime - changedAt) / Mathf.Max(.001f, activeTransitionTime));
        blend = worldHandoff ? (gameplayCut ? 0 : 1) : Mathf.Lerp(from, target, Mathf.SmoothStep(0, 1, t));
        blackout.color = new Color(0, 0, 0, blend);
        if (actorCamera == null || source == null || actorA == null || actorB == null) return;
        ResizeTexture();
        firstCopy.rig.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        secondCopy.rig.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        float worldMix=WorldMix;
        framingOffset=Vector3.zero;
        Vector3 viewReference=Vector3.Lerp(reference,worldCore,worldMix);
        foreach (var pair in transforms) pair.Sync(stageOrigin - viewReference);
        foreach (var pair in sprites) pair.Sync(); foreach (var pair in lines) pair.Sync();
        Vector3 sourcePosition = stageOrigin + source.transform.position - reference + Vector3.back * 20;
        Quaternion sourceRotation = source.transform.rotation;
        if (orbitView != null)
        {
            float aspect = (float)texture.width / texture.height;
            if (!Mathf.Approximately(aspect, orbitAspect))
            {
                orbitAspect = aspect; zoomTo = CalculateOrbitSize(orbitRadius, aspect,leadDistance);
                orbitView.SetAspect(aspect);
            }
            float exit=returning ? Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-returnStartedAt)/Mathf.Max(.001f,returnDuration))) : 0;
            orbitView.SetRadius(orbitRadius);
            if(worldHandoff) orbitView.SetGameplayContact(worldMix,stageOrigin.y+worldFloor.y-worldCore.y,stageOrigin.x+worldFloor.x-worldCore.x,gameplayAltitude);
            orbitView.UpdatePose(Time.deltaTime,1-exit);
            gameplayCamera?.UpdateLanding(worldMix,orbitView.ShakeOffset,worldMix);
            if(impactFrameActive)
            {
                CameraShot=Shot.ImpactFrame;
                // 흑백 화면에서는 두 배우를 함께 잡고, 동작과 시간은 계속 빠르게 진행한다.
                Vector3 centre=(orbitView.CorePosition+orbitView.PlayerPosition)*.5f;
                actorCamera.transform.SetPositionAndRotation(centre+Vector3.back*30,Quaternion.identity);
                actorCamera.orthographicSize=CalculateOrbitSize(orbitRadius*.65f,aspect);
                Vector3 focus=actorCamera.WorldToViewportPoint(orbitView.PlayerPosition);
                compositeInstance.SetVector("_ImpactFocus",new Vector4(Mathf.Clamp(focus.x,.1f,.9f),Mathf.Clamp(focus.y,.1f,.9f),0,0));
                compositeInstance.SetFloat("_ImpactAspect",aspect);
            }
            else if(gameplayCut)
            {
                CameraShot=Shot.LandingHold;
                ApplyGameplayProjection();
            }
            else if(worldHandoff)
            {
                BlendCameraTo(Shot.DescentLead,stageOrigin+source.transform.position-worldCore+Vector3.back*20,
                    sourceRotation,source.orthographicSize,handoffDuration);
                ApplyDescentFraming();
            }
            else if(closeRequested)
            {
                float pullback=shotSettings!=null ? Mathf.Min(shotSettings.risePullbackDuration,
                    shotSettings.fakeRiseDuration+shotSettings.verticalDecelerationDuration) : zoomDuration;
                BlendCameraTo(Shot.Duo,stageOrigin+orbitView.FlightOffset+Vector3.up*travelLead+Vector3.back*30,
                    Quaternion.identity,zoomTo,pullback);
                actorCamera.transform.position+=orbitView.ShakeOffset;
                ApplyRocketFraming();
            }
            else
            {
                float elapsed=Time.unscaledTime-viewStartedAt;
                float captureDuration=shotSettings!=null ? shotSettings.blackoutDuration : activeTransitionTime;
                float split=captureDuration*(shotSettings!=null ? shotSettings.playerFocusFraction : .4f);
                float duration=shotSettings!=null ? shotSettings.closeupBlendDuration : .65f;
                if(elapsed<split)
                    BlendCameraTo(Shot.PlayerCloseup,orbitView.PlayerPosition+Vector3.back*30,Quaternion.identity,
                        shotSettings!=null ? shotSettings.playerCloseupSize : 2.2f,Mathf.Min(duration,split*.8f));
                else
                    BlendCameraTo(Shot.BossCloseup,orbitView.CorePosition+Vector3.back*30,Quaternion.identity,
                        shotSettings!=null ? shotSettings.bossCloseupSize : 3.2f,Mathf.Min(duration,(captureDuration-split)*.8f));
            }
        }
        else { actorCamera.transform.SetPositionAndRotation(sourcePosition,sourceRotation); actorCamera.orthographicSize=source.orthographicSize; }
        backdropView?.Update(worldHandoff ? blend : 1,(float)texture.width/texture.height,actorCamera.orthographicSize,(Vector2)(actorCamera.transform.position-stageOrigin-framingOffset));
        coverageCamera.CopyFrom(actorCamera); coverageCamera.targetTexture = coverageTexture;
        coverageCamera.transform.SetPositionAndRotation(actorCamera.transform.position, actorCamera.transform.rotation);
        coverageCamera.backgroundColor = Color.clear; coverageCamera.depth = actorCamera.depth + 1;
    }
    private void CameraUpdated(CinemachineBrain brain)
    { if(brain.OutputCamera==source && worldHandoff) SyncGameplayProjection(); }
    private CameraState CameraPose(Vector3 position,Quaternion rotation,float size)
    {
        var state=CameraState.Default;
        state.Lens=LensSettings.FromCamera(actorCamera); state.Lens.OrthographicSize=size;
        state.RawPosition=position; state.RawOrientation=rotation;
        state.BlendHint=CameraState.BlendHints.IgnoreLookAtTarget;
        return state;
    }
    private void BlendCameraTo(Shot shot,Vector3 position,Quaternion rotation,float size,float duration)
    {
        if(cameraBlend==null || blendedShot!=shot)
        {
            // 직전 출력에서 이어 받아 확대·이동을 하나의 Cinemachine 블렌드로 연결한다.
            blendSource.Pose=CameraPose(actorCamera.transform.position,actorCamera.transform.rotation,actorCamera.orthographicSize);
            var definition=new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut,Mathf.Max(.001f,duration));
            cameraBlend=new CinemachineBlend { CamA=blendSource,CamB=blendTarget,BlendCurve=definition.BlendCurve,Duration=definition.Time };
            cameraBlendStartedAt=Time.unscaledTime; blendedShot=shot;
        }
        CameraShot=shot; UpdateCameraBlend(position,rotation,size);
    }
    private void UpdateCameraBlend(Vector3 position,Quaternion rotation,float size)
    {
        blendTarget.Pose=CameraPose(position,rotation,size);
        cameraBlend.TimeInBlend=Mathf.Max(0,Time.unscaledTime-cameraBlendStartedAt);
        var state=cameraBlend.State;
        actorCamera.transform.SetPositionAndRotation(state.GetFinalPosition(),state.GetFinalOrientation());
        actorCamera.orthographicSize=state.Lens.OrthographicSize;
    }
    private void SyncGameplayProjection()
    {
        if(actorCamera==null || source==null || impactFrameActive) return;
        if(gameplayCut) ApplyGameplayProjection();
        else if(cameraBlend!=null && blendedShot==Shot.DescentLead)
        {
            UpdateCameraBlend(stageOrigin+source.transform.position-worldCore+Vector3.back*20,source.transform.rotation,source.orthographicSize);
            framingOffset=Vector3.zero; ApplyDescentFraming();
        }
        actorCamera.aspect=source.aspect;
        if(coverageCamera!=null) { coverageCamera.transform.SetPositionAndRotation(actorCamera.transform.position,actorCamera.transform.rotation); coverageCamera.orthographicSize=actorCamera.orthographicSize; coverageCamera.aspect=source.aspect; }
    }
    private void ApplyGameplayProjection()
    {
        framingOffset=Vector3.zero;
        Vector3 worldPosition=stageOrigin+source.transform.position-worldCore+Vector3.back*20;
        actorCamera.transform.SetPositionAndRotation(worldPosition,source.transform.rotation);
        actorCamera.orthographicSize=source.orthographicSize;
    }
    private void ApplyRocketFraming()
    {
        float rise=shotSettings!=null ? shotSettings.fakeRiseDuration : zoomDuration;
        float travelFraction=shotSettings!=null ? shotSettings.rocketRiseTravelFraction : .85f;
        float duration=Mathf.Max(.001f,(rise-rocketRiseEntryDuration)*travelFraction);
        float progress=Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-rocketRiseStartedAt-rocketRiseEntryDuration)/duration));
        float start=BossEdgeViewport(false,shotSettings!=null ? shotSettings.rocketBossVisibleFraction : .5f);
        float end=shotSettings!=null ? shotSettings.rocketRiseEndViewportY : .58f;
        ApplyBossFramingAtViewport(Mathf.Lerp(start,end,progress),RocketFramingWeight);
    }
    private void ApplyDescentFraming()
    {
        float progress=float.IsInfinity(secondsUntilImpactFrame) || float.IsNaN(secondsUntilImpactFrame)
            ? (Time.unscaledTime-handoffStartedAt)/descentFramingSpan : 1-secondsUntilImpactFrame/descentFramingSpan;
        float start=shotSettings!=null ? shotSettings.descentBossStartViewportY : .5f;
        float end=BossEdgeViewport(true,shotSettings!=null ? shotSettings.preImpactBossVisibleFraction : .5f);
        // 하강 시작부터 흑백 연출 직전까지 화면 중앙에서 상단으로 계속 이어진다.
        ApplyBossFramingAtViewport(Mathf.Lerp(start,end,Mathf.SmoothStep(0,1,Mathf.Clamp01(progress))),1);
    }
    private float BossEdgeViewport(bool top,float fraction)
    {
        if(descentBorder==null || actorCamera==null) return .5f;
        var bounds=descentBorder.bounds; Vector3 up=actorCamera.transform.up;
        float height=(Mathf.Abs(up.x)*bounds.size.x+Mathf.Abs(up.y)*bounds.size.y+Mathf.Abs(up.z)*bounds.size.z)/(2*actorCamera.orthographicSize);
        float edge=.5f+(top ? .5f : -.5f)*CinematicLetterbox.VisibleHeightRatio;
        return edge+(top ? .5f-fraction : fraction-.5f)*height;
    }
    private void ApplyBossFramingAtViewport(float desired,float weight)
    {
        if(descentBorder==null || actorCamera==null || weight<=0) return;
        float current=actorCamera.WorldToViewportPoint(descentBorder.bounds.center).y;
        Vector3 correction=actorCamera.transform.up*((current-desired)*2*actorCamera.orthographicSize*weight);
        actorCamera.transform.position+=correction; framingOffset+=correction;
    }
    private void RemoveInput()
    {
        if (!ownsInput) return; ownsInput = false;
        if (inputOwner != null) { inputOwner.RemoveInputTarget(this); inputOwner.PopGameplayBlock(); }
    }
    private void OnDisable() { active = false; RemoveInput(); DisposeGraphics(); }
    private void OnDestroy() { RemoveInput(); DisposeGraphics(); }
    private void DisposeGraphics()
    {
        SetImpactFrame(false,0);
        RemoveInput(); RestoreOriginals();
        CinemachineCore.CameraUpdatedEvent.RemoveListener(CameraUpdated);
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= BeginCoverage;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= EndCoverage;
        RestoreCoverage();
        if (actorCamera != null) { actorCamera.enabled = false; actorCamera.targetTexture = null; }
        if (coverageCamera != null) { coverageCamera.enabled = false; coverageCamera.targetTexture = null; }
        if (texture != null) { texture.Release(); Destroy(texture); }
        if (coverageTexture != null) { coverageTexture.Release(); Destroy(coverageTexture); }
        gameplayCamera?.Dispose(); gameplayCamera=null;
        backdropView?.Dispose(); backdropView = null;
        if (stage != null) Destroy(stage);
        if (compositeInstance != null) Destroy(compositeInstance);
        compositeInstance = null; texture = coverageTexture = null; stage = null; actorCamera = coverageCamera = null; orbitView = null; cameraBlend=null;
        descentBorder=null; framingOffset=Vector3.zero;
    }
    public void OnMove(Vector2 direction) { }
    public void OnConfirm() { }
    public void OnCancel() { }
    private sealed class ShotCamera : ICinemachineCamera
    {
        public CameraState Pose;
        public string Name => "GrabShot";
        public string Description => Name;
        public CameraState State => Pose;
        public bool IsValid => true;
        public ICinemachineMixer ParentCamera => null;
        public void UpdateCameraState(Vector3 worldUp,float deltaTime) { }
        public void OnCameraActivated(ICinemachineCamera.ActivationEventParams evt) { }
    }
    private sealed class ActorCopy
    {
        public Transform rig, model;
        public Dictionary<Transform, Transform> mapping;
    }
    private sealed class TransformCopy
    {
        public Transform source, copy; public bool root;
        public void Sync(Vector3 offset)
        {
            if (source == null) return;
            copy.localPosition = root ? source.position + offset - copy.parent.position : source.localPosition;
            copy.localRotation = root ? source.rotation : source.localRotation;
            copy.localScale = root ? source.lossyScale : source.localScale;
        }
    }
    private sealed class SpriteCopy
    {
        public SpriteRenderer source, copy; public bool wasForcedOff;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        public void Sync()
        {
            if (source == null) { copy.enabled = false; return; }
            copy.enabled = source.enabled && source.gameObject.activeInHierarchy && !wasForcedOff;
            copy.sprite = source.sprite; copy.sharedMaterial = source.sharedMaterial; copy.color = source.color;
            copy.flipX = source.flipX; copy.flipY = source.flipY; copy.maskInteraction = SpriteMaskInteraction.None;
            copy.sortingLayerID = source.sortingLayerID; copy.sortingOrder = source.sortingOrder;
            source.GetPropertyBlock(properties); copy.SetPropertyBlock(properties);
        }
    }
    private sealed class LineCopy
    {
        public LineRenderer source, copy; public bool wasForcedOff;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        public void Sync()
        {
            if (source == null) { copy.enabled = false; return; }
            copy.enabled = source.enabled && source.gameObject.activeInHierarchy && !wasForcedOff;
            copy.sharedMaterial = source.sharedMaterial; copy.useWorldSpace = false; copy.loop = source.loop;
            copy.positionCount = source.positionCount; copy.widthCurve = source.widthCurve;
            copy.widthMultiplier = source.widthMultiplier;
            copy.colorGradient = source.colorGradient; copy.numCapVertices = copy.numCornerVertices = 2;
            copy.sortingLayerID = source.sortingLayerID; copy.sortingOrder = source.sortingOrder;
            for (int i = 0; i < source.positionCount; i++) copy.SetPosition(i, source.useWorldSpace ? source.transform.InverseTransformPoint(source.GetPosition(i)) : source.GetPosition(i));
            source.GetPropertyBlock(properties); copy.SetPropertyBlock(properties);
        }
    }
}
