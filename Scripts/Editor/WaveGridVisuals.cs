using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// 팔레트와 배치 항목이 같은 이미지·크기 정보를 사용한다.
public static class WavePrefabVisuals
{
    public enum Category { Enemy, Platform, Other }
    private static readonly Dictionary<int, Sprite> sprites = new();
    private static readonly Dictionary<int, Rect> previewUVs = new();
    public static void Invalidate() { sprites.Clear();previewUVs.Clear(); }
    public static bool IsPaletteItem(GameObject prefab) => prefab != null && (prefab.GetComponent<EnemyController>() != null || prefab.GetComponent<WaveTerrainCellView>() != null);

    public static Category Classify(GameObject prefab)
    {
        if (prefab == null) return Category.Other;
        if (prefab.GetComponent<EnemyController>() != null) return Category.Enemy;
        if (prefab.GetComponent<WaveTerrainCellView>() != null) return Category.Platform;
        return Category.Other;
    }

    public static Sprite SpriteFor(GameObject prefab)
    {
        if (prefab == null) return null;
        int id = prefab.GetInstanceID();
        if (sprites.TryGetValue(id, out var cached)) return cached;
        var enemy = prefab.GetComponent<EnemyController>();
        var root = enemy != null && enemy.model != null ? enemy.model : prefab;
        var renderer = root.GetComponentsInChildren<SpriteRenderer>(true).Where(r => VisibleModelRenderer(r,root.transform))
            .OrderByDescending(r => r.sprite.bounds.size.sqrMagnitude).FirstOrDefault();
        Sprite result = renderer != null ? renderer.sprite : null;
        if (result == null)
            foreach (var map in prefab.GetComponentsInChildren<Tilemap>(true))
            {
                foreach (var cell in map.cellBounds.allPositionsWithin)
                    if (map.GetSprite(cell) != null) { result = map.GetSprite(cell); break; }
                if (result != null) break;
            }
        return sprites[id] = result;
    }

