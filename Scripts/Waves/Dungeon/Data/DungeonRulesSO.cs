using UnityEngine;

/// <summary>
/// 던전 Wave 공통 규칙(Resources/Training/DungeonRules.asset). 공용 WaveRoomRules는 고치지 않고 던전 전용 값만 여기에 둔다.
/// </summary>
[CreateAssetMenu(menuName = "Training/Dungeon Rules", fileName = "DungeonRules")]
public class DungeonRulesSO : ScriptableObject
{
    public const string ResourcePath = "Training/DungeonRules";
    /// <summary>던전 Wave 실행 사본의 CustomTrigger 키. 던전 조건을 채우면 DungeonManager가 이 키로 Wave를 끝낸다.</summary>
    public const string ClearTriggerKey = "Dungeon.Clear";

    private static DungeonRulesSO cached;
    public static DungeonRulesSO Default
    {
        get
        {
            if (cached == null) cached = Resources.Load<DungeonRulesSO>(ResourcePath);
            return cached;
        }
    }

    [Header("방 이동")]
    [Tooltip("Wave의 던전 설정에 연출이 없을 때 쓰는 기본 방 이동 연출입니다.")]
    public DungeonPassageEffectSO defaultPassage;
    [Tooltip("관 통과 중 화면을 덮는 오버레이 UI Prefab입니다(UI Canvas 아래에 실행 중 생성).")]
    public GameObject passageOverlayPrefab;

    [Header("방 진행")]
    [Tooltip("클리어 안 한 방에 들어온 뒤 출입구를 잠그기까지의 시간(초)입니다.")]
    [Min(0f)] public float lockDelay = .25f;
    [Tooltip("잠근 뒤 첫 적 구성 항목을 시작하기까지의 시간(초)입니다.")]
    [Min(0f)] public float encounterStartDelay = .5f;
    [Tooltip("방을 클리어한 뒤 출입구를 여는 시간(초)입니다.")]
    [Min(0f)] public float unlockDelay = .6f;
    [Tooltip("던전 조건을 채운 뒤 Wave를 끝내기까지의 시간(초)입니다(마지막 방 클리어 연출을 보여 준다).")]
    [Min(0f)] public float floorClearDelay = 1.2f;

    [Header("방 상태 표시·적 위치")]
    [Tooltip("화면 위 가운데 방 상태 띠와 적 위치 마커 Prefab(UI Canvas에 실행 중 생성).")]
    public GameObject roomStatusPrefab;
    [Tooltip("남은 필수 적이 이 수 이하이면 위치를 표시한다(생성 완료·1마리 이상 처치 뒤).")]
    [Min(0)] public int markerThreshold = 3;
    [Tooltip("필수 적이 화면 밖에 이 시간(초) 넘게 있으면 수와 관계없이 화살표를 띄운다.")]
    [Min(0f)] public float offscreenRevealDelay = 4f;
    [Tooltip("방 클리어 때 화면 흔들림(세기·시간).")]
    [Min(0f)] public float clearShakeIntensity = .15f;
    [Min(0f)] public float clearShakeDuration = .15f;

    [Header("텔레포트·시설")]
    [Tooltip("텔레포트 시설로 방을 옮기는 연출.")]
    public DungeonPassageEffectSO teleportPassage;
    [Tooltip("방 클리어 뒤 시설(점프대 제외)을 Player에 가까운 것부터 차례로 켜는 간격(초).")]
    [Min(0f)] public float facilityPowerStagger = .12f;

    [Header("정적 벽면 키트(배경 Wall 방)")]
    [Tooltip("키트 종류별 Prefab. 비어 있으면 그 키트 방은 시차 배경으로 남는다.")]
    public WallKitEntry[] wallKits = System.Array.Empty<WallKitEntry>();

    [System.Serializable]
    public sealed class WallKitEntry
    {
        public DungeonWallKit kit;
        public GameObject prefab;
    }

    public GameObject WallKitPrefab(DungeonWallKit kit)
    {
        if (wallKits == null) return null;
        foreach (var entry in wallKits) if (entry != null && entry.kit == kit) return entry.prefab;
        return null;
    }

    [Header("미니맵")]
    [Tooltip("UI Canvas 아래에 실행 중 생성하는 미니맵 Prefab입니다.")]
    public GameObject minimapPrefab;

    /// <summary>던전 진행 중 Tab으로 여는 현장 단말기 Prefab(Resources/UI/DungeonFieldTerminal). UI Canvas 아래에 실행 중 생성한다.</summary>
    [Header("현장 단말기·목표 패널(3차)")]
    [Tooltip("던전 진행 중 Tab으로 여는 현장 단말기 Prefab입니다(UI Canvas에 실행 중 생성).")]
    public GameObject fieldTerminalPrefab;
    /// <summary>훈련 상황판 바로 뒤에 붙는 목표 진행 패널 Prefab(Resources/UI/DungeonObjectivePanel). IDungeonObjectiveView로 연결한다.</summary>
    [Tooltip("훈련 상황판 바로 뒤에 붙는 목표 진행 패널 Prefab입니다(예: LEVERS 1/3).")]
    public GameObject objectivePanelPrefab;

    /// <summary>지금 방의 목표 대상(레버·출구)을 화면에 표시하는 마커 Prefab(Resources/UI/DungeonObjectiveMarkers). IDungeonObjectiveTargetsView로 연결한다(4차).</summary>
    [Header("목표 마커(4차)")]
    [Tooltip("지금 방의 레버·출구 위치를 표시하는 마커 Prefab입니다(UI Canvas에 실행 중 생성, 화면 밖이면 가장자리 화살표).")]
    public GameObject objectiveMarkersPrefab;

    [Header("효과(임시: 기존 효과음 재사용 — 나중에 CC0 청취 후보로 교체)")]
    public FeedbackDataSO doorLockFeedback;
    public FeedbackDataSO doorUnlockFeedback;
    public FeedbackDataSO passageEnterFeedback;
    public FeedbackDataSO passageExitFeedback;
    public FeedbackDataSO roomClearFeedback;
}
