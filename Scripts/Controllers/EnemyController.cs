using System;
using System.Collections;
using UnityEngine;

public class EnemyController : CharacterController2D
{
    [Header("Physics Setting")]
    public bool GravityOn = false;
    public float Gravity = -30f;

    [Header("Health")]
    public EnemyHealth enemyHealth;

    public EnemyController Poolkey { get; set; }
    public EnemyHealth Health => enemyHealth;
    public AiBrain Brain => brain;
    private AnimatorOverrideController animatorOverride;
    private bool canTouchDamage = true;
    private EnemyShieldSystem shieldSystem;
    private EnemySpawnController spawnController;
    private EnemyTeleporter teleporter;
    private WeaponBaseSO.DamageInfo damageInfo;
    private AiBrain brain;
    private Action waveDeathHandler;
    private float initialGravity = 0f;

    protected override void Awake()
    {
        base.Awake();
        initialGravity = Gravity;
        brain = GetComponent<AiBrain>();
        spawnController = GetComponent<EnemySpawnController>();
        teleporter = GetComponent<EnemyTeleporter>();

        if (overrideAnimator != null)
        {
            animatorOverride = Instantiate(overrideAnimator);
            animator.runtimeAnimatorController = animatorOverride;
        }

        if (enemyHealth == null)
            return;

        // Shield 없다면 취소
        if (enemyHealth.enemyShieldSystem == null)
            return;

        shieldSystem = enemyHealth.enemyShieldSystem;
        shieldSystem.OnBreak += HandleBreak;
        shieldSystem.OnBreakEnd += HandleBreakEnd;
    }
    protected override Vector2 CalculateVelocity()
    {
        Velocity.y += GravityOn ? Gravity * Time.deltaTime : 0;
        return Velocity;
    }
    private void HandleBreak()
    {
        // 즉시 멈춤
        Velocity = Vector2.zero;
        externalForce = Vector2.zero;
        CanTouchDamageActive(false);

        Vector2 force = knockbackForce;
        force.x *= GameManager.Instance.Player.transform.position.x - transform.position.x < 0 ? -1f : 1f;

        Gravity = -55f;

        AddKnockback(force);
    }
    private void HandleBreakEnd()
    {
        // 브레이크 종료시 호출
        Gravity = initialGravity;
        CanTouchDamageActive(true);
    }
    public void ApplyAttackAnimation(AnimationSetSO set)
    {
        animatorOverride["Attack0_Windup"] = set.windup;
        animatorOverride["Attack0_Execute"] = set.execute;
        animatorOverride["Attack0_Recover"] = set.recover;
    }
    public void RequestTouchDamage(PlayerHealth player, float dmg)
    {
        // Break 상태일 경우 접촉 데미지 무효화
        if (!canTouchDamage)
            return;

        damageInfo.damageType = DamageType.Normal;
        damageInfo.damage = dmg;

        player.TakeDamage(damageInfo, GameManager.Instance.Player.transform.position);
    }
    public void FacingUpdate(int value)
    {
        Facing = value;
        facingModel.transform.localScale = new Vector3(Facing, 1, 1);
    }

    public void CanTouchDamageActive(bool value)
    {
        canTouchDamage = value;
    }
    public void EnterFreeze()
    {
        // 위치, Velocity, 애니메이션 리셋
        brain.PauseBrain();

        if (shieldSystem != null)
            shieldSystem.IsSpawnLocked = true;

        //animator.speed = 0f;
        animator.SetTrigger("Spawn");
        Velocity = Vector2.zero;

        // 중력 끄기
        Gravity = 0f;

        ColliderActive(false);
        CanTouchDamageActive(false);
        enemyHealth.IsInvincible = true;
    }
    public void ExitFreeze()
    {
        animator.speed = 1f;
        animator.SetTrigger("Idle");
        // Spawn Animation이 끝나고나면 무적 해제, AiBrain 동작, 몸통 데미지 활성화
        brain.ResumeBrain();

        if (shieldSystem != null)
            shieldSystem.IsSpawnLocked = false;

        // 물리 복구
        model.SetActive(true);
        Gravity = initialGravity;

        ColliderActive(true);
        CanTouchDamageActive(true);
        enemyHealth.IsInvincible = false;
    }
    public IEnumerator OnSpawn()
    {
        spawnController.ResetSpawnSequence();
        if (enemyHealth is TrainingDummyHealth trainingDummy)
        {
            // 비키 관절도 Prefab 기준으로 돌린 뒤 새 등장 상태를 시작한다.
            trainingDummy.RestoreSpawnPose();
            animator.Rebind();
            animator.Update(0f);
        }
        EnterFreeze();

        enemyHealth.ResetHealth();
        if (shieldSystem != null)
            shieldSystem.ResetShield();

        if (teleporter != null)
            teleporter.Cancel();

        yield return spawnController.PlaySpawnSequence();

        ExitFreeze();
    }
    public void OnDespawn()
    {
        ClearWaveDeathHandler();
        brain.PauseBrain();
        spawnController.ResetSpawnSequence();
        if (enemyHealth is TrainingDummyHealth trainingDummy)
            trainingDummy.RestoreSpawnPose();
    }
    public void SetWaveDeathHandler(Action handler)
    {
        // 기존 핸들러 제거
        if (waveDeathHandler != null)
            Health.OnDeath -= handler;

        waveDeathHandler = handler;
        Health.OnDeath += waveDeathHandler;
    }
    public void ClearWaveDeathHandler()
    {
        if (waveDeathHandler == null)
            return;

        Health.OnDeath -= waveDeathHandler;
        waveDeathHandler = null;
    }
}