    private static bool VisibleModelRenderer(Renderer renderer,Transform root)
    {
        if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer is TrailRenderer) return false;
        for(var node=renderer.transform;node!=null;node=node.parent)
        { if(!node.gameObject.activeSelf)return false;if(node==root)break; }
        if(renderer is SpriteRenderer sprite && (sprite.sprite==null || sprite.color.a<=0)) return false;
        if(renderer is LineRenderer line && (line.positionCount<2 || line.widthMultiplier<=0 || line.startColor.a<=0 && line.endColor.a<=0))return false;
        return renderer is SpriteRenderer || renderer is MeshRenderer || renderer is SkinnedMeshRenderer || renderer is LineRenderer;
    }
    public static Bounds LocalBounds(GameObject prefab, bool collision = false)
    {
        if (prefab == null) return new Bounds(Vector3.zero, Vector3.one);
        var box=prefab.GetComponent<BoxCollider2D>();var capsule=prefab.GetComponent<CapsuleCollider2D>();
        if(collision && (box!=null || capsule!=null))
        {
            var scale=(Vector2)prefab.transform.localScale;
            return new Bounds(Vector2.Scale(box!=null?box.offset:capsule.offset,scale),Vector2.Scale(box!=null?box.size:capsule.size,new Vector2(Mathf.Abs(scale.x),Mathf.Abs(scale.y))));
        }
        var enemy=prefab.GetComponent<EnemyController>();var root=enemy!=null && enemy.model!=null?enemy.model:prefab;
        bool found=false;var result=new Bounds();
        foreach(var renderer in root.GetComponentsInChildren<Renderer>(true).Where(r=>VisibleModelRenderer(r,root.transform)))
        {
            var matrix=Matrix4x4.Scale(prefab.transform.localScale)*prefab.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
            var bounds=renderer.localBounds;
            foreach(float x in new[]{bounds.min.x,bounds.max.x})foreach(float y in new[]{bounds.min.y,bounds.max.y})foreach(float z in new[]{bounds.min.z,bounds.max.z})
            {
                var point=matrix.MultiplyPoint3x4(new Vector3(x,y,z));
                if(!found){result=new Bounds(point,Vector3.zero);found=true;}else result.Encapsulate(point);
            }
        }
        return found && result.size.x>0 && result.size.y>0?result:collision?new Bounds(Vector3.zero,Vector3.one):LocalBounds(prefab,true);
    }
    public static Vector2 Placement(GameObject prefab, Vector2 point)
    {
        var shape = LocalBounds(prefab, true);
        return new Vector2(Mathf.Floor(point.x) + .5f - shape.center.x, Mathf.Floor(point.y) - shape.min.y);
    }
    public static void DrawCell(Rect rect, WaveMapCell cell, WaveCellMap map)
    {
        if (cell.kind == WaveCellKind.OneWayPlatform)
        {
            DrawOneWay(rect,map.oneWayPrefab.gameObject); return;
        }
        if (map.styles.Length==0) { EditorGUI.DrawRect(rect,new Color(.16f,.65f,.36f)); return; }
        var style=map.styles[Mathf.Clamp(cell.style,0,map.styles.Length-1)]; var tile=style.tile as Tile;
        if(tile==null||tile.sprite==null)return;
        var sprite=tile.sprite; var size=new Vector2(Mathf.Abs(style.transform.m00)*sprite.bounds.size.x,Mathf.Abs(style.transform.m11)*sprite.bounds.size.y)*rect.width;
        var imageRect=new Rect(rect.center-size*.5f,size); var pixels=sprite.textureRect;
        var uv=new Rect(pixels.x/sprite.texture.width,pixels.y/sprite.texture.height,pixels.width/sprite.texture.width,pixels.height/sprite.texture.height);
        var previous=GUI.color;GUI.color=style.color*tile.color;GUI.DrawTextureWithTexCoords(imageRect,sprite.texture,uv,true);GUI.color=previous;
    }
    private static void DrawOneWay(Rect rect,GameObject prefab)
    {
            var material = prefab.GetComponentsInChildren<LineRenderer>(true).FirstOrDefault()?.sharedMaterial;
            Color color = material != null && material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : new Color(1,.45f,.05f);
            color /= Mathf.Max(1,Mathf.Max(color.r,Mathf.Max(color.g,color.b))); color.a=1;
            Handles.BeginGUI(); Handles.color=color;
            Vector3 Point(float x,float y) => new Vector3(rect.x+rect.width*x,rect.y+rect.height*y);
            Handles.DrawAAPolyLine(2,Point(0,0),Point(1,0));
            Handles.DrawAAPolyLine(2,Point(.025f,.035f),Point(.975f,.035f),Point(.925f,.2f),Point(.075f,.2f),Point(.025f,.035f));
            Handles.EndGUI();
    }
    private static Rect PreviewUV(Texture2D texture)
    {
        int id=texture.GetInstanceID();if(previewUVs.TryGetValue(id,out var cached))return cached;
        Texture2D readable=texture;Texture2D copy=null;RenderTexture target=null;var previous=RenderTexture.active;
        try
        {
            if(!texture.isReadable)
            {
                target=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32);
                Graphics.Blit(texture,target);RenderTexture.active=target;
                copy=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);
                copy.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);readable=copy;
            }
            var pixels=readable.GetPixels32();int width=texture.width,height=texture.height;
            bool transparent=pixels.Any(p=>p.a<=2);int minX=width,minY=height,maxX=-1,maxY=-1;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                var p=pixels[y*width+x];var left=pixels[y*width];var right=pixels[y*width+width-1];
                bool visible=transparent?p.a>2:Mathf.Abs(p.r-(left.r+right.r)*.5f)>8||Mathf.Abs(p.g-(left.g+right.g)*.5f)>8||Mathf.Abs(p.b-(left.b+right.b)*.5f)>8;
                if(!visible)continue;minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);minY=Mathf.Min(minY,y);maxY=Mathf.Max(maxY,y);
            }
            // Unity 썸네일의 정사각 여백을 제거해 긴 모델도 실제 월드 크기로 표시한다.
            return previewUVs[id]=maxX>=minX?Rect.MinMaxRect((float)minX/width,(float)minY/height,(float)(maxX+1)/width,(float)(maxY+1)/height):new Rect(0,0,1,1);
        }
        catch(UnityException){return new Rect(0,0,1,1);}
        finally{RenderTexture.active=previous;if(target!=null)RenderTexture.ReleaseTemporary(target);if(copy!=null)UnityEngine.Object.DestroyImmediate(copy);}
    }
    public static bool Draw(Rect rect, GameObject prefab, bool actualSize = false)
    {
        if (prefab == null) return false;
        var cell=prefab.GetComponent<WaveTerrainCellView>();
        if(cell!=null&&cell.kind==WaveCellKind.OneWayPlatform) { DrawOneWay(rect,prefab);return true; }
        // 격자와 팔레트에서 같은 복합 Prefab 미리보기를 사용한다. 실제 크기는 호출부의 월드 Bounds가 정한다.
        var preview = cell!=null ? null : AssetPreview.GetAssetPreview(prefab);
        if (preview != null)
        {
            var uv=PreviewUV(preview);var image=rect;
            if(!actualSize)
            {
                var size=new Vector2(preview.width*uv.width,preview.height*uv.height);size*=Mathf.Min(rect.width/size.x,rect.height/size.y);
                image=new Rect(rect.center-size*.5f,size);
            }
            GUI.DrawTextureWithTexCoords(image,preview,uv,true);return true;
        }
        var sprite = SpriteFor(prefab);
        if (sprite != null)
        {
            try
            {
                Rect pixels = sprite.textureRect;
                float scale = Mathf.Min(rect.width / pixels.width, rect.height / pixels.height);
                var fitted = new Rect(rect.center - pixels.size * scale * .5f, pixels.size * scale);
                var uv = new Rect(pixels.x / sprite.texture.width, pixels.y / sprite.texture.height,
                    pixels.width / sprite.texture.width, pixels.height / sprite.texture.height);
                GUI.DrawTextureWithTexCoords(fitted, sprite.texture, uv, true);
                return true;
            }
            catch (UnityException) { }
        }
        GUI.DrawTexture(rect, AssetPreview.GetMiniThumbnail(prefab), ScaleMode.ScaleToFit, true);
        return false;
    }
}

