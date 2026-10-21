using System.Collections.Generic;
using UnityEngine;

public class SpinningHammerShot : ProjectileBase
{
    [Header("Lifetime")]
    [SerializeField] private FeedbackData emptyHitFeedbackData;
    [SerializeField, Min(0f)] private float lifeTime = 1.2f;

    [Header("Visual Rotation")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private float rotateSpeed = 720f;

    [Header("Swing Sound")]
    [SerializeField] private SoundOptions swingSound;
    [SerializeField, Min(1f)] private float degreesPerSwingSound = 180f;
    [SerializeField] private bool playSwingSoundWhenStopped = true;

    [Header("Multi Hit Damage")]
    [SerializeField, Min(0.01f)] private float tickInterval = 0.08f;

    [Header("Slowdown On Hit")]
    [SerializeField] private bool slowDownOnlyOnce = true;
    [SerializeField, Range(0.01f, 1f)] private float hitSpeedMultiplier = 0.25f;
    [SerializeField, Min(0f)] private float minSpeedAfterHit = 1.5f;

    private float spawnTime;
    private float currentSpeed;
    private float accumulatedSwingDegrees;

    private bool hasCustomDirection;
    private Vector2 customDirection;
    private Vector2 moveDirection;

    private bool hasSlowedDown;
    private bool stoppedByWall;

    private SoundHandle flyingLoopHandle;

    private readonly Dictionary<HealthBase, float> nextDamageTimes = new();

    protected override void Awake()
    {
        base.Awake();

        // SpinningHammerShot은 Trail을 쓰지 않는 구조로 보이므로
        // ProjectileBase의 Trail 처리에서 제외한다.
        trail = null;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        spawnTime = Time.time;
        currentSpeed = 0f;

        hasCustomDirection = false;
        customDirection = Vector2.zero;
        moveDirection = Vector2.right;

        hasSlowedDown = false;
        stoppedByWall = false;

        accumulatedSwingDegrees = 0f;

        nextDamageTimes.Clear();

        if (visualRoot != null)
            visualRoot.localRotation = Quaternion.identity;
    }

    public void SetFireDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        hasCustomDirection = true;
        customDirection = direction.normalized;
    }

    public override void Initialize(
        CharacterController2D owner,
        WeaponBase.DamageInfo damageInfo,
        float speed,
        int count,
        LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);

        moveDirection = hasCustomDirection
            ? customDirection
            : Vector2.right * owner.Facing;

        moveDirection.Normalize();

        currentSpeed = speed;
        velocity = moveDirection * currentSpeed;

        spawnTime = Time.time;

        hasSlowedDown = false;
        stoppedByWall = false;

        nextDamageTimes.Clear();
    }

    private void Update()
    {
        RotateVisual();
    }

    protected override void FixedUpdate()
    {
        if (Time.time >= spawnTime + lifeTime)
        {
            if (emptyHitFeedbackData != null)
                FeedbackPlayer.Instance.Play(emptyHitFeedbackData, transform.position);

            ReturnToPool();
            return;
        }

        velocity = moveDirection * currentSpeed;

        base.FixedUpdate();
    }

    /// <summary>
    /// 벽이나 지형에 닿았을 때 ProjectileBase처럼 파괴하지 않고,
    /// 현재 위치에서 멈춘다.
    /// </summary>
    protected override void OnHitSomething()
    {
        if (stoppedByWall)
            return;

        stoppedByWall = true;

        currentSpeed = 0f;
        velocity = Vector2.zero;

        if (hitFeedbackData != null)
            FeedbackPlayer.Instance.Play(hitFeedbackData, transform.position);
    }

    private void RotateVisual()
    {
        if (visualRoot == null)
            return;

        float signedRotateSpeed = rotateSpeed;

        if (moveDirection.x < -0.01f)
            signedRotateSpeed *= -1f;

        float deltaDegrees = signedRotateSpeed * Time.deltaTime;

        visualRoot.Rotate(0f, 0f, deltaDegrees, Space.Self);

        UpdateSwingSound(Mathf.Abs(deltaDegrees));
    }
    private void UpdateSwingSound(float deltaDegrees)
    {
        if (swingSound == null || swingSound.clip == null)
            return;

        if (SoundManager.Instance == null)
            return;

        if (!playSwingSoundWhenStopped && currentSpeed <= 0.01f)
            return;

        accumulatedSwingDegrees += deltaDegrees;

        while (accumulatedSwingDegrees >= degreesPerSwingSound)
        {
            accumulatedSwingDegrees -= degreesPerSwingSound;

            SoundManager.Instance.PlaySFX(
                swingSound,
                transform.position);
        }
    }
    protected override void OnTriggerEnter2D(Collider2D collision)
    {
        TryDamage(collision);
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        TryDamage(collision);
    }

    private void TryDamage(Collider2D collision)
    {
        HealthBase health = GetHealth(collision);

        if (health == null)
            return;

        if (health.IsInvincible)
            return;

        float now = Time.time;

        if (nextDamageTimes.TryGetValue(health, out float nextTime))
        {
            if (now < nextTime)
                return;
        }

        if (!CanDamageTarget(health))
            return;

        Vector2 hitPoint = collision.ClosestPoint(transform.position);

        ApplyDamage(health, hitPoint);

        nextDamageTimes[health] = now + tickInterval;

        ApplyHitSlowdown();

        if (hitFeedbackData != null)
            FeedbackPlayer.Instance.Play(hitFeedbackData, hitPoint);
    }

    private HealthBase GetHealth(Collider2D collision)
    {
        HealthBase health = collision.GetComponentInParent<HealthBase>();

        if (health != null)
            return health;
        //if (collision.TryGetComponent<HealthBase>(out var health))
        //    return health;

        return null;
    }

    private bool CanDamageTarget(HealthBase health)
    {
        if (ownerFaction == Faction.Player && health is EnemyHealth)
            return true;

        if (ownerFaction == Faction.Enemy && health is PlayerHealth)
            return true;

        return false;
    }

    private void ApplyDamage(HealthBase health, Vector2 hitPoint)
    {
        if (ownerFaction == Faction.Player && health is EnemyHealth enemy)
        {
            if (enemy.enemyShieldSystem == null)
                enemy.TakeDamage(damageInfo, hitPoint);
            else
                enemy.enemyShieldSystem.TakeDamage(damageInfo, hitPoint);

            OnHitEnemy?.Invoke(owner, hitPoint);
        }
        else if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
        {
            player.TakeDamage(damageInfo, hitPoint);
        }
    }

    private void ApplyHitSlowdown()
    {
        if (slowDownOnlyOnce && hasSlowedDown)
            return;

        currentSpeed *= hitSpeedMultiplier;
        currentSpeed = Mathf.Max(currentSpeed, minSpeedAfterHit);

        hasSlowedDown = true;
    }
}