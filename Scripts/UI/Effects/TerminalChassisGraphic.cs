using UnityEngine;

/// <summary>불투명한 청색 합금 표면에 베벨·명암·가공 홈을 그린다.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TerminalChassisGraphic : UnityEngine.UI.MaskableGraphic
{
    [SerializeField] private Color upper=new Color32(28,64,88,255),lower=new Color32(10,27,43,255),bevel=new Color32(62,109,132,255);
    [SerializeField] private float cornerCut=38,bevelWidth=7;
    [SerializeField] private int grooveCount=160;
    private readonly Vector2[] outer=new Vector2[8],inner=new Vector2[8];
    public override Texture mainTexture=>Texture2D.whiteTexture;
    public void Configure(Color top,Color bottom,Color rim,float cut=38,float inset=7,int grooves=160)
    {upper=top;lower=bottom;bevel=rim;cornerCut=Mathf.Max(0,cut);bevelWidth=Mathf.Max(.5f,inset);grooveCount=Mathf.Max(0,grooves);color=Color.white;raycastTarget=false;SetVerticesDirty();}
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();var r=GetPixelAdjustedRect();if(r.width<=0||r.height<=0)return;
        float inset=Mathf.Min(bevelWidth,Mathf.Min(r.width,r.height)*.25f),cut=Mathf.Min(cornerCut,Mathf.Min(r.width,r.height)*.5f);
        var face=new Rect(r.x+inset,r.y+inset,r.width-inset*2,r.height-inset*2);float innerCut=Mathf.Max(0,cut-inset*.5857864f);
        Corners(r,cut,outer);Corners(face,innerCut,inner);
        // 표면은 완전히 채우고 가공 흔적만 낮은 투명도로 겹친다.
        int center=vh.currentVertCount;vh.AddVert(face.center,Surface(face.center,face),Vector2.zero);
        for(int i=0;i<8;i++)vh.AddVert(inner[i],Surface(inner[i],face),Vector2.zero);
        for(int i=0;i<8;i++)vh.AddTriangle(center,center+i+1,center+(i+1)%8+1);
        for(int i=0;i<8;i++)
        {
            int next=(i+1)%8;var mid=(outer[i]+outer[next])*.5f;float light=Mathf.Clamp01(.5f+(mid.y-r.center.y)/r.height);
            var rim=Tint(Color.Lerp(lower*.6f,bevel,light));Quad(vh,outer[i],outer[next],inner[next],inner[i],rim,rim,Surface(inner[next],face),Surface(inner[i],face));
        }
        for(int i=0;i<grooveCount;i++)
        {
            float y=Mathf.Lerp(face.yMin,face.yMax,(i+.5f)/Mathf.Max(1,grooveCount));float edge=Mathf.Max(0,innerCut-Mathf.Min(y-face.yMin,face.yMax-y));
            float left=face.xMin+edge+3,right=face.xMax-edge-3;if(right<=left)continue;
            float length=Mathf.Min(Mathf.Lerp(24,140,Hash(i+17)),right-left),x=Mathf.Lerp(left,right-length,Hash(i+71));
            var scratch=Tint(i%4==0?bevel:lower*.5f,i%4==0?.07f:.12f);
            Quad(vh,new Vector2(x,y-.35f),new Vector2(x+length,y-.35f),new Vector2(x+length,y+.35f),new Vector2(x,y+.35f),scratch,scratch,scratch,scratch);
        }
    }
    private Color Surface(Vector2 p,Rect r)=>Tint(Color.Lerp(lower,upper,Mathf.InverseLerp(r.yMin,r.yMax,p.y)));
    private Color Tint(Color v,float opacity=1)=>new Color(v.r*color.r,v.g*color.g,v.b*color.b,color.a*opacity);
    private static float Hash(int v){unchecked{uint n=(uint)v*747796405u+2891336453u;n=(n^(n>>16))*2246822519u;return(n&65535u)/65535f;}}
    private static void Corners(Rect r,float cut,Vector2[] p)
    {cut=Mathf.Min(cut,Mathf.Min(r.width,r.height)*.5f);p[0]=new Vector2(r.xMin+cut,r.yMin);p[1]=new Vector2(r.xMax-cut,r.yMin);p[2]=new Vector2(r.xMax,r.yMin+cut);p[3]=new Vector2(r.xMax,r.yMax-cut);p[4]=new Vector2(r.xMax-cut,r.yMax);p[5]=new Vector2(r.xMin+cut,r.yMax);p[6]=new Vector2(r.xMin,r.yMax-cut);p[7]=new Vector2(r.xMin,r.yMin+cut);}
    private static void Quad(UnityEngine.UI.VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color ca,Color cb,Color cc,Color cd)
    {int i=vh.currentVertCount;vh.AddVert(a,ca,Vector2.zero);vh.AddVert(b,cb,Vector2.zero);vh.AddVert(c,cc,Vector2.zero);vh.AddVert(d,cd,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);}
}