// 저장된 실제 타일과 공통 생성기의 외곽 규칙을 Room 좌표 셀로 해석한다.
public sealed class WaveGridGeometry
{
    private TrainingRoomContent cachedTemplate;
    private HashSet<Vector2Int> cachedBase;
    public void Invalidate() { cachedTemplate = null; cachedBase = null; }

    public HashSet<Vector2Int> Cells(WaveDataSO data, int index, float time)
    {
        if (data.cellMap.enabled)
            return new HashSet<Vector2Int>(TypedCells(data, index, time).Keys);
        var layout = WaveRoomState.LayoutAt(data, index);
        var result = new HashSet<Vector2Int>();
        var template = data.roomTemplate;
        var assessment = template != null ? template.GetComponent<TrainingRoomLayoutController>() : null;
        if (data.customRoom && template != null && assessment == null)
        {
            if (cachedTemplate != template || cachedBase == null)
            {
                cachedTemplate = template;
                cachedBase = new HashSet<Vector2Int>();
                ProjectTilemaps(template.gameObject, template.transform.worldToLocalMatrix, cachedBase);
            }
            result.UnionWith(cachedBase);
        }
        else Shell(layout.bounds, result);
        if (assessment != null && !layout.paintPlatforms && layout.platformCells.Length > 0)
        {
            result.UnionWith(FixedPlatformCells(template));
        }
        else result.UnionWith(layout.platformCells);
        result.ExceptWith(layout.removedCells);

        var wave = data.waves[index];
        var times = WaveAuthoring.Times(wave);
        var changes = new List<(float time, int order, Action apply)>();
        for (int i = 0; i < (wave.spawnObjects?.Length ?? 0); i++)
        {
            var spawn = wave.spawnObjects[i];
            if (spawn.prefab == null || WavePrefabVisuals.Classify(spawn.prefab) != WavePrefabVisuals.Category.Platform
                || times[i] > time || spawn.lifetime > 0 && time >= times[i] + spawn.lifetime) continue;
            var matrix = Matrix4x4.TRS(spawn.spawnPosition + layout.spawnOffset, Quaternion.identity, spawn.prefab.transform.localScale) * spawn.prefab.transform.worldToLocalMatrix;
            changes.Add((times[i], i, () => ProjectTilemaps(spawn.prefab, matrix, result)));
        }
        changes.Add((0, 100000, () => result.ExceptWith(layout.removedCells)));
        int order = 200000;
        foreach (var entry in wave.terrainEvents ?? Array.Empty<WaveTerrainEvent>())
        {
            if (entry.time > time || entry.custom) continue;
            changes.Add((entry.time, order++, () =>
            {
                if (entry.operation == WaveTerrainOperation.Add) result.UnionWith(entry.cells);
                else result.ExceptWith(entry.cells);
            }));
        }
        foreach (var change in changes.OrderBy(row => row.time).ThenBy(row => row.order)) change.apply();
        return result;
    }
    public Dictionary<Vector2Int, WaveCellKind> TypedCells(WaveDataSO data, int index, float time)
        => StyledCells(data, index, time).ToDictionary(row => row.Key, row => row.Value.kind);
    public Dictionary<Vector2Int, WaveMapCell> StyledCells(WaveDataSO data, int index, float time)
    {
        var offsets = Enumerable.Range(1, data.cellMap.groups.Length).ToDictionary(i => i,
            i => PresentedCell(data, index, time, new WaveMapCell { group = i }));
        var result = new Dictionary<Vector2Int, WaveMapCell>();
        Vector2Int Position(WaveMapCell c) => c.position + (offsets.TryGetValue(c.group, out var offset) ? offset : Vector2Int.zero);
        foreach (var cell in WaveCellState.AllAt(data, index, time, Position))
        {
            var position=Position(cell);
            bool invisible=cell.style>=0&&cell.style<data.cellMap.styles.Length&&data.cellMap.styles[cell.style].color.a<=0;
            if (!invisible||!result.ContainsKey(position)) result[position]=cell;
        }
        return result;
    }

