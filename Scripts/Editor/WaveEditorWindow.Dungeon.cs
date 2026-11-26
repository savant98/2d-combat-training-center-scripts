using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Wave 격자 편집기의 던전 기능(DESIGN.md §2.7).
// - Wave 상세: 'Wave 진행 연출' 드롭다운과 던전 설정(템플릿·방 수·난이도·보스 방·생성 미리보기)
// - 방 템플릿(DungeonRoomTemplateSO): 모양·역할·적 구성 정보, Wave 목록 이름, 적 구성 추가·삭제, 템플릿 검사, 적 구성 편집 중 맵 층 흐린 표시
// - 2차(DESIGN_V2): 템플릿 메타(컨셉·크기 등급·방향·배경·벽면 키트·꼬리표·던전당 최대 수), 생성 규칙 v2 설정(작은 방 비율·방향 배율·반복 제한·
//   휴식·보스 앞 작은 방·텔레포트), 미리보기의 작은 방 크기·텔레포트 연결선·방향 통계·휴식 표식, 시설·텔레포트·크레인·승강기 배치 검사
// - 3차(DESIGN_V3): 클리어 조건 '목표(기믹) 완료' + 목표 모듈(DungeonObjectiveSO) 선택·인라인 편집·새 레버 목표 만들기, 생성 규칙 v3 설정
//   (고정 시작 방 목록·거리 난이도·보스가 있을 때 전투 방 0), 미리보기의 목표 방 ◆·텔레포트 거리·생성 알림, 템플릿 검사(목표 자리 표식·
//   승강기 정지점 = 인접 출입구 바닥·크레인 레일이 출입구 설 자리를 덮음·새 시설 상호작용 간격·바닥 시설 받침, 적 구성 없는 휴식 시작 방 경고 완화)
// - 4차(DESIGN_V4): 적 생성 방식(손 배치·동적) + 적 생성 규칙 SO(DungeonEncounterRulesSO) 선택·인라인 편집·'새 적 생성 규칙', 목표 SO의 마무리 방식(시작 방 출구)·출구 문 이름표,
//   미리보기의 출구 방 ▣·동적 구성 단계별 예상 적 수, 경로 크레인 경로 편집(상세 패널 숫자·점 추가·삭제·격자 클릭 추가, Undo), 템플릿 검사 v4(삭제 시설·목표 4×6·출구 5×6 뒤 벽 자리·
//   경로 크레인 쓸기 상자·출입구 앞 통과·고정 크레인 사슬·생성 구역·수직 여유·크레인 없음·레이저 예고선). 3차의 승강기 검사는 지웠다(승강기 삭제 — R5).
// 본체(WaveEditorWindow.cs)는 이 파일의 메서드를 부르는 한 줄씩만 가진다. 회귀 도구가 리플렉션으로 쓰는 본체 멤버 이름은 바꾸지 않으며,
// 이 파일의 멤버는 모두 'Dungeon' 접두사를 붙여 본체 이름과 겹치거나 같은 인자 수의 오버로드가 생기지 않게 한다.
// - 6차(DESIGN_V6 T16·T17): 정적 클래스(DungeonTemplateIssue·DungeonTemplateMeta·DungeonPreviewStats·DungeonTemplateValidation·DungeonTemplateAuthoring)를
//   Assets/Scripts/Editor/Dungeon/으로 나눴다. 템플릿 패널은 조각(머리말·층 정보·적 구성 버튼·검사 결과 — 결과 '이동')으로 나눠 던전 템플릿 편집기가 다시 쓰고,
//   경로 크레인 경로 패널은 '배치 오브젝트 상세'(일반 WaveData 포함)로 옮겼다. 검사 위반 칸은 격자에 빨강 점선으로 그린다.
public partial class WaveEditorWindow
{
    private static readonly GUIContent[] dungeonFlowLabels =
    {
        new GUIContent("기본 진행 (처치 → 다음 Wave · 지형 패널 + 크레인)", "필수 목표를 처치하면 다음 Wave로 넘어갑니다. Wave 사이에 지형 패널이 바뀌고 크레인이 Player를 시작 좌표로 옮깁니다."),
        new GUIContent("던전 (방 탐색 · 미니맵 · 관 이동)", "Wave 하나가 던전 하나입니다. 방 템플릿으로 입장할 때마다 새 구조를 만들고, 방을 돌며 적을 처치합니다. 방 사이는 관으로 이동하고, 던전을 클리어하면 크레인이 다음 던전으로 옮깁니다."),
    };
    // 순서 = DungeonClearCondition 값(AllRooms 0 · BossRoom 1 · Objective 2).
    private static readonly string[] dungeonClearLabels = { "모든 전투 방 클리어", "보스 방 클리어", "목표(기믹) 완료" };
    // 목표 모듈 인라인 편집에서 알려진 필드의 한글 이름(없는 필드는 Unity 기본 이름).
    private static readonly Dictionary<string, string> dungeonObjectiveLabels = new()
    {
        { "title", "목표 제목" }, { "shortLabel", "짧은 표기(HUD)" }, { "color", "목표 색" },
        { "leverPrefab", "레버 Prefab" }, { "count", "레버 수" }, { "minStartDistance", "시작 방 최소 거리" },
        { "startDistanceRatio", "시작 거리 비율" }, { "minSpacing", "레버 방 최소 간격" },
        { "allowRestRooms", "휴식 방 허용" }, { "allowAppendedNodes", "덧붙인 방 허용" },
        // 4차: 시작 방 출구(DungeonObjectiveSO.finish·exitPrefab·exitTitle·exitShortLabel).
        { "finish", "마무리 방식" }, { "exitPrefab", "출구 문 Prefab" }, { "exitTitle", "출구 안내 제목" }, { "exitShortLabel", "출구 짧은 표기(HUD)" },
        // 7차 V5: 레버 대기(DungeonObjectiveSO.gatesRoomClear — 목표가 남은 방은 적을 다 잡아도 문이 잠긴 채 목표를 기다린다).
        { "gatesRoomClear", "방 클리어에 필요(문 잠금 유지)" },
    };
    // 순서 = DungeonObjectiveFinish 값(Immediate 0 · ExitInStartRoom 1).
    private static readonly GUIContent[] dungeonFinishLabels =
    {
        new GUIContent("즉시 클리어 (마지막 목표를 채운 순간)", "마지막 목표(레버)를 채우면 바로 던전을 클리어합니다(3차 방식)."),
        new GUIContent("시작 방 출구 문으로 나가야 클리어", "마지막 목표를 채우면 시작 방의 출구 문이 열리고, 그 문에 들어가야 던전을 클리어합니다. 시작 방 템플릿에 출구 자리 표식(5×6)이 있어야 합니다."),
    };
    // 순서 = DungeonEncounterMode 값(Authored 0 · Dynamic 1).
    private static readonly GUIContent[] dungeonEncounterModeLabels =
    {
        new GUIContent("손 배치 구성 (템플릿 적 구성 · 난이도 1~3)", "템플릿마다 손으로 놓은 적 구성 Wave를 난이도 범위(거리 난이도)로 고릅니다. 기존 Wave와 W2 보스 방이 이 방식입니다."),
        new GUIContent("동적 구성 (빈 칸 면적 × 클리어 수 난이도 5단계)", "방에 들어오는 순간 빈 칸 면적과 난이도 단계(첫 클리어 방 수 문턱 → 입문·하급·중급·상급·특급)로 적 구성을 짭니다. 템플릿의 생성 구역 표식 자리를 선호하고, 클리어한 방에 다시 들어오면 순찰 적이 나옵니다(잠그지 않음)."),
    };
    // 적 생성 규칙 인라인 편집에서 알려진 필드의 한글 이름(없는 필드는 Unity 기본 이름).
    private static readonly Dictionary<string, string> dungeonEncounterRuleLabels = new()
    {
        { "baseDensity", "기준 밀집도 D0 (빈 칸 100칸당)" }, { "multipliers", "단계 배율 (입문→특급)" }, { "minCounts", "단계 최소 수" }, { "maxCount", "방당 최대 수" },
        { "thresholds", "단계 문턱 (첫 클리어 방 수)" }, { "concurrentCaps", "동시 생존 상한" }, { "firstWaveRatio", "첫 파도 비율" }, { "waveInterval", "파도 간격 (초)" },
        { "species", "적 종류" }, { "orientationBias", "방 방향 보정 배율" }, { "bulwark", "Bulwark 조건" },
        { "patrolRatio", "재입장 순찰 배율" }, { "patrolMin", "순찰 최소 수" }, { "patrolDelay", "순찰 생성 지연 (초)" },
        { "playerClearance", "Player 거리 (칸)" }, { "firstWaveClearance", "첫 파도 Player 거리 (칸)" },
        // 6차 T1(nav6): 잠긴 방 즉시 교전·재입장 순찰 무리 감지 공유.
        { "engageOnLock", "잠긴 방 즉시 교전" }, { "patrolShareDetection", "순찰 무리 감지 공유" },
    };
    private static readonly string[] dungeonRoleLabels = { "일반 방", "보스 방" };

    [SerializeField] private bool dungeonFoldout = true;
    [SerializeField] private int dungeonPreviewSeed = 1;
    private DungeonLayout dungeonPreview;
    private string dungeonPreviewError;
    private WaveDataSO dungeonPreviewData;
    private int dungeonPreviewWave = -1;
    private int dungeonPreviewDirty = -1;
    private int dungeonPreviewUsedSeed;
    // 성공한 생성의 알림(건너뛴 템플릿·완화한 규칙 등). 생성기는 성공해도 알릴 내용을 error 문장에 담는다.
    private string dungeonPreviewNotes;
    // 목표 모듈 인라인 편집용(대상 SO가 바뀌면 새로 만든다). 적용은 SerializedObject.ApplyModifiedProperties(Undo 기록).
    private SerializedObject dungeonObjectiveSerialized;
    [SerializeField] private bool dungeonObjectiveFoldout = true;
    // 4차: 적 생성 규칙 인라인 편집용(대상 SO가 바뀌면 새로 만든다).
    private SerializedObject dungeonRulesSerialized;
    [SerializeField] private bool dungeonRulesFoldout = true;
    // 4차: 격자 클릭으로 경로 크레인 점을 더하는 중인지(대상 = 지금 선택한 경로 크레인). Esc·다른 항목 선택·모드 변경이면 끝난다.
    [SerializeField] private bool dungeonPathPicking;
    private readonly Dictionary<int, (int dirty, List<DungeonTemplateIssue> issues)> dungeonValidation = new();

    // ───────────────────────────── Wave 목록 이름 ─────────────────────────────
    // 일반 WaveData는 'Wave n'(예전과 같다). 방 템플릿은 '맵 · Prop · 출입구'(Wave 1)와 '적 구성 n: 이름 (난이도 t)'.
    private string[] DungeonWaveNames()
    {
        int count = data != null ? data.waves?.Length ?? 0 : 0;
        if (!(data is DungeonRoomTemplateSO template)) return Enumerable.Range(0, count).Select(i => $"Wave {i + 1}").ToArray();
        return Enumerable.Range(0, count).Select(i => i == 0 ? "맵 · Prop · 출입구" : DungeonEncounterLabel(template, i)).ToArray();
    }
    private static string DungeonEncounterLabel(DungeonRoomTemplateSO template, int number)
    {
        var info = template.encounters != null && number - 1 < template.encounters.Length ? template.encounters[number - 1] : null;
        string name = info != null && !string.IsNullOrWhiteSpace(info.name) ? info.name : $"적 구성 {number}";
        // Popup은 '/'를 하위 메뉴로 읽는다.
        return $"적 구성 {number}: {name.Replace('/', '∕')} (난이도 {(info != null ? info.tier : 1)})";
    }

