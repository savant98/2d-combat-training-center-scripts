using System.Collections.Generic;
using UnityEngine;

// 훈련 구간 전체에 진행 방향으로 흐르는 길 안내 화살표(GuideArrow) 레인을 깔아 "이쪽으로 가라"고 알린다.
// 화살표 한 줄은 별도 Prefab(전용 화살표 텍스처·Training/GuideArrow 셰이더 재질, Tile 방식 LineRenderer)이며,
// 구간이 끝날 때까지 남아 재질 오프셋(mainTextureOffset)을 계속 흘려 진행 방향으로 움직이고, 경고색과 섬광색 사이를 부드럽게 오간다.
// 공유 원본 Material은 바꾸지 않고, 실행 중 복사본 하나를 모든 레인이 함께 쓴다.
// 레인은 이 개체의 로컬 좌표로 깔리므로 생성 회전(격자 편집기의 설치 방향)만큼 함께 돈다. 격자 편집기에 놓는 화살표
// (Environment/WaveTools/GuideArrowField)는 길이·폭·흐르는 속도를 배치마다 정하고, 추격 분쇄기와 같은 칸 기준점을 써서
// 같은 칸·같은 방향·같은 폭으로 놓으면 분쇄기가 쓸고 지나가는 띠와 정확히 겹친다.
public sealed class CourseGuideField : WorldEntityBase, IWaveDeviceSettings
{
    [Header("Area")]
    [SerializeField, Tooltip("화살표가 흐르는 방향입니다. 등반은 위, 달리기는 오른쪽입니다.")]
    private Vector2 direction = Vector2.up;
    [SerializeField, Min(0f), Tooltip("레인이 생성 위치에서 진행 방향으로 뻗는 길이(칸)입니다.")]
    private float length = 84f;
    [SerializeField, Min(0f), Tooltip("레인을 늘어놓는 폭(칸)입니다. 위로 갈 때는 오른쪽, 오른쪽으로 갈 때는 위쪽으로 잽니다.")]
    private float width = 26f;
    [SerializeField, Min(.5f), Tooltip("레인 사이 간격(칸)입니다. 남는 폭은 양쪽에 똑같이 나눕니다.")]
    private float laneSpacing = 3.8f;
    [SerializeField, Min(0f), Tooltip("레인을 폭의 양 끝에서 안쪽으로 띄우는 최소 거리(칸)입니다.")]
    private float laneInset = 1.5f;
    [SerializeField, Tooltip("켜면 생성 위치가 놓인 칸의 가운데이고, 레인은 그 칸의 진행 반대쪽·폭 시작 쪽 모서리부터 깔립니다(격자 편집기에 놓는 화살표). 끄면 생성 위치가 모서리입니다.")]
    private bool anchorAtCellCorner;

    [Header("Look")]
    [SerializeField, Tooltip("레인으로 복제해 깔 길 안내 화살표 Prefab입니다. 화살표 재질과 Tile 방식이 설정된 LineRenderer입니다.")]
    private LineRenderer arrowPrefab;
    [SerializeField, Min(.05f)] private float laneWidth = .8f;
    [SerializeField, Min(.01f), Tooltip("1칸마다 반복되는 화살표 수입니다.")]
    private float arrowsPerUnit = .6f;
    [SerializeField, Tooltip("화살표가 흐르는 속도(칸/초)입니다.")]
    private float flowSpeed = 3f;
    [SerializeField, Tooltip("기본 경고색입니다.")]
    private Color color = new Color(1f, 0f, 0f, 1f);
    [SerializeField, Min(0f)] private float intensity = 1.2f;
    [SerializeField, Tooltip("경고색과 번갈아 오가는 섬광색입니다.")]
    private Color flashColor = Color.white;
    [SerializeField, Min(0f)] private float flashIntensity = 2f;
    [SerializeField, Min(0f), Tooltip("경고색 → 섬광색 → 경고색으로 한 번 오가는 시간(초)입니다. 0이면 경고색만 씁니다.")]
    private float colorCycle = .9f;
    [SerializeField, Min(0f)] private float fadeDuration = .4f;

