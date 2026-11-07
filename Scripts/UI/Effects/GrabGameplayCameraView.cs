using Unity.Cinemachine;
using UnityEngine;

// 원본 룸 카메라는 그대로 두고, 착지 구도를 선점하는 자기 override만 유지한다.
public sealed class GrabGameplayCameraView
{
    private readonly Camera source;
    private readonly CinemachineBrain brain;
    private readonly CinemachineCamera camera;
    private readonly Bounds room;
    private readonly Vector3 originalPosition;
    private readonly float originalSize, visibleBottom;
    private Vector3 resting;
    private Vector2 floor;
    private float size, landingSize;
    private int id=-1;
    public float ViewSize => size;
    public Vector3 Position => camera!=null ? camera.transform.position : Vector3.zero;
    public Vector3 RestingPosition => resting;
    // 렌즈가 달라져도 원래 보이던 화면 아래를 더 드러내지 않는다.
    public float VisibleBottom => visibleBottom;
    public float MinimumY => visibleBottom+size;
    public float FloorViewport => .5f+(floor.y-Position.y)/(2*Mathf.Max(.1f,size));
    public float LeadOffset { get; private set; }
    public float OverrideWeight { get; private set; }
    public bool IsSettled { get; private set; }
    public GrabGameplayCameraView(Camera source,Bounds roomBounds)
    {
        this.source=source; room=roomBounds;
        brain=source.GetComponent<CinemachineBrain>();
        camera=new GameObject("GrabGameplayCamera").AddComponent<CinemachineCamera>(); camera.Priority=-1000; camera.enabled=false;
        camera.Lens=LensSettings.FromCamera(source);
        originalSize=size=landingSize=source.orthographicSize;
        originalPosition=source.transform.position;
        visibleBottom=Mathf.Max(room.min.y,originalPosition.y-originalSize);
        resting=new Vector3(room.center.x,Mathf.Max(visibleBottom+size,originalPosition.y),originalPosition.z);
        camera.transform.SetPositionAndRotation(originalPosition,source.transform.rotation);
    }
    /// <summary>바닥의 화면 비율과 렌즈 크기로 룸 중앙의 최종 착지 구도를 정한다.</summary>
    public void Configure(float landingViewSize,float floorViewport,Vector2 landingFloor)
    {
        floor=landingFloor; landingSize=landingViewSize>0 ? Mathf.Max(.1f,landingViewSize) : originalSize;
        float screenY=Mathf.Clamp(floorViewport,0,.45f);
        float y=floor.y+landingSize*(1-2*screenY);
        resting=new Vector3(room.center.x,Mathf.Max(visibleBottom+landingSize,y),originalPosition.z);
        IsSettled=false;
    }
    /// <summary>원래 바닥뷰에서 착지 구도로 보간한 뒤, 해제될 때까지 그 앵커를 유지한다.</summary>
    public void UpdateLanding(float weight,Vector3 shake,float progress)
    {
        if(camera==null || brain==null) return;
        OverrideWeight=weight;
        float normalized=Mathf.Clamp01(progress),t=Mathf.SmoothStep(0,1,normalized);
        size=Mathf.Lerp(originalSize,landingSize,t);
        Vector3 focus=Vector3.Lerp(originalPosition,resting,t);
        LeadOffset=focus.y-originalPosition.y;
        focus+=shake;
        float halfWidth=size*Mathf.Max(.1f,source.aspect);
        float x=room.size.x<=halfWidth*2 ? room.center.x : Mathf.Clamp(focus.x,room.min.x+halfWidth,room.max.x-halfWidth);
        camera.Lens.OrthographicSize=size;
        // 진동 뒤에도 가로 경계와 동적 하한을 적용해 플랫폼 하부 노출을 막는다.
        camera.transform.position=new Vector3(x,Mathf.Max(MinimumY,focus.y),originalPosition.z);
        id=brain.SetCameraOverride(id,100,null,camera,weight,-1);
        IsSettled=normalized>=.999f;
    }
    public void Dispose()
    {
        if(brain!=null && id>=0) brain.ReleaseCameraOverride(id); id=-1;
        if(camera!=null) Object.Destroy(camera.gameObject);
    }
}
