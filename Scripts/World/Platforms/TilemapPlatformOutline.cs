using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Lobby 등 고정 Tilemap도 동적 셀과 같은 안쪽 테두리 규칙을 따른다.
[RequireComponent(typeof(Tilemap))]
public sealed class TilemapPlatformOutline : MonoBehaviour
{
    private Tilemap map;
    private readonly List<LineRenderer> lines=new();
    private bool wasVisible;
    private bool Visible
    {
        get { var renderer=map != null?map.GetComponent<TilemapRenderer>():null;return renderer != null && renderer.enabled && map.color.a>0; }
    }
    private void OnEnable() { map=GetComponent<Tilemap>();Tilemap.tilemapTileChanged+=Changed;Rebuild(); }
    private void OnDisable() { Tilemap.tilemapTileChanged-=Changed;foreach(var line in lines) if(line!=null) line.enabled=false; }
    private void Changed(Tilemap changed,Tilemap.SyncTile[] tiles) { if(changed==map) Rebuild(); }
    private void LateUpdate() { if(Visible != wasVisible) Rebuild(); }
    public void Rebuild()
    {
        var rules=WaveRoomRulesSO.Default;if(rules==null)return;
        wasVisible=Visible;
        if(!wasVisible){foreach(var line in lines)line.enabled=false;return;}
        var cells=new List<Vector2Int>();
        foreach(var p in map.cellBounds.allPositionsWithin)
            if(map.HasTile(p) && map.GetColor(p).a>0) cells.Add((Vector2Int)p);
        var contours=WaveCellContours.Trace(cells,rules.terrainLineInset);
        var source=map.GetComponent<TilemapRenderer>();
        for(int i=0;i<contours.Count;i++)
        {
            if(i==lines.Count)
            {
                var node=new GameObject("AutomaticTerrainLine");node.transform.SetParent(transform,false);
                lines.Add(node.AddComponent<LineRenderer>());
            }
            var line=lines[i];line.useWorldSpace=false;line.loop=true;line.sharedMaterial=rules.terrainLineMaterial;
            line.widthMultiplier=rules.terrainLineWidth;line.sortingLayerID=source.sortingLayerID;line.sortingOrder=source.sortingOrder;
            var points=contours[i];line.positionCount=points.Length;
            for(int j=0;j<points.Length;j++)
                line.SetPosition(j,map.CellToLocalInterpolated(new Vector3(points[j].x,points[j].y,0)));
            line.enabled=true;
        }
        for(int i=contours.Count;i<lines.Count;i++)lines[i].enabled=false;
    }
}