    private static readonly int ColorID = Shader.PropertyToID("_Color");

    private readonly List<LineRenderer> lanes = new List<LineRenderer>();
    private Material material;
    // 이 개체에 적용된 값. Prefab의 값이 기본값이고, 격자 편집기에서 배치마다 바꾼 값이 ApplySettings로 들어온다.
    private float currentLength, currentWidth, currentFlowSpeed;
    private float offset;
    private float fade;
    private float fadeTarget;
    private bool returning;

    public Vector2 Direction => direction.normalized;
    public float Length => currentLength;
    public float Width => currentWidth;
    public float FlowSpeed => currentFlowSpeed;
    public float FlowOffset => offset;
    public float Fade => fade;
    // 0이면 경고색, 1이면 섬광색이다. 사인 곡선(InOutSine)으로 부드럽게 오간다.
    public float ColorBlend => colorCycle > 0f ? .5f - .5f * Mathf.Cos(Time.time * Mathf.PI * 2f / colorCycle) : 0f;
    public Color CurrentEmission => material != null ? material.GetColor(ColorID) : Color.clear;
    public Material RuntimeMaterial => material;
    public int LaneCount
    {
        get
        {
            int count = 0;
            foreach (var lane in lanes) if (lane != null && lane.gameObject.activeSelf) count++;
            return count;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        ApplySettings(WaveDeviceSettings.None);
        if (arrowPrefab == null) return;
        material = new Material(arrowPrefab.sharedMaterial) { name = arrowPrefab.sharedMaterial.name + " (Runtime)" };
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        // 풀에서 다시 꺼낸 개체는 Prefab 기본값에서 시작한다. 배치별 값은 WaveManager가 곧바로 적용한다(레인을 다시 깐다).
        ApplySettings(WaveDeviceSettings.None);
        Build();
        offset = 0f;
        fade = 0f;
        fadeTarget = 1f;
        returning = false;
        Refresh();
    }

    // 폭을 레인 간격으로 나눠 가운데 맞춘 위치에 진행 방향으로 뻗는 레인을 깐다.
    private void Build()
    {
        if (arrowPrefab == null || material == null) return;
        Vector2 dir = Direction;
        Vector2 across = new Vector2(Mathf.Abs(dir.y), Mathf.Abs(dir.x));
        Vector2 corner = anchorAtCellCorner ? -(dir + across) * .5f : Vector2.zero;
        int count = Mathf.FloorToInt(Mathf.Max(0f, currentWidth - laneInset * 2f) / laneSpacing) + 1;
        float start = (currentWidth - (count - 1) * laneSpacing) * .5f;
        while (lanes.Count < count)
        {
            var lane = Instantiate(arrowPrefab, transform);
            lane.name = "Lane" + lanes.Count;
            lanes.Add(lane);
        }
        for (int i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            bool used = i < count;
            lane.gameObject.SetActive(used);
            if (!used) continue;
            Vector3 from = corner + across * (start + i * laneSpacing);
            lane.useWorldSpace = false;
            lane.positionCount = 2;
            lane.SetPosition(0, from);
            lane.SetPosition(1, from + (Vector3)(dir * currentLength));
            lane.startWidth = lane.endWidth = laneWidth;
            lane.textureMode = LineTextureMode.Tile;
            lane.textureScale = new Vector2(arrowsPerUnit, 1f);
            lane.sharedMaterial = material;
        }
    }

    private void Update()
    {
        // 텍스처가 선 끝(진행 방향)으로 흐르도록 오프셋을 줄인다. LaserPreviewLine과 같은 방향 규칙이다.
        offset -= currentFlowSpeed * arrowsPerUnit * Time.deltaTime;
        if (offset < -1000f) offset += 1000f;
        fade = fadeDuration > 0f ? Mathf.MoveTowards(fade, fadeTarget, Time.deltaTime / fadeDuration) : fadeTarget;
        Refresh();
        if (returning && fade <= 0f)
        {
            returning = false;
            base.ForceReturnToPool();
        }
    }

    private void Refresh()
    {
        if (material == null) return;
        // 재질 오프셋으로 화살표를 흘리고, 재질 색은 경고색과 섬광색 사이를 끊김 없이 보간한다. 선 색은 흰색으로 두고 알파로만 나타나고 사라진다.
        material.mainTextureOffset = new Vector2(offset, 0f);
        material.SetColor(ColorID, Color.Lerp(color * intensity, flashColor * flashIntensity, ColorBlend) * fade);
        Color tint = new Color(1f, 1f, 1f, fade);
        foreach (var lane in lanes)
        {
            if (lane == null || !lane.gameObject.activeSelf) continue;
            lane.startColor = tint;
            lane.endColor = tint;
        }
    }

    // 웨이브 정리·목표 도달로 회수될 때는 서서히 사라진 뒤 풀로 돌아간다.
    public override void ForceReturnToPool()
    {
        if (!gameObject.activeInHierarchy || fadeDuration <= 0f)
        {
            base.ForceReturnToPool();
            return;
        }
        returning = true;
        fadeTarget = 0f;
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }

    // ───────────── 격자 편집기 세부 설정 ─────────────
    private WaveDeviceParameter LengthParameter => new WaveDeviceParameter("length", "길이",
        "화살표 레인이 놓은 칸에서 진행 방향으로 뻗는 길이입니다. 추격 분쇄기와 함께 쓰면 분쇄기의 최대 거리와 맞춥니다.", "칸", 1f, 300f, 1f, length);
    private WaveDeviceParameter WidthParameter => new WaveDeviceParameter("width", "폭",
        "레인을 늘어놓는 폭입니다. 놓은 칸에서 진행 방향의 오른쪽으로 잽니다. 추격 분쇄기와 함께 쓰면 분쇄기의 띠 폭과 맞춥니다.", "칸", 1f, 60f, 1f, width);
    private WaveDeviceParameter FlowParameter => new WaveDeviceParameter("flow", "흐르는 속도",
        "화살표 무늬가 진행 방향으로 흐르는 속도입니다.", "칸/초", 0f, 12f, .5f, flowSpeed);

    public WaveDeviceParameter[] Parameters => new[] { LengthParameter, WidthParameter, FlowParameter };
    // 격자 편집기의 그림은 작은 표식이라 설정과 무관하다. 길이·폭은 범위(TryGetRange)로 그린다.
    public bool SettingsChangeShape => false;

    public void ApplySettings(WaveDeviceSetting[] overrides)
    {
        overrides ??= WaveDeviceSettings.None;
        currentLength = WaveDeviceSettings.Value(overrides, LengthParameter);
        currentWidth = WaveDeviceSettings.Value(overrides, WidthParameter);
        currentFlowSpeed = WaveDeviceSettings.Value(overrides, FlowParameter);
        // 나와 있는 개체면 바뀐 길이·폭으로 레인을 다시 깐다(Edit Mode 미리보기에서는 깔지 않는다).
        if (Application.isPlaying && isActiveAndEnabled && !returning) { Build(); Refresh(); }
    }

    public bool TryGetRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        // 레인이 깔리는 넓이. 장치 원점 기준, 회전 전 좌표다.
        float along = WaveDeviceSettings.Value(overrides, LengthParameter), across = WaveDeviceSettings.Value(overrides, WidthParameter);
        Vector2 dir = Direction, side = new Vector2(Mathf.Abs(dir.y), Mathf.Abs(dir.x));
        Vector2 corner = anchorAtCellCorner ? -(dir + side) * .5f : Vector2.zero;
        Vector2 center = corner + side * (across * .5f) + dir * (along * .5f);
        Vector2 size = new Vector2(side.x * across + Mathf.Abs(dir.x) * along, side.y * across + Mathf.Abs(dir.y) * along);
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, center, size, new Color(1f, .66f, 0f, .9f));
        return true;
    }
}
