using UnityEngine;

/// <summary>공통 좌표계와 선 굵기로 그리는 기본 장비 조작 아이콘.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TerminalEquipmentGlyph : UnityEngine.UI.MaskableGraphic
{
    // 직렬화된 정수 값을 지키기 위해 새 아이콘은 항상 끝에 붙인다(Shield=7, Breakable=8, Break=9).
    public enum Glyph { Move, Jump, Dash, Attack, Swap, Gear, Secondary, Shield, Breakable, Break }
    [SerializeField] private Glyph glyph;
    [SerializeField] private float thickness=2.8f;
    private Rect drawingRect;
    private float unitScale;
    public Glyph Current=>glyph;
    public override Texture mainTexture=>Texture2D.whiteTexture;

    // 쉴드 아이콘 세 개가 함께 쓰는 방패 윤곽(80칸 좌표계). 위 가운데가 살짝 솟고 아래는 둥글게 모여 끝이 뾰족하다.
    private static readonly Vector2[] ShieldOutline=BuildShield();
    // 금 간 방패의 금(위 테두리에서 아래로 내려가는 지그재그)과 부서진 방패가 갈라지는 선은 같은 꺾임을 쓴다.
    private static readonly Vector2[] Crack={new Vector2(2,29.67f),new Vector2(-5,15),new Vector2(5,4),new Vector2(-4,-9),new Vector2(2,-20)};
    private static readonly Vector2[] LeftShard=BuildShard(true), RightShard=BuildShard(false);

    public void Configure(Glyph value,Color tint,float stroke=2.8f)
    {glyph=value;color=tint;thickness=Mathf.Max(.5f,stroke);raycastTarget=false;SetVerticesDirty();}
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();drawingRect=GetPixelAdjustedRect();unitScale=Mathf.Min(drawingRect.width,drawingRect.height)/80;
        if(unitScale<=0)return;
        switch(glyph)
        {
            case Glyph.Move:
                Arrow(vh,new Vector2(-3,0),new Vector2(-28,0));Arrow(vh,new Vector2(3,0),new Vector2(28,0));
                Line(vh,new Vector2(-19,-20),new Vector2(19,-20));Line(vh,new Vector2(-19,20),new Vector2(19,20));break;
            case Glyph.Jump:
                Line(vh,new Vector2(-23,-28),new Vector2(23,-28));Arrow(vh,new Vector2(0,-19),new Vector2(0,10),10);
                Line(vh,new Vector2(-10,17),new Vector2(0,27));Line(vh,new Vector2(0,27),new Vector2(10,17));break;
            case Glyph.Dash:
                Arrow(vh,new Vector2(-12,0),new Vector2(29,0),12);Line(vh,new Vector2(-29,14),new Vector2(-4,14));
                Line(vh,new Vector2(-33,0),new Vector2(-20,0));Line(vh,new Vector2(-29,-14),new Vector2(-4,-14));break;
            case Glyph.Attack:
                Blade(vh,new Vector2(-25,-25),new Vector2(25,25));Blade(vh,new Vector2(25,-25),new Vector2(-25,25));break;
            case Glyph.Swap:
                Line(vh,new Vector2(-27,-6),new Vector2(-27,15));Arrow(vh,new Vector2(-27,15),new Vector2(25,15),9);
                Line(vh,new Vector2(27,6),new Vector2(27,-15));Arrow(vh,new Vector2(27,-15),new Vector2(-25,-15),9);break;
            case Glyph.Gear:
                Ring(vh,19,12);Ring(vh,6,8);
                for(int i=0;i<8;i++){float a=i*Mathf.PI/4;var d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));Line(vh,d*20,d*29);}break;
            case Glyph.Secondary:
                Plate(vh,Vector2.zero,new Vector2(28,38),7);Plate(vh,new Vector2(-26,0),new Vector2(10,20),3);Plate(vh,new Vector2(26,0),new Vector2(10,20),3);
                Line(vh,new Vector2(-21,0),new Vector2(-14,0));Line(vh,new Vector2(14,0),new Vector2(21,0));
                Line(vh,new Vector2(4,11),new Vector2(-5,-1));Line(vh,new Vector2(-5,-1),new Vector2(5,-1));Line(vh,new Vector2(5,-1),new Vector2(-4,-12));break;
            case Glyph.Shield:
                // 쉴드: 방패 윤곽과 안쪽의 작은 방패(겹 장갑).
                Outline(vh,ShieldOutline,Vector2.zero,1,0,true);Outline(vh,ShieldOutline,new Vector2(0,1),.56f,0,true);break;
            case Glyph.Breakable:
                // 브레이크 가능 상태: 위 테두리에서 시작해 아래로 번지는 금이 간 방패.
                Outline(vh,ShieldOutline,Vector2.zero,1,0,true);Outline(vh,Crack,Vector2.zero,1,0,false);
                Line(vh,new Vector2(5,4),new Vector2(13,-1));Line(vh,new Vector2(-4,-9),new Vector2(-12,-13));break;
            case Glyph.Break:
                // 브레이크 상태: 금을 따라 두 조각으로 갈라져 벌어진 방패와 바깥으로 뻗는 충격선.
                Outline(vh,LeftShard,new Vector2(-6,-2),.76f,8,true);Outline(vh,RightShard,new Vector2(6,-4),.76f,-8,true);
                foreach(float degrees in new[]{90f,48f,132f,232f,308f})
                {float a=degrees*Mathf.Deg2Rad;var d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));Line(vh,d*32,d*38.5f);}
                break;
        }
    }
    private static Vector2 Bezier(Vector2 a,Vector2 control,Vector2 b,float t)
    {float u=1-t;return u*u*a+2*u*t*control+t*t*b;}
    private static Vector2[] BuildShield()
    {
        var points=new System.Collections.Generic.List<Vector2>{new Vector2(-24,26),new Vector2(0,30),new Vector2(24,26),new Vector2(24,4)};
        for(int i=1;i<=6;i++)points.Add(Bezier(new Vector2(24,4),new Vector2(23,-18),new Vector2(0,-32),i/6f));
        for(int i=1;i<6;i++)points.Add(Bezier(new Vector2(0,-32),new Vector2(-23,-18),new Vector2(-24,4),i/6f));
        points.Add(new Vector2(-24,4));
        return points.ToArray();
    }
    // 방패를 금(위 2,29.67 → 아래 끝 0,-32)으로 나눈 한쪽 조각의 닫힌 윤곽.
    private static Vector2[] BuildShard(bool left)
    {
        var points=new System.Collections.Generic.List<Vector2>{new Vector2(2,29.67f)};
        if(left){points.Add(new Vector2(0,30));points.Add(new Vector2(-24,26));points.Add(new Vector2(-24,4));}
        else{points.Add(new Vector2(24,26));points.Add(new Vector2(24,4));}
        float side=left?-1:1;
        for(int i=1;i<6;i++)points.Add(Bezier(new Vector2(24*side,4),new Vector2(23*side,-18),new Vector2(0,-32),i/6f));
        points.Add(new Vector2(0,-32));points.Add(new Vector2(-4,-9));points.Add(new Vector2(5,4));points.Add(new Vector2(-5,15));
        return points.ToArray();
    }
    private void Outline(UnityEngine.UI.VertexHelper vh,Vector2[] points,Vector2 offset,float scale,float degrees,bool closed)
    {
        float a=degrees*Mathf.Deg2Rad,cos=Mathf.Cos(a),sin=Mathf.Sin(a);
        Vector2 Place(Vector2 p){p*=scale;return new Vector2(p.x*cos-p.y*sin,p.x*sin+p.y*cos)+offset;}
        int count=closed?points.Length:points.Length-1;
        for(int i=0;i<count;i++)Line(vh,Place(points[i]),Place(points[(i+1)%points.Length]));
    }
    private void Arrow(UnityEngine.UI.VertexHelper vh,Vector2 from,Vector2 to,float head=8)
    {Line(vh,from,to);var d=(to-from).normalized;var n=new Vector2(-d.y,d.x);Line(vh,to-d*head+n*head,to);Line(vh,to-d*head-n*head,to);}
    private void Blade(UnityEngine.UI.VertexHelper vh,Vector2 from,Vector2 to)
    {var d=(to-from).normalized;var n=new Vector2(-d.y,d.x);Line(vh,from,to);var guard=from+d*11;Line(vh,guard-n*7,guard+n*7);Line(vh,to-d*9+n*4,to);Line(vh,to-d*9-n*4,to);}
    private void Ring(UnityEngine.UI.VertexHelper vh,float radius,int sides)
    {for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;Line(vh,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius);}}
    private void Plate(UnityEngine.UI.VertexHelper vh,Vector2 center,Vector2 size,float cut)
    {
        var h=size*.5f;var p=new[]{new Vector2(-h.x+cut,-h.y),new Vector2(h.x-cut,-h.y),new Vector2(h.x,-h.y+cut),new Vector2(h.x,h.y-cut),new Vector2(h.x-cut,h.y),new Vector2(-h.x+cut,h.y),new Vector2(-h.x,h.y-cut),new Vector2(-h.x,-h.y+cut)};
        for(int i=0;i<p.Length;i++)Line(vh,center+p[i],center+p[(i+1)%p.Length]);
    }
    private void Line(UnityEngine.UI.VertexHelper vh,Vector2 a,Vector2 b)
    {
        var delta=b-a;if(delta.sqrMagnitude<.0001f)return;var n=new Vector2(-delta.y,delta.x).normalized*thickness*.5f;int i=vh.currentVertCount;
        vh.AddVert(Map(a-n),color,Vector2.zero);vh.AddVert(Map(a+n),color,Vector2.zero);vh.AddVert(Map(b+n),color,Vector2.zero);vh.AddVert(Map(b-n),color,Vector2.zero);
        vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
    }
    private Vector2 Map(Vector2 p)=>drawingRect.center+p*unitScale;
}
