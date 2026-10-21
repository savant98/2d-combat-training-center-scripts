using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEngine.UI.Image;

public class LandMine : ProjectileBase
{
    [Header("Physics")]
    [SerializeField] private int angleStep = 15;
    [SerializeField] private float gravity = -10f;

    [Header("Mine Timings")]
    [SerializeField] private float armDelay = 2f;
    [SerializeField] private float triggerDelay = 0.5f;
    [SerializeField] private float lifeTimeAfterArmed = 10f;

    [Header("Explosion")]
    [SerializeField] private Vector2 offSet = Vector2.zero;
    [SerializeField] private float explosionRadius = 2.5f;
    [SerializeField] private LayerMask targetLayer;
    [SerializeField] private SoundOptions explosionWarningSound;
    [SerializeField] private FeedbackData explodeFeedback;

    private MineState state;
    private Coroutine stateRoutine;
    private Animator animator;
    private SoundHandle warningLoopHandle;
    protected override void Awake()
    {
        base.Awake();
        animator = GetComponent<Animator>();
    }
    private enum MineState
    {
        Flying, // 공중
        Arming, // 바닥 도착 후 활성화 대기
        Armed, // 활성화 됨
        Triggered, // 플레이어 감지 -> 폭발 대기
        Exploded
    }
    protected override void FixedUpdate()
    {
        if (state != MineState.Flying)
            return;

        base.FixedUpdate();
        velocity.y += gravity * Time.fixedDeltaTime;
    }
    protected override void HandleCollision(RaycastCollisionInfo2D info)
    {
        if (state != MineState.Flying)
            return;

        // 바닥
        if (info.down)
        {
            velocity = Vector2.zero;
            state = MineState.Arming;
            animator.SetInteger("LandMineSequence", 1);
            StartCoroutine(ArmRoutine());
            return;
        }

        // 벽 반사
        if (info.left || info.right)
        {
            velocity.x *= -1f;
        }
    }
    public override void Initialize(CharacterController2D owner, WeaponBase.DamageInfo damageInfo, float speed, int count, LayerMask collideLayer)
    {
        base.Initialize(owner, damageInfo, speed, count, collideLayer);

        float angle = angleStep * count;
        velocity = AngleToDirection(angle) * speed;

        state = MineState.Flying;
        animator.SetTrigger("Fly");
    }
    private Vector2 AngleToDirection(float angle)
    {
        float rad = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Abs(Mathf.Sin(rad)));
    }
    protected override void OnTriggerEnter2D(Collider2D collision)
    {
        if (state != MineState.Armed)
            return;

        //if (!collision.TryGetComponent<HealthBase>(out var health))
        //    return;
        HealthBase health = collision.GetComponentInParent<HealthBase>();
        if (health == null) 
            return;

        if (ownerFaction == Faction.Player && health is EnemyHealth enemy)
        {
            TriggerExplosion();
        }
        else if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
        {
            TriggerExplosion();
        }
    }

    private IEnumerator ArmRoutine()
    {
        yield return new WaitForSeconds(armDelay);

        state = MineState.Armed;
        animator.SetInteger("LandMineSequence", 2);
        // 활성화 이펙트
        // armedFeedback

        if (IsTargetOnMine())
        {
            yield return new WaitForFixedUpdate();
            TriggerExplosion();

            yield break;
        }

        // 수명 제한
        yield return new WaitForSeconds(lifeTimeAfterArmed);

        TriggerExplosion();
    }

    private void TriggerExplosion()
    {
        if (state != MineState.Armed)
            return;

        state = MineState.Triggered;
        animator.SetInteger("LandMineSequence", 3);

        if (stateRoutine != null)
            StopCoroutine(stateRoutine);

        stateRoutine = StartCoroutine(TriggerRoutine());
    }
    private void StartWarningLoopSound()
    {
        StopWarningLoopSound();

        if (explosionWarningSound == null || explosionWarningSound.clip == null)
            return;

        if (SoundManager.Instance == null)
            return;

        warningLoopHandle = SoundManager.Instance.PlayLoop(
            explosionWarningSound,
            transform.position,
            null);
    }

    private void StopWarningLoopSound()
    {
        warningLoopHandle?.Stop();
        warningLoopHandle = null;
    }

    private IEnumerator TriggerRoutine()
    {
        StartWarningLoopSound();

        yield return new WaitForSeconds(triggerDelay);
        StopWarningLoopSound();

        Explode();
    }

    private void Explode()
    {
        if (state == MineState.Exploded)
            return;

        state = MineState.Exploded;

        Vector2 explodePos = new Vector2(transform.position.x + offSet.x, transform.position.y + offSet.y);

        // 데미지 처리
        Collider2D[] hits = Physics2D.OverlapCircleAll(explodePos, explosionRadius, targetLayer);
        HashSet<HealthBase> hitTargets = new HashSet<HealthBase>();

        foreach (var hit in hits)
        {
            //if (!hit.TryGetComponent<HealthBase>(out var health))
            //    continue;
            HealthBase health = hit.GetComponentInParent<HealthBase>();

            if (health == null || !hitTargets.Add(health))
                continue;

            if (ownerFaction == Faction.Player && health is EnemyHealth enemy)
            {
                if (enemy.enemyShieldSystem == null)
                    enemy.TakeDamage(damageInfo, transform.position);
                else
                    enemy.enemyShieldSystem.TakeDamage(damageInfo, transform.position);

                Vector2 hitPoint = hit.ClosestPoint(transform.position);
                OnHitEnemy?.Invoke(owner, hitPoint);
            }
            else if (ownerFaction == Faction.Enemy && health is PlayerHealth player)
            {
                player.TakeDamage(damageInfo, transform.position);
            }
        }

        FeedbackPlayer.Instance.Play(explodeFeedback, transform.position);
        ReturnToPool();
    }
    private bool IsTargetOnMine()
    {
        Collider2D hit = Physics2D.OverlapCircle(
            new Vector2(transform.position.x + offSet.x, transform.position.y + offSet.y),
            0.5f,              // 지뢰 크기에 맞게 조절
            targetLayer);

        return hit != null;
    }

    protected override void ReturnToPool()
    {
        StopAllCoroutines();
        stateRoutine = null;

        base.ReturnToPool();
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(new Vector2(transform.position.x + offSet.x, transform.position.y + offSet.y), explosionRadius);
    }
#endif
}
