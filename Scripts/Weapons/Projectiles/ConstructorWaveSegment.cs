using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(110)]
public sealed class ConstructorWaveSegment : WorldEntityBase
{
    [Header("Wave Cube")]
    [SerializeField] private Transform visualRoot;
    [SerializeField, Min(0.1f)] private float segmentWidth = 1.05f;
    [SerializeField] private FeedbackDataSO hitFeedbackData;

    public float Age => age;
    public float CurrentHeight { get; private set; }
    public float Width => segmentWidth;
    public Vector2 SurfacePosition => transform.position;

    private RaycastMotor2D motor;
    private Vector3 restScale;
    private Vector3 restPosition;
    private bool initialized;
    private float age;
    private float lifeTime;
    private float peakHeight;
    private LayerMask targetLayer;
    private HashSet<HealthBase> waveHitTargets;
    private readonly Collider2D[] hitResults = new Collider2D[32];

    protected override void Awake()
    {
        base.Awake();
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();
        if (visualRoot == null) visualRoot = transform.Find("Visual");
        if (visualRoot == transform) visualRoot = null;
        if (visualRoot != null)
        {
            restScale = visualRoot.localScale;
            restPosition = visualRoot.localPosition;
        }
        if (col != null) col.enabled = false;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        initialized = false;
        waveHitTargets = null;
        age = CurrentHeight = 0f;
        ResetVisual();
    }

    public void InitializeSegment(CharacterController2D owner, WeaponBaseSO.DamageInfo damage,
        float lifeTime, float height, HashSet<HealthBase> sharedTargets)
    {
        base.Initialize(owner, damage, 0);
        this.lifeTime = Mathf.Max(0.1f, lifeTime);
        peakHeight = Mathf.Max(0.1f, height);
        waveHitTargets = sharedTargets ?? new HashSet<HealthBase>();
        targetLayer = 1 << LayerMask.NameToLayer(ownerFaction == Faction.Player ? "Enemy" : "Player");
        age = 0f;
        initialized = true;
        UpdateShape();
    }

    private void FixedUpdate()
    {
        if (!initialized || isReturning) return;
        age += Time.fixedDeltaTime;
        if (age >= lifeTime) { ReturnToPool(); return; }
        UpdateShape();
        if (CurrentHeight < 0.18f) return;

        RaycastMotor2D.PrepareAttackQueries();
        Vector2 center = (Vector2)transform.position + Vector2.up * (CurrentHeight * 0.5f);
        int count = motor.OverlapAttackBox(center, new Vector2(segmentWidth * 0.85f, CurrentHeight),
            0f, hitResults, targetLayer);
        for (int i = 0; i < count; i++)
        {
            var hit = hitResults[i];
            if (hit == null) continue;
            var health = hit.GetComponentInParent<HealthBase>();
            if (health == null || health.IsInvincible || waveHitTargets.Contains(health)) continue;
            if (owner != null && (health == owner.GetComponentInParent<HealthBase>()
                || health.transform.IsChildOf(owner.transform))) continue;
            Vector2 point = hit.ClosestPoint(center);
            if (ownerFaction == Faction.Player && health is EnemyHealth enemy)
            {
                if (enemy.enemyShieldSystem != null && enemy.enemyShieldSystem.IsSpawnLocked) continue;
                waveHitTargets.Add(health);
                if (enemy.enemyShieldSystem == null) enemy.TakeDamage(damageInfo, point);
                else enemy.enemyShieldSystem.TakeDamage(damageInfo, point);
            }
            else if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
            {
                waveHitTargets.Add(health);
                player.TakeDamage(damageInfo, point);
            }
            else continue;
            if (hitFeedbackData != null && FeedbackPlayer.Instance != null)
                FeedbackPlayer.Instance.Play(hitFeedbackData, point);
        }
    }

    private void UpdateShape()
    {
        float normalized = Mathf.Clamp01(age / lifeTime);
        CurrentHeight = Mathf.Sin(normalized * Mathf.PI) * peakHeight;
        if (visualRoot == null) return;
        // 물리 루트는 바닥의 같은 위치에 둔다. 내부 큐브만 솟았다 가라앉는다.
        visualRoot.localScale = new Vector3(restScale.x * segmentWidth,
            restScale.y * Mathf.Max(0.01f, CurrentHeight), restScale.z);
        // Visual 원본은 바닥 pivot·높이 1로 정규화한다. 바닥은 고정하고 위쪽으로만 자란다.
        visualRoot.localPosition = restPosition - Vector3.up * 0.025f;
    }

    public void CancelForWave(HashSet<HealthBase> context)
    {
        if (initialized && ReferenceEquals(waveHitTargets, context)) ForceReturnToPool();
    }

    public override void ForceReturnToPool()
    {
        // 방 전체 회수의 snapshot에 이미 회수된 Segment가 남아 있어도 중복 enqueue하지 않는다.
        if (isReturning) return;
        base.ForceReturnToPool();
    }

    protected override void ReturnToPool()
    {
        if (isReturning) return;
        initialized = false;
        waveHitTargets = null;
        CurrentHeight = 0f;
        ResetVisual();
        base.ReturnToPool();
    }

    private void ResetVisual()
    {
        if (visualRoot == null) return;
        visualRoot.localScale = restScale;
        visualRoot.localPosition = restPosition;
    }

    private void OnDisable()
    {
        initialized = false;
        waveHitTargets = null;
        CurrentHeight = 0f;
        ResetVisual();
    }
}
