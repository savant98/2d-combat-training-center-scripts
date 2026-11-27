using System;
using System.Collections.Generic;
using UnityEngine;

// 던전 방에 놓는 Prop(장식·발판·파괴형·함정)의 공통 수명.
// - 루트 하나에 DungeonProp + WaveEnvironmentTool(격자 편집기 표식)을 두고, 기능은 같은 루트의 부품 컴포넌트
//   (DestructibleProp·PropHazard·PropShredder 등 함정·DungeonPropMotion)가 맡는다. 부품은 IDungeonPropPart로 생성·반환 순간을 받는다.
// - 풀에서 꺼내는 순간 바로 준비 상태다(환경 장치와 달리 조립 등장이 없다 — 방 교체가 화면을 가린 동안 즉시 일어나므로).
//   표시 묶음(visualRoot)을 켜고, 모든 자식 Collider2D를 Prefab에 저장된 켜짐 상태로 되돌리고, 부서짐 상태를 지운다.
// - 배치별 설정(IWaveDeviceSettings)은 이 컴포넌트 하나만 구현한다(WaveDeviceSettings.Apply가 루트에서 처음 찾은 구현 하나만 부르므로).
//   부품의 항목은 IDungeonPropSettings로 모아 이어 붙인다. 편집기 미리보기(Edit Mode, Awake 전)에서도 불린다.
// - 퇴장은 ForceReturnToPool. 이미 풀로 돌아간 개체에 다시 불려도 아무 일도 하지 않는다(부서진 뒤 스스로 돌아간 Prop을
//   방 정리가 한 번 더 돌려보내도 풀 경고가 나지 않게). 단 풀이 같은 개체를 다른 자리에 다시 내줬을 수 있으므로,
//   참조를 오래 들고 있는 쪽은 꺼낼 때의 PoolLease와 비교하거나 Returning 이벤트에서 참조를 버린다.

public enum DungeonPropKind
{
    /// <summary>장식. Collider 없음(탄·레이저를 막지 않는다).</summary>
    Decoration = 0,
    /// <summary>발판. 고체(Platform 레이어) 또는 OneWay(TransparentPlatform + OneWayPlatform).</summary>
    Platform = 1,
    /// <summary>파괴형. DestructibleProp(EnemyHealth 파생)과 Enemy 레이어 hurtbox.</summary>
    Destructible = 2,
    /// <summary>함정. PropHazard(증기)·PropShredder·PropRockfall(7차 낙석 구조물)·PropLaserWelder 등 피해 부품. 6차부터 방 클리어 때 멈춘다(IDungeonRoomHazard).</summary>
    Hazard = 3,
}

/// <summary>DungeonProp 루트에 함께 붙는 기능 부품. 생성·반환 순간을 받는다.</summary>
public interface IDungeonPropPart
{
    /// <summary>풀에서 꺼내 DungeonProp가 표시·Collider를 되돌린 직후 불린다. 부품 상태를 처음으로 되돌린다.</summary>
    void OnPropSpawned(DungeonProp prop);
    /// <summary>풀로 돌아가기 직전에 불린다. 지속 효과·코루틴을 정리한다.</summary>
    void OnPropReturning(DungeonProp prop);
}

/// <summary>배치별 설정을 받는 부품. DungeonProp가 항목을 모아 격자 편집기에 보인다.</summary>
public interface IDungeonPropSettings
{
    WaveDeviceParameter[] PropParameters { get; }
    /// <summary>비어 있으면 Prefab 기본값. Edit Mode(Awake 전)에서도 불리므로 실행 상태에 기대지 않는다.</summary>
    void ApplyPropSettings(WaveDeviceSetting[] overrides);
    bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range);
}

[DisallowMultipleComponent]
public sealed class DungeonProp : WorldEntityBase, IWaveDeviceSettings
{
    [Header("Prop")]
    [Tooltip("격자 편집기 분류와 방 정리에 쓰는 종류. 실제 기능은 같은 루트의 부품 컴포넌트가 정한다.")]
    [SerializeField] private DungeonPropKind kind = DungeonPropKind.Decoration;
    [Tooltip("보이는 부품 묶음. 비우면 자식 'Visual'을 찾는다. 부서지면 이 묶음을 끈다.")]
    [SerializeField] private Transform visualRoot;

    private Collider2D[] colliders = Array.Empty<Collider2D>();
    private bool[] colliderDefaults = Array.Empty<bool>();
    private Vector3 visualHome;
    private bool visualDefaultActive = true;
    private bool cached;
    private bool live;

    public DungeonPropKind Kind => kind;
    public Transform VisualRoot { get { Cache(); return visualRoot; } }
    /// <summary>풀에서 꺼내져 아직 돌아가지 않은 동안 true.</summary>
    public bool IsLive => live;
    /// <summary>지금 대여 번호(풀에서 꺼낼 때마다 1 늘어난다). 참조를 들고 있는 쪽이 같은 대여인지 비교한다.</summary>
    public uint Lease => PoolLease;
    /// <summary>풀로 돌아가기 직전에 한 번 나간다.</summary>
    public event Action<DungeonProp> Returning;

