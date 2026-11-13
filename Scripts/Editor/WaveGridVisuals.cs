using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// 팔레트와 배치 항목이 같은 이미지·크기 정보를 사용한다.
public static class WavePrefabVisuals
{
    public enum Category { Enemy, Platform, Other, Goal }
    private static readonly Dictionary<int, Sprite> sprites = new();
    private static readonly Dictionary<int, Rect> previewUVs = new();
    private static readonly Dictionary<(int, bool), Bounds> boundsCache = new();
    public static void Invalidate() { sprites.Clear();previewUVs.Clear();boundsCache.Clear();ownVisuals.Clear();WaveDevicePreviewCache.Invalidate(); }
    public static bool IsEnvironmentTool(GameObject prefab) => prefab != null && prefab.GetComponent<WaveEnvironmentTool>() != null;
    public static bool IsPaletteItem(GameObject prefab) => prefab != null && (prefab.GetComponent<EnemyController>() != null || prefab.GetComponent<WaveTerrainCellView>() != null || IsEnvironmentTool(prefab));
    // 한 칸보다 큰 환경 도구는 표식 배경과 선택 영역을 놓인 칸 하나로 줄여 아래 지형을 가리지 않는다.
    public static bool UsesCompactMarker(GameObject prefab) => prefab != null && prefab.TryGetComponent<WaveEnvironmentTool>(out var tool) && tool.CompactMarker;
    // 같은 칸 중복 금지에 드는 환경 장치. 칸을 차지하지 않는 표시 장치(길 안내 화살표)는 다른 장치와 같은 칸에 놓을 수 있다.
    public static bool OccupiesCells(GameObject prefab) => prefab != null && prefab.TryGetComponent<WaveEnvironmentTool>(out var tool) && tool.OccupiesCells;
    // 환경 장치가 차지하는 칸 수(회전 반영). Enemy와 지형은 한 칸이다.
    public static Vector2Int Footprint(GameObject prefab, int rotationSteps = 0)
    {
        if (prefab == null || !prefab.TryGetComponent<WaveEnvironmentTool>(out var tool)) return Vector2Int.one;
        var size = tool.Footprint;
        return (((rotationSteps % 4) + 4) % 4) % 2 == 1 ? new Vector2Int(size.y, size.x) : size;
    }
    // 장치 기준점(격자 좌표)이 차지하는 칸들. 홀수 축은 칸 중심, 짝수 축은 격자 꼭짓점에 기준점이 놓인다.
    public static RectInt Cells(Vector2 point, Vector2Int size) => new RectInt(Vector2Int.FloorToInt(point - (Vector2)size * .5f + Vector2.one * .5f), size);
    // 움직이는 환경 도구가 닿는 범위를 덧그린다. origin은 장치 원점의 화면 좌표, zoom은 한 칸의 픽셀 수다.
    public static bool DrawRange(GameObject prefab, int rotationSteps, Vector2 origin, float zoom, WaveDeviceSetting[] settings = null)
    {
        if (!IsEnvironmentTool(prefab) || !WaveDevicePreviewCache.TryGetRange(prefab, settings, out var tool) || tool.Shape == WaveEnvironmentTool.RangeShape.None) return false;
        var rotation = Quaternion.Euler(0, 0, rotationSteps * 90f);
        // 화면 Y축은 아래를 향한다.
        Vector3 Point(Vector2 local) { Vector2 world = rotation * (Vector3)local; return origin + new Vector2(world.x, -world.y) * zoom; }
        Vector3[] points;
        if (tool.Shape == WaveEnvironmentTool.RangeShape.Circle)
        {
            points = new Vector3[49];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / 48f;
                points[i] = Point(tool.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * tool.Size.x);
            }
        }
        else
        {
            var half = tool.Size * .5f;
            points = new[] { Point(tool.Center + new Vector2(-half.x, -half.y)), Point(tool.Center + new Vector2(half.x, -half.y)),
                Point(tool.Center + half), Point(tool.Center + new Vector2(-half.x, half.y)), Point(tool.Center + new Vector2(-half.x, -half.y)) };
        }
        var color = tool.Color;
        Handles.BeginGUI();
        try
        {
            Handles.color = new Color(color.r, color.g, color.b, color.a * .12f);
            Handles.DrawAAConvexPolygon(points.Take(points.Length - 1).ToArray());
            Handles.color = color;
            Handles.DrawAAPolyLine(2, points);
        }
        finally { Handles.EndGUI(); }
        return true;
    }

    public static Category Classify(GameObject prefab)
    {
        if (prefab == null) return Category.Other;
        if (prefab.GetComponent<TutorialGoal>() != null) return Category.Goal;
        if (prefab.GetComponent<EnemyController>() != null) return Category.Enemy;
        if (prefab.GetComponent<WaveTerrainCellView>() != null || IsEnvironmentTool(prefab)) return Category.Platform;
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
        bool persistent = EditorUtility.IsPersistent(prefab);
        if (persistent && boundsCache.TryGetValue((prefab.GetInstanceID(), collision), out var cachedBounds)) return cachedBounds;
        var computed = ComputeLocalBounds(prefab, collision);
        if (persistent) boundsCache[(prefab.GetInstanceID(), collision)] = computed;
        return computed;
    }
    private static Bounds ComputeLocalBounds(GameObject prefab, bool collision)
    {
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
        return found && result.size.x>0 && result.size.y>0?result:collision?new Bounds(Vector3.zero,Vector3.one):ComputeLocalBounds(prefab,true);
    }
    // 환경 장치는 배치 설정(톱날 길이 등)을 반영한 크기를 캐시에서 받는다.
    public static Bounds RotatedBounds(GameObject prefab, int rotationSteps, WaveDeviceSetting[] settings)
        => IsEnvironmentTool(prefab) ? WaveDevicePreviewCache.RotatedBounds(prefab, settings, ((rotationSteps % 4) + 4) % 4) : RotatedBounds(prefab, rotationSteps);
    public static Bounds RotatedBounds(GameObject prefab, int rotationSteps)
    {
        var source = LocalBounds(prefab);
        var rotation = Quaternion.Euler(0, 0, rotationSteps * 90f);
        var result = new Bounds(rotation * source.center, Vector3.zero);
        foreach (float x in new[] { source.min.x, source.max.x })
            foreach (float y in new[] { source.min.y, source.max.y })
                result.Encapsulate(rotation * new Vector3(x, y, 0));
        return result;
    }
    public static Vector2 Placement(GameObject prefab, Vector2 point, int rotationSteps = 0)
    {
        // 환경 도구의 회전 기준을 차지하는 칸들의 중심에 맞춘다. 한 칸이면 칸 중심, 2×2면 가장 가까운 격자 꼭짓점이다.
        if (IsEnvironmentTool(prefab))
        {
            var size = Footprint(prefab, rotationSteps);
            return new Vector2(size.x % 2 == 0 ? Mathf.Round(point.x) : Mathf.Floor(point.x) + .5f,
                size.y % 2 == 0 ? Mathf.Round(point.y) : Mathf.Floor(point.y) + .5f);
        }
        var shape = LocalBounds(prefab, true);
        return new Vector2(Mathf.Floor(point.x) + .5f - shape.center.x, Mathf.Floor(point.y) - shape.min.y);
    }
    public static void DrawCell(Rect rect, WaveMapCell cell, WaveCellMap map, int mask = 0)
    {
        // 스타일 전용 Prefab(시범 발판 등)이 자기 그림을 쓰면 실행 화면과 같게 그 그림을 그린다.
        var own = map.styles.Length > 0 ? map.styles[Mathf.Clamp(cell.style,0,map.styles.Length-1)].prefab : null;
        var source = own != null && own.kind == cell.kind ? own : cell.kind == WaveCellKind.OneWayPlatform ? map.oneWayPrefab : null;
        if (source != null && source.ownVisual && DrawOwnVisual(rect, source, mask)) return;
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
    // 전용 그림 셀의 그림 위치·색은 Prefab마다 한 번만 읽는다(다시 그릴 때마다 수백 칸에서 부품을 찾지 않게).
    private static readonly Dictionary<int, (SpriteRenderer image, Matrix4x4 toRoot, Color line, bool hasLine)> ownVisuals = new();
    // Prefab의 그림(이웃 조합 그림이 있으면 그것)을 셀 중심 기준 위치·크기 그대로 그린다. OneWay는 윗면 발광선도 덧그린다.
    private static bool DrawOwnVisual(Rect rect, WaveTerrainCellView view, int mask)
    {
        if (!ownVisuals.TryGetValue(view.GetInstanceID(), out var info))
        {
            var root = view.visualRoot != null ? view.visualRoot.transform : view.transform;
            var renderer = root.GetComponentInChildren<SpriteRenderer>(true);
            var line = root.GetComponentsInChildren<LineRenderer>(true).FirstOrDefault(l => l.name == "SurfaceLine");
            var material = line != null ? line.sharedMaterial : null;
            Color color = material != null && material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : new Color(1, .45f, .05f);
            color /= Mathf.Max(1, Mathf.Max(color.r, Mathf.Max(color.g, color.b))); color.a = 1;
            info = (renderer, renderer != null ? root.worldToLocalMatrix * renderer.transform.localToWorldMatrix : Matrix4x4.identity, color, line != null);
            ownVisuals[view.GetInstanceID()] = info;
        }
        var image = info.image;
        if (image == null) return false;
        var sprite = view.ConnectedSprite(mask) ?? image.sprite;
        if (sprite == null) return false;
        var bounds = sprite.bounds;
        Vector2 min = info.toRoot.MultiplyPoint3x4(bounds.min), max = info.toRoot.MultiplyPoint3x4(bounds.max);
        float unit = rect.width;
        var imageRect = Rect.MinMaxRect(rect.center.x + Mathf.Min(min.x, max.x) * unit, rect.center.y - Mathf.Max(min.y, max.y) * unit,
            rect.center.x + Mathf.Max(min.x, max.x) * unit, rect.center.y - Mathf.Min(min.y, max.y) * unit);
        var pixels = sprite.textureRect;
        var uv = new Rect(pixels.x / sprite.texture.width, pixels.y / sprite.texture.height, pixels.width / sprite.texture.width, pixels.height / sprite.texture.height);
        var previous = GUI.color; GUI.color = image.color; GUI.DrawTextureWithTexCoords(imageRect, sprite.texture, uv, true); GUI.color = previous;
        if (view.kind == WaveCellKind.OneWayPlatform && info.hasLine)
        {
            Handles.BeginGUI(); Handles.color = info.line;
            Handles.DrawAAPolyLine(2, new Vector3(rect.x, rect.y), new Vector3(rect.xMax, rect.y));
            Handles.EndGUI();
        }
        return true;
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
    // 환경 도구는 실제 Mesh·Sprite를 정면 합성한다. 비활성 Editor의 썸네일 생성 여부에 의존하지 않는다.
    private static bool DrawEnvironment(Rect rect, GameObject prefab, bool actualSize, int rotationSteps)
    {
        var bounds = RotatedBounds(prefab, rotationSteps);
        if (!actualSize)
        {
            var size = new Vector2(bounds.size.x, bounds.size.y);
            size *= Mathf.Min(rect.width / Mathf.Max(.001f, size.x), rect.height / Mathf.Max(.001f, size.y));
            rect = new Rect(rect.center - size * .5f, size);
        }
        Vector2 Point(Vector3 point) => new Vector2(rect.xMin + (point.x - bounds.min.x) / Mathf.Max(.001f, bounds.size.x) * rect.width,
            rect.yMax - (point.y - bounds.min.y) / Mathf.Max(.001f, bounds.size.y) * rect.height);
        var rotation = Matrix4x4.Rotate(Quaternion.Euler(0, 0, rotationSteps * 90f));
        var root = rotation * Matrix4x4.Scale(prefab.transform.localScale) * prefab.transform.worldToLocalMatrix;
        bool drawn = false;
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true)
            .Where(r => VisibleModelRenderer(r, prefab.transform))
            .OrderBy(r => SortingLayer.GetLayerValueFromID(r.sortingLayerID)).ThenBy(r => r.sortingOrder))
        {
            var transform = root * renderer.transform.localToWorldMatrix;
            if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
            {
                var mesh = filter.sharedMesh;
                var vertices = mesh.vertices;
                var materials = renderer.sharedMaterials;
                Handles.BeginGUI();
                try
                {
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    {
                        var material = submesh < materials.Length ? materials[submesh] : null;
                        Handles.color = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor")
                            : material != null && material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                        var triangles = mesh.GetTriangles(submesh);
                        for (int i = 0; i + 2 < triangles.Length; i += 3)
                            Handles.DrawAAConvexPolygon(Point(transform.MultiplyPoint3x4(vertices[triangles[i]])),
                                Point(transform.MultiplyPoint3x4(vertices[triangles[i + 1]])), Point(transform.MultiplyPoint3x4(vertices[triangles[i + 2]])));
                    }
                    drawn = true;
                }
                finally { Handles.EndGUI(); }
            }
            else if (renderer is SpriteRenderer sprite && sprite.sprite != null)
            {
                try
                {
                    var shape = sprite.sprite.bounds;
                    var topLeft = Point(transform.MultiplyPoint3x4(new Vector3(shape.min.x, shape.max.y)));
                    var right = Point(transform.MultiplyPoint3x4(new Vector3(shape.max.x, shape.max.y))) - topLeft;
                    var down = Point(transform.MultiplyPoint3x4(new Vector3(shape.min.x, shape.min.y))) - topLeft;
                    var matrix = Matrix4x4.identity;
                    matrix.SetColumn(0, new Vector4(right.x, right.y, 0, 0));
                    matrix.SetColumn(1, new Vector4(down.x, down.y, 0, 0));
                    matrix.SetColumn(3, new Vector4(topLeft.x, topLeft.y, 0, 1));
                    var pixels = sprite.sprite.textureRect;
                    var texture = sprite.sprite.texture;
                    var uv = new Rect(pixels.x / texture.width, pixels.y / texture.height, pixels.width / texture.width, pixels.height / texture.height);
                    if (sprite.flipX) { uv.x += uv.width; uv.width = -uv.width; }
                    if (sprite.flipY) { uv.y += uv.height; uv.height = -uv.height; }
                    var previousMatrix = GUI.matrix;
                    var previousColor = GUI.color;
                    try
                    {
                        GUI.matrix = previousMatrix * matrix;
                        GUI.color *= sprite.color;
                        GUI.DrawTextureWithTexCoords(new Rect(0, 0, 1, 1), texture, uv, true);
                        drawn = true;
                    }
                    finally { GUI.matrix = previousMatrix; GUI.color = previousColor; }
                }
                catch (UnityException) { }
            }
        }
        return drawn;
    }
    // 미리 그려 둔 텍스처를 실제 크기 또는 칸에 맞춘 크기로 놓는다. 다시 그릴 때 Mesh를 읽거나 삼각형을 그리지 않는다.
    private static bool DrawCachedEnvironment(Rect rect, GameObject prefab, bool actualSize, int turns, WaveDeviceSetting[] settings)
    {
        if (Event.current != null && Event.current.type != EventType.Repaint) return true;
        var bounds = WaveDevicePreviewCache.RotatedBounds(prefab, settings, turns);
        if (!actualSize)
        {
            var size = new Vector2(bounds.size.x, bounds.size.y);
            size *= Mathf.Min(rect.width / Mathf.Max(.001f, size.x), rect.height / Mathf.Max(.001f, size.y));
            rect = new Rect(rect.center - size * .5f, size);
        }
        float pixelsPerUnit = rect.width / Mathf.Max(.001f, bounds.size.x) * (EditorGUIUtility.pixelsPerPoint);
        var texture = WaveDevicePreviewCache.Picture(prefab, settings, turns, pixelsPerUnit, out var world);
        // 텍스처는 가장자리 여백을 포함하므로 같은 비율로 넓혀 놓는다.
        float sx = rect.width / Mathf.Max(.001f, bounds.size.x), sy = rect.height / Mathf.Max(.001f, bounds.size.y);
        var target = new Rect(rect.xMin + (world.xMin - bounds.min.x) * sx, rect.yMin + (bounds.max.y - world.yMax) * sy, world.width * sx, world.height * sy);
        GUI.DrawTexture(target, texture, ScaleMode.StretchToFill, true);
        return true;
    }
    public static bool Draw(Rect rect, GameObject prefab, bool actualSize = false, int rotationSteps = 0, WaveDeviceSetting[] settings = null)
    {
        if (prefab == null) return false;
        int turns = ((rotationSteps % 4) + 4) % 4;
        if (IsEnvironmentTool(prefab))
        {
            if (WaveDevicePreviewCache.Get(prefab, settings).HasSprites) return DrawEnvironment(rect, prefab, actualSize, turns);
            return DrawCachedEnvironment(rect, prefab, actualSize, turns, settings);
        }
        if (turns != 0)
        {
            var previous = GUI.matrix;
            var image = turns % 2 == 0 ? rect : new Rect(rect.center - new Vector2(rect.height, rect.width) * .5f, new Vector2(rect.height, rect.width));
            try
            {
                // 화면 Y축이 아래를 향하므로 월드의 반시계 회전은 음의 GUI 각도다.
                GUIUtility.RotateAroundPivot(-turns * 90f, rect.center);
                return Draw(image, prefab, actualSize);
            }
            finally { GUI.matrix = previous; }
        }
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
    private (int data, int dirty, int wave, float time) styledKey = (0, -1, -1, float.NaN);
    private Dictionary<Vector2Int, WaveMapCell> styledCache;
    private (int data, int dirty, int wave, float time) cellsKey = (0, -1, -1, float.NaN);
    private HashSet<Vector2Int> cellsCache;
    public void Invalidate() { cachedTemplate = null; cachedBase = null; styledCache = null; cellsCache = null; assemblyCache = null; }
    // 편집기 창이 다시 그릴 때마다 지형 상태(시작 맵·생성 묶음·예약·코스 이동)를 처음부터 계산하지 않도록,
    // WaveData가 바뀌거나(더티 횟수) Wave·편집 시각이 바뀔 때만 다시 계산한다. 돌려받은 값은 고치지 않는다.
    public Dictionary<Vector2Int, WaveMapCell> CachedStyledCells(WaveDataSO data, int index, float time)
    {
        var key = (data.GetInstanceID(), EditorUtility.GetDirtyCount(data), index, time);
        if (styledCache == null || !styledKey.Equals(key)) { styledCache = StyledCells(data, index, time); styledKey = key; }
        return styledCache;
    }
    // 칸이 속한 생성 묶음 번호(시작 맵·지형 예약 칸은 0). 실행 중 생성 묶음마다 외곽선·이어진 그림이 따로인 것과 같게 미리 보여 준다.
    public Dictionary<Vector2Int, int> CachedAssemblies(WaveDataSO data, int index, float time)
    {
        var key = (data.GetInstanceID(), EditorUtility.GetDirtyCount(data), index, time);
        if (assemblyCache != null && assemblyKey.Equals(key)) return assemblyCache;
        var result = new Dictionary<Vector2Int, int>();
        var initial = new HashSet<WaveMapCell>(WaveCellState.Initial(data, index));
        int id = 0;
        for (int w = 0; w <= index && w < data.waves.Length; w++)
            foreach (var batch in data.waves[w].cellBatches ?? Array.Empty<WaveCellBatch>())
            {
                id++;
                bool present = w == index ? batch.time <= time + .0005f : batch.keepAfterWave;
                if (!present) continue;
                foreach (var cell in batch.cells)
                    if (!initial.Contains(cell)) result[PresentedCell(data, index, time, cell)] = id;
            }
        assemblyCache = result; assemblyKey = key;
        return result;
    }
    private Dictionary<Vector2Int, int> assemblyCache;
    private (int, int, int, float) assemblyKey;
    public HashSet<Vector2Int> CachedCells(WaveDataSO data, int index, float time)
    {
        var key = (data.GetInstanceID(), EditorUtility.GetDirtyCount(data), index, time);
        if (cellsCache == null || !cellsKey.Equals(key)) { cellsCache = Cells(data, index, time); cellsKey = key; }
        return cellsCache;
    }

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
            var matrix = Matrix4x4.TRS(spawn.spawnPosition + layout.spawnOffset, spawn.Rotation, spawn.prefab.transform.localScale) * spawn.prefab.transform.worldToLocalMatrix;
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
                    // 코스 벽 열림은 Wave 시각 0 이전의 공통 준비 단계에서 끝난다.
                    return cell.position + Vector2Int.RoundToInt(group.openOffset);
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
