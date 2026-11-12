using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

// 도약 내려찍기의 표시 전용 개체. 판정과 이동은 LeapSlamAbility와 모터가 맡고 여기서는 그리기만 한다.
// - 예고: 룸 중앙의 사각형 피격 범위(바탕 빗금 + 굵은 테두리 + 모서리 + 가운데에서 사방으로 번지는 화살표)와
//         착지점 표식·낙하 경로 기둥. 착지가 가까울수록 밝아지고 맥동이 빨라지며 직전에는 흰빛으로 번쩍인다.
//         Admin 낙하 큐브 경고와 같은 규칙이고, 훨씬 크며 사방을 가리킨다.
// - 속도선: 도약·낙하 순간 세로 빛줄기가 잠깐 남아 화면 밖으로 이어진 움직임을 보여 준다.
// - 예열: 준비 동안 바닥을 짚은 추진기 분사구의 발치에서 불길이 새어 나와 바닥을 따라 깔린다.
// - 도약 분사: 솟구치는 순간 추진기 분사가 바닥을 때려 불기둥이 솟고 불길이 좌우로 퍼진다.
// - 자세 제어 분사: 회수 때 기운 본체의 옆면에서 옆으로 뿜는 짧은 분사 불꽃.
// - 낙하 화염: 내려찍는 몸이 불덩이가 되어 떨어진다. 몸을 감싼 불과, 지나온 길에 남아 따라 내려오는 화염 꼬리를 그린다.
// - 폭발: 범위 전체가 번쩍이고 테두리가 밖으로 밀려 나가며, 범위 크기에 맞춘 폭발 입자가 터진다.
//         이어서 범위 전체와 바닥에서 화염이 치솟고, 바닥에는 잔불이 잠시 남았다가 꺼진다.
// 밝기는 Renderer별 MaterialPropertyBlock으로만 바꾼다(공유 Material 원본은 그대로).
public sealed class LeapSlamAreaVisual : MonoBehaviour
{
    // 입자를 방출할 자리: 범위 전체, 착지 바닥선(범위 폭), 착지점
    public enum BurstPlacement { Area, FloorLine, ImpactPoint }

    [Serializable]
    public struct AreaBurst
    {
        public ParticleSystem system;
        public BurstPlacement placement;
        [Tooltip("Area는 기준 면적(burstReferenceArea)에서의 수량, FloorLine은 바닥 1칸당 수량, ImpactPoint는 그대로의 수량")]
        public float count;
        [Tooltip("폭발 뒤 이 시간에 방출한다(초). 잇따라 터지는 느낌을 만든다")]
        public float delay;
    }

    // 폭발 뒤 정해진 시간에 한꺼번에 치솟는 화염 한 무리
    [Serializable]
    public struct FlameWave
    {
        public BurstPlacement placement;
        public float delay;
        [Tooltip("Area는 1칸²당, FloorLine은 바닥 1칸당, ImpactPoint는 그대로의 화염 수")]
        public float amount;
        [Tooltip("화염 폭(칸): 최소·최대")]
        public Vector2 width;
        [Tooltip("화염 길이(칸): 최소·최대")]
        public Vector2 length;
        [Tooltip("위로 솟는 속도(칸/초): 최소·최대")]
        public Vector2 riseSpeed;
        [Tooltip("수명(초): 최소·최대")]
        public Vector2 lifetime;
        [Tooltip("ImpactPoint일 때 착지점 좌우로 흩는 폭(칸)")]
        public float spread;
    }

    [Header("예고 범위")]
    [SerializeField] private SpriteRenderer fill;
    [Tooltip("아래·위·왼쪽·오른쪽 순서. 1 × 1칸 사각 Sprite를 늘여 쓴다")]
    [SerializeField] private SpriteRenderer[] borders = new SpriteRenderer[4];
    [Tooltip("왼쪽 아래부터 반시계 순서. 꼭짓점이 피벗이고 오른쪽·위로 뻗는 ㄴ자 Sprite")]
    [SerializeField] private SpriteRenderer[] corners = new SpriteRenderer[4];
    [Tooltip("오른쪽을 가리키는 화살표 원본. 실행 때 복제해 8방향으로 놓는다")]
    [SerializeField] private SpriteRenderer chevronTemplate;
    [SerializeField, Min(0.05f)] private float borderThickness = 0.22f;
    [SerializeField, Min(0.5f)] private float chevronSpacing = 2.4f;
    [SerializeField, Min(0f)] private float chevronStartDistance = 3f;
    [SerializeField, Min(0f)] private float chevronEdgeMargin = 1.2f;
    [SerializeField, Min(1)] private int maxChevronsPerDirection = 7;

    [Header("착지점 표식")]
    [SerializeField] private Transform landingMark;
    [SerializeField] private Transform landingColumn;
    [SerializeField] private SpriteRenderer[] landingRenderers;
    [SerializeField, Min(0.01f)] private float columnGrowDuration = 0.3f;

