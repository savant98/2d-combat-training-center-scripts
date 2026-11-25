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
public sealed partial class WaveEditorWindow
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
    private void DungeonTemplatePanel()
    {
        if (!(data is DungeonRoomTemplateSO template)) return;
        // 툴바 버튼은 적 구성 단위로 처리하지만(DungeonToolbar*), Undo·Inspector 직접 편집 뒤에는 정보 길이가 Wave 수와 다를 수 있다. Layout 때 한 번 맞춘다(Undo 기록).
        if (Event.current.type == EventType.Layout && (template.encounters?.Length ?? -1) != template.EncounterCount)
            Mutate(() => template.SyncEncounters());
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
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
            if (waveIndex == 0)
            {
                EditorGUILayout.HelpBox("맵 층: 지형·출입구(팔레트 '던전' 탭)·Prop(팔레트 'Prop' 탭)·고정 함정을 놓습니다. 적은 적 구성 Wave에 놓습니다.\n출입구는 좌우 바깥 벽 3칸 중 안쪽 2칸 자리에 놓습니다(회전 0° = 오른쪽 벽, 180° = 왼쪽 벽). 연결된 출입구만 실행 때 벽을 파고 세웁니다.\n"
                    + "목표 자리 표식(점선 앰버 4×6 뒤 벽 패널)·출구 자리 표식(점선 앰버 5×6 문 — 시작 전용 템플릿에 1개)은 실물이 설 수 있는 자리입니다. 실행 때 표식 자체는 생기지 않습니다.\n"
                    + "생성 구역 표식(지상 녹·공중 하늘·혼합 노랑 상자)은 동적 구성이 적을 내보내기를 선호하는 자리입니다. 크레인(경로·고정)은 붙기 반경 점선 고리로 보이고, 경로 크레인은 선택하면 아래에서 경로를 편집합니다.", MessageType.None);
                DungeonCranePathPanel();
                string clearance = DungeonTemplateValidation.ClearanceLine(template);
                if (!string.IsNullOrEmpty(clearance)) EditorGUILayout.LabelField(new GUIContent(clearance, "수직 여유(템플릿 규칙 v4-1): 서는 칸에서 위 플랫폼(지형·OneWay·고체 Prop) 아랫면까지 빈 칸이 5칸 이상인 서는 칸의 비율입니다. 75% 이상이면 통과(80% 미만은 경고)."), EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                var encounters = serialized.FindProperty("encounters");
                int index = waveIndex - 1;
                if (encounters != null && index < encounters.arraySize)
                {
                    var info = encounters.GetArrayElementAtIndex(index);
                    EditorGUILayout.PropertyField(info.FindPropertyRelative("name"), new GUIContent("적 구성 이름"));
                    EditorGUILayout.IntSlider(info.FindPropertyRelative("tier"), 1, 3, new GUIContent("난이도", "던전 설정의 난이도 범위 안에서 고릅니다."));
                    EditorGUILayout.PropertyField(info.FindPropertyRelative("weight"), new GUIContent("가중치", "같은 난이도 후보 사이에서 뽑힐 비율입니다."));
                }
                EditorGUILayout.HelpBox("적 구성 Wave는 맵 층 지형을 그대로 씁니다(흐리게 보이는 항목이 맵 층 배치). 이 Wave에서는 지형을 칠하지 말고 적과 이 구성 전용 함정만 놓으세요. 생성 시각은 방에 들어와 잠긴 뒤부터 셉니다.", MessageType.None);
            }
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
            EditorGUILayout.HelpBox("툴바의 '+ Wave'·'복제'·'삭제'도 방 템플릿에서는 적 구성 단위로 동작합니다(이름·난이도·가중치를 같은 번호로 함께 추가·복제·삭제). 맵 층(Wave 1)은 복제·삭제할 수 없습니다.", MessageType.None);
            var issues = DungeonValidate(template);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(issues.Count == 0 ? "템플릿 검사 통과" : $"템플릿 검사: 오류 {issues.Count(i => i.Error)} · 경고 {issues.Count(i => !i.Error)}", EditorStyles.miniBoldLabel);
                if (GUILayout.Button("다시 검사", EditorStyles.miniButton, GUILayout.Width(64))) { dungeonValidation.Clear(); issues = DungeonValidate(template); }
            }
            foreach (var issue in issues) EditorGUILayout.HelpBox(issue.Message, issue.Error ? MessageType.Error : MessageType.Warning);
            // '검사 통과'가 출입구까지 갈 수 있다는 뜻은 아니다. 도달성은 이 검사 범위 밖임을 늘 보인다.
            EditorGUILayout.HelpBox(DungeonTemplateValidation.ReachabilityNote, MessageType.Info);
        }
        GUILayout.Space(6);
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
            EditorGUILayout.LabelField(new GUIContent($"경로 {feet.Count}점 · 길이 {length:0.#}칸 · 편도 약 {length / speed + .1f:0.##}초({speed:0.#}칸/초) · 빈 크레인 약 {length / DungeonCranePaths.ReturnSpeed + .1f:0.##}초",
                "양 끝(점 0 = 집, 마지막 점)이 도크입니다. 크레인은 마지막으로 도착한 도크에서 쉬고, 붙으면 반대 도크까지 옮긴 뒤 매달린 채 기다립니다(점프·대시·붙기로 내림). "
                + "도중에 내리면 빈 크레인이 출발 도크로 40칸/초 돌아갑니다. 반대편 빈 도크에서 ↑를 누르면 크레인을 부르고, 도착 때 그 도크를 계속 고르고 있으면 자동으로 붙습니다. "
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

/// <summary>템플릿 검사 결과 한 줄. Error가 false면 경고다.</summary>
public readonly struct DungeonTemplateIssue
{
    public readonly bool Error;
    public readonly string Message;
    public DungeonTemplateIssue(bool error, string message) { Error = error; Message = message; }
    public override string ToString() => (Error ? "[오류] " : "[경고] ") + Message;
}

/// <summary>방 템플릿 메타(DESIGN_V2: 크기 등급·방향·배경·벽면 키트·꼬리표)의 표시 이름과 판정. 격자 편집기 패널·미리보기·검사가 같이 쓴다.</summary>
public static class DungeonTemplateMeta
{
    public static readonly string[] SizeLabels = { "큰 방", "작은 방" };
    public static readonly string[] OrientationLabels = { "수평", "수직", "혼합" };
    public static readonly string[] BackdropLabels = { "자동 (작은 방 = 정적 벽면 · 큰 방 = 시차 배경)", "시차 배경 (큰 Parallax)", "정적 벽면" };
    public static readonly string[] KitLabels = { "없음", "배관", "환풍 덕트", "전선 트렁크", "설비 벽면" };
    // 4차: 꼬리표 'NoCrane'(크레인 없음 — 휴식 방·피스톤 복도만, 템플릿 규칙 v4-3)·'TrapHeavy'(함정 위주 — 묶음 2~3, 규칙 v4-5).
    // 통합 4차에 계약 열거형 DungeonRoomTags 끝에 두 값을 더했다(빌더가 사양 꼬리표를 에셋에 그대로 옮긴다).
    public static readonly DungeonRoomTags[] TagOrder = BuildTagOrder();
    public static readonly string[] TagLabels = TagOrder.Select(t => t == DungeonRoomTags.Rest ? "휴식" : t == DungeonRoomTags.TeleportHub ? "텔레포트 허브" : t == DungeonRoomTags.TeleportNode ? "텔레포트 끝점"
        : t == DungeonRoomTags.DeadEnd ? "막다른 방" : t == DungeonRoomTags.NoStart ? "시작 불가" : t == DungeonRoomTags.TrapHeavy ? "함정 위주" : "크레인 없음").ToArray();
    static DungeonRoomTags[] BuildTagOrder()
    {
        var order = new List<DungeonRoomTags> { DungeonRoomTags.Rest, DungeonRoomTags.TeleportHub, DungeonRoomTags.TeleportNode, DungeonRoomTags.DeadEnd, DungeonRoomTags.NoStart };
        if (TryNoCraneTag(out var noCrane)) order.Add(noCrane);
        order.Add(DungeonRoomTags.TrapHeavy);
        return order.ToArray();
    }
    /// <summary>NoCrane 꼬리표를 쓸 수 있는 휴식 방 밖 템플릿(빌더 NoCraneAllowedIds·검사기 NO_CRANE_IDS와 같다).</summary>
    public static readonly string[] NoCraneAllowedNames = { "CombatPistonCorridor" };
    /// <summary>DungeonRoomTags에 'NoCrane' 값이 정의돼 있으면 그 값.</summary>
    public static bool TryNoCraneTag(out DungeonRoomTags tag)
    {
        tag = DungeonRoomTags.None;
        return Enum.TryParse("NoCrane", false, out tag) && tag != DungeonRoomTags.None && Enum.IsDefined(typeof(DungeonRoomTags), tag);
    }
    /// <summary>크레인 없음 예외(규칙 v4-3): 꼬리표 NoCrane(정의돼 있으면) 또는 컨셉 한 줄에 'NoCrane'이 적혀 있음(꼬리표 값이 계약에 생기기 전의 대신 표기).</summary>
    public static bool HasNoCraneTag(DungeonRoomTemplateSO template)
        => template != null && (TryNoCraneTag(out var tag) && template.HasTag(tag) || (template.concept ?? "").IndexOf("NoCrane", StringComparison.OrdinalIgnoreCase) >= 0);

    static string Pick(string[] names, int index) => names[Mathf.Clamp(index, 0, names.Length - 1)];
    public static string SizeName(DungeonRoomSize size) => Pick(SizeLabels, (int)size);
    public static string OrientationName(DungeonRoomOrientation orientation) => Pick(OrientationLabels, (int)orientation);
    public static string BackdropName(DungeonBackdropMode mode) => mode == DungeonBackdropMode.Parallax ? "시차 배경" : mode == DungeonBackdropMode.Wall ? "정적 벽면" : "자동";
    public static string KitName(DungeonWallKit kit) => Pick(KitLabels, (int)kit);
    public static string TagsName(DungeonRoomTags tags)
    {
        var names = new List<string>();
        for (int i = 0; i < TagOrder.Length; i++) if ((tags & TagOrder[i]) != 0) names.Add(TagLabels[i]);
        return names.Count == 0 ? "없음" : string.Join("·", names);
    }
    /// <summary>한 줄 요약: 크기 · 방향 · 배경(자동이면 푼 값과 표시) · 키트 · 꼬리표 · 던전당 최대 수.</summary>
    public static string Summary(DungeonRoomTemplateSO template)
    {
        if (template == null) return "";
        var backdrop = template.ResolvedBackdrop;
        string text = $"{SizeName(template.sizeClass)} · 방향 {OrientationName(template.orientation)} · 배경 {BackdropName(backdrop)}"
            + (template.backdropMode == DungeonBackdropMode.Auto ? "(자동)" : "")
            + (backdrop == DungeonBackdropMode.Wall ? $" · 벽면 {KitName(template.wallKit)}" : "")
            + $" · 꼬리표 {TagsName(template.tags)}";
        if (template.maxPerFloor > 0) text += $" · 던전당 최대 {template.maxPerFloor}";
        int slots = ObjectiveSlots(template).Count;
        if (slots > 0) text += $" · 목표 자리 {slots}";
        // 4차: 출구 자리·생성 구역·크레인 수.
        int exits = ExitSlots(template).Count, zones = SpawnZones(template).Count, cranes = CraneCount(template);
        if (exits > 0) text += $" · 출구 자리 {exits}";
        if (zones > 0) text += $" · 생성 구역 {zones}";
        if (cranes > 0) text += $" · 크레인 {cranes}";
        return text;
    }
    // 맵 층 항목 중 조건에 맞는 것의 spawnObjects 번호(놓인 순서).
    private static List<int> MapIndices(DungeonRoomTemplateSO template, Func<GameObject, bool> match)
    {
        var result = new List<int>();
        var spawns = template?.MapLayer?.spawnObjects;
        if (spawns == null) return result;
        for (int i = 0; i < spawns.Length; i++)
            if (spawns[i]?.prefab != null && match(spawns[i].prefab)) result.Add(i);
        return result;
    }
    /// <summary>4차: 맵 층 출구 자리 표식 항목(spawnObjects 번호, 놓인 순서). 실행 쪽(DungeonRoomData.exit)은 첫 표식을 쓴다.</summary>
    public static List<int> ExitSlots(DungeonRoomTemplateSO template) => MapIndices(template, WavePrefabVisuals.IsDungeonExitSlot);
    /// <summary>출구 자리 표식 순번 slot이 놓인 단위 칸. 찾지 못하면 모양의 첫 단위 칸.</summary>
    public static Vector2Int ExitSlotUnit(DungeonRoomTemplateSO template, int slot) => SpawnUnit(template, ExitSlots(template), slot);
    /// <summary>4차: 맵 층 생성 구역 표식 항목(spawnObjects 번호).</summary>
    public static List<int> SpawnZones(DungeonRoomTemplateSO template) => MapIndices(template, WavePrefabVisuals.IsDungeonSpawnZone);
    /// <summary>4차: 맵 층 크레인(경로·고정) 수.</summary>
    public static int CraneCount(DungeonRoomTemplateSO template) => MapIndices(template, DungeonCranePaths.IsCrane).Count;
    /// <summary>아직 적용하지 않은 SerializedObject 값으로 푼 배경(패널 표시용). 없으면 템플릿 값.</summary>
    public static DungeonBackdropMode ResolvedBackdrop(DungeonRoomTemplateSO template, SerializedObject serialized)
    {
        var mode = serialized?.FindProperty("backdropMode");
        var size = serialized?.FindProperty("sizeClass");
        if (mode == null || size == null) return template != null ? template.ResolvedBackdrop : DungeonBackdropMode.Parallax;
        if (mode.enumValueIndex != (int)DungeonBackdropMode.Auto) return (DungeonBackdropMode)mode.enumValueIndex;
        return size.enumValueIndex == (int)DungeonRoomSize.Small ? DungeonBackdropMode.Wall : DungeonBackdropMode.Parallax;
    }
    /// <summary>생성 규칙 v2의 시작 방 조건: 시작 방 가능 · '시작 불가'·'휴식' 꼬리표 없음 · 배경 시차 · 보스 방 아님.</summary>
    public static bool CanStart(DungeonRoomTemplateSO template)
        => template != null && template.role != DungeonRoomRole.Boss && template.allowAsStart && !template.HasTag(DungeonRoomTags.NoStart)
           && !template.HasTag(DungeonRoomTags.Rest) && template.ResolvedBackdrop == DungeonBackdropMode.Parallax;
    /// <summary>맵 층 텔레포트 시설 항목(spawnObjects 번호, 놓인 순서). 생성기의 '텔레포트 항목 순번'은 이 목록의 순번이다.</summary>
    public static List<int> TeleportPads(DungeonRoomTemplateSO template)
    {
        var result = new List<int>();
        var spawns = template?.MapLayer?.spawnObjects;
        if (spawns == null) return result;
        for (int i = 0; i < spawns.Length; i++)
            if (spawns[i]?.prefab != null && WavePrefabVisuals.IsDungeonTeleporter(spawns[i].prefab)) result.Add(i);
        return result;
    }
    /// <summary>텔레포트 짝을 받을 수 있는 템플릿: 꼬리표 '텔레포트 허브'·'텔레포트 끝점' + 맵 층 텔레포트 시설 + 보스 방 아님.</summary>
    public static bool CanHostTeleport(DungeonRoomTemplateSO template)
        => template != null && template.role != DungeonRoomRole.Boss
           && (template.HasTag(DungeonRoomTags.TeleportHub) || template.HasTag(DungeonRoomTags.TeleportNode)) && TeleportPads(template).Count > 0;
    /// <summary>텔레포트 항목 순번 pad가 놓인 단위 칸(모양 기준, TryReadDoors와 같은 실내 등분). 찾지 못하면 모양의 첫 단위 칸.</summary>
    public static Vector2Int PadUnit(DungeonRoomTemplateSO template, int pad) => SpawnUnit(template, TeleportPads(template), pad);

    // ───────── 3차(DESIGN_V3): 목표 자리 표식·생성 규칙 v3 ─────────
    /// <summary>
    /// 맵 층 목표 자리 표식 항목(spawnObjects 번호, 놓인 순서). '자리 표식 순번'(DungeonObjectiveAssignment.slot·DungeonObjectivePlacement.slot)은 이 목록의 순번이다
    /// (텔레포트 항목 순번과 같은 규칙 — 생성기·실행 쪽도 같은 순서로 세야 한다).
    /// </summary>
    public static List<int> ObjectiveSlots(DungeonRoomTemplateSO template)
    {
        var result = new List<int>();
        var spawns = template?.MapLayer?.spawnObjects;
        if (spawns == null) return result;
        for (int i = 0; i < spawns.Length; i++)
            if (spawns[i]?.prefab != null && WavePrefabVisuals.IsDungeonObjectiveSlot(spawns[i].prefab)) result.Add(i);
        return result;
    }
    /// <summary>자리 표식 순번 slot이 놓인 단위 칸. 찾지 못하면 모양의 첫 단위 칸.</summary>
    public static Vector2Int ObjectiveSlotUnit(DungeonRoomTemplateSO template, int slot) => SpawnUnit(template, ObjectiveSlots(template), slot);
    /// <summary>전투 방 수 입력 하한(생성 규칙 v3-1): 보스 방이 있으면 0(시작 방 + 보스 방), 없으면 1.</summary>
    public static int MinCombatRooms(bool includeBossRoom) => includeBossRoom ? 0 : 1;
    /// <summary>고정 시작 방(startTemplates)으로 쓸 수 있는지: 보스 방 아님 · 시차 배경('휴식'·'시작 불가'·시작 방 가능 여부는 보지 않음 — 생성 규칙 v3-2).</summary>
    public static bool CanFixedStart(DungeonRoomTemplateSO template)
        => template != null && template.role != DungeonRoomRole.Boss && template.ResolvedBackdrop == DungeonBackdropMode.Parallax;
    /// <summary>
    /// 빌더(DungeonTemplateBuilder.InspectV4 FightRoom)·정적 검사기(check_templates.py fight_room)와 같은 '적이 나오는 템플릿' 판정:
    /// 일반 방 · 출구 자리 표식 없음(시작 전용 아님) · 휴식 시작 방('휴식' + 시작 방 가능) 아님. 손 배치 적 구성 수는 보지 않는다(4차 동적 구성 템플릿은 구성이 0개다).
    /// </summary>
    public static bool IsFightTemplate(DungeonRoomTemplateSO template)
        => template != null && template.role == DungeonRoomRole.Normal && ExitSlots(template).Count == 0
           && !(template.HasTag(DungeonRoomTags.Rest) && template.allowAsStart);
    /// <summary>
    /// 이 던전 설정에서 template이 전투 방으로 싸우는지(실행 쪽 DungeonRoomNode.HasFight와 같은 뜻): 동적 구성(UsesDynamicEncounters)이면 적 구성이 없어도 전투 방은 싸우고,
    /// 손 배치 구성이면 적 구성이 1개 이상이어야 한다.
    /// </summary>
    public static bool FightsIn(DungeonWaveSettings settings, DungeonRoomTemplateSO template)
        => template != null && (template.EncounterCount > 0 || (settings != null && settings.UsesDynamicEncounters));
    /// <summary>
    /// 목표를 놓을 수 있는 일반 방 후보 템플릿: 가중치 > 0 · 보스 방 아님 · 전투 방으로 싸움(FightsIn — 동적 구성이면 적 구성이 없어도 됨, DungeonObjectiveRoomPicker의 HasFight와 같음)
    /// · 자리 표식 1개 이상 · (휴식 꼬리표면 레버 목표의 '휴식 방 허용').
    /// </summary>
    public static List<DungeonRoomTemplateSO> ObjectiveCandidates(DungeonWaveSettings settings, DungeonObjectiveSO objective)
    {
        var result = new List<DungeonRoomTemplateSO>();
        bool allowRest = !(objective is LeverObjectiveSO lever) || lever.allowRestRooms;
        foreach (var entry in settings?.templates ?? Array.Empty<DungeonTemplateEntry>())
        {
            var t = entry?.template;
            if (t == null || !(entry.weight > 0f) || result.Contains(t) || t.role == DungeonRoomRole.Boss || !FightsIn(settings, t)) continue;
            if (t.HasTag(DungeonRoomTags.Rest) && !allowRest) continue;
            if (ObjectiveSlots(t).Count > 0) result.Add(t);
        }
        return result;
    }
    /// <summary>목표 설정 요약 한 줄: 자리 표식 있는 전투 템플릿 n/m종(일반 후보 중).</summary>
    public static string ObjectiveCandidateLine(DungeonWaveSettings settings, DungeonObjectiveSO objective)
    {
        if (settings == null || objective == null) return null;
        int normal = (settings.templates ?? Array.Empty<DungeonTemplateEntry>()).Where(e => e?.template != null && e.weight > 0f && e.template.role != DungeonRoomRole.Boss).Select(e => e.template).Distinct().Count();
        return $"목표 자리 표식이 있는 전투 템플릿 {ObjectiveCandidates(settings, objective).Count}/{normal}종" + (objective is LeverObjectiveSO lever ? $" · 레버 {lever.count}개(방당 1개)" : "");
    }
    /// <summary>
    /// 목표 설정 검사(클리어 조건이 '목표 완료'일 때). 목표 SO 비어 있음, 실물 Prefab 없음·목표 실물 계약(IDungeonObjectivePoint) 없음,
    /// 자리 표식 있는 전투 템플릿 없음, 전투 방 최대 수 &lt; 레버 수, 같은 템플릿 최대 수 합 &lt; 레버 수.
    /// </summary>
    public static List<DungeonTemplateIssue> ObjectiveIssues(DungeonWaveSettings settings, DungeonObjectiveSO objective)
    {
        var issues = new List<DungeonTemplateIssue>();
        void Error(string message) => issues.Add(new DungeonTemplateIssue(true, message));
        void Warn(string message) => issues.Add(new DungeonTemplateIssue(false, message));
        if (settings == null) return issues;
        if (objective == null) { Error("목표 모듈이 비어 있습니다. 실행 때는 '모든 전투 방 클리어' 규칙으로 대신합니다."); return issues; }
        var lever = objective as LeverObjectiveSO;
        int needed = lever != null ? Mathf.Max(1, lever.count) : 1;
        GameObject prefab = null;
        try { prefab = objective.PrefabFor(0); } catch (Exception) { /* 파생 SO가 Edit Mode에서 실패해도 아래 경고로 알린다. */ }
        if (prefab == null) Error($"{objective.name}: 목표 실물 Prefab이 비어 실행 때 세울 것이 없습니다" + (lever != null ? "(레버 Prefab)." : "."));
        else if (prefab.GetComponent<IDungeonObjectivePoint>() == null) Warn($"{objective.name}: 실물 Prefab {prefab.name}에 목표 실물 컴포넌트(IDungeonObjectivePoint)가 없어 완료를 알릴 수 없습니다.");
        var candidates = ObjectiveCandidates(settings, objective);
        if (candidates.Count == 0)
            Error("목표 자리 표식이 있는 전투 방 템플릿(" + (settings.UsesDynamicEncounters ? "동적 구성 전투 방" : "적 구성 있음") + " · 보스 방 아님"
                + (lever != null && !lever.allowRestRooms ? " · 휴식 꼬리표 아님" : "") + ")이 후보에 없어 목표를 놓을 수 없습니다. 템플릿 맵 층에 목표 자리 표식을 놓으세요.");
        int maxRooms = Mathf.Max(settings.minRooms, settings.maxRooms);
        if (maxRooms < needed)
        {
            string message = $"전투 방 최대 {maxRooms}개가 레버 {needed}개보다 적습니다. 레버는 방당 1개라 다 놓지 못합니다";
            if (lever != null && lever.allowAppendedNodes) Warn(message + "(덧붙인 텔레포트 끝점 방이 있어야만 채워집니다).");
            else Error(message + ".");
        }
        int capacity = candidates.Sum(t => t.maxPerFloor > 0 ? t.maxPerFloor : Mathf.Max(1, settings.sameTemplateLimit));
        if (candidates.Count > 0 && capacity < needed)
            Warn($"자리 표식 있는 전투 템플릿 {candidates.Count}종의 던전당 최대 수 합이 {capacity}로 레버 {needed}개보다 적습니다. 생성이 자주 실패할 수 있습니다.");
        // 4차: 시작 방 출구(마무리 방식 ExitInStartRoom) — 출구 문 Prefab·출구 문 계약(IDungeonExitPoint)·출구 자리 표식이 있는 시작 후보.
        if (objective.finish == DungeonObjectiveFinish.ExitInStartRoom)
        {
            GameObject exit = null;
            try { exit = objective.ExitPrefab; } catch (Exception) { /* 파생 SO 계산 실패는 아래 오류로 알린다. */ }
            if (exit == null) Error($"{objective.name}: 마무리 방식이 '시작 방 출구'인데 출구 문 Prefab이 비어 있습니다. 실행 때는 즉시 클리어로 대신합니다.");
            else if (exit.GetComponent<IDungeonExitPoint>() == null) Warn($"{objective.name}: 출구 문 Prefab {exit.name}에 출구 문 컴포넌트(IDungeonExitPoint)가 없어 진행·입장을 받을 수 없습니다.");
            var starts = StartCandidates(settings);
            if (starts.Count > 0 && !starts.Any(t => ExitSlots(t).Count > 0))
                Error("마무리 방식이 '시작 방 출구'인데 시작 방 후보(" + string.Join(", ", starts.Select(t => t.name)) + ")에 출구 자리 표식(5×6)이 없습니다. 시작 전용 템플릿에 출구 자리 표식을 놓으세요.");
            else if (starts.Any(t => ExitSlots(t).Count == 0))
                Warn("시작 방 후보 중 출구 자리 표식이 없는 템플릿(" + string.Join(", ", starts.Where(t => ExitSlots(t).Count == 0).Select(t => t.name)) + ")은 시작 방으로 고르지 않습니다(생성 규칙 v4-2).");
        }
        return issues;
    }
    /// <summary>시작 방 후보: 고정 시작 방 목록이 있으면 그중 쓸 수 있는 것, 없으면 일반 후보 중 시작 방 조건(CanStart)을 채우는 템플릿.</summary>
    public static List<DungeonRoomTemplateSO> StartCandidates(DungeonWaveSettings settings)
    {
        var fixedStarts = (settings?.startTemplates ?? new List<DungeonRoomTemplateSO>()).Where(CanFixedStart).Distinct().ToList();
        if ((settings?.startTemplates?.Count ?? 0) > 0) return fixedStarts;
        return (settings?.templates ?? Array.Empty<DungeonTemplateEntry>()).Where(e => e?.template != null && e.weight > 0f).Select(e => e.template).Where(CanStart).Distinct().ToList();
    }

    // ───────── 4차: 적 생성 규칙 검사·요약 ─────────
    /// <summary>
    /// 적 생성 규칙 검사(편집기 표시): 종류 없음(오류), 종류 Prefab 없음·적이 아님, 단계 배열 길이 5 미만, 문턱 오름차순 아님, 어떤 단계의 가중치 합 0, Bulwark Prefab 없음(경고).
    /// </summary>
    public static List<DungeonTemplateIssue> EncounterRuleIssues(DungeonEncounterRulesSO rules)
    {
        var issues = new List<DungeonTemplateIssue>();
        void Error(string message) => issues.Add(new DungeonTemplateIssue(true, message));
        void Warn(string message) => issues.Add(new DungeonTemplateIssue(false, message));
        if (rules == null) return issues;
        var species = rules.species ?? Array.Empty<DungeonSpeciesRule>();
        if (species.Length == 0) Error($"{rules.name}: 적 종류가 없어 동적 구성이 적을 만들지 못합니다.");
        foreach (var rule in species)
        {
            if (rule == null) continue;
            string name = string.IsNullOrWhiteSpace(rule.name) ? "이름 없는 종류" : rule.name;
            if (rule.prefab == null) Warn($"{rules.name}: 종류 {name}의 Prefab이 비어 있어 그 종류는 나오지 않습니다.");
            else if (rule.prefab.GetComponent<EnemyController>() == null) Warn($"{rules.name}: 종류 {name}의 Prefab {rule.prefab.name}이 적(EnemyController)이 아닙니다.");
        }
        foreach (var (field, length) in new[] { ("단계 배율", rules.multipliers?.Length ?? 0), ("단계 최소 수", rules.minCounts?.Length ?? 0), ("단계 문턱", rules.thresholds?.Length ?? 0), ("동시 생존 상한", rules.concurrentCaps?.Length ?? 0) })
            if (length < DungeonEncounterRulesSO.LevelCount) Warn($"{rules.name}: {field} 값이 {length}개입니다(5단계 — 모자라면 마지막 값을 씁니다).");
        var thresholds = rules.thresholds ?? Array.Empty<int>();
        for (int i = 1; i < thresholds.Length; i++)
            if (thresholds[i] < thresholds[i - 1]) { Warn($"{rules.name}: 단계 문턱이 오름차순이 아닙니다({string.Join("·", thresholds)}). 실행 때는 앞 값으로 맞춥니다."); break; }
        for (int level = 0; level < DungeonEncounterRulesSO.LevelCount; level++)
        {
            var threat = (DungeonThreatLevel)level;
            if (species.Length > 0 && species.Where(s => s != null && s.prefab != null).Sum(s => s.Weight(threat)) <= 0f)
                Warn($"{rules.name}: {DungeonThreatText.Label(threat)} 단계에서 나올 수 있는 종류의 가중치 합이 0입니다.");
        }
        if (rules.bulwark == null || rules.bulwark.prefab == null) Warn($"{rules.name}: Bulwark Prefab이 비어 있어 큰 방에도 Bulwark가 나오지 않습니다.");
        return issues;
    }
    /// <summary>'단계: 입문 0 ×0.7 · 하급 2 ×1 · …' 한 줄(문턱 = 첫 클리어 방 수, 배율 = 적 수 배율).</summary>
    public static string ThreatLine(DungeonEncounterRulesSO rules)
    {
        if (rules == null) return null;
        var parts = new List<string>();
        for (int level = 0; level < DungeonEncounterRulesSO.LevelCount; level++)
        {
            var threat = (DungeonThreatLevel)level;
            int threshold = rules.thresholds != null && rules.thresholds.Length > 0 ? rules.thresholds[Mathf.Min(level, rules.thresholds.Length - 1)] : 0;
            parts.Add($"{DungeonThreatText.Label(threat)} {threshold}방 ×{rules.Multiplier(threat):0.##}(최소 {rules.MinCount(threat)} · 동시 {rules.ConcurrentCap(threat)})");
        }
        return "난이도 단계(첫 클리어 방 수 · 적 수 배율): " + string.Join(" / ", parts) + $" · 방당 최대 {Mathf.Max(1, rules.maxCount)}";
    }

    // 맵 층 항목 indices[order]의 기준점이 놓인 단위 칸(TryReadDoors와 같은 실내 등분). 찾지 못하면 모양의 첫 단위 칸.
    private static Vector2Int SpawnUnit(DungeonRoomTemplateSO template, List<int> indices, int order)
    {
        if (template == null) return Vector2Int.zero;
        var first = DungeonShapes.Units(template.shape).FirstOrDefault();
        if (order < 0 || order >= indices.Count || !template.TryGetCellBounds(out var box)) return first;
        var point = template.MapLayer.spawnObjects[indices[order]].spawnPosition;
        var size = DungeonShapes.Size(template.shape);
        int wall = DungeonRoomTemplateSO.WallThickness;
        float ix = box.xMin + wall, iy = box.yMin + wall;
        float iw = Mathf.Max(1f, box.width - 2 * wall), ih = Mathf.Max(1f, box.height - 2 * wall);
        var unit = new Vector2Int(Mathf.Clamp(Mathf.FloorToInt((point.x - ix) / iw * size.x), 0, size.x - 1),
            Mathf.Clamp(Mathf.FloorToInt((point.y - iy) / ih * size.y), 0, size.y - 1));
        return DungeonShapes.Contains(template.shape, unit) ? unit : first;
    }
}

/// <summary>생성 미리보기 통계(편집기 표시·회귀용).</summary>
public readonly struct DungeonPreviewSummary
{
    public readonly int Rooms, Combat, Small, Horizontal, Vertical, Mixed, Links, SameOrientationLinks, Rest, TeleportPairs;
    public DungeonPreviewSummary(int rooms, int combat, int small, int horizontal, int vertical, int mixed, int links, int sameOrientationLinks, int rest, int teleportPairs)
    {
        Rooms = rooms; Combat = combat; Small = small; Horizontal = horizontal; Vertical = vertical; Mixed = mixed;
        Links = links; SameOrientationLinks = sameOrientationLinks; Rest = rest; TeleportPairs = teleportPairs;
    }
    public string SizeLine => $"작은 방 {Small}/전투 {Combat} · 휴식 방 {Rest} · 텔레포트 {TeleportPairs}쌍";
    public string OrientationLine => $"방향: 수평 {Horizontal} · 수직 {Vertical} · 혼합 {Mixed} · 같은 방향 이웃 {SameOrientationLinks}/{Links}";
}

public static class DungeonPreviewStats
{
    /// <summary>작은 방은 전투 방 중에서, 방향·휴식은 모든 방(시작·보스 포함)에서 센다. 연결은 방 쌍마다 한 번(같은 쌍의 고리 연결도 따로 센다).</summary>
    public static DungeonPreviewSummary Summarize(DungeonLayout layout)
    {
        if (layout == null) return default;
        int combat = 0, small = 0, horizontal = 0, vertical = 0, mixed = 0, links = 0, same = 0, rest = 0, rooms = 0;
        foreach (var room in layout.rooms)
        {
            if (room == null) continue;
            rooms++;
            var template = room.template;
            if (room.kind == DungeonRoomKind.Combat)
            {
                combat++;
                if (template != null && template.sizeClass == DungeonRoomSize.Small) small++;
            }
            var orientation = template != null ? template.orientation : DungeonRoomOrientation.Mixed;
            if (orientation == DungeonRoomOrientation.Horizontal) horizontal++;
            else if (orientation == DungeonRoomOrientation.Vertical) vertical++;
            else mixed++;
            if (template != null && template.HasTag(DungeonRoomTags.Rest)) rest++;
            foreach (var link in room.links)
            {
                if (link.toRoom <= room.id) continue;
                links++;
                var other = layout.Room(link.toRoom)?.template;
                if (template != null && other != null && template.orientation != DungeonRoomOrientation.Mixed && template.orientation == other.orientation) same++;
            }
        }
        return new DungeonPreviewSummary(rooms, combat, small, horizontal, vertical, mixed, links, same, rest, layout.teleports.Count);
    }

    // ───────── 3차: 텔레포트 거리·목표 배치 ─────────
    /// <summary>방 id별 시작 방에서의 연결 거리(닿지 않으면 -1). 생성기와 같은 BFS(DungeonGenerator.Distances).</summary>
    public static int[] StartDistances(DungeonLayout layout) => layout != null && layout.rooms.Count > 0 ? Reachable(DungeonGenerator.Distances(layout)) : Array.Empty<int>();
    /// <summary>from 방에서의 연결 거리(닿지 않으면 -1).</summary>
    public static int[] DistancesFrom(DungeonLayout layout, int from) => layout != null ? Reachable(DungeonGenerator.DistancesFrom(layout, from)) : Array.Empty<int>();
    // 생성기는 닿지 않는 방을 int.MaxValue로 둔다. 표시·비교를 위해 -1로 바꾼다.
    private static int[] Reachable(int[] distance)
    {
        for (int i = 0; i < distance.Length; i++) if (distance[i] == int.MaxValue) distance[i] = -1;
        return distance;
    }
    /// <summary>텔레포트 짝의 연결 거리(BFS, 닿지 않으면 -1)와 던전 격자 거리(단위 칸 맨해튼).</summary>
    public static (int graph, int grid) TeleportDistance(DungeonLayout layout, DungeonTeleportLink pair)
    {
        var a = layout?.Room(pair.roomA);
        var b = layout?.Room(pair.roomB);
        if (a == null || b == null) return (-1, -1);
        var distance = DistancesFrom(layout, pair.roomA);
        return (pair.roomB < distance.Length ? distance[pair.roomB] : -1, DungeonGenerator.GridDistance(a, b));
    }
    public static string DistanceText(int distance) => distance >= 0 ? distance.ToString() : "닿지 않음";
    /// <summary>'텔레포트 거리: 방 0↔5 연결 5 · 격자 3 / …'. 설정의 최소 거리보다 작으면 '!'. 짝이 없으면 null.</summary>
    public static string TeleportLine(DungeonLayout layout, DungeonWaveSettings settings)
    {
        if (layout == null || layout.teleports.Count == 0) return null;
        var parts = new List<string>();
        foreach (var pair in layout.teleports)
        {
            var (graph, grid) = TeleportDistance(layout, pair);
            bool shortGraph = settings != null && graph >= 0 && graph < settings.teleportMinGraphDistance;
            bool shortGrid = settings != null && grid >= 0 && grid < settings.teleportMinGridDistance;
            parts.Add($"방 {pair.roomA}↔{pair.roomB} 연결 {DistanceText(graph)}{(shortGraph ? "!" : "")} · 격자 {grid}{(shortGrid ? "!" : "")}");
        }
        string limits = settings != null ? $" (최소 연결 {settings.teleportMinGraphDistance} · 격자 {settings.teleportMinGridDistance})" : "";
        return "텔레포트 거리" + limits + ": " + string.Join(" / ", parts);
    }
    /// <summary>
    /// '목표 3/3(필요 3) · 시작 거리 4/6/7 · 목표 방 최소 간격 3'. 목표가 없고 클리어 조건도 목표가 아니면 null.
    /// 필요 수는 목표 SO의 RequiredCount(놓은 수). 레버 목표면 놓은 수/레버 수로 보인다.
    /// </summary>
    public static string ObjectiveLine(DungeonLayout layout, DungeonWaveSettings settings)
    {
        if (layout == null) return null;
        bool expected = settings != null && settings.clearCondition == DungeonClearCondition.Objective;
        if (layout.objectives.Count == 0 && !expected) return null;
        int placed = layout.objectives.Count;
        var objective = settings?.objective;
        int wanted = objective is LeverObjectiveSO lever ? lever.count : placed;
        int required = placed;
        try { if (objective != null) required = objective.RequiredCount(placed); } catch (Exception) { /* 파생 SO 계산 실패는 놓은 수로 본다. */ }
        if (placed == 0) return $"목표 0/{wanted}개 — 목표를 놓지 못했습니다(생성 알림 확인). 실행 때는 '모든 전투 방 클리어'로 대신합니다.";
        var start = StartDistances(layout);
        var rooms = layout.objectives.Select(o => o.room).Distinct().ToList();
        string starts = string.Join("/", layout.objectives.OrderBy(o => o.index).Select(o => o.room >= 0 && o.room < start.Length ? DistanceText(start[o.room]) : "?"));
        int spacing = int.MaxValue;
        for (int i = 0; i < rooms.Count; i++)
        {
            var from = DistancesFrom(layout, rooms[i]);
            for (int j = i + 1; j < rooms.Count; j++)
                if (rooms[j] < from.Length && from[rooms[j]] >= 0) spacing = Mathf.Min(spacing, from[rooms[j]]);
        }
        string spacingText = rooms.Count < 2 ? "" : $" · 목표 방 최소 간격 {(spacing == int.MaxValue ? "닿지 않음" : spacing.ToString())}";
        string sameRoom = rooms.Count < placed ? $" · 같은 방에 {placed - rooms.Count}개 더" : "";
        return $"목표 {placed}/{wanted}개(필요 {required}) · 시작 거리 {starts}{spacingText}{sameRoom}";
    }

    // ───────── 4차: 시작 방 출구·동적 구성 예상 ─────────
    /// <summary>
    /// '출구: 시작 방 0(템플릿) 출구 자리 1'. 목표 SO가 '시작 방 출구'가 아니면 null. 시작 방에 출구 자리 표식이 없으면 missing = true(오류 문장).
    /// </summary>
    public static string ExitLine(DungeonLayout layout, DungeonWaveSettings settings, out bool missing)
    {
        missing = false;
        if (layout == null || settings?.objective == null || settings.objective.finish != DungeonObjectiveFinish.ExitInStartRoom) return null;
        var start = layout.Room(layout.startRoom);
        if (start?.template == null) { missing = true; return "출구: 시작 방을 찾지 못했습니다."; }
        int slots = DungeonTemplateMeta.ExitSlots(start.template).Count;
        if (slots == 0) { missing = true; return $"출구: 시작 방 {start.id}({start.template.name})에 출구 자리 표식이 없어 출구 문을 세우지 못합니다(시작 후보를 출구 자리 있는 템플릿으로)."; }
        return $"출구 ▣: 시작 방 {start.id}({start.template.name}) · 출구 자리 {slots}" + (slots > 1 ? "(첫 표식 사용)" : "") + $" · 문 {(settings.objective.ExitPrefab != null ? settings.objective.ExitPrefab.name : "없음")}";
    }
    /// <summary>방 하나의 동적 구성 예상: '빈 칸 520 · 예상 4/7/9/12/14(입문→특급)'. 측정하지 못하면 이유.</summary>
    public static string DynamicRoomText(DungeonRoomTemplateSO template, DungeonEncounterRulesSO rules)
    {
        if (template == null || rules == null) return "규칙 없음";
        if (!DungeonTemplateValidation.TryMeasure(template, out int empty, out var interior)) return "빈 칸을 재지 못함(실내가 닫히지 않음)";
        var counts = Enumerable.Range(0, DungeonEncounterRulesSO.LevelCount).Select(l => rules.TargetUnits((DungeonThreatLevel)l, empty, false)).ToArray();
        int bulwark = rules.bulwark != null ? rules.bulwark.MaxFor(DungeonThreatLevel.Extreme, interior.height, empty) : 0;
        return $"빈 칸 {empty} · 예상 {string.Join("/", counts)}(입문→특급)" + (bulwark > 0 ? $" · Bulwark 최대 {bulwark}(특급)" : "");
    }
    /// <summary>
    /// 동적 구성 요약: '동적 구성: 전투 방 4 · 빈 칸 평균 480 · 방당 예상 입문 3 ~ 특급 13(합 12 ~ 52)'. 손 배치 구성이면 null, 규칙이 없으면 경고 문장.
    /// </summary>
    public static string DynamicLine(DungeonLayout layout, DungeonWaveSettings settings)
    {
        if (layout == null || settings == null || settings.encounterMode != DungeonEncounterMode.Dynamic) return null;
        var rules = settings.encounterRules;
        if (rules == null) return "동적 구성인데 적 생성 규칙이 없어 손 배치 구성으로 대신합니다.";
        var combat = layout.rooms.Where(r => r != null && r.kind == DungeonRoomKind.Combat && r.template != null).ToList();
        if (combat.Count == 0) return "동적 구성: 전투 방 없음";
        var empty = new List<int>();
        foreach (var room in combat) if (DungeonTemplateValidation.TryMeasure(room.template, out int cells, out _)) empty.Add(cells);
        if (empty.Count == 0) return "동적 구성: 빈 칸을 잰 방이 없습니다(실내가 닫히지 않음).";
        int Total(DungeonThreatLevel level) => empty.Sum(a => rules.TargetUnits(level, a, false));
        int low = Total(DungeonThreatLevel.Intro), high = Total(DungeonThreatLevel.Extreme);
        return $"동적 구성: 전투 방 {combat.Count} · 빈 칸 평균 {Mathf.RoundToInt((float)empty.Average())} · 방당 예상 입문 {Mathf.RoundToInt(low / (float)empty.Count)} ~ 특급 {Mathf.RoundToInt(high / (float)empty.Count)}(합 {low} ~ {high}) · 단계는 첫 클리어 방 수로 오름";
    }
}

/// <summary>
/// 던전 방 템플릿 검사(편집기 전용, 데이터를 바꾸지 않는다). 격자 편집기의 템플릿 패널과 회귀 도구가 같은 규칙을 쓴다.
/// 출입구 자리·바닥·바깥 벽, 실내 닫힘(파기 전·후), 적 구성의 지형 재정의 금지, 맵 층 배치 규칙, L·ㄱ 빈 모서리, 단위 칸 크기,
/// 적 구성 적의 몸과 맵 층 고체 칸의 겹침, 적 구성 장치와 맵 층 점유 항목(Prop·출입구·장치)·출입구 앞 공간의 겹침을 본다.
/// 2차(DESIGN_V2): 실내 23×11 이상(카메라 확대 없음), 시작 가능 템플릿의 시차 배경, 정적 벽면의 키트, 텔레포트 꼬리표 ↔ 맵 층 텔레포트 시설,
/// 휴식 꼬리표 ↔ 수리대, 상호작용 시설 사용 지점 3.5칸 이상, 텔레포트 도착 지점 ↔ 적 생성 위치 4칸 이상, 크레인 이동 경로 빈 칸·끝과 출입구 3칸 이상,
/// 화물 승강기 위 정지점·통로, 늘어난 컨베이어·레일 칸, 적 구성의 시설·Swarmer 수. 시설은 텔레포트(계약 타입)만 타입으로, 나머지는 컴포넌트 이름으로 찾는다.
/// 3차(DESIGN_V3): 목표 자리 표식(DungeonObjectiveSlot — 맵 층만·바닥 위·출입구 앞 금지·보스/전투 아닌 방 경고), 승강기 정지점 = 기둥에서 3칸 안 출입구의 바닥,
/// 크레인 레일 이동 범위가 출입구 설 자리 x를 덮음(2차의 '크레인 끝 ↔ 출입구 3칸' 규칙은 없앰), 차폐막 스위치·캐터펄트·레버의 상호작용 간격과 바닥 받침,
/// 텔레포트 받침(크기는 Prefab 점유 칸 — 3차 6×4)의 바닥·지형 겹침, 적 구성 없는 휴식 시작 방 경고 완화.
/// 4차(DESIGN_V4 §6 — 빌더·정적 검사기와 같은 뜻): 삭제 시설(승강기·차폐막 스위치·캐터펄트) 오류, 목표 자리 4×6·출구 자리 5×6 뒤 벽 규칙(지형·고체 Prop 겹침,
/// 아랫줄 밑 가운데 2칸 서는 면, 출입구 벽면 3칸, 크레인 경로와 겹침 오류·생성 구역 겹침 경고, 크레인 반경 중심 3.5칸), 출구 자리(시작 전용·1개),
/// 경로 크레인(쓸기 상자 '발 ~ 발 + 4칸, 폭 2칸'의 고체 겹침·경로 점 실내·출입구 앞 통과 오류, 전투 구간 2곳 권장), 고정 크레인 사슬(반경 × 0.9)·서는 면 도달(경고),
/// 크레인 없음(큰 방 오류·작은 방 경고, 예외 NoCrane), 생성 구역(지상/공중 1개 이상·겹침 경고), 수직 여유 비율(경고 — 빌더는 75% 미만 오류), 레이저 예고 0.5초(경고).
/// 3차의 승강기 정지점·크레인 레일 덮기 검사는 지웠다(승강기 삭제·경로 크레인 재작성).
/// 통합 4차 검토 반영(빌더 DungeonTemplateBuilder.InspectV4가 정본 — 같은 상수·같은 칸 정의): 크레인 출입구 통과 ±1.5(경로 표본 0.5칸), 전투 구간 = 넉넉한 바닥 구간(여유 5 · 4칸),
/// 자리 표식 ↔ 출입구(행 바닥 − 1 ~ 바닥 + 3 · 벽면 3칸 · 사용 지점 ↔ 서는 자리 3.5칸), 자리 표식·생성 구역 ↔ 크레인 차지/쓸기 칸, Swarmer 12(오류),
/// 생성 구역 대상 = 적이 나오는 템플릿(IsFightTemplate — 손 배치 적 구성 수와 무관), 생성 구역 ↔ 함정 = 빌더 Trap 종류의 칸 + 위험 범위(점프대 제외).
/// 출입구 도달성은 보지 않는다(<see cref="ReachabilityNote"/>).
/// </summary>
public static class DungeonTemplateValidation
{
    /// <summary>템플릿 패널에 늘 보이는 안내. 도달성 BFS(걷기·점프·2단 점프·점프대·크레인)는 사양 검사기·빌더에만 있다.</summary>
    public const string ReachabilityNote = "출입구 도달성(맵 층 시작 좌표에서 걷기·점프·점프대·크레인으로 각 출입구·층·목표/출구 자리에 닿는지)은 이 검사에서 보지 않습니다. "
        + "tools/Unity/DungeonTemplateBuilder 사양 검사기(design/check_templates.py)를 참고하세요.";
    /// <summary>적 몸이 덮는 칸을 셀 때 맞닿은 면을 겹침으로 보지 않는 여유(칸). DungeonTemplateBuilder 검사와 같은 값.</summary>
    public const float BodyMargin = .08f;
    /// <summary>출입구 앞 공간의 폭(실내 쪽 칸 수, 판단값). 관에서 나온 Player가 서는 자리라 적 구성 장치를 두지 않는다. DungeonTemplateBuilder와 같은 값.</summary>
    public const int DoorFrontDepth = 2;

    /// <summary>기준 결투장 단위 칸 1개(실내 26×17 + 벽 3칸)의 외접 크기 가로:세로(판단값).</summary>
    public const float ReferenceUnitAspect = 32f / 23f;
    /// <summary>단위 칸 비율이 기준의 이 배수를 넘거나 1/배수보다 작으면 경고(판단값).</summary>
    public const float AspectTolerance = 2f;
    /// <summary>단위 칸 하나의 최소 외접 크기(벽 포함, 판단값). 이보다 작으면 관 출입구·전투 공간이 모자라다.</summary>
    public const int MinUnitWidth = 14, MinUnitHeight = 10;

    public static List<DungeonTemplateIssue> Check(DungeonRoomTemplateSO template)
    {
        var issues = new List<DungeonTemplateIssue>();
        void Error(string message) => issues.Add(new DungeonTemplateIssue(true, message));
        void Warn(string message) => issues.Add(new DungeonTemplateIssue(false, message));
        if (template == null) { Error("템플릿이 없습니다."); return issues; }
        var map = template.MapLayer;
        if (map == null) { Error("맵 층(Wave 1)이 없습니다."); return issues; }
        if (template.cellMap == null || !template.cellMap.enabled) Error("셀 맵이 꺼져 있습니다. 던전 방은 셀 지형만 지원합니다.");
        var cells = template.MapCells ?? Array.Empty<WaveMapCell>();
        var solids = new HashSet<Vector2Int>(cells.Where(c => c.kind == WaveCellKind.Platform).Select(c => c.position));
        if (solids.Count == 0 || !template.TryGetCellBounds(out var box)) { Error("맵 층에 지형(Platform) 칸이 없습니다."); return issues; }
        var size = DungeonShapes.Size(template.shape);
        Vector2Int UnitOf(Vector2 point) => new Vector2Int(
            Mathf.Clamp(Mathf.FloorToInt((point.x - box.xMin) / box.width * size.x), 0, size.x - 1),
            Mathf.Clamp(Mathf.FloorToInt((point.y - box.yMin) / box.height * size.y), 0, size.y - 1));

        // ── 맵 층 배치: 적 없음, 처치 필수 아님 ──
        var mapSpawns = map.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            string item = $"맵 층 #{i + 1} {(spawn?.prefab != null ? spawn.prefab.name : "참조 없음")}";
            if (spawn?.prefab == null) { Error(item + ": Prefab 참조가 비었습니다."); continue; }
            if (spawn.prefab.GetComponent<EnemyController>() != null) Error(item + ": 맵 층에 적이 있습니다. 적은 적 구성 Wave에 놓으세요.");
            else if (spawn.isRequiredToClear) Error(item + ": 맵 층 항목은 '처치 필수 목표'를 끄세요(Prop·출입구·함정은 방 클리어 조건이 아닙니다).");
        }
        if ((map.cellBatches?.Length ?? 0) > 0 || (map.terrainEvents?.Length ?? 0) > 0)
            Warn("맵 층에 시간별 지형 예약이 있습니다. 던전 방은 맵 층의 처음 지형만 씁니다.");

        // ── 출입구 ──
        var doors = new List<DungeonDoorSocket>();
        bool doorsRead = template.TryReadDoors(doors, out string doorError);
        if (!doorsRead) Error("출입구: " + doorError);
        else if (doors.Count == 0) Error("출입구가 없습니다. 팔레트 '던전' 탭의 출입구를 좌우 바깥 벽에 하나 이상 놓으세요.");
        var carved = new HashSet<Vector2Int>();
        foreach (var door in doors)
        {
            string label = $"출입구 {door.spawnIndex + 1}번({(door.side == DungeonDoorSide.Right ? "오른쪽" : "왼쪽")} 벽 · 단위 {door.unit})";
            var carve = door.carve;
            int notWall = 0;
            for (int x = carve.xMin; x < carve.xMax; x++)
                for (int y = carve.yMin; y < carve.yMax; y++)
                {
                    var p = new Vector2Int(x, y);
                    carved.Add(p);
                    if (!solids.Contains(p)) notWall++;
                }
            if (notWall > 0) Error($"{label}: 파낼 자리 x {carve.xMin}~{carve.xMax - 1}, y {carve.yMin}~{carve.yMax - 1} 중 {notWall}칸이 벽 칸이 아닙니다. 출입구는 벽 3칸 중 안쪽 2칸 자리에 놓아야 합니다.");
            for (int x = carve.xMin; x < carve.xMax; x++)
                if (!solids.Contains(new Vector2Int(x, door.floorY - 1))) { Error($"{label}: 출입구 아래 바닥 ({x}, {door.floorY - 1})이 Platform이 아닙니다."); break; }
            int frontX = door.side == DungeonDoorSide.Right ? door.wallFaceX - 1 : door.wallFaceX;
            if (!solids.Contains(new Vector2Int(frontX, door.floorY - 1)))
                Error($"{label}: 실내 쪽 한 칸 앞 바닥 ({frontX}, {door.floorY - 1})이 Platform이 아닙니다. 관에서 나온 Player가 설 바닥이 없습니다.");
            for (int y = door.floorY; y < door.floorY + DungeonDoor.Height; y++)
                if (solids.Contains(new Vector2Int(frontX, y))) { Warn($"{label}: 실내 쪽 출입구 앞 ({frontX}, {y})이 막혀 있습니다."); break; }
            int outerX = door.side == DungeonDoorSide.Right ? carve.xMax : carve.xMin - 1;
            for (int y = door.floorY; y < door.floorY + DungeonDoor.Height; y++)
                if (!solids.Contains(new Vector2Int(outerX, y))) { Error($"{label}: 바깥 벽 1칸 ({outerX}, {y})이 없어 출입구를 파면 방 공간이 밖으로 샙니다."); break; }
        }
        foreach (var group in doors.GroupBy(d => (d.unit, d.side)).Where(g => g.Count() > 1))
            Warn($"단위 {group.Key.unit}의 {(group.Key.side == DungeonDoorSide.Right ? "오른쪽" : "왼쪽")} 벽에 출입구가 {group.Count()}개 있습니다. 같은 이웃으로 이어지는 출입구는 하나만 둡니다.");

        // ── 실내 닫힘(파기 전·후) ──
        var start = WaveRoomState.PlayerStartAt(template, 0);
        var region = new HashSet<Vector2Int>();
        bool closed = TryRoom(solids, start, region);
        if (!closed) Error($"시작 좌표 ({start.x:0.##}, {start.y:0.##})에서 찾은 실내가 닫혀 있지 않거나 시작 좌표가 벽 안입니다. 맵 층 시작 좌표를 실내에 두고 벽을 닫으세요.");
        else
        {
            if (carved.Count > 0)
            {
                var open = new HashSet<Vector2Int>(solids); open.ExceptWith(carved);
                var openRegion = new HashSet<Vector2Int>();
                if (!TryRoom(open, start, openRegion)) Error("출입구를 모두 판 뒤 실내가 밖으로 샙니다. 출입구 바깥쪽 벽 1칸을 남기세요.");
                else
                    foreach (var door in doors)
                    {
                        var mouth = new Vector2Int(door.side == DungeonDoorSide.Right ? door.carve.xMin : door.carve.xMax - 1, door.floorY);
                        if (!openRegion.Contains(mouth)) Warn($"출입구 {door.spawnIndex + 1}번을 파도 실내와 이어지지 않습니다(실내 쪽이 막혔는지 확인).");
                    }
            }
            // 외접 사각형 안인데 실내 밖인 빈 칸(L·ㄱ 빈 모서리 등)은 카메라가 보므로 고체로 채운다.
            int outside = 0; Vector2Int? example = null;
            for (int x = box.xMin; x < box.xMax; x++)
                for (int y = box.yMin; y < box.yMax; y++)
                {
                    var p = new Vector2Int(x, y);
                    if (solids.Contains(p) || region.Contains(p)) continue;
                    outside++; example ??= p;
                }
            if (outside > 0) Warn($"외접 사각형 안에서 실내 밖인 빈 칸이 {outside}개 있습니다(예: {example}). L·ㄱ의 빈 모서리는 고체 칸으로 채우세요(카메라가 외접 사각형을 봅니다).");
            int strayInterior = region.Count(p => !DungeonShapes.Contains(template.shape, UnitOf(p + Vector2.one * .5f)));
            if (strayInterior > 0) Warn($"모양({DungeonShapes.DisplayName(template.shape)})에 없는 단위 칸 자리에 실내 칸이 {strayInterior}개 있습니다. 빈 단위 칸 쪽은 고체로 채우세요.");
        }

        // ── 단위 칸 크기 ──
        float unitWidth = box.width / (float)size.x, unitHeight = box.height / (float)size.y;
        float aspect = unitWidth / unitHeight / ReferenceUnitAspect;
        if (aspect > AspectTolerance || aspect < 1f / AspectTolerance)
            Warn($"단위 칸 하나가 {unitWidth:0.#}×{unitHeight:0.#}칸으로 기준 결투장(32×23) 비율과 많이 다릅니다. 모양({DungeonShapes.DisplayName(template.shape)})과 지형 크기를 확인하세요.");
        if (unitWidth < MinUnitWidth || unitHeight < MinUnitHeight)
            Warn($"단위 칸 하나가 {unitWidth:0.#}×{unitHeight:0.#}칸으로 작습니다(최소 {MinUnitWidth}×{MinUnitHeight}, 벽 포함).");

        // ── 맵 층 점유(Prop·출입구·장치)와 출입구 앞 공간: 적 구성 장치 겹침 검사에 쓴다 ──
        // 격자 Tool과 같은 규칙: 점유 칸 = WavePrefabVisuals.Cells(spawnPosition + spawnOffset, Footprint(회전)), 칸을 차지하지 않는 표시 장치는 뺀다.
        var mapOffset = WaveRoomState.LayoutAt(template, 0).spawnOffset;
        var mapOccupied = new Dictionary<Vector2Int, string>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || !WavePrefabVisuals.OccupiesCells(spawn.prefab)) continue;
            var area = WavePrefabVisuals.Cells(spawn.spawnPosition + mapOffset, WavePrefabVisuals.Footprint(spawn.prefab, spawn.rotationSteps));
            foreach (var c in area.allPositionsWithin) mapOccupied.TryAdd(c, $"#{i + 1} {spawn.prefab.name}");
        }
        var doorFront = new Dictionary<Vector2Int, int>();
        foreach (var door in doors)
        {
            // WaveEnvironmentTool이 없는 출입구도 파낼 칸을 점유로 본다.
            foreach (var c in door.carve.allPositionsWithin) mapOccupied.TryAdd(c, $"#{door.spawnIndex + 1} 출입구");
            int first = door.side == DungeonDoorSide.Right ? door.wallFaceX - DoorFrontDepth : door.wallFaceX;
            for (int x = first; x < first + DoorFrontDepth; x++)
                for (int y = door.floorY; y < door.floorY + DungeonDoor.Height; y++) doorFront.TryAdd(new Vector2Int(x, y), door.spawnIndex + 1);
        }
        // 고체 Prop 칸(컨테이너·폐자재 더미·관제 책상 등 Platform 레이어 BoxCollider2D, 클리어 뒤 열리는 셔터 제외).
        // 적 구성 Wave와 맵 층은 Wave가 달라 같은 칸 검사로 잡히지 않으므로 적 몸과 따로 비교한다(사양 빌더 plan.Blocked와 같은 규칙).
        var propSolidCells = new Dictionary<Vector2Int, string>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || NotSolidAtStart(WavePrefabVisuals.DungeonFacilityName(spawn.prefab))) continue;
            var cellsOfProp = new HashSet<Vector2Int>();
            SolidCells(spawn.prefab, spawn.spawnPosition + mapOffset, spawn.rotationSteps, cellsOfProp);
            foreach (var c in cellsOfProp) propSolidCells.TryAdd(c, $"#{i + 1} {spawn.prefab.name}");
        }
        // 적 몸 칸은 외접 사각형보다 한 칸 넓은 범위까지만 센다(그 밖은 어차피 실내 밖).
        var bodyLimit = new RectInt(box.xMin - 1, box.yMin - 1, box.width + 2, box.height + 2);
        var body = new List<Vector2Int>();

        // ── 적 구성 ──
        int encounterCount = template.EncounterCount;
        // 4차: 생성 구역 표식이 있으면 동적 구성(Dynamic) 전투 방을 뜻한다 — 적 구성 Wave가 없거나 비어도 동적 Wave에서는 적이 나온다.
        bool zoned = mapSpawns.Any(s => WavePrefabVisuals.IsDungeonSpawnZone(s?.prefab));
        if ((template.encounters?.Length ?? 0) != encounterCount) Warn($"적 구성 정보 {template.encounters?.Length ?? 0}개가 적 구성 Wave {encounterCount}개와 다릅니다(템플릿을 격자 편집기로 열면 맞춥니다).");
        // 통합 4차 검토 반영: 손 배치 적 구성이 없는 일반 템플릿은 동적 구성(Dynamic) 전용 전투 방이거나(빌더 FightRoom — 생성 구역은 CheckZones가 본다)
        // 시작 전용·휴식 시작 방이라 경고하지 않는다(빌더·정적 검사기도 안내만 남긴다). 실행 쪽 목표 배치도 동적 구성 방을 후보로 본다(DungeonObjectiveRoomPicker — HasFight).
        // 손 배치 Wave에 이 템플릿을 넣었는지는 Wave 설정의 목표·후보 검사(DungeonTemplateMeta.FightsIn)가 본다. 보스 방은 손 배치 보스 구성이 꼭 있어야 한다.
        if (encounterCount == 0 && template.role == DungeonRoomRole.Boss) Error("적 구성이 없어 이 템플릿을 쓸 수 없습니다. '적 구성 추가'로 만드세요.");
        else if (template.encounters != null && template.encounters.Where(e => e != null).All(e => e.weight <= 0)) Warn("모든 적 구성의 가중치가 0입니다.");
        if (template.role == DungeonRoomRole.Boss && template.allowAsStart) Warn("보스 방 템플릿이 '시작 방으로 쓸 수 있음'으로 되어 있습니다.");
        for (int w = 1; w < (template.waves?.Length ?? 0); w++)
        {
            var wave = template.waves[w];
            var info = template.encounters != null && w - 1 < template.encounters.Length ? template.encounters[w - 1] : null;
            string prefix = $"적 구성 {w}({(info != null ? info.name : "이름 없음")})";
            if (wave == null) { Error(prefix + ": Wave가 비었습니다."); continue; }
            if (wave.overrideCellMap) Error(prefix + ": 지형을 재정의합니다. 적 구성은 맵 층 지형을 그대로 써야 합니다(이 Wave에서 칠한 지형을 되돌리세요).");
            if (wave.changeTerrain) Error(prefix + ": 'Wave 시작 시 지형 변경'이 켜져 있습니다.");
            if ((wave.cellBatches?.Length ?? 0) > 0) Error(prefix + $": 시간별 생성 발판 {wave.cellBatches.Length}묶음이 있습니다. 적 구성은 지형을 바꾸지 않습니다.");
            if ((wave.terrainEvents?.Length ?? 0) > 0) Error(prefix + $": 시간별 지형 예약 {wave.terrainEvents.Length}개가 있습니다. 적 구성은 지형을 바꾸지 않습니다.");
            var spawns = wave.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
            var offset = WaveRoomState.LayoutAt(template, w).spawnOffset;
            int required = 0;
            for (int s = 0; s < spawns.Length; s++)
            {
                var spawn = spawns[s];
                if (spawn?.prefab == null) { Error(prefix + ": Prefab 참조가 빈 항목이 있습니다."); continue; }
                if (spawn.prefab.GetComponent<DungeonDoor>() != null) Error(prefix + ": 출입구는 맵 층에만 놓습니다.");
                if (spawn.isRequiredToClear && spawn.prefab.GetComponent<EnemyController>() != null) required++;
                if (spawn.keepAfterWave) Warn(prefix + $": {spawn.prefab.name}의 'Wave 종료 후 유지'는 던전 방에서 쓰이지 않습니다.");
                string item = $"{prefix} #{s + 1} {spawn.prefab.name}";
                if (spawn.prefab.GetComponent<EnemyController>() != null)
                {
                    // 적 몸: 격자 Tool이 놓은 원점에 Prefab의 실제 충돌 범위(Collider, 없으면 Renderer)를 더한 칸들.
                    bool beyond = EnemyBodyCells(spawn.prefab, spawn.spawnPosition + offset, bodyLimit, body, out var shape);
                    int hit = body.FindIndex(solids.Contains);
                    int propHit = hit >= 0 ? -1 : body.FindIndex(propSolidCells.ContainsKey);
                    if (hit >= 0)
                        Error($"{item}: 몸({shape.size.x:0.##}×{shape.size.y:0.##})이 맵 층 고체 칸 ({body[hit].x}, {body[hit].y})과 겹칩니다. 벽에 묻힌 적은 처치할 수 없어 방이 잠긴 채로 남습니다.");
                    else if (propHit >= 0)
                        Error($"{item}: 몸이 맵 층 고체 Prop {propSolidCells[body[propHit]]}의 칸 ({body[propHit].x}, {body[propHit].y})과 겹칩니다. Prop 속에 생긴 적은 처치할 수 없어 방이 잠긴 채로 남습니다.");
                    else if (beyond || (closed && body.Any(c => !region.Contains(c))))
                        Error($"{item}: 몸이 실내 밖으로 나갑니다. 실내 빈 칸 안에 놓으세요.");
                    int frontHit = body.FindIndex(doorFront.ContainsKey);
                    if (frontHit >= 0)
                        Error($"{item}: 출입구 {doorFront[body[frontHit]]}번 앞 공간 ({body[frontHit].x}, {body[frontHit].y})에 생성됩니다. 관에서 나온 Player와 겹쳐 바로 맞습니다.");
                }
                else if (WavePrefabVisuals.IsEnvironmentTool(spawn.prefab) && WavePrefabVisuals.OccupiesCells(spawn.prefab))
                {
                    // 적 구성 장치(함정 등)와 맵 층 점유 항목의 같은 칸: Wave가 달라 WaveAuthoring.Validate의 같은 칸 검사가 보지 못한다.
                    var area = WavePrefabVisuals.Cells(spawn.spawnPosition + offset, WavePrefabVisuals.Footprint(spawn.prefab, spawn.rotationSteps));
                    Vector2Int? shared = null, front = null;
                    foreach (var c in area.allPositionsWithin)
                    {
                        if (shared == null && mapOccupied.ContainsKey(c)) shared = c;
                        if (front == null && doorFront.ContainsKey(c)) front = c;
                    }
                    if (shared is Vector2Int sc) Error($"{item}: 맵 층 {mapOccupied[sc]}와 같은 칸 ({sc.x}, {sc.y})을 차지합니다. 실행 때 두 항목이 겹칩니다.");
                    if (front is Vector2Int fc) Error($"{item}: 출입구 {doorFront[fc]}번 앞 공간 ({fc.x}, {fc.y})을 차지합니다. 관에서 나온 Player가 바로 맞거나 막힙니다.");
                }
            }
            if (required == 0) Warn(prefix + ": 처치 필수 적이 없어 방에 들어가자마자 클리어됩니다" + (zoned ? "(동적 구성 Wave에서는 생성 구역으로 적을 짜므로 손 배치 Wave에 쓸 때만 해당)." : "."));
        }
        CheckV2(template, issues, solids, closed ? region : null, box, doors);
        return issues;
    }

    // ───────────────────────────── 2차·4차 검사(DESIGN_V2·V4) ─────────────────────────────
    /// <summary>
    /// 최소 실내(23×11). 3차까지는 'RoomViewRules: Confiner = 실내 + (2, 3)이 기본 시야 24.9×14를 덮는 크기'가 근거였다. 4차 카메라(던전 Wave는 연결 공간 + 사방 3칸, DESIGN_V4 §2.7)
    /// 뒤에는 더 작은 방도 늘림 없이 들어가지만, 작은 방 하한·전투 공간 기준으로 값은 그대로 둔다(템플릿 규칙 v4-6).
    /// </summary>
    public const int MinInteriorWidth = 23, MinInteriorHeight = 11;
    /// <summary>상호작용 시설(텔레포트·수리대·목표/출구 자리)과 크레인 붙기 반경 중심 사이 최소 거리. 상호작용은 가까운 Collider 하나만 잡는다.</summary>
    public const float InteractSpacing = 3.5f;
    /// <summary>텔레포트 도착 지점과 적 구성 적 생성 위치(몸 중심) 사이 최소 거리. 클리어 안 한 방에 도착하자마자 적이 옆에 나오지 않게 한다.</summary>
    public const float TeleportEnemyClearance = 4f;
    /// <summary>2차 규칙의 크레인 끝 ↔ 출입구 최소 거리. 3차부터 검사에 쓰지 않는다(외부 참조 호환용으로만 남김).</summary>
    public const float CraneDoorClearance = 3f;
    /// <summary>적 구성 하나의 Swarmer 상한(풀 예열 12/Prefab — 4차 방당 4묶음). DungeonTemplateBuilder·check_templates.py SWARMER_LIMIT과 같은 값(넘으면 오류).</summary>
    public const int SwarmerLimit = 12;
    /// <summary>경로 검사에 쓰는 Player 몸(판단값): 반폭 0.4, 높이 1.51(몸통 중심 0.755 — DungeonTeleporter.RecessPoint와 같은 기준).</summary>
    public const float PlayerHalfWidth = .4f, PlayerHeight = 1.51f;
    /// <summary>시설 배치별 설정 키(PropCraneRail·PropConveyor와 같은 문자열). 4차 크레인 키는 DungeonCranePaths에 있다.</summary>
    public const string CraneRailLengthKey = "railLength", ConveyorLengthKey = "conveyorLength";
    /// <summary>4차 레이저 사출기 예고선 키(traps4 — 공용 LaserEmitterCell 배치 설정)와 던전 묶음 하한(템플릿 규칙 v4-5: 예고 ≥ 0.5초).</summary>
    public const string LaserWarningKey = "laserWarningTime";
    public const float LaserWarningMin = .5f;
    /// <summary>수직 여유(템플릿 규칙 v4-1): 서는 칸 위 빈 칸 기준, 통과 비율(이 미만은 빌더 오류), 넉넉 비율(이 미만은 경고).</summary>
    public const int ClearanceCells = 5;
    public const float ClearancePass = .75f, ClearanceComfort = .80f;
    /// <summary>
    /// 경로 크레인이 지나야 하는 출입구 앞: 경로 표본(0.5칸)의 발이 서는 자리 x ± 이 값, 바닥 ~ 바닥 + CraneDoorRise칸(규칙 v4-3 '서는 자리 위 3칸 안').
    /// 통합 4차 검토 반영: 빌더(DungeonTemplateBuilder 1110행 `≤ 1.5`)·정적 검사기와 같은 값 — 빌더가 정본이다.
    /// </summary>
    public const float CraneDoorReach = 1.5f;
    public const int CraneDoorRise = 3;
    /// <summary>경로 표본 간격(칸) — 빌더 PolylineSamples·check_templates.py polyline_samples와 같은 0.5칸.</summary>
    public const float CranePathSampleStep = .5f;
    /// <summary>'넉넉한 바닥 구간'으로 볼 이어진 서는 칸 수(수직 여유 ≥ ClearanceCells) — 빌더 FloorSegment와 같은 4칸. 경로 크레인의 '전투 구간 통과'를 센다.</summary>
    public const int FloorSegmentCells = 4;
    /// <summary>고정 크레인 사슬 간격 상한 비율(붙기 반경 × 0.9, 규칙 v4-3).</summary>
    public const float CraneChainRatio = .9f;
    // 텔레포트 도착 몸통 = 루트 + (0, −0.5 + 0.755). 바뀌면 같이 고친다.
    const float TeleporterBodyLift = -DungeonTeleporter.StandOffsetY + .755f;
    const string Hook = DungeonCranePaths.PathComponent, Anchor = DungeonCranePaths.AnchorComponent, Rail = "PropCraneRail", Repair = "PropRepairStation",
        Shutter = "PropShutterGate", Conveyor = "PropConveyor", Teleporter = nameof(DungeonTeleporter);
    // 3·4차 시설(컴포넌트 이름 — WavePrefabVisuals.DungeonFacilityComponents와 같은 문자열)과 자리 표식 종류.
    const string Lever = "PropObjectiveLever", ExitGate = "PropExitGate", Slot = nameof(DungeonObjectiveSlot), ExitSlot = nameof(DungeonExitSlot);
    /// <summary>목표·출구 자리 표식과 출입구 실내 벽면 사이 최소 가로 거리(칸). 관에서 나온 Player가 서는 자리·관 통과 판정을 피한다(판단값).</summary>
    public const int ObjectiveDoorClearance = 3;
    // 처음에는 고체가 아닌 Prop 벽: 클리어 뒤 열리는 셔터(4차: 차폐막 스위치는 삭제).
    static bool NotSolidAtStart(string kind) => kind == Shutter;
    /// <summary>출입구 설 자리(DungeonDoor.StandPoint)의 실내 벽면에서 안쪽 거리: 기준점(벽 2칸 가운데) − 바깥 방향 × 2.2.</summary>
    public const float DoorStandInset = 2.2f - DungeonDoor.Depth * .5f;
    /// <summary>출입구 설 자리 x(템플릿 좌표, DungeonDoor.StandPoint와 같은 계산).</summary>
    public static float DoorStandX(DungeonDoorSocket door) => door.side == DungeonDoorSide.Right ? door.wallFaceX - DoorStandInset : door.wallFaceX + DoorStandInset;
    // 상호작용 지점: Prefab 안 FacilityInteractPoint(트리거 상자 중심)마다 하나. 없으면 점유 칸 바닥에 선 몸 중심.
    static List<(string what, Vector2 point)> InteractPoints(GameObject prefab, Vector2 root, Vector2Int footprint)
    {
        var result = new List<(string, Vector2)>();
        var toRoot = prefab.transform.worldToLocalMatrix;
        foreach (var point in prefab.GetComponentsInChildren<FacilityInteractPoint>(true))
        {
            var world = point.TryGetComponent<BoxCollider2D>(out var area) ? point.transform.TransformPoint(area.offset) : point.transform.position;
            result.Add((point.gameObject == prefab ? "" : " " + point.name, root + (Vector2)toRoot.MultiplyPoint3x4(world)));
        }
        if (result.Count == 0) result.Add(("", new Vector2(root.x, root.y - footprint.y * .5f + .75f)));
        return result;
    }
    // 삭제한 시설의 한글 이름.
    static string DeletedLabel(string component) => component == "PropCargoLift" ? "화물 승강기" : component == "PropBarrierSwitch" ? "차폐막 스위치" : component == "PropCatapult" ? "사출 캐터펄트" : component;

    private sealed class Facility
    {
        public int index;
        public string kind;
        public WaveSpawnInfo spawn;
        public Vector2 root;
        public Vector2Int footprint;
        public string label;
    }
    // 생성 구역(맵 층 표식 하나).
    private sealed class Zone
    {
        public int index;
        public Rect rect;
        public int kind;
        public string label;
    }

    private static void CheckV2(DungeonRoomTemplateSO template, List<DungeonTemplateIssue> issues, HashSet<Vector2Int> solids, HashSet<Vector2Int> region, RectInt box, List<DungeonDoorSocket> doors)
    {
        void Error(string message) => issues.Add(new DungeonTemplateIssue(true, message));
        void Warn(string message) => issues.Add(new DungeonTemplateIssue(false, message));
        var mapSpawns = template.MapLayer?.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
        var mapOffset = WaveRoomState.LayoutAt(template, 0).spawnOffset;
        int wall = DungeonRoomTemplateSO.WallThickness;

        // ── 실내 크기: 시작 좌표에서 찾은 닫힌 실내의 외접 크기. 닫히지 않았으면 셀 외접 − 벽 두께. ──
        RectInt interior;
        if (region != null && region.Count > 0)
        {
            int xMin = region.Min(c => c.x), yMin = region.Min(c => c.y), xMax = region.Max(c => c.x), yMax = region.Max(c => c.y);
            interior = new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
        }
        else interior = new RectInt(box.xMin + wall, box.yMin + wall, Mathf.Max(0, box.width - 2 * wall), Mathf.Max(0, box.height - 2 * wall));
        if (interior.width < MinInteriorWidth || interior.height < MinInteriorHeight)
            Error($"실내 {interior.width}×{interior.height}칸이 최소 크기 {MinInteriorWidth}×{MinInteriorHeight}보다 작습니다"
                + (template.sizeClass == DungeonRoomSize.Small ? "(작은 방 하한)" : "") + ". 전투 공간이 모자라고 작은 화면 비율에서 카메라가 확대될 수 있습니다.");

        // ── 배경·시작 방 ──
        var backdrop = template.ResolvedBackdrop;
        if (backdrop == DungeonBackdropMode.Wall && template.wallKit == DungeonWallKit.None)
            Warn("배경이 정적 벽면인데 벽면 키트가 '없음'입니다. 실행 때 시차 배경으로 남습니다(배관·환풍 덕트·전선 트렁크·설비 벽면 중 고르세요).");
        if (template.allowAsStart && template.role != DungeonRoomRole.Boss)
        {
            if (template.HasTag(DungeonRoomTags.NoStart)) Warn("'시작 방으로 쓸 수 있음'과 꼬리표 '시작 불가'가 함께 켜져 있습니다. 생성기는 이 방을 시작 방으로 고르지 않습니다.");
            // 적 구성 없는 휴식 방은 고정 시작 방(startTemplates)으로 쓰는 구성이라 경고하지 않는다(생성 규칙 v3-2).
            else if (template.HasTag(DungeonRoomTags.Rest) && template.EncounterCount > 0)
                Warn("휴식 방(꼬리표 '휴식')은 일반 시작 방 후보에서 빠집니다. '시작 방으로 쓸 수 있음'을 끄세요(던전 설정의 '고정 시작 방'으로는 쓸 수 있습니다).");
            else if (backdrop != DungeonBackdropMode.Parallax)
                Warn($"시작 방은 관 통과 덮개 없이 바로 보이므로 배경이 시차 배경인 방만 씁니다. 이 방은 배경이 {DungeonTemplateMeta.BackdropName(backdrop)}이라 시작 방 후보에서 빠집니다(배경을 시차 배경으로 두거나 '시작 방으로 쓸 수 있음'을 끄세요).");
        }

        // ── 맵 층 시설 · 4차 삭제 시설 ──
        var facilities = new List<Facility>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null) continue;
            string deleted = WavePrefabVisuals.DeletedFacilityName(spawn.prefab);
            if (deleted != null)
            {
                Error($"맵 층 #{i + 1} {spawn.prefab.name}: 4차에서 삭제한 시설({DeletedLabel(deleted)})입니다. 지우고 크레인(경로·고정)·점프대로 바꾸세요(템플릿 규칙 v4-4).");
                continue;
            }
            string kind = WavePrefabVisuals.DungeonFacilityName(spawn.prefab);
            if (kind == null) continue;
            var facility = new Facility
            {
                index = i, kind = kind, spawn = spawn, root = spawn.spawnPosition + mapOffset,
                footprint = WavePrefabVisuals.Footprint(spawn.prefab, 0), label = $"맵 층 #{i + 1} {spawn.prefab.name}",
            };
            if (kind == Hook || kind == Anchor) facility.footprint = DungeonCranePaths.Footprint(spawn.prefab);
            facilities.Add(facility);
            if ((((spawn.rotationSteps % 4) + 4) % 4) != 0) Warn($"{facility.label}: 시설은 회전 0°로 둡니다(배치 규약). 회전한 시설의 경로·사용 지점은 회전 없이 검사합니다.");
        }

        // ── 목표·출구 자리 표식(맵 층, 회전 0°). 목표는 전투 방에서, 출구는 시작 방에서만 쓰인다 ──
        var slots = new List<Facility>();
        int objectiveSlots = 0, exitSlots = 0;
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || !WavePrefabVisuals.IsDungeonSlot(spawn.prefab)) continue;
            bool exit = WavePrefabVisuals.IsDungeonExitSlot(spawn.prefab);
            var slot = new Facility
            {
                index = i, kind = exit ? ExitSlot : Slot, spawn = spawn, root = spawn.spawnPosition + mapOffset,
                footprint = WavePrefabVisuals.SlotFootprint(spawn.prefab, 0),
                label = exit ? $"맵 층 #{i + 1} 출구 자리 표식 {++exitSlots}번" : $"맵 층 #{i + 1} 목표 자리 표식 {++objectiveSlots}번",
            };
            slots.Add(slot);
            if ((((spawn.rotationSteps % 4) + 4) % 4) != 0) Warn($"{slot.label}: 자리 표식은 회전 0°로 둡니다(실물은 회전 없이 뒤 벽에 섭니다).");
        }
        if (objectiveSlots > 0)
        {
            if (template.role == DungeonRoomRole.Boss) Warn($"보스 방의 목표 자리 표식 {objectiveSlots}개는 쓰이지 않습니다(목표는 전투 방에만 놓습니다).");
            // 통합 4차 검토 반영: 적 구성 수가 아니라 빌더와 같은 '시작 전용(출구 자리 있음)' 기준으로 본다 — 동적 구성 전투 방의 목표 자리는 쓰인다.
            else if (exitSlots > 0) Warn($"시작 전용 템플릿(출구 자리 표식 있음)의 목표 자리 표식 {objectiveSlots}개는 쓰이지 않습니다(목표는 전투 방에만 놓습니다 — 빌더·정적 검사기는 오류, 규칙 v4-7).");
            else if (template.HasTag(DungeonRoomTags.Rest)) Warn("휴식 방의 목표 자리 표식은 레버 목표의 '휴식 방 허용'을 켰을 때만 쓰입니다.");
        }
        // 4차 출구 자리(규칙 v4-8): 시작 전용 템플릿만, 1개.
        if (exitSlots > 1) Error($"출구 자리 표식이 {exitSlots}개입니다. 시작 방 출구는 1개만 둡니다(실행 쪽은 첫 표식만 씁니다).");
        if (exitSlots > 0)
        {
            if (template.role == DungeonRoomRole.Boss || !template.allowAsStart)
                Error("출구 자리 표식은 시작 전용 템플릿(시작 방으로 쓸 수 있음 · 보스 방 아님)에만 둡니다. 다른 방의 출구 자리는 쓰이지 않습니다.");
            else if (template.EncounterCount > 0)
                Warn("적 구성이 있는 템플릿에 출구 자리 표식이 있습니다. 시작 방으로 골랐을 때만 쓰입니다(시작 전용 템플릿 권장 — 규칙 v4-8).");
        }
        foreach (var f in facilities.Where(f => f.kind == Lever))
            Warn($"{f.label}: 목표 레버 실물을 템플릿에 직접 놓았습니다. 직접 놓은 레버는 목표 진행에 들어가지 않습니다. 목표 자리 표식을 놓고 목표 SO가 레버를 세우게 하세요.");
        foreach (var f in facilities.Where(f => f.kind == ExitGate))
            Warn($"{f.label}: 출구 문 실물을 템플릿에 직접 놓았습니다. 직접 놓은 문은 레버 진행을 받지 않습니다. 출구 자리 표식을 놓고 목표 SO(마무리 방식 '시작 방 출구')가 문을 세우게 하세요.");

        // ── 텔레포트 꼬리표 ↔ 맵 층 텔레포트 시설, 휴식 꼬리표 ↔ 수리대 ──
        int pads = facilities.Count(f => f.kind == Teleporter);
        bool teleportTag = template.HasTag(DungeonRoomTags.TeleportHub) || template.HasTag(DungeonRoomTags.TeleportNode);
        if (teleportTag && pads == 0) Error("꼬리표 '텔레포트 허브'·'텔레포트 끝점'이 있는데 맵 층에 텔레포트 시설(DungeonTeleporter)이 없습니다. 생성기가 짝을 짓지 않습니다.");
        if (!teleportTag && pads > 0) Error($"맵 층에 텔레포트 시설 {pads}개가 있는데 꼬리표 '텔레포트 허브'·'텔레포트 끝점'이 없습니다. 짝을 짓지 않아 늘 꺼진 받침으로 섭니다.");
        if (pads > 0 && template.role == DungeonRoomRole.Boss) Warn("보스 방의 텔레포트 시설은 짝을 짓지 않습니다(보스 방 제외 규칙).");
        int repairs = facilities.Count(f => f.kind == Repair);
        if (template.HasTag(DungeonRoomTags.Rest) && repairs == 0) Warn("꼬리표 '휴식'인데 맵 층에 수리대(PropRepairStation)가 없습니다.");
        if (!template.HasTag(DungeonRoomTags.Rest) && repairs > 0) Warn($"수리대 {repairs}개가 있는데 꼬리표 '휴식'이 없습니다. 휴식 방 개수·시작 방 거리 제한을 받지 않습니다.");

        // ── 적 구성: 시설·표식 금지, 삭제 시설, Swarmer 수, 텔레포트 도착 지점 ↔ 적 생성 위치 ──
        var padPoints = facilities.Where(f => f.kind == Teleporter).Select(f => (facility: f, point: f.root + new Vector2(0f, TeleporterBodyLift))).ToList();
        for (int w = 1; w < (template.waves?.Length ?? 0); w++)
        {
            var wave = template.waves[w];
            if (wave == null) continue;
            var info = template.encounters != null && w - 1 < template.encounters.Length ? template.encounters[w - 1] : null;
            string prefix = $"적 구성 {w}({(info != null ? info.name : "이름 없음")})";
            var offset = WaveRoomState.LayoutAt(template, w).spawnOffset;
            var spawns = wave.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
            int swarmers = 0;
            for (int s = 0; s < spawns.Length; s++)
            {
                var spawn = spawns[s];
                if (spawn?.prefab == null) continue;
                string item = $"{prefix} #{s + 1} {spawn.prefab.name}";
                string deleted = WavePrefabVisuals.DeletedFacilityName(spawn.prefab);
                if (deleted != null) { Error($"{item}: 4차에서 삭제한 시설({DeletedLabel(deleted)})입니다. 지우세요(템플릿 규칙 v4-4)."); continue; }
                string kind = WavePrefabVisuals.DungeonFacilityName(spawn.prefab);
                if (WavePrefabVisuals.IsDungeonObjectiveSlot(spawn.prefab)) Error($"{item}: 목표 자리 표식은 맵 층에만 놓습니다(적 구성에 놓은 표식은 생성기가 읽지 않습니다).");
                if (WavePrefabVisuals.IsDungeonExitSlot(spawn.prefab)) Error($"{item}: 출구 자리 표식은 맵 층에만 놓습니다(적 구성에 놓은 표식은 읽지 않습니다).");
                if (WavePrefabVisuals.IsDungeonSpawnZone(spawn.prefab)) Error($"{item}: 생성 구역 표식은 맵 층에만 놓습니다(적 구성에 놓은 표식은 읽지 않습니다).");
                if (kind == Teleporter) Error($"{item}: 텔레포트 시설은 맵 층에만 놓습니다(적 구성으로 나온 시설은 짝을 짓지 않습니다).");
                else if (kind != null) Warn($"{item}: 방 클리어에 반응하는 시설은 맵 층에 놓습니다(적 구성으로 두면 고른 구성에 따라 있었다 없었다 합니다).");
                if (spawn.prefab.GetComponent<EnemyController>() == null) continue;
                if (spawn.prefab.name.IndexOf("Swarmer", StringComparison.OrdinalIgnoreCase) >= 0) swarmers++;
                var center = spawn.spawnPosition + offset + (Vector2)WavePrefabVisuals.LocalBounds(spawn.prefab, true).center;
                foreach (var (pad, point) in padPoints)
                {
                    float distance = Vector2.Distance(center, point);
                    if (distance >= TeleportEnemyClearance) continue;
                    Error($"{item}: {pad.label}의 도착 지점과 {distance:0.#}칸으로 가깝습니다({TeleportEnemyClearance:0.#}칸 이상). 텔레포트로 클리어 안 한 방에 오면 적이 바로 옆에 나옵니다.");
                    break;
                }
            }
            // 통합 4차 검토 반영: 빌더(DungeonTemplateBuilder SwarmerLimit 12 · 오류)와 같은 상한·심각도.
            if (swarmers > SwarmerLimit) Error($"{prefix}: Swarmer {swarmers}마리입니다. 방마다 {SwarmerLimit}마리(4묶음) 이하로 둡니다(풀 예열 상한 — 빌더와 같은 기준).");
        }

        // ── 경로 검사용 칸: 지형 고체 + 고체 Prop(Platform 레이어 BoxCollider2D, 셔터 제외 — 클리어 뒤 열림), 바닥(+ OneWay·Prop 윗면), 점유 칸 ──
        var propSolids = new Dictionary<int, HashSet<Vector2Int>>();
        var propSurfaces = new HashSet<Vector2Int>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || NotSolidAtStart(WavePrefabVisuals.DungeonFacilityName(spawn.prefab))) continue;
            var cells = new HashSet<Vector2Int>();
            SolidCells(spawn.prefab, spawn.spawnPosition + mapOffset, spawn.rotationSteps, cells);
            if (cells.Count > 0) propSolids[i] = cells;
            SolidCells(spawn.prefab, spawn.spawnPosition + mapOffset, spawn.rotationSteps, propSurfaces, OneWayLayer());
        }
        var propSolidAll = new HashSet<Vector2Int>(propSolids.Values.SelectMany(c => c));
        string PropAt(Vector2Int cell)
        {
            foreach (var pair in propSolids) if (pair.Value.Contains(cell)) return mapSpawns[pair.Key]?.prefab != null ? $"#{pair.Key + 1} {mapSpawns[pair.Key].prefab.name}" : $"#{pair.Key + 1}";
            return null;
        }
        var oneWayCells = new HashSet<Vector2Int>((template.MapCells ?? Array.Empty<WaveMapCell>()).Where(c => c.kind == WaveCellKind.OneWayPlatform).Select(c => c.position));
        var floors = new HashSet<Vector2Int>(solids);
        floors.UnionWith(oneWayCells);
        floors.UnionWith(propSolidAll);
        floors.UnionWith(propSurfaces);
        bool Hard(Vector2Int cell) => solids.Contains(cell) || propSolidAll.Contains(cell);
        var occupied = new Dictionary<Vector2Int, int>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || !WavePrefabVisuals.OccupiesCells(spawn.prefab)) continue;
            foreach (var c in WavePrefabVisuals.Cells(spawn.spawnPosition + mapOffset, WavePrefabVisuals.Footprint(spawn.prefab, spawn.rotationSteps)).allPositionsWithin) occupied.TryAdd(c, i);
        }
        foreach (var door in doors) foreach (var c in door.carve.allPositionsWithin) occupied.TryAdd(c, door.spawnIndex);
        string OccupantName(int index) => index >= 0 && index < mapSpawns.Length && mapSpawns[index]?.prefab != null ? $"#{index + 1} {mapSpawns[index].prefab.name}" : $"#{index + 1}";

        // 서는 칸(수직 여유·고정 크레인 도달 기준): 실내 빈 칸 · 그 칸과 위 칸이 플랫폼 아님 · 아래 칸이 받침(지형·OneWay·고체/OneWay Prop).
        var stands = StandCells(region, floors, solids, oneWayCells, propSolidAll, propSurfaces);

        // ── 상호작용 지점: 텔레포트·수리대·직접 놓은 레버/출구 문·목표/출구 자리 표식·크레인 붙기 반경 중심(집 매달림 몸 + 5차 경로 크레인 끝 도크 매달림 몸) ──
        var interact = new List<(Facility owner, string what, Vector2 point)>();
        foreach (var f in facilities)
        {
            if (f.kind == Teleporter) interact.Add((f, "", f.root + new Vector2(0f, TeleporterBodyLift)));
            else if (f.kind == Repair) interact.Add((f, "", f.root));
            else if (f.kind == Lever || f.kind == ExitGate)
                foreach (var (what, point) in InteractPoints(f.spawn.prefab, f.root, f.footprint)) interact.Add((f, what, point));
            else if (f.kind == Hook || f.kind == Anchor)
            {
                interact.Add((f, " 붙기 반경 중심", DungeonCranePaths.HangCenter(DungeonCranePaths.HomeFeet(f.spawn.prefab, f.root))));
                // 5차(도크 왕복): 경로 크레인 끝 도크도 붙기·호출 지점이다(빌더·검사기 간격 규칙과 같은 양 끝 도크). 같은 크레인의 두 중심끼리는 아래 간격 판정에서 건너뛴다.
                if (f.kind == Hook)
                {
                    var feet = DungeonCranePaths.Feet(f.spawn.prefab, f.root, f.spawn.deviceSettings);
                    if (feet.Count >= 2) interact.Add((f, " 끝 도크 붙기 반경 중심", DungeonCranePaths.HangCenter(feet[feet.Count - 1])));
                }
            }
        }
        // 자리 표식: 세워질 실물의 사용 지점(패널·문 아래 가운데, y + 0.75). 같은 방에는 목표가 하나만 서므로 목표 표식끼리는 간격을 보지 않는다.
        foreach (var slot in slots)
        {
            var area = WavePrefabVisuals.Cells(slot.root, slot.footprint);
            interact.Add((slot, "", new Vector2(area.xMin + area.width * .5f, area.yMin + .75f)));
        }

        // ── 텔레포트 받침: 점유 칸이 지형 밖이고 맨 아래 줄 바로 밑이 바닥(3차 규칙 그대로) ──
        foreach (var f in facilities.Where(f => f.kind == Teleporter))
        {
            var area = WavePrefabVisuals.Cells(f.root, f.footprint);
            Vector2Int? buried = null;
            int supported = 0;
            for (int x = area.xMin; x < area.xMax; x++)
            {
                for (int y = area.yMin; y < area.yMax && buried == null; y++)
                    if (solids.Contains(new Vector2Int(x, y))) buried = new Vector2Int(x, y);
                if (floors.Contains(new Vector2Int(x, area.yMin - 1))) supported++;
            }
            if (buried is Vector2Int sunk) Error($"{f.label}: 점유 칸 ({sunk.x}, {sunk.y})이 지형 고체 칸과 겹칩니다. 실내 빈 칸, 바닥 바로 위에 놓으세요.");
            if (supported == 0) Error($"{f.label}: 점유 칸 아래 줄(y {area.yMin - 1})에 바닥이 없습니다(공중에 뜸). 바닥 바로 위에 놓으세요.");
            else if (supported < area.width) Warn($"{f.label}: 아래 바닥이 {area.width}칸 중 {supported}칸만 받칩니다(가장자리가 허공에 걸림).");
        }

        // ── 4차: 목표 자리(4×6)·출구 자리(5×6) 뒤 벽 규칙(템플릿 규칙 v4-7·8): 점유 칸이 지형·고체 Prop과 겹치지 않고,
        //    아랫줄 바로 밑 가운데(상호작용 지점 아래)에 2칸 이상 이어진 서는 면이 있어야 한다(뒤 벽 패널이라 가장자리 받침은 보지 않음) ──
        foreach (var slot in slots)
        {
            var area = WavePrefabVisuals.Cells(slot.root, slot.footprint);
            Vector2Int? buried = null, propHit = null;
            foreach (var c in area.allPositionsWithin)
            {
                if (buried == null && solids.Contains(c)) buried = c;
                if (propHit == null && propSolidAll.Contains(c)) propHit = c;
            }
            if (buried is Vector2Int sunk) Error($"{slot.label}: 점유 칸 ({sunk.x}, {sunk.y})이 지형 고체 칸과 겹칩니다. 뒤 벽 실물({slot.footprint.x}×{slot.footprint.y})은 실내 빈 칸에 둡니다.");
            if (propHit is Vector2Int hit) Error($"{slot.label}: 점유 칸 ({hit.x}, {hit.y})이 고체 Prop {PropAt(hit)}과 겹칩니다. 실물이 Prop 속에 묻힙니다.");
            int run = CenterSupport(area, floors);
            if (run < 2) Error($"{slot.label}: 아랫줄 바로 밑(y {area.yMin - 1}) 가운데에 이어진 서는 면이 {run}칸입니다(2칸 이상). 상호작용 지점 아래에 바닥을 두세요.");
        }

        // ── 목표·출구 자리 표식은 출입구 앞에 두지 않는다 — 관에서 나온 Player가 서는 자리(빌더 InspectV4 규칙 7·8과 같은 범위·값):
        //    ① 사용 지점(아래 가운데, y + 0.75) ↔ 출입구 서는 자리(DoorStandX, 바닥 + 0.755) InteractSpacing칸 이상,
        //    ② 행이 출입구 바닥 − 1 ~ 바닥 + 높이(3)와 겹치면 실내 벽면과 ObjectiveDoorClearance칸 이상(벽면 바깥쪽 음수 간격은 다른 검사가 본다) ──
        foreach (var slot in slots)
        {
            var area = WavePrefabVisuals.Cells(slot.root, slot.footprint);
            var use = new Vector2(area.xMin + area.width * .5f, area.yMin + .75f);
            foreach (var door in doors)
            {
                float toStand = Vector2.Distance(use, new Vector2(DoorStandX(door), door.floorY + DungeonCranePaths.BodyLift));
                if (toStand < InteractSpacing)
                    Error($"{slot.label}: 사용 지점이 출입구 {door.spawnIndex + 1}번 서는 자리와 {toStand:0.#}칸입니다({InteractSpacing:0.#}칸 이상 — 관에서 나오자마자 키가 잡히지 않게).");
                bool level = area.yMin <= door.floorY + DungeonDoor.Height && area.yMax - 1 >= door.floorY - 1;
                int gap = door.side == DungeonDoorSide.Right ? door.wallFaceX - area.xMax : area.xMin - door.wallFaceX;
                if (!level || gap < 0 || gap >= ObjectiveDoorClearance) continue;
                Error($"{slot.label}: 출입구 {door.spawnIndex + 1}번 앞({gap}칸)에 있습니다. 자리 표식은 출입구 실내 벽면에서 {ObjectiveDoorClearance}칸 이상 떨어뜨리세요(관에서 나온 Player가 서는 자리).");
            }
        }

        // ── 늘어난 컨베이어·레일 칸(왼쪽 고정, 오른쪽으로 늘어남 — 격자 Tool이 점유로 보지 않는 칸). 4차: 레일은 장식으로만 남는다 ──
        foreach (var f in facilities.Where(f => f.kind == Conveyor || f.kind == Rail))
        {
            float left = f.root.x - f.footprint.x * .5f;
            int length = Mathf.RoundToInt(DeviceValue(f.spawn, f.kind == Rail ? CraneRailLengthKey : ConveyorLengthKey, f.footprint.x));
            int row = Mathf.FloorToInt(f.root.y), x0 = Mathf.RoundToInt(left);
            for (int x = x0 + f.footprint.x; x < x0 + length; x++)
                if (occupied.TryGetValue(new Vector2Int(x, row), out int other) && other != f.index)
                {
                    Error($"{f.label}: 늘어난 칸 ({x}, {row})에 맵 층 {OccupantName(other)}이 있습니다. 길이 설정으로 늘어난 칸은 격자 Tool이 점유로 보지 않으니 비워 두세요.");
                    break;
                }
            if (f.kind != Rail) continue;
            for (int x = x0; x < x0 + length; x++)
                if (solids.Contains(new Vector2Int(x, row))) { Warn($"{f.label}: 레일이 지형 칸 ({x}, {row})을 지나갑니다(벽 속으로 들어간 레일)."); break; }
        }

        // ── 생성 구역 표식(4차, 규칙 v4-9) ──
        var zones = new List<Zone>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || !DungeonSpawnZone.TryRead(spawn, mapOffset, out var rect, out int zoneKind, out _)) continue;
            zones.Add(new Zone { index = i, rect = rect, kind = zoneKind, label = $"맵 층 #{i + 1} 생성 구역({ZoneKindName(zoneKind)} {rect.width:0}×{rect.height:0})" });
            if ((((spawn.rotationSteps % 4) + 4) % 4) != 0) Warn($"맵 층 #{i + 1} 생성 구역: 회전하지 않고 놓습니다(구역은 놓은 칸에서 오른쪽·위로 넓어집니다).");
        }

        // ── 넉넉한 바닥 구간(빌더 ampleSegs): 수직 여유 ≥ ClearanceCells인 서는 칸이 한 줄에 FloorSegmentCells칸 이상 이어진 구간 — 경로 크레인 '전투 구간 통과'의 기준 ──
        var floorSegments = AmpleSegments(stands, floors, region);

        // ── 4차 크레인(규칙 v4-3): 수·경로 쓸기 상자·실내·출입구 앞 통과·전투 구간(넉넉한 바닥 구간)·고정 크레인 사슬. 돌려주는 값 = 크레인 차지 칸 + 경로 쓸기 칸(빌더 craneCells) ──
        var craneCells = CheckCranes(template, facilities, doors, region, stands, floorSegments, Hard, PropAt, solids, Error, Warn);

        // ── 자리 표식 ↔ 크레인 차지 칸·경로 쓸기 칸(빌더와 같은 칸 기준 — 생성 구역과의 겹침은 CheckZones가 본다) ──
        foreach (var slot in slots)
        {
            var area = WavePrefabVisuals.Cells(slot.root, slot.footprint);
            foreach (var c in area.allPositionsWithin)
                if (craneCells.TryGetValue(c, out string label)) { Error($"{slot.label}: 크레인 {label}의 차지 칸·경로(쓸기 상자)와 겹칩니다(예 ({c.x}, {c.y})). 자리 표식은 크레인이 Player를 옮기는 길 밖에 두세요."); break; }
        }

        // ── 생성 구역(규칙 v4-9, 빌더와 같은 정의): 적이 나오는 템플릿(IsFightTemplate)은 지상(수직 방은 공중) 구역 1개 이상,
        //    출입구 앞·목표/출구 자리·크레인 칸·함정 칸과 위험 범위·텔레포트와 겹치지 않게 ──
        var slotCells = new HashSet<Vector2Int>(slots.SelectMany(s => CellsOf(WavePrefabVisuals.Cells(s.root, s.footprint))));
        var padCells = new HashSet<Vector2Int>(facilities.Where(f => f.kind == Teleporter).SelectMany(f => CellsOf(WavePrefabVisuals.Cells(f.root, f.footprint))));
        CheckZones(template, zones, doors, craneCells, slotCells, padCells, region, interior, solids, floors, Hard, mapSpawns, mapOffset, Warn);

        // ── 수직 여유(규칙 v4-1): 빌더·정적 검사기는 < 75%를 오류로 본다. 이 검사는 Prop 윗면 근사가 달라 경고로만 알린다(판단값) ──
        var clearance = MeasureClearance(stands, floors, region, interior);
        if (clearance.stands > 0)
        {
            if (clearance.ratio < ClearanceComfort)
                Warn($"수직 여유: 서는 칸 {clearance.stands}개 중 위로 빈 칸 {ClearanceCells}칸 이상이 {clearance.roomy}개({Mathf.RoundToInt(clearance.ratio * 100f)}%)입니다"
                    + (clearance.ratio < ClearancePass ? $" — 75% 미만이라 빌더·정적 검사기 기준으로 오류입니다(예외 clearanceTarget은 note에 이유 필수)." : " — 75~80%(통과, 경고 구간).")
                    + (string.IsNullOrEmpty(clearance.narrow) ? "" : " 좁은 구간: " + clearance.narrow));
            if ((interior.height + 1) % 6 != 0 && clearance.lowCeiling > 0)
                Warn($"실내 높이 {interior.height}칸이 6k − 1(11·17·23·29·35)이 아니고, 최상층 서는 칸 {clearance.lowCeiling}개의 천장 여유가 {ClearanceCells}칸 미만입니다.");
        }

        // ── 레이저 예고선(규칙 v4-5): 예고 설정을 내는 레이저 사출기는 예고 0.5초 이상 ──
        for (int w = 0; w < (template.waves?.Length ?? 0); w++)
        {
            var spawns = template.waves[w]?.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
            for (int s = 0; s < spawns.Length; s++)
            {
                var spawn = spawns[s];
                if (spawn?.prefab == null) continue;
                bool known = DungeonCranePaths.TryParameter(spawn.prefab, LaserWarningKey, out _) || WaveDeviceSettings.Has(spawn.deviceSettings, LaserWarningKey);
                if (!known) continue;
                float warning = DeviceValue(spawn, LaserWarningKey, 0f);
                if (warning < LaserWarningMin - .001f)
                    Warn($"{(w == 0 ? "맵 층" : $"적 구성 {w}")} #{s + 1} {spawn.prefab.name}: 레이저 예고선 {warning:0.##}초 — 던전 템플릿은 {LaserWarningMin:0.#}초 이상으로 둡니다(배치 설정 {LaserWarningKey}, 기본 0.6).");
            }
        }

        // ── 상호작용 지점 간격: 시설끼리·자리 표식과 시설·크레인 붙기 반경 중심(자리 표식과는 오류, 다른 시설과는 경고 — 붙기는 보통 상호작용 키 안내가 뜨면 양보) ──
        var reported = new HashSet<(int, int)>();
        for (int i = 0; i < interact.Count; i++)
            for (int j = i + 1; j < interact.Count; j++)
            {
                var (ownerA, whatA, pointA) = interact[i];
                var (ownerB, whatB, pointB) = interact[j];
                bool craneA = ownerA.kind == Hook || ownerA.kind == Anchor, craneB = ownerB.kind == Hook || ownerB.kind == Anchor;
                if (ownerA == ownerB || ownerA.kind == Slot && ownerB.kind == Slot || craneA && craneB) continue;
                float distance = Vector2.Distance(pointA, pointB);
                var key = (Mathf.Min(ownerA.index, ownerB.index), Mathf.Max(ownerA.index, ownerB.index));
                if (distance >= InteractSpacing || !reported.Add(key)) continue;
                string message = $"상호작용 시설 {ownerA.label}{whatA}와 {ownerB.label}{whatB}의 사용 지점이 {distance:0.#}칸으로 가깝습니다({InteractSpacing:0.#}칸 이상).";
                bool slotPair = ownerA.kind == Slot || ownerA.kind == ExitSlot || ownerB.kind == Slot || ownerB.kind == ExitSlot;
                if (craneA || craneB)
                {
                    if (slotPair) Error(message + " 목표·출구 자리는 크레인 붙기 반경 중심과 떨어뜨립니다(규칙 v4-7).");
                    else Warn(message + " 그 시설의 키 안내가 뜨는 동안에는 크레인에 붙지 않습니다.");
                }
                else Error(message + " 상호작용 키가 어느 시설을 잡을지 정해지지 않습니다.");
            }
    }

    // 생성 구역 종류 이름(DungeonSpawnZoneKind 순서).
    static string ZoneKindName(int kind) => kind == (int)DungeonSpawnZoneKind.Ground ? "지상" : kind == (int)DungeonSpawnZoneKind.Air ? "공중" : "혼합";
    // 맞닿은 면을 겹침으로 보지 않도록 사각형을 조금 줄인다.
    static Rect Shrink(Rect rect) => new Rect(rect.x + BodyMargin, rect.y + BodyMargin, Mathf.Max(0f, rect.width - BodyMargin * 2), Mathf.Max(0f, rect.height - BodyMargin * 2));
    // 아랫줄 바로 밑에서 가운데 열(짝수 폭 = 가운데 2열, 홀수 폭 = 가운데 1열)을 포함해 이어진 받침 칸 수. 가운데 열이 받침이 아니면 0.
    static int CenterSupport(RectInt area, HashSet<Vector2Int> floors)
    {
        int y = area.yMin - 1;
        int left = area.xMin + (area.width - 1) / 2, right = area.xMin + area.width / 2;
        for (int x = left; x <= right; x++) if (!floors.Contains(new Vector2Int(x, y))) return 0;
        int run = right - left + 1;
        for (int x = left - 1; x >= area.xMin && floors.Contains(new Vector2Int(x, y)); x--) run++;
        for (int x = right + 1; x < area.xMax && floors.Contains(new Vector2Int(x, y)); x++) run++;
        return run;
    }
    // OneWay Prop 윗면 레이어(TransparentPlatform). 없으면 -1.
    static int OneWayLayer() => LayerMask.NameToLayer("TransparentPlatform");
    // 서는 칸: 실내 빈 칸 c와 c + 위가 플랫폼(지형·OneWay·고체/OneWay Prop)이 아니고, c 아래가 받침이다.
    static List<Vector2Int> StandCells(HashSet<Vector2Int> region, HashSet<Vector2Int> floors, HashSet<Vector2Int> solids, HashSet<Vector2Int> oneWay, HashSet<Vector2Int> props, HashSet<Vector2Int> surfaces)
    {
        var result = new List<Vector2Int>();
        if (region == null) return result;
        bool Platform(Vector2Int c) => solids.Contains(c) || oneWay.Contains(c) || props.Contains(c) || surfaces.Contains(c);
        foreach (var c in region)
        {
            if (Platform(c) || Platform(c + Vector2Int.up) || !floors.Contains(c + Vector2Int.down)) continue;
            result.Add(c);
        }
        result.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        return result;
    }
    // 수직 여유 측정: 서는 칸마다 위로 다음 플랫폼(바닥 집합 = 지형·OneWay·고체/OneWay Prop) 아랫면까지 빈 칸 수.
    private struct ClearanceResult { public int stands, roomy, lowCeiling; public float ratio; public string narrow; }
    static ClearanceResult MeasureClearance(List<Vector2Int> stands, HashSet<Vector2Int> floors, HashSet<Vector2Int> region, RectInt interior)
    {
        var result = new ClearanceResult { stands = stands.Count };
        if (stands.Count == 0) return result;
        var narrowCells = new List<Vector2Int>();
        foreach (var c in stands)
        {
            int gap = 0;
            var p = c;
            while (!floors.Contains(p) && region.Contains(p) && gap < 64) { gap++; p += Vector2Int.up; }
            if (gap >= ClearanceCells) result.roomy++;
            else
            {
                narrowCells.Add(c);
                // 위를 막은 칸이 실내 위쪽 끝 너머(천장 벽)면 최상층 천장 여유가 모자란 칸이다.
                if (p.y >= interior.yMax) result.lowCeiling++;
            }
        }
        result.ratio = result.roomy / (float)result.stands;
        // 좁은 구간: 같은 줄에서 이어진 서는 칸 묶음(최대 6개 표시).
        var segments = new List<string>();
        for (int i = 0; i < narrowCells.Count && segments.Count < 6;)
        {
            int j = i;
            while (j + 1 < narrowCells.Count && narrowCells[j + 1].y == narrowCells[i].y && narrowCells[j + 1].x == narrowCells[j].x + 1) j++;
            segments.Add($"y {narrowCells[i].y}: x {narrowCells[i].x}~{narrowCells[j].x}");
            i = j + 1;
        }
        result.narrow = string.Join(" / ", segments) + (segments.Count == 6 ? " …" : "");
        return result;
    }

    // ───────── 4차 크레인 검사(규칙 v4-3) ─────────
    // 돌려주는 값 = 크레인 칸(칸 → 크레인 이름): 크레인마다 차지 칸(집 발 x − 1 ~ x, 발 ~ 발 + 3) + 경로 크레인의 0.5칸 경로 표본마다 쓸기 칸(발 ~ 발 + 4칸, 폭 2칸).
    // 통합 4차 검토 반영: 빌더 InspectV4의 craneCells와 같은 칸 정의(목표/출구 자리·생성 구역 겹침 검사용 — 맞닿은 면은 겹침이 아니다).
    private static Dictionary<Vector2Int, string> CheckCranes(DungeonRoomTemplateSO template, List<Facility> facilities, List<DungeonDoorSocket> doors, HashSet<Vector2Int> region,
        List<Vector2Int> stands, List<(int y, int x0, int x1)> floorSegments, Func<Vector2Int, bool> hard, Func<Vector2Int, string> propAt, HashSet<Vector2Int> solids, Action<string> error, Action<string> warn)
    {
        var craneCells = new Dictionary<Vector2Int, string>();
        var cranes = facilities.Where(f => f.kind == Hook || f.kind == Anchor).ToList();
        // 통합 4차: 꼬리표 NoCrane(열거값)을 쓴 자리가 빌더·검사기 규칙(휴식 방·CombatPistonCorridor만)과 맞는지(빌더는 오류 — 편집기는 경고로 알린다).
        if (DungeonTemplateMeta.TryNoCraneTag(out var noCraneTag) && template.HasTag(noCraneTag))
        {
            if (!template.HasTag(DungeonRoomTags.Rest) && Array.IndexOf(DungeonTemplateMeta.NoCraneAllowedNames, template.name) < 0)
                warn($"NoCrane 꼬리표는 휴식 방·{string.Join("·", DungeonTemplateMeta.NoCraneAllowedNames)}만 씁니다(빌더·정적 검사기는 오류).");
            if (cranes.Count > 0) warn($"NoCrane 꼬리표인데 크레인이 {cranes.Count}개 있습니다.");
        }
        // 수: 템플릿마다 1개 이상(큰 방 오류 · 작은 방 경고), 예외 꼬리표 NoCrane.
        if (cranes.Count == 0)
        {
            if (!DungeonTemplateMeta.HasNoCraneTag(template))
            {
                string message = "크레인(경로·고정)이 없습니다. 템플릿마다 1개 이상 둡니다(템플릿 규칙 v4-3 — 예외 꼬리표 NoCrane: 휴식 방·피스톤 복도만).";
                if (template.sizeClass == DungeonRoomSize.Large) error(message); else warn(message + "(작은 방이라 경고)");
            }
            return craneCells;
        }
        // 몸이 지나는 선(붙기 직선)이 고체(지형·고체 Prop)와 겹치는지: 몸 위·가운데·아래 3줄(±0.5)을 0.2칸 간격으로 본다. OneWay는 무시한다.
        bool LineClear(Vector2 a, Vector2 b)
        {
            float length = Vector2.Distance(a, b);
            int samples = Mathf.Max(1, Mathf.CeilToInt(length / .2f));
            foreach (float lift in new[] { -.5f, 0f, .5f })
                for (int k = 0; k <= samples; k++)
                {
                    var p = Vector2.Lerp(a, b, k / (float)samples) + new Vector2(0f, lift);
                    if (hard(Vector2Int.FloorToInt(p))) return false;
                }
            return true;
        }
        var nodes = new List<(Facility crane, Vector2 center, float radius, bool ground)>();
        foreach (var f in cranes)
        {
            var settings = f.spawn.deviceSettings;
            float radius = DungeonCranePaths.Radius(f.spawn.prefab, settings);
            var feet = f.kind == Hook ? DungeonCranePaths.Feet(f.spawn.prefab, f.root, settings) : new List<Vector2> { DungeonCranePaths.HomeFeet(f.spawn.prefab, f.root) };
            // 크레인 차지 칸(빌더 RectCells(round(집 발 x) − 1, 집 발 y, 2, 4)).
            var home = DungeonCranePaths.HomeFeet(f.spawn.prefab, f.root);
            for (int x = Mathf.RoundToInt(home.x) - 1; x <= Mathf.RoundToInt(home.x); x++)
                for (int y = Mathf.RoundToInt(home.y); y < Mathf.RoundToInt(home.y) + DungeonCranePaths.Size.y; y++) craneCells.TryAdd(new Vector2Int(x, y), f.label);
            if (f.kind == Hook)
            {
                float length = DungeonCranePaths.Length(feet);
                if (length < .5f) { error($"{f.label}: 경로 크레인의 경로 길이가 {length:0.#}칸입니다. 점 1을 집과 다른 곳에 두세요(배치 설정 {DungeonCranePaths.CountKey}·{DungeonCranePaths.PointXKey(1)}·{DungeonCranePaths.PointYKey(1)})."); }
                // 쓸기 상자: 0.5칸 경로 표본(빌더 PolylineSamples)마다 쓸기 칸(빌더 SweepCells — 발 ~ 발 + 4칸, 폭 2칸)이 고체 지형·고체 Prop과 겹치면 안 된다(OneWay 통과 허용).
                // 통합 4차 검토 반영: 표본 간격·칸 경계를 빌더와 같게 맞췄다(전에는 0.25칸 표본 + 0.08 여유라 대각선 구간에서 판정이 갈릴 수 있었다).
                Vector2Int? blocked = null; int blockedSegment = -1;
                for (int i = 0; i + 1 < feet.Count && blocked == null; i++)
                {
                    int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(feet[i], feet[i + 1]) / CranePathSampleStep));
                    for (int k = i == 0 ? 0 : 1; k <= samples && blocked == null; k++)
                        foreach (var cell in SweepCells(Vector2.Lerp(feet[i], feet[i + 1], k / (float)samples)))
                            if (hard(cell)) { blocked = cell; blockedSegment = i; break; }
                }
                if (blocked is Vector2Int c)
                    error($"{f.label}: 경로 크레인 구간 {blockedSegment}→{blockedSegment + 1}의 쓸기 상자(발 ~ 발 + {DungeonCranePaths.SweepHeight:0}칸, 폭 {DungeonCranePaths.SweepWidth:0}칸)가 "
                        + (solids.Contains(c) ? "지형 고체 칸" : $"고체 Prop {propAt(c)}의 칸") + $" ({c.x}, {c.y})과 겹칩니다. 크레인은 지형을 통과해 옮기므로 몸·케이지가 벽 속을 지나갑니다(템플릿 규칙 v4-3).");
                // 같은 자리 점(빌더 오류와 같음 — 길이 0 구간).
                for (int i = 1; i < feet.Count; i++)
                    if (Vector2.Distance(feet[i], feet[i - 1]) < .001f) { error($"{f.label}: 경로 점 {i}가 앞 점과 같은 자리입니다(빌더와 같은 오류)."); break; }
                // 집·끝점·꺾이는 점에 매달린 몸(빌더 BodyCells — 발 기준 폭 1.5 · 높이 1.5칸)이 모두 실내 빈 칸 안이어야 한다(빌더와 같은 범위 — 집 점도 본다).
                if (region != null)
                    for (int i = 0; i < feet.Count; i++)
                    {
                        if (BodyCells(feet[i]).All(region.Contains)) continue;
                        error($"{f.label}: 경로 점 {i}의 발 ({feet[i].x:0.#}, {feet[i].y:0.#})에 매달린 몸(1.5칸)이 실내 밖입니다. 끝점·꺾이는 점은 실내 안에 둡니다.");
                        break;
                    }
                // 경로 표본: 빌더 PolylineSamples와 같은 0.5칸 간격(첫 점 + 구간마다 끝 포함). 출입구 통과·바닥 구간 통과·쓸기 칸을 모두 이 표본으로 센다.
                var samplesOnPath = PathSamples(feet);
                foreach (var p in samplesOnPath)
                    foreach (var cell in SweepCells(p)) craneCells.TryAdd(cell, f.label);
                // 출입구 앞 통과(빌더와 같은 판정): 표본 발이 어느 출입구의 서는 자리 x ± CraneDoorReach(1.5), 바닥 ~ 바닥 + CraneDoorRise 안을 지나야 한다.
                var passed = doors.Where(d => samplesOnPath.Any(p => Mathf.Abs(p.x - DoorStandX(d)) <= CraneDoorReach && p.y >= d.floorY && p.y <= d.floorY + CraneDoorRise)).ToList();
                if (doors.Count > 0 && passed.Count == 0)
                    error($"{f.label}: 경로 크레인이 어느 출입구 앞(서는 자리 x ± {CraneDoorReach:0.#}칸, 바닥 위 {CraneDoorRise}칸 안)도 지나지 않습니다. 출입구에서 바로 탈 수 있게 경로가 출입구 앞을 한 번 이상 지나게 하세요(템플릿 규칙 v4-3).");
                // 전투 구간 통과(빌더와 같은 뜻 — 생성 구역이 아니라 넉넉한 바닥 구간): 표본 발이 구간 x0 − 0.5 ~ x1 + 1.5, 구간 바닥 위 0 ~ ClearanceCells칸을 지나면 그 구간을 지난 것이다.
                // 생성 구역 ↔ 크레인 경로 겹침 금지(CheckZones)와 서로 막지 않는다(구역은 바닥 구간 안에서 경로 쓸기 칸만 피하면 된다).
                int crossed = floorSegments.Count(g => samplesOnPath.Any(p => p.x >= g.x0 - .5f && p.x <= g.x1 + 1.5f && p.y - g.y >= 0f && p.y - g.y <= ClearanceCells));
                if (crossed < 2) warn($"{f.label}: 경로가 지나는 넉넉한 바닥 구간이 {crossed}곳입니다. 전투 구간 사이를 2곳 이상 지나는 경로를 권장합니다(템플릿 규칙 v4-3 — 빌더와 같은 기준).");
                nodes.Add((f, DungeonCranePaths.HangCenter(feet[0]), radius, false));
                if (feet.Count > 1) nodes.Add((f, DungeonCranePaths.HangCenter(feet[feet.Count - 1]), radius, false));
            }
            else nodes.Add((f, DungeonCranePaths.HangCenter(feet[0]), radius, false));
        }

        // ── 붙기 도달(규칙 v4-3): 서는 면 몸 중심(서는 칸 + (0.5, 0.755))에서 반경 안 · 붙기 직선이 고체와 안 겹침.
        //    5차(DESIGN_V5 §1 도크 왕복): 경로 크레인은 집 도크·끝 도크 둘 다 붙기 대상(빈 도크는 호출로 크레인이 온다 — 빌더·검사기 BFS 양방향 간선과 같다).
        //    끝 도크는 반경 안·직선 열린 서는 면이 없으면 오류(빌더·검사기 오류와 같다 — 편집기는 서는 면 전체 기준). ──
        var standBodies = stands.Select(s => new Vector2(s.x + .5f, s.y + DungeonCranePaths.BodyLift)).ToList();
        var groundReach = new bool[nodes.Count];
        var inRadius = new bool[nodes.Count];
        for (int n = 0; n < nodes.Count; n++)
        {
            var node = nodes[n];
            foreach (var body in standBodies)
            {
                if (Vector2.Distance(body, node.center) > node.radius) continue;
                inRadius[n] = true;
                if (LineClear(body, node.center)) { groundReach[n] = true; break; }
            }
            bool endDock = node.crane.kind == Hook && n > 0 && nodes[n - 1].crane == node.crane;
            if (endDock && !groundReach[n])
                error($"{node.crane.label}: 경로 크레인 끝 도크의 붙기 반경({node.radius:0.#}칸) 안에 붙기 직선이 열린 서는 면이 없습니다. 끝 도크에서도 탈 수 있게 도크 0~2칸 안에 서는 자리를 두세요(템플릿 규칙 v4-3 — 5차 도크 왕복).");
        }
        // ── 사슬: 다른 크레인 노드와 반경 × 0.9 안 + 붙기 직선 비어 있음 = 이어짐. 같은 경로 크레인의 집·끝은 타고 이어진다 ──
        var parent = Enumerable.Range(0, nodes.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        void Union(int a, int b) { parent[Find(a)] = Find(b); }
        for (int a = 0; a < nodes.Count; a++)
            for (int b = a + 1; b < nodes.Count; b++)
            {
                if (nodes[a].crane == nodes[b].crane) { Union(a, b); continue; }
                float distance = Vector2.Distance(nodes[a].center, nodes[b].center);
                float limit = Mathf.Min(nodes[a].radius, nodes[b].radius);
                if (distance > limit || !LineClear(nodes[a].center, nodes[b].center)) continue;
                if (distance <= limit * CraneChainRatio) Union(a, b);
                else if (nodes[a].crane.kind == Anchor || nodes[b].crane.kind == Anchor)
                    warn($"크레인 {nodes[a].crane.label}과 {nodes[b].crane.label}의 사슬 간격 {distance:0.#}칸이 붙기 반경 × {CraneChainRatio:0.#}({limit * CraneChainRatio:0.#}칸)를 넘습니다. 연쇄 붙기가 아슬아슬합니다(템플릿 규칙 v4-3).");
            }
        var reachedRoots = new HashSet<int>(Enumerable.Range(0, nodes.Count).Where(n => groundReach[n]).Select(Find));
        var reportedCranes = new HashSet<Facility>();
        for (int n = 0; n < nodes.Count; n++)
        {
            if (reachedRoots.Contains(Find(n)) || !reportedCranes.Add(nodes[n].crane)) continue;
            var crane = nodes[n].crane;
            string kind = crane.kind == Hook ? "경로 크레인" : "고정 크레인";
            warn(inRadius[n]
                ? $"{crane.label}: {kind}의 붙기 반경 안에 서는 면은 있지만 붙기 직선이 모두 지형·고체 Prop에 막힙니다(사슬로도 이어지지 않음 — 템플릿 규칙 v4-3)."
                : $"{crane.label}: {kind}이 서는 면에서 붙기 반경({nodes[n].radius:0.#}칸) 안에 없고, 닿는 다른 크레인과 반경 × {CraneChainRatio:0.#} 안으로 이어지지도 않습니다(템플릿 규칙 v4-3).");
        }
        return craneCells;
    }

    // ───────── 빌더와 같은 칸 계산(통합 4차 검토 반영 — DungeonTemplateBuilder.InspectV4·check_templates.py와 같은 정의, 빌더가 정본) ─────────
    /// <summary>사각형 안의 칸(RectInt.allPositionsWithin을 LINQ·HashSet에 넘기려고 감싼다).</summary>
    static IEnumerable<Vector2Int> CellsOf(RectInt area)
    {
        foreach (var c in area.allPositionsWithin) yield return c;
    }
    /// <summary>경로 표본(빌더 PolylineSamples): 첫 점 + 구간마다 ceil(길이 / 0.5)등분한 점(끝 포함).</summary>
    public static List<Vector2> PathSamples(List<Vector2> corners)
    {
        var list = new List<Vector2>();
        if (corners == null || corners.Count == 0) return list;
        list.Add(corners[0]);
        for (int k = 1; k < corners.Count; k++)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(corners[k - 1], corners[k]) / CranePathSampleStep));
            for (int i = 1; i <= n; i++) list.Add(Vector2.Lerp(corners[k - 1], corners[k], i / (float)n));
        }
        return list;
    }
    /// <summary>매달린 몸 칸(빌더 BodyCells): 발 기준 x ± 0.75, 발 ~ 발 + 1.5칸을 덮는 칸(맞닿은 면 0.02 제외).</summary>
    public static IEnumerable<Vector2Int> BodyCells(Vector2 feet)
    {
        const float e = .02f, size = 1.5f;
        for (int x = Mathf.FloorToInt(feet.x - size * .5f + e); x <= Mathf.FloorToInt(feet.x + size * .5f - e); x++)
            for (int y = Mathf.FloorToInt(feet.y + e); y <= Mathf.FloorToInt(feet.y + size - e); y++) yield return new Vector2Int(x, y);
    }
    /// <summary>쓸기 칸(빌더 SweepCells): 발 기준 x − 1 ~ x + 1, 발 ~ 발 + 4칸을 덮는 칸(맞닿은 면 0.02 제외).</summary>
    public static IEnumerable<Vector2Int> SweepCells(Vector2 feet)
    {
        const float e = .02f;
        for (int x = Mathf.FloorToInt(feet.x - DungeonCranePaths.SweepWidth * .5f + e); x <= Mathf.FloorToInt(feet.x + DungeonCranePaths.SweepWidth * .5f - e); x++)
            for (int y = Mathf.FloorToInt(feet.y + e); y <= Mathf.FloorToInt(feet.y + DungeonCranePaths.SweepHeight - e); y++) yield return new Vector2Int(x, y);
    }
    /// <summary>
    /// 넉넉한 바닥 구간(빌더 ampleSegs): 서는 칸 중 위로 다음 플랫폼까지 빈 칸이 ClearanceCells 이상인 칸을 같은 줄에서 이어 묶고, FloorSegmentCells칸 이상인 구간만 남긴다.
    /// </summary>
    static List<(int y, int x0, int x1)> AmpleSegments(List<Vector2Int> stands, HashSet<Vector2Int> floors, HashSet<Vector2Int> region)
    {
        var segments = new List<(int y, int x0, int x1)>();
        if (region == null) return segments;
        foreach (var c in stands.OrderBy(c => c.y).ThenBy(c => c.x))
        {
            int gap = 0;
            var p = c;
            while (!floors.Contains(p) && region.Contains(p) && gap < 64) { gap++; p += Vector2Int.up; }
            if (gap < ClearanceCells) continue;
            if (segments.Count > 0 && segments[segments.Count - 1].y == c.y && segments[segments.Count - 1].x1 == c.x - 1) segments[segments.Count - 1] = (c.y, segments[segments.Count - 1].x0, c.x);
            else segments.Add((c.y, c.x, c.x));
        }
        segments.RemoveAll(g => g.x1 - g.x0 + 1 < FloorSegmentCells);
        return segments;
    }

    // 함정(빌더 PropRules의 Trap 종류 — 이름이 같은 Prefab). 점프대(JumpPadCell)·컨베이어 같은 다른 환경 장치는 함정이 아니다.
    public static readonly string[] TrapPrefabNames =
    {
        "DungeonPropSteamVent", "DungeonPropShredderIntake", "DungeonPropScrapChute", "DungeonPropLaserWelderArm", "DungeonPropCableJunction",
        "RotatingSawCell", "RotatingSawLargeCell", "LaserEmitterCell", "PistonTrapCell", "SpikeTrapCell",
    };
    public static bool IsTrap(GameObject prefab) => prefab != null && Array.IndexOf(TrapPrefabNames, prefab.name) >= 0;
    // 톱날 (톱날 반지름, 기본 팔 길이) — 빌더 SawBlades와 같은 값.
    static readonly Dictionary<string, (float blade, float length)> SawBlades = new() { { "RotatingSawCell", (.4f, 3f) }, { "RotatingSawLargeCell", (.8f, 3f) } };
    // 저장된 배치 설정 값, 없으면 빌더 기본값(빌더 SettingOr와 같다 — Prefab 항목 기본값 대신 빌더 기본값을 써서 같은 결과를 낸다).
    static float StoredOr(WaveSpawnInfo spawn, string key, float fallback)
    {
        if (spawn?.deviceSettings != null)
            foreach (var setting in spawn.deviceSettings)
                if (setting.key == key) return setting.value;
        return fallback;
    }
    /// <summary>함정 차지 칸(빌더 PropCells): 회전한 점유 칸, 분쇄 투입구는 폭 설정(shredderWidth, 회전 0·2)만큼 오른쪽으로 늘어난다.</summary>
    static RectInt TrapArea(WaveSpawnInfo spawn, Vector2 offset)
    {
        var area = WavePrefabVisuals.Cells(spawn.spawnPosition + offset, WavePrefabVisuals.Footprint(spawn.prefab, spawn.rotationSteps));
        if (spawn.prefab.name == "DungeonPropShredderIntake" && (((spawn.rotationSteps % 4) + 4) % 4) % 2 == 0)
        {
            int width = Mathf.RoundToInt(StoredOr(spawn, "shredderWidth", area.width));
            if (width >= 1) area.width = width;
        }
        return area;
    }
    /// <summary>
    /// 함정이 실제로 해치는 칸(빌더 DangerCells와 같은 계산): 차지 칸 + 톱날 원·레이저 빔·피스톤 뻗음·증기 기둥·용접 빔·슈트 낙하·방전 원·투입구 윗줄.
    /// blocked = 고체(지형·고체 Prop), interior = 실내 칸(빈 칸 + 실내의 고체), solids = 지형 고체(벽 옆 설치면 판정).
    /// </summary>
    static HashSet<Vector2Int> TrapDanger(WaveSpawnInfo spawn, RectInt area, Func<Vector2Int, bool> blocked, Func<Vector2Int, bool> interior, HashSet<Vector2Int> solids)
    {
        var cells = new HashSet<Vector2Int>(CellsOf(area));
        string name = spawn.prefab.name;
        var at = new Vector2Int(area.xMin, area.yMin);
        int rot = ((spawn.rotationSteps % 4) + 4) % 4;
        bool Column(int x)
        {
            for (int y = area.yMin; y < area.yMax; y++) if (!solids.Contains(new Vector2Int(x, y))) return false;
            return true;
        }
        char Mount() => Column(area.xMin - 1) ? 'L' : Column(area.xMax) ? 'R' : '\0';
        Vector2Int Direction() => rot == 0 ? Vector2Int.up : rot == 2 ? Vector2Int.down : Mount() == 'L' ? Vector2Int.right : Vector2Int.left;
        void Ray(Vector2Int from, Vector2Int d)
        {
            var c = from;
            for (int i = 0; i < 50; i++)
            {
                c += d;
                if (blocked(c) || !interior(c)) return;
                cells.Add(c);
            }
        }
        void Circle(Vector2 center, float radius)
        {
            for (int x = Mathf.FloorToInt(center.x - radius); x <= Mathf.FloorToInt(center.x + radius); x++)
                for (int y = Mathf.FloorToInt(center.y - radius); y <= Mathf.FloorToInt(center.y + radius); y++)
                    if (Vector2.Distance(new Vector2(x + .5f, y + .5f), center) <= radius + .5f) cells.Add(new Vector2Int(x, y));
        }
        if (SawBlades.TryGetValue(name, out var saw)) Circle(new Vector2(at.x + .5f, at.y + .5f), StoredOr(spawn, "length", saw.length) + saw.blade);
        else if (name == "LaserEmitterCell") Ray(at, Direction());
        else if (name == "PistonTrapCell")
        {
            var d = Direction();
            int stroke = Mathf.RoundToInt(StoredOr(spawn, "stroke", 2f));
            for (int k = 1; k <= stroke; k++) cells.Add(at + d * k);
        }
        else if (name == "DungeonPropSteamVent")
        {
            var d = Direction();
            for (int k = 1; k <= 2; k++) cells.Add(at + d * k);
        }
        else if (name == "DungeonPropLaserWelderArm")
        {
            char side = Mount();
            var d = side == 'L' ? Vector2Int.right : Vector2Int.left;
            int sx = side == 'L' ? area.xMax - 1 : area.xMin;
            for (int y = area.yMin; y <= area.yMin + 1; y++) Ray(new Vector2Int(sx, y), d);
        }
        else if (name == "DungeonPropScrapChute")
            for (int x = area.xMin; x < area.xMax; x++) Ray(new Vector2Int(x, area.yMin), Vector2Int.down);
        else if (name == "DungeonPropCableJunction") Circle(new Vector2(at.x + .5f, at.y + .5f), StoredOr(spawn, "sparkRadius", 1.5f));
        else if (name == "DungeonPropShredderIntake")
            for (int x = area.xMin; x < area.xMax; x++) cells.Add(new Vector2Int(x, area.yMax));
        cells.RemoveWhere(c => !interior(c));
        return cells;
    }

    // ───────── 4차 생성 구역 검사(규칙 v4-9) ─────────
    // 통합 4차 검토 반영: 빌더 InspectV4 '생성 구역'과 같은 정의로 본다.
    //   · 대상 = 적이 나오는 템플릿(DungeonTemplateMeta.IsFightTemplate — 손 배치 적 구성 수와 무관).
    //   · 지상 구역 = 지상·혼합 구역 안에 서는 칸(실내 빈 칸 · 아래가 받침)이 있음, 공중 구역 = 공중·혼합 구역 안에 바닥 위 2칸 넘게 뜬 빈 칸이 있음.
    //   · 겹침 금지 = 출입구 앞 공간 · 목표/출구 자리 칸 · 크레인 차지/경로 쓸기 칸 · 함정 칸과 위험 범위(빌더 Trap 종류만 — 점프대는 함정이 아님) · 텔레포트 칸.
    // 빌더·정적 검사기는 이 겹침과 '지상(수직 방은 공중) 구역 없음'을 오류로 본다. 여기서는 고체 Prop 칸을 Prefab 충돌체로 세어(빌더는 Prop 규칙표) 한두 칸 해석이 갈릴 수 있어
    // 경고로 알리고 문구에 '빌더 오류'를 적는다(빌더가 이 검사의 오류를 빌드 실패로 가져다 쓰므로 해석이 갈릴 수 있는 규칙은 경고로 둔다 — editor4 원칙).
    private static void CheckZones(DungeonRoomTemplateSO template, List<Zone> zones, List<DungeonDoorSocket> doors, Dictionary<Vector2Int, string> craneCells,
        HashSet<Vector2Int> slotCells, HashSet<Vector2Int> padCells, HashSet<Vector2Int> region, RectInt interior, HashSet<Vector2Int> solids, HashSet<Vector2Int> floors,
        Func<Vector2Int, bool> hard, WaveSpawnInfo[] mapSpawns, Vector2 mapOffset, Action<string> warn)
    {
        bool fight = DungeonTemplateMeta.IsFightTemplate(template);
        if (template.role == DungeonRoomRole.Boss && zones.Count > 0) warn($"보스 방의 생성 구역 {zones.Count}개는 쓰이지 않습니다(보스 방은 손 배치 보스 구성을 씁니다).");
        // 출입구 앞 공간(빌더 DoorZone: 실내 쪽 DoorFrontDepth칸 · 바닥 ~ 바닥 + 출입구 높이).
        var doorZone = new Dictionary<Vector2Int, int>();
        foreach (var d in doors)
        {
            int first = d.side == DungeonDoorSide.Right ? d.wallFaceX - DoorFrontDepth : d.wallFaceX;
            for (int x = first; x < first + DoorFrontDepth; x++)
                for (int y = d.floorY; y < d.floorY + DungeonDoor.Height; y++) doorZone.TryAdd(new Vector2Int(x, y), d.spawnIndex + 1);
        }
        // 함정 칸 + 위험 범위(빌더 DangerCells). 실내 = 실내 빈 칸 + 실내 외접 사각형 안의 고체(빌더 plan.Interior와 같은 뜻).
        bool InteriorCell(Vector2Int c) => region != null ? region.Contains(c) || (hard(c) && interior.Contains(c)) : interior.Contains(c);
        var traps = new List<(string name, HashSet<Vector2Int> cells)>();
        for (int i = 0; i < mapSpawns.Length; i++)
        {
            var spawn = mapSpawns[i];
            if (spawn?.prefab == null || !IsTrap(spawn.prefab)) continue;
            traps.Add(($"#{i + 1} {spawn.prefab.name}", TrapDanger(spawn, TrapArea(spawn, mapOffset), hard, InteriorCell, solids)));
        }
        bool groundOk = false, airOk = false;
        foreach (var zone in zones)
        {
            int x0 = Mathf.RoundToInt(zone.rect.xMin), y0 = Mathf.RoundToInt(zone.rect.yMin);
            int w = Mathf.Max(1, Mathf.RoundToInt(zone.rect.width)), h = Mathf.Max(1, Mathf.RoundToInt(zone.rect.height));
            var cells = new List<Vector2Int>();
            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++) cells.Add(new Vector2Int(x, y));
            // 겹침: 첫 칸 하나만 알린다(빌더와 같은 칸 집합 — 무엇과 겹쳤는지 이름을 붙인다).
            string hit = null;
            Vector2Int at = default;
            foreach (var c in cells)
            {
                if (doorZone.TryGetValue(c, out int doorNumber)) hit = $"출입구 {doorNumber}번 앞 공간";
                else if (slotCells.Contains(c)) hit = "목표·출구 자리 표식 칸";
                else if (craneCells.TryGetValue(c, out string crane)) hit = $"크레인 {crane}의 차지 칸·경로(쓸기 상자)";
                else
                {
                    foreach (var (name, danger) in traps)
                        if (danger.Contains(c)) { hit = $"함정 {name}의 칸·위험 범위"; break; }
                    if (hit == null && padCells.Contains(c)) hit = "텔레포트 시설 칸";
                }
                if (hit != null) { at = c; break; }
            }
            if (hit != null)
                warn($"{zone.label}: {hit}과 겹칩니다(예 ({at.x}, {at.y}) — 실행 때 그 칸은 후보에서 빠집니다. 빌더·정적 검사기는 오류, 템플릿 규칙 v4-9).");
            // 지상·공중 구역 판정(빌더 standsIn·airs). 실내를 찾지 못했으면 종류만 본다.
            bool standsIn = region == null ? zone.kind != (int)DungeonSpawnZoneKind.Air
                : cells.Any(c => region.Contains(c) && !hard(c) && floors.Contains(c + Vector2Int.down));
            bool airs = region == null ? zone.kind != (int)DungeonSpawnZoneKind.Ground
                : cells.Any(c => region.Contains(c) && !hard(c) && !floors.Contains(c + Vector2Int.down) && !floors.Contains(c + Vector2Int.down * 2));
            if (zone.kind != (int)DungeonSpawnZoneKind.Air && standsIn) groundOk = true;
            if (zone.kind != (int)DungeonSpawnZoneKind.Ground && airs) airOk = true;
            if (zone.kind == (int)DungeonSpawnZoneKind.Ground && !standsIn) warn($"{zone.label}: 지상 구역인데 안에 서는 칸(아래가 받침인 빈 칸)이 없습니다.");
            if (zone.kind == (int)DungeonSpawnZoneKind.Air && !airs) warn($"{zone.label}: 공중 구역인데 바닥 위 2칸 넘게 뜬 빈 칸이 없습니다.");
            if (region != null)
            {
                bool inside = false;
                for (int x = Mathf.FloorToInt(zone.rect.xMin); x < Mathf.CeilToInt(zone.rect.xMax) && !inside; x++)
                    for (int y = Mathf.FloorToInt(zone.rect.yMin); y < Mathf.CeilToInt(zone.rect.yMax) && !inside; y++)
                        inside = region.Contains(new Vector2Int(x, y));
                if (!inside) warn($"{zone.label}: 실내 빈 칸과 겹치지 않습니다(벽 속 구역은 쓰이지 않습니다).");
            }
        }
        if (!fight) return;
        bool vertical = template.orientation == DungeonRoomOrientation.Vertical;
        if (zones.Count == 0)
            warn($"전투 템플릿에 생성 구역 표식이 없습니다. 동적 구성은 빈 칸 전체에서 고릅니다({(vertical ? "공중" : "지상")} 구역 1개 이상 — 빌더·정적 검사기는 오류, 템플릿 규칙 v4-9).");
        else if (vertical)
        {
            if (!airOk) warn("수직 방에 공중 적을 받는 생성 구역(공중·혼합, 바닥 위 2칸 넘게 뜬 빈 칸)이 없습니다(빌더·정적 검사기는 오류 — 템플릿 규칙 v4-9).");
            if (!groundOk) warn("수직 방에 지상 적을 받는 생성 구역(지상·혼합, 안에 서는 칸)이 없습니다(템플릿 규칙 v4-9).");
        }
        else if (!groundOk) warn("지상 적을 받는 생성 구역(지상·혼합, 안에 서는 칸)이 없습니다(빌더·정적 검사기는 오류 — 템플릿 규칙 v4-9).");
    }

    // 배치별 설정 값: Prefab의 IWaveDeviceSettings 항목(기본값·범위)으로 읽고, 항목을 못 찾으면 저장된 값, 그것도 없으면 fallback.
    private static float DeviceValue(WaveSpawnInfo spawn, string key, float fallback)
    {
        try
        {
            var parameters = spawn?.prefab != null ? spawn.prefab.GetComponent<IWaveDeviceSettings>()?.Parameters : null;
            if (parameters != null)
                foreach (var parameter in parameters)
                    if (parameter.Key == key) return WaveDeviceSettings.Value(spawn.deviceSettings, parameter);
        }
        catch (Exception) { /* Prefab 쪽 항목 계산이 Edit Mode에서 실패해도 저장값·기본값으로 검사한다. */ }
        if (spawn?.deviceSettings != null)
            foreach (var setting in spawn.deviceSettings)
                if (setting.key == key) return setting.value;
        return fallback;
    }

    // Prop의 고체 칸: Platform 레이어의 트리거 아닌 BoxCollider2D를 Prefab 루트 기준으로 모아 회전·배치한 뒤 맞닿은 면(BodyMargin)을 빼고 센다.
    // 4차: layer를 주면 그 레이어(예: OneWay Prop 윗면 TransparentPlatform)의 칸을 센다. 음수면 아무것도 넣지 않는다.
    private static void SolidCells(GameObject prefab, Vector2 root, int rotationSteps, HashSet<Vector2Int> into, int layer = int.MinValue)
    {
        if (layer == int.MinValue)
        {
            layer = LayerMask.NameToLayer("Platform");
            if (layer < 0) layer = 6;
        }
        if (layer < 0) return;
        var toRoot = prefab.transform.worldToLocalMatrix;
        var rotation = Quaternion.Euler(0f, 0f, (((rotationSteps % 4) + 4) % 4) * 90f);
        foreach (var collider in prefab.GetComponentsInChildren<BoxCollider2D>(true))
        {
            if (collider.isTrigger || collider.gameObject.layer != layer) continue;
            var matrix = toRoot * collider.transform.localToWorldMatrix;
            Vector2 half = collider.size * .5f, min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var corner in new[] { new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y), new Vector2(-half.x, half.y), new Vector2(half.x, half.y) })
            {
                Vector2 point = rotation * matrix.MultiplyPoint3x4(collider.offset + corner);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            for (int y = Mathf.FloorToInt(root.y + min.y + BodyMargin); y <= Mathf.FloorToInt(root.y + max.y - BodyMargin); y++)
                for (int x = Mathf.FloorToInt(root.x + min.x + BodyMargin); x <= Mathf.FloorToInt(root.x + max.x - BodyMargin); x++)
                    into.Add(new Vector2Int(x, y));
        }
    }

    // 적 몸이 덮는 칸(셀 좌표). origin = 격자 Tool이 놓은 원점(spawnPosition + spawnOffset, WavePrefabVisuals.Placement 결과),
    // 모양 = WavePrefabVisuals.LocalBounds(collision: true)(Collider 범위, 없으면 Renderer 범위). 맞닿은 면은 BodyMargin만큼 빼고 센다.
    // limit 밖 칸은 목록에 넣지 않고, 몸이 limit을 벗어나면 true를 돌려준다.
    private static bool EnemyBodyCells(GameObject prefab, Vector2 origin, RectInt limit, List<Vector2Int> cells, out Bounds shape)
    {
        cells.Clear();
        shape = WavePrefabVisuals.LocalBounds(prefab, true);
        Vector2 min = origin + (Vector2)shape.min, max = origin + (Vector2)shape.max;
        int x0 = Mathf.FloorToInt(min.x + BodyMargin), x1 = Mathf.FloorToInt(max.x - BodyMargin);
        int y0 = Mathf.FloorToInt(min.y + BodyMargin), y1 = Mathf.FloorToInt(max.y - BodyMargin);
        // 여유보다 작은 몸은 중심 칸 하나로 본다.
        if (x1 < x0) x0 = x1 = Mathf.FloorToInt((min.x + max.x) * .5f);
        if (y1 < y0) y0 = y1 = Mathf.FloorToInt((min.y + max.y) * .5f);
        bool beyond = x0 < limit.xMin || y0 < limit.yMin || x1 >= limit.xMax || y1 >= limit.yMax;
        for (int y = Mathf.Max(y0, limit.yMin); y <= Mathf.Min(y1, limit.yMax - 1); y++)
            for (int x = Mathf.Max(x0, limit.xMin); x <= Mathf.Min(x1, limit.xMax - 1); x++) cells.Add(new Vector2Int(x, y));
        return beyond;
    }

    // 시작 좌표(서 있는 몸 위치일 수 있어 한 칸 위도 본다)에서 닫힌 실내를 찾는다.
    private static bool TryRoom(HashSet<Vector2Int> solids, Vector2 start, HashSet<Vector2Int> region)
    {
        region.Clear();
        if (WaveCellRoomSpace.TryGetBounds(solids, start, out _, region)) return true;
        region.Clear();
        if (WaveCellRoomSpace.TryGetBounds(solids, start + Vector2.up, out _, region)) return true;
        region.Clear();
        return false;
    }

    // ───────── 4차: 빈 칸 면적·수직 여유 요약(미리보기·템플릿 패널용, 템플릿 변경 횟수로 캐시) ─────────
    private sealed class Measure
    {
        public int dirty;
        public bool ok;
        public int empty;
        public RectInt interior;
        public string clearance;
    }
    private static readonly Dictionary<int, Measure> measures = new();
    private static Measure MeasureOf(DungeonRoomTemplateSO template)
    {
        if (template == null) return null;
        int id = template.GetInstanceID(), dirty = EditorUtility.GetDirtyCount(template);
        if (measures.TryGetValue(id, out var cached) && cached.dirty == dirty) return cached;
        var result = new Measure { dirty = dirty };
        measures[id] = result;
        var cells = template.MapCells ?? Array.Empty<WaveMapCell>();
        var solids = new HashSet<Vector2Int>(cells.Where(c => c.kind == WaveCellKind.Platform).Select(c => c.position));
        var region = new HashSet<Vector2Int>();
        if (solids.Count == 0 || template.MapLayer == null || !TryRoom(solids, WaveRoomState.PlayerStartAt(template, 0), region)) return result;
        var mapSpawns = template.MapLayer.spawnObjects ?? Array.Empty<WaveSpawnInfo>();
        var mapOffset = WaveRoomState.LayoutAt(template, 0).spawnOffset;
        var props = new HashSet<Vector2Int>();
        var surfaces = new HashSet<Vector2Int>();
        foreach (var spawn in mapSpawns)
        {
            if (spawn?.prefab == null || NotSolidAtStart(WavePrefabVisuals.DungeonFacilityName(spawn.prefab))) continue;
            SolidCells(spawn.prefab, spawn.spawnPosition + mapOffset, spawn.rotationSteps, props);
            SolidCells(spawn.prefab, spawn.spawnPosition + mapOffset, spawn.rotationSteps, surfaces, OneWayLayer());
        }
        int xMin = region.Min(c => c.x), yMin = region.Min(c => c.y), xMax = region.Max(c => c.x), yMax = region.Max(c => c.y);
        result.ok = true;
        result.interior = new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
        // 빈 칸 수 A(DESIGN_V4 §2.8) = 실내 − 고체 지형 − 고체 Prop. 실내(region)에는 고체 지형이 없다.
        result.empty = region.Count(c => !props.Contains(c));
        var oneWay = new HashSet<Vector2Int>(cells.Where(c => c.kind == WaveCellKind.OneWayPlatform).Select(c => c.position));
        var floors = new HashSet<Vector2Int>(solids); floors.UnionWith(oneWay); floors.UnionWith(props); floors.UnionWith(surfaces);
        var stands = StandCells(region, floors, solids, oneWay, props, surfaces);
        var clearance = MeasureClearance(stands, floors, region, result.interior);
        if (clearance.stands > 0)
            result.clearance = $"수직 여유: 서는 칸 {clearance.stands}개 중 빈 칸 {ClearanceCells}칸 이상 {clearance.roomy}개({Mathf.RoundToInt(clearance.ratio * 100f)}%)"
                + (clearance.ratio >= ClearanceComfort ? " · 통과" : clearance.ratio >= ClearancePass ? " · 통과(경고 구간 75~80%)" : " · 75% 미만(빌더 오류 기준)")
                + $" · 빈 칸 면적 {result.empty} · 실내 {result.interior.width}×{result.interior.height}" + ((result.interior.height + 1) % 6 == 0 ? "(6k − 1)" : "")
                + (string.IsNullOrEmpty(clearance.narrow) ? "" : " · 좁은 구간 " + clearance.narrow);
        return result;
    }
    /// <summary>
    /// 빈 칸 수 A(실내 − 고체 지형 − 고체 Prop, DESIGN_V4 §2.8 동적 구성의 면적)와 실내 외접 사각형. 시작 좌표에서 닫힌 실내를 찾지 못하면 false.
    /// 미리보기의 '예상 적 수'가 쓴다(실행 쪽 DungeonEncounterComposer의 emptyCells와 같은 뜻 — 셀·Prop 경계 계산 방식은 실행 쪽이 정본).
    /// </summary>
    public static bool TryMeasure(DungeonRoomTemplateSO template, out int emptyCells, out RectInt interior)
    {
        var measure = MeasureOf(template);
        emptyCells = measure?.empty ?? 0;
        interior = measure?.interior ?? default;
        return measure != null && measure.ok;
    }
    /// <summary>템플릿 패널의 수직 여유 한 줄(서는 칸 비율·빈 칸 면적·실내 크기·좁은 구간). 잴 수 없으면 null.</summary>
    public static string ClearanceLine(DungeonRoomTemplateSO template) => MeasureOf(template)?.clearance;
}

