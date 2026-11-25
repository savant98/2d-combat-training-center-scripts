using System;
using System.Collections.Generic;
using UnityEngine;

// 분쇄기 투입구(N03, 함정). 바닥에 판 구덩이 안에서 맞물린 절단 롤러가 돈다.
// - Player: 구덩이 안(판정 상자)에 몸이 닿으면 WaveTrapDamage 경로로 피해(시작 위치 복귀는 쓰지 않는다 — 던전 방은 Wave 시작 좌표와 다르다).
// - 적: 몸 가운데가 구덩이 윗변보다 shredDepth 이상 내려오면 갈아 처치한다(ChasingGrinder.Shred와 같은 처리 — 피해가 아니라 Wave·던전이 건
//   사망 처리(OnDeath)를 부르고 아직 풀로 돌아가지 않았으면 EnemyPool.Despawn). 보스로 나온 적·등장 중인 적은 갈지 않는다.
//   그래서 발판에서 떨어진 적이 구덩이에 남아 방이 잠긴 채 남지 않는다(T05 분쇄기 낙하 갱도).
// - 판정은 루트 RaycastMotor2D.OverlapAttackBox(Player + Enemy 레이어)만 쓴다. 맞닿은 이웃 칸이 겹침으로 잡히지 않게 좌우·위를 조금 줄여 묻는다.
// - 폭(2~14칸)은 배치별 설정이다. 왼쪽 끝은 footprint 왼쪽 변에 고정되고 오른쪽으로 늘어난다. 표시는 생성 도구가 만든 한 칸 조각(Cell)을
//   실행 중에 복제해 칸마다 놓는다(크기 늘이기 없음). 절단 롤러는 칸마다 서로 반대로 돌고, 하우징 묶음이 작게 떨린다(이동만).
// - 소리(임시 기존 효과음): 구동 루프 ChasingGrinderLoop(거리 감쇠), 적 분쇄 ChasingGrinderEnemyFeedback, Player 피격 ChasingGrinderHitFeedback.
[DisallowMultipleComponent]
public sealed class PropShredder : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings
{
    public const string WidthKey = "shredderWidth";
    public const int MinWidth = 2, MaxWidth = 14;

    [Header("배치 기본값")]
    [SerializeField, Range(MinWidth, MaxWidth)] private int width = 2;
    [SerializeField, Min(0f)] private float damage = 15f;

    [Header("판정")]
    [SerializeField, Min(1), Tooltip("footprint 가로 칸 수(왼쪽 끝 기준점).")] private int footprintCells = 2;
    [SerializeField, Min(.5f), Tooltip("구덩이 깊이(칸) = footprint 세로.")] private float pitHeight = 2f;
    [SerializeField, Min(0f), Tooltip("좌우 이웃 칸이 맞닿음으로 잡히지 않게 줄이는 폭(칸).")] private float edgeInset = .06f;
    [SerializeField, Min(0f), Tooltip("구덩이 윗변을 스치기만 한 몸은 맞지 않게 위를 줄이는 폭(칸).")] private float topInset = .12f;
    [SerializeField, Min(0f), Tooltip("적 몸 가운데가 윗변보다 이만큼 내려오면 갈린다(칸).")] private float shredDepth = .35f;
    [SerializeField, Min(.01f), Tooltip("Player 피해 판정 간격(초). 피격 무적이 연속 피해를 막는다.")] private float checkInterval = .1f;
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private LayerMask enemyMask;

    [Header("구성(생성 도구가 연결)")]
    [SerializeField, Tooltip("칸 조각들의 부모(떨림 대상).")] private Transform cellRoot;
    [SerializeField, Tooltip("복제할 칸 조각(Prefab의 첫 칸).")] private Transform cellTemplate;
    [SerializeField, Tooltip("Prefab에 미리 만든 칸 조각(기본 폭만큼).")] private Transform[] builtCells = Array.Empty<Transform>();
    [SerializeField, Tooltip("칸 조각 안의 절단 롤러 이름(위·아래 두 개).")] private string[] cutterNames = { "CutterLow", "CutterHigh" };
    [SerializeField, Tooltip("오른쪽 끝 덮개(폭에 맞춰 옮긴다).")] private Transform capRight;
    [SerializeField, Tooltip("큰 롤러 회전 속도(도/초). 작은 롤러는 반대로 1.6배.")] private float cutterSpeed = 160f;
    [SerializeField, Min(0f), Tooltip("하우징 떨림 폭(칸).")] private float vibration = .012f;
    [SerializeField, Tooltip("늘 조금씩 튀는 쇳조각(폭에 맞춰 상자 모양을 늘린다).")] private ParticleSystem chips;
    [SerializeField, Tooltip("분쇄 순간 터지는 불꽃.")] private ParticleSystem shredSparks;
    [SerializeField, Tooltip("분쇄 순간 튀는 폐자재 조각.")] private ParticleSystem shredDebris;

