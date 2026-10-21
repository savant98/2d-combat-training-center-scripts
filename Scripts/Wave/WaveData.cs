using UnityEngine;

[CreateAssetMenu(menuName = "Game/WaveData")]
public class WaveData : ScriptableObject
{
    public Wave[] waves;
}

public enum WaveClearType
{
    DefeatAllTargets,
    SurviveForTime,
    CustomTrigger
}

[System.Serializable]
public class Wave
{
    [Header("Wave Settings")]
    public WaveClearType clearType = WaveClearType.DefeatAllTargets;
    public float surviveTime = 0f;
    public string customTriggerKey = "";
    public WaveSpawnInfo[] spawnObjects;
}

[System.Serializable]
public class WaveSpawnInfo
{
    public GameObject prefab;
    public bool isRequiredToClear = true;
    public bool keepAfterWave = false;
    public bool isBossEnemy = false;
    public float spawnDelay;
    public Vector2 spawnPosition;
}
