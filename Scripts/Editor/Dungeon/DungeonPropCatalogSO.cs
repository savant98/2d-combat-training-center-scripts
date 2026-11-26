using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 던전 Prop 규칙 카탈로그(6차 T17 — Editor 전용 SO 하나, 정본 에셋 <see cref="AssetPath"/>).
/// Wave 격자 Tool·던전 템플릿 편집기(팔레트 한글 이름·종류 묶음·허용 회전·설치면·길이 설정형 칸 점유)와
/// 단일 검사기(DungeonTemplateValidation)·템플릿 빌더(DungeonTemplateBuilder)가 같은 표를 읽는다.
/// 예전에는 빌더 PropRules·편집기 TrapPrefabNames·작업 폴더 check_templates.py PROPS에 같은 표가 나뉘어 있었다.
/// - 열쇠는 Prefab 이름(빌더 사양의 prefab 문자열과 같다). prefab 참조는 생성 도구(tools/Unity/DungeonPropCatalogSetup.cs)가 이름으로 채운다.
/// - 에셋이 아직 없으면 <see cref="DefaultEntries"/>(초기값)로 만든 메모리 사본을 쓴다(검사·Tool은 그대로 동작한다). 초기값은 생성 도구가 에셋을 처음 만들 때만 쓰며,
///   그 뒤의 정본은 에셋이다(Inspector에서 고친다).
/// - 새 Prop을 만들면 Prefab(루트 DungeonProp + WaveEnvironmentTool) 다음에 이 카탈로그에 한 줄을 더한다(생성 도구 Run이 없는 항목을 추정값으로 더하고 알린다).
/// </summary>
public sealed class DungeonPropCatalogSO : ScriptableObject
{
    public const string AssetPath = "Assets/Data/Dungeon/DungeonPropCatalog.asset";

    /// <summary>빌더 종류(검사 규칙이 쓴다). 순서를 바꾸지 않는다(직렬화 값).</summary>
    public enum Kind { Decoration, Platform, Destructible, Trap, Anchor, Mobility, Interactive, Objective, Marker, Device, Door }
    /// <summary>설치면: Floor 아래 받침 · Wall 옆 벽 · Ceiling 위 지형 · Span 한쪽 끝 이상 천장 · Any · Back 뒤 벽(아랫줄 밑 가운데 2칸 받침) · Rot 회전으로 면(0 바닥·2 천장·1/3 벽).</summary>
    public enum Mount { Floor, Wall, Ceiling, Span, Any, Back, Rot }
    /// <summary>검사용 충돌: None · Solid(차지 칸 전부 고체) · OneWay(윗줄 통과 발판) · Base(아랫줄 고체 받침).</summary>
    public enum Collision { None, Solid, OneWay, Base }
    /// <summary>
    /// 함정 위험 범위 모양(빌더 DangerCells와 같은 계산). 순서·값을 바꾸지 않는다(에셋에 int로 저장).
    /// 7차(DESIGN_V7 V1): 값 5는 옛 폐자재 슈트 ChuteDown(열마다 아래로 광선)의 이름만 DropBlock으로 바꿨다 — 낙석 구조물의 강체 블록(가장 높은 고체 받침에서 모든 열이 멈춤, OneWay는 받침이 아님).
    /// </summary>
    public enum Danger { None, Circle, Ray, Stroke, WelderRows, DropBlock, TopRow }
    /// <summary>팔레트 묶음(장식·플랫폼·파괴·함정·시설·표식).</summary>
    public enum Group { Decoration, Platform, Breakable, Trap, Facility, Marker, Other }
    public static readonly string[] GroupLabels = { "장식", "플랫폼", "파괴", "함정", "시설", "표식", "기타" };
    public const int Rotation0 = 1, Rotation0And180 = 1 | 4, RotationAll = 15;

