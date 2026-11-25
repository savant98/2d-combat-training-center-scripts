using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>방의 적 구성을 정하는 방식(4차 R16~R18).</summary>
public enum DungeonEncounterMode
{
    /// <summary>템플릿의 손 배치 적 구성(Wave 2~)을 생성 때 고른다(기존 Wave·W2 보스). 난이도는 minTier~maxTier·tierByDistance.</summary>
    Authored = 0,
    /// <summary>
    /// 방에 들어오는 순간 DungeonEncounterComposer가 적 구성을 짠다(W1). 난이도는 첫 클리어 방 수 문턱(DungeonThreatTracker),
    /// 적 수는 빈 칸 면적 × 단계 배율, 클리어한 방 재입장은 순찰 구성. 생성기(DungeonGenerator)의 배치·연결 결과는 Authored와 같다.
    /// </summary>
    Dynamic = 1,
}

/// <summary>던전을 클리어했다고 보는 조건.</summary>
public enum DungeonClearCondition
{
    /// <summary>적이 나오는 방을 모두 클리어한다(보스 방이 있으면 그 방도 포함).</summary>
    AllRooms = 0,
    /// <summary>보스 방을 클리어한다. 보스 방 봉인(sealBossRoom)을 켜면 다른 방을 모두 클리어해야 보스 방이 열린다.</summary>
    BossRoom = 1,
    /// <summary>
    /// 던전 목표(<see cref="DungeonWaveSettings.objective"/> — 레버 등)를 모두 완료한다. 방 클리어 수와 관계없다.
    /// 목표가 없거나 배치되지 않으면 실행 쪽(DungeonManager)이 AllRooms 규칙으로 대신한다(3차 계약).
    /// </summary>
    Objective = 2,
}

/// <summary>던전 생성에 쓰는 방 템플릿 후보 하나.</summary>
[Serializable]
public sealed class DungeonTemplateEntry
{
    public DungeonRoomTemplateSO template;
    [Min(0f)] public float weight = 1f;
}

/// <summary>
/// 던전 Wave(<see cref="WaveFlowMode.Dungeon"/>)의 생성 설정. Wave 격자 Tool의 Wave 상세에서 편집한다.
/// 입장할 때마다 <see cref="DungeonRunBuilder"/>가 이 설정으로 던전을 새로 만들어 실행 사본에 펼친다(원본 에셋은 바꾸지 않는다).
/// </summary>
[Serializable]
public sealed class DungeonWaveSettings
{
    [Tooltip("시작 방·보스 방을 뺀 전투 방 수의 범위입니다. 0은 보스 방이 있을 때만 허용합니다(보스가 없으면 생성기가 1로 봅니다).")]
    [Min(0)] public int minRooms = 3;
    [Min(1)] public int maxRooms = 4;
    [Tooltip("일반 방(시작 방·전투 방)으로 쓸 템플릿과 가중치입니다.")]
    public DungeonTemplateEntry[] templates = Array.Empty<DungeonTemplateEntry>();
    [Tooltip("방마다 고를 적 구성의 난이도 범위입니다. 템플릿에 그 난이도가 없으면 가장 가까운 난이도를 고릅니다.")]
    [Range(1, 3)] public int minTier = 1;
    [Range(1, 3)] public int maxTier = 1;
    [Tooltip("시작 방에서 가장 먼 끝에 보스 방을 하나 둡니다.")]
    public bool includeBossRoom;
    public DungeonRoomTemplateSO bossRoom;
    [Tooltip("보스 방 템플릿에서 쓸 적 구성 번호입니다(1부터).")]
    [Min(1)] public int bossEncounter = 1;
    public DungeonClearCondition clearCondition = DungeonClearCondition.AllRooms;
    /// <summary>클리어 조건이 <see cref="DungeonClearCondition.Objective"/>일 때 배치·추적할 목표 모듈(레버 등). 다른 조건이면 쓰지 않는다.</summary>
    [Tooltip("클리어 조건이 '목표 완료'일 때 쓸 목표 모듈(예: 레버 3개)입니다.")]
    public DungeonObjectiveSO objective;
    [Tooltip("켜면 다른 방을 모두 클리어할 때까지 보스 방 출입구가 봉인됩니다.")]
    public bool sealBossRoom = true;
    [Tooltip("이미 놓인 방끼리 마주 보는 빈 출입구를 이어 고리를 만들 확률입니다.")]
    [Range(0f, 1f)] public float loopChance = .25f;
    [Tooltip("방 사이를 옮기는 연출입니다. 비우면 던전 공통 규칙(DungeonRules)의 기본 연출을 씁니다.")]
    public DungeonPassageEffectSO passage;
    [Tooltip("훈련 테마별 Prop 교체표입니다. 비우면 템플릿에 놓인 Prop을 그대로 씁니다.")]
    public DungeonThemeSO theme;
    [Tooltip("0이면 입장할 때마다 새 구조를 만듭니다. 0이 아니면 이 값으로 고정합니다(검증·시연용).")]
    public int seed;