    protected override void Awake()
    {
        base.Awake();
        Cache();
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        if (visualRoot == null) visualRoot = transform.Find("Visual");
        if (visualRoot != null)
        {
            visualHome = visualRoot.localPosition;
            visualDefaultActive = visualRoot.gameObject.activeSelf;
        }
        colliders = GetComponentsInChildren<Collider2D>(true);
        colliderDefaults = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++) colliderDefaults[i] = colliders[i] != null && colliders[i].enabled;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        Cache();
        live = true;
        RestoreReadyState();
        ApplySettings(WaveDeviceSettings.None);   // 배치값은 생성한 쪽이 곧바로 WaveDeviceSettings.Apply로 다시 적용한다.
        foreach (var part in Parts()) part.OnPropSpawned(this);
        RaycastMotor2D.InvalidateAttackQuerySync();   // 같은 틱의 공격 질의가 새 자리의 hurtbox를 보게 한다.
    }

    /// <summary>표시·Collider를 Prefab 상태로 되돌린다(조립 등장 없음).</summary>
    public void RestoreReadyState()
    {
        Cache();
        if (visualRoot != null)
        {
            visualRoot.localPosition = visualHome;
            visualRoot.gameObject.SetActive(visualDefaultActive);
        }
        SetCollidersEnabled(true);
    }

    /// <summary>true면 Prefab에 저장된 켜짐 상태로, false면 모든 자식 Collider2D를 끈다(hurtbox·고체·OneWay 모두).</summary>
    public void SetCollidersEnabled(bool enabled)
    {
        Cache();
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = enabled && colliderDefaults[i];
        RaycastMotor2D.InvalidateAttackQuerySync();
    }

    public void SetVisualVisible(bool visible)
    {
        Cache();
        if (visualRoot != null) visualRoot.gameObject.SetActive(visible && visualDefaultActive);
    }

    /// <summary>표시 묶음의 원래 자리(피격 흔들림이 이 값을 기준으로 흔들고 되돌린다).</summary>
    public Vector3 VisualHome { get { Cache(); return visualHome; } }

    public override void ForceReturnToPool()
    {
        if (!live) return;   // 이미 돌아갔거나 풀에서 꺼낸 적 없는 개체
        live = false;
        foreach (var part in Parts()) part.OnPropReturning(this);
        Returning?.Invoke(this);
        Returning = null;
        if (poolPrefab == null)
        {
            // 풀을 거치지 않고 놓인 개체(검사용 등)는 끄기만 한다.
            StopAllCoroutines();
            gameObject.SetActive(false);
            return;
        }
        base.ForceReturnToPool();
    }

    /// <summary>풀을 거치지 않고 만든 개체를 준비 상태로 켠다(검사·장면 배치용). 풀 개체에는 쓰지 않는다.</summary>
    public void BeginStandalone()
    {
        if (poolPrefab != null) return;
        OnSpawnedFromPool();
    }

    private IEnumerable<IDungeonPropPart> Parts()
    {
        var parts = GetComponents<IDungeonPropPart>();
        for (int i = 0; i < parts.Length; i++)
            if (parts[i] != null) yield return parts[i];
    }

    // ── 배치별 설정 ─────────────────────────────

    public WaveDeviceParameter[] Parameters
    {
        get
        {
            var result = new List<WaveDeviceParameter>();
            var keys = new HashSet<string>();
            foreach (var part in GetComponents<IDungeonPropSettings>())
            {
                var parameters = part?.PropParameters;
                if (parameters == null) continue;
                foreach (var parameter in parameters)
                    if (keys.Add(parameter.Key)) result.Add(parameter);   // 같은 열쇠(피해량 등)는 처음 것만 보인다
            }
            return result.ToArray();
        }
    }

    public bool SettingsChangeShape => false;

    public void ApplySettings(WaveDeviceSetting[] overrides)
    {
        foreach (var part in GetComponents<IDungeonPropSettings>())
            part?.ApplyPropSettings(overrides ?? WaveDeviceSettings.None);
    }

    public bool TryGetRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        foreach (var part in GetComponents<IDungeonPropSettings>())
            if (part != null && part.TryGetPropRange(overrides ?? WaveDeviceSettings.None, out range)) return true;
        range = default;
        return false;
    }
}

/// <summary>
/// Prop 효과 재생. 카메라에서 먼 Prop은 소리와 흔들림을 줄인다(WaveEnvironmentDevice.PlayFeedback과 같은 거리 7~24칸 규칙).
/// 공유 FeedbackDataSO는 바꾸지 않고 에셋마다 실행 중 사본 하나를 만들어 세기만 바꿔 쓴다.
/// </summary>
public static class DungeonPropFeedback
{
    public const float NearDistance = 7f;
    public const float FarDistance = 24f;
    private static readonly Dictionary<FeedbackDataSO, FeedbackDataSO> copies = new Dictionary<FeedbackDataSO, FeedbackDataSO>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCopies() => copies.Clear();