    [Serializable]
    public sealed class Entry
    {
        [Tooltip("Prefab 이름(빌더 사양·검사의 열쇠). Prefab 이름을 바꾸면 함께 고친다.")] public string prefabName = "";
        [Tooltip("생성 도구가 이름으로 채우는 Prefab 참조(확인용).")] public GameObject prefab;
        [Tooltip("격자 Tool 팔레트에 보일 한글 이름.")] public string displayName = "";
        public Kind kind;
        public Mount mount = Mount.Any;
        public Collision collision;
        [Tooltip("차지 칸(회전 전). 생성 도구가 Prefab의 WaveEnvironmentTool 점유 칸과 맞춘다.")] public Vector2Int size = Vector2Int.one;
        [Tooltip("허용 회전 비트(1 = 0°, 2 = 90°, 4 = 180°, 8 = 270° — 0이면 제한 없음). R 키는 허용 회전만 돈다.")] public int allowedRotations = Rotation0;
        [Tooltip("빌더 문자 지도 글자.")] public string letter = "?";
        [Tooltip("길이 설정 키(왼쪽 변 고정 · 오른쪽으로 늘어남 — 늘어난 칸도 같은 칸 규칙에 든다). 없으면 비움.")] public string lengthKey = "";
        [Tooltip("중력형 파괴 Prop(중력형 위에 쌓지 않는다).")] public bool gravity;
        public Danger danger;
        [Tooltip("위험 범위 설정 키(톱날 length · 접속함 sparkRadius · 피스톤 stroke). 비면 기본값만 쓴다.")] public string dangerKey = "";
        [Tooltip("위험 범위 기본값(설정 키 값이 없을 때) — 원 반지름·뻗는 칸 수.")] public float dangerBase;
        [Tooltip("위험 원에 더하는 값(톱날 날 반지름).")] public float dangerExtra;
        [Tooltip("함정 묶음 위상 설정 키(일정하게 늘어야 한다 — 파도).")] public string phaseKey = "";
        [Tooltip("레이저 예고(충전) 설정 키. 던전 템플릿은 0.5초 이상.")] public string warningKey = "";
        public float warningDefault;
        [Tooltip("점프대 높이 배율 설정 키(도달성 검사).")] public string padHeightKey = "";
        public float padHeightDefault;
        [Tooltip("천장 설치(Mount.Ceiling)를 차지 칸 윗줄 위로 최대 몇 칸까지 찾는지(사슬로 매다는 Prop — 사이 칸은 비어 있어야 함). 0이면 바로 위(기존).")]
        public int ceilingReach;
        [Tooltip("끄면 템플릿에 직접 두지 않는 실물(목표 SO가 세운다 — 검사 오류·템플릿 편집기 팔레트에서 숨김).")] public bool templateAllowed = true;
        [TextArea(1, 3)] public string note = "";

        public Group Group => kind switch
        {
            Kind.Decoration or Kind.Anchor => Group.Decoration,
            Kind.Platform => Group.Platform,
            Kind.Destructible => Group.Breakable,
            Kind.Trap => Group.Trap,
            Kind.Mobility or Kind.Interactive or Kind.Device or Kind.Door => Group.Facility,
            Kind.Objective or Kind.Marker => Group.Marker,
            _ => Group.Other,
        };
        public char Letter => string.IsNullOrEmpty(letter) ? '?' : letter[0];
        public bool HasLength => !string.IsNullOrEmpty(lengthKey);
        public bool Allows(int rotation) => (allowedRotations & (1 << Normalize(rotation))) != 0 || allowedRotations == 0;
        /// <summary>허용 회전 중 from 다음(반시계 90°씩) 회전. 허용 회전이 하나뿐이면 그것.</summary>
        public int NextAllowed(int from)
        {
            for (int step = 1; step <= 4; step++)
            {
                int next = Normalize(from + step);
                if (Allows(next)) return next;
            }
            return Normalize(from);
        }
        public int FirstAllowed(int preferred)
        {
            if (Allows(preferred)) return Normalize(preferred);
            for (int r = 0; r < 4; r++) if (Allows(r)) return r;
            return 0;
        }
        public Entry Clone() => (Entry)MemberwiseClone();
    }

    [Serializable]
    public sealed class ThemeRule
    {
        [Tooltip("템플릿 폴더 이름(Assets/Data/Dungeon/Templates/<테마>).")] public string theme = "";
        [Tooltip("이 테마 템플릿에 둘 수 없는 Prefab 이름(검사 오류·템플릿 편집기 팔레트에서 숨김).")] public string[] forbiddenPrefabs = Array.Empty<string>();
        [TextArea(1, 3)] public string reason = "";
    }

    public List<Entry> entries = new List<Entry>();
    public List<ThemeRule> themes = new List<ThemeRule>();
    [Tooltip("던전 템플릿 편집기 팔레트에서 숨길 Prefab 이름(템플릿에 쓰지 않는 Wave 장치·튜토리얼 부품).")]
    public string[] hiddenInTemplateTool = Array.Empty<string>();

    [NonSerialized] private Dictionary<string, Entry> byName;
    private void OnEnable() => byName = null;
    private void OnValidate() => byName = null;

    public static int Normalize(int rotation) => ((rotation % 4) + 4) % 4;

