using System.Collections.Generic;
using UnityEngine;

public class SpinningHammerShot : ProjectileBase
{
    [Header("Lifetime")]
    [SerializeField] private FeedbackDataSO emptyHitFeedbackData;
    [SerializeField, Min(0f)] private float lifeTime = 1.2f;

    [Header("Visual Rotation")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private float rotateSpeed = 720f;

    [Header("Spin Trail")]
    // 회전 궤적(HammerSwingArc 셰이더). Prefab의 기본 회전 방향(rotateSpeed 부호)에 맞춰 그렸고,
    // 왼쪽으로 날아가 반대로 돌 때는 해머 머리 축을 기준으로 좌우를 뒤집어 꼬리가 항상 머리 뒤에 오게 한다.
    [SerializeField] private Transform spinTrail;

    [Header("Swing Sound")]
    [SerializeField] private SoundOptions swingSound;
    [SerializeField, Min(1f)] private float degreesPerSwingSound = 180f;
    [SerializeField] private bool playSwingSoundWhenStopped = true;

    [Header("Multi Hit Damage")]
    [SerializeField, Min(0.01f)] private float tickInterval = 0.08f;

    [Header("Slowdown On Hit")]
    // 타격할 때마다 slowDuration 동안만 감속하고, 그 뒤에는 원래 속도로 돌아간다.
    // 붙어 있는 적은 tickInterval마다 다시 맞아 감속이 이어지고, 판정 중 적이 파괴되면 곧바로 원래 속도로 이어서 날아간다.
    [SerializeField, Range(0.01f, 1f)] private float hitSpeedMultiplier = 0.25f;
    [SerializeField, Min(0f)] private float minSpeedAfterHit = 1.5f;
    [SerializeField, Min(0f)] private float slowDuration = 0.1f;

    private float spawnTime;
    private float baseSpeed;
    private float currentSpeed;
    private float slowUntil;
    private float accumulatedSwingDegrees;

    private bool hasCustomDirection;
    private Vector2 customDirection;
    private Vector2 moveDirection;

    private bool wasBlocked;

    private SoundHandle flyingLoopHandle;

    private readonly Dictionary<HealthBase, float> nextDamageTimes = new(8);

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
        baseSpeed = 0f;
        currentSpeed = 0f;
        slowUntil = -1f;

        hasCustomDirection = false;
        customDirection = Vector2.zero;
        moveDirection = Vector2.right;

        wasBlocked = false;

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
        WeaponBaseSO.DamageInfo damageInfo,
        float speed,
        int count,
        LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);

        moveDirection = hasCustomDirection
            ? customDirection
            : Vector2.right * owner.Facing;

        moveDirection.Normalize();
        UpdateSpinTrailSide();

        baseSpeed = speed;
        currentSpeed = speed;
        slowUntil = -1f;
        velocity = moveDirection * currentSpeed;

        spawnTime = Time.time;

        wasBlocked = false;

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

        currentSpeed = IsSlowed() ? GetSlowSpeed() : baseSpeed;

        velocity = moveDirection * currentSpeed;

        base.FixedUpdate();
    }

    // 마지막 타격 뒤 slowDuration 동안 감속. 물리 스텝 오차(반 스텝)를 더해 다음 틱 타격과 맞닿아도 한 스텝씩 원래 속도로 튀지 않게 한다.
    private bool IsSlowed()
    {
        return Time.time < slowUntil + Time.fixedDeltaTime * 0.5f;
    }

    private float GetSlowSpeed()
    {
        return Mathf.Min(baseSpeed, Mathf.Max(baseSpeed * hitSpeedMultiplier, minSpeedAfterHit));
    }

    /// <summary>
    /// 벽·지형에 닿아도 멈추거나 사라지지 않는다. 진행 방향이 막히면 모터가 그 자리에서 막아(앞으로 못 감) 붙어 있고,
    /// 길이 열려 있으면 계속 날아간다. 예전에는 어느 방향이든 닿으면 그 자리에 영구히 멈췄는데, 수평 이동에도 모터가 쏘는
    /// 발밑 확인 레이가 판정 원을 감싼 사각형의 아래 모서리에서 시작해 1.8칸 안의 벽(뒤쪽 포함)을 '바닥'으로 잡아,
    /// 벽 근처에서 쓰면 앞으로 가지 못하고 멈춰 있었다. 진행 방향이 막힌 순간에만 벽 충돌 효과를 한 번 재생한다.
    /// </summary>
    protected override void HandleCollision(RaycastCollisionInfo2D info)
    {
        bool blocked = (moveDirection.x > 0.01f && info.right) || (moveDirection.x < -0.01f && info.left)
                       || (moveDirection.y > 0.01f && info.up) || (moveDirection.y < -0.01f && info.down);

        if (blocked && !wasBlocked && hitFeedbackData != null)
            FeedbackPlayer.Instance.Play(hitFeedbackData, GetFrontPoint());

        wasBlocked = blocked;
    }

    // 해머 판정 원의 진행 방향 앞쪽 끝(벽에 닿는 곳)
    private Vector3 GetFrontPoint()
    {
        Bounds bounds = col.bounds;
        return bounds.center + new Vector3(moveDirection.x * bounds.extents.x, moveDirection.y * bounds.extents.y, 0f);
    }

    private void UpdateSpinTrailSide()
    {
        if (spinTrail == null)
            return;

        // RotateVisual과 같은 조건(왼쪽 이동)에서 회전 방향이 바뀌므로 궤적도 같은 조건으로 뒤집는다.
        Vector3 scale = spinTrail.localScale;
        scale.x = Mathf.Abs(scale.x) * (moveDirection.x < -0.01f ? -1f : 1f);
        spinTrail.localScale = scale;
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
    private void OnTriggerStay2D(Collider2D collision)
    {
        base.OnTriggerEnter2D(collision);
    }

    protected override void HitTarget(HealthBase health, Vector2 hitPoint)
    {
        float now = Time.time;
        if (nextDamageTimes.TryGetValue(health, out float nextTime) && now < nextTime)
            return;

        nextDamageTimes[health] = now + tickInterval;
        ApplyHitDamage(health, hitPoint);
        ApplyHitSlowdown();
    }

    private void ApplyHitSlowdown()
    {
        slowUntil = Time.time + slowDuration;
        currentSpeed = GetSlowSpeed();
    }
}