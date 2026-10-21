using UnityEngine;

public abstract class DroneBase : MovingEntityBase
{
    [Header("Follow Settings")]
    [SerializeField] protected float followDistance = 1.5f;
    [SerializeField] protected float followHeight = 1.0f;

    [Header("Destroy Feedback")]
    [SerializeField] protected FeedbackDataSO destroyFeedback;

    protected Transform player;
    protected float spawnTime;
    protected float lifeTime;

    public virtual void InitializeDrone(CharacterController2D owner, WeaponBaseSO.DamageInfo damageInfo, float speed, int count, float lifeTime, LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);
        this.player = owner.transform;
        this.lifeTime = lifeTime;
        this.spawnTime = Time.time;
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        if (Time.time - spawnTime > lifeTime)
        {
            FeedbackPlayer.Instance.Play(destroyFeedback, transform.position);
            ForceReturnToPool();
            return;
        }

        DroneTick();
    }

    protected abstract void DroneTick();
}