    public Entry Find(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (byName == null)
        {
            byName = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var entry in entries ?? new List<Entry>())
                if (entry != null && !string.IsNullOrEmpty(entry.prefabName) && !byName.ContainsKey(entry.prefabName)) byName[entry.prefabName] = entry;
        }
        return byName.TryGetValue(name, out var found) ? found : null;
    }
    public Entry Find(GameObject prefab) => prefab == null ? null : Find(prefab.name);
    /// <summary>테마 금지 목록에 있으면 true(이유 포함).</summary>
    public bool Forbidden(string theme, string prefabName, out string reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(theme) || string.IsNullOrEmpty(prefabName)) return false;
        foreach (var rule in themes ?? new List<ThemeRule>())
        {
            if (rule == null || !string.Equals(rule.theme, theme, StringComparison.Ordinal) || rule.forbiddenPrefabs == null) continue;
            if (Array.IndexOf(rule.forbiddenPrefabs, prefabName) < 0) continue;
            reason = string.IsNullOrWhiteSpace(rule.reason) ? $"{theme} 테마 금지 목록" : rule.reason.Trim();
            return true;
        }
        return false;
    }
    public bool HiddenInTemplateTool(GameObject prefab) => prefab != null && hiddenInTemplateTool != null && Array.IndexOf(hiddenInTemplateTool, prefab.name) >= 0;
    public IEnumerable<string> Names(Kind kind) => (entries ?? new List<Entry>()).Where(e => e != null && e.kind == kind).Select(e => e.prefabName);

    // ───────── 공용 접근 ─────────
    private static DungeonPropCatalogSO asset, fallback;
    /// <summary>정본 에셋(없으면 초기값 메모리 사본).</summary>
    public static DungeonPropCatalogSO Active
    {
        get
        {
            if (asset == null) asset = AssetDatabase.LoadAssetAtPath<DungeonPropCatalogSO>(AssetPath);
            if (asset != null) return asset;
            if (fallback == null)
            {
                fallback = CreateDefault();
                fallback.hideFlags = HideFlags.HideAndDontSave;
            }
            return fallback;
        }
    }
    /// <summary>에셋이 없어 초기값 사본을 쓰는 중이면 true(생성 도구 DungeonPropCatalogSetup.Run을 아직 돌리지 않음).</summary>
    public static bool UsingFallback => Active == fallback;
    /// <summary>프로젝트가 바뀌면(에셋 생성·재가져오기) 다시 읽는다.</summary>
    public static void Invalidate()
    {
        if (asset != null) asset.byName = null;
        asset = null;
    }
    public static Entry Lookup(GameObject prefab) => Active.Find(prefab);
    public static Entry Lookup(string name) => Active.Find(name);
    /// <summary>prefab을 rotation으로 둘 수 있는지(카탈로그에 없으면 모든 회전).</summary>
    public static bool RotationAllowed(GameObject prefab, int rotation) => Lookup(prefab)?.Allows(rotation) ?? true;
    /// <summary>R 키: 허용 회전 중 다음 회전(카탈로그에 없으면 +1).</summary>
    public static int NextRotation(GameObject prefab, int from) => Lookup(prefab)?.NextAllowed(from) ?? Normalize(from + 1);
    /// <summary>브러시를 바꿀 때 지금 회전이 허용되지 않으면 첫 허용 회전.</summary>
    public static int FitRotation(GameObject prefab, int rotation) => Lookup(prefab)?.FirstAllowed(rotation) ?? Normalize(rotation);
    /// <summary>팔레트·상세에 보일 이름(한글 이름 · 없으면 Prefab 이름).</summary>
    public static string DisplayName(GameObject prefab)
    {
        if (prefab == null) return "참조 없음";
        var entry = Lookup(prefab);
        return entry != null && !string.IsNullOrWhiteSpace(entry.displayName) ? entry.displayName : prefab.name;
    }
    public static Group GroupOf(GameObject prefab) => Lookup(prefab)?.Group ?? Group.Other;

    /// <summary>
    /// 설치면 판정(빌더 Inspect의 Mount 규칙과 같은 정의). cells = 차지 칸(늘어난 칸 포함), terrainSolid = 지형 고체, blocked = 지형 + 고체 Prop,
    /// support = 받침(지형·고체 Prop·지형 OneWay·Prop 윗면). 문제가 없으면 null. 뒤 벽(Back)은 자리 표식 검사가 따로 본다(여기서도 같은 규칙).
    /// </summary>
    public static string MountProblem(Entry entry, RectInt cells, int rotation, Func<Vector2Int, bool> terrainSolid, Func<Vector2Int, bool> blocked, Func<Vector2Int, bool> support)
    {
        if (entry == null) return null;
        int x0 = cells.xMin, y0 = cells.yMin, w = cells.width, h = cells.height;
        int steps = Normalize(rotation);
        switch (entry.mount)
        {
            case Mount.Floor:
                for (int x = x0; x < x0 + w; x++)
                    if (!support(new Vector2Int(x, y0 - 1))) return $"바닥에 서는 Prop인데 아래 칸 ({x}, {y0 - 1})이 받침(지형·고체 Prop·OneWay)이 아닙니다";
                return null;
            case Mount.Wall:
            {
                char side = MountSide(cells, terrainSolid);
                if (side == '\0') return "벽에 붙는 Prop인데 옆이 벽(지형)이 아닙니다";
                if (entry.danger == Danger.WelderRows && (side == 'L' && steps != 0 || side == 'R' && steps != 2))
                    return "용접 팔은 왼쪽 벽이면 회전 0(오른쪽으로 발사), 오른쪽 벽이면 회전 2(왼쪽으로 발사)입니다";
                return null;
            }
            case Mount.Ceiling:
                if (entry.ceilingReach > 0)
                {
                    // 7차 V1: 사슬로 매다는 Prop(낙석 구조물) — 차지 칸 윗줄 위로 최대 ceilingReach칸 안에서 열마다 천장(지형)을 찾는다.
                    // 모든 열이 같은 높이에서 만나야 하고(평평한 천장 — 호이스트 레일), 그 사이(사슬 칸)가 고체 Prop이 아니어야 하며, 사슬 칸이 1칸 이상이어야 한다.
                    // 사슬 칸의 OneWay·다른 Prop 차지 칸은 검사기 규칙 'v7 낙석'(DungeonTemplateValidation.Rules7)이 따로 본다.
                    int ceiling = int.MinValue;
                    for (int x = x0; x < x0 + w; x++)
                    {
                        int found = int.MinValue;
                        for (int k = 0; k <= entry.ceilingReach; k++)
                        {
                            var c = new Vector2Int(x, y0 + h + k);
                            if (terrainSolid(c)) { found = c.y; break; }
                            if (blocked(c)) return $"사슬 칸 ({x}, {c.y})이 고체 Prop에 막혔습니다(천장 호이스트까지 사슬이 지나는 칸은 비워 둡니다)";
                        }
                        if (found == int.MinValue) return $"사슬로 매다는 Prop인데 열 x {x}의 위 {entry.ceilingReach + 1}칸 안(y {y0 + h}~{y0 + h + entry.ceilingReach})에 천장(지형)이 없습니다";
                        if (ceiling != int.MinValue && found != ceiling) return $"천장 높이가 열마다 다릅니다(x {x}: y {found}, 앞 열: y {ceiling}) — 호이스트 레일은 평평한 천장 아래에 겁니다";
                        ceiling = found;
                    }
                    if (ceiling == y0 + h) return "차지 칸 윗줄 바로 위가 천장입니다(사슬·호이스트를 걸 빈 칸이 1칸 이상 필요 — 1칸 이상 내리세요)";
                    return null;
                }
                for (int x = x0; x < x0 + w; x++)
                    if (!terrainSolid(new Vector2Int(x, y0 + h))) return $"천장에 매다는 Prop인데 위 칸 ({x}, {y0 + h})이 지형이 아닙니다";
                return null;
            case Mount.Span:
                return terrainSolid(new Vector2Int(x0, y0 + h)) || terrainSolid(new Vector2Int(x0 + w - 1, y0 + h)) ? null : "레일 양 끝 모두 위가 지형이 아닙니다(한쪽 끝은 천장에 겁니다)";
            case Mount.Back:
            {
                int mid = Mathf.FloorToInt(x0 + w * .5f - .5f);
                return support(new Vector2Int(mid, y0 - 1)) && support(new Vector2Int(mid + 1, y0 - 1)) ? null : $"뒤 벽 패널 아랫줄 밑 가운데 2칸(x {mid}~{mid + 1}, y {y0 - 1})이 서는 면(받침)이 아닙니다";
            }
            case Mount.Rot:
            {
                // 회전 설치 함정(소형 레이저·피스톤·증기 배출구·가시): 실행은 Z 회전을 따르므로 뻗는/쏘는 방향은 0 위·1 왼쪽·2 아래·3 오른쪽이고
                // 붙는 면은 그 반대쪽이다(laser6 — 6차 검토로 레이저 밖 Rot 함정도 같은 규칙).
                bool Column(int x) => Enumerable.Range(y0, h).All(y => terrainSolid(new Vector2Int(x, y)));
                bool mounted = steps switch
                {
                    0 => Enumerable.Range(x0, w).All(x => blocked(new Vector2Int(x, y0 - 1))),
                    2 => Enumerable.Range(x0, w).All(x => blocked(new Vector2Int(x, y0 + h))),
                    1 => Column(x0 + w),   // 왼쪽으로 뻗음 → 오른쪽 열이 벽
                    _ => Column(x0 - 1),   // 3: 오른쪽으로 뻗음 → 왼쪽 열이 벽
                };
                return mounted ? null : $"회전 {steps}의 설치 면({(steps == 0 ? "바닥" : steps == 2 ? "천장" : steps == 1 ? "오른쪽 벽" : "왼쪽 벽")})이 고체가 아닙니다";
            }
            default: return null;
        }
    }
    /// <summary>벽 옆 설치면: 'L'(왼쪽 열 전부 지형) · 'R' · '\0'(빌더 MountSide와 같다).</summary>
    public static char MountSide(RectInt cells, Func<Vector2Int, bool> terrainSolid)
    {
        bool Column(int x)
        {
            for (int y = cells.yMin; y < cells.yMax; y++) if (!terrainSolid(new Vector2Int(x, y))) return false;
            return true;
        }
        if (Column(cells.xMin - 1)) return 'L';
        if (Column(cells.xMax)) return 'R';
        return '\0';
    }
    /// <summary>위험을 뻗는 방향(회전 0 위 · 2 아래 · 1/3 벽 반대쪽 — 빌더 TrapDirection).</summary>
    public static Vector2Int Direction(RectInt cells, int rotation, Func<Vector2Int, bool> terrainSolid)
    {
        int steps = Normalize(rotation);
        if (steps == 0) return Vector2Int.up;
        if (steps == 2) return Vector2Int.down;
        return MountSide(cells, terrainSolid) == 'L' ? Vector2Int.right : Vector2Int.left;
    }
    /// <summary>항목별 방향: 회전 설치(Mount.Rot — 소형 레이저·피스톤·증기·가시)는 회전만으로(0 위 · 1 왼쪽 · 2 아래 · 3 오른쪽), 그 밖은 벽 설치면 기준(위 오버로드).</summary>
    public static Vector2Int Direction(Entry entry, RectInt cells, int rotation, Func<Vector2Int, bool> terrainSolid)
    {
        if (entry != null && (entry.mount == Mount.Rot || entry.danger == Danger.Ray))
            switch (Normalize(rotation)) { case 1: return Vector2Int.left; case 2: return Vector2Int.down; case 3: return Vector2Int.right; default: return Vector2Int.up; }
        return Direction(cells, rotation, terrainSolid);
    }

    // ───────── 초기값(빌더 PropRules · 4차 이름표 + 6차 변경) ─────────
    public static DungeonPropCatalogSO CreateDefault()
    {
        var catalog = CreateInstance<DungeonPropCatalogSO>();
        catalog.name = "DungeonPropCatalog(초기값)";
        catalog.entries = DefaultEntries();
        catalog.themes = DefaultThemes();
        catalog.hiddenInTemplateTool = DefaultHidden();
        return catalog;
    }
    public static string[] DefaultHidden() => new[] { "ChasingGrinder", "GuideArrowField", "TutorialWallPanel", "WaveGoal", "PlayerContainment", "LaserEmitterCell" };
    public static List<ThemeRule> DefaultThemes() => new List<ThemeRule>
    {
        new ThemeRule
        {
            theme = "Combat",
            forbiddenPrefabs = new[] { "DungeonPropLaserWelderArm", "DungeonPropLaserEmitter", "LaserEmitterCell" },
            reason = "전투 훈련(Combat 테마) 템플릿에는 레이저(소형 레이저·용접 팔·옛 사출기)를 두지 않습니다(6차 T6 — 피스톤·증기·접속함 등으로 대체).",
        },
    };
    static Entry E(string name, string display, Kind kind, Mount mount, Collision collision, int w, int h, int rotations, char letter)
        => new Entry { prefabName = name, displayName = display, kind = kind, mount = mount, collision = collision, size = new Vector2Int(w, h), allowedRotations = rotations, letter = letter.ToString() };
    public static List<Entry> DefaultEntries()
    {
        const int R0 = Rotation0, R02 = Rotation0And180, RA = RotationAll;
        var list = new List<Entry>
        {
            // 장식(Decoration·Anchor)
            E("DungeonPropConsole", "콘솔", Kind.Decoration, Mount.Floor, Collision.None, 2, 2, R0, 'C'),
            E("DungeonPropPipeBundle", "배관 다발", Kind.Decoration, Mount.Wall, Collision.None, 1, 4, R02, 'P'),
            E("DungeonPropGearDrive", "기어 구동부", Kind.Decoration, Mount.Any, Collision.None, 3, 3, RA, 'G'),
            E("DungeonPropWarningSign", "경고 표지판", Kind.Decoration, Mount.Any, Collision.None, 1, 1, RA, '!'),
            E("DungeonPropShredderHousing", "분쇄기 하우징", Kind.Decoration, Mount.Wall, Collision.None, 2, 3, R02, 'Z'),
            E("DungeonPropCableTrunk", "전선 다발", Kind.Decoration, Mount.Wall, Collision.None, 1, 6, R02, 'Y'),
            E("DungeonPropDuctFan", "덕트 팬", Kind.Decoration, Mount.Any, Collision.None, 2, 2, RA, 'u'),
            E("DungeonPropSortingArm", "분류 팔", Kind.Decoration, Mount.Ceiling, Collision.None, 2, 3, R0, 'a'),
            E("DungeonPropInspectionTrolley", "점검 트롤리", Kind.Decoration, Mount.Ceiling, Collision.None, 2, 1, R0, 't'),
            E("DungeonPropCraneRail", "장식 레일", Kind.Decoration, Mount.Span, Collision.None, 4, 1, R0, 'H'),
            E("DungeonPropFabricatorBooth", "제작 부스", Kind.Anchor, Mount.Floor, Collision.None, 3, 4, R0, 'F'),
            E("DungeonPropRepairCradle", "수리 거치대", Kind.Anchor, Mount.Floor, Collision.None, 3, 3, R0, 'c'),
            E("DungeonPropRelayBeacon", "중계 비컨", Kind.Anchor, Mount.Floor, Collision.None, 1, 3, R0, 'b'),
            // 플랫폼
            E("DungeonPropCargoContainer", "화물 컨테이너", Kind.Platform, Mount.Floor, Collision.Solid, 2, 2, R0, 'K'),
            E("DungeonPropServiceGantry", "정비 발판", Kind.Platform, Mount.Any, Collision.OneWay, 4, 1, R0, '~'),
            E("DungeonPropHangingCargo", "매달린 화물", Kind.Platform, Mount.Any, Collision.Solid, 2, 2, R0, 'A'),
            E("DungeonPropBulkheadCover", "차폐벽", Kind.Platform, Mount.Floor, Collision.Solid, 1, 3, R0, 'q'),
            E("DungeonPropControlDesk", "관제 책상", Kind.Platform, Mount.Floor, Collision.Solid, 3, 1, R0, 'd'),
            E("DungeonPropScrapHeap", "폐자재 더미", Kind.Platform, Mount.Any, Collision.Solid, 3, 2, R0, 'n'),
            E("DungeonPropShutterGate", "셔터", Kind.Platform, Mount.Floor, Collision.Solid, 1, 3, R0, 'I'),
            // 파괴(중력형은 중력형 위에 쌓지 않는다)
            E("DungeonPropScrapCrate", "폐자재 상자", Kind.Destructible, Mount.Floor, Collision.Solid, 1, 1, R0, 'X'),
            E("DungeonPropSupplyRack", "보급 랙", Kind.Destructible, Mount.Floor, Collision.None, 2, 2, R0, 'R'),
            E("DungeonPropReinforcedCrate", "강화 상자", Kind.Destructible, Mount.Floor, Collision.Solid, 2, 2, R0, 'x'),
            E("DungeonPropPalletStack", "팔레트 더미", Kind.Destructible, Mount.Floor, Collision.OneWay, 2, 1, R0, 'l'),
            E("DungeonPropPartsBin", "부품 통", Kind.Destructible, Mount.Floor, Collision.None, 1, 1, R0, 'o'),
            E("DungeonPropToolCabinet", "공구 캐비닛", Kind.Destructible, Mount.Floor, Collision.None, 1, 2, R0, 'i'),
            E("DungeonPropGlassPartition", "유리 칸막이", Kind.Destructible, Mount.Floor, Collision.Solid, 1, 3, R0, '|'),
            E("DungeonPropBreakableGrate", "파괴 격자", Kind.Destructible, Mount.Any, Collision.OneWay, 2, 1, R0, 'g'),
            // 함정
            E("DungeonPropSteamVent", "증기 배출구", Kind.Trap, Mount.Rot, Collision.None, 1, 1, RA, 'S'),
            E("DungeonPropShredderIntake", "분쇄 투입구", Kind.Trap, Mount.Floor, Collision.None, 2, 2, R0, 'w'),
            E("DungeonPropRockfall", "낙석 구조물", Kind.Trap, Mount.Ceiling, Collision.None, 4, 4, R0, 'r'),   // 7차 V1: 옛 폐자재 슈트(k)를 대신함
            E("DungeonPropLaserWelderArm", "레이저 용접 팔", Kind.Trap, Mount.Wall, Collision.None, 2, 2, R02, 'Q'),
            E("DungeonPropLaserEmitter", "소형 레이저 사출기", Kind.Trap, Mount.Rot, Collision.None, 1, 1, RA, '+'),
            E("DungeonPropCableJunction", "전기 접속함", Kind.Trap, Mount.Any, Collision.None, 1, 1, RA, 'e'),
            E("RotatingSawCell", "회전 톱날", Kind.Trap, Mount.Any, Collision.None, 1, 1, RA, '*'),
            E("RotatingSawLargeCell", "큰 회전 톱날", Kind.Trap, Mount.Any, Collision.None, 1, 1, RA, '&'),
            E("PistonTrapCell", "피스톤", Kind.Trap, Mount.Rot, Collision.None, 1, 1, RA, 'm'),
            E("PistonTrapLargeCell", "큰 피스톤", Kind.Trap, Mount.Rot, Collision.None, 2, 2, RA, 'p'),
            E("SpikeTrapCell", "가시 함정", Kind.Trap, Mount.Rot, Collision.None, 1, 1, RA, '^'),
            // 시설(이동·상호작용·장치·출입구)
            E("DungeonPropConveyorBelt", "컨베이어", Kind.Mobility, Mount.Any, Collision.Solid, 4, 1, R0, '>'),   // 0°만: 180°는 운반 기준(윗면)·늘어나는 쪽이 실행과 맞지 않는다(방향은 배치 설정으로)
            E("DungeonPropCraneHook", "경로 크레인", Kind.Mobility, Mount.Any, Collision.None, 2, 4, R0, 'h'),
            E("DungeonPropCraneAnchor", "고정 크레인", Kind.Mobility, Mount.Any, Collision.None, 2, 4, R0, 'N'),
            E("DungeonPropRepairStation", "수리대", Kind.Interactive, Mount.Floor, Collision.None, 2, 2, R0, 'M'),
            E("DungeonTeleportPad", "텔레포트 중계기", Kind.Interactive, Mount.Floor, Collision.Base, 6, 4, R0, 'T'),
            E("JumpPadCell", "점프대", Kind.Device, Mount.Floor, Collision.None, 1, 1, R0, 'J'),
            E("DungeonPipeDoor", "관 출입구", Kind.Door, Mount.Any, Collision.None, 2, 3, R02, 'D'),
            E("DungeonPropObjectiveLever", "목표 레버(실물)", Kind.Interactive, Mount.Back, Collision.None, 4, 6, R0, 'L'),
            E("DungeonPropExitGate", "출구 문(실물)", Kind.Interactive, Mount.Back, Collision.None, 5, 6, R0, 'V'),
            // 표식(실행 때 세우지 않는다)
            E("DungeonObjectiveSlot", "목표 자리 표식", Kind.Objective, Mount.Back, Collision.None, 4, 6, R0, 'O'),
            E("DungeonExitSlot", "출구 자리 표식", Kind.Objective, Mount.Back, Collision.None, 5, 6, R0, 'E'),
            E("DungeonArrivalSlot", "도착 자리 표식", Kind.Objective, Mount.Back, Collision.None, 5, 6, R0, 'U'),
            E("DungeonSpawnZone", "생성 구역", Kind.Marker, Mount.Any, Collision.None, 1, 1, R0, 'z'),
        };
        Entry Of(string name) => list.First(e => e.prefabName == name);
        // 중력형 파괴 Prop
        foreach (var name in new[] { "DungeonPropScrapCrate", "DungeonPropSupplyRack", "DungeonPropReinforcedCrate", "DungeonPropPalletStack", "DungeonPropPartsBin", "DungeonPropToolCabinet" }) Of(name).gravity = true;
        // 길이 설정형(왼쪽 변 고정 · 오른쪽으로 늘어남)
        Of("DungeonPropConveyorBelt").lengthKey = "conveyorLength";
        Of("DungeonPropCraneRail").lengthKey = "railLength";
        Of("DungeonPropShredderIntake").lengthKey = "shredderWidth";
        Of("DungeonPropBreakableGrate").lengthKey = "grateLength";
        // 함정 위험 범위·위상·예고(빌더 DangerCells·PhaseKeys·LaserWarnings)
        void Trap(string name, Danger danger, string dangerKey, float dangerBase, float extra, string phase)
        {
            var e = Of(name); e.danger = danger; e.dangerKey = dangerKey ?? ""; e.dangerBase = dangerBase; e.dangerExtra = extra; e.phaseKey = phase ?? "";
        }
        Trap("DungeonPropSteamVent", Danger.Stroke, "", 2f, 0f, "ventOffset");
        Trap("DungeonPropShredderIntake", Danger.TopRow, "", 0f, 0f, "");
        Trap("DungeonPropRockfall", Danger.DropBlock, "", 0f, 0f, "");   // 7차 V1: 한 번만 떨어짐 — 위상 키 없음
        Trap("DungeonPropLaserWelderArm", Danger.WelderRows, "", 0f, 0f, "welderOffset");
        Trap("DungeonPropLaserEmitter", Danger.Ray, "", 0f, 0f, "welderOffset");
        Trap("DungeonPropCableJunction", Danger.Circle, "sparkRadius", 1.5f, 0f, "sparkOffset");
        Trap("RotatingSawCell", Danger.Circle, "length", 3f, .4f, "sawStartAngle");
        Trap("RotatingSawLargeCell", Danger.Circle, "length", 3f, .8f, "sawStartAngle");
        Trap("PistonTrapCell", Danger.Stroke, "stroke", 2f, 0f, "pistonOffset");
        Trap("PistonTrapLargeCell", Danger.None, "", 0f, 0f, "pistonOffset");
        Trap("SpikeTrapCell", Danger.None, "", 0f, 0f, "");
        // 예고(충전) 키 — Prefab 기본값을 먼저 쓴다(laser6: 소형 0.2초·용접 팔 0.9초). 템플릿 규칙은 0.5초 이상(전투 훈련은 레이저 금지가 먼저 걸린다).
        Of("DungeonPropLaserWelderArm").warningKey = "welderCharge"; Of("DungeonPropLaserWelderArm").warningDefault = .9f;
        Of("DungeonPropLaserEmitter").warningKey = "welderCharge"; Of("DungeonPropLaserEmitter").warningDefault = .2f;
        var pad = Of("JumpPadCell"); pad.padHeightKey = "height"; pad.padHeightDefault = 3.5f;
        // 7차 V1 낙석 구조물: 천장 호이스트까지 사슬 칸(블록 윗변 위 1~14칸)을 찾는다(PropRockfall.ceilingReach와 같은 값).
        Of("DungeonPropRockfall").ceilingReach = 14;
        Of("DungeonPropRockfall").note = "7차 V1: 한 번만 떨어지는 4×4 압축 고철 블록(천장 호이스트 사슬). 고체 바닥에만 — 낙하 열에 OneWay 금지, 낙하 높이 3칸 권장(시야).";
        // 템플릿에 직접 두지 않는 실물(목표 SO가 자리 표식에 세운다)
        Of("DungeonPropObjectiveLever").templateAllowed = false;
        Of("DungeonPropObjectiveLever").note = "레버 실물은 목표 SO가 세웁니다 — DungeonObjectiveSlot(4×6)을 놓으세요.";
        Of("DungeonPropExitGate").templateAllowed = false;
        Of("DungeonPropExitGate").note = "출구 문 실물은 목표 SO가 세웁니다 — DungeonExitSlot(5×6)을 놓으세요(W2 도착 문은 DungeonArrivalSlot).";
        Of("DungeonArrivalSlot").note = "6차 T11: 다음 Wave 시작 방의 도착 문 자리(출구 자리와 같은 5×6 뒤 벽 규격 · 시작 방 템플릿에 1개).";
        Of("DungeonPropLaserEmitter").note = "6차 T6: 1칸 소형 Prop 레이저(회전 0 위 · 1 왼쪽 · 2 아래 · 3 오른쪽으로 쏨 — 붙는 면은 반대쪽). 전투 훈련 테마에서는 금지.";
        Of("DungeonPipeDoor").note = "좌우 바깥 벽 3칸 중 안쪽 2칸(회전 0 = 오른쪽 벽, 180° = 왼쪽 벽).";
        return list;
    }
}
