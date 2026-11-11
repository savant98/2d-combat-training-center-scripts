using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LaserBeam : WorldEntityBase
{
    // 자동 반환 뒤 같은 풀 개체가 재대여됐는지 효과를 추적하는 쪽에서 구분한다.
    public uint SpawnVersion { get; private set; }

    // 적 쪽 레이저가 플레이어에게 피해를 주는 동안(TakeDamage 호출 중)에만 그 레이저를 가리킨다.
    // PlayerHealth.OnDamaged를 받는 쪽이 피해의 출처를 구분할 때 쓴다(훈련 코스 레이저에 맞을 때만 처음부터 다시 시작).
    public static LaserBeam PlayerHitSource { get; private set; }

    [Header("References")]
    [SerializeField] private LineRenderer coreLineRenderer;
    [SerializeField] private LineRenderer glowLineRenderer;

    [Header("Feedback")]
    [SerializeField] private FeedbackDataSO hitFeedbackData;
    [SerializeField] private FeedbackDataSO blockFeedbackData;
    [SerializeField, Min(0f)] private float blockFeedbackInterval = 0.08f;
    [SerializeField, Min(0f), Tooltip("0이면 기존 적 레이저처럼 모든 판정 시각에 효과를 재생합니다.")]
    private float hitFeedbackInterval;

    [Header("Loop Sound")]
    [SerializeField] private SoundOptions loopSound;
    [SerializeField] private bool useDistanceAttenuation;
    [SerializeField, Min(0f)] private float soundNearDistance = 3f;
    [SerializeField, Min(0f)] private float soundFarDistance = 22f;

    [Header("Endpoint Sparks")]
    [SerializeField] private ParticleSystem[] endpointSparks = System.Array.Empty<ParticleSystem>();

    [Header("Beam Visual")]
    [SerializeField, Min(1f)] private float glowWidthMultiplier = 1.8f;
    [SerializeField, Min(1f)] private float burstWidthMultiplier = 2.5f;
    [SerializeField, Min(0f)] private float burstDuration = 0.08f;
    [SerializeField, Min(0f)] private float settleDuration = 0.15f;
    [SerializeField, Min(0f)] private float widthNoiseAmount = 0.02f;
    [SerializeField, Min(0f)] private float widthNoiseSpeed = 12f;

    [Header("Caps")]
    [SerializeField] private Transform startCap;
    [SerializeField] private Transform endCap;
    [SerializeField, Min(0f)] private float startCapScaleMultiplier = 1.0f;
    [SerializeField, Min(0f)] private float endCapScaleMultiplier = 1.15f;

    [Header("Damage")]
    [SerializeField, Min(0f)] private float tickInterval = 0.1f;

    [Header("Lifetime")]
    [SerializeField] private bool autoReturnToPool = true;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.2f;

    private RaycastMotor2D motor;
    private Collider2D[] boxHits = new Collider2D[16];
    private readonly List<Collider2D> capHits = new();
    private readonly HashSet<Collider2D> beamHits = new();
    private readonly HashSet<HealthBase> damagedTargets = new();

    private Coroutine lifeRoutine;
    private Coroutine widthRoutine;

    private Vector2 origin;
    private Vector2 direction;
    private float length;
    private float baseWidth;
    private float animatedWidth;
    private float currentVisualWidth;
    private float damageTimer;
    private float lifetimeWidthMultiplier;
    private float blockFeedbackTimer;
    private float nextHitFeedbackTime;
    private LayerMask blockMask;
    private LayerMask damageMask;

    private bool activeBeam;

    private SoundHandle loopSoundHandle;
    private float loopSoundBaseVolume;
    private float nextLoopSoundRetryTime;
    private Vector2 loopSoundPosition;
    private readonly Dictionary<FeedbackDataSO, FeedbackDataSO> distanceFeedbacks = new();

    protected override void Awake()
    {
        base.Awake();
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();

        InitializeLineRenderer(coreLineRenderer);
        InitializeLineRenderer(glowLineRenderer);
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        SpawnVersion++;
        ResetBeamState();
    }

    public override void ForceReturnToPool()
    {
        if (!gameObject.activeSelf) return;
        ResetBeamState();
        base.ForceReturnToPool();
    }
    private void OnDisable()
    {
        StopLoopSound();
        StopEndpointSparks();
    }

    private void OnDestroy()
    {
        foreach (var feedback in distanceFeedbacks.Values)
        {
            if (feedback == null) continue;
            if (Application.isPlaying) Destroy(feedback);
            else DestroyImmediate(feedback);
        }
        distanceFeedbacks.Clear();
    }
    public override void Initialize(CharacterController2D owner, WeaponBaseSO.DamageInfo damageInfo, int count = 0)
    {
        base.Initialize(owner, damageInfo, count);
    }

    public void Fire(
        WeaponBaseSO.DamageInfo damageInfo,
        Vector2 origin,
        Vector2 direction,
        float length,
        float duration,
        LayerMask blockMask,
        LayerMask damageMask,
        float width,
        CharacterController2D owner = null)
    {
        Initialize(owner, damageInfo);

        this.origin = origin;
        this.direction = direction.normalized;
        this.length = Mathf.Max(0f, length);
        this.blockMask = blockMask;
        this.damageMask = damageMask;
        baseWidth = Mathf.Max(0.01f, width);

        animatedWidth = 0f;
        currentVisualWidth = 0f;
        damageTimer = tickInterval;
        activeBeam = true;

        loopSoundPosition = origin;
        if (useDistanceAttenuation || (endpointSparks != null && endpointSparks.Length > 0))
        {
            // 풀의 이전 발사 위치에서 스파크가 나오지 않도록 기존 모터 질의로 양 끝을 먼저 정한다.
            var initialHit = LaserLineUtility.GetBlockHit(origin, this.direction, this.length, baseWidth * .5f, blockMask);
            Vector2 endPoint = origin + this.direction * (initialHit.collider != null ? initialHit.distance : this.length);
            if (useDistanceAttenuation) loopSoundPosition = (origin + endPoint) * .5f;
            if (endpointSparks != null && endpointSparks.Length > 0)
            {
                RefreshBeam(endPoint);
                // 발사 첫 순간의 시각 폭은 0이므로 스파크의 부모만 정상 크기로 초기화한다.
                LaserLineUtility.UpdateCaps(startCap, endCap, origin, endPoint, this.direction, baseWidth,
                    startCapScaleMultiplier, endCapScaleMultiplier);
            }
        }
        nextLoopSoundRetryTime = 0f;
        StartLoopSound();

        SetLinesEnabled(true);
        PlayEndpointSparks();

        StopWidthRoutine();
        widthRoutine = StartCoroutine(CoBeamWidthAnim());

        if (autoReturnToPool)
        {
            StopLifeRoutine();
            lifeRoutine = StartCoroutine(CoReturnAfter(duration));
        }
    }

    private void FixedUpdate()
    {
        if (!activeBeam)
            return;

        blockFeedbackTimer -= Time.fixedDeltaTime;

        UpdateVisualWidth();

        float radius = GetHitRadius();
        RaycastHit2D blockHit = LaserLineUtility.GetBlockHit(
        origin,
        direction,
        length,
        radius,
        blockMask);

        float blockedDistance = blockHit.collider != null
            ? blockHit.distance
            : length;

        Vector2 endPoint = origin + direction * blockedDistance;

        RefreshBeam(endPoint);
        UpdateLoopSoundPosition(endPoint);
        TryPlayBlockFeedback(blockHit);

        if (currentVisualWidth < baseWidth - (widthNoiseAmount * 2f))
            return;

        UpdateDamageTick(Time.fixedDeltaTime, blockedDistance);
    }

    private void UpdateVisualWidth()
    {
        float noise = 0f;

        if (widthNoiseAmount > 0f && widthNoiseSpeed > 0f)
        {
            noise = (Mathf.PerlinNoise(Time.time * widthNoiseSpeed, 0f) - 0.5f) * 2f;
            noise *= widthNoiseAmount;
        }

        float rawWidth = (animatedWidth * lifetimeWidthMultiplier) + noise;
        currentVisualWidth = Mathf.Max(0f, rawWidth);

        ApplyWidths(currentVisualWidth);
    }

    private void RefreshBeam(Vector2 endPoint)
    {
        SetLinePositions(origin, endPoint);
    }
    private void TryPlayBlockFeedback(RaycastHit2D blockHit)
    {
        if (blockHit.collider == null)
            return;

        if (blockFeedbackData == null)
            return;

        if (blockFeedbackTimer > 0f)
            return;

        FeedbackPlayer.Instance.Play(GetDistanceFeedback(blockFeedbackData, blockHit.point), blockHit.point);
        blockFeedbackTimer = blockFeedbackInterval;
    }
    private void UpdateDamageTick(float deltaTime, float blockedDistance)
    {
        damageTimer += deltaTime;

        while (damageTimer >= tickInterval)
        {
            ApplyDamageToAllTargets(blockedDistance);
            damageTimer -= tickInterval;
        }
    }
    private void ApplyDamageToAllTargets(float blockedDistance)
    {
        float radius = GetHitRadius();

        if (radius <= 0)
            return;

        beamHits.Clear();
        damagedTargets.Clear();
        Vector2 end = origin + direction * blockedDistance;

        // 기존 원형 Cast와 같은 캡슐 범위를 모터의 사각형·양 끝 원의 합집합으로 검사합니다.
        if (blockedDistance > 0f)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            int count;
            do
            {
                count = motor.OverlapAttackBox((origin + end) * .5f,
                    new Vector2(blockedDistance, radius * 2f), angle, boxHits, damageMask);
                if (count < boxHits.Length) break;
                System.Array.Resize(ref boxHits, boxHits.Length * 2);
            } while (true);
            for (int i = 0; i < count; i++) beamHits.Add(boxHits[i]);
        }
        var filter = new ContactFilter2D();
        filter.SetLayerMask(damageMask);
        filter.useTriggers = Physics2D.queriesHitTriggers;
        motor.OverlapAttackCircle(origin, radius, filter, capHits);
        foreach (var hit in capHits) beamHits.Add(hit);
        motor.OverlapAttackCircle(end, radius, filter, capHits);
        foreach (var hit in capHits) beamHits.Add(hit);

        foreach (var hit in beamHits)
        {
            if (hit == null) continue;
            HealthBase health = hit.GetComponentInParent<HealthBase>();
            if (health == null || !damagedTargets.Add(health)) continue;

            Vector2 hitPoint = hit.ClosestPoint(origin);

            if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
            {
                PlayerHitSource = this;
                try
                {
                    player.TakeDamage(damageInfo, hitPoint);
                }
                finally
                {
                    PlayerHitSource = null;
                }
                PlayHitFeedback(hitPoint);
            }
        }
    }

    private float GetHitRadius()
    {
        return currentVisualWidth * 0.5f;
    }

    private void PlayHitFeedback(Vector2 hitPoint)
    {
        if (hitFeedbackData == null || Time.time < nextHitFeedbackTime)
            return;

        nextHitFeedbackTime = Time.time + hitFeedbackInterval;
        FeedbackPlayer.Instance.Play(GetDistanceFeedback(hitFeedbackData, hitPoint), hitPoint);
    }

    private IEnumerator CoBeamWidthAnim()
    {
        float burstWidth = baseWidth * burstWidthMultiplier;

        if (burstDuration > 0f)
        {
            float t = 0f;

            while (t < burstDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / burstDuration);
                animatedWidth = Mathf.Lerp(0f, burstWidth, k);
                yield return null;
            }
        }
        else
        {
            animatedWidth = burstWidth;
        }

        if (settleDuration > 0f)
        {
            float t = 0f;

            while (t < settleDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / settleDuration);
                animatedWidth = Mathf.Lerp(burstWidth, baseWidth, k);
                yield return null;
            }
        }

        animatedWidth = baseWidth;
        widthRoutine = null;
    }

    private IEnumerator CoReturnAfter(float duration)
    {
        float clampedDuration = Mathf.Max(0f, duration);
        float shrinkDuration = Mathf.Min(fadeOutDuration, clampedDuration);
        float holdDuration = Mathf.Max(0f, clampedDuration - shrinkDuration);

        if (holdDuration > 0f)
            yield return new WaitForSeconds(holdDuration);

        if (shrinkDuration > 0f)
        {
            float t = 0f;

            while (t < shrinkDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / shrinkDuration);
                lifetimeWidthMultiplier = Mathf.Lerp(1f, 0f, k);
                yield return null;
            }
        }

        lifetimeWidthMultiplier = 0f;
        lifeRoutine = null;
        ForceReturnToPool();
    }

    private void ResetBeamState()
    {
        StopLoopSound();
        StopEndpointSparks();
        nextLoopSoundRetryTime = 0f;

        activeBeam = false;

        StopLifeRoutine();
        StopWidthRoutine();

        damageTimer = tickInterval;
        animatedWidth = 0f;
        blockFeedbackTimer = 0f;
        nextHitFeedbackTime = 0f;
        currentVisualWidth = 0f;
        lifetimeWidthMultiplier = 1f;

        SetLinesEnabled(false);
        ApplyWidths(0f);
    }

    private void StopLifeRoutine()
    {
        if (lifeRoutine == null)
            return;

        StopCoroutine(lifeRoutine);
        lifeRoutine = null;
    }

    private void StopWidthRoutine()
    {
        if (widthRoutine == null)
            return;

        StopCoroutine(widthRoutine);
        widthRoutine = null;
    }
    private void InitializeLineRenderer(LineRenderer line)
    {
        if (line == null)
            return;

        line.positionCount = 2;
        line.enabled = false;
    }
    private void SetLinesEnabled(bool enabled)
    {
        if (coreLineRenderer != null)
            coreLineRenderer.enabled = enabled;

        if (glowLineRenderer != null)
            glowLineRenderer.enabled = enabled;

        if (startCap != null)
            startCap.gameObject.SetActive(enabled);

        if (endCap != null)
            endCap.gameObject.SetActive(enabled);
    }
    private void ApplyWidths(float width)
    {
        if (coreLineRenderer != null)
        {
            coreLineRenderer.startWidth = width;
            coreLineRenderer.endWidth = width;
        }

        if (glowLineRenderer != null)
        {
            float glowWidth = width * glowWidthMultiplier;
            glowLineRenderer.startWidth = glowWidth;
            glowLineRenderer.endWidth = glowWidth;
        }
    }
    private void SetLinePositions(Vector2 start, Vector2 end)
    {
        if (coreLineRenderer != null)
        {
            coreLineRenderer.SetPosition(0, start);
            coreLineRenderer.SetPosition(1, end);
        }

        if (glowLineRenderer != null)
        {
            glowLineRenderer.SetPosition(0, start);
            glowLineRenderer.SetPosition(1, end);
        }

        LaserLineUtility.UpdateCaps(
            startCap,
            endCap,
            start,
            end,
            direction,
            currentVisualWidth,
            startCapScaleMultiplier,
            endCapScaleMultiplier);
    }
    public void SetOrigin(Vector2 newOrigin)
    {
        origin = newOrigin;
    }

    public void SetDirection(Vector2 newDirection)
    {
        if (newDirection.sqrMagnitude <= 0.0001f)
            return;

        direction = newDirection.normalized;
    }

    private void StartLoopSound()
    {
        StopLoopSound();

        if (loopSound == null || loopSound.clip == null || SoundManager.Instance == null)
            return;

        Vector2 position = useDistanceAttenuation ? loopSoundPosition : origin;
        float gain = LoopSoundDistanceGain();
        if (useDistanceAttenuation)
        {
            nextLoopSoundRetryTime = Time.unscaledTime + .12f;
            // 들리지 않는 빔은 공용 반복음의 동시 재생 자리를 차지하지 않는다.
            if (gain <= 0f) return;
        }
        loopSoundHandle = SoundManager.Instance.PlayLoop(loopSound, position, null);
        if (loopSoundHandle == null || !loopSoundHandle.IsValid) return;
        var source = loopSoundHandle.GetSource();
        // SoundManager가 계산한 동시 재생 감쇠를 기준으로 거리 음량만 덧곱한다.
        loopSoundBaseVolume = source.volume;
        if (useDistanceAttenuation) source.volume = loopSoundBaseVolume * gain;
    }

    private void StopLoopSound()
    {
        loopSoundHandle?.Stop();
        loopSoundHandle = null;
        loopSoundBaseVolume = 0f;
    }

    private void UpdateLoopSoundPosition(Vector2 endPoint)
    {
        Vector2 center = (origin + endPoint) * .5f;
        loopSoundPosition = center;
        float gain = LoopSoundDistanceGain();
        if (useDistanceAttenuation)
        {
            if (gain <= 0f) { StopLoopSound(); return; }
            if ((loopSoundHandle == null || !loopSoundHandle.IsValid) && Time.unscaledTime >= nextLoopSoundRetryTime)
                StartLoopSound();
        }
        if (loopSoundHandle == null || !loopSoundHandle.IsValid) return;
        loopSoundHandle.SetPosition(center);
        if (useDistanceAttenuation) loopSoundHandle.GetSource().volume = loopSoundBaseVolume * gain;
    }

    private float LoopSoundDistanceGain()
    {
        if (!useDistanceAttenuation) return 1f;
        var listener = Camera.main;
        if (listener == null) return 1f;
        // 긴 빔은 중앙이 멀어도 발사점이나 도착점 근처에서 들려야 하므로 실제 구간까지의 거리를 쓴다.
        Vector2 segment = (loopSoundPosition - origin) * 2f;
        Vector2 point = origin;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared > .000001f)
        {
            Vector2 delta = (Vector2)listener.transform.position - origin;
            float progress = Mathf.Clamp01(Vector2.Dot(delta, segment) / lengthSquared);
            point += segment * progress;
        }
        return SoundDistanceGain(point);
    }

    private float SoundDistanceGain(Vector2 position)
    {
        if (!useDistanceAttenuation) return 1f;
        var listener = Camera.main;
        if (listener == null) return 1f;
        // 기존 비행음처럼 XY 거리만 사용해 카메라 깊이가 2D 음량에 영향을 주지 않는다.
        float distance = Vector2.Distance(listener.transform.position, position);
        float near = Mathf.Max(0f, soundNearDistance);
        float far = Mathf.Max(near, soundFarDistance);
        if (far <= near) return distance <= near ? 1f : 0f;
        float t = Mathf.InverseLerp(near, far, distance);
        return 1f - t * t * (3f - 2f * t);
    }

    private FeedbackDataSO GetDistanceFeedback(FeedbackDataSO data, Vector2 position)
    {
        if (!useDistanceAttenuation || data == null || data.sound == null) return data;
        if (!distanceFeedbacks.TryGetValue(data, out var copy))
        {
            // 공유 SO를 바꾸지 않고 개체별 사본을 재사용한다. 매 피격마다 사본을 생성하지 않는다.
            copy = Instantiate(data);
            copy.name = data.name + " (Distance Instance)";
            copy.hideFlags = HideFlags.DontSave;
            var sound = data.sound;
            copy.sound = new SoundOptions
            {
                bus = sound.bus, clip = sound.clip, volume = sound.volume, pitch = sound.pitch,
                spatialBlend = sound.spatialBlend, randomizePitch = sound.randomizePitch,
                pitchRange = sound.pitchRange, minInterval = sound.minInterval,
                maxSimultaneous = sound.maxSimultaneous, crowdedVolumeMultiplier = sound.crowdedVolumeMultiplier
            };
            distanceFeedbacks.Add(data, copy);
        }
        float gain = SoundDistanceGain(position);
        copy.sound.volume = data.sound.volume * gain;
        // 음량 0인 단발음도 공용 동시 재생 수를 차지하므로 비가청 소리는 재생 요청을 생략한다.
        copy.sound.clip = gain > 0f ? data.sound.clip : null;
        return copy;
    }

    private void PlayEndpointSparks()
    {
        if (endpointSparks == null) return;
        foreach (var sparks in endpointSparks)
        {
            if (sparks == null) continue;
            sparks.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            sparks.Play(false);
        }
    }

    private void StopEndpointSparks()
    {
        if (endpointSparks == null) return;
        foreach (var sparks in endpointSparks)
            if (sparks != null) sparks.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}