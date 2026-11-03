using UnityEngine;

[CreateAssetMenu(menuName = "Game/WaveData")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "WaveData")]
public class WaveDataSO : ScriptableObject
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
    [Tooltip("이 웨이브를 클리어한 뒤 다음 웨이브 안내를 시작하기 전까지 기다리는 시간(초)입니다. 발판이 사라지고 지형이 돌아오는 시간을 둘 때 씁니다.")]
    public float clearDelay = 0f;
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