    // ───────────────────────────── Wave 진행 연출 · 던전 설정 ─────────────────────────────
    // Details의 클리어 조건 바로 앞에서 부른다. 값 대입은 SerializedProperty(Details 끝에서 Apply) 또는 Mutate 경로만 쓴다.
    private void DungeonFlowDetails(SerializedProperty wave)
    {
        if (data is DungeonRoomTemplateSO) return;
        var flow = wave?.FindPropertyRelative("flowMode");
        if (flow == null) return;
        GUILayout.Space(4);
        int current = Mathf.Clamp(flow.enumValueIndex, 0, dungeonFlowLabels.Length - 1);
        int next = EditorGUILayout.Popup(new GUIContent("Wave 진행 연출", "이 Wave를 어떻게 진행할지 고릅니다. 기존 WaveData는 기본 진행입니다."), current, dungeonFlowLabels);
        if (next != current)
        {
            serialized.ApplyModifiedProperties();
            DungeonSetFlowMode(next);
            GUIUtility.ExitGUI();
        }
        EditorGUILayout.HelpBox(dungeonFlowLabels[current].tooltip, MessageType.None);
        if (current != (int)WaveFlowMode.Dungeon) return;
        dungeonFoldout = EditorGUILayout.Foldout(dungeonFoldout, "던전 설정", true);
        if (!dungeonFoldout) return;
        var settings = wave.FindPropertyRelative("dungeon");
        if (settings == null) return;
        EditorGUILayout.HelpBox("던전 Wave는 실행할 때 실행 사본에서 클리어 조건(커스텀 트리거 '" + DungeonRulesSO.ClearTriggerKey + "')·시작 지형·시작 좌표·시작 시 텔레포트·크레인 이동이 던전 시작 방 기준으로 덮어씌워집니다. "
            + "그래서 위 시작 좌표·크레인 설정과 아래 기존 '클리어 조건'은 실행에 쓰이지 않습니다. 방 지형·적은 방 템플릿에서 가져오므로 이 Wave의 격자 배치도 쓰이지 않습니다.", MessageType.Info);
        int unused = Current.spawnObjects?.Length ?? 0;
        if (unused > 0) EditorGUILayout.HelpBox($"이 Wave에 배치한 항목 {unused}개는 던전 실행 때 생성되지 않습니다(방 내용물은 템플릿에서 생성).", MessageType.Warning);
        using (new EditorGUI.IndentLevelScope())
        {
            var minRooms = settings.FindPropertyRelative("minRooms");
            var maxRooms = settings.FindPropertyRelative("maxRooms");
            // 생성 규칙 v3-1: 전투 방 0(시작 방 + 보스 방)은 보스 방이 있을 때만 허용한다. 하한은 입력을 바꿀 때만 적용한다(그리기만으로 값을 고치지 않음).
            bool bossIncluded = settings.FindPropertyRelative("includeBossRoom")?.boolValue == true;
            int roomFloor = DungeonTemplateMeta.MinCombatRooms(bossIncluded);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("전투 방 수", "시작 방·보스 방을 뺀 전투 방 수의 범위입니다(최소 ~ 최대). 보스 방을 넣으면 0(시작 방 + 보스 방만)도 쓸 수 있습니다."));
                int indent = EditorGUI.indentLevel; EditorGUI.indentLevel = 0;
                int typedMin = EditorGUILayout.IntField(minRooms.intValue, GUILayout.MinWidth(30));
                if (typedMin != minRooms.intValue) minRooms.intValue = Mathf.Max(roomFloor, typedMin);
                GUILayout.Label("~", GUILayout.Width(12));
                int typedMax = EditorGUILayout.IntField(maxRooms.intValue, GUILayout.MinWidth(30));
                if (typedMax != maxRooms.intValue) maxRooms.intValue = Mathf.Max(roomFloor, typedMax);
                EditorGUI.indentLevel = indent;
            }
            if (minRooms.intValue > maxRooms.intValue) EditorGUILayout.HelpBox("최소가 최대보다 큽니다. 실행 때는 두 값을 맞바꿔 씁니다.", MessageType.Warning);
            if (Mathf.Min(minRooms.intValue, maxRooms.intValue) < 1)
                EditorGUILayout.HelpBox(bossIncluded
                    ? "전투 방 0: 시작 방에 보스 방을 바로 붙일 수 있습니다(시작 방 템플릿에 보스 방 쪽으로 이을 출입구가 있어야 합니다)."
                    : "전투 방 0은 보스 방이 있을 때만 씁니다. 보스 방이 없으면 생성기가 1개로 봅니다.", bossIncluded ? MessageType.Info : MessageType.Warning);
            DungeonTemplateList(settings.FindPropertyRelative("templates"));
            var minTier = settings.FindPropertyRelative("minTier");
            var maxTier = settings.FindPropertyRelative("maxTier");
            // 4차: 적 생성 방식(손 배치·동적)과 적 생성 규칙. 동적이면 아래 난이도 범위·거리 난이도는 규칙이 없을 때의 대신 값이다.
            bool dynamic = DungeonEncounterGui(settings);
            EditorGUILayout.IntSlider(minTier, 1, 3, new GUIContent("난이도 최소", "방마다 고를 적 구성의 난이도 범위입니다. 템플릿에 그 난이도가 없으면 가장 가까운 난이도를 고릅니다."));
            EditorGUILayout.IntSlider(maxTier, 1, 3, new GUIContent("난이도 최대"));
            if (minTier.intValue > maxTier.intValue) EditorGUILayout.HelpBox("난이도 최소가 최대보다 큽니다. 실행 때는 두 값을 맞바꿔 씁니다.", MessageType.Warning);
            if (dynamic) EditorGUILayout.HelpBox("동적 구성에서는 난이도 최소·최대와 거리 난이도를 쓰지 않습니다(적 생성 규칙의 5단계를 씀). 보스 방은 아래 '보스 방 적 구성'을 그대로 씁니다.", MessageType.None);
            var includeBoss = settings.FindPropertyRelative("includeBossRoom");
            EditorGUILayout.PropertyField(includeBoss, new GUIContent("보스 방 포함", "시작 방에서 가장 먼 끝에 보스 방을 하나 둡니다."));
            if (includeBoss.boolValue)
            {
                var bossProperty = settings.FindPropertyRelative("bossRoom");
                EditorGUILayout.PropertyField(bossProperty, new GUIContent("보스 방 템플릿"));
                var boss = bossProperty.objectReferenceValue as DungeonRoomTemplateSO;
                var bossEncounter = settings.FindPropertyRelative("bossEncounter");
                if (boss == null) EditorGUILayout.HelpBox("보스 방 템플릿을 지정하세요.", MessageType.Warning);
                else if (boss.EncounterCount == 0) EditorGUILayout.HelpBox($"{boss.name}에 적 구성이 없습니다.", MessageType.Error);
                else
                {
                    var names = Enumerable.Range(1, boss.EncounterCount).Select(n => DungeonEncounterLabel(boss, n)).ToArray();
                    int shown = Mathf.Clamp(bossEncounter.intValue - 1, 0, names.Length - 1);
                    int picked = EditorGUILayout.Popup(new GUIContent("보스 방 적 구성", "보스 방 템플릿에서 쓸 적 구성입니다."), shown, names.Select(n => new GUIContent(n)).ToArray());
                    if (picked + 1 != bossEncounter.intValue) bossEncounter.intValue = picked + 1;
                    if (boss.role != DungeonRoomRole.Boss) EditorGUILayout.HelpBox($"{boss.name}의 역할이 보스 방이 아닙니다.", MessageType.Warning);
                }
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("sealBossRoom"), new GUIContent("보스 방 봉인", "켜면 다른 방을 모두 클리어할 때까지 보스 방 출입구가 봉인됩니다."));
            }
            var clear = settings.FindPropertyRelative("clearCondition");
            int clearIndex = EditorGUILayout.Popup(new GUIContent("던전 클리어 조건", "모든 전투 방: 적이 나오는 방을 모두 클리어(보스 방 포함). 보스 방: 보스 방을 클리어. 목표(기믹) 완료: 목표 모듈(예: 레버 3개)을 모두 완료(방 클리어 수와 무관)."),
                Mathf.Clamp(clear.enumValueIndex, 0, dungeonClearLabels.Length - 1), dungeonClearLabels.Select(l => new GUIContent(l)).ToArray());
            if (clearIndex != clear.enumValueIndex) clear.enumValueIndex = clearIndex;
            if (clear.enumValueIndex == (int)DungeonClearCondition.BossRoom && !includeBoss.boolValue)
                EditorGUILayout.HelpBox("보스 방 클리어 조건인데 보스 방이 포함되지 않았습니다.", MessageType.Error);
            DungeonObjectiveGui(settings, clear.enumValueIndex == (int)DungeonClearCondition.Objective);
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("loopChance"), new GUIContent("고리 확률", "이미 놓인 방끼리 마주 보는 빈 출입구를 이어 고리를 만들 확률입니다."));
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("passage"), new GUIContent("방 이동 연출", "비우면 던전 공통 규칙(DungeonRules)의 기본 연출(관 통과)을 씁니다."));
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("theme"), new GUIContent("테마 (Prop 교체표)", "비우면 템플릿에 놓인 Prop을 그대로 씁니다."));
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("seed"), new GUIContent("고정 시드", "0이면 입장할 때마다 새 구조를 만듭니다. 0이 아니면 이 값으로 고정합니다(검증·시연용)."));
            DungeonGenerationRules(settings);
        }
        DungeonTemplateReports(settings);
        DungeonPreviewGui();
    }

    // 생성 규칙 v2(DESIGN_V2 §2): 크기 섞기·방향 교대·반복 제한·휴식 방·보스 앞 작은 방·텔레포트. 모두 SerializedProperty 경로(Details 끝 Apply에서 Undo 기록).
    private static void DungeonGenerationRules(SerializedProperty settings)
    {
        GUILayout.Space(2);
        GUILayout.Label(new GUIContent("생성 규칙", "방을 고를 때의 섞기·교대·반복 제한입니다. 템플릿의 크기 등급·방향·꼬리표·던전당 최대 수를 씁니다."), EditorStyles.miniBoldLabel);
        DungeonField(settings, "smallRoomRatio", "작은 방 비율", "전투 방 중 작은 방(템플릿 크기 등급 '작은 방')의 목표 비율입니다. 부족한 쪽 템플릿의 가중치를 키워 맞춥니다.");
        DungeonField(settings, "sameOrientationWeight", "같은 방향 이웃 배율", "붙일 방과 방향(수평·수직)이 같은 템플릿의 가중치 배율입니다. 낮을수록 수평·수직이 번갈아 나옵니다(원거리·근거리 교대). 혼합 방은 영향이 없습니다.");
        DungeonField(settings, "sameTemplateLimit", "같은 템플릿 최대 수", "한 던전에 같은 템플릿을 둘 수 있는 기본 최대 수입니다(템플릿의 '던전당 최대 수'가 0일 때). 바로 이웃에는 같은 템플릿을 두지 않습니다.");
        DungeonField(settings, "maxRestRooms", "휴식 방 최대 수", "꼬리표 '휴식'(수리대) 템플릿을 한 던전에 둘 수 있는 최대 수입니다.");
        DungeonField(settings, "restMinDistance", "휴식 방 최소 거리", "시작 방에서 연결을 따라 이 수(방 단위) 이상 떨어진 곳에만 휴식 방을 둡니다.");
        DungeonField(settings, "smallRoomBeforeBoss", "보스 앞 작은 방 우선", "시작에서 가장 먼 방이 여럿이면 작은 방 앞에 보스 방을 붙입니다(긴장 완화).");
        GUILayout.Label(new GUIContent("텔레포트", "짝지은 텔레포트 시설로 멀리 떨어진 두 방을 잇습니다. 그 방을 클리어하면 작동하고, 도착 방은 방문하지 않은 방이어도 됩니다."), EditorStyles.miniBoldLabel);
        var teleports = DungeonField(settings, "teleports", "텔레포트 짝 만들기", "꼬리표 '텔레포트 허브'·'텔레포트 끝점'과 텔레포트 시설을 가진 템플릿끼리 짝을 짓습니다. 방이 모자라면 막다른 출입구에 끝점 방을 덧붙입니다.");
        using (new EditorGUI.DisabledScope(teleports != null && !teleports.boolValue))
        {
            var minRooms = DungeonField(settings, "teleportMinRooms", "최소 방 수", "던전의 방 수(시작·보스 포함)가 이 값 이상일 때만 짝을 만듭니다.");
            var pairs = DungeonField(settings, "maxTeleportPairs", "짝 수", "기본 짝 수입니다. 0이면 만들지 않습니다.");
            var second = DungeonField(settings, "teleportSecondPairRooms", "한 쌍 더 만들 방 수", "방 수가 이 값 이상이면 짝을 하나 더 만듭니다.");
            DungeonField(settings, "teleportMinGraphDistance", "최소 연결 거리", "짝지은 두 방 사이의 연결 그래프 거리(방 단위) 하한입니다. 너무 가까운 방끼리는 잇지 않습니다.");
            DungeonField(settings, "teleportMinGridDistance", "최소 격자 거리", "짝지은 두 방의 가장 가까운 단위 칸 사이 맨해튼 거리 하한입니다.");
            if (teleports != null && teleports.boolValue && pairs != null && pairs.intValue == 0) EditorGUILayout.HelpBox("짝 수가 0이라 텔레포트를 만들지 않습니다.", MessageType.Info);
            if (minRooms != null && second != null && second.intValue < minRooms.intValue) EditorGUILayout.HelpBox("'한 쌍 더 만들 방 수'가 '최소 방 수'보다 작습니다. 최소 방 수를 넘으면 바로 짝이 하나 더 생깁니다.", MessageType.Warning);
        }
        // 생성 규칙 v3(DESIGN_V3 §2): 거리 난이도·고정 시작 방. 같은 SerializedProperty 경로(Details 끝 Apply에서 Undo 기록).
        GUILayout.Label(new GUIContent("생성 규칙 v3", "시작 방에서의 거리에 따른 난이도와 고정 시작 방입니다."), EditorStyles.miniBoldLabel);
        var byDistance = DungeonField(settings, "tierByDistance", "거리 난이도", "켜면 시작 방에서 멀수록(연결 거리) 적 구성 난이도가 '난이도 최소'에서 '난이도 최대'로 오릅니다. 그 난이도 구성이 없는 템플릿은 가장 가까운 난이도를 씁니다.");
        var lowTier = settings?.FindPropertyRelative("minTier");
        var highTier = settings?.FindPropertyRelative("maxTier");
        if (byDistance != null && byDistance.boolValue && lowTier != null && highTier != null && lowTier.intValue == highTier.intValue)
            EditorGUILayout.HelpBox("난이도 최소·최대가 같아 거리 난이도가 효과가 없습니다.", MessageType.Info);
        DungeonStartTemplateList(settings?.FindPropertyRelative("startTemplates"));
    }
    // 고정 시작 방 목록(List<DungeonRoomTemplateSO>): ObjectField + 삭제, 아래에 추가 버튼. 비어 있으면 기존 시작 방 규칙.
    private static void DungeonStartTemplateList(SerializedProperty list)
    {
        if (list == null) return;
        GUILayout.Label(new GUIContent("고정 시작 방", "비어 있지 않으면 이 템플릿만 시작 방 후보로 씁니다(예: 휴식 방에서 시작). '휴식'·'시작 불가' 꼬리표 제외 규칙은 건너뛰고, 배경이 시차 배경인 템플릿만 씁니다(시작 방은 관 통과 덮개 없이 세움)."), EditorStyles.miniBoldLabel);
        int remove = -1;
        for (int i = 0; i < list.arraySize; i++)
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(list.GetArrayElementAtIndex(i), GUIContent.none, GUILayout.MinWidth(80));
                if (GUILayout.Button(new GUIContent("삭제", "이 템플릿을 고정 시작 방 목록에서 뺍니다."), EditorStyles.miniButton, GUILayout.Width(38))) remove = i;
            }
        if (remove >= 0)
        {
            // 참조가 남은 원소는 DeleteArrayElementAtIndex가 비우기만 하는 Unity 버전이 있어 먼저 비운다.
            list.GetArrayElementAtIndex(remove).objectReferenceValue = null;
            list.DeleteArrayElementAtIndex(remove);
        }
        if (GUILayout.Button(new GUIContent("+ 고정 시작 방 추가", "시작 방 후보를 이 목록으로 덮어씁니다."), EditorStyles.miniButton))
        {
            int index = list.arraySize;
            list.arraySize = index + 1;
            list.GetArrayElementAtIndex(index).objectReferenceValue = null;
        }
        if (list.arraySize == 0) EditorGUILayout.LabelField(" ", "비어 있음 → 템플릿 목록의 '시작 방 가능' 규칙으로 고릅니다.", EditorStyles.miniLabel);
    }

    // 목표 모듈(DESIGN_V3 §1): 클리어 조건이 '목표(기믹) 완료'면 SO 선택·새 레버 목표 만들기·인라인 편집·경고를 그린다.
    // 다른 조건인데 SO가 남아 있으면 비울 수 있게 필드와 안내만 보인다. 필드 값은 Details 끝 Apply(Undo 기록), SO 편집은 그 SO의 SerializedObject로 적용한다.
    private void DungeonObjectiveGui(SerializedProperty settings, bool active)
    {
        var property = settings?.FindPropertyRelative("objective");
        if (property == null) return;
        var label = new GUIContent("목표 모듈", "어느 방·자리에 무엇을 세울지 정하는 목표 SO입니다(예: 레버 3개). 템플릿 맵 층에는 목표 자리 표식(팔레트 '던전' 탭)만 둡니다.");
        if (!active)
        {
            if (property.objectReferenceValue == null) return;
            EditorGUILayout.PropertyField(property, label);
            EditorGUILayout.HelpBox("클리어 조건이 '목표(기믹) 완료'가 아니라 목표 모듈은 클리어 판정에 쓰이지 않습니다. 쓰지 않으면 비우세요.", MessageType.Info);
            return;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(property, label);
            if (GUILayout.Button(new GUIContent("새 레버 목표", $"{DungeonTemplateAuthoring.ObjectiveFolder}에 레버 목표 SO를 새로 만들어 연결합니다(레버 Prefab이 있으면 함께 지정)."), EditorStyles.miniButton, GUILayout.Width(76)))
            {
                try
                {
                    var created = DungeonTemplateAuthoring.CreateLeverObjective(DungeonTemplateAuthoring.ObjectiveFolder);
                    if (created != null) property.objectReferenceValue = created;
                }
                catch (Exception exception) { ShowNotification(new GUIContent("레버 목표를 만들지 못했습니다: " + exception.Message)); }
            }
        }
        var objective = property.objectReferenceValue as DungeonObjectiveSO;
        if (objective == null)
        {
            EditorGUILayout.HelpBox("목표 모듈이 비어 있습니다. 실행 때는 '모든 전투 방 클리어' 규칙으로 대신합니다.", MessageType.Error);
            return;
        }
        DungeonObjectiveInspector(objective);
        var issues = DungeonTemplateMeta.ObjectiveIssues(Current?.dungeon, objective);
        foreach (var issue in issues) EditorGUILayout.HelpBox(issue.Message, issue.Error ? MessageType.Error : MessageType.Warning);
        string line = DungeonTemplateMeta.ObjectiveCandidateLine(Current?.dungeon, objective);
        if (!string.IsNullOrEmpty(line)) EditorGUILayout.LabelField(new GUIContent(line, "목표 자리 표식이 있는 전투 방 템플릿 수입니다. 레버는 방당 1개만 놓습니다."), EditorStyles.miniLabel);
    }
    // 목표 SO의 보이는 필드를 그대로 그린다(Enemy Tool의 패턴 SO 편집과 같은 방식 — 공유 에셋이라 같은 SO를 쓰는 모든 Wave에 적용).
    private void DungeonObjectiveInspector(DungeonObjectiveSO objective)
    {
        if (dungeonObjectiveSerialized == null || dungeonObjectiveSerialized.targetObject != objective) dungeonObjectiveSerialized = new SerializedObject(objective);
        var serializedObjective = dungeonObjectiveSerialized;
        serializedObjective.Update();
        bool persistent = EditorUtility.IsPersistent(objective);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            dungeonObjectiveFoldout = EditorGUILayout.Foldout(dungeonObjectiveFoldout, new GUIContent($"목표 설정 · {objective.name} ({objective.GetType().Name})",
                "목표 SO의 값을 여기서 바로 고칩니다. 공유 에셋이므로 같은 SO를 쓰는 모든 Wave에 적용됩니다."), true);
            if (dungeonObjectiveFoldout)
            {
                DungeonDrawObjectiveFields(serializedObjective);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(!persistent))
                        if (GUILayout.Button(new GUIContent("Project에서 선택", "목표 SO 에셋을 Project 창에서 보여 줍니다."), EditorStyles.miniButton, GUILayout.Width(96)))
                            EditorGUIUtility.PingObject(objective);
                    using (new EditorGUI.DisabledScope(!persistent || !EditorUtility.IsDirty(objective)))
                        if (GUILayout.Button(new GUIContent("목표 저장", "이 목표 SO 에셋만 저장합니다(다른 에셋은 저장하지 않음)."), EditorStyles.miniButton, GUILayout.Width(64)))
                        {
                            serializedObjective.ApplyModifiedProperties();
                            AssetDatabase.SaveAssetIfDirty(objective);
                        }
                }
            }
        }
        serializedObjective.ApplyModifiedProperties();
    }
    // 보이는 필드를 순서대로 그린다(m_Script 제외). 알려진 필드는 한글 이름, 설명은 SO의 Tooltip.
    private static void DungeonDrawObjectiveFields(SerializedObject serializedObjective)
    {
        var property = serializedObjective.GetIterator();
        for (bool enter = true; property.NextVisible(enter); enter = false)
        {
            if (property.propertyPath == "m_Script") continue;
            string name = dungeonObjectiveLabels.TryGetValue(property.name, out var korean) ? korean : property.displayName;
            // 4차: 마무리 방식(enum)은 한글 선택지로 그린다. 시작 방 출구가 아니면 출구 문 필드는 흐리게 그린다(값은 남긴다).
            if (property.name == "finish" && property.propertyType == SerializedPropertyType.Enum)
            {
                int current = Mathf.Clamp(property.enumValueIndex, 0, dungeonFinishLabels.Length - 1);
                int picked = EditorGUILayout.Popup(new GUIContent(name, property.tooltip), current, dungeonFinishLabels);
                if (picked != property.enumValueIndex) property.enumValueIndex = picked;
                continue;
            }
            bool exitField = property.name == "exitPrefab" || property.name == "exitTitle" || property.name == "exitShortLabel";
            var finish = exitField ? serializedObjective.FindProperty("finish") : null;
            using (new EditorGUI.DisabledScope(finish != null && finish.enumValueIndex != (int)DungeonObjectiveFinish.ExitInStartRoom))
                EditorGUILayout.PropertyField(property, new GUIContent(name, property.tooltip), true);
        }
    }
    // ───────── 4차: 적 생성 방식 · 적 생성 규칙 SO ─────────
    // 적 생성 방식 Popup과, 동적이면 규칙 SO 선택·'새 적 생성 규칙'·인라인 편집·검사를 그린다. 값 대입은 SerializedProperty(Details 끝 Apply에서 Undo 기록). 동적이면 true.
    private bool DungeonEncounterGui(SerializedProperty settings)
    {
        var mode = settings?.FindPropertyRelative("encounterMode");
        var rules = settings?.FindPropertyRelative("encounterRules");
        if (mode == null || rules == null) return false;
        int current = Mathf.Clamp(mode.enumValueIndex, 0, dungeonEncounterModeLabels.Length - 1);
        int next = EditorGUILayout.Popup(new GUIContent("적 생성 방식", "손 배치 구성: 템플릿의 적 구성 Wave를 고릅니다. 동적 구성: 방에 들어올 때 적 생성 규칙으로 적을 짭니다(W1)."), current, dungeonEncounterModeLabels);
        if (next != mode.enumValueIndex) mode.enumValueIndex = next;
        EditorGUILayout.HelpBox(dungeonEncounterModeLabels[next].tooltip, MessageType.None);
        var label = new GUIContent("적 생성 규칙", "동적 구성의 밀집도·단계 문턱·종류 가중치·Bulwark·순찰 규칙 SO입니다(예: CombatEncounterRules). 공유 에셋이라 같은 SO를 쓰는 모든 Wave에 적용됩니다.");
        if (next != (int)DungeonEncounterMode.Dynamic)
        {
            if (rules.objectReferenceValue == null) return false;
            EditorGUILayout.PropertyField(rules, label);
            EditorGUILayout.HelpBox("손 배치 구성이라 적 생성 규칙은 쓰이지 않습니다. 쓰지 않으면 비우세요.", MessageType.Info);
            return false;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(rules, label);
            if (GUILayout.Button(new GUIContent("새 적 생성 규칙", $"{DungeonTemplateAuthoring.EncounterRulesFolder}에 적 생성 규칙 SO를 새로 만들어 연결합니다(기본값 · 이름이 같은 적 Prefab이 있으면 함께 지정)."), EditorStyles.miniButton, GUILayout.Width(92)))
            {
                try
                {
                    var created = DungeonTemplateAuthoring.CreateEncounterRules(DungeonTemplateAuthoring.EncounterRulesFolder);
                    if (created != null) rules.objectReferenceValue = created;
                }
                catch (Exception exception) { ShowNotification(new GUIContent("적 생성 규칙을 만들지 못했습니다: " + exception.Message)); }
            }
        }
        var asset = rules.objectReferenceValue as DungeonEncounterRulesSO;
        if (asset == null)
        {
            EditorGUILayout.HelpBox("적 생성 규칙이 비어 있습니다. 실행 때는 손 배치 구성(템플릿 적 구성)으로 대신합니다.", MessageType.Error);
            return true;
        }
        DungeonEncounterRulesInspector(asset);
        foreach (var issue in DungeonTemplateMeta.EncounterRuleIssues(asset)) EditorGUILayout.HelpBox(issue.Message, issue.Error ? MessageType.Error : MessageType.Warning);
        EditorGUILayout.LabelField(new GUIContent(DungeonTemplateMeta.ThreatLine(asset), "첫 클리어 방 수(시작·적 없는 방·순찰 처치 제외, 훈련 한 판 누적)가 문턱에 닿으면 ROOM CLEAR 직후 단계가 오르고 다음 방부터 적용됩니다."), EditorStyles.wordWrappedMiniLabel);
        return true;
    }
    // 적 생성 규칙 SO의 보이는 필드를 그대로 그린다(목표 SO 인라인 편집과 같은 방식 — 공유 에셋).
    private void DungeonEncounterRulesInspector(DungeonEncounterRulesSO rules)
    {
        if (dungeonRulesSerialized == null || dungeonRulesSerialized.targetObject != rules) dungeonRulesSerialized = new SerializedObject(rules);
        var serializedRules = dungeonRulesSerialized;
        serializedRules.Update();
        bool persistent = EditorUtility.IsPersistent(rules);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            dungeonRulesFoldout = EditorGUILayout.Foldout(dungeonRulesFoldout, new GUIContent($"적 생성 규칙 설정 · {rules.name}",
                "적 생성 규칙 SO의 값을 여기서 바로 고칩니다. 공유 에셋이므로 같은 SO를 쓰는 모든 Wave에 적용됩니다."), true);
            if (dungeonRulesFoldout)
            {
                var property = serializedRules.GetIterator();
                for (bool enter = true; property.NextVisible(enter); enter = false)
                {
                    if (property.propertyPath == "m_Script") continue;
                    string name = dungeonEncounterRuleLabels.TryGetValue(property.name, out var korean) ? korean : property.displayName;
                    EditorGUILayout.PropertyField(property, new GUIContent(name, property.tooltip), true);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(!persistent))
                        if (GUILayout.Button(new GUIContent("Project에서 선택", "적 생성 규칙 SO 에셋을 Project 창에서 보여 줍니다."), EditorStyles.miniButton, GUILayout.Width(96)))
                            EditorGUIUtility.PingObject(rules);
                    using (new EditorGUI.DisabledScope(!persistent || !EditorUtility.IsDirty(rules)))
                        if (GUILayout.Button(new GUIContent("규칙 저장", "이 적 생성 규칙 SO 에셋만 저장합니다(다른 에셋은 저장하지 않음)."), EditorStyles.miniButton, GUILayout.Width(64)))
                        {
                            serializedRules.ApplyModifiedProperties();
                            AssetDatabase.SaveAssetIfDirty(rules);
                        }
                }
            }
        }
        serializedRules.ApplyModifiedProperties();
    }

    // 이름으로 찾은 하위 항목을 그린다(계약 필드가 없으면 그리지 않고 null).
    private static SerializedProperty DungeonField(SerializedProperty parent, string name, string label, string tooltip)
    {
        var property = parent?.FindPropertyRelative(name);
        if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip));
        return property;
    }

    // 진행 연출을 바꾼다(Undo 기록). 검증 도구도 이 경로로 바꾼다.
    private void DungeonSetFlowMode(int mode)
    {
        if (Current == null) return;
        Mutate(() =>
        {
            Current.flowMode = (WaveFlowMode)Mathf.Clamp(mode, 0, dungeonFlowLabels.Length - 1);
            Current.dungeon ??= new DungeonWaveSettings();
        });
    }

    // 템플릿 후보 목록: ObjectField + 가중치 + 삭제, 아래에 추가 버튼.
    private static void DungeonTemplateList(SerializedProperty templates)
    {
        if (templates == null) return;
        GUILayout.Label(new GUIContent("방 템플릿 (일반 방 후보 · 가중치)", "시작 방·전투 방으로 쓸 템플릿입니다. 시작 방은 '시작 방 가능' 템플릿 중에서 고릅니다."), EditorStyles.miniBoldLabel);
        int remove = -1;
        for (int i = 0; i < templates.arraySize; i++)
        {
            var entry = templates.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("template"), GUIContent.none, GUILayout.MinWidth(80));
                var weight = entry.FindPropertyRelative("weight");
                int indent = EditorGUI.indentLevel; EditorGUI.indentLevel = 0;
                float nextWeight = Mathf.Max(0, EditorGUILayout.FloatField(weight.floatValue, GUILayout.Width(44)));
                if (!Mathf.Approximately(nextWeight, weight.floatValue)) weight.floatValue = nextWeight;
                EditorGUI.indentLevel = indent;
                if (GUILayout.Button(new GUIContent("삭제", "이 후보를 목록에서 뺍니다."), EditorStyles.miniButton, GUILayout.Width(38))) remove = i;
            }
        }
        if (remove >= 0) templates.DeleteArrayElementAtIndex(remove);
        if (GUILayout.Button("+ 템플릿 추가", EditorStyles.miniButton))
        {
            int index = templates.arraySize;
            templates.arraySize = index + 1;
            var entry = templates.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("template").objectReferenceValue = null;
            entry.FindPropertyRelative("weight").floatValue = 1;
        }
        if (templates.arraySize == 0) EditorGUILayout.HelpBox("템플릿이 없으면 던전을 만들 수 없습니다.", MessageType.Error);
    }

    // 지정한 템플릿마다 출입구 읽기(TryReadDoors)와 템플릿 검사 요약.
    private void DungeonTemplateReports(SerializedProperty settings)
    {
        var list = new List<DungeonRoomTemplateSO>();
        var templates = settings.FindPropertyRelative("templates");
        for (int i = 0; i < (templates?.arraySize ?? 0); i++)
            if (templates.GetArrayElementAtIndex(i).FindPropertyRelative("template").objectReferenceValue is DungeonRoomTemplateSO t && !list.Contains(t)) list.Add(t);
        var boss = settings.FindPropertyRelative("includeBossRoom").boolValue ? settings.FindPropertyRelative("bossRoom").objectReferenceValue as DungeonRoomTemplateSO : null;
        if (boss != null && !list.Contains(boss)) list.Add(boss);
        // 3차: 고정 시작 방(startTemplates)도 출입구·검사 대상이다(일반 방 후보에 없어도 된다).
        var fixedStarts = new List<DungeonRoomTemplateSO>();
        var startProperty = settings.FindPropertyRelative("startTemplates");
        for (int i = 0; i < (startProperty?.arraySize ?? 0); i++)
            if (startProperty.GetArrayElementAtIndex(i).objectReferenceValue is DungeonRoomTemplateSO s && !fixedStarts.Contains(s)) fixedStarts.Add(s);
        var normalCandidates = new HashSet<DungeonRoomTemplateSO>(list.Where(t => t != boss));
        foreach (var s in fixedStarts) if (!list.Contains(s)) list.Add(s);
        if (list.Count == 0) return;
        // 4차: 목표 SO가 '시작 방 출구'면 시작 후보는 출구 자리 표식이 있는 템플릿만 쓴다(생성 규칙 v4-2).
        var objectiveForExit = settings.FindPropertyRelative("objective")?.objectReferenceValue as DungeonObjectiveSO;
        bool exitFinish = settings.FindPropertyRelative("clearCondition")?.enumValueIndex == (int)DungeonClearCondition.Objective
            && objectiveForExit != null && objectiveForExit.finish == DungeonObjectiveFinish.ExitInStartRoom;
        GUILayout.Label("템플릿 출입구 · 검사", EditorStyles.miniBoldLabel);
        bool anyStart = false;
        foreach (var template in list)
        {
            string text = DungeonDoorReport(template, out bool ok);
            if (fixedStarts.Contains(template)) text += "\n고정 시작 방" + (normalCandidates.Contains(template) ? " · 일반 방 후보에도 있음" : "");
            if (exitFinish && fixedStarts.Contains(template) && DungeonTemplateMeta.ExitSlots(template).Count == 0)
                text += "\n출구 자리 표식 없음 → 시작 방 출구 방식에서는 시작 후보에서 빠집니다";
            var issues = DungeonValidate(template);
            int errors = issues.Count(i => i.Error), warnings = issues.Count - errors;
            if (errors + warnings > 0) text += $"\n템플릿 검사: 오류 {errors} · 경고 {warnings} (템플릿을 열어 확인)";
            EditorGUILayout.HelpBox(text, !ok || errors > 0 ? MessageType.Error : warnings > 0 ? MessageType.Warning : MessageType.None);
            if (normalCandidates.Contains(template) && template.allowAsStart) anyStart = true;
            if (normalCandidates.Contains(template) && template.role == DungeonRoomRole.Boss) EditorGUILayout.HelpBox($"{template.name}은 보스 방 템플릿인데 일반 방 후보에 있습니다.", MessageType.Warning);
        }
        if (fixedStarts.Count > 0)
        {
            // 생성 규칙 v3-2: 고정 시작 방은 '휴식'·'시작 불가' 제외를 건너뛰지만 시차 배경 조건은 유지한다.
            foreach (var s in fixedStarts)
            {
                if (s.role == DungeonRoomRole.Boss) EditorGUILayout.HelpBox($"고정 시작 방 {s.name}은 보스 방 템플릿입니다.", MessageType.Error);
                else if (s.ResolvedBackdrop != DungeonBackdropMode.Parallax)
                    EditorGUILayout.HelpBox($"고정 시작 방 {s.name}의 배경이 {DungeonTemplateMeta.BackdropName(s.ResolvedBackdrop)}입니다. 시작 방은 시차 배경만 쓰므로 후보에서 빠집니다.", MessageType.Warning);
            }
            if (!fixedStarts.Any(DungeonTemplateMeta.CanFixedStart))
                EditorGUILayout.HelpBox("고정 시작 방 중 쓸 수 있는 템플릿(보스 방 아님 · 시차 배경)이 없습니다.", MessageType.Error);
            else if (exitFinish && !fixedStarts.Any(s => DungeonTemplateMeta.CanFixedStart(s) && DungeonTemplateMeta.ExitSlots(s).Count > 0))
                EditorGUILayout.HelpBox("목표 SO가 '시작 방 출구'인데 고정 시작 방 중 출구 자리 표식(5×6)이 있는 템플릿이 없습니다. 출구 문을 세울 수 없습니다.", MessageType.Error);
        }
        else if (exitFinish && !list.Any(t => normalCandidates.Contains(t) && DungeonTemplateMeta.CanStart(t) && DungeonTemplateMeta.ExitSlots(t).Count > 0))
            EditorGUILayout.HelpBox("목표 SO가 '시작 방 출구'인데 시작 방 후보 중 출구 자리 표식(5×6)이 있는 템플릿이 없습니다. 출구 문을 세울 수 없습니다.", MessageType.Error);
        else if (!anyStart) EditorGUILayout.HelpBox("일반 방 후보 중 '시작 방 가능' 템플릿이 없습니다.", MessageType.Error);
        else if (!list.Any(t => normalCandidates.Contains(t) && DungeonTemplateMeta.CanStart(t)))
            EditorGUILayout.HelpBox("시작 방 조건(시작 방 가능 · 꼬리표 '시작 불가'·'휴식' 없음 · 배경 시차)을 모두 채우는 템플릿이 없습니다. 생성기가 조건을 줄여 고르므로 시작 방이 정적 벽면으로 보일 수 있습니다.", MessageType.Warning);
        var normal = list.Where(t => normalCandidates.Contains(t) && t.role != DungeonRoomRole.Boss).ToList();
        EditorGUILayout.LabelField(new GUIContent(
            $"일반 후보 {normal.Count}종: 큰 방 {normal.Count(t => t.sizeClass == DungeonRoomSize.Large)} · 작은 방 {normal.Count(t => t.sizeClass == DungeonRoomSize.Small)}"
            + $" / 수평 {normal.Count(t => t.orientation == DungeonRoomOrientation.Horizontal)} · 수직 {normal.Count(t => t.orientation == DungeonRoomOrientation.Vertical)} · 혼합 {normal.Count(t => t.orientation == DungeonRoomOrientation.Mixed)}"
            + $" / 시작 가능 {normal.Count(DungeonTemplateMeta.CanStart)} · 휴식 {normal.Count(t => t.HasTag(DungeonRoomTags.Rest))} · 텔레포트 {normal.Count(DungeonTemplateMeta.CanHostTeleport)}"
            + $" · 목표 자리 {normal.Count(t => DungeonTemplateMeta.ObjectiveSlots(t).Count > 0)} · 생성 구역 {normal.Count(t => DungeonTemplateMeta.SpawnZones(t).Count > 0)} · 크레인 {normal.Count(t => DungeonTemplateMeta.CraneCount(t) > 0)}",
            "생성 규칙이 섞는 후보 구성입니다. 크기·방향이 한쪽에 몰리면 비율·교대 규칙이 맞추지 못합니다."), EditorStyles.miniLabel);
        float ratio = settings.FindPropertyRelative("smallRoomRatio")?.floatValue ?? 0f;
        if (ratio > 0f && normal.Count > 0 && !normal.Any(t => t.sizeClass == DungeonRoomSize.Small))
            EditorGUILayout.HelpBox($"작은 방 비율이 {ratio:0.##}인데 후보에 작은 방 템플릿이 없습니다. 모두 큰 방으로 만듭니다.", MessageType.Warning);
        if (ratio < 1f && normal.Count > 0 && !normal.Any(t => t.sizeClass == DungeonRoomSize.Large))
            EditorGUILayout.HelpBox("후보에 큰 방 템플릿이 없습니다. 모두 작은 방으로 만듭니다.", MessageType.Warning);
        if (settings.FindPropertyRelative("teleports")?.boolValue == true && !normal.Any(DungeonTemplateMeta.CanHostTeleport))
            EditorGUILayout.HelpBox("텔레포트 시설을 가진 템플릿(꼬리표 '텔레포트 허브'·'텔레포트 끝점' + 맵 층 텔레포트 시설)이 후보에 없어 텔레포트 짝을 만들지 않습니다.", MessageType.Info);
        if (GUILayout.Button("템플릿 다시 검사", EditorStyles.miniButton)) dungeonValidation.Clear();
    }
    private static string DungeonDoorReport(DungeonRoomTemplateSO template, out bool ok)
    {
        var doors = new List<DungeonDoorSocket>();
        ok = template.TryReadDoors(doors, out string error);
        if (!ok) return $"{template.name}: {error}";
        if (doors.Count == 0) { ok = false; return $"{template.name}: 출입구가 없습니다."; }
        int left = doors.Count(d => d.side == DungeonDoorSide.Left);
        return $"{template.name} · {DungeonShapes.DisplayName(template.shape)} · {(template.role == DungeonRoomRole.Boss ? "보스" : "일반")}\n출입구 {doors.Count}개(왼쪽 {left} · 오른쪽 {doors.Count - left}) · 적 구성 {template.EncounterCount}개" + (template.allowAsStart ? " · 시작 방 가능" : "")
            + "\n" + DungeonTemplateMeta.Summary(template) + (string.IsNullOrWhiteSpace(template.concept) ? "" : "\n컨셉: " + template.concept.Trim());
    }
    private List<DungeonTemplateIssue> DungeonValidate(DungeonRoomTemplateSO template)
    {
        int id = template.GetInstanceID(), dirty = EditorUtility.GetDirtyCount(template);
        if (dungeonValidation.TryGetValue(id, out var hit) && hit.dirty == dirty) return hit.issues;
        var issues = DungeonTemplateValidation.Check(template);
        dungeonValidation[id] = (dirty, issues);
        return issues;
    }

    // ───────────────────────────── 생성 미리보기 ─────────────────────────────
    private void DungeonPreviewGui()
    {
        if (dungeonPreviewData != data || dungeonPreviewWave != waveIndex) { dungeonPreview = null; dungeonPreviewError = null; dungeonPreviewNotes = null; }
        GUILayout.Space(4);
        GUILayout.Label("생성 미리보기", EditorStyles.miniBoldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            dungeonPreviewSeed = EditorGUILayout.IntField(new GUIContent("시드", "미리보기에 쓸 시드입니다. 0이면 누를 때마다 무작위 시드를 씁니다. 실행 때의 시드와는 별개입니다."), dungeonPreviewSeed);
            if (GUILayout.Button("다시 생성", EditorStyles.miniButton, GUILayout.Width(64))) DungeonRegeneratePreview();
        }
        if (dungeonPreviewError != null) { EditorGUILayout.HelpBox("생성 실패: " + dungeonPreviewError, MessageType.Error); return; }
        if (dungeonPreview == null) { EditorGUILayout.HelpBox("'다시 생성'을 누르면 지금 설정으로 던전 배치도를 그립니다(시작 흰색 · 전투 청록 · 보스 빨강).", MessageType.None); return; }
        int combat = dungeonPreview.rooms.Count(r => r.kind == DungeonRoomKind.Combat);
        int boss = dungeonPreview.rooms.Count(r => r.kind == DungeonRoomKind.Boss);
        EditorGUILayout.LabelField($"시드 {dungeonPreviewUsedSeed} · 방 {dungeonPreview.rooms.Count}개(전투 {combat} · 보스 {boss})", EditorStyles.miniLabel);
        var summary = DungeonPreviewStats.Summarize(dungeonPreview);
        EditorGUILayout.LabelField(new GUIContent(summary.SizeLine, "작은 방 수는 전투 방 기준입니다. 던전 설정의 '작은 방 비율'과 비교하세요."), EditorStyles.miniLabel);
        EditorGUILayout.LabelField(new GUIContent(summary.OrientationLine, "템플릿 방향별 방 수(시작·보스 포함)와, 연결된 이웃 중 둘 다 같은 방향(수평-수평 또는 수직-수직)인 연결 수입니다. 혼합 방은 같은 방향으로 세지 않습니다."), EditorStyles.miniLabel);
        // 3차: 텔레포트 짝마다 연결(BFS)·격자 거리, 목표 배치(개수·시작 거리·최소 간격).
        var settings = Current?.dungeon;
        string teleportLine = DungeonPreviewStats.TeleportLine(dungeonPreview, settings);
        if (!string.IsNullOrEmpty(teleportLine))
            EditorGUILayout.LabelField(new GUIContent(teleportLine, "짝지은 두 방의 연결 그래프 거리(방 단위)와 던전 격자 거리(단위 칸 맨해튼)입니다. 던전 설정의 최소 거리보다 작으면 '!'를 붙입니다."), EditorStyles.wordWrappedMiniLabel);
        string objectiveLine = DungeonPreviewStats.ObjectiveLine(dungeonPreview, settings);
        if (!string.IsNullOrEmpty(objectiveLine))
            EditorGUILayout.LabelField(new GUIContent(objectiveLine, "목표(◆)가 놓인 방 수와 클리어에 필요한 수, 각 목표 방의 시작 방 거리, 목표 방끼리의 가장 짧은 연결 거리입니다."), EditorStyles.wordWrappedMiniLabel);
        // 4차: 시작 방 출구(▣)와 동적 구성 단계별 예상 적 수.
        string exitLine = DungeonPreviewStats.ExitLine(dungeonPreview, settings, out bool exitMissing);
        if (!string.IsNullOrEmpty(exitLine))
        {
            if (exitMissing) EditorGUILayout.HelpBox(exitLine, MessageType.Error);
            else EditorGUILayout.LabelField(new GUIContent(exitLine, "마지막 목표를 채우면 시작 방 출구 문이 열리고, 그 문에 들어가야 던전을 클리어합니다."), EditorStyles.wordWrappedMiniLabel);
        }
        string dynamicLine = DungeonPreviewStats.DynamicLine(dungeonPreview, settings);
        if (!string.IsNullOrEmpty(dynamicLine))
            EditorGUILayout.LabelField(new GUIContent(dynamicLine, "동적 구성: 방 적 수(환산, Bulwark = 3) = clamp(round(D0 × 단계 배율 × 빈 칸 / 100), 단계 최소, 최대). 단계는 실행 중 첫 클리어 방 수로 오르므로 방마다 실제 단계는 진행 순서에 따라 다릅니다. 방 툴팁에 방별 값이 있습니다."), EditorStyles.wordWrappedMiniLabel);
        if (DungeonPreviewStamp() != dungeonPreviewDirty) EditorGUILayout.LabelField("설정이 바뀌었습니다. '다시 생성'을 누르세요.", EditorStyles.miniLabel);
        DungeonDrawLayout(dungeonPreview);
        EditorGUILayout.LabelField("시작 흰색 · 전투 청록 · 보스 빨강 · 작은 방은 작게 · 초록 + 휴식 방 · 파란 선 텔레포트 짝 · 노랑 ◆ 목표(번호 = 진행 순번) · 앰버 ▣ 시작 방 출구 문", EditorStyles.wordWrappedMiniLabel);
        if (!string.IsNullOrEmpty(dungeonPreviewNotes)) EditorGUILayout.HelpBox("생성 알림:\n" + dungeonPreviewNotes, MessageType.Info);
    }
    // 미리보기 뒤 설정 변경 감지: WaveData와 목표 SO(인라인 편집)의 변경 횟수.
    private int DungeonPreviewStamp()
    {
        if (data == null) return -1;
        int stamp = EditorUtility.GetDirtyCount(data);
        var objective = Current?.dungeon?.objective;
        if (objective != null) stamp = unchecked(stamp * 31 + EditorUtility.GetDirtyCount(objective) + 1);
        // 4차: 적 생성 규칙(인라인 편집)도 예상 적 수에 쓰인다.
        var rules = Current?.dungeon?.encounterRules;
        return rules != null ? unchecked(stamp * 31 + EditorUtility.GetDirtyCount(rules) + 7) : stamp;
    }
    // 지금 설정으로 생성기를 한 번 돌린다(실패하면 오류 문장을 남긴다). 검증 도구도 이 경로를 쓴다.
    private void DungeonRegeneratePreview()
    {
        serialized?.ApplyModifiedProperties();
        if (dungeonObjectiveSerialized != null && dungeonObjectiveSerialized.targetObject != null) dungeonObjectiveSerialized.ApplyModifiedProperties();
        if (dungeonRulesSerialized != null && dungeonRulesSerialized.targetObject != null) dungeonRulesSerialized.ApplyModifiedProperties();
        dungeonPreviewData = data; dungeonPreviewWave = waveIndex; dungeonPreviewDirty = DungeonPreviewStamp();
        dungeonPreview = null; dungeonPreviewError = null; dungeonPreviewNotes = null;
        dungeonPreviewUsedSeed = dungeonPreviewSeed != 0 ? dungeonPreviewSeed : new System.Random().Next(1, int.MaxValue);
        var settings = Current?.dungeon;
        if (settings == null) { dungeonPreviewError = "던전 설정이 없습니다."; return; }
        try
        {
            // 성공해도 error에 알림(건너뛴 템플릿·완화한 목표 규칙 등)이 담길 수 있어 따로 보인다.
            if (DungeonGenerator.TryGenerate(settings, dungeonPreviewUsedSeed, out var layout, out string error) && layout != null) { dungeonPreview = layout; dungeonPreviewNotes = string.IsNullOrWhiteSpace(error) ? null : error.Trim(); }
            else dungeonPreviewError = string.IsNullOrEmpty(error) ? "이유를 알 수 없습니다." : error;
        }
        catch (Exception exception) { dungeonPreviewError = $"생성기 예외 {exception.GetType().Name}: {exception.Message}"; }
        Repaint();
    }
    // 단위 칸 배치도. 방마다 색(시작 흰 · 전투 청록 · 보스 빨강), 같은 방의 단위 칸은 이어 그리고, 연결된 출입구는 벽을 가로지르는 막대로 그린다.
    private void DungeonDrawLayout(DungeonLayout layout)
    {
        var units = layout.rooms.Where(r => r != null).SelectMany(r => r.Units.Select(u => (room: r, unit: u))).ToList();
        if (units.Count == 0) { EditorGUILayout.HelpBox("방이 없습니다.", MessageType.Warning); return; }
        var min = units.Aggregate(units[0].unit, (m, u) => Vector2Int.Min(m, u.unit));
        var max = units.Aggregate(units[0].unit, (m, u) => Vector2Int.Max(m, u.unit));
        int columns = max.x - min.x + 1, rows = max.y - min.y + 1;
        float available = Mathf.Max(80, detailsWidth - 40);
        float cell = Mathf.Clamp(Mathf.Floor((available - 8) / columns), 8, 28);
        var area = GUILayoutUtility.GetRect(columns * cell + 8, rows * cell + 8, GUILayout.ExpandWidth(false));
        Rect UnitRect(Vector2Int u) => new Rect(area.x + 4 + (u.x - min.x) * cell, area.y + 4 + (max.y - u.y) * cell, cell, cell);
        var owner = new Dictionary<Vector2Int, DungeonRoomNode>();
        foreach (var (room, unit) in units) owner[unit] = room;
        bool repaint = Event.current.type == EventType.Repaint;
        if (repaint)
        {
            EditorGUI.DrawRect(area, new Color(.031f, .067f, .114f, .86f));
            foreach (var (room, unit) in units)
            {
                var color = DungeonKindColor(room.kind);
                var rect = UnitRect(unit);
                // 작은 방(크기 등급)은 테두리를 더 넓게 비워 작게 그린다. 같은 방의 단위 칸 사이 틈은 같은 두께로 메운다.
                float inset = DungeonPreviewInset(room, cell);
                EditorGUI.DrawRect(new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2, rect.height - inset * 2), color);
                // 같은 방의 오른쪽·위 단위 칸과 틈을 메워 한 덩어리로 보이게 한다.
                if (owner.TryGetValue(unit + Vector2Int.right, out var right) && right == room)
                    EditorGUI.DrawRect(new Rect(rect.xMax - inset, rect.y + inset, inset * 2, rect.height - inset * 2), color);
                if (owner.TryGetValue(unit + Vector2Int.up, out var up) && up == room)
                    EditorGUI.DrawRect(new Rect(rect.x + inset, rect.y - inset, rect.width - inset * 2, inset * 2), color);
            }
            // 휴식 방(꼬리표 '휴식'): 첫 단위 칸 오른쪽 위에 초록 +.
            var rest = new Color(.2f, .95f, .35f);
            foreach (var room in layout.rooms.Where(r => r != null && r.template != null && r.template.HasTag(DungeonRoomTags.Rest)))
            {
                var rect = UnitRect(room.Units.First());
                float arm = Mathf.Max(3, cell * .16f), thick = Mathf.Max(1.5f, cell * .06f);
                var center = new Vector2(rect.xMax - arm - 3, rect.y + arm + 3);
                EditorGUI.DrawRect(new Rect(center.x - arm, center.y - thick * .5f, arm * 2, thick), rest);
                EditorGUI.DrawRect(new Rect(center.x - thick * .5f, center.y - arm, thick, arm * 2), rest);
            }
            var line = new Color(.56f, .96f, 1f);
            foreach (var room in layout.rooms.Where(r => r != null))
                foreach (var link in room.links)
                {
                    if (!DungeonSocket(room, link.socket, out var socket)) continue;
                    var rect = UnitRect(room.gridOrigin + socket.unit);
                    float x = socket.side == DungeonDoorSide.Right ? rect.xMax : rect.xMin;
                    EditorGUI.DrawRect(new Rect(x - 4, rect.center.y - 2, 8, 4), line);
                }
            // 텔레포트 짝: 각 방의 텔레포트 시설이 놓인 단위 칸 사이에 파란 선(중립 기능 = 파랑), 양 끝에 작은 사각형.
            var teleport = new Color(.35f, .62f, 1f);
            Handles.BeginGUI();
            try
            {
                foreach (var pair in layout.teleports)
                {
                    var a = layout.Room(pair.roomA);
                    var b = layout.Room(pair.roomB);
                    if (a == null || b == null) continue;
                    Vector2 pa = UnitRect(a.gridOrigin + DungeonTemplateMeta.PadUnit(a.template, pair.padA)).center;
                    Vector2 pb = UnitRect(b.gridOrigin + DungeonTemplateMeta.PadUnit(b.template, pair.padB)).center;
                    Handles.color = teleport;
                    Handles.DrawAAPolyLine(2.5f, pa, pb);
                    foreach (var p in new[] { pa, pb }) EditorGUI.DrawRect(new Rect(p.x - 3, p.y - 3, 6, 6), teleport);
                }
                // 목표(◆, 목표 앰버): 목표를 세울 자리 표식이 놓인 단위 칸의 왼쪽 아래(휴식 +는 오른쪽 위, 방 번호는 가운데).
                Handles.color = WavePrefabVisuals.ObjectiveColor;
                foreach (var objective in layout.objectives)
                {
                    if (!DungeonObjectiveMark(layout, objective, UnitRect, cell, out var center, out float radius)) continue;
                    Handles.DrawAAConvexPolygon(center + new Vector2(0, -radius), center + new Vector2(radius, 0), center + new Vector2(0, radius), center + new Vector2(-radius, 0));
                }
                // 4차: 시작 방 출구 문(▣, 목표 앰버): 목표 SO가 '시작 방 출구'이고 시작 방 템플릿에 출구 자리 표식이 있으면 그 단위 칸 오른쪽 아래에 그린다.
                if (DungeonExitMark(layout, Current?.dungeon, UnitRect, cell, out var exitCenter, out float exitSize))
                {
                    var square = new Rect(exitCenter.x - exitSize, exitCenter.y - exitSize, exitSize * 2, exitSize * 2);
                    EditorGUI.DrawRect(square, WavePrefabVisuals.ObjectiveColor);
                    EditorGUI.DrawRect(new Rect(square.x + square.width * .3f, square.y + square.height * .3f, square.width * .4f, square.height * .4f), new Color(.03f, .07f, .11f));
                }
            }
            finally { Handles.EndGUI(); }
        }
        // 목표 진행 순번(1부터)과 툴팁. 툴팁은 모든 이벤트에서 그린다.
        var objectiveLabel = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft, normal = { textColor = WavePrefabVisuals.ObjectiveColor } };
        foreach (var objective in layout.objectives)
        {
            if (!DungeonObjectiveMark(layout, objective, UnitRect, cell, out var center, out float radius)) continue;
            GUI.Label(new Rect(center.x + radius + 1, center.y - 8, 22, 16), new GUIContent((objective.index + 1).ToString(), $"목표 {objective.index + 1}번 · 방 {objective.room} · 자리 표식 {objective.slot + 1}번"), objectiveLabel);
        }
        // 방 번호·표식과 툴팁(방 번호 · 템플릿 · 적 구성)은 모든 이벤트에서 그려야 툴팁이 뜬다.
        var label = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(.03f, .07f, .11f) } };
        foreach (var (room, unit) in units)
        {
            var rect = UnitRect(unit);
            bool first = unit == room.Units.First();
            string mark = room.kind == DungeonRoomKind.Start ? "S" : room.kind == DungeonRoomKind.Boss ? "B" : "";
            GUI.Label(rect, new GUIContent(first ? $"{mark}{room.id}" : "", DungeonRoomTooltip(room)), label);
        }
    }
    // 목표 표식 자리: 자리 표식이 놓인 단위 칸 사각형의 왼쪽 아래 안쪽. 방을 찾지 못하면 false.
    private static bool DungeonObjectiveMark(DungeonLayout layout, DungeonObjectiveAssignment objective, Func<Vector2Int, Rect> unitRect, float cell, out Vector2 center, out float radius)
    {
        center = default; radius = Mathf.Max(3f, cell * .18f);
        var room = layout?.Room(objective.room);
        if (room == null) return false;
        var rect = unitRect(room.gridOrigin + DungeonTemplateMeta.ObjectiveSlotUnit(room.template, objective.slot));
        center = new Vector2(rect.xMin + radius + 3f, rect.yMax - radius - 3f);
        return true;
    }
    // 출구 표식 자리: 시작 방 템플릿의 첫 출구 자리 표식이 놓인 단위 칸의 오른쪽 아래 안쪽. 출구 방식이 아니거나 표식이 없으면 false.
    private static bool DungeonExitMark(DungeonLayout layout, DungeonWaveSettings settings, Func<Vector2Int, Rect> unitRect, float cell, out Vector2 center, out float size)
    {
        center = default; size = Mathf.Max(3f, cell * .16f);
        if (layout == null || settings?.objective == null || settings.objective.finish != DungeonObjectiveFinish.ExitInStartRoom) return false;
        var room = layout.Room(layout.startRoom);
        if (room?.template == null || DungeonTemplateMeta.ExitSlots(room.template).Count == 0) return false;
        var rect = unitRect(room.gridOrigin + DungeonTemplateMeta.ExitSlotUnit(room.template, 0));
        center = new Vector2(rect.xMax - size - 3f, rect.yMax - size - 3f);
        return true;
    }
    private static Color DungeonKindColor(DungeonRoomKind kind) => kind switch
    {
        DungeonRoomKind.Start => new Color(.92f, .94f, .96f),
        DungeonRoomKind.Boss => new Color(1f, .35f, .24f),
        _ => new Color(.17f, .78f, .89f),
    };
    // 작은 방은 단위 칸 크기의 약 1/5만큼 비워 작게 그린다(판단값). 큰 방은 예전과 같은 2px.
    private static float DungeonPreviewInset(DungeonRoomNode room, float cell)
        => room?.template != null && room.template.sizeClass == DungeonRoomSize.Small ? Mathf.Max(2f, Mathf.Round(cell * .2f)) : 2f;
    private string DungeonRoomTooltip(DungeonRoomNode room)
    {
        string kind = room.kind == DungeonRoomKind.Start ? "시작 방" : room.kind == DungeonRoomKind.Boss ? "보스 방" : "전투 방";
        string template = room.template != null ? room.template.name : "템플릿 없음";
        string encounter = room.encounter <= 0 || room.template == null ? "없음" : DungeonEncounterLabel(room.template, room.encounter);
        // 4차: 동적 구성이면 전투 방의 적 구성 번호는 쓰이지 않고, 빈 칸 면적과 단계별 예상 적 수를 보인다(보스 방은 손 배치 그대로).
        var dungeonSettings = Current?.dungeon;
        if (dungeonSettings != null && dungeonSettings.UsesDynamicEncounters && room.kind == DungeonRoomKind.Combat)
            encounter = "동적 구성(입장 때 짬) · " + DungeonPreviewStats.DynamicRoomText(room.template, dungeonSettings.encounterRules);
        string text = $"방 {room.id} · {kind}\n템플릿: {template}\n적 구성: {encounter}\n격자 {room.gridOrigin} · 연결 {room.links.Count}개";
        if (room.kind == DungeonRoomKind.Start && room.template != null && DungeonTemplateMeta.ExitSlots(room.template).Count > 0) text += "\n출구 자리 표식 있음(시작 방 출구 방식이면 출구 문이 섬)";
        if (room.template != null)
        {
            text += "\n" + DungeonTemplateMeta.Summary(room.template);
            if (!string.IsNullOrWhiteSpace(room.template.concept)) text += "\n컨셉: " + room.template.concept.Trim();
        }
        var layout = dungeonPreview;
        if (layout != null)
        {
            // 3차: 시작 방에서의 연결 거리(거리 난이도·목표 배치 기준), 텔레포트 짝 거리, 목표.
            var distance = DungeonPreviewStats.StartDistances(layout);
            if (room.id >= 0 && room.id < distance.Length && distance[room.id] >= 0) text += $"\n시작 방에서 연결 거리 {distance[room.id]}";
            foreach (var pair in layout.teleports)
            {
                if (pair.roomA != room.id && pair.roomB != room.id) continue;
                bool a = pair.roomA == room.id;
                var (graph, grid) = DungeonPreviewStats.TeleportDistance(layout, pair);
                text += $"\n텔레포트 {(a ? pair.padA : pair.padB) + 1}번 ↔ 방 {(a ? pair.roomB : pair.roomA)} (연결 거리 {DungeonPreviewStats.DistanceText(graph)} · 격자 거리 {grid})";
            }
            foreach (var objective in layout.objectives)
                if (objective.room == room.id) text += $"\n목표 {objective.index + 1}번(자리 표식 {objective.slot + 1}번)";
        }
        return text;
    }
    // 생성 때 읽은 출입구가 없으면(자리표시 생성기 등) 템플릿에서 다시 읽는다.
    private static bool DungeonSocket(DungeonRoomNode room, int index, out DungeonDoorSocket socket)
    {
        socket = default;
        if (index < 0) return false;
        if (index < room.sockets.Count) { socket = room.sockets[index]; return true; }
        if (room.template == null) return false;
        var doors = new List<DungeonDoorSocket>();
        if (!room.template.TryReadDoors(doors, out _) || index >= doors.Count) return false;
        socket = doors[index];
        return true;
    }

    // ───────────────────────────── 방 템플릿 패널 ─────────────────────────────
    // Details 맨 위(Wave 상세 앞)에서 부른다. 템플릿이 아니면 아무것도 그리지 않는다.
    // 6차(T16): 패널을 조각(머리말·층 정보·적 구성 버튼·검사 결과)으로 나눠 던전 템플릿 편집기가 자기 순서로 다시 쓴다. 경로 크레인 경로 패널은 '배치 오브젝트 상세'로 옮겼다.
    private void DungeonTemplatePanel()
    {
        if (!(data is DungeonRoomTemplateSO template)) return;
        DungeonSyncEncounterInfo(template);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DungeonTemplateHeaderGui(template);
            // 6차(D7): 기존 창으로 열어도 그대로 편집할 수 있다. 템플릿 목록·지형 잠금·숨긴 상세는 던전 템플릿 편집기에 있다.
            if (GUILayout.Button(new GUIContent("던전 템플릿 편집기에서 열기", "템플릿 목록·템플릿 전용 상세·지형 잠금·검사 결과 이동을 쓰는 전용 창으로 엽니다.")))
            {
                serialized?.ApplyModifiedProperties();
                DungeonTemplateEditorWindow.Open(template);
                GUIUtility.ExitGUI();
            }
            DungeonLayerInfoGui(template);
            DungeonEncounterButtonsGui();
            EditorGUILayout.HelpBox("툴바의 '+ Wave'·'복제'·'삭제'도 방 템플릿에서는 적 구성 단위로 동작합니다(이름·난이도·가중치를 같은 번호로 함께 추가·복제·삭제). 맵 층(Wave 1)은 복제·삭제할 수 없습니다.", MessageType.None);
            DungeonTemplateIssuesGui(template);
        }
        GUILayout.Space(6);
    }
    // 툴바 버튼은 적 구성 단위로 처리하지만(DungeonToolbar*), Undo·Inspector 직접 편집 뒤에는 정보 길이가 Wave 수와 다를 수 있다. Layout 때 한 번 맞춘다(Undo 기록).
    protected void DungeonSyncEncounterInfo(DungeonRoomTemplateSO template)
    {
        if (Event.current.type == EventType.Layout && (template.encounters?.Length ?? -1) != template.EncounterCount)
            Mutate(() => template.SyncEncounters());
    }
    // 머리말: 모양·역할·시작 가능·메타(컨셉·크기·방향·배경·벽면 키트·꼬리표·던전당 최대 수)·설계 메모(6차 designNote).
    protected void DungeonTemplateHeaderGui(DungeonRoomTemplateSO template)
    {
        GUILayout.Label("방 템플릿", EditorStyles.boldLabel);
        var shape = serialized.FindProperty("shape");
        var shapes = (DungeonRoomShape[])Enum.GetValues(typeof(DungeonRoomShape));
        int shapeIndex = Mathf.Max(0, Array.IndexOf(shapes, (DungeonRoomShape)shape.intValue));
        int nextShape = EditorGUILayout.Popup(new GUIContent("모양", "미니맵·연결 그래프에서 쓰는 단위 칸 모양입니다. 실제 방 크기는 맵 층 지형의 외접 사각형을 단위 칸 수로 나눠 봅니다."), shapeIndex, shapes.Select(s => new GUIContent(DungeonShapes.DisplayName(s))).ToArray());
        if (nextShape != shapeIndex) shape.intValue = (int)shapes[nextShape];
        var role = serialized.FindProperty("role");
        int roleIndex = EditorGUILayout.Popup(new GUIContent("역할", "보스 방은 던전 설정의 '보스 방 템플릿'으로만 씁니다."), Mathf.Clamp(role.enumValueIndex, 0, 1), dungeonRoleLabels.Select(l => new GUIContent(l)).ToArray());
        if (roleIndex != role.enumValueIndex) role.enumValueIndex = roleIndex;
        EditorGUILayout.PropertyField(serialized.FindProperty("allowAsStart"), new GUIContent("시작 방으로 쓸 수 있음", "던전의 시작 방(적 없음)으로 고를 수 있는지입니다. 시작 방은 관 통과 덮개 없이 바로 보이므로 배경이 시차(Parallax)인 방만 씁니다."));
        DungeonTemplateMetaGui(template);
        var note = serialized.FindProperty("designNote");
        if (note != null) EditorGUILayout.PropertyField(note, new GUIContent("설계 메모", "왜 이렇게 배치했는지 적는 메모입니다(6차 — 예전 사양 JSON의 note를 옮겼다). 실행에는 쓰이지 않습니다."));
    }
    // 지금 층의 안내: 맵 층이면 배치 안내·수직 여유 줄, 적 구성이면 이름·난이도·가중치.
    protected void DungeonLayerInfoGui(DungeonRoomTemplateSO template)
    {
        if (waveIndex == 0)
        {
            EditorGUILayout.HelpBox("맵 층: 지형·출입구(팔레트 '던전' 탭)·Prop(팔레트 'Prop' 탭)·고정 함정을 놓습니다. 적은 적 구성 Wave에 놓습니다.\n출입구는 좌우 바깥 벽 3칸 중 안쪽 2칸 자리에 놓습니다(회전 0° = 오른쪽 벽, 180° = 왼쪽 벽). 연결된 출입구만 실행 때 벽을 파고 세웁니다.\n"
                + "목표 자리 표식(점선 앰버 4×6 뒤 벽 패널)·출구 자리 표식(점선 앰버 5×6 문 — 시작 전용 템플릿에 1개)·도착 자리 표식(갈매기 표시 5×6 — 다음 Wave 시작 방에 1개)은 실물이 설 수 있는 자리입니다. 실행 때 표식 자체는 생기지 않습니다.\n"
                + "생성 구역 표식(지상 녹·공중 하늘·혼합 노랑 상자)은 동적 구성이 적을 내보내기를 선호하는 자리입니다. 크레인(경로·고정)은 붙기 반경 점선 고리로 보이고, 경로 크레인은 선택하면 '배치 오브젝트 상세'에서 경로를 편집합니다.", MessageType.None);
            string clearance = DungeonTemplateValidation.ClearanceLine(template);
            if (!string.IsNullOrEmpty(clearance)) EditorGUILayout.LabelField(new GUIContent(clearance, "수직 여유(템플릿 규칙 v4-1): 서는 칸에서 위 플랫폼(지형·OneWay·고체 Prop) 아랫면까지 빈 칸이 5칸 이상인 서는 칸의 비율입니다. 75% 이상이면 통과(80% 미만은 경고)."), EditorStyles.wordWrappedMiniLabel);
            return;
        }
        var encounters = serialized.FindProperty("encounters");
        int index = waveIndex - 1;
        if (encounters != null && index < encounters.arraySize)
        {
            var info = encounters.GetArrayElementAtIndex(index);
            EditorGUILayout.PropertyField(info.FindPropertyRelative("name"), new GUIContent("적 구성 이름"));
            EditorGUILayout.IntSlider(info.FindPropertyRelative("tier"), 1, 3, new GUIContent("난이도", "던전 설정의 난이도 범위 안에서 고릅니다."));
            EditorGUILayout.PropertyField(info.FindPropertyRelative("weight"), new GUIContent("가중치", "같은 난이도 후보 사이에서 뽑힐 비율입니다."));
        }
        EditorGUILayout.HelpBox("적 구성 Wave는 맵 층 지형을 그대로 씁니다(흐리게 보이는 항목이 맵 층 배치). 이 Wave에서는 지형을 칠하지 말고 적만 놓으세요(6차 T8 — 적 구성 층 함정 금지). 생성 시각은 방에 들어와 잠긴 뒤부터 셉니다.", MessageType.None);
    }
    // 적 구성 추가·삭제 버튼.
    protected void DungeonEncounterButtonsGui()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("적 구성 추가", "맵 층 지형을 그대로 쓰는 새 적 구성 Wave를 만듭니다.")))
            {
                DungeonAddEncounter();
                GUIUtility.ExitGUI();
            }
            using (new EditorGUI.DisabledScope(waveIndex < 1))
                if (GUILayout.Button(new GUIContent("이 적 구성 삭제", "현재 적 구성 Wave와 그 정보(이름·난이도·가중치)를 함께 지웁니다.")))
                {
                    DungeonRemoveEncounter(waveIndex);
                    GUIUtility.ExitGUI();
                }
        }
    }
    // 템플릿 검사 결과(6차 단일 검사기 — 테마 폴더 실물은 빌더와 같은 기준). 위치가 있는 결과는 '이동'으로 그 배치·칸을 고른다.
    protected void DungeonTemplateIssuesGui(DungeonRoomTemplateSO template, int limit = 80)
    {
        var issues = DungeonValidate(template);
        var options = DungeonTemplateValidation.OptionsFor(template);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(issues.Count == 0 ? "템플릿 검사 통과" : $"템플릿 검사: 오류 {issues.Count(i => i.Error)} · 경고 {issues.Count(i => !i.Error)}", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("다시 검사", EditorStyles.miniButton, GUILayout.Width(64))) { dungeonValidation.Clear(); issues = DungeonValidate(template); }
        }
        EditorGUILayout.LabelField(options.Strict ? $"기준: 빌더와 같음(테마 '{options.Theme ?? "없음"}' 폴더의 실물 템플릿 — 빌더 오류 규칙·완성도 규칙이 오류)" : "기준: 작업 사본(테마 폴더 밖 — 빌더 오류 규칙은 경고로 보입니다)", EditorStyles.miniLabel);
        foreach (var issue in issues.OrderByDescending(i => i.Error).Take(limit)) DungeonIssueRow(issue);
        if (issues.Count > limit) EditorGUILayout.LabelField($"외 {issues.Count - limit}개", EditorStyles.miniLabel);
        EditorGUILayout.HelpBox(DungeonTemplateValidation.ReachabilityNote, MessageType.Info);
    }
    protected void DungeonIssueRow(DungeonTemplateIssue issue)
    {
        bool locatable = issue.Wave >= 0 || issue.HasCell;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.HelpBox(issue.Message, issue.Error ? MessageType.Error : MessageType.Warning);
            if (locatable && GUILayout.Button(new GUIContent("이동", "이 결과의 배치(층·항목)를 고르고 그 칸으로 격자를 옮깁니다."), EditorStyles.miniButton, GUILayout.Width(36)))
            {
                DungeonFocusIssue(issue);
                GUIUtility.ExitGUI();
            }
        }
    }
    // 6차(T16): 검사 결과 클릭 → 그 층으로 바꾸고 배치를 고른 뒤(선택 / 이동 모드) 그 칸으로 격자를 옮긴다. 회귀 도구도 이 경로를 쓴다.
    [NonSerialized] private DungeonTemplateIssue? dungeonFocusedIssue;
    protected void DungeonFocusIssue(DungeonTemplateIssue issue)
    {
        if (data?.waves == null || data.waves.Length == 0) return;
        serialized?.ApplyModifiedProperties();
        int wave = issue.Wave >= 0 && issue.Wave < data.waves.Length ? issue.Wave : waveIndex;
        if (wave != waveIndex) { waveIndex = wave; time = 0f; }
        selected = -1; selectedBatch = -1; eventIndex = -1; selectedCell = null; areaStart = areaEnd = null;
        ResetGesture();
        Vector2? focus = null;
        var spawns = Current?.spawnObjects;
        if (issue.Spawn >= 0 && spawns != null && issue.Spawn < spawns.Length && spawns[issue.Spawn] != null)
        {
            mode = 0; editPlayerStart = editClearPortal = false;
            selected = issue.Spawn;
            time = WaveAuthoring.Times(Current)[selected];
            focus = spawns[selected].spawnPosition + Offset;
        }
        if (issue.HasCell)
        {
            if (selected < 0) selectedCell = issue.Cell;
            focus ??= (Vector2)issue.Cell + Vector2.one * .5f;
        }
        dungeonFocusedIssue = issue;
        serialized = null;
        if (focus.HasValue) { pan = new Vector2(-focus.Value.x, focus.Value.y) * zoom; }
        Repaint();
    }
    // 6차(T10): 검사 결과의 위반 칸(함정 간격·크레인 경로·수직 여유 좁은 칸 등)을 격자에 빨강 점선으로 그린다. DrawGrid의 GUI.BeginClip(canvas) 안(Repaint).
    private void DungeonDrawIssueCells()
    {
        if (Event.current.type != EventType.Repaint || !(data is DungeonRoomTemplateSO template)) return;
        var issues = DungeonValidate(template);
        var view = new Rect(Vector2.zero, canvas.size);
        Handles.BeginGUI();
        try
        {
            foreach (var issue in issues)
            {
                if (issue.Cells == null || issue.Cells.Length == 0 || (issue.Wave >= 0 && issue.Wave != waveIndex && waveIndex != 0)) continue;
                bool focused = dungeonFocusedIssue.HasValue && dungeonFocusedIssue.Value.Message == issue.Message;
                var color = issue.Error ? new Color(1f, .25f, .2f, focused ? 1f : .85f) : new Color(1f, .7f, .25f, focused ? 1f : .6f);
                foreach (var cell in issue.Cells)
                {
                    var corner = Screen(new Vector2(cell.x, cell.y + 1)) - canvas.position;
                    var rect = new Rect(corner, Vector2.one * zoom);
                    if (!rect.Overlaps(view)) continue;
                    if (focused) EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, .18f));
                    DungeonDashedCell(rect, color);
                }
            }
        }
        finally { Handles.EndGUI(); }
    }
    static void DungeonDashedCell(Rect rect, Color color)
    {
        Handles.color = color;
        float dash = Mathf.Max(2f, rect.width * .22f), gap = dash * .7f;
        var corners = new[] { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax) };
        for (int side = 0; side < 4; side++)
        {
            Vector2 from = corners[side], to = corners[(side + 1) % 4];
            float length = Vector2.Distance(from, to);
            var direction = (to - from) / Mathf.Max(.001f, length);
            for (float t = 0f; t < length; t += dash + gap)
                Handles.DrawAAPolyLine(1.6f, from + direction * t, from + direction * Mathf.Min(length, t + dash));
        }
    }

    // 템플릿 메타(DESIGN_V2): 컨셉 한 줄·크기 등급·방향·배경·벽면 키트·생성 꼬리표·던전당 최대 수. SerializedProperty 경로(Details 끝 Apply에서 Undo 기록).
    private void DungeonTemplateMetaGui(DungeonRoomTemplateSO template)
    {
        var concept = serialized.FindProperty("concept");
        if (concept == null) return;
        EditorGUILayout.PropertyField(concept, new GUIContent("컨셉 한 줄", "이 방이 무엇을 시키는 방인지 한 줄로 적습니다(예: '완전 대칭 아레나', '생산 라인: 적이 컨베이어로 실려 온다'). 미리보기 툴팁·검사 요약에 보입니다."));
        if (string.IsNullOrWhiteSpace(concept.stringValue)) EditorGUILayout.HelpBox("컨셉이 비어 있습니다. 무작위처럼 보이는 배치를 피하려면 방마다 확실한 컨셉을 정하세요.", MessageType.None);
        DungeonEnumPopup(serialized.FindProperty("sizeClass"), "크기 등급", "작은 방은 큰 방의 1/4 크기까지입니다. 실내 23×11 이상이어야 합니다(던전 카메라 범위 = 실내 + 사방 3칸이라 16:9에서 화면보다 큽니다). 생성기가 '작은 방 비율'로 섞습니다.", DungeonTemplateMeta.SizeLabels);
        DungeonEnumPopup(serialized.FindProperty("orientation"), "방향", "주된 이동 방향입니다. 생성기가 이웃 방과 같은 방향을 덜 고르게 해서 수평(원거리)·수직(근거리) 방이 번갈아 나옵니다. 혼합은 영향이 없습니다.", DungeonTemplateMeta.OrientationLabels);
        var backdrop = serialized.FindProperty("backdropMode");
        DungeonEnumPopup(backdrop, "배경", "자동: 작은 방 = 정적 벽면, 큰 방 = 시차 배경. 시작 방은 시차 배경인 방만 씁니다.", DungeonTemplateMeta.BackdropLabels);
        var resolved = DungeonTemplateMeta.ResolvedBackdrop(template, serialized);
        if (backdrop != null && backdrop.enumValueIndex == (int)DungeonBackdropMode.Auto)
            EditorGUILayout.LabelField(" ", "→ " + DungeonTemplateMeta.BackdropName(resolved), EditorStyles.miniLabel);
        using (new EditorGUI.DisabledScope(resolved != DungeonBackdropMode.Wall))
            DungeonEnumPopup(serialized.FindProperty("wallKit"), "벽면 키트", "배경이 정적 벽면일 때 쓰는 벽면 종류입니다(배관 속·환풍 덕트·전선 트렁크·설비 벽면). 실제 Prefab은 DungeonRules의 벽면 키트 표가 정합니다.", DungeonTemplateMeta.KitLabels);
        var tags = serialized.FindProperty("tags");
        if (tags != null)
        {
            EditorGUILayout.PrefixLabel(new GUIContent("생성 꼬리표", "생성 규칙이 쓰는 꼬리표입니다. 휴식 = 수리대 방(던전당 개수·시작 방 거리 제한), 텔레포트 허브·끝점 = 텔레포트 시설 방(맵 층에 텔레포트 시설 필요), 막다른 방 = 출입구 하나로 붙이는 방, 시작 불가 = 떨어짐·컨베이어·함정 방, 크레인 없음 = 크레인 규칙 예외(휴식 방·피스톤 복도만), 함정 위주 = 함정 묶음 2~3개 허용."));
            int value = tags.intValue, next = value;
            using (new EditorGUI.IndentLevelScope())
                for (int row = 0; row < DungeonTemplateMeta.TagOrder.Length; row += 3)
                    using (new EditorGUILayout.HorizontalScope())
                        for (int i = row; i < Mathf.Min(row + 3, DungeonTemplateMeta.TagOrder.Length); i++)
                        {
                            int flag = (int)DungeonTemplateMeta.TagOrder[i];
                            bool on = EditorGUILayout.ToggleLeft(DungeonTemplateMeta.TagLabels[i], (next & flag) != 0, GUILayout.MinWidth(60));
                            next = on ? next | flag : next & ~flag;
                        }
            if (next != value) tags.intValue = next;
        }
        var limit = serialized.FindProperty("maxPerFloor");
        if (limit != null) EditorGUILayout.PropertyField(limit, new GUIContent("던전당 최대 수", "한 던전에 이 템플릿을 둘 수 있는 최대 수입니다. 0이면 던전 설정의 '같은 템플릿 최대 수'를 씁니다."));
    }
    // 연속 값 enum(0부터)을 한글 이름 Popup으로 그린다.
    private static void DungeonEnumPopup(SerializedProperty property, string label, string tooltip, string[] names)
    {
        if (property == null) return;
        int current = Mathf.Clamp(property.enumValueIndex, 0, names.Length - 1);
        int next = EditorGUILayout.Popup(new GUIContent(label, tooltip), current, names.Select(n => new GUIContent(n)).ToArray());
        if (next != property.enumValueIndex) property.enumValueIndex = next;
    }

    // 6차(T16): 던전 템플릿 편집기(파생 창)가 쓰는 적 구성·검사 진입점.
    protected string[] TemplateLayerNames() => DungeonWaveNames();
    protected void TemplateAddEncounter() => DungeonAddEncounter();
    protected bool TemplateDuplicateEncounter() => DungeonToolbarDuplicate();
    protected bool TemplateRemoveEncounter() => DungeonToolbarRemove();
    protected List<DungeonTemplateIssue> TemplateIssues(DungeonRoomTemplateSO template) => DungeonValidate(template);
    protected void TemplateRevalidate() => dungeonValidation.Clear();

    // 적 구성 Wave를 하나 더한다: 시작 좌표 = 맵 층과 같게(직접 지정), 지형 재정의 없음, 필수 처치 클리어, 배치 없음. 정보 길이도 맞춘다.
    private void DungeonAddEncounter()
    {
        if (!(data is DungeonRoomTemplateSO template)) return;
        serialized?.ApplyModifiedProperties();
        Mutate(() =>
        {
            var list = (data.waves ?? Array.Empty<Wave>()).ToList();
            if (list.Count == 0) list.Add(new Wave { overridePlayerStart = true, playerStart = data.playerStart, spawnObjects = Array.Empty<WaveSpawnInfo>() });
            var start = list[0].overridePlayerStart ? list[0].playerStart : data.playerStart;
            list.Add(new Wave
            {
                overridePlayerStart = true, playerStart = start,
                overrideCellMap = false, changeTerrain = false,
                clearType = WaveClearType.DefeatAllTargets,
                spawnObjects = Array.Empty<WaveSpawnInfo>(),
            });
            data.waves = list.ToArray();
            template.SyncEncounters();
            waveIndex = list.Count - 1;
            selected = -1; selectedBatch = -1; eventIndex = -1; selectedCell = null; areaStart = areaEnd = null; time = 0;
            ResetGesture();
        });
    }
    // 적 구성 Wave(번호 1 이상)와 그 정보를 함께 지운다.
    private void DungeonRemoveEncounter(int number)
    {
        if (!(data is DungeonRoomTemplateSO template) || data.waves == null || number < 1 || number >= data.waves.Length) return;
        serialized?.ApplyModifiedProperties();
        Mutate(() =>
        {
            template.SyncEncounters();
            data.waves = data.waves.Where((w, i) => i != number).ToArray();
            template.encounters = template.encounters.Where((e, i) => i != number - 1).ToArray();
            template.SyncEncounters();
            waveIndex = Mathf.Min(number, data.waves.Length - 1);
            selected = -1; selectedBatch = -1; eventIndex = -1; selectedCell = null; areaStart = areaEnd = null;
            ResetGesture();
        });
    }

    // ───────────────────────────── 툴바 '+ Wave'·'복제'·'삭제' (방 템플릿) ─────────────────────────────
    // 방 템플릿의 encounters는 waves[1..]과 같은 번호의 병렬 배열이다. 툴바가 waves만 고치면 뒤쪽 적 구성이 앞 정보의
    // 난이도·가중치를 넘겨받으므로, 템플릿이면 여기서 적 구성 단위로 처리하고 true를 돌려준다. 일반 WaveData면 false(본체가 예전대로 처리).
    private bool DungeonToolbarAdd()
    {
        if (!(data is DungeonRoomTemplateSO)) return false;
        DungeonAddEncounter();
        return true;
    }
    // 지금 적 구성 Wave를 바로 뒤에 복제하고, 같은 번호 정보(이름·난이도·가중치)도 복제해 같은 자리에 끼워 넣는다. 맵 층은 복제하지 않는다.
    private bool DungeonToolbarDuplicate()
    {
        if (!(data is DungeonRoomTemplateSO template)) return false;
        if (waveIndex < 1 || Current == null)
        {
            ShowNotification(new GUIContent("맵 층(Wave 1)은 복제할 수 없습니다. 적 구성 Wave를 골라 복제하세요."));
            return true;
        }
        serialized?.ApplyModifiedProperties();
        Mutate(() =>
        {
            template.SyncEncounters();
            int number = waveIndex;
            var copy = JsonUtility.FromJson<Wave>(JsonUtility.ToJson(Current));
            var waves = data.waves.ToList();
            waves.Insert(number + 1, copy);
            data.waves = waves.ToArray();
            var source = template.encounters[number - 1];
            var infos = template.encounters.ToList();
            infos.Insert(number, new DungeonEncounterInfo
            {
                name = (source != null && !string.IsNullOrWhiteSpace(source.name) ? source.name : $"적 구성 {number}") + " 사본",
                tier = source != null ? source.tier : 1,
                weight = source != null ? source.weight : 1f,
            });
            template.encounters = infos.ToArray();
            template.SyncEncounters();
            waveIndex = number + 1;
            selected = -1; selectedBatch = -1; eventIndex = -1; selectedCell = null; areaStart = areaEnd = null;
            ResetGesture();
        });
        return true;
    }
    // 지금 적 구성 Wave와 그 정보를 함께 지운다. 맵 층(Wave 1)은 지우지 않는다.
    private bool DungeonToolbarRemove()
    {
        if (!(data is DungeonRoomTemplateSO)) return false;
        if (waveIndex < 1)
        {
            ShowNotification(new GUIContent("맵 층(Wave 1)은 지울 수 없습니다. 적 구성 Wave를 골라 지우세요."));
            return true;
        }
        DungeonRemoveEncounter(waveIndex);
        return true;
    }

    // ───────── 4차: 경로 크레인 경로 편집(상세 패널 · 격자 클릭) ─────────
    // 지금 선택한 항목이 경로 크레인이면 경로 점 목록(숫자 편집·끼워 넣기·삭제·끝에 추가·격자 클릭 추가)과 요약을, 고정 크레인이면 반경 안내를 그린다.
    // 값은 배치 설정 키(cranePathCount·cranePath{i}X·Y)로 저장하고 Mutate 경로(Undo 기록)를 쓴다.
    private void DungeonCranePathPanel()
    {
        var spawns = Current?.spawnObjects;
        if (spawns == null || selected < 0 || selected >= spawns.Length || spawns[selected]?.prefab == null) { dungeonPathPicking = false; return; }
        var spawn = spawns[selected];
        bool path = DungeonCranePaths.IsPathCrane(spawn.prefab), anchor = !path && DungeonCranePaths.IsFixedCrane(spawn.prefab);
        if (!path && !anchor) { dungeonPathPicking = false; return; }
        int index = selected;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Label($"{(path ? "경로 크레인" : "고정 크레인")} #{index + 1} {spawn.prefab.name}", EditorStyles.miniBoldLabel);
            float radius = DungeonCranePaths.Radius(spawn.prefab, spawn.deviceSettings);
            EditorGUILayout.LabelField(new GUIContent("붙기 반경", "Player 몸 중심 ↔ 매달림 몸 중심 거리입니다(배치 설정 zipRadius, 4~14). 위 배치 오브젝트 상세의 세부 설정에서 바꿉니다."), new GUIContent($"{radius:0.#}칸"));
            if (anchor)
            {
                EditorGUILayout.HelpBox("고정 크레인: 붙으면 움직이지 않습니다. 고정 크레인 사이 간격은 붙기 반경 × 0.9 이하로 두고, 첫 크레인은 서는 면에서 반경 안에 둡니다(템플릿 규칙 v4-3).", MessageType.None);
                dungeonPathPicking = false;
                return;
            }
            var offsets = DungeonCranePaths.Offsets(spawn.prefab, spawn.deviceSettings);
            var points = offsets.Skip(1).ToList();
            bool changed = false;
            int remove = -1, insert = -1;
            EditorGUILayout.LabelField(new GUIContent("점 0 (집)", "집 = 매달린 Player 발 위치(점유 칸 왼쪽 아래 + (1, 0)). 아래 점은 집 기준 칸 오프셋이며 눈금 0.5칸으로 맞춥니다."), new GUIContent("(0, 0)"));
            for (int i = 0; i < points.Count; i++)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(i == points.Count - 1 ? $"점 {i + 1} (끝)" : $"점 {i + 1}", GUILayout.Width(62));
                    int indent = EditorGUI.indentLevel; EditorGUI.indentLevel = 0;
                    float x = EditorGUILayout.DelayedFloatField(points[i].x, GUILayout.MinWidth(36));
                    float y = EditorGUILayout.DelayedFloatField(points[i].y, GUILayout.MinWidth(36));
                    EditorGUI.indentLevel = indent;
                    var next = new Vector2(DungeonCranePaths.Snap(x), DungeonCranePaths.Snap(y));
                    if (next != points[i]) { points[i] = next; changed = true; }
                    using (new EditorGUI.DisabledScope(points.Count + 1 >= DungeonCranePaths.MaxPoints))
                        if (GUILayout.Button(new GUIContent("+", "이 점 뒤(다음 점과의 가운데)에 점을 끼워 넣습니다."), EditorStyles.miniButton, GUILayout.Width(22))) insert = i;
                    using (new EditorGUI.DisabledScope(points.Count <= 1))
                        if (GUILayout.Button(new GUIContent("삭제", "이 점을 지웁니다(뒤 점이 한 칸씩 당겨짐). 점은 2개(집 + 1) 이상이어야 합니다."), EditorStyles.miniButton, GUILayout.Width(38))) remove = i;
                }
            if (insert >= 0)
            {
                var from = points[insert];
                var to = insert + 1 < points.Count ? points[insert + 1] : from + new Vector2(4f, 0f);
                var middle = new Vector2(DungeonCranePaths.Snap((from.x + to.x) * .5f), DungeonCranePaths.Snap((from.y + to.y) * .5f));
                points.Insert(insert + 1, middle == from ? from + new Vector2(DungeonCranePaths.PointStep, 0f) : middle);
                changed = true;
            }
            if (remove >= 0) { points.RemoveAt(remove); changed = true; }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(points.Count + 1 >= DungeonCranePaths.MaxPoints))
                    if (GUILayout.Button(new GUIContent("+ 끝에 점 추가", "마지막 점에서 오른쪽 4칸에 점을 더합니다(최대 8점)."), EditorStyles.miniButton))
                    {
                        points.Add((points.Count > 0 ? points[points.Count - 1] : Vector2.zero) + new Vector2(4f, 0f));
                        changed = true;
                    }
                using (new EditorGUI.DisabledScope(points.Count + 1 >= DungeonCranePaths.MaxPoints && !dungeonPathPicking))
                {
                    bool picking = GUILayout.Toggle(dungeonPathPicking, new GUIContent("격자 클릭으로 점 추가", "켜면 격자를 누른 자리(매달린 발 위치)를 끝에 점으로 더합니다. Esc·다시 누르기로 끝냅니다(선택 / 이동 모드)."), EditorStyles.miniButton);
                    if (picking != dungeonPathPicking)
                    {
                        dungeonPathPicking = picking;
                        if (picking) { mode = 0; ResetGesture(); }
                    }
                }
            }
            if (dungeonPathPicking) EditorGUILayout.HelpBox("격자를 누르면 그 자리(매달린 Player 발)를 끝 점으로 더합니다. 눈금 0.5칸. Esc로 끝냅니다.", MessageType.Info);
            if (changed)
            {
                DungeonSetCranePath(index, points);
                GUIUtility.ExitGUI();
            }
            var feet = DungeonCranePaths.Feet(spawn.prefab, spawn.spawnPosition + Offset, spawn.deviceSettings);
            float length = DungeonCranePaths.Length(feet), speed = DungeonCranePaths.Speed(spawn.prefab, spawn.deviceSettings);
            EditorGUILayout.LabelField(new GUIContent($"경로 {feet.Count}점 · 길이 {length:0.#}칸 · 편도 약 {length / speed + .1f:0.##}초({speed:0.#}칸/초) · 도중 하차 복귀 약 {length / DungeonCranePaths.ReturnSpeed + .1f:0.##}초",
                "양 끝(점 0 = 집, 마지막 점)이 도크입니다. 크레인은 마지막으로 도착한 도크에서 쉬고, 붙으면 반대 도크까지 옮긴 뒤 매달린 채 기다립니다(점프·대시·붙기로 내림). "
                + "도중에 내리면 빈 크레인이 출발 도크로 40칸/초 돌아갑니다. 크레인이 없는 도크를 골라 ↑를 누르면 크레인이 그 도크에 바로 생겨나 같은 순간 붙습니다(돌아가는 중에도 됩니다 — 6차 T5). "
                + "끝 도크도 붙기 반경 안에 서는 자리가 있어야 합니다(템플릿 규칙 v4-3 — 5차)."), EditorStyles.miniLabel);
            if (data is DungeonRoomTemplateSO template)
            {
                string mark = $"#{index + 1} ";
                foreach (var issue in DungeonValidate(template).Where(i => i.Message.Contains(mark) && (i.Message.Contains("경로 크레인") || i.Message.Contains("크레인"))))
                    EditorGUILayout.HelpBox(issue.Message, issue.Error ? MessageType.Error : MessageType.Warning);
            }
        }
    }
    // 경로 크레인 index의 경로 점(집 제외, 집 기준 오프셋)을 통째로 바꾼다(Undo 기록). 회귀 도구도 이 경로로 바꾼다.
    private void DungeonSetCranePath(int index, List<Vector2> points)
    {
        if (Current?.spawnObjects == null || index < 0 || index >= Current.spawnObjects.Length) return;
        var spawn = Current.spawnObjects[index];
        if (spawn?.prefab == null) return;
        serialized?.ApplyModifiedProperties();
        var next = DungeonCranePaths.WithOffsets(spawn.prefab, spawn.deviceSettings, points ?? new List<Vector2>());
        Mutate(() => Current.spawnObjects[index].deviceSettings = next);
    }
    // 경로 크레인 index의 끝에 월드(Room 좌표) 발 위치 point를 점으로 더한다(최대 8점이면 false). 격자 클릭이 부른다.
    private bool DungeonAddCranePoint(int index, Vector2 point)
    {
        if (Current?.spawnObjects == null || index < 0 || index >= Current.spawnObjects.Length) return false;
        var spawn = Current.spawnObjects[index];
        if (spawn?.prefab == null || !DungeonCranePaths.IsPathCrane(spawn.prefab)) return false;
        var points = DungeonCranePaths.Offsets(spawn.prefab, spawn.deviceSettings).Skip(1).ToList();
        if (points.Count + 1 >= DungeonCranePaths.MaxPoints) return false;
        var home = DungeonCranePaths.HomeFeet(spawn.prefab, spawn.spawnPosition + Offset);
        points.Add(new Vector2(DungeonCranePaths.Snap(point.x - home.x), DungeonCranePaths.Snap(point.y - home.y)));
        DungeonSetCranePath(index, points);
        return true;
    }
    // 격자 클릭 점 추가: DrawGrid의 GUI.BeginClip(canvas) 안(GridInput보다 먼저)에서 부른다. 처리한 클릭은 Use해 선택·배치로 넘어가지 않게 한다.
    private void DungeonCranePathPickInput()
    {
        if (!dungeonPathPicking) return;
        var e = Event.current;
        var spawns = Current?.spawnObjects;
        if (mode != 0 || spawns == null || selected < 0 || selected >= spawns.Length || !DungeonCranePaths.IsPathCrane(spawns[selected]?.prefab)) { dungeonPathPicking = false; return; }
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { dungeonPathPicking = false; e.Use(); Repaint(); return; }
        if (e.type != EventType.MouseDown || e.button != 0 || !new Rect(Vector2.zero, canvas.size).Contains(e.mousePosition)) return;
        if (!DungeonAddCranePoint(selected, World(e.mousePosition + canvas.position))) { dungeonPathPicking = false; ShowNotification(new GUIContent("경로 점은 8개까지입니다.")); }
        e.Use();
    }

    // 적 구성 Wave를 편집하는 동안 맵 층(Wave 1)의 배치를 흐리게 그린다. DrawGrid의 GUI.BeginClip(canvas) 안에서 부른다.
    // 4차: 맵 층의 크레인 경로·붙기 반경과 생성 구역 상자도 흐리게 그린다(적을 놓을 자리를 맞추기 쉽게). 경로 점 격자 클릭 추가도 여기서 먼저 처리한다.
    private void DungeonDrawMapLayerGhosts()
    {
        DungeonCranePathPickInput();
        if (Event.current.type != EventType.Repaint || !(data is DungeonRoomTemplateSO) || waveIndex < 1 || data.waves == null || data.waves.Length == 0) return;
        var map = data.waves[0].spawnObjects;
        if (map == null || map.Length == 0) return;
        var offset = WaveRoomState.LayoutAt(data, 0).spawnOffset;
        var view = new Rect(Vector2.zero, canvas.size);
        var previous = GUI.color;
        GUI.color = new Color(1, 1, 1, .3f);
        try
        {
            foreach (var spawn in map)
            {
                if (spawn?.prefab == null) continue;
                var bounds = WavePrefabVisuals.RotatedBounds(spawn.prefab, spawn.rotationSteps, spawn.deviceSettings);
                var corner = Screen(spawn.spawnPosition + offset + new Vector2(bounds.min.x, bounds.max.y)) - canvas.position;
                var visual = new Rect(corner, new Vector2(bounds.size.x, bounds.size.y) * zoom);
                if (!visual.Overlaps(view)) continue;
                EditorGUI.DrawRect(visual, new Color(.09f, .11f, .15f, .25f));
                WavePrefabVisuals.Draw(visual, spawn.prefab, true, spawn.rotationSteps, spawn.deviceSettings);
            }
            foreach (var spawn in map)
            {
                if (spawn?.prefab == null || !(DungeonCranePaths.IsCrane(spawn.prefab) || WavePrefabVisuals.IsDungeonSpawnZone(spawn.prefab))) continue;
                WavePrefabVisuals.DrawRange(spawn.prefab, spawn.rotationSteps, Screen(spawn.spawnPosition + offset) - canvas.position, zoom, spawn.deviceSettings, .4f);
            }
        }
        finally { GUI.color = previous; }
    }
}
