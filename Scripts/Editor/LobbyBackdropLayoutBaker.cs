#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>로비 배치 연결점을 기존 장식 애니메이션의 좌표로 변환한다.</summary>
public static class LobbyBackdropLayoutBaker
{
    public const string PrefabPath = "Assets/Prefabs/Environment/LobbyBackdrop.prefab";
    public const string SourceClipPath = "Assets/Animations/Clips/Lobby/Lobby_Ambient_Loop.anim";
    public const string LayoutClipPath = "Assets/Animations/Clips/Lobby/Lobby_Layout_Loop.anim";
    public const string OverridePath = "Assets/Animations/Controllers/Lobby/LobbyLayout.overrideController";
    public const string Facilities = "FactoryFacilities/";
    public const string Service = "ServiceFacility/";
    public const string CranePath = Facilities + "CraneProp/GantryCrane";

    private static readonly Dictionary<int, string> Signatures = new Dictionary<int, string>();

    public static readonly string[] PropPaths =
    {
        Facilities + "SupplyConveyorProp", Facilities + "AssemblyConveyorProp",
        Facilities + "SupplySpawnerProp",
        Facilities + "CraneProp",
        Facilities + "LogisticsSpawnerProp", Facilities + "PickupConveyorProp",
        Facilities + "ReceivingPitProp", Service + "RepairBuildingProp",
        Service + "RepairBuildingProp/RepairStationsProp", "AuxiliaryProduction/FarFactoryProp",
        "GeometricDepth/LeftTransferLiftProp", "GeometricDepth/RightStampPressProp",
        Facilities + "RightCargoRackProp",
        "FactoryFacilities/LeftReturnLineProp",
        "FactoryFacilities/WestBufferRackProp",
        "FactoryFacilities/EastBufferRackProp",
        "FactoryFacilities/EastOverheadLineProp",
        "GeometricDepth/CentralValveBankProp",
        "FactoryFacilities/LowerWestLineProp",
        "AuxiliaryProduction/CentralAssemblyCellProp",
        "AuxiliaryProduction/WestCraneCellProp",
        "AuxiliaryProduction/EastCraneCellProp",
        "GeometricDepth/WestLiftBayProp",
        "GeometricDepth/CentralPressBayProp",
        "FactoryFacilities/RightStockTowerProp",
        "AuxiliaryProduction/CeilingAssemblyLineProp",
        "AuxiliaryProduction/LowerServiceConveyorProp",
        "AuxiliaryProduction/RemoteWestLineProp",
        "AuxiliaryProduction/RemoteAssemblyLineProp",
        "AuxiliaryProduction/RemoteCenterLineProp",
        "AuxiliaryProduction/RemoteEastLineProp",
        "AuxiliaryProduction/RemoteReceivingLineProp",
        "GeometricDepth/RemoteWestLiftProp",
        "GeometricDepth/RemoteCenterLiftProp",
        "GeometricDepth/RemoteEastLiftProp",
        "GeometricDepth/RemoteWestPressProp",
        "GeometricDepth/RemoteCenterPressProp",
        "GeometricDepth/RemoteEastPressProp",
        "AuxiliaryProduction/RemoteStorageProp",
        "FactoryFacilities/FacilityBayGrid", "AuxiliaryProduction/FacilityBayGrid",
        "GeometricDepth/FacilityBayGrid", "ServiceFacility/FacilityBayGrid"
    };

