using UnityEngine;

public class TargetShot : ProjectileBase
{
    [SerializeField] private FeedbackData emptyHitFeedbackData;
    [SerializeField] private float lifeTime = 0.3f;
    private float currentLifeTime = 0;

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
    public override void Initialize(CharacterController2D owner, WeaponBase.DamageInfo damageInfo, float speed, int count, LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);

        Vector2 desiredDir =
                ((Vector2)target.position - (Vector2)transform.position).normalized;

        Vector2 ownerForward = new Vector2(owner.Facing, 0);

        float signedAngle = Vector2.SignedAngle(ownerForward, desiredDir);
        Vector2 targetDir = (Vector2)(Quaternion.Euler(0f, 0f, signedAngle) * ownerForward).normalized;

        velocity = targetDir * speed;
        currentLifeTime = Time.time;
    }
}
