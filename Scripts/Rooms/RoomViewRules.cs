using System.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Tilemaps;

// 원본 가상 카메라 설정을 유지하고 최종 출력과 하단 마감에만 공통 규칙을 적용한다.
[DisallowMultipleComponent]
public sealed class RoomViewRules : MonoBehaviour
{
    private Room room;
    private SpriteRenderer blinder;
    private float blinderDepth;
    private GameObject blinderContent;
    private GameObject outlinedContent;
    // 방 내용의 훈련 구간 목록. 방 아래에는 지형 칸과 반복 배경 모듈까지 수만 개의 오브젝트가 있어 한 번 훑는 데 여러 ms가 든다.
    // 구간은 방 내용(Prefab)의 고정 구성원이므로 내용이 바뀔 때만 다시 찾는다.
    private GameObject stagedContent;
    private bool stagesFound;
    private TrainingCourseStage[] stages=System.Array.Empty<TrainingCourseStage>();
    public BoxCollider2D CurrentBounds { get; private set; }
    public SpriteRenderer Blinder => blinder;

    private void Awake() => room=GetComponent<Room>();

    /// <summary>이 방 내용에 놓인 훈련 구간 목록. 구간이 필요한 곳은 방을 직접 훑지 말고 이것을 쓴다.</summary>
    public TrainingCourseStage[] CourseStages()
    {
        if(room == null) room=GetComponent<Room>();
        if(room.ContentRoot != stagedContent || !stagesFound)
        {
            stagedContent=room.ContentRoot;
            stagesFound=true;
            stages=room.GetComponentsInChildren<TrainingCourseStage>(true);
        }
        return stages;
    }
    /// <summary>방의 훈련 구간 목록. 방이 없으면 빈 목록이다.</summary>
    public static TrainingCourseStage[] CourseStagesIn(Room room)
    {
        if(room == null) return System.Array.Empty<TrainingCourseStage>();
        var rules=room.GetComponent<RoomViewRules>();
        return rules != null ? rules.CourseStages() : room.GetComponentsInChildren<TrainingCourseStage>(true);
    }
    private void OnEnable() => CinemachineCore.CameraUpdatedEvent.AddListener(Apply);
    private void OnDisable() => CinemachineCore.CameraUpdatedEvent.RemoveListener(Apply);

    private static CinemachineCamera Leaf(ICinemachineCamera camera)
    {
        while(camera is NestedBlendSource nested) camera=nested.Blend.CamB;
        return camera as CinemachineCamera;
    }
    public BoxCollider2D ResolveBounds(CinemachineBrain brain)
    {
        var camera=Leaf(brain.ActiveBlend != null ? brain.ActiveBlend.CamB : brain.ActiveVirtualCamera);
        var confiner=camera != null ? camera.GetComponent<CinemachineConfiner2D>() : null;
        if(confiner != null && confiner.BoundingShape2D is BoxCollider2D own) return own;
        foreach(var stage in CourseStages())
                if(stage != null && camera != null && stage.RevealCamera==camera)
                {
                    var course=stage.CourseCamera != null ? stage.CourseCamera.GetComponent<CinemachineConfiner2D>() : null;
                    if(course != null && course.BoundingShape2D is BoxCollider2D bounds) return bounds;
                    if(stage.RoomBounds is BoxCollider2D stageBounds) return stageBounds;
                }
        return room.VirtualCamera != null ? room.VirtualCamera.GetComponent<CinemachineConfiner2D>()?.BoundingShape2D as BoxCollider2D : null;
    }
    private void Apply(CinemachineBrain brain)
    {
        if(room == null || RoomManager.Instance == null || RoomManager.Instance.CurrentRoom != room || brain.OutputCamera == null) return;
        CurrentBounds=ResolveBounds(brain);
        if(CurrentBounds == null) return;
        Constrain(brain.OutputCamera,CurrentBounds);
        ApplyBlinder(CurrentBounds);
        ApplyTerrainOutlines();
    }

