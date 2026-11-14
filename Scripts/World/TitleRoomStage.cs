using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 타이틀 최초 실행 연출의 3D 방(책상·모니터·휴대폰·먼지)을 다룬다.
/// 모니터 화면에는 타이틀 카메라가 그린 그림을 흑백으로 띄우고, 방 카메라가 그 방을 화면에 그린다.
/// 연출의 흐름(전화·메일·서명)과 휴대폰 화면의 내용은 TitleTrainingNoticeIntro가 맡고, 여기는 "얼마나"만 받아 매 프레임 자세·빛을 계산한다.
/// 방은 타이틀 월드에서 멀리 떨어진 자리에 두어 서로의 카메라에 잡히지 않는다(레이어를 따로 쓰지 않는다).
/// </summary>
[DisallowMultipleComponent]
public sealed class TitleRoomStage : MonoBehaviour
{
    [Header("카메라")]
    [SerializeField] private Camera roomCamera;
    [SerializeField, Tooltip("멀리서 모니터를 보는 자리")] private Transform restView;
    [SerializeField, Tooltip("벨이 울릴 때 휴대폰 쪽으로 시선이 간 자리")] private Transform glanceView;
    [SerializeField] private float restFieldOfView = 34f;
    [SerializeField, Tooltip("화면이 시야를 채울 때의 시야각")] private float zoomFieldOfView = 22f;
    [SerializeField, Tooltip("가만히 있을 때의 미세한 흔들림(도)")] private float breathingAngle = .12f;

    [Header("모니터")]
    [SerializeField, Tooltip("화면 비율에 맞춰 가로 배율을 바꾸는 모니터 몸체")] private Transform monitorBody;
    [SerializeField, Tooltip("화면. 로컬 +Z가 보는 사람 쪽이다")] private MeshRenderer screenRenderer;
    [SerializeField, Tooltip("화면의 실제 높이(미터)")] private float screenHeight = .336f;
    [SerializeField] private Light screenLight;
    [SerializeField, Range(0f, 1f)] private float introSaturation;
    [SerializeField, Range(0f, 2f)] private float introBrightness = .22f;
    [SerializeField, Tooltip("방에서 보이는 화면의 밝기 배율(화면이 시야를 채우면 이 배율이 걷혀 1이 된다)")] private float roomEmission = 9f;

    [Header("휴대폰")]
    [SerializeField, Tooltip("책상 위에 놓인 자세가 기본 자세. 로컬 +Y가 화면, +Z가 위쪽 끝")] private Transform phone;
    [SerializeField, Tooltip("눈앞에 들었을 때 카메라에서 떨어진 거리(미터)")] private float heldDistance = .236f;
    [SerializeField] private Vector2 heldOffset = new Vector2(0f, -.004f);
    [SerializeField, Tooltip("휴대폰 화면(520×908 Canvas)을 붙이는 자리. 화면 한가운데에 화면 쪽을 보고 놓여 있다")] private Transform phoneScreenAnchor;
    [SerializeField] private Light phoneLight;

    [Header("분위기")]
    [SerializeField] private ParticleSystemRenderer dustRenderer;
    [SerializeField] private Volume roomVolume;

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
    private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    private static readonly int EmissionId = Shader.PropertyToID("_Emission");
    private static readonly int DetailId = Shader.PropertyToID("_Detail");
    private static readonly int GridSizeId = Shader.PropertyToID("_GridSize");
    private static readonly int LightPositionId = Shader.PropertyToID("_LightPosition");
    private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
    private static readonly int LightColorId = Shader.PropertyToID("_LightColor");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    private Camera titleCamera;
    private RenderTexture liveScreen, originalTarget;
    private Material screenMaterial, dustMaterial, originalScreenMaterial, originalDustMaterial;
    private VolumeProfile originalProfile, runtimeProfile;
    private Bloom bloom;
    private Vignette vignette;
    private FilmGrain grain;
    private DepthOfField depthOfField;
    private float bloomBase, vignetteBase, grainBase, dustBase, screenLightBase, phoneLightBase, restFocus;
    private Vector3 phoneDeskPosition, monitorScale;
    private Quaternion phoneDeskRotation;
    private CanvasGroup phoneScreen;
    private int fittedWidth, fittedHeight;
    private bool running, cached, followsDisplay;

