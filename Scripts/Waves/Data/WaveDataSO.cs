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
    [Tooltip("실내 치수 연출(TrainingRoomPresentation)을 가진 커스텀 Room에서 저장된 실내 크기에 카메라를 맞춥니다. 그 밖의 Room은 커스텀이어도 셀 연결 공간을 이 설정 없이 자동 적용합니다.")]
    public bool adaptCameraToRoom;
    [Tooltip("던전이 아닌 일반 Wave에서도 전투 훈련(던전)처럼 방 상하좌우 3칸 외벽을 카메라 범위에 포함하고 사방 검정색 블라인더(WaveRoomFloorShade)를 적용합니다.")]
    public bool useRoomBorderBlinder;
    public GameObject backgroundPrefab;
    [Tooltip("선택한 배경을 현재 방의 연결 공간에 맞춰 중심 정렬합니다. 원본 비율을 유지하며 빈 부분 없이 덮습니다.")]
    public bool fitBackgroundToRoom;
    [Tooltip("통창 유리와 반사를 표시합니다. 프레임과 케이블은 이 설정과 관계없이 유지합니다.")]
    public bool showObservationGlass = true;
    [Tooltip("Room 콘텐츠의 'TrainingBackdrop'을 이 Prefab으로 바꿉니다. 반복 모듈·시차·입장 로딩은 그대로 적용됩니다. 비우면 Room 원본 배경입니다.")]
    public GameObject roomBackdrop;
    [Tooltip("통창 전체(유리·프레임·테두리 빛·케이블)를 그리지 않습니다.")]
    public bool hideObservationWindow;
    [Tooltip("끄면 Room 콘텐츠에 원래 연결된 음악을 사용합니다. 켜고 Clip을 비우면 무음입니다.")]
    public bool overrideBgm;
    public AudioClip bgmClip;
    [Range(0f, 1f)] public float bgmVolume = .38f;
    [Min(0f)] public float bgmFadeDuration = .8f;
    [Tooltip("끄면 Room 콘텐츠의 공용 보스 음악을 사용합니다. 켜고 Clip을 비우면 보스가 등장해도 방 음악을 그대로 유지합니다.")]
    public bool overrideBossBgm;
    public AudioClip bossBgmClip;
    [Range(0f, 1f)] public float bossBgmVolume = .38f;
    [Tooltip("모든 Wave를 클리어한 뒤 나타나는 Lobby 복귀 포탈의 위치를 Tool에서 지정합니다.")]
    public bool overrideClearPortalPosition;
    public Vector2 clearPortalPosition;
    public WaveCellMap cellMap = new WaveCellMap();
    public Wave[] waves;

    public GameObject BackgroundAt(int index)
    {
        var wave = WaveAt(index);
        return wave != null && wave.overrideBackground ? wave.backgroundPrefab : backgroundPrefab;
    }
    public bool FitBackgroundAt(int index)
    {
        var wave = WaveAt(index);
        return wave != null && wave.overrideBackground ? wave.fitBackgroundToRoom : fitBackgroundToRoom;
    }
    public bool ShowObservationGlassAt(int index)
    {
        var wave = WaveAt(index);
        return wave != null && wave.overrideBackground ? wave.showObservationGlass : showObservationGlass;
    }
    // false면 Room 원본 음악을 사용한다. true와 null Clip의 조합은 명시적인 무음이다.
    public bool TryGetBgm(int index, out AudioClip clip, out float volume, out float fade)
    {
        var wave = WaveAt(index);
        bool useWave = wave != null && wave.overrideBgm;
        clip = useWave ? wave.bgmClip : bgmClip;
        float selectedVolume = useWave ? wave.bgmVolume : bgmVolume;
        float selectedFade = useWave ? wave.bgmFadeDuration : bgmFadeDuration;
        volume = float.IsFinite(selectedVolume) ? Mathf.Clamp01(selectedVolume) : .38f;
        fade = float.IsFinite(selectedFade) ? Mathf.Max(0f, selectedFade) : .8f;
        return useWave || overrideBgm;
    }
    // false면 Room 원본 보스 음악을 사용한다. true와 null Clip의 조합은 '보스가 등장해도 방 음악 유지'다.
    public bool TryGetBossBgm(out AudioClip clip, out float volume)
    {
        clip = overrideBossBgm ? bossBgmClip : null;
        volume = float.IsFinite(bossBgmVolume) ? Mathf.Clamp01(bossBgmVolume) : .38f;
        return overrideBossBgm;
    }
    private Wave WaveAt(int index) => waves != null && index >= 0 && index < waves.Length ? waves[index] : null;
}

public enum WaveClearType
{
    DefeatAllTargets,
    SurviveForTime,
    CustomTrigger,
    ReachGoal
}

[System.Serializable]
public class Wave
{
    public TutorialLesson tutorial = new TutorialLesson();
    [Tooltip("이 Wave만 별도 배경을 사용합니다. 끄면 WaveData 기본 배경을 사용하며, 켜고 Prefab을 비우면 Room 원본 배경입니다.")]
    public bool overrideBackground;
    public GameObject backgroundPrefab;
    public bool fitBackgroundToRoom = true;
    public bool showObservationGlass = true;
    [Tooltip("이 Wave만 별도 음악을 사용합니다. 켜고 Clip을 비우면 무음입니다. 보스 음악은 우선합니다.")]
    public bool overrideBgm;
    public AudioClip bgmClip;
    [Range(0f, 1f)] public float bgmVolume = .38f;
    [Min(0f)] public float bgmFadeDuration = .8f;
    public float startDelay;
    public bool overridePlayerStart;
    public Vector2 playerStart;
    [Tooltip("지형 준비가 끝나면 이 Wave 시작 좌표로 이동하며 원본 Spawn 연출을 재생합니다.")]
    public bool teleportAtStart;
    [Tooltip("직전 전투가 다음 입장 발판 근처에서 끝나도 크레인으로 옮깁니다.")]
    public bool alwaysCarryToStart;
    public bool overrideCellMap;
    public WaveMapCell[] cells = System.Array.Empty<WaveMapCell>();
    public WaveCellBatch[] cellBatches = System.Array.Empty<WaveCellBatch>();
    public bool changeTerrain;
    public WaveRoomLayout room = new WaveRoomLayout();
    public WaveTerrainEvent[] terrainEvents = System.Array.Empty<WaveTerrainEvent>();
    public bool custom;
    public string customDescription;
    [Tooltip("Wave 진행 연출입니다. 기본 진행은 처치하면 다음 Wave로 넘어가며 지형 패널과 크레인 연출을 씁니다. 던전은 방을 돌며 진행하고 클리어하면 크레인이 다음 던전으로 옮깁니다.")]
    public WaveFlowMode flowMode = WaveFlowMode.Sequential;
    [Tooltip("던전 Wave의 생성 설정입니다(진행 연출이 던전일 때만 씁니다).")]
    public DungeonWaveSettings dungeon = new DungeonWaveSettings();
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
    [Tooltip("셀 중심을 기준으로 반시계 방향 90도씩 회전합니다.")]
    public int rotationSteps;
    public Quaternion Rotation => Quaternion.Euler(0, 0, ((rotationSteps % 4 + 4) % 4) * 90);
    [Tooltip("0이면 시간 제한 없이 유지합니다. 생성 시점부터 센 초입니다.")]
    public float lifetime;
    public string goalKeyOverride;
    [Tooltip("이 배치에만 적용하는 장치 설정입니다. Prefab 기본값과 다른 값만 저장합니다.")]
    public WaveDeviceSetting[] deviceSettings = System.Array.Empty<WaveDeviceSetting>();
}