    public static readonly string[] PropLabels =
    {
        "하단 컨베이어", "상단 컨베이어", "왼쪽 스포너",
        "크레인", "크레인 공급 스포너", "수취 컨베이어",
        "오른쪽 투입구", "뒤쪽 수리 건물", "벽면 수리 장치", "원경 생산 라인",
        "먼 작업실 리프트", "먼 작업실 프레스", "오른쪽 화물 보관대",
        "왼쪽 상부 보조 벨트",
        "물류 구역 상단 완충 적재대",
        "물류 구역 하단 완충 적재대",
        "우측 천장 이송 라인",
        "중앙 벽면 프레스",
        "왼쪽 하부 보조 벨트",
        "왼쪽 보조 조립 공정",
        "물류 구역 1차 크레인 공정",
        "물류 구역 2차 크레인 공정",
        "중앙 대형 승강 설비",
        "중앙 보조 프레스",
        "후면 물류 적재대",
        "후면 천장 조립 라인",
        "후면 하부 이송 라인",
        "원경 서쪽 이송 라인",
        "원경 조립 라인",
        "원경 중앙 이송 라인",
        "원경 동쪽 이송 라인",
        "원경 수취 라인",
        "원경 서쪽 리프트",
        "원경 중앙 리프트",
        "원경 동쪽 리프트",
        "원경 서쪽 프레스",
        "원경 중앙 프레스",
        "원경 동쪽 프레스",
        "원경 창고 적재대",
        "주 생산실 벽·보·배선", "보조 생산실 벽·보·배선",
        "승강 작업실 벽·보·배선", "수리동 벽·보·배선"
    };

    public static readonly string[] AnchorPaths =
    {
        Facilities + "SupplySpawnerProp/OutputPoint",
        Facilities + "AssemblyConveyorProp/FeedPoint",
        Facilities + "AssemblyConveyorProp/ExitPoint",
        Facilities + "LogisticsSpawnerProp/OutputPoint",
        Facilities + "PickupConveyorProp/PickupPoint",
        Facilities + "ReceivingPitProp/DropPoint",
        Facilities + "ReceivingPitProp/InsidePoint",
        Facilities + "CraneProp/LiftHeight",
        CranePath + "/Trolley/Hoist/GripPoint"
    };

    public static readonly string[] AnchorLabels =
    {
        "왼쪽 공급점", "상단 벨트 도착점", "상단 벨트 퇴장점", "크레인 공급점",
        "집는 지점", "놓는 지점", "투입구 안쪽", "올리는 높이", "집게 중심"
    };

    public static Transform FindBackdrop()
    {
        return UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(t => t.name == "LobbyBackdrop" && t.gameObject.scene.IsValid()
                && !EditorUtility.IsPersistent(t) && t.parent != null && t.parent.name == "Lobby");
    }

    public static bool IsReady(Transform root)
    {
        return root != null && PropPaths.All(p => root.Find(p) != null)
            && AnchorPaths.All(p => root.Find(p) != null);
    }

    public static string[] Validate(Transform root)
    {
        if (!IsReady(root)) return new[] { "MainScene의 LobbyBackdrop 배치 구성을 찾을 수 없습니다." };
        var warnings = new List<string>();
        var crane = root.Find(CranePath);
        var grip = root.Find(AnchorPaths[8]);
        foreach (int index in new[] { 4, 5 })
        {
            float height = crane.InverseTransformPoint(root.Find(AnchorPaths[index]).position).y - grip.localPosition.y;
            if (height > -.25f) warnings.Add("크레인 레일을 집는 지점과 놓는 지점보다 높게 배치하세요.");
        }
        foreach (string path in PropPaths)
        {
            var prop = root.Find(path);
            if (Quaternion.Angle(prop.localRotation, Quaternion.identity) > .01f
                || (prop.localScale - Vector3.one).sqrMagnitude > .0001f)
            {
                warnings.Add("배치 루트는 위치 조정용입니다. 회전과 크기는 내부 모델에서 조정하고 연결점을 확인하세요.");
                break;
            }
        }
        return warnings.Distinct().ToArray();
    }