    /// <summary>방 카메라. 휴대폰 화면(World Space Canvas)의 입력 카메라로도 쓴다.</summary>
    public Camera RoomCamera => roomCamera;
    public bool IsRunning => running;
    /// <summary>0이면 모니터를 보고, 1이면 휴대폰 쪽으로 시선이 간다.</summary>
    public float Glance { get; set; }
    /// <summary>0이면 휴대폰이 책상 위에, 1이면 눈앞에 있다.</summary>
    public float PhoneLift { get; set; }
    /// <summary>0이면 멀리서 보고, 1이면 모니터 화면이 시야를 정확히 채운다.</summary>
    public float MonitorZoom { get; set; }
    /// <summary>휴대폰 화면이 켜진 정도(0~1). 화면 표시와 주변을 비추는 빛에 함께 쓴다.</summary>
    public float PhonePower { get; set; }
    /// <summary>벨이 울리는 동안의 진동.</summary>
    public bool Ringing { get; set; }

    private void Awake()
    {
        Cache();
        if (!running) SetVisible(false);
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        phoneDeskPosition = phone.localPosition;
        phoneDeskRotation = phone.localRotation;
        monitorScale = monitorBody.localScale;
        screenLightBase = screenLight != null ? screenLight.intensity : 0f;
        phoneLightBase = phoneLight != null ? phoneLight.intensity : 0f;
    }

    private void SetVisible(bool visible)
    {
        if (roomCamera != null) roomCamera.enabled = visible;
        foreach (Transform child in transform) child.gameObject.SetActive(visible);
    }

    /// <summary>
    /// 방을 켜고 타이틀 카메라의 그림을 모니터 화면으로 돌린다.
    /// 화면 크기를 주지 않으면 실제 화면 크기를 쓴다(검증 도구는 창 없는 편집기의 화면 비율에 좌우되지 않게 크기를 정해 준다).
    /// </summary>
    public void Begin(Camera source, int screenWidth = 0, int screenHeight = 0)
    {
        if (running) return;
        Cache();
        running = true;
        titleCamera = source;
        Glance = PhoneLift = MonitorZoom = PhonePower = 0f;
        Ringing = false;
        SetVisible(true);

        originalTarget = titleCamera.targetTexture;
        originalScreenMaterial = screenRenderer.sharedMaterial;
        screenMaterial = new Material(originalScreenMaterial) { name = "TitleRoomScreenInstance", hideFlags = HideFlags.DontSave };
        screenRenderer.sharedMaterial = screenMaterial;
        followsDisplay = screenWidth <= 0 || screenHeight <= 0;
        FitScreen(followsDisplay ? Screen.width : screenWidth, followsDisplay ? Screen.height : screenHeight);

        if (dustRenderer != null)
        {
            originalDustMaterial = dustRenderer.sharedMaterial;
            dustMaterial = new Material(originalDustMaterial) { name = "TitleRoomDustInstance", hideFlags = HideFlags.DontSave };
            dustBase = dustMaterial.GetFloat(IntensityId);
            dustRenderer.sharedMaterial = dustMaterial;
        }

        if (roomVolume != null && roomVolume.sharedProfile != null)
        {
            // 공유 프로파일(에셋)은 건드리지 않고 사본의 값을 움직인다.
            // 프로파일만 복제하면 그 안의 효과들은 여전히 원본을 가리키므로, 효과를 하나씩 복제해 새 프로파일에 담는다.
            originalProfile = roomVolume.sharedProfile;
            runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name = originalProfile.name + " (Runtime)";
            runtimeProfile.hideFlags = HideFlags.DontSave;
            foreach (var component in originalProfile.components)
            {
                if (component == null) continue;
                var copy = Instantiate(component);
                copy.hideFlags = HideFlags.DontSave;
                runtimeProfile.components.Add(copy);
            }
            roomVolume.profile = runtimeProfile;
            if (runtimeProfile.TryGet(out bloom)) bloomBase = bloom.intensity.value;
            if (runtimeProfile.TryGet(out vignette)) vignetteBase = vignette.intensity.value;
            if (runtimeProfile.TryGet(out grain)) grainBase = grain.intensity.value;
            if (runtimeProfile.TryGet(out depthOfField)) restFocus = depthOfField.focusDistance.value;
        }
        Apply();
    }

