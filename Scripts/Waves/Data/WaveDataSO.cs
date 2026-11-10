using UnityEngine;

[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "WaveData")]
public class WaveDataSO : ScriptableObject
{
    public TrainingRoomContent roomTemplate;
    public TrainingDefinitionSO training;
    public Vector2 playerStart = new Vector2(9f, -1.5f);
    public WaveRoomLayout room = new WaveRoomLayout();
    [Tooltip("기존 훈련의 카메라·목표 마무리·보스 연출을 보존하는 명시적 커스텀 모듈입니다.")]
    public bool customRoom;
    [Tooltip("커스텀 Room에서 저장된 실내 크기에 카메라를 맞춥니다. 일반 Room의 셀 연결 공간은 이 설정 없이 자동 적용합니다.")]
    public bool adaptCameraToRoom;
    public GameObject backgroundPrefab;
    [Tooltip("모든 Wave를 클리어한 뒤 나타나는 Lobby 복귀 포탈의 위치를 Tool에서 지정합니다.")]
    public bool overrideClearPortalPosition;
    public Vector2 clearPortalPosition;
    public WaveCellMap cellMap = new WaveCellMap();
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
    public float startDelay;
    public bool overridePlayerStart;
    public Vector2 playerStart;
    [Tooltip("지형 준비가 끝나면 이 Wave 시작 좌표로 이동하며 원본 Spawn 연출을 재생합니다.")]
    public bool teleportAtStart;
    public bool overrideCellMap;
    public WaveMapCell[] cells = System.Array.Empty<WaveMapCell>();
    public WaveCellBatch[] cellBatches = System.Array.Empty<WaveCellBatch>();
    public bool changeTerrain;
    public WaveRoomLayout room = new WaveRoomLayout();
    public WaveTerrainEvent[] terrainEvents = System.Array.Empty<WaveTerrainEvent>();
    public bool custom;
    public string customDescription;
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
    [Tooltip("0이면 시간 제한 없이 유지합니다. 생성 시점부터 센 초입니다.")]
    public float lifetime;
    public string goalKeyOverride;
}
