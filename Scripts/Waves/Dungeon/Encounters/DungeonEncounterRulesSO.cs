using System;
using UnityEngine;

/// <summary>
/// 동적 적 구성 규칙(4차 R16~R18, DESIGN_V4 §2.8). DungeonWaveSettings.encounterRules로 연결하고 encounterMode = Dynamic일 때 쓴다.
/// 데이터만 둔다 — 계산은 DungeonThreatTracker(단계)·DungeonEncounterComposer(구성)·DungeonSpawnPointMap(위치)가 한다.
/// 값은 모두 판단값(사용자 미확인)이며 Wave 격자 Tool의 던전 설정에서 이 SO를 골라 Inspector로 편집한다.
/// </summary>
[CreateAssetMenu(menuName = "Training/Dungeon Encounter Rules", fileName = "DungeonEncounterRules")]
public class DungeonEncounterRulesSO : ScriptableObject
{
    /// <summary>난이도 단계 수(입문·하급·중급·상급·특급).</summary>
    public const int LevelCount = 5;

    [Header("적 수")]
    [Tooltip("기준 밀집도 D0: 빈 칸 100칸당 마리 수(Bulwark = 3 환산). 3차 템플릿 평균을 빈 칸 기준으로 잰 값입니다.")]
    [Min(0f)] public float baseDensity = 1.35f;
    [Tooltip("단계별 적 수 배율(입문·하급·중급·상급·특급). 특급 = 지금 평균의 2배.")]
    public float[] multipliers = { .7f, 1f, 1.3f, 1.65f, 2f };
    [Tooltip("단계별 방당 최소 적 수(환산 단위).")]
    public int[] minCounts = { 3, 4, 5, 6, 7 };
    [Tooltip("방당 최대 적 수(환산 단위).")]
    [Min(1)] public int maxCount = 30;

    [Header("난이도 단계")]
    [Tooltip("단계가 되는 첫 클리어 방 수(훈련 한 판 누적, 오름차순). 기본 0·2·4·6·8 → 입문·하급·중급·상급·특급.")]
    public int[] thresholds = { 0, 2, 4, 6, 8 };

    [Header("동시 생존·파도")]
    [Tooltip("단계별 동시 생존 상한(마리). 살아 있는 적이 상한 이상이면 다음 파도를 기다립니다.")]
    public int[] concurrentCaps = { 8, 10, 12, 14, 16 };
    [Tooltip("첫 파도에 내보내는 비율(방 적 수 대비).")]
    [Range(0f, 1f)] public float firstWaveRatio = .4f;
    [Tooltip("이후 파도 간격(초).")]
    [Min(0f)] public float waveInterval = 3f;
    [Tooltip("첫 파도 뒤 나머지를 나눠 내보낼 파도 수(각 파도는 동시 생존 상한을 넘지 않게 더 나뉠 수 있음).")]
    [Min(1)] public int laterWaves = 2;

    [Header("종류")]
    [Tooltip("종류별 Prefab·단계별 가중치·지상/공중·몸 크기·묶음. Bulwark는 아래 별도 조건으로 넣습니다.")]
    public DungeonSpeciesRule[] species = DefaultSpecies();
    [Tooltip("방 방향 보정: 수평 방은 지상 종류, 수직 방은 공중 종류의 가중치를 이 배율로 올립니다.")]
    [Min(0f)] public float orientationBias = 1.3f;

    [Header("Bulwark")]
    public DungeonBulwarkRule bulwark = new DungeonBulwarkRule();

    [Header("재입장 순찰(R18)")]
    [Tooltip("순찰 구성의 적 수 배율(그 단계 수 × 값).")]
    [Range(0f, 1f)] public float patrolRatio = .5f;
    [Tooltip("순찰 구성의 최소 적 수.")]
    [Min(0)] public int patrolMin = 2;
    [Tooltip("재입장 순찰 생성 지연(초, encounterStartDelay에 더함).")]
    [Min(0f)] public float patrolDelay = 1f;

    [Header("생성 거리")]
    [Tooltip("실행 중 Player 몸에서 이 거리(칸) 안에는 생성하지 않습니다.")]
    [Min(0f)] public float playerClearance = 6f;
    [Tooltip("첫 파도의 Player 거리(칸).")]
    [Min(0f)] public float firstWaveClearance = 8f;

