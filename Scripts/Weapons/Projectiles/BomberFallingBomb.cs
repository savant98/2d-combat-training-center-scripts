using System.Collections.Generic;
using UnityEngine;

// AdminFallingCube와 같은 중력·모터·경로 판정을 사용한다. 직격은 별도 피해 없이 원형 폭발로만 처리한다.
public class BomberFallingBomb : ProjectileBase
{
    [SerializeField] private Transform visualRoot;
    [SerializeField] private ParticleSystem fuseFlame;
    [SerializeField] private float gravity = 28f;
    [SerializeField] private float maxFallSpeed = 22f;
    [SerializeField] private float lifeTime = 5f;
    [SerializeField] private float explosionRadius = 1.25f;
    [SerializeField] private FeedbackDataSO explosionFeedback;
    private readonly List<Collider2D> blastHits = new(16);
    private readonly HashSet<HealthBase> damaged = new();
    private ContactFilter2D playerFilter;
    private bool initialized;
    private float age;
    public bool Exploded { get; private set; }
    public float ExplosionRadius => explosionRadius;
    public Vector2 CurrentVelocity => velocity;
    public Vector3 ExplosionPosition { get; private set; }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        initialized = false; Exploded = false; age = 0; velocity = Vector2.zero;
        StopFuse();
        if (visualRoot != null) visualRoot.localRotation = Quaternion.identity;
    }
    public override void Initialize(CharacterController2D owner, WeaponBaseSO.DamageInfo damage, float speed, int count, LayerMask collideLayer)
    {
        base.Initialize(owner, damage, 0, count, collideLayer);
        velocity = Vector2.zero; age = 0; Exploded = false;
        playerFilter.SetLayerMask(LayerMask.GetMask("Player")); playerFilter.useTriggers = true;
        initialized = true;
        if (fuseFlame != null) fuseFlame.Play(true);
    }
    protected override void FixedUpdate()
    {
        if (!initialized || isReturning) return;
        age += Time.fixedDeltaTime;
        if (age >= lifeTime) { ReturnToPool(); return; }
        velocity.x = 0; velocity.y = Mathf.Max(velocity.y - gravity * Time.fixedDeltaTime, -maxFallSpeed);
        base.FixedUpdate();
    }
    protected override void HandleCollision(RaycastCollisionInfo2D info)
    {
        if (!initialized || isReturning || Exploded) return;
        if (info.down)
        {
            CheckMovementHits();
            if (!isReturning) Explode();
        }
    }
    protected override void HitTarget(HealthBase health, Vector2 hitPoint) => Explode();
    private void Explode()
    {
        if (Exploded || isReturning) return;
        Exploded = true; ExplosionPosition = transform.position;
        damaged.Clear(); motor.OverlapAttackCircle(ExplosionPosition, explosionRadius, playerFilter, blastHits);
        foreach (var hit in blastHits)
        {
            var health = hit.GetComponentInParent<PlayerHealth>();
            if (health != null && damaged.Add(health)) health.TakeDamage(damageInfo, ExplosionPosition);
        }
        if (FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.PlayExact(explosionFeedback, ExplosionPosition, Quaternion.identity);
        ReturnToPool();
    }
    protected override void ReturnToPool()
    {
        if (isReturning) return;
        initialized = false; velocity = Vector2.zero; StopFuse(); base.ReturnToPool();
    }
    private void LateUpdate()
    {
        if (!initialized || isReturning) return;
        if (visualRoot != null) visualRoot.localRotation = Quaternion.Euler(0, 0, age * 30f);
    }
    private void StopFuse() { if (fuseFlame != null) fuseFlame.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
    private void OnDisable() { initialized = false; StopFuse(); }
}
