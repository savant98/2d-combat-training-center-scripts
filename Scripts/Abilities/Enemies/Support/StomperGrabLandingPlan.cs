using UnityEngine;

// 연출과 실제 모터가 같은 중앙 착지점을 사용한다. 위치를 직접 변경하지 않는다.
public struct StomperGrabLandingPlan
{
    public Vector2 PlayerRoot, BossRoot, Core, Floor;
    public Bounds RoomBounds;
    public float Radius, ContactDegrees;
    public Collider2D Platform;
    public static bool TryCreate(EnemyController boss,PlayerController player,Vector3 core,float initialAngle,float fixedRadius,out StomperGrabLandingPlan plan)
    {
        plan=default;
        var motor=boss.Motor; Bounds bounds;
        if(!EnemyCameraBounds.TryGet(out bounds))
        {
            var left=motor.RaycastEnvironment(core,Vector2.left,100);
            var right=motor.RaycastEnvironment(core,Vector2.right,100);
            if(left.collider==null || right.collider==null || left.point.x>=right.point.x) return false;
            bounds=new Bounds(new Vector3((left.point.x+right.point.x)*.5f,core.y,0),new Vector3(right.point.x-left.point.x,20,1));
        }
        float x=bounds.center.x;
        var floor=motor.RaycastEnvironment(new Vector2(x,Mathf.Max(core.y,bounds.center.y)),Vector2.down,100);
        if(floor.collider==null || floor.normal.y<.5f || floor.distance<.02f)
            floor=motor.RaycastEnvironment(new Vector2(x,boss.transform.position.y+.1f),Vector2.down,100);
        if(floor.collider==null || floor.normal.y<.5f || floor.distance<.02f || floor.point.y<bounds.min.y-.5f || floor.point.y>bounds.max.y) return false;
        var body=boss.model.transform.Find("ModelBorderMask").GetComponent<SpriteRenderer>().bounds.extents;
        var collider=player.GetComponent<Collider2D>(); float playerSupport=collider.bounds.extents.y;
        for(float degrees=initialAngle;degrees<=75;degrees+=5)
        {
            float angle=degrees*Mathf.Deg2Rad,s=Mathf.Sin(angle),c=Mathf.Cos(angle);
            float supportX=body.x*c+body.y*s,supportY=body.x*s+body.y*c;
            float radius=fixedRadius;
            if(radius*s+playerSupport<supportY+.2f) continue;
            float maximum=(bounds.extents.x-supportX-.25f)/c;
            if(radius>maximum || radius>9) continue;
            Vector2 pivot=new Vector2(x-boss.Facing*radius*c,floor.point.y+playerSupport+radius*s);
            if(pivot.y+supportY>bounds.max.y-.1f) continue;
            var offset=(Vector2)core-(Vector2)boss.transform.position;
            plan=new StomperGrabLandingPlan { Floor=new Vector2(x,floor.point.y), Core=pivot, Radius=radius, ContactDegrees=degrees,
                RoomBounds=bounds, Platform=floor.collider, BossRoot=pivot-offset,
                PlayerRoot=new Vector2(x,floor.point.y-(collider.bounds.min.y-player.transform.position.y)+.015f) };
            return true;
        }
        return false;
    }
}