    [Header("효과(임시: 기존 효과음)")]
    [SerializeField] private FeedbackDataSO enemyFeedback;
    [SerializeField] private FeedbackDataSO hitFeedback;
    [SerializeField] private SoundOptions grindLoop;

    private readonly WaveTrapDamage trapDamage = new WaveTrapDamage();
    private readonly HashSet<PlayerHealth> damagedPlayers = new HashSet<PlayerHealth>();
    private readonly HashSet<EnemyController> shredded = new HashSet<EnemyController>();
    private readonly List<Transform> cells = new List<Transform>();
    private readonly List<Transform> cutters = new List<Transform>();
    private readonly List<float> cutterSigns = new List<float>();
    private readonly List<Quaternion> cutterHome = new List<Quaternion>();
    private readonly HazardLoopSound loop = new HazardLoopSound();
    private Collider2D[] hits = new Collider2D[16];
    private RaycastMotor2D motor;
    private Renderer[] probes = Array.Empty<Renderer>();
    private Vector3 cellRootHome, capRightHome, chipsHome;
    private float activeDamage;
    private int activeWidth;
    private float nextPlayerCheck;
    private float clock;
    private bool cached;
    private bool live;

    public int Width => cached ? activeWidth : width;
    /// <summary>이번 대여에서 갈아 처치한 적 수(검사용).</summary>
    public int ShredCount { get; private set; }
    /// <summary>이번 대여에서 Player에 실제로 들어간 피해 횟수(검사용).</summary>
    public int PlayerHits { get; private set; }
    public EnemyController LastShredded { get; private set; }
    public int CellCount => cells.Count;
    private float LeftLocal => -footprintCells * .5f;
    private float TopLocal => pitHeight * .5f;

    private void Awake()
    {
        Cache();
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();   // 정적 공격 질의만 쓴다
        if (playerMask.value == 0) playerMask = LayerMask.GetMask("Player");
        if (enemyMask.value == 0) enemyMask = LayerMask.GetMask("Enemy");
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        ReadSettings(WaveDeviceSettings.None);
        if (cellRoot != null) cellRootHome = cellRoot.localPosition;
        if (capRight != null) capRightHome = capRight.localPosition;
        if (chips != null) chipsHome = chips.transform.localPosition;
        cells.Clear();
        foreach (var cell in builtCells) if (cell != null) cells.Add(cell);
        if (cells.Count == 0 && cellTemplate != null) cells.Add(cellTemplate);
        CollectCutters();
    }

