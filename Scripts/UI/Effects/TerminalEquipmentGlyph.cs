using UnityEngine;

/// <summary>공통 좌표계와 선 굵기로 그리는 기본 장비 조작 아이콘.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TerminalEquipmentGlyph : UnityEngine.UI.MaskableGraphic
{
    public enum Glyph { Move, Jump, Dash, Attack, Swap, Gear, Secondary }
    [SerializeField] private Glyph glyph;
    [SerializeField] private float thickness=2.8f;
    private Rect drawingRect;
    private float unitScale;
    public override Texture mainTexture=>Texture2D.whiteTexture;
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
        }
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
