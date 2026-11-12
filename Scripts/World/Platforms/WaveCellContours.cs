using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 공유 변을 상쇄해 연결된 블록의 실제 경계만 만든다. 방향의 왼쪽은 항상 고체다.
public static class WaveCellContours
{
    public static List<Vector2[]> Trace(IEnumerable<Vector2Int> cells,float inset=0,bool simplify=true)
    {
        var edges=new HashSet<(Vector2Int a,Vector2Int b)>();
        void Edge(Vector2Int a,Vector2Int b){if(!edges.Remove((b,a)))edges.Add((a,b));}
        foreach(var p in cells.Distinct())
        {Edge(p,p+Vector2Int.right);Edge(p+Vector2Int.right,p+Vector2Int.one);Edge(p+Vector2Int.one,p+Vector2Int.up);Edge(p+Vector2Int.up,p);}
        var result=new List<Vector2[]>();
        // 수천 칸 덩어리도 한 번에 따라가도록 남은 변을 시작점으로 찾는다(변마다 전체를 다시 훑지 않는다).
        var byStart=new Dictionary<Vector2Int,List<(Vector2Int a,Vector2Int b)>>();
        foreach(var e in edges){if(!byStart.TryGetValue(e.a,out var list))byStart[e.a]=list=new List<(Vector2Int a,Vector2Int b)>(2);list.Add(e);}
        // 외곽선은 가장 아래·왼쪽 변부터 시작한다(선의 시작 위치가 늘 같아 발광 무늬가 흔들리지 않는다).
        var ordered=edges.OrderBy(e=>e.a.y).ThenBy(e=>e.a.x).ThenBy(e=>e.b.y).ThenBy(e=>e.b.x).ToList();int cursor=0;
        while(edges.Count>0)
        {
            while(!edges.Contains(ordered[cursor]))cursor++;
            var edge=ordered[cursor];var start=edge.a;var points=new List<Vector2Int>();
            while(true)
            {
                points.Add(edge.a);edges.Remove(edge);byStart[edge.a].Remove(edge);if(edge.b==start)break;
                var incoming=edge.b-edge.a;if(!byStart.TryGetValue(edge.b,out var candidates)||candidates.Count==0)break;
                int Turn((Vector2Int a,Vector2Int b) e){var d=e.b-e.a;int cross=incoming.x*d.y-incoming.y*d.x;return cross>0?0:incoming.x*d.x+incoming.y*d.y>0?1:cross<0?2:3;}
                edge=candidates.OrderBy(Turn).First();
            }
            var corners=points.Where((p,i)=>{var a=p-points[(i+points.Count-1)%points.Count];var b=points[(i+1)%points.Count]-p;return !simplify || a.x*b.y-a.y*b.x!=0;}).Select(p=>(Vector2)p).ToArray();
            if(corners.Length<3)continue;
            var shifted=new Vector2[corners.Length];
            for(int i=0;i<corners.Length;i++)
            {
                var a=(corners[i]-corners[(i+corners.Length-1)%corners.Length]).normalized;
                var b=(corners[(i+1)%corners.Length]-corners[i]).normalized;
                var na=new Vector2(-a.y,a.x);var nb=new Vector2(-b.y,b.x);
                shifted[i]=corners[i]+inset*(na+nb)/Mathf.Max(.001f,1+Vector2.Dot(na,nb));
            }
            result.Add(shifted);
        }
        return result;
    }
}