    [Header("교전(6차)")]
    [Tooltip("켜면 잠긴 전투 방에서 나온 비행 무리 적(Drone·Swarmer·Bomber)이 등장 연출이 끝나자마자 Player와 교전합니다(감지 거리 제한 없음). 끄면 Prefab 감지 거리(생성 자리 기준 9.5칸)를 씁니다. 어느 쪽이든 무리는 방·종류 단위로 나뉩니다. 규칙 SO가 없는 손 배치 던전(W2 보스 방 등)은 켠 것과 같습니다.")]
    public bool engageOnLock = true;
    [Tooltip("켜면 재입장 순찰의 비행 무리 적은 Prefab 감지 거리를 그대로 쓰되, 한 마리가 Player를 감지하면 같은 방·같은 종류 무리 전체가 함께 교전합니다. 끄면 개체마다 따로 감지합니다.")]
    public bool patrolShareDetection = true;

    /// <summary>단계 번호(0~4)로 자른다.</summary>
    public static int LevelIndex(DungeonThreatLevel level) => Mathf.Clamp((int)level, 0, LevelCount - 1);

    private static float At(float[] values, DungeonThreatLevel level, float fallback)
    {
        if (values == null || values.Length == 0) return fallback;
        return values[Mathf.Min(LevelIndex(level), values.Length - 1)];
    }

    private static int At(int[] values, DungeonThreatLevel level, int fallback)
    {
        if (values == null || values.Length == 0) return fallback;
        return values[Mathf.Min(LevelIndex(level), values.Length - 1)];
    }

    /// <summary>단계 배율(배열이 짧으면 마지막 값).</summary>
    public float Multiplier(DungeonThreatLevel level) => Mathf.Max(0f, At(multipliers, level, 1f));
    /// <summary>단계 최소 적 수.</summary>
    public int MinCount(DungeonThreatLevel level) => Mathf.Max(0, At(minCounts, level, 0));
    /// <summary>단계 동시 생존 상한.</summary>
    public int ConcurrentCap(DungeonThreatLevel level) => Mathf.Max(1, At(concurrentCaps, level, 8));

    /// <summary>
    /// 방 적 수(환산 단위, DESIGN_V4 §2.8): clamp(round(D0 × m[단계] × 빈 칸 / 100), min[단계], max).
    /// 순찰(patrol)이면 그 값 × patrolRatio(반올림), 최소 patrolMin(max는 넘지 않음).
    /// </summary>
    public int TargetUnits(DungeonThreatLevel level, int emptyCells, bool patrol)
    {
        int max = Mathf.Max(1, maxCount);
        int units = Mathf.RoundToInt(baseDensity * Multiplier(level) * Mathf.Max(0, emptyCells) / 100f);
        units = Mathf.Clamp(units, Mathf.Min(MinCount(level), max), max);
        if (!patrol) return units;
        return Mathf.Clamp(Mathf.RoundToInt(units * patrolRatio), Mathf.Min(patrolMin, max), max);
    }

    /// <summary>기본 4종(Roller·Swarmer·Drone·Jumper). Prefab은 생성 도구(DungeonEncounterRulesSetup)가 연결한다.</summary>
    public static DungeonSpeciesRule[] DefaultSpecies() => new[]
    {
        new DungeonSpeciesRule { name = "Roller", air = false, bodySize = new Vector2(2f, 2f), spawnOffset = new Vector2(0f, .7f), weights = new[] { 35f, 30f, 25f, 20f, 20f } },
        new DungeonSpeciesRule { name = "Swarmer", air = true, bodySize = new Vector2(3f, 3f), groupSize = 3, groupRadius = 1.5f, maxGroupsPerRoom = 4, weights = new[] { 35f, 30f, 30f, 30f, 25f } },
        new DungeonSpeciesRule { name = "Drone", air = true, bodySize = new Vector2(3f, 3f), weights = new[] { 15f, 20f, 20f, 25f, 25f } },
        new DungeonSpeciesRule { name = "Jumper", air = false, bodySize = new Vector2(2f, 2f), spawnOffset = new Vector2(0f, .85f), weights = new[] { 15f, 20f, 20f, 25f, 25f } },
    };
}