    public static Vector2Int PresentedCell(WaveDataSO data, int index, float time, WaveMapCell cell)
    {
        if (cell.group <= 0 || cell.group > data.cellMap.groups.Length) return cell.position;
        var group = data.cellMap.groups[cell.group - 1];
        for (int waveIndex = 0; waveIndex <= index; waveIndex++)
        {
            var wave = data.waves[waveIndex]; var times = WaveAuthoring.Times(wave);
            for (int i = 0; i < (wave.spawnObjects?.Length ?? 0); i++)
            {
                var prefab = wave.spawnObjects[i].prefab;
                if (prefab == null || prefab.GetComponent<TrainingCourseActivator>() == null) continue;
                var key = new SerializedObject(prefab.GetComponent<TrainingCourseActivator>()).FindProperty("courseKey").stringValue;
                if (key != group.courseKey) continue;
                if (waveIndex < index && group.persistAfterWave) return cell.position + Vector2Int.RoundToInt(group.openOffset);
                if (waveIndex == index && !group.afterHandoff)
                {
                    float t = Mathf.Clamp01((time - times[i] - group.delay) / Mathf.Max(.001f, group.duration));
                    return cell.position + Vector2Int.RoundToInt(group.openOffset * Mathf.SmoothStep(0, 1, t));
                }
            }
        }
        return cell.position;
    }

    public static HashSet<Vector2Int> FixedPlatformCells(TrainingRoomContent template)
    {
        var result = new HashSet<Vector2Int>();
        foreach (var guid in AssetDatabase.FindAssets("t:WaveDataSO", new[] { "Assets/Data/Waves" }))
        {
            var data=AssetDatabase.LoadAssetAtPath<WaveDataSO>(AssetDatabase.GUIDToAssetPath(guid));
            if(data.roomTemplate==template && data.cellMap.enabled)
                result.UnionWith(data.cellMap.cells.Where(c=>c.kind==WaveCellKind.OneWayPlatform || data.cellMap.styles[c.style].generated).Select(c=>c.position));
        }
        return result;
    }

    public static void Shell(Rect bounds, HashSet<Vector2Int> result)
    {
        int left = Mathf.FloorToInt(bounds.xMin), right = Mathf.CeilToInt(bounds.xMax);
        int bottom = Mathf.FloorToInt(bounds.yMin), top = Mathf.CeilToInt(bounds.yMax);
        if (right - left > 256 || top - bottom > 256) return;
        for (int x = left - 3; x < right + 3; x++)
            for (int y = bottom - 3; y < top + 3; y++)
                if (x < left || x >= right || y < bottom || y >= top) result.Add(new Vector2Int(x, y));
    }

    public static void ProjectTilemaps(GameObject root, Matrix4x4 toRoom, HashSet<Vector2Int> result)
    {
        foreach (var map in root.GetComponentsInChildren<Tilemap>(true))
        {
            if (map.GetComponent<TilemapCollider2D>() == null) continue;
            var matrix = toRoom * map.transform.localToWorldMatrix;
            foreach (var cell in map.cellBounds.allPositionsWithin)
            {
                if (!map.HasTile(cell)) continue;
                Vector3 center = Vector3.Scale(cell, map.cellSize) + map.tileAnchor;
                var tile = map.GetTransformMatrix(cell);
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
                foreach (var corner in new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) })
                {
                    Vector2 point = matrix.MultiplyPoint3x4(center + tile.MultiplyPoint3x4(corner));
                    min = Vector2.Min(min, point); max = Vector2.Max(max, point);
                }
                for (int x = Mathf.FloorToInt(min.x + .001f); x < Mathf.CeilToInt(max.x - .001f); x++)
                    for (int y = Mathf.FloorToInt(min.y + .001f); y < Mathf.CeilToInt(max.y - .001f); y++) result.Add(new Vector2Int(x, y));
            }
        }
        foreach (var platform in root.GetComponentsInChildren<OneWayPlatform>(true))
        {
            var box = platform.GetComponent<BoxCollider2D>();
            var matrix = toRoom * box.transform.localToWorldMatrix;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var corner in new[] { new Vector2(-.5f, -.5f), new Vector2(.5f, -.5f), new Vector2(.5f, .5f), new Vector2(-.5f, .5f) })
            {
                Vector2 point = matrix.MultiplyPoint3x4(box.offset + Vector2.Scale(box.size, corner));
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            for (int x = Mathf.FloorToInt(min.x + .001f); x < Mathf.CeilToInt(max.x - .001f); x++)
                for (int y = Mathf.FloorToInt(min.y + .001f); y < Mathf.CeilToInt(max.y - .001f); y++) result.Add(new Vector2Int(x, y));
        }
    }
}