    public static float DistanceGain(Vector2 position) => DistanceGain(position, NearDistance, FarDistance);

    /// <summary>
    /// 7차(DESIGN_V7 V3): 가까운·먼 거리(칸)를 따로 받는 거리 감쇠(텔레포트 반복음을 받침 근처 4~14칸으로 줄이는 등). 규칙은 기본값(7~24칸)과 같다 —
    /// near 안은 1, far 밖은 0, 사이는 smoothstep. far ≤ near면 near에서 끊는다.
    /// </summary>
    public static float DistanceGain(Vector2 position, float near, float far)
    {
        var listener = Camera.main;
        if (listener == null) return 1f;
        if (far <= near) far = near + .01f;
        float t = Mathf.InverseLerp(near, far, Vector2.Distance(listener.transform.position, position));
        return 1f - t * t * (3f - 2f * t);
    }

    public static void Play(FeedbackDataSO data, Vector3 position, Quaternion rotation)
    {
        if (data == null || FeedbackPlayer.Instance == null) return;
        float gain = DistanceGain(position);
        if (gain <= 0f) return;
        if (!copies.TryGetValue(data, out var copy) || copy == null)
        {
            copy = UnityEngine.Object.Instantiate(data);
            copy.name = data.name + " (Prop Instance)";
            copy.hideFlags = HideFlags.DontSave;
            if (copy.particlePrefabs == null) copy.particlePrefabs = Array.Empty<GameObject>();
            copies[data] = copy;
        }
        copy.shakeIntensity = data.shakeIntensity * gain;
        if (data.sound != null)
        {
            copy.sound = data.sound.WithPitchMultiplier(1f);
            copy.sound.volume = data.sound.volume * gain;
        }
        FeedbackPlayer.Instance.PlayExact(copy, position, rotation);
    }
}

/// <summary>
/// 발광 부품의 밝기 배율을 MaterialPropertyBlock으로 바꾼다(공유 재질은 건드리지 않는다).
/// 재질에 _Intensity가 있으면 그 값(Wave/EnvironmentTheme·WeaponMeshGlow), 없으면 _EmissionColor(선형 값으로 SetVector — HDR 색의 감마 변환을 피한다),
/// 그것도 없으면 _Color의 RGB를 배율로 곱한다. 0으로 꺼야 하면 밝기 대신 Renderer를 끈다(알파 블렌드 재질은 밝기 0이 검은 선으로 남는다).
/// </summary>
public sealed class DungeonPropGlow
{
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private readonly Renderer[] renderers;
    private readonly int[] modes;          // 0 없음, 1 _Intensity, 2 _EmissionColor, 3 _Color
    private readonly Vector4[] baseValues;
    private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
    private bool dirty;

    public DungeonPropGlow(Renderer[] targets)
    {
        renderers = targets ?? Array.Empty<Renderer>();
        modes = new int[renderers.Length];
        baseValues = new Vector4[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            var material = renderers[i] != null ? renderers[i].sharedMaterial : null;
            if (material == null) continue;
            if (material.HasProperty(IntensityId)) { modes[i] = 1; baseValues[i] = new Vector4(material.GetFloat(IntensityId), 0f, 0f, 0f); }
            else if (material.HasProperty(EmissionId)) { modes[i] = 2; Color c = material.GetColor(EmissionId).linear; baseValues[i] = new Vector4(c.r, c.g, c.b, c.a); }
            else if (material.HasProperty(ColorId)) { modes[i] = 3; Color c = material.GetColor(ColorId); baseValues[i] = new Vector4(c.r, c.g, c.b, c.a); }
        }
    }

    public int Count => renderers.Length;

    /// <summary>모든 부품의 밝기를 원래 값 × level로.</summary>
    public void Set(float level)
    {
        for (int i = 0; i < renderers.Length; i++) SetOne(i, level);
    }

    public void SetOne(int index, float level)
    {
        if (index < 0 || index >= renderers.Length) return;
        var renderer = renderers[index];
        if (renderer == null || modes[index] == 0) return;
        renderer.GetPropertyBlock(block);
        Vector4 b = baseValues[index];
        switch (modes[index])
        {
            case 1: block.SetFloat(IntensityId, b.x * level); break;
            case 2: block.SetVector(EmissionId, new Vector4(b.x * level, b.y * level, b.z * level, b.w)); break;
            case 3: block.SetColor(ColorId, new Color(b.x * level, b.y * level, b.z * level, b.w)); break;
        }
        renderer.SetPropertyBlock(block);
        dirty = true;
    }

    /// <summary>MaterialPropertyBlock을 지워 재질 값으로 돌린다.</summary>
    public void Clear()
    {
        if (!dirty) return;
        dirty = false;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].SetPropertyBlock(null);
    }
}
