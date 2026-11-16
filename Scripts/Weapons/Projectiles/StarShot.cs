using UnityEngine;

public class StarShot : ProjectileBase
{
    [SerializeField] private FeedbackDataSO emptyHitFeedbackData;
    [SerializeField] private float lifeTime = 0.3f;

    // 날아가는 동안 별 모양만 돌린다. 보이는 부분(자식)만 돌리므로 판정 원·이동과는 무관하다.
    // 진행 방향으로 굴러가듯 오른쪽으로 날면 시계 방향, 왼쪽으로 날면 반시계 방향으로 돈다.
    [Header("Spin Visual")]
    [SerializeField] private Transform spinVisual;
    [SerializeField] private float spinSpeed = 900f;

    private float currentLifeTime = 0f;

    private bool hasCustomDirection;
    private Vector2 customDirection;
    private bool hasMuzzlePosition;
    private Vector3 muzzlePosition;
    public Vector2 SpawnPosition { get; private set; }
    public float LifeTime => lifeTime;
    public void SetMuzzlePosition(Vector3 position) { hasMuzzlePosition = true; muzzlePosition = position; }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        hasCustomDirection = false;
        hasMuzzlePosition = false;
        customDirection = Vector2.zero;

        if (spinVisual != null)
            spinVisual.localRotation = Quaternion.identity;
    }

    private void Update()
    {
        if (spinVisual == null || isReturning)
            return;

        float direction = velocity.x < -0.01f ? 1f : -1f;
        spinVisual.Rotate(0f, 0f, direction * spinSpeed * Time.deltaTime, Space.Self);
    }

    public void SetFireDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        hasCustomDirection = true;
        customDirection = direction.normalized;
    }

    protected override void FixedUpdate()
    {
        if (Time.time > currentLifeTime + lifeTime)
        {
            FeedbackPlayer.Instance.Play(emptyHitFeedbackData, transform.position);
            ReturnToPool();
            return;
        }

        base.FixedUpdate();
    }

    public override void Initialize(
        CharacterController2D owner,
        WeaponBaseSO.DamageInfo damageInfo,
        float speed,
        int count,
        LayerMask collideLayer)
    {
        if (hasMuzzlePosition)
        {
            // 기존 발사 피드백의 위치 보정이 총구에 두 번 더해지지 않게 상쇄한다.
            Vector3 offset = launchFeedbackData != null ? launchFeedbackData.offset : Vector3.zero;
            if (owner != null && owner.Facing < 0) offset.x *= -1f;
            transform.position = muzzlePosition - offset;
        }
        base.Initialize(owner, damageInfo, speed, count, collideLayer);
        SpawnPosition = transform.position;

        Vector2 dir = hasCustomDirection
            ? customDirection
            : Vector2.right * owner.Facing;

        velocity = dir.normalized * speed;
        currentLifeTime = Time.time;
    }
}