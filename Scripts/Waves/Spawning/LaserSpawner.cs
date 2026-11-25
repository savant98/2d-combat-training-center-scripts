
using System.Collections.Generic;
using UnityEngine;

public class LaserSpawner : ObjectSpawner, IWaveDeviceSettings
{
    [Header("Laser Settings")]
    [SerializeField] private WeaponBaseSO.DamageInfo laserDamageInfo;
    [SerializeField] private Vector2 direction = Vector2.down;
    [SerializeField, Tooltip("회전된 환경 도구의 로컬 방향으로 발사합니다. 기존 Tutorial은 월드 방향을 유지합니다.")]
    private bool useLocalDirection;
    [SerializeField, Tooltip("환경 도구가 제거될 때 자신이 생성한 레이저도 회수합니다.")]
    private bool cleanupSpawnedBeams;
    [SerializeField] private float laserLength = 10f;
    [SerializeField] private float duration = 1f;
    [SerializeField] private float laserWidth = 1f;
    [SerializeField] private LayerMask laserBlockMask;
    [SerializeField] private LayerMask laserDamageMask;

    private readonly WaveTrapDamage trapSettings = new WaveTrapDamage();
    public WaveDeviceParameter[] Parameters => new[] { WaveTrapDamage.DamageParameter(laserDamageInfo.damage), WaveTrapDamage.ReturnParameter, WarningParameter, OffsetParameter };
    public bool SettingsChangeShape => false;
    public void ApplySettings(WaveDeviceSetting[] overrides)
    {
        trapSettings.ApplySettings(overrides, laserDamageInfo.damage);
        warningTime = WaveDeviceSettings.Value(overrides, WarningParameter);
        laserOffset = Mathf.Max(0f, WaveDeviceSettings.Value(overrides, OffsetParameter));
    }
    public bool TryGetRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range) { range = default; return false; }

    // 발사 전 예고선(배치 설정). 기본 0이면 예고선을 만들지 않아 기존 발사기(다른 훈련 Wave)와 완전히 같다.
    // 발사 주기는 바꾸지 않는다. 빔이 꺼져 있는 구간의 마지막 N초에만 보이며, 꺼진 구간이 N보다 짧으면 그 구간 전체에 보인다.
    public const string WarningKey = "laserWarningTime";
    private static WaveDeviceParameter WarningParameter => new WaveDeviceParameter(WarningKey, "예고선 시간",
        "발사 직전 이 시간 동안 빔이 지나갈 자리에 얇은 붉은 선을 보입니다. 발사 주기는 바뀌지 않으며, 빔이 꺼져 있는 구간보다 길면 그 구간 전체에 보입니다. 0이면 예고선이 없습니다.",
        "초", 0f, 1.5f, .05f, 0f);
    private float warningTime;
    private LaserWarningLine warningLine;
    private bool autoWarning;
    private bool cycleRunning;
    private float cycleStartTime;
    private float lastFireTime = float.NegativeInfinity;

    // 발사 시차(배치 설정). 주기가 시작된 뒤 첫 발사만 이 시간만큼 늦춰 주기 안 위상을 민다. 그 뒤 발사 간격(creationCycle)은 그대로다.
    // 예고선은 다음 발사 시각(NextFireTime)을 기준으로 하므로 같은 위상을 따른다. 기본 0이면 기존 발사기와 같다.
    public const string OffsetKey = "laserOffset";
    private static WaveDeviceParameter OffsetParameter => new WaveDeviceParameter(OffsetKey, "발사 시차",
        "등장을 마친 뒤 첫 발사를 이 시간만큼 늦춥니다. 같은 줄의 사출기마다 조금씩 늘려 주면 차례로 쏩니다. 발사 간격과 예고선 길이는 바뀌지 않고 예고선도 같이 밀립니다. 0이면 기본 동작입니다.",
        "초", 0f, 3f, .025f, 0f);
    private float laserOffset;
    // 이번 주기에서 아직 한 번도 쏘지 않았는지. 시차는 이 첫 발사에만 더한다.
    private bool firstCycle;
    /// <summary>이 개체에 적용된 발사 시차(초).</summary>
    public float CycleOffset => laserOffset;
    // 시차를 더한 이번 주기의 시작 시각. 시차가 0이거나 이미 쏜 뒤면 cycleStartTime 그대로다.
    private float PhasedCycleStart => firstCycle && laserOffset > 0f ? cycleStartTime + laserOffset : cycleStartTime;

    /// <summary>이 개체에 적용된 예고선 시간(초). 0이면 예고선이 없다.</summary>
    public float WarningTime => warningTime;
    /// <summary>예고선이 지금 보이는지(자동 예고와 외부 예고 모두).</summary>
    public bool WarningVisible => warningLine != null && warningLine.Visible;
    /// <summary>예고선 그림(검사용). 한 번도 보인 적이 없으면 null이다.</summary>
    public LaserWarningLine WarningLine => warningLine;
    /// <summary>마지막으로 빔을 쏜 시각(Time.time). 이번 대여에서 쏜 적이 없으면 음의 무한대다.</summary>
    public float LastFireTime => lastFireTime;
    /// <summary>이번 대여에서 쏜 빔 수(검사용).</summary>
    public int FireCount { get; private set; }
    /// <summary>다음 발사 예정 시각(Time.time). 주기가 돌고 있지 않으면 양의 무한대다.</summary>
    public float NextFireTime => cycleRunning ? PhasedCycleStart + Mathf.Max(0f, creationCycle) : float.PositiveInfinity;

    public uint SpawnVersion { get; private set; }
    private LaserBeam laserPrefab;
    private WaveEnvironmentLifecycle lifecycle;
    private readonly List<(LaserBeam beam, uint version)> spawnedBeams = new();
    protected override void Awake()
    {
        base.Awake();
        ApplySettings(WaveDeviceSettings.None);
        lifecycle = GetComponent<WaveEnvironmentLifecycle>();
        laserPrefab = entityBase.GetComponent<LaserBeam>();
        if (laserPrefab == null)
            Debug.LogError($"[LaserSpawner] {gameObject.name}의 entityBase에 LaserBeam 컴포넌트가 없습니다!");
    }
    public override void OnSpawnedFromPool()
    {
        SpawnVersion++;
        trapSettings.Cancel();
        ApplySettings(WaveDeviceSettings.None);
        HideWarning();
        FireCount = 0;
        lastFireTime = float.NegativeInfinity;
        if (lifecycle == null) { base.OnSpawnedFromPool(); MarkCycleStart(); return; }
        isReturning = false;
        cycleRunning = false;
        StopAllCoroutines();
        CleanupSpawnedBeams();
        // 등장 완료 뒤 발사 주기를 시작하여 준비 중에는 레이저를 만들지 않는다.
        lifecycle.BeginSpawn(BeginSpawnCycle);
    }

    private void BeginSpawnCycle()
    {
        base.OnSpawnedFromPool();
        MarkCycleStart();
    }

    // ObjectSpawner의 주기 타이머가 다시 시작된 시각을 따라 적는다(타이머 자체는 그대로 둔다).
    private void MarkCycleStart()
    {
        cycleStartTime = Time.time;
        cycleRunning = true;
        firstCycle = true;
    }

    protected override void Update()
    {
        if (lifecycle != null && !lifecycle.IsReady)
        {
            // 등장·퇴장 중에는 예고선을 숨긴다.
            if (autoWarning) HideWarning();
            return;
        }
        // 시차가 남은 동안에는 주기 판정을 부르지 않는다. ObjectSpawner 타이머(시작 = cycleStartTime)는 그대로 두므로,
        // 시차가 지난 첫 Update에 바로 쏘고 그때부터 원래 간격으로 돈다(위치·물리는 건드리지 않는다).
        if (!HoldingForOffset) base.Update();
        if (warningTime > 0f) UpdateAutoWarning();
        else if (autoWarning) HideWarning();
    }

    private bool HoldingForOffset => firstCycle && laserOffset > 0f && cycleRunning && Time.time - cycleStartTime < laserOffset + Mathf.Max(0f, creationCycle);

    // 꺼진 구간 = 이번 주기 시작(또는 지난 빔이 끝난 때)부터 다음 발사까지. 그 마지막 warningTime초에만 보인다.
    private void UpdateAutoWarning()
    {
        float nextFire = NextFireTime;
        float offStart = Mathf.Max(cycleStartTime, lastFireTime + Mathf.Max(0f, duration));
        float window = Mathf.Min(warningTime, nextFire - offStart);
        float now = Time.time;
        if (!cycleRunning || isReturning || window <= 0f || now < nextFire - window || now >= nextFire)
        {
            if (autoWarning) HideWarning();
            return;
        }
        if (ShowWarning(now - (nextFire - window), window)) autoWarning = true;
    }

    /// <summary>
    /// 발사와 같은 경로(발사점, 방향, 길이, 기존 모터 질의 LaserLineUtility.GetBlockHit)로 빔이 닿을 구간을 구한다.
    /// </summary>
    public bool TryGetBeamPath(out Vector2 start, out Vector2 end)
    {
        start = end = default;
        if (spawnTr == null) return false;
        Vector2 fireDirection = useLocalDirection ? (Vector2)transform.TransformDirection(direction) : direction;
        if (fireDirection.sqrMagnitude <= 0.0001f) return false;
        fireDirection.Normalize();
        start = spawnTr.position;
        float length = Mathf.Max(0f, laserLength);
        var hit = LaserLineUtility.GetBlockHit(start, fireDirection, length, Mathf.Max(0.01f, laserWidth) * .5f, laserBlockMask);
        end = start + fireDirection * (hit.collider != null ? hit.distance : length);
        return true;
    }

    /// <summary>
    /// 예고선을 보인다. elapsed = 예고 구간이 시작된 뒤 지난 시간, window = 예고 구간 길이(초).
    /// 용접 팔처럼 자기 주기로 쏘는 쪽은 이 함수로 직접 예고하고, 끝나면 HideWarning을 부른다.
    /// </summary>
    public bool ShowWarning(float elapsed, float window)
    {
        if (!TryGetBeamPath(out var start, out var end)) { HideWarning(); return false; }
        warningLine ??= new LaserWarningLine(transform, laserPrefab != null ? laserPrefab : (Component)entityBase);
        warningLine.Show(start, end, elapsed, window, laserWidth);
        return warningLine.Visible;
    }

    public void HideWarning()
    {
        autoWarning = false;
        if (warningLine != null) warningLine.Hide();
    }

    protected override void SpawnEntity()
    {
        // ObjectSpawner가 이 호출 직후 주기 타이머를 다시 시작하므로 같은 시각을 적는다.
        cycleStartTime = Time.time;
        firstCycle = false;
        if (spawnOnce) cycleRunning = false;
        if (autoWarning) HideWarning();
        if (lifecycle != null && !lifecycle.IsReady) return;
        if (laserPrefab == null)
            return;

        LaserBeam laser = WorldEntityPool.Instance.Spawn(laserPrefab, spawnTr.position, spawnTr.rotation);

        if (cleanupSpawnedBeams)
        {
            spawnedBeams.RemoveAll(item => item.beam == null || !item.beam.gameObject.activeInHierarchy || item.beam.SpawnVersion != item.version);
            spawnedBeams.Add((laser, laser.SpawnVersion));
        }

        Vector2 fireDirection = useLocalDirection ? (Vector2)transform.TransformDirection(direction) : direction;
        var hitInfo = laserDamageInfo;
        hitInfo.damage = trapSettings.Damage;
        laser.Fire(
            hitInfo,
            spawnTr.position,
            fireDirection.normalized,
            laserLength,
            duration,
            laserBlockMask,
            laserDamageMask,
            laserWidth);
        // 기존 튜토리얼 발사기는 원래의 회복·복귀 정책을 유지한다.
        if (GetComponent<WaveEnvironmentTool>() != null) laser.ConfigureWaveTrap(trapSettings, this);
        lastFireTime = Time.time;
        FireCount++;
    }
    public override void ForceReturnToPool()
    {
        if (isReturning) return;
        cycleRunning = false;
        HideWarning();
        trapSettings.Cancel();
        CleanupSpawnedBeams();
        if (lifecycle != null) lifecycle.BeginDespawn(ReturnAfterDespawn);
        else base.ForceReturnToPool();
    }

    private void ReturnAfterDespawn() => base.ForceReturnToPool();

    private void OnDisable()
    {
        trapSettings.Cancel();
        cycleRunning = false;
        HideWarning();
        CleanupSpawnedBeams();
    }

    private void CleanupSpawnedBeams()
    {
        // 반환된 풀 개체가 다른 발사에 재대여됐다면 건드리지 않습니다.
        foreach (var item in spawnedBeams)
            if (item.beam != null && item.beam.gameObject.activeInHierarchy && item.beam.SpawnVersion == item.version)
                item.beam.ForceReturnToPool();
        spawnedBeams.Clear();
    }
}
