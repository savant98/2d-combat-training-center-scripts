using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Overlays;
using UnityEngine;

#region [1] 통합 데이터 모델 정의 (SaveData 구조)

/// <summary>
/// 게임의 모든 영구 저장 데이터를 통합 관리하는 최상위 클래스
/// </summary>
/// 

[Serializable]
public class SaveData
{
    // [확장 영역] 플레이어 기본 정보 및 재화
    public int currentGold;

    // 훈련장 관련 클리어 및 베스트 기록 정보 리스트
    public List<TrainingRoomSaveData> trainingRoomRecords = new List<TrainingRoomSaveData>();

    // 무기 해금 및 업그레이드 정보 리스트
    public List<WeaponSaveData> weaponRecords = new List<WeaponSaveData>();
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
    private string saveFilePath;

    // 게임 내부 시스템(UI, 플레이어 등)에서 실시간으로 읽고 쓸 데이터 원본
    public SaveData CurrentSaveData { get; private set; } = new SaveData();

    // 런타임 중 고속 조회를 위한 딕셔너리 캐시 레이어 (JsonUtility 직렬화 한계 극복)
    private Dictionary<string, WeaponSaveData> weaponCache = new Dictionary<string, WeaponSaveData>();
    private Dictionary<string, TrainingRoomSaveData> trainingCache = new Dictionary<string, TrainingRoomSaveData>();

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
        saveFilePath = Path.Combine(Application.persistentDataPath, "player_save_data.json");

        // 게임 켜지면 자동 로드
        LoadGame();
    }

    /// <summary>
    /// 현재 런타임 데이터를 JSON 파일로 파일에 영구 저장
    /// </summary>
    public void SaveGame()
    {
        try
        {
            // 저장하기 직전에, 캐시(Dictionary)에 있는 최신 데이터들을 파일 저장용 리스트(List) 동기화
            SyncCacheToList();

            string json = JsonUtility.ToJson(CurrentSaveData, true); // true 옵션으로 JSON을 가독성 좋게 정렬
            File.WriteAllText(saveFilePath, json);
            Debug.Log($"[DataManager] 데이터 저장 완료: {saveFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] 데이터 저장 실해: {e.Message}");
        }
    }

    /// <summary>
    /// JSON 파일로부터 세이브 데이터를 불러옴
    /// </summary>
    public void LoadGame()
    {
        try
        {
            if (File.Exists(saveFilePath))
            {
                string json = File.ReadAllText(saveFilePath);
                CurrentSaveData = JsonUtility.FromJson<SaveData>(json);
                Debug.Log("[DataManager] 기존 세이브 파일을 성공적으로 로드했습니다.");
            }
            else
            {
                Debug.Log("[DataManager] 세이브 파일이 없어 완전히 새로운 데이터셋을 생성합니다");
                CurrentSaveData = new SaveData();
            }

            // 파일에서 불러온 리스트 데이터를 런타임 검색 효율을 위해 딕셔너리 캐시로 복사
            GenerateCache();
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] 데이터 로드 실패 (데이터 강제 초기화): {e.Message}");
            CurrentSaveData = new SaveData();
            GenerateCache();
        }
    }

    /// <summary>
    /// 세이브 데이터를 완전히 포맷팅 (새 게임 시작 시 활용)
    /// </summary>
    public void ResetSaveData()
    {
        CurrentSaveData = new SaveData();
        GenerateCache();
        SaveGame();
        Debug.Log("[DataManager] 세이브 데이터가 초기화 되었습니다.");
    }

    #region 내부 동기화 유틸리티 (JsonUtility 보완용)

    private void GenerateCache()
    {
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
