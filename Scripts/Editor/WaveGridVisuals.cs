using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// 팔레트와 배치 항목이 같은 이미지·크기 정보를 사용한다.
public static class WavePrefabVisuals
{
    // Prop·Dungeon은 뒤에 더한다(팔레트 탭 번호는 WaveEditorWindow.Palette에서 명시적으로 대응한다).
    public enum Category { Enemy, Platform, Other, Goal, Prop, Dungeon }
    private static readonly Dictionary<int, Sprite> sprites = new();
    private static readonly Dictionary<int, Rect> previewUVs = new();
    private static readonly Dictionary<(int, bool), Bounds> boundsCache = new();
    public static void Invalidate() { sprites.Clear();previewUVs.Clear();boundsCache.Clear();ownVisuals.Clear();dungeonProps.Clear();dungeonFacilities.Clear();arrivalSlots.Clear();WaveDevicePreviewCache.Invalidate();DungeonCranePaths.Invalidate();DungeonPropCatalogSO.Invalidate(); }
    public static bool IsEnvironmentTool(GameObject prefab) => prefab != null && prefab.GetComponent<WaveEnvironmentTool>() != null;
    // 4차: 출구 자리 표식(DungeonExitSlot)·생성 구역 표식(DungeonSpawnZone)도 WaveEnvironmentTool이 없어도 팔레트 항목이다(계약 타입).
    // 6차(T11): 도착 자리 표식(DungeonArrivalSlot — 영역 cutscene6가 만드는 타입, 이름으로 찾는다)도 팔레트 항목이다.
    public static bool IsPaletteItem(GameObject prefab) => prefab != null && (prefab.GetComponent<EnemyController>() != null || prefab.GetComponent<WaveTerrainCellView>() != null || IsEnvironmentTool(prefab) || IsDungeonDoor(prefab)
        || IsDungeonObjectiveSlot(prefab) || IsDungeonExitSlot(prefab) || IsDungeonArrivalSlot(prefab) || IsDungeonSpawnZone(prefab) || IsDungeonFacility(prefab) || IsDungeonProp(prefab));
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
    /// <summary>
    /// 6차(T17): 같은 칸 규칙에 드는 차지 칸. 길이 설정형(카탈로그 lengthKey — 컨베이어·장식 레일·분쇄 투입구·파괴 격자)은 회전 0·180°일 때
    /// 왼쪽 변을 고정하고 설정 길이만큼 오른쪽으로 차지한다(빌더 PropCells와 같다). 그 밖은 <see cref="Cells"/>와 같다.
    /// </summary>
    public static RectInt OccupiedCells(GameObject prefab, Vector2 point, int rotationSteps, WaveDeviceSetting[] settings)
    {
        var area = Cells(point, Footprint(prefab, rotationSteps));
        var entry = DungeonPropCatalogSO.Lookup(prefab);
        if (entry == null || !entry.HasLength || (((rotationSteps % 4) + 4) % 4) % 2 == 1) return area;
        int length = Mathf.RoundToInt(DungeonCranePaths.Value(prefab, settings, entry.lengthKey, area.width));
        if (length >= 1) area.width = length;
        return area;
    }
    // 움직이는 환경 도구가 닿는 범위를 덧그린다. origin은 장치 원점의 화면 좌표, zoom은 한 칸의 픽셀 수다.
    // 4차(DESIGN_V4 §2.3·2.4): 던전 크레인의 경로 다각선·쓸기 띠·붙기 반경 고리·고정 크레인 줄도 그린다(DungeonCranePaths).
    // 생성 구역 표식은 TryGetRange의 상자(종류별 색)로 그려진다. alpha는 흐린 표시(적 구성 편집 중 맵 층)에 쓴다.
    public static bool DrawRange(GameObject prefab, int rotationSteps, Vector2 origin, float zoom, WaveDeviceSetting[] settings = null, float alpha = 1f)
    {
        bool crane = DungeonCranePaths.Draw(prefab, origin, zoom, settings, alpha);
        // 통합 4차: 크레인 2종은 격자 Tool 범위(붙기 반경 원)를 Prefab에 두지만, 반경 고리는 위에서 점선으로 이미 그렸다 — 두 번 그리지 않는다.
        if (crane) return true;
        if (!IsEnvironmentTool(prefab) || !WaveDevicePreviewCache.TryGetRange(prefab, settings, out var tool) || tool.Shape == WaveEnvironmentTool.RangeShape.None) return crane;
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
        color.a *= Mathf.Clamp01(alpha);
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
        // 던전 출입구·시설·Prop도 루트에 WaveEnvironmentTool을 가지므로 환경 장치(Platform) 검사보다 먼저 본다.
        // 시설(텔레포트·크레인·컨베이어 등)은 루트에 DungeonProp도 있으므로 Prop 검사보다 먼저 '던전' 탭으로 보낸다.
        // 3차: 목표 자리 표식(DungeonObjectiveSlot)과 목표 레버(시설 이름 목록)도 '던전' 탭이다.
        // 4차: 출구 자리 표식(DungeonExitSlot)·생성 구역 표식(DungeonSpawnZone, 계약 타입)과 경로·고정 크레인·출구 문(시설 이름 목록)도 '던전' 탭이다.
        //      새 파괴형 Prop 6종(강화 상자 등)은 루트 DungeonProp이라 그대로 'Prop' 탭이다(DungeonPropBody는 Enemy가 아니다).
        if (IsDungeonDoor(prefab)) return Category.Dungeon;
        if (IsDungeonObjectiveSlot(prefab) || IsDungeonExitSlot(prefab) || IsDungeonArrivalSlot(prefab) || IsDungeonSpawnZone(prefab)) return Category.Dungeon;
        if (IsDungeonFacility(prefab)) return Category.Dungeon;
        if (IsDungeonProp(prefab)) return Category.Prop;
        if (prefab.GetComponent<TutorialGoal>() != null) return Category.Goal;
        if (prefab.GetComponent<EnemyController>() != null) return Category.Enemy;
        if (prefab.GetComponent<WaveTerrainCellView>() != null || IsEnvironmentTool(prefab)) return Category.Platform;
        return Category.Other;
    }
    // 던전 방 출입구(관 입구). 파생 클래스도 포함한다.
    public static bool IsDungeonDoor(GameObject prefab) => prefab != null && prefab.GetComponent<DungeonDoor>() != null;
    // 던전 텔레포트 중계기(계약 타입 DungeonTeleporter, 파생 포함).
    public static bool IsDungeonTeleporter(GameObject prefab) => prefab != null && prefab.GetComponent<DungeonTeleporter>() != null;
    // 방 클리어에 반응하는 던전 이동·상호작용 시설의 루트 컴포넌트 이름(영역 facilities). 런타임 시설 스크립트와 편집기를 따로
    // 컴파일·병합할 수 있게 타입 대신 이름(상위 타입 포함)으로 찾는다. 텔레포트는 계약 타입이라 따로 본다.
    // 3차(DESIGN_V3): 목표 레버(PropObjectiveLever)는 영역 facilities3가 만들며 계약 단계에 타입이 없으므로 같은 방식(컴포넌트 타입 이름 문자열)으로 분류한다.
    // 4차(DESIGN_V4 §5): 경로 크레인(PropCraneHook 재작성)·고정 크레인(PropCraneAnchor)·출구 문(PropExitGate)은 영역 facilities4·objective4가 만든다(이름 문자열).
    //   삭제한 화물 승강기(PropCargoLift)·차폐막 스위치(PropBarrierSwitch)·캐터펄트(PropCatapult)는 목록에서 뺐다 — 템플릿에 남아 있으면 검사 오류(DeletedFacilityName).
    public static readonly string[] DungeonFacilityComponents = { "PropCraneHook", "PropCraneAnchor", "PropCraneRail", "PropConveyor", "PropRepairStation", "PropShutterGate", "PropObjectiveLever", "PropExitGate" };
    /// <summary>4차에서 삭제한 시설의 컴포넌트 이름(DESIGN_V4 §5 삭제 목록).</summary>
    public static readonly string[] DeletedFacilityComponents = { "PropCargoLift", "PropBarrierSwitch", "PropCatapult" };
    // 삭제한 시설 Prefab 이름(스크립트가 지워져 컴포넌트가 비어도 이름으로 찾는다). 순서는 DeletedFacilityComponents와 같다.
    private static readonly string[] DeletedFacilityPrefabs = { "DungeonPropCargoLift", "DungeonPropBarrierSwitch", "DungeonPropCatapult" };
    /// <summary>삭제한 시설(승강기·차폐막 스위치·캐터펄트)이면 그 컴포넌트 이름, 아니면 null. 컴포넌트가 없으면 Prefab 이름으로 본다.</summary>
    public static string DeletedFacilityName(GameObject prefab)
    {
        if (prefab == null) return null;
        foreach (var component in prefab.GetComponents<Component>())
        {
            if (component == null) continue;
            for (var type = component.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                if (Array.IndexOf(DeletedFacilityComponents, type.Name) >= 0) return type.Name;
        }
        int index = Array.IndexOf(DeletedFacilityPrefabs, prefab.name);
        return index >= 0 ? DeletedFacilityComponents[index] : null;
    }
    // 던전 목표 자리 표식(계약 타입 DungeonObjectiveSlot). 실행 중에는 생성하지 않고, 격자 Tool에서는 점선 노란 실루엣으로 그린다.
    public static bool IsDungeonObjectiveSlot(GameObject prefab) => prefab != null && prefab.GetComponent<DungeonObjectiveSlot>() != null;
    /// <summary>목표 자리 표식의 점유 칸(WaveEnvironmentTool이 없으면 계약 크기 — 4차 뒤 벽 패널 4×6, DESIGN_V4 §2.5).</summary>
    public static readonly Vector2Int ObjectiveSlotSize = new Vector2Int(4, 6);
    public static Vector2Int ObjectiveSlotFootprint(GameObject prefab, int rotationSteps = 0)
    {
        var size = prefab != null && prefab.TryGetComponent<WaveEnvironmentTool>(out var tool) ? tool.Footprint : ObjectiveSlotSize;
        return (((rotationSteps % 4) + 4) % 4) % 2 == 1 ? new Vector2Int(size.y, size.x) : size;
    }
    // 4차: 시작 방 출구 자리 표식(계약 타입 DungeonExitSlot, 5×6 뒤 벽 문). 실행 중에는 생성하지 않는다.
    public static bool IsDungeonExitSlot(GameObject prefab) => prefab != null && prefab.GetComponent<DungeonExitSlot>() != null;
    /// <summary>출구 자리 표식의 점유 칸(WaveEnvironmentTool이 없으면 계약 크기 DungeonExitSlot.Size = 5×6).</summary>
    public static Vector2Int ExitSlotFootprint(GameObject prefab, int rotationSteps = 0)
    {
        var size = prefab != null && prefab.TryGetComponent<WaveEnvironmentTool>(out var tool) ? tool.Footprint : DungeonExitSlot.Size;
        return (((rotationSteps % 4) + 4) % 4) % 2 == 1 ? new Vector2Int(size.y, size.x) : size;
    }
    /// <summary>6차(T11): 다음 Wave 시작 방의 도착 문 자리 표식(DungeonArrivalSlot, 5×6 뒤 벽 — 출구 자리와 같은 규격). 타입은 영역 cutscene6가 만들어 이름으로 찾는다.</summary>
    public const string ArrivalSlotComponent = "DungeonArrivalSlot";
    public static readonly Vector2Int ArrivalSlotSize = new Vector2Int(5, 6);
    private static readonly Dictionary<int, bool> arrivalSlots = new();
    public static bool IsDungeonArrivalSlot(GameObject prefab)
    {
        if (prefab == null) return false;
        bool persistent = EditorUtility.IsPersistent(prefab);
        int id = prefab.GetInstanceID();
        if (persistent && arrivalSlots.TryGetValue(id, out bool cached)) return cached;
        bool result = false;
        foreach (var component in prefab.GetComponents<Component>())
        {
            if (component == null) continue;
            for (var type = component.GetType(); type != null && type != typeof(MonoBehaviour) && !result; type = type.BaseType)
                result = type.Name == ArrivalSlotComponent;
            if (result) break;
        }
        if (persistent) arrivalSlots[id] = result;
        return result;
    }
    public static Vector2Int ArrivalSlotFootprint(GameObject prefab, int rotationSteps = 0)
    {
        var size = prefab != null && prefab.TryGetComponent<WaveEnvironmentTool>(out var tool) ? tool.Footprint : ArrivalSlotSize;
        return (((rotationSteps % 4) + 4) % 4) % 2 == 1 ? new Vector2Int(size.y, size.x) : size;
    }
    /// <summary>목표·출구·도착 자리 표식(뒤 벽 실물이 설 자리)인지.</summary>
    public static bool IsDungeonSlot(GameObject prefab) => IsDungeonObjectiveSlot(prefab) || IsDungeonExitSlot(prefab) || IsDungeonArrivalSlot(prefab);
    /// <summary>목표·출구·도착 자리 표식의 점유 칸(종류에 맞는 계약 크기).</summary>
    public static Vector2Int SlotFootprint(GameObject prefab, int rotationSteps = 0) => IsDungeonExitSlot(prefab) ? ExitSlotFootprint(prefab, rotationSteps)
        : IsDungeonArrivalSlot(prefab) ? ArrivalSlotFootprint(prefab, rotationSteps) : ObjectiveSlotFootprint(prefab, rotationSteps);
    // 4차: 적 생성 구역 표식(계약 타입 DungeonSpawnZone). 점유 1×1, 넓이는 배치 설정(spawnZoneWidth·Height)으로 오른쪽·위로 넓어진다.
    public static bool IsDungeonSpawnZone(GameObject prefab) => DungeonSpawnZone.IsZone(prefab);
    private static readonly Dictionary<int, string> dungeonFacilities = new();
    // 루트에서 찾은 시설 컴포넌트 이름(DungeonFacilityComponents 중 하나). 텔레포트면 "DungeonTeleporter", 시설이 아니면 null.
    public static string DungeonFacilityName(GameObject prefab)
    {
        if (prefab == null) return null;
        if (IsDungeonTeleporter(prefab)) return nameof(DungeonTeleporter);
        bool persistent = EditorUtility.IsPersistent(prefab);
        int id = prefab.GetInstanceID();
        if (persistent && dungeonFacilities.TryGetValue(id, out string cached)) return cached;
        string result = null;
        foreach (var component in prefab.GetComponents<Component>())
        {
            if (component == null) continue;
            for (var type = component.GetType(); type != null && type != typeof(MonoBehaviour) && result == null; type = type.BaseType)
                if (Array.IndexOf(DungeonFacilityComponents, type.Name) >= 0) result = type.Name;
            if (result != null) break;
        }
        if (persistent) dungeonFacilities[id] = result;
        return result;
    }
    // 던전 시설(텔레포트 + 크레인 훅·레일·컨베이어·화물 승강기·수리대·셔터 게이트). 팔레트 '던전' 탭에 출입구와 함께 보인다.
    public static bool IsDungeonFacility(GameObject prefab) => DungeonFacilityName(prefab) != null;
    // 던전 Prop: 루트에 DungeonProp 계열(이름으로 찾는다 — 런타임 Prop 스크립트와 편집기를 따로 컴파일·병합할 수 있게)
    // 또는 Prop 계약(IDungeonPropTarget·IDungeonBreakable)을 구현한 컴포넌트가 있다. 출입구는 먼저 따로 본다.
    private static readonly Dictionary<int, bool> dungeonProps = new();
    public static bool IsDungeonProp(GameObject prefab)
    {
        if (prefab == null) return false;
        bool persistent = EditorUtility.IsPersistent(prefab);
        int id = prefab.GetInstanceID();
        if (persistent && dungeonProps.TryGetValue(id, out bool cached)) return cached;
        bool result = false;
        foreach (var component in prefab.GetComponents<Component>())
        {
            if (component == null || component is DungeonDoor) continue;
            if (component is IDungeonPropTarget || component is IDungeonBreakable) { result = true; break; }
            for (var type = component.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                if (type.Name == "DungeonProp") { result = true; break; }
            if (result) break;
        }
        if (persistent) dungeonProps[id] = result;
        return result;
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
        var enemy=prefab.GetComponent<EnemyController>();
        // 몸체가 자식 HitBox에 있는 훈련 표적은 실제 충돌 하단을 격자 바닥에 맞춘다.
        if (collision && enemy != null && enemy.Health != null && enemy.Health.TryGetComponent(out BoxCollider2D hitBox))
        {
            var matrix = Matrix4x4.Scale(prefab.transform.localScale) * prefab.transform.worldToLocalMatrix * hitBox.transform.localToWorldMatrix;
            var bounds = new Bounds(matrix.MultiplyPoint3x4(hitBox.offset), Vector3.zero);
            var halfSize = hitBox.size * .5f;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    bounds.Encapsulate(matrix.MultiplyPoint3x4(hitBox.offset + new Vector2(x * halfSize.x, y * halfSize.y)));
            return bounds;
        }
        var root=enemy!=null && enemy.model!=null?enemy.model:prefab;
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
    // 목표·출구 자리 표식은 그림이 없어도 점유 칸 크기(기준점 = 칸 묶음 중심)로 보인다. 생성 구역 표식은 놓인 1칸 크기(넓이는 범위 상자로 그림).
    public static Bounds RotatedBounds(GameObject prefab, int rotationSteps, WaveDeviceSetting[] settings)
        => IsDungeonSlot(prefab) ? ObjectiveSlotBounds(prefab, rotationSteps)
            : IsDungeonSpawnZone(prefab) ? SpawnZoneBounds
            : IsEnvironmentTool(prefab) ? WaveDevicePreviewCache.RotatedBounds(prefab, settings, ((rotationSteps % 4) + 4) % 4) : RotatedBounds(prefab, rotationSteps);
    // 목표·출구 자리 표식 공통(이름은 3차 호환 — 출구 자리 표식도 이 크기 계산을 쓴다).
    public static Bounds ObjectiveSlotBounds(GameObject prefab, int rotationSteps)
    {
        var size = SlotFootprint(prefab, rotationSteps);
        return new Bounds(Vector3.zero, new Vector3(size.x, size.y, 0f));
    }
    private static readonly Bounds SpawnZoneBounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 0f));
    public static Bounds RotatedBounds(GameObject prefab, int rotationSteps)
    {
        if (IsDungeonSlot(prefab)) return ObjectiveSlotBounds(prefab, rotationSteps);
        if (IsDungeonSpawnZone(prefab)) return SpawnZoneBounds;
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
        // 전용 Prefab이 없는 칸은 실행 화면처럼 맵 기본 Prefab이 그린다. 자기 그림을 쓰지 않는 기본 Platform Prefab은 아래에서 스타일의 Tile로 그린다.
        var source = own != null && own.kind == cell.kind ? own : cell.kind == WaveCellKind.OneWayPlatform ? map.oneWayPrefab : map.platformPrefab;
        if (source != null && source.ownVisual && DrawOwnVisual(rect, source, mask)) return;
        // 기본 Platform Prefab이 스타일 Tile에 따라 이어진 그림을 쓰면(녹색 기본 지형) 실행 화면처럼 그 그림을 스타일 색으로 그린다.
        var connectedStyle = map.styles.Length > 0 ? map.styles[Mathf.Clamp(cell.style,0,map.styles.Length-1)] : null;
        if (source != null && source.StyleConnects(connectedStyle) && DrawOwnVisual(rect, source, mask, connectedStyle.color)) return;
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
    private static bool DrawOwnVisual(Rect rect, WaveTerrainCellView view, int mask, Color? tint = null)
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
        var previous = GUI.color; GUI.color = tint ?? image.color; GUI.DrawTextureWithTexCoords(imageRect, sprite.texture, uv, true); GUI.color = previous;
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
    /// <summary>목표 표식 색(목표 앰버 #FFB020 계열 — DungeonObjectiveSO.color 기본값과 같은 계열).</summary>
    public static readonly Color ObjectiveColor = new Color(1f, .69f, .13f);
    // 목표·출구 자리 표식 공통: 칸에 맞춘 사각형과 흐린 채움. Repaint가 아니면 false.
    private static bool BeginSlotFrame(ref Rect rect, Vector2 size, bool actualSize, out float unit, out Func<float, Color> tinted)
    {
        unit = 1f; tinted = null;
        if (Event.current != null && Event.current.type != EventType.Repaint) return false;
        if (!actualSize)
        {
            var fitted = size * Mathf.Min(rect.width / Mathf.Max(.001f, size.x), rect.height / Mathf.Max(.001f, size.y));
            rect = new Rect(rect.center - fitted * .5f, fitted);
        }
        var tint = GUI.color;
        tinted = alpha => new Color(ObjectiveColor.r * tint.r, ObjectiveColor.g * tint.g, ObjectiveColor.b * tint.b, alpha * tint.a);
        EditorGUI.DrawRect(rect, tinted(.10f));
        unit = Mathf.Max(1f, Mathf.Min(rect.width / Mathf.Max(.001f, size.x), rect.height / Mathf.Max(.001f, size.y)));
        return true;
    }
    // 사각형 테두리를 점선으로 그린다(Handles.BeginGUI 안에서 부른다).
    private static void DashedRect(Rect rect, float unit, Color color)
    {
        float dash = Mathf.Max(3f, unit * .22f), gap = dash * .7f, width = Mathf.Clamp(unit * .06f, 1.2f, 2.5f);
        var corners = new[] { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax) };
        Handles.color = color;
        for (int side = 0; side < 4; side++)
        {
            Vector2 from = corners[side], to = corners[(side + 1) % 4];
            float length = Vector2.Distance(from, to);
            var direction = (to - from) / Mathf.Max(.001f, length);
            for (float t = 0f; t < length; t += dash + gap)
                Handles.DrawAAPolyLine(width, from + direction * t, from + direction * Mathf.Min(length, t + dash));
        }
    }
    // 목표 자리 표식(4차 뒤 벽 레버 패널 4×6): 점유 칸을 점선 앰버 테두리로, 안에 세워질 대형 배전반(프레임·배선 덕트·표시등 3줄·두꺼운 레버 팔·경고등)을 흐린 실루엣으로 그린다.
    // 실물이 아니라 '여기에 목표가 설 수 있음'만 알리는 표식이라 실제 Prefab 그림을 쓰지 않는다. 아래 가운데 원 = 상호작용 지점(x + w/2, y + 0.75). 회전·흐림(GUI.color)을 따른다.
    private static void DrawObjectiveSlot(Rect rect, GameObject prefab, bool actualSize, int turns)
    {
        var size = (Vector2)ObjectiveSlotFootprint(prefab, turns);
        if (!BeginSlotFrame(ref rect, size, actualSize, out float unit, out var tinted)) return;
        var area = rect;
        Vector2 P(float x, float y) => new Vector2(area.xMin + area.width * x, area.yMax - area.height * y);
        Handles.BeginGUI();
        try
        {
            DashedRect(area, unit, tinted(.95f));
            // 패널 프레임(바닥 위 약 0.3칸부터) · 위로 나가는 배선 덕트 2줄
            Handles.color = tinted(.22f);
            Handles.DrawAAConvexPolygon(P(.08f, .05f), P(.92f, .05f), P(.92f, .86f), P(.08f, .86f));
            Handles.color = tinted(.38f);
            Handles.DrawAAPolyLine(Mathf.Max(1.5f, unit * .08f), P(.24f, .86f), P(.24f, .98f));
            Handles.DrawAAPolyLine(Mathf.Max(1.5f, unit * .08f), P(.36f, .86f), P(.36f, .98f));
            // 패널 표시등 3줄(왼쪽)
            float lamp = Mathf.Max(1.5f, unit * .1f);
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 2; column++)
                {
                    var c = P(.18f + column * .1f, .62f - row * .12f);
                    EditorGUI.DrawRect(new Rect(c.x - lamp, c.y - lamp, lamp * 2, lamp * 2), tinted(.5f));
                }
            // 두꺼운 레버 팔(축 → 손잡이 머리) · 경고등(오른쪽 위)
            Handles.color = tinted(.6f);
            Handles.DrawAAPolyLine(Mathf.Max(2.5f, unit * .2f), P(.6f, .45f), P(.74f, .74f));
            Handles.DrawSolidDisc(P(.6f, .45f), Vector3.forward, Mathf.Max(2f, unit * .16f));
            Handles.DrawSolidDisc(P(.74f, .74f), Vector3.forward, Mathf.Max(2f, unit * .22f));
            Handles.color = tinted(.45f);
            Handles.DrawSolidDisc(P(.82f, .93f), Vector3.forward, Mathf.Max(2f, unit * .3f));
            // 상호작용 지점(패널 아래 가운데, y + 0.75칸)
            Handles.color = tinted(.9f);
            Handles.DrawWireDisc(P(.5f, .75f / Mathf.Max(1f, size.y)), Vector3.forward, Mathf.Max(2f, unit * .18f));
        }
        finally { Handles.EndGUI(); }
    }
    // 출구 자리 표식(4차 5×6 뒤 벽 문): 점선 앰버 테두리, 문틀 기둥·상인방(램프 3개), 좌우 문짝 두 장, 문짝을 가로지르는 잠금 볼트 3줄, 상호작용 지점 원.
    private static void DrawExitSlot(Rect rect, GameObject prefab, bool actualSize, int turns, bool arrival = false)
    {
        var size = arrival ? (Vector2)ArrivalSlotFootprint(prefab, turns) : (Vector2)ExitSlotFootprint(prefab, turns);
        if (!BeginSlotFrame(ref rect, size, actualSize, out float unit, out var tinted)) return;
        var area = rect;
        Vector2 P(float x, float y) => new Vector2(area.xMin + area.width * x, area.yMax - area.height * y);
        Handles.BeginGUI();
        try
        {
            DashedRect(area, unit, tinted(.95f));
            Handles.color = tinted(.34f);
            Handles.DrawAAConvexPolygon(P(.04f, 0f), P(.14f, 0f), P(.14f, .84f), P(.04f, .84f));
            Handles.DrawAAConvexPolygon(P(.86f, 0f), P(.96f, 0f), P(.96f, .84f), P(.86f, .84f));
            Handles.DrawAAConvexPolygon(P(.04f, .84f), P(.96f, .84f), P(.96f, .97f), P(.04f, .97f));
            Handles.color = tinted(.18f);
            Handles.DrawAAConvexPolygon(P(.15f, .02f), P(.495f, .02f), P(.495f, .83f), P(.15f, .83f));
            Handles.DrawAAConvexPolygon(P(.505f, .02f), P(.85f, .02f), P(.85f, .83f), P(.505f, .83f));
            Handles.color = tinted(.6f);
            for (int bolt = 0; bolt < 3; bolt++)
                Handles.DrawAAPolyLine(Mathf.Max(2f, unit * .12f), P(.2f, .3f + bolt * .16f), P(.8f, .3f + bolt * .16f));
            for (int lamp = 0; lamp < 3; lamp++)
                Handles.DrawSolidDisc(P(.38f + lamp * .12f, .905f), Vector3.forward, Mathf.Max(1.5f, unit * .12f));
            Handles.color = tinted(.9f);
            Handles.DrawWireDisc(P(.5f, .75f / Mathf.Max(1f, size.y)), Vector3.forward, Mathf.Max(2f, unit * .18f));
            // 6차 도착 자리: 문에서 걸어 나오는 방향(앞쪽 아래)을 갈매기 두 개로 표시해 출구 자리와 구별한다.
            if (arrival)
                for (int chevron = 0; chevron < 2; chevron++)
                {
                    float y = .62f - chevron * .14f;
                    Handles.DrawAAPolyLine(Mathf.Max(2f, unit * .14f), P(.36f, y + .06f), P(.5f, y), P(.64f, y + .06f));
                }
        }
        finally { Handles.EndGUI(); }
    }
    // 생성 구역 표식(놓인 1칸): 종류 색(지상 녹·공중 하늘·혼합 노랑 — DungeonSpawnZone.TryGetRange 색)의 칸과 기호(지상 = 아래 삼각형, 공중 = 위 마름모, 혼합 = 둘 다).
    // 구역 넓이 상자는 DrawRange가 범위로 그린다.
    private static void DrawSpawnZoneMarker(Rect rect, GameObject prefab, bool actualSize, WaveDeviceSetting[] settings)
    {
        if (Event.current != null && Event.current.type != EventType.Repaint) return;
        if (!actualSize)
        {
            float side = Mathf.Min(rect.width, rect.height);
            rect = new Rect(rect.center - Vector2.one * side * .5f, Vector2.one * side);
        }
        var zone = prefab.GetComponent<DungeonSpawnZone>();
        var color = new Color(1f, .85f, .3f, .9f);
        int kind = (int)DungeonSpawnZoneKind.Mixed;
        if (zone != null)
        {
            if (zone.TryGetRange(settings, out var range)) color = range.Color;
            zone.Describe(settings, Vector2.zero, Vector2.zero, out _, out kind, out _);
        }
        var tint = GUI.color;
        Color Tinted(float alpha) => new Color(color.r * tint.r, color.g * tint.g, color.b * tint.b, alpha * tint.a);
        EditorGUI.DrawRect(rect, Tinted(.28f));
        var area = rect;
        Vector2 P(float x, float y) => new Vector2(area.xMin + area.width * x, area.yMax - area.height * y);
        Handles.BeginGUI();
        try
        {
            Handles.color = Tinted(.95f);
            Handles.DrawAAPolyLine(2f, P(0f, 0f), P(1f, 0f), P(1f, 1f), P(0f, 1f), P(0f, 0f));
            if (kind != (int)DungeonSpawnZoneKind.Air) Handles.DrawAAConvexPolygon(P(.18f, .12f), P(.82f, .12f), P(.5f, .5f));
            if (kind != (int)DungeonSpawnZoneKind.Ground) Handles.DrawAAConvexPolygon(P(.5f, .55f), P(.75f, .72f), P(.5f, .9f), P(.25f, .72f));
        }
        finally { Handles.EndGUI(); }
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
        if (IsDungeonObjectiveSlot(prefab)) { DrawObjectiveSlot(rect, prefab, actualSize, turns); return true; }
        if (IsDungeonExitSlot(prefab)) { DrawExitSlot(rect, prefab, actualSize, turns); return true; }
        if (IsDungeonArrivalSlot(prefab)) { DrawExitSlot(rect, prefab, actualSize, turns, true); return true; }
        if (IsDungeonSpawnZone(prefab)) { DrawSpawnZoneMarker(rect, prefab, actualSize, settings); return true; }
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

/// <summary>
/// 던전 크레인 2종(DESIGN_V4 §2.3 경로 크레인·§2.4 고정 크레인)의 배치 설정 해석과 격자 Tool 그림. 런타임 타입(PropCraneHook 재작성·PropCraneAnchor — 영역 facilities4)과
/// 따로 컴파일되도록 컴포넌트 이름(WavePrefabVisuals.DungeonFacilityName)과 배치 설정 키 문자열로만 계산한다. Prefab이 그 키의 항목(IWaveDeviceSettings.Parameters)을
/// 내면 기본값·범위를 따르고, 없으면 저장된 값, 그것도 없으면 이 클래스의 기본값을 쓴다.
/// 점유 2×4, (x, y) = 왼쪽 아래 칸, 집(점 0)의 매달린 Player 발 = (x + 1, y) = 기준점 + (0, −2). 경로 점 i(1..7)는 집 발 기준 칸 오프셋(cranePath{i}X·Y, 눈금 0.5).
/// 쓸기 상자 = 발 ~ 발 + 4칸, 폭 2칸(경로 모든 구간에서 고체 지형·고체 Prop과 겹치면 안 됨 — 템플릿 규칙 v4-3). 매달린 몸 중심 = 발 + 0.755(붙기 반경의 기준).
/// 5차(DESIGN_V5 §1 crane5): 경로 크레인은 양 끝(첫 점 = 집 도크, 마지막 점 = 끝 도크)을 왕복한다. 격자 그림에 양 끝 도크 표시(파랑 머리 보·게이트 기둥)·
/// 왕복 화살·끝 도크 붙기 반경 고리를 그린다. 기본 이동 속도 24칸/초(PropCraneHook.DefaultSpeed와 같은 값).
/// </summary>
public static class DungeonCranePaths
{
    public const string PathComponent = "PropCraneHook", AnchorComponent = "PropCraneAnchor";
    public const string CountKey = "cranePathCount", SpeedKey = "craneSpeed", RadiusKey = "zipRadius", CableKey = "craneCableLength";
    public const int MinPoints = 2, MaxPoints = 8;
    public const float DefaultSpeed = 24f, DefaultRadius = 8f, MinRadius = 4f, MaxRadius = 14f, DefaultCable = 3f;
    /// <summary>5차: 빈 크레인 도중 하차 복귀 속도(6차: 빈 도크 호출은 즉시 생성 — 칸/초, PropCraneHook.ReturnSpeed와 같은 상수 — 배치 설정 키 아님).</summary>
    public const float ReturnSpeed = 40f;
    /// <summary>5차: 도크(승강장) 표시 색(중립 기능 파랑 — WAVE_ENVIRONMENT_DESIGN).</summary>
    public static readonly Color DockColor = new Color(.25f, .55f, 1f);
    /// <summary>경로 점 눈금(칸)과 편집기에서 받는 오프셋 한계(칸). 통합 4차: PropCraneHook 설정 항목(가로 ±80·세로 ±60)에 맞춰 가로 한계를 80으로 — 세로는 With가 항목 범위로 자른다.</summary>
    public const float PointStep = .5f, PointLimit = 80f;
    public const float SweepWidth = 2f, SweepHeight = 4f, BodyLift = .755f;
    public static readonly Vector2Int Size = new Vector2Int(2, 4);
    /// <summary>상호작용 강조 청록(선형 ≈ .25, .95, 1 — DESIGN_V4 §5).</summary>
    public static readonly Color PathColor = new Color(.25f, .95f, 1f);
    public static string PointXKey(int index) => "cranePath" + index + "X";
    public static string PointYKey(int index) => "cranePath" + index + "Y";

    public static bool IsPathCrane(GameObject prefab) => WavePrefabVisuals.DungeonFacilityName(prefab) == PathComponent;
    public static bool IsFixedCrane(GameObject prefab) => WavePrefabVisuals.DungeonFacilityName(prefab) == AnchorComponent;
    public static bool IsCrane(GameObject prefab)
    {
        string name = WavePrefabVisuals.DungeonFacilityName(prefab);
        return name == PathComponent || name == AnchorComponent;
    }

    // Prefab의 배치 설정 항목(키 → 항목). Prefab 에셋만 캐시한다(프로젝트 변경 때 WavePrefabVisuals.Invalidate가 비운다).
    private static readonly Dictionary<int, Dictionary<string, WaveDeviceParameter>> parameters = new();
    public static void Invalidate() => parameters.Clear();
    public static bool TryParameter(GameObject prefab, string key, out WaveDeviceParameter parameter)
    {
        parameter = default;
        if (prefab == null) return false;
        bool persistent = EditorUtility.IsPersistent(prefab);
        int id = prefab.GetInstanceID();
        if (!persistent || !parameters.TryGetValue(id, out var table))
        {
            table = new Dictionary<string, WaveDeviceParameter>();
            try
            {
                var device = prefab.GetComponent<IWaveDeviceSettings>();
                if (device?.Parameters != null) foreach (var item in device.Parameters) if (!string.IsNullOrEmpty(item.Key)) table[item.Key] = item;
            }
            catch (Exception) { /* Edit Mode에서 항목 계산이 실패해도 저장값·기본값으로 읽는다. */ }
            if (persistent) parameters[id] = table;
        }
        return table.TryGetValue(key, out parameter);
    }
    /// <summary>배치 설정 값: Prefab 항목이 있으면 그 규칙(범위·기본값), 없으면 저장값, 그것도 없으면 fallback.</summary>
    public static float Value(GameObject prefab, WaveDeviceSetting[] settings, string key, float fallback)
    {
        if (TryParameter(prefab, key, out var parameter)) return WaveDeviceSettings.Value(settings, parameter);
        if (settings != null) foreach (var setting in settings) if (setting.key == key) return setting.value;
        return fallback;
    }
    /// <summary>한 키를 바꾼 새 배열. Prefab 항목이 있으면 WaveDeviceSettings.With(기본값과 같으면 저장 안 함), 없으면 값을 그대로 저장한다(키 순 정렬).</summary>
    public static WaveDeviceSetting[] With(GameObject prefab, WaveDeviceSetting[] settings, string key, float value)
    {
        if (TryParameter(prefab, key, out var parameter)) return WaveDeviceSettings.With(settings, parameter, value);
        var list = (settings ?? WaveDeviceSettings.None).Where(s => s.key != key && !string.IsNullOrEmpty(s.key)).ToList();
        list.Add(new WaveDeviceSetting(key, value));
        list.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
        return list.ToArray();
    }
    public static WaveDeviceSetting[] Without(WaveDeviceSetting[] settings, string key) => (settings ?? WaveDeviceSettings.None).Where(s => s.key != key).ToArray();

    public static int PointCount(GameObject prefab, WaveDeviceSetting[] settings) => Mathf.Clamp(Mathf.RoundToInt(Value(prefab, settings, CountKey, MinPoints)), MinPoints, MaxPoints);
    public static float Speed(GameObject prefab, WaveDeviceSetting[] settings) => Mathf.Max(.1f, Value(prefab, settings, SpeedKey, DefaultSpeed));
    public static float Radius(GameObject prefab, WaveDeviceSetting[] settings) => Mathf.Clamp(Value(prefab, settings, RadiusKey, DefaultRadius), MinRadius, MaxRadius);
    public static float Cable(GameObject prefab, WaveDeviceSetting[] settings) => Mathf.Max(0f, Value(prefab, settings, CableKey, DefaultCable));
    /// <summary>경로 점 오프셋(집 발 기준, [0] = 집 (0, 0)). 경로 크레인이 아니어도 설정대로 읽는다.</summary>
    public static List<Vector2> Offsets(GameObject prefab, WaveDeviceSetting[] settings)
    {
        int count = PointCount(prefab, settings);
        var result = new List<Vector2>(count) { Vector2.zero };
        for (int i = 1; i < count; i++) result.Add(new Vector2(Value(prefab, settings, PointXKey(i), 0f), Value(prefab, settings, PointYKey(i), 0f)));
        return result;
    }
    public static Vector2Int Footprint(GameObject prefab) => prefab != null && prefab.TryGetComponent<WaveEnvironmentTool>(out var tool) ? tool.Footprint : Size;
    /// <summary>집(점 0)의 매달린 Player 발: 기준점(점유 칸 묶음 중심) + (0, −높이/2). 시설은 회전 0°로 둔다(배치 규약).</summary>
    public static Vector2 HomeFeet(GameObject prefab, Vector2 root) => root + new Vector2(0f, -Footprint(prefab).y * .5f);
    /// <summary>
    /// 7차(DESIGN_V7 V11): 매달린 Player 발(root 좌표계). 고정 크레인은 몸 중심이 그래플 고리 중심에 오도록 root + PropCraneAnchor.HangLocal(집 발보다 1.345칸 위),
    /// 그 밖(경로 크레인 집 등)은 HomeFeet. 편집기 검사(도달 탐색·함정 금지 구역·함정 안전 경로·상호작용 간격·사슬 노드)와 격자 Tool 고리 그림이 이 값 하나를 쓴다.
    /// 차지 칸(footprint)은 계속 HomeFeet 기준이다.
    /// </summary>
    public static Vector2 HangFeet(GameObject prefab, Vector2 root) => IsFixedCrane(prefab) ? root + PropCraneAnchor.HangLocal : HomeFeet(prefab, root);
    /// <summary>경로 점의 발 위치(root 좌표계).</summary>
    public static List<Vector2> Feet(GameObject prefab, Vector2 root, WaveDeviceSetting[] settings)
    {
        // 통합 4차: 프로젝트의 경로 크레인 Prefab이면 실행 쪽 읽기 API(PropCraneHook.TryGetPath — 루트 로컬 매달린 발)를 정본으로 쓴다.
        // 메모리 개체(회귀 고정물)는 아래 키 해석을 쓴다(둘은 같은 키·범위·집 (0, −2) 규칙이다).
        if (prefab != null && EditorUtility.IsPersistent(prefab) && prefab.TryGetComponent<PropCraneHook>(out var hook))
        {
            try
            {
                if (hook.TryGetPath(settings ?? WaveDeviceSettings.None, out var local) && local != null && local.Length >= MinPoints)
                    return local.Select(p => root + p).ToList();
            }
            catch (Exception) { /* Edit Mode에서 읽기가 실패하면 키 해석으로 대신한다. */ }
        }
        var home = HomeFeet(prefab, root);
        return Offsets(prefab, settings).Select(o => home + o).ToList();
    }
    public static Vector2 HangCenter(Vector2 feet) => feet + new Vector2(0f, BodyLift);
    public static Rect SweepRect(Vector2 feet) => new Rect(feet.x - SweepWidth * .5f, feet.y, SweepWidth, SweepHeight);
    public static float Length(IReadOnlyList<Vector2> points)
    {
        float total = 0f;
        for (int i = 0; i + 1 < (points?.Count ?? 0); i++) total += Vector2.Distance(points[i], points[i + 1]);
        return total;
    }
    public static float Snap(float value) => Mathf.Clamp(Mathf.Round(value / PointStep) * PointStep, -PointLimit, PointLimit);
    /// <summary>경로 점(집 제외)을 통째로 쓴 새 설정: cranePathCount = 점 수 + 1(2~8), 점 i는 눈금 0.5로 맞추고, 쓰지 않는 번호의 키는 지운다.</summary>
    public static WaveDeviceSetting[] WithOffsets(GameObject prefab, WaveDeviceSetting[] settings, IReadOnlyList<Vector2> points)
    {
        int count = Mathf.Clamp((points?.Count ?? 0) + 1, MinPoints, MaxPoints);
        var result = With(prefab, settings, CountKey, count);
        for (int i = 1; i < MaxPoints; i++)
        {
            if (i < count)
            {
                var point = points != null && i - 1 < points.Count ? points[i - 1] : Vector2.zero;
                result = With(prefab, result, PointXKey(i), Snap(point.x));
                result = With(prefab, result, PointYKey(i), Snap(point.y));
            }
            else { result = Without(result, PointXKey(i)); result = Without(result, PointYKey(i)); }
        }
        return result;
    }
    /// <summary>두 점의 쓸기 상자를 이은 볼록 다각형(구간의 쓸기 영역).</summary>
    public static List<Vector2> SegmentHull(Vector2 a, Vector2 b)
    {
        var points = new List<Vector2>();
        foreach (var feet in new[] { a, b })
        {
            var r = SweepRect(feet);
            points.Add(new Vector2(r.xMin, r.yMin)); points.Add(new Vector2(r.xMax, r.yMin)); points.Add(new Vector2(r.xMax, r.yMax)); points.Add(new Vector2(r.xMin, r.yMax));
        }
        return Hull(points);
    }
    // 볼록 껍질(단조 사슬). 점이 적어 단순하게 계산한다.
    private static List<Vector2> Hull(List<Vector2> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
        if (sorted.Count < 3) return sorted;
        float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        var hull = new List<Vector2>();
        foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
        {
            int start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        return hull;
    }

    private static GUIStyle labelStyle;
    /// <summary>
    /// 크레인 그림(격자 Tool DrawRange에서 부른다). origin = 기준점의 화면 좌표, zoom = 칸당 픽셀. 경로 크레인: 쓸기 띠(흐린 청록), 발 경로(밝은 선)·트롤리 선(발 + 4칸),
    /// 꺾이는 점 관절 원과 번호, 왕복 화살(구간 가운데 양쪽 방향), 5차 양 끝 도크 표시(파랑 머리 보 + 게이트 기둥 2, '도크' 꼬리표). 공통: 매달림 몸 중심의 붙기 반경 점선 고리(7차 V11: 고정 크레인 = 그래플 고리 중심 — HangFeet)
    /// (경로 크레인은 양 끝 도크 모두 — 끝 도크는 조금 흐리게). 고정 크레인: 위로 올라가는 기둥(craneCableLength).
    /// </summary>
    public static bool Draw(GameObject prefab, Vector2 origin, float zoom, WaveDeviceSetting[] settings, float alpha = 1f)
    {
        bool path = IsPathCrane(prefab), anchor = !path && IsFixedCrane(prefab);
        if (!path && !anchor) return false;
        if (Event.current != null && Event.current.type != EventType.Repaint) return true;
        alpha = Mathf.Clamp01(alpha);
        Vector2 S(Vector2 local) => origin + new Vector2(local.x, -local.y) * zoom;
        Color A(float a) => new Color(PathColor.r, PathColor.g, PathColor.b, a * alpha);
        var home = HomeFeet(prefab, Vector2.zero);
        // 7차 V11: 붙기 반경 고리는 매달린 몸 중심 기준 — 고정 크레인은 그래플 고리 중심(HangFeet = 집 발 + 1.345칸), 경로 크레인은 집 발 그대로.
        // 경로·쓸기 띠·도크·기둥 그림은 계속 집 발(HomeFeet) 기준이다.
        var ringHome = HangFeet(prefab, Vector2.zero);
        var labels = new List<(Vector2 at, string text)>();
        Handles.BeginGUI();
        try
        {
            if (path)
            {
                var feet = Offsets(prefab, settings).Select(o => home + o).ToList();
                Handles.color = A(.10f);
                for (int i = 0; i + 1 < feet.Count; i++)
                {
                    var hull = SegmentHull(feet[i], feet[i + 1]).Select(p => (Vector3)S(p)).ToArray();
                    if (hull.Length >= 3) Handles.DrawAAConvexPolygon(hull);
                }
                Handles.color = A(.55f);
                Handles.DrawAAPolyLine(1.5f, feet.Select(f => (Vector3)S(f + new Vector2(0f, SweepHeight))).ToArray());
                Handles.color = A(.95f);
                Handles.DrawAAPolyLine(2.5f, feet.Select(f => (Vector3)S(f)).ToArray());
                float joint = Mathf.Max(2.5f, zoom * .14f);
                for (int i = 0; i < feet.Count; i++)
                {
                    var p = S(feet[i]);
                    bool dock = i == 0 || i == feet.Count - 1;
                    if (dock) EditorGUI.DrawRect(new Rect(p.x - joint, p.y - joint, joint * 2, joint * 2), A(.95f));
                    else { Handles.color = A(.95f); Handles.DrawSolidDisc(p, Vector3.forward, joint); }
                    labels.Add((p + new Vector2(joint + 2f, -joint - 12f), i == 0 ? "0 도크(집)" : i == feet.Count - 1 ? i + " 도크(끝)" : i.ToString()));
                }
                // 5차 도크 표시(양 끝): 레일(발 + 4칸) 위 머리 보 + 아래로 1칸 내려온 게이트 기둥 2개(파랑 — 실행 중 Prefab의 도크와 같은 자리).
                if (feet.Count >= 2)
                    foreach (var dockFeet in new[] { feet[0], feet[feet.Count - 1] })
                    {
                        Handles.color = new Color(DockColor.r, DockColor.g, DockColor.b, .9f * alpha);
                        Vector2 track = dockFeet + new Vector2(0f, SweepHeight);
                        Handles.DrawAAPolyLine(3f, S(track + new Vector2(-1.1f, .48f)), S(track + new Vector2(1.1f, .48f)));
                        foreach (float side in new[] { -1.05f, 1.05f })
                            Handles.DrawAAPolyLine(2f, S(track + new Vector2(side, .48f)), S(track + new Vector2(side, -1f)));
                    }
                // 왕복 화살(구간 가운데, 집 ↔ 끝 — 5차: 양 끝 도크 사이를 오간다)
                Handles.color = A(.9f);
                for (int i = 0; i + 1 < feet.Count; i++)
                {
                    Vector2 a = S(feet[i]), b = S(feet[i + 1]);
                    float length = Vector2.Distance(a, b);
                    if (length < joint * 6f) continue;
                    var d = (b - a) / length; var n = new Vector2(-d.y, d.x);
                    var mid = (a + b) * .5f; float s = Mathf.Max(4f, zoom * .3f);
                    var ahead = mid + d * s * .9f; var back = mid - d * s * .9f;
                    Handles.DrawAAConvexPolygon(ahead + d * s, ahead - d * s * .6f + n * s * .6f, ahead - d * s * .6f - n * s * .6f);
                    Handles.DrawAAConvexPolygon(back - d * s, back + d * s * .6f + n * s * .6f, back + d * s * .6f - n * s * .6f);
                }
            }
            else
            {
                // 고정 크레인: 점유 칸 위쪽에서 천장 쪽으로 기둥(표시만)
                var top = home + new Vector2(0f, Footprint(prefab).y);
                Handles.color = new Color(.7f, .75f, .8f, .8f * alpha);
                Handles.DrawAAPolyLine(1.5f, S(top), S(top + new Vector2(0f, Cable(prefab, settings))));
            }
            // 붙기 반경 고리(점선, 매달림 몸 중심). 5차: 경로 크레인은 끝 도크에서도 붙으므로 끝 도크 고리도 그린다(조금 흐리게).
            float radius = Radius(prefab, settings) * zoom;
            var rings = new List<(Vector2 feet, float a)> { (ringHome, .5f) };
            if (path)
            {
                var offsets = Offsets(prefab, settings);
                if (offsets.Count >= 2) rings.Add((home + offsets[offsets.Count - 1], .3f));
            }
            const int segments = 64;
            foreach (var (ringFeet, ringAlpha) in rings)
            {
                var center = S(HangCenter(ringFeet));
                Handles.color = A(ringAlpha);
                for (int i = 0; i < segments; i += 2)
                {
                    float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                    Handles.DrawAAPolyLine(1.5f, center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius, center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius);
                }
                Handles.DrawWireDisc(center, Vector3.forward, Mathf.Max(2f, zoom * .2f));
            }
        }
        finally { Handles.EndGUI(); }
        if (alpha >= .9f && zoom >= 8f && labels.Count > 0)
        {
            labelStyle ??= new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = PathColor } };
            foreach (var (at, text) in labels) GUI.Label(new Rect(at.x, at.y, 48f, 14f), text, labelStyle);
        }
        return true;
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
