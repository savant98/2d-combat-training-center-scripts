using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class FeedbackPlayer : MonoBehaviour
{
    public static FeedbackPlayer Instance { get; private set; }

    [Header("Impulse Settings")]
    [SerializeField] private int initialImpulsePoolSize = 5;
    [SerializeField] private CinemachineImpulseSource shortImpulsePrefab;
    [SerializeField] private CinemachineImpulseSource continuousImpulsePrefab;

    [Header("Shake Limit")]
    [SerializeField] private float shakeWindow = 0.08f;
    [SerializeField] private float shakeCombineMultiplier = 0.35f;
    [SerializeField] private float maxShakePerWindow = 1.5f;
    [SerializeField] private float dominantShakeThreshold = 0.8f;
    [SerializeField] private float overrideThresholdMultiplier = 1.2f;

    [Header("Particle Pool")]
    [SerializeField] private int particlePoolPerPrefab = 5;

    [Header("Volume")]
    [SerializeField] private Volume globalVolume;

    private readonly List<CinemachineImpulseSource> shortImpulsePool = new();
    private readonly List<CinemachineImpulseSource> continuousImpulsePool = new();
    private readonly Dictionary<GameObject, Queue<GameObject>> particlePools = new();

    private Transform impulseRoot;
    private Transform particleRoot;

    private Vignette vignette;
    private Bloom bloom;
    private ChromaticAberration chroma;
    private ColorAdjustments colorAdjust;

    private float defaultVignetteIntensity;
    private float defaultBloomIntensity;
    private float defaultChromatic;
    private Color defaultColorAdjust;

    // shake aggregation
    private float shakeTimer = 0f;
    private float pendingShakeIntensity = 0f;
    private float pendingShakeDuration = 0f;
    private Vector3 pendingShakePosition = Vector3.zero;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Impulse들을 담을 부모 오브젝트
        impulseRoot = new GameObject("ImpulsePool").transform;
        impulseRoot.SetParent(transform);

        particleRoot = new GameObject("ParticlePool").transform;
        particleRoot.SetParent(transform);

        // 미리 여러 개 만들어두기
        for (int i = 0; i < initialImpulsePoolSize; i++)
        {
            if (shortImpulsePrefab != null)
                CreateImpulseSource(shortImpulsePrefab, shortImpulsePool);

            if (continuousImpulsePrefab != null)
                CreateImpulseSource(continuousImpulsePrefab, continuousImpulsePool);
        }

        // Volume Component 연결
        CacheVolumeComponents();

        // 임시코드
        Application.targetFrameRate = 144;
        QualitySettings.vSyncCount = 0;
    }
    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
    private void Update()
    {
        UpdateShakeWindow();
    }
    private void CacheVolumeComponents()
    {
        if (globalVolume == null || globalVolume.profile == null)
            return;

        if (globalVolume.profile.TryGet(out vignette))
            defaultVignetteIntensity = vignette.intensity.value;

        if (globalVolume.profile.TryGet(out bloom))
            defaultBloomIntensity = bloom.intensity.value;

        if (globalVolume.profile.TryGet(out chroma))
            defaultChromatic = chroma.intensity.value;

        if (globalVolume.profile.TryGet(out colorAdjust))
            defaultColorAdjust = colorAdjust.colorFilter.value;
    }

    #region ImpulsePool
    private CinemachineImpulseSource CreateImpulseSource(CinemachineImpulseSource prefab, List<CinemachineImpulseSource> pool)
    {
        var src = Instantiate(prefab, impulseRoot);

        pool.Add(src);
        return src;
    }

    private CinemachineImpulseSource GetImpulse(bool isContinuous)
    {
        var pool = isContinuous ? continuousImpulsePool : shortImpulsePool;
        var prefab = isContinuous ? continuousImpulsePrefab : shortImpulsePrefab;

        if (pool.Count == 0)
            return CreateImpulseSource(prefab, pool);

        var src = pool[0];
        pool.RemoveAt(0);
        pool.Add(src);

        return src;
    }
    #endregion

    #region EffectPool
    private GameObject GetParticleObject(GameObject prefab)
    {
        if (!particlePools.ContainsKey(prefab))
            particlePools[prefab] = new Queue<GameObject>();

        var pool = particlePools[prefab];

        // pool에 남은게 없으면 새로 생성
        if (pool.Count == 0)
        {
            for (int i = 0; i < particlePoolPerPrefab; i++)
            {
                var obj = Instantiate(prefab, particleRoot);
                obj.SetActive(false);
                pool.Enqueue(obj);
            }
        }

        var particleObj = pool.Dequeue();
        particleObj.SetActive(true);
        return particleObj;
    }

    private void ReturnParticle(GameObject prefab, GameObject obj)
    {
        obj.SetActive(false);
        particlePools[prefab].Enqueue(obj);
    }
    #endregion

    // ─────────────────────────────────
    // 외부에서 호출하는 API
    // ─────────────────────────────────
    public void Play(FeedbackDataSO data, Transform spawnPoint)
    {
        if (data == null || spawnPoint == null) return;

        Vector3 pos = spawnPoint.TransformPoint(data.offset);
        PlayInternal(data, pos, spawnPoint.rotation);
    }

    // Offset 적용
    public void Play(FeedbackDataSO data, Vector3 worldPos)
    {
        if (data == null) return;

        Vector3 pos = worldPos + data.offset;
        PlayInternal(data, pos, Quaternion.identity);
    }

    // Offset 미적용시
    public void PlayExact(FeedbackDataSO data, Vector3 worldPos, Quaternion rot)
    {
        if (data == null) return;

        PlayInternal(data, worldPos, rot);
    }
    private void PlayInternal(FeedbackDataSO data, Vector3 pos, Quaternion rot)
    {
        // Particle
        foreach (var prefab in data.particlePrefabs)
        {
            GameObject obj = GetParticleObject(prefab);

            obj.transform.SetPositionAndRotation(pos, rot);

            StartCoroutine(ReturnWhenFinished(prefab, obj, data.lifeTime));
        }

        // Sound
        if (data.sound != null && data.sound.clip != null)
        {
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(data.sound, pos);
        }

        // Shake
        if (data.shakeIntensity > 0f)
        {
            RequestShake(data.shakeIntensity, pos, data.shakeDuration);
        }

        // Time
        if (data.useHitStop)
        {
            if (GameTimeController.Instance != null)
                GameTimeController.Instance.PlayHitStop(data.hitStopScale, data.hitStopDuration);
        }

        // PostProcessing
        if (data.postFX != null)
        {
            StartCoroutine(PlayPostProcessing(data.postFX));
        }
    }
    private void RequestShake(float intensity, Vector3 pos, float duration)
    {
        float scaled = intensity * shakeCombineMultiplier;

        // 이미 강한 shake가 쌓여있으면 약한 건 무시
        if (pendingShakeIntensity >= dominantShakeThreshold && scaled < pendingShakeIntensity)
            return;

        // 더 강한 shake가 들어오면 기존 걸 덮어씀
        if (scaled > pendingShakeIntensity * overrideThresholdMultiplier)
        {
            pendingShakeIntensity = scaled;
            pendingShakePosition = pos;
            pendingShakeDuration = duration;
        }
        else
        {
            // 일반 누적
            pendingShakeIntensity += scaled;

            // 위치는 더 강한 쪽 기준으로 갱신
            if (scaled > pendingShakeIntensity * 0.5f)
                pendingShakePosition = pos;

            // 지속 시간은 겹친 것 중 가장 긴 시간을 유지
            pendingShakeDuration = Mathf.Max(pendingShakeDuration, duration);
        }

        if (shakeTimer <= 0f)
            shakeTimer = shakeWindow;
    }

    private void UpdateShakeWindow()
    {
        if (shakeTimer <= 0f)
            return;

        shakeTimer -= Time.unscaledDeltaTime;

        if (shakeTimer > 0f)
            return;

        EmitPendingShake();
    }

    private void EmitPendingShake()
    {
        if (pendingShakeIntensity <= 0f || (shortImpulsePrefab == null && continuousImpulsePrefab == null))
            return;

        float finalIntensity = Mathf.Min(pendingShakeIntensity, maxShakePerWindow);

        // 너무 작은 값은 아예 무시 (미세 떨림 제거)
        if (finalIntensity < 0.02f)
        {
            pendingShakeIntensity = 0f;
            pendingShakeDuration = 0f;
            return;
        }

        // duration이 0보다 크면 지속형, 아니면 단발형
        bool isContinuous = pendingShakeDuration > 0f;
        var src = GetImpulse(isContinuous);

        src.transform.position = pendingShakePosition;
        src.ImpulseDefinition.TimeEnvelope.SustainTime = pendingShakeDuration;

        if (isContinuous)
        {
            src.GenerateImpulse(finalIntensity);
        }
        else
        {
            src.GenerateImpulse(Vector2.up * finalIntensity);
        }
            
        pendingShakeIntensity = 0f;
        shakeTimer = 0f;
        pendingShakeDuration = 0f;
    }

    private IEnumerator ReturnWhenFinished(GameObject prefab, GameObject obj, float lifeTime)
    {
        yield return new WaitForSeconds(lifeTime);
        ReturnParticle(prefab, obj);
    }

    private IEnumerator PlayPostProcessing(PostProcessingOptions fx)
    {
        if (fx == null) yield break;

        StartCoroutine(Flash(fx));
        StartCoroutine(Chromatic(fx));
        StartCoroutine(BloomFX(fx));
        StartCoroutine(VignetteFX(fx));
        StartCoroutine(Saturation(fx));

        yield break;
    }

    private IEnumerator Flash(PostProcessingOptions fx)
    {
        if (!fx.useFlash) yield break;

        float t = 0;
        while (t < fx.flashDuration)
        {
            t += Time.unscaledDeltaTime;
            colorAdjust.colorFilter.value = Color.Lerp(fx.flashColor * fx.flashIntensity, defaultColorAdjust, t / fx.flashDuration);
            yield return null;
        }

        colorAdjust.colorFilter.value = defaultColorAdjust;
    }
    private IEnumerator Saturation(PostProcessingOptions fx)
    {
        if (!fx.useSaturation) yield break;

        float t = 0;
        while (t < fx.saturationDuration)
        {
            t += Time.unscaledDeltaTime;
            colorAdjust.saturation.value = Mathf.Lerp(fx.saturationIntensity, 0, t / fx.saturationDuration);
            yield return null;
        }

        colorAdjust.saturation.value = 0;
    }
    private IEnumerator Chromatic(PostProcessingOptions fx)
    {
        if (!fx.useChromaticAberration) yield break;

        float t = 0;
        while (t < fx.chromaDuration)
        {
            t += Time.unscaledDeltaTime;
            chroma.intensity.value = Mathf.Lerp(fx.chromaIntensity, 0, t / fx.chromaDuration);
            yield return null;
        }

        chroma.intensity.value = defaultChromatic;
    }

    private IEnumerator BloomFX(PostProcessingOptions fx)
    {
        if (!fx.useBloom) yield break;

        float t = 0;
        while (t < fx.bloomDuration)
        {
            t += Time.unscaledDeltaTime;
            bloom.intensity.value = Mathf.Lerp(fx.bloomIntensity, defaultBloomIntensity, t / fx.bloomDuration);
            yield return null;
        }

        bloom.intensity.value = defaultBloomIntensity;
    }

    private IEnumerator VignetteFX(PostProcessingOptions fx)
    {
        vignette.color.value = fx.vignetteColor;

        if (!fx.useVignette) yield break;

        float t = 0;
        while (t < fx.vignetteDuration)
        {
            t += Time.unscaledDeltaTime;
            vignette.intensity.value = Mathf.Lerp(fx.vignetteIntensity, 0, t / fx.vignetteDuration);
            yield return null;
        }

        vignette.intensity.value = defaultVignetteIntensity;
    }
}