/// <summary>
/// 새 던전 방 템플릿을 만든다(Tools/Training/던전 방 템플릿 만들기). 결투장 기준 기본 방(실내 26×17 + 벽 3칸) + 맵 층 Wave 1개 + 적 구성 Wave 1개.
/// 출입구 Prefab이 있으면 좌우 벽 바닥 높이에 출입구를 하나씩 놓는다.
/// </summary>
public static class DungeonTemplateAuthoring
{
    public const string DefaultFolder = "Assets/Data/Dungeon/Templates";
    public const string DoorPrefabPath = "Assets/Prefabs/Environment/WaveTools/DungeonPipeDoor.prefab";
    /// <summary>목표 SO 기본 폴더(DESIGN_V3 이름표: CombatLeverObjective.asset이 놓이는 곳).</summary>
    public const string ObjectiveFolder = "Assets/Data/Dungeon/Objectives";
    /// <summary>레버 실물 Prefab(영역 facilities3가 만든다). 있으면 새 레버 목표에 미리 지정한다.</summary>
    public const string LeverPrefabPath = "Assets/Prefabs/Environment/DungeonProps/DungeonPropObjectiveLever.prefab";
    /// <summary>4차 출구 문 Prefab(영역 objective4가 만든다). 있으면 새 레버 목표의 exitPrefab에 미리 지정한다(마무리 방식은 기본 '즉시 클리어').</summary>
    public const string ExitPrefabPath = "Assets/Prefabs/Environment/DungeonProps/DungeonPropExitGate.prefab";
    /// <summary>4차 적 생성 규칙 SO 기본 폴더(DESIGN_V4 §5: CombatEncounterRules.asset이 놓이는 곳).</summary>
    public const string EncounterRulesFolder = "Assets/Data/Dungeon/EncounterRules";
    /// <summary>적 Prefab 폴더(새 적 생성 규칙의 종류 Prefab을 이름으로 찾는다).</summary>
    public const string EnemyFolder = "Assets/Prefabs/Enemies";
    private const string RoomContentPath = "Assets/Prefabs/Training/TrainingRoomBase.prefab";