    /// <summary>
    /// 모니터와 그 화면에 띄울 그림을 화면 크기에 맞춘다.
    /// 모니터의 가로·세로 비율이 실제 화면과 같아야, 화면이 시야를 채우는 순간 실제 타이틀 화면과 그림이 어긋나지 않는다.
    /// </summary>
    private void FitScreen(int displayWidth, int displayHeight)
    {
        fittedWidth = displayWidth;
        fittedHeight = displayHeight;
        float aspect = Mathf.Max(1, displayWidth) / (float)Mathf.Max(1, displayHeight);
        // 그림의 해상도는 비율을 지킨 채로 줄이거나 키운다(가로·세로를 따로 자르면 비율이 달라진다).
        int height = Mathf.Clamp(displayHeight, 360, 1440);
        int width = Mathf.RoundToInt(height * aspect);
        if (width > 3840)
        {
            width = 3840;
            height = Mathf.RoundToInt(width / aspect);
        }
        width = Mathf.Max(16, width);
        height = Mathf.Max(16, height);

        if (liveScreen == null || liveScreen.width != width || liveScreen.height != height)
        {
            var previous = liveScreen;
            liveScreen = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "TitleRoomLiveScreen", hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear, antiAliasing = 1
            };
            liveScreen.Create();
            titleCamera.targetTexture = liveScreen;
            if (previous != null)
            {
                previous.Release();
                Release(previous);
            }
        }
        monitorBody.localScale = new Vector3(monitorScale.x * aspect / (16f / 9f), monitorScale.y, monitorScale.z);
        screenMaterial.SetTexture(MainTexId, liveScreen);
        screenMaterial.SetVector(GridSizeId, new Vector4(width, height, 0f, 0f));
    }

    /// <summary>
    /// 휴대폰 화면의 내용(World Space Canvas)을 3D 휴대폰의 화면 자리에 붙인다.
    /// Canvas는 방 Prefab이 아니라 Scene이 갖고 있어, 방을 다시 만들어도 화면의 내용과 연결이 남는다.
    /// </summary>
    public void AttachPhoneCanvas(Canvas canvas, CanvasGroup screenGroup)
    {
        phoneScreen = screenGroup;
        if (canvas == null || phoneScreenAnchor == null) return;
        var canvasTransform = canvas.transform;
        canvasTransform.SetParent(phoneScreenAnchor, false);
        canvasTransform.localPosition = Vector3.zero;
        canvasTransform.localRotation = Quaternion.identity;
        canvasTransform.localScale = Vector3.one;
        canvas.worldCamera = roomCamera;
        canvas.gameObject.SetActive(true);
    }

    /// <summary>방을 끄고 타이틀 카메라를 원래 출력으로 되돌린다.</summary>
    public void End()
    {
        if (!running) return;
        running = false;
        if (titleCamera != null && titleCamera.targetTexture == liveScreen) titleCamera.targetTexture = originalTarget;
        titleCamera = null;
        if (screenRenderer != null && originalScreenMaterial != null) screenRenderer.sharedMaterial = originalScreenMaterial;
        if (dustRenderer != null && originalDustMaterial != null) dustRenderer.sharedMaterial = originalDustMaterial;
        if (roomVolume != null && originalProfile != null)
        {
            roomVolume.profile = null;
            roomVolume.sharedProfile = originalProfile;
        }
        if (runtimeProfile != null)
        {
            foreach (var component in runtimeProfile.components) Release(component);
            Release(runtimeProfile);
        }
        Release(screenMaterial); Release(dustMaterial);
        screenMaterial = dustMaterial = null;
        runtimeProfile = null;
        bloom = null; vignette = null; grain = null; depthOfField = null;
        if (liveScreen != null)
        {
            liveScreen.Release();
            Release(liveScreen);
            liveScreen = null;
        }
        if (cached)
        {
            phone.localPosition = phoneDeskPosition;
            phone.localRotation = phoneDeskRotation;
            monitorBody.localScale = monitorScale;
            if (screenLight != null) screenLight.intensity = screenLightBase;
            if (phoneLight != null) phoneLight.intensity = phoneLightBase;
        }
        SetVisible(false);
    }

    private static void Release(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
    }

    private void LateUpdate()
    {
        if (running) Apply();
    }

    private void Apply()
    {
        // 연출 도중에 창 크기나 전체 화면 여부가 바뀌면 모니터를 다시 맞춘다.
        if (followsDisplay && (Screen.width != fittedWidth || Screen.height != fittedHeight)) FitScreen(Screen.width, Screen.height);

        float time = Time.unscaledTime;
        float zoom = Mathf.Clamp01(MonitorZoom), lift = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(PhoneLift)), glance = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Glance));

        // ---- 카메라: 쉬는 자리(시선 이동 포함) → 화면이 시야를 채우는 자리 ----
        Vector3 basePosition = Vector3.Lerp(restView.position, glanceView.position, glance);
        Quaternion baseRotation = Quaternion.Slerp(restView.rotation, glanceView.rotation, glance);
        float sway = breathingAngle * (1f - zoom);
        Quaternion breathing = Quaternion.Euler(
            (Mathf.Sin(time * .37f) + .4f * Mathf.Sin(time * .83f + 1.3f)) * sway,
            Mathf.Sin(time * .29f + 2.1f) * sway * 1.2f, 0f);

        Transform screen = screenRenderer.transform;
        float halfHeight = screenHeight * .5f * screen.lossyScale.y;
        float distance = halfHeight / Mathf.Tan(zoomFieldOfView * .5f * Mathf.Deg2Rad);
        Vector3 zoomPosition = screen.position + screen.forward * distance;
        Quaternion zoomRotation = Quaternion.LookRotation(-screen.forward, screen.up);

        Transform cameraTransform = roomCamera.transform;
        cameraTransform.SetPositionAndRotation(
            Vector3.Lerp(basePosition, zoomPosition, zoom),
            Quaternion.Slerp(baseRotation * breathing, zoomRotation, zoom));
        roomCamera.fieldOfView = Mathf.Lerp(restFieldOfView, zoomFieldOfView, zoom);

        // ---- 휴대폰: 책상 위 → 눈앞(화면이 카메라를 향한다) ----
        Transform parent = phone.parent;
        Vector3 deskPosition = parent != null ? parent.TransformPoint(phoneDeskPosition) : phoneDeskPosition;
        Quaternion deskRotation = parent != null ? parent.rotation * phoneDeskRotation : phoneDeskRotation;
        if (Ringing && lift < .01f)
        {
            float buzz = Mathf.Sin(time * 43f);
            deskPosition += deskRotation * new Vector3(buzz * .0012f, 0f, Mathf.Sin(time * 57f) * .0006f);
            deskRotation *= Quaternion.Euler(0f, buzz * .7f, 0f);
        }
        Vector3 heldPosition = basePosition + baseRotation * new Vector3(heldOffset.x, heldOffset.y, heldDistance);
        Quaternion heldRotation = Quaternion.LookRotation(baseRotation * Vector3.up, baseRotation * Vector3.back);
        Vector3 arc = Vector3.up * (Mathf.Sin(lift * Mathf.PI) * .04f);
        phone.SetPositionAndRotation(Vector3.Lerp(deskPosition, heldPosition, lift) + arc, Quaternion.Slerp(deskRotation, heldRotation, lift));

        // ---- 빛 ----
        float power = Mathf.Clamp01(PhonePower);
        if (phoneScreen != null) phoneScreen.alpha = power;
        if (phoneLight != null)
        {
            float pulse = Ringing ? .75f + .25f * Mathf.Sin(time * 12f) : 1f;
            phoneLight.intensity = phoneLightBase * power * pulse;
        }
        if (screenLight != null)
        {
            // 화면의 미세한 밝기 변화
            float flicker = 1f + .012f * Mathf.Sin(time * 7.3f) + .008f * Mathf.Sin(time * 17.9f + .7f);
            screenLight.intensity = screenLightBase * flicker;
        }

        // ---- 화면: 시야를 채우는 순간 실제 타이틀 화면과 이어지도록 방 전용 효과를 걷는다 ----
        if (screenMaterial != null)
        {
            screenMaterial.SetFloat(SaturationId, introSaturation);
            screenMaterial.SetFloat(BrightnessId, introBrightness);
            // 방에서만 보이는 화면 효과(광원처럼 밝은 정도·백라이트·가장자리 감광·픽셀 격자)는 하나의 값으로 함께 걷힌다.
            // 어두운 방에서는 화면이 눈부시게 밝고, 화면이 시야를 차지할수록 눈이 적응하듯 제 밝기로 내려와 실제 타이틀 화면과 이어진다.
            screenMaterial.SetFloat(EmissionId, roomEmission);
            screenMaterial.SetFloat(DetailId, 1f - Ramp(.45f, 1f, zoom));
        }
        if (dustMaterial != null)
        {
            dustMaterial.SetVector(LightPositionId, screen.position);
            dustMaterial.SetVector(LightDirectionId, screen.forward);
            if (screenLight != null) dustMaterial.SetColor(LightColorId, screenLight.color);
            dustMaterial.SetFloat(IntensityId, dustBase * (1f - Ramp(.55f, 1f, zoom)));
        }
        // 방의 분위기(빛 번짐·가장자리 어둠·입자감)는 화면이 시야를 거의 채울 때까지 남아 있다가 걷힌다.
        float room = 1f - Ramp(.6f, 1f, zoom);
        if (bloom != null) bloom.intensity.value = bloomBase * room;
        if (vignette != null) vignette.intensity.value = vignetteBase * room;
        if (grain != null) grain.intensity.value = grainBase * room;
        if (depthOfField != null)
        {
            // 휴대폰을 들면 초점이 휴대폰으로 옮겨 가 방이 흐려진다.
            depthOfField.active = lift > .02f;
            depthOfField.focusDistance.value = Mathf.Lerp(restFocus, heldDistance, lift);
        }
    }

    // start 이하에서 0, end 이상에서 1로 부드럽게 오른다(Mathf.SmoothStep은 두 값을 보간하는 함수라 이 용도로 쓸 수 없다).
    private static float Ramp(float start, float end, float value)
    {
        float t = Mathf.Clamp01((value - start) / (end - start));
        return t * t * (3f - 2f * t);
    }

    private void OnDisable()
    {
        End();
    }
}
