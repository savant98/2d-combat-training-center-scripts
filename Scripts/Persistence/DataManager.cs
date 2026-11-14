using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

#region [1] 통합 데이터 모델 정의 (SaveData 구조)

/// <summary>
/// 세이브 슬롯 하나의 진행 기록. 사용자 환경 설정(OptionSettings)은 슬롯과 무관하게 GlobalSaveData에 둔다.
/// </summary>
///

[Serializable]
public class SaveData
{
    // Lobby 안내를 모두 마친 onboardingStep 값. 진행 중인 단계는 이보다 작은 값을 쓴다.
    public const int OnboardingDone = 99;

    public int version = 2;

    // 게임 최초 시작 여부. 새 슬롯은 true로 만들어지고, Nightmare 도입부를 마치고 Lobby에 도착하면 false가 된다.
    public bool isFirstStart = true;

    // Lobby 도착 뒤의 안내(단말기 메일 확인 → 훈련 메뉴) 진행 단계.
    public int onboardingStep;

    // 마지막으로 저장한 시각(UTC, ISO 8601)과 누적 플레이 시간(초). 세이브 선택 화면이 표시한다.
    public string lastPlayedUtc = "";
    public float playTimeSeconds;

    // [확장 영역] 플레이어 기본 정보 및 재화
    public int currentGold;

    // 훈련장 관련 클리어 및 베스트 기록 정보 리스트
    public List<TrainingRoomSaveData> trainingRoomRecords = new List<TrainingRoomSaveData>();

    // 무기 해금 및 업그레이드 정보 리스트
    public List<WeaponSaveData> weaponRecords = new List<WeaponSaveData>();
}

/// <summary>
/// 슬롯과 무관하게 한 벌만 두는 데이터(settings.json)
/// </summary>
[Serializable]
public class GlobalSaveData
{
    public int version = 1;
    public OptionSettings options = new OptionSettings();

    // 마지막으로 고른 슬롯(0부터, 없으면 -1). 타이틀을 거치지 않고 게임 Scene을 바로 여는 편집기 실행이 이 슬롯을 이어서 쓴다.
    public int lastSlot = -1;

    // 슬롯 기능 이전의 단일 세이브(player_save_data.json)를 확인했는지. 한 번만 옮긴다.
    public bool legacyChecked;
}

/// <summary>
/// 세이브 선택 화면이 한 칸을 그리는 데 필요한 요약. 파일을 읽기만 하고 현재 슬롯을 바꾸지 않는다.
/// </summary>
public struct SaveSlotSummary
{
    public int slot;
    public bool exists;        // 세이브 파일이 있는가
    public bool corrupted;     // 파일은 있으나 읽지 못했는가(고를 수 없고 지울 수만 있다)
    public bool isFirstStart;
    public int gold;
    public float playTimeSeconds;
    public DateTime lastPlayedLocal;   // 기록이 없으면 DateTime.MinValue
    public TrainingRoomSaveData[] trainingRecords;

    /// <summary>수료한 훈련의 기록만 돌려준다. 없으면 null.</summary>
    public TrainingRoomSaveData FindCleared(string trainingId)
    {
        if (trainingRecords == null || string.IsNullOrEmpty(trainingId)) return null;
        foreach (var record in trainingRecords)
            if (record != null && record.trainingId == trainingId && DataManager.IsCleared(record)) return record;
        return null;
    }
}

/// <summary>
/// 훈련장 개별 방의 기록 데이터
/// </summary>
[Serializable]
public class TrainingRoomSaveData
{
    public string trainingId;

    public int bestHitCount = int.MaxValue;
    public float bestTimeSeconds = float.MaxValue;
    public Grade bestGrade = Grade.Poor;
}

/// <summary>
/// 개별 무기의 해금 및 성장 데이터
/// </summary>
[Serializable]
public class WeaponSaveData
{
    public string weaponId;
    public bool isUnlocked;
    public bool isLeftNodeUpgraded;
    public bool isRightNodeUpgraded;
}

#endregion

#region [2] DataManager 구현 (매니저 클래스)
public class DataManager : MonoBehaviour
{
    public const int SlotCount = 4;
    private const string TitleSceneName = "TitleScene";
    private const string SettingsFileName = "settings.json";
    private const string LegacyFileName = "player_save_data.json";

    // 검증 도구가 사용자의 실제 세이브를 건드리지 않도록 저장 폴더를 바꾼다. 비어 있으면 persistentDataPath를 쓴다.
    // 편집기의 도메인 리로드가 꺼져 있으면 값이 남으므로, 쓴 도구가 끝날 때 null로 되돌린다.
    public static string DirectoryOverride;