    private void CollectCutters()
    {
        cutters.Clear();
        cutterSigns.Clear();
        cutterHome.Clear();
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i] == null) continue;
            for (int k = 0; k < cutterNames.Length; k++)
            {
                var cutter = cells[i].Find(cutterNames[k]);
                if (cutter == null) continue;
                cutters.Add(cutter);
                // 이웃 칸·위아래 롤러가 서로 반대로 돌아 맞물린다. 위 롤러는 작아서 더 빨리 돈다.
                float sign = ((i + k) % 2 == 0 ? 1f : -1f) * (k == 0 ? 1f : 1.6f);
                cutterSigns.Add(sign);
                cutterHome.Add(cutter.localRotation);
            }
        }
    }

    // ── IDungeonPropPart ─────────────────────────────

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        live = true;
        ShredCount = 0;
        PlayerHits = 0;
        LastShredded = null;
        nextPlayerCheck = 0f;
        clock = 0f;
        trapDamage.Cancel();
        shredded.Clear();
        for (int i = 0; i < cutters.Count; i++) if (cutters[i] != null) cutters[i].localRotation = cutterHome[i];
        if (cellRoot != null) cellRoot.localPosition = cellRootHome;
        if (probes.Length == 0) probes = PropHazardKit.VisualRenderers(this);
        PropHazardKit.Stop(shredSparks, true);
        PropHazardKit.Stop(shredDebris, true);
        PropHazardKit.Play(chips);
    }

    public void OnPropReturning(DungeonProp prop)
    {
        live = false;
        trapDamage.Cancel();
        loop.Stop();
        PropHazardKit.Stop(chips, true);
    }

    private void OnDisable()
    {
        loop.Stop();
        trapDamage.Cancel();
    }

    // ── 움직임·소리 ─────────────────────────────

    private void Update()
    {
        if (!live) return;
        Vector3 center = transform.TransformPoint(new Vector3(LeftLocal + activeWidth * .5f, 0f, 0f));
        loop.Update(grindLoop, center, true);
        if (!PropHazardKit.AnyVisible(probes)) return;
        float dt = Time.deltaTime;
        clock += dt;
        for (int i = 0; i < cutters.Count; i++)
            if (cutters[i] != null) cutters[i].localRotation *= Quaternion.Euler(0f, 0f, -cutterSpeed * cutterSigns[i] * dt);
        if (cellRoot != null && vibration > 0f)
        {
            // 하우징 떨림: 서로 다른 두 주기의 작은 상하·좌우 이동(크기 변형 없음).
            float x = Mathf.Sin(clock * 53f) * .4f + Mathf.Sin(clock * 31f) * .6f;
            float y = Mathf.Sin(clock * 47f + 1.3f);
            cellRoot.localPosition = cellRootHome + new Vector3(x, y, 0f) * vibration;
        }
    }

    // ── 판정 ─────────────────────────────

    private void FixedUpdate()
    {
        if (!live) return;
        float left = LeftLocal + edgeInset, right = LeftLocal + activeWidth - edgeInset;
        float bottom = -pitHeight * .5f + .02f, top = TopLocal - topInset;
        if (right <= left || top <= bottom) return;
        Vector2 center = transform.TransformPoint(new Vector3((left + right) * .5f, (bottom + top) * .5f, 0f));
        var scale = transform.lossyScale;
        var size = new Vector2(Mathf.Abs((right - left) * scale.x), Mathf.Abs((top - bottom) * scale.y));
        bool checkPlayer = Time.time >= nextPlayerCheck && (activeDamage > 0f);
        int mask = enemyMask.value | (checkPlayer ? playerMask.value : 0);
        int count = PropHazardKit.OverlapBox(motor, center, size, transform.eulerAngles.z, ref hits, mask);
        if (checkPlayer) nextPlayerCheck = Time.time + checkInterval;
        damagedPlayers.Clear();
        shredded.Clear();
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            hits[i] = null;
            if (hit == null || !hit.gameObject.activeInHierarchy) continue;
            int layer = 1 << hit.gameObject.layer;
            if ((layer & playerMask.value) != 0)
            {
                if (!checkPlayer) continue;
                var player = hit.GetComponentInParent<PlayerHealth>();
                if (player == null || !damagedPlayers.Add(player)) continue;
                Vector2 point = hit.ClosestPoint(center);
                var info = new WeaponBaseSO.DamageInfo { damage = activeDamage, damageType = DamageType.Normal, knockbackOrigin = point };
                if (trapDamage.TryDamage(this, player, info, point, activeDamage, false))
                {
                    PlayerHits++;
                    PropHazardKit.PlayFeedback(hitFeedback, point);
                    PropHazardKit.Burst(shredSparks, 8, point);
                }
                if (!live) return;
            }
            else if ((layer & enemyMask.value) != 0)
            {
                var enemy = hit.GetComponentInParent<EnemyController>();
                if (enemy == null || !enemy.gameObject.activeInHierarchy || !shredded.Add(enemy)) continue;
                if (!CanShred(enemy)) continue;
                Vector2 core = hit.bounds.center;
                Vector3 local = transform.InverseTransformPoint(core);
                if (local.y > TopLocal - shredDepth) continue;   // 아직 날에 닿지 않았다(구덩이 위를 스침)
                Shred(enemy, core);
            }
        }
    }

    private static bool CanShred(EnemyController enemy)
    {
        var health = enemy.Health;
        if (health == null || health.currentHP <= 0f) return false;
        if (health.IsBossEnemy) return false;                       // 보스로 나온 적은 갈지 않는다
        if (DungeonPropTargeting.IsProp(health)) return false;
        if (EnemyPool.Instance != null && EnemyPool.Instance.IsSpawning(enemy)) return false;   // 등장 연출 중
        return true;
    }

    // 적을 갈아 없앤다. 피해로 쓰러뜨린 것이 아니므로 체력·훈련 기록은 건드리지 않고, Wave·던전이 걸어 둔 사망 처리만 부른다(ChasingGrinder.Shred와 같다).
    private void Shred(EnemyController enemy, Vector2 point)
    {
        ShredCount++;
        LastShredded = enemy;
        PropHazardKit.PlayFeedback(enemyFeedback, point);
        PropHazardKit.Burst(shredSparks, 18, point);
        PropHazardKit.Burst(shredDebris, 10, point);
        var health = enemy.Health;
        if (health != null && health.OnDeath != null) health.OnDeath.Invoke();
        // 걸어 둔 처리가 없거나 그 처리가 풀로 돌려보내지 않은 적은 여기서 돌려보낸다.
        if (enemy.gameObject.activeSelf && EnemyPool.Instance != null) EnemyPool.Instance.Despawn(enemy);
    }

    // ── 배치별 설정 ─────────────────────────────

    private WaveDeviceParameter WidthParameter => new WaveDeviceParameter(WidthKey, "폭", "투입구 폭(칸)입니다. 왼쪽 끝은 그대로 두고 오른쪽으로 늘어납니다. 늘어난 칸에는 다른 것을 놓지 마세요.", "칸", MinWidth, MaxWidth, 1f, width, true);

    public WaveDeviceParameter[] PropParameters => new[] { WidthParameter, WaveTrapDamage.DamageParameter(damage) };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        ReadSettings(overrides);
        // 표시 칸 수는 실행 중 개체에만 바꾼다(편집기 미리보기·Prefab 에셋은 건드리지 않는다).
        if (Application.isPlaying && cached) Rebuild();
    }

    private void ReadSettings(WaveDeviceSetting[] overrides)
    {
        activeWidth = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, WidthParameter));
        activeDamage = WaveDeviceSettings.Value(overrides, WaveTrapDamage.DamageParameter(damage));
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        int cellsWide = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, WidthParameter));
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, new Vector2(LeftLocal + cellsWide * .5f, 0f), new Vector2(cellsWide, pitHeight),
            new Color(1f, .22f, .16f, .9f));
        return true;
    }

    // 폭에 맞춰 칸 조각을 놓고(모자라면 첫 칸을 복제), 오른쪽 덮개·쇳조각 입자 상자를 옮긴다.
    private void Rebuild()
    {
        int wanted = Mathf.Clamp(activeWidth, MinWidth, MaxWidth);
        var template = cellTemplate != null ? cellTemplate : cells.Count > 0 ? cells[0] : null;
        bool changed = false;
        while (template != null && cells.Count < wanted)
        {
            var clone = Instantiate(template, template.parent, false);
            clone.name = template.name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') + cells.Count;
            cells.Add(clone);
            changed = true;
        }
        float left = LeftLocal;
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (cell == null) continue;
            bool on = i < wanted;
            if (cell.gameObject.activeSelf != on) cell.gameObject.SetActive(on);
            if (on) cell.localPosition = new Vector3(left + i + .5f, cell.localPosition.y, cell.localPosition.z);
        }
        if (changed) CollectCutters();
        if (capRight != null) capRight.localPosition = capRightHome + new Vector3(wanted - footprintCells, 0f, 0f);
        if (chips != null)
        {
            // 상자 가운데를 투입구 가운데로 옮기고 가로 폭만 맞춘다(입자 상자는 가로 = x).
            chips.transform.localPosition = new Vector3(left + wanted * .5f, chipsHome.y, chipsHome.z);
            var shape = chips.shape;
            shape.scale = new Vector3(Mathf.Max(.5f, wanted - .4f), shape.scale.y, shape.scale.z);
            var emission = chips.emission;
            emission.rateOverTime = 2.5f * wanted;
        }
        probes = PropHazardKit.VisualRenderers(this);
        RaycastMotor2D.InvalidateAttackQuerySync();
    }
}