/// <summary>동적 구성의 적 종류 하나(Bulwark 제외).</summary>
[Serializable]
public sealed class DungeonSpeciesRule
{
    [Tooltip("표시·기록용 이름(예: Roller).")]
    public string name = "";
    public GameObject prefab;
    [Tooltip("단계별 가중치(입문·하급·중급·상급·특급).")]
    public float[] weights = { 25f, 25f, 25f, 25f, 25f };
    [Tooltip("공중 적이면 켭니다(공중 후보 지점 사용, 수직 방 보정 대상).")]
    public bool air;
    [Tooltip("생성 위치를 고를 때 비어 있어야 하는 몸 크기(칸). 묶음이면 묶음 전체 크기.")]
    public Vector2 bodySize = new Vector2(2f, 2f);
    [Tooltip("한 번에 함께 나오는 수(Swarmer = 3). 위치만 묶고 행동은 기존 AI 그대로입니다.")]
    [Min(1)] public int groupSize = 1;
    [Tooltip("묶음 중심에서 각 개체까지의 최대 거리(칸).")]
    [Min(0f)] public float groupRadius;
    [Tooltip("개체 하나의 환산 단위(방 적 수에 세는 값).")]
    [Min(1)] public int units = 1;
    [Tooltip("방당 최대 묶음 수(0 = 제한 없음). Swarmer = 4묶음(12마리).")]
    [Min(0)] public int maxGroupsPerRoom;
    [Tooltip("생성 위치 보정(칸). 지상 적 = 발 위치(몸 아래 가운데) → Prefab 원점(격자 Tool·템플릿 빌더의 적 놓기 WavePrefabVisuals.Placement와 같은 값), 공중 적 = 몸 상자 가운데 → Prefab 원점(충돌 모양 가운데를 상자 가운데에 맞춤). 생성 도구가 Prefab 충돌 모양으로 채운다.")]
    public Vector2 spawnOffset;

    /// <summary>단계 가중치(배열이 짧으면 마지막 값, 없으면 0).</summary>
    public float Weight(DungeonThreatLevel level)
    {
        if (weights == null || weights.Length == 0) return 0f;
        return Mathf.Max(0f, weights[Mathf.Min(DungeonEncounterRulesSO.LevelIndex(level), weights.Length - 1)]);
    }

    /// <summary>묶음 하나의 환산 단위(개체 단위 × 묶음 수).</summary>
    public int GroupUnits => Mathf.Max(1, units) * Mathf.Max(1, groupSize);
}

/// <summary>Bulwark(큰 지상 적) 조건. W1의 Bulwark는 보스가 아니다(isBossEnemy false).</summary>
[Serializable]
public sealed class DungeonBulwarkRule
{
    public GameObject prefab;
    [Tooltip("이 단계부터 넣습니다(0 입문 … 2 중급).")]
    public DungeonThreatLevel minLevel = DungeonThreatLevel.Mid;
    [Tooltip("실내 높이(칸)가 이 값 이상인 방만.")]
    [Min(0)] public int minInteriorHeight = 17;
    [Tooltip("연속 바닥(칸)이 이 값 이상인 자리만.")]
    [Min(1)] public int minFloorRun = 8;
    [Tooltip("그 바닥 위로 비어 있어야 하는 높이(칸).")]
    [Min(0f)] public float headroom = 5f;
    [Tooltip("생성 몸 크기(칸).")]
    public Vector2 bodySize = new Vector2(5f, 5f);
    [Tooltip("환산 단위.")]
    [Min(1)] public int units = 3;
    [Tooltip("발 위치 → Prefab 원점(생성 위치) 거리(칸). 생성 도구가 격자 Tool 놓기 기준과 같게 채운다.")]
    public Vector2 spawnOffset = new Vector2(0f, 2.0625f);
    [Tooltip("방당 최대 수.")]
    [Min(0)] public int maxPerRoom = 1;
    [Tooltip("특급이고 빈 칸이 이 값 이상이면 방당 2마리.")]
    [Min(0)] public int extremeLargeArea = 1000;
    [Min(0)] public int extremeMaxPerRoom = 2;
    [Tooltip("둘째 Bulwark는 첫째가 쓰러지거나 이 시간(초) 뒤에 나옵니다.")]
    [Min(0f)] public float secondDelay = 6f;

    /// <summary>이 단계·방에서 넣을 수 있는 최대 수(조건 미달이면 0).</summary>
    public int MaxFor(DungeonThreatLevel level, int interiorHeight, int emptyCells)
    {
        if (prefab == null || level < minLevel || interiorHeight < minInteriorHeight) return 0;
        return level >= DungeonThreatLevel.Extreme && emptyCells >= extremeLargeArea ? Mathf.Max(maxPerRoom, extremeMaxPerRoom) : maxPerRoom;
    }
}
