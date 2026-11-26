using System;
using System.Collections.Generic;
using UnityEngine;

// 분쇄기 투입구(N03, 함정). 바닥에 판 구덩이 안에서 추격 분쇄기와 같은 분쇄 날이 돈다.
// - Player: 구덩이 안(판정 상자)에 몸이 닿으면 WaveTrapDamage 경로로 피해(시작 위치 복귀는 쓰지 않는다 — 던전 방은 Wave 시작 좌표와 다르다).
// - 적: 몸 가운데가 구덩이 윗변보다 shredDepth 이상 내려오면 갈아 처치한다(ChasingGrinder.Shred와 같은 처리 — 피해가 아니라 Wave·던전이 건
//   사망 처리(OnDeath)를 부르고 아직 풀로 돌아가지 않았으면 EnemyPool.Despawn). 보스로 나온 적·등장 중인 적은 갈지 않는다.
//   그래서 발판에서 떨어진 적이 구덩이에 남아 방이 잠긴 채 남지 않는다(T05 분쇄기 낙하 갱도).
// - 판정은 루트 RaycastMotor2D.OverlapAttackBox(Player + Enemy 레이어)만 쓴다. 맞닿은 이웃 칸이 겹침으로 잡히지 않게 좌우·위를 조금 줄여 묻는다.
// - 폭(4~14칸)은 배치별 설정이다. 왼쪽 끝은 footprint 왼쪽 변에 고정되고 오른쪽으로 늘어난다.
// - 6차 T15: 보이는 몸체는 추격 분쇄기 몸체(ChasingGrinderBody — 같은 Mesh·분쇄 날 Animation ChasingGrinder_Run·불꽃)를 빌려 쓴다
//   (Rent/Release, 폭은 ChasingGrinderBody.Fit — 늘이지 않고 다시 그림). 몸체는 '고정 모드'로 둔다: 어둠·몸체·소켓 묶음을 끄고 날 띠만
//   지형 뒤에 그려 구덩이 아래로 나가는 부분은 바닥 지형이 가린다. 화면 떨림은 내지 않는다(추격 분쇄기만 낸다). 그래서 최소 폭은 4칸(몸체 최소 폭).
//   Prefab의 자식 EditorPreview(EditorOnly)는 격자 Tool 미리보기 그림이다(실행 중에는 WaveEnvironmentTool이 끈다).
// - 6차 T9(방 클리어 정지, IDungeonRoomHazard): 정지 신호를 받은 호출에서 판정(Player·적)을 끄고, 정전 물결 차례가 오면 분쇄 날을 감속해 세우며
//   (SetDrive → 0) 몸체를 구덩이 아래로 가라앉히고 불꽃·구동 루프음·날의 빨강을 끈다. 신호가 없으면(던전 밖) 늘 작동한다.
// - 소리(임시 기존 효과음): 구동 루프 ChasingGrinderLoop(거리 감쇠), 적 분쇄 ChasingGrinderEnemyFeedback, Player 피격 ChasingGrinderHitFeedback.
[DisallowMultipleComponent]
public sealed class PropShredder : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings, IDungeonRoomHazard
{
    public const string WidthKey = "shredderWidth";
    public const int MinWidth = 4, MaxWidth = 14;

    [Header("배치 기본값")]
    [SerializeField, Range(MinWidth, MaxWidth)] private int width = MinWidth;
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

    [Header("몸체(6차 T15 — 추격 분쇄기 몸체를 빌려 씀)")]
    [SerializeField, Tooltip("빌려 쓸 추격 분쇄기 몸체 Prefab(+Y가 나아가는 방향 = 구덩이 위).")] private ChasingGrinderBody bodyPrefab;
    [SerializeField, Min(0f), Tooltip("정지 때 몸체를 가라앉히는 깊이(칸). 날 띠가 구덩이 아래로 내려가 공격 모양이 아니게 된다.")] private float haltSinkDepth = 1.25f;
    [SerializeField, Min(.05f), Tooltip("정지 감속·가라앉음에 걸리는 시간(초).")] private float haltTime = .35f;

    [Header("효과(임시: 기존 효과음)")]
    [SerializeField] private FeedbackDataSO enemyFeedback;
    [SerializeField] private FeedbackDataSO hitFeedback;
    [SerializeField] private SoundOptions grindLoop;

    private readonly WaveTrapDamage trapDamage = new WaveTrapDamage();
    private readonly HashSet<PlayerHealth> damagedPlayers = new HashSet<PlayerHealth>();
    private readonly HashSet<EnemyController> shredded = new HashSet<EnemyController>();
    private readonly HazardLoopSound loop = new HazardLoopSound();
    private readonly HazardHalt halt = new HazardHalt();
    private Collider2D[] hits = new Collider2D[16];
    private RaycastMotor2D motor;
    private ChasingGrinderBody body;
    private float activeDamage;
    private int activeWidth;
    private float nextPlayerCheck;
    private bool cached;
    private bool live;