    private static DataManager instance;
    public static DataManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<DataManager>();
                if (instance == null)
                {
                    GameObject go = new GameObject("DataManager");
                    instance = go.AddComponent<DataManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return instance;
        }
    }
    public OptionSettings Options => global.options ??= new OptionSettings();

    public bool SaveOptions(OptionSettings options)
    {
        var previous = global.options;
        global.options = options.Copy();
        if (TrySaveGlobal()) return true;
        global.options = previous;
        return false;
    }

    // 슬롯과 무관한 데이터(환경 설정, 마지막 슬롯)
    private GlobalSaveData global = new GlobalSaveData();
    private string saveDirectory;
    private string settingsFilePath;

    // 현재 슬롯의 파일 경로. 슬롯을 고르기 전에는 null이고 SaveGame은 아무것도 쓰지 않는다.
    private string slotFilePath;

    /// <summary>현재 고른 슬롯(0부터). 고르기 전에는 -1이다.</summary>
    public int CurrentSlot { get; private set; } = -1;
    public bool HasSelectedSlot => CurrentSlot >= 0;

    // 게임 내부 시스템(UI, 플레이어 등)에서 실시간으로 읽고 쓸 데이터 원본
    // 슬롯을 고르기 전(타이틀)에는 저장되지 않는 빈 데이터다.
    public SaveData CurrentSaveData { get; private set; } = new SaveData();

    // 런타임 중 고속 조회를 위한 딕셔너리 캐시 레이어 (JsonUtility 직렬화 한계 극복)
    private Dictionary<string, WeaponSaveData> weaponCache = new Dictionary<string, WeaponSaveData>();
    private Dictionary<string, TrainingRoomSaveData> trainingCache = new Dictionary<string, TrainingRoomSaveData>();

    // 슬롯 기능 이전의 단일 세이브 파일 구조(읽기 전용)
    [Serializable]
    private class LegacySaveData
    {
        public OptionSettings options;
        public int currentGold;
        public bool trainingNoticeSigned;
        public List<TrainingRoomSaveData> trainingRoomRecords;
        public List<WeaponSaveData> weaponRecords;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        // 영구 저장 장치 경로 설정
        saveDirectory = string.IsNullOrEmpty(DirectoryOverride) ? Application.persistentDataPath : DirectoryOverride;
        Directory.CreateDirectory(saveDirectory);
        settingsFilePath = Path.Combine(saveDirectory, SettingsFileName);

        // 게임 켜지면 환경 설정만 자동 로드. 진행 기록은 타이틀에서 슬롯을 고를 때 읽는다.
        LoadGame();
    }

    // 타이틀을 거치지 않고 게임 Scene이 바로 열리면(편집기에서 MainScene을 Play, 검증 도구) 마지막 슬롯을 이어서 쓴다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void SelectSlotWhenTitleSkipped()
    {
        if (SceneManager.GetActiveScene().name == TitleSceneName) return;
        if (!Instance.HasSelectedSlot) Instance.SelectFallbackSlot();
    }

    // 플레이 시간은 여기에 모으고 세이브에는 값만 옮긴다(프레임마다 float에 바로 더하면 몇 시간 뒤부터 더하는 값이 반올림으로 줄어든다).
    private double playTime;
    private float writtenPlayTime = -1f;

    private void Update()
    {
        if (!HasSelectedSlot) return;
        // 슬롯을 바꿨거나 세이브를 다시 읽었으면 그 세이브의 값에서 이어 센다.
        if (CurrentSaveData.playTimeSeconds != writtenPlayTime) playTime = CurrentSaveData.playTimeSeconds;
        // 멈춰 있던 편집기가 한 번에 넘겨주는 긴 프레임은 플레이 시간으로 세지 않는다.
        playTime += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
        writtenPlayTime = CurrentSaveData.playTimeSeconds = (float)playTime;
    }

    // 게임을 끌 때 마지막 접속 시각과 플레이 시간을 남긴다.
    // 저장할 일이 없던 접속(훈련을 수료하지 않고 끈 경우)도 세이브 선택 화면의 "마지막 접속"·"플레이"에 반영된다.
    private void OnApplicationQuit()
    {
        if (instance == this && HasSelectedSlot) TrySaveGame();
    }

    /// <summary>
    /// 현재 슬롯의 런타임 데이터를 JSON 파일로 파일에 영구 저장
    /// </summary>
    public void SaveGame() => TrySaveGame();

    private bool TrySaveGame()
    {
        // 슬롯을 고르기 전(타이틀)에는 진행 기록을 쓰지 않는다.
        if (!HasSelectedSlot || string.IsNullOrEmpty(slotFilePath)) return false;

        try
        {
            // 저장하기 직전에, 캐시(Dictionary)에 있는 최신 데이터들을 파일 저장용 리스트(List) 동기화
            SyncCacheToList();
            CurrentSaveData.lastPlayedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            string json = JsonUtility.ToJson(CurrentSaveData, true); // true 옵션으로 JSON을 가독성 좋게 정렬
            WriteAtomic(slotFilePath, json);
            Debug.Log($"[DataManager] 데이터 저장 완료: {slotFilePath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] 데이터 저장 실패: {e.Message}");
            return false;
        }
    }

    private bool TrySaveGlobal()
    {
        try
        {
            WriteAtomic(settingsFilePath, JsonUtility.ToJson(global, true));
            Debug.Log($"[DataManager] 데이터 저장 완료: {settingsFilePath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] 환경 설정 저장 실패: {e.Message}");
            return false;
        }
    }

    // 쓰는 도중 꺼져도 기존 파일이 깨지지 않도록 임시 파일에 쓴 뒤 바꿔 끼운다.
    private static void WriteAtomic(string path, string json)
    {
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json);
        if (File.Exists(path)) File.Replace(temporaryPath, path, null);
        else File.Move(temporaryPath, path);
    }

    /// <summary>
    /// 환경 설정과, 고른 슬롯이 있으면 그 슬롯의 진행 기록을 JSON 파일로부터 다시 불러옴
    /// </summary>
    public void LoadGame()
    {
        LoadGlobal();

        if (HasSelectedSlot)
        {
            SaveData data = File.Exists(slotFilePath) ? ReadSlot(slotFilePath) : null;
            if (data == null) Debug.LogError("[DataManager] 데이터 로드 실패 (데이터 강제 초기화)");
            CurrentSaveData = data ?? new SaveData();
        }

        // 파일에서 불러온 리스트 데이터를 런타임 검색 효율을 위해 딕셔너리 캐시로 복사
        GenerateCache();
    }

    private void LoadGlobal()
    {
        global = null;
        bool settingsExisted = File.Exists(settingsFilePath);
        try
        {
            if (settingsExisted)
                global = JsonUtility.FromJson<GlobalSaveData>(File.ReadAllText(settingsFilePath));
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] 환경 설정 로드 실패 (기본값 사용): {e.Message}");
        }

        global ??= new GlobalSaveData();
        global.options ??= new OptionSettings();
        global.options.Sanitize();
        if (!IsValidSlot(global.lastSlot)) global.lastSlot = -1;
        if (!global.legacyChecked) MigrateLegacySave(!settingsExisted);
    }

    // 슬롯 기능 이전의 단일 세이브를 한 번만 옮긴다: 환경 설정은 settings.json으로, 진행 기록은 비어 있는 1번 슬롯으로.
    // 원본 파일은 지우지 않는다(같은 PC에서 이전 버전을 함께 실행할 수 있다).
    // takeOptions: 환경 설정 파일이 아직 없을 때만 옛 세이브의 환경 설정을 가져온다(다시 시도하는 실행에서 그 사이 바꾼 설정을 덮지 않는다).
    private void MigrateLegacySave(bool takeOptions)
    {
        // 파일을 읽거나 쓰지 못했으면(다른 프로그램이 잡고 있는 경우 등) 확인을 끝내지 않고 다음 실행 때 다시 옮긴다. 원본은 그대로 있다.
        // 파일의 내용이 잘못된 경우는 다시 해도 같으므로 확인을 끝낸다.
        bool finished = true;
        string legacyPath = Path.Combine(saveDirectory, LegacyFileName);
        try
        {
            if (File.Exists(legacyPath))
            {
                var legacy = JsonUtility.FromJson<LegacySaveData>(File.ReadAllText(legacyPath));
                if (legacy != null)
                {
                    if (takeOptions && legacy.options != null)
                    {
                        global.options = legacy.options;
                        global.options.Sanitize();
                    }

                    bool hasProgress = legacy.currentGold != 0 || legacy.trainingNoticeSigned
                        || (legacy.trainingRoomRecords != null && legacy.trainingRoomRecords.Count > 0)
                        || (legacy.weaponRecords != null && legacy.weaponRecords.Count > 0);
                    string target = SlotPath(0);
                    if (hasProgress && !File.Exists(target))
                    {
                        var migrated = new SaveData
                        {
                            // 이미 플레이하던 기록이므로 도입부와 Lobby 안내를 다시 강제하지 않는다.
                            isFirstStart = false,
                            onboardingStep = SaveData.OnboardingDone,
                            currentGold = legacy.currentGold,
                            lastPlayedUtc = File.GetLastWriteTimeUtc(legacyPath).ToString("o", CultureInfo.InvariantCulture),
                            trainingRoomRecords = legacy.trainingRoomRecords ?? new List<TrainingRoomSaveData>(),
                            weaponRecords = legacy.weaponRecords ?? new List<WeaponSaveData>(),
                        };
                        WriteAtomic(target, JsonUtility.ToJson(migrated, true));
                        Debug.Log($"[DataManager] 이전 세이브를 1번 슬롯으로 옮겼습니다: {target}");
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            finished = false;
            Debug.LogError($"[DataManager] 이전 세이브 옮기기 실패(다음 실행 때 다시 시도): {e.Message}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] 이전 세이브 옮기기 실패: {e.Message}");
        }

        global.legacyChecked = finished;
        TrySaveGlobal();
    }

    /// <summary>
    /// 현재 슬롯의 세이브 데이터를 완전히 포맷팅 (새 게임 시작 시 활용). 환경 설정은 슬롯과 따로 저장되므로 그대로다.
    /// </summary>
    public void ResetSaveData()
    {
        CurrentSaveData = new SaveData();
        GenerateCache();
        SaveGame();
        Debug.Log("[DataManager] 세이브 데이터가 초기화 되었습니다.");
    }

    #region 세이브 슬롯

    public static bool IsValidSlot(int slot) => slot >= 0 && slot < SlotCount;

    private string SlotPath(int slot) => Path.Combine(saveDirectory, $"save_slot_{slot + 1}.json");

    /// <summary>세이브 파일이 하나라도 있는가. 타이틀이 최초 실행 연출을 재생할지 정할 때 쓴다.</summary>
    public bool HasAnySave
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (File.Exists(SlotPath(i))) return true;
            return false;
        }
    }

    /// <summary>수료 기록인가. 실패 때 만들어지는 초기 기록(최댓값 그대로)은 수료가 아니다.</summary>
    public static bool IsCleared(TrainingRoomSaveData record) =>
        record != null && record.bestHitCount >= 0 && record.bestHitCount != int.MaxValue
        && record.bestTimeSeconds >= 0f && record.bestTimeSeconds != float.MaxValue
        && !float.IsNaN(record.bestTimeSeconds) && !float.IsInfinity(record.bestTimeSeconds);

    /// <summary>세이브 선택 화면용 요약. 현재 슬롯은 메모리의 값을, 다른 슬롯은 파일을 읽어 만든다.</summary>
    public SaveSlotSummary GetSlotSummary(int slot)
    {
        var summary = new SaveSlotSummary { slot = slot, trainingRecords = Array.Empty<TrainingRoomSaveData>() };
        if (!IsValidSlot(slot)) return summary;

        SaveData data;
        if (slot == CurrentSlot)
        {
            SyncCacheToList();
            data = CurrentSaveData;
            summary.exists = true;
        }
        else
        {
            string path = SlotPath(slot);
            if (!File.Exists(path)) return summary;
            summary.exists = true;
            data = ReadSlot(path);
            if (data == null)
            {
                summary.corrupted = true;
                return summary;
            }
        }

        summary.isFirstStart = data.isFirstStart;
        summary.gold = data.currentGold;
        summary.playTimeSeconds = data.playTimeSeconds;
        summary.trainingRecords = data.trainingRoomRecords.ToArray();
        if (DateTime.TryParse(data.lastPlayedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime played))
            summary.lastPlayedLocal = played.ToLocalTime();
        return summary;
    }

    /// <summary>
    /// 슬롯을 골라 현재 진행 기록으로 삼는다. 비어 있는 슬롯이면 새 세이브(최초 시작)를 만들어 바로 파일로 남긴다.
    /// 읽지 못하는 파일이면 false를 돌려주고 현재 슬롯을 바꾸지 않는다.
    /// </summary>
    public bool SelectSlot(int slot)
    {
        if (!IsValidSlot(slot)) return false;

        string path = SlotPath(slot);
        bool exists = File.Exists(path);
        SaveData data = exists ? ReadSlot(path) : new SaveData();
        if (data == null) return false;

        // 다른 슬롯으로 넘어가기 전에 고르던 슬롯의 변경을 남긴다.
        if (HasSelectedSlot && CurrentSlot != slot) TrySaveGame();

        CurrentSlot = slot;
        slotFilePath = path;
        CurrentSaveData = data;
        GenerateCache();

        if (!exists && !TrySaveGame())
        {
            ReleaseSlot();
            return false;
        }

        if (global.lastSlot != slot)
        {
            global.lastSlot = slot;
            TrySaveGlobal();
        }
        return true;
    }

    /// <summary>슬롯의 세이브 파일을 지운다. 지금 고른 슬롯이면 선택도 푼다.</summary>
    public bool DeleteSlot(int slot)
    {
        if (!IsValidSlot(slot)) return false;

        try
        {
            string path = SlotPath(slot);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] 세이브 삭제 실패: {e.Message}");
            return false;
        }

        if (slot == CurrentSlot) ReleaseSlot();
        if (global.lastSlot == slot)
        {
            global.lastSlot = -1;
            TrySaveGlobal();
        }
        return true;
    }

    /// <summary>Nightmare 도입부를 마치고 Lobby에 도착했다. 다음 접속부터는 Lobby에서 시작한다.</summary>
    public void CompleteFirstStart()
    {
        if (!HasSelectedSlot || !CurrentSaveData.isFirstStart) return;
        CurrentSaveData.isFirstStart = false;
        SaveGame();
    }

    /// <summary>Lobby 안내의 진행 단계를 남긴다. 같은 값이면 저장하지 않는다.</summary>
    public void SetOnboardingStep(int step)
    {
        if (!HasSelectedSlot || CurrentSaveData.onboardingStep == step) return;
        CurrentSaveData.onboardingStep = step;
        SaveGame();
    }

    private void ReleaseSlot()
    {
        CurrentSlot = -1;
        slotFilePath = null;
        CurrentSaveData = new SaveData();
        GenerateCache();
    }

    // 마지막 슬롯 → 가장 앞의 세이브 → 비어 있는 첫 슬롯을 새로 만든다.
    // 타이틀을 건너뛴 편집기 실행이므로 새로 만든 세이브에는 도입부와 Lobby 안내를 걸지 않는다.
    private void SelectFallbackSlot()
    {
        if (IsValidSlot(global.lastSlot) && File.Exists(SlotPath(global.lastSlot)) && SelectSlot(global.lastSlot)) return;

        for (int i = 0; i < SlotCount; i++)
            if (File.Exists(SlotPath(i)) && SelectSlot(i)) return;

        for (int i = 0; i < SlotCount; i++)
        {
            if (File.Exists(SlotPath(i))) continue; // 읽지 못한 파일은 덮어쓰지 않는다
            if (!SelectSlot(i)) continue;
            CurrentSaveData.isFirstStart = false;
            CurrentSaveData.onboardingStep = SaveData.OnboardingDone;
            TrySaveGame();
            return;
        }
    }

    private static SaveData ReadSlot(string path)
    {
        try
        {
            SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            if (data == null) return null;
            data.weaponRecords ??= new List<WeaponSaveData>();
            data.trainingRoomRecords ??= new List<TrainingRoomSaveData>();
            data.lastPlayedUtc ??= "";
            return data;
        }
        catch (Exception e)
        {
            // 읽지 못한 파일은 세이브 선택 화면이 '읽을 수 없는 세이브'로 보여 주고 지울 수 있게 한다.
            Debug.LogWarning($"[DataManager] 세이브 로드 실패: {path} ({e.Message})");
            return null;
        }
    }

    #endregion

    #region 내부 동기화 유틸리티 (JsonUtility 보완용)

    private void GenerateCache()
    {
        CurrentSaveData ??= new SaveData();
        CurrentSaveData.weaponRecords ??= new List<WeaponSaveData>();
        CurrentSaveData.trainingRoomRecords ??= new List<TrainingRoomSaveData>();
        weaponCache.Clear();
        foreach (var data in CurrentSaveData.weaponRecords)
        {
            if (!string.IsNullOrEmpty(data.weaponId))
                weaponCache[data.weaponId] = data;
        }

        trainingCache.Clear();
        foreach (var data in CurrentSaveData.trainingRoomRecords)
        {
            if (!string.IsNullOrEmpty(data.trainingId))
                trainingCache[data.trainingId] = data;
        }
    }

    private void SyncCacheToList()
    {
        CurrentSaveData.weaponRecords.Clear();
        CurrentSaveData.weaponRecords.AddRange(weaponCache.Values);

        CurrentSaveData.trainingRoomRecords.Clear();
        CurrentSaveData.trainingRoomRecords.AddRange(trainingCache.Values);
    }

    #endregion

    #region 외부 시스템 접근용 API (인터페이스)

    // =========================
    // [1] 무기 데이터 제어 API
    // =========================

    /// <summary>
    /// 무기의 해금 상태를 즉시 반환 (딕셔너리 검색)
    /// </summary>
    public bool IsWeaponUnlocked(string weaponId)
    {
        if (weaponCache.TryGetValue(weaponId, out var data))
        {
            return data.isUnlocked;
        }
        return false; // 데이터 없으면 잠금 상태가 기본
    }

    /// <summary>
    /// 무기의 특정 노드의 업그레이드 여부 반환
    /// </summary>
    public bool IsWeaponNodeUpgraded(string weaponId, bool isRightNode)
    {
        if (weaponCache.TryGetValue(weaponId, out var data))
        {
            return isRightNode ? data.isRightNodeUpgraded : data.isLeftNodeUpgraded;
        }
        return false;
    }

    /// <summary>
    /// 무기를 해금 또는 잠금 상태로 강제 변경
    /// </summary>
    public void SetWeaponUnlockState(string weaponId, bool isUnlocked)
    {
        if (!weaponCache.TryGetValue(weaponId, out var data))
        {
            data = new WeaponSaveData { weaponId = weaponId, isUnlocked = isUnlocked, isLeftNodeUpgraded = false, isRightNodeUpgraded = false };
            weaponCache[weaponId] = data;
        }
        else
        {
            data.isUnlocked = isUnlocked;
        }

        SaveGame();
    }

    /// <summary>
    /// 무기의 특정 노드 업그레이드 상태를 변경
    /// </summary>
    public void SetWeaponNodeUpgradeState(string weaponId, bool isRightNode, bool state)
    {
        if (weaponCache.TryGetValue(weaponId, out var data))
        {
            if (isRightNode)
                data.isRightNodeUpgraded = state;
            else
                data.isLeftNodeUpgraded = state;

            SaveGame();
        }
    }

    // ==========================
    // [2] 훈련장 데이터 제어 API
    // ==========================

    /// <summary>
    /// 특정 훈련장의 기록 정보를 반환
    /// </summary>
    public TrainingRoomSaveData GetTrainingRoomRecord(string trainingId)
    {
        if (trainingCache.TryGetValue(trainingId, out var data))
        {
            return data;
        }
        return null;
    }

    /// <summary>
    /// 훈련장 클리어 시 기록 갱신 및 파일 저장
    /// </summary>
    public void UpdateTrainingRecord(RunResult result)
    {
        if (!trainingCache.TryGetValue(result.trainingId, out var data))
        {
            data = new TrainingRoomSaveData { trainingId = result.trainingId };
            trainingCache[result.trainingId] = data;
        }

        if (!result.cleared)
            return; // 실패는 저장 안함 (원하면 제거 가능)

        // 최소 피격
        if (result.hitCount < data.bestHitCount)
            data.bestHitCount = result.hitCount;

        // 최소 시간
        if (result.timeSeconds < data.bestTimeSeconds)
            data.bestTimeSeconds = result.timeSeconds;

        // 최고 성적
        if (GradeRank(result.grade) > GradeRank(data.bestGrade))
            data.bestGrade = result.grade;

        SaveGame(); // 기록 갱신 및 저장
    }
    private int GradeRank(Grade grade) => grade switch
    {
        Grade.Flawless => 4,
        Grade.Excellent => 3,
        Grade.Pass => 2,
        Grade.Poor => 1,
        _ => 0
    };

    public int CurrentGold => CurrentSaveData != null ? CurrentSaveData.currentGold : 0;


    // 골드 획득
    public void AddGold(int amount)
    {
        if (amount <= 0)
            return;

        CurrentSaveData.currentGold += amount;
        SaveGame();

        Debug.Log($"[Gold] {amount}G 획득! 현재 골드: {CurrentSaveData.currentGold}G");
    }

    // 골드 소비
    public bool TrySpendGold(int amount)
    {
        if (CurrentSaveData.currentGold >= amount)
        {
            CurrentSaveData.currentGold -= amount;
            SaveGame();
            return true;
        }

        Debug.LogWarning("[Gold] 골드가 부족합니다!");
        return false;
    }

    #endregion
}

#endregion