    public static AnimationClip Bake(Transform root, bool saveAssets = true, bool force = false)
    {
        if (!IsReady(root)) throw new InvalidOperationException("로비 배치 루트와 연결점이 필요합니다.");
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode에서 경로를 갱신하세요.");

        var animator = root.GetComponent<Animator>();
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(SourceClipPath);
        if (animator == null || source == null) throw new InvalidOperationException("로비 Animator와 원본 Clip이 필요합니다.");

        string signature = Signature(root);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(LayoutClipPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(OverridePath);
        ConfigureBatches(root, clip != null ? clip : source);
        if (!force && clip != null && controller != null && animator.runtimeAnimatorController == controller
            && Signatures.TryGetValue(root.GetInstanceID(), out string previous) && previous == signature)
            return clip;

        if (clip == null)
        {
            clip = UnityEngine.Object.Instantiate(source);
            AssetDatabase.CreateAsset(clip, LayoutClipPath);
        }
        else EditorUtility.CopySerialized(source, clip);
        clip.name = "Lobby_Layout_Loop";

        BakeSupply(root, clip);
        BakeCrane(root, clip);
        BakeRepair(root, clip);
        ConfigureBatches(root, clip);

        if (controller == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            controller = new AnimatorOverrideController(prefab.GetComponent<Animator>().runtimeAnimatorController);
            AssetDatabase.CreateAsset(controller, OverridePath);
        }
        controller[source.name] = clip;
        if (animator.runtimeAnimatorController != controller)
        {
            Undo.RecordObject(animator, "로비 배치 애니메이션 연결");
            animator.runtimeAnimatorController = controller;
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        }
        EditorUtility.SetDirty(clip);
        EditorUtility.SetDirty(controller);
        Signatures[root.GetInstanceID()] = signature;
        if (saveAssets) AssetDatabase.SaveAssets();
        return clip;
    }

    public static void ConfigureBatches(Transform root, AnimationClip clip)
    {
        var batcher = root.GetComponent<LobbyBackdropBatchRenderer>();
        if (batcher == null || clip == null) return;
        var animated = new HashSet<Transform>();
        var excluded = new HashSet<Renderer>();
        foreach (var binding in AnimationUtility.GetCurveBindings(clip)
            .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
        {
            var target = root.Find(binding.path);
            if (target == null) continue;
            if (binding.type == typeof(Transform) || binding.type == typeof(GameObject)) animated.Add(target);
            else if (typeof(Renderer).IsAssignableFrom(binding.type))
            {
                var renderer = target.GetComponent<Renderer>();
                if (renderer != null) excluded.Add(renderer);
            }
        }
        // 별도 Animator가 있는 하위 소품은 해당 Renderer의 원래 재생 경로를 유지한다.
        foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            if (animator.transform != root)
                foreach (var renderer in animator.GetComponentsInChildren<Renderer>(true)) excluded.Add(renderer);
        var serialized = new SerializedObject(batcher);
        bool changed = false;
        void SetReferences(string property, UnityEngine.Object[] values)
        {
            var array = serialized.FindProperty(property);
            if (array.arraySize == values.Length && values.Select((value, i) =>
                array.GetArrayElementAtIndex(i).objectReferenceValue == value).All(equal => equal)) return;
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            changed = true;
        }
        SetReferences("animatedRoots", animated.OrderBy(t => AnimationUtility.CalculateTransformPath(t, root)).Cast<UnityEngine.Object>().ToArray());
        SetReferences("unbatchedRenderers", excluded.OrderBy(r => AnimationUtility.CalculateTransformPath(r.transform, root)).Cast<UnityEngine.Object>().ToArray());
        var prepared = serialized.FindProperty("layoutPrepared");
        if (!prepared.boolValue) { prepared.boolValue = true; changed = true; }
        if (!changed) return;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(batcher);
        if (PrefabUtility.IsPartOfPrefabInstance(batcher)) PrefabUtility.RecordPrefabInstancePropertyModifications(batcher);
    }

    private static string Signature(Transform root)
    {
        var values = new List<float>();
        foreach (string path in PropPaths.Concat(AnchorPaths))
        {
            var t = root.Find(path);
            // 상위 후경 레이어의 크기·배치 변경도 동선 갱신에 반영한다.
            Matrix4x4 relative = root.worldToLocalMatrix * t.localToWorldMatrix;
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    values.Add(relative[row, column]);
        }
        return AssetDatabase.GetAssetDependencyHash(SourceClipPath) + ";"
            + string.Join(";", values.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static void BakeSupply(Transform root, AnimationClip clip)
    {
        Vector3 source = root.Find(AnchorPaths[0]).position;
        Vector3 landing = root.Find(AnchorPaths[1]).position;
        Vector3 exit = root.Find(AnchorPaths[2]).position;
        for (int i = 0; i < 4; i++)
        {
            var unit = root.Find(Facilities + "SupplyOutput" + i);
            float start = i * 10;
            // 공급 직후에는 아래로 내려오고, 벨트 위에서는 끝 덮개를 향해 이동한다.
            Position(root, clip, unit, new[] { 0f, start + .4f, start + 1.4f, start + 4.7f, start + 8.8f, 40 },
                new[] { source, source, new Vector3(source.x, landing.y, source.z), landing, exit, source });
        }
    }

    private static void BakeCrane(Transform root, AnimationClip clip)
    {
        var crane = root.Find(CranePath);
        var trolley = crane.Find("Trolley");
        var hoist = trolley.Find("Hoist");
        var cable = trolley.Find("HoistCable");
        Vector3 gripOffset = root.Find(AnchorPaths[8]).localPosition;
        Vector3 source = root.Find(AnchorPaths[3]).position;
        Vector3 pickup = root.Find(AnchorPaths[4]).position;
        Vector3 drop = root.Find(AnchorPaths[5]).position;
        Vector3 inside = root.Find(AnchorPaths[6]).position;
        Vector3 pickLocal = crane.InverseTransformPoint(pickup);
        Vector3 dropLocal = crane.InverseTransformPoint(drop);
        float pickX = pickLocal.x - gripOffset.x;
        float dropX = dropLocal.x - gripOffset.x;
        float pickY = pickLocal.y - gripOffset.y;
        float dropY = dropLocal.y - gripOffset.y;
        float liftY = crane.InverseTransformPoint(root.Find(AnchorPaths[7]).position).y;
        liftY = Mathf.Min(-.25f, Mathf.Max(liftY, Mathf.Max(pickY, dropY) + .60f));
        // 기존 승강 스트랩은 로컬 좌표 0에서 -2까지 이어진다.
        const float cableLength = 2;

        var xKeys = new List<Keyframe>();
        var yKeys = new List<Keyframe>();
        for (int i = 0; i < 4; i++)
        {
            float start = i * 10;
            Add(xKeys, start, new[] { 0f, 4.45f, 6.2f, 8, 9.8f, 10 },
                new[] { pickX, pickX, dropX, dropX, pickX, pickX });
            Add(yKeys, start, new[] { 0f, 2.6f, 3.4f, 3.7f, 4.45f, 6.2f, 6.95f, 7.25f, 8, 10 },
                new[] { liftY, liftY, pickY, pickY, liftY, liftY, dropY, dropY, liftY, liftY });

            Vector3 liftedPickup = crane.TransformPoint(new Vector3(pickLocal.x, liftY + gripOffset.y, pickLocal.z));
            Vector3 liftedDrop = crane.TransformPoint(new Vector3(dropLocal.x, liftY + gripOffset.y, dropLocal.z));
            var unit = root.Find(Facilities + "TransferPayload" + i);
            Position(root, clip, unit,
                new[] { 0f, start + .4f, start + 1.5f, start + 2.6f, start + 3.4f,
                    start + 3.7f, start + 4.45f, start + 6.2f, start + 6.95f, start + 7.25f, start + 7.9f, 40 },
                new[] { source, source, new Vector3(source.x, pickup.y, source.z), pickup, pickup,
                    pickup, liftedPickup, liftedDrop, drop, drop, inside, source });
        }
        Set(root, clip, trolley, "m_LocalPosition.x", Curve(xKeys));
        Set(root, clip, hoist, "m_LocalPosition.y", Curve(yKeys));
        Set(root, clip, cable, "m_LocalScale.y", Curve(yKeys.Select(k => new Keyframe(k.time, -k.value / cableLength))));
    }

    private static void BakeRepair(Transform root, AnimationClip clip)
    {
        var stations = root.Find(Service + "RepairBuildingProp/RepairStationsProp");
        // 벽에 붙은 장치마다 짧은 작업 범위를 순회한다. 모든 동작은 장식 Animation이다.
        for (int i = 0; i < stations.childCount; i++)
        {
            var station = stations.GetChild(i);
            if (!station.name.StartsWith("RepairStation")) continue;
            var pivot = station.Find("ToolPivot");
            var beam = pivot.Find("RepairLaser");
            var contact = station.Find("WeldContact");
            var angles = new List<Keyframe>();
            var lengths = new List<Keyframe>();
            var xs = new List<Keyframe>();
            var ys = new List<Keyframe>();
            float speed = Mathf.PI * 2 / (i % 2 == 0 ? 8 : 10);
            float phase = i * 1.17f;
            for (float t = 0; t <= 40; t += .125f)
            {
                float a = t * speed + phase;
                var target = new Vector2(1.12f + Mathf.Cos(a) * .16f, Mathf.Sin(a) * .30f);
                var velocity = new Vector2(-Mathf.Sin(a) * .16f * speed, Mathf.Cos(a) * .30f * speed);
                float length = target.magnitude;
                float angleDerivative = (target.x * velocity.y - target.y * velocity.x) / target.sqrMagnitude * Mathf.Rad2Deg;
                float lengthDerivative = Vector2.Dot(target, velocity) / length;
                angles.Add(new Keyframe(t, Mathf.Atan2(target.y, target.x) * Mathf.Rad2Deg, angleDerivative, angleDerivative));
                lengths.Add(new Keyframe(t, length - .55f, lengthDerivative, lengthDerivative));
                xs.Add(new Keyframe(t, target.x, velocity.x, velocity.x));
                ys.Add(new Keyframe(t, target.y, velocity.y, velocity.y));
            }
            Set(root, clip, pivot, "localEulerAnglesRaw.z", Curve(angles));
            Set(root, clip, beam, "m_LocalScale.x", Curve(lengths));
            Set(root, clip, contact, "m_LocalPosition.x", Curve(xs));
            Set(root, clip, contact, "m_LocalPosition.y", Curve(ys));
        }
    }

    private static void Position(Transform root, AnimationClip clip, Transform target, float[] times, Vector3[] world)
    {
        var points = world.Select(target.parent.InverseTransformPoint).ToArray();
        Set(root, clip, target, "m_LocalPosition.x", Curve(times.Select((t, i) => new Keyframe(t, points[i].x))));
        Set(root, clip, target, "m_LocalPosition.y", Curve(times.Select((t, i) => new Keyframe(t, points[i].y))));
    }

    private static void Add(List<Keyframe> keys, float start, float[] times, float[] values)
    {
        for (int i = 0; i < times.Length; i++) keys.Add(new Keyframe(start + times[i], values[i]));
    }

    private static AnimationCurve Curve(IEnumerable<Keyframe> keys)
    {
        return new AnimationCurve(keys.GroupBy(k => Mathf.RoundToInt(k.time * 100000))
            .Select(g => g.Last()).OrderBy(k => k.time).ToArray());
    }

    private static void Set(Transform root, AnimationClip clip, Transform target, string property, AnimationCurve curve)
    {
        var binding = EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(target, root), typeof(Transform), property);
        AnimationUtility.SetEditorCurve(clip, binding, curve);
    }
}
#endif
