using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class WaveAuthoring
{
    public static float[] Times(Wave wave)
    {
        var items = wave.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
        var times = new float[items.Length];
        double time = wave.startDelay;
        for (int i = 0; i < items.Length; i++) { times[i] = (float)time; time += items[i].spawnDelay; }
        return times;
    }

    // 기존 '항목 뒤 대기' 표현을 보존하면서 절대 시각으로 추가·이동·삭제한다.
    public static int SetTime(Wave wave, int index, float time, bool remove = false)
    {
        float[] times = Times(wave);
        float end = (float)((double)wave.startDelay + (wave.spawnObjects ?? Array.Empty<WaveSpawnInfo>()).Sum(s => (double)s.spawnDelay));
        var rows = wave.spawnObjects.Select((s, i) => (item: s, time: times[i], order: i)).ToList();
        var changed = rows[index];
        rows.RemoveAt(index);
        if (!remove) rows.Add((changed.item, Mathf.Max(0, time), changed.order));
        rows = rows.OrderBy(r => r.time).ThenBy(r => r.order).ToList();
        wave.spawnObjects = rows.Select(r => r.item).ToArray();
        for (int i = 0; i < rows.Count; i++)
            rows[i].item.spawnDelay = Mathf.Max(0, (i + 1 < rows.Count ? rows[i + 1].time : Mathf.Max(end, rows[i].time)) - rows[i].time);
        // 첫 생성 전 대기는 기존 표현에 없으므로 별도 startDelay에 기록한다.
        wave.startDelay = rows.Count > 0 ? rows[0].time : 0;
        for (int i = 0; i < rows.Count; i++) if (ReferenceEquals(rows[i].item, changed.item)) return i;
        return -1;
    }

    public static List<string> Validate(WaveDataSO data)
    {
        var issues = new List<string>();
        if (data.waves == null || data.waves.Length == 0) issues.Add("Wave가 없습니다.");
        CheckBounds(data.room.bounds, "기본 Room", issues);
        if (data.cellMap.enabled && (data.cellMap.platformPrefab == null || data.cellMap.oneWayPrefab == null)) issues.Add("한 칸 Platform / OneWayPlatform Prefab을 연결하세요.");
        if (!data.customRoom && !data.room.bounds.Contains(data.playerStart)) issues.Add("시작점이 Room 밖입니다. 실행 시 1칸 안쪽으로 보정됩니다.");
        for (int w = 0; w < (data.waves?.Length ?? 0); w++)
        {
            var wave = data.waves[w];
            string prefix = $"W{w + 1}";
            if (wave.changeTerrain) CheckBounds(wave.room.bounds, prefix, issues);
            if (wave.clearType == WaveClearType.CustomTrigger && string.IsNullOrWhiteSpace(wave.customTriggerKey)) issues.Add(prefix + ": 목표 트리거 키가 비었습니다.");
            if (wave.surviveTime < 0 || wave.clearDelay < 0 || wave.startDelay < 0) issues.Add(prefix + ": 음수 시간을 수정하세요.");
            var spawns = wave.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
            for (int i = 0; i < spawns.Length; i++)
            {
                var spawn = spawns[i];
                string item = $"{prefix} #{i + 1} {spawn.prefab?.name ?? "참조 없음"}";
                if (spawn.prefab == null) issues.Add(prefix + ": Prefab 참조가 비었습니다.");
                CheckTime(spawn.spawnDelay, item + " 생성 후 대기", issues);
                CheckTime(spawn.lifetime, item + " 수명", issues);
                if (spawn.isRequiredToClear && spawn.prefab != null && spawn.prefab.GetComponent<EnemyController>() == null && spawn.prefab.GetComponent<HealthBase>() != null)
                    issues.Add(prefix + ": 파괴 가능한 월드 목표의 사망 이벤트 연결을 확인하세요.");
            }
            foreach (var batch in wave.cellBatches)
                if (!float.IsFinite(batch.time) || !float.IsFinite(batch.duration) || !float.IsFinite(batch.lifetime) || batch.time < 0 || batch.duration < 0 || batch.lifetime < 0) issues.Add(prefix + ": 셀 생성 묶음의 시간/수명 오류입니다.");
            foreach (var entry in wave.terrainEvents ?? Array.Empty<WaveTerrainEvent>())
            {
                if (!float.IsFinite(entry.time) || !float.IsFinite(entry.duration) || entry.time < 0 || entry.duration < 0) issues.Add(prefix + ": 지형 예약 시간이 올바르지 않습니다.");
                if (!entry.custom && entry.operation == WaveTerrainOperation.Add) CheckTime(entry.lifetime, prefix + " 지형 생성 수명", issues);
                if (entry.custom && entry.customPrefab == null) issues.Add(prefix + ": 커스텀 지형 Prefab이 비었습니다.");
            }
        }
        return issues;
    }
    private static void CheckTime(float value, string label, List<string> issues)
    {
        if (!float.IsFinite(value) || value < 0) issues.Add($"{label}: {value:G9}초 — 0 이상의 유한한 값을 입력하세요.");
    }
    private static void CheckBounds(Rect rect, string label, List<string> issues)
    {
        if (float.IsNaN(rect.x) || float.IsNaN(rect.y) || float.IsNaN(rect.width) || float.IsNaN(rect.height)
            || Mathf.Abs(rect.x) > 10000 || Mathf.Abs(rect.y) > 10000 || rect.width < 4 || rect.height < 4 || rect.width > 256 || rect.height > 256)
            issues.Add(label + ": 가로·세로는 4~256칸이어야 합니다.");
        if (rect != Rect.MinMaxRect(Mathf.Round(rect.xMin), Mathf.Round(rect.yMin), Mathf.Round(rect.xMax), Mathf.Round(rect.yMax)))
            issues.Add(label + ": 소수 외곽은 원본 보존용입니다. 새 Room은 정수 칸을 권장합니다.");
    }

    public static WaveDataSO Create(string path, TrainingRoomContent template)
    {
        var data = ScriptableObject.CreateInstance<WaveDataSO>();
        data.roomTemplate = template;
        var basis = AssetDatabase.LoadAssetAtPath<WaveDataSO>("Assets/Data/Waves/ComprehensiveFundamentalsWave.asset");
        if (basis != null && basis.cellMap.enabled)
        {
            data.cellMap = JsonUtility.FromJson<WaveCellMap>(JsonUtility.ToJson(basis.cellMap));
            data.room = JsonUtility.FromJson<WaveRoomLayout>(JsonUtility.ToJson(basis.room));
            data.playerStart = basis.playerStart;
        }
        data.waves = new[] { new Wave { spawnObjects = Array.Empty<WaveSpawnInfo>() } };
        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();
        return data;
    }
    [MenuItem("Assets/Create/Game/WaveData")]
    private static void CreateCellWave()
    {
        string path = EditorUtility.SaveFilePanelInProject("새 셀 맵 WaveData", "NewWave", "asset", "저장 위치");
        if (string.IsNullOrEmpty(path)) return;
        Selection.activeObject = Create(path, AssetDatabase.LoadAssetAtPath<TrainingRoomContent>("Assets/Prefabs/Training/TrainingRoomBase.prefab"));
    }
}