    /// <summary>메모리에만 있는 새 템플릿(저장하지 않는다). door가 있으면 좌우 벽에 출입구를 놓는다.</summary>
    public static DungeonRoomTemplateSO NewTemplate(GameObject door = null)
    {
        var template = ScriptableObject.CreateInstance<DungeonRoomTemplateSO>();
        var content = AssetDatabase.LoadAssetAtPath<TrainingRoomContent>(RoomContentPath);
        template.roomTemplate = content;
        try { template.cellMap = WaveAuthoring.BaseCellMap(content); }
        catch { UnityEngine.Object.DestroyImmediate(template); throw; }
        var room = WaveAuthoring.BaseRoom;
        template.room = new WaveRoomLayout { bounds = new Rect(room.x, room.y, room.width, room.height), spawnOffset = Vector2.zero };
        template.shape = DungeonRoomShape.Single;
        template.role = DungeonRoomRole.Normal;
        template.allowAsStart = true;
        var start = template.playerStart;
        var mapItems = new List<WaveSpawnInfo>();
        if (door != null)
        {
            // 오른쪽 벽: 실내 xMax부터 3칸 중 안쪽 2칸(기준점 = 2×3칸 가운데). 왼쪽 벽은 180° 회전.
            float y = room.yMin + DungeonDoor.Height * .5f;
            mapItems.Add(new WaveSpawnInfo { prefab = door, isRequiredToClear = false, spawnPosition = new Vector2(room.xMax + DungeonDoor.Depth * .5f, y), rotationSteps = 0 });
            mapItems.Add(new WaveSpawnInfo { prefab = door, isRequiredToClear = false, spawnPosition = new Vector2(room.xMin - DungeonDoor.Depth * .5f, y), rotationSteps = 2 });
        }
        template.waves = new[]
        {
            new Wave { overridePlayerStart = true, playerStart = start, spawnObjects = mapItems.ToArray() },
            new Wave { overridePlayerStart = true, playerStart = start, overrideCellMap = false, changeTerrain = false, clearType = WaveClearType.DefeatAllTargets, spawnObjects = Array.Empty<WaveSpawnInfo>() },
        };
        template.encounters = new[] { new DungeonEncounterInfo { name = "적 구성 1", tier = 1, weight = 1 } };
        return template;
    }

