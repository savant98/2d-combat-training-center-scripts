using System;
using UnityEngine;

[Serializable]
public sealed class WaveRoomLayout
{
    public Rect bounds = new Rect(-4, -3, 26, 17);
    public Vector2 spawnOffset = new Vector2(0, -2);
    public bool centerPlayer;
    public bool faceCenter;
    public float duration = 1.2f;
    [Tooltip("커스텀 Room의 고정 발판 대신 아래 단위 셀을 공통 규칙으로 생성합니다.")]
    public bool paintPlatforms;
    public Vector2Int[] platformCells = Array.Empty<Vector2Int>();
    public Vector2Int[] removedCells = Array.Empty<Vector2Int>();
}

// 변경이 없는 Wave는 직전 지형과 생성 기준점을 이어 쓴다.
public static class WaveRoomState
{
    public static WaveRoomLayout LayoutAt(WaveDataSO data, int index)
    {
        var result = data.room;
        for (int i = 0; i <= index && i < (data.waves?.Length ?? 0); i++)
            if (data.waves[i].changeTerrain) result = data.waves[i].room;
        return result;
    }

    public static Vector2 PlayerStartAt(WaveDataSO data, int index)
    {
        if (data.waves != null && index >= 0 && index < data.waves.Length && data.waves[index].overridePlayerStart)
            return data.waves[index].playerStart;
        return data.playerStart;
    }

    public static Vector2 EntryPoint(WaveDataSO data)
    {
        var point = PlayerStartAt(data, 0);
        if (!data.customRoom && !data.cellMap.enabled)
        {
            point.x = Mathf.Clamp(point.x, data.room.bounds.xMin + 1, data.room.bounds.xMax - 1);
            point.y = Mathf.Clamp(point.y, data.room.bounds.yMin + 1, data.room.bounds.yMax - 1);
        }
        return point;
    }
}

public enum WaveTerrainOperation { Add, Remove }

[Serializable]
public sealed class WaveTerrainEvent
{
    public WaveCellKind cellKind;
    public int cellStyle = 1;
    public string label = "지형 변경";
    public float time;
    public float duration = .55f;
    public WaveTerrainOperation operation;
    public float lifetime;
    public bool keepAfterWave;
    public Vector2Int[] cells = Array.Empty<Vector2Int>();
    public bool custom;
    public GameObject customPrefab;
}