    [Header("예고 밝기")]
    [Tooltip("예고 직후의 밝기(착지 순간 1)")]
    [SerializeField, Range(0f, 1f)] private float minIntensity = 0.55f;
    [Tooltip("맥동 횟수(Hz): 예고 직후(x) → 착지 직전(y)")]
    [SerializeField] private Vector2 pulseRate = new Vector2(3f, 12f);
    [SerializeField, Range(0f, 1f)] private float pulseDepth = 0.45f;
    [Tooltip("착지 이 시간(초) 전부터 흰빛으로 번쩍인다")]
    [SerializeField, Min(0f)] private float flashTime = 0.15f;
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField, Range(0f, 1f)] private float flashAmount = 0.7f;
    [SerializeField, Min(0.01f)] private float appearDuration = 0.18f;
    [Tooltip("화살표 밝기 물결의 길이(칸)")]
    [SerializeField, Min(0.1f)] private float chevronWaveLength = 9f;
    [Tooltip("물결이 밖으로 퍼지는 속도(칸/초): 예고 직후(x) → 착지 직전(y)")]
    [SerializeField] private Vector2 chevronWaveSpeed = new Vector2(10f, 26f);

    [Header("속도선")]
    [SerializeField] private SpriteRenderer streak;
    [SerializeField, Min(0.1f)] private float streakWidth = 2.4f;
    [SerializeField, Min(0.01f)] private float streakDuration = 0.32f;

    [Header("폭발")]
    [SerializeField] private SpriteRenderer blastFlash;
    [Tooltip("범위 전체의 빛이 사라지기까지의 시간(초). 처음 잠깐 번쩍인 뒤 주황 잔광이 남는다")]
    [SerializeField, Min(0.01f)] private float blastFlashDuration = 1.1f;
    [Tooltip("폭발 때 테두리가 밖으로 밀려 나가는 거리(칸)")]
    [SerializeField, Min(0f)] private float blastBorderPush = 1.2f;
    [SerializeField, Min(0.01f)] private float blastBorderDuration = 0.3f;
    [SerializeField] private AreaBurst[] bursts = new AreaBurst[0];
    [Tooltip("bursts의 수량을 정한 기준 면적(칸²)")]
    [SerializeField, Min(1f)] private float burstReferenceArea = 80f;
    [SerializeField, Min(0.1f)] private float maxBurstCountScale = 2f;
    [SerializeField, Min(1f)] private float maxBurstSizeScale = 2.4f;

    [Header("화염")]
    [Tooltip("화염 한 덩이를 빨강 외곽·주황 몸체·노랑 코어 세 층으로 겹쳐 그린다")]
    [SerializeField] private ParticleSystem flameOuter;
    [SerializeField] private ParticleSystem flameBody;
    [SerializeField] private ParticleSystem flameCore;
    [SerializeField] private ParticleSystem flameEmbers;
    [SerializeField] private FlameWave[] flameWaves = new FlameWave[0];
    [Tooltip("한 무리에서 방출할 화염 수의 상한. 넓은 룸에서 입자가 지나치게 늘지 않게 한다")]
    [SerializeField, Min(1)] private int maxFlamesPerWave = 160;

    [Header("예열 · 분사구 발치의 불길")]
    [Tooltip("불길 폭(칸): 예열 시작(x) → 발사 직전(y)")]
    [SerializeField] private Vector2 washWidth = new Vector2(0.4f, 0.8f);
    [Tooltip("불길 높이(칸): 예열 시작(x) → 발사 직전(y). 추진기 길이(1칸)보다 낮게 둬 본체를 받친 추진기가 가려지지 않게 한다")]
    [SerializeField] private Vector2 washLength = new Vector2(0.3f, 0.6f);
    [Tooltip("바닥을 따라 퍼지는 속도(칸/초): 예열 시작(x) → 발사 직전(y)")]
    [SerializeField] private Vector2 washSpeed = new Vector2(2f, 7.5f);

    [Header("자세 제어 분사")]
    [Tooltip("분사 불꽃 폭(칸): 최소·최대")]
    [SerializeField] private Vector2 jetWidth = new Vector2(0.5f, 0.75f);
    [Tooltip("분사 불꽃 길이(칸): 최소·최대")]
    [SerializeField] private Vector2 jetLength = new Vector2(1.6f, 2.4f);
    [Tooltip("불꽃이 분사 방향으로 밀려 나가는 속도(칸/초): 최소·최대")]
    [SerializeField] private Vector2 jetSpeed = new Vector2(3f, 6f);

    [Header("도약 분사")]
    [Tooltip("분사구 하나 아래에서 솟는 불기둥의 수")]
    [SerializeField, Min(0)] private int launchColumnFlames = 2;
    [SerializeField] private Vector2 launchColumnWidth = new Vector2(1.1f, 1.7f);
    [SerializeField] private Vector2 launchColumnLength = new Vector2(3.2f, 5.2f);
    [Tooltip("분사구 하나에서 바닥을 따라 퍼지는 불길의 수")]
    [SerializeField, Min(0)] private int launchFloorFlames = 6;
    [SerializeField] private Vector2 launchFloorWidth = new Vector2(0.9f, 1.6f);
    [SerializeField] private Vector2 launchFloorLength = new Vector2(1.1f, 2.3f);
    [Tooltip("바닥을 따라 퍼지는 속도(칸/초): 최소·최대")]
    [SerializeField] private Vector2 launchFloorSpeed = new Vector2(6f, 16f);

    [Header("낙하 화염")]
    [Tooltip("낙하 경로에 화염 꼬리를 놓는 간격(칸)")]
    [SerializeField, Min(0.1f)] private float diveFlameSpacing = 0.7f;
    [SerializeField] private Vector2 diveFlameWidth = new Vector2(1.9f, 2.8f);
    [SerializeField] private Vector2 diveFlameLength = new Vector2(3.6f, 5.6f);
    [SerializeField] private Vector2 diveFlameLifetime = new Vector2(0.22f, 0.4f);
    [Tooltip("꼬리가 몸을 따라 내려가는 속도(칸/초)")]
    [SerializeField, Min(0f)] private float diveFlameFollowSpeed = 18f;
    [Tooltip("몸을 감싸는 불덩이의 폭·길이(칸)")]
    [SerializeField] private Vector2 diveHeadSize = new Vector2(3.4f, 4.8f);

    public bool IsWarningVisible { get; private set; }
    public Rect WarningArea { get; private set; }
    public Vector2 LandingPoint { get; private set; }
    public float WarningProgress { get; private set; }
    public float WarningIntensity { get; private set; }
    public float WarningPulseHz { get; private set; }
    public float WarningFlash { get; private set; }
    public int ActiveChevronCount { get; private set; }
    public bool IsStreakVisible => streakAge >= 0f;
    public bool IsBlastVisible => blastAge >= 0f;
    public Rect LastBlastArea { get; private set; }
    public int BlastCount { get; private set; }
    public int LastEmittedParticles { get; private set; }
    public int LastEmittedFlames { get; private set; }
    public float LastBurstCountScale { get; private set; }
    public float LastBurstSizeScale { get; private set; }
    public bool IsDiveFlameActive { get; private set; }
    public int DiveFlameCount { get; private set; }
    public int LaunchBlastFlames { get; private set; }
    public int NozzleWashFlames { get; private set; }
    public int SideJetFlames { get; private set; }

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly Vector2[] ChevronDirections =
    {
        new Vector2(1f, 0f), new Vector2(0.7071f, 0.7071f), new Vector2(0f, 1f), new Vector2(-0.7071f, 0.7071f),
        new Vector2(-1f, 0f), new Vector2(-0.7071f, -0.7071f), new Vector2(0f, -1f), new Vector2(0.7071f, -0.7071f)
    };

    private struct GlowBase { public float intensity; public Color color; }
    private readonly Dictionary<Material, GlowBase> glowBases = new Dictionary<Material, GlowBase>();
    private MaterialPropertyBlock block;
    private SpriteRenderer[] chevrons = new SpriteRenderer[0];
    private float[] chevronDistance = new float[0];
    private ParticleSystem.MinMaxCurve[] burstSizes = new ParticleSystem.MinMaxCurve[0];
    private bool[] burstFired = new bool[0];
    private bool[] flameFired = new bool[0];
    private Vector3 columnScale = Vector3.one;
    private float warningAge, pulsePhase, wavePosition;
    private float streakAge = -1f, blastAge = -1f;
    private Vector2 streakBottom, blastImpactPoint, lastDivePosition;
    private float streakHeight;
    private bool initialized;

    private void Awake() => EnsureInitialized();

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        block = new MaterialPropertyBlock();
        if (landingColumn != null) columnScale = landingColumn.localScale;

        // 화살표는 가장 긴 방향을 채울 수만큼 미리 만들어 두고 범위에 맞춰 켜고 끈다.
        if (chevronTemplate != null)
        {
            int total = ChevronDirections.Length * Mathf.Max(1, maxChevronsPerDirection);
            chevrons = new SpriteRenderer[total];
            chevronDistance = new float[total];
            for (int i = 0; i < total; i++)
            {
                var copy = Instantiate(chevronTemplate, chevronTemplate.transform.parent);
                copy.name = "Chevron" + i;
                copy.gameObject.SetActive(true);
                copy.enabled = false;
                chevrons[i] = copy;
            }
            chevronTemplate.gameObject.SetActive(false);
        }

        burstSizes = new ParticleSystem.MinMaxCurve[bursts.Length];
        burstFired = new bool[bursts.Length];
        for (int i = 0; i < bursts.Length; i++)
            if (bursts[i].system != null) burstSizes[i] = bursts[i].system.main.startSize;
        flameFired = new bool[flameWaves.Length];

        SetWarningVisible(false);
        if (streak != null) streak.enabled = false;
        if (blastFlash != null) blastFlash.enabled = false;
    }

    // ===== 예고 =====
    public void BeginWarning(Rect area, Vector2 landingPoint)
    {
        EnsureInitialized();
        // 폭발 잔상이 남아 있어도 새 예고의 테두리가 제자리에서 시작하게 한다.
        if (blastAge >= 0f && blastFlash != null) blastFlash.enabled = false;
        blastAge = -1f;
        WarningArea = area;
        LandingPoint = landingPoint;
        warningAge = pulsePhase = wavePosition = 0f;
        IsWarningVisible = true;
        SetWarningVisible(true);
        Layout(area, 0f, true);
        PlaceLanding();
        UpdateWarning(1f, 1f);
    }

    public void SetLandingPoint(Vector2 landingPoint)
    {
        LandingPoint = landingPoint;
        if (IsWarningVisible) PlaceLanding();
    }

    // 예고가 끝날 때까지 남은 시간으로 밝기·맥동·화살표 물결을 진행한다. 보이는 동안 매 프레임 부른다.
    public void UpdateWarning(float remaining, float total)
    {
        if (!IsWarningVisible) return;
        float dt = Time.deltaTime;
        warningAge += dt;
        WarningProgress = total > 0.0001f ? 1f - Mathf.Clamp01(remaining / total) : 1f;

        WarningPulseHz = Mathf.Lerp(pulseRate.x, pulseRate.y, WarningProgress * WarningProgress);
        pulsePhase = Mathf.Repeat(pulsePhase + WarningPulseHz * dt, 1f);
        float pulse = 1f - pulseDepth * (0.5f - 0.5f * Mathf.Cos(pulsePhase * 2f * Mathf.PI));
        WarningFlash = flashTime > 0f ? Mathf.Clamp01(1f - Mathf.Max(0f, remaining) / flashTime) : 0f;
        float appear = Mathf.Clamp01(warningAge / appearDuration);
        float level = Mathf.Lerp(minIntensity, 1f, WarningProgress);
        WarningIntensity = level * Mathf.Lerp(pulse, 1f, WarningFlash) * appear;

        // 바탕은 면적이 넓어 맥동을 약하게 받는다. 테두리·모서리·표식이 박자를 이끈다.
        Apply(fill, level * Mathf.Lerp(0.5f + 0.5f * pulse, 1.6f, WarningFlash) * appear, WarningFlash * flashAmount * 0.5f);
        foreach (var border in borders) Apply(border, WarningIntensity, WarningFlash * flashAmount);
        foreach (var corner in corners) Apply(corner, WarningIntensity, WarningFlash * flashAmount);
        if (landingRenderers != null)
            foreach (var renderer in landingRenderers) Apply(renderer, WarningIntensity, WarningFlash * flashAmount);

        // 화살표: 가운데에서 밖으로 밝기 물결이 번져 사방이 위험하다는 것을 알린다.
        wavePosition += Mathf.Lerp(chevronWaveSpeed.x, chevronWaveSpeed.y, WarningProgress) * dt;
        for (int i = 0; i < chevrons.Length; i++)
        {
            var chevron = chevrons[i];
            if (chevron == null || !chevron.enabled) continue;
            float phase = (chevronDistance[i] - wavePosition) / chevronWaveLength;
            float wave = 0.5f + 0.5f * Mathf.Cos(phase * 2f * Mathf.PI);
            float brightness = Mathf.Lerp(0.22f + 0.78f * wave * wave, 1f, WarningFlash);
            Apply(chevron, level * brightness * appear, WarningFlash * flashAmount);
        }

        if (landingColumn != null)
        {
            float grow = EaseOut(Mathf.Clamp01(warningAge / columnGrowDuration));
            landingColumn.localScale = new Vector3(columnScale.x, columnScale.y * Mathf.Lerp(0.2f, 1f, grow), columnScale.z);
        }
    }

    public void EndWarning()
    {
        IsWarningVisible = false;
        SetWarningVisible(false);
    }

    // 범위에 맞춰 바탕·테두리·모서리를 놓는다. push는 테두리를 밖으로 민 거리다. 화살표는 예고를 시작할 때만 놓는다.
    private void Layout(Rect area, float push, bool placeChevrons)
    {
        Vector3 center = new Vector3(area.center.x, area.center.y, transform.position.z);
        transform.SetPositionAndRotation(center, Quaternion.identity);
        float halfWidth = area.width * 0.5f, halfHeight = area.height * 0.5f;

        if (fill != null)
        {
            fill.transform.localPosition = Vector3.zero;
            fill.size = area.size;
        }
        float thickness = borderThickness;
        float outerX = halfWidth + push, outerY = halfHeight + push;
        SetBorder(0, new Vector2(0f, -outerY + thickness * 0.5f), new Vector2(outerX * 2f, thickness));
        SetBorder(1, new Vector2(0f, outerY - thickness * 0.5f), new Vector2(outerX * 2f, thickness));
        SetBorder(2, new Vector2(-outerX + thickness * 0.5f, 0f), new Vector2(thickness, outerY * 2f));
        SetBorder(3, new Vector2(outerX - thickness * 0.5f, 0f), new Vector2(thickness, outerY * 2f));
        SetCorner(0, new Vector2(-outerX, -outerY), 0f);
        SetCorner(1, new Vector2(outerX, -outerY), 90f);
        SetCorner(2, new Vector2(outerX, outerY), 180f);
        SetCorner(3, new Vector2(-outerX, outerY), 270f);
        if (!placeChevrons) return;

        ActiveChevronCount = 0;
        int perDirection = Mathf.Max(1, maxChevronsPerDirection);
        for (int d = 0; d < ChevronDirections.Length; d++)
        {
            Vector2 direction = ChevronDirections[d];
            // 가운데에서 이 방향으로 범위 경계까지의 거리
            float reachX = Mathf.Abs(direction.x) > 0.001f ? halfWidth / Mathf.Abs(direction.x) : float.PositiveInfinity;
            float reachY = Mathf.Abs(direction.y) > 0.001f ? halfHeight / Mathf.Abs(direction.y) : float.PositiveInfinity;
            float reach = Mathf.Min(reachX, reachY) - chevronEdgeMargin;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            for (int n = 0; n < perDirection; n++)
            {
                int index = d * perDirection + n;
                if (index >= chevrons.Length || chevrons[index] == null) continue;
                float distance = chevronStartDistance + n * chevronSpacing;
                bool visible = distance <= reach;
                chevrons[index].enabled = visible;
                if (!visible) continue;
                chevronDistance[index] = distance;
                chevrons[index].transform.localPosition = new Vector3(direction.x * distance, direction.y * distance, 0f);
                chevrons[index].transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                ActiveChevronCount++;
            }
        }
    }

    private void SetBorder(int index, Vector2 position, Vector2 size)
    {
        if (index >= borders.Length || borders[index] == null) return;
        borders[index].transform.localPosition = position;
        borders[index].transform.localScale = new Vector3(size.x, size.y, 1f);
    }

    private void SetCorner(int index, Vector2 position, float angle)
    {
        if (index >= corners.Length || corners[index] == null) return;
        corners[index].transform.localPosition = position;
        corners[index].transform.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void PlaceLanding()
    {
        if (landingMark == null) return;
        landingMark.SetPositionAndRotation(new Vector3(LandingPoint.x, LandingPoint.y, landingMark.position.z), Quaternion.identity);
    }

    private void SetWarningVisible(bool visible)
    {
        if (fill != null) fill.enabled = visible;
        foreach (var border in borders) if (border != null) border.enabled = visible;
        foreach (var corner in corners) if (corner != null) corner.enabled = visible;
        if (landingRenderers != null)
            foreach (var renderer in landingRenderers) if (renderer != null) renderer.enabled = visible;
        if (!visible)
        {
            foreach (var chevron in chevrons) if (chevron != null) chevron.enabled = false;
            ActiveChevronCount = 0;
        }
    }

    // ===== 속도선 =====
    // bottom에서 위로 height만큼 뻗는 빛줄기를 잠깐 보인다. 솟구칠 때는 위쪽이, 내려찍을 때(descending)는 아래쪽이 밝다.
    public void PlayStreak(Vector2 bottom, float height, bool descending)
    {
        EnsureInitialized();
        if (streak == null) return;
        streakBottom = bottom;
        streakHeight = Mathf.Max(0.5f, height);
        streakAge = 0f;
        streak.flipY = descending;
        streak.enabled = true;
        UpdateStreak();
    }

    private void UpdateStreak()
    {
        float t = Mathf.Clamp01(streakAge / streakDuration);
        float fade = (1f - t) * (1f - t);
        // Sprite의 피벗은 가운데다.
        streak.transform.SetPositionAndRotation(
            new Vector3(streakBottom.x, streakBottom.y + streakHeight * 0.5f, streak.transform.position.z), Quaternion.identity);
        // 굵게 나타났다가 가늘어지며 사라진다.
        streak.transform.localScale = new Vector3(streakWidth * Mathf.Lerp(1f, 0.25f, t), streakHeight, 1f);
        Apply(streak, fade, 0f);
    }

    // ===== 화염 =====
    // root(아래 끝)에서 위로 타오르는 화염 한 덩이를 세 층으로 방출한다.
    private void EmitFlame(Vector2 root, float width, float length, Vector2 velocity, float lifetime, int embers)
    {
        Vector2 center = root + Vector2.up * (length * 0.45f);
        EmitLayer(flameOuter, center, width * 1.3f, length * 1.12f, velocity * 0.92f, lifetime * 1.15f);
        EmitLayer(flameBody, center, width, length, velocity, lifetime);
        EmitLayer(flameCore, root + Vector2.up * (length * 0.3f), width * 0.58f, length * 0.68f, velocity * 1.05f, lifetime * 0.85f);
        LastEmittedFlames++;
        if (flameEmbers == null) return;
        for (int i = 0; i < embers; i++)
        {
            // 화염에서 튀어 오르는 불씨
            var ember = new Vector2(root.x + Random.Range(-width, width) * 0.5f, root.y + Random.Range(0f, length * 0.5f));
            EmitLayer(flameEmbers, ember, Random.Range(0.06f, 0.1f), Random.Range(0.22f, 0.4f),
                velocity * 0.6f + new Vector2(Random.Range(-3.5f, 3.5f), Random.Range(6f, 15f)), Random.Range(0.4f, 0.9f));
        }
    }

    // 화염 텍스처는 뿌리가 위, 혀끝이 아래다(분사용). 위로 타오르게 180도 돌려 그린다.
    // rotation은 입자 회전(도)이고 시계 방향이 양수다. 0이면 혀끝이 아래, 180이면 위, -90이면 오른쪽을 향한다.
    private static void EmitLayer(ParticleSystem system, Vector2 position, float width, float length, Vector2 velocity, float lifetime,
        float rotation = 180f)
    {
        if (system == null) return;
        var emit = new ParticleSystem.EmitParams
        {
            position = new Vector3(position.x, position.y, system.transform.position.z),
            velocity = velocity,
            startLifetime = lifetime,
            startSize3D = new Vector3(width, length, 1f),
            rotation = rotation + Random.Range(-7f, 7f),
            applyShapeToPosition = false
        };
        system.Emit(emit, 1);
    }

    private static float Pick(Vector2 range) => Random.Range(range.x, range.y);

    // ===== 예열 =====
    // 바닥을 짚은 분사구에서 새어 나온 불길이 바닥을 따라 좌우로 깔린다. strength(0 ~ 1)가 클수록 크고 멀리 퍼진다.
    // 예열하는 동안 일정한 간격으로 부른다.
    public void EmitNozzleWash(Vector2 nozzleFloorPoint, float strength)
    {
        EnsureInitialized();
        strength = Mathf.Clamp01(strength);
        for (int side = -1; side <= 1; side += 2)
        {
            float scale = Random.Range(0.85f, 1.15f);
            EmitFlame(nozzleFloorPoint + Vector2.right * (side * Random.Range(0.1f, 0.3f)),
                Mathf.Lerp(washWidth.x, washWidth.y, strength) * scale, Mathf.Lerp(washLength.x, washLength.y, strength) * scale,
                new Vector2(side * Mathf.Lerp(washSpeed.x, washSpeed.y, strength) * Random.Range(0.7f, 1.2f), Random.Range(0.3f, 1.4f)),
                Random.Range(0.14f, 0.24f), Random.value < strength * 0.5f ? 1 : 0);
        }
        NozzleWashFlames += 2;
    }

    // ===== 자세 제어 분사 =====
    // origin에서 direction으로 뿜는 짧은 분사 불꽃 한 덩이. 분사하는 동안 매 프레임 부르면 본체에 붙어 따라온다.
    public void EmitSideJet(Vector2 origin, Vector2 direction)
    {
        EnsureInitialized();
        if (direction.sqrMagnitude < 0.0001f) return;
        direction.Normalize();
        // 화염 그림의 혀끝이 분사 방향을 향하게 돌린다.
        float rotation = -(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f);
        float width = Pick(jetWidth), length = Pick(jetLength), life = Random.Range(0.07f, 0.12f);
        Vector2 velocity = direction * Pick(jetSpeed);
        // 입자 자리는 그림의 가운데다. 뿌리가 분사구에 놓이도록 길이의 절반쯤 내보낸다.
        Vector2 center = origin + direction * (length * 0.45f);
        EmitLayer(flameOuter, center, width * 1.3f, length * 1.12f, velocity * 0.92f, life * 1.15f, rotation);
        EmitLayer(flameBody, center, width, length, velocity, life, rotation);
        EmitLayer(flameCore, origin + direction * (length * 0.3f), width * 0.58f, length * 0.68f, velocity * 1.05f, life * 0.85f, rotation);
        if (flameEmbers != null && Random.value < 0.7f)
            EmitLayer(flameEmbers, origin, Random.Range(0.05f, 0.09f), Random.Range(0.2f, 0.36f),
                direction * Random.Range(10f, 18f) + Random.insideUnitCircle * 2f, Random.Range(0.2f, 0.4f), rotation);
        SideJetFlames++;
    }

    // ===== 도약 분사 =====
    // 솟구치는 순간 두 추진기의 분사가 바닥을 때린다. 분사구 아래에서 불기둥이 솟고, 불길이 바닥을 따라 좌우로 퍼진다.
    // floorPoint는 몸 아랫면 가운데의 바닥 접점, nozzleOffset은 가운데에서 분사구까지의 가로 거리(칸)다.
    public void EmitLaunchBlast(Vector2 floorPoint, float nozzleOffset)
    {
        EnsureInitialized();
        int before = LastEmittedFlames;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 nozzle = floorPoint + Vector2.right * (side * nozzleOffset);
            for (int i = 0; i < launchColumnFlames; i++)
                EmitFlame(nozzle + Vector2.right * Random.Range(-0.25f, 0.25f), Pick(launchColumnWidth), Pick(launchColumnLength),
                    new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(7f, 13f)), Random.Range(0.26f, 0.42f), 1);
            for (int i = 0; i < launchFloorFlames; i++)
            {
                // 바깥쪽으로 더 많이 퍼지고, 셋 중 하나는 안쪽으로 밀려 두 불길이 가운데에서 만난다.
                float direction = i % 3 == 2 ? -side : side;
                EmitFlame(nozzle + Vector2.right * (direction * Random.Range(0f, 0.5f)), Pick(launchFloorWidth), Pick(launchFloorLength),
                    new Vector2(direction * Pick(launchFloorSpeed), Random.Range(0.5f, 3f)), Random.Range(0.24f, 0.42f), 1);
            }
        }
        LaunchBlastFlames = LastEmittedFlames - before;
    }

    // ===== 낙하 화염 =====
    // 내려찍는 몸의 가운데를 매 프레임 알려 준다. 지나온 길에 화염 꼬리를 고르게 놓아 빠르게 내려와도 끊기지 않는다.
    public void BeginDiveFlame(Vector2 bodyCenter)
    {
        EnsureInitialized();
        IsDiveFlameActive = true;
        DiveFlameCount = 0;
        lastDivePosition = bodyCenter;
        EmitDiveHead(bodyCenter);
    }

    public void UpdateDiveFlame(Vector2 bodyCenter)
    {
        if (!IsDiveFlameActive) return;
        float distance = Vector2.Distance(bodyCenter, lastDivePosition);
        int count = Mathf.Min(48, Mathf.FloorToInt(distance / diveFlameSpacing));
        for (int i = 1; i <= count; i++)
        {
            Vector2 point = Vector2.Lerp(lastDivePosition, bodyCenter, i / (float)count);
            // 몸 뒤(위)로 뻗는 꼬리. 조금 따라 내려오다 사그라든다.
            EmitFlame(point + new Vector2(Random.Range(-0.35f, 0.35f), 0.2f), Pick(diveFlameWidth), Pick(diveFlameLength),
                new Vector2(Random.Range(-0.8f, 0.8f), -diveFlameFollowSpeed), Pick(diveFlameLifetime), 1);
            DiveFlameCount++;
        }
        if (count > 0) lastDivePosition = bodyCenter;
        EmitDiveHead(bodyCenter);
    }

    public void EndDiveFlame() => IsDiveFlameActive = false;

    // 몸을 감싸는 불덩이. 프레임마다 짧게 방출해 몸에 붙어 보이게 한다.
    private void EmitDiveHead(Vector2 bodyCenter)
    {
        Vector2 root = bodyCenter + Vector2.down * (diveHeadSize.y * 0.28f);
        Vector2 follow = Vector2.down * (diveFlameFollowSpeed * 2f);
        EmitLayer(flameOuter, root + Vector2.up * (diveHeadSize.y * 0.5f), diveHeadSize.x * 1.15f, diveHeadSize.y * 1.1f, follow, 0.09f);
        EmitLayer(flameBody, root + Vector2.up * (diveHeadSize.y * 0.45f), diveHeadSize.x, diveHeadSize.y, follow, 0.08f);
        EmitLayer(flameCore, root + Vector2.up * (diveHeadSize.y * 0.36f), diveHeadSize.x * 0.62f, diveHeadSize.y * 0.7f, follow, 0.07f);
    }

    // ===== 폭발 =====
    public void Explode(Rect area, Vector2 impactPoint)
    {
        EnsureInitialized();
        EndWarning();
        EndDiveFlame();
        LastBlastArea = area;
        blastImpactPoint = impactPoint;
        blastAge = 0f;
        BlastCount++;
        LastEmittedParticles = 0;
        LastEmittedFlames = 0;
        Layout(area, 0f, false);

        // 범위가 넓을수록 입자를 늘리되, 수는 상한까지만 늘리고 나머지는 입자 크기로 채운다.
        float areaRatio = area.width * area.height / burstReferenceArea;
        LastBurstCountScale = Mathf.Clamp(areaRatio, 0.3f, maxBurstCountScale);
        LastBurstSizeScale = Mathf.Clamp(Mathf.Sqrt(areaRatio / LastBurstCountScale), 1f, maxBurstSizeScale);
        for (int i = 0; i < burstFired.Length; i++) burstFired[i] = false;
        for (int i = 0; i < flameFired.Length; i++) flameFired[i] = false;
        if (blastFlash != null)
        {
            blastFlash.enabled = true;
            blastFlash.transform.localPosition = Vector3.zero;
            blastFlash.transform.localScale = new Vector3(area.width, area.height, 1f);
        }
        foreach (var border in borders) if (border != null) border.enabled = true;
        foreach (var corner in corners) if (corner != null) corner.enabled = true;
        UpdateBlast();
    }

    private void UpdateBlast()
    {
        Rect area = LastBlastArea;
        for (int i = 0; i < bursts.Length; i++)
        {
            if (burstFired[i] || blastAge < bursts[i].delay) continue;
            burstFired[i] = true;
            var system = bursts[i].system;
            if (system == null) continue;
            var shape = system.shape;
            var main = system.main;
            int count;
            float z = system.transform.position.z;
            switch (bursts[i].placement)
            {
                case BurstPlacement.FloorLine:
                    // 범위 폭만큼의 바닥선에서 솟는다(범위 가운데 x, 착지 바닥 높이).
                    system.transform.position = new Vector3(area.center.x, blastImpactPoint.y, z);
                    shape.scale = new Vector3(area.width, shape.scale.y, shape.scale.z);
                    count = Mathf.RoundToInt(bursts[i].count * area.width);
                    break;
                case BurstPlacement.ImpactPoint:
                    system.transform.position = new Vector3(blastImpactPoint.x, blastImpactPoint.y, z);
                    count = Mathf.RoundToInt(bursts[i].count);
                    break;
                default:
                    system.transform.position = new Vector3(area.center.x, area.center.y, z);
                    shape.scale = new Vector3(area.width, area.height, 1f);
                    main.startSize = Scaled(burstSizes[i], LastBurstSizeScale);
                    count = Mathf.RoundToInt(bursts[i].count * LastBurstCountScale);
                    break;
            }
            count = Mathf.Clamp(count, 0, main.maxParticles);
            if (count <= 0) continue;
            system.Emit(count);
            LastEmittedParticles += count;
        }

        // 화염: 범위 전체와 바닥선, 착지점에서 치솟는다. 뒤쪽 무리일수록 낮아져 잔불로 남는다.
        for (int i = 0; i < flameWaves.Length; i++)
        {
            if (flameFired[i] || blastAge < flameWaves[i].delay) continue;
            flameFired[i] = true;
            EmitFlameWave(flameWaves[i], area);
        }

        // 범위 전체의 빛: 아주 짧게 번쩍인 뒤 주황 잔광이 천천히 사라진다.
        if (blastFlash != null && blastFlash.enabled)
        {
            float t = Mathf.Clamp01(blastAge / blastFlashDuration);
            Apply(blastFlash, 0.8f * Mathf.Pow(1f - t, 7f) + 0.2f * Mathf.Pow(1f - t, 1.5f), 0.55f * Mathf.Pow(1f - t, 9f));
            if (t >= 1f) blastFlash.enabled = false;
        }

        // 테두리는 밖으로 밀려 나가며 사라진다.
        float push = Mathf.Clamp01(blastAge / blastBorderDuration);
        if (push < 1f)
        {
            Layout(area, blastBorderPush * EaseOut(push), false);
            float fade = (1f - push) * (1f - push);
            foreach (var border in borders) Apply(border, 1.6f * fade, 0.8f * fade);
            foreach (var corner in corners) Apply(corner, 1.6f * fade, 0.8f * fade);
        }
        else
        {
            foreach (var border in borders) if (border != null) border.enabled = false;
            foreach (var corner in corners) if (corner != null) corner.enabled = false;
        }
    }

    private void EmitFlameWave(FlameWave wave, Rect area)
    {
        float amount = wave.placement == BurstPlacement.Area ? wave.amount * area.width * area.height
            : wave.placement == BurstPlacement.FloorLine ? wave.amount * area.width : wave.amount;
        int count = Mathf.Clamp(Mathf.RoundToInt(amount), 0, maxFlamesPerWave);
        for (int i = 0; i < count; i++)
        {
            float length = Pick(wave.length);
            Vector2 root;
            switch (wave.placement)
            {
                case BurstPlacement.FloorLine:
                    root = new Vector2(Random.Range(area.xMin, area.xMax), blastImpactPoint.y);
                    break;
                case BurstPlacement.ImpactPoint:
                    root = new Vector2(blastImpactPoint.x + Random.Range(-wave.spread, wave.spread), blastImpactPoint.y);
                    break;
                default:
                    root = new Vector2(Random.Range(area.xMin, area.xMax), Random.Range(area.yMin, area.yMax));
                    break;
            }
            EmitFlame(root, Pick(wave.width), length, new Vector2(Random.Range(-1.2f, 1.2f), Pick(wave.riseSpeed)), Pick(wave.lifetime),
                i % 2 == 0 ? 1 : 0);
        }
    }

    private bool BlastFinished()
    {
        if (blastAge < Mathf.Max(blastFlashDuration, blastBorderDuration)) return false;
        for (int i = 0; i < burstFired.Length; i++)
            if (!burstFired[i]) return false;
        for (int i = 0; i < flameFired.Length; i++)
            if (!flameFired[i]) return false;
        return true;
    }

    private void Update()
    {
        if (streakAge >= 0f)
        {
            streakAge += Time.deltaTime;
            if (streakAge >= streakDuration)
            {
                streakAge = -1f;
                if (streak != null) streak.enabled = false;
            }
            else UpdateStreak();
        }
        if (blastAge >= 0f)
        {
            blastAge += Time.deltaTime;
            UpdateBlast();
            if (BlastFinished()) blastAge = -1f;
        }
    }

    // 예고·속도선·낙하 화염을 바로 거둔다(패턴 중단). 이미 터진 폭발과 방출된 화염은 제 수명대로 사라진다.
    public void HideAll()
    {
        if (!initialized) return;
        EndWarning();
        EndDiveFlame();
        streakAge = -1f;
        if (streak != null) streak.enabled = false;
        if (blastAge < 0f)
        {
            foreach (var border in borders) if (border != null) border.enabled = false;
            foreach (var corner in corners) if (corner != null) corner.enabled = false;
        }
    }

    private void OnDisable()
    {
        if (!initialized) return;
        HideAll();
        blastAge = -1f;
        if (blastFlash != null) blastFlash.enabled = false;
        foreach (var border in borders) if (border != null) border.enabled = false;
        foreach (var corner in corners) if (corner != null) corner.enabled = false;
    }

    // Material의 기본 발광값에 배율을 곱하고, whiten만큼 흰빛(flashColor)으로 섞는다.
    private void Apply(SpriteRenderer renderer, float intensity, float whiten)
    {
        if (renderer == null) return;
        var material = renderer.sharedMaterial;
        var glow = new GlowBase { intensity = 1f, color = Color.white };
        if (material != null && !glowBases.TryGetValue(material, out glow))
        {
            glow.intensity = material.HasProperty(IntensityId) ? material.GetFloat(IntensityId) : 1f;
            glow.color = material.HasProperty(EmissionColorId) ? material.GetColor(EmissionColorId) : Color.white;
            glowBases.Add(material, glow);
        }
        renderer.GetPropertyBlock(block);
        block.SetFloat(IntensityId, glow.intensity * Mathf.Max(0f, intensity));
        block.SetColor(EmissionColorId, Color.Lerp(glow.color, flashColor, Mathf.Clamp01(whiten)));
        renderer.SetPropertyBlock(block);
    }

    private static ParticleSystem.MinMaxCurve Scaled(ParticleSystem.MinMaxCurve curve, float scale)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                return new ParticleSystem.MinMaxCurve(curve.constant * scale);
            case ParticleSystemCurveMode.TwoConstants:
                return new ParticleSystem.MinMaxCurve(curve.constantMin * scale, curve.constantMax * scale);
            default:
                curve.curveMultiplier *= scale;
                return curve;
        }
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
}
