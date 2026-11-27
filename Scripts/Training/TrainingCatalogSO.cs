using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 훈련 목록의 원본(2026-11-19 훈련 개편). 목록의 순서가 곧 진행 순서다.
/// - 훈련 메뉴는 여기서 listed 항목을 순서대로 읽고, 아직 열리지 않은 훈련은 목록에 내지 않는다.
/// - 진행 사슬: alwaysAvailable(테스트 훈련)을 뺀 항목들의 순서. 사슬의 첫 항목은 처음부터 열려 있고, 그 뒤는 앞선 과정을 모두 수료하면 열린다.
///   훈련 메뉴에 나오지 않는 항목(listed 꺼짐, 각개 전투)도 사슬에는 들어간다.
/// - 훈련 필증: grantsCertificate 항목을 처음 수료하면 1개. 지급과 기록은 DataManager가 하고, 여기서는 대상인지와 표시 이름·아이콘만 정한다.
/// Resources/Training/TrainingCatalog 에 한 벌만 둔다(WaveRoomRules와 같은 방식). 읽기 전용 데이터이며 실행 중에 바꾸지 않는다.
/// </summary>
[CreateAssetMenu(menuName = "Training/TrainingCatalog", fileName = "TrainingCatalog")]
public sealed class TrainingCatalogSO : ScriptableObject
{
    public const string ResourcePath = "Training/TrainingCatalog";
    public const string DefaultCertificateName = "훈련 필증";

    [Serializable]
    public sealed class Entry
    {
        [Tooltip("기록과 성적 조건의 원본. trainingId가 세이브 키다")]
        public TrainingDefinitionSO definition;
        [Tooltip("이 훈련으로 입장할 때 TrainingRoom에 불러올 WaveData")]
        public WaveDataSO waveData;
        [Tooltip("훈련 메뉴의 제목. 비우면 definition.displayName을 쓴다")]
        public string title;
        [TextArea] public string summary;
        [Tooltip("훈련 메뉴에 나오는가. 끄면 다른 경로(Lobby 엘리베이터)로만 입장한다")]
        public bool listed = true;
        [Tooltip("선행 조건 없이 항상 열려 있는가(테스트 훈련). 진행 사슬에 들어가지 않는다")]
        public bool alwaysAvailable;
        [Tooltip("처음 수료하면 훈련 필증 1개를 주는가")]
        public bool grantsCertificate = true;

        /// <summary>세이브 키. 정의가 비어 있으면 빈 문자열이다.</summary>
        public string TrainingId => definition != null && definition.trainingId != null ? definition.trainingId : "";
        public string Title => !string.IsNullOrEmpty(title) ? title : definition != null ? definition.displayName : "";
    }

    [Tooltip("진행 순서대로 적는다")]
    public Entry[] entries = Array.Empty<Entry>();

    [Tooltip("테스트를 위해 등록한 모든 과정을 바로 선택합니다. 수료 기록·과정 번호·필증은 실제 진행 기준을 따릅니다.")]
    public bool openAllForTesting;

    [Header("재화(훈련 필증) 표시")]
    public string certificateName = DefaultCertificateName;
    public Sprite certificateIcon;

    private static TrainingCatalogSO cached;
    private static bool missingReported;

