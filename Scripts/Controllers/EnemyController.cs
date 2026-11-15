using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyController : CharacterController2D
{
    [Header("Physics Setting")]
    public bool GravityOn = false;
    public float Gravity = -30f;

    [Header("Health")]
    public EnemyHealth enemyHealth;

    [Header("Display")]
    [Tooltip("화면에 보이는 표준 이름(보스 등장 이름표·보스 체력바). 비우면 오브젝트 이름을 쓴다.")]
    [SerializeField] private string displayName;
    public string DisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName : name.Replace("(Clone)", string.Empty).Trim();

    public EnemyController Poolkey { get; set; }
    public EnemyHealth Health => enemyHealth;
    public AiBrain Brain => brain;
    [SerializeField, Tooltip("TrainingDummy는 종류와 쉴드 상태에 관계없이 끕니다.")]
    private bool weaponKnockbackEnabled = true;
    public bool CanReceiveWeaponKnockback => weaponKnockbackEnabled && !(enemyHealth is TrainingDummyHealth);
    private AnimatorOverrideController animatorOverride;
    // 패턴 Animation 교체용 캐시. Attack0_Windup·Execute·Recover 원본 Clip을 처음 교체할 때 한 번만 이름으로 찾아 둔다.
    private AnimationClip[] attackOriginals;
    // ApplyOverrides에 넘길 (원본, 새 Clip) 3쌍. 매번 비우고 다시 채워 할당하지 않는다.
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> attackOverrides = new(3);
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
    public void ApplyWeaponKnockback(WeaponBaseSO.DamageInfo info)
    {
        // 쉴드 종류는 소진·브레이크 중에도 일반 피격 넉백을 받지 않는다.
        // 쉴드 브레이크 순간의 기존 전용 외력은 HandleBreak에서 별도로 처리한다.
        if (!CanReceiveWeaponKnockback || enemyHealth == null || enemyHealth.enemyShieldSystem != null ||
            IgnoreExternalForces || !isActiveAndEnabled || info.knockbackFacing == 0 ||
            info.sourceWeapon == null)
            return;

        float power = info.sourceWeapon.knockbackPower;
        if (power <= 0f || float.IsNaN(power) || float.IsInfinity(power))
            return;

        float offset = transform.position.x - info.knockbackOrigin.x;
        float direction = Mathf.Abs(offset) > 0.001f ? Mathf.Sign(offset) : info.knockbackFacing;
        AddKnockback(Vector2.right * (direction * power));
    }

    private void HandleBreak()
    {
        // 즉시 멈춤
        Velocity = Vector2.zero;
        externalForce = Vector2.zero;
        CanTouchDamageActive(false);

        // Dummy는 무장 피격과 쉴드 소진 모두 위치를 밀지 않는다. 브레이크 상태 전환은 유지한다.
        if (!CanReceiveWeaponKnockback) return;

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
        // 예전에는 인덱서(이름)로 3번 넣어 넣을 때마다 다시 바인딩(3회, 비용은 Transform 수에 비례)했다.
        // 같은 원본 Clip에 같은 순서·같은 Clip을 ApplyOverrides로 한 번에 넣어 다시 바인딩을 1회로 줄인다(최종 Override 결과는 같다).
        // 같은 Clip이어도 건너뛰지 않고 늘 다시 바인딩한다. 예전 세 번의 바인딩 사이에는 Animator 평가가 없었으므로
        // 패턴 시작 순간의 자세를 기본값으로 잡는 결과도 예전과 같다.
        if (attackOriginals == null)
            attackOriginals = FindAttackOriginals();
        attackOverrides.Clear();
        AddAttackOverride(attackOriginals[0], set.windup);
        AddAttackOverride(attackOriginals[1], set.execute);
        AddAttackOverride(attackOriginals[2], set.recover);
        // 이름이 맞는 원본이 없으면 인덱서도 바꾸지 않았으므로, 넣을 것이 없을 때는 다시 바인딩하지 않는다.
        if (attackOverrides.Count > 0)
            animatorOverride.ApplyOverrides(attackOverrides);
    }
    private AnimationClip[] FindAttackOriginals()
    {
        var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        animatorOverride.GetOverrides(clips);
        return new[] { FindOriginalClip(clips, "Attack0_Windup"), FindOriginalClip(clips, "Attack0_Execute"), FindOriginalClip(clips, "Attack0_Recover") };
    }
    // 인덱서가 이름으로 찾던 원본 Clip(EnemyBase.controller 안에서 원본 이름은 겹치지 않는다).
    private static AnimationClip FindOriginalClip(List<KeyValuePair<AnimationClip, AnimationClip>> clips, string name)
    {
        foreach (var pair in clips)
            if (pair.Key != null && pair.Key.name == name)
                return pair.Key;
        return null;
    }
    private void AddAttackOverride(AnimationClip original, AnimationClip clip)
    {
        if (original != null)
            attackOverrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(original, clip));
    }
    public void RequestTouchDamage(PlayerHealth player, float dmg)
    {
        // Break 상태일 경우 접촉 데미지 무효화
        if (!canTouchDamage)
            return;

        dmg = ResolveContactDamage(dmg);
        if (dmg <= 0f) return;
        damageInfo.damageType = DamageType.Normal;
        damageInfo.damage = dmg;

        player.TakeDamage(damageInfo, GameManager.Instance.Player.transform.position);
    }
    /// <summary>실행 중 패턴의 접촉 피해 설정을 적용한다. 비활성 설정은 기존 부위별 피해를 유지한다.</summary>
    public float ResolveContactDamage(float defaultDamage)
    {
        var pattern = brain != null ? brain.current : null;
        var data = pattern != null && pattern.IsRunning ? pattern.Data : null;
        return data != null && data.overrideContactDamage ? Mathf.Max(0f, data.contactDamage) : defaultDamage;
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
        motor.ResetOneWayState();
        spawnController.ResetSpawnSequence();
        if (enemyHealth is TrainingDummyHealth trainingDummy)
        {
            // 비키 관절도 Prefab 기준으로 돌린 뒤 새 등장 상태를 시작한다.
            trainingDummy.RestoreSpawnPose();
            animator.Rebind();
            animator.Update(0f);
        }
        EnterFreeze();
        brain.ResetForSpawn();

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