    public int Width => cached ? activeWidth : width;
    /// <summary>이번 대여에서 갈아 처치한 적 수(검사용).</summary>
    public int ShredCount { get; private set; }
    /// <summary>이번 대여에서 Player에 실제로 들어간 피해 횟수(검사용).</summary>
    public int PlayerHits { get; private set; }
    public EnemyController LastShredded { get; private set; }
    /// <summary>지금 빌려 쓰는 몸체(없으면 null, 검사용).</summary>
    public ChasingGrinderBody Body => body;
    /// <summary>방 클리어 정지 신호를 받아 판정이 꺼져 있다(6차 T9, 검사용).</summary>
    public bool IsHalted => halt.Halted;
    /// <summary>정지 외형 진행(0 작동 ~ 1 가라앉아 멈춤, 검사용).</summary>
    public float HaltBlend => halt.Blend;
    /// <summary>몸체 Prefab(입장 로딩 예열용).</summary>
    public ChasingGrinderBody BodyPrefab => bodyPrefab;
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
    }

    // ── 입장 로딩 예열(6차 T15) ─────────────────────────────

    /// <summary>
    /// 한 묶음(Wave 하나, 던전이면 방 하나)에 나오는 분쇄 투입구의 폭별 몸체 Mesh·부품과 같은 몸체 Prefab의 동시 수만큼 몸체를 미리 만든다
    /// (ChasingGrinder.WarmWave와 같은 방식 — 입장 로딩의 풀 예열이 부른다).
    /// </summary>
    public static void WarmWave(WaveSpawnInfo[] spawns)
    {
        if (spawns == null) return;
        var counts = new Dictionary<ChasingGrinderBody, int>();
        foreach (var info in spawns)
        {
            if (info == null || info.prefab == null || !info.prefab.TryGetComponent(out PropShredder shredder) || shredder.bodyPrefab == null) continue;
            int count = counts[shredder.bodyPrefab] = (counts.TryGetValue(shredder.bodyPrefab, out int n) ? n : 0) + 1;
            ChasingGrinderBody.Prewarm(shredder.bodyPrefab, WaveDeviceSettings.Value(info.deviceSettings, shredder.WidthParameter), count);
        }
    }

    // ── IDungeonPropPart ─────────────────────────────

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        live = true;
        halt.Reset();
        ShredCount = 0;
        PlayerHits = 0;
        LastShredded = null;
        nextPlayerCheck = 0f;
        trapDamage.Cancel();
        shredded.Clear();
        RentBody();
    }

    public void OnPropReturning(DungeonProp prop)
    {
        live = false;
        trapDamage.Cancel();
        loop.Stop();
        ReleaseBody();
        halt.Reset();
    }

    private void OnDisable()
    {
        loop.Stop();
        trapDamage.Cancel();
        // 풀 반환이 ForceReturnToPool이면 OnPropReturning이 먼저 몸체를 돌려준다. 방 이동의 즉시 반환(WorldEntityPool.Return)은 OnPropReturning 없이 꺼지고,
        // 꺼지는 중에는 계층을 바꿀 수 없으므로(OnDisable 중 SetParent 불가) 자식이 아닐 때만 여기서 돌려준다
        // (자식이면 함께 꺼졌다가 다음 대여의 RentBody가 작동 외형으로 되돌려 그대로 쓴다).
        if (Application.isPlaying && body != null && !body.transform.IsChildOf(transform)) ReleaseBody();
    }

    private void OnDestroy()
    {
        // 자식으로 둔 몸체는 이 개체와 함께 지워진다(몸체 목록은 ChasingGrinderBody가 빈 항목을 정리한다).
        if (body != null && body.transform.IsChildOf(transform)) { body = null; return; }
        ReleaseBody();
    }

    // ── 몸체(빌려 쓰기) ─────────────────────────────

    private void RentBody()
    {
        if (bodyPrefab == null || !Application.isPlaying) return;
        if (body == null)
        {
            body = ChasingGrinderBody.Rent(bodyPrefab, Mathf.Clamp(activeWidth, MinWidth, MaxWidth));
            if (body == null) return;
            // 빌린 동안 이 Prop의 자식으로 둔다: 방 보류(레버 컷씬 미리보기 — Prop 루트 아래 Renderer·Animator를 멈춤)가 몸체도 함께 숨기고 멈춘다.
            // 돌려줄 때(Release) 몸체 풀 묶음으로 되돌아간다.
            body.transform.SetParent(transform, true);
        }
        else body.Fit(Mathf.Clamp(activeWidth, MinWidth, MaxWidth));
        // 방 이동의 즉시 반환(DungeonRoomStage.Leave(true) → WorldEntityPool.Return)은 OnPropReturning을 거치지 않아 자식 몸체가
        // 지난 대여의 정지 외형(구동 0·빨강 꺼짐)을 지닌 채 남아 있을 수 있다 → 대여마다 작동 외형으로 되돌린다.
        body.SetFixedMode(true);
        body.SetDangerLights(true);
        body.SetDrive(1f);
        PlaceBody();
    }

    private void ReleaseBody()
    {
        if (body == null) return;
        var rented = body;
        body = null;
        if (rented != null) rented.Release();
    }

    // 분쇄선(날 끝)을 구덩이 윗변 가운데에, +Y를 구덩이 위로 둔다. 정지 중이면 그만큼 가라앉힌다.
    private void PlaceBody()
    {
        if (body == null) return;
        float span = Mathf.Clamp(activeWidth, MinWidth, MaxWidth);
        Vector2 edge = transform.TransformPoint(new Vector3(LeftLocal + span * .5f, TopLocal, 0f));
        body.PlaceFixed(edge, transform.up, haltSinkDepth * halt.Blend);
    }

    // ── 6차 T9: 방 클리어 정지 ─────────────────────────────

    public void SetHazardHalted(bool halted, bool instant)
    {
        bool changed = halt.Signal(halted, instant);
        if (halted)
        {
            trapDamage.Cancel();   // 판정은 이 호출에서 바로 끈다(FixedUpdate가 묻지 않는다)
            if (instant && live) ApplyHaltPose();
            return;
        }
        if (changed && live) ApplyHaltPose();   // 다시 작동: Blend가 0으로 돌아가는 동안 Update가 이어서 맞춘다
    }

    // 지금 Blend에 맞춰 몸체 구동·가라앉음·불꽃·빨강을 맞춘다.
    private void ApplyHaltPose()
    {
        if (body == null) return;
        float b = halt.Blend;
        body.SetDrive(1f - b);
        bool running = !halt.Halted;
        body.SetSparks(running, 1f, false);
        body.SetDangerLights(!(halt.Halted && b >= 1f));
        PlaceBody();
    }

    // ── 움직임·소리 ─────────────────────────────

    private void Update()
    {
        if (!live) return;
        if (body == null) RentBody();
        else PlaceBody();   // Prop을 옮겨도(검사·장면 배치) 몸체가 따라온다
        float level = 1f;
        if (halt.HasSignal && (halt.Halted || halt.Blend > 0f))
        {
            halt.Step(Time.deltaTime, haltTime, out _, out _);
            if (halt.VisualHalting || !halt.Halted) ApplyHaltPose();
            else if (body != null) body.SetSparks(true, 1f, false);   // 정전 물결 차례 전: 외형만 잠시 그대로
            level = halt.VisualHalting ? 1f - halt.Blend : 1f;
        }
        else if (body != null) body.SetSparks(true, 1f, false);
        Vector3 center = transform.TransformPoint(new Vector3(LeftLocal + activeWidth * .5f, 0f, 0f));
        loop.Update(grindLoop, center, level > .001f, level);
    }

    // ── 판정 ─────────────────────────────

    private void FixedUpdate()
    {
        if (!live || halt.Halted) return;
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
                    if (body != null) body.EmitContact(point, transform.up, .25f, 1f);
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
        if (body != null) body.EmitShred(point, transform.up, 1f);
        var health = enemy.Health;
        if (health != null && health.OnDeath != null) health.OnDeath.Invoke();
        // 걸어 둔 처리가 없거나 그 처리가 풀로 돌려보내지 않은 적은 여기서 돌려보낸다.
        if (enemy.gameObject.activeSelf && EnemyPool.Instance != null) EnemyPool.Instance.Despawn(enemy);
    }

    // ── 배치별 설정 ─────────────────────────────

    public WaveDeviceParameter WidthParameter => new WaveDeviceParameter(WidthKey, "폭", "투입구 폭(칸, 4~14)입니다. 왼쪽 끝은 그대로 두고 오른쪽으로 늘어납니다. 늘어난 칸에는 다른 것을 놓지 마세요.", "칸", MinWidth, MaxWidth, 1f, width, true);

    public WaveDeviceParameter[] PropParameters => new[] { WidthParameter, WaveTrapDamage.DamageParameter(damage) };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        int before = activeWidth;
        ReadSettings(overrides);
        // 몸체 폭은 실행 중 개체에만 바꾼다(편집기 미리보기·Prefab 에셋은 건드리지 않는다).
        if (Application.isPlaying && cached && live && body != null && before != activeWidth)
        {
            body.Fit(Mathf.Clamp(activeWidth, MinWidth, MaxWidth));
            PlaceBody();
        }
        if (Application.isPlaying) RaycastMotor2D.InvalidateAttackQuerySync();
    }

    private void ReadSettings(WaveDeviceSetting[] overrides)
    {
        // 6차 전 Prefab(기본 폭 2)이 남아 있어도 최소 폭 4(몸체 최소 폭)로 읽는다.
        activeWidth = Mathf.Clamp(Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, WidthParameter)), MinWidth, MaxWidth);
        activeDamage = WaveDeviceSettings.Value(overrides, WaveTrapDamage.DamageParameter(damage));
    }

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        int cellsWide = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, WidthParameter));
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, new Vector2(LeftLocal + cellsWide * .5f, 0f), new Vector2(cellsWide, pitHeight),
            new Color(1f, .22f, .16f, .9f));
        return true;
    }
}
