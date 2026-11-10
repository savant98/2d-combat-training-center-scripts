using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum WaveCellKind { Platform, OneWayPlatform }

[Serializable]
public struct WaveMapCell : IEquatable<WaveMapCell>
{
    public Vector2Int position;
    public WaveCellKind kind;
    public int group;
    public int style;
    public bool Equals(WaveMapCell other) => position == other.position && kind == other.kind && group == other.group && style == other.style;
    public override bool Equals(object value) => value is WaveMapCell other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(position, kind, group, style);
}

[Serializable]
public sealed class WaveCellGroup
{
    public string path;
    public Vector2 origin;
    public Vector2 openOffset;
    public string courseKey;
    public float delay;
    public float duration = 1.6f;
    public bool persistAfterWave;
    public bool afterHandoff;
}

[Serializable]
public sealed class WaveCellStyle
{
    public UnityEngine.Tilemaps.TileBase tile;
    public Material material;
    public Color color = Color.white;
    public Matrix4x4 transform = Matrix4x4.identity;
    public int sortingLayer;
    public int sortingOrder;
    public int layer = -1;
    public bool generated;
}

[Serializable]
public sealed class WaveCellMap
{
    public bool enabled;
    public WaveCellStyle[] styles = Array.Empty<WaveCellStyle>();
    public WaveTerrainCellView platformPrefab;
    public WaveTerrainCellView oneWayPrefab;
    public WaveMapCell[] cells = Array.Empty<WaveMapCell>();
    public WaveCellGroup[] groups = Array.Empty<WaveCellGroup>();
}

[Serializable]
public sealed class WaveCellBatch
{
    public string label;
    public float time;
    public float duration = .5f;
    public bool hasAnimationPivot;
    public Vector2 animationPivot;
    public float lifetime;
    public bool keepAfterWave;
    public WaveMapCell[] cells = Array.Empty<WaveMapCell>();
}

// 편집기와 실제 맵은 이 스냅샷을 공유한다. Prefab의 숨은 Tilemap을 다시 합치지 않는다.
public static class WaveCellState
{
    public static WaveMapCell[] Initial(WaveDataSO data, int index)
    {
        var result = data.cellMap.cells;
        for (int i = 0; i <= index && i < data.waves.Length; i++)
            if (data.waves[i].overrideCellMap) result = data.waves[i].cells;
        return result;
    }

    public static Dictionary<Vector2Int, WaveMapCell> At(WaveDataSO data, int index, float time)
        => AllAt(data, index, time).GroupBy(c => c.position).ToDictionary(g => g.Key, g => g.Last());

    public static WaveMapCell[] AllAt(WaveDataSO data, int index, float time, Func<WaveMapCell, Vector2Int> presented = null)
    {
        var initial = new HashSet<WaveMapCell>(Initial(data, index));
        var initialKeys = new HashSet<(Vector2Int,int)>(initial.Select(c=>(c.position,c.group)));
        var result = new HashSet<WaveMapCell>();
        for(int w=0;w<index;w++)
        {
            var previousInitial=new HashSet<WaveMapCell>(Initial(data,w));
            var keys=new HashSet<(Vector2Int,int)>(previousInitial.Select(c=>(c.position,c.group)));
            result.RemoveWhere(c=>keys.Contains((c.position,c.group)));
            var history=new List<(float time,int order,Action apply)>();int sequence=0;
            void Carry(WaveMapCell cell,bool keep)
            {
                result.RemoveWhere(c=>c.position==cell.position&&c.group==cell.group);
                if(keep)result.Add(cell);
            }
            foreach(var batch in data.waves[w].cellBatches)
                history.Add((batch.time,sequence++,()=>{foreach(var cell in batch.cells)if(!previousInitial.Contains(cell))Carry(cell,batch.keepAfterWave);}));
            foreach(var entry in data.waves[w].terrainEvents ?? Array.Empty<WaveTerrainEvent>())
            {
                if(entry.custom)continue;
                history.Add((entry.time,sequence++,()=>
                {
                    foreach(var position in entry.cells)
                        if(entry.operation==WaveTerrainOperation.Remove)result.RemoveWhere(c=>c.position==position);
                        else Carry(new WaveMapCell{position=position,kind=entry.cellKind,style=entry.cellStyle},entry.keepAfterWave);
                }));
            }
            foreach(var action in history.OrderBy(a=>a.time).ThenBy(a=>a.order))action.apply();
        }
        result.RemoveWhere(c=>initialKeys.Contains((c.position,c.group)));
        result.UnionWith(initial);
        var owners = new Dictionary<WaveMapCell, int>();
        var actions = new List<(float time, int order, Action apply)>();
        int order = 0, generation = 0;
        bool Generated(WaveMapCell cell) => cell.style >= 0 && cell.style < data.cellMap.styles.Length && data.cellMap.styles[cell.style].generated;
        void Add(WaveMapCell cell,int token)
        {
            foreach(var old in result.Where(c=>c.position==cell.position && c.group==cell.group && !c.Equals(cell)
                && !(initial.Contains(c) && (initial.Contains(cell) || Generated(cell) && c.kind==cell.kind))).ToArray()) { result.Remove(old);owners.Remove(old); }
            result.Add(cell); owners[cell]=token;
        }
        foreach(var batch in data.waves[index].cellBatches)
        {
            if(batch.time>time)continue;
            int token=++generation;var cells=batch.cells.Where(c=>!initial.Contains(c)).ToArray();
            actions.Add((batch.time,order++,()=>{foreach(var cell in cells)Add(cell,token);}));
            if(batch.lifetime>0&&batch.time+batch.lifetime<=time)
                actions.Add((batch.time+batch.lifetime,order++,()=>{foreach(var cell in cells)if(owners.TryGetValue(cell,out int current)&&current==token){result.Remove(cell);owners.Remove(cell);}}));
        }
        foreach(var entry in data.waves[index].terrainEvents??Array.Empty<WaveTerrainEvent>())
        {
            if(entry.time>time||entry.custom)continue;
            int token=++generation;
            actions.Add((entry.time,order++,()=>
            {
                foreach(var position in entry.cells)
                    if(entry.operation==WaveTerrainOperation.Remove)
                    {
                        foreach(var cell in result.Where(c=>(presented!=null?presented(c):c.position)==position).ToArray()){result.Remove(cell);owners.Remove(cell);}
                    }
                    else Add(new WaveMapCell{position=position,kind=entry.cellKind,style=entry.cellStyle},token);
            }));
            if(entry.operation==WaveTerrainOperation.Add && entry.lifetime>0 && entry.time+entry.lifetime<=time)
                actions.Add((entry.time+entry.lifetime,order++,()=>
                {
                    foreach(var position in entry.cells)
                    {
                        var cell=new WaveMapCell{position=position,kind=entry.cellKind,style=entry.cellStyle};
                        if(owners.TryGetValue(cell,out int current)&&current==token){result.Remove(cell);owners.Remove(cell);}
                    }
                }));
        }
        foreach(var action in actions.OrderBy(a=>a.time).ThenBy(a=>a.order)) action.apply();
        return result.ToArray();
    }
}
