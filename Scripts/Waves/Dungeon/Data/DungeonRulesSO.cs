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

    /// <summary>
    /// 6차 T11 컷씬(DungeonCutsceneDirector). 레버 컷씬 = 당김 → 덮개 → 시작 방(출구 방) 미리보기(상태 변경 없음) → 볼트 풀림·램프 점등(마지막 레버는 문 열림) → 덮개 → 레버 방.
    /// 문 컷씬 = 흡입 → 문짝 닫힘·봉인 → 다음 Wave는 도착 문에서 걸어 나오며 시작(크레인 생략). 끄면 4차 지연 재생·크레인 운반으로 돌아간다(회귀용 스위치).
    /// 시간 값은 게임 시간(초)이며 판단값이다.
    /// </summary>
    [Header("컷씬(6차 T11)")]
    [Tooltip("레버를 당기면 출구 방을 잠깐 보여 주는 컷씬을 켭니다. 끄면 출구 문은 시작 방에 돌아왔을 때 지연 재생합니다(4차).")]
    public bool leverCutscene = true;
    [Tooltip("출구 문에 들어가면 문이 닫히는 컷씬과, 다음 Wave를 도착 문에서 걸어 나오며 시작하는 연출을 켭니다. 끄면 크레인으로 옮깁니다(4차).")]
    public bool exitCutscene = true;
    [Tooltip("컷씬 위아래 검은 띠(CinematicLetterbox Prefab). 비우면 띠 없이 진행합니다.")]
    public CinematicLetterbox letterboxPrefab;
    [Tooltip("레버 컷씬이 시작 방으로 넘어갈 때 화면을 덮는 방식: 관 속 오버레이(시작 방 쪽 방향) / 암전.")]
    public DungeonCutsceneCover cutsceneCover = DungeonCutsceneCover.Pipe;
    [Tooltip("레버를 당긴 뒤 덮개를 치기까지 레버 연출을 보여 주는 시간(초, 손잡이 .6초 + 빗장).")]
    [Min(0f)] public float leverWatchTime = .75f;
    [Tooltip("덮개가 화면을 다 덮는 시간(초).")]
    [Min(.02f)] public float cutsceneCoverTime = .2f;
    [Tooltip("덮개가 걷히는 시간(초).")]
    [Min(.02f)] public float cutsceneRevealTime = .2f;
    [Tooltip("출구 문 연출(볼트·봉인·문짝)이 끝난 뒤 머무는 시간(초).")]
    [Min(0f)] public float exitRevealHold = .6f;
    [Tooltip("출구 문 연출을 기다리는 최대 시간(초). 넘으면 끝난 것으로 보고 돌아온다.")]
    [Min(.5f)] public float exitRevealLimit = 6f;
    [Tooltip("다음 Wave 시작 안내(WAVE n 판, 2초)가 뜬 뒤 도착 문이 열리기 시작하는 시간(초) — 판 끝 0.3초 전.")]
    [Min(0f)] public float arrivalOpenDelay = 1.7f;
    [Tooltip("도착 문이 열린 뒤 Player가 문 앞 선 자리로 걸어 나오는 시간(초).")]
    [Min(.05f)] public float arrivalWalkTime = .45f;
    [Tooltip("걸어 나와 서는 자리(문 가운데에서 방 안쪽으로 가로 칸 수).")]
    [Min(0f)] public float arrivalStandDistance = 2.2f;
    [Tooltip("출구 자리와 도착 자리가 다른 방 좌표일 때(암전 대체안) 암전이 내려오고 걷히는 시간(초).")]
    [Min(.05f)] public float arrivalFadeTime = .3f;
    [Tooltip("도착 문 Prefab(출구 문 Prefab을 도착 모드로 재사용). 비우면 이 훈련 목표 SO의 ExitPrefab을 씁니다.")]
    public GameObject arrivalGatePrefab;

    /// <summary>
    /// 난이도 상승 알림. 7차(DESIGN_V7 V8): 등급 알림과 다른 자리·다른 모양 — 오른쪽 HUD 열 미니맵 아래 '난이도 상승 계기'(DungeonThreatRise: 세로 5칸 사다리·빛 기둥·오르는 ▲,
    /// 약 3.65초)를 띄운다. 6차 T12의 훈련 상황판 등급 알림 재사용은 없앴다(등급 알림은 main 그대로). 띄울 곳의 판단은 DungeonThreatAlert(훈련 기록 중일 때만).
    /// </summary>
    [Header("난이도 알림(7차 V8 — 난이도 상승 계기)")]
    [Tooltip("난이도가 오른 순간 미니맵 아래에 '난이도 상승' 계기(5칸 사다리 + 빛 기둥)를 띄웁니다. 끄거나 훈련 기록 중이 아니면 상태 띠의 THREAT 단계 표시를 씁니다.")]
    public bool threatAlert = true;
    [Tooltip("계기 머리줄 오른쪽 작은 꼬리표(정적 글꼴 — 영문 대문자·숫자, 공백은 여백 태그로 바뀝니다). 머리줄 큰 글자는 '난이도 상승'입니다.")]
    public string threatAlertTitle = "THREAT";
    [Tooltip("계기 아래 줄에 적 출현 배율 변화(예: 적 출현 x1.00 ▸ x1.30)를 보입니다.")]
    public bool threatAlertDetail = true;
    [Tooltip("계기 새 칸 점화 순간 소리(기존 클립의 Feedback). 비우면 소리 없이 띄웁니다.")]
    public FeedbackDataSO threatAlertFeedback;
}

/// <summary>6차 레버 컷씬이 시작 방(출구 방)으로 넘어갈 때 화면을 덮는 방식.</summary>
public enum DungeonCutsceneCover
{
    /// <summary>관 속 오버레이(DungeonPassageOverlay — 던전 지도에서 시작 방 쪽 방향으로 흐른다).</summary>
    Pipe = 0,
    /// <summary>공용 암전(UIManager.StartScreenFade).</summary>
    Fade = 1,
}