    /// <summary>path에 새 템플릿 에셋을 만들고 그 에셋만 저장한다.</summary>
    public static DungeonRoomTemplateSO CreateAsset(string path)
    {
        var template = NewTemplate(AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath));
        AssetDatabase.CreateAsset(template, path);
        AssetDatabase.SaveAssetIfDirty(template);
        return template;
    }

    /// <summary>
    /// folder에 새 레버 목표 SO(LeverObjectiveSO, 기본값 — 레버 3개)를 만들고 그 에셋만 저장한다. 레버 Prefab이 있으면 지정한다.
    /// 격자 편집기 '새 레버 목표' 버튼이 부른다(Undo: 에셋 생성은 Undo로 지우지 않으며, 설정의 연결만 Details Apply로 기록된다).
    /// </summary>
    public static LeverObjectiveSO CreateLeverObjective(string folder)
    {
        EnsureFolder(folder);
        var objective = ScriptableObject.CreateInstance<LeverObjectiveSO>();
        objective.leverPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeverPrefabPath);
        objective.exitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ExitPrefabPath);
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/LeverObjective.asset");
        AssetDatabase.CreateAsset(objective, path);
        AssetDatabase.SaveAssetIfDirty(objective);
        return objective;
    }

    /// <summary>
    /// folder에 새 적 생성 규칙 SO(DungeonEncounterRulesSO, 기본값 — DESIGN_V4 §2.8)를 만들고 그 에셋만 저장한다. 종류(Roller·Swarmer·Drone·Jumper)와 Bulwark의 Prefab은
    /// EnemyFolder에서 같은 이름의 적 Prefab이 있으면 지정한다(실제 연결의 정본은 생성 도구 DungeonEncounterRulesSetup — 영역 encounter4).
    /// 격자 편집기 '새 적 생성 규칙' 버튼이 부른다(Undo: 에셋 생성은 Undo로 지우지 않으며, 설정의 연결만 Details Apply로 기록된다).
    /// </summary>
    public static DungeonEncounterRulesSO CreateEncounterRules(string folder)
    {
        EnsureFolder(folder);
        var rules = ScriptableObject.CreateInstance<DungeonEncounterRulesSO>();
        FillEncounterPrefabs(rules);
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/DungeonEncounterRules.asset");
        AssetDatabase.CreateAsset(rules, path);
        AssetDatabase.SaveAssetIfDirty(rules);
        return rules;
    }
    /// <summary>비어 있는 종류·Bulwark Prefab을 이름이 같은 적 Prefab(EnemyFolder/이름.prefab, EnemyController 있음)으로 채운다. 채운 수를 돌려준다.</summary>
    public static int FillEncounterPrefabs(DungeonEncounterRulesSO rules)
    {
        if (rules == null) return 0;
        int filled = 0;
        GameObject Enemy(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{EnemyFolder}/{name.Trim()}.prefab");
            return prefab != null && prefab.GetComponent<EnemyController>() != null ? prefab : null;
        }
        foreach (var species in rules.species ?? Array.Empty<DungeonSpeciesRule>())
        {
            if (species == null || species.prefab != null) continue;
            species.prefab = Enemy(species.name);
            if (species.prefab != null) filled++;
        }
        if (rules.bulwark != null && rules.bulwark.prefab == null)
        {
            rules.bulwark.prefab = Enemy("Bulwark");
            if (rules.bulwark.prefab != null) filled++;
        }
        return filled;
    }

    [MenuItem("Tools/Training/던전 방 템플릿 만들기")]
    private static void CreateFromMenu()
    {
        string folder = SelectedFolder() ?? DefaultFolder;
        try
        {
            EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/DungeonRoomTemplate.asset");
            var template = CreateAsset(path);
            Selection.activeObject = template;
            EditorGUIUtility.PingObject(template);
            EditorWindow.GetWindow<WaveEditorWindow>("Wave 격자 편집기").Select(template);
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog("던전 방 템플릿 만들기", "만들지 못했습니다: " + exception.Message, "확인");
        }
    }
    // Project 창에서 고른 폴더(또는 고른 에셋의 폴더). 없으면 null.
    private static string SelectedFolder()
    {
        foreach (var guid in Selection.assetGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets", StringComparison.Ordinal)) continue;
            if (AssetDatabase.IsValidFolder(path)) return path;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && AssetDatabase.IsValidFolder(parent)) return parent;
        }
        return null;
    }
    internal static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(parent)) throw new InvalidOperationException("폴더를 만들 수 없습니다: " + folder);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}
