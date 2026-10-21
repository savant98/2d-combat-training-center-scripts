using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LaserBeam : WorldEntityBase
{
    public Action<CharacterController2D, Vector2> OnHitEnemy;

    [Header("References")]
    [SerializeField] private LineRenderer coreLineRenderer;
    [SerializeField] private LineRenderer glowLineRenderer;

    [Header("Feedback")]
    [SerializeField] private FeedbackDataSO hitFeedbackData;
    [SerializeField] private FeedbackDataSO blockFeedbackData;
    [SerializeField, Min(0f)] private float blockFeedbackInterval = 0.08f;

    [Header("Loop Sound")]
    [SerializeField] private SoundOptions loopSound;

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
    private LayerMask blockMask;
    private LayerMask damageMask;

    private bool activeBeam;

    private SoundHandle loopSoundHandle;

    protected override void Awake()
    {
        base.Awake();

        InitializeLineRenderer(coreLineRenderer);
        InitializeLineRenderer(glowLineRenderer);
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        ResetBeamState();

        OnHitEnemy = null;
    }

    public override void ForceReturnToPool()
    {
        ResetBeamState();
        base.ForceReturnToPool();
    }
    private void OnDisable()
    {
        StopLoopSound();
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

        StartLoopSound();

        SetLinesEnabled(true);

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

        FeedbackPlayer.Instance.Play(blockFeedbackData, blockHit.point);
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

        RaycastHit2D[] hits = Physics2D.CircleCastAll(
            origin,
            radius,
            direction,
            blockedDistance,
            damageMask);

        if (hits == null || hits.Length == 0)
            return;

        HashSet<HealthBase> damagedTargets = new HashSet<HealthBase>();

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D col = hits[i].collider;
            if (col == null)
                continue;

            //if (!col.TryGetComponent<HealthBase>(out var health))
            //    continue;

            HealthBase health = hits[i].collider.GetComponentInParent<HealthBase>();
            if (health == null)
                continue;

            if (!damagedTargets.Add(health))
                continue;

            Vector2 hitPoint = hits[i].point;

            if (ownerFaction == Faction.Player && health is EnemyHealth enemy)
            {
                if (enemy.enemyShieldSystem == null)
                    enemy.TakeDamage(damageInfo, hitPoint);
                else
                    enemy.enemyShieldSystem.TakeDamage(damageInfo, hitPoint);

                OnHitEnemy?.Invoke(owner, hitPoint);
                PlayHitFeedback(hitPoint);
            }
            else if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
            {
                player.TakeDamage(damageInfo, hitPoint);
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
        if (hitFeedbackData == null)
            return;

        FeedbackPlayer.Instance.Play(hitFeedbackData, hitPoint);
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

        activeBeam = false;

        StopLifeRoutine();
        StopWidthRoutine();

        damageTimer = tickInterval;
        animatedWidth = 0f;
        blockFeedbackTimer = 0f;
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

        if (loopSound == null || loopSound.clip == null)
            return;

        if (SoundManager.Instance == null)
            return;

        loopSoundHandle = SoundManager.Instance.PlayLoop(
            loopSound,
            origin,
            null);
    }

    private void StopLoopSound()
    {
        loopSoundHandle?.Stop();
        loopSoundHandle = null;
    }

    private void UpdateLoopSoundPosition(Vector2 endPoint)
    {
        if (loopSoundHandle == null || !loopSoundHandle.IsValid)
            return;

        Vector2 center = (origin + endPoint) * 0.5f;
        loopSoundHandle.SetPosition(center);
    }
}