    // 편집기의 도메인 리로드가 꺼져 있어도 Play마다 다시 읽고, 에셋이 없으면 다시 알린다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cached = null;
        missingReported = false;
    }

    /// <summary>Resources의 훈련 목록. 에셋이 없으면 null(한 번만 경고한다).</summary>
    public static TrainingCatalogSO Load()
    {
        if (cached != null) return cached;
        cached = Resources.Load<TrainingCatalogSO>(ResourcePath);
        if (cached == null && !missingReported)
        {
            missingReported = true;
            Debug.LogWarning($"[TrainingCatalog] Resources/{ResourcePath} 에셋이 없습니다. 훈련 목록과 훈련 필증 지급이 동작하지 않습니다.");
        }
        return cached;
    }

    /// <summary>재화의 표시 이름. 목록 에셋이 없거나 이름이 비어 있어도 기본 이름을 돌려준다.</summary>
    public static string CertificateName
    {
        get
        {
            var catalog = Load();
            return catalog != null && !string.IsNullOrEmpty(catalog.certificateName) ? catalog.certificateName : DefaultCertificateName;
        }
    }

    /// <summary>재화 아이콘. 정해지지 않았으면 null(표시하는 쪽은 원래 그림을 그대로 둔다).</summary>
    public static Sprite CertificateIcon
    {
        get
        {
            var catalog = Load();
            return catalog != null ? catalog.certificateIcon : null;
        }
    }

    public int Count => entries != null ? entries.Length : 0;

    /// <summary>순서의 항목. 범위 밖이면 null.</summary>
    public Entry GetEntry(int index) => index >= 0 && index < Count ? entries[index] : null;

    public int IndexOf(string trainingId)
    {
        if (string.IsNullOrEmpty(trainingId)) return -1;
        for (int i = 0; i < Count; i++)
            if (IsValid(entries[i]) && entries[i].definition.trainingId == trainingId) return i;
        return -1;
    }

    public int IndexOf(Entry entry)
    {
        if (entry == null) return -1;
        for (int i = 0; i < Count; i++)
            if (ReferenceEquals(entries[i], entry)) return i;
        return -1;
    }

    /// <summary>trainingId의 항목. 없으면 null.</summary>
    public Entry Find(string trainingId) => GetEntry(IndexOf(trainingId));

    /// <summary>처음 수료하면 훈련 필증을 주는 훈련인가. 목록에 없는 훈련은 대상이 아니다.</summary>
    public bool GrantsCertificate(string trainingId)
    {
        var entry = Find(trainingId);
        return entry != null && entry.grantsCertificate;
    }

    /// <summary>
    /// 테스트 전체 개방 또는 상시 항목이면 바로 입장하고, 일반 진행은 앞선 과정을 모두 수료하면 입장한다.
    /// data가 null이면 기록이 없는 것으로 본다.
    /// </summary>
    public bool IsUnlocked(int index, DataManager data)
    {
        var entry = GetEntry(index);
        if (!IsValid(entry)) return false;
        return openAllForTesting || entry.alwaysAvailable || AllProgressionCleared(entry.TrainingId, data);
    }

    public bool IsUnlocked(Entry entry, DataManager data) => IsUnlocked(IndexOf(entry), data);
    public bool IsUnlocked(string trainingId, DataManager data) => IsUnlocked(IndexOf(trainingId), data);

    /// <summary>
    /// 진행 사슬에서 untilTrainingId보다 앞선 훈련을 모두 수료했는가(Lobby 엘리베이터 조건: 각개 전투 앞의 훈련 전부).
    /// untilTrainingId를 비우면 사슬 전체를 본다. 목록에 없는 ID이거나 사슬이 비어 있으면 false.
    /// </summary>
    public bool AllProgressionCleared(string untilTrainingId, DataManager data)
    {
        bool whole = string.IsNullOrEmpty(untilTrainingId);
        int end = whole ? Count : IndexOf(untilTrainingId);
        if (end < 0) return false;
        int chained = 0;
        for (int i = 0; i < end; i++)
        {
            if (!InChain(entries[i])) continue;
            if (!IsCleared(entries[i], data)) return false;
            chained++;
        }
        return chained > 0 || !whole;
    }

    /// <summary>진행 사슬에서 몇 번째 훈련인가(1부터). 사슬에 들지 않는 항목(테스트 훈련)은 0.</summary>
    public int ProgressionNumber(Entry entry)
    {
        int index = IndexOf(entry);
        if (index < 0 || !InChain(entry)) return 0;
        int number = 0;
        for (int i = 0; i <= index; i++)
            if (InChain(entries[i])) number++;
        return number;
    }

    /// <summary>훈련 메뉴에 지금 보일 항목을 순서대로 담는다: listed이고, 입장할 WaveData가 있고, 열려 있는 훈련.</summary>
    public void CollectListed(DataManager data, List<Entry> results)
    {
        if (results == null) return;
        results.Clear();
        for (int i = 0; i < Count; i++)
        {
            var entry = entries[i];
            if (IsValid(entry) && entry.listed && entry.waveData != null && IsUnlocked(i, data)) results.Add(entry);
        }
    }

    /// <summary>이 훈련을 수료했는가(통과 이상의 기록이 있는가).</summary>
    public static bool IsCleared(Entry entry, DataManager data) =>
        data != null && IsValid(entry) && DataManager.IsCleared(data.GetTrainingRoomRecord(entry.definition.trainingId));

    // 정의와 trainingId가 있어야 기록을 찾을 수 있다. 비어 있는 항목은 없는 것으로 다룬다.
    private static bool IsValid(Entry entry) =>
        entry != null && entry.definition != null && !string.IsNullOrEmpty(entry.definition.trainingId);

    private static bool InChain(Entry entry) => IsValid(entry) && !entry.alwaysAvailable;
}
