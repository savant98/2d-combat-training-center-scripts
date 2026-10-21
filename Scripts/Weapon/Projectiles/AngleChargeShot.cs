using System.Collections.Generic;
using UnityEngine;

public class AngleChargeShot : ProjectileBase
{
    private enum ShotState
    {
        Preparing,
        Fired
    }

    [Header("Timing")]
    [SerializeField] private float prepareTime = 0.6f;
    [SerializeField] private float lifeTimeAfterFire = 3f;

    [Header("Aim")]
    [SerializeField, Range(0f, 89f)] private float angleFromVertical = 30f;
    [SerializeField] private bool useOwnerFacing = true;

    [Header("Preview")]
    [SerializeField] private LaserPreviewLine previewLinePrefab;
    [SerializeField] private float previewLineLength = 12f;
    [SerializeField] private float previewLineWidth = 0.2f;
    [SerializeField] private Color previewLineColor = Color.red;
    [SerializeField] private LayerMask previewBlockMask;

    [Header("Optional")]
    [SerializeField] private Animator animator;
    [SerializeField] private string prepareTriggerName = "Prepare";
    [SerializeField] private string fireTriggerName = "Fire";
    [SerializeField] private FeedbackData emptyHitFeedbackData;

    private ShotState state;
    private float stateStartTime;
    private Vector2 fireDirection;
    private LaserPreviewLine activePreview;

    private bool hasExplicitAngle;
    private float explicitAngleFromDown;

    // 관통하면서 같은 대상을 여러 번 때리지 않도록
    private readonly HashSet<HealthBase> hitSet = new();

    public void SetAngleFromVertical(float angle)
    {
        angleFromVertical = angle;
        hasExplicitAngle = false;
    }
    public void SetAngle(float angleFromDown)
    {
        explicitAngleFromDown = angleFromDown;
        hasExplicitAngle = true;
    }

    public void SetUseOwnerFacing(bool value)
    {
        useOwnerFacing = value;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        state = ShotState.Preparing;
        stateStartTime = Time.time;
        velocity = Vector2.zero;
        fireDirection = Vector2.zero;
        hitSet.Clear();

        hasExplicitAngle = false;
        explicitAngleFromDown = 0f;

        ReturnPreview();
    }

    public override void Initialize(
        CharacterController2D owner,
        WeaponBase.DamageInfo damageInfo,
        float speed,
        int count,
        LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);

        state = ShotState.Preparing;
        stateStartTime = Time.time;
        velocity = Vector2.zero;
        hitSet.Clear();

        fireDirection = CalculateFireDirection();
        SpawnPreview();

        if (animator != null && !string.IsNullOrEmpty(prepareTriggerName))
            animator.SetTrigger(prepareTriggerName);
    }

    protected override void FixedUpdate()
    {
        if (state == ShotState.Preparing)
        {
            UpdatePreparing();
            return;
        }

        if (Time.time >= stateStartTime + lifeTimeAfterFire)
        {
            if (emptyHitFeedbackData != null)
                FeedbackPlayer.Instance.Play(emptyHitFeedbackData, transform.position);

            ReturnToPool();
            return;
        }

        base.FixedUpdate();
    }

    private void UpdatePreparing()
    {
        velocity = Vector2.zero;

        float angle = Mathf.Atan2(fireDirection.y, fireDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle + 90f);

        UpdatePreview();

        if (Time.time < stateStartTime + prepareTime)
            return;

        Fire();
    }

    private void Fire()
    {
        state = ShotState.Fired;
        stateStartTime = Time.time;
        velocity = fireDirection * speed;

        ReturnPreview();

        if (animator != null && !string.IsNullOrEmpty(fireTriggerName))
            animator.SetTrigger(fireTriggerName);
    }
    private Vector2 CalculateFireDirection()
    {
        Vector2 down = Vector2.down;

        if (hasExplicitAngle)
        {
            Vector2 dir = Quaternion.Euler(0f, 0f, explicitAngleFromDown) * down;
            return dir.normalized;
        }

        int horizontalSign = 1;

        if (useOwnerFacing && owner != null)
            horizontalSign = owner.Facing >= 0 ? 1 : -1;

        float signedAngle = angleFromVertical * -horizontalSign;
        Vector2 defaultDir = Quaternion.Euler(0f, 0f, signedAngle) * down;

        return defaultDir.normalized;
    }

    private void SpawnPreview()
    {
        if (previewLinePrefab == null)
            return;

        if (activePreview != null)
            return;

        activePreview = WorldEntityPool.Instance.Spawn(
            previewLinePrefab,
            transform.position,
            Quaternion.identity);

        activePreview.Show(
            transform.position,
            fireDirection,
            previewLineLength,
            previewLineWidth,
            previewBlockMask,
            previewLineColor,
            -1f);
    }

    private void UpdatePreview()
    {
        if (activePreview == null)
            return;

        activePreview.SetOrigin(transform.position);
        activePreview.SetDirection(fireDirection);

        float remaining = Mathf.Max(0f, (stateStartTime + prepareTime) - Time.time);
        activePreview.UpdatePreviewVisual(remaining, Mathf.Max(0.0001f, prepareTime));
    }

    private void ReturnPreview()
    {
        if (activePreview == null)
            return;

        activePreview.ForceReturnToPool();
        activePreview = null;
    }

    protected override void OnTriggerEnter2D(Collider2D collision)
    {
        if (state != ShotState.Fired)
            return;

        //if (!collision.TryGetComponent<HealthBase>(out var health))
        //    return;
        HealthBase health = collision.GetComponentInParent<HealthBase>();
        if (health == null) 
            return;

        if (!hitSet.Add(health))
            return;

        if (ownerFaction == Faction.Player && health is EnemyHealth enemy)
        {
            if (enemy.enemyShieldSystem == null)
                enemy.TakeDamage(damageInfo, transform.position);
            else
                enemy.enemyShieldSystem.TakeDamage(damageInfo, transform.position);

            Vector2 hitPoint = collision.ClosestPoint(transform.position);

            OnHitEnemy?.Invoke(owner, hitPoint);

            if (hitFeedbackData != null)
                FeedbackPlayer.Instance.Play(hitFeedbackData, hitPoint);
        }
        else if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
        {
            player.TakeDamage(damageInfo, transform.position);

            if (hitFeedbackData != null)
                FeedbackPlayer.Instance.Play(hitFeedbackData, transform.position);
        }

        // 중요:
        // 여기서는 ReturnToPool() 하지 않음.
        // 공격 대상은 관통하고, 벽 충돌만 MovingEntityBase 쪽에서 처리됨.
    }

    protected override void ReturnToPool()
    {
        //ReturnPreview();
        hitSet.Clear();
        base.ReturnToPool();
    }
}