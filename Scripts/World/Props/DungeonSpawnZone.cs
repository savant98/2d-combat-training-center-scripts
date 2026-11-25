using UnityEngine;

/// <summary>생성 구역이 받는 적 종류.</summary>
public enum DungeonSpawnZoneKind
{
    /// <summary>지상 적(Roller·Jumper·Bulwark) 후보만 편향한다.</summary>
    Ground = 0,
    /// <summary>공중 적(Swarmer 묶음·Drone) 후보만 편향한다.</summary>
    Air = 1,
    /// <summary>둘 다.</summary>
    Mixed = 2,
}

/// <summary>
/// 던전 적 생성 구역 표식(4차 R16, 루트 WaveEnvironmentTool로 Wave 격자 Tool 팔레트에 들어간다). 템플릿 맵 층에 놓아
/// '이 사각형 안에서 적이 나오기를 선호함'을 알린다. 점유는 1×1이고 (x, y) = 구역 왼쪽 아래 칸이며, 실제 넓이는 배치 설정
/// spawnZoneWidth × spawnZoneHeight로 오른쪽·위로 넓어진다(격자 Tool은 TryGetRange의 상자로 그린다 — 회전하지 않고 놓는다).
/// 실행 때 세우지 않는다 — DungeonRunBuilder가 Prop 목록에서 빼고 DungeonRoomData.spawnZones로 옮기며,
/// DungeonSpawnPointMap이 구역 안 후보 지점의 가중치를 ×(1 + spawnZoneWeight)로 올린다.
/// 물리 질의 없음(데이터 표식).
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonSpawnZone : MonoBehaviour, IWaveDeviceSettings
{
    public const string KindKey = "spawnZoneKind";
    public const string WidthKey = "spawnZoneWidth";
    public const string HeightKey = "spawnZoneHeight";
    public const string WeightKey = "spawnZoneWeight";

    private static readonly string[] KindOptions = { "지상", "공중", "혼합" };

    [Header("기본값(배치별 설정이 없을 때)")]
    [SerializeField, Tooltip("받는 적 종류(지상·공중·혼합).")] private DungeonSpawnZoneKind kind = DungeonSpawnZoneKind.Mixed;
    [SerializeField, Range(1, 40), Tooltip("구역 폭(칸, 놓은 칸에서 오른쪽으로).")] private int width = 4;
    [SerializeField, Range(1, 30), Tooltip("구역 높이(칸, 놓은 칸에서 위로).")] private int height = 3;
    [SerializeField, Range(0f, 4f), Tooltip("구역 안 후보의 가중치 가산(×(1 + 값)).")] private float weight = 1f;

    private DungeonSpawnZoneKind currentKind;
    private int currentWidth, currentHeight;
    private float currentWeight;
    private bool applied;

    private WaveDeviceParameter KindParameter => new WaveDeviceParameter(KindKey, "적 종류",
        "이 구역이 받는 적 종류입니다. 지상 = Roller·Jumper·Bulwark, 공중 = Swarmer 묶음·Drone, 혼합 = 둘 다.", KindOptions, (int)kind);
    private WaveDeviceParameter WidthParameter => new WaveDeviceParameter(WidthKey, "폭",
        "구역의 가로 칸 수입니다. 놓은 칸에서 오른쪽으로 넓어집니다.", "칸", 1f, 40f, 1f, width);
    private WaveDeviceParameter HeightParameter => new WaveDeviceParameter(HeightKey, "높이",
        "구역의 세로 칸 수입니다. 놓은 칸에서 위로 넓어집니다.", "칸", 1f, 30f, 1f, height);
    private WaveDeviceParameter WeightParameter => new WaveDeviceParameter(WeightKey, "선호 가중치",
        "구역 안 생성 후보의 가중치를 ×(1 + 값)으로 올립니다. 0이면 표시만 하고 편향하지 않습니다.", "", 0f, 4f, .25f, weight);

    /// <summary>배치별 설정 항목(키 spawnZoneKind·spawnZoneWidth·spawnZoneHeight·spawnZoneWeight).</summary>
    public WaveDeviceParameter[] Parameters => new[] { KindParameter, WidthParameter, HeightParameter, WeightParameter };
    // 격자 Tool의 표식 그림은 작은 1×1 표식이라 설정과 무관하다. 넓이는 범위(TryGetRange)로 그린다.
    public bool SettingsChangeShape => false;

    /// <summary>마지막으로 적용한 설정의 종류·크기·가중치(적용 전이면 Prefab 기본값).</summary>
    public DungeonSpawnZoneKind Kind => applied ? currentKind : kind;
    public Vector2Int Size => applied ? new Vector2Int(currentWidth, currentHeight) : new Vector2Int(width, height);
    public float Weight => applied ? currentWeight : weight;

    public void ApplySettings(WaveDeviceSetting[] overrides)
    {
        Read(overrides, out currentKind, out currentWidth, out currentHeight, out currentWeight);
        applied = true;
    }

    public bool TryGetRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        Read(overrides, out var k, out int w, out int h, out _);
        // 장치 원점 = 놓은 칸의 가운데. 구역은 그 칸의 왼쪽 아래 꼭짓점부터 w×h칸이다(회전 전 좌표).
        Vector2 center = new Vector2((w - 1) * .5f, (h - 1) * .5f);
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, center, new Vector2(w, h), ZoneColor(k));
        return true;
    }

    /// <summary>
    /// 배치 설정과 놓은 자리(spawnPosition = 놓은 칸의 가운데)로 구역 사각형을 계산한다(개체를 만들지 않고 Prefab 컴포넌트로 불러도 된다).
    /// offset은 결과에 더할 좌표(방을 세운 자리 — 월드로 옮길 때). kind는 (int)DungeonSpawnZoneKind.
    /// </summary>
    public void Describe(WaveDeviceSetting[] overrides, Vector2 position, Vector2 offset, out Rect rect, out int zoneKind, out float zoneWeight)
    {
        Read(overrides, out var k, out int w, out int h, out zoneWeight);
        rect = new Rect(position + offset - new Vector2(.5f, .5f), new Vector2(w, h));
        zoneKind = (int)k;
    }

    /// <summary>맵 층 항목이 생성 구역 표식이면 구역 사각형(offset을 더한 좌표)·종류·가중치를 돌려준다. 아니면 false.</summary>
    public static bool TryRead(WaveSpawnInfo info, Vector2 offset, out Rect rect, out int zoneKind, out float zoneWeight)
    {
        rect = default;
        zoneKind = (int)DungeonSpawnZoneKind.Mixed;
        zoneWeight = 0f;
        if (info == null || info.prefab == null || !info.prefab.TryGetComponent(out DungeonSpawnZone zone)) return false;
        zone.Describe(info.deviceSettings, info.spawnPosition, offset, out rect, out zoneKind, out zoneWeight);
        return true;
    }

    /// <summary>prefab이 생성 구역 표식인지.</summary>
    public static bool IsZone(GameObject prefab) => prefab != null && prefab.GetComponent<DungeonSpawnZone>() != null;

    private void Read(WaveDeviceSetting[] overrides, out DungeonSpawnZoneKind k, out int w, out int h, out float wt)
    {
        overrides ??= WaveDeviceSettings.None;
        k = (DungeonSpawnZoneKind)Mathf.Clamp(Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, KindParameter)), 0, 2);
        w = Mathf.Clamp(Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, WidthParameter)), 1, 40);
        h = Mathf.Clamp(Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, HeightParameter)), 1, 30);
        wt = WaveDeviceSettings.Value(overrides, WeightParameter);
    }

    // 격자 Tool 표시 색: 지상 녹색·공중 하늘색·혼합 노랑(적 생성 구역 — 위험 빨강과 구분).
    private static Color ZoneColor(DungeonSpawnZoneKind k) => k switch
    {
        DungeonSpawnZoneKind.Ground => new Color(.4f, .95f, .45f, .9f),
        DungeonSpawnZoneKind.Air => new Color(.45f, .75f, 1f, .9f),
        _ => new Color(1f, .85f, .3f, .9f),
    };
}