    // Collider.bounds는 물리 동기화보다 늦을 수 있어 현재 Transform·offset·size로 계산한다.
    public static void Constrain(Camera camera,BoxCollider2D bounds)
    {
        if(!camera.orthographic) return;
        var frame=bounds.transform;
        var right=frame.InverseTransformVector(camera.transform.right*camera.orthographicSize*camera.aspect);
        var up=frame.InverseTransformVector(camera.transform.up*camera.orthographicSize);
        var extent=new Vector2(Mathf.Abs(right.x)+Mathf.Abs(up.x),Mathf.Abs(right.y)+Mathf.Abs(up.y));
        var half=bounds.size*.5f;
        var snap=camera.GetComponent<CinemachinePixelPerfectSnap>();
        float pixel=snap != null && snap.pixelsPerUnit>0 ? .5f/snap.pixelsPerUnit : 0;
        var pixelX=frame.InverseTransformVector(Vector3.right*pixel);
        var pixelY=frame.InverseTransformVector(Vector3.up*pixel);
        var margin=new Vector2(Mathf.Abs(pixelX.x)+Mathf.Abs(pixelY.x),Mathf.Abs(pixelX.y)+Mathf.Abs(pixelY.y))+Vector2.one*.001f;
        float fit=Mathf.Min((half.x-margin.x)/Mathf.Max(.0001f,extent.x),(half.y-margin.y)/Mathf.Max(.0001f,extent.y));
        // 특이한 화면 비율에서도 빈 공간을 노출하지 않는다. 저장된 VCam Lens는 바꾸지 않는다.
        if(fit<1) { camera.orthographicSize*=Mathf.Max(.001f,fit); extent*=Mathf.Max(.001f,fit); }
        var point=frame.InverseTransformPoint(camera.transform.position);
        var range=Vector2.Max(Vector2.zero,half-extent-margin);
        var safe=point;
        safe.x=Mathf.Clamp(point.x,bounds.offset.x-range.x,bounds.offset.x+range.x);
        safe.y=Mathf.Clamp(point.y,bounds.offset.y-range.y,bounds.offset.y+range.y);
        camera.transform.position+=frame.TransformVector(safe-point);
    }
    public void ApplyBlinder(BoxCollider2D bounds)
    {
        var rules=WaveRoomRulesSO.Default;
        if(rules == null || rules.blinderSprite == null || room.ContentRoot == null) return;
        if(blinderContent != room.ContentRoot)
        {
            // Destroy가 지연된 이전 콘텐츠의 Sprite를 새 콘텐츠로 옮기지 않는다.
            blinder=null;blinderContent=room.ContentRoot;
        }
        if(blinder == null)
        {
            blinder=room.ContentRoot.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(r=>r.name.EndsWith("RoomFadeSprite") || r.name=="LobbyFadeSprite" || r.name=="FloorShadow" || r.name=="AutomaticFloorBlinder");
            if(blinder == null)
            {
                var node=new GameObject("AutomaticFloorBlinder");
                blinder=node.AddComponent<SpriteRenderer>();blinder.sortingOrder=1;
            }
            blinder.sprite=rules.blinderSprite;blinder.sharedMaterial=rules.blinderMaterial;
            blinderDepth=blinder.transform.position.z;
        }
        float rows=Mathf.Min(WaveRoomRulesSO.BlinderRows,bounds.size.y);
        blinder.transform.SetParent(bounds.transform,false);
        float localDepth=bounds.transform.InverseTransformPoint(new Vector3(bounds.transform.position.x,bounds.transform.position.y,blinderDepth)).z;
        blinder.transform.localPosition=new Vector3(bounds.offset.x,bounds.offset.y-bounds.size.y*.5f+rows*.5f,localDepth);
        blinder.transform.localRotation=Quaternion.Euler(0,0,90);
        var size=blinder.sprite.bounds.size;
        blinder.transform.localScale=new Vector3(rows/size.x,bounds.size.x/size.y,1);
        blinder.enabled=true;
    }
    private void ApplyTerrainOutlines()
    {
        if(room.ContentRoot==null || outlinedContent==room.ContentRoot) return;
        outlinedContent=room.ContentRoot;
        if(outlinedContent.GetComponent<WaveCellTerrain>()!=null) return;
        foreach(var line in outlinedContent.GetComponentsInChildren<LineRenderer>(true))
            if(line.name=="PlatformLine" || line.name.StartsWith("PlatformLine (")) line.enabled=false;
        foreach(var map in outlinedContent.GetComponentsInChildren<Tilemap>(true))
        {
            var renderer=map.GetComponent<TilemapRenderer>();
            if(map.GetComponent<TilemapCollider2D>() != null && renderer != null && renderer.enabled && map.color.a>0 && map.GetComponent<TilemapPlatformOutline>()==null)
                map.gameObject.AddComponent<TilemapPlatformOutline>();
        }
    }
}