    [Header("생성 규칙 v2")]
    [Tooltip("전투 방 중 작은 방의 목표 비율(0~1). 큰 방과 작은 방을 섞는다.")]
    [Range(0f, 1f)] public float smallRoomRatio = .5f;
    [Tooltip("이웃 방과 같은 방향(수평/수직)인 템플릿의 가중치 배율. 낮을수록 수평·수직이 번갈아 나온다.")]
    [Range(0f, 1f)] public float sameOrientationWeight = .5f;
    [Tooltip("같은 템플릿을 한 던전에 둘 수 있는 기본 최대 수(템플릿의 maxPerFloor가 0일 때).")]
    [Min(1)] public int sameTemplateLimit = 2;
    [Tooltip("휴식 방(Rest 꼬리표)의 던전당 최대 수와 시작 방에서의 최소 거리(BFS).")]
    [Min(0)] public int maxRestRooms = 1;
    [Min(0)] public int restMinDistance = 2;
    [Tooltip("보스 방 바로 앞 방은 작은 방을 우선한다.")]
    public bool smallRoomBeforeBoss = true;

    /// <summary>비어 있지 않으면 이 목록만 시작 방 후보로 쓴다(Rest·NoStart 제외 규칙은 건너뛰고 Parallax 조건은 유지 — 생성 규칙 v3-2).</summary>
    [Header("생성 규칙 v3")]
    [Tooltip("비어 있지 않으면 이 템플릿만 시작 방 후보로 씁니다(예: 휴식 방 고정 시작).")]
    public List<DungeonRoomTemplateSO> startTemplates = new();
    /// <summary>켜면 방의 적 구성 난이도를 시작 방에서의 BFS 거리에 따라 minTier → maxTier로 올린다(생성 규칙 v3-3).</summary>
    [Tooltip("켜면 시작 방에서 멀수록 난이도(minTier → maxTier)가 오릅니다.")]
    public bool tierByDistance;

    /// <summary>적 구성 방식(4차). 기본 Authored = 기존 Wave 그대로. Dynamic이면 tierByDistance·minTier~maxTier 대신 encounterRules를 쓴다.</summary>
    [Header("적 생성(4차)")]
    [Tooltip("Authored = 템플릿의 손 배치 적 구성, Dynamic = 방에 들어올 때 생성 규칙으로 적 구성을 짭니다(클리어 수 기준 난이도 5단계·재입장 순찰).")]
    public DungeonEncounterMode encounterMode = DungeonEncounterMode.Authored;
    /// <summary>Dynamic 모드의 적 생성 규칙(난이도 문턱·적 수·종류 가중치·순찰). Authored면 쓰지 않는다.</summary>
    [Tooltip("Dynamic 모드에서 쓰는 적 생성 규칙입니다(예: CombatEncounterRules).")]
    public DungeonEncounterRulesSO encounterRules;

    /// <summary>동적 적 구성을 실제로 쓰는지(Dynamic이고 규칙이 연결됨). 규칙이 비어 있으면 실행 쪽은 Authored로 대신한다.</summary>
    public bool UsesDynamicEncounters => encounterMode == DungeonEncounterMode.Dynamic && encounterRules != null;

    [Header("텔레포트")]
    [Tooltip("텔레포트 시설을 짝지을지.")]
    public bool teleports = true;
    [Tooltip("던전의 방 수(시작·보스 포함)가 이 값 이상일 때만 텔레포트 짝을 만든다.")]
    [Min(2)] public int teleportMinRooms = 6;
    [Tooltip("짝 수 상한. 방 수가 teleportSecondPairRooms 이상이면 하나 더.")]
    [Min(0)] public int maxTeleportPairs = 1;
    [Min(2)] public int teleportSecondPairRooms = 11;
    [Tooltip("짝지은 두 방의 최소 거리: 연결 그래프(BFS)와 던전 격자(단위 칸 맨해튼).")]
    [Min(1)] public int teleportMinGraphDistance = 3;
    [Min(1)] public int teleportMinGridDistance = 2;

    public int ClampedMinRooms => Mathf.Max(1, Mathf.Min(minRooms, maxRooms));
    public int ClampedMaxRooms => Mathf.Max(1, Mathf.Max(minRooms, maxRooms));
    public int ClampedMinTier => Mathf.Clamp(Mathf.Min(minTier, maxTier), 1, 3);
    public int ClampedMaxTier => Mathf.Clamp(Mathf.Max(minTier, maxTier), 1, 3);
}
